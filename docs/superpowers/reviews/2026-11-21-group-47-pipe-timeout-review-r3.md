# CARD-1514 group-47 pipe-bound repair, Final Review round 3

**Verdict — blockers.** Reviewed `b9bf6ea58d0e6f5c7c72fbed906f3dfc475f4269..8fa8148983c5d5513624081a17e227976d19891b`. The remote repair ref `origin/feat/card-task-3add1e5d` resolves to the reviewed tip. Code task `3add1e5d-4510-4a3e-a464-a2010d4227d5` continues landing owner `4699a1a9-1553-4299-b517-27f403b6f2df`. The source worktree started clean.

## Blocking findings

### B1. Three current reads enter the incomplete-output success path

- **Where.** `server/Infrastructure/Git/LandingGit.cs:459–468,822–840`, `server/Application/Services/WorktreeBaseInspection.cs:176–189`, `server/Application/Services/DelegationWorktreeService.cs:186–213,1044–1049`, and `server/Application/Services/AgentTaskLandSourceResolver.cs:416–429`. Introduced by the round-3 fix at `64cb0c927b9a9857ababc05f7cbdc188315434bb`.
- **Failure.** The default is Mutation, including an absent verb. `cherry`, `reflog show`, and `check-ignore` reach this default through unannotated server calls. For a `cherry` read that needs stdout and hits the post-exit pipe bound, the process can exit 0 and the driver returns `Succeeded=true` with an empty body. The containment readers interpret that body as proof that all patches are present. Source adoption records `RecoveryPatchesContained=true` without the required patch evidence. Worktree-base and sibling inspection can skip work that is still unlanded.
- **Why.** Parsing the verb after global options closes the operand collision. The allowlist omits existing reads. The explicit-effect witnesses supply the answer at the call site, and the table witness omits the affected reads and the unknown case.
- **Fix.** Require complete output by default or refuse unknown effects. Enumerate known mutations and mixed-mode subcommands or classify them explicitly at their callers. Add a native-path witness that makes a real containment consumer reject an incomplete read with exit 0, plus checks for unknown effects and the other affected reads.

### B2. Schema-3 rebase recovery refuses the state it just queued

- **Where.** `server/Application/Services/AgentTaskLandingProtocol.cs:133–135,344–380,500–517` and `server/Application/Services/AgentTaskLandService.cs:823–827,1330–1344`. Introduced by `64cb0c927b9a9857ababc05f7cbdc188315434bb`.
- **Failure.** After a successful rebase whose independent HEAD read misses, the protocol persists `RebaseStarted / rebase_head_unconfirmed` and requeues. On the next sweep schema 3 hits the unconditional `interrupted_rebase` guard before the new HEAD recovery path. The catch transitions the operation to Refused and the service completes it with a terminal refusal, even if the HEAD read would now succeed.
- **Why.** The retry branch is unreachable for the schema that creates new operations. Simply bypassing the older guard would also be insufficient. Reading HEAD twice proves stability between the reads. It does not bind that SHA to the original rebase if another actor moved the disposable checkout during the retry interval.
- **Fix.** Keep the existing refusal for a rebase without a durable positive acknowledgement. Add or reuse evidence that safely binds recovery to the actual rebased output and original inputs, or return a deliberate retryable state that can safely reprepare from the pinned inputs. Test a successful exit with an initial HEAD miss across service restart, including a moved HEAD.

### B3. A requeue write failure can settle an unconfirmed push, and delivery coverage omits that recovery

