# CARD-0188: leaked prior always-on incarnation

**Desktop follow-up, task `4d2ddeb2`: no live leaked standing incarnation found;
the original replacement trigger remains unconfirmed.** The original agent and
both session rows are absent from the desktop database, as is their retained
launch/stop evidence. Automatic Fresh versus manual replacement cannot be
distinguished, and A's former `StandingAgentId` cannot be recovered. All current
desktop Running sessions have a current agent pointer. Do not close this as
**proven superseded**: the conditional retention/visibility mechanism below is
still present in the running build. The bounded live-history investigation is
complete; the remaining question is whether to commission the narrow visibility
candidate under **Not done, noted**, or retire an unreconstructable historical
incident without claiming a proven fix. Next: **decide**.

The first-pass findings and test costs below are retained as historical evidence.
The desktop follow-up section supersedes its proposed next investigation.

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

Recommended candidate if the residual visibility gap is retained as scope: an operator-visible warning for a stamped prior A still runner-Running while G points to B, with a deterministic A-running/B-current fixture (and pointed/Exited controls) asserting the warning and zero stop calls; no design, implementation or new test was performed.

## Desktop live-history follow-up (2026-10-19, task 4d2ddeb2)

### Provenance and boundaries

Assigned branch: `feat/card-task-4d2ddeb2`; starting commit:
`f81d5ce129ceede924a17b3d5021a60a171ea672`. Read the full external brief before
inspection. The investigation made HTTP GETs and PostgreSQL SELECTs only against
the running system. No session was messaged, started, stopped, killed or patched;
no board state changed. **No tests or builds ran in this follow-up.**

At 22:07:21Z, `GET http://localhost:17202/api/version` reported
`81cfb267a0af5c0ddb65e8b238af355067bee9cc` with `land-v2`. The canonical
`C:\src\Antiphon` checkout had that same HEAD. The desktop runner's
`GET http://localhost:17204/capabilities` also reported that commit, process start
`2026-10-19T15:56:41.3474292Z`, store
`6c459598-54af-4cb9-84f7-380c578fedc1`. Comparing the first pass's source
`6ec71d427574f3a1b74bfbe2042e0ea9a819d191` to the live commit produced **no diff**
in AgentControlService, AgentSupervisorService, SessionReconciliationService,
AttentionService.Leaks, or AgentSession. Thus the relevant source reconstruction
above also describes the observed running version; it is not inferred from health.

### August history is unavailable on the desktop too

At 22:07:21.670–22:07:21.814Z these documented reads all returned **404**:

- Server `/api/agents/0c83e820-0bd5-464a-97c9-72967ce0ffa5`, its
  `/sessions?take=25`, and its `/incidents`.
- Server `/api/sessions/912eb415-daa3-4ec7-a1df-9f740da7195e/transcript?since=0`
  and `/api/sessions/eaf64b4c-dcc6-4777-8508-f4fd858dabee/transcript?since=0`.
- Runner `/sessions/912eb415-daa3-4ec7-a1df-9f740da7195e` and
  `/sessions/eaf64b4c-dcc6-4777-8508-f4fd858dabee`.

Direct read-only queries of `antiphon-postgres`, database `antiphon`, at
22:07:45.970084Z and 22:08:40.366253Z disambiguated absence from HTTP routing:

| Stored evidence selection | Rows |
|---|---:|
| Agents: G by exact ID or any name containing `Prediction` | 0 |
| AgentSessions: A/B by ID or any session stamped with G | 0 |
| AgentIncidents: G or A/B | 0 |
| AgentTasks: G as execution agent or A/B as execution session | 0 |
| AgentTasks: A/B as parent session | 0 |
| AgentTaskEvents: A/B session ID, or any Detail containing G/A/B's distinctive eight-character IDs | 0 |
| TranscriptEntries: A/B | 0 |

The DB retained 18,508 task rows and 94,545 task-event rows, with the oldest of
both dated 2026-08-09; the absence search was not restricted to a capped API
page. No matching task ID survived to retrieve through the individual task
GET/event projection. All 6,591 retained incidents were dated September 19 or
later. Incident pruning is explicitly implemented at
`server/Application/Services/AgentSupervisorService.cs:611`; its default is
30 days (`server/Application/Settings/SupervisionSettings.cs:35`).

The documented log roots contained six server Serilog files dated October 15–19
and eight runner Serilog files dated October 12–19. The 251 pty-host logs had
oldest last-write October 5. No file named for either historical session survived
under `C:\logs\antiphon\session-runner` (searched dashed and compact IDs).
Root `C:\src\Antiphon\logs` has one `antiphon-20260818.log`, predating the
replacement; its other August logs are named test logs. Searching all `.log`
files in those four roots for G/A/B found only today's GET/404 observations,
not launch, replacement or stop evidence. Rotation/retention owners:
`docs/logs.md:11`, `:13`, `:14`, `:76`.

