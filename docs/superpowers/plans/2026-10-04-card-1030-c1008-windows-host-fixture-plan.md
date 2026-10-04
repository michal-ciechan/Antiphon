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

## Plan verification proposal (superseded by the appended freeze)

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

### Proposed checkpoints (archived)

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

### Proposed cost (archived)

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

### Native Windows triage amendment, task 94914cdf

The explicit Windows repair brief at source `9f1cd6d398f572f179c9983b78d52e987b9d6663`
authorizes replacing Windows fixture `git init -b` for Git 2.25.1 compatibility,
superseding only that restriction in D-3. Native Linux program bytes remain
frozen. Use init followed by symbolic-ref to select master before the first
commit. WSL startup must use a stable directory outside the disposable fixture
tree; owned children still exit before deletion. No timeout/retry change.

Ordinary scope for this repair is the brief's Windows CP-4..CP-6, plus exact
base replay and red-first repair guards. No whole Unit or Linux rerun is claimed.
V-3 now also observes the actual startup directory and runs Git setup through
a fixture-owned pre-2.28 compatibility wrapper, which rejects init -b and
forwards other operations to the resolved real Git. Both guards have ordinary
pre-repair assertion-red evidence; deliberate mutants remain Mutation's work.
Details: `docs/investigations/2026-10-04-card-1030-windows-triage-94914cdf.md`.

Two additional controls supplement the frozen inventory; all earlier PCs and
variants remain pending. This supersedes the earlier 52-control census with
54 controls. Each new control costs the same estimated nine minutes as P3's
existing controls (method-scoped baseline/red/restore-green). Mutation floor
becomes 437 minutes; prior ordinary/setup estimates are unchanged (509 total).

| PC | Compiling defect | Exact method red at assertion |
|---|---|---|
| PC-53 | Set Windows ShellStart.WorkingDirectory back to Root | `C1008HostFixturePortabilityTests.C1030_Windows_transport_ignores_ambient_launchers`, `c1030-cwd-outside-fixture`: actual startup directory is outside the disposable tree |
| PC-54 | Generate the legacy init -b command for Windows Git setup | `C1008HostFixturePortabilityTests.C1030_Windows_transport_ignores_ambient_launchers`, `c1030-portable-git-init`: compatibility wrapper rejection is captured and asserted absent; real commit is required |
| PC-55 | Omit clearing ReadOnly on owned Windows Git objects before native deletion | `C1008HostFixturePortabilityTests.C1030_Fixture_path_data_preserves_faults_and_round_trips`, `c1030-owned-cleanup`: owned root including read-only sentinel is removed; foreign target remains |

The first combined Windows run exposed read-only Git-object cleanup after init
was repaired. PC-55 supplements the two rows above: 55 controls, estimated
Mutation floor 446 minutes and prior combined estimate 518 minutes. Mutation
owns validating its labeled reachability and any missing-control discovery;
ordinary UnauthorizedAccessException evidence is not a completed PC cycle.

## Verification design

Frozen by TestDesign `9888335c-a881-4b9e-b21d-05a7216a3fd4`, 2026-10-04.
This append-only verification/admission update supersedes the Plan proposal,
its roster assumptions and D-7's dispatch order; D-1..D-6 and D-8's repair
remain the fix design. Only the old verification/checkpoint/cost headings were
renamed: the importer takes the **first exact `### Checkpoints` heading**.
No implementation, build, test execution, deployment or mutation occurred here.

Admission is now **CARD-0980 landed -> CARD-0983 reviewed and landed ->
CARD-1030 -> CARD-1010 S1**. Read-only comparison used CARD-0983 Code
`cb95a4c3c171df7a55bfd06266e17980712c68fb` on
`origin/feat/card-task-608ed47c`; Review `1df2bada-4173-4842-bda8-c93e29c2a385`
was Dispatched. CARD-0980's `2c310b370194d834a7457037b61bb006356b151d` is
an ancestor of inspected origin/master `97e697017ede03643576a5dba199b13c55457e5a`.
This task branch stays fast-forward-only from `740245f4`; it is not rebased.
At Code admission integrate the confirmed landed target by a normal merge
(or start a fresh caller-created Code worktree containing both this plan and
that target); record that integrated pre-fix commit as **B**. Landing owns any
subsequent rebase. Never restore the old Remote file wholesale. Preserve
C980's four tests/helper, C983's two methods/seven argument-expanded results,
its `C983Fixture`, and Rolling's `C1008_Legacy_rolling_and_jq_rosters_remain`
`-RequireJq` present-case change. Those are the actual overlap seams.
If C983 does not land first, return to TestDesign for a roster/admission update;
do not rewrite this pushed branch or silently use the archived table.

The source census is 81 methods / 99 results after C980 and 83 / **106** after
C983 (six arguments plus one singleton added). Consequently the frozen full-class
rows are **4/106/4 per OS = 228 results**, a deliberate update from proposed
4/99/4 = 214. Every original 99 result remains mandatory; seven added C983 passes
cannot offset one missing or failing original. The four new singleton tests
belong to `Antiphon.Tests.Scripts`, **none** to `Antiphon.Tests.Checkpoints`.
`scripts/lib/checkpoint-usage.ps1`'s census literal **377** remains unchanged.

### Inspection

Bodies read, not just declarations (coordinates in the Plan remain historical):

- Rolling's eight test bodies, `C1008Process`, complete `C1008WrapperFixture`
  and `C1008HostFixture` (constructor, Volume/Container, Run, ReloadDocker,
  Dispose) | shell/environment/JSON/cleanup -> V-1..V-4, R-2.
- Remote's eleven `C1008_*` tests, `C1008GitGraph`, and complete
  `C849_Deploy_prepares_and_verifies_before_acceptance` | Git layouts, real
  lock exclusion, data paths, resume and receipt-copy -> V-2..V-4, R-1.
- Post-C980 four witness bodies, `PrepareLinuxShellScript`, `LinuxShell`,
  C905 availability/skip helpers and witnesses | alias/raw-root/status/absolute
  guards and inherited Linux byte contract -> V-1/V-2, R-1/R-3.
- Post-C983 two test bodies, assertion helpers, complete C983Fixture and Rolling
  caller diff | seven new results retained; sealed PATH remains child-local ->
  R-1/R-3; no CARD-0983 implementation or assertion changes here.
