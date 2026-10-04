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

### Proposed checkpoints (superseded by Verification design)

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

## Verification design

TestDesign task `cef1a2a9-a166-4f55-a1db-becddcd9f04e`, inspected at
`157dd95ad3468cfb641647ccd26504347cb38a92`. This addition freezes verification;
D-1 through D-6 above are unchanged. The earlier proposed checkpoint heading was
renamed because `PlanTableImporter.ExtractSection` reads the **first** exact
`### Checkpoints` heading. Only the table below is executable authority.

**Readiness: next is Plan, not Code.** P-1 is a concrete production-topology seam:
`c1008_compose_model` requires state-init's runner-state target to be `/state`, but
`docker-compose.server2-runner.yml` mounts it at `/runner-state`.
`c1008_owned_mounts` also requires every model mount to be a named volume, every
inspected mount to be writable, and equal mount counts. Production has read-only
file binds, provider-directory binds, Compose secrets and tmpfs; the temp override
adds the Grok bind. Both C1008 fixtures substitute a volumes-only model with the
incorrect `/state` destination. Reusing that model cannot prove D-2 or D-6.
This is established by reading the predicates and Compose files, not a claim of a
live-host failure. Plan must specify how the existing helpers validate the actual
mount kinds/destinations without reading secret contents, and whether the shared
C1008 correction lands here or in a prerequisite. Do not drop those mounts from
inspection or weaken the match to make the fixture pass. V-2's production-topology
method and V-9 are the required closure evidence. No human policy choice is needed.
If that repair expands the shared scope, refresh this manifest before Code;
unchanged helper internals remain owned by CARD-1008's verification.

### Inspection

| Bodies read | Boundaries -> evidence or exclusion |
|---|---|
| `RollingVolumeRecycleScriptTests`: all eight C1008 methods, `C1008Process`, `C1008WrapperFixture` and `C1008HostFixture`, including constructors, Run, ReloadDocker, Container and Dispose | Typed inputs, direct validators, retries, trace/state divergence, receipt copy, 30-second child deadline -> V-1..V-8, R-1/R-2; nearest fixtures for both new Unit files |
| `RemoteScriptContractTests`: eleven C1008 methods and `C1008GitGraph` | Native flock, complete census, mounts, Git audit, journal boundaries, direct strict-null helper -> V-2/V-3/V-8, R-1; no inference from unrelated methods in this large class |
| `RollingVolumeRecycleDockerTests.C1008_Real_docker_comparison`; complete `c1008-recycle-real-cases.mjs` and `c1008-recycle-cleanup.mjs` | Real Docker, base comparison, 32 outcomes, ownership and timeout cleanup; identifies volumes-only substitute -> V-9, R-4, P-1 |
| `c1008-fake-docker.sh` and `c1008-recycle-cases.json`, complete bodies | All-state census, fail-closed unknown argv, fake image pin, retained state; missing removal races, receipt-image failures and full topology -> V-1..V-4/V-8 |
| `c727-fake-http.ps1`, `c727-fake-verify.ps1`, complete bodies; `test-deploy-server2.ps1` Run/Final-TempStatus, T-1..T-24 and final roster; complete jq driver | Immediate retirement does not model exit; C1008 mode only permits GET or explicit clear. New exit scripts must persist real intermediate states -> V-5..V-8, R-2 |
| `DockerStackDocumentationTests.Main_volume_recycling_is_scripted_only`, adjacent doc-contract methods and Read; `DockerStackDocuments.Read`/RepoRoot; `DelegateScriptRunner` argument-list and RepoRoot helpers | Whitespace-normalized section assertions and source location -> R-3; no doc wording used as Docker proof |
| `deploy-server2.ps1`: Assert-ZeroCounters, Assert-RecycleContext, Assert-RetiredTempCounters, Assert-TempSeedCounters, Wait-RunnerStatus, Invoke-HostCase, Assert-TempProjectAbsent and drain/retire phase bodies | Separate cleanup admission, strict volume admission, bounded waiting, no rearm on retirement -> V-5..V-8, R-1/R-2 |
| `c590-real.ps1`: Assert-C1008BridgeContext and live manifest/export/copy path; verifier predispatch validation; `c590-remote.sh`: rollout/cache locks, save/refuse, container/volume census, status proof, Compose/private/cache/mount helpers, audit, recycle admission and retire entry | Independent transport gates, image custody and physical receipts -> V-1..V-4/V-8; P-1 is not covered by the simplified fixtures |
| Production main/temp Compose mount definitions; docker-stack gates 8/9, recycling and manual caveat; orchestration autonomy/stage rules; project context; testing guide checkpoint/PC/receipt rules; importer and coverage-reader bodies | Preservation, correct stage handoff, single recognized checkpoint manifest, strict source receipts -> V-9/R-3/Checkpoints |

Missing setup is implementation work, not existing coverage: extend the fake Docker
boundary with deterministic numbered read/remove barriers, full Compose topology,
image-inspect failure, disappearance/replacement, failed or partial census/inspect,
per-ID rm failure and falsely successful rm; add host atomic-save/copy barriers and
bridge-copy failure. Add file-backed HTTP status sequences and a deterministic clock
for the existing shortened wait/poll seam. Persist event ordinal and virtual time,
not wall-clock guesses. Load real PowerShell function ASTs for direct predicate tests,
with I/O dependencies replaced and an admitted/refused result visible to the test.
Add a C994 host-case dispatcher to the real-Docker fixture. All child launchers need
`ParallelLimiter<ProcessSpawnLimit>`, asynchronous pipe drains, bounded waits,
kill-tree-and-await on timeout, and owned cleanup in finally. A fixture setup failure
or missing Bash/jq/pwsh/node/Docker prerequisite fails the row; it is not a skip.

### Delivery inventory

There is **no new or changed queued asynchronous delivery path**. The existing
server-to-runner Retire command and its acknowledgement/shutdown mechanism are
unchanged and explicitly outside D-1..D-6's implementation scope. No session input is
sent; no UserPrompt transcript is claimed. Therefore a real queue/busy-recipient
transcript test is inapplicable here. If implementation changes retirement dispatch,
phone-home transport or session input, return to TestDesign to add the real queue,
busy and already-eligible recipient cases, every enqueue/crash handoff, and matching
complete UserPrompt evidence for any session input; the following substitutes do
not cover that expansion.

The changed **synchronous** handoffs still require end-state evidence:

