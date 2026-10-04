# CARD-1025: provision and check jq on the deployment host

Date: 2026-10-04. Plan task: `cffd614e`.
Inspected source: `56772d0961cc464704e249b6dc34f7d20653d01b`.
Card: Antiphon `66b75498-78a5-4e97-a699-0ba4fe8db651`.

## Outcome and stage boundary

Make outer-host jq an explicit rollout prerequisite. Add an idempotent,
verified provisioning phase and a read-only preflight to the existing rollout
front door. Ordinary rollout phases never install software implicitly. Preserve
the last-line `RecycleToolsMissing` refusal in `c1008_recycle`.

Complexity is **medium**: this changes a privileged host operation and the
ordering of a PowerShell-to-SSH-to-bash boundary, with existing offline fixtures
whose traces must remain meaningful. TestDesign is a separate stage. This is
the fix design and bounded test roster, not an executable verification manifest.
No unresolved product choice prevents TestDesign; decisions D-1 through D-7
implement the brief's request to choose the smallest sanctioned fix.

Publish this plan promptly through the owning task's normal landing operation
after settlement. The runner branch contract permits pushes only to the task
branch; do not directly push master or rebase the pushed branch. The caller
orders `scripts/delegate.ps1 -Land cffd614e -ExpectedSourceSha <full-plan-tip>`
and verifies its structured publication before dispatching TestDesign.

## Ground truth

Coordinates refer to the inspected source above. Live CARD-1025 and the entire
landed [CARD-1040 plan and appended TestDesign](2026-10-04-card-1040-jq-prerequisites-and-unit-timing-plan.md)
were read. Historical test counts below are card evidence, not runs by this Plan.

| What the card assumes | What the source/evidence does | Design consequence |
|---|---|---|
| Recycling needs jq on the host. | `scripts/c590-remote.sh:4008` calls `require_lane host`; line 4016 checks `command -v jq` before Compose/status proof, journals and removal. Absence exits 2 with `RecycleToolsMissing`. | The premise is correct. Provision the outer SSH host, preserving the refusal. |
| The image jq pin does not satisfy that host PATH. | `docker/session-runner-grok/Dockerfile:101` installs a checksum-verified static jq 1.7.1 inside the image. `c590-real.ps1:396` copies the remote script to the host and executes host bash. | Keep host and image receipts separate. Reuse the reviewed artifact pin, not the image activation workflow. |
| The documented prerequisite is missing. | `docs/docker-stack.md:563` describes only image jq; the staged-rollout preparation and recycling sections give no host jq check or provision command. | Update those two sections with the new explicit phases and receipt requirement. |
| Only the destructive function encounters missing jq. | `deploy-server2.ps1:115` also runs host jq in `Assert-NoIncompleteRecycle`; its nonzero SSH exit becomes `RecycleReceiptUnavailable`. The same-SHA path calls it at line 480. | Run the new preflight before entering a phase, including same-SHA retries, preview and resume. |
| The current rollout already checks this before handoff. | `Invoke-Phase` starts with runner status logic. `deploy-temp` can POST a retirement clear/hold and seed caches before any host jq admission. | Check before the first phase body and repeat on independently invoked phases, before any POST/seed/stop/removal. |
| Missing jq is a harmless test skip. | CARD-1025 records 34 passed/1 failed/19 skipped without host-fixture jq and 54/54 after adding it; the failing `C849_Deploy_prepares_and_verifies_before_acceptance` expected `RunnerBusy` but got the earlier prerequisite refusal. | Do not weaken busy/refusal assertions or add skips. Test prerequisites and behavioral failures are distinct. |
| CARD-1040 can close this card. | CARD-1040 owns durable runner-image qualification, fifteen existing consumer methods and timing accounting. Its activation gate explicitly depends on this card's outer-host receipt. | This card owns host provision/preflight/runbook only; no image edits, rollout rebuild, fifteen-method replay or Unit timing project. |
| A current host install is known to be necessary. | The card records historical host absence. This Plan runs inside a runner worktree and has not probed the outer host. Live runner catalogue reads prove neither its PATH nor jq provenance. | First run the read-only host check; an already working installation is a successful no-op provision. Do not claim current absence or activation. |

## Decisions

- **D-1 — Extend the existing rollout entry point.** Add named phases
  `check-host-jq` and `provision-host-jq` to `scripts/deploy-server2.ps1`, backed
  by one small `scripts/server2-host-jq.sh` helper. Reuse the rollout's existing
  SSH destination and noninteractive transport; do not add a fleet-routing
  setting. Rejected: a second deployment framework, new service/API, or a
  separate manually maintained install recipe.
- **D-2 — Installation is explicit and idempotent.** Check mode observes only.
  Provision mode returns successfully without downloading or elevating when
  the same deployment-shell jq already works. If absent, install the existing
  CARD-0927 static Linux x64 artifact at `/usr/local/bin/jq`, root:root 0755,
  after SHA-256 verification. Rejected: hand installation, arbitrary/latest
  downloads, an assumed distro/package repository, apt upgrades, replacing a
  present but broken/shadowed binary, or changing global PATH. This targets the
  demonstrated absence without creating a general host package manager.
- **D-3 — Check the actual consumer environment.** Resolve and execute jq in
  the non-login SSH bash environment used by the rollout, not a container,
  root's sudo PATH, interactive profile or the desktop. Require a working
  version command and JSON predicate smoke. Existing functional jq need not
  match the image pin; record its actual version/hash and resolved owner/mode.
  A newly installed artifact must additionally match the literal pinned
  hash/version and root:root 0755, and resolve through that same host PATH.
  Rejected: treating `command -v` or an installer exit alone as qualification.
- **D-4 — Fail early, retain the final guard.** Every existing rollout phase
  performs the read-only preflight before its body, including `all`'s individual
  phases, same-SHA shortcuts, `-DryRun` and `-ResumeRecycle`. No install is added
  to `all`. The direct remote recycle guard stays intact for callers that
  bypass the wrapper or lose jq after preflight. Rejected: moving/removing
  `RecycleToolsMissing`, preferring `RunnerBusy` without a prerequisite check,
  or continuing after SSH/probe/receipt failure.
