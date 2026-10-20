# CARD-1327: Checkpoint output cleanup spawns one git per deleted entry

Confirmed by reconstructing the current call path and the stored Windows Review transcript: successful output deletion repeats a synchronous Git preflight for every file **and directory**. Each logical Git request starts two native Git processes, including its configuration probe. This remains present after CARD-1358, CARD-1368 and CARD-1369. No builds or tests were run for this investigation.

## Source and evidence identities

- Investigation task: `e6123466-2571-4200-a369-064dfe641fa8`, assigned branch `feat/card-task-e6123466`.
- Inspected checkout HEAD and freshly fetched `origin/master`: `792dd4bc5fd13fcca45aa74e30e75320c8eddb1a`. File:line references below refer to that commit.
- Antiphon board: `8988ca03-7414-47ad-b0b6-51556c701703`; CARD-1327 row: `5d3019c8-00b8-43ea-9b33-2b8a0e688050`, concurrency token `f396e087-66d0-4e69-ac21-7c06ddc3da3c`, zero revisions when read with `pwsh -NoProfile -File scripts/card.ps1 get CARD-1327 -Board Antiphon`.
- Original Review task: `4c9cd695-21cf-4e73-9691-3f43a8c21096`; session: `e79368db-f2e1-4751-b5ff-dc1ff7a4e8e3`; reviewed source: `cc022d9122c2d8ef5407489e112b8e62c330f866`.
- Stored evidence retrieved through `GET /api/agent-tasks/4c9cd695` and `GET /api/sessions/e79368db-f2e1-4751-b5ff-dc1ff7a4e8e3/transcript?since=0` (120 entries, last sequence 120). Local copies are ignored: `.antiphon/c1327-investigation-evidence/review.json` (SHA-256 `cbd44ed743f56bc393113aa277010d049057425ad5b5bed90a81e4609e3de7fa`) and `transcript.json` (SHA-256 `70c1e3b4b2dedbfc849fe3dbf11ee283d5099af548d21dbff91f5bf08c1828f9`). Essential observations are transcribed below so the report survives mirror retirement.

The inspected master contains CARD-1358's progress-rearmed reader (`a855e7c942aa3a7a79ecabdda825e81cef466ed8`), CARD-1368's host-output adoption and streaming stamps (`33c8096ed50a8b60f49f10a1d901cb4c1dc9446a`, `7c20ea9eb98a35febc2f915cfd15203ec6d333ee`), and CARD-1369's retained-handle size recheck (`ae58707e80f189f06bf69ca16c4d228f693409a4`). In the final source, adoption is at `OwnedOutputLedger.cs:43` and `RowRunner.cs:271`; progress and final-size checks are at `CheckpointFileReader.cs:107` and `:113`. None removes the per-entry Git work below.

## Mechanism and exact counts

All paths in this table are repository-relative.

| Boundary | Source | Observed mechanism |
|---|---|---|
| Run cleanup | `tools/Antiphon.Checkpoints/CheckpointApp.cs:360` | Unless `KeepOutputs`, calls `CleanOwnedOutputs` with the run exit code and `CleanOnRed`. |
| Red retention | `tools/Antiphon.Checkpoints/Cleanup/OutputCleanup.cs:41` | Nonzero exit with `cleanOnRed=false` returns immediately, before any deletion Git request. |
| Successful root admission | `tools/Antiphon.Checkpoints/Cleanup/OutputCleanup.cs:95` | Calls delete `Refusal`, ledger `Check`, then delete `Apply` with another ledger `Check` callback. |
| Root rechecks | `src/Antiphon.HostCleanup/Checkpoints/SafeDelete.cs:75`, `:82`, `:95` | `ApplyCore` evaluates twice, invokes the inventory callback, then calls ledger `Remove`. |
| Per-entry repetition | `src/Antiphon.HostCleanup/Checkpoints/OwnedOutputLedger.cs:86`, `:89`, `:93`, `:100` | `Remove` checks the root, inventories descendants plus the ledger, then preflights each file and each directory immediately before its content check and unlink/nonrecursive removal. The root's final removal has no additional preflight. |
| Preflight implementation | `src/Antiphon.HostCleanup/Checkpoints/SafeWrite.cs:131`, `:140` | Walks link components, then calls `CheckpointDelete.PreflightRoots`. |
| Git request | `src/Antiphon.HostCleanup/Checkpoints/SafeDelete.cs:287`, `:299`, `:308` | Builds one `git -C <owner> ls-files -z -- <paths...>` request. The API accepts multiple roots, but the deletion loops supply one path. `FindOwner` at `:318` walks the filesystem without spawning Git. |
| Native runner | `src/Antiphon.HostCleanup/Checkpoints/SafeDelete.cs:358`; `CheckpointCleanupDefaults.cs:17`, `:23` | Sends the request synchronously to `CheckpointProcessRunner` with `git=true`. |
| Two native launches per request | `scripts/lib/CheckpointProcessRunner.cs:62`, `:68`, `:79`, `:129` | First runs `git config --null --get-regexp …` to disable named drivers, waits, then launches the requested Git command. Both reach `process.Start()`. Linux launches through `setsid`; Windows launches Git directly. |

