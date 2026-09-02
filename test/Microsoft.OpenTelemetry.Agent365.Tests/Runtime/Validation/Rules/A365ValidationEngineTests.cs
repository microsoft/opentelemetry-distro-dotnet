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
    [DataTestMethod]
    [DataRow("invoke_agent", "microsoft.channel.name", "A365-COMMON-008")]
    [DataRow("invoke_agent", "gen_ai.conversation.id", "A365-COMMON-009")]
    [DataRow("invoke_agent", "client.address", "A365-COMMON-010")]
    [DataRow("invoke_agent", "user.id", "A365-COMMON-011")]
    [DataRow("invoke_agent", "user.email", "A365-COMMON-012")]
    [DataRow("invoke_agent", "server.address", "A365-COMMON-013")]
    [DataRow("invoke_agent", "gen_ai.input.messages", "A365-CONTENT-001")]
    [DataRow("invoke_agent", "gen_ai.output.messages", "A365-CONTENT-002")]
    [DataRow("execute_tool", "gen_ai.tool.type", "A365-TOOL-002")]
    [DataRow("execute_tool", "gen_ai.tool.call.id", "A365-TOOL-003")]
    [DataRow("execute_tool", "gen_ai.tool.call.arguments", "A365-TOOL-004")]
    [DataRow("execute_tool", "gen_ai.tool.call.result", "A365-TOOL-005")]
    [DataRow("chat", "gen_ai.input.messages", "A365-CONTENT-001")]
    [DataRow("chat", "gen_ai.output.messages", "A365-CONTENT-002")]
    [DataRow("output_messages", "gen_ai.output.messages", "A365-CONTENT-002")]
    public void Validate_StorePublishingRequiredAttribute_IsEnforced(
        string operationName,
        string attributeName,
        string ruleId)
    {
        var attributes = CreateStorePublishingAttributes(operationName);
        attributes.Remove(attributeName);

        var result = A365ValidationEngine.Validate(
            new[] { CreateSpan(operationName, attributes) },
            new A365ValidationOptions()).Single();

        result.Findings.Should().ContainSingle(f =>
            f.RuleId == ruleId &&
            f.AttributeName == attributeName &&
            f.Message == $"Missing {attributeName}");
    }

    [TestMethod]
    public void Validate_StorePublishingOptionalAttributes_AreNotRequired()
    {
        var attributes = CreateStorePublishingAttributes("invoke_agent");
        attributes.Remove(GenAiAgentDescriptionKey);
        attributes.Remove(UserNameKey);

        var result = A365ValidationEngine.Validate(
            new[] { CreateSpan("invoke_agent", attributes) },
            new A365ValidationOptions()).Single();

        result.Findings.Should().NotContain(f =>
            f.AttributeName == GenAiAgentDescriptionKey ||
            f.AttributeName == UserNameKey);
    }

    [DataTestMethod]
    [DataRow("execute_tool")]
    [DataRow("chat")]
    [DataRow("output_messages")]
    public void Validate_ServerRules_ApplyOnlyToInvokeAgent(string operationName)
    {
        var attributes = CreateStorePublishingAttributes(operationName);
        attributes.Remove(ServerAddressKey);
        attributes.Remove(ServerPortKey);

        var result = A365ValidationEngine.Validate(
            new[] { CreateSpan(operationName, attributes) },
            new A365ValidationOptions()).Single();

        result.Findings.Should().NotContain(f =>
            f.RuleId == A365ValidationRuleIds.ServerAddressRequired);
    }

    [TestMethod]
    public void Validate_InvokeAgent_DefaultServerPortMayBeOmitted()
    {
        var attributes = CreateStorePublishingAttributes("invoke_agent");
        attributes.Remove(ServerPortKey);

        var result = A365ValidationEngine.Validate(
            new[] { CreateSpan("invoke_agent", attributes) },
            new A365ValidationOptions()).Single();

        result.Findings.Should().NotContain(f =>
            f.AttributeName == ServerPortKey);
    }

    [TestMethod]
    public void Validate_ValidChatSpan_HasNoFindings()
    {
        var span = CreateSpan("chat", CreateStorePublishingAttributes("chat"));

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
            A365ValidationRuleIds.UserIdRequired,
            "invoke_agent",
            "Anonymous entry point");

        var attributes = CreateStorePublishingAttributes("invoke_agent");
        attributes.Remove(UserIdKey);

        var result = A365ValidationEngine.Validate(
            new[] { CreateSpan("invoke_agent", attributes) },
            options).Single();

        result.Findings.Should().ContainSingle(f =>
            f.RuleId == A365ValidationRuleIds.UserIdRequired &&
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

        var optionalAttributes = CreateStorePublishingAttributes("execute_tool");
        optionalAttributes.Remove(GenAiToolNameKey);
        var requiredAttributes = CreateStorePublishingAttributes("execute_tool");
        requiredAttributes.Remove(GenAiToolNameKey);

        var results = A365ValidationEngine.Validate(
            new[]
            {
                CreateSpan("execute_tool", optionalAttributes, "execute_tool optional"),
                CreateSpan("execute_tool", requiredAttributes, "execute_tool required"),
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

        var attributes = CreateStorePublishingAttributes("execute_tool");
        attributes.Remove(GenAiToolNameKey);

        var result = A365ValidationEngine.Validate(
            new[] { CreateSpan("execute_tool", attributes, "preferred") },
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
            f.RuleId == A365ValidationRuleIds.UserEmailRequired &&
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
    public void Validate_AgentPlatformId_DoesNotReplaceRequiredAgentIdAttribute()
    {
        var attributes = CreateStorePublishingAttributes("chat");
        attributes.Remove(GenAiAgentIdKey);
        attributes[AgentPlatformIdKey] = "platform-agent";

        var result = A365ValidationEngine.Validate(
            new[] { CreateSpan("chat", attributes) },
            new A365ValidationOptions()).Single();

        result.Findings.Should().NotContain(f =>
            f.RuleId == A365ValidationRuleIds.AgentIdentityRequired);
        result.Findings.Should().ContainSingle(f =>
            f.RuleId == A365ValidationRuleIds.AgentIdRequired &&
            f.AttributeName == GenAiAgentIdKey);
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
    public void Validate_UserRules_ApplyToOutputMessages()
    {
        var attributes = CreateStorePublishingAttributes("output_messages");
        attributes.Remove(UserIdKey);
        attributes.Remove(UserEmailKey);
        attributes.Remove(UserNameKey);

        var result = A365ValidationEngine.Validate(
            new[] { CreateSpan("output_messages", attributes) },
            new A365ValidationOptions()).Single();

        result.Findings.Should().Contain(f =>
            f.RuleId == A365ValidationRuleIds.UserIdRequired);
        result.Findings.Should().Contain(f =>
            f.RuleId == A365ValidationRuleIds.UserEmailRequired);
        result.Findings.Should().NotContain(f =>
            f.AttributeName == UserNameKey);
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

        var attributes = CreateStorePublishingAttributes("execute_tool");
        attributes.Remove(GenAiToolNameKey);

        Action act = () => A365ValidationEngine.Validate(
            new[] { CreateSpan("execute_tool", attributes) },
            options);

        act.Should().Throw<A365ValidationExecutionException>()
            .WithMessage("*A365-TOOL-001*0000000000000001*")
            .WithInnerException<InvalidOperationException>()
            .WithMessage("boom");
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

    [TestMethod]
    public void Validate_TimeoutShorterThanQuietPeriod_Throws()
    {
        var options = new A365ValidationOptions
        {
            SpanCompletionTimeout = TimeSpan.FromMilliseconds(249),
        };

        Action act = () => A365ValidationEngine.Validate(
            new[] { CreateValidCommonSpan("chat") },
            options);

        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName("SpanCompletionTimeout")
            .WithMessage("*250ms quiet period*");
    }

    [TestMethod]
    public void Validate_TimeoutEqualToQuietPeriod_IsAccepted()
    {
        var options = new A365ValidationOptions
        {
            SpanCompletionTimeout = TimeSpan.FromMilliseconds(250),
        };

        Action act = () => A365ValidationEngine.Validate(
            new[] { CreateValidCommonSpan("chat") },
            options);

        act.Should().NotThrow();
    }

    [TestMethod]
    public void Validate_RoutingIdentityInAttributes_SatisfiesNonSuppressibleRules()
    {
        // A snapshot built from attributes alone must derive its effective
        // routing identity from those attributes, so tag-supplied identity
        // still satisfies the two export routing rules.
        var result = A365ValidationEngine.Validate(
            new[] { CreateSpan("chat", CreateValidChatAttributes()) },
            new A365ValidationOptions()).Single();

        result.Findings.Should().NotContain(f =>
            f.RuleId == A365ValidationRuleIds.TenantIdRequired ||
            f.RuleId == A365ValidationRuleIds.AgentIdentityRequired);
    }

    [TestMethod]
    public void Validate_MissingRoutingIdentity_ReportsBothNonSuppressibleRules()
    {
        var attributes = CreateValidChatAttributes();
        attributes.Remove(TenantIdKey);
        attributes.Remove(GenAiAgentIdKey);

        var result = A365ValidationEngine.Validate(
            new[] { CreateSpan("chat", attributes) },
            new A365ValidationOptions()).Single();

        result.Findings.Should().ContainSingle(f =>
            f.RuleId == A365ValidationRuleIds.TenantIdRequired &&
            f.Message == $"Missing {TenantIdKey}");
        result.Findings.Should().ContainSingle(f =>
            f.RuleId == A365ValidationRuleIds.AgentIdentityRequired &&
            f.Message == "Missing agent identity: set gen_ai.agent.id or microsoft.a365.agent.platform.id");
    }

    [TestMethod]
    public void Validate_WhitespaceRoutingIdentity_IsReportedAsMissing()
    {
        var attributes = CreateValidChatAttributes();
        attributes[TenantIdKey] = " ";
        attributes[GenAiAgentIdKey] = " ";

        var result = A365ValidationEngine.Validate(
            new[] { CreateSpan("chat", attributes) },
            new A365ValidationOptions()).Single();

        result.Findings.Should().Contain(f =>
            f.RuleId == A365ValidationRuleIds.TenantIdRequired);
        result.Findings.Should().Contain(f =>
            f.RuleId == A365ValidationRuleIds.AgentIdentityRequired);
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
        return CreateSpan(
            operationName,
            CreateStorePublishingAttributes(operationName),
            displayName);
    }

    private static Dictionary<string, object?> CreateValidChatAttributes()
    {
        return CreateStorePublishingAttributes(ChatOperationName);
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

    private static Dictionary<string, object?> CreateStorePublishingAttributes(
        string operationName)
    {
        var attributes = new Dictionary<string, object?>(CreateValidCommonAttributes())
        {
            [ChannelNameKey] = "web",
            [GenAiConversationIdKey] = "conversation",
            [CallerClientIpKey] = "127.0.0.1",
            [UserIdKey] = "user",
            [UserEmailKey] = "user@example.com",
        };

        if (operationName == InvokeAgentOperationName ||
            operationName == ExecuteToolOperationName ||
            operationName == ChatOperationName)
        {
            attributes[ServerAddressKey] = "agent.example.com";
            attributes[ServerPortKey] = "443";
        }

        if (operationName == InvokeAgentOperationName ||
            operationName == ChatOperationName)
        {
            attributes[GenAiInputMessagesKey] = "[]";
        }

        if (operationName == InvokeAgentOperationName ||
            operationName == ChatOperationName ||
            operationName == OutputMessagesOperationName)
        {
            attributes[GenAiOutputMessagesKey] = "[]";
        }

        if (operationName == ChatOperationName)
        {
            attributes[GenAiRequestModelKey] = "gpt-4.1";
            attributes[GenAiProviderNameKey] = "openai";
        }

        if (operationName == ExecuteToolOperationName)
        {
            attributes[GenAiToolNameKey] = "search";
            attributes[GenAiToolTypeKey] = "function";
            attributes[GenAiToolCallIdKey] = "call-1";
            attributes[GenAiToolArgumentsKey] = "{}";
            attributes[GenAiToolCallResultKey] = "{}";
        }

        return attributes;
    }
}
