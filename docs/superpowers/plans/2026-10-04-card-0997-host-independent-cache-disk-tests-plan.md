# CARD-0997: Host-independent cache disk contract tests

Date: 2026-10-04. Stage: Plan; TestDesign remains a separate stage.
Inspected source: `bb18064ba647e0ddb03cae4da437ab60ed447d98`.
Card: Antiphon CARD-0997, read in full with
`pwsh -NoProfile -File scripts/card.ps1 get CARD-0997 -Board Antiphon`.

Make the three reported Unit tests supply their own free-space readings. Keep the
production shell gate at 20 GiB and the saved-donor importer at 20 GiB plus the
validated package and npm payload sizes. Exercise both real comparisons with
deterministic values below, at, and above their boundaries.

This dispatch changes only this plan. The implementation described below includes
one narrow production I/O seam; it does not implement that seam in this dispatch.
The card's premise is supported by source inspection. Its historical failures are
card evidence, not a new reproduction at the inspected SHA.

## Ground truth

Line references refer to the inspected source, not future edited files.

| Card assumption | What the code does | Consequence for the plan |
|---|---|---|
| Three Unit tests depend on free space. | `tests/Antiphon.Tests/Scripts/RemoteScriptContractTests.cs:18` marks the class Unit. The reported methods start at 2856, 2978 and 3265. Their child processes reach actual disk probes. | Keep all three tests and their existing assertions; repair their inputs. Do not skip on low disk or move them to Integration. |
| The deploy test already supplies adequate disk. | `C849_Deploy_temp_observes_inaccessible_docker_mountpoints_with_sudo` fakes `sudo df`, which serves the runner-state preflight. Its extracted real `c849_budget_gate` later calls bare `df`, which is not faked. | Install a shell-local `df` fake for the cache gate as well. Retain the sudo/path-access assertions and the separate runner-state preflight. |
| The shell threshold is near line 2206. | `scripts/c590-remote.sh:2210-2226`, `c849_budget_gate`, observes three volumes, enforces their size budgets, parses column 4 of `df -Pk` for DockerRootDir, refuses malformed input with `CacheDiskUnavailable`, and requires `free_kb >= 20971520`. | Use 1-KiB units for shell fixtures. Execute the extracted production function, including parsing, comparison and receipt; never replace the gate with a success stub. |
| A PowerShell `Get-PSDrive` fake might isolate the importer. | `scripts/c849-import-saved-donor.ps1:49-52` uses `([System.IO.DriveInfo]::new($Stage)).AvailableFreeSpace`, not `Get-PSDrive`. `Check-Space` runs after the validation pass and before the copy pass for both tar and directory sources. | Add an explicit callable seam at that one I/O read. A cmdlet mock or a `df` shim cannot affect this .NET call. |
| Saved-donor tests fake Docker, so they are isolated. | Their `docker()` functions directly invoke the real importer with `pwsh -File` at test lines 2893 and 3005. That child reads the test host's stage volume. The FIFO and size-bomb probes also invoke the importer directly at 3040 and 3099. | Route all importer invocations in these three saved-donor methods through one fixture wrapper, including cases that currently refuse before the space check. |
| Both thresholds mean simply 20 GiB. | The shell gate requires 20 GiB available, independently of its per-volume limits. The importer requires `20GB + $sizes.packages + $sizes.npm`, with sizes accumulated from accepted file lengths. PowerShell `GB` here is binary. | Preserve the different formulas and units. Boundary cases for the importer need nonzero package and npm bytes. |
| The real gate already has boundary coverage. | `C849_Prune_preview_is_read_only_and_bounded` at 3389 exercises `c849_headroom_state` with byte values below/at 20 GiB. It does not execute `c849_budget_gate` or importer `Check-Space`. | Retain that test and add behavioral coverage of both actual gates. A preview classifier test cannot substitute. |
| The historical missing-pack failure must always have one incidental diagnosis. | The card records `CacheDonorUnsafePath` instead of `AppHostDonorMissing`. Current code can also terminate at `Check-Space` before the missing-pack validator runs. No current failure output was measured here. | Preserve the existing `AppHostDonorMissing` assertion with controlled adequate disk. Do not rewrite it to match an unrelated earlier refusal. |
| Linux-only paths require a pinned machine. | `LinuxShell` at 4400 uses native bash or Windows WSL. `PrepareLinuxShellScript` translates the repository root; `RequireLinuxPwsh` and the C905 guard protect missing prerequisites. | Keep that transport and prerequisite behavior. A fixture path goes through the `repo` variable, never an embedded native checkout path. |

