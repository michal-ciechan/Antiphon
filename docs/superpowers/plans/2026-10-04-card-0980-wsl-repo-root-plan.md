# CARD-0980: translate repository paths at the Windows/WSL test boundary

Date: 2026-10-04. Plan task: `0c961739-2c87-489b-b7ba-2cf27132c4ae`.
Source baseline and fetched `origin/master`:
`7b8e687a73c17167a49b6ce0a3ace1d1ab1f796a`.

## Outcome and stage boundary

The card remains necessary. All five C849 methods still embed a native
`DelegateScriptRunner.RepoRoot` in a script sent to WSL. Plan one small test-only
slice that translates that root with `wslpath -u` on Windows, preserves the Linux
script bytes, and refuses future raw-root callers before launching a shell.

This is Plan only. No implementation files or settings were changed, and no
build, test or deployment was run. **Next: TestDesign**: the brief did not
fold that stage into Plan. The verification design below provides concrete
methods, counts and checkpoints for its freeze; it is not authorization to skip
TestDesign. These are implementation decisions within the card's requested
behavior, with no outstanding product-default decision.

Owners consulted: `AGENTS.md`, `docs/project-context.md`, `docs/ops-http.md`,
`docs/antiphon-api.md`, `docs/orchestration-loop.md`,
`docs/agent-card-lifecycle.md`, and `docs/testing-and-build.md`.

## Ground truth

Card evidence: `card.ps1 get CARD-0980 -Board Antiphon`, its full description,
`card.ps1 history`, and the card thread. History contains the current Plan
dispatch Move at 2026-10-04 00:47:27 UTC; no content revision supersedes the ask.
The historical Windows failures are card/thread evidence, not new executions.

| Card assumption | What current code/evidence does | Planning consequence |
|---|---|---|
| Five Windows C849 methods hand Windows paths to Linux PowerShell. | `tests/Antiphon.Tests/Scripts/RemoteScriptContractTests.cs:2061,2285,2430,2552,2650` interpolate the root with apostrophe escaping only. The surrounding methods are the five listed below. | Repair those assignments, preserving all existing behavioral assertions. |
| Linux and Windows use different shell launchers. | `LinuxShell` at line 3936 starts `bash -s` on Linux and system `wsl.exe -e bash -s` on Windows. It normalizes CRLF to LF at stdin, uses UTF-8 without BOM, captures stdout/stderr, and has a 60-second bound. | Convert in the existing WSL invocation; retain shell selection, encoding, timeout, stream handling and cleanup. |
| RepoRoot can be changed globally to POSIX. | `tests/Antiphon.Tests/Application/DelegateScriptRunner.cs:68` discovers the native checkout path by walking from the test binary. Native file reads and native PowerShell use it throughout the suite. | Leave that helper unchanged; translation belongs at this Linux-shell boundary. |
| The missing-pwsh policy should disappear now that WSL has pwsh. | `RequireLinuxPwsh`, the cached shell probe and the two C905 tests at Remote lines 862-929 still encode CARD-0905. Each affected method calls the prerequisite first. | Preserve the real missing-tool skip and its exact reason. An installed tool plus a bad path must fail, never skip. |
| There is no existing conversion example. | `C946_Green_harness_removes_root_and_failed_keep_run_retains_evidence` at lines 833-859 replaces separators and runs `wslpath` through a separate `LinuxShell` call on Windows. | Reuse the same OS boundary, but consolidate this sixth caller with the new helper so the guard does not need an exemption. |
| CARD-1008 was a prerequisite. | Land `0049877688f895bc4fb75e49cfe8911489b2a937` is an ancestor of the baseline. RemoteScriptContractTests has identical Git blob `c496900532437ac64403da5052210ee7d18ea965` at that land and current master. No later master commit changes this file or `scripts/deploy-server2.ps1`. | The hold is satisfied; plan against these current files, not the old line numbers. |
| Windows full-Remote proof is 57/57. | Current Remote source declares 77 `[Test]` methods and 95 argument-expanded cases: 77 + 7 + 2 + 5 + 4. The CARD-1008 plan also calls out a 95-case post-land roster. The later C973 Windows thread reported 69/64/5/0 at `26637c41`. | Historical 57 is stale. Four proposed single-result guards make the planned class total **99**. Recount at TestDesign/Code admission if another card lands. |
| Only this class uses LinuxShell. | `CodexRunnerImageContractTests`, `GrokRunnerImageContractTests`, and `JqRunnerImageContractTests` call its internal helper. Their current expanded counts are 11, 26 and 14. | Add a 51-result consumer row on both platforms; do not edit those consumers merely to make a guard pass. |
| The newer full class is already qualified on Windows. | CARD-1008 commissioned Linux evidence. `C1008HostFixture` in `RollingVolumeRecycleScriptTests.cs:438-600` launches bare `bash` with a native temp script path and interpolates native paths. `C1008GitGraph` at Remote lines 624-655 does likewise. | Additional Windows failures are a source-inspected risk, not proven inherited failures. Keep the full-class row; replay any extra failing method at the exact base before classification. Do not widen this fix into fixture portability repairs. |
| The checkpoint namespace census might need updating. | `scripts/lib/checkpoint-usage.ps1:114` currently has literal `selected = 377`, for `Antiphon.Tests.Checkpoints`, not Scripts. | Leave **377** unchanged. New tests stay in the Scripts namespace. |
| Rolling production behavior is implicated. | Current `deploy-server2.ps1` includes C1008 rollout locking, recycle admission and retired-temp checks. The affected C849 seed test uses the c727 HTTP/verifier stubs and asserts its existing case order. | No deployment-script change; retain the current C1008 lock/call-chain fixture additions and all retirement/cache assertions. |

The five required existing methods, all in `RemoteScriptContractTests`:

1. `C849_Cache_cases_use_only_the_validated_host_lane`
2. `C849_Seed_publishes_complete_payloads_before_its_marker`
3. `C849_Saved_donor_archive_is_checked_and_imported_without_a_container`
4. `C849_Saved_donor_rejects_unsafe_archives_missing_pack_and_busy_counters`
5. `C849_Saved_donor_rejects_declared_size_bomb_before_writing`

## Decisions

### D-1: keep translation in this test helper

Extend `LinuxShell` with an optional repository-variable argument, accepting only
the existing literal names `root` and `repo`. Existing callers with no argument
continue to send their script unchanged. A small pure preparation helper takes
the body, variable name, native root and Windows flag; the process entry point
supplies `DelegateScriptRunner.RepoRoot` and `OperatingSystem.IsWindows()`.
Explicit inputs let tests exercise both preparations on either platform without
global OS overrides or a new framework/service.

