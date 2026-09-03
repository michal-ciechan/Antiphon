# CARD-0323 — reuse a project Herdr workspace and consume a created root pane (2026-09-03)

## Verdict

Implement two deliberately narrow changes in `HerdrPaneChild`:

1. Reuse one **untagged**, exact-label Herdr workspace as an operator-owned placement container, but never write `antiphon-ws` to it. The existing exact `antiphon-ws == WorkspaceKey` match remains the authoritative match for Antiphon-created workspaces.
2. When Antiphon does create a workspace, use the returned root tab and root pane for the launch rather than running the ordinary allocator's `tab.create` branch. Rename that newly created tab to `PaneTitle`.

This fixes first launch without changing the 2x2 allocator, relocating live panes, or turning an operator's whole workspace into Antiphon furniture.

Herdr's documented creation contract explicitly returns `workspace`, `tab`, and `root_pane`, and directs automation to use that returned pane for the first process. Its workspace/tab creation calls also accept cwd and environment for the root shell. [Herdr agent automation](https://herdr.dev/docs/agent-automation/) and [CLI reference](https://herdr.dev/docs/cli-reference/) confirm those shapes.

## Decisions

### D1 — workspace matching: token first; one untagged exact label second

`EnsureWorkspaceAsync` will select in this order:

1. A workspace whose `tokens["antiphon-ws"]` exactly equals `opts.WorkspaceKey`. This remains the strong, pre-existing Antiphon-owned identity and takes precedence even if a human later changes its label.
2. Otherwise, exactly one workspace whose `Label` exactly equals `opts.WorkspaceLabel` **and which has no non-empty `antiphon-ws` token**. This is an operator-visible placement match, not an ownership match.
3. Otherwise, create a workspace. That includes no label match, two or more untagged label matches, and a same-label workspace that is explicitly tagged to a different Antiphon workspace key.

Do not retain the current `tokens["cwd"]` predicate. Antiphon never writes that token, so it makes the intended fallback unreachable. Do not add a `workspace.get`/live-pane-cwd heuristic in this card: workspace list/get identity is label plus metadata, while pane cwd is per-terminal state. A project workspace can deliberately contain agents in subdirectories or worktrees, so cwd absence or mismatch would reject the real `PredictionMarkets` shape, and a coincidental cwd match is not a durable ownership proof. Exact label is useful only when unique; ambiguity must create a new managed workspace and log the candidate ids/labels.

This cannot prove that a uniquely labelled operator workspace represents the intended project; no runner-visible durable project identity exists for that. The safety boundary is therefore explicit: automatic reuse places one new Antiphon tab in that space but never stamps it, never splits its existing tabs, and never treats its other panes as allocator capacity. A future explicit project-to-Herdr-workspace picker can provide stronger operator intent if needed.

### D2 — use `workspace.create`'s root tab/pane only for this new workspace

Change `HerdrClient.WorkspaceCreateAsync` to accept the launch environment and return the existing complete `HerdrWorkspaceCreateResult`, not only its `Workspace`. Its result must contain `Tab` and `RootPane`; a missing field is a protocol-shape failure, not a reason to call `tab.create` and leave an unknown default pane behind.

Have `EnsureWorkspaceAsync` return a small internal result such as:

```csharp
private sealed record EnsuredWorkspace(
    string WorkspaceId,
    bool RefreshesAntiphonWorkspaceToken,
    HerdrTabInfo? CreatedRootTab = null,
    HerdrPaneInfo? CreatedRootPane = null);
```

`LaunchAsync` still resolves a CARD-0224 last-pane target first. Only when that result is `Allocate` and `CreatedRootTab`/`CreatedRootPane` are present does it bypass `AllocatePaneAsync` and pass the root ids to `CompleteTypedLaunchAsync`. Relaunch/adopt paths remain exactly as they are.

In that fresh-root branch:

- Set `_paneId` before any fallible preparation, then call a new typed `TabRenameAsync(createdTabId, opts.PaneTitle, ct)`. It is safe to rename because this tab was returned by the creation call, not discovered in an operator workspace.
- Keep the existing `PaneRenameAsync` and `PaneReportMetadataAsync` calls for the root pane, then launch there. The tab and pane will both display `PaneTitle`; there is no default `"1"` tab left idle.
- Pass `request.Env` to `workspace.create`, so the root shell has the same launch environment that `tab.create`/`pane.split` currently receive. Do not put environment values into a typed PowerShell command or the launch-script file.
- Create the workspace at `opts.WorkspaceCwd` as today so it retains its project-container meaning. Because the root shell may need the session's distinct worktree cwd, extend `HerdrLaunchScript` with an optional, PowerShell-quoted `Set-Location -LiteralPath <request.Cwd>` prelude used only for this freshly-created root branch. It runs inside the existing one-script/one-Enter delivery contract before `& <exe> @(args)`. Normal `tab.create` and `pane.split` launches retain their existing direct cwd behavior and script bytes.

Add the thin `tab.rename` parameter model and client wrapper, plus the corresponding fake-server route. Do not change `HerdrPaneAllocator`: its operator-tab exclusion and 2x2 geometry remain correct. This is a one-time creation fast path before the allocator is relevant.

If a root-tab rename or launch fails, the normal `StartHerdrAsync` cleanup begins with `_paneId` already set and closes the pane. Herdr removes an empty last tab itself; do not add `tab.close`.

### D3 — never automatically tag a reused operator workspace

Only these paths call `WorkspaceReportMetadataAsync` with `antiphon-ws`:

- A workspace just created by this launch.
- An existing workspace selected by the exact matching `antiphon-ws` token, to preserve the current best-effort TTL refresh behavior.

