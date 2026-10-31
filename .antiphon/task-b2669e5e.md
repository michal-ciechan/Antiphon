# C459 busy-caller queued-outcome Debug result

The Windows C459 failure was a plain 60-second wait. The busy-caller gate now renews on completed protocol-git records, up to the existing 600-second cap, and a timeout names the recipient hold, the last Git record and a safe log tail. The C467 and C488 quiet-window stalls are a separate family and stay unproven. Native Windows reruns remain.

## Cause

Windows Full rc run d47848cc, candidate cde8def41, failed `C459_AdmittedWorkspaceInputReachesRecipient(busy-then-released)` in 1 min 29.7 s (31 Oct 10:13:08–10:14:38 UTC). `ProgressAwareWait.UntilAsync` threw `TimeoutException` with the message exactly `busy caller has queued outcome` (`tests/Shared/ProgressAwareWait.cs` line 36). That line is the path taken when the progress probe is null, so the wait was one plain 60-second deadline. The ~90-second test duration matches setup plus that deadline.

The predicate is an Outcome row for the task whose `QueueMessageId` is set. The wait starts after `ReleaseExecutionAsync`, which is the land that writes `protocol-git-*.json` records. Sibling `C467_V23_BusyCallerDoesNotBlockAnotherLand` and C498 already renew the same predicate on those records. C459 did not.

There is no Windows protocol-git file or fixture snapshot for this C459 failure, so this report does not assign progress 46 to it. If the land had already stopped emitting Git records before `QueueMessageId` was set, the new wait still fails one quiet window later. The timeout text then names the recipient hold and release files, the awaited condition, the last Git command and the safe log category in `queued-outcome-<task>.txt`.

Host load is a weak explanation for the family. Desktop land-verification builds ran at 05:16–05:35, 07:12–07:35, 09:24–09:43 and 09:53–10:2x UTC. C467 (06:37–06:50) and C488 (08:51–08:55) fall outside those windows. Only C459 (10:13–10:14) overlaps one.

## Family verdict

Three separate outcomes, from the Windows messages and the wait each test actually called:

1. `C488_ApprovalPollingCannotConfirm` is an independent selector defect. It is already fixed on this line (rc top-up carries afa8b24e8; this branch has the equivalent). This task does not reopen it. The native rerun is still owed.
2. C467 V29 batches, V30 `receipt-before-save`, and `C488_ApprovalQueueInsertCrashReusesRow` share the progress-aware protocol-git wait. They timed out quiet at progress 46 or 47. The stall (verification, build-slot admission, or native delivery) is unproven. Diagnostics for that family are already on this line. The 60-second quiet window and 600-second cap stay as they are. This task does not claim those failures fixed. A Linux fake-clock sample is one sample, and there is no usable Windows p95, so the window was not widened.
3. C459 is distinct. Its exception text had no quiet or progress suffix, which is the no-probe shape. The fix makes that one wait protocol-aware.

## What changed

Assigned branch `feat/card-task-b2669e5e`, fast-forward from rc tip `fc064a68247b0186918dfb1f04465e6be0475389`. No rebase, amend, reset or force-push.

- `a07a3a2738d5527aec5fcfaeb8923e9fd9b821bb` — red. The busy-caller gate was a plain wait, and two fake-clock witnesses require 700 protocol-git records before success and require the timeout to name the recipient and the last Git record. Census rows went to 183 total / 176 required. The probe class price stays the provisional 10 seconds per case (6 cases, 60 seconds).
- `e9ae46eb64ffd58c0d5d3c7df2327cdb586ca771` — the gate calls `UntilProtocolAsync`. `ReceiptTimeoutAsync` accepts the `queued-outcome` prefix. The diagnostic records `recipientBusyHeld`, `recipientBusyReleased` and `executionReleased` from `caller-busy.held`, `caller-busy.release` and `execute.release`. `docs/testing-and-build.md` records the same 60-second quiet window and 600-second cap.
- `d7dc896ebe6c2007277a202712fe6df4494d7ba8` — the CARD-1412 source pin now requires five forwarded arguments: predicate, evidence, quiet seconds, `ProtocolProgress`, and `clock: clock`. The dispatch helper stays at four arguments.

`WorktreeRetirementDeliveryE2ETests` busy-then-released still does, in order: `WaitForBusyCallerQueuedOutcomeAsync`, `ReleaseBusyAsync`, `ReceiptAsync`, `AssertRemoteAsync`, `AssertOnePromptAsync`.

An earlier probe run is not red evidence. Its renewal predicate became true after eight protocol-git files, inside the plain 60-second deadline, so the product defect was not exercised. The credited red run requires `ProtocolProgress() >= 700`.

## Protective intent kept

- The queued Outcome with `QueueMessageId` set is still required before `ReleaseBusyAsync`.
- `ReceiptAsync` is unchanged: confirmed notification, queue row, confirming UserPrompt, body equality, `PromptSubmissionMatch` complete and confirmed, sequence above the baseline.
- `AssertRemoteAsync` still checks the remote ancestor.
- `AssertOnePromptAsync` still requires exactly one native prompt and one transcript prompt.
- A land that stops emitting protocol-git records still fails one 60-second quiet window later.
- The 60-second quiet window, the 600-second cap, and every production deadline are unchanged. The Linux renewal witness (2.179 seconds of wall time for 700 virtual steps) is one sample, not a Windows p95.

## Verification

