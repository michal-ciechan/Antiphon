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
