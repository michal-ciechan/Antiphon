# CARD-1030: make the C1008 host fixture executable from native Windows tests

Date: 2026-10-04. Plan task: `8b690c54-961b-4c5a-bf1a-ca029611a7ee`.
Assigned branch: `feat/card-task-8b690c54`.
Assigned base: `8bb0cea045f5a89359b0ba55712417f02e31a42a`.
Inspected current master: `f7132d32976ae1d7d8bca5df9442c7c004f2667b`.
Read-only CARD-0980 implementation: `origin/feat/card-task-fa69124f` at
`2c310b370194d834a7457037b61bb006356b151d`.

## Outcome and stage boundary

The card's test-fixture premise is supported. Three fixture process boundaries
bypass the existing Windows WSL stdin transport and embed native Windows paths.
Repair them together with the data paths those processes consume. No inspected
mechanism requires a production change in `scripts/deploy-server2.ps1` or
`scripts/c590-remote.sh`.

This dispatch writes a plan only. It makes no implementation changes and runs
no builds, tests, deployments, or mutations. **Next: TestDesign**; the brief did
not fold that stage into Plan. The proposed verification section and checkpoint
table below are inputs to that separate freeze, not permission to skip it.
Decisions D-1 through D-8 are implementation choices within the requested scope;
no product/default decision is outstanding.

Owners read: `AGENTS.md`, `docs/project-context.md`, `docs/ops-http.md`,
`docs/orchestration-loop.md`, `docs/agent-card-lifecycle.md`, and the checkpoint,
build-slot, process-safety, and evidence rules in `docs/testing-and-build.md`.
The full live CARD-1030, CARD-0980, CARD-0983, and CARD-1010 descriptions were read.

## Ground truth

Coordinates below refer to the inspected master unless marked **C980 tip**.
The five principal source files have no diff between the assigned base and
that master: RemoteScriptContractTests, RollingVolumeRecycleScriptTests,
c1008-fake-docker.sh, c590-remote.sh, and deploy-server2.ps1. Thus reading the
local copies establishes the current-master behavior without moving this branch.

Abbreviations: **Remote** = `tests/Antiphon.Tests/Scripts/RemoteScriptContractTests.cs`;
**Rolling** = `tests/Antiphon.Tests/Scripts/RollingVolumeRecycleScriptTests.cs`;
**Fake** = `scripts/fixtures/c1008-fake-docker.sh`;
**Production** = `scripts/c590-remote.sh`.