Let **E** be all descendants actually deleted from one output root: ordinary files, child directories, owner/build markers, **and** `.checkpoint-output-ledger.json`; exclude the root itself. Assume an unchanged, fully owned tree inside a Git worktree, successful Git replies, no retries, no dry run and no refusal.

- `CheckpointOutputLedger.Remove`: **E + 1 logical Git requests**, all `ls-files`; **2E + 2 native Git launches**.
- Full `CheckpointOutputCleanup.CleanOwnedOutputs`, one root: **E + 9 logical requests**, **2E + 18 native launches**.
- Fixed full-path overhead: three `Evaluate` calls, each doing `rev-parse` plus `ls-files` (`SafeDelete.cs:170`, `:265`, `:276`), and three ledger `Check` calls, each doing one `ls-files` (`OwnedOutputLedger.cs:70`). Thus six plus three requests before the E per-entry requests.
- If counting `Inventory(root).Count` instead, that inventory excludes the ledger (`OwnedOutputLedger.cs:119`), so E is that count plus one.

| E, including markers and ledger | `Remove` logical requests | `Remove` native launches | Full cleanup logical requests | Full cleanup native launches |
|---:|---:|---:|---:|---:|
| 500 | 501 | 1,002 | 509 | 1,018 |
| 1,000 | 1,001 | 2,002 | 1,009 | 2,018 |
| 2,500 | 2,501 | 5,002 | 2,509 | 5,018 |

These are exact **source-derived counts for those E values**, not a newly recorded process trace. The card's approximately 2,500-entry workload therefore implies approximately 5,000 native launches, not 2,500. The same extra config launch already existed at the historical reviewed SHA (`scripts/lib/CheckpointProcessRunner.cs:33`, `:39`, `:50` at `cc022d9122c2d8ef5407489e112b8e62c330f866`).

The brief's “retained red row” needs qualification. A red run retained by default makes **zero deletion Git requests** in `CleanOwnedOutputs`. The large count applies to a green cleanup, or `CleanOnRed` while the original process still owns the creation records. A fresh process cannot authorize deletion from an old disk ledger alone: `Check` returns `output-memory-authority-missing` at `OwnedOutputLedger.cs:73`. For an otherwise eligible untracked root, that full-path refusal requires three logical requests/six native launches, then stops before the per-entry loops. Build, sealing, reporting and unrelated Git work are excluded from all these counts.

## Stored Windows witness and cost

Run `20261017-152755-0e49` used `C:\Antiphon\worktrees\card-task-4c9cd695`, output `tests\Antiphon.Tests\bin-c1318rv`, executor PID 26868. The following are normalized transcript ToolResult rows from the session above; timestamps are UTC unless the sampled Windows field is explicitly local time.

