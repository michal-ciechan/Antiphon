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

### Proposed checkpoints (superseded by TestDesign below)

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

## Verification design

TestDesign, 2026-10-04, inspected at
`e84b80f10571887c86fe8dd5e0c293ccdbfca2ab`. The fix decisions D-1 through D-6 and
slices S1/S2 above stand. This section finalizes verification only. Its roster,
cost and sole `### Checkpoints` table supersede the earlier proposals, including
the proposed full-Unit R-5 run and 60-90-minute authoring estimate. The old table's
heading is renamed because `PlanTableImporter.ExtractSection` selects the first
exact checkpoint heading; leaving two would silently execute the obsolete scope.

The dispatch's 30-60-minute ordinary budget is met by a **56-minute estimated Code
task: 40 authoring + 2 setup + 14 ordinary checkpoints**. There is no whole-Unit
run: selection can be checked directly, and this change does not claim to repair
the other known Unit failures. Fifteen distinct, independently bypassable guards
within this change have fifteen PCs; argument combinations share each guard's
control. Existing unrelated C849 guards are retained regression evidence, not a
new certification of the entire cache subsystem.

### Inspection

Bodies read, including their assertions rather than just method declarations:

- `tests/Antiphon.Tests/Scripts/RemoteScriptContractTests.cs`:
  `C849_Deploy_temp_observes_inaccessible_docker_mountpoints_with_sudo` |
  bare cache `df` versus elevated state `df`, DockerRootDir, observe/prune/deploy
  continuation and elevated path access -> R-1, V-1.
- Same file: `C849_Saved_donor_archive_is_checked_and_imported_without_a_container`,
  `C849_Saved_donor_rejects_unsafe_archives_missing_pack_and_busy_counters`,
  `C849_Saved_donor_rejects_declared_size_bomb_before_writing` |
  fake Docker calling real pwsh, direct FIFO/size-bomb invocations, archive and
  directory sources, refusal status, marker/recovery/ownership, malformed sources
  and missing pack -> R-2/R-3, V-2/V-3.
- Same file: `C849_Prune_preview_is_read_only_and_bounded`,
  `C905_Missing_linux_pwsh_skips_all_five_cases_before_a_script_block`,
  `RequireLinuxPwsh`, `HasLinuxPwsh`, the lazy probe, `LinuxShell`,
  `PrepareLinuxShellScript`, `CacheSeedTreeHarness`, `Block`, `Remote`, `Order` |
  preview's separate boundary, five prerequisite skips, per-child repository
  conversion, source extraction, stdout-only returned evidence, 60-second child
  limit -> R-3/R-4/R-5 and all new behavioral tests. The helper waits for exit but
  does **not** assert the exit code; each new harness must print/capture the
  importer or gate's code explicitly before subsequent shell commands overwrite it.
- `scripts/c849-import-saved-donor.ps1`, complete body including `Resolve-Entry`,
  `Check-Entry`, `Check-Space`, `Write-Entry`, `Read-Tar`, `Read-Directory`, both
  top-level branches and catch | validated size sums, ordering, exception mapping,
  default I/O, traversal/entry protections -> V-2/V-3, R-2/R-3/R-4.
- `scripts/c590-remote.sh`: `c849_budget_gate`, `c849_headroom_state`,
  `c849_budget_state`, `c849_preview`, `c849_saved_copy` |
  three volume rows, column-4 parsing, KiB-to-byte receipt and caller omission of
  overrides -> V-1, R-3/R-4. Volume capacity policy beyond the unchanged setup is
  excluded from new PCs below.
- Nearest existing fixtures: `CacheSeedTreeHarness` in the touched class and
  `tests/Antiphon.Tests/Agents/Fixtures/claude-effort-dialog.ps1`; also read
  `tests/Antiphon.SessionRunner.Tests/Fixtures/CodexVersionChild.ps1` for explicit
  process status and separate trace conventions. The proposed
  `Scripts/Fixtures` directory and C997 wrapper do **not** exist yet. The former
  is the nearest same-assembly PowerShell fixture; neither is an importer seam.
  Create the wrapper in S1 and read it from the checked-out repo through `repo`,
  not a copied output file. Read `DelegateScriptRunner.RepoRoot` and
  `TestHelpers/ProcessSpawnLimit.cs` for that lookup and the one-process limiter.