For a caller requesting a repository variable, prepend the existing
single-quoted assignment, escaping apostrophes once. On Linux this is exactly
the previous assignment plus body, byte for byte before the existing CRLF
normalization. On Windows, immediately follow that assignment with
`variable="$(wslpath -u "$variable")"` and explicit failure checks before the
body. Require successful conversion and a nonempty absolute POSIX result;
otherwise emit `C980_REPO_ROOT_CONVERSION_FAILED` and exit the shell before any
script body runs. Do not assume `/mnt/c`, the drive, a mount configuration or a
WSL distribution. Do not call `wslpath` on Linux.

This uses one shell process per original invocation, avoids a path cache and
keeps conversion adjacent to the code consuming the path. The real five Windows
tests remain the oracle that WSL PowerShell can actually open the scripts.

Rejected: changing global RepoRoot, a hard-coded drive/mount substitution, only
changing backslashes, production-script edits, or merely adding a skip. A
separate conversion subprocess per caller is unnecessary when the existing
script already executes in the right WSL distribution.

### D-2: fail loudly for future raw-root bodies

Before adding the trusted prelude or starting a process, Windows preparation
rejects a body containing the current native repository root: cover its native
and forward-slash spelling, both literal and bash single-quote-escaped forms.
Compare the Windows spelling without case sensitivity. The diagnostic must say
that RepoRoot needs the repository-variable argument. Check only this actual
root, not every drive-looking string, so negative fixtures can still contain
unrelated sample paths. Linux preparation is unchanged by this guard.

Apply that check even when the caller requests the variable: otherwise a future
caller could use the helper and still embed a second raw path. Insert the raw
root only after validation, inside the helper-owned prelude. No public bypass
flag and no C946 exemption. Migrate C946 to the same `repo` argument and retain
its green-cleanup/failed-retention assertions.

Rejected: broad Windows-path regexes, silently rewriting arbitrary script text,
a source-text-only check for the five present call sites, or allowing the guard
to reach pwsh and report only its usage message. This is a regression guard for
direct RepoRoot embeddings, not a general shell parser or security boundary.

### D-3: preserve every existing Linux and prerequisite contract

Keep all five method bodies and expected output unchanged except for relocating
their root assignment. In particular retain the C1008 lock block in saved-donor
setup, retired-container false/true case assertions, unsafe-archive and budget
diagnoses, cache ownership, fixture stubs, and cleanup traps. `RequireLinuxPwsh`
must precede conversion, fixture setup and child execution. Preserve the C905
forced-missing behavior and exact skip reason, and the C912 jq checks.

No change to `LinuxShell`'s general treatment of nonzero script exit codes: many
negative fixtures intentionally produce them. The conversion failure marker and
absence of body output must remain visible to the invoking test. No changes to
timeouts, retries, global PATH, line-ending attributes, or Linux expected values.

### D-4: native Windows is a separate final-SHA proof

Linux Code cannot produce native Windows evidence from this mirror. The caller
commissions a separate **Debug task with `-Platform Windows`** at the final
pushed implementation SHA for CP-3 and CP-4. It launches the .NET test host
natively on Windows; only the existing shell child runs in WSL. Running the
whole test assembly inside WSL would leave `IsWindows()` false and miss the bug.

The earlier approved desktop WSL installation is jq **1.7.1** and pwsh **7.6.6**
under `/usr/local/bin`. Debug rechecks them through the exact noninteractive
`wsl.exe -e bash -s` lane, along with `wslpath`, and records the actual versions
and resolved paths. This is read-only setup inspection, not a new install or
PATH edit. A missing prerequisite leaves this proof incomplete even if a TUnit
skip is correctly named. The five affected cases must execute and pass with
zero skips; the required full class also has zero failed/skipped results.

## Scope, slices and activation

Only implementation target: `tests/Antiphon.Tests/Scripts/RemoteScriptContractTests.cs`.
The plan artifact is the only file changed by this Plan task.

| Slice | Files and work | Tests / closing evidence |
|---|---|---|
| S1 | RemoteScriptContractTests.cs: preparation/guard helper; optional repository-variable argument; five assignment migrations; C946 consolidation; four C980 single-result tests below. Preserve all current assertions and attributes, using `ParallelLimiter<ProcessSpawnLimit>` on every new process-spawning test. | CP-1 and CP-2 from the committed slice; CP-3 and CP-4 in separate Windows Debug at that same final SHA. |

Keep S1 small and atomic: a guard without migrated callers would knowingly break
the Windows class. Commit and push S1 before checkpoints. Fix a red row only
after its process exits, then commit/push the fix before rerunning the affected
row. Never amend/rebase/reset this assigned pushed branch.

Activation is **tests only**: land the reviewed test change. No AppHost/runner
restart, image publication, remote deployment, cache recycling or provider
canary is required. If landing changes the source SHA through integration,
qualify the resulting SHA as required by the landing/review contract; do not
relabel earlier Windows receipts.

## Placement and collisions

Read GET `/api/runner-defaults` and GET `/api/session-runners` again at every
dispatch. The 2026-10-04 00:47 UTC observation was defaults revision 2, an
automatic Linux preference, an available Windows entry, an available Linux
entry, and an unavailable drained temporary entry. This is an observation, not
a placement constant. Omit `-Runner` unless deliberately pinning one host.
Omit `-Platform` for portable Plan/TestDesign/Code; use the OS constraint for
the native Windows Debug proof. `-Platform Any` removes an inherited OS pin.
The checkpoint group names below explicitly identify each execution lane.

The scoped Antiphon task listing observed `bd02f8d9`, `ffc7e9ad`, `adbad12b`,
and this task Dispatched; `044f2398` was a dispatched TestDesign task. Card
states and scopes can change; repeat the collision check before Code and land.
This Plan dispatch does not allocate sibling tasks or alter fleet settings.

| Work named in the brief | Collision / coordination |
|---|---|
| CARD-1008, landed `00498776` | Dependency satisfied. Preserve its current Remote test additions and deploy-script behavior. Do not reapply a pre-land copy of the class. |
| CARD-1010 Plan `adbad12b` | Direct reservation on deploy scripts and RemoteScriptContractTests. Parallel plan writing is safe; serialize implementation touching the shared class. Reconcile the latest plan/source and recount after either Code slice lands. CARD-0980 owns only the helper and named callers, not recycle opt-ins. |
| CARD-0959 Code `bd02f8d9` | Runner/CLI capability work; no intended S1 production overlap, but image-contract consumer counts and expectations may move. Re-read/recount the consumer row after its land; no provider or version changes in this card. |
| CARD-1011 replay `ffc7e9ad` | Windows Grok qualification/routing and bundle work. Coordinate Windows seats and current Debug routing with its settled result; do not hard-code a provider override or recreate its canary. |
| CARD-1013 Windows rows | Shared Windows build capacity only; checkpoint-coverage work is outside S1. Use build slots, distinct outputs and task-owned processes. |
| CARD-1017 freeze | Worktree/evidence disposal policy. Keep raw checkpoint output ignored, preserve the final report and pushed plan pointer, and leave cleanup-policy/custody code untouched. |
| CARD-1020 plan | Test-owned PtyHost/OpenConsole lifetime work. No Pty tests selected here and no kill/restart authority inferred from it. Coordinate native host capacity; clean only children/outputs owned by this task. |
| CARD-1022 plan | Backend removal and Windows dispatch context may change; this plan needs WSL, not an inbox/modern ConPTY matrix. Do not pin or expand that backend work. |
| Checkpoint census literal 377 | No Checkpoints-namespace additions. Keep the independent literal and its guards unchanged. |

