# PR 156 Packaging Contract Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Merge current `main` into PR #156 and make its three NuGet artifacts satisfy the approved public Contracts, internal ETW, and public umbrella-package contract.

**Architecture:** Keep three source projects and use project references for repository builds. NuGet converts the non-private Contracts project references into package dependencies, while a private ETW project reference plus an explicit pack target embeds ETW outputs directly in `Microsoft.OpenTelemetry` without creating an ETW package dependency.

**Tech Stack:** .NET SDK/MSBuild, central package management, NuGet pack/nuspec generation, MSTest, FluentAssertions, GitHub Actions.

## Global Constraints

- Keep exactly three source projects: `Microsoft.Agents.A365.Observability.Contracts`, `Microsoft.Agents.A365.Observability.Etw`, and `Microsoft.OpenTelemetry`.
- All three projects remain packable so the external build can discover all artifacts.
- `Microsoft.Agents.A365.Observability.Etw.nupkg` depends on `Microsoft.Agents.A365.Observability.Contracts`.
- `Microsoft.OpenTelemetry.nupkg` embeds ETW DLL/XML/PDB but does not embed Contracts outputs.
- `Microsoft.OpenTelemetry.nupkg` depends on Contracts and does not depend on ETW.
- Feed routing is outside this repository; do not modify or invent an external release pipeline.
- Merge `origin/main`; do not rebase or rewrite PR history.
- Preserve current upstream Agent365 behavior when relocating conflict changes into the extracted Contracts and ETW projects.

---

### Task 1: Merge Current Main Into the PR Branch

**Files:**
- Modify through merge: `CHANGELOG.md`
- Modify through merge: `Directory.Packages.props`
- Modify through merge: `Microsoft.OpenTelemetry.slnx`
- Modify through merge: `src/Microsoft.OpenTelemetry/.publicApi/PublicAPI.Unshipped.txt`
- Modify through merge: `src/Microsoft.Agents.A365.Observability.Contracts/DTOs/Builders/ExecuteToolDataBuilder.cs`
- Modify through merge: `src/Microsoft.Agents.A365.Observability.Contracts/Tracing/Contracts/ToolCallDetails.cs`
- Create through merge relocation: `src/Microsoft.Agents.A365.Observability.Contracts/Tracing/Contracts/TransferDetails.cs`
- Modify through merge: `test/Microsoft.OpenTelemetry.Agent365.Tests/Runtime/Etw/EtwLoggingBuilderTests.cs`
- Modify corresponding moved tests under `test/Microsoft.Agents.A365.Observability.Contracts.Tests`

**Interfaces:**
- Consumes: PR head `feature/standalone-etw-sdk`, latest `origin/main`.
- Produces: A merge commit with no unresolved paths and the latest upstream transfer-details, exporter, SDK-stats, and S2S sample behavior.

- [ ] **Step 1: Preserve the existing package-validation work**

```powershell
git status --short
git stash push --include-untracked --message "pr-156 package consumer validation before main merge"
git stash list
```

Expected: the four modified test project files and `ConsumerRestoreTests.cs` are stored in `stash@{0}`, and the worktree is clean.

- [ ] **Step 2: Refresh remote refs**

```powershell
git fetch origin main feature/standalone-etw-sdk
git rev-parse origin/main
git rev-parse origin/feature/standalone-etw-sdk
```

Expected: both commands print full commit SHAs.

- [ ] **Step 3: Merge main and expose conflicts**

```powershell
git merge --no-ff origin/main
git status --short
```

Expected before resolution: conflicts include the shared changelog/package/solution/API-baseline files and delete/modify conflicts for types moved from `Microsoft.OpenTelemetry` into Contracts.

- [ ] **Step 4: Resolve structural conflicts**

Use these resolution rules:

1. Keep all projects and examples added independently by both branches in `Microsoft.OpenTelemetry.slnx`.
2. Keep package versions added by both branches in `Directory.Packages.props`, with one entry per package ID.
3. Merge both sets of `CHANGELOG.md` entries under `Unreleased`.
4. For upstream changes to files deleted from `src/Microsoft.OpenTelemetry/Agent365/Runtime/DTOs` or `Tracing/Contracts`, apply the upstream behavior to the corresponding file under `src/Microsoft.Agents.A365.Observability.Contracts`.
5. Add upstream `TransferDetails.cs` to the Contracts project location, not the old distro location.
6. Keep the distro API baseline limited to APIs still declared by the distro plus required type-forward entries; put moved-type baselines in the Contracts project.
7. Retain latest upstream ETW test cases while updating namespaces/references for the extracted ETW project.
8. Accept non-conflicting latest-main exporter, SDK-stats, and S2S sample changes.