The relevant owners read for this plan are `docs/project-context.md`, the
checkpoint/fast-lane/build-slot sections of `docs/testing-and-build.md`, the
Plan/stage policy in `docs/orchestration-loop.md`, `docs/ops-http.md`, and the shared
cache contract in `docs/docker-stack.md`. The complete importer and the affected
test bodies and shell functions were inspected.

## Decisions

- **D-1: Fake measurements, retain policy.** The shell threshold, importer
  headroom formula, per-role budgets, validation-before-copy ordering, diagnoses,
  and refusal exits stay intact. Reject lowering the threshold, checking the host
  before running tests, freeing host disk as the fix, or accepting a disk refusal
  in a test intended to reach another guard. Those choices preserve the coupling
  or weaken the contract.
- **D-2: Use shell command resolution for `df`.** Add a small fixture-local
  function that accepts only the expected `-Pk` invocation and emits a fixed
  two-line POSIX table. It never calls the host `df` as a fallback. Supply values
  explicitly per child shell; keep the existing `sudo` shim for its other duties.
  No production shell change or environment-variable override is needed.
- **D-3: Add an explicit PowerShell probe parameter.** Add an optional
  `[scriptblock]$GetAvailableFreeBytes` parameter to the importer, defaulting to
  a block that takes the stage path and performs the existing `DriveInfo` read.
  `Check-Space` obtains its numeric reading by invoking that block with `$Stage`;
  keep the comparison in `Check-Space`. The production caller continues to omit
  the parameter. Reject a process-global environment override: it could leak
  into operational invocations or parallel tests. Reject rewriting the importer
  into a temporary test copy: a narrow callable seam lets tests execute the same
  tracked script and makes changes to its guards visible to them.
- **D-4: Keep fixture state in each child process.** A test-only PowerShell wrapper
  constructs the probe from its own numeric argument, binds captured values with
  `GetNewClosure()`, and invokes the real importer. No global C# environment
  mutation, static reading cache, additional library, or real Docker is needed.
  Ordinary saved-donor tests supply 64 GiB; boundary tests supply their exact
  case value. Both are fake observations, never reserved disk allocations.
- **D-5: Preserve the unmodified production route.** `c849_saved_copy` continues
  to mount the real importer and pass only `-Source` and `-Stage`. The new probe
  is an I/O substitution, not an instruction to skip `Check-Space`. Retain a
  structural test of the real default and caller wiring as well as behavioral
  injected-gate tests. The structural test alone is not gate evidence.
- **D-6: Follow effective placement, not a named host.** Both required routes,
  `GET /api/runner-defaults` and `GET /api/session-runners`, were read successfully
  on 2026-10-04 at about 12:23 UTC. Defaults revision 2 resolved to an eligible
  Linux runner; an eligible Windows runner was also listed. These are observations,
  not pins. Omit `-Runner` and `-Platform` when dispatching this Any-platform card;
  re-read placement at dispatch. The targeted lane requires a Linux shell with
  pwsh and the existing Unix utilities, either native or through WSL. An absent
  prerequisite is not successful qualification of the changed tests.
- **D-7: Complete verification design separately.** The brief did not fold
  TestDesign into Plan. The acceptance map and checkpoint proposal below constrain
  that next stage; TestDesign must add the full inspection/guard/positive-control
  design before Code. These are implementation decisions within the card's scope;
  no product or operator decision is outstanding.

## Implementation shape

### Shell fixture

Use a helper in `RemoteScriptContractTests.cs` to generate a child-shell `df()`
function with explicit available-KiB data. Validate/log the argument vector in
fixture-owned files so the test proves it asked for DockerRootDir. The deploy
regression retains its existing sufficient `sudo df` reading for the earlier
runner-state check; the added bare `df` fake supplies sufficient cache headroom.
Unexpected invocations fail visibly rather than consulting the host.

For the focused budget-gate cases, load `Block(Remote(), "c849_budget_gate")`
unchanged and fake its surrounding observations: a known DockerRootDir and three
within-budget volume rows. Capture the subshell exit and diagnosis explicitly.
On acceptance assert the written `free-bytes` equals the supplied KiB times 1024;
on refusal assert there is no accepted free-space receipt or continuation witness.
This tests the real parser and refusal path, not only `c849_headroom_state`.

### Saved-donor fixture

Add `tests/Antiphon.Tests/Scripts/Fixtures/c997-import-saved-donor.ps1`. Pass its
path and the actual importer path through the existing Linux repository-variable
mapping. The wrapper accepts source, stage, available bytes, and optional
test-only trace/failure settings. It calls the real script with a closed-over
probe, verifies the received stage path, and returns exactly one numeric reading.
Keep diagnostic tracing out of stdout, which belongs to the importer's diagnosis.

