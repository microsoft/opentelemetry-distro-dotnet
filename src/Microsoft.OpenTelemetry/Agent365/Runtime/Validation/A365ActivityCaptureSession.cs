// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Agents.A365.Observability.Runtime.Common;
using Microsoft.Agents.A365.Observability.Runtime.Tracing.Scopes;

namespace Microsoft.Agents.A365.Observability.Runtime.Validation;

/// <summary>
/// Temporarily attaches a process-wide <see cref="ActivityListener"/> to
/// capture recognized A365 GenAI spans and waits for a quiet period before
/// producing immutable snapshots.
/// </summary>
/// <remarks>
/// <para>
/// The session has exactly two states: <em>listening</em> and <em>closed</em>.
/// The transition between them is the session's closure boundary, and it is
/// performed exactly once, under <see cref="gate"/>, by whichever
/// <see cref="CompleteAsync"/> call first decides the outcome (a full quiet
/// period, or the completion deadline). The same gate serializes every
/// listener-callback mutation of the session's observation state
/// (<see cref="active"/>, <see cref="completed"/> and
/// <see cref="eligibleChangeVersion"/>), so the boundary is atomic with
/// respect to <see cref="OnStarted"/>/<see cref="OnStopped"/>: a callback
/// either wins the gate and is fully applied inside the evaluation window,
/// or loses it and is ignored as being outside the window. There is no
/// interleaving in which a callback is partially applied across the
/// boundary, and no unsynchronized re-read of live collections is used to
/// decide the final result.
/// </para>
/// <para>
/// Two operations are deliberately performed <em>outside</em> the gate.
/// First, evaluating the customer-supplied span filter, which must never run
/// while a lock is held. Second, physically detaching the
/// <see cref="ActivityListener"/>: <c>ActivityListener.Dispose</c> takes
/// <c>DiagnosticSource</c>'s own internal listener-list locks, which are
/// already held while activity callbacks are dispatched, so detaching under
/// <see cref="gate"/> would invert the lock order and could deadlock against
/// a concurrent <c>Activity.Start</c>/<c>Stop</c>. Detaching immediately
/// after the gate is released is safe and sufficient: the logical closure
/// recorded under the gate already causes every in-flight or subsequent
/// callback to be ignored.
/// </para>
/// </remarks>
internal sealed class A365ActivityCaptureSession : IDisposable
{
    /// <summary>
    /// The window of no eligible activity that must elapse before a capture
    /// session declares success. Exposed internally so option validation can
    /// reject a <see cref="A365ValidationOptions.SpanCompletionTimeout"/> that
    /// is too short for a full quiet period to ever be observed.
    /// </summary>
    internal static readonly TimeSpan QuietPeriod = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// Serializes listener-callback state mutation against the single
    /// session-closure decision. See the type-level remarks.
    /// </summary>
    private readonly object gate = new();

    private readonly ConcurrentDictionary<Activity, byte> active = new();
    private readonly ConcurrentQueue<Activity> completed = new();
    private readonly ConcurrentDictionary<Activity, Lazy<FilterDecision>> filterDecisions = new();
    private readonly Func<A365SpanSnapshot, bool>? spanFilter;
    private readonly ActivityListener listener;
    private long eligibleChangeVersion;

    /// <summary>
    /// Whether the session has left the listening state. Written only under
    /// <see cref="gate"/> (where it is authoritative); read without the gate
    /// only as a cheap advisory fast path.
    /// </summary>
    private volatile bool closed;

    /// <summary>
    /// The immutable state captured at the closure boundary. Guarded by
    /// <see cref="gate"/>. Non-null exactly when the session is closed by a
    /// completion decision, which makes closure idempotent: concurrent or
    /// repeated <see cref="CompleteAsync"/> calls all report the same
    /// boundary rather than deriving a second, inconsistent one.
    /// </summary>
    private ClosureState? closure;

    private bool disposed;

