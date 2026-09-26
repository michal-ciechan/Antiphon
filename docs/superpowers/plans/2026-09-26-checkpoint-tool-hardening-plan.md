# Checkpoint tool hardening: CARD-0759, CARD-0760, CARD-0762

## Verified baseline (2026-09-26)

Source baseline is `8805577f19bc4d63fb7139552fa04bc1fe5bc0a7`, the current `master` and this task branch's starting HEAD. All three board cards are open with no revisions. The CARD-0759 and CARD-0762 incident times and exit codes are board evidence; this investigation did not reproduce either live incident.

| Card | Finding on current master and cause |
|---|---|
| CARD-0759 | Still open. `CheckpointApp.Start` detaches `execute` without task ownership arguments (`tools/Antiphon.Checkpoints/CheckpointApp.cs:154-169`); `Program` calls `ExecuteAsync(..., CancellationToken.None)` (`Program.cs:64-65`); the scheduler only cancels on its total timeout (`Execution/RunScheduler.cs:41-80`). `--parent` belongs to the **slot holder**, started with the executor pid (`Slots/LeaseHolder.cs:20-22,35-50`; `Program.cs:72-98`), so it keeps the broker lease tied to the executor, but cannot notice that the owning Antiphon task has settled. `BuildSlotClient` captures `ANTIPHON_TASK_ID` only as lease metadata (`Slots/BuildSlotClient.cs:133-140`), and releases after driver completion (`:170-192`). The tool can therefore start CP-2 after settlement while its executor and holder remain alive. `EstimatedMinutes` is one integer (`Manifest/PlanTableImporter.cs:80-95`), and row timeout is three times it (`Execution/RowTimeout.cs:5-11`); the CARD-0759 Windows 9-minute CP-1 estimate is about 3x low. |
| CARD-0760 | The absolute claim “no way to make a row serial” is stale: YAML has `CheckpointSpec.Serial` (`Manifest/CheckpointManifest.cs:64-79`), and the scheduler honors it (`Execution/RunScheduler.cs:278-305`). CARD-0726's checked-in YAML at commit `89a00a7b` sets CP-12 `serial: true`; that commit is on `feat/card-task-ad64242f`, **not current master**, so the named YAML file is absent here. The live gap is the documented `run --plan` path: the importer has no serial column and assigns only the first nine fields (`Manifest/PlanTableImporter.cs:16-17,62-89`), while `ExtractPayload` takes only the backticked filter and drops the following `TUNIT_MAX_PARALLEL_TESTS=1` text (`:281-293`). `RowRunner` has no per-row environment field (`Execution/RowRunner.cs:3-30`), so the resolved Markdown manifest stays `serial: false`. |
| CARD-0762 | Still open. Concurrent builds/rows share one `BuildSlotClient` logging callback (`CheckpointApp.cs:19-20,37-40`), and each slot grant/wait/release invokes it (`Slots/BuildSlotClient.cs:149-171,206-211,232-244`). The callback uses independent `File.AppendAllText` opens with no synchronization. On Windows, overlapping exclusive opens can throw `IOException`; that escapes the scheduler to `CheckpointApp.ExecuteAsync`'s catch and becomes exit 6 (`CheckpointApp.cs:69-76`). `--serial` reduces row overlap but the log writer must be safe even when build and row lease events coincide. |

### Importer integrity and authoring friction

- `Filter` text such as `same as CP-1` is **not a filter**. The importer treats any payload not starting `/` as a shell command (`PlanTableImporter.cs:98-109`), silently ignoring a CP reuse build cell for that command (`:126-129`). Refuse this phrase with a diagnostic requiring the exact filter. Auto-expanding it would undermine the closed-list, exact-filter contract.
- `Build` accepts only bare `CP-n` or `<project> -> bin-x/` (`:131-147`). Thus `CP-1 (-NoBuild)` from CARD-0711's plan needs hand editing. Accept this exact annotation as equivalent to `CP-1` and still check prior-row/build identity; reject other prose. This belongs with CARD-0760's importer slice.
- The CARD-0585 rule says reuse only within the same `After` group (`docs/testing-and-build.md:155-162`). The current importer instead warns and sets `RelaxSharedBuildAfter=true` (`PlanTableImporter.cs:133-140,164`), bypassing the YAML validator's proper rejection (`Manifest/ManifestValidator.cs:63-70`). Remove the relaxation and reject cross-slice reuse; require a fresh build row. This integrity repair belongs in the importer slice.
- A bare `|` inside a Markdown filter splits cells; `SplitRow` already unescapes `\|` (`PlanTableImporter.cs:249-278`). Keep Markdown escaping, add a targeted malformed-column diagnostic, and show the escaped combined-class form in the doc. No implicit reconstruction of ambiguous cells.

