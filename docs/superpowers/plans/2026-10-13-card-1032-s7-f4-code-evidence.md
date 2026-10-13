# CARD-1032 S7 F4 negative-witness Code evidence

All five required negative witnesses were added. Each was green on unchanged
production. This Code task built and executed each probe scratch mutant, which
turned its witness red on the first vector reached, and reverted it without
committing. That scratch execution is Code evidence, not formal post-land
SourceLanding Mutation qualification; no PC is discharged. The full 14-row F4 selection is GREEN with 232
executions at afafe9c6604d4578db7b1ce2ef4ad3f189209a5d. Production is unchanged
and inert. Next: Review. Restart: none.

Code task: 55328335-fca8-474e-a307-7a5fc84b37ff; landing owner Code
60025819-39c5-49e6-8951-817cdeeff5fc. Branch feat/card-task-55328335, worktree
/work/worktrees/task-55328335. Base 1383709fc74516e4b79570d3bd880ed9bea23aaf
(full task base 63b2e70479c846cacd01af6b20190e845f8258e4). Commits were added
without rebase: 2497d511786343470dd2b03f6938b3160f043d27 (tests first),
afafe9c6604d4578db7b1ce2ef4ad3f189209a5d (selection/PCs, tested SHA). This
evidence commit changes Markdown only.
Selection: docs/superpowers/plans/2026-10-13-card-1032-s7-f4-verification.md.
Review finding: /work/evidence/task-75a50129/review.md (F4). Probe:
/work/worktrees/task-045e9719/docs/superpowers/mutations/2026-10-13-card-1032-s7-viability-probe.md.

## Witnesses and scratch red

Changed files: tests/Antiphon.Tests/Application/CardWorktreeMirrorCleanupTests.cs
(four witnesses), tests/Antiphon.Tests/TestHelpers/CompletedCardMirrorFixture.cs
(removal-request counter at the removal-client wrapper seam, which also counts
capability-preflight and intercepted refusals, and a refusal-only interceptor
that asserts its reply has Complete=false), tests/Antiphon.SessionRunner.Tests/CompletedCardWorkspaceContractTests.cs
(signature witness), plus the plan, test design, mutation candidate and this selection.
No existing test body, assertion or timeout changed. No production file changed.

Scratch mutations were the probe's exact patches, run on the dirty tree at
2497d511 through scripts/build-slot.ps1 (slot granted, waited=0s), then
reverted with git checkout. Diffs and logs: /work/evidence/task-55328335/.

| Family | Mutation | Witness | Scratch red |
|---|---|---|---|
| VP-08 | Mac normalizes Binding.TaskId to Guid.Empty | C1032_LandingSignatureCoversTaskId | 1/1 failed: `Verify(changed, key)` should be False but was True, TaskId 55555555-... |
| VP-09 | PublicationAsync `o.TaskId == task.Id && o.Active` -> `o.Active` | C1032_WitnessLandingOwnership | failed: `result.Complete` should be False but was True, vector foreign-task |
| VP-10 | drop `HasPublication(op)` | C1032_WitnessLandingCompleteness | failed: `result.Complete` should be False but was True, vector observedRemoteTargetSha:null |
| VP-13 | ReadCommand drops RequestDigest comparison | C1032_WitnessPersistedReplayTamper | failed: reason should be immutable_request_unreadable but was receipt_mismatch, vector reportDigest (the replay hold no longer stopped the tampered request; the later refusal point was not separately traced) |
| VP-15 | unsupported removal reply -> `new(true, null, null)` | C1032_WitnessUnsupportedRemovalAfterInspection | failed: `result.Complete` should be False but was True, vector runtime-unsupported |

Batches: A = VP-09 + VP-13 and B = VP-10 + VP-15, each pairing mutations in
different production members with witnesses in different test methods; each
red named the expected witness and vector. Interaction between the paired
mutations was not separately excluded. VP-08 ran alone on the runner project. Each red names its vector.
Only the first red vector per witness was observed; per-vector red belongs to
Mutation (PC-595..599).

Disclosed setup failure: the first batch-B run failed Completeness in
CompletedCardMirrorFixture.InitializeAsync (`saved.Succeeded` was False) before
any witness assertion. It has the same symptom as the unreproduced fixture-setup failure the repair
evidence disclosed at 3cbd00d3. AgentReportStore.StoreAsync has a 2 s I/O
budget, and the run's cold Postgres start took about 66 s; these observations
do not establish the cause. It is not counted
as red. The witness was rerun alone under the unchanged batch-B mutation and
failed on its real assertion (row above). No budget, retry or assertion changed.

