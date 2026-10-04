# CARD-1048: isolate Windows PowerShell's child module path

Date: 2026-10-04. Stage: Plan with TestDesign folded in; next: Code.
Inspected source: `62150034f3fadbabad0d7f31323598379e3b206b`.
Card: `934734a7-7d9c-4091-8432-4f295a7de4b7` on Antiphon, read through
`scripts/card.ps1 get CARD-1048 -Board Antiphon`.

Make a wrapped Windows PowerShell 5.1 process resolve its own modules so that
`Get-FileHash` works through the mandatory build-slot wrapper. The production
change is one guarded child-environment adjustment in
`scripts/build-slot.ps1`. Keep PowerShell 7 children, argument forwarding,
working directory, process cleanup, exit propagation and the lease protocol as
they are. Complexity is easy; verification is sufficiently bounded to design here.

This Plan performed source inspection and read-only routing queries, with no
builds, runtime tests or Windows reproduction. The originating Windows Review
is evidence reported by the card, not a run performed by this Plan.

## Ground truth

| Card assumption | Current code or evidence | Consequence |
|---|---|---|
| Wrapping 5.1 breaks Get-FileHash. | CARD-0496 Review `133888ed`, evidence `508152376eba40c5b789e55932c8573a`, reports the deployment harness directly under 5.1 at 167 passed / 0 failed, versus wrapped at 68 passed / 23 failed with Get-FileHash failures. | Preserve that provenance; qualify the repair with a small real 5.1 child, not that whole deployment suite. |
| The wrapper leaks PowerShell 7's module search path. | `Start-AntiphonWrappedCommand` creates `ProcessStartInfo`, sets FileName, UseShellExecute, WorkingDirectory and ArgumentList, then starts it without editing Environment. The Application branch passes the resolved executable Source. | The process inherits the wrapper's environment and bypasses PowerShell's native-command launch compatibility handling. This is the concrete defect site. |
| Every PowerShell target needs path rewriting. | Direct `.ps1` targets deliberately run the PowerShell 7 binary under the wrapper's PSHOME. The command shim also launches pwsh. | Restrict the adjustment to the resolved Windows powershell.exe executable, not scripts or every PowerShell process. |
| Lease changes may be needed. | Acquisition precedes the launcher; the existing outer finally disposes the child and calls Exit-AntiphonBuildSlot. | No edits to `scripts/lib/build-slot.ps1`, runner endpoints or checkpoint source. |
| Existing tests cover real 5.1 module loading. | `scripts/test-build-slot.ps1` has real-child cases with DisableCommandShim, but no Windows PowerShell module-path case. `BuildSlotScriptTests` selects named harness cases and asserts their PASS inventories. | Extend the existing harness and class; do not introduce another test framework. |
| This Linux checkout can qualify the repair. | Windows PowerShell 5.1 is required. Live routing reads found eligible Windows and Linux runners; the preference resolves to Linux. | CP-1 must run on the Windows lane; a Linux skip is not acceptance. |

Microsoft's [about_PSModulePath](https://learn.microsoft.com/en-us/powershell/module/microsoft.powershell.core/about/about_psmodulepath?view=powershell-7.6#starting-windows-powershell-from-powershell-7)
documents removal of the child variable when native PowerShell compatibility
handling is bypassed: Windows PowerShell reconstructs its default module path
at startup. Its description of PowerShell 7 modules preceding Windows modules
supports the card's diagnosis. Source inspection plus the card's Windows result
confirm the cause; execution of the proposed repair remains pending.

## Decisions

**D-1: remove PSModulePath from only the Windows powershell.exe child.** In
Start-AntiphonWrappedCommand, after creating/configuring the ProcessStartInfo and
before Process.Start, use this shape:

```powershell
if ($IsWindows -and [IO.Path]::GetFileName($FileName) -ieq 'powershell.exe') {
    [void]$psi.Environment.Remove('PSModulePath')
}
```

FileName is already resolved by Get-Command in the Application branch. Matching
the executable leaf case-insensitively handles bare, absolute and mixed-case
invocations without hard-coding an installation directory or adding a version
probe process. Windows PowerShell 5.1 is the supported target; do not broaden
this to powershell_ise.exe, renamed binaries or a cmd.exe descendant.

