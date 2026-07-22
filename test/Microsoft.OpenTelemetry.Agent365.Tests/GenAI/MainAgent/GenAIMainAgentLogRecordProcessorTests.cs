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

    [TestMethod]
    public void OnEnd_DoesNotDuplicateOrOverwriteExistingMainAgentAttributes()
    {
        var exported = new List<LogRecord>();
        using var loggerFactory = BuildLoggerFactory(exported);

        // Insert a processor BEFORE ours that seeds the log record with a
        // main-agent attribute the caller (or an upstream enricher) already set.
        // The activity carries a DIFFERENT value for the same key — the processor
        // must respect the pre-existing value and must not append a duplicate.
        var exportedWithSeed = new List<LogRecord>();
        using var seededFactory = LoggerFactory.Create(builder =>
        {
            builder.AddOpenTelemetry(options =>
            {
                options.IncludeFormattedMessage = true;
                options.AddProcessor(new SeedAttributeProcessor(
                    new KeyValuePair<string, object?>(GenAiMainAgentNameKey, "explicit-from-caller")));
                options.AddProcessor(new GenAIMainAgentLogRecordProcessor());
                options.AddInMemoryExporter(exportedWithSeed);
            });
        });
        var logger = seededFactory.CreateLogger("dedup-attrs");

        using var source = new ActivitySource(TestActivitySourceName);
        using var listener = CreateAlwaysOnListener();
        using var activity = source.StartActivity("invoke_agent");
        activity.Should().NotBeNull();
        activity!.SetTag(GenAiMainAgentNameKey, "from-activity");
        activity.SetTag(GenAiMainAgentIdKey, "from-activity-id"); // not seeded — should be added

        logger.LogInformation("dedup log");

        FlushLogs(seededFactory);
        var record = exportedWithSeed.Should().ContainSingle().Subject;
        var attributes = record.Attributes ?? new List<KeyValuePair<string, object?>>();

        attributes.Count(kvp => kvp.Key == GenAiMainAgentNameKey).Should().Be(
            1,
            because: "existing log-record keys must not be duplicated by the merge");
        attributes.Single(kvp => kvp.Key == GenAiMainAgentNameKey).Value.Should().Be(
            "explicit-from-caller",
            because: "log-record attributes take precedence over ambient activity values");
        attributes.Should().ContainSingle(kvp => kvp.Key == GenAiMainAgentIdKey)
            .Which.Value.Should().Be("from-activity-id",
                because: "keys not already present on the log record are still merged in");
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

    // Injects a caller-set attribute onto every log record before the
    // GenAIMainAgentLogRecordProcessor runs, to exercise the de-dup path.
    private sealed class SeedAttributeProcessor : global::OpenTelemetry.BaseProcessor<LogRecord>
    {
        private readonly KeyValuePair<string, object?>[] _seed;

        public SeedAttributeProcessor(params KeyValuePair<string, object?>[] seed)
        {
            _seed = seed;
        }

        public override void OnEnd(LogRecord data)
        {
            if (data == null)
            {
                return;
            }

            var merged = new List<KeyValuePair<string, object?>>();
            if (data.Attributes != null)
            {
                merged.AddRange(data.Attributes);
            }
            merged.AddRange(_seed);
            data.Attributes = merged;
        }
    }
}
