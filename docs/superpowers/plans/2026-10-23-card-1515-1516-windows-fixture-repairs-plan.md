# CARD-1515 / CARD-1516: owned PTY cleanup and effort-fixture readiness

Plan owner: task `08ec1369`, 2026-10-23. Source inspected:
`1c955459a3ef39981b459cbcc12d8f656551173d` (assigned base and fetched
`origin/master`). Both board records were read in full with `scripts/card.ps1 get
CARD-1515 -Board Antiphon` and the equivalent CARD-1516 command.

Repair the fixtures without weakening disposal, effort-selection, or readiness
assertions. Publish this plan as documentation through its own landing owner.
TestDesign remains a separate stage: the brief requests a checkpoint proposal but
does not explicitly fold TestDesign into Plan. The proposal below is a closed
execution envelope for that stage to freeze, not a claim of completed verification.

## Ground truth

The [investigation on master](../../investigations/2026-10-22-rc-group-3-reds-debug.md)
was inspected and is unchanged at the assigned base. Its original report commit is
`93c015715a886c2626f6948aad14d3a1797417f2`.

| Card/brief assumption | What the inspected code and evidence establish | Design consequence |
|---|---|---|
| Disposal idempotence failed. | `DirectSessionRunnerClientDisposalTests.Dispose_is_idempotent` passes dispose-core-once, shared completion task, null exception, and independent process-exit assertions before `OwnedPtyFixture.DeleteRoot` calls `Directory.Delete(Root, true)` once. | Keep every assertion; establish a separate filesystem-release boundary after process exit. |
| An owned process or scanner held OpenConsole. | Host, child, and console witnesses were exited. The exact original holder is unproven. The remaining private shadow image was Archive, not ReadOnly. `Witness.Release()` already precedes deletion. | Capture actual holder evidence on a new denial; do not blame a process name, change ACLs/attributes, or kill an observed foreign holder. |
| Fixture disposal always removes scratch. | `OwnedPtyFixture.DisposeAsync` disposes adapter, client, and witness but never deletes `Root`; a body failure can bypass explicit `DeleteRoot`. `TestOwnedPtyHost.ScopeAsync` already preserves primary and cleanup exceptions. | Add root cleanup to the owned finalizer and reuse the existing scope helper for exception preservation. |
| The effort value was wrong / CARD-1481 caused the red. | The screen already said xhigh; readiness returned false before the JSON effort assertion. The test constructs its own PowerShell launch spec and bypasses role-to-argv policy. | Keep high/xhigh and wrong-highlight navigation; change fixture/test code only. |
| Readiness currently sleeps once instead of polling. | `ComposerInputProbe.RunAsync` already polls rendered screen state and retries Ctrl+U. The local test borrows runner-fake settings: 25 ms polling, 1,000 ms retype, and a 500 ms clear sub-budget. `ClaudeStartupReadiness` also has a 12,000 ms post-floor deadline. | Reuse existing polling with a local-fixture clear allowance. Do not add a second production readiness algorithm or reinterpret false as ready. |
| The 16 s delay proves screen clearance. | `Task.Delay(16000)` in `RunAsync` is an independent completion watchdog. After readiness succeeds, JSON is read once without waiting for a coherent final trace. | Keep the watchdog, distinguish screen proof from trace publication, and poll both required observations within a bounded test deadline. |
| Key tracing is passive. | `claude-effort-dialog.ps1` serializes the entire growing key list and rewrites JSON before painting on every key. | Retain the in-memory oracle; publish only semantic snapshots after paint. |
| Failures retain sufficient evidence. | The success/stuck helper omits `StartupLogger` and deletes `trace.json` in finally. Another method in the same class already uses that logger. | Attach the logger and retain trace, raw/rendered output, and exception/cleanup evidence outside scratch before removal. |
| CPU/memory starvation is proven causal, and the earlier rc was fully green. | The brief reports 99.98% desktop CPU; the investigation establishes an earlier green group, not a complete green qualification. No incident-time memory measurement or original handle capture proves causality. | Use deterministic delayed/released/held controls, not a load soak or historical-pass substitution. |

## Decisions

