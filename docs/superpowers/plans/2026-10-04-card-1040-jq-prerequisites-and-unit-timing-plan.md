# CARD-1040: qualify jq prerequisites and account for Unit elapsed time

Date: 2026-10-04. Plan owner: `08164059-a403-4819-86c7-fa53e426b1e4`.
Inspected source: `0a94e15fc07a39c2781362e336be6c784855b101`.

## Outcome and stage boundary

Provision and qualify the already-pinned jq in the executing runner image, then
rerun only the fifteen inherited failures. Preserve the existing required-jq
assertions. The retained CARD-1021 run completed with zero timeout/aborted results;
its fifteen failures do not justify raising a deadline. Measure any separate
startup delay or stall from retained evidence before commissioning a fix.

This is a Plan artifact, not an implementation or activation receipt. TestDesign
is separate and must freeze the execution manifest and single prerequisite
control before Code. The choices below follow the brief; no human decision is
needed to write this plan. Land this document through the owning task's normal
landing protocol promptly, before the next stage. Do not push directly to master.

Read the live CARD-1040, CARD-1025, CARD-0927, CARD-1030 and CARD-1041 descriptions;
the full `jq` search included archived cards. Read the original
[CARD-1021 evidence](../../investigations/2026-10-04-card-1021-code-7c7fdeeb.md),
its retained TRX/source receipt/log, the jq image contract, shell fixtures, and
the orchestration, HTTP, logs, build/test and Docker rollout owners.

## Ground truth

Source coordinates below refer to the inspected commit, not future landed edits.
`Remote` means `tests/Antiphon.Tests/Scripts/RemoteScriptContractTests.cs`;
`Rolling` means `tests/Antiphon.Tests/Scripts/RollingVolumeRecycleScriptTests.cs`.

| Card assumption | What code or retained evidence actually does | Consequence |
|---|---|---|
| Fifteen Unit failures might be timeouts. | At `bd5f4dc9f0ed1501d825a2dab6fcab3088433e91`, TRX has 4,051 executed, 4,036 passed, 15 failed, 52 skipped, timeout=0, aborted=0. The fifteen failing durations are 0.0776095–4.6942987 seconds. The exact base selection at `d9cba338aa47d014cabf107183dd45c066b36a59` has 15/15 failed and timeout=aborted=0. | These are inherited assertion/missing-receipt failures. No full Unit replay or timeout increase. |
| Installing jq in source fixed the executing image. | `docker/session-runner-grok/Dockerfile:101–109` already downloads jq 1.7.1, checks the literal SHA-256 before installation, installs root-owned 0755 `/usr/local/bin/jq`, and checks its version. CARD-0927 explicitly deferred rebuilding/deploying the image. | Reuse that implementation; prove the actual container's image identity and installed binary. A checkout SHA or runner health alone is insufficient. |
| The current container still has no jq. | This Plan's read-only native bash probe as uid 1654 finds `/home/app/.local/bin/jq`, version `jq-1.7.1`, digest `5942c9b0934e510ee61eb3e30273f1b3fe2590df93933a93d7c58b81d19c8ff5`, owner app:app, mode 755. `/usr/local/bin/jq` is absent. The historical run did lack jq; the environment has since changed. | The present binary can support explicitly provisional diagnostics. Its installer/persistence and outer image provenance are unverified; it is not durable image-activation evidence. This Plan installed nothing. |
| All consumers already skip when jq is missing. | `Remote:1310` has `RequireLinuxJq` for C912/C973. C1008HostFixture launches real bash with inherited PATH (`Rolling:580–594`) without that guard. `scripts/c590-remote.sh:4016` refuses `RecycleToolsMissing` before census or volume removal. | Do not extend optional C912/C973 skipping to C1008's required proof. Preserve the fail-closed production guard. |
| The legacy roster test can be optional. | `Rolling:268–286` adds `-RequireJq` for its `present` invocation, requires 31 assertions per driver mode, and rejects `C973_JQ_SKIPPED` for present. The testing owner explicitly requires jq for this consumer. | A skip-based fix would weaken CARD-0983's contract and hide the missing prerequisite. |
| CARD-1025 duplicates the image work. | [CARD-1025](/api/cards/66b75498-78a5-4e97-a699-0ba4fe8db651) covers the outer deployment host's PATH, where real recycling runs. The runner image is a different filesystem/PATH. The fixture simulates the host lane inside its test process. | Link and retain both scopes. CARD-1025 owns outer-host jq and is a prerequisite for rollout phases that recycle; do not create another host-jq card. |
| Twelve similar Windows failures have this cause. | [CARD-1030](/api/cards/214820b4-4312-4c9b-bf8a-070196b8d5dc) covers Windows/WSL fixture transport, argv and lock paths, with jq already available there. It edits the same two test files. | Keep this card's proof on native Linux. Coordinate any later fixture edit with CARD-1030; no speculative Windows repair here. |
| 45 minutes is an enforced checkpoint ceiling. | `tools/Antiphon.Checkpoints/Execution/RowTimeout.cs` returns positive explicit values unchanged, otherwise max(15, 3 × estimate); the importer warning does not clamp it. [CARD-1041](/api/cards/28e970bb-0c0e-4bea-a55a-38a3fb9ad1ba) already owns the discrepancy. | Record effective limits; leave their semantics/documentation repair to CARD-1041. |

