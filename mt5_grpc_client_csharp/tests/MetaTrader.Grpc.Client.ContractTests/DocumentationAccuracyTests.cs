using System.IO;
using System;
using System.Linq;
using System.Xml.Linq;
using Metatrader.V1;
using Xunit;

namespace MetaTrader.Grpc.Client.ContractTests
{
    public sealed class DocumentationAccuracyTests
    {
        // Drift guard (FR-005, Decision 4): the authored compatibility values live
        // in the csproj (<ProtoContractIdentity>/<TestedServerVersionRange>). The
        // feed-visible surfaces a consumer sees without source access - the packed
        // README.md and <PackageReleaseNotes> - MUST quote those same values, or
        // compatibility metadata silently drifts out of sync.
        [Fact]
        public void Readme_and_release_notes_carry_current_compatibility_metadata()
        {
            var csproj = XDocument.Load(ProjectPath());

            string Prop(string name)
            {
                // SDK-style csproj has no default namespace, so a local-name match works.
                var value = csproj.Descendants()
                    .Where(e => e.Name.LocalName == name)
                    .Select(e => e.Value)
                    .FirstOrDefault();
                Assert.False(string.IsNullOrWhiteSpace(value), $"csproj is missing <{name}>.");
                return value!.Trim();
            }

            var contractId   = Prop("ProtoContractIdentity");
            var serverRange  = Prop("TestedServerVersionRange");
            var releaseNotes = Prop("PackageReleaseNotes");

            var readme = File.ReadAllText(Path.Combine(RepoClientRoot(), "README.md"));

            Assert.Contains(contractId, readme);
            Assert.Contains(serverRange, readme);
            Assert.Contains(contractId, releaseNotes);
            Assert.Contains(serverRange, releaseNotes);
        }

        // As of 0.3.0 the contract has its first server-streaming RPC
        // (TradeEventsService.SubscribeTradeTransactions). The README must document
        // it accurately and no longer claim the services are unary-only.
        [Fact]
        public void Readme_documents_the_trade_events_streaming_rpc()
        {
            var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
            var readme = File.ReadAllText(Path.Combine(root, "README.md"));

            Assert.Contains("SubscribeTradeTransactions", readme);
            Assert.Contains("server-streaming", readme);
            Assert.DoesNotContain("Current MT5 proto services are unary-only", readme);
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

        [Fact]
        public void Readme_documents_every_lifecycle_category_and_safety_rule()
        {
            var readme = File.ReadAllText(Path.Combine(RepoClientRoot(), "README.md"));

            Assert.Contains("OpenOrderAsync", readme);
            Assert.Contains("ClosePositionAsync", readme);
            Assert.Contains("ModifyTradeAsync", readme);
            Assert.Contains("ClosePositionByAsync", readme);
            Assert.Contains("ClosePositionsByAsync", readme);
            Assert.Contains("full close", readme, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("partial close", readme, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("ExecutionStatus", readme);
            Assert.Contains("hedging account", readme, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("non-atomic", readme, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("do not retry", readme, StringComparison.OrdinalIgnoreCase);
        }

        [Theory]
        [InlineData("NetStandardClientExample")]
        [InlineData("NetFramework48ClientExample")]
        public void Runnable_examples_cover_all_lifecycle_operations_and_result_inspection(string project)
        {
            var program = File.ReadAllText(Path.Combine(RepoClientRoot(), "examples", project, "Program.cs"));

            Assert.Contains("OpenOrderAsync", program);
            Assert.Contains("ClosePositionAsync", program);
            Assert.Contains("ModifyTradeAsync", program);
            Assert.Contains("ClosePositionByAsync", program);
            Assert.Contains("ClosePositionsByAsync", program);
            Assert.Contains("CallResult", program);
            Assert.Contains("ExecutionStatus", program);
            Assert.Contains("non-atomic", program, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("retry", program, StringComparison.OrdinalIgnoreCase);
        }

        private static string MigrationGuidePath()
        {
            return Path.Combine(RepoClientRoot(), "MIGRATION.md");
        }

        // mt5_grpc_client_csharp root (where the packed README.md / MIGRATION.md live).
        private static string RepoClientRoot()
        {
            return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        }

        private static string ProjectPath()
        {
            return Path.Combine(
                RepoClientRoot(), "src", "MetaTrader.Grpc.Client", "MetaTrader.Grpc.Client.csproj");
        }
    }
}
