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
