# CARD-0188: leaked prior always-on incarnation

Date: 2026-10-19. Task: `05080662`. Investigated source:
`6ec71d427574f3a1b74bfbe2042e0ea9a819d191`, the assigned master baseline,
on `feat/card-task-05080662`.

**Verdict: partially superseded; do not close as fully fixed from this evidence.**
Current code still preserves an already-running prior session after its agent
points elsewhere. Later changes prevent the ordinary automatic Fresh replacement
path and add some leak visibility, but they do not constitute a complete
superseded-incarnation reconciler. The original replacement's root cause is
**not confirmed**: its database state and launch trigger were never captured in
the available incident evidence. Continue investigation on the reporter's
machine or its retained evidence, rather than commission a cleanup implementation
from the old card text.

## Evidence and provenance

Read the live Antiphon-board records with:

```sh
pwsh -NoProfile -File scripts/card.ps1 get CARD-0188 -Board Antiphon
pwsh -NoProfile -File scripts/card.ps1 get CARD-0181 -Board Antiphon
```

The board is `8988ca03-7414-47ad-b0b6-51556c701703`.
CARD-0188 is row `bfb85052-7bc5-42e6-94fa-74cbf037ab05`, InProgress,
three revisions, concurrency token `d740f109-f042-463f-9620-d8efd7cfe89a`
at this read. CARD-0181 is row `674388eb-8900-461e-8964-a35491c1affd`,
Done, eight revisions, token `db6b8163-fa8e-420d-a64f-a9c695d0a519`.
Its stored description, sections 2–4, contains the following historical
observations, dated 2026-08-23T19:27Z on reporter build
`4909bbf9b28d7733c8a6e797c206bd8a1b3e14e4`. These are quoted/reconstructed
reporter observations, not fresh readings of that machine.

| Record | Stored observation |
|---|---|
| Standing agent G | `0c83e820-0bd5-464a-97c9-72967ce0ffa5`, PredictionMarkets-Orchestrator |
| Older session A | `912eb415-daa3-4ec7-a1df-9f740da7195e`; child start `2026-08-20T16:25:35.2452227Z`; runner Running; host PID 45988; child PID 77848 |
| Current session B | `eaf64b4c-dcc6-4777-8508-f4fd858dabee`; child start `2026-08-21T08:58:43.570919Z`; runner Running; host PID 43824; child PID 43936 |
| Agent pointers | G's current pointer was B; the reporter found no agent pointer to A |
| Stored transcript observations | A had nine misattributed entries; B had zero entries, `lastSequence: 0` |
| Runner restart log | `Adopted pty-host for session 912eb415-… (host pid 45988, running)`, followed by adoption of B |
| Missing observation | A's `AgentSessions.Status` and the actor/reason for G's A-to-B pointer transition |

The old investigation explicitly leaves these same facts unresolved:
`docs/superpowers/plans/2026-08-24-card-0181-stale-sidecar-claim-vs-exact-id-bind-plan.md:15`
and `:319`, “The leaked previous incarnation.” The two Running labels
above are **runner state**, not proof of two Running database rows. A DB-Running
current pointer would have blocked Start even in the old implementation.

Read-only historical lookups on the available server, using the documented
`ANTIPHON_API` base and task header, returned:

| UTC, 2026-10-19 | GET | HTTP |
|---|---|---|
| 21:47:25.312 | `/api/agents/0c83e820-0bd5-464a-97c9-72967ce0ffa5` | 404 |
| 21:47:26.153 | `/api/agents/0c83e820-0bd5-464a-97c9-72967ce0ffa5/sessions?take=25` | 404 |
| 21:47:26.202 | `/api/sessions/912eb415-daa3-4ec7-a1df-9f740da7195e/transcript?since=0` | 404 |
| 21:47:26.245 | `/api/sessions/eaf64b4c-dcc6-4777-8508-f4fd858dabee/transcript?since=0` | 404 |

These responses establish that the needed history is unavailable through this
server; they do not establish that the reporter's processes exited. No session
input, stop, start, restart, cleanup, or board write was performed.

## Current persistence mechanism

For an already-existing A that the runner lists Running, while G points at live
B, the following paths explain why A can persist. This is a source reconstruction
of the state described by the incident, conditional on the missing database fields;
it is not a claim that this task reproduced the original A-to-B launch.

