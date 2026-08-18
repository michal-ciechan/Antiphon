# Antiphon Ideas

Loose, unscheduled feature ideas. Not on the active board, no slice plan, no commitment attached.
Promote to a card when someone actually wants to build it.

## Grok ACP adapter (headless, for delegate sessions)

Split out of CARD-0080 (Grok Build TUI) on purpose. Grok TUI (interactive, PTY, real terminal) and a
Grok ACP adapter (headless, `grok agent stdio`, no composer) are different features aimed at different
kinds of session, not two slices of the same card.

**Why this might be worth it eventually.** CARD-0080's S1 canary measurements (2026-08-18) found
Grok's TUI composer drops every newline from typed/pasted input, with no separator inserted — lines
run together. That is a real, permanent cost of running anything through the interactive TUI. An
ACP-native path has no composer to strip anything from.

**What it would be** (originally slices S3/S4 of
`docs/superpowers/plans/2026-08-18-grok-first-class-acp.md`):

- `AcpAgentAdapter : IAgentProtocolAdapter` speaking ACP JSON-RPC over `grok agent stdio` —
  `session/new` → `session/prompt` → stream `session/update` → response `stopReason` is turn-complete.
  For delegate/worker sessions only, never operator-facing interactive ones.
- No composer means the entire delivery-verification/ceiling/clipping/DA1/quiet-period apparatus
  (CARD-0027/28/30/37/48/52/55) is structurally unnecessary on this path.
- Open design points, unresolved: restart survival (`loadSession`/`session/resume`, state lives in
  Grok's own session store, vs. a detached acp-host the way PTY sessions work today); client rendering
  for a screenless session (render the update stream as a chat/log view — there is no terminal to show).
- Longer-term direction: two adapter families — PTY for operator-facing interactive sessions, ACP for
  delegates. Grok would pilot it since it is already ACP-native; Claude/Codex explicitly NOT
  migrating — their PTY machinery is load-bearing and already paid for.

**Status:** not planned, not scheduled. Steered away from in favor of PTY-only, 2026-08-18 — revisit
only if the newline-loss cost (or another PTY limitation) becomes a real problem, or someone actually
wants a headless delegate path badly enough to build it.
