# CARD-0083 S1 — Grok / Codex / OpenCode usage-limit signal survey

**Date:** 2026-08-19
**Card:** CARD-0083 slice S1 (plan `docs/superpowers/plans/2026-08-18-provider-capability-contract-card-0083.md`)
**Status:** survey complete. No code changes. Quota exhaustion was not triggered.

This is the input S4 needs before it grows `UsageLimitSignal` past Claude's synthetic stub. An honest
**Unknown** is a valid cell: it marks survey debt instead of inventing a shape.

## Method

Read, then probe. In that order.

1. **Source.** `RunnerGrokAdapter` / `GrokTranscriptTailer` / `GrokTranscriptNormalizer`;
   `RunnerCodexAdapter` / `CodexResponseAnalyzer`; `RunnerOpenCodeAdapter`. None of the three
   adapters classify API errors. The Grok normalizer's `sessionUpdate` switch is
   `user_message_chunk` / `agent_message_chunk` / `agent_thought_chunk` / `tool_call` /
   `turn_completed` only — everything else, including the measured `retry_state` below, is dropped.
2. **Local corpus.** 68 Grok `updates.jsonl` files (3 702 rows) under `~/.grok/sessions` (grok
   1.0.5); 15 Grok `events.jsonl` files (30 690 rows); 7 Codex `~/.codex/sessions/**/rollout-*.jsonl`
   files. Shared-read so a live session's lock does not skip the file.
3. **Cheap probes** (isolated `GROK_HOME` / `CODEX_HOME`; live homes untouched). Expired/garbage
   auth, missing auth, malformed model id. Headless / `exec --json` so the envelope is the
   structured stream, not a PTY scrape.
4. **Not done.** A real quota / credit / 5-hour-window exhaustion. The resilience spec's testing
   stance, and the brief, forbid it. xAI/OpenAI public docs and third-party reports are labelled
   as such and are **not** a measured TUI/transcript shape.

Claude's baseline (CARD-0072, already landed) is the comparison vocabulary, not a finding:

| Class | Claude stub | Classifier |
|---|---|---|
| Wall | `error: rate_limit`, status 429, text `"You've hit your session limit · resets 6:10pm (Europe/London)"` | `Wall` |
| Transient | `error: server_error`, 529 / no-status connection drop | `Transient` |
| NeedsHuman | `authentication_failed`, `model_not_found` / 404 | `NeedsHuman` |

`ApiErrorClassifier` keys on the structural class, then HTTP status; the reset time lives only in
text and is a later parser's problem.

---

## Grok (grok.exe 1.0.5 / 5115b46bc9)

### What Antiphon can see today

The tailed stream is `GROK_HOME/sessions/<url-enc-cwd>/<session-id>/updates.jsonl`. CARD-0080 S2
already consumes `turn_completed` as `TurnEnd` (`stop_reason` verbatim). The normalizer does **not**
stamp `IsApiError` / `ApiErrorClass` / `ApiErrorStatus` on any Grok row.

Corpus of 3 702 updates:

| `sessionUpdate` | Count | Notes |
|---|---|---|
| `tool_call` / `tool_call_update` | 928 / 1 853 | |
| `agent_thought_chunk` / `agent_message_chunk` / `user_message_chunk` | 411 / 214 / 88 | |
| `turn_completed` | 72 | **all** `stop_reason=end_turn` |
| `task_*` / `plan` / compaction pair / `session_recap` | 52+52 / 15 / 6+6 / 1 | skipped by the normalizer, by design |
| **`retry_state`** | **4** | skipped by the normalizer; the only error-shaped ACP update in the corpus |

Zero `stop_reason` other than `end_turn` in this corpus (the headed canary's `cancelled` Esc shape
is measured elsewhere and is not a usage-limit). Zero rows whose payload classified a rate limit,
quota, or 429. The 206 regex "error-ish" hits were this survey talking about rate limits.

### Measured: mid-turn `retry_state` (Transient, not a wall)

Session `6d688fb5-550b-48cb-a888-be9740a422db` (2026-08-18), four consecutive
`_x.ai/session/update` rows, then the turn completed normally:

```json
{"timestamp":1787090028,"method":"_x.ai/session/update","params":{"sessionId":"6d688fb5-550b-48cb-a888-be9740a422db","update":{"sessionUpdate":"retry_state","type":"retrying","attempt":1,"max_retries":15,"reason":"API error (status 500 Internal Server Error): API error (status 500 Internal Server Error): error: Service temporarily unavailable. The model did not respond to this request."},"_meta":{"eventId":"6d688fb5-550b-48cb-a888-be9740a422db-981","agentTimestampMs":1787090028623}}}
```

