# Corrected qualification failures: groups 104 and 105

The caller's correction supersedes the original Substring premise: that exception
came from the caller's extraction script, not a test. The original assertions are
now available. A concrete Windows encoding-fixture defect has been repaired;
group 104's underlying refusal remains unconfirmed pending native diagnostics.
No short-SHA product defect or common cause for all nine tests is claimed.

## Findings against the corrected evidence

| Failure | Shared path and what the assertion establishes | Remaining uncertainty |
|---|---|---|
| Six census methods: empty sources/PCs/census or exit 2, missing expected diagnostic | `CoverageCommand.Run` reads fixture files directly through `ConfinedFileReader` -> `CheckpointFileReader`. An input refusal returns an invalid report with `INPUT_INVALID`; an internal exception returns `ANALYSIS_INVALID`. Both bypass the expected census/checklist diagnostics. There is no Git invocation in this path. | The supplied failure messages omit the actual report diagnostics. The new native diagnostic prints the original invalid report before disposal, without re-reading or retrying the file. |
| Repeat-host method: `trx.Failed == 0` | `TrxReport.Parse` catches `ReceiptFile.ReadText`/XML errors and returns a bad result with default zero counts. The original test inspected `Failed` without first asserting `Ok`. Thus this message does not establish that later repetitions erased the failure. | The original parse error and child output are absent. The test now asserts `Ok` first with the parse error and captured child output. If parsing succeeds, the original count and repetition assertions still apply. |
| Scheduler method: one green row | Fake builds/runs still write/read real build-binding receipts, ownership stamps and TRX files. A refused receipt/TRX or other row refusal can leave one row nongreen despite correct build serialization. | The original row state was not retained. The assertion now includes state, terminal/slot reason, TRX error, CHECKPOINT line and output. No serialization or count assertion changed. |
| C1608 encoding script exits 1 at its refusal assertion | The Windows fixture wrote `git.cmd` into PATH, but the production helper starts `git` with `UseShellExecute=false`. Windows CreateProcess appends `.exe` to an extensionless executable name and requires an explicit interpreter for batch files. The shim was not a valid way to select the intended raw-byte producer. | The caller omitted the actual `Reason`/`Exit` values, so the historical process/exit cannot be independently reconstructed. The exception's phrase "invalid UTF-8 accepted" also covers launch/git failures; it alone does not prove successful decoding of invalid bytes. Native confirmation remains owed. |

The file reader is a genuine common dependency of the eight group-104 methods,
but that is a hypothesis boundary, not a demonstrated common failure. The one
Linux scheduler red below lasted **0.7138677 s**, shorter than the reader's
three-second stall deadline; it cannot be labelled a reproduced three-second
stall. Native timing (repeat 5.7 s, scheduler 4.6 s and two census methods 12.6/16.5 s)
is consistent with several possibilities and does not supply the missing reason.
No timeout was widened, assertion relaxed, retry introduced or test skipped.

Windows executable semantics are documented by Microsoft:
[CreateProcessW](https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-createprocessw)
and [ProcessStartInfo.UseShellExecute](https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.processstartinfo.useshellexecute?view=net-10.0).
This establishes the fixture defect by source/contract comparison; the new Linux
witness establishes actual executable binding and byte rejection, not native execution.

## Candidate versus rc/master; missing top-ups

Inspected native candidate `cde8def4163829aa5b867df6ec625433be2522cb`, assigned rc
`335b4fbb3b1eac700cb8d3c64ea032d9579c6b5f`, and fetched master
`31f990317038635c2f1e652b6a950905fcf07a31`.

| Change | Comparison and applicability |
|---|---|
| `26e812ef10c1624abf8893e680836b21629f4365` / `ad79edb0ebfa8cfeb32262ac1acc0dba4dc6b1e8` | Red witness then 8,000-character preflight batching and explicit Win32 start diagnostics. This fixes long write-root command lines. Census never invokes preflight Git; repeat-host parsing never invokes it. Scheduler's tiny fake project roots are not an aggregate solution. Native TEMP location is unknown; roots inside a checkout can still invoke preflight. No native disappearance is claimed. |
| CARD-1616 `f44e1c75e45608aadc90a7171216fd98f7321060` | Avoids redundant Git driver discovery for the exact index-only query. Other changes retain admission/terminal receipt state. The scheduler change does not change a successful fake run's expected two green rows. Reduced process pressure may help aggregate runs, but that is not proof of a repair for these failures. |
| Fixture isolation `b3a1971f35b2f4fe29c6759a6bdf9778c85a9a21` | Isolates Git/config/NuGet children in the S-B1 confinement/shape/tracked-baseline fixtures. None of the nine named methods uses the added environment helper. Neither autocrlf isolation nor source checkout shape explains the in-process census directly. |
| Shared reader and coverage/TRX code | `git diff cde8def41..335b4fbb3` and `cde8def41..origin/master` show no changes in `CheckpointFileReader`, `CheckpointNativeIo`, Coverage, Trx, `BuildConfinement`, or the encoding script. The named original test sources are unchanged too. Reading the later commits therefore does **not** establish that groups 104/105 disappear on rc/master. Linux portability passes below remain Linux evidence. |