- Complete `c1008-fake-docker.sh`, `c1008-recycle-cases.json`, C727 fake HTTP
  C1008 branch and verify rollout-lock/recycle branches; production
  `c1008_rollout_lock`, audit path hash and canonical-mount checks |
  file-backed fake, actual shell/flock/Git, quoted remap -> V-1/V-3/V-4.
- `DelegateScriptRunner` including RepoRoot, `ProcessSpawnLimit` |
  native filesystem ownership and assembly-local single process limit -> V-3/V-4.
  These existing fixtures are the nearest fixture for the new portability file.
- `PlanTableImporter` (all branches), `ManifestValidator`, `CheckpointManifest`,
  `AfterSelector`, `RowTimeout`, importer tests for table/filter/minimum/timeout
  semantics, plus testing owner checkpoint/slot/mutation rules | six-row import
  and exact execution accounting below; no new importer tests or census change.

Missing setup is explicit: the new test file and its narrow observation seams
are not implemented. Code must expose **actual** preparation/launch/serialization
used by Run, Git, holder and receipt-copy entries, with an injected executable
and per-instance root for witnesses. A second unused launcher/serializer cannot
qualify. Keep this within D-1's test files; production seams need a new Plan.
This mirror has no native Windows parent and no fresh WSL qualification. The
C983 Code report records missing Linux jq. Required tools must be qualified on
the executing host before CP admission; this task did not install them.

The stored Windows reports were read via task detail: `22e766b4` at `2c310b37`
ran all 99 (87 pass, the 12 below fail), with identical assertions at its base;
`80ff713f` at `cb95a4c3` ran C983 7/7, Rolling required-jq 1/1 and the
C849/C912/C973/C946 subset 40/41. Its one red was the same C849 fixture path
assertion, replayed at `15a2601b`. Thus cb95a4c3's report corroborates that
member; it does **not** claim a fresh run of all twelve. No additional
Windows-red test for another reason is established in these reports. Linux
missing-jq failures are not Windows failures. The five old C849 Windows-root
failures at pre-C980 base are already green at 2c310b37 and must stay green.
An unreported Windows failure is unknown until the method-only base replay
below; it is never preclassified as out of scope.

### Delivery inventory

No changed product queue, notification, session input, recipient or durable
asynchronous delivery path exists. Real-queue busy/eligible-recipient and
crash/enqueue recovery tests, and matching complete UserPrompt transcripts,
are therefore excluded: no such producer/handoff is touched. These tests must
not claim message delivery from a request, event, queue row, Sent flag or ack.

The asynchronous operations that *are* changed are owned local test processes:

| Producer -> recipient | Identity / persistence | Failure/recovery | Observable completion |
|---|---|---|---|
| Run -> bash/WSL -> fake Docker + real production shell body | fixture instance/native Root and converted shell Root; docker.json, statuses.json, tasks.json; fixed C1008 operation plus fixture root | conversion/start failure before body; owned timeout cleanup; repeated Run after ReloadDocker | child exit/output **and** exact native files/Removed/trace/refusal assertions, V-2/V-3/V-4 + R-1 |
| Git setup -> bash -> fixture-only Git repositories | same root; real .git/objects/refs and linked gitdir | failing Git returns captured exit/stderr; no audit after failed setup | published control, all four audit hashes, linked lock and escaping-link refusals, R-1/V-4 |
| Holder -> bash/flock and contender -> same flock inode | same converted `server/locks/rollout.lock`; held/lock-wait are fixture files | held stdin retained, explicit release, early exit diagnosed, kill-tree plus await on failure | held + lock-wait + contender pending + no stop/rm, then released contender success; V-3/R-1 |
| Receipt-copy PowerShell stub -> resumed remote shell -> copied receipt | same fixture root and operation journal; evidence/deploy-parent/recycle.json | failCopy=true then false; existing atomic-write failure/resume vectors retained | copied bytes equal host journal and exactly three original removals; R-1/V-3 |

The Docker/HTTP/SSH/scp substitutes prove script decisions and fixture file
handoffs only, not daemon execution, live HTTP, remote delivery or uid isolation.
Real Git/flock/shell children supply the relevant OS evidence. A launch-spec
spy proves argv/encoding/wiring, not execution; pair it with child-observed
values, real Git commits, lock contention and recipient file contents. Durable
operation receipt existence alone never satisfies those assertions. Every new
fixture protection in this inventory has a guard and PC below.

### Proves it works now

The names below are frozen single `[Test]` methods in
`C1008HostFixturePortabilityTests`, each with
`[ParallelLimiter<ProcessSpawnLimit>]`. Matrices are internal assertions, not
extra TUnit results. Tests run on both OSes; the native Windows rows must enter
`OperatingSystem.IsWindows()`. Linux uses the injected Windows preparation
branch plus its real Linux transport; that substitute cannot prove wsl.exe.
All named assertion labels below are required implementation oracles.

- V-1: `C1030_Linux_programs_preserve_legacy_bytes` | unit + fixture preparation |
  CP-1/CP-4 | independent golden bytes for Run injection/composition, every
  Git fault-program suffix, holder body, JSON and fake remap. Assert labels
  `c1030-linux-run`, `c1030-linux-git`, `c1030-linux-holder`,
  `c1030-linux-json`, `c1030-linux-remap`; a converter spy throws if invoked on
  Linux. LF, CRLF and no-final-newline extra bodies survive unchanged. A
  baseline-green preservation witness is intentional; its PC must turn it red.
- V-2: `C1030_Windows_paths_convert_before_fixture_effects` | pure preparation
  coupled to the actual fixture entry | CP-1/CP-4 | both Root and RepoRoot
  converted once per fixture; `wslpath` sees exactly `-u` and one intact path.
  Use native backslash and forward-slash drive spellings, spaces, apostrophe,
  dollar, backtick and Unicode together and individually. Success: same native
  sentinel is readable through the returned absolute Linux path. Fake converter
  cross-product status {0,23} x output {absolute,empty,relative}, plus absent
  converter and unmapped drive, yields no body sentinel except 0/absolute.
  Never assume an unmapped drive fails: require a real native/shell file round
  trip before admitting it. Unsupported/nonreachable conversion fails setup.
  Null repository-variable, root/repo and invalid alias values retain C980's
  contract; raw native/forward/quoted/case-changed RepoRoot bodies still refuse.
  Two live fixtures cannot reuse each other's converted root.
