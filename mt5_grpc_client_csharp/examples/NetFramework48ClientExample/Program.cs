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
        }
    }
}
