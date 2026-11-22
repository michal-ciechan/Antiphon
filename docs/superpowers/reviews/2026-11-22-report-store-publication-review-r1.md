# CARD-1514 report store publication review, round 1

**Return to Code.** The new witness proves that a staged writer reuses a verified winner. It allows a regression that reopens the race between the final destination check and the rename.

## Finding

**Where.** `tests/Antiphon.Tests/Application/AgentReportStoreTests.cs:190` awaits the winner's entire `StoreAsync`. Line 198 then releases the contender. Both stores have passed their initial check, but only one can reach the new publication section at a time in this test.

**Failure.** Giving each store instance its own gate, or removing the gate while retaining the final existence and byte checks, preserves every observable outcome of the controlled test. The winner has already returned when the contender rechecks. The original simultaneous test checks both returned paths and leaves a second same-byte rename on Linux undetected.

**Why.** Two independent gates would allow both writers to see the canonical path absent at the final check and both attempt the overwrite. This is the interleaving the fix needs to exclude. The timestamp assertion detects an actual replacement once one occurs. The witness cannot establish that the gate prevents this interleaving.

**Fix.** Add a deterministic contention test using two store instances for the same task and bytes. Hold the first writer between its final miss and its move. Observe the second writer attempting admission while the first still owns publication. Assert one publication, the same verified canonical path and exact bytes for both results, and temporary-file cleanup. The witness should fail if coordination is scoped to an instance or omitted while the final check remains. The current open-reader case covers a distinct requirement and should remain.

## Publication and custody

- `server/Infrastructure/Files/AgentReportStore.cs:22` allocates 64 process-static semaphores. The task GUID selects a stripe. Storage stays bounded as tasks finish. Different IDs can share a stripe and its existing two-second I/O allowance. Acquisition observes cancellation and every acquired gate is released in `finally`. The code under the gate reads the file and moves the staged sibling. It has no callback or recursive store call. Production registers the store as a singleton at `server/Program.cs:884`.
- The gate coordinates instances in one process. Settlement, completion snapshot regeneration and card artifact preservation are server-side writers. The queue and worktree retirement also verify existing reports. The runner does not register a writer. The cutover and rollback runbooks require a single active production server. Two servers pointed at one writable report directory would lack mutual exclusion.
- `TaskReportTurnSelector` selects within the owning prompt span. The store captures `task.Result`, hashes its strict UTF-8 bytes and appends `<full-task-guid>/<sha256>.md` to the durable root. A later report with different bytes gets its own path, including whitespace-only revisions. The existing-file check validates length, exact bytes and decoded text. The staged sibling is flushed and closed before `File.Move(..., overwrite: true)`. A mismatched file can be repaired. Verification precedes every path return. The outer cleanup removes a losing sibling on normal completion. An abrupt process exit can leave a private temporary file.
- Production verification and the witness both open the canonical file with `FileShare.Read | FileShare.Delete`. The witness marks the winner with a fixed earlier timestamp, reads the open handle and verifies the canonical path separately. The original simultaneous assertion still checks every returned path and now includes the refusal reason. The native Windows error behind the historical assertion remains unknown. The original method and the controlled witness remain owed at the next Windows qualification.
- Settlement retains raw result and path. Completion recovery verifies the frozen snapshot, regenerates from its task ID and raw text if needed, and checks the regenerated path against the snapshot. The durable notification keeps its queue identity across retries. A working recipient waits for an eligible flush. `OutputDistillationDeliveryTests.DeliveryFixture.DeliverAsync` observes the submitted body through the protocol adapter and checks a single matching full `UserPrompt` in the parent's transcript. Admission failure, provider recreation and storage cancellation/retry have distinct test cases. No session input-wait or release policy changed.

## Historical comparison

