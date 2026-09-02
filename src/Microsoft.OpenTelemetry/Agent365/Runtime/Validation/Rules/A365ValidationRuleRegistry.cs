// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.Agents.A365.Observability.Runtime.Validation;

internal static class A365ValidationRuleRegistry
{
    internal static bool TryGetRule(
        string ruleId,
        out bool suppressible)
    {
        if (A365CertificationRuleCatalog.TryGetRule(ruleId, out var rule))
        {
            suppressible = rule.Suppressible;
            return true;
        }

        if (string.Equals(ruleId, A365ValidationRuleIds.NoSpansCaptured, System.StringComparison.Ordinal) ||
            string.Equals(ruleId, A365ValidationRuleIds.SpanCompletionTimeout, System.StringComparison.Ordinal) ||
            string.Equals(ruleId, A365ValidationRuleIds.UnusedSuppression, System.StringComparison.Ordinal))
        {
            suppressible = false;
            return true;
        }

        suppressible = default;
        return false;
    }
}
