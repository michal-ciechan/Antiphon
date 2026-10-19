# CARD-0242: activation gap

**Confirmed:** the standing `server2` runner advertises source that drops Codex
`turn_aborted`, although the ingestion fix is already on master and the selected
RC tip. The first loss is reconstructable at the runner normalizer. No new defect
in the fixed source was established. A particular live interrupted turn was **not**
confirmed; the desktop runner's build SHA also remains unobserved from this mirror.

Date: 2026-10-19. Investigate task `c2119d6d-2c27-4a69-bb83-f1df55a553a0`.
Assigned branch `feat/card-task-c2119d6d`, base
`d5dec9555bd726ad11bb30073c1733d6b3e7bb0e`. Card
`d037e481-5787-43b1-ba5d-dc6561797eca`, Antiphon board
`8988ca03-7414-47ad-b0b6-51556c701703`.

The commissioned plan was read in full from fetched commit
`c1985f8656bc7e0e1017a8c5813714459471df0e`,
`docs/superpowers/plans/2026-10-19-card-0242-codex-turn-abort-plan.md`.
This report changes documentation only. Production inspection used GETs; no
session creation, input, interruption, container exec, restart, deployment, drain,
RC promotion, provider-history access or deletion was performed.

## Publication and source inclusion

`GET /api/agent-tasks/e576977a-4b57-4240-942b-d8031000c9a7` returned the
structured landing row below, independently re-reading the plan's claim:

```text
operationId=ab646321-da53-485c-84f6-71a00cc5b1c9
publication=Landed cleanup=Complete destinationRef=refs/heads/master
sourceSha=1224f48867daf326d9e70b4c8832d5e04e0403f2
verifiedSha=f5df4d3f6eab8ef298eafb3f79e228f215c46bc4
remoteSha=f5df4d3f6eab8ef298eafb3f79e228f215c46bc4
remoteConfirmedAt=2026-10-17T10:47:24.748826Z
reviewEvidenceId=96ee2028-a8a0-4364-8a88-926ca43a4109
```

For each row below, executed
`git merge-base --is-ancestor f5df4d3f6eab8ef298eafb3f79e228f215c46bc4 <sha>`
and inspected `git show <sha>:src/Antiphon.SessionRunner/CodexTranscriptNormalizer.cs`.
Exit 0 means the fix is an ancestor; exit 1 means it is not. Source inspection also
rules out an equivalent arm being present without that ancestry.

| Observed source | Full SHA | Ancestry exit | Abort arm |
|---|---|---:|---|
| Fetched master / investigation base | `d5dec9555bd726ad11bb30073c1733d6b3e7bb0e` | 0 | Present |
| Fetched `rc/2026-10-18` | `df6b422c71b0828b4bd7f855a86e2a51d1987e79` | 0 | Present |
| Standing server2 advertised source | `09e48c5d6f1d404cfea8ec0d992270424fd2a184` | 1 | Absent |

The normalizer is byte-identical between these master and RC tips (`git diff
--exit-code` for that file returned 0). Fixed-source references below use the
investigation base. Old-runner references explicitly use `09e48c5d...`.

## Runner identities, observed separately

Initial `runner-drain.ps1 status -RunnerId server2` and `... -RunnerId server2-temp`
reads agreed with the later HTTP snapshot. All final snapshot GETs returned 200
at **2026-10-19T15:08:25.289Z–15:08:25.345Z**.

| Process | Availability / admission | Advertised build SHA | CARD-0242 source assessment |
|---|---|---|---|
| Desktop production runner, Windows, port 17204 | Catalogue: available, eligible, accepting | **Unknown**: catalogue omits build identity; direct capability read unavailable here | Cannot establish inclusion or need for activation |
| Standing `server2`, Linux | Available, eligible, accepting; not draining | `09e48c5d6f1d404cfea8ec0d992270424fd2a184` | Missing fix; activation gap confirmed |
| `server2-temp` | Unavailable, ineligible, not accepting; drained and retired | `null` | No current advertised source to compare; cannot credit it with either runner's build |
| Desktop Antiphon **server**, `/api/version` | HTTP 200 | `df6b422c71b0828b4bd7f855a86e2a51d1987e79` | Fixed source, but this is not either runner's identity |