After editing:

```powershell
git add CHANGELOG.md Directory.Packages.props Microsoft.OpenTelemetry.slnx src test examples
git diff --check
git status --short
git commit
```

Expected: `git diff --check` prints nothing; the commit completes as a merge commit; no `UU`, `AA`, `DU`, or `UD` paths remain.

- [ ] **Step 5: Reapply the preserved validation work**

```powershell
git stash pop
git status --short
```

Expected: the validation edits return. Resolve any stash conflicts in favor of the package contract defined in the global constraints.

---

### Task 2: Define Failing Package Contract Tests

**Files:**
- Modify: `test/Microsoft.OpenTelemetry.Package.Tests/PackageDependencyTests.cs`
- Modify: `test/Microsoft.OpenTelemetry.Package.Tests/PackageContentsTests.cs`
- Modify: `test/Microsoft.OpenTelemetry.Package.Tests/Microsoft.OpenTelemetry.Package.Tests.csproj`
- Create or modify: `test/Microsoft.OpenTelemetry.Package.Tests/ConsumerRestoreTests.cs`
- Modify: `test/package-smoke/DistroConsumer/DistroConsumer.csproj`
- Modify: `test/package-smoke/StandaloneEtwConsumer/StandaloneEtwConsumer.csproj`

**Interfaces:**
- Consumes: packages placed under `$(PackageValidationArtifactsPath)`.
- Produces: assertions for nuspec dependencies, archive contents, and isolated consumer restores.

- [ ] **Step 1: Change the nuspec dependency assertions**

Update `PackageDependencyTests.cs` so it includes these assertions:

```csharp
[TestMethod]
public void DistroNuspecDependsOnContractsButNotEtw()
{
    using var archive = PackageArchive.Open(DistroPackageId, ValidationVersion, "nupkg");
    var dependencyIds = GetDependencyIds(archive);

    dependencyIds.Should().Contain(ContractsPackageId);
    dependencyIds.Should().NotContain(EtwPackageId);
}

[TestMethod]
public void StandaloneEtwNuspecDependsOnContracts()
{
    using var archive = PackageArchive.Open(EtwPackageId, ValidationVersion, "nupkg");
    GetDependencyIds(archive).Should().Contain(ContractsPackageId);
}
```

- [ ] **Step 2: Change the distro archive assertions**

Replace the three-assembly expectation in `PackageContentsTests.cs` with:

```csharp
private static readonly string[] EmbeddedAssemblyNames =
[
    "Microsoft.OpenTelemetry",
    "Microsoft.Agents.A365.Observability.Etw",
];

private const string ContractsAssemblyName =
    "Microsoft.Agents.A365.Observability.Contracts";
```

For each `netstandard2.0` and `net8.0` archive, assert DLL/XML/PDB entries for `EmbeddedAssemblyNames` and assert the following entries are absent:

```csharp
$"lib/{targetFramework}/{ContractsAssemblyName}.dll"
$"lib/{targetFramework}/{ContractsAssemblyName}.xml"
$"lib/{targetFramework}/{ContractsAssemblyName}.pdb"
```

- [ ] **Step 3: Update consumer restore assertions**

In `ConsumerRestoreTests.cs`, make `DistroConsumerRestoresContractsTransitively` use a package source containing both public packages:

```csharp
GetSourcePackages(result.PackageSource).Should().BeEquivalentTo(
[
    $"{DistroPackageId}.{ValidationVersion}.nupkg",
    $"{ContractsPackageId}.{ValidationVersion}.nupkg",
]);

var libraries = ReadPackageLibraries(result.AssetsFile);
GetResolvedVersion(libraries, DistroPackageId).Should().Be(ValidationVersion);
GetResolvedVersion(libraries, ContractsPackageId).Should().Be(ValidationVersion);
libraries.Should().NotContain(
    library => GetPackageId(library).Equals(EtwPackageId, StringComparison.OrdinalIgnoreCase));
```

Assert the consumer output contains all three assemblies because ETW is embedded in the distro and Contracts arrives through the public package dependency.

Keep `StandaloneEtwConsumerRestoresContractsTransitively` isolated to the ETW and Contracts nupkgs and assert that `Microsoft.OpenTelemetry.dll` is absent.

- [ ] **Step 4: Arrange isolated validation package sources**

In `Microsoft.OpenTelemetry.Package.Tests.csproj`, create:

