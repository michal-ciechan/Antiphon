# CARD-0965 remaining input proofs

Original Code and landing owner: `b630f3bf-9528-4a6c-a669-903d56c66361`.
Start: `c6d5d56b5b4c565d36157e21de9c85029cc45b53`.
Branch: `feat/card-task-b630f3bf`; worktree: `/work/worktrees/task-b630f3bf`.
Parent design: `2026-10-01-card-0888-runner-bound-refinement-spill-plan.md`.

Continuation Code and current landing owner: `4c1697dc-d944-46ad-ae9e-085276be1663`.
Branch `feat/card-task-4c1697dc`, worktree `/work/worktrees/task-4c1697dc`,
fast-forward continuation of `02e97b63c48ab170715d2d0edbed83f90ae74405`.
The previous owner's partial commits are included in this branch.

This test-only repair closes V-16, V-20, V-22, V-29, V-32 and adds malformed
task-input sweep and seeded pre-upgrade event coverage. The fixture opts into a
large injected desktop PtyDeliveryProfile; normal fixture defaults stay intact.
Its modern-sized ceilings also survive Linux backend fallback. This tests profile
selection, without launching a Windows pseudoconsole.

The ordinary spill incident change is deferred: the queue has three separate
SpillWriteFailedBeforeInput branches (durable-now, send-now and flush); restoring
consistent incident behavior requires more than the authorized one production
line. Existing delivery behavior remains pinned. No production file is changed.

## Verification design

V-16: real dispatcher and writer, file blocking `.antiphon`, zero runtime/file
pointer input, same-row API pointer, one Warning and authorized whole-body GET.
V-20: valid recipient and valid capability positive controls; all four forbidden
principals return no input canary. V-22: public HTTP task/event response, captured
formatted and structured log state, and authorized whole-body HTTP response.
V-29: both polls observe identical task/session/message/key/time; intercept actual
database writes and runner input calls. V-32: complaint exists before settlement,
disappears after, and an independent blocked-question condition remains.

S2 completes V-16 as three parameterized fresh-database cases: SendNow, idle flush,
and immediate persisted enqueue. For immediate enqueue a SaveChanges interceptor
seeds an admitted task/event conversation key on the new row and records its ID;
ordinary question overlays intentionally have no input key. This isolates the
internal fallback branch without changing that public API or any production file.
Phone-home readiness uses the endpoint's accepted-connection log as a completion
signal, calls MarkRecovered for this synthetic empty runner, and awaits the injected
IRunnerEligibilityObserver's expected live-connection signal. No wall-clock poll.
It does not claim production catch-up-worker coverage. V-29 injects a fixed poll
clock, advances one minute, requires the exact complaint timestamp, and compares
fresh persisted queue/task/session snapshots before and after both polls.

Ordinary diagnostic strength checks for V-16/20/22/29/32 select only their precise
methods and temporarily suppress fallback, allow a foreign task principal, log the
body, reset episode time to poll time, or include terminal tasks respectively.
These already-implemented behaviors are baseline-compatible: baseline green is
reported honestly, not described as unchanged-production red. Only named assertion
failures from diagnostic defects count as strength evidence. All bytes are restored
exactly before final clean qualification; these do not discharge any PC.

New sweep test: malformed length, uppercase GUID and non-GUID segment stay Pending;
valid keyed machine row is canceled in the same sweep. Migration test inserts a
legacy row while InputBody does not exist, then verifies owner/detail and null body
after the real up-migration.

Explicit brief-authorized scratch checks use precise methods for the ceiling and
sweep guards; extra V-row strength checks are diagnostic, never PC discharge.
Restore exact production bytes and rebuild before ordinary green qualification.
All parent PC-1 through PC-36 stay pending for SourceLanding Mutation. Windows
parent CP-5 remains owned by CARD-0960 and is outside this dispatch.

### Checkpoints

Rows CP-1 through CP-4 retain the parent's exact selections. CP-6 is the Final Unit
lane because the task edits TaskInputSpillFixture; CP-7 adds the two full affected
integration classes. S1 is the committed test-only repair group. This is no full
assembly run: Unit misses delivery, persistence, leases and landing, while the
named integration selections exercise this input feature's delivery/persistence.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---|---|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c965-input/` | task-input | `/*/*/(AgentTaskInputSpillTests*)\|(AgentTaskRefineTests*)\|(AgentTaskReplyOverlayTests*)/*` | V-1..V-15, R-1, R-2 | AgentTaskInputSpillTests,AgentTaskRefineTests,AgentTaskReplyOverlayTests | 31 | 10 | true | `C804_ORPHAN_SWEEP_ROOT=c965-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-2 | S1 | `tests/Antiphon.Tests -> bin-c965-fallback/` | fallback | `/*/*/(AgentTaskInputFallbackTests*)\|(PhoneHomeSpillTests*)\|(PhoneHomeSpillTransportTests*)\|(DurableRunnerSpillReceiptTests*)/*` | V-16..V-24, R-3 | AgentTaskInputFallbackTests,PhoneHomeSpillTests,PhoneHomeSpillTransportTests,DurableRunnerSpillReceiptTests | 29 | 10 | true | `C804_ORPHAN_SWEEP_ROOT=c965-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-3 | S1 | `tests/Antiphon.Tests -> bin-c965-attention/` | attention | `/*/*/TaskInputReadFailureTests*/*` | V-25..V-32 | TaskInputReadFailureTests | 8 | 8 | true | `C804_ORPHAN_SWEEP_ROOT=c965-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-4 | S1 | n/a | client | `pwsh -NoProfile -File scripts/test-client.ps1 src/features/attention/attentionVisuals.test.ts` | V-33 | 24 Vitest results, no failed/skipped | n/a | 2 | true | n/a |
| CP-6 | S1 | `tests/Antiphon.Tests -> bin-c965-unit/` | unit | `/*/*/*/*[Category=Unit]` | Final Unit | nonzero Unit results | 1 | 4 | true | `C804_ORPHAN_SWEEP_ROOT=c965-disabled` |
| CP-7 | S1 | `tests/Antiphon.Tests -> bin-c965-compat/` | compatibility | `/*/*/(ParkedMessageSweepServiceTests*)\|(CapacityRecoveryCompatibilityTests*)/*` | malformed-key, seeded migration | ParkedMessageSweepServiceTests,CapacityRecoveryCompatibilityTests | 18 | 4 | true | `C804_ORPHAN_SWEEP_ROOT=c965-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |

### Cost

Ordinary estimated floor: 38 minutes plus authoring. The brief has a 45-minute
time box and permits partial slices, so unfinished proof IDs must remain pending.
Bootstrap the checkpoint tool and client dependencies under the host slot gate;
these are setup exceptions. Scratch runs are explicit brief-authorized diagnostic
exceptions and report exact filters, red labels, build provenance and restoration.
