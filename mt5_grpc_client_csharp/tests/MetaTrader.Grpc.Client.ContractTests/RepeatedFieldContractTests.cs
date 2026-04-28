using Google.Protobuf.WellKnownTypes;
using Metatrader.V1;
using Xunit;

namespace MetaTrader.Grpc.Client.ContractTests
{
    public sealed class RepeatedFieldContractTests
    {
        [Fact]
        public void Market_data_repeated_rates_preserve_order_and_numeric_types()
        {
            var response = MarketDataResponseFixtures.CreateRatesResponse(3);

            Assert.Equal(3, response.Rates.Count);
            Assert.Equal(1.10, response.Rates[0].Open);
            Assert.Equal(1.12, response.Rates[2].Open);
            Assert.IsType<long>(response.Rates[0].TickVolume);
            Assert.IsType<Timestamp>(response.Rates[0].Time);
        }

        [Fact]
        public void Symbol_response_preserves_repeated_symbol_order()
        {
            var response = new SymbolsGetResponse();
            response.Symbols.Add("EURUSD");
            response.Symbols.Add("USDJPY");
            response.Symbols.Add("XAUUSD");

            Assert.Equal(new[] { "EURUSD", "USDJPY", "XAUUSD" }, response.Symbols);
        }
    }
}
