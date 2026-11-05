# CARD-1591 salvage: withhold reasons, channel incident, idle re-launch (plan)

Plan task 11a0b0b8, 2026-11-05, on `feat/card-task-11a0b0b8` at base
`6cda98f3c7797c4b9b3619b45e94b067efb708f2`. Plan document only. No product code,
no tests, no live writes.

`GET /api/runner-defaults` (revision 2) and `GET /api/session-runners` (windows and
linux both present) were read. Checkpoints pin no runner and no OS. The card's
`requiredPlatform` is Any.

S2 is the slice that restores Telegram delivery. It ships off. S1 is the first
land because it only records an incident. The operator stop/start already used on
Family remains the manual way to restore one seat before S2 is enabled.

## Decisions

### D-1 — Leave the landed withhold record alone

The safe salvage items (1), (2), and the `PendingNotesAged` attention row are
already ancestors of this base. See the ground-truth table. Code does not rename
reasons, does not increment `DeliveryAttempts`, and does not rebuild that alert.

Rejected: another pass that counts a withhold as a delivery attempt. That was
replaced on purpose by `WithholdCount` (`e5beafabb`, `ce4ece4a4`) so expiry,
parking, and land-receipt "attempted" checks keep treating the row as never typed.
Rejected: changing the adopted-plus-unreadable-screen order. `DialogDeliveryAdmissionTests`
already requires that pair to read `ScreenUnknown`, and a blocked screen still
wins over adopted so a real dialog is not charged.

### D-2 — One existing incident, one existing threshold

A queued Telegram (or other channel) row that has a durable withhold older than
`ChannelBridge:AgedHoldIncidentMinutes` (default 10) raises one Critical
`ChannelReplyLost` with `FailureReason` `ChannelInputWithheld`. The stamp is a
nullable `ChannelInbound.InputWithheldIncidentAt`, same survival rule as
`ContinuityHoldIncidentAt`: one row, kept across restart and incident-history
pruning. No new `AttentionKind`, no new `AgentIncidentKind`.

`PendingNotesAged` stays a Warning on the attention feed at 900 seconds
(`DeliveryVerification.PendingNoteAlertAgeSeconds`). It does not page. The
2026-11-04 Family outage had withhold text stored and no `AgentIncident` after
2026-10-16. This incident is that missing page.

Zero or negative minutes is clamped to 0, matching the continuity hold, so the
threshold fires immediately. There is no second off switch.

### D-3 — The incident scan does not sit behind the queue lock

`RunWakeWorkerAsync` calls `DrainPendingAsync`, and that drain's last step
`FlushSessionAsync` waits on the per-session lock with the worker token
(`SessionMessageQueueService.cs` around the `sem.WaitAsync` in
`FlushSessionReportingDeliveryAsync`). Debug 59ab5d9a found that wait stuck, after
which the worker logged nothing. A scan at the top of the next drain never runs
while that wait is outstanding.

S1 starts `RunWithheldIncidentLoopAsync` beside `RunWakeWorkerAsync` from
`ChannelBridgeService.ExecuteAsync`. It uses the host stop token, sleeps 10
seconds, and never calls `FlushSessionAsync`, `DeliverNextLockedAsync`, or
conditional input. A failed pass is logged and the loop continues.

Rejected: folding the scan into `RecordWakeIncidentAsync`. That method returns
early once `QueueMessageId` is set, which is exactly the withheld-but-queued
shape. Rejected: writing the incident from `AttentionService.GetAsync`. A read
route would gain a side effect, and it still would not run unless somebody opened
the feed.

### D-4 — Durable withhold evidence only

The scan incidents a row when all of these hold:

- the inbound has `QueueMessageId`, `AgentId`, and a null `InputWithheldIncidentAt`
- the queue row is that id, `Origin` Channel, `Status` Pending, `DeliveryAttempts`
  0, `LastDeliveryStartedAt` null, `WithholdCount` > 0, `FirstWithheldAt` set
