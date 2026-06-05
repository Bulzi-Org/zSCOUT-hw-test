# zSCOUT-hw-test — Agent Instructions

## Project Board Workflow
All work in this repository MUST follow the zSCOUT project board workflow.
Board: https://github.com/orgs/Bulzi-Org/projects/1

### When starting work on an issue
1. Assign the issue to yourself (or the requesting user):
   ```
   gh issue edit <number> --repo Bulzi-Org/zSCOUT-hw-test --add-assignee <username>
   ```
   This triggers the `sync-project-board.yml` workflow to move the issue to **In Progress**.

### When creating a pull request
1. Always include `Fixes #<issue-number>` or `Closes #<issue-number>` in the PR body.
   This triggers the workflow to move the linked issue to **In Review**.
2. If the issue is in a different repo, use the full reference: `Fixes Bulzi-Org/<repo>#<number>`.

### When merging a pull request
1. After merge, the workflow automatically moves linked issues to **Done**.
2. Verify the issue moved to Done on the project board.

### When creating a new issue
1. Add it to the project board immediately:
   ```
   gh project item-add 1 --owner Bulzi-Org --url <issue-url>
   ```

## Tech Stack
- C# / .NET 10 Minimal API with Blazor Server (InteractiveServer)
- xUnit for tests (`dotnet test`)
- File-backed JSON repositories in `data/` directory
- Docker multi-stage build targeting linux/arm64 (Raspberry Pi CM5)
- GHCR publishing via `docker-publish.yml` on push to main

## Build & Validation
- Build: `dotnet build zSCOUT-hw-test.slnx`
- Test: `dotnet test zSCOUT-hw-test.slnx`
- Always run both before committing

## Conventions
- Do not add co-author lines to commit messages
- PR branches: `fix/<issue>-<short-desc>` or `feat/<issue>-<short-desc>`
- **Worktrees** must be created in `~/GitHub/Bulzi-Org/zSCOUT/worktrees/` — NEVER in the repo root or submodule directories. Naming: `<repo-short>-<type><issue>` (e.g. `hw-test-fix65`)


## Parent Project Rules

This repository is a submodule of the zSCOUT parent project at `~/GitHub/Bulzi-Org/zSCOUT/`.
Before starting any work, read and follow the project-wide rules in the parent repository:

**File:** `../AGENTS.md` (or `~/GitHub/Bulzi-Org/zSCOUT/AGENTS.md`)

The parent AGENTS.md contains critical instructions for:
- Kanban board updates (project board status transitions)
- WSL working directory requirements
- Cross-repo orchestration conventions

These parent rules apply to ALL zSCOUT submodule repositories and must be followed in addition to this file's repo-specific rules.
