# Groups 104/105 Substring / source-identity investigation

Task: `21443bfd`. Date: 2026-10-30. Host: server2 Linux (Debian 12).
Assigned branch: `feat/card-task-21443bfd`.
Tested code: `335b4fbb3b1eac700cb8d3c64ea032d9579c6b5f`.
Reported native candidate: `cde8def41`, qualification `d47848cc`.

## Outcome and causal limit

Investigation incomplete: the supplied PowerShell exception has no stack or script
location, and the proposed short Git SHA explanation is not supported by the
listed tests' execution paths. No product fix or cause-specific failing regression
test is claimed.
The reported PowerShell failure has not been reproduced. One C# scheduler
assertion failed once on the assigned base; its isolation/control outcomes and
the diagnostic-only change are recorded below. The caller must provide an
original group-104 or group-105 TRX failure entry (message **and stack**) and its corresponding
chunk stdout/stderr before a causal repair can be selected.

The brief says seven tests but enumerates **eight**: one repeat-host method, six
census methods and one scheduler method. All eight were included in the matrix.
The three named test source files are byte-identical between the native candidate
and the assigned base (`git diff cde8def41 335b4fbb3b1eac700cb8d3c64ea032d9579c6b5f
-- <the three files>` is empty).
The shared `CheckpointTestScope.cs` is also unchanged between those commits.
This does not assert that every production dependency is unchanged.

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

A PowerShell `MethodInvocationException` could originate in an outer qualification
or reporting call. That is an **unconfirmed hypothesis**, not a diagnosed reporting
bug. An ordinary C# exception does not become proof of a PowerShell call in these
bodies merely because the qualification driver is PowerShell.

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
regression witness. The requested red-first witness and fix remain **unfulfilled**.

## Verification

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


## Refinement: group 105 added to the investigation

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
on this path. The Windows `.cmd` shim branch remains a native obligation.

The same exception text reported by the caller is a reason to investigate the
cases together; it does not establish a shared call site. No Substring exception
was reproduced by the added selection, so a cause-specific red witness and repair
remain unfulfilled. An original native message **and stack**, especially for this
PowerShell-backed case, are still required to establish the cause.

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

## Native row owed and operator evidence

| Host / selection | Status |
|---|---|
| Windows: all methods in `CheckpointRepeatHostTests`, `PlanCoverageCensusTests`, `RunSchedulerTests`, plus exact method `CheckpointGitEncodingScriptTests.C1608_invalid_utf8_git_output_is_refused`, one pass after qualification releases its native lock | **Owed, not run.** Linux results do not discharge this row. |

No operation was performed against `d47848cc`, `C:\Antiphon`, the desktop checkout,
any native lock, or any native test host. No tag, publish, rebase, or force push was
performed. Existing authorization covered the Linux investigation; no new approval
was required.

Ask the orchestrator to inspect the commissioning script under
`C:\Antiphon\releases\task-3ec2f72f` and record the checkout creation command and
its original exit/output. These read-only commands distinguish actual checkout
state; do not infer it from `.git` presence:

```powershell
$checkout = 'C:\Antiphon\releases\checkout-3ec2f72f'
Get-Item -Force -LiteralPath "$checkout\.git" | Select-Object FullName, Attributes
& git -C $checkout rev-parse --show-toplevel --git-dir --is-shallow-repository
& git -C $checkout rev-parse HEAD
$LASTEXITCODE
& git -C $checkout rev-parse --verify 'HEAD^{commit}'
$LASTEXITCODE
& git -C $checkout symbolic-ref -q HEAD
$LASTEXITCODE # 1 is expected for a detached HEAD
foreach ($name in @('GIT_DIR','GIT_WORK_TREE','GIT_COMMON_DIR','GIT_INDEX_FILE','GIT_OBJECT_DIRECTORY')) {
    [pscustomobject]@{ Name = $name; Value = [Environment]::GetEnvironmentVariable($name) }
}
Get-ChildItem -Force -LiteralPath 'C:\Antiphon\releases\task-3ec2f72f' -Filter '*.ps1'
```

Also supply one original `UnitTestResult` with its referenced `TestDefinition`,
complete `ErrorInfo/Message` and `ErrorInfo/StackTrace`, plus the corresponding raw
chunk log. Identify whether the reported PowerShell error is a TRX test failure or
a qualification-processing exception. A standalone `.Substring()` message cannot
identify a script, the string being sliced, or the originating test.

After native lock release, build once under `scripts/build-slot.ps1` into
`bin-g104-native/`, then run each of the three exact class filters separately:
`/*/*/CheckpointRepeatHostTests/*`, `/*/*/PlanCoverageCensusTests/*`, and
`/*/*/RunSchedulerTests/*`. Add the separate exact filter
`/*/*/CheckpointGitEncodingScriptTests/C1608_invalid_utf8_git_output_is_refused`
for the group-105 obligation. Use `dotnet run --project tests/Antiphon.Tests --no-build`
with the same output properties and fresh TRX directories, all through the gate.
Record actual executed/passed/failed/skipped counts; stop for investigation on red.
Do not use the zero-selection compound filter from this investigation.

## Handoff

The caller must provide the raw native failure entry and chunk log. Continue Debug
at that exact call site, preserve the first reproducible assertion failure, then
commit/push a minimal repair and verify the requested matrix. No merge-to-rc repair
is recommended from this report: it contains an investigation record, not a
validated fix. The only test change adds failure diagnostics. The owed native row
stays attached to the qualification handoff.
