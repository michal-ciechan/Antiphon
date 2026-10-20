# CARD-1322 I1: Codex turn_aborted ordering

**Incomplete measurement; replacement ordering remains unconfirmed.** One native
`interrupted` trace and one successful native control were inspected. Neither
contains an abort followed by completion. The requested server2-temp native store
was inaccessible from this checkout. There is no observed counterexample to S1-S4
in this sample, but it cannot qualify their behavior for replacement turns, late
usage, or a repeated turn ID across boundaries.

Investigation date: 2026-10-20 UTC. Source under examination:
`a8888786b2034aaa15ebb8ee08b04ee5569cbfdd` (S4 tip), on
`feat/card-task-d310a8e5`. All source references below refer to that SHA.
The task brief narrows the plan's I1 to existing records: no test runs, live
interruptions, provider calls, or runner changes were performed. Only the report
and ignored, sanitized evidence in this assigned worktree were written.

## Access and provenance

The documented command
`ssh -o BatchMode=yes -o ConnectTimeout=8 mc@server2 'docker ps --format "{{.Names}}"'`
returned `Host key verification failed.` No host-key verification was disabled,
trust file changed, or alternate credential read.

The delegate already runs as uid/gid `1654:1654`, verified by `id` and
`process.getuid()`. Additional local native reads used that identity directly,
inside the assigned container; they were **not** remote `docker exec` reads of
server2-temp. Only `rollout-*.jsonl` below `/state/codex/sessions` were opened.
No provider credential file was opened or printed. Output and saved extracts
exclude prompt, answer, tool-input, and tool-output content.

The distinction between stores was checked rather than inferred:

| Read | Observed identity |
|---|---|
| Local container `hostname` | `328f856108e4` |
| Local `GET http://127.0.0.1:8080/capabilities` | `runnerStoreId=7f97168e-3831-4713-99ba-96ac2352ebac` |
| Server `GET /api/session-runners/server2-temp/status` | `runnerStoreId=aa441996-a9c4-429c-8409-9398b7888ec9`; `processBootId=8a0cfc87-adad-4085-ba63-3714f788787f` |
| Server `GET /api/session-runners/server2/status` | `runnerStoreId=f519bd08-e53a-47d1-adb1-2ab33475446f` |
| Server `GET /api/version`, local capabilities, and temp status | build `81cfb267a0af5c0ddb65e8b238af355067bee9cc` |

The local store matches neither catalogued store ID. Matching build SHAs do not
establish matching transcript contents. The running build includes the original
abort normalization arm but predates this S1-S4 source tip; live observations
therefore cannot be represented as executions of S1-S4.

At `2026-10-20T06:02:56.285Z`, the local scan completed over 103 rollout files,
under dates 2026-10-19 and 2026-10-20. Exactly one parsed
`event_msg/turn_aborted` was found. Its file has 19 complete lines and no parse
errors. These counts describe this local store only. A metadata-only filename
search for the two native rollout IDs below in local runner JSON sidecars found
no binding; no Antiphon session identity is claimed for either native file.

## Native orderings

Both files identify `cli_version=0.160.0`, `originator=codex-tui` in line 1.
This is retained CLI metadata, not an independently authenticated account or
launch receipt. All line numbers are physical, one-based JSONL lines; the
reported native ordinal is zero-based.

**A — interruption**

File:
`/state/codex/sessions/2026/10/19/rollout-2026-10-19T15-13-40-01a154b9-b8c9-73a3-9852-8533d26c3b95.jsonl`

SHA-256: `3ca35a96874f151359086c7382fd24bea55071c2c22fc69678451755f4751f68`;
50,664 bytes, 19 lines.

Let `A = 01a154b9-be10-7431-bffc-9715805d53a8`. Every identified turn-bearing
event in the table has this same ID. Times are UTC on 2026-10-19.
Token tuples are `(input, cached input, cache write, output)`.

| File:line / ordinal | Time | Native event | Turn ID / usage |
|---|---|---|---|
| A:2 / 1 | 15:13:41.400 | `task_started` | A |
| A:10 / 9 | `15:13:41.811` | `item_completed/UserMessage` | A |
| A:11 / 10 | `15:13:43.436` | `item_completed/AgentMessage`, `phase=final_answer` | A |
| A:14 / 13 | `15:13:43.445` | `token_count` | ID absent; total = last = `(14597,12160,0,5)` |
| A:16 / 15 | `15:13:43.456` | `item_completed/UserMessage` | A |
| A:17 / 16 | `15:13:46.398` | `token_count` | ID absent; total = last = `(14597,12160,0,5)` |
| A:19 / 18 | `15:13:46.413` | `turn_aborted`, `reason=interrupted` | A |