Reject manually assembling SystemRoot/ProgramFiles/Documents module paths:
Windows PowerShell already owns that startup logic, including redirected user
locations and architecture. Reject copying WinPSModulePath or filtering strings
as unnecessary extra configuration/fallback logic. Clearing is explicitly
authorized by the brief. A caller's process-only custom 5.1 module-path additions
are deliberately not preserved by this choice; no registry/user/machine settings
are written. Work requiring custom paths can set them within its child script.

**D-2: preserve parent and other children.** Modify only psi.Environment. Never
set/remove env:PSModulePath in the wrapper process, even temporarily. Never clear
the entire child environment. Retain every non-PSModulePath variable. Leave
pwsh.exe, pwsh, the PowerShell 7 script branch and command-shim branch untouched.
Reject replacing the launcher with the call operator or Start-Process: that
would unnecessarily revisit the established literal-argv/exit contracts.

**D-3: reuse real-child tests and fake only the slot broker.** Extend the existing
C589 harness with two C1048 cases using DisableCommandShim. Keep its loopback
dead fallback URL and granted slot shim, exact lease-ID/order assertions,
ProcessSpawnLimit and bounded owned-child waiting. A fake Get-FileHash or an
assertion against source text cannot establish module autoloading.

**D-4: Windows verification, no host pin.** GET /api/runner-defaults (revision 2)
and GET /api/session-runners were read on 2026-10-04 around 20:53 UTC. The live
preference was Linux; a Windows runner was available, dispatch-eligible and
accepting work. These are observations, not permanent fleet configuration.
Dispatch Code and ordinary Review with `-Platform Windows`, omit `-Runner`,
and re-read routing at dispatch. No fleet address or checkout location is part
of this plan. Do not change the card's Any default as a substitute for specifying
the required lane. Require real 5.1 and PowerShell 7; missing Windows prerequisites
are a blocker, not a passed skip.

**D-5: bounded verification and stage order.** Code has a 30-60 minute budget and
runs ordinary V/R only. Separate Review reruns the same checkpoint on its exact
source. After Code lands, SourceLanding Mutation runs the three distinct controls
below, one per changed behavior. No full Unit/namespace/suite run, no deployment
suite, no broker refactor, no retry or deadline widening. No unresolved product
choice is needed; these decisions implement the brief, rather than defer it.

## Implementation slices

One implementation slice avoids an unnecessary interim build. Commit and push
S1 before running CP-1. If verification requires a correction, commit/push that
correction and rerun the same row against its new exact SHA.

| Slice | Files | Work and tests |
|---|---|---|
| S1 | `scripts/build-slot.ps1`; `scripts/test-build-slot.ps1`; `tests/Antiphon.Tests/Scripts/BuildSlotScriptTests.cs`; `docs/testing-and-build.md` | Add D-1's guarded removal and short ASCII comment; implement the two C1048 cases and their exact C# PASS inventories; update the harness's platform-dependent default case enumeration/count; document the Windows PowerShell exception to inherited environment in the Build slots section. Verify V-1/V-2/R-1 with CP-1. |

No new production helper or source file is necessary. Generate child/caller
fixtures inside each New-C589Case root. Keep temporary JSON/logs ignored and use
ScriptHarness cleanup. Do not edit generated `docs/cards/` files.

## Verification design

### Inspection

| Bodies read | Boundaries covered or excluded |
|---|---|
| `scripts/build-slot.ps1`: argument parsing, resolution, Start-AntiphonWrappedCommand, try/finally | D-1/D-2 and V-1/V-2; R-1 retains argv, script binding and exit evidence. |
| `scripts/test-build-slot.ps1`: Invoke/Wait-C589Wrapper, seams, C800 real-native cases, C845 caller/payload/lease helpers, final enumeration/count | Reuse DisableCommandShim and CallerPath, real native launch, safe payload parsing, 30-second child deadlines and POST/CMD/DELETE observations. |
| `tests/Antiphon.Tests/Scripts/BuildSlotScriptTests.cs`; `tests/Antiphon.Tests/Scripts/ScriptHarness.cs` | Integration category, assembly-local process limiter, exact named assertion inventory and one execution per method. |
| `scripts/fixtures/c589-slot-shim.ps1`; `scripts/lib/build-slot.ps1` acquisition/launch relationship; assertion and receipt helpers in `scripts/lib/c487-harness.ps1` | Fake only lease HTTP. Existing protocol and broker policy are not changed or newly mutation-qualified. |
| `tools/Antiphon.Checkpoints/Manifest/PlanTableImporter.cs` header schema; testing/build owner | Platform is recorded in checkpoint prose and Group: the table importer has no Platform column. |

### Delivery inventory

