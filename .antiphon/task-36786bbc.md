# CARD-1446 Code report (task 36786bbc): milestone 2, S4 complete and green, S5 deferred

Branch `feat/card-task-36786bbc`, worktree `/work/worktrees/task-36786bbc` (server2 runner mirror), start ref
`3fa8d7bd480d7df345aea2bb46c042be19f78a76` (origin/master at dispatch; milestone 1 landed there). Tested tip
`88fa931aa61c1d7a4c76391e1938f141fd0fada9`; this report commit is the final tip. Plan
`docs/superpowers/plans/2026-10-22-card-1446-stale-landings-plan.md` (unchanged; on master and the Plan branch).
Amendment carrying only the CP-7..CP-16 table with the rewritten filters:
`docs/superpowers/plans/2026-10-22-card-1446-stale-landings-plan-m2-checkpoints.md` (the `--plan` the tool ran).
Lane: `GET /api/runner-defaults` -> `globalRunnerId: server2`; `GET /api/session-runners` lists server2 (linux) and
desktop (windows); every row ran on server2, no `-Runner`, no `-Platform`. No session waits for input (plan D-6).
Nothing was deleted anywhere outside the test temp directories.

## Outcome

S4a (copy-based evidence retention), S4b (`**/.antiphon/**` evidence catch-all), S4c (paced Publication lane,
`LandCleanupStatus.Abandoned`, `AbandonCleanupAsync`, `Cleanup != Abandoned` in `AntiphonRefProtection`) and S4d
(living docs) are implemented, red-witnessed, green and receipt-validated at `88fa931aa`. **S5 (candidate-row dedupe,
`LastOutcomeKey` migration, Held-row retention; V-11, V-12, CP-14..CP-16) is not started**: the 60-minute budget
was spent on S4 (two checkpoint-tool runs, 39 minutes of wall time). Per the brief, S4 is reported as the coherent
subset; S5 needs a third Code task.

## Commits (base..HEAD, oldest first)

| SHA | Slice | Note |
|---|---|---|
| `60a2994ec24df5dc44482831cddc7263a20f313c` | S4a-test | `FileSystemEvidenceRetentionTests`, `RetainedEvidenceRoot`, refusing skeleton |
| `6f5cc369eb77f9851e0b10193fc4c4c1cec4e3d5` | S4b-test | classifier `C1446_unlisted_antiphon_spill_is_evidence_not_protected` |
| `b1be44b2dd7dacebfdf57c83522b0e216c888320` | S4c-test | `Abandoned = 4`, cooldown/cap settings, empty policy, abandon skeleton, V-8/V-9/V-10 tests, two RV-1 rows |
| `a8e337a024f7602ae6174b431bda8a5d7abe8496` | plan amendment | CP-7..CP-16 table with rewritten filters; **CP-7, CP-9, CP-11 red here** |
| `9ecac5ead98f14e42f5ca4042b5320d973f85905` | S4a-impl | `FileSystemEvidenceRetention`, `Program.cs` registration |
| `0a7b70e5fd446a2a90bfdec21e02b4abbce7d00c` | S4b-impl | catch-all default, `appsettings.json`, classifier precedence, existing rows re-asserted |
| `4e0ca79710edd8a64e86806bde2c9c3d2b03b255` | S4c-impl | policy, sweep lane, `AbandonCleanupAsync`, protocol, ref-protection clause |
| `88fa931aa61c1d7a4c76391e1938f141fd0fada9` | S4d | docs; **CP-8, CP-10, CP-12, CP-13, RV-1 green here** |

## Files changed

- S4a: `server/Infrastructure/Git/FileSystemEvidenceRetention.cs` (new), `server/Application/Settings/WorktreeCleanupSettings.cs`
  (`RetainedEvidenceRoot`, null derives `<Git:WorktreeBasePath>/.retained`), `server/Program.cs` (replaces `RefusingEvidenceRetention`),
  `tests/Antiphon.Tests/Infrastructure/FileSystemEvidenceRetentionTests.cs` (new; V-6, 4 cases). Budget refusals
  (`evidence_retention_budget_files`, `_bytes`) are decided before any copy; `evidence_retention_conflict` for an
  existing destination with different bytes (identical bytes are accepted as an earlier pass); `evidence_retention_verify_failed`
  when the copied hash differs (the bad copy is removed); `evidence_retention_source_missing` / `_path_escapes` for a listed path that
  is gone or escapes. A copy, never a move.
