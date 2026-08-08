// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.Agents.A365.Observability.Tests.Tracing;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Agents.A365.Observability.Runtime.Tracing.Exporters;
using Microsoft.Agents.A365.Observability.Runtime.Tracing.Processors;

/// <summary>
/// Deterministic lifecycle tests for <see cref="BatchActivityExportProcessorAsync"/>: the processor must
/// stop accepting work, drain every queued activity, await the in-flight export, shut the exporter down
/// exactly once, and stay safely owned even when a caller cancels its wait.
/// </summary>
[TestClass]
public sealed class BatchActivityExportProcessorAsyncTests
{
    private const string TestSourceName = "BatchActivityExportProcessorAsyncTests";

    [TestMethod]
    [Timeout(30_000)]
    public async Task ShutdownDrainsEveryQueuedActivity()
    {
        var exporter = new RecordingAsyncExporter(blockFirstExport: true);
        var processor = new BatchActivityExportProcessorAsync(
            exporter,
            maxQueueSize: 10,
            scheduledDelayMilliseconds: 60_000,
            maxExportBatchSize: 2);

        processor.OnEnd(CreateRecordedActivity("one"));
        processor.OnEnd(CreateRecordedActivity("two"));
        processor.OnEnd(CreateRecordedActivity("three"));
        exporter.ReleaseFirstExport();

        await processor.ShutdownAsync(CancellationToken.None);

        exporter.ExportedNames.Should().BeEquivalentTo("one", "two", "three");
    }

    [TestMethod]
    [Timeout(30_000)]
    public async Task ShutdownWaitsForActiveExportBeforeExporterShutdown()
    {
        var exporter = new OrderedAsyncExporter();
        var processor = CreateProcessor(exporter);
        processor.OnEnd(CreateRecordedActivity("one"));

        await processor.ShutdownAsync(CancellationToken.None);

        exporter.Events.Should().Equal("export-start", "export-end", "shutdown");
    }

    [TestMethod]
    [Timeout(30_000)]
    public async Task RejectsActivitiesAfterShutdownStarts()
    {
        var processor = CreateProcessor(new RecordingAsyncExporter());
        var shutdown = processor.ShutdownAsync(CancellationToken.None);

        Action action = () => processor.OnEnd(CreateRecordedActivity("late"));
        action.Should().Throw<ObjectDisposedException>();
        await shutdown;
    }

    /// <summary>
    /// Cancelling the caller token must stop the caller's wait promptly (throwing
    /// <see cref="OperationCanceledException"/>) without cancelling the in-flight export. The worker
    /// remains owned: once the export is released it finishes draining, shuts the exporter down, and a
    /// later <see cref="BatchActivityExportProcessorAsync.Dispose()"/> disposes the exporter exactly once.
    /// </summary>
    [TestMethod]
    [Timeout(30_000)]
    public async Task ShutdownCancellationStopsCallerWaitButWorkerCompletesAndDisposesSafely()
    {
        var exporter = new RecordingAsyncExporter(blockFirstExport: true);
        var processor = new BatchActivityExportProcessorAsync(
            exporter,
            maxQueueSize: 10,
            scheduledDelayMilliseconds: 60_000,
            maxExportBatchSize: 10);

        processor.OnEnd(CreateRecordedActivity("one"));
        exporter.WaitForExportStarted();

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Func<Task> shutdown = () => processor.ShutdownAsync(cts.Token);
        await shutdown.Should().ThrowAsync<OperationCanceledException>();

        // The in-flight export was NOT cancelled just because shutdown started: it is still blocked,
        // so nothing has been exported and the exporter has not been shut down yet.
        exporter.ExportedNames.Should().BeEmpty();
        exporter.ShutdownCallCount.Should().Be(0);

        // The worker is still safely owned; releasing the export lets it finish on its own.
        exporter.ReleaseFirstExport();
        await WaitUntilAsync(() => exporter.ShutdownCallCount == 1, TimeSpan.FromSeconds(10));

        exporter.ExportedNames.Should().BeEquivalentTo("one");

        processor.Dispose();
        processor.Dispose();
        await WaitUntilAsync(() => exporter.DisposeCallCount == 1, TimeSpan.FromSeconds(10));
        exporter.DisposeCallCount.Should().Be(1);
    }

