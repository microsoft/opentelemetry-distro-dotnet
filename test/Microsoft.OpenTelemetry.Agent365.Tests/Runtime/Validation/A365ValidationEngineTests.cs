using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Microsoft.Agents.A365.Observability.Runtime.Tracing.Scopes;
using Microsoft.Agents.A365.Observability.Runtime.Validation;

using static Microsoft.Agents.A365.Observability.Runtime.Tracing.Scopes.OpenTelemetryConstants;

namespace Microsoft.OpenTelemetry.Agent365.Tests.Runtime.Validation;

[TestClass]
public sealed class A365ValidationEngineTests
{
    [TestMethod]
    public void Validate_ValidChatSpan_HasNoFindings()
    {
        var span = CreateSpan("chat", new Dictionary<string, object?>
        {
            [TenantIdKey] = "tenant",
            [GenAiAgentIdKey] = "agent",
            [GenAiAgentNameKey] = "Weather agent",
            [GenAiAgentDescriptionKey] = "Answers weather questions",
            [AgentAUIDKey] = "agent-user",
            [AgentEmailKey] = "agent@example.com",
            [AgentBlueprintIdKey] = "blueprint",
            [GenAiRequestModelKey] = "gpt-4.1",
            [GenAiProviderNameKey] = "openai",
        });

        var results = A365ValidationEngine.Validate(
            new[] { span },
            new A365ValidationOptions());

        results.Should().HaveCount(1);
        results.Single().Findings.Should().BeEmpty();
    }

    [TestMethod]
    public void Validate_MissingExporterIdentity_IsNotSuppressible()
    {
        var options = new A365ValidationOptions();
        options.Suppress(
            A365ValidationRuleIds.TenantIdRequired,
            "Local tenant is unavailable");

        Action act = () => A365ValidationEngine.Validate(
            new[] { CreateSpan("chat") },
            options);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*non-suppressible*");
    }

    [DataTestMethod]
    [DataRow(A365ValidationRuleIds.NoSpansCaptured)]
    [DataRow(A365ValidationRuleIds.SpanCompletionTimeout)]
    [DataRow(A365ValidationRuleIds.UnusedSuppression)]
    public void Validate_SessionRuleSuppression_IsRejectedAsNonSuppressible(
        string ruleId)
    {
        var options = new A365ValidationOptions();
        options.Suppress(ruleId, "Session rule suppression");

        Action act = () => A365ValidationEngine.Validate(
            new[] { CreateValidCommonSpan("chat") },
            options);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*non-suppressible*");
    }

    [TestMethod]
    public void Validate_OperationSuppression_LeavesVisibleSuppressedFinding()
    {
        var options = new A365ValidationOptions();
        options.Suppress(
            A365ValidationRuleIds.InvokeUserIdRequired,
            "invoke_agent",
            "Anonymous entry point");

        var result = A365ValidationEngine.Validate(
            new[] { CreateValidCommonSpan("invoke_agent") },
            options).Single();

        result.Findings.Should().ContainSingle(f =>
            f.RuleId == A365ValidationRuleIds.InvokeUserIdRequired &&
            f.Status == A365ValidationFindingStatus.Suppressed &&
            f.SuppressionReason == "Anonymous entry point");
    }

    [TestMethod]
    public void Validate_PredicateSuppression_AppliesOnlyToMatchingSpan()
    {
        var options = new A365ValidationOptions();
        options.Suppress(
            A365ValidationRuleIds.ToolNameRequired,
            "execute_tool",
            span => span.DisplayName.Contains("optional", StringComparison.Ordinal),
            "Synthetic optional tool span");

        var results = A365ValidationEngine.Validate(
            new[]
            {
                CreateValidCommonSpan("execute_tool", "execute_tool optional"),
                CreateValidCommonSpan("execute_tool", "execute_tool required"),
            },
            options);

        results[0].Findings.Should().ContainSingle(f =>
            f.RuleId == A365ValidationRuleIds.ToolNameRequired &&
            f.Status == A365ValidationFindingStatus.Suppressed &&
            f.SuppressionReason == "Synthetic optional tool span");
        results[1].Findings.Should().ContainSingle(f =>
            f.RuleId == A365ValidationRuleIds.ToolNameRequired &&
            f.Status == A365ValidationFindingStatus.Active &&
            f.SuppressionReason == null);
    }

