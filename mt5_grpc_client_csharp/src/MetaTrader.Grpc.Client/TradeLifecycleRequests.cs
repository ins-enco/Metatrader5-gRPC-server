using Google.Protobuf.WellKnownTypes;
using Metatrader.V1;

namespace MetaTrader.Grpc.Client
{
    public enum PositionSide
    {
        Buy = 0,
        Sell = 1
    }

    public sealed class OpenOrderRequest
    {
        public OpenOrderRequest(string symbol, ENUM_ORDER_TYPE type, double volume)
        {
            Symbol = symbol;
            Type = type;
            Volume = volume;
        }

        public string Symbol { get; }
        public ENUM_ORDER_TYPE Type { get; }
        public double Volume { get; }
        public double? Price { get; set; }
        public double? StopLimitPrice { get; set; }
        public double? StopLoss { get; set; }
        public double? TakeProfit { get; set; }
        public int Deviation { get; set; }
        public ENUM_ORDER_TYPE_FILLING FillingPolicy { get; set; }
        public ENUM_ORDER_TYPE_TIME TimePolicy { get; set; }
        public Timestamp? Expiration { get; set; }
        public int Magic { get; set; }
        public string? Comment { get; set; }
    }

    public sealed class ClosePositionRequest
    {
        public ClosePositionRequest(long positionTicket, string symbol, PositionSide side, double currentVolume)
        {
            PositionTicket = positionTicket;
            Symbol = symbol;
            Side = side;
            CurrentVolume = currentVolume;
        }

        public long PositionTicket { get; }
        public string Symbol { get; }
        public PositionSide Side { get; }
        public double CurrentVolume { get; }
        public double? Volume { get; set; }
        public double? Price { get; set; }
        public int Deviation { get; set; }
        public ENUM_ORDER_TYPE_FILLING FillingPolicy { get; set; }
        public int Magic { get; set; }
        public string? Comment { get; set; }
    }

    public sealed class ModifyTradeRequest
    {
        public ModifyTradeRequest(PositionModification position)
            : this(position, null)
        {
        }

        public ModifyTradeRequest(PendingOrderModification pendingOrder)
            : this(null, pendingOrder)
        {
        }

        public ModifyTradeRequest(PositionModification? position, PendingOrderModification? pendingOrder)
        {
            Position = position;
            PendingOrder = pendingOrder;
        }

        public PositionModification? Position { get; }
        public PendingOrderModification? PendingOrder { get; }
    }

    public sealed class PositionModification
    {
        public PositionModification(long positionTicket, double stopLoss, double takeProfit)
        {
            PositionTicket = positionTicket;
            StopLoss = stopLoss;
            TakeProfit = takeProfit;
        }

        public long PositionTicket { get; }
        public double StopLoss { get; }
        public double TakeProfit { get; }
    }

    public sealed class PendingOrderModification
    {
        public PendingOrderModification(
            long orderTicket,
            double price,
            double stopLimitPrice,
            double stopLoss,
            double takeProfit,
            ENUM_ORDER_TYPE_TIME timePolicy)
        {
            OrderTicket = orderTicket;
            Price = price;
            StopLimitPrice = stopLimitPrice;
            StopLoss = stopLoss;
            TakeProfit = takeProfit;
            TimePolicy = timePolicy;
        }

        public long OrderTicket { get; }
        public double Price { get; }
        public double StopLimitPrice { get; }
        public double StopLoss { get; }
        public double TakeProfit { get; }
        public ENUM_ORDER_TYPE_TIME TimePolicy { get; }
        public Timestamp? Expiration { get; set; }
    }

    public sealed class CloseByRequest
    {
        public CloseByRequest(long positionTicket, long oppositePositionTicket)
        {
            PositionTicket = positionTicket;
            OppositePositionTicket = oppositePositionTicket;
        }

        public long PositionTicket { get; }
        public long OppositePositionTicket { get; }
        public int Magic { get; set; }
        public string? Comment { get; set; }
    }

    public sealed class ClosePositionsByRequest
    {
        public ClosePositionsByRequest(string symbol)
        {
            Symbol = symbol;
        }

        public string Symbol { get; }
        public int? Magic { get; set; }
        public string? Comment { get; set; }
    }
}