- `FirstWithheldAt` is at least `AgedHoldIncidentMinutes` before now
- the session `Status` is Running

The clock is `FirstWithheldAt`, not `AcceptedAt` and not `CreatedAt`. The message
names provider, conversation, and native id plus `LastWithholdReason`. It does not
copy the body.

A pending channel row with `WithholdCount` 0 stays quiet. That covers a session
that is still Working (the flush has not recorded a withhold) and the no-charge
withholds (null snapshot, real dialog, remote-control menu). Those last three are
a residual: they still do not page. `PendingNotesAged` misses them for the same
reason. Widening this scan to "idle and old" would page a row that is merely next
in line. Left for a later card if the residual shows up in production.

Inbounds with a null `QueueMessageId` stay on the existing unowned-hold path.
They are the wake-worker stall (defect B below), not this incident.

### D-5 — Re-launch is possible, and it ships off

A runner restart re-attaches a surviving pty host in `AdoptAsync`, which sets
`_adopted = true` (`SessionRunnerRuntime.cs` `AdoptCoreAsync`). `ObserveDialog`
then forces dialog `State` to Unknown (`known` is false at the line the card
cites, around 2785) while still returning the real `ScreenState`. Automated
conditional input therefore stays `observation-unknown`.

`StartAsync` on a new host never sets `_adopted`. `AgentControlService.StartInteractiveSessionAsync`,
when it finds the agent's previous row, keeps that session id, advances
`StartedAt` with `SessionGeneration.Next`, and enqueues the launch with
`resume: true` (`claude --resume`). That is the stop-then-start path that left
Family `adopted: false` on the same session id on 2026-11-05.

S2 calls that same stop and start. It does not add a keystroke into the adopted
composer. The new process is non-adopted, so the existing guard may type on it
once the new screen is Clear. The launch also types the existing resume note
(`ChannelPreamble.RestartResumeBody` when the agent has a preamble). That note is
created by the start path after the fence below, so the fence does not swallow it.

The setting is `Supervision:DeliveryVerification:DeadoptIdleBoundSessions`, default
**false**. While false the hosted scan returns before it lists sessions. Enabling
it is an operator change after S2 is on the running host, not part of landing S2.

Eligibility, all required:

- setting true
- Claude, pty host (Herdr stays adopted; the guard already reports Herdr as
  unknown for this purpose)
- runner session Running and `Adopted`
- a fresh snapshot for this session and generation, `ScreenState` Clear,
  `InputUncertain` false, screen not Blocked
- the agent is channel-bound (`ChatChannels` any row for that agent, the same
  shape as `AgentTaskDispatcher.IsChannelBoundAsync`) or is `AlwaysOn` and has
  the orchestrator instruction bundle attached (`InstructionBundles.Orchestrator`)

One eligible agent per 10-second pass, oldest adopted start first. The snapshot
is read again immediately before the stamp and the stop. A second read that is
not Clear aborts with no stamp and no stop.

Rejected: re-launch every adopted session, including ordinary delegates and
operator drafts. Rejected: default on. A runner restart would then stop every
bound seat and the existing flush would type the machine backlog the 2026-10-27
activation note forbids. Rejected: doing this inside the runner. Channel binding
and the orchestrator bundle live on the server.

### D-6 — Machine backlog stays pending across the re-launch

Before stop, one transaction sets `SessionQueuedMessage.AutoFlushSuppressedAt`
(nullable, null means eligible) on pending rows for that session whose origin is
not `Channel` and not `Ui`, and whose stamp is still null. Channel rows and UI
rows stay eligible, which is how a withheld Telegram message becomes deliverable
on the new generation. Delegation, Check, System, Supervision, Scheduled, and
Mention rows already pending stay pending and are skipped by the flush.

The flush skip continues to the next sequence. A suppressed head must not block
a later channel row. The skip does not charge `DeliveryAttempts` and does not
clear the row. Cancel still works.

A move onto a different session id already clears the withhold fields in
`AgentControlService` (`AcceptAsync`). That path also clears
`AutoFlushSuppressedAt`, so a deliberate transfer can deliver. The same-id resume
does not clear it.

