# Instruction bundles

Every `*.md` file beside this one is a named, versioned block of standing instructions that
`InstructionBundleComposer` renders into an agent's `--append-system-prompt` at launch (CARD-0058,
plan `docs/superpowers/specs/2026-08-16-card-0058-0059-0060-instruction-bundles.md`).

**This file is the only one here that is not a bundle** — it is excluded from the embedded-resource
glob in `Antiphon.Server.csproj`, and `InstructionBundleTests` pins the exact key set so an
accidentally-embedded file fails a test instead of reaching an agent.

## Editing one

- The file's **entire content is prompt text**. There is no frontmatter and no title heading; a
  comment you add for a human reader is text an agent pays for on every launch, so put that here
  instead.
- The **key** is the filename without `.md`; the **version** is the first 8 hex digits of the
  SHA-256 of the LF-normalised text, rendered as `[bundle:<key> v<hash8>]` above the body. Editing a
  bundle changes its version automatically — there is nothing to bump by hand.
- A change reaches an agent at its **next launch**. AlwaysOn agents are guaranteed one by
  supervision; a fresh delegate gets it on dispatch; a warm pool delegate keeps what it launched with
  until it retires (`PoolIdleRetireMinutes`, 60 min idle). Nothing types bundles into a live session;
  instead a standing agent that is idle is relaunched with `--resume` and the new composition
  (CARD-0334), keeping its conversation. `Agent.PolicyRefreshMode` picks the lane: `Auto` relaunches
  when idle and past cooldown, `Relaunch` is the same but never falls back to Notify, `Notify` posts
  a queued message describing the drift instead of killing the session, and `Off` does neither.
- Never write `{agentName}` or `{channels}` into a bundle: `ChannelPreamble.Render` substitutes those
  over the whole composed append, so they would be replaced inside the bundle text too. A test pins
  this.
- Keep it **standing rules only**. Anything that will be wrong tomorrow — today's known-red tests, a
  warning count, "slices 1 and 5 already landed" — belongs in the brief for that one dispatch, not
  here. That split is the reason this directory exists.

## Which agent carries which bundle

Delegates get theirs from the role map in `InstructionBundles.ForDelegate` (Orchestrator tasks:
`orchestrator` + `delegate-basics`; a Worker whose role is a pipeline stage — Investigate, Plan,
TestDesign, Code, Review — gets that stage's `stage-*` bundle then `delegate-basics`; other worker
roles: `delegate-basics`; specialist tasks: none, the specialist's contract is its own — and an
attachment does not reopen that, because the carve-out is about what the specialist can obey).
Stage bundles never name a kind: routing stays on pins, chains and RolePolicy.

Anything else is an **attachment**: an `AgentBundleAttachment` row naming this agent and this key,
edited in the agent settings modal. That is how `board-api` reaches an agent — it is in the catalog
but on no role, because widening the role map would hand it to every delegate of that role. Role
defaults come first in the composition, attachments after, and a bundle reachable both ways is
composed once.

Attachments are the only bundle state in the database. The key is a plain string with no foreign
key, so a bundle file **renamed or deleted** in a later PR leaves rows naming nothing: those are
dropped from the composition with a warning rather than failing the launch, and stop appearing in
the agent's "carries bundles" list. If you rename a bundle, detach the old key.

### The drift badge

`AgentSession.ComposedBundleStamp` records the stamp line a launch composed (`board-api v1a2b3c4d`
— stamps, never text). The agent DTOs expose `BundlesOutOfDate` when that no longer matches a
composition recomputed now, and the UI shows a quiet badge. It is informational: the agent picks the
new instructions up at its next launch and nothing forces one. Editing a bundle here will therefore
badge every agent carrying it until each next launches — that is the mechanism working, not a fault.

A rule that earns standing status gets PR'd into a file here. Recorded anywhere else — a findings
doc, a skill, one orchestrator's habit — it reaches nobody.

### The output-distiller bundle (CARD-0330)

`output-distiller.md` is the standing contract of the `antiphon-output-distiller` seat. Edits
arrive as Review cards from the weekly prompt-review loop
(`docs/orchestration-loop.md` §10), never as a live write to `SystemPromptAppend` or to the
running session. A review may change any sentence except the INVARIANTS block (pinned by
`InstructionBundleTests`); the file must stay at most 3 000 characters and open with
`You are the Antiphon OUTPUT DISTILLER (contract v`. The gates in `OutputDistillationGate` are
code, not this file — the loop must not change them. After a merge, stop and start the seat so
the new version composes; rollback is `git revert` plus the same restart.

## Reply styles

The `style-*` files (`style-normal`, `style-terse`, `style-caveman`, `style-brief`,
`style-explanatory`) are chosen through `Agent.ReplyStyle`, never attached — `bundleKeys` refuses
them with 422 because two voices at once has nothing to dedup against. `Normal` composes to
nothing (the file still ships so every enum value has a block). The style block sits after
attachments and before the agent's own `SystemPromptAppend`, which keeps the last word.
