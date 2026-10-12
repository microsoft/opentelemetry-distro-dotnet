# Microsoft.OpenTelemetry.A365.Contracts

> **Important:** This package is a shared dependency of `Microsoft.OpenTelemetry` and `Microsoft.OpenTelemetry.A365.Etw`. Do not install or consume it directly as a standalone package.

This package contains shared Agent365 observability contracts, DTOs, builders, and runtime helpers used by both parent packages. It is published to NuGet.org so that those packages can resolve it as a dependency, not as a separate onboarding package for applications.

## Install Microsoft.OpenTelemetry instead

Add [Microsoft.OpenTelemetry](https://www.nuget.org/packages/Microsoft.OpenTelemetry) to your application. It brings in the Contracts package automatically as a transitive dependency.

```bash
dotnet add package Microsoft.OpenTelemetry
```

See the [Microsoft.OpenTelemetry documentation](https://github.com/microsoft/opentelemetry-distro-dotnet#readme) for configuration and usage.

For Microsoft internal standalone ETW integration scenarios, `Microsoft.OpenTelemetry.A365.Etw` also brings in the Contracts package automatically. See the [ETW package README](https://www.nuget.org/packages/Microsoft.OpenTelemetry.A365.Etw) for its intended audience.
