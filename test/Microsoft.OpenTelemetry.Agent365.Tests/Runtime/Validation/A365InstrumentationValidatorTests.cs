using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Agents.A365.Observability.Runtime.Common;
using Microsoft.Agents.A365.Observability.Runtime.Tracing.Contracts;
using Microsoft.Agents.A365.Observability.Runtime.Tracing.Processors;
using Microsoft.Agents.A365.Observability.Runtime.Tracing.Scopes;
using Microsoft.Agents.A365.Observability.Runtime.Validation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenTelemetry.AgentFramework;
using OpenTelemetry;
using OpenTelemetry.Trace;

namespace Microsoft.OpenTelemetry.Agent365.Tests.Runtime.Validation;

[TestClass]
public sealed class A365InstrumentationValidatorTests
{
    [TestMethod]
    public async Task EvaluateAsync_CapturesRecognizedCustomSourceSpan()
    {
        var report = await A365InstrumentationValidator.EvaluateAsync(() =>
        {
            using var source = new ActivitySource("Customer.Agent");
            using var activity = source.StartActivity("chat model");
            activity.Should().NotBeNull();
            SetValidChatAttributes(activity!);
            return Task.CompletedTask;
        });

        report.Spans.Should().ContainSingle();
        report.Spans[0].Span.SourceName.Should().Be("Customer.Agent");
        report.IsValid.Should().BeTrue();
    }

    [TestMethod]
    public async Task EvaluateAsync_ExportIdentityOnlyInActivityBaggage_SatisfiesNonSuppressibleRules()
    {
        var report = await A365InstrumentationValidator.EvaluateAsync(() =>
        {
            using var source = new ActivitySource("Customer.Agent.Baggage");
            using var activity = source.StartActivity("chat model");
            activity.Should().NotBeNull();

            SetValidChatAttributes(activity!, includeExportIdentity: false);

            // Tenant and agent identity are carried only in Activity baggage --
            // exactly what Agent365ExporterCore reads through
            // GetAttributeOrBaggage when it partitions spans for export. The
            // two non-suppressible identity rules must therefore pass.
            activity!.AddBaggage(OpenTelemetryConstants.TenantIdKey, "tenant");
            activity.AddBaggage(OpenTelemetryConstants.GenAiAgentIdKey, "agent");
            activity.GetTagItem(OpenTelemetryConstants.TenantIdKey).Should().BeNull();
            activity.GetTagItem(OpenTelemetryConstants.GenAiAgentIdKey).Should().BeNull();

            return Task.CompletedTask;
        });

        var spanResult = report.Spans.Should().ContainSingle().Which;
        spanResult.Findings.Should().NotContain(f =>
            f.RuleId == A365ValidationRuleIds.TenantIdRequired ||
            f.RuleId == A365ValidationRuleIds.AgentIdentityRequired);

        // The public attribute set reflects what the exporter serializes --
        // activity tags only -- so the baggage-only routing identity must not
        // be reported as an exported attribute.
        spanResult.Span.Attributes.Should().NotContainKey(
            OpenTelemetryConstants.TenantIdKey);
        spanResult.Span.Attributes.Should().NotContainKey(
            OpenTelemetryConstants.GenAiAgentIdKey);
        report.EnsureValid();
    }

    [TestMethod]
    public async Task EvaluateAsync_AgentPlatformIdOnlyInActivityBaggage_SatisfiesAgentIdentityRule()
    {
        var report = await A365InstrumentationValidator.EvaluateAsync(() =>
        {
            using var source = new ActivitySource("Customer.Agent.BaggagePlatformId");
            using var activity = source.StartActivity("chat model");
            activity.Should().NotBeNull();

            SetValidChatAttributes(activity!, includeExportIdentity: false);
            activity!.AddBaggage(OpenTelemetryConstants.TenantIdKey, "tenant");
            activity.AddBaggage(
                OpenTelemetryConstants.AgentPlatformIdKey,
                "platform-agent");

            return Task.CompletedTask;
        });

        var spanResult = report.Spans.Should().ContainSingle().Which;
        spanResult.Findings.Should().NotContain(f =>
            f.RuleId == A365ValidationRuleIds.TenantIdRequired ||
            f.RuleId == A365ValidationRuleIds.AgentIdentityRequired);
        spanResult.Span.Attributes.Should().NotContainKey(
            OpenTelemetryConstants.AgentPlatformIdKey);
        report.EnsureValid();
    }

