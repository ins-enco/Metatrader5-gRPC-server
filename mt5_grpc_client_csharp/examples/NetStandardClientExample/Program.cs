using System;
using System.Threading;
using System.Threading.Tasks;
using MetaTrader.Grpc.Client;
using Metatrader.V1;

internal static class Program
{
    private static async Task Main()
    {
        var options = new Mt5GrpcClientOptions
        {
            Address = new Uri("http://localhost:50051"),
            DefaultDeadline = TimeSpan.FromSeconds(5)
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
         var a  = await client.SendOrderAsync(new OrderSendRequest { TradeRequest = tradeRequest });

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
    }
}
