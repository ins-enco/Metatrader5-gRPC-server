using System;
using System.Threading;
using Grpc.Core;

namespace MetaTrader.Grpc.Client
{
    public sealed class Mt5GrpcCallOptions
    {
        public Mt5GrpcCallOptions(TimeSpan? defaultDeadline, Metadata? defaultHeaders)
        {
            DefaultDeadline = defaultDeadline;
            DefaultHeaders = defaultHeaders;
        }

        public TimeSpan? DefaultDeadline { get; }

        public Metadata? DefaultHeaders { get; }

        public CallOptions Create(DateTime? deadline, CancellationToken cancellationToken)
        {
            var effectiveDeadline = deadline;
            if (effectiveDeadline == null && DefaultDeadline.HasValue)
            {
                effectiveDeadline = DateTime.UtcNow.Add(DefaultDeadline.Value);
            }

            return new CallOptions(
                headers: DefaultHeaders,
                deadline: effectiveDeadline,
                cancellationToken: cancellationToken);
        }
    }
}
