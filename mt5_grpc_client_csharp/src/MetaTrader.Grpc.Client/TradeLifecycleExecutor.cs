using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using Metatrader.V1;

namespace MetaTrader.Grpc.Client
{
    internal sealed class TradeLifecycleExecutor
    {
        internal const string BatchOperationName = "TradeLifecycle.ClosePositionsBy";

        private readonly Func<OrderSendRequest, DateTime?, CancellationToken, Task<Mt5GrpcResult<OrderSendResponse>>> sendOrder;
        private readonly Func<PositionsGetRequest, DateTime?, CancellationToken, Task<Mt5GrpcResult<PositionsGetResponse>>> getPositions;
        private readonly TimeSpan? defaultDeadline;
        private readonly ILogger? logger;
        private readonly Func<DateTime> utcNow;

        public TradeLifecycleExecutor(
            Func<OrderSendRequest, DateTime?, CancellationToken, Task<Mt5GrpcResult<OrderSendResponse>>> sendOrder,
            Func<PositionsGetRequest, DateTime?, CancellationToken, Task<Mt5GrpcResult<PositionsGetResponse>>> getPositions,
            TimeSpan? defaultDeadline = null,
            ILogger? logger = null,
            Func<DateTime>? utcNow = null)
        {
            this.sendOrder = sendOrder ?? throw new ArgumentNullException(nameof(sendOrder));
            this.getPositions = getPositions ?? throw new ArgumentNullException(nameof(getPositions));
            this.defaultDeadline = defaultDeadline;
            this.logger = logger;
            this.utcNow = utcNow ?? (() => DateTime.UtcNow);
        }

        internal DateTime? CaptureEffectiveDeadline(DateTime? explicitDeadline)
        {
            if (explicitDeadline.HasValue || !defaultDeadline.HasValue)
            {
                return explicitDeadline;
            }

            return utcNow().Add(defaultDeadline.Value);
        }

        internal Task<TradeOperationResult> OpenOrderAsync(
            OpenOrderRequest? request,
            DateTime? deadline,
            CancellationToken cancellationToken)
        {
            if (request == null)
            {
                return Task.FromResult(ValidationFailure(
                    TradeLifecycleOperation.Open,
                    "An open-order request is required."));
            }

            var symbol = request.Symbol;
            var type = request.Type;
            var volume = request.Volume;
            var price = request.Price;
            var stopLimitPrice = request.StopLimitPrice;
            var stopLoss = request.StopLoss;
            var takeProfit = request.TakeProfit;
            var deviation = request.Deviation;
            var fillingPolicy = request.FillingPolicy;
            var timePolicy = request.TimePolicy;
            var expiration = CloneTimestamp(request.Expiration);
            var magic = request.Magic;
            var comment = request.Comment;

            if (string.IsNullOrWhiteSpace(symbol))
            {
                return Task.FromResult(ValidationFailure(
                    TradeLifecycleOperation.Open,
                    "Open-order symbol must not be blank."));
            }

            if (!IsFinite(volume) || volume <= 0)
            {
                return Task.FromResult(ValidationFailure(
                    TradeLifecycleOperation.Open,
                    "Open-order volume must be positive and finite."));
            }

            var isMarket = type == ENUM_ORDER_TYPE.OrderTypeBuy || type == ENUM_ORDER_TYPE.OrderTypeSell;
            var isPending = IsPendingOrderType(type);
            if (!isMarket && !isPending)
            {
                return Task.FromResult(ValidationFailure(
                    TradeLifecycleOperation.Open,
                    "Open-order type must be BUY, SELL, or a supported pending-order type."));
            }

            if (!IsFinite(price) || !IsFinite(stopLimitPrice) || !IsFinite(stopLoss) || !IsFinite(takeProfit))
            {
                return Task.FromResult(ValidationFailure(
                    TradeLifecycleOperation.Open,
                    "Open-order prices and protection values must be finite when supplied."));
            }

            if (isPending && !price.HasValue)
            {
                return Task.FromResult(ValidationFailure(
                    TradeLifecycleOperation.Open,
                    "A pending open order requires a price."));
            }

            var isStopLimit = type == ENUM_ORDER_TYPE.OrderTypeBuyStopLimit ||
                              type == ENUM_ORDER_TYPE.OrderTypeSellStopLimit;
            if (isStopLimit && !stopLimitPrice.HasValue)
            {
                return Task.FromResult(ValidationFailure(
                    TradeLifecycleOperation.Open,
                    "A stop-limit pending order requires a stop-limit price."));
            }

            var timeValidation = ValidateTimePolicy(timePolicy, expiration);
            if (timeValidation != null)
            {
                return Task.FromResult(ValidationFailure(TradeLifecycleOperation.Open, timeValidation));
            }

            if (!IsFillingPolicy(fillingPolicy))
            {
                return Task.FromResult(ValidationFailure(
                    TradeLifecycleOperation.Open,
                    "Open-order filling policy is not supported by the current contract."));
            }

            var tradeRequest = new TradeRequest
            {
                Action = isMarket
                    ? ENUM_TRADE_REQUEST_ACTIONS.TradeActionDeal
                    : ENUM_TRADE_REQUEST_ACTIONS.TradeActionPending,
                Symbol = symbol,
                Type = type,
                Volume = volume,
                Deviation = deviation,
                TypeFilling = fillingPolicy,
                TypeTime = timePolicy,
                Magic = magic
            };
            SetOptionalValues(tradeRequest, price, stopLimitPrice, stopLoss, takeProfit, expiration, comment);

            return SendAsync(
                TradeLifecycleOperation.Open,
                tradeRequest,
                deadline,
                cancellationToken);
        }

