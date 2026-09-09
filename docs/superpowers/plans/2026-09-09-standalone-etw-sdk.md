# Standalone Agent365 ETW SDK Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Extract Agent365 contracts and ETW payload emission into independently consumable NuGet packages while preserving `Microsoft.OpenTelemetry` as the umbrella package.

**Architecture:** A Contracts project owns the existing event contracts, DTOs, builders, message utilities, and schema constants. An ETW project references Contracts and owns `EtwEventSource` plus `EtwExportFormatter.FormatLogData(...)`; the existing distro references both projects, retains HTTP/Activity formatting, and forwards moved shipped public types.

**Tech Stack:** C#; .NET Standard 2.0; .NET 8; SDK-style projects; NuGet; `System.Diagnostics.Tracing.EventSource`; `System.Text.Json`; MSTest; FluentAssertions; GitHub Actions; DocFX.

## Global Constraints

- Target `netstandard2.0;net8.0` for both production packages.
- Preserve the namespace of every moved existing type.
- Preserve ETW provider name `A365-O11y-EventSource`, event IDs `1000` and `2000`, levels, opcodes, messages, and payload ordering.
- Preserve the current `FormatLogData(...)` JSON shape, timestamp conversion, default span kind, and default status.
- `Microsoft.Agents.A365.Observability.Etw` must not depend on `Microsoft.OpenTelemetry`.
- `Microsoft.OpenTelemetry` remains an umbrella package and references both new packages.
- Preserve binary compatibility for moved shipped public APIs through type forwarding.
- Do not add Azure Monitor, Azure Identity, MSAL, durable storage, hosting, or HTTP exporter dependencies to Contracts or ETW.
- Package versions begin aligned with the current distro version, `1.1.0`.

---

## File Structure

### New Contracts project

- `Directory.Packages.props`: central versions for `System.Diagnostics.DiagnosticSource` and `System.Text.Json`.
- `src/Microsoft.Agents.A365.Observability.Contracts/Microsoft.Agents.A365.Observability.Contracts.csproj`: package metadata, target frameworks, analyzers, and signing.
- `src/Microsoft.Agents.A365.Observability.Contracts/Properties/AssemblyInfo.cs`: test internals visibility using the repository signing key.
- `src/Microsoft.Agents.A365.Observability.Contracts/.publicApi/PublicAPI.Shipped.txt`: shipped API entries moved from the distro baseline.
- `src/Microsoft.Agents.A365.Observability.Contracts/.publicApi/PublicAPI.Unshipped.txt`: unshipped moved API entries.
- `src/Microsoft.Agents.A365.Observability.Contracts/DTOs/**`: existing event DTOs and builders.
- `src/Microsoft.Agents.A365.Observability.Contracts/Tracing/Contracts/**`: existing event contracts.
- `src/Microsoft.Agents.A365.Observability.Contracts/Tracing/MessageUtils.cs`: existing structured message serialization.
- `src/Microsoft.Agents.A365.Observability.Contracts/Tracing/Scopes/OpenTelemetryConstants.cs`: existing schema keys required by DTOs/builders.
- `src/Microsoft.Agents.A365.Observability.Contracts/Tracing/Scopes/AutoInstrumentationConstants.cs`: existing invocation keys required by formatting compatibility.

### New ETW project

- `src/Microsoft.Agents.A365.Observability.Etw/Microsoft.Agents.A365.Observability.Etw.csproj`: package metadata and Contracts reference.
- `src/Microsoft.Agents.A365.Observability.Etw/EtwEventSource.cs`: moved existing EventSource.
- `src/Microsoft.Agents.A365.Observability.Etw/EtwExportFormatter.cs`: ETW JSON envelope formatting extracted from `ExportFormatter`.
- `src/Microsoft.Agents.A365.Observability.Etw/.publicApi/PublicAPI.Shipped.txt`: `EtwEventSource` and formatter API baseline.
- `src/Microsoft.Agents.A365.Observability.Etw/.publicApi/PublicAPI.Unshipped.txt`: empty starting baseline.

### Tests

- `test/Microsoft.Agents.A365.Observability.Contracts.Tests/Microsoft.Agents.A365.Observability.Contracts.Tests.csproj`: isolated Contracts tests.
- `test/Microsoft.Agents.A365.Observability.Contracts.Tests/Properties/AssemblyInfo.cs`: Contracts internals visibility target.
- `test/Microsoft.Agents.A365.Observability.Contracts.Tests/Runtime/**`: moved DTO, builder, contract, and message utility tests.
- `test/Microsoft.Agents.A365.Observability.Etw.Tests/Microsoft.Agents.A365.Observability.Etw.Tests.csproj`: isolated formatter and EventSource tests.
- `test/Microsoft.Agents.A365.Observability.Etw.Tests/EtwExportFormatterTests.cs`: JSON compatibility tests.
- `test/Microsoft.Agents.A365.Observability.Etw.Tests/EtwEventSourceTests.cs`: moved EventSource contract tests.
- `test/Microsoft.Agents.A365.Observability.Etw.Tests/EtwEventSourceConfigureTests.cs`: moved EventSource configuration tests.

### Existing distro integration

- `src/Microsoft.OpenTelemetry/Microsoft.OpenTelemetry.csproj`: references Contracts and ETW.
- `src/Microsoft.OpenTelemetry/Agent365/Runtime/Common/ExportFormatter.cs`: retains Activity/HTTP formatting and forwards the shipped log-formatting method.
- `src/Microsoft.OpenTelemetry/Agent365/Runtime/Etw/EtwLogProcessor.cs`: consumes `EtwExportFormatter`.
- `src/Microsoft.OpenTelemetry/Agent365/Runtime/Etw/EtwLoggingBuilder.cs`: registers `EtwExportFormatter`.
- `src/Microsoft.OpenTelemetry/Properties/TypeForwards.Agent365.cs`: forwards moved shipped public types.
- `src/Microsoft.OpenTelemetry/.publicApi/PublicAPI.Shipped.txt`: removes declarations now owned by new assemblies while retaining the obsolete forwarding member declared by `ExportFormatter`.
- `src/Microsoft.OpenTelemetry/.publicApi/PublicAPI.Unshipped.txt`: removes unshipped declarations moved to Contracts.

