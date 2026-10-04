# CARD-1035: bind checkpoint children to the certified repository

Plan and folded TestDesign, 2026-10-04. Complexity: **easy**. Inspected source:
`9b78712f6671d7ccb98bcf4a5ebea8f080d7cecd`. Code budget: **40 minutes**,
excluding observed build-slot queue time. Next stage: **Code**.

The source receipt and both dotnet children must describe the same repository.
Set `ProcessStartInfo.WorkingDirectory` to the already resolved Git root in the
shared `Invoke-Dotnet` function. Add an executable regression that deliberately
separates the launching process's native cwd from its PowerShell location.

## Ground truth

| Card assumption | What the inspected code does | Consequence |
|---|---|---|
| Source identity follows PowerShell location. | `scripts/lib/checkpoint-source.ps1:Get-CheckpointSource` defaults to `(Get-Location).Path`, finds the Git root, and passes an explicit working directory to each Git child. | A `Push-Location` into repository B certifies B. |
| Dotnet can use a different repository. | `scripts/run-checkpoint.ps1:Invoke-Dotnet` sets executable, literal arguments and redirected streams, but never `WorkingDirectory`. | Its build and run children inherit native cwd A, even while the receipt names B. |
| A clean receipt should prove the build's source. | The script computes `$root`, the build-stamp path and expected binding from B, then marks a successful child build verified. | Existing SHA and cleanliness checks do not detect this directory mismatch. |
| One launch fix covers both phases. | Build and run, including `-NoBuild`, all use `Invoke-Dotnet`; the shim branch shares the same `ProcessStartInfo`. | One common assignment fixes all these launches. |
| Existing tests cover source qualification. | `RunCheckpointSourceScriptTests` has five tests for diagnostic/strict/drift/reuse/terminal/validation paths. Its fixture always starts PowerShell directly in the selected repository. `test-run-checkpoint.ps1` also explicitly aligns its wrapper cwd. | Neither fixture creates the mismatch in this card. |
| The checkpoint tool also needs fixing. | `tools/Antiphon.Checkpoints/Execution/RowRunner.cs` supplies `request.WorkingDirectory` to build and run `DriverRequest`s. | Keep this change in the PowerShell entry point. |

A read-only process diagnostic during planning confirmed the mechanism on Linux:
with PowerShell location `/tmp`, the unconfigured child retained its parent's
native cwd; assigning the selected directory made the child report `/tmp`.
Both diagnostic children exited 0. This was not a checkpoint test or a build.

## Decisions

- **D-1: Use the existing absolute `$root` in the common child launcher.** Add
  `$psi.WorkingDirectory = $root` before either executable branch starts its
  process, with a short comment connecting it to the source/build binding.
  Reject changing only the build call, only the run call, or only the real-dotnet
  branch: those leave an independently reachable launch with the old cwd.
- **D-2: Preserve the script's repository-root project contract.** The existing
  build-stamp binding already resolves relative `-Project` against `$root`.
  Use that same root for execution. Reject `$PSScriptRoot` (scripts directory),
  the script checkout's parent (may be another checkout), and inherited native
  cwd. Reject process-wide `Environment.CurrentDirectory` changes and a further
  `Push-Location`: the former is unnecessary shared state; the latter does not
  fix native inheritance. Receipt format, source capture and repeat semantics
  need no redesign for this repair.
- **D-3: Observe a real child process through the existing dotnet shim seam.**
  Extend the private source-test fixture and add one non-parameterized test.
  The shim must independently report its native cwd and read a tracked marker
  there. Reject source-text assertions, echoing an expected path from an
  environment variable, and another nested SDK build: these either miss the
  failure or add cost without exercising a different launch boundary.
- **D-4: Use the portable script Integration lane through runtime placement.**
  `GET /api/runner-defaults` and `GET /api/session-runners` were read during
  planning; revision 2 resolves to an available, dispatch-eligible Linux
  default. This work needs no OS-specific API. Dispatch without `-Runner` or
  `-Platform`; re-read those endpoints at dispatch. Checkpoints below name the
  lane, not a fleet host or path. Do not infer Windows execution from Linux.
- **D-5: One changed invariant, one positive control.** Ordinary Code and Review
  run only the two rows below; Mutation later removes the single shared cwd
  assignment and runs the exact new method red, then restored green. No whole
  Unit, namespace or assembly run is authorized by this plan.

These are resolved implementation decisions, not unanswered operator choices.

