// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using FluentAssertions;
using Microsoft.Agents.A365.Observability.Runtime.Common;
using Microsoft.Agents.A365.Observability.Runtime.Tracing.Exporters;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Http.Headers;

namespace Microsoft.Agents.A365.Observability.Tests.Tracing.Exporters;

/// <summary>
/// Exercises the durable replay contract of <see cref="Agent365ReplayCoordinator"/> and the fresh
/// authentication entry point <see cref="Agent365ExporterCore.ReplayRecordAsync"/>:
/// each pass asks the shared <see cref="Agent365TransmissionGate"/> for a permit, reads at most ten
/// leased records, resolves a *fresh* token per record (including the agentic user id), sends once,
/// and drives storage — success deletes, retryable retains and stops the pass, permanent/corrupt
/// deletes the poison blob, a missing token retains the record for a later pass, and a delete failure
/// after a successful send logs the duplicate risk. Owned half-open probes are always released.
/// </summary>
[TestClass]
public sealed class Agent365ReplayCoordinatorTests
{
    private DateTimeOffset _now = new(2026, 8, 8, 12, 0, 0, TimeSpan.Zero);

    // ------------------------------------------------------------------ helpers

    private static Agent365DurableRecord CreateRecord(
        string tenantId = "tenant-1",
        string agentId = "agent-1",
        string? agenticUserId = null,
        bool useS2SEndpoint = false,
        string? payload = null) =>
        new(
            tenantId,
            agentId,
            agenticUserId,
            useS2SEndpoint,
            payload ?? "{\"resourceSpans\":[]}",
            new DateTimeOffset(2026, 8, 8, 0, 0, 0, TimeSpan.Zero));

    private Agent365ReplayCoordinator CreateCoordinator(
        IAgent365PersistentStorage storage,
        Func<string, string, Task<string?>>? tokenResolver = null,
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? sendAsync = null,
        Agent365TransmissionGate? gate = null,
        ILogger? logger = null,
        int maxRecordsPerPass = 10,
        Func<TimeSpan, CancellationToken, Task>? delayAsync = null,
        TimeSpan? replayInterval = null,
        AsyncContextualTokenResolver? contextualResolver = null,
        TenantDomainResolver? domainResolver = null)
    {
        gate ??= new Agent365TransmissionGate(() => _now);
        tokenResolver ??= (_, _) => Task.FromResult<string?>("fresh-token");
        sendAsync ??= (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));

        var options = new Agent365ExporterOptions
        {
            DomainResolver = domainResolver ?? (_ => "api.example.com"),
            ContextualTokenResolver = contextualResolver,
        };

        var core = new Agent365ExporterCore(
            new ExportFormatter(NullLogger<ExportFormatter>.Instance),
            NullLogger<Agent365ExporterCore>.Instance,
            () => _now,
            storage,
            gate);