```xml
<DistroPackageSourcePath>$(PackageValidationArtifactsPath)distro-source\</DistroPackageSourcePath>
<EtwPackageSourcePath>$(PackageValidationArtifactsPath)etw-source\</EtwPackageSourcePath>
```

After packing, copy:

```xml
<Copy
  SourceFiles="$(PackageValidationArtifactsPath)Microsoft.OpenTelemetry.$(MicrosoftOpenTelemetryPackageVersion).nupkg;$(PackageValidationArtifactsPath)Microsoft.Agents.A365.Observability.Contracts.$(Agent365EtwSdkPackageVersion).nupkg"
  DestinationFolder="$(DistroPackageSourcePath)" />
<Copy
  SourceFiles="$(PackageValidationArtifactsPath)Microsoft.Agents.A365.Observability.Etw.$(Agent365EtwSdkPackageVersion).nupkg;$(PackageValidationArtifactsPath)Microsoft.Agents.A365.Observability.Contracts.$(Agent365EtwSdkPackageVersion).nupkg"
  DestinationFolder="$(EtwPackageSourcePath)" />
```

- [ ] **Step 5: Run the package tests and confirm the old packaging fails**

```powershell
dotnet test test\Microsoft.OpenTelemetry.Package.Tests\Microsoft.OpenTelemetry.Package.Tests.csproj --configuration Release --verbosity normal
```

Expected: failures show that the distro currently lacks a Contracts dependency and still contains Contracts DLL/XML/PDB.

- [ ] **Step 6: Commit the failing tests**

```powershell
git add test/Microsoft.OpenTelemetry.Package.Tests test/package-smoke
git commit -m "test: define Agent365 package publication contract"
```

---

### Task 3: Correct Project References and Distro Package Composition

**Files:**
- Modify: `src/Microsoft.OpenTelemetry/Microsoft.OpenTelemetry.csproj`
- Verify: `src/Microsoft.Agents.A365.Observability.Contracts/Microsoft.Agents.A365.Observability.Contracts.csproj`
- Verify: `src/Microsoft.Agents.A365.Observability.Etw/Microsoft.Agents.A365.Observability.Etw.csproj`

**Interfaces:**
- Consumes: Contracts project package identity/version and ETW build outputs.
- Produces: three packable nupkgs with the approved dependency graph.

- [ ] **Step 1: Make the distro's Contracts reference a package dependency**

Use these project references in `Microsoft.OpenTelemetry.csproj`:

```xml
<ItemGroup>
  <ProjectReference Include="..\Microsoft.Agents.A365.Observability.Contracts\Microsoft.Agents.A365.Observability.Contracts.csproj" />
  <ProjectReference Include="..\Microsoft.Agents.A365.Observability.Etw\Microsoft.Agents.A365.Observability.Etw.csproj"
                    PrivateAssets="all" />
</ItemGroup>
```

The Contracts reference must not set `PrivateAssets="all"` because NuGet must emit it as a dependency. ETW remains private because its outputs are embedded.

- [ ] **Step 2: Embed only ETW outputs**

Change `IncludeStandaloneEtwSdkBuildOutputs` so `_StandaloneEtwSdkBuildOutput` selects only:

```xml
Condition="'%(ReferenceCopyLocalPaths.ReferenceSourceTarget)' == 'ProjectReference'
  and '%(ReferenceCopyLocalPaths.Filename)' == 'Microsoft.Agents.A365.Observability.Etw'"
```

Keep the existing DLL/XML `BuildOutputInPackage` and PDB `TfmSpecificDebugSymbolsFile` handling.

- [ ] **Step 3: Verify standalone project packability and dependency**

Confirm neither standalone source project sets `<IsPackable>false</IsPackable>`.

Keep this ETW project reference without `PrivateAssets`:

```xml
<ProjectReference Include="..\Microsoft.Agents.A365.Observability.Contracts\Microsoft.Agents.A365.Observability.Contracts.csproj" />
```

- [ ] **Step 4: Run the package tests**

```powershell
dotnet test test\Microsoft.OpenTelemetry.Package.Tests\Microsoft.OpenTelemetry.Package.Tests.csproj --configuration Release --verbosity normal
```

Expected: all package dependency, content, symbol, and consumer restore tests pass.

- [ ] **Step 5: Inspect generated nuspecs directly**

```powershell
$packages = Get-ChildItem target -Recurse -Filter '*.nupkg' |
  Where-Object Name -Match 'Microsoft\.(OpenTelemetry|Agents\.A365\.Observability\.(Contracts|Etw))'
$packages | Select-Object FullName
```

Open each archive through the existing `PackageArchive` tests or `System.IO.Compression.ZipFile` and confirm:

