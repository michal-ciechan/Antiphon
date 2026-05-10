# E09 — WorkflowDefinition loader + hot reload + Monaco editor

> **Status legend:** `[ ]` not started · `[~]` in progress · `[x]` done · `[!]` blocked.
> **Index:** [05-epics.md](../05-epics.md) · **Reqs:** [01-requirements.md](../01-requirements.md)

**Goal:** edit per-board `WORKFLOW.md`; live reload; running attempts pinned to launch-time version.

**Covers:** FR-14

---

## Stories

- **E09-S01** `[ ]` YAML+Markdown parser.
  - Work items:
    - `WorkflowDefinitionLoader` parses front matter (YAML) + body (Markdown). *TDD:* test `Loader_parses_front_matter_and_body_separately`.
    - Strict template renderer (unknown var → render fail). *TDD:* test `PromptRenderer_unknown_variable_throws`.
- **E09-S02** `[ ]` Hot reload via `IFileSystemWatcher`.
  - Work items:
    - On change → re-parse → publish; bad parse keeps last-good + emits `WorkflowReloaded { ok: false, error }`. *TDD:* test `Loader_invalid_reload_keeps_last_good`.
    - Pin version to attempt at launch. *TDD:* test `RunAttempt_uses_definition_version_at_launch_not_current`.
- **E09-S03** `[ ]` Monaco editor in `WorkflowEditor.tsx`.
  - Work items:
    - YAML+Markdown highlighting; PUT `/api/boards/{id}/workflow`. *TDD:* RTL test `WorkflowEditor_save_button_puts_content`.
