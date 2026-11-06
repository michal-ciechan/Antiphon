# CARD-1731: publication-ledger timeout investigation

The three requested methods reproduced their 300-second timeouts at unchanged master. Repeated snapshot restoration and evidence-path filesystem checks dominate the measured work. Git fixture creation, TRX parsing, EF startup and an actual nightly execution do not explain the four-to-five minutes. No product or test source was changed; the repair belongs to Code.

## Source, scope and result

- Task/branch: `b8770621`, `feat/card-task-b8770621`.
- Tested source: `0dbd8295a034f8d88f5b98d1e29f14ac82c1890a`, the brief's master base. A separate detached worktree at `/work/worktrees/task-b8770621-measure` held the native build and run. Its tracked tree stayed clean.
- Host: runner container `dfcc116be984`, Linux/Debian 12, 24 available CPUs; .NET 9.0.22, TUnit 1.44.0, MTP 2.2.2. Nothing ran on the desktop.
- Exactly one invocation selected the three native UIDs below, under one granted execution lease. They ran serially through the assembly-local `ProcessSpawnLimit`. No assembly sweep, retry of a method, timeout increase or assertion change occurred.
- Native result: **3 executed, 0 passed, 3 failed, 0 skipped; exit 2**. All failures were the existing `ScriptHarness` execution timeout. MTP records these as failed tests, not its separate timeout counter.

| Method in `ReleaseGatePublicationAuthorityTests` | UTC start | TRX seconds | Result |
|---|---|---:|---|
| `C599A_ExecutionLedgerJoins` | 00:18:56.3546203 | 300.1528668 | execution timeout |
| `C599A_ExecutionLedger` | 00:23:56.5750768 | 300.0818396 | execution timeout |
| `C599A_ExecutionLedgerMarkers` | 00:28:56.6608495 | 300.0482370 | execution timeout |

All dates in this measurement are 2026-11-06 UTC. MTP reported 15m05.650s for the run. The gated wrapper's measured wall time, including admission, was 1016.685s. These are different envelopes, not contradictory test durations.

Admission and compilation are separate from test execution:

```text
BUILD SLOT granted lease=a19817fc-add9-4513-90d4-1388f8f9ece5 waited=196s maxcpucount=6
native build: 1728 warnings, 0 errors; Time Elapsed 00:02:26.84
BUILD SLOT released lease=a19817fc-add9-4513-90d4-1388f8f9ece5 held=148s

BUILD SLOT granted lease=380d235d-1afe-42b4-83db-039821091223 waited=105s maxcpucount=6
BUILD SLOT released lease=380d235d-1afe-42b4-83db-039821091223 held=911s
```

The build was the single native incremental compile requested by the brief, with `UseAppHost=false`, no alternate output, and a fresh native output tree. The execution used the same property and `--no-build`. The 18-minute overall run limit and 12-minute slot wait were finite and did not fire. The 105-second broker wait did not consume a method's 300-second execution budget. Subsequent methods waited for the first method's serial seat; their TRX start times show that this wait was outside their own measured bodies.

## What the fixture actually does

The C# methods in `tests/Antiphon.Tests/Scripts/ReleaseGatePublicationAuthorityTests.cs` call `ScriptHarness`, which launches one owned PowerShell child per method. Its real runtime budget is 300s in `ScriptHarnessProcess.cs`; the class comment's 120-second description is stale.

Each PowerShell case builds `New-C599PublishFx`, saves a byte snapshot, then runs many `Invoke-C599AVariant` calls. Every variant restores the entire snapshot before mutation and again in `finally`. Each negative gate still runs the real pinned-policy and ledger validators; publisher variants also exercise the real publisher against file-backed recipients. The fixture's card API is real loopback HTTP in an owned runspace.

The measured fixture held 60 files / 89,184 bytes, including Git metadata; its release subtree held 21 files / 33,338 bytes. The policy checkout commits only the 7,500-byte fixture policy. This is not a clone or scan of the monorepo. The 13 TRXs and 19 required UIDs are synthetic execution evidence written by the fixture. This path creates a plan and validates evidence; it does not launch a real nightly suite, build its projects, start AppHost, migrate a database or initialize an EF model. The selected Scripts tests do not require the shared store. The d69fe301/d8bdfd3a EF warm-up repair is therefore not the fix for these bodies.

## Where the time goes

The strongest phase sample is **Markers**, instrumented before `New-C599PublishFx` began. Entry and return-boundary observations used the owned process's PowerShell debugger and monotonic stopwatch ticks (1 GHz frequency). No source bytes or return values were replaced. Entries without a matching completed boundary remain incomplete; return-boundary spans end immediately before construction of the returned value.