| Producer -> destination | Durable join / persistence | Recovery and observable receipt |
|---|---|---|
| Rolling wrapper -> verifier -> live bridge -> host cleanup | source SHA, run ID, `c994` operation ID, exact project, project ID, normalized retirement instant; typed cleanup manifest | Refusal/transport failure prevents recycle. After a lost reply or copy, reload the host receipt and fresh Docker census. V-3/V-4/V-8 check host and copied JSON agree; a request or child exit alone is insufficient. |
| Host cleanup -> Docker | Receipt outside all target volumes; complete candidate full IDs, service/image/mount identities, removal intent then observed result | Interrupt before intent, after intent/before rm, after rm/before outcome, and after final census/before copy. Retry re-proves current facts and remaining candidates. Exact-ID inspect plus successful fresh all-state project census proves absence; an rm stdout line does not. |
| Gate 8 host receipt -> later gate 9 audit/recycler | Checked lookup keyed by source SHA, exact temp project and retirement instant; original image observation and cleanup operation link retained across different run IDs | V-8 starts a fresh wrapper process. It must consume the matching retained receipt or use the inspected deployment-image fallback only when no original receipt exists. Conflicting/corrupt evidence is a refusal. The uid-1654 audit and actual volume absence close the chain. |
| Recycler -> wrapper/operator evidence | Existing C1008 operation/journal plus cleanup operation link; copied evidence remains outside deleted volumes | Recycle starts only after successful retained cleanup receipt. Copy failure stays red even if Docker effects happened. V-9 checks all four volume identities are absent and preservation sentinels remain; R-4 retains disk/journal checks. |

The offline Docker ledger proves production control flow and fault responses, not
Docker semantics. HTTP sequences prove observation/order/deadline handling, not
Retire command delivery or a provider shutdown. The owned real-Docker lane proves
container/volume effects and preservation using real production shell logic, but
its local HTTP server, relocated projects and lightweight runner do not prove live
fleet routing, registration lease expiry, provider execution or SSH connectivity.
No proof stops at a manifest, ack, event, receipt flag or command trace: V-9 reads
actual Docker identities and payloads after the transition. G/PC-10, 11, 21..24,
31..38 and 40 below protect these handoffs and recovery boundaries.

Transport contract frozen for implementation: a `tempContainerCleanup` object has
exactly `version` (integer 1), `operationId` (`^c994[0-9a-f]{32}$`), `project`
(`antiphon-runner-temp`), `projectId` (lowercase canonical UUID), `dryRun` (JSON
Boolean), and `retiredAt` (parseable ISO-8601 instant). The existing envelope owns
source SHA/run ID. Both PowerShell validators and the host validate independently;
normalization of ConvertFrom-Json DateTime preserves the same UTC instant. Reject
unknown/missing/duplicate JSON members, arrays/scalars, coercible Booleans, arbitrary
paths, selectors, IDs, volumes and force options. Duplicate-member validation must
inspect raw JSON before ConvertFrom-Json loses that evidence. The sole additive
C1008 context field is optional `cleanupOperationId` with the same c994 pattern,
valid only for normal temp retirement; main and resume inputs reject it. Resume
uses its original journal link. No raw image/path field is accepted from a caller.
Host lookup resolves the operation under its owned evidence root and rejects links,
source/project/stamp mismatch, malformed receipts and ambiguous matching entries.

Cleanup receipt schema 1 records source/run/operation/project/projectId/retiredAt,
preview/apply, original explicit null-or-zero runnerSessions, filtered main/temp
observations, complete candidates, original image digest, per-ID intent/outcome,
final census, refusal and linked original cleanup operation when already absent.
Use atomic replacement before removal; retain previous valid JSON on write failure.
The host's result and rollout copy must contain the same operation and content
hash. Preview may write evidence but creates no audit helper, cleanup lookup entry
usable as apply authority, container mutation, volume mutation or routing change.
An absent retry never overwrites a known original image with an empty observation.

### Proves it works now

All methods below are **new, single-result TUnit methods** with internal case
vectors, not Arguments expansion: 20 host methods, 13 wrapper/transport methods,
and one real-Docker method. Names in the earlier handoff are refined by this roster.
Every vector emits its key on failure; expected case-key sets are compared for
exact equality so early return cannot silently drop a boundary. Assertions listed
here are required labels in the corresponding test body.