- Owners read: `docs/project-context.md`, testing/build fast-lane, manifest,
  runner, slot and Mutation sections, orchestration stage/handoff sections and
  `docs/docker-stack.md` saved-cache contract. Also read
  `tools/Antiphon.Checkpoints/Manifest/PlanTableImporter.cs` for heading selection,
  escaped pipes, positive integer estimates and method-token roster extraction.

Missing setup to supply in Code: the wrapper, local `df` generator, four C997
methods, per-call traces and named assertions below. Both ordinary checkpoints
require native Linux bash/pwsh or working WSL bash/pwsh, plus existing tar, perl,
timeout, stat, realpath and ordinary Unix utilities. No real Docker or database
is needed by this selection. An absent prerequisite/skip cannot qualify the row.
Keep existing prerequisites and deadlines; add `RequireLinuxPwsh()` and the
process limiter to the new importer methods before any child work.

The brief supplies a historical **15-failure Unit baseline**, but its exact
TRX/roster is absent from this checkout. The read CARD-0980 Code report records
that the final Unit evidence was returned to its caller; it does not enumerate
those failures. This is a limit on historical classification, not a reason to
run all Unit tests or assert that 15 failures were reproduced. Code reports the
20-result roster against the caller's baseline when available. If a selected
existing method fails, reproduce just that exact method at the recorded task
base before calling it inherited. New C997 tests cannot be classified inherited.

### Delivery inventory

No new or changed asynchronous delivery path exists. These are synchronous
child-process disk admissions and local copies; there is no queue, session input,
outbox, recipient or delivery recovery handoff. Therefore busy/eligible recipient,
enqueue/crash recovery and complete UserPrompt checks are not applicable.

For the local observable boundary, join a fixture's unique root, source and stage
with its child invocation trace. The real importer produces files in that stage;
read their **exact bytes** after child completion to prove a successful copy.
For refusal, enumerate the stage recursively and prove zero payload files and
unchanged outside-sentinel bytes. Exit status/diagnosis/trace alone never proves
payload copying. An unsafe source may have no probe event because validation
correctly precedes the probe; its wrapper-entry event still proves isolation.

Substitutes: `df` output replaces physical free-space measurement; the closed
PowerShell scriptblock replaces only `DriveInfo` observation; Docker/sudo fakes
replace infrastructure in the existing C849 tests. These prove parser, policy,
ordering and fixture plumbing, not physical capacity, kernel permission checks,
real container execution or live recovery. The default/caller structural test
proves tracked wiring only; it cannot qualify operational disk availability.

### Proves it works now

All methods below belong to `Antiphon.Tests.Scripts.RemoteScriptContractTests`.
New assertion labels are required Shouldly messages (use `customMessage:` where
overload resolution requires it), so Mutation can identify a decisive failure.
Case names/argument values must accompany failures. Keep fixture diagnostics out
of importer stdout; read trace files explicitly after capturing child status.

- **V-1:** `C997_Cache_budget_gate_uses_controlled_disk_readings` | Unit, real
  extracted shell function | **5 `[Arguments]` rows**: available KiB `20971519`,
  `20971520`, `20971521`, nonnumeric `invalid`, and absent column 4. Use a fresh
  root/case directory, DockerRootDir with a space, three valid within-budget
  volume rows and strict `df -Pk <that-root>` function. The missing-column row
  must actually have only three data columns; omitting one interior value in a
  six-column table would shift another value into column 4. Assert recorded argv
  equals exactly two arguments (`C997-DF-ARGV`) and exactly one call before
  checking outcomes. Expected tuples are `(2, CacheDiskLow)`, `(0, empty)`,
  `(0, empty)`, `(2, CacheDiskUnavailable)`, `(2, CacheDiskUnavailable)`
  (`C997-SHELL-RESULT`). On acceptance assert `free-bytes=21474836480` or
  `free-bytes=21474837504` respectively (`C997-SHELL-BYTES`) and a continuation
  file created **after** the real function returns. On refusal assert no
  `free-bytes` line and no continuation (`C997-SHELL-NO-ACCEPTANCE`). The fixture
  logs unexpected df invocations before refusing them and never calls host df.