- **D-1 — Keep the repair inside test fixtures.** No edits to `server/`, `src/`,
  production timeouts, linger policy, adapters, settings defaults, release scripts,
  or CARD-1481 policy. Rejected: changing production disposal/readiness to compensate
  for fixture I/O. The current product contracts are not disproved by either red.
- **D-2 — Retry only the owned root deletion, with a real release observation.**
  Snapshot the fixture's exact root, session and witness PID/start/image records
  before releasing observer handles. The existing independent exit assertions stay
  before successful-path deletion. Replace `DeleteRoot` with awaited
  `DeleteRootAsync`; attempt immediately, then poll by attempting that same owned
  deletion at 50 ms intervals for at most five seconds. Success means the private
  root is absent; a timer firing or a process exit alone is insufficient. Keep one
  monotonic deadline, including all retry attempts, with a cancellation token.
  Attribute the refusal to the captured file with a direct delete attempt on that
  path; a basename in an exception alone cannot select a file elsewhere. Once that
  image is released/deleted, finish deleting the same private tree. If the refused
  resource cannot be attributed, surface the error rather than widening the retry.
  Restrict retries to Windows sharing/lock violations (Win32 32/33) and access
  denial (5) for the captured private OpenConsole image with unchanged, non-readonly
  attributes. Access denial is a retry candidate, never proof of a lock. Other
  errors propagate immediately. Persistent candidate errors still throw, retaining
  first/last exceptions, HResults, elapsed time, attempts and exact path. Rejected:
  blanket exception retries, arbitrary sleeps, attribute/ACL repair, full-test
  retries, and deletion outside the already owned tree.
- **D-3 — Diagnose the first denial while the holder may still exist.** Add a
  fixture-only Windows Restart Manager query of exact captured OpenConsole file
  paths (not a directory). Include PID, start time and application/service name;
  enrich executable path only when readable and generation still matches. An empty,
  partial, failed or timed-out query is explicitly unknown, never no-lock proof.
  Record attributes and owned witness identities even when native diagnosis fails.
  Register once, bound list resizing, and always end the Restart Manager session.
  Run the native query in a locally inherited diagnostic child, using the existing
  `WorktreeLockChild` launch/identity pattern, with five seconds for the query and
  two seconds to terminate/join that exact child if necessary. Start the five-second
  deletion retry budget after this single diagnostic attempt: the added failure-path
  allowance is at most 12 seconds apart from OS syscall scheduling. No diagnostic
  work runs on an immediate-success delete. No unjoined Task.Run around native I/O.
  Rejected: requiring a configured/elevated Sysinternals installation, calling
  Restart Manager shutdown/restart, or scanning/killing processes by name.
- **D-4 — Make final cleanup unconditional and preserve primary failures.** Route
  owned-fixture call sites through `TestOwnedPtyHost.ScopeAsync` (including failed
  startup), so adapter/client/witness disposal and root deletion are all attempted.
  If the body alone fails, rethrow that original exception; if cleanup also fails,
  preserve both original exceptions through the existing aggregate contract. Do
  not let diagnostic-copy errors replace either. Memoize the root-cleanup task so
  an explicit delete and finalizer share one outcome/budget; an absent root is a
  no-op. A failed delete remains a reported failure and is not silently rerun with
  a fresh deadline by the finalizer. Preserve existing process-generation checks
  and emergency cleanup; do not alter `TestOwnedPtyHost` process semantics.
- **D-5 — Move trace publication out of the key hot path.** Keep key collection,
  effort state and composer state in memory for every key. Paint promptly; publish
  coherent JSON after Enter (including a stuck Enter) and after Ctrl+U processing.
  Do not serialize/write after ordinary j/token characters. Publish through a
  sibling temporary file and replacement, with an increasing snapshot sequence and
  phase; retain applied/highlight/keys/composer/pid fields. Both real console input
  and a finite scripted-key test driver must call the same key-processing function.
  Rejected: dropping keys, a periodic disk timer/background writer, and accepting
  partial or stale JSON as final evidence.
