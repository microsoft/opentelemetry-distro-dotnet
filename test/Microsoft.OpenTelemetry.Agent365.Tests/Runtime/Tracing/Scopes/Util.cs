// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.OpenTelemetry.Agent365.Tests.Tracing.Scopes;

using Microsoft.OpenTelemetry.Agent365.Tracing.Contracts;

public static class Util
{
    public static AgentDetails GetAgentDetails() =>
        new AgentDetails("agentId", "Test Agent", "A test agent for unit testing.");

    public static Request GetDefaultRequest() =>
        new Request(content: "test");
}