- **V-2:** `C997_Saved_donor_space_gate_preserves_headroom_and_payload_allowance`
  | Unit, real importer through wrapper | **6 `[Arguments]` rows**, format
  `tar`/`directory` crossed with free bytes `21474836497`, `21474836498`,
  `21474836499`. Write exactly seven bytes `pkgdata` and eleven bytes
  `npm-content` in the source, with no added newline, in both cases. Expected
  required bytes are the independent constant **21474836498**. Capture actual
  child code and diagnosis; inspect files before asserting status so a bypassed
  ordering guard fails at `C997-IMPORT-NO-WRITES`. Low rows require empty stage,
  unchanged outside sentinel and then exactly exit 2/`CacheDiskLow`
  (`C997-IMPORT-RESULT`). At/above rows require exit 0, empty diagnosis and exact
  destination bytes (`C997-IMPORT-PAYLOAD`). Require one wrapper-entry event and
  one probe event with the requested reading and actual stage, not a source or
  host-drive path (`C997-PROBE-STAGE`, `C997-PROBE-COUNT`, `C997-PROBE-VALUE`).
  Record the probe's actual argument **before** the wrapper checks it, so a
  wrong-path mutation is an assertion failure with readable evidence.
- **V-3:** `C997_Saved_donor_probe_failure_refuses_before_copy` | Unit, real
  importer | **2 `[Arguments]` rows**, tar/directory. Use the same valid nonzero
  payload and a probe that records its argument then throws a non-`Cache...`
  exception. Assert empty stage first (`C997-PROBE-FAILURE-NO-WRITES`), unchanged
  sentinel, one probe call to the requested stage, then exactly exit 2 and stdout
  `CacheSavedDonorReadFailed` (`C997-PROBE-FAILURE-RESULT`). Do not allow wrapper
  catches to synthesize that diagnosis.

The complete boundary product is covered: shell low/equal/high plus unreadable
column shapes; importer tar/directory times low/equal/high and throwing read.
Both payload summands are positive, so omitting **either** admits the low row;
zero-byte variants would mask those defects. Separate package-only/npm-only rows
are unnecessary for those summand guards. Decimal-unit, strict-comparison and
wrong-column changes are detected by the same exact boundaries/receipts. Overflow,
negative scriptblock results, concurrent source mutation and arbitrary new probe
return types are outside D-3's numeric-reading contract and this change's scope.

### Guards the regression

- **R-1:** retain
  `C849_Deploy_temp_observes_inaccessible_docker_mountpoints_with_sudo` (**1**).
  Keep all existing `OBSERVE_EXIT=0`, `PRUNE_EXIT=0`, `DEPLOY_EXIT=0`,
  `DEPLOY_REACHED_BROKER`, elevated-count and no-denied-access assertions. Add
  exact bare-df root/arguments/call evidence while retaining sufficient elevated
  state-df data. The broker continuation, not a mocked gate return, is decisive.
- **R-2:** retain
  `C849_Saved_donor_archive_is_checked_and_imported_without_a_container` and
  `C849_Saved_donor_rejects_unsafe_archives_missing_pack_and_busy_counters`
  (**2**). Keep every existing assertion: successful imports, masked mode,
  marker, recovery payload/ownership, smoke refusal cleanup, unsafe archive and
  FIFO refusal, exact `AppHostDonorMissing`, and busy/unknown consumer diagnoses.
  Route all child importer calls through the wrapper with 64 GiB. Give each
  invocation a unique trace path and assert its entry records the expected
  source/stage, including the 5-second FIFO child
  (`C997-WRAPPER-ENTRY`, include `directory-fifo` in that assertion). Logging
  begins before invoking the importer, so validation-only refusals are visible.
  Do not add probe-count assertions to sources refused before `Check-Space`.
