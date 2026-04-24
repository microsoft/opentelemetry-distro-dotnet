// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.OpenTelemetry;

/// <summary>
/// Marker class registered as a singleton to detect duplicate calls to
/// <see cref="MicrosoftOpenTelemetryBuilderExtensions.UseMicrosoftOpenTelemetry"/>.
/// </summary>
internal sealed class UseMicrosoftOpenTelemetryRegistration
{
    public static readonly UseMicrosoftOpenTelemetryRegistration Instance = new();

    private UseMicrosoftOpenTelemetryRegistration()
    {
    }
}
