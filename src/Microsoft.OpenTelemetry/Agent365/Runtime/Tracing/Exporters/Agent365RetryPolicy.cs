// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Net;
using System.Net.Http.Headers;

namespace Microsoft.Agents.A365.Observability.Runtime.Tracing.Exporters
{
    internal static class Agent365RetryPolicy
    {
        internal const int MaxRetries = 3;
        internal const int MaxAttempts = MaxRetries + 1;
        internal static readonly TimeSpan MaxRetryAfter = TimeSpan.FromSeconds(60);

        internal static bool IsRetryable(HttpStatusCode statusCode)
        {
            var code = (int)statusCode;
            return statusCode == HttpStatusCode.RequestTimeout
                || code == 429
                || code >= 500 && code <= 599;
        }

        internal static TimeSpan GetDelay(
            HttpResponseHeaders headers,
            int retryIndex,
            DateTimeOffset utcNow)
        {
            var retryAfter = headers.RetryAfter;
            TimeSpan? requestedDelay = retryAfter?.Delta;

            if (!requestedDelay.HasValue && retryAfter?.Date is DateTimeOffset retryDate)
            {
                var dateDelay = retryDate - utcNow;
                if (dateDelay > TimeSpan.Zero)
                {
                    requestedDelay = dateDelay;
                }
            }

            if (requestedDelay.HasValue && requestedDelay.Value >= TimeSpan.Zero)
            {
                return requestedDelay.Value > MaxRetryAfter
                    ? MaxRetryAfter
                    : requestedDelay.Value;
            }

            return GetFallbackDelay(retryIndex);
        }

        /// <summary>
        /// Exponential backoff fallback used when the response carries no honorable
        /// <c>Retry-After</c> header and for transport-level exceptions (timeout / connection
        /// failures). Produces 500 ms, 1 s, and 2 s for retry indexes 0, 1, and 2.
        /// </summary>
        /// <param name="retryIndex">Zero-based index of the retry about to be scheduled.</param>
        internal static TimeSpan GetFallbackDelay(int retryIndex) =>
            TimeSpan.FromMilliseconds(500 * (1 << retryIndex));
    }
}
