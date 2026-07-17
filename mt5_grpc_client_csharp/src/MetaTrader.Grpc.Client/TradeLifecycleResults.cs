using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Metatrader.V1;

namespace MetaTrader.Grpc.Client
{
    public enum TradeLifecycleOperation
    {
        Open = 0,
        Close = 1,
        ModifyPosition = 2,
        ModifyPendingOrder = 3,
        CloseBy = 4,
        CloseOrder = 5
    }

    public enum TradeExecutionStatus
    {
        Completed = 0,
        PartiallyCompleted = 1,
        AcceptedOrPlaced = 2,
        RejectedOrFailed = 3,
        Unknown = 4
    }

    public enum MultipleCloseByStatus
    {
        Completed = 0,
        ValidationFailed = 1,
        DiscoveryFailed = 2,
        RefreshFailed = 3,
        Cancelled = 4,
        DeadlineExceeded = 5
    }

    public enum PairAttemptState
    {
        Attempted = 0,
        Unattempted = 1
    }

    public enum PositionRemainderReason
    {
        NoOpposite = 0,
        BecameIneligible = 1,
        WithheldAfterPair = 2,
        UnattemptedAfterStop = 3,
        MissingFromRefresh = 4,
        InvalidSnapshot = 5
    }

    public sealed class TradeOperationResult
    {
        public TradeOperationResult(
            TradeLifecycleOperation operation,
            Mt5GrpcResult<OrderSendResponse> callResult,
            TradeExecutionStatus? executionStatus)
        {
            Operation = operation;
            CallResult = callResult ?? throw new ArgumentNullException(nameof(callResult));
            ExecutionStatus = executionStatus;
        }

        public TradeLifecycleOperation Operation { get; }
        public Mt5GrpcResult<OrderSendResponse> CallResult { get; }
        public TradeExecutionStatus? ExecutionStatus { get; }
        public int? RawRetcode
        {
            get { return CallResult.Value?.TradeResult?.Retcode; }
        }
    }

    public sealed class CloseByPairOutcome
    {
        public CloseByPairOutcome(
            int pairIndex,
            long positionTicket,
            long oppositePositionTicket,
            PairAttemptState attemptState,
            TradeOperationResult? operationResult)
        {
            PairIndex = pairIndex;
            PositionTicket = positionTicket;
            OppositePositionTicket = oppositePositionTicket;
            AttemptState = attemptState;
            OperationResult = operationResult;
        }

        public int PairIndex { get; }
        public long PositionTicket { get; }
        public long OppositePositionTicket { get; }
        public PairAttemptState AttemptState { get; }
        public TradeOperationResult? OperationResult { get; }
    }

    public sealed class PositionRemainder
    {
        public PositionRemainder(long ticket, double? lastKnownVolume, PositionRemainderReason reason)
        {
            Ticket = ticket;
            LastKnownVolume = lastKnownVolume;
            Reason = reason;
        }

        public long Ticket { get; }
        public double? LastKnownVolume { get; }
        public PositionRemainderReason Reason { get; }
    }

    public sealed class MultipleCloseByResult
    {
        public MultipleCloseByResult(
            MultipleCloseByStatus status,
            Mt5GrpcError? batchError,
            IEnumerable<long> frozenTickets,
            IEnumerable<CloseByPairOutcome> pairs,
            IEnumerable<PositionRemainder> remainders)
        {
            Status = status;
            BatchError = batchError;
            FrozenTickets = Copy(frozenTickets, nameof(frozenTickets));
            Pairs = Copy(pairs, nameof(pairs));
            Remainders = Copy(remainders, nameof(remainders));
        }

        public MultipleCloseByStatus Status { get; }
        public Mt5GrpcError? BatchError { get; }
        public IReadOnlyList<long> FrozenTickets { get; }
        public IReadOnlyList<CloseByPairOutcome> Pairs { get; }
        public IReadOnlyList<PositionRemainder> Remainders { get; }

        private static IReadOnlyList<T> Copy<T>(IEnumerable<T> source, string parameterName)
        {
            if (source == null)
            {
                throw new ArgumentNullException(parameterName);
            }

            return new ReadOnlyCollection<T>(source.ToList());
        }
    }
}
