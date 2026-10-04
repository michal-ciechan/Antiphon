# CARD-0994: remove exited temp containers before volume retirement

Date: 2026-10-04. Plan task: `f1713f40-5ba3-4231-a775-4ced7680c60c`.
Source baseline: `bb18064ba647e0ddb03cae4da437ab60ed447d98`.
Card: Antiphon / CARD-0994, revision 1, read in full through `card.ps1 get`.

## Outcome and authority

Complete the remaining CARD-0957 follow-up: after returning scheduling to main,
remove the retired temp project's exited containers promptly, then always run
`retire-temp` to reclaim its four private volumes. A later `deploy-temp` continues
to require an absent project before clearing retirement.

The dispatch supplies the operator's 2026-10-02/03 authorization: exited temp
containers are removed immediately, and temp is always retired after main resumes
scheduling and temp drains. Prune and volumes outside the existing recycling policy
remain human-gated. These are authorized decisions, not unanswered product defaults.
This Plan task changes documentation only and performs no rollout or Docker mutation.

CARD-1008 has already implemented the retired/absent/null-inventory volume path in
this baseline. Do not implement that fix again. Its historical plan is
[the CARD-1008 plan](2026-10-03-card-1008-rolling-volume-recycle-and-retire-temp-plan.md).
CARD-0994 owns the preceding exited-container transition and its integration.
Coordinate shared script changes with CARD-1008 follow-ups and CARD-1010; do not
include main state/cache opt-ins, routing settings, or runner runtime changes.

Owners read: `docs/docker-stack.md` (staged rollout, abandonment and volume
recycling), `docs/orchestration-loop.md` (autonomy and stage handoffs),
`docs/ops-http.md`, `docs/testing-and-build.md` (checkpoints, slots and rolling
harness), `docs/project-context.md`, and `docs/agent-card-lifecycle.md`.

## Ground truth

| Card assumption / request | Code at the source baseline | Consequence |
|---|---|---|
| Idle retirement leaves a stopped temp container. | `RunnerRetireService.TryRetireIdleAsync` sends Retire and stamps retirement; `PhoneHomeCommandDispatcher.StopAfterReplyAsync` stops the app after a 250 ms delay. `docker-compose.server2-runner.temp.yml` sets `restart: "no"`. None removes the container. | A retirement stamp alone cannot prove Docker exit. Observe the host before removal. No change to retirement or registration semantics is needed. |
| Gate 8 removes temp. | `scripts/deploy-server2.ps1`, `Invoke-Phase` / `drain-temp`, returns when `retiredAt` is nonempty. It does not inspect or remove a container. | Add an exited-container completion step; keep volume retirement as gate 9. |
| Both retained and absent retired temp are permanently blocked. | `Assert-RetiredTempCounters` now accepts explicit `runnerSessions=null` for strictly offline, retired, drained temp with zero bound/queued work and successful `Assert-TempProjectAbsent`. | The absent half is fixed by CARD-1008. Retained exited containers still cause `RunnerCounterUnknown server2-temp runnerSessions`. |
| Only the PowerShell guard needs changing. | `scripts/c590-remote.sh`, `c1008_status_proof`, independently requires no temp project containers before normalizing null live inventory. | Keep both absence gates; supply removal before them. Do not merely loosen a wrapper check. |
| Next deployment may replace a stopped temp. | `Assert-TempProjectAbsent` uses an all-state project census and refuses `TempContainersRemain` before clearing retirement. The host also checks absence. | Preserve CARD-0957's protection against reconnecting leftovers and donor replacement. Cleanup belongs to retirement. |
| Existing host retirement is unguarded `down -v`. | `case_retire_temp_runner` calls `c1008_recycle`: admission/task/land proof, Compose and mount checks, publication audit, container/reference checks, journal, four private volumes, preservation and disk receipts. | Reuse this volume path after absence; do not replace it with a new deletion loop. |
| All temp-project services are disposable. | Base Compose has `session-runner`, `state-init`, and a `build-slots` service behind the `broker` profile. The normal temp project uses the standing broker. | Allow only owned exited runner/state-init containers; an unexpected broker or service in temp is a refusal. Preserve the main broker. |
| A stopped container does not affect volume deletion. | Docker references remain until container removal; C1008 explicitly includes exited state-init containers in its custody checks. | Census all states and inspect every selected ID. Container removal uses no volume flag. |
| Retirement needs fresh human confirmation / rollback retention. | Current gate 9 and orchestration autonomy already say always retire temp. The manual subsection still claims that the absent-null guard is unimplemented and incorrectly describes `drain-temp` as removing the container. | Correct the stale caveat and document the new container step without reinstating an approval gate. |
| Tests already cover the requested recovery. | `RollingVolumeRecycleScriptTests.C1008_Present_or_unknown_temp_keeps_null_refusal` and `RemoteScriptContractTests.C1008_Retire_temp_rechecks_absence_and_retirement` deliberately reject a present container with null inventory. | Retain that strict volume predicate while testing the new cleanup entry point separately and through the wrapper. |

