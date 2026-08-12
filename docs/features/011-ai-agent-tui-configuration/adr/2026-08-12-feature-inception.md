# ADR — 2026-08-12 — Feature Inception

## Decisions Recorded Today

### D-1 — Create first-class AI agent TUI configuration

**Context.** Antiphon can launch configured terminal agents, but runner choice is global and credentials are outside its managed configuration.

**Decision.** Deliver per-agent runner and model selection backed by centrally managed TUI profiles, safe credential handling, model discovery, and provider capability checks.

**Rationale.** This makes Claude Code, Codex, OpenCode, and future tools selectable without machine-specific source edits while retaining wrapper-based authentication.

**Consequences.** The feature spans persistent configuration, runtime launch resolution, provider adapters, UI administration, migration, testing, and operational controls.

**Decided by.** Mike Ciechan and Codex.

---

### D-2 — Persist profiles with immutable revisions

**Context.** Runner definitions must be editable in the UI, but a running or historical session must retain the launch configuration it actually used.

**Decision.** Store runner profiles as durable identities with immutable revisions. One revision is active for future launches; sessions record the revision they resolved.

**Rationale.** This makes updates atomic, auditable, and safe for in-flight work.

**Consequences.** Profile edits create revisions rather than mutating a launch snapshot. Cleanup must retain revisions referenced by sessions.

**Decided by.** Mike Ciechan and Codex.

---

### D-3 — Select runner and exact model per agent

**Context.** A single installation default and generic capability levels cannot express an agent that uses a different terminal client or provider/model identifier.

**Decision.** Each agent selects one profile and may select one exact model owned by that profile. No exact selection omits the model argument and delegates default choice to the profile or runner.

**Rationale.** This is explicit, preserves runner namespaces, and keeps optional-model wrapper behaviour.

**Consequences.** Agent changes apply on the next session. Existing model levels require a migration mapping rather than remaining the final public model selector.

**Decided by.** Mike Ciechan and Codex.

---

### D-4 — Support two authentication modes

**Context.** Existing wrappers already set credentials and proxy configuration, while direct runner profiles need safe application-managed environment secrets.

**Decision.** Profiles explicitly choose wrapper-managed authentication or Antiphon-managed write-only protected environment values.

**Rationale.** This preserves proven local wrappers without withholding a cross-platform managed secret option.

**Consequences.** Antiphon never extracts wrapper secrets. Managed secrets require an external protected key ring and fail closed when it is unavailable.

**Decided by.** Mike Ciechan and Codex.

---

### D-5 — Merge discovery with curated suggestions

**Context.** Some runners enumerate models, others do not, and discovery can fail because of version, network, authentication, or provider availability.

**Decision.** Merge successful discovery with curated and operator-added entries, retain the last successful discovery, and label every entry's source and verification state.

**Rationale.** The picker remains useful offline without presenting suggestions as verified account access.

**Consequences.** Catalogue entries have provenance and staleness. Missing discovery never silently changes an agent's selected model.

**Decided by.** Mike Ciechan and Codex.

---

### D-6 — Make runner capabilities explicit

**Context.** Claude Code, Codex, and OpenCode differ in discovery, transcript, resume, prompt, permission, and remote-control behaviour even when all render in a terminal.

**Decision.** Add distinct runner handlers and a dedicated OpenCode adapter. Report capabilities as supported, unsupported, degraded, or unknown based on type, version, configuration, and probes.

**Rationale.** Capability truth prevents terminal quiet-time heuristics from masquerading as reliable structured activity.

**Consequences.** Unattended features can gate on required capabilities. New runners implement the same capability contract rather than borrowing an unrelated identity.

**Decided by.** Mike Ciechan and Codex.

---

### D-7 — Import existing file definitions

**Context.** Existing installations and local ignored configuration already define working launch commands and wrappers.

**Decision.** Import existing definitions once with provenance, assign a deterministic default, and then make the managed profile revision authoritative. Retain the old resolver as a bounded migration and rollback path.

**Rationale.** Operators gain UI control without losing a working installation or fighting continuous configuration overwrites.

**Consequences.** Import is idempotent and reports what it changed. Subsequent file edits do not silently overwrite managed profiles.

**Decided by.** Mike Ciechan and Codex.
