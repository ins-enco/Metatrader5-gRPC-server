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
    }
}
