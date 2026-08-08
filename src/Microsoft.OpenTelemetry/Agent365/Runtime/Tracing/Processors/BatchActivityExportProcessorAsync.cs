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
        private int exporterShutdownStarted;
        private int exporterDisposeClaim;
        private int disposeRequested;
        private int disposed;

        /// <summary>
        /// Initializes a new instance of the <see cref="BatchActivityExportProcessorAsync"/> class.
        /// </summary>
        /// <param name="exporter">The async exporter instance.</param>
        /// <param name="maxQueueSize">Maximum queue size.</param>
        /// <param name="scheduledDelayMilliseconds">Delay between exports in ms.</param>
        /// <param name="maxExportBatchSize">Max batch size per export.</param>
        public BatchActivityExportProcessorAsync(
            BaseExporterAsync<Activity> exporter,
            int maxQueueSize = DefaultMaxQueueSize,
            int scheduledDelayMilliseconds = DefaultScheduledDelayMilliseconds,
            int maxExportBatchSize = DefaultMaxExportBatchSize)
        {
            this.exporter = exporter ?? throw new ArgumentNullException(nameof(exporter));
            this.maxQueueSize = maxQueueSize;
            this.scheduledDelayMilliseconds = scheduledDelayMilliseconds;
            this.maxExportBatchSize = maxExportBatchSize;

            // A positive scheduled delay bounds how long the idle worker sleeps before it re-checks state,
            // acting as a safety net; every enqueue and shutdown also releases the signal to wake it early.
            this.workerIdleWaitMilliseconds = scheduledDelayMilliseconds > 0
                ? scheduledDelayMilliseconds
                : Timeout.Infinite;

            this.queue = new ConcurrentQueue<Activity>();
            this.signal = new SemaphoreSlim(0);
            this.friendlyTypeName = $"{this.GetType().Name}{{{exporter.GetType().Name}}}";

            // Start the worker last so every field it reads is fully initialized before it runs.
            this.workerTask = Task.Run(this.ProcessLoopAsync);
        }

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
        /// Enqueues activity data for export. Once shutdown has begun the processor no longer accepts
        /// activities; if the queue is full the data is dropped.
        /// </summary>
        /// <param name="data">The activity to export.</param>
        private void OnExport(Activity data)
        {
            if (Volatile.Read(ref this.state) != StateRunning)
            {
                throw new ObjectDisposedException(nameof(BatchActivityExportProcessorAsync));
            }

            if (this.queue.Count < this.maxQueueSize)
            {
                this.queue.Enqueue(data);
                this.TryReleaseSignal();
            }

            // else: drop, could count dropped
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
        /// The main processing loop that batches and exports activities asynchronously. It drains until
        /// shutdown has begun and the queue is empty, then shuts the exporter down exactly once.
        /// </summary>
        /// <returns>A <see cref="Task"/> representing the asynchronous processing loop.</returns>
        private async Task ProcessLoopAsync()
        {
            try
            {
                while (Volatile.Read(ref this.state) == StateRunning || !this.queue.IsEmpty)
                {
                    if (this.queue.IsEmpty)
                    {
                        // Idle: wait for work or a shutdown signal (bounded so state changes are re-checked).
                        await this.signal.WaitAsync(this.workerIdleWaitMilliseconds).ConfigureAwait(false);
                        continue;
                    }

                    // Never cancel an in-flight export merely because shutdown started.
                    await this.ExportNextBatchAsync().ConfigureAwait(false);
                }
            }
            finally
            {
                await this.ShutdownExporterOnceAsync().ConfigureAwait(false);
                Volatile.Write(ref this.state, StateStopped);

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
                // Record intent before waking the worker so a worker finishing concurrently observes it.
                Volatile.Write(ref this.disposeRequested, 1);
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