No agent/session delivery, persistent queue or recovery path changes. The
observable chain is wrapper holder PID -> granted lease ID -> real child CMD
marker -> child outcome -> DELETE of that same lease ID. The offline broker
establishes ordering and cleanup calls, not production capacity reclamation.
That substitution is sufficient because the broker/protocol is unchanged.

### Proves it works now

**V-1: Windows PowerShell autoload and parent isolation.** Add the single TUnit
method `BuildSlotScriptTests.C1048_WrapperWindowsPowerShellGetFileHash`, invoking
the matching Test-C1048_WrapperWindowsPowerShellGetFileHash harness case.

In a disposable PowerShell 7 caller launched through the existing CallerPath
seam, prepend its real PSHOME/Modules directory to PSModulePath before invoking
the actual wrapper. Record that exact parent value. The fixture must observe
the PS7 directory first; do not rely on the ambient service account's ordering.
Do this inside the fixture process after PowerShell startup, not in the test
host's global environment. Do not manufacture or replace Utility modules.

Create an input file of exactly ASCII bytes `abc` using IO.File.WriteAllBytes.
The Windows PowerShell child must call unqualified Get-FileHash with SHA256
without an explicit Import-Module, PSModulePath edit or compatibility workaround.
Expected digest is the independent fixed value
`BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD`.
Emit one parseable payload containing actual version, edition, digest, child
PSHOME, effective module path and Get-FileHash's module location. Set
ErrorActionPreference to Stop so a failed hash cannot return a successful child
exit. Mark CMD before hashing. Capture a failure as diagnostics and a nonzero
exit; the harness must still emit its named failed assertions when payload is
missing, rather than aborting during JSON/property access.

The caller invokes the wrapper with `&`, captures LASTEXITCODE immediately on
return, records its module path again, then returns that captured code. This
directly observes parent isolation after the wrapper's nested script returns.
Keep the snapshot/result emission independent of newly auto-imported modules.

Run three scenarios inside this one method: PATH-resolved `powershell.exe`, its
resolved absolute application path, and that absolute path with a mixed-case
executable leaf. Each calls `-NoProfile -NonInteractive -File <fixture>` through
build-slot with no command shim. Resolve the native Windows installation rather
than copying/renaming the binary. Use scenario names `bare`, `absolute`, `mixed`.

For each scenario require these six PASS labels, prefixed exactly with
`C1048 WindowsPowerShell <scenario> `:

1. `exits zero`: no timeout, child/wrapper exit 0 and valid payload.
2. `runs 5.1 Desktop`: observed version major/minor 5/1 and PSEdition Desktop.
3. `hash matches`: the actual Get-FileHash digest equals the fixed SHA256 above.
4. `uses Windows modules`: effective path excludes the injected PS7 Modules
   directory and Get-FileHash's module file is under that child's PSHOME/Modules.
5. `preserves parent path`: before and after match ordinally, with the injected
   PS7 directory still present; absent snapshots fail.
6. `releases lease`: one POST, one child CMD, one DELETE of the granted ID, in
   that order, plus granted/released console receipts.

Exactly 18 harness assertion rows; one TUnit result. Windows-only TUnit guard;
on Windows an absent/non-5.1 executable fails qualification instead of skipping.

**V-2: PowerShell 7 still inherits the same environment.** Add
`BuildSlotScriptTests.C1048_WrapperPwshModulePathUnchanged`, invoking the matching
Test-C1048_WrapperPwshModulePathUnchanged harness case. Use another disposable
caller and the actual pwsh.exe under its PSHOME. Add a fixture-owned module root
to the process module path, containing a small module with a unique exported
function returning a known sentinel. No user/machine environment changes.

Run the same probe twice from the same caller environment: first with an
independent raw ProcessStartInfo launch, then through the actual wrapper with
DisableCommandShim. Keep the raw comparator independent of the production
launcher/helper; do not reuse the code under test. Capture separate payloads.
PowerShell startup may normalize paths, so compare these two fresh children's
effective values ordinally, not a parent snapshot to a normalized child value.
Both children must actually load the custom module and return its sentinel;
raw-baseline failure is a fixture failure, never a PC red. Only the wrapped
probe writes CMD to the lease log. Await and dispose both owned processes within
the existing bounds; record the wrapper's exit separately from the baseline.
Capture the effective module path before importing the sentinel module and emit
it even if that import fails, with a separate error field and nonzero exit. This
lets PC-2 fail the path comparison explicitly instead of losing its payload.