    [TestMethod]
    [Timeout(30_000)]
    public async Task ForceFlushWaitsForQueueEmptyAndActiveExport()
    {
        var exporter = new RecordingAsyncExporter(blockFirstExport: true);
        var processor = new BatchActivityExportProcessorAsync(
            exporter,
            maxQueueSize: 10,
            scheduledDelayMilliseconds: 60_000,
            maxExportBatchSize: 10);

        processor.OnEnd(CreateRecordedActivity("one"));

        // The worker has dequeued the batch and is blocked inside the export: the queue is empty but an
        // export is active.
        exporter.WaitForExportStarted();

        var flush = processor.ForceFlushAsync();

        await Task.Delay(200);
        flush.IsCompleted.Should().BeFalse(
            because: "ForceFlushAsync must wait for the in-flight export, not merely for an empty queue");

        exporter.ReleaseFirstExport();

        await flush;
        exporter.ForceFlushCallCount.Should().Be(1);
        exporter.ExportedNames.Should().BeEquivalentTo("one");

        await processor.ShutdownAsync(CancellationToken.None);
        processor.Dispose();
    }

    [TestMethod]
    [Timeout(30_000)]
    public async Task ConcurrentShutdownShutsExporterDownExactlyOnce()
    {
        var exporter = new RecordingAsyncExporter();
        var processor = CreateProcessor(exporter);
        processor.OnEnd(CreateRecordedActivity("one"));

        var first = processor.ShutdownAsync(CancellationToken.None);
        var second = processor.ShutdownAsync(CancellationToken.None);
        await Task.WhenAll(first, second);

        exporter.ShutdownCallCount.Should().Be(1);
        exporter.ExportedNames.Should().BeEquivalentTo("one");
        processor.Dispose();
    }

    [TestMethod]
    [Timeout(30_000)]
    public async Task DisposeDrainsQueueAndDisposesExporterExactlyOnce()
    {
        var exporter = new RecordingAsyncExporter();
        var processor = new BatchActivityExportProcessorAsync(
            exporter,
            maxQueueSize: 10,
            scheduledDelayMilliseconds: 60_000,
            maxExportBatchSize: 10);

        processor.OnEnd(CreateRecordedActivity("one"));
        processor.OnEnd(CreateRecordedActivity("two"));

        processor.Dispose();

        await WaitUntilAsync(() => exporter.DisposeCallCount == 1, TimeSpan.FromSeconds(10));
        exporter.ShutdownCallCount.Should().Be(1);
        exporter.ExportedNames.Should().BeEquivalentTo("one", "two");

        processor.Dispose();
        exporter.DisposeCallCount.Should().Be(1);
    }

    [TestMethod]
    [Timeout(30_000)]
    public void OnEndNullThrowsArgumentNullException()
    {
        var processor = CreateProcessor(new RecordingAsyncExporter());
        Action action = () => processor.OnEnd(null!);
        action.Should().Throw<ArgumentNullException>();
        processor.Dispose();
    }

    [TestMethod]
    [Timeout(30_000)]
    public async Task NonRecordedActivitiesAreNotExported()
    {
        var exporter = new RecordingAsyncExporter();
        var processor = CreateProcessor(exporter);

        processor.OnEnd(CreateNonRecordedActivity("dropped"));
        processor.OnEnd(CreateRecordedActivity("kept"));

        await processor.ShutdownAsync(CancellationToken.None);

        exporter.ExportedNames.Should().BeEquivalentTo("kept");
        processor.Dispose();
    }

    private static BatchActivityExportProcessorAsync CreateProcessor(BaseExporterAsync<Activity> exporter) =>
        new(exporter, maxQueueSize: 2048, scheduledDelayMilliseconds: 100, maxExportBatchSize: 512);

    private static Activity CreateRecordedActivity(string name) => CreateActivity(name, recorded: true);