| Card assumption / reported mechanism | What the source and evidence establish | Consequence |
|---|---|---|
| M-1: a Windows temp script is passed to system32 bash. | Rolling:440 creates a native temp root. Rolling:574-577 writes native `remote.sh` and starts **bare `bash`** with that path as its argument. Rolling:550-569 also embeds native Root and RepoRoot in shell text. Debug `22e766b4` resolved that bare command to `C:\Windows\system32\bash.exe` and reported the mangled path. | Confirmed wrong fixture boundary. Source does not itself select system32; PATH did on that Windows host. Select system `wsl.exe` explicitly and feed LF script input. |
| M-2: Git setup exits 128 with `set: -` / `unknown switch b`. | Remote:624-655 constructs a multiline `set -e; git init ...` program with native fixture paths, then passes it as one `bash -c` argument through the same bare launcher. Unlike LinuxShell:3963, it does not normalize CRLF. This code creates only fixture Git repositories. | Confirmed unsafe Windows launch/path/line-ending boundary in test setup. The historical error is consistent with it; the exact division between launcher reparsing and CRLF is **not independently reproduced here**. Native Debug must capture which command fails; do not claim `git init -b` itself is invalid. |
| M-3: flock is missing / the held sentinel is absent. | Remote:98-107 starts another bare `bash -c`, opens the lock and touches `held` using native Root, then polls native File.Exists. The holder does not capture stderr. Debug reported a missing sentinel. Production:3453-3458 uses Linux `$SERVER2_ROOT/locks/rollout.lock` and flock normally. | Confirmed wrong holder boundary; absent `held` is not proof that the flock executable is missing. Use the same converted root for holder and contender, capture exit/stderr, and retain the real lock oracle. |
| Converting only the script filename will fix all 12. | Rolling:517-521 and 539 store native paths in volume Mountpoint and container Mounts.Source. Remote:187-205 creates further bind paths. Production:3837-3839 requires canonical Linux mount paths and compares them to readlink output. | Translate fixture-owned path fields as well as process inputs; otherwise a repaired launch exposes further false refusals. |
| RepoRoot conversion can be reused as-is everywhere. | **C980 tip** Remote:4081-4113 has private `PrepareLinuxShellScript`, validates aliases root/repo, quotes the input, calls wslpath only on Windows, and rejects failed/nonabsolute conversion. Its guard rejects raw-root bodies before inserting the trusted assignment. Remote:4118-4158 retains stdin transport. | Reuse this preparation contract through a narrow internal test seam. Do not feed its prepared prelude back through LinuxShell's raw-root guard, weaken the guard, or change global RepoRoot. |
| Git audit consumers use only native file paths. | Remote:255-261 hashes native `Path.Combine` results, but Production:3818 hashes the Linux `$repo`. Remote:320-322 reads a Git-created `.git` file and uses its path in native File.WriteAllText. Remote:306 creates a native symlink. | Windows expected hashes must use shell paths; write the linked Git index lock and create the Linux symlink through the fixture's WSL transport. Retain the same audit/refusal assertions. |
| A single Docker-state serialization point is sufficient. | Rolling:544 serializes Docker; Remote:422-424 separately writes `recreated.json`. ReloadDocker at Rolling:449-453 reads the shell-mutated model back. Fake:8 and 87-98 then read local files and run the production Git audit. | Apply the schema-aware path adapter at both outputs, accept already-POSIX paths without double conversion, and preserve reload/resume semantics. |
| The file-backed Docker fake is independent of path quoting and checkout EOL. | Fake:3-4 is an executable bash file, and Rolling:556 executes the checkout copy directly. Fake:87 inserts the fixture work path into the audit program without shell quoting. Fake:92-98 resolves Git and bash in the Linux child. | Windows gets an LF/no-BOM fixture-local copy. Cover roots with spaces/apostrophes and prevent shell expansion in the test-only audit remap; keep the Linux remap bytes unchanged. |
| A production deployment launcher is implicated. | `deploy-server2.ps1:42-56` uses the explicit C727 stub in tests, otherwise SSH to a Linux host with Linux paths. Production:3453-3458 owns the actual flock. The three broken launches above are all in C# tests; the fake never contacts a daemon (Fake:2,106). | Production scripts remain byte-identical. If an actual product defect emerges after fixture repair, report its independent evidence and commission its scope separately. |
| All 12 reported failures were introduced by CARD-0980. | Stored Debug `22e766b4-4f3b-4481-8924-c9fa2af68b70`: tip 99 executed / 87 passed / 12 failed / 0 skipped; base `17485f615bdc38c28214b1ab0dca103f929ab86b` 95 / 78 / 17 / 0. All 12 match at base after normalizing random fixture IDs. Its consumer row passed 51/51. | Historical inherited-failure evidence, not a new run. CARD-1030 must make these repaired cases pass; inherited classification is not completion of this repair. |
| Required Remote count is 95+4. | Current master has 77 Test methods, including four parameterized methods with 22 total arguments: 73+22 = 95 results. C980 adds four single-result methods: 81 methods / **99 results**. Rolling has eight singleton methods, four directly using C1008HostFixture. | Keep the 99 Remote results. Add new portability witnesses in a separate class so 95+4 remains independently checkable. |
| Shared census needs an increment. | `scripts/lib/checkpoint-usage.ps1:114` has literal **377**, for Antiphon.Tests.Checkpoints. | All additions here are in Scripts; leave 377 and CARD-0927's separate 163 floor untouched. |

The native Windows host is not accessible from this mirror. Static confirmation
above establishes the broken boundaries, not a fresh execution of the reported
Windows parser diagnostics. The first Windows proof must retain the native
parent identity, actual executable/argv, input EOL, translated paths, holder
exit/stderr, and tool versions to distinguish these mechanisms without guessing.

## Decisions

### D-1: one atomic test-only repair after CARD-0980

