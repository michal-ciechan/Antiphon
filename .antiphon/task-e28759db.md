# CARD-0167 S5 Final Review — task e28759db

Verdict: clean. The three repaired witnesses fail when the named product behaviour is broken and pass again when that change is restored. The repair diff is test-only. Ordinary receipt at `19cd7885f4f2d84b0e65fc092acd9e5d9b6df879` is CP-6 7 passed and the ten neighbour files 122 passed. Source was clean and the command-row receipt validated. Merge-tree against current `origin/master` is clean.

Subject (land owner): Code task `6c1c3a92-8b8e-451b-946c-3b4d28d882d0`, branch `feat/card-task-6c1c3a92`. This review did not commit to that branch. Review branch `feat/card-task-e28759db` was the same commit until this report.

## Receipt

CP-6 checkpoint run `20261022-201523-4fe2` (tool `dotnet exec` of `tools/Antiphon.Checkpoints/bin-c0167review/Antiphon.Checkpoints.dll`, bootstrap through `scripts/build-slot.ps1 -Label c0167-review-boot`, lease `3f709486-5c85-431e-8d45-901a8965a682`, 0 errors). The run itself was not wrapped in a second slot. `--rows CP-6 --expected-source-sha 19cd7885f4f2d84b0e65fc092acd9e5d9b6df879 --row-timeout 9m --total-timeout 9m`. Shell `timeout 590`.

```text
CHECKPOINT CP-6 commit=19cd7885f4f2d84b0e65fc092acd9e5d9b6df879 build=n/a filter=pwsh -File scripts/test-client.ps1 ProviderEndpointFields.test executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a exit=0 slot=granted waited=120s dirty=0 source=19cd7885f4f2d84b0e65fc092acd9e5d9b6df879 sourceState=clean buildSource=notApplicable
```

Vitest console (`.antiphon/checkpoints/20261022-201523-4fe2/rows/CP-6/console.log`): typecheck exit 0, lint exit 0, Test Files 1 passed (1), Tests 7 passed (7), `CLIENT TESTS EXIT CODE: 0 (PASS)`. Wall 5m16s, tool verdict GREEN exit 0. `scripts/validate-checkpoint-receipt.ps1 -Evidence .antiphon/checkpoints/20261022-201523-4fe2/report.json -ExpectedSourceSha 19cd7885f4f2d84b0e65fc092acd9e5d9b6df879 -Rows CP-6` printed `CHECKPOINT SOURCE VALID source=19cd7885f4f2d84b0e65fc092acd9e5d9b6df879 rows=1` exit 0. `buildSource=notApplicable` is the command-row receipt (CP-6 build cell is `n/a`); the validator allows that. There is no compiled test build to bind. Counts come from the Vitest log, not the `n/a` CHECKPOINT counters.

Neighbours, one build-slot command, lease `5550162e-f99a-4731-bd94-959e9f602fb4` held 75s, waited 0s: the ten files that render `ProjectConfig` or `AgentSettingsModal` other than `ProviderEndpointFields.test.tsx`. Typecheck 0, lint 0, Test Files 10 passed (10), Tests 122 passed (122), `CLIENT TESTS EXIT CODE: 0`. The ten names match every such importer in `client/src`.

Plan `### Checkpoints` CP-6 still says 5 tests. The repair added two cases; the commissioned count is 7; the run produced 7. The command row does not parse the Expect cell. Advisory only.

No V-n or R-n IDs exist for S5. Operator real-CLI acceptance in the plan stays outside this repair and pending. No PC-n IDs exist. Throwaway mutations below do not discharge SourceLanding Mutation.

## Mutations (throwaway, each restored)

Each red and green went through `scripts/build-slot.ps1` and `scripts/test-client.ps1` with `-t` on the named case. Product files were restored with `git checkout --` of the three paths. Post-run SHA-256 of those files matched the pre-run hashes, and `git diff` on them was empty.

| Case | Mutation | Red | Restored green |
|---|---|---|---|
| M1-project | `JSON.stringify(keys.data)` inside `ProjectProviderEndpoints` | exit 1. `renders stored endpoints per kind and submits the parsed map` failed at line 147 `not.toContain(SENTINEL)` after the option lookup (lines 145–146) and before Save (line 148). Lease `387859dd`. | exit 0, 1 passed. Lease `059bb796`. |
| M1-agent | same stringify inside `AgentProviderEndpoint`, outside the Endpoint-only fields | exit 1. `Direct mode hides the fields and submits providerEndpoint null` failed at line 317 `not.toContain(SENTINEL)` after the Direct payload assertions (lines 315–316). Lease `52230218`. | exit 0, 1 passed. Lease `759f8d05`. |
| M2 | `ProjectConfig` always sends `providerEndpoints: providerEndpoints.map` | exit 1. `omits providerEndpoints when saving an unrelated project field` failed at line 178 `not.toHaveProperty('providerEndpoints')` after the renamed `name` assertion. Body contained the Claude endpoint. Lease `4ab144c4`. | exit 0, 1 passed. Lease `4569f23a`. |
| M3 | Own-endpoint branch assigns `providerEndpoint: null` | exit 1. `Own endpoint mode submits the edited URL and full endpoint` failed at line 294: mode stayed `Endpoint`, `providerEndpoint` was `null` instead of the edited URL and `claude-proxy-key`. Lease `c380ca91`. | exit 0, 1 passed. Lease `6a37eb9d`. |

