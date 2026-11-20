# Review r1: run e7f92f66 repairs (CARD-1514, Debug d591d78b)

Review task `d36e22c2`. Subject `feat/card-task-d591d78b` at `82822d79f3d0faf2bc73f2921455e288be8d2365`, range `810e7fa73dfd66fd11d40af19e33f0818ecfba9d..82822d79f3d0faf2bc73f2921455e288be8d2365` (6 commits). Round 1, Final, Full ordinary scope. Author Grok, reviewer Claude.

Verdict: **CLEAN**. No blockers. Advisories are below and do not hold the land.

## Evidence

Checkpoint run `20261120-040713-36e7` over `docs/superpowers/plans/2026-11-20-run-e7f92f66-failures-checkpoints.md`, all 10 rows, cold build `bin-d591`, verdict GREEN exit 0. `validate --expected-source-sha 82822d79f...` printed `CHECKPOINT SOURCE VALID source=82822d79f3d0faf2bc73f2921455e288be8d2365 rows=10`.

| CP | Result |
|---|---|
| CP-1 AgentTaskLandHeldNotificationTests | 12/12, 63 s (C641 rewind 36.0 s) |
| CP-2 PhoneHomeBackpressureTests | 9/9, 41 s (Slow_observer 10.3 s) |
| CP-3 AgentTaskInputFallbackTests | 13/13, 50 s |
| CP-4 WorktreeDefaultCallerTests | 8/8, 55 s |
| CP-5 C0824RefOnlyIdentityTests/Mode_round_trips_with_legacy_default | 1/1, 37 s (test 12.3 s) |
| CP-6 nine-class guard union | 81/81, 76 s, `VIOLATIONS 0 LISTED 162` |
| CP-7 derived-index driver build | exit 0 |
| CP-8 DerivedImpactIndexTests | 27/27, 211 s |
| CP-9 land-graph `-Check` | exit 0, approved |
| CP-10 check-evidence-diff | `commits=6 entries=0 violations=0` |

Unlisted runs, with reasons: one native `dotnet build tests/Antiphon.Tests` (188 s, build-slot) for the brief's 10-run timing of `PhoneHomeBackpressureTests`; ten `--no-build` class runs, each leased separately: 10/10 green, 9/9 each, wall 34.2-39.0 s including host startup, load 5-7.

`git merge-tree --write-tree origin/master(d36d95412) 82822d79f` is clean (tree `d0bedbbe8`). Master has 21 commits past the base. Of the touched areas, master changed only `tests/derived-impact-index.json`.

## Judgements

A. Migration rewind helper. `DetachLandVerificationFailureAsync` is test-only (`TestHelpers/MigrationTestDatabase.cs`) and runs against the real Postgres test clone. It drops the columns, the partial index and the history row only when all three failure columns are null. Otherwise it throws the migration's own message. `AddLandVerificationFailure.Down` is unchanged and `LandVerificationFailureMigrationTests` still requires it to throw. Both callers migrate down and then back up with `MigrateAsync()`, so the deleted history row re-applies the Up. C641 and C0824 then write `AgentTaskLandings` rows, which would fail if the columns were missing. The count check and the drop are separate statements, not one transaction. The clone is per test and has no concurrent failure writer, so this is not a blocker. Other rewinds through this migration: `CreateReversibleTipAsync` / `CreateAsync(predecessor)` users never apply it. The tip-clone rewinds in `AgentTaskLandingPersistenceTests` and `PipelineDefinitionMigrationTests` use fresh or own databases. Only the two named methods call `DetachHostLoadAsync`.

B. Activity hold. Product `RecordActivityAsync` runs the tagged UPDATE through `ExecuteSqlInterpolatedAsync`, which is what `NonQueryExecutingAsync` intercepts. The hold is gated by `Hold` and released by a TCS that stays completed, so later commands pass at once. A wrong session held would fail `Single(...)["SessionId"].ShouldBe(other)`. If the tag were dropped, `Entered` would never fire and the test would go red. The hold models the slow observer and does not decide attribution. `PhoneHomeSignalWait` (20 s) bounds one signal and is not the whole method. That signal now fires right after output: the method takes 10.3 s under load and the class 34-41 s.

C. Task-input host. `server/Program.cs:134` registers `AgentTaskGetPollMetrics` as a singleton, so production returns no 500. Only the test host was wrong. The counter is in memory, has no database access and keeps only allow-listed labels. The throwing placeholders for every other namespace type remain, and the body-absence assertions are unchanged.

D. Longpaths. Fixture only. The `nightly-run-impl.ps1` refusal (line 719) is unchanged. `WorktreeDefaultCallerTests` is the only C# test that launches the real script with `-NoSync` against a `ScratchGitRepo`. `LongPathsCheckoutScriptTests.LP_NoSyncRefuses` asserts the refusal, and the script harnesses are as the note lists.

E. All five desktop filters name an existing class and a single method declaration.

F. The why-missed note matches the code. Its three proposed guards are named and are not built.

## Advisories (cards, not land holds)

- A-1: The next irreversible migration will hit the same two rewinds. Guard 1 in the note is the structural fix, and it is not built yet.
- A-2: The check and the drop in `DetachLandVerificationFailureAsync` could run in one transaction for clarity.
- A-3: Master moved `tests/derived-impact-index.json` 21 commits ahead. The text merge is clean. The land should regenerate or re-check the index if `DerivedImpactIndexTests` runs post-merge.

Mutation is waived (operator 2026-11-12). No PC rows.
