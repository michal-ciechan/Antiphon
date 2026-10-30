# CARD-1199 native Windows feasibility

Investigation date: 2026-10-30. Task `6a38c2dd`, branch `feat/card-task-6a38c2dd`.
Product and assertion source: `ade54222543381e2e1b31ad79d8ae33a4035f70b`.

## Outcome

**12 of 35 passed unchanged after lifting only the three gates; 23 failed.
The 23 failures are ten POSIX-shell arrangements and thirteen Git/CRLF fixture
cases. No product implementation gap or inherently inapplicable Windows behavior
has been demonstrated.** The thirteen configuration controls below all passed.

| Requested class | Rows | Passed | Failed | Skipped |
|---|---:|---:|---:|---:|
| ShapeConflict | 17 | 7 | 10 | 0 |
| TrackedBaseline | 13 | 5 | 8 | 0 |
| OutputConfinement (requested five) | 5 | 0 | 5 | 0 |
| **Total requested** | **35** | **12** | **23** | **0** |

The existing Windows-only OutputConfinement sibling passed separately, making
the whole third class 6 rows, 1 passed, 5 failed. It is not added to the 35.

The skip mechanism is confirmed: each class invokes a `RequireLinux` helper
before its body. These are historical qualification gates, not a declaration
that every arrangement needs Linux. There is no missing Windows implementation
on the ordinary checkpoint paths examined. This is not a claim that every
Windows race or R-2 obligation has been qualified.

## Source and execution provenance

The Antiphon board read returned CARD-1199 ID
`3a994d16-4fa0-4e03-b85f-18806ba7ad1a`, board
`8988ca03-7414-47ad-b0b6-51556c701703`, 33 revisions, Done. Its closing record
explicitly retains native Windows R-2. Policy context was read from
`origin/feat/card-task-93cbfd8a:.antiphon/task-93cbfd8a.md` at `3b544b9f7939405c2338e131a3916e005217bacc`; it keeps these 35
Windows rows required. The R-2 owner is
`docs/superpowers/plans/2026-10-12-card-1199-detect-report-plan.md:16`.

Native host: Windows 10 Pro 10.0.19045, x64, NTFS, SDK 10.0.300, runtime 9.0.22,
PowerShell 7.6.9, Git 2.50.1.windows.1, TUnit 1.44.0. No WSL or container lane.
The host reports `core.autocrlf=true`; all three checked-out test files contain
CRLF, including their raw multiline C# fixture strings.

One build of `tests/Antiphon.Tests` and its dependency graph used
`--property:OutputPath=bin-c1199win6a/` through `scripts/build-slot.ps1`. It had
0 errors, 1,380 warnings and took 9m56.13s (lease
`53e77d01-a255-4f6b-980e-3ea9e2bff1f7`, 0s queue, 599s held).
The build was made after documentation commit
`dfdd9a903057ad799e5edda48066c6e16c06febb`, with exactly three uncommitted
one-line gate edits: permit the existing body when `C1199_WINDOWS_PROBE=1`.
No fixture, assertion, timeout or product code was changed. Subsequent
documentation commit `eda726fdbb8fdd85c482f6babe26e01fea363c4e` records the first
class. All runs reuse that one binary with `--no-build`.

Test assembly SHA-256: `f3a1326425e19f4fdc9c24a58dc196877bad10dec9034308787d9d5db13c64ac`.
Gate patch SHA-256: `2aba8fd48347e6e4f8cef69b52f9cd2060d0628f7bb8fcaa275520fa4cfef20f`.

Evidence root (local, ignored):
`C:\Antiphon\worktrees\card-task-6a38c2dd\.antiphon\probe-6a38c2dd`.
It holds `build.log`, `gates.patch`, original source bytes, `source-hashes.json`,
`provenance.json` (including test assembly SHA-256), per-run console logs, TRX,
row extracts, run metadata and opt-in timing JSONL. TEMP/TMP were redirected
to its own `temp` child, so fixture repositories and their sweep remain inside
the assigned worktree. All children were local inherited processes. AppHost,
runners, canonical checkout and host configuration were not changed.

These are investigative results from a gate-modified binary, not clean-source
release receipts. Original Windows coverage remains incomplete until a later
commissioned change and verification qualify it.

## Exact gates and what each class proves

Source aliases used in the row table:

- **S** = `tests/Antiphon.Tests/Checkpoints/CheckpointShapeConflictTests.cs`.
  Calls at lines 48 and 240; gate `if (!OperatingSystem.IsLinux())` at 505,
  reason at 506: `CARD-1199 shape-conflict proof runs on Linux; Windows junctions remain an R2 disclosure.`
  Sixteen argument rows exercise preservation/detection through the tool and
  PowerShell entry points; the seventeenth rejects a missing negative receipt.
- **T** = `tests/Antiphon.Tests/Checkpoints/CheckpointTrackedBaselineTests.cs`.
  All 13 methods invoke the helper; gate at 618, reason at 619:
  `CARD-1199 tracked-baseline proof runs on Linux; Windows is unverified.`
  Assertions cover tracked-content/index detection, refusal receipts, clean
  real builds, snapshot membership and preservation of newly staged user data.
- **O** = `tests/Antiphon.Tests/Checkpoints/CheckpointOutputConfinementTests.cs`.
  Calls at 24/33/42/51/60; gate at 480, reason at 481:
  `CARD-1199 output confinement real-build proof runs on Linux; Windows is unverified.`
  Four graph forms require tracked dependency DLL/assets to survive while new
  outputs appear under the entry project's owned root; one rejects a missing
  import. The Windows sibling at 86 is a separate control, outside the 35.

## Product path on Windows

**P1: baseline and detection.** `tools/Antiphon.Checkpoints/BuildConfinement.cs:234`
reads tracked status, captures HEAD/index/content/untracked data, then prepares
the build. `:294` compares the baseline after execution; `:427` distinguishes
file/link/directory/absence and hashes regular content. `:716` captures the
whole tracked set, `:789` reads status, and `:820` calls hardened Git. The
PowerShell equivalents are `scripts/run-checkpoint.ps1:832`, `:863`, `:912`
and `:977`. These paths use Git and portable file operations, not an
unimplemented Linux-only baseline API. Detection deliberately leaves the
build-result bytes/index in place; it does not automatically restore source.

**P2: graph/output layout.** `BuildConfinement.cs:509` rewrites OutputPath,
ArtifactsPath and UseArtifactsOutput; `:620` evaluates project references using
real MSBuild. This includes imported/conditional/transitive references.
`BuildConfinement.cs:545` explicitly avoids forcing UseAppHost=false on Windows.
No Linux-only graph evaluator is required.

**P3: fresh output and receipts.** `src/Antiphon.HostCleanup/Checkpoints/SafeWrite.cs:346`
creates an exclusive owned directory and marker. Link checks include
`SafeWrite.cs:513` and `SafeDelete.cs:181`; Git tracked-output refusal is at
`SafeDelete.cs:289`. Script collision refusals are at
`scripts/run-checkpoint.ps1:409`. Existing stamps, output trees and reparse
components are refused before build; no existing user output is adopted simply
because it has a `bin-*` name.

**P4: implemented native Windows I/O.** The factory is
`src/Antiphon.HostCleanup/Checkpoints/CheckpointCleanupDefaults.cs:9`, selecting
`CheckpointNativeIo`, not the unrelated top-level `WindowsCleanupFileSystem`
stub. The relevant Windows code is implemented (short file names below are under `src/Antiphon.HostCleanup/Checkpoints`):

- `CheckpointNativeIo.cs:165`: handle-relative deletion, reparse attributes,
  volume/file index and creation identity, then SetFileInformationByHandle.
- `CheckpointNativeIo.cs:247`: owner-only Windows directory security descriptor;
  `:298`: CreateFile with OPEN_REPARSE_POINT/BACKUP_SEMANTICS,
  GetFileInformationByHandle, GetFileInformationByHandleEx, link count, real
  change time, owner/DACL and file ID. Linux statx/mode/uid equivalents are
  separately selected. `OutputCustody.cs:39` applies custody rules.
- `CheckpointFileReader.cs:87`: Windows regular-file/reparse/device precheck and
  retained Windows handle; `CheckpointNativeIo.cs:513` opens it and `:546`
  inspects regularity, hard-link count and GetFinalPathNameByHandle.
  `scripts/lib/checkpoint-file-reader.ps1:311` and `:322` implement the script
  counterparts. Linux NOFOLLOW/NONBLOCK/statx/proc-fd paths are OS-gated.
- `CheckpointNativeIo.Anchored.cs:196`, `:225`, `:263`, `:294` implement Windows
  anchored listing/unlink/truncate and component walks. Linux openat/unlinkat
  and `/proc/self/fd` are not called on Windows.
