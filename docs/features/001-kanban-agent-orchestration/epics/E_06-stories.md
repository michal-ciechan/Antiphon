# E06 — Workspace hook runner

> **Status legend:** `[ ]` not started · `[~]` in progress · `[x]` done · `[!]` blocked.
> **Index:** [05-epics.md](../05-epics.md) · **Reqs:** [01-requirements.md](../01-requirements.md)

**Goal:** run shell hooks at workspace lifecycle points with timeouts + abort semantics.

**Covers:** FR-15

---

## Stories

- **E06-S01** `[ ]` `IWorkspaceHookRunner` + impl.
  - Work items:
    - Run `after_create`, `before_run`, `after_run`, `before_remove` with cwd = workspace, timeout per hook. *TDD:* test `HookRunner_runs_script_in_workspace_cwd`.
    - Pre-hook failure aborts attempt; `after_run` failure logged-only. *TDD:* test `HookRunner_pre_hook_nonzero_aborts` and `HookRunner_post_hook_nonzero_does_not_abort`.
    - Timeout kills hook process. *TDD:* test `HookRunner_timeout_kills_hung_hook`.
- **E06-S02** `[ ]` Hook config in `WorkflowDefinition`.
  - Work items:
    - YAML schema accepts `hooks: { after_create, before_run, after_run, before_remove }`. *TDD:* test `WorkflowDefinition_parses_hooks_block`.