| ID | Exact method | Layer, vectors and decisive expectation |
|---|---|---|
| V-1 | `RetiredTempContainerHostTests.C994_Exited_owned_containers_are_removed_without_volumes` | Real host branch + Docker boundary: runner only, state-init only, runner plus two state-init; cross with explicit null/zero (6 cases). Full exact rm IDs, no force/volume flag/stop/kill/down/prune, all volume identities and payloads unchanged. Assertion `c994-container-only`. No seed marker, image build or allocation-budget call. |
| V-1 | `RetiredTempContainerHostTests.C994_Absent_cleanup_is_idempotent` | Absent null/zero with four volumes present and already absent (4 cases), then normal retry in each. No removal; fresh empty census and checked receipt; `c994-absence-idempotent`. |
| V-2 | `RetiredTempContainerHostTests.C994_Host_retirement_proof_is_typed` | Independently exercise every temp field in matrix A below through the host predicate, then one invalid real-entry case. `c994-host-retirement`: invalid proof never admitted; no removal. |
| V-2 | `RetiredTempContainerHostTests.C994_Host_main_admission_is_required` | Matrix B at host; no mutation while main is unavailable, ineligible, nonaccepting, drained or retired. `c994-host-main`. |
| V-2 | `RetiredTempContainerHostTests.C994_Host_task_and_land_census_is_complete` | Matrix C through existing host census plus new cleanup entry; `c994-host-work`: no removal for bound work, pending land or incomplete census. |
| V-2 | `RetiredTempContainerHostTests.C994_Census_and_inspect_fail_closed` | Nonzero ps with empty/partial output; truncated/duplicate/nonhex ID; inspect nonzero, empty, malformed, wrong ID, multirow or omitted State/Image/Mounts/Labels. `c994-census-known`: no rm even when stdout looks empty. |
| V-2 | `RetiredTempContainerHostTests.C994_Container_service_and_state_are_restricted` | Both allowed services crossed with running, paused, restarting, created, dead, exited and inconsistent Running/Status; exited is sole success. Unknown/broker service, duplicate runner, foreign project and prefix-neighbour inputs refuse or remain unselected as appropriate. `c994-service`, `c994-state`, `c994-project`, `c994-cardinality`. |
| V-2 | `RetiredTempContainerHostTests.C994_Production_mount_topology_is_proven` | P-1 gate: materialize real main+temp Compose config with fixture-owned provider/secret paths. Valid `/runner-state`, named volumes, read-only binds, provider binds, secrets and tmpfs succeed; wrong type/source/destination/RW, omitted/extra/duplicate mount, foreign volume labels/generation, symlink source and external-private mismatch refuse. `c994-mounts`, `c994-private-identity`, `c994-compose`. Never inspect file contents. |
| V-3 | `RetiredTempContainerHostTests.C994_Whole_candidate_set_precedes_removal` | Valid first runner plus invalid last state-init, in both enumeration orders; `c994-whole-set`: zero rm before all candidates pass. |
| V-3 | `RetiredTempContainerHostTests.C994_Rollout_lock_covers_final_census` | Hold a real flock in an owned child, observe lock-request barrier, release and await success. Second entrant cannot validate/remove until first final census/receipt completes; cache lock, if needed, is second and acquired once. `c994-lock`, `c994-lock-order`. |
| V-3 | `RetiredTempContainerHostTests.C994_Each_removal_rechecks_live_proofs` | Before first and second rm separately change retirement instant, main admission, task/land snapshot, ID, state, image or mounts. `c994-recheck-status`, `c994-recheck-work`, `c994-recheck-container`: no next rm; prior progress truthful. Replacement ID is never swept. |
| V-3 | `RetiredTempContainerHostTests.C994_Final_absence_is_observed` | Fake rm returns 0 but retains ID; exact-ID inspect errors; replacement appears before final census; final ps errors/returns partial. `c994-id-absent`, `c994-project-absent`: phase fails, no success receipt/recycle authority. |
| V-3 | `RetiredTempContainerHostTests.C994_Removal_failure_and_crash_retry_preserve_progress` | Fail first/second rm; interrupt at four persistence boundaries from Delivery inventory. Rehydrate persisted Docker and status state before retry. `c994-rm-failure`, `c994-retry`: only remaining freshly proven IDs removed, all volumes retained, one summary and exact partial outcomes. |
| V-3 | `RetiredTempContainerHostTests.C994_Receipt_write_and_copy_fail_closed` | Fail initial intent save, later outcome save, host evidence copy and bridge copy separately. `c994-intent-first`, `c994-receipt-copy`: no rm before durable intent; no green on lost evidence; previous atomic JSON intact and normal retry recovers. |
| V-4 | `RetiredTempContainerHostTests.C994_Host_context_is_strict` | Direct host context vectors from transport contract, including wrong lane, source/project/stamp mismatch and absent required context. `c994-host-context`, `c994-host-lane`: no Docker mutation. |
| V-4 | `RetiredTempContainerHostTests.C994_Host_preview_never_mutates` | Present/absent crossed with null/zero. Exact preview IDs and four volume targets; no helper/seed/lookup authority/rm/routing; `c994-host-preview`. A subsequent apply with changed facts refuses. |
| V-8 | `RetiredTempContainerHostTests.C994_Image_receipt_lookup_is_bound` | Matching original receipt, absent retry link, no-receipt fallback; corrupt/symlink/traversal/foreign operation/source/project/stamp and two conflicting matches. `c994-image-binding`, `c994-image-retention`: never select newest unrelated receipt or erase known digest. |
| V-8 | `RetiredTempContainerHostTests.C994_Audit_image_is_pinned_and_required` | Original image differs from deployment tag; inspect/pin exact digest and create uid-1654 readonly audit helper from it. Missing image or inspect failure yields RecycleGitAuditUnknown with volumes intact; dirty/unpublished work after container removal also retains work. `c994-image-required`, `c994-audit-required`. |
| V-4 | `RetiredTempContainerHostTests.C994_Cleanup_receipts_redact_unapproved_fields` | Sentinel secrets in Env, extra labels, provider path contents and stderr; success and refusal JSON/stdout/copies contain none. Approved identities remain. `c994-redaction`. |
| V-9 | `RetiredTempContainerHostTests.C994_Fixture_custody_is_bounded` | Offline contract for the new real fixture and cleanup helper: reject production names, wrong ownership ledger/label, symlink root and sibling-daemon identity before a destructive argv; launch an owned deliberately hung child and await its teardown. `c994-fixture-ownership`, `c994-child-reaped`. No daemon use in this method. |
| V-5 | `RetiredTempContainerScriptTests.C994_Drain_waits_for_exit_and_removes_containers` | Delayed retirement; retired but connected; offline with still-running Docker; offline exited; already absent at entry. Timestamped barriers prove no success before confirmed absence and no stop/kill. `c994-drain-exit`. |
| V-5 | `RetiredTempContainerScriptTests.C994_Drain_uses_one_deadline` | Existing wait seam uses virtual budget 100: retirement at 80, exit at 95 succeeds; exit at 105 fails at 100; still-connected and still-running at 100 refuse TempContainerExitTimeout. Malformed ownership/status refuses immediately. `c994-one-deadline`; child-call elapsed time consumes the same budget. |
| V-6 | `RetiredTempContainerScriptTests.C994_Retire_recovers_exited_and_absent_temp` | Standalone present-exited/absent x null/zero. Cleanup -> receipt retained -> fresh status/census -> strict volume admission -> recycler. Live container refuses TempContainerStillRunning, with no polling kill. `c994-retire-order`, `c994-live-retire`. |
| V-7 | `RetiredTempContainerScriptTests.C994_Wrapper_retirement_proof_is_typed` | Matrix A independently through wrapper predicate; invalid inputs cannot invoke cleanup. `c994-wrapper-retirement`. |
| V-7 | `RetiredTempContainerScriptTests.C994_Wrapper_main_admission_is_required` | Matrix B independently through wrapper; drain also refuses wrong requested SHA. `c994-wrapper-main`, `c994-drain-sha`. |
| V-7 | `RetiredTempContainerScriptTests.C994_Wrapper_task_and_land_census_is_complete` | Matrix C independently through wrapper with host replaced by a recording boundary; zero cleanup calls for invalid work facts. `c994-wrapper-work`. |
| V-8 | `RetiredTempContainerScriptTests.C994_Strict_volume_admission_remains_independent` | AST-load actual Assert-RetiredTempCounters with present/null and failed census, then absent/null control. Direct host c1008_status_proof gets same pair. Also call shared Assert-ZeroCounters and c849_status_zero: null refuses, integer zero accepts. `c994-wrapper-strict-null`, `c994-host-strict-null`, `c994-shared-null`. Input status remains explicit null after call. |
| V-8 | `RetiredTempContainerScriptTests.C994_Preview_reports_pending_volume_proof` | DryRun present/absent x null/zero; present previews exact four targets and pending absence/audit without invoking a destructive recycler or claiming strict admission; absent uses existing C1008 preview. `c994-preview-pending`; no POST/helper/clear/removal. |
| V-8 | `RetiredTempContainerScriptTests.C994_Resume_never_runs_container_cleanup` | Matching C1008 partial journal+absent control; replacement runner or state-init refuses. No new c994 operation for any ResumeRecycle; failed cleanup uses normal retry instead. `c994-resume-isolation`. |
| V-8 | `RetiredTempContainerScriptTests.C994_Cleanup_receipt_is_required_before_recycle` | Cleanup child failure, missing/corrupt/mismatched copied receipt, copy failure after removal, and fresh busy/stamp/task drift before recycle. `c994-receipt-before-recycle`, `c994-fresh-volume-proof`: zero recycler calls and unchanged retirement. |
| V-4 | `RetiredTempContainerScriptTests.C994_Verifier_manifest_is_strict` | Real verifier predispatch boundary; all raw transport vectors, duplicate members and unknown C1008 fields; only defined cleanupOperationId addition admitted in normal temp context. `c994-verifier-context`, `c994-recycle-addition`. |
| V-4 | `RetiredTempContainerScriptTests.C994_Bridge_transport_preserves_literal_context` | Direct bridge validator and actual live-case export builder with SSH/scp replaced. DateTime and offset timestamps normalize equally; invalid strings/quotes/newline/metacharacters fail before transport. Capture ArgumentList and parsed host values; sentinel command never executes. `c994-bridge-context`, `c994-literal-transport`; all touched PowerShell remains ASCII. |
| V-8 | `RetiredTempContainerScriptTests.C994_Separate_invocations_preserve_audit_identity` | Gate 8 then fresh-process gate 9 with different run ID; persisted exact receipt selected, original image used, final cleanup/recycle copies linked. No-receipt deployment fallback and failed copy retry included. `c994-cross-run-image`. |
| V-9 | `RetiredTempContainerDockerTests.C994_Real_retired_temp_lifecycle` | Exact RD roster below using real Docker/Compose/Git and production shell branches. `c994-real-effects`: matching evidence, physical absence/preservation and no fixture residue, not stdout-only success. |

