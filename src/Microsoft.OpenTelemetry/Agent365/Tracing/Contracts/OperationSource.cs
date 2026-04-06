// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.OpenTelemetry.Agent365.Tracing.Contracts
{
    /// <summary>
    /// Enum representing the source of an operation.
    /// </summary>
    public enum OperationSource
    {
        /// <summary>
        /// Operation executed by SDK.
        /// </summary>
        SDK,

        /// <summary>
        /// Operation executed by Gateway.
        /// </summary>
        Gateway,

        /// <summary>
        /// Operation executed by MCP Server.
        /// </summary>
        MCPServer
    }
}
