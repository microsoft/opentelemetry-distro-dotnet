// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Microsoft.Agents.A365.Observability.Runtime.Tracing.Exporters
{
    /// <summary>
    /// Lifecycle surface of the durable replay loop, factored into an interface so an exporter can own
    /// the loop's lifetime without depending on the concrete <see cref="Agent365ReplayCoordinator"/> and
    /// so tests can substitute a fake. <see cref="Start"/> launches the single background loop,
    /// <see cref="StopAsync"/> stops it asynchronously, and <see cref="System.IDisposable.Dispose"/>
    /// stops it synchronously.
    /// </summary>
    internal interface IAgent365ReplayCoordinator : IDisposable
    {
        /// <summary>Starts the single background replay loop. Idempotent.</summary>
        void Start();

        /// <summary>Signals the background loop to stop and awaits its completion.</summary>
        Task StopAsync(CancellationToken cancellationToken);
    }

    /// <summary>
    /// Drains durably-persisted Agent365 exports back onto the wire. On a fixed cadence it asks the
    /// shared <see cref="Agent365TransmissionGate"/> for a permit and, when granted, reads at most a
    /// bounded number of leased records and replays each with freshly resolved authentication:
    /// <list type="bullet">
    ///   <item>A delivered record is deleted. If the delete fails, a duplicate-risk warning is logged.</item>
    ///   <item>A retryable failure retains the record, backs the gate off, and stops the pass.</item>
    ///   <item>A permanent failure or an unreadable/poison blob is deleted (it can never succeed).</item>
    ///   <item>A record whose token cannot be resolved is retained for a later pass.</item>
    ///   <item>An unknown replay exception or global misconfiguration retains the current record and stops the
    ///   pass; durable telemetry is never deleted for an unknown fault (only readable/poison and permanent
    ///   failures delete).</item>
    /// </list>
    /// Exactly one background loop runs between <see cref="Start"/> and <see cref="StopAsync"/>. The class
    /// targets <c>netstandard2.0</c>, so the loop uses a cancellation-aware delay rather than
    /// <c>PeriodicTimer</c> or <c>System.Timers.Timer</c>. The single half-open gate probe is only ever
    /// released when this pass owns it and recorded no terminal gate outcome, so a probe is never leaked
    /// nor double-released.
    /// </summary>
    internal sealed class Agent365ReplayCoordinator : IAgent365ReplayCoordinator
    {
        internal static readonly TimeSpan DefaultReplayInterval = TimeSpan.FromMinutes(2);

        private readonly IAgent365PersistentStorage _storage;
        private readonly Agent365TransmissionGate _gate;
        private readonly Func<Agent365DurableRecord, CancellationToken, Task<Agent365SendOutcome>> _replayAsync;
        private readonly ILogger _logger;
        private readonly TimeSpan _replayInterval;
        private readonly TimeSpan _leaseDuration;
        private readonly int _maxRecordsPerPass;
        private readonly Func<TimeSpan, CancellationToken, Task> _delayAsync;
        private readonly CancellationTokenSource _shutdown = new();

        private readonly object _lifecycleLock = new();
        private bool _started;
        private bool _shutdownRequested;
        private bool _disposed;
        private Task? _runTask;

        /// <summary>
        /// Initializes a new instance of the <see cref="Agent365ReplayCoordinator"/> class.
        /// </summary>
        /// <param name="storage">Durable store the persisted records are drained from.</param>
        /// <param name="gate">Shared transmission gate that permits or defers each pass.</param>
        /// <param name="replayAsync">Delegate that replays a single record and classifies the single send attempt.</param>
        /// <param name="logger">Logger for warnings (e.g. duplicate risk on delete failure).</param>
        /// <param name="replayInterval">Delay between passes. Defaults to <see cref="DefaultReplayInterval"/>.</param>
        /// <param name="maxRecordsPerPass">Upper bound on records handled per pass. Defaults to 10.</param>
        /// <param name="delayAsync">
        /// Cancellation-aware delay used by the background loop. Defaults to <see cref="Task.Delay(TimeSpan, CancellationToken)"/>;
        /// injectable so the loop can be stepped deterministically in tests.
        /// </param>
        internal Agent365ReplayCoordinator(
            IAgent365PersistentStorage storage,
            Agent365TransmissionGate gate,
            Func<Agent365DurableRecord, CancellationToken, Task<Agent365SendOutcome>> replayAsync,
            ILogger logger,
            TimeSpan? replayInterval = null,
            int maxRecordsPerPass = 10,
            Func<TimeSpan, CancellationToken, Task>? delayAsync = null)
        {
            _storage = storage ?? throw new ArgumentNullException(nameof(storage));
            _gate = gate ?? throw new ArgumentNullException(nameof(gate));
            _replayAsync = replayAsync ?? throw new ArgumentNullException(nameof(replayAsync));
            _logger = logger ?? NullLogger.Instance;

            if (maxRecordsPerPass < 1)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maxRecordsPerPass), maxRecordsPerPass, "At least one record must be handled per pass.");
            }

            if (replayInterval.HasValue && replayInterval.Value <= TimeSpan.Zero)
            {
                // A non-positive interval would spin the background loop with no delay between passes.
                throw new ArgumentOutOfRangeException(
                    nameof(replayInterval), replayInterval, "The replay interval must be positive.");
            }

            _replayInterval = replayInterval ?? DefaultReplayInterval;
            _maxRecordsPerPass = maxRecordsPerPass;

            // A lease that covers one full cycle prevents a second worker (or the next pass) from grabbing
            // a record still being processed, while expiring in time for the next cadence to retry it.
            _leaseDuration = _replayInterval;
            _delayAsync = delayAsync ?? ((interval, token) => Task.Delay(interval, token));
        }

        /// <summary>
        /// Starts the single background replay loop. Idempotent: repeated calls are no-ops so only one
        /// loop ever runs. A no-op once shutdown has been requested or the coordinator disposed, so a
        /// stopped coordinator never revives.
        /// </summary>
        public void Start()
        {
            lock (_lifecycleLock)
            {
                // Publish _runTask under the same lock StopAsync reads it under. This removes the race
                // where a concurrent StopAsync observes a null _runTask (Start had CAS-ed _started but not
                // yet assigned _runTask) and returns without awaiting the loop. If shutdown was already
                // requested (StopAsync/Dispose ran), no loop is launched, so a stopped coordinator never
                // revives.
                if (_started || _shutdownRequested)
                {
                    return;
                }

                _started = true;
                _runTask = Task.Run(RunAsync);
            }
        }

        /// <summary>
        /// Signals the background loop to stop and awaits its completion. Safe to call when the loop was
        /// never started, and safe to call more than once. Never blocks past <paramref name="cancellationToken"/>.
        /// </summary>
        public async Task StopAsync(CancellationToken cancellationToken)
        {
            Task? run;
            lock (_lifecycleLock)
            {
                RequestShutdownNoLock();

                // Read the run task under the same lock Start publishes it under, so a Start that has
                // already begun is guaranteed visible here. If Start has not yet run, shutdown is now
                // requested and that later Start will observe it and decline to launch a loop.
                run = _runTask;
            }

            if (run == null)
            {
                return;
            }

            // RunAsync swallows its own cancellation/exceptions, so awaiting it will not throw. Cap the
            // wait with the caller's token so shutdown cannot hang.
            var cancelTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using (cancellationToken.Register(() => cancelTcs.TrySetResult(true)))
            {
                await Task.WhenAny(run, cancelTcs.Task).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Stops the background loop synchronously and releases the shutdown token source. Idempotent, so
        /// a double dispose (or a dispose after <see cref="StopAsync"/>) is safe. Awaiting the loop here
        /// cannot throw because <see cref="RunAsync"/> swallows its own cancellation/exceptions.
        /// </summary>
        public void Dispose()
        {
            Task? run;
            lock (_lifecycleLock)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                RequestShutdownNoLock();
                run = _runTask;
            }

            // Await the loop so no background pass outlives Dispose. The cancellation-aware delay unblocks
            // immediately on the shutdown signal, so this returns promptly.
            run?.GetAwaiter().GetResult();
            _shutdown.Dispose();
        }

        /// <summary>
        /// Requests cooperative shutdown exactly once. Must be called under <see cref="_lifecycleLock"/>.
        /// Uses a dedicated flag rather than <c>_shutdown.IsCancellationRequested</c> so it never touches a
        /// <see cref="CancellationTokenSource"/> that <see cref="Dispose"/> may have already released.
        /// </summary>
        private void RequestShutdownNoLock()
        {
            if (_shutdownRequested)
            {
                return;
            }

            _shutdownRequested = true;
            _shutdown.Cancel();
        }

        private async Task RunAsync()
        {
            while (!_shutdown.IsCancellationRequested)
            {
                try
                {
                    await _delayAsync(_replayInterval, _shutdown.Token).ConfigureAwait(false);
                    await ReplayOnceAsync(_shutdown.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
                {
                    // Cooperative shutdown requested via StopAsync: exit the loop.
                    break;
                }
                catch (Exception ex)
                {
                    // One intentional, long-lived background task: never let a single pass tear it down.
                    // This also catches a non-shutdown OperationCanceledException (e.g. a stray token
                    // cancellation not originating from StopAsync), which must be logged and survived
                    // rather than silently killing the loop.
                    _logger.LogError(ex, "Agent365ReplayCoordinator: Unhandled exception during a replay pass.");
                }
            }
        }

        /// <summary>
        /// Runs a single replay pass: acquire a gate permit, then read, lease, and replay up to
        /// <see cref="_maxRecordsPerPass"/> records. Stops early on a retryable outcome (retaining the
        /// current record) or cancellation.
        /// </summary>
        internal async Task ReplayOnceAsync(CancellationToken cancellationToken)
        {
            if (!_gate.TryAcquire(out var ownsProbe))
            {
                // Gate is in backoff: skip the network entirely this pass.
                return;
            }

            var recordedTerminalGateOutcome = false;
            try
            {
                for (var processed = 0; processed < _maxRecordsPerPass; processed++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (!_storage.TryGetNext(out var stored))
                    {
                        break;
                    }

                    if (!stored!.TryLease(_leaseDuration))
                    {
                        // The real FileBlobProvider is non-destructive: a failed lease leaves the same
                        // unleased blob at the head of the queue, so the next TryGetNext re-serves it. A
                        // "continue" here would therefore re-fetch and re-lease the identical blob up to
                        // _maxRecordsPerPass times (a tight spin). Stop the pass instead; the next cadence
                        // retries it once the contending lease or maintenance window clears.
                        break;
                    }

                    if (!stored.TryRead(out var record))
                    {
                        // Unreadable/unsupported poison blob: discard it so it cannot wedge the queue.
                        DeleteRecord(stored, delivered: false);
                        continue;
                    }

                    Agent365SendOutcome outcome;
                    try
                    {
                        outcome = await _replayAsync(record!, cancellationToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        // Cooperative cancellation (e.g. shutdown mid-flight): retain the leased record and
                        // let the cancellation propagate. It must never be misclassified as poison.
                        throw;
                    }
                    catch (Exception ex)
                    {
                        // Replaying this record threw a non-cancellation exception. The record already
                        // deserialized cleanly (unreadable/poison blobs are deleted at the TryRead boundary
                        // above), so this is an unknown fault far more likely to be a global misconfiguration
                        // (e.g. a throwing DomainResolver or a plaintext endpoint) or a transient dependency
                        // outage than bad data in this one record. Deleting-and-continuing here could discard
                        // up to _maxRecordsPerPass durable records on a single global fault. Retain the
                        // current record — durable telemetry is never deleted for an unknown fault — and stop
                        // the pass; the next cadence retries it once the fault clears. A single bad record
                        // still cannot tear down the loop because RunAsync also survives exceptions.
                        _logger.LogError(
                            ex,
                            "Agent365ReplayCoordinator: Replaying a record threw an unknown non-cancellation " +
                            "exception; retaining the record and stopping the pass to avoid deleting durable " +
                            "telemetry on a global fault.");
                        return;
                    }

                    switch (outcome.Disposition)
                    {
                        case Agent365SendDisposition.Delivered:
                            _gate.RecordSuccess();
                            recordedTerminalGateOutcome = true;
                            DeleteRecord(stored, delivered: true);
                            break;

                        case Agent365SendDisposition.RetryableFailure:
                            _gate.RecordRetryableFailure(outcome.RetryAfter);
                            recordedTerminalGateOutcome = true;
                            // Retain the record and stop the pass; the gate is now in backoff.
                            return;

                        case Agent365SendDisposition.PermanentFailure:
                            // Permanent, non-retryable failure (e.g. 403): discard without signalling the
                            // gate (a permanent failure is not an availability signal).
                            DeleteRecord(stored, delivered: false);
                            break;

                        case Agent365SendDisposition.TokenUnavailable:
                            // No token this pass: retain the record and continue with the others, exactly
                            // as the live path leaves other identities' work undisturbed.
                            break;

                        case Agent365SendDisposition.Canceled:
                            // Retain the current record and stop the pass.
                            return;
                    }
                }
            }
            finally
            {
                // Release the single half-open probe only when this pass owns it and recorded no terminal
                // gate outcome (RecordSuccess/RecordRetryableFailure already reset the probe). This covers
                // permanent-failure, token-unavailable, poison, unknown-fault retain-and-stop, cancellation
                // and empty-storage exits.
                if (ownsProbe && !recordedTerminalGateOutcome)
                {
                    _gate.ReleaseProbe();
                }
            }
        }

        private void DeleteRecord(IAgent365StoredRecord stored, bool delivered)
        {
            if (stored.TryDelete())
            {
                return;
            }

            if (delivered)
            {
                _logger.LogWarning(
                    "Agent365ReplayCoordinator: A replayed record was delivered but could not be deleted; " +
                    "it may be re-sent on a later pass (possible duplicate telemetry).");
            }
            else
            {
                _logger.LogWarning(
                    "Agent365ReplayCoordinator: A poison or permanently-failed record could not be deleted; " +
                    "it will be retried until storage maintenance removes it.");
            }
        }
    }
}