Matrix A: valid retired instant (including equivalent UTC offset), required true
Boolean draining/retireWhenIdle, exact redirect server2, required false Boolean
available/dispatchEligible/acceptingNewWork, integer sessions=queuedTasks=0,
runnerSessions explicitly null or integer zero. For each field independently:
omit, null, wrong type (string/number/Boolean as appropriate) and opposing value;
for counters also -1, 0.5, 1 and string "0"; for retiredAt also empty/impossible date
and changed valid instant. A null runnerSessions is an admitted cleanup value only
in the otherwise-valid row. Non-retiring failed-deploy hold is a distinct refusal.
Cover both null/zero success with present-exited/absent, and retired-online plus
null as a refusal. All-invalid Cartesian products add no new branch and are excluded;
the late-proof matrix separately changes one field after valid admission.

Matrix B: each of main available, dispatchEligible, acceptingNewWork must be a true
Boolean, draining a false Boolean and retiredAt explicitly null. Test omission,
coercible string, opposite value and non-null retirement separately. Main may be
busy while accepting; do not invent a zero-main-work requirement for temp cleanup.

Matrix C: bound/routed Queued, Dispatched, Working, Blocked and Failed task; succeeded
task with pending requested/started land or Queued/Held/Running/NeedsResolution/
Unknown land state; wrong/withheld project exclusion count, unscoped row, malformed
or incomplete page/envelope, inconsistent detail, API error. Published terminal,
complete empty census, and valid closed excluded-project census are controls.
Use existing production complete-census routines, not a fixture-side acceptance
predicate. Re-read after wrapper admission and immediately before each host rm.

RD roster is **10 internal outcomes**, one TUnit execution, in the new C994 fixture:

| RD | Physical evidence |
|---|---|
| RD-1 | Baseline script at 157dd95 refuses retained exited/null temp through existing retire-temp-runner; containers and all private volumes remain. |
| RD-2 | Changed drain-temp with local HTTP retirement/exit sequence reaches cleanup against real exited runner+state-init with production-shaped mounts. Before volume retirement all four volumes and provider bind sentinels remain, selected IDs are absent, main ID/StartedAt unchanged. |
| RD-3 | Separate normal retire-temp consumes RD-2 receipt, audits as uid 1654, removes exactly four private volumes and leaves temp retired/offline; main/broker/caches/provider binds unchanged. |
| RD-4 | Next deploy-temp absence/admission sequence reaches permitted fixture Compose creation; explicit retirement clear occurs only after fresh absence and safe hold. This proves script admission, not server registration lease semantics. |
| RD-5 | Already absent/null without a cleanup receipt uses inspected fixture deployment image; four private volumes reclaimed, image remains. |
| RD-6 | Live temp refuses standalone cleanup; same real ID remains running with same StartedAt. No production stop/kill. |
| RD-7 | Dirty/unpublished temp Git content survives after container removal when volume audit refuses; work sentinel and unpublished commit still readable. |
| RD-8 | Second container rm fails after first real removal; failed phase and exact progress; normal retry removes only remaining original, all volumes retained until recycle. |
| RD-9 | Insert replacement at final-census barrier and attempt C1008 resume: replacement remains and no new cleanup runs; volume generation unchanged. |
| RD-10 | DryRun with exited containers: IDs, all volume generations, main StartedAt and bind payloads unchanged; pending proof is honest. |

Each changed outcome includes preservation of all four main private volumes, the
three shared caches, standing broker, foreign prefix-neighbour volume/container,
fixture provider bind hashes and image digest. No fixture uses real provider homes
or secrets. Create inert credential files only. Use actual Compose-derived mount
metadata, not the C1008 volumes-only rewrite. Relocate exact project names and
owned paths only; record original/transformed script hashes and substitutions.
Fake status server may drive states; it cannot fake Docker ps/inspect/rm/volume rm
success. Failure wrappers delegate to real Docker except at the named barrier.
The existing C1008 32-case real comparison remains an independent R-4 row.

### Guards the regression

