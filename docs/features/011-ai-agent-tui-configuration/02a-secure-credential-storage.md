# Secure Credential Storage — Satellite Doc

**Feature:** AI Agent TUI Configuration  
**Date:** 2026-08-12  
**Relates to:** Requirements §4 (FR-11–FR-14), §5 (NFR-1–NFR-5), §6

## 1. What This Covers

This satellite defines the threat model, encryption boundary, key custody, write-only API behaviour, runtime injection, rotation, audit, and recovery expectations for credentials managed by Antiphon runner profiles. It also preserves the option to keep authentication entirely inside an existing wrapper script.

## 2. Why It Is Separate From Requirements

The requirements state the security outcomes. This document records the cross-platform cryptographic and operational constraints that security reviewers and deployers need without coupling the product contract to storage mechanics.

## 3. Detail

### 3.1 Threat model

The design protects against:

- a database backup, export, or read-only query disclosing provider credentials;
- API consumers, browser state, logs, metrics, traces, validation output, or process listings disclosing plaintext;
- one feature area accidentally unprotecting another feature area's ciphertext;
- a partial profile update exposing or silently dropping credentials.

The design does not claim to protect secrets after the Antiphon host account or a launched child process is fully compromised. A process that must authenticate inevitably receives the credential in its environment for the duration of that launch.

### 3.2 Protection boundary

Antiphon-managed secret values are protected before persistence and remain encrypted until the final launch environment is assembled. ASP.NET Data Protection supplies authenticated encryption, purpose isolation, key versioning, and rotation. The protection purpose includes the application, feature, profile identity, and environment-variable name so ciphertext cannot be moved to another profile or use without detection.

Each stored value carries only the metadata needed to operate it: profile identity, environment-variable name, ciphertext, protection version, created time, updated time, and the actor or operation that changed it. Plaintext is never retained in a second column, cache, audit event, or validation result.

### 3.3 Protecting-key custody

The Data Protection key ring is persisted outside the repository and outside the profile database. Production-like deployments must protect the key ring with an installation-provided X.509 certificate or an equivalent supported external key protector. Windows installations may use machine- or user-scoped DPAPI when that matches the service identity. Linux and macOS installations use a certificate or secret-mounted key protector with owner-only filesystem permissions.

The application shall expose key-ring readiness without exposing key material. If keys are missing, inaccessible, expired, or unable to decrypt an existing value, affected managed-secret launches fail closed. Wrapper-managed profiles remain usable because Antiphon holds no credential for them.

Key-ring backup is operationally paired with database backup. Losing the protecting keys makes managed ciphertext intentionally unrecoverable; the supported recovery is to replace the affected credentials, not bypass encryption.

### 3.4 Write-only UI and API contract

Create and update requests may submit plaintext over the same protected application channel used for other administration. Read responses return only the environment-variable name, whether a value is configured, timestamps, and validation state. They never return ciphertext or plaintext.

An omitted secret field means "leave unchanged." An explicit replace operation stores a new protected value atomically. An explicit clear operation deletes it. Placeholder strings such as bullets or asterisks are never interpreted as credentials.

### 3.5 Runtime injection and redaction

Managed secrets enter only the child-process environment. They are not rendered into ordered arguments, command previews, shell command strings, setup examples, or process titles. Launch diagnostics use environment-variable names plus set/missing state. Values known to the secret store are registered with the logging redaction boundary before any launch or validation work begins.

Profile tests may prove that a required variable exists and that the runner authenticates, but failure output is bounded and sanitized before persistence or display. Raw child output is treated as potentially sensitive when authentication fails.

### 3.6 Authentication modes

Each profile selects one of two explicit modes:

- **Wrapper-managed:** Antiphon launches the configured command and arguments but stores no credential. The wrapper may supply API keys, proxy URLs, certificate paths, or provider configuration.
- **Antiphon-managed environment:** Antiphon stores selected environment values under this contract and injects them into the child. Non-secret environment values remain ordinary profile configuration.

Changing modes never attempts to extract credentials from a wrapper or copy plaintext out of an existing process. A profile may be duplicated without duplicating managed secret values unless the operator explicitly supplies replacements.

### 3.7 Rotation and audit

Data Protection key rotation follows its key-ring lifecycle; existing ciphertext remains readable while retained keys are valid. Provider credential rotation is a profile secret replacement and affects only future launches. Running processes retain the environment with which they started.

Audit events record profile identity, environment-variable name, operation, result, time, and correlation identity. They never record old values, new values, ciphertext, full launch arguments, or child environment blocks.

## 4. Impact On Requirements

| Requirement | Impact |
|---|---|
| FR-11 | Wrapper-managed mode deliberately creates no Antiphon secret record. |
| FR-12 | Defines write-only create, replace, clear, and read semantics. |
| FR-13 | Separates secret metadata from ordinary environment configuration. |
| FR-14 | Bounds validation output and treats raw authentication failure output as sensitive. |
| NFR-1 | Uses authenticated encryption with protecting keys held outside persisted profile data. |
| NFR-2 | Prevents plaintext disclosure through responses, diagnostics, arguments, and telemetry. |
| NFR-3 | Defines fail-closed behaviour and replacement-based recovery. |
| NFR-4 | Defines non-sensitive secret audit events. |
| NFR-5 | Defines Windows, Linux, and macOS key-protection options. |

## 5. Open Questions

None for the specification baseline. Each deployment must document which supported key protector it uses.
