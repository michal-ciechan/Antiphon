# CARD-0242: Codex turn-abort ingestion

Date: 2026-10-19. Stage: Plan. Target: **master only**.
Plan source: `d5dec9555bd726ad11bb30073c1733d6b3e7bb0e`.
Assigned branch: `feat/card-task-3e1ff59c`.

**The requested ingestion fix has already landed on master.** Preserve that
implementation; do not dispatch another Code task to add its switch arm. The live
Linux runner advertised a different revision whose source lacks the arm. The next
stage is **Investigate** to reconcile activation and any remaining incident, with
the measurements below. No product changes, dispatch, RC promotion, restart or
deployment are part of this Plan.

TestDesign was not folded into this dispatch. The checkpoint allocation below is
conditional input for that separate stage if investigation establishes new code
work or a specific missing verification obligation. It is not permission to skip
TestDesign or rerun already accepted evidence merely to recreate it.

## Evidence and provenance

CARD-0242 is card `d037e481-5787-43b1-ba5d-dc6561797eca` on Antiphon board
`8988ca03-7414-47ad-b0b6-51556c701703`. Its full description, eight revisions,
card thread and the original Code/Review reports were read. The description still
states the pre-fix premise; revisions 6 and 7 record a later Code/Review cycle.

| Evidence | Immutable reference | Meaning and limit |
|---|---|---|
| [Original investigation](../../investigations/2026-10-17-card-0242-investigation.md) | `feat/card-task-58bc3539`, `b494ec2199b3c7a86d2454cef106e67fa83b4cb8` | Correct pre-fix diagnosis; reconstructed abort input, not a local live Esc capture. |
| [CARD-0159 plan, section 1.4](2026-08-30-card-0159-settlement-positive-evidence-plan.md#14-what-the-interrupt-path-does-on-the-other-kinds) | Historical card reference | Explains why an interrupt must not become a successful report; its Codex ingestion description is now stale. |
| Original Code task | `e576977a-4b57-4240-942b-d8031000c9a7`, branch `feat/card-task-e576977a` | Tests-first commit `7ca72a6a6eaaa26045886707a6095d01e8653331`; fix `1224f48867daf326d9e70b4c8832d5e04e0403f2`. Report records 1 failed assertion before the fix, then 1 passed test. |
| Ordinary Review | `f113cf09-4990-4eee-9105-aeed8f2e8793`, reviewed SHA `1224f48867daf326d9e70b4c8832d5e04e0403f2` | Reports Clean Final/Full for its scope, 17/17 normalizer tests, zero failed/skipped, clean SHA-validated receipt. This Plan did not rerun them or independently retrieve the receipt files. No PCs were discharged. |
| Structured landing outcome | Operation `ab646321-da53-485c-84f6-71a00cc5b1c9` | `publication=Landed`, `cleanup=Complete`, destination `refs/heads/master`, verified and remote SHA `f5df4d3f6eab8ef298eafb3f79e228f215c46bc4`, confirmed 2026-10-17T10:47:24Z. |
| Current source | Master and task base `d5dec9555bd726ad11bb30073c1733d6b3e7bb0e` | Contains landed test `90b1dafc752935410853aeacbd6efecec654197a` and fix `f5df4d3f6eab8ef298eafb3f79e228f215c46bc4`; ancestry checked. |
| [CARD-0907 parser plan](2026-10-19-card-0907-codex-0160-parser-plan.md) | `feat/card-task-046b1e67`, tip `08bdb7087f3903fcc7f2980ff520a3a4997c250a` | Read with `git show`; the document may not yet be in this branch. Owns current working/queue captures, parser repairs and dialect-comment correction. |
| CARD-0907 parser investigation | `feat/card-task-359b4ff7`, `c5543c29839e9c9d6d19e012daedb927551a78af`, `docs/investigations/2026-10-18-codex-0160-parsers.md` | Exec rollout observed; abort not emitted in that probe. Confirms the arm existed in its checkout, not necessarily in the deployed runner. Full exec JSONL was not committed. |
| CARD-0907 readiness investigation and adjacent captures | `feat/card-task-13a42530`, `87fad6edb6e8bc80ff27c25419b869f7d84e2aac`, `docs/investigations/2026-10-18-codex-0160-ready.md` | Reuse existing idle/trust/readiness evidence; no submitted interrupted turn was captured there. |

All named task branches above were fetched. Use their immutable documents and
CARD-0907's eventual curated rollout; do not copy or recreate its working, queue,
trust, update, ready, or dialect-comment slices under CARD-0242.

Live HTTP reads on 2026-10-19 returned runner-defaults revision 2, no per-kind
overrides, eligible accepting Linux and Windows runners reporting Codex 0.160.0,
and an unavailable spare. The default selected the Linux runner. The observed
Linux runner's `buildVersion` was
`09e48c5d6f1d404cfea8ec0d992270424fd2a184`, process boot
`0e14a3d4-ba81-4ae9-a956-515188b82233`. Reading the normalizer at that exact Git
revision showed **no** `turn_aborted` arm. The server's `/api/version` was
`df6b422c71b0828b4bd7f855a86e2a51d1987e79`; server SHA and CLI version do not
establish the runner's ingestion implementation. Advertised build identity plus
its source is evidence of an activation gap, not a newly measured live abort.

Placement remains dynamic: re-read `GET /api/runner-defaults` and
`GET /api/session-runners` before subsequent work. Offline checkpoints use the
portable .NET lane; omit `-Runner` and `-Platform`. Use `-Platform Any` only to
clear an inherited OS pin. An actual PTY measurement names its OS/backend lane
and pins a host only if that host is the subject of the incident. No fleet
address, container path or deployment destination is an execution default here.

## Ground truth

| Card/brief assumption | What the code or evidence does | Consequence |
|---|---|---|
| Codex aborts are discarded by the current normalizer. | `src/Antiphon.SessionRunner/CodexTranscriptNormalizer.cs`, `Normalize` and `FromTurnAborted`, already emit one `TurnEnd/cancelled`. | Do not implement a duplicate fix. Distinguish current master from advertised deployed source. |
| Codex stores no stop reason. | `task_complete` synthesizes `end_turn`; `turn_aborted` synthesizes exactly `TranscriptKinds.StopReasons.Cancelled`. | Raw CLI fields and normalized fields differ deliberately. |
| A missing turn id or reason should prevent ingestion. | `turn_id` is optional; `reason` is not inspected. `ApiCallId` is the bounded turn id or null. `IsApiError` remains unset. | Keep the tolerant existing contract, including other abort reasons. The current witness tests interrupted with/without turn id, not every reason. |
| Any turn end can settle partial narration. | `TranscriptKinds.IsReportBoundary` excludes exactly `cancelled`; `TaskReportTurnSelector` returns Interrupted; `AgentTaskReplyService.RecordInterruptedTurnAsync` writes a deduplicated warning and leaves task status untouched. | Session idle and task success are separate. A Working task stays Working; a Dispatched task can stay Dispatched. |
| An interrupted session must remain working until a timer. | Runner `TranscriptWorkingState` and server `TranscriptWorkingStateQuery` treat `TurnEnd` as an end. `AgentSessionRuntime.IsTurnBoundary` includes cancelled and schedules the existing queue path. | Once ingested, the session can be idle and pending WhenIdle delivery can proceed without treating the abort as a report. Deferred effects are not synchronous receipt proof. |
| Idle means the process or seat is released. | Idle inference does not terminate the session. Parking is disabled by default. Task deadlines and process ownership are separate. | There is no automatic release deadline for a session waiting for input while parking is off. |
| The phase timeout remains the mechanism after a valid abort. | `TaskDeadlinePolicy` consults working state before applying a phase deadline. Its role ceiling is separate; a silent first prompt has CARD-1151 boot handling. | Do not change timeouts or promise an exact physical seat-release time from a task ceiling. |
| Codex being live at 0.160.0 means this source is activated. | Advertised runner build source lacks the landed arm, despite the CLI version. | Investigate activation first; master publication alone cannot qualify production behavior. |
| CARD-0907's captures prove interrupted-turn ingestion. | Existing probes did not produce an abort. Its parser plan explicitly preserves the normalizer logic. | Reuse its captures for shared context, but label abort coverage synthetic until a matching raw event is observed. |
| A killed child should also produce this boundary. | No event can be ingested if Codex dies before writing it; restart boundaries own that case. | Do not infer aborts from silence, process exit or a screen word. |

## Decisions

**D-1 — Preserve the landed contract.** The smallest implementation is already
present and reviewed. Reject a second switch arm, a new transcript kind, or a
synthetic Claude interrupt UserPrompt: they add no missing behavior and a prompt
would alter delivery attribution. Reject `end_turn`, raw `interrupted`, or null
as the normalized stop reason; they can admit a partial report.

**D-2 — Investigate the source/runtime mismatch before new Code.** Measure the
actual incident's runner build, session generation and transcript frontier. The
advertised source omission is sufficient to reject the premise of a new master
implementation. It is not authorization to promote to RC or restart a runner.
Any release action belongs to the caller under the release/runbook gates.

**D-3 — Preserve lifecycle policy.** A cancelled boundary makes the session idle
and allows the existing queue flush; it does not mark the delegate successful,
failed, blocked or canceled. Reject automatic retry, synthetic continuation and
new stall-kill policy. These would spend work or change ownership beyond ingestion.

**D-4 — Keep CARD-0907 separate.** Reuse its fixture/provenance and capture harness
when available. Do not duplicate its parser grammar work, change readiness
timeouts, remove provider history or change launch flags in this card.

**D-5 — Preserve known limits visibly.** Abort usage is not stamped or reset by
`FromTurnAborted`; spend can roll into the next `task_complete`. Review also noted
consumers that treat any non-error TurnEnd as progress and unmeasured `replaced`
ordering. These are follow-up investigation candidates, not evidence that the
landed arm is missing and not silently added acceptance for this plan.

**D-6 — Verification remains scoped.** Historical red/green evidence is retained
at its tested SHAs. No suite runs or new tests for the documentation-only Plan.
If new behavior is justified, TestDesign must bind its precise witness, ordinary
checks and mutation obligations before Code. Do not manufacture a current red by
shipping a regression to code that already works.

## Slices

### S0 — Reconcile remaining work and record the runtime evidence

This is the next Investigate task, independently publishable as
`docs/investigations/2026-10-19-card-0242-activation-and-abort-evidence.md` on its
assigned master-targeted branch. No product edit is planned.

1. Re-read the original Code landing and Review outcomes and the affected runner's
   current status. Record exact SHAs, source inclusion, process boot/session
   generation and observation time. Keep master publication and runtime activation
   as separate verdicts. The advertised revision above already lacks the fix;
   establish whether the reported remaining incident occurred on that revision.
2. Reuse CARD-0907's submitted-turn rollout/capture when available. Inspect a
   specifically identified interrupted session's raw abort row and surrounding
   prompt/activity, then its runner-normalized and server-persisted transcript.
   Record turn id, row identity, timestamp and sequence at each boundary. Do not
   bulk-read unrelated service history or call an idle screen a delivery receipt.
3. Measure where progress stops: raw event absent; normalization absent; runner
   row not persisted; cancelled row present but working state stale; deferred
   queue action pending; or task correctly open while its session is idle. For
   queue delivery, require the complete correlated UserPrompt after the queue
   attempt floor. A successful write or a screen redraw is insufficient.
4. If no existing interrupted capture is available, report that exact missing
   observation. A separately commissioned, owned scratch PTY capture may extend
   the CARD-0907 harness with one abort and subsequent prompt; do not re-run its
   whole capture matrix, interrupt an unrelated delegate, or silently buy another
   model turn. Define child cleanup and the paid-turn bound in that capture brief.
5. If the only gap is activation, return the evidence to the caller for release
   handling, with no duplicate source repair. If a current-source failure is
   measured, return the first failing boundary and a narrow regression candidate
   for Plan/TestDesign. If it is merely an open task with an idle session, record
   the intended policy and request a separate policy decision only if needed.

Tests/files to consult: `CodexTranscriptNormalizerTests.cs`,
`CodexTranscriptTailerTests.cs`, `TranscriptWorkingStateTests.cs` under
`tests/Antiphon.SessionRunner.Tests/`; `TaskDeadlinePolicyTests.cs` and
`AgentTaskReplyIntegrationTests.cs` under `tests/Antiphon.Tests/Application/`.
Do not execute all those classes as discovery. Inspect and select only the
boundary demonstrated by the measurement.

### S1 — Abort normalization, already independently landed

Files: `src/Antiphon.SessionRunner/CodexTranscriptNormalizer.cs` and
`tests/Antiphon.SessionRunner.Tests/CodexTranscriptNormalizerTests.cs`.

The historical tests-first slice adds
`Turn_aborted_is_a_cancelled_boundary_and_not_end_turn`; the following fix adds
the arm and `FromTurnAborted`. On the tests-only commit the single-item assertion
saw zero rows (1 failed test); on the fix it passed (1 test). The witness asserts
one TurnEnd, exact cancelled reason, optional turn attribution and no API error.
Its two input cases are internal assertions, **one** TUnit execution.

S1 is complete at `f5df4d3f6eab8ef298eafb3f79e228f215c46bc4` on master. Do not
re-land the original branch or author its implementation again. There is no S2
product slice without new evidence from S0. Any resulting slice must name the
actual failing file and test; activation alone creates no source slice.

## Waiting-session ownership

This plan adds no production wait or release mechanism. After the existing abort
boundary the live child remains owned by its session, awaiting queued input or
explicit owner action. A new prompt resumes work; it is not itself a release.
Explicit cancellation/stop and the existing terminal cleanup path can release
the process. With parking disabled there is **no automatic input-wait release
deadline** (CARD-1083). An enabled conditional parking path has its own configured
rules. The configured task ceiling (historically 240 minutes) is not a promise
that the process is killed or its seat returned at that time. Record confirmed
ownership/release separately from a task's terminal status.

## TestDesign handoff after the investigation gate

For ingestion alone, V-1 is the existing single abort method. The historical red
witness detects dropping the arm; the exact reason assertion also detects changing
it to end_turn. Preserve those recorded outcomes instead of claiming new runs.
R-1 preserves successful completion and R-2 preserves completion with API error.
No downstream code change is selected. If S0 demonstrates a downstream failure,
TestDesign must add a specific producer-to-recipient witness and its exact lane;
the generic settlement test
`AgentTaskReplyIntegrationTests.a_cancelled_turn_end_does_not_settle_the_task_and_says_so`
already protects cancellation semantics but is not a live Codex ingestion test.

Any new repair requires its failing test committed and pushed before the fix,
an actual assertion failure rather than a build error/zero tests, then the same
exact filter green. Later positive controls remain method-scoped and separately
commissioned; this Plan neither executes nor discharges Mutation.

The following is the **conditional** portable ingestion regression allocation
for TestDesign to finalize if such a run is justified. These rows are not a new
mandatory rerun of the accepted original Review. Run through the checkpoint tool
with the committed source SHA and selected slice rows; bootstrap/build drivers
take `scripts/build-slot.ps1`, and checkpoint rows take their own slot. Await a
running tool until completion (exit 75 means keep waiting). Use fresh isolated
outputs after changes; retain unedited CHECKPOINT lines, counts and source receipts
in ignored evidence. Validate clean build provenance and remove owned alternate
outputs through the documented cleanup procedure. A slot timeout stays not run.
Code/Review also run `scripts/check-evidence-diff.ps1` over their base..HEAD.

### Cost

This Plan requires no build/test run. S0 is estimated at 20–35 minutes of read-only
evidence correlation, excluding an independently commissioned paid capture or
release action. New ingestion implementation estimate: **0 minutes**, already
landed. Conditional checkpoint ordinary floor: **5 minutes** (3 + 1 + 1), plus
slot wait; an actual new defect's red pass adds about 3 minutes and authoring must
be estimated from that defect. No new PC rows are commissioned, so this plan adds
0 minutes of Mutation execution; original missing-control discovery is not
claimed complete. No full assembly, namespace, native or paid canary run is hidden
inside these estimates.

### Checkpoints

Proposed allocation only; current handoff is Investigate. `After=S1` identifies
the existing landed slice, not a request to implement it again. All three methods
exist at plan source. Each Group names the portable offline lane.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | S1 | `tests/Antiphon.SessionRunner.Tests -> bin-c242-abort/` | portable-offline-abort-witness | `/*/*/CodexTranscriptNormalizerTests/Turn_aborted_is_a_cancelled_boundary_and_not_end_turn` | V-1 | all listed, 0 failed | 1 | 3 | true |
| CP-2 | S1 | CP-1 | portable-offline-completed-turn | `/*/*/CodexTranscriptNormalizerTests/TurnEnd_carries_the_stop_reason_the_turn_boundary_rule_keys_on` | R-1 | all listed, 0 failed | 1 | 1 | true |
| CP-3 | S1 | CP-1 | portable-offline-api-error | `/*/*/CodexTranscriptNormalizerTests/An_errored_task_complete_is_still_a_turn_end_and_carries_the_error` | R-2 | all listed, 0 failed | 1 | 1 | true |