Two exact retained runner lines independently record the missing sessions:

```text
2026-10-19 23:07:21.782 +01:00 [INF] 404: Session '912eb415-daa3-4ec7-a1df-9f740da7195e' was not found.
2026-10-19 23:07:21.814 +01:00 [INF] 404: Session 'eaf64b4c-dcc6-4777-8508-f4fd858dabee' was not found.
```

Source: `%TEMP%\antiphon-logs\session-runner-20261019.log:205150` and `:205151`.
The first pass's 21:47Z agent lookup also appears in the **desktop** server log,
`C:\src\Antiphon\server\logs\antiphon-20261019.log:377967`, at
`2026-10-19 22:47:26.031 +01:00`, followed by HTTP 404 at `:377970`.
Therefore the earlier 404s are not evidence of isolation behind a different
server2 mirror; at least that earlier lookup reached this desktop server.

**What this does and does not establish:** August incident/log/transcript history
is outside the retained windows. The exact deletion mechanism for G/A/B is
unknown. In particular, the current default session retention is 90 days
(`server/Application/Settings/RetentionSettings.cs:18`), whereas August 21 is
only 59 days before this read; do not label the missing session rows ordinary
90-day pruning. Session deletion also requires terminal/unreferenced state
(`server/Application/Services/DataRetentionService.cs:175`, `:187`). No surviving
audit proves when or why these rows disappeared or when their processes stopped.
There is **no current A row whose StandingAgentId can be inspected**, which is
different from finding a row with a null stamp. Automatic Fresh and manual
replacement remain alternative historical explanations, neither confirmed.

### Current live-incarnation check

First snapshot, 22:07:24Z: 2,654 agents, 21 desktop runner sessions, **12 Running,
9 Exited; zero Running sessions without an agent pointer**. The comparison used
the actual `persistentSessionId` field, not the DTO's optional `liveSession`.

A second snapshot at **22:10:07.2414713–22:10:09.301958Z** bracketed a full
`/api/agents` GET with two `/sessions` GETs and parsed pointer strings as GUIDs.
Both runner reads contained 22 sessions. All **13 Running** sessions retained
the same status/start timestamp across those reads and each had **exactly one**
current agent pointer. The extra live row was a newly launched pool task, not an
older standing incarnation. The remaining nine entries were Exited.

| Current owner | Runner-Running session ID |
|---|---|
| Torquay Leander | `8c1d3da6-7fad-4c7b-8897-e2c7c18914d6` |
| ClaudeBot-Antiphon | `e1626153-65bd-46eb-8257-6b67d44f6e32` |
| antiphon-check-interpreter | `cea73d57-3072-4cc2-8cb0-ab859e7d3415` |
| Antiphon-Orchestrator | `daec6a72-771d-4a66-9634-fbe474d87464` |
| school-revision | `d1880154-dd37-45d7-a705-a0ccbf4d8b03` |
| slides Orchestrator | `b788e4ec-1635-4451-8784-3067c5b17064` |
| markdown-package Orchestrator | `384d59ee-45b7-4ae4-9bc3-a271a8bee8d4` |
| Gym Stat Orchestrator | `4f71db42-71fd-4f64-b278-64a17b29b960` |
| antiphon-diagnose | `b0faf006-ad13-43ff-9ea7-843b22c32359` |
| antiphon-output-distiller | `7723afb0-2768-442b-9237-97b0dc51d2af` |
| AZ Care | `b8aad212-ca2a-4f4e-9c7e-604a0ba1f8c7` |
| task-4d2ddeb2 | `81dac530-d3af-4b77-adf9-fc226706e5d9` |
| task-adcd99e7 | `4adbe110-1bb8-4949-bfa4-ebc17bff1b2b` |

Database cross-check, 22:08:40Z, over **all rows**, with no LIMIT:

| Population | Result |
|---|---|
| Non-pool agents, including AlwaysOn-off standing agents | 23; all desktop; eight AlwaysOn |
| Stamped sessions lacking every current agent pointer | 66; all desktop |
| Their DB statuses | 21 Stopped, 45 Failed; zero Starting/Running/Stopping |
| Their stamped owner still exists | 65; one historical owner deleted |
| Their owner is currently AlwaysOn | 43: 17 Stopped, 26 Failed |
| Stamped sessions on any remote runner | 0 |

