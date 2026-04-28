using System.Reflection;
using Xunit;

namespace MetaTrader.Grpc.Client.Tests
{
    public sealed class BinaryProtocolTests
    {
        [Fact]
        public void Unary_invoker_does_not_reference_json_or_text_serialization()
        {
            var assemblyNames = typeof(Mt5GrpcClient).Assembly.GetReferencedAssemblies();

            Assert.DoesNotContain(assemblyNames, name => name.Name == "Newtonsoft.Json" || name.Name == "System.Text.Json");
            Assert.DoesNotContain(typeof(Mt5GrpcClient).Assembly.GetTypes(), type => type.Name.Contains("Json"));
        }
    }
}