Implement one coherent slice. A path-only fix that leaves Git setup or the lock
holder broken cannot qualify the existing Remote class. Preserve CARD-0980's
four witnesses and raw-RepoRoot guard; make its pure preparation seam internal
only if needed by the adjacent fixture. Do not refactor the shared LinuxShell
transport or its consumers merely to share process boilerplate.

Expected implementation scope:

- `tests/Antiphon.Tests/Scripts/RollingVolumeRecycleScriptTests.cs`: fixture
  shell transport, fixture path conversion/serialization, and owned cleanup.
- `tests/Antiphon.Tests/Scripts/RemoteScriptContractTests.cs`: the three
  fixture-boundary callers and the path-sensitive audit/setup lines above;
  narrowly expose the C980 preparation seam without changing its behavior.
- New `tests/Antiphon.Tests/Scripts/C1008HostFixturePortabilityTests.cs`:
  four focused singleton witnesses described below.
- `scripts/fixtures/c1008-fake-docker.sh`: only the Windows-fixture branch of
  its audit-path remap if required for quoted roots. This is test code, despite
  living under scripts. No Docker commands may escape the fake.

Rejected: production deployment changes; a repository-wide process framework;
disabling assertions; skipping these tests on Windows; copying old pre-C980
Remote source; folding CARD-0983 or CARD-1010 behavior into this repair.

### D-2: distinguish native filesystem paths from Linux shell paths

Keep Root, StatePath, and all native .NET file operations native. Obtain a
validated shell root with `wslpath -u` in the selected WSL distribution, retaining
the C980 exit-status and absolute-result checks. Do the same for RepoRoot when
the Windows child needs repository files. Cache only in the fixture instance;
no static path cache, host path constant, mount-prefix substitution, or global
RepoRoot change. Conversion failure aborts setup before the body runs.

For Windows shell bodies, use quoted shell variables or a single tested literal
encoder; never interpolate raw native roots. For JSON, clone and transform only
known path-bearing fields: volume Mountpoint and container Mounts.Source under
the fixture's native root. Preserve unrelated absolute POSIX/foreign values,
missing or malformed fields, options, labels, booleans, and injected faults.
Require a path-component boundary when matching the root. Already-translated
values survive ReloadDocker and repeated Run calls unchanged. Apply the same
adapter to `recreated.json`; do not perform a textual replace over arbitrary JSON.

The expected audit hashes use UTF-8 bytes of each actual Linux repository path,
including linked, standalone, and bare layouts. The Windows linked-worktree
index-lock setup runs in WSL (resolve the Git directory there and write there).
Create the escaping-link fixture as a Linux symlink in WSL, and remove that
owned link there if native cleanup cannot remove its representation. No Windows
privilege requirement or symlink-policy change is added.

Rejected: treating slash replacement as conversion; hashing native Windows
paths while production hashes Linux paths; converting every JSON string;
weakening identity/canonical-path guards to accept Windows fixture data.

### D-3: preserve Linux program bytes and fixture semantics

Retain the native Linux branches of Run, Git-program construction, holder
program construction, Docker-model serialization, and fake audit remapping.
The new Windows preparation must not normalize or quote the Linux branch
differently. TestDesign freezes independent legacy literals from the post-C980
base for byte comparisons, including the composed injection, Git program,
holder body, and representative JSON. A helper compared with itself is not
preservation evidence. C980's V-2 remains baseline-green by design.

Windows stdin and the copied fake script are UTF-8 without BOM with CRLF changed
to LF. Do not edit checkout EOL attributes, production source bytes, Git behavior,
Git init flags, audit programs, expected refusal codes, timeout thresholds, or
retry policy to obtain green. The fake's Windows audit remap must safely refer
to the fixture work directory, for example by using a quoted environment-based
path at the known `/work` operand. Retain `/worktrees` and unrelated scratch text.

Executable/environment qualification is outside the byte comparison: select
the intended interpreter explicitly and pass child-only tool resolution as
D-4 describes. This must execute the same Linux fixture programs and effects.
It does not authorize changing global PATH or relying on a particular host PATH.

### D-4: explicit WSL transport and child environment

Windows launches the absolute system `wsl.exe` with `-e /bin/bash -s`, using
ArgumentList and redirected stdin/stdout/stderr. Never select Windows `bash`
from PATH and never send a multiline program as a Windows `-c` argument.
Native Linux uses an explicitly qualified Linux bash executable while preserving
the existing program/argument payload. The test harness owns the executable
selection seam; tests can supply a fixture executable without global overrides.

