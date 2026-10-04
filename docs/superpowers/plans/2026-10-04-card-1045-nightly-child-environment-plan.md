# CARD-1045: restore nightly child environment replacement

Date: 2026-10-04. Plan with verification design folded in; complexity: easy.
Ready for Code with a 30-60 minute budget. No operator decision is outstanding.

This is a real launch-contract regression, established by the production code,
with a narrow compatibility fix. A supplied environment must replace the child's
inherited environment and omit empty values. An omitted/null environment must
continue to inherit. The current production safe-map caller still overwrites its
clear-list values, so this finding does not establish a current opt-in or
credential fail-open. Do not close it as merely theoretical: the existing G044
fixture already supplies a restricted map, and the native boundary violates that
map's replacement semantics.

## Ground truth

Inspected source: `a517c354fb07b5420788c063e6545e1185510c7d`.
Read the live card with `scripts/card.ps1 get CARD-1045 -Board Antiphon`.
The card attributes discovery to CARD-1039 S1n-a Review `81dea5db`; this plan
independently inspected the source, not that Review's raw Windows evidence.
No native execution, build or test was run in this Linux Plan checkout.

| Card premise / verification question | What the code does | Consequence |
|---|---|---|
| A null environment inherits; a supplied map replaces. | `scripts/lib/nightly-common.ps1`, `Start-NightlyProcess`, distinguishes null, removes unlisted parent names for a supplied map, removes null/empty values, and restores the parent in `finally`. | This is the compatibility contract to restore at the new boundary. Do not revive its temporary parent-environment mutation. |
| `Invoke-NightlyOwnedProcess` uses the native owner in production. | `scripts/lib/nightly-tests-impl.ps1` calls `Start-NightlyNativeOwner` after the controlled-I/O seam. The PowerShell wrapper initializes `$entries = @()` and uses `if ($Environment)`. | Null and an explicitly empty hashtable currently become the same empty string array. The wrapper must preserve the distinction. |
| A restricted supplied map excludes other parent names. | `scripts/lib/nightly-owned-process.cs`, `NativeProcessOwner.Run`, always seeds its sorted dictionary from `Environment.GetEnvironmentVariables()` before overlaying entries. | Unlisted parent names reach the child. Build the explicit block only from supplied entries. |
| Cleared names are absent. | The native overlay stores `name=` and serializes it into the Unicode block. | Empty values are present entries, unlike the previous removal behavior. Filter them from explicit maps. |
| Does production retain the old clear-list values? | `Get-NightlySafeChildEnvironment` in `scripts/lib/nightly-policy.ps1` copies the full process environment by default, sets every clear-list value to `''`, then applies policy overrides; messaging sets its broker opt-in. | The native overlay replaces the old values before CreateProcess. Preserve this caller and its policy; there is no evidence here of a current fail-open. |
| Does G044 catch replacement and name absence? | `scripts/test-nightly-tests.ps1`, `Test-C487_G044`, passes a restricted `BaseEnvironment` and checks `IsNullOrEmpty` for one cleared name. | It cannot distinguish absent from present-empty or detect an unlisted parent name. A child-side presence oracle is needed. |
| Does the native ownership checkpoint cover this contract? | `NightlyNativeOwnershipTests` has three methods: assignment/argv, descendant/root exit, and output drain. `scripts/test-nightly-native.ps1` and the compiled `owned-child` fixture implement those checks. | Add environment checks through the real production entry and retain all three existing regression methods. |
| Can the existing fixture observe the child environment? | `scripts/fixtures/nightly/owned-child/Program.cs` already accepts its directory and mode through argv; its project is staged by `Antiphon.Tests.csproj`. | Extend this fixture with an environment-observation mode. Control must not depend on variables being tested. |

Platform reads on 2026-10-04: `GET /api/runner-defaults` returned revision 2,
no kind overrides and no unresolved references. `GET /api/session-runners`
returned available, dispatch-eligible Windows and Linux descriptors plus one
unavailable draining descriptor. Re-read both at dispatch; availability is not a
reservation. This plan prescribes a **native Windows / PowerShell 7 / .NET lane**,
not a fleet host. Use `-Platform Windows` for Code, Review and Mutation; omit
`-Runner`. Planning itself needs no platform pin.

