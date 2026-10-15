# CARD-1274 unbound Mutation candidates

Each row is pending. Code owner: `6889fec1`. Bind after ordinary Review and landing; Code ran no
deliberate mutant. Witness names are vitest test names in
`client/src/features/settings/settingsPendingReads.test.tsx` unless a file is given.

| ID | Mutation | Exact witness | Variants |
|---|---|---|---|
| PC-1 | ProjectDeleteDialog: read `impact.data` without the `impact.isError` guard, or drop the `PendingRead` line | `C1274 project delete dialog hides the stale impact and keeps Delete disabled after a failed refetch`; `C1274 'project deletion impact' read 'paused'` | stale data; paused label |
| PC-2 | ApiKeysSection: drop `listUnknown` from the Save key `disabled`, or drop `keys.isError` from `listUnknown`, or key the list on `isLoading` | `C1274 api keys offline keep Save key disabled with a typed key and send no PUT after reconnect`; `C1274 'api keys' read 'failed'`; `C1274 'api keys' read 'paused'` | pending gate; failed gate; list label |
| PC-3 | TemplateManager: drop the groups, providers or model-routing `ListRead` branch (back to the empty text), or the templates `pendingReadLabel` return | `C1274 'template groups' read 'paused'`, `… 'failed'`; `C1274 'template providers' read 'paused'`, `… 'failed'`; `C1274 'template model routing' read 'paused'`, `… 'failed'`; `C1274 'templates' read 'paused'` | groups; providers; routing; templates |
| PC-4 | ProjectConfig: drop the projects pending return, or the Boards cell pending/failed branches | `C1274 'projects' read 'paused'`; `C1274 'project boards' read 'paused'`, `… 'failed'` | projects; boards pending; boards failed |
| PC-5 | AgentTuiConfig, StatusTab, ProviderConfig: key the page on `isLoading` again | `C1274 'agent tui profiles' read 'paused'`; `C1274 'github status' read 'paused'`; `C1274 'providers' read 'paused'`; `C1274 $name offline from the first render shows its read as unknown` rows | one per component |
| PC-6 | DispatchConcurrencyEditor: drop `historyPending` from the empty-history condition | `DispatchConcurrencyEditor.test.tsx` `C1274 history read loading shows no empty history`, `… paused …` | loading; paused |

Use method-scoped bound packs and red/restore/green receipts. Mutation owns final binding and
missing-control discovery.

## Repair 9277c201 additions (pending)

Ordinary red selection at `b2ba84ec817811dfcaaa9008d77473cbdf10f6f7`: 31 failed, 116 passed.
This is defect reproduction against unmodified production, not Mutation qualification.
The following witnesses use the `C1274 repair` prefix; bind exact expanded Vitest names.

| ID | Mutation | Witness | Variants pending |
|---|---|---|---|
| PC-7 | Restore ungrouped placeholder or enable Group while its read is unknown; remove stored-id fallback | settingsPendingReads: `C1274 repair 'Edit' Template Group read 'paused' retains its value`; Add/Edit state rows; `C1274 repair missing stored Group stays visible after successful list []` | Add/Edit x loading, paused, failed, stale failure; successful empty and partial group lists; retained payload |
| PC-8 | Restore negative-root/zero-limit Delegation fallback | ProjectSetupModal: `C1274 repair delegation catalog paused shows unknown instead of zero limits` and its loading/failed/stale-failure rows | four catalog states, failure detail |
| PC-9 | Expose catalog-dependent agent draft or remove Create disabled condition; bypass preset initialization | ProjectSetupModal: `C1274 repair setup catalog paused withholds the agent draft and recovers preset payload` and its loading/failed/stale-failure rows | draft visibility; Create gate; recovery request fields (Frontier, Phone, alwaysOn, remoteControlEnabled, bundles) |
| PC-10 | Treat unknown profiles as unsupported/resolved or remove profile Create gate | ProjectSetupModal: `C1274 repair profiles paused show remote support unknown and block agent create` and its loading/failed/stale-failure rows | profile label; RC description/disabled; Create gate; recovered RC flag |
| PC-11 | Expose tier-only model select while its list is unread or remove models Create gate | ProjectSetupModal: `C1274 repair selected models paused show unknown and block agent create` and its loading/failed/stale-failure rows | four model states; Create gate |
| PC-12 | Submit a missing selected preset or suppress empty-preset diagnosis | ProjectSetupModal: `C1274 repair empty presets are explicit and require skipping the first agent` | empty label; disabled agent create; explicit skip payload |
| PC-13 | Remove runner type unknown text/disabled state or keep setup guidance through failed read | settingsPendingReads: `C1274 repair Runner type read paused retains the profile kind` and its loading/failed/stale-failure rows | four runner-type states; guidance; recovery selection |

PC-1 through PC-13 and their listed variants remain pending. Mutation also owns missing-control
discovery, including direct submit-handler guard independence and runner-type list partial results.