An existing unmerged change, `87ba567ec507789050dd728c96f2efd53573916c`
("keep Windows checkpoint reads on the dedicated IO thread"), replaces Windows
overlapped/pool completions with the dedicated synchronous stream in compiled and
embedded readers. It is absent from the compared rc/master; it appears in several
remote task branches, including `origin/feat/card-task-0a72f317`. It is a relevant
existing repair candidate for the reader hypothesis, **not** a change validated
against d47848cc. No duplicate production reader fix was applied here.

CARD-1611 is a different call site: `BuildConfinement.Git` still calls
`CheckpointProcessRunner.Run`, whose stdout is text decoded by StreamReader.
C1608 instead calls `Invoke-CheckpointGit` -> `RunBytes` and strict UTF-8 decoding.
The encoding script does not call `BuildConfinement.Git`. CARD-1611 was neither
claimed fixed nor widened into this fixture repair.

## Repair and red-first witness

`76090079a1e6a9d2c09f1ece0518d5aff3c88f9b` adds a minimal staged native Git fixture and
`C1608_invalid_utf8_comes_from_the_configured_executable`. It supplies an explicit
executable and requires its invocation record after the encoding checks pass.
Against the old script: **1 executed, 0 passed, 1 failed, 0 skipped**, exit 2;
failure is precisely `File.Exists(invocationLog)` with the assertion
"the configured executable must serve the actual git queries". The old Linux
PATH shim still passed its UTF-8 checks, proving why those checks alone could not
establish that the configured executable was used. Launch wall: 5.7597808 s.
This is an assertion red, not zero tests or a build/fixture error.

`0a8d0adccdd6ae41c98633060e09a914ed54554e` removes the platform shell shims. The script requires an existing
absolute native executable and replaces only `FileName` in its imported production
function, using a checked one-occurrence seam like its existing legacy-encoding
seam. Production Git hardening, the config query, process custody, raw capture,
strict decoding and both refusal assertions remain intact. The native helper
accepts the hardening prefix, returns an empty config query, and writes byte FF
only for status/ls-files. The new witness requires the real sequence
`config, status, config, ls-files` from the helper. Unexpected commands exit 91.

The helper is built once with the parent and staged once per output tree; no
per-test compilation/restore or app/container/database setup was introduced.
Parent builds use `UseAppHost=false`; only this small fixture locally requires
`UseAppHost=true` because selecting a native executable is the behavior being
tested. Its alternate output has no standing process owner and is cleaned after
all children exit. Existing process limiting and 120-second child cap are retained.
The cap is inherited, not a new measured p95 or a widened timeout.

`fed180b9acc837512cd64382db242ba3ce67c81f` adds the group-104 diagnostics above.
The code under verification is that committed tip. New run results, current graph
check, cleanup and the explicit native commands are appended after completion.

## Corrected verification and gate status

All commands completed in the foreground. No test or build remains running.

| Context at tested commit `fed180b9acc837512cd64382db242ba3ce67c81f` | Executed / passed / failed / skipped | Launch wall seconds | Sum of TRX body seconds |
|---|---|---:|---:|
| Normal assigned worktree (nine original methods, new witness, adjacent legacy tamper control) | 11 / 11 / 0 / 0 | 457.56 | 27.12 |
| Exact source archive export without .git (nine original methods plus new witness) | 10 / 10 / 0 / 0 | 373.64 | 19.65 |

**New green selection: 21 executed, 21 passed, 0 failed, 0 skipped.** The earlier
assertion red is 1 executed / 1 expected failure, not credited as a passing test.
Historical 55/56 plus two diagnostic passes and seven group-105 passes remain
recorded below; the unexplained base scheduler red has not been erased.

The archive was created from the tested committed source, then received only the
built entry output and its NuGet evaluation metadata. Both test assembly SHA256s:

`703e4c5cd3250d358dd37e280cf48bfb8b4b14f01fa1adb273532c1c02702fae`.

