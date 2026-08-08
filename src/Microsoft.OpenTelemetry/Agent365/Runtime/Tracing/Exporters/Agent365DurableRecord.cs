// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Microsoft.Agents.A365.Observability.Runtime.Tracing.Exporters
{
    /// <summary>
    /// Versioned durable record persisted by the store-and-forward delivery pipeline.
    /// Must never contain bearer tokens or other credentials.
    /// </summary>
    internal sealed class Agent365DurableRecord
    {
        private const int CurrentVersion = 1;

        internal Agent365DurableRecord(
            string tenantId,
            string agentId,
            string? agenticUserId,
            bool useS2SEndpoint,
            string payload,
            DateTimeOffset createdAtUtc)
        {
            TenantId = tenantId;
            AgentId = agentId;
            AgenticUserId = agenticUserId;
            UseS2SEndpoint = useS2SEndpoint;
            Payload = payload;
            CreatedAtUtc = createdAtUtc;
        }

        [JsonConstructor]
        internal Agent365DurableRecord(
            int version,
            string tenantId,
            string agentId,
            string? agenticUserId,
            bool useS2SEndpoint,
            string payload,
            DateTimeOffset createdAtUtc)
            : this(tenantId, agentId, agenticUserId, useS2SEndpoint, payload, createdAtUtc)
        {
            Version = version;
        }

        public int Version { get; } = CurrentVersion;
        public string TenantId { get; }
        public string AgentId { get; }
        public string? AgenticUserId { get; }
        public bool UseS2SEndpoint { get; }
        public string Payload { get; }
        public DateTimeOffset CreatedAtUtc { get; }

        internal static byte[] Serialize(Agent365DurableRecord record) =>
            JsonSerializer.SerializeToUtf8Bytes(record);

        internal static bool TryDeserialize(
            ReadOnlySpan<byte> data,
#if NETSTANDARD2_0
            out Agent365DurableRecord? record)
#else
            [NotNullWhen(true)] out Agent365DurableRecord? record)
#endif
        {
            try
            {
                Agent365DurableRecord? candidate;
#if NET
                candidate = JsonSerializer.Deserialize<Agent365DurableRecord>(data);
#else
                var json = Encoding.UTF8.GetString(data.ToArray());
                candidate = JsonSerializer.Deserialize<Agent365DurableRecord>(json);
#endif
                if (candidate?.Version != CurrentVersion
                    || string.IsNullOrWhiteSpace(candidate.TenantId)
                    || string.IsNullOrWhiteSpace(candidate.AgentId)
                    || string.IsNullOrWhiteSpace(candidate.Payload))
                {
                    record = null;
                    return false;
                }

                record = candidate;
                return true;
            }
            catch (JsonException)
            {
                record = null;
                return false;
            }
        }
    }
}
