// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Agents.A365.Observability.Runtime.Tracing;
using Microsoft.Agents.A365.Observability.Runtime.Tracing.Contracts;
using Microsoft.Agents.A365.Observability.Runtime.Tracing.Contracts.Tools;
using System;

namespace Microsoft.Agents.A365.Observability.Runtime.Etw
{
    /// <summary>
    /// Extension methods for <see cref="IA365EtwLogger{T}"/>.
    /// </summary>
    public static class A365EtwLoggerExtensions
    {
        /// <summary>
        /// Logs an execute_tool event with a structured result payload without changing the <see cref="IA365EtwLogger{T}"/> contract.
        /// </summary>
        /// <typeparam name="T">The logger category type.</typeparam>
        /// <param name="logger">The ETW logger.</param>
        /// <param name="toolCallDetails">The details of the tool call.</param>
        /// <param name="result">Structured result content to include in the log.</param>
        /// <param name="agentDetails">The details of the agent (includes tenant ID).</param>
        /// <param name="conversationId">The required conversation ID.</param>
        /// <param name="startTime">Optional start time of the tool execution.</param>
        /// <param name="endTime">Optional end time of the tool execution.</param>
        /// <param name="spanId">Optional span ID for tracing.</param>
        /// <param name="parentSpanId">Optional parent span ID for tracing.</param>
        /// <param name="channel">Optional channel information for the tool call.</param>
        /// <param name="callerDetails">Optional details of the caller.</param>
        /// <param name="traceId">Optional trace ID for distributed tracing.</param>
        /// <param name="error">Optional exception describing a failure; sets an OTel error status and the <c>error.type</c> attribute.</param>
        public static void LogToolCall<T>(
            this IA365EtwLogger<T> logger,
            ToolCallDetails toolCallDetails,
            ExecuteToolCallResult result,
            AgentDetails agentDetails,
            string conversationId,
            DateTimeOffset? startTime = null,
            DateTimeOffset? endTime = null,
            string? spanId = null,
            string? parentSpanId = null,
            Channel? channel = null,
            CallerDetails? callerDetails = null,
            string? traceId = null,
            Exception? error = null)
        {
            if (result == null)
            {
                throw new ArgumentNullException(nameof(result));
            }

            logger.LogToolCall(
                toolCallDetails,
                agentDetails,
                conversationId,
                ExecuteToolPayloadSerializer.Serialize(result),
                startTime,
                endTime,
                spanId,
                parentSpanId,
                channel,
                callerDetails,
                traceId,
                error);
        }
    }
}
