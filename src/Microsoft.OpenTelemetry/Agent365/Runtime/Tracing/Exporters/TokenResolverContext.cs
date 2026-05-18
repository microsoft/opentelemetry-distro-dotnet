// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;

namespace Microsoft.Agents.A365.Observability.Runtime.Tracing.Exporters
{
    /// <summary>
    /// Provides contextual information to the token resolver delegate.
    /// <para>
    /// <see cref="AgentId"/> and <see cref="TenantId"/> identify the cache key.
    /// Additional contextual data (e.g. agentic user ID, which is the AAD Object ID) is available via the
    /// <see cref="Metadata"/> dictionary and corresponding convenience accessors.
    /// </para>
    /// </summary>
    public class TokenResolverContext
    {
        /// <summary>
        /// Well-known metadata key for the agentic user identifier (AAD Object ID).
        /// </summary>
        public const string AgenticUserIdKey = "AgenticUserId";

        /// <summary>
        /// Initializes a new instance of the <see cref="TokenResolverContext"/> class.
        /// </summary>
        /// <param name="agentId">The agent identifier (cache key).</param>
        /// <param name="tenantId">The tenant identifier (cache key).</param>
        /// <param name="metadata">Optional metadata dictionary with additional context.</param>
        public TokenResolverContext(string agentId, string tenantId, IReadOnlyDictionary<string, string>? metadata = null)
        {
            AgentId = agentId;
            TenantId = tenantId;
            Metadata = metadata ?? new Dictionary<string, string>();
        }

        /// <summary>
        /// Gets the agent identifier (part of the cache key).
        /// </summary>
        public string AgentId { get; }

        /// <summary>
        /// Gets the tenant identifier (part of the cache key).
        /// </summary>
        public string TenantId { get; }

        /// <summary>
        /// Gets additional contextual metadata associated with this token resolution request.
        /// Use well-known keys such as <see cref="AgenticUserIdKey"/> or custom keys as needed.
        /// </summary>
        public IReadOnlyDictionary<string, string> Metadata { get; }

        /// <summary>
        /// Gets the agentic user identifier (AAD Object ID) from metadata, or <c>null</c> if not present.
        /// Convenience accessor for <c>Metadata[<see cref="AgenticUserIdKey"/>]</c>.
        /// </summary>
        public string? AgenticUserId =>
            Metadata.TryGetValue(AgenticUserIdKey, out var value) ? value : null;
    }
}
