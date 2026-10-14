# CARD-1259: live transcript ingestion lags the CLI by 28 s; delivery verifier presses Enter again and fails at 30 s

Investigation by task `baa53a4a`, 2026-10-14 UTC. Source baseline and initial fetched
`origin/master`: `5665ddb1a3ba730f3b858acc109e65b5d7304d6c`.
Evidence only; no production implementation, configuration, deployment or chat input.

## Status

The original prompt's producer-to-database-stamp interval is **30.541032 seconds**;
the assistant/TurnEnd interval is **27.777032 seconds**. These are not measurements
of the file watcher alone. The exact blocking hop in this incident is unconfirmed.
The inline event pump has an independently reproduced head-of-line blocking mechanism
(CARD-0432), retained in the current source. Controlled measurements are pending below.

## Stored incident evidence

Retrieved `GET /api/agent-tasks/963621db` and the complete retained transcript for its
session `22e8899d-1c52-464e-a803-d312bc624dc8` (100 entries; continuation after 100
returned empty). Its ToolCall 51 and ToolResult 52 contain the original desktop SQL
and output, not merely the Debug task's final interpretation:

Session `fb38e751-dfbd-4b8d-9ce5-ef84d1828461`, `TranscriptEntries`:

| Sequence | Kind | Producer timestamp UTC | CreatedAt UTC | Difference, seconds |
|---|---|---|---|---:|
| 1124 | TurnEnd | 2026-10-12 21:50:16.347 | 2026-10-12 21:50:43.388964 | 27.041964 |
| 1125 | UserPrompt | 2026-10-14 07:02:16.815 | 2026-10-14 07:02:47.356032 | 30.541032 |
| 1126 | AssistantText | 2026-10-14 07:02:19.579 | 2026-10-14 07:02:47.356032 | 27.777032 |
| 1127 | TurnEnd | 2026-10-14 07:02:19.579 | 2026-10-14 07:02:47.356032 | 27.777032 |

ToolResult 46 additionally identifies prompt UUID `f3e88a7d-51f4-4db1-b691-89827485b2be`
and assistant/TurnEnd UUID `0d928d08-9840-4b94-ac37-368c0010c690`.
ToolResult 52 records queue message `fc2286e9-2811-4537-b68e-0bfcbf872567`:
baseline 1124, attempts 1, LastDeliveryStartedAt 07:02:15.577028Z,
SentAt/DeliveryVerdictAt 07:02:47.371093Z, verdict 8 (LateConfirmed).

ToolCall 40 / ToolResult 41 preserve lines from desktop
`C:\src\Antiphon\server\logs\antiphon-20261014.log` (original log zone +01:00):
line 133107: second Enter at 07:02:24.001Z; line 133171: third Enter at
07:02:31.247Z; line 133277: 30-second verification failure at 07:02:47.189Z;
line 133278: late confirmation at 07:02:47.371Z. Those excerpts are truncated
by the original capture and are not a complete pump trace.

The same CreatedAt for 1125-1127, immediately after the timeout and immediately
before late confirmation, is consistent with the grace-window catch-up batch.
CreatedAt is assigned before SaveChanges, not a database commit timestamp.
The native timestamp is not an observed filesystem append/flush time.

## Verification scope

The new tailer probe writes only generated files under an owned temporary
CLAUDE_CONFIG_DIR. It measures monotonic time before append through receipt from
the real TranscriptTailer / SessionRunnerEventHub, with one and sixteen concurrent
tailers. It checks all prompt identities and counts. It is a characterization,
not a latency SLA, CPU saturation test, Windows qualification or complete server
ingestion measurement. Existing S0 uses the real hosted server pump with an
in-memory runner, isolated PostgreSQL and a deliberately blocked side effect.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | evidence | `tests/Antiphon.SessionRunner.Tests -> bin-c1259-tailer/` | tailer-measurement | `/*/*/TranscriptIngestionLatencyProbeTests/Appended_prompts_reach_the_event_hub_under_concurrent_file_load` | file-to-hub characterization | 2 parameter rows, 0 failed/skipped | 2 | 5 |
| CP-2 | evidence | `tests/Antiphon.Tests -> bin-c1259-pump/` | blocked-pump | `/*/*/SessionRunnerEventPumpTests/S0_blocked_hosted_pump_probe` | existing serial-pump reproduction | 4 parameter rows, 0 failed/skipped | 4 | 8 |

## Disclosures and remaining evidence

Desktop log retrieval through `scripts/logs.ps1 -Source desktop-server -Tail 1`
failed because `/work/repos/antiphon/server/logs/antiphon-*.log` does not exist in
the Linux mirror. Original desktop runner logs and incident-time host samples
have not been retrieved. The public transcript DTO omits CreatedAt, so a seven-day
ingestion distribution cannot be computed from that endpoint. The four stored SQL
rows above are a selected incident sample, not a seven-day population.

## Not done, noted

No fix designed or implemented; assess the existing CARD-0432 blocked-pump mechanism before changing a verifier timeout.