- **Where.** server/Application/Services/AgentTaskLandingProtocol.cs:457–470,1075–1080, server/Application/Services/AgentTaskLandService.cs:823–827,1440–1498,1607–1618 and 	ests/Antiphon.Tests/Application/AgentTaskLandPublicationTests.cs:936–988,1008–1112. Remaining part of round-2 B3.
- **Failure.** After the zero exit is durable, an observation miss stays pending when both progress persistence and requeue succeed. If recording the miss or requeueing fails transiently and failure settlement can write, the separate failure handler sees no publication timestamp and commits a terminal LandRefused. It clears the pending request even though the saved zero-exit operation can still be confirmed. The pending-push test resumes by directly running the service with no recipient. The hung-pipe test delivers one complete UserPrompt through both enqueue faults and a busy-to-eligible recipient, but its first remote observation succeeds. Neither test covers the repaired pending path plus terminal commit acknowledgement loss.
- **Why.** The resumable disposition is handled only on the normal protocol-return path. The catch-side durable settlement recognizes confirmed publication and has no branch for an already durable push exit of zero. The tests split publication recovery from recipient acceptance. The existing ref-only delivery tests cover terminal cuts for a different producer path.
- **Fix.** Make transient failure settlement preserve an eligible request with a durable zero-exit push until the exact destination is observed. Keep binding and authority checks. Carry one request through the observation miss, failed progress or requeue write, sweep recovery and lost terminal-commit acknowledgement to the real recipient queue. Assert one push of the verified SHA, one terminal outcome, one keyed row, and one complete matching UserPrompt above the intended recipient delivery floor.

## Git caller audit

This audit follows the native process reached by `ILandingGit.RunAsync`, `RunOwnedAsync`, `RequiredAsync` and forwarded argv in server and src. The other Git drivers use their own result types and do not consume this incomplete-drain result.

| Reached form | Correct effect | Review result |
|---|---|---|
| `cat-file`, `check-ref-format`, `diff`, `diff-index`, `diff-tree`, `for-each-ref`, `log`, `ls-files`, `ls-remote`, `ls-tree`, `merge-base`, `rev-list`, `rev-parse`, `show`, `show-ref`, `status` | Read | Verb classification agrees. All listing consumers need EOF. `merge-base --is-ancestor` and other quiet predicates retain the read contract. |
| `cherry`, `reflog show`, `check-ignore --quiet` | Read | **Mutation by default.** Reflog consumers read creation receipts and salvage evidence. The quiet ignore predicate uses exit as its answer. |
| `config --get/--bool/--list`, `hash-object` without `-w`, `remote get-url`, `symbolic-ref -q`, `worktree list` | Read | The reached forms classify as reads, including `remote get-url --push --all`. |
| `add`, `checkout`, `checkout-index`, `cherry-pick --abort/--quit`, `clean -fdx`, `clone`, `commit`, `fetch`, `init`, `merge`, `push`, `rebase` and its continuations, `reset`, `restore`, `revert --abort/--quit`, `update-ref` | Mutation | The reached forms classify as mutations or use an explicit effect. Push, fetch and other writes rely on their exit or independently inspect the resulting ref, sequencer or checkout. Init and clone can run before a journal root exists. |
| `branch -- <name> <ref>`, branch deletion, `worktree add/lock/prune/remove`, config writes, remote writes, symbolic-ref writes | Mutation | Create, lock, prune and add on the repaired call paths use explicit Mutation. The reached fallback forms are mutations. |
| `cat-file --batch`, `check-attr --stdin`, `update-ref --stdin` | Read, Read, Mutation | The separate byte driver does not return partial stdout. Its shared classifier now unnecessarily gives the attr read a mutation journal. |
| Missing or unknown verb | Require complete pipes or refuse | **Mutation.** This is the reachable fail-open in B1. |

Other server/src wrappers issue reads such as `branch --list/--show-current`, `check-ignore`, `cherry`, `reflog show`, and runner `push --dry-run --porcelain` probes. Their mutations include `gc`, `read-tree`, `write-tree`, `worktree repair`, and runner branch/worktree setup. Their results do not enter this driver's timeout arm. No reached `LandingGit` call consumes `commit-tree` output. If added, that write needs both its exit and complete output.

On the normal post-exit path, `ReadExit` now precedes the drain verdict, kill or abandonment, and the explicit relinquish. A blocking drain on that path returns the saved mutation exit or carries it in the read exception. In the generic exception/kill path, the pre-release exit read is best effort. A post-handoff read can touch a disposed handle, but the guarded read absorbs that exception and the original cancellation or driver failure is rethrown. The disposal witness covers a zero exit, a nonzero exit, and a read exception. Its seam supplements the actual native hung-holder cases.

## Publication and delivery trace

