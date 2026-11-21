# CARD-1514 Final Review, round 2

**Verdict: blockers.** Reviewed `a0ab23f54af3652e5a5159149589448af22f734d..b9bf6ea58d0e6f5c7c72fbed906f3dfc475f4269` on `feat/card-task-c4660fc1`. The reviewed Code source task is `c4660fc1-ec8a-4b29-8e4e-e7864ce8fc03`. The original landing owner remains `4699a1a9-1553-4299-b517-27f403b6f2df`. Its remote branch still points to the previous reviewed SHA. This pass gives no approval to land.

## Blocking findings

### B1. EOF during handoff can discard the Git exit

- **Where.** `server/Infrastructure/Git/LandingGit.cs:417–455,603–651,766–786`. Introduced by `a537bd9ca0501fbb57c81bfcdef6f8d3247ba13c`.
- **Failure.** After the post-exit pipe deadline, a holder can close while `ReleaseChildAsync` hands the streams to its continuation. That continuation can dispose the `Process` synchronously or on another thread before the timeout branch reads `process.ExitCode`. A disposed .NET process loses its association and raises `InvalidOperationException` on exit-code access. The exception can still prevent `OwnedAsync` from clearing the recorded child, storing `PushExitCode`, or observing the remote. With the run budget expired, the generic catch can again emit `git_timeout`. The same race can lose a nonzero exit or the exit attached to a read exception.
- **Why.** The drain verdict is stable, but the exit is still read from a handle whose owner has changed. The hung-holder witnesses hold the pipe through the verdict and clean up afterward. The forced timeout and closed-pipe cases do not synchronize EOF with the handoff.
- **Fix.** Capture the exit once immediately after the exit wait, before cleanup can acquire custody. Use the captured value for every post-exit contract. Add assertions for zero and nonzero exits when EOF and disposal occur before the caller builds its result. Include the Linux journal being retained while the writer is open and released after EOF.

### B2. The exit-preserving path uses command arguments as verbs

- **Where.** `LandingGit.cs:441–450,792–793,1561–1574,1642–1655`, `LandWorkspace.cs:68–108`, `DelegationWorktreeService.cs:549–556`, and `RemoteWorkspaceService.cs:1237–1250`.
- **Failure.** A relative endpoint named `push` can make a real `ls-remote` read enter the mutation arm. At exit zero with incomplete pipes, the two-ref source/target read interprets empty stdout as `source_remote_missing`. The single-ref route reports an invalid response. A remote name, revision or path equal to a listed verb can change the command's classification. Conversely, `cherry-pick --abort/--quit`, `revert --abort/--quit`, `clean -fdx`, `branch -- <name> <ref>`, and `worktree lock/prune` reach this driver as apparent reads under ordinary operands. Their native CLI path can complete the mutation and then throw away the exit, interrupting worktree repair or provisioning. The latter branch was already absent from the journal allowlist. The repair newly relies on that allowlist for the caller's post-exit verdict.
- **Why.** `Any` examines options, paths, endpoints and positional arguments. Worktree subcommands need their own verb check. The rebase HEAD follow-up uses the same argument-token assumption independently.
- **Fix.** Parse the actual verb after global options with `LandingGitProfile.SubcommandIndex`, then apply subcommand and option rules to every current mutation path. Require complete output on listing reads. Exercise a real server read with a colliding operand and a native missing-verb mutation. Bind the HEAD follow-up to the parsed rebase operation.

### B3. Publication can still settle before confirmation and the delivery witness stops at the outbox

