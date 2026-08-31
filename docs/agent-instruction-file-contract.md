# Agent instruction-file contract


<!-- CARD-0254 preserved source begins -->

## CARD-0254 preserved operational detail

This document owns the supported project instruction import mechanism and its portability contract.

## Why a pointer and not a symlink

The goal is one source of truth with no drift between `AGENTS.md` and `CLAUDE.md`. This repo gets
that with a stub plus an `@` import rather than a committed symlink, because a symlink does not
survive a normal clone here. Measured on this machine, 2026-08-24:

- **Claude Code discovers `CLAUDE.md` and `CLAUDE.local.md` only — never `AGENTS.md`.** So the real
  content cannot simply live in `AGENTS.md` with nothing at `CLAUDE.md`; a Claude Code session would
  load none of it. (Anthropic's own Codex-config importer agrees on the direction: it migrates a
  project's `AGENTS.md` *into* `CLAUDE.md`.)
- **`@` imports work, and are the supported committed "link".** A probe (`CLAUDE.md` containing only
  `@imported.md`, a codeword in `imported.md`, read back through `claude -p`) returned the codeword.
  Imports are plain text in a tracked file, so they clone and check out identically on every OS with
  no per-machine setup. Nesting is capped at 5 levels; this repo uses 1.
- **A committed symlink would not work.** `git config core.symlinks` is `false` in this clone
  (`C:/src/Antiphon/.git/config`). Under that setting git checks a tracked symlink out as an ordinary
  file whose entire content is the literal target path — a `CLAUDE.md` containing the nine bytes
  `AGENTS.md` — which Claude Code would load verbatim as its project instructions. That is silent
  total context loss, not a redirect. Git sets `core.symlinks=false` automatically at clone time on
  Windows when the account cannot create symlinks, so anyone cloning without Developer Mode (or an
  elevated shell) lands in exactly that state; forcing it true would be a per-machine, per-clone
  prerequisite that nothing in the repo can enforce.
- **A junction cannot express this at all.** NTFS junctions are directory-only, so `mklink /J` cannot
  target a single file. A hard link (`mklink /H`) is same-volume only and git does not preserve
  hard-link identity across clones — it would commit two independent files that happen to match
  today and drift apart silently, which is the exact failure this card exists to prevent.

Net effect: `CLAUDE.md` holds a pointer and no knowledge, `AGENTS.md` holds everything, and the two
cannot drift because only one of them has content.
<!-- CARD-0254 preserved source ends -->