The premise is partly superseded, but the retained-exited gap remains directly
visible in both implementations and their tests. No further investigation stage is
needed to establish it. Live host container presence was not measured by this task;
the card's historical observations are not a current deployment census.

## Decisions

### D-1: cleanup on drain completion, recover through retire-temp

After `drain-temp` has established retirement, run the new container-only host case
`retire-temp-containers`. It returns success only after a fresh project census is
empty. Gate 9 remains mandatory: the caller immediately runs `retire-temp` after
successful gate 8, even when cleanup reports already absent. `-Phase all` retains
its existing phase order and also reaches gate 9; operational instructions continue
to use one named phase per gate.

Standalone `retire-temp` first runs the same cleanup case, so rollouts completed by
older scripts have a sanctioned recovery. It then re-reads status, executes the
existing `Assert-RetiredTempCounters`, and invokes `retire-temp-runner` with the
existing recycle context. That host case independently rechecks absence and all
volume-removal proofs. Never rewrite the API's null counter to zero.

Rejected: implicit replacement/removal in `deploy-temp` (weakens the admission
boundary); retaining exited containers for rollback (contradicts the operator's
policy); making volume removal part of `drain-temp` (loses the separate preview,
publication audit and retirement receipt); a new public phase or force flag (the
existing named phases already express the operation).

### D-2: absence or proven exited ownership, never unknown-as-zero

Separate validation of retired status from the strict *volume-admission* function.
Before the cleanup case can mutate, both wrapper and host require:

- Parseable, nonempty `retiredAt`, bound to the manifest and unchanged on re-read;
  `draining=true`, `retireWhenIdle=true`, `redirectTo=server2`.
- Explicit Boolean `available=false`, `dispatchEligible=false`, and
  `acceptingNewWork=false`; integer `sessions=0` and `queuedTasks=0`.
- `runnerSessions` present and either integer zero or explicit null. Missing,
  string, Boolean, fractional, negative or positive inventory is refused. This
  admits only the container-proof operation, not volume deletion.
- The main runner available, dispatch eligible, accepting, undrained and unretired;
  `drain-temp` keeps its existing requested-SHA check.
- The existing complete task/land census passes: no work routed/bound to temp and
  no pending land. Preserve the no-new-land interval from the rollout runbook;
  a script lock does not claim to exclude independent server lands atomically.

Under the existing rollout lock, enumerate the exact temp Compose project across
all container states and inspect full IDs, labels, image and mounts. Only
`session-runner` (at most one) and exited `state-init` containers are admissible.
Every selected container must have `State.Running=false` and `State.Status=exited`.
Verify the expected mount topology and retained named-volume identities using the
existing Compose/ownership helpers. Unknown services, duplicate runners, foreign
ownership/mounts, a failed census/inspect, and created/dead/paused/restarting/running
states all refuse. Main, other projects, the broker, caches and provider bind mounts
are never selected. Check the complete candidate set before the first removal.

Immediately before each `docker rm -- <full-id>`, recheck identity, exited state,
retirement/admission and task facts. Remove no volumes (`-v` prohibited), use no
force, and issue no stop/kill. Docker's running-container refusal is retained. After
removal, prove that exact ID is absent; finally repeat the whole project census.
New or changed containers invalidate the operation instead of being swept up.

