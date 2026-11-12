# Why missed: group-97 C0824 ref-only cleanup temp-source refusal

This note covers one failure in the rc/2026-11-17 run `ae84478d`, chunk `antiphon-all-group-97`: `C0824RefOnlyCleanupTests.All_desktop_consumers_skip_ref_only_without_deleting_refs`. The full TRX extract is `docs/investigations/2026-11-10-run-ae84478d-g97-c0824-ref-only-cleanup-red-windows-failure.md`. The test failed in 5.18 s with `InvalidOperationException: c1017_checkout_under_temp: C:\Users\lndco\AppData\Local\Temp\c1017\<nonce> is under C:\Users\lndco\AppData\Local\Temp\`.

Classification: a deterministic semantic conflict between two test-only changes, with no textual conflict. The CARD-1230 fixture guard refuses any `sourceRoot` under the OS temp directory. The C0824 R-5 arm deliberately passes a temp `sourceRoot` together with a persistent `reportRoot`. The guard's stated cause (the report root) does not hold when `reportRoot` is explicit. Product code is unchanged. This is not the group-56 slow-Windows-git timeout family.

## Introducing commit/card

introducing commit/card: `d912ed6a043ba6a854c588270249f62b905c586e` (CARD-1230)

This commit was authored 2026-10-15 on `feat/card-task-5a3d03f6` (original `61d6609b8`) and applied to master at 2026-11-11 03:32Z. It is an ancestor of the rc cut `3db1a43b02246706dc28075fce7e7b5e84804ddb`. The colliding caller arm is `c8e61a69d` (CARD-0824 S5b-2, 2026-11-06), which was already on `rc/2026-11-15` `ac6d00022`. There the same test passed in run 6591e378 (group 96, 33.81 s).

## Approving reviews

approving review(s) with scope (Full/Interim) and the selection they actually ran:

- CARD-1230 review of tip `4bf2d2e6ed385132b442d445afb67fc3d9256c80` (`feat/card-task-5a3d03f6`), scope Final/Full clean=true at the tip. This is per `docs/investigations/2026-11-11-unlanded-landing-queue.md` from `2b4069534`, which ranked it the only LAND-NOW subject. The plan's checkpoints (`docs/superpowers/plans/2026-10-15-card-1230-c1017-temp-checkout.md`) are:
  - CP-1: `CardDoneWorktreeCleanupTests/(C1230_TempCheckoutIsRefusedByName*)|(C1017_TerminalTaskRequired*)`
  - CP-2: `(CardDoneWorktreeCleanupTests*)|(TaskWorktreeRetirementTests*)|(WorktreeRetirementRaceTests*)`
  - CP-3 "fixture-consumers": `(CardWorktreeCleanupRecoveryTests*)|(CardWorktreeMirrorCleanupTests*)|(CompletedCardWorktreeRemovalTests*)`
- The 2026-11-11 landing batch: the LAND-NOW decision rested on "merge-tree clean" plus the old at-tip review. It used final-tree checks only (land graph, evidence diff, one compile, conflict-hunk tests). No test selection was re-derived against current master.

## Outside the selection

why the failing test was outside that selection (lane/category/path): Integration, Slow, `tests/Antiphon.Tests/Application/C0824RefOnlyCleanupTests.cs`.

CP-3 was a census of `CompletedCardCleanupFixture` consumers taken on 2026-10-15. At that time no test passed `sourceRoot:`. The only caller that does (`C0824RefOnlyCleanupTests.cs:260`) arrived three weeks later, on 2026-11-06. The branch was landed as-reviewed. Merge-tree was clean because the two changes touch different files, and conflict-hunk tests only run code near textual conflicts. So no pre-land run executed the new fixture contract against its newest consumer. The failure is not Windows-specific: on Linux `Path.GetTempPath()` is `/tmp/`, and the R-5 root is `/tmp/c1017/<nonce>`, so it would have failed on server2 too.

## Cheapest pre-land check

the cheapest pre-land check as a path-glob -> class-filter entry for the impacted-classes table of CARD-1782:

`tests/Antiphon.Tests/TestHelpers/CompletedCardCleanupFixture.cs` -> `/*/Antiphon.Tests.Application/(C0824RefOnlyCleanupTests*)|(CardDoneWorktreeCleanupTests*)|(CardWorktreeCleanupRecoveryTests*)/*`

The general lesson: a stale-review LAND-NOW of a shared test fixture should re-take the consumer census (`grep -rln CompletedCardCleanupFixture.CreateAsync tests/`) against the target tip. It should then run every consumer class, not only the classes the original plan named. This note does not edit `tests/impacted-classes.json`.

## Exemption

same cause as an already written note: none

`docs/investigations/2026-11-10-why-missed-group-56-c0824-ref-only-receipt.md` is a different cause: a class timeout budget under Windows git load in `C0824RefOnlyReceiptTests`.