### Build, packaging, and documentation

- `Microsoft.OpenTelemetry.slnx`: includes four new production/test projects.
- `.github/workflows/ci.yml`: packs all three production projects.
- `.github/workflows/docs.yml`: watches all production project files.
- `docfx.json`: generates API metadata from all three production projects.
- `docs/standalone-agent365-etw.md`: package usage and BotDesigner migration.

---

### Task 1: Extract the Contracts assembly

**Files:**
- Modify: `Directory.Packages.props`
- Create: `src/Microsoft.Agents.A365.Observability.Contracts/Microsoft.Agents.A365.Observability.Contracts.csproj`
- Create: `src/Microsoft.Agents.A365.Observability.Contracts/Properties/AssemblyInfo.cs`
- Move: `src/Microsoft.OpenTelemetry/Agent365/Runtime/DTOs/**`
- Move: `src/Microsoft.OpenTelemetry/Agent365/Runtime/Tracing/Contracts/**`
- Move: `src/Microsoft.OpenTelemetry/Agent365/Runtime/Tracing/MessageUtils.cs`
- Move: `src/Microsoft.OpenTelemetry/Agent365/Runtime/Tracing/Scopes/OpenTelemetryConstants.cs`
- Move: `src/Microsoft.OpenTelemetry/Agent365/Runtime/Tracing/Scopes/AutoInstrumentationConstants.cs`
- Create: `test/Microsoft.Agents.A365.Observability.Contracts.Tests/Microsoft.Agents.A365.Observability.Contracts.Tests.csproj`
- Move: `test/Microsoft.OpenTelemetry.Agent365.Tests/Runtime/DTOs/**`
- Move: `test/Microsoft.OpenTelemetry.Agent365.Tests/Runtime/Tracing/Contracts/**`
- Move: `test/Microsoft.OpenTelemetry.Agent365.Tests/Runtime/Tracing/MessageUtilsTest.cs`
- Move: `test/Microsoft.OpenTelemetry.Agent365.Tests/Runtime/Tracing/MessageUtilsToolPayloadTests.cs`

**Interfaces:**
- Consumes: `System.Diagnostics.ActivityKind`, `System.Text.Json`, and BCL collection/network types.
- Produces: existing types in `Microsoft.Agents.A365.Observability.Runtime.DTOs`, `.DTOs.Builders`, `.Tracing.Contracts`, `.Tracing.Contracts.Messages`, and `.Tracing.Contracts.Tools`.

- [ ] **Step 1: Create the Contracts test project and reference the not-yet-created production project**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFrameworks>net8.0;net10.0</TargetFrameworks>
    <RootNamespace>Microsoft.Agents.A365.Observability.Contracts.Tests</RootNamespace>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
    <GenerateDocumentationFile>false</GenerateDocumentationFile>
    <TreatWarningsAsErrors>false</TreatWarningsAsErrors>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="MSTest.TestAdapter" />
    <PackageReference Include="MSTest.TestFramework" />
    <PackageReference Include="FluentAssertions" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\Microsoft.Agents.A365.Observability.Contracts\Microsoft.Agents.A365.Observability.Contracts.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Run restore to verify the project reference fails before the production project exists**

Run:

```powershell
dotnet restore test\Microsoft.Agents.A365.Observability.Contracts.Tests\Microsoft.Agents.A365.Observability.Contracts.Tests.csproj
```

Expected: failure stating that `Microsoft.Agents.A365.Observability.Contracts.csproj` does not exist.

- [ ] **Step 3: Create the Contracts production project**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <Description>Agent365 observability event contracts, DTOs, and builders.</Description>
    <AssemblyTitle>Microsoft Agent365 Observability Contracts</AssemblyTitle>
    <RootNamespace>Microsoft.Agents.A365.Observability</RootNamespace>
    <AssemblyName>Microsoft.Agents.A365.Observability.Contracts</AssemblyName>
    <TargetFrameworks>netstandard2.0;net8.0</TargetFrameworks>
    <PackageId>Microsoft.Agents.A365.Observability.Contracts</PackageId>
    <Version>1.1.0</Version>
    <PackageLicenseFile>LICENSE</PackageLicenseFile>
    <PackageReadmeFile>README.md</PackageReadmeFile>
    <PackageProjectUrl>https://github.com/microsoft/opentelemetry-distro-dotnet</PackageProjectUrl>
    <RepositoryUrl>https://github.com/microsoft/opentelemetry-distro-dotnet</RepositoryUrl>
    <RepositoryType>git</RepositoryType>
    <PackageRequireLicenseAcceptance>true</PackageRequireLicenseAcceptance>
  </PropertyGroup>
  <ItemGroup>
    <None Include="..\..\LICENSE" Pack="true" PackagePath="\" />
    <None Include="..\..\README.md" Pack="true" PackagePath="\" />
    <None Include="..\..\NOTICE.md" Pack="true" PackagePath="\" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.CodeAnalysis.PublicApiAnalyzers" PrivateAssets="all" />
    <PackageReference Include="Microsoft.SourceLink.GitHub" PrivateAssets="all" />
    <PackageReference Include="System.Diagnostics.DiagnosticSource" />
    <PackageReference Include="System.Text.Json" />
    <AdditionalFiles Include=".publicApi\PublicAPI.Shipped.txt" />
    <AdditionalFiles Include=".publicApi\PublicAPI.Unshipped.txt" />
  </ItemGroup>