- **D-6 — Give only the real local fixture a measured clearance window.** Create
  local settings in `ClaudeAdapterEffortPromptTests`; retain 25 ms polling, 1,000 ms
  retype, 12,000 ms startup budget and the independent 16,000 ms test watchdog.
  Set only this fixture's clear sub-budget to 3,000 ms. Leave
  `RunnerClaudeAdapterEffortPromptTests.Settings()` and all production defaults
  unchanged. This is an explicit test-budget change qualified by a 750 ms delayed
  clear (beyond the former 500 ms) and a never-clear control. It does not extend the
  coordinator's 12 s deadline. Readiness must still return true through the real
  adapter. Then poll rendered state and the latest coherent clear-phase trace at
  25 ms until both show an empty composer, using the remainder of the original 16 s
  test deadline; never restart readiness or reset that deadline. An early false
  remains a failure. Rejected: an unconditional three-second delay, a longer
  production timeout, and post-hoc success because the screen eventually cleared.
- **D-7 — Retain failure evidence before stopping/deleting.** Attach the existing
  `StartupLogger` to this adapter. On assertion, watchdog, cancellation, or teardown
  failure, capture the exception, requested effort, session ID, probe token,
  elapsed time, structured logger entries, raw output, rendered screen and latest
  complete trace. Copy to a unique result artifact under the current
  `--results-directory`, outside scratch; use the existing `FullCommandCounter`
  artifact-location/`AttachArtifact` pattern without coupling to SQL. When trace is
  missing/unreadable, retain that fact and the other evidence. Capture terminal
  state before kill, and append cleanup outcome afterward. Do not print environment
  variables. The record survives scratch deletion; JSON/log/TRX output stays ignored.
- **D-8 — Separate design completion from native qualification.** These are
  engineering choices within the requested repair, with no outstanding product
  decision. TestDesign freezes the verification details below. Linux can establish
  helper policy and script state transitions; it cannot establish Windows image
  sharing, Restart Manager attribution, or ModernConPty readiness. No claim of a
  repaired Windows gate is allowed until the Windows-owed rows execute.

