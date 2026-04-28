using System;
using System.Reflection;
using Grpc.Core;
using Grpc.Net.Client;
using Metatrader.V1;
using Xunit;

namespace MetaTrader.Grpc.Client.Tests
{
    public sealed class Mt5GrpcClientFactoryTests
    {
        [Fact]
        public void CreateChannel_allows_insecure_endpoint_by_default()
        {
            using var channel = Mt5GrpcClientFactory.CreateChannel(new Mt5GrpcClientOptions
            {
                Address = new Uri("http://localhost:50051")
            });

            Assert.NotNull(channel);
        }

        [Fact]
        public void CreateChannel_switches_http_address_to_https_when_tls_options_are_supplied()
        {
            var options = new Mt5GrpcClientOptions
            {
                Address = new Uri("http://localhost:50051"),
                TlsOptions = Mt5GrpcTlsOptions.SystemTrust()
            };
            var method = typeof(Mt5GrpcClientFactory).GetMethod("ResolveAddress", BindingFlags.NonPublic | BindingFlags.Static);

            var resolvedAddress = (string)method!.Invoke(null, new object[] { options })!;

            Assert.Equal("https://localhost:50051/", resolvedAddress);
        }

        [Fact]
        public void CreateClient_returns_generated_client_for_advanced_callers()
        {
            using var channel = GrpcChannel.ForAddress("http://localhost:50051");

            var client = Mt5GrpcClientFactory.CreateClient<AccountInfoService.AccountInfoServiceClient>(channel);

            Assert.NotNull(client);
            Assert.IsAssignableFrom<ClientBase<AccountInfoService.AccountInfoServiceClient>>(client);
        }

        [Fact]
        public void CallOptions_uses_no_deadline_when_default_and_override_are_absent()
        {
            var options = new Mt5GrpcCallOptions(null, null).Create(null, default);

            Assert.Null(options.Deadline);
        }

        [Fact]
        public void CallOptions_uses_per_call_deadline_over_client_default()
        {
            var deadline = DateTime.UtcNow.AddSeconds(2);
            var options = new Mt5GrpcCallOptions(TimeSpan.FromSeconds(30), null).Create(deadline, default);

            Assert.Equal(deadline, options.Deadline);
        }
    }
}