## Implementation slices

### S1: child-directory binding and executable regression

Change only these implementation/owner paths:

| File | Change | Verification |
|---|---|---|
| `scripts/run-checkpoint.ps1` | Set the common `ProcessStartInfo.WorkingDirectory` to `$root`; keep script ASCII-only. | V-1 / R-1, PC-1; existing source and forwarding regressions. |
| `tests/Antiphon.Tests/Scripts/RunCheckpointSourceScriptTests.cs` | Add `C1035_ChildCwdMatchesCertifiedRoot`; extend its private fixture with distinct seed content, a path containing spaces, a wrapper-location option, and separate native-child observations. Keep existing defaults and call counters intact. | CP-1: six TUnit executions after this addition. |
| `docs/testing-and-build.md` | Add one source-qualification paragraph: the script's dotnet children execute in its certified Git root, including after `Push-Location`; relative project paths use that root. | Review against D-1/D-2. |

The fixture must retain `Integration` and `ParallelLimiter<ProcessSpawnLimit>`.
Pass paths through `ArgumentList`, a parameterized wrapper file or a serialized
argument array; do not interpolate unescaped paths into PowerShell code. Join
each owned child, drain output, and dispose fixture roots. Any new launch helper
must kill and join only its own child tree on timeout before cleanup, preserving
the test failure. Do not change production timeout or process-lifecycle policy.

Commit and push S1 before verification. No source edits during a running row.
If a fix is needed, commit/push it and rerun the affected row with fresh results.

## Verification design

### Inspection

Bodies read before selecting coverage:

- `run-checkpoint.ps1`: source preflight, `$root`, stamp binding, shared launcher,
  build/run calls and boundary checks -> V-1, R-1, R-2.
- `lib/checkpoint-source.ps1`: Git cwd, snapshot and receipt checks -> R-2.
- `RunCheckpointSourceScriptTests.cs`: all five tests, fixture constructor,
  generated shim, `RunAsync`, receipt validation and disposal -> V-1, R-1, R-2.
- `RunCheckpointScriptTests.cs` and `test-run-checkpoint.ps1`: wrapper launch,
  shim argv, no-build, forwarding and ASCII cases -> R-3.
- `RowRunner.cs`: explicit cwd on build/run requests -> excluded from edits.

Missing setup is precisely the divergent wrapper/native cwd and independent
child observations; current fixtures supply neither. Reuse their isolated Git
repositories, fixture TRX and offline slot shim. No database, browser, live
broker or deployment is required for the new test.

### Delivery inventory

No asynchronous user/session delivery path changes. The boundary here is a
locally owned process launch, observed directly at child startup and joined at
exit. The dotnet shim substitutes for SDK work and emits the existing canned
TRX; it proves cwd/relative-file selection through the real production launcher,
not that a second real project compiled. CP-1 performs the actual build of the
test assembly that exercises that launcher.

### Proves it works now

**V-1:** `RunCheckpointSourceScriptTests.C1035_ChildCwdMatchesCertifiedRoot`
is one non-parameterized Integration test with this sequence:

1. Create disposable, clean Git repositories A and B with different committed
   marker contents and therefore different HEADs. Put B in a path with spaces.
   Both contain the same relative sample project path. Keep scripts, logs and
   results outside their tracked source; output/stamp paths remain ignored.
2. Start a PowerShell wrapper with `ProcessStartInfo.WorkingDirectory = A`.
   In that same PowerShell process, `Push-Location -LiteralPath B` and call the
   real `run-checkpoint.ps1` by its absolute path, with relative `-Project sample`,
   `-ExpectedSourceSha B.Head`, and the absolute shim/results paths. Do not start
   a new wrapper in B, which would hide the defect. Record/assert the setup:
   native parent directory is A and PowerShell location is B immediately before
   calling the script. Carry the script exit code back to the test.
3. Each shim invocation records phase and `[Environment]::CurrentDirectory`
   to a separate JSONL observation file. It reads the tracked marker using
   `System.IO.File.ReadAllText` and a path based on that observed native cwd.
   It must not use `C835_REPO` or the expected B path to obtain this evidence.
4. Assert exactly two initial observations, ordered `build`, `run`, with native
   cwd B and B's marker on both. Use the stable decisive assertion label
   `child-cwd-matches-certified-root`. The source receipt must have start/end
   commit B.Head, `dirty=0`, `sourceState=clean`, `buildSource=verified`, exit 0
   and three passed fixture results. Assert the build stamp binds repository B
   and B's project/output paths; no stamp is created under A. Validate the
   receipt using the existing fixture validator.