The actual source base is `c1e03aac509d8e949f34750a4ead2147b13695d0`. Commit `1ccc9cd1e32dd9195f64836e5fc8a7c14ec3ae99` adds only the controlled witness. The store and original test were unchanged there. The author's stored report records 10/10 original passes and 10/10 timestamp assertion failures in Linux baseline run `20261122-094516-b8b0`, followed by 29/29 custody passes at the tip. The original raw receipt directory was unavailable in this reviewer worktree. These are attributed historical counts. No positive-control mutation was executed in Review.

## Verification

The Debug source task is `e291b60e-dcd1-4abe-b085-c26f6784a0c7`, branch `feat/card-task-e291b60e`, tip `51afdacc1c0da9d25c2385eca0048ce99565b25d`. Its note names one custody class, `AgentReportStoreTests`, and gives no separate numbered checkpoint table. Review used that exact class, the nine-class Final guard filter, and the additional classes returned by `scripts/impacted-classes.ps1` on the two-commit range. Both named publication methods ran inside the custody class. The migration downgrade selector matched **0** classes.

| Row | What ran | Result | Row wall time |
| --- | --- | --- | --- |
| CP-1 | All 8 custody methods and their argument rows | 29/29 passed. Original 146 ms, controlled open reader 178 ms. | 11.6 s |
| CP-2 | Classification policy 45, classification guard 1, slow registry 5, slow tripwire 2, native Linux policy 3, mechanical contract 11, task platform 5, layering 11, lane category 1 | 84/84 passed across all nine classes | 83.9 s |
| CP-3 | Storage budget 21 and opt-in Linux load 1 | 22/22 passed. All four buckets had 20 successful stores each. | 33.8 s |
| CP-4 | `OutputDistillationDeliveryTests` | **0 of 81 executed.** No TRX. | Host withheld |

**Incomplete delivery verification.** The selector includes `OutputDistillationDeliveryTests`. Its harness uses the PostgreSQL testcontainer. The default fixture creates an anonymous Docker volume and removes volumes during teardown (`TestDbContainerOwnership.RemoveAsync`). The brief forbids touching any Docker volume. CP-4 obtained its slot and stopped before launching the host. Receiving the report after enqueue failure or restart remains unverified in this run. In the existing source, `CompletionWarningDeliveryTests.C788_ProgressWarningRecovery` covers busy and idle callers through the enqueue and receipt crash boundaries. The delivery fixture checks the entire recipient prompt. A fresh execution of the impacted class is still owed on a lane authorized for its fixture.

Run `20261122-103050-3fc7` used one completed test-project build with zero errors, source and build provenance recorded at the reviewed SHA. The build was admitted at load 4.32. The three test hosts were admitted at 10.68, 9.09 and 5.34, each with its own lease and a 570-second host timeout. Slot waits rounded to zero. Build wall was 438.8 s and run wall was 10m02s. TRX IDs were `4c7108fd-20b6-474d-867b-a74ca1f9f70d`, `fdd54228-3276-4c67-8085-55c8cf62a7a7` and `800a6f67-78e6-4531-9693-11c7c4e0ddee`. Each result had a distinct execution ID. The per-method expanded counts matched the source for every storage class. Linux I/O p95 across the 20-sample buckets was 4/7 ms for the normal 4/64 KiB cases and 8/15 ms for the loaded cases. All 80 measured samples succeeded. Full row timings, TRX and phase measurements remain in the run and external evidence directory.

The combined run ended with exit 2. The SHA validator returned `report_failed` because CP-4 lacks a TRX. Git was clean throughout verification and the three completed lines report verified build provenance. The combined receipt cannot set the clean-source latch. All positive controls remain pending.

