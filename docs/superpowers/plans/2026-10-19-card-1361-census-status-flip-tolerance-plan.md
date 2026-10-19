# CARD-1361: tolerate open-state status changes in redeploy censuses

Date: 2026-10-19. Plan task: `6af5a239-b10c-4133-a8ff-4472e20ec9de`.
Branch: `feat/card-task-6af5a239`.
Source inspected: CARD-1353 reviewed tip `cd5e2c5fb1c94307f5b226a52cb0eb86cca9f515`.
Card: `3579a748-f6a3-4d8f-8cef-4446e1ba803f`, Antiphon board, revision 0.

## Outcome and scope

Permit an existing off-runner task to move among `Queued`, `Dispatched`, `Working`
and `Blocked` between list/detail reads and between the two passes of each census.
Both comparisons must preserve every other reduced task field and require explicit
null or terminal-proven land evidence. A changed runner, project, scope source,
land timestamp or land proof remains a refusal. Bound work remains a refusal.

Change only the PowerShell wrapper census, the shell host census, their offline
fixtures, and the census exception paragraph in `docs/docker-stack.md`. Preserve
CARD-1353's bound-only land snapshot, off-runner land handling, counts and bounded
land wait. This task is master-only; do not run any live `deploy-server2.ps1` phase,
restart a service, or change the rollout in progress.

TestDesign is separate: this dispatch was not commissioned to fold that stage.
The witness and checkpoint scope below are its inputs. TestDesign must add the
executable `## Verification design` and `### Checkpoints` manifest before Code.
No product-policy decision is needed to proceed under the card's stated scope.

## Ground truth

Line anchors describe the inspected source; function and method names are durable.

| What the card assumes | What the code does at the reviewed tip | Plan consequence |
|---|---|---|
| A Queued-to-Dispatched detail can be refused despite unchanged binding and land evidence. | `Assert-RecycleTaskCensus`, `scripts/deploy-server2.ps1:737-750`, skips status equality only for terminal detail with null `landRequest`; `c1008_tasks_collect`, `scripts/c590-remote.sh:5991-6007`, has the same two alternatives in jq. | The premise is confirmed. Add a third, narrowly qualified open-to-open alternative in each implementation. |
| A status-only move between passes is also refused. | `Test-RecycleForgivenDrift`, wrapper `:615-640`, only handles settled disappearance and no-land appearance. Host `:6053-6100` does likewise, then compares complete serialized snapshots. | Add same-ID status reconciliation to comparison copies, followed by the existing full comparison. |
| Pending land must not make a status flip safe. | CARD-1353 intentionally accepts and records **stable off-runner** pending lands, including detail states Queued/Held/Running/NeedsResolution. Unknown or unproven terminal states still fail. | Reject pending evidence as a qualification for the **new exception**; do not restore the old blanket pending-land refusal. |
| Every other reduced key agrees. | Both implementations retain `id`, `status`, `runnerId`, `projectId`, `scopeSource`, `landRequestedAt`, `landStartedAt`. Land evidence is separately retained as null or `{state, terminalEventId}`. | Compare the six non-status fields exactly; across passes, compare both explicit land entries exactly as well. |
| Bound work is already guarded. | The normal wrapper and host reject a bound open row before reading detail. Wrapper `-Report` deliberately bypasses that refusal to count bound work. | Keep guard order and reporting behavior. Explicitly exclude bound rows from the new tolerance, including report-mode comparisons. |
| Existing fixtures only need additions. | `C1324WrapperProbe("changed-row")` and `C1324HostCensusScript` currently use Queued-to-Working with no other change and expect Unstable (`RollingVolumeRecycleScriptTests.cs:1191-1197`, `:1322-1327`). | Replace that negative vector with a status change **plus a runner change** between two off-runner IDs. Add positive status-only witnesses separately. |
| Two-pass tolerance can be achieved by normalizing stored status. | Snapshots and receipts preserve list facts; `c1008_tasks_match_record` and `c994_status_proof` separately compare live facts with saved rollout records. | Do not normalize stored status or change journal comparisons. This card concerns one census's list/detail and two-pass boundaries. |
| Existing fixtures can exercise both scripts without a rollout. | C1317/C1319/C1324 extract shell functions and use sequenced fake HTTP; `C1008WrapperFixture` provides fake reads/details and trace/receipt inspection. `RemoteScriptContractTests.C1087_Host_census_filters_and_names_cause` exercises host census guards. | Use these seams and their process limiter. No real HTTP, Docker mutation or deployment is required for Code evidence. |
| Master publication changes the scripts used by the current rollout. | `docs/release-gates.md` distinguishes master publication from RC activation; product hosts and the rollout use the RC line. | Recommend a separately commissioned RC fix, with CARD-1353 dependency considered explicitly. Master-only completion does not fix the running rollout. |

