# CARD-0845: forward named parameters to PowerShell script targets

Date: 2026-09-30. Stage: TestDesign finalized; next: Code.
Plan source: `e833925894b5e3d6ffea4d51ee9671b73473a218`.
TestDesign inspected plan/source at `8a30f2b4edca1463d7574c6e68429015a7e09653`
and re-read CARD-0845 in full through `scripts/card.ps1 get CARD-0845`
(card `4446f88b-1da0-4146-bead-8c28706b7af7`, board
`8988ca03-7414-47ad-b0b6-51556c701703`). This artifact changes no implementation.
TestDesign performed source inspection and a static census only: no builds,
tests, design probes or mutations. The verification design below is closed for Code.

## Outcome and collision boundary

Make a direct `.ps1` target of `scripts/build-slot.ps1` receive named parameters
through a child PowerShell 7 `-File` invocation. Use the existing literal native
launcher, propagate its process exit code, and retain the wrapper's lease until
the child finishes and cleanup runs. Correct checkpoint recipes to invoke
`run-checkpoint.ps1` directly: that driver already takes its own slot.

The implementation footprint is **only build-slot.ps1, its tests, and its
documentation**:

- `scripts/build-slot.ps1`.
- `scripts/test-build-slot.ps1` and
  `tests/Antiphon.Tests/Scripts/BuildSlotScriptTests.cs`.
- `tests/Antiphon.SessionRunner.Tests/BuildSlotEndToEndTests.cs`, an existing test
  of the wrapper against an isolated broker.
- `docs/testing-and-build.md`, this artifact, and narrowly bounded wrapper
  corrections in the CARD-0603, CARD-0835 and CARD-0800 plans listed below.

**Do not edit `scripts/run-checkpoint.ps1` or `scripts/checkpoint-usage.ps1`.**
CARD-0835 owns checkpoint receipts; CARD-0804/0805 own checkpoint lifecycle work
awaiting land. `checkpoint-usage.ps1` is not present at this source revision;
its later arrival does not admit it to this card's scope. Also leave
`scripts/lib/build-slot.ps1`, broker/runner production code, checkpoint tooling,
shared harness helpers, bundles, AGENTS.md and generated `docs/cards/` untouched.
Coordinate the shared owner-document and CARD-0835 plan paragraphs at landing;
do not overwrite concurrent receipt/lifecycle edits or borrow their source files.

## Ground truth and root cause

| Assumption or question | Evidence at the source revision | Consequence |
|---|---|---|
| The wrapper can pass `-Name CP-1` to an in-process script. | `scripts/build-slot.ps1:118-130` resolves an `ExternalScript` into the fallback `& $command[0] @rest`; `rest` is an ordinary string array. Its elements bind positionally, including strings beginning with `-`. | The target sees parameter spellings as values. For the incident invocation, `bin-c603-cp1/` reaches the integer `MinExecuted` parameter before any driver body runs. |
| run-checkpoint needs an outer lease. | `scripts/run-checkpoint.ps1:103-126` already acquires after validation and releases in its own `finally` at `:204`. | The documented direct `pwsh -File scripts/run-checkpoint.ps1 ...` route is already leased. |
| Moving scripts to a child automatically shares the lease. | `BuildSlotBroker.TryAcquire` matches both holder PID and process start time (`src/Antiphon.SessionRunner/BuildSlotBroker.cs:80-87`). The library defaults `HolderPid` to its own `$PID`; there is no inherited-lease protocol. | A nested checkpoint child would ask for a second lease. At budget one it waits behind its parent. Correct the recipe; do not invent lease sharing inside this change. |
| A literal launcher is missing. | `Start-AntiphonWrappedCommand` already uses `ProcessStartInfo.ArgumentList`, the caller's PowerShell location, inherited output, a 250 ms completion poll, and process-tree cleanup before release. | Reuse it for `.ps1`; keep CARD-0800's native argv fix. |
| Existing script coverage catches the bug. | `C800_WrapperKeepsScriptCommandsInProcess` runs a script without a declared parameter block and checks raw tokens/exit, not named binding or PID equality. The ordinary C589 command shim bypasses the real script-dispatch branch. | Add real parameterized targets with `C589_COMMAND_SHIM` unset, and rename the misleading existing case without dropping its assertions. |
| A hashtable splat is an equivalent small fix. | A hashtable names parameters, but translating arbitrary CLI tokens needs target metadata, switch/colon/alias handling, negative and dash-prefixed values, duplicate rejection, positional/unbound arguments and array rules. | Let the PowerShell CLI bind its own arguments. No second parameter parser and no evaluation of joined source text. |

