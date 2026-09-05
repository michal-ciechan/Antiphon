# Runner Capabilities And Model Discovery — Satellite Doc

**Feature:** AI Agent TUI Configuration  
**Date:** 2026-08-12  
**Relates to:** Requirements §4 (FR-2–FR-10, FR-14–FR-19), §5 (NFR-6–NFR-12), §6

## 1. What This Covers

This satellite defines how Antiphon describes terminal-runner capabilities, discovers model identifiers, merges curated suggestions, injects an optional model argument, validates a profile, and configures this installation's OpenCode gateway wrapper. It covers Claude Code, Codex, and OpenCode without pretending their features or model namespaces are interchangeable.

## 2. Why It Is Separate From Requirements

Runner commands and capabilities change independently of the product contract. Keeping this detail separate lets the requirements remain stable while discovery parsers, curated suggestions, and capability probes evolve with installed CLI versions.

## 3. Detail

### 3.1 Profile capability contract

Each runner profile publishes a capability snapshot derived from its runner type, configured launch mode, installed version, and successful probes. A capability has a state of supported, unsupported, degraded, or unknown plus a short reason. The UI never infers support from the profile name.

The initial capability set is:

| Capability | Meaning |
|---|---|
| Model argument | An exact model identifier can be passed for a session. |
| Model discovery | The installed runner can enumerate usable model identifiers. |
| Structured activity | Antiphon has a reliable turn/activity source rather than terminal quiet-time alone. |
| Session resume | Antiphon can resume this runner without silently starting a different conversation. |
| Remote control | The runner supports Antiphon's remote-control launch behaviour. |
| System-prompt append | Antiphon can add its agent/channel contract at launch. |
| Permission bypass | The profile explicitly requests the runner's non-interactive permission mode. |

Capability probes are bounded and side-effect-free. A failed or unrecognized probe produces unknown or degraded state, not optimistic support. Features that depend on a reliable turn boundary must reject or visibly degrade when structured activity is unavailable.

### 3.2 Model catalogue

One profile owns one model catalogue. Every entry contains:

- the exact opaque identifier passed to that runner;
- a display label and optional family/provider label;
- source: discovered, curated, or operator-added;
- availability: verified, unverified, stale, or unavailable;
- discovery timestamp and runner version when known;
- whether it is suggested as a profile or capability-level default.

Discovery replaces only the discovered portion of the catalogue after a successful complete parse. It does not delete curated or operator-added entries. A failed refresh preserves the last successful discovered result and marks it stale. Duplicate exact identifiers collapse to one entry, preferring verified availability while preserving an operator label.

An agent's selected exact identifier remains stable even when a later discovery omits it. The UI warns that it is no longer verified; Antiphon never silently substitutes another model.

### 3.3 Curated suggestions

Curated suggestions guarantee a useful picker even when a CLI has no discovery command or the network is unavailable:

| Runner | Initial suggestions | Notes |
|---|---|---|
| Claude Code | `fable`, `opus`, `sonnet`, `haiku` | Family aliases are preferred over version-pinned identifiers. |
| Codex | `gpt-6-astra`, `gpt-5.6-sol`, `gpt-5.6-terra`, `gpt-5.6-luna` | Suggestions remain unverified until the installed client or configured endpoint confirms them. `gpt-6-astra` requires codex-cli 0.153.4+. |
| OpenCode | `llmgateway/grok-4-5` | Required fallback suggestion when discovery yields no usable model; operator-added provider/model IDs remain valid. |

Curated catalogues are application defaults, not proof that the current account can use a model. The UI labels them as suggestions until verified.

### 3.4 Discovery strategies

Runner types own their discovery strategy:

- **Claude Code:** use a supported model-list capability if a future installed version exposes one; otherwise return the curated family aliases and mark discovery unsupported rather than scraping help text.
- **Codex:** use a supported machine-readable model-list capability when available. If the installed client has none, retain curated suggestions and report discovery unsupported.
- **OpenCode:** invoke the configured profile command with the `models` operation, through the same wrapper and non-secret environment used for launches. Parse one provider/model identifier per output line and reject prompts, banners, malformed identifiers, or secret-bearing diagnostics.

