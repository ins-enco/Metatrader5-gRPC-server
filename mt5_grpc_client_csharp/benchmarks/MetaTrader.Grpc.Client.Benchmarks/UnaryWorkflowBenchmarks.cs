using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using MetaTrader.Grpc.Client;
using Metatrader.V1;

namespace MetaTrader.Grpc.Client.Benchmarks
{
    public class UnaryWorkflowBenchmarks
    {
        private readonly SymbolsGetResponse symbolsResponse = new SymbolsGetResponse();

        public UnaryWorkflowBenchmarks()
        {
            symbolsResponse.Symbols.Add("EURUSD");
            symbolsResponse.Symbols.Add("USDJPY");
            symbolsResponse.Symbols.Add("XAUUSD");
        }

        [Benchmark(Baseline = true)]
        public int DirectGeneratedClientShape()
        {
            return symbolsResponse.Symbols.Count;
        }

        [Benchmark]
        public int WrapperResultShape()
        {
            var result = Mt5GrpcResult<SymbolsGetResponse>.Success(symbolsResponse);
            return result.Value!.Symbols.Count;
        }
    }

    internal static class Program
    {
        private static void Main(string[] args)
        {
            BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
        }
    }
}
