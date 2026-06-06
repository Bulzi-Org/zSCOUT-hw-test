<!-- SPECKIT START -->
For additional context about technologies to be used, project structure,
shell commands, and other important information, read:
specs/004-gps-live-stream-fix/plan.md
<!-- SPECKIT END -->

## Agent execution mode

Always use interactive VS Code agent chat tabs for sub-agents.
Do not use background/headless task agents.
If interactive tabs are unavailable, stop and report that instead of falling back.