| Transcript sequence | Stored observation | What it establishes |
|---:|---|---|
| 75 / 78 | Executor log: CP-2 slot released at `2026-10-17T14:33:56.2532797+00:00`; both rows green. Earlier CP-1 acquisition returned `build_slot_memory_floor` 409 twice, then acquired after 30 s. | Tests had finished; host memory pressure was observed. The slot wait is separate from cleanup time. |
| 78, received `14:38:24.516Z` | Root still present, 759 regular files; owner and ledger present; executor CPU total `107.78125` s. | Cleanup was still incomplete over four minutes after the rows finished. |
| 80 | Sampled UTC `14:38:41`; 634 files, 354 directories including root; executor CPU total `109.03125` s. | The file count was decreasing, while executor CPU changed little. Counts were separate live enumerations, not an atomic inventory. |
| 85 | Sampled UTC `14:39:08`; 442 files; child PID 26976, `Name: git.exe`, parent selected by `ParentProcessId=26868`; command line empty. Host sample: 8,305 MiB free, CPU `52.4674818978717` percent. | 192 files disappeared in 27 s: **7.11 files/s**. A Git child was present. Cleanup remained incomplete **311.747 s** after the last row's release. |
| 91, received `14:41:06.970Z` | Report: 5 tests passed, output deleted, `GREEN exit=0`; concurrent `wait` command also reports tool-copy cleanup `IOException`, command exit 2. | Bin-output completion was visible by this point, 430.717 s after the row release. This includes reporting and tool-copy work, not just bin deletion. |
| 97 | Original run output: tool-copy cleanup `Removed`, output deleted, `GREEN exit=0`, command `exit=0`; validator `CHECKPOINT SOURCE VALID … rows=2`, exit 0; `cleanedOutputs=true`, `cleanupRefusals=[]`, bin root absent. | Original run completed successfully. The concurrent wait's exit 2 is preserved as a separate historical outcome. |

The card and stored Review result estimate the post-row deletion at about 5.5 minutes and the initial tree at roughly 2,500 entries. The card says completion was about `14:39:40Z`; no exact completion timestamp is present in the retrieved raw output. The stronger direct observation is that 442 files remained at `14:39:08Z`, and completion was reported by `14:41:06.970Z`. This supports a **roughly six-minute post-row delay on that loaded Windows host**, without claiming every second was spent in Git.

Do not use the historical report's `wallSeconds=348.8087013` as the cleanup duration. `CheckpointApp` builds the report model before cleanup; its `WallSeconds` is assigned when that model is constructed (`CheckpointApp.cs:283`, `:360`, `:743`). The transcript's live counts and timestamps establish the post-row delay separately.

For planning scale only, applying the card's approximately 1–7 entries/s range gives 500 entries about **1.2–8.3 minutes**, 1,000 about **2.4–16.7 minutes**, and 2,500 about **6–42 minutes** if that rate persisted. This is a sensitivity range, not a measured full-run distribution or deadline guarantee. The concrete historical run was near the fast end overall; the card reports a quieter Code run (`20261017-150333-ab9b`) cleaned in under a minute. That quieter comparison is a stored claim, not an independently retrieved timing series here.

Attribution: repeated synchronous process startup/wait work scales with entry count and explains a largely idle executor with ongoing Git children. Content hashing, link/metadata probes, filesystem latency, filters and host scheduling also contribute; there is no per-child duration trace to isolate their shares.

Essential unedited receipt lines from transcript sequence 91, also preserved in the task's stored result:

```text
CHECKPOINT CP-1 commit=cc022d9122c2d8ef5407489e112b8e62c330f866 build=ok filter=/*/*/OutputCleanupTests/(windows_copied_old_change_time_is_adopted*)|(windows_set_clock_and_foreign_dacl_are_not_adopted*)|(windows_sharing_violation_retries_then_adopts*)|(windows_seal_docs_match_the_landed_adoption*) executed=4 passed=4 failed=0 skipped=0 trx=C:\Antiphon\worktrees\card-task-4c9cd695\.antiphon\checkpoints\20261017-152755-0e49\rows\CP-1\run.trx slot=granted waited=30s dirty=0 source=cc022d9122c2d8ef5407489e112b8e62c330f866 sourceState=clean buildSource=verified
CHECKPOINT CP-2 commit=cc022d9122c2d8ef5407489e112b8e62c330f866 build=reused filter=/*/*/HostCleanupCheckpointCustodyTests/Stalled_receipt_read_is_retried_once* executed=1 passed=1 failed=0 skipped=0 trx=C:\Antiphon\worktrees\card-task-4c9cd695\.antiphon\checkpoints\20261017-152755-0e49\rows\CP-2\run.trx slot=granted waited=0s dirty=0 source=cc022d9122c2d8ef5407489e112b8e62c330f866 sourceState=clean buildSource=verified
outputs: deleted bin-c1318rv/
verdict: GREEN exit=0
```

## Existing safety boundary

