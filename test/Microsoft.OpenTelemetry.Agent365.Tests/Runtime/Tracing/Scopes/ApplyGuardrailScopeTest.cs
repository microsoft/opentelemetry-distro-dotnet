// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.Agents.A365.Observability.Tests.Tracing.Scopes;

using System;
using System.Linq;
using FluentAssertions;
using Microsoft.Agents.A365.Observability.Runtime.Tracing.Scopes;
using Microsoft.Agents.A365.Observability.Runtime.Tracing.Contracts;

[TestClass]
public sealed class ApplyGuardrailScopeTest : ActivityTest
{
    [TestMethod]
    public void Start_SetsRequiredAttributes()
    {
        var details = new GuardrailDetails(
            targetType: GuardrailTargetType.LlmInput,
            decisionType: GuardrailDecisionType.Deny);

        var activity = ListenForActivity(() =>
        {
            using var scope = ApplyGuardrailScope.Start(details, Util.GetAgentDetails());
        });

        activity.ShouldHaveTag(OpenTelemetryConstants.GenAiOperationNameKey, OpenTelemetryConstants.ApplyGuardrailOperationName);
        activity.ShouldHaveTag(OpenTelemetryConstants.GenAiSecurityDecisionTypeKey, "deny");
        activity.ShouldHaveTag(OpenTelemetryConstants.GenAiSecurityTargetTypeKey, GuardrailTargetType.LlmInput);
    }

    [TestMethod]
    public void Start_SetsGuardianAttributes_WhenProvided()
    {
        var details = new GuardrailDetails(
            targetType: GuardrailTargetType.LlmOutput,
            decisionType: GuardrailDecisionType.Allow,
            guardianName: "PII Filter",
            guardianId: "guard_abc123",
            guardianProviderName: "azure.ai.content_safety",
            guardianVersion: "2.1.0");

        var activity = ListenForActivity(() =>
        {
            using var scope = ApplyGuardrailScope.Start(details, Util.GetAgentDetails());
        });

        activity.ShouldHaveTag(OpenTelemetryConstants.GenAiGuardianNameKey, "PII Filter");
        activity.ShouldHaveTag(OpenTelemetryConstants.GenAiGuardianIdKey, "guard_abc123");
        activity.ShouldHaveTag(OpenTelemetryConstants.GenAiGuardianProviderNameKey, "azure.ai.content_safety");
        activity.ShouldHaveTag(OpenTelemetryConstants.GenAiGuardianVersionKey, "2.1.0");
    }

    [TestMethod]
    public void Start_SetsPolicyAttributes_WhenProvided()
    {
        var details = new GuardrailDetails(
            targetType: GuardrailTargetType.ToolCall,
            decisionType: GuardrailDecisionType.Modify,
            policyId: "policy_pii_v2",
            policyName: "PII Protection Policy",
            policyVersion: "1.0");

        var activity = ListenForActivity(() =>
        {
            using var scope = ApplyGuardrailScope.Start(details, Util.GetAgentDetails());
        });

        activity.ShouldHaveTag(OpenTelemetryConstants.GenAiSecurityPolicyIdKey, "policy_pii_v2");
        activity.ShouldHaveTag(OpenTelemetryConstants.GenAiSecurityPolicyNameKey, "PII Protection Policy");
        activity.ShouldHaveTag(OpenTelemetryConstants.GenAiSecurityPolicyVersionKey, "1.0");
    }

    [TestMethod]
    public void Start_SetsContentAttributes_WhenProvided()
    {
        var details = new GuardrailDetails(
            targetType: GuardrailTargetType.LlmInput,
            decisionType: GuardrailDecisionType.Allow,
            contentInputHash: "sha256:abc123",
            contentModified: true,
            externalEventId: "ext-001");

        var activity = ListenForActivity(() =>
        {
            using var scope = ApplyGuardrailScope.Start(details, Util.GetAgentDetails());
        });

        activity.ShouldHaveTag(OpenTelemetryConstants.GenAiSecurityContentInputHashKey, "sha256:abc123");
        activity.TagObjects.First(t => t.Key == OpenTelemetryConstants.GenAiSecurityContentModifiedKey).Value.Should().Be(true);
        activity.ShouldHaveTag(OpenTelemetryConstants.GenAiSecurityExternalEventIdKey, "ext-001");
    }

    [TestMethod]
    public void Start_BuildsActivityName_WithGuardianName()
    {
        var details = new GuardrailDetails(
            targetType: GuardrailTargetType.LlmInput,
            decisionType: GuardrailDecisionType.Deny,
            guardianName: "Azure Content Safety");

        var activity = ListenForActivity(() =>
        {
            using var scope = ApplyGuardrailScope.Start(details, Util.GetAgentDetails());
        });

        activity.DisplayName.Should().Be("apply_guardrail Azure Content Safety llm_input");
    }

    [TestMethod]
    public void Start_BuildsActivityName_WithoutGuardianName()
    {
        var details = new GuardrailDetails(
            targetType: GuardrailTargetType.ToolCall,
            decisionType: GuardrailDecisionType.Allow);

        var activity = ListenForActivity(() =>
        {
            using var scope = ApplyGuardrailScope.Start(details, Util.GetAgentDetails());
        });

        activity.DisplayName.Should().Be("apply_guardrail tool_call");
    }

