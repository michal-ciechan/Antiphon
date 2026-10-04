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

## Verification design

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

### Checkpoints

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

--- next stage ---
next: test-design
handoff: Freeze CARD-0980's test-only RepoRoot prelude/guard and four new witnesses; preserve Linux bytes and C905 skips. Recount 95 existing Remote cases plus four planned additions and 51 consumers. Keep separate final-SHA native Windows Debug rows, classify any extra C1008 failures at base, and serialize shared-file Code with CARD-1010.
artifact: docs/superpowers/plans/2026-10-04-card-0980-wsl-repo-root-plan.md