The successful push writes `PushExitCode=0` for the existing request and destination. A transient observation miss requeues that request. On resume it checks the immutable approval and recorded pins, then observes the saved destination and verified SHA. The saved-exit-zero branch returns from observation or requeues. It cannot reach another push. Once destination containment is proven, confirmation is durable and replay takes cleanup. The task transaction binds the terminal event to the request and inserts the Outcome before clearing pending state. The same request is guarded by its pending and terminal-event fields. The database keys the outbox by source event and the queue by `SourceLandNotificationId`.

The hosted reconcile recovers the durable Outcome after a restart. A before-enqueue fault leaves it retryable. An insert followed by a lost acknowledgement relinks by the notification ID after checking the session and digest. WhenIdle holds a busy recipient's pending row until it can flush. The receipt path pulls the intended session transcript and matches above the delivery floor. The new hung-pipe test checks an actual complete `UserPrompt`, its sequence and a single submitted body, rather than treating queue insertion or Sent as proof. B3 describes the remaining fault-path gap. The session waiting-for-input policy did not change. With parking disabled there is no automatic release deadline.

## Merge and pins

`git fetch origin master` resolved `20d801e8a003d7026dfc31e77d933ad39533705a`. `git merge-tree --write-tree` exited 1 against that ref and the commissioned master `0372c6f663cb2b3fe631f047198e626a94c9382e`. In each merge, the two actual conflict hunks are the `policyHash` and `PinnedInputDigest` lines. The merge trees were `31d4b84c9cbb9a94e03de0c43687b79eb40025e8` and `63062c045c5a58e0dea58226ea440234dc52e88e`, respectively.

| Tree | `policyHash` | `PinnedInputDigest` |
|---|---|---|
| Reviewed source, computed and stored values agree | `016ed18faae0c180aacddf382b4e6a2fadff3ce40a65117e62c6c39b9baf325c` | `74bd9e3d734ff272ed80a6fcf0a0408a2aaf4505387a97d32ef85581962a2de6` |
| Both scratch merges after reseal | `bafc2a8d62da805d25db3f901d009f14bdcb58927f39aba8d53c97c17c9b8598` | `580583ad588bbeb574b0b8afe8bd868616f2ce9e4b0660b9661b423904a72c05` |

Preserve both sets of policy rows when resolving the two pin hunks. Run the first command in the merged tree and write its output to `policyHash` in `tests/test-execution-policy.json`. Then run the second command and write its output to `$script:PinnedInputDigest` in `scripts/test-live-lane-run.ps1`.

```sh
pwsh -NoProfile -Command '. ./scripts/lib/nightly-core.ps1; if (-not (Test-NightlyCoreAvailable)) { throw "nightly-core-required" }; [Antiphon.Nightly.NightlyPolicy]::HashText((Get-Content -LiteralPath tests/test-execution-policy.json -Raw))'
pwsh -NoProfile -File scripts/test-live-lane-run.ps1 -PrintInputDigest
```

Both commands ran using existing nightly-core caches. The replay changed only the two pins in the detached scratch merges. Fetched master carries the landed equivalents of the separate C1714 and C1199 fixes as `3cd3e5da4835c3d5b5d9d91c60e8b177c543ef38` and `20d801e8a003d7026dfc31e77d933ad39533705a`.

## Verification

The compiled successor to `check-evidence-diff.ps1` checked candidate range `e4243def97326dfc8edf05df08d5dd4a3a07c068..8fa8148983c5d5513624081a17e227976d19891b` with nine commits and zero violations. `git diff --check` passed. The range impact selector returned 155 classes, seven Linux filter entries and one Windows entry. Migration and downgrade selection count is zero.

Code's settled report names checkpoint `20261121-215123-5c19` at `64cb0c927b9a9857ababc05f7cbdc188315434bb`. CP-1 reported 93 passed and C1714 failed, CP-2 nine passed and C1199 failed, CP-3 ten passed, CP-4 65 passed with one inherited Windows sharing-denial skip, and CP-5 nine passed. Code reported both exact reds on the commissioned master. The Code receipt directory is unavailable in this worktree. The candidate adds a generated index after the Code test SHA. Code's pass counts supply historical evidence only.

