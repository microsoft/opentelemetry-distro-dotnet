// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Microsoft.Agents.A365.Observability.Runtime.Validation;

/// <summary>
/// Validation results for a single span.
/// </summary>
public sealed class A365SpanValidationResult
{
    /// <summary>
    /// Initializes a new instance of the <see cref="A365SpanValidationResult"/> class.
    /// </summary>
    /// <param name="span">The span snapshot.</param>
    /// <param name="findings">The findings for the span.</param>
    internal A365SpanValidationResult(
        A365SpanSnapshot span,
        IEnumerable<A365ValidationFinding> findings)
    {
        Span = span;
        Findings = new ReadOnlyCollection<A365ValidationFinding>(
            new List<A365ValidationFinding>(findings));
    }

    /// <summary>
    /// Gets the span snapshot.
    /// </summary>
    public A365SpanSnapshot Span { get; }

    /// <summary>
    /// Gets the findings for the span.
    /// </summary>
    public ReadOnlyCollection<A365ValidationFinding> Findings { get; }
}
