// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Microsoft.Agents.A365.Observability.Runtime.Validation;

internal static class A365ValidationReportFormatter
{
    internal static string Format(A365ValidationReport report)
    {
        if (report == null)
        {
            throw new ArgumentNullException(nameof(report));
        }

        var builder = new StringBuilder();
        var headline = report.IsValid ? "passed" : "failed";
        builder.Append("A365 instrumentation validation ");
        builder.Append(headline);
        builder.Append(": ");
        builder.Append(FormatCount(report.ErrorCount, "error"));
        builder.Append(", ");
        builder.Append(FormatCount(report.WarningCount, "warning"));
        builder.Append(", ");
        builder.Append(FormatCount(report.SuppressedFindingCount, "suppressed finding"));

        AppendSection(builder, report.SessionFindings, "Session findings");

        foreach (var spanResult in report.Spans
            .OrderBy(span => span.Span.TraceId, StringComparer.Ordinal)
            .ThenBy(span => span.Span.SpanId, StringComparer.Ordinal))
        {
            builder.AppendLine();
            builder.AppendLine();
            builder.Append(spanResult.Span.DisplayName);
            builder.Append(" [trace=");
            builder.Append(spanResult.Span.TraceId);
            builder.Append(", span=");
            builder.Append(spanResult.Span.SpanId);
            builder.AppendLine("]");

            foreach (var finding in OrderFindings(spanResult.Findings))
            {
                AppendFinding(builder, finding);
            }
        }

        return builder.ToString();
    }

    private static void AppendSection(
        StringBuilder builder,
        IReadOnlyList<A365ValidationFinding> findings,
        string title)
    {
        if (findings.Count == 0)
        {
            return;
        }

        builder.AppendLine();
        builder.AppendLine();
        builder.AppendLine(title + ":");

        foreach (var finding in OrderFindings(findings))
        {
            AppendFinding(builder, finding);
        }
    }

    private static void AppendFinding(
        StringBuilder builder,
        A365ValidationFinding finding)
    {
        builder.Append("  [");
        builder.Append(finding.RuleId);
        builder.Append("] ");
        builder.AppendLine(finding.Message);
        builder.Append("  Fix: ");
        builder.AppendLine(finding.Remediation);

        if (finding.Status == A365ValidationFindingStatus.Suppressed &&
            !string.IsNullOrWhiteSpace(finding.SuppressionReason))
        {
            builder.Append("  SUPPRESSED: ");
            builder.AppendLine(finding.SuppressionReason);
        }
    }

    private static IEnumerable<A365ValidationFinding> OrderFindings(
        IEnumerable<A365ValidationFinding> findings)
    {
        return findings
            .OrderBy(finding => finding.Status)
            .ThenByDescending(finding => finding.Severity)
            .ThenBy(finding => finding.RuleId, StringComparer.Ordinal);
    }

    private static string FormatCount(int count, string noun)
    {
        return count == 1
            ? $"1 {noun}"
            : $"{count} {noun}s";
    }
}
