# ADR — 2026-08-16 — Local Release Scope

## Decision

Feature 011 has no separate DEV, production, GitOps, CI, or release-deployment target in this checkout. The Mikeys.Tools-hosted simple stack is the accepted DEV-equivalent and release installation for the feature.

## Evidence

The repository contains the local stack scripts and operator guide, but no deployment manifest or pipeline for this application. The accepted local smoke verifies the backend, frontend, session runner, browser, OpenCode discovery and validation, default-model omission, and exact-model response delivery. It retains only sanitized evidence.

## Consequences

E-07 and E-08 are complete as evidence-backed no-action deployment decisions. A future deployed target must document its owner, key-ring custody, runner credentials, and release smoke before it is treated as a production environment.

**Decided by.** Mike Ciechan and Codex.
