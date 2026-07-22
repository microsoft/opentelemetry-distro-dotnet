// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.OpenTelemetry.Tests.GenAI.MainAgent;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using FluentAssertions;
using Microsoft.OpenTelemetry.GenAI.MainAgent;
using global::OpenTelemetry;
using global::OpenTelemetry.Trace;
using static Microsoft.Agents.A365.Observability.Runtime.Tracing.Scopes.OpenTelemetryConstants;

/// <summary>
/// Real-SDK propagation tests for <see cref="GenAIMainAgentSpanProcessor"/>.
/// <para>
/// Uses a real <see cref="TracerProvider"/> + <c>AddInMemoryExporter</c> so exported
/// <see cref="Activity"/> instances are inspected via the exporter (not the live
/// activity), matching the behaviour production consumers observe. This catches
/// timing issues (parent enriched after child creation) and OnEnd fallback
/// interactions that pure OnStart/OnEnd unit tests cannot detect.
/// </para>
/// </summary>
[TestClass]
public sealed class GenAIMainAgentSpanProcessorSdkPropagationTests
{
    private const string TestSourceName = "Microsoft.OpenTelemetry.Tests.GenAIMainAgent.Sdk";

    // ── invoke_agent → chat ────────────────────────────────────────────────

    [TestMethod]
    public void InvokeAgent_PropagatesToChatSpan()
    {
        using var scope = BuildScope();

        var agent = scope.Source.StartActivity("invoke_agent TravelBot");
        agent.Should().NotBeNull();
        agent!.SetTag(GenAiOperationNameKey, InvokeAgentOperationName);
        agent.SetTag(GenAiAgentNameKey, "TravelBot");
        agent.SetTag(GenAiAgentIdKey, "agent-1");
        agent.SetTag(GenAiAgentVersionKey, "2.0");
        agent.SetTag(GenAiConversationIdKey, "conv-1");

        var chat = scope.Source.StartActivity("chat gpt-4");
        chat!.Stop();
        agent.Stop();

        var spans = scope.GetExportedByName();
        var chatSpan = spans["chat gpt-4"];
        chatSpan.GetTagItem(GenAiMainAgentNameKey).Should().Be("TravelBot");
        chatSpan.GetTagItem(GenAiMainAgentIdKey).Should().Be("agent-1");
        chatSpan.GetTagItem(GenAiMainAgentVersionKey).Should().Be("2.0");
        chatSpan.GetTagItem(GenAiMainAgentConversationIdKey).Should().Be("conv-1");
    }

    // ── invoke_agent → execute_tool ────────────────────────────────────────

    [TestMethod]
    public void InvokeAgent_PropagatesToToolSpan()
    {
        using var scope = BuildScope();

        var agent = scope.Source.StartActivity("invoke_agent TravelBot");
        agent.Should().NotBeNull();
        agent!.SetTag(GenAiOperationNameKey, InvokeAgentOperationName);
        agent.SetTag(GenAiAgentNameKey, "TravelBot");
        agent.SetTag(GenAiAgentIdKey, "agent-1");

        var tool = scope.Source.StartActivity("execute_tool get_weather");
        tool!.Stop();
        agent.Stop();

        var spans = scope.GetExportedByName();
        spans["execute_tool get_weather"].GetTagItem(GenAiMainAgentNameKey).Should().Be("TravelBot");
        spans["execute_tool get_weather"].GetTagItem(GenAiMainAgentIdKey).Should().Be("agent-1");
    }

    // ── invoke_agent wrapper → inner invoke_agent → chat ───────────────────

    [TestMethod]
    public void TwoSpanWrapper_PropagatesThroughInnerToChat()
    {
        using var scope = BuildScope();

        var wrapper = scope.Source.StartActivity("invoke_agent TravelBot");
        wrapper.Should().NotBeNull();
        wrapper!.SetTag(GenAiOperationNameKey, InvokeAgentOperationName);
        wrapper.SetTag(GenAiAgentNameKey, "TravelBot");
        wrapper.SetTag(GenAiAgentIdKey, "agent-1");

        var inner = scope.Source.StartActivity("invoke_agent LangGraph");
        inner.Should().NotBeNull();

        var chat = scope.Source.StartActivity("chat gpt-4");
        chat!.Stop();
        inner!.Stop();
        wrapper.Stop();

        var spans = scope.GetExportedByName();

        // Inner span inherits main_agent tags from wrapper via the fallback path
        // (wrapper has gen_ai.agent.* but not microsoft.gen_ai.main_agent.*).
        spans["invoke_agent LangGraph"].GetTagItem(GenAiMainAgentNameKey).Should().Be("TravelBot");
        spans["invoke_agent LangGraph"].GetTagItem(GenAiMainAgentIdKey).Should().Be("agent-1");
        // Chat inherits main_agent tags from inner via the primary path
        // (inner now carries microsoft.gen_ai.main_agent.*).
        spans["chat gpt-4"].GetTagItem(GenAiMainAgentNameKey).Should().Be("TravelBot");
        spans["chat gpt-4"].GetTagItem(GenAiMainAgentIdKey).Should().Be("agent-1");
    }

