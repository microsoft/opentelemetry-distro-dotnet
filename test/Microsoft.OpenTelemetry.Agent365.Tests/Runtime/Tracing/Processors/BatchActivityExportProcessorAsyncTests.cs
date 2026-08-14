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
    public async Task DropsActivitiesAfterShutdownStartsWithoutThrowing()
    {
        // Once shutdown has begun the processor must stop accepting activities, but OnEnd must never
        // throw (BaseProcessor<T>.OnEnd is contractually not allowed to throw) — the late activity is
        // dropped silently and is never handed to the exporter.
        var exporter = new RecordingAsyncExporter(blockFirstExport: true);
        var processor = new BatchActivityExportProcessorAsync(
            exporter,
            maxQueueSize: 10,
            scheduledDelayMilliseconds: 60_000,
            maxExportBatchSize: 10);

        processor.OnEnd(CreateRecordedActivity("first"));
        exporter.WaitForExportStarted();

        // BeginShutdown runs synchronously before ShutdownAsync's first await, so the processor is
        // already Draining once the call returns its Task.
        var shutdown = processor.ShutdownAsync(CancellationToken.None);

        Action action = () => processor.OnEnd(CreateRecordedActivity("late"));
        action.Should().NotThrow();

        exporter.ReleaseFirstExport();
        await shutdown;

        exporter.ExportedNames.Should().BeEquivalentTo("first");
        exporter.ExportedNames.Should().NotContain("late");
        processor.Dispose();
    }

    [TestMethod]
    [Timeout(30_000)]
    public void DropsActivitiesAfterShutdownCompletesWithoutThrowing()
    {
        var exporter = new RecordingAsyncExporter();
        var processor = CreateProcessor(exporter);
        processor.Shutdown(10_000);

        Action action = () => processor.OnEnd(CreateRecordedActivity("late"));
        action.Should().NotThrow();
        exporter.ExportedNames.Should().NotContain("late");
        processor.Dispose();
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

    // ---- Constructor argument validation (mirrors OpenTelemetry's BatchExportProcessor guards) ----

    [DataTestMethod]
    [DataRow(0)]
    [DataRow(-1)]
    [Timeout(30_000)]
    public void ConstructorRejectsMaxQueueSizeBelowOne(int maxQueueSize)
    {
        Action action = () => new BatchActivityExportProcessorAsync(
            new RecordingAsyncExporter(), maxQueueSize: maxQueueSize);
        action.Should().Throw<ArgumentOutOfRangeException>()
            .Which.ParamName.Should().Be("maxQueueSize");
    }

    [DataTestMethod]
    [DataRow(0)]
    [DataRow(-1)]
    [Timeout(30_000)]
    public void ConstructorRejectsMaxExportBatchSizeBelowOne(int maxExportBatchSize)
    {
        Action action = () => new BatchActivityExportProcessorAsync(
            new RecordingAsyncExporter(), maxQueueSize: 10, maxExportBatchSize: maxExportBatchSize);
        action.Should().Throw<ArgumentOutOfRangeException>()
            .Which.ParamName.Should().Be("maxExportBatchSize");
    }

    [TestMethod]
    [Timeout(30_000)]
    public void ConstructorRejectsMaxExportBatchSizeAboveMaxQueueSize()
    {
        Action action = () => new BatchActivityExportProcessorAsync(
            new RecordingAsyncExporter(), maxQueueSize: 8, maxExportBatchSize: 9);
        action.Should().Throw<ArgumentOutOfRangeException>()
            .Which.ParamName.Should().Be("maxExportBatchSize");
    }

    [DataTestMethod]
    [DataRow(0)]
    [DataRow(-1)]
    [Timeout(30_000)]
    public void ConstructorRejectsScheduledDelayBelowOne(int scheduledDelayMilliseconds)
    {
        Action action = () => new BatchActivityExportProcessorAsync(
            new RecordingAsyncExporter(), scheduledDelayMilliseconds: scheduledDelayMilliseconds);
        action.Should().Throw<ArgumentOutOfRangeException>()
            .Which.ParamName.Should().Be("scheduledDelayMilliseconds");
    }

    [TestMethod]
    [Timeout(30_000)]
    public void ConstructorAcceptsBoundaryValues()
    {
        // maxExportBatchSize == maxQueueSize and scheduledDelay == 1 are the tightest legal values.
        Action action = () =>
        {
            using var processor = new BatchActivityExportProcessorAsync(
                new RecordingAsyncExporter(),
                maxQueueSize: 1,
                scheduledDelayMilliseconds: 1,
                maxExportBatchSize: 1);
        };
        action.Should().NotThrow();
    }

    // ---- Production hooks: TracerProvider shutdown/force-flush go through Shutdown/ForceFlush ----

    [TestMethod]
    [Timeout(30_000)]
    public void ShutdownViaBaseProcessorDrainsQueueAndShutsExporterDownOnce()
    {
        var exporter = new RecordingAsyncExporter();
        var processor = new BatchActivityExportProcessorAsync(
            exporter,
            maxQueueSize: 100,
            scheduledDelayMilliseconds: 60_000,
            maxExportBatchSize: 10);

        processor.OnEnd(CreateRecordedActivity("one"));
        processor.OnEnd(CreateRecordedActivity("two"));

        // BaseProcessor.Shutdown -> OnShutdown -> async drain. Standard TracerProvider shutdown path.
        var result = processor.Shutdown(20_000);

        result.Should().BeTrue();
        exporter.ExportedNames.Should().BeEquivalentTo("one", "two");
        exporter.ShutdownCallCount.Should().Be(1);
        processor.Dispose();
    }

    [TestMethod]
    [Timeout(30_000)]
    public void ForceFlushViaBaseProcessorFlushesExporterAndDrainsQueue()
    {
        var exporter = new RecordingAsyncExporter();
        var processor = new BatchActivityExportProcessorAsync(
            exporter,
            maxQueueSize: 100,
            scheduledDelayMilliseconds: 60_000,
            maxExportBatchSize: 10);

        processor.OnEnd(CreateRecordedActivity("one"));
        processor.OnEnd(CreateRecordedActivity("two"));

        // BaseProcessor.ForceFlush -> OnForceFlush -> async flush. Standard TracerProvider flush path.
        var result = processor.ForceFlush(20_000);

        result.Should().BeTrue();
        exporter.ExportedNames.Should().BeEquivalentTo("one", "two");
        exporter.ForceFlushCallCount.Should().BeGreaterThanOrEqualTo(1);

        processor.Shutdown(20_000);
        processor.Dispose();
    }

    [TestMethod]
    [Timeout(30_000)]
    public void ShutdownViaBaseProcessorTimesOutButWorkerKeepsDrainingInBackground()
    {
        var exporter = new RecordingAsyncExporter(blockFirstExport: true);
        var processor = new BatchActivityExportProcessorAsync(
            exporter,
            maxQueueSize: 100,
            scheduledDelayMilliseconds: 60_000,
            maxExportBatchSize: 10);

        processor.OnEnd(CreateRecordedActivity("one"));
        exporter.WaitForExportStarted();

        // The in-flight export is blocked, so a bounded shutdown cannot complete and must return false
        // promptly (bounded, no deadlock) rather than hang.
        var stopwatch = Stopwatch.StartNew();
        var result = processor.Shutdown(200);
        stopwatch.Stop();

        result.Should().BeFalse();
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(10));

        // The worker stays owned: releasing the export lets it finish draining and shut the exporter down.
        exporter.ReleaseFirstExport();
        WaitUntilAsync(() => exporter.ShutdownCallCount == 1, TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
        exporter.ExportedNames.Should().BeEquivalentTo("one");
        processor.Dispose();
    }

    [TestMethod]
    [Timeout(30_000)]
    public void ForceFlushViaBaseProcessorIsBoundedWhenExportBlocks()
    {
        var exporter = new RecordingAsyncExporter(blockFirstExport: true);
        var processor = new BatchActivityExportProcessorAsync(
            exporter,
            maxQueueSize: 100,
            scheduledDelayMilliseconds: 60_000,
            maxExportBatchSize: 10);

        processor.OnEnd(CreateRecordedActivity("one"));
        exporter.WaitForExportStarted();

        var stopwatch = Stopwatch.StartNew();
        var result = processor.ForceFlush(200);
        stopwatch.Stop();

        result.Should().BeFalse();
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(10));

        exporter.ReleaseFirstExport();
        processor.Shutdown(20_000);
        processor.Dispose();
    }

    // ---- Concurrency / race coverage (Findings 3 & 4) ----

    [TestMethod]
    [Timeout(60_000)]
    public async Task ConcurrentProducersNeverExceedConfiguredQueueCapacity()
    {
        const int producerCount = 64;
        var activities = CreateRecordedActivities(producerCount, "contender");

        for (var trial = 0; trial < 25; trial++)
        {
            var exporter = new RecordingAsyncExporter(blockFirstExport: true);
            var processor = new BatchActivityExportProcessorAsync(
                exporter,
                maxQueueSize: 1,
                scheduledDelayMilliseconds: 60_000,
                maxExportBatchSize: 1);

            processor.OnEnd(CreateRecordedActivity($"in-flight-{trial}"));
            exporter.WaitForExportStarted();

            using var go = new Barrier(producerCount + 1);
            var producers = new Thread[producerCount];
            for (var i = 0; i < producerCount; i++)
            {
                var activity = activities[i];
                producers[i] = new Thread(() =>
                {
                    go.SignalAndWait();
                    processor.OnEnd(activity);
                });
                producers[i].Start();
            }

            try
            {
                go.SignalAndWait();
                foreach (var producer in producers)
                {
                    producer.Join();
                }
            }
            finally
            {
                exporter.ReleaseFirstExport();
                await processor.ShutdownAsync(CancellationToken.None);
                processor.Dispose();
            }

            exporter.ExportedNames.Count(name => name.StartsWith("contender", StringComparison.Ordinal)).Should().BeLessThanOrEqualTo(
                1,
                "only one contender can be admitted while the configured one-slot queue is blocked");
        }
    }

    [TestMethod]
    [Timeout(60_000)]
    public async Task ConcurrentOnEndRacingShutdownNeverStrandsItemsNorThrows()
    {
        // Producers keep calling OnEnd while shutdown flips the lifecycle underneath them. The
        // producer/worker handshake must guarantee that no accepted activity is left stranded in the
        // queue after the worker exits, and OnEnd must never throw.
        const int producerCount = 6;
        const int perProducer = 250;

        // Pre-create activities single-threaded: ActivitySource/ActivityListener registration is not
        // safe to run concurrently, so producer threads must only call OnEnd on ready-made activities.
        var activities = new Activity[producerCount][];
        for (var p = 0; p < producerCount; p++)
        {
            activities[p] = CreateRecordedActivities(perProducer, $"p{p}");
        }

        for (var trial = 0; trial < 15; trial++)
        {
            var exporter = new RecordingAsyncExporter();
            var processor = new BatchActivityExportProcessorAsync(
                exporter,
                maxQueueSize: 1_000_000,
                scheduledDelayMilliseconds: 50,
                maxExportBatchSize: 64);

            var exceptions = new ConcurrentQueue<Exception>();
            using var go = new ManualResetEventSlim(false);

            var producers = new Task[producerCount];
            for (var p = 0; p < producerCount; p++)
            {
                var producerActivities = activities[p];
                producers[p] = Task.Run(() =>
                {
                    go.Wait();
                    foreach (var activity in producerActivities)
                    {
                        try
                        {
                            processor.OnEnd(activity);
                        }
                        catch (Exception ex)
                        {
                            exceptions.Enqueue(ex);
                        }
                    }
                });
            }

            go.Set();
            await Task.Delay(1);
            var shutdown = processor.ShutdownAsync(CancellationToken.None);
            await Task.WhenAll(producers);
            await shutdown;

            exceptions.Should().BeEmpty(because: "OnEnd must never throw, even while racing shutdown");
            processor.QueueCount.Should().Be(0, because: "no accepted activity may be stranded after the worker exits");
            exporter.ShutdownCallCount.Should().Be(1);
            exporter.ExportedNames.Should().OnlyHaveUniqueItems(because: "no activity may be exported twice");

            processor.Dispose();
        }
    }

    [TestMethod]
    [Timeout(60_000)]
    public async Task AllItemsQueuedWhileRunningAreDrainedOnShutdown()
    {
        // Every activity enqueued while Running (before shutdown) must be exported exactly once — a
        // strong end-to-end no-stranding / no-loss assertion with a queue large enough that nothing is
        // dropped for capacity.
        const int producerCount = 8;
        const int perProducer = 500;

        var activities = new Activity[producerCount][];
        for (var p = 0; p < producerCount; p++)
        {
            activities[p] = CreateRecordedActivities(perProducer, $"p{p}");
        }

        var exporter = new RecordingAsyncExporter();
        var processor = new BatchActivityExportProcessorAsync(
            exporter,
            maxQueueSize: 1_000_000,
            scheduledDelayMilliseconds: 60_000,
            maxExportBatchSize: 128);

        using var go = new ManualResetEventSlim(false);
        var producers = new Task[producerCount];
        for (var p = 0; p < producerCount; p++)
        {
            var producerActivities = activities[p];
            producers[p] = Task.Run(() =>
            {
                go.Wait();
                foreach (var activity in producerActivities)
                {
                    processor.OnEnd(activity);
                }
            });
        }

        go.Set();
        await Task.WhenAll(producers);
        await processor.ShutdownAsync(CancellationToken.None);

        exporter.ExportedNames.Count.Should().Be(producerCount * perProducer);
        processor.QueueCount.Should().Be(0);
        exporter.ShutdownCallCount.Should().Be(1);
        processor.Dispose();
    }

    [TestMethod]
    [Timeout(60_000)]
    public async Task ConcurrentDisposeAndShutdownShutsDownAndDisposesExporterExactlyOnce()
    {
        // Dispose racing the worker's natural exit (driven by ShutdownAsync) must shut down and dispose
        // the exporter exactly once — never zero (leak) and never twice — under the full-fence handshake.
        for (var trial = 0; trial < 50; trial++)
        {
            var exporter = new RecordingAsyncExporter();
            var processor = new BatchActivityExportProcessorAsync(
                exporter,
                maxQueueSize: 1_000,
                scheduledDelayMilliseconds: 10,
                maxExportBatchSize: 32);

            processor.OnEnd(CreateRecordedActivity("one"));

            using var barrier = new Barrier(2);
            var disposeTask = Task.Run(() =>
            {
                barrier.SignalAndWait();
                processor.Dispose();
            });
            var shutdownTask = Task.Run(() =>
            {
                barrier.SignalAndWait();
                return processor.ShutdownAsync(CancellationToken.None);
            });

            await Task.WhenAll(disposeTask, shutdownTask);

            await WaitUntilAsync(() => exporter.DisposeCallCount == 1, TimeSpan.FromSeconds(10));
            exporter.DisposeCallCount.Should().Be(1);
            exporter.ShutdownCallCount.Should().Be(1);
        }
    }

    private static BatchActivityExportProcessorAsync CreateProcessor(BaseExporterAsync<Activity> exporter) =>
        new(exporter, maxQueueSize: 2048, scheduledDelayMilliseconds: 100, maxExportBatchSize: 512);

    private static Activity CreateRecordedActivity(string name) => CreateActivity(name, recorded: true);

    private static Activity CreateNonRecordedActivity(string name) => CreateActivity(name, recorded: false);

    private static Activity[] CreateRecordedActivities(int count, string prefix)
    {
        // Created single-threaded on purpose: ActivitySource/ActivityListener registration is not safe
        // to run concurrently, so tests build the activities up front and hand them to producer threads.
        var activities = new Activity[count];
        for (var i = 0; i < count; i++)
        {
            activities[i] = CreateRecordedActivity($"{prefix}-{i}");
        }

        return activities;
    }

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
