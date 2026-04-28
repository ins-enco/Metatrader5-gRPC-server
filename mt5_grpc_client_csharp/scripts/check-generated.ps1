param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$root = Resolve-Path (Join-Path $PSScriptRoot "..")
$project = Join-Path $root "src/MetaTrader.Grpc.Client/MetaTrader.Grpc.Client.csproj"

dotnet build $project -c $Configuration --no-restore

if ($LASTEXITCODE -ne 0) {
    throw "Generated C# bindings are stale or failed to build from protos/*.proto."
}

Write-Host "Generated C# bindings match the current proto inputs."
