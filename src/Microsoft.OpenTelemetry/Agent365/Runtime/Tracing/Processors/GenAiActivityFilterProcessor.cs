// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.Agents.A365.Observability.Runtime.Tracing.Processors
{
    using Microsoft.Agents.A365.Observability.Runtime.Tracing.Scopes;
    using global::OpenTelemetry;
    using System;
    using System.Diagnostics;

    /// <summary>
    /// A composite processor that filters activities, forwarding only genAI spans
    /// (those with a recognized <c>gen_ai.operation.name</c>) to the wrapped inner processor.
    /// Non-genAI activities are silently dropped before reaching the inner processor.
    /// </summary>
    /// <remarks>
    /// This processor is designed to wrap a <see cref="BatchActivityExportProcessor"/> or
    /// <see cref="BatchActivityExportProcessorAsync"/> so that the exporter only receives
    /// genAI spans without needing its own filtering logic.
    /// </remarks>
    public sealed class GenAiActivityFilterProcessor : BaseProcessor<Activity>
    {
        private readonly BaseProcessor<Activity> _inner;

        /// <summary>
        /// Initializes a new instance of the <see cref="GenAiActivityFilterProcessor"/> class.
        /// </summary>
        /// <param name="inner">The inner processor that receives only genAI activities.</param>
        public GenAiActivityFilterProcessor(BaseProcessor<Activity> inner)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

        /// <inheritdoc/>
        public override void OnEnd(Activity data)
        {
            if (IsGenAiActivity(data))
            {
                _inner.OnEnd(data);
            }
        }

        /// <inheritdoc/>
        protected override bool OnForceFlush(int timeoutMilliseconds)
        {
            return _inner.ForceFlush(timeoutMilliseconds);
        }

        /// <inheritdoc/>
        protected override bool OnShutdown(int timeoutMilliseconds)
        {
            return _inner.Shutdown(timeoutMilliseconds);
        }

        /// <inheritdoc/>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _inner.Dispose();
            }

            base.Dispose(disposing);
        }

        private static bool IsGenAiActivity(Activity activity)
        {
            var operationName = activity.GetTagItem(OpenTelemetryConstants.GenAiOperationNameKey) as string;
            if (string.IsNullOrEmpty(operationName))
            {
                // Fall back to baggage
                operationName = activity.GetBaggageItem(OpenTelemetryConstants.GenAiOperationNameKey);
            }

            return !string.IsNullOrEmpty(operationName) && OpenTelemetryConstants.GenAiOperationNames.Contains(operationName!);
        }
    }
}
