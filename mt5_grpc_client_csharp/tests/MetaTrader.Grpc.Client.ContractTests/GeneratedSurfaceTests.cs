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
    }
}