- **R-3:** retain
  `C849_Saved_donor_rejects_declared_size_bomb_before_writing`,
  `C849_Prune_preview_is_read_only_and_bounded`, and
  `C905_Missing_linux_pwsh_skips_all_five_cases_before_a_script_block` (**3**).
  Size-bomb keeps `CacheBudgetExceeded`/2 and no bomb file, now with wrapper-entry
  evidence; do not allocate its declared 12 GiB. Preview retains its read-only
  text restrictions and independent headroom checks. C905 must still execute
  its assertions and pass; its five inner caught skips are not outer skipped
  TUnit results. Preserve its forced-prerequisite flag restoration.
- **R-4:** add `C997_Saved_donor_default_probe_remains_real` (**1**). Inspect the
  actual importer's optional parameter default, scoped to the whole default
  block rather than matching a token elsewhere: its only returned value is
  `DriveInfo` of the block's supplied stage argument, `.AvailableFreeSpace`
  (`C997-DEFAULT-REAL`). Inspect `Check-Space` for invocation with `$Stage` and
  the unchanged comparison; scope caller checks to `c849_saved_copy`, requiring
  the original importer mount, `-Source /saved -Stage /stage` and no supplied
  `GetAvailableFreeBytes` (`C997-CALLER-DEFAULT`). Source/fixture structural
  assertions supplement V-1/V-2 and do not execute a real free-space reading.
- **R-5:** in that same R-4 method, inspect the class's `Category("Unit")` and
  the ten named methods' attributes/source so none overrides it, adds explicit
  or disk-dependent skip admission, or loses its process limiter when spawning.
  Keep only existing platform/prerequisite skips, and keep the probe/wrapper
  invocations local to each child. Review the source diff as well. Exact roster
  plus zero skips in CP-1/CP-2 proves execution; no full-Unit run is required to
  establish category membership. No extra TUnit result is introduced by this
  inspection (`C997-UNIT-ROSTER`).

Confirmed roster: **6 existing + (5 + 6 + 2 + 1) new = 20 executions** from ten
methods. CP-1 selects the four new methods (14), CP-2 the six existing methods
(6), with no overlap. Internal assertions and the five C905 delegate calls are
not extra results. Require the exact expanded counts and method/argument roster
in fresh TRX, not merely each row's minimum floor. Wildcards below accommodate
TUnit's generated parameter suffixes, not additional named test families.

### Guard inventory

The guarded surface is disk admission, default measurement, wrapper isolation and
evidence supporting these claims. Every listed guard has its own executable PC;
none is left untested. Tar and directory entry paths, the two size summands and
default-versus-caller wiring are independently bypassable and are split.

| Guard | Plan reference and safety-critical invariant | Control |
|---|---|---|
| G-1 | D-1/D-2: shell admits only at least 20971520 available KiB | PC-1 |
| G-2 | D-1: absent/nonnumeric df availability refuses as unavailable | PC-2 |
| G-3 | D-1/D-3: importer retains 20 GiB of base headroom | PC-3 |
| G-4 | D-1: validated package bytes are added to required space | PC-4 |
| G-5 | D-1: validated npm bytes are added to required space | PC-5 |
| G-6 | D-1: tar validation and admission finish before any copy | PC-6 |
| G-7 | D-1: directory validation and admission finish before any copy | PC-7 |
| G-8 | D-3: a throwing measurement cannot admit an import | PC-8 |
| G-9 | D-3: the injected probe receives the actual destination stage | PC-9 |
| G-10 | D-3/D-5: omitted probe retains the real DriveInfo default | PC-10 |
| G-11 | D-5: operational saved-copy caller does not inject fake capacity | PC-11 |
| G-12 | D-4: wrapper preserves the child's refusal exit code | PC-12 |
| G-13 | D-4/S1: every test importer subprocess enters the isolated wrapper | PC-13 |
| G-14 | D-2: the shell gate measures DockerRootDir with the expected df units | PC-14 |
| G-15 | D-1/shell fixture: accepted receipt reports supplied KiB as exact bytes | PC-15 |

### Positive controls

