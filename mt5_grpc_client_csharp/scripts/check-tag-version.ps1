<#
.SYNOPSIS
    Fails if a release tag's version does not match the csproj <Version>
    (US3 Independent Test; Contract C precondition).

.DESCRIPTION
    The publish workflow triggers on tags of the form:
        csharp-client-v<X.Y.Z>            (stable,      e.g. csharp-client-v0.2.0)
        csharp-client-v<X.Y.Z>-<label>    (pre-release, e.g. csharp-client-v0.3.0-preview.1)

    This guard extracts the version from the tag, compares it to <Version> in
    MetaTrader.Grpc.Client.csproj, and exits non-zero on any mismatch - run BEFORE
    push so a package whose number disagrees with its tag is never published.

.PARAMETER Tag
    The full git tag name (e.g. the CI 'github.ref_name'). Defaults to the
    GITHUB_REF_NAME environment variable when omitted.
#>
param(
    [string]$Tag = $env:GITHUB_REF_NAME
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

if ([string]::IsNullOrWhiteSpace($Tag)) {
    throw "No tag supplied. Pass -Tag 'csharp-client-v<X.Y.Z>' or set GITHUB_REF_NAME."
}

$prefix = "csharp-client-v"
if (-not $Tag.StartsWith($prefix)) {
    throw "Tag '$Tag' does not start with the required prefix '$prefix'."
}

$tagVersion = $Tag.Substring($prefix.Length)
if ([string]::IsNullOrWhiteSpace($tagVersion)) {
    throw "Tag '$Tag' has no version component after '$prefix'."
}

$root    = Resolve-Path (Join-Path $PSScriptRoot "..")
$project = Join-Path $root "src/MetaTrader.Grpc.Client/MetaTrader.Grpc.Client.csproj"

$xml = [xml](Get-Content -Raw -LiteralPath $project)
$csprojVersion = $null
foreach ($pg in $xml.Project.PropertyGroup) {
    $node = $pg.SelectSingleNode("Version")
    if ($node) { $csprojVersion = $node.InnerText.Trim(); break }
}

if ([string]::IsNullOrWhiteSpace($csprojVersion)) {
    throw "Could not read <Version> from $project."
}

if ($tagVersion -ne $csprojVersion) {
    throw "Tag version '$tagVersion' does not match csproj <Version> '$csprojVersion'. " +
          "Bump <Version> and re-tag so the published package is traceable to this revision."
}

Write-Host "Tag/version guard passed: tag '$Tag' -> version '$tagVersion' == csproj <Version> '$csprojVersion'."
