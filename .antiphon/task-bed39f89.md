# CARD-1029 Code budget split: S1

S1 is implemented and verified: five selected rows, 14 passed, zero assertion failures or skips. CP-15 initially selected zero tests; its manifest filter was repaired and its exact three-method roster then passed. S2/S3 and CP-5..8 are still unimplemented/unrun. This is the operator-authorized first-part split, not full CARD-1029 closure or a complete Final certificate.

## Source, ownership and scope

- Original Code task / landing owner: bed39f89-24ec-4ef4-a70e-f8af615ae5b2.
- Branch: feat/card-task-bed39f89. Worktree: /work/worktrees/task-bed39f89.
- Task base: e22dd059a46413bd14dd410836231c26fb2fb91b.
- Prerequisite merge: 2704c24757d4e8c9b5cccbc6f103cf1e3a66dd7c, merging plan-pinned M 9b78712f6671d7ccb98bcf4a5ebea8f080d7cecd without rebase/reset. M supplies required CARD-1031/CARD-1022 plus already-published target changes. No new production behavior was authored in this task.
- Tested C-S1: 491c2efb7301d111029bd8aeddff32feb8441a1c (CP-3, CP-4, CP-16, CP-17 and initial zero-result CP-15).
- Tested filter repair: 241adc856d9b63291567b8bbf91fe66e77a0a9d4 (CP-15). C-S1..repair changes only the two plan/ledger Markdown files; test and production bytes are identical. Receipts retain their actual SHA, never relabelled to the later report commit.
- Plan: /work/worktrees/task-bed39f89/docs/superpowers/plans/2026-10-04-card-1029-inert-observation-verification-gaps-plan.md.
- Controls: /work/worktrees/task-bed39f89/docs/superpowers/plans/2026-10-04-card-1029-control-qualification.md.
- GET /api/runner-defaults and /api/session-runners read before verification. Portable local execution, no runner/platform pin. Restart: none; no activation needed for these test/docs edits. Any later activation remains caller-owned.

The brief's explicit 30–60-minute budget, no whole Unit/no Windows instruction, and permission to split override its generic Final profile. The active nine-row estimate is 55 minutes plus four bootstrap minutes before authoring. This slice selected only CP-3/4/15/16/17 (27 estimated row minutes). No full assembly, Unit lane, Windows lane or extra test group ran. Actual checkpoint wall time: 1014.2s + 180.5s. Six isolated row builds ran (five initial, one repaired-row build); unused manifest builds did not run. One ordinary execution per actual test; initial CP-15 executed zero. No reassurance repeats or loaded repeats.

## Authored changes

- tests/Antiphon.Tests/Application/CodexCliDescriptorCases.cs: independent literal 32768/32769 boundaries, NUL, literal/mixed-case dollar-secret and key placeholders across all five descriptor fields (35 vectors).
- tests/Antiphon.Tests/Application/CodexCliObservationTests.cs: all pure FromSpec boundary vectors; independent direct Sol, Frontier, Low, canonical lookup and public alias assertions before aggregate checks. Preserves all eight methods.
- tests/Antiphon.Tests/Application/RunnerCodexCliEvidenceTests.cs: 30 rejected descriptors through each of real local POST and framed production dispatch, with no child after refusal; PATH/PATHEXT equality through both transports; separately counted local and selected typed diagnostic calls before peer/result assertions.
- Plan/ledger: record the split, ten authored S1 witness rows and P-1's operator-overridden scope. Repair CP-15's TUnit OR syntax with the owner-documented suffix wildcards; same intended methods and count floor. No timeout or assertion was loosened.

PATH is the first field to expose shared runner-validator defects with a valid absolute native executable. Pure FromSpec proves representability for the other fields; filesystem-masked rejection is not credited as per-field Mutation proof. No deliberate mutant was run and no red qualification is claimed. These remain post-land Mutation obligations.

## Ordinary outcomes

