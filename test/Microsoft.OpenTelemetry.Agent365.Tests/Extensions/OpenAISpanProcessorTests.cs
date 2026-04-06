// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.OpenTelemetry.Agent365.Extensions.OpenAI;
using Microsoft.OpenTelemetry.Agent365.Tracing.Scopes;
using global::OpenTelemetry;
using System.Diagnostics;

namespace Microsoft.OpenTelemetry.Agent365.Tests.Extensions;

[TestClass]
public class OpenAISpanProcessorTests
{
    private static readonly ActivitySource OpenAISource = new("OpenAI.Test");

    static OpenAISpanProcessorTests()
    {
        ActivitySource.AddActivityListener(new ActivityListener
        {
            ShouldListenTo = _ => true,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
        });
    }

    [TestMethod]
    public void OnEnd_ProcessesChatOperation()
    {
        var processor = new OpenAISpanProcessor();

        using var activity = OpenAISource.StartActivity("chat gpt-4");
        Assert.IsNotNull(activity);
        activity.SetTag(OpenTelemetryConstants.GenAiOperationNameKey, "chat");

        processor.OnEnd(activity);

        // Processor is a passthrough for chat — should not throw or modify
        Assert.AreEqual("chat", activity.GetTagItem(OpenTelemetryConstants.GenAiOperationNameKey)?.ToString());
    }

    [TestMethod]
    public void OnEnd_IgnoresNonOpenAISource()
    {
        var processor = new OpenAISpanProcessor();
        var otherSource = new ActivitySource("SomeOtherSource");

        using var activity = otherSource.StartActivity("test");
        Assert.IsNotNull(activity);
        activity.SetTag(OpenTelemetryConstants.GenAiOperationNameKey, "chat");

        processor.OnEnd(activity);

        // Should not modify activities from other sources
        Assert.AreEqual("chat", activity.GetTagItem(OpenTelemetryConstants.GenAiOperationNameKey)?.ToString());
    }

    [TestMethod]
    public void OnEnd_HandlesActivityWithoutOperationName()
    {
        var processor = new OpenAISpanProcessor();

        using var activity = OpenAISource.StartActivity("unknown-op");
        Assert.IsNotNull(activity);
        // No gen_ai.operation.name tag set

        processor.OnEnd(activity);

        // Should not throw
        Assert.IsNull(activity.GetTagItem(OpenTelemetryConstants.GenAiOperationNameKey));
    }

    [TestMethod]
    public void OnStart_DoesNothing()
    {
        var processor = new OpenAISpanProcessor();

        using var activity = OpenAISource.StartActivity("chat");
        Assert.IsNotNull(activity);

        processor.OnStart(activity);

        // OnStart is a no-op — should not throw
    }
}
