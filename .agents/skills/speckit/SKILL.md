---
name: speckit
description: >
  Run the full SpecKit spec-driven development workflow for a GitHub issue.
  Use when asked to take over a GitHub issue, implement a feature end-to-end
  using the SpecKit workflow, or run any individual SpecKit step (specify, plan,
  tasks, implement, analyze, checklist, clarify, constitution). The workflow
  agents and prompts are defined in .github/agents/ and .github/prompts/.
canonical_source: "https://github.com/Bulzi-Org/zSCOUT/blob/main/.agents/skills/speckit/SKILL.md"
---

> **Canonical source**: This skill is maintained in the parent
> [Bulzi-Org/zSCOUT](https://github.com/Bulzi-Org/zSCOUT/blob/main/.agents/skills/speckit/SKILL.md)
> repository. Sub-repo copies must be kept in sync with that version.

# SpecKit — Spec-Driven Development Workflow

## Arguments

- `$0` — GitHub issue number (e.g. `42`) or a natural-language feature description.

## Overview

SpecKit is a spec-driven development workflow defined in this repository under
`.github/agents/speckit.*.agent.md` (agent definitions) and
`.github/prompts/speckit.*.prompt.md` (prompt templates).

Each step produces artifacts in the `.specify/` directory and hands off to the
next step. Git operations (branching, committing) are woven in between steps.

## GitHub Project Board Tracking

This workflow integrates with the **zSCOUT Project** board (Bulzi-Org, project
number 1). At key phase transitions, update the issue's status on the kanban
board using the `gh` CLI and GraphQL API.

### Project Board Reference

- **Project number**: 1
- **Owner**: Bulzi-Org
- **Status field ID**: PVTSSF_lADOBuYw384BX6mazhTEHqU
- **Kanban stages**:
  - Backlog (`f75ad846`)
  - Ready (`61e4505c`)
  - In progress (`47fc9ee4`)
  - In review (`df73e18b`)
  - Done (`98236657`)

### How to Update the Project Board

All project board GraphQL calls require the `GH_PROJECT_TOKEN` Oz secret (a GitHub
Classic PAT with `project`, `read:org`, and `repo` scopes). Prefix every
`gh api graphql` project call with `GH_TOKEN=$GH_PROJECT_TOKEN` so the PAT is used
instead of the default agent token.

To move an issue to a new status, run these commands:

1. Add the issue to the project and capture its item ID (idempotent — safe to run
   even if the issue is already in the project):
   ```
   ISSUE_NODE=$(gh api repos/Bulzi-Org/<REPO>/issues/<ISSUE_NUMBER> --jq '.node_id')
   ITEM_ID=$(GH_TOKEN=$GH_PROJECT_TOKEN gh api graphql -f query='
     mutation($project: ID!, $content: ID!) {
       addProjectV2ItemById(input: { projectId: $project contentId: $content }) {
         item { id }
       }
     }' \
     -f project="PVT_kwDOBuYw384BX6ma" \
     -f content="$ISSUE_NODE" \
     --jq '.data.addProjectV2ItemById.item.id')
   ```

2. Update the status:
   ```
   GH_TOKEN=$GH_PROJECT_TOKEN gh api graphql -f query='
     mutation($project: ID!, $item: ID!, $field: ID!, $value: String!) {
       updateProjectV2ItemFieldValue(input: {
         projectId: $project
         itemId: $item
         fieldId: $field
         value: { singleSelectOptionId: $value }
       }) { projectV2Item { id } }
     }' \
     -f project="PVT_kwDOBuYw384BX6ma" \
     -f item="$ITEM_ID" \
     -f field="PVTSSF_lADOBuYw384BX6mazhTEHqU" \
     -f value="<STATUS_OPTION_ID>"
   ```

3. Verify the update (required — the GitHub Projects API can silently drop
   mutations). Query the current status and retry up to 2 times if it did not
   take effect:
   ```
   EXPECTED="<STATUS_OPTION_ID>"
   for ATTEMPT in 1 2 3; do
     CURRENT=$(GH_TOKEN=$GH_PROJECT_TOKEN gh api graphql -f query='
       query($id: ID!) {
         node(id: $id) {
           ... on ProjectV2Item {
             fieldValueByName(name: "Status") {
               ... on ProjectV2ItemFieldSingleSelectValue { optionId }
             }
           }
         }
       }' -f id="$ITEM_ID" --jq '.data.node.fieldValueByName.optionId')
     if [ "$CURRENT" = "$EXPECTED" ]; then
       echo "[project-board] Status verified on attempt $ATTEMPT"
       break
     fi
     echo "[project-board] Status mismatch (got=$CURRENT, want=$EXPECTED), retrying ($ATTEMPT/3)..."
     sleep 2
     GH_TOKEN=$GH_PROJECT_TOKEN gh api graphql -f query='
       mutation($project: ID!, $item: ID!, $field: ID!, $value: String!) {
         updateProjectV2ItemFieldValue(input: {
           projectId: $project
           itemId: $item
           fieldId: $field
           value: { singleSelectOptionId: $value }
         }) { projectV2Item { id } }
       }' \
       -f project="PVT_kwDOBuYw384BX6ma" \
       -f item="$ITEM_ID" \
       -f field="PVTSSF_lADOBuYw384BX6mazhTEHqU" \
       -f value="$EXPECTED"
   done
   ```

Replace `<REPO>` with the repository name (e.g. `zSCOUT-image-CM5`),
`<ISSUE_NUMBER>` with the issue number, and `<STATUS_OPTION_ID>` with the
appropriate option ID from the kanban stages above.

## Workflow Steps

Execute the following steps **in order**. For each step, read the corresponding
agent file from `.github/agents/` and follow its full instructions. Commit after
every step that produces or modifies artifacts.

### Phase 0 — Setup

1. **Self-assign the issue** — Assign yourself to the GitHub issue using:
   ```
   gh issue edit $0 --add-assignee @me
   ```
   Run this from within the checked-out repository directory so `gh` auto-detects
   the repo. If `@me` is not available, use the bot/agent username.

2. **Move to "In progress"** — Update the project board status to **In progress**
   (option ID: `47fc9ee4`) using the commands in the "How to Update the Project
   Board" section above.

3. **Read the GitHub issue** — Fetch issue `$0` from the repo. Understand the
   requirements, acceptance criteria, and any linked context.

4. **speckit.git.feature** — Create a feature branch for the issue.
   Agent: `.github/agents/speckit.git.feature.agent.md`

5. **speckit.git.validate** — Validate the branch name follows conventions.
   Agent: `.github/agents/speckit.git.validate.agent.md`

### Phase 1 — Specification

6. **speckit.specify** — Create or update the feature specification from the
   issue description.
   Agent: `.github/agents/speckit.specify.agent.md`
   Prompt: `.github/prompts/speckit.specify.prompt.md`
   → Commit after completion.

7. **speckit.clarify** — Identify underspecified areas and resolve them. If
   running autonomously (no human in the loop), use best judgment to fill gaps
   based on the issue context and existing codebase.
   Agent: `.github/agents/speckit.clarify.agent.md`
   Prompt: `.github/prompts/speckit.clarify.prompt.md`
   → Commit after completion.

### Phase 2 — Planning

8. **speckit.plan** — Generate the implementation plan from the specification.
   Agent: `.github/agents/speckit.plan.agent.md`
   Prompt: `.github/prompts/speckit.plan.prompt.md`
   → Commit after completion.

9. **speckit.checklist** — Generate a requirements-quality checklist for the
   feature.
   Agent: `.github/agents/speckit.checklist.agent.md`
   Prompt: `.github/prompts/speckit.checklist.prompt.md`
   → Commit after completion.

### Phase 3 — Task Generation

10. **speckit.tasks** — Break the plan into actionable, dependency-ordered tasks.
    Agent: `.github/agents/speckit.tasks.agent.md`
    Prompt: `.github/prompts/speckit.tasks.prompt.md`
    → Commit after completion.

11. **speckit.analyze** — Cross-artifact consistency and quality analysis across
    spec.md, plan.md, and tasks.md. Fix any issues found.
    Agent: `.github/agents/speckit.analyze.agent.md`
    Prompt: `.github/prompts/speckit.analyze.prompt.md`
    → Commit after completion.

### Phase 4 — Implementation

12. **speckit.implement** — Execute all tasks from tasks.md. Implement the code
    changes, following the plan and specification. Commit after each meaningful
    unit of work.
    Agent: `.github/agents/speckit.implement.agent.md`
    Prompt: `.github/prompts/speckit.implement.prompt.md`

### Phase 5 — Verification

13. **Build verification** — Identify and run the project's build command. Check
    `README.md`, `build.sh`, `Makefile`, `package.json`, or `*.csproj` / `*.sln`
    files to determine the right command. Fix any errors until the project builds
    cleanly.

14. **Self code review** — Review all changes for:
    - Alignment with the specification and plan
    - Missing edge cases or error handling
    - Code quality and consistency with existing patterns
    - Proper test coverage where applicable
    Fix anything found and commit.

15. **Move to "In review"** — Update the project board status to **In review**
    (option ID: `df73e18b`).

### Phase 6 — Delivery

16. **Create pull request** — Push the branch and create a PR targeting `main`.
    Link the PR to issue `$0`. Include a summary of what was implemented and
    which SpecKit steps were completed.

17. **Notify reviewer** — Post a comment on issue `$0` notifying the assigned
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
- The project board status should only be moved forward, never backward. If a
  phase fails and needs rework, keep the current status.