## Planning verification proposal (superseded by the TestDesign freeze below)

TestDesign must freeze these method names, assertions, expanded counts and
checkpoint import before Code. The four proposed tests are single-result
methods (use internal vector loops, not parameter expansion). Thus the proposed
Remote roster is 81 methods / 99 results. If TestDesign chooses parameterized
cases, it must update the table and explain the new count before Code.

| ID | Class-qualified method(s) | Required witness |
|---|---|---|
| V-1 | `RemoteScriptContractTests.C849_Cache_cases_use_only_the_validated_host_lane`; `RemoteScriptContractTests.C849_Seed_publishes_complete_payloads_before_its_marker`; `RemoteScriptContractTests.C849_Saved_donor_archive_is_checked_and_imported_without_a_container`; `RemoteScriptContractTests.C849_Saved_donor_rejects_unsafe_archives_missing_pack_and_busy_counters`; `RemoteScriptContractTests.C849_Saved_donor_rejects_declared_size_bomb_before_writing` | All five existing behavioral oracles execute in native Windows TUnit and reach the intended script assertions; no path/usage errors and no skips. Linux expectations remain identical. |
| V-2 | `RemoteScriptContractTests.C980_Repo_root_assignment_preserves_linux_bytes` (new) | Independently construct the old literal assignment plus body for both `root` and `repo`; compare all bytes with actual Linux preparation. Include spaces, apostrophes, dollar signs and backticks. A shell sentinel confirms the quoted value arrives intact and a fake wslpath that would fail is never called on Linux. Do not compare the helper with itself. |
| V-3 | `RemoteScriptContractTests.C980_Repo_root_assignment_converts_windows_paths` (new) | Execute the Windows-generated prelude with a fixture wslpath function that records argv and returns a literal POSIX path. Require `-u`, one intact native-path argument, and exact translated value in the following body for both variable names, including quoting edge cases. Real WSL conversion is independently exercised by V-1. |
| V-4 | `RemoteScriptContractTests.C980_Linux_shell_rejects_unconverted_repo_root` (new) | Exercise the actual preparation guard with native/forward-slash/escaped/case-variant embeddings, including a raw embedding plus a requested variable. Require the named exception before process launch; safe variable-only bodies and unrelated sample paths remain accepted. Linux bodies remain unchanged. Invalid variable names are refused. |
| V-5 | `RemoteScriptContractTests.C980_Repo_root_conversion_failure_stops_before_body` (new) | Execute the generated Windows prelude with a converter returning nonzero, empty output or a relative path. Each emits the conversion diagnosis and never executes a sentinel body; a successful absolute conversion does execute it. Use fixture roots, not production paths. |
| R-1 | `RemoteScriptContractTests.C905_Missing_linux_pwsh_skips_all_five_cases_before_a_script_block`; `RemoteScriptContractTests.C905_Linux_shell_finds_pwsh_when_installed`; `RemoteScriptContractTests.C946_Green_harness_removes_root_and_failed_keep_run_retains_evidence` | Forced missing pwsh still stops before the script; installed Linux probe succeeds; C946 keeps its cleanup/retention behavior after using the helper. The forced-missing test itself passes rather than contributing five TUnit skips. |
| R-2 | `RemoteScriptContractTests` (whole class) | All current cache/retirement/jq/cleanup contracts, including unchanged C1008 additions. Baseline 95 plus four additions = 99. Source inspection establishes the expected roster; fresh TRX establishes execution. |
| R-3 | `CodexRunnerImageContractTests`; `GrokRunnerImageContractTests`; `JqRunnerImageContractTests` (whole classes) | Every other current LinuxShell consumer: 11 + 26 + 14 = 51 expanded results. No consumer behavior changes from optional prelude/guard plumbing. |

TestDesign's positive controls are limited to these changed test-helper
contracts: bypass conversion (V-1 on Windows or V-3), remove quote escaping
(V-2/V-3), bypass raw-root rejection (V-4), or continue after failed/empty
conversion (V-5). Each must reach its intended assertion using an exact method
filter `/*/*/RemoteScriptContractTests/ExactMethodName`. A build/fixture error,
usage error in an unrelated method, or zero executed tests is not a positive
control. Do not schedule a whole-class mutation cycle. Their final PC inventory
and post-land execution belong to TestDesign/Mutation, not this Plan dispatch.

### Regression-only verdicts and Windows uncertainty

The ordinary scope is these four affected classes, not the full Unit/assembly
suite: this change is confined to a test shell helper and its explicit callers,
and no application layer or production script changes. This bounded profile is
the reason for omitting unrelated Unit tests, native Pty suites and rolling live
deployment tests. Source review must verify that scope remains true.

Do not declare new failures inherited from the C1008 source risk alone. At
Windows Debug, replay each extra failing exact method at the recorded pre-fix
base under the same environment, using a detached, caller-owned baseline
checkout/output and the build-slot gate. Record these conditional diagnostic
runs and their reason separately from the ordinary closed checkpoint list.
Never repeat the full assembly to classify one failure. Fix introduced
regressions; report confirmed inherited defects separately under the operator's
regression-only Review policy. They do not justify weaker assertions, skips or
timeouts. If confirmed inherited failures prevent CP-3's full-class green, the
five-method fix may be demonstrated but the full Windows acceptance remains
unmet; caller triages a separate repair and final-SHA replay before card closure.

### Execution and evidence

Freeze tracked source and report amendments, commit and push, then select only
the rows for the current lane in one checkpoint-tool run per committed S1
group. Use `run --plan <this-plan> --rows CP-1,CP-2 --expected-source-sha <full-sha>
--serial` on Linux; the separate Windows Debug selects `CP-3,CP-4` at the same
SHA. The current project default is a Debug build; record configuration with the
receipt, and never reuse Linux build outputs on Windows. No invented Platform
column is added to the importer: the caller selects rows by their stated lane.

Bootstrap `tools/Antiphon.Checkpoints` only if necessary, through
`pwsh -NoProfile -File scripts/build-slot.ps1 -Label c980-checkpoint-tool -- dotnet build tools/Antiphon.Checkpoints --property:OutputPath=bin-c980-tool/ --nologo`.
This tool bootstrap is the explicitly named administrative build, not an extra
test run. Invoke the built tool with `dotnet run --project
tools/Antiphon.Checkpoints --no-build --property:OutputPath=bin-c980-tool/ -- run
...`; the executor acquires each row's build slots itself. Await completion,
calling `wait` again if it returns 75. Do not end a task with a live executor or
edit source while it runs. Slot refusal/timeout is not permission to run unleased.

