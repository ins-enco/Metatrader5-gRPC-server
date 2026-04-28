using System;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using Metatrader.V1;

namespace MetaTrader.Grpc.Client
{
    internal sealed class Mt5GrpcUnaryInvoker
    {
        private readonly Mt5GrpcCallOptions callOptions;
        private readonly ILogger? logger;

        public Mt5GrpcUnaryInvoker(Mt5GrpcCallOptions callOptions, ILogger? logger)
        {
            this.callOptions = callOptions;
            this.logger = logger;
        }

        public async Task<Mt5GrpcResult<TResponse>> InvokeAsync<TResponse>(
            string operation,
            Func<CallOptions, AsyncUnaryCall<TResponse>> call,
            Func<TResponse, Error?> errorSelector,
            DateTime? deadline,
            CancellationToken cancellationToken)
            where TResponse : class
        {
            try
            {
                var options = callOptions.Create(deadline, cancellationToken);
                var response = await call(options).ResponseAsync.ConfigureAwait(false);
                var mt5Error = Mt5GrpcErrorMapper.FromMt5Error(operation, errorSelector(response));
                if (mt5Error != null)
                {
                    logger.Mt5ErrorPayload(mt5Error);
                    return Mt5GrpcResult<TResponse>.Failure(mt5Error);
                }

                return Mt5GrpcResult<TResponse>.Success(response);
            }
            catch (RpcException exception)
            {
                var error = Mt5GrpcErrorMapper.FromRpcException(operation, exception);
                if (exception.StatusCode == StatusCode.DeadlineExceeded || exception.StatusCode == StatusCode.Cancelled)
                {
                    logger.DeadlineOrCancellation(operation, exception.StatusCode);
                }

                logger.CallFailure(error);
                return Mt5GrpcResult<TResponse>.Failure(error);
            }
            catch (OperationCanceledException exception)
            {
                var error = Mt5GrpcErrorMapper.FromCancellation(operation, exception);
                logger.DeadlineOrCancellation(operation, StatusCode.Cancelled);
                logger.CallFailure(error);
                return Mt5GrpcResult<TResponse>.Failure(error);
            }
            catch (Exception exception)
            {
                var error = Mt5GrpcErrorMapper.FromException(operation, exception);
                logger.CallFailure(error);
                return Mt5GrpcResult<TResponse>.Failure(error);
            }
        }
    }
}
