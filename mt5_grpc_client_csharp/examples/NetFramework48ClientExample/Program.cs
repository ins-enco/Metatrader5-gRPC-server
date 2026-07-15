using System;
using System.Threading;
using System.Threading.Tasks;
using MetaTrader.Grpc.Client;
using Metatrader.V1;

internal static class Program
{
    // Terminal / login defaults. Override any of them from the command line:
    //   NetFramework48ClientExample.exe <login> <password> <server> [terminalPath]
    private const string DefaultServerAddress = "http://10.27.102.101:8292";
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
        }
    }
}
