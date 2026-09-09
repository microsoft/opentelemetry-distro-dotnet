using Microsoft.Identity.Client;

namespace Microsoft.OpenTelemetry.Agent365.S2S.Demo;

internal sealed class MsalTokenExchangeClient : ITokenExchangeClient
{
    private static readonly string[] TokenExchangeScopes =
        ["api://AzureAdTokenExchange/.default"];

    private static readonly string[] ObservabilityScopes =
        ["api://9b975845-388f-4429-889e-eab1ef63949c/.default"];

    public async Task<TokenExchangeResult> AcquireBlueprintTokenAsync(
        SampleOptions options,
        CancellationToken cancellationToken)
    {
        try
        {
            var application = ConfidentialClientApplicationBuilder
                .Create(options.ClientId)
                .WithClientSecret(options.ClientSecret)
                .WithAuthority(BuildAuthority(options))
                .Build();

            var result = await application
                .AcquireTokenForClient(TokenExchangeScopes)
                .WithFmiPath(options.AgentAppInstanceId)
                .ExecuteAsync(cancellationToken)
                .ConfigureAwait(false);

            return new TokenExchangeResult(result.AccessToken, result.ExpiresOn);
        }
        catch (MsalException exception)
        {
            throw CreateExchangeFailure(
                "The blueprint app token exchange",
                exception);
        }
    }

    public async Task<TokenExchangeResult> AcquireObservabilityTokenAsync(
        SampleOptions options,
        string clientAssertion,
        CancellationToken cancellationToken)
    {
        try
        {
            var application = ConfidentialClientApplicationBuilder
                .Create(options.AgentAppInstanceId)
                .WithClientAssertion(
                    (Func<AssertionRequestOptions, Task<string>>)(_ => Task.FromResult(clientAssertion)))
                .WithAuthority(BuildAuthority(options))
                .Build();

            var result = await application
                .AcquireTokenForClient(ObservabilityScopes)
                .ExecuteAsync(cancellationToken)
                .ConfigureAwait(false);

            return new TokenExchangeResult(result.AccessToken, result.ExpiresOn);
        }
        catch (MsalException exception)
        {
            throw CreateExchangeFailure(
                "The Agent365 observability token exchange",
                exception);
        }
    }

    internal static InvalidOperationException CreateExchangeFailure(
        string stage,
        MsalException exception) =>
        new(
            $"{stage} failed ({exception.ErrorCode}).",
            exception);

    private static string BuildAuthority(SampleOptions options) =>
        $"{options.Authority.AbsoluteUri.TrimEnd('/')}/{options.TenantId}";
}