Do not assume .NET ProcessStartInfo.Environment entries automatically cross
into WSL. The Windows stdin bootstrap explicitly exports the fixture's existing
C590_CASE, C590_SHA, C590_RUN, C590_REEXEC, and C604_SERVER_ORIGIN values before
the production source's first parameter checks. Preserve fixture fakes and
never allow a failed setup to fall through to real Docker, sudo, SSH, or HTTP.

Qualify Linux tools through a child-only known tool search list, including
`/usr/local/bin`, `/usr/bin`, and `/bin`, then use resolved absolute executables
where processes are selected. Negative and launcher-selection witnesses supply
their own child PATH/tool stubs: installed host tools must not decide a negative
case. In particular, a poison `bash`/`git` in the supplied Windows PATH must not
be run. Do not read or modify the user's shell profiles or persistent PATH.
The fake Git-fault shim remains first only in its owned child environment.

The ordinary proof is tool-required: missing bash, wslpath, Git, node, flock,
jq, or pwsh is an explicit prerequisite failure/incomplete row, not green via
skips or fallback to Git Bash. Preserve unrelated C905/C912 skip contracts.
The expected Windows WSL jq/pwsh versions are **1.7.1** and **7.6.6**, resolved
under `/usr/local/bin`; verify them, do not install packages in this task.

Rejected: ambient PATH command discovery as a test oracle; globally editing
PATH; WSLENV dependence; guessing a distro or `/mnt/c`; executing the entire
.NET test host inside WSL, which would not exercise IsWindows().

### D-5: retain the real lock and process-ownership proof

Both holder and contender must use the same converted fixture root and actual
Linux flock. Retain Remote's sequence: holder has `held`; contender has
`lock-wait`; no stop/rm trace while held; contender remains pending; release;
contender exits successfully. Do not replace this with a sleep or fake flock.
Capture holder stderr and early exit so startup failure is not reported merely
as a missing sentinel. Preserve the existing ten-second readiness/release bounds
and Run's thirty-second bound; do not widen them to mask setup errors.

The holder needs stdin for its later release. On Windows, materialize an owned
LF holder file and send a short `source` bootstrap through `bash -s`; leave the
pipe open while that file's `read` awaits release. Send release only after the
sentinel, then close stdin. Do not send a whole stdin program and close it before
`read`, or let the shell parser consume the release as another command.
TestDesign must witness this handshake, including failure cleanup. Drain both
output streams concurrently; await every owned process; kill-tree and await
only that owned process on failure. Preserve existing exit codes and diagnostics.

### D-6: required final-SHA native Windows proof

Linux Code completes its rows, commits/pushes all fixes and tracked notes, and
returns the exact final implementation SHA C. The caller then commissions a
separate **Debug with `-Platform Windows`** at C for CP-4 through CP-6. Omit
`-Runner` unless deliberately pinning a host. Use native Windows .NET with WSL
only for shell children. Separate Debug is a handoff, not sub-delegation by Code.

All **99 = 95+4 Remote results** must execute and pass with zero failed/skipped,
including the 12 originally failing cases and all four C980 witnesses. Add the
four new portability results and four direct Rolling consumers as separate
rows. A Linux pass, the old Windows Debug report, or an inherited classification
cannot replace this final-SHA Windows result. If a fix follows Debug, rerun
affected rows against the new committed SHA; never relabel old receipts.

### D-7: fixed source order and regression-only Review

Use the requested order **CARD-0980 -> CARD-1030 -> CARD-0983 -> CARD-1010 S1**.
Finish ordinary Review and land each shared-file change before the next Code
owner edits it. The caller controls dispatch/land; this Plan task changes no card
state or sibling work. Re-read current source at TestDesign and Code admission.

Review findings follow the operator's regression-only policy: product/test
behavior broken by the delta, a build break, weakened safety oracle, or new
reachable exposure is a finding. Independently reproduced inherited defects
and design preferences are disclosures. Required proof still must be reported
as met/unmet; a clean regression verdict cannot fabricate missing Windows green.
No production restart or deployment is needed for this test-only change.

### D-8: evidence and custody

