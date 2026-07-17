using System;
using System.Threading;
using System.Threading.Tasks;
using Google.Protobuf.WellKnownTypes;
using MetaTrader.Grpc.Client;
using Metatrader.V1;

internal static class Program
{
    private static async Task Main()
    {
        var options = new Mt5GrpcClientOptions
        {
            Address = new Uri("http://10.27.102.101:8292"),
        };

        using var client = Mt5GrpcClientFactory.Create(options);

        var connectRequest = new ConnectRequest();
        var terminalPath = "C:\\Program Files\\MetaTrader 5\\terminal64.exe";
        string loginValue = "833671";
        var password = "6cU!DaDy";
        var server = "185.97.161.40";

        if (!string.IsNullOrWhiteSpace(terminalPath))
        {
            connectRequest.Path = terminalPath;
        }

        if (long.TryParse(loginValue, out var login))
        {
            connectRequest.Login = login;
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
            Console.WriteLine($"{connect.Error!.Operation}: {connect.Error.Message}");
            return;
        }

        var account = await client.GetAccountInfoAsync(
            deadline: DateTime.UtcNow.AddSeconds(2),
            cancellationToken: CancellationToken.None);

        if (!account.IsSuccess)
        {
            Console.WriteLine($"{account.Error!.Operation}: {account.Error.Message}");
            return;
        }

        Console.WriteLine(account.Value!.AccountInfo.Login);

        var symbols = await client.GetSymbolsAsync(new SymbolsGetRequest { Group = "*" });
        var symbol = symbols.IsSuccess && symbols.Value!.Symbols.Count > 0
            ? symbols.Value.Symbols[0]
            : "EURUSD";

        _ = await client.GetSymbolInfoAsync(new SymbolInfoRequest { Symbol = symbol });
        _ = await client.GetSymbolInfoTickAsync(new SymbolInfoTickRequest { Symbol = symbol });
        _ = await client.CopyTicksFromAsync(new CopyTicksFromRequest { Symbol = symbol, Count = 10, Flags = 1 });

        // Build the trade request with named MT5 values (0.2.0+): the compiler
        // restricts each field to its valid option set and IntelliSense lists them.
        var tradeRequest = new TradeRequest
        {
            Symbol = "EURUSD",
            Volume = 0.01,
            Action = ENUM_TRADE_REQUEST_ACTIONS.TradeActionDeal,
            Type = ENUM_ORDER_TYPE.OrderTypeBuy,
            TypeFilling = ENUM_ORDER_TYPE_FILLING.OrderFillingIoc,
            TypeTime = ENUM_ORDER_TYPE_TIME.OrderTimeGtc,
        };

        _ = await client.CheckOrderAsync(new OrderCheckRequest { TradeRequest = tradeRequest.Clone() });

        var subscription = client.SubscribeTradeTransactions(
            new SubscribeTradeTransactionsRequest()
            {
                PollIntervalMs = 200,
            });

        subscription.TransactionReceived += (_, evt) =>
            Console.WriteLine($"deal {evt.DealTicket} {evt.Symbol} vol={evt.Volume} @ {evt.Price} profit={evt.Entry}");
        subscription.Faulted += (_, error) =>
            Console.WriteLine($"stream faulted: {error.Message}");   // resubscribe from last TimeMsc here
        subscription.Completed += (_, _) =>
            Console.WriteLine("stream ended");


        subscription.Start();          // begins consuming on a background task

        var a = await client.SendOrderAsync(new OrderSendRequest { TradeRequest = tradeRequest });

        await Task.Delay(TimeSpan.FromSeconds(30));
        subscription.Stop();           // graceful cancel; releases the server-side worker
        subscription.Dispose();

        // Calculation requests share the ENUM_ORDER_TYPE named set (US2).
        _ = await client.CalcMarginAsync(new OrderCalcMarginRequest
        {
            Action = ENUM_ORDER_TYPE.OrderTypeBuy,
            Symbol = symbol,
            Volume = 0.01,
            Price = 1.0850,
        });

        // Profit calc expects Buy or Sell (documented; not compile-enforced).
        _ = await client.CalcProfitAsync(new OrderCalcProfitRequest
        {
            Action = ENUM_ORDER_TYPE.OrderTypeSell,
            Symbol = symbol,
            Volume = 0.01,
            PriceOpen = 1.0850,
            PriceClose = 1.0800,
        });

        // These calls submit real trade operations. Set the opt-in flag only on
        // a test account after replacing the example tickets/prices as needed.
        if (Environment.GetEnvironmentVariable("RUN_TRADE_LIFECYCLE_EXAMPLES") == "1")
        {
            await RunTradeLifecycleExamplesAsync(client, symbol);
        }
    }