Pre-mutation sanity on unchanged production at 2497d511: the four server
witnesses passed 4/4 (one leased build and run). The first selection attempt
used exact names in an OR group without wildcards and selected zero tests. It
was rerun with `*` suffixes, and the F4 witness filters CP-33..37 each end in `*`.

## Ordinary verification

Checkpoint run 20261013-174707-f33f, one run of all 14 rows, GREEN exit 0.
CP-29, 30, 31 and 34..37 were 1/1 each. CP-13 was 7/7, CP-14 7/7, CP-F2
63/63, CP-RG1 1/1, CP-32 1/1, CP-33 1/1 and CP-F3 145/145, for 232 executions
including deliberate overlap. The TRX census shows one exact method each in
CP-33..37; CP-F2 contains the four server witnesses and CP-F3 the signature
witness. validate-checkpoint-receipt.ps1 exited 0:
`CHECKPOINT SOURCE VALID source=afafe9c6604d4578db7b1ce2ef4ad3f189209a5d rows=14`.
Every row had slot=granted, dirty=0, sourceState=clean and buildSource=verified.

V-1, V-2, V-3 and V-6 PASS at the stated scope. V-4 and V-5 PASS at adjacent
scope only. R-1 and R-2 PASS at adjacent scope; R-3 PASS for the
regressions named in the selection. Native Windows and S8 work remain deferred
with their owners and are not reported passed. No Unit lane was run because the
brief excludes it. Manual composition inspection found no production diff, no
Program registration, residue caller or route, and no Execute/cutoff change.

Extra leased drivers outside the table, all through build-slot: one sanity
server build and run, two scratch server builds and four runs (including the
setup-failure rerun and the zero-test filter attempt), one runner build and run,
and the checkpoint-tool bootstrap. No proof selection on unchanged source exceeded three
normal executions. Outputs from those drivers (bin-c55328335-srv,
bin-c55328335-runner from scratch, bin-c55328335-tool) were deleted. The
checkpoint tool deleted its own outputs.

## Pending

Post-land SourceLanding Mutation was NOT run. PC-595..599 (F4) and every earlier
PC/variant (PC-52..PC-594 as censused in the repair evidence) remain pending.

## Closing checks

Fetched origin/master b5653d5832aac66c5ac1ff1a0012efb33b54de79; `git merge-tree
--write-tree origin/master HEAD` exit 0 at the tested code tip (tree
3b61f2f04bbf45b0163a7429aa3aaae3578ec1c3). The final documentation tip is
checked again in the caller report.

Unedited checkpoint lines:

```text
commit: afafe9c6604d4578db7b1ce2ef4ad3f189209a5d  branch: feat/card-task-55328335  worktree: /work/worktrees/task-55328335  host: Debian GNU/Linux 12 (bookworm) cores=24
source: afafe9c6604d4578db7b1ce2ef4ad3f189209a5d state=clean buildSource=verified
CHECKPOINT CP-29 commit=afafe9c6604d4578db7b1ce2ef4ad3f189209a5d build=ok filter=/*/*/CardWorktreeMirrorCleanupTests/C1032_ReportRecognitionCaseTable* executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-55328335/.antiphon/checkpoints/20261013-174707-f33f/rows/CP-29/run.trx slot=granted waited=15s dirty=0 source=afafe9c6604d4578db7b1ce2ef4ad3f189209a5d sourceState=clean buildSource=verified
CHECKPOINT CP-30 commit=afafe9c6604d4578db7b1ce2ef4ad3f189209a5d build=reused filter=/*/*/CardWorktreeMirrorCleanupTests/C1032_ReportAddedAfterProducerInspection* executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-55328335/.antiphon/checkpoints/20261013-174707-f33f/rows/CP-30/run.trx slot=granted waited=0s dirty=0 source=afafe9c6604d4578db7b1ce2ef4ad3f189209a5d sourceState=clean buildSource=verified
CHECKPOINT CP-31 commit=afafe9c6604d4578db7b1ce2ef4ad3f189209a5d build=reused filter=/*/*/CardWorktreeMirrorCleanupTests/C1032_ReportAbsentAllowsCleanup* executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-55328335/.antiphon/checkpoints/20261013-174707-f33f/rows/CP-31/run.trx slot=granted waited=0s dirty=0 source=afafe9c6604d4578db7b1ce2ef4ad3f189209a5d sourceState=clean buildSource=verified
CHECKPOINT CP-13 commit=afafe9c6604d4578db7b1ce2ef4ad3f189209a5d build=reused filter=/*/*/CardWorktreeMirrorCleanupTests/(C1017_MirrorBehindPushedTip)|(C1017_ReopenBetweenEndpoints)|(C1017_MultipleMirrorsRemainDistinct)|(C1032_Admission*) executed=7 passed=7 failed=0 skipped=0 trx=/work/worktrees/task-55328335/.antiphon/checkpoints/20261013-174707-f33f/rows/CP-13/run.trx slot=granted waited=0s dirty=0 source=afafe9c6604d4578db7b1ce2ef4ad3f189209a5d sourceState=clean buildSource=verified
CHECKPOINT CP-14 commit=afafe9c6604d4578db7b1ce2ef4ad3f189209a5d build=reused filter=/*/*/CardWorktreeMirrorCleanupTests/(C1017_ReceiptMustMatchOperation)|(C1017_ReceiptMustMatchEndpoint)|(C1017_ReceiptMustMatchStore)|(C1017_ReceiptMustMatchTaskAttempt)|(C1017_AckIsNotRemoval)|(C1017_UnsupportedNeverFallsBack)|(C1032_Receipt*) executed=7 passed=7 failed=0 skipped=0 trx=/work/worktrees/task-55328335/.antiphon/checkpoints/20261013-174707-f33f/rows/CP-14/run.trx slot=granted waited=0s dirty=0 source=afafe9c6604d4578db7b1ce2ef4ad3f189209a5d sourceState=clean buildSource=verified
CHECKPOINT CP-34 commit=afafe9c6604d4578db7b1ce2ef4ad3f189209a5d build=reused filter=/*/*/CardWorktreeMirrorCleanupTests/C1032_WitnessLandingOwnership* executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-55328335/.antiphon/checkpoints/20261013-174707-f33f/rows/CP-34/run.trx slot=granted waited=0s dirty=0 source=afafe9c6604d4578db7b1ce2ef4ad3f189209a5d sourceState=clean buildSource=verified
CHECKPOINT CP-35 commit=afafe9c6604d4578db7b1ce2ef4ad3f189209a5d build=reused filter=/*/*/CardWorktreeMirrorCleanupTests/C1032_WitnessLandingCompleteness* executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-55328335/.antiphon/checkpoints/20261013-174707-f33f/rows/CP-35/run.trx slot=granted waited=15s dirty=0 source=afafe9c6604d4578db7b1ce2ef4ad3f189209a5d sourceState=clean buildSource=verified
CHECKPOINT CP-36 commit=afafe9c6604d4578db7b1ce2ef4ad3f189209a5d build=reused filter=/*/*/CardWorktreeMirrorCleanupTests/C1032_WitnessPersistedReplayTamper* executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-55328335/.antiphon/checkpoints/20261013-174707-f33f/rows/CP-36/run.trx slot=granted waited=0s dirty=0 source=afafe9c6604d4578db7b1ce2ef4ad3f189209a5d sourceState=clean buildSource=verified
CHECKPOINT CP-37 commit=afafe9c6604d4578db7b1ce2ef4ad3f189209a5d build=reused filter=/*/*/CardWorktreeMirrorCleanupTests/C1032_WitnessUnsupportedRemovalAfterInspection* executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-55328335/.antiphon/checkpoints/20261013-174707-f33f/rows/CP-37/run.trx slot=granted waited=0s dirty=0 source=afafe9c6604d4578db7b1ce2ef4ad3f189209a5d sourceState=clean buildSource=verified
CHECKPOINT CP-F2 commit=afafe9c6604d4578db7b1ce2ef4ad3f189209a5d build=reused filter=/*/*/(CardWorktreeCleanupRecoveryTests*)|(CompletedCardWorktreeRemovalTests*)|(CardWorktreeMirrorCleanupTests*)/* executed=63 passed=63 failed=0 skipped=0 trx=/work/worktrees/task-55328335/.antiphon/checkpoints/20261013-174707-f33f/rows/CP-F2/run.trx slot=granted waited=0s dirty=0 source=afafe9c6604d4578db7b1ce2ef4ad3f189209a5d sourceState=clean buildSource=verified
CHECKPOINT CP-RG1 commit=afafe9c6604d4578db7b1ce2ef4ad3f189209a5d build=reused filter=/*/*/TestClassificationGuardTests/Registry_matches_compiled_metadata* executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-55328335/.antiphon/checkpoints/20261013-174707-f33f/rows/CP-RG1/run.trx slot=granted waited=0s dirty=0 source=afafe9c6604d4578db7b1ce2ef4ad3f189209a5d sourceState=clean buildSource=verified
CHECKPOINT CP-32 commit=afafe9c6604d4578db7b1ce2ef4ad3f189209a5d build=ok filter=/*/*/CompletedCardWorkspaceRemovalTests/C1032_RemovalEligiblePhysical* executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-55328335/.antiphon/checkpoints/20261013-174707-f33f/rows/CP-32/run.trx slot=granted waited=0s dirty=0 source=afafe9c6604d4578db7b1ce2ef4ad3f189209a5d sourceState=clean buildSource=verified
CHECKPOINT CP-33 commit=afafe9c6604d4578db7b1ce2ef4ad3f189209a5d build=reused filter=/*/*/CompletedCardWorkspaceContractTests/C1032_LandingSignatureCoversTaskId* executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-55328335/.antiphon/checkpoints/20261013-174707-f33f/rows/CP-33/run.trx slot=granted waited=0s dirty=0 source=afafe9c6604d4578db7b1ce2ef4ad3f189209a5d sourceState=clean buildSource=verified
CHECKPOINT CP-F3 commit=afafe9c6604d4578db7b1ce2ef4ad3f189209a5d build=reused filter=/*/*/(CompletedCardWorkspaceRemovalTests*)|(CompletedCardWorkspaceContractTests*)|(RunnerWorkspaceServiceTests*)|(LegacyWorkspaceRemovalGateTests*)/* executed=145 passed=145 failed=0 skipped=0 trx=/work/worktrees/task-55328335/.antiphon/checkpoints/20261013-174707-f33f/rows/CP-F3/run.trx slot=granted waited=0s dirty=0 source=afafe9c6604d4578db7b1ce2ef4ad3f189209a5d sourceState=clean buildSource=verified
unlisted: none (the tool ran no other build or test command)
wall: 22m17s  sequential-equivalent: 18m59s  builds: 2  max-concurrent-builds: 1  rows: 14 green 0 red 0 skipped
outputs: deleted bin-c55328335-server/, bin-c55328335-runner/
verdict: GREEN exit=0
```

