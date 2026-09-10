// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using FluentAssertions;

namespace Microsoft.OpenTelemetry.Package.Tests;

[TestClass]
public sealed class PackageDependencyTests
{
    private const string ContractsPackageId = "Microsoft.Agents.A365.Observability.Contracts";
    private const string DistroPackageId = "Microsoft.OpenTelemetry";
    private const string EtwPackageId = "Microsoft.Agents.A365.Observability.Etw";
    private const string ValidationVersion = "1.2.0-validation";

    [TestMethod]
    public void DistroNuspecDoesNotDependOnInternalPackages()
    {
        using var archive = PackageArchive.Open(DistroPackageId, ValidationVersion, "nupkg");
        var dependencyIds = GetDependencyIds(archive);

        dependencyIds.Should().NotContain(EtwPackageId);
        dependencyIds.Should().NotContain(ContractsPackageId);
    }

    [TestMethod]
    public void StandaloneEtwNuspecDependsOnContracts()
    {
        using var archive = PackageArchive.Open(EtwPackageId, ValidationVersion, "nupkg");
        var dependencyIds = GetDependencyIds(archive);

        dependencyIds.Should().Contain(ContractsPackageId);
    }

    private static IEnumerable<string> GetDependencyIds(System.IO.Compression.ZipArchive archive)
    {
        var nuspec = PackageArchive.ReadNuspec(archive);
        var packageNamespace = nuspec.Root!.Name.Namespace;

        return nuspec.Descendants(packageNamespace + "dependency")
            .Select(dependency => dependency.Attribute("id")?.Value)
            .Where(id => id is not null)
            .Cast<string>();
    }
}