## Decisions

| ID | Decision and reason | Rejected alternative |
|---|---|---|
| D-1 | The allowed status set is exactly `Queued/Dispatched/Working/Blocked`, case-sensitive as in the existing validators. Permit all directed moves, including Blocked-to-Dispatched and Dispatched-to-Blocked: this is equivalence for census safety, not enforcement of the task state machine. | Permit only forward progress; it would keep rejecting the decision/re-dispatch case named by the card. |
| D-2 | List/detail tolerance requires both statuses open, the row off the recycled runner, all six non-status reduced fields present and equal, and an explicit null or terminal-proven `landRequest`. Terminal proof means `Completed`, `Superseded` or `Canceled` with the existing lowercase GUID-shaped `terminalEventId`. | Ignore status before validating land evidence, or accept any non-pending string; either would admit unknown/malformed evidence. |
| D-3 | Across passes, qualify only IDs present in both open task maps, off the target runner in both observations, with different open statuses, equal non-status fields, and explicitly present, equal, safe land entries. Reconcile only that ID's status in the comparison copy. Keep full snapshot equality as the final gate. | Drop status from all snapshots, delete changed rows, or compare only selected safe keys; each would conceal other drift or rewrite observed evidence. |
| D-4 | An unchanged non-null land timestamp does not itself invalidate this exception when the required null/terminal proof is present; any timestamp addition, removal or value change fails non-status equality. Stable pending off-runner land remains governed by CARD-1353. Pending/unknown proof can never qualify a changed status. | Require every timestamp to be null, or refuse all off-runner pending lands; those would exceed the card and reverse CARD-1353 behavior. |
| D-5 | Preserve CARD-1317 terminal/null settlement, CARD-1319 settled disappearance, and CARD-1324/1326 no-land appearance restrictions. Apply status reconciliation alongside those exceptions and then compare the complete closure. Do not rewrite scope membership, excluded counts or land closure membership for a status flip. | Forgive a whole pass after finding one safe row; it could hide an unsafe companion change. |
| D-6 | Preserve refusal tokens: invalid list/detail qualification remains `RecycleLandUnknown`; residual pass differences remain `RecycleTaskCensusUnknown cause=Unstable`; earlier bound/land guards retain their current tokens and order. | Rename the misleading land token in this fix; callers and fixtures already depend on it, and diagnostic redesign is independent work. |
| D-7 | Preserve actual list status in reduced rows and receipts, and the actual second-pass snapshot returned by a successful census. Only temporary comparison data is aligned. Do not modify `c1008_tasks_match_record`, `c994_status_proof`, journal schema, retry policy, wait defaults or HTTP budgets. | Broaden tolerance between rollout phases or retry all Unstable failures; those are different observation windows and safety contracts. |
| D-8 | Four independent red-first witnesses: list/detail and pass comparison in each language. Commit and push tests before production changes, then one narrow green checkpoint after the implementation/docs group. | One aggregate assertion that can stop at the first failing branch, whole-class/Unit runs, or unmeasured broad testing; none supplies the required branch evidence within the budget. |
| D-9 | Code budget is 45-60 minutes, target 55, with offline Linux execution. Omit `-Runner`; use `-Platform Linux` for Code/Review because native host probes require it. Planning/TestDesign need no OS pin. | Embed a currently available host or force Windows plus WSL; placement can change during the rollout and Linux is the actual test requirement. |
| D-10 | Land the assigned work on master only. This is RC-worthy because it fixes routine continuous-dispatch rollout refusals without relaxing binding/land invariants. RC adoption requires a separate caller-owned task after review, checking whether CARD-1353 is present and adapting the fix to that line if necessary. | Push/cherry-pick to RC here, deploy master scripts, or promise the in-progress rollout is repaired by master publication. None is authorized. |

## Implementation design

### List/detail admission

In the wrapper, compute an `openStatusMove` qualification in
`Assert-RecycleTaskCensus` beside `settledWithoutLand`. Retain the required-member
loop. Skip only the `status` comparison when either the existing settlement rule
or the new fully qualified rule applies. All other keys still use the current
JSON equality check. The ordinary detail land validator, receipt update and
pending-land counters still execute. A missing `landRequest` is never null proof.

Use a small local PowerShell predicate for safe land evidence if shared by the
two comparison sites. It must not replace CARD-1353's ordinary validator: that
validator intentionally admits stable off-runner pending lands. Keep proof
equality separate from proof validity.

In the host's existing jq list/detail expression, add the equivalent third arm,
passing `C1008_RUNNER` as a jq argument to make the binding boundary explicit.
Keep the required summary members, exact equality arm, terminal/null arm and
subsequent land validator intact. A jq helper within the expression may define
open status and safe terminal proof; reuse the existing GUID grammar.

### Two-pass comparison

