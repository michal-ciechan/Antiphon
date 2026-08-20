# CARD-0020 — The stall backstop, the silent caller, and a phase-aware deadline: plan

**Date:** 2026-08-20
**Status:** planned (not implemented)
**Card:** CARD-0020 (`cd1bfc41-bc09-4943-be1d-4b0fda7c56c5`) — addendum to CARD-0003: the stall
backstop cannot fire, and a phase-aware deadline is needed.
**Precedent:** CARD-0003 (`FailNeverStartedAsync`, the delivery backstop this card asked for),
CARD-0021 (`FailDeadSessionTasksAsync`), CARD-0035 (`AttentionService` — the read-only projection
that already carries `NeverStarted`, `DeadSession`, `UncorrelatedReport` and `PastExpectedIdle`),
CARD-0041 (local-command / compaction records are not activity), CARD-0055 (`CatchUpTranscriptAsync`
— pull before you judge; never kill on "the transcript does not contain X"), CARD-0072
(`ApiErrorRecoveryService`, the retry ladder that watches the same sessions), CARD-0077 (the
watchdog's predicate is `TranscriptPromptSpan`, not "any entry at all"), CARD-0085
(`TryRecoverBindRefusalAsync`), CARD-0019 (the card-description limit).
**Evidence:** live Postgres (`antiphon` on 17280) and the current tree, queried 2026-08-20
03:20–04:10Z. 299 tasks all-time, 247 Succeeded; 71 801 transcript entries; two live API probes
against the running server on 17202.

This is a planning document only. Do not write the fix in the Plan pass.

## Verdict

**Two of the card's four defects are already shipped and firing. One is real, reproducible today,
and nothing covers it. The fourth — the phase-aware deadline — is wanted, but the card's numbers are
wrong by 3–25× and its chosen signal (`last entry Kind`) misreads housekeeping as a stalled model
call. Measured below.**

| Question the card asks | Answer, on this evidence |
|---|---|
| Is there any deadline on a task at all? | **Yes, since `10d379e` (2026-08-09 23:00, four hours after this card was filed).** `FailNeverStartedAsync` fails a Dispatched task at `DeliveryFailTimeoutMinutes` (10) with the delivery error attached — exactly the rule the card's last row demands. It has fired **14 times**, most recently 2026-08-19 08:58. |
| Was the 4th occurrence (`fe53500d`) left silent? | **No — it was failed by that watchdog**, at 21:54:18Z, reason `"Boot prompt was never delivered: 10 minutes after dispatch…"`. The card was filed from the 17-minute observation *before* the fix landed the same evening. |
| Can the stall/escalation scan fire? | **Effectively no, and it never has.** Zero `Auto:` escalation events in 299 tasks all-time. Three gates confirmed in code; the measured eligibility is **11 of 299 tasks (3.7%)**. |
| Is `RolePolicyEntry.TimeoutMinutes` dead? | **Yes.** `DelegationSettings.cs:456`, declared, defaulted to 60, referenced nowhere in `server/` or `src/`. |
| Should it simply be turned on? | **No — not at its shipped default.** 5 of 247 successful tasks (2.0%) ran past 60 minutes; the longest Succeeded task ran 2 732 minutes. Enabling 60 would kill real work on day one. |
| Is the no-token caller path still open? | **Yes — reproduced live, twice, today.** The server process cwd is `C:\src\Antiphon\server`; the repo root is its parent and is rejected. CARD-0018 does not touch it. |
| Is the card-description 500 still there? | **No.** Fixed by `c14a009` under CARD-0019: `RequireWithinLimit`, `/api/cards/limits`, and the column is now `text`, not `varchar(4000)`. |
| Is "last entry Kind" a sound phase signal? | **No, on its own.** Every one of the 6 longest `UserPrompt` gaps in 10 days (up to **45.5 hours**) is a `<local-command-stdout>` record. The card's rule would have failed those sessions 45 hours early. |
| Is ~60 s the right model-wait deadline? | **No.** Measured first-token latency after a genuine prompt: p99 163 s, max **217 s**; after a tool result: p99 60 s, max **1 478 s**. 60 s fires on ~4% of prompts and ~1% of 15 191 tool-result waits. |

## 1. What is already shipped, and what is genuinely dead

### 1.1 The delivery backstop exists and works

`AgentTaskDispatcher.TickAsync` runs seven isolated sweeps (`RunSweepAsync`, `:110-146`). Three are
health clocks, not the escalation heuristic the card examined:

| Sweep | Covers | Verdict |
|---|---|---|
| `AutoEscalateStalledAsync` (`:265`) | role-configured stall → bump a tier | the card's target; effectively dead (§1.2) |
| `FailNeverStartedAsync` (`:358`) | Dispatched + no turn prompt since `DispatchedAt` → **Fail**, session killed, caller notified | **shipped, fires** |
| `FailDeadSessionTasksAsync` | task open + session dead, behind a runner-evidence gate | shipped |

`FailNeverStartedAsync`'s own doc-comment already names `CARD-0003/CARD-0020`. Its two branches are
the card's zero-entries rule and the uncorrelated-report rule, and it is explicit that it must not
escalate — "escalation re-runs work on a bigger model, which would launder a lost prompt into a
billed upgrade", the card's own argument, in the code.

Fourteen tasks carry its reasons. Seven are the never-delivered case (`fe53500d`, `f978e957`,
`10e30ff7`, `45fd150a`, `ee035614`, `9a5b93a3`, `db49e6fa`), seven the uncorrelated-report case.
Since 2026-08-14 every one settled **10 minutes and a few seconds** after dispatch — the watchdog on
its cadence, not a human noticing.

CARD-0077 (`c81f8f3`, this morning) replaced the "any transcript entry at all" test with
`TranscriptPromptSpan.HasTurnPromptSinceAsync`, so a **reused warm-pool** session whose inherited
history made the old test always true is now covered too. The card's last row is closed.

> One doc drift to fix in passing: `DelegationSettings.cs:270-277` still describes
> `DeliveryFailTimeoutMinutes` as "ZERO transcript entries", which CARD-0077 made untrue.

### 1.2 The escalation scan is dead, and the measurement is unambiguous

Three gates, all confirmed in the current tree:

1. **`:270-273`** — only roles with **both** `EscalateTo` and `EscalateAfterMinutes`. Shipped
   defaults (`DelegationSettings.cs:250-267`): `Debug` alone qualifies. `Test` has `EscalateTo` and
   no minutes, so the `Where` drops it. Plan, Code, Review, Coverage, Merge, Docs, Commit, Deploy
   have neither.
2. **`:290-292`** — `if ((int)task.ModelLevel <= (int)policy.EscalateTo!.Value) continue;`. Frontier
   is 0, so anything already at the target or above is skipped.
3. `RolePolicyEntry.TimeoutMinutes` is read nowhere.

Measured against 299 tasks all-time:

| | count | share |
|---|---|---|
| Debug (the one escalatable role) | 13 | 4.3% |
| …of those, below Frontier, i.e. actually scannable | 11 | **3.7%** |
| Tasks at Frontier (skipped by gate 2 whatever their role) | 143 | **47.8%** |
| `Auto:` escalation events ever recorded | **0** | — |

Gate 2 is the one worth stating plainly: **half of all delegated work runs at the top tier, which is
precisely the work the scan is written to ignore.**

### 1.3 The card's incidental bug is fixed

`CardService.ValidateCreateRequest` (`:854`) calls `RequireWithinLimit` on Title and Description,
whose doc-comment names the exact `22001 value too long` → raw 500 failure the card describes.
`GET /api/cards/limits` answers `{"maxTitleLength":300,"maxDescriptionLength":20000,…}` live, and
`Cards.Description` is now `text` — the card's `varchar(4000)` is stale. Shipped `c14a009`
(CARD-0019). **Nothing to do.**

## 2. The no-token caller path — open, and reproduced today

`AgentTaskEndpoints.ResolveCallerAsync` (`:97-104`) is unchanged:

```csharp
if (string.IsNullOrWhiteSpace(token))
    return new AgentTaskService.Caller(null, null, Directory.GetCurrentDirectory());
```

`Directory.GetCurrentDirectory()` is the **server process's** cwd, and it becomes `parentDirectory`
in `DelegationWorkspaceResolver.ResolveAsync`, whose only implicit permission is "the parent's OWN
tree". `Delegation:AllowedRoots` is unset in `server/appsettings.json`, so it is empty.

Two probes against the running server, 2026-08-20 03:5xZ, neither of which creates a row:

```
POST /api/agent-tasks {"workingDirectory":"__antiphon_cwd_probe_does_not_exist__"}
 → 422  "Directory does not exist: C:\src\Antiphon\server\__antiphon_cwd_probe_does_not_exist__"

POST /api/agent-tasks {"workingDirectory":"C:/src/Antiphon"}
 → 422  "Directory 'C:\src\Antiphon' is outside the allowed roots.
         Add it to Delegation:AllowedRoots to permit it."
```

The first pins the server cwd at `C:\src\Antiphon\server`. The second confirms the consequence: the
repo root is the *parent* of the inherited root, so `IsWithinRoot` is false and **a shell caller with
no token cannot delegate into this repo at all**. CARD-0018 does not touch `ResolveCallerAsync`.

The second half of the defect survives even if a caller works around the first: per the endpoint's
own comment the no-token path is `(null, null, …)` — no parent task, no `ParentSessionId`, so
`ReplyTo == Session` is impossible and the report can only land on the board. A caller that pastes a
token-less `curl` gets either a 422 it can misread as a bad path, or a task whose result never comes
back. **Both failure modes are silent to the caller, which is this card's subject.**

## 3. The phase-aware deadline: the design is wanted, the card's numbers are not

### 3.1 The signal the card proposes misreads housekeeping as a stalled model call

`RunnerTranscriptEvent.Kind` is a `string` from `TranscriptKinds`
(`SessionRunnerContracts.cs:137`). `UserPrompt` is **not** one phase. The same Kind carries at least
four things, and three of them mean *no API call is coming*:

| Shape | Predicate that already exists | What the card's rule would conclude |
|---|---|---|
| a real typed prompt | — | waiting on model ✓ |
| `<command-name>` / `<local-command-stdout>` | `IsLocalCommandRecord` | **waiting on model ✗** — nothing is coming, ever |
| `[Request interrupted…` | `IsInterruptPrompt` | **waiting on model ✗** — the turn ended |
| the compaction continuation prompt | `IsCompactionContinuationPrompt` | **waiting on model ✗** — housekeeping |

This is not hypothetical. Every `UserPrompt`-headed gap over 10 minutes in the last 10 days — all
six of them, 611 s to **163 650 s (45.5 hours)** — is a `<local-command-stdout>` record from a
`/compact`. A 60-second first-token deadline keyed on raw Kind would have failed three sessions
**45 hours early**, and would do so again on the next `/compact`.

The correct primitive already exists and already excludes all three:
`SessionMessageQueueService.IsWorkingAsync(_db, sessionId, ct)`. `AttentionService` states the rule
this repo has settled on — *"not a second implementation of it: three already run in lockstep and a
fourth would be a defect"*. **The phase rule must gate on the shared working verdict first, and use
the last Kind only to pick which deadline applies.**

### 3.2 The deadline numbers, measured

Inter-entry gaps over the live corpus, partitioned by session and ordered by `Sequence`
(10 days, seconds):

| Phase | Transition measured | n | p99 | p99.9 | **max** |
|---|---|---|---|---|---|
| model first token, after a prompt | `UserPrompt` → next, **housekeeping excluded** | 1 074 | 163.0 | 215.2 | **217** |
| model first token, after a tool | `ToolResult` → assistant/TurnEnd | 15 191 | 59.5 | 120.7 | **1 478** |
| model streaming | `AssistantText` → next | 4 696 | 60.8 | 116.6 | 146 |
| model streaming | `Thinking` → next | 83 | 19.9 | 31.8 | 33 |
| **local execution** | `ToolCall` → `ToolResult` | 15 210 | 134.4 | 323.1 | **5 311** |
| idle | `TurnEnd` → next | 806 | 39 732 | 232 752 | 253 118 |

Three conclusions, and they change the design:

1. **~60 s is far too tight for both model-wait phases.** It sits between p95 (41 s) and p99 (163 s)
   for prompts — roughly **1 turn in 25** — and at p99 exactly for the 15 191 tool-result waits.
2. **The phases separate by only ~3.6×, not by orders of magnitude.** Model-wait tops out at
   1 478 s; local execution at 5 311 s. Phase-awareness buys ~25 min versus ~90 min. It is worth
   having, but it does not deliver the "catch a hung upstream call in a minute" the card imagines,
   and any implementation claiming it will kill healthy work.
3. **`TurnEnd` must stay excluded**, as the card says — the idle tail is measured in days.

A defensible first cut, ~3× the measured max so a single slow day is not an incident:

| Phase | Deadline |
|---|---|
| model wait (`UserPrompt` / `ToolResult` / `Thinking` / `AssistantText` last) | **20 min** |
| local execution (`ToolCall` last) | **90 min** |
| `TurnEnd` last, or not working | **n/a** — `PastExpectedIdle` already owns this |

### 3.3 Ordering: `Sequence` is arrival order, not time order

Stored sequences are rebased on backfill, so the last row by `Sequence` is not always the last by
time. Measured: **195 of 71 801 adjacent pairs (0.27%) have a negative gap, 72 of them worse than
60 s.** A phase read is therefore occasionally stale by minutes. The mitigation is the one the
working rule already uses — a timestamp tie-break — and it is another reason the phase must be read
*through* `IsWorkingAsync` rather than beside it.

## 4. Coexistence with the load-bearing systems

| System | Interaction | Rule for the new sweep |
|---|---|---|
| **CARD-0035 `AttentionService`** | `PastExpectedIdle` already covers *"past the estimate and NOT mid-turn"* (`2× expected`, 30 min floor, measured 2026-08-17). Its exclusion is explicit: a mid-turn session "is never listed for being slow, however far past the estimate it has run." | **That exclusion is exactly the hole.** The new deadline owns `working == true` and nothing else. No overlap, by construction. |
| **CARD-0055 delivery confirmation** | `GraceConfirmAsync` runs under the per-session queue lock; `NoTranscriptRecord` already has its own kill guard. | Do **not** take the queue lock and do **not** touch message state. Reuse only `AgentSessionRuntime.CatchUpTranscriptAsync(sessionId, ct)` — the fetch-and-persist half with no queue side effects, which exists precisely because the lock cannot be re-entered. |
| **CARD-0055's "never kill on 'the transcript does not contain X'"** | Six records once landed in one burst *at the instant a session was killed*; the kill produced the evidence it was wrong. | **`CatchUpTranscriptAsync` first, re-read the phase, then judge.** `FailNeverStartedAsync` does not do this today (§6.1) — worth fixing while the code is open. |
| **CARD-0072 `ApiErrorRecoveryService`** | Triggered by an API-error **TurnEnd stub**, so the session reads *not working* and the phase is `TurnEnd`; the two mostly cannot collide. The exception is after a ladder rung enqueues a resume: the session becomes working again on a schedule the ladder owns (hourly for Transient/Wall). | **Stand down while `ApiErrorRecovery` has a row with `ResolvedAt IS NULL` for this session.** One clean gate; the ladder is the more specific mechanism and already escalates to Critical on its own caps. |
| **CARD-0006 C1–C4 binding** | An unbound session has no transcript to read, and its correct verdict is *idle*. | Nothing here reads or relaxes C1–C4. `TranscriptBindFailed` is already `FailNeverStartedAsync`'s territory via CARD-0085's `TryRecoverBindRefusalAsync`; the new sweep must call the same recovery before writing Failed. |

## 5. Slices

The card asks to prioritise whatever classifies the most **real** occurrences correctly. All four it
names are the zero-entries case, and that case has shipped and fired 14 times — so the ranking below
is by *remaining* risk, not by the card's ordering, and S1 is not the card's own first bullet.

### S1 — the token-less caller is refused, not silently misrouted (Code, ~small)

**Why first:** it is the only defect on this card that is certainly open, reproducible on demand
(§2), and 100% silent to the caller. It needs no measurement and no new heuristic.

- `ResolveCallerAsync`: stop inheriting `Directory.GetCurrentDirectory()`. A no-token request keeps
  the manual/UI meaning but carries **no inherited directory** — `Caller(null, null, "")` — so
  `DelegationWorkspaceResolver` raises its existing *"No working directory was given and the caller
  has none to inherit"* instead of silently authorising the server's own folder.
- Because that makes `workingDirectory` mandatory on the no-token path, the rejection when it is
  outside the roots must say **why the caller has no root**: name `Delegation:AllowedRoots` *and*
  state that a token-less request inherits nothing. The current message tells an agent to edit
  config it may not need to edit.
- The endpoint comment's second half ("no parent, no reply routing") becomes a **response field**,
  not a comment: the created task's DTO should carry the fact that no reply will be routed, so a
  shell caller learns it at creation instead of never.
- Tests: `DelegationUnitTests` / a new `AgentTaskCallerResolutionTests` — no-token + no directory is
  refused; no-token + an allowed root succeeds and reports no reply routing; token path unchanged.
- **Decision for the caller (§7).** An alternative is to configure `Delegation:AllowedRoots` to
  `["C:\\src"]` and leave the code alone. That fixes the 422 and not the silence, and it widens what
  *any* delegate may point a task at. Recommended: do the code change; do not widen the roots.

### S2 — a task that is working forever has a ceiling (Code, ~medium)

**Why second:** this is the deadline the card is actually right about not existing. `TimeoutMinutes`
is dead config, and `PastExpectedIdle` explicitly declines the mid-turn case.

- Give `RolePolicyEntry.TimeoutMinutes` its meaning as a **hard wall-clock ceiling** on
  `Dispatched`/`Working`, in a new `FailOverdueTasksAsync` sweep next to the other three.
- **The default must change.** 5 of 247 Succeeded tasks (2.0%) ran past 60 minutes; the longest ran
  2 732. Ship `0 = off` and set real per-role values only where measured, or ship a uniform ceiling
  well above the observed p99 (88.6 min) — e.g. 240. **Do not enable 60.**
- It **fails**, never escalates — the card's argument, and `FailNeverStartedAsync`'s existing
  precedent. Reason text must name the last phase and the last entry's age, so the failure is
  diagnosable without opening the session.
- Reuse before deciding: `CatchUpTranscriptAsync`, then `TryRecoverBindRefusalAsync`, then judge.
  Stand down while an unresolved `ApiErrorRecovery` row exists (§4).
- Surface it in `AttentionService` as its own kind (`Overdue`) *before* it fires, the way
  `NeverStartedGrace` (2 min) already previews `FailNeverStartedAsync`'s 10.
- Tests: `AgentTaskDeliveryWatchdogTests` sibling — a working task past the ceiling fails with the
  phase named; one under it does not; one with an unresolved API-error recovery is left alone; a
  bind-refusal recovery still wins.

### S3 — the phase-aware deadline (Code, ~medium; depends on S2)

Only worth building once S2's ceiling exists, because S3 is a *tightening* of it, not a replacement.

- Phase is read **only when `IsWorkingAsync` is true**, from the last entry, with the timestamp
  tie-break of §3.3. No fourth working-rule implementation.
- Deadlines from §3.2 (20 min model wait / 90 min local execution), configurable, defaulting to the
  measured values — and the plan should record that these are ~3× the observed max, not guesses.
- Same failure semantics, same stand-down gates as S2.
- Tests must include the three housekeeping negatives from §3.1 — a `<local-command-stdout>` tail, an
  interrupt marker, a compaction continuation prompt — each of which must **not** fire.

### S4 — the escalation scan (Docs, ~small)

Not a code change. The card's own argument is that escalation is the wrong response to this class of
failure, and the measurement says the scan is inert (0/299). Record in `DelegationSettings` that it
is a deliberate, narrow ladder for `Debug` and not a health check, and point at S2/S3 as the health
path — so the next reader does not re-derive §1.2 from scratch.

## 6. Found while investigating, worth doing in the same pass

1. **`FailNeverStartedAsync` judges without pulling.** It goes straight from
   `HasTurnPromptSinceAsync` to `TryRecoverBindRefusalAsync` to `FailAsync` + `KillAsync`. CARD-0055
   established that the live stream is not a reliable clock and that the kill can produce the very
   records that prove it wrong. One `CatchUpTranscriptAsync` before the predicate costs one runner
   round trip per suspect, 10 minutes after dispatch, on a query that returns nothing on a healthy
   day.
2. **`DeliveryFailTimeoutMinutes`' doc-comment is stale** — it still says "ZERO transcript entries"
   after CARD-0077 changed the predicate.

## 7. Deliberately not in scope

- **Widening `Delegation:AllowedRoots`** (§S1). It is a security boundary; loosening it to fix an
  ergonomics bug trades a silent failure for a silent authorisation.
- **Changing the escalation gates.** Making Plan/Code/Review escalatable would send half the fleet's
  most expensive work up a tier on a stall whose usual cause is a lost prompt.
- **Any second implementation of working/idle.** Three run in lockstep; a fourth is a defect by the
  repo's own standing rule.
- **The card's incidental card-description bug** — shipped, verified live (§1.3).
- **Cancelling or restarting on a deadline.** Every sweep here fails and reports; retry stays a
  human click, as it is for `FailNeverStartedAsync`.

## 8. Card housekeeping

CARD-0020 should be corrected in place before S1 starts, because its description is now the most
misleading artefact in this area:

- Its central claim ("there is no deadline on a task at all") was true for four hours on 2026-08-09
  and has been false since `10d379e`. A future reader will otherwise re-plan a shipped feature.
- `fe53500d` did not sit silent — it was failed by the watchdog at 21:54:18Z.
- The incidental card bug is fixed (CARD-0019, `c14a009`), and `Description` is `text`, not
  `varchar(4000)`.
- The `~60s` figures should carry §3.2's measurements, so the implementing slice does not inherit
  them as a specification.

The card stays open for S1–S3.
