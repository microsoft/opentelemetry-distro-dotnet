// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Concurrent;
using System.Threading.Tasks;

namespace Microsoft.OpenTelemetry.Agent365
{
    /// <summary>
    /// Thread-safe token store for Agent365 observability exporter tokens.
    /// Tokens are populated by agent middleware on each turn and consumed by the Agent365 exporter.
    /// </summary>
    public static class ObservabilityTokenStore
    {
        private static readonly ConcurrentDictionary<string, string> _tokens = new();

        /// <summary>
        /// Stores a token for the specified agent and tenant.
        /// </summary>
        /// <param name="agentId">The agent identifier.</param>
        /// <param name="tenantId">The tenant identifier.</param>
        /// <param name="token">The authentication token.</param>
        public static void SetToken(string agentId, string tenantId, string token)
        {
            if (!string.IsNullOrEmpty(agentId) && !string.IsNullOrEmpty(tenantId) && !string.IsNullOrEmpty(token))
            {
                _tokens[$"{agentId}:{tenantId}"] = token;
            }
        }

        /// <summary>
        /// Retrieves a cached token for the specified agent and tenant.
        /// </summary>
        /// <param name="agentId">The agent identifier.</param>
        /// <param name="tenantId">The tenant identifier.</param>
        /// <returns>The cached token, or null if not found.</returns>
        public static Task<string?> GetTokenAsync(string agentId, string tenantId)
        {
            _tokens.TryGetValue($"{agentId}:{tenantId}", out var token);
            return Task.FromResult(token);
        }
    }
}