        return new Agent365ReplayCoordinator(
            storage,
            gate,
            replayAsync: (record, ct) => core.ReplayRecordAsync(record, options, tokenResolver, sendAsync, ct),
            logger ?? NullLogger.Instance,
            replayInterval,
            maxRecordsPerPass,
            delayAsync);
    }

    /// <summary>Puts a gate into the half-open Probe state so the next acquire owns the single probe.</summary>
    private Agent365TransmissionGate ProbeGate()
    {
        var gate = new Agent365TransmissionGate(() => _now);
        gate.RecordRetryableFailure(TimeSpan.FromSeconds(30));
        _now = _now.AddSeconds(31);
        return gate;
    }

    /// <summary>Puts a gate into Backoff so acquiring a permit fails (replay is skipped this pass).</summary>
    private Agent365TransmissionGate BackoffGate()
    {
        var gate = new Agent365TransmissionGate(() => _now);
        gate.RecordRetryableFailure(null);
        return gate;
    }

    // ------------------------------------------------------------------ success

    [TestMethod]
    public async Task SuccessfulReplayUsesFreshTokenAndDeletesRecord()
    {
        var stored = FakeStoredRecord.From(CreateRecord());
        var storage = new FakeStorage(stored);
        var tokens = 0;
        var sentAuthorization = string.Empty;
        var coordinator = CreateCoordinator(
            storage,
            tokenResolver: (_, _) =>
            {
                tokens++;
                return Task.FromResult<string?>("fresh-token");
            },
            sendAsync: (request, _) =>
            {
                sentAuthorization = request.Headers.Authorization!.Parameter!;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
            });

        await coordinator.ReplayOnceAsync(CancellationToken.None);

        tokens.Should().Be(1);
        sentAuthorization.Should().Be("fresh-token");
        stored.DeleteCalls.Should().Be(1);
    }

    [TestMethod]
    public async Task SuccessfulReplayBuildsFreshEndpointFromRecord()
    {
        var stored = FakeStoredRecord.From(CreateRecord(tenantId: "tenant-9", agentId: "agent-9"));
        var storage = new FakeStorage(stored);
        Uri? sentUri = null;
        var coordinator = CreateCoordinator(
            storage,
            sendAsync: (request, _) =>
            {
                sentUri = request.RequestUri;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
            });

        await coordinator.ReplayOnceAsync(CancellationToken.None);

        sentUri.Should().NotBeNull();
        sentUri!.ToString().Should().Be(
            "https://api.example.com/observability/tenants/tenant-9/otlp/agents/agent-9/traces?api-version=1");
        stored.DeleteCalls.Should().Be(1);
    }

    [TestMethod]
    public async Task SuccessfulReplaySendsRecordPayload()
    {
        var stored = FakeStoredRecord.From(CreateRecord(payload: "{\"resourceSpans\":[{\"marker\":42}]}"));
        var storage = new FakeStorage(stored);
        var body = string.Empty;
        var coordinator = CreateCoordinator(
            storage,
            sendAsync: async (request, _) =>
            {
                body = await request.Content!.ReadAsStringAsync();
                return new HttpResponseMessage(HttpStatusCode.OK);
            });

        await coordinator.ReplayOnceAsync(CancellationToken.None);

        body.Should().Contain("\"marker\":42");
        stored.DeleteCalls.Should().Be(1);
    }

    // ------------------------------------------------------------------ retryable

    [TestMethod]
    public async Task RetryableFailureRetainsRecordAndStopsPass()
    {
        var first = FakeStoredRecord.From(CreateRecord(agentId: "agent-1"));
        var second = FakeStoredRecord.From(CreateRecord(agentId: "agent-2"));
        var storage = new FakeStorage(first, second);
        var sends = 0;
        var coordinator = CreateCoordinator(
            storage,
            sendAsync: (_, _) =>
            {
                sends++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
            });

        await coordinator.ReplayOnceAsync(CancellationToken.None);

        sends.Should().Be(1, "the pass stops after the first retryable outcome");
        first.DeleteCalls.Should().Be(0, "a retryable record is retained for a later pass");
        second.ReadCalls.Should().Be(0, "the second record is not processed once the pass stops");
        second.DeleteCalls.Should().Be(0);
    }

    [TestMethod]
    public async Task RetryableFailureBacksOffTheSharedGate()
    {
        var stored = FakeStoredRecord.From(CreateRecord());
        var storage = new FakeStorage(stored);
        var gate = new Agent365TransmissionGate(() => _now);
        var coordinator = CreateCoordinator(
            storage,
            gate: gate,
            sendAsync: (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));

        await coordinator.ReplayOnceAsync(CancellationToken.None);

        gate.ConsecutiveErrors.Should().Be(1);
        gate.TryAcquire(out _).Should().BeFalse("the gate is now in backoff");
        stored.DeleteCalls.Should().Be(0);
    }

    [TestMethod]
    public async Task RetryAfterHeaderIsHonoredByGateDuringReplay()
    {
        var stored = FakeStoredRecord.From(CreateRecord());
        var storage = new FakeStorage(stored);
        var gate = new Agent365TransmissionGate(() => _now);
        var coordinator = CreateCoordinator(
            storage,
            gate: gate,
            sendAsync: (_, _) =>
            {
                var response = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
                response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(45));
                return Task.FromResult(response);
            });

        await coordinator.ReplayOnceAsync(CancellationToken.None);

        gate.CurrentDelay.Should().BeCloseTo(TimeSpan.FromSeconds(45), TimeSpan.FromSeconds(1));
    }

    // ------------------------------------------------------------------ permanent / poison

    [TestMethod]
    public async Task PermanentFailureDeletesRecord()
    {
        var stored = FakeStoredRecord.From(CreateRecord());
        var storage = new FakeStorage(stored);
        var coordinator = CreateCoordinator(
            storage,
            sendAsync: (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden)));

        await coordinator.ReplayOnceAsync(CancellationToken.None);

        stored.DeleteCalls.Should().Be(1, "a permanent failure discards the poison record");
    }

    [TestMethod]
    public async Task PermanentFailureDoesNotBackOffTheGate()
    {
        var stored = FakeStoredRecord.From(CreateRecord());
        var storage = new FakeStorage(stored);
        var gate = new Agent365TransmissionGate(() => _now);
        var coordinator = CreateCoordinator(
            storage,
            gate: gate,
            sendAsync: (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)));

        await coordinator.ReplayOnceAsync(CancellationToken.None);

        gate.ConsecutiveErrors.Should().Be(0, "a permanent failure is not an availability signal");
        gate.TryAcquire(out _).Should().BeTrue("the gate stays closed");
    }

    [TestMethod]
    public async Task InvalidRecordDeletesPoisonBlobWithoutSending()
    {
        var poison = FakeStoredRecord.Corrupt();
        var storage = new FakeStorage(poison);
        var sends = 0;
        var coordinator = CreateCoordinator(
            storage,
            sendAsync: (_, _) =>
            {
                sends++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
            });

        await coordinator.ReplayOnceAsync(CancellationToken.None);

        sends.Should().Be(0, "a record that cannot be read is never sent");
        poison.DeleteCalls.Should().Be(1, "an unreadable poison blob is deleted");
    }

    // ------------------------------------------------------------------ missing token

    [TestMethod]
    public async Task NullTokenRetainsRecordForLaterPass()
    {
        var stored = FakeStoredRecord.From(CreateRecord());
        var storage = new FakeStorage(stored);
        var sends = 0;
        var coordinator = CreateCoordinator(
            storage,
            tokenResolver: (_, _) => Task.FromResult<string?>(null),
            sendAsync: (_, _) =>
            {
                sends++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
            });

        await coordinator.ReplayOnceAsync(CancellationToken.None);

        sends.Should().Be(0, "no request is sent without a token");
        stored.DeleteCalls.Should().Be(0, "the record is retained until a token is available");
    }

    [TestMethod]
    public async Task NullTokenDoesNotBackOffTheGate()
    {
        var stored = FakeStoredRecord.From(CreateRecord());
        var storage = new FakeStorage(stored);
        var gate = new Agent365TransmissionGate(() => _now);
        var coordinator = CreateCoordinator(
            storage,
            gate: gate,
            tokenResolver: (_, _) => Task.FromResult<string?>(null));

        await coordinator.ReplayOnceAsync(CancellationToken.None);

        gate.ConsecutiveErrors.Should().Be(0, "a missing token is not a transport failure");
        gate.TryAcquire(out _).Should().BeTrue("the gate stays closed for the live path");
    }

    [TestMethod]
    public async Task TokenResolverExceptionRetainsRecord()
    {
        var stored = FakeStoredRecord.From(CreateRecord());
        var storage = new FakeStorage(stored);
        var sends = 0;
        var coordinator = CreateCoordinator(
            storage,
            tokenResolver: (_, _) => throw new InvalidOperationException("token boom"),
            sendAsync: (_, _) =>
            {
                sends++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
            });

        await coordinator.ReplayOnceAsync(CancellationToken.None);

        sends.Should().Be(0);
        stored.DeleteCalls.Should().Be(0, "a token-resolver outage retains the record for a later pass");
    }

    // ------------------------------------------------------------------ delete failure

    [TestMethod]
    public async Task DeleteFailureAfterSuccessLogsDuplicateRisk()
    {
        var stored = FakeStoredRecord.From(CreateRecord());
        stored.DeleteResult = false;
        var storage = new FakeStorage(stored);
        var logger = new ReplayTestLogger();
        var coordinator = CreateCoordinator(
            storage,
            logger: logger,
            sendAsync: (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));

        await coordinator.ReplayOnceAsync(CancellationToken.None);

        stored.DeleteCalls.Should().Be(1, "a delete is attempted after a successful send");
        logger.Entries.Should().Contain(
            e => e.Level == LogLevel.Warning && e.Message.ToLowerInvariant().Contains("duplicate"),
            "a successful send whose record cannot be deleted risks a duplicate on the next pass");
    }

    // ------------------------------------------------------------------ per-pass cap

    [TestMethod]
    public async Task OnePassHandlesAtMostTenRecords()
    {
        var records = Enumerable.Range(0, 12)
            .Select(_ => FakeStoredRecord.From(CreateRecord()))
            .ToArray();
        var storage = new FakeStorage(records);
        var sends = 0;
        var coordinator = CreateCoordinator(
            storage,
            sendAsync: (_, _) =>
            {
                sends++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
            });

        await coordinator.ReplayOnceAsync(CancellationToken.None);

        sends.Should().Be(10, "a pass handles at most ten records");
        storage.PendingCount.Should().Be(2, "the remaining records are left for the next pass");
        records.Take(10).Should().OnlyContain(r => r.DeleteCalls == 1);
        records.Skip(10).Should().OnlyContain(r => r.DeleteCalls == 0 && r.ReadCalls == 0);
    }

    // ------------------------------------------------------------------ leases

    [TestMethod]
    public async Task LeasedBlobIsSkipped()
    {
        var leased = FakeStoredRecord.From(CreateRecord());
        leased.LeaseResult = false;
        var storage = new FakeStorage(leased);
        var sends = 0;
        var coordinator = CreateCoordinator(
            storage,
            sendAsync: (_, _) =>
            {
                sends++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
            });

        await coordinator.ReplayOnceAsync(CancellationToken.None);

        sends.Should().Be(0, "a blob leased by another worker is skipped");
        leased.ReadCalls.Should().Be(0, "a skipped blob is never read");
        leased.DeleteCalls.Should().Be(0, "a skipped blob is left in place");
    }

    [TestMethod]
    public async Task RecordsAreLeasedBeforeReading()
    {
        var stored = FakeStoredRecord.From(CreateRecord());
        var storage = new FakeStorage(stored);
        var coordinator = CreateCoordinator(storage);

        await coordinator.ReplayOnceAsync(CancellationToken.None);

        stored.LeaseCalls.Should().Be(1);
        stored.LeasedDuration.Should().BeGreaterThan(TimeSpan.Zero, "a positive lease is taken before delivery");
    }

    // ------------------------------------------------------------------ agentic user id

    [TestMethod]
    public async Task ReplayWithAgenticUserIdUsesContextualResolver()
    {
        var stored = FakeStoredRecord.From(CreateRecord(agenticUserId: "user-77"));
        var storage = new FakeStorage(stored);
        TokenResolverContext? captured = null;
        var coordinator = CreateCoordinator(
            storage,
            contextualResolver: ctx =>
            {
                captured = ctx;
                return Task.FromResult<string?>("ctx-token");
            });

        await coordinator.ReplayOnceAsync(CancellationToken.None);

        captured.Should().NotBeNull();
        captured!.Identity.AgentId.Should().Be("agent-1");
        captured.Identity.AgenticUserId.Should().Be("user-77");
        captured.TenantId.Should().Be("tenant-1");
        stored.DeleteCalls.Should().Be(1);
    }

    [TestMethod]
    public async Task ReplayWithoutAgenticUserIdPassesNullUserId()
    {
        var stored = FakeStoredRecord.From(CreateRecord(agenticUserId: null, useS2SEndpoint: true));
        var storage = new FakeStorage(stored);
        TokenResolverContext? captured = null;
        var coordinator = CreateCoordinator(
            storage,
            contextualResolver: ctx =>
            {
                captured = ctx;
                return Task.FromResult<string?>("ctx-token");
            });

        await coordinator.ReplayOnceAsync(CancellationToken.None);

        captured.Should().NotBeNull();
        captured!.Identity.AgentId.Should().Be("agent-1");
        captured.Identity.AgenticUserId.Should().BeNull();
        stored.DeleteCalls.Should().Be(1);
    }

    // ------------------------------------------------------------------ gate permits

    [TestMethod]
    public async Task GateInBackoffSkipsPassEntirely()
    {
        var stored = FakeStoredRecord.From(CreateRecord());
        var storage = new FakeStorage(stored);
        var sends = 0;
        var coordinator = CreateCoordinator(
            storage,
            gate: BackoffGate(),
            sendAsync: (_, _) =>
            {
                sends++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
            });

        await coordinator.ReplayOnceAsync(CancellationToken.None);

        sends.Should().Be(0, "no permit is granted while the gate is in backoff");
        storage.PendingCount.Should().Be(1, "the record is not even read while the gate is closed");
        stored.LeaseCalls.Should().Be(0);
    }

    [TestMethod]
    public async Task OwnedProbeIsReleasedWhenNoTerminalOutcome()
    {
        var stored = FakeStoredRecord.From(CreateRecord());
        var storage = new FakeStorage(stored);
        var gate = ProbeGate();
        var coordinator = CreateCoordinator(
            storage,
            gate: gate,
            sendAsync: (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden)));

        await coordinator.ReplayOnceAsync(CancellationToken.None);

        // A permanent failure records no terminal gate outcome, so the half-open probe must be
        // returned: a subsequent acquire still owns the single probe (it was not leaked).
        gate.TryAcquire(out var ownsProbe).Should().BeTrue();
        ownsProbe.Should().BeTrue("the owned probe was released, not leaked");
    }

    [TestMethod]
    public async Task SuccessfulProbeClosesGate()
    {
        var stored = FakeStoredRecord.From(CreateRecord());
        var storage = new FakeStorage(stored);
        var gate = ProbeGate();
        var coordinator = CreateCoordinator(
            storage,
            gate: gate,
            sendAsync: (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));

        await coordinator.ReplayOnceAsync(CancellationToken.None);

        gate.ConsecutiveErrors.Should().Be(0);
        gate.TryAcquire(out var ownsProbe).Should().BeTrue();
        ownsProbe.Should().BeFalse("a successful probe closes the gate");
        stored.DeleteCalls.Should().Be(1);
    }

    [TestMethod]
    public async Task EmptyStorageDoesNothing()
    {
        var storage = new FakeStorage();
        var sends = 0;
        var coordinator = CreateCoordinator(
            storage,
            sendAsync: (_, _) =>
            {
                sends++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
            });

        await coordinator.ReplayOnceAsync(CancellationToken.None);

        sends.Should().Be(0);
    }

    // ------------------------------------------------------------------ lifecycle

    [TestMethod]
    public async Task StartRunsReplayPassesUntilStopped()
    {
        var stored = FakeStoredRecord.From(CreateRecord());
        var storage = new FakeStorage(stored);
        var delay = new ControlledDelay();
        var coordinator = CreateCoordinator(
            storage,
            delayAsync: delay.WaitAsync,
            replayInterval: TimeSpan.FromMinutes(2),
            sendAsync: (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));

        coordinator.Start();

        // The loop awaits the (controlled) delay before its first pass; release it once.
        await delay.WaitForCallAsync(1);
        delay.ReleaseOnce();

        // After the first pass the record is delivered and deleted, and the loop awaits again.
        await WaitUntilAsync(() => stored.DeleteCalls == 1);
        await delay.WaitForCallAsync(2);

        await coordinator.StopAsync(CancellationToken.None);

        stored.DeleteCalls.Should().Be(1);
    }

    [TestMethod]
    public async Task StopAsyncWithoutStartCompletes()
    {
        var coordinator = CreateCoordinator(new FakeStorage());

        Func<Task> stop = () => coordinator.StopAsync(CancellationToken.None);

        await stop.Should().CompleteWithinAsync(TimeSpan.FromSeconds(5));
    }

    [TestMethod]
    public async Task StartIsIdempotent()
    {
        var storage = new FakeStorage();
        var delay = new ControlledDelay();
        var coordinator = CreateCoordinator(storage, delayAsync: delay.WaitAsync);

        coordinator.Start();
        coordinator.Start();

        await delay.WaitForCallAsync(1);
        delay.Calls.Should().Be(1, "a single background loop runs even after repeated Start calls");

        await coordinator.StopAsync(CancellationToken.None);
    }

    // ------------------------------------------------------------------ per-record exception isolation

    [TestMethod]
    public async Task ReplayThrowingRecordIsQuarantinedAndPassContinues()
    {
        var poison = FakeStoredRecord.From(CreateRecord(agentId: "poison"));
        var good = FakeStoredRecord.From(CreateRecord(agentId: "good"));
        var storage = new FakeStorage(poison, good);
        var sends = 0;
        var coordinator = CreateCoordinator(
            storage,
            sendAsync: (request, _) =>
            {
                sends++;
                if (request.RequestUri!.ToString().Contains("poison"))
                    throw new InvalidOperationException("replay boom");
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
            });

        Func<Task> act = () => coordinator.ReplayOnceAsync(CancellationToken.None);

        await act.Should().NotThrowAsync("one record's replay exception must not tear down the whole pass");
        sends.Should().Be(2, "the pass continues to the next record after a throwing one");
        poison.DeleteCalls.Should().Be(1, "a record whose replay throws is quarantined as poison and deleted");
        good.DeleteCalls.Should().Be(1, "the following record is still delivered and deleted");
    }

    [TestMethod]
    public async Task ThrownCancellationDuringReplayRetainsRecordAndPropagates()
    {
        var stored = FakeStoredRecord.From(CreateRecord());
        var storage = new FakeStorage(stored);
        using var cts = new CancellationTokenSource();
        var coordinator = CreateCoordinator(
            storage,
            sendAsync: (request, token) =>
            {
                cts.Cancel();
                token.ThrowIfCancellationRequested();
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
            });

        Func<Task> act = () => coordinator.ReplayOnceAsync(cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>(
            "cooperative cancellation is preserved, not swallowed and treated as poison");
        stored.DeleteCalls.Should().Be(0, "a cancelled replay retains the record rather than deleting it");
    }

    // ------------------------------------------------------------------ lease-failure spin guard

    [TestMethod]
    public async Task LeaseFailureDoesNotSpinWhenStorageReservesSameBlob()
    {
        // The real FileBlobProvider.TryGetNext is non-destructive: it re-serves the same first
        // unleased blob on every call. A failed lease must stop the pass, not loop up to
        // maxRecordsPerPass times re-fetching and re-leasing the identical blob.
        var leased = FakeStoredRecord.From(CreateRecord());
        leased.LeaseResult = false;
        var storage = new ReServingStorage(leased);
        var coordinator = CreateCoordinator(storage, maxRecordsPerPass: 10);

        await coordinator.ReplayOnceAsync(CancellationToken.None);

        leased.LeaseCalls.Should().Be(1, "a failed lease stops the pass instead of re-leasing the re-served blob");
        storage.GetNextCalls.Should().Be(1, "the pass does not repeatedly re-fetch the same unleased blob");
    }

    // ------------------------------------------------------------------ interval validation

    [TestMethod]
    public void NonPositiveReplayIntervalThrows()
    {
        Action zero = () => CreateCoordinator(new FakeStorage(), replayInterval: TimeSpan.Zero);
        Action negative = () => CreateCoordinator(new FakeStorage(), replayInterval: TimeSpan.FromSeconds(-1));

        zero.Should().Throw<ArgumentOutOfRangeException>("a zero interval would spin the loop with no delay");
        negative.Should().Throw<ArgumentOutOfRangeException>("a negative interval is invalid");
    }

    // ------------------------------------------------------------------ non-shutdown cancellation

    [TestMethod]
    public async Task NonShutdownCancellationDoesNotKillTheLoop()
    {
        var storage = new FakeStorage();
        var calls = 0;
        var secondReached = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Func<TimeSpan, CancellationToken, Task> delay = (interval, token) =>
        {
            var n = Interlocked.Increment(ref calls);
            if (n == 1)
            {
                // A cancellation that is NOT the coordinator's shutdown must not tear down the loop.
                throw new OperationCanceledException();
            }

            if (n == 2)
            {
                secondReached.TrySetResult(true);
            }

            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            token.Register(() => tcs.TrySetCanceled());
            return tcs.Task;
        };
        var coordinator = CreateCoordinator(storage, delayAsync: delay);

        coordinator.Start();
        await Task.WhenAny(secondReached.Task, Task.Delay(TimeSpan.FromSeconds(3)));
        await coordinator.StopAsync(CancellationToken.None);

        secondReached.Task.IsCompletedSuccessfully.Should().BeTrue(
            "a non-shutdown cancellation must be logged and the loop must keep running for later passes");
    }

    // ------------------------------------------------------------------ start/stop lifecycle race

    [TestMethod]
    public async Task StopAsyncAwaitsTheLoopLaunchedByStart()
    {
        var storage = new FakeStorage();
        var delay = new ControlledDelay();
        var coordinator = CreateCoordinator(storage, delayAsync: delay.WaitAsync);

        coordinator.Start();
        await delay.WaitForCallAsync(1);

        Func<Task> stop = () => coordinator.StopAsync(CancellationToken.None);
        await stop.Should().CompleteWithinAsync(
            TimeSpan.FromSeconds(5),
            "StopAsync must observe and await the run task launched by Start, then return once it unwinds");

        var callsAtStop = delay.Calls;
        await Task.Delay(50);
        delay.Calls.Should().Be(callsAtStop, "the loop has fully stopped after StopAsync returns");
    }

    // ------------------------------------------------------------------ coordinator token reaches the send

    [TestMethod]
    public async Task InFlightSendReceivesCoordinatorToken()
    {
        var stored = FakeStoredRecord.From(CreateRecord());
        var storage = new FakeStorage(stored);
        using var cts = new CancellationTokenSource();
        CancellationToken received = default;
        var coordinator = CreateCoordinator(
            storage,
            sendAsync: (request, token) =>
            {
                received = token;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
            });

        await coordinator.ReplayOnceAsync(cts.Token);

        received.Should().Be(cts.Token, "the coordinator's cancellation token flows to the actual HTTP send");
    }

    [TestMethod]
    public async Task MidFlightShutdownCancelsInFlightSendAndRetainsRecord()
    {
        var stored = FakeStoredRecord.From(CreateRecord());
        var storage = new FakeStorage(stored);
        using var cts = new CancellationTokenSource();
        var coordinator = CreateCoordinator(
            storage,
            sendAsync: async (request, token) =>
            {
                // Shutdown arrives while the request is in flight; the send observes the token.
                cts.Cancel();
                await Task.Delay(Timeout.Infinite, token);
                return new HttpResponseMessage(HttpStatusCode.OK);
            });

        Func<Task> act = () => coordinator.ReplayOnceAsync(cts.Token);

        await act.Should().NotThrowAsync("a mid-flight cancellation is classified as a Canceled outcome, not thrown");
        stored.DeleteCalls.Should().Be(0, "a send cancelled mid-flight by shutdown retains the record for a later pass");
    }

    // ------------------------------------------------------------------ S2S endpoint

    [TestMethod]
    public async Task ReplayWithS2SEndpointBuildsS2SUri()
    {
        var stored = FakeStoredRecord.From(CreateRecord(tenantId: "tenant-9", agentId: "agent-9", useS2SEndpoint: true));
        var storage = new FakeStorage(stored);
        Uri? sentUri = null;
        var coordinator = CreateCoordinator(
            storage,
            sendAsync: (request, _) =>
            {
                sentUri = request.RequestUri;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
            });

        await coordinator.ReplayOnceAsync(CancellationToken.None);

        sentUri.Should().NotBeNull();
        sentUri!.ToString().Should().Be(
            "https://api.example.com/observabilityService/tenants/tenant-9/otlp/agents/agent-9/traces?api-version=1");
        stored.DeleteCalls.Should().Be(1);
    }

    private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs = 5000)
    {
        var deadline = Environment.TickCount + timeoutMs;
        while (!condition())
        {
            if (Environment.TickCount > deadline)
                throw new TimeoutException("Condition was not met within the timeout.");
            await Task.Delay(10);
        }
    }
}

