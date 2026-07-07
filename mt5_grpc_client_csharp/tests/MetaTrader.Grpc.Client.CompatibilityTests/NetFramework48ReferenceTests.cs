using System.IO;
using System;
using Google.Protobuf;
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

        [Fact]
        public void NetFramework48_example_uses_named_request_enum_values()
        {
            var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
            var program = File.ReadAllText(Path.Combine(root, "examples", "NetFramework48ClientExample", "Program.cs"));

            Assert.Contains("ENUM_TRADE_REQUEST_ACTIONS.TradeActionDeal", program);
            Assert.Contains("ENUM_ORDER_TYPE.OrderTypeBuy", program);
        }

        [Fact]
        public void Request_enum_values_transmit_identically_on_netstandard_target()
        {
            // The enum types ship in the netstandard2.0 package a net48 host consumes.
            // Named values must encode to the same varint the prior int32 fields used.
            var request = new TradeRequest
            {
                Action = ENUM_TRADE_REQUEST_ACTIONS.TradeActionDeal,
                Type = ENUM_ORDER_TYPE.OrderTypeSell,
                TypeFilling = ENUM_ORDER_TYPE_FILLING.OrderFillingReturn,
                TypeTime = ENUM_ORDER_TYPE_TIME.OrderTimeDay,
            };

            var parsed = TradeRequest.Parser.ParseFrom(request.ToByteArray());

            Assert.Equal(1, (int)parsed.Action);
            Assert.Equal(1, (int)parsed.Type);
            Assert.Equal(2, (int)parsed.TypeFilling);
            Assert.Equal(1, (int)parsed.TypeTime);
        }
    }
}
