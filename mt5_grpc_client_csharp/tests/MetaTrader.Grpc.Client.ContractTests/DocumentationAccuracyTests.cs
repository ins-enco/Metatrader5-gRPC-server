using System.IO;
using System;
using Metatrader.V1;
using Xunit;

namespace MetaTrader.Grpc.Client.ContractTests
{
    public sealed class DocumentationAccuracyTests
    {
        [Fact]
        public void Readme_states_current_mt5_services_are_unary_only()
        {
            var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
            var readme = File.ReadAllText(Path.Combine(root, "README.md"));

            Assert.Contains("Current MT5 proto services are unary-only", readme);
            Assert.DoesNotContain("current MT5 bidirectional streaming", readme);
        }

        [Fact]
        public void Migration_guide_exists_and_documents_the_named_values()
        {
            var migration = File.ReadAllText(MigrationGuidePath());

            // Every covered field's target enum type must be named in the guide.
            Assert.Contains("ENUM_TRADE_REQUEST_ACTIONS", migration);
            Assert.Contains("ENUM_ORDER_TYPE", migration);
            Assert.Contains("ENUM_ORDER_TYPE_FILLING", migration);
            Assert.Contains("ENUM_ORDER_TYPE_TIME", migration);
        }

        // The guide documents each integer→named migration as transmitting the
        // identical numeric value (SC-004). Verify those documented pairs hold.
        [Theory]
        [InlineData("Action = 1", (int)ENUM_TRADE_REQUEST_ACTIONS.TradeActionDeal, 1)]
        [InlineData("Type = 0", (int)ENUM_ORDER_TYPE.OrderTypeBuy, 0)]
        [InlineData("TypeFilling = 1", (int)ENUM_ORDER_TYPE_FILLING.OrderFillingIoc, 1)]
        [InlineData("TypeTime = 0", (int)ENUM_ORDER_TYPE_TIME.OrderTimeGtc, 0)]
        public void Migration_examples_preserve_the_transmitted_value(
            string documentedIntegerAssignment, int namedValueNumber, int expected)
        {
            var migration = File.ReadAllText(MigrationGuidePath());

            // The "before" integer assignment appears in the guide...
            Assert.Contains(documentedIntegerAssignment, migration);
            // ...and the named value it maps to transmits the identical number.
            Assert.Equal(expected, namedValueNumber);
        }

        private static string MigrationGuidePath()
        {
            var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
            return Path.Combine(root, "MIGRATION.md");
        }
    }
}
