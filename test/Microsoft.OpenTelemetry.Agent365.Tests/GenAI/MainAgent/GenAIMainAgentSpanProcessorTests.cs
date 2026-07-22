// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.OpenTelemetry.Tests.GenAI.MainAgent;

using System.Diagnostics;
using FluentAssertions;
using Microsoft.OpenTelemetry.GenAI.MainAgent;
using global::OpenTelemetry;
using global::OpenTelemetry.Trace;
using static Microsoft.Agents.A365.Observability.Runtime.Tracing.Scopes.OpenTelemetryConstants;

/// <summary>
/// Tests for <see cref="GenAIMainAgentSpanProcessor"/>. Verifies that
/// <c>microsoft.gen_ai.main_agent.*</c> and <c>microsoft.foundry.project.id</c> are
/// propagated from the parent activity onto child activities at OnStart and, when
/// necessary, again at OnEnd (self-promotion for top-level invoke_agent, and
/// fallback re-read from a parent that was enriched after the child started).
/// </summary>
[TestClass]
public sealed class GenAIMainAgentSpanProcessorTests
{
    private const string TestSourceName = "Microsoft.OpenTelemetry.Tests.GenAIMainAgent";
    private const string TestProjectArmId =
        "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/rg/"
        + "providers/Microsoft.CognitiveServices/accounts/acct/projects/proj";

    // ── OnStart ────────────────────────────────────────────────────────────

    [TestMethod]
    public void OnStart_WithNoParent_LeavesActivityUnchanged()
    {
        using var scope = BuildScope();

        using var activity = scope.Source.StartActivity("root");

        activity.Should().NotBeNull();
        activity!.Parent.Should().BeNull();
        activity.GetTagItem(GenAiMainAgentNameKey).Should().BeNull();
        activity.GetTagItem(GenAiFoundryProjectIdKey).Should().BeNull();
    }

    [TestMethod]
    public void OnStart_CopiesMainAgentAttributesFromParent()
    {
        using var scope = BuildScope();

        using var parent = scope.Source.StartActivity("parent");
        parent!.SetTag(GenAiMainAgentNameKey, "main-agent");
        parent.SetTag(GenAiMainAgentIdKey, "main-agent-id");
        parent.SetTag(GenAiMainAgentVersionKey, "v1");
        parent.SetTag(GenAiMainAgentConversationIdKey, "conv-1");

        using var child = scope.Source.StartActivity("child");

        child.Should().NotBeNull();
        child!.Parent.Should().Be(parent);
        child.GetTagItem(GenAiMainAgentNameKey).Should().Be("main-agent");
        child.GetTagItem(GenAiMainAgentIdKey).Should().Be("main-agent-id");
        child.GetTagItem(GenAiMainAgentVersionKey).Should().Be("v1");
        child.GetTagItem(GenAiMainAgentConversationIdKey).Should().Be("conv-1");
    }

    [TestMethod]
    public void OnStart_FallsBackToGenAiAgentAttributes_WhenParentLacksMainAgentTags()
    {
        using var scope = BuildScope();

        using var parent = scope.Source.StartActivity("parent");
        parent!.SetTag(GenAiAgentNameKey, "agent-a");
        parent.SetTag(GenAiAgentIdKey, "agent-a-id");
        parent.SetTag(GenAiAgentVersionKey, "v2");
        parent.SetTag(GenAiConversationIdKey, "conv-2");

        using var child = scope.Source.StartActivity("child");

        child.Should().NotBeNull();
        child!.GetTagItem(GenAiMainAgentNameKey).Should().Be("agent-a");
        child.GetTagItem(GenAiMainAgentIdKey).Should().Be("agent-a-id");
        child.GetTagItem(GenAiMainAgentVersionKey).Should().Be("v2");
        child.GetTagItem(GenAiMainAgentConversationIdKey).Should().Be("conv-2");
    }

    [TestMethod]
    public void OnStart_CopiesProjectIdKeysFromParent()
    {
        using var scope = BuildScope();

        using var parent = scope.Source.StartActivity("parent");
        parent!.SetTag(GenAiFoundryProjectIdKey, TestProjectArmId);
        parent.SetTag(GenAiAzureAiProjectIdKey, "azure-ai-project-id");

        using var child = scope.Source.StartActivity("child");

        child.Should().NotBeNull();
        child!.GetTagItem(GenAiFoundryProjectIdKey).Should().Be(TestProjectArmId);
        child.GetTagItem(GenAiAzureAiProjectIdKey).Should().Be("azure-ai-project-id");
    }

    // ── OnEnd ──────────────────────────────────────────────────────────────