If stop throws after the stamp, the stamp remains. The seat is still adopted, so
those machine rows were not going to be typed anyway. Channel rows were not
stamped.

### D-7 — Defect B is another card

Debug 59ab5d9a defect B: one stuck `FlushSessionAsync` stalls the whole inbound
drain, so later messages never gain a `QueueMessageId`. Master at that date still
had the unbounded lock wait. This plan does not bound that wait, move the flush,
or add a drain heartbeat. The sibling incident loop still pages rows that were
queued and withheld before the drain stuck. Messages that never queued stay
silent until defect B has its own card.

### D-8 — No new input-wait, no release deadline

This plan does not add a session that waits for input. S2 stops and starts an
existing seat through the current agent control path. Parking stays off.
CARD-1083's "no automatic release deadline" is unchanged.

## Ground truth

| What the card or the 2026-11-04 debug assumes | Code at `6cda98f3c` | Consequence for this plan |
|---|---|---|
| One "observation unavailable" string hides adopted, uncertain input, stale generation, null snapshot, and screen Unknown | `DialogDeliveryAdmission.Describe` returns `SnapshotUnavailable` (no charge), `GenerationMismatch`, `BlocksInput` (no charge), `InputUncertain`, `ScreenUnknown`, `AdoptedSession`, or `ObservationUnavailable`. The guarded-input `Code` stays `observation-unknown` except for a real block | Distinct queue text is landed. Do not redo it (D-1) |
| The cause is not on a public route | `GET /api/sessions/{id}/messages` carries `modalBlockedReason`. `GET /api/sessions/{id}/pending-notes` carries `withholdReason`, `withholdCount`, `firstWithheldAt`, `lastWithheldAt`, `lastWithholdReason` and does not flush. Runner session DTO carries `Adopted`. There is no `GET /api/sessions/{id}` | The debug 404 is that missing single-session route. The queue and pending-note routes are the public surface. No new session GET |
| Notes sit at 0 attempts so they never age | `ChargeDurableWithholdAsync` increments `WithholdCount` once per distinct reason and leaves `DeliveryAttempts` at 0. `PendingNotesAged` (attention kind 61) keys off `WithholdCount` and `FirstWithheldAt` | Aging is the withhold record, not the attempt counter. Family rows 130 and 132 already had `LastWithholdReason` "adopted session" |
| Nothing alerts | `PendingNotesAged` is a Warning attention item, minimum 1, age 900s, OpenDrawer only. It is not an `AgentIncident` and it is not in `DigestSettings.WakeOnIncidentKinds` (default `ChannelReplyLost`). Aged channel holds incident only for continuity, suspension, and liveness, and those return when `QueueMessageId` is set (`ChannelBridgeService.RecordWakeIncidentAsync`) | S1 adds the missing `ChannelReplyLost` for a queued withhold |
| Adopted idle can be made writable by a better observation | Two Final Reviews parked that approach (tasks 6a7fae42 and c2dd203a): historical idle, a stale fence, and an operator draft all allowed a write the baseline refuses | No slice weakens `ProviderDialogObservationService.Refusal` or types into an adopted host |
| Stop then start resumes the same conversation non-adopted | Same-id resume in `StartInteractiveSessionAsync` (around the `previous is not null` arm) plus `StartAsync` leaving `_adopted` at its false default | S2 calls that path. It does not invent a second launcher |
| Re-launch would type the 340-note backlog | After a non-adopted Clear screen, `DeliverNextLockedAsync` submits pending rows in sequence order | S2 stamps machine origins first (D-6) |
| 13 of 14 sessions were deaf, including orchestrators | Adoption is every surviving pty host. Orchestrators are not channel-bound, so a channel-only re-launch would leave them deaf | S2's second eligibility arm is AlwaysOn plus the orchestrator bundle |