These are planned post-land Mutation controls, not ordinary Code runs. Code runs
V/R; Review judges both the ordinary evidence and executable PC design before
land. Each defect below is syntactically valid; compilation/parsing/setup failure
or zero executions never counts as red. All changes are temporary in the bound
snapshot. Use the copied, unchanged `run-checkpoint.ps1` and build-slot helper in
the assigned external evidence root, separate `bin-c997-pcN-{baseline,red,green}/`
outputs and separate external phase results. Each phase runs only
`/*/*/RemoteScriptContractTests/<exact method below>*`; use that method's listed
expanded count for `-MinExecuted` and inspect the identified argument row.
No class, namespace, Unit or all-C997 PC filters are allowed.

For each PC: baseline green, apply only that defect, build/run red, restore exact
source and refresh timestamps, rebuild/run green. Keep assertion, counts and
source provenance for all phases. Do not batch these controls: most share the
same source/test method. Three phases are the maximum planned repetitions per
PC, not permission for additional unchanged proof rounds.

| PC | Break the mapped guard with this compiling defect | Exact method; Min | Decisive red assertion |
|---|---|---|---|
| PC-1 | G-1: in `c849_budget_gate`, lower `20971520` to `20971519` | `C997_Cache_budget_gate_uses_controlled_disk_readings`; 5 | `C997-SHELL-RESULT`: low row returns 0 instead of 2/CacheDiskLow |
| PC-2 | G-2: replace the numeric-match refusal line in that gate with `:`; retain the numeric threshold | `C997_Cache_budget_gate_uses_controlled_disk_readings`; 5 | `C997-SHELL-RESULT`: malformed/missing rows produce CacheDiskLow instead of CacheDiskUnavailable; this is a captured gate verdict, not a fixture exception |
| PC-3 | G-3: change only `20GB` to `19GB` inside `Check-Space` | `C997_Saved_donor_space_gate_preserves_headroom_and_payload_allowance`; 6 | `C997-IMPORT-NO-WRITES`: low tar/directory rows contain copied payload |
| PC-4 | G-4: remove only `+ $sizes.packages` from the comparison | `C997_Saved_donor_space_gate_preserves_headroom_and_payload_allowance`; 6 | `C997-IMPORT-NO-WRITES`: low rows admit 18 bytes against a requirement missing 7 |
| PC-5 | G-5: remove only `+ $sizes.npm` from the comparison | `C997_Saved_donor_space_gate_preserves_headroom_and_payload_allowance`; 6 | `C997-IMPORT-NO-WRITES`: low rows admit 18 bytes against a requirement missing 11 |
| PC-6 | G-6: change the tar branch's first `Read-Tar $false` to `Read-Tar $true`, leaving its later steps intact | `C997_Saved_donor_space_gate_preserves_headroom_and_payload_allowance`; 6 | `C997-IMPORT-NO-WRITES`: low tar row has files even if the later duplicate copy refuses |
| PC-7 | G-7: change the directory branch's first `Read-Directory $false` to `Read-Directory $true` | `C997_Saved_donor_space_gate_preserves_headroom_and_payload_allowance`; 6 | `C997-IMPORT-NO-WRITES`: low directory row has files before admission |
| PC-8 | G-8: surround only the injected probe invocation with a catch that substitutes `[long]::MaxValue` on exception | `C997_Saved_donor_probe_failure_refuses_before_copy`; 2 | `C997-PROBE-FAILURE-NO-WRITES`: throwing-probe rows copy payload |
| PC-9 | G-9: invoke the probe with `$Source` instead of `$Stage` inside `Check-Space` | `C997_Saved_donor_space_gate_preserves_headroom_and_payload_allowance`; 6 | `C997-PROBE-STAGE`: recorded actual argument equals source, not requested stage; assert trace before outcome |
| PC-10 | G-10: replace the optional default block's DriveInfo expression with `[long]68719476736`, retaining its parameter | `C997_Saved_donor_default_probe_remains_real`; 1 | `C997-DEFAULT-REAL`: complete default block is a constant instead of real stage DriveInfo |
| PC-11 | G-11: replace the caller's `-File /import.ps1 -Source /saved -Stage /stage` suffix with `-Command '& /import.ps1 -Source /saved -Stage /stage -GetAvailableFreeBytes { param($p) [long]68719476736 }'` | `C997_Saved_donor_default_probe_remains_real`; 1 | `C997-CALLER-DEFAULT`: the syntactically valid operational invocation supplies fake capacity instead of using the omitted default |
| PC-12 | G-12: change the wrapper's final propagated exit to `exit 0` | `C997_Saved_donor_probe_failure_refuses_before_copy`; 2 | `C997-PROBE-FAILURE-RESULT`: diagnosis remains CacheSavedDonorReadFailed but process code is 0, expected 2 |
| PC-13 | G-13: change only the existing FIFO child's command back to direct real-importer invocation, retaining its args and 5-second timeout | `C849_Saved_donor_rejects_unsafe_archives_missing_pack_and_busy_counters`; 1 | `C997-WRAPPER-ENTRY` for directory-fifo: no wrapper entry, despite the normal unsafe-entry refusal; this detects bypass regardless of host space |
| PC-14 | G-14: change only the gate's df target from the DockerRootDir expression to `/` | `C997_Cache_budget_gate_uses_controlled_disk_readings`; 5 | `C997-DF-ARGV`: logged target is `/`, not the fixture Docker root; assert log before gate result |
| PC-15 | G-15: change only receipt multiplication from `free_kb * 1024` to `free_kb` | `C997_Cache_budget_gate_uses_controlled_disk_readings`; 5 | `C997-SHELL-BYTES`: exact/above accepted rows report KiB values instead of bytes |

