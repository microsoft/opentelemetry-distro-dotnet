// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Microsoft.Agents.A365.Observability.Runtime.Tracing.Contracts.Tools
{
    internal sealed class ToolCallJsonStringEnumConverter : JsonStringEnumConverter
    {
        public ToolCallJsonStringEnumConverter()
            : base(JsonNamingPolicy.SnakeCaseLower, allowIntegerValues: false)
        {
        }
    }
}