Initialize and forward the child script's exit status explicitly: an importer
`exit 2` reached through PowerShell's call operator must not become wrapper exit 0.
Do not catch/relabel importer diagnoses in the wrapper. The failure setting throws
from the probe, letting the importer's existing catch map it to
`CacheSavedDonorReadFailed`.

Update the two reported saved-donor tests and the size-bomb regression to use this
wrapper for every importer subprocess, including the FIFO's bounded subprocess.
Retain all existing payload, marker, mode, ownership, recovery, unsafe-entry,
consumer-counter and missing-pack assertions. Keep `RequireLinuxPwsh()` before
starting script work and `[ParallelLimiter<ProcessSpawnLimit>]` on spawning tests.
Do not change the existing 60-second shell or 5-second FIFO deadlines.

New gate cases use tiny source trees with known, nonzero file bytes in both roles,
for example 7 package bytes and 11 npm bytes. Set required space independently in
the test to `21474836480 + 7 + 11`; do not read a threshold from the production
script to compute the expected answer. Test both tar and directory imports using
fresh stages. Check exact payload bytes on success and an empty stage on refusal;
retain an outside sentinel. No source fixture exceeds a few small files.

## Slices

### S1: Isolate the three regressions at the measurement boundaries

Files:

- `scripts/c849-import-saved-donor.ps1`: optional probe parameter with real default;
  call it from `Check-Space`. No other behavior change.
- `tests/Antiphon.Tests/Scripts/RemoteScriptContractTests.cs`: local `df` helper,
  deploy-fixture wiring, and saved-donor wrapper invocations.
- `tests/Antiphon.Tests/Scripts/Fixtures/c997-import-saved-donor.ps1`: new isolated
  probe wrapper with exact exit propagation and optional trace.

Tests retained: the three methods named on CARD-0997, plus
`C849_Saved_donor_rejects_declared_size_bomb_before_writing`.
Commit and push this slice. Its ordinary qualification is grouped with S2 so the
seam and the gate tests receive one targeted build/run at the same source SHA.

### S2: Prove both gates and document the fixture contract

Files:

- `tests/Antiphon.Tests/Scripts/RemoteScriptContractTests.cs`: the four proposed
  C997 methods below; each spawning method retains the assembly's process limiter.
- `tests/Antiphon.Tests/Scripts/Fixtures/c997-import-saved-donor.ps1`: only fixture
  support required by those cases, including stage-path and probe-call evidence.
- `docs/testing-and-build.md`: short owner note explaining that these Unit tests
  fake readings while operational callers retain real disk admission.

Commit and push before executing the checkpoint group. Run the finalized
TestDesign manifest through the checkpoint tool; preserve actual counts and source
provenance. No server, client, database, image, compose, scheduler or live deployment
change belongs to either slice. CARD-0905/0980 portability and CARD-0875 cleanup
remain separate work; preserve their existing protections.

## Acceptance map for TestDesign

These are proposed exact test names and result counts, not tests claimed to exist
or to have run. Keep new tests in `Antiphon.Tests.Scripts`; no checkpoint-namespace
census change is needed.

| ID | Method(s) in `RemoteScriptContractTests` | Required observations | Planned results |
|---|---|---|---:|
| R-1 | `C849_Deploy_temp_observes_inaccessible_docker_mountpoints_with_sudo` | Still reaches the broker, reports deploy/observe/prune exit 0, records elevated path reads and no unprivileged access; bare and sudo disk paths are controlled. | 1 |
| R-2 | `C849_Saved_donor_archive_is_checked_and_imported_without_a_container`; `C849_Saved_donor_rejects_unsafe_archives_missing_pack_and_busy_counters` | All existing assertions survive, including successful archive/directory import, smoke refusal cleanup, missing-pack diagnosis and busy/unknown consumers. Every importer invocation uses controlled space. | 2 |
| R-3 | `C849_Saved_donor_rejects_declared_size_bomb_before_writing`; `C849_Prune_preview_is_read_only_and_bounded`; `C905_Missing_linux_pwsh_skips_all_five_cases_before_a_script_block` | Size budget still refuses before writing; preview boundary assertions remain; the five existing pwsh-dependent methods still refuse script work when prerequisites are absent. | 3 |
| V-1 | `C997_Cache_budget_gate_uses_controlled_disk_readings` | Five argument rows: 20971519 KiB refuses `CacheDiskLow`/exit 2; 20971520 and 20971521 accept/exit 0 with exact byte receipts; malformed and missing available columns refuse `CacheDiskUnavailable`/exit 2. Probe arguments name the fake Docker root. | 5 |
| V-2 | `C997_Saved_donor_space_gate_preserves_headroom_and_payload_allowance` | Six argument rows: tar/directory crossed with required-minus-one, exact-required, required-plus-one bytes. Low is exactly `CacheDiskLow`/exit 2 before copying. Accepted imports preserve exact package/npm content. Probe called once for the given stage. | 6 |
| V-3 | `C997_Saved_donor_probe_failure_refuses_before_copy` | Two argument rows, tar/directory: probe gets the actual stage, throws, and yields exactly `CacheSavedDonorReadFailed`/exit 2 with no payload writes and outside sentinel unchanged. | 2 |
| R-4 | `C997_Saved_donor_default_probe_remains_real` | Structural guard: optional default still reads `DriveInfo.AvailableFreeSpace` for its supplied path, `Check-Space` invokes it with Stage, and `c849_saved_copy` supplies no fake override. This does not replace V-1/V-2 behavioral evidence. | 1 |
| R-5 | Existing `Antiphon.Tests` Unit selection | The ordinary Unit lane still selects the repaired tests; no low-disk skip, category change or ambient test setting hides them. | Full lane; count reported from fresh TRX |