## Design and slices

### S1 — CARD-0759: stop with the owning task

Add an executor ownership guard enabled when the inherited `ANTIPHON_TASK_ID` and `ANTIPHON_API` are present. It polls `GET /api/agent-tasks/{id}` with the inherited task token, reading `summary.status` and `session.status` (`server/Api/Endpoints/AgentTaskEndpoints.cs:155-164`; `server/Application/Dtos/AgentTaskDtos.cs:248-277,382-385,386-442`). `Succeeded`, `Failed`, or `Canceled` settles ownership; `Stopped`/`Failed`/`Stopping` on the matching task session means the task's process owner has gone. Do not watch the transient `run` CLI pid: it exits normally after detaching. Do not persist the token or echo request headers into `request.json`, executor.log, or reports. Local tool use without task env stays unguarded.

Check ownership before creating a run, at executor entry, and before each build/row launch; poll during a running driver and slot wait (target interval 2-5 seconds, bounded HTTP timeout). On settlement or dead session, cancel all work, kill **each** child process tree, await all row/build tasks and lease disposal, mark pending rows `owner-ended`, publish a terminal report and return a distinct nonzero ownership exit (propose 7; keep exit 6 for crashes). A status read failure is not proof of settlement: retry briefly, then fail closed for a task-bound executor with a recorded `owner-unverified` reason. Fix `RunScheduler`'s external-cancellation path, which currently checks only `totalTimedOut` (`Execution/RunScheduler.cs:66-81`) and can otherwise schedule later rows. `ProcessDriver` must cancel its own local process rather than its shared `_current` field (`Execution/ProcessDriver.cs:19,35-45,57-73`) so two simultaneous drivers are both killed and their leases can dispose. Ensure cancellation during baseline comparison and cleanup cannot start new work.

For estimates, add optional `EstimatedMinutesWindows` after the nine required table columns, selecting the Windows value into the resolved row's `EstimatedMinutes` and derived row/total deadlines. Require positive integers and retain the original `EstimatedMinutes` for Linux. Document separate ordinary cost floors. This avoids a global multiplier hiding short Windows rows; CARD-0759's git-heavy row can state 9 Linux / 27 Windows. Keep the existing nine-column table valid.

Verification design: new `CheckpointTaskOwnershipTests` with four deterministic cases: terminal task before executor admission starts zero drivers; settlement during two parallel drivers cancels both, releases both fake leases and skips later rows; a stopped bound session stops the executor while a normally exiting start CLI does not; transient HTTP error recovers, persistent unknown ends `owner-unverified` with no further launch. Use an in-memory HTTP handler/fake driver; no production task, runner, or build slot. Positive control PC-1 removes the terminal-status cancellation branch; the settled-during-run case must fail at its no-later-launch/lease assertion, then pass restored.

### S2 — CARD-0760: table serial declaration and strict import

Add optional `Serial` after the required table columns (`true`/`false`, blank = false); importer sets `CheckpointSpec.Serial`. `run --plan` and `import --plan` must yield the same value, and the resolved YAML must show it. `Serial=true` means scheduler exclusion only. It does **not** set `TUNIT_MAX_PARALLEL_TESTS=1`, which governs tests within one TUnit host; add a separate explicit per-row `Environment` column with `NAME=value` parsing and child-only propagation, rather than accepting prose in Filter. CARD-0726 CP-12 can then declare `TUNIT_MAX_PARALLEL_TESTS=1` and `Serial=true` independently. Put the parsed map on `CheckpointSpec`/`RowRequest`/`DriverRequest`, and apply it to the child `ProcessStartInfo.Environment` (`Execution/IDriver.cs:15-20`; `Execution/ProcessDriver.cs:75-84`), never to global process environment or other rows. Validate names and duplicate keys. Update `docs/testing-and-build.md` schema/example. Support the narrow annotated Build form and refuse `same as CP-n`, cross-After reuse, and malformed extra cells as above.