</Project>
```

- [ ] **Step 4: Add explicit central versions for standalone BCL dependencies**

Add to `Directory.Packages.props`:

```xml
<PackageVersion Include="System.Diagnostics.DiagnosticSource" Version="10.0.10" />
<PackageVersion Include="System.Text.Json" Version="10.0.10" />
```

- [ ] **Step 5: Move the Contracts source files without changing namespaces**

Run:

```powershell
git mv src\Microsoft.OpenTelemetry\Agent365\Runtime\DTOs src\Microsoft.Agents.A365.Observability.Contracts\DTOs
New-Item -ItemType Directory -Force src\Microsoft.Agents.A365.Observability.Contracts\Tracing | Out-Null
git mv src\Microsoft.OpenTelemetry\Agent365\Runtime\Tracing\Contracts src\Microsoft.Agents.A365.Observability.Contracts\Tracing\Contracts
git mv src\Microsoft.OpenTelemetry\Agent365\Runtime\Tracing\MessageUtils.cs src\Microsoft.Agents.A365.Observability.Contracts\Tracing\MessageUtils.cs
New-Item -ItemType Directory -Force src\Microsoft.Agents.A365.Observability.Contracts\Tracing\Scopes | Out-Null
git mv src\Microsoft.OpenTelemetry\Agent365\Runtime\Tracing\Scopes\OpenTelemetryConstants.cs src\Microsoft.Agents.A365.Observability.Contracts\Tracing\Scopes\OpenTelemetryConstants.cs
git mv src\Microsoft.OpenTelemetry\Agent365\Runtime\Tracing\Scopes\AutoInstrumentationConstants.cs src\Microsoft.Agents.A365.Observability.Contracts\Tracing\Scopes\AutoInstrumentationConstants.cs
```

- [ ] **Step 6: Move isolated contract tests into the new test project**

Run:

```powershell
New-Item -ItemType Directory -Force test\Microsoft.Agents.A365.Observability.Contracts.Tests\Runtime\Tracing | Out-Null
git mv test\Microsoft.OpenTelemetry.Agent365.Tests\Runtime\DTOs test\Microsoft.Agents.A365.Observability.Contracts.Tests\Runtime\DTOs
git mv test\Microsoft.OpenTelemetry.Agent365.Tests\Runtime\Tracing\Contracts test\Microsoft.Agents.A365.Observability.Contracts.Tests\Runtime\Tracing\Contracts
git mv test\Microsoft.OpenTelemetry.Agent365.Tests\Runtime\Tracing\MessageUtilsTest.cs test\Microsoft.Agents.A365.Observability.Contracts.Tests\Runtime\Tracing\MessageUtilsTest.cs
git mv test\Microsoft.OpenTelemetry.Agent365.Tests\Runtime\Tracing\MessageUtilsToolPayloadTests.cs test\Microsoft.Agents.A365.Observability.Contracts.Tests\Runtime\Tracing\MessageUtilsToolPayloadTests.cs
```

- [ ] **Step 7: Add Contracts internals visibility**

Create `src/Microsoft.Agents.A365.Observability.Contracts/Properties/AssemblyInfo.cs` using the same `PUBLIC_RELEASE` and internal signing public keys as `src/Microsoft.OpenTelemetry/Properties/AssemblyInfo.cs`, with:

```csharp
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo(
    "Microsoft.Agents.A365.Observability.Contracts.Tests, PublicKey=" + AssemblyInfo.PublicKey)]
```

- [ ] **Step 8: Move the Contracts API baseline entries**

Move every line whose API begins with one of these namespaces from the distro `.publicApi` files into matching files under the Contracts project:

```text
Microsoft.Agents.A365.Observability.Runtime.DTOs
Microsoft.Agents.A365.Observability.Runtime.Tracing.Contracts
```

Also move the builder APIs and nested message/tool contract APIs. Preserve `#nullable enable` headers and lexical sorting.

- [ ] **Step 9: Run the isolated Contracts tests**

Run:

```powershell
dotnet test test\Microsoft.Agents.A365.Observability.Contracts.Tests\Microsoft.Agents.A365.Observability.Contracts.Tests.csproj --configuration Release
```

Expected: all moved DTO, builder, contract, and message serialization tests pass for `net8.0` and `net10.0`.

- [ ] **Step 10: Commit the Contracts extraction**

```powershell
git add Directory.Packages.props src\Microsoft.Agents.A365.Observability.Contracts test\Microsoft.Agents.A365.Observability.Contracts.Tests src\Microsoft.OpenTelemetry\.publicApi
git commit -m "refactor: extract Agent365 contracts package"
```

---

### Task 2: Extract ETW emission and log formatting

**Files:**
- Create: `src/Microsoft.Agents.A365.Observability.Etw/Microsoft.Agents.A365.Observability.Etw.csproj`
- Move: `src/Microsoft.OpenTelemetry/Agent365/Runtime/Etw/EtwEventSource.cs`
- Create: `src/Microsoft.Agents.A365.Observability.Etw/EtwExportFormatter.cs`
- Create: `test/Microsoft.Agents.A365.Observability.Etw.Tests/Microsoft.Agents.A365.Observability.Etw.Tests.csproj`
- Create: `test/Microsoft.Agents.A365.Observability.Etw.Tests/EtwExportFormatterTests.cs`
- Move: `test/Microsoft.OpenTelemetry.Agent365.Tests/Runtime/Etw/EtwEventSourceTests.cs`
- Move: `test/Microsoft.OpenTelemetry.Agent365.Tests/Runtime/Etw/EtwEventSourceConfigureTests.cs`

**Interfaces:**
- Consumes: Contracts DTO dictionaries and `SpanKindConstants`.
- Produces: `EtwExportFormatter.FormatLogData(IDictionary<string, object?>)` and existing `EtwEventSource.Log`, `SpanStop(...)`, and `LogJson(...)`.

- [ ] **Step 1: Write formatter compatibility tests against the planned class**

Create tests covering error status, missing status, explicit span kind, timestamps, trace IDs, and null optional fields. The first test is:

```csharp
[TestMethod]
public void FormatLogData_WithErrorStatus_PreservesExistingJsonShape()
{
    var data = new InvokeAgentData(
        new Dictionary<string, object?> { ["key"] = "value" },
        startTime: DateTimeOffset.FromUnixTimeSeconds(10),
        endTime: DateTimeOffset.FromUnixTimeSeconds(11),
        spanId: "span",
        parentSpanId: "parent",
        spanKind: "Server",
        traceId: "trace")
    {
        StatusCode = SpanStatusCode.Error,
        StatusMessage = "failed",
    };

    var json = new EtwExportFormatter().FormatLogData(data.ToDictionary());

    using var document = JsonDocument.Parse(json);
    var root = document.RootElement;
    root.GetProperty("Name").GetString().Should().Be("InvokeAgent");
    root.GetProperty("SpanId").GetString().Should().Be("span");
    root.GetProperty("ParentSpanId").GetString().Should().Be("parent");
    root.GetProperty("TraceId").GetString().Should().Be("trace");
    root.GetProperty("Kind").GetString().Should().Be("Server");
    root.GetProperty("Status").GetProperty("code").GetInt32().Should().Be(2);
    root.GetProperty("Status").GetProperty("message").GetString().Should().Be("failed");
}
```

- [ ] **Step 2: Run the ETW tests to verify the missing project and formatter fail**

Run:

```powershell
dotnet test test\Microsoft.Agents.A365.Observability.Etw.Tests\Microsoft.Agents.A365.Observability.Etw.Tests.csproj --configuration Release
```

Expected: failure because the ETW project and `EtwExportFormatter` do not exist.

- [ ] **Step 3: Create the ETW project**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <Description>Agent365 observability ETW event emission and payload formatting.</Description>
    <AssemblyTitle>Microsoft Agent365 Observability ETW</AssemblyTitle>
    <RootNamespace>Microsoft.Agents.A365.Observability</RootNamespace>
    <AssemblyName>Microsoft.Agents.A365.Observability.Etw</AssemblyName>
    <TargetFrameworks>netstandard2.0;net8.0</TargetFrameworks>
    <PackageId>Microsoft.Agents.A365.Observability.Etw</PackageId>
    <Version>1.1.0</Version>
    <PackageLicenseFile>LICENSE</PackageLicenseFile>
    <PackageReadmeFile>README.md</PackageReadmeFile>
    <PackageProjectUrl>https://github.com/microsoft/opentelemetry-distro-dotnet</PackageProjectUrl>
    <RepositoryUrl>https://github.com/microsoft/opentelemetry-distro-dotnet</RepositoryUrl>
    <RepositoryType>git</RepositoryType>
    <PackageRequireLicenseAcceptance>true</PackageRequireLicenseAcceptance>
  </PropertyGroup>
  <ItemGroup>
    <None Include="..\..\LICENSE" Pack="true" PackagePath="\" />
    <None Include="..\..\README.md" Pack="true" PackagePath="\" />
    <None Include="..\..\NOTICE.md" Pack="true" PackagePath="\" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.CodeAnalysis.PublicApiAnalyzers" PrivateAssets="all" />
    <PackageReference Include="Microsoft.SourceLink.GitHub" PrivateAssets="all" />
    <PackageReference Include="System.Text.Json" />
    <AdditionalFiles Include=".publicApi\PublicAPI.Shipped.txt" />
    <AdditionalFiles Include=".publicApi\PublicAPI.Unshipped.txt" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\Microsoft.Agents.A365.Observability.Contracts\Microsoft.Agents.A365.Observability.Contracts.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 4: Move `EtwEventSource` unchanged**

Run:

```powershell
git mv src\Microsoft.OpenTelemetry\Agent365\Runtime\Etw\EtwEventSource.cs src\Microsoft.Agents.A365.Observability.Etw\EtwEventSource.cs
```

- [ ] **Step 5: Implement `EtwExportFormatter` with the extracted behavior**

```csharp
using Microsoft.Agents.A365.Observability.Runtime.DTOs;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Microsoft.Agents.A365.Observability.Runtime.Etw
{
    public sealed class EtwExportFormatter
    {
        public string FormatLogData(IDictionary<string, object?> data)
        {
            var payload = new
            {
                Name = data["Name"],
                Attributes = data["Attributes"],
                StartTimeUnixNano = data.TryGetValue("StartTime", out var startTimeObj) && startTimeObj != null
                    ? ToUnixNanos(((DateTimeOffset)startTimeObj).UtcDateTime)
                    : 0,
                EndTimeUnixNano = data.TryGetValue("EndTime", out var endTimeObj) && endTimeObj != null
                    ? ToUnixNanos(((DateTimeOffset)endTimeObj).UtcDateTime)
                    : 0,
                SpanId = data["SpanId"],
                ParentSpanId = data["ParentSpanId"],
                TraceId = data.TryGetValue("TraceId", out var traceIdObj) ? traceIdObj : null,
                Kind = data.TryGetValue("SpanKind", out var spanKindObj) && spanKindObj != null
                    ? spanKindObj
                    : SpanKindConstants.Client,
                Status = data.TryGetValue("Status", out var statusObj) && statusObj != null
                    ? statusObj
                    : new Dictionary<string, object> { ["code"] = 0, ["message"] = string.Empty },
            };

            return JsonSerializer.Serialize(payload, new JsonSerializerOptions
            {
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                PropertyNamingPolicy = null,
                WriteIndented = false,
            });
        }

        private static ulong ToUnixNanos(DateTime utc)
        {
            var value = utc.Kind == DateTimeKind.Utc ? utc : utc.ToUniversalTime();
            var unixEpoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            return (ulong)((value - unixEpoch).Ticks * 100);
        }
    }
}
```

- [ ] **Step 6: Create the isolated ETW test project and move EventSource tests**