Rejected: `docker ps -q` alone, state inferred from offline status, name/prefix
matching, age-based cleanup, or treating `retiredAt` as proof of exit. All can hide
live or unrelated custody. Shared `Assert-ZeroCounters`, `c849_status_zero` and
`c1008_status_proof` keep their existing strict-null behavior.

### D-3: observe exit; do not turn a retirement race into a kill

Retire can be acknowledged before the app has exited or its connection is reported
offline. `drain-temp` waits for retirement plus the explicit offline and host
absent/exited proof within one deadline derived from `WaitIdleMinutes`; adding the
container step must not restart the full drain budget. A pending live/offline
transition performs read-only observations. An exhausted deadline reports
`TempContainerExitTimeout`, preserves current state and removes nothing live.
Malformed evidence or changed ownership refuses immediately. Tests use the existing
shortened wait/poll seam, not production-scale sleeps.

An explicit standalone `retire-temp` refuses `TempContainerStillRunning` when a
container is live; it never tries to make it removable. An operator can rerun after
the normal shutdown completes. Success at gate 8 means absence, not merely a
retirement stamp. No polling worker or cleanup daemon is introduced.

### D-4: keep container cleanup independent of volume destruction

Container cleanup releases references and retains the four private volumes and
all mounted work. It needs no warm seed marker, image build, cache allocation gate,
prune, or disk-space threshold. Record the owned runner image digest before removing
it and leave images intact. The volume path still performs the uid-1654 publication
audit, including worktrees and local refs, and refuses unpublished/dirty/unknown
work. Failure of that later audit leaves the work volume present and temp retired.

Removing a container must not lose the image identity needed by the subsequent
offline work audit: carry the recorded digest as a validated audit-image hint into
the temp recycle operation. The host must inspect/pin the image and bind any hint
to the matching cleanup receipt, retirement stamp and source; a hint grants no
volume-removal authority. Retain a checked lookup keyed by source SHA, exact temp
project and retirement stamp so gate 9 can find gate 8's receipt across separate
invocations; never select an unrelated "latest" receipt. An already-absent cleanup
receipt links that original image observation instead of overwriting it with an
empty value. If the project was already absent without such a receipt,
retain C1008's inspected deployment-image fallback. An unavailable audit image is
`RecycleGitAuditUnknown`, with volumes retained, never permission to skip the audit.

`retire-temp -DryRun` previews container IDs and exact volume targets without
removing containers, creating an audit helper, clearing retirement or changing
routing. With exited containers present, report the volume proof as pending
confirmed absence (and the offline publication audit as pending); do not claim the
strict absent-volume guard passed. For an already absent project, retain the
existing C1008 preview. Do not broaden `-DryRun` to `drain-temp` in this card.

Rejected: relaxing the volume predicate for exited containers, auto-removing their
volumes while releasing references, or adding state/cache opt-ins. Those changes
would bypass the existing, separately reviewed recycling contract.

### D-5: checked transport, durable receipts and bounded retries

Add `retire-temp-containers` to the live-case transport in
`scripts/verify-docker-stack.ps1` and `scripts/c590-real.ps1`, with a distinct strict
container-cleanup manifest context. Its fixed fields are schema/version, operation
ID, exact temp project, project ID, typed preview Boolean and retirement stamp;
source SHA/run ID use the existing manifest envelope. No arbitrary container IDs,
volume names, project selectors or force options are accepted from the caller.
Validate at both PowerShell boundaries and again on the host. Preserve ASCII-only
PowerShell, literal argument transport, bounded child lifetimes and secret redaction.
Use a validated cleanup operation ID to link the optional audit-image receipt in
the retire manifest; resolve it under the host-owned evidence root, never from a
caller-supplied path. TestDesign freezes this additive transport field without
loosening the existing recycle context's unknown-field rejection.

Reuse the host rollout lock; acquire the cache lock second only where the shared
identity helpers require it, with no recursive lock acquisition. Wrapper status
reads do not substitute for fresh host proof. Keep the lock through candidate
validation, exact removals and the final census. The two host cases may release the
lock between them: the volume case reacquires it and revalidates everything.