5. Repeat from native A / PowerShell B with `-NoBuild` and a fresh results
   directory. Assert just one additional `run` observation, cwd/marker B,
   `build=reused`, and the same clean/verified source binding. No extra build
   observation is allowed. Both runs retain the existing literal filter argv.

The test counts as **one** TUnit execution. The copied fixture's three results
and the three child observations are internal assertions, not execution counts
for this plan's checkpoint rows.

### Guards the regression

- **R-1:** The V-1 method must fail at `child-cwd-matches-certified-root` when
  the child's working directory is inherited from A. A successful receipt by
  itself is insufficient. Build, run and reuse are all checked in that method.
- **R-2:** Run the five existing methods in `RunCheckpointSourceScriptTests`:
  `C835_DiagnosticReceipts`, `C835_StrictAdmission`, `C835_DriftAndReuse`,
  `C835_TerminalEvidence`, `C835_ReceiptValidation`. They retain clean/dirty
  diagnostics, strict refusal before launches, drift detection, matching and
  mismatched reuse, failed-build invalidation, terminal failures and receipt
  validation. These also cover the ordinary aligned-cwd case.
- **R-3:** Run `RunCheckpointScriptTests.C585_NoBuild`,
  `RunCheckpointScriptTests.C585_MsBuildForwarding` and
  `RunCheckpointScriptTests.C585_AsciiOnly`. Their existing assertions retain
  no-build behavior, property forwarding to both phases and script encoding.

### Guard inventory

| Guard | Changed invariant | Control |
|---|---|---|
| G-1 | D-1: every child from the shared launcher executes at the repository root used by its source/build receipt. | PC-1 |

The three invocation paths share one unconditional guard. Existing admission,
drift and receipt-validator guards are unchanged and receive ordinary R-2
coverage, not additional controls in this card. Guards=1, mapped=1, missing=0,
duplicate PC maps=0.

### Positive controls

| PC | Compiling production mutation | Exact detecting method and expected failure |
|---|---|---|
| PC-1 | Remove only the new `$psi.WorkingDirectory = $root` assignment in `Invoke-Dotnet`. | `RunCheckpointSourceScriptTests.C1035_ChildCwdMatchesCertifiedRoot`: `child-cwd-matches-certified-root` fails with observed A versus expected B. The child must have launched and produced observations; build/fixture failure or zero tests is not red. |

Post-land Mutation uses exactly
`/*/Antiphon.Tests.Scripts/RunCheckpointSourceScriptTests/C1035_ChildCwdMatchesCertifiedRoot`
for each baseline/red/restored-green phase: one executed result, zero skips.
Restore the exact script bytes before green. Since the mutated artifact is a
script loaded at runtime, the same source-bound test assembly can execute the
cycle; preserve the script hashes and actual source state instead of claiming
a dirty mutant is clean. The SourceLanding external-evidence and restoration
contract applies. Code runs ordinary V/R; Review judges it before land.

Mutation first builds `tests/Antiphon.Tests` under `scripts/build-slot.ps1` into
`bin-c1035-pc/`, with `UseAppHost=false`, at the clean landed source. Each phase
then uses `scripts/build-slot.ps1 -Label c1035-pc -- dotnet run --project
tests/Antiphon.Tests --no-build --property:OutputPath=bin-c1035-pc/
--property:UseAppHost=false -- --treenode-filter
'/*/Antiphon.Tests.Scripts/RunCheckpointSourceScriptTests/C1035_ChildCwdMatchesCertifiedRoot'
--report-trx --report-trx-filename run.trx --results-directory <fresh-external-phase-directory>`.
Keep each phase's TRX and assertion outcome; the deliberately edited script
requires no test-assembly rebuild. Remove the owned alternate outputs before
the SourceLanding restoration receipt.

### Out of scope

Changes to receipt schemas, Git fingerprinting, build-slot scheduling, checkpoint
tool process custody, SDK behavior, arbitrary external projects, symlink root
identity, and general subdirectory-relative/repeat path redesign are excluded.
The script already binds relative projects at the repository root. This card
does not recertify CARD-1031's disqualified historical baseline or rewrite its
evidence. No second OS qualification or whole-Unit run is needed for this
portable child-launch assignment.

