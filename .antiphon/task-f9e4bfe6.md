# CARD-0167 S5 client provider endpoint UI - task f9e4bfe6 (Code, Final)

Branch `feat/card-task-f9e4bfe6`, runner mirror `/work/worktrees/task-f9e4bfe6`, base `445df8a805dac8525203081c03f6e58fa3f9ff3d`.
Plan: `docs/superpowers/plans/2026-10-22-card-0167-provider-endpoint-plan.md` (S5 row, CP-6). S4 (descriptor) is on origin/master (`bf67b09d3`); this branch carries only the client, which compiles and tests against mocked routes, so S4 was not merged in (git-common: never merge the target).

## Commits
- `2615aa170b1cc4bc56034161e587fe5abc0c08be` test: `client/src/features/settings/ProviderEndpointFields.test.tsx`, 5 `it` cases, red against the unmodified modals.
- `c69ae4ac204e62f956572a3eaf844b665777fefc` feat: types (`projects.ts` ProviderEndpointDto/map + warnings, `agents.ts` ProviderEndpointMode + detail/create/update fields, `agentTui.ts` AgentTuiProviderEndpointDescriptorDto, `apiKeys.ts` useAllApiKeys over GET /api/api-keys), `ProviderEndpointFields.tsx` (shared group, project variant, agent variant with mode radio), `providerEndpointModel.ts` (draft/map helpers; split out of the .tsx because react-refresh/only-export-components refuses non-component exports there - a file the plan did not name), `ProjectConfig.tsx`, `AgentSettingsModal.tsx`.

## Behaviour
- Project modal: "LLM endpoint" group per kind whose runner-types descriptor says `supported`; base URL, key-name Select (global + this project's key NAMES from GET /api/api-keys), set/not-set status text, projected env names and argv template read-only from the descriptor, per-kind Clear. `providerEndpoints` is sent only when the group was edited; blank kinds are dropped, so clearing everything sends `{}`. Codex (descriptor keyRequired) with a URL and no key name blocks save with an inline error; a key name without a URL also blocks.
- Agent modal: mode radio Inherit / Own endpoint / Direct, seeded once from the detail DTO; fields only in Endpoint mode; sent only when edited; non-Endpoint modes send `providerEndpoint: null`.
- Server `warnings` from either save are shown as a yellow notification.
- No key value is read or rendered: the witnesses' key rows carry a sentinel `value` and both modals assert it never reaches the document.

## Runs (all through scripts/build-slot.ps1 -> scripts/test-client.ps1; typecheck and lint gate each run)
- Red, at 2615aa170 (unlisted by plan D-14): `ProviderEndpointFields.test` - Tests 5 failed (5), `CLIENT TESTS EXIT CODE: 1`; every case fails on a missing-element assertion (`Unable to find role="textbox" and name "Claude Code base URL"` / `"Codex base URL"` / `role="group" and name "Grok endpoint"`), typecheck and lint passed. Slot waited=226s.
- CP-6 at c69ae4ac2: `Test Files 1 passed (1)`, `Tests 5 passed (5)`, `CLIENT TESTS EXIT CODE: 0 (PASS)`, typecheck and lint 0 errors. Slot waited=0s.
- Unlisted, reason: the two edited components' neighbour test files (every client test that renders AgentSettingsModal or ProjectConfig): AgentPreamblePreset, AgentBundleAttachments, AgentSessionBackend, AgentHerdrPlacement, AgentRemoteControl, AgentReplyStyle, AgentAutoCompact, settingsPendingReads, AgentLaunchEnv, ProjectConfig - `Test Files 10 passed (10)`, `Tests 122 passed (122)`, `CLIENT TESTS EXIT CODE: 0 (PASS)` at c69ae4ac2.
- The final CP-6 run is on the tip that commits this report; its lines are in the caller report.
- S5 has no server (TUnit) checkpoint row, so the checkpoint tool was not run; no client build, no E2E.

## Other gates
- `scripts/check-evidence-diff.ps1 -BaseRef 445df8a80 -HeadRef c69ae4ac2`: commits=2 violations=0.
- `git merge-tree --write-tree origin/master HEAD` (origin/master 713801a8f): clean, exit 0.
- V-n/R-n: the plan defines none. PCs: none defined; every mutation of the S5 witnesses stays pending for SourceLanding Mutation.