- **Where.** `LandingGit.cs:444–447`, `AgentTaskLandingProtocol.cs:327,411–426,1015–1019,1030–1047`, `AgentTaskLandService.cs:1442–1463,1574–1585`, and `AgentTaskLandPublicationTests.cs:943–994`.
- **Failure.** A successful push with a held pipe now reaches observation when the handle is still usable. If the follow-up read is unavailable, it still escapes or refuses and the service commits a terminal `LandRefused` and clears the pending request. The remote may already contain the verified SHA. A later explicitly authorized request can re-observe. No pending request remains for the automatic sweep. A successful rebase with incomplete pipes similarly awaits a fresh HEAD read with a new five-minute default process budget before returning the exit. A thrown read loses the rebase result, and a nonzero read leaves the caller with `interrupted_rebase`. The typed read exception also bypasses `StartRefAvailability.RunBoundedAsync`'s bounded-read translation and lands in the generic `Unexpected` classification in `LandFailureDiagnostic`.
- **Why.** The post-exit exit and the independent observation have different lifetimes. The new publication witness uses `ReplyTo.None` from `LandingSafetyHarness.SeedAsync` and only counts the outcome notification. It never selects an actual parent recipient or confirms receipt.
- **Fix.** Preserve the exit across follow-up read failures and persist a resumable observation outcome with the exact verified SHA and destination. On recovery, observe that destination before considering another push. Require a validated HEAD before advancing a rebase. Translate typed read timeouts at the domain boundary. Add a producer-to-recipient regression that reaches the real queue for a busy parent and an eligible parent, exercises the terminal-commit and queue-insert acknowledgement cuts, and confirms exactly one matching complete `UserPrompt` for the intended recipient.

## Caller audit

| Command through the changed driver | Verdict when the pipe is incomplete |
| --- | --- |
| Target `push`, source push, branch deletion | Target land saves the process exit and checks the verified SHA's remote containment. Deletion and source promotion use exit and ref observations. Other GitHub/workspace push wrappers use the exit as the Git command verdict. None reads push stdout. A SessionRunner `push --dry-run --porcelain` probe belongs to a separate Git driver. |
| `rebase`, including `--continue` and `--abort` | The main protocol and custom-base path use `RebaseHeadSha` and verify the checkout separately. They never parse rebase stdout. An independent HEAD read must succeed to proceed. See B1 and B3 for exit recovery. The abort path judges the exit and its own sequencer checks. |
| `merge --ff-only`, direct `update-ref` | The canonical advance verifies target ref/HEAD against the expected SHA. Observation pins, deletes and retirement refs use exit/CAS followed by a read as required by the caller. Stdin `update-ref` has a separate byte-stream path and discards stdout. |
| `fetch` | The reached forms fill objects, tracking refs or unique observation pins. Callers use exit and re-resolve the expected commit or pin. No changed-driver caller uses `--update-head-ok` or parses fetch stdout. |
| `add`, `checkout`, `checkout-index`, `restore`, `reset`, `commit` | Status, staged diff, sequencer, HEAD, or generated commit checks supply data where needed. `GitService.CommitAllChangesAsync` uses the commit's exit for its Boolean. No caller relies on mutation stdout. |
| `worktree add/remove` | Currently counted by the `add`/`remove` token. Creation verifies registration and checkout identity. Removal checks registration/filesystem state. `worktree list` needs a complete listing. |
| `cherry-pick`, `revert`, `clean`, branch creation, `worktree lock/prune` | The existing mutation callers listed in B2 lose the exit when the native Git pipes time out. The eligible in-process worktree-lock path has no redirected pipes. |
| Reads | `rev-parse`, `ls-remote`, `merge-base`, `cat-file`, `check-ref-format`, `status`, `ls-files`, `ls-tree`, `show-ref`, `for-each-ref`, `diff`, `diff-tree`, `rev-list`, `cherry`, `log`, `show`, `check-ignore`, `symbolic-ref -q`, `config` reads and `remote get-url` are read by verb. Argument collisions in B2 are possible. Quiet predicates use exit as their data. Listing consumers need EOF. The `cat-file --batch` and `check-attr --stdin` readers use the byte-stream runner. |
| Other server Git drivers | `GitWorkspaceService`, the separate repository/process helpers, and SessionRunner Git calls use their own process/result types. They do not acquire `PipeDrainIncomplete` from this change. No reached caller of this result type consumes SHA output from `commit-tree`, which would need both exit and output. |

Given a live handle and a correctly classified mutation, the incomplete-drain return preserves a nonzero exit and `git_exit_<code>`; `LandingGitResult.Succeeded` is false. If a later independent observation proves that the expected SHA is already present, the landing protocol records `AlreadyPresent`. The supplied new witnesses use exit zero. No fresh nonzero-plus-incomplete witness was supplied.

## Drain and Windows policy

The drain classifier makes a stable decision when it samples completion. Once that decision is `TimedOut`, the grace wait does not reinterpret it. When EOF is complete at the sample, an expired run budget does not change the drain result. `PipeDrainEofAtTheDeadlineIsClosed` supplies a previously completed task and a never-completing task. It omits the interleaving in which the timer wins the wait and EOF arrives before the sample. The expired-budget integration tests force the verdict, so they check the mapping after the sample. Add a deterministic boundary case that exercises the production selection in that interleaving as part of B1.