- `CheckpointNativeIo.cs:33`, `:53`, `:74`: Windows process start time, kernel
  boot time and local namespace substitute for Linux `/proc` identity data.

The generic `CheckpointNativeIo.Stat` portable Windows fallback (`:482`) has
no inode/change time. This is not the custody implementation: identity reads
explicitly select `CustodyStat` on Windows
(`ICheckpointCleanupIo.cs:25`). Treating that fallback or the unrelated adapter
stub as proof of a missing checkpoint Windows implementation would be wrong.

## Confirmed fixture mechanisms

**F1: POSIX commands in an MSBuild Exec arrangement.** S:315 and S:320 select
the shell operations, then S:404 uses MSBuild Exec. This host cannot resolve
rm.exe, mv.exe, printf.exe or ln.exe (`posix-command-availability.json`). Nine
failed rows retain the exact missing-command diagnostic in TRX; directory-on-file
instead throws on its absent arranged descendant before the combined diagnostic
is assembled. Its source uses the same missing commands. This is a fixture
failure, not proof that the checkpoint deleted/recreated a user file. Some
assertion labels say `tool-recreated-*` or `tool-rewrote-*` because they assume
the arrangement already happened. On Windows a failing command with output
redirection can still affect the redirection destination. The failed fixture assertions do not establish a product deletion.

**F2: inconsistent fixture/product Git configuration with CRLF.**
`tests/Antiphon.Tests/Checkpoints/CheckpointFixtureProcess.cs:28` invokes the runner with hardening disabled;
its Git wrapper at `:44` retains installed Git behavior. T:514 and O:484 create
repositories without pinning autocrlf; S:284 and O:94 explicitly pin it false.
The product calls the runner with hardening enabled (`BuildConfinement.cs:835`,
`scripts/run-checkpoint.ps1:131`). `scripts/lib/CheckpointProcessRunner.cs:95`
sets GIT_CONFIG_NOSYSTEM=1 and GIT_CONFIG_GLOBAL=NUL on Windows.

The staging failures reach the fixture's own `git restore --staged` at T:361 after an early refusal, so the expected staged path is absent. The four output-confinement cases pass the tool-side assertions at O:160, restore assets at O:475, then fail on the script output existence assertion at O:172; DLL/assets equality is checked first at O:170.

Stored failure evidence includes `fresh_checkout_run_executes_a_test`:

```text
refused: dirty-tracked
DIRTY App/App.csproj
CHECKPOINT fresh-tool build=failed project=App/App.csproj outputPath=bin-probe/ exit=2 slot=skipped waited=0s
CHECKPOINT fresh-tool EXIT CODE: 2
```

An ordinary scratch text-file comparison reproduced the configuration difference
without invoking cleanup: `git-config-differential.txt` records index LF/worktree
CRLF; after invalidating only the file stat cache, isolated configuration reports
`1 .M ... fixture.txt`, while explicit `core.autocrlf=true` reports clean.
An earlier cached-status observation was clean under both settings; this explains
why an individual native pass does not make the shared fixture setup deterministic.

The eight failed TrackedBaseline methods were then run with process-local
`GIT_CONFIG_NOSYSTEM=1` and `GIT_CONFIG_GLOBAL=NUL`. All eight passed, using
the same binary and unchanged assertions. This is a configuration experiment,
not a replacement of the original red receipt. OutputConfinement control:
all five passed, zero failed/skipped, in 7m28.605s. This confirms F2 for all thirteen original configuration failures. Across the initial run and the isolated-configuration controls, 25 of the requested 35 rows have native passing evidence; the ten POSIX arrangements still require a fixture port. These are separate investigative runs, not one clean-source release qualification.

The first combined-method control selector lacked the documented trailing
wildcards and executed zero tests (`baseline-config-control`, 9.02s outer
wall, lease `f0bb610d-024c-468f-b9fb-4e49f9ca603a`, 7s held). It is invalid
execution evidence and remains in the audit table. The corrected selectors
follow `docs/testing-and-build.md:1120`, select only the failed method names,
and reuse the same built binary.

## Per-row results

The result column always means the first run with only the gate lifted and the
ordinary host environment. Follow-up configuration controls are separate.
References S/T/O expand to the exact files above. P1/P2/P3/P4 identify the
implemented product paths; F1/F2 identify fixture causes, not product repairs.