The export's own Git query returned 128 / not a repository. Exact method filters
were used individually, sequentially under a lease, with TEMP=/tmp and native
diagnostics/phase recording enabled. The separate legacy-mode run checks the
unchanged branch of the modified script. No broad/full suite was rerun.

| Driver | Outcome | Elapsed / held / admission wait seconds |
|---|---|---|
| Red build, 76090079a | 0 errors, 1395 warnings | 307.93 / 309 / 30 |
| Red exact method | Expected assertion red, 1/1 failed | 5.76 launch / 6 / 75 |
| Green build, fed180b9a | 0 errors, 1395 warnings | 248.63 / 249 / 120 |
| 21 methods plus graph Check | Tests green; driver exit 1 from graph refusal | 831.19 launches / 849 / 105 |

Per-test launch/body data are in `corrected-durations.json`; phase JSONL is in
`corrected-timing/`. Unmeasured CLI/discovery/startup/teardown subdivision remains
unknown/remainder; it is not assigned to the test body. No Docker, AppHost, DB,
migrations or seed were used. These are single observations, not per-case p95.
The batch stayed below 30 minutes; no further repetitions were purchased.

The **real** land graph command was run once for the corrected code, in the same
lease, without -Update:

`pwsh -NoProfile -File scripts/audit-land-verification-graph.ps1 -Check -ToolPath tools/Antiphon.LandGraphAudit/bin-g104/Antiphon.LandGraphAudit.dll`

It **refused**, exit 1, before approving evaluation/canary:

```text
digest=E7C6EDAFA83B544A42E186899267267FD73E6FDD2FB7DD2DF36C233F2C405806 count=69
manifest does not match the selected graph
audit refused: land graph pre-check refused
```

**Definition files changed:** the new fixture csproj and the two staging/reference
entries in Antiphon.Tests.csproj. The count changed from 68 to 69. No manifest,
evaluation record, pins, global.json, instruction, test selection policy or schema
was changed; -Update was not run. The earlier graph passes at count 68 are
historical and do not clear this refusal. This branch is **not landable** under
the current gate. A separately authorized build-definition review/audit is needed
if the caller retains this helper project. The brief explicitly prohibits doing
that update in this task.

The required fetch/master merge-tree probe also returned 1. It was read-only:
no merge, rebase or conflict resolution was performed in the assigned branch.
Against fetched master 31f990317038635c2f1e652b6a950905fcf07a31, conflicts are:

- .antiphon/task-af2b78c8.md
- docs/investigations/2026-10-25-card-1584-e2e-launch-bounds.md
- docs/investigations/2026-10-30-card-1618-sf-aggregate-run-debug.md
- scripts/test-checkpoint-git-encoding.ps1
- server/Application/Services/CapacityRecoveryService.cs
- tests/Antiphon.Tests/Application/CapacityLockOrderTests.cs
- tests/Antiphon.Tests/Checkpoints/CheckpointGitEncodingScriptTests.cs

This task started from rc 335b4fbb3, not master. The caller's landing mechanism
must port the task slices; this report does not approve a direct master merge.

All **37** corrected-run alternate output roots (36 producer roots and the export
entry output) were removed by recorded individual literal paths after all runs
finished. A subsequent read-only walk found no remaining bin-g104 in the assigned
worktree or any of the four scratch source trees. Receipts, immutable logs,
source archive, hashes, scripts and cleanup records remain outside Git at
`/work/evidence/debug-21443bfd/`. Generated JSON/TRX/log evidence was not committed.

Changed source/test files: scripts/test-checkpoint-git-encoding.ps1;
tests/Antiphon.Checkpoints.GitFixture/{Antiphon.Checkpoints.GitFixture.csproj,Program.cs};
tests/Antiphon.Tests/Antiphon.Tests.csproj; and the Checkpoints test files
CheckpointGitEncodingScriptTests.cs, CheckpointRepeatHostTests.cs,
PlanCoverageCensusTests.cs and RunSchedulerTests.cs. This Markdown is the durable
report; .antiphon/task-21443bfd.md is its ignored full-report copy.

## Retained earlier evidence and identity-prefix sweep

The following sections preserve results, costs and the sweep from the pre-correction
reports. Their absent-Substring-stack premise is superseded above; they are not
a current request to find that nonexistent test exception.

## Execution-path findings

- `PlanCoverageCensusTests.Make/Run` writes C# and checklist files in fixture-owned
  temp roots and calls `CoverageCommand.Run` in process. Its public CLI test calls
  `Program.RunAsync` in process with a fake driver and launch/wait callbacks that
  throw if reached. These methods do not invoke a PowerShell script.