| Path | Behavior at the investigated baseline |
|---|---|
| Session pass 1 | Only absent/Exited runner entries close a DB-live row; a listed Running A is unchanged. `server/Application/Services/SessionReconciliationService.cs:193`, `:216`, `:258`, `:264`. |
| Mirror pass | The switch acts on Failed, Stopped, or overdue Stopping; Running reaches `_ => 0`. The orphan alert collects only sessions with **no DB row**, not sessions lacking an agent pointer. Same file `:487`, `:493`, `:504`. |
| Failed A alternative | Re-adoption requires matching accepted generation and positive process/probe evidence. It restores Running without stealing G's pointer back and explicitly leaves stopping to the operator. Same file `:564`, `:583`, `:680`, `:937`. Missing legacy generation withholds re-adoption. |
| Agent reconciliation / supervisor | Both inspect G's current pointer B; they do not enumerate every historical session belonging to G. Same file `:1007`; `server/Application/Services/AgentSupervisorService.cs:151`, `:188`, `:634`. |
| Census backstop | “Unclaimed” here means absent from `AgentSessions`; a listed host with a live child also need not create a host surplus. `SessionReconciliationService.cs:1088`, `:1094`, `:1100`. The OS classifier excludes an ordinary live non-pool session from zombie candidates: `server/Infrastructure/Agents/ZombieCensusClassifier.cs:165`, `:206`. |

The no-kill behavior is deliberate safety policy, not sufficient evidence of a
defect by itself: `SessionReconciliationService.cs:31`, `:552` and
`docs/session-runtime-invariants.md:1032` explicitly forbid deriving kill
authority from “unclaimed.” The existing test
`tests/Antiphon.Tests/Application/SessionReconciliationServiceTests.cs:921`
seeds a Failed A, moves its agent pointer away, and expects re-adoption to Running
with an unclaimed warning and no pointer theft. That test was inspected, not run.

## What later changes cover, and what they do not

1. **Standing continuity (CARD-0466) changes the creation side.** The old
   `4909bbf` supervisor chose Fresh once the failure threshold was reached
   (`AgentSupervisorService.cs:194` at that historical revision); the old
   `AgentControlService.cs:311`/ `:339` created a new ID when resume was
   bypassed or unavailable. This makes a historical supervision replacement
   possible, not proven for A/B.

   Current supervision always requests `Fresh: false`
   (`server/Application/Services/AgentSupervisorService.cs:267`, `:283`).
   Automatic Fresh/history selection is refused
   (`server/Application/Services/AgentControlService.cs:160`).
   Cardless local standing starts inspect the current source and chosen target
   against the runner and refuse a still-listed live process
   (`:498`–`:515`), then recheck pointer/launch ownership under reservation
   (`:532`–`:539`). A compatible Claude resume reuses the same session ID
   (`:581`–`:624`); missing or incompatible history holds instead
   (`:837`–`:885`). This protection entered in
   `344125472eb0ac16ca0d220fcbaa7e44c5272d1e`, with infrastructure handling in
   `df4fcc9d2f7c3b1d274fe7bb067d7fafbf43139f`.
   It checks source/current and target, not a third, already-unpointed A.
   This investigation does not claim exhaustive prevention across card launches,
   manual attachment, remote placement, or every race.

2. **CARD-0691 adds visibility, with an ownership exception.**
   `server/Application/Services/AttentionService.cs:278` calls the leak
   projection. `AttentionService.Leaks.cs:128`–`:154` warns after ten minutes
   for a live, non-ended, cardless session with no open task, no agent pointer,
   and **null StandingAgentId**. Thus the old claim that every such row is
   completely invisible is stale for that eligible population.

   A non-null `StandingAgentId` excludes the row before current pointers are
   examined (`:133`). This field is explicitly a **historical** physical owner,
   without a cascading FK (`server/Domain/Entities/AgentSession.cs:7`).
   New standing launches stamp it (`AgentControlService.cs:645`), and migration
   `server/Migrations/20260909204339_StandingSessionContinuity.cs:83`–`:103`
   can backfill earlier sessions from task/incident evidence as well as pointers.
   Therefore a prior standing session can retain the stamp after its current
   pointer has moved, and can miss this warning. Whether reporter A received
   that stamp is unknown. The projection's exclusion is deterministic and
   covered by the focused existing test below; it is not proof of a newly
   reproduced production leak.

3. **CARD-0502 generation fencing does not discover old ownership mismatches.**
   Its documented contract compares accepted generations for exit/re-adoption
   writes (`docs/session-runtime-invariants.md:805`); the Running mirror arm
   still does nothing. It prevents stale generation evidence from closing a
   newer life, not the continued existence of a separate old ID.

4. **CARD-0079 is not a general old-incarnation collector.**
   Discovery selects Check seats and follows their current pointer
   (`server/Application/Services/CheckCompactionContinuationService.cs:69`,
   `:899`). Its only automatic Working-stop authorization requires the
   specific silent automatic-compaction continuation episode
   (`docs/session-runtime-invariants.md:1058`). It does not select this old,
   unpointed orchestrator session.

5. **Other recent release/liveness work does not close this case.**
   Standing boot stalls are detection only
   (`docs/session-runtime-invariants.md:862`–`:878`).
   Terminal seat release remains dormant by default (`:100`, `:170`) and
   explicitly returns `StandingOwner` for a stamped session
   (`server/Application/Services/TerminalRunnerSeatReleaseService.cs:900`).
   CARD-0181's exact-claim displacement fixes transcript theft
   (`src/Antiphon.SessionRunner/TranscriptClaimRegistry.cs:78`), not process
   lifetime.