The final selection includes the five closed plan rows, the nine-class policy/mechanical/platform/layering guard batch and the diagnostic/live-lane classes. CP-5 preserves all nine named witness patterns, including the five specifically requested. The blocker's round-3 diff restriction governs source review. The Final profile requires fresh ordinary rows. Other range-selected filters need separate execution credit. Required Windows Job Object and pipe behavior and `RepositoryLeaseWindowsBoundaryTests` remain pending. Production resolves `ILandingGit` to `LandingGit` and its owner factory uses a Windows Job Object on Windows and a process environment marker on Linux. The actual Linux hung-holder cases exercise the latter, and disposal seams do not exercise the Windows branch. The plan has no PC-n row. The waiver supplies no mutation evidence.

### Fresh execution

Checkpoint `20261121-224308-b302` ran directly against `8fa8148983c5d5513624081a17e227976d19891b`. It reported clean source, verified build provenance and one isolated build reused across seven serial rows. Each driver took a granted slot with zero broker wait. The build was admitted at load 12.88. CP-1 was held for one 30-second load poll, then admitted at 10.70. CP-2 through CP-7 were admitted at 8.28, 5.27, 4.14, 5.18, 4.64 and 3.17. Each test host received `--timeout 570s`. Policy harness builds nested under CP-2 and CP-7 used those rows' leases and passed the same load check. The preliminary run `20261121-224027-5bdf` used a bare duration, which the CLI parsed as seconds. It stopped at its hard-wall deadline without a completed build or any test receipt. The measured run used a fresh output and an explicit `34m` cap.

| Row | Executed | Passed | Failed | Skipped | Wall | Fresh TRX ID |
|---|---:|---:|---:|---:|---:|---|
| CP-1 Git | 94 | 93 | 1 | 0 | 61 s | `b141e12f-8ae9-4e86-853d-f48769d19acc` |
| CP-2 policy and roster | 10 | 9 | 1 | 0 | 152 s | `665720fc-8993-4761-a629-d6719c1c59c1` |
| CP-3 resettlement | 10 | 10 | 0 | 0 | 61 s | `ee21f2a8-8c63-45e0-947d-c6717fbaef61` |
| CP-4 publication | 65 | 65 | 0 | 1 | 128 s | `c3a077be-42a1-4a9b-8c6b-d6f08366d053` |
| CP-5 named witnesses | 9 | 9 | 0 | 0 | 46 s | `9b0121dd-8ef7-4a22-babf-576b472101fb` |
| CP-6 guards | 84 | 84 | 0 | 0 | 82 s | `68a60de1-a5e1-48b9-bd5f-bdb861f0dddc` |
| CP-7 diagnostics and live-lane inputs | 17 | 17 | 0 | 0 | 36 s | `59dd8ed4-edc0-4720-a9c4-daca0ca0f4d2` |

CP-1 failed only C1714 with `Accepted=True` where the test expects false. CP-2 failed only C1199 at the 35-entry assertion with 37 unique entries and zero shape conflicts. Both match the separately reported master failures and are inherited. CP-4 includes the explicitly skipped `C688_Repair_UnknownLinkedHeadPreventsCanonicalAdvance(locked, False)` parameter row. Its other 65 cases passed. CP-5 contains all nine distinct passing method identities. CP-6 contains all nine named guard classes. CP-7 contains 13 diagnostic and four live-lane cases.

The run finished in 17m48s. The checkpoint build phase was 449.25 s, including confinement and provenance work. The compiler reported 350.02 s. CP-5 timed the native hung-holder witness at 3.42 s, the pending-push case at 9.49 s and the hung-pipe recipient case at 11.35 s. These are single-run observations. Row startup, test-wall and teardown phases are in the checkpoint report. The finer database and process phase split is unknown. The receipt validator returned `report_failed` on the aggregate red result, so this Review cannot assert the source-clean gate. The exact selection covered CP-1 through CP-7. The remaining derived impact selections and native Windows checks stay pending, and the Final ordinary gate is incomplete.

### Checkpoint receipts

