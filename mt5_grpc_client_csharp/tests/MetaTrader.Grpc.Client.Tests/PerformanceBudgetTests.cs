using System;
using System.Diagnostics;
using Metatrader.V1;
using Xunit;

namespace MetaTrader.Grpc.Client.Tests
{
    public sealed class PerformanceBudgetTests
    {
        [Fact]
        public void Typed_result_mapping_stays_within_small_overhead_budget()
        {
            var response = new SymbolsGetResponse();
            response.Symbols.Add("EURUSD");

            var direct = Measure(() =>
            {
                _ = response.Symbols.Count;
            });

            var wrapped = Measure(() =>
            {
                var result = Mt5GrpcResult<SymbolsGetResponse>.Success(response);
                _ = result.Value!.Symbols.Count;
            });

            Assert.True(wrapped <= direct + TimeSpan.FromMilliseconds(25), $"Wrapped: {wrapped}; Direct: {direct}");
        }

        private static TimeSpan Measure(Action action)
        {
            var stopwatch = Stopwatch.StartNew();
            for (var i = 0; i < 10000; i++)
            {
                action();
            }

            stopwatch.Stop();
            return stopwatch.Elapsed;
        }
    }
}