- S4b: `WorktreeCleanupSettings.DefaultRetainedIgnored` + `server/appsettings.json` gain `**/.antiphon/**`;
  `server/Application/Services/WorktreeIgnoredContentClassifier.cs` treats a pattern ending in `.antiphon/**` as a catch-all consulted
  **after** the explicit `.antiphon` disposable patterns (otherwise `.antiphon/inbox/*.md` would have become evidence, contradicting
  V-7 and three existing rows); `tests/.../WorktreeIgnoredContentClassifierTests.cs`: new V-7 method; `Nested_antiphon_segment_is_protected`
  became `Nested_antiphon_segment_is_evidence_not_disposable` (the plan's catch-all names nested spills, so they are evidence; a nested
  protected name still wins) plus `Nested_antiphon_segment_is_protected_without_the_catch_all` keeping the pre-CARD-1446 contract;
  `.antiphon/inbox/photo.png` moved from the protected row to the evidence row; `Configured_antiphon_pattern_is_explicit` asserts
  `other.txt` is evidence under the defaults and protected with `RetainedIgnored = []`. These are contract changes the plan asked for
  (G-5: an unlisted spill is no longer Protected), not loosened assertions; every bucket is still asserted exhaustively.
- S4c: `server/Domain/Enums/LandingEnums.cs` (`Abandoned = 4`), `WorktreeResidueSettings` (`CleanupRetryCooldownDays` 7,
  `MaxCleanupRetries` 3) + validator (`>= 0`), `server/Application/Services/PublicationLanePolicy.cs` (new; `Decide` checks Abandon
  before Cooldown: a third refusal stamps a cooldown and waiting it out would only queue a forbidden fourth retry),
  `WorktreeResidueSweepService.cs` (selection `Cleanup != Complete && != Abandoned`; reads `landing:<opId:N>` cursors and the count of
  earlier `Queued` rows per operation; `Held/cooldown` consumes no action; `Refused/cleanup_abandoned` after `AbandonCleanupAsync`, or
  the exception's code; a queued retry stamps `NotBefore = now + CleanupRetryCooldownDays`), `AgentTaskLandService.AbandonCleanupAsync`
  (task row lock; requires Active, `ActiveLandingId`, `HasPublication`, `Cleanup == Refused`, no pending request; keeps `LastReason`;
  one `LandingCleanup` event with the operation columns set), `AgentTaskLandingProtocol.CleanupAsync` (a refused retry keeps
  `Abandoned`), the interrupted-after-publication path no longer resets `Abandoned` to `Pending`, `AntiphonRefProtection.cs` landing
  clause `&& o.Cleanup != Abandoned`. Tests: `tests/.../WorktreeResiduePublicationLaneTests.cs` (new; V-8 seven rows, V-9),
  `WorktreeLandingCleanupRetryTests.cs` (+`C1446_AbandonedStaysAbandonedOnRefusedRetry`, `C1446_AbandonedCompletesOnCleanRetry`),
  `LandRefRetentionTests.C1432_protection_rule_rows` (+`landing-cleanup-refused` true, `landing-cleanup-abandoned` false).
- S4d: `docs/orchestration-loop.md` (line ~159 retention sentence; line ~2196 activation prerequisites, cooldown, cap, `Abandoned`),
  `docs/testing-and-build.md` (line ~357 retention sentence). The CARD-1459 stale-doc lines (`docs/orchestration-loop.md:1639`,
  `docs/ops-http.md:179`, `AbandonedLandingDays`) are **not** covered by S4d's two named sentences and were not touched.

## Checkpoints

Both runs through the checkpoint tool (`tools/Antiphon.Checkpoints`, built under `scripts/build-slot.ps1` into `bin-c1446-tool/`,
deleted afterwards) with the amendment manifest `.antiphon/c1446/manifest-m2.yaml` (`import --plan`, ignored, kept). The plan says
red rows go through `scripts/run-checkpoint.ps1`; the brief asked for the checkpoint tool on server2, so the red rows ran as one tool
run (`--rows CP-7,CP-9,CP-11`, verdict RED exit=1 as intended) at the test commits, before any implementation commit existed.

Red run `20261022-065328-85bf` at `a8e337a02` (S4a-test, S4b-test, S4c-test and the amendment; no impl):
```
CHECKPOINT CP-7 commit=a8e337a024f7602ae6174b431bda8a5d7abe8496 build=ok filter=/*/*/FileSystemEvidenceRetentionTests/C1446_copies_and_verifies executed=1 passed=0 failed=1 skipped=0 trx=/work/worktrees/task-36786bbc/.antiphon/checkpoints/20261022-065328-85bf/rows/CP-7/run.trx slot=granted waited=0s dirty=0 source=a8e337a024f7602ae6174b431bda8a5d7abe8496 sourceState=clean buildSource=verified
CHECKPOINT CP-9 commit=a8e337a024f7602ae6174b431bda8a5d7abe8496 build=ok filter=/*/*/WorktreeIgnoredContentClassifierTests/C1446_unlisted_antiphon_spill_is_evidence_not_protected executed=1 passed=0 failed=1 skipped=0 trx=/work/worktrees/task-36786bbc/.antiphon/checkpoints/20261022-065328-85bf/rows/CP-9/run.trx slot=granted waited=0s dirty=0 source=a8e337a024f7602ae6174b431bda8a5d7abe8496 sourceState=clean buildSource=verified
CHECKPOINT CP-11 commit=a8e337a024f7602ae6174b431bda8a5d7abe8496 build=ok filter=/*/*/WorktreeResiduePublicationLaneTests/C1446_policy_rows executed=7 passed=5 failed=2 skipped=0 trx=/work/worktrees/task-36786bbc/.antiphon/checkpoints/20261022-065328-85bf/rows/CP-11/run.trx slot=granted waited=0s dirty=0 source=a8e337a024f7602ae6174b431bda8a5d7abe8496 sourceState=clean buildSource=verified
```
CP-7 failed on `result.Reason should be null but was "evidence_retention_unavailable"`; CP-9 on the spill's Evidence bucket being
empty (it was Protected); CP-11 on `cooldown-active` (Queue, not Cooldown) and `three-queued-abandons` (Queue, not Abandon), the
other five rows pass with the empty policy as the plan predicted.

Green run `20261022-070830-713f` at `88fa931aa` (**verdict GREEN exit=0**, outputs deleted; `validate --evidence report.json
--expected-source-sha 88fa931aa...` -> `CHECKPOINT SOURCE VALID source=88fa931aa61c1d7a4c76391e1938f141fd0fada9 rows=5`):
```
CHECKPOINT CP-8 commit=88fa931aa61c1d7a4c76391e1938f141fd0fada9 build=ok filter=/*/*/FileSystemEvidenceRetentionTests/* executed=4 passed=4 failed=0 skipped=0 trx=/work/worktrees/task-36786bbc/.antiphon/checkpoints/20261022-070830-713f/rows/CP-8/run.trx slot=granted waited=0s dirty=0 source=88fa931aa61c1d7a4c76391e1938f141fd0fada9 sourceState=clean buildSource=verified
CHECKPOINT CP-10 commit=88fa931aa61c1d7a4c76391e1938f141fd0fada9 build=ok filter=/*/*/WorktreeIgnoredContentClassifierTests/* executed=50 passed=50 failed=0 skipped=0 trx=/work/worktrees/task-36786bbc/.antiphon/checkpoints/20261022-070830-713f/rows/CP-10/run.trx slot=granted waited=45s dirty=0 source=88fa931aa61c1d7a4c76391e1938f141fd0fada9 sourceState=clean buildSource=verified
CHECKPOINT CP-12 commit=88fa931aa61c1d7a4c76391e1938f141fd0fada9 build=ok filter=/*/*/(WorktreeResiduePublicationLaneTests*)|(WorktreeLandingCleanupRetryTests*)/C1446* executed=10 passed=10 failed=0 skipped=0 trx=/work/worktrees/task-36786bbc/.antiphon/checkpoints/20261022-070830-713f/rows/CP-12/run.trx slot=granted waited=0s dirty=0 source=88fa931aa61c1d7a4c76391e1938f141fd0fada9 sourceState=clean buildSource=verified
CHECKPOINT RV-1 commit=88fa931aa61c1d7a4c76391e1938f141fd0fada9 build=reused filter=/*/*/LandRefRetentionTests/C1432_protection_rule_rows executed=34 passed=34 failed=0 skipped=0 trx=/work/worktrees/task-36786bbc/.antiphon/checkpoints/20261022-070830-713f/rows/RV-1/run.trx slot=granted waited=0s dirty=0 source=88fa931aa61c1d7a4c76391e1938f141fd0fada9 sourceState=clean buildSource=verified
CHECKPOINT CP-13 commit=88fa931aa61c1d7a4c76391e1938f141fd0fada9 build=reused filter=/*/*/(WorktreeLandingCleanupRetryTests*)|(WorktreeResidueSweepTests*)|(WorktreeResidueRecoveryTests*)/* executed=56 passed=56 failed=0 skipped=0 trx=/work/worktrees/task-36786bbc/.antiphon/checkpoints/20261022-070830-713f/rows/CP-13/run.trx slot=granted waited=0s dirty=0 source=88fa931aa61c1d7a4c76391e1938f141fd0fada9 sourceState=clean buildSource=verified
```
Rosters (TRX): CP-8 `C1446_copies_and_verifies` + `C1446_refusals(bytes|files|conflict)`; CP-12 seven `C1446_policy_rows`,
`C1446_abandon_writes_status_and_event`, `C1446_AbandonedStaysAbandonedOnRefusedRetry`, `C1446_AbandonedCompletesOnCleanRetry`;
RV-1 34 rows incl. `landing-cleanup-refused`/`landing-cleanup-abandoned`; CP-13 56 (16 cleanup-retry, 18 sweep, 22 recovery).
Repetitions per unchanged selection: every row ran exactly once (3 red, 5 green); no reruns, no flakes. Every build and row
`slot=granted`. Builds: s4ared 211 s, s4bred 202 s, s4cred 331 s, s4a 225 s, s4b 390 s, s4c 233 s (serialized by the tool).

## Filter rewrites (stated as the brief asked; manifest/amendment only, plan file untouched)

- CP-12 `/*/*/(WorktreeResiduePublicationLaneTests/*)|(WorktreeLandingCleanupRetryTests/C1446*)` ->
  `/*/*/(WorktreeResiduePublicationLaneTests*)|(WorktreeLandingCleanupRetryTests*)/C1446*` (every method of the new class is `C1446_*`).
- CP-13 `/*/*/(WorktreeLandingCleanupRetryTests/C459*)|(WorktreeResidueSweepTests/*)|(WorktreeResidueRecoveryTests/*)` ->
  `/*/*/(WorktreeLandingCleanupRetryTests*)|(WorktreeResidueSweepTests*)|(WorktreeResidueRecoveryTests*)/*` (a superset: the two
  `C1446` retry methods ran again beside the `C459*` rows).
- CP-16 `/*/*/(WorktreeResidueRegistrationTests/*)|(WorktreeRetirementRaceTests/*)|(CardWorktreeMirrorCleanupTests/*)` ->
  `/*/*/(WorktreeResidueRegistrationTests*)|(WorktreeRetirementRaceTests*)|(CardWorktreeMirrorCleanupTests*)/*` (not run; S5).

## Unlisted builds and runs

- **RV-1** `LandRefRetentionTests/C1432_protection_rule_rows` (34 cases): plan D-3's conditional row; the `Cleanup != Abandoned`
  landing clause was added to `AntiphonRefProtection` with two new rows. Run on CP-12's build.
- The checkpoint tool build (`bin-c1446-tool/`, 8 s, under a build slot). No other build or test driver ran.

## Deviations from the plan

- CP-11 has 7 policy rows (plan: 4): the four V-8 behaviours plus `cooldown-expired-queues`, `cap-zero-never-abandons`,
  `pacing-off-ignores-cursor` (the rollback switches); 2 of 7 failed red, as the plan's "at least 2" expects.
- S4b needed a precedence rule the plan did not state (catch-all after the explicit `.antiphon` disposable patterns) and changes
  four existing classifier expectations, listed under Files changed.
- `PublicationLanePolicy.Decide` checks Abandon before Cooldown (plan order was unspecified); `Held/cooldown` is also reported on
  preview/inventory runs (informational, no action).
- Red rows ran through the checkpoint tool, not `run-checkpoint.ps1`, as the brief directed.

## Verification design outcomes

| ID | Outcome |
|---|---|
| V-6 | pass (CP-8; red at CP-7) |
| V-7 | pass (CP-10; red at CP-9) |
| V-8 | pass (CP-12, 7 rows; red at CP-11) |
| V-9 | pass (CP-12 `C1446_abandon_writes_status_and_event`) |
| V-10 | pass (CP-12, both methods; not a red row by plan, but they fail without the protocol change) |
| V-13 | CP-13 pass (56); CP-16 deferred with S5 |
| V-11, V-12 | deferred (S5 not started); CP-14..CP-16 not run |

PCs pending for method-scoped SourceLanding Mutation (plan "Mutation targets", S4 part): `Abandoned` kept versus `Refused` in
`AgentTaskLandingProtocol.CleanupAsync` (detected by `C1446_AbandonedStaysAbandonedOnRefusedRetry`); the `>=` at
`MaxCleanupRetries` in `PublicationLanePolicy.Decide` (`three-queued-abandons`/`two-queued-no-cooldown`); the `NotBefore > now`
comparison (`cooldown-active`/`cooldown-expired-queues`); the `Cleanup == Refused` guards in `Decide` and `AbandonCleanupAsync`
(`pending-never-abandons`, the second ConflictException in V-9); the budget `>` comparisons and the destination hash equality in
`FileSystemEvidenceRetention` (`C1446_refusals`); the catch-all ordering in the classifier (V-7's `.antiphon/inbox/a.md`); the
`Cleanup != Abandoned` clause (`landing-cleanup-abandoned`). S5's targets (`LastOutcomeKey` equality, retention filter) remain.

## Landability, evidence policy, rc

- `git fetch origin master` -> `6c1b0704b8b078d26adb359f1d7e9fa28def2c19`; `git merge-tree --write-tree origin/master HEAD` at
  `88fa931aa` exit 0, no conflicts.
- `scripts/check-evidence-diff.ps1 -Repository . -BaseRef 3fa8d7bd4... -HeadRef 88fa931aa...`: `commits=8 entries=0 violations=0`.
- **rc port (`origin/rc/2026-10-20` = `ceb6e3d6b`), per-commit `merge-tree --merge-base` chain:** S4a-test `60a2994ec`, S4b-test
  `6f5cc369e`, the amendment `a8e337a02`, S4a-impl `9ecac5ead`, S4b-impl `0a7b70e5f` and S4d `88fa931aa` apply clean; **S4c is not
  rc-eligible**: S4c-test `b1be44b2d` conflicts on `WorktreeResidueSettings.cs`/`...Validator.cs` (content; rc lacks milestone 1's
  `AbandonedLandingDays` hunk, so pick S1 first) and modify/delete on `LandRefRetentionTests.cs` (CARD-1432 absent on rc); S4c-impl
  `4e0ca7971` is modify/delete on `AntiphonRefProtection.cs` (CARD-1432). S4d's text describes S4c's pacing, so port it with S4c or
  not at all. S4a/S4b alone already turn 134 of the 145 `Cleanup=Refused` rows retryable (G-5), but Execute must stay false on rc
  until S4c's pacing is there (G-6).

## Deploy and operator notes

New settings with safe defaults, no migration: `WorktreeCleanup:RetainedEvidenceRoot` (null -> `<WorktreeBasePath>/.retained`),
`WorktreeResidue:CleanupRetryCooldownDays` (7; 0 disables pacing), `WorktreeResidue:MaxCleanupRetries` (3; 0 disables abandonment).
`appsettings.json` `WorktreeCleanup:RetainedIgnored` now mirrors the `**/.antiphon/**` default (an operator override without it
restores the old classification). `WorktreeResidue:Execute` stays false; S4d's activation is the orchestrator's commissioned deploy
after land. Retained copies under `.retained/` are never deleted by code (the plan defers their retention).

## Third milestone (not done): S5

`WorktreeResidueCandidateCursor.LastOutcomeKey` + migration `AddWorktreeResidueCursorLastOutcome` (`dotnet tool restore`, `dotnet ef
migrations add` through a build slot), SettledTask/Publication lane dedupe (`<Lane>|<Outcome>|<ReasonCode>`), the
`candidates=/rows=/unchanged=` log line, `Retention:WorktreeResidueHeldRowRetentionDays` (30) + `DataRetentionService.PruneWorktreeResidueHeldRowsAsync`,
`appsettings.json` Retention block, `WorktreeResidueCandidateRowTests` (V-11, V-12), CP-14..CP-16 (about 27 minutes of builds and runs
with the CP-16 filter above). The amendment file already carries the rows.