```text
CHECKPOINT CP-1 commit=8fa8148983c5d5513624081a17e227976d19891b build=ok filter=/*/*/LandingGitTests/* executed=94 passed=93 failed=1 skipped=0 trx=/work/worktrees/ws-05/.antiphon/checkpoints/20261121-224308-b302/rows/CP-1/run.trx slot=granted waited=0s dirty=0 source=8fa8148983c5d5513624081a17e227976d19891b sourceState=clean buildSource=verified
CHECKPOINT CP-2 commit=8fa8148983c5d5513624081a17e227976d19891b build=reused filter=/*/*/(NativeLinuxPolicyWitnessTests*)|(ReleaseGatePlatformTests*)/* executed=10 passed=9 failed=1 skipped=0 trx=/work/worktrees/ws-05/.antiphon/checkpoints/20261121-224308-b302/rows/CP-2/run.trx slot=granted waited=0s dirty=0 source=8fa8148983c5d5513624081a17e227976d19891b sourceState=clean buildSource=verified
CHECKPOINT CP-3 commit=8fa8148983c5d5513624081a17e227976d19891b build=reused filter=/*/*/ReviewEvidenceResettlementTests/* executed=10 passed=10 failed=0 skipped=0 trx=/work/worktrees/ws-05/.antiphon/checkpoints/20261121-224308-b302/rows/CP-3/run.trx slot=granted waited=0s dirty=0 source=8fa8148983c5d5513624081a17e227976d19891b sourceState=clean buildSource=verified
CHECKPOINT CP-4 commit=8fa8148983c5d5513624081a17e227976d19891b build=reused filter=/*/*/AgentTaskLandPublicationTests/* executed=65 passed=65 failed=0 skipped=1 trx=/work/worktrees/ws-05/.antiphon/checkpoints/20261121-224308-b302/rows/CP-4/run.trx slot=granted waited=0s dirty=0 source=8fa8148983c5d5513624081a17e227976d19891b sourceState=clean buildSource=verified
CHECKPOINT CP-5 commit=8fa8148983c5d5513624081a17e227976d19891b build=reused filter=/*/*/(LandingGitTests*)|(AgentTaskLandPublicationTests*)|(LandFailureDiagnosticTests*)/(TimerWinThenEofBeforeTheSampleIsClosed*)|(ExitIsCapturedBeforeTheProcessIsDisposed*)|(CallSiteReadThrowsWhenTheOperandIsPush*)|(CallSiteMutationKeepsAMissingVerbExit*)|(EffectOfDoesNotTreatOperandsAsVerbs*)|(HungPipeHolderFailsWithGitPipeTimeout*)|(PushExitZeroWithFailedObservationStaysResumable*)|(PushExitZeroWithHungPipeConfirmsPublication*)|(C498_ClassifierMapsConcurrencyBeforePersistence*) executed=9 passed=9 failed=0 skipped=0 trx=/work/worktrees/ws-05/.antiphon/checkpoints/20261121-224308-b302/rows/CP-5/run.trx slot=granted waited=0s dirty=0 source=8fa8148983c5d5513624081a17e227976d19891b sourceState=clean buildSource=verified
CHECKPOINT CP-6 commit=8fa8148983c5d5513624081a17e227976d19891b build=reused filter=/*/*/(TestClassificationPolicyTests*)|(TestClassificationGuardTests*)|(SlowTestRegistryMergeTests*)|(SlowTestTripwireTests*)|(NativeLinuxPolicyWitnessTests*)|(MechanicalPcContractTests*)|(TaskPlatformGuidanceTests*)|(C1729LayeringGuardTests*)|(TestLaneCategoryGuardTests*)/* executed=84 passed=84 failed=0 skipped=0 trx=/work/worktrees/ws-05/.antiphon/checkpoints/20261121-224308-b302/rows/CP-6/run.trx slot=granted waited=0s dirty=0 source=8fa8148983c5d5513624081a17e227976d19891b sourceState=clean buildSource=verified
CHECKPOINT CP-7 commit=8fa8148983c5d5513624081a17e227976d19891b build=reused filter=/*/*/(LandFailureDiagnosticTests*)|(LiveLaneRunnerTests*)/* executed=17 passed=17 failed=0 skipped=0 trx=/work/worktrees/ws-05/.antiphon/checkpoints/20261121-224308-b302/rows/CP-7/run.trx slot=granted waited=0s dirty=0 source=8fa8148983c5d5513624081a17e227976d19891b sourceState=clean buildSource=verified
```
