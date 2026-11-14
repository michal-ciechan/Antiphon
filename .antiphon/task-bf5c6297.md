# CARD-1886 Final Review, task bf5c6297

Round 2, diff-only, blockers only. Verdict: clean. No product change in this commit.

Reviewed source: `6382939955d577630de802494698b794fe872a8b` on `feat/card-task-6a3c6a69` (also the tip this review branch started from). Code/landing owner: `6a3c6a69-5ec6-44c8-8ad4-bebc4dc8a540`. Previously reviewed S2 tip: `e9f3bab5c40e36dafe8ba095528396f3906f72ea` (Review 41e8a4a2, authoring task f56921e1). That SHA is not an ancestor of this branch. The equivalent cherry-pick is `fe0cd5bc905b5d7e477b4ede1c8147402c0195d2`. Reviewed range: `fe0cd5bc905b5d7e477b4ede1c8147402c0195d2..6382939955d577630de802494698b794fe872a8b` (three commits: `1c5525a0d`, `a059a4667`, `638293995`).

`reviewedSourceClean` is false. Every build and test here was a build-slot leased `dotnet build`, `dotnet run`, or `dotnet exec`. None is a checkpoint-tool receipt with verified build provenance. `ordinaryScopeCompleted` is Full for the commissioned round: the named claim-query, layering, and guard-union runs, the scratch red witness, the master cherry-pick, the inherited-red confirmation, evidence-diff, and merge-tree. Plan CP-2's recovery and routing rows were confirmed on current master and were not re-run as this repair's verdict, as the brief required. Plan CP-10's classes beyond the named guard union were outside the commissioned filter. Positive controls stay pending. Native Windows proof stays owed.

## Repair delta

Seven files, +148/−23, against `fe0cd5bc9`.

- `SessionRuntimeActionClaimQuery` no longer references `SessionRuntimeActionCoordinator`. `Query` takes `int queuePhase` and `string channelKind` and binds them in the same parameter order as S2: `sessionId` (uuid), `now` (timestamptz), `queuePhase` (integer), `channelKind` (text).
- The only production call is `DrainCoreAsync`: `Query(db, sessionId, now, QueuePhase, ChannelReplyDebt)`. `QueuePhase` is 8. `ChannelReplyDebt` is `"channel"`. The coordinator diff against the S2 cherry-pick is that call.
- `Runtime_action_claim_query_has_no_application_dependency_even_in_warn_mode` asserts the claim-query file has no edge into `server/Application/` in `Unlisted` or `Listed`. `allowlist.txt` remains `strict: false`. The repository test still passes when Unlisted is nonempty.
- `Query_binds_the_phase_and_channel_kind_supplied_by_its_caller` asserts parameter names, Npgsql types, and values for a non-production pair `(23, "custom-channel")`.
- The derived impact index was regenerated in its own commit.

## Blockers

1. SQL and plan. The raw SQL literal is 1414 bytes, sha256 `534dd92d2f2ff8e6c363d5c206a95abf6d4afd3e1ce162d8136c5bc4a8555abe`, identical in `e9f3bab5`, `fe0cd5bc9`, and `638293995`. Transcript, debt-heavy, and null-wildcard cases share that one literal. Claim tests at the reviewed SHA and on the master cherry-pick both passed 3/3. Planner costs reproduced: transcript newCost 86.30 (old about 500469.49), debt-heavy newCost 105.42 (old about 10704139.38). A call-site swap of the int and the string does not compile. Swapping the two `Value` assignments inside `Query` keeps the names and types and changes the bound values, which the binding test asserts in order, so that test fails. The green binding run is the non-swapped witness. No live mutant rebuild was done.

2. Layering witness. On a detached tree at `638293995` with `SessionRuntimeActionClaimQuery.cs` restored from `fe0cd5bc9` (the Application using and the in-query constant reads), `C1729LayeringGuardTests` executed 10, passed 9, failed 1. The failed method is `Runtime_action_claim_query_has_no_application_dependency_even_in_warn_mode`, on the edge `server/Infrastructure/Data/SessionRuntimeActionClaimQuery.cs -> SessionRuntimeActionCoordinator @ server/Application/Services/SessionRuntimeActionCoordinator.cs`. The same run printed `VIOLATIONS 1 LISTED 162` and `Repository_graph_satisfies_the_committed_allowlist` passed. The new witness fails while warn mode still passes, so it does not depend on `strict: false`.

3. `DrainCoreAsync`. The repair diff of the coordinator is the `Query` argument list. No other drain behavior changed.

4. No new Infrastructure to Application edge. Reviewed SHA and the master cherry-pick both printed `VIOLATIONS 0 LISTED 162 STALE 0 AMBIGUOUS 3`. The warn-mode claim-query method passed in both places. The claim-query file has no `Antiphon.Server.Application` reference.

## Runs