Extend `Test-RecycleForgivenDrift` with the recycled `RunnerId` supplied by
`Assert-RecycleTaskCensus`. After the existing gone/fresh handling, inspect IDs
in the intersection of the original task maps. Qualify each under D-3, and align
only `prior.open.tasks[id].status` with the live value in that parsed comparison
copy. Include a changed-status count in the existing early-return condition so
status-only drift reaches `Test-RecycleJsonEqual`. The original `$snapshot` and
receipt objects remain untouched.

In `c1008_tasks_collect`, add the equivalent jq reconciliation after the existing
gone/appeared blocks and before the final Unstable check. Require explicit
`has(id)` for both land maps; jq's missing-member-as-null behavior must not supply
evidence. Align eligible prior statuses to current statuses, preserving every
other field. The final byte comparison still rejects any remaining difference;
the success result remains the original current snapshot.

All validators remain before these exceptions. A row moving onto the recycled
runner must fail even if that requires the earlier bound guard instead of the
Unstable branch. Stable report-mode bound rows retain their count behavior;
changed bound rows do not acquire the new tolerance.

## Slices and test ownership

### S1: tests-only red witnesses and retained negative coverage

Files:

- `tests/Antiphon.Tests/Scripts/RollingVolumeRecycleScriptTests.cs`: add
  `C1361_Wrapper_census_tolerates_open_status_changes_between_list_and_detail`
  and `C1361_Wrapper_census_tolerates_open_status_changes_between_passes`, beside
  C1317/C1319/C1324. Reuse `C1319Row`, `C1319Detail`, `C1319Envelope`, sequenced
  `taskQueries`/`details`, and `C1008WrapperFixture`.
- `tests/Antiphon.Tests/Scripts/RemoteScriptContractTests.cs`: add
  `C1361_Host_census_tolerates_open_status_changes_between_list_and_detail`
  and `C1361_Host_census_tolerates_open_status_changes_between_passes` beside
  `C1087_Host_census_filters_and_names_cause`. Extract the real census functions
  using existing `Block`/`LinuxShell` conventions and stub only HTTP/time inputs.
- In both C1324 `changed-row` fixture arms in `RollingVolumeRecycleScriptTests.cs`,
  change the pass-1 runner to a distinct off-runner ID along with the status.
  Keep their Unstable expectations. The old status-only rejection is superseded
  by CARD-1361; the non-status drift invariant remains covered.

Each new test's first positive vector must independently fail at S1 against the
unchanged scripts: list/detail returns `RecycleLandUnknown`; pass comparison
has matching list/detail within each pass but ends `cause=Unstable`. Run all four
exact methods individually against one isolated tests-only build; retain each
assertion failure and SHA. A missing helper, build failure, unavailable jq or
zero-test execution is not a red witness. Commit/push S1 before these runs.

TestDesign must bind these additional vectors to the four methods, retaining
per-vector outcomes so failures cannot be masked by a later PASS:

- All twelve unequal directed open-state pairs with null land evidence; identical
  status remains a baseline control. Use deterministic input sequences, no races.
- Completed/Superseded/Canceled with valid terminal event proof; at the pass
  boundary the proof must be identical in both observations.
- With status drift, independently change runnerId, projectId, scopeSource,
  landRequestedAt and landStartedAt. Use valid per-observation shapes where
  needed so pass cases reach the comparison instead of failing malformed input.
- Pending Queued/Held/Running/NeedsResolution proof, unknown state, missing
  `landRequest`, terminal proof missing/invalid event ID, changed terminal event
  ID and null-to-terminal proof between passes: all refuse the exception.
- Bound open rows, including wrapper report-mode drift; malformed/missing reduced
  keys; an unsafe companion change alongside a safe status move.
- Coexistence with a proven settled disappearance or safe no-land appearance;
  neither exception may erase a scope-count difference or a changed land entry.

Assert outcomes and evidence: exit/error token on refusal; latest list status,
unchanged binding and explicit land proof in the successful snapshot/receipt;
actual detail reads; no real host/deploy action. Keep subprocess tests under
`ParallelLimiter<ProcessSpawnLimit>`. Internal vector counts are separate from
the number of TUnit methods executed.

### S2: symmetric census change

Files: `scripts/deploy-server2.ps1` (`Assert-RecycleTaskCensus`,
`Test-RecycleForgivenDrift`, and an optional local safe-proof helper) and
`scripts/c590-remote.sh` (`c1008_tasks_collect` only).

Implement D-1 through D-7 together. Update nearby comments that currently say
every changed row refuses. Preserve ASCII PowerShell source, shell error handling,
full closure validation and CARD-1353's four land predicates/snapshot/wait logic.
Commit and push this meaningful production slice before the green run.

### S3: documentation and one narrow green

