// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using OpenTelemetry.PersistentStorage.Abstractions;
using OpenTelemetry.PersistentStorage.FileSystem;

namespace Microsoft.Agents.A365.Observability.Runtime.Tracing.Exporters
{
    internal interface IAgent365PersistentStorage : IDisposable
    {
        bool TryStore(Agent365DurableRecord record);

        bool TryGetNext(
#if NETSTANDARD2_0
            out IAgent365StoredRecord? record);
#else
            [NotNullWhen(true)] out IAgent365StoredRecord? record);
#endif
    }

    internal interface IAgent365StoredRecord
    {
        bool TryLease(TimeSpan duration);

        Agent365StoredRecordReadResult Read(
#if NETSTANDARD2_0
            out Agent365DurableRecord? record);
#else
            [NotNullWhen(true)] out Agent365DurableRecord? record);
#endif

        bool TryDelete();
    }

    internal enum Agent365StoredRecordReadResult
    {
        Success,
        ReadFailure,
        InvalidPayload,
    }

    internal sealed class Agent365PersistentStorage : IAgent365PersistentStorage
    {
        private readonly PersistentBlobProvider _provider;

        private Agent365PersistentStorage(PersistentBlobProvider provider, string directoryPath)
        {
            _provider = provider;
            DirectoryPath = directoryPath;
        }

        internal const long MaxSizeInBytes = 50L * 1024 * 1024;
        internal static readonly TimeSpan Retention = TimeSpan.FromDays(2);
        internal static readonly TimeSpan MaintenanceInterval = TimeSpan.FromMinutes(2);
        internal static readonly TimeSpan WriteTimeout = TimeSpan.FromMinutes(1);

        internal string DirectoryPath { get; }

        internal static Agent365PersistentStorage Create(string rootDirectory)
        {
            if (string.IsNullOrWhiteSpace(rootDirectory))
            {
                throw new ArgumentException("Root directory is required.", nameof(rootDirectory));
            }

            var applicationDirectory = Path.Combine(rootDirectory, "Microsoft", "Agent365");
            Directory.CreateDirectory(applicationDirectory);

            return new Agent365PersistentStorage(
                new FileBlobProvider(
                    applicationDirectory,
                    MaxSizeInBytes,
                    (int)MaintenanceInterval.TotalMilliseconds,
                    (long)Retention.TotalMilliseconds,
                    (int)WriteTimeout.TotalMilliseconds),
                applicationDirectory);
        }

        public bool TryStore(Agent365DurableRecord record) =>
            _provider.TryCreateBlob((ReadOnlySpan<byte>)Agent365DurableRecord.Serialize(record), out _);

        public bool TryGetNext(
#if NETSTANDARD2_0
            out IAgent365StoredRecord? record)
#else
            [NotNullWhen(true)] out IAgent365StoredRecord? record)
#endif
        {
            if (_provider.TryGetBlob(out var blob))
            {
                record = new StoredRecord(blob);
                return true;
            }

            record = null;
            return false;
        }

        public void Dispose() => (_provider as IDisposable)?.Dispose();

        private sealed class StoredRecord : IAgent365StoredRecord
        {
            private readonly PersistentBlob _blob;

            internal StoredRecord(PersistentBlob blob)
            {
                _blob = blob;
            }

            public bool TryLease(TimeSpan duration) =>
                _blob.TryLease((int)duration.TotalMilliseconds);

            public Agent365StoredRecordReadResult Read(
#if NETSTANDARD2_0
                out Agent365DurableRecord? record)
#else
                [NotNullWhen(true)] out Agent365DurableRecord? record)
#endif
            {
                if (!_blob.TryRead(out var data))
                {
                    record = null;
                    return Agent365StoredRecordReadResult.ReadFailure;
                }

                return Agent365DurableRecord.TryDeserialize(data, out record)
                    ? Agent365StoredRecordReadResult.Success
                    : Agent365StoredRecordReadResult.InvalidPayload;
            }

            public bool TryDelete() => _blob.TryDelete();
        }
    }

    internal sealed class Agent365StorageDirectoryResolver
    {
        internal static string Resolve(string? configuredRoot) =>
            Resolve(
                configuredRoot,
                isWindows: () => RuntimeInformation.IsOSPlatform(OSPlatform.Windows),
                getEnvVar: Environment.GetEnvironmentVariable,
                hashIdentity: ComputeHashIdentity);

        internal static string Resolve(
            string? configuredRoot,
            Func<bool> isWindows,
            Func<string, string?> getEnvVar,
            Func<string> hashIdentity)
        {
            if (isWindows == null)
            {
                throw new ArgumentNullException(nameof(isWindows));
            }

            if (getEnvVar == null)
            {
                throw new ArgumentNullException(nameof(getEnvVar));
            }

            if (hashIdentity == null)
            {
                throw new ArgumentNullException(nameof(hashIdentity));
            }

            var rootDirectory = isWindows()
                ? !string.IsNullOrWhiteSpace(configuredRoot)
                    ? configuredRoot!
                    : ResolveWindowsRoot(getEnvVar)
                : ResolveUnixRoot(configuredRoot);

            return Path.Combine(rootDirectory, hashIdentity());
        }

        private static string ResolveWindowsRoot(Func<string, string?> getEnvVar)
        {
            var localAppData = getEnvVar("LOCALAPPDATA");
            if (!string.IsNullOrWhiteSpace(localAppData))
            {
                return localAppData!;
            }

            var temp = getEnvVar("TEMP");
            if (!string.IsNullOrWhiteSpace(temp))
            {
                return temp!;
            }

            return Environment.CurrentDirectory;
        }

        private static string ResolveUnixRoot(string? configuredRoot)
        {
            if (configuredRoot == null || string.IsNullOrWhiteSpace(configuredRoot))
            {
                throw new InvalidOperationException(
                    "Agent365 offline storage on Unix requires StorageDirectory to specify an existing "
                    + "application-owned private directory. Provision its permissions before enabling storage.");
            }

            if (!Path.IsPathRooted(configuredRoot))
            {
                throw new ArgumentException("Unix StorageDirectory must be an absolute path.", nameof(configuredRoot));
            }

            if (!Directory.Exists(configuredRoot))
            {
                throw new DirectoryNotFoundException(
                    $"Agent365 StorageDirectory '{configuredRoot}' does not exist. Provision an application-owned "
                    + "private directory and its permissions before enabling storage.");
            }

            return configuredRoot;
        }

        private static string ComputeHashIdentity()
        {
            using var process = Process.GetCurrentProcess();
            using var sha256 = SHA256.Create();

            var identity = string.Concat(
                Environment.UserName,
                process.ProcessName,
                AppDomain.CurrentDomain.BaseDirectory);
            var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(identity));
            var builder = new StringBuilder(capacity: 8);

            for (var i = 0; i < 4; i++)
            {
                builder.Append(hash[i].ToString("x2"));
            }

            return builder.ToString();
        }
    }
}
