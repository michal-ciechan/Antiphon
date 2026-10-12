# CARD-1171: resolve installed PowerShell before App Execution Aliases

Code owner/landing task: 1527665f. Base: 118bba94f1dae3499b84260b26d062cd75e1a6c4.

An App Execution Alias at %LOCALAPPDATA%\Microsoft\WindowsApps\pwsh.exe is a
zero-byte reparse point. It can precede installed PowerShell on PATH but cannot run
when its Store package is absent or restricted. ResolvePowerShell currently accepts
the first existing file; WindowsScriptHarnessProcess then correctly refuses it.
Current source and its two resolver history commits contain no fix.

Keep the Windows owner refusal unchanged. ResolvePowerShell accepts the first
regular, nonempty executable candidate on PATH, skipping WindowsApps components
(case insensitive), reparse points and empty files. Normalize relative entries
against the captured working directory, trim surrounding quotes/whitespace and
trailing separators, and skip nonexistent or malformed candidates. On Windows,
fallback order is ProgramFiles\PowerShell\7\pwsh.exe, then
ProgramFiles(x86)\PowerShell\7\pwsh.exe. Never fall back to PowerShell 5.1:
the harness scripts require pwsh/PowerShell 7. No candidate gives FileNotFoundException
with the named diagnostic ScriptHarnessPowerShellNotFound. Linux retains PATH
pwsh discovery, including ordinary symlinks.

Files: tests/Antiphon.Tests/Scripts/ScriptHarnessProcess.cs and
tests/Antiphon.Tests/Scripts/ScriptHarnessPowerShellResolverTests.cs. No production
configuration, environment PATH, installed aliases, scripts or existing assertions change.

S1: this design commit. S2: mechanically inject PATH, candidate metadata, cwd and
fallback paths around the unchanged first-existing selection; add one table-driven
test and record baseline red before the selection fix. S3: implement selection and
qualify clean committed source. The table test collects every row's independent
outcome, so baseline red cannot conceal later rows. Ordinary positive regression
rows can pass baseline; the test as a whole must fail the guarded alias defect.

## Verification design

V-1: ScriptHarnessPowerShellResolverTests.Path_table_selects_real_shell_or_named_refusal
checks alias first/real later, alias only, real only, first real precedence, relative
entries, trailing separators, quoted paths, missing directories, WindowsApps casing,
WindowsApps-like regular directory names, empty regular files outside WindowsApps,
zero-byte reparses outside WindowsApps, nonempty reparses, malformed entries,
standard fallback order, fallback after aliases and total refusal. It uses only
supplied PATH plus a fake metadata view; Linux symlink behavior is also covered.
R-1: unchanged full ScriptHarnessProcessContractTests (26 results) protects owner
cleanup/deadline semantics. R-2: full RunCheckpointScriptTests (24 results) and
ScriptHarnessCancellationTests (5 results) exercise actual Windows default resolver
callers, including owned process joins. R-3: whole Unit lane is required by the
standing Code Final contract despite the brief's narrow-lane request; estimate
5 minutes with no full-assembly selection. V-2: manual Windows qualification:
confirm Windows host, run the resolver class and the named real ScriptHarness
caller classes via checkpoint tool without changing PATH; no restart required.

### Pending SourceLanding Mutation controls

All PCs detect through /*/*/ScriptHarnessPowerShellResolverTests/Path_table_selects_real_shell_or_named_refusal.
Mutation owns deliberate mutants and method-scoped red/restore/green, not Code.

| PC | Cases -> mutation -> expected red |
|---|---|
| PC-1 | alias-first, alias-only, casing -> admit WindowsApps/reparse candidates -> wrong path/refusal |
| PC-2 | real-only, first-real, relative -> change PATH order or ignore cwd -> wrong resolved path |
| PC-3 | quoted/trailing paths -> remove normalization -> wrong path/refusal |
| PC-4 | missing/malformed -> accept absent files or stop at invalid entries -> wrong path/refusal |
| PC-5 | zero regular/zero reparse/nonempty reparse outside WindowsApps -> remove length/reparse guards -> wrong path/refusal |
| PC-6 | WindowsApps-like name -> use substring rather than component match -> wrong refusal |
| PC-7 | fallback/order/alias fallback -> remove or reverse fallback -> wrong path/refusal |
| PC-8 | no usable shell -> guess pwsh or remove diagnostic name -> named refusal assertion |
| PC-9 | Linux symlink -> apply Windows reparse guard to Linux -> wrong refusal |

### Cost

Authoring 15 minutes; baseline and final isolated graph builds plus ordinary rows
approximately 27 minutes. Tool bootstrap is the only unlisted build, needed to
invoke checkpoint tool from an isolated output while shared daemon bins are held.
No full-assembly run, no loaded repetitions, no known-red exclusions predeclared.

### Checkpoints

Every build uses UseAppHost=false. The run verb does not forward --msbuild-property
(the row verb does); import this exact table to ignored .antiphon/c1171.yaml, set
only build.properties.UseAppHost to false, then run that manifest with --rows and
--expected-source-sha. This is the same closed selection, with explicit properties
recorded in build provenance. CP-1 is expected diagnostic
red against unchanged master selection plus mechanical seam; it is not passed
coverage. Final Review runs CP-2..CP-6 at its reviewed SHA. Every reused row has the
same After group and isolated build provenance. No zero-test row counts as proof.

Baseline CP-1 run 20261013-005253-ea8d at
dcc031de4d2e85a70a3780d7438cc8c131f3acb5 executed 1, passed 0, failed 1:
21 internal rows were exercised and 13 failed. In particular alias-first expected
c1171-working\installed\pwsh.exe but returned
c1171-working\Microsoft\WindowsApps\pwsh.exe. The production selection loop
was mechanically extracted unchanged from master (base equals fetched master).
First attempt 20261013-005012-78ab did not execute tests because the new diagnostic
assertion used the wrong Shouldly overload; corrected without weakening it.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | S2 | `tests/Antiphon.Tests -> bin-c1171-baseline/` | resolver-baseline | `/*/*/ScriptHarnessPowerShellResolverTests/*` | V-1 baseline | 1 executed, expected alias-first assertion red | 1 | 5 | true |
| CP-2 | S3 | `tests/Antiphon.Tests -> bin-c1171-final/` | resolver-final | `/*/*/ScriptHarnessPowerShellResolverTests/*` | V-1, V-2 | 1 executed, all internal table rows green | 1 | 5 | true |
| CP-3 | S3 | CP-2 | unit-final | `/*/*/*/*[Category=Unit]` | R-3 | whole Unit lane, 0 failed | 1 | 5 | true |
| CP-4 | S3 | CP-2 | checkpoint-callers | `/*/*/RunCheckpointScriptTests/*` | R-2, V-2 | all 24 existing results, 0 failed/skipped | 24 | 5 | true |
| CP-5 | S3 | CP-2 | cancellation-callers | `/*/*/ScriptHarnessCancellationTests/*` | R-2, V-2 | all 5 existing results, 0 failed/skipped | 5 | 5 | true |
| CP-6 | S3 | CP-2 | process-contract | `/*/*/ScriptHarnessProcessContractTests/*` | R-1 | all 26 existing results, 0 failed/skipped | 26 | 2 | true |
