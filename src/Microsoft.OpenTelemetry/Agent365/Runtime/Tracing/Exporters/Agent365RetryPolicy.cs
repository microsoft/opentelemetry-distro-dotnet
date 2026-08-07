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

            return TimeSpan.FromMilliseconds(500 * (1 << retryIndex));
        }
    }
}