    [TestMethod]
    public void Validate_SuppressionPrecedence_PrefersPredicateOverOperationAndGlobal()
    {
        var options = new A365ValidationOptions();
        options.Suppress(
            A365ValidationRuleIds.ToolNameRequired,
            "Global suppression");
        options.Suppress(
            A365ValidationRuleIds.ToolNameRequired,
            "execute_tool",
            "Operation suppression");
        options.Suppress(
            A365ValidationRuleIds.ToolNameRequired,
            "execute_tool",
            span => span.DisplayName == "preferred",
            "Predicate suppression");

        var result = A365ValidationEngine.Validate(
            new[] { CreateValidCommonSpan("execute_tool", "preferred") },
            options).Single();

        result.Findings.Should().ContainSingle(f =>
            f.RuleId == A365ValidationRuleIds.ToolNameRequired &&
            f.SuppressionReason == "Predicate suppression");
    }

    [TestMethod]
    public void Validate_WhitespaceValues_AreReportedAsMissing()
    {
        var result = A365ValidationEngine.Validate(
            new[]
            {
                CreateSpan("chat", new Dictionary<string, object?>(CreateValidChatAttributes())
                {
                    [GenAiAgentNameKey] = " ",
                }),
            },
            new A365ValidationOptions()).Single();

        result.Findings.Should().ContainSingle(f =>
            f.RuleId == A365ValidationRuleIds.AgentNameRequired &&
            f.Message == $"Missing {GenAiAgentNameKey}");
    }

    [TestMethod]
    public void Validate_NonStringValues_AreReportedAsInvalid()
    {
        var result = A365ValidationEngine.Validate(
            new[]
            {
                CreateSpan("invoke_agent", new Dictionary<string, object?>(CreateValidCommonAttributes())
                {
                    [UserIdKey] = "caller-id",
                    [UserNameKey] = "caller",
                    [UserEmailKey] = 42,
                }),
            },
            new A365ValidationOptions()).Single();

        result.Findings.Should().ContainSingle(f =>
            f.RuleId == A365ValidationRuleIds.InvokeUserEmailRequired &&
            f.Message == $"Invalid {UserEmailKey}: expected a non-empty string but found Int32");
    }

    [TestMethod]
    public void Validate_AgentPlatformId_SatisfiesAgentIdentityRule()
    {
        var attributes = CreateValidChatAttributes();
        attributes.Remove(GenAiAgentIdKey);
        attributes[AgentPlatformIdKey] = "platform-agent";

        var result = A365ValidationEngine.Validate(
            new[] { CreateSpan("CHAT", attributes) },
            new A365ValidationOptions()).Single();

        result.Findings.Should().NotContain(f =>
            f.RuleId == A365ValidationRuleIds.AgentIdentityRequired);
    }

    [TestMethod]
    public void Validate_InferenceRules_ApplyOnlyToChat()
    {
        var result = A365ValidationEngine.Validate(
            new[] { CreateValidCommonSpan("execute_tool") },
            new A365ValidationOptions()).Single();

        result.Findings.Should().NotContain(f =>
            f.RuleId == A365ValidationRuleIds.InferenceModelRequired ||
            f.RuleId == A365ValidationRuleIds.InferenceProviderRequired);
    }

    [TestMethod]
    public void Validate_ToolRules_ApplyOnlyToExecuteTool()
    {
        var result = A365ValidationEngine.Validate(
            new[] { CreateSpan("chat", CreateValidCommonAttributes()) },
            new A365ValidationOptions()).Single();

        result.Findings.Should().NotContain(f =>
            f.RuleId == A365ValidationRuleIds.ToolNameRequired);
    }

    [TestMethod]
    public void Validate_GuardrailRules_ApplyOnlyToApplyGuardrail()
    {
        var result = A365ValidationEngine.Validate(
            new[] { CreateValidCommonSpan("chat") },
            new A365ValidationOptions()).Single();

        result.Findings.Should().NotContain(f =>
            f.RuleId == A365ValidationRuleIds.GuardrailDecisionRequired ||
            f.RuleId == A365ValidationRuleIds.GuardrailTargetRequired);
    }

