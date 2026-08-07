// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using FluentAssertions;
using Microsoft.Agents.A365.Observability.Runtime.Common;
using Microsoft.Agents.A365.Observability.Runtime.Tracing.Exporters;
using Microsoft.Agents.A365.Observability.Runtime.Tracing.Scopes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.OpenTelemetry.AzureMonitor.SdkStats;
using OpenTelemetry;
using OpenTelemetry.Resources;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading;

namespace Microsoft.Agents.A365.Observability.Tests.Tracing.Exporters;

[TestClass]
public sealed class Agent365ExporterRetryTests
{
    private readonly List<TimeSpan> _delays = new();
    private DateTimeOffset _now = new(2026, 8, 7, 12, 0, 0, TimeSpan.Zero);

    private Agent365ExporterCore CreateCore(ILogger<Agent365ExporterCore>? logger = null)
    {
        return new Agent365ExporterCore(
            new ExportFormatter(NullLogger<ExportFormatter>.Instance),
            logger ?? NullLogger<Agent365ExporterCore>.Instance,
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

    [TestMethod]
    public async Task CallerCancellationDuringHalfOpenExportDoesNotPermanentlyBlockNextProbe()
    {
        var core = CreateCore();
        using var cts = new CancellationTokenSource();

        var cbField = typeof(Agent365ExporterCore)
            .GetField("_circuitBreaker", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var cb = (Agent365CircuitBreaker)cbField.GetValue(core)!;

        for (var i = 0; i < Agent365CircuitBreaker.FailureThreshold; i++)
            cb.RecordTransientFailure();

        _now = _now.Add(Agent365CircuitBreaker.RecoveryTimeout).AddSeconds(1);
        cb.State.Should().Be(Agent365CircuitState.HalfOpen);

        Func<Task> cancelAction = async () => await ExportOneAsync(
            core,
            _ =>
            {
                cts.Cancel();
                return Task.FromCanceled<HttpResponseMessage>(cts.Token);
            },
            cts.Token);

        await cancelAction.Should().ThrowAsync<OperationCanceledException>();

        cb.State.Should().Be(Agent365CircuitState.HalfOpen);
        cb.TryAcquirePermit().Should().BeTrue();
    }

    [TestMethod]
    public async Task NonRetryableResponseDuringHalfOpenDoesNotPermanentlyBlockNextProbe()
    {
        var core = CreateCore();

        var cbField = typeof(Agent365ExporterCore)
            .GetField("_circuitBreaker", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var cb = (Agent365CircuitBreaker)cbField.GetValue(core)!;

        for (var i = 0; i < Agent365CircuitBreaker.FailureThreshold; i++)
            cb.RecordTransientFailure();

        _now = _now.Add(Agent365CircuitBreaker.RecoveryTimeout).AddSeconds(1);
        cb.State.Should().Be(Agent365CircuitState.HalfOpen);

        var result = await ExportOneAsync(core, _ =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden)));

        result.Should().Be(ExportResult.Failure);
        cb.State.Should().Be(Agent365CircuitState.HalfOpen);
        cb.TryAcquirePermit().Should().BeTrue();
    }

    [TestMethod]
    public async Task UnexpectedExceptionDuringHalfOpenReleasesPermitForNextProbe()
    {
        var core = CreateCore();

        var cbField = typeof(Agent365ExporterCore)
            .GetField("_circuitBreaker", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var cb = (Agent365CircuitBreaker)cbField.GetValue(core)!;

        for (var i = 0; i < Agent365CircuitBreaker.FailureThreshold; i++)
            cb.RecordTransientFailure();

        _now = _now.Add(Agent365CircuitBreaker.RecoveryTimeout).AddSeconds(1);
        cb.State.Should().Be(Agent365CircuitState.HalfOpen);

        Func<Task> unexpected = async () => await ExportOneAsync(
            core,
            _ => throw new InvalidOperationException("unexpected"));

        // An exception that is neither a timeout nor an HttpRequestException bubbles out, but the
        // ownership-gated finally must still release the half-open probe this invocation acquired.
        await unexpected.Should().ThrowAsync<InvalidOperationException>();

        cb.State.Should().Be(Agent365CircuitState.HalfOpen);
        cb.TryAcquirePermit().Should().BeTrue();
    }

    [TestMethod]
    public async Task TwoRetryableResponsesThenSuccessProducesTwoRetriesAndOneSuccess()
    {
        DistroNetworkSdkStats.ResetForTesting();
        DistroNetworkSdkStats.Initialize("N/A", "1.0.0");
        try
        {
            var measurements = new List<(string instrument, long value, string? host)>();
            using var listener = CreateStatsListener(measurements);

            var attempts = 0;
            var result = await ExportOneAsync(CreateCore(), _ =>
            {
                attempts++;
                return Task.FromResult(new HttpResponseMessage(
                    attempts <= 2 ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK));
            });

            result.Should().Be(ExportResult.Success);
            attempts.Should().Be(3);
            // Filter by host "api" (from "api.example.com") to isolate this test's measurements.
            var ours = measurements.Where(m => m.host == "api").ToList();
            ours.Count(m => m.instrument == "Retry_Count").Should().Be(2);
            ours.Count(m => m.instrument == "Request_Success_Count").Should().Be(1);
            ours.Should().NotContain(m => m.instrument == "Request_Failure_Count");
        }
        finally
        {
            DistroNetworkSdkStats.ResetForTesting();
        }
    }

    [TestMethod]
    public async Task FourRetryableResponsesProducesThreeRetriesAndOneFinalFailure()
    {
        DistroNetworkSdkStats.ResetForTesting();
        DistroNetworkSdkStats.Initialize("N/A", "1.0.0");
        try
        {
            var measurements = new List<(string instrument, long value, string? host)>();
            using var listener = CreateStatsListener(measurements);

            var result = await ExportOneAsync(CreateCore(), _ =>
                Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));

            result.Should().Be(ExportResult.Failure);
            var ours = measurements.Where(m => m.host == "api").ToList();
            ours.Count(m => m.instrument == "Retry_Count").Should().Be(3);
            ours.Count(m => m.instrument == "Request_Failure_Count").Should().Be(1);
            ours.Should().NotContain(m => m.instrument == "Request_Success_Count");
        }
        finally
        {
            DistroNetworkSdkStats.ResetForTesting();
        }
    }

    [TestMethod]
    public async Task PartialContentResponseRecordsDurationOnlyAndExportsSuccess()
    {
        DistroNetworkSdkStats.ResetForTesting();
        DistroNetworkSdkStats.Initialize("N/A", "1.0.0");
        try
        {
            var measurements = new List<(string instrument, long value, string? host)>();
            using var listener = CreateStatsListener(measurements);

            var result = await ExportOneAsync(CreateCore(), _ =>
                Task.FromResult(new HttpResponseMessage(HttpStatusCode.PartialContent)));

            // 206 preserves export success but records neither Request_Success_Count nor a failure.
            result.Should().Be(ExportResult.Success);
            var ours = measurements.Where(m => m.host == "api").ToList();
            ours.Should().Contain(m => m.instrument == "Request_Duration");
            ours.Should().NotContain(m => m.instrument == "Request_Success_Count");
            ours.Should().NotContain(m => m.instrument == "Request_Failure_Count");
            ours.Should().NotContain(m => m.instrument == "Retry_Count");
            ours.Should().NotContain(m => m.instrument == "Throttle_Count");
        }
        finally
        {
            DistroNetworkSdkStats.ResetForTesting();
        }
    }

    [DataTestMethod]
    [DataRow(307)]
    [DataRow(308)]
    public async Task RedirectResponseRecordsDurationOnly(int statusCode)
    {
        DistroNetworkSdkStats.ResetForTesting();
        DistroNetworkSdkStats.Initialize("N/A", "1.0.0");
        try
        {
            var measurements = new List<(string instrument, long value, string? host)>();
            using var listener = CreateStatsListener(measurements);

            var result = await ExportOneAsync(CreateCore(), _ =>
                Task.FromResult(new HttpResponseMessage((HttpStatusCode)statusCode)));

            var ours = measurements.Where(m => m.host == "api").ToList();
            ours.Should().Contain(m => m.instrument == "Request_Duration");
            ours.Should().NotContain(m => m.instrument == "Request_Success_Count");
            ours.Should().NotContain(m => m.instrument == "Request_Failure_Count");
            ours.Should().NotContain(m => m.instrument == "Retry_Count");
            ours.Should().NotContain(m => m.instrument == "Throttle_Count");
        }
        finally
        {
            DistroNetworkSdkStats.ResetForTesting();
        }
    }

    [TestMethod]
    public async Task SuccessResponseRecordsRequestSuccessForTwoHundred()
    {
        DistroNetworkSdkStats.ResetForTesting();
        DistroNetworkSdkStats.Initialize("N/A", "1.0.0");
        try
        {
            var measurements = new List<(string instrument, long value, string? host)>();
            using var listener = CreateStatsListener(measurements);

            var result = await ExportOneAsync(CreateCore(), _ =>
                Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));

            result.Should().Be(ExportResult.Success);
            var ours = measurements.Where(m => m.host == "api").ToList();
            ours.Count(m => m.instrument == "Request_Success_Count").Should().Be(1);
            ours.Should().Contain(m => m.instrument == "Request_Duration");
            ours.Should().NotContain(m => m.instrument == "Request_Failure_Count");
        }
        finally
        {
            DistroNetworkSdkStats.ResetForTesting();
        }
    }

    [TestMethod]
    public async Task CircuitOpensAfterFiveExhaustedCycles()
    {
        var attempts = 0;
        var core = CreateCore();

        for (var cycle = 0; cycle < 5; cycle++)
        {
            var result = await ExportOneAsync(core, _ =>
            {
                attempts++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
            });
            result.Should().Be(ExportResult.Failure);
        }

        var openResult = await ExportOneAsync(core, _ =>
        {
            attempts++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });

        openResult.Should().Be(ExportResult.Failure);
        attempts.Should().Be(5 * Agent365RetryPolicy.MaxAttempts);
    }

    [TestMethod]
    public async Task SuccessfulHalfOpenProbeClosesCircuit()
    {
        var attempts = 0;
        var core = CreateCore();

        await OpenCircuitAsync(core, _ =>
        {
            attempts++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        });

        _now = _now.AddSeconds(31);
        var probe = await ExportOneAsync(core, _ =>
        {
            attempts++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });
        var next = await ExportOneAsync(core, _ =>
        {
            attempts++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });

        probe.Should().Be(ExportResult.Success);
        next.Should().Be(ExportResult.Success);
        // 5 exhausted 4-attempt cycles to open the circuit, then one probe and one follow-up call,
        // each succeeding on its first attempt.
        attempts.Should().Be(Agent365CircuitBreaker.FailureThreshold * Agent365RetryPolicy.MaxAttempts + 2);
    }

    [TestMethod]
    public async Task NonRetryableFailuresDoNotOpenCircuit()
    {
        var attempts = 0;
        var core = CreateCore();

        for (var i = 0; i < 7; i++)
        {
            (await ExportOneAsync(core, _ =>
            {
                attempts++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden));
            })).Should().Be(ExportResult.Failure);
        }

