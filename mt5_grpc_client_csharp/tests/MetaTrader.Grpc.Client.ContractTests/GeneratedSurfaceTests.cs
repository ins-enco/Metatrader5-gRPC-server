using System;
using System.Linq;
using System.Reflection;
using Metatrader.V1;
using Xunit;

namespace MetaTrader.Grpc.Client.ContractTests
{
    public sealed class GeneratedSurfaceTests
    {
        [Fact]
        public void Generated_contract_exposes_all_current_services()
        {
            var assembly = typeof(AccountInfoService).Assembly;

            foreach (var service in ProtoContractCatalog.UnaryServices.Keys)
            {
                var serviceType = assembly.GetType("Metatrader.V1." + service);
                Assert.NotNull(serviceType);
                Assert.NotNull(serviceType!.GetNestedType(service + "Client", BindingFlags.Public));
            }
        }

        [Fact]
        public void Generated_contract_exposes_all_current_unary_rpc_methods()
        {
            var assembly = typeof(AccountInfoService).Assembly;

            foreach (var pair in ProtoContractCatalog.UnaryServices)
            {
                var clientType = assembly.GetType("Metatrader.V1." + pair.Key)!.GetNestedType(pair.Key + "Client", BindingFlags.Public)!;
                foreach (var rpc in pair.Value)
                {
                    Assert.Contains(clientType.GetMethods(), method => method.Name == rpc || method.Name == rpc + "Async");
                }
            }
        }

        [Fact]
        public void Generated_contract_counts_match_spec()
        {
            Assert.Equal(16, ProtoContractCatalog.UnaryServices.Count);
            Assert.Equal(31, ProtoContractCatalog.UnaryServices.Values.Sum(methods => methods.Length));
        }

        // --- Request enum fields (0.2.0): correct enum types on preserved field numbers ---
        // (FR-013, SC-008; see specs/003-csharp-request-enums)

        [Theory]
        [InlineData("Action", typeof(ENUM_TRADE_REQUEST_ACTIONS), TradeRequest.ActionFieldNumber, 1)]
        [InlineData("Type", typeof(ENUM_ORDER_TYPE), TradeRequest.TypeFieldNumber, 11)]
        [InlineData("TypeFilling", typeof(ENUM_ORDER_TYPE_FILLING), TradeRequest.TypeFillingFieldNumber, 12)]
        [InlineData("TypeTime", typeof(ENUM_ORDER_TYPE_TIME), TradeRequest.TypeTimeFieldNumber, 13)]
        public void Trade_request_fields_are_enum_typed_on_preserved_numbers(
            string propertyName, Type expectedType, int fieldNumberConstant, int expectedFieldNumber)
        {
            var property = typeof(TradeRequest).GetProperty(propertyName);
            Assert.NotNull(property);
            Assert.Equal(expectedType, property!.PropertyType);
            Assert.Equal(expectedFieldNumber, fieldNumberConstant);
        }

        [Fact]
        public void Calc_request_action_fields_share_order_type_enum_on_field_one()
        {
            var margin = typeof(OrderCalcMarginRequest).GetProperty("Action");
            var profit = typeof(OrderCalcProfitRequest).GetProperty("Action");

            Assert.Equal(typeof(ENUM_ORDER_TYPE), margin!.PropertyType);
            Assert.Equal(typeof(ENUM_ORDER_TYPE), profit!.PropertyType);
            Assert.Equal(1, OrderCalcMarginRequest.ActionFieldNumber);
            Assert.Equal(1, OrderCalcProfitRequest.ActionFieldNumber);
        }
    }
}
