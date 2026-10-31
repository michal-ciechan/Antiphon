# C467 native Land receipt Debug result

The missing timeout diagnostics are fixed and verified red then green. The underlying Windows stall is not reproduced or proven fixed: native reruns remain owed after qualification d47848cc releases its lock. No receipt budget or production deadline was increased.

## Source and commits

- Assigned base: `13565c5a480d8f30a52aa4679a5f1681ef2a8c70`.
- Red witness: `eb2d76a8346b466484fb355a3023f7495efa505d`.
- Diagnostic implementation and census update, verified source: `86adfb3df3f248d1382f13fa4e8aba82e23657d2`.
- Branch: `feat/card-task-4ed10da4`; each implementation slice was pushed immediately. The final additional commit contains this report only.
- Comparing failing candidate `cde8def41` with the assigned base shows no changes in LandDeliveryFixture.cs, LandDeliveryOptions.cs or ProgressAwareWait.cs. The receipt diagnostic defect was still present.

## Meaning of the receipt and progress

`C467_V29_RealOutcomeProducerMatrix` awaits `LandNotificationKind.Outcome`, except conflict, which awaits `Conflict`. The matrix now also supplies the accepted request identity to the first receipt wait.

The real Land request/operation path commits a terminal event and a durable AgentTaskLandNotification. The notification service and independent hosted recovery worker enqueue its keyed session message. Queue delivery sends through the isolated HTTP runner to the real native FakeGrok caller session over modern ConPTY. FakeGrok records the submitted input in its native updates; runner transcript catch-up brings it into the server. AgentTaskLandNotificationService reconciles a complete correlated transcript prompt above the queue attempt floor and persists ConfirmedAt and ConfirmingPromptSequence. Thus this is caller/orchestrator-session delivery, not merely successful Git publication, HTTP acceptance or status polling. The real model provider and credentials are replaced by the staged native FakeGrok.

The fixture retains every strict assertion: a confirmed notification, its queue row, the confirming caller UserPrompt, exact queue/notification body equality, PromptSubmissionMatch identity and completeness, and a sequence above the attempt baseline (or timestamp after the attempt start). The matrix still checks the exact terminal event, conflict state, remote source containment for publication outcomes, residue cleanup retry identity/digest and operation continuity, and single native/transcript prompt delivery.

`ProtocolProgress()` counts committed `protocol-git-*.json` files. EvidenceGit writes those after uncached Git commands return, including owned commands; cache hits and partial `.tmp` records do not count. Forty-six means 46 completed recorded Git commands, not 46 runner or delivery events. Build-slot admission, SDK identification, verifier children, queue delivery and transcript confirmation can all occur without changing that count.

## Cause assessment

Caller-reported Windows outcomes in d47848cc: landed and residue-cleanup failed with quiet 60 seconds after about 68 seconds and progress 46; already-present, preoperation-refusal, operation-refusal and conflict passed. These statuses were not independently rerun here. No supplied result establishes the separate retained-evidence case.

The proven defect is diagnostic: the wait discarded the producer and receipt-path state, leaving identical failures indistinguishable. A progressing Land can leave its Git stage and become silent while real verification waits for admission or a child. LandingVerifier performs SDK/artifact preparation and takes a runner build slot before building. The isolated runner retains a host-memory floor; loaded-host admission is a plausible cause. Other possible stalls, including native delivery, remain unresolved. No Windows protocol file, operation snapshot or pre-teardown log was supplied, so assigning command 46 to a specific stage or declaring a Windows receipt defect would exceed the evidence. The local bare remote and the hook installed only for operation-refusal provide no evidence of a credential prompt causing these two failures. The stream-close/container-delete stdout tail is consistent with teardown, not proof of the initiating cause.

There is no usable Windows p95 for this wait. A 2m15s whole-test failure includes setup and teardown and cannot justify enlarging the quiet window. Both the 60-second quiet window and 600-second absolute cap remain unchanged. No slot, memory floor, Git hook or production delivery deadline was bypassed.

