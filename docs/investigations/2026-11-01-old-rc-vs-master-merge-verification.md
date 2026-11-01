# Old rc/2026-10-27 vs master: test-merge containment verification

Date: 2026-11-01. Task 3fdf288c (Investigate, server2/Linux, git 2.47.3). Read-only on master and rc.

## Headline

**Nothing from rc/2026-10-27 is missing on master.** The `-X ours` merge tree differs from
master only in 6 `.antiphon/` rc-integration task records. Every rc-side line in the
122 conflict hunks across 37 files traces to an rc commit that has a patch-id-equal or
same-subject counterpart on master. Each such line was later edited, extended or deliberately
replaced by a named master-only commit. No class-R (real missing fix) hunk was found.

## Inputs

| Ref | SHA |
|---|---|
| origin/master | `87f4a868a86ce594d6dc9d1f1365315cb3f86df9` (tree `85283b399d72151d2fa1377dabc4f163bab3e14d`) |
| origin/rc/2026-10-27 | `5010e23f0b394ecd4a1ea70e47f7f0c868f24f15` |
| merge base | `a97194ee7a4adf215454fd82c453ba46362596ee` (2026-10-24, "docs(evidence): record CARD-1545 continuation checkpoint…") |

Counts since the base: master has 1739 commits (1731 non-merge with a diff). rc has 1201, all
non-merge. Patch-id comparison found 552 master-only and 22 rc-only commits (`git cherry`: 22 `+`).

## 1. Test merge reproduced

`git merge-tree --write-tree --name-only --merge-base=a97194ee7 origin/master origin/rc/2026-10-27`
exits 1 with tree `099d65e7464982473d6aa868f46b71372df5faa5` and **37 CONFLICT lines**. That
is the same list the orchestrator reported: 11 add/add and 26 content conflicts. The conflicted
blobs contain 122 `<<<<<<<` hunks.

## 2. Clean-apply check (`-X ours`)

`git merge-tree --write-tree -X ours --merge-base=a97194ee7 origin/master origin/rc/2026-10-27`
exits 0, tree `8090102cdef1b2713e1599f634dffc744220fa6e`.

```
$ git diff --stat origin/master 8090102cd
 .antiphon/task-11874f32.md | 132 +++++++++++++++
 .antiphon/task-53a3f119.md |  68 ++++++++
 .antiphon/task-7ba35e25.md | 397 +++++++++++++++++++++++++++++++++++++++++++++
 .antiphon/task-8331de83.md |  57 +++++++
 .antiphon/task-a54cc694.md | 194 ++++++++++++++++++++++
 .antiphon/task-bbfa2afa.md | 113 +++++++++++++
 6 files changed, 961 insertions(+)
```

**Nothing differs outside `.antiphon/`** (nothing in `docs/cards/` either). The six files are
added by the rc-only record commits 335b4fbb3, a13e544e2, 665bf12b1, 105792f7d, f3aa3b43e and
afdeebdc1 (rc integration/top-up mapping records). Every non-conflicting rc hunk is already
byte-present on master, including the outputs of rc-only commits 3cb215cf3, 9c271b7e1, 80b3a59c4,
125c720b9, b226c226b and the rc-only investigation docs (C514, report-budget, C1379).

## 3. The 22 rc-only commits (patch-id)

