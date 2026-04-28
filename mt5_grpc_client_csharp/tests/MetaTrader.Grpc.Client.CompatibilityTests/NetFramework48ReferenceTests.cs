using System.IO;
using System;
using MetaTrader.Grpc.Client;
using Metatrader.V1;
using Xunit;

namespace MetaTrader.Grpc.Client.CompatibilityTests
{
    public sealed class NetFramework48ReferenceTests
    {
        [Fact]
        public void Client_package_exposes_netstandard_compatible_public_types()
        {
            Assert.Equal("MetaTrader.Grpc.Client", typeof(Mt5GrpcClient).Namespace);
            Assert.NotNull(typeof(AccountInfoService.AccountInfoServiceClient));
        }

        [Fact]
        public void NetFramework48_example_targets_net48_and_uses_winhttphandler()
        {
            var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
            var project = File.ReadAllText(Path.Combine(root, "examples", "NetFramework48ClientExample", "NetFramework48ClientExample.csproj"));
            var program = File.ReadAllText(Path.Combine(root, "examples", "NetFramework48ClientExample", "Program.cs"));

            Assert.Contains("<TargetFramework>net48</TargetFramework>", project);
            Assert.Contains("System.Net.Http.WinHttpHandler", project);
            Assert.Contains("WinHttpHandler", program);
        }
    }
}
