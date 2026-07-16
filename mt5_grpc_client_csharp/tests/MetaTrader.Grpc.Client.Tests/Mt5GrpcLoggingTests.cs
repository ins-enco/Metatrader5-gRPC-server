using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Metatrader.V1;
using Xunit;

namespace MetaTrader.Grpc.Client.Tests
{
    public sealed class Mt5GrpcLoggingTests
    {
        [Fact]
        public void Factory_logs_connection_attempt_when_logger_factory_is_configured()
        {
            using var provider = new TestLoggerProvider();
            using var loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(provider));

            using var channel = Mt5GrpcClientFactory.CreateChannel(new Mt5GrpcClientOptions
            {
                Address = new Uri("http://localhost:50051"),
                LoggerFactory = loggerFactory
            });

            Assert.Contains(provider.Messages, message => message.Contains("Creating MT5 gRPC channel"));
        }

        [Fact]
        public void Error_mapper_exposes_mt5_error_payload_for_logging()
        {
            var error = Mt5GrpcErrorMapper.FromMt5Error("op", new Error { Code = 5, Message = "mt5 failed" });

            Assert.NotNull(error);
            Assert.Equal("op", error!.Operation);
            Assert.Equal("mt5 failed", error.Message);
        }

        [Fact]
        public async Task Lifecycle_logs_identify_operation_and_status_without_comment_or_payload()
        {
            using var provider = new TestLoggerProvider();
            using var loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(provider));
            var logger = loggerFactory.CreateLogger<Mt5GrpcClient>();
            var executor = new TradeLifecycleExecutor(
                (request, deadline, cancellationToken) => Task.FromResult(
                    Mt5GrpcResult<OrderSendResponse>.Success(new OrderSendResponse
                    {
                        TradeResult = new TradeResult { Retcode = 10009 }
                    })),
                (request, deadline, cancellationToken) => Task.FromResult(
                    Mt5GrpcResult<PositionsGetResponse>.Success(new PositionsGetResponse())),
                (request, deadline, cancellationToken) => Task.FromResult(
                    Mt5GrpcResult<SymbolInfoResponse>.Success(new SymbolInfoResponse
                    {
                        SymbolInfo = new SymbolInfo()
                    })),
                logger: logger);

            await executor.OpenOrderAsync(new OpenOrderRequest(
                "EURUSD", ENUM_ORDER_TYPE.OrderTypeBuy, 0.1)
            {
                Comment = "TOP-SECRET-COMMENT"
            }, null, CancellationToken.None);

            Assert.Contains(provider.Messages, message =>
                message.Contains("Open") && message.Contains("Completed"));
            Assert.DoesNotContain(provider.Messages, message => message.Contains("TOP-SECRET-COMMENT"));
            Assert.DoesNotContain(provider.Messages, message => message.Contains("TradeRequest"));
        }

        [Fact]
        public void Batch_item_logs_include_item_and_status_identity_only()
        {
            using var provider = new TestLoggerProvider();
            using var loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(provider));
            var logger = loggerFactory.CreateLogger<Mt5GrpcClient>();

            logger.CloseByBatchItemStatus(3, PairAttemptState.Attempted, TradeExecutionStatus.RejectedOrFailed);

            Assert.Contains(provider.Messages, message =>
                message.Contains("3") && message.Contains("Attempted") && message.Contains("RejectedOrFailed"));
            Assert.DoesNotContain(provider.Messages, message => message.Contains("password", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(provider.Messages, message => message.Contains("comment", StringComparison.OrdinalIgnoreCase));
        }
    }
}
