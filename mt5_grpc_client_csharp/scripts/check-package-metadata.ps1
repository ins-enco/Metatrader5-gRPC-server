<#
.SYNOPSIS
    Packs MetaTrader.Grpc.Client and asserts the produced .nupkg carries the
    distribution contract (Contract A in
    specs/004-client-package-distribution/contracts/package-distribution.md).

.DESCRIPTION
    Verifies, from the packed artifact alone (no source access), that:
      * the package targets netstandard2.0 (T005, FR-004/SC-003),
      * it declares EXACTLY the runtime dependency set and contains NO Grpc.Tools
        dependency (T005, FR-003/SC-002),
      * README.md is packed into the .nupkg (T005, FR-006),
      * the nuspec version, MIT license expression, and release notes are present,
        and the packed README + release notes carry the current proto contract
        identity and tested server version range a consumer needs without source
        access (T011, FR-005/SC-004).

    The custom MSBuild properties <ProtoContractIdentity>/<TestedServerVersionRange>
    are NOT emitted into the nuspec (NuGet drops unknown metadata), so the
    compatibility values are asserted in the feed-visible README + releaseNotes
    fields per research.md Decision 4.
#>
param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$root    = Resolve-Path (Join-Path $PSScriptRoot "..")
$project = Join-Path $root "src/MetaTrader.Grpc.Client/MetaTrader.Grpc.Client.csproj"

# Expected runtime dependency set (Contract A). Grpc.Tools MUST NOT appear.
$expectedDeps = @{
    "Google.Protobuf"                           = "3.29.3"
    "Grpc.Core.Api"                             = "2.71.0"
    "Grpc.Net.Client"                           = "2.71.0"
    "Microsoft.Extensions.Logging.Abstractions" = "9.0.0"
}

function Get-CsprojValue {
    param([string]$Element)
    $xml = [xml](Get-Content -Raw -LiteralPath $project)
    foreach ($pg in $xml.Project.PropertyGroup) {
        $node = $pg.SelectSingleNode($Element)
        if ($node) { return $node.InnerText.Trim() }
    }
    return $null
}

# Authored source-of-truth values the feed-visible fields must quote (Decision 4).
$expectedVersion        = Get-CsprojValue "Version"
$expectedContractId     = Get-CsprojValue "ProtoContractIdentity"
$expectedServerRange    = Get-CsprojValue "TestedServerVersionRange"

if ([string]::IsNullOrWhiteSpace($expectedVersion))     { throw "csproj is missing <Version>." }
if ([string]::IsNullOrWhiteSpace($expectedContractId))  { throw "csproj is missing <ProtoContractIdentity>." }
if ([string]::IsNullOrWhiteSpace($expectedServerRange)) { throw "csproj is missing <TestedServerVersionRange>." }

