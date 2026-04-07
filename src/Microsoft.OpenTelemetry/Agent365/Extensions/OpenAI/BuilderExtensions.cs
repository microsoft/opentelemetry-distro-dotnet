// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.OpenTelemetry.Agent365.Extensions.OpenAI;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenTelemetry.Agent365;
using global::OpenTelemetry.Trace;

/// <summary>
/// Extension methods for configuring Builder with OpenAI integration.
/// </summary>
internal static class BuilderExtensions
{
    /// <summary>
    /// Adds OpenAI integration to the builder.
    /// </summary>
    /// <param name="builder">The builder to configure.</param>
    /// <param name="enableRelatedSources">Whether to enable related tracing sources for OpenTelemetry.</param>
    /// <returns>The configured builder for method chaining.</returns>
    public static Builder WithOpenAI(this Builder builder, bool enableRelatedSources = true)
    {
        if (enableRelatedSources)
        {
            AppContext.SetSwitch("OpenAI.Experimental.EnableOpenTelemetry", true);
            builder.Services.AddOpenTelemetry()
                .WithTracing(tracing => tracing
                    .AddSource(OpenAITelemetryConstants.OpenAISourceWildcard)
                    .AddProcessor(new OpenAISpanProcessor()));
        }

        return builder;
    }
}