## Decisions

**D-1 - Fix the existing contract, without a new option.** Keep the public
`[hashtable]$Environment = $null` API. In `Start-NightlyNativeOwner`, initialize
the entries argument to null and populate a typed string array only when
`$null -ne $Environment`. Preserve an explicitly supplied zero-length array.
Do not use a positive-count admission check or pipeline output that collapses an
empty collection back to null. A local PowerShell binding probe confirmed that
an empty hashtable is truthy, so changing only `if ($Environment)` is not an
effective empty-map mutation. Null-valued hashtable entries retain the existing
string conversion to empty. Reject closing as theoretical, adding an
inherit/replace switch, or requiring all callers to manufacture complete maps:
the old API already defines these cases.

**D-2 - Serialize only explicit entries.** For a null entries array, pass
`IntPtr.Zero` as `lpEnvironment` to inherit normally. For a non-null array,
construct the existing case-insensitive sorted dictionary solely from that
array, split each entry at the first `=`, retain the existing malformed-name
guard, and omit entries whose value is empty. Keep nonempty values byte-for-byte
as UTF-16 strings, including spaces, further `=` characters and Unicode.
Allocate a non-null, double-NUL-terminated Unicode block even if the supplied
array is empty or every value was omitted. Keep the existing Unicode flag and
`finally` deallocation. The null-pointer inheritance and Unicode block framing
follow [Microsoft's CreateProcessW contract](https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-createprocessw).

Reject copying the live environment before overlay, passing a null pointer for
an empty map, or changing the parent's process environment. Leave executable
resolution, quoting, suspended creation, assignment, resume, job ownership,
timeout, stream drain and result fields unchanged. No environment-schema or
general Win32 launcher redesign is needed.

**D-3 - Observe the real child.** Reuse the compiled `OwnedChild.exe`, the native
PowerShell harness and `NightlyNativeOwnershipTests`; add four nonparameterized
methods. All four go through `Invoke-NightlyOwnedProcess` with `NightlySeams`
unset. The fixture receives its mode, results directory and selected names via
argv, and enumerates its raw environment using `GetEnvironmentStringsW` with
`FreeEnvironmentStringsW` in `finally`. The oracle must distinguish no entry
from `NAME=`; `IsNullOrEmpty(GetEnvironmentVariable(...))` is insufficient.
Observe only test-owned sentinel values and presence bits for policy clear-list
names. Never dump the full inherited environment or real credential values.

The harness owns a fresh results root, restores every changed parent sentinel
in `finally`, awaits the existing owner, and asserts exit zero, completed cleanup
and both final output sentinels before accepting the observation. Use the
existing assembly-local process limiter and Windows prerequisite failures.
Retain the current PASS/FAIL protocol; each new wrapper also requires its
decisive PASS label so an empty switch case cannot pass. No new retry, relaxed
timeout, production runner, database or external service is needed.

**D-4 - One bounded implementation slice; four behavior controls.** Commit/push
the complete fix and tests before the ordinary checkpoint run. Ordinary Code
and Review execute only the seven methods below. Post-land Mutation owns one
method-scoped PC per changed behavior. Preserve CARD-1039's existing custody PC
obligations; this card neither repeats them nor grants them new completion
credit. No whole-Unit, namespace, full nightly or operational qualification run.

## Implementation slices

| Slice | Files | Work and closing tests |
|---|---|---|
| S1 | `scripts/lib/nightly-owned-process.ps1`; `scripts/lib/nightly-owned-process.cs`; `scripts/test-nightly-native.ps1`; `scripts/fixtures/nightly/owned-child/Program.cs`; `tests/Antiphon.Tests/Scripts/NightlyNativeOwnershipTests.cs` | Preserve null versus explicit maps, serialize replacement blocks without empty values, add the fixture observation mode and four tests V-1..V-4. CP-1..CP-7 close this slice, including the three existing native regressions. |

No change is expected in the safe-map policy, `Start-NightlyProcess`, nightly
callers, project staging, schedules, custody logic or execution-policy JSON.
Existing native harness calls supplying only fixture-control entries must still
work as genuinely restricted maps; if a fixture dependency is exposed, declare
only the concrete runtime prerequisite in that fixture, never silently restore
parent merging in production. The empty-map test must remain genuinely empty.
Keep modified PowerShell files ASCII-only; these native scripts already require
PowerShell 7. Test observation artifacts stay in ignored temporary/results roots.

## Verification design

### Inspection

Bodies inspected: both native launcher files; `Invoke-NightlyOwnedProcess` and
`Wait-NightlyOwnedCleanup`; `Start-NightlyProcess`;
`Get-NightlySafeChildEnvironment`; G044; all three `NightlyNativeOwnershipTests`
methods and their `RunAsync` helper; the native PowerShell harness; `OwnedChild`
source/project and the test project's staging target. The null/map conversion
and native block boundary map to V-1..V-4; native custody/argv/drain map to R-1.
The only missing setup is the four named cases and the fixture observation mode,
implemented in S1. The staged executable is built once by the checkpoint, never
compiled inside a test.

### Delivery inventory

There is no new queued application delivery or recovery path. The relevant
transfer is caller map -> PowerShell entries -> CreateProcessW environment ->
child observation file -> joined owner result. A unique test results directory
and exact launched process identify each transfer. Acceptance requires the
child's selective observation plus successful owner completion and drained
output; inspecting only the intended input map earns no delivery credit.
Existing native job and drain recovery remain covered by R-1.

### Proves it works now

All new methods belong to `NightlyNativeOwnershipTests`, retain Integration and
`ParallelLimiter<ProcessSpawnLimit>`, and each contributes one TUnit result.
Internal subcases and PASS lines are not extra test results.

| ID | Exact method | Setup and decisive witness |
|---|---|---|
| V-1 | `NightlyNativeOwnershipTests.C1045_NullEnvironmentInherits` | Set a synthetic parent sentinel to a fixed nonempty value. Launch once with `-Environment` omitted and once with explicit `$null`. Both children must enumerate that name with the exact value; the parent remains unchanged. Required PASS label: `C1045 null inherits parent sentinel`. |
| V-2 | `NightlyNativeOwnershipTests.C1045_SuppliedEnvironmentReplaces` | Set parent-only and overridden synthetic names. Supply only the override plus a new nonempty synthetic value containing spaces, another `=` and a Unicode character constructed without non-ASCII script source. Require the parent-only name absent and exact supplied values present. Use different casing for the overridden name and require a single case-insensitive entry. Require the parent values unchanged. PASS label: `C1045 supplied map excludes parent sentinel`. |
| V-3 | `NightlyNativeOwnershipTests.C1045_EmptyEnvironmentDoesNotInherit` | With a nonempty parent sentinel, launch with literal `@{}`. Child startup and output must succeed and the sentinel must be absent; parent unchanged. Assert selected synthetic names, not total environment size, because runtime/OS-generated names are outside this contract. PASS label: `C1045 empty map excludes parent sentinel`. |
| V-4 | `NightlyNativeOwnershipTests.C1045_ClearedEntriesAreAbsent` | First supply a map with one retained synthetic value and two parent-seeded synthetic names mapped to `''` and `$null`. Require both names absent from the raw child block, and the retained value exact. Then exercise an all-cleared nonempty map, proving it still excludes the parent-only sentinel. Finally obtain the real policy map using `Get-NightlySafeChildEnvironment` without `BaseEnvironment`, seed a harmless headed opt-in in the harness process, and require every policy clear-list name whose final map value is empty to be absent in the child; the unrelated synthetic parent sentinel must survive this full-copy map. Do not seed or disclose credentials. Parent sentinel/opt-in values must be restored. PASS label: `C1045 cleared names absent from child block`. |

### Guards the regression

| ID | Exact method(s) | Required existing assertions |
|---|---|---|
| R-1 | `NightlyNativeOwnershipTests.C1039_AssignBeforeResume`; `NightlyNativeOwnershipTests.C1039_DescendantExit`; `NightlyNativeOwnershipTests.C1039_DrainBeforeReturn` | Keep assignment-before-execution/refusal, literal argv, root/descendant exit and job breakaway checks, both pipe readers and final log drain, and production entry cleanup consumption green. These are three TUnit results. Do not edit assertions to accommodate the environment fix. |

### Guard inventory

| Guard | Contract and plan reference | Control |
|---|---|---|
| G-1 | D-1/D-2: omitted/null map retains inheritance. | PC-1 |
| G-2 | D-2: supplied map cannot gain unlisted live parent entries. | PC-2 |
| G-3 | D-1/D-2: empty supplied map must not collapse into null/inheritance. | PC-3 |
| G-4 | D-2: empty/null supplied values remove names, including clear-list names. | PC-4 |

Guards=4, mapped=4, missing=0, duplicate PC maps=0. Existing malformed-entry,
argv and native custody guards are unchanged and retain their original controls.

### Positive controls

Mutation runs these serially after implementation land. Each is a compiling
production mutation with one exact method, one intended red, restoration and
fresh green. Each run must execute one TUnit result with zero skips. Setup,
fixture/runtime admission or build failure is not an intended assertion failure.

| PC | Compiling mutation | Exact method filter | Intended red witness |
|---|---|---|---|
| PC-1 | Initialize the wrapper's entries to an empty typed array instead of null, retaining the non-null map branch. | `/*/*/NightlyNativeOwnershipTests/C1045_NullEnvironmentInherits` | `C1045 null inherits parent sentinel` fails because the child lacks the sentinel. |
| PC-2 | In the native explicit-map branch, seed the dictionary from the live parent before applying supplied entries, retaining the empty-value omission. | `/*/*/NightlyNativeOwnershipTests/C1045_SuppliedEnvironmentReplaces` | `C1045 supplied map excludes parent sentinel` fails on an inherited unlisted entry. |
| PC-3 | Admit the wrapper's map branch only when `$null -ne $Environment -and $Environment.Count -gt 0`, leaving its initial entries value null. | `/*/*/NightlyNativeOwnershipTests/C1045_EmptyEnvironmentDoesNotInherit` | `C1045 empty map excludes parent sentinel` fails because an empty hashtable now inherits. |
| PC-4 | Retain empty values as `NAME=` entries in the native explicit block instead of omitting them. | `/*/*/NightlyNativeOwnershipTests/C1045_ClearedEntriesAreAbsent` | `C1045 cleared names absent from child block` fails on raw presence, even though value-based `IsNullOrEmpty` would pass. |

The first assertion after fixture admission in each case must be the indicated
witness, so a later compatibility check cannot mask the PC. Use a fresh harness
process for every cycle because Add-Type caches the loaded native owner type.
The modified scripts/C# library are loaded from source by that process; reuse of
the staged test/fixture binaries is valid only when those binaries are unchanged.
Keep mutations and evidence method-scoped. SourceLanding reports/restoration
remain in its assigned external evidence root; no commit/push or repair from
that snapshot. Code returns these four obligations as pending, not completed.

### Out of scope

No change to the policy clear list, broker opt-in policy, credentials, launcher
name validation, working-directory semantics, process cancellation, nightly
registration, scheduling or qualification. A raw child observation proves the
environment contract only, not operational full-nightly readiness. The known
G044 null-or-empty assertion is superseded for this boundary by V-4; running its
entire script suite would add unrelated work without an additional oracle.

### Execution and evidence

Run the checkpoint tool once after S1 is committed/pushed with this plan and
the exact source SHA: `run --plan <this-plan> --after S1 --expected-source-sha
<source-sha>`. Use the documented tool bootstrap under `scripts/build-slot.ps1`
with isolated `bin-c1045-tool/` output if a matching tool is unavailable; this
is a declared tooling bootstrap, not another product build. Await `wait` until
terminal exit, never settle on exit 75. The tool takes the row build slots.
Respect slot refusal/exit 4; do not run an unleased fallback.

Leave tracked source frozen during runs. Preserve complete, unedited CHECKPOINT
lines and source-qualified receipts for CP-1..CP-7, including counts and failures;
validate the receipts against the exact committed source. On inherited red,
confirm only the failing existing method at the base before assigning blame.
Run `scripts/check-evidence-diff.ps1` over the complete Code/Review task range.
Clean only this run's alternate output directories after all children exit.
Static plan coverage lint may require a disposition for PowerShell helper
witnesses; it cannot replace the native observations or the four actual PCs.

### Cost

Estimates, excluding build-slot queue time: Code authoring 25-35 minutes;
ordinary Code V/R floor 14 minutes, including one isolated product build and
seven narrow executions; Code total 39-49 minutes, ceiling 60. Review uses the
same 14-minute ordinary scope. Mutation estimates one setup/build allowance of
7 minutes plus four 3-minute red/restore/green pairs = 19 minutes, with reporting
outside that floor. Code plus Mutation execution floor is 33 minutes; adding
ordinary Review makes 47. Zero full-suite executions are required. This avoids
the documented approximately 25.5-minute Antiphon.Tests suite for each ordinary
round while retaining the affected native boundaries. Do not spend the remaining
budget on unrelated checks or repeated green runs.

### Checkpoints

Every group is in the native Windows lane. One isolated build is reused by all
seven rows; every row uses the exact method filter shown and runs serially.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---|---|---|
| CP-1 | S1 | tests/Antiphon.Tests -> bin-c1045-env/ | windows-null-env | `/*/*/NightlyNativeOwnershipTests/C1045_NullEnvironmentInherits` | V-1 | exact named method, 0 failed, 0 skipped | 1 | 7 | true |
| CP-2 | S1 | CP-1 | windows-replacement-env | `/*/*/NightlyNativeOwnershipTests/C1045_SuppliedEnvironmentReplaces` | V-2 | exact named method, 0 failed, 0 skipped | 1 | 1 | true |
| CP-3 | S1 | CP-1 | windows-empty-env | `/*/*/NightlyNativeOwnershipTests/C1045_EmptyEnvironmentDoesNotInherit` | V-3 | exact named method, 0 failed, 0 skipped | 1 | 1 | true |
| CP-4 | S1 | CP-1 | windows-cleared-env | `/*/*/NightlyNativeOwnershipTests/C1045_ClearedEntriesAreAbsent` | V-4 | exact named method, 0 failed, 0 skipped | 1 | 1 | true |
| CP-5 | S1 | CP-1 | windows-native-assignment | `/*/*/NightlyNativeOwnershipTests/C1039_AssignBeforeResume` | R-1 | exact named method, 0 failed, 0 skipped | 1 | 1 | true |
| CP-6 | S1 | CP-1 | windows-native-descendants | `/*/*/NightlyNativeOwnershipTests/C1039_DescendantExit` | R-1 | exact named method, 0 failed, 0 skipped | 1 | 2 | true |
| CP-7 | S1 | CP-1 | windows-native-drain | `/*/*/NightlyNativeOwnershipTests/C1039_DrainBeforeReturn` | R-1 | exact named method, 0 failed, 0 skipped | 1 | 1 | true |

## Handoff and publication

Publish this plan through the canonical land service immediately after this
Plan task settles. `AgentTaskLandService.RequestAsync` requires Succeeded for
ordinary land, so a Working delegate cannot land itself. Commit only on the
assigned task branch and push it; the caller lands its exact pushed SHA to master.
Never push master directly from this mirror or rewrite the branch.

After publication, commission Code with this artifact/plan commit, the seven
checkpoint rows, a 30-60 minute budget and `-Platform Windows` without a runner
pin. Serialize work with CARD-1039 slices touching the same launcher/harness.
Code -> ordinary Review -> implementation land -> SourceLanding Mutation for
PC-1..PC-4. Record the verification companion before implementation land. Next
stage is `code`; verification design is complete and needs no separate dispatch.