The two marked Linux tests have method-scoped Windows exclusions. The native Windows Job Object, post-exit pipes and `RepositoryLeaseWindowsBoundaryTests` remain owed to the Windows rerun. The Linux process substitution, POSIX double-fork and Windows sharing-denial skip supply Linux scope only. The repair retains the production 30-second bound. Code measured 3.40 s for the Linux hung-holder case and 4.39 s for the new publication witness. The plan's timeout estimates remain provisional from those samples. Its isolated database-schema setup uses the assembly fixture, and process-spawning methods stay on their existing class limiters. No session input-wait release code changed. Disabled parking leaves no automatic release deadline.

## Outcome delivery trace

The landing service adds the terminal event and outcome note while completing the request in the same transaction. The note captures the parent's session, durable notification ID and content digest. On restart the hosted scanner picks up that note. The queue inserts a `WhenIdle` row keyed by the notification ID, firing the flush for an eligible session and deferring a busy session. A busy queue lock leaves the note for the next pass. INSERT followed by a crash or lost acknowledgement is recovered by the unique source-notification ID, with the recipient and digest checked on relink. An enqueue exception retains a retryable note. The reconcile path reads the real recipient's transcript above the delivery floor and records the confirming prompt sequence only on matching receipt. The production outcome matcher can also consider a QueuedUserPrompt, so its Confirmed state cannot establish the prompt type for this review. The acceptance assertion needs the complete `UserPrompt` text in that recipient's transcript. Queue insertion, Sent state and runner acknowledgement provide earlier milestones.

Existing receipt/recovery tests supply component coverage for that storage and transcript logic. Neither CP-3 nor the new pipe publication test joins the repaired producer to the recipient. That missing V/R acceptance is B3.

## Merge and pins

`git fetch origin master` resolved `3ed773829e584eec0c93ec8b47bf3517a69204d7`. `git merge-tree --write-tree origin/master <reviewed-sha>` exited 1 with tree `0f9100f8804c77f1efc35e169a4cd1fafaef847c`. The two conflict hunks are exactly `policyHash` and `PinnedInputDigest`. Both sides' policy rows are present in the combined tree.

I computed the clean candidate pins and the would-be combined values in memory using Git blobs. No pins were written. The candidate computes `016ed18faae0c180aacddf382b4e6a2fadff3ce40a65117e62c6c39b9baf325c` and `74bd9e3d734ff272ed80a6fcf0a0408a2aaf4505387a97d32ef85581962a2de6`. For this exact combined tree, the computed values are `bafc2a8d62da805d25db3f901d009f14bdcb58927f39aba8d53c97c17c9b8598` and `580583ad588bbeb574b0b8afe8bd868616f2ce9e4b0660b9661b423904a72c05`.

In the authorized combined checkout, preserve the merged exclusion rows and resolve the conflict markers. Run the first command and write its output to `tests/test-execution-policy.json` as `policyHash`. Then run the second command and write its output to `$script:PinnedInputDigest` in `scripts/test-live-lane-run.ps1`. Repeat against the actual combined tree if either input ref changes.

```sh
pwsh -NoProfile -Command '. ./scripts/lib/nightly-core.ps1; if (-not (Test-NightlyCoreAvailable)) { throw "nightly-core-required" }; [Antiphon.Nightly.NightlyPolicy]::HashText((Get-Content -LiteralPath tests/test-execution-policy.json -Raw))'
pwsh -NoProfile -File scripts/test-live-lane-run.ps1 -PrintInputDigest
```

## Verification

Code reported one checkpoint run, `20261121-192710-7870`, over CP-1 through CP-4 at the reviewed tip. It reported CP-1 with 89 executed, 88 passed and one failed, CP-2 with 10 executed, nine passed and one failed, CP-3 with 10 passed, and CP-4 with 64 passed and one skipped. Code's run took 13m19s. The red-first commit `cb980f4c3bba87a05f165f17c52cd8fcc131892e` replaces the old exception assertion before the production change. Its amended assertion can fail on the old path. A fresh pre-fix execution receipt was unavailable in this review. No plan PC row exists. The operator's mutation waiver supplies no red or green receipt.