Essential returned fields from `/api/session-runners/server2/status`:

```json
{"runnerId":"server2","runnerStoreId":"f519bd08-e53a-47d1-adb1-2ab33475446f","processBootId":"0e14a3d4-ba81-4ae9-a956-515188b82233","epoch":1,"available":true,"dispatchEligible":true,"lastHeartbeatUtc":"2026-10-19T15:08:11.5691466+00:00","buildVersion":"09e48c5d6f1d404cfea8ec0d992270424fd2a184","acceptingNewWork":true,"draining":false,"sessions":5,"queuedTasks":0,"runnerSessions":5}
```

Essential returned fields from `/api/session-runners/server2-temp/status`:

```json
{"runnerId":"server2-temp","runnerStoreId":null,"processBootId":null,"epoch":null,"available":false,"dispatchEligible":false,"buildVersion":null,"acceptingNewWork":false,"draining":true,"redirectTo":"server2","retireWhenIdle":true,"retiredAt":"2026-10-18T11:45:09.071621+00:00","retireReason":"idle","sessions":0,"queuedTasks":0,"runnerSessions":null}
```

`GET /api/runner-defaults` returned revision 2, `globalRunnerId=server2`, and
`kindDefaults=[]`. Both available runners reported Codex CLI `0.160.0`; this is
launcher evidence, independent of the Antiphon build.

The catalogue projection has no build field
(`server/Application/Services/SessionRunnerCatalogue.cs:135`). In particular,
`GET /api/session-runners/desktop/status` returned the phone-home desktop sentinel:
`available=false`, `buildVersion=null`, `disconnectReason=desktop`. That is explicitly
constructed by `server/Infrastructure/Agents/SessionRunner/PhoneHomeRunnerDirectory.cs:651`,
not evidence that the desktop process is down. Its catalogue row was available.
The remote status build is read from the registered runner capabilities at `:697`.

At 15:04:21Z, a direct GET to the API hostname on port 17204 `/capabilities` timed
out after ten seconds; the short hostname `desktop` failed DNS resolution. The
remaining desktop measurement is the documented **read-only** command on that host:
`(Invoke-RestMethod 'http://localhost:17204/capabilities').build`, followed by the
same ancestry/source check. No desktop activation assertion is made without it.
The build identity comes from the loaded assembly's informational version, not
checkout HEAD (`src/Antiphon.SessionRunner/RunnerBuildIdentity.cs:13`).

One local `127.0.0.1:8080/capabilities` diagnostic also returned `09e48c5d...`,
assembly-write time `2026-10-16T17:53:21.214542Z` and process-start time
`2026-10-18T00:32:49.4336593Z`. Its store ID was
`897f354b-0f0d-4032-93a3-d3addffccbe2`, different from the phone-home identity;
it is not substituted for the standing runner's status or historical session owner.

## Why publication did not activate the fix

The advertised revision's Git commit is dated 2026-10-16T17:21:50Z, before the
October 17 ingestion fix. An October 18 rollout result still names that revision,
the same standing store/boot IDs and its image. See immutable commit
`73af32ba70b67f661485858b42e8836a37a6b87f`,
`docs/investigations/2026-10-17-rc-rollout-finish.md:140` and `:171`.
Thus a rollout marked complete in the drain reason does not mean this newer fix
was activated.

The current RC rollout is separately gated. The stored Fixture investigation at
`a1a6f84939da1489fea48a336493816385d8b1aa`,
`docs/investigations/2026-10-19-rollout-fixture-gate.md:19`, binds the attempted
source to `df6b422c...`. At `:42` it records that the requested new image tag was
absent and Fixture used the retained `09e48c5d...` image. Its retained gate result
was `FixtureSeedFailed`, exit 2 (`:20`), and native acceptance remained outstanding.
These are that investigation's observations, not new container inspection here.

The outstanding CARD-0242 activation is therefore on **standing server2**, from
its observed old build to a caller-qualified build containing the existing fix;
the named RC `df6b422c...` already contains it. Temp has no live build to certify,
and the desktop verdict requires the read above. This report neither changes the
ongoing rollout's gates nor commissions any release action.

## First missing boundary: reconstructed mechanism

