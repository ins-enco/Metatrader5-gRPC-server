using System;

namespace MetaTrader.Grpc.Client
{
    public sealed class Mt5GrpcClientException : Exception
    {
        public Mt5GrpcClientException(string message)
            : base(message)
        {
        }

        public Mt5GrpcClientException(string message, Exception innerException)
            : base(message, innerException)
        {
        }

        public Mt5GrpcClientException(Mt5GrpcError error)
            : base(error?.Message ?? "MT5 gRPC error", error?.Exception)
        {
            Error = error;
        }

        /// <summary>
        /// The mapped MT5 error that terminated a streaming call, when the failure
        /// originated from an in-band <c>Error</c> frame or a transport fault on a
        /// server stream. Null for message-only exceptions.
        /// </summary>
        public Mt5GrpcError? Error { get; }
    }
}
