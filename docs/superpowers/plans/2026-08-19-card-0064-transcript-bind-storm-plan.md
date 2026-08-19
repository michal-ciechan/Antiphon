# CARD-0064 — the transcript bind storm: plan

**Date:** 2026-08-19
**Status:** planned
**Card:** CARD-0064 (Backlog, labels `reliability`, `transcripts`, `incidents`) — filed 2026-08-17,
description already corrected in full by task `a65cc6fe` the same day.
**Precedent:** CARD-0006 (the C1–C4 adoption rules), `docs/investigations/2026-08-17-az-care-transcript-bind-CARD-0064.md`.
**Evidence:** re-measured against the live database, the live sidecar store and the live transcript
files on 2026-08-19/20. Nothing in this document is carried over from the card's numbers.

This is a planning document only. Do not write the probe change in the Plan pass — that is S1.

## Verdict up front

**Both agents the card was filed about are cured, and the card should not be closed — because the
same defect reproduced today, on a fresh launch, in the session writing this plan.**

The 08-17 investigation concluded the rules "behaved correctly throughout" and that C4 was
"unsatisfiable by construction" for az-care because its only user prompt was a 1-character `"—"`.
That reading was right about az-care and one step short of the general cause. C4 is not merely
unlucky about short prompts: **it is structurally blind to any body Claude's composer QUEUES instead
of submitting**, and queueing is the normal outcome when we type a brief into a session that is
still mid-turn.

| Question | Answer |
|---|---|
| Is the card's original agent still failing? | **No.** `AZ Care`'s last `TranscriptBindFailed` was **2026-08-16 13:42:01Z**; `Family`'s was **2026-08-13 10:35:04Z**. Both now bind `how: sidecar`, re-adopted 2026-08-19 02:43:21Z. |
| Was the third agent in the aggregate fixed? | **Yes.** `antiphon-check-interpreter` last failed **2026-08-16 20:28:31Z** — CARD-0047's trust-dialog fix, as the card predicted. |
| So is there residual work? | **Yes, and it is not what the card recommends.** A new storm fired **2026-08-19 20:27:44Z → 21:12:45Z**: 10 incidents, one agent, a **fresh** (`resumeLaunch: false`) launch. |
| Root cause of the new storm | The delegation brief reached the transcript as `queue-operation` + `queued_command` **attachment** records, never as a `user` prompt. `TranscriptNormalizer` maps both to `[]`, so C4's probe never saw the one piece of positive identification that was sitting in the file. |
| Blast radius | The session ran **49m 01s** with no transcript. Everything that reads working/idle, delivery confirmation, channel reply routing and check-in digests was blind for that window. |
| Scope | **One Code slice** in `TranscriptCandidateProbe`. No migration, no new rule, no rule relaxed. |

## 1. What is actually happening now

### 1.1 The card's three agents

`AgentIncidents` where `Kind = 15` (`TranscriptBindFailed`), all time:

| Agent | Severity | Count | First | Last |
|---|---|---|---|---|
| AZ Care | 3 (Critical) | 499 | 2026-08-14 20:11:21Z | **2026-08-16 13:42:01Z** |
| Family | 3 (Critical) | 12 | 2026-08-13 09:40:03Z | **2026-08-13 10:35:04Z** |
| antiphon-check-interpreter | 1 (Warning) | 9 | 2026-08-16 20:02:22Z | **2026-08-16 20:28:31Z** |
| **task-d52298ac** | **1 (Warning)** | **10** | **2026-08-19 20:27:44Z** | **2026-08-19 21:12:45Z** |

Both channel-bound agents are healthy and are no longer leaning on the migration shim — their
sidecars read `how: sidecar`, `resumeLaunch: true`, updated 2026-08-19 02:43:21Z. The card's
instruction to leave the shim in place still holds (§5.3), but it is no longer load-bearing for
them.

### 1.2 The incident table under-reports this failure class

