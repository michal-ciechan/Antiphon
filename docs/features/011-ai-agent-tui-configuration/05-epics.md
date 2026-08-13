# AI Agent TUI Configuration — Epic Plan

### E-01 — Secure Profile Foundation And Migration

**Status:** [x]

**Outcome.** Persist profile identities and immutable revisions, protect managed environment secrets with externally protected key custody, import current file definitions once with provenance, establish the installation default, and preserve existing agents and rollback behaviour.

**What we test:**

- A database export, API read, log capture, and process argument listing reveal no managed secret plaintext.
- Missing or incorrect protecting keys fail affected launches closed while wrapper-managed profiles remain usable.
- Concurrent profile edits produce one accepted revision and one explicit conflict without partial state.
- Re-running migration is idempotent and preserves the existing effective default and agent model intent.

### E-02 — Runner Catalogue, Discovery, And Validation API

**Status:** [x]

**Outcome.** Deliver runner-type metadata, profile lifecycle operations, model catalogues with provenance and staleness, bounded discovery, capability snapshots, staged sanitized validation, and write-only secret operations through the accepted public contract.

**What we test:**

- Successful discovery merges verified entries without deleting curated or operator-added models.
- Failed, malformed, oversized, or timed-out discovery preserves the last successful catalogue and curated suggestions.
- Secret create, replace, clear, duplicate, and read metadata follow the write-only contract.
- Validation reports distinct stage and suitability outcomes without leaking credentials or unbounded child output.
- Concurrent discovery or validation requests join one active run per profile.

### E-03 — Profile-Aware Launch And OpenCode Runtime

**Status:** [x]

**Outcome.** Resolve every new session from the agent's selected profile revision and optional exact model, add a dedicated OpenCode adapter and capability-aware behaviour, retain Claude Code and Codex semantics, and record the effective revision/model on the session.

**What we test:**

- An omitted model produces no model argument; a selected model is passed as a separate exact argument.
- Editing a profile or agent selection leaves a running process unchanged and affects the next session only.
- Claude Code, Codex, and OpenCode each use their own adapter and report truthful capabilities.
- OpenCode structured activity is used when supported; PTY fallback is marked degraded and cannot masquerade as safe unattended delivery.
- A stopped launch process is cleaned up and sensitive environment data is absent from diagnostics.

### E-04 — AI Agent TUI Configuration Experience

**Status:** [x]

**Outcome.** Add the browser administration area for listing, creating, duplicating, editing, enabling, disabling, testing, and deleting profiles, with command previews, auth-mode controls, secret set/missing state, model provenance, capabilities, guidance, and pending revision feedback.

**What we test:**

- Operators can complete direct-executable and wrapper-based setup without editing application files.
- Secret inputs clear from browser state after submission and never reappear in responses or previews.
- Verified, stale, curated, operator-added, missing-credential, unsupported, and degraded states are visually distinct and accessible.
- Destructive or conflicting profile actions explain assignments and required remediation.
- Refresh and validation remain usable through partial failure and bounded timeout.

### E-05 — Per-Agent Runner And Model Selection

**Status:** [x]

**Outcome.** Add profile and optional exact-model selection to agent creation and settings, expose configured versus live-session selection, retain compatibility for callers that omit new fields, and prevent invalid or capability-incompatible assignments.

**What we test:**

- Different agents can run different profiles and exact model namespaces in the same installation.
- No-model selection delegates default choice to the selected profile or runner.
- Disabled, missing, unvalidated, or capability-incompatible profiles produce actionable assignment errors.
- Existing agents resolve to the imported default without behavioural regression.
- The UI accurately shows a selection change that is waiting for a fresh session.

### E-06 — Local OpenCode Gateway End-To-End Proof

**Status:** [~]

**Outcome.** Create this installation's editable `OpenCode Gateway` profile using `ocg.ps1`, preserve wrapper-managed gateway authentication, prove automatic model discovery and Grok 4.5 fallback, assign Atlas to the profile, and complete a real message round trip with and without an explicit model.

**What we test:**

- The profile launches the wrapper with permission and minimal-TUI arguments while keeping key and proxy data out of Antiphon.
- Discovery returns usable provider/model identifiers, or the labelled Grok 4.5 suggestion remains selectable when discovery is unavailable.
- The process command contains an exact model only when selected.
- Atlas answers an exact-response probe and records the effective OpenCode profile revision/model.
- The normal Antiphon local-stack verification remains green after restart.

### E-07 — DEV Deployment and Smoke Test

**Status:** [ ]

**Outcome.** Deploy the feature to the accepted DEV-equivalent Antiphon environment with its persistent key ring, migrate existing profiles and agents, and run configuration, discovery, validation, launch, restart, and redaction smoke checks.

**What we test:**

- Migration and rollback rehearsal preserve the prior default and existing agent startup.
- Managed-secret and wrapper-managed profiles both launch in the deployed environment.
- Restart proves protected secrets remain decryptable and cached model catalogues remain usable.
- The named DEV-equivalent end-to-end smoke passes with retained sanitized evidence.

### E-08 — Production Deployment and AI/Bot Smoke Test

**Status:** [ ]

**Outcome.** Deploy through the approved production/release path and run an AI-agent smoke against Claude Code, Codex, and OpenCode where licensed and configured; if Antiphon has no separate production target, record that evidence-backed no-action decision and run the smoke against the approved release installation.

**What we test:**

- The selected deployment or documented no-action decision matches the actual Antiphon operating model.
- A bounded AI smoke confirms runner selection, exact/default model behaviour, response delivery, and sanitized diagnostics.
- Key custody, backup, and credential-rotation ownership are confirmed for the release installation.
- No runner or model silently falls back to a different configured choice.

### E-09 — Documentation and Operational Readiness

**Status:** [x]

**Outcome.** Publish operator and user guidance for profile setup, direct and wrapper launches, supported runner capabilities, model discovery, managed secret key custody and recovery, migration/rollback, validation, regression smoke procedures, metrics, dashboard use, and actionable failure response.

**What we test:**

- A new operator can configure each initial runner and understand wrapper-owned versus managed authentication.
- Recovery guidance covers lost keys, stale discovery, invalid executable, failed validation, and rollback without exposing secrets.
- Regression and deployment procedures reproduce the accepted smoke evidence.
- Metrics, logs, dashboards, ownership, and any no-alert decisions match the accepted observability contract.