Linux builds used `TEMP=/tmp`, `UseAppHost=false`, and isolated `bin-c459-e2e/` or `bin-c459-tests/`. Every build and TUnit run went through `scripts/build-slot.ps1`. Native C459, C467 and C488 cannot run here: `InitializeAsync` requires Windows, ConPTY redistributables, staged `fakegrok.exe` and a Windows runner executable.

| Run | Source | Result | Duration | Slot |
|---|---|---|---|---|
| E2E red probes | a07a3a273 | 2 executed, 0 passed, 2 failed | 2.214s | lease 5a5c4106, waited 15s, held 4s |
| E2E green probes | e9ae46eb6 | 6 executed, 6 passed, 0 failed | 5.653s | lease ef30218c, waited 391s, held 11s |
| Portable, before the pin | e9ae46eb6 | 20 executed, 19 passed, 1 failed | 2m 06s | lease 14536385, waited 60s, held 132s |
| Portable, after the pin | d7dc896eb | 20 executed, 20 passed, 0 failed | 1m 24s | lease 2865b204, waited 60s, held 88s |

The red failures were the bare message `busy caller has queued outcome` (renewal 0.952s, silent diagnostic 0.163s). Green probe times: renewal 2.179s, silent diagnostic 0.034s, plus the four existing probes. The portable failure was `Renewing_helpers_forward_the_protocol_probe_to_the_shared_wait`: the inner call had five arguments and the pin required four. After `d7dc896eb` the same twenty tests passed, including ProgressAwareWaitTests (8), LandDeliveryWaitRoutingTests (4), RcRosterRepairTests (3) and C1514LandingSplitBudgetTests (5: C1404, C1577, C1584, C1410, G2865). Zero skipped. A zero-test run is not in these counts.

E2E builds, 0 errors: red lease 622a8ca8 waited 75s, 39 warnings; incremental red2 lease de74d6f7 waited 15s, 5 warnings; green lease bb2db1e3 waited 15s, 39 warnings. Antiphon.Tests builds, 0 errors, 1402 warnings: lease 686b755a waited 45s held 245s; pin rebuild lease ee237498 waited 0s held 312s. Warnings are the existing nullable, obsolete and TUnit0015 set.

Land-graph `-Check` against `tests/Antiphon.Tests/bin-c459-tests/land-graph-audit/Antiphon.LandGraphAudit.dll` exited 0 in 31.39s. Digest `E7C6EDAFA83B544A42E186899267267FD73E6FDD2FB7DD2DF36C233F2C405806`, count=69, both passes and the canary approved. No `-Update`. The evaluator DLL came from the slotted test build; the check script itself was not given a second slot.

`scripts/check-evidence-diff.ps1` over `fc064a68247b0186918dfb1f04465e6be0475389..d7dc896eb` exited 0: 3 commits, 0 evidence entries, 0 violations. The repository was not reported shallow. `git diff --check` on the worktree and on that range exited 0.

Fetched `origin/master` is `2051d327123a5e98524b4bd1ecf31c6e516bdb4e`. `git merge-tree --write-tree origin/master HEAD` exits 1 with 48 conflict paths. The same command against the unchanged base `fc064a682` also exits 1 with those same 48 paths, so this branch adds no conflict path. No master landing is claimed and no merge was made. Fetched `origin/rc/2026-10-27` is still `fc064a68247b0186918dfb1f04465e6be0475389`, an ancestor of HEAD. Merge-tree against it exits 0 and the result tree equals `HEAD^{tree}` `d60f1ae5157bfcb102236b68734ff96bc02848d3`.

Cleanup inventoried 52 `bin-c459-e2e` and `bin-c459-tests` roots, found no directory links, deleted files and empty directories by literal path, and left zero of those roots. Inventory: `/tmp/c459-output-inventory.txt`. TRX stays under `/tmp/task-b2669e5e-*`. Nothing under `C:\Antiphon` or qualification run d47848cc was touched.

## Native reruns owed

After merge to rc, ordinary Windows lane, native apphosts:

- `/*/*/WorktreeRetirementDeliveryE2ETests/C459_AdmittedWorkspaceInputReachesRecipient` (idle, busy-then-released, claim-first; the owed failing row is busy-then-released)
- `/*/*/AgentTaskLandDeliveryE2ETests/C467_V29_RealOutcomeProducerMatrix_Batch1` (landed and already-present)
- `/*/*/AgentTaskLandDeliveryE2ETests/C467_V29_RealOutcomeProducerMatrix_Batch2` (residue-cleanup and preoperation-refusal)
- `/*/*/AgentTaskLandDeliveryE2ETests/C467_V29_RealOutcomeProducerMatrix_Batch3` (operation-refusal and conflict)
- `/*/*/AgentTaskLandDeliveryE2ETests/C467_V30_ReceiptSaveFailureNeverRetypes` (receipt and verdict; the reported failure was receipt-before-save)
- `/*/*/AgentTaskLandDeliveryE2ETests/C488_ApprovalQueueInsertCrashReusesRow`
- `/*/*/AgentTaskLandDeliveryE2ETests/C488_ApprovalPollingCannotConfirm` remains owed from the selector fix; this change does not discharge it

Retain the fixture evidence root, wait JSON, queued-outcome and receipt-timeout text, protocol-git records, and referenced server or runner logs if any fail.

--- next stage ---
next: merge to rc
handoff: C459 busy-caller queued-outcome wait renews on protocol-git and names recipient state on timeout. C467 and C488 quiet stalls are not fixed. Native Windows reruns above remain.
artifact: .antiphon/task-b2669e5e.md
