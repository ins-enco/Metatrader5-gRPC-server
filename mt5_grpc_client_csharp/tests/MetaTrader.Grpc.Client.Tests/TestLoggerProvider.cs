using System;
using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace MetaTrader.Grpc.Client.Tests
{
    internal sealed class TestLoggerProvider : ILoggerProvider
    {
        public ConcurrentQueue<string> Messages { get; } = new ConcurrentQueue<string>();

        public ILogger CreateLogger(string categoryName)
        {
            return new TestLogger(Messages);
        }

        public void Dispose()
        {
        }

        private sealed class TestLogger : ILogger
        {
            private readonly ConcurrentQueue<string> messages;

            public TestLogger(ConcurrentQueue<string> messages)
            {
                this.messages = messages;
            }

            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull
            {
                return NullScope.Instance;
            }

            public bool IsEnabled(LogLevel logLevel)
            {
                return true;
            }

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                messages.Enqueue(formatter(state, exception));
            }
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new NullScope();

            public void Dispose()
            {
            }
        }
    }
}
