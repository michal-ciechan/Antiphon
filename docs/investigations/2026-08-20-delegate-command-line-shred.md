# The delegate command line is shredded by an unescaped quote in an instruction bundle

**Date:** 2026-08-20 (investigation, task `1a1b6b7b`, Debug/High)
**Status:** root cause measured. No fix written — a follow-up card owns the fix.
**Symptom reported:** every delegate dispatched 04:00–05:50 UTC came back "recovered from an
unbound session"; six `AgentSession`s accumulated 5–37 `TranscriptBindFailed` (Kind 15) incidents
each; 57 live `Antiphon.PtyHost.exe` with only 30 `claude.exe`.

## Verdict up front

Three independent defects stacked. The first is the root cause and it is **not** in the transcript
layer at all — it is one unescaped `"` in `server/Bundles/delegate-basics.md`, passed through an
argument builder that escapes quotes by doubling them.

| # | Defect | Where | Since |
|---|---|---|---|
| 1 | `--append-system-prompt <bundle>` shreds the rest of the command line | `ModernConPtyConnection.BuildCommandLine` + `server/Bundles/delegate-basics.md:18` | 2026-08-17 07:21 UTC (`28afb5f`) |
| 2 | C4 cannot see a queued brief | `TranscriptCandidateProbe` — **fixed** in `94947f1`, but the deployed daemon predates it | daemon built 2026-08-19 03:43 |
| 3 | The refusal fault repeats every 5 min forever, at Warning, uncapped | `TranscriptTailer.MaybeReportRefusal` / `MaybeReportNoCandidates` | CARD-0073 S1 |

## 1. Measured evidence — the live command line

`Get-CimInstance Win32_Process` for this investigation's own `claude.exe` (session
`b42dc25b-56aa-44c0-97d0-9464cd47716f`), command line length 2448:

```
... so a message
  claiming ""tests green"" while two still fail is worse than no message at all.
...
  word ""flaky""." "--session-id" "b42dc25b-56aa-44c0-97d0-9464cd47716f"
```

`server/Bundles/delegate-basics.md:18` is:

```
  claiming "tests green" while two still fail is worse than no message at all.
```

`ModernConPtyConnection.BuildCommandLine` (`src/Antiphon.Agents.Pty/ModernConPtyConnection.cs:423`):

```csharp
"\"" + a.Replace("\"", "\"\"") + "\""
```

Every argument is wrapped in `"` and every inner `"` is **doubled**. The comment states this is
`WindowsArguments.Format` plus Porta's app-quoting rule, i.e. the inbox-conhost backend does the
same — this is not backend-specific.

Feeding the **actual live command line** to `CommandLineToArgvW` (the parser Windows/Node/Bun use
to build `argv`) yields **165 arguments**:

```
[6] --append-system-prompt
[7] [bundle:delegate-basics v5e1c2c6a]\nYou are ru … [len=1320] … so a message\n  claiming "tests
[8] green
[9] while
[10] two
...
[163] word
[164] flaky. --session-id b42dc25b-56aa-44c0-97d0-9464cd47716f
```

Three consequences, all confirmed against the live session:

1. **The system prompt is silently truncated.** `delegate-basics.md` is 2217 chars; the delegate
   receives 1320 (stamp + 1285 chars of body) and loses the remaining **42%** — the whole
   "BUILD TO AN ALTERNATE OUTPUT PATH" and "VERIFY PRE-EXISTING RED" sections. Verified by reading
   this session's own system prompt, which ends mid-sentence at `claiming "tests`.
2. **`green` becomes `argv[8]`, and Claude Code submits the first positional as the session's
   initial prompt.** That is the origin of the mystery `green` first turn. 21 sessions carry a
   literal `green` user record, the earliest **2026-08-17T14:22:24Z** (`card-task-10e30ff7` — the
   session CARD-0073 was filed about). Only fresh launches are affected; a warm-pool reuse composes
   no launch spec and so has no stray prompt, which is why most transcripts still open with the brief.
