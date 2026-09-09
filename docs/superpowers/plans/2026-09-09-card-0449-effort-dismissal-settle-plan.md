# CARD-0449 follow-up: effort-dialog dismissal reports "settle deadline exhausted" — plan

Date: 2026-09-09. Stage: Plan (task `c6d64663`, Frontier, shared checkout). Investigate
authority: task `a78ee39f`, [investigation](../../investigations/2026-09-09-card-0449-effort-dismissal-settle-timeout.md).
Prior plan: [readiness plan](2026-09-08-card-0449-claude-effort-dialog-readiness-plan.md) (D-1..D-6, S1..S4).
Next stage: **decide** — this plan is written under defaults D-1..D-7 below, and D-1 re-scopes the fix
from the brief's "replace the composer gate" to a `TerminalScreen` emulator correction plus the
requested resolver hardening. If the defaults are accepted, dispatch **test-design**.

## Premise correction, in one paragraph

The brief's root cause ("`ComposerIsLive`'s hint-bar substring check is defeated by usage-banner
redraw churn before two consecutive polls can agree") is not what the stored bytes show. Replaying the
failing session's raw PTY stream through the production emulator (`TerminalScreen`) reproduces the
runner's rendered screen exactly, and on every frame after the dismissal `ComposerIsLive` is **true**,
`Parse` is null, and `HasRemnant` is **true** — because the rendered screen still holds a ghost row
` Use Fable 5.1 at high effort by default?` (row 9) and a ghost `task. You can change this any time
with /effort.` (row 12) above and below a composer that was painted over the dialog's area. No further
bytes arrived for the remaining ~13 s, so the settle loop failed the remnant gate on ~270 consecutive
polls. The ghost rows come from `TerminalScreen` wrapping **immediately** when a printable character
lands in the last column, where xterm, Windows Terminal, and ConPTY's own renderer assume a **deferred**
wrap (the cursor stays on the row until the next printable). Claude's full-width 120-column rules
therefore push our cursor one row too far, every row-relative move after them (`\r\e[1B`, `\e[2K\e[1A`)
lands one row off, and the dialog's erase-and-repaint leaves stale rows behind. The usage-limit banner
is incidental: a replay with the banner bytes excised ghosts identically. Replacing `ComposerIsLive`
with `IsSettled`, as the brief scopes, cannot clear this screen — the ghost title is stable content,
not animated chrome, and the remnant gate would still block.

## Ground truth

Code references checked at `17682a49`. Replay evidence is in the next section.

