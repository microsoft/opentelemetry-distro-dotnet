// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using FluentAssertions;
using Microsoft.Agents.A365.Observability.Runtime.Common;
using Microsoft.Agents.A365.Observability.Runtime.Tracing.Exporters;
using Microsoft.Extensions.Logging.Abstractions;
using System.Diagnostics.CodeAnalysis;
using System.Net.Http;
using System.Reflection;

namespace Microsoft.Agents.A365.Observability.Tests.Tracing.Exporters;

/// <summary>
/// Exercises the exporter durable-delivery lifecycle wiring added in Task 6: the sync exporter's
/// dispose and the async exporter's shutdown must stop/dispose the replay coordinator; a caller-owned
/// <see cref="HttpClient"/> must never be disposed while an internally created one must be; the shared
/// store is disposed with the exporter; disabled offline storage starts no replay loop; and dispose is
/// idempotent (no double-dispose). A <see cref="FakeReplayCoordinator"/> substitutes for the real
/// background loop so lifecycle calls are observed without touching disk or the network.
/// </summary>
[TestClass]
public sealed class Agent365ExporterLifecycleTests
{
    // ------------------------------------------------------------------ helpers

    private static Agent365ExporterCore CreateCore(IAgent365PersistentStorage? storage = null) =>
        new(
            new ExportFormatter(NullLogger<ExportFormatter>.Instance),
            NullLogger<Agent365ExporterCore>.Instance,
            null,
            storage ?? new FakeStorage(),
            null);

    private static Agent365ExporterOptions CreateOptions() =>
        new()
        {
            TokenResolver = (_, _) => Task.FromResult<string?>("token"),
        };

    private static Agent365Exporter CreateSyncExporter(
        IAgent365ReplayCoordinator coordinator,
        HttpClient? httpClient = null) =>
        new(
            CreateCore(),
            NullLogger<Agent365Exporter>.Instance,
            CreateOptions(),
            resource: null,
            httpClient: httpClient,
            replayCoordinator: coordinator,
            wireDurableDelivery: false);

    private static Agent365ExporterAsync CreateAsyncExporter(
        IAgent365ReplayCoordinator coordinator,
        HttpClient? httpClient = null) =>
        new(
            CreateCore(),
            NullLogger<Agent365Exporter>.Instance,
            CreateOptions(),
            resource: null,
            httpClient: httpClient,
            replayCoordinator: coordinator,
            wireDurableDelivery: false);

    // ------------------------------------------------------------------ option defaults

    [TestMethod]
    public void OfflineStorageDefaultsToEnabled()
    {
        var options = new Agent365ExporterOptions();

        options.DisableOfflineStorage.Should().BeFalse();
        options.StorageDirectory.Should().BeNull();
    }

    [TestMethod]
    public void Agent365OptionsForwardOfflineStorageSettings()
    {
        var options = new Microsoft.OpenTelemetry.Agent365Options
        {
            DisableOfflineStorage = true,
            StorageDirectory = @"C:\custom\telemetry",
        };

        options.DisableOfflineStorage.Should().BeTrue();
        options.StorageDirectory.Should().Be(@"C:\custom\telemetry");
    }

    // ------------------------------------------------------------------ coordinator lifecycle

    [TestMethod]
    public void ExporterConstructionStartsReplayCoordinator()
    {
        var coordinator = new FakeReplayCoordinator();

        using var exporter = CreateSyncExporter(coordinator);

        coordinator.StartCalls.Should().Be(1);
    }

    [TestMethod]
    public async Task AsyncExporterShutdownStopsReplayCoordinator()
    {
        var coordinator = new FakeReplayCoordinator();
        var exporter = CreateAsyncExporter(coordinator);

        (await exporter.ShutdownAsync(CancellationToken.None)).Should().BeTrue();

        coordinator.StopCalls.Should().Be(1);
    }

    [TestMethod]
    public void SyncExporterDisposeStopsReplayCoordinator()
    {
        var coordinator = new FakeReplayCoordinator();
        var exporter = CreateSyncExporter(coordinator);

        exporter.Dispose();

        coordinator.DisposeCalls.Should().Be(1);
    }

    [TestMethod]
    public void AsyncExporterDisposeDisposesReplayCoordinator()
    {
        var coordinator = new FakeReplayCoordinator();
        var exporter = CreateAsyncExporter(coordinator);

        exporter.Dispose();

        coordinator.DisposeCalls.Should().Be(1);
    }

    [TestMethod]
    public void SyncExporterDisposeIsIdempotent()
    {
        var coordinator = new FakeReplayCoordinator();
        var exporter = CreateSyncExporter(coordinator);

        exporter.Dispose();
        exporter.Dispose();

        coordinator.DisposeCalls.Should().Be(1, "the exporter must not double-dispose the coordinator");
    }

    [TestMethod]
    public async Task AsyncExporterShutdownThenDisposeStopsAndDisposesOnce()
    {
        var coordinator = new FakeReplayCoordinator();
        var exporter = CreateAsyncExporter(coordinator);

        await exporter.ShutdownAsync(CancellationToken.None);
        exporter.Dispose();

        coordinator.StopCalls.Should().Be(1);
        coordinator.DisposeCalls.Should().Be(1);
    }

    // ------------------------------------------------------------------ no-coordinator safety (public ctor)