Verification design: extend `CheckpointImportTests` with at least four cases: optional Serial/Environment are imported and round-tripped, a fake two-row run observes the environment only on its target row, annotated reuse maps to the prior build, and the three invalid shorthand/reuse/malformed cases refuse before execution. Retain its eight existing tests, so the class floor is 12. Existing `RunSchedulerTests.serial_rows_run_alone` covers scheduler exclusion. Positive control PC-2 removes the importer Serial assignment: the new import case must fail at `Serial=true`; restore it and rerun.

### S3 — CARD-0762: one executor log writer

Replace `Note`'s per-event `File.AppendAllText` with a single executor-owned serialized writer (lock around one append handle or a channel consumed by one writer). Dispose/flush it after all build and row work ends; keep the catch path able to write a crash reason without recursively failing. Do not suppress I/O errors as if evidence were saved. Keep each row's separate build/console log paths. Do not rely on `--serial`.

Verification design: new `CheckpointExecutorLogTests` with two tests: coordinate two parallel row/slot callbacks on a barrier and assert every line appears once, uncorrupted, with exit not 6; inject a write failure and assert a clear terminal error without a recursive logging crash. Positive control PC-3 removes serialization; the concurrent append test must fail under a deterministic shared-open seam on Windows and Linux, then pass restored. A stress loop alone is not a sufficient red control.

## Verification design

The Code stage runs the following **closed list** through `dotnet run --project tools/Antiphon.Checkpoints -- run --plan docs/superpowers/plans/2026-09-26-checkpoint-tool-hardening-plan.md --after S1` (then `S2`, `S3` separately after each slice commit), waiting through exit 75. Each row builds once into its own isolated output and runs one exact class filter. Do not co-schedule `Antiphon.Tests` with `Antiphon.Agents.Pty.Tests`. The two optional columns are implemented by S1/S2 before their rows execute; the table itself needs no edit.

### Cost

Ordinary checkpoint floor: **20 minutes Linux** (8+6+6) or **28 minutes Windows** (12+8+8), excluding broker wait and authoring. Positive-control floor: **12 minutes** for three method-scoped red/green cycles at four minutes each, after the ordinary build outputs exist. Estimated Code wall budget: 45 minutes Linux or 55 minutes Windows including implementation/review slack; adjust after a measured host run. Every new test must demonstrate a production-line red assertion, not merely a compile error, fixture failure, or zero-test result.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | EstimatedMinutesWindows | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---:|---|---|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c759/` | task-ownership | `/*/*/CheckpointTaskOwnershipTests/*` | V-1, V-2, V-3, V-4, R-1 | all 4 methods, 0 failed/skipped | 4 | 8 | 12 | false | n/a |
| CP-2 | S2 | `tests/Antiphon.Tests -> bin-c760/` | importer-serial | `/*/*/CheckpointImportTests/*` | V-5, V-6, V-7, V-8, R-2 | at least 12 executed, 0 failed/skipped | 12 | 6 | 8 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-3 | S3 | `tests/Antiphon.Tests -> bin-c762/` | executor-log | `/*/*/CheckpointExecutorLogTests/*` | V-9, V-10, R-3 | both methods, 0 failed/skipped | 2 | 6 | 8 | false | n/a |

V-1..V-4 are ownership admission, running cancellation/leases, dead session, and API uncertainty. V-5..V-8 are table fields, environment isolation, annotated reuse, and strict refusals. V-9..V-10 are simultaneous logging and write failure. R-1..R-3 are unguarded local use, existing import fixtures/YAML, and parallel scheduler behavior. TestDesign must preserve these floors and exact class filters if it names individual methods.

## Risks and acceptance

- The task GET is a server read, and the executor can lose connectivity independently of the task. Bound retries and report uncertainty; never convert an HTTP error into “task still live.” A settled task may be observed up to one poll interval after the transition; recheck immediately before every new launch. The holder's `--parent` remains a lease safety net when the executor dies, not an owner signal.
- Cancellation must drain every task and child process before reporting `done`; a successful DELETE alone is insufficient if a child still runs. Test two concurrent drivers and slot leases. Preserve the existing `stop`, timeout, and crash exit semantics for unbound runs.
- Optional table columns must be named and validated; ignored unknown columns are unsafe because a typo can silently remove serialization. The existing nine-column plans continue to import. `run --plan` is the acceptance path, not a hand-edited YAML workaround.
- The CARD-0726 YAML evidence is branch-only as of this baseline. Recheck its landed state before Code uses it as a fixture; the implementation does not depend on that branch.

No production code or tests changed in this Investigate stage. TestDesign should refine method names and red controls while keeping the three slices and checkpoint roster.
