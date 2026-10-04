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

## Verification design

TestDesign: `fea04260-8710-4eba-ae70-3206c1245256`, 2026-10-04. Inspected
checkout: `2c15feb754797f9b76035fdbec19d68168ec93df`; landed fix design:
`5de230cf75f24aa4a5c00cd8d9d53293bf00d064`. This appended section freezes the
verification choices; it does not change D-1 through D-6 or either Code slice.
The earlier Plan handoff is historical; the next stage after this design is Code.

### Inspection

`Remote` and `Rolling` retain the file abbreviations defined above. Read every
selected method body, not just its name. All fifteen have `[Test]`, no
`[Arguments]`, and `ParallelLimiter<ProcessSpawnLimit>`; each contributes exactly
one TUnit result. Internal loops and driver assertion totals do not expand `Min`.

| Bodies read | Boundaries -> verification IDs or exclusion |
|---|---|
| Remote `C1008_Recycle_exact_default_volumes` | Default main removes exactly work/tmp/dind in order, preserves eleven other volumes and broker, never prunes; Options `{}`/null accepted versus empty-string/false/array/nonempty/omitted refused; four private roles x external/name/label, three cache roles x external/name/owner/missing, and both service mount lists -> V-1/R-1, PC-1. |
| Remote `C1008_Recycle_refuses_references_and_unknown_census` | Held/released native flock; valid controlled stop; five mount defects; running/exited references; fourteen census/stop/late-change faults; three counters x null/string/bool/negative/fraction/positive plus omitted; three state-init states; exact/descendant/ancestor/last-target binds; duplicate identity; generation drift; seven task-envelope/detail faults -> V-1/R-1. |
| Remote `C1008_Recycle_audits_work_as_1654`, `C1008GitGraph` | Readonly helper argv includes uid 1654; ordinary, linked-with-space, standalone and bare Git layouts appear as hashed audit identities -> V-1/R-1. Helper creates real private Git repositories and origin, never publishes task source. |
| Remote `C1008_Recycle_refuses_unpublished_and_dirty_work` | Published clean control; quiescent audit drift; unpublished HEAD/branch/tag/other-remote/linked/detached/bare, dirty/staged/untracked -> V-1/R-1. |
| Remote `C1008_Recycle_refuses_uninspectable_git` | Complete-history control; exit128/timeout/empty/nonnumeric/negative, shallow/partial/stale/deleted/missing origin, broken gitdir/escaping link/missing objects/origin failure; index.lock/MERGE_HEAD/rebase-merge and linked index lock -> V-1/R-1. |
| Remote `C1008_Recycle_preserves_tmp_copyup` | Selected tmp removal and no volume-nocopy; wrong recreated mode versus missing assets -> V-1/R-1. Real image copy-up remains the rollout owner's qualification. |
| Remote `C1008_Recycle_resume_requires_matching_receipt` | Seed failure at creation intent; second removal failure; stop/owned-remove/first/third removal recovery; eight receipt/store/generation/stop-receipt drifts; recorded versus foreign recreated container -> V-1/R-1. |
| Remote `C1008_Recycle_receipt_records_disk_and_partial_failure` | Before/after/signed disk delta; atomic journal failure at preflight/stop/remove/volume, then recovery; empty/invalid/negative/after/reverse/filesystem disk readings; partial removal; failed/successful bridge copy with exact final journal bytes and no second removal -> V-1/R-1. |
| Remote `C1008_Retire_temp_rechecks_absence_and_retirement` | Retired absent/null control; nine retirement/status changes; retained volume/census failure/counterpart refusal; exited state-init; shared null-refuses/integer-zero-accepts -> V-1/R-1. |
| Remote `C1008_Retire_temp_reclaims_below_cache_disk_gate` | Four temp defaults reclaimed with allocation gate forced to refuse if consulted -> V-1/R-1. |
| Remote `C1008_Recycle_dry_run_never_mutates` | Main/temp x preview/apply; empty effect trace during preview; busy status changed before apply refuses -> V-1/R-1. |
| Remote `C849_Deploy_prepares_and_verifies_before_acceptance`, `Block`, `Order` | Main/temp prepare/ready ordering; three busy counters; missing preserved cache; exact retired targets and preserved sentinel bytes; prepare-fails/ready-fails x parent/temp with no unsafe effects -> V-1/R-1. |
| Rolling `C1008_Retired_absent_null_is_accepted` | Wrapper emits host case without clearing retirement; copied operation ID/stamp drives the actual host entry; exact four removed defaults -> V-2/R-2. |
| Rolling `C1008_Refusal_receipts_do_not_leak_secrets` | Malformed Git refusal and public receipt exclude filename/stderr sentinels; accepted Docker receipt excludes Env/foreign label; HTTP refusal excludes error/token sentinel -> V-2/R-2. |
| Rolling `C1008_Legacy_rolling_and_jq_rosters_remain`, `C1008Process` | Present with `-RequireJq`, absent, missing-shell, failing-shell; four 31-assertion driver receipts, present never skipped; 12-minute child deadline unchanged -> V-3/R-3. |
| Rolling `C1008HostFixture` and `C1008WrapperFixture` constructors, Run, state/trace readers and Dispose; Remote `LinuxShell`, `PrepareLinuxShellScript`, `RequireLinuxJq`; `DelegateScriptRunner.RepoRoot`; `ProcessSpawnLimit` | Test-private roots and JSON state, real native bash/pwsh, inherited PATH, explicit fake HTTP/Docker boundary, Run(extra) injection before dispatch/trap, 30-second fixture and 60-second LinuxShell deadlines; limiter=1 inside each assembly -> all rows and PC-1. Optional RequireLinuxJq is not added to the selected methods. |
| `scripts/fixtures/c1008-fake-docker.sh`, `c1008-recycle-cases.json`, `c727-fake-http.ps1`, `c727-fake-verify.ps1`, `c973-marker-reader.sh`; `scripts/test-deploy-server2-jq.ps1`; jq probe, required admission and T-20 bodies in `test-deploy-server2.ps1` | File-backed boundary faults, typed offline statuses, actual cold-reader slices; present 24 groups/66 invocations/227 assertions versus optional absent modes 23/62/218; driver=31 each -> R-1..R-3. These substitutes prove neither live Docker effects nor outer-host installation. |
| Dockerfile jq RUN; `verify-codex-image.sh` jq-version/need_uid/result; wrapper `Invoke-Probe` and jq row; all `JqRunnerImageContractTests` methods/argument rows | Pin/digest-before-install/root ownership; exact successful version with empty stderr; existing twelve version vectors include wrong versions, extra output, nonzero exit and stderr. Source contracts inspected, not rerun or remutated: no image/probe edit is proposed. Runtime admission below is still mandatory. |
| `PlanTableImporter`, `CheckpointManifest`, `ManifestValidator`, `RowTimeout`; testing owner jq/checkpoint/filter/slot/Mutation sections | Escaped method-prefix OR, exact floors, isolated outputs, serial execution, 18/15/24-minute derived row deadlines -> CP-1..CP-3. No timeout override or knownFlaky entry. |
| Nearest evidence document `docs/investigations/2026-10-04-card-1021-code-7c7fdeeb.md`; retained source.json/TRX/log identities; Plan timing table | Code's new evidence Markdown follows that provenance format. Re-hashed all three retained HEAD artifacts: identical to the Plan inventory. Retained red/zero-timeout evidence supplies accounting, not a fresh green or activation verdict. |

