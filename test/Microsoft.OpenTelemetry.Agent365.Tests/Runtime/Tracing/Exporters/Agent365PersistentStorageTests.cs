// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using System.Text;
using FluentAssertions;
using Microsoft.Agents.A365.Observability.Runtime.Common;
using Microsoft.Agents.A365.Observability.Runtime.Tracing.Exporters;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
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

        storage.TryStore(CreateRecord()).Should().BeTrue();

        Directory.GetFiles(Path.Combine(temp.Path, "Microsoft", "Agent365")).Should().NotBeEmpty();
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
    public void ReusesCustomerProvisionedStorageWithoutChangingPermissionsOnUnix()
    {
        if (OperatingSystem.IsWindows()) return;

        using var temp = new TemporaryDirectory();
        var root = Agent365StorageDirectoryResolver.Resolve(temp.Path);
        var applicationDirectory = Path.Combine(root, "Microsoft", "Agent365");
        Directory.CreateDirectory(applicationDirectory);
        const UnixFileMode existingMode = UnixFileMode.UserRead | UnixFileMode.UserWrite
            | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute
            | UnixFileMode.OtherRead | UnixFileMode.OtherExecute;
        File.SetUnixFileMode(applicationDirectory, existingMode);
        var rootMode = File.GetUnixFileMode(temp.Path);
        var existingFile = Path.Combine(applicationDirectory, "existing-record");
        File.WriteAllText(existingFile, "preserve");

        using var storage = Agent365DurableDelivery.CreateStorage(
            new Agent365ExporterOptions { StorageDirectory = temp.Path }, NullLogger.Instance);

        storage.Should().BeOfType<Agent365PersistentStorage>();
        storage.TryStore(CreateRecord()).Should().BeTrue();
        Directory.GetFiles(applicationDirectory, "*.blob").Should().NotBeEmpty();
        File.GetUnixFileMode(applicationDirectory).Should().Be(existingMode);
        File.GetUnixFileMode(temp.Path).Should().Be(rootMode);
        File.ReadAllText(existingFile).Should().Be("preserve");
    }

    [TestMethod]
    public void UnprovisionedUnixStorageDisablesDurabilityAndLogsAnError()
    {
        if (OperatingSystem.IsWindows()) return;

        using var temp = new TemporaryDirectory();
        var root = Path.Combine(temp.Path, "not-provisioned");
        var logger = new Mock<ILogger>();

        using var storage = Agent365DurableDelivery.CreateStorage(
            new Agent365ExporterOptions { StorageDirectory = root }, logger.Object);

        storage.Should().BeOfType<DisabledAgent365Storage>();
        logger.Invocations.Should().Contain(call => call.Method.Name == "Log"
            && (LogLevel)call.Arguments[0] == LogLevel.Error
            && call.Arguments[3] is DirectoryNotFoundException);
        Directory.Exists(root).Should().BeFalse();
    }

    [TestMethod]
    public void ReopensStorageAndReplaysExistingRecord()
    {
        using var temp = new TemporaryDirectory();
        var options = new Agent365ExporterOptions { StorageDirectory = temp.Path };
        var expected = CreateRecord();
        using (var storage = Agent365DurableDelivery.CreateStorage(options, NullLogger.Instance))
        {
            storage.TryStore(expected).Should().BeTrue();
        }

        using var reopened = Agent365DurableDelivery.CreateStorage(options, NullLogger.Instance);
        reopened.TryGetNext(out var stored).Should().BeTrue();
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

    [DataTestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow(" ")]
    public void RequiresConfiguredStorageDirectoryOnUnix(string? configuredRoot)
    {
        var resolve = () => Agent365StorageDirectoryResolver.Resolve(
            configuredRoot: configuredRoot,
            isWindows: () => false,
            getEnvVar: _ => Path.GetTempPath(),
            hashIdentity: () => "abcd1234");

        resolve.Should().Throw<InvalidOperationException>().WithMessage("*StorageDirectory*");
    }

    [TestMethod]
    public void RequiresExistingStorageRootOnUnix()
    {
        using var temp = new TemporaryDirectory();
        var missingRoot = Path.Combine(temp.Path, "not-provisioned");
        var resolve = () => Agent365StorageDirectoryResolver.Resolve(
            configuredRoot: missingRoot,
            isWindows: () => false,
            getEnvVar: _ => null,
            hashIdentity: () => "abcd1234");

        resolve.Should().Throw<DirectoryNotFoundException>();
        Directory.Exists(missingRoot).Should().BeFalse();
    }

    [TestMethod]
    public void RequiresAbsoluteStorageRootOnUnix()
    {
        var resolve = () => Agent365StorageDirectoryResolver.Resolve(
            configuredRoot: "relative-storage",
            isWindows: () => false,
            getEnvVar: _ => null,
            hashIdentity: () => "abcd1234");

        resolve.Should().Throw<ArgumentException>().WithMessage("*absolute*");
    }

    [TestMethod]
    public void ResolvesProvisionedUnixRootWithoutConsultingTemporaryDirectories()
    {
        using var temp = new TemporaryDirectory();
        var dir = Agent365StorageDirectoryResolver.Resolve(
            configuredRoot: temp.Path,
            isWindows: () => false,
            getEnvVar: _ => throw new InvalidOperationException("Temporary directories must not be consulted."),
            hashIdentity: () => "abcd1234");

        dir.Should().Be(Path.Combine(temp.Path, "abcd1234"));
    }

    [TestMethod]
    public void MissingUnixStorageRootDisablesDurabilityAndLogsAnError()
    {
        if (OperatingSystem.IsWindows()) return;

        var logger = new Mock<ILogger>();
        using var storage = Agent365DurableDelivery.CreateStorage(new Agent365ExporterOptions(), logger.Object);

        storage.Should().BeOfType<DisabledAgent365Storage>();
        logger.Invocations.Should().Contain(call => call.Method.Name == "Log"
            && (LogLevel)call.Arguments[0] == LogLevel.Error
            && call.Arguments[3] is InvalidOperationException
            && ((Exception)call.Arguments[3]).Message.Contains("StorageDirectory"));
    }

    [TestMethod]
    public void CoreWithoutInjectedStorageDisablesDurabilityAndLogsAnErrorOnUnix()
    {
        if (OperatingSystem.IsWindows()) return;

        var logger = new Mock<ILogger<Agent365ExporterCore>>();
        var core = new Agent365ExporterCore(
            new ExportFormatter(NullLogger<ExportFormatter>.Instance),
            logger.Object, utcNow: null, storage: null);

        core.Storage.Should().BeOfType<DisabledAgent365Storage>();
        logger.Invocations.Should().Contain(call => call.Method.Name == "Log"
            && (LogLevel)call.Arguments[0] == LogLevel.Error
            && call.Arguments[3] is InvalidOperationException);
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
        if (OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(Path);
        }
        else
        {
            Directory.CreateDirectory(Path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
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
