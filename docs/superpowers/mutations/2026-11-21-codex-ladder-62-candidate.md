# Codex 6.2 ladder mutation candidate

Original Code and landing owner `ae8b6243-c56b-4cea-bb8a-383059354890`.
Source task branch `feat/card-task-ae8b6243`.
Ordinary verification uses the [closed checkpoint plan](../plans/2026-11-21-codex-ladder-62-checkpoints.md).

The candidate is unbound. SourceLanding Mutation owns binding, each deliberate edit,
assertion red, restoration, and green on the same method filter.

| Control | Variant | Exact test filter | Required red |
|---|---|---|---|
| PC-1 | Restore `ModelAlias.Gpt6Astra` in the Frontier ladder entry in `ModelLevelAliases.cs`. | `/*/*/CodexGpt62LadderTests/Codex_tiers_resolve_to_the_pinned_model_slugs` | Frontier gives `gpt-6-astra` where the test requires `gpt-6.2-astra`. |
| PC-2 | Disable the current launch slug requirement for Codex Frontier warm reuse in `AgentTaskDispatcher.cs` by setting `expectedWarmModel` to null. | `/*/*/AgentTaskPoolTests/Frontier_Codex_reuse_requires_the_current_launch_model` | Legacy and unstamped sessions return `Reused` where the test requires `SpawnFresh`. The observed session must be the seeded pool process. |

Both controls are pending for the caller-commissioned SourceLanding Mutation task.
