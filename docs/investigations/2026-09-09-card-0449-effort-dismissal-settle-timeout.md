# CARD-0449 follow-up: effort-dialog dismissal reports "settle deadline exhausted" despite the dialog actually clearing

- **Date**: 2026-09-09
- **Trigger**: live dispatch (markdown-package project, task `f418105f`, session `23b5663c-fbce-40ea-8ec9-613764086827`) hit `EffortDialogNotCleared` with detail `requested=xhigh; current=xhigh; suggested=high; selected=Keep; Enter=1; settle deadline exhausted.`
- **Scope**: Investigate only. No fix implemented.

## 1. Mechanism under test

`ClaudeEffortPrompt.ResolveAsync` (`src/Antiphon.Agents.Pty/ClaudeEffortPrompt.cs:113-192`) polls the rendered
screen every 50ms. Once the dialog stops parsing (`Parse(screen) is null`) and at least one Enter has been
sent, it only counts a poll toward the "cleared" verdict when:

```csharp
if (enters > 0 && !HasRemnant(screen)
    && (ClaudeScreen.ComposerIsLive(screen) || nextModal is not null))
{
    ... 
    if (++clearFrames >= 2) return Result(true, "two clear observations");
}
else clearFrames = 0;
```
(`ClaudeEffortPrompt.cs:149-158`)

It needs **two consecutive** polls satisfying that condition inside the 15s `ClaudeEffortPromptSettleMs`
budget (`server/Application/Settings/AgentRegistrySettings.cs:6`; wired at
`server/Infrastructure/Agents/SessionRunner/RunnerClaudeAdapter.cs:170-179`). Any poll where the condition is
false resets the counter to 0 — a single bad frame between two good ones is enough to never reach 2.

`ComposerIsLive` (`src/Antiphon.Agents.Pty/ClaudeScreen.cs:56-61`) is a substring test over the whole compacted
screen for `"forshortcuts"` or `"bypasspermissionson"` — i.e. it depends on the hint-bar row
(`⏵⏵ bypass permissions on (shift+tab to cycle) · ← for agents`) being intact in whatever snapshot was taken.

## 2. What the actual PTY bytes show (session `23b5663c`, task `f418105f`)

Pulled via `GET /sessions/23b5663c-fbce-40ea-8ec9-613764086827/snapshot` (endpoint was still live). The raw
VT stream for the dialog dismissal, decoded:

1. Effort dialog was showing `Use Fable 5.1 at high effort by default?` with `> Keep xhigh` highlighted —
   matches `requested=xhigh; current=xhigh; suggested=high; selected=Keep` exactly (Current=xhigh from the
   `Keep` row, Suggested=high from the question).
2. One Enter is sent (`Enter=1` matches). The dialog's 5-6 lines are cleared (`[2K[1A` x5).
3. The border + placeholder composer prompt are redrawn.
4. The hint-bar row is redrawn as `⏵⏵ bypass permissions on (shift+tab to cycle) · ← for agents`, then
   **cleared from column 64 to end-of-line** (`[64G...[K`) — this erases the `◉ xhigh · /effort`
   segment that normally sits at column ~102 on that same row, without redrawing it yet.
5. Cursor drops one row below the hint bar and writes, at column 53, `You've used 83% of your weekly limit ·
   resets 12am (Europe/London)` — i.e. **the usage-limit banner occupies the row directly under the hint
   bar, in the exact screen region the effort indicator (`◉ xhigh · /effort`) normally ends up.**
6. Five more rows below that are blanked (leftover dialog explanatory text/blank lines).
7. Cursor repositions with a **whole-line clear** (`[2K`, not clear-to-EOL) near the hint-bar area,
   then `◉ xhigh · /effort` is written back onto the hint-bar row at column 102, restoring the row to its
   normal single-line form (hint bar + effort indicator together, banner row now empty/reused).
8. Final `renderedScreen` (post-strip by the terminal renderer) is the fully normal, single-line hint bar
   `⏵⏵ bypass permissions on (shift+tab to cycle) · ← for agents ◉ xhigh · /effort` with no visible banner —
   i.e. by the time this snapshot was captured, the screen had already fully settled to a clean state and
   `ComposerIsLive` would read `true` on it.

## 3. Confirmed vs. not confirmed

**Confirmed**: the usage-limit banner, when present, is not a static addition — the CLI inserts and later
retracts an extra row directly beneath the hint bar, and that redraw touches the same row range the effort
indicator lives on (clear-to-EOL on the hint-bar row itself in step 4, then a further whole-line clear in
step 7 before the indicator is restored). This is strictly more redraw churn in the exact region
`ComposerIsLive` and the dialog-remnant checks inspect, compared to the banner-absent case (which only needs
to repaint `◉ {effort} · /effort` inline, no extra row insert/retract). This is a real, reproducible
mechanism by which banner-present dismissals accumulate more transitional frames than banner-absent ones in
the same code path the two-consecutive-clean-frame check depends on.