        internal Task<TradeOperationResult> ClosePositionAsync(
            ClosePositionRequest? request,
            DateTime? deadline,
            CancellationToken cancellationToken)
        {
            if (request == null)
            {
                return Task.FromResult(ValidationFailure(
                    TradeLifecycleOperation.Close,
                    "A close-position request is required."));
            }

            var positionTicket = request.PositionTicket;
            var symbol = request.Symbol;
            var side = request.Side;
            var currentVolume = request.CurrentVolume;
            var requestedVolume = request.Volume;
            var price = request.Price;
            var deviation = request.Deviation;
            var fillingPolicy = request.FillingPolicy;
            var magic = request.Magic;
            var comment = request.Comment;

            if (positionTicket <= 0)
            {
                return Task.FromResult(ValidationFailure(
                    TradeLifecycleOperation.Close,
                    "Position ticket must be greater than zero."));
            }

            if (string.IsNullOrWhiteSpace(symbol))
            {
                return Task.FromResult(ValidationFailure(
                    TradeLifecycleOperation.Close,
                    "Close-position symbol must not be blank."));
            }

            if (side != PositionSide.Buy && side != PositionSide.Sell)
            {
                return Task.FromResult(ValidationFailure(
                    TradeLifecycleOperation.Close,
                    "Position side must be Buy or Sell."));
            }

            if (!IsFinite(currentVolume) || currentVolume <= 0)
            {
                return Task.FromResult(ValidationFailure(
                    TradeLifecycleOperation.Close,
                    "Current position volume must be positive and finite."));
            }

            var volume = requestedVolume ?? currentVolume;
            if (!IsFinite(volume) || volume <= 0 || volume > currentVolume)
            {
                return Task.FromResult(ValidationFailure(
                    TradeLifecycleOperation.Close,
                    "Close volume must be positive, finite, and no greater than current volume."));
            }

            if (!IsFinite(price))
            {
                return Task.FromResult(ValidationFailure(
                    TradeLifecycleOperation.Close,
                    "Close execution price must be finite when supplied."));
            }

            if (!IsFillingPolicy(fillingPolicy))
            {
                return Task.FromResult(ValidationFailure(
                    TradeLifecycleOperation.Close,
                    "Close filling policy is not supported by the current contract."));
            }

            var tradeRequest = new TradeRequest
            {
                Action = ENUM_TRADE_REQUEST_ACTIONS.TradeActionDeal,
                Position = positionTicket,
                Symbol = symbol,
                Type = side == PositionSide.Buy
                    ? ENUM_ORDER_TYPE.OrderTypeSell
                    : ENUM_ORDER_TYPE.OrderTypeBuy,
                Volume = volume,
                Deviation = deviation,
                TypeFilling = fillingPolicy,
                Magic = magic
            };
            if (price.HasValue)
            {
                tradeRequest.Price = price.Value;
            }

            if (comment != null)
            {
                tradeRequest.Comment = comment;
            }

            return SendAsync(
                TradeLifecycleOperation.Close,
                tradeRequest,
                deadline,
                cancellationToken);
        }

