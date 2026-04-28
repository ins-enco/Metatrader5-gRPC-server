using System.Threading;
using Grpc.Core;
using Metatrader.Test.V1;
using Xunit;

namespace MetaTrader.Grpc.Client.ContractTests
{
    public sealed class StreamingFixtureTests
    {
        [Fact]
        public void Streaming_fixture_generates_all_streaming_client_patterns()
        {
            Assert.True(StreamingFixtureUsage.HasExpectedStreamingMethods());
        }

        [Fact]
        public void Streaming_fixture_supports_cancellation_options()
        {
            using var source = new CancellationTokenSource();
            var options = StreamingFixtureUsage.CreateCancellableOptions(source.Token);

            Assert.Equal(source.Token, options.CancellationToken);
        }

        [Fact]
        public void Streaming_fixture_messages_are_typed()
        {
            var request = new StreamingRequest { Symbol = "EURUSD", Sequence = 1 };
            var response = new StreamingResponse { Symbol = request.Symbol, Sequence = request.Sequence, Status = "ok" };

            Assert.Equal("EURUSD", response.Symbol);
            Assert.Equal(1, response.Sequence);
        }
    }
}
