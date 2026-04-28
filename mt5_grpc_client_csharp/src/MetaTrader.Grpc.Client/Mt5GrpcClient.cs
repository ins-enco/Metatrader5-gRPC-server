using System;
using Grpc.Net.Client;
using Microsoft.Extensions.Logging;
using Metatrader.V1;

namespace MetaTrader.Grpc.Client
{
    public sealed partial class Mt5GrpcClient : IDisposable
    {
        private readonly GrpcChannel channel;
        private readonly bool ownsChannel;
        private readonly Mt5GrpcUnaryInvoker invoker;

        internal Mt5GrpcClient(GrpcChannel channel, Mt5GrpcClientOptions options, bool ownsChannel)
        {
            this.channel = channel ?? throw new ArgumentNullException(nameof(channel));
            this.ownsChannel = ownsChannel;

            var callOptions = new Mt5GrpcCallOptions(options.DefaultDeadline, options.DefaultHeaders);
            var logger = options.LoggerFactory?.CreateLogger<Mt5GrpcClient>();
            invoker = new Mt5GrpcUnaryInvoker(callOptions, logger);

            MetaTrader = new MetaTraderService.MetaTraderServiceClient(channel);
            Initialize = new InitializeService.InitializeServiceClient(channel);
            TerminalInfo = new TerminalInfoService.TerminalInfoServiceClient(channel);
            AccountInfo = new AccountInfoService.AccountInfoServiceClient(channel);
            Symbols = new SymbolsService.SymbolsServiceClient(channel);
            SymbolInfo = new SymbolInfoService.SymbolInfoServiceClient(channel);
            SymbolInfoTick = new SymbolInfoTickService.SymbolInfoTickServiceClient(channel);
            MarketData = new MarketDataService.MarketDataServiceClient(channel);
            MarketBook = new MarketBookService.MarketBookServiceClient(channel);
            Orders = new OrdersService.OrdersServiceClient(channel);
            OrderCheck = new OrderCheckService.OrderCheckServiceClient(channel);
            OrderCalc = new OrderCalcService.OrderCalcServiceClient(channel);
            OrderSend = new OrderSendService.OrderSendServiceClient(channel);
            Positions = new PositionsService.PositionsServiceClient(channel);
            HistoryOrders = new HistoryOrdersService.HistoryOrdersServiceClient(channel);
            TradeHistory = new TradeHistoryService.TradeHistoryServiceClient(channel);
        }

        public MetaTraderService.MetaTraderServiceClient MetaTrader { get; }
        public InitializeService.InitializeServiceClient Initialize { get; }
        public TerminalInfoService.TerminalInfoServiceClient TerminalInfo { get; }
        public AccountInfoService.AccountInfoServiceClient AccountInfo { get; }
        public SymbolsService.SymbolsServiceClient Symbols { get; }
        public SymbolInfoService.SymbolInfoServiceClient SymbolInfo { get; }
        public SymbolInfoTickService.SymbolInfoTickServiceClient SymbolInfoTick { get; }
        public MarketDataService.MarketDataServiceClient MarketData { get; }
        public MarketBookService.MarketBookServiceClient MarketBook { get; }
        public OrdersService.OrdersServiceClient Orders { get; }
        public OrderCheckService.OrderCheckServiceClient OrderCheck { get; }
        public OrderCalcService.OrderCalcServiceClient OrderCalc { get; }
        public OrderSendService.OrderSendServiceClient OrderSend { get; }
        public PositionsService.PositionsServiceClient Positions { get; }
        public HistoryOrdersService.HistoryOrdersServiceClient HistoryOrders { get; }
        public TradeHistoryService.TradeHistoryServiceClient TradeHistory { get; }

        public void Dispose()
        {
            if (ownsChannel)
            {
                channel.Dispose();
            }
        }
    }
}
