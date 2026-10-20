# CARD-1280: unbound SourceLanding Mutation candidate

Status: pending; no deliberate mutation run by Code.
Original Code / landing owner: `865422b6-ccd8-43c8-a0c6-d78586cc68d9`.
Target: master only. Bind this candidate to the confirmed SourceLanding snapshot
after ordinary Review and publication; this document is not a bound execution pack.

Plan: [modal positioning](../plans/2026-10-20-card-1280-modal-positioning-plan.md),
Verification design V-1. No PC variants currently specified.

| PC | Mutation | Exact detecting selection | Required red |
|---|---|---|---|
| PC-1 | In `client/src/test/setup.ts`, return the actual `computePosition` and `autoUpdate` exports instead of their test replacements. Preserve all other setup. | `pwsh -NoProfile -File scripts/test-client.ps1 src/features/agents/AgentCreateModal.test.tsx -t "defaults to Standing orchestrator and submits preset with filled bundles" --no-file-parallelism` | The real dropdown and submitted ManualConfirm body assertions complete, then `expect(positioningStyleReads).toBe(0)` fails with a positive count. |

Mutation owns deliberate red/restore/green and missing-control discovery. Use
the host build-slot gate and only this one case for the cycle; restore the mock
and require the same case green. A query failure, timeout, import failure or
zero selected cases is not the intended red. Ordinary Code's pre-fix witness
does not discharge this post-landing obligation.

Limits: no proof of clipping, target-width sizing, arrow placement or viewport
collision geometry; the ordinary four-case selection does not qualify every
consumer of shared Vitest setup.