Store a schema-versioned cleanup receipt outside the volumes, then copy it into
the existing rollout evidence directory. Record source/run/operation identity,
retirement stamp, original explicit null/zero inventory, main/temp observations,
candidate IDs/services/image digests/mount identities, per-ID removal intent and
outcome, final census, preview/apply and exact refusal. Emit one summary such as
`C994_TEMP_CONTAINERS removed=N alreadyAbsent=N outcome=<...> receipt=<path>`.
Do not include secrets or provider-home contents. Generated receipts remain ignored.

A partial `docker rm` or receipt-copy failure is a failed phase with exact progress,
not green. A normal retry freshly proves retirement and the remaining exited
candidates; absence is idempotent. A replacement ID never inherits an earlier
operation's approval. Readable local/host evidence identifies completed removals;
missing proof is unknown. Do not start the C1008 volume operation until the
container stage succeeds and its receipt is retained.

Keep `-ResumeRecycle` bound to C1008's existing journal. A resume must not run a new
container cleanup that could erase a replacement generation: it uses the original
strict absence/journal checks and refuses unexpected containers. A failed
container-only stage has not started recycling; rerun normal `retire-temp`, not
`-ResumeRecycle`. Link cleanup and recycle receipts on successful normal retirement.

### D-6: preserve deployment safety and correct the runbook

After successful retirement, require absent temp containers and all four private
volumes, unchanged main container ID/start time and retained main state/cache
identities, main accepting, and the temp row still retired/offline with no bound
work. Keep C1008's disk and volume receipt. A following `deploy-temp` may explicitly
clear retirement only through its existing host-absence and admission sequence.

Update gates 8/9 and the manual caveat to describe implemented behavior. The
abandonment procedure for a non-retiring failed-deploy hold remains unchanged:
that state cannot use this retired-only cleanup. Remove the obsolete claim that
absent/null retirement is unimplemented, and explain the new recovery command for
an exited retired temp. Preserve human gates for prune, other volumes, markers,
donor archives and work outside the approved recycling policy.

## Implementation slices

Commit and push each slice on the assigned Code branch. Shared files are a scope
collision with other rolling-script work; coordinate admission before dispatch.
No slice modifies the server retirement service, runner registration, or Compose
restart policy. Freeze all S1-S3 commits before the ordinary checkpoint group.

| Slice | Files | Work and required tests |
|---|---|---|
| S1: host container proof and receipt | `scripts/c590-remote.sh`; `scripts/verify-docker-stack.ps1`; `scripts/c590-real.ps1`; new `tests/Antiphon.Tests/Scripts/RetiredTempContainerHostTests.cs`; `scripts/fixtures/c1008-fake-docker.sh` and shared fixture helpers as needed | Implement the strict new host case, typed transport, locked full census, exact non-force removal, checked receipt and preview. Cover exited runner plus state-init, absence, disallowed states/ownership, observation/removal races, partial failure and retry. Test the real host branch, not a success-only fake. |
| S2: phase composition | `scripts/deploy-server2.ps1`; `scripts/fixtures/c727-fake-http.ps1`; `scripts/fixtures/c727-fake-verify.ps1`; `scripts/test-deploy-server2.ps1`; new `tests/Antiphon.Tests/Scripts/RetiredTempContainerScriptTests.cs`; affected C1008 wrapper tests | Add bounded exit observation to drain completion, standalone retirement recovery, preview composition, fresh strict-volume revalidation, receipt linkage/audit-image transport and resume isolation. Update fixture phase order/counts explicitly. Keep deployment, main counters, strict volume guard and non-retiring-hold refusals. |
| S3: real lifecycle proof and documentation | new `scripts/fixtures/c994-retired-temp-real-cases.mjs`; new `tests/Antiphon.Tests/Scripts/RetiredTempContainerDockerTests.cs`; shared owned-cleanup helper if required; `docs/docker-stack.md`; `docs/orchestration-loop.md`; `tests/Antiphon.Tests/Infrastructure/DockerStackDocumentationTests.cs` | Run the production shell logic against uniquely named fixture resources. Prove drain/exit/removal/retire/redeploy admission, failure preservation and no fixture residue. Document gates and recovery; add one focused runbook-contract test. Preserve existing C1008 real-Docker proofs. |

