# CARD-0167 S5 Final Review - task a4cdbeab (subject f9e4bfe6, reviewed 68444cb48)

Verdict: found. The production behaviour is correct (contract, send-only-when-edited, no key value), but the S5 witnesses cannot fail for three principal behaviours, including the project-modal secret check. Fix in Code (test-only).

## Runs (all at 68444cb48f6a518b3e8d3999d249a40a4cf5c5f3, through scripts/build-slot.ps1)
- CP-6 `scripts/test-client.ps1 ProviderEndpointFields.test`: typecheck 0, lint 0, Test Files 1 passed (1), Tests 5 passed (5), `CLIENT TESTS EXIT CODE: 0 (PASS)`; slot lease 310502d1 held 124s (included the one-time npm ci).
- Neighbours (the 10 files rendering AgentSettingsModal/ProjectConfig): typecheck 0, lint 0, Test Files 10 passed (10), Tests 122 passed (122), EXIT 0; lease 51a7cdea.
- The plan has no checkpoint-tool row for S5 (CP-6 is `n/a` build), so validate-checkpoint-receipt does not apply. No V-n/R-n defined. PCs stay pending.
- Scratch behaviour tests (detached worktree at 68444cb48, deleted after): R1 unrelated-field project save omits `providerEndpoints`; R2 a 422 `...userinfo` error surfaces as the form error and the typed password is not on screen; R3 Own endpoint sends `{providerEndpointMode:'Endpoint', providerEndpoint:{baseUrl, apiKeyName}}`; R4 untouched agent save sends neither endpoint key; R5 Inherit sends `Inherit` + `null`. 5/5 pass.
- merge-tree vs origin/master 3f54023d6: clean (exit 0). check-evidence-diff 445df8a80..68444cb48: commits=3 violations=0.

## Mutations (scratch, each restored; named file set: ProviderEndpointFields.test + ProjectConfig.test + settingsPendingReads.test + my scratch)
| Mutation | Named S5 case red? |
|---|---|
| M1 render `JSON.stringify(keys.data)` (rows carry the sentinel `value`) in both variants | only `Direct mode hides the fields...` (agent). The project half survives: a scratch probe proved the sentinel IS on screen while the project modal is open, and gone after save. |
| M2 always send `providerEndpoints` (untouched save) | none (ProjectConfig.test checks only `defaultLaunchEnv`); only scratch R1 red |
| M3 Own endpoint sends `providerEndpoint: null` | none; only scratch R3 red |

## Defects
D-1 (owner f9e4bfe6) Where: `client/src/features/settings/ProviderEndpointFields.test.tsx`, case `renders stored endpoints per kind and submits the parsed map`, final `expect(document.body.textContent).not.toContain(SENTINEL)`. Failure: the assertion runs after Save, when the modal has closed, so a key value rendered in the project modal passes (M1 project half survived). Why: secret custody is the slice's point; the project modal's only no-value witness cannot go red. Fix: assert the sentinel is absent while the modal is open (after the key options are listed and the dropdown is open), before Save.

D-2 (owner f9e4bfe6) Where: same test file; no case. Failure: making ProjectConfig send `providerEndpoints` on every save (M2) keeps every named test green, so a regression that clears or rewrites a project's endpoints on an unrelated save is unwitnessed. Why: the brief names this as the main regression risk for an existing settings flow. Fix: add a case that opens the project editor, changes an unrelated field (e.g. Name), saves, and asserts the PUT body has no `providerEndpoints` key (scratch R1 is a working shape).

D-3 (owner f9e4bfe6) Where: same test file; no case. Failure: sending `providerEndpoint: null` in Own endpoint mode (M3) keeps every named test green; the server would then 422 `provider_endpoint_required` or, with a stored endpoint, keep a stale one while the operator's edit is dropped. Why: the agent Endpoint save is the agent variant's main behaviour. Fix: add a case that edits the Own endpoint URL, saves, and asserts `providerEndpointMode: 'Endpoint'` and the full `providerEndpoint` object (scratch R3); optionally an untouched agent save that sends neither key (R4).

## Checked clean (disclosure)
- Secrets: `ApiKeyDto` on master (`server/Application/Dtos/ApiKeyDtos.cs`) is Id/Name/ProjectId/ProjectName/CreatedAt/UpdatedAt, no value or masked tail; GET /api/api-keys returns it. The picker maps rows to `name` only; the form state holds URL + key name; the payload carries a name; query key `['apiKeys',...,'all']` has no value or URL; no console logging; no storage.
- URL userinfo: the client does not refuse it (looser than the server). Server `ProviderEndpointSettings.ValidateBaseUrl` and `ProviderEndpoint.ValidateBaseUrl` refuse it with 422 and never quote the URL; the project modal shows the `errors` text and the agent modal uses `getApiErrorMessage` (scratch R2). The operator-typed userinfo does travel once in the PUT/PATCH body before the refusal; a client-side refusal would be stricter and is a fair Backlog item.
- Contract vs master: PUT `/api/projects/{id}` `providerEndpoints` (null = unchanged, `{}` = clear) and response `providerEndpoints` + `warnings`; PATCH `/api/agents/{id}` `providerEndpointMode`/`providerEndpoint` (both omitted = unchanged; non-Endpoint mode requires null endpoint, which drops it); enum strings via `JsonStringEnumConverter(namingPolicy: null)` = `Inherit|Endpoint|Direct`; map keys are canonical AgentKind names (no DictionaryKeyPolicy); descriptor `supported/keyRequired/environmentNames/argumentTemplate`. All match.
- a11y: TextInput/Select labels, Fieldset legend as the group name, Radio.Group label, Clear button aria-label.
- `providerEndpointModel.ts`: helpers are pure; it also holds `useProviderEndpointKinds` (a read-only query hook), so "pure" is slightly overstated. The split is justified: helpers are imported by ProjectConfig/AgentSettingsModal and `eslint-plugin-react-refresh` is configured; inlining was not tried (budget).
- Agent Codex key requirement is `agent.kind === 'Codex'`, consistent with server `RequiresKeyName`.
