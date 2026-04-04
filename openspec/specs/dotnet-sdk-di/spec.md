## ADDED Requirements

### Requirement: Register AbsurdClient via AddAbsurd delegate overload
The SDK SHALL provide an `AddAbsurd(Action<AbsurdOptions>)` extension method on `IServiceCollection` that configures and registers `AbsurdClient` (and `IAbsurdClient`) as singletons.

#### Scenario: Fluent registration with connection string
- **WHEN** `services.AddAbsurd(opts => opts.ConnectionString = "Host=localhost;Database=mydb")` is called
- **THEN** `AbsurdClient` and `IAbsurdClient` are registered as singletons in the container
- **AND** resolving `IAbsurdClient` from the provider returns a configured `AbsurdClient` connected to the specified database

#### Scenario: Registration with queue name override
- **WHEN** `services.AddAbsurd(opts => { opts.ConnectionString = "..."; opts.QueueName = "payments"; })` is called
- **THEN** the resolved `IAbsurdClient` targets the `"payments"` queue by default

### Requirement: Register AbsurdClient via AddAbsurd IConfiguration overload
The SDK SHALL provide an `AddAbsurd(IConfiguration)` extension method on `IServiceCollection` that binds `AbsurdOptions` from the given configuration section and registers the client.

#### Scenario: Configuration section binding
- **WHEN** `appsettings.json` contains `{ "Absurd": { "ConnectionString": "Host=...", "QueueName": "orders" } }`
- **AND** `services.AddAbsurd(configuration.GetSection("Absurd"))` is called
- **THEN** the resolved `AbsurdClient` uses the connection string and queue name from configuration

#### Scenario: Missing configuration section falls back to defaults
- **WHEN** `services.AddAbsurd(configuration.GetSection("Absurd"))` is called and the section does not exist
- **THEN** `AbsurdClient` falls back to the same environment variable and default connection string chain as direct construction

### Requirement: Automatic ILoggerFactory integration
When `AbsurdClient` is registered via `AddAbsurd` and the caller has NOT set `AbsurdOptions.Log` explicitly, the SDK SHALL resolve `ILoggerFactory` from the DI container and assign `ILogger<AbsurdClient>` to the client's logger.

#### Scenario: Logger auto-wired from DI
- **WHEN** `services.AddLogging(...)` and `services.AddAbsurd(opts => opts.ConnectionString = "...")` are both called
- **AND** `AbsurdOptions.Log` is not set explicitly
- **THEN** the client uses the application's configured logging pipeline for its log output

#### Scenario: Explicit logger takes precedence
- **WHEN** `services.AddAbsurd(opts => { opts.Log = myCustomLogger; })` is called
- **THEN** the client uses `myCustomLogger` and does NOT override it with a DI-resolved logger

### Requirement: IAbsurdClient resolved from DI container
Both `IAbsurdClient` and `AbsurdClient` SHALL be resolvable from the DI container after `AddAbsurd` is called, and SHALL resolve to the same singleton instance.

#### Scenario: Resolve by interface
- **WHEN** `serviceProvider.GetRequiredService<IAbsurdClient>()` is called
- **THEN** a non-null `AbsurdClient` instance is returned

#### Scenario: Interface and concrete class share same instance
- **WHEN** both `serviceProvider.GetRequiredService<IAbsurdClient>()` and `serviceProvider.GetRequiredService<AbsurdClient>()` are called
- **THEN** both calls return the same object reference (singleton)

### Requirement: AbsurdClient disposed with service provider
When the root `IServiceProvider` is disposed, the registered `AbsurdClient` singleton SHALL have `DisposeAsync` called, releasing its owned `NpgsqlDataSource` if one was created internally.

#### Scenario: Disposal on container shutdown
- **WHEN** the host shuts down and the root service provider is disposed
- **THEN** `AbsurdClient.DisposeAsync` is invoked
- **AND** any internally created `NpgsqlDataSource` is closed and released

### Requirement: External NpgsqlDataSource supplied via delegate
Callers SHALL be able to supply an existing `NpgsqlDataSource` through the delegate overload; the SDK SHALL NOT dispose the externally-supplied data source.

#### Scenario: External data source is not disposed
- **WHEN** `services.AddAbsurd(opts => opts.DataSource = myDataSource)` is called
- **AND** the service provider is disposed
- **THEN** `myDataSource` is NOT disposed by the SDK