    [TestMethod]
    public async Task EvaluateAsync_PayloadAttributesOnlyInActivityBaggage_DoNotSatisfyTheirRules()
    {
        var report = await A365InstrumentationValidator.EvaluateAsync(() =>
        {
            using var source = new ActivitySource("Customer.Agent.BaggagePayload");
            using var activity = source.StartActivity("chat model");
            activity.Should().NotBeNull();

            // Only the values the exporter resolves before serialization --
            // the operation name and the routing identity -- are tagged or
            // baggaged in a way that reaches the exporter. Every payload
            // attribute lives exclusively in Activity baggage, which is never
            // serialized into OTLP span attributes.
            activity!.SetTag(OpenTelemetryConstants.GenAiOperationNameKey, "chat");
            activity.AddBaggage(OpenTelemetryConstants.TenantIdKey, "tenant");
            activity.AddBaggage(OpenTelemetryConstants.GenAiAgentIdKey, "agent");
            activity.AddBaggage(OpenTelemetryConstants.GenAiAgentNameKey, "Weather agent");
            activity.AddBaggage(
                OpenTelemetryConstants.GenAiAgentDescriptionKey,
                "Answers weather questions");
            activity.AddBaggage(OpenTelemetryConstants.AgentAUIDKey, "agent-user");
            activity.AddBaggage(OpenTelemetryConstants.AgentEmailKey, "agent@example.com");
            activity.AddBaggage(OpenTelemetryConstants.AgentBlueprintIdKey, "blueprint");
            activity.AddBaggage(OpenTelemetryConstants.GenAiRequestModelKey, "gpt-4.1");
            activity.AddBaggage(OpenTelemetryConstants.GenAiProviderNameKey, "openai");

            return Task.CompletedTask;
        });

        var spanResult = report.Spans.Should().ContainSingle().Which;
        var activeRuleIds = spanResult.Findings
            .Where(f => f.Status == A365ValidationFindingStatus.Active)
            .Select(f => f.RuleId)
            .ToList();

        // Baggage-only routing identity still routes the export, so only the
        // routing rules pass.
        activeRuleIds.Should().NotContain(A365ValidationRuleIds.TenantIdRequired);
        activeRuleIds.Should().NotContain(A365ValidationRuleIds.AgentIdentityRequired);

        activeRuleIds.Should().Contain(new[]
        {
            A365ValidationRuleIds.AgentNameRequired,
            A365ValidationRuleIds.AgentDescriptionRequired,
            A365ValidationRuleIds.AgentUserIdRequired,
            A365ValidationRuleIds.AgentUserEmailRequired,
            A365ValidationRuleIds.AgentBlueprintIdRequired,
            A365ValidationRuleIds.InferenceModelRequired,
            A365ValidationRuleIds.InferenceProviderRequired,
        });

        spanResult.Findings.Should().Contain(f =>
            f.RuleId == A365ValidationRuleIds.AgentNameRequired &&
            f.Message == $"Missing {OpenTelemetryConstants.GenAiAgentNameKey}");

        // None of the baggage-only values are reported as exported attributes.
        spanResult.Span.Attributes.Keys.Should().Equal(
            OpenTelemetryConstants.GenAiOperationNameKey);
    }

    [TestMethod]
    public async Task EvaluateAsync_OperationSpecificAttributesOnlyInActivityBaggage_DoNotSatisfyTheirRules()
    {
        var report = await A365InstrumentationValidator.EvaluateAsync(() =>
        {
            using var source = new ActivitySource("Customer.Agent.BaggageOperationPayload");

            using (var invoke = source.StartActivity("invoke_agent WeatherAgent"))
            {
                SetBaggageOnlyCommonAttributes(invoke!, "invoke_agent");
                invoke!.AddBaggage(OpenTelemetryConstants.UserIdKey, "user-id");
                invoke.AddBaggage(OpenTelemetryConstants.UserNameKey, "User Name");
                invoke.AddBaggage(OpenTelemetryConstants.UserEmailKey, "user@example.com");
            }

            using (var tool = source.StartActivity("execute_tool weather"))
            {
                SetBaggageOnlyCommonAttributes(tool!, "execute_tool");
                tool!.AddBaggage(OpenTelemetryConstants.GenAiToolNameKey, "weather");
            }

            using (var guardrail = source.StartActivity("apply_guardrail input"))
            {
                SetBaggageOnlyCommonAttributes(guardrail!, "apply_guardrail");
                guardrail!.AddBaggage(
                    OpenTelemetryConstants.GenAiSecurityDecisionTypeKey,
                    "allow");
                guardrail.AddBaggage(
                    OpenTelemetryConstants.GenAiSecurityTargetTypeKey,
                    "llm_input");
            }

            return Task.CompletedTask;
        });

        report.Spans.Should().HaveCount(3);

        foreach (var spanResult in report.Spans)
        {
            // Baggage-only operation name still classifies the span, and
            // baggage-only tenant/agent identity still routes it.
            spanResult.Span.OperationName.Should().NotBeNullOrEmpty();
            spanResult.Findings.Should().NotContain(f =>
                f.RuleId == A365ValidationRuleIds.TenantIdRequired ||
                f.RuleId == A365ValidationRuleIds.AgentIdentityRequired);

            // Nothing from baggage is reported as an exported attribute.
            spanResult.Span.Attributes.Should().BeEmpty();
        }

        ActiveRuleIds("invoke_agent").Should().Contain(new[]
        {
            A365ValidationRuleIds.InvokeUserIdRequired,
            A365ValidationRuleIds.InvokeUserNameRequired,
            A365ValidationRuleIds.InvokeUserEmailRequired,
        });
        ActiveRuleIds("execute_tool").Should().Contain(
            A365ValidationRuleIds.ToolNameRequired);
        ActiveRuleIds("apply_guardrail").Should().Contain(new[]
        {
            A365ValidationRuleIds.GuardrailDecisionRequired,
            A365ValidationRuleIds.GuardrailTargetRequired,
        });

        List<string> ActiveRuleIds(string operationName) => report.Spans
            .Single(s => s.Span.OperationName == operationName)
            .Findings
            .Where(f => f.Status == A365ValidationFindingStatus.Active)
            .Select(f => f.RuleId)
            .ToList();
    }