**Not confirmed**: whether this is actually what burned the full 15s budget in this incident. The single
`GET .../snapshot` pull only returns the PTY's current accumulated buffer, not a sequence of timestamped
polls, so it cannot show whether the CLI emitted steps 4-7 as one atomic write (in which case a 50ms poll
would never observe a "bad" intermediate frame at all — the terminal-emulation layer applies a full buffered
write set before any snapshot reads it) or as several separate, time-separated writes (plausible if the
usage-percentage banner requires an async fetch before it can render, independent of the dialog-dismissal
redraw) — only the latter would let 50ms polling actually catch the transitional "hint bar cleared to column
64" or "whole-line-cleared" frames enough times to repeatedly reset `clearFrames` to 0 and exhaust the
budget. The final captured `renderedScreen` here is already fully clean, which is consistent with either:
(a) settlement genuinely completed quickly and this snapshot was pulled well after the reported failure, or
(b) the CLI kept re-emitting this clear/insert-banner/retract-banner cycle on a timer for as long as the
banner condition was true, and this is just the last cycle before the resolver gave up and the CLI's own
redraw loop then produced one more, unobserved-by-the-resolver, clean frame.

No per-poll instrumentation exists today (`ClaudeEffortPrompt.ResolveAsync` does not log per-iteration
`ComposerIsLive`/`Parse` results), so this residual timing question cannot be settled from stored evidence
alone. Confirming it requires either instrumented logging on a repro run, or a fixture that replays timed,
separate PTY writes for banner-insert/retract and observes whether `ResolveAsync` mis-times against it.

## 4. Refutation check on the "banner is a constant fixture" framing

Caller's refinement is consistent with the evidence: the banner text is written as an extra row insert/retract
around the existing hint-bar redraw, not baked into the dialog-clear path itself — nothing in
`ClaudeEffortPrompt.cs` or `ClaudeScreen.cs` special-cases the banner today, and the mechanism above would
equally apply to *any* text the CLI might transiently insert in that same row band (not usage-limit banners
specifically), so a fix must not special-case the banner string — it must make the settle/composer-live check
robust to an arbitrary extra transient row near the hint bar, present or absent.

## 5. Recommended fix scope (not implemented)

- Stop gating "cleared" on a single-poll substring match of the hint bar. Prefer: dialog no longer parses
  (`Parse(screen) is null`) AND no remnant (`!HasRemnant`) AND the *stable*, animation-stripped screen
  (`ClaudeScreen.Stable`) is unchanged across 2 consecutive polls — i.e. reuse the existing `IsSettled`
  quiescence primitive (`ClaudeScreen.cs:44-45`) instead of `ComposerIsLive`, since `Stable` already strips
  spinner/counter/hint-bar noise by design and comparing two stripped snapshots for equality does not care
  whether an extra banner row is present, only whether it has stopped changing.
- Keep the existing contradiction check (`CurrentEffort` vs. `desired`) as an independent verdict — it reads
  the top banner line (`Fable 5.1 with xhigh effort · Claude Max`), which this incident's evidence shows is
  untouched by the bottom-of-screen banner churn, so it stays a reliable signal regardless of fix approach.
- Any fix must be verified in both a banner-present and a banner-absent fixture/replay (per the caller's
  refinement) — the current test suite (`ClaudeScreenTests.cs`, `RunnerClaudeAdapterEffortPromptTests.cs`)
  has no banner-churn case today.

## Evidence

- `src/Antiphon.Agents.Pty/ClaudeEffortPrompt.cs:113-192` (ResolveAsync, the settle loop)
- `src/Antiphon.Agents.Pty/ClaudeScreen.cs:56-61` (ComposerIsLive), `:27-41` (Stable/IsSettled)
- `src/Antiphon.Agents.Pty/ClaudeBlockingPrompt.cs:141-184` (Detect — confirms the banner alone never parses
  as a blocking modal, so `nextModal` is not a fallback here)
- `server/Application/Settings/AgentRegistrySettings.cs:6` (`ClaudeEffortPromptSettleMs = 15_000`)
- Live snapshot: `GET http://localhost:17204/sessions/23b5663c-fbce-40ea-8ec9-613764086827/snapshot`
  (task `f418105f`), decoded in §2 above — raw VT bytes captured 2026-09-09, `startedAt`
  `2026-09-09T07:46:18.2725409Z`, `lastSequence: 19`.

## Correction (Plan stage, task `c6d64663`, 2026-09-09)

Replaying the same bytes (`C:\logs\antiphon\session-runner\23b5663cfbce40ea8ec9613764086827.ansi.log`)
through the production `TerminalScreen` reproduces the runner's `renderedScreen` exactly, and that screen is
**not** clean: rows 7–8 hold two stacked rules, row 9 holds ` Use Fable 5.1 at high effort by default?`, and
row 12 holds `task. You can change this any time with /effort.` — above and below a composer painted over the
dialog's area. On every post-dismissal frame `ComposerIsLive` is true, `Parse` is null and `HasRemnant` is
true; no further bytes arrived, so the settle loop failed the **remnant** gate on every poll for ~13 s. §1's
hint-bar mechanism and §3's banner churn are not the cause: a replay with the banner write excised ghosts
identically, and an xterm-style emulator (pyte 0.8.2) renders both variants clean. The divergence is
`TerminalScreen` wrapping immediately when a character lands in the last column, where ConPTY's renderer
assumes deferred wrap; Claude's full-width rules shift every later row-relative move by one row. §5's
`IsSettled` recommendation cannot clear that screen on its own (the ghost title is stable content). Fix
design, frame-by-frame evidence and fixtures:
[2026-09-09-card-0449-effort-dismissal-settle-plan.md](../superpowers/plans/2026-09-09-card-0449-effort-dismissal-settle-plan.md).