## Decisions

- **D-1 — Qualify durable provisioning using CARD-0927's existing image pin.**
  This is the smallest fix because the install implementation and image-contract
  tests already exist. Rejected: a second installer, unpinned apt installation,
  a new jq version, modifying a standing container, or treating the current
  user-owned binary as image activation. If an already-qualified image is active,
  reuse its matching receipt instead of rebuilding it again.
- **D-2 — Keep missing prerequisites visible.** Retain C1008's required-jq
  assertion and CARD-0983's present-mode requirement. Rejected: adding skips to
  these fifteen methods, accepting fewer executions, weakening assertions, or
  suppressing `RecycleToolsMissing`. Existing C912/C973 optional guards remain
  their owners' contract. A missing prerequisite stops the required proof with
  its path/shell diagnosis; it is not a green or skipped acceptance result.
- **D-3 — Separate three locations.** Record the native test shell, the runner
  image, and the outer deployment host independently. CARD-1025 owns outer-host
  installation and must provide its own receipt before recycling. This card
  owns test-image qualification and the fifteen consumer results. Rejected:
  closing either card on the other location's `jq --version`.
- **D-4 — Change no timeout, retry or process limit.** Retained evidence supports
  elapsed-time accounting, not a timeout defect. Rejected: increasing a
  fixture's 30-second deadline, LinuxShell's 60-second deadline, C1008Process's
  12-minute per-child deadline, row estimates to buy time, or host slot budgets.
  A new failure needs its exact child/method and base comparison first.
- **D-5 — Bound the implementation/proof work.** Use two 30–60 minute Code
  slices below, one ordinary execution per selected row, and one method-scoped
  positive control for the prerequisite behavior. No full Unit, namespace or
  assembly run; no whole-class positive controls. Daily broad coverage belongs
  to CARD-1039, planned separately. No new product behavior or test class is
  proposed merely to restate existing jq contracts.