| CP | Outcome | Scope |
|---|---|---|
| CP-3 | 1/1 passed | Exact RunnerCodexCliEvidenceTests.C959_Exact_probe_transport_is_bound |
| CP-4 | 8/8 passed | Entire CodexCliObservationTests; every named method observed in fresh TRX |
| CP-15 | Initial 0 executed, exit 3; repaired 3/3 passed | CodexPhoneHomeCreateTests.Signed_out_remote_create_refuses_with_codex_problem_details; Present_unknown_and_unavailable_probe_admit; ModelAvailabilityCreateTests.Create_without_IgnoreModelDisabled_is_still_409_while_held |
| CP-16 | 1/1 passed | Signed_out_codex_runner_still_claims_and_reuses_a_warm_session; claim/reuse/queue only, no recipient claim |
| CP-17 | 1/1 passed | Composed_router_measures_codex_in_its_own_home_and_gates_the_launch |
| CP-5, CP-6, CP-7, CP-8 | NOT IMPLEMENTED / NOT RUN | Seven gap methods, preserved for next Code slice |

V-13, V-14, V-19: PASS. V-21 and V-22: existing CP-4 witnesses PASS; new CP-5..8 witnesses remain NOT RUN. V-23, V-24, V-25, V-26: existing CP-4 witnesses PASS; remote matrix remains deferred. V-27: PASS for claim/reuse/queue only. R-1, R-2, R-3, R-6, R-7: PASS at their selected rows. R-4/native and other V-1..12 wire/probe/native obligations stay in their named follow-ups. No manual/native acceptance is claimed.

Each fresh TRX was inspected for class/method identity, nonzero count and outcomes. Checkpoint validate accepted C-S1 rows CP-3/4/16/17 and repair row CP-15 with expected-source-sha equal to their actual committed HEAD. Both source envelopes are clean, dirtyFiles=0, buildSource=verified. Every executed build and row held slot=granted, waited=0s. Initial run overall exit=3 remains preserved; only its CP-15 selection was incomplete. The rerun exit=0.

The only non-row build was the plan-authorized tool bootstrap through scripts/build-slot.ps1, OutputPath=bin-c1029-tool/: 5.41s build, zero errors, one pre-existing CS8602 warning at TaskOwnerGuard.cs:170; slot=granted waited=0s. No other build/test driver ran. Checkpoint clean removed the first run's owned alternate outputs; the green rerun removed its outputs. The bootstrap output was removed after validation. Generated build evidence directories remain ignored and retained; they are not executable outputs.

## Unedited checkpoint receipts

```text
CHECKPOINT CP-3 commit=491c2efb7301d111029bd8aeddff32feb8441a1c build=ok filter=/*/*/RunnerCodexCliEvidenceTests/C959_Exact_probe_transport_is_bound executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-bed39f89/.antiphon/checkpoints/20261004-154327-ef35/rows/CP-3/run.trx slot=granted waited=0s dirty=0 source=491c2efb7301d111029bd8aeddff32feb8441a1c sourceState=clean buildSource=verified
CHECKPOINT CP-4 commit=491c2efb7301d111029bd8aeddff32feb8441a1c build=ok filter=/*/*/CodexCliObservationTests/* executed=8 passed=8 failed=0 skipped=0 trx=/work/worktrees/task-bed39f89/.antiphon/checkpoints/20261004-154327-ef35/rows/CP-4/run.trx slot=granted waited=0s dirty=0 source=491c2efb7301d111029bd8aeddff32feb8441a1c sourceState=clean buildSource=verified
CHECKPOINT CP-15 commit=491c2efb7301d111029bd8aeddff32feb8441a1c build=ok filter=/*/*/(CodexPhoneHomeCreateTests)|(ModelAvailabilityCreateTests)/(Signed_out_remote_create_refuses_with_codex_problem_details)|(Present_unknown_and_unavailable_probe_admit)|(Create_without_IgnoreModelDisabled_is_still_409_while_held) executed=0 passed=0 failed=0 skipped=0 trx=/work/worktrees/task-bed39f89/.antiphon/checkpoints/20261004-154327-ef35/rows/CP-15/run.trx slot=granted waited=0s dirty=0 source=491c2efb7301d111029bd8aeddff32feb8441a1c sourceState=clean buildSource=verified
CHECKPOINT CP-16 commit=491c2efb7301d111029bd8aeddff32feb8441a1c build=ok filter=/*/*/PhoneHomeTaskDispatchProjectionTests/Signed_out_codex_runner_still_claims_and_reuses_a_warm_session executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-bed39f89/.antiphon/checkpoints/20261004-154327-ef35/rows/CP-16/run.trx slot=granted waited=0s dirty=0 source=491c2efb7301d111029bd8aeddff32feb8441a1c sourceState=clean buildSource=verified
CHECKPOINT CP-17 commit=491c2efb7301d111029bd8aeddff32feb8441a1c build=ok filter=/*/*/CodexProviderAuthRoutingTests/Composed_router_measures_codex_in_its_own_home_and_gates_the_launch executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-bed39f89/.antiphon/checkpoints/20261004-154327-ef35/rows/CP-17/run.trx slot=granted waited=0s dirty=0 source=491c2efb7301d111029bd8aeddff32feb8441a1c sourceState=clean buildSource=verified
CHECKPOINT CP-15 commit=241adc856d9b63291567b8bbf91fe66e77a0a9d4 build=ok filter=/*/Antiphon.Tests.Application/(CodexPhoneHomeCreateTests*)|(ModelAvailabilityCreateTests*)/(Signed_out_remote_create_refuses_with_codex_problem_details*)|(Present_unknown_and_unavailable_probe_admit*)|(Create_without_IgnoreModelDisabled_is_still_409_while_held*) executed=3 passed=3 failed=0 skipped=0 trx=/work/worktrees/task-bed39f89/.antiphon/checkpoints/20261004-160150-0c7f/rows/CP-15/run.trx slot=granted waited=0s dirty=0 source=241adc856d9b63291567b8bbf91fe66e77a0a9d4 sourceState=clean buildSource=verified
```

