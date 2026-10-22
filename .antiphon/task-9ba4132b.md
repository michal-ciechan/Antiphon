# Task 9ba4132b — Final Review (Full) of CARD-1475 Code task 0fa7bfd0

Subject: feat/card-task-0fa7bfd0 at a13522f8b2352df5061c0d61fe47c5d03799d977 (base cb7af8ccd). Verdict: clean, land.

## Receipt (own run, server2, reviewed SHA)
Run 20261022-150721-0cbf, verdict GREEN exit=0, wall 9m54s, `unlisted: none`; scripts/validate-checkpoint-receipt.ps1 -Rows CP-2,CP-3,CP-4: `CHECKPOINT SOURCE VALID source=a13522f8b... rows=3`.

CHECKPOINT CP-4 commit=a13522f8b2352df5061c0d61fe47c5d03799d977 build=n/a filter=pwsh -NoProfile -File scripts/test-client.ps1 src/features/agents/SmartComposer.test.tsx executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a exit=0 slot=granted waited=210s dirty=0 source=a13522f8b2352df5061c0d61fe47c5d03799d977 sourceState=clean buildSource=notApplicable
CHECKPOINT CP-2 commit=a13522f8b2352df5061c0d61fe47c5d03799d977 build=ok filter=/*/Antiphon.Tests.Application/DeliveryUnverifiedDiagnosticTests/* executed=3 passed=3 failed=0 skipped=0 trx=/work/worktrees/task-9ba4132b/.antiphon/checkpoints/20261022-150721-0cbf/rows/CP-2/run.trx slot=granted waited=165s dirty=0 source=a13522f8b2352df5061c0d61fe47c5d03799d977 sourceState=clean buildSource=verified
CHECKPOINT CP-3 commit=a13522f8b2352df5061c0d61fe47c5d03799d977 build=reused filter=/*/Antiphon.Tests.Application/SessionMessageQueueDeliveryVerificationTests/Mode_Now_* executed=6 passed=6 failed=0 skipped=0 trx=/work/worktrees/task-9ba4132b/.antiphon/checkpoints/20261022-150721-0cbf/rows/CP-3/run.trx slot=granted waited=120s dirty=0 source=a13522f8b2352df5061c0d61fe47c5d03799d977 sourceState=clean buildSource=verified

CP-4 console: `Test Files 1 passed (1)`, `Tests 9 passed (9)`, `CLIENT TESTS EXIT CODE: 0 (PASS)`. CP-1 (red row) not re-run per brief.

## Checks
1. Wording is true. The only `DeliveryConfirmedBy.Screen` producer is `WaitForTranscriptConfirmAsync` (SessionMessageQueueService.cs:4158), reached only when `!observable` (baseline = zero `TranscriptEntries` rows, captured before the body write), the full `TranscriptConfirmTimeoutSeconds` deadline has passed (the shortened first-input deadline applies only to fullInline, which returns NoTranscriptRecord instead), no identity match was found (an incomplete identity returns Truncated earlier), and `sawPositiveSubmit` is set. `entersSent` is the loop's real count. `RecordDeliveryUnverifiedAsync` has that single call site, so the receipt reason and incident never appear on any other path. Gotcha #50: the unobservable branch polls `TryFindUnobservableConfirmingRecordAsync` from the attempt-time floor and calls `CatchUpTranscriptAsync` every `pullEvery`; `VerifiedPromptSubmitter` (src/Antiphon.Agents.Pty) confirms only by output-mark advance, and the boot transcript check is a separate after-the-fact read in AgentSessionService (BootConfirmClockTolerance). No overclaim: "proves" applies only to a later matching UserPrompt. No ASCII-only script touched.
2. No behaviour change. Server diff: two string literals, plus an added `int entersSent` parameter used only in the message. Severity, dedup constant/query, kind, event, return value, timeouts and receipt structure are byte-identical. Nothing parses the old text: `git grep` for the old strings finds only SessionTranscriptPanel/AgentRail/HomePage (binding state) and an AgentTaskDispatcher stall message, all unrelated.
3. Red record and guard adequacy. CP-1 run 20261022-144251-89e0: the TRX has V-1 Failed (assertion `should not contain "no transcript bound"`) and V-2/V-3 Passed. Mutation in a discarded scratch worktree (DeliveryUnverified severity forced to Warning at :3431, parking-incident severity untouched): V-3 alone gave `failed: 1`, `ShouldAssertException: incidents[0].Severity should be AlertSeverity.Critical but was AlertSeverity.Warning`. The dedup count assertion `incidents.Count.ShouldBe(1)` precedes it.
4. Pins. The only label pin is SmartComposer.test.tsx (updated). No e2e or other Vitest file mentions the old label. The receipt pin in SessionMessageQueueDeliveryVerificationTests is updated.
5. Deviation. CP-1 report.md/json, failures.md, rows/CP-1/run.trx and console.log are present; only `builds/bin-c1475-red` is gone. The Code receipt 20261022-145244-a852 also validates at a13522f8b (rows=3).
6. rc. Scratch worktree at origin/rc/2026-10-20 6e79f3e13: `22d057843` (with the plan doc dropped) and `bbae064cf` both cherry-picked with no conflict. SmartComposer.tsx and SmartComposer.test.tsx are identical between base and rc. The rc call site has `entersSent` in scope (:4033/:4156). The worktree was discarded.

Land: `git merge-tree --write-tree origin/master(158b25606) a13522f8b` is clean. `check-evidence-diff.ps1` over cb7af8ccd..a13522f8b: commits=3 entries=1 violations=0.

## Disclosure (no defect)
- SessionTranscriptPanel's unbound banner still says "nothing can be read back". It describes a live unbound session, not a delivery verdict, and is out of scope by plan.
- PC-1..PC-4 remain pending for SourceLanding Mutation. The severity mutation above is a review spot-check and does not discharge PC-3.