The first Review scheduler attempts, `20261121-195353-3991` and `20261121-195643-09cb`, had build slots, but .NET's native child resolution bypassed the PATH load wrapper. The two attempts were stopped before any test host launch. They supply zero test credit. The recovery reused the compiled checkpoint tool, a PowerShell host that resolves the wrapper for native children, and fresh output identities. The wrapper reads `/proc/loadavg` immediately before each test or build and waits in 30-second intervals for load below 14. The load gate applied `--timeout 570s` to the test host and left each admitted row running through later load increases. The candidate and detached exact-master attempts each have one new isolated test build, bundled serial rows, and one slot per build/test driver. The tool itself was built once under an independent build slot at source `b9bf6ea58d0e6f5c7c72fbed906f3dfc475f4269`.

Candidate run `20261121-200000-0f37` uses the repair plan's four filters, the nine-class Final guard batch and `LiveLaneRunnerTests` for the changed pinned input gate. Each named spot check is covered by its class row. Baseline run `20261121-200006-0f14` uses the two exact methods at `3ed773829e584eec0c93ec8b47bf3517a69204d7`.

The compiled successor to `check-evidence-diff.ps1`, `antiphon-scripts evidence-diff`, checked `e4243def97326dfc8edf05df08d5dd4a3a07c068..b9bf6ea58d0e6f5c7c72fbed906f3dfc475f4269`. It checked six commits and reported zero evidence violations. `git diff --check` passed. The impacted-classes selector found no migration row and zero downgrade selections. It returned seven Linux filter entries and one Windows entry for the round-2 diff. The named classes, guards and pinned-input gate take precedence within the 40-minute review budget. The selector's remaining derived filter entries receive no execution credit.

C1714 reproduced on master, with one distinct execution and the same failed `result.Accepted` assertion (expected false, actual true). The candidate C1199 check produced `c1199-roster-35: count=37 unique=37 shape_conflict_rule=0`. The one commissioned exact master C1199 check reached its existing 120 s per-test deadline while awaiting the script harness. It did not produce the roster assertion. The roster method, harness and three selected checkpoint test sources are byte-identical between the revisions. That supports the inheritance hypothesis while leaving the identical failure unverified. Neither result establishes a repair-introduced roster regression.

The candidate CP-2 TRX contains both selected classes. `Markers_do_not_expand_policy_exclusions`, `Policy_rows_require_pinned_attribute`, and `LinuxOnlyWindowsExclusionPolicy` passed. The candidate CP-1 TRX contains all six named new or changed Git pipe witnesses, including the forced budget and fresh rebase HEAD cases. The native hung-holder case took 3.44 s.

### Fresh results

| Source and row | Executed | Passed | Failed | Skipped | Result and fresh TRX ID |
| --- | ---: | ---: | ---: | ---: | --- |
| Candidate CP-1, LandingGitTests | 89 | 88 | 1 | 0 | C1714. `a9beb724-f36a-4484-8adc-84fda87f74b4`. |
| Candidate CP-2, native policy and release platform | 10 | 9 | 1 | 0 | C1199 roster assertion. `f9bad489-fff4-4cb5-b739-22f92b5c5ac1`. |
| Master baseline CP-1, exact C1714 | 1 | 0 | 1 | 0 | Same assertion. `16376d61-136d-407d-b991-6ac95f626e5e`. |
| Master baseline CP-2, exact C1199 | 1 | 0 | 1 | 0 | 120 s timeout. `ef0eaa8e-0b5f-41ad-8b5a-653a84ac3b0b`. |
| Candidate CP-3, ReviewEvidenceResettlementTests | 10 | 10 | 0 | 0 | `a099a8a5-7790-4f50-aacc-0bf41eff02e4`. |
| Candidate CP-4, AgentTaskLandPublicationTests | — | — | — | — | Admitted at load 11.93. The review wall deadline ended the row before it produced a TRX. |
| Candidate CP-5, nine-class Final guard batch | — | — | — | — | Queued at the deadline. |
| Candidate CP-6, LiveLaneRunnerTests | — | — | — | — | Queued at the deadline. |