Create:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFrameworks>net8.0;net10.0</TargetFrameworks>
    <RootNamespace>Microsoft.Agents.A365.Observability.Etw.Tests</RootNamespace>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
    <GenerateDocumentationFile>false</GenerateDocumentationFile>
    <TreatWarningsAsErrors>false</TreatWarningsAsErrors>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="MSTest.TestAdapter" />
    <PackageReference Include="MSTest.TestFramework" />
    <PackageReference Include="FluentAssertions" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\Microsoft.Agents.A365.Observability.Etw\Microsoft.Agents.A365.Observability.Etw.csproj" />
  </ItemGroup>
</Project>
```

Move:

```powershell
git mv test\Microsoft.OpenTelemetry.Agent365.Tests\Runtime\Etw\EtwEventSourceTests.cs test\Microsoft.Agents.A365.Observability.Etw.Tests\EtwEventSourceTests.cs
git mv test\Microsoft.OpenTelemetry.Agent365.Tests\Runtime\Etw\EtwEventSourceConfigureTests.cs test\Microsoft.Agents.A365.Observability.Etw.Tests\EtwEventSourceConfigureTests.cs
```

- [ ] **Step 7: Add ETW API baselines**

Move the existing `EtwEventSource` shipped entries from the distro baseline and add:

```text
Microsoft.Agents.A365.Observability.Runtime.Etw.EtwExportFormatter
Microsoft.Agents.A365.Observability.Runtime.Etw.EtwExportFormatter.EtwExportFormatter() -> void
Microsoft.Agents.A365.Observability.Runtime.Etw.EtwExportFormatter.FormatLogData(System.Collections.Generic.IDictionary<string!, object?>! data) -> string!
```

- [ ] **Step 8: Run ETW tests**

Run:

```powershell
dotnet test test\Microsoft.Agents.A365.Observability.Etw.Tests\Microsoft.Agents.A365.Observability.Etw.Tests.csproj --configuration Release
```

Expected: formatter compatibility and EventSource tests pass for `net8.0` and `net10.0`.

- [ ] **Step 9: Commit ETW extraction**

```powershell
git add src\Microsoft.Agents.A365.Observability.Etw test\Microsoft.Agents.A365.Observability.Etw.Tests src\Microsoft.OpenTelemetry\.publicApi
git commit -m "feat: add standalone Agent365 ETW package"
```

---

### Task 3: Rewire the distro and preserve compatibility

**Files:**
- Modify: `src/Microsoft.OpenTelemetry/Microsoft.OpenTelemetry.csproj`
- Modify: `src/Microsoft.OpenTelemetry/Agent365/Runtime/Common/ExportFormatter.cs`
- Modify: `src/Microsoft.OpenTelemetry/Agent365/Runtime/Etw/EtwLogProcessor.cs`
- Modify: `src/Microsoft.OpenTelemetry/Agent365/Runtime/Etw/EtwLoggingBuilder.cs`
- Create: `src/Microsoft.OpenTelemetry/Properties/TypeForwards.Agent365.cs`
- Modify: `src/Microsoft.OpenTelemetry/.publicApi/PublicAPI.Shipped.txt`
- Modify: `src/Microsoft.OpenTelemetry/.publicApi/PublicAPI.Unshipped.txt`
- Modify: `test/Microsoft.OpenTelemetry.Agent365.Tests/Runtime/Common/ExportFormatterTests.cs`
- Modify: `test/Microsoft.OpenTelemetry.Agent365.Tests/Runtime/Common/ExportFormatterStatusTests.cs`
- Modify: `test/Microsoft.OpenTelemetry.Agent365.Tests/Runtime/Etw/EtwLoggingBuilderTests.cs`

**Interfaces:**
- Consumes: Contracts types, `EtwEventSource`, and `EtwExportFormatter`.
- Produces: unchanged umbrella-package behavior and obsolete `ExportFormatter.FormatLogData(...)` compatibility.

- [ ] **Step 1: Add failing assertions that the compatibility method matches the ETW formatter**

```csharp
[TestMethod]
public void FormatLogData_ForwardsToEtwFormatter()
{
    var data = new InvokeAgentData(
        new Dictionary<string, object?> { ["key"] = "value" },
        spanId: "span");

#pragma warning disable CS0618
    var compatibilityJson = CreateFormatter().FormatLogData(data.ToDictionary());
#pragma warning restore CS0618
    var etwJson = new EtwExportFormatter().FormatLogData(data.ToDictionary());

    compatibilityJson.Should().Be(etwJson);
}
```

- [ ] **Step 2: Add project references to the distro**

```xml
<ItemGroup>
  <ProjectReference Include="..\Microsoft.Agents.A365.Observability.Contracts\Microsoft.Agents.A365.Observability.Contracts.csproj" />
  <ProjectReference Include="..\Microsoft.Agents.A365.Observability.Etw\Microsoft.Agents.A365.Observability.Etw.csproj" />
</ItemGroup>
```

- [ ] **Step 3: Replace the formatter implementation with an obsolete forwarding method**

In `ExportFormatter`:

```csharp
[Obsolete(
    "Use Microsoft.Agents.A365.Observability.Runtime.Etw.EtwExportFormatter.FormatLogData instead.")]
public string FormatLogData(IDictionary<string, object?> data)
{
    return new EtwExportFormatter().FormatLogData(data);
}
```

Remove only the old anonymous-payload implementation. Retain shared serializer and timestamp helpers still used by `FormatMany(...)` and `FormatSingle(...)`.

- [ ] **Step 4: Rewire `EtwLogProcessor`**

Change its field and constructor:

```csharp
private readonly EtwExportFormatter _formatter;

