// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Microsoft.Agents.A365.Observability.Runtime.Tracing
{
    internal sealed class SnakeCaseJsonStringEnumConverter : JsonStringEnumConverter
    {
        public SnakeCaseJsonStringEnumConverter()
            : base(JsonNamingPolicy.SnakeCaseLower, allowIntegerValues: false)
        {
        }
    }
}
