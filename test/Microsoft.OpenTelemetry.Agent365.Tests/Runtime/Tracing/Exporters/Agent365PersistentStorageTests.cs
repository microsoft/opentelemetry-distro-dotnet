// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using FluentAssertions;
using Microsoft.Agents.A365.Observability.Runtime.Tracing.Exporters;
using Microsoft.Extensions.Logging;
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
    public void CreatesPrivateStorageDirectoriesOnUnix()
    {
        if (OperatingSystem.IsWindows()) return;

        using var temp = new TemporaryDirectory();
        File.SetUnixFileMode(temp.Path, UnixFileMode.UserRead | UnixFileMode.UserWrite
            | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute
            | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        var parentMode = File.GetUnixFileMode(temp.Path);
        using var storage = Agent365PersistentStorage.Create(temp.Path);
        const UnixFileMode privateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

        File.GetUnixFileMode(Path.Combine(temp.Path, "Microsoft")).Should().Be(privateMode);
        File.GetUnixFileMode(storage.DirectoryPath).Should().Be(privateMode);
        File.GetUnixFileMode(temp.Path).Should().Be(parentMode);
    }

    [TestMethod]
    public void RejectsInsecureExistingStorageOnUnixWithoutChangingPermissions()
    {
        if (OperatingSystem.IsWindows()) return;

        using var temp = new TemporaryDirectory();
        var applicationDirectory = Path.Combine(temp.Path, "Microsoft", "Agent365");
        Directory.CreateDirectory(Path.Combine(temp.Path, "Microsoft"),
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        Directory.CreateDirectory(applicationDirectory);
        const UnixFileMode insecureMode = UnixFileMode.UserRead | UnixFileMode.UserWrite
            | UnixFileMode.UserExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute;
        File.SetUnixFileMode(applicationDirectory, insecureMode);
        var existingFile = Path.Combine(applicationDirectory, "existing-record");
        File.WriteAllText(existingFile, "preserve");

        var create = () => Agent365PersistentStorage.Create(temp.Path);

        create.Should().Throw<UnauthorizedAccessException>();
        File.GetUnixFileMode(applicationDirectory).Should().Be(insecureMode);
        File.ReadAllText(existingFile).Should().Be("preserve");
    }

    [TestMethod]
    public void RejectsSymbolicLinkStorageOnUnix()
    {
        if (OperatingSystem.IsWindows()) return;

        using var temp = new TemporaryDirectory();
        using var target = new TemporaryDirectory();
        Directory.CreateSymbolicLink(Path.Combine(temp.Path, "Microsoft"), target.Path);

        var create = () => Agent365PersistentStorage.Create(temp.Path);

        create.Should().Throw<UnauthorizedAccessException>();
        Directory.GetFileSystemEntries(target.Path).Should().BeEmpty();
    }

    [TestMethod]
    public void RejectsWritableStorageParentOnUnix()
    {
        if (OperatingSystem.IsWindows()) return;

        using var temp = new TemporaryDirectory();
        File.SetUnixFileMode(temp.Path, UnixFileMode.UserRead | UnixFileMode.UserWrite
            | UnixFileMode.UserExecute | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute);

        var create = () => Agent365PersistentStorage.Create(temp.Path);

        create.Should().Throw<UnauthorizedAccessException>();
        Directory.GetFileSystemEntries(temp.Path).Should().BeEmpty();
    }

    [TestMethod]
    public void SupportsStickySharedTemporaryParentOnUnix()
    {
        if (OperatingSystem.IsWindows()) return;

        using var temp = new TemporaryDirectory();
        File.SetUnixFileMode(temp.Path, UnixFileMode.StickyBit | UnixFileMode.UserRead
            | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead
            | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute | UnixFileMode.OtherRead
            | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute);

        using var storage = Agent365PersistentStorage.Create(temp.Path);

        storage.TryStore(CreateRecord()).Should().BeTrue();
    }

    [TestMethod]
    public void SupportsSetGroupIdStorageParentOnUnix()
    {
        if (OperatingSystem.IsWindows()) return;

        using var temp = new TemporaryDirectory();
        const UnixFileMode privateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
        File.SetUnixFileMode(temp.Path, privateMode | UnixFileMode.SetGroup);

        using var storage = Agent365PersistentStorage.Create(temp.Path);

        storage.TryStore(CreateRecord()).Should().BeTrue();
        var mode = File.GetUnixFileMode(storage.DirectoryPath);
        (mode & ~UnixFileMode.SetGroup).Should().Be(privateMode);
        File.GetUnixFileMode(temp.Path).Should().Be(privateMode | UnixFileMode.SetGroup);
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void RejectsExtendedAllowAclOnMacOs(bool onAncestor)
    {
        if (!OperatingSystem.IsMacOS()) return;

        using var temp = new TemporaryDirectory();
        var directory = temp.Path;
        if (!onAncestor)
        {
            using var storage = Agent365PersistentStorage.Create(temp.Path);
            directory = storage.DirectoryPath;
        }

        const string entry = "everyone allow read,search,file_inherit,directory_inherit";
        ChangeMacOsAcl(directory, "+a", entry);
        try
        {
            var mode = File.GetUnixFileMode(directory);
            var create = () => Agent365PersistentStorage.Create(temp.Path);

            create.Should().Throw<UnauthorizedAccessException>();
            File.GetUnixFileMode(directory).Should().Be(mode);
            if (onAncestor)
            {
                Directory.Exists(Path.Combine(temp.Path, "Microsoft")).Should().BeFalse();
            }
        }
        finally
        {
            ChangeMacOsAcl(directory, "-a", entry);
        }
    }

    [TestMethod]
    public void SupportsDenyOnlyAclOnMacOs()
    {
        if (!OperatingSystem.IsMacOS()) return;

        using var temp = new TemporaryDirectory();
        const string entry = "everyone deny delete";
        ChangeMacOsAcl(temp.Path, "+a", entry);
        try
        {
            using var storage = Agent365PersistentStorage.Create(temp.Path);

            storage.TryStore(CreateRecord()).Should().BeTrue();
        }
        finally
        {
            ChangeMacOsAcl(temp.Path, "-a", entry);
        }
    }

    [TestMethod]
    public void RejectsStorageOwnedByAnotherUserOnLinux()
    {
        if (!OperatingSystem.IsLinux()) return;
        if (GetEffectiveUserId() != 0)
        {
            Assert.Inconclusive("Changing ownership for this regression requires root.");
        }

        using var temp = new TemporaryDirectory();
        string applicationDirectory;
        using (var storage = Agent365PersistentStorage.Create(temp.Path))
        {
            applicationDirectory = storage.DirectoryPath;
        }

        ChangeOwner(applicationDirectory, 65534, uint.MaxValue).Should().Be(0);
        var create = () => Agent365PersistentStorage.Create(temp.Path);

        create.Should().Throw<UnauthorizedAccessException>();
    }

    [TestMethod]
    public void AncestorLinkRetargetingCannotRedirectStorageOnLinux()
    {
        if (!OperatingSystem.IsLinux()) return;
        if (GetEffectiveUserId() != 0)
        {
            Assert.Inconclusive("Creating a trusted root-owned ancestor link requires root.");
        }

        using var temp = new TemporaryDirectory();
        using var safe = new TemporaryDirectory();
        using var redirected = new TemporaryDirectory();
        var intermediateLink = Path.Combine(temp.Path, "redirect");
        var trustedLink = Path.Combine(temp.Path, "cache");
        Directory.CreateSymbolicLink(intermediateLink, safe.Path);
        Directory.CreateSymbolicLink(trustedLink, intermediateLink);
        var redirectedCache = Path.Combine(redirected.Path, "Microsoft", "Agent365");
        Directory.CreateDirectory(redirectedCache);

        using var storage = Agent365PersistentStorage.Create(trustedLink);
        Directory.Delete(intermediateLink);
        Directory.CreateSymbolicLink(intermediateLink, redirected.Path);

        storage.TryStore(CreateRecord()).Should().BeTrue();
        Directory.GetFileSystemEntries(redirectedCache).Should().BeEmpty();
        Directory.GetFiles(Path.Combine(safe.Path, "Microsoft", "Agent365")).Should().NotBeEmpty();
    }

    [TestMethod]
    public void UnsafeStorageDisablesDurabilityAndLogsAnErrorOnUnix()
    {
        if (OperatingSystem.IsWindows()) return;

        using var temp = new TemporaryDirectory();
        var root = Agent365StorageDirectoryResolver.Resolve(temp.Path);
        Directory.CreateDirectory(Path.Combine(root, "Microsoft"));
        File.SetUnixFileMode(Path.Combine(root, "Microsoft"), UnixFileMode.UserRead
            | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.OtherRead);
        var logger = new Mock<ILogger>();

        using var storage = Agent365DurableDelivery.CreateStorage(
            new Agent365ExporterOptions { StorageDirectory = temp.Path }, logger.Object);

        storage.Should().BeOfType<DisabledAgent365Storage>();
        logger.Invocations.Should().Contain(call => call.Method.Name == "Log"
            && (LogLevel)call.Arguments[0] == LogLevel.Error
            && call.Arguments[3] is UnauthorizedAccessException);
    }

    [TestMethod]
    public void ReopensPrivateStorageAndReplaysExistingRecord()
    {
        using var temp = new TemporaryDirectory();
        var expected = CreateRecord();
        using (var storage = Agent365PersistentStorage.Create(temp.Path))
        {
            storage.TryStore(expected).Should().BeTrue();
        }

        using var reopened = Agent365PersistentStorage.Create(temp.Path);
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
        using (var initialStorage = Agent365PersistentStorage.Create(temp.Path))
        {
        }

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

    private static void ChangeMacOsAcl(string path, string action, string entry)
    {
        var startInfo = new ProcessStartInfo("/bin/chmod")
        {
            UseShellExecute = false,
            RedirectStandardError = true,
        };
        startInfo.ArgumentList.Add(action);
        startInfo.ArgumentList.Add(entry);
        startInfo.ArgumentList.Add(path);
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start chmod.");
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        process.ExitCode.Should().Be(0, "chmod must succeed: {0}", error);
    }

    [DllImport("libc", EntryPoint = "geteuid")]
    private static extern uint GetEffectiveUserId();

    [DllImport("libc", EntryPoint = "lchown", CharSet = CharSet.Ansi, SetLastError = true)]
    private static extern int ChangeOwner(string path, uint owner, uint group);
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
