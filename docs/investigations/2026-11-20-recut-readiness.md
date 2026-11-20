# Recut readiness: can the next rc line be cut from master now (2026-11-20)

Investigate task `3b4e47f0`. Read-only except this note. No restart, deploy, tag, rc push, cut,
build or test was run. No secret, private-notes route or production Postgres was read.

**Sources.** Task base `ee40f9dd7215be6df529b4b13def00efeda04ca4`. `origin/master` was fetched at
10:45Z and was `a79ce4086ebbafd2d00d89b4107e07b9f5992463`. That is two commits past the base: the
graceful-stop fixture land of task `a4ead677`. Every "on master" below was checked with
`git merge-base --is-ancestor <sha> origin/master` at that tip. Live reads from
`$ANTIPHON_API` were taken 10:45-10:52Z:

- `GET /api/version`: `26a99f1a747d94120aeff8f0d6b9a2e7c7d4e248`, which is `rc/2026-11-29`.
- `GET /api/session-runners` and `/{id}/status`.
- `GET /api/agent-tasks?projectId=<Antiphon>&since=2026-11-18` (309 rows) and `GET /api/agent-tasks/{id}`.
- `card.ps1 get` / `list -Status Review|InProgress -All` on the Antiphon board.

## Verdict

**Cut now. Code-wise nothing on the list is missing from master.** The cut pin should be
`origin/master` re-fetched right before the push. The CARD-1820 S3 land (`c412d01f`, test-only)
published at 10:54Z, so master is now `6fcc72779122a53bc096969553809d0523aec1be` and carries it. One decision belongs to
the operator: on-demand `gpt-6.2-astra` (`2c673f12`) is **not** on master. Code finished at
10:50:37Z, and Review and land are still owed. It is a feature with a migration. Under the
line rule ("features and refactors are master-only", `docs/release-gates.md:22-26`) it cannot be
cherry-picked onto the new line later. If the operator wants a pool delegate to be able to ask for
`gpt-6.2-astra` in this line, hold the cut for its Review and land. Otherwise it rides the next cut.
The default ladder (High/Medium `gpt-6.2-sol`, Frontier `gpt-6-astra`) is on master either way.

`rc/2026-11-29` is a strict ancestor of master. At `a79ce4086`, master was 223 commits ahead and 0 behind, so the cut
is a plain new ref at a master SHA.

**Line name: `rc/2026-11-30`, not `rc/<UTC date>`.** `rc/2026-11-20` already exists at
`b559bfba2e9a420495122d27171352fe9b7e1ba4`, the 2026-11-10 run 34d41b7e line. The remote has
`rc/2026-11-20` through `rc/2026-11-29` (`git ls-remote`). The precedent is the next unused date
after the newest line. Cut task `fc9a0547` said: "The newest remote line was `rc/2026-11-27`
(e229fb4d3), so the next unused date is `rc/2026-11-28`". The "`rc/<UTC date of the cut>`" wording
in `2026-11-20-codex-0-170-everywhere.md` §4 step 3 would collide.

Activation is a separate matter from the cut. It has two order constraints, set out in §E:

1. The server2 image goes before the AppHost restart (Codex floor).
2. The CARD-1893 server binding goes before the server2 roll. The AppHost reads user-secrets and
   `StandingInstructions` only at startup.

## Table

"Blocks cut" asks only whether the code is missing from master. Activation needs are in the evidence
column and in §E.

