// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using Xunit;

namespace Microsoft.OpenTelemetry.AzureMonitor.Tests;

/// <summary>
/// xUnit collection that serializes all tests that modify environment variables.
/// Prevents parallel test execution from causing env var pollution.
/// </summary>
[CollectionDefinition("EnvironmentVariableTests", DisableParallelization = true)]
public class EnvironmentVariableTestsCollection : ICollectionFixture<EnvironmentVariableTestsFixture>
{
}

/// <summary>
/// Shared fixture for environment variable tests. Saves and restores env vars.
/// </summary>
public class EnvironmentVariableTestsFixture : IDisposable
{
    private readonly string? _originalConnectionString;
    private readonly string? _originalSampler;
    private readonly string? _originalSamplerArg;

    public EnvironmentVariableTestsFixture()
    {
        _originalConnectionString = Environment.GetEnvironmentVariable("APPLICATIONINSIGHTS_CONNECTION_STRING");
        _originalSampler = Environment.GetEnvironmentVariable("OTEL_TRACES_SAMPLER");
        _originalSamplerArg = Environment.GetEnvironmentVariable("OTEL_TRACES_SAMPLER_ARG");
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("APPLICATIONINSIGHTS_CONNECTION_STRING", _originalConnectionString);
        Environment.SetEnvironmentVariable("OTEL_TRACES_SAMPLER", _originalSampler);
        Environment.SetEnvironmentVariable("OTEL_TRACES_SAMPLER_ARG", _originalSamplerArg);
    }
}
