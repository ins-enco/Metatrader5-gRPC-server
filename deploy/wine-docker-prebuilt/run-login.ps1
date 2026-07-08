<#
.SYNOPSIS
    Per-login launcher for the PREBUILT MT5 gRPC image (Windows hosts).

.DESCRIPTION
    Mirrors run-login.sh: brings up one isolated container per MT5 login, each
    with its own container name, host port, and writable layer, with NO mounted
    volume. Repeated invocations with distinct -Login/-Port scale to dozens of
    containers on one host (FR-016, FR-006, FR-007, FR-013,
    contracts/launcher-cli.md).

    Exit codes:
      0          container started
      2          missing/invalid arguments
      3          container name already exists
      non-zero   Docker failure (e.g. host port in use), surfaced verbatim
#>
[CmdletBinding()]
param(
    [string]$Login,
    [string]$Port,
    [string]$Password,
    [string]$Server,
    [string]$Name,
    [string]$Image = "ghcr.io/ins-enco/mt5-grpc-server-prebuilt:latest",
    [string]$Bind = "127.0.0.1",
    [string]$Verbose = "true",
    [string]$ShmSize = "1gb",
    [Alias("h")][switch]$Help
)

function Show-Usage {
    @"
Usage: run-login.ps1 -Login <LOGIN> -Port <HOST_PORT> [options]

Launch one isolated per-login container from the prebuilt MT5 gRPC image.

Required:
  -Login <LOGIN>      MT5 account login. Also names the container mt5-grpc-<LOGIN>
                      unless -Name is given.
  -Port <HOST_PORT>   Host port to publish (integer 1-65535). Published as
                      <BIND>:<HOST_PORT>:50051.

Options:
  -Password <PW>      MT5 password (redacted in logs). Default: unset.
  -Server <SRV>       MT5 broker server. Default: unset.
  -Name <NAME>        Container name. Default: mt5-grpc-<LOGIN>.
  -Image <REF>        Prebuilt image reference.
                      Default: ghcr.io/ins-enco/mt5-grpc-server-prebuilt:latest.
  -Bind <ADDR>        Host bind address. Default: 127.0.0.1. Pass 0.0.0.0
                      explicitly to expose the endpoint (FR-013).
  -Verbose <bool>     Sets GRPC_VERBOSE. Default: true.
  -ShmSize <SIZE>     Passed to docker run --shm-size. Default: 1gb.
  -h, -Help           Show this help and exit.

Exit codes: 0 started, 2 bad args, 3 name exists, non-zero Docker error.
"@
}

function Die {
    param([string]$Message, [int]$Code = 2)
    Write-Error "run-login.ps1: $Message"
    [Console]::Error.WriteLine("Try 'run-login.ps1 -Help' for usage.")
    exit $Code
}

if ($Help) { Show-Usage; exit 0 }

# Validation (launcher-cli.md behavior contract 1).
if ([string]::IsNullOrEmpty($Login)) { Die "-Login is required" }
if ([string]::IsNullOrEmpty($Port))  { Die "-Port is required" }
if ($Port -notmatch '^[0-9]+$') { Die "-Port must be a positive integer, got: $Port" }
$portNum = [int]$Port
if ($portNum -lt 1 -or $portNum -gt 65535) { Die "-Port must be in range 1-65535, got: $Port" }

if ([string]::IsNullOrEmpty($Name)) { $Name = "mt5-grpc-$Login" }

# Name uniqueness (behavior contract 2): refuse rather than clobber.
$existing = docker ps -a --format '{{.Names}}'
if ($existing -contains $Name) {
    Die "container named '$Name' already exists; remove it or pass -Name" 3
}

# Build the docker run arguments. NO volume is mounted (behavior contract 4).
$dockerArgs = @(
    "run", "-d",
    "--name", $Name,
    "--restart", "unless-stopped",
    "--shm-size", $ShmSize,
    "-p", "$($Bind):$($Port):50051",
    "-e", "MT5_LOGIN=$Login",
    "-e", "GRPC_HOST=0.0.0.0",
    "-e", "GRPC_PORT=50051",
    "-e", "GRPC_VERBOSE=$Verbose"
)
if (-not [string]::IsNullOrEmpty($Password)) { $dockerArgs += @("-e", "MT5_PASSWORD=$Password") }
if (-not [string]::IsNullOrEmpty($Server))   { $dockerArgs += @("-e", "MT5_SERVER=$Server") }
$dockerArgs += $Image

Write-Host "Launching '$Name' -> $($Bind):$($Port):50051 (login $Login)"
# Surface Docker's port-in-use / other errors verbatim (behavior contract 3).
& docker @dockerArgs
exit $LASTEXITCODE