- **D-6 — Let the caller own activation.** The Code delegate records evidence
  and executes its frozen proof after the prerequisite gate. The orchestrator
  performs any required rollout from the reviewed canonical checkout under
  [the staged rollout owner](../../docker-stack.md#staged-server2-rolling-rollout-card-0934),
  using named phases and all their stop gates. The delegate must not restart
  itself, pin fleet routing, or count rollout/drain waiting inside a Code slice.

## Placement and prerequisite gate

`GET /api/runner-defaults` and `GET /api/session-runners` were read through the
task's configured API at approximately 16:14 UTC. Defaults revision was 2;
there were eligible Linux and Windows descriptors, and an unavailable draining
descriptor. Those observations neither identify an image digest nor prove jq
activation. Re-read both routes when dispatching; do not copy today's fleet
location or occupancy into the plan's commands. Omit `-Runner` except for an
explicit host qualification. Use `-Platform Linux` for the native shell/image
proof only; Plan, TestDesign and document work need no platform pin. `-Platform
Any` unpins an existing OS requirement.

Before the required ordinary test rows, retain a sanitized qualification record:

1. Exact test source SHA and clean state, runtime OS/architecture, uid, actual
   bash executable, resolved jq path/version/SHA-256, and owner/mode. Probe from
   the same environment as the child, not a login shell with different PATH.
   Do not dump environment variables or credential-bearing Docker inspection.
2. From the host that owns the *outer* container, collect only container ID,
   creation/start time, immutable image ID/digest and selected source provenance;
   join this to the staged build/activation receipt and runner `buildVersion`.
   Nested Docker inside a runner cannot prove its own outer image identity.
3. Verify `/usr/local/bin/jq` in the activated container, the expected digest
   above, root:root 0755, and exact version as the runner uid. Also verify the
   child PATH resolves the qualified file (or the same verified file via an
   explicit alias). A user-home shadow binary alone is not this proof. Reuse
   the existing `jq-version` probe in
   `docker/session-runner-grok/verify-codex-image.sh`; do not run unrelated image
   qualification suites to obtain this single result.
4. If activation is needed, the caller coordinates CARD-1025's outer-host
   prerequisite and the documented staged rollout. Record before/after image
   identity and the named phase receipts. No direct volume manipulation or
   ad hoc install in the running runner is part of this plan.

Absent image identity or canonical host access is an explicitly pending
activation obligation. It does not block writing/finalizing the plan or reading
retained logs. A current-PATH diagnostic may be reported separately, but cannot
close the durable fix or substitute for the post-qualification rows.

## Retained elapsed-time evidence and measurement procedure

The original evidence remains under
`/work/worktrees/task-7c7fdeeb/.antiphon/c1021-unit/c1021-unit-20261004-063854-5ec0/`.
The base TRX is under
`/work/worktrees/task-7c7fdeeb/.antiphon/c1021-base/c1021-unit-base-prefix-20261004-065423-354e/`.
These are historical evidence locations, not dispatch destinations. Plan read
`source.json`, `run.trx` and `run.log`; it launched no test process.

| Measured phase | Seconds | Interpretation |
|---|---:|---|
| Build-slot wait | 0 | No queue delay in this receipt. |
| Isolated build | 102.2440571 | Build cost, separate from test execution. |
| Test-host startup | 110.3572973 | Driver-start to first TRX result boundary; not attributed to a particular initialization routine. |
| Test interval | 407.7809549 | Wall interval between TRX method boundaries, not sum of parallel method durations. |
| Teardown | 4.3298523 | Remaining host exit interval. |
| Test-host wall | 522.468084 | Startup + test interval + teardown; retained log exits normally with failed-test status. |

Build plus test-host wall is 624.7121411 seconds; the recorded approximately
10m27s end-to-end also includes orchestration/receipt overhead. Do not call that
difference a stall. The longest recorded method,
`RollingVolumeRecycleScriptTests.C1008_Busy_routed_and_land_in_flight_refuse`,
passed in 84.788839 seconds; it runs multiple children, so its total is not one
30-second child deadline. Other passed methods include
`C983_Required_jq_driver_rejects_successful_false_probe` at 44.0001454 seconds
and `C1008_Present_or_unknown_temp_keeps_null_refusal` at 41.5105173 seconds.
Duration alone does not prove a deadlock or justify a limit change.

Historical source receipt (verbatim; red, not a clean green certificate):

```text
CHECKPOINT c1021-unit commit=bd5f4dc9f0ed1501d825a2dab6fcab3088433e91 build=ok filter=/*/*/*/*[Category=Unit] executed=4051 passed=4036 failed=15 skipped=52 trx=/work/worktrees/task-7c7fdeeb/.antiphon/c1021-unit/c1021-unit-20261004-063854-5ec0/run.trx slot=granted waited=0s dirty=0 source=bd5f4dc9f0ed1501d825a2dab6fcab3088433e91 sourceState=clean buildSource=verified
```

Read-time SHA-256 inventory:

| Retained artifact | SHA-256 |
|---|---|
| HEAD source.json | `7e085e2d85e6bba695826688e8229f50c4b3fa31c30883a907646e0e78fc6461` |
| HEAD run.trx | `b970078efd70cf762e2e443b51cf9964fd5b9a9fc89b250bc59eb1af71eb3da6` |
| HEAD run.log | `c159fe48fb658bfa40ed4e7f3f7a79ea6a11e5a20a075fe234d388a5b6c77a58` |
| Base run.trx | `5e09ec6897f85bf38b2f937da857329cc2f88befb1cb55983f0b746774c4bc38` |

For the bounded follow-up, extract the same timing fields, individual outcomes,
start/end timestamps and overlapping intervals from each fresh row. Keep slot,
build, startup, method and teardown costs distinct. Compare the fifteen methods'
failure messages with the retained baseline; missing receipts are symptoms of
early jq refusal, not evidence of waiting for a receipt deadline.

If another retained run actually timed out, identify its source SHA, driver,
exact filter, effective deadline, last progress/child exit, build-slot receipt,
and owned process identity before assigning a cause. Relate it to CARD-0820,
CARD-0828, CARD-0818 or CARD-0815 only after reading that matching card. Missing
timestamps/process records mean **unattributed**, not permission for a new Unit
run. Only commission a separate, bounded method/startup probe when this evidence
names the unresolved phase. No such additional executable probe is authorized
by the current evidence.

## Slices and files

### S1 — Prerequisite admission and documentation (30–45 minutes)

Read/reuse `docker/session-runner-grok/Dockerfile`,
`docker/session-runner-grok/verify-codex-image.sh`,
`scripts/verify-card0660-codex-image.ps1`, and
`tests/Antiphon.Tests/Infrastructure/JqRunnerImageContractTests.cs` unchanged.
Obtain the qualification record above or explicitly record what the activation
owner still owes. Patch `docs/testing-and-build.md`'s jq receipt subsection with
the three-location distinction, same-shell qualification, strict fifteen-method
proof and links to CARD-0927/CARD-1025/this plan. Preserve its existing required-jq
contract. CARD-1025 owns changes to the rollout's host-install instructions in
`docs/docker-stack.md`; coordinate rather than duplicate those changes.

Commit/push the documentation slice before any build. This slice introduces no
new source behavior, so it does not warrant new tests or rerunning all existing
image contract classes. If the Dockerfile/probe actually needs a repair, stop
at that concrete finding and return to TestDesign with the additional file and
behavior; do not silently enlarge this scope.

### S2 — Qualified consumer proof and retained-log accounting (30–60 minutes)

After the activation gate, execute the frozen narrow rows at one clean committed
SHA, using the unchanged two test files `Remote` and `Rolling`. Store the
sanitized qualification, counters, per-method roster, measured timing table,
image identity, unedited CHECKPOINT lines and outstanding obligations in
`docs/investigations/2026-10-04-card-1040-jq-and-unit-timing.md` (to be created by
Code). Generated TRX, JSON and logs stay ignored in task-owned evidence paths;
do not copy the inherited evidence directories into git. Commit/push the
Markdown evidence slice after all runs/children finish.

Fresh execution must show all fifteen exact methods passing with zero skips;
the legacy method's four driver-mode summaries must each have 31 assertions,
and its present mode must actually execute the required-jq path. A new failure
gets an exact-method replay at the appropriate committed base with the **same
qualified jq environment**, preserving diagnostics. The historical no-jq base
failure alone cannot classify a different failure after provisioning.

## TestDesign handoff: bounded checkpoint candidates

The following is a roster proposal, not an executable manifest or a folded
TestDesign stage. TestDesign must freeze exact selectors, argument-expanded
counts, import validity, effective deadlines, isolated outputs and the control
seam. Name the lane in every executable row. The source census here is twelve
Remote methods and three Rolling methods, one result per method.

| Candidate | Lane | Exact method roster / scope | Expected result | Estimated wall, including build |
|---|---|---|---|---:|
| CP-1 | Native Linux, qualified runner image | Remote: `C1008_Recycle_receipt_records_disk_and_partial_failure`, `C1008_Recycle_audits_work_as_1654`, `C1008_Recycle_preserves_tmp_copyup`, `C1008_Recycle_refuses_uninspectable_git`, `C1008_Recycle_exact_default_volumes`, `C1008_Recycle_refuses_references_and_unknown_census`, `C849_Deploy_prepares_and_verifies_before_acceptance`, `C1008_Retire_temp_rechecks_absence_and_retirement`, `C1008_Retire_temp_reclaims_below_cache_disk_gate`, `C1008_Recycle_dry_run_never_mutates`, `C1008_Recycle_resume_requires_matching_receipt`, `C1008_Recycle_refuses_unpublished_and_dirty_work` | Exactly 12 passed, 0 failed/skipped | 6 minutes |
| CP-2 | Native Linux, qualified runner image | Rolling: `C1008_Retired_absent_null_is_accepted`, `C1008_Refusal_receipts_do_not_leak_secrets` | Exactly 2 passed, 0 failed/skipped | 4 minutes |
| CP-3 | Native Linux, qualified runner image | `/*/*/RollingVolumeRecycleScriptTests/C1008_Legacy_rolling_and_jq_rosters_remain` | Exactly 1 passed, 0 failed/skipped; four required driver receipts | 8 minutes |

Use the documented prefix-OR selector form for CP-1 and CP-2, with only those
methods; the original base attempt's non-prefix OR selected zero tests.
Do not replace this roster with `C1008*`, either entire class, or the Unit category.
Estimates are planning estimates, not measured qualified-green costs. Their
current derived row deadlines would be 18/15/24 minutes, not a new global limit.
The three-row ordinary estimate is 18 minutes plus tool setup, authoring and
evidence inspection, within S2's budget. TestDesign may split the twelve-method
row further if retained per-method costs require it; it must not widen scope or
increase estimates simply to mask a stall.

Freeze **one PC for the one provisioning behavior**: in an isolated child/test
environment, make jq unavailable while preserving all other prerequisites;
`/*/*/RemoteScriptContractTests/C1008_Recycle_exact_default_volumes` must execute
and fail its success assertion with `RecycleToolsMissing`, then pass with the
qualified jq restored. The absence control must not remove/rename a shared
binary, edit a global PATH/profile, touch live deployment state, or convert
absence into a skip. TestDesign owns the deterministic environment seam and
red/restore/green cost. Build errors, process-start failures and zero tests are
not this red. Existing CARD-0927 checksum/version PCs remain under that card;
do not clone its whole inventory. Measurement-only claims need retained data,
not extra synthetic behavior tests. Code ordinary proof does not discharge PC
evidence reserved for separately commissioned Mutation.

Execute through the checkpoint tool's committed-slice manifest, with host
build-slot gates, exact expected source SHA, isolated forward-slash outputs and
foreground wait to completion. Any tool bootstrap uses `build-slot.ps1` and its
own output. Slot timeout is not run; no unleased retry. Preserve source/build
provenance and remove only task-owned build outputs after children exit. Code
and Review run `scripts/check-evidence-diff.ps1` across their full task range.

## Completion conditions

The card's fix is complete when durable image qualification and the fifteen
passing methods are recorded, the measured timing account states the supported
conclusion (currently no demonstrated timeout), and CARD-1025's distinct host
dependency remains explicitly linked. If activation/provenance remains pending,
report that obligation rather than closing on provisional user-PATH success.
No AppHost restart, timeout change, whole-Unit run, daily-schedule change or
checkpoint-ceiling repair follows merely from landing this plan.

--- next stage ---
next: test-design
handoff: Freeze the 15-method native-Linux manifest and one isolated jq-absence control; preserve required-jq assertions, qualify CARD-0927 image activation, and coordinate outer-host jq with CARD-1025. Use retained timing evidence, no timeout increase or whole-Unit run; keep Code slices within 30–60 minutes.
artifact: docs/superpowers/plans/2026-10-04-card-1040-jq-prerequisites-and-unit-timing-plan.md
