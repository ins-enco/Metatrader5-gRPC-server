using System;
using System.Threading;
using System.Threading.Tasks;
using Metatrader.V1;

namespace MetaTrader.Grpc.Client
{
    public sealed partial class Mt5GrpcClient
    {
        public Task<Mt5GrpcResult<SymbolsTotalResponse>> GetSymbolsTotalAsync(
            SymbolsTotalRequest? request = null,
            DateTime? deadline = null,
            CancellationToken cancellationToken = default)
        {
            return invoker.InvokeAsync(
                "SymbolsService.GetSymbolsTotal",
                options => Symbols.GetSymbolsTotalAsync(request ?? new SymbolsTotalRequest(), options),
                response => response.Error,
                deadline,
                cancellationToken);
        }

        public Task<Mt5GrpcResult<SymbolsGetResponse>> GetSymbolsAsync(
            SymbolsGetRequest? request = null,
            DateTime? deadline = null,
            CancellationToken cancellationToken = default)
        {
            return invoker.InvokeAsync(
                "SymbolsService.GetSymbols",
                options => Symbols.GetSymbolsAsync(request ?? new SymbolsGetRequest(), options),
                response => response.Error,
                deadline,
                cancellationToken);
        }

        public Task<Mt5GrpcResult<SymbolSelectResponse>> SelectSymbolAsync(
            SymbolSelectRequest? request = null,
            DateTime? deadline = null,
            CancellationToken cancellationToken = default)
        {
            return invoker.InvokeAsync(
                "SymbolsService.SelectSymbol",
                options => Symbols.SelectSymbolAsync(request ?? new SymbolSelectRequest(), options),
                response => response.Error,
                deadline,
                cancellationToken);
        }

        public Task<Mt5GrpcResult<SymbolInfoResponse>> GetSymbolInfoAsync(
            SymbolInfoRequest? request = null,
            DateTime? deadline = null,
            CancellationToken cancellationToken = default)
        {
            return invoker.InvokeAsync(
                "SymbolInfoService.GetSymbolInfo",
                options => SymbolInfo.GetSymbolInfoAsync(request ?? new SymbolInfoRequest(), options),
                response => response.Error,
                deadline,
                cancellationToken);
        }

        public Task<Mt5GrpcResult<SymbolInfoTickResponse>> GetSymbolInfoTickAsync(
            SymbolInfoTickRequest? request = null,
            DateTime? deadline = null,
            CancellationToken cancellationToken = default)
        {
            return invoker.InvokeAsync(
                "SymbolInfoTickService.GetSymbolInfoTick",
                options => SymbolInfoTick.GetSymbolInfoTickAsync(request ?? new SymbolInfoTickRequest(), options),
                response => response.Error,
                deadline,
                cancellationToken);
        }

        public Task<Mt5GrpcResult<CopyRatesFromResponse>> CopyRatesFromAsync(
            CopyRatesFromRequest? request = null,
            DateTime? deadline = null,
            CancellationToken cancellationToken = default)
        {
            return invoker.InvokeAsync(
                "MarketDataService.CopyRatesFrom",
                options => MarketData.CopyRatesFromAsync(request ?? new CopyRatesFromRequest(), options),
                response => response.Error,
                deadline,
                cancellationToken);
        }

        public Task<Mt5GrpcResult<CopyRatesFromPosResponse>> CopyRatesFromPosAsync(
            CopyRatesFromPosRequest? request = null,
            DateTime? deadline = null,
            CancellationToken cancellationToken = default)
        {
            return invoker.InvokeAsync(
                "MarketDataService.CopyRatesFromPos",
                options => MarketData.CopyRatesFromPosAsync(request ?? new CopyRatesFromPosRequest(), options),
                response => response.Error,
                deadline,
                cancellationToken);
        }

        public Task<Mt5GrpcResult<CopyRatesRangeResponse>> CopyRatesRangeAsync(
            CopyRatesRangeRequest? request = null,
            DateTime? deadline = null,
            CancellationToken cancellationToken = default)
        {
            return invoker.InvokeAsync(
                "MarketDataService.CopyRatesRange",
                options => MarketData.CopyRatesRangeAsync(request ?? new CopyRatesRangeRequest(), options),
                response => response.Error,
                deadline,
                cancellationToken);
        }

        public Task<Mt5GrpcResult<CopyTicksFromResponse>> CopyTicksFromAsync(
            CopyTicksFromRequest? request = null,
            DateTime? deadline = null,
            CancellationToken cancellationToken = default)
        {
            return invoker.InvokeAsync(
                "MarketDataService.CopyTicksFrom",
                options => MarketData.CopyTicksFromAsync(request ?? new CopyTicksFromRequest(), options),
                response => response.Error,
                deadline,
                cancellationToken);
        }

        public Task<Mt5GrpcResult<CopyTicksRangeResponse>> CopyTicksRangeAsync(
            CopyTicksRangeRequest? request = null,
            DateTime? deadline = null,
            CancellationToken cancellationToken = default)
        {
            return invoker.InvokeAsync(
                "MarketDataService.CopyTicksRange",
                options => MarketData.CopyTicksRangeAsync(request ?? new CopyTicksRangeRequest(), options),
                response => response.Error,
                deadline,
                cancellationToken);
        }

        public Task<Mt5GrpcResult<MarketBookAddResponse>> AddMarketBookAsync(
            MarketBookAddRequest? request = null,
            DateTime? deadline = null,
            CancellationToken cancellationToken = default)
        {
            return invoker.InvokeAsync(
                "MarketBookService.AddMarketBook",
                options => MarketBook.AddMarketBookAsync(request ?? new MarketBookAddRequest(), options),
                response => response.Error,
                deadline,
                cancellationToken);
        }

        public Task<Mt5GrpcResult<MarketBookGetResponse>> GetMarketBookAsync(
            MarketBookGetRequest? request = null,
            DateTime? deadline = null,
            CancellationToken cancellationToken = default)
        {
            return invoker.InvokeAsync(
                "MarketBookService.GetMarketBook",
                options => MarketBook.GetMarketBookAsync(request ?? new MarketBookGetRequest(), options),
                response => response.Error,
                deadline,
                cancellationToken);
        }

        public Task<Mt5GrpcResult<MarketBookReleaseResponse>> ReleaseMarketBookAsync(
            MarketBookReleaseRequest? request = null,
            DateTime? deadline = null,
            CancellationToken cancellationToken = default)
        {
            return invoker.InvokeAsync(
                "MarketBookService.ReleaseMarketBook",
                options => MarketBook.ReleaseMarketBookAsync(request ?? new MarketBookReleaseRequest(), options),
                response => response.Error,
                deadline,
                cancellationToken);
        }
    }
}