The unique untagged label fallback must not report `antiphon-ws`, now or on later launches. Its Antiphon session panes continue to have their normal pane metadata and sidecars, but the workspace remains operator-owned. This preserves CARD-0213's ownership line: a shared workspace is not evidence that Antiphon owns human tabs, panes, or processes.

Existing duplicate spaces are intentionally not consolidated. If a tokened `wK` already exists beside an untagged `w2`, the strong token match keeps future launches in `wK`; this card neither moves its live pane nor closes either workspace. A human-directed migration/cleanup would need its own card.

## Implementation slices

### S1 — typed client and creation result

Touch:

- `src/Antiphon.SessionRunner/HerdrApiModels.cs`
- `src/Antiphon.SessionRunner/HerdrClient.cs`
- `tests/Antiphon.SessionRunner.Tests/FakeHerdrServer.cs`
- `tests/Antiphon.SessionRunner.Tests/HerdrClientTests.cs`

Make `WorkspaceCreateAsync(cwd, env, label, ct)` return `HerdrWorkspaceCreateResult`; validate that the returned tab/root pane are present. Add `HerdrTabRenameParams` and `TabRenameAsync`. Update the fake's `workspace.create` to preserve the supplied environment on its root pane and add its `tab.rename` route. Update the client wrapper test to assert the full creation result, `workspace.create` environment, and `tab.rename` wire shape.

### S2 — safe ensure policy and root handoff

Touch:

- `src/Antiphon.SessionRunner/HerdrPaneChild.cs`
- `src/Antiphon.SessionRunner/HerdrLaunchScript.cs`

Replace the `string workspaceId` return with `EnsuredWorkspace`; implement D1 and D3 exactly. Have the newly created result carry the returned root objects. Thread the result through `LaunchAsync` so the root fast path occurs only after CARD-0224 target resolution elects normal allocation. Extend `CompleteTypedLaunchAsync` with an optional created-root-tab id and root-working-directory flag; perform the tab rename after `_paneId` is set and use the cwd prelude only on that branch.

No wire contract, database migration, server resolver, allocator decision, sidecar schema, attach path, or session-restart path changes.

### S3 — regression tests and owned documentation

Add focused `HerdrLaunchShapeTests` cases, all against `FakeHerdrServer`:

1. **Fresh workspace uses the root:** one `workspace.create`, zero `tab.create`, one `tab.rename` to `PaneTitle`; exactly one tab/pane hosts the launched agent; the sidecar records the returned root ids.
2. **Root keeps launch context:** make `WorkspaceCwd` and `RunnerLaunchRequest.Cwd` differ and include a sentinel environment value. Assert `workspace.create` receives the workspace cwd and environment, the root receives that environment, and only the fresh-root script contains the safely quoted `Set-Location` prelude for the request cwd. The sentinel must not occur in typed text or the script.
3. **Unique untagged label reuse:** seed an operator workspace named `PredictionMarkets` with no tokens. Assert no `workspace.create`, ordinary `tab.create` targets that workspace, and no `workspace.report_metadata` writes `antiphon-ws` to it.
4. **Ambiguous/foreign match refuses automatic reuse:** seed two untagged same-label spaces, then separately a same-label space tagged with another key. Assert each case creates a new workspace rather than placing an Antiphon tab in either candidate.
5. **Own token still wins:** seed an owned matching-token workspace and an untagged same-label one. Assert no new workspace, token refresh remains, and allocation stays in the owned workspace.

Update `docs/herdr-sessions.md` §3/§4 to document the token/unique-label policy, no stamping of reused workspaces, and first-use of a newly-created root pane. State explicitly that existing roots are not an allocator slot and that operator tabs remain unsplittable.

## Verification

Run the narrow runner classes sequentially (the project already handles the fake named-pipe scope):

```powershell
dotnet run --project tests/Antiphon.SessionRunner.Tests -- --treenode-filter "/*/*/HerdrClientTests/*"
dotnet run --project tests/Antiphon.SessionRunner.Tests -- --treenode-filter "/*/*/HerdrLaunchShapeTests/*"
```

Then run the full runner assembly when practical:

```powershell
dotnet run --project tests/Antiphon.SessionRunner.Tests
```

Post-deploy, use a disposable Herdr session and fixture directories, never `w2`/`wK` or another live operator workspace. Verify (a) one untagged, uniquely labelled operator fixture receives an Antiphon-created tab but no workspace token, and (b) a new label produces one workspace, one renamed tab/root pane, no extra `tab.create`, correct pane cwd/environment, and a removable session. Inspect the result before cleanup; do not move or close production panes as part of this card.

## Related-card boundary

| Card | Preserved boundary |
|---|---|
| CARD-0160 | Keeps project-key placement context, runner-side sidecars, token best-effort semantics, and the pure 2x2 allocator. Corrects only the dead `label + tokens.cwd` fallback and discarded creation result. |
| CARD-0225 | Keeps `PaneTitle` as the agent-visible tab/pane display value. The fresh root tab is renamed to that same value. |
| CARD-0224 | Leaves last-pane target resolution before normal allocation; a reusable prior pane still wins over any creation-root fast path. |
| CARD-0213 | Does not attach, rename, split, metadata-tag, or otherwise take lifecycle authority over an operator pane. Reusing a workspace is not attaching a pane. |

## Non-goals

- No automatic consolidation, migration, closure, or movement of current `wK`/`wJ` duplicates or their live agents.
- No owner picker, persisted Herdr workspace id, or project-to-workspace database mapping.
- No changes to operator-tab splitting, the quad geometry, `agent.start` policy, attach ownership, restart adoption, or `tab.close` rule.
- No best-effort cwd matching heuristic. If label uniqueness is insufficient for a future deployment, add an explicit operator selection mechanism rather than silently broadening ownership.
