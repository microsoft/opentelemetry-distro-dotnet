// ------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// ------------------------------------------------------------------------------

namespace Microsoft.OpenTelemetry.Agent365.Extensions.AgentFramework;

/// <summary>
/// Constants for Agent Framework activity source names.
/// </summary>
internal static class BuilderExtensions
{
    /// <summary>
    /// The activity source name for Agent Framework tracing.
    /// </summary>
    public const string AgentFrameworkSource = "Experimental.Microsoft.Agents.AI";

    /// <summary>
    /// The activity source name for Agent Framework agent tracing.
    /// </summary>
    public const string AgentFrameworkAgentSource = "Experimental.Microsoft.Agents.AI.Agent";

    /// <summary>
    /// The activity source name for Agent Framework chat client tracing.
    /// </summary>
    public const string AgentFrameworkChatClientSource = "Experimental.Microsoft.Agents.AI.ChatClient";
}