Require five labels prefixed exactly `C1048 Pwsh `: `exits zero` (both children
and wrapper), `runs Core` (major >= 7, Core), `module path matches baseline`,
`custom module survives` (sentinel from both children), and `releases lease`
(same matching-ID/order/console assertions as V-1). Exactly five harness rows;
one TUnit result. Qualify on Windows alongside V-1.

Add literal expected labels/counts in BuildSlotScriptTests using ScriptHarness's
C1048 prefix. Make default harness enumeration include C1048 only on Windows;
an explicitly selected Windows case on an unsupported OS must not report green.
Default harness count becomes the current `39 + 16 + 50 + (Linux ? 3 : 0)` plus
23 on Windows. No default full-harness run is commissioned by this plan.

### Guards the regression

**R-1: four existing methods** in BuildSlotScriptTests remain unchanged and are
selected explicitly by CP-1: `C589_WrapperAsciiOnly` (5 assertion rows),
`C800_WrapperLaunchesNativeExecutableLiterally` (3),
`C845_WrapperBindsNamedScriptParameters` (16), and
`C845_WrapperPropagatesScriptExitCodes` (10). They retain ASCII, native literal
argv/cwd, direct-script binding/child identity and exit-code checks.

Together V-1/V-2/R-1 are exactly six TUnit executions and 57 internal harness
assertion rows. The internal rows are not a TUnit execution floor. Review also
checks that the final production diff contains only the conditional environment
removal/comment, and the owner documentation accurately describes it.

### Guard inventory

Three changed invariants, three distinct controls, zero missing mappings and
zero duplicate guard-to-PC mappings. Existing lease/argv/process-lifetime guards
are preserved and have regression coverage; their own mutation suites are not
recommissioned. PC-1 and PC-3 use the same detecting method for different,
independently observable failures.

| Guard | Decision and behavior | Control |
|---|---|---|
| G-1 | D-1: remove PS7 contamination for the real 5.1 child. | PC-1 |
| G-2 | D-2: exempt pwsh children and preserve their custom module search path. | PC-2 |
| G-3 | D-2: keep the wrapper/caller process environment unchanged. | PC-3 |

### Positive controls

| PC | Deliberate production fault | Exact detecting filter | Required red witness |
|---|---|---|---|
| PC-1 | Remove just the new child PSModulePath removal, keeping the launcher executable. | `/*/*/BuildSlotScriptTests/C1048_WrapperWindowsPowerShellGetFileHash` | `FAIL C1048 WindowsPowerShell bare hash matches`, with child module/autoload diagnostics and nonzero wrapper exit; the fixture completed and observed the product failure. |
| PC-2 | Widen the new condition to every Windows executable, retaining child-only removal. | `/*/*/BuildSlotScriptTests/C1048_WrapperPwshModulePathUnchanged` | `FAIL C1048 Pwsh module path matches baseline` and loss of the custom module sentinel; the independent raw baseline succeeded. |
| PC-3 | Keep the correct child removal but also clear the process PSModulePath in that powershell.exe branch with Environment.SetEnvironmentVariable. | `/*/*/BuildSlotScriptTests/C1048_WrapperWindowsPowerShellGetFileHash` | `FAIL C1048 WindowsPowerShell bare preserves parent path`; the real 5.1 hash remains successful. |

These are designed controls, not executed evidence. Run separately after land in
the commissioned Windows SourceLanding snapshot. For each: one method baseline
green, apply only its fault, same method red, restore exact bytes, same method
green. Each phase must execute exactly one test, with no skip; red is driver
exit 1 and the named FAIL witness, not build failure, missing TRX, absent shell,
fixture failure, timeout or zero selection. ScriptHarness's outer exit assertion
may be the TRX failure, but its captured diagnostic must include that witness.

All faults touch the same production file, so do not batch them. Use the
unchanged self-leasing checkpoint script copied with its dependencies to the
assigned external evidence root, per the testing owner. Keep per-phase build,
run, TRX and restoration receipts there; no source/report commits from the
SourceLanding snapshot. Use separate `bin-c1048-pcN-baseline/`,
`bin-c1048-pcN-red/`, `bin-c1048-pcN-green/` outputs, matching exact method Expect
and MinExecuted 1, and await every phase before editing/restoring source.

### Out of scope