    [TestMethod]
    public void PublicSyncExporterWithoutDurableWiringDisposesSafely()
    {
        var exporter = new Agent365Exporter(
            CreateCore(),
            NullLogger<Agent365Exporter>.Instance,
            CreateOptions());

        Action dispose = exporter.Dispose;

        dispose.Should().NotThrow();
    }

    [TestMethod]
    public async Task PublicAsyncExporterWithoutDurableWiringShutsDownSafely()
    {
        var exporter = new Agent365ExporterAsync(
            CreateCore(),
            NullLogger<Agent365Exporter>.Instance,
            CreateOptions());

        (await exporter.ShutdownAsync(CancellationToken.None)).Should().BeTrue();
        exporter.Dispose();
    }

    // ------------------------------------------------------------------ HttpClient ownership

    [TestMethod]
    public void CallerOwnedHttpClientIsNotDisposed()
    {
        var handler = new TrackingHandler();
        var callerClient = new HttpClient(handler);
        var coordinator = new FakeReplayCoordinator();
        var exporter = CreateSyncExporter(coordinator, httpClient: callerClient);

        exporter.Dispose();

        handler.Disposed.Should().BeFalse("a caller-supplied HttpClient must never be disposed by the exporter");

        callerClient.Dispose();
    }

    [TestMethod]
    public void InternallyCreatedHttpClientIsDisposed()
    {
        var coordinator = new FakeReplayCoordinator();
        var exporter = CreateSyncExporter(coordinator, httpClient: null);

        var field = typeof(Agent365Exporter).GetField("_httpClient", BindingFlags.NonPublic | BindingFlags.Instance);
        var ownedClient = (HttpClient)field!.GetValue(exporter)!;

        exporter.Dispose();

        Action useAfterDispose = () => ownedClient.Timeout = TimeSpan.FromSeconds(5);
        useAfterDispose.Should().Throw<ObjectDisposedException>("an internally created HttpClient must be disposed with the exporter");
    }

    // ------------------------------------------------------------------ shared store ownership + disabled storage

    [TestMethod]
    public void DisposeDisposesSharedStoreWhenDurableDeliveryWired()
    {
        var storage = new TrackingStorage();
        var exporter = new Agent365Exporter(
            CreateCore(storage),
            NullLogger<Agent365Exporter>.Instance,
            CreateOptions(),
            resource: null,
            httpClient: null,
            replayCoordinator: null,
            wireDurableDelivery: true);

        exporter.Dispose();

        storage.DisposeCalls.Should().Be(1);
    }

    [TestMethod]
    public void DisabledOfflineStorageStartsNoReplayLoopAndDisposesCleanly()
    {
        var storage = new DisabledAgent365Storage();
        var exporter = new Agent365Exporter(
            CreateCore(storage),
            NullLogger<Agent365Exporter>.Instance,
            CreateOptions(),
            resource: null,
            httpClient: null,
            replayCoordinator: null,
            wireDurableDelivery: true);

        // No coordinator is wired when the store is disabled, so the private field stays null.
        var field = typeof(Agent365Exporter).GetField("_replayCoordinator", BindingFlags.NonPublic | BindingFlags.Instance);
        field!.GetValue(exporter).Should().BeNull("no replay loop should run when offline storage is disabled");

        Action dispose = exporter.Dispose;
        dispose.Should().NotThrow();
    }

    // ------------------------------------------------------------------ durable-delivery factory

    [TestMethod]
    public void FactoryReturnsDisabledStorageWhenOfflineStorageDisabled()
    {
        var storage = Agent365DurableDelivery.CreateStorage(
            new Agent365ExporterOptions { DisableOfflineStorage = true },
            NullLogger<Agent365Exporter>.Instance);

        storage.Should().BeOfType<DisabledAgent365Storage>();
        storage.TryStore(new Agent365DurableRecord("t", "a", null, false, "{}", DateTimeOffset.UtcNow))
            .Should().BeTrue("the disabled store accepts and silently drops records");
        storage.TryGetNext(out var record).Should().BeFalse();
        record.Should().BeNull();
    }

    [TestMethod]
    public void FactoryReturnsNoCoordinatorForDisabledStorage()
    {
        var core = CreateCore(new DisabledAgent365Storage());

        var coordinator = Agent365DurableDelivery.CreateCoordinator(
            core,
            CreateOptions(),
            new HttpClient(),
            NullLogger<Agent365Exporter>.Instance);

        coordinator.Should().BeNull();
    }

    // ------------------------------------------------------------------ fakes

    private sealed class FakeReplayCoordinator : IAgent365ReplayCoordinator
    {
        public int StartCalls { get; private set; }

        public int StopCalls { get; private set; }

        public int DisposeCalls { get; private set; }

        public void Start() => StartCalls++;

        public Task StopAsync(CancellationToken cancellationToken)
        {
            StopCalls++;
            return Task.CompletedTask;
        }

        public void Dispose() => DisposeCalls++;
    }

    private sealed class TrackingStorage : IAgent365PersistentStorage
    {
        public int DisposeCalls { get; private set; }

        public bool TryStore(Agent365DurableRecord record) => true;

        public bool TryGetNext(
#if NETSTANDARD2_0
            out IAgent365StoredRecord? record)
#else
            [NotNullWhen(true)] out IAgent365StoredRecord? record)
#endif
        {
            record = null;
            return false;
        }

        public void Dispose() => DisposeCalls++;
    }

    private sealed class TrackingHandler : HttpMessageHandler
    {
        public bool Disposed { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK));

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Disposed = true;
            }

            base.Dispose(disposing);
        }
    }
}
