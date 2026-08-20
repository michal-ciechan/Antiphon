# CARD-0073 — transcript binding runs through the guarded fallback on every launch: plan

**Date:** 2026-08-19 (written 2026-08-20)
**Status:** planned
**Card:** CARD-0073 (Backlog) — filed 2026-08-17 off four launches in one five-minute window.
**Precedent:** CARD-0006 (the C1–C4 adoption rules), CARD-0064 (`94947f1`, shipped 2026-08-20
00:21 — the queued-delivery C4 evidence, in the same file this card asks about),
CARD-0047 (`6072027`, the trust dialog), CARD-0003/CARD-0020 (the boot-prompt watchdog).
**Evidence:** measured 2026-08-20 against the live sidecar store
(`C:\logs\antiphon\session-runner\transcripts`, 194 sidecars / 143 Claude), the live
`AgentIncidents` table, the live `.ansi.log` set, and a four-arm A/B against the real Claude CLI.
Nothing below is carried over from the card's numbers.

This is a planning document only. Do not write the fix in the Plan pass.

## Verdict up front

**The card asks the wrong question, for a reason that is itself a finding: the evidence it reasoned
from is structurally incapable of reporting an exact bind.** Discovery did become the normal path —
but on a specific date, as a regression, not as the design. And the failure the card actually cares
about (`10e30ff7`, which produced nothing and raised nothing) has a precise, separate cause that no
fast-path would touch.

| Question the card asks | Answer |
|---|---|
| Is discovery the normal path rather than the fallback? | **Since 2026-08-17 07:47, yes — for named launches only, and it is a regression.** Before that boundary every bound Claude session bound `exact` (59/59). After it, launches carrying `--name` bind `discovery` 57 times against 1 exact; launches without `--name` still bind `exact` 2/2. |
| Does an exact bind ever happen? | **Yes, constantly — the card could not have seen it.** `TryBind` deliberately suppresses the `SessionTranscriptBound` event for `exact` and `sidecar` (`TranscriptTailer.cs:382`), and `OnHeuristicBindAsync` drops the incident entirely when no `Agents` row has `PersistentSessionId == sessionId` — which delegate task sessions do not. 57 discovery binds produced **5** incidents. "All three binds report discovery, none reports exact" was guaranteed by construction. |
| Is "one launch in four" still accurate? | **No. It is ~7%, and more than half the historical failures were a different, already-fixed bug.** 13 of 143 Claude sidecars never bound (9.1%); **7 of those 13** are the `antiphon-check-interpreter` launches of 08-16 20:03–20:28 — CARD-0047's trust dialog, fixed at 22:27 that night. Genuine "ran but never bound" is **6 of 143 = 4.2%**. |
| Is there a faster deterministic path for the unambiguous case? | **No, and it should not be built.** Discovery is not slower than exact (median 15.3s vs 15.7s launch→bind, p90 313s both). Both are floored by the same fact: the transcript does not exist until the first submit. A fast path buys ≈0 latency and costs the property CARD-0006 exists to hold. |
| So what is the actual fix? | **Two independent things, neither of them a fast path:** close the silence that let `10e30ff7` die unreported (S1), and restore the exact bind by removing what broke it (S2). |

## 1. What the sidecars say, which is the record the card lacked

`TranscriptSidecar.How` is written for **every** bind, including `exact` and `sidecar` — it is the
only complete account of bind method anywhere in the system. Claude-format sidecars, by day:

| day | exact | discovery | sidecar | unbound | unbound % |
|---|---|---|---|---|---|
| 08-13 | 9 | 0 | 0 | 1 | 10.0% |
| 08-14 | 9 | 0 | 0 | 0 | 0.0% |
| 08-15 | 5 | 0 | 0 | 0 | 0.0% |
| 08-16 | 30 | 0 | 2 | 7 | 17.9% |
| 08-17 | 7 | 32 | 0 | 2 | 4.9% |
| 08-18 | 0 | 18 | 0 | 2 | 10.0% |
| 08-19 | 2 | 7 | 9 | 1 | 5.3% |

The 08-17 row is a boundary inside one day, not a trend. The last `exact` bind is **07:44:56**
(`task-a8ad3b2f`); the first `discovery` bind is **07:50:09** (`task-c930aeb8`). Everything before is
exact; essentially everything after is discovery.