Git does **not** establish ownership and the cleanup does not use `check-ignore`. `ls-files` output refuses tracked content; nonzero, unavailable or timed-out Git refuses (`SafeDelete.cs:308`, `:315`, `:360`, `:372`). A creation record and a matching current content digest are also required (`SafeWrite.cs:121`); unknown additions or changed bytes fail the initial ledger check or the immediate per-entry check (`OwnedOutputLedger.cs:75`, `:94`, `:100`). Untracked but unowned content is therefore retained.

Outer deletion checks containment, allowed isolated names, the created-path inventory, links, nested repositories and the bounded tree walk (`SafeDelete.cs:146`, `:213`). Directory deletion is nonrecursive (`OwnedOutputLedger.cs:101`, `:103`), preserving an unrecorded late addition by refusing to remove its nonempty parent. The creation registry is process-local; disk inventory is diagnostic, not deletion authority.

The current code obtains fresh Git information at each per-entry preflight. A tracking change completed before that entry's check can therefore be detected. There is still a non-atomic interval between check and unlink. No evidence here establishes that replacing those checks with an earlier snapshot preserves the same concurrent-index behavior. That is a safety question for Plan, not a proven equivalent optimization.

### Checkpoints

Evidence inventory for the next stage, **not an executable Code checkpoint manifest**. The Investigate stage prohibits fix design; no new test was added or run, and no current green result is claimed. The requested implementation plan and closed execution table belong to Plan.

| Item | Witness or existing narrow filter | Evidence/status |
|---|---|---|
| Red performance witness | Successful `Remove` of E=500 recorded descendants entails 501 logical requests / 1,002 native Git launches; full cleanup entails 509 / 1,018. | Reconstructed from the call path above; stored native child observation at transcript sequence 85. There is no existing successful-cleanup spawn-count assertion in the inspected custody tests. `VirtualCheckpointIo.GitRequests` at `tests/Antiphon.Tests/Infrastructure/HostCleanupCheckpointFixture.cs:171`, `:289` records logical requests only, not the config child. |
| Existing safety regression selection | `/*/*/HostCleanupCheckpointCustodyTests*/(Fresh_owned_output_can_be_removed*)\|(Late_or_changed_entries_are_retained*)\|(Individual_deletes_preserve_late_additions*)\|(Tracked_or_nested_repository_paths_are_retained*)\|(Git_failure_and_deadline_refuse_before_mutation*)` | Five existing methods at `HostCleanupCheckpointCustodyTests.cs:391`, `:281`, `:296`, `:321`, `:344`; inspect positive deletion, changed/late content, final link checks/nonrecursive removal, tracked/nested content and Git failure. **Not run.** Plan must decide sufficiency and add the performance witness before this can be a green acceptance gate. |

Caller-requested Code budget: **30–60 minutes**, to be assessed by Plan against the required safety proof and closed checkpoints. This investigation does not certify that budget or spend it.

## Remaining uncertainties

1. The original initial inventory is reported as approximately 2,500 entries; it was not recovered as an exact ledger roster. The raw Windows run directory is not reachable from this runner mirror. An exact initial ledger plus a native process-start trace would turn the reconstructed total into a measured total.
2. No tests/builds or fresh benchmark ran. Current-master operation counts are a source reconstruction; historical Windows evidence establishes the real slow cleanup behavior. Transient spawn retries can add attempts and failures can stop the loop early.
3. The exact bin-deletion end time and process-start versus hashing/I/O costs are not separable from the stored samples. A timestamped start/exit trace covering cleanup would resolve that.
4. A batched admission's equivalence under concurrent index changes remains unproved. The existing tests cited above cover several retention boundaries, not a full native process-count or index-race proof.

## Not done, noted

Plan may assess batching the existing `ls-files` admission while retaining per-entry ownership/content/link checks and fail-closed Git behavior; no fix or verification design was produced here.

--- next stage ---
next: plan
handoff: Confirmed on master 792dd4bc5fd13fcca45aa74e30e75320c8eddb1a: cleanup starts two native Git children per deleted descendant, plus fixed checks; about 5,000 for the reported 2,500-entry root. Plan the 30–60 minute change and red/green checkpoint manifest, preserving unknown/tracked retention and addressing concurrent index changes. No tests ran.
artifact: docs/investigations/2026-10-20-card-1327-cleanup-git-per-entry.md