```text
CHECKPOINT CP-1 commit=51afdacc1c0da9d25c2385eca0048ce99565b25d build=ok filter=/*/Antiphon.Tests.Application/AgentReportStoreTests/* executed=29 passed=29 failed=0 skipped=0 trx=/work/worktrees/ws-01/.antiphon/checkpoints/20261122-103050-3fc7/rows/CP-1/run.trx slot=granted waited=0s dirty=0 source=51afdacc1c0da9d25c2385eca0048ce99565b25d sourceState=clean buildSource=verified
CHECKPOINT CP-2 commit=51afdacc1c0da9d25c2385eca0048ce99565b25d build=reused filter=/*/*/(TestClassificationPolicyTests*)|(TestClassificationGuardTests*)|(SlowTestRegistryMergeTests*)|(SlowTestTripwireTests*)|(NativeLinuxPolicyWitnessTests*)|(MechanicalPcContractTests*)|(TaskPlatformGuidanceTests*)|(C1729LayeringGuardTests*)|(TestLaneCategoryGuardTests*)/* executed=84 passed=84 failed=0 skipped=0 trx=/work/worktrees/ws-01/.antiphon/checkpoints/20261122-103050-3fc7/rows/CP-2/run.trx slot=granted waited=0s dirty=0 source=51afdacc1c0da9d25c2385eca0048ce99565b25d sourceState=clean buildSource=verified
CHECKPOINT CP-3 commit=51afdacc1c0da9d25c2385eca0048ce99565b25d build=reused filter=/*/*/(AgentReportStoreBudgetTests*)|(AgentReportStoreLoadTests*)/* executed=22 passed=22 failed=0 skipped=0 trx=/work/worktrees/ws-01/.antiphon/checkpoints/20261122-103050-3fc7/rows/CP-3/run.trx slot=granted waited=0s dirty=0 source=51afdacc1c0da9d25c2385eca0048ce99565b25d sourceState=clean buildSource=verified
CHECKPOINT CP-4 commit=51afdacc1c0da9d25c2385eca0048ce99565b25d build=n/a filter=/*/Antiphon.Tests.Application/OutputDistillationDeliveryTests/* executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a slot=granted waited=0s dirty=0 source=51afdacc1c0da9d25c2385eca0048ce99565b25d sourceState=clean buildSource=verified
```

Initial leased attempt `20261122-102530-99e9` stopped with exit 130 and zero test hosts. Its SDK process selected the system executable before the load wrapper. The retry used the unmodified checkpoint DLL with a local framework-dependent launcher, checked child resolution first, and logged admission for the build and each test driver. The additional leased tooling work bootstrapped the checkpoint tool, ran the required evidence-diff launcher, evaluated the land graph using the DLL from the checkpoint build, and built the index generator. A preliminary in-memory index inspection could not resolve its assembly dependencies. The official generator then ran against an isolated copy of the reviewed source.

`scripts/antiphon evidence-diff` reported two commits and zero violations. `audit-land-verification-graph.ps1 -Check` approved the graph. The regenerated and checked-in derived indices matched byte for byte, SHA-256 `0fd64a5d9ccad3e3ccac7917e8de3adcb69bcf05982a61f43c9423d8cd45c89c`. Merge-tree was clean against the brief's `810f891c108e45dd13246dd5edec147edec933a8` and fetched master `0b08a25e02af9cb9c0cc746eb749c8c332ed5991` (tree `6f1e351347460d64b023d496c20f39d5186db72b`).

--- review evidence ---
subjectTaskId: e291b60e-dcd1-4abe-b085-c26f6784a0c7
reviewedSourceSha: 51afdacc1c0da9d25c2385eca0048ce99565b25d
reviewedSourceClean: false
ordinaryScopeCompleted: Interim
reviewRound: 1
reviewedRange: c1e03aac509d8e949f34750a4ead2147b13695d0..51afdacc1c0da9d25c2385eca0048ce99565b25d

--- next stage ---
next: code
handoff: For 51afdacc1c0da9d25c2385eca0048ce99565b25d, add a deterministic two-store witness for contention across the final check and rename. Obtain a fresh OutputDistillationDeliveryTests receipt on a lane authorized for its PostgreSQL fixture. Retain the open-reader witness and original Code owner.
artifact: docs/superpowers/reviews/2026-11-22-report-store-publication-review-r1.md
