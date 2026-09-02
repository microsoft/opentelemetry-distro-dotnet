// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Microsoft.Agents.A365.Observability.Runtime.Tracing.Scopes;

namespace Microsoft.Agents.A365.Observability.Runtime.Validation;

internal static class A365CertificationRuleCatalog
{
    private static readonly HashSet<string> ValidGuardrailDecisions = new(
        new[]
        {
            "allow",
            "audit",
            "deny",
            "modify",
            "warn",
        },
        StringComparer.OrdinalIgnoreCase);

    private static readonly Dictionary<string, A365ValidationRule> RulesById =
        new(StringComparer.Ordinal);

    static A365CertificationRuleCatalog()
    {
        var rules = new List<A365ValidationRule>
        {
            CreateRule(
                A365ValidationRuleIds.TenantIdRequired,
                operationName: null,
                OpenTelemetryConstants.TenantIdKey,
                suppressible: false,
                ValidateRoutingTenantId,
                "Set AgentDetails.TenantId or provide microsoft.tenant.id through A365 baggage."),
            CreateRule(
                A365ValidationRuleIds.AgentIdentityRequired,
                operationName: null,
                attributeName: null,
                suppressible: false,
                ValidateRoutingAgentIdentity,
                "Set AgentDetails.AgentId or AgentDetails.AgentPlatformId, or provide gen_ai.agent.id or microsoft.a365.agent.platform.id through A365 baggage."),
            CreateStorePublishingRule(
                A365ValidationRuleIds.AgentIdRequired,
                OpenTelemetryConstants.GenAiAgentIdKey,
                StorePublishingOperations,
                "Set AgentDetails.AgentId to the calling application's Entra appId.",
                ValidateRequiredAgentId),
            CreateStorePublishingRule(
                A365ValidationRuleIds.AgentNameRequired,
                OpenTelemetryConstants.GenAiAgentNameKey,
                StorePublishingOperations,
                "Set AgentDetails.AgentName or provide gen_ai.agent.name through A365 baggage."),
            CreateStorePublishingRule(
                A365ValidationRuleIds.AgentUserIdRequired,
                OpenTelemetryConstants.AgentAUIDKey,
                StorePublishingOperations,
                "Set AgentDetails.AgenticUserId or provide microsoft.agent.user.id through A365 baggage."),
            CreateStorePublishingRule(
                A365ValidationRuleIds.AgentUserEmailRequired,
                OpenTelemetryConstants.AgentEmailKey,
                StorePublishingOperations,
                "Set AgentDetails.AgenticUserEmail or provide microsoft.agent.user.email through A365 baggage."),
            CreateStorePublishingRule(
                A365ValidationRuleIds.AgentBlueprintIdRequired,
                OpenTelemetryConstants.AgentBlueprintIdKey,
                StorePublishingOperations,
                "Set AgentDetails.AgentBlueprintId or provide microsoft.a365.agent.blueprint.id through A365 baggage."),
            CreateStorePublishingRule(
                A365ValidationRuleIds.ChannelNameRequired,
                OpenTelemetryConstants.ChannelNameKey,
                StorePublishingOperations,
                "Set Request.Channel.Name or microsoft.channel.name through A365 baggage."),
            CreateStorePublishingRule(
                A365ValidationRuleIds.ConversationIdRequired,
                OpenTelemetryConstants.GenAiConversationIdKey,
                StorePublishingOperations,
                "Set Request.ConversationId or gen_ai.conversation.id through A365 baggage."),
            CreateStorePublishingRule(
                A365ValidationRuleIds.ClientAddressRequired,
                OpenTelemetryConstants.CallerClientIpKey,
                StorePublishingOperations,
                "Set the caller client address through UserDetails.UserClientIP or A365 baggage."),
            CreateStorePublishingRule(
                A365ValidationRuleIds.UserIdRequired,
                OpenTelemetryConstants.UserIdKey,
                StorePublishingOperations,
                "Set CallerDetails.UserDetails.UserId or user.id through A365 baggage."),
            CreateStorePublishingRule(
                A365ValidationRuleIds.UserEmailRequired,
                OpenTelemetryConstants.UserEmailKey,
                StorePublishingOperations,
                "Set CallerDetails.UserDetails.UserEmail or user.email through A365 baggage."),
            CreateStorePublishingRule(
                A365ValidationRuleIds.ServerAddressRequired,
                OpenTelemetryConstants.ServerAddressKey,
                NetworkOperations,
                "Set the scope endpoint or server.address through A365 baggage."),
            CreateStorePublishingRule(
                A365ValidationRuleIds.InputMessagesRequired,
                OpenTelemetryConstants.GenAiInputMessagesKey,
                InputMessageOperations,
                "Record input messages on invoke_agent and chat spans."),
            CreateStorePublishingRule(
                A365ValidationRuleIds.OutputMessagesRequired,
                OpenTelemetryConstants.GenAiOutputMessagesKey,
                OutputMessageOperations,
                "Record output messages on invoke_agent, chat, and output_messages spans."),
            CreateRule(
                A365ValidationRuleIds.InferenceModelRequired,
                OpenTelemetryConstants.ChatOperationName,
                OpenTelemetryConstants.GenAiRequestModelKey,
                suppressible: true,
                span => ValidateRequiredString(span, OpenTelemetryConstants.GenAiRequestModelKey),
                "Set InferenceCallDetails.Model for chat/inference spans."),
            CreateRule(
                A365ValidationRuleIds.InferenceProviderRequired,
                OpenTelemetryConstants.ChatOperationName,
                OpenTelemetryConstants.GenAiProviderNameKey,
                suppressible: true,
                span => ValidateRequiredString(span, OpenTelemetryConstants.GenAiProviderNameKey),
                "Set InferenceCallDetails.ProviderName for chat/inference spans."),
            CreateRule(
                A365ValidationRuleIds.ToolNameRequired,
                OpenTelemetryConstants.ExecuteToolOperationName,
                OpenTelemetryConstants.GenAiToolNameKey,
                suppressible: true,
                span => ValidateRequiredString(span, OpenTelemetryConstants.GenAiToolNameKey),
                "Set ToolCallDetails.ToolName for execute_tool spans."),
            CreateRule(
                A365ValidationRuleIds.ToolTypeRequired,
                OpenTelemetryConstants.ExecuteToolOperationName,
                OpenTelemetryConstants.GenAiToolTypeKey,
                suppressible: true,
                span => ValidateRequiredString(span, OpenTelemetryConstants.GenAiToolTypeKey),
                "Set ToolCallDetails.ToolType for execute_tool spans."),
            CreateRule(
                A365ValidationRuleIds.ToolCallIdRequired,
                OpenTelemetryConstants.ExecuteToolOperationName,
                OpenTelemetryConstants.GenAiToolCallIdKey,
                suppressible: true,
                span => ValidateRequiredString(span, OpenTelemetryConstants.GenAiToolCallIdKey),
                "Set ToolCallDetails.ToolCallId for execute_tool spans."),
            CreateRule(
                A365ValidationRuleIds.ToolCallArgumentsRequired,
                OpenTelemetryConstants.ExecuteToolOperationName,
                OpenTelemetryConstants.GenAiToolArgumentsKey,
                suppressible: true,
                span => ValidateRequiredString(span, OpenTelemetryConstants.GenAiToolArgumentsKey),
                "Set ToolCallDetails arguments for execute_tool spans."),
            CreateRule(
                A365ValidationRuleIds.ToolCallResultRequired,
                OpenTelemetryConstants.ExecuteToolOperationName,
                OpenTelemetryConstants.GenAiToolCallResultKey,
                suppressible: true,
                span => ValidateRequiredString(span, OpenTelemetryConstants.GenAiToolCallResultKey),
                "Call ExecuteToolScope.RecordResponse with the tool result."),
            CreateRule(
                A365ValidationRuleIds.GuardrailDecisionRequired,
                OpenTelemetryConstants.ApplyGuardrailOperationName,
                OpenTelemetryConstants.GenAiSecurityDecisionTypeKey,
                suppressible: true,
                ValidateGuardrailDecision,
                "Set GuardrailDetails.DecisionType when starting ApplyGuardrailScope."),
            CreateRule(
                A365ValidationRuleIds.GuardrailTargetRequired,
                OpenTelemetryConstants.ApplyGuardrailOperationName,
                OpenTelemetryConstants.GenAiSecurityTargetTypeKey,
                suppressible: true,
                span => ValidateRequiredString(span, OpenTelemetryConstants.GenAiSecurityTargetTypeKey),
                "Set GuardrailDetails.TargetType when starting ApplyGuardrailScope."),
        };

        Rules = new ReadOnlyCollection<A365ValidationRule>(rules);

        foreach (var rule in rules)
        {
            RulesById.Add(rule.Id, rule);
        }
    }