    private static async Task RunTradeLifecycleExamplesAsync(Mt5GrpcClient client, string symbol)
    {
        // 1. Open: market DEAL and expiring pending PENDING requests. Call status
        // and MT5 execution status are deliberately inspected separately.
        var marketOpen = await client.OpenOrderAsync(new OpenOrderRequest(
            symbol, ENUM_ORDER_TYPE.OrderTypeBuy, 0.01)
        {
            FillingPolicy = ENUM_ORDER_TYPE_FILLING.OrderFillingIoc,
            TimePolicy = ENUM_ORDER_TYPE_TIME.OrderTimeGtc,
            StopLoss = 1.00,
            TakeProfit = 2.00,
            Comment = "lifecycle-market-open"
        });
        PrintTradeOutcome("market open", marketOpen);

        var pendingOpen = await client.OpenOrderAsync(new OpenOrderRequest(
            symbol, ENUM_ORDER_TYPE.OrderTypeBuyLimit, 0.01)
        {
            Price = 1.00,
            StopLoss = 0.95,
            TakeProfit = 1.05,
            FillingPolicy = ENUM_ORDER_TYPE_FILLING.OrderFillingReturn,
            TimePolicy = ENUM_ORDER_TYPE_TIME.OrderTimeSpecified,
            Expiration = Timestamp.FromDateTime(DateTime.UtcNow.AddHours(1)),
            Comment = "lifecycle-pending-open"
        });
        PrintTradeOutcome("pending open", pendingOpen);

        const long buyPositionTicket = 1001;
        const long sellPositionTicket = 1002;
        const long pendingOrderTicket = 2001;

        // 2. Close: the client looks up the position and symbol settings. Omit
        // volume for a full close; provide volume for a partial close.
        var fullClose = await client.ClosePositionAsync(buyPositionTicket);
        PrintTradeOutcome("full close", fullClose);

        var partialClose = await client.ClosePositionAsync(sellPositionTicket, volume: 0.01);
        PrintTradeOutcome("partial close", partialClose);

        // 3. Modify: callers provide the complete desired final state. Zero SL/TP
        // clears that value when MT5 permits it; there is no hidden state lookup.
        var modifyPosition = await client.ModifyTradeAsync(new ModifyTradeRequest(
            new PositionModification(buyPositionTicket, stopLoss: 0, takeProfit: 2.10)));
        PrintTradeOutcome("position modify", modifyPosition);

        var modifyPending = await client.ModifyTradeAsync(new ModifyTradeRequest(
            new PendingOrderModification(
                pendingOrderTicket,
                price: 1.01,
                stopLimitPrice: 0,
                stopLoss: 0.96,
                takeProfit: 1.06,
                timePolicy: ENUM_ORDER_TYPE_TIME.OrderTimeSpecified)
            {
                Expiration = Timestamp.FromDateTime(DateTime.UtcNow.AddHours(2))
            }));
        PrintTradeOutcome("pending modify", modifyPending);

        // 4. Cancel a pending order using only its ticket.
        var closeOrder = await client.CloseOrderAsync(pendingOrderTicket);
        PrintTradeOutcome("pending order close", closeOrder);

        // 5. Single close-by is hedging-account-only. Ticket roles are preserved.
        var closeBy = await client.ClosePositionByAsync(new CloseByRequest(
            buyPositionTicket, sellPositionTicket)
        {
            Comment = "lifecycle-close-by"
        });
        PrintTradeOutcome("single close-by", closeBy);

        // 6. Batch close-by is sequential and non-atomic. Inspect every pair;
        // earlier successes are not rolled back when a later pair fails.
        var batch = await client.ClosePositionsByAsync(new ClosePositionsByRequest(symbol)
        {
            Comment = "lifecycle-close-by-batch"
        });
        Console.WriteLine($"batch status={batch.Status} error={batch.BatchError?.Message}");
        foreach (var pair in batch.Pairs)
        {
            if (pair.AttemptState == PairAttemptState.Unattempted)
            {
                Console.WriteLine($"pair {pair.PairIndex} unattempted: {pair.PositionTicket}/{pair.OppositePositionTicket}");
                continue;
            }

            PrintTradeOutcome($"pair {pair.PairIndex}", pair.OperationResult!);
        }

        // A transport-uncertain order may already have executed. Do not retry it
        // automatically; reconcile account state first.
    }

    private static void PrintTradeOutcome(string label, TradeOperationResult result)
    {
        if (!result.CallResult.IsSuccess)
        {
            Console.WriteLine($"{label}: call failed: {result.CallResult.Error!.Message}");
            return;
        }

        Console.WriteLine($"{label}: execution={result.ExecutionStatus}, retcode={result.RawRetcode}");
    }
}
