# CARD-1004 Linux startup captures

| Session prefix | Captured at (UTC) | Grok Build | Host | Terminal | Worktree |
|---|---|---|---|---|---|
| `83cf3d39` | 2026-10-02T20:17:14Z | 1.0.41 | server2-temp Linux runner container | 120x30 | `task-6a98e94f` |
| `e2264b56` | 2026-10-02T17:17:30Z | 1.0.40 | standing server2 Linux runner container | 120x30 | `task-2de224e3` |

Source: CARD-1004 and the task-1982599f dispatch brief, received 2026-10-02.
Exact capture timestamps above come from the full CARD-1004 text.
The 1.0.41 rendered frame was decoded from every ASCII `\uXXXX` escape in that
brief, including U+000A. The 1.0.40 rendered frame was reconstructed from its
explicit statement that only the worktree name differs. Both stored screens were
then checked against the full card's two verbatim frames after decoding and match
exactly. These are rendered
screens, not raw ANSI chunk replays. Both have 30 rows, maximum row length 118,
and U+276F at zero-based row 25, column 4. Stored SHA-256 values cover the UTF-8
rendered screens. No secrets are present: content consists of a worktree header,
empty composer, model/permission footer and shortcut hint.

The original Windows captures in `../card0778/startup-frames.json` are untouched.
These fixtures prove classifier behavior; real Linux first-prompt delivery still
requires the orchestrator's post-rollout unpinned server2-temp canary and matching
complete `UserPrompt` transcript evidence.