    internal static IReadOnlyList<A365ValidationRule> Rules { get; }

    private static readonly string[] StorePublishingOperations =
    {
        OpenTelemetryConstants.InvokeAgentOperationName,
        OpenTelemetryConstants.ExecuteToolOperationName,
        OpenTelemetryConstants.ChatOperationName,
        OpenTelemetryConstants.OutputMessagesOperationName,
    };

    private static readonly string[] NetworkOperations =
    {
        OpenTelemetryConstants.InvokeAgentOperationName,
    };

    private static readonly string[] InputMessageOperations =
    {
        OpenTelemetryConstants.InvokeAgentOperationName,
        OpenTelemetryConstants.ChatOperationName,
    };

    private static readonly string[] OutputMessageOperations =
    {
        OpenTelemetryConstants.InvokeAgentOperationName,
        OpenTelemetryConstants.ChatOperationName,
        OpenTelemetryConstants.OutputMessagesOperationName,
    };

    internal static bool TryGetRule(string ruleId, out A365ValidationRule rule)
    {
        var found = RulesById.TryGetValue(ruleId, out var candidate);
        rule = candidate!;
        return found;
    }

    private static A365ValidationRule CreateRule(
        string id,
        string? operationName,
        string? attributeName,
        bool suppressible,
        Func<A365SpanSnapshot, string?> validate,
        string remediation)
    {
        return new A365ValidationRule(
            id,
            operationName == null ? null : new[] { operationName },
            attributeName,
            suppressible,
            validate,
            remediation);
    }

