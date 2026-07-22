// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.OpenTelemetry.Tests.GenAI.MainAgent;

using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.OpenTelemetry.GenAI.MainAgent;
using global::OpenTelemetry.Logs;
using static Microsoft.Agents.A365.Observability.Runtime.Tracing.Scopes.OpenTelemetryConstants;

/// <summary>
/// Tests for <see cref="GenAIMainAgentLogRecordProcessor"/>. Verifies that
/// <c>microsoft.gen_ai.main_agent.*</c> and <c>microsoft.foundry.project.id</c>
/// attributes on the ambient <see cref="Activity"/> are merged onto emitted
/// <see cref="LogRecord"/> instances.
/// </summary>
[TestClass]
public sealed class GenAIMainAgentLogRecordProcessorTests
{
    private const string TestActivitySourceName = "Microsoft.OpenTelemetry.Tests.GenAIMainAgent.Logs";
    private const string TestProjectArmId =
        "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/rg/"
        + "providers/Microsoft.CognitiveServices/accounts/acct/projects/proj";

    [TestMethod]
    public void OnEnd_WithNoCurrentActivity_LeavesLogRecordUnchanged()
    {
        var exported = new List<LogRecord>();
        using var loggerFactory = BuildLoggerFactory(exported);
        var logger = loggerFactory.CreateLogger("no-activity");

        // Ensure no ambient activity while the log is emitted.
        Activity.Current.Should().BeNull();
        logger.LogInformation("plain log");

        FlushLogs(loggerFactory);
        exported.Should().HaveCount(1);
        var attributes = exported[0].Attributes ?? new List<KeyValuePair<string, object?>>();
        attributes.Should().NotContain(kvp => kvp.Key.StartsWith(GenAiMainAgentAttributePrefix, System.StringComparison.Ordinal));
        attributes.Should().NotContain(kvp => kvp.Key == GenAiFoundryProjectIdKey);
    }

    [TestMethod]
    public void OnEnd_CopiesMainAgentAttributesFromCurrentActivity()
    {
        var exported = new List<LogRecord>();
        using var loggerFactory = BuildLoggerFactory(exported);
        var logger = loggerFactory.CreateLogger("with-main-agent");

        using var source = new ActivitySource(TestActivitySourceName);
        using var listener = CreateAlwaysOnListener();
        using var activity = source.StartActivity("invoke_agent");
        activity.Should().NotBeNull();
        activity!.SetTag(GenAiMainAgentNameKey, "weather-agent");
        activity.SetTag(GenAiMainAgentIdKey, "weather-agent-id");
        activity.SetTag(GenAiMainAgentConversationIdKey, "conv-xyz");

        logger.LogInformation("in-scope log");

        FlushLogs(loggerFactory);
        var record = exported.Should().ContainSingle().Subject;
        var attributes = (record.Attributes ?? new List<KeyValuePair<string, object?>>()).ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
        attributes.Should().ContainKey(GenAiMainAgentNameKey).WhoseValue.Should().Be("weather-agent");
        attributes.Should().ContainKey(GenAiMainAgentIdKey).WhoseValue.Should().Be("weather-agent-id");
        attributes.Should().ContainKey(GenAiMainAgentConversationIdKey).WhoseValue.Should().Be("conv-xyz");
    }

    [TestMethod]
    public void OnEnd_CopiesProjectIdAttributesFromCurrentActivity()
    {
        var exported = new List<LogRecord>();
        using var loggerFactory = BuildLoggerFactory(exported);
        var logger = loggerFactory.CreateLogger("with-project-id");

        using var source = new ActivitySource(TestActivitySourceName);
        using var listener = CreateAlwaysOnListener();
        using var activity = source.StartActivity("invoke_agent");
        activity.Should().NotBeNull();
        activity!.SetTag(GenAiFoundryProjectIdKey, TestProjectArmId);
        activity.SetTag(GenAiAzureAiProjectIdKey, "azure-ai-project-id");

        logger.LogInformation("in-scope log");

        FlushLogs(loggerFactory);
        var record = exported.Should().ContainSingle().Subject;
        var attributes = (record.Attributes ?? new List<KeyValuePair<string, object?>>()).ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
        attributes.Should().ContainKey(GenAiFoundryProjectIdKey).WhoseValue.Should().Be(TestProjectArmId);
        attributes.Should().ContainKey(GenAiAzureAiProjectIdKey).WhoseValue.Should().Be("azure-ai-project-id");
    }

