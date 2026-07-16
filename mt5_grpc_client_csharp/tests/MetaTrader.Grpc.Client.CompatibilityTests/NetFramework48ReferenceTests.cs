using System.IO;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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
        public void NetFramework48_example_targets_net48_and_uses_grpc_core_channel()
        {
            var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
            var project = File.ReadAllText(Path.Combine(root, "examples", "NetFramework48ClientExample", "NetFramework48ClientExample.csproj"));
            var program = File.ReadAllText(Path.Combine(root, "examples", "NetFramework48ClientExample", "Program.cs"));

            Assert.Contains("<TargetFramework>net48</TargetFramework>", project);
            // WinHttpHandler lacks HTTP/2 on Windows 10, so the example no longer
            // references that package. It connects through the native Grpc.Core channel
            // via CreateCore, which flows transitively from the library's .NET Framework
            // target. (Match the package id, not the bare word, so explanatory comments
            // mentioning WinHttpHandler don't trip this assertion.)
            Assert.DoesNotContain("System.Net.Http.WinHttpHandler", project);
            Assert.Contains("Mt5GrpcClientFactory.CreateCore", program);
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

        [Fact]
        public void NetFramework48_consumers_can_reference_every_trade_lifecycle_type_and_signature()
        {
            var expectedMethods = new[]
            {
                new { Name = "OpenOrderAsync", Request = typeof(OpenOrderRequest), Result = typeof(Task<TradeOperationResult>) },
                new { Name = "ClosePositionAsync", Request = typeof(ClosePositionRequest), Result = typeof(Task<TradeOperationResult>) },
                new { Name = "ModifyTradeAsync", Request = typeof(ModifyTradeRequest), Result = typeof(Task<TradeOperationResult>) },
                new { Name = "ClosePositionByAsync", Request = typeof(CloseByRequest), Result = typeof(Task<TradeOperationResult>) },
                new { Name = "ClosePositionsByAsync", Request = typeof(ClosePositionsByRequest), Result = typeof(Task<MultipleCloseByResult>) }
            };

            foreach (var expected in expectedMethods)
            {
                var method = typeof(Mt5GrpcClient).GetMethod(expected.Name);
                Assert.NotNull(method);
                Assert.Equal(expected.Result, method!.ReturnType);
                Assert.Equal(
                    new[] { expected.Request, typeof(DateTime?), typeof(CancellationToken) },
                    method.GetParameters().Select(parameter => parameter.ParameterType));
                Assert.True(method.GetParameters()[1].IsOptional);
                Assert.True(method.GetParameters()[2].IsOptional);
            }

            _ = new OpenOrderRequest("EURUSD", ENUM_ORDER_TYPE.OrderTypeBuy, 0.1);
            _ = new ClosePositionRequest(1, "EURUSD", PositionSide.Buy, 0.1);
            _ = new ModifyTradeRequest(new PositionModification(1, 0, 0));
            _ = new ModifyTradeRequest(new PendingOrderModification(
                2, 1, 0, 0, 0, ENUM_ORDER_TYPE_TIME.OrderTimeGtc));
            _ = new CloseByRequest(1, 2);
            _ = new ClosePositionsByRequest("EURUSD");
            _ = new CloseByPairOutcome(1, 1, 2, PairAttemptState.Unattempted, null);
            _ = new PositionRemainder(1, 0.1, PositionRemainderReason.NoOpposite);
            Assert.Equal(5, Enum.GetValues(typeof(TradeExecutionStatus)).Length);
            Assert.Equal(6, Enum.GetValues(typeof(MultipleCloseByStatus)).Length);
        }
    }
}