| # | Item | State | Evidence | Blocks cut |
|---|---|---|---|---|
| A1 | CARD-0824 slot pool, slot decisions (S1, flag default off) | ON MASTER | `8395c4a43` (2026-11-02). `ResolveSlotDecisionAsync` in `RemoteWorkspaceService` | no |
| A2 | Overflow slots and re-homable slot directories (H-2) | ON MASTER | Same S1 series. `C0824WorkspaceSlotOverflowTests` and `C0824WorkspaceSlotRehomeTests` present (`f32aed42e` runs them Linux-only) | no |
| A3 | Native build output per slot (H-3, CARD-1629 step 1) | ON MASTER | `5ffeac884` (2026-11-02) | no |
| A4 | All-roles enablement | ON MASTER and LIVE on server2 | Code default `WorkspaceSlotRoles = "*"` (`DelegationSettings.cs:749`). It went live 2026-11-17: `2026-11-17-slots-on-path.md` S-3, Code task ab41aa31 `reason=all`. `WorkspaceSlotsEnabled` is restart-only and defaults false in code (`:741`), so it lives in the desktop's untracked `server/appsettings.Development.json`. The value now is UNKNOWN, but leases were working on 11-17 | no |
| A5 | All-roles follow-on slices I1-I3 (inventory and attention) and M1-M3 (Merge lease custody) | MISSING (none dispatched in the task list) | No `C0824WorkspaceSlotInventoryTests`, `C0824MergeSlot*Tests` or `WorkspaceSlotInventoryPollService` on master. Plan: `2026-11-14-card-0824-all-roles-enablement-plan.md:253-258` | no. They gate further widening per that plan, but `*` is already live |
| A6 | S4a automatic desktop legacy release, default on | ON MASTER and LIVE | `3eb9d0027` (2026-11-04). `server/appsettings.json` `WorktreeLegacyRelease.Enabled=true, Execute=true`. The same values are on `rc/2026-11-29` | no |
| A7 | S5b ref-only desktop halves (F1-F4 producer) | ON MASTER, flag default OFF | F-series `ac473d56d`..`e90695c2f` (2026-11-14). `RefOnlyDesktopSettings.Enabled` defaults false with roles `none` (`RefOnlyDesktopSettings.cs:9-12`). Live value UNKNOWN. The card says it stays off until F1-F4 acceptance | no |
| A8 | CARD-1961 warm-reset fix (pool degradation) | ON MASTER and LIVE on server2 | `72d98d936`. The server2 runner `buildVersion` is `e229fb4d3` (`rc/2026-11-27`), which contains it | no |
| A9 | "Slot wave 1, activates at next restart": server2 runner flag | ON MASTER and LIVE | Compose key `PhoneHome__WorkspaceSlots__Enabled` (`docker-compose.server2-runner.yml:65`, default false). The live server2 runner advertises `workspaceSlotV1` | no |
| A10 | "Slot wave 1": `server/appsettings.Development.json` | UNKNOWN | The file is local and not in git (`docs/incident-runbook-server2-dispatch.md:150`). The phrase "prepared, activates at next restart" appears in neither the repo nor the CARD-0824 text. `WorkspaceSlotsEnabled` needs an AppHost restart. `WorkspaceSlotRoles` hot-reloads unless `Delegation__WorkspaceSlotRoles` shadows it | no. It activates at the post-cut AppHost restart |
| A11 | Desktop hot slots S1-S3 (Windows lease behind flags) | ON MASTER, flags default off | S1 census `c84185e9a`, S2 provision `a05fe660a` and `59ca3ef94`, S3 `0dec5150e`..`857883d44`, then `c077b9183` (pin reseal) and `d36e95412` (index), both 11-20. `WindowsSlotRunnerIds=[]` and `WindowsSlotRoles=none` (`DelegationSettings.cs:756-763`) | no |
| A12 | Desktop hot slots S4 and S5 (operator steps) | NOT STARTED, waits on `desktop-win` | Plan `2026-11-19-desktop-hot-worktree-slots-plan.md:442-486`. Step 1 needs `desktop-win` eligible. Live: `desktop-win` status **404** | no |
| A13 | StartRef never waits for a pending sibling land | IN FLIGHT: `dc59c094` Dispatched (Code, server2) | Branch `5ee5e5e7c`, 3 commits ahead (red witness, fix, test), 26 behind. Touches `AgentTaskDispatcher.cs` and `CardSiblingInspectionService.cs` | no. It is a bug fix, so it may be ported to the line after Review |
| B1 | CARD-1744 S1 compose, S2 scan, S3 direct key, S4 supervision and AppHost refusal | ON MASTER (and already in `rc/2026-11-29`) | `3027e64c4`, `ec316e433`, `1fa8f445b`, `e1b42f895`, `ed0d45cb3`. All are ancestors of `26a99f1a7`, so the desktop is already running the S4 guard | no |
| B2 | `restart-apphost.ps1` exit 3 `phone-home-config-present` (phase 2 refusal) | ON MASTER, LIVE | `scripts/restart-apphost.ps1` guard. `C:\Antiphon\runner\phone-home.json` must **not** exist until phase 2 (gap plan §3 step 6) | no |
| B3 | DW-1 desktop-win history off | ON MASTER (not in rc29) | `aab91e4aa`, witness `912a30119`, floor `a4c36047d` | no |
| B4 | DW-2 docker-stack sentences | ON MASTER | `4753c4eef` | no |
| B5 | DW-3 operator secret helper `scripts/desktop-win-phase1-secrets.ps1` | ON MASTER (not in rc29) | `135ce18ad`, rounds `0278ec62e`, `5be178005`, `d744ec272` and the D-1/D-2 live-temp guard. Reviews `54633aec`, `e09c2246`, `84306e5a`, `58087192`, `212806cc` all Succeeded. `scripts/lib/windows-phone-home-env.ps1` is on master | no |
| B6 | DW-4 | UNKNOWN | No `DW-4` in the repo, the task list or a board search (0 cards) | no |
| B7 | Gap plan `c93364585` (desktop-win call-home gap) | ON MASTER | `docs/superpowers/plans/2026-11-19-desktop-win-call-home-gap-plan.md` | no |
| B8 | Operator's DW-3 run and the desktop-win registration | NOT DONE | `/api/session-runners` has no `desktop-win` row, and `/desktop-win/status` is 404 at 10:46Z. Still missing per the plan (§2): the user-secrets map and the secret file (DW-3 `-Execute`), an AppHost restart to load them, the `C:\Antiphon\runner\app` clone at a tip that has DW-1, and the 17214 phase-1 start. Push from a Windows mirror (no deploy key) and CARD-1778, CARD-1751 and CARD-1406 (all Review or InProgress) gate phase 2 and the window, not phase 1 | no. Activation only |
| B9 | graceful-stop fixture repair (14th e7f92f66 failure) | ON MASTER since 10:42Z | `aa536aacc` red, `a79ce4086` fix (task `a4ead677` landed `remoteSha a79ce4086`). Linux pwsh 13/0. The four sibling harnesses moved onto the same copier were not re-run, and Windows was not run | no |
| C1 | Dockerfile `CODEX_VERSION=0.170.0` | ON MASTER | `docker/session-runner-grok/Dockerfile:136-137`, SHA-512 `e9c09925...ce5bb2`. Claude Code `2.1.327` at `:87`. The live server2 runner is `rc/2026-11-27`: Codex `0.160.0` (live `codexCliVersion 0.160.0`), Claude `2.1.280` | no. Needs the server2 refresh |
| C2 | Ladder: High/Medium `gpt-6.2-sol` (floor 0.169.1), Frontier `gpt-6-astra`, `gpt-6.2-astra` recognised (floor 0.169.1) | ON MASTER | `ModelLevelAliases.cs:30-57`, `ModelAlias.cs:21-22,97,180-181`. "Review" has no row of its own: Review runs `-Level Frontier`, which gives `gpt-6-astra` (`docs/agent-kinds.md:230`). The live server (`rc/2026-11-29`) still has `gpt-6.1-sol` | no |
| C3 | Explicit selection `-Model gpt-6.2-astra` (`2c673f12`) | IN FLIGHT: Code **Succeeded 10:50:37Z**, `next=Review`, no land | Branch `3bc8ac66f`, 6 ahead and 55 behind. Includes migration `20261120091515_AddRequestedModelAlias` | **Operator decision.** It is a feature, so the line rule allows no later port |
| C4 | Desktop Codex CLI | 0.170.0 LIVE | `/api/session-runners` desktop `codexCliVersion 0.170.0`, checked 10:43:32Z. This closes the open measurement in `2026-11-20-codex-0-170-everywhere.md` §3 | no |
| D1 | e7f92f66 natives (13 failures, 11 identities, 8 chunks) | ALL ON MASTER | `e36a4efd3` (groups 71, 91), `75fcf995b` (74), `0f2925544` (84), `0861d3e5d` (86), `4cb4545b6`+`3452fc56a` (134), `d26917474` (139), `6d91197c0` (split-8 x3). None is in any rc line | no |
| D2 | e7f92f66 scripts/all graceful-stop | ON MASTER | See B9 | no |
| D3 | CARD-2005 summary writer OOM | ON MASTER, proven by e7f92f66 | `26a99f1a7` (in rc29) plus `04ade42ce` and the `5561cf9e8` reseal on master. e7f92f66 wrote a whole summary.json of 3,674,934 bytes (`2026-11-20-run-e7f92f66-ended.md` §2 #5-6). The card is still in Review | no |
| D4 | CARD-1981 c487 probe leak | ON MASTER | `aee11e330`, `9fc9b6643`, `13ec68f43`. Review `4334d7fa` round 2 Succeeded | no |
| D5 | CARD-1820 selector v2 | ON MASTER including the S3 test-only fix | 70 commits, then `c412d01f` published `remoteSha 6fcc72779122a53bc096969553809d0523aec1be` at 10:54Z (request still `Running` for cleanup) | no |
| D6 | CARD-1836 incremental chain | Shadow and inert on master; more in flight | Libraries are inert (`2026-11-19-card-1836-remaining-chain-status.md`). `3a5b76f5` S6c-prereq C# port: Review `bb05708f` clean, land not requested (`ffa9e26a9`). `ad11c378` S8b is Blocked with land `NeedsResolution`. Every slice publishes `effectiveMode=Full`, `gateCredit=false` | no. The next run is a seeded Full |
| D7 | CARD-2008 shadow Passed gap (A-1) | IN FLIGHT: `f6865b8e` Dispatched, 0 commits | Gates only incremental credit ("before any Incremental cut or any use of a shadow Passed") | no |
| D8 | FT-2 docs-only land skips build (`df40f8cb`) | IN FLIGHT: Dispatched, 1 red-witness commit | `6fbf68815`. Land-path change | no. Rides the next cut |
| D9 | CARD-1893 S1 host-load history binding | ON MASTER (and rc29); not on the live server2 runner | `e839cf8ef`. `bash scripts/c1893-runner-hold.sh a79ce4086...` prints **RELEASED** (run here). Activation needs the server user-secrets `HostLoadHistory:Bindings:0:*` and an AppHost restart **before** the runner roll (activation plan §7.1 step 5). Whether they are written is UNKNOWN | no. Activation order, see §E |
| D10 | Other Review/InProgress cards | No rc-next label exists | 193 Review and 41 InProgress cards. Critical/Now ones not covered above: CARD-0824, 1406, 1717, 1718 (S4a audits), 1729. None names a pre-cut obligation in its title line. 1717 and 1718 are open audits of the S4a deletion path, which is live already | no |

## E. Cut mechanics

The precedents are `2026-11-16-rc-cut-2026-11-24.md` and cut task `fc9a0547` (`rc/2026-11-28`,
desktop, about 9 min). Launch prep for the line is task `6068be37`, note
`docs/investigations/2026-11-19-rc-2026-11-29-launch-commands.md` on `feat/card-task-6068be37`
`2136025ae`. That note is not on master, which is why the codex note called it missing.

**At the cut pin, all of these must hold** (`docs/orchestration-loop.md:737`, `:809`;
`2026-11-16-rc-cut-2026-11-24.md`):

1. `git fetch origin` and pin the full SHA once. `rc/2026-11-30` must not exist.
2. The derived impact index matches. Run `pwsh -NoProfile -File scripts/regenerate-derived-impact-index.ps1`
   (I-5). If it prints `changed`, land that commit on master first and re-pin, then
   `DerivedImpactIndexTests`. Whether `a79ce4086` changed the index is UNKNOWN; it added
   `scripts/lib/apphost-fixture-closure.ps1`.
3. `scripts/audit-land-verification-graph.ps1 -Check` (build slot) returns `approved`.
4. `policyHash` computed equals stored, `PinnedInputDigest` computed equals pinned, and
   `LegacyExclusionDigest` passes.
5. The guard union is green: `TestClassificationPolicyTests*`, `TestClassificationGuardTests*`,
   `DerivedImpactIndexTests*`, `C1729LayeringGuardTests*`, `WindowsUnitPlatformAdmissionTests*`,
   `NativeLinuxPolicyWitnessMutationTests`. That was 100 tests, 3m20s plus a 7m30s build, at the 11-24 cut.
6. **Merge high-radius rows (CARD-1783/1801) on the cut pin, all 0 failed, before the push.** Take the
   filters from
   `pwsh -NoProfile -File scripts/impacted-classes.ps1 -MergeSubset -Range 26a99f1a747d94120aeff8f0d6b9a2e7c7d4e248..<pin> -Json`.
   The fixed rows and their table estimates are:

   | Row | Estimate |
   |---|---|
   | `CP-merge-subset-tests` | 44 tests, 5 min |
   | `-land-1` | 34 tests, 5 min |
   | `-land-2` | 104 tests, 10 min |
   | `CP-merge-subset-ReleaseGatePlatformTests` | 2 tests, 3 min |
   | `CP-merge-subset-e2e` `ContractSnapshotTests` | 5 tests, 10 min |
   | `CP-merge-subset-messaging` | 1 test, 5 min |

   That is about 38 min of rows, plus three builds (Antiphon.Tests, E2E, Messaging), plus any rows
   the 223-commit range selects. They are serial, through the checkpoint tool or the build slot.
   The e2e row runs on server2. Any `windowsFilters` entry is owed to the Windows Full, not passed.
   The selected extra rows are UNKNOWN until the selector runs.

**Commands** (the 11-24/11-28/11-29 shape; the operator's 2026-11-20 sentence covers the new-ref push):

```powershell
git fetch origin
git ls-remote origin refs/heads/rc/2026-11-30          # must print nothing
git push origin <pin-sha>:refs/heads/rc/2026-11-30      # plain new ref, never --force
git ls-remote origin refs/heads/rc/2026-11-30           # must equal <pin-sha>
pwsh -NoProfile -File scripts/release-candidate.ps1 -Ref rc/2026-11-30 -CheckoutRoot C:\Antiphon\releases\checkout-<task> -WhatIf
pwsh -NoProfile -File scripts/release-candidate.ps1 -Ref rc/2026-11-30 -CheckoutRoot C:\Antiphon\releases\checkout-<task>
git -C C:\Antiphon\releases\checkout-<task> config --local core.longpaths true
```

The candidate id is `rc-2026-11-30-<pin sha12>`. `release-candidate.ps1` journals and clones. It does
not push, test or publish (`scripts/release-candidate.ps1:3-16`). Write the `.antiphon-nightly-owned`
marker by hand, as `fc9a0547` and `6068be37` did. Do not use `release-cut.ps1` as the launcher.

### Post-cut sequence

The order is the one the brief asks for, corrected for startup-only configuration:

0. **Pre-restart configuration on the desktop, before any AppHost restart**, so one restart loads
   all of it. All of these are read only at process start:
   - The operator runs `pwsh -NoProfile -File scripts/desktop-win-phase1-secrets.ps1` (dry run), then
     `... -Execute`. This is DW-3; it writes the `desktop-win` map and the secret file and prints no value.
   - The orchestrator writes the `Delegation:OrchestratorInstructions:StandingInstructions` line
     (`docs/orchestration-loop.md:797`: startup `IOptions`; refresh only re-renders). The live section
     8 has 2 lines. The new line's text is not in the repo (UNKNOWN).
   - CARD-1893 `HostLoadHistory:Bindings:0:*` user-secrets (activation plan §5, §7.1 step 5).
1. **Restart the AppHost at the current tip `26a99f1a7`** (`-ExpectedServerSha 26a99f1a747d94120aeff8f0d6b9a2e7c7d4e248`).
   This loads step 0 and keeps the `gpt-6.1-sol` ladder, so server2 at Codex 0.160.0 stays valid.
   The binding settings and service are byte-identical between rc29 and master. Afterwards check:
   - `GET /api/session-runners` lists `desktop-win` (200, `available:false` is fine).
   - `GET /api/hosts/server2/load` reports `physicalHostKey=server2`.

   If the operator prefers fewer restarts and skips the CARD-1893 binding this round, drop this
   step and do step 0 before step 4 instead. The runner then rolls with a history path that the
   server does not bind, and `binding-unknown` continues.
2. **server2 runner refresh from `rc/2026-11-30`**, as in `2026-11-20-codex-0-170-everywhere.md` §2.
   Run `bash scripts/c1893-runner-hold.sh <pin>` (must print `RELEASED`; it does at `a79ce4086`),
   then `check-host-jq`, `deploy-temp`, smoke the temp as uid 1654 for `codex-cli 0.170.0`,
   `drain-old -WaitIdleMinutes 240`, `redeploy-old`, smoke, `verify-card0849-caches.ps1 -Case Both`,
   `drain-temp`, then `retire-temp` preview and apply. Never `-Phase all`, never `-RecycleVolumes`.
   This one interruption also rolls Claude 2.1.327 and the CARD-1893 S1 compose.
3. **Desktop Codex**: already 0.170.0 (C4). No desktop runner restart is needed for the CLI.
4. **AppHost restart onto the new rc**:
   `git -C C:\src\Antiphon checkout --detach <pin>` after a clean status (the `6068be37` step 1 shape),
   then `scripts/restart-apphost.ps1 -ExpectedServerSha <pin>`. Exit 0 only; 3 is a refusal, 5 a SHA
   mismatch. Do not create `C:\Antiphon\runner\phone-home.json`, or this step refuses with exit 3.
   This activates the `gpt-6.2-sol` ladder, `WorkspaceSlotsEnabled` from
   `appsettings.Development.json`, and anything else that is startup-only.
5. **Canary** `-Kind Codex -Level High -Runner server2`: the transcript must show
   `--model gpt-6.2-sol` and `model_reasoning_effort=high`.
6. **Seeded Windows Full** of `rc/2026-11-30` on the desktop direct runner, never `desktop-win`.
   Follow the `6068be37` steps 3-8: release checkout, launch set, cold slot reset, both preflights GO,
   `Win32_Process` start, then watchers. Seed flags:
   `-PriorFailuresPath C:\Users\lndco\antiphon-seeds\e7f92f66.json -PriorFailuresDigest a489a7e210401b003fc43f13c39fba63fca2c58655e17932d92b0a08f8c92a5c -AcceptCrossCandidateSeed -SeedSourceCandidateId rc-2026-11-29-26a99f1a747d`.
   The seed is valid until 2026-12-04T07:33Z. It has 13 open identities and 1 proven. The two
   `ScriptModelSpawnLaneTests` probes stay open by design (§3 of the run note).
7. **desktop-win phase 1** (operator): clone `C:\Antiphon\runner\app` at a DW-1 tip, write
   `phone-home.phase1.json`, start on 17214 (gap plan §3 steps 6-9). This is independent of the
   Full run. The first pinned task is the push witness.

## F. Blockers and nice-to-haves

Blockers to the cut: **none in code.** Gates, each with its reason:

- `2c673f12` gpt-6.2-astra on demand. **Operator decision.** It is a feature, so the line rule allows
  no rc port. Hold only if it is wanted in this line. That costs Review, possible fix rounds, and a
  land with a migration.
- The cut-pin checks in §E (index regeneration, graph check, digests, guard union, high-radius
  rows). These are steps of the cut, not missing work. A red row stops the push.

Nice to have, can ride the next cut or a reviewed port:

- `dc59c094` StartRef (bug fix, portable).
- `df40f8cb` FT-2 (next cut).
- `f6865b8e` CARD-2008 and `3a5b76f5`/`ad11c378` CARD-1836 (shadow only, next cut).
- CARD-0824 I1-I3 and M1-M3 (not started).
- Desktop slots S4/S5 (after `desktop-win`).

## Unknown

- The live `server/appsettings.Development.json` values (`WorkspaceSlotsEnabled`, `RefOnlyDesktop`,
  `StandingInstructions`) and whether the CARD-1893 bindings are written.
- Whether a Windows qualification is running now. e7f92f66 ended 07:33Z. No later launch task was
  found, and `native-run.lock` is not readable from server2.
- Whether `DerivedImpactIndexTests` is green at `a79ce4086`, and which extra high-radius rows the
  223-commit range selects.
- DW-4: no definition found.
- The live slot ledger and its open quarantine count on server2.

## Not done, noted

Fix idea: correct `2026-11-20-codex-0-170-everywhere.md` §4 step 3 to "next unused rc date"
(`rc/2026-11-30`).