| Row / source | Assertion and Windows path | Initial native result | Failure / configuration control |
|---|---|---|---|
| S `directory-on-file` (S:101) | Directory replacing tracked file retains staged and unstaged descendants/index; P1 | FAILED 20.61s | DirectoryNotFoundException: tracked.txt/unstaged.txt; F1 |
| S `file-on-directory` (S:106) | File replacing tracked directory survives and conflict is reported; P1 | FAILED 65.48s | toolNote expected ok, actual missing exit=2; F1 |
| S `symlink-on-file` (S:108) | Replacement file symlink and its target bytes survive; P1/P4 | FAILED 65.83s | toolNote expected ok, actual symlink-lost exit=2 target=&lt;none&gt;; F1 |
| S `untracked-descendant` (S:118) | Ordinary untracked descendant survives a tracked-file shape conflict; P1 | FAILED 56.57s | toolNote expected ok, actual missing exit=2; F1 |
| S `ignored-descendant` (S:120) | Ignored descendant survives a tracked-file shape conflict; P1 | FAILED 54.28s | toolNote expected ok, actual missing exit=2; F1 |
| S `staged-untracked` (S:122) | Newly staged user file and index survive; source evidence is invalid; P1/P3 | PASSED 56.57s | No fixture edit; no control repeat. |
| S `modified-tracked` (S:141) | Changed tracked bytes remain; tampering is detected without restore; P1 | FAILED 51.54s | toolNote expected ok, actual tool-rewrote-build-bytes exit=2; F1 |
| S `deleted-tracked` (S:142) | Build-deleted tracked file stays absent; tampering is detected; P1 | FAILED 60.38s | toolNote expected ok, actual tool-recreated-deleted-file; F1 |
| S `clean-build` (S:186) | Clean real TUnit build executes one test and preserves side/keep bytes; P1/P2/P3 | PASSED 70.94s | No fixture edit; no control repeat. |
| S `regular-user-move` (S:166) | User file moved onto tracked path survives; conflict is reported; P1 | FAILED 55.83s | toolNote expected ok, actual bytes-changed exit=2; F1 |
| S `deleted-untracked` (S:161) | Deleted pre-existing user file stays absent and is reported; P1 | FAILED 52.83s | toolNote expected ok, actual tool-recreated-user-file; F1 |
| S `changed-untracked` (S:162) | Changed pre-existing user bytes remain and are reported; P1 | FAILED 50.22s | toolNote expected ok, actual tool-rewrote-user-file; F1 |
| S `existing-stamp` (S:168) | Pre-existing build-source stamp survives; build is refused; P3 | PASSED 25.08s | No fixture edit; no control repeat. |
| S `compiler-collision` (S:169) | Pre-existing compiler output survives; build is refused; P3 | PASSED 26.18s | No fixture edit; no control repeat. |
| S `existing-output-directory` (S:170) | Pre-existing output directory prevents the build; P3 | PASSED 31.65s | No fixture edit; no control repeat. |
| S `existing-output-symlink` (S:171) | Directory symbolic-link output and target survive; build is refused; P3/P4 | PASSED 25.01s | No fixture edit; no control repeat. |
| S `missing_receipt_is_not_negative_success` (S:240) | Absent negative receipt is not accepted as success; Receipt helper only | PASSED 0.00s | No fixture edit; no control repeat. |
| T `local_output_property_tampering_is_reported` (T:19) | TreatAsLocalProperty output overwrite is detected; resulting bytes remain; P1/P2 | FAILED 12.62s | result.Output should contain refused: tampered; F2; isolated Git: PASSED |
| T `intermediate_output_path_tampering_is_reported` (T:39) | Generated tracked AssemblyInfo change is detected and retained; P1/P2 | PASSED 176.28s | No fixture edit; no control repeat. |
| T `generated_target_file_tampering_is_reported` (T:59) | Custom generated version text change is detected and retained; P1/P2 | PASSED 116.62s | No fixture edit; no control repeat. |
| T `imported_props_reference_tampering_is_reported` (T:81) | Imported dependency's local output overwrite is detected; P1/P2 | FAILED 19.60s | result.Output should contain refused: tampered; F2; isolated Git: PASSED |
| T `transitive_reference_tampering_is_reported` (T:118) | Transitive dependency's local output overwrite is detected; P1/P2 | FAILED 21.43s | result.Output should contain refused: tampered; F2; isolated Git: PASSED |
| T `dirty_tracked_tree_is_refused_and_unmodified` (T:158) | Already-dirty tracked source refuses without changes; P1 | PASSED 42.07s | No fixture edit; no control repeat. |
| T `clean_untracked_output_build_exits_zero` (T:191) | Clean non-test build preserves source, creates output and reports no TRX; P1/P2 | FAILED 14.47s | tool.Output should not contain refused: dirty-tracked; F2; isolated Git: PASSED |
| T `fresh_checkout_run_executes_a_test` (T:224) | Fresh fixture builds/runs one test with verified build source; P1/P2/P3 | FAILED 66.89s | tool.Exit expected 0, actual 2; DIRTY App/App.csproj; F2; isolated Git: PASSED |
| T `tracked_snapshot_contains_committed_files` (T:257) | PowerShell snapshot includes tracked files outside entry project; P1 | PASSED 10.72s | No fixture edit; no control repeat. |
| T `staged_untracked_file_is_kept_and_indexed` (T:306) | Build-staged ordinary user file survives in worktree and index; P1/P3 | FAILED 23.91s | Git exit expected 0, actual 1; pathspec user-keep.txt did not match; F2; isolated Git: PASSED |
| T `staged_ignored_file_is_kept_and_indexed` (T:320) | Build-force-added ignored user file survives in worktree and index; P1/P3 | FAILED 49.07s | Git exit expected 0, actual 1; pathspec user-ignored.dat did not match; F2; isolated Git: PASSED |
| T `index_added_file_is_kept_and_indexed` (T:335) | Build update-index-added user file survives with invalid source receipt; P1/P3 | PASSED 130.81s | No fixture edit; no control repeat. |
| T `passing_script_requires_writable_source_receipt` (T:459) | Passing test cannot qualify when source.json cannot be written; P3 | FAILED 16.43s | result.Output should contain source.json; actual dirty-tracked; F2; isolated Git: PASSED |
| O `imported_props_build_preserves_the_tracked_dependency` (O:22) | Imported props graph preserves tracked Lib DLL/assets and confines new outputs; P1/P2/P3 | FAILED 164.99s | Owned script Lib.dll should exist (false); DIRTY Lib/obj/project.assets.json; F2; isolated Git: PASSED |
| O `directory_build_props_build_preserves_the_tracked_dependency` (O:31) | Directory.Build.props graph preserves tracked dependency and confines output; P1/P2/P3 | FAILED 111.92s | Owned script Lib.dll should exist (false); DIRTY Lib/obj/project.assets.json; F2; isolated Git: PASSED |
| O `conditional_import_build_preserves_the_tracked_dependency` (O:40) | Conditional import graph preserves dependency and confines output; P1/P2/P3 | FAILED 147.07s | Owned script Lib.dll should exist (false); DIRTY Lib/obj/project.assets.json; F2; isolated Git: PASSED |
| O `transitive_import_build_preserves_the_tracked_dependency` (O:49) | Transitive import graph preserves dependency and confines output; P1/P2/P3 | FAILED 166.16s | Owned script Lib.dll should exist (false); DIRTY Lib/obj/project.assets.json; F2; isolated Git: PASSED |
| O `unevaluable_import_is_refused_before_the_build` (O:58) | Missing import refuses graph evaluation before build; sentinel survives; P1/P2 | FAILED 18.35s | output should contain refused: graph-unevaluated; F2; isolated Git: PASSED |