Assertion order for V-2 is trace argument/count first, no-writes on low rows next,
then outcome/payload. PC-9 therefore fails on a specific trace mismatch rather
than the downstream mapped error. PCs 3-7 preserve a correct trace when they
reach the probe and fail at the copied-files assertion. Fixture startup and
parse failures must be investigated, never accepted as the named failure.

### Out of scope

- No async/session delivery, live Docker seeding, deployments, physical disk
  exhaustion or disk cleanup. These add different risks and cannot strengthen
  the deterministic policy claim.
- No full Unit/assembly, new database/native integration, independent Windows
  certification, or repeated success runs. The affected code uses the existing
  Any-platform LinuxShell route; CARD-0980 owns Windows path conversion. R-3
  retains CARD-0905's five-case prerequisite behavior; extending that unrelated
  prerequisite matrix is not another result in this roster.
- Existing volume-identity, per-volume budgets, unsafe-entry validation details,
  permissions, consumer counters, ready-marker/recovery and preview policies
  remain unchanged. R-1/R-2/R-3 retain their current assertions; the new PCs
  specifically certify the measurement/admission seam and validation/copy order,
  not every independent C849 safety guard. Do not infer live recovery safety from
  the mocked ownership/consumer/marker evidence.
- No source stabilization, probe API expansion or assertion weakening to address
  an unrelated Unit red. Preserve the exact failing evidence and compare the
  failing existing method at base. A newly discovered out-of-scope production
  defect needs its own named follow-up card/slice; none is required to execute
  the present manifest.

### Checkpoints

Closed Code/ordinary Review scope, after S1 and S2 are committed and pushed.
Both rows are Unit tests in the inspected Unit class; category membership is
checked by R-5 without using a broad category selector. Each builds separately
and runs one exact method-OR filter. Both are serial because the limiter is only
assembly-local. Run once per unchanged qualification, no automatic repeats.
Failure-driven reruns are reported separately; never exceed three rounds without
returning the evidence and revised scope to the caller.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | S1-S2 | `tests/Antiphon.Tests -> bin-c997-disk-contract/` | unit-disk-contract | `/*/Antiphon.Tests.Scripts/RemoteScriptContractTests/(C997_Cache_budget_gate_uses_controlled_disk_readings*)\|(C997_Saved_donor_space_gate_preserves_headroom_and_payload_allowance*)\|(C997_Saved_donor_probe_failure_refuses_before_copy*)\|(C997_Saved_donor_default_probe_remains_real*)` | V-1, V-2, V-3, R-4, R-5 | Exactly 14 executed: 5/6/2/1 by method; all listed, 0 failed/skipped | 14 | 7 | true |
| CP-2 | S1-S2 | `tests/Antiphon.Tests -> bin-c997-unit-regressions/` | unit-disk-regressions | `/*/Antiphon.Tests.Scripts/RemoteScriptContractTests/(C849_Deploy_temp_observes_inaccessible_docker_mountpoints_with_sudo*)\|(C849_Saved_donor_archive_is_checked_and_imported_without_a_container*)\|(C849_Saved_donor_rejects_unsafe_archives_missing_pack_and_busy_counters*)\|(C849_Saved_donor_rejects_declared_size_bomb_before_writing*)\|(C849_Prune_preview_is_read_only_and_bounded*)\|(C905_Missing_linux_pwsh_skips_all_five_cases_before_a_script_block*)` | R-1, R-2, R-3, R-5 | Exactly 6 executed: 1 per method; all listed, 0 failed/skipped | 6 | 7 | true |