Negative control (rows withheld): `data={options.slice(0, 0)}` so the select still typechecks and renders no options. Exit 1. The same project case failed at line 136, `Claude Code key name` value expected `claude-proxy-key` and received empty, before the dropdown option lookup and before the sentinel line. The case did not pass. An earlier `data={[]}` edit never reached Vitest (TS6133 unused `options`, typecheck exit 2) and is not this evidence. Restored; the following green of the same case on the restored file was exit 0 (lease `7eba00ea`, from the unused-variable cycle's restore).

## Diff

`68444cb48f6a518b3e8d3999d249a40a4cf5c5f3..19cd7885f4f2d84b0e65fc092acd9e5d9b6df879` is four commits and two files: `client/src/features/settings/ProviderEndpointFields.test.tsx` and `.antiphon/task-6c1c3a92.md`. No product source.

Hunks in the test:

- Profiles `GET /api/agent-tui/profiles` returns `[]` in `seedProject`, matching the read the modal now issues.
- D-1 sentinel `not.toContain` moves to after the open key-option lookup and before choosing a key or Save. The negative control shows a missing option list fails earlier, so that line cannot be the reason a row-less render passes.
- D-2 adds `omits providerEndpoints when saving an unrelated project field`. It edits the project name and asserts one PUT with `name` `Renamed project` and no `providerEndpoints` property.
- Agent route setup moves into `seedAgent` without dropping a handler. D-3 adds `Own endpoint mode submits the edited URL and full endpoint`: mode `Endpoint` and the full `{ baseUrl, apiKeyName }` after a URL edit.

Name selector: `8c7005d8a` used `getByRole('textbox', { name: 'Name', exact: true })`, which is not a `ByRoleOptions` field (typecheck TS2769 at `94635d3ee`). `d9d0ec449` replaced it with `name: /^Name$/`. That anchor is an exact accessible-name match and is not a weaker substring. It still matched two Name textboxes on the settings page (`ProjectConfig` label Name, placeholder `My Project`, and `ApiKeysSection` label Name). `19cd7885` uses `getByPlaceholderText('My Project')` plus `toHaveValue('Antiphon')` before the edit. The placeholder is unique to the project name field. The payload assertions are unchanged. M2 still fails on `not.toHaveProperty('providerEndpoints')`, so the landed selector does not weaken the witness.

Report hunks in `.antiphon/task-6c1c3a92.md` record the two authoring failures (TS2769, then the duplicate Name textbox) and do not change product code. Those runs are not mutation evidence.

## Secrets

Test strings beyond `sentinel-0167-never-printed` are key names (`claude-proxy-key`, `project-1-key`, `other-project-key`), descriptor environment names (`ANTHROPIC_API_KEY`, `ANTHROPIC_BASE_URL`, `OPENAI_API_KEY`, `C0167_DESCRIPTOR_ONLY_NAME`, `GROK_CLI_CHAT_PROXY_BASE_URL`), and URLs with no userinfo (`http://claude-proxy:8080`, `http://grok-proxy:9000`, `http://codex-proxy:7000`, `http://agent-proxy:8080`, `http://edited-agent-proxy:9000`, `https://example.test/repo.git`). No password, bearer, or `user:pass@host` string.

## Landing checks

`git merge-tree --write-tree origin/master HEAD` exit 0, tree `c083f557de00dccd5461079e0695f39396f7ec14`, `origin/master` `182eac62b24c1c55b76045f167edd421695275c4` (later than `921c0a09f`). Not merged.

`scripts/check-evidence-diff.ps1` exit 0 both ranges: repair `68444cb48f6a518b3e8d3999d249a40a4cf5c5f3..19cd7885` commits=4 entries=3 violations=0; whole S5 `445df8a805dac8525203081c03f6e58fa3f9ff3d..19cd7885` commits=7 entries=4 violations=0.

## Disclosure (not a defect)

- Neighbour `ProjectConfig.test.tsx` still logs an unhandled MSW `GET /api/agent-tui/profiles` and passed 15/15. The repair stubbed that read only in `ProviderEndpointFields.test.tsx`.
- Client refusal of URL userinfo is already Backlog CARD-1509 (`42857453-08aa-449d-af5b-28d73ca44823`), filed by the repair task. No second card.
- Parking disabled: a session that waits for input has no automatic release deadline (CARD-1083). This review did not change an input wait.

PCs stay pending for method-scoped SourceLanding Mutation after land. This review did not fix product or tests.