Raw evidence remains ignored under:

- /work/worktrees/task-bed39f89/.antiphon/checkpoints/20261004-154327-ef35/ (includes initial zero-result CP-15).
- /work/worktrees/task-bed39f89/.antiphon/checkpoints/20261004-160150-0c7f/ (repaired CP-15).

## Still owed

Next Code must implement the seven CodexCliObservationGapTests methods in CP-5..8 using real delivery/receipt paths and separately owned recipient/server-graph lifetimes, then rerun affected fixture consumers. P-1 blocks only PC-274 in paused Mutation; do not repair it or treat it as an ordinary-checkpoint blocker.

All named follow-ups remain: F-Observation (CP-1 and CP-3 remainder); F-Faults (CP-9/10, 44 vectors); F-Matrix (CP-11..14, exactly 38 real Worktree Retry + 38 seeded warm + 38 seeded recreated-warm = 114 vectors); F-Regression (remaining CP-15/16 methods); F-Native (CP-2 exact L0959 29-result qualification and CP-18 current-source 30-result M-derived roster plus additions). None was commissioned or passed here.

All 211 primary controls remain pending method-scoped SourceLanding Mutation: PC-1..14,16..84,89..109,111..120,122..127,153..159,184..188,190..192,194,199..203,205..255 (original 192); PC-256 (repair); PC-257..274 (18 additional). PC-274 alone retains the P-1 seam blocker. All 21 remote-only variants also remain pending: PC-225/228/231/234 Create (4); PC-226/229/232/235 Retry (4); PC-227/230/233/236 cold and seeded warm (8); PC-237,238,239,240,253 (5). The ledger retains every ID and recipe; ordinary green discharges none.

The full task-range evidence-history guard passed through C-S1 (27 introduced commits, zero violations). It is rerun against the final pushed report commit before settlement; the final task message records that terminal outcome. This Markdown report is the only committed runtime evidence summary; no generated logs, JSON or TRX are committed.

--- next stage ---
next: code
handoff: S1 passes CP-3/4/15/16/17 (14 tests); implement the seven S2/S3 gap methods for CP-5..8 and rerun changed fixture consumers. Preserve 38/38/38 follow-ups and all pending PCs. CP-16 is claim/reuse/queue only; P-1 blocks only post-land PC-274. Keep original Code bed39f89 as landing owner.
artifact: docs/superpowers/plans/2026-10-04-card-1029-inert-observation-verification-gaps-plan.md