Microsoft documents array splatting as positional and hashtable splatting as
named in [about_Splatting](https://learn.microsoft.com/en-us/powershell/module/microsoft.powershell.core/about/about_splatting?view=powershell-7.5).
The `-File` argument/exit contract is documented in
[about_Pwsh](https://learn.microsoft.com/en-us/powershell/module/microsoft.powershell.core/about/about_pwsh?view=powershell-7.5).

### Measured design probes

Four disposable probes ran on Linux, PowerShell **7.5.4**, using the real current
wrapper, the existing offline slot shim, a dead fallback broker URL, and no
command shim. No .NET build, TUnit run, real checkpoint or production broker was
used. The candidate shape was exercised through the wrapper's already-working
native `-- pwsh -NoProfile -NonInteractive -File <fixture> ...` branch; this is
evidence for the chosen mechanism, not evidence that the direct-script fix exists.

| Probe | Observed exit and receipt |
|---|---|
| Direct parameterized `.ps1`, current code | Exit 1, `MinExecuted` conversion error, `POST,DELETE`, target body never ran. |
| Child `pwsh -File`, reordered named values and a switch | Exit 0; exact Name/Project/MinExecuted/Flag values; script path, working directory and Project contained spaces; quotes, semicolon, dollar sign, wildcard and trailing backslash survived in a string value; `POST,CMD,DELETE`. |
| Child explicitly exits 7 | Wrapper exit 7; `POST,CMD,DELETE`. |
| Child throws after its body marker | Wrapper exit 1; `POST,CMD,DELETE`. |

Local scratch evidence was `/tmp/c845 plan probe a5f23a59476249579b664dd7405a8c51/summary.json`
and sibling stdout/stderr/log files. The table preserves the findings when that
scratch is retired. An initial Python probe launcher could not start because
`python3` was absent (exit 127); the four completed probes used PowerShell.
Windows behavior has not been measured in this Plan.

### Documentation audit

The repository-wide `build-slot.ps1` reference search and surrounding invocation
passages found two direct-script nested checkpoint recipes. Correct all active
claims within these bounded passages, not just the command line:

| Document | Required disposition |
|---|---|
| `docs/testing-and-build.md`, Build slots | Replace the claim that PowerShell scripts retain the call operator. Explain child `pwsh -File`, literal tokens, exit semantics and one slot-owning layer. Keep native examples and checkpoint's direct recipe. |
| `docs/superpowers/plans/2026-09-30-card-0603-repairsource-land-failed-owner-plan.md` | Correct the verification inspection row, Execution/platform/receipt rules, CP-1 example and final same-process-validation claim. Use direct `pwsh -NoProfile -File scripts/run-checkpoint.ps1` with all existing row arguments unchanged. Preserve its CARD-0823 tool exception, counts, filters, source-clean receipts and recovery design. |
| `docs/superpowers/plans/2026-09-30-card-0835-checkpoint-receipt-dirty-tree-plan.md` | Correct Platform qualification and Runnable procedure, including the CP-1 example and PID/double-release explanation. Preserve `-ExpectedSourceSha`, all receipt requirements and that plan's own launcher exception. Only wrapper documentation is in this footprint. |
| `docs/superpowers/plans/2026-09-29-card-0800-build-slot-literal-argv-plan.md` | Add a clearly dated supersession note for D-2/V-3 and the renamed script case: CARD-0845 moves ExternalScript to a child. Preserve its historical measurements, completed checkpoint counts and historical PC results. |

The remaining reference inventory does not prescribe the failing direct `.ps1`
checkpoint shape. It was read and needs no binding-related rewrite:

- AGENTS.md, `docs/ops-http.md`, `docs/docker-stack.md`,
  `docs/orchestration-loop.md` and `server/Bundles/delegate-basics.md` state the
  general gate or already distinguish checkpoint self-leasing.
- Plans CARD-0417, 0458, 0718 and 0726 use native commands or explicit
  `-- pwsh -File` for client/measurement scripts. Their PowerShell target binding
  already happens in a child.
- Plans CARD-0452, 0464, 0494, 0578, 0580, 0589, 0593, 0650, 0675, 0723, 0727,
  0772, 0802, 0807, 0808, 0810, 0812 and the 2026-09-26 checkpoint-tool-hardening
  plan refer to native drivers, tool bootstrap/launch, the lease library or
  historical wrapper behavior. This card does not change their tool lifecycle.
- The CARD-0407 verification plan and investigations CARD-0418, 0550, 0727,
  0728 and 0730 record past runs or broker configuration; retain historical evidence.

Repeat the search before S3 closes to catch concurrent additions. A newly found
binding-related wrapper recipe belongs in S3's documentation inventory. This is
not authorization for a general checkpoint-tool or old-plan cleanup.

## Decisions

**D-1: add an ExternalScript branch.** After existing command resolution and before
the call-operator fallback, a resolved `CommandType ExternalScript` runs through
`Start-AntiphonWrappedCommand`. Launch the actual PowerShell 7 binary under
`$PSHOME` (`pwsh.exe` on Windows, `pwsh` elsewhere) so a PATH alias cannot select
Windows PowerShell 5.1 or a different interpreter. Pass the token array
`-NoProfile`, `-NonInteractive`, `-File`, resolved script path, followed by the
unchanged flattened `rest` tokens. Use `ArgumentList`, never `.Arguments` joined
by spaces, `Start-Process -ArgumentList`, `Invoke-Expression`, `-Command`, or a
shell command string. Do not add quotes around elements of an argument array.

Keep raw-argv capture, array flattening (including empty elements), wrapper option
parsing, native executable routing, command-shim routing, `.cmd`/`.bat` behavior
and function/cmdlet/alias fallback unchanged. Named forwarding in those fallback
command types is not being promised. Resolve the script path once; relative
values still resolve from the caller's PowerShell location in the child.

**D-2: use process exit semantics.** Assign `$code` from the helper's returned
`Process.ExitCode`; do not consult the parent's stale `$LASTEXITCODE` for this
branch. Explicit `exit N` propagates N (test portable 0..255 values); normal script
completion is 0 and a terminating error or parameter-binding failure is 1.
Nonterminating errors follow the target's own PowerShell preferences: scripts
that need a failing process must throw or explicitly exit nonzero. No new mapping
turns target binding errors into wrapper usage exit 2. Missing wrapper command
remains 2; broker wait timeout remains 4 with no target launched.

A child inherits environment, working directory and console streams, not parent
PowerShell functions, aliases, variables, preferences or live pipeline objects.
This is a documented CLI boundary. Structured in-process output and parent-scope
mutation are not supported target contracts. No automatic execution-policy
bypass is added on Windows. The wrapper already requires PowerShell 7.

**D-3: keep lease custody in the wrapper.** Acquisition and the existing finally
remain in the parent. Await the child; kill/dispose a still-running owned child
tree on interruption before releasing. Normal nonzero exit, throw, binding error,
resolution/start failure all pass through release when a grant exists. Preserve
the existing renewal job, timeout/unlimited/unreachable outcomes and diagnostic
lines. A hard process kill still relies on broker reap/renewal expiry; do not
claim finally runs after SIGKILL or forcible Windows termination.

**D-4: one slot-owning layer in recipes.** Invoke `run-checkpoint.ps1` directly.
Do not wrap it in build-slot, insert an extra native `pwsh` to hide the nesting,
inject `-NoSlot`, spoof holder PIDs, or copy a lease into an environment variable.
The new child branch is for scripts that rely on the wrapper for their lease.
It does not automatically recognize or share leases with arbitrary self-leasing
scripts. Nested self-leasing callers remain unsupported and may time out at low
budget; removing the two prescribed instances is part of acceptance, not optional
follow-up work. No special-case parser or new refusal for run-checkpoint is needed.

The CARD-0603 CP-1 correction starts as follows; preserve the rest of its existing
filter/Expect/MinExecuted/ResultsRoot arguments exactly:

```powershell
pwsh -NoProfile -File scripts/run-checkpoint.ps1 -Name CP-1 -Project tests/Antiphon.Tests -OutputPath bin-c603-cp1/ -Filter '/*/Antiphon.Tests.Application/(RepairSourceRecoveryLandingTests*)|(RepairSourceLandRefusalTests*)/*' -Expect RepairSourceRecoveryLandingTests.C603_FailedOwnerLandsReviewedRepairedTip,RepairSourceRecoveryLandingTests.C603_RepairSuccessDoesNotAuthorizeUnreviewedOwnerLand,RepairSourceLandRefusalTests.C603_RepairLandRefusalNamesReviewedOwnerRecovery,RepairSourceLandRefusalTests.C499_V24ii_LandRequestIsRefusedForARepairTask,RepairSourceLandRefusalTests.C499_V24iii_ProtocolEntryIsRefusedForARepairTask,RepairSourceLandRefusalTests.C499_V24iv_OwnerLandRequestStillQueues -MinExecuted 10 -ResultsRoot .antiphon/c603-cp1-attempt1
```

**D-5: preserve literal path values on both systems.** A script path and a string
parameter value containing spaces are each one `ArgumentList` element. Quotes,
empty strings, wildcard text, dollar signs and backslashes are data after the
caller's shell has produced argv. Callers must still quote values in their own
shell, for example `-- './driver folder/build.ps1' -Project 'project folder/app'`.
The child's `-File` binder owns switch syntax and conversions; do not promise
arbitrary object/array parameters over a string CLI. Checkpoint's comma-delimited
`-Expect` and `-MsBuildProperty` conventions remain unchanged. OutputPath is still
the checkpoint driver's guarded `bin-name/` format, not a path-with-spaces test.

**D-6: separate TestDesign completed.** The fixture specifications, exact PASS
inventories, two broker-test modifications and four observable mutation failures
are fixed below. Runtime qualification remains Code's work. No user decision is
needed to choose the child-process design. Return to Plan only for a demonstrated
incompatibility that cannot fit this boundary.

Rejected alternatives: a home-grown hashtable parser (another PowerShell binder),
rejecting every `.ps1` target (unnecessarily removes a useful supported command
kind), eval/command-string reconstruction (reparses data), and introducing shared
lease inheritance (requires excluded lease/checkpoint changes and new ownership
semantics). Fixing documentation alone leaves other named script targets broken.

## Slices

| Slice | Files and concrete deliverable | Verification |
|---|---|---|
| S1: executable regression fixtures | `scripts/test-build-slot.ps1`, `tests/Antiphon.Tests/Scripts/BuildSlotScriptTests.cs`, `tests/Antiphon.SessionRunner.Tests/BuildSlotEndToEndTests.cs` | Four C845 cases, preserve/rename C800 script case, run existing broker scenarios with a direct named script target. V-1..V-5 and R-1 below. |
| S2: script child launch | `scripts/build-slot.ps1` only; ExternalScript routing and matching synopsis | D-1..D-3, V-1..V-5. Keep scripts ASCII-only. |
| S3: correct wrapper guidance | Four existing docs in the audit's correction table, plus this plan if TestDesign changes the roster | D-4/D-5; exact before/after recipe review. No edits to checkpoint implementation. |

Commit and push each slice. Run the ordinary checkpoint group only after S1-S3
are committed and the tree is clean. No test-only red checkpoint is commissioned;
the reproduced baseline and the post-land method-scoped controls establish red
capability. Code reports any extra run and its reason. No runtime test/build is
required to commit this TestDesign-only artifact.

## Verification design

### Inspection and test isolation

Read bodies: `scripts/{build-slot,test-build-slot,run-checkpoint}.ps1`,
`scripts/lib/{build-slot,c487-harness}.ps1`, both C589 slot/command fixtures,
`BuildSlotScriptTests`, `ScriptHarness`, `BuildSlotEndToEndTests`,
`BuildSlotTestHost`, `ProcessSpawnLimit`, broker `TryAcquire`, and the owner
document's Build slots, Checkpoint manifest, Checkpoint runner and Mutation rules.

The fresh-process cases use `Invoke-C589Wrapper -DisableCommandShim` with a real
temporary `.ps1`, the C589 slot shim and a dead `ANTIPHON_BUILD_SLOTS_URL` fallback.
They do not run run-checkpoint, dotnet or a provider inside their target. Use
literal `ProcessStartInfo.ArgumentList` in test launchers. Include the wrapper's
fresh-process and in-process entry forms, using a temporary PowerShell caller for
the latter so caller state cannot leak into another case. Fixture payloads report
bound values, PID, working directory and a body marker as JSON/log data.

Assertions must first check that output/JSON exists and parses, then compare it
with independently supplied values. A missing payload produces a named assertion
failure, not a fixture parser exception. Restore test seam variables in finally;
bound and await every fixture child. Retain each assembly's
`[ParallelLimiter<ProcessSpawnLimit>]`. No shared helper changes are necessary.

The in-process cases need a **harness-local** caller launcher: start a temporary
caller directly with `pwsh -NoProfile -NonInteractive -File <caller>`, not as the
target of another build-slot wrapper. That caller invokes `& <wrapper> @tokens`,
captures `$LASTEXITCODE` immediately and explicitly exits with it. Otherwise a
normally completing caller can mask the wrapper's nonzero result. Its raw argv
names the caller, so the wrapper exercises its `$args` entry path. Seed stale
`$global:LASTEXITCODE` only in that disposable caller. Keep helpers in
`scripts/test-build-slot.ps1`; do not edit `ScriptHarness` or the C589 fixtures.
Use the existing result/wait shape and a 30-second deadline per C845 child,
drain output and kill/await/dispose an owned tree on timeout. Preserve/restore
the C589 seam environment around these cases, including any extra fixture
variables, without changing the shared lease library.

The slot shim already logs `SLOT POST label=... pid=... uri=...` and
`SLOT DELETE <id>`; its granted ID is `$script:Lease`. Parse exactly one POST
holder PID and require a positive integer before comparing it with the fixture
PID. Require exactly one DELETE overall **and** that it names this granted ID.
The real fixture appends its own `CMD <scenario>` to the same log; the disabled
command shim cannot supply a body marker. For JSON cases emit one prefixed JSON
line, catch parse failures, check required properties, then guard comparisons on
that success. Always emit the full assertion inventory, even when the payload is
missing. `Wait-C589Wrapper.Text` merges stdout and stderr, so distinct sentinels
there prove both arrived, without claiming separate-stream attribution.

### Proves it works now

Each V-1..V-4 is **one** nonparameterized TUnit method in `BuildSlotScriptTests`,
mapping to `Test-<same name>` and `RunHarnessCaseAsync` with prefix `C845`.
Internal scenario/PASS counts are not TUnit execution counts.

| ID / exact new method | Required scenarios and decisive assertions |
|---|---|
| V-1 `C845_WrapperBindsNamedScriptParameters` | A fixture declares string Name/Project/OutputPath, int MinExecuted, a switch and a string Expect. Send parameters in a different order from their declaration, with MinExecuted=3, both an included switch and a `-Switch:$false` case, and comma-delimited Expect. Repeat through a PowerShell caller invoking the wrapper in-process. Require exit 0 and exact typed values. Compare fixture PID with the logged POST holder PID: the target must be a child. The old array splat must fail the named-values assertion. |
| V-2 `C845_WrapperPropagatesScriptExitCodes` | Separate real targets explicitly exit 0, 7 and 23; another sets LASTEXITCODE=23 and completes normally. Require wrapper exits 0/7/23/0 respectively and body/stdout/stderr sentinels where emitted. A parent caller with a prior nonzero LASTEXITCODE must not affect success. Capture the outer wrapper's actual process exit, not the harness's current LASTEXITCODE. |
| V-3 `C845_WrapperReleasesLeaseAfterScriptFailure` | Real targets (a) mark body then exit 9, (b) mark body then throw, (c) fail typed binding before their body. Require exits 9/1/1; exactly one granted-lease DELETE, matching the granted ID, after the target for (a)/(b); exact `POST,CMD,DELETE` order there and `POST,DELETE` with no body marker for (c). Also cover an unresolved target after acquisition: nonzero exit and one DELETE. A release log message alone is insufficient; inspect shim calls. |
| V-4 `C845_WrapperPreservesScriptPathsAndValues` | Put the script and caller working directory under a temporary path with spaces. Use named string values containing spaces, embedded quotes, an empty string, literal dollar/semicolon/subexpression text, wildcard/bracket filter text and a trailing backslash. Require target execution and exact JSON values; no interpolation side-effect marker. Include a relative target and named relative path, launched after Push-Location differs from Environment.CurrentDirectory, and an absolute script path. Require the fixture's cwd equals the caller's location. |

Use small independent fixture files/subcases inside each method; no sleeps are
needed for these four tests. The following is the exact C845 PASS inventory.
Each row expands the Cartesian product of its listed scenarios and suffixes;
the name is `C845 <method without C845_> <scenario> <suffix>` with single spaces.
For example, `C845 WrapperBindsNamedScriptParameters file-true binds typed values`.
Pass every expanded name as a `requiredRows` entry and the stated count as
`expectedRows` to `RunHarnessCaseAsync("test-build-slot.ps1", "C845", ...)`.
Each predicate below is one `Assert-C487`, not an extra TUnit method.

| Method stem | Exact scenario names | Exact assertion suffixes | PASS rows |
|---|---|---|---:|
| `WrapperBindsNamedScriptParameters` | `file-true`, `file-false`, `inprocess-true`, `inprocess-false` | `exits zero`; `payload parses`; `binds typed values`; `runs outside the lease holder` | 16 |
| `WrapperPropagatesScriptExitCodes` | `exit0`, `exit7`, `exit23`, `normal23`, `parent37` | `propagates the process exit`; `preserves body and stream sentinels` | 10 |
| `WrapperReleasesLeaseAfterScriptFailure` | `exit9`, `throw`, `binding`, `unresolved` | `returns the failure exit`; `observes the body boundary`; `releases the granted lease in order` | 12 |
| `WrapperPreservesScriptPathsAndValues` | `absolute`, `pushed-relative` | `exits zero`; `payload parses`; `preserves literal values`; `uses caller location`; `resolves relative input`; `does not evaluate text` | 12 |

Concrete fixture/expected-value rules:

- **V-1 (16 rows):** declare `[string]$Name`, `[string]$Project`,
  `[string]$OutputPath`, `[int]$MinExecuted`, `[switch]$Switch`, `[string]$Expect`
  in that order. Send `-Expect A.One,B.Two -OutputPath bin-c845/ -MinExecuted 3
  -Project 'project folder/app' -Name CP-1` followed by `-Switch` or the single
  literal token `-Switch:$false`. This reordering makes the old positional splat
  bind `bin-c845/` to the integer MinExecuted and fail before the body. Compare
  every value with these independent inputs; JSON must contain numeric 3 and a
  Boolean switch (emit `Switch.IsPresent`), not stringified stand-ins. All four
  payloads must have a positive PID unequal to the logged holder PID; require
  one POST and `POST,CMD,DELETE`, so the assertion also observes lease custody.
  PID inequality proves a separate process; source inspection establishes direct
  parentage, which the offline shim does not expose.
- **V-2 (10 rows):** separate targets for explicit `exit 0`, `exit 7`, `exit 23`,
  and normal completion after `$global:LASTEXITCODE = 23`. A fifth case uses a
  normal target from a caller seeded with `$global:LASTEXITCODE = 37`. Expected
  outer process exits are respectively 0, 7, 23, 0, 0, all without timeout.
  Each target writes exactly one `CMD <scenario>`, a unique stdout sentinel via
  `[Console]::Out.WriteLine` and a unique stderr sentinel via
  `[Console]::Error.WriteLine` before completing. Avoid `Write-Error`, whose
  behavior depends on error preferences. Require all three sentinels per case.
- **V-3 (12 rows):** append `CMD exit9`/`CMD throw` immediately before `exit 9`
  or a terminating `throw`. The binding target has `param([int]$Count)` and gets
  `-Count not-an-integer`; its first body statement would append `CMD binding`.
  The unresolved target is a unique nonexistent absolute `.ps1` path in the
  owned fixture directory. Expected exits are 9, 1, 1 and nonzero, with no
  timeout. Require one correct body marker for exit9/throw and no CMD marker
  for binding/unresolved. Require exactly one POST and matching DELETE, with
  exact `POST,CMD,DELETE` or `POST,DELETE` order respectively. Absence of a body
  marker alone cannot pass a release assertion.
- **V-4 (12 rows):** both fixture paths and working directories contain spaces.
  Both subcases send distinct named string values for spaces, embedded single
  and double quotes, empty text, literal dollar/semicolon/subexpression text,
  wildcard/bracket text and a trailing backslash. Declare the empty string
  parameter with `[AllowEmptyString()]`. Compare each JSON field case-sensitively
  with the independently supplied token, including the empty string. Put a
  unique side-effect-marker path inside the literal subexpression text and
  require its absence after successful execution. Each fixture also receives
  a relative input-file path and reads a unique prewritten sentinel from it;
  assert both the unmodified path string and the sentinel. `absolute` uses a
  full target path via the wrapper's fresh-process entry. `pushed-relative`
  starts a disposable caller in a different directory, performs `Push-Location`
  and invokes the wrapper with `./driver folder/target.ps1`. Record caller
  location and `[Environment]::CurrentDirectory` before invocation and require
  they differ; require the child cwd to equal the independently known pushed
  directory and its relative input read to succeed. The fixture never evaluates
  or joins argument text into code.

### Static census and harness discovery

At the inspected SHA, `BuildSlotScriptTests` has 13 `[Test]` methods and
`BuildSlotEndToEndTests` has two; none has `[Arguments]` or a data source. Only
the explicitly excluded SIGINT method has an OS skip. Renaming the C800 script
case replaces one method, so it does not increase this census.

| CP-1 existing method (post-rename name) | TUnit executions | Harness PASS rows |
|---|---:|---:|
| `C589_WrapperRunsUnderLease` | 1 | 7 |
| `C589_WrapperMaxCpuCountRules` | 1 | 12 |
| `C589_WrapperReleasesOnFailure` | 1 | 2 |
| `C589_WrapperTimeout` | 1 | 3 |
| `C589_WrapperUnreachableAtDeadline` | 1 | 4 |
| `C589_WrapperUnreachable` | 1 | 4 |
| `C589_WrapperAsciiOnly` | 1 | 5 |
| `Wrapper_renews_a_renew_mode_grant_while_the_command_runs` | 1 | 2 |
| `C800_WrapperPassesWildcardArgvLiterally` | 1 | 6 |
| `C800_WrapperStartsUnitFilterWithinDeadline` | 1 | 3 |
| `C800_WrapperForwardsScriptTokens` | 1 | 4 |
| `C800_WrapperLaunchesNativeExecutableLiterally` | 1 | 3 |
| Existing portable subtotal | 12 | 55 |
| Four C845 methods above | 4 | 50 |
| CP-1 total on either OS | **16** | **105** |

The C589 harness subtotal is **39**, not the current aggregate floor's 35:
MaxCpuCountRules has eleven loop rows plus its wrapper row, and AsciiOnly checks
five files. Keep their existing per-case counts/required names. C800 contributes
16 portable rows, plus three Linux-only interrupt rows outside CP-1. Add
`Get-C487CaseFunctions -Prefix 'C845_'` to standalone discovery and change its
floor to `39 + 16 + 50 + $(if ($IsLinux) { 3 } else { 0 })`: **105 on Windows,
108 on Linux**. This fixes accounting inside the existing harness, not checkpoint
tooling. The standalone harness is not an additional commissioned run.

Together with the two unchanged-count broker methods below, ordinary execution
is **16 + 2 = 18 tests per OS, 36 across both OSes**, with zero skips. These are
static expectations for the future implementation, not executed results.

**V-5: real broker composition, two existing executions.** In
`BuildSlotEndToEndTests.Wrapper.Start`, replace the target's `pwsh ... -File hold`
prefix with `hold` directly and send `-Seconds`, `-File`, `-Name` in a reordered
named sequence. Keep the broker on a random loopback port, budget one, with its
existing bounded wait and cleanup. Explicitly remove `C589_COMMAND_SHIM` as well
as the slot seams from the child environment. Preserve both methods and all
assertions:

- `Two_wrappers_on_a_budget_of_one_run_one_after_the_other`: both finish, the
  second waits, recorded intervals do not overlap, and final Occupied is zero.
- `A_wrapper_killed_mid_hold_is_reaped_and_the_waiter_is_granted`: kill only the
  test-owned wrapper tree, then the waiting wrapper runs and the dead holder's
  lease is absent. This proves reap behavior, not finally-after-hard-kill.

The exact replacement tail is `hold, "-Seconds", holdSeconds.ToString(), "-File",
marks, "-Name", label`, each a separate `ArgumentList` element. The fixture's
existing declaration is Name/File/Seconds; keep it unchanged so this is named
binding rather than coincidentally correct positional order. Keep the existing
90-second bounds, process limiter and test-owned process-tree cleanup. The first
method asserts zero occupancy after both normal completions; the hard-kill method
asserts waiter completion and absence of the dead holder, not a finally receipt.

### Guards the regression

**R-1: twelve existing portable wrapper methods**, selected in CP-1:
the seven `C589_*` methods; `Wrapper_renews_a_renew_mode_grant_while_the_command_runs`;
`C800_WrapperPassesWildcardArgvLiterally`;
`C800_WrapperStartsUnitFilterWithinDeadline`;
`C800_WrapperLaunchesNativeExecutableLiterally`; and the renamed
`C800_WrapperForwardsScriptTokens` (formerly
`C800_WrapperKeepsScriptCommandsInProcess`). Rename its harness/PASS text too;
keep all four existing assertions for exit 6, tokens, nested-array flattening and
empty elements. These tests pin maxcpucount placement, timeout/no launch,
unreachable/unlimited behavior, renewals, ASCII, literal native argv and cwd.

**R-2: documentation review.** Inspect each bounded change in the audit table.
Neither active checkpoint recipe may still prescribe an outer lease or claim
same-process binding is verified. Preserve all unrelated filters/expected counts
and CARD-0835's SHA/receipt requirements. Read the final diff for excluded files.
This is an editorial check, not another test or a new documentation test class.

The existing Linux-only `C800_WrapperInterruptKillsChildAndReleasesLease` is not
in the portable CP-1 filter: this card does not change the shared polling/kill
implementation and must not turn its Windows skip into claimed coverage. Review
checks that implementation is unchanged. V-5 separately covers forcible wrapper
death and queue progress on both platforms. A future cancellation implementation
change requires revising this bounded selection.

### Delivery inventory and evidence limits

No agent/session messaging or durable async delivery path changes. The relevant
path is wrapper PID/start identity -> acquired lease ID -> child script -> child
exit -> release of that ID. Offline JSON and call logs observe named binding and
release ordering; they cannot prove the real broker freed capacity. V-5 joins the
real wrapper to a real isolated broker and observes the next holder and final
occupancy. There is no lease-transfer protocol to fake or test.

### Guard inventory and positive controls

Four changed guarantees, four distinct mappings, zero missing, zero duplicate PC
mappings. Existing broker admission/reap, renewals and native cancellation remain
inherited boundaries, not newly qualified mutations.

| Guard | Positive control and deliberate production fault | Exact method filter; intended red |
|---|---|---|
| G-1: named script binding and child isolation (D-1) | PC-1: replace only the new ExternalScript dispatch with the old `& $command[0] @rest` plus its old exit fallback. | `/*/Antiphon.Tests.Scripts/BuildSlotScriptTests/C845_WrapperBindsNamedScriptParameters*`; `FAIL C845 WrapperBindsNamedScriptParameters file-true exits zero` observes exit 1 from the MinExecuted binding error. `file-true binds typed values` also fails safely without JSON. |
| G-2: process exit propagation (D-2) | PC-2: keep the script child launch/await but assign `$code = 0` after it, only in ExternalScript dispatch. | `/*/Antiphon.Tests.Scripts/BuildSlotScriptTests/C845_WrapperPropagatesScriptExitCodes*`; `FAIL C845 WrapperPropagatesScriptExitCodes exit7 propagates the process exit` observes 0 instead of 7; the exit23 assertion also fails. Body/stream assertions stay green. |
| G-3: release after failure (D-3) | PC-3: guard the wrapper finally's `Exit-AntiphonBuildSlot` call with `$code -eq 0`. Keep child disposal and the offline slot shim unchanged. | `/*/Antiphon.Tests.Scripts/BuildSlotScriptTests/C845_WrapperReleasesLeaseAfterScriptFailure*`; `FAIL C845 WrapperReleasesLeaseAfterScriptFailure exit9 releases the granted lease in order` observes `POST,CMD` without the matching DELETE. The exit9 and body assertions stay green. |
| G-4: literal script path and values (D-5) | PC-4: in the existing launcher, replace the ArgumentList population with `$psi.Arguments = $Arguments -join ' '`. Keep the harness launcher literal and the fixture file intact. | `/*/Antiphon.Tests.Scripts/BuildSlotScriptTests/C845_WrapperPreservesScriptPathsAndValues*`; `FAIL C845 WrapperPreservesScriptPathsAndValues absolute exits zero` observes the spaced target path split before the body. Payload/value assertions also fail safely. The test harness and TUnit host must still complete. |

Run each PC separately after confirmed land in SourceLanding Mutation. For every
phase use its exact method filter, `-Expect BuildSlotScriptTests.<method>` and
`-MinExecuted 1`. Baseline: exactly 1 executed/passed, 0 failed/skipped, exit 0.
Red: exactly 1 executed/failed, 0 passed/skipped, driver exit 1 with the named
assertion. Restore exact source bytes, refresh timestamps and rebuild green:
exactly 1 executed/passed, 0 failed/skipped, exit 0. A compiler error, missing TRX,
zero selection, harness startup failure or timeout is not a PC red.

`ScriptHarness` first asserts harness exit 0 and includes the full captured output
in that failure. Therefore the intended red TRX may report that outer assertion;
its diagnostic must contain the exact `FAIL C845 ...` line above. A target's
binding error or split-path refusal is intended production behavior under PC-1
or PC-4; an unhandled fixture parsing exception is not. These are statically
validated mutation sites and expected observations, not executed PC evidence.

Copy the unchanged checkpoint driver and its required library/helper files into
the task's external evidence root before mutation, as the testing owner requires.
Run it directly so the mutated wrapper cannot sabotage its own verification
driver. Use separate `bin-c845-pcN-baseline/`, `bin-c845-pcN-red/`,
`bin-c845-pcN-green/` outputs and fresh external result roots. All four faults
touch the wrapper; do not batch them. Preserve phase build/run receipts, TRX,
patch/digest and restoration evidence. Code runs ordinary V/R, not these faults.

### Platform, execution and source receipts

The Plan read `/api/runner-defaults` and `/api/session-runners`: the preference
resolves to Linux and eligible Linux and Windows runners were available. These
are transient routing facts, not host pins. Omit `-Runner` for ordinary dispatch;
the card stays Any. Require the same two rows on **real Linux and real Windows**
before final Review, recording OS and PowerShell version separately. A simulated
platform variable does not qualify Windows argv. No desktop UI, live provider,
FakeClaude apphost or production runner is needed. Keep the default Linux
`UseAppHost=false` behavior; do not invoke a broad test namespace or Unit suite.

Use the checkpoint tool per the current owner: one `run --plan
docs/superpowers/plans/2026-09-30-card-0845-build-slot-named-parameters-plan.md
--after S1-S3 --serial` per platform, then `wait` while it reports exit 75. Both
rows have their own isolated build; no build reuse or extra harness run is planned.
The CARD-0823 exceptions in the two other plans are task-specific and are not
silently inherited here. If the tool cannot run, report the actual blocker and
obtain an explicitly commissioned launcher exception; do not fix it in this card.

If the tool needs bootstrap, one **declared supporting build per platform** is
allowed: wrap `dotnet build tools/Antiphon.Checkpoints
--property:OutputPath=bin-c845-tool/ --nologo` with build-slot, adding
`--property:UseAppHost=false` on Linux. Await it and release its slot, then use
`dotnet run --no-build --project tools/Antiphon.Checkpoints
--property:OutputPath=bin-c845-tool/` (same Linux property) for run/wait commands.
Do not hold an outer wrapper lease throughout the tool's row execution.
Report a bootstrap explicitly; it is not a hidden CP or an excuse for other builds.

Capture full HEAD and `git status --porcelain=v1 --untracked-files=all` before and
after ordinary runs, with receipts outside tracked source. Keep source clean and
unchanged while running. Require exact rosters/counts below, not merely the Min
floor. Report CP-n lines with executed/passed/failed/skipped, commit, platform,
TRX, slot state, elapsed time and reruns. Exit 4 is a slot timeout with no work
admitted; never bypass it with NoSlot. Report any existing unleased fallback.

The filters enumerate every selected method with the owner's parenthesized
method-OR syntax. Static inspection of `PlanTableImporter.RosterTokens` confirms
that these operands also become the tool's expected name tokens. The `Expect`
cell's prose does not enforce exact counts or zero skips: compare the reported
executed roster with the census above and both V-5 names. This requires no
checkpoint-tool change and no discovery/test run during TestDesign.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | EstimatedMinutesWindows | Serial |
|---|---|---|---|---|---|---|---:|---:|---:|---|
| CP-1 | S1-S3 | `tests/Antiphon.Tests -> bin-c845-scripts/` | script-targets | `/*/Antiphon.Tests.Scripts/BuildSlotScriptTests/(C589_WrapperRunsUnderLease*)\|(C589_WrapperMaxCpuCountRules*)\|(C589_WrapperReleasesOnFailure*)\|(C589_WrapperTimeout*)\|(C589_WrapperUnreachableAtDeadline*)\|(C589_WrapperUnreachable*)\|(C589_WrapperAsciiOnly*)\|(Wrapper_renews_a_renew_mode_grant_while_the_command_runs*)\|(C800_WrapperPassesWildcardArgvLiterally*)\|(C800_WrapperStartsUnitFilterWithinDeadline*)\|(C800_WrapperForwardsScriptTokens*)\|(C800_WrapperLaunchesNativeExecutableLiterally*)\|(C845_WrapperBindsNamedScriptParameters*)\|(C845_WrapperPropagatesScriptExitCodes*)\|(C845_WrapperReleasesLeaseAfterScriptFailure*)\|(C845_WrapperPreservesScriptPathsAndValues*)` | V-1, V-2, V-3, V-4, R-1 | exactly the 16 methods in the static census, all passed; 0 failed/skipped on either OS | 16 | 9 | 12 | true |
| CP-2 | S1-S3 | `tests/Antiphon.SessionRunner.Tests -> bin-c845-broker/` | script-broker | `/*/Antiphon.SessionRunner.Tests/BuildSlotEndToEndTests/(Two_wrappers_on_a_budget_of_one_run_one_after_the_other*)\|(A_wrapper_killed_mid_hold_is_reaped_and_the_waiter_is_granted*)` | V-5 | exactly the 2 named V-5 methods, both passed; 0 failed/skipped on either OS | 2 | 4 | 6 | true |

### Cost

Estimated ordinary floor: **13 minutes on Linux**, **18 on Windows**; **31 minutes
combined**, four isolated row builds and **36 TUnit executions** (18 per OS).
All include their builds and bounded script/broker tests; cold restore, slot queue
time and optional bootstrap are additional. Supporting bootstrap allowance is
3 minutes Linux / 5 Windows, at most two additional builds when needed. These
are estimates, not durations measured by Plan. No runtime checkpoint has run here.

Allow 2-3 hours authoring, 45-60 minutes separate TestDesign and about 45 minutes
Review, plus scheduling of the second OS. Post-land Mutation is a separate
**12 isolated phase builds / 12 TUnit executions**, four method-scoped
baseline/red/green cycles on the inherited custody platform, estimated **72-108
minutes** including builds. It does not replace ordinary qualification on both
OSes. There is no claimed build-count saving from reuse; the brief requires one
isolated build per row. Avoiding full Unit/namespace runs bounds the ordinary work
to the wrapper's behavior and real broker composition.

Ordinary plus Mutation verification is **103-139 estimated minutes**, **16
isolated builds / 48 TUnit executions**, before optional bootstrap, queue/restore
time and authoring/Review. TestDesign adds no runtime verification spend.

## Acceptance and handoff

The direct script branch binds named values and uses literal child argv; all
expected process exits reach the caller; failures release the parent lease; spaced
paths work on both OSes; existing portable wrapper behavior remains green; the
real budget-one broker admits the waiting wrapper after completion/death. Both
broken checkpoint recipes are corrected without modifying checkpoint source.

TestDesign confirms the four fixture specifications against the actual wrapper,
slot/command shims and assertion harness; the exact 16/2 checkpoint roster; the
renamed method's four retained assertions; direct named V-5 wiring; and four
distinct faults with named observable failures. It corrects the standalone
floor's 35-versus-39 C589 accounting and pins 50 new PASS rows, without adding
TUnit executions beyond the planned four. The build-slot-only boundary and
direct self-leasing checkpoint recipes remain unchanged.

Code implements S1-S3, commits/pushes each slice, then runs only CP-1 and CP-2
on real Linux and Windows with the recorded exact rosters and clean source
receipts. Ordinary verification and all post-land PCs remain pending. The four
Linux probes above are historical Plan evidence, not TestDesign execution.
