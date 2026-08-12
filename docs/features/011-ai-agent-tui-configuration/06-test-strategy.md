# AI Agent TUI Configuration — Test Strategy

## 1. Feature Test Catalogue

| Area | Must prove |
|---|---|
| Secret confidentiality | Database, API, browser state, logs, metrics, traces, validation output, and process arguments contain no managed plaintext. |
| Key custody and recovery | Supported protectors decrypt after restart; incorrect keys fail closed without breaking wrapper profiles. |
| Revision isolation | Concurrent edits are atomic; running sessions retain their revision and new sessions receive updates. |
| Model catalogue resilience | Entries merge deterministically; failure, timeout, malformed output, and omission preserve prior choices. |
| Capability truth | Capability states gate dependent unattended behaviour exactly as displayed. |
| Runner conformance | Each runner owns its launch, model, readiness, completion, cleanup, and capability semantics. |
| Migration and rollback | Existing definitions, agents, model intent, default, and wrappers survive idempotent import and rollback. |
| API concurrency | Revision conflicts, joined refresh/validation, secret idempotency, and in-use deletion return stable outcomes. |
| UI safety and accessibility | Secret inputs clear after write; statuses and errors are accessible without colour alone. |
| Cross-platform behaviour | Supported systems preserve path, environment, key, executable, wrapper, and child-process semantics. |

## 2. Execution Policy

- Deterministic fake runner executables emit version, models, capability, prompt, malformed, timeout, oversized, and secret-shaped output; live provider calls are forbidden outside the deployment gate.
- The confidentiality oracle scans every persisted and observable boundary for submitted canary values, including failure paths and test artifacts.
- Runner probes use isolated working directories, bounded child processes, deterministic clocks where persisted freshness matters, and guaranteed process cleanup.
- Platform-specific key protectors and path semantics run on their matching build agents; unsupported combinations report skipped capability rather than false success.
- Real CLI conformance tests are serialized by runner installation and use disposable sessions and non-sensitive prompts.

## 3. Quality Gates

- Merge is blocked by any contract, migration, secret-redaction, profile-revision, discovery-fallback, adapter-conformance, or browser workflow regression.
- All new behaviours require observed failing tests before implementation and fresh passing focused plus affected-suite evidence before review.
- **AI Agent TUI Profile Smoke** is the sole deployment gate and proves migration, key readiness, profile CRUD, discovery/fallback, validation, per-agent selection, restart persistence, OpenCode gateway launch, exact/default model behaviour, response delivery, and redacted evidence.

## 4. Environments

| Environment | Purpose | External Access |
|---|---|---|
| Developer / PR | Fast domain, API, persistence, crypto-boundary, adapter, and UI feedback using isolated fakes. | No live provider credentials or model calls. |
| CI platform matrix | Migration, concurrency, process cleanup, and supported key/path semantics across operating systems. | Package restore only; runner responses remain deterministic. |
| AI Agent TUI Profile Smoke | End-to-end proof against the approved Antiphon installation and configured local runners. | Bounded real runner discovery and exact-response prompts through approved credentials. |

Only the deployment gate may use live credentials or provider calls. Its evidence is sanitized before retention.

## 5. Test Data Strategy

- Generate profiles for direct executables, PowerShell/shell wrappers, ordinary environment, managed secrets, missing secrets, disabled defaults, and each capability state.
- Use model identifiers containing provider separators, punctuation, whitespace errors, duplicates, stale entries, unknown selections, and malicious shell-like text to prove opaque argument handling.
- Use unique canary credentials per test and assert their absence from every retained artifact before cleanup.
- Seed legacy definitions and agents at each migration boundary, including null selection, generic model tiers, referenced profiles, and interrupted import.
- Retain sanitized stage results, catalogue provenance, effective session revision/model, process exit evidence, and screenshots needed to diagnose failures.
