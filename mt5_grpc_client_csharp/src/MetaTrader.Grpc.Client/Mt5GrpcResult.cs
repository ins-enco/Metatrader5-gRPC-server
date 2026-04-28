using System;
using Grpc.Core;

namespace MetaTrader.Grpc.Client
{
    public sealed class Mt5GrpcResult<T>
    {
        private Mt5GrpcResult(T? value, Mt5GrpcError? error)
        {
            Value = value;
            Error = error;
        }

        public bool IsSuccess
        {
            get { return Error == null; }
        }

        public T? Value { get; }

        public Mt5GrpcError? Error { get; }

        public static Mt5GrpcResult<T> Success(T value)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            return new Mt5GrpcResult<T>(value, null);
        }

        public static Mt5GrpcResult<T> Failure(Mt5GrpcError error)
        {
            if (error == null)
            {
                throw new ArgumentNullException(nameof(error));
            }

            return new Mt5GrpcResult<T>(default, error);
        }
    }

    public sealed class Mt5GrpcError
    {
        public string Operation { get; set; } = string.Empty;

        public StatusCode? StatusCode { get; set; }

        public string Message { get; set; } = string.Empty;

        public Metadata? Trailers { get; set; }

        public int? Mt5ErrorCode { get; set; }

        public string? Mt5ErrorMessage { get; set; }

        public Exception? Exception { get; set; }
    }
}
