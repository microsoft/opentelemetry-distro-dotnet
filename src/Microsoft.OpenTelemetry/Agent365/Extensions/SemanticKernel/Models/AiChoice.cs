// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.OpenTelemetry.Agent365.Extensions.SemanticKernel.Models;

using System.Text.Json.Serialization;

/// <summary>
/// Represents an AI choice containing a message with role and tool calls information.
/// </summary>
internal sealed class AiChoice
{
    /// <summary>
    /// Gets or sets the AI choice message, which includes role and tool calls information.
    /// </summary>
    [JsonPropertyName("message")]
    public AiChoiceMessage? Message { get; set; }
}

/// <summary>
/// Represents an AI choice message, including the role and associated tool calls.
/// </summary>
internal sealed class AiChoiceMessage
{
    /// <summary>
    /// Gets or sets the role of the AI message.
    /// </summary>
    [JsonPropertyName("role")]
    public string? Role { get; set; }

    /// <summary>
    /// Gets or sets the direct content of the AI message.
    /// </summary>
    [JsonPropertyName("content")]
    public string? Content { get; set; }

    /// <summary>
    /// Gets or sets the collection of tool calls associated with the AI message.
    /// </summary>
    [JsonPropertyName("tool_calls")]
    public List<AiChoiceToolCall>? ToolCalls { get; set; }
}

/// <summary>
/// Represents a tool call within an AI choice message.
/// </summary>
internal sealed class AiChoiceToolCall
{
    /// <summary>
    /// Gets or sets the function associated with the tool call.
    /// </summary>
    [JsonPropertyName("function")]
    public AiChoiceFunction? Function { get; set; }
}

/// <summary>
/// Represents a function within a tool call.
/// </summary>
internal sealed class AiChoiceFunction
{
    /// <summary>
    /// Gets or sets the arguments for the function.
    /// </summary>
    [JsonPropertyName("arguments")]
    public AiChoiceArguments? Arguments { get; set; }
}

/// <summary>
/// Represents the arguments for an AI choice function.
/// </summary>
internal sealed class AiChoiceArguments
{
    /// <summary>
    /// Gets or sets the message body for the function arguments.
    /// </summary>
    [JsonPropertyName("messageBody")]
    public string? MessageBody { get; set; }
}