# CARD-1960 + CARD-1961: the workspace-slot fixes needed to turn slot leasing back on (plan)

Plan task 87ebe4f0 (Opus), 2026-11-17. This is a plan only: no code, no setting change, no restart and no
release were made. "UNOBSERVED" marks a fact this task could not read.

**Sources read, with exact SHAs**

- master `c3cc0bb9b7b1d0c55464d07cd8e60a63b4239028` (= `origin/master` = this branch's base). Every
  `file:line` below is at this SHA.
- Incident note `docs/investigations/2026-11-17-slot-pool-degraded-incident.md` on
  `origin/feat/card-task-23362b22`, commits `7dfd7dce9a0982416d33e0c8edeec31b9b17b31c` and
  `3629e0787e846dec6e73ddcb2a91d4b2215042eb` (the tip).
- The CARD-1960 and CARD-1961 card bodies (`card.ps1 get`, 0 revisions each), CARD-1957 and CARD-1893.
- The live server: `GET /api/version` = `870a93d21f2b18c85a8b0905ab689b28e3099813`, which is the tip
  of `origin/rc/2026-11-24`. `GET /api/runner-defaults` gives `globalRunnerId=server2` (revision 2).
  `GET /api/session-runners` shows `server2` (linux, advertises `workspaceSlotV1`), `desktop` (windows,
  no slot feature) and `server2-temp` (no features listed).

## Outcome in one paragraph

The two cards touch separate files, so they can run in **parallel**. CARD-1960 is **server + script
+ docs only**: the runner already handles phone-home op 44 `WorkspaceSlotQuarantineRelease`. It
reaches production through an rc port and a **desktop AppHost restart**. It does **not** need the
server2 rollout. CARD-1961 is **runner only** (`RunnerWorkspaceSlotPool.cs`). It reaches server2
only through an rc port and the server2 runner rollout, and that rollout is on hold for CARD-1893.
After 1960 is live, the five open quarantines can be released, and roles that do not build can
lease slots again. Roles that run checkpoint builds wait for 1961 on server2.

## Ground truth: what the cards assume vs what the code does

| # | Card or brief assumption | Code at c3cc0bb9b | Consequence |
|---|---|---|---|
| G-1 | "both fixes are runner code" (brief) | Only 1961 is. The op-44 handler exists: `PhoneHomeCommandDispatcher.cs:488-493` → `RunnerWorkspaceSlotPool.ReleaseQuarantineAsync` (`:942`). Both came in `d131390bf`, the same commit as the `WorkspaceSlotV1` feature. That commit is an ancestor of `rc/2026-11-24`. server2 advertises `workspaceSlotV1`, so its runner has the handler. The exact runner SHA on server2 is UNOBSERVED: `/api/session-runners` has no SHA field. | 1960 activates with an AppHost restart. It does not have to wait for the CARD-1893 hold. |
| G-2 | No caller for op 44 | True. The server has only `cold-reset` (`SessionRunnerEndpoints.cs:153`) and `{slotId}/record-release` (`:181`). `PhoneHomeRunnerClient` has `ReleaseWorkspaceSlotAsync` and `ColdResetWorkspaceSlotsAsync` (`:527-540`) but no quarantine method. `PhoneHomeLiveConnection.cs:357` already sets a 5-minute timeout for op 44. | Add one client method and one route. |
| G-3 | 1960: "script verb (HTTP stub like the cold-reset tests)" | No test runs `scripts/workspace-slots.ps1` at all. The only cold-reset test is the server route test `C0824_OperatorColdResetRouteRecordsAndForwards` (`C0824WorkspaceSlotServerTests.cs:334`). The HTTP-stub pattern to copy is `tests/Antiphon.Tests/Scripts/RoutingPinScriptTests.cs` (`StubApi` over `EphemeralHttpListener`). | The script test class is new; it does not extend an existing one. |
| G-4 | The runner refuses an unknown disposition | `ReleaseQuarantineAsync` throws `Refuse(...)` → `PhoneHomeAdmissionException(UnsupportedTarget, 409)` (`:1378`). The server maps that to `ConflictException` (`PhoneHomeRunnerClient.cs:725-731`). A closed or unknown dirId also returns 409. | The route validates first and returns 400. It never sends a request the runner would refuse. |
| G-5 | The Pushed release checks are enough | Pushed runs `UnpushedAsync` with a fetch, then `DirtAsync` (`:950-955`). It does **not** recheck process holders. Removed runs **no** check (`:961`). | D-6 (recommend Pushed) and Q-4. |
| G-6 | 1961: the reset leaves the parent of a nested keep-list name | Confirmed on paper. The warm clean uses `-e **/obj/ -e **/bin/ -e **/node_modules/ -e /client/dist/` (`WorkspaceSlotContracts.cs:100,107-120`). `CheckFreeAsync` lists `status --ignored=matching` and keeps an `!!` entry only when some segment is literally `obj`, `bin` or `node_modules` (`:645-650`, `IsKeepListPath` `:123-135`). The retry in `SettleAsync` (`:438-443`) repeats the same clean, so the slot is quarantined `filesystem/reset` with `residue: ...`. | The red witness W-B1 below. |
| G-7 | 1961: "Keep the keep-list for the directories the pool must preserve" | `ResetForLeaseAsync` (`:253-268`) also runs the warm clean, but a lease reaches it only after `PreflightFreeAsync` → `CheckFreeAsync` has passed (`:113`). A Free slot therefore has no rejected residue. The cold paths (`CleanArguments(cold: true)`, `:1072`) have no `-e`. | Change only `ResetForFreeAsync`'s warm path (D-8). |
| G-8 | The ledger attention rows can be watched | Attention rows exist only in the runner ledger (`Attention(state, ...)`, `:883-884`, `:830`). No server route exposes the slot ledger. `GET /sessions/workspace-slots` is S4 and was not built (`docs/ops-http.md:428-430`). | The re-enable steps read `/work/workspace-slots/ledger.json` read-only inside the runner container, as the incident did, and also read the server log. |
| G-9 | `*` means every role | `*` is every role except Mutation, and SourceLanding never leases (`WorkspaceSlotRoles.cs:4-6`). Lists take exact `AgentTaskRole` names: Custom, Plan, Code, Review, Debug, Coverage, Docs, Commit, Test, Deploy, Merge, Check, Distill, Diagnose, Investigate, TestDesign. | Staged lists in the rollout section. |
| G-10 | The pool degrades at 4 | It refuses at **more than** 4 (`> MaxQuarantinedDirectories`, `:95`), that is, at the fifth open entry. A slot retires on the fourth re-home in 24 h (`> MaxRehomesPerSlotPerDay` 3, `:826`). ws-01 re-homed at 16:00Z, 22:02Z and 01:05Z (incident note). | One more ws-01 quarantine before about 16:00Z on 2026-11-17 retires ws-01. |
| G-11 | The five quarantines are still open | They were open at 01:40Z (incident note). Their state now is UNOBSERVED; this task read no ledger. | Re-read the ledger before step R-1. |

## Decisions (D-n)

- **D-1. 1960 is server-only, with no runner change.** The route forwards to the existing op 44. The
  requester is folded into the reason the runner records (`"<reason> (by <requester>)"`, bounded to
  200 characters), because `PhoneHomeWorkspaceSlotQuarantineReleaseRequest` has no requester field.
  *Rejected:* adding a requester field or a runner HTTP route. Either would make 1960 runner code and
  tie it to the held server2 rollout.
- **D-2. The route validates everything it can before sending.** It requires the operator token
  (`OperatorCredential.Require`). It requires a `dirId` matching `^[0-9a-f]{12}$` (the generator is
  `Guid.NewGuid().ToString("N")[..12]`, `:867`) and a disposition that is exactly `Pushed`,
  `AcceptLoss` or `Removed`, compared ordinally. The reason must be 1..200 characters
  (`RunnerStateService.MaxReasonLength`), and the requester must pass
  `WorkspaceSlotReleaseService.IsAcceptableRequester`. Any failure returns
  `400 workspace_slot_quarantine_release_invalid`, and nothing is sent. A runner that is not phone-home
  returns `409 workspace_slot_runner_not_phone_home`, as cold-reset does. The runner's
  `refused` and `removal_failed` answers return as **200** with the runner's body, because they are
  answers, not transport failures. *Rejected:* mapping `refused` to 409. That would hide the runner's
  detail behind the generic error handler.
- **D-3. The route shape** is `POST /api/session-runners/{runnerId}/workspace-slots/quarantine/{dirId}/release`.
  Its body is `WorkspaceSlotQuarantineReleaseRouteRequest(string Disposition, string Reason, string? Requester = null)`.
  The DTO goes in `server/Application/Dtos/RemoteWorkspaceMirrorResult.cs`, beside the cold-reset
  DTO. The route has five segments after `workspace-slots` and record-release has two, so the routes
  cannot collide.
- **D-4. The script verb is `release`.** Usage: `-Runner <id> -DirId <12hex> -Disposition Pushed|AcceptLoss|Removed -Reason <text> [-Requester]`.
  `-AllRunners` and `-Slot` with `release` exit 2. The disposition uses `ValidateSet`. The script
  prints `runner=<id> dir=<dirId> outcome=<outcome> [detail]`. It exits 0 only on `removed`, 1 on
  `refused`, `removal_failed` or an HTTP failure, and 2 on usage errors. The script stays ASCII-only,
  and the token is never printed.
- **D-5. Docs.** `docs/ops-http.md:428-430` names the new route and the verb. `docs/docker-stack.md`
  gets one sentence in the paragraph at `:931-933` that names the front door
  (`workspace-slots.ps1 release`). No other docker-stack.md lines change (see Collisions).
- **D-6. Use `Pushed` for published residue; reserve `Removed`.** `Removed` deletes with no check
  (G-5). The runbook text and the script help say so.
- **D-7. 1961 changes no contract.** The scoped clean is a private helper inside
  `RunnerWorkspaceSlotPool.cs`. `WorkspaceSlotContracts.cs` stays untouched. *Why:* editing that
  contracts file pulls `C0824WorkspaceSlotRoleGateTests`, `C0824WorkspaceSlotServerTests`,
  `C1656RecordReleaseHardeningTests` and `PhoneHomeRollingRunnerTests` (34 cases, three files) into
  the impacted scope, and those tests do not exercise this change. *Rejected:* the incident's
  `WorkspaceSlotResetPolicy.ScopedCleanArguments` in contracts.
- **D-8. 1961 changes `ResetForFreeAsync` only, and only when warm.** After the warm clean succeeds,
  it runs `status --porcelain=v1 -z --untracked-files=all --ignored=matching`. For each `!!` entry
  that `IsKeepListPath` rejects, it runs
  `git --literal-pathspecs clean -ffdx -- <entry>` with no `-e`. A non-zero exit returns
  `"scoped clean: " + Bound(stderr)`. That stays the existing `filesystem/reset` quarantine.
  `CheckFreeAsync` and the reset must share one helper that selects the entries, so the two cannot
  disagree. `--literal-pathspecs` stops a residue name such as `*` or `:(glob)` from widening the
  clean to keep-list output. *Safety argument:* the warm clean already deleted everything in those
  directories except the nested `obj/`, `bin/` and `node_modules/`. The new step adds only those
  nested build directories under parents the free check already rejects. `DirtAsync`
  (`:417`), the unpushed check and `RetainEvidence` all still run first. *Rejected:* (a) widening
  `IsKeepListPath` to keep such parents: checkpoint output (GiB-scale) would carry across leases,
  stale dlls with it. (b) A cold clean on every release: this loses the warm native output the
  CARD-0824 design keeps. (c) Anchored `-e` patterns: gitignore syntax cannot say "`obj/` not under
  an ignored non-keep parent".
- **D-9. Not in either card:** automatic release (Q-3), raising `MaxQuarantinedDirectories` (Q-2),
  a holder recheck in Pushed (Q-4). Each of these needs its own card.

## Slice A: CARD-1960 (server route, client method, script verb, docs)

**Change**

| Slice | Files | What |
|---|---|---|
| A1 | `server/Infrastructure/Agents/SessionRunner/PhoneHomeRunnerClient.cs` | New `ReleaseWorkspaceSlotQuarantineAsync(PhoneHomeWorkspaceSlotQuarantineReleaseRequest, ct)` beside `ColdResetWorkspaceSlotsAsync` (`:535`): `RequestAsync(PhoneHomeOperation.WorkspaceSlotQuarantineRelease, ...)` then `Read<PhoneHomeWorkspaceSlotQuarantineReleaseResponse>`, or `Missing("workspace-slot-quarantine-release")`. |
| A1 | `server/Application/Dtos/RemoteWorkspaceMirrorResult.cs` | `WorkspaceSlotQuarantineReleaseRouteRequest` (D-3). |
| A1 | `server/Api/Endpoints/SessionRunnerEndpoints.cs` | The route (D-2, D-3), after the cold-reset route (`:153-175`). It logs `WorkspaceSlotQuarantineRelease` at Warning with runner, dirId, disposition, requester and reason. |
| A1 | `tests/Antiphon.Tests/Application/C1960QuarantineReleaseRouteTests.cs` (new) | Witnesses W-A1..W-A3. |
| A2 | `scripts/workspace-slots.ps1` | The `release` verb (D-4). Header usage lines are updated. |
| A2 | `tests/Antiphon.Tests/Scripts/C1960WorkspaceSlotsScriptTests.cs` (new) | Witnesses W-A4, W-A5. |
| A3 | `docs/ops-http.md`, `docs/docker-stack.md` | D-5. |

**Red witnesses (each fails at c3cc0bb9b, argued from the code)**

- **W-A1** `C1960QuarantineReleaseRouteTests.Route_forwards_release_and_returns_runner_answer`.
  It uses `PhoneHomeTestHost` and a scripted peer, as `C0824_OperatorColdResetRouteRecordsAndForwards`
  does. With no token, the route returns 400-403 and the peer sees 0 op-44 frames. With a token,
  `{Pushed, "published", "orchestrator"}` sends one op-44 frame whose payload has the same `DirId`
  and `Disposition` and a `Reason` that starts with `published` and names `orchestrator`. The peer's
  `refused` answer returns as 200 with `outcome=refused` and the detail. A second case answers
  `removed`. *Red at base:* no route matches the path, so ASP.NET returns 404 and the
  `ShouldBeInRange(400,403)` / 200 assertions fail.
- **W-A2** `C1960QuarantineReleaseRouteTests.Route_refuses_invalid_input_before_sending`
  (`[Arguments]`: `pushed` (wrong case), `Delete`, the empty string, a dirId of `../x`, a dirId of
  13 hex digits, a 201-character reason, and a requester equal to the token). Each returns 400
  `workspace_slot_quarantine_release_invalid`, and `peer.RequestCount(WorkspaceSlotQuarantineRelease)`
  stays 0. *Red at base:* 404 is not 400.
- **W-A3** `C1960QuarantineReleaseRouteTests.Route_releases_real_quarantine_end_to_end`. It uses
  `C0824SlotTransportWorld` with a real runner pool. Its `Answer` (`C0824SlotTransportWorld.cs:122-133`)
  forwards only Mirror and SlotRelease frames, so the change adds op 44 to that list. The test
  leases one slot through a tick, plants an untracked `work.txt`, sets the task Succeeded and
  reconciles. The runner quarantines `custody/dirty`. That reason does not depend on 1961, so the
  test is unaffected by the other card. POST `Pushed` returns `refused` with `dirty:`. POST
  `AcceptLoss` returns `removed`. Then the ledger entry has `ClosedAt` set and
  `Disposition=AcceptLoss`, the directory is gone, and `retained/quarantine/<dirId>/untracked/work.txt`
  has the planted bytes. *Red at base:* 404, so the entry stays open and the directory stays. Code
  must confirm that `Rig.Host` resolves `ISessionRunnerDirectory` to `PhoneHomeRunnerDirectory`. The
  cold-reset test registers it by hand. Whether `Rig` does is UNOBSERVED.
- **W-A4** `C1960WorkspaceSlotsScriptTests.Release_posts_body_and_maps_outcomes` (`[Arguments]`
  removed→0, refused→1, removal_failed→1, HTTP 409→1). The stub API records the method, path and
  JSON body (`disposition`, `reason`, `requester`). stdout carries `outcome=` and never contains the
  token text. *Red at base:* `ValidateSet('cold-reset')` rejects `release`, so the script exits 1
  with a parameter-binding error and the stub records no POST.
- **W-A5** `C1960WorkspaceSlotsScriptTests.Release_usage_errors_exit_2_and_file_is_ascii`
  (`-AllRunners`, `-Slot`, missing `-DirId`). Each exits 2 and the stub sees no request. The test
  also asserts every byte of the script file is below 0x80. *Red at base:* the verb is unknown, so
  the exit is 1, not 2. The ASCII assertion is a guard that is green today.

**Must NOT touch:** `src/Antiphon.SessionRunner/**` (runner), `src/Antiphon.SessionRunner.Contracts/**`,
`PhoneHomeContracts.cs` op numbers, `PhoneHomeLiveConnection.cs` timeouts, the record-release and
cold-reset routes, `WorkspaceSlotReleaseService` logic, any EF migration, `appsettings*.json`,
compose and stack files, `tests/impacted-classes.json`, `tests/derived-impact-index.json` (Merge
regenerates it), and every docs/docker-stack.md line outside `:925-937`.

**Review and Merge.** The Code task (Worktree, server2, portable) commits the red tests first, then
green. Review round 1 is full; rounds 2-3 review only the diff (CARD-1635). It checks D-2's
pre-send validation, that the token never reaches a log, and the CP rows. Land to master, then
merge into the rc line with a Merge-role task (`-StartRef` at the rc tip, `docs/release-gates.md`
"Merging the rc line"). Activation is in the rollout section.

## Slice B: CARD-1961 (scoped clean of rejected ignored residue)

**Change.** `src/Antiphon.SessionRunner/RunnerWorkspaceSlotPool.cs` only:

- Extract `ForeignEntries(string porcelainZ)` from `CheckFreeAsync` (`:645-648`): the status entries
  that are not `!!` keep-list entries. `CheckFreeAsync` keeps its behaviour and calls the helper.
- In `ResetForFreeAsync` (`:505-507`), when `!cold`, after the warm clean succeeds:
  `ClearRejectedIgnoredAsync(path, ct)` runs the status listing, takes the `!!` entries from
  `ForeignEntries`, and runs `--literal-pathspecs clean -ffdx -- <entry>` for each. It returns the
  first failure (D-8). `??` and tracked entries never reach this step: `DirtAsync` already
  quarantined them, and `checkout --force` plus `clean -x` removed what was left.
- Test file: `tests/Antiphon.SessionRunner.Tests/C0824WorkspaceSlotResetTests.cs` (new methods; the
  class is `NativeLinuxOnly`, uses `ProcessSpawnLimit` and has a 60 s timeout).

**Red witnesses**

- **W-B1** `C1961_WarmReleaseClearsNestedKeepNamesUnderRejectedIgnoredParents`. Lease A. Plant
  `tests/X/bin-row/artifacts/obj/f.dll`, `.antiphon/control-x/coordinator-y/bin/ssh`, and native
  `src/P/obj/project.assets.json` and `src/P/bin/Debug/a.dll` with `OldMtime`. The scratch
  `.gitignore` already ignores `bin-*/`, `.antiphon/`, `bin/` and `obj/` (`C0824SlotScratch.cs:25-38`).
  Release, then assert: outcome `released`. `QuarantinedDirectories` is empty. The ledger path of
  `ws-01` is still `a.Path` (no re-home). `tests/X/bin-row` and `.antiphon/control-x` are absent.
  The native files are byte- and mtime-identical. One recorded argv in `scratch.Argv` is exactly
  `--literal-pathspecs clean -ffdx -- tests/X/bin-row/`. `WorkspaceSlotResetPolicy.KeepList` equals
  `["**/obj/","**/bin/","**/node_modules/","/client/dist/"]` (the card asks the test to state the
  keep-list). *Red at base (G-6):* the clean keeps `bin-row/artifacts/obj/` and so `bin-row/`. Status
  prints `!! tests/X/bin-row/`, whose segments contain no `obj`, `bin` or `node_modules`. The retry
  fails the same way, so the outcome is `quarantined`, with one `filesystem/reset` entry whose detail
  starts with `residue:`, and the slot re-homes to `ws-01-d2`.
- **W-B2** `C1961_UntrackedFileBesideResidueStillQuarantinesUntouched` (a guard, green at base).
  Plant the same residue plus an untracked, not-ignored `work.txt`. Release gives `quarantined`
  `custody/dirty`, and `work.txt` and `tests/X/bin-row/artifacts/obj/f.dll` are byte-identical in the
  kept directory. It goes red if a mutant moves the scoped clean before `DirtAsync`.
- Existing guards in the same row: `C0824_WarmResetWipesEveryNonKeepListClass` (V-2) and
  `C0824_WarmResetKeepsNativeOutputByteAndMtime` (V-27). V-27 asserts that no argv contains
  `bin-ws`. It plants no `bin-ws`, so the scoped clean never runs there and V-27 must stay green
  unchanged.

**Must NOT touch:** `WorkspaceSlotContracts.cs` (D-7), `KeepList`, `IsKeepListPath`,
`ResetForLeaseAsync`, `ColdResetAsync`, `ReleaseQuarantineAsync`, `QuarantineAndRehomeAsync`, the
re-home and retire caps, `PhoneHomeWorkspaceSlotSettings` defaults, any server file, any doc,
`tools/Antiphon.Checkpoints/**` (the layout at `BuildConfinement.cs:508-540` stays; D-8 handles it),
and every existing test assertion.

**Review and Merge.** These are the same steps as Slice A. In addition, Review attacks D-8 with
these mutants: drop `--literal-pathspecs`, scope the clean to every `!!` entry (that deletes
keep-list output and fails V-27), and move the clean before the dirt check (that fails W-B2).
Mutation checks are waived (operator 2026-11-12), so Review runs these as reasoning or dll-swap
probes, not as a Mutation stage.

## Existing tests affected (CARD-1782)

From `scripts/impacted-classes.ps1 -FileList` run at c3cc0bb9b, file by file:

- `SessionRunnerEndpoints.cs` and `RemoteWorkspaceMirrorResult.cs`: kind `contracts`. That selects
  E2E `ContractSnapshotTests`, plus `C0824WorkspaceSlotServerTests` and `C1656RecordReleaseHardeningTests`.
- `PhoneHomeRunnerClient.cs`: 26 derived classes (CP-A4, CP-A5).
- `docs/ops-http.md`: 24 classes (CP-A6, CP-A7). `docs/docker-stack.md`: 20 Antiphon.Tests classes
  (CP-A8, CP-A9) and `C1818GcOwnerRobustnessTests` (CP-A10).
- `scripts/workspace-slots.ps1`: none.
- `RunnerWorkspaceSlotPool.cs`: `C0824WorkspaceSlotProcessScanTests`, `C0824WorkspaceSlotRehomeTests`
  and `C1817RunnerRepositoryMaintenanceTests` (CP-B2).
- No migration, no operation label, no remote-script function and no statement budget changes. The
  public route and DTO are covered by CP-A12.

## Order and collisions

- **1960 and 1961 in parallel: yes.** Their file sets do not overlap. 1960 does not touch
  `RunnerWorkspaceSlotPool.cs`. Neither card edits the other's tests: W-A3 quarantines through
  `custody/dirty`, not residue.
- **docs/docker-stack.md.** CARD-1957 (`feat/card-task-c6c85b5b`) edits `:653-736`, which is far
  from `:931-933`. The docs fix that is already in flight (`feat/card-task-83d30eec` tip `3d1fb98c8`;
  the same commits are on `5f0633e8` and `d336fcc3`) **inserts 32 lines after `:935`**, two lines
  below 1960's sentence. Slice A3 must land after that docs fix, or Code must run
  `git merge-tree --write-tree` against the docs-fix tip before Review. If the result conflicts,
  re-cut A3 on the new master. Slices A1 and A2 do not depend on A3 and can land first.
- **Selector and index chain.** Neither card edits `tests/impacted-classes.json`,
  `tests/derived-impact-index.json` or the selector scripts. The new test classes reach the derived
  index only through the Merge's sanctioned regenerate step. Which card holds the "selector/index
  chain" was not named to this task: UNOBSERVED. No file overlap was found. A 4-day scan of the
  remote `feat/*` branches against the ten target files found only docker-stack.md edits.
- **rc.** Between `rc/2026-11-24` and master, there is no diff in `RunnerWorkspaceSlotPool.cs`,
  `WorkspaceSlotContracts.cs`, `SessionRunnerEndpoints.cs`, `PhoneHomeRunnerClient.cs`,
  `PhoneHomeCommandDispatcher.cs` or `workspace-slots.ps1`. Both code slices should therefore apply
  to the rc cleanly. The plan doc itself does not go to rc (rc carries no `docs/superpowers/plans`).

## Rollout chain

1. **CARD-1960 activation, desktop AppHost.** Land to master. Merge it into the current rc line,
   `rc/2026-11-24` or whichever rc the AppHost runs then. Then, from the main checkout at that rc
   tip, run `pwsh -NoProfile -File scripts/restart-apphost.ps1` and confirm `GET /api/version` shows
   that SHA. **Yes, the route needs an AppHost restart.** It does **not** need a server2 rollout
   (G-1). This holds while the production server is the desktop AppHost (CARD-1744). If the
   CARD-1744 window has passed by then, the activation is `scripts/restart-linux-server.ps1` instead;
   which host serves then is UNOBSERVED. The script ships in the same checkout.
2. **CARD-1961 activation, server2 runner.** Land to master and merge into the rc line. It reaches
   server2 only through the staged server2 rollout. That rollout is **on hold for the CARD-1893
   history-path compose change**, so 1961 must be in the rc that rollout deploys: ship both in the
   same rollout. Note that temp reads its own `secrets/stack.temp.env` and also has slots enabled
   (docs fix `83d30eec`). During the rollout, temp can quarantine with the old code if it runs a
   pre-1961 build; the route works for `-Runner server2-temp` too. No server change and no AppHost
   restart is needed for 1961.
3. **Re-enable slots in steps.** The setting is `Delegation:WorkspaceSlotRoles` in
   `C:\src\Antiphon\server\appsettings.Development.json`. It hot-reloads when no environment variable
   shadows it (`docs/docker-stack.md:938-944`). Read the evidence at every step:
   - the runner ledger, read-only, inside `antiphon-runner-session-runner-1`: open quarantines and
     their class and reason, `attention[]`, slot states, and `rehomes[]`;
   - the server log, for `workspace_slot_pool_degraded`;
   - the next task's `WorkspaceSlotDecision` event.

   | Step | Needs | Setting | Advance when | Roll back when (each rollback: set `none`, confirm `reason=roles_none`) |
   |---|---|---|---|---|
   | R-0 | 1960 live | (still `none`) | Release the open quarantines with `workspace-slots.ps1 release` per Q-1, one at a time. Read the ledger after each one. The open count must reach 0. | A `refused` or `removal_failed` answer: stop. That entry stays for the operator. |
   | R-1 | 1960 live (1961 optional) | `Plan,Docs,Investigate` | 4 h, or at least 5 slot releases, with no new open quarantine | Any `filesystem/reset` quarantine: a role that should not build left build output. |
   | R-2 | **1961 live on server2** | `Plan,Docs,Investigate,TestDesign,Review,Check,Debug` | 8 h, or at least 10 releases including at least 3 checkpoint-building tasks, with 0 `filesystem/reset` quarantines | Any `residue:` quarantine (1961 incomplete), or 3 or more open entries of any class |
   | R-3 | R-2 clean | add `Code` | 24 h at the 7-task server2 load, with open quarantines never above 2 | Same as R-2 |
   | R-4 | R-3 clean | `*` | Steady state | Same as R-2 |

   A `custody/lingering` quarantine, such as a `sleep` left by a background command, does not
   trigger a rollback by itself. Confirm that the holder pid is gone, then release it as `Pushed`.
   With outstanding leases, a rollback sets `none` and keeps runner support enabled until the leases
   release (CARD-0824 S1c). Before R-1 or R-2, check `rehomes[]` for ws-01: its first 24 h entry ages
   out at about 16:00Z on 2026-11-17 (G-10).

## Decision list for the operator (yes/no, with a recommendation)

- **Q-1. Release the five quarantines as `Pushed` once the route exists?** **Yes.** All five passed
  the runner-equivalent unpushed and dirt checks at 01:15-01:18Z, and `Pushed` reruns them with a
  fetch. Before releasing ws-01-d2 (`2d0ef6e62dae`), copy `.antiphon/c1956-review/` if that Review's
  evidence is wanted, because the release deletes it. For the two `custody/lingering` entries,
  confirm that pids 2840 and 5898 are still absent (G-5).
- **Q-2. Keep `MaxQuarantinedDirectories` at 4?** **Yes.** With the route available, the hold is
  recoverable in minutes, and 1961 removes the main source. Raising the limit costs about 2.3-2.5 GiB
  per entry against a 64 GiB budget, and it only delays the refusal.
- **Q-3. Auto-release published filesystem quarantines?** **No, not now.** File a separate card for
  the incident's option C: a sweep that releases only holder-free entries that pass the `Pushed`
  checks. Decide it after R-4, using the measured quarantine rate. It deletes with no operator in the
  loop.
- **Q-4. Add a holder recheck to the runner's `Pushed` path (G-5)?** **Yes, as a separate small
  runner card.** It rides the next server2 rollout and does not block 1960.
- **Q-5. Re-enable `Plan,Docs,Investigate` (R-1) before 1961 reaches server2?** **Yes.** Those roles
  do not normally run checkpoint builds, and any quarantine they produce can now be released.
- **Q-6. Ship 1961 in the CARD-1893 rollout rather than a separate one?** **Yes.** It is one
  rollout, and slots stay limited to R-1 until then.

## Session-waiting checklist

Neither card adds or changes a session that waits for input. Parking stays disabled. A Blocked task
keeps its slot, and no automatic release deadline exists (CARD-1083). This is unchanged.

## Platform and lanes

`/api/runner-defaults` sends all work to server2. Every row below is a Linux (server2) row. Rows
marked `linux-` need Linux: the SessionRunner slot classes are `NativeLinuxOnly`. Rows marked
`portable-` also run on Windows, but no Windows row is required. Omit `-Runner` and `-Platform` when
dispatching. The E2E `ContractSnapshotTests` row runs on server2 without Chromium.

## Performance budget

- **Measured sources:**
  - `C0824WorkspaceSlotResetTests`: Linux per-case p95 ≤ 5.2 s, n=5, class timeout 60 s (CARD-1722;
    class header).
  - `C0824WorkspaceSlotServerTests`: per-test p95 12.6-15.8 s alone, and 17.2 s loaded (load 37), on
    server2 at `30cb5279b` on 2026-11-02; class timeout 35 s (CARD-1660 header).
  - ScriptHarness invocations: one 300 s deadline per invocation (`docs/testing-and-build.md`).
- **New tests and timeouts:**
  - W-B1 and W-B2 inherit the 60 s class timeout. Each is about one lease and one release (≈ V-2,
    p95 ≤ 5.2 s), so 60 s is the existing 60 s Git floor, not a new number.
  - W-A1, W-A2 and W-A3 take `[Timeout(35_000)]` on their class. That is the 2x loaded p95 of the
    nearest measured relative: W-A3 ≈ `C0824_ReleaseRoundTripRealRunner`. **Provisional:** TestDesign
    or Code records n≥5 per-case durations and re-derives the timeout.
  - W-A4 and W-A5: about one pwsh start each, ≈ 2-5 s per case (unmeasured). The class takes
    `[Timeout(60_000)]` provisionally, with the same re-derivation duty.
- **Shared setup:**
  - The route tests share nothing across tests; each builds its own `PhoneHomeTestHost`, as the
    C0824 server class does, with the R5 note.
  - The W-A3 world builds one scratch origin and clone per test.
  - The script tests use one stub listener per test.
  - No container, migration or snapshot is added. `IsolatedTestSchema` comes from the existing
    `Rig`.
- **Phases:**
  - Build: a SessionRunner.Tests build takes about 2-4 min on server2 and an Antiphon.Tests build
    about 5-8 min; both are UNOBSERVED estimates.
  - Slot wait is reported separately.
  - Test bodies take the row minutes below.
- **Row estimates:** every row is 15 minutes or less, so each derived row timeout (3x the estimate) is 45 minutes or less, at the 45-minute advisory. The impacted
  rows CP-A4..A9 are **provisional**: their class costs on loaded server2 are UNOBSERVED. If any row
  goes over 30 minutes, Code splits it and files a Debug task with the timing. Recording: checkpoint
  receipts and TRX under the task's ignored `.antiphon/checkpoints/`.

## Verification design

TestDesign stays a separate stage (`next: plan-review`, then test-design). It refines the
`[Arguments]` sets, measures the provisional timeouts, and confirms the `Rig` directory wiring
(W-A3).

### Checkpoints

Every OR operand has parentheses and its own trailing `*`, and pipes are escaped. Each operand was
checked against the 2,141 test class names at c3cc0bb9b: each matches exactly one class. Min is a
floor on executed results; for an impacted union it is the class count. Do not run a row while its
new tests are absent. Rows A* belong to the CARD-1960 Code task and rows B* to the CARD-1961 Code
task, each run in its own plan extract.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-A1 | A1 | tests/Antiphon.Tests -> bin-c1960-route/ | portable-route | `/*/*/C1960QuarantineReleaseRouteTests/*` | W-A1..W-A3 | every method and case, 0 failed/skipped | 9 | 12 | true |
| CP-A2 | A2 | tests/Antiphon.Tests -> bin-c1960-script/ | portable-script | `/*/*/C1960WorkspaceSlotsScriptTests/*` | W-A4, W-A5 | every case, 0 failed/skipped | 7 | 12 | true |
| CP-A3 | A1 | tests/Antiphon.Tests -> bin-c1960-slotsrv/ | portable-slot-server-regression | `/*/*/(C0824WorkspaceSlotServerTests*)\|(C1656RecordReleaseHardeningTests*)/*` | DTO/route neighbours, cold-reset and record-release unchanged | both classes, 0 failed | 35 | 14 | true |
| CP-A4 | A1 | tests/Antiphon.Tests -> bin-c1960-client-a/ | portable-client-impact-a | `/*/*/(AgentTaskInputFallbackTests*)\|(C1818RepositoryMaintenanceStatusTests*)\|(ClaudeDialogDeliveryTests*)\|(DefaultRunnerPinTests*)\|(DelegateBranchDeliveryTests*)\|(GrokRulesReadyOrderingTests*)\|(PhoneHomeBackpressureTests*)\|(PhoneHomeCompletedCardTransportTests*)\|(PhoneHomeConnectionTests*)\|(PhoneHomeImmediateSendTests*)\|(PhoneHomeQueuedTurnTests*)\|(PhoneHomeReadAdmissionTests*)/*` | CARD-1782 PhoneHomeRunnerClient callers (1/2) | every named class, 0 failed | 12 | 15 | true |
| CP-A5 | A1 | tests/Antiphon.Tests -> bin-c1960-client-b/ | portable-client-impact-b | `/*/*/(PhoneHomeRecoveryEligibilityTests*)\|(PhoneHomeSessionRoutingTests*)\|(PhoneHomeSpillTransportTests*)\|(PhoneHomeStandingLaunchTests*)\|(PhoneHomeTaskDispatchProjectionTests*)\|(PtyConditionalReadClientTests*)\|(RunnerCapacityEndpointTests*)\|(RunnerCodexCliEvidenceTests*)\|(SessionMessageQueuePhoneHomeDropTests*)\|(TaskPlatformDispatchTests*)\|(TranscriptSinceClientTests*)/*` | CARD-1782 PhoneHomeRunnerClient callers (2/2) | every named class, 0 failed | 11 | 15 | true |
| CP-A6 | A3 | tests/Antiphon.Tests -> bin-c1960-docs-ops-a/ | portable-docs-ops-http-a | `/*/*/(AgentAttachHerdrTests*)\|(AgentBundleAttachmentTests*)\|(AgentSessionCancelledTurnTests*)\|(AgentSessionLaunchFailureTests*)\|(AgentSupervisionTests*)\|(AgentSystemPromptLaunchTests*)\|(AgentTaskCheckInterpreterTests*)\|(AgentTaskCheckScheduleTests*)\|(AgentTaskCheckSweepTests*)\|(AgentTaskDeadSessionReconciliationTests*)\|(AgentTaskDeliveryWatchdogTests*)\|(AgentTaskDispatchBaseGuardTests*)/*` | CARD-1782 rows for docs/ops-http.md (1/2) | every named class, 0 failed | 12 | 15 | true |
| CP-A7 | A3 | tests/Antiphon.Tests -> bin-c1960-docs-ops-b/ | portable-docs-ops-http-b | `/*/*/(AgentTaskDispatchFailureTests*)\|(AgentTaskInputSpillTests*)\|(AgentTaskInternalDecisionLifecycleTests*)\|(AgentTaskLandActiveSourceClaimTests*)\|(BlockedTaskParkProjectionTests*)\|(DockerStackDocumentationTests*)\|(InstructionBundleTests*)\|(InstructionFileStampTests*)\|(PipelineDefinitionDocumentationTests*)\|(RunnerBranchContractDocumentationTests*)\|(SessionRunnerAbsenceEvidenceDocumentationTests*)\|(StandingPipelinePolicyDocumentationTests*)/*` | CARD-1782 rows for docs/ops-http.md (2/2) | every named class, 0 failed | 12 | 15 | true |
| CP-A8 | A3 | tests/Antiphon.Tests -> bin-c1960-docs-stack-a/ | portable-docs-docker-stack-a | `/*/*/(ClaudeTokenRefreshOptInTests*)\|(CodexRootExecContractTests*)\|(CodexRunnerImageContractTests*)\|(DeployServer2ApiDefaultTests*)\|(DockerStackContractTests*)\|(DockerStackDocumentationTests*)\|(GrokRunnerImageContractTests*)\|(HangfireLeaseDocumentationTests*)\|(HangfirePostgreSqlStorageTests*)\|(HangfireStorageSelectionTests*)/*` | CARD-1782 rows for docs/docker-stack.md (1/2) | every named class, 0 failed | 10 | 12 | true |
| CP-A9 | A3 | tests/Antiphon.Tests -> bin-c1960-docs-stack-b/ | portable-docs-docker-stack-b | `/*/*/(JqRunnerImageContractTests*)\|(RcBridgeProbePlatformTests*)\|(RemoteScriptContractTests*)\|(RunnerPushCredentialDocsTests*)\|(Server2ProductionServerContractTests*)\|(Server2SidecarStandingAgentsTests*)\|(Server2StagingServerContractTests*)\|(ServerImagePushCredentialTests*)\|(VolumeRetirementApplyGateTests*)\|(ZombieCensusPlatformTests*)/*` | CARD-1782 rows for docs/docker-stack.md (2/2) | every named class, 0 failed | 10 | 12 | true |
| CP-A10 | A3 | tests/Antiphon.SessionRunner.Tests -> bin-c1960-docs-sr/ | linux-docs-gc-owner | `/*/*/C1818GcOwnerRobustnessTests/*` | CARD-1782 docker-stack.md SessionRunner row | every method, 0 failed | 10 | 8 | true |
| CP-A11 | A1 | n/a | portable-frontend-build | `pwsh -NoProfile -Command 'npm --prefix client run build; exit $LASTEXITCODE'` | fresh client/dist for the contract row | exit 0 | n/a | 3 | true |
| CP-A12 | A1 | tests/Antiphon.E2E -> bin-c1960-contract/ | portable-public-contract | `/*/*/ContractSnapshotTests/*` | CARD-1782 contracts kind (route, DTO) | every class method, 0 failed | 5 | 15 | true |
| CP-A13 | A3 | tests/Antiphon.Tests -> bin-c1960-guards/ | portable-guards | `/*/*/(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)\|(TestLaneCategoryGuardTests*)/*` | standing nine-class guard union (new classes' categories, layering) | 0 failed | 78 | 12 | true |
| CP-B1 | B1 | tests/Antiphon.SessionRunner.Tests -> bin-c1961-reset/ | linux-slot-reset | `/*/*/C0824WorkspaceSlotResetTests/*` | W-B1, W-B2, V-2, V-27 and the other reset rows | 12 methods, 0 failed/skipped | 12 | 8 | true |
| CP-B2 | B1 | tests/Antiphon.SessionRunner.Tests -> bin-c1961-impact/ | linux-slot-impact | `/*/*/(C0824WorkspaceSlotCustodyTests*)\|(C0824WorkspaceSlotRehomeTests*)\|(C0824WorkspaceSlotColdResetTests*)\|(C0824WorkspaceSlotProcessScanTests*)\|(C1817RunnerRepositoryMaintenanceTests*)/*` | CARD-1782 pool rows, plus dirt-before-reset, quarantine release and cold reset unchanged | every named class, 0 failed | 69 | 15 | true |
| CP-B3 | B1 | tests/Antiphon.Tests -> bin-c1961-guards/ | portable-guards | `/*/*/(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)\|(TestLaneCategoryGuardTests*)/*` | NativeLinux policy for the new Linux-only methods, and categories | 0 failed | 78 | 12 | true |

Notes on the counts:

- CP-A1 Min is 9: W-A1 1, W-A2 7 cases, W-A3 1.
- CP-A2 Min is 7: W-A4 4 cases, W-A5 3 cases.
- CP-A3 Min is 35: 25 + 10 results measured from source at c3cc0bb9b.
- CP-B2 Min is 69: Custody 19, Rehome 18, ColdReset 12, ProcessScan 3 and C1817 17.
- `tools/Antiphon.Checkpoints import --plan` at c3cc0bb9b accepted all 16 rows with no derived-timeout
  advisory warning. It printed 8 build-reuse notes. Each row keeps its own build on purpose, because
  the brief asks for one build per row. A Code task may reuse builds inside one After group if its
  caller allows it.
- If `NativeLinuxPolicyWitnessTests` needs a reseal for the new Linux-only methods, Code follows the
  policy-row recipe in the same slice.
