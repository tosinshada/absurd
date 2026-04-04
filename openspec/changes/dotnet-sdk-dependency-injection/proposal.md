## Why

The .NET SDK's `AbsurdClient` currently requires manual construction, making it awkward to use in ASP.NET Core and other hosted .NET applications that rely heavily on the built-in dependency injection container. Developers must manually wire up lifetimes, configure logging from the DI-provided `ILogger<T>`, and handle disposal — work the DI system is designed to do automatically.

## What Changes

- Add `IServiceCollection.AddAbsurd(...)` extension method to register `AbsurdClient` as a singleton service
- Support configuration via `IConfiguration` / `IOptions<AbsurdOptions>` pattern so connection strings and queue names can come from `appsettings.json`
- Integrate with `ILoggerFactory` so the client automatically uses the application's configured logging pipeline
- Provide a separate `Absurd.Sdk.DependencyInjection` NuGet package (or integrate into the existing package behind a conditional dependency)
- Expose `IAbsurdClient` interface to allow mocking in unit tests

## Capabilities

### New Capabilities

- `dotnet-sdk-di`: Extension methods and supporting types for registering and configuring `AbsurdClient` in a .NET DI container (`IServiceCollection`), including options binding from `IConfiguration` and automatic `ILoggerFactory` integration.

### Modified Capabilities

- `dotnet-sdk-client`: Introduce `IAbsurdClient` interface extracted from `AbsurdClient` to enable DI-friendly mocking; existing construction remains unchanged.

## Impact

- **Absurd.Sdk.csproj**: Add reference to `Microsoft.Extensions.DependencyInjection.Abstractions` and `Microsoft.Extensions.Options` (both already available in .NET 10 BCL via transitive deps; explicit reference for clarity)
- **AbsurdClient.cs**: Extract `IAbsurdClient` interface; no breaking changes to construction or public API
- New file(s): `DependencyInjection/ServiceCollectionExtensions.cs`, `DependencyInjection/AbsurdOptionsSetup.cs`
- Consumers: ASP.NET Core apps and Worker Service hosts gain a first-class `AddAbsurd()` registration path