At advertised source `09e48c5d...`,
`src/Antiphon.SessionRunner/CodexTranscriptNormalizer.cs:170` switches on
`payload.type`. Its recognized types end with `task_complete` at `:177`, followed
by `_ => []` at `:178`. There is no `turn_aborted` arm.

For the existing persisted-shape input in
`tests/Antiphon.SessionRunner.Tests/CodexTranscriptNormalizerTests.cs:139`
(`event_msg`, `payload.type=turn_aborted`, `reason=interrupted`), that old switch
returns **zero parts**. This is a source reconstruction using the committed test's
synthetic row, not a new live capture or an executed test in this investigation.
At the same old revision, `CodexTranscriptTailer.cs:376` calls that normalizer;
only its returned parts enter the loop at `:384`, receive a sequence at `:390`,
and publish at `:397`. Therefore the first absent boundary is **raw rollout to
runner-normalized transcript**, before server persistence or queue processing.

At fixed source, the arm is at `CodexTranscriptNormalizer.cs:182` and
`FromTurnAborted` at `:366` emits `TurnEnd` with `StopReason=cancelled` (`:374`).
That ends session working state (`TranscriptWorkingState.cs:41`;
`server/Infrastructure/Data/TranscriptWorkingStateQuery.cs:16`) and is a queue
boundary (`server/Application/Services/AgentSessionRuntime.cs:506`). It is excluded
from report completion (`src/Antiphon.SessionRunner.Contracts/SessionRunnerContracts.cs:474`).
An open task after a valid cancelled boundary is not, by itself, this ingestion bug.

The original Code/Review reports were re-read from the card thread. Code records
1 failed single-item assertion before the fix, then 1 passed witness. Review
`f113cf09-4990-4eee-9105-aeed8f2e8793` records this exact historical checkpoint:

```text
CHECKPOINT CP-1 commit=1224f48867daf326d9e70b4c8832d5e04e0403f2 build=ok filter=/*/*/CodexTranscriptNormalizerTests/* executed=17 passed=17 failed=0 skipped=0 trx=/work/worktrees/task-f113cf09/.antiphon/review-f113cf09/CP-1-20261017-103331-ab64/run.trx slot=granted waited=0s dirty=0 source=1224f48867daf326d9e70b4c8832d5e04e0403f2 sourceState=clean buildSource=verified
```

This preserves reported provenance; the old receipt files were not independently
retrieved and no result is attributed to today's source or production process.

## Existing transcript evidence and its limit

The card description and thread provide no specific interrupted Codex incident ID.
A board-scoped task metadata read with `since=2026-10-12T00:00:00Z` returned 1,019
rows, including 484 Codex rows; none had a positive `interruptWritten` or
`refinementDelivered` value. A failed/canceled/blocked subset contained 50 Codex
rows. The 19 candidates with sessions and completion on/after October 15 were
inspected for failure and interruption events. Task cancellation, readiness
failure and a role ceiling are not evidence of a provider `turn_aborted` event.
This is a bounded search, not proof that no interrupted conversation ever existed.

Five identified server transcripts were read through
`GET /api/sessions/<id>/transcript?since=0`, all HTTP 200. This endpoint reads
persisted `TranscriptEntries`, ordered by sequence, after best-effort live catch-up
(`server/Application/Services/AgentSessionService.cs:2235`). Counts below equal
the returned `lastSequence`; there was no response truncation.

| Session ID | Selection / stored evidence | Observed transcript |
|---|---|---|
| `df4882b3-214a-4868-919e-c7c855bac220` | CARD-0242 Plan on server2 | 8 rows; final #8 `TurnEnd/end_turn`, 2026-10-19T15:00:02.418Z |
| `f04cd114-18d9-4cf0-b71e-3ef581f9261a` | Named Codex agent's only returned history row | 3 rows; #3 `TurnEnd/end_turn`, 2026-08-25T18:49:18.301Z |
| `a14ae97e-8c87-40ad-b9f1-79b4b44b64f3` | Server2 task `04228bff-e4a8-4ecd-bdbb-9315c2e41395`, refinement mentioned in failure reason | 33 rows; prompts #1 and #30, only turn end #33, `end_turn` |
| `71606d5b-2423-42dc-9e33-432a1d76068c` | Desktop task `8aa5fb3c-2f0b-42f8-adb4-9ece8d838ea4`, role-ceiling failure | 23 rows; #14 and #23 both `TurnEnd/end_turn`; no cancelled row |
| `e41748c6-2e9c-4a53-9c48-86210b4b98f9` | CARD-0907 readiness incident, task `405d59ce-d622-4ae9-9fa9-1a3a4157b505` | 0 rows; recorded failure was “Agent process did not become ready.” |

