# CARD-1614 slice B report (task 2bfb3eae)

Outcome: Linux custody seal semantics are implemented on `feat/card-task-2bfb3eae`. Ordinary Linux observation no longer terminates on root exit. Explicit release and dead-host recovery stay Unknown unless a receipt is real. A missing tree is unreadable rather than an empty process list. The Linux portable checkpoint rows are green. Native cgroup rows were not run on this host and are not claimed green or red. Windows linger and job close are unchanged. `SessionRunner:LinuxCustodyQualified` stays at its default false.

No `csproj` or `props` file changed. Land-graph `-Update` was not run.

## Commits

Branch `feat/card-task-2bfb3eae`, fast-forward only from slice A `7a2cd795590c1836ca213c3894cf17fab780e7d6`. No rebase, amend, reset, force-push, or merge of `origin/master`.

- RED `6139f090d4ec77bfeb6c6ea6212124ddc0164911` — `test(card-1614): witness Linux seal, recovery, and Windows linger`
- GREEN `e0c422acfa29f45bf1655abcebcf869230106400` — `fix(card-1614): withhold Linux auto-seal and recover dead hosts`
- This report is a later docs commit on the same branch. Product behavior is the green commit.

Range `7a2cd795590c1836ca213c3894cf17fab780e7d6..e0c422acfa29f45bf1655abcebcf869230106400`: 22 files, +1301/−73. No `*.csproj` or `*.props`.

`PtyHostOptions.cs` and `ModernConPtyConnection.cs` are not in the range. `PtyHostOptions.LingerTtl` stays 24h.

## Behavior

- Linux automatic seal is withheld. `HostSession.GetCustodyAsync` escalates on exit only for a non-Linux backend (`linux ? seal : seal || Status == Exited`). Ordinary Linux observation uses `PtyAgentRunner.ObserveLinuxAccountingAsync` and does not call `Terminate`. Explicit Linux release still uses `SealAndObserve` (that path does kill). Windows `SealAndObserve` accounting is unchanged.
- A natural zero is accepted only after placement, a matching namespace, tracking, native-start revision 1, and a root that is gone, then a fenced re-read. Root exit with live members stays Draining. A missing tree, a changed namespace, a permission failure, or a malformed PID is Unknown. An empty `cgroup.procs` file remains observed zero. `LinuxCustodyOperations.ReadTreePids` no longer returns `[]` when the procs file is absent; that failure is `verification_custody_tree_unreadable`.
- `linux-release-disposition.json` is release evidence, not a receipt. It is written only when an explicit release observed zero, a receipt cannot be persisted, and `runner-seal.json` matches the store, binding, namespace, host, and container. The runner then stops its observer without receipt-guarded shutdown and the host may arm one fresh linger at the existing TTL (tests use a short TTL). The default linger TTL was not changed.
- Dead-host recovery (`RunnerCustodyLedger.ReleaseDeadHost`) runs only for an explicit Linux release when the host session is gone and the runner seal, namespace record, and container id match. It always returns Unknown. It kills only when the recorded host is gone; a live host, a mismatched record, a missing seal, or an ordinary read does not kill. One request performs one helper attempt. A second explicit release may kill the same container again. Identity failure returns `custody_observer_unavailable` and does not kill.
- Draining or Unknown for 30 minutes writes one warning (`custody-unresolved execution {ExecutionId} container {ContainerId}`). That warning does not kill. Tracking and Exited do not start the clock.
- A live host that carries a verification binding and cannot be adopted is not killed. `SessionRunnerRuntime` already skipped `KillPidBestEffort` when `VerificationBinding` is set. The false “child is gone too” comment was rewritten. No takeover was added. The operator procedure is in `docs/orchestration-loop.md`, `docs/session-runtime-invariants.md`, `docs/testing-and-build.md`, and `docs/adr/0002-modern-conpty-backend.md`.
- `VerificationCleanupService` remains the named owner. The orchestrator invokes it after Succeeded, Failed, or Canceled and retries residue. It is not a new scheduler. A successful kill is not a receipt and is not a source-clean verdict.
- The three-argument `HostCustodyJournal.ObserveAsync` was removed. Its one caller, `VerificationCustodyStoreTests.Only_sealed_no_native_intent_failure_can_produce_never_started`, now passes `seal: true`. Failure without native intent still writes NeverStarted and does not terminate.