| rc commit | master counterpart | note |
|---|---|---|
| 3cb215cf3 fix: hold only the current Failed owner… | 909e99ad4 (same subject) | conflict-resolved variant; `FailedTaskSessionOwnership.cs` byte-equal on both |
| 9c271b7e1 fix: retain zero-row pool sessions… | c2ebb2a47 | `TaskSupervisionVisibilityTests.cs` byte-equal |
| 80b3a59c4 test: witness CARD-1610 mixed-cap… | 698bf1b73 | |
| 125c720b9 test(CARD-1574): expose regrowth… | 583a36b0b | |
| b226c226b test: observe committed C1379 replay… | cbe3e6b42 | |
| db8a211f2 / 1f3794102 fix: cover CARD-1619 … in rc policy | 67f80cd7d / 651eb516b | policy JSON below |
| 665bf12b1, f3aa3b43e, a13e544e2, afdeebdc1, 105792f7d, 335b4fbb3 | none | `.antiphon` rc-integration records: the 6 files above (T) |
| 59deddd30 + 97756db4e | none | add then delete `.antiphon/task-00ae50cf.md`, net zero |
| 2b7383b24 | none | plan-doc one-line delete, already the master state (merge clean) |
| f866c0f04, 193524ac9, b9575b5e6, f90ea8f9a, 64c7537aa, f0caf7762 | none | investigation docs; merge clean, so already on master |

## 4. Conflict check: method

For each conflicted file, `git merge-file --diff3` was run over the base, master and rc blobs.
In each hunk the check took every rc-side line that is new relative to the base and absent
anywhere in master's file (whitespace-trimmed). Each such line went through three steps:

1. `git blame --line-porcelain origin/rc/2026-10-27`, to find the rc commit that wrote it;
2. a patch-id lookup of that commit among the master commits since the base;
3. `git log -S` on master since the base, to find the master commit that last changed that
   line's count. That commit is the superseding commit.

A second pass looked for base lines that rc deleted while master still keeps them. It found
none in any hunk.

Result: 52 of 122 hunks have no rc-side line missing from master. They are E: both sides added
the same block, and master added more beside it. The remaining hunks' rc lines came from only
two rc-only commits, 3cb215cf3 and 1f3794102. Every other rc line came from rc commits with
patch-id-equal master commits, so master once held that exact line and a later master-only
commit changed it. Lines whose newest pickaxe hit was the introducing commit itself were
checked by hand: c590-real.ps1, c1008-host-proof.ps1, c1008-fake-docker.sh, c590-remote.sh
`local id=…` and RollingVolumeRecycleScriptTests `proofEnv`. Master holds each of them in an
extended form, such as added `-ProofVersion 2 -PhaseNonce`, `C1609_PROOF_*` envs,
`candidate=""` and `$phaseNonce` names.

## 5. Verdict table

Classes: E = both sides made the same change and master edited around it. S = master
supersedes it, with the commit named. A = add/add. T = record or generated file. R = missing.
None of the rows is R.

