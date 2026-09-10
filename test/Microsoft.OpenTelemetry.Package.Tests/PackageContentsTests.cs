// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using FluentAssertions;
using FluentAssertions.Execution;
using System.Text;

namespace Microsoft.OpenTelemetry.Package.Tests;

[TestClass]
public sealed class PackageContentsTests
{
    private const string DistroPackageId = "Microsoft.OpenTelemetry";
    private const string ValidationVersion = "1.2.0-validation";

    private static readonly string[] AssemblyNames =
    [
        "Microsoft.OpenTelemetry",
        "Microsoft.Agents.A365.Observability.Etw",
        "Microsoft.Agents.A365.Observability.Contracts",
    ];

    [DataTestMethod]
    [DataRow("netstandard2.0")]
    [DataRow("net8.0")]
    public void DistroNupkgContainsAllAssembliesAndXmlDocumentation(string targetFramework)
    {
        using var archive = PackageArchive.Open(DistroPackageId, ValidationVersion, "nupkg");
        var entries = archive.Entries.Select(entry => entry.FullName);
        var expectedEntries = AssemblyNames.SelectMany(assemblyName => new[]
        {
            $"lib/{targetFramework}/{assemblyName}.dll",
            $"lib/{targetFramework}/{assemblyName}.xml",
        });

        entries.Should().Contain(expectedEntries);
    }

    [DataTestMethod]
    [DataRow("netstandard2.0")]
    [DataRow("net8.0")]
    public void DistroSnupkgContainsPortablePdbsForAllAssemblies(string targetFramework)
    {
        using var archive = PackageArchive.Open(DistroPackageId, ValidationVersion, "snupkg");

        using var scope = new AssertionScope();
        foreach (var assemblyName in AssemblyNames)
        {
            var expectedEntry = $"lib/{targetFramework}/{assemblyName}.pdb";
            var entry = archive.GetEntry(expectedEntry);
            entry.Should().NotBeNull($"the symbol package should contain {expectedEntry}");

            if (entry is null)
            {
                continue;
            }

            using var stream = entry.Open();
            var signature = new byte[4];
            stream.ReadExactly(signature);
            Encoding.ASCII.GetString(signature).Should().Be(
                "BSJB",
                $"{expectedEntry} should be a portable PDB");
        }
    }
}
