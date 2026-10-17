# CARD-1290: verification-incomplete next stage, and a non-ancestor evidence base

Date: 2026-10-17. Investigate task `45d4537b`. Branch `feat/card-task-45d4537b`.
Card `30fcf6ff-c850-4d03-85bf-a3593b750f01`, Backlog, label `rc-2026-10-15`.
Train: land on `rc/2026-10-15`, then merge to master.

Tips read for the already-fixed check:

| Ref | SHA | Contains the defect |
|---|---|---|
| `origin/rc/2026-10-15` and this branch before this note | `69978ec1a246cd7a860f88a364eafc53a416aff7` | yes |
| `origin/master` | `aa3f12f1d7925f0f0df91971f00c86e6548dd64a` | yes, same sentences |

`git log origin/rc/2026-10-15 --grep=CARD-1290` and the same query on `origin/master` are empty. `scripts/check-evidence-diff.ps1`, `scripts/lib/evidence-policy.ps1`, and `server/Bundles/stage-review.md` are identical on the two tips. The D-2 sentences in `docs/orchestration-loop.md` match; master carries four extra rollout lines earlier in the file, so its copies sit at 2245 and 2253.

## Outcome

Both defects are still open. Neither is a product-behavior bug in the handoff parser. One is two instructions that name different next stages for the same Review finding. The other is `check-evidence-diff.ps1` treating "not an ancestor" as an opaque Git failure.

## 1. Next stage for an ungated or unnamed-broad run

### How the two sentences got apart

`b707e7387e93737ba2ccd289e2358ef1f98d3367` (`feat(card-1281): separate advisory follow-ups from verification gates`) put the classification in `stage-review.md`. Its SCOPE line said missing rows, zero counts, unjustified unlisted/ungated runs, or unprovable builds "leave verification incomplete". Its next line sent code only "for substantive repairs or exact missing execution" and said incomplete verification never authorizes land.

`3931a31149acbc63837fcdda13cad60fdd1117cb` (`fix(review): restore pinned instructions alongside D-2 policy`) put the pinned base text back. SCOPE again says `Defects: missing row, zero count, unlisted build/test run without a reason, build or test driver outside the slot gate, broad run without named invariant/cost, adequacy`. The next line again says `code when there are defects`. The same commit also left the D-2 classification sentence in the bundle.

`8ffe3633357e6383b7e408bd078465acf9f5fd79` (`fix: restore stage bundle caps while preserving D-2 policy`) deleted that classification from `stage-review.md` and copied it into `docs/orchestration-loop.md`. The restored next line stayed in the bundle. Review still has to apply both: `server/Bundles/delegate-basics.md:84` points Code and Review at `docs/orchestration-loop.md`, section `D-2 advisory policy`.

### What a reviewer reads now

`server/Bundles/stage-review.md:2` lists an unlisted run, a run outside the slot gate, and a broad run without a named invariant as defects. Line 15 then says:

`next: land when there are no defects and this was a Final Review; review (Final) when a clean Interim; code when there are defects (name them in handoff:); decide when a human choice blocks.`

`docs/orchestration-loop.md:2241` (master `:2245`) names those same runs as verification incomplete, and names missing acceptance, lost assertions, failed tests, and inadequate tests as substantive:

`Missing required rows, zero counts, unjustified unlisted/ungated runs, broad runs without named invariant/cost or unprovable builds leave verification incomplete.`

`docs/orchestration-loop.md:2249` (master `:2253`) is the other routing rule:

`Substantive repairs or exact missing execution require code; incomplete verification never authorizes land.`

An ungated run or a broad run without a named invariant is in the incomplete list. It is not in the substantive list. "Exact missing execution" is the absent-selection case at line 2245 (`If required execution is absent, request only the exact missing selection once`), so a run that happened and is inadmissible is not named there either. Land is forbidden. The next token is not.

`PipelineHandoff.TryParse` (`server/Application/Services/PipelineHandoff.cs:154-164`) stores whatever `next:` token the report emits. Its alias map (`:24-44`) accepts `code`, `review`, `land`, and `decide` with equal status. `PipelineDefinitions.cs:18` allows Review the same four. Nothing in that path reads the finding class. The header comment at `PipelineHandoff.cs:7-9` says a missing block still settles.

`tests/Antiphon.Tests/Application/TaskPlatformGuidanceTests.cs:136` pins the restored next line verbatim, and `:97` pins `code when there are defects`. `D2AdvisoryDocumentationTests` pins `incomplete verification never authorizes land` and does not require the incomplete class to name `code`.

### Smallest fix

Add one sentence to the D-2 next-stage classification, and leave `stage-review.md:15` unchanged so the pin stays green. The sentence that defines the class once:

`An ungated run, a broad run without a named invariant, a missing row, a zero count, or an unprovable build is verification incomplete and is exact missing execution: its next stage is code for that selection once, never land and never another Review only to relabel it.`

That matches the bundle, which already sends those defects to code, and the existing D-2 clause that sends exact missing execution to code. It keeps land blocked. It does not reopen the bundle-cap edit.

Red-first witness, one method, in `D2AdvisoryDocumentationTests`: assert the resolved D-2 section contains that sentence. The phrase is absent today, so the test is red before the sentence lands. Filter:

`/*/*/D2AdvisoryDocumentationTests/VerificationIncompleteRoutesToCodeOnce`

## 2. `check-evidence-diff.ps1` and a base that is not an ancestor

### Mechanism

`scripts/check-evidence-diff.ps1:7` calls `Invoke-EvidenceHistory`. That resolves both refs, then calls `Assert-EvidenceAncestor` (`scripts/lib/evidence-policy.ps1:199-201`). The helper (`:70-73`) runs `git merge-base --is-ancestor` through `Read-EvidenceGit`. Any non-zero exit throws `git_result_` plus the first argument (`:40-41`), so exit 1 becomes `git_result_merge-base`. The catch at `:219` prints `EVIDENCE error reason=...` and returns 2. Git's own "not an ancestor" distinction is discarded.

The event path does not do this. For a non-default-branch push it computes `git merge-base` of `origin/<default>` and the head (`:183-186`). The direct `-BaseRef` path never does.

`docs/testing-and-build.md:324-325` tells Code and Review to pass the recorded task-base SHA. That SHA stays an ancestor when master only moves forward. The failure is passing the moved master ref (or any other ref that is not an ancestor of the head).

`Assert-EvidenceAncestor` is also the deletion guard at `:306`, `:320`, and `:321`. A clearer reason there is the same condition.

### Reproduced

Temporary repository, script from this checkout, then the repository was removed. `master` moved one commit ahead of the feature fork.

| Ref | SHA |
|---|---|
| fork `M0` / merge-base | `650f945bcb1807cae145749cacdf6cfde27a3493` |
| feature `F1` | `665ba1841a9d482e83d9b01e0623265930d9368c` |
| moved `master` `M1` | `f29c73c9a24d2f6b9d61551852400aa792d53e9e` |

`git merge-base --is-ancestor master feature` exited 1. `git merge-base --is-ancestor M0 feature` exited 0. `git rev-list master..feature` and `git rev-list M0..feature` both printed only `F1`.

| Invocation | Exit | Output |
|---|---:|---|
| `-BaseRef master -HeadRef feature` | 2 | `EVIDENCE error reason=git_result_merge-base` |
| `-BaseRef 650f945bcb1807cae145749cacdf6cfde27a3493 -HeadRef feature` | 0 | `EVIDENCE result commits=1 entries=0 violations=0` |

The recorded base works. The moved master ref fails before `rev-list`, even though `master..feature` is the same commit set.

`EvidenceDiffGuardTests.Refuses_unverifiable_ranges_and_objects` (`tests/Antiphon.Tests/Scripts/EvidenceDiffGuardTests.cs:240-241`) expects an unrelated head to exit 2 and to omit `EVIDENCE result`. The hook at `:259` forces `merge-base` exit 1 and expects exit 2 (`:279`). Neither assertion pins the string `git_result_merge-base`.

### Smallest fix

In `Assert-EvidenceAncestor`, call `Get-EvidenceGitResult` directly. Exit 1 throws `base_not_ancestor` and includes the two resolved ids. Any other non-zero exit stays `git_result_merge-base`. Do not replace the stated base with `git merge-base`. That is what the event path does, and it would audit a different range from the ref the caller named. The unrelated-history refusal stays exit 2.

Red-first witness, one method, next to the existing refusal test: build the moved-master shape through `EvidenceGitFixture` and expect exit 2, `base_not_ancestor`, and no `EVIDENCE result`. Today the reason is `git_result_merge-base`, so the new assertion is red. The class is already `Integration` and `Slow`; run the method, not the class. Filter:

`/*/*/EvidenceDiffGuardTests/Names_a_moved_master_base_that_is_not_an_ancestor`

## Remaining uncertainties

The card attributes the finding to Review `b6802c2e`. That report is not in this checkout. The landed text and the two commits above are the mechanism.

Review's SCOPE also tells it to re-run the claimed checks before it settles. The sentence above is the next stage when that Review settles and the run is still incomplete. It does not stop the same Review from running the exact selection itself first.

`adequacy` in the bundle and `inadequate tests` in D-2 are already the substantive bucket. This card's hole is the incomplete bucket.

## Not done, noted

No bundle, policy, or script edit in this task. The two sentences above are the proposed fix.
