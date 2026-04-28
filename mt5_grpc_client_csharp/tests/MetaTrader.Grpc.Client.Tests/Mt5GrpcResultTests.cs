using System;
using Grpc.Core;
using Metatrader.V1;
using Xunit;

namespace MetaTrader.Grpc.Client.Tests
{
    public sealed class Mt5GrpcResultTests
    {
        [Fact]
        public void Success_wraps_value()
        {
            var value = new AccountInfoResponse();

            var result = Mt5GrpcResult<AccountInfoResponse>.Success(value);

            Assert.True(result.IsSuccess);
            Assert.Same(value, result.Value);
            Assert.Null(result.Error);
        }

        [Fact]
        public void Failure_wraps_error()
        {
            var error = new Mt5GrpcError { Operation = "op", Message = "failed" };

            var result = Mt5GrpcResult<AccountInfoResponse>.Failure(error);

            Assert.False(result.IsSuccess);
            Assert.Null(result.Value);
            Assert.Same(error, result.Error);
        }

        [Fact]
        public void Mapper_preserves_mt5_error_payload()
        {
            var error = Mt5GrpcErrorMapper.FromMt5Error("AccountInfoService.GetAccountInfo", new Error { Code = 100, Message = "bad account" });

            Assert.NotNull(error);
            Assert.Equal(100, error!.Mt5ErrorCode);
            Assert.Equal("bad account", error.Mt5ErrorMessage);
        }

        [Fact]
        public void Mapper_preserves_rpc_status_failure()
        {
            var exception = new RpcException(new Status(StatusCode.Unavailable, "down"));

            var error = Mt5GrpcErrorMapper.FromRpcException("op", exception);

            Assert.Equal(StatusCode.Unavailable, error.StatusCode);
            Assert.Same(exception, error.Exception);
        }

        [Fact]
        public void Mapper_preserves_cancellation_failure()
        {
            var exception = new OperationCanceledException();

            var error = Mt5GrpcErrorMapper.FromCancellation("op", exception);

            Assert.Equal(StatusCode.Cancelled, error.StatusCode);
        }
    }
}