## TestDesign handoff

Verification is a separate stage in this dispatch. TestDesign must add the complete
`## Verification design`, map each invariant to named assertions, freeze exact
expanded rosters and controls, and make this manifest executable before Code.
No build, test or live deployment was run by Plan. Proposed new test names below
are implementation targets, not claims of existing coverage.

Required coverage:

| ID | Boundary / required evidence | Intended owner |
|---|---|---|
| V-1 | Exact exited runner/state-init deletion and idempotent absence; all volumes retained during container cleanup | `RetiredTempContainerHostTests.C994_Exited_owned_containers_are_removed_without_volumes` |
| V-2 | Running/paused/restarting/created/dead, unexpected service, duplicate/foreign IDs, mount mismatch, failed/partial census or inspect: no unsafe removal | `RetiredTempContainerHostTests.C994_Unsafe_or_unknown_census_refuses` |
| V-3 | Recheck every removal; changed retirement, main admission, task/land state, ID or state; partial removal, receipt loss and recovery | `RetiredTempContainerHostTests.C994_Races_and_partial_failures_preserve_custody` |
| V-4 | Preview and malformed transport cannot mutate; context/stamp/image evidence independently checked | `RetiredTempContainerHostTests.C994_Preview_and_transport_are_strict` |
| V-5 | Retirement acknowledgement before exit: wait within one deadline, no stop/kill, timed refusal, final absence before success | `RetiredTempContainerScriptTests.C994_Drain_waits_for_exit_and_removes_containers` |
| V-6 | Standalone retire removes exited leftovers before strict absent/null admission and guarded volume retirement | `RetiredTempContainerScriptTests.C994_Retire_recovers_exited_and_absent_temp` |
| V-7 | Missing/invalid/nonzero counters, non-retiring hold, wrong redirect, accepting/live status, changed retirement: no cleanup | `RetiredTempContainerScriptTests.C994_Invalid_retirement_cannot_cleanup` |
| V-8 | Preview has no side effects; resume never removes a replacement; failure ordering and image-hint/receipt linkage | `RetiredTempContainerScriptTests.C994_Preview_resume_and_receipts_preserve_boundaries` |
| V-9 | Real Docker lifecycle: retained exited runner/state-init -> containers absent -> four volumes absent -> next deployment admission; negative and partial cases; main/cache sentinels unchanged; checked cleanup | `RetiredTempContainerDockerTests.C994_Real_retired_temp_lifecycle` |
| R-1 | C1008 absent/null, strict present/null volume guard, publication audit, volume references, preview, receipts and main resume remain enforced | Existing `RollingVolumeRecycleScriptTests` and `RemoteScriptContractTests` C1008 methods; adjust wrapper expectations only for the new preceding host step |
| R-2 | CARD-0957 deployment census/hold race still refuses all leftovers before state change; legacy rolling/jq roster reconciled | `scripts/test-deploy-server2.ps1` T-21/T-22 and the C1008 legacy rolling harness consumer |
| R-3 | Gate 8 cleanup, mandatory gate 9 and human-only exclusions are accurate | Proposed `DockerStackDocumentationTests.Retired_temp_cleanup_and_retirement_gates_match` |
| R-4 | Real existing recycler, publication audit and preservation behavior | `RollingVolumeRecycleDockerTests.C1008_Real_docker_comparison` |

For R-1, preserve a direct test of `Assert-RetiredTempCounters` with present/null
input: it must still refuse. A public `retire-temp` invocation may now first remove
proven exited containers, so its old assertion of no host-case invocation must be
replaced with checks of ordering and the independent strict boundary. Keep the
direct host `c1008_status_proof` regression unchanged.