Landed commits, all ancestors of this base: `2f00a03df105e217b1a796ee6a4954baf3e0b04f`
(name, charge, alert), `e5beafabb218cf3d7d4ba362e2db06d14669552c` (withhold
columns apart from attempts), `ce4ece4a4d5bc3b98d8e61c9370e33bbce7c96e6` (never
attempted), `26eae0852de98b9a45e7c3300eff7c21bf7fafab` (timestamp compare).

## S1 — One channel incident for a withheld queued row

### Files

- `server/Domain/Entities/ChannelInbound.cs` — `InputWithheldIncidentAt`
- `server/Application/Services/ChannelBridgeService.cs` — start the sibling loop
  from `ExecuteAsync`
- `server/Application/Services/ChannelBridgeService.WithheldIncident.cs` — the scan
  and the insert
- `server/Migrations/<timestamp>_ChannelInboundInputWithheld.cs` — nullable column
  only. No index. Follow `ChannelInboundHoldIncidents`
- `tests/Antiphon.Tests/Application/ChannelInputWithheldIncidentTests.cs`
- `docs/telegram.md` — add `ChannelInputWithheld` beside the continuity,
  suspension, and liveness sentences (the list around lines 67–71)
- `docs/session-runtime-invariants.md` — one sentence: the sibling loop records
  the incident and does not flush

### Behaviour

Each pass loads the D-4 set, oldest `FirstWithheldAt` first, at most 64, and
inserts one incident per inbound inside a transaction that also sets the stamp.
A second pass sees the stamp and inserts nothing. The incident `Kind` is
`ChannelReplyLost`, `Severity` Critical, `FailureReason` exactly
`ChannelInputWithheld`.

### Fail-safe default

The threshold is the existing 10 minutes. The loop writes an incident row and the
stamp. It does not flush, type, stop, or change `DeliveryAttempts`.

### Witnesses, written first

New class `ChannelInputWithheldIncidentTests`, Integration, `NotInParallel("MessageQueue")`,
one isolated schema per test via the existing bridge harness. Red at this base
because `RunWithheldIncidentLoopAsync` / `ScanWithheldChannelIncidentsAsync` do
not exist, so the witness commit fails to compile. Methods:

1. `C1591_AgedWithheldChannelRowRaisesOneIncident`
2. `C1591_RepeatScanStaysOneIncident`
3. `C1591_YoungWithholdStaysQuiet` — `AcceptedAt` old, `FirstWithheldAt` inside the window
4. `C1591_WorkingUnchargedRowStaysQuiet` — Pending channel, `WithholdCount` 0
5. `C1591_ScanWritesNoInput` — harness inputs, submitted bodies, and conditional
   inputs stay empty, and the queue lock is not required for the scan to finish

`DialogDeliveryAdmissionTests.C1591_NamesEachWithholdAndNeverAllowsAWrite` stays
in the same checkpoint as the guard regression (R-1). It is already green at this
base. It is not part of the red witness commit.

### Mutants

| Id | Mutation | Killed by |
|---|---|---|
| M1 | Skip the stamp write | `C1591_RepeatScanStaysOneIncident` (count becomes 2) |
| M2 | Age from `AcceptedAt` or `CreatedAt` | `C1591_YoungWithholdStaysQuiet` |
| M3 | Incident when `WithholdCount` is 0 | `C1591_WorkingUnchargedRowStaysQuiet` |
| M4 | Call `FlushSessionAsync` from the scan | `C1591_ScanWritesNoInput` |
| M5 | New incident kind, or reason text other than `ChannelInputWithheld` | method 1's kind and reason assertions |

### Risks

A 10-minute-old adopted withhold pages Critical once per inbound. A debounced
batch with several inbounds and one queue row pages once per inbound, matching
the existing hold incidents. The body is not copied into the incident. While the
drain is stuck, already-queued withholds still page; never-queued messages do not
(D-7).

### Not in S1

No re-launch, no flush change, no `DeliveryAttempts` change, no reason-order
change, no bound on the queue lock.

## S2 — Re-launch idle adopted bound seats, default off

### Files

