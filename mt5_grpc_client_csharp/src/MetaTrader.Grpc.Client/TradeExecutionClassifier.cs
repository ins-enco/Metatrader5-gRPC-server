using System;
using Metatrader.V1;

namespace MetaTrader.Grpc.Client
{
    internal static class TradeExecutionClassifier
    {
        public static TradeExecutionStatus Classify(
            TradeLifecycleOperation operation,
            OrderSendResponse response)
        {
            if (response == null)
            {
                throw new ArgumentNullException(nameof(response));
            }

            // The operation is intentionally part of the classifier contract so
            // future operation-specific MT5 return-code semantics remain additive.
            _ = operation;

            if (response.TradeResult == null)
            {
                return TradeExecutionStatus.Unknown;
            }

            switch (response.TradeResult.Retcode)
            {
                case 10009:
                    return TradeExecutionStatus.Completed;
                case 10010:
                    return TradeExecutionStatus.PartiallyCompleted;
                case 10008:
                case 10028:
                    return TradeExecutionStatus.AcceptedOrPlaced;

                case 10004:
                case 10006:
                case 10007:
                case 10011:
                case 10012:
                case 10013:
                case 10014:
                case 10015:
                case 10016:
                case 10017:
                case 10018:
                case 10019:
                case 10020:
                case 10021:
                case 10022:
                case 10023:
                case 10024:
                case 10025:
                case 10026:
                case 10027:
                case 10029:
                case 10030:
                case 10031:
                case 10032:
                case 10033:
                case 10034:
                case 10035:
                case 10036:
                case 10038:
                case 10039:
                case 10040:
                case 10041:
                case 10042:
                case 10043:
                case 10044:
                case 10045:
                case 10046:
                    return TradeExecutionStatus.RejectedOrFailed;
                default:
                    return TradeExecutionStatus.Unknown;
            }
        }
    }
}
