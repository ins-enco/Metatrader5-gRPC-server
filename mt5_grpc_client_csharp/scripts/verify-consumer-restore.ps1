<#
.SYNOPSIS
    Proves a clean consumer can add MetaTrader.Grpc.Client from a package source,
    restore, and build on both .NET Framework 4.8 and a modern .NET TFM with zero
    hand-added dependencies and no protobuf/gRPC code-generation step
    (Contract B; T006; SC-002, SC-003, SC-006).

.DESCRIPTION
    1. Packs the library into an isolated local folder "feed".
    2. Generates throwaway consumer projects (net48 + a modern net* TFM) whose ONLY
       client dependency is a single <PackageReference Include="MetaTrader.Grpc.Client">.
    3. Uses a consumer nuget.config that clears inherited sources and adds only the
       local feed + nuget.org, so restore is reproducible and independent of any
       machine-level feed.
    4. Asserts, for each consumer, that:
         * restore + build succeed,
         * the client's runtime dependencies (Google.Protobuf, Grpc.Core.Api,
           Grpc.Net.Client) flowed transitively into the build output - none added
           by hand (SC-002),
         * Grpc.Tools did NOT enter the consumer graph and no protobuf/gRPC code was
           generated in the consumer (SC-003).

    The net48 consumer additionally references the net48 reference-assemblies pack
    (build toolchain, PrivateAssets=all) and WinHttpHandler (the documented net48
    transport prerequisite, FR-004) - neither is a runtime dependency of the client.
#>
param(
    [string]$Configuration = "Release",
    [string]$ModernTfm     = "net9.0"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$root    = Resolve-Path (Join-Path $PSScriptRoot "..")
$project = Join-Path $root "src/MetaTrader.Grpc.Client/MetaTrader.Grpc.Client.csproj"

$work = Join-Path ([System.IO.Path]::GetTempPath()) ("mt5-consumer-" + [System.Guid]::NewGuid().ToString("N"))
$feed = Join-Path $work "feed"
New-Item -ItemType Directory -Path $feed -Force | Out-Null

$failures = New-Object System.Collections.Generic.List[string]

function New-ConsumerNugetConfig {
    param([string]$Dir, [string]$FeedPath)
    $cfg = @"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="local-mt5" value="$FeedPath" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
"@
    Set-Content -LiteralPath (Join-Path $Dir "nuget.config") -Value $cfg -Encoding UTF8
}

function Test-Consumer {
    param(
        [string]$Name,
        [string]$Tfm,
        [bool]$IsNetFramework
    )

    Write-Host ""
    Write-Host "=== Consumer: $Name ($Tfm) ===" -ForegroundColor Cyan
    $dir = Join-Path $work $Name
    New-Item -ItemType Directory -Path $dir -Force | Out-Null
    New-ConsumerNugetConfig -Dir $dir -FeedPath $feed

    $refAssemblies = ""
    if ($IsNetFramework) {
        $refAssemblies = @"
    <PackageReference Include="Microsoft.NETFramework.ReferenceAssemblies.net48" Version="1.0.3" PrivateAssets="all" />
    <PackageReference Include="System.Net.Http.WinHttpHandler" Version="9.0.0" />
"@
    }

    # ONLY MetaTrader.Grpc.Client is referenced for the client itself (SC-002).
    $csproj = @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>$Tfm</TargetFramework>
    <Nullable>disable</Nullable>
    <ImplicitUsings>disable</ImplicitUsings>
    <RestorePackagesWithLockFile>false</RestorePackagesWithLockFile>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="MetaTrader.Grpc.Client" Version="$script:packageVersion" />
$refAssemblies
  </ItemGroup>
</Project>
"@
    Set-Content -LiteralPath (Join-Path $dir "$Name.csproj") -Value $csproj -Encoding UTF8

    # A first call that uses the generated types + the wrapper, proving the client
    # compiles against a consumer with no code-generation of its own.
    $program = @"
using System;
using MetaTrader.Grpc.Client;
using Metatrader.V1;

internal static class Program
{
    private static void Main()
    {
        var options = new Mt5GrpcClientOptions { Address = new Uri("http://localhost:50051") };
        var request = new AccountInfoRequest();
        Console.WriteLine(options.Address + " " + request.GetType().FullName);
    }
}
"@
    Set-Content -LiteralPath (Join-Path $dir "Program.cs") -Value $program -Encoding UTF8

    Push-Location $dir
    try {
        dotnet restore "$Name.csproj" | Out-Host
        if ($LASTEXITCODE -ne 0) { $failures.Add("[$Name] restore failed."); return }

        dotnet build "$Name.csproj" -c $Configuration --no-restore | Out-Host
        if ($LASTEXITCODE -ne 0) { $failures.Add("[$Name] build failed."); return }
    }
    finally {
        Pop-Location
    }

    # --- SC-003: no protobuf/gRPC code generation ran in the consumer ---
    $assets = Join-Path $dir "obj/project.assets.json"
    if (Test-Path $assets) {
        $assetsText = Get-Content -Raw -LiteralPath $assets
        if ($assetsText -match '"Grpc\.Tools/') {
            $failures.Add("[$Name] Grpc.Tools entered the consumer dependency graph (SC-003 violated).")
        }
    }
    $generated = Get-ChildItem -Path (Join-Path $dir "obj") -Recurse -Filter *.cs -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -match 'Grpc\.cs$' -or $_.Name -match '^Metatrader' }
    if ($generated) {
        $failures.Add("[$Name] protobuf/gRPC code was generated in the consumer ($($generated.Count) file(s)) - SC-003 violated.")
    }

    # --- SC-002: client runtime deps flowed transitively into the build output ---
    $binDir = Join-Path $dir "bin/$Configuration/$Tfm"
    foreach ($dll in @("Google.Protobuf.dll", "Grpc.Core.Api.dll", "Grpc.Net.Client.dll", "MetaTrader.Grpc.Client.dll")) {
        if (-not (Test-Path (Join-Path $binDir $dll))) {
            $failures.Add("[$Name] expected transitive runtime dependency '$dll' missing from build output ($binDir) - SC-002.")
        }
    }

    Write-Host "[$Name] restore + build OK; runtime deps flowed; no consumer codegen." -ForegroundColor Green
}