## Rerun recipe

The ignored `run-class.ps1` and `run-control.ps1` under the evidence root retain
the exact environment and commands. After reapplying the recorded temporary
gate patch, build once and use a fresh results directory for each class:

```powershell
pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1199win6a-build -- dotnet build tests/Antiphon.Tests --property:OutputPath=bin-c1199win6a/ --nologo -v:minimal
pwsh -NoProfile -File .antiphon/probe-6a38c2dd/run-class.ps1 -Class CheckpointShapeConflictTests -RunName new-shape-native
pwsh -NoProfile -File .antiphon/probe-6a38c2dd/run-class.ps1 -Class CheckpointTrackedBaselineTests -RunName new-baseline-native
pwsh -NoProfile -File .antiphon/probe-6a38c2dd/run-class.ps1 -Class CheckpointOutputConfinementTests -RunName new-confinement-native
```

Each helper invokes `dotnet run --project tests/Antiphon.Tests --no-build`
under `scripts/build-slot.ps1`, with that same OutputPath, the named class
filter, `--report-trx --report-trx-filename results.trx`, and a new results
directory. Diagnostic control filters are stored in the corresponding
`*-filter.txt`; they list only initially failed methods. Their sole extra
runtime change is the two Git environment variables above. The source gate
delta itself is retained in `gates.patch`; it was not committed as a fix.