- V-3: `C1030_Windows_transport_ignores_ambient_launchers` | launch wiring + real
  child/lock lifecycle | CP-1/CP-4 | inspect each actual Run/Git/holder/receipt-copy
  launch; Windows uses absolute system wsl.exe, `-e /bin/bash -s`, ArgumentList,
  UTF-8 without BOM, LF stdin, no multiline `-c` or native shell filename.
  An observation hook immediately after bootstrap and before production's first
  parameter reads reports all five C590/origin values from inside the child.
  Use test-owned poison bash/git/sudo/docker/ssh/curl sentinels, sealed child PATH
  and an explicit qualified Linux tool list; none may run. Also omit a required
  tool from an entirely owned tool directory: installed host tools cannot rescue
  the negative. Never mutate process-global PATH. Real Run and Git entries must
  produce the expected fixture files and commits. Holder: hold -> contend ->
  pending/no effects -> release -> success; early holder exit 23 with a known
  stderr marker reports both. Inject failure after start and at readiness;
  verify bounded cleanup and owned PID/start-time exit. Fill stdout and stderr
  beyond pipe capacity (1 MiB each), require both tails and exit. Rescue cleanup
  in the witness owns only those PIDs, so a mutant cannot leave children behind.
- V-4: `C1030_Fixture_path_data_preserves_faults_and_round_trips` | real fixture
  data + shell audit | CP-1/CP-4 | convert volume Mountpoint, container volume/bind
  Source and both recreated.json subtrees, then compare recipient file identity.
  Cross both main/temp and dry-run/apply; test same fixture across ReloadDocker,
  Run, interrupted recreation and resumed Run. Before/after JSON deep comparison
  allows changes only at approved path fields; caller's input graph is unchanged.
  Fault vectors include missing/null/string/bool/array/object path fields,
  Options null versus omitted versus malformed, wrong-source and foreign binds,
  labels, RW=false, CreatedAt and faults. Exact root/descendant convert;
  sibling-prefix, foreign absolute and already-POSIX paths stay identical.
  Actual audit uses paths with spaces/apostrophe/$/backtick/Unicode and hashes
  the four shell repository paths. Assert linked index.lock exists in Git's
  resolved Linux directory before refusal; assert a real Linux escaping symlink
  before refusal. Fake remap preserves `/worktrees` and unrelated scratch text;
  command-looking root text never creates an injected sentinel. Windows local
  fake copy has LF/no BOM; Linux copy/remap bytes stay legacy. Dispose removes
  owned links/files/children while a separate foreign-target sentinel survives.

Frozen Linux baseline provenance (UTF-8 Git blob bytes, post-C980
`2c310b370194d834a7457037b61bb006356b151d`):

| Source | SHA-256 |
|---|---|
| Rolling | `98385bf0783a332ad4f5ff1872de417ca94022c4ef6a0d96b3b23dde63dbb135` |
| Remote | `65200a82d1f4a401f4b9ca157ecaa5ef52abd159cac9206b7e3954e29dd948cc` |
| Fake | `976650028e45fc8a76f5e6aa61ef2aee62c2ecef45ac8a2b17e1101f9b48662f` |
| c590-remote.sh | `199b3a523e00c8278c5da5866eec7ab54396b3b76275ca050410bdca094e0378` |
| deploy-server2.ps1 | `209ef537fffc6c133cb4f15597eb7184c0ea6c92474412c1d6ece9e36ec74b5d` |

Code copies independent expected literals from these pinned blobs into V-1
**before** refactoring. No test-runtime git lookup, current-source extraction,
production serializer or new quoting helper may generate the expected side.
Freeze root `/tmp/c1008-host-legacy`, repo `/fixture/repo`, dryRun false/true,
extra `echo one\necho two\n`, `echo one\r\necho two\r\n`, and
`echo no-final-newline`. Run's expected injection is the complete old raw literal
from `C1008HostFixture.Run`, deindented exactly as C# does; composition is the old
ordinal replacement of `trap 'ec=$?;` with that literal plus LF and the original
trap prefix. Compare actual bytes presented to the Linux branch, not a second
call to the new composer. Keep the original production source as the independent
composition input and verify the pinned SHA separately in R-3.

Git's golden is the complete old `C1008GitGraph` literal plus each existing
suffix for empty fault, origin-failed, head, branch, tag, second-remote, linked,
detached, bare, layouts, dirty, staged and untracked. Freeze each concatenation's
newlines (including raw-literal versus appended `\n` differences). Holder's
independent expected string is exactly:

```text
exec 8>'/tmp/c1008-host-legacy/server/locks/rollout.lock'; flock 8; touch '/tmp/c1008-host-legacy/held'; read -r release
```

No final newline belongs to that holder string. Representative compact JSON
has insertion order and literal bytes exactly as follows (no final newline):

```json
{"volumes":{"v":{"Mountpoint":"/tmp/c1008-host-legacy/volumes/v/_data","Options":null}},"containers":[{"Mounts":[{"Type":"bind","Source":"/tmp/c1008-host-legacy/work","RW":false}]}],"fault":"inspect-error"}
```

For the unchanged Linux fake remap, fixed input
`cd /work; printf '%s' /worktrees /work/file` must produce
`cd /work; printf '%s' /worktrees /tmp/c1008-host-legacy/work/file`. Freeze defect found by Code: unmodified helper and pinned fake at `a4d2d4b851fa131763f4408ee3f1f70e1b387882` produced identical actual/expected bytes (both SHA-256 `6f2d8ffdb1b894e75fb2eac24b239cb1e7864b0a2705dabfc775280617efe7c4`); replacing only the random fixture root with the fixed root gives SHA-256 `52236355fa3f00e84b998b5042edb9f88c6655ca3121d60665a7bfa151c0e28b`. The legacy regex does not match a semicolon boundary.
Also preserve double-quoted and single-quoted `/work` operands, whitespace/end
boundaries and CRLF byte vectors. Linux apostrophe-root execution is deliberately
excluded: legacy interpolation cannot execute it safely and D-3 forbids changing
those bytes; Windows quoted-root execution is required. This is a stated boundary,
not permission to claim Linux quoted-root execution support.

Independent LF-blob golden digests (read-only extraction of the pinned old
literals, not the new implementation). In the Run rows, digest covers the
**complete composed source** with the fixed root/repo above. Preserve embedded
CRLF in the extra vector; do not normalize the comparison. These LF-blob
digests qualify the native Linux branch on Linux. On Windows, P1 also compares
the old independent C# literals and new preparation under the same checkout
source EOL; an auto-CRLF checkout must not cause Code to normalize the Linux
branch to fit an LF constant. That cross-platform literal comparison does not
replace the Linux digest result. No .gitattributes change is authorized.

