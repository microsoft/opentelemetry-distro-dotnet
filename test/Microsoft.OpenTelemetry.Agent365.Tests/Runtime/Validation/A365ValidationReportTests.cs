using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Microsoft.Agents.A365.Observability.Runtime.Validation;
using static Microsoft.Agents.A365.Observability.Runtime.Validation.A365ValidationFindingStatus;
using static Microsoft.Agents.A365.Observability.Runtime.Validation.A365ValidationRuleIds;

namespace Microsoft.OpenTelemetry.Agent365.Tests.Runtime.Validation;

[TestClass]
public sealed class A365ValidationReportTests
{
    [TestMethod]
    public void EnsureValid_InvalidReport_ThrowsActionableException()
    {
        var report = CreateReport(
            new[]
            {
                CreateSpanResult(
                "00000000000000000000000000000001",
                "0000000000000001",
                "execute_tool weather",
                new Dictionary<string, object?>(),
                CreateFinding(
                    ToolNameRequired,
                    A365ValidationSeverity.Error,
                    Active,
                    "execute_tool",
                    "gen_ai.tool.name",
                    "Missing gen_ai.tool.name",
                    "Set ToolCallDetails.ToolName.")),
            });

        Action act = report.EnsureValid;

        var exception = act.Should().Throw<A365ValidationException>().Which;
        exception.Report.Should().BeSameAs(report);
        exception.Message.Should().Contain("A365 instrumentation validation failed");
        exception.Message.Should().Contain("[A365-TOOL-001]");
        exception.Message.Should().Contain("execute_tool");
        exception.Message.Should().Contain("Fix: Set ToolCallDetails.ToolName.");
    }

    [TestMethod]
    public void SuppressedAndWarningFindings_DoNotInvalidateReport()
    {
        var report = CreateReport(
            new[]
            {
                CreateSpanResult(
                "00000000000000000000000000000001",
                "0000000000000001",
                "invoke_agent weather",
                new Dictionary<string, object?>(),
                CreateFinding(
                    InvokeUserIdRequired,
                    A365ValidationSeverity.Error,
                    Suppressed,
                    "invoke_agent",
                    "user.id",
                    "Missing user.id",
                    "Set CallerDetails.UserDetails.UserId.",
                    "Anonymous entry point")),
            },
            sessionFindings: new[]
            {
                CreateFinding(
                    UnusedSuppression,
                    A365ValidationSeverity.Warning,
                    Active,
                    null,
                    null,
                    "Suppression A365-INVOKE-001 did not match any finding.",
                    "Remove the stale suppression or correct its targeting."),
            });

        report.IsValid.Should().BeTrue();
        report.ErrorCount.Should().Be(0);
        report.WarningCount.Should().Be(1);
        report.SuppressedFindingCount.Should().Be(1);
        report.Invoking(r => r.EnsureValid()).Should().NotThrow();
        report.ToString().Should().Contain("SUPPRESSED:");
    }

    [TestMethod]
    public void ToString_ContainsGettingStartedGuideExcerpt()
    {
        var report = CreateReport(
            new[]
            {
                CreateSpanResult(
                "00000000000000000000000000000001",
                "0000000000000001",
                "execute_tool weather",
                new Dictionary<string, object?>(),
                CreateFinding(
                    ToolNameRequired,
                    A365ValidationSeverity.Error,
                    Active,
                    "execute_tool",
                    "gen_ai.tool.name",
                    "Missing gen_ai.tool.name",
                    "Set ToolCallDetails.ToolName.")),
                CreateSpanResult(
                "00000000000000000000000000000002",
                "0000000000000002",
                "invoke_agent weather",
                new Dictionary<string, object?>(),
                CreateFinding(
                    InvokeUserIdRequired,
                    A365ValidationSeverity.Error,
                    Suppressed,
                    "invoke_agent",
                    "user.id",
                    "Missing user.id",
                    "Set CallerDetails.UserDetails.UserId.",
                    "This entry point intentionally supports anonymous users.")),
            });

        var text = report.ToString();

        text.Should().Contain("A365 instrumentation validation failed: 1 error, 0 warnings, 1 suppressed finding");
        text.Should().Contain("[A365-TOOL-001] Missing gen_ai.tool.name");
        text.Should().Contain("Fix: Set ToolCallDetails.ToolName.");
        text.Should().Contain("SUPPRESSED: This entry point intentionally supports anonymous users.");
    }