try {
    Write-Host "Packing $project into local feed $feed ..."
    dotnet pack $project -c $Configuration -p:ContinuousIntegrationBuild=true -o $feed | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "dotnet pack failed." }

    $nupkg = Get-ChildItem -Path $feed -Filter "MetaTrader.Grpc.Client.*.nupkg" |
        Where-Object { $_.Name -notlike "*.symbols.nupkg" } | Select-Object -First 1
    if (-not $nupkg) { throw "No .nupkg produced." }
    if ($nupkg.BaseName -match '^MetaTrader\.Grpc\.Client\.(.+)$') {
        $script:packageVersion = $Matches[1]
    } else {
        throw "Could not parse package version from $($nupkg.Name)."
    }
    Write-Host "Packed version: $script:packageVersion"

    Test-Consumer -Name "ModernConsumer" -Tfm $ModernTfm -IsNetFramework $false
    Test-Consumer -Name "NetFx48Consumer" -Tfm "net48" -IsNetFramework $true
}
finally {
    Remove-Item -Recurse -Force -LiteralPath $work -ErrorAction SilentlyContinue
}

if ($failures.Count -gt 0) {
    Write-Host ""
    Write-Host "Consumer restore/build verification FAILED:" -ForegroundColor Red
    foreach ($f in $failures) { Write-Host "  - $f" -ForegroundColor Red }
    throw "Consumer verification failed ($($failures.Count) issue(s))."
}

Write-Host ""
Write-Host "Consumer restore/build verification passed on net48 and $ModernTfm." -ForegroundColor Green
Write-Host "  - single PackageReference to MetaTrader.Grpc.Client; runtime deps resolved automatically (SC-002)"
Write-Host "  - no protobuf/gRPC code generation in either consumer (SC-003)"
