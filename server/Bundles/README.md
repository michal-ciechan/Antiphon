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
  until it retires (`PoolIdleRetireMinutes`, 60 min idle). Nothing types bundles into a live session.
- Never write `{agentName}` or `{channels}` into a bundle: `ChannelPreamble.Render` substitutes those
  over the whole composed append, so they would be replaced inside the bundle text too. A test pins
  this.
- Keep it **standing rules only**. Anything that will be wrong tomorrow — today's known-red tests, a
  warning count, "slices 1 and 5 already landed" — belongs in the brief for that one dispatch, not
  here. That split is the reason this directory exists.

## Which agent carries which bundle

Delegates get theirs from the role map in `InstructionBundles.ForDelegate` (Orchestrator tasks:
`orchestrator` + `delegate-basics`; other work: `delegate-basics`; `Check` tasks: none, the
specialist's contract is its own). `board-api` is in the catalog but on no role by default — it goes
to standing agents that touch the board, via per-agent attachments (plan slice 6).

A rule that earns standing status gets PR'd into a file here. Recorded anywhere else — a findings
doc, a skill, one orchestrator's habit — it reaches nobody.