Use the prebuilt checkpoint tool's `run --plan
docs/superpowers/plans/2026-10-04-card-0997-host-independent-cache-disk-tests-plan.md
--after S1-S2 --expected-source-sha <the committed Code SHA>` and await every owned
run until its exit differs from 75. A necessary tool bootstrap build is setup,
reported explicitly and run through `scripts/build-slot.ps1` into its own
alternate output; row execution owns its slots. No bypass after a timeout.
Validate clean SHA-bound receipts for CP-1/CP-2, retain unedited CHECKPOINT lines,
expanded counts and phase durations. Keep raw evidence ignored, run the full
task-range evidence-diff guard in Code/Review, and remove only owned `bin-c997-*`
outputs after completion. Source remains frozen during each run.

### Cost

All numbers are estimates, not measured test performance at this SHA.

- **Ordinary V/R floor (Code): 7 + 7 = 14 minutes**, the sum of checkpoint
  estimates. CP-1's exact four-method filter/14 results costs 7 minutes; CP-2's
  exact six-method filter/6 results costs 7 minutes. Each includes 2 minutes for
  its isolated build and 5 for execution/receipt inspection. Setup/tool bootstrap
  adds 2 once; authoring S1/S2 adds 40. **Code total: 56 minutes.** An independent
  ordinary Review execution uses the same 14-minute scope; its review-writing
  time is not a test floor.
- **Mutation floor: 65 minutes** = 5 setup/discovery/reporting + **15 PCs times
  4 minutes**. For each PC, reserve 1 minute per isolated phase build (baseline,
  red, restored green), 0.75 total for its three exact-method executions and 0.25
  for applying/restoring/checking source. Thus every PC's green and red work is
  priced, with no broad shared run or unpriced restore. Filters are the exact
  methods in the PC table: V-1's method for PC-1/2/14/15 (16 minutes); V-2's for
  PC-3/4/5/6/7/9 (24); V-3's for PC-8/12 (8); R-4's for PC-10/11 (8); R-2's unsafe
  donor method for PC-13 (4). These sum to 60 plus the 5 setup minutes.
- **Code + Mutation total: 121 minutes** = 40 authoring + 2 Code setup + 14 V/R
  (including its builds) + 65 Mutation. The verification-only total is **81**;
  adding one independent 14-minute ordinary Review run makes verification **95**
  and authoring plus verification **135**. No Mutation time is hidden in the
  30-60-minute ordinary task estimate. Slot contention and an actual defect can
  increase elapsed time; report them, not speculative successful reruns.
- Against the earlier 26-minute ordinary proposal, the closed targeted scope
  saves **12 estimated minutes (46%)** and still executes every one of the 20
  promised results once. There are no measured savings yet. Mutation has no
  prior numeric baseline, so no additional numerical PC savings are claimed.
  Every individual checkpoint (7) and PC cycle (4) fits a ten-minute work window;
  no row needs a deferred follow-up slice. Stop to scope a real overrun or new
  defect instead of adding a whole-Unit round.

Handoff audit: touched bodies and nearest fixtures read; **guards=15, mapped=15,
missing=0, duplicate PC maps=0**. All PCs have a concrete syntactically valid
defect, exact method, expanded floor and decisive assertion. They become runnable
with the S1/S2 tests named here; none requires a new production seam beyond D-3.
This stage changes only the plan and performs documentary/schema validation;
ordinary execution and deliberate mutations remain with their respective stages.
Next: **code**.