The closest candidate, `a14ae97e...`, has these unchanged metadata values:

```text
#29 AssistantText 2026-10-15T12:21:00.127Z uuid=msg_05b3a16089e64983016ad0c52963148191b43ba5226dbbe568
#30 UserPrompt    2026-10-15T12:24:36.489Z uuid=01a13f85-8188-7440-a61c-bce77b22e8b3
#33 TurnEnd       2026-10-15T12:28:37.753Z stopReason=end_turn apiCallId=01a13f49-7335-72d1-af2d-2a5247f94976
```

No turn end precedes #30 in that response. A second prompt can also be steering
within a turn: **this does not prove an abort was written or dropped**. Its bound
raw row, historical runner build and generation were not available. The local
HTTP runner returned 404 for this old session, which also cannot certify absence
from a different runtime or retained server records.

Earlier raw evidence has a documented availability limit. Task
`e59b0f15-a398-45b1-90fb-cd1ed9e95ec9`, completed 2026-10-19T08:11:14.126202Z,
reports deleting 1,368 server2 rollout files whose latest day was October 16.
Immutable source: `3a2e2ce2168e76595f27d7db5cb4c62943f8abb3`,
`docs/investigations/2026-10-19-codex-history-wipe.md:31` and `:33`. That includes
the candidate's date window. No surviving copy was identified in this task.

CARD-0907 captures were reused as evidence, without rerunning them:

- `c5543c29839e9c9d6d19e012daedb927551a78af`,
  `docs/investigations/2026-10-18-codex-0160-parsers.md:15`: successful exec rollout;
  `:16`: interactive draft did not submit; `:30`: no `turn_aborted` emitted.
- `87fad6edb6e8bc80ff27c25419b869f7d84e2aac`,
  `docs/investigations/2026-10-18-codex-0160-ready.md`, “What was run”: no prompt
  sent. Those readiness frames cannot establish interrupted-turn ingestion.

## Remaining uncertainties and resolution evidence

1. **Desktop build:** obtain its local `/capabilities` build and compare its exact
   SHA with the landed fix. Server `/api/version` and the catalogue cannot answer it.
2. **A specific interrupted turn:** a retained native `turn_aborted` row tied to
   session, turn ID, timestamp and runner generation is needed, alongside the
   runner-normalized and server-persisted rows. The current candidates lack that
   raw witness. If none survives, only a separately commissioned owned capture can
   establish the live chain; this task authorizes no new model turn.
3. **Activation acceptance:** today's inventory proves the old source remains
   advertised on server2. It does not verify an eventual new build or its live
   abort handling. The ongoing rollout's owner retains that acceptance obligation.

Ignored supporting observations are under `.antiphon/card-0242-activation/`:
`observations.json` SHA-256
`2d13ff879c301dba923b700e97addd60a4a8e4c3d38a32e9121158f96873c16f`;
`transcript-metadata.json` SHA-256
`3aa1ea346157661e8fd63a86835783a02c171824ce134391b04ab69dcfe1127e`.
The essential observations are preserved above because ignored runtime evidence
is not delivered by Git. Transcript bodies and credentials are not committed.

No build or test was run for this evidence-only task. Source inclusion and the
closed-switch reconstruction establish the activation mechanism; they do not
claim a successful production abort canary or complete fleet activation.

## Not done, noted

The existing ingestion fix requires activation on standing server2 through the caller-owned rollout; no additional source fix is established or designed here.

--- next stage ---
next: none
handoff: CARD-0242 source fix already exists on master and RC df6b422c; standing server2 still advertises 09e48c5d without it. Return this activation evidence to the ongoing rollout owner. Desktop SHA and a matched raw-abort witness remain unverified; no duplicate Code or release action is commissioned.
artifact: docs/investigations/2026-10-19-card-0242-activation-gap.md
