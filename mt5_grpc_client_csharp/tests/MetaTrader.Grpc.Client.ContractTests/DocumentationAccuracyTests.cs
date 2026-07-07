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