public EtwLogProcessor(
    EtwExportFormatter formatter,
    ILogger<EtwLogProcessor>? logger = null)
{
    _formatter = formatter;
    _logger = logger;
}
```

Keep:

```csharp
var jsonContent = _formatter.FormatLogData(attributes);
EtwEventSource.Log.LogJson(jsonContent);
```

- [ ] **Step 5: Register `EtwExportFormatter` in `EtwLoggingBuilder`**

Replace the `ExportFormatter` singleton used by `EtwLogProcessor` with:

```csharp
.AddSingleton<EtwExportFormatter>()
```

Keep `ExportFormatter` registration only in paths that require `FormatSingle(...)` or `FormatMany(...)`.

- [ ] **Step 6: Add type forwards for every moved shipped public type**

Create `TypeForwards.Agent365.cs` with:

```csharp
using System.Runtime.CompilerServices;
using Microsoft.Agents.A365.Observability.Runtime.DTOs;
using Microsoft.Agents.A365.Observability.Runtime.DTOs.Builders;
using Microsoft.Agents.A365.Observability.Runtime.Etw;
using Microsoft.Agents.A365.Observability.Runtime.Tracing.Contracts;
using Microsoft.Agents.A365.Observability.Runtime.Tracing.Contracts.Messages;
using Microsoft.Agents.A365.Observability.Runtime.Tracing.Contracts.Tools;

[assembly: TypeForwardedTo(typeof(BaseData))]
[assembly: TypeForwardedTo(typeof(InvokeAgentData))]
[assembly: TypeForwardedTo(typeof(ExecuteInferenceData))]
[assembly: TypeForwardedTo(typeof(ExecuteToolData))]
[assembly: TypeForwardedTo(typeof(OutputData))]
[assembly: TypeForwardedTo(typeof(ApplyGuardrailData))]
[assembly: TypeForwardedTo(typeof(SpanStatus))]
[assembly: TypeForwardedTo(typeof(SpanStatusCode))]
[assembly: TypeForwardedTo(typeof(InvokeAgentDataBuilder))]
[assembly: TypeForwardedTo(typeof(ExecuteInferenceDataBuilder))]
[assembly: TypeForwardedTo(typeof(ExecuteToolDataBuilder))]
[assembly: TypeForwardedTo(typeof(OutputDataBuilder))]
[assembly: TypeForwardedTo(typeof(ApplyGuardrailDataBuilder))]
[assembly: TypeForwardedTo(typeof(SpanStatusBuilder))]
[assembly: TypeForwardedTo(typeof(EtwEventSource))]
[assembly: TypeForwardedTo(typeof(AgentDetails))]
[assembly: TypeForwardedTo(typeof(AgentType))]
[assembly: TypeForwardedTo(typeof(CallerDetails))]
[assembly: TypeForwardedTo(typeof(GenAiRequestParameters))]
[assembly: TypeForwardedTo(typeof(GenAiResponseParameters))]
[assembly: TypeForwardedTo(typeof(GuardrailDecisionType))]
[assembly: TypeForwardedTo(typeof(GuardrailDetails))]
[assembly: TypeForwardedTo(typeof(GuardrailFinding))]
[assembly: TypeForwardedTo(typeof(GuardrailRiskSeverity))]
[assembly: TypeForwardedTo(typeof(GuardrailTargetType))]
[assembly: TypeForwardedTo(typeof(InferenceCallDetails))]
[assembly: TypeForwardedTo(typeof(InferenceOperationType))]
[assembly: TypeForwardedTo(typeof(InvokeAgentScopeDetails))]
[assembly: TypeForwardedTo(typeof(OperationSource))]
[assembly: TypeForwardedTo(typeof(Channel))]
[assembly: TypeForwardedTo(typeof(Request))]
[assembly: TypeForwardedTo(typeof(Response))]
[assembly: TypeForwardedTo(typeof(SpanDetails))]
[assembly: TypeForwardedTo(typeof(ThreatDiagnosticsSummary))]
[assembly: TypeForwardedTo(typeof(ToolCallDetails))]
[assembly: TypeForwardedTo(typeof(ToolType))]
[assembly: TypeForwardedTo(typeof(UserDetails))]
[assembly: TypeForwardedTo(typeof(MessageRole))]
[assembly: TypeForwardedTo(typeof(FinishReason))]
[assembly: TypeForwardedTo(typeof(Modality))]
[assembly: TypeForwardedTo(typeof(IMessagePart))]
[assembly: TypeForwardedTo(typeof(TextPart))]
[assembly: TypeForwardedTo(typeof(ToolCallRequestPart))]
[assembly: TypeForwardedTo(typeof(ToolCallResponsePart))]
[assembly: TypeForwardedTo(typeof(ReasoningPart))]
[assembly: TypeForwardedTo(typeof(BlobPart))]
[assembly: TypeForwardedTo(typeof(FilePart))]
[assembly: TypeForwardedTo(typeof(UriPart))]
[assembly: TypeForwardedTo(typeof(ServerToolCallPart))]
[assembly: TypeForwardedTo(typeof(ServerToolCallResponsePart))]
[assembly: TypeForwardedTo(typeof(GenericPart))]
[assembly: TypeForwardedTo(typeof(ChatMessage))]
[assembly: TypeForwardedTo(typeof(OutputMessage))]
[assembly: TypeForwardedTo(typeof(InputMessages))]
[assembly: TypeForwardedTo(typeof(OutputMessages))]
[assembly: TypeForwardedTo(typeof(ToolCallAction))]
[assembly: TypeForwardedTo(typeof(ExecuteToolCallArguments))]
[assembly: TypeForwardedTo(typeof(ToolCallResource))]
[assembly: TypeForwardedTo(typeof(ToolCallIdentifier))]
[assembly: TypeForwardedTo(typeof(ToolCallContainer))]
[assembly: TypeForwardedTo(typeof(ToolCallOutcomeStatus))]
[assembly: TypeForwardedTo(typeof(ToolPolicyDecision))]
[assembly: TypeForwardedTo(typeof(ExecuteToolCallResult))]
[assembly: TypeForwardedTo(typeof(ToolCallResultResource))]
[assembly: TypeForwardedTo(typeof(ToolCallResultOutcome))]
[assembly: TypeForwardedTo(typeof(ToolCallResultSensitivity))]
[assembly: TypeForwardedTo(typeof(ToolCallResultPolicy))]
[assembly: TypeForwardedTo(typeof(ToolCallResultSecurity))]
[assembly: TypeForwardedTo(typeof(ToolCallResultPagination))]
```

Also add:

```csharp
[assembly: TypeForwardedTo(typeof(BaseDataBuilder<>))]
```

The complete list above covers every public type declaration currently present in the moved DTO, builder, contract, message, tool, and EventSource files.

- [ ] **Step 7: Build to identify incomplete forwards and API baseline errors**

Run:

```powershell
dotnet build src\Microsoft.OpenTelemetry\Microsoft.OpenTelemetry.csproj --configuration Release
```

Expected: success with no `RS0016`, `RS0017`, missing-type, or type-conflict errors. If a moved shipped type is reported as removed, add its `TypeForwardedTo` attribute and move its API baseline entry to the owning project.

- [ ] **Step 8: Run distro formatter and ETW logging tests**

Run:

```powershell
dotnet test test\Microsoft.OpenTelemetry.Agent365.Tests\Microsoft.OpenTelemetry.Agent365.Tests.csproj --configuration Release --filter "FullyQualifiedName~ExportFormatter|FullyQualifiedName~EtwLoggingBuilder"
```

Expected: all selected tests pass.

- [ ] **Step 9: Commit distro integration**

```powershell
git add src\Microsoft.OpenTelemetry test\Microsoft.OpenTelemetry.Agent365.Tests
git commit -m "refactor: consume standalone Agent365 packages"
```

---

### Task 4: Update solution, CI packaging, and API documentation

**Files:**
- Modify: `Microsoft.OpenTelemetry.slnx`
- Modify: `.github/workflows/ci.yml`
- Modify: `.github/workflows/docs.yml`
- Modify: `docfx.json`

**Interfaces:**
- Consumes: all three production projects and three test projects.
- Produces: reproducible builds, three NuGet artifacts, and combined API documentation.

- [ ] **Step 1: Add all new projects to the solution**

Add:

```xml
<Project Path="src/Microsoft.Agents.A365.Observability.Contracts/Microsoft.Agents.A365.Observability.Contracts.csproj" />
<Project Path="src/Microsoft.Agents.A365.Observability.Etw/Microsoft.Agents.A365.Observability.Etw.csproj" />
<Project Path="test/Microsoft.Agents.A365.Observability.Contracts.Tests/Microsoft.Agents.A365.Observability.Contracts.Tests.csproj" />
<Project Path="test/Microsoft.Agents.A365.Observability.Etw.Tests/Microsoft.Agents.A365.Observability.Etw.Tests.csproj" />
```

- [ ] **Step 2: Run the solution build**

Run:

```powershell
dotnet restore Microsoft.OpenTelemetry.slnx
dotnet build Microsoft.OpenTelemetry.slnx --no-restore --configuration Release
```

Expected: every production, test, example, and documentation dependency builds successfully.

- [ ] **Step 3: Pack all production projects in CI**

Replace the single pack command with:

```yaml
- name: Pack
  run: |
    dotnet pack src/Microsoft.Agents.A365.Observability.Contracts/Microsoft.Agents.A365.Observability.Contracts.csproj --no-build --configuration Release --output ./packages
    dotnet pack src/Microsoft.Agents.A365.Observability.Etw/Microsoft.Agents.A365.Observability.Etw.csproj --no-build --configuration Release --output ./packages
    dotnet pack src/Microsoft.OpenTelemetry/Microsoft.OpenTelemetry.csproj --no-build --configuration Release --output ./packages