The new real-Docker fixture must isolate every project, container, volume, network,
Git remote and receipt with a unique test prefix; refuse production identifiers.
Use only owned local children and await them, including timeout cleanup. Compare
the baseline refusal with changed success for the central exited/null case; assert
actual Docker/volume/sentinel state and exact removals, not just stdout. Reuse the
existing process limiter and build-slot gate. TestDesign should separate individual
mutation targets into precise method filters rather than a monolithic PC suite.

Placement observation at 2026-10-04 12:13 UTC: `GET /api/runner-defaults` revision 2
selected an automatic Linux runner; `GET /api/session-runners` showed eligible Linux
and Windows runners and an unavailable/draining temp entry. These are live
observations, not a fleet location embedded in the plan. Re-read both routes at
dispatch. Omit `-Runner`; use `-Platform Linux` only for the Bash/jq/real-Docker
verification lane. Omit `-Platform` for plan/design work; `-Platform Any` unpins.
Existing deployment IDs in this document describe the product contract, not task
placement. TestDesign must check Windows transport compatibility using the existing
literal-argument fixture conventions; any required native Windows row is added
explicitly, not silently treated as Linux evidence.

### Checkpoints

Proposed ordinary scope for TestDesign to freeze. All rows name the Linux lane and
run after one committed S1-S3 group; TUnit rows share one isolated build. Eight proposed new
single-result methods give CP-1 its floor; CP-2's 19 methods and CP-5's existing one
come from the current C1008 roster. CP-3 is a non-TUnit harness command with two
invocations and six assertions; it has no TUnit execution floor.
This scripts-only change uses the affected script/host Unit classes instead of the
entire application Unit lane: the named boundary is the rolling shell/PowerShell
contract, with no server/domain/client implementation change.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | S1-S3 | `tests/Antiphon.Tests -> bin-c994/` | linux-temp-container-contract | `/*/*/(RetiredTempContainerHostTests*)\|(RetiredTempContainerScriptTests*)/C994_*` | V-1..V-8 | 8 proposed methods, 0 failed/skipped; freeze argument expansion in TestDesign | 8 | 10 | true |
| CP-2 | S1-S3 | CP-1 | linux-recycle-regression | `/*/*/(RemoteScriptContractTests*)\|(RollingVolumeRecycleScriptTests*)/C1008_*` | R-1, R-2 | All 19 baseline methods plus any explicit additions, 0 failed/skipped; report internal harness roster separately | 19 | 12 | true |
| CP-3 | S1-S3 | n/a | linux-deploy-absence-regression | `pwsh -NoProfile -File scripts/test-deploy-server2.ps1 -Only host-absence -RequireJq` | R-2 | T-22: 2 invocations, 6 assertions, exit 0, no skipped prerequisite | n/a | 4 | true |
| CP-4 | S1-S3 | CP-1 | linux-retirement-doc-contract | `/*/*/DockerStackDocumentationTests/Retired_temp_cleanup_and_retirement_gates_match` | R-3 | 1 proposed result, 0 failed/skipped | 1 | 1 | true |
| CP-5 | S1-S3 | CP-1 | linux-isolated-docker-lifecycle | `/*/*/(RetiredTempContainerDockerTests*)\|(RollingVolumeRecycleDockerTests*)/*` | V-9, R-4 | 2 TUnit results, all named native outcomes, 0 failed/skipped and zero fixture residue | 2 | 12 | true |

Estimated ordinary execution floor: 39 minutes plus authoring, not measured runtime.
TestDesign may split a row by its named classes if it exceeds a foreground window;
freeze that split before Code. Keep Code and Review receipts per CP with counts and
exact source SHA. Use the checkpoint tool once per committed slice group and await
completion; do not run unlisted builds/tests without recording a reason. Build-slot
timeout is not permission to bypass the gate. Clean producer-owned `bin-c994/`
outputs after all owned children finish. Generated evidence remains gitignored.

## Completion and next stage

The Plan deliverable is this committed/pushed artifact. Next is **test-design**:
freeze adversarial vectors, typed transport/image receipt shape, timing and failure
oracles, exact checkpoint rosters and method-scoped positive controls. Code follows
only after the verification design is added. No operator decision or new approval
is outstanding for the behavior selected above. Live rollout remains a caller-owned
post-land operation from the canonical checkout under the existing rollout gates.
