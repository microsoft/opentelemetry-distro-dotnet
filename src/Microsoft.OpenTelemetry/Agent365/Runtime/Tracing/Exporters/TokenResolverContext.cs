// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.Agents.A365.Observability.Runtime.Tracing.Exporters
{
    /// <summary>
    /// Provides contextual information to the token resolver delegate.
    /// This class is extensible — new properties can be added in future versions
    /// without breaking existing resolver implementations.
    /// </summary>
    public class TokenResolverContext
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="TokenResolverContext"/> class.
        /// </summary>
        /// <param name="agentId">The agent identifier.</param>
        /// <param name="tenantId">The tenant identifier.</param>
        /// <param name="agenticUserId">The optional agentic user identifier.</param>
        public TokenResolverContext(string agentId, string tenantId, string? agenticUserId)
        {
            AgentId = agentId;
            TenantId = tenantId;
            AgenticUserId = agenticUserId;
        }

        /// <summary>
        /// Gets the agent identifier.
        /// </summary>
        public string AgentId { get; }

        /// <summary>
        /// Gets the tenant identifier.
        /// </summary>
        public string TenantId { get; }

        /// <summary>
        /// Gets the agentic user identifier, or <c>null</c> if not available.
        /// </summary>
        public string? AgenticUserId { get; }
    }
}