- `server/Application/Settings/SupervisionSettings.cs` —
  `DeliveryVerificationSettings.DeadoptIdleBoundSessions`, default false
- `server/Application/Services/IdleAdoptedRelaunch.cs` — pure `Decide`
- `server/Application/Services/IdleAdoptedRelaunchService.cs` — scan, stamp, stop, start
- `server/Infrastructure/Hosting` registration beside the bridge (new hosted
  service, not inside `ChannelBridgeService`, so an orchestrator still qualifies
  when the bridge is the component under test separately)
- `server/Domain/Entities/SessionQueuedMessage.cs` — `AutoFlushSuppressedAt`
- `server/Infrastructure/Data/AppDbContext.cs` — no extra index
- `server/Migrations/<timestamp>_SessionQueuedMessageAutoFlushSuppressed.cs`
- `server/Application/Services/SessionMessageQueueService.cs` — skip a stamped
  pending row and keep walking sequence order, inside `DeliverNextLockedAsync`
- `server/Application/Services/AgentControlService.cs` — clear the stamp next to
  the withhold clear on a different-session transfer
- `server/Application/Dtos/SessionQueueDtos.cs` and `client/src/api/sessions.ts` —
  additive nullable `autoFlushSuppressedAt` on the pending-note DTO. Old clients
  ignore it
- `tests/Antiphon.Tests/Application/IdleAdoptedRelaunchTests.cs` (Unit)
- `tests/Antiphon.Tests/Application/IdleAdoptedRelaunchFlushTests.cs` (Integration,
  `NotInParallel("MessageQueue")`)
- `docs/session-runtime-invariants.md` and `docs/ops-http.md` — the setting's
  default, the fence, and the new pending-note field. The ops sentence says the
  field is informational and authorizes no write

The hosted service takes an `IAgentSessionControl` port that production binds to
the existing stop and start methods. Tests pass a fake that records calls and
does not launch a process. The runner snapshot is the existing dialog observation
fake. No pty, no live runner.

### Behaviour

`Decide` returns `Skip` or `Relaunch` from D-5. `Relaunch` carries the agent id
and session id and no input string. The service, for one winner per pass:

1. Re-reads the snapshot. Not Clear means return.
2. Stamps eligible machine rows (D-6).
3. Stops, then starts, the agent with the default resume (not `fresh`).
4. Assertable side effects: the fake control sees one stop and one start, and
   the adopted runner client's input list stays empty.

### Fail-safe default

The setting is false. Landing S2 does not stop any seat. The fence column
defaults null, so an unstamped row flushes exactly as it does today.

### Witnesses, written first

Red at this base because `IdleAdoptedRelaunch.Decide` and
`AutoFlushSuppressedAt` do not exist, so the witness commit fails to compile.

Unit `IdleAdoptedRelaunchTests`:

1. `C1591_DefaultSettingDoesNotRelaunch`
2. `C1591_ClearBoundAdoptedSessionRelaunches` — one `[Test]` method, not
   `[Arguments]`: channel-bound, and AlwaysOn with the orchestrator bundle
3. `C1591_IneligibleSessionDoesNotRelaunch` — one `[Test]` method, not
   `[Arguments]`: blocked screen, uncertain input, screen Unknown, not adopted,
   Herdr, ordinary delegate, setting false
4. `C1591_DecisionCarriesNoInput`

Integration `IdleAdoptedRelaunchFlushTests`:

5. `C1591_SuppressedHeadDoesNotBlockTheChannelRow` — sequence 1 Delegation
   stamped, sequence 2 Channel unstamped, Clear non-adopted session: the channel
   body is submitted once, the delegation body stays Pending with
   `DeliveryAttempts` 0
6. `C1591_UnsuppressedChannelRowStillTypesOnce` — no stamp, the channel body is
   submitted once (flush regression)
7. `C1591_TransferClearsTheStamp` — the stamp clears only when the row moves to
   a different session id. The same-id resume leaves it set

### Mutants

