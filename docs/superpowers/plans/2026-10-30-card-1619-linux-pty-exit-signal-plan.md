# CARD-1619: Carry Linux PTY exit signals end to end

Date: 2026-10-30. Plan source: `4e67121ef7b85a563c5563cf5fc6aa769cb45743`.
Status: initial skeleton; source audit and executable verification design in progress.
Plan task: `9a468e98`; product code is outside this dispatch.

## Decisions

- **D-1 — Preserve exit evidence, not inferred success.** Carry a nullable signal through the PTY, detached host, runner and stored session; retain a distinct unknown state for old producers. Reject guessing signals from historical zero exit codes.
- **D-2 — Make the change additive across rollout skew.** Old hosts and old runners must still parse each other's messages. Detailed wire, persistence and attribution contracts follow the source audit.
- **D-3 — Keep settlement authority unchanged.** Correct stop evidence and failure wording without replacing transcript-confirmed report settlement, CARD-1610 holds, or CARD-1614 custody proof.
- **D-4 — Fold verification design into this plan.** The requested next stage is Opus Review of the plan, then Code. Code slices are bounded to 150 minutes each.
- **D-5 — Land as a fix.** RC first, separately reviewed master port; no deployment, tag or publish is authorized by this Plan task.

## Ground-truth table

| Card assumption | Code / observed truth | Design consequence |
|---|---|---|
| The Linux exit signal is lost | `src/Antiphon.Agents.Pty/PtySession.cs:41` forwards only `e.ExitCode` | Replace the integer-only exit seam with explicit evidence |
| Exit zero proves a clean self-exit | Investigation found 75/75 logged requested Linux kills reported zero | Unknown old evidence must not claim self-exit |
| This is a missing-report settlement defect | Investigation found no final message; dispatcher already tries report settlement before dead-session failure | Keep the existing settlement ordering |

## Slices

Source audit will name concrete files, tests, estimates and landing dependencies for transport, persistence/text and kill-intent slices.

## Performance budget

Apply `docs/testing-and-build.md#performance-standards` and `docs/test-performance-standards.md`.
This Plan runs no product tests. Its only planned build/tool validation is the requested checkpoint import, through the host build-slot gate. Code measurements are currently unavailable, not measured p95. The completed design will give exact test selections, bounded collection, phase/setup ownership, whole-minute estimates and provisional exceptions tracked by CARD-1619; derive final timeouts near twice measured p95. Any selection or run over 30 minutes requires Debug with retained timing evidence.

## Verification design

Required witnesses: real Linux PTY external SIGKILL/SIGTERM, normal zero/nonzero exit, Antiphon's own stop, both protocol skew directions, durable manifest/session propagation, honest failure text, and native Windows regression. Exact roster and importable checkpoints follow.

## Review and handoff

Read the investigation at `docs/investigations/2026-10-30-codex-session-exit-no-report.md`.
Review the completed plan with Opus before Code. Resolve placement from `GET /api/runner-defaults` and `GET /api/session-runners`; use portable, Linux-native and Windows-native lane names, not a fixed fleet location.