- R-1: Keep all **19 existing single-result C1008 methods**: the eleven
  RemoteScriptContractTests methods are Recycle_exact_default_volumes,
  Recycle_refuses_references_and_unknown_census, Recycle_audits_work_as_1654,
  Recycle_refuses_unpublished_and_dirty_work, Recycle_refuses_uninspectable_git,
  Recycle_preserves_tmp_copyup, Recycle_resume_requires_matching_receipt,
  Recycle_receipt_records_disk_and_partial_failure,
  Retire_temp_rechecks_absence_and_retirement,
  Retire_temp_reclaims_below_cache_disk_gate, Recycle_dry_run_never_mutates;
  every name has the `C1008_` prefix. The eight RollingVolumeRecycleScriptTests
  names with that prefix are Present_or_unknown_temp_keeps_null_refusal,
  Busy_routed_and_land_in_flight_refuse, Same_sha_and_partial_retries_are_safe,
  Option_manifest_is_strict, Legacy_rolling_and_jq_rosters_remain,
  Documentation_and_transport_pins_match, Refusal_receipts_do_not_leak_secrets,
  Retired_absent_null_is_accepted. Preserve outcome assertions; change only wrapper
  ordering expectations made obsolete by the preceding cleanup. Direct host
  present/null refusal stays unchanged. V-8 supplies direct wrapper strict proof.
- R-2: `RollingVolumeRecycleScriptTests.C1008_Legacy_rolling_and_jq_rosters_remain`
  still runs all four jq modes. Preserve 24 groups/66 invocations/227 assertions
  when jq is present, 23/62/218 for each intentionally unavailable mode, and
  31 driver assertions per mode: **252 wrapper invocations, 881 harness assertions,
  124 jq-driver assertions**, one TUnit result. Update T-1's existing order assertion
  to include cleanup at gate 8 and its idempotent recheck at gate 9. Update fixture
  shutdown state for T-4; T-12's explicit null counter case must deliberately stay
  live/unknown and refuse, while new V-6 owns exited/null success. Reuse existing
  assertions so roster counts remain exact. T-21 (3 invocations/9 assertions) and
  T-22 (2/6) remain included in each full harness; no duplicate ordinary command row.
- R-3: New `DockerStackDocumentationTests.Retired_temp_cleanup_and_retirement_gates_match`
  requires gate 8 container absence, mandatory gate 9, normal exited-temp recovery,
  one-budget wait, preview/resume boundaries and human gates for prune/other volumes.
  It rejects the obsolete absent/null caveat. Existing
  `DockerStackDocumentationTests.Main_volume_recycling_is_scripted_only` also runs;
  manual main-volume deletion remains absent. Documentation is not physical proof.
- R-4: `RollingVolumeRecycleDockerTests.C1008_Real_docker_comparison` remains **1 result,
  32 internal outcomes (5 baseline, 27 changed)** with checked Docker cleanup and
  Git/disk evidence. Do not replace its baseline ref or silently shrink its roster.

### Guard inventory

These are the decision-bearing guards introduced or crossed by D-1..D-6, separated
where wrapper/host, per-removal/final proof, or durable/local receipt checks can be
bypassed independently. A row may validate a compound typed proof object; its test
varies every conjunct independently, while its PC breaks one decisive conjunct.
This keeps one control per behavior without treating dozens of input encodings as
separate controls. Unchanged C1008 audit/reference/generation internals retain their
own plan's controls; this card adds controls at the new integration boundaries.
No guard is untested; all controls remain pending execution after land.

| Guard | Plan reference and invariant | Control |
|---|---|---|
| G-1 | D-2 wrapper typed retired/offline/zero-or-null proof | PC-1 |
| G-2 | D-2 independent host typed retirement proof | PC-2 |
| G-3 | D-2 wrapper main admission | PC-3 |
| G-4 | D-2 independent host main admission | PC-4 |
| G-5 | D-2 wrapper complete task/land proof | PC-5 |
| G-6 | D-2 independent host complete task/land proof | PC-6 |
| G-7 | D-2 successful all-state census; unknown cannot mean absent | PC-7 |
| G-8 | D-2 full ID and exact inspection binding | PC-8 |
| G-9 | D-2 exact temp project selection | PC-9 |
| G-10 | D-2 only allowed services | PC-10 |
| G-11 | D-2 at most one runner | PC-11 |
| G-12 | D-2 both Running=false and Status=exited | PC-12 |
| G-13 | D-2 complete actual mount topology (P-1) | PC-13 |
| G-14 | D-2 retained private-volume ownership/generation | PC-14 |
| G-15 | D-2 actual Compose model owns the targets | PC-15 |
| G-16 | D-2 validate entire candidate set before first removal | PC-16 |
| G-17 | D-5 rollout lock held through final census | PC-17 |
| G-18 | D-5 rollout then cache lock; no recursive acquisition | PC-18 |
| G-19 | D-2 per-removal fresh retirement/main proof | PC-19 |
| G-20 | D-2 per-removal fresh task/land proof | PC-20 |
| G-21 | D-2 per-removal container identity/image/mount/state unchanged | PC-21 |
| G-22 | D-2 exact removed ID is observed absent | PC-22 |
| G-23 | D-2 final project census proves absence and no replacement | PC-23 |
| G-24 | D-4 container-only nonforce removal; retain volumes/images | PC-24 |
| G-25 | D-3 drain success waits for exit/absence | PC-25 |
| G-26 | D-3 one original deadline, including host observation time | PC-26 |
| G-27 | D-3 standalone live-container refusal, no stop/kill | PC-27 |
| G-28 | D-1 normal retire orders cleanup before strict revalidation/recycle | PC-28 |
| G-29 | D-2 wrapper strict present/null volume admission unchanged | PC-29 |
| G-30 | D-2 host strict present/null volume admission unchanged | PC-30 |
| G-31 | D-5 rm failure remains failed with exact partial progress | PC-31 |
| G-32 | D-5 durable removal intent before effect | PC-32 |
| G-33 | D-5 checked host evidence copy before success | PC-33 |
| G-34 | D-5 retry re-proves remaining candidates; no inherited approval | PC-34 |
| G-35 | D-4 exact source/project/stamp/operation receipt lookup | PC-35 |
| G-36 | D-4 absent retry retains original image observation | PC-36 |
| G-37 | D-4 inspect/pin available audit image | PC-37 |
| G-38 | D-4 publication audit still required after container removal | PC-38 |
| G-39 | D-4 host preview has no destructive or audit effects | PC-39 |
| G-40 | D-4 wrapper preview reports pending volume proof honestly | PC-40 |
| G-41 | D-5 resume never starts new container cleanup | PC-41 |
| G-42 | D-5 retained matching local cleanup receipt before recycle | PC-42 |
| G-43 | D-1 fresh strict-volume facts after cleanup | PC-43 |
| G-44 | D-5 verifier raw manifest validation | PC-44 |
| G-45 | D-5 independent bridge validation | PC-45 |
| G-46 | D-5 independent host context validation | PC-46 |
| G-47 | D-5 host-only operation | PC-47 |
| G-48 | D-5 literal transport and timestamp normalization | PC-48 |
| G-49 | D-5 additive recycle field cannot relax unknown-field rejection | PC-49 |
| G-50 | D-5 approved-field receipts and secret redaction | PC-50 |
| G-51 | D-6 following deploy still requires host absence before clear | PC-51 |
| G-52 | D-6 drain requires requested main SHA | PC-52 |
| G-53 | D-6 fixture ownership prevents touching production/foreign objects | PC-53 |
| G-54 | D-6 test child timeout reaps all owned children before cleanup | PC-54 |
| G-55 | D-5 bridge copy independently checked despite successful host persistence | PC-55 |

