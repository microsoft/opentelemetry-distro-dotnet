# PR 156 Packaging Contract Design

## Goal

Merge the latest `main` into PR #156 and preserve three independently buildable
source projects while producing NuGet packages with the dependency and assembly
composition required by the external release pipeline.

The release pipeline is outside this repository. This repository is responsible
for producing correct package artifacts; feed routing remains the release
pipeline's responsibility.

## Source Projects

The repository will contain these three source projects:

1. `Microsoft.Agents.A365.Observability.Contracts`
2. `Microsoft.Agents.A365.Observability.Etw`
3. `Microsoft.OpenTelemetry`

All three projects remain packable so an external build can discover and pack
each artifact.

## Package Contract

### Contracts

`Microsoft.Agents.A365.Observability.Contracts.nupkg` is a standalone public
package containing the Agent365 contracts assembly and its symbols and XML
documentation.

### ETW

`Microsoft.Agents.A365.Observability.Etw.nupkg` is a standalone package intended
for the internal feed. Its nuspec must depend on
`Microsoft.Agents.A365.Observability.Contracts`; it must not embed a duplicate
Contracts assembly.

The project uses a project reference during repository builds. NuGet packing
converts that reference into a package dependency using the Contracts project's
package identity and version.

### Microsoft OpenTelemetry

`Microsoft.OpenTelemetry.nupkg` remains the public umbrella package.

It must:

- contain the `Microsoft.OpenTelemetry` assembly;
- embed the ETW assembly, XML documentation, and portable PDB for every target
  framework;
- not embed the Contracts assembly;
- declare a NuGet dependency on
  `Microsoft.Agents.A365.Observability.Contracts`;
- not declare a dependency on the internal ETW package.

The source project references ETW privately so it can compile and copy the ETW
build outputs into the umbrella package without exposing an ETW package
dependency. It references Contracts non-privately so packing creates the public
Contracts package dependency.

## Merge Strategy

Work in the existing isolated `feature/standalone-etw-sdk` worktree and create a
merge commit from the latest `origin/main`. Do not rebase or rewrite the PR's
history.

Conflict resolution will preserve the package extraction and compatibility
surface from the PR while taking the latest behavior and tests from `main`.
Files moved into Contracts or ETW will receive the corresponding upstream
changes in their new locations. Type-forwarding and compatibility shims in
`Microsoft.OpenTelemetry` will be retained.

Existing uncommitted package-consumer validation work will be retained where it
supports this package contract and adjusted where it assumes Contracts is
embedded in the umbrella package.

## Validation

Automated package validation will pack all three projects with deterministic
validation versions and inspect the resulting archives.

Tests will prove:

- all three nupkgs are produced;
- the ETW nuspec depends on Contracts;
- the Microsoft OpenTelemetry nuspec depends on Contracts and not ETW;
- the Microsoft OpenTelemetry package contains ETW outputs and excludes
  Contracts outputs;
- a consumer restores Microsoft OpenTelemetry plus Contracts without requiring
  the ETW package;
- a consumer restores ETW plus Contracts without requiring Microsoft
  OpenTelemetry;
- symbols are portable and present for the expected assemblies and target
  frameworks.

The final verification will include a Release solution build, targeted package
tests, consumer restore/build smoke tests, and package archive inspection.

## Publication Boundary

This repository cannot enforce which feed receives a generated nupkg. The
external release pipeline must publish Contracts and Microsoft OpenTelemetry to
public NuGet and publish ETW only to the internal feed. The package IDs and
artifact separation make that routing possible without repacking or modifying
the artifacts.
