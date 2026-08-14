// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Text;
using FluentAssertions;
using Microsoft.Agents.A365.Observability.Runtime.Tracing.Exporters;

namespace Microsoft.OpenTelemetry.Agent365.Tests.Runtime.Tracing.Exporters;

[TestClass]
public sealed class Agent365DurableRecordTests
{
    [TestMethod]
    public void RoundTripsCompleteChunkWithoutToken()
    {
        var record = new Agent365DurableRecord(
            tenantId: "tenant",
            agentId: "agent",
            agenticUserId: "user",
            useS2SEndpoint: true,
            payload: """{"resourceSpans":[{"id":"1"}]}""",
            createdAtUtc: new DateTimeOffset(2026, 8, 8, 12, 0, 0, TimeSpan.Zero));

        var bytes = Agent365DurableRecord.Serialize(record);
        Agent365DurableRecord.TryDeserialize(bytes, out var result).Should().BeTrue();

        result.Should().BeEquivalentTo(record);
        Encoding.UTF8.GetString(bytes).Should().NotContain("Bearer");
        Encoding.UTF8.GetString(bytes).Should().NotContain("token");
    }

    [TestMethod]
    public void RejectsUnsupportedSchemaVersion()
    {
        var bytes = Encoding.UTF8.GetBytes(
            """{"version":99,"tenantId":"t","agentId":"a","useS2SEndpoint":false,"payload":"{}","createdAtUtc":"2026-08-08T12:00:00Z"}""");

        Agent365DurableRecord.TryDeserialize(bytes, out _).Should().BeFalse();
    }

    [DataTestMethod]
    [DataRow("""{}""")]
    [DataRow("""{"version":1,"tenantId":"","agentId":"a","payload":"{}"}""")]
    [DataRow("""{"version":1,"tenantId":"t","agentId":"","payload":"{}"}""")]
    public void RejectsIncompleteRecord(string json)
    {
        Agent365DurableRecord.TryDeserialize(Encoding.UTF8.GetBytes(json), out _)
            .Should().BeFalse();
    }
}