### Positive controls

Each row is a distinct compiling/syntactically valid defect in the named production
script, except PCs 53/54 which mutate the new fixture's custody checks. Mutate one
row at a time. Direct validator tests isolate the boundary so a later guard cannot
mask its red. Whole-entry tests also assert effects. For controls on shared helpers,
mutate the C994 call site/argument where possible; do not repair unrelated C1008
behavior. Each row's exact method runs with
`--treenode-filter "/*/*/ClassName/ExactTestMethod"`, MinExecuted=1 and exact
Class.Method expectation; no whole-class or ordinary CP filter is a PC selector.

| PC | Break the mapped guard by this defect | Exact method expected red at assertion |
|---|---|---|
| PC-1 | Permit omitted runnerSessions in wrapper cleanup validator. | `RetiredTempContainerScriptTests.C994_Wrapper_retirement_proof_is_typed` at `c994-wrapper-retirement` |
| PC-2 | Host cleanup converts missing runnerSessions to null before validation. | `RetiredTempContainerHostTests.C994_Host_retirement_proof_is_typed` at `c994-host-retirement` |
| PC-3 | Remove wrapper main acceptingNewWork condition. | `RetiredTempContainerScriptTests.C994_Wrapper_main_admission_is_required` at `c994-wrapper-main` |
| PC-4 | Remove host main available condition. | `RetiredTempContainerHostTests.C994_Host_main_admission_is_required` at `c994-host-main` |
| PC-5 | Skip wrapper complete task/land census for cleanup. | `RetiredTempContainerScriptTests.C994_Wrapper_task_and_land_census_is_complete` at `c994-wrapper-work` |
| PC-6 | Treat failed host task census as an empty successful snapshot. | `RetiredTempContainerHostTests.C994_Host_task_and_land_census_is_complete` at `c994-host-work` |
| PC-7 | Convert nonzero cleanup ps to successful empty census. | `RetiredTempContainerHostTests.C994_Census_and_inspect_fail_closed` at `c994-census-known` |
| PC-8 | Drop inspected Id equality with requested full ID. | `RetiredTempContainerHostTests.C994_Census_and_inspect_fail_closed` at `c994-census-known` |
| PC-9 | Select by project prefix instead of exact equality. | `RetiredTempContainerHostTests.C994_Container_service_and_state_are_restricted` at `c994-project` |
| PC-10 | Add build-slots to disposable service allowlist. | `RetiredTempContainerHostTests.C994_Container_service_and_state_are_restricted` at `c994-service` |
| PC-11 | Remove one-runner cardinality check. | `RetiredTempContainerHostTests.C994_Container_service_and_state_are_restricted` at `c994-cardinality` |
| PC-12 | Accept any Running=false status, including created. | `RetiredTempContainerHostTests.C994_Container_service_and_state_are_restricted` at `c994-state` |
| PC-13 | Skip inspected mount-source equality in cleanup topology predicate. | `RetiredTempContainerHostTests.C994_Production_mount_topology_is_proven` at `c994-mounts` |
| PC-14 | Skip private-volume Compose-owner label equality at cleanup call site. | `RetiredTempContainerHostTests.C994_Production_mount_topology_is_proven` at `c994-private-identity` |
| PC-15 | Admit external=true on temp work in cleanup model validation. | `RetiredTempContainerHostTests.C994_Production_mount_topology_is_proven` at `c994-compose` |
| PC-16 | Move first rm before validating the last candidate. | `RetiredTempContainerHostTests.C994_Whole_candidate_set_precedes_removal` at `c994-whole-set` |
| PC-17 | Release rollout lock immediately before final census. | `RetiredTempContainerHostTests.C994_Rollout_lock_covers_final_census` at `c994-lock` |
| PC-18 | Swap cache/rollout lock acquisition at cleanup entry. | `RetiredTempContainerHostTests.C994_Rollout_lock_covers_final_census` at `c994-lock-order` |
| PC-19 | Reuse initial status proof inside per-ID removal loop. | `RetiredTempContainerHostTests.C994_Each_removal_rechecks_live_proofs` at `c994-recheck-status` |
| PC-20 | Reuse initial task/land snapshot inside per-ID loop. | `RetiredTempContainerHostTests.C994_Each_removal_rechecks_live_proofs` at `c994-recheck-work` |
| PC-21 | Remove per-ID identity/image/mount reinspection. | `RetiredTempContainerHostTests.C994_Each_removal_rechecks_live_proofs` at `c994-recheck-container` |
| PC-22 | Record removed from rm exit zero without exact-ID absence observation. | `RetiredTempContainerHostTests.C994_Final_absence_is_observed` at `c994-id-absent` |
| PC-23 | Replace final project census with saved initial candidate count minus removals. | `RetiredTempContainerHostTests.C994_Final_absence_is_observed` at `c994-project-absent` |
| PC-24 | Add -v to cleanup docker rm argv. | `RetiredTempContainerHostTests.C994_Exited_owned_containers_are_removed_without_volumes` at `c994-container-only` (assert forbidden argv before checking child exit) |
| PC-25 | Return drain success when retiredAt is first observed. | `RetiredTempContainerScriptTests.C994_Drain_waits_for_exit_and_removes_containers` at `c994-drain-exit` |
| PC-26 | Restart full wait budget when retirement becomes visible. | `RetiredTempContainerScriptTests.C994_Drain_uses_one_deadline` at `c994-one-deadline` |
| PC-27 | Stop a live container in standalone retire to make it removable. | `RetiredTempContainerScriptTests.C994_Retire_recovers_exited_and_absent_temp` at `c994-live-retire` |
| PC-28 | Invoke retire-temp-runner before cleanup in normal retire. | `RetiredTempContainerScriptTests.C994_Retire_recovers_exited_and_absent_temp` at `c994-retire-order` |
| PC-29 | Remove Assert-TempProjectAbsent from wrapper's strict null-volume branch. | `RetiredTempContainerScriptTests.C994_Strict_volume_admission_remains_independent` at `c994-wrapper-strict-null` |
| PC-30 | Remove project-count-zero condition from c1008_status_proof null branch. | `RetiredTempContainerScriptTests.C994_Strict_volume_admission_remains_independent` at `c994-host-strict-null` |
| PC-31 | Ignore second rm error and report success. | `RetiredTempContainerHostTests.C994_Removal_failure_and_crash_retry_preserve_progress` at `c994-rm-failure` |
| PC-32 | Make cleanup intent-save failure nonfatal before rm. | `RetiredTempContainerHostTests.C994_Receipt_write_and_copy_fail_closed` at `c994-intent-first` |
| PC-33 | Make host cleanup evidence copy failure nonfatal. | `RetiredTempContainerHostTests.C994_Receipt_write_and_copy_fail_closed` at `c994-receipt-copy` |
| PC-34 | Reuse prior approved candidates on normal cleanup retry without fresh proof. | `RetiredTempContainerHostTests.C994_Removal_failure_and_crash_retry_preserve_progress` at `c994-retry` |
| PC-35 | Select most recent cleanup receipt rather than matching retirement key. | `RetiredTempContainerHostTests.C994_Image_receipt_lookup_is_bound` at `c994-image-binding` |
| PC-36 | Save empty image over original image on already-absent cleanup. | `RetiredTempContainerHostTests.C994_Image_receipt_lookup_is_bound` at `c994-image-retention` |
| PC-37 | Use current deployment tag without inspecting the bound original digest. | `RetiredTempContainerHostTests.C994_Audit_image_is_pinned_and_required` at `c994-image-required` |
| PC-38 | Treat cleanup receipt success as permission to skip publication audit. | `RetiredTempContainerHostTests.C994_Audit_image_is_pinned_and_required` at `c994-audit-required` |
| PC-39 | Move preview return after first container rm. | `RetiredTempContainerHostTests.C994_Host_preview_never_mutates` at `c994-host-preview` |
| PC-40 | Report strict volume proof passed for present-container DryRun. | `RetiredTempContainerScriptTests.C994_Preview_reports_pending_volume_proof` at `c994-preview-pending` |
| PC-41 | Execute normal cleanup before handling ResumeRecycle. | `RetiredTempContainerScriptTests.C994_Resume_never_runs_container_cleanup` at `c994-resume-isolation` |
| PC-42 | Admit recycler from child exit zero even when local cleanup receipt is missing. | `RetiredTempContainerScriptTests.C994_Cleanup_receipt_is_required_before_recycle` at `c994-receipt-before-recycle` |
| PC-43 | Reuse pre-cleanup retired/status/task facts for volume admission. | `RetiredTempContainerScriptTests.C994_Cleanup_receipt_is_required_before_recycle` at `c994-fresh-volume-proof` |
| PC-44 | Skip verifier raw cleanup validation before intercepted live transport. | `RetiredTempContainerScriptTests.C994_Verifier_manifest_is_strict` at `c994-verifier-context` |
| PC-45 | Coerce bridge cleanup dryRun string to Boolean. | `RetiredTempContainerScriptTests.C994_Bridge_transport_preserves_literal_context` at `c994-bridge-context` |
| PC-46 | Default missing host cleanup operation ID to a valid constant. | `RetiredTempContainerHostTests.C994_Host_context_is_strict` at `c994-host-context` |
| PC-47 | Remove require_lane host from cleanup entry. | `RetiredTempContainerHostTests.C994_Host_context_is_strict` at `c994-host-lane` |
| PC-48 | Convert DateTime retirement to culture-dependent string before export. | `RetiredTempContainerScriptTests.C994_Bridge_transport_preserves_literal_context` at `c994-literal-transport` |
| PC-49 | Allow arbitrary recycle context fields while adding cleanupOperationId. | `RetiredTempContainerScriptTests.C994_Verifier_manifest_is_strict` at `c994-recycle-addition` |
| PC-50 | Serialize full Docker Config into cleanup receipt. | `RetiredTempContainerHostTests.C994_Cleanup_receipts_redact_unapproved_fields` at `c994-redaction` |
| PC-51 | Skip Assert-TempProjectAbsent before retired-placeholder clear in deploy-temp. | `RetiredTempContainerScriptTests.C994_Separate_invocations_preserve_audit_identity` at `c994-cross-run-image` (include present leftover -> zero clear POST subcase) |
| PC-52 | Remove requested-SHA equality from drain-temp's main check. | `RetiredTempContainerScriptTests.C994_Wrapper_main_admission_is_required` at `c994-drain-sha` |
| PC-53 | Remove exact ownership-label check in new fixture cleanup guard. | `RetiredTempContainerHostTests.C994_Fixture_custody_is_bounded` at `c994-fixture-ownership` |
| PC-54 | Return from fixture child timeout before kill-tree and await. | `RetiredTempContainerHostTests.C994_Fixture_custody_is_bounded` at `c994-child-reaped` |
| PC-55 | Ignore bridge scp failure while a valid host receipt exists. | `RetiredTempContainerHostTests.C994_Receipt_write_and_copy_fail_closed` at `c994-receipt-copy` |