        internal Task<TradeOperationResult> ModifyTradeAsync(
            ModifyTradeRequest? request,
            DateTime? deadline,
            CancellationToken cancellationToken)
        {
            if (request == null)
            {
                return Task.FromResult(ValidationFailure(
                    TradeLifecycleOperation.ModifyPosition,
                    "A modify-trade request is required."));
            }

            var position = request.Position;
            var pendingOrder = request.PendingOrder;
            if ((position == null) == (pendingOrder == null))
            {
                return Task.FromResult(ValidationFailure(
                    TradeLifecycleOperation.ModifyPosition,
                    "A modify-trade request must identify exactly one position or pending order."));
            }

            if (position != null)
            {
                var ticket = position.PositionTicket;
                var stopLoss = position.StopLoss;
                var takeProfit = position.TakeProfit;
                if (ticket <= 0)
                {
                    return Task.FromResult(ValidationFailure(
                        TradeLifecycleOperation.ModifyPosition,
                        "Position ticket must be greater than zero."));
                }

                if (!IsFinite(stopLoss) || !IsFinite(takeProfit))
                {
                    return Task.FromResult(ValidationFailure(
                        TradeLifecycleOperation.ModifyPosition,
                        "Final position stop-loss and take-profit values must be finite."));
                }

                return SendAsync(
                    TradeLifecycleOperation.ModifyPosition,
                    new TradeRequest
                    {
                        Action = ENUM_TRADE_REQUEST_ACTIONS.TradeActionSltp,
                        Position = ticket,
                        Sl = stopLoss,
                        Tp = takeProfit
                    },
                    deadline,
                    cancellationToken);
            }

            var orderTicket = pendingOrder!.OrderTicket;
            var price = pendingOrder.Price;
            var stopLimitPrice = pendingOrder.StopLimitPrice;
            var pendingStopLoss = pendingOrder.StopLoss;
            var pendingTakeProfit = pendingOrder.TakeProfit;
            var timePolicy = pendingOrder.TimePolicy;
            var expiration = CloneTimestamp(pendingOrder.Expiration);

            if (orderTicket <= 0)
            {
                return Task.FromResult(ValidationFailure(
                    TradeLifecycleOperation.ModifyPendingOrder,
                    "Pending-order ticket must be greater than zero."));
            }

            if (!IsFinite(price) ||
                !IsFinite(stopLimitPrice) ||
                !IsFinite(pendingStopLoss) ||
                !IsFinite(pendingTakeProfit))
            {
                return Task.FromResult(ValidationFailure(
                    TradeLifecycleOperation.ModifyPendingOrder,
                    "Final pending-order price and protection values must be finite."));
            }

            var timeValidation = ValidateTimePolicy(timePolicy, expiration);
            if (timeValidation != null)
            {
                return Task.FromResult(ValidationFailure(
                    TradeLifecycleOperation.ModifyPendingOrder,
                    timeValidation));
            }

            var tradeRequest = new TradeRequest
            {
                Action = ENUM_TRADE_REQUEST_ACTIONS.TradeActionModify,
                Order = orderTicket,
                Price = price,
                Stoplimit = stopLimitPrice,
                Sl = pendingStopLoss,
                Tp = pendingTakeProfit,
                TypeTime = timePolicy
            };
            if (expiration != null)
            {
                tradeRequest.Expiration = expiration;
            }

            return SendAsync(
                TradeLifecycleOperation.ModifyPendingOrder,
                tradeRequest,
                deadline,
                cancellationToken);
        }