The following completed outer spans are disjoint. Nested work is identified separately, never added twice:

| Markers phase | Completed spans | Seconds |
|---|---:|---:|
| Build valid publication fixture, `New-C599PublishFx` | 1 | 3.053 |
| Save snapshot | 1 | 0.146 |
| Restore snapshot | 91 | 160.276 |
| Validate pinned policy/authority | 47 | 6.480 |
| Validate execution ledger | 47 | 115.955 |
| **Unattributed remainder of 300.048s** | — | **14.138** |

Fixture creation includes the real Git checkout construction at **0.206s** and authority/evidence generation at **1.738s**. Those are nested within 3.053s. The remainder includes startup before the fixture, uninstrumented wrapper/mutation work, return-expression work, the unfinished final restore, cleanup and observation overhead. It is not assigned a zero cost.

Inside the 115.955s ledger spans:

- `Resolve-ReleaseAuthorityEvidencePath`: **611 calls, 106.776s**, about 92% of ledger validation. Each evidence path repeats the component walk in `scripts/lib/release-authority.ps1:410`, constructing `FileInfo` and using PowerShell `Get-Item` to check links/reparse points.
- `Read-NightlyTrxIdentities`: **611 completed calls, 1.966s**. Parsing tiny TRXs is not the expensive component.
- Required-UID recomputation was also small: 288 completed calls / 0.532s across the complete observed fixture and validation spans.
- Individual HTTP, file-hash, kernel IO and scheduling times were not independently closed by these probes. They remain unknown within their enclosing spans. A request-log append is not an HTTP completion span.

A matched subset provides an independent accounting check: **45 complete variants took 280.129s**. Their **90 restores took 159.078s**, pinned-authority checks 6.182s, and ledger checks 110.657s, leaving 4.211s of other variant work. Thus restores were about 57% of the complete-variant wall time. Within those restores, the initial file scan took 16.156s and the saved-file write loop took 142.885s. That loop includes `Split-Path`, repeated parent `Test-Path`/possible `New-Item`, and unconditional `File.WriteAllBytes`; it is not a measurement of physical disk writing alone. Evidence-path checks nested in these variants took another 102.245s.

Real Git Trace2 timing corroborates that repository work is a minor contributor:

| Method | Real Git commands | Sum of Git's own exit elapsed seconds | Sampled PowerShell lifetime CPU seconds |
|---|---:|---:|---:|
| Joins | 114 | 0.242 | 249.10 |
| Ledger | 150 | 0.254 | 311.36 |
| Markers | 150 | 0.251 | 278.05 |

Git's internal time excludes parent-side spawn, waits and pipe draining; those costs must not be relabelled zero. CPU counters are sampled process totals at 100 ticks/s, not exclusive phase timings or quiet-host duration estimates; multiple threads can accumulate more CPU seconds than wall seconds.

All timeout messages included `completion root=True stdout=False stderr=False`. In this implementation `root` is the asynchronous `StartAndWaitForRootAsync` task, and cancellation can complete it. `IsCompleted` is not `IsCompletedSuccessfully`, an exit receipt, or proof that the OS process exited. The samples show substantial PowerShell CPU demand and ongoing fixture work near the deadlines. These flags alone do not establish a post-exit pipe-holder defect.

## Load, quiet-host limits and measurement quality

The host was shared throughout. One-minute load ranges while each PowerShell root was sampled were Joins **28.05–52.59**, Ledger **7.70–32.40**, and Markers **17.17–42.20** on 24 CPUs. A low instantaneous load sample during Ledger is not an isolated quiet-host experiment. No other session was stopped to manufacture one.

**Quiet-host execution duration and p95 remain unknown.** This one run separates admission from execution and identifies repeated work; it does not establish a quiet/loaded slowdown ratio. The unchanged policy fixture rules out dependence on the batch's live-policy edit, not every possible logical failure, and does not prove that all 300 seconds are scheduler delay. The measured repeated filesystem work is substantial in its own right.

Instrumentation limitations are retained explicitly:

- The native opt-in C1514 recorder captured the three test envelopes but has no internal `ScriptHarnessProcess` spans; those phases were otherwise unattributed.
- An initial observation launcher failed before lease acquisition or any test launch because this container lacks `/proc/pressure/cpu`. The sampler was corrected to record that unavailable input as null. It did not repeat a method.
- PowerShell prepended its installation directory to PATH, bypassing the intended executable observation shim. The requested run was kept intact. Read-only Git Trace2, 500ms owned-process samples and fixture request-file events remained active.
- Probes were attached locally to owned PowerShell processes through their diagnostic named pipes. Joins has no usable function-boundary profile. Ledger's first callback format was invalid; later boundary selection was corrected. Exclude those early records and do not use Ledger as a clean performance baseline. Markers received the corrected probes before fixture creation and supplies the tables above.
- Debugger/append overhead was not separately calibrated, so these are diagnostic observations, not an uninstrumented benchmark or a measured p95. No test result is credited green from them. A separate read-only command-discovery microprobe (five misses per mode) did not support missing-command discovery as the main multi-minute cost; that hypothesis was discarded.

