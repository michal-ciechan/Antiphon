# CARD-0167 S4 — Code task 16fc75cf report

Branch `feat/card-task-16fc75cf`, worktree `/work/worktrees/task-16fc75cf` (runner mirror, server2), base `713f5db86e8679bc5f3f65c9e4548fcaf4e8bbea`.

## What S4 did

`GET /api/agent-tui/runner-types` now carries `providerEndpoint` per runner kind:
`{ supported, keyRequired, environmentNames, argumentTemplate }`, built from
`ProviderEndpointProjection.Describe` so the catalogue and the launch path cannot name different variables.
Only the environment NAMES leave the catalogue (ordinal order); Describe's `{{key:<name>}}` values never do.
The Codex argument template carries the literal `<url>`.

| Kind | supported | keyRequired | environmentNames | argumentTemplate |
|---|---|---|---|---|
| ClaudeCode | true | false | ANTHROPIC_API_KEY, ANTHROPIC_BASE_URL | none |
| Grok | true | false | GROK_CLI_CHAT_PROXY_BASE_URL, GROK_CODE_XAI_API_KEY, GROK_XAI_API_BASE_URL | none |
| Codex | true | true | OPENAI_API_KEY | the five `-c` overrides, `base_url="<url>/v1"` |
| OpenCode, Raw | false | false | none | none |

Files: `server/Application/Dtos/AgentTuiDtos.cs` (`AgentTuiRunnerTypeDto.ProviderEndpoint`,
`AgentTuiProviderEndpointDescriptorDto`), `server/Application/Services/AgentTuiRunnerCatalog.cs`
(`ProviderEndpointDescriptor`), `tests/Antiphon.Tests/AgentTui/AgentTuiApiTests.cs`
(`C0167_runner_types_describe_provider_endpoint_projection_per_kind`). The client type is S5's.
No migration. No secret was read, printed or stored.

## Commits

- `382827029ec88a43d851a39d7ee830334c8b9e88` test first (red).
- `98c76f96b857441701e6f4ec96065038afc11739` implementation plus the S4 amendment
  `docs/superpowers/plans/2026-10-22-card-0167-provider-endpoint-plan-s4-amendment.md` (CP-5 unchanged, CP-8 adds
  the three whole classes that read the catalogue's DTOs, floor 71).
- This report commit; the receipt run starts at this tip.

## Red witness (unlisted by plan D-14)

`scripts/build-slot.ps1 -Label c0167-red-s4 -- dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-c0167red/ -- --treenode-filter "/*/*/AgentTuiApiTests/C0167_*"`
at `382827029`: total 1, failed 1, on the assertion
`runner.TryGetProperty("providerEndpoint", out var descriptor) should be True — runner type ClaudeCode has no providerEndpoint descriptor`.
Slot waited 135s.

Other unlisted runs: one build-slot `dotnet build server` compile check (`bin-c0167chk/`, 0 errors, waited 45s) before
spending a checkpoint run; one build-slot build of `tools/Antiphon.Checkpoints` (`bin-c0167tool/`) to run `import`,
`start`, `wait` and `validate`. `import --plan` of the amendment: imported 2 rows.

## Gates at 98c76f96b

- `git merge-tree --write-tree origin/master HEAD` clean (tree `7e4aaa633`).
- `scripts/check-evidence-diff.ps1` over `713f5db86..98c76f96b`: commits=2 violations=0.

## Scope against the Final profile

The brief says "minimal testing, no whole-suite runs", so the whole Unit lane was not run; it is deferred, not
passed. The S4 rows are CP-5 (plan) and CP-8 (amendment). No V-n/R-n IDs exist for S4. The plan's PC rows (none
for S4) stay with SourceLanding Mutation.

CHECKPOINT lines for CP-5 and CP-8 are in the task's final message (run started after this commit).
