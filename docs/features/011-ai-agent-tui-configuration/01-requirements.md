# AI Agent TUI Configuration — Requirements

**Related:** [secure credential storage](02a-secure-credential-storage.md) · [runner capabilities and model discovery](02b-runner-capabilities-and-model-discovery.md)

## 1. Executive Summary

Antiphon shall let an operator configure the terminal-based AI agents available to the installation, select a runner and model independently for each Antiphon agent, and safely choose between wrapper-managed and Antiphon-managed authentication. Claude Code, Codex, and OpenCode are the initial supported runners. Model discovery, curated suggestions, configuration validation, and embedded setup guidance make the feature usable without editing server files by hand.

## 2. Problem Statement

### 2.1 Current State

The installation has one default terminal runner for all persistent agents. Its executable, arguments, environment, and authentication are machine configuration rather than managed application data. An agent can select a generic capability tier, but cannot select its runner or an exact model. Operators cannot inspect, test, or safely update this configuration in the UI.

### 2.2 Who Is Affected

| Stakeholder | How They Are Affected |
|---|---|
| Antiphon operator | Must edit machine configuration and restart services to change runners. |
| Agent owner | Cannot choose the most appropriate runner and exact model for an individual agent. |
| Platform maintainer | Cannot distinguish unsupported runner features, failed discovery, invalid launch settings, or missing credentials before a session starts. |
| Security reviewer | Cannot verify a consistent application-level secret storage and redaction contract. |

### 2.3 Measurable Consequences

- A runner change affects every persistent agent that uses the installation default.
- Exact provider/model identifiers cannot be selected per agent.
- Launch and authentication failures are discovered only when an agent starts.
- Machine-local wrapper knowledge is not visible or explainable in Antiphon.

## 3. Goals

### 3.1 Business Goals

- Make terminal runner and model choice an explicit property of each Antiphon agent.
- Make runner setup discoverable, testable, and editable through Antiphon.
- Support secure cross-platform credential persistence without removing wrapper-managed authentication.
- Add OpenCode as a first-class runner while preserving Claude Code and Codex.

### 3.2 Non-Goals

- Replace provider accounts, billing, quotas, or upstream authentication policy.
- Guarantee that every runner exposes identical session, transcript, resume, or remote-control capabilities.
- Install or upgrade third-party terminal applications automatically.
- Store repository-scoped agent instructions, prompts, or workflow definitions in runner profiles.

## 4. Functional Requirements

| ID | Requirement | Priority |
|---|---|---|
| FR-1 | The system shall provide an AI Agent TUI Configuration area that lists, creates, edits, duplicates, enables, disables, tests, and deletes runner profiles. | Must |
| FR-2 | A runner profile shall identify its runner type, display name, launch command, ordered arguments, environment-variable configuration, authentication mode, model-selection behaviour, and operator guidance. | Must |
| FR-3 | The initial runner types shall be Claude Code, Codex, and OpenCode, without preventing additional runner types later. | Must |
| FR-4 | Agent creation and agent settings shall allow selection of one enabled runner profile. | Must |
| FR-5 | Agent creation and agent settings shall allow selection of an exact model offered by the selected profile, or no model so the runner chooses its own default. | Must |
| FR-6 | Changing an agent's runner or model shall apply to its next session and shall not mutate an already-running process. | Must |
| FR-7 | The system shall discover available models automatically when the selected runner supports discovery. | Must |
| FR-8 | Discovered models shall be combined with curated suggestions, clearly identifying the source and whether availability has been verified. | Must |
| FR-9 | Claude Code suggestions shall include Fable, Opus, Sonnet, and Haiku; OpenCode shall include Grok 4.5 when discovery produces no usable result; Codex shall include current curated family suggestions. | Must |
| FR-10 | Operators shall be able to refresh model discovery and retain the last successful result when a refresh fails. | Must |
| FR-11 | A profile shall support wrapper-managed authentication with no credential stored by Antiphon. | Must |
| FR-12 | A profile shall also support Antiphon-managed secret environment values that are write-only after submission. | Must |
| FR-13 | Operators shall be able to add non-secret environment values and identify which configured values are secret without revealing secret contents. | Must |
| FR-14 | Testing a profile shall validate executable resolution, argument construction, working-directory access, authentication readiness, model discovery where available, and basic runner startup without creating a persistent agent. | Must |
| FR-15 | The UI shall display runner capabilities and limitations, including model discovery, structured activity, resume, remote control, and system-prompt support. | Must |
| FR-16 | Existing installations shall retain a usable default runner and existing agents shall receive an explicit profile assignment without losing their current model-level intent. | Must |
| FR-17 | The installation shall support one default profile for callers that do not yet supply an agent-specific selection. | Must |
| FR-18 | Setup guidance shall explain direct executable launches, wrapper-script launches, required arguments, supported authentication modes, model discovery, and safe troubleshooting for each initial runner type. | Should |
| FR-19 | This machine shall be able to define an OpenCode profile using its existing gateway wrapper and omit the model argument when an agent has no exact model selection. | Must |