**It is not a Claude version change.** Both sides of the boundary ran **2.1.233** (read from the TUI
banner in the `.ansi.log`s). Across the whole corpus 2.1.233 accounts for 42 exact *and* 32
discovery. Model, effort and worktree-vs-main do not discriminate either (discovery splits 28 main /
29 worktree).

**The discriminator is `--name`:**

| era | launch shape | exact | discovery |
|---|---|---|---|
| before 08-17 07:47 | `--name` present | 54 | 0 |
| before | no `--name` | 5 | 0 |
| after 08-17 07:47 | `--name` present | 1 | 57 |
| after | no `--name` | 2 | 0 |

`--name` has been passed since `1d278fb` (07-22) and was harmless for three weeks, so the flag is not
itself the change — something about how Claude *treats* a named session changed at that boundary,
inside one CLI version. A server-side config/rollout is the obvious candidate and is **not
established**.

Claude does not merely pick a different filename: it adopts a different session id outright. Every
discovery-bound file records its own self-chosen id in `sessionId`, and no `<our-id>.jsonl` exists
anywhere under the projects root.

**What was ruled out, by experiment.** `AgentRegistry` neutralizes five Claude "nesting markers"
(`CLAUDECODE`, `CLAUDE_CODE_CHILD_SESSION`, …) by setting them to the **empty string**, and the
obvious hypothesis was that Claude had switched from a truthiness test to a presence test, which
would silently un-neutralize them. Four arms against the real CLI in a fresh cwd — markers unset,
markers empty (production's shape), markers inherited with real values, and `--name` added —
**all four honoured `--session-id`**. The hypothesis is refuted; empty-string neutralization still
works.

That experiment also bounds what is knowable from print mode: `--name` only does anything in the
interactive TUI (it writes the `custom-title` and `agent-name` meta records and sets the terminal
title), which `-p` never exercises. Confirming the mechanism needs a real pty, which is S2's first
step and not a thing to skip — the card names CARD-0014 and CARD-0065 as the cost of fixing before
measuring.

## 2. Why a "deterministic fast path" is the wrong fix

The card's hypothesis is that C1–C4 exist to disambiguate between *multiple* candidates, so a launch
with exactly one candidate (or zero) could skip the guarded probe. Three reasons to reject it:

1. **It misreads what C1–C4 are for.** They do not disambiguate; they *prove ownership*. "Exactly one
   candidate" is not evidence of ownership. The 2026-08-09 incident bound the operator's own
   conversation in a shared cwd — and in that cwd the operator's file can easily be the only
   cwd-matching one, because the agent's own file does not exist yet. A count-based fast path binds
   precisely that file. This is the trade the card forbids.
2. **It buys no latency.** Launch→bind, from the sidecars' own `childStartUtc`→`updatedAtUtc`:
   exact median **15.7s** (n=59, p90 313s), discovery median **15.3s** (n=57, p90 313s). Both floor
   at ~13s on the same cause — nothing exists to bind until the first prompt is submitted. Discovery's
   old 2940.8s tail is the CARD-0064 case and is already fixed.
3. **It does nothing for the case the card was filed about.** A fresh worktree has *zero*
   cwd-matching candidates, not one. A one-candidate fast path never fires there.

**Do not touch C1–C4.** Nothing in this plan changes an adoption rule.

## 3. The real defect: an unbound session with no candidates and a live child is silent forever

`10e30ff7` "raised no incident of any kind" because it structurally cannot. There are three fault
paths in `TranscriptTailer` and none covers it:

- **`ReportRootFault`** — only fires when the projects *root* is missing or unreadable.
- **`MaybeReportRefusal`** — returns immediately on `verdict.Refusals.Count == 0`
  (`TranscriptTailer.cs:634`). Refusals are only appended for candidates that already passed **C2**
  (cwd match). A fresh worktree's project dir holds no transcripts at all ⇒ zero candidates ⇒ zero
  refusals ⇒ **no fault, ever**.
- **`ReportMissingAfterChildExit`** — only on child *exit*, and only if input was delivered. A child
  parked on a modal or a wedged composer never exits.

So the one state with no reporting path is exactly "fresh worktree, child alive, nothing bound" —
the card's case. The incident data confirms it: `TranscriptBindFailed` (kind 15) exists for **12
distinct sessions all-time**, and **none at all on 08-17 or 08-18**, including `10e30ff7` — whose
551 KB `.ansi.log` proves the session ran hard for ten minutes. The silence is a hole, not a fluke.

The blindness compounds: `TranscriptBindingIncidentService.OnHeuristicBindAsync` early-returns when
`agent is null`, and delegate task sessions have no `Agents` row pointing at them, so both the
success signal and the failure signal are dropped for the population this card measured.

## 4. Slices

### S1 — a session that has not bound, and should have, says so (tier: sonnet)

The defect above, and it stands alone: independent of S2, worth shipping even if the exact bind never
returns.

- Add an unbound-too-long fault to `LocateAsync`: child alive, input delivered, nothing bound after a
  configured window (60s is the existing `_refusalFaultDelay`, and reusing it keeps one knob) ⇒ raise
  `TranscriptBindFailed` once, repeating on the existing `RefusalFaultRepeat` cadence.
- Carry the **candidate census** in the detail — files under the root, how many matched cwd, how many
  were refused and why — so the report distinguishes "no candidate existed" from "candidates existed
  and all were refused". Today those two produce identical silence and need different fixes.
- Fix the `agent is null` early return in `TranscriptBindingIncidentService` so a delegate session's
  bind outcome is not dropped. Resolve the owner the way `ChannelReplyDispatcher.ReportLostAsync`
  does, and when nothing owns the session log at Error rather than returning.
- Tests: `TranscriptBindingIncidentTests` for the delegate-owner path; a
  `TranscriptAdoptionSafetyTests` case for zero-candidates-child-alive raising exactly one fault.

Would have caught `10e30ff7` at ~60s instead of the boot watchdog's 10 minutes.

### S2 — measure `--name`, then restore the exact bind (tier: opus)

**Measure first.** A real-pty A/B — same cwd, same CLI version, `--name` present vs absent, n≥5 each
— using the existing harness (`Antiphon.Agents.Pty.Tests`, `ModernConPtyConnection`). Print mode
cannot reproduce this and neither can `winpty` from a tool session; this needs the repo's own pty.
Record the result in `docs/investigations/`.

If `--name` is confirmed: stop passing it at launch (`AgentControlService.cs:168`,
`AgentTaskDispatcher.cs:1207`) and set the TUI title another way, or accept its loss.

**The one real trade, and it is the caller's call:** C2b reads the `agentName` record that `--name`
writes. Dropping the flag removes that evidence. It is acceptable *only* because an exact bind needs
no C2b at all — but any session that still falls through to discovery would lose C2b, which is the
rule that correctly refused `74bef32b` in the card's own table. Two ways to keep both, to be decided
with the measurement in hand: keep `--name` and accept discovery, or drop it and accept a weaker
fallback for the rare session that still needs one.

Payoff if it lands: ~98% of launches leave the guarded fallback entirely, so C1–C4 stop being
load-bearing on every single session — the card's actual concern — **without weakening them by one
line**.

### S3 — surface `How`, so this is not filesystem archaeology next time (tier: sonnet)

`TranscriptSidecar.How` is the only complete record of bind method and nothing reads it. Expose it
(attention projection or a runner endpoint) so "are we on the fallback?" is answerable without a
python sweep over `C:\logs`. This investigation needed one; the next should not.

## 5. Not in scope

- Any change to C1–C4, including a "one candidate" or "zero candidates" shortcut.
- The CARD-0064 probe changes (`94947f1`), which are correct and orthogonal — this plan proposes
  nothing in `TranscriptCandidateProbe`.
- Chasing *why* Claude changed behaviour inside 2.1.233. If it is a server-side rollout it is outside
  our control and could revert; S1 is what makes either direction survivable.

## 6. Housekeeping

`C:\Antiphon\worktrees\card-task-10e30ff7` still exists but is no longer what the card describes:
**0 files** (the 3606 are gone, presumably the weekly Windmill sweep). What remains is a directory
skeleton whose `tests/Antiphon.Agents.Pty.Tests/bin/Debug/net9.0` will not delete —
`Device or resource busy`, not the `Permission denied` the card recorded, i.e. a live handle rather
than a transient lock. It needs the holder identified, not another retry.
