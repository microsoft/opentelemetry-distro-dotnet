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
                span => ValidateRequiredString(span, OpenTelemetryConstants.TenantIdKey),
                "Set AgentDetails.TenantId or provide microsoft.tenant.id through A365 baggage."),
            CreateRule(
                A365ValidationRuleIds.AgentIdentityRequired,
                operationName: null,
                attributeName: null,
                suppressible: false,
                ValidateAgentIdentity,
                "Set AgentDetails.AgentId or AgentDetails.AgentPlatformId, or provide gen_ai.agent.id or microsoft.a365.agent.platform.id through A365 baggage."),
            CreateRule(
                A365ValidationRuleIds.AgentNameRequired,
                operationName: null,
                OpenTelemetryConstants.GenAiAgentNameKey,
                suppressible: true,
                span => ValidateRequiredString(span, OpenTelemetryConstants.GenAiAgentNameKey),
                "Set AgentDetails.AgentName or provide gen_ai.agent.name through A365 baggage."),
            CreateRule(
                A365ValidationRuleIds.AgentDescriptionRequired,
                operationName: null,
                OpenTelemetryConstants.GenAiAgentDescriptionKey,
                suppressible: true,
                span => ValidateRequiredString(span, OpenTelemetryConstants.GenAiAgentDescriptionKey),
                "Set AgentDetails.AgentDescription or provide gen_ai.agent.description through A365 baggage."),
            CreateRule(
                A365ValidationRuleIds.AgentUserIdRequired,
                operationName: null,
                OpenTelemetryConstants.AgentAUIDKey,
                suppressible: true,
                span => ValidateRequiredString(span, OpenTelemetryConstants.AgentAUIDKey),
                "Set AgentDetails.AgenticUserId or provide microsoft.agent.user.id through A365 baggage."),
            CreateRule(
                A365ValidationRuleIds.AgentUserEmailRequired,
                operationName: null,
                OpenTelemetryConstants.AgentEmailKey,
                suppressible: true,
                span => ValidateRequiredString(span, OpenTelemetryConstants.AgentEmailKey),
                "Set AgentDetails.AgenticUserEmail or provide microsoft.agent.user.email through A365 baggage."),
            CreateRule(
                A365ValidationRuleIds.AgentBlueprintIdRequired,
                operationName: null,
                OpenTelemetryConstants.AgentBlueprintIdKey,
                suppressible: true,
                span => ValidateRequiredString(span, OpenTelemetryConstants.AgentBlueprintIdKey),
                "Set AgentDetails.AgentBlueprintId or provide microsoft.a365.agent.blueprint.id through A365 baggage."),
            CreateRule(
                A365ValidationRuleIds.InvokeUserIdRequired,
                OpenTelemetryConstants.InvokeAgentOperationName,
                OpenTelemetryConstants.UserIdKey,
                suppressible: true,
                span => ValidateRequiredString(span, OpenTelemetryConstants.UserIdKey),
                "Set CallerDetails.UserDetails.UserId when starting InvokeAgentScope."),
            CreateRule(
                A365ValidationRuleIds.InvokeUserNameRequired,
                OpenTelemetryConstants.InvokeAgentOperationName,
                OpenTelemetryConstants.UserNameKey,
                suppressible: true,
                span => ValidateRequiredString(span, OpenTelemetryConstants.UserNameKey),
                "Set CallerDetails.UserDetails.UserName when starting InvokeAgentScope."),
            CreateRule(
                A365ValidationRuleIds.InvokeUserEmailRequired,
                OpenTelemetryConstants.InvokeAgentOperationName,
                OpenTelemetryConstants.UserEmailKey,
                suppressible: true,
                span => ValidateRequiredString(span, OpenTelemetryConstants.UserEmailKey),
                "Set CallerDetails.UserDetails.UserEmail when starting InvokeAgentScope."),
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
            operationName,
            attributeName,
            suppressible,
            validate,
            remediation);
    }

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

    private static string? ValidateAgentIdentity(A365SpanSnapshot span)
    {
        var agentIdValidation = ValidateRequiredString(
            span,
            OpenTelemetryConstants.GenAiAgentIdKey);
        if (agentIdValidation == null)
        {
            return null;
        }

        var agentPlatformValidation = ValidateRequiredString(
            span,
            OpenTelemetryConstants.AgentPlatformIdKey);
        if (agentPlatformValidation == null)
        {
            return null;
        }

        return agentIdValidation.StartsWith("Invalid ", StringComparison.Ordinal) ?
            agentIdValidation :
            agentPlatformValidation.StartsWith("Invalid ", StringComparison.Ordinal) ?
                agentPlatformValidation :
                "Missing agent identity: set gen_ai.agent.id or microsoft.a365.agent.platform.id";
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