## Disposition table

Covered on this host by CP-38 (four `RunnerCustodyReleaseTests`, all passed) and by the host classes in CP-47 that actually ran.

| Situation | Result |
|---|---|
| Ordinary Linux read, root exited, descendants live | Draining. No producer seal. No kill. |
| Ordinary Linux read, tree missing or namespace changed | Unknown. Never an empty list. |
| Natural completion after placement and a fenced zero | Receipt with `TerminationSucceeded` absent. Linger stays withheld until runner shutdown. |
| Explicit Linux release that can persist a receipt | Receipt. Disposition is not also written. |
| Explicit Linux release, zero observed, receipt impossible, runner seal present | `linux-release-disposition.json`. Host may arm linger. Runner stops polling and does not shut down on a receipt. State stays Unknown. |
| Explicit release, host gone, records match | Unknown. One bounded kill. Zero descendants: `released_unknown`. Descendants remain: `descendants_remain`. Unreadable accounting: `accounting_unreadable`. |
| Explicit release, host still present or records mismatch | Unknown. No kill. |
| Ordinary read | No recovery kill. No runner seal invented by the read. |
| Draining or Unknown for 30 minutes | One warning. No kill. |
| Live unadopted host with a verification binding | Stays alive. Operator stop, then cleanup again. |
| Windows tracked linger | Unchanged. The new witness skips on Linux. |

## Checkpoints

Manifest `.antiphon/checkpoints-c1614-b1.yaml` is gitignored. `run --plan` was not used for the timed runs; the patched manifest was passed positionally so the importer’s 15-minute floor did not replace the row deadlines. The checkpoint `run` was not wrapped in a second build slot. `TEMP=/tmp`. No row exceeded its estimate by more than 25%. No slot timeout (exit 4). `unlisted: none` on both runs. Project `bin-c1614*` directories were deleted by literal path after B3. Checkpoint evidence copies under `.antiphon/checkpoints/` were left in place. Cgroup trees under `/sys/fs/cgroup/{pids,freezer}/antiphon-custody` were not deleted.

### B1 RED — run `20261030-034254-8410`

Commit `6139f090d4ec77bfeb6c6ea6212124ddc0164911`. Tool exit 1. Wall 3m00s (180.197s). Builds 22. Evidence `.antiphon/checkpoints/20261030-034254-8410/report.md`.

| Row | Result | Counts | Wall | Classification |
|---|---|---|---|---|
| CP-8 | failed | executed 1, failed 1 | 5.811s | FIXTURE. `DirectoryNotFoundException` on `/run/antiphon-custody/namespace` in dispose. Not a semantic seal failure. |
| CP-9 | failed | executed 2, failed 2 | 5.969s | FIXTURE. Same missing namespace marker, both methods. |
| CP-34 | failed | executed 1, failed 1 | 3.934s | Valid RED. `ReadTreePids` did not throw `IOException`. |
| CP-36 | failed | executed 1, failed 1 | 4.331s | Valid RED. No `custody-unresolved` warning. |
| CP-39 | failed | executed 3, failed 3 | 6.599s | Valid RED. Recovery kill list empty; released-unknown poll did not stop. |

CP-8 and CP-9 were not re-run after the fix. Dispose still calls `CurrentNamespace()`, and `/run/antiphon-custody` does not exist here (root-owned `/run`, uid 1654).

### B3 GREEN — run `20261030-040436-5ea0`