Admission and missing setup, before CP-1:

- The caller supplies the outer container/image/build provenance receipt and,
  if rollout is required, CARD-1025's separate host-jq qualification plus named
  phase receipts. Neither this checkout nor nested Docker establishes these
  facts. TestDesign has not obtained a fresh activation receipt. S1 may document
  the obligation; S2 cannot start required proof until it is satisfied.
- Qualify the actual non-login child environment as uid 1654: native Linux x64,
  bash, node, pwsh, Git, flock and ordinary core utilities must execute. Record
  resolved bash/jq, jq version/digest/owner/mode, and resolve symlinks. jq must
  resolve to the qualified `/usr/local/bin/jq` (or the same file through an
  explicit alias), with the Plan's SHA-256, root:root 0755 and `jq-1.7.1`.
  A same-digest user-home copy is insufficient image-custody evidence.
- Reuse only the existing `jq-version` probe row: the owning host may invoke
  `/c660/verify-codex-image.sh jq-version` in a throwaway container of the recorded
  immutable image, with the reviewed script mounted readonly, uid `1654:1654`,
  `--network none`, no ports/socket, and a private writable
  `/c660-home` tmpfs owned by 1654. Require exit 0 and exactly
  `C660_ROW jq-version ok jq-1.7.1 as uid 1654`. This supplements the active
  container checks; a successful throwaway probe alone is not activation.
  Do not invoke the full wrapper, which also runs unrelated provider probes.
