# CARD-1733 Review round 1

Verdict: not clean. Tip `803291f3cd4ca008afabbcc65d0cb56fd392546d` is Debug task `8f43dbed-e7b8-4d44-b8e3-0252c374f651`'s diagnosis. It does not fix the reservation-matrix timeouts. The fixture repair is already on `origin/master`.

## Subject

- Unique commit: `803291f3cd4ca008afabbcc65d0cb56fd392546d` `docs: diagnose allocator reservation matrix timeouts`
- Parent and merge-base with the pre-fetch master: `1a22e0d2ca7b32e7f6ac346a658e1c7824252a46`
- Only path: `docs/investigations/2026-11-06-debug-card-1733-allocator-timeouts.md` (327 lines)
- `git cherry -v origin/master` at review start: that one commit, nothing else
- No `### Checkpoints` table on the tip. No product, test, script, or allowlist change.

## Does this commit fix the cause?

No. `ReleaseTagAllocatorTests` is still Integration-only, with `ParallelLimiter<ProcessSpawnLimit>` and no Slow allowlist row. `Assert-C1339ContainedPath` in `scripts/fixtures/c1339-release-git.ps1` still walks ancestors with `Test-Path`, `Get-Item`, and `Split-Path`. The ScriptHarness execution budget is still 300 seconds. The class file is unchanged since the split at `4f9eb9f3aa097ae8e78758d57ed69b591a019ff1`. The investigation says the same thing and hands the repair to Code.

Source checks that match the diagnosis:

- `Invoke-C1339PromotionReservations` applies 27 journal edits across G-43..G-52.
- `Test-C1339_PromoteReservationAndTarget` builds 11 promotion fixtures and 18 promotion attempts, and it does not call `Save-C599ASnapshot`.
- Stored `policyHash` is `0ac7b7c0dfcf6949ef1714fcfa02d4acd58e6258a8556209248338408f8f46a2`.
- `Scratch_git_deadline_includes_inherited_output_drains` has `[NativeLinuxOnly]` and no policy row at this SHA.

The diagnosis does not widen the 300-second cap from censored samples. That matches the performance rule: a censored 300-second sample is not a p95, and 2x of that sample would be an invented budget. There is no completed post-repair duration in this commit.

## Fresh execution at the reviewed SHA

Build, one lease, native output, `UseAppHost=false`, `TEMP=/tmp`:

- lease `92b232b1-a54a-4385-ab22-40387d905626`, waited 0s, held 195s
- 1729 warnings, 0 errors, elapsed 00:03:12.78

`/*/*/ReleaseTagAllocatorTests/*`, lease `964a96af-a47c-4da9-baa6-996a6aa1b7ff`, waited 0s, held 2055s. Exit 2. 7 executed, 1 passed, 6 failed, 0 skipped. Duration 34m 09s. Every failure is the unchanged 300-second ScriptHarness budget.

| Method | Outcome | Duration |
|---|---|---|
| `C1339_PromoteResumedReservations` | failed | 00:05:00.152 |
| `C1339_RemoteTagAllocation` | failed | 00:05:00.139 |
| `C1339_PromoteFreshReservations` | failed | 00:05:00.070 |
| `C1339_PromoteAuthorityHash` | failed | 00:05:00.048 |
| `C1339_ExactRemoteRefs` | failed | 00:05:00.136 |
| `C1339_AllocationUtcDay` | passed | 00:04:03.267 |
| `C1339_PromoteReservationAndTarget` | failed | 00:05:00.341 |

Target printed G-54 through G-60 before the cap, which matches the diagnosis. Fresh printed G-43 and G-44, then died inside the invalid-edit group. The two-method debug sample did not credit the other five methods. This full-class run shows four of those five also hit the cap: Resumed, AuthorityHash, ExactRemoteRefs, and RemoteTagAllocation. `C1339_AllocationUtcDay` passed in 243 seconds, close to the same cap.

Guard union present at this SHA, lease `329b1005-c94a-4a3e-9ad2-082a865615f0`, waited 151s, held 109s. Exit 2. 67 executed, 65 passed, 2 failed, 0 skipped, 1m 47s. The two failures are `Policy_rows_require_pinned_attribute` and `Markers_do_not_expand_policy_exclusions`, both `reverse-coverage` for `C0824TransportGitTests.Scratch_git_deadline_includes_inherited_output_drains`. That is the diagnosis's 66/64/2 table plus the one `TestLaneCategoryGuardTests` pass. `C1584_E2EExpandedBudget` passed 1/1 in 27s (lease `93d681b5-dd78-4031-81a6-5b03f3fa5b4f`, waited 0s, exit 0).

`DerivedImpactIndexTests` and `C1729LayeringGuardTests` are not in this tree. `scripts/impacted-classes.ps1` is not in this tree, so there is no downgrade-selector count. The diff is one Markdown file.

`scripts/check-evidence-diff.ps1` over `1a22e0d2..803291f3`: commits=1, entries=0, violations=0, exit 0. `git merge-tree --write-tree origin/master HEAD` exit 0, tree `7ff5c32c6385717547da329e7c91df407be7ed7a`. Fetched `origin/master` is `3fd231a2e498e463443079c40cf38711c0be8504`. The brief's master `bc71a95e7710532a9a68416f1d7b9e158fe96a15` is an ancestor of that tip. No checkpoint receipt exists, so `reviewedSourceClean` is false.

## Repair already on master

`6799d85c0d0f2a877c170b982b883110cff668bf` is on `origin/master` (cherry-pick of `142a1acd857c23f59374d02064a1fb60104a910a`). It changes containment to `File.GetAttributes` and `Path.GetDirectoryName`, adds private fixture clones, marks `ReleaseTagAllocatorTests` Slow and Integration, and allowlists it with a CARD-1733 reason. Code `7e4799eb-145c-42e5-815a-265434ba5b7e` failed. Code `d463ea3b-75bc-4f87-bd7c-08c0cae8325d` succeeded. Merge `7cb546f2-94f8-47e8-8af2-4e421ba4b0af` succeeded. The diagnosis file itself is not on master.

On current master the `Scratch_git_deadline_includes_inherited_output_drains` name is in `tests/test-execution-policy.json`. Do not open another card for the reverse-coverage failure reproduced on this old tree.

Positive controls stay pending. This review applied no mutants. The landed plan's PC-1..PC-6 are not discharged.

## Defect

Where: `803291f3cd4ca008afabbcc65d0cb56fd392546d`, only the investigation Markdown.

Failure: the card's repair is absent. The fresh full class is 6 failed / 1 passed, all six failures at the unchanged 300-second cap. There is no shared seed, no native containment, no matrix split, and no Slow classification in the diff.

Why: the commit is the Debug measurement. It states that no product or test source changed.

Fix: do not re-implement that repair on this base. It is already on `origin/master` at `6799d85c0d0f2a877c170b982b883110cff668bf`. Do not land `803291f3` as the fix. Publishing the historical diagnosis is a separate decision; this review has no clean checkpoint receipt and the required class is red, so evidence-backed land is not available.

## Advisories

- The two-method sample under-counts the class. Resumed, AuthorityHash, ExactRemoteRefs, and RemoteTagAllocation also stop at 300 seconds here. The diagnosis said those methods were not credited.
- `C1339_AllocationUtcDay` passes in 243 seconds, inside the cap with little margin. No budget was changed.
- The in-document `next: code` handoff is historical. The successor Code and Merge tasks already settled.