    [TestMethod]
    public async Task EvaluateAsync_TimeoutShorterThanQuietPeriod_ThrowsBeforeRunningAction()
    {
        var actionRan = false;

        Func<Task> act = () => A365InstrumentationValidator.EvaluateAsync(
            () =>
            {
                actionRan = true;
                return Task.CompletedTask;
            },
            options => options.SpanCompletionTimeout = TimeSpan.FromMilliseconds(249));

        var exception = await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
        exception.Which.ParamName.Should().Be("SpanCompletionTimeout");
        exception.Which.Message.Should().Contain("250ms quiet period");
        actionRan.Should().BeFalse();
    }

    [TestMethod]
    public async Task EvaluateAsync_TimeoutEqualToQuietPeriod_IsAccepted()
    {
        var report = await A365InstrumentationValidator.EvaluateAsync(
            () =>
            {
                using var source = new ActivitySource("Customer.Agent.MinimumTimeout");
                using var activity = source.StartActivity("chat model");
                SetValidChatAttributes(activity!);
                return Task.CompletedTask;
            },
            options => options.SpanCompletionTimeout = TimeSpan.FromMilliseconds(250));

        report.Spans.Should().ContainSingle();
        report.IsValid.Should().BeTrue();
        report.SessionFindings.Should().NotContain(
            finding => finding.RuleId == A365ValidationRuleIds.SpanCompletionTimeout);
    }

    [TestMethod]
    public async Task EvaluateAsync_OperationNameOnlyInActivityBaggage_IsValidated()
    {
        var report = await A365InstrumentationValidator.EvaluateAsync(() =>
        {
            using var source = new ActivitySource("Customer.Agent.BaggageOperation");
            using var activity = source.StartActivity("chat model");
            activity.Should().NotBeNull();

            SetValidChatAttributes(activity!);
            activity!.SetTag(OpenTelemetryConstants.GenAiOperationNameKey, null);
            activity.AddBaggage(OpenTelemetryConstants.GenAiOperationNameKey, "chat");
            activity.GetTagItem(OpenTelemetryConstants.GenAiOperationNameKey)
                .Should().BeNull();

            return Task.CompletedTask;
        });

        var spanResult = report.Spans.Should().ContainSingle().Which;
        spanResult.Span.OperationName.Should().Be("chat");
        spanResult.Span.Attributes.Should().NotContainKey(
            OpenTelemetryConstants.GenAiOperationNameKey);
        report.EnsureValid();
    }

    [TestMethod]
    public async Task EvaluateAsync_IgnoresUnsupportedActivities()
    {
        var report = await A365InstrumentationValidator.EvaluateAsync(() =>
        {
            using var source = new ActivitySource("Customer.Agent");
            using var activity = source.StartActivity("http request");
            activity!.SetTag("gen_ai.operation.name", "unsupported");
            return Task.CompletedTask;
        });

        report.IsValid.Should().BeFalse();
        report.SessionFindings.Should().ContainSingle(f =>
            f.RuleId == A365ValidationRuleIds.NoSpansCaptured);
    }

