// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.Agents.A365.Observability.Runtime.Validation;

/// <summary>
/// Stable public certification rule identifiers for A365 validation.
/// </summary>
public static class A365ValidationRuleIds
{
    /// <summary>Requires tenant identity to be present.</summary>
    public const string TenantIdRequired = "A365-COMMON-001";

    /// <summary>Requires agent identity to be present.</summary>
    public const string AgentIdentityRequired = "A365-COMMON-002";

    /// <summary>Requires the agent name to be present.</summary>
    public const string AgentNameRequired = "A365-COMMON-003";

    /// <summary>Requires the agent description to be present.</summary>
    public const string AgentDescriptionRequired = "A365-COMMON-004";

    /// <summary>Requires the agent user id to be present.</summary>
    public const string AgentUserIdRequired = "A365-COMMON-005";

    /// <summary>Requires the agent user email to be present.</summary>
    public const string AgentUserEmailRequired = "A365-COMMON-006";

    /// <summary>Requires the agent blueprint id to be present.</summary>
    public const string AgentBlueprintIdRequired = "A365-COMMON-007";

    /// <summary>Requires the invoke user id to be present.</summary>
    public const string InvokeUserIdRequired = "A365-INVOKE-001";

    /// <summary>Requires the invoke user name to be present.</summary>
    public const string InvokeUserNameRequired = "A365-INVOKE-002";

    /// <summary>Requires the invoke user email to be present.</summary>
    public const string InvokeUserEmailRequired = "A365-INVOKE-003";

    /// <summary>Requires the inference model to be present.</summary>
    public const string InferenceModelRequired = "A365-INFERENCE-001";

    /// <summary>Requires the inference provider to be present.</summary>
    public const string InferenceProviderRequired = "A365-INFERENCE-002";

    /// <summary>Requires the tool name to be present.</summary>
    public const string ToolNameRequired = "A365-TOOL-001";

    /// <summary>Requires a guardrail decision to be present.</summary>
    public const string GuardrailDecisionRequired = "A365-GUARDRAIL-001";

    /// <summary>Requires a guardrail target to be present.</summary>
    public const string GuardrailTargetRequired = "A365-GUARDRAIL-002";

    /// <summary>Indicates that no spans were captured.</summary>
    public const string NoSpansCaptured = "A365-SESSION-001";

    /// <summary>Indicates that span completion timed out.</summary>
    public const string SpanCompletionTimeout = "A365-SESSION-002";

    /// <summary>Indicates that a suppression was not used.</summary>
    public const string UnusedSuppression = "A365-SESSION-003";
}