/// <summary>
/// Minimal non-generic <see cref="ILogger"/> that records every entry, used to assert the
/// coordinator's duplicate-risk warning.
/// </summary>
internal sealed class ReplayTestLogger : ILogger
{
    public List<(LogLevel Level, string Message, Exception? Exception)> Entries { get; } = new();

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        Entries.Add((logLevel, formatter(state, exception), exception));
    }
}

/// <summary>
/// A deterministic delay double for the background replay loop. Each <see cref="WaitAsync"/> call
/// awaits a fresh completion source that the test releases with <see cref="ReleaseOnce"/>, so passes
/// are stepped explicitly rather than waiting on wall-clock time. Cancellation completes the pending
/// wait so the loop can exit on <c>StopAsync</c>.
/// </summary>
internal sealed class ControlledDelay
{
    private readonly object _lock = new();
    private TaskCompletionSource<bool> _next = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _calls;

    public int Calls { get { lock (_lock) { return _calls; } } }

    public Task WaitAsync(TimeSpan interval, CancellationToken cancellationToken)
    {
        TaskCompletionSource<bool> tcs;
        lock (_lock)
        {
            _calls++;
            tcs = _next;
        }

        if (cancellationToken.CanBeCanceled)
        {
            cancellationToken.Register(() => tcs.TrySetCanceled());
        }

        return tcs.Task;
    }

