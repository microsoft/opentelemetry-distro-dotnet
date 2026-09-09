using FluentAssertions;
using Microsoft.OpenTelemetry.Agent365.S2S.Demo;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Microsoft.OpenTelemetry.Agent365.S2S.Demo.Tests;

[TestClass]
public sealed class S2STokenProviderTests
{
    [TestMethod]
    public async Task ResolveAsync_ValidRequest_PerformsBothExchanges()
    {
        var client = new FakeTokenExchangeClient();
        var options = TestData.CreateOptions();
        var provider = new S2STokenProvider(options, client, TimeProvider.System);

        var token = await provider.ResolveAsync(
            options.AgentId,
            options.TenantId);

        token.Should().Be("observability-token-1");
        client.BlueprintCalls.Should().Be(1);
        client.ObservabilityCalls.Should().Be(1);
        client.LastClientAssertion.Should().Be("blueprint-token");
    }

    [TestMethod]
    public async Task ResolveAsync_DifferentTenant_ThrowsBeforeTokenAcquisition()
    {
        var client = new FakeTokenExchangeClient();
        var provider = new S2STokenProvider(
            TestData.CreateOptions(),
            client,
            TimeProvider.System);

        var action = () => provider.ResolveAsync(
            "44444444-4444-4444-4444-444444444444",
            "55555555-5555-5555-5555-555555555555");

        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*does not match configured tenant*");
        client.BlueprintCalls.Should().Be(0);
    }

    [TestMethod]
    public async Task ResolveAsync_UnexpiredToken_ReturnsCachedToken()
    {
        var client = new FakeTokenExchangeClient();
        var options = TestData.CreateOptions();
        var provider = new S2STokenProvider(options, client, TimeProvider.System);

        var first = await provider.ResolveAsync(options.AgentId, options.TenantId);
        var second = await provider.ResolveAsync(options.AgentId, options.TenantId);

        second.Should().Be(first);
        client.BlueprintCalls.Should().Be(1);
        client.ObservabilityCalls.Should().Be(1);
    }

    [TestMethod]
    public async Task ResolveAsync_ConcurrentRequests_PerformOneExchange()
    {
        var client = new FakeTokenExchangeClient(delay: TimeSpan.FromMilliseconds(50));
        var options = TestData.CreateOptions();
        var provider = new S2STokenProvider(options, client, TimeProvider.System);

        var tokens = await Task.WhenAll(
            Enumerable.Range(0, 8)
                .Select(_ => provider.ResolveAsync(options.AgentId, options.TenantId)));

        tokens.Should().OnlyContain(token => token == "observability-token-1");
        client.BlueprintCalls.Should().Be(1);
        client.ObservabilityCalls.Should().Be(1);
    }

    [TestMethod]
    public async Task ResolveAsync_TokenWithinRefreshBuffer_AcquiresNewToken()
    {
        var now = new DateTimeOffset(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);
        var timeProvider = new TestTimeProvider(now);
        var client = new FakeTokenExchangeClient
        {
            ObservabilityExpiresOn = now.AddMinutes(2),
        };
        var options = TestData.CreateOptions();
        var provider = new S2STokenProvider(options, client, timeProvider);

        var first = await provider.ResolveAsync(options.AgentId, options.TenantId);
        timeProvider.Advance(TimeSpan.FromSeconds(61));
        client.ObservabilityExpiresOn = now.AddHours(1);
        var second = await provider.ResolveAsync(options.AgentId, options.TenantId);

        first.Should().Be("observability-token-1");
        second.Should().Be("observability-token-2");
        client.ObservabilityCalls.Should().Be(2);
    }

    [TestMethod]
    public async Task ResolveAsync_BlueprintFailure_PreservesStageMessage()
    {
        var client = new FakeTokenExchangeClient
        {
            BlueprintException = new InvalidOperationException(
                "The blueprint app token exchange failed (invalid_client)."),
        };
        var options = TestData.CreateOptions();
        var provider = new S2STokenProvider(options, client, TimeProvider.System);

        var action = () => provider.ResolveAsync(options.AgentId, options.TenantId);

        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*blueprint app token exchange failed*");
    }

    [TestMethod]
    public async Task ResolveAsync_ObservabilityFailure_PreservesStageMessage()
    {
        var client = new FakeTokenExchangeClient
        {
            ObservabilityException = new InvalidOperationException(
                "The Agent365 observability token exchange failed (invalid_scope)."),
        };
        var options = TestData.CreateOptions();
        var provider = new S2STokenProvider(options, client, TimeProvider.System);

        var action = () => provider.ResolveAsync(options.AgentId, options.TenantId);

        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Agent365 observability token exchange failed*");
    }

    private sealed class FakeTokenExchangeClient : ITokenExchangeClient
    {
        private readonly TimeSpan _delay;
        private int _blueprintCalls;
        private int _observabilityCalls;

        internal FakeTokenExchangeClient(TimeSpan? delay = null)
        {
            _delay = delay ?? TimeSpan.Zero;
        }

        internal int BlueprintCalls => _blueprintCalls;

        internal int ObservabilityCalls => _observabilityCalls;

        internal string? LastClientAssertion { get; private set; }

        internal DateTimeOffset ObservabilityExpiresOn { get; set; } =
            DateTimeOffset.UtcNow.AddHours(1);

        internal Exception? BlueprintException { get; init; }

        internal Exception? ObservabilityException { get; init; }

        public async Task<TokenExchangeResult> AcquireBlueprintTokenAsync(
            SampleOptions options,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _blueprintCalls);
            if (_delay > TimeSpan.Zero)
            {
                await Task.Delay(_delay, cancellationToken);
            }

            if (BlueprintException is not null)
            {
                throw BlueprintException;
            }

            return new TokenExchangeResult(
                "blueprint-token",
                DateTimeOffset.UtcNow.AddMinutes(10));
        }

        public Task<TokenExchangeResult> AcquireObservabilityTokenAsync(
            SampleOptions options,
            string clientAssertion,
            CancellationToken cancellationToken)
        {
            var call = Interlocked.Increment(ref _observabilityCalls);
            this.LastClientAssertion = clientAssertion;
            if (ObservabilityException is not null)
            {
                throw ObservabilityException;
            }

            return Task.FromResult(
                new TokenExchangeResult(
                    $"observability-token-{call}",
                    this.ObservabilityExpiresOn));
        }
    }

}
