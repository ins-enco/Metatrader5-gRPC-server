using System;
using System.Linq;
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
    }
}