## F5 wording repair (Code task 1167283e)

Final Review 87af7b11 F5a-F5d; branch feat/card-task-1167283e from
7c5c6badc4fd49eb5b5624b2189a3d5277fbd0cb, commits added without rebase. Comments
and Markdown only: no production change, no test body, assertion, timeout or
method name change.

- F5a: CP-37's first `unsupported` is produced by the client capability
  preflight (PhoneHomeRunnerClient.RemoveCompletedCardWorkspaceAsync, after
  `Runtime.CompletedCardWorkspace` is withdrawn) before any removal frame; no
  recipient removal handler answers it. The `runtime-unsupported` step of
  C1032_WitnessUnsupportedRemovalAfterInspection proves the server's handling
  of that result. The fixture counter counts entries to its client wrapper.
- F5b: CP-34/CP-35 run the real inspection and assert no removal request; they
  were not refused at removal. CP-36 holds with no removal request.
- F5c: CP-35 is 13 vectors across 11 fields; the verification design now
  discloses the evidence-type fields the table does not vary (Review 87af7b11
  inventory).
- F5d: the scratch mutants were built and executed by Code task 55328335 as
  Code evidence; formal post-land SourceLanding Mutation is pending and no PC
  is discharged.

Sweep of every F4-added or touched sentence: this document, the verification
design and pending-PC table, the test-design F4 table, the plan's F4
paragraph, the mutation-candidate F4 section, the five `F4/VP-*` test
comments, the three F4 helper comments and the fixture interceptor comment.
Changed beyond F5a-d: "a broken fixture cannot pass" (now, as corrected by
F6: for CP-34..37 one that never reaches real removal fails the method; CP-33
has no removal and pairs its refusals with a successful signature
verification); "no hole was found" (now: these
witnesses found no production hole); batch rationale "never reaches/never
replays" and "red before any other path" (unproven; replaced with what was
observed); VP-13 "tampered request reached the recipient" and PC-598
"reaches recipient" (the later refusal point was not traced); "the same
unreproduced failure" (now: same symptom, cause not established); "every
checkpoint filter uses wildcards" (now: CP-33..37 end in `*`); fixture
"replaces the real reply ... never synthesizes" (now: returns a refusal
instead of the inner call, asserted Complete=false); "absent" reply (now: a
no-reason Complete=false response). Kept as accurate: VP-08 comment, the
landing helper comments, test counts, PC filters, and the ordinary
verification paragraphs above (F6 later qualified R-1/R-2/R-3 and extended
the VP-08 comment).