## Timing and execution transcript

| Run | Selected | Passed / failed / skipped | TRX interval | Outer wall incl. slot | TRX SHA-256 |
|---|---:|---|---|---|---|
| `shape-native` | 17 | 7 / 10 / 0 | 771.72s | 779.16s | `a73c91239b49c2099c4a2444e4832314d601e8c7b9cea6bcf466897c37adb6c7` |
| `baseline-native` | 13 | 5 / 8 / 0 | 703.59s | 712.92s | `788229b46a6af735c12ec1fb8bc6a9ae4e4aa44ad13fad4c5ba6cbb2c5e6cebe` |
| `confinement-native` | 6 | 1 / 5 / 0 | 667.92s | 680.54s | `e67fdccfa2c971a4ea4d0075d655915bd1619a740be611fdc4d5d8a7922170ae` |
| `baseline-config-control` | 0 | 0 / 0 / 0 | 0.26s | 9.02s | `0759205552d20514fd9e8d7a8e8e98dd65595c024b7904db8659219a69987a7f` |
| `baseline-config-selected` | 8 | 8 / 0 / 0 | 797.40s | 805.22s | `8f0b950517d07d9712c45ed4048fc5d1fd8d1737121141e0d241cd7189bd034f` |
| `confinement-config-selected` | 5 | 5 / 0 / 0 | 448.44s | 504.14s | `0605b015353e5cd5669dd6966fefde0220e91ed5a8a2a307b36413a83876a5f0` |

Unedited slot provenance (labels above the lines identify their log):

```text
# build
BUILD SLOT granted lease=53e77d01-a255-4f6b-980e-3ea9e2bff1f7 waited=0s maxcpucount=4
BUILD SLOT released lease=53e77d01-a255-4f6b-980e-3ea9e2bff1f7 held=599s
# shape-native
BUILD SLOT granted lease=30108bdc-f780-426b-ba0b-62417fc8639b waited=0s maxcpucount=4
BUILD SLOT released lease=30108bdc-f780-426b-ba0b-62417fc8639b held=778s
# baseline-native
BUILD SLOT granted lease=2171bb6a-366d-4c98-a5f7-4b4f0f059e0b waited=0s maxcpucount=4
BUILD SLOT released lease=2171bb6a-366d-4c98-a5f7-4b4f0f059e0b held=711s
# confinement-native
BUILD SLOT granted lease=eab8fc9f-6ef2-4e44-a9ec-27c747ebdb51 waited=0s maxcpucount=4
BUILD SLOT released lease=eab8fc9f-6ef2-4e44-a9ec-27c747ebdb51 held=678s
# baseline-config-control
BUILD SLOT granted lease=f0bb610d-024c-468f-b9fb-4e49f9ca603a waited=0s maxcpucount=4
BUILD SLOT released lease=f0bb610d-024c-468f-b9fb-4e49f9ca603a held=7s
# baseline-config-selected
BUILD SLOT granted lease=af437231-7c23-438c-8474-a8e0e0cb98a9 waited=0s maxcpucount=4
BUILD SLOT released lease=af437231-7c23-438c-8474-a8e0e0cb98a9 held=803s
# confinement-config-selected
BUILD SLOT granted lease=5e651cde-9e6e-407f-b255-1be58cf76c4f waited=45s maxcpucount=4
BUILD SLOT released lease=5e651cde-9e6e-407f-b255-1be58cf76c4f held=457s
```

Per-test durations and exact identities are retained in fresh TRX. Timing JSONL
has complete test start/end records but empty phase/process arrays for these
fixtures: fixture, filesystem and subprocess component timings are unknown,
not zero. Each case is a single timing sample, not a measured p95. The recorded remainder is not attributed to a guessed subsystem.
No timeout was increased and no retry was used to relabel the original failures.
This selection is narrow; no full-suite or unrelated native tests were run.
The three initial class invocations aggregate 35m43.591s of console-reported
test time (including the 56.273s Windows sibling), excluding the 9m56.13s
build. The aggregate exceeds the 30-minute R1 threshold in
`docs/test-performance-standards.md:10`, although no individual invocation
does. This timing requires a caller-owned Debug/performance follow-up; the
delegate contract forbids sub-delegation. The recorded filters, host, source,
individual durations and missing phase attribution supply its evidence.

