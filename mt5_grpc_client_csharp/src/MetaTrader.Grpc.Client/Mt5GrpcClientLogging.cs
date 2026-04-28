using Grpc.Core;
using Microsoft.Extensions.Logging;

namespace MetaTrader.Grpc.Client
{
    internal static class Mt5GrpcClientLogging
    {
        public static void ConnectionAttempt(this ILogger? logger, string address)
        {
            logger?.LogInformation("Creating MT5 gRPC channel for {Address}.", address);
        }

        public static void CallFailure(this ILogger? logger, Mt5GrpcError error)
        {
            logger?.LogWarning(error.Exception, "MT5 gRPC call {Operation} failed: {Message}", error.Operation, error.Message);
        }

        public static void DeadlineOrCancellation(this ILogger? logger, string operation, StatusCode statusCode)
        {
            logger?.LogWarning("MT5 gRPC call {Operation} ended with {StatusCode}.", operation, statusCode);
        }

        public static void Mt5ErrorPayload(this ILogger? logger, Mt5GrpcError error)
        {
            logger?.LogWarning("MT5 operation {Operation} returned error {Mt5ErrorCode}: {Mt5ErrorMessage}", error.Operation, error.Mt5ErrorCode, error.Mt5ErrorMessage);
        }
    }
}