- Record the Code source SHA and clean source before the three rows. Probe with
  the same PATH inherited by their children; do not dump the environment. Record
  any child-only prerequisite adjustment. Missing bash/node/pwsh/Git, a broken
  build-slot broker, or inability to identify the executing image is missing
  setup, never a test skip, intended PC red, or reason to extend deadlines.

### Delivery inventory

No new or changed asynchronous delivery path exists in D-1..D-6/S1..S2. The
change is image-prerequisite qualification and documentation; it adds no
producer, queue, destination, persistence handoff or recovery branch. Busy and
already-eligible recipients and enqueue/crash cuts are consequently excluded.
The async C# methods await owned subprocesses; they do not implement queued
session delivery. No UserPrompt/session receipt is claimed by this card.

The existing synchronous recycle evidence path exercised by R-1 is host script
-> host journal -> bridge copy -> local receipt file, joined by operation ID,
source SHA and project. The test reads final copied bytes and compares them to
the surviving host journal after a failed then successful copy. A request or
copy exit alone is not its oracle. Its fake SSH/scp, file-backed Docker, canned
HTTP statuses and fake privilege boundary cannot prove live rollout delivery,
image activation, actual UID isolation, or a recipient transcript. Caller-owned
activation evidence remains a separate prerequisite, not inferred from these
substitutes. No delivery/recovery guard is introduced or changed here.

### Proves it works now

- V-1: Qualified jq lets all twelve frozen Remote consumers complete their
  existing success and refusal assertions | native Linux shell/PowerShell with
  private boundary fakes | CP-1 exact method-prefix OR | exactly 12 passed,
  zero failed/skipped, precisely the twelve names in the inspection table.
- V-2: Qualified jq lets the wrapper-to-host retirement and receipt-custody
  consumers complete | native Linux wrapper and host fixtures | CP-2 exact OR |
  exactly 2 passed, zero failed/skipped, exact four retired targets and no public
  credential sentinels.
- V-3: Required present-jq and the three optional driver modes retain their
  distinct behavior | native Linux, actual marker-reader shell | CP-3 exact
  method | exactly 1 passed, zero failed/skipped; four
  `C973_JQ case=<mode> assertions=31 failures=0` lines; present includes
  `C973_JQ_PROBE available=True`, PASS T-20 and 24/66/227, excludes
  `C973_JQ_SKIPPED`; other modes include the named internal skip and 23/62/218.
  Those deliberate internal optional branches are not TUnit skips.

### Guards the regression

- R-1: Missing jq cannot masquerade as accepted C1008 proof; prerequisite
  provisioning must preserve every existing recycle/refusal/recovery assertion |
  the twelve CP-1 methods and boundary combinations above. Decisive PC oracle:
  `RemoteScriptContractTests.C1008_Recycle_exact_default_volumes` first
  `f.Removed.ShouldBe(new[] { "antiphon-runner_work", "antiphon-runner_runner-tmp",
  "antiphon-runner_dind-data" }, "recycle-exact-defaults: exact ordered defaults; " + run.Output)`.
  Full ordinary execution also retains the no-removal, refusal-diagnosis,
  receipt-byte, generation and preservation assertions in each selected body.
