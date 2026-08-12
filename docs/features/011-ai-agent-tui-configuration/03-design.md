# AI Agent TUI Configuration — High-Level Design

## 1. Solution Overview

Antiphon will replace its single effective runner definition with persistent, centrally managed TUI profiles. An agent selects one enabled profile and either an exact model from that profile's catalogue or the profile's default behaviour. A session launches from an immutable profile revision so later edits affect future sessions without changing a running process (FR-1–FR-6, FR-17).

Profiles combine launch settings, authentication mode, setup guidance, model catalogue, validation state, and a truthful capability snapshot. Runner-specific handlers own discovery and launch semantics for Claude Code, Codex, and OpenCode. Managed credentials cross a separate write-only protection boundary; wrapper-managed profiles continue to launch without storing credentials in Antiphon (FR-7–FR-15).

Existing file definitions remain a compatibility and seed source during migration. This installation will seed an editable `OpenCode Gateway` profile that launches `ocg.ps1`; its model argument is added only when the selected agent has an exact OpenCode model (FR-16, FR-19).

## 2. Architecture At A Glance

### 2.1 Diagram

```text
┌──────────────────────── Antiphon browser ────────────────────────┐
│ TUI profile admin       Agent create/settings       Test status │
└───────────────┬──────────────────┬──────────────────────▲────────┘
                │                  │                      │
         ┌──────▼──────────────────▼──────────────────────┴──────┐
         │ Profile, model, secret, validation and agent APIs     │
         └───────────┬───────────────────────┬───────────────────┘
                     │                       │
          ┌──────────▼──────────┐   ┌────────▼──────────────────┐
          │ Profile catalogue   │   │ Protected secret service │
          │ + immutable revision│   │ + external key ring      │
          │ + model cache       │   └────────┬──────────────────┘
          └──────────┬──────────┘            │ launch only
                     │                       │
              ┌──────▼───────────────────────▼───────┐
              │ Launch resolver + capability handler │
              └──────┬──────────────┬───────────────┘
                     │              │
        ┌────────────▼─────┐  ┌─────▼──────────────────────────┐
        │ Discovery/test   │  │ Session runner + protocol     │
        │ short-lived jobs │  │ adapter (Claude/Codex/OpenCode)│
        └──────────────────┘  └────────────────────────────────┘
```

### 2.2 Component Summary

| Component | Responsibility | New / Changed / Reused |
|---|---|---|
| TUI profile administration | Edit launch settings, auth mode, guidance, curated models, enabled/default state, and validation. | New |
| Agent runner/model picker | Select an enabled profile and verified/suggested/explicit model; explain pending-next-session behaviour. | Changed |
| Profile catalogue | Persist profile identity, immutable active revision, provenance, capability snapshot, model cache, and validation summary. | New |
| Protected secret service | Protect, replace, clear, decrypt-at-launch, redact, and audit secret environment values. | New |
| Runner capability handlers | Supply suggestions, discovery, model argument semantics, capability probes, validation stages, and adapter selection. | New |
| Launch resolver | Combine agent choice, active revision, exact model, ordinary environment, and protected environment into one immutable session launch. | Changed |
| Session runner and adapters | Add first-class OpenCode behaviour while retaining Claude Code, Codex, and raw process support. | Changed |
| File-definition importer | Seed profiles and preserve the current default during migration or rollback. | New |

## 3. Key Design Decisions

