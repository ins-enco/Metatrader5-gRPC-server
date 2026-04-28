using System.Linq;
using Grpc.Core;
using Metatrader.Test.V1;

namespace MetaTrader.Grpc.Client.ContractTests
{
    internal static class StreamingFixtureUsage
    {
        public static bool HasExpectedStreamingMethods()
        {
            var clientType = typeof(StreamingFixtureService.StreamingFixtureServiceClient);
            var methodNames = clientType.GetMethods().Select(method => method.Name).ToArray();

            return methodNames.Contains("ServerStream")
                && methodNames.Contains("ClientStream")
                && methodNames.Contains("BidiStream");
        }

        public static CallOptions CreateCancellableOptions(System.Threading.CancellationToken cancellationToken)
        {
            return new CallOptions(cancellationToken: cancellationToken);
        }
    }
}
