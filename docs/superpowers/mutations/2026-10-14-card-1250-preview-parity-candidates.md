# CARD-1250 preview parity — unbound Mutation candidates

Repair Code task: `679ecbea-ed90-4c82-8937-5d8b824f7b74`.
Original Code / landing owner: `0814a3a5-8a8e-456a-869f-1e2a10f359ae`.
Ordinary verified source: `4eb039453416c138f149a187274ef1e30f6a8aed`.
Plan: [next-create preview](../plans/2026-10-14-card-1250-next-create-preview-plan.md).

These are candidates for post-Review, post-land SourceLanding Mutation. They are not a bound
execution pack. Code inserted no deliberate mutants. The baseline repair regressions ran red
against the unchanged f047d2ef7 implementation, then green after the repair. Mutation owns
binding, missing-control discovery and method-scoped baseline/red/restore/green evidence.

The witnesses below are Vitest tests. Bind each to its file and exact test-title selection with
`scripts/test-client.ps1 <file> -t <escaped-title>` through the build-slot gate. For a V17
parameter row, use the exact row identity from fresh Vitest JSON, including the server citation;
confirm a nonzero executed count. The checkpoint's six-file ordinary selection is not a PC
selection. Keep generated evidence in the assigned external evidence root.

Files: **model** = `client/src/features/settings/nextCreatePreview.test.ts`;
**section** = `client/src/features/settings/RolePolicySection.test.tsx`;
**modal** = `client/src/features/delegations/DelegateModal.test.tsx`.

| PC / variant | Guarded mutation | Exact witness identity within the named file | Status |
|---|---|---|---|
| PC-1 | Read Medium regardless of configured default tier | model: `C1250 V4 non-medium-configured-default-tier` | pending |
| PC-2 | Choose the head without availability | model: `C1250 V2 held-head-walks-to-next-candidate` | pending |
| PC-3 | Apply the matrix to Orchestrator | model: `C1250 V5 orchestrator-ignores-the-row-and-discloses-the-floor` | pending |
| PC-4 Worker | Drop the effective agent source arm | modal: `C1250 V16 agent-only-stage-pin` | pending |
| PC-4 Orchestrator | Drop the stage-pin agent binding arm | model: `C1250 V6 agent-only-stage-pin` | pending |
| PC-5 | Let the row override an explicit level | model: `C1250 V7 explicit-level-bypasses-the-row` | pending |
| PC-6 | Treat resolvedFrom none as a resolved matrix | model: `C1250 V8 unresolved-row-respects-read-certainty` | pending |
| PC-7 | Drop the unknown-floor guard on a single Orchestrator target | model: `C1250 V5 orchestrator-ignores-the-row-and-discloses-the-floor` | pending |
| PC-8 | Label exhaustion as the head target | model: `C1250 V3 every-candidate-held-is-blocked` | pending |
| PC-9 matrix | Omit stage forbid filtering | model V17: `F1 required-forbid-only` | pending |
| PC-9 pin list | Omit stage forbid filtering | model V17: `F1 legacy-forbidden-head` | pending |
| PC-10 forbid-only Preferred | Skip no-chain composition | model V17: `F2 preferred-forbid-only-refusal` | pending |
| PC-10 Preferred dedup | Do not deduplicate the policy target | model V17: `F2 preferred-dedup-held` | pending |
| PC-10 Required dedup | Keep duplicate pin pairs | model V17: `required-dedup-single` | pending |
| PC-11 | Omit Orchestrator's ClaudeCode walk filter | model V17: `F3 orchestrator-kind-filter` | pending |
| PC-12 | Floor a walked pin target | model V17: `F3 walked-medium-not-floored`; modal: `C1250 V19 walked-orchestrator-medium-is-not-floored` (separate model/UI variants) | pending |
| PC-13 | Turn a held single target into Blocked | model V17: `F4 single-required-held`; modal: `C1250 V18 single-held-pin-shows-refusal` (separate model/UI variants) | pending |
| PC-14 | Assume an unobserved Preferred policy tail is available | model V17: `preferred-policy-tail-unknown`; modal: `C1250 V20 unknown-policy-tail-does-not-promise-a-tier` (separate model/UI variants) | pending |
| PC-15 walk | Ignore quota when walking | model V17: `matrix-quota-skip` | pending |
| PC-15 single | Ignore single-target quota refusal | model V17: `required-single-quota` | pending |
| PC-16 conflict | Ignore Required explicit-level conflicts | model V17: `explicit-required-conflict` | pending |
| PC-16 narrowing | Keep the head instead of the explicitly narrowed survivor | model V17: `explicit-required-narrowed-single` | pending |

PC-7 now guards the missing configuration boundary: MinOrchestratorLevel is not in the read DTOs.
The expected output is unknown, not an invented High target. PC-12 separately guards the server's
unfloored walk. The two exact C097 V25 pin labels remain additional regression witnesses.