Discovery runs as a separate short-lived process. It receives the profile's authentication mode but no repository prompt, agent task token, or session identity. Output size, execution time, and persisted failure excerpts are bounded.

### 3.5 Optional model argument

The launch contract treats model selection as structured data: argument name and exact value are separate ordered arguments. When an agent has no exact selection, both are omitted and the runner or wrapper chooses its configured default. Empty strings and placeholder values are treated as no selection.

Profiles may define the runner-specific argument name, with curated defaults of `--model` for the initial runners. A command preview shows the argument name and non-secret model identifier without shell concatenation. The model value is never interpolated into an executable or wrapper path.

### 3.6 OpenCode adapter and local gateway profile

OpenCode is a distinct runner type with its own readiness, completion, response, capability, and model-discovery behaviour. It is not reported as Codex or Raw merely because those adapters also use a terminal.

The local profile is named `OpenCode Gateway` and launches PowerShell with:

```text
-NoProfile -ExecutionPolicy Bypass -File C:\Users\mike.ciechan\.local\bin\ocg.ps1 --auto --mini
```

The existing wrapper forwards trailing OpenCode arguments and remains responsible for its API key, proxy, and provider configuration. If an agent selects a model, Antiphon appends `--model` and the exact provider/model identifier. If the selection is empty, no model argument is appended. This makes the model parameter optional without copying gateway credentials into Antiphon.

The generic curated fallback remains a catalogue suggestion, not proof that a particular wrapper can authenticate to that provider. For this installation's `ocg.ps1` wrapper, the explicit Grok 4.5 smoke uses the discovered `maven/grok-4.5` identifier; it is forwarded unchanged and the wrapper does not inject its own second model argument.

The OpenCode capability probe checks the installed version and supported `models`, session, and event/ACP surfaces. The adapter uses a supported structured session/event surface for turn state when available and exposes PTY quiet detection only as degraded fallback. Degraded mode is acceptable for direct interactive use but must not be represented as reliable structured activity for unattended queued delivery or delegation.

### 3.7 Profile validation

A profile test executes these stages independently and reports each as pass, fail, skipped, or degraded:

1. resolve the executable and wrapper path;
2. validate ordered argument construction without shell interpolation;
3. verify the chosen working directory is accessible;
4. check required secret/non-secret environment names without displaying values;
5. query version and capabilities;
6. refresh models when supported;
7. start the runner with a bounded health probe and stop it cleanly;
8. report whether the profile is safe for interactive, queued, delegated, and resumable use.

A failed test never changes the currently active profile revision. Saving an invalid or untested draft is allowed, but enabling it or assigning it as the installation default requires passing all mandatory stages for its declared use.

## 4. Impact On Requirements

| Requirement | Impact |
|---|---|
| FR-2 | Defines model-selection and capability metadata held by a profile. |
| FR-3 | Defines distinct initial runner behaviour. |
| FR-5 | Defines stable exact selection and omitted-model semantics. |
| FR-7 | Defines safe runner-owned discovery strategies. |
| FR-8 | Defines catalogue merge, source, verification, and staleness. |
| FR-9 | Defines the initial curated suggestions. |
| FR-10 | Preserves the last successful catalogue across refresh failure. |
| FR-14 | Defines the staged profile-test outcome. |
| FR-15 | Defines the capability states and degraded-mode contract. |
| FR-18 | Supplies runner-specific setup and troubleshooting facts. |
| FR-19 | Defines the local `ocg.ps1` launch and optional model argument. |
| NFR-7 | Bounds discovery and profile-test processes. |
| NFR-8 | Prevents discovery failure or omission from silently changing agent choice. |
| NFR-11 | Defines non-sensitive capability, discovery, and validation signals. |
| NFR-12 | Defines the labels needed to explain verified, suggested, stale, and degraded states. |

## 5. Open Questions

None for the specification baseline. Exact capability states are determined by the installed runner versions during implementation and validation.