    [TestMethod]
    public async Task EvaluateAsync_RethrowsActionException()
    {
        var expected = new InvalidOperationException("application failed");

        Func<Task> act = () => A365InstrumentationValidator.EvaluateAsync(
            () => Task.FromException(expected));

        var actual = await act.Should().ThrowAsync<InvalidOperationException>();
        actual.Which.Should().BeSameAs(expected);
    }

    [TestMethod]
    public async Task EvaluateAsync_ActionThrows_DetachesListenerAndReleasesLockForNextEvaluation()
    {
        Func<Task> failing = () => A365InstrumentationValidator.EvaluateAsync(
            () => Task.FromException(new InvalidOperationException("boom")));

        await failing.Should().ThrowAsync<InvalidOperationException>();

        using var probe = new ActivitySource(
            nameof(EvaluateAsync_ActionThrows_DetachesListenerAndReleasesLockForNextEvaluation));
        probe.HasListeners().Should().BeFalse();

        var report = await A365InstrumentationValidator.EvaluateAsync(() =>
        {
            using var source = new ActivitySource("Customer.Agent.Recovery");
            using var activity = source.StartActivity("chat model");
            SetValidChatAttributes(activity!);
            return Task.CompletedTask;
        });

        report.IsValid.Should().BeTrue();
        report.Spans.Should().ContainSingle();
    }

    [TestMethod]
    public async Task EvaluateAsync_NullAction_ThrowsArgumentNullException()
    {
        Func<Task> act = () => A365InstrumentationValidator.EvaluateAsync(null!);

        var exception = await act.Should().ThrowAsync<ArgumentNullException>();
        exception.Which.ParamName.Should().Be("action");
    }

    [TestMethod]
    public async Task EvaluateAsync_ActiveSpanNeverCompletes_AddsTimeoutFinding()
    {
        Activity? stuck = null;

        var report = await A365InstrumentationValidator.EvaluateAsync(
            () =>
            {
                using var source = new ActivitySource(
                    nameof(EvaluateAsync_ActiveSpanNeverCompletes_AddsTimeoutFinding));
                stuck = source.StartActivity("stuck chat");
                stuck!.SetTag(OpenTelemetryConstants.GenAiOperationNameKey, "chat");
                return Task.CompletedTask;
            },
            options => options.SpanCompletionTimeout = TimeSpan.FromMilliseconds(300));

        try
        {
            report.IsValid.Should().BeFalse();
            report.SessionFindings.Should().ContainSingle(f =>
                f.RuleId == A365ValidationRuleIds.SpanCompletionTimeout &&
                f.Severity == A365ValidationSeverity.Error);
        }
        finally
        {
            stuck?.Dispose();
        }
    }

    [TestMethod]
    public async Task EvaluateAsync_UnusedSuppression_AddsWarningFinding()
    {
        var report = await A365InstrumentationValidator.EvaluateAsync(
            () =>
            {
                using var source = new ActivitySource("Customer.Agent.Unused");
                using var activity = source.StartActivity("chat model");
                SetValidChatAttributes(activity!);
                return Task.CompletedTask;
            },
            options => options.Suppress(
                A365ValidationRuleIds.ToolNameRequired,
                "execute_tool",
                "Not exercised by this scenario"));

        report.IsValid.Should().BeTrue();
        report.WarningCount.Should().Be(1);
        report.SessionFindings.Should().ContainSingle(f =>
            f.RuleId == A365ValidationRuleIds.UnusedSuppression &&
            f.Severity == A365ValidationSeverity.Warning &&
            f.Message.Contains(A365ValidationRuleIds.ToolNameRequired));
    }

    [TestMethod]
    public async Task EvaluateAsync_SpanFilterThrows_ThrowsA365ValidationExecutionException()
    {
        Func<Task> act = () => A365InstrumentationValidator.EvaluateAsync(
            () =>
            {
                using var source = new ActivitySource("Customer.Agent.Filtered");
                using var activity = source.StartActivity("chat model");
                SetValidChatAttributes(activity!);
                return Task.CompletedTask;
            },
            options => options.SpanFilter = _ => throw new InvalidOperationException("filter boom"));

        var exception = await act.Should().ThrowAsync<A365ValidationExecutionException>();
        exception.Which.InnerException.Should().BeOfType<InvalidOperationException>()
            .Which.Message.Should().Be("filter boom");

        using var probe = new ActivitySource(
            nameof(EvaluateAsync_SpanFilterThrows_ThrowsA365ValidationExecutionException) + ".Probe");
        probe.HasListeners().Should().BeFalse();
    }

    [TestMethod]
    public async Task EvaluateAsync_AlreadyCancelledToken_ThrowsWithoutRunningAction()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var actionRan = false;