    // ── multi-agent: main → sub → chat ─────────────────────────────────────

    [TestMethod]
    public void MultiAgent_PreservesMainAgentOverSubAgent()
    {
        using var scope = BuildScope();

        var main = scope.Source.StartActivity("invoke_agent MainBot");
        main.Should().NotBeNull();
        main!.SetTag(GenAiOperationNameKey, InvokeAgentOperationName);
        main.SetTag(GenAiAgentNameKey, "MainBot");
        main.SetTag(GenAiAgentIdKey, "main-1");

        var sub = scope.Source.StartActivity("invoke_agent SubBot");
        sub.Should().NotBeNull();
        sub!.SetTag(GenAiOperationNameKey, InvokeAgentOperationName);
        sub.SetTag(GenAiAgentNameKey, "SubBot");
        sub.SetTag(GenAiAgentIdKey, "sub-1");

        var chat = scope.Source.StartActivity("chat gpt-4");
        chat!.Stop();
        sub.Stop();
        main.Stop();

        var spans = scope.GetExportedByName();
        // Sub-agent inherits MainBot via primary propagation at OnStart.
        spans["invoke_agent SubBot"].GetTagItem(GenAiMainAgentNameKey).Should().Be("MainBot");
        spans["invoke_agent SubBot"].GetTagItem(GenAiMainAgentIdKey).Should().Be("main-1");
        // Chat also preserves MainBot (propagated through the sub-agent).
        spans["chat gpt-4"].GetTagItem(GenAiMainAgentNameKey).Should().Be("MainBot");
        spans["chat gpt-4"].GetTagItem(GenAiMainAgentIdKey).Should().Be("main-1");
    }

    // ── siblings under a common invoke_agent parent ────────────────────────

    [TestMethod]
    public void PropagatesToSiblingSpans()
    {
        using var scope = BuildScope();

        var agent = scope.Source.StartActivity("invoke_agent TravelBot");
        agent.Should().NotBeNull();
        agent!.SetTag(GenAiOperationNameKey, InvokeAgentOperationName);
        agent.SetTag(GenAiAgentNameKey, "TravelBot");

        var chat = scope.Source.StartActivity("chat gpt-4");
        chat!.Stop();
        var tool = scope.Source.StartActivity("execute_tool search");
        tool!.Stop();
        agent.Stop();

        var spans = scope.GetExportedByName();
        spans["chat gpt-4"].GetTagItem(GenAiMainAgentNameKey).Should().Be("TravelBot");
        spans["execute_tool search"].GetTagItem(GenAiMainAgentNameKey).Should().Be("TravelBot");
    }

    // ── non-agent parent → child ───────────────────────────────────────────

    [TestMethod]
    public void NonAgentParent_DoesNotPropagate()
    {
        using var scope = BuildScope();

        var parent = scope.Source.StartActivity("chat gpt-4");
        parent.Should().NotBeNull();
        parent!.SetTag("http.method", "POST");

        var child = scope.Source.StartActivity("some_child");
        child!.Stop();
        parent.Stop();

        var spans = scope.GetExportedByName();
        spans["some_child"].GetTagItem(GenAiMainAgentNameKey).Should().BeNull();
    }

    // ── timing: attributes set after child creation ────────────────────────

    [TestMethod]
    public void AttrsSetAfterChildCreation_RecoveredOnEnd()
    {
        using var scope = BuildScope();

        var agent = scope.Source.StartActivity("invoke_agent TravelBot");
        agent.Should().NotBeNull();

        // Create child BEFORE setting attributes on parent → OnStart sees an empty parent.
        var child = scope.Source.StartActivity("chat gpt-4");
        child.Should().NotBeNull();
        child!.GetTagItem(GenAiMainAgentNameKey).Should().BeNull();

        agent!.SetTag(GenAiOperationNameKey, InvokeAgentOperationName);
        agent.SetTag(GenAiAgentNameKey, "TravelBot");

        child.Stop();
        agent.Stop();

        var spans = scope.GetExportedByName();
        spans["chat gpt-4"].GetTagItem(GenAiMainAgentNameKey).Should().Be(
            "TravelBot",
            because: "OnEnd fallback recovers propagation from a parent enriched after child creation");
    }