# Pack into an isolated temp output so we inspect exactly what would be published.
$outDir = Join-Path ([System.IO.Path]::GetTempPath()) ("mt5-pkg-meta-" + [System.Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $outDir -Force | Out-Null

$failures = New-Object System.Collections.Generic.List[string]

try {
    Write-Host "Packing $project (Configuration=$Configuration) ..."
    dotnet pack $project -c $Configuration -p:ContinuousIntegrationBuild=true -o $outDir | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "dotnet pack failed." }

    $nupkg = Get-ChildItem -Path $outDir -Filter "MetaTrader.Grpc.Client.*.nupkg" |
        Where-Object { $_.Name -notlike "*.symbols.nupkg" } |
        Select-Object -First 1
    if (-not $nupkg) { throw "No MetaTrader.Grpc.Client .nupkg was produced in $outDir." }
    Write-Host "Inspecting $($nupkg.Name)"

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = [System.IO.Compression.ZipFile]::OpenRead($nupkg.FullName)
    try {
        $entries = $zip.Entries | ForEach-Object { $_.FullName }

        # --- README.md packed (T005, FR-006) ---
        if (-not ($entries -contains "README.md")) {
            $failures.Add("README.md is not packed into the .nupkg (found: $($entries -join ', ')).")
        }

        # --- Read the nuspec ---
        $nuspecEntry = $zip.Entries | Where-Object { $_.FullName -like "*.nuspec" } | Select-Object -First 1
        if (-not $nuspecEntry) { throw "No .nuspec found inside the .nupkg." }
        $reader = New-Object System.IO.StreamReader($nuspecEntry.Open())
        try { $nuspecXmlText = $reader.ReadToEnd() } finally { $reader.Dispose() }

        [xml]$nuspec = $nuspecXmlText
        $meta = $nuspec.package.metadata

        # --- version (T011, FR-005) ---
        if ($meta.version -ne $expectedVersion) {
            $failures.Add("nuspec version '$($meta.version)' != csproj <Version> '$expectedVersion'.")
        }

        # --- MIT license expression (T011, FR-006) ---
        $license = $null
        if ($meta.license) {
            # <license type="expression">MIT</license>
            $license = ($meta.license.'#text', $meta.license.InnerText | Where-Object { $_ } | Select-Object -First 1)
        }
        if ($license -ne "MIT") {
            $failures.Add("nuspec license expression is '$license' (expected 'MIT').")
        }

        # --- release notes present + carry compatibility metadata (T011, FR-005/SC-004) ---
        $releaseNotes = [string]$meta.releaseNotes
        if ([string]::IsNullOrWhiteSpace($releaseNotes)) {
            $failures.Add("nuspec releaseNotes is empty (must carry proto contract identity + tested server range).")
        } else {
            if (-not $releaseNotes.Contains($expectedContractId)) {
                $failures.Add("nuspec releaseNotes does not contain proto contract identity '$expectedContractId'.")
            }
            if (-not $releaseNotes.Contains($expectedServerRange)) {
                $failures.Add("nuspec releaseNotes does not contain tested server range '$expectedServerRange'.")
            }
        }

        # --- target framework netstandard2.0 (T005, FR-004/SC-003) ---
        # dependencies are grouped by targetFramework; also assert the group TFM.
        $depGroups = @()
        if ($meta.dependencies) { $depGroups = @($meta.dependencies.group) }
        $tfms = $depGroups | ForEach-Object { $_.targetFramework } | Where-Object { $_ }
        if (-not ($tfms -contains ".NETStandard2.0")) {
            $failures.Add("nuspec dependency group targetFramework is not '.NETStandard2.0' (found: $($tfms -join ', ')).")
        }

        # --- exact runtime dependency set, no Grpc.Tools (T005, FR-003/SC-002) ---
        $actualDeps = @{}
        foreach ($g in $depGroups) {
            foreach ($d in @($g.dependency)) {
                if ($d -and $d.id) { $actualDeps[$d.id] = $d.version }
            }
        }

        if ($actualDeps.ContainsKey("Grpc.Tools")) {
            $failures.Add("Grpc.Tools leaked into the consumer dependency graph (must be PrivateAssets=all).")
        }

        foreach ($id in $expectedDeps.Keys) {
            if (-not $actualDeps.ContainsKey($id)) {
                $failures.Add("Missing expected runtime dependency '$id'.")
            } elseif (-not ([string]$actualDeps[$id]).Contains($expectedDeps[$id])) {
                $failures.Add("Dependency '$id' version '$($actualDeps[$id])' does not include expected '$($expectedDeps[$id])'.")
            }
        }

        foreach ($id in $actualDeps.Keys) {
            if (-not $expectedDeps.ContainsKey($id)) {
                $failures.Add("Unexpected runtime dependency '$id' (not in the declared runtime set).")
            }
        }

        # --- README content carries compatibility metadata (T011, FR-005/SC-004) ---
        $readmeEntry = $zip.Entries | Where-Object { $_.FullName -eq "README.md" } | Select-Object -First 1
        if ($readmeEntry) {
            $rr = New-Object System.IO.StreamReader($readmeEntry.Open())
            try { $readmeText = $rr.ReadToEnd() } finally { $rr.Dispose() }
            if (-not $readmeText.Contains($expectedContractId)) {
                $failures.Add("Packed README.md does not contain proto contract identity '$expectedContractId'.")
            }
            if (-not $readmeText.Contains($expectedServerRange)) {
                $failures.Add("Packed README.md does not contain tested server range '$expectedServerRange'.")
            }
        }
    }
    finally {
        $zip.Dispose()
    }
}
finally {
    Remove-Item -Recurse -Force -LiteralPath $outDir -ErrorAction SilentlyContinue
}

if ($failures.Count -gt 0) {
    Write-Host ""
    Write-Host "Package metadata check FAILED:" -ForegroundColor Red
    foreach ($f in $failures) { Write-Host "  - $f" -ForegroundColor Red }
    throw "Package metadata contract violated ($($failures.Count) issue(s))."
}

Write-Host ""
Write-Host "Package metadata check passed:" -ForegroundColor Green
Write-Host "  version                = $expectedVersion"
Write-Host "  target framework       = netstandard2.0"
Write-Host "  runtime dependencies   = $($expectedDeps.Keys -join ', ')"
Write-Host "  Grpc.Tools excluded    = yes"
Write-Host "  README.md packed       = yes"
Write-Host "  proto contract id      = $expectedContractId (in README + releaseNotes)"
Write-Host "  tested server range    = $expectedServerRange (in README + releaseNotes)"