    [TestMethod]
    public void ToString_IsDeterministic_AndHidesAttributeValues()
    {
        var report = CreateReport(
            new[]
            {
                CreateSpanResult(
                "00000000000000000000000000000002",
                "0000000000000002",
                "second span",
                new Dictionary<string, object?>
                {
                    ["gen_ai.tool.name"] = "secret-value",
                },
                CreateFinding(
                    UnusedSuppression,
                    A365ValidationSeverity.Warning,
                    Active,
                    null,
                    null,
                    "Suppression A365-TOOL-001 did not match any finding.",
                    "Remove the stale suppression or correct its targeting.")),
                CreateSpanResult(
                "00000000000000000000000000000001",
                "0000000000000001",
                "first span",
                new Dictionary<string, object?>
                {
                    ["gen_ai.tool.name"] = "top-secret",
                },
                CreateFinding(
                    ToolNameRequired,
                    A365ValidationSeverity.Error,
                    Active,
                    "execute_tool",
                    "gen_ai.tool.name",
                    "Missing gen_ai.tool.name",
                    "Set ToolCallDetails.ToolName.")),
            });

        var text = report.ToString();

        text.Should().Contain("A365 instrumentation validation failed: 1 error, 1 warning");
        text.Should().Contain("first span");
        text.Should().Contain("second span");
        text.IndexOf("first span", StringComparison.Ordinal).Should()
            .BeLessThan(text.IndexOf("second span", StringComparison.Ordinal));
        text.Should().NotContain("secret-value");
        text.Should().NotContain("top-secret");
    }

    [TestMethod]
    public void ToString_IncludesSessionFindingsAndSingularCounts()
    {
        var report = CreateReport(
            new[]
            {
                CreateSpanResult(
                "00000000000000000000000000000001",
                "0000000000000001",
                "execute_tool weather",
                new Dictionary<string, object?>(),
                CreateFinding(
                    ToolNameRequired,
                    A365ValidationSeverity.Error,
                    Suppressed,
                    "execute_tool",
                    "gen_ai.tool.name",
                    "Missing gen_ai.tool.name",
                    "Set ToolCallDetails.ToolName.",
                    "Targeted suppression")),
            },
            sessionFindings: new[]
            {
                CreateFinding(
                    UnusedSuppression,
                    A365ValidationSeverity.Warning,
                    Active,
                    null,
                    null,
                    "Suppression A365-TOOL-001 did not match any finding.",
                    "Remove the stale suppression or correct its targeting."),
            });

        var text = report.ToString();

        text.Should().Contain("A365 instrumentation validation passed: 0 errors, 1 warning, 1 suppressed finding");
        text.Should().Contain("Session findings:");
        text.Should().Contain("[A365-SESSION-003] Suppression A365-TOOL-001 did not match any finding.");
        text.Should().Contain("SUPPRESSED: Targeted suppression");
    }

    private static A365ValidationReport CreateReport(
        IEnumerable<A365SpanValidationResult> spans = null!,
        IEnumerable<A365ValidationFinding> sessionFindings = null!)
    {
        return new A365ValidationReport(
            spans ?? Array.Empty<A365SpanValidationResult>(),
            sessionFindings ?? Array.Empty<A365ValidationFinding>());
    }

    private static A365SpanValidationResult CreateSpanResult(
        string traceId,
        string spanId,
        string displayName,
        IDictionary<string, object?> attributes,
        params A365ValidationFinding[] findings)
    {
        var span = new A365SpanSnapshot(
            traceId,
            spanId,
            displayName,
            "Test.Source",
            findings.FirstOrDefault()?.OperationName ?? "execute_tool",
            attributes);

        return new A365SpanValidationResult(span, findings);
    }

    private static A365ValidationFinding CreateFinding(
        string ruleId,
        A365ValidationSeverity severity,
        A365ValidationFindingStatus status,
        string? operationName,
        string? attributeName,
        string message,
        string remediation,
        string? suppressionReason = null)
    {
        return new A365ValidationFinding(
            ruleId,
            severity,
            status,
            operationName,
            attributeName,
            message,
            remediation,
            suppressionReason,
            "00000000000000000000000000000001",
            "0000000000000001");
    }
}
