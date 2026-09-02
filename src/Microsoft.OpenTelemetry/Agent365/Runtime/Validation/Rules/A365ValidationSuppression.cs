// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;

namespace Microsoft.Agents.A365.Observability.Runtime.Validation;

internal sealed class A365ValidationSuppression
{
    private bool wasUsed;

    private A365ValidationSuppression(
        string ruleId,
        string? operationName,
        Func<A365SpanSnapshot, bool>? predicate,
        string reason)
    {
        RuleId = ruleId;
        OperationName = operationName;
        Predicate = predicate;
        Reason = reason;
    }

    internal string RuleId { get; }

    internal string? OperationName { get; }

    internal Func<A365SpanSnapshot, bool>? Predicate { get; }

    internal string Reason { get; }

    internal bool WasUsed
    {
        get => wasUsed;
        set => wasUsed = value;
    }

    internal static A365ValidationSuppression Create(
        string ruleId,
        string? operationName,
        Func<A365SpanSnapshot, bool>? predicate,
        string reason)
    {
        if (string.IsNullOrWhiteSpace(ruleId))
        {
            throw new ArgumentException(
                "Rule id must not be empty.",
                nameof(ruleId));
        }

        if (operationName != null && string.IsNullOrWhiteSpace(operationName))
        {
            throw new ArgumentException(
                "Operation name must not be empty.",
                nameof(operationName));
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException(
                "Reason must not be empty.",
                nameof(reason));
        }

        return new A365ValidationSuppression(
            ruleId,
            operationName,
            predicate,
            reason);
    }
}