    private static A365ValidationRule CreateStorePublishingRule(
        string id,
        string attributeName,
        IReadOnlyCollection<string> operationNames,
        string remediation,
        Func<A365SpanSnapshot, string?>? validate = null)
    {
        return new A365ValidationRule(
            id,
            operationNames,
            attributeName,
            suppressible: true,
            validate ?? (span => ValidateRequiredString(span, attributeName)),
            remediation);
    }

    /// <summary>
    /// Validates a required payload attribute. Payload attributes are read
    /// from <see cref="A365SpanSnapshot.Attributes"/>, which contains the
    /// activity's tags only: baggage is never serialized into OTLP span
    /// attributes, so a value carried only in baggage would be missing from
    /// the exported payload and must not satisfy the rule.
    /// </summary>
    private static string? ValidateRequiredString(
        A365SpanSnapshot span,
        string key)
    {
        if (!span.Attributes.TryGetValue(key, out var value) ||
            value == null ||
            value is string empty && string.IsNullOrWhiteSpace(empty))
        {
            return $"Missing {key}";
        }

        if (value is not string)
        {
            return $"Invalid {key}: expected a non-empty string but found " +
                value.GetType().Name;
        }

        return null;
    }

    /// <summary>
    /// Validates the tenant identifier the A365 exporter would route this span
    /// with. Unlike payload attributes, routing identity is resolved by the
    /// exporter through <c>GetAttributeOrBaggage</c> before serialization, so
    /// a tenant supplied only through <see cref="System.Diagnostics.Activity"/>
    /// baggage still routes the export and therefore satisfies this rule.
    /// </summary>
    private static string? ValidateRoutingTenantId(A365SpanSnapshot span)
    {
        return string.IsNullOrWhiteSpace(span.RoutingTenantId) ?
            $"Missing {OpenTelemetryConstants.TenantIdKey}" :
            null;
    }

    /// <summary>
    /// Validates the agent identity the A365 exporter would route this span
    /// with: <c>gen_ai.agent.id</c> from tag or baggage, falling back to the
    /// agent platform identifier from tag or baggage, exactly as
    /// <c>Agent365ExporterCore</c> resolves it when partitioning a batch.
    /// </summary>
    private static string? ValidateRoutingAgentIdentity(A365SpanSnapshot span)
    {
        return string.IsNullOrWhiteSpace(span.RoutingAgentId) ?
            "Missing agent identity: set gen_ai.agent.id or microsoft.a365.agent.platform.id" :
            null;
    }

    private static string? ValidateRequiredAgentId(A365SpanSnapshot span)
    {
        // Avoid reporting the same root cause twice when the exporter cannot
        // route the span at all. A platform identity can route legacy spans,
        // but it does not replace the store-required gen_ai.agent.id payload.
        return string.IsNullOrWhiteSpace(span.RoutingAgentId) ?
            null :
            ValidateRequiredString(span, OpenTelemetryConstants.GenAiAgentIdKey);
    }

    private static string? ValidateGuardrailDecision(A365SpanSnapshot span)
    {
        var requiredValidation = ValidateRequiredString(
            span,
            OpenTelemetryConstants.GenAiSecurityDecisionTypeKey);
        if (requiredValidation != null)
        {
            return requiredValidation;
        }

        var value = (string)span.Attributes[OpenTelemetryConstants.GenAiSecurityDecisionTypeKey]!;
        return ValidGuardrailDecisions.Contains(value) ?
            null :
            "Invalid microsoft.security.decision.type: expected allow, audit, deny, modify, or warn";
    }
}
