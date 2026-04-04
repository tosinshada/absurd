## 1. Package Setup

- [x] 1.1 Add `Microsoft.Extensions.DependencyInjection.Abstractions` and `Microsoft.Extensions.Options` package references to `Absurd.Sdk.csproj`
- [x] 1.2 Create `DependencyInjection/` folder under `Absurd.Sdk/`

## 2. IAbsurdClient Interface

- [x] 2.1 Create `IAbsurdClient.cs` in `Absurd.Sdk/` with the full client interface (SpawnAsync, FetchTaskResultAsync, AwaitTaskResultAsync, EmitEventAsync, CancelTaskAsync, RetryTaskAsync, BindToConnection, RegisterTask, StartWorkerAsync)
- [x] 2.2 Update `AbsurdClient.cs` to declare `public sealed class AbsurdClient : IAsyncDisposable, IAbsurdClient`
- [x] 2.3 Verify all interface methods match existing `AbsurdClient` public signatures exactly (no signature changes)

## 3. ServiceCollectionExtensions

- [x] 3.1 Create `DependencyInjection/ServiceCollectionExtensions.cs` with a static class `AbsurdServiceCollectionExtensions`
- [x] 3.2 Implement `AddAbsurd(this IServiceCollection services, Action<AbsurdOptions> configure)` overload — calls `services.Configure<AbsurdOptions>(configure)` and registers the singleton factory
- [x] 3.3 Implement `AddAbsurd(this IServiceCollection services, IConfiguration configuration)` overload — calls `services.Configure<AbsurdOptions>(configuration)` and delegates to the common registration
- [x] 3.4 Implement the shared singleton factory: resolve `IOptions<AbsurdOptions>`, resolve `ILoggerFactory`, set `options.Log = loggerFactory.CreateLogger<AbsurdClient>()` when `options.Log` is `null`, construct and return `AbsurdClient`
- [x] 3.5 Register both `AbsurdClient` (concrete) and `IAbsurdClient` (interface) pointing to the same singleton — use `services.AddSingleton<AbsurdClient>(factory)` then `services.AddSingleton<IAbsurdClient>(sp => sp.GetRequiredService<AbsurdClient>())`

## 4. Tests

- [x] 4.1 Add `Microsoft.Extensions.DependencyInjection` and `Microsoft.Extensions.Configuration` references to `Absurd.Sdk.Tests.csproj`
- [x] 4.2 Write test: `AddAbsurd` with delegate configures `ConnectionString` and resolves non-null `IAbsurdClient`
- [x] 4.3 Write test: `IAbsurdClient` and `AbsurdClient` resolve to the same singleton instance
- [x] 4.4 Write test: explicit `Log` on options is not overridden by DI logger factory
- [x] 4.5 Write test: `AddAbsurd(IConfiguration)` binds `QueueName` from in-memory configuration
- [x] 4.6 Write test: `AbsurdClient` implements `IAbsurdClient` (type assignment check)

## 5. Documentation

- [x] 5.1 Add a "Dependency Injection" section to `sdks/dotnet/README.md` (or create it) with `AddAbsurd` usage examples for both overloads
- [x] 5.2 Add XML doc comments to `AddAbsurd` overloads and `IAbsurdClient`
- [x] 5.3 Update `docs/sdk-dotnet.md` with DI registration section and configuration table