        Func<Task> act = () => A365InstrumentationValidator.EvaluateAsync(
            () =>
            {
                actionRan = true;
                return Task.CompletedTask;
            },
            cancellationToken: cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        actionRan.Should().BeFalse();
    }

    [TestMethod]
    public async Task EvaluateAsync_ConcurrentEvaluations_RunSerially()
    {
        var concurrentCount = 0;
        var maxObservedConcurrency = 0;
        var gate = new object();

        async Task TrackedAction()
        {
            lock (gate)
            {
                concurrentCount++;
                maxObservedConcurrency = Math.Max(maxObservedConcurrency, concurrentCount);
            }

            await Task.Delay(150).ConfigureAwait(false);

            lock (gate)
            {
                concurrentCount--;
            }
        }

        var first = A365InstrumentationValidator.EvaluateAsync(TrackedAction);
        var second = A365InstrumentationValidator.EvaluateAsync(TrackedAction);

        await Task.WhenAll(first, second);

        maxObservedConcurrency.Should().Be(1);
    }

    [TestMethod]
    public async Task EvaluateAsync_LongLivedFilteredOutActiveSpan_DoesNotReportTimeout()
    {
        Activity? foreign = null;

        var report = await A365InstrumentationValidator.EvaluateAsync(
            () =>
            {
                using var source = new ActivitySource(
                    nameof(EvaluateAsync_LongLivedFilteredOutActiveSpan_DoesNotReportTimeout));
                foreign = source.StartActivity("foreign long-lived");
                foreign!.SetTag(OpenTelemetryConstants.GenAiOperationNameKey, "chat");
                return Task.CompletedTask;
            },
            options =>
            {
                options.SpanCompletionTimeout = TimeSpan.FromSeconds(2);
                options.SpanFilter = span => span.DisplayName != "foreign long-lived";
            });

        try
        {
            report.SessionFindings.Should().NotContain(f =>
                f.RuleId == A365ValidationRuleIds.SpanCompletionTimeout);
        }
        finally
        {
            foreign?.Dispose();
        }
    }

    [TestMethod]
    public void BuildReport_TimedOutWithNoActiveSpanAtDeadline_AddsGenericTimeoutFinding()
    {
        // Deterministic coverage for the case where CompleteAsync reaches its
        // deadline without ever observing a full quiet period (this can
        // happen because of continuous eligible activity churn, but equally
        // because of a short configured SpanCompletionTimeout or a residual
        // window too short to qualify -- see BuildReport), but no activity
        // happens to be active at the exact final instant. The capture
        // result still reports TimedOut = true with an empty TimedOutSpans
        // list; BuildReport must not silently treat that as a clean report.
        var options = new A365ValidationOptions();
        var captured = new A365CaptureResult(
            spans: Array.Empty<A365SpanSnapshot>(),
            timedOutSpans: Array.Empty<A365SpanSnapshot>(),
            timedOut: true);

        var report = A365InstrumentationValidator.BuildReport(captured, options);

        report.IsValid.Should().BeFalse();
        report.SessionFindings.Should().Contain(f =>
            f.RuleId == A365ValidationRuleIds.SpanCompletionTimeout &&
            f.Severity == A365ValidationSeverity.Error &&
            f.Status == A365ValidationFindingStatus.Active &&
            f.SpanId == null &&
            f.Message.Contains("250ms quiet period") &&
            !f.Message.Contains("continuous"));
    }

    [TestMethod]
    public void BuildReport_TimedOutSpanWithNoCompletedSpans_DoesNotAddNoSpansCapturedFinding()
    {
        // A recognized span that timed out while still active was in fact
        // captured -- it just never completed. BuildReport must report that
        // via SpanCompletionTimeout only, and must not additionally claim
        // (falsely) that no span was captured at all.
        var options = new A365ValidationOptions();
        var timedOutSpan = new A365SpanSnapshot(
            "0123456789abcdef0123456789abcdef",
            "0123456789abcdef",
            "stuck chat",
            "Customer.Agent",
            "chat",
            new Dictionary<string, object?>());
        var captured = new A365CaptureResult(
            spans: Array.Empty<A365SpanSnapshot>(),
            timedOutSpans: new[] { timedOutSpan },
            timedOut: true);

        var report = A365InstrumentationValidator.BuildReport(captured, options);

        report.SessionFindings.Should().NotContain(f =>
            f.RuleId == A365ValidationRuleIds.NoSpansCaptured);
        report.SessionFindings.Should().ContainSingle(f =>
            f.RuleId == A365ValidationRuleIds.SpanCompletionTimeout &&
            f.SpanId == timedOutSpan.SpanId);
    }

