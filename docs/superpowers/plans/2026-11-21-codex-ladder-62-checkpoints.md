# Codex 6.2 ladder checkpoints

Code task `ae8b6243`. Task base `2afaf4bebfb763c12ace57849ed7dd17e5bfe0ca`.

- V-1. Codex Frontier resolves to GPT-6.2 Astra. High and Medium resolve to GPT-6.2 Sol. Low resolves to GPT-5.6 Luna. CP-1 runs first against the base implementation with the changed assertion and then against the fix.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---|---|
| CP-1 | all | `tests/Antiphon.Tests -> bin-codex62-ladder/` | ladder-witness | `/*/*/CodexGpt62LadderTests/Codex_tiers_resolve_to_the_pinned_model_slugs` | V-1 | 1 passed | 1 | 3 | true | `ANTIPHON_LINUX_COMPILER_POLICY=0` |
| CP-14 | all | CP-1 | frontier-pool-witness | `/*/*/AgentTaskPoolTests/Frontier_Codex_reuse_requires_the_current_launch_model` | V-3 | 7 passed | 7 | 3 | true | `ANTIPHON_LINUX_COMPILER_POLICY=0` |