File: `docs/docker-stack.md`, volume recycling census exception paragraph (near
line 258 in the inspected tip). State both observation boundaries, the four-state
set, unchanged off-runner identity/land facts, explicit safe proof, and continuing
refusals. Clarify that stable off-runner pending lands still follow CARD-1353 and
cannot qualify the new status exception. Replace the blanket "changed row"
sentence with the precise remaining refusal boundary.

Review the existing pins in
`tests/Antiphon.Tests/Infrastructure/DockerStackDocumentationTests.cs`; adjust only
an assertion whose old wording conflicts with the new paragraph. No new prose-only
test is required. Commit/push S3, then execute the TestDesign manifest once against
the final committed group. Keep generated TRX, receipts and logs ignored.

## Checkpoint handoff to TestDesign

Read-only placement discovery completed on 2026-10-19 via
`GET /api/runner-defaults` (revision 2) and `GET /api/session-runners`. The catalogue
offers an eligible native Linux lane; availability is a snapshot, not a reservation.
Re-read both routes at dispatch. Do not encode a fleet location in the plan or pin
a host. Required tools are native bash, jq, pwsh and the repository .NET SDK.

| Proposed checkpoint | Lane | Build and selection | Expected evidence |
|---|---|---|---|
| CP-1, after all slices | Native Linux, offline; `-Platform Linux`, no `-Runner` | One isolated `tests/Antiphon.Tests -> bin-c1361/` build; one exact combined class/method filter selecting the four new C1361 methods and the seven regression methods listed below. | Exactly 11 named methods, nonzero execution, 0 failed/skipped, all expected vector outcomes and fresh TRX. TestDesign supplies the executable nine-column manifest and exact filter. |

Retained regression methods in that same narrow green:

1. `RollingVolumeRecycleScriptTests.C1317_Census_tolerates_a_task_that_settles_without_a_land`
2. `RollingVolumeRecycleScriptTests.C1319_Census_tolerates_a_pass0_settled_task_that_leaves_before_pass1`
3. `RollingVolumeRecycleScriptTests.C1324_Census_tolerates_a_no_land_task_that_appears_in_pass1`
4. `RollingVolumeRecycleScriptTests.C1326_Census_refuses_a_land_timestamp_or_land_pending_appearance`
5. `RollingVolumeRecycleScriptTests.C1353_Host_census_passes_off_runner_lands_and_waits_for_bound_lands`
6. `RollingVolumeRecycleScriptTests.C1353_Wrapper_waits_for_a_bound_land_then_proceeds`
7. `RemoteScriptContractTests.C1087_Host_census_filters_and_names_cause`

The four red runs are explicitly required pre-fix witnesses outside the green
manifest. Build once into `bin-c1361-red/` at S1, then run each new method using
`/*/*/<Class>/<ExactMethod>` against that output. All build/test drivers use the
host build-slot gate. The green group uses the checkpoint tool and waits through
exit 75 to completion. Do not run an entire class, Unit lane, assembly, live census
or rollout phase. Delete all task-owned `bin-c1361*/` outputs safely before finishing.
Report retained checkpoint lines, actual method/vector counts, SHA and failures.

Budget: 8-12 minutes for S1 authoring; 8-12 for one red build plus four witnesses;
16-20 for both implementations and docs; 8-12 for the single green build/run;
5 minutes for review, cleanup and publication. Target 55 minutes, range 45-60.
If gate wait or unexpected failures exceed that budget, report the measured cause
and missing evidence; do not broaden testing or weaken assertions to compensate.

## Risks, limits and release disposition

- PowerShell and jq must agree on explicit null versus missing proof and on
  terminal event validity. Independent witnesses in both scripts guard this.
- Across-pass land equality includes terminal event identity, not just state;
  otherwise a new land could be hidden by a simultaneous status move.
- The final full-snapshot comparison is essential: nested excluded-count changes,
  additional unsafe rows and unexpected closure changes remain Unstable.
- Cross-phase saved-record comparisons still include status. A status change
  between those separate proofs may still refuse; measure phase, saved/live reduced
  fields and land proof before commissioning any extension to that boundary.
- This plan neither adds nor changes a session that waits for input. The CARD-1083
  release/deadline checklist does not apply; existing script polling is unchanged.
- **RC-worthy: yes.** The failure interrupts routine dispatch during rollout, and
  the fix preserves the safety predicates. **Assigned publication: master only.**
  Caller-owned RC adoption must account for CARD-1353 and qualify the resulting RC
  scripts. No live rollout result is claimed by this plan or its offline tests.

## Plan validation

The card, both production functions, adjacent fixtures, CARD-1353 plan and owner
documents were inspected. No tests/builds or deploy phases ran in this Plan task.
The assigned source is the required reviewed tip and the worktree was initially
clean. Before publication, validate whitespace, confirm the plan is the only
changed path, push the assigned branch, and check merge-tree against fetched master.