`FK_AgentIncidents_Agents_AgentId` is **`ON DELETE CASCADE`**, and delegate agents are ephemeral —
3 of the many `task-*` agents run this week still exist. **Every incident raised by a delegate that
has since been cleaned up is gone.** That is the mechanical reason the card's aggregate looked like
"one agent": the survivors are the long-lived channel agents. Any future measurement of this class
from `AgentIncidents` alone is a lower bound. (Noted, not fixed here — see §5.4.)

### 1.3 Bind health across every session the runner has recorded

From the 191 sidecars in `C:\logs\antiphon\session-runner\transcripts\`:

| Format | `how` | Count |
|---|---|---|
| claude | `exact` | 62 |
| claude | `discovery` | **57** |
| claude | `sidecar` | 11 |
| claude | (none recorded) | 13 |
| grok | `deterministic` | 48 |

**`--session-id` is honoured for roughly half of Claude launches**, so the C1–C4 discovery path is
load-bearing for ~40% of them, not an edge case.

Time from `childStartUtc` to bind, for the 57 discovery binds: **median 15.3s**, 7 over 60s, 7 over
300s, max **2940.8s**.

| Agent | Bound after | First record in the file | First textual user prompt |
|---|---|---|---|
| **task-d52298ac** | **2940.8s** | **16.3s** | 16.3s (`"green"`, 5 chars) |
| task-0320dca6 | 315.9s | 314.0s | 314.0s |
| task-53d3424c | 314.9s | 313.9s | 313.9s |
| task-4b12ef21 | 314.7s | 313.7s | 313.7s |
| task-74bef32b | 313.3s | 311.7s | 311.7s |
| task-ae4da41c | 312.9s | 311.7s | 311.7s |
| task-34576910 | 312.8s | 311.8s | 311.8s |

**The ~313s cluster is not a binding defect and needs no fix.** Their briefs were typed at 9.3–13.4s
after session creation (`SessionQueuedMessages.SentAt`), but Claude wrote *no transcript record at
all* until ~312s — its own lazy-write lag. C4 matched **1.0–1.9s** after the file appeared. The
rules had nothing to bind and correctly ran unbound.

**The outlier is a different shape entirely**: its file existed and was being written from 16.3s,
and the rules refused it anyway for another 49 minutes.

## 2. Root cause of the outlier

Session `8fb1c60e-28fa-41f2-b80e-aacafa31613d`, agent `task-d52298ac`, cwd `C:\src\Antiphon`,
`resumeLaunch: false`, child start `2026-08-19T20:26:29.4072311Z`.

| Time | Event |
|---|---|
| 20:26:29.101Z | Brief enqueued, `SessionQueuedMessages` seq 1, `Origin = Delegation` |
| 20:26:41.915Z | Brief **typed and stamped Sent** — it is in `SessionInputLog` from here |
| 20:26:45.658Z | `attachment` record — first root `timestamp` **and** first `cwd` in the file |
| 20:26:45.667Z | `queue-operation` `enqueue`, `content` = **the brief, verbatim** |
| 20:26:45.667Z | `attachment`, `attachment.type = "queued_command"`, `prompt` = **the brief, verbatim** |
| 20:26:45.679Z | `user` record — content `"green"`, **5 characters** |
| 20:26:56.034Z | `queue-operation` `remove`, `content` = **the brief, verbatim** |
| 20:27:44Z … 21:12:45Z | 10 × `TranscriptBindFailed`, one per 5 minutes |
| **21:15:29.488Z** | A `/compact` prompt (389 chars) is written as a real `user` record |
| 21:15:30.217Z | `TranscriptBoundByDiscovery` — 2940.8s after child start |

The composer was busy with the `"green"` turn, so the brief was **queued rather than submitted**.
When it was dequeued at 20:26:56 the model received it — the work went ahead normally — but Claude
wrote **no `user` prompt record for it, ever**. Scanning every line of the bound transcript that
contains the brief's task id: `queue-operation` ×2, `attachment` ×1, `assistant` ×1, `user` ×4 —
and all four `user` records are the two later compaction summaries plus two tool results from this
investigation. **Zero user prompts.**

So for 49 minutes the only C4-eligible text in the file was `"green"`, and
`PromptSubmissionMatch.MinMatchChars = 12` (`src/Antiphon.SessionRunner.Contracts/PromptSubmissionMatch.cs:34`)
correctly rejects a 5-character needle. What finally bound the session was an unrelated `/compact`
prompt 49 minutes later.

### 2.1 Why the probe could not see it

`TranscriptCandidateProbe.RememberPromptText` (`src/Antiphon.SessionRunner/TranscriptCandidateProbe.cs`)
feeds every line through `TranscriptNormalizer.Normalize` and keeps only parts whose kind is
`TranscriptKinds.UserPrompt`. `TranscriptNormalizer.Normalize`
(`src/Antiphon.SessionRunner/TranscriptNormalizer.cs:71-78`) dispatches on the root `type`:

```csharp
"assistant" => FromAssistant(root),
"user"      => FromUser(root),
"ai-title"  => FromTitle(root),
"system"    => FromSystem(root),
_           => [],
```

`queue-operation` and `attachment` fall to `_ => []` — deliberately, per the class comment: it is
lossy by design and skips pure-metadata records. That is correct for the *ingested* transcript
stream. It is wrong for C4, whose whole job is to find any trace of text we sent.

The brief was in the candidate file, verbatim, three times, from 16.3s after child start. **C4
could not see any of it.** `grep` confirms nothing in `src/` or `tests/` reads `queue-operation` or
`queued_command` today.

### 2.2 This is the general form of the az-care finding

The card already recorded, for az-care: *"The long resume note … was enqueued then **removed**, so
it never became a prompt record."* That was read as a quirk of one message. It is the same
mechanism, and it fires on ordinary fresh delegate launches whenever the brief lands while the
composer is busy.

## 3. The slice

**S1 — teach the C4 probe to read queued deliveries as evidence.** One file, plus tests.

`src/Antiphon.SessionRunner/TranscriptCandidateProbe.cs`, in `ConsumeLine`/`RememberPromptText`:
before falling through to the normalizer, read the root `type` directly and harvest into
`_recentPrompts`:

- `"queue-operation"` → the root `content` string (both `enqueue` and `remove` carry the full body;
  either is equally valid identification).
- `"attachment"` → when `attachment.type == "queued_command"`, the `attachment.prompt` string.

Everything downstream is untouched. `_recentPrompts` still feeds
`SessionInputLog.MatchesRecordedInput`, which still applies `PromptSubmissionMatch.TryBuildNeedle`,
so `MinMatchChars = 12` and `MatchWindowChars` govern exactly as they do now. The existing
`MaxRetainedPrompts = 32` bound applies unchanged.

### 3.1 Why this is safe, stated against the rule it touches

CARD-0006 exists because an agent once bound the operator's live conversation on **cwd + recency
alone** and reported 65 of their edits as its own work. This slice:

- **adds positive evidence; removes no gate.** C1 (claims), C2 (cwd), C2b (agent-name), C3 (epoch)
  and the migration shim are all untouched.
- **does not lower `MinMatchChars`** and does not widen `_activeWriteWindow` — the two things the
  card explicitly forbids.
- cannot enable a cwd+recency bind: a candidate still has to contain text this session actually
  typed into its own pty.
- rests on a fact of the same class C4 already trusts. A `user` prompt record says "this text was
  submitted to this conversation"; a `queued_command` says "this text was handed to this
  conversation's composer". For it to match ours, our exact body must have been delivered there.

### 3.2 Do not touch `TranscriptNormalizer`

It is tempting to add the record kinds there instead. **Do not.** The normalizer feeds the ingested
transcript stream, which is what the working/idle rules, CARD-0055 delivery confirmation, CARD-0024
completeness and CARD-0067 channel-reply routing all read. Making a queued-but-unsubmitted body
emit a `UserPrompt` part would change what "a `UserPrompt` record" means for delivery confirmation —
a body could be confirmed Sent while still sitting in the composer queue, which is precisely the
CARD-0055 defect in a new disguise. The probe is C4-only and is the correct seam.

### 3.3 Measured effect

Replaying today's file against the proposed rule: C2 and C3 are both satisfied by the attachment at
20:26:45.658Z, C2b by the `agent-name` header record (`task-d52298ac`, an exact match), and C4 by
the `queue-operation` content at 20:26:45.667Z. **Bind at ~16.3s instead of 2940.8s; 10 incidents
become 0** (the first refusal fault needs 60s of continuous refusal). The six ~313s cases are
unaffected — correctly, since nothing existed to read.

## 4. Alternatives considered and rejected

- **Persist `SessionInputLog` (the card's recommended fix #1).** Still un-done, still worth doing —
  but it **would not have helped today**. The input log *had* the brief; the blindness was entirely
  on the transcript-reading side. It addresses the restart/relaunch exposure in §5.2, which is a
  different failure. Keep it separate; do not bundle.
- **Guarantee a C4-usable boot prompt (the card's fix #2).** Changes launch behaviour to work around
  a read gap. With S1 the brief itself becomes usable evidence at 16s, so this loses most of its
  value. Defer; do not do both.
- **Lower `MinMatchChars` so `"green"` qualifies.** Explicitly forbidden by the card, and correctly:
  short strings recur across unrelated conversations, and this is the one rule standing between
  discovery and a stranger's transcript.
- **Extend the migration shim to fresh launches.** Explicitly forbidden by the card. The shim binds
  on activity, not identity — the 2026-08-09 failure mode exactly.
- **Suppress or rate-limit the repeat incidents.** The incidents were the only reason this was
  visible at all. Fix the bind; the incidents stop on their own.

## 5. Out of scope

1. **Claude's ~5-minute transcript write lag** (§1.3, 6 of 57 discovery binds). Not a binding
   defect — the session runs unbound and correctly reads idle. No fix proposed.
2. **The relaunch exposure the card names.** An AlwaysOn kill plus `--resume` produces a new session
   id, so no sidecar, `restartAdopt: false`, an empty input log, and the shim unavailable. Untested
   since 08-13 — neither channel agent has been relaunched. This is what the card's fix #1 is for;
   it deserves its own card.
3. **Do not remove the migration shim** (`TranscriptTailer.cs:474`) in this slice, per the card,
   even though both channel agents now bind via `sidecar`.
4. **Incident cascade-deletion** (§1.2). Worth a card if incident history should outlive ephemeral
   delegate agents. Not this one.
5. The source of the 5-character `"green"` input is **not in this repo** — not in `server/`, `src/`,
   `scripts/`, `client/`, or `~/.claude/settings.json`. It appears in 31 of 88 transcripts. It is
   incidental to the defect (any short input racing the brief produces the same outcome) and is not
   worth chasing as part of this fix.

## 6. Verification

New cases in `tests/Antiphon.SessionRunner.Tests/TranscriptAdoptionSafetyTests.cs` (14 today):

- a candidate whose **only** evidence is a `queue-operation` `enqueue` carrying delivered text binds
  (red before S1);
- the same for an `attachment` / `queued_command`;
- a `queue-operation` whose content was **never** delivered to this session is still refused;
- a queued body under `MinMatchChars` is still refused;
- a queued-evidence candidate that fails C2b or C3 is still refused — evidence does not override
  gates.

Regression: the existing 14 `TranscriptAdoptionSafetyTests` and the 4
`tests/Antiphon.Tests/Application/TranscriptBindingIncidentTests.cs` stay green unmodified — that is
the proof no rule was loosened.

Run (the always-on runner locks `bin/`; note the **forward** slash):

```
dotnet run --project tests/Antiphon.SessionRunner.Tests --property:OutputPath=bin-c64/
```

then delete the `bin-c64` directories it drops across the graph. Do not co-schedule with
`Antiphon.Agents.Pty.Tests`.

## 7. Commit line

```
fix(transcripts): CARD-0064 - accept a queued delivery as C4 evidence
```