3. **`--session-id` is swallowed into `argv[164]` and never reaches Claude.** Claude therefore picks
   its own conversation id, `<our-id>.jsonl` never exists, and `TranscriptTailer`'s exact-bind fast
   path (step 2 of `LocateAsync`) can never fire. **This is CARD-0073's unexplained regression,
   root-caused.** CARD-0073 measured the boundary as last `exact` bind 07:44:56 / first `discovery`
   07:50:09 on 2026-08-17 and attributed it to `--name`; `28afb5f` (CARD-0058 slice 1, which
   introduced the bundle and its embedded quote) landed **2026-08-17 08:21:25 +0100 = 07:21 UTC**,
   ~25 min earlier. The `--name` correlation is confounded: `--name` and `--append-system-prompt`
   are added in the same branch of `AgentTaskDispatcher.BuildLaunchSpec` (`:1427` and `:1461`), for
   the same non-Grok delegates. Dropping `--name` would not fix anything.

## 2. Why the refusal follows

`card-task-861c4f19`, transcript `a450d4b3-…jsonl`, records in file order:

```
[7]  queue-operation enqueue   02:47:02.348Z  content = the full 3489-char brief
[10] user                      02:47:02.360Z  message.content = "green"
[25] queue-operation remove    02:47:14.595Z  content = the full brief
[27] attachment                02:47:02.347Z  the brief, attached to the "green" prompt
```

The brief text appears in exactly three records and **none of them is a `user` prompt**. Claude was
already mid-turn on the stray `green` prompt when the queue typed the brief 12 ms later, so Claude
Code recorded it as a queued delivery / attachment. C4 ("a user prompt matching text this session
was actually sent") therefore has nothing to compare against, and `EvaluateCandidates` refuses
forever with `no prompt in it matches input delivered to this session`.

**CARD-0064 (`94947f1`, 2026-08-20 00:21:15 +0100) already fixes exactly this** —
`TranscriptCandidateProbe.HarvestQueuedDelivery` harvests `queue-operation.content` and
`attachment.queued_command.prompt` as C4 evidence, and `TranscriptAdoptionSafetyTests` replays this
very shape (it hard-codes the captured `2026-08-19T20:26:45.679Z` `green` record).

**It is not running.** The live daemon:

```
Antiphon.SessionRunner.exe  StartTime 2026-08-19 03:43:18  LastWriteTime 2026-08-19 03:43:13
```

started by the "Antiphon Session Runner" Scheduled Task at logon and never restarted — ~21 h older
than the fix. So the deployed C4 is the pre-CARD-0064 prompt-records-only version.

## 3. Why it never gives up or escalates

`TranscriptTailer.cs:45` `RefusalFaultRepeat = TimeSpan.FromMinutes(5)`, and
`MaybeReportRefusal` / `MaybeReportNoCandidates` only rate-limit — there is no attempt cap, no
backoff and no severity escalation. `TranscriptBindingIncidentService.OnTranscriptFaultAsync`
records `Critical` only when the agent is channel-bound; a delegate task agent is not, so it is
**Warning forever**, and `AgentSupervisorService.RecordIncidentAsync` writes a fresh row every time
(the alert `DedupKey` groups the alert, not the incident).

Measured cadence matches exactly: session `5409c537`, 37 incidents from 02:48:03.696 to
05:48:11.315 = 180.1 min / 36 intervals = **5.003 min**.

**The cascade is still running.** `AgentIncidents` stops at 05:51:09 UTC, but
`logs/session-runner.log` was still writing `refusing every transcript candidate … after 11165s`
for the same eight sessions at 07:16 local. "Zero new incidents since the cleanup" is the incident
stream having gone quiet, not the fault having stopped — a second observability gap.

## 4. The 39 pty-hosts with no `claude.exe` child are the TEST SUITE, not a retry loop

`C:\logs\antiphon\session-runner\pty-hosts\manifests\*.json`, 58 manifests:

```
36  C:\Windows\system32\cmd.exe
19  claude.exe
 3  grok.exe
```

Every `cmd.exe` host has cwd `C:\logs\antiphon\check-interpreter` or a
`antiphon-interp-wire*` / `antiphon-kind-test*` temp dir. Their `.ansi.log`s are 164 bytes of
`Microsoft Windows [Version 10.0.19045.6466]` plus a `cmd` prompt, sometimes with a check brief
typed at the prompt answering `The filename, directory name, or volume label syntax is incorrect.`

`tests/Antiphon.E2E/Fixtures/AntiphonAppFixture.cs:385-388`:

```csharp
["Agents:DefaultDefinition"] = "e2e-raw",
["Agents:Definitions:e2e-raw:Exe"] = Path.Combine(Environment.SystemDirectory, "cmd.exe"),
["SessionRunner:BaseUrl"] = "http://localhost:17204"
```

The E2E fixture deliberately does not start a runner — it drives the **always-on production
daemon**. Every E2E session therefore creates a real detached `Antiphon.PtyHost.exe` running
`cmd.exe` on the shared runner, and `SessionRunnerSettings.PtyHostLingerHours = 24` keeps it alive
for a day. The "one every 1–2 minutes with no agent inside" cadence is test cases, and the
5-minute-to-10-hour age spread is accumulation across the night's test runs (the CARD-0020/0075/0099
build delegates ran the suites repeatedly). Nothing in production spawns a bare pty-host.

