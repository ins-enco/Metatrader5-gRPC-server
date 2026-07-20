using System;
using System.Reflection;
using Grpc.Core;
using Metatrader.V1;

namespace MetaTrader.Grpc.Client
{
    public static class Mt5GrpcErrorMapper
    {
        public static Mt5GrpcError? FromMt5Error(string operation, Error? error)
        {
            if (error == null || error.Code == 0)
            {
                return null;
            }

            return new Mt5GrpcError
            {
                Operation = operation,
                Message = string.IsNullOrWhiteSpace(error.Message) ? $"MT5 error {error.Code}" : error.Message,
                Mt5ErrorCode = error.Code,
                Mt5ErrorMessage = error.Message
            };
        }

        public static Mt5GrpcError FromRpcException(string operation, RpcException exception)
        {
            var error = new Mt5GrpcError
            {
                Operation = operation,
                StatusCode = exception.StatusCode,
                Message = exception.Status.Detail,
                Trailers = exception.Trailers,
                Exception = exception
            };

            // Some server operations (e.g. Connect) abort with a gRPC status instead of
            // returning an Error payload. In that case the native MT5 error code is carried
            // in trailing metadata so callers don't need a separate GetLastError round-trip.
            // The human-readable message is already in Status.Detail (Message above).
            var codeEntry = exception.Trailers.Get("mt5-error-code");
            if (codeEntry != null && int.TryParse(codeEntry.Value, out var mt5Code))
            {
                error.Mt5ErrorCode = mt5Code;
                error.Mt5ErrorMessage = exception.Status.Detail;
            }

            return error;
        }

        public static Mt5GrpcError FromCancellation(string operation, OperationCanceledException exception)
        {
            return new Mt5GrpcError
            {
                Operation = operation,
                StatusCode = StatusCode.Cancelled,
                Message = "The gRPC call was cancelled.",
                Exception = exception
            };
        }

        public static Mt5GrpcError FromException(string operation, Exception exception)
        {
            return new Mt5GrpcError
            {
                Operation = operation,
                StatusCode = StatusCode.Unknown,
                Message = exception.Message,
                Exception = exception
            };
        }

        public static Error? TryGetErrorPayload<TResponse>(TResponse response)
        {
            if (response == null)
            {
                return null;
            }

            if (response is Error error)
            {
                return error;
            }

            var property = response.GetType().GetProperty("Error", BindingFlags.Public | BindingFlags.Instance);
            return property?.GetValue(response) as Error;
        }
    }
}