    [TestMethod]
    public async Task EvaluateAsync_ValidManualScopes_PassCertification()
    {
        using var provider = Sdk.CreateTracerProviderBuilder()
            .AddSource(OpenTelemetryConstants.SourceName)
            .AddProcessor(new ActivityProcessor())
            .Build();

        var report = await A365InstrumentationValidator.EvaluateAsync(() =>
        {
            // Only the identity AgentDetails needed by the scopes directly (AgentId) is passed
            // in. Every other certification attribute -- tenant, agent name/description,
            // agentic user id/email, blueprint id -- is supplied exclusively through
            // BaggageBuilder, so a passing report can only be explained by
            // ActivityProcessor.OnStart actually coalescing baggage onto each span (rather
            // than the scopes' direct-from-AgentDetails tagging, which is exercised by
            // CreateCertificationAgentDetails() in the other tests below).
            var agent = new AgentDetails(agentId: "agent");
            var user = new UserDetails(
                "user-id",
                "user@example.com",
                "User Name");
            var request = new Request(
                "hello",
                sessionId: "session",
                conversationId: "conversation");

            using (new BaggageBuilder()
                .TenantId("tenant")
                .AgentName("Weather agent")
                .AgentDescription("Answers weather questions")
                .AgenticUserId("agent-user")
                .AgenticUserEmail("agent@example.com")
                .AgentBlueprintId("blueprint")
                .Build())
            {
                using (InvokeAgentScope.Start(
                    request,
                    new InvokeAgentScopeDetails(new Uri("https://example.com")),
                    agent,
                    new CallerDetails(user)))
                {
                }

                using (InferenceScope.Start(
                    request,
                    new InferenceCallDetails(
                        InferenceOperationType.Chat,
                        "gpt-4.1",
                        "openai"),
                    agent,
                    user))
                {
                }

                using (ExecuteToolScope.Start(
                    request,
                    new ToolCallDetails("weather", "{}"),
                    agent,
                    user))
                {
                }

                using (OutputScope.Start(
                    request,
                    new Response(new[] { "sunny" }),
                    agent,
                    user))
                {
                }

                using (ApplyGuardrailScope.Start(
                    new GuardrailDetails(
                        GuardrailTargetType.LlmInput,
                        GuardrailDecisionType.Allow),
                    agent,
                    request,
                    user))
                {
                }
            }

            return Task.CompletedTask;
        });

        report.EnsureValid();
        report.Spans.Should().HaveCount(5);

        // Every one of the five manual scopes must carry the baggage-derived certification
        // attributes plus the ActivityProcessor's own SDK identity tags -- proof the real
        // ActivityProcessor.OnStart path ran for each span, not just that the report happens
        // to be valid (which minimal AgentDetails alone could not achieve).
        foreach (var spanResult in report.Spans)
        {
            var attributes = spanResult.Span.Attributes;
            attributes.Should().ContainKey(OpenTelemetryConstants.TenantIdKey)
                .WhoseValue.Should().Be("tenant");
            attributes.Should().ContainKey(OpenTelemetryConstants.GenAiAgentNameKey)
                .WhoseValue.Should().Be("Weather agent");
            attributes.Should().ContainKey(OpenTelemetryConstants.GenAiAgentDescriptionKey)
                .WhoseValue.Should().Be("Answers weather questions");
            attributes.Should().ContainKey(OpenTelemetryConstants.AgentAUIDKey)
                .WhoseValue.Should().Be("agent-user");
            attributes.Should().ContainKey(OpenTelemetryConstants.AgentEmailKey)
                .WhoseValue.Should().Be("agent@example.com");
            attributes.Should().ContainKey(OpenTelemetryConstants.AgentBlueprintIdKey)
                .WhoseValue.Should().Be("blueprint");
            attributes.Should().ContainKey(OpenTelemetryConstants.TelemetrySdkNameKey)
                .WhoseValue.Should().Be(OpenTelemetryConstants.TelemetrySdkNameValue);
        }
    }