The original 973b4d1b report remains historical evidence: base Ledger passed in 258.553s, base Joins/Markers timed out, and the candidate timed out all three. It is not replaced by this run. The current three timeouts are inherited reproduction at the brief's exact source, not an introduced source change.

## Smallest compliant Code repair

Start with the **test fixture**, keeping the current 300-second execution limit and every named refusal/publication assertion:

1. In `scripts/test-release-gate.ps1`, make the valid restored snapshot the invariant between variants. Keep mandatory restoration in `finally`; remove the redundant restore at the start of `Invoke-C599AVariant` only after a regression proves first-variant validity, byte-for-byte restoration, cleanup after thrown mutations and isolation between successive variants. A failed restore must stop the case, not permit a contaminated next variant.
2. Materialize the immutable snapshot inventory, saved bytes and unique parent-directory information once. Make restoration avoid rewriting byte-identical files and repeating PowerShell path-provider operations for every unchanged parent. Preserve removal of unexpected files, absence restoration, link handling and recipient isolation. Measure the native file reads/writes and parent operations separately. The measured write-loop cost is the priority; copying a 33KB snapshot should not repeatedly pay seconds of metadata/provider dispatch.
3. Apply R3 to any shared seed setup: one immutable fixture/template builder per selected test run, independent clones and mutable recipient stores per case, selected-run warm-up outside timed bodies, and explicit setup/clone/teardown phases. If sharing the real Git seed, use the `GitTreeTemplate` approach from master `1527c29fef2737bf3eaa49f2293d82d85402265a`, with explicit rebinding of absolute fixture JSON paths and creation of each case's own listener. Do not share a live listener or mutable ledger. **A Git-template-only change cannot fix this incident:** the measured checkout setup was 0.206s, and the entire fixture only 3.053s.
4. Preserve fresh production evidence-path checks for every variant. Do not cache acceptance across mutations or remove the G-287 checks. If the remaining 106.776s path component walk still dominates after the fixture repair, a separately tested native-metadata implementation may preserve its containment, link and reparse semantics; that is a measured follow-up, not grounds to skip the guard.

The first two changes remove measured duplicate/redundant work without weakening the publication contract. Their post-fix savings have not been executed here and must not be claimed. Do not move an unchanged expensive body outside a timer just to conceal its cost. Record shared setup separately and keep finite admission, body and teardown budgets.

Run the exact three methods once after the repair, with meaningful snapshot-isolation regression coverage and phase recording. Collect comparable uninstrumented per-method samples from the next scheduled qualifications, separating platform, load, cold setup and admission. Bound collection to the next 20 qualifications/20 days under CARD-1731, with an early checkpoint after five samples and an explicitly arranged quiet-host sample if that comparison is still required. Set method/row execution budgets to approximately **2 × measured p95**, with provenance and rounding. This run's censored 300s observations are neither completed costs nor p95; it authorizes no arbitrary 600s timeout. Keep the existing limit until the repair is measured. Retain failed receipts and provisional costs.

## Classification and coupled policy

The class is already `[Category("Integration")]` and `[ParallelLimiter<ProcessSpawnLimit>]`. It is not `Slow` and is absent from `tests/Antiphon.Tests/slow-tests-allowlist.txt`. The measured methods clearly qualify for **Slow as well as Integration**. Add the class marker and an adjacent qualifying `#` reason followed by its exact FQN, `Antiphon.Tests.Scripts.ReleaseGatePublicationAuthorityTests`, and run the compiled classification/registry guards. Keep it out of ordinary Unit-only selections; an Integration selection that covers publication must still run it. Slow is a cost marker, never Skip or a new nightly/RC exclusion.

The existing class-cost entry is **600s for 15 nodes, `observed-bound`**, derived from a partial Windows run, explicitly not p95. These three censored Linux methods do not establish a complete 15-method class cost. Preserve that history and append measured post-repair costs rather than replacing it with a fabricated complete total. No extra full-class sweep was run here.