        attempts.Should().Be(7);
    }

    private async Task OpenCircuitAsync(
        Agent365ExporterCore core,
        Func<HttpRequestMessage, Task<HttpResponseMessage>> sendAsync)
    {
        for (var cycle = 0; cycle < Agent365CircuitBreaker.FailureThreshold; cycle++)
        {
            (await ExportOneAsync(core, sendAsync)).Should().Be(ExportResult.Failure);
        }
    }

    [TestMethod]
    public async Task TimeoutRetryLogsWarningWithAttemptAndDelayOnIntermediateAttempt()
    {
        var logger = new CapturingLogger();
        var attempts = 0;

        var result = await ExportOneAsync(CreateCore(logger), _ =>
        {
            attempts++;
            return attempts == 1
                ? Task.FromException<HttpResponseMessage>(new TaskCanceledException("timeout"))
                : Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });

        result.Should().Be(ExportResult.Success);
        var warning = logger.Entries.Should().ContainSingle(e => e.Level == LogLevel.Warning).Which;
        warning.Message.Should().Contain("attempt 1 of 4");
        warning.Message.Should().Contain("500");
        logger.Entries.Should().NotContain(e => e.Level == LogLevel.Error);
    }

    [TestMethod]
    public async Task TimeoutExhaustionLogsWarningsThenErrorOnFinalAttempt()
    {
        var logger = new CapturingLogger();

        var result = await ExportOneAsync(CreateCore(logger), _ =>
            Task.FromException<HttpResponseMessage>(new TaskCanceledException("timeout")));

        result.Should().Be(ExportResult.Failure);
        logger.Entries.Count(e => e.Level == LogLevel.Warning).Should().Be(3);
        var error = logger.Entries.Should().ContainSingle(e => e.Level == LogLevel.Error).Which;
        error.Message.Should().Contain("attempt 4 of 4");
    }

    [TestMethod]
    public async Task HttpRequestExceptionRetryLogsWarningOnIntermediateAttempt()
    {
        var logger = new CapturingLogger();
        var attempts = 0;

        var result = await ExportOneAsync(CreateCore(logger), _ =>
        {
            attempts++;
            return attempts == 1
                ? Task.FromException<HttpResponseMessage>(new HttpRequestException("network"))
                : Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });

        result.Should().Be(ExportResult.Success);
        var warning = logger.Entries.Should().ContainSingle(e => e.Level == LogLevel.Warning).Which;
        warning.Message.Should().Contain("attempt 1 of 4");
        warning.Message.Should().Contain("500");
    }

    [TestMethod]
    public async Task HttpRequestExceptionExhaustionLogsErrorOnFinalAttempt()
    {
        var logger = new CapturingLogger();

        var result = await ExportOneAsync(CreateCore(logger), _ =>
            Task.FromException<HttpResponseMessage>(new HttpRequestException("network")));

        result.Should().Be(ExportResult.Failure);
        logger.Entries.Count(e => e.Level == LogLevel.Warning).Should().Be(3);
        logger.Entries.Should().ContainSingle(e => e.Level == LogLevel.Error)
            .Which.Message.Should().Contain("attempt 4 of 4");
    }

    [TestMethod]
    public async Task RetryableResponseWarningIncludesCorrelationIdWhenPresent()
    {
        var logger = new CapturingLogger();
        var attempts = 0;

        var result = await ExportOneAsync(CreateCore(logger), _ =>
        {
            attempts++;
            if (attempts == 1)
            {
                var response = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
                response.Headers.Add("x-ms-correlation-id", "corr-xyz");
                return Task.FromResult(response);
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });

        result.Should().Be(ExportResult.Success);
        logger.Entries.Should().ContainSingle(e => e.Level == LogLevel.Warning)
            .Which.Message.Should().Contain("corr-xyz");
    }

    private static string? GetHost(ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        for (int i = 0; i < tags.Length; i++)
        {
            if (tags[i].Key == "host")
                return tags[i].Value as string;
        }
        return null;
    }

    private static MeterListener CreateStatsListener(List<(string instrument, long value, string? host)> measurements)
    {
        var listener = new MeterListener
        {
            InstrumentPublished = (instrument, l) =>
            {
                if (instrument.Meter.Name == DistroNetworkSdkStats.MeterName)
                    l.EnableMeasurementEvents(instrument);
            },
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
            measurements.Add((instrument.Name, value, GetHost(tags))));
        listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) =>
            measurements.Add((instrument.Name, (long)value, GetHost(tags))));
        listener.Start();
        return listener;
    }

    private sealed class CapturingLogger : ILogger<Agent365ExporterCore>
    {
        public List<(LogLevel Level, string Message, Exception? Exception)> Entries { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Entries.Add((logLevel, formatter(state, exception), exception));
        }
    }
}