| Id | Mutation | Killed by |
|---|---|---|
| M1 | Default the setting true | method 1 |
| M2 | `Relaunch` on Blocked, uncertain, Unknown, Herdr, or an ordinary delegate | method 3 |
| M3 | Put an input string on the decision, or call conditional input on the adopted client | method 4 and the service test's empty adopted input list |
| M4 | Forget the stamp, or stamp Channel rows too | method 5 (delegation body submitted, or channel body left pending) |
| M5 | Stop the walk at the first suppressed row | method 5 (channel body not submitted) |
| M6 | Clear the stamp on the same-id resume | `C1591_TransferClearsTheStamp` |

### Risks

Enabling the setting stops a live process. The second snapshot cuts the race
where the screen becomes a dialog or Working between passes. A Working turn that
starts after the second read and before stop can still be killed. That window is
the existing stop path's window. The default stays off so landing does not take it.

The new process's resume note is typed by the existing launcher. Channel rows
then wait for a Clear observation on the new generation. A trust dialog on the
new process withholds them again through the unchanged guard, and S1 will have
paged them already.

Thirteen seats at one per 10 seconds take a few minutes of passes, plus each
start's own boot. The scan does not parallelize starts.

### Not in S2

No typing into the adopted host. No flush of Delegation, Check, System,
Supervision, Scheduled, or Mention rows that were already pending. No change to
the dialog guard. No enablement of the setting in configuration. No repair of
the wake-worker lock wait.

## Performance budget

No new test has a measured p95. The figures below are provisional. The first
green checkpoint receipt for each row is the sample. If that row's test-body p95
is above 60 seconds, the per-test timeout becomes twice that p95 in the same
slice before Review, and this table's `EstimatedMinutes` is updated to the
measured row wall. A row whose wall exceeds 30 minutes is a Debug task with the
receipt, not a retry.

