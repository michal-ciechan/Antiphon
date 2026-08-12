# ADR — 2026-08-12 — Feature Inception

## Decisions Recorded Today

### D-1 — Create first-class AI agent TUI configuration

**Context.** Antiphon can launch configured terminal agents, but runner choice is global and credentials are outside its managed configuration.

**Decision.** Deliver per-agent runner and model selection backed by centrally managed TUI profiles, safe credential handling, model discovery, and provider capability checks.

**Rationale.** This makes Claude Code, Codex, OpenCode, and future tools selectable without machine-specific source edits while retaining wrapper-based authentication.

**Consequences.** The feature spans persistent configuration, runtime launch resolution, provider adapters, UI administration, migration, testing, and operational controls.

**Decided by.** Mike Ciechan and Codex.