- `RunSchedulerTests.builds_never_overlap_even_with_free_slots_and_width_two`
  uses `FakeDriver`, task gates and fake source/slot observations. Its build/run
  requests are recorded rather than starting PowerShell or dotnet builds.
- `CheckpointRepeatHostTests.RunHost` launches `dotnet` against the staged
  `Antiphon.Checkpoints.RepeatHost.dll`, with an explicit TRX directory and a
  fixture-owned TEMP/TMP/TMPDIR. It does not invoke PowerShell.
- `CheckpointTestBase` and `CheckpointTestScope` allocate/clean fixture-owned roots
  using the C# host-cleanup implementation. `ReadOnlyDirectory.ClearReadOnly` is
  C# filesystem traversal. The shared assembly timing receiver is also C#.
- `rg -n 'Substring' tools/Antiphon.Checkpoints src/Antiphon.HostCleanup` returns no
  matches. The selected test methods have no inline PowerShell code either.

The initial report treated outer qualification/report processing as an unconfirmed
hypothesis. The caller subsequently confirmed its own extraction-script failure;
that correction and the real assertion failures supersede the original premise.

## Prefix-truncation sweep

Search: all `.ps1`/`.psm1` files under `scripts/`, including `scripts/lib/`, for
`Substring`, with inspection of fixed-prefix and identity-like values. The current
checkout contains 110 `.Substring(0, ...)` occurrences, including text excerpts,
source-parser operations and literal fixture source strings. This is an inventory,
not a claim that 110 sites can receive an empty identity.

| Site (assigned base line) | Value truncated | Existing bound / reachability / disposition |
|---|---|---|
| `scripts/run-checkpoint.ps1:671` | New GUID formatted `N`, first 4 characters | Producer always returns 32 characters; not a Git SHA. No source-identity prefix truncation exists in this script. Candidate equivalent is line 606. |
| `scripts/run-checkpoint.ps1:825` | MSBuild property key before `=` | Delimiter validated before slicing; not a SHA. |
| `scripts/run-checkpoint.ps1:918` | Git index record header before TAB | TAB and nonempty following path validated first; not a fixed SHA prefix. Git failure returns `git-head` before index processing. |
| `scripts/delegate.ps1:545` | observed served identity, first 7 | Explicit `Length -ge 7` guard. Not on the eight test paths. |
| `scripts/delegate.ps1:555` | checkout HEAD, first 7 | Nonblank and length guard. Not on the eight test paths. |
| `scripts/delegate.ps1:1239` | continuation **task ID**, first 8 | No local guard; malformed/missing response metadata could throw. This is not a commit SHA or a call reached by these eight methods. Changing it would not establish a group-104 repair. |
| `scripts/checkpoint-task.ps1:97` | task ID, first 8 | Blank/short ID returned before slicing. |
| `scripts/check-daemon-build.ps1:67` | daemon commit SHA, first 7 | Uses `Min(7, Length)` after nonblank check. |
| `scripts/lib/nightly-run-impl.ps1:27` | run ID, first 8 | Slices only when length exceeds 8. |
| `scripts/lib/release-gate.ps1:152` | candidate SHA, first 12 | Requires `Test-ReleaseGateFullSha`; that predicate rejects blank and requires exactly 40 lowercase hex characters. This guard is present at the native candidate too. Parse-only ref validation intentionally accepts no SHA without manufacturing a candidate ID. |
| `scripts/register-release-gates.ps1:282` | desired definition digest, first 12 | No local guard; producer is `Get-ReleaseGateDefinitionDigest` -> `Get-NightlySha256Text`, which computes 64 hex characters. No checkout HEAD read. Not reached by these eight tests. A replacement/broken producer is a separate hardening scenario. |
| `scripts/lib/test-phase-evidence.ps1:2149` | timing identity, variable prefix length | Checks empty identity and `Identity.Length -lt prefix` before slicing. |
| `scripts/lib/test-timings.ps1:51` | computed SHA256 mutex suffix, first 32 | Computed 64-character SHA256 hex, not Git output. |
| `scripts/cutover-postgres-server2.ps1:113`, `scripts/test-cutover-postgres-server2.ps1:420` | computed hash, first 8 | Hash producer supplies fixed-size digest; no empty Git output. |
| `scripts/test-test-selection.ps1:120,128` | scratch commit B first 8; scratch tree first 2 | Fixture-owned real Git commits/trees, with checked Git calls. Not the outer checkout HEAD or the eight methods. |
| `scripts/test-release-gate.ps1:1386,3069,3673,4472,4796,4893` | real fixture commit identities (`Head`, `B`, `co.Sha`, `C`), first 12 | Scratch Git fixtures check command exit before returning their full SHA. Not the eight methods. |
| `scripts/test-release-gate.ps1:3326,3421,3578,3748,3749,3752,3753` | deliberately full fixture constants / receipt identity, first 12 | Used to create explicitly abbreviated negative fixtures and candidate IDs. No caller checkout identity read at these sites. |
| `scripts/test-release-gate.ps1:3915` | checked second fixture commit SHA, first 12 | Real scratch commit. Not the eight methods. |
| `scripts/test-release-gate.ps1:4567` | candidate fixture argument `Sha`, first 12 | `Set-C1339Candidate` accepts a caller argument; current callers supply complete fixture SHAs. It would throw for an independently injected short argument, but is not a group-104 call. |