All through `scripts/build-slot.ps1`. `UseAppHost=false`. `TEMP=/tmp`. Native default output. Master at the time of the runs: `f805ae444dcfdec07fd1ed18727817b16f2e5bc0`.

Reviewed SHA `/work/worktrees/task-bf5c6297`:

| Row | Result |
|---|---|
| `dotnet build` `tests/Antiphon.Tests` | 0 errors, 2091 warnings. Lease `7ba4a3fd-f52a-416e-acbe-13256e1b61fe`. |
| Named guard union | 90 executed, 89 passed, 1 failed. The failure is `DerivedImpactIndexTests.regenerate_script_reports_changed_then_unchanged`: `Antiphon.DerivedImpactIndex.dll` is not produced by the test-project build. |
| Generator `scripts/regenerate-derived-impact-index` | 0 errors. Lease `1b00e18b-6ebf-4230-9b01-fdf75a0858b1`. Unlisted reason: the impact tests need that assembly. |
| `DerivedImpactIndexTests` after that build | 27 executed, 27 passed. |
| `RuntimeActionClaimQueryTests` | 3 executed, 3 passed. |
| Layering red scratch `/tmp/c1886-layering-red` | 10 executed, 9 passed, 1 failed, as intended. |

Master plus the ten branch commits, cherry-pick tip `44106d436ff61d691036e92954fe948f185242b6` at `/tmp/c1886-master-repair`. `git cherry` marked all ten `+`. Product claim-query, coordinator, claim tests, and layering tests match `638293995`.

| Row | Result |
|---|---|
| `Antiphon.Tests` build | 0 errors, 2091 warnings. Lease `cfa03b25-f0de-40cf-b7ff-fb21c627aaac`. |
| Generator build | 0 errors. Lease `9e74d053-189c-4e21-93c9-d50509556adb`. |
| Named guard union | 90 executed, 90 passed. `VIOLATIONS 0 LISTED 162`. Lease `efffa3c3-a4ca-4de7-a402-6b866f75e43c`. |
| `RuntimeActionClaimQueryTests` | 3 executed, 3 passed. Same planner costs. Lease `701a7f84-5daf-4f79-aff8-bea5eb74d170`. |

Inherited confirmation, pure master `/tmp/c1886-master-base` at `f805ae444`, classes `SessionRuntimeActionRecoveryTests` and `SessionRuntimeActionRoutingTests` only. 59 executed, 51 passed, 8 failed. The brief's 11/3/8 figure is a narrower author slice. The full classes expand through `[Arguments]`. The eight failures are `C1435_owed_channel_reply_does_not_hold_next_message_delivery`, `C1826_mixed_source_machine_note_keeps_its_own_chat` for both bools, and `C1259_buffered_turns_route_their_own_recipients_before_flush` for `trailing-text`, `prompt-window`, `frontier-race`, `early-text`, and `channel-bound`. Debug task `1bf97852` owns them. They are not a defect of this repair.

## Land checks

- `scripts/check-evidence-diff.ps1` on `fe0cd5bc9..638293995`: commits=3 entries=0 violations=0.
- Same script on `d5c798092..638293995`: commits=10 entries=0 violations=0.
- `git merge-tree --write-tree origin/master HEAD`: exit 0, tree `55e21a579bdbc3a0ecdd9cc3821352e2d754bc49`. Claim-query, coordinator, claim tests, and layering tests have no diff against that merge tree.
- `scripts/impacted-classes.ps1 -Range fe0cd5bc9..638293995 -MergeSubset`: `migrations=False`. No migration, `AppDbContext`, or model file changed. Downgrade callers added: 0.
- No input-wait, lease, timeout, delivery, or OS branch in the repair. Native Windows proof stays owed and is not a pass.
- Positive controls stay pending, including PC-L-1 and PC-L-2. Operator 2026-11-12 waived mutation checks for dispatch. That waiver does not discharge these controls.
- The impact-index cap kept 25 coordinator classes and moved `ChannelOutboundRetentionTests` into `cappedPaths` owed when `RuntimeActionClaimQueryTests` was added. Head, merge tree, and cherry agree. The index tests passed. This is the generator cap. No advisory card.

Scratch trees left in place: `/tmp/c1886-master-repair`, `/tmp/c1886-layering-red` (claim-query file restored from `fe0cd5bc9`), `/tmp/c1886-master-base`.

## Review evidence

```
--- review evidence ---
subjectTaskId: 6a3c6a69-5ec6-44c8-8ad4-bebc4dc8a540
reviewedSourceSha: 6382939955d577630de802494698b794fe872a8b
reviewedSourceClean: false
ordinaryScopeCompleted: Full
reviewRound: 2
reviewedRange: fe0cd5bc905b5d7e477b4ede1c8147402c0195d2..6382939955d577630de802494698b794fe872a8b
```
