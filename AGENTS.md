# zSCOUT Hardware Communication Test Suite

Blazor Server dashboard and CLI tool that verifies peripherals (GPS, SDR, Wi-Fi HaLow, compass) on a Raspberry Pi CM5 can be communicated with from Docker containers.

## Commands

- Build: `dotnet build zSCOUT-hw-test.slnx`
- Test: `dotnet test`
- Run dashboard: `cd src/ZScout.HwTest.App && dotnet run`
- Run CLI: `dotnet run --project src/ZScout.HwTest.Cli`
- Docker build: `docker build -f deploy/Dockerfile -t zscout-hw-test .`
- Parity smoke test: `bash scripts/run-parity-smoke.sh`

## Tech stack

- .NET 10, C# (latest LangVersion), `net10.0` target framework
- Blazor Server (dashboard) + ASP.NET Core Minimal APIs (REST)
- SignalR for real-time hardware status streaming
- xUnit for tests, Serilog for structured logging
- Docker (multi-stage ARM64 build), targets Raspberry Pi CM5

## Project structure

```
src/
  ZScout.HwTest.Contracts/   Shared domain models — enums, records. No logic.
  ZScout.HwTest.App/         Blazor Server dashboard + REST API + orchestrator
    Api/                     Minimal API endpoints (static extension methods)
    Auth/                    Cookie auth, user store, authorization policies
    Dashboard/               Blazor components, SignalR hubs, UI services
    Hardware/                Peripheral adapters (GPS, SDR, HaLow, Compass)
      Common/                IHardwareAdapter interface, ProcessHelper, DiagnosticEnvelope
    Persistence/             File-backed repositories, retention, export
    Runs/                    Run orchestration, locking, verdicts
    Streams/                 Live telemetry publishing
  ZScout.HwTest.Cli/         Headless CLI runner
tests/                       Test projects (xUnit)
deploy/                      Dockerfile, docker-compose.yml, offline export helper
scripts/                     Parity smoke test
specs/                       Speckit design artifacts
```

## Architecture rules

- **Layer separation**: API endpoints are thin — delegate to services/repositories. Business logic lives in `Runs/` and `Hardware/`. Persistence is file-backed via `IRepository<T>`.
- **Hardware adapters**: Every peripheral implements `IHardwareAdapter`. Adapters must never throw — the orchestrator catches exceptions and records them as `Unavailable` evidence (T024). New peripherals = new adapter class + register in DI.
- **Contracts project**: `ZScout.HwTest.Contracts` holds shared domain models only (enums, records). No logic, no dependencies on App or Cli.
- **Run lifecycle**: Queued → Running → AwaitingVerdict → Completed/Failed/Stopped. All transitions go through `RunOrchestrator`.
- **Concurrency**: Adapters execute concurrently via `Task.WhenAll`. `RunLockService` prevents overlapping runs.

## Code style

- Nullable reference types enabled (`<Nullable>enable</Nullable>`), warnings are errors
- Tab indentation (not spaces)
- File-scoped namespaces (`namespace Foo.Bar;`)
- `sealed` on classes that are not designed for inheritance
- Constructor DI with `private readonly` fields prefixed with `_`
- Async methods suffixed with `Async` and accept `CancellationToken ct = default`
- XML doc comments (`<summary>`) on all public interfaces, adapters, and orchestration methods
- Structured logging with message templates — `_logger.LogInformation("Run {RunId} started", runId)` — never string interpolation in log calls
- Use `record` types for immutable domain models in Contracts

## Rules

- Always use the SpecKit workflow and commit on each completed step for all new code changes
- Never modify files under `obj/` or `bin/` — these are build artifacts
- Never commit secrets, `.env` files, or hardcoded credentials
- All REST endpoints require authorization — use policies defined in `AuthorizationPolicies.cs`
- The `data/` directory is for runtime persistence — never check it in
- Centralized package versions live in `Directory.Packages.props` — do not pin versions in individual `.csproj` files
- Reference hardware spec IDs (e.g., T020, T024, T040) in doc comments when implementing spec requirements
- Docker images must target ARM64 for CM5 deployment
- Keep `TreatWarningsAsErrors` enabled — fix warnings, do not suppress them

## Testing

- Framework: xUnit
- Run all tests: `dotnet test`
- Add tests for any new service, adapter, or API endpoint
- Hardware adapters should be testable via interface mocking (`IHardwareAdapter`)
- Test projects go in `tests/` — mirror the source project name (e.g., `ZScout.HwTest.App.Tests`)

## Git workflow

- Verify you have full credentials to read, write, push, and create pull requests before starting any new branch or workflow
- Branch from `main`
- Use worktree's when possible
- Use conventional commit messages: `feat:`, `fix:`, `chore:`, `docs:`, `test:`
- Do not commit directly to `main`
- Verify `dotnet build zSCOUT-hw-test.slnx` succeeds before pushing