No module-path policy for indirect cmd/bat descendants, ISE, renamed shells or
customized process-only 5.1 module additions; no registry edits, provider/daemon
deployment, live broker experiment or checkpoint-tool repair. No Linux
qualification is needed for a Windows-guarded change; Linux green/skip results
cannot substitute for the required Windows run. Broker release ordering is
observed through the existing seam, not claimed as fresh live-broker coverage.

### Execution and source qualification

**Checkpoint platform: Windows; lane: windows-module-path; applies to CP-1.**
Record OS, wrapper PowerShell 7 version and child 5.1 version in the report.
There is deliberately no unsupported Platform column in the importer table.

Use the checkpoint tool's `run --plan
docs/superpowers/plans/2026-10-04-card-1048-build-slot-module-path-plan.md
--after S1 --expected-source-sha <full-source-sha> --serial`. Keep ownership of
the foreground command; if it returns 75, continue `wait` until completion.
Do not end the task with a running executor. CP-1 is the closed ordinary list.

If a matching checkpoint tool binary is unavailable, declare one supporting
bootstrap: `pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1048-tool --
dotnet build tools/Antiphon.Checkpoints --property:OutputPath=bin-c1048-tool/
--nologo`. Await completion and release that slot. Invoke tool verbs using
`dotnet run --project tools/Antiphon.Checkpoints --no-build
--property:OutputPath=bin-c1048-tool/ -- ...`. Do not hold an outer build-slot
lease across the tool's self-leasing row execution. This bootstrap is the one
declared supporting build, not an extra ordinary test row.

Run the read-only plan coverage verb at the final source, explain any syntax-lint
limitations for PowerShell harness bodies, and preserve its complete output.
Run `scripts/check-evidence-diff.ps1 -BaseRef <task-base>
-HeadRef <pushed-source-sha>` over the full Code/Review history. These static
checks are not extra test runs. Retain unedited CHECKPOINT lines and validate
the receipt against the exact committed SHA with validate-checkpoint-receipt.ps1.
Require dirty=0, sourceState=clean, buildSource=verified, matching source identity,
the six exact executed methods and zero failures/skips. Report reruns and reasons.
Exit 4 means not run/blocked; do not bypass the gate. Leave generated output
ignored and remove all task-owned alternate bin directories before completion.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1048-scripts/` | windows-module-path | `/*/Antiphon.Tests.Scripts/BuildSlotScriptTests/(C1048_WrapperWindowsPowerShellGetFileHash*)\|(C1048_WrapperPwshModulePathUnchanged*)\|(C589_WrapperAsciiOnly*)\|(C800_WrapperLaunchesNativeExecutableLiterally*)\|(C845_WrapperBindsNamedScriptParameters*)\|(C845_WrapperPropagatesScriptExitCodes*)` | V-1, V-2, R-1 | exactly the 6 named methods, 6 passed, 0 failed/skipped; 57 internal harness rows | 6 | 12 | true |

### Cost

Estimates, not measurements: authoring 20-35 minutes; ordinary Code floor
12 minutes (one build, six tests); optional tool bootstrap 5 minutes. Total
Code 32-52 minutes before external slot/restore delays, fitting the 30-60 minute
brief. Review repeats the 12-minute row plus inspection and its own bootstrap
when required. Report external delays rather than reducing scope.

Post-land Mutation is separate: three method-scoped baseline/red/restore-green
cycles, nine phase builds and nine TUnit executions, estimated 45-63 minutes
including build/setup. Full ordinary Code plus Mutation verification floor is
57-75 minutes, excluding authoring, Review and optional tool bootstrap. No
build-reuse saving is claimed. The six-method selection avoids a full Unit run;
the available owner estimate for the entire Antiphon.Tests assembly is about
25.5 minutes, so the 12-minute ordinary allowance is about 13.5 minutes lower
than that broad run alone without purchasing unrelated coverage.

## Acceptance and landing handoff

Acceptance requires the actual wrapped Windows PowerShell 5.1 hash, observed
Windows module source, preserved parent path, unchanged real pwsh child behavior,
and CP-1's complete passing roster at the final Code SHA. No Windows green is
claimed by this Plan. No decision or separate TestDesign dispatch is outstanding.

The Plan delegate commits/pushes this artifact on its assigned task branch only.
After settlement, the caller lands that exact pushed plan tip using the normal
task landing service, then dispatches Windows Code with this artifact's checkpoint
section and its landed commit. Do not push directly to master from the runner
mirror or rebase the assigned branch. Code -> ordinary Review -> land -> separate
Windows SourceLanding Mutation is the remaining implementation sequence.