- R-2: Provisioning must not weaken retired/null acceptance or sanitized failure
  receipts | CP-2 exact two methods | host removes the exact four temp defaults;
  malformed Git retains all targets; public output/journal excludes every
  fixture credential sentinel; expected typed refusal survives.
- R-3: A missing prerequisite must not silently turn required present coverage
  into optional coverage | `RollingVolumeRecycleScriptTests.C1008_Legacy_rolling_and_jq_rosters_remain`
  | existing `arguments.ShouldContain("-RequireJq")`, four summary assertions,
  `run.Output.ShouldNotContain("C973_JQ_SKIPPED")` for present, and exit 0.
  No new skip or reduced assertion/execution count is allowed.

### Guard inventory

- G-1: D-1/D-2, S1 admission and S2 required-consumer proof: jq availability in
  the executing native shell is a required prerequisite; its loss must make the
  unchanged success consumer red with `RecycleToolsMissing`, never green/skip |
  PC-1.

Scope census: guards=1, mapped=1, missing=0, duplicate PC maps=0. This is the one
provisioning behavior required by the Plan. There are no new/changed product
guards, queue guards or recovery guards. R-1/R-2 deliberately retain many
independently bypassable **existing** C1008 guards; they are regression consumers
of jq, not repairs to those guards. Their mutation batteries remain with
CARD-1008. Existing C983 required-driver admission and C927 pin/version/digest
guards similarly retain their owners' controls; no new bypass of their
assertions is commissioned here. Runtime image identity/ownership, strict roster
and source provenance are evidence-admission checks; PC-1 does not claim to
mutation-test them. An edit to any of these source guards/probes/fixtures changes
scope and returns to TestDesign, rather than broadening this single-PC battery.

### Positive controls

- PC-1: Break G-1 with the compiling, method-local prerequisite defect below;
  expect **exactly**
  `RemoteScriptContractTests.C1008_Recycle_exact_default_volumes` red at its
  first `f.Removed.ShouldBe(...)`, labelled
  `recycle-exact-defaults: exact ordered defaults`. Expected actual list is empty
  versus the three literal defaults; its failure detail must include
  `C1040_PC_JQ_ABSENT` and production `RecycleToolsMissing`. The fixture child
  exits 2 through the production refusal. Compile errors, timeout, launch error,
  other missing executables, setup error, zero results, wrong assertion or a skip
  do not count. This is a required-consumer control, not a mutation removing the
  production fail-closed jq guard.

In the SourceLanding snapshot only, replace the **first** `var run = await
f.Run();` in that method with the following. Leave its assertions and all other
calls unchanged. This uses the existing `extra` seam; no permanent fixture/helper
edit is needed and no Code-stage source change is requested:

```csharp
var run = await f.Run(extra: """
    node <<'C1040_NODE'
    const fs = require('node:fs'), path = require('node:path');
    const root = process.env.C1008_FIXTURE_ROOT;
    if (!root || !path.basename(root).startsWith('c1008-host-'))
        throw Error('C1040_PC_SETUP_ROOT');
    const bin = path.join(root, 'pc1-no-jq');
    fs.mkdirSync(bin);
    const seen = new Set();
    for (const part of process.env.PATH.split(path.delimiter)) {
        const dir = path.resolve(part || '.');
        let names;
        try { names = fs.readdirSync(dir); }
        catch (e) {
            if (['ENOENT', 'ENOTDIR', 'EACCES'].includes(e.code)) continue;
            throw e;
        }
        for (const name of names) {
            if (name === 'jq' || seen.has(name)) continue;
            const source = path.join(dir, name);
            try {
                if (!fs.statSync(source).isFile()) continue;
                fs.accessSync(source, fs.constants.X_OK);
            } catch (e) {
                if (['ENOENT', 'ENOTDIR', 'EACCES'].includes(e.code)) continue;
                throw e;
            }
            fs.symlinkSync(source, path.join(bin, name));
            seen.add(name);
        }
    }
    for (const name of ['bash', 'node', 'pwsh', 'git', 'flock', 'sed', 'mkdir'])
        if (!seen.has(name)) throw Error('C1040_PC_SETUP_TOOL_' + name);
    C1040_NODE
    PATH="$C1008_FIXTURE_ROOT/pc1-no-jq"; export PATH; hash -r
    if command -v jq >/dev/null 2>&1; then
        printf '%s\n' C1040_PC_SETUP_JQ_VISIBLE >&2; exit 97
    fi
    printf '%s\n' C1040_PC_JQ_ABSENT
    """);
```

