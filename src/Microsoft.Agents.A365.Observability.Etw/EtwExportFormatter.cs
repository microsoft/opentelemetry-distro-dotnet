// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Agents.A365.Observability.Runtime.Common;
using Microsoft.Extensions.Logging.Abstractions;
using System.Collections.Generic;

namespace Microsoft.Agents.A365.Observability.Runtime.Etw
{
    /// <summary>
    /// Formats ETW log payload dictionaries into the existing JSON envelope.
    /// </summary>
    public sealed class EtwExportFormatter
    {
        private static readonly ExportFormatter SharedFormatter =
            new ExportFormatter(NullLogger<ExportFormatter>.Instance);

        /// <summary>
        /// Formats operation data into the ETW JSON payload shape.
        /// </summary>
        /// <param name="data">The operation data to serialize.</param>
        /// <returns>The serialized ETW JSON payload.</returns>
        public string FormatLogData(IDictionary<string, object?> data)
        {
            return SharedFormatter.FormatLogData(data);
        }
    }
}