        internal Task<TradeOperationResult> ClosePositionByAsync(
            CloseByRequest? request,
            DateTime? deadline,
            CancellationToken cancellationToken)
        {
            if (request == null)
            {
                return Task.FromResult(ValidationFailure(
                    TradeLifecycleOperation.CloseBy,
                    "A close-by request is required."));
            }

            var positionTicket = request.PositionTicket;
            var oppositePositionTicket = request.OppositePositionTicket;
            var magic = request.Magic;
            var comment = request.Comment;

            if (positionTicket <= 0 || oppositePositionTicket <= 0)
            {
                return Task.FromResult(ValidationFailure(
                    TradeLifecycleOperation.CloseBy,
                    "Both close-by position tickets must be greater than zero."));
            }

            if (positionTicket == oppositePositionTicket)
            {
                return Task.FromResult(ValidationFailure(
                    TradeLifecycleOperation.CloseBy,
                    "Close-by position tickets must be distinct."));
            }

            var tradeRequest = new TradeRequest
            {
                Action = ENUM_TRADE_REQUEST_ACTIONS.TradeActionCloseBy,
                Position = positionTicket,
                PositionBy = oppositePositionTicket,
                Magic = magic
            };
            if (comment != null)
            {
                tradeRequest.Comment = comment;
            }

            return SendAsync(
                TradeLifecycleOperation.CloseBy,
                tradeRequest,
                deadline,
                cancellationToken);
        }