The DB agrees: all six Kind-15 sessions are worktree delegate sessions with a real `claude.exe`;
none is a `cmd.exe` host. Killing the 39 was correct hygiene and had no bearing on the bind failures.

Secondary finding while reading the log: `GET /transcript/{id}` and `POST /sessions/{id}/kill` throw
`KeyNotFoundException` out of `SessionRunnerRuntime.GetSession` (`:382`) for an unknown session,
surfacing as unhandled 500s in the runner log rather than 404s.

## 5. Blast radius

Tasks dispatched today after 02:00 UTC: 15. Of those, **nine settled at exactly ~10:05 after
dispatch**, the `TryRecoverBindRefusalAsync` / boot-prompt-watchdog timeout, rather than on their own
report: `861c4f19`, `c6bc61f7`, `d2477fd1`, `ec9031d4`, `a8ea9c8f`, `9e97b122`, `29faba7d`,
`1a1b6b7b` (this investigation), `4cd78fcb`. Unbound wall-clock at 07:16 local, from the runner's
own counters: 11165 s, 8766 s, 7865 s, 6964 s, 2162 s, 2162 s across the six worktree sessions —
roughly **11 agent-hours** of sessions that were working with nobody able to read them.

Wider: since 2026-08-17 07:21 UTC **every fresh delegate launch** has run on 58% of its system
prompt and without `--session-id`. CARD-0073 measured the transcript half of that (57 discovery
binds vs 1 exact, 13/143 never bound) without knowing the cause.

## 6. Confirmed vs suspected

**Confirmed by measurement:** the doubled-quote escaping, the 165-argv split of the real command
line, the truncated system prompt, `green` as `argv[8]`, `--session-id` lost, the three brief-bearing
record types and the absent `user` record, the daemon build predating `94947f1`, the 5.003-minute
repeat cadence, the `cmd.exe` manifests and the E2E fixture that produces them, the nine 10-minute
settlements.

**Suspected, not proven:** that Claude Code takes the *first* positional as the initial prompt is
inferred (argv[8] is `green`, the prompt is exactly `green`, and none of argv[9..163] appears)
rather than read from its CLI. Whether the correct escape is `\"` or full MSVCRT backslash rules is
for the fix to establish against a real `claude.exe`, not assumed.

**Not the cause:** worktree mode (the `C--src-Antiphon` sessions are affected identically),
`ShadowCopyStore`, resource exhaustion, and today's CARD-0020/0075/0099 merges — the live server DLL
is dated 2026-08-19 22:47, before all of them, and the trigger commit is `28afb5f` from 08-17.

## 7. What a fix has to cover (for the follow-up card, not done here)

1. Escape arguments correctly in `BuildCommandLine` (and the Porta path it mirrors), with a test
   that round-trips a value containing `"`, `\`, `\"` and a trailing backslash through
   `CommandLineToArgvW`.
2. A launch-time assertion that the argv the child will see still contains `--session-id` and the
   full bundle — this failure was invisible from every surface for three days.
3. Restart the session-runner so CARD-0064 is actually deployed, and decide whether daemon staleness
   should be detectable (running binary's build date vs `HEAD`).
4. Cap/escalate the refusal fault instead of repeating at Warning forever.
5. Stop the E2E fixture leaking lingering `cmd.exe` pty-hosts into the production runner.