    [TestMethod]
    public async Task EvaluateAsync_AgentFrameworkAutoInstrumentation_EnrichesAndPassesCertification()
    {
        var services = new ServiceCollection();
        services.AddOpenTelemetry()
            .UseAgentFramework()
            .WithTracing(tracing => tracing
                .AddSource(AgentFrameworkConstants.DefaultSource)
                .AddProcessor(new ActivityProcessor()));

        using var serviceProvider = services.BuildServiceProvider();
        serviceProvider.GetService<TracerProvider>();

        // Raw Agent Framework wire format ({role, parts:[{type, content}], ...}). The input
        // message carries a participant name plus an unrecognized property, and the output
        // message deliberately omits finish_reason. Only
        // AgentFrameworkSpanProcessor.OnEnd -> AgentFrameworkMessageMapper reconstructs these
        // into the A365 ChatMessage/OutputMessage shape: the unrecognized property can only
        // disappear, and finish_reason can only default to "stop", if that mapping actually
        // executed -- generic ActivityProcessor enrichment never touches these tags.
        const string rawInputMessages =
            "[{\"role\":\"user\",\"name\":\"end-user\"," +
            "\"parts\":[{\"type\":\"text\",\"content\":\"What is the weather in Seattle?\"}]," +
            "\"unsupported_field\":\"should-be-dropped\"}]";
        const string rawOutputMessages =
            "[{\"role\":\"assistant\"," +
            "\"parts\":[{\"type\":\"text\",\"content\":\"It is sunny in Seattle.\"}]}]";

        var report = await A365InstrumentationValidator.EvaluateAsync(() =>
        {
            using var source = new ActivitySource(AgentFrameworkConstants.DefaultSource);

            using (new BaggageBuilder()
                .TenantId("tenant")
                .AgentId("agent")
                .AgentName("Weather agent")
                .AgentDescription("Answers weather questions")
                .AgenticUserId("agent-user")
                .AgenticUserEmail("agent@example.com")
                .AgentBlueprintId("blueprint")
                .Build())
            {
                var tags = new ActivityTagsCollection
                {
                    {
                        OpenTelemetryConstants.GenAiOperationNameKey,
                        OpenTelemetryConstants.InvokeAgentOperationName
                    },
                    { OpenTelemetryConstants.UserIdKey, "user-id" },
                    { OpenTelemetryConstants.UserNameKey, "User Name" },
                    { OpenTelemetryConstants.UserEmailKey, "user@example.com" },
                    { OpenTelemetryConstants.GenAiInputMessagesKey, rawInputMessages },
                    { OpenTelemetryConstants.GenAiOutputMessagesKey, rawOutputMessages },
                };

                using var activity = source.StartActivity(
                    "invoke_agent WeatherAgent",
                    ActivityKind.Internal,
                    default(ActivityContext),
                    tags);
                activity.Should().NotBeNull();
            }

            return Task.CompletedTask;
        });

        // The synthetic activity is tagged with gen_ai.operation.name = invoke_agent
        // (the operation actually recognized by the capture session for this span),
        // not gen_ai.operation.name = chat.
        var spanResult = report.Spans.Should().ContainSingle(span =>
            string.Equals(
                span.Span.OperationName,
                OpenTelemetryConstants.InvokeAgentOperationName,
                StringComparison.OrdinalIgnoreCase)).Which;

        var attributes = spanResult.Span.Attributes;

        // Prove AgentFrameworkSpanProcessor.OnEnd actually ran: the final tag values are the
        // re-serialized A365 messages, not a pass-through of the raw Agent Framework strings.
        var finalInput = attributes[OpenTelemetryConstants.GenAiInputMessagesKey]!.ToString()!;
        finalInput.Should().NotBe(rawInputMessages);
        finalInput.Should().Contain("\"role\":\"user\"");
        finalInput.Should().Contain("\"content\":\"What is the weather in Seattle?\"");
        finalInput.Should().Contain("\"name\":\"end-user\"");
        finalInput.Should().NotContain("unsupported_field");

        var finalOutput = attributes[OpenTelemetryConstants.GenAiOutputMessagesKey]!.ToString()!;
        finalOutput.Should().NotBe(rawOutputMessages);
        finalOutput.Should().Contain("\"role\":\"assistant\"");
        finalOutput.Should().Contain("\"content\":\"It is sunny in Seattle.\"");
        // finish_reason is absent from the raw payload; OutputMessage defaults it to "stop",
        // so its presence here can only be explained by the mapper having run.
        finalOutput.Should().Contain("\"finish_reason\":\"stop\"");

        // Baggage-derived certification metadata must also be present on the final span.
        attributes.Should().ContainKey(OpenTelemetryConstants.TenantIdKey)
            .WhoseValue.Should().Be("tenant");
        attributes.Should().ContainKey(OpenTelemetryConstants.GenAiAgentNameKey)
            .WhoseValue.Should().Be("Weather agent");
        attributes.Should().ContainKey(OpenTelemetryConstants.GenAiAgentDescriptionKey)
            .WhoseValue.Should().Be("Answers weather questions");
        attributes.Should().ContainKey(OpenTelemetryConstants.AgentAUIDKey)
            .WhoseValue.Should().Be("agent-user");
        attributes.Should().ContainKey(OpenTelemetryConstants.AgentEmailKey)
            .WhoseValue.Should().Be("agent@example.com");
        attributes.Should().ContainKey(OpenTelemetryConstants.AgentBlueprintIdKey)
            .WhoseValue.Should().Be("blueprint");

        report.EnsureValid();
    }