    [TestMethod]
    public void OnEnd_ActivityWithoutRelevantTags_DoesNotMutateLog()
    {
        var exported = new List<LogRecord>();
        using var loggerFactory = BuildLoggerFactory(exported);
        var logger = loggerFactory.CreateLogger("irrelevant-tags");

        using var source = new ActivitySource(TestActivitySourceName);
        using var listener = CreateAlwaysOnListener();
        using var activity = source.StartActivity("chat");
        activity.Should().NotBeNull();
        activity!.SetTag("http.request.method", "POST");
        activity.SetTag(GenAiAgentNameKey, "not-main-agent"); // not in the propagation set

        logger.LogInformation("in-scope log");

        FlushLogs(loggerFactory);
        var record = exported.Should().ContainSingle().Subject;
        var attributes = record.Attributes ?? new List<KeyValuePair<string, object?>>();
        attributes.Should().NotContain(kvp => kvp.Key.StartsWith(GenAiMainAgentAttributePrefix, System.StringComparison.Ordinal));
        attributes.Should().NotContain(kvp => kvp.Key == GenAiFoundryProjectIdKey);
    }

    [TestMethod]
    public void OnEnd_PreservesExistingLogAttributes()
    {
        var exported = new List<LogRecord>();
        using var loggerFactory = BuildLoggerFactory(exported);
        var logger = loggerFactory.CreateLogger("merge-attrs");

        using var source = new ActivitySource(TestActivitySourceName);
        using var listener = CreateAlwaysOnListener();
        using var activity = source.StartActivity("invoke_agent");
        activity.Should().NotBeNull();
        activity!.SetTag(GenAiMainAgentNameKey, "weather-agent");
        activity.SetTag(GenAiFoundryProjectIdKey, TestProjectArmId);

        // Emit a structured log so the message template placeholders become attributes.
        logger.LogInformation("hello {User}", "alice");

        FlushLogs(loggerFactory);
        var record = exported.Should().ContainSingle().Subject;
        var attributes = (record.Attributes ?? new List<KeyValuePair<string, object?>>()).ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
        attributes.Should().ContainKey("User").WhoseValue.Should().Be("alice");
        attributes.Should().ContainKey(GenAiMainAgentNameKey).WhoseValue.Should().Be("weather-agent");
        attributes.Should().ContainKey(GenAiFoundryProjectIdKey).WhoseValue.Should().Be(TestProjectArmId);
    }

    // ── helpers ─────────────────────────────────────────────────────────────

    private static ILoggerFactory BuildLoggerFactory(List<LogRecord> exported)
    {
        return LoggerFactory.Create(builder =>
        {
            builder.AddOpenTelemetry(options =>
            {
                options.IncludeFormattedMessage = true;
                options.AddProcessor(new GenAIMainAgentLogRecordProcessor());
                options.AddInMemoryExporter(exported);
            });
        });
    }

    private static void FlushLogs(ILoggerFactory loggerFactory)
    {
        // Dispose forces the OpenTelemetry logger provider to flush its processors.
        loggerFactory.Dispose();
    }

    private static ActivityListener CreateAlwaysOnListener()
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == TestActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            SampleUsingParentId = (ref ActivityCreationOptions<string> _) => ActivitySamplingResult.AllDataAndRecorded,
        };
        ActivitySource.AddActivityListener(listener);
        return listener;
    }
}
