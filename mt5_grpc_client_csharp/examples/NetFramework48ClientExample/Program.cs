using System;
using System.Net.Http;
using System.Threading.Tasks;
using Grpc.Net.Client;
using Metatrader.V1;

internal static class Program
{
    private static async Task Main()
    {
        using (var handler = new WinHttpHandler())
        using (var channel = GrpcChannel.ForAddress(
            "https://localhost:50051",
            new GrpcChannelOptions { HttpHandler = handler }))
        {
            var client = new AccountInfoService.AccountInfoServiceClient(channel);
            var response = await client.GetAccountInfoAsync(new AccountInfoRequest());
            Console.WriteLine(response.AccountInfo.Login);

            // The request enum types (0.2.0+) compile and behave identically on
            // the .NET Framework 4.8 / netstandard2.0 target (FR-011, SC-007).
            var orderClient = new OrderSendService.OrderSendServiceClient(channel);
            var send = await orderClient.SendOrderAsync(new OrderSendRequest
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
            Console.WriteLine(send.TradeResult?.Retcode);
        }
    }
}