        internal async Task<MultipleCloseByResult> ClosePositionsByAsync(
            ClosePositionsByRequest? request,
            DateTime? deadline,
            CancellationToken cancellationToken)
        {
            if (request == null)
            {
                return BatchValidationFailure("A multiple close-by request is required.");
            }

            var symbol = request.Symbol;
            var magic = request.Magic;
            var comment = request.Comment;
            if (string.IsNullOrWhiteSpace(symbol))
            {
                return BatchValidationFailure("Multiple close-by symbol must not be blank.");
            }

            var effectiveDeadline = CaptureEffectiveDeadline(deadline);
            var preCallStop = GetLocalStop(effectiveDeadline, cancellationToken);
            if (preCallStop != null)
            {
                return EmptyTerminalResult(preCallStop.Value.Status, preCallStop.Value.Error);
            }

            var discovery = await QueryPositionsAsync(
                symbol,
                effectiveDeadline,
                cancellationToken).ConfigureAwait(false);
            if (!discovery.IsSuccess)
            {
                return EmptyTerminalResult(
                    StatusFromError(discovery.Error, MultipleCloseByStatus.DiscoveryFailed),
                    discovery.Error);
            }

            var discovered = discovery.Value!.Positions
                .Where(position => IsEligiblePosition(position, symbol, magic))
                .GroupBy(position => position.Ticket)
                .Where(group => group.Count() == 1)
                .Select(group => ClonePosition(group.Single()))
                .OrderBy(position => position.Time.Seconds)
                .ThenBy(position => position.Time.Nanos)
                .ThenBy(position => position.Ticket)
                .ToList();
            var frozenTickets = discovered.Select(position => position.Ticket).ToList();
            var active = discovered.ToDictionary(position => position.Ticket, ClonePosition);
            var attemptedTickets = new HashSet<long>();
            var withheld = new HashSet<long>();
            var pairs = new List<CloseByPairOutcome>();
            var remainderMap = new Dictionary<long, PositionRemainder>();

            if (!HasOppositePair(active.Values))
            {
                AddNoOppositeRemainders(active.Values, remainderMap);
                return BuildBatchResult(
                    MultipleCloseByStatus.Completed,
                    null,
                    frozenTickets,
                    pairs,
                    remainderMap);
            }

            while (HasOppositePair(active.Values))
            {
                var stop = GetLocalStop(effectiveDeadline, cancellationToken);
                if (stop != null)
                {
                    MaterializeTerminalWork(active, attemptedTickets, pairs, remainderMap);
                    return BuildBatchResult(
                        stop.Value.Status,
                        stop.Value.Error,
                        frozenTickets,
                        pairs,
                        remainderMap);
                }

                var refresh = await QueryPositionsAsync(
                    symbol,
                    effectiveDeadline,
                    cancellationToken).ConfigureAwait(false);
                if (!refresh.IsSuccess)
                {
                    var status = StatusFromError(refresh.Error, MultipleCloseByStatus.RefreshFailed);
                    if (status == MultipleCloseByStatus.Cancelled || status == MultipleCloseByStatus.DeadlineExceeded)
                    {
                        MaterializeTerminalWork(active, attemptedTickets, pairs, remainderMap);
                    }
                    else
                    {
                        AddStoppedRemainders(active, attemptedTickets, remainderMap);
                    }

                    return BuildBatchResult(
                        status,
                        refresh.Error,
                        frozenTickets,
                        pairs,
                        remainderMap);
                }

                RefreshActiveSet(
                    refresh.Value!.Positions,
                    symbol,
                    magic,
                    active,
                    attemptedTickets,
                    withheld,
                    remainderMap);

                var orderedBuys = OrderedSide(active.Values, type: 0);
                var orderedSells = OrderedSide(active.Values, type: 1);
                if (orderedBuys.Count == 0 || orderedSells.Count == 0)
                {
                    AddNoOppositeRemainders(active.Values, remainderMap);
                    break;
                }

                stop = GetLocalStop(effectiveDeadline, cancellationToken);
                if (stop != null)
                {
                    MaterializeTerminalWork(active, attemptedTickets, pairs, remainderMap);
                    return BuildBatchResult(
                        stop.Value.Status,
                        stop.Value.Error,
                        frozenTickets,
                        pairs,
                        remainderMap);
                }

                var buy = orderedBuys[0];
                var sell = orderedSells[0];
                var operationResult = await ClosePositionByAsync(
                    new CloseByRequest(buy.Ticket, sell.Ticket)
                    {
                        Magic = magic ?? 0,
                        Comment = comment
                    },
                    effectiveDeadline,
                    cancellationToken).ConfigureAwait(false);
                var outcome = new CloseByPairOutcome(
                    pairs.Count + 1,
                    buy.Ticket,
                    sell.Ticket,
                    PairAttemptState.Attempted,
                    operationResult);
                pairs.Add(outcome);
                logger.CloseByBatchItemStatus(
                    outcome.PairIndex,
                    outcome.AttemptState,
                    operationResult.ExecutionStatus);
                attemptedTickets.Add(buy.Ticket);
                attemptedTickets.Add(sell.Ticket);

                var sendTerminalStatus = StatusFromError(operationResult.CallResult.Error, MultipleCloseByStatus.Completed);
                if (sendTerminalStatus == MultipleCloseByStatus.Cancelled ||
                    sendTerminalStatus == MultipleCloseByStatus.DeadlineExceeded)
                {
                    Withhold(buy, active, withheld, remainderMap);
                    Withhold(sell, active, withheld, remainderMap);
                    MaterializeTerminalWork(active, attemptedTickets, pairs, remainderMap);
                    return BuildBatchResult(
                        sendTerminalStatus,
                        operationResult.CallResult.Error,
                        frozenTickets,
                        pairs,
                        remainderMap);
                }

                if (operationResult.ExecutionStatus != TradeExecutionStatus.Completed &&
                    operationResult.ExecutionStatus != TradeExecutionStatus.PartiallyCompleted)
                {
                    Withhold(buy, active, withheld, remainderMap);
                    Withhold(sell, active, withheld, remainderMap);
                }
            }

            AddNoOppositeRemainders(active.Values, remainderMap);
            return BuildBatchResult(
                MultipleCloseByStatus.Completed,
                null,
                frozenTickets,
                pairs,
                remainderMap);
        }

        internal static Timestamp? CloneTimestamp(Timestamp? value)
        {
            return value?.Clone();
        }

        internal static Position ClonePosition(Position value)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            return value.Clone();
        }

        internal TradeOperationResult ValidationFailure(
            TradeLifecycleOperation operation,
            string message)
        {
            var error = new Mt5GrpcError
            {
                Operation = OperationName(operation),
                Message = message
            };

            var result = new TradeOperationResult(
                operation,
                Mt5GrpcResult<OrderSendResponse>.Failure(error),
                executionStatus: null);
            Log(result);
            return result;
        }

        internal MultipleCloseByResult BatchValidationFailure(string message)
        {
            return new MultipleCloseByResult(
                MultipleCloseByStatus.ValidationFailed,
                new Mt5GrpcError { Operation = BatchOperationName, Message = message },
                Array.Empty<long>(),
                Array.Empty<CloseByPairOutcome>(),
                Array.Empty<PositionRemainder>());
        }