F5 verification: NOT complete. Checkpoint run 20261013-184851-0e9e at
e21c499d246e751e9a0b5888923b6a11b3d4bc6d (one run, all 14 rows) ended RED
exit 2 on the server build, not on any test: `tests/Antiphon.Tests` restore
failed with NU1605 package downgrades (Microsoft.AspNetCore.Mvc.Testing `9.*`
now resolves 9.0.21, whose Microsoft.Extensions.* 9.0.21 conflict with
10.0.0 reached through Microsoft.EntityFrameworkCore.Relational `9.*` =
9.0.20; nuget.org lists Mvc.Testing and Extensions.Logging 9.0.21 but no
EntityFrameworkCore.Relational 9.0.21). The 11 server rows (CP-29..31, 13,
14, 34..37, F2, RG1) therefore have no result. This branch changes no
project, props or NuGet file; Review 87af7b11 built the same project files
green at 18:14. Runner rows passed: CP-32 1/1, CP-33 1/1, CP-F3 145/145.
The server rows must be rerun once the feed is consistent (or after a
separate change pins the floating versions).

## F6 wording repair (Code task 9bbebbf5)

Final Review bd49988a F6; branch feat/card-task-9bbebbf5 from
07d3bd180e405cd97ec73123f233a47e645e34b1, commits added without rebase.
Markdown and // comments only: no production change, no test body, assertion,
timeout or method name change. Review bd49988a's retry run 20261013-190814-ebef
executed all 14 rows green (232 passed) at 07d3bd180; the code is unchanged
since, so the selection was not rerun.

F6: the verification design no longer says every witness ends in physical
removal. CP-34..37 end with `RemovedAsync` (real removal, imported receipt equal
to the runner journal receipt); CP-33 signs an in-memory command, asserts it
verifies true with its key and false with another key, then that changing only
Binding.TaskId (other GUID, empty) fails verification. It removes nothing.

One complete sweep, checked against the method bodies, the fixture and
CardWorktreeMirrorCleanupService.TryAsync: every sentence of the verification
design (intro, five witness bullets, fixture, counts, coverage, PC table), every
sentence of this document, the plan's S7 F4 paragraph, the test-design S7 F4
table, the mutation-candidate F4 section, the five F4/VP-* test comments, the
landing helper comments and the fixture interceptor comment. Corrected:

- physical removal limited to CP-34..37; CP-33's positive control stated
  (verification design, this document's F5 sweep, test-design CP-33, VP-08
  comment);
- refusal origin: CP-34/35 `unlanded_work` comes from the observation step
  after PublicationAsync finds no qualifying landing (H is observed and the
  target lacks it), and the methods do not assert which publication
  predicate rejected each vector; "internally consistent" dropped (only the
  foreign-task vector also changes the recovery prefix) (verification design,
  VP-09 comment);
- vector shape: CP-35 is 13 null/empty/false vectors, not "missing/empty"
  (verification design, test-design, VP-10 comment);
- CP-37: "keeps ... journal" and "keeps everything" replaced by the asserted
  items (mirror, admin directory, sentinel, empty journal, Refused, reason, no
  receipt); "replays the same operation to real removal" replaced, because the
  operation id is asserted only through the injected vectors, not on the final
  removal (verification design, test-design, VP-15 comment);
- CP-34/36 per-vector assertions listed in full (admin directory, sentinel,
  no imported receipt; CP-36 also the same operation and one removal request
  on replay);
- interceptor "is cleared before the positive" attributed to CP-37, its only
  user; "only setup build outside the table" limited to the checkpoint run;
- coverage prose matched to the Covers column: V-4 is CP-F3 only (CP-32
  covers V-5), R-3 does not include CP-F3; the ordinary verification above
  now says R-1/R-2 PASS at adjacent scope and R-3 for the named regressions;
- PC variants stated as the per-vector red Mutation must establish (scratch
  observed only the first red vector per method).

Checked and kept: the CP-36 claims and VP-13 comment, CP-37 preflight origin
and counter seam (F5a), the CP-35 field list and disclosure (F5c), the
scratch-red table, batches and setup-failure disclosure, counts (CP-13/14
seven, CP-F2 63, CP-F3 145, 232), the plan paragraph, the mutation-candidate F4
section, the `RebasedLandingAsync`/`ChangeLandingAsync` comments and the
fixture comment.

PC-595..599 and every earlier PC/variant remain pending; Mutation was not run.
