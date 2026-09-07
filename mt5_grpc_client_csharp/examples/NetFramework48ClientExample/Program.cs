using System;
using System.Threading;
using System.Threading.Tasks;
using Google.Protobuf.WellKnownTypes;
using MetaTrader.Grpc.Client;
using Metatrader.V1;

internal static class Program
{
    // Terminal / login defaults. Override any of them from the command line:
    //   NetFramework48ClientExample.exe <login> <password> <server> [terminalPath]
    private const string DefaultServerAddress = "http://10.27.102.101:50051";
    private const string DefaultLogin         = "833671";
    private const string DefaultPassword      = "6cU!DaDy";
    private const string DefaultServer        = "185.97.161.40";
    private const string DefaultTerminalPath  = @"C:\Program Files\MetaTrader 5\terminal64.exe";

    private static async Task Main(string[] args)
    {
        var login        = args.Length > 0 ? args[0] : DefaultLogin;
        var password     = args.Length > 1 ? args[1] : DefaultPassword;
        var server       = args.Length > 2 ? args[2] : DefaultServer;
        var terminalPath = args.Length > 3 ? args[3] : DefaultTerminalPath;

        var options = new Mt5GrpcClientOptions
        {
            Address = new Uri(DefaultServerAddress),
        };

        // CreateCore builds a native Grpc.Core channel. Unlike the GrpcChannel +
        // WinHttpHandler path, it runs on .NET Framework 4.8 / Windows 10, which
        // otherwise throws "the current version of Windows doesn't support HTTP/2
        // features required by gRPC".
        using (var client = Mt5GrpcClientFactory.CreateCore(options))
        {
            // Log in to the MT5 terminal with the supplied credentials. Every field
            // is optional; only set the ones you provide.
            var connectRequest = new ConnectRequest();

            if (!string.IsNullOrWhiteSpace(terminalPath))
            {
                connectRequest.Path = terminalPath;
            }

            if (long.TryParse(login, out var loginId))
            {
                connectRequest.Login = loginId;
            }

            if (!string.IsNullOrWhiteSpace(password))
            {
                connectRequest.Password = password;
            }

            if (!string.IsNullOrWhiteSpace(server))
            {
                connectRequest.Server = server;
            }

            var connect = await client.ConnectAsync(
                connectRequest,
                deadline: DateTime.UtcNow.AddSeconds(30),
                cancellationToken: CancellationToken.None);

            if (!connect.IsSuccess)
            {
                Console.WriteLine($"connect failed: {connect.Error!.Operation}: {connect.Error.Message}");
                return;
            }

            Console.WriteLine($"connected as {login}");

            var account = await client.GetAccountInfoAsync(
                deadline: DateTime.UtcNow.AddSeconds(5),
                cancellationToken: CancellationToken.None);

            if (!account.IsSuccess)
            {
                Console.WriteLine($"{account.Error!.Operation}: {account.Error.Message}");
                return;
            }

            Console.WriteLine(account.Value!.AccountInfo.Login);

            // The request enum types (0.2.0+) compile and behave identically on the
            // .NET Framework 4.8 / netstandard2.0 target (FR-011, SC-007).
            var send = await client.SendOrderAsync(new OrderSendRequest
            {
                TradeRequest = new TradeRequest
                {
                    Symbol      = "EURUSD",
                    Volume      = 0.01,
                    Action      = ENUM_TRADE_REQUEST_ACTIONS.TradeActionDeal,
                    Type        = ENUM_ORDER_TYPE.OrderTypeBuy,
                    TypeFilling = ENUM_ORDER_TYPE_FILLING.OrderFillingIoc,
                    TypeTime    = ENUM_ORDER_TYPE_TIME.OrderTimeGtc,
                }
            });

            Console.WriteLine(send.IsSuccess
                ? send.Value!.TradeResult?.Retcode.ToString()
                : $"{send.Error!.Operation}: {send.Error.Message}");

            // Deal history over the native channel (5.1.1). Reading a large closed
            // history with GetDealsAsync builds one oversized response that
            // terminates the server; StreamDealsAsync delivers it in bounded chunks.
            await ReadDealHistoryAsync(client);

            // Opt in only on a test account: these examples submit real trades.
            if (Environment.GetEnvironmentVariable("RUN_TRADE_LIFECYCLE_EXAMPLES") == "1")
            {
                await RunTradeLifecycleExamplesAsync(client);
            }
        }
    }