    internal A365ActivityCaptureSession(Func<A365SpanSnapshot, bool>? spanFilter)
    {
        this.spanFilter = spanFilter;

        listener = new ActivityListener
        {
            ShouldListenTo = _ => true,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllDataAndRecorded,
            SampleUsingParentId = (ref ActivityCreationOptions<string> _) =>
                ActivitySamplingResult.AllDataAndRecorded,
            ActivityStarted = OnStarted,
            ActivityStopped = OnStopped,
        };

        ActivitySource.AddActivityListener(listener);
    }

    /// <summary>
    /// Gets or sets a test-only synchronization hook. When set, it is invoked
    /// synchronously inside <see cref="OnStopped"/>, while the closure gate is
    /// held, after a stopping activity has been made durably observable
    /// (queued to the completed set when eligible) but before it is removed
    /// from the active set. Production code never sets this; it exists solely
    /// so regression tests can deterministically hold the gate across that
    /// transition -- for example to force a stop callback to win the gate
    /// against a concurrent completion boundary -- instead of relying on
    /// wall-clock timing.
    /// </summary>
    internal Action<Activity>? OnStoppedTransitionHookForTests { get; set; }

    /// <summary>
    /// Gets or sets a test-only synchronization hook. When set, it is invoked
    /// synchronously by <see cref="OnStarted"/> and <see cref="OnStopped"/>
    /// immediately before they acquire the closure gate (and after any span
    /// filter evaluation, which never runs under the gate). Production code
    /// never sets this; tests use it to park a listener callback just outside
    /// the gate so that a concurrent completion boundary provably wins the
    /// race and the callback is then observed to fall outside the evaluation
    /// window.
    /// </summary>
    internal Action<Activity>? OnBeforeCallbackGateForTests { get; set; }

    /// <summary>
    /// Gets or sets a test-only synchronization hook. When set, it is invoked
    /// synchronously by <see cref="CompleteAsync"/> immediately before it
    /// acquires the closure gate to make its final success or timeout
    /// decision -- that is, after the unsynchronized candidate quiet-period
    /// check has already passed. Production code never sets this; tests use
    /// it to deterministically inject listener callbacks into exactly that
    /// window, which is the race the gate exists to close.
    /// </summary>
    internal Action? OnBeforeClosureGateForTests { get; set; }

    /// <summary>
    /// Waits for a 250-millisecond quiet period, bounded by <paramref name="timeout"/>,
    /// during which no new eligible activity starts or stops. Returns filtered
    /// snapshots for completed eligible spans and snapshots for any span that
    /// was still active and eligible when the timeout elapsed. The session is
    /// closed and its listener detached before this method returns, so the
    /// returned result describes a boundary that no later activity can change.
    /// </summary>
    /// <param name="timeout">The maximum time to wait for span completion.</param>
    /// <param name="cancellationToken">A token used to cancel the wait.</param>
    /// <returns>The captured result.</returns>
    internal async Task<A365CaptureResult> CompleteAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Closure is idempotent: if this session's boundary has already
            // been defined (by a concurrent or earlier CompleteAsync call),
            // report that same boundary instead of deriving a second one
            // from collections that are no longer being updated.
            var establishedClosure = TryGetClosure();
            if (establishedClosure != null)
            {
                return CreateResult(establishedClosure);
            }

            var remaining = timeout - stopwatch.Elapsed;
            if (remaining <= TimeSpan.Zero)
            {
                break;
            }

            // Never sleep past the remaining timeout budget: cap the delay to
            // whatever time is left instead of always waiting the full quiet
            // period, so CompleteAsync returns promptly once the deadline is
            // reached instead of intentionally oversleeping by up to QuietPeriod.
            var isFullQuietPeriodDelay = remaining >= QuietPeriod;
            var delay = isFullQuietPeriodDelay ? QuietPeriod : remaining;