| Item | Budget |
|---|---|
| CP-1 wall | 12 minutes provisional, including a cold `Antiphon.Tests` build. Test body expected under 8 minutes for 5 integration methods plus 1 unit method |
| CP-2 wall | 15 minutes provisional, including its own cold build (different `After`, so it cannot reuse CP-1's output). Two integration methods dominate |
| Per integration test timeout | 120 seconds, provisional, about 2x a 60-second cold-schema allowance. Not a measured p95 |
| Unit tests | runner default timeout. No database |
| Fixture lifetime | `TestDbFixture` container once per test process. Each integration method creates one isolated schema and drops it. No AppHost, no per-assertion schema, no live runner |
| Phases | schema create, seed, scan or flush, assert. Postgres is the only external call, bounded by the test timeout. Record wall and phase time in the checkpoint receipt (`CHECKPOINT-TIMING`). Unavailable phases stay unknown |
| Queue versus execution | Checkpoint admission is the slot wait (default 45 minutes, exit 4). `EstimatedMinutes` is execution only. A slot timeout is the row not run |
| Over 30 minutes | No row is planned over 30 minutes. The full `Antiphon.Tests` assembly is not a checkpoint |
| Categories | Server plus messaging. Both new classes sit in `Antiphon.Tests`. No pty project, no client suite. The pending-note field on `sessions.ts` is a type-only additive field; CP rows do not run Vitest. A client compile is unlisted if Code touches more than the type |

Shared setup and the 120-second cap are the R3 and R5 obligations for this plan.
R1 does not trigger on the planned rows.

## Verification design

### Inspection

S1 is a database read plus one insert, covered by the five new integration
methods and the existing admission unit test. S2's decision is pure and covered
by four unit methods. S2's flush skip is one integration method plus the
unsuppressed twin. The transfer-clear is the seventh method (see S2 mutants).
No native pty, no Windows-only branch, no live Telegram.

### V-n

| Id | Statement |
|---|---|
| V-1 | An aged channel withhold produces one Critical `ChannelReplyLost` / `ChannelInputWithheld` |
| V-2 | A second scan does not insert another incident |
| V-3 | Age is `FirstWithheldAt`, so a young withhold with an old acceptance stays quiet |
| V-4 | `WithholdCount` 0 stays quiet |
| V-5 | The scan submits no input |
| V-6 | The re-launch setting defaults off and `Decide` returns Skip |
| V-7 | Clear adopted channel-bound, and Clear adopted AlwaysOn orchestrator, return Relaunch |
| V-8 | Blocked, uncertain, screen Unknown, not adopted, Herdr, and an ordinary delegate return Skip |
| V-9 | The decision carries no input |
| V-10 | A suppressed delegation head does not block the following channel row, and an unsuppressed channel row is typed once |
| V-11 | Moving a stamped row to a different session clears `AutoFlushSuppressedAt` |

### R-n

| Id | Statement |
|---|---|
| R-1 | `DialogDeliveryAdmission.Describe` still returns null only for a Clear known observation, and every other sample stays a withhold. Adopted plus a blocked screen stays `BlocksInput` with no charge |
| R-2 | An unsuppressed channel row on a Clear non-adopted session is still submitted once |

### Guard inventory

`ProviderDialogObservationService.Refusal` and `DialogDeliveryAdmission.Describe`
stay the only write allow. S1 never reaches them. S2's adopted host receives no
conditional input. The new process uses the existing launcher and the existing
guard. No raw-input fallback.

### PC-n

The mutant tables in S1 and S2 are the positive controls. Each is one method-scoped
red-then-green cycle after land, on the test named in the table. They are not
checkpoint rows.

### Out of scope

Parked typing fixes (tasks 6a7fae42 and c2dd203a). Incrementing `DeliveryAttempts`.
A new alert sink. `GET /api/sessions/{id}`. Reordering screen-Unknown ahead of
adopted. Paging no-charge withholds. Bounding the wake-worker queue-lock wait
(defect B). Enabling `DeadoptIdleBoundSessions` in a deployed configuration.
Flushing the pre-existing machine backlog. Any keystroke into an adopted session.

### Cost

Ordinary Code floor is 12 + 15 = 27 minutes of checkpoint execution, plus
authoring. Slot wait is extra. `repeat-proof: at most 3 normal + 2 loaded
repetitions per unchanged proof selection; none required after green.`

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1591-s1/` | withheld-incident | `/*/*/(ChannelInputWithheldIncidentTests*)\|(DialogDeliveryAdmissionTests*)/*` | V-1, V-2, V-3, V-4, V-5, R-1 | all 6 methods, 0 failed/skipped | 6 | 12 |
| CP-2 | S2 | `tests/Antiphon.Tests -> bin-c1591-s2/` | idle-relaunch | `/*/*/(IdleAdoptedRelaunchTests*)\|(IdleAdoptedRelaunchFlushTests*)/*` | V-6, V-7, V-8, V-9, V-10, V-11, R-2 | all 7 methods, 0 failed/skipped | 7 | 15 |

`DialogDeliveryAdmissionTests` is one method. `ChannelInputWithheldIncidentTests`
is five. `IdleAdoptedRelaunchTests` is four (`C1591_DefaultSettingDoesNotRelaunch`,
`C1591_ClearBoundAdoptedSessionRelaunches`, `C1591_IneligibleSessionDoesNotRelaunch`,
`C1591_DecisionCarriesNoInput`). `IdleAdoptedRelaunchFlushTests` is three
(`C1591_SuppressedHeadDoesNotBlockTheChannelRow`,
`C1591_UnsuppressedChannelRowStillTypesOnce`, `C1591_TransferClearsTheStamp`).
No other class carries these names, so the trailing `*` on each class operand does
not pull a neighbour. Both rows are Integration-capable. CP-2's unit methods do
not need the database. Neither row is Serial. Neither row sets Environment.

Code runs this table through `dotnet run --project tools/Antiphon.Checkpoints --
run --plan docs/superpowers/plans/2026-11-05-card-1591-salvage-plan.md`, one run
per `After` group, from a clean commit of that slice. Witness commit first, red
because the new types are absent. Fix commit second, then the green row.