Commit/push coherent source before long runs. Keep source frozen during each
run. Preserve generated receipts/TRX/logs under ignored task-owned evidence;
commit only an optional small Markdown note before final qualification. Store
unedited CHECKPOINT lines, source SHA, counts, and validation in the final task
report. Run `scripts/check-evidence-diff.ps1` over the full Code/Review range.
Clean only the producer-owned alternate outputs and fixture children. Leave
census 377, checkpoint tooling, runner settings, and other worktrees alone.

## Slices

| Slice | Files and work | Tests / completion boundary |
|---|---|---|
| S1: Windows fixture boundary | Remote, Rolling, new C1008HostFixturePortabilityTests, and the Windows-only fake-Docker audit remap described in D-1. Expose only the C980 preparation accessibility needed, translate shell/data paths, use stdin/environment bootstrap, retain the holder handshake, and repair Windows audit-path consumers. | CP-1..CP-3 on Linux from committed S1; CP-4..CP-6 in separate native Windows Debug at final SHA C. Every original C1008 assertion remains. Commit/push the coherent slice before these runs. |

TestDesign first freezes the witness seams, independent byte baselines, exact
counts, and method-scoped controls. It may split authoring commits for red-first
evidence, but a partial launcher repair does not close S1. New tests that specify
new behavior need a meaningful failing assertion on the old boundary; the Linux
preservation witness deliberately passes at base. Compiler/fixture errors and
zero discovered tests do not count as red. No full-suite or Pty run is justified
by this bounded change; any additional driver needs a stated reason.

## Placement, sequence, and collisions

GET `/api/runner-defaults` and GET `/api/session-runners` were read during Plan.
Defaults revision was 2, with an automatic Linux preference; a Windows entry and
a Linux entry were available/eligible, and a temporary entry was unavailable
and draining. These are observations, not fixed fleet locations. Read both
routes again before dispatch; also read pipeline/host occupancy as required of
the orchestrator. Portable Plan/TestDesign/Code omit Platform and Runner;
native Windows Debug requires Platform Windows. Platform Any unpins an inherited
OS constraint. Checkpoint groups below explicitly identify their lane.

The board-scoped task listing and task detail endpoints were read; null scope
fields were not treated as proof of no collision. Fetched branch file footprints
and plan/freeze files provide the comparison below. Repeat before implementation.

| Work | Inspected state / collision |
|---|---|
| CARD-0980 Review `458db087` | Listing said Dispatched, reviewing C=`2c310b37`. Its Code branch is not yet in inspected master. Direct overlap in Remote; wait for its reviewed land. Its four new witnesses and conversion guard are a required base, not code to cherry-pick into this Plan branch. |
| CARD-0983 Plan `b12e3f04`, TestDesign `896f3d65` | Freeze at `15a2601b8f6105c664ebfa7efa61ae12556a0a2c`, `docs/superpowers/plans/2026-10-04-card-0983-require-jq-plan.md`: seven new Remote results plus a Rolling required-jq caller; six checkpoints. Direct overlap in **both Remote and Rolling**, so CARD-1030 precedes it. Reconcile that freeze's old C980->C983 ordering and recount after this card. Do not add RequireJq here. |
| CARD-1010 Plan `adbad12b` | `2395858ecc0a6ab2e8acc3b2deb2cb6298ed1307`, `docs/superpowers/plans/2026-10-04-card-1010-runner-state-cache-optins-plan.md`, explicitly awaiting separate S1 TestDesign. **Correction to the brief's shared-file assumption:** current S1 is server admission (PhoneHomeContracts/PhoneHomeRunnerDirectory), not Remote. S2/S3 own Remote/Rolling/deploy script overlaps and need later freezes. Preserve the requested overall order; do not assert a nonexistent S1 Remote edit or an already-completed freeze. |
| CARD-1017 Code `c37846cc` | Listing Working; inspected tip `e3bb9aa67520508b58419e6169fb92d81c9de16e`. Current footprint is cleanup services, persistence/migrations, and CardDone cleanup fixtures, with no intersection with D-1. Its Done-generation/evidence-removal ownership remains separate. Generated evidence stays ignored; pushed plan and stored report are the durable handoff. |
| CARD-1020 Code `1d3e0d92` | Listing Dispatched; stored result says Linux implementation complete, Windows pending, C=`6a019da5f3c8240b7ca9ffee21423fe5ba81bcf8`. Diff is DirectSessionRunnerClient/TestOwnedPtyHost and Agent tests, not these Script fixtures. Share Windows/build-slot capacity only; do not change its teardown or production linger. |
| CARD-0959 lesson | Missing-tool, launcher, or negative assertions must use explicit child resolution and test-owned stubs, never installed host PATH as the oracle. This plan adds no CLI/provider capability work. |
| CARD-1025 / CARD-0927 | Host jq availability and runner-image qualification are separate ownership. A missing jq in the current Linux worker is a prerequisite gap to report, not justification to alter the test or perform a host installation. |
| Census / policy work | No Checkpoints namespace additions; keep literal 377. No change to the separate image-contract floor 163, evidence policy, or checkpoint tool. |

