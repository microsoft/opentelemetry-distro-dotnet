// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics;
using System.Net.Http;
using System.Reflection;
using FluentAssertions;
using Microsoft.Agents.A365.Observability.Runtime.Common;
using Microsoft.Agents.A365.Observability.Runtime.Tracing.Exporters;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTelemetry;
using OpenTelemetry.Resources;

namespace Microsoft.OpenTelemetry.Agent365.S2S.Demo.Tests;

/// <summary>
/// Reproduces a token-resolver failure end-to-end through the real
/// <see cref="Agent365ExporterCore"/>/<see cref="Agent365Exporter"/> logging path (no real
/// credentials required — the resolver is a fake that throws) and proves that wiring
/// <see cref="SafeConsoleLoggerProvider"/> as the sample's console logger, instead of
/// <c>AddSimpleConsole</c>, prevents the resulting exception chain and any secret it carries
/// from ever reaching console output.
/// </summary>
[TestClass]
public sealed class SafeConsoleLoggerResolverFailureTests
{
    private const string MarkerSecret = "marker-secret-b7e21c4d";

    [TestMethod]
    public void Export_WhenTokenResolverThrows_SafeConsoleLoggerOmitsExceptionChain()
    {
        var writer = new StringWriter();
        using var provider = new SafeConsoleLoggerProvider(writer);
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(provider));

        var core = new Agent365ExporterCore(
            new ExportFormatter(loggerFactory.CreateLogger<ExportFormatter>()),
            loggerFactory.CreateLogger<Agent365ExporterCore>());

        var options = new Agent365ExporterOptions
        {
            TokenResolver = (_, _) => throw CreateResolverFailureWithMarker(),
        };

        using var httpClient = new HttpClient(new NeverInvokedHandler());
        using var exporter = new Agent365Exporter(
            core,
            loggerFactory.CreateLogger<Agent365Exporter>(),
            options,
            ResourceBuilder.CreateEmpty().AddService("resolver-failure-test").Build(),
            httpClient);

        using var activity = CreateActivity("tenant-fail", "agent-fail");
        var batch = CreateBatch(activity);

        // The exporter treats a thrown resolver as a transient outage and attempts to persist
        // for retry (which fails with default in-memory storage disabled); the export result
        // itself is not the subject of this test — the console output is.
        exporter.Export(in batch);

        var output = writer.ToString();

        // The safe formatted log line (level, category, message/args) must still be present.
        output.Should().Contain("fail");
        output.Should().Contain(nameof(Agent365ExporterCore));
        output.Should().Contain("TokenResolver threw for agent agent-fail tenant tenant-fail.");

        // No part of the resolver's exception chain may leak: message, type, marker secret,
        // or stack trace/inner-exception framing.
        output.Should().NotContain(MarkerSecret);
        output.Should().NotContain(nameof(InvalidOperationException));
        output.Should().NotContain(nameof(ApplicationException));
        output.Should().NotContain("outer resolver failure");
        output.Should().NotContain("inner resolver failure");
        output.Should().NotContain("--- End of inner exception stack trace ---");
        output.Should().NotContain("at Microsoft.OpenTelemetry");
    }

    private static Exception CreateResolverFailureWithMarker()
    {
        try
        {
            try
            {
                throw new InvalidOperationException(
                    $"inner resolver failure containing {MarkerSecret}");
            }
            catch (Exception inner)
            {
                throw new ApplicationException(
                    $"outer resolver failure containing {MarkerSecret}", inner);
            }
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    private static Activity CreateActivity(string tenantId, string agentId)
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "Agent365Sdk.ResolverFailureTests",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStarted = _ => { },
            ActivityStopped = _ => { },
        };
        ActivitySource.AddActivityListener(listener);

        var source = new ActivitySource("Agent365Sdk.ResolverFailureTests");
        var activity = source.StartActivity("test-span", ActivityKind.Client)
            ?? throw new InvalidOperationException("Failed to start activity. Ensure an ActivityListener is registered.");

        // Tag keys mirror the internal OpenTelemetryConstants values (gen_ai.operation.name,
        // microsoft.tenant.id, gen_ai.agent.id); that type is internal to the core assembly
        // and not visible here, so the literal semantic-convention keys are used directly.
        activity.SetTag("gen_ai.operation.name", "invoke_agent");
        activity.SetTag("microsoft.tenant.id", tenantId);
        activity.SetTag("gen_ai.agent.id", agentId);
        activity.Stop();
        return activity;
    }

    private static Batch<Activity> CreateBatch(params Activity[] activities)
    {
        // Batch<T> has an internal ctor with no InternalsVisibleTo grant to this test
        // assembly; use reflection exactly as the core exporter test suite does.
        var batchType = typeof(Batch<Activity>);
        var ctor = batchType
            .GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance)
            .FirstOrDefault();

        if (ctor is null)
        {
            Assert.Inconclusive("Could not locate internal Batch<Activity> constructor - OpenTelemetry version changed.");
        }

        var circularBufferType = batchType.Assembly.GetType("OpenTelemetry.Internal.CircularBuffer`1")!
            .MakeGenericType(typeof(Activity));
        var buffer = Activator.CreateInstance(circularBufferType, activities.Length);

        if (buffer is null)
        {
            Assert.Inconclusive("Could not create CircularBuffer<Activity> - Activator.CreateInstance returned null.");
        }

        var addMethod = circularBufferType.GetMethod("Add");
        foreach (var act in activities)
        {
            addMethod!.Invoke(buffer, new object[] { act });
        }

        object? batchObj;
        try
        {
            batchObj = ctor!.Invoke(new object[] { buffer!, activities.Length });
        }
        catch (TargetParameterCountException)
        {
            Assert.Inconclusive("Unexpected Batch<Activity> constructor shape - adjust test helper.");
            throw;
        }

        return (Batch<Activity>)batchObj!;
    }

    private sealed class NeverInvokedHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException(
                "The HTTP send path must not be reached when the token resolver throws.");
    }
}