| # | File | Hunks | Class | rc source → master supersession (evidence) | Loss |
|---|---|---|---|---|---|
| 1 | .antiphon/task-af2b78c8.md | 1 | A/T | rc holds the in-progress draft (4b058e7b5, bf0a6f9e9, 1a0d60593 = master 3ddefa402, 255ecdab1, 5937e9b03); master's final report is 308bdd8be. Record only. | no |
| 2 | client/src/api/attention.ts | 1 | E | Both sides add the PendingNotesAged, PooledSessionWorkingPastTtl and FailedTaskSessionHeld union members at the same point. rc last touched it in ac650b8c6 (= master 0f9f7da4f). Master appends `'CeilingHold'` in 29d3baaf1 (CARD-1610 ceiling holds). rc's block is a prefix of master's. | no |
| 3 | client/src/features/attention/attentionVisuals.test.ts | 1 | E | same; master adds `'CeilingHold'` (641616d56) | no |
| 4 | client/src/features/attention/attentionVisuals.ts | 1 | E | same three entries on both sides; master adds the `CeilingHold` visual (641616d56) | no |
| 5 | docs/docker-stack.md | 2 | S | rc's C994/C1008 image-pinned retire-temp preview paragraphs come from 6634c1e54, 7bc0aa5ec, 95de364e3, 29218e553, 8ac9b627f and 68565db92 (= master ca5e51839, 60b261f31, ae0e71276, 4277f94cb, ce6aae63b, 86d21b50b). Master rewrote them in de1a52dd7 and the CARD-1609 set. CARD-1609 decision D-9 (9c8021277) makes retirement content-audited, not image-pinned. | no |
| 6 | docs/investigations/2026-10-25-card-1584-e2e-launch-bounds.md | 1 | A/T | rc lacks master's extra section from 317fb964f. Record. | no |
| 7 | docs/investigations/2026-10-30-card-1618-sf-aggregate-run-debug.md | 1 | A/T | rc holds the "in progress" opening (fbeef58c1 = b154687cd); master's finished record is 7ba9c8625. Record. | no |
| 8 | scripts/c1008-host-proof.ps1 | 4 | A/S | rc file is 29218e553 (= 4277f94cb); master extends it to the v2 proof in 9c8021277, 85776c1c8 and 7650549b0 (CARD-1609) | no |
| 9 | scripts/c590-real.ps1 | 9 | S | rc lines come from 29218e553 (= 4277f94cb). Master extends them with phase nonce, v2 proof and coordinator (7650549b0, 85776c1c8, de1a52dd7, d9dbe0d7c, CARD-1609). | no |
| 10 | scripts/c590-remote.sh | 22 | S | rc commits 95de364e3, 7bc0aa5ec, 7060e9505, fb740b61e, 8ac9b627f and 29218e553 are all patch-equal on master. Master superseded them in de1a52dd7, 3e6ccdbf2, 0bc71df36 and e6f7cb25a, and 9c8021277 replaced c994 image pinning with a content audit (CARD-1609 D-9). | no |
| 11 | scripts/fixtures/c1008-fake-docker.sh | 10 | S | rc commits 29218e553, 7bc0aa5ec and ccfb7f1ec are patch-equal on master. Master extends them in 3e6ccdbf2, 85776c1c8, dffb9aa73 and 678655489 (CARD-1609 fixture). | no |
| 12 | server/Application/Dtos/AgentTaskDtos.cs | 1 | S | rc `FailedSession = null);` (afadc6625 = 81168ada8); master adds CeilingHold and SupervisionNotices after it (29d3baaf1) | no |
| 13 | server/Application/Dtos/AttentionDtos.cs | 1 | E | Both sides add the enum members 61–63. Master adds `CeilingHold = 64` (29d3baaf1). | no |
| 14 | server/Application/Services/AgentSessionRuntime.cs | 1 | E | rc side has 0 lines; master's 32-line insertion (641616d56) sits next to a shared CARD-1619 edit (7e004ccc7 = 021c0d6c9) | no |
| 15 | server/Application/Services/AgentTaskDispatcher.cs | 1 | E | rc side has 0 lines (CARD-1619 e49607899 = 23c43e58d); master adds ceiling and notice work beside it (c12969fb8, a7a136563, 641616d56) | no |
| 16 | server/Application/Services/AgentTaskService.cs | 2 | S | Hunk 1 is the rc-only 3cb215cf3 `Describe(...)` call. Master's 909e99ad4 is the same change, and 2bf104be4 (CARD-1610) adds a notice-state argument. Hunk 2 is rc `FailedSession: failedSession);` (ac650b8c6 = 0f9f7da4f); master adds CeilingHold and SupervisionNotices (29d3baaf1, 2bf104be4). | no |
| 17 | server/Application/Services/AttentionService.Seats.cs | 3 | S | rc-only 3cb215cf3 = master 909e99ad4. Master's version is rc's plus the FailureDelegate notice lookup and `notice={facts.NoticeState ?? "none"}` (2bf104be4). | no |
| 18 | server/Application/Services/AttentionService.cs | 1 | E | Both sides add `BuildPendingNotesAgedItemsAsync`; master adds `BuildSupervisionNoticeItemsAsync` above it (2bf104be4) | no |
| 19 | server/Application/Services/CapacityRecoveryService.cs | 7 | S | rc `RedeemOnAsync` consume (480280efc = 9524068f4); master extends it with the Admitted receipt, revalidation grant and stamps (29c9f1a6f, 476a1a266, 3a4981571, c49db812c) | no |
| 20 | server/Application/Services/FailedTaskSessionFacts.cs | 3 | A/S | rc `NoticeState: null` placeholder ("until the notice ledger exists"), from ac650b8c6 = 0f9f7da4f. Master fills NoticeState from the ledger in 2bf104be4 (CARD-1610). | no |
| 21 | server/Application/Services/LandVerificationBuildGraph.evaluation.txt | 1 | T | Generated digest. rc E7C6EDAF… (b37178b6b = 78fee7464); master's newer CARD-1545 digest is 4c0cdfe3a. | no |
| 22 | server/Application/Services/LandVerificationBuildGraph.manifest.txt | 1 | T | same | no |
| 23 | server/Application/Services/SessionMessageQueueService.cs | 1 | E | rc side has 0 lines; master's CARD-1610 notice additions sit next to it | no |
| 24 | server/Application/Services/TaskDeadlinePolicy.cs | 1 | E | rc inline `activityAt` / `Smaller(NonNegative(now - activityAt), elapsed)` (0829df2f9 = 2b426f550). Master moved the same formula into `ModelWaitElapsed` (9a6122318). | no |
| 25 | server/Migrations/20261030141651_AddSessionExitSignalEvidence.Designer.cs | 7 | A/T | rc→master diff is **185 insertions, 0 deletions** (not 127): the CARD-1610 TaskSupervisionNotice entity and AgentTask ceiling columns, re-cut in 00ee6f4c3 and 20f0801bd. The migration `.cs` files are byte-equal. | no |
| 26 | src/Antiphon.PtyHost.Client/PtyHostClient.cs | 1 | S | rc retries on `InvalidOperationException or EndOfStreamException` (1ac613aa5 = b136a6fbc). Master narrows the retry to a missing hello reply with `HelloEofException`, thrown on hello EOF (1186580e2). | no |
| 27 | tests/Antiphon.SessionRunner.Tests/Card1619LinuxStopIntentTests.cs | 3 | A/S | rc wall-clock `Stopwatch` < 250 ms (dd80f4317 = 394d206c0); master asserts < 100 ms on the store clock (e8610de8b) | no |
| 28 | tests/Antiphon.SessionRunner.Tests/Card1619RunnerExitTests.cs | 1 | A/E | every rc line is on master; master adds 157 lines (dd8b72512) | no |
| 29 | tests/Antiphon.Tests/Agents/ClaudeDialogCanaryDiagnostics.cs | 2 | A/S | rc local `Redact` (8b13b9ffe = fba1afba3); master routes all sinks through `LiveClaudeEvidence` (3aaf87550, CARD-1545 sanitizer) | no |
| 30 | tests/Antiphon.Tests/Agents/ClaudeDialogWatchdogCanaryTests.cs | 3 | S | rc `ANTIPHON_C680_DEBUG_MODEL` env model and inline launch (8b13b9ffe, 8fcb44c3f = fba1afba3, e903c3137). Master uses an owned profile model and `CreateProfileLaunch` (7494f8537, d17040c0a, 3aaf87550, CARD-1545). | no |
| 31 | tests/Antiphon.Tests/Application/CapacityLockOrderTests.cs | 2 | A/E | every rc line is on master; master adds supervisor-consume witnesses (a571bf711, daca8a6ad) | no |
| 32 | tests/Antiphon.Tests/Scripts/RetiredTempContainerHostTests.cs | 1 | S | rc `c994-image-binding` assertion (8ac9b627f = ce6aae63b). C994 image binding was retired on master (c80f5de16, de1a52dd7, 9c8021277; CARD-1609). | no |
| 33 | tests/Antiphon.Tests/Scripts/RetiredTempContainerScriptTests.cs | 1 | S | rc copies `c1008-host-proof.ps1`; master copies it and `c1609-host-proof.ps1` (0094b7867) | no |
| 34 | tests/Antiphon.Tests/Scripts/RollingVolumeRecycleScriptTests.cs | 19 | S | rc preview and private-origin proof tests: f50c8f545, 8cd467832, 96a4c34dd, ccfb7f1ec, 32f358b40 and 5451b3dd4, all patch-equal on master. Master rewrote them for v2 proofs and content retirement (c4e76c8d3, 9c8021277, 3e6ccdbf2, a9ed39fa0, e52c3b889; CARD-1609). | no |
| 35 | tests/Antiphon.Tests/TestHelpers/OverdueSweepHarness.cs | 1 | S | rc adds `runner` as the last parameter (45f907bc7 = 808e1be10); master adds `connectionString` after it (0c5039329) | no |
| 36 | tests/Shared/NativeLinuxWindowsBaseline.json | 2 | A/T/S | Roster and Reasons have 256 rows on rc and 254 on master. Placements (45) and Problems (0) are equal. The two rc-only rows are `C994_Image_receipt_lookup_is_bound()` and `C994_Audit_image_is_pinned_and_required()`. Master deleted those test methods (e0f13fb77, de1a52dd7; CARD-1609) and dropped their rows (58a237a8f). Master has no rows that rc lacks. | no |
| 37 | tests/test-execution-policy.json | 2 | T | Every rc row is on master, deep-equal. Master's only extra is the scriptCensus row `test-qualification-graph` (0e2c171c1). Owner tooling (`scripts/lib/nightly-policy.ps1` `Get-NightlyPolicyHash`) computes rc 833dc822… = stated and master 9cd8d728… = stated. `Read-NightlyExecutionPolicy` loads master cleanly. The hash differs only because of that row. | no |

