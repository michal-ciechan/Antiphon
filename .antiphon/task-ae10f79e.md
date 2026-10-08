# Final Review S2-S4 (CARD-1134, CARD-1126, CARD-1152)

Clean. Code owner `d5c9ffe8-44b8-49d2-9f1b-4e6f0e1a6c39`. Reviewed source `e3ed9c6f6a254fa12bf955a0556fe53a9972f7ed` (tested product/test commit `238114961c66acaa1a02ff93ade21c3d906c642a`). Base `4e372939c78aa42591994a15262e7b10fdf61ee6`. Branch was not rebased.

## Ordinary scope

Run `20261008-022935-4851`, `--rows CP-4` through `CP-41` (38 ids), `--serial`, `--total-timeout 180m`, `--expected-source-sha e3ed9c6f6a254fa12bf955a0556fe53a9972f7ed`. Tool adds `UseAppHost=false` off Windows. Wall 51m35s. Verdict GREEN exit 0. `unlisted: none`.

`validate --evidence .antiphon/checkpoints/20261008-022935-4851/report.json --expected-source-sha e3ed9c6f6a254fa12bf955a0556fe53a9972f7ed --rows CP-4..CP-41` printed `CHECKPOINT SOURCE VALID` for 38 rows.

38 rows, 69 executed, 69 passed, 0 failed, 0 skipped. Every row `dirty=0 sourceState=clean buildSource=verified slot=granted`, and each executed count equals the plan Expect. Builds actually compiled: `bin-hardening-s2` (121s), `bin-hardening-s3` (169s), `bin-hardening-s4` (172s), each clean and verified. The report's build count of 5 includes unused plan ids `bin-hardening-s1` and `bin-hardening-s5` (`state=unused`); they were not compiled and no S1/S5 row ran.

Executed: CP-4 4, CP-5 5, CP-6 4, CP-7 4, CP-8 2, CP-9 1, CP-10 1, CP-11 1, CP-12 1, CP-13 4, CP-14 5, CP-15 1, CP-16 1, CP-17 1, CP-18 1, CP-19 3, CP-20 1, CP-21 2, CP-22 1, CP-23 1, CP-24 1, CP-25 1, CP-26 1, CP-27 1, CP-28 1, CP-29 1, CP-30 1, CP-31 2, CP-32 6, CP-33 1, CP-34 1, CP-35 2, CP-36 1, CP-37 1, CP-38 1, CP-39 1, CP-40 1, CP-41 1.

## Diff checks

`4e372939c..e3ed9c6f` is 4 commits and 10 files, all under `tests/Antiphon.Tests/Application/` or `docs/superpowers/plans/`. No production, script, or migration path. `check-evidence-diff.ps1` over that range: 4 commits, 0 violations.

Method counts are unchanged. Notification partials stay 27 `[Test]` methods (11 + 13 + 3). Phone-home stays 7 + 19 Drain + 4 Retire = 30. The three renames are the only method-name changes. `DispatcherSweepLifetimeRegistrationTests` still has both methods. The CARD-1125/1128 roster sentence matches this SHA.

No `Should*` assertion was removed or loosened. The only added assertion is `intents.ShouldAllBe(i => i.CreatedAt == finalDispatch.At)` in `C508_VanishedDefaultStillCapturesWarning`. The two clock advances now use `new GitSettings().WorktreeBaseInspectionTimeoutSeconds` plus one tick. The two Pending `RemoteSyncEvidence` objects gained `Reason: LeaseBusy`. The Unavailable fixture already had that reason and was not edited.

Old names `C508_ClaimCapturesWarningIntents`, `C508_ClaimIntentAtomic`, and `C508_IntentCaptureRoute` remain only as historical mentions in investigations and in plans that name the rename. No C# method still uses them. XML on the renamed methods already describes kept-sibling behavior.

## Mutations (scratch, reverted)

Scratch worktree `/tmp/review-ae10-scratch` at `e3ed9c6f`, removed after the probes. Main tree stayed clean for the checkpoint run.

- Claimed PC-4: `DispatchBaseWarningIntentService.cs:66` `CreatedAt = payload.CreatedAt.AddSeconds(1)`. `C508_VanishedDefaultStillCapturesWarning` executed 1, failed 1, at `i.CreatedAt == finalDispatch.At` (`Coverage.cs:382`). Count and dispatch-id checks are earlier, so this is the timestamp invariant. Production sets `createdAt` from `dispatchEvent.At` (`DispatchBaseWarningIntentService.cs:32`) and persists `CreatedAt = payload.CreatedAt` (`:66`). The assertion compares the stored intent with the stored dispatch event. It is not a tautology.
- Claimed coupling: `GitSettings.WorktreeBaseInspectionTimeoutSeconds` default 5 changed to 9, and only the script fake reverted to `TimeSpan.FromSeconds(5)`. `T0442_V15_initial_post_prints_the_service_base_preview` executed 4, passed 3, failed 1: `unknown_fallback` lacked `inspection_timeout`. A copied 5s advance does not cross the moved setting, so the derived advance tracks `GitSettings`.
- Own: guard fake advanced `TimeSpan.FromTicks(1)` instead of the derived budget. `T0442_V30_...` executed 5, passed 4, failed 1: argument `deadline` was `Dispatched` rather than `Blocked`. The other four arguments passed. A one-second shortfall (`setting - 1`) stayed green (5/5) because that fake advances on every matching rev-parse and the advances accumulate; that probe is not the red.

## Disclosure

F1 from the first Final Review stays a disclosure, not a verdict finding. Receipt tests insert queue and transcript rows directly. `DispatchBaseWarningDeliveryE2ETests` is not a checkpoint row of this plan. Backlog card CARD-1155 (`14fe65f2-1051-4192-8974-ec3ecc46dc9d`) asks a later roster to add those existing methods as named costed rows with a complete UserPrompt transcript. `card.ps1 new` saved the card, then exited 1 while mirroring a card file onto a Windows path that this runner does not have. `card.ps1 get CARD-1155` shows the Backlog text. CARD-0782 and CARD-0576 are different gaps.

## Merge

`git merge-tree --write-tree origin/master` (`0af45a8cba4b33d2f5c99756cd74ce0ac5e27b2f`) conflicts only as add/add on `docs/superpowers/plans/2026-10-08-test-hardening-batch-1137-1134-1130-1126-1152-plan.md`. `7ae4ea6b9` is an ancestor of `origin/master`. Informational. This branch was not rebased.

Catalogue read: runner-defaults revision 2, empty kind defaults. No host pin.

PC-1..PC-7 and FC-1/FC-2 stay pending for SourceLanding Mutation. AppHost restart: no.