### Execution procedure

Use the checkpoint tool for one committed S1 group. Supporting bootstrap is
explicitly budgeted outside the two test rows to avoid an unleased implicit
tool build. From the actual OS workdir at the candidate checkout root:

```powershell
pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1035-tool -- dotnet build tools/Antiphon.Checkpoints --property:OutputPath=bin-c1035-tool/ --nologo
dotnet tools/Antiphon.Checkpoints/bin-c1035-tool/net9.0/Antiphon.Checkpoints.dll coverage --plan docs/superpowers/plans/2026-10-04-card-1035-checkpoint-child-cwd-plan.md
$sha = (git rev-parse HEAD).Trim()
dotnet tools/Antiphon.Checkpoints/bin-c1035-tool/net9.0/Antiphon.Checkpoints.dll run --plan docs/superpowers/plans/2026-10-04-card-1035-checkpoint-child-cwd-plan.md --after S1 --expected-source-sha $sha --max-wait 50s
```

This explicit built-DLL invocation is the no-implicit-build equivalent of the
owner's `dotnet run --project tools/Antiphon.Checkpoints -- run --plan ...`.
The tool takes its own build/row slots; do not wrap its run in an outer slot.
For exit 75, keep issuing `wait --run <returned-run-id> --max-wait 50s` on the
same DLL until terminal; own and await the run before ending the task. Lease
timeout 4 is not run, never permission to bypass the gate.

Keep the complete static-coverage output with dispositions (the existing
PowerShell harness assertions may be statically unmapped), the unedited
CHECKPOINT lines, actual SHA/counts and fresh structured receipts in the task
report. Validate the tool `report.json` with
`scripts/validate-checkpoint-receipt.ps1 -Evidence <report.json> -ExpectedSourceSha <sha> -Rows CP-1,CP-2`.
Generated output stays ignored. Run `scripts/check-evidence-diff.ps1` over the
full Code/Review task range. Remove task-owned `bin-c1035/` and
`bin-c1035-tool/` directories across projects after all owned processes exit.

### Cost

Estimated ordinary V/R floor: **9 minutes** (CP-1 8 + CP-2 1), with one isolated
test-project build reused by CP-2. Supporting leased tool bootstrap/coverage:
**2 minutes**. Authoring: **25 minutes**; report and cleanup: **4 minutes**.
Total Code estimate: **40 minutes**, plus observed slot waits, within the
operator's 30-60 minute budget. Review uses the same ordinary selection.

Estimated Mutation floor: **3 minutes** for the one method's baseline, red and
restored-green runs; allow **5 minutes** for assembly/setup and **2 minutes**
for restoration/evidence, total **10 minutes**. Combined Code + Mutation
allowance: **50 minutes**, excluding separate Review and queue time. These are
estimates, not measured test results. CP-2 reuse saves one test-project rebuild;
the one required PC replaces no other changed guard. No numerical runtime
savings are claimed without measurement.

### Checkpoints

Closed ordinary scope: **portable script Integration lane**, executed on the
runtime-selected eligible host. All rows use the same committed S1 source and
run serially. Six source-script methods plus three existing compatibility
methods give **nine** TUnit executions, zero failed/skipped. No checkpoint
namespace census update is needed: the new test is in `Antiphon.Tests.Scripts`.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1035/` | script-integration-source | `/*/Antiphon.Tests.Scripts/RunCheckpointSourceScriptTests*/*` | V-1, R-1, R-2 | exactly 6 executed/passed, 0 failed/skipped | 6 | 8 | true |
| CP-2 | S1 | CP-1 | script-integration-compatibility | `/*/Antiphon.Tests.Scripts/RunCheckpointScriptTests*/(C585_NoBuild*)\|(C585_MsBuildForwarding*)\|(C585_AsciiOnly*)` | R-3 | exactly 3 executed/passed, 0 failed/skipped | 3 | 1 | true |

## Publication and handoff

This Plan task commits and pushes only its assigned branch. The caller lands
the plan with `delegate.ps1 -Land 3e370582-6552-4d92-8fa0-3fa1882664bd
-ExpectedSourceSha <pushed-plan-sha>` immediately after settlement; ordinary land
admission requires the task to have succeeded. Do not push directly to master
or rewrite a pushed task commit. After plan publication, dispatch Code with this
artifact's `### Checkpoints` table pinned to the full plan commit SHA. No separate
TestDesign dispatch or operator decision is needed.
