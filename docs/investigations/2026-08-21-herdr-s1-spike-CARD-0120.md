# CARD-0120 — Herdr S1 live spike

**Date:** 2026-08-21
**Verdict:** GO for the separate, opt-in Herdr backend slices — with the delivery contract below.

## Environment and protocol

- Windows 10 19045, Herdr 0.8.2.
- The installer placed `herdr.exe` and its app-local ConPTY runtime at
  `%LOCALAPPDATA%\Programs\Herdr\bin`. The persisted user `PATH` includes that directory; the
  already-running task host had an older inherited `PATH`, so a child process composed with the
  persisted path was used for the live tests.
- `herdr api schema --json` reported protocol **20**, schema version **1**. A live raw `ping`
  returned `type: pong`, version `0.8.2`, protocol `20`.
- Windows transport is a generic namespaced pipe using Herdr's displayed socket path unchanged:
  `C:\Users\lndco\AppData\Roaming\herdr\herdr.sock` becomes the `NamedPipeClientStream` pipe
  name. The `.sock` file is Herdr's Windows marker file, not a Unix socket.

## Named-pipe measurement

200 fresh request connections, each sending one NDJSON `ping` and reading its matching reply:

| Measure | Milliseconds |
| --- | ---: |
| Successful / attempted | 200 / 200 |
| Min | 0.715 |
| Median | 2.771 |
| Mean | 6.557 |
| p95 | 19.981 |
| p99 | 113.601 |
| Max | 125.677 |

Herdr closes normal request pipes after responding. A client must open one connection per normal
request; only `events.subscribe` retains its connection for pushed event envelopes.

## Claude transcript binding

An isolated headless Herdr server was started, then a workspace rooted at
`C:\Antiphon\worktrees\card-task-4316a0ab` and a real Claude Code agent were created with
`agent.start`. The first `agent.prompt` created:

```text
%USERPROFILE%\.claude\projects\C--Antiphon-worktrees-card-task-4316a0ab\
  ff08adc5-9597-402f-8df9-403d1c99d97b.jsonl
```

The directory did not exist before the prompt. The JSONL carried the expected cwd and session id,
so it is exactly the cwd-keyed location that the existing C1-C4 transcript-binding rules inspect.
Herdr did not wrap or relocate the Claude child process.

## Delivery measurement

Both paths produced an exact **86,400 UTF-8-byte** Claude `UserPrompt`, with independently checked
start/end markers and exact byte count in the JSONL:

1. `agent.prompt` delivered the whole body and Claude replied `LARGE-OK`. Its optional Herdr
   state wait incorrectly returned `agent_prompt_stalled` after **6.922 s** because no state change
   was observed inside Herdr's fixed 5-second window; state later advanced from 3 to 5 and the
   exact record was present. Do not use that wait result as a delivery verdict.
2. `pane.send_text` with Antiphon's bracketed-paste wrapper plus a separate `enter` also delivered
   the whole body. The first enter at **500 ms** was too early and left the intact paste in Claude's
   composer (`[Pasted text #2]`); a retry enter submitted the full exact body and changed the
   agent to working. This matches Antiphon's existing transcript-confirmed, Enter-only retry
   contract rather than the inbox-conhost clipping regime.

There was no 1,024-byte inbox-style clipping at the measured 86,400-byte envelope. The later S3
delivery adapter should therefore preserve Antiphon's LF normalization, bracketed-paste wrapper,
separate CR, and transcript-confirmed re-Enter behaviour; it must not substitute Herdr's
agent-state wait for the existing delivery verdict.

## S1 implementation

`HerdrClient` is in `Antiphon.SessionRunner` and is registered only as an additive concrete client.
`SessionRunner:Herdr:Enabled` defaults to false; no current launch, queue, supervision, or existing
PTY-host path selects it. It provides:

- documented socket/session resolution and Windows named-pipe NDJSON framing;
- per-request request/response handling with request-id and API-error checks;
- protocol validation through `ping` (expected protocol 20);
- `events.subscribe` push event streaming;
- explicit `HerdrBackendUnavailableException` for disabled, missing, stopped, or dropped backend
  connections, and explicit protocol mismatch failures.

The fake named-pipe tests cover request framing, successful protocol validation, protocol mismatch,
disabled/missing backend failures, and subscription acknowledgement plus pushed event framing. They
do not require a live Herdr instance or Claude account.

## Verification

- `dotnet run --project tests/Antiphon.SessionRunner.Tests --property:OutputPath=bin-card0120/ -- --treenode-filter "/*/*/HerdrClientTests/*"` — 6 passed, 0 failed.
- `dotnet run --project tests/Antiphon.SessionRunner.Tests --property:OutputPath=bin-card0120/` — 141 passed, 0 failed.

The isolated Herdr server and its test Claude process were stopped after measurement. No existing
operator-owned Herdr session was present before the spike server started.
