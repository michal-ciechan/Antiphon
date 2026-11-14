# g90: three CARD-1892 uncertainty consumers

Debug task `9c1feeba`, base `8cec1e74e75819ef20756c228add987ce4ae4170`.
The Windows extract was read at commit `d403313ab8d631effa851f46dc7cedad356ee8e4`:
`docs/investigations/2026-11-14-run-34d41b7e-g90-three-classes-windows-failure.md`.
Its run `34d41b7e` had 383 results, 380 passed and these three failures.
No live qualification, desktop, production service or production volume was changed.

One note per failing family:

- [PhoneHomeOutageMentionTests](2026-11-14-why-missed-g90-phonehomeoutagemention.md)
- [GrokRulesCompactionRecoveryTests](2026-11-14-why-missed-g90-grokrulescompactionrecovery.md)
- [StandingSessionSwitchConcurrencyTests](2026-11-14-why-missed-g90-standingsessionswitchconcurrency.md)

## Local red evidence

All three whole classes ran serially against the clean, unchanged base above,
using the native-output build with `UseAppHost=false`, the host slot gate and
the assembly's shared PostgreSQL fixture. These are assertion failures, not
build, discovery, fixture, zero-test, polling or timeout failures.

| Evidence directory under `.antiphon/evidence/g90/` | Executed | Passed | Failed | Skipped | Wall including admission |
|---|---:|---:|---:|---:|---:|
| `red-phone-2` | 8 | 7 | 1 | 0 | 87.33 s |
| `red-grok` | 11 | 10 | 1 | 0 | 84.41 s |
| `red-switch` | 24 | 23 | 1 | 0 | 93.16 s |

At witness commit `91fbecb735a3eb2c61ddd8f746f720068edd80ac`, the new
`ImpactedClassesSelectorTests.Input_uncertainty_paths_select_delivery_and_switch_witnesses`
ran three cases, all failed as intended (`red-selector`, 32.00 s). Both service
paths matched no uncertainty kind; the exception path omitted the PhoneHome
class. This independently witnesses the selection defect before its repair.

Each directory retains `run.trx`, `run.log`, source/dirty/load metadata and a
phase journal. Missing phase spans remain unknown rather than zero. Generated
evidence remains ignored. The first local wrapper (`red-phone`) was stopped
before acquiring a lease or starting a test: an unquoted native argument was
expanding the tree filter as a filesystem glob. The corrected wrapper quotes
the filter; that aborted wrapper is not red test evidence.

## Cheapest pre-land selection

Extend the existing `input-delivery-uncertainty` row of
`tests/impacted-classes.json`. It now watches these exact paths:

- `server/Application/Exceptions/InputDeliveryUncertainException.cs`
- `server/Application/Services/SessionMessageQueueService.cs`
- `server/Application/Services/AgentControlService.cs`

Its explicit classes are AgentTaskMidTurnRefineTests,
LandRefusalDeliveryWitnessTests, SessionMessageQueuePhoneHomeDropTests,
PhoneHomeOutageMentionTests, GrokRulesCompactionRecoveryTests,
StandingSessionSwitchConcurrencyTests, CodexCliObservationGapTests and
GrokRulesReadyOrderingTests. The new selector witness checks all three paths
and requires all eight classes on the emitted Antiphon.Tests filter.

The old row, added by `8cec1e74e`, watched only the exception file and named
only the first three classes. The behavior that broke these consumers lives
in SessionMessageQueueService's catch/recovery paths. An edit there therefore
did not select this row, and an exception-file edit still omitted all three
g90 families. The fixed Merge subset is not a substitute for these consumers.
The syntax-derived index is capped; explicit classes also prevent these
witnesses from being displaced by unrelated higher-ranked consumers.

The committed CARD-1892 Code and round-3 evidence enumerate CP-2/4/5/6/8/9
without these three classes. Those closed checkpoints supplied the ordinary
Review selection; none exercised these indirect callers. A distinct original
Merge execution receipt and the full Review transcript were not supplied, so
their actual execution is unknown; this note identifies the recorded roster
and selector omissions rather than inventing a Merge run result.

## Guard and caller audit

Product commit: `dcb068f89c33caebaa543f0413fd254e501b1261`.
The original concurrency failure is its red witness. The same queue semaphores,
transaction, source-pointer recheck, pending-row transfer rules, conflict code
and message remain in place. A separate session-scoped `AnyAsync` also refuses
source rows that are Sent, have no verdict and have at least one attempt.
Such rows are not added to the list of pending messages eligible for transfer.

`StartInteractiveSessionAsync` is the common reservation path for explicit
history, default continuation, explicit Fresh, automatic/supervised Fresh and
capacity recovery. The refusal remains inside `switching`: same-conversation
continuation/resume does not hit it. `ResumeAsync` for a compaction episode
calls `StartAsync` with that episode's session; resuming the current session
therefore remains allowed. Selecting a different session, including automatic
Fresh, intentionally holds while the source delivery remains unresolved.

The new controls prove that Delivered/LateConfirmed source rows permit a
switch, an unconfirmed row on selected history A does not block switching
away from current source B, and a stopped B with an unconfirmed row can resume
B while preserving its attempt. They exercise real reservation and launch.

For a running source, existing fresh/interrupted recovery and composer-less
watchdog reconciliation can accept complete matching UserPrompt evidence as
LateConfirmed. Composer-less recovery does not type or press Enter while the
attempt is unknown, and the next safe flush advances queued work. Existing
`RecordTransportFailureAsync` immediately records the lost reply as an error
incident for the pointed-to agent. Existing overdue composer-less discovery
records `input-delivery-unknown` attention, including while working, and a
later complete receipt may still confirm it.

A stopped session does not receive ordinary running-session delivery recovery;
it may resume itself first. If native history/evidence cannot be recovered,
release is not guaranteed: the existing incident and switch conflict surface
an operator decision hold. `CancelAsync` and the ordinary queue DTO expose
Pending rows only; they must not be described as a way to cancel a Sent
attempt. This repair adds no timer, automatic verdict, cancellation API or
replay authority. Review should attack this stuck-row scenario explicitly.

## Verification continuation

The caller's standing authority authorized the guard widening without another
approval. The two expectation repairs and selector expansion are test-only;
the widened guard requires ordinary Review. Final measured rows and source
provenance are appended after execution.