Status numbers are defined in `server/Domain/Enums/SessionStatus.cs:5` (Running 2,
Stopped 4, Failed 5). For a concrete persisted historical stamp, session
`c83e3c62-c0ec-4a12-b40d-8f083dbd8885` still names standing owner
`a392cbc4-0fc0-4603-b4d0-5198d1929718`, although that agent points at
`daec6a72-771d-4a66-9634-fbe474d87464`; the prior row is Failed, ended
`2026-10-15T07:35:54.191025Z`. This proves stamps outlive pointer replacement in
stored production rows, **not** that this prior process is live.

Remote boundary, 22:10:09Z: `server2-temp` slots returned five Running sessions,
each with an open task and `orphan:false`. `server2` status returned unavailable,
zero DB-live bound sessions and unknown runner inventory; its slots GET returned
503. All known non-pool agents and stamped histories are desktop-bound, so this
does not obstruct the observed standing-agent check. It does prevent claiming a
fleet-wide OS/process absence proof for the unavailable host. No remote restart,
deployment or process inspection was attempted.

### Remaining mechanism, recommendation and evidence custody

The live version still refuses automatic Fresh
(`server/Application/Services/AgentControlService.cs:160`) and the supervisor
still requests `Fresh: false` (`server/Application/Services/AgentSupervisorService.cs:284`).
However, the Running mirror arm still does nothing
(`server/Application/Services/SessionReconciliationService.cs:500`), and
`StandingAgentId != null` still excludes a row before the SessionUnowned pointer
comparison (`server/Application/Services/AttentionService.Leaks.cs:133`, `:144`).
The field is expressly historical (`server/Domain/Entities/AgentSession.cs:7`).
The earlier four-case test receipt remains evidence for that predicate only;
this follow-up did not execute it again or reproduce an unwanted replacement.

Recommendation: **do not close as proven superseded and do not commission an
automatic reclamation fix on this evidence**. If preserving scope beyond the
unreconstructable August incident, use the visibility candidate recorded above.
The next stage is **decide**, because the historical actor/intent remains unknown
and any ensuing design must respect that uncertainty. Another identical live
lookup will not restore the missing evidence. A retained August DB backup plus
launch/stop audit, or a newly captured A-to-B transition with DB/runner generation
and actor evidence, would resolve the original cause. No such backup was supplied
or restored during this task.

Local generated evidence is gitignored at
`C:\Antiphon\worktrees\card-task-4d2ddeb2\.antiphon\card0188-4d2ddeb2`.
The essential results are preserved verbatim or tabulated in this committed
report. Reproducible local commands (read-only; results are point-in-time):

```powershell
pwsh -NoProfile -File .antiphon/card0188-4d2ddeb2/probe.ps1
pwsh -NoProfile -File .antiphon/card0188-4d2ddeb2/final-snapshot.ps1
Get-Content -Raw .antiphon/card0188-4d2ddeb2/history.sql | docker exec -i -e 'PGOPTIONS=-c default_transaction_read_only=on -c statement_timeout=15000' antiphon-postgres psql -X -v ON_ERROR_STOP=1 -U antiphon -d antiphon -f -
Get-Content -Raw .antiphon/card0188-4d2ddeb2/current.sql | docker exec -i -e 'PGOPTIONS=-c default_transaction_read_only=on -c statement_timeout=15000' antiphon-postgres psql -X -v ON_ERROR_STOP=1 -U antiphon -d antiphon -f -
```

SHA-256 receipts for this capture (rerunning scripts replaces local JSON):

| File | SHA-256 |
|---|---|
| historical-http.json | `565225ffcb0304223724b21b0bca75ea98837a1311904fbc66bd3d89a0bd9755` |
| history-db.txt | `db01d20117bd699bcadbec1591b62377032d615647d242347757d233292a1afc` |
| current-db.txt | `57f6211c8303cd0796af062b6ce2ffa90a52738919a9430906843b8898688f06` |
| final-snapshot.json | `5ea884a337677fc4dee096c5a1bb6aede3aa97136e3ec03d1704458daa20fff0` |
| remote.json | `0b4a8cb9fc3114ab7b8142ecfdd61f8ddcdc4fdcd84deb406b1395a7e628cf11` |
| runner-build.json | `1ffd5c4d3bcb672552705a137e1e8d36e923ba7ca02f97c74ea2216736dbd8de` |

--- next stage ---
next: decide
handoff: Desktop history has no G/A/B rows or launch audit; automatic Fresh versus manual replacement remains unknown. No live standing leak was found. Decide whether to retire the unconfirmed historical incident or commission a detection-only Plan for the confirmed historical-stamp visibility gap; no automatic reclamation is justified.
artifact: docs/investigations/2026-10-19-card-0188-leaked-incarnation.md