Attempts 2–4 are the same `type: "retrying"` with the same 500 reason. `events.jsonl` for that
session then wrote `{"type":"turn_ended","outcome":"completed"}`. So:

- **Structural**, on the tailed ACP file, with a typed field (`sessionUpdate=retry_state`) plus
  attempt/max and a free-text `reason` that happens to contain `status 500`.
- **Mid-turn, not a dead-turn stub.** Claude's `rate_limit` stub *is* the turn. Grok's
  `retry_state` is Grok doing its own ladder (cap 15) *inside* the turn. Mapping it onto `Wall`
  would pause the fleet for a 500 that the binary already recovered from.
- **Reset time:** none. The reason is an HTTP status + prose.
- **Classifier mapping:** `Transient` (status 500 / `server_error`). A `retry_state` whose reason
  parsed as 429 would still be "Grok is retrying", not "the turn is dead" — do not treat presence
  of `retry_state` as a wall.
- **Unobserved:** `type` other than `retrying` (exhausted? failed?); a `retry_state` whose reason
  is a 429 / quota string; what lands after `max_retries=15` is spent.

### Documented, not observed: `StopFailure` hook class `rate_limit`

Grok's shipped hooks guide (`~/.grok/docs/user-guide/10-hooks.md`, this binary) defines
`StopFailure` as the observe-only event that fires instead of `Stop` when a turn dies of an API
error. The matcher tests a classified `error` field. Six values, quoted:

> `rate_limit`, `authentication_failed`, `invalid_request`, `server_error`, `max_output_tokens`,
> `unknown`. Capacity errors (503/529) classify as `rate_limit`.

The payload also carries `errorDetails` (raw detail, 1 000-char clip) and `lastAssistantMessage`
(the rendered error string shown in the conversation).

This is **structural, but it is a hook stdin envelope, not an `updates.jsonl` row.** Antiphon does
not install Grok hooks and does not tail them. A 3 702-row corpus contains no `StopFailure` and no
ACP `sessionUpdate` that names those six classes. Until a real wall is captured, we do not know
whether a quota death also writes `retry_state`, a new `sessionUpdate`, only the hook, only
screen text, or some combination.

**Do not map Grok's hook class `rate_limit` 1:1 onto `ApiErrorClassifier.Wall`.** Claude's 529 is
`Transient`; Grok's docs put 503/529 in `rate_limit`. A mechanical copy would fleet-pause on
capacity errors.

OTEL (`24-monitoring-usage.md`) exports `grok_code.api_error` (`error_category`, `status_code?`)
and `grok_code.turn.count` `outcome=error`. That stream is opt-in, content-free by default, and
not tailed. Same debt: documented, not observed on a wall.

`events.jsonl` (local, not tailed) has `turn_ended.outcome` (`completed` on the recovered 500
turn). An `outcome=error` row would be structural if it exists. Unobserved.

`/usage` is a billing/credits UI, not a turn-death record.

### Measured cheap probes (2026-08-19, isolated `GROK_HOME`)

| Probe | Result | Classifier |
|---|---|---|
| No `auth.json` | Headless `-p` parks on the device-code screen (`Approve in your browser to finish signing in.` + code + `Waiting for approval...`), never exits, no stdout JSON, no `updates.jsonl`. Confirms CARD-0080's fail-fast modal (global per home, not per-cwd). | `NeedsHuman` — startup, not a turn stub |
| Garbage / expired `auth.json` (invalid key + refresh, `expires_at` 2020) | Same device-code screen. Invalid auth is indistinguishable from no-auth at the stream/screen layer. | `NeedsHuman` — startup, not a turn stub |
| Valid auth copy + `--model card0083-does-not-exist` | Exit 1 **before a session**. stdout `{"type":"error","message":"Couldn't set model 'card0083-does-not-exist': Invalid params: \"unknown model id\". Run 'grok models' to see available models."}` (same text on stderr). No `updates.jsonl`. | `NeedsHuman` (`model_not_found` analog) — CLI, not a turn stub |

None of these is a usage-limit wall. They bound the adjacent classes.

### Usage-limit wall (the S1 question)

**Unknown, 2026-08-19.** Reason: 68 local sessions never wrote one; a real quota hit was not
triggered; the documented `StopFailure.error=rate_limit` class has no captured ACP/transcript
twin; `retry_state` is a mid-turn 500 ladder, not a dead-turn stub; xAI's public API docs say a
bare HTTP 429 + exponential backoff and do not document a reset-time field (API, not TUI).

Reset time: **unknown**. Claude's wall states one in text. Grok's `/usage` UI and hook
`lastAssistantMessage` *might* carry one. Unobserved.

S4 fixture from this survey: the `retry_state` JSON above (Transient / mid-turn). Not a wall
fixture.

---