        internal async Task<TradeOperationResult> SendAsync(
            TradeLifecycleOperation operation,
            TradeRequest tradeRequest,
            DateTime? deadline,
            CancellationToken cancellationToken)
        {
            if (tradeRequest == null)
            {
                throw new ArgumentNullException(nameof(tradeRequest));
            }

            var callResult = await sendOrder(
                new OrderSendRequest { TradeRequest = tradeRequest },
                deadline,
                cancellationToken).ConfigureAwait(false);

            var executionStatus = callResult.Value == null
                ? (TradeExecutionStatus?)null
                : TradeExecutionClassifier.Classify(operation, callResult.Value);
            var result = new TradeOperationResult(operation, callResult, executionStatus);
            Log(result);
            return result;
        }

        internal Task<Mt5GrpcResult<PositionsGetResponse>> QueryPositionsAsync(
            string symbol,
            DateTime? deadline,
            CancellationToken cancellationToken)
        {
            return getPositions(
                new PositionsGetRequest { Symbol = symbol },
                deadline,
                cancellationToken);
        }

        internal static string OperationName(TradeLifecycleOperation operation)
        {
            return "TradeLifecycle." + operation;
        }

        internal static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        private static bool IsFinite(double? value)
        {
            return !value.HasValue || IsFinite(value.Value);
        }

        private static bool IsPendingOrderType(ENUM_ORDER_TYPE type)
        {
            switch (type)
            {
                case ENUM_ORDER_TYPE.OrderTypeBuyLimit:
                case ENUM_ORDER_TYPE.OrderTypeSellLimit:
                case ENUM_ORDER_TYPE.OrderTypeBuyStop:
                case ENUM_ORDER_TYPE.OrderTypeSellStop:
                case ENUM_ORDER_TYPE.OrderTypeBuyStopLimit:
                case ENUM_ORDER_TYPE.OrderTypeSellStopLimit:
                    return true;
                default:
                    return false;
            }
        }

        private static bool IsFillingPolicy(ENUM_ORDER_TYPE_FILLING value)
        {
            return value == ENUM_ORDER_TYPE_FILLING.OrderFillingFok ||
                   value == ENUM_ORDER_TYPE_FILLING.OrderFillingIoc ||
                   value == ENUM_ORDER_TYPE_FILLING.OrderFillingReturn;
        }

        private static string? ValidateTimePolicy(ENUM_ORDER_TYPE_TIME timePolicy, Timestamp? expiration)
        {
            var requiresExpiration = timePolicy == ENUM_ORDER_TYPE_TIME.OrderTimeSpecified ||
                                     timePolicy == ENUM_ORDER_TYPE_TIME.OrderTimeSpecifiedDay;
            var forbidsExpiration = timePolicy == ENUM_ORDER_TYPE_TIME.OrderTimeGtc ||
                                    timePolicy == ENUM_ORDER_TYPE_TIME.OrderTimeDay;

            if (!requiresExpiration && !forbidsExpiration)
            {
                return "Order time policy is not supported by the current contract.";
            }

            if (requiresExpiration && expiration == null)
            {
                return "The selected order time policy requires an expiration.";
            }

            if (forbidsExpiration && expiration != null)
            {
                return "The selected order time policy does not accept an expiration.";
            }

            if (expiration != null)
            {
                try
                {
                    _ = expiration.ToDateTime();
                }
                catch (InvalidOperationException)
                {
                    return "Order expiration is outside the valid protobuf timestamp range.";
                }
            }

            return null;
        }

        private static void SetOptionalValues(
            TradeRequest tradeRequest,
            double? price,
            double? stopLimitPrice,
            double? stopLoss,
            double? takeProfit,
            Timestamp? expiration,
            string? comment)
        {
            if (price.HasValue)
            {
                tradeRequest.Price = price.Value;
            }

            if (stopLimitPrice.HasValue)
            {
                tradeRequest.Stoplimit = stopLimitPrice.Value;
            }

            if (stopLoss.HasValue)
            {
                tradeRequest.Sl = stopLoss.Value;
            }

            if (takeProfit.HasValue)
            {
                tradeRequest.Tp = takeProfit.Value;
            }

            if (expiration != null)
            {
                tradeRequest.Expiration = expiration;
            }

            if (comment != null)
            {
                tradeRequest.Comment = comment;
            }
        }