The private directory mirrors executable resolution in original PATH order,
excluding **every** basename `jq`; hashing is cleared before the real admission
check. All other executable entries are symlinks to their original files. This
is actual child PATH absence, not a fake success/failure from jq or a mock of
`command -v`. It does not remove/rename a shared binary or edit parent PATH,
profiles, daemon settings or live state. Fixture Dispose removes the owned root.
It proves the consumer detects loss of its prerequisite; it cannot prove a
Dockerfile rebuild or durable deployment, which requires the activation receipt.

Mutation runs baseline/break/red/restore/green **after land**; Code runs V/R;
Review judges this executable design and ordinary receipts **before land**.
Use precisely `/*/*/RemoteScriptContractTests/C1008_Recycle_exact_default_volumes`
for all three phases, `-MinExecuted 1` and
`-Expect RemoteScriptContractTests.C1008_Recycle_exact_default_volumes`.
Each phase gets its own build output `bin-c1040-pc1-baseline/`,
`bin-c1040-pc1-red/`, `bin-c1040-pc1-green/`, and distinct results directory under
the assigned external evidence root. Use the unchanged external copy of
`scripts/run-checkpoint.ps1` and `scripts/lib/build-slot.ps1` per the testing
owner; it takes its own slot. Do not use strict clean-source mode on the intended
dirty red. Baseline and restored green must be one pass/zero skips, with clean
landed-source provenance; red must be one expected assertion failure, driver
exit 1. Await all children, restore exact tracked/index bytes and timestamps,
rebuild green, remove only owned alternate outputs and retain external restoration
records. Never commit the mutant or any SourceLanding snapshot amendment.

### Out of scope

- Whole Unit/namespace/class/assembly runs, extra repeats, timeout/retry/process
  budget changes, and a speculative startup probe. The retained timing receipt
  already distinguishes slot/build/startup/test/teardown with zero timeouts;
  unavailable additional evidence is reported as unattributed.
- Windows/WSL path/argv/lock behavior (CARD-1030), outer-host installation
  (CARD-1025), daily broad qualification (CARD-1039), and timeout semantics
  (CARD-1041). Their boundary combinations cannot be inferred from Linux results.
- New image implementation, provider qualification suites, reinstalling jq in a
  standing container, real volume removal or rollout by this delegate. The
  orchestrator owns activation and its stop gates. Missing activation proof is
  an explicit incomplete runtime obligation, not permission to close the card.