| Decision | Rationale | ADR |
|---|---|---|
| Persist editable profiles with immutable revisions. | Operators need UI management while sessions need a stable launch snapshot. | [D-2](adr/2026-08-12-feature-inception.md#d-2--persist-profiles-with-immutable-revisions) |
| Store runner profile and optional exact model per agent. | Global defaults and generic tiers cannot express per-agent runner/provider choices. | [D-3](adr/2026-08-12-feature-inception.md#d-3--select-runner-and-exact-model-per-agent) |
| Support wrapper-owned and protected managed authentication. | Existing wrappers remain valuable; managed secrets still need a consistent safe path. | [D-4](adr/2026-08-12-feature-inception.md#d-4--support-two-authentication-modes) |
| Use discovery plus curated suggestions. | Discovery is useful but not universal or reliable enough to be the sole catalogue. | [D-5](adr/2026-08-12-feature-inception.md#d-5--merge-discovery-with-curated-suggestions) |
| Model runner capabilities explicitly and add a dedicated OpenCode adapter. | Similar terminal rendering does not imply equivalent session semantics. | [D-6](adr/2026-08-12-feature-inception.md#d-6--make-runner-capabilities-explicit) |
| Import file definitions once, then make managed profiles authoritative. | Preserves existing installations without making UI edits fight configuration reloads. | [D-7](adr/2026-08-12-feature-inception.md#d-7--import-existing-file-definitions) |

## 4. Phasing Summary

| Phase | Theme | Epics | Requirements Covered |
|---|---|---|---|
| 1 | Safe persisted foundation | E-01 | FR-1–FR-3, FR-11–FR-13, FR-16–FR-17 |
| 2 | Runner knowledge and public contract | E-02 | FR-7–FR-10, FR-14–FR-15, FR-18 |
| 3 | Launch and protocol support | E-03 | FR-3, FR-5–FR-6, FR-19 |
| 4 | Operator and agent UX | E-04, E-05 | FR-1–FR-6, FR-8–FR-15, FR-18 |
| 5 | Local OpenCode proof | E-06 | FR-14–FR-16, FR-19 |
| 6 | Release readiness | E-07–E-09 | NFR-1–NFR-12 |

## 5. Runtime Characteristics

All values are estimates to be replaced with measurements during delivery.

| Path | Data usable / complete when | Measured or estimated | Expected magnitude | Accepted magnitude | Requirement |
|---|---|---|---|---|---|
| Startup | Profiles, active revisions, cached models, and key readiness are loaded; discovery is not on the critical path. | Estimated | 1s | 5s | NFR-6, NFR-9 |
| Recovery | After a server restart, cached configuration is usable and running sessions remain bound to their recorded revision; unavailable keys are reported fail-closed. | Estimated | 5s | 10s | NFR-3, NFR-8, NFR-10 |
| Snapshot / batch | A profile validation or model refresh finishes all supported stages or returns a bounded partial result. | Estimated | 30s | 30s | NFR-7 |
| Incremental update | A saved profile revision, model choice, or secret metadata appears in UI queries and is available to the next session. | Estimated | 1s | 5s | NFR-6, NFR-9 |

## 6. Cross-Cutting Concerns

| Concern | Approach | NFR |
|---|---|---|
| Authentication | Write-only managed secrets or explicit wrapper ownership; no implicit mixing. | NFR-1–NFR-5 |
| Authorization | Existing trusted administration boundary initially; profile and secret actions remain separable for future policy. | NFR-2, NFR-4 |
| Observability | Stage-level metrics and sanitized lifecycle logs; no secret or full child-output labels. | NFR-11 |
| Migration | One-time import with provenance, stable default selection, and nullable transitional agent assignment. | NFR-10 |
| Rollback | Retain file resolution and imported provenance until the new launch path is proven; no ciphertext downgrade. | NFR-1, NFR-10 |
| Capability safety | Unsupported or degraded features are visible and gate unattended workflows that require reliable turn state. | NFR-8, NFR-12 |

## 7. Risks & Mitigations

| Risk | Likelihood | Impact | Mitigation | Owner |
|---|---|---|---|---|
| A CLI changes discovery or TUI output. | High | Medium | Version-aware bounded probes, cached catalogues, curated fallback, and capability degradation. | Antiphon maintainers |
| Protecting keys are lost or misconfigured. | Medium | High | Fail closed, expose readiness, document paired backup, and support credential replacement. | Installation operator |
| UI profile edits interrupt running work. | Low | High | Immutable session revision and next-session semantics. | Antiphon maintainers |
| Model discovery output contains sensitive diagnostics. | Medium | High | Bounded capture, sanitization, no raw persistence by default, and wrapper mode. | Antiphon maintainers |
| OpenCode lacks a reliable structured turn source in an installed version. | Medium | High | Report degraded capability and prevent unattended features from treating PTY quiet as equivalent. | Antiphon maintainers |
| Migration selects the wrong default profile. | Low | High | Deterministic import, dry-run summary, explicit provenance, and rollback to existing file definition. | Installation operator |

## 8. Alternatives Considered

| Alternative | Rejected Because | ADR |
|---|---|---|
| Continue editing only application files. | Does not provide per-agent selection, safe UI secret management, revisioning, or validation. | [D-2](adr/2026-08-12-feature-inception.md#d-2--persist-profiles-with-immutable-revisions) |
| Store only wrapper presets. | Cannot satisfy managed credentials, discovery metadata, or direct executable setup. | [D-4](adr/2026-08-12-feature-inception.md#d-4--support-two-authentication-modes) |
| Treat OpenCode as Raw or Codex. | Would misrepresent readiness, response, transcript, resume, and discovery behaviour. | [D-6](adr/2026-08-12-feature-inception.md#d-6--make-runner-capabilities-explicit) |
| Require discovery before any model can be selected. | Breaks offline use and runners without a model-list operation. | [D-5](adr/2026-08-12-feature-inception.md#d-5--merge-discovery-with-curated-suggestions) |

## 9. Out Of Scope

- Automatic installation or upgrading of runner applications.
- A shared universal model identity across runner ecosystems.
- Replacing provider account or authorization administration.
- Claiming structured transcripts for a runner that cannot provide a reliable source.

## 10. Open Questions

None for the specification baseline.
