using System;
using System.Net.Http;
using Grpc.Core;
using Microsoft.Extensions.Logging;

namespace MetaTrader.Grpc.Client
{
    public sealed class Mt5GrpcClientOptions
    {
        public Uri? Address { get; set; }

        public Mt5GrpcTlsOptions? TlsOptions { get; set; }

        public TimeSpan? DefaultDeadline { get; set; }

        public int? MaxSendMessageSize { get; set; }

        public int? MaxReceiveMessageSize { get; set; }

        public ILoggerFactory? LoggerFactory { get; set; }

        public HttpMessageHandler? HttpHandler { get; set; }

        public Metadata? DefaultHeaders { get; set; }
    }

    public sealed class Mt5GrpcTlsOptions
    {
        private Mt5GrpcTlsOptions(bool useSystemTrust)
        {
            UseSystemTrust = useSystemTrust;
        }

        public bool UseSystemTrust { get; }

        public static Mt5GrpcTlsOptions SystemTrust()
        {
            return new Mt5GrpcTlsOptions(useSystemTrust: true);
        }
    }
}
