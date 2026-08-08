// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
using Microsoft.Agents.A365.Observability.Runtime.Tracing.Exporters;
using global::OpenTelemetry;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Agents.A365.Observability.Runtime.Tracing.Processors
{
    /// <summary>
    /// Implements an async processor that batches <see cref="Activity"/> objects before calling exporter asynchronously.
    /// </summary>
    /// <remarks>
    /// The processor owns a single background worker that drains a queue in batches. Its lifecycle is an
    /// atomic three-phase state machine: <c>Running</c> (accepting activities), <c>Draining</c> (shutdown
    /// requested — no new activities accepted, but every already-queued activity plus any in-flight export
    /// is still completed), and <c>Stopped</c> (worker finished and the exporter has been shut down).
    /// Shutdown never cancels an in-flight export; a caller that cancels <see cref="ShutdownAsync"/> only
    /// stops waiting — the worker remains owned and finishes draining, shutting the exporter down and
    /// (when disposal was requested) disposing it exactly once.
    /// </remarks>
    public class BatchActivityExportProcessorAsync : BaseProcessor<Activity>
    {
        internal const int DefaultMaxQueueSize = 2048;
        internal const int DefaultScheduledDelayMilliseconds = 5000;
        internal const int DefaultMaxExportBatchSize = 512;

        // Lifecycle states. Ordered so a state only ever moves forward: Running -> Draining -> Stopped.
        private const int StateRunning = 0;
        private const int StateDraining = 1;
        private const int StateStopped = 2;

        private const int ForceFlushPollIntervalMilliseconds = 10;

        private readonly BaseExporterAsync<Activity> exporter;
        private readonly int maxQueueSize;
        private readonly int scheduledDelayMilliseconds;
        private readonly int maxExportBatchSize;
        private readonly int workerIdleWaitMilliseconds;

        private readonly ConcurrentQueue<Activity> queue;
        private readonly SemaphoreSlim signal;
        private readonly Task workerTask;
        private readonly string friendlyTypeName;

        private int state;
        private int activeExport;
        private int activeProducers;
        private int exporterShutdownStarted;
        private int exporterDisposeClaim;
        private int disposeRequested;
        private int disposed;

        /// <summary>
        /// Initializes a new instance of the <see cref="BatchActivityExportProcessorAsync"/> class.
        /// </summary>
        /// <param name="exporter">The async exporter instance.</param>
        /// <param name="maxQueueSize">Maximum queue size. Must be greater than or equal to 1.</param>
        /// <param name="scheduledDelayMilliseconds">Delay between exports in ms. Must be greater than or equal to 1.</param>
        /// <param name="maxExportBatchSize">Max batch size per export. Must be in the range [1, <paramref name="maxQueueSize"/>].</param>
        /// <exception cref="ArgumentNullException"><paramref name="exporter"/> is <c>null</c>.</exception>
        /// <exception cref="ArgumentOutOfRangeException">An argument is outside its allowed range.</exception>
        public BatchActivityExportProcessorAsync(
            BaseExporterAsync<Activity> exporter,
            int maxQueueSize = DefaultMaxQueueSize,
            int scheduledDelayMilliseconds = DefaultScheduledDelayMilliseconds,
            int maxExportBatchSize = DefaultMaxExportBatchSize)
        {
            this.exporter = exporter ?? throw new ArgumentNullException(nameof(exporter));

            // Validate arguments to match OpenTelemetry's synchronous BatchExportProcessor<T>: a queue of
            // at least one slot, a batch in [1, maxQueueSize], and a strictly positive scheduled delay.
            // Requiring scheduledDelayMilliseconds >= 1 also guarantees the idle worker always sleeps on
            // a bounded, positive interval, so it can never busy-spin re-checking an empty queue.
            if (maxQueueSize < 1)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maxQueueSize), maxQueueSize, "maxQueueSize must be greater than or equal to 1.");
            }

            if (maxExportBatchSize < 1 || maxExportBatchSize > maxQueueSize)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maxExportBatchSize), maxExportBatchSize, "maxExportBatchSize must be greater than or equal to 1 and less than or equal to maxQueueSize.");
            }

            if (scheduledDelayMilliseconds < 1)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(scheduledDelayMilliseconds), scheduledDelayMilliseconds, "scheduledDelayMilliseconds must be greater than or equal to 1.");
            }

            this.maxQueueSize = maxQueueSize;
            this.scheduledDelayMilliseconds = scheduledDelayMilliseconds;
            this.maxExportBatchSize = maxExportBatchSize;

            // The scheduled delay bounds how long the idle worker sleeps before it re-checks state; it is
            // only a safety net because every enqueue, shutdown, and producer-exit also releases the
            // signal to wake it early. Validation above guarantees it is strictly positive.
            this.workerIdleWaitMilliseconds = scheduledDelayMilliseconds;

            this.queue = new ConcurrentQueue<Activity>();

            // A binary (max-count 1) signal: releases saturate at one pending wake, so a burst of enqueues
            // cannot accumulate a large count that would make the worker spin through WaitAsync returning
            // synchronously once the queue drains. One wake is enough — the worker drains the whole queue.
            this.signal = new SemaphoreSlim(0, 1);
            this.friendlyTypeName = $"{this.GetType().Name}{{{exporter.GetType().Name}}}";

            // Start the worker last so every field it reads is fully initialized before it runs.
            this.workerTask = Task.Run(this.ProcessLoopAsync);
        }

        /// <summary>
        /// Gets the number of activities currently buffered in the queue. Test-only diagnostic used to
        /// assert that no accepted activity is stranded after the worker exits.
        /// </summary>
        internal int QueueCount => this.queue.Count;

        /// <summary>
        /// Called when an <see cref="Activity"/> is ended.
        /// </summary>
        /// <param name="data">The activity to export.</param>
        public override void OnEnd(Activity data)
        {
            if (data == null)
            {
                throw new ArgumentNullException(nameof(data));
            }

            if (!data.Recorded)
            {
                return;
            }

            this.OnExport(data);
        }

        /// <summary>
        /// Enqueues activity data for export using a producer/worker handshake. Once shutdown has begun
        /// the processor no longer accepts activities and the data is dropped silently (never throwing —
        /// <see cref="BaseProcessor{T}.OnEnd"/> is contractually not allowed to throw); if the queue is
        /// full while running the data is also dropped.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The <see cref="activeProducers"/> counter closes the race between a producer that has already
        /// observed <see cref="StateRunning"/> and a worker deciding the queue is drained. A producer
        /// increments the counter <em>before</em> it reads the state; the worker only exits once it has
        /// observed <see cref="StateDraining"/> and then <see cref="activeProducers"/> at zero. Because
        /// the increment carries a full fence, any producer the worker does not count reads the already
        /// published <see cref="StateDraining"/> and drops — so an accepted activity is never stranded in
        /// the queue after the worker has exited.
        /// </para>
        /// </remarks>
        /// <param name="data">The activity to export.</param>
        private void OnExport(Activity data)
        {
            // Publish the producer's presence before reading the state so the worker's drain-exit
            // handshake can never miss an in-flight enqueue (see the remarks above).
            Interlocked.Increment(ref this.activeProducers);
            try
            {
                if (Volatile.Read(ref this.state) != StateRunning)
                {
                    // Shutdown has begun: drop silently rather than throw or strand the activity.
                    return;
                }

                if (this.queue.Count < this.maxQueueSize)
                {
                    this.queue.Enqueue(data);
                    this.TryReleaseSignal();
                }

                // else: queue full, drop (could count dropped).
            }
            finally
            {
                // Wake the worker once the last in-flight producer leaves during shutdown, so its
                // drain-exit handshake re-evaluates promptly instead of waiting out the idle interval.
                if (Interlocked.Decrement(ref this.activeProducers) == 0
                    && Volatile.Read(ref this.state) != StateRunning)
                {
                    this.TryReleaseSignal();
                }
            }
        }

        /// <summary>
        /// Forces the processor to flush all queued activities asynchronously. The returned task completes
        /// only once the queue is empty <em>and</em> no export is in flight (or the wait is cancelled),
        /// after which the exporter is flushed.
        /// </summary>
        /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
        /// <returns>A <see cref="Task"/> representing the asynchronous flush operation.</returns>
        public async Task ForceFlushAsync(CancellationToken cancellationToken = default)
        {
            this.TryReleaseSignal();

            while (!this.IsDrained())
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                // Once the worker has stopped it will never process more work, so waiting for it to drain
                // the queue would hang; break and flush whatever the exporter still holds.
                if (Volatile.Read(ref this.state) == StateStopped)
                {
                    break;
                }

                try
                {
                    await Task.Delay(ForceFlushPollIntervalMilliseconds, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }

            await this.exporter.ForceFlushAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Shuts down the processor and exporter asynchronously. The queued activities and any in-flight
        /// export are drained first, then the exporter is shut down — none of which is cancelled by
        /// <paramref name="cancellationToken"/>. Cancelling the token only stops the caller from waiting;
        /// the worker remains owned and completes the drain and exporter shutdown on its own, and a later
        /// <see cref="Dispose(bool)"/> stays safe.
        /// </summary>
        /// <param name="cancellationToken">A token that stops the caller's wait (not the drain itself).</param>
        /// <returns>A <see cref="Task"/> representing the asynchronous shutdown operation.</returns>
        public async Task ShutdownAsync(CancellationToken cancellationToken = default)
        {
            this.BeginShutdown();

            await WaitAsync(this.workerTask, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Called by the base <c>Shutdown</c> (invoked by a standard <c>TracerProvider</c> shutdown). It
        /// bridges to the asynchronous drain in <see cref="ShutdownAsync"/> so the queued activities and
        /// any in-flight export are drained and the exporter is shut down, bounded by
        /// <paramref name="timeoutMilliseconds"/>. On timeout it returns <c>false</c> without cancelling
        /// the drain — the worker remains owned and keeps draining in the background, and a later
        /// <see cref="Dispose(bool)"/> stays safe.
        /// </summary>
        /// <param name="timeoutMilliseconds">The bound on the caller's wait, or <see cref="Timeout.Infinite"/>.</param>
        /// <returns><c>true</c> when the drain and exporter shutdown completed within the timeout; otherwise <c>false</c>.</returns>
        protected override bool OnShutdown(int timeoutMilliseconds)
            => RunBounded(token => this.ShutdownAsync(token), timeoutMilliseconds);

        /// <summary>
        /// Called by the base <c>ForceFlush</c> (invoked by a standard <c>TracerProvider</c> force flush).
        /// It bridges to <see cref="ForceFlushAsync"/> so the queued activities and any in-flight export
        /// are drained and the exporter is flushed, bounded by <paramref name="timeoutMilliseconds"/>. On
        /// timeout it returns <c>false</c> without cancelling the worker.
        /// </summary>
        /// <param name="timeoutMilliseconds">The bound on the caller's wait, or <see cref="Timeout.Infinite"/>.</param>
        /// <returns><c>true</c> when the flush completed within the timeout; otherwise <c>false</c>.</returns>
        protected override bool OnForceFlush(int timeoutMilliseconds)
            => RunBounded(token => this.ForceFlushAsync(token), timeoutMilliseconds);

        /// <summary>
        /// Runs an asynchronous operation from a synchronous <see cref="BaseProcessor{T}"/> hook without
        /// risking a sync-over-async deadlock, bounded by <paramref name="timeoutMilliseconds"/>. The
        /// operation is started on the thread pool (its awaits already use <c>ConfigureAwait(false)</c>,
        /// so no caller context is captured) and awaited with a bounded <see cref="Task.Wait(int)"/>. On
        /// timeout the token is cancelled to stop the operation's own wait — never the background drain —
        /// and <c>false</c> is returned; any fault is swallowed and reported as <c>false</c> so the hook
        /// never throws.
        /// </summary>
        /// <param name="operation">The bounded operation, receiving a token that fires only on timeout.</param>
        /// <param name="timeoutMilliseconds">The wait bound, or <see cref="Timeout.Infinite"/> to wait indefinitely.</param>
        /// <returns><c>true</c> when the operation completed within the timeout; otherwise <c>false</c>.</returns>
        private static bool RunBounded(Func<CancellationToken, Task> operation, int timeoutMilliseconds)
        {
            var timeoutSource = new CancellationTokenSource();

            // Offload to the thread pool so the synchronous caller's context is never captured and cannot
            // deadlock (the async chain uses ConfigureAwait(false) throughout).
            var task = Task.Run(() => operation(timeoutSource.Token));

            bool completed;
            try
            {
                completed = task.Wait(timeoutMilliseconds);
            }
            catch (Exception)
            {
                // The operation faulted within the timeout; hooks must not throw.
                completed = false;
            }

            if (!completed)
            {
                // Timed out (or faulted): stop the operation's own wait — never the background drain.
                timeoutSource.Cancel();
            }

            // Dispose the timeout source and observe any fault only once the background operation has
            // finished using the token, so we neither race a disposed token nor leave an unobserved
            // task exception when the caller stopped waiting early.
            task.ContinueWith(
                static (t, state) =>
                {
                    _ = t.Exception;
                    ((CancellationTokenSource)state!).Dispose();
                },
                timeoutSource,
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);

            return completed;
        }

        /// <summary>
        /// The main processing loop that batches and exports activities asynchronously. It drains until
        /// shutdown has begun, the queue is empty, and no producer is mid-enqueue, then shuts the exporter
        /// down exactly once.
        /// </summary>
        /// <returns>A <see cref="Task"/> representing the asynchronous processing loop.</returns>
        private async Task ProcessLoopAsync()
        {
            try
            {
                while (true)
                {
                    if (!this.queue.IsEmpty)
                    {
                        // Never cancel an in-flight export merely because shutdown started.
                        await this.ExportNextBatchAsync().ConfigureAwait(false);
                        continue;
                    }

                    if (Volatile.Read(ref this.state) == StateRunning)
                    {
                        // Idle: wait for work or a shutdown signal (bounded so state changes are re-checked).
                        await this.signal.WaitAsync(this.workerIdleWaitMilliseconds).ConfigureAwait(false);
                        continue;
                    }

                    // Draining and the queue looks empty. Only exit once no producer is mid-enqueue: a
                    // producer increments activeProducers before it reads the state, so observing zero here
                    // (after having observed StateDraining above) proves no accepted activity can still be
                    // added — nothing is stranded. If a producer is in flight, wait for it (it releases the
                    // signal on its way out) and re-check.
                    if (Volatile.Read(ref this.activeProducers) != 0)
                    {
                        await this.signal.WaitAsync(this.workerIdleWaitMilliseconds).ConfigureAwait(false);
                        continue;
                    }

                    if (!this.queue.IsEmpty)
                    {
                        continue;
                    }

                    break;
                }
            }
            finally
            {
                await this.ShutdownExporterOnceAsync().ConfigureAwait(false);

                // Full-fence publish of the Stopped transition. Interlocked.Exchange is a full barrier, so
                // the Stopped store is globally visible before the disposeRequested read below, which
                // cannot be reordered ahead of it. Together with the symmetric full fence in Dispose this
                // forms a Dekker handshake: at least one side observes the other's write, so a requested
                // disposal is never lost, and the exactly-once claim prevents a double dispose.
                Interlocked.Exchange(ref this.state, StateStopped);

                // If disposal was requested while the worker still owned the exporter, dispose it now that
                // the worker is done using it. The claim guard keeps this to exactly one disposal.
                if (Volatile.Read(ref this.disposeRequested) == 1)
                {
                    this.DisposeExporterOnce();
                }
            }
        }

        /// <summary>
        /// Dequeues up to <see cref="maxExportBatchSize"/> activities and exports them. The
        /// <see cref="activeExport"/> flag is raised for the whole operation (before the dequeue) so a
        /// concurrent <see cref="ForceFlushAsync"/> never observes an empty queue while a batch that has
        /// just been dequeued is still being exported.
        /// </summary>
        /// <returns>A <see cref="Task"/> representing the asynchronous export of the next batch.</returns>
        private async Task ExportNextBatchAsync()
        {
            Volatile.Write(ref this.activeExport, 1);
            try
            {
                var batch = new List<Activity>(this.maxExportBatchSize);
                while (batch.Count < this.maxExportBatchSize && this.queue.TryDequeue(out var item))
                {
                    batch.Add(item);
                }

                if (batch.Count > 0)
                {
                    try
                    {
                        await this.exporter.ExportAsync(batch, CancellationToken.None).ConfigureAwait(false);
                    }
                    catch (Exception)
                    {
                        // Best-effort drain: a failing batch must not fault the worker or stop the drain.
                    }
                }
            }
            finally
            {
                Volatile.Write(ref this.activeExport, 0);
            }
        }

        /// <summary>
        /// Returns a string that represents the current processor.
        /// </summary>
        /// <returns>
        /// A string containing the friendly type name of the processor and exporter.
        /// </returns>
        public override string ToString()
            => this.friendlyTypeName;

        /// <summary>
        /// Releases resources used by the processor and exporter. Requests a graceful drain and transfers
        /// exporter-disposal ownership to the worker when it is still running, so the exporter is never
        /// disposed while an export is in flight and is disposed exactly once.
        /// </summary>
        /// <param name="disposing">Whether managed resources should be released.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && Interlocked.Exchange(ref this.disposed, 1) == 0)
            {
                // Record intent with a full fence before reading the state, so this cannot be reordered
                // after the state read. Interlocked.Exchange is a full barrier; paired with the worker's
                // full-fence Stopped transition it forms a Dekker handshake — at least one side observes
                // the other's write, so the exporter is disposed exactly once and never leaked.
                Interlocked.Exchange(ref this.disposeRequested, 1);
                this.BeginShutdown();

                // If the worker already stopped it will not dispose the exporter, so do it here. Whichever
                // party wins the claim disposes exactly once.
                if (Volatile.Read(ref this.state) == StateStopped)
                {
                    this.DisposeExporterOnce();
                }
            }

            base.Dispose(disposing);
        }

        /// <summary>
        /// Atomically moves the lifecycle from <see cref="StateRunning"/> to <see cref="StateDraining"/>
        /// (a no-op once draining/stopped) and wakes the worker so it observes the change.
        /// </summary>
        private void BeginShutdown()
        {
            Interlocked.CompareExchange(ref this.state, StateDraining, StateRunning);
            this.TryReleaseSignal();
        }

        /// <summary>
        /// Whether the queue is empty and no export is currently in flight.
        /// </summary>
        /// <returns><c>true</c> when there is no pending or in-flight work.</returns>
        private bool IsDrained()
            => this.queue.IsEmpty && Volatile.Read(ref this.activeExport) == 0;

        /// <summary>
        /// Shuts the exporter down exactly once, swallowing exceptions so the worker never faults.
        /// </summary>
        /// <returns>A <see cref="Task"/> representing the asynchronous exporter shutdown.</returns>
        private async Task ShutdownExporterOnceAsync()
        {
            if (Interlocked.Exchange(ref this.exporterShutdownStarted, 1) == 0)
            {
                try
                {
                    await this.exporter.ShutdownAsync(CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception)
                {
                    // Best-effort: never fault the worker on exporter shutdown.
                }
            }
        }

        /// <summary>
        /// Disposes the exporter exactly once, swallowing exceptions.
        /// </summary>
        private void DisposeExporterOnce()
        {
            if (Interlocked.Exchange(ref this.exporterDisposeClaim, 1) == 0)
            {
                try
                {
                    this.exporter.Dispose();
                }
                catch (Exception)
                {
                    // Best-effort: disposal must not throw.
                }
            }
        }

        /// <summary>
        /// Releases the worker signal, tolerating a disposed or saturated semaphore.
        /// </summary>
        private void TryReleaseSignal()
        {
            try
            {
                this.signal.Release();
            }
            catch (ObjectDisposedException)
            {
                // The worker owns the semaphore's lifetime; a race on teardown is benign.
            }
            catch (SemaphoreFullException)
            {
                // Already at the maximum count; the worker will still observe pending work.
            }
        }

        /// <summary>
        /// Awaits <paramref name="task"/> but returns early (throwing <see cref="OperationCanceledException"/>)
        /// if <paramref name="cancellationToken"/> fires first. Unlike <c>Task.WaitAsync</c> this is available
        /// on netstandard2.0; the awaited task keeps running and is left owned by its originator.
        /// </summary>
        /// <param name="task">The task to await.</param>
        /// <param name="cancellationToken">A token that ends the wait early.</param>
        /// <returns>A <see cref="Task"/> that completes when the task finishes or the token fires.</returns>
        private static async Task WaitAsync(Task task, CancellationToken cancellationToken)
        {
            if (task.IsCompleted || !cancellationToken.CanBeCanceled)
            {
                await task.ConfigureAwait(false);
                return;
            }

            var cancellationTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using (cancellationToken.Register(static state => ((TaskCompletionSource<bool>)state!).TrySetResult(true), cancellationTcs))
            {
                var completed = await Task.WhenAny(task, cancellationTcs.Task).ConfigureAwait(false);
                if (completed != task)
                {
                    throw new OperationCanceledException(cancellationToken);
                }
            }

            // Observe the worker's result/exception now that it has completed.
            await task.ConfigureAwait(false);
        }
    }
}