Preserve each unedited CHECKPOINT line, executed/passed/failed/skipped counts,
selected roster, actual source SHA and ignored TRX/source/JSON receipt paths in
the stored report. Require `dirty=0 sourceState=clean buildSource=verified` and
validate the receipt against the exact SHA with
`scripts/validate-checkpoint-receipt.ps1`. `Min` is only a floor: a zero exit with
extra skipped or unexpected/missing cases does not meet the table's exact
Expect criterion. Clean only the owned `bin-c980-*` directories after children
exit; generated logs/TRX/JSON/checkpoint folders remain ignored.

Code and read-only Review run `scripts/check-evidence-diff.ps1 -BaseRef <task-base>
-HeadRef <pushed-sha>` over the full task history. No service activation or live
SSH/Docker work is commissioned by any checkpoint here; the script tests use
their existing stubs and task-owned filesystem fixtures.

### Cost

Ordinary row floor: **42 minutes** (14 + 5 + 18 + 5), with 19 on Linux and 23 on
Windows, plus authoring, one tool bootstrap per host if needed, and build-slot
wait. The C1008 methods make full-Remote materially more expensive than just the
five original cases. This estimate is not a runtime measurement; TestDesign
may refine it from current receipts. Conditional baseline failure replays are
additional method-scoped diagnostics, never hidden reruns.