| Legacy vector | UTF-8 SHA-256 |
|---|---|
| Run dryRun=false, LF | `390d5d349fd77f9a7bb805d2043d83cdf32daed8aa611b04c962fd89d0c88e99` |
| Run dryRun=false, CRLF | `7cfea54fff2332b550be7c270878e171973edb32db0e6086df09cae5994cb9f2` |
| Run dryRun=false, no-final-newline | `52d973dd3478468118230510fcfe1b042ae07bbc04f794cf30dc00d7a07faa41` |
| Run dryRun=true, LF | `8ebb5d09bbda46e468824fc82c744a02488922b6c4fdf45bcd9f22c23cd624a0` |
| Run dryRun=true, CRLF | `6354bd99a9ced9457597554cd587c51a6f8fbf0300a45216f858c6e54371f841` |
| Run dryRun=true, no-final-newline | `46750b5e25728df8b24e059d724fecc60d68a45e030e040c09fc8e5d121453c6` |
| Git base, empty fault | `02e15c6b4fbf0a8db8af50acb9036408211fdb6a9a90ca6c3633a27bc0f68732` |
| Holder, no final newline | `9d32c84319af34e7df0c26a101b02ab9c593503541461d6e3eef86739dd3b979` |
| Compact JSON above | `5b92635c3bc9c2244f9cae30080da57285b8f4a13fee246ae9500dabc1c869f7` |

### Guards the regression

- R-1: CP-2/CP-5 run **all 106 Remote results**, with exact TRX names and argument
  multiplicities (99 original plus seven C983). C980's four witnesses and the
  five repaired C849 callers retain their original assertions. C905/C912 forced
  missing-tool tests keep their skip-contract assertions; those assertions run
  as passing tests, with no skipped result allowed on a qualified ordinary row.
- R-2: CP-3/CP-6 select only the four direct Rolling consumers named in the table:
  typed manifest rejection, WrongLane, receipt confidentiality and retired/null
  acceptance with exact four removed volumes. No expansion to all eight Rolling
  methods for this fixture delta. C983's required-jq consumer is unchanged and
  has separate sibling proof; touching it requires a new freeze.
- R-3: read-only full B..C diff plus pinned Linux baseline/production-byte checks
  with each OS's receipts and ordinary Review: production deploy/c590 unchanged,
  Linux behavior bytes unchanged, C980 helper semantics unchanged (accessibility
  only), no census/tool/global-PATH changes, no Windows skips, no relaxed
  assertions/timeouts/retries. Run the evidence diff guard over the full task
  range. Static diff has no separate build or TUnit count.

CP-5's mandatory repaired Windows roster and decisive existing assertions:

| Method in RemoteScriptContractTests | Must be green at final C |
|---|---|
| C1008_Recycle_exact_default_volumes | exact three removed names; malformed identity/options refuse |
| C1008_Recycle_dry_run_never_mutates | auditPending=true, no effects; changed busy apply refuses |
| C1008_Retire_temp_reclaims_below_cache_disk_gate | four removals and exit 0 despite allocation gate |
| C1008_Retire_temp_rechecks_absence_and_retirement | absent/null control removes four; invalid fresh predicates remove none |
| C1008_Recycle_receipt_records_disk_and_partial_failure | disk bytes/delta, atomic-write failure recovery, partial receipt, copy-fail then exact copied journal |
| C1008_Recycle_resume_requires_matching_receipt | only remaining originals removed; drift refuses; recreated identity resumes without second deletion |
| C1008_Recycle_preserves_tmp_copyup | named tmp removal plus mode/assets refusal |
| C1008_Recycle_refuses_uninspectable_git | real clean control; every fault including escaping link and linked index.lock refuses |
| C1008_Recycle_refuses_unpublished_and_dirty_work | published control; unpublished/dirty/staged/untracked/drift refuse |
| C1008_Recycle_audits_work_as_1654 | readonly 1654 argv and four actual shell-path audit hashes |
| C1008_Recycle_refuses_references_and_unknown_census | real held lock excludes effects, release succeeds; bind/reference/census faults refuse |
| C849_Deploy_prepares_and_verifies_before_acceptance | RunnerBusy and missing-cache refusal; exact temp retirement, deployment-order guards |

Two easily missed sub-boundaries belong to that same scope: Remote's receipt
method currently compares `df-argv` with native `disk.Root`, and its native pwsh
`Invoke-C590Ssh` stub starts bare bash with `copied.Root/remote.sh`. The repaired
method must compare the actual shell data-root and route that nested resume
launch through D-4's Windows transport, retaining failCopy=true/false and
byte-equal receipt assertions. Otherwise early greens hide a later Windows red.

Separate Debug is commissioned **after** Linux Code has committed/pushed its
final implementation SHA C; use native Windows .NET Debug, WSL only for children,
CP-4..CP-6 and `--expected-source-sha C`. It must verify default-distro
`/usr/local/bin/jq` = 1.7.1 and `/usr/local/bin/pwsh` = 7.6.6 using that same
noninteractive transport, plus bash/wslpath/Git/node/flock and native pwsh.
Use child-only `/usr/local/bin:/usr/bin:/bin` qualification and resolved absolute
executables. Sealed negative cases contain only owned stubs; ambient PATH is
never their oracle. Missing tools/Windows parent/real lock mean incomplete,
not skipped-green. No installation, Git Bash fallback or global PATH edit.

Any remaining Windows red is replayed at integrated B with the **same exact
method** and qualified environment in an owned isolated checkout/output. Capture
B/C SHAs, counts, assertion text and logs, normalizing only random root IDs.
Same assertion at B is inherited; different/new assertion is regression or
unknown until diagnosed. A build failure, missing tool or zero execution is not
base proof. Other independently confirmed inherited defects are outside this
test-only repair and disclosed by name, but a required row remains unmet.
**None of the twelve above can be waived as inherited.** All original 99 and
all seven C983 results must be green at C. Any fix changes C and requires fresh
affected receipts; never relabel pre-fix or Linux evidence as native Windows proof.

### Guard inventory

Inventory covers the safety-critical fixture invariants introduced, moved or
relied upon by this repair, including unchanged protections at the touched fake
and preparation seams. Independently bypassable entry wiring, exports, encoders,
serializers and cleanup steps are separate. Existing product refusal predicates
remain untouched, exercised by R-1/R-2; re-mutating every production C1008 guard
is outside this fixture change. There are **52 guards, 52 distinct controls,
0 missing and 0 duplicate PC mappings**; none is waived or untested by design.
All executions are pending, not claimed complete.

