using Microsoft.OpenTelemetry.Agent365.S2S.Demo;

namespace Microsoft.OpenTelemetry.Agent365.S2S.Demo.Tests;

internal static class TestData
{
    internal static SampleOptions CreateOptions() =>
        new(
            new Uri("https://login.microsoftonline.com"),
            "11111111-1111-1111-1111-111111111111",
            "sample-secret",
            "22222222-2222-2222-2222-222222222222",
            "33333333-3333-3333-3333-333333333333",
            "44444444-4444-4444-4444-444444444444",
            "production");
}
