// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;

namespace Microsoft.Agents.A365.Observability.Runtime.Validation;

internal static class A365ValidationEngine
{
    internal static IReadOnlyList<A365SpanValidationResult> Validate(
        IReadOnlyList<A365SpanSnapshot> spans,
        A365ValidationOptions options)
    {
        if (spans == null)
        {
            throw new ArgumentNullException(nameof(spans));
        }

        if (options == null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        ValidateOptions(options);
        var results = new List<A365SpanValidationResult>(spans.Count);

        foreach (var span in spans)
        {
            var findings = new List<A365ValidationFinding>();
            foreach (var rule in A365CertificationRuleCatalog.Rules)
            {
                if (!AppliesToOperation(rule, span))
                {
                    continue;
                }

                var message = rule.Validate(span);
                if (message == null)
                {
                    continue;
                }

                var suppression = FindSuppression(rule, span, options.Suppressions);
                findings.Add(CreateFinding(rule, span, message, suppression));
                if (suppression != null)
                {
                    suppression.WasUsed = true;
                }
            }

            results.Add(new A365SpanValidationResult(span, findings));
        }

        return results.AsReadOnly();
    }

    private static bool AppliesToOperation(
        A365ValidationRule rule,
        A365SpanSnapshot span)
    {
        return rule.OperationName == null ||
            string.Equals(
                rule.OperationName,
                span.OperationName,
                StringComparison.OrdinalIgnoreCase);
    }

    private static A365ValidationFinding CreateFinding(
        A365ValidationRule rule,
        A365SpanSnapshot span,
        string message,
        A365ValidationSuppression? suppression)
    {
        return new A365ValidationFinding(
            rule.Id,
            A365ValidationSeverity.Error,
            suppression == null ?
                A365ValidationFindingStatus.Active :
                A365ValidationFindingStatus.Suppressed,
            span.OperationName,
            rule.AttributeName,
            message,
            rule.Remediation,
            suppression?.Reason,
            span.TraceId,
            span.SpanId);
    }

    private static A365ValidationSuppression? FindSuppression(
        A365ValidationRule rule,
        A365SpanSnapshot span,
        IReadOnlyList<A365ValidationSuppression> suppressions)
    {
        A365ValidationSuppression? operationSuppression = null;
        A365ValidationSuppression? globalSuppression = null;

        foreach (var suppression in suppressions)
        {
            if (!string.Equals(suppression.RuleId, rule.Id, StringComparison.Ordinal))
            {
                continue;
            }

            if (suppression.OperationName != null &&
                !string.Equals(
                    suppression.OperationName,
                    span.OperationName,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (suppression.Predicate != null)
            {
                bool matches;
                try
                {
                    matches = suppression.Predicate(span);
                }
                catch (Exception ex)
                {
                    throw new A365ValidationExecutionException(
                        $"Suppression predicate for rule '{rule.Id}' failed for span '{span.SpanId}'.",
                        ex);
                }

                if (matches)
                {
                    return suppression;
                }

                continue;
            }

            if (suppression.OperationName != null)
            {
                operationSuppression ??= suppression;
            }
            else
            {
                globalSuppression ??= suppression;
            }
        }

        return operationSuppression ?? globalSuppression;
    }

    internal static void ValidateOptions(A365ValidationOptions options)
    {
        if (options.Profile != A365ValidationProfile.Certification)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options.Profile),
                options.Profile,
                "Only the Certification profile is supported.");
        }

        if (options.SpanCompletionTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options.SpanCompletionTimeout),
                options.SpanCompletionTimeout,
                "Span completion timeout must be positive.");
        }

        foreach (var suppression in options.Suppressions)
        {
            suppression.WasUsed = false;

            if (!A365ValidationRuleRegistry.TryGetRule(
                suppression.RuleId,
                out var suppressible))
            {
                throw new ArgumentException(
                    $"Unknown rule id '{suppression.RuleId}'.",
                    nameof(options));
            }

            if (!suppressible)
            {
                throw new ArgumentException(
                    $"Rule id '{suppression.RuleId}' is non-suppressible.",
                    nameof(options));
            }
        }
    }
}