Mutation runs baseline/break/red/restore/green **after land**; Code runs ordinary
V/R; Review judges this pending design and Code evidence before land. Snapshot
Mutation uses only local inherited children and offline fake boundaries, never a
Docker daemon or SSH executor. It does not run RD fixtures. Each PC must fail at
its named assertion with a successful build and one executed result; zero tests,
fixture setup errors, syntax errors or timeout alone are not red. Restore the exact
source and timestamps, rebuild through the slot gate and require green before
moving on. Evidence remains in the assigned external root, including restoration
records; no snapshot commit/push. Sequential controls share files, so no batching
credit is claimed. At most three ordinary rounds; no repeats after green.

### Out of scope

- Retire phone-home delivery and provider shutdown: unchanged mechanism; observation
  waits do not claim to test it. Live rollout/canary is caller-owned after land.
- Main state/cache opt-ins, routing settings, pruning, donor/marker removal and
  registration-lease changes: CARD-1010/other owners; no relaxed guard here.
- Full Unit: no application/domain implementation changes. Known 15 baseline Unit
  failures are not rerun or declared reproduced by this TestDesign. Any selected
  failure must be checked with its exact method at 157dd95 before being called
  inherited; never widen timeouts/assertions to hide it.
- Native Windows SSH/process qualification is a named follow-up slice
  **C994-Windows-transport-qualification** only if launcher/quoting code changes.
  This card adds validated scalar fields and uses existing ArgumentList conventions;
  Linux tests prove those values and PowerShell semantics, not Windows argv parsing.
  A need to change that launcher returns to Plan rather than silently adding a row.
