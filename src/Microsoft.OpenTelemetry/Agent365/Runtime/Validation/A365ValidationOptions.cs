// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;

namespace Microsoft.Agents.A365.Observability.Runtime.Validation;

/// <summary>
/// Options for configuring A365 validation.
/// </summary>
public sealed class A365ValidationOptions
{
    private readonly List<A365ValidationSuppression> suppressions = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="A365ValidationOptions"/> class.
    /// </summary>
    public A365ValidationOptions()
    {
    }

    /// <summary>
    /// Gets or sets the validation profile.
    /// </summary>
    public A365ValidationProfile Profile { get; set; } =
        A365ValidationProfile.Certification;

    /// <summary>
    /// Gets or sets the maximum time to wait for span completion. Defaults to
    /// 10 seconds.
    /// </summary>
    /// <remarks>
    /// A validation session only settles after a 250-millisecond quiet period
    /// during which no recognized span starts or stops, so this value must be
    /// at least 250 milliseconds. A shorter deadline could never be satisfied
    /// and is rejected with <see cref="ArgumentOutOfRangeException"/> by
    /// <see cref="A365InstrumentationValidator.EvaluateAsync"/> before the
    /// validated action runs.
    /// </remarks>
    public TimeSpan SpanCompletionTimeout { get; set; } =
        TimeSpan.FromSeconds(10);

    /// <summary>
    /// Gets or sets an optional predicate used to exclude unrelated
    /// telemetry (e.g. spans from other libraries or components sharing the
    /// process) from validation. A span that is otherwise recognized but for
    /// which this predicate returns <see langword="false"/> is excluded from
    /// the validation report and does not extend the wait for span
    /// completion, nor is it reported as a completion timeout.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This predicate is the only supported way to narrow the set of
    /// validated spans. Capture is deliberately not constrained by
    /// <c>TracerProvider</c> source registration: which sources a pipeline
    /// listens to is external configuration the validator cannot reliably
    /// introspect, so every recognized A365 GenAI span in the process is
    /// validated, including spans from custom <see cref="System.Diagnostics.ActivitySource"/>
    /// instances.
    /// </para>
    /// <para>
    /// The predicate may be evaluated while the span is still in flight
    /// (started but not yet stopped) — including to decide, before the span
    /// completes, whether it should keep the capture session waiting. It
    /// must therefore depend only on metadata that is stable at span start:
    /// <see cref="A365SpanSnapshot.TraceId"/>, <see cref="A365SpanSnapshot.SpanId"/>,
    /// <see cref="A365SpanSnapshot.DisplayName"/>, <see cref="A365SpanSnapshot.SourceName"/>,
    /// <see cref="A365SpanSnapshot.OperationName"/>, and attributes known to
    /// be set at span start. Do not depend on attributes or status that are
    /// only set when the span ends (e.g. response payloads, error status),
    /// since the predicate's decision is cached the first time the span
    /// becomes eligible and is never re-evaluated against later attribute
    /// changes. Note that <see cref="A365SpanSnapshot.Attributes"/> contains
    /// the span's activity tags only — the attributes the A365 exporter
    /// serializes — so a predicate cannot match an attribute carried solely
    /// in <see cref="System.Diagnostics.Activity"/> baggage.
    /// <see cref="A365SpanSnapshot.OperationName"/> is the exception because
    /// the exporter resolves it from either a tag or baggage.
    /// </para>
    /// <para>
    /// The predicate may be invoked from <see cref="System.Diagnostics.ActivityListener"/>
    /// callbacks and background threads rather than the thread that started
    /// the validated action; it must be thread-safe and must not assume it
    /// runs on any particular thread. If it throws, the validation session
    /// fails with <see cref="A365ValidationExecutionException"/> wrapping the
    /// original exception rather than returning a partial report.
    /// </para>
    /// </remarks>
    public Func<A365SpanSnapshot, bool>? SpanFilter { get; set; }

    /// <summary>
    /// Gets the configured suppressions.
    /// </summary>
    internal IReadOnlyList<A365ValidationSuppression> Suppressions =>
        suppressions;

    /// <summary>
    /// Suppresses a rule for all spans.
    /// </summary>
    /// <param name="ruleId">The rule identifier.</param>
    /// <param name="reason">The suppression reason.</param>
    public void Suppress(string ruleId, string reason)
    {
        suppressions.Add(A365ValidationSuppression.Create(
            ruleId,
            null,
            null,
            reason));
    }

    /// <summary>
    /// Suppresses a rule for a specific operation.
    /// </summary>
    /// <param name="ruleId">The rule identifier.</param>
    /// <param name="operationName">The operation name.</param>
    /// <param name="reason">The suppression reason.</param>
    public void Suppress(
        string ruleId,
        string operationName,
        string reason)
    {
        suppressions.Add(A365ValidationSuppression.Create(
            ruleId,
            RequireOperationName(operationName),
            null,
            reason));
    }

    /// <summary>
    /// Suppresses a rule for a specific operation and predicate.
    /// </summary>
    /// <param name="ruleId">The rule identifier.</param>
    /// <param name="operationName">The operation name.</param>
    /// <param name="predicate">The suppression predicate.</param>
    /// <param name="reason">The suppression reason.</param>
    public void Suppress(
        string ruleId,
        string operationName,
        Func<A365SpanSnapshot, bool> predicate,
        string reason)
    {
        if (predicate == null)
        {
            throw new ArgumentNullException(nameof(predicate));
        }

        suppressions.Add(A365ValidationSuppression.Create(
            ruleId,
            RequireOperationName(operationName),
            predicate,
            reason));
    }

    private static string RequireOperationName(string operationName)
    {
        if (string.IsNullOrWhiteSpace(operationName))
        {
            throw new ArgumentException(
                "Operation name must not be empty.",
                nameof(operationName));
        }

        return operationName;
    }
}