```

- [ ] **Step 4: Expand the docs workflow path filter**

```yaml
paths:
  - "src/Microsoft.OpenTelemetry/Microsoft.OpenTelemetry.csproj"
  - "src/Microsoft.Agents.A365.Observability.Contracts/Microsoft.Agents.A365.Observability.Contracts.csproj"
  - "src/Microsoft.Agents.A365.Observability.Etw/Microsoft.Agents.A365.Observability.Etw.csproj"
```

- [ ] **Step 5: Add all production projects to DocFX metadata**

```json
"files": [
  "src/Microsoft.OpenTelemetry/Microsoft.OpenTelemetry.csproj",
  "src/Microsoft.Agents.A365.Observability.Contracts/Microsoft.Agents.A365.Observability.Contracts.csproj",
  "src/Microsoft.Agents.A365.Observability.Etw/Microsoft.Agents.A365.Observability.Etw.csproj"
]
```

- [ ] **Step 6: Pack locally and inspect artifacts**

Run:

```powershell
dotnet pack src\Microsoft.Agents.A365.Observability.Contracts\Microsoft.Agents.A365.Observability.Contracts.csproj --no-build --configuration Release --output .\packages
dotnet pack src\Microsoft.Agents.A365.Observability.Etw\Microsoft.Agents.A365.Observability.Etw.csproj --no-build --configuration Release --output .\packages
dotnet pack src\Microsoft.OpenTelemetry\Microsoft.OpenTelemetry.csproj --no-build --configuration Release --output .\packages
Get-ChildItem .\packages\*.nupkg | Select-Object Name
```

Expected package names:

```text
Microsoft.Agents.A365.Observability.Contracts.1.1.0.nupkg
Microsoft.Agents.A365.Observability.Etw.1.1.0.nupkg
Microsoft.OpenTelemetry.1.1.0.nupkg
```

- [ ] **Step 7: Commit build and packaging changes**

```powershell
git add Microsoft.OpenTelemetry.slnx .github\workflows\ci.yml .github\workflows\docs.yml docfx.json
git commit -m "build: package standalone Agent365 assemblies"
```

---

### Task 5: Add package-consumer and BotDesigner migration validation

**Files:**
- Create: `docs/standalone-agent365-etw.md`
- Create: `test/package-smoke/StandaloneEtwConsumer/StandaloneEtwConsumer.csproj`
- Create: `test/package-smoke/StandaloneEtwConsumer/Program.cs`
- Create: `test/package-smoke/DistroConsumer/DistroConsumer.csproj`
- Create: `test/package-smoke/DistroConsumer/Program.cs`
- Modify: `.github/workflows/ci.yml`

**Interfaces:**
- Consumes: locally packed Contracts, ETW, and distro packages.
- Produces: proof that BotDesigner needs only ETW and distro users need only `Microsoft.OpenTelemetry`.

- [ ] **Step 1: Create an ETW-only package consumer**

`StandaloneEtwConsumer.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.Agents.A365.Observability.Etw" Version="1.1.0" />
  </ItemGroup>
