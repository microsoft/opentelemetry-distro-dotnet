// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;

namespace Microsoft.Agents.A365.Observability.Runtime.Tracing
{
    /// <summary>
    /// Sanitizes URLs for telemetry attributes.
    /// </summary>
    public static class UrlSanitizer
    {
        private const string RedactedValue = "REDACTED";

        private static readonly HashSet<string> SensitiveQueryParameterNames =
            new HashSet<string>(StringComparer.Ordinal)
            {
                "X-Amz-Signature",
                "X-Amz-Credential",
                "X-Amz-Security-Token",
                "sig",
                "X-Goog-Signature",
                "access_token",
                "client_secret",
                "id_token",
                "refresh_token",
            };

        /// <summary>
        /// Removes URL user information and redacts known sensitive query parameter values.
        /// </summary>
        /// <param name="url">The absolute URL to sanitize.</param>
        /// <returns>The sanitized absolute URL.</returns>
        public static string Sanitize(Uri url)
        {
            if (url == null)
            {
                throw new ArgumentNullException(nameof(url));
            }

            var builder = new UriBuilder(url)
            {
                UserName = string.Empty,
                Password = string.Empty,
            };

            var query = builder.Query;
            if (!string.IsNullOrEmpty(query))
            {
                builder.Query = SanitizeQuery(query.Substring(1));
            }

            return builder.Uri.AbsoluteUri;
        }

        private static string SanitizeQuery(string query)
        {
            var parameters = query.Split('&');
            for (var i = 0; i < parameters.Length; i++)
            {
                var separatorIndex = parameters[i].IndexOf('=');
                var encodedName = separatorIndex >= 0
                    ? parameters[i].Substring(0, separatorIndex)
                    : parameters[i];
                var name = Uri.UnescapeDataString(encodedName.Replace("+", " "));

                if (SensitiveQueryParameterNames.Contains(name))
                {
                    parameters[i] = $"{encodedName}={RedactedValue}";
                }
            }

            return string.Join("&", parameters);
        }
    }
}
