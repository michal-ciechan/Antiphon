# CARD-1381: bounded-reader timing under host load

Plan and verification design, 2026-10-20. Master-only, test-only; Code budget **30–45 minutes**. Inspected base and fetched `origin/master`: `47cdf05e761fa733a59d8f9c630ca09127effb41`. No builds or tests run during Plan.

## Ground truth

Line references below bind to that base.

| Card assumption | What the code does |
|---|---|
| Project/held failed at 4033 ms against 4000 ms. | The card records that observation; it was not reproduced here. `scripts/test-checkpoint-bounded-readers.mjs:24` spawns PowerShell; `:29` starts `Date.now()` and `:30` arms the watchdog. There is **no numerical assertion on `elapsed`**: `:41` fails on watchdog expiry, a surviving descendant, or child exit != 0. |
| The 4 s clock measures the read. | It starts just **after** `cp.spawn` returns, excluding the synchronous spawn call but including remaining child scheduling/startup, PowerShell parsing, assembly load, helper imports/conditional compilation, function extraction, read, assertions, process shutdown and redirected-pipe close (`:31`). The JSON `elapsed` at `:39` additionally includes survivor inspection and cleanup at `:33–38`. Node startup and fixture/mkfifo preparation at `:1–23` are outside it. |
| Helper compilation is read work. | `scripts/test-checkpoint-bounded-reader.ps1:3–5` loads the tool assembly and sources helpers; `:7–11,18` parses/extracts the project function. `scripts/lib/checkpoint-file-reader.ps1:3–4` and `checkpoint-process-runner.ps1:3–4` conditionally call `Add-Type`; loaded types can skip compilation. All these steps currently consume the supervisor budget. |
| Keep the 4 s / 3 s production bounds. | Precisely: 4000 ms is the **test's project acceptance/watchdog bound**, not a production setting. The loaded facade (`tools/Antiphon.Checkpoints/Coverage/ConfinedFileReader.cs:18–19`) delegates to `src/Antiphon.HostCleanup/Checkpoints/CheckpointFileReader.cs:16,71,107`, whose 3 s stall window re-arms on progress. The script fallback also has a 3 s window (`scripts/lib/checkpoint-file-reader.ps1:24,57,78`). Neither reader changes. |
| Ten argument combinations need coverage. | `tests/Antiphon.Tests/Checkpoints/CheckpointBoundedReaderTests.cs:13–23` has five modes × ordinary/held. The other supervisor bounds are 35 s tool-git, 20 s script-git, and 125 s for either eval (`.mjs:28`). Refusal/value assertions live in the PowerShell probe (`:19–42`); survivor detection is in Node (`:34–41`). |
| Reuse CARD-1233 S1; check S8. | S1's child-side, nonce-bound read timing is present in `CheckpointInheritedFileTests.cs:69–87,212–216`, landed as `38d2b243b628b25c58ac982c5060246d03bf18bf`. S2–S4 are present as `c04b5cd7b5b6c527e717e23a309b713091ac3b69`, `16f23fbfcb2c5b8f76e1707281364994c5c67fdf`, `9e78a08880cb300dab6fc3bedbefc1a47eed3284`. S8 is absent: no `CheckpointFixtureRoutingTests.cs`, and `CheckpointFixtureProcess.cs` has no cancellation overload. This fix does not depend on S8. |

## Decisions

- **D-1 — Measure only the selected project read.** In the test PowerShell probe, after the imports, wrap the existing `Read-CheckpointBytes` function for the exact `fixture.csproj` path, following S1. Save and invoke the original scriptblock without changing returned bytes or exceptions. Start a monotonic `Stopwatch` immediately before that invocation and stop it in `finally`, before emitting the timing receipt. This excludes PowerShell startup, compilation, function extraction, XML setup/parsing, output delivery and shutdown. Keep the existing project refusal/value assertions verbatim.
- **D-2 — Separate acceptance from emergency cleanup.** Node retains `bound=4000` for project read acceptance, but uses an explicit **30,000 ms whole-child watchdog** for project mode. That test-only ceiling leaves 26 s beyond the read bound for startup/scheduling/shutdown; it is not a new reader deadline. Require `0 <= readElapsedMs < 4000`. Watchdog rescue still fails and kills the same owned process group. Keep the non-project watchdogs, descendant checks, exit checks and refusal assertions unchanged. Reject simply changing 4000 to 4500: startup remains charged to the read, and the acceptance bound would be weakened. Reject an idle-host prerequisite, retries, and increasing production deadlines.
- **D-3 — Bind the measurement.** Node generates a fresh nonce and passes it to its PowerShell child. Emit one dedicated stderr record containing that nonce, project mode/shape and integer read duration. Node requires exactly one matching, well-formed record; missing, duplicate, mismatched or invalid timing fails closed. Preserve the whole-process `elapsed` as diagnostic data and add `readElapsedMs` and `watchdogMs` to the JSON. A successful timing record cannot override child failure, expiry or a survivor.
- **D-4 — Controlled pre-read delay.** Add a test-only optional delay argument, default zero, accepted only for the project probe and bounded to 0–10,000 ms. The wrapper applies it **before** starting the read stopwatch. The witness requests 5000 ms and reports the actual monotonic delay under the same nonce, so Code can prove the perturbation occurred. This deliberately injected scheduling pause is the fault; it is not a readiness sleep.
- **D-5 — Placement and scope.** Read `GET /api/runner-defaults` (revision 2) and `GET /api/session-runners` during Plan: eligible Linux capacity exists. Code requires the **Linux / Unit process lane** because these tests use FIFO, `/proc` and process groups. Dispatch with `-Platform Linux`, omit `-Runner`, and re-read placement at dispatch. No fleet location is embedded. No session-wait behavior changes. CARD-1233 S10's broader supervisor/fixture-ownership work remains separate; coordinate if it lands first.