    [TestMethod]
    public void Validate_GuardrailDecision_RejectsUnexpectedValue()
    {
        var result = A365ValidationEngine.Validate(
            new[]
            {
                CreateSpan("apply_guardrail", new Dictionary<string, object?>(CreateValidCommonAttributes())
                {
                    [GenAiSecurityDecisionTypeKey] = "block",
                    [GenAiSecurityTargetTypeKey] = "prompt",
                }),
            },
            new A365ValidationOptions()).Single();

        result.Findings.Should().ContainSingle(f =>
            f.RuleId == A365ValidationRuleIds.GuardrailDecisionRequired &&
            f.Message == "Invalid microsoft.security.decision.type: expected allow, audit, deny, modify, or warn");
    }

    [TestMethod]
    public void Validate_CallerRules_ApplyOnlyToInvokeAgent()
    {
        var result = A365ValidationEngine.Validate(
            new[] { CreateValidCommonSpan("chat") },
            new A365ValidationOptions()).Single();

        result.Findings.Should().NotContain(f =>
            f.RuleId == A365ValidationRuleIds.InvokeUserIdRequired ||
            f.RuleId == A365ValidationRuleIds.InvokeUserNameRequired ||
            f.RuleId == A365ValidationRuleIds.InvokeUserEmailRequired);
    }

    [TestMethod]
    public void Validate_UnknownRuleIdSuppression_Throws()
    {
        var options = new A365ValidationOptions();
        options.Suppress("A365-UNKNOWN-999", "test");

        Action act = () => A365ValidationEngine.Validate(
            new[] { CreateValidCommonSpan("chat") },
            options);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*Unknown rule id*");
    }

    [TestMethod]
    public void Validate_PredicateExceptions_AreWrapped()
    {
        var options = new A365ValidationOptions();
        options.Suppress(
            A365ValidationRuleIds.ToolNameRequired,
            "execute_tool",
            _ => throw new InvalidOperationException("boom"),
            "Predicate failure");

        Action act = () => A365ValidationEngine.Validate(
            new[] { CreateValidCommonSpan("execute_tool") },
            options);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*A365-TOOL-001*0000000000000001*");
    }

    [TestMethod]
    public void Validate_UnsupportedProfile_Throws()
    {
        var options = new A365ValidationOptions
        {
            Profile = (A365ValidationProfile)999,
        };

        Action act = () => A365ValidationEngine.Validate(
            new[] { CreateValidCommonSpan("chat") },
            options);

        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName("Profile");
    }

    [TestMethod]
    public void Validate_NonPositiveTimeout_Throws()
    {
        var options = new A365ValidationOptions
        {
            SpanCompletionTimeout = TimeSpan.Zero,
        };

        Action act = () => A365ValidationEngine.Validate(
            new[] { CreateValidCommonSpan("chat") },
            options);

        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName("SpanCompletionTimeout");
    }

    private static A365SpanSnapshot CreateSpan(
        string operationName,
        IDictionary<string, object?>? attributes = null,
        string displayName = "test span")
    {
        var values = attributes == null
            ? new Dictionary<string, object?>()
            : new Dictionary<string, object?>(attributes);
        values[GenAiOperationNameKey] = operationName;

        return new A365SpanSnapshot(
            "00000000000000000000000000000001",
            "0000000000000001",
            displayName,
            "Test.Source",
            operationName,
            values);
    }

    private static A365SpanSnapshot CreateValidCommonSpan(
        string operationName,
        string displayName = "test span")
    {
        return CreateSpan(operationName, CreateValidCommonAttributes(), displayName);
    }

    private static Dictionary<string, object?> CreateValidChatAttributes()
    {
        return new Dictionary<string, object?>(CreateValidCommonAttributes())
        {
            [GenAiRequestModelKey] = "gpt-4.1",
            [GenAiProviderNameKey] = "openai",
        };
    }

    private static Dictionary<string, object?> CreateValidCommonAttributes()
    {
        return new Dictionary<string, object?>
        {
            [TenantIdKey] = "tenant",
            [GenAiAgentIdKey] = "agent",
            [GenAiAgentNameKey] = "Weather agent",
            [GenAiAgentDescriptionKey] = "Answers weather questions",
            [AgentAUIDKey] = "agent-user",
            [AgentEmailKey] = "agent@example.com",
            [AgentBlueprintIdKey] = "blueprint",
        };
    }
}