    [TestMethod]
    public void OnEnd_TopLevelInvokeAgent_SelfPromotesGenAiAgentAttributes()
    {
        using var scope = BuildScope();

        using var activity = scope.Source.StartActivity("invoke_agent");
        activity.Should().NotBeNull();
        activity!.SetTag(GenAiOperationNameKey, InvokeAgentOperationName);
        activity.SetTag(GenAiAgentNameKey, "weather-agent");
        activity.SetTag(GenAiAgentIdKey, "weather-agent-id");
        activity.SetTag(GenAiAgentVersionKey, "v3");
        activity.SetTag(GenAiConversationIdKey, "conv-3");

        activity.Stop();

        activity.GetTagItem(GenAiMainAgentNameKey).Should().Be("weather-agent");
        activity.GetTagItem(GenAiMainAgentIdKey).Should().Be("weather-agent-id");
        activity.GetTagItem(GenAiMainAgentVersionKey).Should().Be("v3");
        activity.GetTagItem(GenAiMainAgentConversationIdKey).Should().Be("conv-3");
    }

    [TestMethod]
    public void OnEnd_NonInvokeAgent_DoesNotSelfPromote()
    {
        using var scope = BuildScope();

        using var activity = scope.Source.StartActivity("execute_tool");
        activity.Should().NotBeNull();
        activity!.SetTag(GenAiOperationNameKey, "execute_tool");
        activity.SetTag(GenAiAgentNameKey, "helper-agent");

        activity.Stop();

        activity.GetTagItem(GenAiMainAgentNameKey).Should().BeNull(
            because: "self-promotion is limited to top-level invoke_agent activities");
    }

    [TestMethod]
    public void OnEnd_FallsBackToParent_WhenParentEnrichedAfterChildStart()
    {
        using var scope = BuildScope();

        // Parent starts with no main-agent tags — child inherits nothing at OnStart.
        using var parent = scope.Source.StartActivity("parent");
        using var child = scope.Source.StartActivity("child");
        child.Should().NotBeNull();
        child!.GetTagItem(GenAiMainAgentNameKey).Should().BeNull();

        // Parent is enriched after the child started (e.g. Azure SDK sets tags late).
        parent!.SetTag(GenAiMainAgentNameKey, "late-main-agent");
        parent.SetTag(GenAiMainAgentConversationIdKey, "late-conv");

        child.Stop();

        child.GetTagItem(GenAiMainAgentNameKey).Should().Be("late-main-agent");
        child.GetTagItem(GenAiMainAgentConversationIdKey).Should().Be("late-conv");
    }

    [TestMethod]
    public void OnEnd_ProjectIdFallback_WhenParentStampsAfterChildStart()
    {
        using var scope = BuildScope();

        using var parent = scope.Source.StartActivity("parent");
        using var child = scope.Source.StartActivity("child");
        child.Should().NotBeNull();
        child!.GetTagItem(GenAiFoundryProjectIdKey).Should().BeNull();

        parent!.SetTag(GenAiFoundryProjectIdKey, TestProjectArmId);

        child.Stop();

        child.GetTagItem(GenAiFoundryProjectIdKey).Should().Be(TestProjectArmId);
    }

    [TestMethod]
    public void OnEnd_DoesNotOverrideExistingMainAgentAttributes()
    {
        using var scope = BuildScope();

        using var parent = scope.Source.StartActivity("parent");
        parent!.SetTag(GenAiMainAgentNameKey, "from-parent");

        using var child = scope.Source.StartActivity("child");
        child.Should().NotBeNull();
        // Child inherited "from-parent" on OnStart.
        child!.GetTagItem(GenAiMainAgentNameKey).Should().Be("from-parent");

        // Parent changes its main-agent tag AFTER child started. Because the child
        // already has a main-agent attribute, OnEnd should not re-copy from parent.
        parent.SetTag(GenAiMainAgentNameKey, "changed-later");
        child.Stop();

        child.GetTagItem(GenAiMainAgentNameKey).Should().Be(
            "from-parent",
            because: "OnEnd fallback propagation skips when the child already has a main-agent attribute");
    }

    // ── helpers ─────────────────────────────────────────────────────────────

    private static TestScope BuildScope() => new(TestSourceName);

    private sealed class TestScope : IDisposable
    {
        public ActivitySource Source { get; }

        private readonly TracerProvider _tracerProvider;

        public TestScope(string sourceName)
        {
            Source = new ActivitySource(sourceName);
            _tracerProvider = Sdk.CreateTracerProviderBuilder()
                .AddSource(sourceName)
                .SetSampler(new AlwaysOnSampler())
                .AddProcessor(new GenAIMainAgentSpanProcessor())
                .Build();
        }

        public void Dispose()
        {
            _tracerProvider.Dispose();
            Source.Dispose();
        }
    }
}