    [TestMethod]
    public async Task EvaluateAsync_AnonymousInvokeAgent_SuppressesInvokeUserRules()
    {
        const string suppressionReason =
            "Anonymous entry point - this endpoint intentionally accepts unauthenticated callers.";

        var report = await A365InstrumentationValidator.EvaluateAsync(
            () =>
            {
                var agent = CreateCertificationAgentDetails();
                var request = new Request(
                    "hello",
                    sessionId: "session",
                    conversationId: "conversation");

                using (InvokeAgentScope.Start(
                    request,
                    new InvokeAgentScopeDetails(new Uri("https://example.com")),
                    agent))
                {
                }

                return Task.CompletedTask;
            },
            options =>
            {
                options.Suppress(
                    A365ValidationRuleIds.InvokeUserIdRequired,
                    OpenTelemetryConstants.InvokeAgentOperationName,
                    suppressionReason);
                options.Suppress(
                    A365ValidationRuleIds.InvokeUserNameRequired,
                    OpenTelemetryConstants.InvokeAgentOperationName,
                    suppressionReason);
                options.Suppress(
                    A365ValidationRuleIds.InvokeUserEmailRequired,
                    OpenTelemetryConstants.InvokeAgentOperationName,
                    suppressionReason);
            });

        report.IsValid.Should().BeTrue();
        report.SuppressedFindingCount.Should().Be(3);
        report.Spans.Single().Findings.Should().OnlyContain(
            finding => finding.Status == A365ValidationFindingStatus.Suppressed);
        report.ToString().Should().Contain("Anonymous entry point");
    }

    private static AgentDetails CreateCertificationAgentDetails()
    {
        return new AgentDetails(
            agentId: "agent",
            agentName: "Weather agent",
            agentDescription: "Answers weather questions",
            agenticUserId: "agent-user",
            agenticUserEmail: "agent@example.com",
            agentBlueprintId: "blueprint",
            tenantId: "tenant");
    }

    private static void SetValidChatAttributes(Activity activity)
    {
        SetValidChatAttributes(activity, includeExportIdentity: true);
    }

    /// <summary>
    /// Puts the operation name and the export routing identity -- the only
    /// values the A365 exporter resolves from tag-or-baggage -- into Activity
    /// baggage, and nothing into tags. Spans built this way are recognized and
    /// routable, but carry no serialized payload attributes at all.
    /// </summary>
    private static void SetBaggageOnlyCommonAttributes(
        Activity activity,
        string operationName)
    {
        activity.AddBaggage(OpenTelemetryConstants.GenAiOperationNameKey, operationName);
        activity.AddBaggage(OpenTelemetryConstants.TenantIdKey, "tenant");
        activity.AddBaggage(OpenTelemetryConstants.GenAiAgentIdKey, "agent");
        activity.AddBaggage(OpenTelemetryConstants.GenAiAgentNameKey, "Weather agent");
        activity.AddBaggage(
            OpenTelemetryConstants.GenAiAgentDescriptionKey,
            "Answers weather questions");
        activity.AddBaggage(OpenTelemetryConstants.AgentAUIDKey, "agent-user");
        activity.AddBaggage(OpenTelemetryConstants.AgentEmailKey, "agent@example.com");
        activity.AddBaggage(OpenTelemetryConstants.AgentBlueprintIdKey, "blueprint");
    }

    private static void SetValidChatAttributes(
        Activity activity,
        bool includeExportIdentity)
    {
        activity.SetTag(OpenTelemetryConstants.GenAiOperationNameKey, "chat");

        if (includeExportIdentity)
        {
            activity.SetTag(OpenTelemetryConstants.TenantIdKey, "tenant");
            activity.SetTag(OpenTelemetryConstants.GenAiAgentIdKey, "agent");
        }

        activity.SetTag(OpenTelemetryConstants.GenAiAgentNameKey, "Weather agent");
        activity.SetTag(
            OpenTelemetryConstants.GenAiAgentDescriptionKey,
            "Answers weather questions");
        activity.SetTag(OpenTelemetryConstants.AgentAUIDKey, "agent-user");
        activity.SetTag(
            OpenTelemetryConstants.AgentEmailKey,
            "agent@example.com");
        activity.SetTag(
            OpenTelemetryConstants.AgentBlueprintIdKey,
            "blueprint");
        activity.SetTag(OpenTelemetryConstants.GenAiRequestModelKey, "gpt-4.1");
        activity.SetTag(OpenTelemetryConstants.GenAiProviderNameKey, "openai");
    }
}