    private static async Task ReadDealHistoryAsync(Mt5GrpcClient client)
    {
        // Everything here runs over the native Grpc.Core channel from CreateCore,
        // which is the transport .NET Framework 4.8 / Windows 10 has to use. The
        // surface and the semantics are identical to the modern GrpcChannel path,
        // IAsyncEnumerable included -- it resolves from Microsoft.Bcl.AsyncInterfaces,
        // which already ships with the package, so no extra reference is needed.
        //
        // The closed deal history only grows at the newest end, so read it once in
        // full and then ask only for what is new. Re-reading it every cycle is what
        // makes a large account expensive to poll -- and with GetDealsAsync it
        // builds one oversized response that terminates the server, which is why
        // StreamDealsAsync exists.
        //
        // Chunk size is the server's policy, not the client's: chunk_size unset
        // means 500 deals per message, and any larger value is capped at 1000. This
        // client transmits whatever you set, verbatim. Prefer leaving it unset -- a
        // chunk at the 1000 cap is roughly 77 KB, back inside the size band where
        // the server was seen to abort.

        // date_to is a real upper bound, not "no limit": the server hands both
        // values straight to MT5, so 0 means 1970-01-01 and a 0..0 window matches
        // nothing at all. MT5 also stamps deals in broker server time, which can
        // run hours ahead of UTC, so leave a margin instead of using "now"
        // exactly -- otherwise a deal that just closed falls outside the window.
        // Recompute this each polling cycle in a long-running loop.
        var upperBound = DateTimeOffset.UtcNow.AddDays(1).ToUnixTimeSeconds();

        // 1. Backfill: one pass over the whole history, one chunk held at a time.
        var backfill = new DealsRequest
        {
            TimeFilter = new TimeFilter { DateFrom = 0, DateTo = upperBound },
        };

        var backfilled = 0;
        long anchorMsc = 0;

        await foreach (var chunk in client.StreamDealsAsync(backfill))
        {
            backfilled += chunk.Deals.Count;
            foreach (var deal in chunk.Deals)
            {
                if (deal.TimeMsc > anchorMsc)
                {
                    anchorMsc = deal.TimeMsc;
                }
            }
        }

        Console.WriteLine($"backfilled {backfilled} deals; anchor time_msc = {anchorMsc}");

        // 2. Incremental: anchor date_from on the newest deal already held. With no
        // new activity this transfers nothing. date_from is inclusive, so the
        // anchor deal itself may come back -- de-duplicate on Ticket.
        var incremental = new DealsRequest
        {
            TimeFilter = new TimeFilter
            {
                DateFrom = anchorMsc / 1000,   // time_msc is milliseconds; date_from is seconds
                DateTo = upperBound,
            },
        };

        var fresh = 0;
        await foreach (var chunk in client.StreamDealsAsync(incremental))
        {
            foreach (var deal in chunk.Deals)
            {
                if (deal.TimeMsc > anchorMsc)
                {
                    fresh++;
                    anchorMsc = deal.TimeMsc;
                }
            }
        }

        Console.WriteLine($"incremental fetch: {fresh} new deals since the anchor");

        // 3. When the whole history genuinely has to be in memory at once -- a
        // reconciliation pass, a report -- GetAllDealsAsync is the drop-in for
        // GetDealsAsync: same result type, but it reads in chunks underneath. The
        // cost is unbounded client memory, growing with the deal count, so prefer
        // StreamDealsAsync for a large history. Failures come back on the result
        // rather than thrown, and never with a partial collection.
        var all = await client.GetAllDealsAsync(backfill);
        Console.WriteLine(all.IsSuccess
            ? $"GetAllDealsAsync materialised {all.Value!.Deals.Count} deals"
            : $"{all.Error!.Operation}: {all.Error.Message}");
    }

    private static async Task RunTradeLifecycleExamplesAsync(Mt5GrpcClient client)
    {
        const string symbol = "EURUSD";
        const long buyTicket = 1001;
        const long sellTicket = 1002;
        const long pendingTicket = 2001;

        var market = await client.OpenOrderAsync(new OpenOrderRequest(
            symbol, ENUM_ORDER_TYPE.OrderTypeBuy, 0.01)
        {
            FillingPolicy = ENUM_ORDER_TYPE_FILLING.OrderFillingIoc,
            TimePolicy = ENUM_ORDER_TYPE_TIME.OrderTimeGtc
        });
        PrintTradeOutcome("market open", market);

        var pending = await client.OpenOrderAsync(new OpenOrderRequest(
            symbol, ENUM_ORDER_TYPE.OrderTypeBuyLimit, 0.01)
        {
            Price = 1.00,
            //TimePolicy = ENUM_ORDER_TYPE_TIME.OrderTimeSpecified,
            //Expiration = Timestamp.FromDateTime(DateTime.UtcNow.AddHours(1))
        });
        PrintTradeOutcome("pending open", pending);

        var fullClose = await client.ClosePositionAsync(buyTicket);
        PrintTradeOutcome("full close", fullClose);

        var partialClose = await client.ClosePositionAsync(sellTicket, 0.01);
        PrintTradeOutcome("partial close", partialClose);

        var positionModify = await client.ModifyTradeAsync(new ModifyTradeRequest(
            new PositionModification(buyTicket, 0, 2.00)));
        PrintTradeOutcome("position modify", positionModify);

        var pendingModify = await client.ModifyTradeAsync(new ModifyTradeRequest(
            new PendingOrderModification(
                pendingTicket, 1.01, 0, 0.95, 1.05, ENUM_ORDER_TYPE_TIME.OrderTimeGtc)));
        PrintTradeOutcome("pending modify", pendingModify);

        var closeOrder = await client.CloseOrderAsync(pendingTicket);
        PrintTradeOutcome("pending order close", closeOrder);

        var closeBy = await client.ClosePositionByAsync(new CloseByRequest(buyTicket, sellTicket));
        PrintTradeOutcome("single close-by", closeBy);

        var batch = await client.ClosePositionsByAsync(new ClosePositionsByRequest(symbol));
        Console.WriteLine($"batch status={batch.Status}; non-atomic pair count={batch.Pairs.Count}");
        foreach (var pair in batch.Pairs)
        {
            if (pair.OperationResult != null)
            {
                PrintTradeOutcome($"pair {pair.PairIndex}", pair.OperationResult);
            }
        }

        // Never retry a transport-uncertain result automatically; reconcile first.
    }

    private static void PrintTradeOutcome(string label, TradeOperationResult result)
    {
        Console.WriteLine(result.CallResult.IsSuccess
            ? $"{label}: execution={result.ExecutionStatus}, retcode={result.RawRetcode}"
            : $"{label}: call failed: {result.CallResult.Error!.Message}");
    }
}