## Effort ranking supported by evidence

1. **Observed gate-only tier: 12 rows.** Seven ShapeConflict and five
   TrackedBaseline rows passed without any fixture edit. This is one host/run,
   not a promise of identical outcomes under every Git configuration. The shared
   TrackedBaseline setup still has the F2 portability sensitivity.
2. **Common configuration tier: 13 failed rows.** Eight TrackedBaseline and
   all five requested OutputConfinement rows share F2; their assertions use
   available Windows Git/MSBuild behavior. Controls are reported separately.
3. **Shell arrangement tier: 10 failed rows.** These require a fixture port
   before their product behavior can be qualified on a native Windows host
   without POSIX executables. They do not establish ten product gaps.
4. **Product implementation tier: zero demonstrated by this investigation.**
   A days/files/lines estimate for a missing implementation would be invented.
   Native junction and race qualification remain separate, unmeasured R-2 work.

The measured cost is dominated by building and running the existing proof
fixtures, not by changing three gates. Calendar fix estimates and a Code task
design are deferred to Plan by the evidence-only stage contract.

## Remaining uncertainty and R-2

The Windows symbolic-link row uses `Directory.CreateSymbolicLink` (S:382).
Its pass proves that arrangement on this host. It does not execute a junction
variant, establish a privilege-free file-symlink fixture, prove a hostile
same-account race or qualify process custody on all Windows configurations.
The 35 rows do not cover every R-2 obligation. F2 is classified as fixture configuration for these rows; whether ignoring a user-global line-ending policy is acceptable for ordinary Windows checkouts is a separate product-policy question, not resolved here. No missing native API implementation is inferred from F2. Assertions that failed before
the intended build scenario provide no downstream product guarantee.

Retained disclosure: **"Native Windows junction qualification remains pending.
The CARD-1199 directory-link result in this investigation used a symbolic link,
not a junction. Process-custody race qualification and other R-2 obligations
outside these 35 rows are not closed by this report."**

No newly confirmed checkpoint deletion or output-escape defect was found.
Expected build-authored changes to scratch tracked files are the detect/report
contract, not evidence of the old automatic-cleanup bug. The failed shell
arrangements and early configuration refusals must not be counted as successful
security-path execution.

## Restoration and publication

The three temporary gate edits were restored byte-for-byte from their saved
originals, verified by SHA-256 and an empty tracked source diff. Before restore,
the edited source hashes still matched the build provenance. All foreground
runs and children had exited; the remaining workspace-named process was the
inspection shell itself. All 34 producer-owned `bin-c1199win6a` directories
were removed using their exact literal paths after containment and reparse
checks. A final recursive inventory found zero such directories. No c723
fixture repository remained. Raw receipts, timings, gate patch and the small
ordinary Git comparison scratch remain ignored under the evidence root.

The final TRX audit checked exact class names, row counts, outcomes and equality
between each failed-method set and its control set (`verified-census.json`).
No retained product or test change exists; the only committed deliverable is
this investigation. Full source cleanliness was restored before the final
documentation commit. Windows gates and release policy are unchanged.

Every completed evidence slice was committed and pushed to the assigned branch:

| Commit | Evidence slice |
|---|---|
| `dfdd9a903057ad799e5edda48066c6e16c06febb` | Investigation skeleton, before the long build |
| `eda726fdbb8fdd85c482f6babe26e01fea363c4e` | First native class |
| `d030378ae559f565245eeaa59eaf26de20878034` | Complete initial 35-row census |
| `354c9f507212ffe4bf137f72016971ebc0127cec` | Eight passing configuration controls |

The final documentation commit and target-merge check are reported in the
settlement message. No tag, release publication, rebase, merge or source fix
was performed.

## Not done, noted

Gate and fixture portability work is the next candidate; fix design, a Code brief, line/hour estimates, product edits and new junction/race probes were not performed under this evidence-only Investigate contract.

--- next stage ---
next: plan
handoff: Use the measured gate-only results and confirmed fixture causes to scope Windows portability; keep the unexecuted junction and broader R-2 obligations explicit.
artifact: docs/investigations/2026-10-30-card-1199-windows-runnable-feasibility.md
