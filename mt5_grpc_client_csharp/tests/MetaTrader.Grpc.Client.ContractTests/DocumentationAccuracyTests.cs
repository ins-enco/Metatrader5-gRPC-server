using System.IO;
using System;
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
    }
}