Restart Manager's file registration, returned process identities and error handling
are described by Microsoft's [RmRegisterResources](https://learn.microsoft.com/en-us/windows/win32/api/restartmanager/nf-restartmanager-rmregisterresources),
[RmGetList](https://learn.microsoft.com/en-us/windows/win32/api/restartmanager/nf-restartmanager-rmgetlist),
and [RM_PROCESS_INFO](https://learn.microsoft.com/en-us/windows/win32/api/restartmanager/ns-restartmanager-rm_process_info).
Its observation is diagnostic, not authorization to control a holder.

## Slices and file ownership

### S1 — Commit executable failing witnesses and mechanical seams

Add controls before the fixes and push the compiling red-test commit. Mechanical
extraction must retain one-shot deletion, per-key trace publication and the local
500 ms setting so the failures remain reachable. No skipped or deliberately throwing
stub can substitute for exercising these paths.

| File | Intended change / named test |
|---|---|
| `tests/Antiphon.Tests/Agents/DirectSessionRunnerClientDisposalTests.cs` | Preserve existing assertions and add an opt-in, test-process-only held-image control for `Dispose_is_idempotent`. Capture the real private console image before `Witness.Release`; acquire an independent no-delete-share holder after exit witnesses pass. |
| `tests/Antiphon.Tests/TestHelpers/OwnedPtyFixtureCleanup.cs` (new) | Small concrete deletion/time/diagnostic seam used by the actual fixture. Initial extraction remains one-shot; no generic repository cleanup service. |
| `tests/Antiphon.Tests/Agents/OwnedPtyFixtureCleanupTests.cs` (new) | `Delete_observes_release_and_preserves_failures`, four explicit argument cases: transient, persistent, nonretryable, body-and-cleanup. Use fake time for policy and an actual owned temp tree for deletion postconditions. |
| `tests/Antiphon.Tests/Agents/OwnedPtyFixtureCleanupWindowsTests.cs` (new) | `Refusal_names_holder_and_never_stops_it`, two argument cases: release, held. Use `WorktreeLockChild` file mode and independently retained PID/start witness. |
| `tests/Antiphon.Tests/Agents/Fixtures/claude-effort-dialog.ps1` | Extract a callable key-processing function without changing publication frequency. Add fixture-only delayed/never-clear controls and a definitions-only entry for finite scripted input; default remains the real ReadKey loop. |
| `tests/Antiphon.Tests/Agents/Fixtures/claude-effort-trace-contract.ps1` (new) | Finite scripted-key driver for the same function. Observe trace bytes/publication boundary and paint ordering; use the existing ScriptHarness PASS/exit contract. |
| `tests/Antiphon.Tests/Agents/ClaudeEffortFixtureTraceTests.cs` (new) | `Trace_publishes_only_coherent_semantic_snapshots`, one TUnit result invoking the finite script through the existing owned `ScriptHarness`. |
| `tests/Antiphon.Tests/Agents/ClaudeAdapterEffortPromptTests.cs` | Preserve the two original high/xhigh arguments; allow the 750 ms clear injection. Add `Local_clear_failure_retains_evidence_before_scratch_cleanup`, one negative-path result. |

Native holder readiness is acknowledged, not inferred from a delay. The transient
holder is released only after the real first delete denial has been observed and
its holder snapshot captured; the held case releases only in test finally. A
fixture-side callback around the actual delete attempt can acknowledge denial in
both the one-shot and repaired versions. The callback must not delete, retry, or
pretend that the held handle has closed. Separate watchdogs must terminate and
join only test-owned helpers on assertion failure.
Every new class that launches pwsh or a diagnostic/holder child carries
`[ParallelLimiter<ProcessSpawnLimit>]`; the local adapter class retains
`[NotInParallel]` because it changes the process environment. Row serialization
does not replace these assembly-local safeguards.

### S2 — Repair owned-root teardown

Implement D-2 through D-4 in `OwnedPtyFixtureCleanup.cs` and
`DirectSessionRunnerClientDisposalTests.cs`. Add
`tests/Antiphon.Tests/TestHelpers/WindowsFileLockOwners.cs` for the native diagnostic
and its locally inherited worker entry. Reuse `WorktreeLockChild` unchanged for
held-file witnesses; do not alter production lock diagnostics. Retain process
record values before handles are disposed. Update all direct `DeleteRoot` call
sites to await the same bounded cleanup task. Route body and cleanup through the
existing `ScopeAsync`, including fixture initialization failures.

### S3 — Publish trace at semantic boundaries

Implement D-5 in `claude-effort-dialog.ps1`. Keep the real loop and scripted driver
on the same state transition and publisher. The trace test types j, Enter, the
complete token and Ctrl+U; after each ordinary character it checks the prior trace
has not changed, and at each semantic event it checks the new exact state/key
prefix. Observe paint before publication via the shared script's output/sink
boundary. No elapsed-time performance assertion replaces the absence of hot-path
disk writes. Keep fixture PowerShell ASCII.

### S4 — Wait for coherent local completion and retain evidence

Implement D-6/D-7 in `ClaudeAdapterEffortPromptTests.cs`; add a small
`tests/Antiphon.Tests/TestHelpers/EffortFixtureEvidence.cs` only if needed for the
failure artifact and scope cleanup. Retain the exact high/xhigh, j then Enter,
final Ctrl+U, empty composer, readiness true and null `LaunchBlock` assertions.
The stuck-effort test still requires `EffortDialogNotCleared` and null applied
effort. The never-clear control must select xhigh successfully, keep the probe on
screen, produce readiness false with `NeverCleared`, and leave `LaunchBlock` null.
Never turn that false into a passing readiness result; the enclosing regression
test passes by asserting the failure and its retained diagnostics.

Commit/push S2, S3 and S4 individually, then freeze source for the narrow green
rows. No build/test runs are part of this Plan task. No change to the test project
file is expected: its existing fixture glob stages the new PowerShell file.

## Verification proposal for TestDesign

### Inspection and decisive oracles

Bodies inspected: both named adapter test classes, the PowerShell fixture,
`DirectSessionRunnerClientDisposalTests` including `OwnedPtyWitness` and
`OwnedPtyFixture`, `TestOwnedPtyHost.ScopeAsync` and its failure-preservation test,
`ComposerInputProbe.RunAsync`, `ClaudeStartupReadiness.RunAsync`, local
`ClaudeAdapter.WaitForReadyAsync`, `WorktreeLockChild`, existing native sharing
tests, `ScriptHarness`, and result artifact attachment in `FullCommandCounter`.

| ID | Named method and result count | Required observation / red witness |
|---|---|---|
| V-1 / R-1 | `OwnedPtyFixtureCleanupTests.Delete_observes_release_and_preserves_failures` (4) | Transient denial then real deletion succeeds; permanent denial exhausts one fake-time budget and remains an error with original HResult; nonretryable error propagates without wait; simultaneous body/cleanup errors retain both originals and cleanup runs once. Red extraction fails release/diagnostic/final-cleanup assertions, not a timeout or compile. |
| V-2 / R-2 | `ClaudeEffortFixtureTraceTests.Trace_publishes_only_coherent_semantic_snapshots` (1) | No file publication for ordinary keys; Enter and Ctrl+U publish coherent state after paint, preserving every key. Original per-key write makes the unchanged-trace assertion red. Script PASS lines are internal assertions, not extra TUnit results. |
| V-3 / R-3 | `DirectSessionRunnerClientDisposalTests.Dispose_is_idempotent` (1) | With the opt-in real image holder, first deletion is denied after all original process/idempotence assertions. One-shot code fails; bounded repair waits for acknowledged holder release and actually removes the root. Retain package hashes and every independent generation witness. |
| V-4 / R-4 | `OwnedPtyFixtureCleanupWindowsTests.Refusal_names_holder_and_never_stops_it` (2) | Query names the independently observed holder PID/start and file. Release case deletes; held case reaches bounded failure with root still present, original native code, holder still alive, no control operation against it. Finally releases/joins it and removes owned scratch. Original code lacks the diagnostic/release behavior. |
| V-5 / R-5 | `ClaudeAdapterEffortPromptTests.Local_adapter_keeps_requested_effort_and_probes_after_clearance` (2) | Inject 750 ms delay after receipt of Ctrl+U but before applying/painting clear. Both effort rows keep all original assertions. The original 500 ms setting returns false; the local 3 s polling window succeeds before the unchanged watchdog. Record receipt/paint times to prove the delay was reached. |
| V-6 / R-6 | `ClaudeAdapterEffortPromptTests.Local_clear_failure_retains_evidence_before_scratch_cleanup` (1) | Never-clear injection yields false and `NeverCleared` after correct effort selection; capture raw/rendered/trace/logger before kill. After scratch removal, attached artifact still contains the nonce/token, requested/applied effort, retained composer and original assertion error. Missing evidence fails old code. |
| R-7 | `ClaudeAdapterEffortPromptTests.Local_adapter_names_an_uncleared_effort_dialog` (1) | Existing stuck-dialog behavior and null applied effort remain unchanged; cannot be confused with the never-clear probe case. |
| R-8 | `DirectSessionRunnerClientDisposalTests.Body_failure_still_reaps_the_owned_host` (1) | Existing original sentinel and independent exit assertions remain; final owned-root removal must also occur. |

TestDesign must freeze labels, diagnostic worker failure handling, retry
classification variants, cancellation/ownership guards and one-to-one guard/PC
mapping. Subsequent Mutation controls are method-scoped and separate from these
ordinary pre-fix witnesses. They must include bypassed root cleanup/deadline,
swallowed permanent refusal, PID-only diagnostic attribution, skipped terminal
evidence, readiness accepted before empty screen, and restored per-key disk writes.
Do not widen ordinary execution to a class or suite to cover missing design work.

This plan changes no product delivery path or session that waits for user input.
The console fixture is owned and killed/joined in test finally; it is not a parked
delegate. No session-release deadline is introduced (parking remains disabled by
default under CARD-1083).

### Lanes, source binding and run rules

At 2026-10-23 00:09 UTC, `GET /api/runner-defaults` returned revision 2, no
per-kind overrides or unresolved references. `GET /api/session-runners` showed
eligible Linux and Windows lanes and one unavailable/draining entry. This is a
dated observation, not a reservation. Resolve both again at dispatch; embed no
fleet address. Portable work can use the current Linux lane (server2 in the brief).
Omit `-Runner` and omit `-Platform` for portable dispatch. Use `-Platform Windows`
only for the native rows; `-Platform Any` removes an inherited pin.

- **Linux/portable:** CP-1 through CP-4. These prove policy and actual PowerShell
  fixture transitions without a Windows console. They do not certify native locks
  or replace the local adapter's Windows evidence.
- **Windows-owed:** CP-5 through CP-14, only in a separately commissioned lane
  after the protected qualification permits it. This task does not touch the live
  qualification or `C:\Antiphon\releases`, including its logs and outputs. Use a
  separate owned worktree, scratch, result and build output. Do not launch these
  native methods on Linux and count skips as proof.
- Red rows run on the immutable S1 red-test commit; green rows run on the final
  S2-S4 candidate. Windows red evidence may be collected later from a separately
  owned checkout at S1; never reset/rebase the assigned branch to recover it. The
  caller must commission that exact-source verification explicitly. Bind all final
  green receipts to the same final candidate SHA on both lanes; earlier-source
  green and historical rc results do not fill those obligations.
- One committed slice-group selection per checkpoint invocation. Red rows expect
  real assertion failures and exit 1, which the runner will report as red; its
  `Expect` text does not turn a failed receipt green. Capture the exact expected
  failing assertion. A build error, missing helper, zero results, fixture timeout
  or wrong OS is not a successful red witness. Negative-path green tests assert
  exceptions/false outcomes; they do not swallow persistent failure.
- Every row is serial and takes the host build slot. Preserve unedited CHECKPOINT
  lines, source/build binding, executed/passed/failed/skipped counts and attachments.
  Commit before runs; source stays frozen while running. Run the checkpoint tool,
  and continue foreground `wait` until it finishes (exit 75 is still running).
  Report slot timeout as not run. Remove only owned alternate output after evidence
  retention. No whole-suite run, production runner, load soak or retry-until-green.
- The table is closed. A failing assertion not specified by the row gets diagnosis
  and, if claimed inherited, a method-scoped base check; record the reason/cost of
  that extra run. Do not silently add runs. Shared builds below are only reused
  within the same source group and OS lane, never across red/green or Linux/Windows.

After TestDesign freeze, the execution shape is:

```text
<gated checkpoint tool> run --plan docs/superpowers/plans/2026-10-23-card-1515-1516-windows-fixture-repairs-plan.md --rows <explicit-lane-and-phase-rows> --expected-source-sha <full-phase-sha>
<gated checkpoint tool> wait --run <printed-run-id> --max-wait 570s
```

Use the repository's checkpoint/build-slot front door. If the tool needs a
bootstrap build, name and account for that single infrastructure build through
`scripts/build-slot.ps1`; do not nest slot owners. Code/Review also run
`scripts/check-evidence-diff.ps1` over their recorded base..pushed tip.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---|---|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1515-portable-red/` | linux-delete-red | `/*/*/OwnedPtyFixtureCleanupTests/Delete_observes_release_and_preserves_failures` | V-1, R-1 | 4 executed, expected assertion red per R-1, 0 skipped | 4 | 6 | true | n/a |
| CP-2 | S2-S4 | `tests/Antiphon.Tests -> bin-c1515-portable-green/` | linux-delete-green | `/*/*/OwnedPtyFixtureCleanupTests/Delete_observes_release_and_preserves_failures` | V-1, R-1 | 4 passed, 0 failed/skipped | 4 | 6 | true | n/a |
| CP-3 | S1 | CP-1 | linux-trace-red | `/*/*/ClaudeEffortFixtureTraceTests/Trace_publishes_only_coherent_semantic_snapshots` | V-2, R-2 | 1 executed, expected unchanged-trace assertion red, 0 skipped | 1 | 1 | true | n/a |
| CP-4 | S2-S4 | CP-2 | linux-trace-green | `/*/*/ClaudeEffortFixtureTraceTests/Trace_publishes_only_coherent_semantic_snapshots` | V-2, R-2 | 1 passed, 0 failed/skipped | 1 | 1 | true | n/a |
| CP-5 | S1 | `tests/Antiphon.Tests -> bin-c1515-windows-red/` | windows-owed-dispose-red | `/*/*/DirectSessionRunnerClientDisposalTests/Dispose_is_idempotent` | V-3, R-3 | 1 executed, expected one-shot deletion red after exit assertions, 0 skipped | 1 | 8 | true | C1515_HOLD_SHADOW_IMAGE=until-denial |
| CP-6 | S2-S4 | `tests/Antiphon.Tests -> bin-c1515-windows-green/` | windows-owed-dispose-green | `/*/*/DirectSessionRunnerClientDisposalTests/Dispose_is_idempotent` | V-3, R-3 | 1 passed, 0 failed/skipped; held image released and root deleted | 1 | 8 | true | C1515_HOLD_SHADOW_IMAGE=until-denial |
| CP-7 | S1 | CP-5 | windows-owed-holder-red | `/*/*/OwnedPtyFixtureCleanupWindowsTests/Refusal_names_holder_and_never_stops_it` | V-4, R-4 | 2 executed, expected holder/release assertions red, 0 skipped | 2 | 1 | true | n/a |
| CP-8 | S2-S4 | CP-6 | windows-owed-holder-green | `/*/*/OwnedPtyFixtureCleanupWindowsTests/Refusal_names_holder_and_never_stops_it` | V-4, R-4 | 2 passed, 0 failed/skipped; held case asserts permanent error | 2 | 1 | true | n/a |
| CP-9 | S1 | CP-5 | windows-owed-clear-red | `/*/*/ClaudeAdapterEffortPromptTests/Local_adapter_keeps_requested_effort_and_probes_after_clearance` | V-5, R-5 | 2 executed, both readiness assertions red after 750 ms clear control reached, 0 skipped | 2 | 1 | true | C1516_CLEAR_DELAY_MS=750 |
| CP-10 | S2-S4 | CP-6 | windows-owed-clear-green | `/*/*/ClaudeAdapterEffortPromptTests/Local_adapter_keeps_requested_effort_and_probes_after_clearance` | V-5, R-5 | 2 passed, 0 failed/skipped; all original assertions retained | 2 | 1 | true | C1516_CLEAR_DELAY_MS=750 |
| CP-11 | S1 | CP-5 | windows-owed-evidence-red | `/*/*/ClaudeAdapterEffortPromptTests/Local_clear_failure_retains_evidence_before_scratch_cleanup` | V-6, R-6 | 1 executed, expected retained-evidence assertion red, 0 skipped | 1 | 1 | true | n/a |
| CP-12 | S2-S4 | CP-6 | windows-owed-evidence-green | `/*/*/ClaudeAdapterEffortPromptTests/Local_clear_failure_retains_evidence_before_scratch_cleanup` | V-6, R-6 | 1 passed, 0 failed/skipped; readiness remains false in controlled failure | 1 | 1 | true | n/a |
| CP-13 | S2-S4 | CP-6 | windows-owed-stuck-regression | `/*/*/ClaudeAdapterEffortPromptTests/Local_adapter_names_an_uncleared_effort_dialog` | R-7 | 1 passed, 0 failed/skipped | 1 | 1 | true | n/a |
| CP-14 | S2-S4 | CP-6 | windows-owed-body-regression | `/*/*/DirectSessionRunnerClientDisposalTests/Body_failure_still_reaps_the_owned_host` | R-8 | 1 passed, 0 failed/skipped | 1 | 1 | true | n/a |

### Cost and handoff limits

Estimated ordinary execution floor: **38 minutes** plus tool bootstrap if needed,
slot wait and authoring: 14 minutes portable, 24 minutes Windows-owed. This is four
isolated builds shared by 14 exact-method rows, with 13 final-green TUnit results
and 11 pre-fix executions (including both parameterized effort cases). No execution
counts are claimed today. Post-land PC cost/recipes belong to the separate
TestDesign freeze; they are not silently included or claimed complete. Avoiding
either 25-minute whole-assembly run and reusing each lane/phase build keeps the
verification proportional to these fixture repairs.

No server deployment/restart is required: repaired behavior starts with the next
test assembly/fixture build. Windows verification remains incomplete until every
owed row has genuine native evidence. Any need to change production probe or
process-lifetime behavior returns to investigation with the exact controlled
failure, timestamps and retained screen/holder evidence; it is outside this plan.

--- next stage ---
next: test-design
handoff: Freeze CARD-1515/1516 fixture-only verification, guard controls and exact red/green roster; retain the closed checkpoint envelope and Windows-owed lane. Preserve every existing assertion, production timeouts and the protected qualification. Then commission Code.
artifact: docs/superpowers/plans/2026-10-23-card-1515-1516-windows-fixture-repairs-plan.md
