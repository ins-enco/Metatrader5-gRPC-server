using System;
using Google.Protobuf.WellKnownTypes;
using Metatrader.V1;

namespace MetaTrader.Grpc.Client.ContractTests
{
    internal static class MarketDataResponseFixtures
    {
        public static CopyRatesFromResponse CreateRatesResponse(int count)
        {
            var response = new CopyRatesFromResponse();
            for (var i = 0; i < count; i++)
            {
                response.Rates.Add(new Rate
                {
                    Time = Timestamp.FromDateTime(DateTime.SpecifyKind(new DateTime(2026, 1, 1).AddMinutes(i), DateTimeKind.Utc)),
                    Open = 1.10 + i * 0.01,
                    High = 1.20 + i * 0.01,
                    Low = 1.00 + i * 0.01,
                    Close = 1.15 + i * 0.01,
                    TickVolume = 1000 + i,
                    Spread = 2,
                    RealVolume = 900 + i
                });
            }

            return response;
        }
    }
}