    [TestMethod]
    public void RecordDecision_UpdatesDecisionType()
    {
        var details = new GuardrailDetails(
            targetType: GuardrailTargetType.LlmInput,
            decisionType: GuardrailDecisionType.Allow);

        var activity = ListenForActivity(() =>
        {
            using var scope = ApplyGuardrailScope.Start(details, Util.GetAgentDetails());
            scope.RecordDecision(GuardrailDecisionType.Deny, "Content blocked");
        });

        activity.ShouldHaveTag(OpenTelemetryConstants.GenAiSecurityDecisionTypeKey, "deny");
        activity.ShouldHaveTag(OpenTelemetryConstants.GenAiSecurityDecisionReasonKey, "Content blocked");
    }

    [TestMethod]
    public void RecordContentOutput_SetsOutputValue()
    {
        var details = new GuardrailDetails(
            targetType: GuardrailTargetType.LlmOutput,
            decisionType: GuardrailDecisionType.Modify);

        var activity = ListenForActivity(() =>
        {
            using var scope = ApplyGuardrailScope.Start(details, Util.GetAgentDetails());
            scope.RecordContentOutput("sanitized content");
        });

        activity.ShouldHaveTag(OpenTelemetryConstants.GenAiSecurityContentOutputValueKey, "sanitized content");
    }

    [TestMethod]
    public void RecordFinding_AddsEventWithAttributes()
    {
        var details = new GuardrailDetails(
            targetType: GuardrailTargetType.LlmInput,
            decisionType: GuardrailDecisionType.Deny);

        var finding = new GuardrailFinding(
            riskCategory: "hate_speech",
            riskSeverity: GuardrailRiskSeverity.High,
            policyDecisionType: "deny",
            policyId: "policy-abc",
            riskScore: 0.95,
            riskMetadata: new[] { "{\"category\":\"hate\",\"confidence\":0.95}" });

        var activity = ListenForActivity(() =>
        {
            using var scope = ApplyGuardrailScope.Start(details, Util.GetAgentDetails());
            scope.RecordFinding(finding);
        });

        activity.Events.Should().HaveCount(1);
        var findingEvent = activity.Events.First();
        findingEvent.Name.Should().Be(OpenTelemetryConstants.GenAiSecurityFindingEventName);

        var tags = findingEvent.Tags.ToDictionary(t => t.Key, t => t.Value);
        tags[OpenTelemetryConstants.GenAiSecurityRiskCategoryKey].Should().Be("hate_speech");
        tags[OpenTelemetryConstants.GenAiSecurityRiskSeverityKey].Should().Be(GuardrailRiskSeverity.High);
        tags[OpenTelemetryConstants.GenAiSecurityPolicyDecisionTypeKey].Should().Be("deny");
        tags[OpenTelemetryConstants.GenAiSecurityPolicyIdKey].Should().Be("policy-abc");
        tags[OpenTelemetryConstants.GenAiSecurityRiskScoreKey].Should().Be(0.95);
    }

    [TestMethod]
    public void RecordFinding_MultipleFindingsRecorded()
    {
        var details = new GuardrailDetails(
            targetType: GuardrailTargetType.LlmInput,
            decisionType: GuardrailDecisionType.Deny);

        var activity = ListenForActivity(() =>
        {
            using var scope = ApplyGuardrailScope.Start(details, Util.GetAgentDetails());
            scope.RecordFinding(new GuardrailFinding("hate_speech", GuardrailRiskSeverity.High));
            scope.RecordFinding(new GuardrailFinding("pii", GuardrailRiskSeverity.Medium));
        });

        activity.Events.Should().HaveCount(2);
    }

    [TestMethod]
    public void RecordFinding_ThrowsOnNull()
    {
        var details = new GuardrailDetails(
            targetType: GuardrailTargetType.LlmInput,
            decisionType: GuardrailDecisionType.Allow);

        Action act = () =>
        {
            using var scope = ApplyGuardrailScope.Start(details, Util.GetAgentDetails());
            scope.RecordFinding(null!);
        };

        act.Should().Throw<ArgumentNullException>();
    }

    [TestMethod]
    public void Start_SetsRequestContext_WhenProvided()
    {
        var details = new GuardrailDetails(
            targetType: GuardrailTargetType.LlmInput,
            decisionType: GuardrailDecisionType.Allow);

        var request = new Request(
            content: "test input",
            conversationId: "conv-123",
            channel: new Channel(name: "msteams", link: "https://test.link"));

        var activity = ListenForActivity(() =>
        {
            using var scope = ApplyGuardrailScope.Start(details, Util.GetAgentDetails(), request: request);
        });

        activity.ShouldHaveTag(OpenTelemetryConstants.GenAiSecurityContentInputValueKey, "test input");
        activity.ShouldHaveTag(OpenTelemetryConstants.GenAiConversationIdKey, "conv-123");
        activity.ShouldHaveTag(OpenTelemetryConstants.ChannelNameKey, "msteams");
        activity.ShouldHaveTag(OpenTelemetryConstants.ChannelLinkKey, "https://test.link");
    }
}
