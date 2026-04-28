using System;

namespace MetaTrader.Grpc.Client.Tests.Fixtures
{
    public sealed class GrpcTestServerFixture : IDisposable
    {
        public Uri Address { get; } = new Uri("http://localhost:50051");

        public void Dispose()
        {
        }
    }
}
