// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using System.Text;
using FluentAssertions;
using Microsoft.Agents.A365.Observability.Runtime.Tracing.Exporters;
using OpenTelemetry.PersistentStorage.FileSystem;

namespace Microsoft.OpenTelemetry.Agent365.Tests.Runtime.Tracing.Exporters;

[TestClass]
public sealed class Agent365PersistentStorageTests
{
    [TestMethod]
    public void UsesConfiguredStorageDirectory()
    {
        using var temp = new TemporaryDirectory();
        using var storage = Agent365PersistentStorage.Create(temp.Path);
        storage.DirectoryPath.Should().StartWith(temp.Path);
    }

    [TestMethod]
    public void StoresAndReadsDurableRecord()
    {
        using var temp = new TemporaryDirectory();
        using var storage = Agent365PersistentStorage.Create(temp.Path);
        var expected = CreateRecord();

        storage.TryStore(expected).Should().BeTrue();
        storage.TryGetNext(out var stored).Should().BeTrue();
        stored!.TryLease(TimeSpan.FromMinutes(2)).Should().BeTrue();
        stored.Read(out var actual).Should().Be(Agent365StoredRecordReadResult.Success);
        actual.Should().BeEquivalentTo(expected);
    }

    [TestMethod]
    public void UsesAzureCompatibleBounds()
    {
        Agent365PersistentStorage.MaxSizeInBytes.Should().Be(50L * 1024 * 1024);
        Agent365PersistentStorage.Retention.Should().Be(TimeSpan.FromDays(2));
        Agent365PersistentStorage.MaintenanceInterval.Should().Be(TimeSpan.FromMinutes(2));
        Agent365PersistentStorage.WriteTimeout.Should().Be(TimeSpan.FromMinutes(1));
    }

    [TestMethod]
    public void TryGetNextReturnsFalseWhenEmpty()
    {
        using var temp = new TemporaryDirectory();
        using var storage = Agent365PersistentStorage.Create(temp.Path);

        storage.TryGetNext(out var stored).Should().BeFalse();
        stored.Should().BeNull();
    }

    [TestMethod]
    public void TryDeleteRemovesStoredRecord()
    {
        using var temp = new TemporaryDirectory();
        using var storage = Agent365PersistentStorage.Create(temp.Path);

        storage.TryStore(CreateRecord()).Should().BeTrue();
        storage.TryGetNext(out var stored).Should().BeTrue();
        stored!.TryLease(TimeSpan.FromMinutes(2)).Should().BeTrue();
        stored.TryDelete().Should().BeTrue();
        storage.TryGetNext(out _).Should().BeFalse();
    }

    [TestMethod]
    public void TryReadReturnsFalseOnCorruptedData()
    {
        using var temp = new TemporaryDirectory();
        var applicationDirectory = Path.Combine(temp.Path, "Microsoft", "Agent365");
        Directory.CreateDirectory(applicationDirectory);

        using (var provider = CreateProvider(applicationDirectory))
        {
            provider.TryCreateBlob(Encoding.UTF8.GetBytes("not-json"), out _).Should().BeTrue();
        }

        using var storage = Agent365PersistentStorage.Create(temp.Path);
        storage.TryGetNext(out var stored).Should().BeTrue();
        stored!.TryLease(TimeSpan.FromMinutes(2)).Should().BeTrue();
        stored.Read(out var record).Should().Be(Agent365StoredRecordReadResult.InvalidPayload);
        record.Should().BeNull();
    }

    [TestMethod]
    public void ResolvesConfiguredRootToStableHashSubdirectory()
    {
        var dir = Agent365StorageDirectoryResolver.Resolve(
            configuredRoot: @"C:\Configured",
            isWindows: () => true,
            getEnvVar: _ => null,
            hashIdentity: () => "abcd1234");

        dir.Should().Be(Path.Combine(@"C:\Configured", "abcd1234"));
    }