| Guard | Plan reference and invariant | Control |
|---|---|---|
| G-1 | D-4 Run selects absolute WSL stdin launcher | PC-1 |
| G-2 | D-4 Git entry uses the same qualified Windows transport | PC-2 |
| G-3 | D-4/D-5 holder entry uses the qualified Windows transport | PC-3 |
| G-4 | D-4 receipt-copy resume entry uses the qualified Windows transport | PC-4 |
| G-5 | D-3 Windows stdin is LF | PC-5 |
| G-6 | D-3 Windows stdin has no UTF-8 BOM | PC-6 |
| G-7 | D-4 C590_CASE reaches child before parameter reads | PC-7 |
| G-8 | D-4 C590_SHA reaches child before parameter reads | PC-8 |
| G-9 | D-4 C590_RUN reaches child before parameter reads | PC-9 |
| G-10 | D-4 C590_REEXEC reaches child before parameter reads | PC-10 |
| G-11 | D-4 C604_SERVER_ORIGIN reaches child before parameter reads | PC-11 |
| G-12 | D-4 child tool resolution is sealed/qualified, not host PATH | PC-12 |
| G-13 | D-2 fixture Root conversion is wired before body effects | PC-13 |
| G-14 | D-2 repository Root conversion is independently wired | PC-14 |
| G-15 | D-2 converter gets -u and one intact quoted native argument | PC-15 |
| G-16 | D-2 failed/missing converter cannot run body even with absolute output | PC-16 |
| G-17 | D-2 nonabsolute/empty result cannot run body | PC-17 |
| G-18 | D-1 C980 raw-RepoRoot guard precedes trusted prelude | PC-18 |
| G-19 | D-1 C980 only accepts null/root/repo variable aliases | PC-19 |
| G-20 | D-2 converted-root cache is per instance | PC-20 |
| G-21 | D-2 docker.json volume Mountpoint conversion | PC-21 |
| G-22 | D-2 docker.json volume/bind Mounts.Source conversion | PC-22 |
| G-23 | D-2 recreated.json goes through the same schema adapter | PC-23 |
| G-24 | D-2 owned-root match has a path-component boundary | PC-24 |
| G-25 | D-2 only valid strings in declared path fields may change; faults survive | PC-25 |
| G-26 | D-2 adaptation clones and preserves the caller's model | PC-26 |
| G-27 | D-2 already-POSIX reload/resume paths are not converted twice | PC-27 |
| G-28 | D-2 expected audit SHA-256 uses UTF-8 Linux path bytes | PC-28 |
| G-29 | D-2 linked index.lock is written in Git's Linux gitdir | PC-29 |
| G-30 | D-2 escaping-link setup creates a real Linux symlink | PC-30 |
| G-31 | D-3 Windows fake remap quotes root and matches only /work operands | PC-31 |
| G-32 | D-3 fixture-local Windows fake copy normalizes CRLF to LF | PC-32 |
| G-33 | D-3 fixture-local Windows fake copy is UTF-8 without BOM | PC-33 |
| G-34 | D-3 Linux Run injection/composed source bytes unchanged | PC-34 |
| G-35 | D-3 Linux Git program/suffix bytes unchanged | PC-35 |
| G-36 | D-3 Linux holder program bytes unchanged | PC-36 |
| G-37 | D-3 Linux compact JSON bytes unchanged | PC-37 |
| G-38 | D-3 Linux fake audit-remap bytes unchanged | PC-38 |
| G-39 | D-5 real holder and contender lock the same Linux inode | PC-39 |
| G-40 | D-5 holder stdin remains open while waiting for release | PC-40 |
| G-41 | D-5 release is written/flushed to read before close and awaited | PC-41 |
| G-42 | D-5 early holder failure preserves nonzero exit diagnosis | PC-42 |
| G-43 | D-5 stdout is drained during child execution | PC-43 |
| G-44 | D-5 stderr is independently drained during child execution | PC-44 |
| G-45 | D-5 failed/timed-out owned child tree is terminated | PC-45 |
| G-46 | D-5 cleanup awaits actual owned process exit | PC-46 |
| G-47 | D-2/D-5 Dispose removes owned root and escaping link | PC-47 |
| G-48 | D-4 file-backed fake refuses unknown operations without live fallback | PC-48 |
| G-49 | D-4 fake refuses invalid fixture-root identity before file effects | PC-49 |
| G-50 | D-5 early holder failure independently preserves stderr diagnosis | PC-50 |
| G-51 | D-3 Windows fake remap leaves /worktrees and nonoperand text unchanged | PC-51 |
| G-52 | D-2/D-5 disposal never traverses an escaping link into its target | PC-52 |

### Positive controls

Method abbreviations in this table are exact aliases, not filters:

- **P1** = `C1008HostFixturePortabilityTests.C1030_Linux_programs_preserve_legacy_bytes`.
- **P2** = `C1008HostFixturePortabilityTests.C1030_Windows_paths_convert_before_fixture_effects`.
- **P3** = `C1008HostFixturePortabilityTests.C1030_Windows_transport_ignores_ambient_launchers`.
- **P4** = `C1008HostFixturePortabilityTests.C1030_Fixture_path_data_preserves_faults_and_round_trips`.

For every PC-n, break **only G-n** using the compiling test-fixture defect below;
expect that exact method red at the listed assertion. Multiple vectors sharing
one guard are exercised by the same method; a control with explicit variants
runs each variant separately. Do not mutate the expected oracle or production
scripts. A syntax/build error, missing prerequisite, zero execution, raw fixture
exception or timeout of the *test runner* does not count as the intended red.
Capture fixture failures as data and assert the labeled outcome. For launch
mutants assert the prepared launch spec before starting a wrongly selected
executable. For cleanup mutants use test-owned child observations and rescue
cleanup, not a production process. Tests may have narrow internal observation
hooks used by their real callers; hooks cannot replace the guarded behavior.