Commit `e0c422acfa29f45bf1655abcebcf869230106400`. Tool exit 0. Wall 5m19s (319.193s). Start 2026-10-30T04:04:34Z, end 2026-10-30T04:10:02Z. Builds 22. Source clean, `buildSource=verified` on the test rows. Evidence `.antiphon/checkpoints/20261030-040436-5ea0/report.md`.

| Row | Build | Counts | Wall | Phase build |
|---|---|---|---|---|
| CP-51 | n/a | `test-rc-platform.ps1 -Case C1323_PlatformPolicy` exit 0 | 22.986s | script |
| CP-38 | ok | executed 4, passed 4, failed 0, skipped 0 | 4.840s | 75.537s |
| CP-46 | ok | executed 29, passed 29, failed 0, skipped 0 | 5.471s | 36.586s |
| CP-47 | ok | executed 29, passed 29, failed 0, skipped 15 | 4.861s | 39.022s |
| CP-48 | reused | executed 1, passed 1, failed 0, skipped 0 | 4.604s | 0s |
| CP-49 | reused | executed 1, passed 1, failed 0, skipped 0 | 5.153s | 0s |

CP-46’s plan text said 27. The manifest minimum is a floor. 29 passed is the closed-list result (containment gained the missing-tree method; the production-reader assertion change is the same method). CP-47’s 15 skips include the Windows linger witness, which calls `RequireModern()` and does not run on this Linux host. Every selected test in CP-47 that ran passed. CP-48 and CP-49 are the classification guard, one test each.

Rows not run, and not claimed: CP-42, CP-43, and the Windows B3 rows.

## Native rows owed

This host has `/usr/local/bin/antiphon-custody-enter` (`sudo -n … --probe` prints `C604_CUSTODY_PROBE_OK enter`, exit 0). `/usr/local/bin/antiphon-custody-kill --probe` exits 1 (`id: unbound variable` at the installed script’s line 91). That installed file is not `docker/session-runner-grok/antiphon-custody-kill.sh`. It was not replaced. `/run/antiphon-custody/namespace` is absent, so `RequireNativeLifecycle()` skips before the kill probe matters. Do not treat a skip as the witness. Do not claim these red or green.

`$tool` is the candidate `Antiphon.Checkpoints.dll` on a host where the namespace marker and both probes succeed:

- NP-13: `LinuxRunnerCustodyLifecycleTests/(C1614_Root_exit_read_does_not_seal_live_descendant*)|(C1614_Natural_completion_accepts_exact_receipt_and_allows_shutdown*)` min 2, output `bin-c1614-b-red-runner/`.
- NP-34: `LinuxCgroupCustodyTests/(C1614_Failed_tracking_explicit_release_terminates_without_receipt*)|(C1614_Missing_tree_never_finalizes_naturally*)` min 2, output `bin-c1614-prereq-host/`.
- NP-37: `LinuxRunnerCustodyLifecycleTests/C1614_Lost_host_explicit_release_kills_but_stays_unknown` min 1, `--no-build`, output `bin-c1614-b-red-runner/`.
- NP-14: class `LinuxCgroupCustodyTests/*` min 13, output `bin-c1614-v1-host/`.
- NP-16: class `LinuxRunnerCustodyLifecycleTests/*` min 5, output `bin-c1614-v1-runner/`. The class must end with exactly five methods: two slice-A methods plus three that call `RequireNativeLifecycle()` first.

NP-15 and NP-17 are unprovisioned cgroup v2 and stay refused by the v1-only gate.

Known weak bodies, left as written so production was not contorted: `C1614_Missing_tree_never_finalizes_naturally` does not remove a root-owned cgroup directory. `C1614_Failed_tracking_explicit_release_terminates_without_receipt` does not plant `runner-seal.json`. On a qualified image a tracked explicit seal that can persist a receipt will write that receipt, and the committed assertions (null receipt, Unknown, `ExitRequested` after 1200ms with a 400ms linger) can fail even when the production rule is right. Disposition is written only when a receipt is impossible.

## Evidence and land check