## Verification design

This is a concrete proposal for the separate TestDesign stage. Freeze the four
new singleton method names below, their observable assertions, and compiling
method-scoped positive controls before Code. All process-spawning tests carry
the assembly-local ParallelLimiter<ProcessSpawnLimit>. No test contacts a real
Docker daemon, SSH host, provider, or production HTTP service.

| ID | Class / method or coverage | Required witness |
|---|---|---|
| V-1 | `C1008HostFixturePortabilityTests.C1030_Linux_programs_preserve_legacy_bytes` (new) | Independent pre-change LF/CRLF/native-path vectors compared byte-for-byte against actual Linux Run injection/source, Git and holder programs, representative JSON, and unchanged Linux fake remap. No converter invocation on Linux; no self-comparison. |
| V-2 | `C1008HostFixturePortabilityTests.C1030_Windows_paths_convert_before_fixture_effects` (new) | Actual preparation uses wslpath -u with one intact native argument; spaces, apostrophes, dollar signs, backticks, Unicode, forward-slash Windows spelling, and already-POSIX input. Failed/empty/relative conversion never runs a body sentinel. Unsupported/unmapped paths fail explicitly. A fake converter proves argv/refusal; the native Windows class proves real reachability. |
| V-3 | `C1008HostFixturePortabilityTests.C1030_Windows_transport_ignores_ambient_launchers` (new) | Actual launch specification selects absolute wsl.exe and bash stdin, no Windows bash -c/native script argument; LF/no-BOM input; all five C590/origin values arrive before source reads them. Fixture-owned poisoned child PATH is never executed. Exercise Git setup exit/output and a live holder release through the real shell transport, including disposal after failed setup. Do not replace the actual entry wiring with a second toy launcher. |
| V-4 | `C1008HostFixturePortabilityTests.C1030_Fixture_path_data_preserves_faults_and_round_trips` (new) | Mountpoints, container/bind sources, recreated.json and reload map to the same fixture files; sibling-prefix and foreign paths/faults stay intact. Expected audit hashes use shell bytes; linked Git index-lock and symlink cases reach their original refusal. Exercise quoted roots through the fake audit remap. Verify fixture-local LF fake copy on Windows and owned cleanup. |
| R-1 | Whole `RemoteScriptContractTests` | **99/99**, including all 12 historical failures below, C980's four guards, its five C849 callers, and C905/C912 contracts. No failing/skipped result on the qualified lanes. |
| R-2 | Four direct Rolling consumers named below | **4/4**, preserving manifest rejection, WrongLane, refusal-receipt confidentiality, and retired-absent/null transport all the way into the host fixture. |
| R-3 | Diff and byte-baseline inspection | Production deploy-server2.ps1/c590-remote.sh unchanged; no native Linux fixture-body drift; no C980 helper semantic change; no census/tool/global-PATH change. Covered alongside each row and ordinary Review, not a separate build. |

The 12 originally failing singleton methods, all in RemoteScriptContractTests:

1. `C1008_Recycle_exact_default_volumes`
2. `C1008_Recycle_dry_run_never_mutates`
3. `C1008_Retire_temp_reclaims_below_cache_disk_gate`
4. `C1008_Retire_temp_rechecks_absence_and_retirement`
5. `C1008_Recycle_receipt_records_disk_and_partial_failure`
6. `C1008_Recycle_resume_requires_matching_receipt`
7. `C1008_Recycle_preserves_tmp_copyup`
8. `C1008_Recycle_refuses_uninspectable_git`
9. `C1008_Recycle_refuses_unpublished_and_dirty_work`
10. `C1008_Recycle_audits_work_as_1654`
11. `C1008_Recycle_refuses_references_and_unknown_census`
12. `C849_Deploy_prepares_and_verifies_before_acceptance`

