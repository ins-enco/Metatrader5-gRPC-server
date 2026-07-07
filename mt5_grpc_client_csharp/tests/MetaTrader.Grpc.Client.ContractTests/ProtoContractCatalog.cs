using System.Collections.Generic;

namespace MetaTrader.Grpc.Client.ContractTests
{
    internal static class ProtoContractCatalog
    {
        public static IReadOnlyDictionary<string, string[]> UnaryServices { get; } =
            new Dictionary<string, string[]>
            {
                ["MetaTraderService"] = new[] { "Connect", "GetLastError" },
                ["InitializeService"] = new[] { "Login", "Shutdown", "GetVersion" },
                ["TerminalInfoService"] = new[] { "GetTerminalInfo" },
                ["AccountInfoService"] = new[] { "GetAccountInfo" },
                ["SymbolsService"] = new[] { "GetSymbolsTotal", "GetSymbols", "SelectSymbol" },
                ["SymbolInfoService"] = new[] { "GetSymbolInfo" },
                ["SymbolInfoTickService"] = new[] { "GetSymbolInfoTick" },
                ["MarketDataService"] = new[] { "CopyRatesFrom", "CopyRatesFromPos", "CopyRatesRange", "CopyTicksFrom", "CopyTicksRange" },
                ["MarketBookService"] = new[] { "AddMarketBook", "GetMarketBook", "ReleaseMarketBook" },
                ["OrdersService"] = new[] { "GetOrders", "GetOrdersTotal" },
                ["OrderCheckService"] = new[] { "CheckOrder" },
                ["OrderCalcService"] = new[] { "CalcMargin", "CalcProfit" },
                ["OrderSendService"] = new[] { "SendOrder" },
                ["PositionsService"] = new[] { "GetPositions", "GetPositionsTotal" },
                ["HistoryOrdersService"] = new[] { "GetHistoryOrders", "GetHistoryOrdersTotal" },
                ["TradeHistoryService"] = new[] { "GetDeals" }
            };

        // Server-streaming services (the first is TradeEventsService). Kept separate
        // from UnaryServices so the unary contract counts remain unchanged (SC-005).
        public static IReadOnlyDictionary<string, string[]> StreamingServices { get; } =
            new Dictionary<string, string[]>
            {
                ["TradeEventsService"] = new[] { "SubscribeTradeTransactions" }
            };
    }
}