Targeted roster: 6 existing + 14 new parameter-expanded executions = **20**.
Internal shell assertions and loop iterations do not add TUnit results. TestDesign
may improve the organization, but must reconcile names, argument expansion, filters
and floors in this plan before Code. It must specify decisive assertion labels and
method-scoped positive controls for at least shell low-space admission/malformed
readings, importer headroom, each payload-size summand, validation-before-copy, and
wrapper refusal-exit propagation. Breaks must reach the intended assertion, not
fail to build or merely error in fixture setup.

### Checkpoints

Proposed closed ordinary scope, to be finalized by TestDesign. Both rows use the
**Unit lane**; CP-1 is its targeted Linux-shell contract group and CP-2 is the full
Unit regression selection. Each row has one isolated build and one exact filter.
No named machine, live Docker service or low-disk host is required. CP-1 requires
all 20 results and no skips. CP-2's floor is the independently specified 20-result
targeted roster, not an invented full-lane census; report the actual full count
and require those 20 results to be present and passing. Any other skip must be
accounted for under the existing lane policy, never introduced by this card.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | S1-S2 | `tests/Antiphon.Tests -> bin-c997-disk-contract/` | unit-disk-contract | `/*/Antiphon.Tests.Scripts/RemoteScriptContractTests/(C849_Deploy_temp_observes_inaccessible_docker_mountpoints_with_sudo*)\|(C849_Saved_donor_*)\|(C849_Prune_preview_is_read_only_and_bounded*)\|(C905_Missing_linux_pwsh_skips_all_five_cases_before_a_script_block*)\|(C997_*)` | R-1, R-2, R-3, R-4, V-1, V-2, V-3 | All 20 listed results, 0 failed/skipped | 20 | 8 | true |
| CP-2 | S1-S2 | `tests/Antiphon.Tests -> bin-c997-unit/` | unit-regression | `/*/*/*/*[Category=Unit]` | R-5 | At least 20 executed, 0 failed; all 20 targeted results present and passing | 20 | 18 | true |

### Execution and cost

The 26-minute ordinary floor is an estimate including both isolated builds, not a
measurement. Estimate 60-90 minutes for implementation/fixture authoring. TestDesign
must separately price method-scoped post-land Mutation PCs and add its required
`## Verification design` section; this Plan does not authorize skipping that stage.
Retaining the ordinary Unit lane costs a second build but checks suite selection
and child-process isolation. Do not run the full Antiphon.Tests assembly.

Use the checkpoint tool's `run --plan <this-plan> --after S1-S2
--expected-source-sha <committed-sha>` contract. Bootstrap the tool only if needed,
through `scripts/build-slot.ps1` with an alternate output path. The prebuilt
checkpoint tool owns the row build slots; do not wrap its row execution in another
slot lease. Keep calling `wait` while exit is 75 and await every owned run before
settlement. A slot timeout is a not-run row, never a reason to bypass the gate.

Commit source first and leave it unchanged while qualification is in flight. Use
fresh result directories, retain unedited CHECKPOINT lines, and keep TRX/JSON/logs
ignored. Clean only the alternate output directories owned by these runs. If the
Unit row fails, reproduce only the failing methods at the recorded base before
classifying inherited red; report it rather than weakening assertions or retrying
the full suite. Code and Review run the evidence-diff guard over their full task
range. No live cache seed, rollout, volume cleanup or intentional disk exhaustion
is part of this verification.

## Handoff

Next: **test-design**. Expand this plan with the required verification design and
guard-to-PC mapping, confirm the proposed 20-result roster and checkpoint costs,
and retain the separate production/default-probe and controlled-gate obligations.
No builds or tests were run in Plan; validation of this artifact is documentary.
