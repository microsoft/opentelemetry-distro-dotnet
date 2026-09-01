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
    /// Gets or sets the maximum time to wait for span completion.
    /// </summary>
    public TimeSpan SpanCompletionTimeout { get; set; } =
        TimeSpan.FromSeconds(10);

    /// <summary>
    /// Gets or sets the optional span filter.
    /// </summary>
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