## 5. Non-Functional Requirements

| ID | Category | Requirement |
|---|---|---|
| NFR-1 | Security | Managed secrets shall be encrypted at rest using a cross-platform protection mechanism whose protecting keys are stored separately from the encrypted records. |
| NFR-2 | Security | Secret plaintext shall never be returned after submission or included in logs, metrics, browser state, process arguments, validation output, or problem details. |
| NFR-3 | Security | If protecting keys are unavailable or a secret cannot be decrypted, launches that require that secret shall fail closed with an actionable, non-sensitive error. |
| NFR-4 | Security | Secret updates and deletions shall be auditable without recording secret values. |
| NFR-5 | Portability | Profile administration, secret handling, model selection, and launch resolution shall behave consistently on supported Windows, Linux, and macOS installations. |
| NFR-6 | Performance | Cached profiles and models shall make the configuration and agent forms usable within 1 second under normal local conditions. |
| NFR-7 | Performance | A profile test or model refresh shall complete within 30 seconds or return a bounded timeout result. |
| NFR-8 | Reliability | A model-discovery outage shall not remove curated suggestions, erase the last successful catalogue, or prevent launching a previously valid explicit model. |
| NFR-9 | Reliability | Profile and secret updates shall be atomic from an operator's perspective; partial changes shall not become launchable. |
| NFR-10 | Compatibility | Existing file-configured runners and wrapper scripts shall continue to work during migration and rollback. |
| NFR-11 | Observability | Profile validation, discovery, launch selection, secret-protection failures, and fallback use shall be observable without exposing credentials or full sensitive argument values. |
| NFR-12 | Usability | The UI shall distinguish verified models, curated suggestions, wrapper-owned credentials, missing credentials, unsupported capabilities, and pending-restart changes in plain language. |

## 6. Constraints & Assumptions

- Third-party terminal tools differ in model naming, discovery commands, authentication variables, and session capabilities.
- An exact model identifier is opaque runner-owned data; Antiphon shall not infer provider compatibility from its text alone.
- The installation operator is trusted to configure executable paths and non-secret arguments.
- A runner profile may rely entirely on a wrapper script for credentials and proxy settings.
- Protecting-key backup and deployment are installation responsibilities and must be documented.

## 7. Dependencies

- [02a-secure-credential-storage.md](02a-secure-credential-storage.md) — cross-platform encryption, key custody, redaction, rotation, and recovery constraints for managed secrets.
- [02b-runner-capabilities-and-model-discovery.md](02b-runner-capabilities-and-model-discovery.md) — runner-specific discovery, curated suggestions, capability reporting, and wrapper behaviour.
- Availability and compatibility of the installed Claude Code, Codex, and OpenCode applications.

## 8. Out of Scope

- Cloud-hosted multi-tenant secret administration.
- Automatic purchase, provisioning, or revocation of provider credentials.
- A universal model alias shared across unrelated runner ecosystems.
- Silent fallback from a selected exact model to a different model.
- Full transcript normalization for every future terminal runner as part of profile CRUD.

## 9. Open Questions

There are no unresolved product questions for the specification baseline.

<!--
req:FR-1 | Manage AI agent TUI runner profiles in the UI
req:FR-4 | Select one enabled runner profile per agent
req:FR-5 | Select an exact model or use the runner default
req:FR-7 | Discover models automatically where supported
req:FR-11 | Support wrapper-managed authentication
req:FR-12 | Support write-only Antiphon-managed secrets
req:FR-14 | Test a profile before persistent use
req:FR-15 | Display runner capabilities and limitations
req:FR-16 | Preserve existing installations and agent intent
req:FR-19 | Launch local OpenCode through the existing gateway wrapper
req:NFR-1 | Encrypt managed secrets with separately stored protecting keys
req:NFR-2 | Never expose secret plaintext after submission
req:NFR-3 | Fail closed when secret protection is unavailable
req:NFR-5 | Behave consistently across supported operating systems
req:NFR-7 | Bound profile testing and discovery to 30 seconds
req:NFR-8 | Preserve usable catalogues through discovery outages
req:NFR-10 | Preserve existing wrappers and file configuration during migration
-->