Other fixed numeric identity prefixes in scripts are directly generated
`Guid.NewGuid().ToString('N')` values (32 characters), including deployment/run/temp
nonces. Short/empty Git identities cannot flow into those expressions. Variable
class-name/delimiter slices were also inspected; they do not consume a Git SHA.
The Windows string-only witnesses `c487-readonly-windows-witness.ps1:22` and
`group77-windows-input-witness.ps1:72` slice literal Windows parent paths, not SHAs,
and are not called by the selected tests.

There is no evidenced short identity on a selected execution path to fix. No tests
were removed, skipped, relaxed, or given longer timeouts. No skip marker was added.
No arbitrary empty-string `.Substring()` demonstration was substituted for a causal
regression witness. The original short-SHA witness/fix premise was withdrawn by the correction.
The actual executable-binding red-first witness and repair are recorded above.

## Historical portability verification

The base matrix ran at clean code commit
`335b4fbb3b1eac700cb8d3c64ea032d9579c6b5f`: **56 executed, 55 passed,
1 failed, 0 skipped**. The failing scheduler method then passed once in isolation
and once in a normal control at diagnostic-only commit
`be4d1da22d864bf80166fcd85d323452418f0e35`. These two passes do not erase the
base failure or demonstrate a product repair.

| Base context | Git HEAD observation | Executed / passed / failed / skipped | Sum of individual launch wall seconds |
|---|---|---|---:|
| Normal assigned worktree; GIT_DIR unset | full `335b4fbb3...` | 8 / 8 / 0 / 0 | 210.20 |
| Depth-1 clone, detached HEAD | full `335b4fbb3...`, shallow=true | 8 / 8 / 0 / 0 | 207.12 |
| GIT_DIR points to an unborn repo | rev-parse exit 128 | 8 / 8 / 0 / 0 | 205.13 |
| Physical exported source with its own unborn `.git`; timing enabled | rev-parse exit 128 | 8 / 8 / 0 / 0 | 268.69 |
| Shallow clone attached to `qualification/nested/rc.unusual-21443bfd` | full `335b4fbb3...` | 8 / 8 / 0 / 0 | 262.52 |
| `git archive` export, no `.git` | rev-parse exit 128, not a repository | 8 / 8 / 0 / 0 | 196.22 |
| GIT_DIR points to a nonexistent path | rev-parse exit 128, not a repository | 8 / 7 / 1 / 0 | 328.54 |

Each row used eight **individual exact method filters**, one executed test per
invocation. The selection was:

| Class | Method |
|---|---|
| CheckpointRepeatHostTests | native_failure_in_middle_ordinal_survives_later_passes |
| PlanCoverageCensusTests | census_rejects_unselected_or_non_test_roster_methods |
| PlanCoverageCensusTests | opt_out_preserves_legacy_reports_and_declared_counts |
| PlanCoverageCensusTests | census_public_cli_is_deterministic_and_read_only |
| PlanCoverageCensusTests | census_compares_resolved_identities_not_counts |
| PlanCoverageCensusTests | checklist_flag_rejects_invalid_or_conflicting_input |
| PlanCoverageCensusTests | census_reports_unmapped_selection_without_dynamic_discovery |
| RunSchedulerTests | builds_never_overlap_even_with_free_slots_and_width_two |

The command shape was `dotnet run --project tests/Antiphon.Tests --no-build
--property:OutputPath=bin-g104/ --property:UseAppHost=false -- --treenode-filter
'/*/*/<Class>/<Method>' --report-trx ...`. PowerShell matrix drivers ran those
commands sequentially under one host build-slot lease per driver; there were no
concurrent test children. TEMP was `/tmp`. All outputs were fresh per state/method.