## Changes

- `tests/Antiphon.E2E/Fixtures/LandDeliveryFixture.ReceiptDiagnostics.cs`: preserves the original TimeoutException as InnerException; appends kind, task/caller/request/notification identities, evidence root, last completed Git record path/command/exit/time/pid, and bounded request/operation/notification/queue/transcript/event metadata. Producer-state queries use cancellation and a separate five-second diagnostic bound. Metadata capture failure is named without replacing the timeout.
- It collects server, child and isolated-runner log paths and bounded timestamp/level tails with fixed diagnostic categories. Raw messages, prompt bodies, Git arguments, credentials and exception messages are omitted. `receipt-timeout-<identity>.txt` is written inside the fixture evidence root before teardown.
- `LandDeliveryFixture.cs` invokes the decorator on receipt timeout. `AgentTaskLandDeliveryE2ETests.cs` correlates the matrix's initial receipt to its request.
- `LandDeliveryFixtureProbeTests.cs` holds a fake producer beyond a one-second quiet window and verifies the identity, held producer state, last Git evidence, safe log tail, persisted diagnostic and original exception. A failing diagnostic producer still leaves the original failure and file evidence available. The owned producer is released and joined in finally.
- The C1584 source roster, independent source-count assertion and probe cost row include the added witness: 179 total / 172 required / seven unchanged exclusions. Existing cases and their prices remain. `docs/testing-and-build.md` records the diagnostic contract.

## Verification and timings

All builds/test drivers/evaluator ran through `scripts/build-slot.ps1`, with `TEMP=/tmp`; Linux builds and runs used `UseAppHost=false` and isolated output directories. All commands were joined. No native tests were run.

| Source | Selection | Executed / passed / failed / skipped | Run duration |
|---|---|---|---|
| eb2d76a83 | new stalled-receipt witness only | 1 / 0 / 1 / 0, intended diagnostic assertion red | 2.433s |
| 86adfb3df | LandDeliveryFixtureProbeTests | 2 / 2 / 0 / 0 | 3.113s |
| 86adfb3df | ProgressAwareWaitTests | 8 / 8 / 0 / 0 | 0.893s |
| 86adfb3df | LandDeliveryWaitRoutingTests | 4 / 4 / 0 / 0 | 0.784s |
| 86adfb3df | RcRosterRepairTests | 3 / 3 / 0 / 0 | 12.390s |
| 86adfb3df | C1514LandingSplitBudgetTests: C1404, C1577, C1584, C1410, G2865 | 5 / 5 / 0 / 0 | 59.140s |

Green total: 22 executed, 22 passed, zero failed/skipped. An initial combined-class filter selected zero tests and exited 8; it receives no credit. The four individual class filters above corrected that invocation without changing source. The red witness compiled and failed an assertion about the absent diagnostic wrapper, not a build, fixture or zero-test error.

E2E red build: 91.55s, 45s slot wait, 39 warnings, zero errors. E2E green build: 71.88s, zero slot wait, 39 warnings, zero errors. Antiphon.Tests/staged-helper build: 222.06s, 15s slot wait, 1400 reported warnings including repeated referenced-project diagnostics, zero errors. Green E2E probes waited 15s for a slot; the individual portable test launches and graph evaluation waited zero. Build-slot hold durations include process startup/exit overhead and are not test durations.

The green held-producer test itself measured 1.5374375s on this Linux host, one sample, not a Windows p95. The existing provisional CARD-1584 ten-second-per-case planner price was retained for the two probe cases. Native results must replace provisional cost assumptions through the normal release-owned timing path. No existing test timeout was increased. The new five-second secondary collection cap is provisional containment of diagnostic work, not extra receipt eligibility; no measured DB p95 is claimed. The two owed C467 native rows are the bounded collection opportunity before considering a change to that cap. These probes do not start containers, a web host, migrations or a native session. IO/DB/process phase splits not emitted by these runs remain unknown; build/slot time is separated above.

