# Antiphon – To Do

## Caching
- [ ] Switch GitHub repo cache (`GitHubRepoCache`) from hand-rolled in-memory to **FusionCache** (`ZiggyCreatures.FusionCache`)
  - Handles stampede protection, background refresh, and optional L2 (Redis) out of the box
  - Replace `SemaphoreSlim` + manual TTL check with `cache.GetOrSetAsync(key, factory, options)`
  - Can keep `GitHubRepoCacheWarmupService` or lean on FusionCache's eager refresh
