using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using Metatrader.V1;

namespace MetaTrader.Grpc.Client
{
    /// <summary>
    /// Streaming counterpart to <see cref="Mt5GrpcUnaryInvoker"/>. Wraps a
    /// server-streaming call and exposes it as an <see cref="IAsyncEnumerable{T}"/>,
    /// mapping faults to <see cref="Mt5GrpcError"/> and logging them like the unary
    /// path. Terminal in-band <c>Error</c> frames and transport faults both surface
    /// as an <see cref="Mt5GrpcClientException"/> carrying the mapped error.
    /// </summary>
    internal sealed class Mt5GrpcStreamingInvoker
    {
        private readonly Mt5GrpcCallOptions callOptions;
        private readonly ILogger? logger;

        public Mt5GrpcStreamingInvoker(Mt5GrpcCallOptions callOptions, ILogger? logger)
        {
            this.callOptions = callOptions;
            this.logger = logger;
        }

        public async IAsyncEnumerable<TResponse> InvokeAsync<TResponse>(
            string operation,
            Func<CallOptions, AsyncServerStreamingCall<TResponse>> call,
            Func<TResponse, Error?> errorSelector,
            DateTime? deadline,
            [EnumeratorCancellation] CancellationToken cancellationToken)
            where TResponse : class
        {
            var options = callOptions.Create(deadline, cancellationToken);
            using var streamingCall = call(options);
            var responseStream = streamingCall.ResponseStream;

            while (true)
            {
                TResponse current;
                try
                {
                    if (!await responseStream.MoveNext(cancellationToken).ConfigureAwait(false))
                    {
                        yield break;
                    }

                    current = responseStream.Current;
                }
                catch (RpcException exception)
                {
                    var error = Mt5GrpcErrorMapper.FromRpcException(operation, exception);
                    if (exception.StatusCode == StatusCode.DeadlineExceeded || exception.StatusCode == StatusCode.Cancelled)
                    {
                        logger.DeadlineOrCancellation(operation, exception.StatusCode);
                    }

                    logger.CallFailure(error);
                    throw new Mt5GrpcClientException(error);
                }
                catch (OperationCanceledException exception)
                {
                    var error = Mt5GrpcErrorMapper.FromCancellation(operation, exception);
                    logger.DeadlineOrCancellation(operation, StatusCode.Cancelled);
                    logger.CallFailure(error);
                    throw new Mt5GrpcClientException(error);
                }

                // In-band terminal error frame (FR-009): surface the mapped error and
                // end the stream, consistent with the unary path's error handling.
                var mt5Error = Mt5GrpcErrorMapper.FromMt5Error(operation, errorSelector(current));
                if (mt5Error != null)
                {
                    logger.Mt5ErrorPayload(mt5Error);
                    throw new Mt5GrpcClientException(mt5Error);
                }

                yield return current;
            }
        }
    }
}