| PC | Compiling defect | Exact method red at assertion |
|---|---|---|
| PC-1 | Change only Run's Windows ProcessStartInfo filename to bare `bash` | P3 `c1030-run-launch`: absolute system wsl.exe expected |
| PC-2 | Route Git entry to legacy bash/-c specification | P3 `c1030-git-launch`: WSL stdin spec expected |
| PC-3 | Route holder entry to legacy bash/-c specification | P3 `c1030-holder-launch`: WSL stdin spec expected |
| PC-4 | Route receipt-copy resume to bare bash plus native remote.sh | P3 `c1030-bridge-launch`: WSL stdin spec expected |
| PC-5 | Omit Windows stdin CRLF-to-LF replacement | P3 `c1030-stdin-lf`: captured stdin contains no CR byte |
| PC-6 | Select UTF8Encoding(true) instead of false for Windows stdin | P3 `c1030-stdin-bom`: actual transport bytes have no EF BB BF prefix |
| PC-7 | Omit only the C590_CASE export | P3 `c1030-env-case`: child receipt equals requested hostCase before source |
| PC-8 | Omit only the C590_SHA export | P3 `c1030-env-sha`: child receipt equals forty a characters |
| PC-9 | Omit only the C590_RUN export | P3 `c1030-env-run`: child receipt equals c1008fixture |
| PC-10 | Omit only the C590_REEXEC export | P3 `c1030-env-reexec`: child receipt equals 1 |
| PC-11 | Omit only the C604_SERVER_ORIGIN export | P3 `c1030-env-origin`: child receipt equals http://127.0.0.1:1 |
| PC-12 | Use inherited PATH in place of the qualified child tool list | P3 `c1030-sealed-tools`: effective PATH excludes poison directory and poison sentinel is absent; missing-tool case cannot resolve host Git |
| PC-13 | Bypass Root conversion and retain its native spelling | P2 `c1030-root-converted`: actual prepared fixture root equals converter's distinct absolute result |
| PC-14 | Bypass only RepoRoot conversion | P2 `c1030-repo-converted`: actual repo value equals its distinct absolute result |
| PC-15 | Remove quoting around the converter's native argument in generated shell text | P2 `c1030-converter-argv`: exactly two arguments, intact UTF-8 second argument on whitespace/metacharacter vector |
| PC-16 | Ignore converter nonzero status but keep absolute-result check | P2 `c1030-conversion-status`: status 23 + absolute output gives nonzero refusal and absent body sentinel |
| PC-17 | Remove absolute-result case guard, keep converter status check | P2 `c1030-conversion-absolute`: status 0 + empty/relative output refuses before body |
| PC-18 | Replace C980's raw-root rejection condition with false | P2 `c1030-raw-root`: captured InvalidOperationException for native/forward/quoted/case-changed body |
| PC-19 | Remove C980 alias whitelist rejection | P2 `c1030-alias`: captured ArgumentException for empty/ROOT/path/root;false alias |
| PC-20 | Replace instance converted-root storage with static shared storage | P2 `c1030-instance-root`: second fixture sentinel resolves through second root, not first |
| PC-21 | Skip Mountpoint assignment in docker.json adapter | P4 `c1030-mountpoint`: native output file field equals expected Linux path |
| PC-22 | Skip Mounts.Source assignment in adapter | P4 `c1030-mount-source`: both volume and bind recipient fields equal expected Linux paths |
| PC-23 | Serialize recreated.json without calling the adapter | P4 `c1030-recreated`: saved volume and runner source fields are Linux and resume removes no second generation |
| PC-24 | Replace root-or-root-plus-separator predicate with plain StartsWith(root) | P4 `c1030-root-boundary`: sibling-prefix path is byte-identical |
| PC-25 | Replace field-aware traversal with recursive string replacement across JSON | P4 `c1030-faults-preserved`: labels/options/fault strings equal original; malformed path nodes unchanged |
| PC-26 | Return/mutate the input JsonObject instead of DeepClone | P4 `c1030-input-unchanged`: caller model deep-equals saved pre-adaptation model |
| PC-27 | Remove already-POSIX bypass and feed reload values through conversion again | P4 `c1030-roundtrip`: second adaptation has zero new conversions and persisted paths unchanged |
| PC-28 | Hash native Path.Combine bytes in Windows expected audit builder | P4 `c1030-audit-hashes`: four observed hashes equal independent shell-path hashes |
| PC-29 | Skip linked gitdir index.lock write | P4 `c1030-linked-lock`: shell test -f of Git-resolved index.lock succeeds before original refusal assertion |
| PC-30 | Replace Windows ln -s setup with mkdir at escape path | P4 `c1030-escaping-link`: shell test -L and readlink equal intended Linux target before refusal |
| PC-31 | Use the legacy unquoted root substitution in Windows fake audit remap | P4 `c1030-quoted-audit`: quoted-root audit exits 0 with correct hashes and no injected sentinel; /worktrees unchanged |
| PC-32 | Omit CRLF normalization when copying the Windows fake | P4 `c1030-fake-lf`: copy of a CRLF input contains no CR bytes |
| PC-33 | Write Windows fake copy with UTF8Encoding(true) | P4 `c1030-fake-bom`: file bytes have no EF BB BF prefix |
| PC-34 | Append one LF to Linux Run injection only | P1 `c1030-linux-run`: complete expected and observed byte arrays equal |
| PC-35 | Append one LF to Linux Git base program only | P1 `c1030-linux-git`: independent base/suffix byte arrays equal |
| PC-36 | Append one LF to Linux holder body only | P1 `c1030-linux-holder`: exact no-final-newline bytes equal |
| PC-37 | Serialize Linux JSON with WriteIndented=true | P1 `c1030-linux-json`: exact compact literal bytes equal |
| PC-38 | Change Linux fake remap to replace /work without operand boundary | P1 `c1030-linux-remap`: /worktrees and all independent remap bytes unchanged |
| PC-39 | Give holder a different lock filename, keeping real flock | P3 `c1030-lock-exclusion`: after held and lock-wait, contender is pending and no stop/rm trace exists |
| PC-40 | Close holder stdin immediately after sending source bootstrap | P3 `c1030-holder-retained`: holder alive and contender pending until explicit release |
| PC-41 | Suppress release WriteLine/flush while retaining the input pipe until cleanup | P3 `c1030-release-completed`: contender exit 0 and holder exit observed within existing ten-second release bound; rescue cleanup then assertion |
| PC-42 | Substitute exit code 0 in the holder failure result | P3 `c1030-holder-exit`: startup-failure result carries exit 23 |
| PC-43 | Defer stdout ReadToEndAsync until after WaitForExitAsync | P3 `c1030-stdout-drained`: owned pipe-flood child finishes and stdout tail is present within bound |
| PC-44 | Defer stderr ReadToEndAsync until after WaitForExitAsync | P3 `c1030-stderr-drained`: owned pipe-flood child finishes and stderr tail is present within bound |
| PC-45 | Omit failure/timeout Kill(entireProcessTree:true) | P3 `c1030-owned-tree-exited`: root and owned descendant PID/start identities absent after cleanup; witness rescues them before failing |
| PC-46 | Return from cleanup before awaiting owned child exit | P3 `c1030-exit-awaited`: completion is still pending while the observed WaitForExitAsync gate is held, then completes after release |
| PC-47 | Bypass the owned-root disposal cleanup call | P4 `c1030-owned-cleanup`: owned root/link absent after disposal; witness rescues residue before failing |
| PC-48 | Replace fake's final unknown-operation fail() with exit 0 | P4 `c1030-fake-unknown`: unknown operation returns 2, no state mutation or external-command sentinel |
| PC-49 | Remove fake root basename validation | P4 `c1030-fake-root`: invalid-basename owned scratch with valid docker.json refuses before trace/state effects |
| PC-50 | Discard only stderr from the holder failure result | P3 `c1030-holder-stderr`: injected startup stderr marker retained |
| PC-51 | Remove /work operand boundary from Windows remap replacement | P4 `c1030-windows-remap-boundary`: /worktrees and unrelated scratch text remain byte-identical |
| PC-52 | Resolve the owned escape symlink and recursively delete its target instead of unlinking it | P4 `c1030-foreign-target-preserved`: separate test-owned target sentinel still exists with original bytes |

