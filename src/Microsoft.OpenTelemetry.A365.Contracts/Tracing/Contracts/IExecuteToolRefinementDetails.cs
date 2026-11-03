// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;

namespace Microsoft.Agents.A365.Observability.Runtime.Tracing.Contracts
{
    /// <summary>
    /// Explicit semantic-convention metadata for a specialized execute-tool operation.
    /// </summary>
    /// <remarks>Supply metadata from the tool API or runtime state, including for failed attempts.
    /// Do not infer a refinement from a tool's name or opaque arguments.</remarks>
    public interface IExecuteToolRefinementDetails
    {
        /// <summary>Resolves the execute-tool span name using the refinement's semantic-convention fallback rules.</summary>
        /// <param name="toolName">The tool name exposed by the runtime.</param>
        /// <returns>The refined execute-tool span name.</returns>
        string GetSpanName(string toolName);

        /// <summary>Gets the available semantic-convention attributes, omitting empty strings.</summary>
        /// <returns>The explicitly supplied skill and process attributes.</returns>
        IEnumerable<KeyValuePair<string, object>> GetAttributes();
    }
}