### Proposed checkpoints (superseded)

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---|---|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c980-linux-remote/` | linux-remote-contracts | `/*/*/RemoteScriptContractTests*/*` | V-1..V-5, R-1, R-2 | 99 executed/passed, 0 failed, 0 skipped; all five C849 and four C980 methods present | 99 | 14 | true | `C804_ORPHAN_SWEEP_ROOT=c980-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-2 | S1 | `tests/Antiphon.Tests -> bin-c980-linux-consumers/` | linux-shell-consumers | `/*/*/(CodexRunnerImageContractTests*)\|(GrokRunnerImageContractTests*)\|(JqRunnerImageContractTests*)/*` | R-3 | 51 executed/passed = 11+26+14, 0 failed, 0 skipped | 51 | 5 | true | `C804_ORPHAN_SWEEP_ROOT=c980-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-3 | S1 | `tests/Antiphon.Tests -> bin-c980-windows-remote/` | windows-native-debug-remote | `/*/*/RemoteScriptContractTests*/*` | V-1..V-5, R-1, R-2 | 99 executed/passed, 0 failed, 0 skipped; native Windows parent and real WSL; five C849 all passed at final SHA | 99 | 18 | true | `C804_ORPHAN_SWEEP_ROOT=c980-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-4 | S1 | `tests/Antiphon.Tests -> bin-c980-windows-consumers/` | windows-native-debug-consumers | `/*/*/(CodexRunnerImageContractTests*)\|(GrokRunnerImageContractTests*)\|(JqRunnerImageContractTests*)/*` | R-3 | 51 executed/passed = 11+26+14, 0 failed, 0 skipped; same final SHA as CP-3 | 51 | 5 | true | `C804_ORPHAN_SWEEP_ROOT=c980-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |

## Handoff

TestDesign freezes the four new witnesses and method-scoped PCs, validates the
checkpoint manifest, and reconciles the current shared-file roster before Code.
It preserves the five original Windows acceptance oracles and explicitly
accounts for the C1008 native-path risk; source inspection is not a substitute
for a Windows base replay. No decision or broader production change is needed
to start TestDesign.

## Verification design

Frozen by TestDesign task `1e3dddab-5727-4bcd-801b-b09bc8bd7ae2` on
2026-10-04. This appendix supersedes the provisional verification proposal and
its handoff; D-1 through D-4 and S1 remain the fix design. Only this plan is
changed. No build, TUnit execution, deployment or mutation was run by TestDesign.

### Inspection

- Read the full CARD-0980 description through `card.ps1 get CARD-0980 -Board
  Antiphon`, this plan, the testing/build checkpoint and mutation contracts, and
  the orchestration stage/landing contract. Assigned start:
  `6236cd04a10cdd36ed6867977e3e3ef57f484a32`; refreshed `origin/master`:
  `70d7e26128af77d1440d873f297c66af5567fb08`. The relevant Remote class, three
  image-contract consumers and deploy script have no diff between these refs.
- Read all five named C849 bodies; C946 cleanup/retention; both C905 bodies;
  `RequireLinuxPwsh`, `HasLinuxPwsh`, both cached prerequisite probes,
  `RequireLinuxJq`, `LinuxShell`, `Remote`, `Block`, `Order`,
  `CacheSeedTreeHarness`, `CacheStatusHarness`, `CachePrepareHarness` and
  `CachePruneHarness` in `RemoteScriptContractTests.cs` | root/repo, native
  Windows versus Linux, tool present/missing, donor archive/directory, false/true
  retired-container state -> V-1..V-5 and R-1..R-2. Retain every original oracle.
- Read `DelegateScriptRunner.RepoRoot` and `RunAsync`; the complete
  `C1008HostFixture` in `RollingVolumeRecycleScriptTests.cs`, and Remote's
  `C1008GitGraph` | native filesystem discovery remains native; C1008 launches
  bare bash with native paths -> Windows baseline procedure below. This is a
  risk, not evidence of an inherited failure.
- Read the three `Infrastructure/*RunnerImageContractTests.cs` consumer bodies
  and their shell helpers (Codex `StripVerifierCapture`, initializer cases;
  Grok `GrokRow`; jq version-row vectors), and `ProcessSpawnLimit` | optional
  argument omitted, arbitrary script output/nonzero exits, UTF-8/CRLF -> R-3.
  These files are unchanged. New tests use this class's existing shell fixture;
  no new test or fixture file is planned.
- Read `PlanTableImporter`, `ManifestValidator`, `CheckpointManifest`,
  `AfterSelector`, `RowTimeout`, `Program.Import`, `CheckpointImportTests`,
  `CheckpointManifestTests` and their fixture/driver setup | first exact
  Checkpoints heading, escaped pipes, roster extraction, counts, serial child
  environment and timeout derivation -> the imported manifest below.
- Recounted declarations and method-level Arguments at refreshed master:
  Remote **77 methods / 95 results**, with expansions 8 (C951), 3 (C957),
  6 and 5 (C973), hence 77+7+2+5+4=95. The four new single-result methods make
  **81 methods / 99 results**. Codex **11/11**, Grok **2/26**, jq **3/14**:
  **16 methods / 51 results**. There are no Matrix/data-source/Repeat/static
  Skip/Explicit attributes in these classes. Dynamic prerequisites still apply.
- CARD-1008 is present in the inspected source; CARD-1012 at `70d7e261` raises
  **CARD-0927 CP-2 to 163 = DockerStack 112 + Codex 11 + Grok 26 + jq 14**.
  This card adds four Scripts/Remote results, so **163 is unchanged**. It changes
  this plan's Remote floor from 95 to 99; it does not amend historical receipts
  or CARD-0927's older CP-3 table. No tests are added to
  **Antiphon.Tests.Checkpoints**; `scripts/lib/checkpoint-usage.ps1` census
  literal **377 remains unchanged**. Four additions also enter the broad Unit
  roster through the existing class attribute, without changing an unrelated
  frozen Unit floor here.

Missing setup is explicit: the four C980 methods and preparation helper do not
exist yet; Code creates them. This mirror cannot establish desktop WSL setup or
native Windows results. Debug must inspect the noninteractive
`wsl.exe -e bash -s` lane for `wslpath`, `/usr/local/bin/jq` version **1.7.1**,
and `/usr/local/bin/pwsh` version **7.6.6**, and record resolved paths/versions.
Also check bash, tar, perl, node, git, flock and standard GNU filesystem tools
needed by the existing fixtures. Missing prerequisites make the affected proof
incomplete; do not install, rewrite PATH, widen timeouts or add a skip here.

### Delivery inventory

There are **zero new or changed asynchronous delivery paths**. S1 changes a
synchronous test-owned shell invocation and its preparation; no queue, producer,
recipient session, durable identity, persistence handoff or recovery worker is
changed. Busy/eligible-recipient, crash/enqueue-recovery and complete UserPrompt
tests therefore have no applicable path. No request, stdout marker or checkpoint
receipt is represented as session delivery evidence.

The V-3/V-5 converter function is a declared substitute for `wslpath`: it proves
argument/prelude behavior, not desktop mount translation or PowerShell opening a
real file. The five V-1 cases on native Windows supply that missing real boundary.
The existing Docker/HTTP/verifier fixtures prove script contracts only, not a
live deployment, cache publication or caller delivery. No delivery verdict rests
on those substitutes.

### Proves it works now

Keep the four names from the proposal; each is exactly one `[Test]` result with
internal vector loops, no Arguments/Repeat expansion. All new methods that
start bash/WSL carry `[ParallelLimiter<ProcessSpawnLimit>]`. The pure preparation
helper accepts explicit body, optional variable, native root and Windows flag;
tests exercise that same helper called by `LinuxShell`, not a second copy.

- **V-1:** real file reachability and unchanged behavioral assertions | existing
  native TUnit -> bash/WSL -> Linux pwsh integration | CP-1 and CP-3 run
  `RemoteScriptContractTests.C849_Cache_cases_use_only_the_validated_host_lane`,
  `RemoteScriptContractTests.C849_Seed_publishes_complete_payloads_before_its_marker`,
  `RemoteScriptContractTests.C849_Saved_donor_archive_is_checked_and_imported_without_a_container`,
  `RemoteScriptContractTests.C849_Saved_donor_rejects_unsafe_archives_missing_pack_and_busy_counters`,
  and `RemoteScriptContractTests.C849_Saved_donor_rejects_declared_size_bomb_before_writing`.
  Require five passed, zero skipped, including the SHA-validation diagnostic,
  false/true retired-container case sequence, donor payload/ownership/recovery
  assertions, unsafe-archive/busy-counter refusals and size-bomb-not-written.
  Merely losing the pwsh usage error is insufficient.
- **V-2:** Linux bytes and literal quoting | pure preparation plus real shell |
  `RemoteScriptContractTests.C980_Repo_root_assignment_preserves_linux_bytes`.
  Cross both `root` and `repo` with plain POSIX roots and a root containing spaces,
  an apostrophe, `$cash` and backticks. Compare UTF-8 bytes to independently
  written legacy assignment/body literals before shell execution; preserve body
  LF/CRLF and a missing final newline. Label the equality `c980-linux-bytes`.
  Do not derive expected bytes from the preparation helper or its quote routine.
  A shell body prints the exact variable as base64 (`c980-linux-value` assertion);
  a fake wslpath prints `C980_UNEXPECTED_CONVERTER` if called, and its absence is
  asserted. Include no-variable Linux preparation equal to its input bytes.
- **V-3:** Windows conversion and argv integrity | generated Windows prelude
  executed by real bash with a local converter substitute |
  `RemoteScriptContractTests.C980_Repo_root_assignment_converts_windows_paths`.
  Cross `root`/`repo` with plain and quoting-edge native roots, native and
  forward-slash Windows spelling. The fake converter logs argc and each argument
  in an owned shell temp file, emitting only the independently specified POSIX
  path on stdout. Create that log empty before the prelude, so a missing call
  produces a count assertion rather than a missing-file fixture error. Assert
  exactly one call (`c980-converter-called`), two argv
  entries (`c980-converter-argc`), first exactly `-u` (`c980-converter-mode`), and
  second byte-identical to the native root (`c980-converter-arg`). The subsequent
  body prints the value as base64; require exact translated bytes
  (`c980-translated-value`). Use a translated path with spaces, apostrophe,
  dollars and backticks as well as a plain path. No hard-coded `/mnt/c` oracle.
- **V-4:** checked preparation and guard reachability | pure guard on both OS
  flags, plus actual Windows `LinuxShell` entry |
  `RemoteScriptContractTests.C980_Linux_shell_rejects_unconverted_repo_root`.
  Use a synthetic Windows root containing an apostrophe and spaces so the four
  spellings remain distinct. Cross native/forward-slash with literal/bash-escaped
  text, original/mixed case, and variable null/`root`/`repo` (24 rejection
  combinations). Require `InvalidOperationException` whose diagnostic says
  `RepoRoot` and `repository-variable` (`c980-raw-root-rejected`). Invalid variable
  names `""`, `ROOT`, `path`, `root;false`, `root\nrepo`, and `$(false)` require
  `ArgumentException` (`c980-variable-refused`); null means omitted, not invalid.
  Exercise rejection with a comment-only raw embedding as well as an assignment.
  Accepted controls: variable-only body, unrelated Windows path, safe omitted
  variable and both allowed names. Cross Linux flag false with all raw embeddings
  and require the old bytes (`c980-linux-raw-accepted`); reject invalid variables
  on either OS. On native Windows additionally call the actual `LinuxShell` with
  the current RepoRoot in a comment and a benign `C980_BODY_RAN` body, with and
  without a requested variable; require the exception
  (`c980-entry-raw-rejected`). This catches an unwired guard. Source review also
  requires preparation before `Process.Start`; stdout alone cannot prove that no
  process was created. No real script or destructive command is used in this check.
- **V-5:** conversion refusal before body | real bash over generated Windows
  prelude | `RemoteScriptContractTests.C980_Repo_root_conversion_failure_stops_before_body`.
  Cross `root`/`repo` with converter status 0/23 and outputs empty, relative,
  absolute. Only status 0 + absolute may run `C980_BODY_RAN`; all other combinations
  must emit `C980_REPO_ROOT_CONVERSION_FAILED` and omit that body marker.
  Capture the generated program in a subshell, merge its stderr into stdout and
  print its exit status from the outer fixture. Assert nonzero refusal and zero
  success, with labels `c980-status-refused`, `c980-empty-refused`,
  `c980-relative-refused` and `c980-success-ran`. Run status 23 + absolute before
  other error vectors so ignoring status is independently visible. No reliance
  on the caller having `set -e`. Missing converter (command-not-found/nonzero)
  is another refused vector. Install the substitute before the prelude, not in
  the body that must never run. In each negative vector, assert body-marker
  absence with its named refusal label first, then diagnosis and nonzero status;
  this fixes the precise intended red assertion for PC-7..PC-9.

Fixture constraints: setup/cleanup names use `c980_` variables so the prelude's
`root`/`repo` cannot overwrite the owned temp-directory identity. Preserve the
existing stdout-only return and general nonzero-exit policy of `LinuxShell`;
the V-5 outer fixture makes exit/diagnostic evidence observable without changing
that policy. Test roots are synthetic and never real credential or provider
paths. Every success vector reaches a body assertion, preventing always-refuse
implementations from passing. New methods run on Linux and native Windows;
only V-4's actual Windows-entry checks are conditional on the real OS.

### Guards the regression

- **R-1:** prerequisite and cleanup contracts |
  `RemoteScriptContractTests.C905_Missing_linux_pwsh_skips_all_five_cases_before_a_script_block`
  still calls all five methods, requires a caught SkipTestException and exact
  `NoLinuxPwshReason`, and restores its AsyncLocal in finally. It is one passing
  result, not five skipped results. Preserve `RequireLinuxPwsh` before fixture
  setup/conversion/execution in every migrated method.
  `RemoteScriptContractTests.C905_Linux_shell_finds_pwsh_when_installed` retains
  its existing Windows early return; it cannot prove WSL pwsh availability.
  `RemoteScriptContractTests.C946_Green_harness_removes_root_and_failed_keep_run_retains_evidence`
  must retain GREEN_EXIT=0, GREEN_ROOT_REMOVED, FAILED_KEEP_EXIT=1 and
  FAILED_KEEP_ROOT_RETAINED after migration to the `repo` argument.
- **R-2:** whole affected class | CP-1/CP-3 require precisely 99 passed results,
  including unchanged C1008, C905, C912 and C973 assertions; no failed or skipped
  results. Inspect the executed roster, not just MinExecuted. The nine named
  C849/C980 methods must be present once each. No blanket new process exit-code
  rejection, silent body rewrite, conversion cache or raw-root exemption.
- **R-3:** every other current helper consumer | CP-2/CP-4 require Codex 11,
  Grok 26 and jq 14 passed results with zero failed/skipped. Optional-argument
  plumbing must preserve their original script bytes and nonzero-output fixtures.

**Windows final-SHA gate and baseline classification.** The caller commissions
a separate native Windows Debug task with `-Platform Windows`, exactly the final
pushed Code SHA, and CP-3/CP-4 below. Both use Debug configuration and native .NET;
only child bash runs in WSL. Record `OperatingSystem.IsWindows()`/host OS,
configuration, WSL prerequisites, selected roster, source/build binding and all
four counters. Linux receipts and fake-converter tests cannot discharge this gate.
If source changes afterward, commission matching final-SHA Windows evidence.

For every additional C1008 or other unexpected Windows failure, replay the exact
failing method at Code's recorded pre-fix start SHA in a separate caller-owned
baseline checkout, under the same native host, WSL prerequisites, Debug mode and
build-slot gate, with a fresh isolated output. When Code starts from this freeze
tip, that tip is the base; do not substitute `70d7e261` merely because it was the
counting reference. A matching base failure is inherited; a base pass is an
introduced regression. Fixture/build failure or an unexecuted base method is
unclassified. Keep these conditional method-only diagnostic runs outside the
ordinary four rows, with reason, base/final SHAs and receipts. Regression-only
Review may separate inherited defects; it may not relabel CP-3 green or close the
99-result Windows acceptance while that row is red. Repair/triage those defects
separately, without weakening assertions or broadening S1 into C1008 portability.

**Shared-file order:** CARD-0980 S1 first, then CARD-0983, then CARD-1010 S1.
Serialize their Code/land work on RemoteScriptContractTests and the deploy-script
area. Before each successor starts, reconcile the previous landed source and
recount selected results. Planning in parallel does not authorize concurrent
edits or replaying an older copy of the class. CARD-0980 changes no deploy script.

### Guard inventory

The inventory covers newly enforced helper contracts, their entry wiring, and
the five prerequisite refusals whose callers are migrated. Independent spelling,
status/value, argv and caller bypasses have separate controls. The unchanged
production cache/retirement safety guards remain R-2 regression scope and their
own cards' mutation obligations; this card does not claim to requalify them.

| Guard | Plan reference and safety-critical guard/invariant | Control |
|---|---|---|
| G-1 | D-1: repository variable is only root/repo; omitted null remains valid | PC-1 |
| G-2 | D-1: apostrophe escaping preserves one literal assignment | PC-2 |
| G-3 | D-1/D-3: Linux preparation preserves legacy bytes and adds no conversion | PC-3 |
| G-4 | D-1: Windows prelude actually invokes the converter | PC-4 |
| G-5 | D-1: converter direction is exactly -u | PC-5 |
| G-6 | D-1: the native root is one intact converter argument | PC-6 |
| G-7 | D-1: nonzero conversion status refuses even with absolute stdout | PC-7 |
| G-8 | D-1: empty successful conversion refuses | PC-8 |
| G-9 | D-1: nonempty relative successful conversion refuses | PC-9 |
| G-10 | D-2: reject native literal current-root spelling | PC-10 |
| G-11 | D-2: reject forward-slash literal current-root spelling | PC-11 |
| G-12 | D-2: reject native bash-escaped current-root spelling | PC-12 |
| G-13 | D-2: reject forward-slash bash-escaped current-root spelling | PC-13 |
| G-14 | D-2: compare Windows spellings without case sensitivity | PC-14 |
| G-15 | D-2: requesting a variable does not bypass raw-body validation | PC-15 |
| G-16 | D-1/D-4: real Windows LinuxShell entry selects Windows preparation | PC-16 |
| G-17 | D-2: real Windows LinuxShell entry applies raw-body validation | PC-17 |
| G-18 | D-3: missing-pwsh refusal preserves the exact named reason | PC-18 |
| G-19 | D-3: Cache_cases caller preserves the missing-tool skip | PC-19 |
| G-20 | D-3: Seed_publishes caller preserves the missing-tool skip | PC-20 |
| G-21 | D-3: Saved_donor_archive caller preserves the missing-tool skip | PC-21 |
| G-22 | D-3: Saved_donor_rejects_unsafe caller preserves the missing-tool skip | PC-22 |
| G-23 | D-3: Saved_donor_rejects_declared_size caller preserves the missing-tool skip | PC-23 |
| G-24 | D-2/D-3: raw-body rejection is Windows-only | PC-24 |

### Positive controls

Code runs ordinary V/R; Review judges these controls before land. Mutation runs
them after land at the SourceLanding SHA, with initial green discovery, then
**break / intended assertion red / restore / fresh build / green** per control.
Every defect below compiles; mutate helper/caller behavior, never the witness's
expected value. These all touch the same source file and **must not be batched**.
Restore exact bytes before the next PC; no timeout, fixture, compiler or zero-test
failure qualifies. MinExecuted is 1 for every method-only control invocation.

Exact filter dictionary (no class-wide PC run):

| Key | Exact --treenode-filter |
|---|---|
| FL | `/*/*/RemoteScriptContractTests/C980_Repo_root_assignment_preserves_linux_bytes` |
| FW | `/*/*/RemoteScriptContractTests/C980_Repo_root_assignment_converts_windows_paths` |
| FG | `/*/*/RemoteScriptContractTests/C980_Linux_shell_rejects_unconverted_repo_root` |
| FF | `/*/*/RemoteScriptContractTests/C980_Repo_root_conversion_failure_stops_before_body` |
| FR | `/*/*/RemoteScriptContractTests/C849_Cache_cases_use_only_the_validated_host_lane` |
| FS | `/*/*/RemoteScriptContractTests/C905_Missing_linux_pwsh_skips_all_five_cases_before_a_script_block` |

| PC | Compiling defect that breaks its one mapped guard | Exact method filter and decisive red assertion |
|---|---|---|
| PC-1 | Remove variable allow-list validation; an invalid name reaches preparation | FG: `c980-variable-refused` expected ArgumentException absent |
| PC-2 | Emit the native root without apostrophe escaping | FL: `c980-linux-bytes` differs before executing malformed shell text |
| PC-3 | Append a space before the Linux prelude's LF | FL: `c980-linux-bytes` differs |
| PC-4 | Replace the Windows converter invocation with a literal valid `/fixture/converted` assignment | FW: `c980-converter-called` expects one logged call, gets zero |
| PC-5 | Change converter option -u to -w | FW: `c980-converter-mode` expects -u, gets -w |
| PC-6 | Remove double quotes around the native-root expansion passed to wslpath | FW: `c980-converter-argc` expects 2; space-containing vector supplies more |
| PC-7 | Ignore converter exit status, retaining value validation | FF: `c980-status-refused` sees C980_BODY_RAN for status 23 + absolute output |
| PC-8 | Relax value validation to admit empty output (while still refusing nonempty relative output) | FF: `c980-empty-refused` sees C980_BODY_RAN for status 0 + empty |
| PC-9 | Relax value validation to admit any nonempty output (retain empty refusal) | FF: `c980-relative-refused` sees C980_BODY_RAN for status 0 + relative |
| PC-10 | Omit native literal spelling from the raw-root comparisons, retain other three | FG: `c980-raw-root-rejected` lacks exception for apostrophe-bearing native literal vector |
| PC-11 | Omit forward-slash literal spelling only | FG: `c980-raw-root-rejected` lacks exception for forward literal vector |
| PC-12 | Omit native bash-escaped spelling only | FG: `c980-raw-root-rejected` lacks exception for native escaped vector |
| PC-13 | Omit forward-slash bash-escaped spelling only | FG: `c980-raw-root-rejected` lacks exception for forward escaped vector |
| PC-14 | Use Ordinal instead of OrdinalIgnoreCase in the raw-root comparisons | FG: `c980-raw-root-rejected` lacks exception for mixed-case vector |
| PC-15 | Validate raw body only when repository variable is null | FG: `c980-raw-root-rejected` lacks exception for root/repo requested vector |
| PC-16 | In LinuxShell's actual preparation call pass isWindows=false | FR, **native Windows**: ShouldContain the existing reviewed-full-lowercase-SHA diagnostic fails on pwsh's raw-path usage output |
| PC-17 | For Windows calls with no requested variable, validate an empty body and forward the original body to the shell | FG, **native Windows**: `c980-entry-raw-rejected` lacks exception; benign body returns |
| PC-18 | Make RequireLinuxPwsh throw SkipTestException("C980_WRONG_SKIP") on its existing missing branch | FS: `skip.Message.ShouldBe(NoLinuxPwshReason)` fails |
| PC-19 | In Cache_cases, before RequireLinuxPwsh insert `if (ForceNoLinuxPwsh.Value) return;` | FS: `skip.ShouldNotBeNull` fails naming C849_Cache_cases_use_only_the_validated_host_lane |
| PC-20 | Insert the same forced-missing return only in Seed_publishes | FS: `skip.ShouldNotBeNull` fails naming C849_Seed_publishes_complete_payloads_before_its_marker |
| PC-21 | Insert the same forced-missing return only in Saved_donor_archive | FS: `skip.ShouldNotBeNull` fails naming C849_Saved_donor_archive_is_checked_and_imported_without_a_container |
| PC-22 | Insert the same forced-missing return only in Saved_donor_rejects_unsafe | FS: `skip.ShouldNotBeNull` fails naming C849_Saved_donor_rejects_unsafe_archives_missing_pack_and_busy_counters |
| PC-23 | Insert the same forced-missing return only in Saved_donor_rejects_declared_size | FS: `skip.ShouldNotBeNull` fails naming C849_Saved_donor_rejects_declared_size_bomb_before_writing |
| PC-24 | Apply the raw-root comparison on Linux as well as Windows | FG: `c980-linux-raw-accepted` expected unchanged bytes instead gets rejection; assert acceptance through Should.NotThrow first |

PC-8/PC-9 are semantic relaxations of the whole value predicate, not deletion of
a redundant subcondition: an empty result also fails an absolute-path check.
The mutants must respectively admit empty and nonempty-relative results to
exercise independent refusal boundaries. For PC-10..PC-13 use the apostrophe
fixture so literal and escaped variants cannot collapse to the same string.
PC-19..PC-23 deliberately return before child execution, producing the existing
decisive missing-skip assertion rather than an unrelated shell fixture error.

Portable PCs (all except PC-16/PC-17) execute in the Linux Mutation lane. The two
entry-wiring PCs require a separately commissioned native Windows SourceLanding
Mutation at the same landed SHA, serialized with the portable snapshot task;
the caller preserves both obligations on the verification companion. The ordinary
Windows Debug proof is not a substitute for these post-land red/restore cycles.
Use fresh producer-owned `bin-c980-pc-N-red/` and `bin-c980-pc-N-green/` outputs
(N is the control number), exact dictionary filters, host build slots and external
SourceLanding evidence/restoration records. No commit/push from either snapshot.

### Out of scope

- No production deploy/cache/queue/session changes, live broker, SSH, Docker
  rollout, provider sign-in, Pty or full-assembly verification. The changed call
  graph is bounded to this helper and its four classes; full-class rows retain
  its existing fixtures and rejection paths.
- No changes to the global RepoRoot, C905/C912 skip policy, shell encoding,
  normalization, stream handling, 60-second timeout or general exit-code handling.
  Existing ForceNoLinuxPwsh shell sentinel and process-kill behavior remain
  unchanged; this card's PCs target the migrated caller skip contract instead.
- No general shell parser/security guarantee. Splitting a root across shell
  expressions, encoded paths, symlink aliases, UNC mount availability, newlines
  or NUL in repository names are outside the direct-embedding contract. Conversion
  failure must still refuse; success cannot assume a drive or WSL mount prefix.
- No baseline replay now, no Windows portability repairs or relabeled historical
  57/57 evidence. No checkpoint-namespace test additions or census-literal edit.

### Checkpoints

This is the sole active importer table. One isolated build and one exact filter
per row; its union is the whole ordinary scope. All rows follow committed S1.
Run one checkpoint-tool invocation selecting CP-1,CP-2 on Linux; commission
CP-3,CP-4 separately on native Windows at that final SHA, both Debug. Use the
existing execution/evidence instructions above, `--serial`, and await every run
and exit-75 continuation. Code may not substitute the Unit/full assembly suite.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---|---|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c980-linux-remote/` | linux-remote-contracts | `/*/*/RemoteScriptContractTests/*` | V-1..V-5, R-1, R-2 | Exactly 99 passed, 0 failed/skipped; all five C849 and four C980 methods present | 99 | 14 | true | `C804_ORPHAN_SWEEP_ROOT=c980-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-2 | S1 | `tests/Antiphon.Tests -> bin-c980-linux-consumers/` | linux-shell-consumers | `/*/*/(CodexRunnerImageContractTests*)\|(GrokRunnerImageContractTests*)\|(JqRunnerImageContractTests*)/*` | R-3 | Exactly 51 passed = 11+26+14, 0 failed/skipped | 51 | 5 | true | `C804_ORPHAN_SWEEP_ROOT=c980-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-3 | S1 | `tests/Antiphon.Tests -> bin-c980-windows-remote/` | windows-native-debug-remote | `/*/*/RemoteScriptContractTests/*` | V-1..V-5, R-1, R-2 | Exactly 99 passed, 0 failed/skipped; native Windows parent, real WSL, final SHA | 99 | 18 | true | `C804_ORPHAN_SWEEP_ROOT=c980-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-4 | S1 | `tests/Antiphon.Tests -> bin-c980-windows-consumers/` | windows-native-debug-consumers | `/*/*/(CodexRunnerImageContractTests*)\|(GrokRunnerImageContractTests*)\|(JqRunnerImageContractTests*)/*` | R-3 | Exactly 51 passed = 11+26+14, 0 failed/skipped; same final SHA as CP-3 | 51 | 5 | true | `C804_ORPHAN_SWEEP_ROOT=c980-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |

Importer contract: `ExpectText` retains the prose but does not enforce exact
counts, zero skips or the nine named methods. The actual `Expect` tokens come
from `RosterTokens(Filter)`: using the exact Remote class yields its class token;
the escaped OR yields the three consumer class tokens. Code/Debug/Review must
check exact counts and the named method roster in the receipt/TRX in addition to
the tool exit code and Min floor. No automatic waiver for source drift.

Read-only import uses the real `PlanTableImporter.ImportFile` and
`ManifestValidator.Validate` APIs, with `isWindows=false` and `true`; no tool
executor, build or tests are launched. The pre-existing assembly loaded for the
final inspection is `/work/worktrees/task-a87b9e00/tools/Antiphon.Checkpoints/obj/Debug/net9.0/Antiphon.Checkpoints.dll`,
SHA-256 `98a06767409a7627b66a08ac063384f92a4b9ff6c2d2f5042326b4a1bb4b1914`;
its importer source matches this checkout. This is parser evidence only, not a
build-provenance receipt for this task. The frozen import result is four filter
rows/four distinct builds, S1, Min **99/51/99/51**, minutes **14/5/18/5**, repeat 1,
serial true and both environment entries intact on each OS; both imports and
validations returned successfully. Observed derived row
timeouts are **42/15/54/15** minutes. CP-3 emits the importer's advisory
`3 x EstimatedMinutes (54) exceeds the 45 minute row-timeout ceiling`; inspected
`RowTimeout` actually derives 54 and does not clamp. Retain that warning; do not
silently lower the estimate. No rows execute during this import.

### Cost

All numbers are **estimates**, not new measurements. Ordinary V/R floor (Code
obligation, including its commissioned Windows Debug) = CP-1 **14** + CP-2 **5**
+ CP-3 **18** + CP-4 **5** = **42 minutes**, including each isolated row build.
Linux share is **19**, Windows **23**. Allow **4 minutes** additional ordinary
setup/tool bootstrap (2 per host); ordinary setup + V/R = **46 minutes**, plus
authoring and broker wait. The administrative bootstrap is the only named extra
ordinary build and takes the build-slot gate if needed.

Mutation floor is **136 minutes**: **4** for two-host setup, **8** for initial
green discovery (Linux one isolated build plus exact FL/FW/FG/FF/FS invocations;
Windows one isolated build plus exact FR/FG invocations), **110** for 22 portable
cycles at **5 minutes** each (1 edit/restore + 2 red build/run + 2 restored
build/run), and **14** for PC-16/PC-17 at **7 minutes** each (1 edit/restore + 3
red build/run + 3 restored build/run). Filters and exact methods are the dictionary
above; every red and restored run is method-scoped. No class-wide PC allowance.

Total ordinary setup/build/V/R plus every PC setup/discovery/red/restore/green =
**46 + 136 = 182 minutes**, excluding authoring, reporting and slot wait.
Conditional baseline classification adds an estimated **4 minutes per distinct
failing Windows method**, including its isolated base build; it is not hidden
in the green floor. Do not run it without an observed extra failure.

Savings: replacing full-Remote red/green runs for the 22 portable and two Windows
PCs with these exact methods avoids an estimated **22 x (28-4) + 2 x (36-6) =
588 minutes** of mutation build/test time using this plan's row estimates. The
four ordinary builds save zero by reuse: per-row isolation is intentional, and
Linux outputs cannot establish Windows proof. No discretionary repeat after green.

Handoff audit: bodies read; **guards=24, mapped=24, missing=0, duplicate PC
maps=0**. Every PC names a compiling semantic defect, exact executable method and
decisive assertion; two require the explicitly commissioned Windows lane. New
tests remain an implementation obligation, not claimed execution. No unverifiable
seam or human product choice blocks Code.

--- next stage ---
next: code
handoff: Implement S1 from the pushed CARD-0980 TestDesign tip; preserve four single-result C980 witnesses and 24 PCs. Run CP-1/CP-2; commission native Windows Debug CP-3/CP-4 at final SHA (99+51 each OS). Base-replay extra Windows failures. Serialize CARD-0980, CARD-0983, CARD-1010 S1. Keep 163 and 377 unchanged; Review before land, PCs afterward.
artifact: docs/superpowers/plans/2026-10-04-card-0980-wsl-repo-root-plan.md
