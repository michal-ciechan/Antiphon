# Antiphon – To Do

## Caching
- [ ] Switch GitHub repo cache (`GitHubRepoCache`) from hand-rolled in-memory to **FusionCache** (`ZiggyCreatures.FusionCache`)
  - Handles stampede protection, background refresh, and optional L2 (Redis) out of the box
  - Replace `SemaphoreSlim` + manual TTL check with `cache.GetOrSetAsync(key, factory, options)`
  - Can keep `GitHubRepoCacheWarmupService` or lean on FusionCache's eager refresh

## Feature 001 — Kanban + Agent PTY Orchestration (open questions)
See `docs/features/001-kanban-agent-orchestration/05-epics.md` for full plan.

- [ ] Decide: `Card` wraps existing `Workflow` entity, or sibling concept? (plan.md risk #5)
- [ ] Decide: shared user creds vs per-agent secret store for `~/.claude`, `~/.codex`, env vars (plan.md risk #3)
- [ ] Decide: hook script trust model — full trust, or restricted via JobObject + path allowlist (plan.md risk #9)
- [ ] Decide: vs-pty.net Windows fallback — if winpty/conhost unavailable, fall back to `Process` + redirect (no PTY) or fail hard? (E01)
- [ ] Run `/pts-vibe-init` for feature 001 to formalise requirements (`01-requirements.md`) and design (`03-design.md`); migrate inline §0 reqs out of `05-epics.md`
