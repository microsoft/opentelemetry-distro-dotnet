// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;

namespace Microsoft.OpenTelemetry.AzureMonitor.SdkStats
{
    /// <summary>
    /// Bit flags reported as the <c>feature</c> dimension of <c>type=1</c> Feature SDKStats.
    /// </summary>
    /// <remarks>
    /// This bit space is independent from <see cref="DistroFeature"/>. Values are stable;
    /// new instrumentations must be appended only because backend decoders use these indexes.
    /// </remarks>
    [Flags]
    internal enum DistroInstrumentation : ulong
    {
        None = 0,
        AzureSdk = 1UL << 0,
        AspNetCore = 1UL << 1,
        HttpClient = 1UL << 2,
        SqlClient = 1UL << 3,
        OpenAI = 1UL << 4,
        SemanticKernel = 1UL << 5,
        AgentFramework = 1UL << 6,
        Agent365 = 1UL << 7,

        // Bits 8-63 reserved for future instrumentations. Do not renumber.
    }
}