            var version = Interlocked.Read(ref eligibleChangeVersion);

            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);

            // Unsynchronized *candidate* check. It is deliberately performed
            // without the gate because it evaluates the span filter (customer
            // code, which must never run under a lock) and, in doing so,
            // warms the at-most-once filter-decision cache for everything
            // currently active. It can never decide the outcome on its own:
            // only the gated re-check below is authoritative.
            var isCandidateQuiet = version == Interlocked.Read(ref eligibleChangeVersion) &&
                !active.Keys.Any(IsEligibleForWait);

            if (isCandidateQuiet && isFullQuietPeriodDelay &&
                TryCloseOnQuietPeriod(version, out var quietResult))
            {
                return quietResult!;
            }

            // A residual delay shorter than the full 250ms quiet period
            // cannot, by itself, prove that a genuine quiet period occurred:
            // observing no change for a few milliseconds is much weaker
            // evidence than observing no change for the full window. Only a
            // delay of the full QuietPeriod length can establish that
            // guarantee, so once the remaining budget forces a shorter delay
            // this iteration can never declare success -- the loop must fall
            // through to the deadline boundary below. (A successful full
            // quiet period always returns directly above, so there is no
            // "already proven quiet" case left to consider here.)
            if (!isFullQuietPeriodDelay)
            {
                break;
            }
        }

        return CloseAtDeadline();
    }

    /// <summary>
    /// Test-only helper: reports whether <paramref name="activity"/> is
    /// currently observable through either the active set or the completed
    /// queue. Reads both collections without taking the closure gate, so it
    /// can safely be called while a listener callback holds the gate. Used to
    /// assert that an eligible stopping activity is never visible in neither
    /// collection at the same time, and that a callback which lost the
    /// closure gate was ignored entirely.
    /// </summary>
    internal bool IsObservableForTests(Activity activity)
    {
        return active.ContainsKey(activity) || completed.Contains(activity);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (gate)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;

            // Leaving the listening state under the gate is what actually
            // stops listener callbacks from mutating session state; the
            // physical detach below is a separate, idempotent step.
            closed = true;
        }

        // Deliberately outside the gate (see the type-level remarks on lock
        // ordering against DiagnosticSource's internal listener locks). Safe
        // and idempotent even when CompleteAsync already detached.
        DetachListener();
    }

    /// <summary>
    /// Attempts to end the session on a fully observed quiet period.
    /// </summary>
    /// <param name="observedVersion">
    /// The eligible-change version read before the completed quiet-period
    /// delay, re-validated here under the gate.
    /// </param>
    /// <param name="result">The captured result, when closure succeeded.</param>
    /// <returns>
    /// <see langword="true"/> when the session was closed (or was already
    /// closed) and <paramref name="result"/> describes its boundary;
    /// <see langword="false"/> when a listener callback won the gate first
    /// and thereby invalidated the candidate quiet period, in which case the
    /// caller must keep waiting.
    /// </returns>
    private bool TryCloseOnQuietPeriod(long observedVersion, out A365CaptureResult? result)
    {
        OnBeforeClosureGateForTests?.Invoke();

        ClosureState state;
        lock (gate)
        {
            if (closure == null)
            {
                // Authoritative re-check: any start or stop that won the gate
                // between the unsynchronized candidate check and this point
                // has already been fully applied, so it is visible here and
                // invalidates the quiet period. Conversely, once this check
                // passes and the boundary is recorded below, every later
                // callback is by definition outside the evaluation window.
                if (Interlocked.Read(ref eligibleChangeVersion) != observedVersion ||
                    HasBlockingActiveSpanUnderGate())
                {
                    result = null;
                    return false;
                }

                state = CloseUnderGate(timedOut: false);
            }
            else
            {
                state = closure;
            }
        }

        DetachListener();
        result = CreateResult(state);
        return true;
    }

    /// <summary>
    /// Ends the session at the completion deadline. The gate makes the
    /// deadline an atomic boundary: draining the completed set and snapshotting
    /// the still-active set happen in the same critical section, so a stopping
    /// activity is either wholly before the boundary (completed, and already
    /// removed from the active set) or wholly after it (ignored, and therefore
    /// still recorded as active at the deadline). Deadline state is
    /// authoritative: an activity that completes immediately after it keeps its
    /// per-span timeout identity, and no activity can ever be classified as
    /// both completed and timed out.
    /// </summary>
    /// <returns>The captured result.</returns>
    private A365CaptureResult CloseAtDeadline()
    {
        OnBeforeClosureGateForTests?.Invoke();

        ClosureState state;
        lock (gate)
        {
            state = closure ?? CloseUnderGate(timedOut: true);
        }

        DetachListener();
        return CreateResult(state);
    }

    /// <summary>
    /// Records the session's closure boundary. Must be called while holding
    /// <see cref="gate"/> and only when <see cref="closure"/> is still
    /// <see langword="null"/>.
    /// </summary>
    /// <param name="timedOut">Whether the boundary is the completion deadline.</param>
    /// <returns>The recorded closure state.</returns>
    private ClosureState CloseUnderGate(bool timedOut)
    {
        var completedAtClosure = new List<Activity>();
        while (completed.TryDequeue(out var activity))
        {
            completedAtClosure.Add(activity);
        }

        var activeAtClosure = active.Keys.ToList();

        closed = true;
        closure = new ClosureState(completedAtClosure, activeAtClosure, timedOut);
        return closure;
    }

    private ClosureState? TryGetClosure()
    {
        lock (gate)
        {
            return closure;
        }
    }

    /// <summary>
    /// Detaches the activity listener. Never call this while holding
    /// <see cref="gate"/>: see the type-level remarks on lock ordering.
    /// <see cref="ActivityListener.Dispose"/> is itself idempotent, so this
    /// is safe both when <see cref="CompleteAsync"/> closed the session and
    /// when <see cref="Dispose"/> runs afterwards.
    /// </summary>
    private void DetachListener()
    {
        listener.Dispose();
    }

    /// <summary>
    /// Gated counterpart of <see cref="IsEligibleForWait"/>: reports whether
    /// any currently active activity must block closure, consulting only
    /// already-published filter decisions. It never evaluates the span filter,
    /// because customer predicate code must not run while
    /// <see cref="gate"/> is held.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when at least one active activity is recognized
    /// by name and is not known to be excluded by the span filter.
    /// </returns>
    private bool HasBlockingActiveSpanUnderGate()
    {
        foreach (var activity in active.Keys)
        {
            if (!IsEligible(activity))
            {
                continue;
            }

            if (!filterDecisions.TryGetValue(activity, out var lazyDecision) ||
                !lazyDecision.IsValueCreated)
            {
                // Undecided here, or an evaluation is still in flight on
                // another thread. Treat it conservatively as blocking rather
                // than invoking (or blocking on) the predicate under the
                // gate. This can only postpone a successful closure by one
                // polling iteration -- the next unsynchronized candidate
                // check evaluates and caches the decision -- and can never
                // declare a quiet period that did not happen.
                return true;
            }

            var decision = lazyDecision.Value;
            if (decision.Error != null || decision.PassesFilter)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Determines whether <paramref name="activity"/> is a recognized A365
    /// GenAI span. Eligibility deliberately uses the same tag-or-baggage
    /// lookup (<see cref="ActivityExtensions.GetAttributeOrBaggage"/>) that
    /// <c>Agent365ExporterCore</c> applies when it decides which spans to
    /// export, so a span whose <c>gen_ai.operation.name</c> is carried only in
    /// <see cref="Activity.Baggage"/> is validated exactly as it would be
    /// exported.
    /// </summary>
    private static bool IsEligible(Activity activity)
    {
        var operationName =
            activity.GetAttributeOrBaggage(OpenTelemetryConstants.GenAiOperationNameKey);

        return !string.IsNullOrEmpty(operationName) &&
            OpenTelemetryConstants.GenAiOperationNames.Contains(operationName!);
    }

    /// <summary>
    /// Determines whether <paramref name="activity"/> is both recognized (by
    /// operation name) and, when a <see cref="spanFilter"/> is configured,
    /// passes it. A span that is eligible by name but excluded by the span
    /// filter must not extend the quiet-period wait, nor be reported as a
    /// completion timeout, since the caller has declared it out of scope.
    /// </summary>
    private bool IsEligibleForWait(Activity activity)
    {
        return IsEligible(activity) && TryGetFilterDecision(activity, out _);
    }

    /// <summary>
    /// Returns whether <paramref name="activity"/> passes the configured span
    /// filter, computing and caching that decision the first time the
    /// activity is observed rather than re-invoking the predicate (and
    /// re-allocating a snapshot) on every subsequent check. This is valid
    /// because the documented <see cref="A365ValidationOptions.SpanFilter"/>
    /// contract requires the predicate to depend only on metadata that is
    /// stable for the lifetime of the span, so its result cannot legitimately
    /// change between calls for the same activity.
    /// </summary>
    /// <param name="activity">The activity to evaluate. Assumed already eligible by name.</param>
    /// <param name="snapshotUsedForDecision">
    /// The snapshot created to compute the decision, when this call actually
    /// invoked the predicate (cache miss); callers may reuse it instead of
    /// creating another snapshot for the same purpose. <see langword="null"/>
    /// when the decision was served from the cache, since a fresh snapshot
    /// may be needed to reflect attributes observed after the decision was
    /// first cached.
    /// </param>
    private bool TryGetFilterDecision(Activity activity, out A365SpanSnapshot? snapshotUsedForDecision)
    {
        A365SpanSnapshot? computedSnapshot = null;

        // GetOrAdd's value factory is NOT guaranteed to run at most once
        // under contention: the documented ConcurrentDictionary contract
        // allows multiple threads racing on the same missing key to each
        // invoke the factory concurrently, with only one of the resulting
        // values actually stored (and returned to every caller). If the
        // factory itself evaluated the span filter directly, a customer
        // predicate could therefore run more than once per activity -- and,
        // if it has side effects, run them more than once too. Instead, the
        // factory here only allocates a Lazy<FilterDecision>: allocating an
        // unstarted Lazy is cheap and side-effect-free, so it is harmless if
        // several threads each build one and lose the race to store theirs.
        // Whichever Lazy instance GetOrAdd ultimately returns is the same
        // object for every caller (winner and losers alike), and
        // LazyThreadSafetyMode.ExecutionAndPublication guarantees that only
        // one thread ever executes *that* Lazy's factory delegate -- callers
        // that lose the internal Lazy race block until the winner publishes
        // its result and then observe the identical cached value. This
        // guarantees the span filter predicate is evaluated at most once per
        // activity, and that success, exclusion, and a wrapped predicate
        // exception are all cached and reused identically by every
        // subsequent caller (listener callbacks, the quiet-period wait loop,
        // and CreateResult), regardless of which one happens to observe the
        // activity first.
        var lazyDecision = filterDecisions.GetOrAdd(
            activity,
            _ => new Lazy<FilterDecision>(
                () =>
                {
                    computedSnapshot = CreateSnapshot(activity);
                    try
                    {
                        var passesFilter = spanFilter == null || spanFilter(computedSnapshot);
                        return FilterDecision.Success(passesFilter);
                    }
                    catch (Exception ex)
                    {
                        return FilterDecision.Failure(new A365ValidationExecutionException(
                            $"Span filter failed for span '{computedSnapshot.SpanId}'.",
                            ex));
                    }
                },
                LazyThreadSafetyMode.ExecutionAndPublication));

        var decision = lazyDecision.Value;

        if (decision.Error != null)
        {
            snapshotUsedForDecision = null;
            throw decision.Error;
        }

        // computedSnapshot is only non-null on this call's own stack frame
        // when this call is the one whose closure actually executed the
        // Lazy's factory delegate (i.e. the very first evaluation for this
        // activity, across all callers). Every other caller -- whether
        // served from an already-cached decision or blocked behind another
        // thread's in-flight first evaluation -- correctly reports null here
        // per the documented contract below, since callers already fall back
        // to CreateSnapshot(activity) when they need one.
        snapshotUsedForDecision = decision.PassesFilter ? computedSnapshot : null;
        return decision.PassesFilter;
    }

    /// <summary>
    /// Evaluates whether <paramref name="activity"/> passes the configured
    /// span filter for the sole purpose of deciding whether a listener
    /// callback (<see cref="OnStarted"/>/<see cref="OnStopped"/>) should bump
    /// <see cref="eligibleChangeVersion"/>. Unlike <see cref="TryGetFilterDecision"/>,
    /// a failing predicate must never propagate from here: this method runs
    /// synchronously on whatever thread called <c>Activity.Start</c>/<c>Stop</c>
    /// (typically the instrumented application's own thread), and letting an
    /// exception escape there would skip the listener-state cleanup that
    /// follows (e.g. removing the activity from <see cref="active"/>),
    /// stranding it. The failure is already durably cached by
    /// <see cref="TryGetFilterDecision"/> -- the predicate is not re-invoked --
    /// and surfaces explicitly the next time it is retrieved from an
    /// authoritative context: the quiet-period wait loop or
    /// <see cref="CreateResult"/>.
    /// </summary>
    private bool TryGetFilterDecisionForVersionTracking(Activity activity)
    {
        try
        {
            return TryGetFilterDecision(activity, out _);
        }
        catch (A365ValidationExecutionException)
        {
            return false;
        }
    }

    /// <summary>
    /// Builds an immutable snapshot of <paramref name="activity"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="A365SpanSnapshot.Attributes"/> mirrors what
    /// <c>ExportFormatter</c> actually serializes into OTLP: the activity's
    /// <see cref="Activity.TagObjects"/> only, with the same duplicate-key
    /// behavior (the last tag written for a key wins).
    /// <see cref="Activity.Baggage"/> is deliberately not merged in, because
    /// baggage is never serialized into OTLP span attributes -- a payload
    /// attribute supplied only through baggage would not reach the Agent 365
    /// service, so treating it as present would let a span certify while its
    /// exported payload was incomplete.
    /// </para>
    /// <para>
    /// The two values the exporter resolves <em>before</em> serialization are
    /// captured separately, exactly as <c>Agent365ExporterCore</c> resolves
    /// them through <see cref="ActivityExtensions.GetAttributeOrBaggage"/>:
    /// the operation name used to classify the span as GenAI telemetry, and
    /// the tenant/agent identity used to route the export request. Those may
    /// legitimately come from <see cref="Activity.Baggage"/>, so the rules
    /// that model export routing see the value the exporter would see.
    /// </para>
    /// </remarks>
    private static A365SpanSnapshot CreateSnapshot(Activity activity)
    {
        var operationName =
            activity.GetAttributeOrBaggage(OpenTelemetryConstants.GenAiOperationNameKey) ??
            string.Empty;

        var attributes = new Dictionary<string, object?>(StringComparer.Ordinal);

        // Activity.AddTag permits duplicate keys; ExportFormatter.MapAttributes
        // assigns each tag into a dictionary in order, so the last write wins.
        foreach (var tag in activity.TagObjects)
        {
            attributes[tag.Key] = tag.Value;
        }

        var routingTenantId =
            activity.GetAttributeOrBaggage(OpenTelemetryConstants.TenantIdKey);
        var routingAgentId =
            activity.GetAttributeOrBaggage(OpenTelemetryConstants.GenAiAgentIdKey) ??
            activity.GetAttributeOrBaggage(OpenTelemetryConstants.AgentPlatformIdKey);

        return new A365SpanSnapshot(
            activity.TraceId.ToHexString().ToLowerInvariant(),
            activity.SpanId.ToHexString().ToLowerInvariant(),
            activity.DisplayName,
            activity.Source.Name,
            operationName,
            attributes,
            routingTenantId,
            routingAgentId);
    }

    private void OnStarted(Activity activity)
    {
        // Advisory fast path only; the authoritative check is under the gate.
        if (closed)
        {
            return;
        }

        // Evaluate (and cache) the span-filter decision BEFORE acquiring the
        // gate: it may invoke customer-supplied predicate code, which must
        // never run while a lock is held. Doing it here also guarantees that
        // every activity published to `active` already carries a decided (or
        // at least started) filter decision, so the gated closure re-check can
        // consult the cache without ever evaluating a predicate itself.
        //
        // Only a span that is both recognized by name AND passes the
        // configured span filter should reset the quiet-period window. A
        // span that is name-eligible but filtered out is out of scope for
        // this validation session, so its churn must never extend the wait
        // nor produce a false completion timeout.
        var bumpsVersion = IsEligible(activity) && TryGetFilterDecisionForVersionTracking(activity);

        OnBeforeCallbackGateForTests?.Invoke(activity);

        lock (gate)
        {
            if (closed)
            {
                // The closure boundary was defined before this start could be
                // applied, so the span starts outside the evaluation window:
                // it must not join the active set, must not invalidate a
                // quiet period that has already been accepted, and must not
                // appear in the result.
                return;
            }

            active[activity] = 0;

            if (bumpsVersion)
            {
                Interlocked.Increment(ref eligibleChangeVersion);
            }
        }
    }

    private void OnStopped(Activity activity)
    {
        // Advisory fast path only; the authoritative check is under the gate.
        if (closed)
        {
            return;
        }

        var isEligible = IsEligible(activity);

        // As in OnStarted, the filter decision is computed before the gate is
        // taken, and only a span that also passes the configured span filter
        // bumps the quiet-period change version -- see the remarks on
        // IsEligibleForWait and TryGetFilterDecisionForVersionTracking.
        var bumpsVersion = isEligible && TryGetFilterDecisionForVersionTracking(activity);

        OnBeforeCallbackGateForTests?.Invoke(activity);

        lock (gate)
        {
            if (closed)
            {
                // The closure boundary was defined before this stop could be
                // applied. The state observed at that boundary is
                // authoritative, so the activity keeps whatever
                // classification it had there -- notably, a span that was
                // active at the deadline stays a per-span timeout even though
                // it completed immediately afterwards.
                return;
            }

            // The whole transition is atomic with respect to the closure
            // boundary, so an eligible stopping activity can never be
            // observed in neither collection -- nor, as it once could, in
            // both at once (which produced a misleading completed-and-timed-out
            // duplicate). The enqueue-before-remove ordering is retained so
            // that lock-free diagnostics still see it continuously.
            if (isEligible)
            {
                completed.Enqueue(activity);
            }

            if (bumpsVersion)
            {
                Interlocked.Increment(ref eligibleChangeVersion);
            }

            OnStoppedTransitionHookForTests?.Invoke(activity);

            active.TryRemove(activity, out _);
        }
    }

    /// <summary>
    /// Builds the result from the immutable state captured at the closure
    /// boundary. Because the boundary is atomic, no reconciliation between
    /// concurrently-mutating collections is needed (or possible): the
    /// completed set and the active-at-boundary set are disjoint by
    /// construction.
    /// </summary>
    /// <param name="state">The recorded closure state.</param>
    /// <returns>The captured result.</returns>
    private A365CaptureResult CreateResult(ClosureState state)
    {
        var filtered = new List<A365SpanSnapshot>();
        foreach (var activity in state.Completed)
        {
            // Only eligible activities are ever enqueued (see OnStopped), so
            // no need to re-check IsEligible here.
            if (TryGetFilterDecision(activity, out var snapshot))
            {
                filtered.Add(snapshot ?? CreateSnapshot(activity));
            }
        }

        var timedOutSpans = new List<A365SpanSnapshot>();
        if (state.TimedOut)
        {
            foreach (var activity in state.ActiveAtBoundary)
            {
                if (!IsEligible(activity))
                {
                    continue;
                }

                if (TryGetFilterDecision(activity, out var snapshot))
                {
                    timedOutSpans.Add(snapshot ?? CreateSnapshot(activity));
                }
            }
        }

        return new A365CaptureResult(filtered, timedOutSpans, state.TimedOut);
    }

    /// <summary>
    /// The immutable observation state captured, under the closure gate, at
    /// the instant the session stopped listening. It is what every
    /// <see cref="CompleteAsync"/> caller reports, which is what makes closure
    /// idempotent and per-span classification stable.
    /// </summary>
    private sealed class ClosureState
    {
        internal ClosureState(
            IReadOnlyList<Activity> completed,
            IReadOnlyList<Activity> activeAtBoundary,
            bool timedOut)
        {
            Completed = completed;
            ActiveAtBoundary = activeAtBoundary;
            TimedOut = timedOut;
        }

        /// <summary>
        /// Gets the eligible activities that had stopped before the boundary.
        /// </summary>
        internal IReadOnlyList<Activity> Completed { get; }

        /// <summary>
        /// Gets the activities that were still active at the boundary.
        /// </summary>
        internal IReadOnlyList<Activity> ActiveAtBoundary { get; }

        /// <summary>
        /// Gets a value indicating whether the boundary is the completion deadline.
        /// </summary>
        internal bool TimedOut { get; }
    }

    /// <summary>
    /// The cached, at-most-once outcome of evaluating the configured span
    /// filter for one activity: either the resulting decision, or the
    /// (wrapped) exception the predicate threw. Caching the failure -- not
    /// just the success/exclusion result -- is what lets the predicate be
    /// invoked exactly once per activity even when it throws, since a
    /// listener callback (which must not propagate the failure; see
    /// <see cref="TryGetFilterDecisionForVersionTracking"/>) may observe the
    /// activity before an authoritative caller (<see cref="CreateResult"/> or
    /// the quiet-period wait loop) does.
    /// </summary>
    private readonly struct FilterDecision
    {
        private FilterDecision(bool passesFilter, Exception? error)
        {
            PassesFilter = passesFilter;
            Error = error;
        }

        internal bool PassesFilter { get; }

        internal Exception? Error { get; }

        internal static FilterDecision Success(bool passesFilter) => new(passesFilter, null);

        internal static FilterDecision Failure(Exception error) => new(false, error);
    }
}

/// <summary>
/// Filtered snapshots produced by a completed <see cref="A365ActivityCaptureSession"/>.
/// </summary>
internal sealed class A365CaptureResult
{
    internal A365CaptureResult(
        IReadOnlyList<A365SpanSnapshot> spans,
        IReadOnlyList<A365SpanSnapshot> timedOutSpans,
        bool timedOut)
    {
        Spans = spans;
        TimedOutSpans = timedOutSpans;
        TimedOut = timedOut;
    }

    /// <summary>
    /// Gets the spans that completed and passed the configured span filter.
    /// </summary>
    internal IReadOnlyList<A365SpanSnapshot> Spans { get; }

    /// <summary>
    /// Gets the eligible spans that were still active when the completion
    /// timeout elapsed.
    /// </summary>
    internal IReadOnlyList<A365SpanSnapshot> TimedOutSpans { get; }

    /// <summary>
    /// Gets a value indicating whether the completion deadline was reached
    /// without observing a quiet period. This can be <see langword="true"/>
    /// even when <see cref="TimedOutSpans"/> is empty, e.g. when continuous
    /// eligible activity churn kept resetting the quiet-period window but no
    /// activity happened to be active at the exact instant the deadline was
    /// reached.
    /// </summary>
    internal bool TimedOut { get; }
}