    private static Activity CreateNonRecordedActivity(string name) => CreateActivity(name, recorded: false);

    private static Activity CreateActivity(string name, bool recorded)
    {
        var samplingResult = recorded
            ? ActivitySamplingResult.AllDataAndRecorded
            : ActivitySamplingResult.PropagationData;

        using var source = new ActivitySource(TestSourceName);
        using var listener = new ActivityListener
        {
            ShouldListenTo = candidate => candidate.Name == TestSourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => samplingResult,
            ActivityStarted = _ => { },
            ActivityStopped = _ => { },
        };
        ActivitySource.AddActivityListener(listener);

        var activity = source.StartActivity(name, ActivityKind.Internal)
            ?? throw new InvalidOperationException("Failed to start activity.");
        activity.Stop();
        return activity;
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var stopwatch = Stopwatch.StartNew();
        while (!condition())
        {
            if (stopwatch.Elapsed > timeout)
            {
                throw new TimeoutException("Condition was not met within the timeout.");
            }

            await Task.Delay(10).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Test exporter that records the display names of exported activities and can block its first
    /// export until explicitly released, so tests can pin an export in flight deterministically.
    /// </summary>
    private sealed class RecordingAsyncExporter : BaseExporterAsync<Activity>
    {
        private readonly bool _blockFirstExport;
        private readonly ConcurrentQueue<string> _names = new();
        private readonly ManualResetEventSlim _exportStarted = new(false);
        private readonly TaskCompletionSource<bool> _firstExportGate =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private int _exportCount;
        private int _shutdownCallCount;
        private int _forceFlushCallCount;
        private int _disposeCallCount;

        public RecordingAsyncExporter(bool blockFirstExport = false)
        {
            _blockFirstExport = blockFirstExport;
        }

        public IReadOnlyCollection<string> ExportedNames => _names.ToArray();

        public int ShutdownCallCount => Volatile.Read(ref _shutdownCallCount);

        public int ForceFlushCallCount => Volatile.Read(ref _forceFlushCallCount);

        public int DisposeCallCount => Volatile.Read(ref _disposeCallCount);

        public void ReleaseFirstExport() => _firstExportGate.TrySetResult(true);

        public void WaitForExportStarted(TimeSpan? timeout = null)
        {
            if (!_exportStarted.Wait(timeout ?? TimeSpan.FromSeconds(10)))
            {
                throw new TimeoutException("The export did not start within the timeout.");
            }
        }

        public override async Task ExportAsync(IReadOnlyCollection<Activity> batch, CancellationToken cancellationToken)
        {
            var invocation = Interlocked.Increment(ref _exportCount);
            _exportStarted.Set();

            if (_blockFirstExport && invocation == 1)
            {
                await _firstExportGate.Task.ConfigureAwait(false);
            }

            foreach (var activity in batch)
            {
                _names.Enqueue(activity.DisplayName);
            }
        }

        public override Task<bool> ForceFlushAsync(CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _forceFlushCallCount);
            return Task.FromResult(true);
        }

        public override Task<bool> ShutdownAsync(CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _shutdownCallCount);
            return Task.FromResult(true);
        }

        public override void Dispose()
        {
            Interlocked.Increment(ref _disposeCallCount);
            base.Dispose();
        }
    }

    /// <summary>
    /// Test exporter that records the ordering of export and shutdown events so a test can prove the
    /// active export completes before the exporter is shut down.
    /// </summary>
    private sealed class OrderedAsyncExporter : BaseExporterAsync<Activity>
    {
        private readonly ConcurrentQueue<string> _events = new();

        public IReadOnlyList<string> Events => _events.ToArray();

        public override async Task ExportAsync(IReadOnlyCollection<Activity> batch, CancellationToken cancellationToken)
        {
            _events.Enqueue("export-start");
            await Task.Yield();
            _events.Enqueue("export-end");
        }

        public override Task<bool> ShutdownAsync(CancellationToken cancellationToken = default)
        {
            _events.Enqueue("shutdown");
            return Task.FromResult(true);
        }
    }
}
