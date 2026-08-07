// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using FluentAssertions;
using Microsoft.Agents.A365.Observability.Runtime.Common;
using Microsoft.Agents.A365.Observability.Runtime.Tracing.Exporters;
using Microsoft.Agents.A365.Observability.Runtime.Tracing.Scopes;
using Microsoft.Extensions.Logging.Abstractions;
using OpenTelemetry;
using OpenTelemetry.Resources;
using System.Diagnostics;
using System.Net;
using System.Reflection;
using System.Threading;

namespace Microsoft.Agents.A365.Observability.Tests.Tracing.Exporters;

[TestClass]
public sealed class Agent365ExporterRetryTests
{
    private readonly List<TimeSpan> _delays = new();
    private DateTimeOffset _now = new(2026, 8, 7, 12, 0, 0, TimeSpan.Zero);

    private Agent365ExporterCore CreateCore()
    {
        return new Agent365ExporterCore(
            new ExportFormatter(NullLogger<ExportFormatter>.Instance),
            NullLogger<Agent365ExporterCore>.Instance,
            (delay, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                _delays.Add(delay);
                return Task.CompletedTask;
            },
            () => _now);
    }

    private static Activity CreateActivity()
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "Agent365Sdk",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStarted = _ => { },
            ActivityStopped = _ => { }
        };
        ActivitySource.AddActivityListener(listener);

        var source = new ActivitySource("Agent365Sdk");
        var activity = source.StartActivity("test-span", ActivityKind.Client);
        if (activity == null)
            throw new InvalidOperationException("Failed to start activity.");

        activity.SetTag(OpenTelemetryConstants.GenAiOperationNameKey, "invoke_agent");
        activity.SetTag(OpenTelemetryConstants.TenantIdKey, "tenant-1");
        activity.SetTag(OpenTelemetryConstants.GenAiAgentIdKey, "agent-1");
        activity.Stop();
        return activity;
    }

    private async Task<ExportResult> ExportOneAsync(
        Agent365ExporterCore core,
        Func<HttpRequestMessage, Task<HttpResponseMessage>> sendAsync,
        CancellationToken cancellationToken = default)
    {
        var activity = CreateActivity();
        var groups = core.PartitionByIdentity(new[] { activity });
        var options = new Agent365ExporterOptions
        {
            DomainResolver = _ => "api.example.com",
            TokenResolver = (_, _) => Task.FromResult<string?>("test-token")
        };
        return await core.ExportBatchCoreAsync(
            groups: groups,
            resource: ResourceBuilder.CreateEmpty().Build(),
            options: options,
            tokenResolver: (agentId, tenantId) => Task.FromResult<string?>("test-token"),
            sendAsync: sendAsync,
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    [DataTestMethod]
    [DataRow(HttpStatusCode.RequestTimeout)]
    [DataRow((HttpStatusCode)429)]
    [DataRow(HttpStatusCode.InternalServerError)]
    [DataRow(HttpStatusCode.ServiceUnavailable)]
    public async Task RetryableResponseRetriesThenSucceeds(HttpStatusCode firstStatus)
    {
        var attempts = 0;
        var core = CreateCore();

        var result = await ExportOneAsync(core, _ =>
        {
            attempts++;
            return Task.FromResult(new HttpResponseMessage(
                attempts == 1 ? firstStatus : HttpStatusCode.OK));
        });

        result.Should().Be(ExportResult.Success);
        attempts.Should().Be(2);
        _delays.Should().ContainSingle().Which.Should().Be(TimeSpan.FromMilliseconds(500));
    }

    [TestMethod]
    public async Task ExhaustedResponsesMakeFourAttempts()
    {
        var attempts = 0;
        var core = CreateCore();

        var result = await ExportOneAsync(core, _ =>
        {
            attempts++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        });

        result.Should().Be(ExportResult.Failure);
        attempts.Should().Be(4);
        _delays.Should().Equal(
            TimeSpan.FromMilliseconds(500),
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(2));
    }

    [TestMethod]
    public async Task HttpRequestExceptionRetries()
    {
        var attempts = 0;
        var core = CreateCore();

        var result = await ExportOneAsync(core, _ =>
        {
            attempts++;
            return attempts == 1
                ? Task.FromException<HttpResponseMessage>(new HttpRequestException("network"))
                : Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });

        result.Should().Be(ExportResult.Success);
        attempts.Should().Be(2);
    }

    [TestMethod]
    public async Task TimeoutRetriesWhenCallerWasNotCanceled()
    {
        var attempts = 0;
        var core = CreateCore();

        var result = await ExportOneAsync(core, _ =>
        {
            attempts++;
            return attempts == 1
                ? Task.FromException<HttpResponseMessage>(new TaskCanceledException("timeout"))
                : Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });

        result.Should().Be(ExportResult.Success);
        attempts.Should().Be(2);
    }

    [TestMethod]
    public async Task CallerCancellationStopsWithoutRetry()
    {
        var attempts = 0;
        var core = CreateCore();
        using var cts = new CancellationTokenSource();

        Func<Task> action = async () => await ExportOneAsync(
            core,
            _ =>
            {
                attempts++;
                cts.Cancel();
                return Task.FromCanceled<HttpResponseMessage>(cts.Token);
            },
            cts.Token);

        await action.Should().ThrowAsync<OperationCanceledException>();
        attempts.Should().Be(1);
        _delays.Should().BeEmpty();
    }

    [TestMethod]
    public async Task NonRetryableResponseMakesOneAttempt()
    {
        var attempts = 0;
        var result = await ExportOneAsync(CreateCore(), _ =>
        {
            attempts++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden));
        });

        result.Should().Be(ExportResult.Failure);
        attempts.Should().Be(1);
    }

    [TestMethod]
    public async Task EachAttemptUsesFreshRequestAndContent()
    {
        var requests = new List<HttpRequestMessage>();
        var contents = new List<HttpContent?>();

        var result = await ExportOneAsync(CreateCore(), request =>
        {
            requests.Add(request);
            contents.Add(request.Content);
            var status = requests.Count == 1
                ? HttpStatusCode.ServiceUnavailable
                : HttpStatusCode.OK;
            return Task.FromResult(new HttpResponseMessage(status));
        });

        result.Should().Be(ExportResult.Success);
        requests.Should().HaveCount(2);
        requests[0].Should().NotBeSameAs(requests[1]);
        contents[0].Should().NotBeSameAs(contents[1]);
    }
}
