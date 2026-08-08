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
    /// Drains durably-persisted Agent365 exports back onto the wire. On a fixed cadence it asks the
    /// shared <see cref="Agent365TransmissionGate"/> for a permit and, when granted, reads at most a
    /// bounded number of leased records and replays each with freshly resolved authentication:
    /// <list type="bullet">
    ///   <item>A delivered record is deleted. If the delete fails, a duplicate-risk warning is logged.</item>
    ///   <item>A retryable failure retains the record, backs the gate off, and stops the pass.</item>
    ///   <item>A permanent failure or an unreadable/poison blob is deleted (it can never succeed).</item>
    ///   <item>A record whose token cannot be resolved is retained for a later pass.</item>
    /// </list>
    /// Exactly one background loop runs between <see cref="Start"/> and <see cref="StopAsync"/>. The class
    /// targets <c>netstandard2.0</c>, so the loop uses a cancellation-aware delay rather than
    /// <c>PeriodicTimer</c> or <c>System.Timers.Timer</c>. The single half-open gate probe is only ever
    /// released when this pass owns it and recorded no terminal gate outcome, so a probe is never leaked
    /// nor double-released.
    /// </summary>
    internal sealed class Agent365ReplayCoordinator
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
        /// loop ever runs.
        /// </summary>
        internal void Start()
        {
            lock (_lifecycleLock)
            {
                // Publish _runTask under the same lock StopAsync reads it under. This removes the race
                // where a concurrent StopAsync observes a null _runTask (Start had CAS-ed _started but not
                // yet assigned _runTask) and returns without awaiting the loop. If StopAsync already ran,
                // _shutdown is cancelled and no loop is launched, so a stopped coordinator never revives.
                if (_started || _shutdown.IsCancellationRequested)
                {
                    return;
                }

                _started = true;
                _runTask = Task.Run(RunAsync);
            }
        }

        /// <summary>
        /// Signals the background loop to stop and awaits its completion. Safe to call when the loop was
        /// never started. Never blocks past <paramref name="cancellationToken"/>.
        /// </summary>
        internal async Task StopAsync(CancellationToken cancellationToken)
        {
            Task? run;
            lock (_lifecycleLock)
            {
                if (!_shutdown.IsCancellationRequested)
                {
                    _shutdown.Cancel();
                }

                // Read the run task under the same lock Start publishes it under, so a Start that has
                // already begun is guaranteed visible here. If Start has not yet run, _shutdown is now
                // cancelled and that later Start will observe it and decline to launch a loop.
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
                        // Replaying this one record threw a non-cancellation exception. A single bad record
                        // must not tear down the pass (nor, via RunAsync, the whole loop): quarantine it as
                        // poison so it cannot wedge the queue, then continue with the remaining records.
                        _logger.LogError(
                            ex,
                            "Agent365ReplayCoordinator: Replaying a record threw a non-cancellation exception; " +
                            "quarantining it as poison and continuing the pass.");
                        DeleteRecord(stored, delivered: false);
                        continue;
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
                // permanent-failure, token-unavailable, poison, cancellation and empty-storage exits.
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