- Exhaustive all-invalid Cartesian products: one-invalid-at-a-time admission plus
  null/zero x present/absent and late-change combinations cover distinct branches.
- Reexecuting the entire historical C1008 PC inventory: unchanged internals already
  have their own companion; R-1/R-4 and new boundary PCs cover this integration.

### Checkpoints

Closed ordinary scope after one committed S1-S3 group **and P-1's Plan disposition**.
Run on the Linux lane with Bash, jq, pwsh, node, Git and an owned nested Docker daemon.
One isolated Antiphon.Tests build; all other TUnit rows reuse it, unchanged source.
All rows are serial. No Antiphon.Agents.Pty.Tests or live deployment is scheduled.
Use the checkpoint tool through `scripts/build-slot.ps1` with `run --plan` pointing
at this file, `--after S1-S3 --expected-source-sha` set to the committed Code SHA;
await completion (repeat wait on exit 75). A slot timeout is not permission to run
unleased. Retain exact CHECKPOINT lines, roster/TRX and validated clean source/build
receipts. Driver/tool bootstrap is included in CP-1's build allowance, not a hidden
extra application test run. Remove producer-owned alternate outputs after children
finish. EstimatedMinutes includes build where applicable; Min counts TUnit results.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | S1-S3 | `tests/Antiphon.Tests -> bin-c994/` | c994-host | `/*/*/RetiredTempContainerHostTests/C994_*` | V-1,V-2,V-3,V-4,V-8,V-9 | exact 20 listed methods, 0 failed/skipped | 20 | 12 | true |
| CP-2 | S1-S3 | CP-1 | c994-wrapper | `/*/*/RetiredTempContainerScriptTests/C994_*` | V-4,V-5,V-6,V-7,V-8 | exact 13 listed methods, 0 failed/skipped | 13 | 6 | true |
| CP-3 | S1-S3 | CP-1 | c1008-host-regression | `/*/*/RemoteScriptContractTests/C1008_*` | R-1 | exact 11 existing methods, 0 failed/skipped | 11 | 8 | true |
| CP-4 | S1-S3 | CP-1 | c1008-wrapper-regression | `/*/*/RollingVolumeRecycleScriptTests/(C1008_Present_or_unknown_temp_keeps_null_refusal*)\|(C1008_Busy_routed_and_land_in_flight_refuse*)\|(C1008_Same_sha_and_partial_retries_are_safe*)\|(C1008_Option_manifest_is_strict*)\|(C1008_Documentation_and_transport_pins_match*)\|(C1008_Refusal_receipts_do_not_leak_secrets*)\|(C1008_Retired_absent_null_is_accepted*)` | R-1 | exact 7 existing methods, 0 failed/skipped | 7 | 4 | true |
| CP-5 | S1-S3 | CP-1 | c994-rolling-roster | `/*/*/RollingVolumeRecycleScriptTests/C1008_Legacy_rolling_and_jq_rosters_remain` | R-1,R-2 | 1 result; four modes and exact internal rosters above, 0 failed/skipped | 1 | 8 | true |
| CP-6 | S1-S3 | CP-1 | c994-docs | `/*/*/DockerStackDocumentationTests/(Retired_temp_cleanup_and_retirement_gates_match*)\|(Main_volume_recycling_is_scripted_only*)` | R-3 | exact 2 methods, 0 failed/skipped | 2 | 1 | true |
| CP-7 | S1-S3 | CP-1 | c994-real-lifecycle | `/*/*/RetiredTempContainerDockerTests/C994_Real_retired_temp_lifecycle` | V-9 | 1 result; RD-1..RD-10, 0 failed/skipped, owned residue=0 | 1 | 9 | true |
| CP-8 | S1-S3 | CP-1 | c1008-real-regression | `/*/*/RollingVolumeRecycleDockerTests/C1008_Real_docker_comparison` | R-4 | 1 result; 32 outcomes (5 base, 27 changed), 0 failed/skipped, owned residue=0 | 1 | 12 | true |

### Cost

All values are **estimates**, not measured by TestDesign. Ordinary Code floor is
**60 minutes**: CP-1 12 + CP-2 6 + CP-3 8 + CP-4 4 + CP-5 8 + CP-6 1 + CP-7 9 +
CP-8 12. This comprises 4 minutes setup/tool/application build plus 56 minutes V/R.
There are **56 TUnit executions**, not 56 minutes or 56 internal cases. No repeat
round is required; a failure-driven repeat is reported and consumes the operator's
budget. If P-1 broadens scope or a row's measured cost overruns, split the prerequisite
into **C994-production-mount-proof** and refresh the manifest; do not remove its
acceptance claim or compensate by omitting another row.

Separate Mutation floor: **224 minutes**, comprising 4 minutes initial setup and
**55 x 4 = 220 minutes** for the exact PC method filters above. Per control allowance
is baseline build/test 1 minute + defect/edit and red build/test 1.5 minutes + exact
restore and green build/test 1.5 minutes. These include isolated incremental builds,
not a hidden reuse of a mutant binary. PC-1 through PC-55 each use that 4-minute
allowance, MinExecuted=1 per phase, no suite run. The 55 controls are separate from
the 60-minute ordinary budget; they do not fit a 60-minute total including Mutation.
Combined planned setup/build + ordinary V/R + Mutation is **284 minutes**, excluding
implementation authoring and unpredictable build-slot queue time. If the operator
intends 60 minutes inclusive of Mutation, a new scope decision is required; this
artifact does not silently promise that total.

Savings: one shared ordinary build avoids seven duplicate 4-minute builds
(**28 minutes**); omitting the proposed duplicate T-22 command saves its **4-minute**
row because CP-5 already runs it in all modes. No whole-Unit baseline run is bought.
No estimated PC savings are claimed: controls share source files and must retain
independent red/restore/green evidence. Broader stress/repeat or native Windows work
belongs to the named follow-up slice, not a hidden extra run.

Handoff audit: bodies above read; **guards=55, mapped=55, missing=0, duplicate PC
maps=0**. All 55 PC recipes name a concrete executable input, source defect, exact
method and decisive assertion; execution is pending implementation/land. P-1 is an
explicit Plan gate, not unrecorded setup or a green claim. No build/test/Docker/live
rollout was run by this documentation task. The next Plan task must resolve the
production mount proof and reconcile the ordinary budget before authorizing Code.