- **D-5 — Use standing rollout authority for activation.** A human or the
  orchestrator runs provision from the reviewed, landed canonical checkout
  under the [existing autonomy policy](../../orchestration-loop.md#orchestrator-operational-autonomy-restart-rollout).
  The Plan/Code delegate does not install on the shared host or restart itself.
  Provision touches only its staged artifact and jq destination; it does not
  stop containers, recycle volumes, change routing, or handle provider secrets.
  Rejected: a delegate's ad hoc sudo command or widening host sudoers to get it
  through. Unavailable privilege is a reported operational prerequisite.
- **D-6 — Keep both work and proof bounded.** Use two separately commissioned
  Code slices, each 30–60 minutes including ordinary verification. One positive
  control per independently bypassable new behavior/guard, with an exact
  method filter per cycle; no whole-Unit, namespace, class or image suite.
  TestDesign freezes the inventory and costs before Code. Rejected: cloning
  CARD-1008's mutation matrix or CARD-1040's consumer qualification.
- **D-7 — Preserve neighboring ownership.** Avoid editing `c590-remote.sh`,
  the Dockerfile, image probe, the two existing large C1008 test classes or
  their required-jq policy. New tests live in their own file and may reuse
  assembly-internal fixtures. CARD-1030 owns Windows/WSL fixture repairs and
  CARD-1040 owns image activation. Changes to the shared fake verifier must
  retain its existing case traces and defaults.

## Host helper and rollout contract

The new bash helper accepts only `check` or `provision`. Its production
destination and release URL/hash are fixed, not operator-controlled install
paths. Stream the reviewed helper over the existing SSH transport with argument
tokens and stdin; do not re-enter the large C590 bootstrap, whose `ensure_dirs`
also changes ownership and setup state. Use the same non-login shell/PATH as
`c590-remote.sh`. Read-only host-lane identification follows its existing
host/daemon/container distinction; a nested or unobservable lane refuses.

The artifact is `jq-linux-amd64`, release `jq-1.7.1`, from
`https://github.com/jqlang/jq/releases/download/jq-1.7.1/jq-linux-amd64`, with
SHA-256 `5942c9b0934e510ee61eb3e30273f1b3fe2590df93933a93d7c58b81d19c8ff5`.
This pin is copied from inspected repository source, not newly qualified here.
Do not alter the existing image pin or claim host installation qualifies an image.

Check resolves jq, requires successful `jq --version`, and executes a small
literal `jq -en --argjson` predicate using the recycle's JSON operations
(`type`, `all`, `any`, object field comparison). Assert both true/exit-0 and
false/exit-1 cases, so a binary returning success unconditionally cannot pass.
Report missing as `HostJqMissing`, nonfunctional as `HostJqInvalid`, unknown
transport/lane as a separate unavailable/refusal diagnosis. Output never
includes raw environment, Docker inspect, HTTP headers or download credentials.

Provision first performs that same check. Missing is the only state authorizing
installation. Before downloading require Linux x86_64, noninteractive elevation,
the required transfer/hash/install utilities, and a safe, absent destination;
an existing nonfunctional file, dangling link or directory at the destination
refuses. No uninstallation or automatic replacement is in scope. Acquire a
host-local provision lock and recheck after acquiring it so concurrent calls
cannot both decide to install. Use a private temporary download; verify the
literal digest before executing it. Publish from a root-owned staged file in
the destination filesystem with mode 0755 and an atomic no-clobber operation;
do not expose a partial binary or overwrite a destination created by a racer.
Remove only the invocation's staging files on failure/exit.

After publication clear shell command hashing and rerun check in the deployment
user's original environment; additionally verify exact installed digest,
`jq-1.7.1`, owner/mode and resolved destination. A present executable elsewhere
on PATH that prevented installation is reported as existing, never as the
pinned provision. A PATH that cannot resolve the new destination fails and
asks for host configuration diagnosis; do not silently prepend PATH or reinstall.
No success receipt on download, digest, publish, final probe or receipt failure.

The PowerShell wrapper adds one helper invocation function, validates the
explicit provisioning invocation is from a clean canonical checkout whose
HEAD equals `-Sha`, and has no worktree override. Preserve existing operator
token handling without transmitting that token to the helper. Await and reap
the SSH child with a bounded timeout, collecting stdout/stderr without leaking
request objects. Exact deadlines and fixture seams are frozen by TestDesign.
The read-only check never invokes sudo, curl, install or any Docker mutation.

Both explicit phases exit after the helper: no fall-through into deployment.
The automatic check runs at the start of each existing `Invoke-Phase` body,
before its status/POST/host-case logic and before `Assert-NoIncompleteRecycle`.
Write a fresh local receipt under the existing printed
`.antiphon/rolling-server2/<run-id>/` evidence directory. Bind it to source SHA,
run ID, selected phase, observation time, host lane and SSH exit; retain mode,
resolved jq path, version/hash, owner/mode, semantic result, and installed/no-op
outcome. Unknown/malformed or unpersistable proof is not success. Do not cache
one phase's proof for later invocations.

## Operational commands after implementation and land

These are **new proposed phases**, not commands supported at this Plan's base.
Run from the canonical desktop checkout at reviewed full `<landed-sha>`, after
the rollout owner's no-pending-land, clean-checkout and matching server-version
checks. They target the outer deployment host through the existing transport;
no agent-placement `-Runner` or `-Platform` argument belongs on these commands.

```powershell
pwsh -NoProfile -File scripts/deploy-server2.ps1 -Rolling -Sha <landed-sha> -Phase check-host-jq
```

If the host reports `HostJqMissing`, the human or orchestrator explicitly runs:

```powershell
pwsh -NoProfile -File scripts/deploy-server2.ps1 -Rolling -Sha <landed-sha> -Phase provision-host-jq
pwsh -NoProfile -File scripts/deploy-server2.ps1 -Rolling -Sha <landed-sha> -Phase check-host-jq
```

Run provision a second time for the initial activation qualification and require
`installed=false` with unchanged resolved binary hash. Retain both provision
receipts and the independent check receipt. A pre-existing functional binary
needs only the successful checks/no-op receipts, not forced replacement.
An invalid/unknown result stops; installing by hand is not the fallback.

Complete this prerequisite **before cache preparation and gate 1 deploy-temp**.
Then follow the existing named rolling phases and their independent gates.
Each phase now checks again before acting. Host qualification permits starting
the documented rollout; it does not prove its drain, publication, volume or
image-activation obligations. Give CARD-1040 the host receipt reference when it
commissions image activation. No host operation is executed by this Plan.

## Slices, files and tests

### S1 — Explicit host qualification and provision (45–60 minutes)

Add `scripts/server2-host-jq.sh`; extend `scripts/deploy-server2.ps1` with the
two explicit phases, SSH helper invocation, source admission and receipt.
Add `tests/Antiphon.Tests/Scripts/HostJqPrerequisiteScriptTests.cs` with native
bash/private-root executable boundary fakes and a PowerShell transport fixture.
Use the existing assembly `ParallelLimiter<ProcessSpawnLimit>` and await every
child. Production never receives a test install destination or untrusted pin.

Test healthy/missing/invalid lookup, side-effect-free check, install from verified
bytes, bad digest, nonzero/false-positive final probe, unsafe/pre-existing
destination, failed privilege/transport, wrong lane, repeated/concurrent provision
and canonical-source admission. Observe command traces, published bytes and
receipt contents, not source-string matching alone. All external effects are
private fakes; a real shared package/binary is never removed to simulate absence.
Commit/push before the ordinary S1 checkpoint; preserve real failure outcomes.

### S2 — Rollout admission and operator instructions (30–60 minutes)

Wire the automatic check into existing phase entry in
`scripts/deploy-server2.ps1`; extend `scripts/fixtures/c727-fake-verify.ps1` with
an explicit prerequisite branch and `kind=prerequisite` trace (not `kind=case`).
Default healthy proof preserves existing fixture behavior and literal host-case
rosters. Extend the new test file, reusing `C1008WrapperFixture` from
`RollingVolumeRecycleScriptTests.cs` where suitable without editing its owner.
Missing proof must stop before any POST, seed, deploy, stop or removal for each
phase; healthy proof appears first. Cover same-SHA, preview, resume and `all`
without any implicit install. Explicit check/provision must not deploy.

Update `docs/docker-stack.md` preparation and recycling subsections with the
host/image distinction, exact commands, receipt/stop rules, and CARD-1040 link.
Do not rewrite unrelated historical cleanup guidance. Commit/push the complete
slice before its ordinary checkpoint. Run the full-range evidence-diff checker.
Any required shared-fixture repair beyond the new prerequisite branch returns
to TestDesign for a revised scoped row rather than silently widening tests.

## TestDesign handoff and bounded checkpoint candidates

Read the new-design seams against `C1008WrapperFixture`, `C1008HostFixture`,
`c727-fake-verify.ps1`, the rollout driver's two jq consumers, and the nearest
shell/process helpers before freezing executable tests. Append the standard
`## Verification design` including inspection, V/R, guard inventory, PCs,
delivery inventory, exact checkpoint manifest and numeric cost. No new async
session delivery is proposed; the synchronous SSH result and persisted local
receipt must be observed. Fake SSH cannot prove host activation or privilege.

The following are proposed ordinary selections, not runnable manifest rows.
The new method names are intended names to be fixed by TestDesign. Use a literal
method roster and explicit argument-expanded counts; do not select the whole
new class just because it initially contains only this card's methods.

| Candidate | Lane | Slice / files and intended selection | Decisive result | Estimated minutes including isolated build |
|---|---|---|---|---:|
| CP-1 | Native Linux, private bash/SSH fixtures | S1; new `HostJqPrerequisiteScriptTests`: `C1025_Check_is_read_only`, `C1025_Provision_is_verified_and_idempotent`, `C1025_Provision_refuses_unsafe_inputs`, `C1025_Source_and_transport_admission` | Missing/invalid refuses; valid install publishes only verified bytes; second call has zero install effects; failures have no success receipt or foreign mutation | 6 |
| CP-2 | Native Linux, private PowerShell rollout fixtures | S2; same new file: `C1025_Preflight_precedes_rollout_effects`, `C1025_Explicit_tool_phases_never_deploy` | All phase/shortcut variants stop on prerequisite failure before any rollout effect; healthy proof precedes effects; no implicit install | 5 |
| CP-3 | Native Linux, existing fixture consumer with working jq | S2; exact `/*/*/RollingVolumeRecycleScriptTests/C1008_Legacy_rolling_and_jq_rosters_remain` | One pass, zero skips; all four existing 31-assertion driver receipts and required present-mode coverage preserved | 8 |

Existing `RemoteScriptContractTests.C849_Deploy_prepares_and_verifies_before_acceptance`
explains the card's symptom, but its host implementation is unchanged here.
CARD-1040 already freezes that method and the other fourteen consumers; do not
repeat them to claim this host fix. CP-3 is retained because S2 edits the exact
shared fake verifier its legacy driver consumes, so it has a direct regression
reason independent of CARD-1040. TestDesign may narrow it further only with
evidence that every affected driver path remains covered.

Freeze one method-scoped PC per independent new guard/behavior, including
read-only admission, explicit installation/host/source admission, digest-before-
publish, no-clobber/idempotence, post-install qualification, rollout preflight
ordering and receipt failure propagation. Split independently bypassable guards
instead of counting several as one control; do not mutate unchanged C1008 or
image guards. Each proposed test must name its production defect and decisive
assertion. Wrong setup/build failure, zero tests or skipped proof is not red.
All PC cycles belong to post-land SourceLanding Mutation, never live-host
experiments or Code-stage mutation runs.

Planning estimate: ordinary rows total 19 minutes, about 2 minutes tool setup,
and authoring split across the two Code budgets above. Mutation cost is separate
and must be made numeric after TestDesign freezes the guard census. No execution
or wall-time saving is claimed from unmeasured tests. No whole Unit run, broader
class filter, timeout increase, retry policy or host budget change is authorized.

Use the checkpoint tool once per committed slice group and wait to terminal;
every build/test driver uses the host build-slot gate. Use distinct forward-slash
`bin-c1025-*/` outputs, clean committed source and preserved unedited CHECKPOINT
lines. Keep TRX/JSON/logs ignored; remove owned outputs after children exit.
An inherited failure must be confirmed with its exact method at the committed
base in an equivalent environment. Code and Review run
`scripts/check-evidence-diff.ps1` over their full task ranges.

## Placement and acceptance

Plan read `GET /api/runner-defaults` and `GET /api/session-runners` through the
configured task API at approximately 18:31 UTC. Defaults revision was 2; both
Linux and Windows descriptors were eligible and a third descriptor unavailable.
This is placement evidence only. Re-read both routes before dispatch; embed no
fleet address/occupancy in task commands. Omit `-Runner` unless explicitly
qualifying one host, and omit `-Platform` for document work. The native bash
fixture checkpoints justify `-Platform Linux`; `-Platform Any` unpins a prior
OS restriction. Live activation uses the existing canonical rollout lane,
separate from whichever runner executes Code.

Plan completion means this artifact is committed/pushed and ready for prompt
landing. Implementation completion additionally requires ordinary Code/Review
proof and publication. Operational completion requires the actual outer-host
check/provision/no-op receipts before the first staged recycle; source landing,
test fakes and a runner-image jq receipt do not close that obligation. Preserve
the separately commissioned Mutation obligation and CARD-1040 coordination.

--- next stage ---
next: test-design
handoff: Freeze narrow native-Linux host-jq provision/preflight tests, exact checkpoint counts and one method-scoped PC per independent new guard. Preserve C1008 refusal and legacy driver rosters; budget separate 30–60 minute Code slices. Host activation remains caller-owned and separate from CARD-1040 image qualification.
artifact: docs/superpowers/plans/2026-10-04-card-1025-host-jq-prerequisite-plan.md


## Verification design

TestDesign task: `f2306cc6`, based on landed plan/source
`36d19595f81e4241ec2d4932c5e6829148b0e582`. This section appends verification;
the fix design, D-1 through D-7 and the two Code slices above are unchanged.
The candidate checkpoint names/counts above are superseded by the executable
roster and manifest here. This stage runs no provisioning, rollout, builds,
ordinary tests or mutations.

### Inspection

| Bodies read | Boundaries -> verification or exclusion |
|---|---|
| Entire landed CARD-1025 plan; CARD-1040 appended verification's inspection, activation, checkpoint and cost sections | Host proof is separate from image qualification. Working existing jq need not have the image pin -> V-1/V-2; no image replay or image change. |
| Entire `scripts/deploy-server2.ps1`, especially `Invoke-Phase`, `Assert-NoIncompleteRecycle`, `Invoke-RunnerRequest`, `Invoke-HostCase`, `Assert-TempProjectAbsent`, option validation and final dispatch | Five phase entries; same-SHA discovery; preview/resume; explicit early exits; receipt write and child custody -> V-3/V-4, R-3/R-4. |
| Entire `scripts/fixtures/c727-fake-verify.ps1` and `c727-fake-http.ps1`; `test-deploy-server2-jq.ps1`; `test-deploy-server2.ps1` probe, child setup, trace selectors, T-1..T-24 assertions and frozen footer | `kind=case` roster excludes prerequisite traces; 66 versus 62 wrapper invocations; required present jq versus deliberate absent probes -> V-5/R-5. |
| `RollingVolumeRecycleScriptTests.C1008_Legacy_rolling_and_jq_rosters_remain`, `C1008Process`, `C1008_Same_sha_and_partial_retries_are_safe`, `C1008_Documentation_and_transport_pins_match`, `C1008_Refusal_receipts_do_not_leak_secrets`, `C1008_Retired_absent_null_is_accepted`; complete `C1008WrapperFixture` and `C1008HostFixture`; `c1008-recycle-cases.json` | Native process deadlines, trace/state ownership, null/offline statuses and source injection -> new fixture setup, R-3/R-5/R-6. Existing fixture owners remain unedited. |
| `RemoteScriptContractTests.C849_Deploy_prepares_and_verifies_before_acceptance`; `c590-remote.sh` lane functions and `c1008_recycle` admission; `c590-real.ps1` host copy/SSH invocation | The historical failure is an earlier prerequisite refusal; retain actual `RecycleToolsMissing` and downstream busy diagnosis -> R-6. CARD-1040 owns the unchanged fifteen-consumer qualification. |
| Complete `Scripts/C590Harness.cs` and `Scripts/ScriptHarness.cs` | Nearest new-file process helpers inspected. Neither is suitable unchanged: one lacks bounded concurrent drains and the other does not reap on timeout. New fixture owns and awaits children explicitly -> R-3. |
| `docs/testing-and-build.md` process isolation, jq receipts, checkpoint schema/tool, slots, evidence and PC rules; `PlanTableImporter.ImportMarkdown`, `ManifestValidator.Validate`; orchestration role, landing and autonomy owner sections | Nine-column manifest, escaped OR, result floors, isolated outputs, native Linux and exact-method PC cycles -> Checkpoints/Cost. No whole-Unit selection. |
| `docs/docker-stack.md` preparation and recycling sections; project-context conventions | S2 documentation review checks host-before-cache ordering, explicit check/provision/no-op commands, refusal rules and CARD-1040 handoff. No rewrite of cleanup or deployment authority. |

**Missing setup to implement in Code, not assumed existing.** The helper, new
test class, private-root fixture, SSH boundary and prerequisite fixture branch
do not exist at this source. The following freezes their test seams without
adding a production test destination, alternate URL/hash, or privilege bypass:

- Put all new methods and their private fixture in
  `tests/Antiphon.Tests/Scripts/HostJqPrerequisiteScriptTests.cs`. Every process
  method uses the assembly's `ParallelLimiter<ProcessSpawnLimit>`. All nineteen
  methods are non-parameterized `[Test]` methods: vectors below are internal
  assertions, not additional TUnit executions. Use real bash, pwsh, Git,
  filesystem operations and native `flock`; require native Linux, working jq,
  node, curl, sha256sum and coreutils. Missing setup fails with a named
  prerequisite; do not skip. The known runner tools were located during this
  inspection, but their runtime qualification was not executed.
- Execute the production helper in a test-owned copy. Redirect only its fixed
  filesystem literals (destination, provision-lock/staging root and
  `/.dockerenv`) to the fixture root; assert the exact replacement inventory
  before launching. Shortened timeout constants are also allowed in this copy.
  Do not replace guard conditions, predicate programs, digest/version literals
  or dispatch logic. Supplemental source assertions pin the unchanged URL,
  digest/version and ASCII transport. Do not add production environment or CLI
  overrides for paths, pins, lane or admission.
- Native PATH absence is a private allowlist of needed executable links
  excluding every jq; never rename/remove the runner's jq. A present native jq
  validates the real `type`/`all`/`any`/field-comparison predicate. Invalid jq
  executables implement one fault each and emit command traces. The download
  success fixture supplies a private executable; a boundary hash fake returns
  the production pinned hash only for that fixture's expected bytes and records
  its input. Bad-digest vectors include real sha256sum over altered bytes.
  The pin itself is never substituted. Fake curl records the literal release
  URL and does no network I/O.
  Freeze the provision-tool admission vectors to `curl`, `sha256sum`,
  `install`, `mktemp`, `flock`, `ln`, `stat`, `readlink`, `rm` and `uname`;
  sudo admission is separate. Native bash builtins need no PATH fixture.
  Each missing-tool vector must reach the common availability loop with the
  other tools observable; require its named refusal before transfer. Extra
  implementation utilities require corresponding availability vectors.
- Intercept privilege, metadata and host/daemon observations at executable
  boundaries, with all effects contained under the private root. Stage mode,
  complete-byte publication, inode preservation and `flock` use real local
  filesystem behavior. The sudo fake records `-n`, owner/group and install
  arguments; metadata fakes model root ownership where the test user cannot
  chown. A copy/publish barrier permits deterministic racer and partial-reader
  observations. A cleanup assertion reads independent sentinels before fixture
  disposal; cleanup of the fixture itself cannot satisfy the assertion.
- Use actual private Git repos and a registered linked worktree for source
  admission. Copy the reviewed script/helper/token reader into the private
  repo, commit its fixture baseline, then create each fault. This tests the
  main-checkout shape and HEAD/status admission, not whether a checkout has
  been reviewed or published remotely. Those remain caller-owned gates.
- New wrapper fixture subprocesses use a private SSH executable to record argv
  and stdin and run the helper copy through non-login bash. At least one
  healthy check and one private install must traverse wrapper -> SSH child ->
  real helper logic -> parsed proof -> persisted receipt, read back by the
  test. Synthetic JSON cases test parser faults only. Do not claim their output
  proves helper execution.
- Production deadlines: SSH connect 15 seconds; entire check child 30 seconds;
  entire provision child 180 seconds; provision lock wait 60 seconds; download
  connect 15 seconds and total 90 seconds. Drain stdout/stderr concurrently.
  On timeout, kill the owned local process tree, await exit and drain/dispose;
  bound post-kill pipe collection to 5 seconds and refuse if custody is unknown.
  Fixture copies use a 100 ms observation deadline, a child that completes
  after 500 ms, and a 10-second outer test watchdog. This lets the timeout
  mutant finish normally and fail a result assertion, instead of counting a
  harness timeout as red. PID/start identity and an independent child handle
  establish reaping. The fixture finally reaps survivors after making its
  assertion. Existing C1008 30-second and legacy 12-minute limits are unchanged.
- Add only the prerequisite branch to the shared fake verifier, before normal
  host-case dispatch. It accepts the wrapper's request manifest, returns healthy
  proof by default and writes `kind=prerequisite`, never `kind=case`. Give the
  new fixture state a bounded response sequence to fail the nth check. That
  branch alone consumes this sequence; existing scenario/default/state behavior
  and case counts do not change. Sequence state is test data, not a production
  preflight cache.
- Reuse `C1008WrapperFixture` for same-SHA/retire/resume refusal vectors. Its
  `scenario=c1008` rejects most POSTs, so it cannot prove a healthy full rollout.
  The new private wrapper fixture uses the generic C727 state shape from
  `Run-C727` for accepted deploy/drain/all cases, with the existing HTTP fake.
  Do not silently repair the shared HTTP fake or the large C1008 classes.

**Receipt oracle.** Read the final file from the wrapper's printed run
directory; stdout and a child exit are insufficient. Require schema 1,
typed successful helper facts, `lane=host`, absolute resolved jq path, nonempty
version, 64-hex digest, numeric uid/gid, mode, true/false semantic exits 0/1,
mode check/provision, installed boolean and consistent existing/installed
outcome. Wrapper-owned fields bind `sourceSha`, `runId`, `selectedPhase`,
executing `phase`, `observedAtUtc` and `sshExit=0`. In `all`, selectedPhase is
all and phase is the individual entry. Local observation time lies between the
test's invocation and completion timestamps. A check cannot claim installed;
an installed receipt additionally requires destination/pin/version/root:root/
0755. Invalid or unpersistable proof never creates a successful receipt/banner.
Receipt filenames distinguish phase observations within the same run.
Use `host-jq-<phase>.json`, where phase is the validated executing phase
(including the two explicit phase names). The fault fixture can block that
exact filename after the wrapper creates its run directory, before SSH returns.

**Literal method roster and boundary combinations.**

| ID | Exact method in `HostJqPrerequisiteScriptTests` | Slice | Required behavior and decisive observations |
|---|---|---|---|
| M-1 | `C1025_Check_qualifies_deployment_shell` | S1 | Check a working native jq, a non-pinned working jq, PATH absence, non-executable/shadowed jq, version failure/empty output, true-predicate failure, and a jq that returns 0 for the false predicate. Compare resolved path, version, digest, uid/gid/mode and both semantic exits with independently observed fixture facts. The deployment PATH wins over a healthy sudo PATH; do not alter PATH. |
| M-2 | `C1025_Check_has_no_install_effects` | S1 | For healthy, absent and invalid jq, both direct helper check and explicit wrapper check have zero sudo/download/install/publish/unlink/Docker-write calls; compare all private filesystem sentinels before/after. Wrapper evidence creation is allowed and separately inspected. |
| M-3 | `C1025_Provision_requires_missing_jq` | S1 | Healthy existing jq at the destination and elsewhere on PATH returns installed=false with unchanged inode/bytes and no elevation/download. Missing installs once; a second call is a no-op. Broken executable, non-executable file and shadowed invalid jq refuse without replacement. |
| M-4 | `C1025_Provision_admits_host_prerequisites` | S1 | Invoke unsupported/empty/extra mode arguments; host/nested/sibling/no-daemon/failed-hostname lanes; Linux/non-Linux crossed with x86_64/non-x86_64; each required utility unavailable; sudo -n denied. All other prerequisites stay healthy in each single-fault vector. Check mode still works without provision-only tools or sudo. |
| M-5 | `C1025_Provision_refuses_unsafe_destination` | S1 | Absent destination is the accepted control. Existing regular broken file, symlink to file, dangling symlink, directory, FIFO, symlinked parent and unwritable/uninspectable parent refuse. Use lstat-style observations, assert zero download/publish, and compare foreign sentinels. |
| M-6 | `C1025_Provision_serializes_and_rechecks` | S1 | Hold the native flock barrier: no download may start until release; lock refusal cannot continue. Two owned helper processes see missing before the barrier, then the first publishes; the second rechecks and returns no-op, with exactly one download/publication. Also place an invalid destination while the second waits; it must refuse. Always await both children. |
| M-7 | `C1025_Provision_verifies_download_before_use` | S1 | Successful boundary download, transport nonzero with complete-looking bytes, truncated bytes, wrong digest, digest-tool failure, and staged-byte tamper. Assert the fixed URL/hash, private download permissions, hash-before-execute/publish ordering, and zero execution/publication on every rejected artifact. |
| M-8 | `C1025_Provision_publishes_complete_no_clobber` | S1 | Use native same-filesystem publication in the private root and a reader barrier: readers see absent or complete bytes, never a partial executable. Capture root-owned 0755 staging before publish. Place an unrelated file after the absence check; atomic no-clobber failure must preserve its inode/hash. Inject stage/publish errors and require refusal. |
| M-9 | `C1025_Provision_cleans_only_owned_staging` | S1 | At download, digest, staging, publish and final-check failures, require this invocation's temporary files removed and a neighboring invocation's files/lock/destination untouched. SIGTERM before publication exercises EXIT/signal cleanup; SIGKILL may leave private residue, which is retained, never swept by name. A fresh call after either cut can acquire the lock and recheck. |
| M-10 | `C1025_Provision_requalifies_published_jq` | S1 | A full private install yields installed=true and a complete receipt. Independently corrupt final resolved path (including a stale command hash), version, digest, owner, group, mode, true predicate and false predicate. Test a deployment PATH lacking /usr/local/bin. Each fault refuses after publication, emits no success proof, leaves the published file for diagnosis and never retries/replaces it. |
| M-11 | `C1025_Provision_requires_canonical_source` | S1 | Create actual private Git repositories: clean main checkout/HEAD match accepts; linked worktree, Git observation failure, tracked staged/unstaged dirt, nonignored untracked file, and clean mismatching HEAD refuse before SSH. Ignored evidence does not count as dirt. Read-only check remains usable from a worktree. No real canonical checkout is altered. |
| M-12 | `C1025_Transport_is_bounded_and_reaped` | S1 | Start actual child processes for fake SSH: stream helper stdin and argument tokens, return valid proof with exit 0/nonzero, empty/truncated output, fail process start, and delayed valid output beyond the deadline. Observe recorded PID/start identity and complete pipe drains after normal/failure/timeout paths. Tokenize BatchMode=yes, ConnectTimeout=15 and non-login bash; never fall back to a login shell. |
| M-13 | `C1025_Receipt_requires_complete_current_proof` | S1 | Feed valid helper proof and one-field corruptions: malformed/truncated JSON, duplicate document, unsupported schema, omitted/wrong-type required fields, non-host lane, unsuccessful predicates, invalid installed/outcome combination. Independently read the persisted receipt and assert current SHA/run/selected phase/executing phase/mode, an observation timestamp within the invocation, child exit and observed jq facts. |
| M-14 | `C1025_Receipt_persistence_is_required` | S1 | Healthy helper plus an unpersistable receipt path must return refusal, with no success banner. Use a directory at the exact final filename or an exclusive writer fault, not chmod alone (the fixture may run as root). Remove the obstruction and run a fresh invocation: a new complete receipt is required. For S2 the same fault must prevent every phase-body effect. |
| M-15 | `C1025_Transport_preserves_secret_custody` | S1 | Use distinctive fake operator/task/download/remote-stderr sentinels. Inspect child argv, streamed stdin, public stdout/stderr and all receipts: no token forwarding or raw error/request/environment dump. Fixed diagnoses remain visible. SSH launch/read/parse failures do not expose the sentinels. |
| M-16 | `C1025_Explicit_tool_phases_never_deploy` | S1 | Run check-host-jq and provision-host-jq with healthy/no-op/install/refusal outcomes. Success exits immediately after qualified persisted proof; no HTTP status/POST, host case, cache seed, drain, stop or recycle follows. Check sends check, provision sends provision exactly once; unsupported helper mode refuses. |
| M-17 | `C1025_Preflight_precedes_every_phase` | S2 | Run the phase/option/fault matrix below through the real wrapper. A valid persisted prerequisite is the first phase activity; missing/invalid/transport/proof/persistence failure returns refusal before any status/POST/census/discovery/case activity. Healthy prerequisites preserve downstream busy and incomplete-recycle refusals. |
| M-18 | `C1025_Preflight_is_fresh_for_each_entry` | S2 | A healthy all run produces five independently observed/persisted check receipts in phase order, zero provision calls. Fail each of the five checks in turn, including healthy first/failed second: earlier legitimate effects may remain, but the refused phase has no body effects and later phases do not run. Repeat standalone same-SHA, preview and resume invocations with success then missing; no earlier receipt grants admission. |
| M-19 | `C1025_Final_recycle_guard_remains` | S2 | Reuse C1008HostFixture without editing it: override only jq lookup to absent at the command boundary, call direct deploy-parent and retire-temp-runner with valid context for normal/dry-run/resume. All six calls return RecycleToolsMissing before compose/status/journal/removal; Removed is empty and sentinels remain. This pins the unchanged last-line guard, not host activation. |

M-17's admission matrix is **eleven valid entry shapes**: five standalone
ordinary phases; same-SHA `deploy-temp` and `redeploy-old`; and both `-DryRun`
and `-ResumeRecycle` for each of `redeploy-old`/`retire-temp`. Cross all eleven
with healthy, missing, invalid, SSH-failed, malformed-proof and
receipt-write-failed outcomes (66 internal vectors). For healthy rows configure
a reachable real downstream effect or named downstream refusal, not merely a
zero exit. Check the prerequisite precedes even same-SHA recycle discovery.
Additionally cross missing and healthy prerequisites with busy counters for
deploy/redeploy/retire and with an incomplete same-SHA journal: missing wins
early; healthy preserves `RunnerBusy`/`RecycleResumeRequired`. Bind-invalid
preview+resume and preview/resume on unrelated phases remain the existing
`RecycleContextInvalid` parameter refusal, before phase entry; they grant no
body admission and do not need preflight.

M-18 runs one healthy all plus five fail-at-position cases, then success/failure
pairs for independently invoked shortcuts. Existing effects before a later
refusal are not expected to roll back. Require no effects from the refused
entry onward and no implicit provision anywhere.

The remaining vectors vary one boundary at a time with accepted controls.
Multi-fault permutations that can only encounter the same earlier refusal are
excluded; the explicit matrices above cover the meaningful precedence
combinations. Each PC's decisive assertion has its literal label from the PC
table, so a secondary failure cannot be mislabeled as the intended detection.

### Delivery inventory

No session-input, message queue, Hangfire, event notification or other new
asynchronous recipient-delivery path is introduced. Therefore real-queue busy/
already-eligible recipients and enqueue recovery are inapplicable; no request,
event, Sent flag or ack is offered as session-delivery evidence. If Code adds
such a path it returns to Plan/TestDesign for a real producer-to-recipient test
and a matching complete UserPrompt transcript before handoff.

The changed synchronous handoffs still require end-to-end observations:

| Producer -> destination | Identity/persistence | Recovery and observable receipt |
|---|---|---|
| Explicit wrapper/phase entry -> owned SSH child -> non-login host helper | Source SHA + run ID + selected/executing phase + mode; streamed reviewed helper and bounded child result | M-12/M-15 observe argv/stdin/exit/drained streams. M-13 reads the corresponding complete local receipt. Nonzero/lost output cannot succeed (G-36..G-44). Local child death does not prove the remote command stopped. |
| Helper -> private download -> root-owned same-filesystem stage -> `/usr/local/bin/jq` | Invocation-owned staging/lock plus immutable artifact digest; atomic publication is the durable filesystem boundary | M-6..M-10 cut before download, after download, after verified staging, before publish and after publish. Before publication retry may install; after publication retry must freshly qualify existing bytes and no-op or refuse. A success bit alone is insufficient: read destination bytes/metadata, run both predicates, then read receipt (G-16..G-32). |
| Qualified helper result -> wrapper receipt -> admitted phase body | Fresh receipt in `.antiphon/rolling-server2/<run-id>/`, matching the full identity above | M-14 cuts after helper success/before persistence; M-17 cuts before body entry. No receipt or a failed write refuses. A fresh invocation reacquires proof; it does not reuse an earlier file (G-39..G-50). |

Crash coverage uses owned child termination at deterministic barriers. A hard
kill can leave private staging or a completed install without its caller
receipt; retain uncertain residue and retry via check/provision, never infer
completion or remove another invocation's files. Lock release is observed by a
fresh process. The no-op recovery cannot retrospectively manufacture the lost
receipt or claim that the earlier attempt did not install.

Substitutes are explicit: fake SSH proves tokenization, streaming, local child
custody and result-to-file handling, not real connectivity, remote process death
or noninteractive sudo policy. Fake sudo/stat/hash success proves ordering and
refusal decisions, not real root ownership or downloaded release authenticity.
Native private-filesystem tests prove their local atomicity/no-clobber behavior,
not outer-host activation. Generic C727 HTTP/case fakes prove admission order,
not actual drainage, Docker removal or cache/image health.

Operational acceptance remains the Plan's caller-owned canonical-host sequence:
check; if missing, explicit provision; check again; second provision returns
installed=false and unchanged hash. Retain real host path/version/digest/owner/
mode/semantic receipts bound to the landed SHA and phases. For already-working
jq retain check/no-op receipts without replacement. This is the recipient-side
host/filesystem evidence required to close activation. Code may finish source
work using substitutes; neither Code nor this design claims host activation or
CARD-1040 image qualification from those tests.

### Proves it works now

- V-1: Actual deployment-shell qualification and read-only diagnosis work |
  native bash plus wrapper | CP-1, M-1/M-2/M-4 | healthy accepts; missing/invalid/
  wrong-lane refuses accurately; check has zero installation effects.
- V-2: Missing jq can be provisioned safely and repeated without changes |
  private native filesystem/process integration | CP-1, M-3/M-5..M-10 |
  verified complete publication, pinned final proof, unchanged second invocation,
  serialization and recoverable interruption.
- V-3: Explicit front door respects source, transport and evidence boundaries |
  real pwsh/child/Git/file boundaries | CP-1 and affected S2 replay in CP-2,
  M-11..M-16 | current persisted receipt permits success; every boundary failure
  refuses and explicit phases exit without deployment.
- V-4: Every valid rollout entry checks before acting and gets fresh proof |
  wrapper integration | CP-2, M-17/M-18 | eleven-shape matrix and five-position
  all sequence preserve effect order and produce zero implicit installs.
- V-5: The modified shared verifier preserves its actual legacy consumers |
  native Linux existing TUnit-to-PowerShell drivers | CP-3,
  `RollingVolumeRecycleScriptTests.C1008_Legacy_rolling_and_jq_rosters_remain` |
  one passed TUnit execution, four 31-assertion receipts, required present-mode
  T-20 executes.

### Guards the regression

- R-1: Lookup/version alone cannot qualify a broken or constant-success jq |
  M-1/M-2/M-4 | both predicate outputs/exits and the original deployment PATH
  are observed; absent/invalid/wrong-lane has no successful proof.
- R-2: Explicit installation cannot overwrite unrelated state or succeed on
  unverified/partially published bytes | M-3/M-5..M-10 | digest-before-use,
  racer inode/hash preservation, absent-or-complete reader samples,
  invocation-only cleanup and final installed facts.
- R-3: Bad source, child failure, missing proof or receipt persistence cannot
  turn into success | M-11..M-16 | refusal plus zero downstream effects;
  completed child/streams; exact current receipt identity; no secret sentinels.
- R-4: Missing jq is discovered before the first rollout effect, including
  shortcut/retry/preview/resume and later all phases | M-17/M-18 |
  failed entry has no status/discovery/POST/case activity; healthy prerequisite
  preserves existing busy/incomplete-journal diagnoses.
- R-5: A prerequisite trace cannot be mistaken for a legacy host case or weaken
  required jq | existing exact CP-3 method | four literal
  `C973_JQ case=<mode> assertions=31 failures=0` receipts; present has no
  `C973_JQ_SKIPPED`; present footer remains 24 groups/66 invocations/227
  assertions, each deliberate optional absence mode remains 23/62/218.
  These nested harness notices are distinct from TUnit skips, which must be zero.
- R-6: Direct remote recycling still refuses if jq is lost or the wrapper is
  bypassed | M-19 | all six direct normal/preview/resume calls report
  `RecycleToolsMissing`, zero removed volumes and unchanged sentinels.

### Guard inventory

The census covers every independently bypassable safety guard/assertion added
or changed by this plan, including the final installed-file checks separately
from staging checks. Generic loops (required utilities; structured proof
validation) get all their input vectors in one method and one control for the
common guard. Identity fields emitted separately have separate controls.
Existing unmodified C1008 drain/census/journal/volume guards and image guards
remain owned by their prior plans; D-7 explicitly excludes remutating them.
R-5/R-6 protect this change's interaction with them.

- G-1: D-1, helper contract — Only check/provision is executable; unsupported arguments refuse. | PC-1
- G-2: D-3, host-lane contract — Host lane must be positively observed. | PC-2
- G-3: D-3 — Qualification resolves the original non-login deployment PATH. | PC-3
- G-4: D-3 — Existing jq version command must succeed with a nonempty version. | PC-4
- G-5: D-3 — True JSON predicate must return true/exit 0. | PC-5
- G-6: D-3 — False JSON predicate must return false/exit 1. | PC-6
- G-7: D-2/D-4 — Check never performs provisioning effects. | PC-7
- G-8: D-2 — A working existing jq makes provision an unchanged no-op. | PC-8
- G-9: D-2 — Only missing, never invalid jq authorizes install. | PC-9
- G-10: Provision admission — Installation requires Linux. | PC-10
- G-11: Provision admission — Installation requires x86_64. | PC-11
- G-12: Provision admission — Every required provision utility is available before transfer. | PC-12
- G-13: Provision admission — Noninteractive privilege succeeds before transfer. | PC-13
- G-14: D-2, destination contract — Destination is absent, including no dangling link. | PC-14
- G-15: Destination contract — Destination parent is safe and observable. | PC-15
- G-16: Provision lock — Acquired lock excludes concurrent install work. | PC-16
- G-17: Provision lock — Recheck after lock acquisition prevents duplicate install. | PC-17
- G-18: Private download — Download staging is private. | PC-18
- G-19: Download contract — Failed transfer cannot supply an accepted artifact. | PC-19
- G-20: Pinned digest contract — Pinned digest is verified before executing or publishing staged bytes. | PC-20
- G-21: Root staging — Published staging is root:root before visibility. | PC-21
- G-22: Root staging — Published staging has mode 0755 before visibility. | PC-22
- G-23: Atomic publication — Readers never observe a partially written destination. | PC-23
- G-24: No-clobber publication — A destination created by a racer is never overwritten. | PC-24
- G-25: Cleanup contract — Cleanup deletes only this invocation's staging. | PC-25
- G-26: Cleanup contract — Ordinary failure and handled exit remove owned staging. | PC-26
- G-27: D-3, final qualification — Final lookup clears command hashing and resolves the installed destination in original PATH. | PC-27
- G-28: Final qualification — Installed bytes match the literal pinned digest. | PC-28
- G-29: Final qualification — A newly installed artifact is exactly jq-1.7.1. | PC-29
- G-30: Final qualification — Installed uid/gid are root:root. | PC-30
- G-31: Final qualification — Installed mode is exactly 0755. | PC-31
- G-32: Final qualification — Published jq is semantically rechecked, not credited from staging. | PC-32
- G-33: Wrapper source admission — Explicit provision runs only from a canonical, observable main checkout. | PC-33
- G-34: Wrapper source admission — Provision source is clean. | PC-34
- G-35: Wrapper source admission — Provision HEAD equals requested full SHA. | PC-35
- G-36: SSH result — Nonzero SSH exit never grants success. | PC-36
- G-37: SSH deadline — A bounded timeout refuses late output. | PC-37
- G-38: SSH custody — Owned local child is reaped and streams drained before return. | PC-38
- G-39: Receipt validation — Only complete, typed, successful host proof is accepted. | PC-39
- G-40: Receipt identity — Receipt source SHA is this invocation's source. | PC-40
- G-41: Receipt identity — Receipt run ID is this invocation's run. | PC-41
- G-42: Receipt identity — Receipt identifies selected and currently executing phase. | PC-42
- G-43: Receipt identity — Receipt mode identifies check versus provision. | PC-43
- G-44: Receipt freshness — Observation time belongs to the current invocation. | PC-44
- G-45: Receipt persistence — Failed local persistence cannot authorize a phase or print success. | PC-45
- G-46: Credential transport — Operator/task token is not sent to the helper. | PC-46
- G-47: Diagnostic custody — Failures expose bounded diagnoses, not raw remote/request secrets. | PC-47
- G-48: Explicit phase dispatch — Explicit tool phases terminate without rollout fall-through. | PC-48
- G-49: D-4, phase entry — Preflight succeeds and persists before any phase body. | PC-49
- G-50: D-4, fresh observations — Every entry, including all/shortcuts, gets a new check. | PC-50

### Positive controls

Each row is one compiling production defect, one guard and one exact method.
All methods below are in `HostJqPrerequisiteScriptTests`; the complete execution
filter is exactly `/*/*/HostJqPrerequisiteScriptTests/<method>` with the literal
method cell substituted, no wildcard or OR. These are proposed executable
controls against the implementation to be authored, not claims of existing test
bodies or executed mutations. A bash defect must still pass `bash -n`; a
PowerShell defect must still parse; C# tests must still build.

| PC | Guard | Break by this compiling defect | Exact method | Required red assertion label/value |
|---|---|---|---|---|
| PC-1 | G-1 | Accept an unknown mode as provision. | `C1025_Provision_admits_host_prerequisites` | `mode-refused: exit=2; install calls=0` |
| PC-2 | G-2 | Replace host-lane admission with true. | `C1025_Provision_admits_host_prerequisites` | `lane-refused: non-host install calls=0; success receipts=0` |
| PC-3 | G-3 | Resolve jq from the healthy sudo-path fake instead of deployment PATH. | `C1025_Check_qualifies_deployment_shell` | `deployment-path: resolved path equals deployment-shell path; absent reports HostJqMissing` |
| PC-4 | G-4 | Ignore the version exit/empty result. | `C1025_Check_qualifies_deployment_shell` | `version-refused: HostJqInvalid; success receipts=0` |
| PC-5 | G-5 | Replace the true-predicate check with success. | `C1025_Check_qualifies_deployment_shell` | `true-probe-refused: HostJqInvalid` |
| PC-6 | G-6 | Accept exit 0 for the false predicate. | `C1025_Check_qualifies_deployment_shell` | `false-probe-refused: constant-success jq reports HostJqInvalid` |
| PC-7 | G-7 | Route missing check through the provision branch. | `C1025_Check_has_no_install_effects` | `check-read-only: sudo/download/install/publish/unlink calls=0` |
| PC-8 | G-8 | Perform one download before returning the existing-jq no-op. | `C1025_Provision_requires_missing_jq` | `existing-no-op: download calls=0; inode/hash unchanged` |
| PC-9 | G-9 | Treat HostJqInvalid as HostJqMissing. | `C1025_Provision_requires_missing_jq` | `invalid-not-missing: download/publish calls=0` |
| PC-10 | G-10 | Bypass the OS comparison. | `C1025_Provision_admits_host_prerequisites` | `os-refused: non-Linux download calls=0` |
| PC-11 | G-11 | Bypass the architecture comparison. | `C1025_Provision_admits_host_prerequisites` | `arch-refused: non-x86_64 download calls=0` |
| PC-12 | G-12 | Remove the required-utility admission loop. | `C1025_Provision_admits_host_prerequisites` | `tools-refused: named prerequisite refusal before any transfer attempt` |
| PC-13 | G-13 | Skip the sudo -n admission probe. | `C1025_Provision_admits_host_prerequisites` | `privilege-refused: download calls=0` |
| PC-14 | G-14 | Remove the lstat absence refusal. | `C1025_Provision_refuses_unsafe_destination` | `destination-refused: download calls=0; foreign inode/hash unchanged` |
| PC-15 | G-15 | Skip the parent symlink/writability/identity validation. | `C1025_Provision_refuses_unsafe_destination` | `parent-refused: no download/stage/publish outside private approved parent` |
| PC-16 | G-16 | Replace flock admission with a no-op. | `C1025_Provision_serializes_and_rechecks` | `lock-held: download-start has not occurred before barrier release` |
| PC-17 | G-17 | Remove the post-lock jq/destination recheck. | `C1025_Provision_serializes_and_rechecks` | `lock-recheck: total download calls=1; second outcome=no-op` |
| PC-18 | G-18 | Create the private temporary directory mode 0755 instead of 0700. | `C1025_Provision_verifies_download_before_use` | `private-download: observed parent mode=0700 and downloaded file inaccessible to other users` |
| PC-19 | G-19 | Ignore curl's nonzero exit after it writes complete-looking bytes. | `C1025_Provision_verifies_download_before_use` | `transfer-refused: execute/publish calls=0` |
| PC-20 | G-20 | Replace the digest comparison with success. | `C1025_Provision_verifies_download_before_use` | `digest-before-use: bad-digest execute/publish calls=0` |
| PC-21 | G-21 | Omit root owner/group assignment to the destination-filesystem stage. | `C1025_Provision_publishes_complete_no_clobber` | `stage-owner: publication observer sees uid=0,gid=0` |
| PC-22 | G-22 | Stage with mode 0777. | `C1025_Provision_publishes_complete_no_clobber` | `stage-mode: publication observer sees 0755` |
| PC-23 | G-23 | Replace atomic publication with non-atomic `cp` from stage to destination; the destination-copy boundary fake writes two chunks separated by the reader barrier. | `C1025_Provision_publishes_complete_no_clobber` | `atomic-visible: every reader sample is absent or complete verified bytes` |
| PC-24 | G-24 | Replace no-clobber publish with force-replacing rename. | `C1025_Provision_publishes_complete_no_clobber` | `no-clobber: racer inode/hash unchanged` |
| PC-25 | G-25 | Expand cleanup to the neighboring invocation's specifically named sentinel. | `C1025_Provision_cleans_only_owned_staging` | `cleanup-custody: foreign sentinel still exists with original bytes` |
| PC-26 | G-26 | Disable the owned-staging EXIT cleanup. | `C1025_Provision_cleans_only_owned_staging` | `cleanup-owned: owned stage paths absent after digest failure` |
| PC-27 | G-27 | Accept any working jq path after publish without the destination equality check. | `C1025_Provision_requalifies_published_jq` | `final-path: wrong/stale resolved path refuses success` |
| PC-28 | G-28 | Skip the final installed-digest comparison. | `C1025_Provision_requalifies_published_jq` | `final-digest: corrupted installed digest has success receipts=0` |
| PC-29 | G-29 | Skip the exact installed-version comparison. | `C1025_Provision_requalifies_published_jq` | `final-version: functional wrong version has success receipts=0` |
| PC-30 | G-30 | Skip the final owner/group comparison. | `C1025_Provision_requalifies_published_jq` | `final-owner: wrong uid or gid has success receipts=0` |
| PC-31 | G-31 | Skip the final mode comparison. | `C1025_Provision_requalifies_published_jq` | `final-mode: wrong installed mode has success receipts=0` |
| PC-32 | G-32 | Reuse prepublication semantic success instead of invoking final check. | `C1025_Provision_requalifies_published_jq` | `final-semantics: changed true/false result has success receipts=0` |
| PC-33 | G-33 | Skip the common-directory/worktree admission comparison. | `C1025_Provision_requires_canonical_source` | `canonical-source: linked worktree SSH calls=0` |
| PC-34 | G-34 | Skip the porcelain-status admission. | `C1025_Provision_requires_canonical_source` | `clean-source: staged/unstaged/untracked dirty checkout SSH calls=0` |
| PC-35 | G-35 | Skip HEAD equality admission. | `C1025_Provision_requires_canonical_source` | `source-sha: clean wrong HEAD SSH calls=0` |
| PC-36 | G-36 | Ignore SSH ExitCode when proof looks healthy. | `C1025_Transport_is_bounded_and_reaped` | `ssh-exit: healthy JSON plus exit 255 returns refusal` |
| PC-37 | G-37 | Replace the timed wait with an ordinary wait for the finite delayed fake. | `C1025_Transport_is_bounded_and_reaped` | `ssh-deadline: late healthy output returns timeout refusal, not success` |
| PC-38 | G-38 | Remove timeout kill/wait/drain, retaining disposal only. | `C1025_Transport_is_bounded_and_reaped` | `ssh-reaped: recorded child has exited and both pipes reached EOF when wrapper returns` |
| PC-39 | G-39 | Skip the helper-proof schema/required-fields/semantic validation call. | `C1025_Receipt_requires_complete_current_proof` | `proof-shape: malformed/unsuccessful helper proof produces no success receipt` |
| PC-40 | G-40 | Write a different valid 40-hex sourceSha. | `C1025_Receipt_requires_complete_current_proof` | `receipt-sha: persisted sourceSha equals requested SHA` |
| PC-41 | G-41 | Write a different valid c727 runId. | `C1025_Receipt_requires_complete_current_proof` | `receipt-run: persisted runId equals printed evidence-directory run ID` |
| PC-42 | G-42 | Write deploy-temp as the executing phase for a redeploy-old request. | `C1025_Receipt_requires_complete_current_proof` | `receipt-phase: persisted selectedPhase/phase equal actual invocation/entry` |
| PC-43 | G-43 | Write check on a provision receipt. | `C1025_Receipt_requires_complete_current_proof` | `receipt-mode: provision receipt mode=provision` |
| PC-44 | G-44 | Write a timestamp one day before invocation. | `C1025_Receipt_requires_complete_current_proof` | `receipt-time: start <= observedAtUtc <= completion` |
| PC-45 | G-45 | Swallow the receipt write failure and return successful helper status. | `C1025_Receipt_persistence_is_required` | `receipt-required: blocked filename yields refusal and no success banner/body effects` |
| PC-46 | G-46 | Append the fixture operator-token sentinel value to the SSH command arguments. | `C1025_Transport_preserves_secret_custody` | `token-not-forwarded: argv/stdin contain no operator or task sentinel` |
| PC-47 | G-47 | Append captured raw SSH stderr to the public failure message. | `C1025_Transport_preserves_secret_custody` | `diagnostic-custody: public output/receipts contain no remote-secret sentinel` |
| PC-48 | G-48 | After a successful explicit check invoke deploy-temp before returning. | `C1025_Explicit_tool_phases_never_deploy` | `explicit-only: HTTP/case/POST count=0` |
| PC-49 | G-49 | Move preflight after the first status call in Invoke-Phase. | `C1025_Preflight_precedes_every_phase` | `preflight-first: failed prerequisite has phase-body trace count=0` |
| PC-50 | G-50 | Cache the first successful check and skip subsequent phase checks. | `C1025_Preflight_is_fresh_for_each_entry` | `fresh-preflight: fail-second all has exactly two check attempts and no drain-old body effects` |

Mutation first runs each of the eighteen distinct detecting methods green on
the exact landed source, method-scoped, then for each PC:
break -> isolated build -> exact method red at the named assertion -> restore
all touched files -> fresh isolated build -> same method green. Use
`-MinExecuted 1`, require exactly one execution and zero skips each time.
A build/fixture failure, zero tests, unrelated failure or equivalent mutation
is not red. Never change the expected result to make the PC fail. Fixture
source copies must be made from the current mutated source so they exercise
the defect. Mutation may need a syntactically equivalent edit at Code's chosen
statement; it may not silently substitute a different guard or weaker assertion.

Keep all PC effects inside private fixture roots, including the deliberate
overbroad-cleanup and force-overwrite mutants. Native writer/reader and lock
barriers must reach their expected setup markers before assertions. For PC-38
the observer asserts survivor/EOF state before its finally cleanup; for PC-37
the finite delayed child avoids a harness timeout. For PC-20 and PC-28 the
bad-byte vector returns a wrong digest independently of the mutant; never
mutate the hash fake into agreeing with invalid bytes.

All fifty cycles are sequential: most mutate the same helper or wrapper, so
none qualifies for independent same-tree mutation batching. Reports preserve
each PC's exact defect, assertion, counts, tested source, restoration and
owned output inventory under the SourceLanding external evidence root.
No commits/pushes or external executor from that snapshot. Code runs only
ordinary V/R; ordinary Review judges these tests/controls before implementation
land; the separately commissioned post-land Mutation stage executes them.
Pending PCs are not a reason to withhold ordinary Review or relabel source as
mutation-clean.

### Out of scope

- Installing on the outer host, restart/rollout, live privilege/network
  qualification and CARD-1040 image activation during Code/Mutation. They are
  explicit caller-owned operational acceptance, not optional substitutes.
- Any image/Dockerfile/probe change, whole Unit/namespace/class run, the
  unchanged fifteen-consumer replay, or a new timing project. CP-3 is retained
  because this card changes its shared verifier dependency.
- Windows/WSL fixture qualification (CARD-1030), architecture variants other
  than refusing non-x86_64, package-manager installation and replacement of a
  present broken jq. The supported implementation is native Linux x64.
- Remutating unchanged C1008/image guards. The inherited final missing-tools
  guard gets a narrow interaction regression, R-6, without duplicating those
  owners' PC batteries.
- Full permutations of unrelated simultaneous failures, or live kills to
  manufacture absent jq. Single-fault controls, the enumerated precedence
  matrix and deterministic private-child interruption cuts cover the relevant
  boundaries.
- Additional automated tests for the runbook's prose. Code/Review inspect its
  exact commands, both modified subsections, receipt/refusal rules and
  host/image distinction against the executable phases. This is a document
  review obligation, not an unlisted build/test run.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1025-helper/` | linux-host-jq-explicit | `/*/*/HostJqPrerequisiteScriptTests/(C1025_Check_qualifies_deployment_shell*)\|(C1025_Check_has_no_install_effects*)\|(C1025_Provision_requires_missing_jq*)\|(C1025_Provision_admits_host_prerequisites*)\|(C1025_Provision_refuses_unsafe_destination*)\|(C1025_Provision_serializes_and_rechecks*)\|(C1025_Provision_verifies_download_before_use*)\|(C1025_Provision_publishes_complete_no_clobber*)\|(C1025_Provision_cleans_only_owned_staging*)\|(C1025_Provision_requalifies_published_jq*)\|(C1025_Provision_requires_canonical_source*)\|(C1025_Transport_is_bounded_and_reaped*)\|(C1025_Receipt_requires_complete_current_proof*)\|(C1025_Receipt_persistence_is_required*)\|(C1025_Transport_preserves_secret_custody*)\|(C1025_Explicit_tool_phases_never_deploy*)\|(C1025_Check_rejects_canonical_leaf_symlink*)\|(C1025_Receipt_rejects_canonical_lookup_with_unapproved_target*)` | V-1, V-2, V-3, V-6, R-1, R-2, R-3, R-7 | exactly M-1..M-16 plus the two canonical-leaf repair methods: 18 passed, 0 failed/skipped, no extra methods | 18 | 8 |
| CP-2 | S2 | `tests/Antiphon.Tests -> bin-c1025-preflight/` | linux-host-jq-admission | `/*/*/HostJqPrerequisiteScriptTests/(C1025_Provision_requires_canonical_source*)\|(C1025_Transport_is_bounded_and_reaped*)\|(C1025_Receipt_requires_complete_current_proof*)\|(C1025_Receipt_persistence_is_required*)\|(C1025_Transport_preserves_secret_custody*)\|(C1025_Explicit_tool_phases_never_deploy*)\|(C1025_Preflight_precedes_every_phase*)\|(C1025_Preflight_is_fresh_for_each_entry*)\|(C1025_Final_recycle_guard_remains*)` | V-3, V-4, R-3, R-4, R-6 | exactly M-11..M-19: 9 passed, 0 failed/skipped, no extra methods | 9 | 7 |
| CP-3 | S2 | `tests/Antiphon.Tests -> bin-c1025-legacy/` | linux-host-jq-legacy | `/*/*/RollingVolumeRecycleScriptTests/C1008_Legacy_rolling_and_jq_rosters_remain` | V-5, R-5 | exactly 1 passed, 0 failed/skipped; four 31-assertion receipts; required present T-20 | 1 | 8 |

Three isolated builds, twenty unique methods, **26 TUnit executions**:
16 in S1, nine in S2 (six affected wrapper methods replayed after its S2 edit
plus three new methods), and the one existing legacy method. All new methods
have one result; none uses `[Arguments]`. Internal loops, 66 admission vectors
and legacy 31-assertion receipts do not increase Min. Method-prefix stars in
the combined filter accommodate pinned discovery; require the resulting TRX
roster to equal the literal method lists. The importer enforces floors/roster
tokens, not all prose in Expect; Code/Review must check exact counts and nested
receipts. The union is the entire ordinary executable scope.

Commit/push each S1/S2 slice before its group, then run the checkpoint tool once
per group with serial execution. Bootstrap the tool once using the host slot:

```powershell
pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1025-checkpoint-bootstrap -- dotnet build tools/Antiphon.Checkpoints --property:OutputPath=bin-c1025-tool/ --property:UseAppHost=false --nologo
$c1025Source = (git rev-parse HEAD).Trim()
dotnet tools/Antiphon.Checkpoints/bin-c1025-tool/Antiphon.Checkpoints.dll run --plan docs/superpowers/plans/2026-10-04-card-1025-host-jq-prerequisite-plan.md --after S1 --serial --expected-source-sha $c1025Source --max-wait 50s
```

For the separately committed S2 group, refresh `$c1025Source` from its HEAD and
use the identical command with `--after S2`; that selects CP-2 and CP-3.
The checkpoint tool gates its own builds/rows; do not wrap that DLL command in
a second slot. On exit 75 call `wait` for the emitted run ID with
`--max-wait 50s` until terminal, never settle while it runs. Do not edit source
under a run. Exit 4 is a slot timeout, not permission for an unleased retry.
Derived row deadlines are 24/21/24 minutes, not new timeout overrides.
No `knownFlaky`, repetitions or timeout increases are commissioned.

Use fresh clean-source/build-bound receipts, retain unedited CHECKPOINT lines
and actual expanded names/counts, and report reruns against their real SHA.
A failed inherited method is confirmed at this committed base with the exact
method in an equivalent jq/tool environment, never by rerunning the assembly.
Any fixture change beyond the named prerequisite branch needs revised
TestDesign before widening scope. Code and Review run the full-range
`scripts/check-evidence-diff.ps1`; that read-only policy check and the named
bootstrap are declared setup, not extra test selections. Inventory and remove
only owned `bin-c1025-*` output directories after every child exits; retain raw
receipts/logs/TRX as ignored evidence.

### Cost

All numbers here are **estimated**, not measured during TestDesign.

- **Ordinary V/R floor (Code) = 23 minutes:** CP-1 exact sixteen-method helper/
  explicit filter 8; CP-2 exact nine-method wrapper/admission filter 7; CP-3
  exact `C1008_Legacy_rolling_and_jq_rosters_remain` 8. Includes three isolated
  builds at 2 minutes each and 17 minutes for test/start/teardown work.
- **Setup outside rows = 4 minutes:** gated checkpoint-tool bootstrap 2;
  same-shell tool/jq checks, fixture admission and evidence-policy check 2.
  No live host activation or fleet waiting is hidden in this allowance.
- **PC floor (Mutation) = 308 minutes:** exact-method landed baseline for
  eighteen unique detecting methods, one initial isolated build, 8 minutes;
  then **PC-1 through PC-50 each 6 minutes** = defect setup 0.5 + isolated
  red build/run 2.5 + restore 0.5 + fresh isolated green build/run 2.5.
  Total cycles 300 minutes. Each cycle's filter is its one literal method in
  the PC table; no broad baseline/class/suite run. Baseline and cycle counts
  are 18 + 50 + 50 = **118 method-scoped executions**, before justified reruns.
  This floor excludes mutation discovery/analysis/report authoring.
- **Total verification = 335 minutes = setup 4 + ordinary 23 + PC 308.**
  Code-side verification is 27 minutes across two slices; Mutation is a
  separate commission and is not squeezed into either Code slice.
  S1 budgets 45–60 minutes (12 verification/setup + 33–48 authoring);
  S2 budgets 30–60 minutes (15 verification + 15–45 authoring/document review).
  If authoring exceeds a slice, commit/push an honest checkpoint and continue
  the same bounded scope; never omit a guard or CP row to fit the estimate.
- **Claimed wall-clock savings = 0 minutes.** There is no measured same-scope
  comparison for these new tests. Exact filters avoid unrelated Unit and
  unchanged image/C1008 mutation work, but their unknown elapsed cost is not
  booked as a saving. The provisional ordinary 19-minute estimate grows by
  4 minutes to 23 to account for this concrete roster and the affected S2
  wrapper replay; the 308-minute PC floor is intentionally visible.

Handoff audit: selected existing test bodies and nearest fixtures/helpers read;
guards=50, mapped=50, missing=0, duplicate PC maps=0. All fifty controls name a
compiling defect, exact detecting method and decisive assertion; no untested
new guard is omitted. Nineteen new non-parameterized methods plus one existing
consumer are frozen. Setup to author is fully specified above; no unverifiable
seam or human product choice blocks Code. Ordinary floor=23, PC floor=308,
setup=4, total=335 minutes. Live host acceptance and post-land Mutation remain
explicit subsequent obligations, not claims of this documentation result.

--- next stage ---
next: code
handoff: Land this appended TestDesign, then commission S1 and S2 separately within 30–60 minutes each. Implement the private native-Linux seams and 19 methods; run only CP-1 after S1 and CP-2/CP-3 after S2. Ordinary V/R is 23 minutes; 50 exact-method PCs cost 308 minutes post-land. Keep host activation caller-owned and separate from CARD-1040.
artifact: docs/superpowers/plans/2026-10-04-card-1025-host-jq-prerequisite-plan.md

### Code refinement: CARD-1054 canonical host path

Caller input `2a4f19bd-f3c6-450f-b56a-654a36e853ee`, applied by original Code
owner `f92f723a-8e78-4d69-93dc-2fb0e97604e2`, supersedes D-2/D-3 and M-1/M-3's
permission to qualify an unrelated working jq elsewhere on PATH. Accept only
when the actual non-login shell's `command -v jq` is `/usr/local/bin/jq`, or its
resolved path is that file. A separate user-home executable ahead on PATH
refuses even when its bytes, pinned digest, version and predicates are healthy.
A symlink resolving to the canonical file remains an accepted existing no-op.
Do not change PATH, replace the shadow, install through a shadow or qualify the
runner image. The earlier source/evidence remains historical, not evidence of
this narrower contract.

Record `lookupPath` (the literal lookup result) alongside `path` (the resolved
path) in the schema-1 qualified proof and persisted wrapper receipt. A refused
shadow produces a separate, typed `host-jq-<phase>-refused.json` observation with
`qualified=false`, reason `HostJqPathUnapproved`, both observed paths, SSH exit 2
and current source/run/phase/mode/time identity. It never produces the successful
phase receipt or success banner. Malformed refusal output is not retained as
an observation; raw stderr is never copied. Failure to persist the observation
reports `HostJqReceiptUnavailable`, still refusing phase admission.

This adds V-6/R-7 to the existing CP-1 selection, without adding test methods or
broadening its literal filter. M-1 independently qualifies the private native
home copy, then observes direct and wrapper check/provision refusal, unchanged
bytes, zero install effects and the persisted found-path observation. It also
observes direct and wrapper acceptance of a symlink to the canonical file and
requires the alias actually found to survive into the receipt. M-3 replaces the
old unrelated-existing-path no-op with refusal. M-13 tests a typed healthy
synthetic proof with both paths unapproved, required `lookupPath` typing, and
malformed/extra/duplicate refusal documents. Each synthetic success-shape fault
keeps the receiving fixture's canonical path healthy, so a path mismatch cannot
mask its intended parser fault. M-14 blocks the exact refused-observation
filename with a directory, then requires a fresh later observation. These are
native private boundaries, not live host activation.

- V-6: Canonical host jq or its resolving alias qualifies, and found/resolved
  paths are retained; a healthy private home copy refuses check and provision
  without installation or qualification | CP-1, M-1/M-3/M-13/M-14.
- R-7: PATH shadowing or a forged successful proof cannot qualify an unrelated
  binary; malformed or unpersistable diagnostics cannot become a successful
  receipt | CP-1, M-1/M-3/M-13/M-14.

All original PC-1..PC-50 remain pending. Mutation must assess overlap introduced
by this narrower rule (especially PC-3/PC-8/PC-27), not credit an equivalent or
masked mutant. Extend the inventory with G-51 canonical helper-path admission,
G-52 successful wrapper-proof path admission, G-53 found-path identity, G-54
refusal diagnostic shape/custody, G-55 refused found/resolved path identity and
G-56 refused observation qualification flag. The helper/wrapper emitters of
G-53 and the found/resolved fields of G-55 have separate variants. The separate
refusal emitter/persistence path also adds variants of G-40..G-45. Each remains
pending for exact-method SourceLanding Mutation; Code executes no deliberate
mutants.

| PC | Guard / variant | Compiling defect | Exact method in HostJqPrerequisiteScriptTests | Required red assertion |
|---|---|---|---|---|
| PC-51 | G-51 | Remove the helper canonical lookup/resolution comparison. | `C1025_Check_qualifies_deployment_shell` | `canonical-host-path`: healthy home shadow must exit 2, not qualify. |
| PC-52 | G-52 | Remove the successful wrapper-proof canonical path comparison. | `C1025_Receipt_requires_complete_current_proof` | `proof-shape`: typed healthy home-path proof must refuse with no success receipt. |
| PC-53 | G-53 helper | Emit resolved path as lookupPath. | `C1025_Check_qualifies_deployment_shell` | `found-path-recorded`: alias lookup equals the actual alias found. |
| PC-53-wrapper | G-53 wrapper | Overwrite receipt.lookupPath with the canonical destination before writing. | `C1025_Check_qualifies_deployment_shell` | `found-path-recorded`: persisted alias equals the actual lookup. |
| PC-54 | G-54 | Remove refusal required-name/count/duplicate admission while retaining field-value checks. | `C1025_Receipt_requires_complete_current_proof` | `refusal-proof-shape`: extra/duplicate documents leave no refusal observation. |
| PC-55 | G-55 lookup | Write a different absolute lookupPath in the refusal observation. | `C1025_Check_qualifies_deployment_shell` | `refusal-found-path`: persisted lookup equals the private home path. |
| PC-55-resolved | G-55 resolved | Write a different absolute resolved path in the refusal observation. | `C1025_Check_qualifies_deployment_shell` | `refusal-resolved-path`: persisted resolved path equals the observed home binary. |
| PC-56 | G-56 | Write qualified=true in the refusal observation. | `C1025_Check_qualifies_deployment_shell` | `refusal-observation`: qualification remains false. |
| PC-40-refusal | G-40 refusal | Write a different valid source SHA in the refusal observation. | `C1025_Check_qualifies_deployment_shell` | `refusal-source-sha`: equals requested committed fixture SHA. |
| PC-41-refusal | G-41 refusal | Write a different valid run ID in the refusal observation. | `C1025_Check_qualifies_deployment_shell` | `refusal-run-id`: equals the actual observation-directory run ID. |
| PC-42-refusal | G-42 refusal | Write deploy-temp as the refusal observation phase. | `C1025_Check_qualifies_deployment_shell` | `refusal-phase`: selected/executing phase equals actual explicit invocation. |
| PC-43-refusal | G-43 refusal | Write check on the provision refusal observation. | `C1025_Check_qualifies_deployment_shell` | `refusal-mode`: provision invocation records provision. |
| PC-44-refusal | G-44 refusal | Write a timestamp one day before invocation. | `C1025_Check_qualifies_deployment_shell` | `refusal-time`: observation falls within invocation bounds. |
| PC-45-refusal | G-45 refusal | Swallow refusal-observation persistence failure and continue with HostJqPathUnapproved. | `C1025_Receipt_persistence_is_required` | `refusal-receipt-required`: blocked observation reports HostJqReceiptUnavailable. |

The fourteen added cycles have the same six-minute sequential method-scoped
budget as the original controls: 84 additional minutes, 64 total cycles,
PC floor 392 minutes (8-minute baseline plus 384-minute cycles), and 146
method-scoped executions (18 baseline + 64 red + 64 restored green). No cycle
is discharged by ordinary green. The amended ordinary floor is 31 minutes:
original 23 plus one eight-minute refined S1 CP-1 selection. Setup is six
minutes, including the rebuilt checkpoint tool after earlier cleanup. Total
verification floor is 429 minutes, before justified repairs; no wall-clock
saving is claimed. The existing row deadline/filter/count remains unchanged.

This is a materially changed proof selection commissioned by the refinement:
healthy home-shadow, accepted canonical-alias, found-path receipt and refusal
observation cases are new. Run CP-1 once on the new committed SHA; no unchanged
selection repetition is commissioned. CP-2/CP-3 and automatic rollout entry
admission remain S2. Its healthy fake prerequisite proof must include lookupPath
and the approved canonical path under this amended contract. Outer-host
activation and CARD-1040 image qualification remain caller-owned after Review
and land; no shared host operation is executed by this Code amendment.

### Review repair: canonical leaf symlink (Code c8655038)

Review d3e917ce found that the canonical lookup string masked a resolved home
binary. Supersede the earlier lookup-or-resolution admission: the resolved path
must equal the canonical destination, whose leaf must be a non-symlink regular
file. An alias to that file remains valid. The wrapper independently rejects
success proofs with any other resolved path. No real host installation is in
scope. The original unsafe-destination symlink vector now keeps the destination
on PATH; the new executable-home vector proves qualification actually judges it.

CP-1 includes two new exact methods (18 executions total):
- `C1025_Check_rejects_canonical_leaf_symlink`: native check and provision
  must exit 2 with HostJqPathUnapproved, preserve the leaf/target, execute no
  unapproved jq and perform no install effects; real wrapper refusal observations
  retain canonical lookup and home resolution with qualified=false.
- `C1025_Receipt_rejects_canonical_lookup_with_unapproved_target`: an otherwise
valid synthetic success proof with canonical lookup and home resolution must
  independently refuse with HostJqProofInvalid for check and provision, retaining
  no successful receipt or banner. M-1 retains regular-file and alias controls.

V-6/R-7 include these two vectors. This repair brief overrides the generic Final
profile with CP-1 only; CP-2/CP-3, V-4/V-5, R-4/R-5/R-6, whole Unit and live
activation stay pending. A method-scoped baseline run of the two new tests
against unchanged defective production is an explicitly reported diagnostic,
not Mutation; CP-1 runs once after the repair. No repetitions after green.
Baseline build+tests estimate 8 minutes, repair CP-1 estimate 8 minutes.

All 64 existing PC cycles remain pending. Add three independently bypassable
variants for SourceLanding Mutation, also pending (67 total):

| PC | Guard / variant | Compiling defect | Exact method | Required red assertion |
|---|---|---|---|---|
| PC-51-leaf | G-51 canonical leaf | Restore lookup-equals-destination as an alternative to resolved-path/regular-leaf admission. | `C1025_Check_rejects_canonical_leaf_symlink` | canonical-leaf-symlink: exit 2 for working home target found at canonical leaf |
| PC-52-target | G-52 resolved proof | Restore lookup-equals-destination as an alternative to resolved proof equality. | `C1025_Receipt_rejects_canonical_lookup_with_unapproved_target` | canonical-proof-target: exit 2, no success receipt |

CP-1 at repair commit `72f7bba684713f5f10ee7721200f2aabe5e41383` ran
18 tests: 17 passed, one failed at the unchanged strong diagnostic assertion.
The helper now refused correctly, but the wrapper discarded its canonical-lookup
refusal observation as malformed. Refusal admission also judges the resolved
target, preserving the typed diagnostic and qualified=false observation. The
same CP-1 row is rerun after committing this fix (second and final repair round);
no assertion or deadline changes.

| PC | Guard / variant | Compiling defect | Exact method | Required red assertion |
|---|---|---|---|---|
| PC-54-leaf | G-54 canonical-lookup refusal | Reject a typed path refusal solely because lookupPath is canonical. | `C1025_Check_rejects_canonical_leaf_symlink` | wrapper emits HostJqPathUnapproved and persists canonical lookup/home resolution with qualified=false |
