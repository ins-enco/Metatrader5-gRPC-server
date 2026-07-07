# Quickstart: Consume & Publish MetaTrader.Grpc.Client

**Feature**: `004-client-package-distribution` | **Date**: 2026-07-07

Two audiences: a **consumer** adding the package to a new project, and a
**maintainer** publishing a new version. The GitHub owner hosting this repository
is **`ins-enco`**; substitute your own owner if you fork it.

---

## For consumers — add the package and make a call (target: under 15 min, SC-001)

### 1. Create a token

Create a GitHub Personal Access Token (classic) with at least the `read:packages`
scope, then expose it and your GitHub username to the shell:

```powershell
$env:GITHUB_ACTOR = "your-github-username"
$env:GITHUB_PACKAGES_TOKEN = "ghp_xxx"   # PAT with read:packages
```

### 2. Add the feed source

Add a `nuget.config` next to your solution (a ready-to-copy file lives at
[`mt5_grpc_client_csharp/examples/nuget.config`](../../mt5_grpc_client_csharp/examples/nuget.config)):

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
    <add key="github-ins-enco" value="https://nuget.pkg.github.com/ins-enco/index.json" />
  </packageSources>
  <packageSourceCredentials>
    <github-ins-enco>
      <add key="Username" value="%GITHUB_ACTOR%" />
      <add key="ClearTextPassword" value="%GITHUB_PACKAGES_TOKEN%" />
    </github-ins-enco>
  </packageSourceCredentials>
</configuration>
```

`nuget.org` is included so the client's runtime dependencies resolve
automatically. No token is committed — the values come from the environment. If
you use a different owner, replace `ins-enco` in both the URL and the source key
name (the key must be a valid XML element name).

### 3. Add the reference and restore

```powershell
dotnet add package MetaTrader.Grpc.Client --version 0.2.0
dotnet restore
```

Restore resolves the package **and** all runtime dependencies automatically —
you add no other packages by hand (SC-002). No protobuf/gRPC code generation runs
in your project (SC-003).

**Stable vs. pre-release**: production versions use plain SemVer (`0.2.0`);
pre-release builds carry a SemVer pre-release suffix (e.g. `0.3.0-preview.1`).
NuGet excludes pre-release versions by default, so the command above only picks
stable versions — opt in explicitly with `--prerelease` (or a floating `0.3.0-*`
version) to consume a pre-release (FR-011).

### 4. Make a first call

```csharp
using MetaTrader.Grpc.Client;

var options = new Mt5GrpcClientOptions { Address = new Uri("http://localhost:50051") };
using var client = Mt5GrpcClientFactory.Create(options);

var result = await client.GetAccountInfoAsync(deadline: DateTime.UtcNow.AddSeconds(2));
Console.WriteLine(result.IsSuccess
    ? result.Value!.AccountInfo.Login.ToString()
    : $"{result.Error!.Operation}: {result.Error.Message}");
```

Success is a compiling, connecting client returning the login or a typed error.

### .NET Framework 4.8 note

net48 consumers reference the same `netstandard2.0` package but gRPC-over-HTTP/2
requires TLS and `WinHttpHandler` on the channel. See the client
[README](../../mt5_grpc_client_csharp/README.md) and the
`NetFramework48ClientExample` for the transport setup.

### If restore fails

- **401 / authentication** — the token is missing or lacks `read:packages`;
  re-check step 1. This is a clear, expected failure, not a partial restore.
- **Network / offline** — the feed is unreachable; restore fails rather than
  producing a broken client. Reconnect and retry.

### Checking compatibility before you adopt (US2)

From the package's feed listing / README you can read the **version**, the
**proto contract identity** (`protos-003-csharp-request-enums` for `0.2.0`), and
the **tested server version range** (`[0.2.0,1.0.0)` for `0.2.0`) — enough to
confirm it matches your MT5 server without reading source (SC-004). Breaking
versions reference a migration path in their release notes.

---

## For maintainers — publish a new version (target: under 15 min, SC-005)

### 1. Set the version

Bump `<Version>` in
[MetaTrader.Grpc.Client.csproj](../../mt5_grpc_client_csharp/src/MetaTrader.Grpc.Client/MetaTrader.Grpc.Client.csproj).
For a breaking change, also update `<ProtoContractIdentity>` /
`<TestedServerVersionRange>`, the README, `CHANGELOG.md`, `MIGRATION.md`, and the
`<PackageReleaseNotes>` (which must quote the current contract identity and server
range). Use a SemVer pre-release suffix for pre-release builds.

### 2. Verify locally

```powershell
dotnet restore mt5_grpc_client_csharp/MetaTrader.Grpc.Client.sln
dotnet build   mt5_grpc_client_csharp/MetaTrader.Grpc.Client.sln -c Release
dotnet test    mt5_grpc_client_csharp/MetaTrader.Grpc.Client.sln -c Release
mt5_grpc_client_csharp/scripts/check-generated.ps1        -Configuration Release
mt5_grpc_client_csharp/scripts/check-package-metadata.ps1 -Configuration Release
mt5_grpc_client_csharp/scripts/verify-consumer-restore.ps1 -Configuration Release
mt5_grpc_client_csharp/scripts/check-tag-version.ps1 -Tag "csharp-client-v0.2.0"   # tag == <Version>
dotnet pack    mt5_grpc_client_csharp/src/MetaTrader.Grpc.Client/MetaTrader.Grpc.Client.csproj -c Release -p:ContinuousIntegrationBuild=true
```

`check-package-metadata.ps1` asserts the packed `.nupkg` carries the right
dependency set (no `Grpc.Tools`), `netstandard2.0`, README, and the compatibility
metadata; `verify-consumer-restore.ps1` proves a clean net48 + modern-.NET
consumer restores and builds with zero hand-added packages; `check-tag-version.ps1`
confirms the tag you are about to push equals `<Version>`.

### 3. Tag to publish

```powershell
git tag csharp-client-v0.2.0     # must equal <Version> in the csproj
git push origin csharp-client-v0.2.0
```

CI (a client-scoped publish workflow) then builds, tests, packs, and pushes to
GitHub Packages using the built-in `GITHUB_TOKEN` — no manual publish, and no
credential on your machine (FR-013). The tag version must equal the csproj
version or the job fails before publishing.

### Immutability (verify)

Published versions are immutable. The publish job pushes **without**
`--skip-duplicate`, so re-running publish for a version that already exists returns
HTTP **409** from the feed and the job fails (FR-008, SC-007). To confirm: re-run
the publish workflow (or re-push the same `csharp-client-v<X.Y.Z>` tag) for an
already-published version — it must fail at the `dotnet nuget push` step with a
409, never silently succeed. To ship a correction, publish a **new** version
number.

### Reproducibility (verify)

The build is deterministic (`<Deterministic>true</Deterministic>` in
`Directory.Build.props`) and packed with `ContinuousIntegrationBuild=true`, so the
`.nupkg` is a function of source alone (FR-007, SC-005). To confirm: check out the
tagged revision on a clean machine, run the same
`dotnet pack ... -p:ContinuousIntegrationBuild=true`, and compare the produced
`.nupkg` contents (nuspec + assemblies) with the published artifact — they are
equivalent.