The initial isolated build through `scripts/build-slot.ps1` passed: 0 errors,
1395 warnings, 145.43 s MSBuild elapsed, 146 s lease held, 0 s admission wait.
The diagnostic rebuild also passed: 0 errors, 1395 warnings, 127.68 s MSBuild
elapsed, 128 s lease held, 120 s admission wait. Both used `UseAppHost=false`
and `OutputPath=bin-g104/`.

The copied test assembly SHA256 before the diagnostic change was identical in
all four trees:
`2059b88f83630f0051e7f1ace3aa5664251cbaaf9dd9b58d7afddb1cfe5d4064`.
The scratch copies included the built output and the entry project's generated
NuGet props/targets/assets, so `dotnet run --no-build` could evaluate TUnit's
executable project type. The physical source trees came from the same base;
repository discovery occurred in the copied tree, not the original source root.

### Actual scheduler failure and diagnostic change

The last base matrix invocation failed at `RunSchedulerTests.cs:37`:

```text
ShouldAssertException: result.Rows.Count(row => row.State == "green")
    should be 2 but was 1
```

This was a C# assertion failure on the unchanged assigned base, not a PowerShell
`MethodInvocationException`. The original fixture removed its scratch roots in
teardown, and that assertion did not include the rejected row's state/output.
Its underlying refusal is therefore unknown; do not infer a missing SHA from it.

Commit `be4d1da22d864bf80166fcd85d323452418f0e35` adds each row's ID, state and
captured output to that assertion's failure message. It preserves the expected
two green rows and both build-overlap assertions. No timeout, retry, fixture
selection, production guard, or skip behaviour changed. No product repair is
claimed. Native follow-up can now retain this information if the assertion fails.

At that commit, exactly one isolation invocation with the same invalid GIT_DIR
passed (1/1, zero failures/skips, launch wall 3.99 s), and exactly one normal
control passed (1/1, zero failures/skips, launch wall 4.37 s). This establishes an
intermittent portable failure, not a causal fix. No further reruns were used to
replace the historical result.

### Invalid harness attempts retained, excluded from credit

1. The first compound class/method OR filter selected zero tests (exit 8). It was
   replaced by individual exact filters; it supplies no verification credit.
2. Eight first attempts in the shallow copy did not launch a test host: the
   manually copied tree lacked generated NuGet imports, so `dotnet run --no-build`
   saw OutputType=Library. Those are setup failures, not eight failed tests. After
   copying the entry project's generated metadata, all eight were run and passed.
3. The first matrix driver then aborted while preparing its unusual-ref state:
   the previous unborn GIT_DIR had not yet been cleared for the scratch git call.
   The driver was corrected to set the state environment before preparing refs.
   No unusual-ref/export/missing-dir credit was claimed from that aborted driver.

These were introduced investigation-harness errors. None reproduced Substring or
required a repository code workaround. Both original logs are retained unedited.

### Land graph and performance evidence

No definition file changed. `-Update` was never run. The **real** land graph
`-Check` passed at both the base and diagnostic commits, including both evaluation
passes and the canary. Digest:
`7BDABBA075B620C10A47B50CE1277A932DDD801E9314A932C4EAC03885441A99`, count 68.
The final check ran inside the diagnostic driver's lease, after GIT_DIR restoration.
The first check held a separate lease for 43 s after 60 s admission wait. The last
diagnostic driver held its lease for 38 s after 15 s admission wait.

Main matrix admission waits were 105 s and 165 s, separately from test execution.
The valid 56 launch walls total about 1678.4 s (28.0 minutes), with each launch
including dotnet/MSBuild evaluation, discovery, assembly setup and teardown.
Retained TRX files carry test durations; these are not the same as launch wall.
No p95 is claimed from heterogeneous one-per-context samples. Per-phase and
external-call time is unknown where no instrumentation record exists, not zero.
The physical-unborn row enabled the existing qualification timing recorder and
retained its JSONL shards. The costs and invalid setup attempts remain visible;
no deadlines were widened to conceal them. This Debug selection did not justify
running an unrelated assembly-wide suite.

Raw logs, exact drivers, hashes, summary JSON, per-method TRX/HTML and timing shards
are retained outside the worktree at `/work/evidence/debug-21443bfd/`. Generated
payloads are not committed. The report records the essential provenance and counts.
All test/build commands completed before cleanup. The 35 task-owned worktree
`bin-g104` roots and the three scratch-copy output roots were removed by their
individual recorded paths; no wildcard deletion or daemon restart was used.


