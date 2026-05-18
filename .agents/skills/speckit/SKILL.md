---
name: speckit
description: >
  Run the full SpecKit spec-driven development workflow for a GitHub issue.
  Use when asked to take over a GitHub issue, implement a feature end-to-end
  using the SpecKit workflow, or run any individual SpecKit step (specify, plan,
  tasks, implement, analyze, checklist, clarify, constitution). The workflow
  agents and prompts are defined in .github/agents/ and .github/prompts/.
---

# SpecKit — Spec-Driven Development Workflow

## Arguments

- `$0` — GitHub issue number (e.g. `42`) or a natural-language feature description.

## Overview

SpecKit is a spec-driven development workflow defined in this repository under
`.github/agents/speckit.*.agent.md` (agent definitions) and
`.github/prompts/speckit.*.prompt.md` (prompt templates).

Each step produces artifacts in the `.specify/` directory and hands off to the
next step. Git operations (branching, committing) are woven in between steps.

## Workflow Steps

Execute the following steps **in order**. For each step, read the corresponding
agent file from `.github/agents/` and follow its full instructions. Commit after
every step that produces or modifies artifacts.

### Phase 0 — Setup

1. **Read the GitHub issue** — Fetch issue `$0` from the repo. Understand the
   requirements, acceptance criteria, and any linked context.
2. **speckit.git.feature** — Create a feature branch for the issue.
   Agent: `.github/agents/speckit.git.feature.agent.md`
3. **speckit.git.validate** — Validate the branch name follows conventions.
   Agent: `.github/agents/speckit.git.validate.agent.md`

### Phase 1 — Specification

4. **speckit.specify** — Create or update the feature specification from the
   issue description.
   Agent: `.github/agents/speckit.specify.agent.md`
   Prompt: `.github/prompts/speckit.specify.prompt.md`
   → Commit after completion.

5. **speckit.clarify** — Identify underspecified areas and resolve them. If
   running autonomously (no human in the loop), use best judgment to fill gaps
   based on the issue context and existing codebase.
   Agent: `.github/agents/speckit.clarify.agent.md`
   Prompt: `.github/prompts/speckit.clarify.prompt.md`
   → Commit after completion.

### Phase 2 — Planning

6. **speckit.plan** — Generate the implementation plan from the specification.
   Agent: `.github/agents/speckit.plan.agent.md`
   Prompt: `.github/prompts/speckit.plan.prompt.md`
   → Commit after completion.

7. **speckit.checklist** — Generate a requirements-quality checklist for the
   feature.
   Agent: `.github/agents/speckit.checklist.agent.md`
   Prompt: `.github/prompts/speckit.checklist.prompt.md`
   → Commit after completion.

### Phase 3 — Task Generation

8. **speckit.tasks** — Break the plan into actionable, dependency-ordered tasks.
   Agent: `.github/agents/speckit.tasks.agent.md`
   Prompt: `.github/prompts/speckit.tasks.prompt.md`
   → Commit after completion.

9. **speckit.analyze** — Cross-artifact consistency and quality analysis across
   spec.md, plan.md, and tasks.md. Fix any issues found.
   Agent: `.github/agents/speckit.analyze.agent.md`
   Prompt: `.github/prompts/speckit.analyze.prompt.md`
   → Commit after completion.

### Phase 4 — Implementation

10. **speckit.implement** — Execute all tasks from tasks.md. Implement the code
    changes, following the plan and specification. Commit after each meaningful
    unit of work.
    Agent: `.github/agents/speckit.implement.agent.md`
    Prompt: `.github/prompts/speckit.implement.prompt.md`

### Phase 5 — Verification

11. **Build verification** — Run `dotnet build` (or the project's build command)
    and fix any errors until the project compiles cleanly.

12. **Self code review** — Review all changes for:
    - Alignment with the specification and plan
    - Missing edge cases or error handling
    - Code quality and consistency with existing patterns
    - Proper test coverage where applicable
    Fix anything found and commit.

### Phase 6 — Delivery

13. **Create pull request** — Push the branch and create a PR targeting `main`.
    Link the PR to issue `$0`. Include a summary of what was implemented and
    which SpecKit steps were completed.

14. **Notify reviewer** — Post a comment on issue `$0` notifying the assigned
    reviewer that the PR is ready for review.

## Git Commit Convention

Use the auto-commit agent (`.github/agents/speckit.git.commit.agent.md`) and
its configuration in `.specify/extensions/git/git-config.yml` when available.
Otherwise, use conventional commit messages:

- `docs: create feature specification for #<issue>`
- `docs: add implementation plan for #<issue>`
- `docs: generate tasks for #<issue>`
- `feat: implement <feature summary> for #<issue>`
- `fix: resolve build errors for #<issue>`

## Extension Hooks

Each SpecKit step supports pre- and post-execution hooks defined in
`.specify/extensions.yml`. The agent files contain full hook-processing
instructions. Follow them as documented.

## Important Notes

- Always read the full agent file for each step before executing it. The agent
  files contain detailed instructions, validation rules, and hook processing
  that this skill summary does not reproduce.
- The `.specify/` directory holds all SpecKit artifacts (constitution, specs,
  plans, tasks, checklists, templates, memory).
- When running autonomously without human interaction, make reasonable decisions
  for clarification questions rather than blocking.