R-2's methods in RollingVolumeRecycleScriptTests are
`C1008_Option_manifest_is_strict`,
`C1008_Documentation_and_transport_pins_match`,
`C1008_Refusal_receipts_do_not_leak_secrets`, and
`C1008_Retired_absent_null_is_accepted`. They are the four direct fixture
consumers; the other four Rolling methods exercise unchanged wrapper behavior.
Do not add the slow entire Rolling class merely to obtain a round count.
The 51 image-helper consumers need no additional row for a private-to-internal
accessibility change alone; a substantive shared LinuxShell change requires
revised scope and their 51-result rows on both OSes before execution.

TestDesign control candidates: bypass each Windows launcher branch, omit path
conversion/status/absolute checks, omit one exported C590 input, preserve CRLF
in Windows input, leave Mountpoint or bind Source native, omit recreated.json
adaptation, hash native paths, mishandle the linked Git path, remove holder
stdin retention/release, or alter one Linux program byte. Map each to a decisive
assertion with an exact `/*/*/Class/Method` filter, and distinguish a deliberate
preservation baseline from a red-first repair witness. No blanket class-wide
mutation run. Mutation remains separately commissioned; this plan runs none.

Execution protocol after the freeze:

1. Inspect prerequisites read-only through the **same noninteractive shell
   transport** that the tests will use; record native OS/runtime, bash, wslpath
   (Windows), Git, node, flock, jq, and pwsh versions/paths. Explicitly check
   `/usr/local/bin/jq` 1.7.1 and `/usr/local/bin/pwsh` 7.6.6 for Windows WSL.
   Check path conversion and a fixture-local file round trip. Missing setup is
   incomplete qualification, never an automatic package install or fake green.
2. On Linux use the checkpoint tool `run --plan <this-plan> --rows CP-1,CP-2,CP-3
   --expected-source-sha <C> --serial`. Await it and every exit-75 continuation;
   do not settle with a live executor. Bootstrap the tool through build-slot.ps1
   if needed, with producer-owned `bin-c1030-tool/` output and UseAppHost=false
   off Windows; this administrative build is the only planned extra build.
3. Separate native Windows Debug at C uses the same tool with
   `--rows CP-4,CP-5,CP-6 --expected-source-sha <C> --serial` (Debug configuration).
   The checkpoint tool takes each build/test slot; do not wrap its executor in
   a second held slot. An alternative direct run-checkpoint invocation is only
   by explicit Debug brief and takes its own slot. Slot timeout is not run;
   never retry outside the gate or with NoSlot.
4. Require fresh TRX, exact counts and named methods, clean source, verified
   build provenance, and zero skipped. Validate source receipts at C. Importer
   Expect prose/Min floors alone do not enforce exact counts or zero skips;
   inspect them explicitly in the report. Preserve unedited CHECKPOINT lines.
5. For additional red, compare only the failing exact method at the recorded
   post-C980 pre-fix base B and C in isolated owned checkouts. Use the same
   prerequisite environment. Record the matching assertion before calling it
   inherited; do not rerun the whole assembly or loosen an assertion. The old
   17485f61 run is historical context, not a substitute for current B evidence.
   The known 12 must pass at C to finish this card. Report additional unrelated
   inherited red separately under the regression-only policy.
6. Wait for children, remove only owned alternate-output inventory (forward
   slash OutputPath), and retain ignored evidence for the caller. Do not edit
   tracked reports after final receipts and then claim the new HEAD was tested.

### Checkpoints