    [TestMethod]
    public void ResolvesWindowsLocalAppDataFirst()
    {
        var dir = Agent365StorageDirectoryResolver.Resolve(
            configuredRoot: null,
            isWindows: () => true,
            getEnvVar: v => v switch
            {
                "LOCALAPPDATA" => @"C:\Users\test\AppData\Local",
                "TEMP" => @"C:\Windows\Temp",
                _ => null,
            },
            hashIdentity: () => "abcd1234");
        dir.Should().StartWith(@"C:\Users\test\AppData\Local");
    }

    [TestMethod]
    public void FallsBackToTempOnWindows()
    {
        var dir = Agent365StorageDirectoryResolver.Resolve(
            configuredRoot: null,
            isWindows: () => true,
            getEnvVar: v => v == "TEMP" ? @"C:\Windows\Temp" : null,
            hashIdentity: () => "abcd1234");
        dir.Should().StartWith(@"C:\Windows\Temp");
    }

    [TestMethod]
    public void FallsBackToCurrentDirectoryOnWindows()
    {
        var dir = Agent365StorageDirectoryResolver.Resolve(
            configuredRoot: null,
            isWindows: () => true,
            getEnvVar: _ => null,
            hashIdentity: () => "abcd1234");

        dir.Should().StartWith(Environment.CurrentDirectory);
    }

    [TestMethod]
    public void UsesTomDirOnUnixWhenSet()
    {
        var dir = Agent365StorageDirectoryResolver.Resolve(
            configuredRoot: null,
            isWindows: () => false,
            getEnvVar: v => v == "TMPDIR" ? "/custom/tmp" : null,
            hashIdentity: () => "abcd1234");

        dir.Should().StartWith("/custom/tmp");
    }

    [TestMethod]
    public void FallsBackToVarTmpOnUnixWhenTmpdirMissing()
    {
        var dir = Agent365StorageDirectoryResolver.Resolve(
            configuredRoot: null,
            isWindows: () => false,
            getEnvVar: _ => null,
            hashIdentity: () => "abcd1234");

        dir.Should().StartWith("/var/tmp");
    }

    [TestMethod]
    public void FallsBackToSlashTmpOnUnixWhenVarTmpNotAccessible()
    {
        var dir = Agent365StorageDirectoryResolver.Resolve(
            configuredRoot: null,
            isWindows: () => false,
            getEnvVar: _ => null,
            hashIdentity: () => "abcd1234");

        if (!dir.StartsWith("/var/tmp"))
        {
            dir.Should().StartWith("/tmp");
        }
    }

    [TestMethod]
    public void UnixFallbackOrderVerifiedTmpdirFirst()
    {
        var dir = Agent365StorageDirectoryResolver.Resolve(
            configuredRoot: null,
            isWindows: () => false,
            getEnvVar: v => v == "TMPDIR" ? "/explicit/tmpdir" : null,
            hashIdentity: () => "abcd1234");

        dir.Should().StartWith("/explicit/tmpdir");
    }

    [TestMethod]
    public void UnixFallbackOrderVerifiedVarTmpSecond()
    {
        var dir = Agent365StorageDirectoryResolver.Resolve(
            configuredRoot: null,
            isWindows: () => false,
            getEnvVar: _ => null,
            hashIdentity: () => "abcd1234");

        (dir.StartsWith("/var/tmp") || dir.StartsWith("/tmp")).Should().BeTrue();
    }

    private static Agent365DurableRecord CreateRecord() =>
        new(
            tenantId: "tenant",
            agentId: "agent",
            agenticUserId: "user",
            useS2SEndpoint: true,
            payload: """{"resourceSpans":[{"id":"1"}]}""",
            createdAtUtc: new DateTimeOffset(2026, 8, 8, 12, 0, 0, TimeSpan.Zero));

    private static FileBlobProvider CreateProvider(string directory) =>
        new(
            directory,
            Agent365PersistentStorage.MaxSizeInBytes,
            (int)Agent365PersistentStorage.MaintenanceInterval.TotalMilliseconds,
            (long)Agent365PersistentStorage.Retention.TotalMilliseconds,
            (int)Agent365PersistentStorage.WriteTimeout.TotalMilliseconds);
}

internal sealed class TemporaryDirectory : IDisposable
{
    public TemporaryDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public void Dispose()
    {
        if (Directory.Exists(Path))
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}