- distro dependency IDs include Contracts and exclude ETW;
- ETW dependency IDs include Contracts;
- distro `lib` entries include ETW and exclude Contracts.

- [ ] **Step 6: Commit the package implementation**

```powershell
git add src/Microsoft.OpenTelemetry/Microsoft.OpenTelemetry.csproj src/Microsoft.Agents.A365.Observability.Contracts src/Microsoft.Agents.A365.Observability.Etw
git commit -m "build: align Agent365 package dependencies"
```

---

### Task 4: Validate the Three-Artifact Build Contract

**Files:**
- Modify only if required by merged project additions: `Microsoft.OpenTelemetry.slnx`
- Modify only if required by test execution: `.github/workflows/ci.yml`

**Interfaces:**
- Consumes: merged solution and all three source project files.
- Produces: repeatable commands the external build can use to discover and pack all three artifacts.

- [ ] **Step 1: Pack each source project explicitly**

```powershell
$out = Join-Path $PWD 'target\package-contract'
Remove-Item $out -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Path $out | Out-Null

dotnet pack src\Microsoft.Agents.A365.Observability.Contracts\Microsoft.Agents.A365.Observability.Contracts.csproj --configuration Release --output $out
dotnet pack src\Microsoft.Agents.A365.Observability.Etw\Microsoft.Agents.A365.Observability.Etw.csproj --configuration Release --output $out
dotnet pack src\Microsoft.OpenTelemetry\Microsoft.OpenTelemetry.csproj --configuration Release --output $out

Get-ChildItem $out -Filter '*.nupkg' | Select-Object Name
```

Expected: one `.nupkg` and one `.snupkg` for each of the three package IDs.

- [ ] **Step 2: Ensure CI exercises package contract tests**

Confirm `Microsoft.OpenTelemetry.Package.Tests` remains in `Microsoft.OpenTelemetry.slnx`; the existing solution test command must execute it. Do not add release publishing or feed-routing logic to `.github/workflows/ci.yml`.

- [ ] **Step 3: Build and test the merged solution**

```powershell
dotnet restore Microsoft.OpenTelemetry.slnx
dotnet build Microsoft.OpenTelemetry.slnx --no-restore --configuration Release
dotnet test Microsoft.OpenTelemetry.slnx --no-build --configuration Release --verbosity normal
```

Expected: restore, build, and all tests exit with code 0.

- [ ] **Step 4: Check repository and package diffs**

```powershell
git diff --check origin/feature/standalone-etw-sdk...HEAD
git status --short
git --no-pager log --oneline --decorate origin/feature/standalone-etw-sdk..HEAD
```

Expected: no whitespace errors, no untracked build artifacts, and only intentional commits after the remote PR head.

- [ ] **Step 5: Commit any final validation wiring**

If solution or CI wiring changed:

```powershell
git add Microsoft.OpenTelemetry.slnx .github/workflows/ci.yml
git commit -m "build: validate all Agent365 package artifacts"
```

If neither file changed, do not create an empty commit.

---

### Task 5: Review and Update PR #156

**Files:**
- Review all changed files against `origin/main`.

**Interfaces:**
- Consumes: completed merge and package implementation.
- Produces: updated remote PR branch with a documented, verified package contract.

- [ ] **Step 1: Run a focused diff review**

```powershell
git diff --stat origin/main...HEAD
git diff --name-status origin/main...HEAD
```

Check that source moves, type forwards, package references, and tests match the design and that no current-main behavior was accidentally dropped.

- [ ] **Step 2: Push the PR branch**

```powershell
git push origin feature/standalone-etw-sdk
```

Expected: a fast-forward update of the PR head; no force push.

- [ ] **Step 3: Confirm GitHub recognizes the resolved merge**

```powershell
gh pr view 156 --repo microsoft/opentelemetry-distro-dotnet --json mergeable,mergeStateStatus,headRefOid,statusCheckRollup
gh pr checks 156 --repo microsoft/opentelemetry-distro-dotnet
```

Expected: the PR is no longer `CONFLICTING`/`DIRTY`; required checks are visible and either queued, running, or passing.

- [ ] **Step 4: Report the requirement matrix**

Report:

1. Three source projects: confirmed.
2. Public artifacts: Contracts and Microsoft OpenTelemetry; feed routing remains external.
3. ETW: embedded in Microsoft OpenTelemetry and independently packable for internal publication.
4. ETW package dependency: Contracts public package.
5. External-build readiness: all three projects pack independently; archive/dependency/consumer tests enforce artifact correctness.