## Codex (codex-cli 0.147.0 / desktop rollouts 0.148.0-alpha.9)

### What Antiphon can see today

`RunnerCodexAdapter` is quiet-time + `CodexResponseAnalyzer` screen scrape. `TranscriptEnabled`
is false. Nothing tails `~/.codex/sessions/**/rollout-*.jsonl` and nothing speaks `codex exec
--json`. A usage-limit that existed only as a rollout field would be invisible to production
detection. A usage-limit that printed on the TUI would be an undifferentiated string inside
`ResponseText`.

### Measured: structured turn-failure envelopes (not a wall)

Historical TUI rollouts (2 of 7 files), unsupported model on a ChatGPT-account Codex, 2026-08-17:

```json
{"timestamp":"2026-08-17T21:00:01.115Z","type":"event_msg","payload":{"type":"task_complete","turn_id":"01a01186-2633-70c1-a794-1b5506035dde","last_agent_message":null,"error":{"message":"{\"type\":\"error\",\"status\":400,\"error\":{\"type\":\"invalid_request_error\",\"message\":\"The 'gpt-5.6-sol' model is not supported when using Codex with a ChatGPT account.\"}}","codex_error_info":"other"},"started_at":1787000399,"completed_at":1787000401,"duration_ms":1555}}
```

Isolated `CODEX_HOME` probes, 2026-08-19, native `codex.exe exec --json --ephemeral
--ignore-user-config`:

**No auth** (exit 1). Stream of `{"type":"error","message":"Reconnecting... N/5 (unexpected status
401 Unauthorized: ...)"}` then:

```json
{"type":"turn.failed","error":{"message":"unexpected status 401 Unauthorized: Missing bearer or basic authentication in header, url: https://api.openai.com/v1/responses, …"}}
```

**Valid auth copy + `-m card0083-does-not-exist`** (exit 1). Same nested 400 body as the 2026-08-17
rollout, different envelope:

```json
{"type":"item.completed","item":{"id":"item_0","type":"error","message":"Model metadata for `card0083-does-not-exist` not found. Defaulting to fallback metadata; this can degrade performance and cause issues."}}
{"type":"turn.started"}
{"type":"error","message":"{\"type\":\"error\",\"status\":400,\"error\":{\"type\":\"invalid_request_error\",\"message\":\"The 'card0083-does-not-exist' model is not supported when using Codex with a ChatGPT account.\"}}"}
{"type":"turn.failed","error":{"message":"{\"type\":\"error\",\"status\":400,\"error\":{\"type\":\"invalid_request_error\",\"message\":\"The 'card0083-does-not-exist' model is not supported when using Codex with a ChatGPT account.\"}}"}}
```

So Codex **does** have a typed dead-turn record:

| Surface | Envelope | Error body |
|---|---|---|
| TUI rollout (historical) | `event_msg` / `task_complete` / `payload.error` + `codex_error_info` | JSON-in-string: `{type, status, error.type, error.message}` |
| `exec --json` (this survey) | `turn.failed` / `error.message` (and a preceding `type=error` line) | same JSON-in-string for 400; plain `unexpected status 401 …` for auth |

- **Structural** on those streams. **Text-only** to Antiphon today, because neither stream is
  tailed and the PTY adapter does not parse them.
- **Reset time:** none on the measured 400/401 bodies.
- **Classifier mapping:** 401 → `NeedsHuman` (`authentication_failed`). 400
  `invalid_request_error` / model-not-supported → `NeedsHuman` (`model_not_found`). Status 400
  would fall through today's classifier to `Unknown` unless S4 adds a per-provider class map or
  parses the nested `error.type`.
- Seven rollouts contained no 429, no `rate_limit`, no `You've hit your usage limit`.
  `codex_error_info` was only seen as `"other"`.

### Usage-limit wall (the S1 question)

**Unknown, 2026-08-19.** Reason: no local rollout or `exec --json` capture of a quota / 5-hour /
weekly exhaustion; not triggered on purpose.

Third-party reports (GitHub `openai/codex#2669`, Reddit/help threads, **not measured here**) quote
TUI/CLI prose of the form `You've hit your usage limit. Upgrade to Pro … or wait for limits to
reset (every 5h and every week.)` and `try again at <wall-clock>`. If that text is real, a reset
time exists **in text**, Claude-like. We do not know whether the same turn also writes
`task_complete.error` / `turn.failed` with a distinct `error.type` / `codex_error_info`, or only
paints the screen. Until a local capture exists, S4 cannot key detection on either.

S4 fixtures from this survey: the 400 `invalid_request_error` bodies (both envelopes) and the 401
`turn.failed`. Adjacent classes, not a wall.

---

## OpenCode