- Additional PCs for unchanged image/version/rolling safety logic. D-5 freezes
  one prerequisite behavior; no new test class or shared fixture seam is needed.
  A new source defect needs its exact method replay at the committed base in the
  same qualified environment, then a separately scoped repair/design.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1040-remote/` | linux-qualified-remote | `/*/*/RemoteScriptContractTests/(C1008_Recycle_receipt_records_disk_and_partial_failure*)\|(C1008_Recycle_audits_work_as_1654*)\|(C1008_Recycle_preserves_tmp_copyup*)\|(C1008_Recycle_refuses_uninspectable_git*)\|(C1008_Recycle_exact_default_volumes*)\|(C1008_Recycle_refuses_references_and_unknown_census*)\|(C849_Deploy_prepares_and_verifies_before_acceptance*)\|(C1008_Retire_temp_rechecks_absence_and_retirement*)\|(C1008_Retire_temp_reclaims_below_cache_disk_gate*)\|(C1008_Recycle_dry_run_never_mutates*)\|(C1008_Recycle_resume_requires_matching_receipt*)\|(C1008_Recycle_refuses_unpublished_and_dirty_work*)` | V-1, R-1 | exactly 12 listed methods, 12 passed, 0 failed/skipped; no extra names | 12 | 6 |
| CP-2 | S1 | `tests/Antiphon.Tests -> bin-c1040-retirement/` | linux-qualified-retirement | `/*/*/RollingVolumeRecycleScriptTests/(C1008_Retired_absent_null_is_accepted*)\|(C1008_Refusal_receipts_do_not_leak_secrets*)` | V-2, R-2 | exactly 2 listed methods, 2 passed, 0 failed/skipped; no extra names | 2 | 4 |
| CP-3 | S1 | `tests/Antiphon.Tests -> bin-c1040-legacy/` | linux-qualified-legacy | `/*/*/RollingVolumeRecycleScriptTests/C1008_Legacy_rolling_and_jq_rosters_remain` | V-3, R-3 | exactly 1 listed method passed, 0 failed/skipped; all four 31-assertion driver receipts and required present T-20 | 1 | 8 |

All rows run on **native Linux with the qualified image**, after committed S1
and the activation gate, during S2. S2 writes its final evidence after execution;
requiring that report commit before the runs would be circular. The union is
the entire ordinary executable scope: fifteen unique methods, fifteen TUnit
executions, three builds. Prefix `*` exists only for pinned discovery's OR hint;
the fresh TRX roster must still equal these literal names. The tool enforces a
floor, not an exact count or every prose condition in Expect: Code/Review must
check equality, zero skips, and nested driver receipts separately.

One foreground-supervised run covers the group. Bootstrap the checkpoint tool
once, through `scripts/build-slot.ps1`, into `bin-c1040-tool/` with
`UseAppHost=false`; this one-minute setup build is explicitly outside the test
table. Then run in PowerShell:

```powershell
$c1040Source = (git rev-parse HEAD).Trim()
dotnet tools/Antiphon.Checkpoints/bin-c1040-tool/Antiphon.Checkpoints.dll run --plan docs/superpowers/plans/2026-10-04-card-1040-jq-prerequisites-and-unit-timing-plan.md --after S1 --serial --expected-source-sha $c1040Source --max-wait 50s
```

The tool acquires the per-build/per-row leases; do not wrap it in a second slot.
On exit 75, use that emitted run ID with `wait --max-wait 50s` until terminal;
never settle with owned work still running. Serial is required because the
process limiter is assembly-local, not shared across row hosts. Effective
derived row deadlines are 18, 15 and 24 minutes and the total is 46 minutes;
these are admission limits, not wall estimates or new overrides. Exit 4 means
not run due to slot timeout. Any other failure retains its diagnostic receipt;
never rerun unleased or widen a filter. Use the source-receipt validator, retain
unedited CHECKPOINT lines and actual method roster, and clean the tool output
plus all row-owned `bin-c1040-*` directories only after every child exits.

For elapsed-time accounting, retain each row's slot wait, build duration,
driver-start/first-result/last-result/exit boundaries, per-method timestamps and
outcomes; use intervals, not sums of parallel durations. Keep the inherited
red table separate. If new evidence lacks a needed boundary, report that phase
unattributed. No additional timed experiment is in this manifest.

### Cost

- **Ordinary V/R floor (Code), estimated: 18 minutes** = CP-1 Remote twelve-method
  prefix-OR 6 + CP-2 Rolling two-method prefix-OR 4 + CP-3 exact legacy method 8.
  Includes all three isolated builds (estimated 2 minutes each) and 12 minutes
  of test/startup/teardown. No broad Unit execution is included.
- **PC floor (Mutation), estimated: 9 minutes** = PC-1 exact-method baseline
  build/run 3 + break/setup 0.5 + red build/run 2 + restore 0.5 + green build/run 3.
  Every run uses the exact PC-1 filter above; no whole-class phase. A missing
  baseline or unexpected red is a finding, not an excuse to spend an unbounded
  retry budget.
- **Setup outside CP rows, estimated: 8 minutes** = tool bootstrap 1 + same-shell
  prerequisite/receipt reconciliation 7, assuming the caller has supplied a
  qualified activation receipt. Rollout/drain/host access waiting is explicitly
  excluded from a Code slice and cannot be hidden in a CP estimate.
- **Total planned verification: 35 minutes estimated** = setup 8 + ordinary 18
  + PC 9. Code-side verification is 26 minutes; Mutation-side is 9. Documentation
  authoring/review/evidence interpretation fills the Plan's S1 30–45 and S2
  30–60 minute slices; it is not a reason to combine Code and Mutation stages.
  These are estimates, not fresh runtime measurements. The only measured build
  comparison remains the retained 102.2440571-second build and
  522.468084-second test-host wall.
- Scope savings versus repeating the retained 4,051-execution Unit lane:
  **4,036 executions avoided (99.63%)** per ordinary pass. Claimed wall-clock
  savings: **0 minutes** until qualified jq timings exist, because historical
  failures stopped early and these three isolated builds add overhead. One
  prerequisite PC also avoids duplicating the unchanged C1008/C983/C927 mutation
  batteries; no numeric time saving is claimed without their measured costs.

Handoff audit: all selected bodies and their fixtures/helpers read; fifteen
non-parameterized results; guards=1, mapped=1, missing=0, duplicate PC maps=0;
one concrete executable PC; ordinary floor=18, PC floor=9, setup=8, total=35
minutes. Code may begin S1; required S2 execution waits for the explicit image
activation prerequisite. No human policy choice or new implementation seam is
needed to use this design.

TestDesign validation at committed `561d3591a` (this final validation note changes
documentation only): real `PlanTableImporter.ImportFile(..., isWindows: false)`
and `ManifestValidator.Validate` accepted the table; source census found exactly
the fifteen literal, non-parameterized methods with no prefix over-selection.

```text
IMPORT CP-1 executions=12 minutes=6 timeout=18 expectTokens=12
IMPORT CP-2 executions=2 minutes=4 timeout=15 expectTokens=2
IMPORT CP-3 executions=1 minutes=8 timeout=24 expectTokens=1
IMPORT rows=3 builds=3 min=15 minutes=18 warnings=0 validation=ok
PC C# syntax=ok; full method compile and red/green remain Mutation work
```

Validation used one explicitly scoped tool bootstrap, not an Antiphon.Tests
build: `pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1040-testdesign-import
-- dotnet build tools/Antiphon.Checkpoints
--property:OutputPath=bin-c1040-design-tool/ --property:UseAppHost=false --nologo`.
It completed with zero errors and one CS8602 warning in `TaskOwnerGuard.cs:170`;
build wall was 5.05 seconds, lease held 6 seconds, waited=0s. The CLI `import`
also accepted all three rows. An initial scratch validation adapter attempted to
load the tool's Roslyn assembly into PowerShell's already-loaded Roslyn context
and was rejected before its checks; the corrected adapter uses PowerShell's
existing parser. The final mutated C# text has zero syntax errors.

A separate slot-gated, private-root bash check executed the exact PC PATH setup
and extracted production `c1008_recycle` function, substituting only its
`require_lane`/`write_result` boundaries. It completed with the expected child
exit 2 and these lines:

```text
C1040_PC_JQ_ABSENT
SEAM accepted=false diagnosis=RecycleToolsMissing exit=2
SEAM syntax/lookup/refusal valid; this is not a TUnit PC result
```

That check proves the sealed PATH and real guard are reachable, not the TUnit
assertion, deployment, or an executed PC cycle. TUnit executions in TestDesign=0;
Code V/R and Mutation baseline/red/green remain pending their own stages. No
runtime source, tests, fixtures, production PATH or live image was modified.
All validation children exited; the single task-owned tool output directory is
removed before handoff. The entire landed fix-design prefix is byte-for-byte
unchanged.