    // ── partial attributes propagate ───────────────────────────────────────

    [TestMethod]
    public void PartialAttributes_Propagate()
    {
        using var scope = BuildScope();

        var agent = scope.Source.StartActivity("invoke_agent Bot");
        agent.Should().NotBeNull();
        agent!.SetTag(GenAiAgentNameKey, "Bot");

        var chat = scope.Source.StartActivity("chat gpt-4");
        chat!.Stop();
        agent.Stop();

        var spans = scope.GetExportedByName();
        spans["chat gpt-4"].GetTagItem(GenAiMainAgentNameKey).Should().Be("Bot");
        spans["chat gpt-4"].GetTagItem(GenAiMainAgentIdKey).Should().BeNull();
    }

    // ── self-promotion on OnEnd ────────────────────────────────────────────

    [TestMethod]
    public void RootInvokeAgent_SelfPromotesOnEnd()
    {
        using var scope = BuildScope();

        var agent = scope.Source.StartActivity("invoke_agent TravelBot");
        agent.Should().NotBeNull();
        agent!.SetTag(GenAiOperationNameKey, InvokeAgentOperationName);
        agent.SetTag(GenAiAgentNameKey, "TravelBot");
        agent.SetTag(GenAiAgentIdKey, "agent-1");
        agent.SetTag(GenAiAgentVersionKey, "2.0");
        agent.SetTag(GenAiConversationIdKey, "conv-1");
        agent.Stop();

        var exported = scope.GetExportedByName()["invoke_agent TravelBot"];
        exported.GetTagItem(GenAiMainAgentNameKey).Should().Be("TravelBot");
        exported.GetTagItem(GenAiMainAgentIdKey).Should().Be("agent-1");
        exported.GetTagItem(GenAiMainAgentVersionKey).Should().Be("2.0");
        exported.GetTagItem(GenAiMainAgentConversationIdKey).Should().Be("conv-1");
    }

    [TestMethod]
    public void NestedInvokeAgent_DoesNotSelfPromote()
    {
        using var scope = BuildScope();

        var main = scope.Source.StartActivity("invoke_agent MainBot");
        main.Should().NotBeNull();
        main!.SetTag(GenAiOperationNameKey, InvokeAgentOperationName);
        main.SetTag(GenAiAgentNameKey, "MainBot");
        main.SetTag(GenAiAgentIdKey, "main-1");

        var sub = scope.Source.StartActivity("invoke_agent SubBot");
        sub.Should().NotBeNull();
        sub!.SetTag(GenAiOperationNameKey, InvokeAgentOperationName);
        sub.SetTag(GenAiAgentNameKey, "SubBot");
        sub.SetTag(GenAiAgentIdKey, "sub-1");
        sub.Stop();
        main.Stop();

        var subExported = scope.GetExportedByName()["invoke_agent SubBot"];
        // main_agent must be MainBot (inherited), not SubBot (own).
        subExported.GetTagItem(GenAiMainAgentNameKey).Should().Be("MainBot");
        subExported.GetTagItem(GenAiMainAgentIdKey).Should().Be("main-1");
    }

    [TestMethod]
    public void SelfPromotion_OnlyForInvokeAgent()
    {
        using var scope = BuildScope();

        var chat = scope.Source.StartActivity("chat gpt-4");
        chat.Should().NotBeNull();
        chat!.SetTag(GenAiAgentNameKey, "Bot");
        chat.Stop();

        var exported = scope.GetExportedByName()["chat gpt-4"];
        exported.GetTagItem(GenAiMainAgentNameKey).Should().BeNull();
    }

    // ── project-id propagation ─────────────────────────────────────────────

    [TestMethod]
    public void ProjectId_PropagatesToChildSpan()
    {
        using var scope = BuildScope();

        var agent = scope.Source.StartActivity("invoke_agent TravelBot");
        agent.Should().NotBeNull();
        agent!.SetTag(GenAiOperationNameKey, InvokeAgentOperationName);
        agent.SetTag(GenAiAgentNameKey, "TravelBot");
        StampProjectId(agent);

        var chat = scope.Source.StartActivity("chat gpt-4");
        chat!.Stop();
        agent.Stop();

        AssertProjectIdOn(scope.GetExportedByName()["chat gpt-4"]);
    }