Candidate CP-4 still owes the fresh `PushExitZeroWithHungPipeConfirmsPublication` identity and the rest of its class. Code reported that case passing. The next Code slice needs fresh coverage of CP-4, CP-5, CP-6 and the remaining applicable impact selections, plus the exact master C1199 assertion result. The verified candidate TRX identities and the two exact master attempts use clean source and verified build provenance. Final ordinary scope is incomplete.

### Checkpoint receipts

```text
CHECKPOINT CP-1 commit=b9bf6ea58d0e6f5c7c72fbed906f3dfc475f4269 build=ok filter=/*/*/LandingGitTests/* executed=89 passed=88 failed=1 skipped=0 trx=/work/worktrees/ws-07-d2/.antiphon/checkpoints/20261121-200000-0f37/rows/CP-1/run.trx slot=granted waited=0s dirty=0 source=b9bf6ea58d0e6f5c7c72fbed906f3dfc475f4269 sourceState=clean buildSource=verified
CHECKPOINT CP-2 commit=b9bf6ea58d0e6f5c7c72fbed906f3dfc475f4269 build=reused filter=/*/*/(NativeLinuxPolicyWitnessTests*)|(ReleaseGatePlatformTests*)/* executed=10 passed=9 failed=1 skipped=0 trx=/work/worktrees/ws-07-d2/.antiphon/checkpoints/20261121-200000-0f37/rows/CP-2/run.trx slot=granted waited=0s dirty=0 source=b9bf6ea58d0e6f5c7c72fbed906f3dfc475f4269 sourceState=clean buildSource=verified
CHECKPOINT CP-3 commit=b9bf6ea58d0e6f5c7c72fbed906f3dfc475f4269 build=reused filter=/*/*/ReviewEvidenceResettlementTests/* executed=10 passed=10 failed=0 skipped=0 trx=/work/worktrees/ws-07-d2/.antiphon/checkpoints/20261121-200000-0f37/rows/CP-3/run.trx slot=granted waited=0s dirty=0 source=b9bf6ea58d0e6f5c7c72fbed906f3dfc475f4269 sourceState=clean buildSource=verified
CHECKPOINT CP-4 commit=b9bf6ea58d0e6f5c7c72fbed906f3dfc475f4269 build=n/a filter=/*/*/AgentTaskLandPublicationTests/* executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a slot=granted waited=0s dirty=unknown source=unknown sourceState=unknown buildSource=unknown
CHECKPOINT CP-5 commit=b9bf6ea58d0e6f5c7c72fbed906f3dfc475f4269 build=n/a filter=/*/*/(TestClassificationPolicyTests*)|(TestClassificationGuardTests*)|(SlowTestRegistryMergeTests*)|(SlowTestTripwireTests*)|(NativeLinuxPolicyWitnessTests*)|(MechanicalPcContractTests*)|(TaskPlatformGuidanceTests*)|(C1729LayeringGuardTests*)|(TestLaneCategoryGuardTests*)/* executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a slot=skipped waited=0s dirty=unknown source=unknown sourceState=unknown buildSource=unknown
CHECKPOINT CP-6 commit=b9bf6ea58d0e6f5c7c72fbed906f3dfc475f4269 build=n/a filter=/*/*/LiveLaneRunnerTests/* executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a slot=skipped waited=0s dirty=unknown source=unknown sourceState=unknown buildSource=unknown
CHECKPOINT CP-1 commit=3ed773829e584eec0c93ec8b47bf3517a69204d7 build=ok filter=/*/*/LandingGitTests/C1714_Status_that_cannot_open_a_directory_is_untrusted executed=1 passed=0 failed=1 skipped=0 trx=/tmp/c1514-r2-base-7589c103/.antiphon/checkpoints/20261121-200006-0f14/rows/CP-1/run.trx slot=granted waited=0s dirty=0 source=3ed773829e584eec0c93ec8b47bf3517a69204d7 sourceState=clean buildSource=verified
CHECKPOINT CP-2 commit=3ed773829e584eec0c93ec8b47bf3517a69204d7 build=reused filter=/*/*/ReleaseGatePlatformTests/C1199_WindowsRequiredRoster executed=1 passed=0 failed=1 skipped=0 trx=/tmp/c1514-r2-base-7589c103/.antiphon/checkpoints/20261121-200006-0f14/rows/CP-2/run.trx slot=granted waited=0s dirty=0 source=3ed773829e584eec0c93ec8b47bf3517a69204d7 sourceState=clean buildSource=verified
```
