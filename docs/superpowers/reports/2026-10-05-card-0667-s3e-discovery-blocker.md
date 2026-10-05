# CARD-0667 S3e: missing generation-bound delivery evidence

S3e is blocked before implementation: discovery cannot obtain the pre-delivery native transcript binding/floor required by the landed conditional-release protocol. Completing the requested live rowless release needs a prerequisite outside S3e's closed production footprint. No implementation or test pass is claimed.

Original Code task / landing owner: `11f476f4-91b6-46e3-b940-65eb04735422`.
Branch: `feat/card-task-11f476f4`.
Worktree: `/work/worktrees/task-11f476f4`.
Task base and inspected source: `53feeb03dc152f4169a26eb781bfbe3a380ebd08`.
Desktop checkout: `C:\Antiphon\worktrees\card-task-11f476f4` (not reachable from this mirror).
Plan: `docs/superpowers/plans/2026-10-01-card-0667-terminal-task-seat-release-plan.md`, S3e / CP-16.
This report is the only change. The final task response records its pushed commit SHA.

## Evidence

| Source at the inspected SHA | Fact and consequence |
|---|---|
| `src/Antiphon.SessionRunner.Contracts/TerminalSeatRelease.cs:25` | The request explicitly requires the native binding and file-order floor captured **before the current generation's task prompt**. An unknown binding/floor cannot qualify. |
| `src/Antiphon.SessionRunner/TerminalSeatReleaseObservation.cs:97` | `CheckEvidence` refuses an empty binding or negative floor, then requires matching binding, prompt revision above that floor, and end after prompt. Reading an idle transcript now does not establish which prompt belongs to the accepted generation. |
| `src/Antiphon.SessionRunner/SessionRunnerRuntime.cs:1131` | Observation checks runner store and accepted generation, reads the tailer, and passes the caller's request to qualification. It does not supply a stored generation-specific delivery baseline. |
| `src/Antiphon.SessionRunner/TerminalSeatReleaseObservation.cs:173` | Fresh observation assigns file-order revisions while reparsing native normalized records from the beginning. These are not a documented interchangeable server transcript sequence. Binding identity is a hash including native file identity at line 211. |
| `server/Application/Dtos/SessionRunnerDtos.cs:5` | Inventory supplies accepted generation and transcript-bound status, but no pre-delivery native binding/floor or receipt associating a prompt with that generation. |
| `server/Domain/Entities/SessionQueuedMessage.cs:98` | Queue rows retain delivery time, accepted generation, delivery verdict and server transcript baseline sequence. They do not retain the required native binding/floor pair; a rowless unknown seat need not have a queue row at all. |
| `server/Application/Services/TerminalRunnerSeatReleaseService.cs:328` | The existing coordinator accepts a complete observation request from its caller. Its transport call at line 414 consumes that request; no production request-construction boundary exists in the server. |
| `tests/Antiphon.Tests/Application/RunnerSeatReleaseFixture.cs:39` | The fixture supplies `"binding", 10` directly. Its wire returns a fixed qualified observation at lines 341-365 without inspecting observation-request evidence. This is useful isolation for prior coordinator tests, but cannot prove discovery acquired a real generation-bound baseline. |
| `.antiphon/task-b71467ba.md`, Implementation | The S1b report explicitly leaves supplying the native binding/floor to the delivery owner and later server work. This is an unimplemented prerequisite, not an observed failure of an already complete discovery path. |

The source search covered all server `TerminalSeatObservationRequest` / `ObserveTerminalSeatAsync` call sites, the inventory DTO, runtime qualification, native revision construction and queue delivery fields. This is source inspection, not an executed runtime reproduction.

A read-only observation with an empty binding can reveal today's native binding/revisions, but cannot recover the missing pre-delivery fact. Choosing zero or `LastPromptRevision - 1` afterwards would allow an idle turn predating this generation to satisfy the numerical check. Choosing today's transcript revision as the floor safely requires a future prompt and cannot reclaim the already-idle rowless seat required by CP-16. Neither is the planned behavior. Already-exited cleanup does not substitute for the explicitly required live/Idle case.

## Required scope decision

The plan's Exact implementation footprint section says: **"A newly necessary production seam outside this table returns to Plan with its exact file/boundary."** S3e permits only `TerminalRunnerSeatReleaseService.cs`, `RunnerSlotService.cs`, `RunnerSlotDtos.cs` and its two test/fixture files. The missing source of native delivery proof is in the runner/delivery protocol, outside those paths.

Commission a Plan amendment for a prerequisite that captures or independently proves the prompt's association with the accepted generation, preserves that evidence for discovery/recovery, and makes it available through both local and phone-home conditional observation. Keep unknown historical/adopted evidence deferred. The amendment must identify:

- `src/Antiphon.SessionRunner/SessionRunnerRuntime.cs`: generation-bound capture/qualification under the existing launch/input gate, including restart/adoption semantics.
- `src/Antiphon.SessionRunner.Contracts/TerminalSeatRelease.cs`: how a discovery caller requests proof without inventing the pre-delivery baseline. If the evidence is transported in inventory instead, explicitly admit both inventory DTO/mapping sides.
- The selected persistence/delivery owner and concrete files, if proof must survive restart; the present server queue fields are insufficient by themselves.
- `tests/Antiphon.SessionRunner.Tests/TerminalSeatReleaseTests.cs` and `tests/Antiphon.Tests/Application/RunnerSeatReleaseFixture.cs`: a real-runtime witness using proof acquired through that production boundary, including an old-generation idle transcript that remains refused. Preserve existing PC-26/G-26 protection and revise the closed checkpoint scope/cost before Code.

After that prerequisite, resume S3e: bounded/fair inventory processing, independent runner/candidate failures, rowless ownership/revalidation, durable tuple deduplication and the four CP-16 tests. Add the required method wildcard suffixes to CP-16 when it is commissioned for execution. Do not start S4a or activate automatic release to work around this prerequisite.

No partial discovery API or constant-returning test seam was committed: those would leave the central live-rowless acceptance unresolved while making fake-wire green misleading.

## Verification accounting

| ID | Actual outcome in this task |
|---|---|
| CP-16 / V-3 / `Sweep_budget_is_bounded_and_resumes_fairly` | NOT RUN; implementation blocked. |
| CP-16 / V-3 / `One_runner_failure_does_not_hide_other_candidates` | NOT RUN; implementation blocked. |
| CP-16 / V-3 / `Unknown_server_session_with_idle_runner_is_released` | NOT RUN; missing production evidence acquisition blocks this acceptance. |
| CP-16 / V-3 / `Discovery_is_idempotent_across_restart` | NOT RUN; implementation blocked. |
| V-1, V-2, remaining V-3 | NOT RUN; no inherited pass credited. |
| R-1, R-2, R-3, R-4 | NOT RUN; later S4c qualification remains pending. |
| CP-1 through CP-6 | Deferred to S4c final Linux/Windows qualification; not passed. |
| Manual work | Completed source/contract and closed-footprint inspection. No migration, runtime acceptance, deployment or activation performed. |

Builds: 0. Tests: 0 executed, 0 passed, 0 failed, 0 skipped. No checkpoint was launched, no TRX/receipt exists, and no CHECKPOINT line is fabricated. `slot=not-requested`, `waited=not-applicable`. There are no unlisted builds/tests, repair runs, repetitions or alternate output directories to clean. The explicit CP-16-only/no-whole-Unit brief controls this slice's scope.

The task token was present. Authenticated `GET /api/runner-defaults` and `GET /api/session-runners` succeeded on 2026-10-05: defaults revision 2 and eligible Linux/Windows catalogue entries. These reads did not change host placement, budgets or running state. The first read-only HTTP helper attempt found `python3` unavailable; the PowerShell read succeeded. No secret was printed.

Run `scripts/check-evidence-diff.ps1` over the full task base through the final report commit; its exact outcome is recorded in the final response. Generated payloads were not added to Git.

## Pending Mutation and handoff

S3e controls remain **PENDING** for method-scoped SourceLanding Mutation:

- PC-52 / G-52: candidate budget break; bounded processing and fair continuation.
- PC-53 / G-53: early return on a candidate exception; later runner/candidate still releases.
- PC-55 / G-55: mandatory server-session join; both local and phone-home rowless variants.
- PC-56 / G-56: random-row identity instead of tuple upsert; repeated, concurrent and restarted discovery variants.

All other plan controls, PC-1 through PC-51, PC-54 and PC-57 through PC-90, remain with their owning slices and pending Mutation; this report discharges none. Mutation owns deliberate mutants, red/restore/green and missing-control discovery.

Restart: `none`. Owner: caller/orchestrator for eventual activation after the completed plan and Review. Automatic release remains dormant.

Next: `decide` on commissioning the prerequisite Plan amendment, then Code for the remaining S3e work. This is not ready for implementation Review or land. Preserve original Code owner `11f476f4-91b6-46e3-b940-65eb04735422`; adoption of completed S3e still waits for predecessor S3c owner `774a1cc8-7e48-4926-a9a4-4b17a97a85d6` to land. No branch rebase, reset, amend or force-push was performed.