The second UserMessage is before the abort and carries the existing turn ID;
there is no second `task_started` and no `reason=replaced`. It does **not**
establish a replacement turn. The last token snapshot precedes the abort by
15 ms and repeats the earlier snapshot exactly. The file ends at the abort.
There is no next `token_count` or `task_complete`, so no post-abort ordering or
abort-to-completion ID reuse is observable. An earlier final-answer item is
not a successful completion boundary.

**B — successful control, without an abort**

File:
`/state/codex/sessions/2026/10/19/rollout-2026-10-19T10-52-11-01a153ca-551f-7c12-b117-15514fa6dcbf.jsonl`

SHA-256: `a7cccab6438e0772c7c8f6b8c6e86c8c7391b614ce563da00fe9ee41c5046ead`;
2,699,537 bytes, 142 lines.

Let `B = 01a153ca-5f13-7400-b413-f16654c9f6b1`.

| File:line / ordinal | UTC time, 2026-10-19 | Native event | Turn ID / usage |
|---|---|---|---|
| B:2 / 1 | 10:52:13.990 | `task_started` | B |
| B:10 / 9 | 10:52:15.079 | `item_completed/UserMessage` | B |
| B:17 / 16 | 10:52:19.667 | first `token_count` | ID absent; total = last = `(20736,12032,0,85)` |
| B:138 / 137 | 10:58:29.781 | `item_completed/AgentMessage`, `phase=final_answer` | B |
| B:141 / 140 | 10:58:29.807 | last `token_count` | ID absent; total `(1250974,1148032,0,13967)`; last `(112172,111488,0,642)` |
| B:142 / 141 | 10:58:29.827 | `task_complete` | B |

Intermediate activity/count rows are omitted from this table, not from the
inspection. This file has exactly one completion and no abort. Its last token
snapshot precedes completion by 20 ms. It is a successful-turn control, not a
replacement control.

## Stored Antiphon transcript reads

Used the documented `ANTIPHON_API` base and task-token header without printing
the token. `GET /api/agents` supplied Codex agents' non-null
`persistentSessionId` values; each was read through
`GET /api/sessions/{id}/transcript?since=0`.

| Selection | Successful reads | Returned rows | Cancelled boundaries |
|---|---:|---:|---:|
| 294 candidate agents updated on/after 2026-10-13 | 294 | 6,384 | 0 |
| 1,058 additional candidates, first pass | 1,044 | 20,428 | 0 |
| Retry of the 14 HTTP 502 responses | 14 | 427 | 0 |
| Total, excluding separate live/control rereads | 1,352 | 27,239 | 0 |

Three successful responses were empty. Separately, all six live Codex sessions
were readable, with zero cancelled boundaries. The first-pass captures finished
at `06:04:10.741Z` and `06:04:46.802Z`, respectively. All 14 failed reads
succeeded on one subsequent retry.

One exact stored-row control, session
`52396569-a63b-40d0-80c3-478e3da56371`, response `lastSequence=9`:

| Sequence | Kind | Timestamp UTC | ApiCallId | StopReason | Input / cache read / cache creation / output |
|---|---|---|---|---|---|
| 1 | `UserPrompt` | `2026-10-01T22:23:39.834Z` | null | null | null / null / null / null |
| 9 | `TurnEnd` | `2026-10-01T22:38:50.018Z` | `01a0f990-ebcb-7343-8d6a-4b084daba135` | `end_turn` | 189335 / 4063360 / 0 / 26723 |

Sequence 9 UUID is `01a0f990-e1f8-7e40-a038-ab26fa13dae0#249`.
This is an API-returned stored row, not a claimed native line from file A or B.

The source query orders all retained rows after `since` by Sequence and has no
row cap (`server/Application/Services/AgentSessionService.cs:2245`). It first
attempts live synchronization. This sample is **not** all historical sessions:
an agent's persistent session is not its complete session history, retention
can remove records, and missing native events cannot be inferred from missing
normalized rows. A history request for live Codex agent
`bc49c6fd-8e75-424e-be28-182b818f7891` returned `items=[]`, `nextBefore=null`.
No server ingest/backfill ordering involving an abort was available to measure.

## Reconstruction against S1-S4

This section applies the inspected source to the stored native values. It is
arithmetic/source reconstruction, **not** a test run or observed execution of
the new build.

`CodexTranscriptNormalizer.cs:177` ignores `task_started`, maps UserMessage
and AgentMessage items, absorbs token counts, and emits boundaries.
Its cumulative baseline is initialized to `total-last` at `:305`; consumption
at `:322` subtracts cached input and resets the usage flag and baseline.
Both boundary arms consume it (`:341`, `:374`).

