using System;
using Grpc.Core;
using Grpc.Net.Client;

namespace MetaTrader.Grpc.Client
{
    public static class Mt5GrpcClientFactory
    {
        public static GrpcChannel CreateChannel(Mt5GrpcClientOptions options)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            if (options.Address == null)
            {
                throw new ArgumentException("Address is required.", nameof(options));
            }

            AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);

            var address = ResolveAddress(options);
            options.LoggerFactory?.CreateLogger("MetaTrader.Grpc.Client")
                .ConnectionAttempt(address);

            var channelOptions = new GrpcChannelOptions
            {
                HttpHandler = options.HttpHandler,
                LoggerFactory = options.LoggerFactory,
                MaxSendMessageSize = options.MaxSendMessageSize,
                MaxReceiveMessageSize = options.MaxReceiveMessageSize
            };

            return GrpcChannel.ForAddress(address, channelOptions);
        }

        public static TClient CreateClient<TClient>(GrpcChannel channel)
            where TClient : ClientBase<TClient>
        {
            if (channel == null)
            {
                throw new ArgumentNullException(nameof(channel));
            }

            var constructor = typeof(TClient).GetConstructor(new[] { typeof(ChannelBase) });
            if (constructor == null)
            {
                throw new Mt5GrpcClientException($"Client type {typeof(TClient).FullName} does not expose a ChannelBase constructor.");
            }

            return (TClient)constructor.Invoke(new object[] { channel });
        }

        public static Mt5GrpcClient Create(Mt5GrpcClientOptions options)
        {
            var channel = CreateChannel(options);
            return new Mt5GrpcClient(channel, options, ownsChannel: true);
        }

        private static string ResolveAddress(Mt5GrpcClientOptions options)
        {
            var address = options.Address!;
            if (options.TlsOptions != null && string.Equals(address.Scheme, "http", StringComparison.OrdinalIgnoreCase))
            {
                var builder = new UriBuilder(address)
                {
                    Scheme = Uri.UriSchemeHttps
                };
                return builder.Uri.ToString();
            }

            return address.ToString();
        }
    }
}