    public void ReleaseOnce()
    {
        TaskCompletionSource<bool> tcs;
        lock (_lock)
        {
            tcs = _next;
            _next = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        tcs.TrySetResult(true);
    }

    public async Task WaitForCallAsync(int callCount, int timeoutMs = 5000)
    {
        var deadline = Environment.TickCount + timeoutMs;
        while (Calls < callCount)
        {
            if (Environment.TickCount > deadline)
                throw new TimeoutException($"Delay was not awaited {callCount} time(s) within the timeout.");
            await Task.Delay(10);
        }
    }
}

/// <summary>
/// Non-destructive storage double that reproduces the real <c>FileBlobProvider</c> contract: every
/// <see cref="TryGetNext"/> re-serves the same unleased record (it is never dequeued). Used to prove
/// the coordinator does not spin over a re-served blob whose lease keeps failing.
/// </summary>
internal sealed class ReServingStorage : IAgent365PersistentStorage
{
    private readonly IAgent365StoredRecord _record;

    public ReServingStorage(IAgent365StoredRecord record) => _record = record;

    public int GetNextCalls { get; private set; }

    public bool TryStore(Agent365DurableRecord record) => true;

    public bool TryGetNext([NotNullWhen(true)] out IAgent365StoredRecord? record)
    {
        GetNextCalls++;
        record = _record;
        return true;
    }

    public void Dispose()
    {
    }
}
