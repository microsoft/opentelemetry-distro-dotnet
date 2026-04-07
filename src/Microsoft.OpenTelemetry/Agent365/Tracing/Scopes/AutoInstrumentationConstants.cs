// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.OpenTelemetry.Agent365.Tracing.Scopes
{
    /// <summary>
    /// Constants used for auto-instrumentation.
    /// </summary>
    internal static class AutoInstrumentationConstants
    {
        /// <summary> The key for the input to a GenAI agent invocation. </summary>
        /// <remarks> Set by the Semantic Kernel OpenTelemetry integration for agent invocations.</remarks>
        public const string GenAiInvocationInputKey = "gen_ai.agent.invocation_input";

        /// <summary> The key for the output of a GenAI agent invocation. </summary>
        /// <remarks> Set by the Semantic Kernel OpenTelemetry integration for agent invocations.</remarks>
        public const string GenAiInvocationOutputKey = "gen_ai.agent.invocation_output";
    }
}
