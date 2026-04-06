// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.OpenTelemetry.Agent365.Tracing.Contracts
{
    /// <summary>
    /// Constants for tool type identifiers used in observability tracing.
    /// </summary>
    public sealed class ToolType
    {
        /// <summary>
        /// Represents a function tool type.
        /// </summary>
        public static readonly string Function = "function";
        
        /// <summary>
        /// Represents an extension tool type.
        /// </summary>
        public static readonly string Extension = "extension";
        
        /// <summary>
        /// Represents a datastore tool type.
        /// </summary>
        public static readonly string Datastore = "datastore";
    }
}