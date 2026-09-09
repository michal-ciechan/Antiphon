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