For A, the baseline is zero. Duplicate snapshots at A:14 and A:17 leave the
same cumulative value. A:19 therefore emits exactly:

```text
Kind=TurnEnd StopReason=cancelled ApiCallId=01a154b9-be10-7431-bffc-9715805d53a8
InputTokens=2437 CacheReadTokens=12160 CacheCreationTokens=0 OutputTokens=5
Uuid=01a154b9-b8c9-73a3-9852-8533d26c3b95#18
```

The one-based normalized sequence from an otherwise empty mirror would be:
A:10 → UserPrompt 1, A:11 → AssistantText 2, A:16 → UserPrompt 3,
A:19 → TurnEnd 4. Before A:10 the runner verdict is Unknown; through A:10,
A:11, A:14, A:16 and A:17 it is Working; at A:19 it is Idle.
This follows `src/Antiphon.SessionRunner/TranscriptWorkingState.cs:35`.
There is no observed subsequent activity with which to demonstrate false idle.

For B, baseline is also zero. B:142 would carry Input=102942,
CacheRead=1148032, CacheCreation=0, Output=13967. Each file has only one
usage-bearing boundary for its turn ID, so the per-ID maximum equals the sum
of boundary usage for that ID.

| Slice / assumption | Result for these observed orders |
|---|---|
| S1 consumes observed usage once per boundary | Holds for A's duplicate pre-abort snapshots and B's pre-completion snapshots. No late or replacement usage was observed. |
| S2 cancellation is not capacity progress | A emits exact `cancelled`; the gate at `server/Application/Services/CapacityRecoveryService.cs:739` rejects it. This is a source deduction, not a captured durable transition. |
| S3 preserves the interrupted session/attempt | A's cancelled verdict takes AwaitingInput at `server/Application/Services/AgentSessionService.cs:283`, before success/timeout. Actual run-attempt state was not measured. |
| S4 abort cannot become screen success | If A's abort is past the submitted baseline, the first TurnEnd at `server/Infrastructure/Agents/SessionRunner/RunnerCodexAdapter.cs:295` returns cancelled/false before screen fallback (`:249`). A replacement prompt overlapping an older abort remains unqualified. |

The full normalizer path for the preceding paragraph is
`src/Antiphon.SessionRunner/CodexTranscriptNormalizer.cs`.

`server/Application/Services/DelegationUsageRollup.cs:29` requires non-null
InputTokens within the task's timestamp window. At `:40` it groups non-null
ApiCallId rows and takes a maximum **per token field**, then sums groups at
`:67`. Thus its correctness for two usage-bearing boundaries with the same ID
cannot be inferred from correctness for A and B. If such boundaries represented
distinct positive deltas in the same task window, a maximum would not generally
preserve their sum. That is a conditional mechanism visible in source; this
investigation did **not** find the native abort/later-completion witness needed
to call it a confirmed defect. No synthetic witness is substituted for a real
ordering, and no new bug-card text is supplied.

## Remaining uncertainties and what resolves them

- **Next event after abort and genuine replacement order:** obtain existing
  server2-temp rollouts containing `turn_aborted` through a host with trusted
  server2 SSH. Use `docker exec -u 1654:1654` in the temp container and only its
  Codex sessions subtree. Retain file identity, line/ordinal, event type, turn
  ID, reason, timestamp, and token counts; omit all content.
- **Turn-ID reuse across abort/completion:** find an abort followed by completion
  in the same native file, retaining intervening starts, messages and counts.
  The two UserMessages with ID A do not resolve this question.
- **Rollup correctness for that order:** retain the cumulative baseline preceding
  each boundary and the task's dispatch/settlement window, then compare the
  reconstructed boundary deltas with the per-ID maxima. Positive later deltas
  must be observed, not assumed.
- **Server ordering and false idle:** bind the native file to its Antiphon session
  and fetch that stored transcript. This run found no binding for A/B and no
  stored cancelled boundary. No ingest latency, overlap interval, or replacement
  working-state interval can presently be quantified.

The caller was asked for an approved route to the temp store. Without that
access or a content-redacted export made there, I1 remains incomplete. The
missing evidence does not invalidate already reviewed bounded S1-S4 behavior,
nor does this report certify the broader ordering assumptions.

## Not done, noted

No fix was designed or implemented; no tests/builds or live interruption experiments were run.

--- next stage ---
next: investigate
handoff: Continue I1 from a host with trusted server2 SSH; inspect server2-temp native rollouts as uid 1654 and bind abort/replacement sequences to stored API rows. Resolve post-abort token/completion ordering and repeated-ID usage before claiming rollup correctness.
artifact: docs/investigations/2026-10-20-card-1322-i1-turn-aborted-ordering.md
