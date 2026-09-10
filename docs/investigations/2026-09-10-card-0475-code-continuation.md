# CARD-0475 Code continuation and Mutation bootstrap

Code continuation: `c1b2ed14`; original Code/landing owner: `3bfe742a`.
Plan: `docs/superpowers/plans/2026-09-10-card-0475-regression-suite-fixes-plan.md`.
Implementation source commit: `c08fa68d3b9223daac11072182a6ae53d66f1c00`.
Expected restart: **none**. Mutation remains separate; **review-required: yes**.

## Recovery and changes

The remote branch did not exist. Its local branch was occupied by the canceled task's
worktree, with four implementation commits ending at `0367e638` and one uncommitted
boundary-test removal. Continuation started from that exact local tip on
`feat/card-task-c1b2ed14-continuation`, preserving the unfinished removal.
The saved results were more complete than the resume brief: Unit 2012 passed / one
skipped; controlled 252 passed; native/verifier 72 passed; delivery/script 161 passed /
six failed. The native result still included four source-coordinate cases whose
controlled equivalents had already been added; removing those duplicate native cases
completes the planned 38/8/14 native matrix allocation.

The continuation fixed the receipt pump's pre-commit UUID consumption and restart
replay; complete normalized records from one JSONL line now commit together, preserving
assistant/TurnEnd parts sharing a UUID. The next sequence comes from persisted state.
Read/save cuts retry without consuming the line; disposal owns and joins the pump.
Recovery fixtures now preserve the original queued identity across service recreation,
exercise before-write/before-Enter/after-recipient/before-verdict cuts, and require
exact native-file plus database receipts and no duplicate input. One-Esc recovery uses
the real forwarding client and PTY. Batched delivery preserves both queue IDs.

The large multiline fixture's old em dash arrived as NUL in the inbox/.NET fake;
`multiline-diagnostic/multiline.trx` records the exact difference. The ASCII separator
keeps this fixture within ADR 0002's documented character boundary. CRLF input, every
content line, the exact normalized body, one submit, and transcript confirmation remain
asserted. This is no claim of Unicode fidelity or a production transport change.

The landing model's remote-rewrite helper incorrectly created a descendant retaining
the source. It now branches from the seed. The post-push test requires an actual push
and subsequent independent negative observation; cleanup asserts the specific fresh
remote-containment refusal. The support tests also verify the explicit and DI Git
instances, causal modes, exact attempts and operation identity, a valid target-registration
companion, and lease ownership at protocol entry and subsequent inspections.

The frozen manifest now compares every allocated class/method/argument tuple, not only
counts. The external audit independently reads the 169 original tuples at `dc6af182`
and joins them to executed TRX identities. All 72 wrapper identities and argument rows
are preserved with old/new destinations recorded. PC-62's plan selector was corrected
from GuardTests to HarnessTests, where its method lives; all 179 PC IDs and 111 exact
selectors resolve. No positive controls were executed.

## Evidence and boundaries

Evidence root: `C:\Antiphon\worktrees\card-task-c1b2ed14\.antiphon\c475-continuation`.
It retains inherited TRX, failed diagnostics, fresh ordinary runs, command/commit/DLL
hash metadata, tuple and wrapper manifests, historical hashes, and `pc-pending.json`.
Each `run.json` records the exact filter, commit, DLL SHA256, actual exit and outer wall.
`ordinary-summary.json` joins class identities to rows and summed body time; its traces
include the model's actual commands and the six native recovery receipts/input sequences.

No production C# or shared real landing harness changed. The protected crash, journal,
publication, identity, removal and recovery suites remain byte-identical to the design
base. The controlled closure uses the real database, repository lease, protocol,
dispatcher and guarded removal, with injected model Git/verifier/worktree operations.
The inspected model dispatch has no native Git/verifier fallback; the smoke's real
invocations and explicit service references are asserted. Database/container startup is
still real. Controlled Git is not evidence for native Git ref/index/process semantics.
The transcript pump substitutes ingestion only; these tests do not establish SSE
reconnect, production transcript discovery, runner adoption, OS power-loss durability,
live provider behavior or Unicode fidelity of the inbox fake.