For PC-18, the four spellings exercise one shared Any/Contains guard, not four
independently bypassed branches. PC-17's empty/relative vectors exercise the same
single case guard. PC-22's bind/volume fields share the same Source transformer.
PC-23 removes one shared adapter invocation at the independently serialized
recreated.json handoff. If Code implements separate guards instead, split their
PCs and revise this inventory/cost before Review; do not conceal an extra guard
inside a single mutant. Exit status and stderr are already split as PC-42/PC-50;
Windows quoting and operand matching are split as PC-31/PC-51. PC-46 uses a
fixture observation hook around the real exit wait: the witness holds that await
gate, observes disposal still pending, releases it and verifies actual PID exit.
This removes scheduler timing from the await oracle; PC-45 separately proves
real tree termination. No fake process object substitutes for the child.

Mutation runs **baseline green -> break -> intended red -> restore -> fresh-build
green** after confirmed land L, with exact filter
`/*/*/C1008HostFixturePortabilityTests/<the single method above>` and Min=1.
Use a separate output/result path for every phase, the host slot gate, and owned
external SourceLanding evidence. Every phase has one execution; no whole-class
PC cycles. Native Linux owns PC-34..PC-38; native Windows owns the other 47 to
exercise actual WSL entry branches. These are separately commissioned inherited
executions at the same L, respecting same-operation admission; no delegation
from a snapshot. All 52 are executable compiling substitutions in the planned
test seam, with observable assertion failures; Code implements those seams and
oracles, Review judges them before land, Mutation executes them after land.
A missing/non-red seam returns to Plan or Code and cannot be marked verified.

### Out of scope

- Production deployment scripts, real Docker volumes/uid enforcement, SSH, HTTP,
  queue/session delivery and stack activation: no product changes or live rollout.
- CARD-0983 behavior/PCs and CARD-1010 opt-ins: preserve sibling work and sequence
  ownership; C983's seven results are included only because the full shared class
  now contains them. No additional Rolling required-jq run without a source delta.
- Unrelated Windows failures: none newly established by the inspected reports;
  disclose any method-only B-confirmed non-target failure, never count it green.
- Full Unit/assembly and 51 image-helper consumers: no shared LinuxShell transport
  change is authorized. Accessibility-only preparation reuse does not justify
  those builds. A semantic helper change returns to TestDesign and adds both OS
  consumer rows before execution. Existing production-guard PC inventories belong
  to their original cards; this inventory controls the fixture delta and seams.
- Host package installation, persistent PATH/profile changes, symlink policy,
  timeout widening, retries and skip additions: qualification failures remain
  visible and are not repaired by weakening the proof.

### Checkpoints

This is the single executable table. One committed S1 and one isolated build per
row; all builds use Debug defaults, six different forward-slash outputs. Linux
Code runs CP-1..CP-3, then the caller commissions native Windows Debug CP-4..CP-6
at exact final C. There is no optional row and no count substitution. Wildcards
on Rolling's OR operands are required for the pinned TUnit discovery matcher;
source census establishes exactly the four intended methods, and receipts must
verify those names. Min is a result floor; explicit exact counts, names and zero
skips below are additional acceptance requirements.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---|---|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1030-linux-portability/` | linux-fixture-portability | `/*/*/C1008HostFixturePortabilityTests/*` | V-1..V-4, R-3 | Exactly 4 passed, 0 failed/skipped; four C1030 methods | 4 | 4 | true | `C804_ORPHAN_SWEEP_ROOT=c1030-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-2 | S1 | `tests/Antiphon.Tests -> bin-c1030-linux-remote/` | linux-remote-contracts | `/*/*/RemoteScriptContractTests/*` | R-1, R-3 | Exactly 106 passed: every original 99 plus seven C983, 0 failed/skipped | 106 | 16 | true | `C804_ORPHAN_SWEEP_ROOT=c1030-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-3 | S1 | `tests/Antiphon.Tests -> bin-c1030-linux-consumers/` | linux-host-fixture-consumers | `/*/*/RollingVolumeRecycleScriptTests/(C1008_Option_manifest_is_strict*)\|(C1008_Documentation_and_transport_pins_match*)\|(C1008_Refusal_receipts_do_not_leak_secrets*)\|(C1008_Retired_absent_null_is_accepted*)` | R-2, R-3 | Exactly the four named methods passed, 0 failed/skipped | 4 | 8 | true | `C804_ORPHAN_SWEEP_ROOT=c1030-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-4 | S1 | `tests/Antiphon.Tests -> bin-c1030-windows-portability/` | windows-native-debug-portability | `/*/*/C1008HostFixturePortabilityTests/*` | V-1..V-4, R-3 | Exactly 4 passed, 0 failed/skipped; native Windows parent at C | 4 | 6 | true | `C804_ORPHAN_SWEEP_ROOT=c1030-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-5 | S1 | `tests/Antiphon.Tests -> bin-c1030-windows-remote/` | windows-native-debug-remote | `/*/*/RemoteScriptContractTests/*` | R-1, R-3 | Exactly 106 passed; every original 99 including all 12 repaired methods plus seven C983, 0 failed/skipped at C | 106 | 20 | true | `C804_ORPHAN_SWEEP_ROOT=c1030-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-6 | S1 | `tests/Antiphon.Tests -> bin-c1030-windows-consumers/` | windows-native-debug-host-consumers | `/*/*/RollingVolumeRecycleScriptTests/(C1008_Option_manifest_is_strict*)\|(C1008_Documentation_and_transport_pins_match*)\|(C1008_Refusal_receipts_do_not_leak_secrets*)\|(C1008_Retired_absent_null_is_accepted*)` | R-2, R-3 | Exactly the four named methods passed, 0 failed/skipped; native Windows parent at C | 4 | 10 | true | `C804_ORPHAN_SWEEP_ROOT=c1030-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |

Execution: checkpoint tool `run --plan <this path> --rows CP-1,CP-2,CP-3
--expected-source-sha C --serial` on Linux; identical command selecting
CP-4,CP-5,CP-6 on native Windows. Await all exit-75 continuations in foreground;
never settle with executor/children active. The tool's planned administrative
bootstrap, if needed, is gated with `build-slot.ps1`, isolated `bin-c1030-tool/`
and Linux UseAppHost=false; it is not an extra test row. Use the checkpoint
runner's own gate for rows, not a nested held slot. Exit 4 is not run. Preserve
unedited CHECKPOINT lines and validate source/build receipts against C. Inspect
TRX multiplicities and all mandatory method names, not just floors. Clean only
owned alternate outputs after every process has exited. Code and Review run
`scripts/check-evidence-diff.ps1` across their full source range.

Importer qualification is read-only: load the already built
`/work/worktrees/task-608ed47c/tools/Antiphon.Checkpoints/obj/Debug/net9.0/Antiphon.Checkpoints.dll`
using PowerShell reflection, call actual `PlanTableImporter.ImportFile` with
isWindows=false and true, then `ManifestValidator.Validate`. Its importer,
validator and RowTimeout source are identical to this checkout; no tool build
or test driver is launched. Loaded assembly SHA-256 was
`d0059124616ccb68b8fd9208803ed4c880cb9953cd1cd54344ac769d890ac007`.
Both settings were invoked and resolved six builds/rows,
minima 4/106/4/4/106/4, 64 minutes, Serial=true and the two child variables per
row. Derived row timeouts are **15/48/24/18/60/30 minutes**. CP-2 and CP-5
produce advisory 48/60 > 45 warnings; the actual RowTimeout implementation does
not clamp them. Total derived deadline is 138 minutes if all rows are selected;
use the explicit three-row OS selection. Import accepts schema/filter strings;
it cannot establish execution or check Expect prose. Exact counts and roster
receipt inspection remain mandatory. Only this appended table is imported.

### Cost

All costs below are estimates, not measured runtime; this dispatch ran no tests.
Ordinary Code floor is the sum of CP EstimatedMinutes:
**4+16+8+6+20+10 = 64 minutes**, including six isolated builds, split Linux 28
and Windows 36. Expected extra prerequisite/tool setup is **4 minutes** across
two hosts. The original 60-minute proposal increases by four minutes for the
seven C983 results in each full Remote row. Ordinary output is 228 executions.

Post-land Mutation floor is separate and includes an isolated build in each
baseline, red and restored-green phase plus restoration time:

| Exact method filter | PC IDs / cycles | Per cycle (builds + runs + restore) | Floor minutes |
|---|---|---|---:|
| `/*/*/C1008HostFixturePortabilityTests/C1030_Linux_programs_preserve_legacy_bytes` | PC-34..PC-38: 5 | 3 x 1-minute builds + 3 x 0.25-minute runs + 0.25 restore = 4 | 20 |
| `/*/*/C1008HostFixturePortabilityTests/C1030_Windows_paths_convert_before_fixture_effects` | PC-13..PC-20: 8 | 3 x 1-minute builds + 3 x 0.75-minute runs + 0.75 restore = 6 | 48 |
| `/*/*/C1008HostFixturePortabilityTests/C1030_Windows_transport_ignores_ambient_launchers` | PC-1..PC-12, PC-39..PC-46 and PC-50: 21 | 3 x 1-minute builds + 3 x 1.75-minute runs + 0.75 restore = 9 | 189 |
| `/*/*/C1008HostFixturePortabilityTests/C1030_Fixture_path_data_preserves_faults_and_round_trips` | PC-21..PC-33, PC-47..PC-49 and PC-51..PC-52: 18 | 3 x 1-minute builds + 3 x 1.75-minute runs + 0.75 restore = 9 | 162 |

**PC floor = 419 minutes** for 52 independent cycles / 156 method executions;
Code does not spend that floor. Two Mutation host setup allowances add
**4 minutes**. Total planned execution/setup floor is
**4 + 64 + 4 + 419 = 491 minutes**, excluding authoring, review, queue/slot waits
and failure-triggered reruns/base replays. Per-PC green/red/restore evidence is
required even when the class has already passed ordinary verification.
No batching savings are claimed: these mutants share fixture files and methods,
so safe independence is not established. **Quantified savings credited: 0
minutes**; method-scoped runs avoid unnecessary suites but there is no measured
comparable baseline from which to invent a saving. Record actual durations and
any extra run's trigger; never widen timeout/floor to fit this estimate.

Before handoff audit: bodies above read; guards=52, mapped=52, missing=0,
duplicate PC maps=0; all controls specify compiling defects, exact methods and
decisive assertions; ordinary floor=64, Mutation floor=419, total with setup=491.
New witnesses/seams and every ordinary/PC run remain implementation obligations.
The verification design is complete; the next stage is Code after the stated
C983 admission condition, then separate final-SHA Windows Debug and ordinary
Review, land, and the separately commissioned post-land Mutation battery.

--- next stage ---
next: code
handoff: Start from the pushed CARD-1030 TestDesign tip; wait for CARD-0983 Review/land, integrate landed target without rewriting this branch, record B, preserve C980/C983 seams, then implement test-only repair. Run six frozen rows (4/106/4 each OS), separate native Windows Debug at final C; require all original 99 and twelve targets green. PCs follow land.
artifact: docs/superpowers/plans/2026-10-04-card-1030-c1008-windows-host-fixture-plan.md
