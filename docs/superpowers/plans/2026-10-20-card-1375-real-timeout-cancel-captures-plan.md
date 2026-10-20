# CARD-1375: real Timeout/Cancelled captures and probe expiry witness

Base: `aafb284fc8d56dddf8e753ba847ee43bf269bcb7` (CARD-1325).
Target: master only; no activation or live Windmill work. Verification design is
included; next stage is Code. Planning performed source inspection only: no
builds, probes or tests were run.

## Ground truth

| Card/brief assumption | Inspected implementation | Consequence |
|---|---|---|
| Timeout and Cancelled have only reflected controls. | `scripts/fixtures/nightly/c1325-diagnostic-records.ps1`, E07/E08, construct populated state fields through `New-D13StateRecord`. The earlier V-3 runs all emitted Error. | Retain their provenance; add separate captures from the actual producer. |
| `[Timeout]` should produce Timeout. | Pinned TUnit simplifies console exceptions **before** classification. `TestFailedException` hides the timeout type from `FailureCategorizer`; `--detailed-stacktrace` bypasses that wrapper. | Use the flag and a real elapsed `[Timeout]`, not another copy of V-3's command. |
| Throwing `OperationCanceledException` should produce Cancelled. | `TestExecutor`/`TestCoordinator` normally mark caught exceptions Failed. Cancelled reporting is a separate state branch. The public `Execution.OverrideResult` API accepts Cancelled. | Use that supported state-selection API in the probe; identify the trigger honestly. An uncaught cancellation exception or Ctrl+C is not a reliable recipe for this state. |
| Reflected shapes match the producer's optional values. | TUnit creates Timeout with an explanation alone, and Cancelled with the parameterless constructor. MTP's state printers append null values as **empty text**. `nightly-coverage.ps1` currently requires a numeric Timeout suffix and accepts `<null>` or a shaped exception, not an empty exception field. | Expect a real parser red on the captured timeout message. Preserve the empty cancelled explanation as absent Message. |
| The base contains a reusable supervisor. | The tracked CARD-1325 report describes an **ignored**, Linux-only `run-probes.ps1`; no supervisor is in the base's tracked files. Its inspected SHA-256 is `9a903ba73bf477889b3b6436f1b000b51ed6e1bfa7861d3ff1bcd751b868e09c`. | Promote its small observation algorithm into tracked tooling in S1. Do not make Code depend on another task's surviving scratch directory. |
| The kill path has been witnessed. | V-3 receipts report normal exit, drained streams and zero observed descendants; none expired. The script uses a 30 s execution/EOF bound and a shared additional 5 s cleanup allowance. | S2 must start an actual process tree, force expiry and independently observe every process. |

Primary sources are pinned, not links to moving `main`:

- [TUnit 1.44.0 message publication and classification](https://github.com/thomhurst/TUnit/blob/42e3be6d99bb637d21e1dac711d76991a99e49c3/TUnit.Engine/TUnitMessageBus.cs):
  `Failed`, `SimplifyStacktrace`, `GetFailureStateProperty`, `Cancelled`.
- [Failure categorizer](https://github.com/thomhurst/TUnit/blob/42e3be6d99bb637d21e1dac711d76991a99e49c3/TUnit.Engine/Services/FailureCategorizer.cs),
  [timeout helper](https://github.com/thomhurst/TUnit/blob/42e3be6d99bb637d21e1dac711d76991a99e49c3/TUnit.Engine/Helpers/TimeoutHelper.cs),
  [coordinator](https://github.com/thomhurst/TUnit/blob/42e3be6d99bb637d21e1dac711d76991a99e49c3/TUnit.Engine/Services/TestExecution/TestCoordinator.cs),
  and [public result override implementation](https://github.com/thomhurst/TUnit/blob/42e3be6d99bb637d21e1dac711d76991a99e49c3/TUnit.Core/TestContext.Execution.cs).
- [MTP 2.2.2 state properties](https://github.com/microsoft/testfx/blob/3ee14aa6d97279c7b2d244657fafdd0c35f40242/src/Platform/Microsoft.Testing.Platform/Messages/TestNodeProperties.cs):
  Timeout prints Explanation, Exception, Timeout; Cancelled prints Explanation,
  Exception. [Outcome categories](https://github.com/microsoft/testfx/blob/3ee14aa6d97279c7b2d244657fafdd0c35f40242/src/Platform/Microsoft.Testing.Platform/Messages/TestNodeProperties.Categories.cs)
  and [TRX writer](https://github.com/microsoft/testfx/blob/3ee14aa6d97279c7b2d244657fafdd0c35f40242/src/Platform/Microsoft.Testing.Extensions.TrxReport/TrxReportEngine.cs)
  classify both as Failed. The prior raw diagnostic header identifies this exact
  MTP commit. Source-derived predictions below require confirmation by Code's captures.

## Decisions

**D-1 — Keep the pinned versions and use the real reporting pipeline.** Use a
small standalone net9.0 project pinned to TUnit 1.44.0 and MTP 2.2.2, with empty
local `Directory.Build.props`/`.targets` as in `c487-probe`. Verify resolved package
versions and native headers. No package upgrade, reflection, manual construction
of state properties, or string substitution can satisfy the capture requirement.

**D-2 — Use two explicit triggers.** `C1375Producer.TimeoutProbe` has
`[Test, Timeout(100)]` and a token-aware five-second delay; run with
`--detailed-stacktrace`. Classification requires the timeout category, non-null
test timeout and measured duration at least that timeout. Expected explanation:
`[Timeout] Test timed out after 100ms`.

`C1375Producer.CancelledProbe` starts a local cancellable wait, cancels its CTS,
awaits and catches that cancellation, then calls
`TestContext.Current!.Execution.OverrideResult(TestState.Cancelled,
"CARD-1375 controlled cancellation")`. Let the test return normally so TUnit's
coordinator publishes its own Cancelled node and MTP writes diagnostics/TRX.
This is a **real producer capture triggered by the public result override**;
it does not establish automatic classification of external cancellation.
Reject repeating the uncaught-local-token probe or relying on signal timing:
the pinned exception path does not select Cancelled. No reporter replacement is
needed.

**D-3 — Preserve the supervisor's bounds and expose only execution time.**
Extract `Invoke-NightlyProbeSupervisor` into
`scripts/lib/nightly-probe-supervisor.ps1`; its caller is
`scripts/capture-nightly-probe.ps1`. Use literal `ProcessStartInfo.ArgumentList`,
redirect both streams immediately, retain PID plus start-time identities, and
observe descendants through `/proc` on Linux. Completion requires root exit
and both EOFs. Default execution deadline remains 30,000 ms.

Add an optional elapsed-milliseconds provider for the execution loop, defaulting
to its real monotonic stopwatch. Cleanup always uses a separate **real** stopwatch:
kill the live root with `Kill(true)`, kill still-live identity-matched observed
descendants, then join every retained process and drain both streams using the
remaining portion of one 5,000 ms allowance. Include kill-call time in that
allowance; never give each process five seconds. Failed kill/join/drain refuses
further probes and retains evidence. Preserve cleanup in `finally`.

Reject a real 30-second sleep, a fake process adapter as the sole witness, or
considering the root's exit/kill acknowledgement proof of descendant exit.
This tool is local diagnostic tooling, not a new session custodian.

**D-4 — Pin selected input bytes and narrowly support their null fields.**
Use `scripts/fixtures/nightly/c1375-diagnostic-records.ps1` for two curated
terminal records, captured version-header lines, and their matching minimal TRX
result/definition inputs. Store exact selected bytes in base64 with independently
recorded SHA-256 and byte length; decode without trimming, newline conversion,
path replacement or rebuilding a state string. These are named parser inputs,
not copied whole run logs. Preserve raw diagnostics/TRX/receipts under ignored
`.antiphon/c1375-evidence/`; never force-add them.

Add a short `scripts/fixtures/nightly/c1375-provenance.md`: source/build SHA,
package/assembly versions, trigger, literal argv, exit, native counts, encoding,
raw-file hashes, byte offsets/lengths of each selected record and hashes of the
selected bytes. Store literal expected UID/class/method/message independently
of the parser. Mark any newline variants as derived; leave old reflected fixtures
labelled reflected.

Adjust only the validated failure-field boundary: admit the observed empty
Timeout value and empty exception value while retaining populated-duration and
`<null>` compatibility, unique-state/unique-delimiter requirements and malformed
input refusal. A cancelled record with no explanation has no Message property;
never invent an explanation from the override reason. TRX still owns the suite verdict.

**D-5 — Use the native Linux lane without a host pin.** Read-only
`GET /api/runner-defaults` (revision 2) and `GET /api/session-runners` were read
on 2026-10-20; an eligible Linux runner was available. Re-read at dispatch;
omit `-Runner`. Linux is needed for the inspected supervisor's `/proc` inventory,
so `-Platform Linux` is appropriate for these slices. No fleet address/path is
embedded in commands. No agent session or input-waiting lifecycle changes.

## Slices

**S1 — Captures and parser controls, budget 60 minutes (including P-1 and CP-1/2).**

Files: new `scripts/fixtures/nightly/c1375-probe/{Probe.csproj,Probe.cs,
Directory.Build.props,Directory.Build.targets}`, the two supervisor/capture
scripts in D-3, the two fixture/provenance files in D-4, and changes to
`scripts/lib/nightly-coverage.ps1`, `scripts/test-nightly-tests.ps1`,
`tests/Antiphon.Tests/Scripts/NightlyExecutionDiagnosticTests.cs` and the
CARD-1325 evidence-limitation paragraph in `docs/testing-and-build.md`.

Commit/push the capture tooling before P-1. Capture both nodes, then commit/push
their curated inputs and `D13_RealTimeoutCancelled` wrapper/harness case before
changing the parser. CP-1 must fail the exact timeout-message assertion against
the existing parser, not fail because a file or build is missing. Make D-4's
bounded parser fix, commit/push, then run CP-2 once. Preserve all six existing D13
methods and their 72 assertion rows. Do not rewrite historical V-3 outcomes.

**S2 — Deterministic native expiry witness, budget 45–60 minutes (including CP-3/4).**

Files: `scripts/lib/nightly-probe-supervisor.ps1`,
new `scripts/fixtures/nightly/c1375-owned-tree.ps1`,
new `scripts/test-nightly-probe-supervisor.ps1`, and new
`tests/Antiphon.Tests/Scripts/NightlyProbeSupervisorTests.cs`.
The fixture uses pwsh root/child/grandchild modes, so no extra helper compilation
is needed. Use the existing ScriptHarness owner and
`[ParallelLimiter<ProcessSpawnLimit>]`; require Linux rather than silently skip.

Implement V-2 below. The original kill algorithm need not be defective to need
a witness: for the explicitly requested red, commit a compiling, clearly labelled
control variant suppressing its root **and** descendant termination calls while
retaining joins and deadlines. CP-3 must detect live owned processes. Independent
fixture cleanup still runs. Restore real termination, commit/push, run CP-4.
This one bounded negative control is part of the requested acceptance cycle;
report it as intentional, not an inherited product failure or a completed broader
Mutation battery. The final tree must contain no kill bypass.

## Verification design

**P-1 — One bounded capture preparation, at the committed S1 tooling SHA.**
Reserve at most ten minutes including build/observation, within S1's budget.
This is the explicitly enumerated producer-observation exception to the ordinary
checkpoint rows. Do not retry alternative triggers until a desired label appears.

```powershell
pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1375-probe-build -SlotWaitMinutes 1 -- dotnet build scripts/fixtures/nightly/c1375-probe/Probe.csproj --property:OutputPath=bin-c1375-probe/ --property:UseAppHost=false --nologo
pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1375-probe-capture -SlotWaitMinutes 1 -- pwsh -NoProfile -File scripts/capture-nightly-probe.ps1 -ProbeDirectory scripts/fixtures/nightly/c1375-probe -ResultsDirectory .antiphon/c1375-evidence/producer
```

The driver must resolve and record the actual DLL produced by that build; never
guess a `net9.0` subdirectory. Invoke the built DLL with `dotnet exec`, sequentially,
using `/*/*/C1375Producer*/TimeoutProbe*` and
`/*/*/C1375Producer*/CancelledProbe*`. Each invocation supplies
`--detailed-stacktrace --report-trx --results-directory <fresh-method-directory>
--diagnostic --diagnostic-output-directory <fresh-method-directory>/diag`.
Use the normal 30 s / 5 s supervisor bounds, without the injected clock.

Require one matching native terminal node of the requested property type, one
Failed TRX result, zero passed/skipped results, recorded native nonzero exit
(normally 2), no supervisor expiry, successful joins and EOF. Validate raw state
independently of the parser under repair. Timeout/cancellation of the **process**
without a complete native record is not a captured test state. Missing/different
states or mismatching versions stop S1 with preserved evidence and the source
premise to investigate; do not label reflected or rewritten input captured.

**V-1 — Pinned parser evidence.** New non-parameterized
`NightlyExecutionDiagnosticTests.D13_RealTimeoutCancelled` runs ten named harness
rows, five for each captured state: (1) byte length/hash/round-trip match;
(2) literal native property type and expected identity in the input;
(3) exactly one parsed node with literal UID/class/method and Failed outcome;
(4) exact timeout Message or absent cancelled Message; (5) composed
`ConvertTo-NightlyNativeSuiteVerdict` with matching discovery/TRX gives complete
coverage, `testsPassed=false` and failed-row evidence. Fixture hashes detect
accidental edits; semantic assertions are the failure-detection oracle.

**R-1 — Boundary regressions.** CP-2 also runs the six existing D13 methods:
ErrorTerminal, ExecutionCompatibility, ExecutionGuards, StateClassification,
ExplanationBoundary and StatePropertyBoundary. Their quoted-state, ambiguous
delimiter, populated Timeout, UID, version and TRX checks remain unchanged.
Expected green roster: seven methods, 82 harness rows.

**V-2 — `NightlyProbeSupervisorTests.C1375_DeadlineKillsAndJoinsTree`.**

1. Launch the owned root, child and grandchild in a fresh nonce directory.
   Each publishes its PID/start identity and waits indefinitely without a
   self-exit timer. Keep stdout/stderr inherited and open. The harness obtains
   independent Process handles and verifies all three alive; the supervisor's
   observed-descendant inventory must include child and grandchild before expiry.
   Bound readiness by a real five seconds; failure is a fixture failure, not red.
2. The elapsed provider returns 0 until this ready/inventory handshake, then
   29,999 and 30,001 ms on consecutive loop observations. Assert no early
   termination at 29,999 and entry to the actual expiry path after 30,001.
   Do not advance or replace the cleanup stopwatch.
3. Assert the supervisor reports `expired=true`, root joined, two descendants
   joined, both streams successfully drained and cleanup success. Independently
   require each retained process handle to report exit and bounded successful
   `WaitForExit`; require the actual elapsed time from expiry through joins/EOF
   to be at most 5,000 ms. Assert the identity set, not just a descendant count.
   Read these observations before the outer fixture cleanup can kill anything.
4. A separate bounded outer watchdog/finally kills and joins only these owned
   identities on **every** outcome. Record if it was needed; a green witness
   requires it was unnecessary. A five-second join failure produces an explicit
   assertion failure and retained receipt before fallback cleanup; an outer
   timeout, zero selected tests or readiness failure is not the intended red.

The nine harness rows are: ready identities; no early expiry; expired;
root exited/joined; child exited/joined; grandchild exited/joined; both EOFs;
shared cleanup elapsed bound; no fallback needed. The red variant must fail the
exit/join observations, even if the supervisor fabricates a success flag.

**Execution/report contract.** Each row below uses the **native Linux lane**;
the `linux-` Group prefix names it without adding an unsupported table column.
Run only the selected row after its committed/pushed slice, through
`Antiphon.Checkpoints run --plan <this-plan> --rows CP-n
--expected-source-sha <full-sha>`; wait until terminal, including after exit 75.
One source-bound checkpoint-tool setup build is allowed, through
`scripts/build-slot.ps1` with `--property:OutputPath=bin-c1375-tool/`.
Use `dotnet exec` on that built tool to avoid an unleased implicit build.
Rows take their own slots; do not nest a second slot around them.

Commit before each run. Report unedited CHECKPOINT lines, exact source/build
binding, all counts, intentional red assertions and P-1 native results separately.
Validate green receipts. Code/Review run `scripts/check-evidence-diff.ps1` over
base..HEAD. Remove task-owned alternate outputs after their processes finish;
keep generated evidence ignored. Unexpected failures require a stated targeted
follow-up; no full assembly sweep or live nightly run is commissioned.
Ordinary checkpoint floor is 18 minutes; P-1 adds at most ten, included in the
105–120 minute total Code authoring/verification budget.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1-red | `tests/Antiphon.Tests -> bin-c1375-s1-red/` | linux-capture-red | `/*/*/NightlyExecutionDiagnosticTests*/D13_RealTimeoutCancelled*` | V-1 | exactly 1 executed, 1 failed, 0 skipped; captured timeout exact-message assertion | 1 | 4 |
| CP-2 | S1 | `tests/Antiphon.Tests -> bin-c1375-s1-green/` | linux-capture-green | `/*/*/NightlyExecutionDiagnosticTests*/*` | V-1, R-1 | exactly 7 executed, 7 passed, 0 failed/skipped; 82 harness rows | 7 | 5 |
| CP-3 | S2-red | `tests/Antiphon.Tests -> bin-c1375-s2-red/` | linux-expiry-red | `/*/*/NightlyProbeSupervisorTests*/C1375_DeadlineKillsAndJoinsTree*` | V-2 | exactly 1 executed, 1 failed, 0 skipped; live-process exit/join assertions under intentional kill suppression | 1 | 4 |
| CP-4 | S2 | `tests/Antiphon.Tests -> bin-c1375-s2-green/` | linux-expiry-green | `/*/*/NightlyProbeSupervisorTests*/C1375_DeadlineKillsAndJoinsTree*` | V-2 | exactly 1 executed, 1 passed, 0 failed/skipped; 9 harness rows, no fallback | 1 | 5 |
