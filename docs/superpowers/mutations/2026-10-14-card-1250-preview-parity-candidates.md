# CARD-1250 preview parity — unbound Mutation candidates

Round-3 repair Code task: `63109ffc-25c4-449f-8f01-3958cf20ef2c`.
Prior repair Code task: `679ecbea-ed90-4c82-8937-5d8b824f7b74`.
Original Code / landing owner: `0814a3a5-8a8e-456a-869f-1e2a10f359ae`.
Prior ordinary verified source: `4eb039453416c138f149a187274ef1e30f6a8aed`.
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

Round-3 additions (pending, unbound):

| PC / variant | Guarded mutation | Witness | Status |
|---|---|---|---|
| PC-17 role/any/config × held/quota/forbidden (9 model variants) | Remove Required single-row refusal handling | model V17: D1 required-forbid-only-single-<source>-<state> | pending |
| PC-17 held/quota/forbidden (3 modal variants) | Render that refusal as exhaustion | modal V21: required-forbid-only-single-row-<state>-is-refused | pending |
| PC-18 available role/any/config (3 variants) | Refuse an available Required single row | model V17: D1 required-forbid-only-single-<source>-available | pending |
| PC-18 unpinned exhaustion | Apply Required refusal semantics without a Required pin | model V17: no-pin-single-held-still-blocked | pending |
| PC-19 level/kind (2 variants) | Drop the server-dependency text for withheld policy | model V17: role_policy_level_invalid-Orchestrator-withheld / role_policy_kind_unsupported-Orchestrator-withheld | pending |

V17 now has 58 rows; round-3 ordinary selection is 173 tests. Exact source/citation suffixes
come from the fresh Vitest JSON, since this repair corrects the round-2 source citations.

Round-4 additions (pending, unbound; no Code mutants):

| PC / variant | Guarded mutation | Witness | Status |
|---|---|---|---|
| PC-20 goal/card precedence | Drop card detection before stage composition | model V22 goal-leading, card-over-stage-pin, human-card-exempts-stage-forbid (three variants) | pending |
| PC-20 title boundaries | Parse whole goal, bind #N prose, omit clamp or integer bound | model V22 goal-second-line-not-title, goal-hash-not-bound, goal-after-clamp, goal-overflow-not-bound (four variants) | pending |
| PC-20 card origin | Drop retained cardContext from HandBackButton | modal V23 hand-back-prefill-and-edited-goal-withhold-target | pending |
| PC-20 badge | Display an explicit tier for unknown card routing | modal V23 typed-card-overrides-stage-refusal-and-explicit-tier | pending |
| PC-21 disclosure | Remove modal admission note | modal V24 modal-discloses-admission-dependency-with-explicit-tier | pending |
| PC-22 kind/agent | Ignore explicitKind or explicitAgentId context | model V22 explicit-kind-unobserved / standing-agent-explicit (two variants) | pending |

The round-4 selection has 58 retained V17 rows, 46 V22 rows, and 223 ordinary Vitest cases.

| PC / variant | Guarded mutation | Witness | Status |
|---|---|---|---|
| PC-23 supplementary prefix/suffix/digit (3 variants) | Use Unicode code-point word boundaries instead of UTF-16 units | model V22 goal-supplementary-prefix / goal-supplementary-suffix / goal-supplementary-digit-suffix | pending |
| PC-23 joiner/nonjoiner prefix/suffix (4 variants) | Omit .NET boundary join controls | model V22 goal-joiner-prefix / goal-joiner-suffix / goal-nonjoiner-prefix / goal-nonjoiner-suffix | pending |
| PC-24 Check/Distill/Diagnose (3 variants) | Let a specialist use the default matrix | model V22 specialist-Check / specialist-Distill / specialist-Diagnose | pending |
