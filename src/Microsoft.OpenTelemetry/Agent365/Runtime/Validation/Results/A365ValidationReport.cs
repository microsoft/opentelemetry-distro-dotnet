// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Microsoft.Agents.A365.Observability.Runtime.Validation;

/// <summary>
/// Immutable aggregate of captured spans and session findings.
/// </summary>
public sealed class A365ValidationReport
{
    private readonly ReadOnlyCollection<A365SpanValidationResult> spans;
    private readonly ReadOnlyCollection<A365ValidationFinding> sessionFindings;

    internal A365ValidationReport(
        IEnumerable<A365SpanValidationResult> spans,
        IEnumerable<A365ValidationFinding> sessionFindings)
    {
        if (spans == null)
        {
            throw new ArgumentNullException(nameof(spans));
        }

        if (sessionFindings == null)
        {
            throw new ArgumentNullException(nameof(sessionFindings));
        }

        this.spans = new ReadOnlyCollection<A365SpanValidationResult>(
            spans.ToList());
        this.sessionFindings = new ReadOnlyCollection<A365ValidationFinding>(
            sessionFindings.ToList());
    }

    /// <summary>
    /// Gets the captured span validation results.
    /// </summary>
    public IReadOnlyList<A365SpanValidationResult> Spans => spans;

    /// <summary>
    /// Gets the session-level findings.
    /// </summary>
    public IReadOnlyList<A365ValidationFinding> SessionFindings =>
        sessionFindings;

    /// <summary>
    /// Gets the count of active error findings.
    /// </summary>
    public int ErrorCount => AllFindings.Count(f =>
        f.Status == A365ValidationFindingStatus.Active &&
        f.Severity == A365ValidationSeverity.Error);

    /// <summary>
    /// Gets the count of active warning findings.
    /// </summary>
    public int WarningCount => AllFindings.Count(f =>
        f.Status == A365ValidationFindingStatus.Active &&
        f.Severity == A365ValidationSeverity.Warning);

    /// <summary>
    /// Gets the count of suppressed findings.
    /// </summary>
    public int SuppressedFindingCount => AllFindings.Count(f =>
        f.Status == A365ValidationFindingStatus.Suppressed);

    /// <summary>
    /// Gets a value indicating whether the report contains no active errors.
    /// </summary>
    public bool IsValid => ErrorCount == 0;

    /// <summary>
    /// Throws an exception if the report contains active errors.
    /// </summary>
    public void EnsureValid()
    {
        if (!IsValid)
        {
            throw new A365ValidationException(this);
        }
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return A365ValidationReportFormatter.Format(this);
    }

    private IEnumerable<A365ValidationFinding> AllFindings =>
        sessionFindings.Concat(spans.SelectMany(span => span.Findings));
}