If Code changes the policy selection/cost data in `tests/test-execution-policy.json`, recompute its canonical `policyHash`, then recompute and update the live-lane `PinnedInputDigest` together in the same reviewed change. Validate the policy/hash readers and live-lane census. The protected census inputs are `nightly-common.ps1`, `nightly-policy.ps1`, `nightly-coverage.ps1`, the policy JSON, and `fixtures/nightly/c487-probe/discovery.diag`. Recompute both identities whenever relevant inputs change; change a stored value only when its computed input changes. A harness/Slow-registry-only edit does not itself alter these five inputs and must not invent hash churn.

Unchanged identities at this Debug source:

```text
live policyHash: 0686a97cccbec3d421b2298b47ff64aaa63d923d654252e2ea44bd3d1532487b
live-lane pin:   f5151a2a95ed14a8de0a50f9a475d0d45853949ce61c1ed83304c04178bfd73f
fixture policyHash: 4632e48782dd7d8338f6a38e59278eb7da2d2daff35cf3aa207601f2a8a0fa2f
fixture raw SHA256: 29def01d32f4c79c6affebe92355dde59ca8f9d3bb584e8e58a6a5c21d471adb
```

The methods read the committed fixture policy through real Git; they do not consume the live policy or live-lane pin. Neither identity was edited in Debug. No `land-graph -Update`, master/rc push, activation or product repair occurred.

## Reproduction and retained provenance

From a clean detached checkout of the tested source, the native build and exact selection were:

```sh
TEMP=/tmp pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1731-native-build -SlotWaitMinutes 12 -- dotnet build tests/Antiphon.Tests -p:UseAppHost=false --nologo

TEMP=/tmp pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1731-ledger-three -SlotWaitMinutes 12 -- dotnet run --project tests/Antiphon.Tests --no-build -p:UseAppHost=false -- --filter-uid Antiphon.Tests.Scripts.ReleaseGatePublicationAuthorityTests.1.1.C599A_ExecutionLedger.1.1.0 Antiphon.Tests.Scripts.ReleaseGatePublicationAuthorityTests.1.1.C599A_ExecutionLedgerJoins.1.1.0 Antiphon.Tests.Scripts.ReleaseGatePublicationAuthorityTests.1.1.C599A_ExecutionLedgerMarkers.1.1.0 --timeout 18m --results-directory <fresh-absolute-results> --report-trx --report-trx-filename ledger.trx
```

The diagnostic run also enabled `GIT_TRACE2_EVENT` and `ANTIPHON_TEST_PHASE_ROOT`, with phase identity `run=c1731-20261106`, `group=ledger-three`, `attempt=1`, `suite=antiphon`. Raw observations were collected under the task-owned ignored `.antiphon/ledger-debug`; essential outcomes, phase totals, limits and provenance are retained in this report before output cleanup. These hashes identify the original generated artifacts, not a claim that cleaned files remain available:

```text
Antiphon.Tests.dll SHA256: 7d32bfb68b6fe8907dcb1e4e6a6ed599755458a6b822d3f4f1286ac8d214bb52
ledger.trx SHA256: 9c4acc816834c21a2fd4ca4e1e4fb9115b95f21f27dfa089be134b11c68d6fb4
phase-summary.json SHA256: 31a3275384e1544d2dba7a9b89550ad2836f0fdf79061d720925cf45fb250180
git-trace.jsonl SHA256: a43ec17d5a10b12968f687456bda30cd8a487010dcde461238fdce3d2e01c7ab
```

## Cleanup and deliverable boundary

All build, execution, fixture-observation and diagnostic-attachment commands were joined. Both granted leases were released. No owned measurement process remained in the process census. The harness removed all three exact `/tmp/c599-nightly-<invocation>` results trees and their matching control trees; all six were checked absent.

After confirming clean tracked source, `git worktree remove /work/worktrees/task-b8770621-measure` removed the owned detached checkout and its inventoried **72 native bin/obj roots**. The task-owned `/work/worktrees/task-b8770621/.antiphon/ledger-debug` directory, including raw test results and phase outputs, was removed by that exact literal path after retaining the evidence above. A final census found zero bin/obj/TestResults directories in the assigned checkout. No variable/glob deletion, `rm -rf`, `git clean`, shared-checkout cleanup or branch switch was used.

The sole deliverable and sole commit content is this investigation. Code, tests, policy, registry and runtime settings remain unchanged. The task-branch publication identity is supplied in the final caller report.

--- next stage ---
next: code
handoff: Repair C599 snapshot restoration first: preserve finally isolation, remove the redundant pre-restore under a regression, cache immutable snapshot/parent metadata and skip unchanged writes; share any expensive seed once per selected run using isolated template clones. Preserve all production evidence-path checks and assertions. Add Slow plus its registry reason, remeasure the exact three methods, derive budgets from comparable completed samples, and refresh both policy identities if protected inputs change.
artifact: docs/investigations/2026-11-06-debug-card-1731-ledger-tests.md
