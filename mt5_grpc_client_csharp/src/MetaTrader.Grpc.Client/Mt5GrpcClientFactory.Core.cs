#if NETFRAMEWORK
using System;
using System.Collections.Generic;
using Grpc.Core;
using Microsoft.Extensions.Logging;

namespace MetaTrader.Grpc.Client
{
    // .NET Framework-only factory built on the legacy Grpc.Core (C-core) channel.
    // Grpc.Core ships its own native HTTP/2 implementation, so it works on
    // Windows 10 and earlier where GrpcChannel + WinHttpHandler throws
    // "the current version of Windows doesn't support HTTP/2 features required by gRPC".
    // Modern targets keep using Mt5GrpcClientFactory.Create (GrpcChannel).
    public static partial class Mt5GrpcClientFactory
    {
        /// <summary>
        /// Builds a native <see cref="Channel"/> from <paramref name="options"/>. Use
        /// this on .NET Framework 4.8 / Windows 10 where the HTTP/2 GrpcChannel path
        /// is unavailable. Only the <c>Address</c>, message-size and logging options
        /// are honoured; the <see cref="Mt5GrpcClientOptions.HttpHandler"/> is ignored
        /// because Grpc.Core does not use <c>HttpMessageHandler</c>.
        /// </summary>
        public static Channel CreateCoreChannel(Mt5GrpcClientOptions options)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            if (options.Address == null)
            {
                throw new ArgumentException("Address is required.", nameof(options));
            }

            var address = options.Address;
            var isSecure = string.Equals(address.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
                || options.TlsOptions != null;
            var port = address.IsDefaultPort ? (isSecure ? 443 : 80) : address.Port;

            options.LoggerFactory?.CreateLogger("MetaTrader.Grpc.Client")
                .ConnectionAttempt($"{(isSecure ? Uri.UriSchemeHttps : Uri.UriSchemeHttp)}://{address.Host}:{port}");

            var credentials = isSecure ? new SslCredentials() : ChannelCredentials.Insecure;

            var channelOptions = new List<ChannelOption>();
            if (options.MaxSendMessageSize.HasValue)
            {
                channelOptions.Add(new ChannelOption(ChannelOptions.MaxSendMessageLength, options.MaxSendMessageSize.Value));
            }

            if (options.MaxReceiveMessageSize.HasValue)
            {
                channelOptions.Add(new ChannelOption(ChannelOptions.MaxReceiveMessageLength, options.MaxReceiveMessageSize.Value));
            }

            return new Channel(address.Host, port, credentials, channelOptions);
        }

        /// <summary>
        /// Creates a fully-featured <see cref="Mt5GrpcClient"/> backed by a native
        /// Grpc.Core channel. The returned client owns the channel and tears it down
        /// (ShutdownAsync) on <see cref="Mt5GrpcClient.Dispose"/>.
        /// </summary>
        public static Mt5GrpcClient CreateCore(Mt5GrpcClientOptions options)
        {
            var channel = CreateCoreChannel(options);
            return new Mt5GrpcClient(channel, options, ownsChannel: true);
        }
    }
}
#endif