    [TestMethod]
    public void ProjectId_PropagatesThroughMultipleLevels()
    {
        using var scope = BuildScope();

        var agent = scope.Source.StartActivity("invoke_agent TravelBot");
        agent.Should().NotBeNull();
        agent!.SetTag(GenAiOperationNameKey, InvokeAgentOperationName);
        agent.SetTag(GenAiAgentNameKey, "TravelBot");
        StampProjectId(agent);

        var tool = scope.Source.StartActivity("execute_tool get_weather");
        tool.Should().NotBeNull();
        var inner = scope.Source.StartActivity("chat gpt-4");
        inner!.Stop();
        tool!.Stop();
        agent.Stop();

        var spans = scope.GetExportedByName();
        AssertProjectIdOn(spans["execute_tool get_weather"]);
        AssertProjectIdOn(spans["chat gpt-4"]);
    }

    [TestMethod]
    public void ProjectId_RecoveredOnEnd_WhenStampedAfterChild()
    {
        using var scope = BuildScope();

        var agent = scope.Source.StartActivity("invoke_agent TravelBot");
        agent.Should().NotBeNull();
        var child = scope.Source.StartActivity("chat gpt-4");
        child.Should().NotBeNull();

        // Stamp project-id AFTER child was created → OnStart already fired.
        StampProjectId(agent!);

        child!.Stop();
        agent!.Stop();

        AssertProjectIdOn(scope.GetExportedByName()["chat gpt-4"]);
    }

    [TestMethod]
    public void ProjectId_PropagatesAlongsideMainAgentAttrs()
    {
        using var scope = BuildScope();

        var agent = scope.Source.StartActivity("invoke_agent TravelBot");
        agent.Should().NotBeNull();
        agent!.SetTag(GenAiOperationNameKey, InvokeAgentOperationName);
        agent.SetTag(GenAiAgentNameKey, "TravelBot");
        agent.SetTag(GenAiAgentIdKey, "agent-1");
        StampProjectId(agent);

        var chat = scope.Source.StartActivity("chat gpt-4");
        chat!.Stop();
        agent.Stop();

        var chatSpan = scope.GetExportedByName()["chat gpt-4"];
        chatSpan.GetTagItem(GenAiMainAgentNameKey).Should().Be("TravelBot");
        chatSpan.GetTagItem(GenAiMainAgentIdKey).Should().Be("agent-1");
        AssertProjectIdOn(chatSpan);
    }

    [TestMethod]
    public void ProjectId_NotAdded_WhenParentUnstamped()
    {
        using var scope = BuildScope();

        var agent = scope.Source.StartActivity("invoke_agent TravelBot");
        agent.Should().NotBeNull();
        agent!.SetTag(GenAiOperationNameKey, InvokeAgentOperationName);
        agent.SetTag(GenAiAgentNameKey, "TravelBot");

        var chat = scope.Source.StartActivity("chat gpt-4");
        chat!.Stop();
        agent.Stop();

        var chatSpan = scope.GetExportedByName()["chat gpt-4"];
        foreach (var key in GenAiProjectIdKeys)
        {
            chatSpan.GetTagItem(key).Should().BeNull(because: $"parent did not carry {key}");
        }
    }

    // ── helpers ─────────────────────────────────────────────────────────────

    private const string TestProjectId =
        "/subscriptions/sub/resourceGroups/rg/providers/x/projects/p";

    private static void StampProjectId(Activity activity)
    {
        foreach (var key in GenAiProjectIdKeys)
        {
            activity.SetTag(key, TestProjectId);
        }
    }

    private static void AssertProjectIdOn(Activity activity)
    {
        foreach (var key in GenAiProjectIdKeys)
        {
            activity.GetTagItem(key).Should().Be(TestProjectId, because: $"child should inherit {key}");
        }
    }

    private static SdkScope BuildScope() => new(TestSourceName);

    private sealed class SdkScope : IDisposable
    {
        private readonly TracerProvider _tracerProvider;
        private readonly List<Activity> _exported;

        public SdkScope(string sourceName)
        {
            Source = new ActivitySource(sourceName);
            _exported = new List<Activity>();
            _tracerProvider = Sdk.CreateTracerProviderBuilder()
                .AddSource(sourceName)
                .SetSampler(new AlwaysOnSampler())
                // Main-agent processor FIRST so OnStart/OnEnd enrichment runs before export.
                .AddProcessor(new GenAIMainAgentSpanProcessor())
                .AddInMemoryExporter(_exported)
                .Build();
        }

        public ActivitySource Source { get; }

        public Dictionary<string, Activity> GetExportedByName()
        {
            _tracerProvider.ForceFlush();
            return _exported.ToDictionary(a => a.DisplayName, a => a);
        }

        public void Dispose()
        {
            _tracerProvider.Dispose();
            Source.Dispose();
        }
    }
}