</Project>
```

`Program.cs`:

```csharp
using Microsoft.Agents.A365.Observability.Runtime.DTOs.Builders;
using Microsoft.Agents.A365.Observability.Runtime.Etw;
using Microsoft.Agents.A365.Observability.Runtime.Tracing.Contracts;

var data = InvokeAgentDataBuilder.Build(
    new InvokeAgentScopeDetails(new Uri("https://example.com/agent")),
    new AgentDetails("agent-id", tenantId: "tenant-id"),
    "conversation-id");

var json = new EtwExportFormatter().FormatLogData(data.ToDictionary());
EtwEventSource.Log.LogJson(json);
```

- [ ] **Step 2: Create a distro-only package consumer**

`DistroConsumer.csproj` references only:

```xml
<PackageReference Include="Microsoft.OpenTelemetry" Version="1.1.0" />
```

`Program.cs`:

```csharp
using Microsoft.Agents.A365.Observability.Runtime.Etw;

EtwEventSource.Log.LogJson("{\"Name\":\"smoke-test\"}");
```

- [ ] **Step 3: Restore and build both consumers from local packages**

Run:

```powershell
dotnet restore test\package-smoke\StandaloneEtwConsumer\StandaloneEtwConsumer.csproj --source .\packages --source https://api.nuget.org/v3/index.json
dotnet build test\package-smoke\StandaloneEtwConsumer\StandaloneEtwConsumer.csproj --no-restore --configuration Release
dotnet restore test\package-smoke\DistroConsumer\DistroConsumer.csproj --source .\packages --source https://api.nuget.org/v3/index.json
dotnet build test\package-smoke\DistroConsumer\DistroConsumer.csproj --no-restore --configuration Release
```

Expected: both consumers build. The standalone output contains ETW and Contracts assemblies but not `Microsoft.OpenTelemetry.dll`; the distro output contains all three assemblies.

- [ ] **Step 4: Document standalone usage and BotDesigner migration**

Document:

```csharp
private readonly EtwExportFormatter _exportFormatter = new();

var eventData = InvokeAgentDataBuilder.Build(
    invokeAgentScopeDetails,
    agentDetails,
    conversationId,
    request,
    callerDetails,
    inputMessages,
    outputMessages,
    startTime,
    endTime,
    spanId,
    parentSpanId,
    extraAttributes: extraAttributes,
    traceId: traceId);

var jsonContent = _exportFormatter.FormatLogData(eventData.ToDictionary());
EtwEventSource.Log.LogJson(jsonContent);
```

State that BotDesigner changes the package reference in:

```text
/src/Infrastructure/Common/Microsoft.CCI.Common/Microsoft.CCI.Common.csproj
```

and changes the formatter field in:

```text
/src/Infrastructure/Common/Microsoft.CCI.Common/Logging/A365Observability/A365ObservabilityEtwLogger.cs
```

- [ ] **Step 5: Add package smoke builds to the CI pack job**

After packing, add:

```yaml
- name: Validate package consumers
  run: |
    dotnet restore test/package-smoke/StandaloneEtwConsumer/StandaloneEtwConsumer.csproj --source ./packages --source https://api.nuget.org/v3/index.json
    dotnet build test/package-smoke/StandaloneEtwConsumer/StandaloneEtwConsumer.csproj --no-restore --configuration Release
    dotnet restore test/package-smoke/DistroConsumer/DistroConsumer.csproj --source ./packages --source https://api.nuget.org/v3/index.json
    dotnet build test/package-smoke/DistroConsumer/DistroConsumer.csproj --no-restore --configuration Release
```

- [ ] **Step 6: Run the complete validation suite**

Run:

```powershell
dotnet test Microsoft.OpenTelemetry.slnx --no-build --configuration Release
dotnet build test\package-smoke\StandaloneEtwConsumer\StandaloneEtwConsumer.csproj --configuration Release
dotnet build test\package-smoke\DistroConsumer\DistroConsumer.csproj --configuration Release
```

Expected: all tests and both package-consumer builds pass.

- [ ] **Step 7: Commit documentation and package validation**

```powershell
git add docs\standalone-agent365-etw.md test\package-smoke .github\workflows\ci.yml
git commit -m "test: validate standalone ETW package consumption"
```

---

## External BotDesigner validation

After the three packages are available in BotDesigner's package feed:

1. Replace `Microsoft.Agents.A365.Observability.Runtime` with `Microsoft.Agents.A365.Observability.Etw` in `/src/Infrastructure/Common/Microsoft.CCI.Common/Microsoft.CCI.Common.csproj`.
2. Replace `ExportFormatter` with `EtwExportFormatter` in `/src/Infrastructure/Common/Microsoft.CCI.Common/Logging/A365Observability/A365ObservabilityEtwLogger.cs`.
3. Build the BotDesigner `Microsoft.CCI.Common` project.
4. Run `A365ObservabilityEtwLoggerTests` and `A365ObservabilityRequiredFieldsTests`.
5. Verify invoke-agent, execute-tool, output-messages, inference, and apply-guardrail JSON payload snapshots remain unchanged.
