// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using Microsoft.OpenTelemetry.Agent365.Tracing.Exporters;

namespace Microsoft.OpenTelemetry.Agent365.Demo;

/// <summary>
/// Creates a TokenResolver for the Agent365 exporter.
/// Supports two modes:
///   1. Static bearer token from BEARER_TOKEN env var (local dev/testing)
///   2. Production: plug in AgenticTokenCache with UserAuthorization (see A365 samples)
/// </summary>
public static class TokenResolverFactory
{
    public static AsyncAuthTokenResolver? Create(IConfiguration configuration)
    {
        // For local dev/testing: use a static bearer token from env var
        var bearerToken = Environment.GetEnvironmentVariable("BEARER_TOKEN");
        if (!string.IsNullOrEmpty(bearerToken))
        {
            return (agentId, tenantId) => Task.FromResult<string?>(bearerToken);
        }

        // No token available — exporter will be skipped at runtime
        // In production, wire AgenticTokenCache here:
        //
        // var tokenCache = new AgenticTokenCache();
        // return async (agentId, tenantId) =>
        //     await tokenCache.GetObservabilityToken(agentId, tenantId);
        //
        return null;
    }
}
