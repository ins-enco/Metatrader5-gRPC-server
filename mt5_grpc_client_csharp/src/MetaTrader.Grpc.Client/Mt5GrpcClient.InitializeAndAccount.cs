using System;
using System.Threading;
using System.Threading.Tasks;
using Metatrader.V1;

namespace MetaTrader.Grpc.Client
{
    public sealed partial class Mt5GrpcClient
    {
        public Task<Mt5GrpcResult<Error>> ConnectAsync(
            ConnectRequest? request = null,
            DateTime? deadline = null,
            CancellationToken cancellationToken = default)
        {
            return invoker.InvokeAsync(
                "MetaTraderService.Connect",
                options => MetaTrader.ConnectAsync(request ?? new ConnectRequest(), options),
                response => response,
                deadline,
                cancellationToken);
        }

        public Task<Mt5GrpcResult<GetLastErrorResponse>> GetLastErrorAsync(
            Empty? request = null,
            DateTime? deadline = null,
            CancellationToken cancellationToken = default)
        {
            return invoker.InvokeAsync(
                "MetaTraderService.GetLastError",
                options => MetaTrader.GetLastErrorAsync(request ?? new Empty(), options),
                response => response.Error,
                deadline,
                cancellationToken);
        }

        public Task<Mt5GrpcResult<LoginResponse>> LoginAsync(
            LoginRequest? request = null,
            DateTime? deadline = null,
            CancellationToken cancellationToken = default)
        {
            return invoker.InvokeAsync(
                "InitializeService.Login",
                options => Initialize.LoginAsync(request ?? new LoginRequest(), options),
                response => response.Error,
                deadline,
                cancellationToken);
        }

        public Task<Mt5GrpcResult<ShutdownResponse>> ShutdownAsync(
            ShutdownRequest? request = null,
            DateTime? deadline = null,
            CancellationToken cancellationToken = default)
        {
            return invoker.InvokeAsync(
                "InitializeService.Shutdown",
                options => Initialize.ShutdownAsync(request ?? new ShutdownRequest(), options),
                response => response.Error,
                deadline,
                cancellationToken);
        }

        public Task<Mt5GrpcResult<VersionResponse>> GetVersionAsync(
            VersionRequest? request = null,
            DateTime? deadline = null,
            CancellationToken cancellationToken = default)
        {
            return invoker.InvokeAsync(
                "InitializeService.GetVersion",
                options => Initialize.GetVersionAsync(request ?? new VersionRequest(), options),
                response => response.Error,
                deadline,
                cancellationToken);
        }

        public Task<Mt5GrpcResult<TerminalInfoResponse>> GetTerminalInfoAsync(
            TerminalInfoRequest? request = null,
            DateTime? deadline = null,
            CancellationToken cancellationToken = default)
        {
            return invoker.InvokeAsync(
                "TerminalInfoService.GetTerminalInfo",
                options => TerminalInfo.GetTerminalInfoAsync(request ?? new TerminalInfoRequest(), options),
                response => response.Error,
                deadline,
                cancellationToken);
        }

        public Task<Mt5GrpcResult<AccountInfoResponse>> GetAccountInfoAsync(
            AccountInfoRequest? request = null,
            DateTime? deadline = null,
            CancellationToken cancellationToken = default)
        {
            return invoker.InvokeAsync(
                "AccountInfoService.GetAccountInfo",
                options => AccountInfo.GetAccountInfoAsync(request ?? new AccountInfoRequest(), options),
                response => response.Error,
                deadline,
                cancellationToken);
        }
    }
}
