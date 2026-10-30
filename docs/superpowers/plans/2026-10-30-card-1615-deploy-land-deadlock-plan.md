# CARD-1615: remove the Deploy/land circular wait

Status: skeleton; source investigation and design are in progress. Plan only; no product changes.
Base: `ade54222543381e2e1b31ad79d8ae33a4035f70b` (rc source), assigned branch `feat/card-task-7734ac39`.

## Problem and evidence

CARD-1615 records rollout `c727fa0180cd0d55` at rc `8edda999`: Shared Deploy
`ec24c45b` held land request `6f070ca6`, owner `a798027e-698e-4291-9874-dc7f34a01eac`,
with `repository_or_source_writer`. The bound-runner census then exhausted 60 polls
and refused `RecycleLandInFlight`; canceling Deploy released the cycle. The card is
incident evidence, not a locally reproduced failure.

Initial production anchors: `server/Application/Services/AgentTaskLandService.cs:593,742`;
`scripts/deploy-server2.ps1:800,823,888`; `scripts/c590-remote.sh:6408,6538,6586`.
The final design must protect checkout movement and AppHost build coherence while
preserving continuous lands and fail-closed recycle admission.

## Ground truth

| Card assumption | Source finding | Design consequence |
|---|---|---|
| Shared Deploy excludes lands. | Both land admission paths name `repository_or_source_writer`; predicate investigation pending. | Do not remove protection without identifying actual shared Git writes. |
| Recycle waits on the land Deploy holds. | Both wrapper and host census refuse bound pending lands. | A census-only bypass may leave the land blocked for the full rollout. |
| Product hosts run the rc line. | `docs/docker-stack.md` requires current rc SHA; master publication is separate. | Qualify any rc adoption and master port independently. |

## Decisions

- D-1: Preserve repository mutation safety and runner recycle binding checks. No blanket Deploy-role exemption.
- D-2: Evaluate target-aware exclusion, typed rollout ownership, self-held census handling, and an isolated deploy checkout before choosing the smallest sound change.
- D-3: Plan uses Linux for portable service/script checks; a native Windows gate is required only for Windows-specific checkout/restart behavior. Re-read placement APIs at dispatch; omit host pins unless needed and omit OS pins for portable work.

## Implementation slices

Pending source trace: classification and roster; land admission and canonical checkout mutations;
wrapper/host census; runbook and rc/master port instructions. Each final slice will name files and tests.

## Red witnesses and checkpoints

Pending: witnesses must invoke production land admission and actual wrapper/host census functions,
including non-Deploy and real-conflict controls. Checkpoint filters will be imported before handoff.
Verification remains a separate TestDesign stage after independent plan review.

## Performance budget

No product tests run for this Plan task. Checkpoint importer validation only, through the host build-slot gate.
Final proposed costs will distinguish build, admission, shared database setup, script/process work,
test body and cleanup. No measured p95 is available yet; provisional costs must be refined by
TestDesign under CARD-1615 and `docs/test-performance-standards.md`, with approximately 2x measured
p95 timeouts and Debug escalation for any selection or complete run over 30 minutes.

## Evidence gaps and handoff

Need exact writer predicate/registration, actual Git target-checkout behavior, restart ownership,
CARD-1610 timeout/refine evidence, production-path test inventory and importer result.
Live GETs of `/api/runner-defaults` and `/api/session-runners` succeeded on 2026-10-30;
placement is dynamic, not a fleet address embedded in this plan.

Next: independent plan review by a different company's model, then TestDesign; no implementation or deployment authorized by this document.
