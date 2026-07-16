using Metatrader.V1;
using Xunit;

namespace MetaTrader.Grpc.Client.Tests
{
    public sealed class TradeExecutionClassifierTests
    {
        [Theory]
        [InlineData(10009, TradeExecutionStatus.Completed)]
        [InlineData(10010, TradeExecutionStatus.PartiallyCompleted)]
        [InlineData(10008, TradeExecutionStatus.AcceptedOrPlaced)]
        [InlineData(10028, TradeExecutionStatus.AcceptedOrPlaced)]
        public void Classifies_success_and_accepted_codes(int retcode, TradeExecutionStatus expected)
        {
            Assert.Equal(expected, Classify(retcode));
        }

        [Theory]
        [InlineData(10004)]
        [InlineData(10006)]
        [InlineData(10007)]
        [InlineData(10011)]
        [InlineData(10012)]
        [InlineData(10013)]
        [InlineData(10014)]
        [InlineData(10015)]
        [InlineData(10016)]
        [InlineData(10017)]
        [InlineData(10018)]
        [InlineData(10019)]
        [InlineData(10020)]
        [InlineData(10021)]
        [InlineData(10022)]
        [InlineData(10023)]
        [InlineData(10024)]
        [InlineData(10025)]
        [InlineData(10026)]
        [InlineData(10027)]
        [InlineData(10029)]
        [InlineData(10030)]
        [InlineData(10031)]
        [InlineData(10032)]
        [InlineData(10033)]
        [InlineData(10034)]
        [InlineData(10035)]
        [InlineData(10036)]
        [InlineData(10038)]
        [InlineData(10039)]
        [InlineData(10040)]
        [InlineData(10041)]
        [InlineData(10042)]
        [InlineData(10043)]
        [InlineData(10044)]
        [InlineData(10045)]
        [InlineData(10046)]
        public void Classifies_every_documented_rejection_code(int retcode)
        {
            Assert.Equal(TradeExecutionStatus.RejectedOrFailed, Classify(retcode));
        }

        [Fact]
        public void Missing_trade_result_is_unknown()
        {
            var response = new OrderSendResponse();

            Assert.Equal(
                TradeExecutionStatus.Unknown,
                TradeExecutionClassifier.Classify(TradeLifecycleOperation.Open, response));
        }

        [Fact]
        public void Future_retcode_is_unknown_and_preserved_exactly()
        {
            var response = Response(19999);
            var result = new TradeOperationResult(
                TradeLifecycleOperation.CloseBy,
                Mt5GrpcResult<OrderSendResponse>.Success(response),
                TradeExecutionClassifier.Classify(TradeLifecycleOperation.CloseBy, response));

            Assert.Equal(TradeExecutionStatus.Unknown, result.ExecutionStatus);
            Assert.Equal(19999, result.RawRetcode);
            Assert.Same(response, result.CallResult.Value);
        }

        [Theory]
        [InlineData(TradeLifecycleOperation.Open)]
        [InlineData(TradeLifecycleOperation.Close)]
        [InlineData(TradeLifecycleOperation.ModifyPosition)]
        [InlineData(TradeLifecycleOperation.ModifyPendingOrder)]
        [InlineData(TradeLifecycleOperation.CloseBy)]
        public void Classification_accepts_every_lifecycle_operation(TradeLifecycleOperation operation)
        {
            Assert.Equal(
                TradeExecutionStatus.Completed,
                TradeExecutionClassifier.Classify(operation, Response(10009)));
        }

        private static TradeExecutionStatus Classify(int retcode)
        {
            return TradeExecutionClassifier.Classify(TradeLifecycleOperation.Open, Response(retcode));
        }

        private static OrderSendResponse Response(int retcode)
        {
            return new OrderSendResponse { TradeResult = new TradeResult { Retcode = retcode } };
        }
    }
}
