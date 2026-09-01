// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Agents.A365.Observability.Runtime.Tracing.Scopes;

namespace Microsoft.Agents.A365.Observability.Runtime.Validation;

/// <summary>
/// Temporarily attaches a process-wide <see cref="ActivityListener"/> to
/// capture recognized A365 GenAI spans and waits for a quiet period before
/// producing immutable snapshots.
/// </summary>
internal sealed class A365ActivityCaptureSession : IDisposable
{
    private static readonly TimeSpan QuietPeriod = TimeSpan.FromMilliseconds(250);

    private readonly ConcurrentDictionary<Activity, byte> active = new();
    private readonly ConcurrentQueue<Activity> completed = new();
    private readonly ConcurrentDictionary<Activity, Lazy<FilterDecision>> filterDecisions = new();
    private readonly Func<A365SpanSnapshot, bool>? spanFilter;
    private readonly ActivityListener listener;
    private long eligibleChangeVersion;
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
    /// synchronously inside <see cref="OnStopped"/> after a stopping activity
    /// has been made durably observable (queued to the completed set when
    /// eligible) but before it is removed from the active set. Production
    /// code never sets this; it exists solely so regression tests can
    /// deterministically observe the transition point instead of relying on
    /// wall-clock timing.
    /// </summary>
    internal Action<Activity>? OnStoppedTransitionHookForTests { get; set; }

    /// <summary>
    /// Waits for a 250-millisecond quiet period, bounded by <paramref name="timeout"/>,
    /// during which no new eligible activity starts or stops. Returns filtered
    /// snapshots for completed eligible spans and snapshots for any span that
    /// was still active and eligible when the timeout elapsed.
    /// </summary>
    /// <param name="timeout">The maximum time to wait for span completion.</param>
    /// <param name="cancellationToken">A token used to cancel the wait.</param>
    /// <returns>The captured result.</returns>
    internal async Task<A365CaptureResult> CompleteAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var timedOut = true;
        var quietPeriodObserved = false;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

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

            var isQuiet = version == Interlocked.Read(ref eligibleChangeVersion) &&
                !active.Keys.Any(IsEligibleForWait);

            if (isQuiet && isFullQuietPeriodDelay)
            {
                quietPeriodObserved = true;
                timedOut = false;
                break;
            }

            // A residual delay shorter than the full 250ms quiet period
            // cannot, by itself, prove that a genuine quiet period occurred:
            // observing no change for a few milliseconds is much weaker
            // evidence than observing no change for the full window. Only a
            // delay of the full QuietPeriod length can establish that
            // guarantee, so once the remaining budget forces a shorter delay
            // this iteration can never declare success on its own -- unless a
            // full quiet period was already separately confirmed above (in
            // which case the loop has already broken out).
            if (!isFullQuietPeriodDelay && !quietPeriodObserved)
            {
                break;
            }
        }

        return CreateResult(timedOut);
    }

    /// <summary>
    /// Test-only helper: reports whether <paramref name="activity"/> is
    /// currently observable through either the active set or the completed
    /// queue. Used to assert that an eligible stopping activity is never
    /// visible in neither collection at the same time.
    /// </summary>
    internal bool IsObservableForTests(Activity activity)
    {
        return active.ContainsKey(activity) || completed.Contains(activity);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        listener.Dispose();
    }

    private static bool IsEligible(Activity activity)
    {
        var operationName =
            activity.GetTagItem(OpenTelemetryConstants.GenAiOperationNameKey) as string;

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
                        return FilterDecision.Failure(new InvalidOperationException(
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
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static A365SpanSnapshot CreateSnapshot(Activity activity)
    {
        var operationName =
            activity.GetTagItem(OpenTelemetryConstants.GenAiOperationNameKey) as string ??
            string.Empty;

        var attributes = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var tag in activity.TagObjects)
        {
            attributes[tag.Key] = tag.Value;
        }

        return new A365SpanSnapshot(
            activity.TraceId.ToHexString().ToLowerInvariant(),
            activity.SpanId.ToHexString().ToLowerInvariant(),
            activity.DisplayName,
            activity.Source.Name,
            operationName,
            attributes);
    }

    private void OnStarted(Activity activity)
    {
        active[activity] = 0;

        // Only a span that is both recognized by name AND passes the
        // configured span filter should reset the quiet-period window. A
        // span that is name-eligible but filtered out is out of scope for
        // this validation session, so its churn must never extend the wait
        // nor produce a false completion timeout.
        if (IsEligible(activity) && TryGetFilterDecisionForVersionTracking(activity))
        {
            Interlocked.Increment(ref eligibleChangeVersion);
        }
    }

    private void OnStopped(Activity activity)
    {
        // Ordering matters: an eligible stopping activity must remain visible
        // to CompleteAsync's completion logic (via `active` or `completed`) at
        // every instant. Queue it as completed (and publish the version bump,
        // when applicable) BEFORE removing it from `active`, so there is
        // never a window where it exists in neither collection and could be
        // silently dropped by a concurrently-running completion check.
        var isEligible = IsEligible(activity);

        if (isEligible)
        {
            completed.Enqueue(activity);
        }

        // As in OnStarted, only a span that also passes the configured span
        // filter should bump the quiet-period change version -- see the
        // remarks on IsEligibleForWait and TryGetFilterDecisionForVersionTracking.
        if (isEligible && TryGetFilterDecisionForVersionTracking(activity))
        {
            Interlocked.Increment(ref eligibleChangeVersion);
        }

        OnStoppedTransitionHookForTests?.Invoke(activity);

        active.TryRemove(activity, out _);
    }

    private A365CaptureResult CreateResult(bool timedOut)
    {
        // Snapshot the active set BEFORE draining the completed queue. This
        // captures activities that were genuinely active at the deadline, so
        // one that stops in the tiny window between this snapshot and the
        // drain below is still correctly eligible for timed-out
        // classification -- and, symmetrically, so it can be excluded from
        // TimedOutSpans if that drain shows it actually completed. Re-reading
        // `active.Keys` only *after* draining (the prior approach) could
        // double-classify such an activity as both completed (in Spans) and
        // timed out (in TimedOutSpans), since OnStopped makes a stopping
        // activity briefly observable in both `active` and `completed` at
        // once by design.
        var activeCandidates = active.Keys.ToList();

        var filtered = new List<A365SpanSnapshot>();
        var completedActivities = new HashSet<Activity>();
        while (completed.TryDequeue(out var activity))
        {
            completedActivities.Add(activity);

            // Only eligible activities are ever enqueued (see OnStopped), so
            // no need to re-check IsEligible here.
            if (TryGetFilterDecision(activity, out var snapshot))
            {
                filtered.Add(snapshot ?? CreateSnapshot(activity));
            }
        }

        var timedOutSpans = new List<A365SpanSnapshot>();
        if (timedOut)
        {
            foreach (var activity in activeCandidates)
            {
                if (completedActivities.Contains(activity) || !IsEligible(activity))
                {
                    continue;
                }

                if (TryGetFilterDecision(activity, out var snapshot))
                {
                    timedOutSpans.Add(snapshot ?? CreateSnapshot(activity));
                }
            }
        }

        return new A365CaptureResult(filtered, timedOutSpans, timedOut);
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