        private static bool IsEligiblePosition(Position? position, string symbol, int? magic)
        {
            if (position == null ||
                position.Ticket <= 0 ||
                !string.Equals(position.Symbol, symbol, StringComparison.Ordinal) ||
                (magic.HasValue && position.Magic != magic.Value) ||
                (position.Type != 0 && position.Type != 1) ||
                !IsFinite(position.Volume) ||
                position.Volume <= 0 ||
                position.Time == null)
            {
                return false;
            }

            try
            {
                _ = position.Time.ToDateTime();
                return true;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        private static bool HasOppositePair(IEnumerable<Position> positions)
        {
            var hasBuy = false;
            var hasSell = false;
            foreach (var position in positions)
            {
                hasBuy |= position.Type == 0;
                hasSell |= position.Type == 1;
                if (hasBuy && hasSell)
                {
                    return true;
                }
            }

            return false;
        }

        private static List<Position> OrderedSide(IEnumerable<Position> positions, int type)
        {
            return positions
                .Where(position => position.Type == type)
                .OrderBy(position => position.Time.Seconds)
                .ThenBy(position => position.Time.Nanos)
                .ThenBy(position => position.Ticket)
                .ToList();
        }

        private static void RefreshActiveSet(
            IEnumerable<Position> refreshedPositions,
            string symbol,
            int? magic,
            IDictionary<long, Position> active,
            ISet<long> attemptedTickets,
            ISet<long> withheld,
            IDictionary<long, PositionRemainder> remainderMap)
        {
            var byTicket = refreshedPositions
                .Where(position => position != null && active.ContainsKey(position.Ticket))
                .GroupBy(position => position.Ticket)
                .ToDictionary(group => group.Key, group => group.ToList());

            foreach (var ticket in active.Keys.ToList())
            {
                if (withheld.Contains(ticket))
                {
                    active.Remove(ticket);
                    continue;
                }

                if (!byTicket.TryGetValue(ticket, out var matches) || matches.Count == 0)
                {
                    var prior = active[ticket];
                    active.Remove(ticket);
                    if (!attemptedTickets.Contains(ticket))
                    {
                        remainderMap[ticket] = new PositionRemainder(
                            ticket,
                            prior.Volume,
                            PositionRemainderReason.MissingFromRefresh);
                    }

                    continue;
                }

                if (matches.Count != 1 || !IsEligiblePosition(matches[0], symbol, magic))
                {
                    var invalid = matches[0];
                    active.Remove(ticket);
                    remainderMap[ticket] = new PositionRemainder(
                        ticket,
                        IsFinite(invalid.Volume) ? invalid.Volume : (double?)null,
                        matches.Count == 1
                            ? PositionRemainderReason.BecameIneligible
                            : PositionRemainderReason.InvalidSnapshot);
                    continue;
                }

                active[ticket] = ClonePosition(matches[0]);
            }
        }

        private static void Withhold(
            Position position,
            IDictionary<long, Position> active,
            ISet<long> withheld,
            IDictionary<long, PositionRemainder> remainderMap)
        {
            withheld.Add(position.Ticket);
            active.Remove(position.Ticket);
            remainderMap[position.Ticket] = new PositionRemainder(
                position.Ticket,
                position.Volume,
                PositionRemainderReason.WithheldAfterPair);
        }

        private void MaterializeTerminalWork(
            IDictionary<long, Position> active,
            ISet<long> attemptedTickets,
            IList<CloseByPairOutcome> pairs,
            IDictionary<long, PositionRemainder> remainderMap)
        {
            var available = active.Values
                .Where(position => !attemptedTickets.Contains(position.Ticket))
                .ToList();
            var buys = OrderedSide(available, type: 0);
            var sells = OrderedSide(available, type: 1);
            var paired = new HashSet<long>();
            var pairCount = Math.Min(buys.Count, sells.Count);
            for (var index = 0; index < pairCount; index++)
            {
                var outcome = new CloseByPairOutcome(
                    pairs.Count + 1,
                    buys[index].Ticket,
                    sells[index].Ticket,
                    PairAttemptState.Unattempted,
                    null);
                pairs.Add(outcome);
                paired.Add(buys[index].Ticket);
                paired.Add(sells[index].Ticket);
                logger.CloseByBatchItemStatus(outcome.PairIndex, outcome.AttemptState, null);
            }

            foreach (var position in active.Values)
            {
                if (!paired.Contains(position.Ticket) && !remainderMap.ContainsKey(position.Ticket))
                {
                    remainderMap[position.Ticket] = new PositionRemainder(
                        position.Ticket,
                        position.Volume,
                        PositionRemainderReason.UnattemptedAfterStop);
                }
            }
        }

        private static void AddStoppedRemainders(
            IDictionary<long, Position> active,
            ISet<long> attemptedTickets,
            IDictionary<long, PositionRemainder> remainderMap)
        {
            foreach (var position in active.Values)
            {
                if (!attemptedTickets.Contains(position.Ticket) && !remainderMap.ContainsKey(position.Ticket))
                {
                    remainderMap[position.Ticket] = new PositionRemainder(
                        position.Ticket,
                        position.Volume,
                        PositionRemainderReason.UnattemptedAfterStop);
                }
            }
        }

        private static void AddNoOppositeRemainders(
            IEnumerable<Position> positions,
            IDictionary<long, PositionRemainder> remainderMap)
        {
            foreach (var position in positions)
            {
                if (!remainderMap.ContainsKey(position.Ticket))
                {
                    remainderMap[position.Ticket] = new PositionRemainder(
                        position.Ticket,
                        position.Volume,
                        PositionRemainderReason.NoOpposite);
                }
            }
        }

        private static MultipleCloseByResult BuildBatchResult(
            MultipleCloseByStatus status,
            Mt5GrpcError? error,
            IEnumerable<long> frozenTickets,
            IEnumerable<CloseByPairOutcome> pairs,
            IDictionary<long, PositionRemainder> remainderMap)
        {
            var frozen = frozenTickets.ToList();
            var remainders = frozen
                .Where(remainderMap.ContainsKey)
                .Select(ticket => remainderMap[ticket])
                .ToList();
            return new MultipleCloseByResult(status, error, frozen, pairs, remainders);
        }

        private static MultipleCloseByStatus StatusFromError(
            Mt5GrpcError? error,
            MultipleCloseByStatus fallback)
        {
            if (error?.StatusCode == StatusCode.Cancelled)
            {
                return MultipleCloseByStatus.Cancelled;
            }

            if (error?.StatusCode == StatusCode.DeadlineExceeded)
            {
                return MultipleCloseByStatus.DeadlineExceeded;
            }

            return fallback;
        }

        private (MultipleCloseByStatus Status, Mt5GrpcError Error)? GetLocalStop(
            DateTime? deadline,
            CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return (
                    MultipleCloseByStatus.Cancelled,
                    new Mt5GrpcError
                    {
                        Operation = BatchOperationName,
                        StatusCode = StatusCode.Cancelled,
                        Message = "The multiple close-by operation was cancelled."
                    });
            }

            if (deadline.HasValue && deadline.Value.ToUniversalTime() <= utcNow().ToUniversalTime())
            {
                return (
                    MultipleCloseByStatus.DeadlineExceeded,
                    new Mt5GrpcError
                    {
                        Operation = BatchOperationName,
                        StatusCode = StatusCode.DeadlineExceeded,
                        Message = "The multiple close-by deadline was exceeded."
                    });
            }

            return null;
        }

        private static MultipleCloseByResult EmptyTerminalResult(
            MultipleCloseByStatus status,
            Mt5GrpcError? error)
        {
            return new MultipleCloseByResult(
                status,
                error,
                Array.Empty<long>(),
                Array.Empty<CloseByPairOutcome>(),
                Array.Empty<PositionRemainder>());
        }

        private void Log(TradeOperationResult result)
        {
            logger.LifecycleOperationStatus(
                result.Operation,
                result.CallResult.IsSuccess,
                result.ExecutionStatus,
                result.RawRetcode);
        }
    }
}
