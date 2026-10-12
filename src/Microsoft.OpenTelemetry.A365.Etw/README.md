# Microsoft.OpenTelemetry.A365.Etw

> **Important:** This package is intended for Microsoft internal Agent365 ETW integration scenarios. Publication on NuGet.org does not imply that it is intended for general third-party consumption. Application developers should use `Microsoft.OpenTelemetry` instead.

This package provides Agent365 observability ETW event emission and payload formatting for Microsoft internal integrations. It can be consumed as a standalone package in those scenarios and automatically resolves `Microsoft.OpenTelemetry.A365.Contracts` as a transitive dependency. Do not install the Contracts package directly.

## Guidance for application developers

Add [Microsoft.OpenTelemetry](https://www.nuget.org/packages/Microsoft.OpenTelemetry) to your application:

```bash
dotnet add package Microsoft.OpenTelemetry
```

See the [Microsoft.OpenTelemetry documentation](https://github.com/microsoft/opentelemetry-distro-dotnet#readme) for configuration and usage.