Historical duration reproduction used a byte-preserved allowlist from profiling commit
`8234ae3e`. The unchanged broad TRX has 492 rows / 202 slow rows / 174 unlisted hits;
clean Unit has 1992 / 2 / 2. Both script exits are 1 as expected. SHA256 before/after
confirms the original artifacts were not changed. Body sums overlap; they are not
elapsed duration. No new whole-suite benchmark or parallelism saving is claimed.

## Verification ledger

| Selection | Passed | Failed | Skipped | Outer wall seconds |
|---|---:|---:|---:|---:|
| unit | 2012 | 0 | 1 | 70.045 |
| controlled | 253 | 0 | 0 | 95.540 |
| delivery | 167 | 0 | 0 | 282.206 |
| real | 68 | 0 | 0 | 1453.354 |

Total: **2500 passed, zero failed, one skipped** across 2501 selected rows. The sole
skip is the existing `Restored_key_file_symlink_is_rejected_without_mutating_target`
case: the Windows account cannot create file symlinks. No required native row skipped.
Final build: zero errors, 144 warnings; `build-final.log`. All four selections used
the same DLL SHA256: `4400EE9ADA64BEF972377B27FD5CFC1AA2EF473431DFF1042C74A21A7A3060C6`.

The final 140 controlled matrix rows have 655.727 summed body seconds; the complete
253-row controlled selection took 95.540 seconds outer wall. The fixture retains the
existing serialized database-clone semaphore, so overlapping per-row waits are included
in TRX duration. The model closure and trace prove the Git/verifier substitution;
duration alone is not that proof. Native class/body totals appear below and in the JSON summary.

Final tripwire: Unit exit 1 / two existing effort-prompt rows; controlled exit 1 /
30 rows; delivery exit 1 / the 9.264-second native one-Esc negative; native exit 0
after the four planned real-class exceptions. The controlled classes and Unit lane
received no blanket exemptions. These visible duration hits are not failed test assertions.

The native exceptions cover Boundary (real ref/index/sequencer/pin capstones),
Admission (real dispatch and lease orderings), Concurrency (real settlement and writer
races), and Verifier (real generated-project build/test/cancellation and private outputs).

Re-run from the implementation checkout, first building `tests/Antiphon.Tests` with
`dotnet build tests/Antiphon.Tests --property:OutputPath=bin-c475/ --nologo`.
Then use `dotnet run --project tests/Antiphon.Tests --no-build --property:OutputPath=bin-c475/ --`
with each exact filter below and a fresh TRX results directory. Projects remain serial.

- unit: `--treenode-filter '/*/*/*/*[Category=Unit]'`
- controlled: `--treenode-filter '/*/*/(AgentTaskLandBoundaryControlledTests*)|(AgentTaskLandAdmissionControlledTests*)|(AgentTaskLandConcurrencyControlledTests*)|(LandingProtocolHarnessTests*)|(LandingProtocolGuardTests*)|(LandingSourceBoundaryControlTests*)|(LandingAdmissionControlTests*)/*'`
- delivery: `--treenode-filter '/*/*/(DelegateScriptLandStatusTests*)|(TestDurationTripwireTests*)|(SessionMessageQueuePtyIntegrationTests*)|(SessionQueueReceiptPlumbingTests*)|(SessionMessageQueueDeliveryVerificationTests*)|(SessionMessageQueueInterruptedAttemptTests*)/*'`
- real: `--treenode-filter '/*/*/(AgentTaskLandBoundaryTests*)|(AgentTaskLandAdmissionTests*)|(AgentTaskLandConcurrencyTests*)|(AgentTaskLandVerifierTests*)/*'`

Class durations below are summed TRX body time, including overlapping waits;
they are not wall time and must not be added to the selection wall times above.

| Selection / class | Rows | Summed body seconds |
|---|---:|---:|
| controlled / AgentTaskLandAdmissionControlledTests | 24 | 152.398 |
| controlled / AgentTaskLandBoundaryControlledTests | 94 | 406.640 |
| controlled / AgentTaskLandConcurrencyControlledTests | 22 | 96.689 |
| controlled / LandingProtocolGuardTests | 23 | 108.210 |
| controlled / LandingProtocolHarnessTests | 18 | 78.424 |
| controlled / LandingAdmissionControlTests | 42 | 24.680 |
| controlled / LandingSourceBoundaryControlTests | 30 | 9.056 |
| delivery / DelegateScriptLandStatusTests | 11 | 19.697 |
| delivery / SessionMessageQueueDeliveryVerificationTests | 106 | 257.115 |
| delivery / SessionMessageQueueInterruptedAttemptTests | 11 | 6.149 |
| delivery / SessionMessageQueuePtyIntegrationTests | 8 | 48.693 |
| delivery / SessionQueueReceiptPlumbingTests | 15 | 26.453 |
| delivery / TestDurationTripwireTests | 16 | 15.868 |
| real / AgentTaskLandAdmissionTests | 8 | 275.888 |
| real / AgentTaskLandBoundaryTests | 38 | 706.899 |
| real / AgentTaskLandConcurrencyTests | 14 | 329.720 |
| real / AgentTaskLandVerifierTests | 8 | 121.187 |

