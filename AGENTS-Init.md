# Project Agent Guide (Starter Template)

Reusable template for bootstrapping a new project with clear conventions for architecture, quality, and delivery.

## Project summary

Short description of the project, product, or service.

Example:
- Web dashboard and CLI tool for validating system health and reporting diagnostics.

## Commands

Replace these with project-specific commands.

- Build: `<build command>`
- Test: `<test command>`
- Run app: `<run app command>`
- Run worker/cli: `<run worker or cli command>`
- Lint/format: `<lint or format command>`
- Container build: `<docker/podman build command>`
- Smoke test: `<smoke test command>`

## Tech stack

- .NET 10, C# (latest LangVersion), `net10.0` target framework
- Blazor Server (dashboard) + ASP.NET Core Minimal APIs (REST)
- SignalR for real-time hardware status streaming
- xUnit for tests, Serilog for structured logging
- Docker (multi-stage ARM64 build), targets Raspberry Pi CM5

## Project structure

Keep this tree up to date as the project evolves.

```text
src/
  <Project.Contracts>/         Shared models and types
  <Project.App>/               Main application
    Api/                       Transport-facing endpoints/handlers
    Auth/                      Authentication and authorization
    Domain/                    Core business logic
    Infrastructure/            External integrations and adapters
    Persistence/               Repositories, migrations, storage access
    Observability/             Logs, metrics, tracing, diagnostics
  <Project.Cli>/               Optional command-line entrypoint
tests/                         Unit, integration, and end-to-end tests
deploy/                        Dockerfiles, compose, manifests
docs/                          Documentation, runbooks, ADRs
scripts/                       Utility and automation scripts
```

## Architecture rules

- Keep transport layers thin: controllers/endpoints delegate to services.
- Keep business logic in domain/application services, not in UI or transport code.
- Isolate external systems behind interfaces/adapters.
- Handle failures explicitly and return typed error outcomes where possible.
- Enforce single-responsibility per component and clear ownership boundaries.
- Protect concurrent workflows with idempotency and locking where needed.

## Code style

Use defaults below unless the project defines stricter language-specific conventions.

- Enable strict compiler/linter settings and treat warnings as actionable.
- Prefer immutable data structures for domain state where practical.
- Use dependency injection for boundary dependencies.
- Use structured logging with message templates and stable keys.
- Name async methods with `Async` suffix (or language equivalent convention).
- Keep public APIs documented with concise summaries and examples.

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

- Define the primary test framework and test command.
- Add unit tests for all new business logic.
- Add integration tests for adapters, persistence, and API contracts.
- Add regression tests for every bug fix.
- Ensure CI runs tests and fails on test/lint/build errors.

## Git workflow

- Verify you have full credentials to read, write, push, and create pull requests before starting any new branch or workflow
- Branch from `main`
- Use worktree's when possible
- Use conventional commit messages: `feat:`, `fix:`, `chore:`, `docs:`, `test:`
- Do not commit directly to `main`
- Verify `dotnet build zSCOUT-hw-test.slnx` succeeds before pushing

## Done criteria

A change is done when all are true:

- Requirements are implemented and documented.
- Tests are added/updated and passing.
- Build/lint/type checks are passing.
- Monitoring/logging impact is considered.
- PR is reviewed and merged.

## Onboarding checklist

Use this checklist when starting a brand-new project:

- Fill in project summary and scope.
- Replace all placeholder commands.
- Finalize stack versions.
- Confirm folder structure and module boundaries.
- Define auth model and security baseline.
- Configure CI for build/test/lint.
- Add first runbook and local setup docs.
- Confirm release and rollback strategy.