Note on rows 5, 10, 11, 32, 34 and 36: rc's behaviour is the C994/C1594 cleanup-receipt
image pinning for the retire-temp preview audit, including "different images from other SHAs
refuse" and the image-binding assertions. Master does not have it. That is a deliberate
design replacement, not a loss. CARD-1609 D-9 (9c8021277) says: "an empty temp.env and
disagreeing historical cleanup images are not the retirement gate". Master's
`c994_lookup_image` survives only as tooling selection. 9c8021277 is not in rc/2026-10-27.

## 6. Recording merge

The commit was prepared, not pushed to master or rc. It was built with
`git commit-tree 85283b399 -p origin/master -p origin/rc/2026-10-27`; it was pushed only to
`feat/card-task-3fdf288c-recorded` at `697b8873c12f44b69106bbf464660bc8a736a17e`. Checks:
`HEAD^{tree}` = `85283b399…` (master), `merge-base --is-ancestor` holds for both rc and master
(fast-forward of master), and `git cherry 697b8873c origin/rc/2026-10-27` is empty. It changes no tree bytes: its tree is master's tree. Cost:
`git rev-list --count origin/master..<tip>` = 1202, which is 1201 old-rc commits plus the merge,
almost all of them cherry-pick duplicates of master commits. That history joins master's log,
`git log`, `git bisect` and `git blame` walks. Benefit: `git cherry origin/master
origin/rc/2026-10-27` becomes empty, and `git branch -r --contains origin/rc/2026-10-27` lists
master, which is an auditable containment record. The land path rebases and cannot land a merge
branch (CARD-1637). Only a Merge-role fast-forward push of the prepared tip to master keeps it,
and only while master is still `87f4a868a`. If master moves, re-cut it with the same
`commit-tree` command on the new master tree.

## Uncertainties

- Hunk class comes from line-level presence plus blame and pickaxe provenance, not a semantic
  proof. A master-only commit that later removed an rc line is presumed deliberate. Each
  superseding commit named above is a reviewed master change in the CARD-1609, CARD-1610,
  CARD-1545 or CARD-1619 sets.
- Pickaxe on a line's first 90 characters can name an earlier touching commit; such lines were
  checked by hand (section 4).

Not done, noted: no port is needed. The only optional action is the recording-merge
fast-forward.