## Focused verification

Verified unchanged source `6ec71d427574f3a1b74bfbe2042e0ea9a819d191`
on Linux. One existing test method was executed, with four argument cases:
**4 passed, 0 failed, 0 skipped**; run duration 1m 18.610s.

The first gated build omitted the documented Linux `UseAppHost=false` property
and failed in `tests/Antiphon.Tests/Antiphon.Tests.csproj:126` with
MSB3027/MSB3021: the extensionless `fakeclaude` apphost occupied the path needed
for the staged `fakeclaude/` directory. This was an invocation error on the
unchanged baseline, not a source change or a failing assertion. The explanation
and Linux setting are in `docs/testing-and-build.md:452`. Its generated output
was removed before the corrected build; no staging/source repair was made.

Corrected build: exit 0, **1 warning, 0 errors**, 1m 18.98s. The warning was
MSB3277 for the fixture's EF Relational version conflict. The second build was
needed solely to apply that documented Linux setting. Exact successful commands:

```sh
pwsh -NoProfile -File scripts/build-slot.ps1 -Label card0188-investigate-linux-build -SlotWaitMinutes 3 -- dotnet build tests/Antiphon.Tests --property:OutputPath=bin-card0188-05080662/ --property:UseAppHost=false --nologo --verbosity quiet
pwsh -NoProfile -File scripts/build-slot.ps1 -Label card0188-investigate-test -SlotWaitMinutes 3 -- dotnet run --project tests/Antiphon.Tests --no-build --property:OutputPath=bin-card0188-05080662/ --property:UseAppHost=false -- --treenode-filter '/*/*/AttentionServiceTests/a_live_session_an_agent_names_is_not_unowned' --report-trx --report-trx-filename test.trx --results-directory /work/worktrees/task-05080662/.antiphon/card0188-05080662-test
```

Successful build lease: `9eb2412c-335d-483a-8aad-1343283cdaa3`,
maxcpucount 6, waited 60s, held 81s. Test lease:
`87d6941b-5fa5-4af1-a37a-fa5e3cb4e24f`, held 87s.
Generated build/test logs and TRX remain gitignored under
`.antiphon/card0188-05080662-test/`; TRX SHA-256:
`356329a4ebbf63f3ce66449d616ebd0e2c83b5254fd02b0ed369a7227ec7183c`.
All 28 producer-owned `bin-card0188-05080662/` directories were removed after
the run. No broader suite was run.

The focused test first removes the current pointer and observes SessionUnowned,
then sets only `StandingAgentId` in its “standing” case and observes no
SessionUnowned item. Its isolated PostgreSQL fixture uses a fake runner;
it launches no provider or live session. Source:
`tests/Antiphon.Tests/Application/AttentionServiceTests.C691.cs:245`,
`:254`, `:259`, `:328`, `:369`.
The fixture uses Codex and a pool-shaped agent; the production exclusion is
provider-independent. This proves the field predicate, not the entire historical
Claude supervision sequence. No new test or production code was written.

## Remaining uncertainties and the evidence that resolves them

- Obtain A and B's historical `AgentSessions` rows, including Status,
  StartedAt, EndedAt, FailureReason, TerminationSource, and any available
  generation/standing-owner fields. Capture later migration/current values
  separately from values at the incident; the modern fields did not all exist
  then.
- Obtain G's pointer transition evidence and supervisor incidents/server log
  around B's creation, `2026-08-21T08:58:43Z`: Crash, RestartScheduled,
  StartFailure, the “supervised restart attempt … (fresh conversation/resume)”
  log, and any manual Start/Stop request. A current pointer alone is not history.
  Historical automatic Fresh and operator-initiated replacement remain distinct
  live explanations.
- Obtain A's launch failure/kill outcome and runner adoption/exit history.
  An absent or failed cleanup is different from intentionally retained work;
  PID existence and transcript misbinding do not decide that intent.
- Establish whether the old process still exists on the reporter's machine,
  and whether its current ownership/Attention projection falls into the
  unstamped warning population or the stamped exclusion. No current production
  leak or deployment of the investigated baseline was certified here.

Until those records identify the transition, “supervision leaked it” is a
hypothesis. The confirmed current facts justify keeping the card open for a
bounded evidence follow-up; they do not justify automatic reclamation or a
claim that later commits completely fixed the original incident.

## Not done, noted

If the reporter evidence confirms an unwanted superseded standing process, the smallest candidate is an operator-visible ownership-mismatch warning with a deterministic A-running/B-current fixture, preserving the no-unclaimed-kill rule; design and implementation are deferred.

--- next stage ---
next: investigate
handoff: Retrieve reporter-side A/B AgentSessions state and G's pointer/launch/stop evidence around 2026-08-21T08:58Z; distinguish automatic Fresh from manual replacement and determine whether A now carries StandingAgentId. Current retention and warning exclusion are documented; historical replacement cause is unconfirmed.
artifact: docs/investigations/2026-10-19-card-0188-leaked-incarnation.md