## Slices

| Slice | Files and intended outcome | Verification |
|---|---|---|
| S1: commit/push the red witness | `scripts/test-checkpoint-bounded-readers.mjs`: forward the optional project delay, leaving the old 4 s watchdog intact. `scripts/test-checkpoint-bounded-reader.ps1`: install the project-only wrapper with delay and unchanged original read. `tests/Antiphon.Tests/Checkpoints/CheckpointBoundedReaderTests.cs`: add `C1381_Project_read_budget_excludes_startup_delay`, driving real project/held with 5000 ms delay and expecting supervisor exit 0. Share the existing launch helper if needed; retain process registration and limiter. | CP-1 must fail at expected exit 0 versus actual 1, with JSON `expired=true`. Commit before running; this is the old-measurement witness, not a passing timeout test. |
| S2: commit/push the repair | Same three files only: add the stopwatch/nonce receipt and delay-duration evidence; separate the project watchdog from read acceptance; validate the receipt; strengthen the witness's success assertions and update the misleading C# “exact hard bound” comment. Existing ten arguments and PowerShell refusal/value assertions remain unchanged. | CP-2 passes the same witness; CP-3 passes all ten original cases once. |

Do not modify `run-checkpoint.ps1`, either production reader, or shared fixture ownership helpers. No new infrastructure or public production seam is required.

## Verification design

### Inspection and oracles

- **V-1 / R-1:** `C1381_Project_read_budget_excludes_startup_delay` must use the real Node → PowerShell → project reader path with a held FIFO. On S1, the 4 s supervisor expires during the requested 5 s delay, and the test fails its exit-code assertion. On S2, require exit 0, `expired=false`, `survivor=false`, valid nonce-bound timing, actual pre-read delay >=5000 ms, whole-process elapsed >=5000 ms, and read duration <4000 ms. No upper assertion on startup delay, and no subtraction of a guessed startup cost.
- **V-2:** `changed_readers_bound_fifo_and_exited_child_pipes` executes all ten combinations. Both project cases must supply valid timing; held project still catches only the existing `not regular` refusal. Git/eval cases retain their existing ordinary-output and timeout-reason assertions, and held descendants must not survive.
- **Inspection guard:** review the final three-file diff for unchanged production 3 s deadlines, project 4 s acceptance, all refusal/value checks, and Node expiry/exit/survivor checks. Ensure timing is captured in `finally` on refusal and stops before receipt output. Missing/invalid timing is failure, never zero/default success.
- **Positive control:** CP-1 is the single committed red witness for this test-harness repair; CP-2 proves restoration with the same injected delay. Build errors, zero tests, skip, unrelated timeout or a failure before the exit-code assertion do not satisfy red. If needed, repair witness reachability and rerun only CP-1. No production safety guard is changed and no separate mutation campaign is added.
- **Out of scope:** full Unit/namespace/assembly sweeps, Windows qualification (these Linux cases skip there), load generators, repeated green runs, and CARD-1233 S8–S10 fixture migrations. The original failure is card evidence; this Plan makes no runtime reproduction claim.

Run the committed rows through the checkpoint tool and host build-slot gate, one run per `After` group, with `--expected-source-sha <full committed SHA>` and fresh evidence roots. Use `--rows CP-1` for S1 and `--rows CP-2,CP-3` for S2. Continue foreground `wait` calls while exit is 75; do not leave an executor running. CP-1's exit 1 is recorded as the intentional historical red. Final green evidence consists of CP-2/CP-3, with exact counts and SHA-qualified receipts. Keep generated evidence ignored; remove only the producer-owned alternate outputs. No test runs are authorized in Plan.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1381-red/` | linux-unit-delay-red | `/*/*/CheckpointBoundedReaderTests*/C1381_Project_read_budget_excludes_startup_delay*` | R-1 red | exactly 1 executed, 1 expected assertion failure, 0 skipped; expired=true | 1 | 5 | true |
| CP-2 | S1-S2 | `tests/Antiphon.Tests -> bin-c1381-green/` | linux-unit-delay-green | `/*/*/CheckpointBoundedReaderTests*/C1381_Project_read_budget_excludes_startup_delay*` | V-1, R-1 green | exactly 1 passed, 0 failed/skipped | 1 | 5 | true |
| CP-3 | S1-S2 | CP-2 | linux-unit-reader-matrix | `/*/*/CheckpointBoundedReaderTests*/changed_readers_bound_fifo_and_exited_child_pipes*` | V-2 | exactly 10 passed, 0 failed/skipped; five modes, both shapes | 10 | 6 | true |

### Cost

Estimated Code total **30–45 minutes**: 14–25 minutes authoring/inspection plus the 16-minute checkpoint floor, with remaining allowance for receipts/cleanup. CP-3 includes about 285 seconds of intentional held-child deadlines (30 + 15 + 120 + 120), so do not shrink its estimate to ten fast unit tests. No additional green repetitions are required. Test design is included here; next stage is Code.