Retained diagnostic history: inherited delivery 161/167 passed (six plumbing
failures); first repaired plumbing selection 15/15; strengthened controlled selection
252/253 (remote ancestry defect); first strengthened delivery 166/167 (native character
mismatch); isolated multiline diagnostic 0/1 with em dash -> NUL. All are resolved by
the final runs above. One intermediate build had a nullable assertion compile error;
the final build is clean of errors. These ordinary diagnostics are not PC executions.

| IDs | Ordinary evidence |
|---|---|
| V-1, R-1, R-2 | Unit: legacy enum prefix, helper ownership, lane and process-limiter census. |
| V-2 | Eleven private-listener status/acceptance scenarios. |
| V-3, R-3, R-4, R-5 | 140 controlled plus 60 native matrix tuples, original 169-row set equality, precise boundaries and lease/admission modes. |
| V-4 | Model contracts in Unit, harness/guard integration, actual command trace and service identity. |
| V-5 | 30 source-boundary and 42 admission/hold wrappers, exact old/new identity map. |
| V-6, R-6 | Eight real verifier rows (3 V34 + 1 V35 + 4 V32), nine Unit counter rows. |
| V-7, R-7 | Eight native queue rows, including both argv backends, complete receipts and swallowed-Enter negatives. |
| V-8, R-8, R-9 | Fifteen plumbing cases, six native fault cuts, two-session UUID/sequence evidence and joined teardown. |
| V-9 | 106 delivery-verification rows and eleven interrupted-attempt rows, including the two overlay pins. |
| V-10, R-10 | Sixteen private PowerShell tripwire fixtures plus historical/fresh script runs. |
| V-11 | Historical 174/2 reproduction and fresh duration rebaseline above. |
| V-12, R-11 | Guide/Code/Review scope rules agree; Unit includes instruction-bundle checks. Every ordinary selection is named above. |
| V-13 | Fresh nonzero TRX selections, actual exits, source/DLL hashes and exact tuple join. |
| R-12 | Protected native helpers/suites and all production C# unchanged. |

## Mutation bootstrap

The original landing owner is `3bfe742a`, branch `feat/card-task-3bfe742a`, worktree
`C:\Antiphon\worktrees\card-task-3bfe742a`. The continuation branch is
`feat/card-task-c1b2ed14-continuation`, worktree
`C:\Antiphon\worktrees\card-task-c1b2ed14`. Final ref synchronization is verified in
the caller report; use its exact published SHA as the dispatch starting point. Build
fresh isolated output there; never reuse a binary built before a mutation or restore.
Run PC-1 through PC-179 from the plan, using only each exact method selector and all
its argument rows. Record baseline/red/restored-green counts, intended assertion,
patch, source and DLL fingerprints, TRX paths and timings. Baseline may be reused only
for identical filter/arguments and implementation fingerprint. The 179 entries in
`pc-pending.json` are **Pending**, not passed, and the inherited C448 ledger is not
rewritten as new evidence. PC-123's causal preparation helper is now
`LandingProtocolHarnessTests.PrepareModeAsync`; PC-62 uses the corrected HarnessTests
selector. Unexpected red setup failures or survivors return to Code.

Keep the design's conservative **530-minute Mutation floor**, with 135 policy/model,
21 real Git/verifier, eleven native queue and twelve script controls. Reprice from
measured method-specific cycles; the ordinary run is not a mutation-time benchmark.
No change to process parallelism, project sequencing, native timeouts or live stack.
Mutation must hand off to required Review on success. Do not route directly to Land.

The caller requested the CARD-0470 bootstrap default: **next: decide** for the separate
Mutation dispatch. This Code task executes no PC variants and performs no landing or restart.