| Brief / investigation assumption | What the code and the bytes show | Consequence |
|---|---|---|
| The post-dismissal frames intermittently lack the hint bar, so `ComposerIsLive` (`src/Antiphon.Agents.Pty/ClaudeScreen.cs:56-61`) flips false and resets `clearFrames`. | Both post-dismissal frames of the real stream contain `⏵⏵ bypass permissions on (shift+tab to cycle)` (compact `bypasspermissionson`), on `TerminalScreen` and on an independent xterm-style emulator. `ComposerIsLive` is true on every polled frame. | Item 1 of the brief is hardening, not the fix. Keep it (D-2) but do not present it as the fix. |
| The two-consecutive-poll gate was defeated by repeated redraw churn. | The session's whole output is 3,957 bytes in the ansi log; nothing arrives after the dismissal repaint. The screen the resolver polled for ~13 s is static and identical to the `GET /snapshot` `renderedScreen`. The gate failed on `!HasRemnant(screen)` (`ClaudeEffortPrompt.cs:103-104`, `:149`) every poll. | The defect is deterministic, not timing-dependent, and reproducible offline from stored bytes. |
| "Final `renderedScreen` … is the fully normal, single-line hint bar with no visible banner." (investigation §2 step 8) | The hint-bar row is normal; rows 7, 8, 9 and 12 are not: two stacked rules, the dialog title, and one explanatory line remain (see the frame table below). The investigation read the hint bar only. | The remnant gate is doing its job on a wrong screen. Fix the screen. |
| The usage-limit banner's insert/retract is the mechanism. | With the banner write (`\e[53G…You've used 83%…(Europe/London)`, 88 chars) excised from the stream, `TerminalScreen` still renders the same ghosts; pyte renders clean with and without it. | Banner-present and banner-absent fixtures are still required (D-5) because the banner shifts what the CLI erases, but the fix must not and does not special-case it. |
| `ClaudeScreen.Stable`/`IsSettled` strips the noise that defeats the gate. | True for spinners, counters and hint bars. The ghost title is content and survives `Stable`; two settled frames both carry it. | `IsSettled` alone would not clear the failing screen. |
| Whether the CLI's redraw is one atomic write or several time-separated writes matters to the fix. | Unknown and unrecorded: the pty audit store (`PtySessionAudit`, `%TEMP%\antiphon-pty-audits`) is empty, and the runner feeds `TerminalScreen` per ≤4,096-byte read chunk (`PtyAgentRunner.cs:153-165`). It cannot matter here — a static screen failed for 13 s. | D-4 records a per-attempt settle summary so the next incident is answerable from the launch-block reason; D-5's replay test exercises whole / per-frame / 256-byte chunking so the resolver is proven under both hypotheses. |
| `TerminalScreen` is a faithful grid for ConPTY output. | `WriteChar` (`TerminalScreen.cs:148-159`) wraps as soon as `_cursorCol` reaches `Cols`. ConPTY's renderer emits a 120-char rule, then `\r`, then `\e[1B` and expects the next text one row below the rule; our cursor is already a row lower before the `\r`. Every full-width row shifts everything after it by one row. | This is the root cause and is shared by every rendered-screen consumer (runner snapshots, all adapters, the readiness probe). D-1 fixes it. |
| The dismissal path was verified working in CARD-0449's prior rounds. | The retained server logs (2026-09-05..09-09) contain exactly one `Claude startup: requested=` line — this failure. No live dismissal has ever logged `two clear observations`; prior verification was the scripted `EffortTestScreen` fake (whose non-dialog screen is three clean lines) plus an unverified real-picker acceptance. | The parser, intent and selection logic stay untouched (D-6). The dismissal gate has never been exercised against a real rendered screen until now; D-5 makes that the regression. |
| Prior plan D-3: clearance needs "positive evidence of the next UI: `ComposerIsLive` or a different known startup modal". | Superseded by D-2: clearance is two consecutive settled, remnant-free, non-parsing frames. The downstream composer probe (`ClaudeStartupReadiness.RunAsync`, `ComposerInputProbe`) remains the positive proof the composer accepts input. | Record the supersession here; no other prior decision changes. |

## Evidence: offline replay of the real bytes

Sources, all still present at plan time:

- Raw PTY stream: `C:\logs\antiphon\session-runner\23b5663cfbce40ea8ec9613764086827.ansi.log` (3,957 bytes; session
  `23b5663c-fbce-40ea-8ec9-613764086827`, task `f418105f`, started `2026-09-09T07:46:18Z`, 120×30).
  `GET http://localhost:17204/sessions/23b5663c-fbce-40ea-8ec9-613764086827/snapshot` still answers 200 with the
  same `rawOutput` (2,751 chars) and the ghosted `renderedScreen`.
- Server log `server/logs/antiphon-20260909.log`: `Failed to start interactive agent session` at 08:46:42 local,
  reason `requested=xhigh; current=xhigh; suggested=high; selected=Keep; Enter=1; settle deadline exhausted.`
- Three earlier dialog-only streams (paint, no dismissal — those sessions were killed before the CARD-0449 fix
  existed): `9184ec6d…`, `2968433b…`, `787bfee2….ansi.log` in the same directory (2,631–2,637 bytes each; no
  usage banner in any of them). Their `renderedScreen`s are the JSON captures already embedded as
  `Card0449.EffortCaptures.json`.

Method: a throwaway console harness (scratchpad, not committed) referencing `Antiphon.Agents.Pty.csproj` fed the
stream to `new TerminalScreen(120, 30)` split at each `\e[?25l` (Ink frame start) and evaluated
`ClaudeEffortPrompt.Parse`, `HasRemnant`, `ClaudeScreen.ComposerIsLive`, `IsSettled` after each piece. The same
pieces were fed to `pyte 0.8.2` (`pyte.Screen(120, 30)`), a Python VT emulator with deferred wrap, as the
independent reference. pyte is an analysis aid only; it is not a test dependency.

| Piece | `TerminalScreen` (production) | pyte (deferred wrap) | Gates on `TerminalScreen` |
|---|---|---|---|
| 1 — initial composer paint | rule+banner row 5, composer row **7**, rule row **8**, hint bar row **10** | rule+banner 5, composer **6**, rule **7**, hint bar **8** | parse=null remnant=false composer=true |
| dialog paint (up to the dismissal erase) | rules rows 7 **and** 8, title row **9**, explanatory 11–12, cost 14, `> Keep xhigh` **16**, `Switch…` **17** | rule 5, title **6**, explanatory 8–9, cost 11, Keep **13**, Switch **14** | parse=menu (Keep highlighted) — the parser is row-agnostic, so selection works either way |
| 2 — Enter, erase, composer repaint, banner row | rows 7–8 rules, **9 ghost title**, 10 composer, 11 rule, **12 ghost `task. You can change…`**, 13 hint bar (indicator cleared), 14 banner | 5 rule+banner, 6 composer, 7 rule, 8 hint bar (indicator cleared), 9 banner | parse=null **remnant=true** composer=true |
| 3 — banner row cleared, `◉ xhigh · /effort` restored | same ghosts; 13 hint bar complete | 5–8 clean; banner gone | parse=null **remnant=true** composer=true settled=true |
| banner bytes excised (synthetic) | identical ghosts to piece 3 | clean | parse=null **remnant=true** composer=true |

The byte-level mechanism, from the dialog paint: `\e[4A` + 120 × `─` + `\r` + `\e[1B` + ` Use Fable 5.1 at high
effort by default?` + `\r\e[1B` + … Under deferred wrap the cursor is still on the rule's row after the 120th
`─`; `\r` returns to column 1; `\e[1B` moves to the row below the rule; the title lands there. `TerminalScreen`
line-feeds on the 120th `─`, so the title lands two rows below the rule. The initial paint's rule
(`…\e[106Gtask-f418105f\e[120G─\e[39m\r\n`) has the same shape, which is why the composer is already one row low
before the dialog appears and the hint bar two rows low (two full-width rules above it). At dismissal the CLI
erases upward from where it believes the cursor is (`\e[2K\e[1A` ×5, `\e[2K\e[G\e[1A\e[5A`) and repaints the
composer region there; on our grid that region is offset, so rows the CLI never meant to keep survive.

## Decisions

**D-1 — Fix the root cause: `TerminalScreen` implements deferred (last-column) wrap.**
`src/Antiphon.Agents.Pty/TerminalScreen.cs` gains a `_pendingWrap` flag. When a printable character is written
in the last column, the cell is written, `_cursorCol` stays at `Cols - 1`, and `_pendingWrap` is set — no line
feed. The next printable first performs `_cursorCol = 0; LineFeed();` (respecting the scroll region as today),
clears the flag, then writes. The flag is cleared, without moving, by `\r`, `\n`, `\b`, `\t`, and by every
cursor-positioning CSI already handled (`A B C D E F G H f d`); it is left unchanged by SGR, erase (`J K X`),
insert/delete (`L M P @`), scroll (`S T`) and DECSTBM (`r`, which repositions the cursor and therefore clears it
as part of that move). `CursorCol` reports `Cols - 1` while pending. This is xterm's and Windows Terminal's
"delayed EOL wrap" and it is what ConPTY's VT renderer assumes when it emits `\r` + relative moves after a
full-width row.
Reasons: the replay shows every downstream ghost traces to this one divergence; the fix is ~15 lines in one
class with a complete unit-test story; every rendered-screen consumer (runner snapshots, the UI's screen reads,
trust/effort/permission detectors, composer probe, Codex/Grok adapters) gets a row-accurate grid.
Rejected: (a) *resolver-only change per the brief's item 1* — `IsSettled` is true on the ghosted screen and the
remnant gate still blocks; it would ship as a non-fix. (b) *Relax `HasRemnant` so a lone title row no longer
blocks* — tolerates an emulator defect that every other detector still suffers, and trades away the guard
against typing into a half-rendered live dialog (see D-3). (c) *Special-case the usage banner* — refuted by the
excised replay; the investigation's own §4 forbids it. (d) *Ask ConPTY for a different output mode or move to
absolute cursor addressing* — not ours to control; the renderer's output is correct for a compliant terminal.
Blast radius acknowledged: `TerminalScreen` is a Pty-owner surface (`docs/session-runtime-invariants.md`,
`docs/adr/0002-modern-conpty-backend.md`); the change is behaviour-visible in `CursorRow`/`CursorCol` after a
full-width write and in the row index of everything painted after a full-width row. No production consumer
indexes rows (`GetRow`/`FindRow`/`SnapshotRow` have no callers outside `PtyAgentRunner`); tests that pinned the
old row positions are tests of the defect and are corrected in S1. DECAWM off (`\e[?7l`) stays unmodelled
(private modes are skipped today; ConPTY has not been observed emitting it) — note it in the class comment.

**D-2 — Resolver clearance is two consecutive settled, remnant-free, non-parsing frames.**
In `ClaudeEffortPrompt.ResolveAsync` (`src/Antiphon.Agents.Pty/ClaudeEffortPrompt.cs:142-161`) the "cleared"
verdict becomes: with `enters > 0`, a frame *qualifies* when `Parse(screen) is null && !HasRemnant(screen)`;
keep the last qualifying frame; when the next poll also qualifies **and** `ClaudeScreen.IsSettled(previous,
current)` is true, run the existing `CurrentEffort` contradiction check on the current frame and return
`Cleared = true` ("two settled clear observations"). A non-qualifying frame drops the kept frame. A qualifying
but unsettled pair keeps the newer frame and continues. `ClaudeScreen.ComposerIsLive` and the
`ClaudeBlockingPromptDetector.Detect(screen) is not null` alternative leave the gate; `ComposerIsLive` itself
stays in `ClaudeScreen` for its other callers and tests. The 50 ms poll, the 1,500 ms `HighlightSettle`, the
three-Enter cap and the `ClaudeEffortPromptSettleMs` budget are unchanged.
Reasons: this is the brief's item 1 and the investigation's recommendation; it removes the resolver's only
dependency on hint-bar wording (a future CLI that reworded the hint bar would otherwise fail every launch); a
next startup modal that appears and holds still is accepted by the same rule, so the special case is
unnecessary; the composer probe that follows is the positive proof of an accepting composer, so the resolver's
job is only "the dialog is gone and the screen has stopped moving".
Rejected: *require `ComposerIsLive` as well* — no frame in the evidence ever lacked it, it adds nothing to
safety once the emulator is correct, and it keeps the wording dependency. *Accept on either signal (OR)* —
two acceptance paths to pin and no observed need.

**D-3 — `HasRemnant` stays strict: a lone dialog title still blocks.**
A title row without option rows is either a ghost (an emulator or CLI defect we want to fail loudly on) or a
dialog mid-repaint (which D-2's settle pair waits out). Neither should let the readiness probe type into the
screen. With D-1 the observed ghost cannot recur; if a new misalignment appears, the launch fails with the
row named (D-4) instead of silently typing into a dialog.
Rejected: remnant = option rows only (see D-1 (b)).

**D-4 — Make the next settle failure self-describing without a diagnostic build.**
`ResolveAsync` counts polls, qualifying frames, and remembers the last gate that rejected a frame:
`parse` (dialog still parses), `remnant:"<first matching row, ≤ 60 chars>"`, or `unsettled`. The
`ClaudeEffortResolution.Detail` keeps its existing prefix (`requested=…; current=…; suggested=…; selected=…;
Enter=N; <cause>`) — `RunnerClaudeAdapterEffortPromptTests` asserts those fields — and appends
` [polls=P clear=C last=<gate> composer=<live|absent>]` before the trailing remedy sentence. `ResolveAsync`
gains an optional `Action<string>? trace = null` parameter invoked only when the gate outcome changes between
polls (not every 50 ms); `ClaudeStartupReadiness.RunAsync` passes its `log`, so the `Claude startup:` log line
and the persisted launch-block reason carry the summary. For this incident the reason would have read
`… settle deadline exhausted [polls=~270 clear=0 last=remnant:"Use Fable 5.1 at high effort by default?"
composer=live]`, which is the whole diagnosis. No per-poll logging at Information level, no new setting, no
new field on the record or the DTOs.
Rejected: per-poll Debug logging (noise, and the runner-side adapter logs through the server logger only at
Information); a separate diagnostic build (nobody runs it when it matters).

**D-5 — Fixtures: real byte streams, replayed through the real emulator, in both banner states.**
Add `tests/Antiphon.Agents.Pty.Tests/golden/card-0449/` (already shipped by the csproj's `golden\**\*`
Content glob): `effort-dismissal-23b5663c.ansi` (the real banner-present dismissal, byte-for-byte),
`effort-dismissal-23b5663c-banner-excised.ansi` (synthetic: the same bytes with the single banner write
`\e[53G\e[38;2;255;193;7mYou've used 83% of your weekly limit · resets 12am (Europe/London)` removed — the
cursor moves around it are kept; labelled synthetic in a `README.md` beside the files with the excision
regex and both SHA-256s), and the three real dialog-only paints `effort-dialog-9184ec6d.ansi`,
`effort-dialog-2968433b.ansi`, `effort-dialog-787bfee2.ansi`. The files contain the cwd, task banners, the
CLI version and the operator's usage/timezone banner text; no secrets (Code re-checks before committing).
Tests replay them through `TerminalScreen(120, 30)` in three chunkings — one write, split at `\e[?25l`, and
fixed 256-byte pieces — so both the "atomic" and "time-separated" hypotheses from the investigation are
covered by construction. The resolver test drives `ClaudeEffortPrompt.ResolveAsync` with a replay-backed
snapshot function: before Enter it serves the dialog frame; on `\r` it feeds the dismissal bytes (in the
chosen chunking, one chunk per poll) and must clear with `Enter=1`. A **real** banner-absent dismissal capture
does not exist yet; it is an acceptance item, not a gate: the next time a Frontier launch shows the picker
with usage below the banner threshold, save its ansi log beside the others and add it as a fourth
`Arguments` row. Do not fabricate one from the JSON captures (they are rendered screens, not bytes).

**D-6 — Scope guard.**
No change to `ClaudeEffortPrompt.Parse`, `ClaudeEffortIntent`, `ClaudeEffortMenu.Select`, the navigation /
Enter / retry / budget logic, `ClaudeBlockingPromptDetector`, `ClaudeScreen.Stable`, `AgentRegistrySettings`,
either Claude adapter, `ClaudeStartupReadiness` beyond passing `log` as `trace`, or the JSON capture fixture.
The `EffortTestScreen` fake gains two knobs (below) and nothing else.

**D-7 — Documentation.**
`docs/session-runtime-invariants.md`: one new gotcha under the Pty section — deferred wrap is a requirement
of any terminal that consumes ConPTY output; full-width rows are the tell; "a clean hint bar is not a clean
screen: read the rows". `docs/investigations/2026-09-09-card-0449-effort-dismissal-settle-timeout.md`: a
short correction section appended by this Plan stage pointing here (done in this commit). The class comment
on `TerminalScreen` names the invariant and the DECAWM-off gap.

## Design detail

### `TerminalScreen` deferred wrap (S1)

```csharp
private bool _pendingWrap;

private void WriteChar(char c)
{
    if (_pendingWrap) { _pendingWrap = false; _cursorCol = 0; LineFeed(); }
    if (in bounds) _cells[_cursorRow][_cursorCol] = c;
    if (_cursorCol == Cols - 1) _pendingWrap = true; else _cursorCol++;
}
// '\r', '\n', '\b', '\t' and CSI A/B/C/D/E/F/G/H/f/d/r: _pendingWrap = false (then the existing move).
// SGR, J/K/X, L/M/P/@, S/T: unchanged, flag untouched.
```

`\t` already writes spaces through `WriteChar`, so it naturally consumes a pending wrap first; pin that.
The reference behaviour for the fixture streams is the pyte column of the frame table above; the test
asserts structure (title directly under its rule; `Keep`/`Switch` two rows apart from the cost line; after
dismissal no row matches `HasRemnant` and the composer is the row directly under the banner rule) plus the
exact row numbers for the `23b5663c` stream (5/6/7/8, banner at 9 until the last frame).

### `ResolveAsync` clearance (S2)

```text
string? kept = null;             // last qualifying frame
loop every 50 ms while within budget:
  screen = snapshot(); polls++
  if Parse(screen) is menu: kept = null; last = "parse"; …existing navigation/Enter path…; continue
  if enters == 0: kept = null; continue
  if HasRemnant(screen): kept = null; last = "remnant:\"<row>\""; continue
  if kept is not null && IsSettled(kept, screen):
      observed = CurrentEffort(screen); if observed is not null && observed != desired → contradiction (unchanged)
      clear++; return Result(true, "two settled clear observations")
  if kept is not null: last = "unsettled"
  kept = screen; clear = 1
on deadline: Result(false, "settle deadline exhausted") — Detail carries [polls= clear= last= composer=]
```

`trace` fires when `last` changes or when `kept` transitions null↔non-null. `composer=` is
`ComposerIsLive(lastScreen)` at the moment of the verdict — diagnostic only, never a gate.

### `EffortTestScreen` knobs (S2 tests)

`HintBar` (default `"? for shortcuts"`; set `""` to prove D-2 no longer needs chrome) and
`Churn` (an `IEnumerable<string>` of non-dialog screens served round-robin after the accepting Enter until
exhausted, then the normal cleared screen — proves an unsettled pair never clears and a settled pair does).
Both are additive; every existing test keeps its current behaviour.

## Slices and file list

| Slice | Files | Change and exit evidence |
|---|---|---|
| S1: emulator deferred wrap | `src/Antiphon.Agents.Pty/TerminalScreen.cs`; `tests/Antiphon.Agents.Pty.Tests/TerminalScreenTests.cs` | Implement D-1. New unit tests: `Writing_the_last_column_keeps_the_cursor_on_the_row`, `The_next_printable_after_a_full_row_wraps_first`, `A_carriage_return_after_a_full_row_stays_on_that_row`, `Cursor_moves_clear_a_pending_wrap` (parameterised over `A B C D G H d`), `Erase_in_line_keeps_a_pending_wrap`, `A_full_row_then_CRLF_advances_exactly_one_row`. Correct any existing assertion that pinned the immediate wrap (none found by grep; `CursorRow_and_CursorCol_reflect_current_position` is unaffected). Run `TerminalScreenTests`, `ClaudeGoldenReplayTests`, `ClaudeInteractionTests`, `ClaudeTuiModeTests`. |
| S2: resolver settle gate and diagnostics | `src/Antiphon.Agents.Pty/ClaudeEffortPrompt.cs`; `src/Antiphon.Agents.Pty/ClaudeStartupReadiness.cs` (pass `log` as `trace`); `tests/Antiphon.Agents.Pty.Tests/EffortTestScreen.cs`; `tests/Antiphon.Agents.Pty.Tests/ClaudeEffortPromptTests.cs`; `tests/Antiphon.Tests/Agents/RunnerClaudeAdapterEffortPromptTests.cs` | Implement D-2, D-3, D-4. New tests: `Clearance_does_not_require_composer_chrome` (`HintBar = ""` clears, `TokenWrites` unchanged), `An_unsettled_pair_never_clears_and_a_settled_pair_does` (`Churn` of two alternating screens for N polls: no clear while alternating, clear after), `A_lone_ghost_title_still_blocks_and_is_named` (Override = clean composer + ghost title row; `Cleared == false`, `Detail` contains `last=remnant:"Use `), `Settle_failure_detail_summarises_the_polls` (`SwallowEnters = 100`, `Detail` matches `\[polls=\d+ clear=\d+ last=\w+`), and the adapter test asserts `LaunchBlock.Reason` contains `polls=`. Existing `Malformed_effort_remnants_do_not_count_as_clearance`, `Requested_effort_is_applied`, `Redraw_is_not_clearance_before_the_probe`, `A_contradictory_resulting_effort_fails_readiness`, `A_scrolled_away_effort_banner_does_not_block_confirmed_clearance` stay green unchanged. |
| S3: real-byte fixtures and replay regressions | new `tests/Antiphon.Agents.Pty.Tests/golden/card-0449/{README.md, effort-dismissal-23b5663c.ansi, effort-dismissal-23b5663c-banner-excised.ansi, effort-dialog-9184ec6d.ansi, effort-dialog-2968433b.ansi, effort-dialog-787bfee2.ansi}`; new `tests/Antiphon.Agents.Pty.Tests/ClaudeEffortDismissalReplayTests.cs` | Implement D-5. Tests (`[Category("Unit")]`, pure, no process): `Real_dialog_paints_directly_under_its_rule` (4 streams × 3 chunkings: `Parse` returns the Keep-highlighted menu, title row = rule row + 1), `Real_dismissal_leaves_no_dialog_rows` (banner-present, banner-excised × 3 chunkings: `HasRemnant` false, `Parse` null, composer row = banner-rule row + 1, hint bar present), `Resolver_clears_a_replayed_dismissal` (same matrix through `ResolveAsync`: `Cleared`, `Enter == 1`, one `\r` written, `Detail` contains `two settled clear observations`), `Fixture_hashes_are_pinned` (SHA-256 per file as recorded in the README). |
| S4: docs | `docs/session-runtime-invariants.md`; `src/Antiphon.Agents.Pty/TerminalScreen.cs` (class comment); this plan's eventual verification section | Implement D-7. |

Order: S1 → S3 (the replay tests are the acceptance for S1 and go red-then-green against it) → S2 → S4.
Commit each slice; the S3 fixtures commit separately from the tests so the byte files are reviewable alone.

## Verification requirements for TestDesign

Append the executable `## Verification design` section here, following `server/Bundles/stage-test-design.md`,
with V-n/R-n/PC-n rows. Cover at minimum:

- Every new test above, method-scoped filters, and the three chunking modes as explicit `Arguments` rows.
- Positive controls (all method-scoped red/restored-green; batchable where files differ):
  PC-A `TerminalScreen.WriteChar` wraps immediately again → `Real_dismissal_leaves_no_dialog_rows` (banner-present)
  red on the `HasRemnant` assertion, and `Resolver_clears_a_replayed_dismissal` red on `Cleared` — this is the
  incident, reproduced. PC-B `\r` no longer clears the flag → `A_carriage_return_after_a_full_row_stays_on_that_row`
  red. PC-C `IsSettled(kept, screen)` replaced by `true` → `An_unsettled_pair_never_clears…` red (clears while
  alternating). PC-D `!HasRemnant` dropped from the qualifying test → `A_lone_ghost_title_still_blocks…` red.
  PC-E remnant excerpt omitted from `Detail` → the same test's `last=remnant:"Use ` assertion red, `Cleared`
  still false. PC-F `CurrentEffort` contradiction branch disabled → existing
  `A_contradictory_resulting_effort_fails_readiness` red (the prior plan's PC-8; re-run to prove D-2 kept it).
- Regression lanes after all slices: `Antiphon.Agents.Pty.Tests` classes `TerminalScreenTests`,
  `ClaudeGoldenReplayTests`, `ClaudeScreenTests`, `ClaudeEffortPromptTests`, `ClaudeStartupReadinessTests`,
  `ClaudeEffortDismissalReplayTests`, `ComposerInputProbeTests`, `ClaudeStartupTrustPromptTests`, then the
  FakeClaude-driven `ClaudeInteractionTests`/`ClaudeTuiModeTests` (emulator is shared); `Antiphon.Tests`
  `RunnerClaudeAdapterEffortPromptTests`, `RunnerClaudeAdapterTrustPromptTests`, `AgentSessionLaunchFailureTests`,
  then the `[Category=Unit]` lane. Never run the two assemblies concurrently; build to `bin-c449b/` and delete
  every `bin-c449b` directory afterwards.
- Acceptance (not a gate): the headed `ClaudeEffortPromptCanaryTests` when eligible, and the real banner-absent
  capture from D-5 when one occurs. The live proof is the next Frontier dispatch whose launch-block reason is
  absent and whose `Claude startup:` line reads `two settled clear observations`; until then the live
  dismissal path's record is 0 successes / 1 failure and this plan says so.

## Commands

```powershell
# S1/S3 acceptance (Pty assembly; forward slash on the output path)
dotnet run --project tests/Antiphon.Agents.Pty.Tests --property:OutputPath=bin-c449b/ -- --treenode-filter "/*/*/ClaudeEffortDismissalReplayTests/*"
dotnet run --project tests/Antiphon.Agents.Pty.Tests --property:OutputPath=bin-c449b/ -- --treenode-filter "/*/*/TerminalScreenTests/*"
# S2
dotnet run --project tests/Antiphon.Agents.Pty.Tests --property:OutputPath=bin-c449b/ -- --treenode-filter "/*/*/ClaudeEffortPromptTests/*"
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-c449b/ -- --treenode-filter "/*/*/RunnerClaudeAdapterEffortPromptTests/*"
```

## Out of scope

Modelling DECAWM off; the Herdr pane path (`herdr.ReadScreenAsync`, its own renderer); any change to poll
interval or budgets; the UI's terminal view (xterm.js consumes raw bytes, not `renderedScreen`); a fix for the
emulator's ignored DECSC/DECRC (`\e7`/`\e8`), which the streams here use only around the initial clear.

## Verification design

Appended 2026-09-09 by TestDesign task `f27a1212` against plan commit `47fdc804`. D-1..D-7 and S1..S4 above
remain the fix design; this section is executable work for Code. The row indices and hashes below were pinned by
replaying the four retained streams through pyte (deferred wrap), the plan's reference emulator, in all three
chunkings; they are the post-fix expectation for `TerminalScreen`, not what it renders today. Next stage: **code**.

### Harness contract

**Fixtures.** `tests/Antiphon.Agents.Pty.Tests/golden/card-0449/`, each real file copied byte-for-byte from
`C:\logs\antiphon\session-runner\<id>.ansi.log` (binary copy, no re-encoding). Before the fixture commit add
`tests/Antiphon.Agents.Pty.Tests/golden/card-0449/*.ansi -text` to `.gitattributes` — the repo's `* text=auto`
under `core.autocrlf=true` may otherwise rewrite the `\r\n` pairs — and confirm with
`git ls-files --eol tests/Antiphon.Agents.Pty.Tests/golden/card-0449/` that every row shows `attr/-text`.

| File | Bytes | SHA-256 | Provenance |
|---|---:|---|---|
| `effort-dismissal-23b5663c.ansi` | 3,957 | `18c89534612034390d52a09b2e96093687b4be379f98a2d03684620f01ee9d4c` | real; banner-present dismissal |
| `effort-dismissal-23b5663c-banner-excised.ansi` | 3,868 | `15f7381f74850dc508e26eb0b32810bf87e7bc11d95e86c0bac4e0c637f9b23b` | **synthetic**: the real file with the 89-byte span at byte offsets 3694–3782 removed and nothing else. The span is exactly `\e[53G\e[38;2;255;193;7mYou've used 83% of your weekly limit · resets 12am (Europe/London)` (`·` is two bytes); it occurs once. |
| `effort-dialog-9184ec6d.ansi` | 2,637 | `791729350b491bb6805e72e2b79dc7e3b706fe2a37c9a72b44a07b11c72d03b2` | real; dialog paint, no dismissal |
| `effort-dialog-2968433b.ansi` | 2,631 | `cb41ba5defe7afa6089a93dff943d714c02391deb9812ccddf97fa39cbcd77e1` | real |
| `effort-dialog-787bfee2.ansi` | 2,631 | `c478644172a24472166370c4af7470ddf5442957e35583b0623ce2db98a21a30` | real |

`README.md` beside them repeats this table, the excision span, and the derivation rule (`excised == real[..3694] + real[3783..]`).
Byte-exact structure markers, present in every file: Ink frames start at `\e[?25l` (three in the dismissal
stream, two in each dialog-only stream); the dismissal begins at the **first `\e[2K\e[1A`** (byte 2636 of the real
dismissal file; absent from the dialog-only files). The dialog frame of the dismissal stream is everything before
that marker. No `\r` from the Enter key is echoed in the stream, so no fixture editing is needed to split it.

**Chunkings** — `[Arguments]` rows named `"whole"`, `"frames"`, `"pieces"`. Decode a file once with
`Encoding.UTF8.GetString` (the files carry no BOM). `whole`: one `Feed`. `frames`: split *before* each
`\e[?25l`, empty pieces dropped. `pieces`: successive 256-char slices, except that a slice that would end between
an ESC and the final byte of its sequence (CSI final byte `@`..`~`; OSC terminator BEL or `\e\`; the second char of
a two-char ESC sequence) is extended to include that final byte. The extension is required: in every fixture a
plain 256-byte cut lands inside an escape sequence (real dismissal: byte offsets 1024, 2048, 2560, 3840) and
inside a UTF-8 multibyte character (offset 256), and `TerminalScreen.Feed` keeps no parser state between calls, so
a naive cut would fail on the parser, not on deferred wrap. Every replay test asserts **chunk invariance**:
`GetRows()` after `frames` and after `pieces` equals `GetRows()` after `whole`.

**Pinned rows** (post-fix; 0-based, as `TerminalScreen.GetRows()` / `FindRow` index them):

- *Dialog frame* (all four streams; the dismissal stream's is the bytes before the first `\e[2K\e[1A`): row 5 is
  120 × `─`; row 6 is ` Use Fable 5.1 at high effort by default?`; rows 8–9 the explanatory text; row 11 contains
  `estimated cost`; row 13 trims to `> Keep xhigh`; row 14 trims to `Switch Fable 5.1 to high effort`.
  `Parse` → Model `Fable 5.1`, Current `xhigh`, Suggested `high`, Highlight `Keep`. `HasRemnant` true.
- *Final frame* (both dismissal fixtures, every chunking): row 5 ends with ` task-f418105f ─`; row 6 contains
  `Try "how do I log an error?"`; row 7 is 120 × `─`; row 8 contains `bypass permissions on` and
  `◉ xhigh · /effort`; rows 9–29 empty. `HasRemnant` false, `Parse` null, `ClaudeScreen.ComposerIsLive` true,
  `CurrentEffort` = `xhigh`, `CursorRow == 6`, `CursorCol == 2`.
- *Intermediate frame* (banner-present fixture, `frames` chunking, after the piece that starts at the first
  `\e[2K\e[1A` and ends before the last `\e[?25l`): as the final frame, plus row 9 =
  `You've used 83% of your weekly limit · resets 12am (Europe/London)` starting at column 52, and row 8 without
  `◉ xhigh · /effort`. `ClaudeScreen.IsSettled(intermediate, final)` is **false** (the banner row survives
  `Stable`), so in this chunking the resolver's settle pair spans one extra poll.

**Resolver replay driver** (S3). `screen = new TerminalScreen(120, 30)`; feed the dialog frame; queue the
dismissal in the chosen chunking. `snapshot`: once a `\r` has been written, feed one queued piece (if any), then
return `GetScreenText()`. `write`: record the key. Intent `ClaudeEffortIntent.Read(["--effort", "xhigh"])`,
budget 8,000 ms, no sleeps — the resolver's own 50 ms cadence paces the pieces.

**D-1 clarification (tab).** D-1's decision text governs: `\t` clears a pending wrap without moving. The
design-detail sentence "tab naturally consumes a pending wrap first" describes the pre-fix path and is superseded.
Code must also bound the tab loop: today `\t` calls `WriteChar(' ')` until the next stop, and once `WriteChar`
stops advancing past `Cols - 1` that loop never terminates for a tab issued in the last column. Rule: spaces are
written for the columns strictly before `min(next stop, Cols - 1)`, the cursor stops there, and the flag is not
re-armed. V-7 pins it with a `[Timeout(5000)]`.

**`last=remnant` excerpt.** The first `HasRemnant`-matching row after `Row()` trimming (spaces, tabs, `│┃║`),
truncated to 60 chars. For the incident: `Use Fable 5.1 at high effort by default?`. The summary
` [polls=P clear=C last=G composer=live|absent]` is appended to every `Result` (success and failure) between the
cause and its full stop; `last` starts as `none`; `composer` is `ComposerIsLive` of the last polled screen.

**`EffortTestScreen` knobs** (D-6, additive only). `HintBar` (`string`, default `"? for shortcuts"`; rendered as
the cleared screen's last line, `""` renders no hint line) and `Churn` (`IReadOnlyList<string>`; after the
accepting Enter each snapshot serves the next element until the list is exhausted, then the normal cleared
screen; churn screens count as neither `ClearObservations` nor `Dialog`).

### Proves it works now

Unless marked existing, every method is new. Parameterised methods report each named row. `TS` =
`TerminalScreenTests` (helper `S(cols, rows)`), `RP` = `ClaudeEffortDismissalReplayTests` (new, `[Category("Unit")]`,
no process), `E` = `ClaudeEffortPromptTests`, `SR` = `ClaudeStartupReadinessTests`, `A` =
`RunnerClaudeAdapterEffortPromptTests`. Cursor tuples are `(CursorRow, CursorCol)`.

- V-1: a printable in the last column leaves the cursor on that row | unit | TS.`Writing_the_last_column_keeps_the_cursor_on_the_row` — `S(10,3)`, feed `ABCDEFGHIJ` | row 0 `ABCDEFGHIJ`, cursor (0,9), row 1 empty
- V-2: the next printable wraps first | unit | TS.`The_next_printable_after_a_full_row_wraps_first` — feed `ABCDEFGHIJK` | row 0 `ABCDEFGHIJ`, row 1 `K`, cursor (1,1)
- V-3: `\r` after a full row stays on that row | unit | TS.`A_carriage_return_after_a_full_row_stays_on_that_row` — feed `ABCDEFGHIJ\rX` | row 0 `XBCDEFGHIJ`, row 1 empty, cursor (0,1)
- V-4: CRLF after a full row advances exactly one row | unit | TS.`A_full_row_then_CRLF_advances_exactly_one_row` — feed `ABCDEFGHIJ\r\nX` | row 1 `X`, row 2 empty, cursor (1,1)
- V-5: `\b` after a full row steps back on that row | unit | TS.`Backspace_after_a_full_row_steps_back_on_that_row` — feed `ABCDEFGHIJ\bX` | row 0 `ABCDEFGHXJ`, row 1 empty, cursor (0,9)
- V-6: cursor-positioning CSI clears the flag | unit | TS.`Cursor_moves_clear_a_pending_wrap`, rows `A B C D E F G H f d r` — `S(10,3)`, feed `\e[2;1H` + `ABCDEFGHIJ` + the sequence + `X` | A `\e[1A` → row 0 `         X`; B `\e[1B` → row 2 `         X`; C `\e[1C` → row 1 `ABCDEFGHIX`; D `\e[1D` → row 1 `ABCDEFGHXJ`; E `\e[1E` → row 2 `X`; F `\e[1F` → row 0 `X`; G `\e[3G` → row 1 `ABXDEFGHIJ`; H `\e[3;2H` → row 2 ` X`; f `\e[3;2f` → row 2 ` X`; d `\e[3d` → row 2 `         X`; r `\e[1;3r` → row 0 `X`. In every row the other two rows are unchanged and no row received `X` by wrapping.
- V-7: `\t` clears the flag without moving and cannot hang | unit | TS.`A_tab_in_the_last_column_clears_the_wrap_and_terminates` `[Timeout(5000)]`, rows `"pending"` (`ABCDEFGHIJ\tX`), `"landing"` (`ABCDEFGHI\tX`), `"middle"` (`AB\tX`) | feed returns; pending and landing: row 0 `ABCDEFGHIX`, row 1 empty, cursor (0,9); middle: row 0 `AB      X`, cursor (0,9)
- V-8: non-moving sequences keep the flag | unit | TS.`Non_moving_sequences_keep_a_pending_wrap`, rows `K J X P @ L M m S T` — `S(10,3)`, feed `ABCDEFGHIJ` + `\e[<row>`, assert cursor (0,9), then feed `X` | K, J, X, P, @: row 0 `ABCDEFGHI`, row 1 `X`; m: row 0 `ABCDEFGHIJ`, row 1 `X`; L: row 0 empty, row 1 `XBCDEFGHIJ`; M: row 0 empty, row 1 `X`; S: row 0 empty, row 1 `X`; T: row 0 empty, row 1 `XBCDEFGHIJ`
- V-9: a deferred wrap at the scroll bottom scrolls the region on the next printable | unit | TS.`A_deferred_wrap_at_the_scroll_bottom_scrolls_the_region` — `S(10,4)`, feed `\e[2;4r` + `\e[4;1H` + `ABCDEFGHIJ`; assert row 3 `ABCDEFGHIJ`, row 2 empty, cursor (3,9); then feed `K` | row 2 `ABCDEFGHIJ`, row 3 `K`, row 0 empty, cursor (3,1)
- V-10: every existing `TerminalScreen` behaviour holds | unit | `--treenode-filter "/*/*/TerminalScreenTests/*"` | all green; any pre-existing assertion that pinned the immediate wrap is corrected per S1 and named in the Code report
- V-11: real dialog paints put the title directly under the rule | unit | RP.`Real_dialog_paints_directly_under_its_rule`, 4 fixtures × 3 chunkings (12 rows; the dismissal fixture contributes its dialog frame) | the pinned dialog rows; `FindRow("Use Fable 5.1") == 6` and row 5 is 120 × `─`; `Parse` fields as pinned; `HasRemnant` true; chunk invariance
- V-12: a real dismissal leaves no dialog rows | unit | RP.`Real_dismissal_leaves_no_dialog_rows`, 2 fixtures × 3 chunkings (6 rows) | the pinned final rows; `FindRow("Try \"how do I log an error?\"") == FindRow("task-f418105f") + 1`; `HasRemnant` false; `Parse` null; `ComposerIsLive` true; cursor (6,2); chunk invariance; the banner-present `frames` row also asserts the pinned intermediate frame and `IsSettled(intermediate, final) == false`
- V-13: the resolver clears a replayed dismissal with one Enter | unit | RP.`Resolver_clears_a_replayed_dismissal`, 2 × 3 (6 rows) via the replay driver | `Cleared` true; writes exactly `["\r"]`; `Detail` contains `Enter=1` and `two settled clear observations` and matches `\[polls=\d+ clear=2 last=\S+ composer=live\]`; the banner-present `frames` row took ≥ 3 post-Enter snapshots
- V-14: clearance no longer needs composer chrome | unit | E.`Clearance_does_not_require_composer_chrome` — `new EffortTestScreen { HintBar = "" }`, `ResolveAsync()` | `Cleared` true; writes exactly `["\r"]`; `TokenWrites == 0`; `AppliedEffort == "xhigh"`; `Detail` contains `two settled clear observations`
- V-15: an unsettled pair never clears and a settled pair does | unit | E.`An_unsettled_pair_never_clears_and_a_settled_pair_does`, rows `"exhausts"` (`Churn` = `[a, b]` × 10 with `a = "> \n? for shortcuts"`, `b = "> \nSome output line\n? for shortcuts"`, budget 8,000) and `"outlasts-budget"` (`Churn` = `[a, m]` × 100 with `m = "Do you want to proceed?\n1. Yes\n2. No"`, budget 2,600) | exhausts: `Cleared` true, `Enter=1`, snapshots after the Enter ≥ 22, `clear=2`; outlasts-budget: `Cleared` false, `Detail` contains `settle deadline exhausted`, `last=unsettled`, `Enter=1`; `TokenWrites == 0` in both
- V-16: a lone ghost title still blocks and is named | unit | E.`A_lone_ghost_title_still_blocks_and_is_named` — `AfterWrite` on `\r` sets `Override = "Fable 5.1 with xhigh effort · Claude Max\n────\n Use Fable 5.1 at high effort by default?\n> \n────\n? for shortcuts"`, budget 2,600 | `Cleared` false; writes exactly `["\r"]`; `Detail` contains `Enter=1`, `settle deadline exhausted`, `last=remnant:"Use Fable 5.1 at high effort by default?"`, `clear=0`, `composer=live`; `TokenWrites == 0`
- V-17: a settle failure summarises its polls | unit | E.`Settle_failure_detail_summarises_the_polls` — `SwallowEnters = 100`, `ResolveAsync()` | `Cleared` false; `Detail` matches `Enter=3; settle deadline exhausted \[polls=(\d+) clear=0 last=parse composer=absent\]` with the captured polls ≥ 40; the remedy sentence still follows
- V-18: the launch-block reason carries the summary | unit, production adapter | A.`An_effort_dialog_that_never_clears_fails_within_its_budget` (existing, extended) | existing fields plus `polls=` and `last=parse` in `LaunchBlock.Reason`
- V-19: the trace fires on gate changes, not per poll, and is wired from `RunAsync` | unit | SR.`Settle_trace_fires_on_gate_changes_not_per_poll`, rows `"static-hold"` (`SwallowEnters = 100`, `ReadyAsync(totalMs: 5500, maxWrites: 100)` under the same watchdog shape as `Recurring_dialogs_cannot_renew_the_readiness_deadline`) and `"phased"` (after the Enter, `OnSnapshot` serves `""`, then `">\n? for shortcuts"`, then `Template`, then `null` — the `Redraw_is_not_clearance_before_the_probe` shape) | resolver-originated lines are `fake.Trace` entries that neither match `^\d+: (read|write) ` nor start with `requested=`; static-hold: `fake.Snapshots ≥ 40` and such lines ≤ 6; phased: `result.Ready` true, such lines ≥ 2, one containing `parse` and one containing `unsettled`
- V-20: existing resolver and readiness behaviour is intact under D-2 | unit | classes `ClaudeEffortPromptTests`, `ClaudeStartupReadinessTests`, `ClaudeScreenTests`, `ComposerInputProbeTests`, `ClaudeStartupTrustPromptTests` | all green, including `Malformed_effort_remnants_do_not_count_as_clearance`, `Requested_effort_is_applied`, `Retries_respect_settle_and_attempt_limits`, `Redraw_is_not_clearance_before_the_probe`, `A_contradictory_resulting_effort_fails_readiness`, `A_scrolled_away_effort_banner_does_not_block_confirmed_clearance`, and `Startup_dialog_chains_preserve_each_gate` (its effort-then-modal rows are the "next modal that holds still" case D-2 accepts by the same rule)
- V-21: fixture bytes are pinned and the synthetic one is derived | unit | RP.`Fixture_hashes_are_pinned`, 5 rows, plus RP.`Banner_excised_fixture_is_derived_from_the_real_one` | per row: size and SHA-256 of `Path.Combine(AppContext.BaseDirectory, "golden", "card-0449", name)` equal the table; excised bytes == real bytes with `[3694, 3783)` removed
- V-22: the docs and class comment say it | docs check | `grep -n "Gotcha #90" docs/session-runtime-invariants.md`; `grep -n -i "deferred" docs/session-runtime-invariants.md src/Antiphon.Agents.Pty/TerminalScreen.cs`; `grep -n "DECAWM" src/Antiphon.Agents.Pty/TerminalScreen.cs`; `grep -n "polls=" docs/session-runtime-invariants.md`; `grep -c "two positive clear frames" docs/session-runtime-invariants.md`; `grep -c "two positive dismissal frames" docs/agent-kinds.md` | the first four ≥ 1 hit each (the new gotcha follows `### Gotcha #89`); the last two return 0 — the existing CARD-0449 paragraph in the invariants doc and the effort bullet in `docs/agent-kinds.md` now describe two consecutive settled, remnant-free, non-parsing frames and the `[polls= clear= last= composer=]` summary
- V-23: the shared emulator still serves its other consumers | unit / Pty | classes `ClaudeGoldenReplayTests`; `Antiphon.Tests` classes `RunnerClaudeAdapterEffortPromptTests`, `RunnerClaudeAdapterTrustPromptTests`, `AgentSessionLaunchFailureTests`; then `"/*/*/*/*[Category=Unit]"` | all green; a red anywhere is triaged against the base commit before it is attributed

Acceptance, not gates: (a) headed real-CLI lanes `ClaudeInteractionTests` and `ClaudeTuiModeTests`
(`ANTIPHON_HEADED_TESTS=1`, `claude`/`cl` on PATH — they drive the real CLI, not FakeClaude, contrary to the
lane note above); (b) `ClaudeEffortPromptCanaryTests` when its gates pass; (c) the real banner-absent dismissal
capture from D-5 — never fabricated; (d) the next Frontier dispatch whose `Claude startup:` line reads
`two settled clear observations` with `clear=2` and whose launch block is absent. Until (d) the live record stays
0 successes / 1 failure.

### Guards the regression

- R-1: `WriteChar` is "simplified" back to advance-then-wrap, or a new grid class copies the old loop | caught by V-1, V-12, V-13 because the cursor sits one row low after every full-width row and the ghost title returns; V-13 fails on `Cleared` with `last=remnant:"Use Fable 5.1 at high effort by default?"` — the incident
- R-2: a refactored or newly added control/CSI handler forgets to clear `_pendingWrap` | caught by V-3, V-5, V-6 because `X` wraps one row too far
- R-3: a handler that must leave the flag alone starts clearing it ("reset cursor state on SGR") | caught by V-8 because `X` lands on row 0 instead of wrapping
- R-4: the tab loop is unbounded again or re-arms the flag | caught by V-7 because the `[Timeout]` trips or row 1 receives `X`
- R-5: the wrap-first line feed bypasses the scroll region | caught by V-9 because row 2 stays empty
- R-6: the clearance gate regains a chrome dependency (`ComposerIsLive`, hint-bar wording) | caught by V-14 because a hint-less composer never clears
- R-7: the settle pair is dropped or weakened (one qualifying frame; `IsSettled` replaced by raw equality or `true`) | caught by V-15 because "outlasts-budget" clears and "exhausts" clears before the churn ends
- R-8: `HasRemnant` leaves the gate or is loosened to option rows | caught by V-16 and existing `Malformed_effort_remnants_do_not_count_as_clearance` because a title-only screen clears
- R-9: an immediate-accept path for a "next modal" is reintroduced without the settle pair | caught by V-15 "outlasts-budget" because the alternating permission modal clears
- R-10: the summary or the remnant excerpt drop out of `Detail` / `LaunchBlock.Reason` in a `Result` refactor | caught by V-16, V-17, V-18 because the `polls=` / `last=remnant:"Use ` assertions fail
- R-11: `RunAsync` stops passing `log` as `trace`, or the trace becomes per-poll | caught by V-19 because the phased row sees no resolver lines / the static-hold row sees more than 6
- R-12: git normalisation or a hand edit changes fixture bytes | caught by V-21 because the SHA-256 differs
- R-13: the emulator becomes chunk-order dependent (a future partial-sequence buffer that mishandles a boundary) | caught by the chunk-invariance assertions in V-11, V-12 because `frames`/`pieces` rows differ from `whole`
- R-14: Claude reformats the dialog geometry | caught by V-11 naming the rows — re-capture, never loosen (the `ClaudeGoldenReplayTests` rule)

### Positive controls

Each control: apply the mutation to the committed fixed source, build to `bin-c449b/`, run only the named
methods with `--treenode-filter "/*/*/<Class>/<Method>"` (a parameterised method runs all its rows; the report
names which rows went red), confirm the **named** assertion failed (not a build error, fixture error or zero-test
run), restore with `git checkout -- <file>` (which refreshes the timestamp), rebuild, run the same methods green.
Report all three results per PC.

- PC-1: break the deferred wrap by restoring `_cursorCol++; if (_cursorCol >= Cols) { _cursorCol = 0; LineFeed(); }` in `TerminalScreen.WriteChar` (flag never set); expect red: TS.`Writing_the_last_column_keeps_the_cursor_on_the_row` (`CursorRow` 1), RP.`Real_dialog_paints_directly_under_its_rule` (title row 9, every row), RP.`Real_dismissal_leaves_no_dialog_rows` (`HasRemnant` true, every row), RP.`Resolver_clears_a_replayed_dismissal` (`Cleared` false; `Detail` shows `last=remnant:"Use Fable 5.1 at high effort by default?"`) — the incident reproduced offline
- PC-2: break `\r` by removing its `_pendingWrap = false` in `Feed`; expect TS.`A_carriage_return_after_a_full_row_stays_on_that_row` red (row 1 is `X`)
- PC-3: break `G` by removing its flag clear in `HandleCsi`; expect TS.`Cursor_moves_clear_a_pending_wrap` red on the `G` row only (row 2 receives `X`), the other ten rows green
- PC-4: break the tab bound by removing the `Cols - 1` limit from the tab loop; expect TS.`A_tab_in_the_last_column_clears_the_wrap_and_terminates` red on `pending` and `landing` by `[Timeout(5000)]` (or by row 1 receiving `X`), never a hung run
- PC-5: break the region by replacing the wrap-first `LineFeed()` with `_cursorRow++`; expect TS.`A_deferred_wrap_at_the_scroll_bottom_scrolls_the_region` red (row 2 empty after `K`)
- PC-6: break the settle pair by replacing `ClaudeScreen.IsSettled(kept, screen)` with `true` in `ResolveAsync`; expect E.`An_unsettled_pair_never_clears_and_a_settled_pair_does` red on both rows (exhausts: fewer than 22 post-Enter snapshots; outlasts-budget: `Cleared` true)
- PC-7: break the remnant gate by dropping `!HasRemnant(screen)` from the qualifying test; expect E.`A_lone_ghost_title_still_blocks_and_is_named` red (`Cleared` true) and existing E.`Malformed_effort_remnants_do_not_count_as_clearance` red
- PC-8: break the excerpt by emitting `last=remnant` without the quoted row; expect E.`A_lone_ghost_title_still_blocks_and_is_named` red on the `last=remnant:"Use ` assertion while `Cleared` is still false — the report names that assertion
- PC-9: break the contradiction check by disabling the `CurrentEffort` branch; expect SR.`A_contradictory_resulting_effort_fails_readiness` red (`Outcome` not `EffortFailed`) — the prior plan's PC-8, re-run to prove D-2 kept it
- PC-10: break the summary by omitting ` [polls=… composer=…]` from `Result`; expect E.`Settle_failure_detail_summarises_the_polls` red and A.`An_effort_dialog_that_never_clears_fails_within_its_budget` red on `polls=` (build `Antiphon.Tests` to `bin-c449b/` once for this control)
- PC-11: break D-2 by re-requiring `ClaudeScreen.ComposerIsLive(screen)` in the qualifying test; expect E.`Clearance_does_not_require_composer_chrome` red (`Cleared` false, settle deadline)
- PC-12a: break the wiring by passing `null` instead of `log` as `trace` in `ClaudeStartupReadiness.RunAsync`; expect SR.`Settle_trace_fires_on_gate_changes_not_per_poll` `phased` row red (zero resolver lines)
- PC-12b: break the cadence by invoking `trace` on every poll in `ResolveAsync`; expect the `static-hold` row red (more than 6 lines)
- PC-13: break the fixture by changing the first `38;2;136;136;136m` in `effort-dialog-787bfee2.ansi` to `38;2;136;136;137m`; expect RP.`Fixture_hashes_are_pinned` red for that row only while RP.`Real_dialog_paints_directly_under_its_rule` stays green for that fixture (rows unchanged — the hash guards bytes, not the render); restore with `git checkout --`

Batches (different files, no masking): B1 = PC-1 + PC-10 + PC-12a; B2 = PC-2 + PC-6; B3 = PC-3 + PC-7;
B4 = PC-4 + PC-8; B5 = PC-5 + PC-9; B6 = PC-11 + PC-13; B7 = PC-12b alone. Controls in the same file or method
(`TerminalScreen.cs` among PC-1..PC-5; `ResolveAsync` among PC-6, PC-7, PC-8, PC-10, PC-11, PC-12b) never share a
cycle. Seven cycles.

### Out of scope

- DECAWM off (`\e[?7l`) stays unmodelled (D-1); no test.
- Escape sequences and multibyte characters split across `Feed` calls. `TerminalScreen.Feed` keeps no parser
  state between calls and `PtyAgentRunner.ReadLoopAsync` decodes each ≤4,096-byte read with a stateless
  `Encoding.UTF8.GetString`, so a read boundary inside a CSI or a multibyte character corrupts the grid today;
  the fixtures show such boundaries at every 256-byte cut. Not this card (D-6); it deserves its own card. The
  `pieces` chunking is escape-safe precisely so this hazard cannot masquerade as a deferred-wrap failure.
- DECSC/DECRC (`\e7`/`\e8`) stay ignored; the fixtures use them only around the initial clear (plan Out of scope).
- The real banner-absent dismissal capture (D-5): an acceptance item, never fabricated from rendered screens.
- The headed real-CLI lanes and the canary: acceptance when eligible; they spawn the real CLI and are not gates.
- Poll interval, `HighlightSettle`, the three-Enter cap and `ClaudeEffortPromptSettleMs`: unchanged and already
  pinned by `Retries_respect_settle_and_attempt_limits` and `An_effort_dialog_that_never_clears_fails_within_its_budget`.
- The Herdr pane renderer, xterm.js in the UI, and the Codex/Grok adapters' own detectors: they consume
  `TerminalScreen` only indirectly; the whole-class Pty lane is their guard.
- `Parse`, intent, selection, `ClaudeBlockingPromptDetector`, `ClaudeScreen.Stable` (D-6): unchanged; their
  existing tests re-run in V-20.

### Cost

- suites forced: `Antiphon.Agents.Pty.Tests` classes `TerminalScreenTests`, `ClaudeEffortDismissalReplayTests`,
  `ClaudeEffortPromptTests`, `ClaudeStartupReadinessTests`, `ClaudeScreenTests`, `ComposerInputProbeTests`,
  `ClaudeStartupTrustPromptTests`, `ClaudeGoldenReplayTests` (one class per invocation, `--report-trx`);
  `Antiphon.Tests` classes `RunnerClaudeAdapterEffortPromptTests`, `RunnerClaudeAdapterTrustPromptTests`,
  `AgentSessionLaunchFailureTests` (Integration; needs the dev Postgres), then `"/*/*/*/*[Category=Unit]"`
  (~65 s, 778 tests per CARD-0110). Output path `bin-c449b/` with a forward slash; never the two assemblies
  concurrently; delete every `bin-c449b` directory at the end.
- verification floor ~ 35 min: ~3 min builds, ~5 min Pty lane, ~5 min Tests lanes (adapter ~1.5, trust ~0.5,
  launch-failure ~1, Unit ~1.1), seven PC cycles at ~3 min. About 45 min if the headed acceptance lane runs.

```powershell
$out = 'bin-c449b/'
foreach ($c in 'TerminalScreenTests','ClaudeEffortDismissalReplayTests','ClaudeEffortPromptTests','ClaudeStartupReadinessTests','ClaudeScreenTests','ComposerInputProbeTests','ClaudeStartupTrustPromptTests','ClaudeGoldenReplayTests') {
    dotnet run --project tests/Antiphon.Agents.Pty.Tests --property:OutputPath=$out -- --treenode-filter "/*/*/$c/*" --report-trx --report-trx-filename "c449b-$c.trx"
    if ($LASTEXITCODE -ne 0) { throw "Failed: $c (exit $LASTEXITCODE)" }
}
foreach ($c in 'RunnerClaudeAdapterEffortPromptTests','RunnerClaudeAdapterTrustPromptTests','AgentSessionLaunchFailureTests') {
    dotnet run --project tests/Antiphon.Tests --property:OutputPath=$out -- --treenode-filter "/*/*/$c/*" --report-trx --report-trx-filename "c449b-$c.trx"
    if ($LASTEXITCODE -ne 0) { throw "Failed: $c (exit $LASTEXITCODE)" }
}
dotnet run --project tests/Antiphon.Tests --property:OutputPath=$out -- --treenode-filter "/*/*/*/*[Category=Unit]" --report-trx --report-trx-filename "c449b-unit.trx"
# PC cycle shape (method-scoped; one example)
dotnet run --project tests/Antiphon.Agents.Pty.Tests --property:OutputPath=$out -- --treenode-filter "/*/*/ClaudeEffortDismissalReplayTests/Resolver_clears_a_replayed_dismissal"
```

## Verification record

Appended by Code task `567cc113` (High, shared checkout) against base `ff72feae`. Slices landed
S1 -> S3 -> S2 -> S4 as `33fe840f`, `d7f98404` (fixtures), `e38b62f7` (replay tests), `4f311c02`,
`95845d34`. Built to `bin-c449b/` throughout; all `bin-c449b` directories deleted afterwards.

### The incident, reproduced offline

With PC-1's mutation applied (immediate wrap restored) the committed replay of the real failing
stream produces the production failure verbatim, and the new D-4 summary names its cause:

```text
requested=xhigh; current=xhigh; suggested=high; selected=Keep; Enter=1; settle deadline exhausted
[polls=106 clear=0 last=remnant:"Use Fable 5.1 at high effort by default?" composer=live].
Inspect the effort picker and relaunch with a supported explicit effort.
```

Restoring the deferred wrap turns the same replay green with `Enter=1`, one `\r` written, and
`two settled clear observations`. That is the whole card in two runs.

### V rows

| Rows | Result |
|---|---|
| V-1..V-10 | `TerminalScreenTests` **56/56** — includes 11 `Cursor_moves_clear_a_pending_wrap` rows, 10 `Non_moving_sequences_keep_a_pending_wrap` rows, 3 bounded-tab rows under `[Timeout(5000)]`. No pre-existing assertion pinned the immediate wrap (V-10: nothing to correct). |
| V-11..V-13, V-21 | `ClaudeEffortDismissalReplayTests` **30/30** — 12 dialog rows, 6 dismissal rows, 6 resolver rows, 5 hash rows, 1 derivation. Every pinned row index, the pinned cursor `(6,2)`, the pinned intermediate banner row at column 52 and `IsSettled(intermediate, final) == false` all reproduced exactly as TestDesign predicted from pyte. Chunk invariance holds for all five fixtures in both non-`whole` chunkings. |
| V-14..V-17 | `ClaudeEffortPromptTests` **64/64**. |
| V-19, V-20 | `ClaudeStartupReadinessTests` **17/17**; `ClaudeScreenTests` 9/9, `ComposerInputProbeTests` 15/15, `ClaudeStartupTrustPromptTests` 16/16. |
| V-18, V-23 | `RunnerClaudeAdapterEffortPromptTests` **10/10**, `RunnerClaudeAdapterTrustPromptTests` **8/8**, `AgentSessionLaunchFailureTests` **48/48**, `ClaudeGoldenReplayTests` **4/4**. `Antiphon.Tests` `[Category=Unit]` lane **1949/1953, 1 skipped, 3 failed**. |
| V-22 | `Gotcha #90` 1 hit; `deferred` 2 hits in the invariants doc and 4 in `TerminalScreen.cs`; `DECAWM` 1 hit; `polls=` 1 hit; `two positive clear frames` **0**; `two positive dismissal frames` **0**. |

The three `[Category=Unit]` failures are **pre-existing at `ff72feae`** — each re-run
method-scoped in a detached worktree at the base commit and red there too:
`InstructionBundleTests.delegate_basics_carries_the_standing_rules_and_none_of_the_days_state`,
`UnmarkedWaitingContractTests.unmarked_waiting_attention_kind_is_appended_after_report_unsettled`,
`DelegationHarnessCensusTests.RuleB_dispatcher_harnesses_call_AddDelegationWorktreeGraph`. None
touches this card's files.

### Positive controls

Seven batches as designed, plus one extra PC-1-only cycle (`X1`) because PC-10 shares B1 and strips
the very summary PC-1's evidence is read from. Every control: mutate the committed source, build,
run the named methods, confirm the **named** assertion red, `git checkout --`, rebuild, run green.

| PC | Red at | Result |
|---|---|---|
| PC-1 | `s.CursorRow`; `rows[5]` (all 12 dialog rows); `rows[6]`/`HasRemnant` (all 6 dismissal rows); `result.Cleared` (all 6 resolver rows) | red / restored green |
| PC-2 | `s.GetRow(0)` = `ABCDEFGHIJ`, wanted `XBCDEFGHIJ` | red / green |
| PC-3 | `s.GetRow(landing)` on the **`G` row only**; the other 10 rows stayed green | red / green |
| PC-4 | `pending` and `landing` tripped `[Timeout(5000)]` at 5.0 s; `middle` stayed green; the run finished and moved on — never a hung run | red / green |
| PC-5 | `s.GetRow(2)` — the region did not scroll | red / green |
| PC-6 | both rows: `fake.Snapshots - atEnter` (exhausts) and `result.Cleared` (outlasts-budget) | red / green |
| PC-7 | `result.Cleared` and existing `Malformed_effort_remnants_do_not_count_as_clearance` | red / green |
| PC-8 | `result.Detail` on the `last=remnant:"Use ` field while `Cleared` was still false | red / green |
| PC-9 | `(await fake.ReadyAsync()).Outcome` not `EffortFailed` | red / green |
| PC-10 | `summary.Success`; adapter `LaunchBlock.Reason` missing `polls=` (reason read `...; Enter=3; settle deadline exhausted. I...`) | red / green |
| PC-11 | `result.Cleared` — a hint-less composer never clears | red / green |
| PC-12a | `phased` row: `resolver.Length` (zero resolver lines) | red / green |
| PC-12b | `static-hold` row: `resolver.Length` above 6 (one line per poll) | red / green |
| PC-13 | the `effort-dialog-787bfee2.ansi` hash row **only** (1 of 5) while `Real_dialog_paints_directly_under_its_rule` stayed 12/12 — the hash guards bytes, not the render | red / green |

Final combined regression on the restored tree: 211 tests across the eight `Antiphon.Agents.Pty.Tests`
classes, 0 failed.

### Deviations from the design, and what is still open

- **D-1's tab rule was implemented as the decision text says** (clears the flag, does not move) with
  the loop bounded at `Cols - 1`. Without that bound the loop never terminates for a tab in the last
  column, because `WriteChar` no longer advances there; V-7 pins it and PC-4 proves the bound.
- **`ESC M` (RI) does not clear the pending wrap.** D-1 enumerates only the CSI moves and
  `CR/LF/BS/TAB`, so it was left alone; none of the five fixtures contains `ESC M`. A real terminal
  would clear it on any cursor movement — worth a line on a future card, not a change made unpinned.
- **`docs/agent-kinds.md` was edited too**, which S4's file list does not mention: V-22 requires its
  `two positive dismissal frames` count to reach 0.
- **Acceptance still open** (not gates): the headed real-CLI lanes `ClaudeInteractionTests` /
  `ClaudeTuiModeTests` (these are `[Category("Headed")]` real-CLI tests, not FakeClaude as the S1
  exit-evidence note says) and `ClaudeEffortPromptCanaryTests`; the real banner-absent dismissal
  capture; and the live proof. **The live dismissal path's record is still 0 successes / 1 failure.**
  The next Frontier dispatch whose `Claude startup:` line reads `two settled clear observations`
  with `clear=2`, and whose launch block is absent, is its first success.
