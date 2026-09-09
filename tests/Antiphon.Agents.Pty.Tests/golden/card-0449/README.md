# CARD-0449 effort-dialog replay fixtures

Raw PTY byte streams from real `claude` startup sessions, copied byte-for-byte out of
`C:\logs\antiphon\session-runner\<sessionId>.ansi.log`. They exist because the effort-dialog
dismissal path had never been exercised against a real rendered screen: every prior round used the
scripted `EffortTestScreen` fake, whose non-dialog screen is three clean lines. The one real
dismissal that ever ran failed — `TerminalScreen` wrapped immediately at the last column where
ConPTY assumes a **deferred** wrap, so the dialog's erase-and-repaint left a ghost title on the grid
and the readiness gate blocked for its whole budget.

Replayed by `ClaudeEffortDismissalReplayTests` through `TerminalScreen(120, 30)` in three chunkings
(one write; split before each `\x1b[?25l`; escape-safe 256-char pieces), so both the "one atomic
write" and "several time-separated writes" hypotheses are covered by construction.

`*.ansi` files carry `-text` in `.gitattributes` — the repo's `* text=auto` under
`core.autocrlf=true` would otherwise rewrite their `\r\n` pairs and break the pinned hashes.

| File | Bytes | SHA-256 | Provenance |
|---|---:|---|---|
| `effort-dismissal-23b5663c.ansi` | 3,957 | `18c89534612034390d52a09b2e96093687b4be379f98a2d03684620f01ee9d4c` | real; banner-present dismissal (session `23b5663c-fbce-40ea-8ec9-613764086827`, task `f418105f`, 2026-09-09T07:46:18Z, 120×30) — the failing incident |
| `effort-dismissal-23b5663c-banner-excised.ansi` | 3,868 | `15f7381f74850dc508e26eb0b32810bf87e7bc11d95e86c0bac4e0c637f9b23b` | **synthetic**; see below |
| `effort-dialog-9184ec6d.ansi` | 2,637 | `791729350b491bb6805e72e2b79dc7e3b706fe2a37c9a72b44a07b11c72d03b2` | real; dialog paint, no dismissal |
| `effort-dialog-2968433b.ansi` | 2,631 | `cb41ba5defe7afa6089a93dff943d714c02391deb9812ccddf97fa39cbcd77e1` | real; dialog paint, no dismissal |
| `effort-dialog-787bfee2.ansi` | 2,631 | `c478644172a24472166370c4af7470ddf5442957e35583b0623ce2db98a21a30` | real; dialog paint, no dismissal |

## The synthetic file

`effort-dismissal-23b5663c-banner-excised.ansi` is the real file with one 89-byte span removed and
nothing else changed:

```text
excised == real[..3694] + real[3783..]
```

The span at byte offsets `[3694, 3783)` is exactly the operator's usage banner write, and it occurs
once in the stream (`·` is two bytes in UTF-8):

```text
\x1b[53G\x1b[38;2;255;193;7mYou've used 83% of your weekly limit · resets 12am (Europe/London)
```

The cursor moves around it are kept. `Banner_excised_fixture_is_derived_from_the_real_one` re-derives
the file from the real one and fails if either drifts.

Why it exists: the investigation's hypothesis was that the usage banner's insert/retract defeated the
settle gate. It does not — a replay with the banner excised ghosts identically, which is why the fix
must not and does not special-case it. The banner still changes what the CLI erases, so both states
are pinned.

A **real** banner-absent dismissal capture does not exist yet. It is an acceptance item: the next
time a Frontier launch shows the picker with usage below the banner threshold, save its ansi log here
and add it as a fourth `Arguments` row. Do not fabricate one from the JSON captures in
`docs/superpowers/plans/2026-09-08-card-0449-claude-effort-dialog-captures.json` — those are rendered
screens, not bytes.

## Structure markers

Present byte-exactly in every file, and used by the tests instead of hard offsets:

- Ink frames start at `\x1b[?25l`: three in the dismissal stream, two in each dialog-only stream.
- The dismissal begins at the **first** `\x1b[2K\x1b[1A` (byte 2636 of the real dismissal file);
  it is absent from the dialog-only files. Everything before it is the dialog frame.
- No `\r` from the Enter key is echoed in the stream, so no fixture editing is needed to split it.

## Contents

The streams carry the working directory (`C:\src\markdown-package`), Claude Code's version banner,
the delegate task banner (`task-f418105f`, `task-de877677`, `task-2722ce41`) and, in the
banner-present file, the operator's usage percentage and timezone. No credentials, tokens or prompt
content. If Claude reformats the dialog, re-capture — never loosen the row assertions.