## Historical group-105 refinement before the correction

The caller's refinement added qualification group 105 (19:47Z), specifically
`CheckpointGitEncodingScriptTests.C1608_invalid_utf8_git_output_is_refused`
(`CheckpointGitEncodingScriptTests.cs:48`, assertion in `Run`). This expands the
reported scope from the original eight tests to **nine tests across groups 104
and 105**. The original matrix and its failure are preserved above; this section
records the additional exact selection rather than replacing that history.

The added C# test starts `pwsh -NoProfile -File
scripts/test-checkpoint-git-encoding.ps1 -Mode encoding`. That script loads the
embedded file reader and shared process runner, then imports checkpoint functions
from the production script's AST. Unlike the census and scheduler methods, this
test does execute PowerShell. Its fixture script and C# test are byte-identical
between candidate `cde8def41` and this branch before the refinement.

On the encoding path, a fixture-owned Git shim deliberately writes invalid UTF-8
byte `0xFF`. `Invoke-CheckpointGit` must reject decoding with `git-encoding` and
empty stdout; `Get-CheckpointUntrackedManifest` must retain the explicit encoding
refusal. This path reads no checkout commit SHA. The encoding fixture script has
no `.Substring()` call. The shared runner's configuration-key slice
`CheckpointProcessRunner.cs:87` uses a newline index from the same string and
checks it before slicing. No additional unguarded source-identity prefix was found
on this path. The then-unexecuted Windows `.cmd` shim has since been retired
by the repair above; actual native validation remains owed.

At that historical stage no Substring exception was reproduced. The caller later
confirmed that its extraction script produced the exception and supplied the real
assertion messages. Those corrected inputs, the executable-binding witness and
repair above supersede the original request for a Substring stack. Native
validation of the repaired fixture remains owed.

At clean commit `27293e7b8d857edac438d19fe4a602cf87b20e79`, the exact filter
`/*/*/CheckpointGitEncodingScriptTests/C1608_invalid_utf8_git_output_is_refused`
ran once per context through the build-slot-gated sequential driver:

| Context | Executed / passed / failed / skipped | Launch wall seconds |
|---|---|---:|
| Normal assigned worktree | 1 / 1 / 0 / 0 | 6.48 |
| Depth-1 detached clone | 1 / 1 / 0 / 0 | 6.33 |
| GIT_DIR points to unborn repository | 1 / 1 / 0 / 0 | 6.95 |
| Physical unborn checkout, timing enabled | 1 / 1 / 0 / 0 | 7.98 |
| Unusual attached ref | 1 / 1 / 0 / 0 | 7.80 |
| Non-git archive export | 1 / 1 / 0 / 0 | 5.07 |
| GIT_DIR points nowhere | 1 / 1 / 0 / 0 | 7.73 |

Additional selection: **7 executed, 7 passed, 0 failed, 0 skipped**. The earlier
base matrix remains **55 passed / 1 failed**, followed by its two diagnostic
isolation/control passes. No result has been relabelled as fixed or clean release
qualification. No test, assertion, timeout, fixture selection, or skip changed for
this refinement.

The rebuild passed with 0 errors / 1395 warnings; MSBuild elapsed 224.75 s, lease
held 226 s, admission wait 0 s. `UseAppHost=false`, `TEMP=/tmp`, and the same
isolated OutputPath were used. The rebuilt test assembly hash was identical in all
four runtime trees:
`17ac9645c1d49600cbfdde371791978f592bbd376209e46021dbbd4189ba78d6`.
The copied source/helper files for this selection were unchanged from the original
base; the normal worktree's HEAD was the stated diagnostic/report tip. Launch wall
for the seven added invocations totalled 48.33 s, separately from the build. The
matrix plus real land-graph check held one lease for 77 s, with 0 s admission wait.

The real land-graph `-Check` passed again, including both evaluation passes and
canary, with the same digest `7BDABBA075B620C10A47B50CE1277A932DDD801E9314A932C4EAC03885441A99`
and count 68. No definition file changed and `-Update` was not run. All commands
completed before the 38 recorded worktree/scratch-copy output roots were removed
again. Raw `refinement-*` and `g105-*` logs, driver, hashes, summary, cleanup record,
TRX and timing shards are retained in `/work/evidence/debug-21443bfd/` alongside
the original evidence.


## Native obligation and next action

| Row | Exact native selection | State |
|---|---|---|
| W-1 | `/*/*/CheckpointRepeatHostTests/*` | Owed after qualification d47848cc releases its native lock. |
| W-2 | `/*/*/PlanCoverageCensusTests/*` | Owed; retain every `CHECKPOINT_NATIVE census` invalid report. |
| W-3 | `/*/*/RunSchedulerTests/*` | Owed; retain row state, TRX error, terminal/slot reasons and CHECKPOINT output on red. |
| W-4 | `/*/*/CheckpointGitEncodingScriptTests/C1608_invalid_utf8_git_output_is_refused` | Owed for the group-105 repair. |
| W-5 | `/*/*/CheckpointGitEncodingScriptTests/C1608_invalid_utf8_comes_from_the_configured_executable` | Owed for the real native executable/byte producer witness. |

These commands are for a newly synchronized assigned Windows worktree, **after**
the native lock is released. They were not executed by this delegate. Do not run
them in, mutate or replace the live qualification checkout.

```powershell
Set-Location -LiteralPath 'C:\Antiphon\worktrees\card-task-21443bfd'
& git rev-parse HEAD # must equal the pushed branch tip in the completion report
$env:ANTIPHON_CHECKPOINT_NATIVE_DIAGNOSTICS = '1'
& pwsh -NoProfile -File scripts/build-slot.ps1 -Label g104-native-build -- dotnet build tests/Antiphon.Tests --property:OutputPath=bin-g104-native/ --property:UseAppHost=false
if ($LASTEXITCODE -ne 0) { throw "native build/slot refused: $LASTEXITCODE" }
$results = '.antiphon/g104-native-' + (Get-Date -Format 'yyyyMMdd-HHmmss')
$filters = @(
  '/*/*/CheckpointRepeatHostTests/*',
  '/*/*/PlanCoverageCensusTests/*',
  '/*/*/RunSchedulerTests/*',
  '/*/*/CheckpointGitEncodingScriptTests/C1608_invalid_utf8_git_output_is_refused',
  '/*/*/CheckpointGitEncodingScriptTests/C1608_invalid_utf8_comes_from_the_configured_executable'
)
for ($i = 0; $i -lt $filters.Count; $i++) {
  & pwsh -NoProfile -File scripts/build-slot.ps1 -Label "g104-native-$i" -- dotnet run --project tests/Antiphon.Tests --no-build --property:OutputPath=bin-g104-native/ --property:UseAppHost=false -- --treenode-filter $filters[$i] --report-trx --report-trx-filename run.trx --results-directory "$results/$i"
  if ($LASTEXITCODE -ne 0) { throw "native row $i refused/failed: $LASTEXITCODE; retain full TRX and stdout/stderr" }
}
```

Keep executed/passed/failed/skipped counts and complete failure messages/stacks,
not the earlier collapsed/truncated extraction. A green targeted rerun does not
retroactively repair an unidentified aggregate-run failure. A recurrence with
`read-stalled`, `opened-handle verification` or a different exact error determines
which product path needs a separate repair; do not widen the existing deadline.

Secondary checkout-provenance checks requested by the original brief remain
useful, but are no longer a short-SHA causal claim. The operator can inspect the
commissioning script under `C:\Antiphon\releases\task-3ec2f72f` and record:

```powershell
$checkout = 'C:\Antiphon\releases\checkout-3ec2f72f'
Get-Item -Force -LiteralPath "$checkout\.git" | Select-Object FullName, Attributes
& git -C $checkout rev-parse --show-toplevel --git-dir --is-shallow-repository
& git -C $checkout rev-parse --verify 'HEAD^{commit}'
$LASTEXITCODE
& git -C $checkout symbolic-ref -q HEAD
$LASTEXITCODE # 1 means detached HEAD
[IO.Path]::GetTempPath() # parent commissioning environment; actual test root is in diagnostics
Get-ChildItem -Force -LiteralPath 'C:\Antiphon\releases\task-3ec2f72f' -Filter '*.ps1'
```

This task performed no desktop access, native test, live qualification mutation,
tag, publish, rebase or force push. The next merge-to-rc step requires ordinary
review of the fixture repair and all required gates. Group 104 is not marked
fixed. The brief's explicit prohibition on `-Update` also remains in force; a
definition-audit refusal must be resolved by the caller's separately commissioned
review/update work, not waived here.

## Handoff

Blocked for qualification clearance: group 104 needs native diagnostic evidence,
and the new build definitions have an explicitly unapproved graph. The encoding
fixture repair is concrete and green on Linux in both required checkout shapes.
Review the pushed slices; authorize the definition audit separately before any
merge to rc, then run the owed native rows when the qualification lock is released.
Do not call this a common-cause repair or a cleared native qualification.