`pwsh -NoProfile -File scripts/check-evidence-diff.ps1 -BaseRef 7a2cd795590c1836ca213c3894cf17fab780e7d6 -HeadRef e0c422acfa29f45bf1655abcebcf869230106400` exited 0. `EVIDENCE result commits=2 entries=0 violations=0`. That range is the two product commits, before this report file.

`git fetch origin master` then `git merge-tree --write-tree origin/master e0c422acfa29f45bf1655abcebcf869230106400` printed tree `29c30509e80851f1d2d225ad52d1487091eeeea2` and exited 1. Not merged. Worktree was not modified.

Conflicts:

- `.antiphon/task-af2b78c8.md` add/add (not this slice)
- `docs/investigations/2026-10-25-card-1584-e2e-launch-bounds.md` add/add (not this slice)
- `scripts/test-rc-platform.ps1` content (this slice, RED commit)
- `server/Application/Services/CapacityRecoveryService.cs` content (not this slice)
- `tests/Antiphon.Tests/Application/CapacityLockOrderTests.cs` add/add (not this slice)
- `tests/test-execution-policy.json` content (this slice, RED commit)

Auto-merged without a conflict marker, including the files this slice edited: `docs/orchestration-loop.md`, `docs/session-runtime-invariants.md`, `docs/testing-and-build.md`, `src/Antiphon.SessionRunner/SessionRunnerRuntime.cs`, `tests/Antiphon.PtyHost.Tests/HostCustodyTests.cs`, plus `scripts/c590-remote.sh`, `AgentControlService.cs`, `AgentSupervisorService.cs`, `CapacityRecoveryAcceptanceTests.cs`, and `tests/Antiphon.Tests/slow-tests-allowlist.txt`.

## Deviations

- B2 / CP-42, the all-backend linger-withhold control, was not committed. This worker cannot execute Windows and would not have observed a Windows RED. The Windows witness `HostCustodyTests.C1614_Windows_tracked_linger_releases_job` is committed and skips here.
- CP-8 and CP-9 are fixture failures, not seal witnesses. No skip was added to the existing root-exit test.
- The runner ordinary-read half of CP-8 was not added to the host test. That would need a new project reference. It remains NP-13.
- CP-46 executed 29 tests. The plan sentence said 27. The minimum is a floor.
- Three-argument `ObserveAsync` was removed; the single test caller passes `seal: true`.
- Production recovery no longer passes a null ledger recovery. A null `RunnerCustodyLedger` recovery still does not kill. The runtime lazily uses `LinuxInstalledCustodyRecovery`, which calls public `LinuxCgroupContainment` so SessionRunner does not need InternalsVisibleTo.
- Policy: `tests/test-execution-policy.json` schema 4. The HostCustody Linux exclusion lists both `Inbox_is_unsupported_and_never_becomes_no_start_proof` and `C1614_Windows_tracked_linger_releases_job`. `scripts/test-rc-platform.ps1` requires `$c1614.Count -eq 3`. `RunnerCustodyReleaseTests` is not Windows-excluded and was not added to the slow allowlist.

## Verification split (D-2)

Product behavior for the Linux portable contract is in the green commit and is covered by B3. Verification of that portable closed list is complete (CP-38, CP-46, CP-47, CP-48, CP-49, CP-51). Native execution is absent and is an incomplete gate for a qualified image. It is listed above as owed commands. It was not relabeled green. Docs in the green commit are behavior-bearing (cleanup ownership, no global “cleanup never kills”, a kill is not a receipt). They are not an advisory-only follow-up.

--- next stage ---
next: review
handoff: Review the green commit `e0c422acfa29f45bf1655abcebcf869230106400` and this report commit. Do not treat NP-13, NP-34, NP-37, NP-14, or NP-16 as executed. Do not authorize land while `git merge-tree --write-tree origin/master HEAD` still exits 1. Windows linger and `LinuxCustodyQualified=false` stay as they are.
artifact: .antiphon/task-2bfb3eae.md