**Unknown, 2026-08-19.** Reason: the OpenCode binary / `ocg.ps1` wrapper is not installed on this
machine (`where.exe opencode` empty; `C:\Users\lndco\.local\bin\ocg.ps1` and the operator-guide
path `C:\Users\mike.ciechan\.local\bin\ocg.ps1` both missing; no `~/.opencode` home). There is
nothing local to launch.

`RunnerOpenCodeAdapter` is a quiet-time PTY client that reuses `CodexResponseAnalyzer` for prompt
echo stripping. `TranscriptEnabled` is false. `AgentTuiRunnerCatalog` already declares
`structuredActivity: Degraded` (quiet-time) and `sessionResume: Unknown`. There is no OpenCode
tailer, no error-path in the adapter, and no fixture in the test tree.

The plan's leftover question — whether OpenCode has a tailable ACP/app-server stream, or a
blocking first-launch modal — is still open. A fresh-cwd canary was not run because there is no
binary. Installing OpenCode just to ask it was out of S1's "locally reproducible" bar.

---

## Mapping onto `Wall` / `Transient` / `NeedsHuman` / `Unknown`

| Signal | Form | Reset time? | Classifier today | Notes for S4 |
|---|---|---|---|---|
| **Claude** `rate_limit` / 429 stub | Structural JSONL fields | Yes, in text | `Wall` | Landed. Not this survey. |
| **Grok** usage-limit / quota death | **Unknown** | Unknown | — | No capture. Hook class `rate_limit` is documented only. |
| **Grok** `retry_state` / 500 (measured) | Structural ACP update | No | `Transient` | Mid-turn ladder, not a dead stub. Currently dropped. |
| **Grok** device-code login (measured) | Screen-only (startup) | n/a | `NeedsHuman` | Fail-fast modal. No transcript row. |
| **Grok** unknown model (measured) | Headless JSON `type=error` | n/a | `NeedsHuman` | Pre-session CLI. No `updates.jsonl`. |
| **Codex** usage-limit / 5h / weekly | **Unknown** | Reported in text, not measured | — | Need a local capture before detection. |
| **Codex** 400 `invalid_request_error` (measured) | Structural on rollout / `exec --json` | No | `NeedsHuman` (status 400 alone → `Unknown`) | Untailed by Antiphon. |
| **Codex** 401 unauthorized (measured) | Structural on `exec --json` | No | `NeedsHuman` | Untailed. Retries 5× then `turn.failed`. |
| **OpenCode** anything | **Unknown** | Unknown | — | Not installed. |

---

## What S4 should (and should not) take from this

1. **The usage-limit axis stays Unknown for all three kinds.** That is the S1 deliverable. S4's
   detection seam does not gain a second provider's wall fixture. The plan already said the slice
   "shrinks to near-zero if S1 finds only Unknowns" for the wall itself.
2. **Adjacent measured shapes are real and should not be invented again.** Grok `retry_state` and
   Codex `task_complete.error` / `turn.failed` are the fixtures to lift if S4 (or a later card)
   normalises *any* API-error death, not just walls. They are Transient / NeedsHuman, not Wall.
3. **Do not teach `GrokTranscriptNormalizer` to stamp `IsApiError` from `retry_state` and then
   feed it to `ApiErrorClassifier` as a wall.** That would fleet-pause on a 500 Grok already
   retried through. If `retry_state` is ingested, it needs its own class (or Transient-only) and
   must not fire `UsageLimitState`.
4. **Do not map Grok hook `rate_limit` onto `Wall` without a captured payload.** Capacity 503/529
   is in that class by documentation.
5. **Codex detection is gated on a tailer that does not exist.** Screen-scraping
   `You've hit your usage limit` is the rejected primary (an agent writing about the error must
   not trip it). If S4 wants Codex at all, the work is "tail the rollout / app-server stream,
   then classify", not "add a regex to `CodexResponseAnalyzer`".
6. **OpenCode stays Unknown until a binary is present.** Installing it is a separate, cheap
   follow-up — not a guess.

### Recommended `UsageLimitSignal` cells for S2 (starting point only)

S2 is in parallel and owns the declarations. The survey's honest fill-in:

| Kind | `Form` | `StatesResetTime` | `State` / reason |
|---|---|---|---|
| Grok | `Unknown` | null | Unknown — 2026-08-19: no captured wall; hook class documented; `retry_state` is Transient mid-turn |
| Codex | `Unknown` | null | Unknown — 2026-08-19: no captured wall; adjacent 400/401 envelopes exist on an untaileable stream |
| OpenCode | `Unknown` | null | Unknown — 2026-08-19: binary not installed on the survey host |

`Unsupported` would be a lie: we have not shown these providers *cannot* emit a wall, only that
we have not seen one.