Evidence logs and per-test TRX/HTML are at `/tmp/task-4ed10da4-*-results/` and `/tmp/task-4ed10da4-*.log` (task-owned scratch). In particular: red-results, green-e2e-results, progress-results, routing-results, roster-results and census-results. The zero-test attempt remains under portable-results.

Real `audit-land-verification-graph.ps1 -Check -ToolPath tests/Antiphon.Tests/bin-c467-tests/land-graph-audit/Antiphon.LandGraphAudit.dll` passed under a slot, 25s hold. It reported `digest=E7C6EDAFA83B544A42E186899267267FD73E6FDD2FB7DD2DF36C233F2C405806 count=69`. No `-Update` was run. The tool came from the slotted Antiphon.Tests staged-helper build.

`check-evidence-diff.ps1` passed the base-to-implementation range: two commits, zero evidence entries/violations. `git diff --check` passed. Read-only merge-tree against fetched `origin/rc/2026-10-27` at 13565c5a480d8f30a52aa4679a5f1681ef2a8c70 passed, tree feae95787a18fcc840b530a5c1d47d6ed0fa054c. The mandated fetched-master comparison against 256b861468518f1f18489798b2f109f48d78e0c8 exited 1 with 47 conflicts. A base comparison also exited 1, with 44 conflicts: this task adds master-only conflicts in the C1584 source roster, nightly-class-costs.json and test-nightly-tests.ps1 to that existing divergence. This is not a conflict-free master landing claim; the intended rc target is clean. No merge, rebase, branch switch, rc change, tag or deployment was performed.

## Linux lane and native obligation

The testing owner documents server2's nested Docker daemon and Testcontainers network namespace. This host's Docker server answered version 27.5.1. Portable E2E probes ran successfully. Browser E2E additionally requires a current client/dist, Playwright browser prerequisites and the isolated random runner; browser prerequisites were not qualified by this task.

The C467 matrix itself cannot execute on Linux: InitializeAsync explicitly asserts Windows, locates the modern ConPTY redistributable pair and requires staged native `fakegrok.exe`. Its owned-runner identity checks also require the Windows executable. Linux probe results therefore carry no native receipt credit.

After d47848cc releases the native lock, rerun at the merged diagnostic source:

1. `Antiphon.E2E.AgentTaskLandDeliveryE2ETests.C467_V29_RealOutcomeProducerMatrix_Batch1(landed)`.
2. `Antiphon.E2E.AgentTaskLandDeliveryE2ETests.C467_V29_RealOutcomeProducerMatrix_Batch2(residue-cleanup)`.

Use the ordinary native checkpoint/slot lane and native apphosts; do not copy the Linux UseAppHost=false override into native fixture staging. Exact method filters are `/*/*/AgentTaskLandDeliveryE2ETests/C467_V29_RealOutcomeProducerMatrix_Batch1` and the corresponding Batch2 path. Each method filter expands its two argument rows, including the named failing row and its existing passing control. Retain the emitted fixture root, wait JSON, receipt-timeout text, protocol Git records and referenced server/runner logs if either fails. That evidence distinguishes verification/admission, publication/cleanup and native delivery before choosing any further repair or measured budget change.

Caller authority already covered this investigation, diagnostic repair and branch commit/push; no additional approval was requested. The live qualification and C:\\Antiphon were untouched. Cleanup joined all drivers, inventoried the 52 exact task-owned isolated output roots, refused links, removed files individually and directories nonrecursively, and verified no bin-c467-* directory remained. Test receipts and logs are retained in scratch.

--- next stage ---
next: merge to rc
handoff: Merge the diagnostic fix into rc; retain the two native rerun obligations and do not claim the Windows stall fixed from portable evidence.
artifact: /work/worktrees/task-4ed10da4/.antiphon/task-4ed10da4.md