One isolated build and one exact filter per row, all after committed S1.
Proposed ordinary scope is **214 results**: (4+99+4) on each OS. Recount at
TestDesign/Code admission against the integrated C980 source. A sibling land
that changes these rosters requires a documented freeze update, not a reduced
floor or a silent additional run. The table uses the repository importer schema;
TestDesign must validate it with the real importer for both OS settings.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---|---|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1030-linux-portability/` | linux-fixture-portability | `/*/*/C1008HostFixturePortabilityTests/*` | V-1..V-4, R-3 | Exactly 4 passed, 0 failed/skipped; all four C1030 witnesses | 4 | 4 | true | `C804_ORPHAN_SWEEP_ROOT=c1030-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-2 | S1 | `tests/Antiphon.Tests -> bin-c1030-linux-remote/` | linux-remote-contracts | `/*/*/RemoteScriptContractTests/*` | R-1, R-3 | Exactly 99 passed = 95+4, 0 failed/skipped; all 12 repaired methods and four C980 witnesses | 99 | 14 | true | `C804_ORPHAN_SWEEP_ROOT=c1030-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-3 | S1 | `tests/Antiphon.Tests -> bin-c1030-linux-consumers/` | linux-host-fixture-consumers | `/*/*/RollingVolumeRecycleScriptTests/(C1008_Option_manifest_is_strict)\|(C1008_Documentation_and_transport_pins_match)\|(C1008_Refusal_receipts_do_not_leak_secrets)\|(C1008_Retired_absent_null_is_accepted)` | R-2, R-3 | Exactly 4 passed, 0 failed/skipped; all four direct consumers | 4 | 8 | true | `C804_ORPHAN_SWEEP_ROOT=c1030-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-4 | S1 | `tests/Antiphon.Tests -> bin-c1030-windows-portability/` | windows-native-debug-portability | `/*/*/C1008HostFixturePortabilityTests/*` | V-1..V-4, R-3 | Exactly 4 passed, 0 failed/skipped; native Windows parent at final SHA C | 4 | 6 | true | `C804_ORPHAN_SWEEP_ROOT=c1030-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-5 | S1 | `tests/Antiphon.Tests -> bin-c1030-windows-remote/` | windows-native-debug-remote | `/*/*/RemoteScriptContractTests/*` | R-1, R-3 | Exactly 99 passed = 95+4, 0 failed/skipped; all 12 repaired methods and four C980 witnesses at C | 99 | 18 | true | `C804_ORPHAN_SWEEP_ROOT=c1030-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-6 | S1 | `tests/Antiphon.Tests -> bin-c1030-windows-consumers/` | windows-native-debug-host-consumers | `/*/*/RollingVolumeRecycleScriptTests/(C1008_Option_manifest_is_strict)\|(C1008_Documentation_and_transport_pins_match)\|(C1008_Refusal_receipts_do_not_leak_secrets)\|(C1008_Retired_absent_null_is_accepted)` | R-2, R-3 | Exactly 4 passed, 0 failed/skipped; same C and native Windows parent | 4 | 10 | true | `C804_ORPHAN_SWEEP_ROOT=c1030-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |

### Cost

Ordinary execution floor is an estimate, including each row's build:
4+14+8+6+18+10 = **60 minutes** (Linux 26; Windows 34), plus approximately
four minutes for two-host prerequisite/tool setup and authoring/slot waits.
No blanket repeat after green. Conditional method-only base replay is extra
and must be reported with the triggering failure. The 18-minute Windows Remote
estimate may produce the importer's advisory about a derived timeout above
45 minutes; preserve the estimate and have TestDesign inspect actual importer
behavior rather than silently reducing it. TestDesign separately costs the
method-scoped controls; no Mutation execution is included in this Plan dispatch.

## Handoff

The plan is complete; implementation and all new verification remain pending.
TestDesign must freeze the transport/path seams, four witnesses and controls,
independent Linux byte baselines, six-row importer acceptance, prerequisite
qualification, and final-SHA Windows Debug contract. Reconfirm C980's land and
the stated shared-file sequence before Code. Production-script edits require
new evidence and a separate scope decision; no such defect was established here.

--- next stage ---
next: test-design
handoff: Freeze CARD-1030's test-only WSL fixture repair after CARD-0980: paths/data/Git/lock transport, four portability witnesses, Linux byte preservation, and six checkpoints (4/99/4 per OS). Require separate final-SHA native Windows Debug, preserve 377, and serialize before CARD-0983 and CARD-1010 S1.
artifact: docs/superpowers/plans/2026-10-04-card-1030-c1008-windows-host-fixture-plan.md
