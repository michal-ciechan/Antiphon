# Unlanded landing queue (2026-11-11)

Read-only queue of the 49 CODE-UNLANDED cards from the 2026-11-10 census (`origin/feat/card-task-5bbb6ecd`, `docs/investigations/2026-11-10-card-census-review-inprogress.md`). No card was moved, closed, or edited. Nothing was merged or pushed to master or `rc/*`.

Master used for every cherry and merge-tree: `bc71a95e7710532a9a68416f1d7b9e158fe96a15` (2026-11-11T01:46:16Z).

CARD-1514 is not in this set. The census left it IN-FLIGHT, and this queue does not include it. None of the 49 descriptions call the card an umbrella. CARD-0856 was called the test-speed umbrella in the 2026-10-08 backlog triage; it stays in the table as NEEDS-REVIEW and is not a land subject, because the tip's unique commits are other cards' rc backports.

A commit counts as still unlanded when `git cherry -v origin/master <tip>` marks it `+` (patch-id not on master). A branch whose every commit is `-` is STALE-ABANDON: the patch is already on master even when the tip SHA is not an ancestor and a raw merge-tree still conflicts. Merge-tree is `git merge-tree --write-tree --name-only origin/master <tip>` and was not followed by a merge. Review is the latest active `Review` stage-outcome (superseded rows removed). LAND-NOW requires outcome Clean, commissioned round Final, ordinary scope Full, `reviewedSourceClean` true, reviewed SHA equal to the tip, and a clean merge-tree (or conflicts only under `docs/cards/`, `client/dist/`, or `*.g.cs`). Anything short of that is NEEDS-REVIEW. No card text says parked.

## Counts

| Verdict | Cards |
|---|---:|
| LAND-NOW | 1 |
| NEEDS-REBASE | 1 |
| NEEDS-REVIEW | 29 |
| PARKED | 0 |
| STALE-ABANDON | 18 |

Six census cards have since moved to Done (CARD-0569, CARD-0608, CARD-1222, CARD-1225, CARD-1293, CARD-1300). Five of those are STALE-ABANDON. CARD-0608 is NEEDS-REBASE because two commits on the reviewed tip still have no matching patch and the merge conflicts on the live-lane pin script.

The only LAND-NOW subject is CARD-1230. CARD-1581 is the nearest product change that already merges clean and still needs a new review: its latest review is Found, and that reviewed SHA is not the current tip.

## Ranked table

LAND-NOW first, then NEEDS-REBASE, NEEDS-REVIEW, STALE-ABANDON. Inside a verdict: importance, urgency, risk of the commits that are still unique (or of the already-patched files when nothing is unique), then older tip first.

| card | title | branch tip | review | conflicts | risk | verdict | next action |
|---|---|---|---|---|---|---|---|
| CARD-1230 | C1017_TerminalTaskRequired fails deterministically on master (cleanup n... | `feat/card-task-5a3d03f6` `4bf2d2e6ed38` | Clean Final/Full clean=true @tip at-tip | 0 | test-only | LAND-NOW | Land the tip. Test-only fixture repair, merge-tree clean, Clean Final/Full at the tip. |
| CARD-0608 | Harden PosixProcessSpawner.CloseStdioAliases: restrict match to pipe:/s... | `feat/card-task-3838725c` `3afd9f02be76` | Clean Final/Full clean=true @tip at-tip | 1 exit 1 | test-only | NEEDS-REBASE | Do not merge. Card is Done and the fix is on master; the tip still conflicts on scripts/test-live-lane-run.ps1 and touches the live-lane pin. Rebase only if the leftover witness is still wanted, alone. |
| CARD-0552 | Make Mutation a properly tracked pipeline stage, not an ad-hoc post-lan... | `feat/card-task-e67555ad` `b2353ff6d1b4` | none | 24 exit 1 | migration | NEEDS-REVIEW | No Review outcome on the card. Review the unique commits before any land. |
| CARD-1833 | Human review questions: the operator decision queue | `feat/card-task-236f16eb` `a26cbca907c1` | Clean Final/Full clean=false 585b6c1956e6 not-on-tip | 0 | script/docs-only | NEEDS-REVIEW | reviewedSourceClean is not true, so the land is not certified. |
| CARD-1517 | Claude refinement needs one conditional Ctrl+C without killing the sess... | `feat/card-task-38c4e51a` `7c59b5619e22` | none | 2 exit 1 | test-only | NEEDS-REVIEW | No Review outcome on the card. Review the unique commits before any land. |
| CARD-1589 | Cache preflight F-9 rejects supported absent offline temp deployment ho... | `feat/card-task-c634b0a5` `6783c9427ebd` | none | 52 exit 1 | test-only | NEEDS-REVIEW | Do not land this tip. Its unique commits are a shared chain and this card has no Review row. |
| CARD-1820 | Selector v2: derived reverse-reference index, keyword triggers, roster ... | `feat/card-task-6fde97f6` `238af3ff3e30` | Clean Final/Full clean=true cb931f089012 not-on-tip | 7 exit 1 | test-only | NEEDS-REVIEW | Do not merge this tip. Master already has a later CARD-1820 series; the one unique commit add/add-conflicts with it. Diff against master before any re-review. |
| CARD-0511 | Restart loop: SessionRunner/Server capability desync after sessionGener... | `feat/card-task-f1139c44` `63cab35e78d7` | Found Final/Unknown clean=null sha unbound | 7 exit 1 | migration | NEEDS-REVIEW | Latest review is Found (Code's "restart: none" claim is wrong: all changes are server-side and inert until restart-apphost.ps1 runs from the canonical checkout, wh...). Not accepted as fixed: no later Clean review. |
| CARD-1708 | S4a release can destroy reflog-only commits (decision needed) | `feat/card-task-f822e84d` `c9d9d7a631d9` | Found Final/Interim clean=false f45832647421 not-on-tip | 13 exit 1 | product code | NEEDS-REVIEW | Latest review is Found (V-1: inherited failures and deferred verification keep the required gate incomplete. commits=0). Not accepted as fixed: no later Clean review. |
| CARD-1731 | Publication ledger tests (ExecutionLedger, Joins, Markers) sit at their... | `feat/card-task-b8770621` `11cdf6ecbeb4` | Clean Final/Full clean=true cf0aba324bdf not-on-tip | 0 | script/docs-only | NEEDS-REVIEW | The Clean review is not of this tip. Re-review the tip before land. |
| CARD-1733 | ReleaseTagAllocator reservation matrices exhaust 300s; full affected-cl... | `feat/card-task-8f43dbed` `803291f3cd4c` | none | 0 | script/docs-only | NEEDS-REVIEW | No Review outcome on the card. Review the unique commits before any land. |
| CARD-0669 | Land residue accumulates: source-recheck refs, refs/antiphon/land pins,... | `feat/card-task-29f3946d` `c0abb4faca41` | none | 0 | script/docs-only | NEEDS-REVIEW | No Review outcome on the card. Review the unique commits before any land. |
| CARD-1347 | Scheduled dry-run-first sweep of old Codex pool-session history on serv... | `feat/card-task-e59b0f15` `3a2e2ce2168e` | none | 0 | script/docs-only | NEEDS-REVIEW | No Review outcome on the card. Review the unique commits before any land. |
| CARD-1636 | Non-custody adoption does not verify the host session id (local exposur... | `feat/card-task-08469fcb` `7bd2f112e377` | Clean Final/Unknown clean=null sha unbound | 3 exit 1 | script/docs-only | NEEDS-REVIEW | reviewedSourceClean is not true, so the land is not certified. |
| CARD-1690 | Client test performance: slow Mantine tests, RoutingSettingsTab 250 s, ... | `feat/card-task-dd90f1b7` `2e7023b4280f` | Clean Final/Unknown clean=null sha unbound | 3 exit 1 | script/docs-only | NEEDS-REVIEW | reviewedSourceClean is not true, so the land is not certified. |
| CARD-1726 | Release-candidate red 2026-11-09: Antiphon.Tests exit 8, Antiphon.Tests... | `feat/card-task-d8f70e4c` `1d15909de96d` | Clean Final/Interim clean=false 13e9c1c8b019 not-on-tip | 0 | script/docs-only | NEEDS-REVIEW | Review scope is Interim, which cannot approve a land. |
| CARD-1466 | Inherited census reds: TestSchemaSnapshotTests registry, shape_conflict... | `feat/card-task-1f10d17d` `2ce36496456b` | none | 0 | script/docs-only | NEEDS-REVIEW | No Review outcome on the card. Review the unique commits before any land. |
| CARD-1222 | CARD-0038 split: Linux OS zombie census throws (WindowsZombieProcessCen... | `feat/card-task-81fc0b3c` `8ab57f1eac7d` | Found Final/Full clean=false @tip at-tip | 1 exit 1 | script/docs-only | NEEDS-REVIEW | Latest review is Found (CP-1's owed Windows skip makes the required receipt row_failed, so reviewedSourceClean stays false. commits=1). Not accepted as fixed: no later Clean review. |
| CARD-1030 | C1008 host-fixture tests fail on Windows: bash launcher path, git init ... | `feat/card-task-7ee266d3` `4a7cbc1413fa` | none | 3 exit 1 | test-only | NEEDS-REVIEW | No Review outcome on the card. Review the unique commits before any land. |
| CARD-1334 | GrokDelegateEndToEndTests harness registers SessionRuntimeActionCoordin... | `feat/card-task-e3bcb4b8` `f1ba0f8e6c5a` | none | 1 exit 1 | test-only | NEEDS-REVIEW | No Review outcome on the card. Review the unique commits before any land. |
| CARD-1573 | Checkpoint output-inventory reader refuses Antiphon.Tests.dll above 64 ... | `feat/card-task-56ddd55e` `39a7f70c4642` | Found Final/Interim clean=false @tip at-tip | 85 exit 1 | test-only | NEEDS-REVIEW | Latest review is Found (D-1: new C1573 Unit test calls stat -c with no $IsLinux gate, so it is red by construction on Windows; D1/D2 repairs verified on Linux with...). Not accepted as fixed: no later Clean review. |
| CARD-1597 | Qualification timing recorder 256 KiB cap refuses most real chunks | `feat/card-task-34d54a5e` `b71d9b317957` | Clean Final/Full clean=true 17cb417c3b3d not-on-tip | 83 exit 1 | test-only | NEEDS-REVIEW | The Clean review is not of this tip. Re-review the tip before land. |
| CARD-1596 | Older path-based deletes (SafeDelete, ToolCopyAuthority, TempRootSweep)... | `feat/card-task-d1e5f56b` `5c72a5daafbb` | Clean Final/Interim clean=false 3a0d8c9b006a not-on-tip | 85 exit 1 | test-only | NEEDS-REVIEW | Review scope is Interim, which cannot approve a land. |
| CARD-1598 | Timing reader cost still grows with journals in the shared folder (abou... | `feat/card-task-06fc33df` `f218d2eaa067` | Clean Final/Unknown clean=null sha unbound | 103 exit 1 | test-only | NEEDS-REVIEW | reviewedSourceClean is not true, so the land is not certified. |
| CARD-1599 | Checkpoint receipt reader read-stalled 3 s window fails on a loaded host | `feat/card-task-db7d5ba3` `112c6d3b016d` | Found Final/Unknown clean=null sha unbound | 100 exit 1 | test-only | NEEDS-REVIEW | Latest review is Found (6 s window only moves the starvation threshold (probe: 82/200 healthy reads refused at 6 s); advisory follow-up for a pool-independent Linu...). Not accepted as fixed: no later Clean review. |
| CARD-1601 | Receipt reader stall window measures thread-pool starvation (sync-over-... | `feat/card-task-e9436c0d` `956393340834` | Found Final/Unknown clean=null sha unbound | 100 exit 1 | test-only | NEEDS-REVIEW | Latest review is Found (Stale "six-second window" text at docs/testing-and-build.md:1591 (advisory, docs only); no regression and nothing fails open. commits=1). Not accepted as fixed: no later Clean review. |
| CARD-1615 | Rollout Deploy task (Shared workspace) holds bound lands as a source wr... | `feat/card-task-b34eae94` `f9bf72bd500b` | Found Final/None clean=false 0d442c194be8 not-on-tip | 131 exit 1 | test-only | NEEDS-REVIEW | Latest review is Found (Four plan defects: the watchdog can't recover AppHost under a live marker, the marker lapses at every phase handover and lets a new rollout...). Not accepted as fixed: no later Clean review. |
| CARD-1618 | Make the 35 Windows-skipped Checkpoint rows run and gate on Windows | `feat/card-task-dd567e5f` `823c5526425d` | none | 150 exit 1 | test-only | NEEDS-REVIEW | Do not land this tip. Its unique commits are a shared chain and this card has no Review row. |
| CARD-0856 | Full Antiphon.Tests run is 270 min on Windows: effective parallelism 1.... | `feat/card-task-242906fa` `fed245d3145f` | none | 50 exit 1 | product code | NEEDS-REVIEW | No Review outcome on the card. Review the unique commits before any land. |
| CARD-1581 | Same-card dispatch waiting for a land holds the repository lease; the l... | `feat/card-task-edc7b76c` `136083c7a181` | Found Final/Interim clean=false 894b44369d87 not-on-tip | 0 | product code | NEEDS-REVIEW | Merge-tree is clean, but the latest review is Found and its SHA is not this tip. Re-review 136083c7 before land. |
| CARD-1704 | LibGit2Sharp git abstraction with a shared repository for the whole land | `feat/card-task-b578f67a` `9b8c5cb06bb6` | Found Final/Unknown clean=null sha unbound | 5 exit 1 | startup/safety | NEEDS-REVIEW | Latest review is Found (D-1: Landing:GitBackend reports Cli/"LibGit2 not built" while the operator's libgit2 reads are live by default; Track L per-scope session c...). Not accepted as fixed: no later Clean review. |
| CARD-0931 | Flaky-fix plan CP-3/CP-15 method-OR checkpoint filters select zero test... | `feat/card-task-4963339c` `c2420e0336c7` | none | 0 | none unique | STALE-ABANDON | Do not land. git cherry shows no patch that origin/master lacks. |
| CARD-1351 | Output-distiller prompt review: contract v5 checks length first and sto... | `feat/card-task-ab4e2125` `34083e79c02b` | Clean Final/Unknown clean=null sha unbound | 0 | none unique | STALE-ABANDON | Do not land. git cherry shows no patch that origin/master lacks. |
| CARD-1532 | Checkpoint source stamps reject build DLLs above the 64 MiB receipt lim... | `feat/card-task-12dd82bc` `7235182cb61e` | Clean Final/Full clean=true 7932e60205a3 not-on-tip | 6 exit 1 | script/docs-only (already patched) | STALE-ABANDON | Do not land. git cherry shows no patch that origin/master lacks. |
| CARD-1553 | Repair C1115 tests whose nested lease-busy assumption changed with CARD... | `feat/card-task-a5f9c44c` `8c7765eb7674` | none | 0 | none unique | STALE-ABANDON | Do not land. git cherry shows no patch that origin/master lacks. |
| CARD-1548 | Update action-owning retention and purge fixtures for CARD-1259 depende... | `feat/card-task-3fa840bf` `68be5426098f` | none | 1 exit 1 | none unique | STALE-ABANDON | Do not land. git cherry shows no patch that origin/master lacks. |
| CARD-1040 | Unit timeouts/inherited failures: resolve CARD-1021 jq preconditions an... | `feat/card-task-d20ed3ac` `11d9121e3899` | Clean Final/Full clean=false @tip at-tip | 1 exit 1 | script/docs-only (already patched) | STALE-ABANDON | Do not land. git cherry shows no patch that origin/master lacks. |
| CARD-1510 | Windows PATH probe test rejects the PowerShell launcher alias prefix | `feat/card-task-5019ea31` `705662142c95` | Clean Final/Full clean=true @tip at-tip | 0 | script/docs-only (already patched) | STALE-ABANDON | Do not land. git cherry shows no patch that origin/master lacks. |
| CARD-0972 | CARD-0954/0955 pending: Windows native half-reset row and SourceLanding... | `feat/card-task-c2afd605` `4117d4846703` | none | 0 | none unique | STALE-ABANDON | Do not land. git cherry shows no patch that origin/master lacks. |
| CARD-1368 | Checkpoint cleanup refuses runtime-created TestResults under the row ou... | `feat/card-task-30a13032` `359cc912235d` | Clean Final/Full clean=true @tip at-tip | 10 exit 1 | script/docs-only (already patched) | STALE-ABANDON | Do not land. git cherry shows no patch that origin/master lacks. |
| CARD-1376 | ChromiumRuntimeSmokeTests: thread the TUnit cancellation token into Pla... | `feat/card-task-5ec48582` `9b97b7aebae4` | Clean Final/Full clean=true @tip at-tip | 1 exit 1 | none unique | STALE-ABANDON | Do not land. git cherry shows no patch that origin/master lacks. |
| CARD-1528 | CARD-1483 follow-up: canary misses System.IO.File.Exists property funct... | `feat/card-task-2d02464e` `1a30f3bebf5e` | Clean Final/Full clean=true 33f51ccb37a8 not-on-tip | 8 exit 1 | script/docs-only (already patched) | STALE-ABANDON | Do not land. git cherry shows no patch that origin/master lacks. |
| CARD-1639 | CARD-1380 successor: equal-tree receipt land skip and lease doc drift | `feat/card-task-48e15e43` `b032722199a0` | Clean Final/Full clean=true @tip at-tip | 0 | none unique | STALE-ABANDON | Do not land. git cherry shows no patch that origin/master lacks. |
| CARD-1715 | status_untrusted is opaque and invisible; two land sites mislabel it | `feat/card-task-420b5c90` `e64c5cd801c7` | Clean Final/Interim clean=false @tip at-tip | 4 exit 1 | none unique | STALE-ABANDON | Do not land. git cherry shows no patch that origin/master lacks. |
| CARD-1728 | Debug Unit shard completion, coverage indexing cost and stop receipt | `feat/card-task-b1e4f841` `0e841541e11d` | Clean Final/Full clean=true 5acde1e2b7ad not-on-tip | 0 | script/docs-only (already patched) | STALE-ABANDON | Do not land. git cherry shows no patch that origin/master lacks. |
| CARD-0569 | Clean up remaining stale sequential-override wording (antiphon-api.md, ... | `feat/card-task-f0176553` `bca2fa8c4bba` | Clean Final/Full clean=true @tip at-tip | 0 | product code (already patched) | STALE-ABANDON | Do not land. git cherry shows no patch that origin/master lacks. |
| CARD-1293 | B-2 delay projection clearing, test coverage gaps, CP-24 label clash | `feat/card-task-d953dc40` `427b7b9fe6fb` | Clean Final/Full clean=false @tip at-tip | 0 | product code (already patched) | STALE-ABANDON | Do not land. git cherry shows no patch that origin/master lacks. |
| CARD-1300 | CARD-1289 follow-up: refusal order for closed cards, untested compared ... | `feat/card-task-08ee53e0` `822d2de21c7c` | Clean Final/Full clean=true @tip at-tip | 0 | product code (already patched) | STALE-ABANDON | Do not land. git cherry shows no patch that origin/master lacks. |
| CARD-1225 | CARD-0038 split: orchestrator deny hook names powershell but the Linux ... | `feat/card-task-4b46ef18` `bcbc01f23e29` | Clean Final/Full clean=true @tip at-tip | 0 | product code (already patched) | STALE-ABANDON | Do not land. git cherry shows no patch that origin/master lacks. |

## LAND-NOW batches

One batch. There are not 4 to 6 LAND-NOW subjects. No file overlap inside the batch. None of these tips change `tests/test-execution-policy.json` or the protected live-lane inputs (`scripts/lib/nightly-common.ps1`, `scripts/lib/nightly-policy.ps1`, `scripts/lib/nightly-coverage.ps1`, `scripts/lib/nightly-core.ps1`, `scripts/fixtures/nightly/c487-probe/discovery.diag`).

| Batch | Cards | Why together |
|---|---|---|
| 1 | CARD-1230 | The only Clean Final/Full tip that merge-tree accepts. Test and plan files only. |

CARD-0608 would be a pin batch of its own if it is rebased. It changes `tests/test-execution-policy.json` and conflicts in `scripts/test-live-lane-run.ps1`. It is not in the LAND-NOW list.

## Per card

### CARD-1230 LAND-NOW

Review, Normal/Normal. C1017_TerminalTaskRequired fails deterministically on master (cleanup not clean after task reset to Succeeded)

Branch `feat/card-task-5a3d03f6` tip `4bf2d2e6ed385132b442d445afb67fc3d9256c80` (2026-10-15). Merge-base `78e4001788e9`. Cherry `+4` `-0`. Chain: no shared unique commit with the other 48. Master commits since the merge-base: 5530. Of those, 7 touch the three-dot paths (latest 2026-11-06).

Unique commits:

- `35aaeed6f364` test(CARD-1230): C1017_TerminalTaskRequired names the residue and the prior state when the reset cleanup is not clean
- `b509cb8c013a` test(CARD-1230): red first - the C1017 fixture must name a root under the OS temp directory
- `61d6609b8ea2` fix(CARD-1230): C1017 fixture refuses a root under the OS temp directory by name; line-105 message carries the report store's reason
- `4bf2d2e6ed38` docs(CARD-1230): plan, cause and checkpoints; CARD-1213 CP-4 selects its three classes whole again

Unique paths (4): tests/Antiphon.Tests/Application/CardDoneWorktreeCleanupTests.cs, tests/Antiphon.Tests/TestHelpers/CompletedCardCleanupFixture.cs, docs/superpowers/plans/2026-10-13-card-1213-test-design.md, docs/superpowers/plans/2026-10-15-card-1230-c1017-temp-checkout.md

Review 2026-10-15T07:08Z: Clean, round Final, scope Full, reviewedSourceClean true, SHA 4bf2d2e6ed385132b442d445afb67fc3d9256c80 (at-tip). Detail: commits=0

Merge-tree exit 0, 0 conflict path(s).

Risk of what is still unique: test-only.

Next: Land the tip. Test-only fixture repair, merge-tree clean, Clean Final/Full at the tip.

### CARD-0608 NEEDS-REBASE

Done, Low/Normal. Harden PosixProcessSpawner.CloseStdioAliases: restrict match to pipe:/socket: identities

Branch `feat/card-task-3838725c` tip `3afd9f02be7686fc15d2062a1276eda2fd54b241` (2026-11-10). Merge-base `a69789c77865`. Cherry `+2` `-3`. Chain: no shared unique commit with the other 48. Master commits since the merge-base: 84. Of those, 6 touch the three-dot paths (latest 2026-11-10).

Unique commits:

- `0cc7e2bfc300` test(CARD-0608): red witness for file-path stdio alias closes
- `3afd9f02be76` docs(CARD-0608): record the code checkpoint report

Unique paths (4): scripts/test-live-lane-run.ps1, tests/Antiphon.PtyHost.Tests/PosixProcessSpawnerStdioAliasTests.cs, tests/test-execution-policy.json, ...

Review 2026-11-10T20:53Z: Clean, round Final, scope Full, reviewedSourceClean true, SHA 3afd9f02be7686fc15d2062a1276eda2fd54b241 (at-tip). Detail: commits=1

Merge-tree exit 1, 1 conflict path(s). scripts/test-live-lane-run.ps1

Risk of what is still unique: test-only. Pin-moving: tests/test-execution-policy.json.

Master subjects naming the card: `63588deef fix(merge): recompute the live-lane pin on the CARD-0608 tree`; `1d2779f99 plan(CARD-0608): correct the guard floor and add the lane row`; `9dec8f9aa test(CARD-0608): name CloseStdioAliases in the witness`; `b70519714 fix(CARD-0608): close only pipe and socket stdio aliases`.

Next: Do not merge. Card is Done and the fix is on master; the tip still conflicts on scripts/test-live-lane-run.ps1 and touches the live-lane pin. Rebase only if the leftover witness is still wanted, alone.

### CARD-0552 NEEDS-REVIEW

Review, High/Now. Make Mutation a properly tracked pipeline stage, not an ad-hoc post-land dispatch

Branch `feat/card-task-e67555ad` tip `b2353ff6d1b4dd7b34f39f8b134ca01efc59a113` (2026-09-18). Merge-base `77e080f3a6c9`. Cherry `+13` `-0`. Chain: no shared unique commit with the other 48. Master commits since the merge-base: 10448. Of those, 749 touch the three-dot paths (latest 2026-11-11).

Unique commits:

- `6024e4d97b10` docs(CARD-0552): test-design - Verification design appended to the plan: 72 guards mapped 1:1 to 72 method-scoped PCs across land hook (LandingSafetyHarness), ...
- `9582ffaa4553` feat(CARD-0552): S1-S3 - AgentTaskLanding.VerificationCardId FK+index+migration, PostLandVerificationCompanions writer (create/link-by-owner-FK/link-by-key/sup...
- `7a912b8a9dea` feat(CARD-0552): S4,S6,S7 - verification-companion endpoint (200 created/linked, three 409 codes, owner row lock), MutationAutoDispatch settings+validator+wind...
- `b32618d61ebc` test(CARD-0552): unit lane green - C552_U01-03 writer Describe, C552_P01-07 shared projection, C552_C01-04 settings/validator, C552_B01-03 goal composer, C552_...
- `8451c343396d` test(CARD-0552): writer integration (C552_W01-W10, 12/12 green) and land-hook class C552_L01-L11 authored; op fixture uses rebase shape so C differs from L und...
- `f5412872a0f2` test(CARD-0552): land hook 16/17 - C552_L01-L11 green except L05 CardChanged payload round-trip (fixed: case-insensitive JSON); writer renders the persisted-pr...
- `2c483e0d765c` test(CARD-0552): projection integration green - C552_Q01-Q11 plus M-4 seeds (SeedDebtAsync/SeedCardOnBoardAsync/SeedConfirmedLandingAsync/SeedSourcedTaskAsync)...
- `9e6dc1b2cbe4` test(CARD-0552): C552_Q12 route JSON green and leaves the shared factory host as found; AgentTaskPipelineEndpointTests 4/4
- ... 5 more unique commits

Unique paths (59): docs/superpowers/plans/2026-09-17-card-0552-mutation-tracked-stage-plan.md, server/Application/Dtos/AgentTaskPipelineDtos.cs, server/Application/Dtos/LandingEvidenceDto.cs, server/Application/Services/AgentTaskLandService.cs, server/Application/Services/AgentTaskPipelineStatusService.cs, server/Application/Services/MutationDebtProjection.cs, server/Application/Services/PostLandVerificationCompanions.cs, server/Domain/Entities/AgentTaskLanding.cs, server/Infrastructure/Data/AppDbContext.cs, server/Migrations/20260917213042_AddLandingVerificationCard.Designer.cs, server/Migrations/20260917213042_AddLandingVerificationCard.cs, server/Migrations/AppDbContextModelSnapshot.cs, server/Program.cs, .claude/skills/antiphon-delegate/SKILL.md, client/src/api/agentTasks.ts, client/src/api/attention.ts, client/src/features/attention/attentionVisuals.ts, docs/agent-card-lifecycle.md, docs/antiphon-api.md, docs/ops-http.md, docs/orchestration-loop.md, server/Api/Endpoints/AgentTaskEndpoints.cs, server/Application/Dtos/AttentionDtos.cs, server/Application/Services/AttentionService.cs, ...

Review: no active Review stage-outcome.

Merge-tree exit 1, 24 conflict path(s). client/src/api/agentTasks.ts, client/src/api/attention.ts, client/src/features/attention/attentionVisuals.test.ts, client/src/features/attention/attentionVisuals.ts, docs/antiphon-api.md, server/Application/Dtos/AgentTaskPipelineDtos.cs, server/Application/Dtos/AttentionDtos.cs, server/Application/Dtos/LandingEvidenceDto.cs, server/Application/Services/AgentTaskLandService.cs, server/Application/Services/AgentTaskPipelineStatusService.cs, server/Application/Settings/DelegationSettings.cs, server/Bundles/README.md, server/Bundles/orchestrator.md, server/Domain/Entities/AgentTaskLanding.cs, tests/Antiphon.Tests/Application/AgentTaskLandApprovalRequestTests.cs, tests/Antiphon.Tests/Application/AgentTaskLandRequestTests.cs, tests/Antiphon.Tests/Application/AgentTaskLandStageOutcomeTests.cs, tests/Antiphon.Tests/Application/AgentTaskLandSweepTests.cs, tests/Antiphon.Tests/Application/AgentTaskPipelineStatusTests.cs, tests/Antiphon.Tests/Application/DelegationWorktreeTests.cs, tests/Antiphon.Tests/TestHelpers/C544World.cs, tests/Antiphon.Tests/TestHelpers/LandingProtocolHarness.cs, tests/Antiphon.Tests/TestHelpers/LandingSafetyHarness.cs, tests/Antiphon.Tests/TestHelpers/ProductionRunnerGuard.cs

Risk of what is still unique: migration. server/Migrations/20260917213042_AddLandingVerificationCard.cs: additive (Up AddColumn/CreateIndex/AddForeignKey; Down drops 1). The same unique commits also edit server/Program.cs.

Master subjects naming the card: `7fc575c96 docs(CARD-0552): plan amended for operator refinement - Mutation debt is worked unattended: MutationAutoDispatchSweep (Diagnose-sweep shape) creates one sourced Mutation task per tick from the oldest ready row under pause/WIP=1/hold/quota-gate/UTC-day USD ceilings (Delegation:MutationAutoDispatch, Enabled=true, DailyBudgetUsd=75, ExpectedMinutes=720), attempt-once per operation, server-composed brief; MutationDispositionPending attention kind surfaces settled unattended batteries; no new column/status/engine; next: test-design`; `0d7765c1b docs(CARD-0552): plan - confirmed publication creates/links the CARD-0478 companion (AgentTaskLanding.VerificationCardId), Mutation ready row projected from publication with no open/Succeeded sourced task (keyed on the companion, Done exclusion untouched), idempotent verification-companion endpoint backfills the 11 landed cards with operations; CARD-0514 stays a refusal; next: test-design`; `6bda48d3a docs(CARD-0552): investigation - Mutation is fully wired server-side but never tracked: 0 companion cards ever created, no next=mutation source after land, 12 landed cards owe ~914 PCs (442 from tonight's six)`.

Next: No Review outcome on the card. Review the unique commits before any land.

### CARD-1833 NEEDS-REVIEW

Review, High/Soon. Human review questions: the operator decision queue

Branch `feat/card-task-236f16eb` tip `a26cbca907c1649af823d8c11675f2871e819a1a` (2026-11-10). Merge-base `ac6d00022e94`. Cherry `+1` `-0`. Chain: no shared unique commit with the other 48. Master commits since the merge-base: 190. Of those, 0 touch the three-dot paths.

Unique commits:

- `a26cbca907c1` docs: why Antiphon-Decisions failed to launch

Unique paths (1): docs/investigations/2026-11-10-antiphon-decisions-agent-launch.md

Review 2026-11-10T15:38Z: Clean, round Final, scope Full, reviewedSourceClean false, SHA 585b6c1956e60a8b1f27de6eabbc96d8622fed96 (not-on-tip). Reviewed ref `refs/heads/feat/card-task-c0753d6e`. Detail: commits=1

Merge-tree exit 0, 0 conflict path(s).

Risk of what is still unique: script/docs-only.

Next: reviewedSourceClean is not true, so the land is not certified.

### CARD-1517 NEEDS-REVIEW

InProgress, High/Soon. Claude refinement needs one conditional Ctrl+C without killing the session

Branch `feat/card-task-38c4e51a` tip `7c59b5619e22b77bf43727e9074195be84a2f024` (2026-10-22). Merge-base `b8f0ce088b1a`. Cherry `+3` `-0`. Chain: no shared unique commit with the other 48. Master commits since the merge-base: 3921. Of those, 8 touch the three-dot paths (latest 2026-10-31).

Unique commits:

- `a76be8895b70` test: add portable Claude single-control qualification canaries
- `e5d1310218fd` test: fix native canary timeout type and record refusal metadata
- `7c59b5619e22` test: disclose native permission probe and record incomplete qualification

Unique paths (5): docs/superpowers/plans/2026-10-22-card-1517-1518-claude-permission-stalls-plan.md, tests/Antiphon.Agents.Pty.Tests/Antiphon.Agents.Pty.Tests.csproj, tests/Antiphon.Agents.Pty.Tests/ClaudeConditionalControlCanaryTests.cs, tests/Antiphon.Agents.Pty.Tests/ClaudeConditionalControlSession.cs, docs/investigations/2026-10-22-card-1517-1518-s0-code-evidence.md

Review: no active Review stage-outcome.

Merge-tree exit 1, 2 conflict path(s). docs/superpowers/plans/2026-10-22-card-1517-1518-claude-permission-stalls-plan.md, tests/Antiphon.Agents.Pty.Tests/Antiphon.Agents.Pty.Tests.csproj

Risk of what is still unique: test-only.

Master subjects naming the card: `6b09efc3a docs: plan CARD-1517 and CARD-1518 Claude stall recovery`.

Next: No Review outcome on the card. Review the unique commits before any land.

### CARD-1589 NEEDS-REVIEW

Review, High/Soon. Cache preflight F-9 rejects supported absent offline temp deployment hold

Branch `feat/card-task-c634b0a5` tip `6783c9427ebd1c84ab4739ba48f9c867fcd031cd` (2026-10-26). Merge-base `a97194ee7a4a`. Cherry `+7` `-427`. Chain: shares unique commits with CARD-1573, CARD-1596, CARD-1597, CARD-1598, CARD-1599, CARD-1601, CARD-1615, CARD-1618. Master commits since the merge-base: 3528. Of those, 1176 touch the three-dot paths (latest 2026-11-10).

Unique commits:

- `665bf12b12f0` docs: record prepared rc integration pick candidate
- `125c720b97ac` test(CARD-1574): expose regrowth and malformed-byte readiness holes
- `2b7383b243a3` docs: align restored resume-readiness plan for rc integration
- `f3aa3b43ed11` docs: record rc integration top-up picks and build evidence
- `a13e544e2bae` docs: record final batch C rc top-up mapping and build
- `afdeebdc173b` docs: record rc top-up 3 mapping and build evidence
- `105792f7d22f` docs: record rc top-up 4 mapping and verification

Unique paths (7): docs/superpowers/plans/2026-10-24-card-1574-resume-receipt-readiness-plan.md, tests/Antiphon.SessionRunner.Tests/ResumeReceiptReadinessTests.cs, ...

Review: no active Review stage-outcome.

Merge-tree exit 1, 52 conflict path(s). .antiphon/task-af2b78c8.md, docs/investigations/2026-10-25-card-1584-e2e-launch-bounds.md, scripts/audit-land-verification-precheck.ps1, scripts/c590-remote.sh, scripts/check-daemon-build.ps1, scripts/fixtures/c487-readonly-windows-witness.ps1, scripts/fixtures/nightly/c1584-e2e-source-roster.txt, scripts/fixtures/script-harness-cleanup-flow-witness.ps1, scripts/lib/c487-harness.ps1, scripts/lib/nightly-class-costs.json, scripts/lib/nightly-tests-impl.ps1, scripts/test-nightly-tests.ps1 ... (52 total; 0 of them are also in the unique commits)

Risk of what is still unique: test-only.

Next: Do not land this tip. Its unique commits are a shared chain and this card has no Review row.

### CARD-1820 NEEDS-REVIEW

Review, High/Soon. Selector v2: derived reverse-reference index, keyword triggers, roster registry, what-ran receipt

Branch `feat/card-task-6fde97f6` tip `238af3ff3e300f0e13fae828d5286c97cf8e65a8` (2026-11-10). Merge-base `9e050cf9600a`. Cherry `+1` `-4`. Chain: no shared unique commit with the other 48. Master commits since the merge-base: 293. Of those, 34 touch the three-dot paths (latest 2026-11-11).

Unique commits:

- `8e37d8b0c5d9` feat(CARD-1820): select tests from the derived impact index

Unique paths (7): docs/superpowers/plans/2026-11-09-selector-v2-derived-index.md, scripts/impacted-classes.ps1, tests/Antiphon.Tests/Scripts/DerivedImpactIndexBuilder.cs, tests/Antiphon.Tests/Scripts/DerivedImpactIndexTests.cs, tests/Antiphon.Tests/Scripts/ImpactedClassesSelectorTests.cs, tests/Antiphon.Tests/Scripts/MergeHighRadiusSubsetTests.cs, tests/derived-impact-index.json

Review 2026-11-10T19:12Z: Clean, round Final, scope Full, reviewedSourceClean true, SHA cb931f0890126ee1327eb2fc9fe29103e3667fe4 (not-on-tip). Reviewed ref `refs/heads/feat/card-task-0ddd898d`. Detail: commits=1

Merge-tree exit 1, 7 conflict path(s). docs/superpowers/plans/2026-11-09-selector-v2-derived-index.md, scripts/impacted-classes.ps1, tests/Antiphon.Tests/Scripts/DerivedImpactIndexBuilder.cs, tests/Antiphon.Tests/Scripts/DerivedImpactIndexTests.cs, tests/Antiphon.Tests/Scripts/ImpactedClassesSelectorTests.cs, tests/Antiphon.Tests/Scripts/MergeHighRadiusSubsetTests.cs, tests/derived-impact-index.json

Risk of what is still unique: test-only.

Master subjects naming the card: `188ac9421 fix(CARD-1820): regenerate the derived index on the batch 174 tree`; `584a16952 fix(CARD-1820): regenerate the derived index on the combined tree`; `750cb3c79 fix(CARD-1820): regenerate the derived index on master d27d19165`; `2d94f483f fix(CARD-1820): tag declaration-lane fixture classes as Unit`.

Next: Do not merge this tip. Master already has a later CARD-1820 series; the one unique commit add/add-conflicts with it. Diff against master before any re-review.

### CARD-0511 NEEDS-REVIEW

Review, High/Soon. Restart loop: SessionRunner/Server capability desync after sessionGenerationV1 (2026-09-13, distinct from CARD-0509)

Branch `feat/card-task-f1139c44` tip `63cab35e78d71a520ace2003ec0d9aef0a069a6c` (2026-09-18). Merge-base `3a62074ebfe5`. Cherry `+6` `-0`. Chain: no shared unique commit with the other 48. Master commits since the merge-base: 10447. Of those, 424 touch the three-dot paths (latest 2026-11-11).

Unique commits:

- `c66593e2ee19` docs(CARD-0511): carry plan + verification design onto the Code branch
- `b140ea2fe40b` feat(CARD-0511): S1+S2 - decide every launch on a fresh runner probe
- `8b273ceaced8` wip(CARD-0511): S3 runner-build hold - implementation + tests, verification pending
- `cf57225c4c2b` feat(CARD-0511): S3 - a stale runner is a durable hold, not a 1.4 h ladder
- `ca6e8064a5d2` docs(CARD-0511): S4 - runner build hold in the session-runtime and AppHost-runbook owners
- `63cab35e78d7` fix(CARD-0511): a 200 with a JSON-null capabilities body is no evidence, not unreachable

Unique paths (30): docs/superpowers/plans/2026-09-18-card-0511-runner-build-hold-plan.md, server/Application/Exceptions/RunnerCapabilityMismatchException.cs, server/Application/Exceptions/RunnerUnreachableException.cs, server/Application/Services/RestartFailurePolicy.cs, server/Application/Services/RunnerIdentity.cs, server/Domain/Enums/RestartFailureKind.cs, server/Infrastructure/Agents/SessionRunner/SessionRunnerHttpClient.cs, tests/Antiphon.Tests/Agents/SessionRunnerCapabilityGateTests.cs, tests/Antiphon.Tests/Agents/SessionRunnerGenerationWireTests.cs, tests/Antiphon.Tests/Application/RestartFailureClassificationTests.cs, tests/Antiphon.Tests/Application/RunnerIdentityTests.cs, client/src/api/agents.ts, client/src/features/agents/StandingSessionRecovery.tsx, server/Application/Dtos/AgentDtos.cs, server/Application/Services/AgentControlService.cs, server/Application/Services/AgentService.cs, server/Application/Services/AgentSessionService.cs, server/Application/Services/AgentSupervisorService.cs, server/Application/Services/RunnerBuildHoldState.cs, server/Domain/Entities/AgentSupervisionState.cs, server/Domain/Enums/AgentIncidentKind.cs, server/Infrastructure/Data/AppDbContext.cs, server/Migrations/20260918031315_AddRunnerBuildHold.Designer.cs, server/Migrations/20260918031315_AddRunnerBuildHold.cs, ...

Review 2026-09-18T04:34Z: Found, round Final, scope Unknown, reviewedSourceClean null, SHA unbound (sha-unbound). Detail: Code's "restart: none" claim is wrong: all changes are server-side and inert until restart-apphost.ps1 runs from the canonical checkout, which also applies the AddRunnerBuildHold migration at boot; code, tests and docs ...

Merge-tree exit 1, 7 conflict path(s). server/Application/Dtos/AgentDtos.cs, server/Application/Services/AgentService.cs, server/Application/Services/AgentSessionService.cs, server/Application/Services/RestartFailurePolicy.cs, server/Domain/Enums/AgentIncidentKind.cs, server/Infrastructure/Agents/SessionRunner/SessionRunnerHttpClient.cs, tests/Antiphon.Tests/Application/RestartFailureClassificationTests.cs

Risk of what is still unique: migration. server/Migrations/20260918031315_AddRunnerBuildHold.cs: additive (Up AddColumn; Down drops 3)

Master subjects naming the card: `42ed6c1e9 docs(investigation): triage the 53 Review-column cards with dispositions`.

Next: Latest review is Found (Code's "restart: none" claim is wrong: all changes are server-side and inert until restart-apphost.ps1 runs from the canonical checkout, wh...). Not accepted as fixed: no later Clean review.

### CARD-1708 NEEDS-REVIEW

Review, High/Normal. S4a release can destroy reflog-only commits (decision needed)

Branch `feat/card-task-f822e84d` tip `c9d9d7a631d923aa904a4daf1dde5ebd70ed73fb` (2026-11-05). Merge-base `37d92a8a0e86`. Cherry `+9` `-30`. Chain: no shared unique commit with the other 48. Master commits since the merge-base: 1194. Of those, 122 touch the three-dot paths (latest 2026-11-10).

Unique commits:

- `7846f289f911` test(c1717): fail when a disabled S4a switch still deletes
- `96d7cde36af3` fix(c1717): honour the S4a switch at every destructive step
- `35bb34ad7fe2` fix(c1717): re-read the S4a switch at each destructive continuation
- `82df1362cdd5` docs(c1717): round-2 Code report for CARD-1717 A (B-1..B-5)
- `b3873bd12e04` fix(c1717): require the live switch for a system release and re-read it before the mirror send
- `45f1aaee8d05` docs(c1717): Code report for the CARD-1717 S4a A+B integration
- `2f66c1bbe535` fix(c0824): read the S4a switch inside unregister, at the branch-delete child start and once a queued mirror send holds the gate (Review 1999cb95 B-1/B-2)
- `88e9fc746c78` docs(c0824): round-4 Code report for the S4a off-switch blockers B-1/B-2
- ... 1 more unique commits

Unique paths (14): server/Application/Services/TaskWorktreeRetirementService.cs, tests/Antiphon.Tests/Application/C0824DesktopLegacyReleaseSwitchTests.cs, server/Application/Services/DesktopLegacyReleasePolicyService.cs, server/Application/Services/WorktreeResidueSweepService.cs, server/Application/Dtos/WorktreeRemovalRequest.cs, server/Infrastructure/Git/GuardedWorktreeRemoval.cs, server/Application/Services/RemoteWorkspaceService.cs, server/Infrastructure/Agents/SessionRunner/PhoneHomeLiveConnection.cs, server/Infrastructure/Agents/SessionRunner/PhoneHomeRunnerClient.cs, server/Infrastructure/Git/LandingGit.cs, ...

Review 2026-11-06T02:55Z: Found, round Final, scope Interim, reviewedSourceClean false, SHA f458326474217fa34a836aabc15cbd6c1b4ba484 (not-on-tip). Detail: V-1: inherited failures and deferred verification keep the required gate incomplete. commits=0

Merge-tree exit 1, 13 conflict path(s). docs/orchestration-loop.md, server/Application/Services/DesktopLegacyReleasePolicyService.cs, server/Application/Services/RemoteWorkspaceService.cs, server/Application/Services/TaskWorktreeRetirementService.cs, server/Infrastructure/Git/GuardedWorktreeRemoval.cs, server/Infrastructure/Git/LandingGit.cs, server/Infrastructure/Git/WorktreeNoFollowDelete.cs, server/Infrastructure/Git/WorktreeReflogSalvage.cs, tests/Antiphon.Tests/Application/C0824DesktopLegacyReleaseSwitchTests.cs, tests/Antiphon.Tests/Application/C1708ReflogSalvageTests.cs, tests/Antiphon.Tests/Infrastructure/LandingRemovalPolicyControlTests.cs, tests/Antiphon.Tests/Infrastructure/WorktreeGuardedCleanupTests.cs, tests/Antiphon.Tests/Infrastructure/WorktreeNoFollowDeleteTests.cs

Risk of what is still unique: product code.

Next: Latest review is Found (V-1: inherited failures and deferred verification keep the required gate incomplete. commits=0). Not accepted as fixed: no later Clean review.

### CARD-1731 NEEDS-REVIEW

InProgress, Normal/Soon. Publication ledger tests (ExecutionLedger, Joins, Markers) sit at their 300 s timeout under load

Branch `feat/card-task-b8770621` tip `11cdf6ecbeb42ce7b08b4771943192395572507d` (2026-11-06). Merge-base `0dbd8295a034`. Cherry `+1` `-0`. Chain: no shared unique commit with the other 48. Master commits since the merge-base: 1090. Of those, 0 touch the three-dot paths.

Unique commits:

- `11cdf6ecbeb4` docs: investigate CARD-1731 publication ledger timeouts

Unique paths (1): docs/investigations/2026-11-06-debug-card-1731-ledger-tests.md

Review 2026-11-08T01:09Z: Clean, round Final, scope Full, reviewedSourceClean true, SHA cf0aba324bdfff15e10f4ef0120c84a6c380f345 (not-on-tip). Reviewed ref `refs/heads/feat/card-task-317b5f73`. Detail: commits=1

Merge-tree exit 0, 0 conflict path(s).

Risk of what is still unique: script/docs-only.

Master subjects naming the card: `8b75f3154 docs(investigations): decide the C599A execution-ledger budget`.

Next: The Clean review is not of this tip. Re-review the tip before land.

### CARD-1733 NEEDS-REVIEW

Review, Normal/Soon. ReleaseTagAllocator reservation matrices exhaust 300s; full affected-class row cannot finish

Branch `feat/card-task-8f43dbed` tip `803291f3cd4ca008afabbcc65d0cb56fd392546d` (2026-11-06). Merge-base `1a22e0d2ca7b`. Cherry `+1` `-0`. Chain: no shared unique commit with the other 48. Master commits since the merge-base: 1057. Of those, 0 touch the three-dot paths.

Unique commits:

- `803291f3cd4c` docs: diagnose allocator reservation matrix timeouts

Unique paths (1): docs/investigations/2026-11-06-debug-card-1733-allocator-timeouts.md

Review: no active Review stage-outcome.

Merge-tree exit 0, 0 conflict path(s).

Risk of what is still unique: script/docs-only.

Next: No Review outcome on the card. Review the unique commits before any land.

### CARD-0669 NEEDS-REVIEW

Review, Normal/Normal. Land residue accumulates: source-recheck refs, refs/antiphon/land pins, land-verify temp artifacts never collected

Branch `feat/card-task-29f3946d` tip `c0abb4faca41f7cdf3e80a54af421d5b7cac5421` (2026-10-13). Merge-base `63b2e70479c8`. Cherry `+3` `-0`. Chain: no shared unique commit with the other 48. Master commits since the merge-base: 6090. Of those, 0 touch the three-dot paths.

Unique commits:

- `633df73ce6ab` docs: report partial desktop cleanup and retained policy holds
- `49d18e75543f` docs: record refined cleanup authority and fresh worktree guards
- `c0abb4faca41` docs: record 31 GB partial cleanup and verified branch retention

Unique paths (1): docs/investigations/2026-10-13-desktop-antiphon-cleanup.md

Review: no active Review stage-outcome.

Merge-tree exit 0, 0 conflict path(s).

Risk of what is still unique: script/docs-only.

Next: No Review outcome on the card. Review the unique commits before any land.

### CARD-1347 NEEDS-REVIEW

Review, Normal/Normal. Scheduled dry-run-first sweep of old Codex pool-session history on server2

Branch `feat/card-task-e59b0f15` tip `3a2e2ce2168e76595f27d7db5cb4c62943f8abb3` (2026-10-19). Merge-base `c8bc95276ad1`. Cherry `+1` `-0`. Chain: no shared unique commit with the other 48. Master commits since the merge-base: 5021. Of those, 0 touch the three-dot paths.

Unique commits:

- `3a2e2ce2168e` docs(CARD-1347): record the server2 Codex history wipe

Unique paths (1): docs/investigations/2026-10-19-codex-history-wipe.md

Review: no active Review stage-outcome.

Merge-tree exit 0, 0 conflict path(s).

Risk of what is still unique: script/docs-only.

Next: No Review outcome on the card. Review the unique commits before any land.

### CARD-1636 NEEDS-REVIEW

Review, Normal/Normal. Non-custody adoption does not verify the host session id (local exposure)

Branch `feat/card-task-08469fcb` tip `7bd2f112e37792c8a42f12b1f7039e8dd5971b91` (2026-11-02). Merge-base `3a49b6013332`. Cherry `+1` `-8`. Chain: no shared unique commit with the other 48. Master commits since the merge-base: 1693. Of those, 32 touch the three-dot paths (latest 2026-11-10).

Unique commits:

- `7bd2f112e377` docs: task-08469fcb report on CARD-1636 non-custody adoption identity check

Unique paths (1): (task report only), ...

Review 2026-11-02T14:04Z: Clean, round Final, scope Unknown, reviewedSourceClean null, SHA unbound (sha-unbound). Detail: commits=1

Merge-tree exit 1, 3 conflict path(s). src/Antiphon.SessionRunner/SessionRunnerRuntime.cs, tests/Antiphon.SessionRunner.Tests/Card1636AdoptionIdentityTests.cs, tests/Antiphon.SessionRunner.Tests/PtyHostLeakSweep.cs

Risk of what is still unique: script/docs-only.

Master subjects naming the card: `4c1a3049e fix: refuse non-custody adoption when the hello names another session or host instance`; `775d8ddfa feat: record the pty-host instance id in its manifest`.

Next: reviewedSourceClean is not true, so the land is not certified.

### CARD-1690 NEEDS-REVIEW

Review, Normal/Normal. Client test performance: slow Mantine tests, RoutingSettingsTab 250 s, setup guard

Branch `feat/card-task-dd90f1b7` tip `2e7023b4280fe8e5d8630fd6a29cf169e7db2624` (2026-11-03). Merge-base `f32aed42e58a`. Cherry `+1` `-8`. Chain: no shared unique commit with the other 48. Master commits since the merge-base: 1422. Of those, 12 touch the three-dot paths (latest 2026-11-09).

Unique commits:

- `2e7023b4280f` docs(task-dd90f1b7): report the CARD-1690 client test-performance measurements

Unique paths (1): (task report only), ...

Review 2026-11-03T21:35Z: Clean, round Final, scope Unknown, reviewedSourceClean null, SHA unbound (sha-unbound). Detail: commits=1

Merge-tree exit 1, 3 conflict path(s). client/src/features/settings/ProviderEndpointFields.test.tsx, client/src/test/pollingElementError.ts, client/src/test/setupGuards.test.tsx

Risk of what is still unique: script/docs-only.

Master subjects naming the card: `2cad8a419 docs(client): note that a lazy poll error prints the DOM at first read (CARD-1690 review A-1)`; `ed97b91e7 test(client): give the cases still over 5 s provisional per-test budgets`.

Next: reviewedSourceClean is not true, so the land is not certified.

### CARD-1726 NEEDS-REVIEW

Review, Normal/Normal. Release-candidate red 2026-11-09: Antiphon.Tests exit 8, Antiphon.Tests exit 8, Antiphon.Tests exit 8, Antiphon.Tests exit 2, Antiphon.SessionRunner.Tests TIMEOUT, Antiphon.Tests exit 2, Antiphon.Tests exit 2, Antiphon.Tests exit 2, Antiphon.Tests exit 8, Antiphon.Tests exit 8, Antiphon.Tests exit 8

Branch `feat/card-task-d8f70e4c` tip `1d15909de96d07fb10fa66b0381131226c56a40c` (2026-11-05). Merge-base `1527c29fef27`. Cherry `+1` `-0`. Chain: no shared unique commit with the other 48. Master commits since the merge-base: 1094. Of those, 0 touch the three-dot paths.

Unique commits:

- `1d15909de96d` docs: diagnose incomplete Windows rc coverage and LiveLane omission

Unique paths (1): docs/investigations/2026-11-05-debug-rc-coverage-incomplete.md

Review 2026-11-06T01:05Z: Clean, round Final, scope Interim, reviewedSourceClean false, SHA 13e9c1c8b01916bf1e26a18e8945a2b209192b20 (not-on-tip). Reviewed ref `refs/heads/feat/card-task-07ab1e49`. Detail: commits=1

Merge-tree exit 0, 0 conflict path(s).

Risk of what is still unique: script/docs-only.

Next: Review scope is Interim, which cannot approve a land.

### CARD-1466 NEEDS-REVIEW

InProgress, Normal/Normal. Inherited census reds: TestSchemaSnapshotTests registry, shape_conflict_rule negative qualification, C1155 roster, C1382 load race

Branch `feat/card-task-1f10d17d` tip `2ce36496456b5853e043fd4c3f7fd9b24b4d9a17` (2026-11-05). Merge-base `1527c29fef27`. Cherry `+1` `-0`. Chain: no shared unique commit with the other 48. Master commits since the merge-base: 1094. Of those, 0 touch the three-dot paths.

Unique commits:

- `2ce36496456b` docs(card-1466): recheck inherited census reds on master

Unique paths (1): docs/investigations/2026-11-05-debug-card-1466-inherited-reds.md

Review: no active Review stage-outcome.

Merge-tree exit 0, 0 conflict path(s).

Risk of what is still unique: script/docs-only.

Next: No Review outcome on the card. Review the unique commits before any land.

### CARD-1222 NEEDS-REVIEW

Done, Normal/Normal. CARD-0038 split: Linux OS zombie census throws (WindowsZombieProcessCensus registered unconditionally); implement or record supported omission

Branch `feat/card-task-81fc0b3c` tip `8ab57f1eac7dc0ec7cffd90a0e57a444f049449b` (2026-11-10). Merge-base `84bc17576287`. Cherry `+2` `-3`. Chain: no shared unique commit with the other 48. Master commits since the merge-base: 73. Of those, 5 touch the three-dot paths (latest 2026-11-10).

Unique commits:

- `f98d45c4d60d` docs(CARD-1222): record the S1-S2 code report
- `8ab57f1eac7d` docs(CARD-1222): name the evidence-diff head

Unique paths (1): (task report only), ...

Review 2026-11-10T22:40Z: Found, round Final, scope Full, reviewedSourceClean false, SHA 8ab57f1eac7dc0ec7cffd90a0e57a444f049449b (at-tip). Detail: CP-1's owed Windows skip makes the required receipt row_failed, so reviewedSourceClean stays false. commits=1

Merge-tree exit 1, 1 conflict path(s). docs/docker-stack.md

Risk of what is still unique: script/docs-only.

Master subjects naming the card: `65b4aa4b6 fix(CARD-1222): refuse zombie census off Windows`; `aa8539207 test(CARD-1222): red witnesses for the unconditional Windows census`; `e771e7567 docs(CARD-1222): plan the Linux zombie-census omission`.

Next: Latest review is Found (CP-1's owed Windows skip makes the required receipt row_failed, so reviewedSourceClean stays false. commits=1). Not accepted as fixed: no later Clean review.

### CARD-1030 NEEDS-REVIEW

InProgress, Normal/Normal. C1008 host-fixture tests fail on Windows: bash launcher path, git init argv, flock sentinel (12 tests, inherited)

Branch `feat/card-task-7ee266d3` tip `4a7cbc1413fa7d405a5afd0f5fac56daf9234e10` (2026-10-04). Merge-base `a4d2d4b851fa`. Cherry `+12` `-0`. Chain: no shared unique commit with the other 48. Master commits since the merge-base: 7607. Of those, 240 touch the three-dot paths (latest 2026-11-10).

Unique commits:

- `81beefa7e1e4` test(c1030): expose legacy fixture boundaries for red-first witnesses; verification pending
- `920ba4e8c8db` test(c1030): implement fixture transport WIP; frozen remap baseline conflicts with legacy bytes
- `c64e22546c81` test(c1030): fix bridge raw-string compilation; qualification still pending
- `4a4030926571` test(c1030): fix Shouldly message arguments without changing assertions
- `69c9cdabadd5` test(c1030): preserve argv oracle and record blocked WIP; ordinary verification incomplete
- `7ac4919b6b88` docs(C1030): correct frozen remap golden proven by unmodified base
- `6d94dee8a89e` test(C1030): align proven golden and complete serialization and child guards; verification pending
- `fb29662b30cb` test(C1030): restore probe transport on cleanup and couple export and hash guards
- ... 4 more unique commits

Unique paths (6): docs/investigations/2026-10-04-card-1030-code-7ee266d3.md, tests/Antiphon.Tests/Scripts/C1008HostFixturePortabilityTests.cs, tests/Antiphon.Tests/Scripts/RemoteScriptContractTests.cs, tests/Antiphon.Tests/Scripts/RollingVolumeRecycleScriptTests.cs, scripts/fixtures/c1008-fake-docker.sh, docs/superpowers/plans/2026-10-04-card-1030-c1008-windows-host-fixture-plan.md

Review: no active Review stage-outcome.

Merge-tree exit 1, 3 conflict path(s). scripts/fixtures/c1008-fake-docker.sh, tests/Antiphon.Tests/Scripts/RemoteScriptContractTests.cs, tests/Antiphon.Tests/Scripts/RollingVolumeRecycleScriptTests.cs

Risk of what is still unique: test-only.

Next: No Review outcome on the card. Review the unique commits before any land.

### CARD-1334 NEEDS-REVIEW

InProgress, Normal/Normal. GrokDelegateEndToEndTests harness registers SessionRuntimeActionCoordinator but never runs its workers; pump-driven settlement witnesses stay red (inherited at CARD-0870 base)

Branch `feat/card-task-e3bcb4b8` tip `f1ba0f8e6c5a3890d2e4bba4b85a1107e226fa25` (2026-10-18). Merge-base `a1d24b6fc92d`. Cherry `+2` `-0`. Chain: no shared unique commit with the other 48. Master commits since the merge-base: 5025. Of those, 5 touch the three-dot paths (latest 2026-11-09).

Unique commits:

- `c63d6e2791d4` test(CARD-1334): start the runtime-action workers in the Grok E2E harness
- `f1ba0f8e6c5a` test(CARD-1334): start the coordinator task without Unwrap

Unique paths (1): tests/Antiphon.Tests/Application/GrokDelegateEndToEndTests.cs

Review: no active Review stage-outcome.

Merge-tree exit 1, 1 conflict path(s). tests/Antiphon.Tests/Application/GrokDelegateEndToEndTests.cs

Risk of what is still unique: test-only.

Next: No Review outcome on the card. Review the unique commits before any land.

### CARD-1573 NEEDS-REVIEW

Review, Normal/Normal. Checkpoint output-inventory reader refuses Antiphon.Tests.dll above 64 MiB

Branch `feat/card-task-56ddd55e` tip `39a7f70c4642785a0ce51461a5dc8d1d78fd8129` (2026-10-28). Merge-base `a97194ee7a4a`. Cherry `+7` `-533`. Chain: shares unique commits with CARD-1589, CARD-1596, CARD-1597, CARD-1598, CARD-1599, CARD-1601, CARD-1615, CARD-1618. Master commits since the merge-base: 3528. Of those, 1445 touch the three-dot paths (latest 2026-11-10).

Unique commits:

- `665bf12b12f0` docs: record prepared rc integration pick candidate
- `125c720b97ac` test(CARD-1574): expose regrowth and malformed-byte readiness holes
- `2b7383b243a3` docs: align restored resume-readiness plan for rc integration
- `f3aa3b43ed11` docs: record rc integration top-up picks and build evidence
- `a13e544e2bae` docs: record final batch C rc top-up mapping and build
- `afdeebdc173b` docs: record rc top-up 3 mapping and build evidence
- `105792f7d22f` docs: record rc top-up 4 mapping and verification

Unique paths (7): docs/superpowers/plans/2026-10-24-card-1574-resume-receipt-readiness-plan.md, tests/Antiphon.SessionRunner.Tests/ResumeReceiptReadinessTests.cs, ...

Review 2026-10-28T00:24Z: Found, round Final, scope Interim, reviewedSourceClean false, SHA 39a7f70c4642785a0ce51461a5dc8d1d78fd8129 (at-tip). Detail: D-1: new C1573 Unit test calls stat -c with no $IsLinux gate, so it is red by construction on Windows; D1/D2 repairs verified on Linux with red witnesses commits=1

Merge-tree exit 1, 85 conflict path(s). .antiphon/task-af2b78c8.md, docs/investigations/2026-10-25-card-1584-e2e-launch-bounds.md, docs/investigations/2026-10-27-unsafe-delete-and-replace-recovery.md, docs/orchestration-loop.md, docs/testing-and-build.md, scripts/audit-land-verification-graph.ps1, scripts/audit-land-verification-precheck.ps1, scripts/c590-remote.sh, scripts/check-daemon-build.ps1, scripts/fixtures/c487-readonly-windows-witness.ps1, scripts/fixtures/nightly/c1584-e2e-source-roster.txt, scripts/fixtures/script-harness-cleanup-flow-witness.ps1 ... (85 total; 0 of them are also in the unique commits)

Risk of what is still unique: test-only.

Next: Latest review is Found (D-1: new C1573 Unit test calls stat -c with no $IsLinux gate, so it is red by construction on Windows; D1/D2 repairs verified on Linux with...). Not accepted as fixed: no later Clean review.

### CARD-1597 NEEDS-REVIEW

Review, Normal/Normal. Qualification timing recorder 256 KiB cap refuses most real chunks

Branch `feat/card-task-34d54a5e` tip `b71d9b3179571d8444a494f78fa6c2cb5dc0490f` (2026-10-28). Merge-base `a97194ee7a4a`. Cherry `+7` `-532`. Chain: shares unique commits with CARD-1573, CARD-1589, CARD-1596, CARD-1598, CARD-1599, CARD-1601, CARD-1615, CARD-1618. Master commits since the merge-base: 3528. Of those, 1442 touch the three-dot paths (latest 2026-11-10).

Unique commits:

- `665bf12b12f0` docs: record prepared rc integration pick candidate
- `125c720b97ac` test(CARD-1574): expose regrowth and malformed-byte readiness holes
- `2b7383b243a3` docs: align restored resume-readiness plan for rc integration
- `f3aa3b43ed11` docs: record rc integration top-up picks and build evidence
- `a13e544e2bae` docs: record final batch C rc top-up mapping and build
- `afdeebdc173b` docs: record rc top-up 3 mapping and build evidence
- `105792f7d22f` docs: record rc top-up 4 mapping and verification

Unique paths (7): docs/superpowers/plans/2026-10-24-card-1574-resume-receipt-readiness-plan.md, tests/Antiphon.SessionRunner.Tests/ResumeReceiptReadinessTests.cs, ...

Review 2026-10-28T03:02Z: Clean, round Final, scope Full, reviewedSourceClean true, SHA 17cb417c3b3de3eca7bb31177f30082a55f94125 (not-on-tip). Reviewed ref `refs/heads/feat/card-task-c739b97b`. Detail: commits=1

Merge-tree exit 1, 83 conflict path(s). .antiphon/task-af2b78c8.md, docs/investigations/2026-10-25-card-1584-e2e-launch-bounds.md, docs/investigations/2026-10-27-unsafe-delete-and-replace-recovery.md, docs/orchestration-loop.md, docs/testing-and-build.md, scripts/audit-land-verification-graph.ps1, scripts/audit-land-verification-precheck.ps1, scripts/c590-remote.sh, scripts/check-daemon-build.ps1, scripts/fixtures/c487-readonly-windows-witness.ps1, scripts/fixtures/nightly/c1584-e2e-source-roster.txt, scripts/fixtures/script-harness-cleanup-flow-witness.ps1 ... (83 total; 0 of them are also in the unique commits)

Risk of what is still unique: test-only.

Next: The Clean review is not of this tip. Re-review the tip before land.

### CARD-1596 NEEDS-REVIEW

Review, Normal/Normal. Older path-based deletes (SafeDelete, ToolCopyAuthority, TempRootSweep) keep the parent-swap race

Branch `feat/card-task-d1e5f56b` tip `5c72a5daafbb50a48e9d0e9d055545ea112dc5e0` (2026-10-28). Merge-base `a97194ee7a4a`. Cherry `+7` `-532`. Chain: shares unique commits with CARD-1573, CARD-1589, CARD-1597, CARD-1598, CARD-1599, CARD-1601, CARD-1615, CARD-1618. Master commits since the merge-base: 3528. Of those, 1447 touch the three-dot paths (latest 2026-11-10).

Unique commits:

- `665bf12b12f0` docs: record prepared rc integration pick candidate
- `125c720b97ac` test(CARD-1574): expose regrowth and malformed-byte readiness holes
- `2b7383b243a3` docs: align restored resume-readiness plan for rc integration
- `f3aa3b43ed11` docs: record rc integration top-up picks and build evidence
- `a13e544e2bae` docs: record final batch C rc top-up mapping and build
- `afdeebdc173b` docs: record rc top-up 3 mapping and build evidence
- `105792f7d22f` docs: record rc top-up 4 mapping and verification

Unique paths (7): docs/superpowers/plans/2026-10-24-card-1574-resume-receipt-readiness-plan.md, tests/Antiphon.SessionRunner.Tests/ResumeReceiptReadinessTests.cs, ...

Review 2026-10-28T14:16Z: Clean, round Final, scope Interim, reviewedSourceClean false, SHA 3a0d8c9b006aae6fb7bba1cbb83712428b6b072a (not-on-tip). Reviewed ref `refs/heads/feat/card-task-fd72b5eb`. Detail: commits=0

Merge-tree exit 1, 85 conflict path(s). .antiphon/task-af2b78c8.md, docs/investigations/2026-10-25-card-1584-e2e-launch-bounds.md, docs/investigations/2026-10-27-unsafe-delete-and-replace-recovery.md, docs/orchestration-loop.md, docs/testing-and-build.md, scripts/audit-land-verification-graph.ps1, scripts/audit-land-verification-precheck.ps1, scripts/c590-remote.sh, scripts/check-daemon-build.ps1, scripts/fixtures/c487-readonly-windows-witness.ps1, scripts/fixtures/nightly/c1584-e2e-source-roster.txt, scripts/fixtures/script-harness-cleanup-flow-witness.ps1 ... (85 total; 0 of them are also in the unique commits)

Risk of what is still unique: test-only.

Next: Review scope is Interim, which cannot approve a land.

### CARD-1598 NEEDS-REVIEW

Review, Normal/Normal. Timing reader cost still grows with journals in the shared folder (about 200 hosts fit in 200 ms)

Branch `feat/card-task-06fc33df` tip `f218d2eaa067e4e11dc98ad4e555486db23ed1ce` (2026-10-28). Merge-base `a97194ee7a4a`. Cherry `+7` `-586`. Chain: shares unique commits with CARD-1573, CARD-1589, CARD-1596, CARD-1597, CARD-1599, CARD-1601, CARD-1615, CARD-1618. Master commits since the merge-base: 3528. Of those, 1553 touch the three-dot paths (latest 2026-11-11).

Unique commits:

- `665bf12b12f0` docs: record prepared rc integration pick candidate
- `125c720b97ac` test(CARD-1574): expose regrowth and malformed-byte readiness holes
- `2b7383b243a3` docs: align restored resume-readiness plan for rc integration
- `f3aa3b43ed11` docs: record rc integration top-up picks and build evidence
- `a13e544e2bae` docs: record final batch C rc top-up mapping and build
- `afdeebdc173b` docs: record rc top-up 3 mapping and build evidence
- `105792f7d22f` docs: record rc top-up 4 mapping and verification

Unique paths (7): docs/superpowers/plans/2026-10-24-card-1574-resume-receipt-readiness-plan.md, tests/Antiphon.SessionRunner.Tests/ResumeReceiptReadinessTests.cs, ...

Review 2026-10-28T12:30Z: Clean, round Final, scope Unknown, reviewedSourceClean null, SHA unbound (sha-unbound). Detail: commits=1

Merge-tree exit 1, 103 conflict path(s). .antiphon/task-af2b78c8.md, .gitattributes, client/src/api/attention.ts, client/src/features/attention/attentionVisuals.test.ts, client/src/features/attention/attentionVisuals.ts, docs/docker-stack.md, docs/investigations/2026-10-25-card-1584-e2e-launch-bounds.md, docs/orchestration-loop.md, docs/session-runtime-invariants.md, docs/superpowers/plans/2026-10-06-card-1087-recycle-census-plan.md, docs/testing-and-build.md, scripts/audit-land-verification-graph.ps1 ... (103 total; 0 of them are also in the unique commits)

Risk of what is still unique: test-only.

Next: reviewedSourceClean is not true, so the land is not certified.

### CARD-1599 NEEDS-REVIEW

Review, Normal/Normal. Checkpoint receipt reader read-stalled 3 s window fails on a loaded host

Branch `feat/card-task-db7d5ba3` tip `112c6d3b016da12f8ad632b146292d136dab903f` (2026-10-28). Merge-base `a97194ee7a4a`. Cherry `+7` `-616`. Chain: shares unique commits with CARD-1573, CARD-1589, CARD-1596, CARD-1597, CARD-1598, CARD-1601, CARD-1615, CARD-1618. Tip is contained by CARD-1601. Master commits since the merge-base: 3528. Of those, 1569 touch the three-dot paths (latest 2026-11-11).

Unique commits:

- `665bf12b12f0` docs: record prepared rc integration pick candidate
- `125c720b97ac` test(CARD-1574): expose regrowth and malformed-byte readiness holes
- `2b7383b243a3` docs: align restored resume-readiness plan for rc integration
- `f3aa3b43ed11` docs: record rc integration top-up picks and build evidence
- `a13e544e2bae` docs: record final batch C rc top-up mapping and build
- `afdeebdc173b` docs: record rc top-up 3 mapping and build evidence
- `105792f7d22f` docs: record rc top-up 4 mapping and verification

Unique paths (7): docs/superpowers/plans/2026-10-24-card-1574-resume-receipt-readiness-plan.md, tests/Antiphon.SessionRunner.Tests/ResumeReceiptReadinessTests.cs, ...

Review 2026-10-28T16:25Z: Found, round Final, scope Unknown, reviewedSourceClean null, SHA unbound (sha-unbound). Detail: 6 s window only moves the starvation threshold (probe: 82/200 healthy reads refused at 6 s); advisory follow-up for a pool-independent Linux read, does not block commits=1

Merge-tree exit 1, 100 conflict path(s). .antiphon/task-af2b78c8.md, .gitattributes, client/src/api/attention.ts, client/src/features/attention/attentionVisuals.test.ts, client/src/features/attention/attentionVisuals.ts, docs/docker-stack.md, docs/investigations/2026-10-25-card-1584-e2e-launch-bounds.md, docs/orchestration-loop.md, docs/session-runtime-invariants.md, docs/superpowers/plans/2026-10-06-card-1087-recycle-census-plan.md, docs/testing-and-build.md, scripts/audit-land-verification-graph.ps1 ... (100 total; 0 of them are also in the unique commits)

Risk of what is still unique: test-only.

Master subjects naming the card: `cc6a727bc docs: restore the three-second receipt read window`.

Next: Latest review is Found (6 s window only moves the starvation threshold (probe: 82/200 healthy reads refused at 6 s); advisory follow-up for a pool-independent Linu...). Not accepted as fixed: no later Clean review.

### CARD-1601 NEEDS-REVIEW

Review, Normal/Normal. Receipt reader stall window measures thread-pool starvation (sync-over-async on Linux)

Branch `feat/card-task-e9436c0d` tip `9563933408345a2b01d8b27ddf554b6b15b7f2bc` (2026-10-28). Merge-base `a97194ee7a4a`. Cherry `+7` `-621`. Chain: shares unique commits with CARD-1573, CARD-1589, CARD-1596, CARD-1597, CARD-1598, CARD-1599, CARD-1615, CARD-1618. Tip contains CARD-1599. Master commits since the merge-base: 3528. Of those, 1571 touch the three-dot paths (latest 2026-11-11).

Unique commits:

- `665bf12b12f0` docs: record prepared rc integration pick candidate
- `125c720b97ac` test(CARD-1574): expose regrowth and malformed-byte readiness holes
- `2b7383b243a3` docs: align restored resume-readiness plan for rc integration
- `f3aa3b43ed11` docs: record rc integration top-up picks and build evidence
- `a13e544e2bae` docs: record final batch C rc top-up mapping and build
- `afdeebdc173b` docs: record rc top-up 3 mapping and build evidence
- `105792f7d22f` docs: record rc top-up 4 mapping and verification

Unique paths (7): docs/superpowers/plans/2026-10-24-card-1574-resume-receipt-readiness-plan.md, tests/Antiphon.SessionRunner.Tests/ResumeReceiptReadinessTests.cs, ...

Review 2026-10-28T17:27Z: Found, round Final, scope Unknown, reviewedSourceClean null, SHA unbound (sha-unbound). Detail: Stale "six-second window" text at docs/testing-and-build.md:1591 (advisory, docs only); no regression and nothing fails open. commits=1

Merge-tree exit 1, 100 conflict path(s). .antiphon/task-af2b78c8.md, .gitattributes, client/src/api/attention.ts, client/src/features/attention/attentionVisuals.test.ts, client/src/features/attention/attentionVisuals.ts, docs/docker-stack.md, docs/investigations/2026-10-25-card-1584-e2e-launch-bounds.md, docs/orchestration-loop.md, docs/session-runtime-invariants.md, docs/superpowers/plans/2026-10-06-card-1087-recycle-census-plan.md, docs/testing-and-build.md, scripts/audit-land-verification-graph.ps1 ... (100 total; 0 of them are also in the unique commits)

Risk of what is still unique: test-only.

Master subjects naming the card: `82227976a docs: record CARD-1601 validation and landing handoff`.

Next: Latest review is Found (Stale "six-second window" text at docs/testing-and-build.md:1591 (advisory, docs only); no regression and nothing fails open. commits=1). Not accepted as fixed: no later Clean review.

### CARD-1615 NEEDS-REVIEW

Review, Normal/Normal. Rollout Deploy task (Shared workspace) holds bound lands as a source writer, deadlocking redeploy-old RecycleLandInFlight

Branch `feat/card-task-b34eae94` tip `f9bf72bd500b30d7dc7fae32971a2c8c83923c69` (2026-10-30). Merge-base `a97194ee7a4a`. Cherry `+15` `-810`. Chain: shares unique commits with CARD-1573, CARD-1589, CARD-1596, CARD-1597, CARD-1598, CARD-1599, CARD-1601, CARD-1618. Master commits since the merge-base: 3528. Of those, 1782 touch the three-dot paths (latest 2026-11-11).

Unique commits:

- `665bf12b12f0` docs: record prepared rc integration pick candidate
- `125c720b97ac` test(CARD-1574): expose regrowth and malformed-byte readiness holes
- `2b7383b243a3` docs: align restored resume-readiness plan for rc integration
- `f3aa3b43ed11` docs: record rc integration top-up picks and build evidence
- `a13e544e2bae` docs: record final batch C rc top-up mapping and build
- `afdeebdc173b` docs: record rc top-up 3 mapping and build evidence
- `105792f7d22f` docs: record rc top-up 4 mapping and verification
- `3a029544ef7a` docs(card-1615): push deadlock plan skeleton
- ... 7 more unique commits

Unique paths (9): docs/superpowers/plans/2026-10-24-card-1574-resume-receipt-readiness-plan.md, tests/Antiphon.SessionRunner.Tests/ResumeReceiptReadinessTests.cs, docs/superpowers/plans/2026-10-30-card-1615-deploy-land-deadlock-plan.md, ...

Review 2026-10-30T04:35Z: Found, round Final, scope None, reviewedSourceClean false, SHA 0d442c194be86f9cc38e55c56319c78dfc36b11d (not-on-tip). Reviewed ref `refs/heads/feat/card-task-7dd90600`. Detail: Four plan defects: the watchdog can't recover AppHost under a live marker, the marker lapses at every phase handover and lets a new rollout start over an unfinished one, an unknown marker has no override, and D-12 mista...

Merge-tree exit 1, 131 conflict path(s). .antiphon/task-af2b78c8.md, client/src/api/attention.ts, client/src/features/attention/attentionVisuals.test.ts, client/src/features/attention/attentionVisuals.ts, docs/docker-stack.md, docs/investigations/2026-10-25-card-1584-e2e-launch-bounds.md, docs/orchestration-loop.md, docs/release-gates.md, docs/session-runtime-invariants.md, docs/superpowers/plans/2026-09-14-card-0508-worktree-base-selection-plan.md, docs/testing-and-build.md, scripts/audit-land-verification-graph.ps1 ... (131 total; 0 of them are also in the unique commits)

Risk of what is still unique: test-only.

Next: Latest review is Found (Four plan defects: the watchdog can't recover AppHost under a live marker, the marker lapses at every phase handover and lets a new rollout...). Not accepted as fixed: no later Clean review.

### CARD-1618 NEEDS-REVIEW

Review, Normal/Normal. Make the 35 Windows-skipped Checkpoint rows run and gate on Windows

Branch `feat/card-task-dd567e5f` tip `823c5526425d17efe0258e3e96afe40ed9c15e7d` (2026-10-30). Merge-base `a97194ee7a4a`. Cherry `+10` `-884`. Chain: shares unique commits with CARD-1573, CARD-1589, CARD-1596, CARD-1597, CARD-1598, CARD-1599, CARD-1601, CARD-1615. Master commits since the merge-base: 3528. Of those, 1858 touch the three-dot paths (latest 2026-11-11).

Unique commits:

- `665bf12b12f0` docs: record prepared rc integration pick candidate
- `125c720b97ac` test(CARD-1574): expose regrowth and malformed-byte readiness holes
- `2b7383b243a3` docs: align restored resume-readiness plan for rc integration
- `f3aa3b43ed11` docs: record rc integration top-up picks and build evidence
- `a13e544e2bae` docs: record final batch C rc top-up mapping and build
- `afdeebdc173b` docs: record rc top-up 3 mapping and build evidence
- `105792f7d22f` docs: record rc top-up 4 mapping and verification
- `59deddd30a41` test: record Windows scope A skip witnesses
- ... 2 more unique commits

Unique paths (9): docs/superpowers/plans/2026-10-24-card-1574-resume-receipt-readiness-plan.md, tests/Antiphon.SessionRunner.Tests/ResumeReceiptReadinessTests.cs, ...

Review: no active Review stage-outcome.

Merge-tree exit 1, 150 conflict path(s). .antiphon/task-af2b78c8.md, .gitattributes, client/src/api/attention.ts, client/src/features/attention/attentionVisuals.test.ts, client/src/features/attention/attentionVisuals.ts, docs/adr/0002-modern-conpty-backend.md, docs/docker-stack.md, docs/investigations/2026-10-25-card-1584-e2e-launch-bounds.md, docs/orchestration-loop.md, docs/release-gates.md, docs/session-runtime-invariants.md, docs/superpowers/plans/2026-09-14-card-0508-worktree-base-selection-plan.md ... (150 total; 0 of them are also in the unique commits)

Risk of what is still unique: test-only.

Master subjects naming the card: `7ba9c8625 docs: record CARD-1618 S-F aggregate timings and receipts`; `b154687cd docs: open CARD-1618 aggregate Debug record`.

Next: Do not land this tip. Its unique commits are a shared chain and this card has no Review row.

### CARD-0856 NEEDS-REVIEW

Review, Normal/Normal. Full Antiphon.Tests run is 270 min on Windows: effective parallelism 1.4x, Integration DB and git tests are 96% of the time; Herdr is 3%

Branch `feat/card-task-242906fa` tip `fed245d3145f8e66af839904dbe68e88ae2fe431` (2026-10-22). Merge-base `d1cffdb2f88e`. Cherry `+6` `-123`. Chain: no shared unique commit with the other 48. Master commits since the merge-base: 4640. Of those, 1009 touch the three-dot paths (latest 2026-11-11).

Unique commits:

- `0bbc5cfde4e3` test(CARD-1396): witness a write-held run.lock read on rc
- `263a0caddea4` fix(CARD-1396): read a write-held run.lock (rc backport, reader only)
- `4c76971c63c2` docs(CARD-1396): record the rc backport checkpoint evidence
- `ff00653c7045` fix(CARD-1422): a failed channel reply becomes its own debt, not a held action
- `1e42986c2425` docs(CARD-1422): rc port plan and checkpoint rows
- `fed245d3145f` docs(CARD-0856): S1 Code report - CARD-1420 ported to rc, CP-1 green with clean repair receipt

Unique paths (10): docs/superpowers/plans/2026-10-21-rc-backport-card-1396-1400-plan.md, scripts/test-nightly-run.ps1, tests/Antiphon.Tests/Scripts/NightlyDeadOwnerLockTests.cs, scripts/lib/nightly-common.ps1, docs/session-runtime-invariants.md, server/Application/Services/AgentSessionRuntime.cs, server/Application/Services/SessionMessageQueueService.cs, server/Application/Services/SessionRuntimeActionCoordinator.cs, docs/superpowers/plans/2026-10-21-card-1422-rc-port-plan.md, ...

Review: no active Review stage-outcome.

Merge-tree exit 1, 50 conflict path(s). docs/orchestration-loop.md, docs/release-gates.md, docs/session-runtime-invariants.md, docs/superpowers/plans/2026-10-21-card-1400-docs-input-bypasses-plan.md, docs/testing-and-build.md, scripts/audit-land-verification-graph.ps1, scripts/c590-remote.sh, scripts/lib/nightly-class-costs.json, scripts/lib/nightly-common.ps1, scripts/lib/nightly-coverage.ps1, scripts/lib/nightly-tests-impl.ps1, scripts/lint-client.ps1 ... (50 total; 6 of them are also in the unique commits)

Risk of what is still unique: product code. Pin-moving: scripts/lib/nightly-common.ps1.

Master subjects naming the card: `6428ea7f6 docs(CARD-0856): plan - rc picks of the schema cache and git lane, measured class costs, lane-aware packing, ActiveSourceClaim and sweep-suite slices`.

Next: No Review outcome on the card. Review the unique commits before any land.

### CARD-1581 NEEDS-REVIEW

Review, Normal/Normal. Same-card dispatch waiting for a land holds the repository lease; the land yields to it (lease cycle starves lands)

Branch `feat/card-task-edc7b76c` tip `136083c7a181d942a78f92d9d43afb1cb5206864` (2026-11-06). Merge-base `5abd946dd2c2`. Cherry `+3` `-0`. Chain: no shared unique commit with the other 48. Master commits since the merge-base: 859. Of those, 1 touch the three-dot paths (latest 2026-11-08).

Unique commits:

- `998f95c3c098` test(CARD-1581): reproduce dependent dispatch land yield cycle
- `fb7a40f3d5c0` test(CARD-1581): correct witness compilation
- `136083c7a181` fix(CARD-1581): avoid yielding lands to dependent same-card dispatches

Unique paths (5): docs/superpowers/mutations/2026-11-06-card-1581-land-dispatch-cycle.md, docs/superpowers/plans/2026-11-06-card-1581-land-dispatch-cycle.md, tests/Antiphon.Tests/Application/AgentTaskLandDispatchYieldTests.cs, server/Application/Core/RepositoryLeaseWaiters.cs, server/Application/Services/AgentTaskLandService.cs

Review 2026-11-07T01:37Z: Found, round Final, scope Interim, reviewedSourceClean false, SHA 894b44369d87405e19dbbbdb9ee7ef46dd2abd4e (not-on-tip). Detail: B-6, B-7 product defects; B-8 missing acceptance; Unit gate remains incomplete. commits=0

Merge-tree exit 0, 0 conflict path(s).

Risk of what is still unique: product code.

Next: Merge-tree is clean, but the latest review is Found and its SHA is not this tip. Re-review 136083c7 before land.

### CARD-1704 NEEDS-REVIEW

Review, Normal/Normal. LibGit2Sharp git abstraction with a shared repository for the whole land

Branch `feat/card-task-b578f67a` tip `9b8c5cb06bb61fbf4c7d13d1c90ef50097dd5373` (2026-11-04). Merge-base `5449f8f50b92`. Cherry `+5` `-0`. Chain: no shared unique commit with the other 48. Master commits since the merge-base: 1291. Of those, 68 touch the three-dot paths (latest 2026-11-10).

Unique commits:

- `c6e84673efad` test(card-1704): S1 red: typed-read, observer, session-scope, backend-setting, contract and recorder tests on stub seams
- `97e1faab64dd` feat(card-1704): S1 IGitSession seam, CLI only: typed reads, observer, Landing:GitBackend, callers moved
- `bdb0641b9088` test(card-1704): E-4 finds EvidenceGit on LandDeliveryOptions, its declaring type
- `344d40585b8c` fix(card-1704): keep LandingGit's one-argument constructor; observer/backend get their own
- `9b8c5cb06bb6` docs(card-1704): S1 Code report (task b578f67a)

Unique paths (30): server/Application/Interfaces/IGitSession.cs, server/Application/Interfaces/ILandingGit.cs, server/Application/Settings/LandGitBackendSettings.cs, server/Infrastructure/Git/CliGitSession.cs, server/Infrastructure/Git/LandingGit.cs, tests/Antiphon.E2E/C1704ProtocolGitRecorderTests.cs, tests/Antiphon.E2E/Fixtures/ProtocolGitRecorder.cs, tests/Antiphon.Tests/Infrastructure/C1704/land-trace-base.txt, tests/Antiphon.Tests/Infrastructure/C1704BackendSettingTests.cs, tests/Antiphon.Tests/Infrastructure/C1704ContractTests.cs, tests/Antiphon.Tests/Infrastructure/C1704GitObserverTests.cs, tests/Antiphon.Tests/Infrastructure/C1704SessionScopeTests.cs, tests/Antiphon.Tests/Infrastructure/C1704TypedReadShapeTests.cs, tests/Antiphon.Tests/TestHelpers/C1704Corpus.cs, tests/Antiphon.Tests/TestHelpers/C1704Doubles.cs, tests/Antiphon.Tests/TestHelpers/C1704LandTrace.cs, tests/Antiphon.Tests/TestHelpers/C1704Settings.cs, tests/Antiphon.Tests/TestHelpers/LandingGitFixture.cs, server/Application/Dtos/LandingDtos.cs, server/Application/Services/AgentTaskLandService.cs, server/Application/Services/AgentTaskLandSourceResolver.cs, server/Application/Services/AgentTaskLandingProtocol.cs, server/Application/Services/LandOperationFactory.cs, server/Application/Services/LandVerificationPolicy.cs, ...

Review 2026-11-04T13:33Z: Found, round Final, scope Unknown, reviewedSourceClean null, SHA unbound (sha-unbound). Detail: D-1: Landing:GitBackend reports Cli/"LibGit2 not built" while the operator's libgit2 reads are live by default; Track L per-scope session contradicts 4a4945b6c commits=1

Merge-tree exit 1, 5 conflict path(s). server/Application/Services/AgentTaskLandingProtocol.cs, server/Infrastructure/Git/GuardedVerificationRemoval.cs, server/Infrastructure/Git/GuardedWorktreeRemoval.cs, server/Infrastructure/Git/LandWorkspace.cs, server/Infrastructure/Git/LandingGit.cs

Risk of what is still unique: startup/safety.

Next: Latest review is Found (D-1: Landing:GitBackend reports Cli/"LibGit2 not built" while the operator's libgit2 reads are live by default; Track L per-scope session c...). Not accepted as fixed: no later Clean review.

### CARD-0931 STALE-ABANDON

Review, High/Soon. Flaky-fix plan CP-3/CP-15 method-OR checkpoint filters select zero tests on Linux (missing trailing * per operand)

Branch `feat/card-task-4963339c` tip `c2420e0336c7b1571740bd5f0b006654a7ad7ab6` (2026-10-23). Merge-base `c51023b99b3f`. Cherry `+0` `-4`. Chain: no shared unique commit with the other 48. Master commits since the merge-base: 3633. Of those, 98 touch the three-dot paths (latest 2026-11-10).

No unique patch. The branch commits are patch-equivalent to origin/master.

Review: no active Review stage-outcome.

Merge-tree exit 0, 0 conflict path(s).

Risk of what is still unique: none unique.

Next: Do not land. git cherry shows no patch that origin/master lacks.

### CARD-1351 STALE-ABANDON

Review, High/Normal. Output-distiller prompt review: contract v5 checks length first and stops hoarding non-gate identifiers

Branch `feat/card-task-ab4e2125` tip `34083e79c02b9bcb8479712e5fdabe2351f472ab` (2026-10-19). Merge-base `d5dec9555bd7`. Cherry `+0` `-1`. Chain: no shared unique commit with the other 48. Master commits since the merge-base: 4990. Of those, 2 touch the three-dot paths (latest 2026-11-09).

No unique patch. The branch commits are patch-equivalent to origin/master.

Review 2026-10-20T15:47Z: Clean, round Final, scope Unknown, reviewedSourceClean null, SHA unbound (sha-unbound). Detail: commits=0

Merge-tree exit 0, 0 conflict path(s).

Risk of what is still unique: none unique.

Next: Do not land. git cherry shows no patch that origin/master lacks.

### CARD-1532 STALE-ABANDON

InProgress, High/Normal. Checkpoint source stamps reject build DLLs above the 64 MiB receipt limit

Branch `feat/card-task-12dd82bc` tip `7235182cb61e1f99eb0e4fb911e501669c3d5240` (2026-10-23). Merge-base `0643a3a721cf`. Cherry `+0` `-7`. Chain: no shared unique commit with the other 48. Master commits since the merge-base: 3829. Of those, 119 touch the three-dot paths (latest 2026-11-10).

No unique patch. The branch commits are patch-equivalent to origin/master.

Review 2026-10-23T07:22Z: Clean, round Final, scope Full, reviewedSourceClean true, SHA 7932e60205a31576011e7b91c384f014cbb303e2 (not-on-tip). Reviewed ref `refs/heads/feat/card-task-7d024505`. Detail: commits=0

Merge-tree exit 1, 6 conflict path(s). docs/superpowers/plans/2026-10-23-card-1531-checkpoint-admission-budget-plan.md, docs/testing-and-build.md, scripts/lib/checkpoint-file-reader.ps1, scripts/test-checkpoint-large-stamp.ps1, tests/Antiphon.Tests/Checkpoints/CheckpointLargeStampScriptTests.cs, tests/Antiphon.Tests/Checkpoints/CheckpointLargeStampTests.cs

Risk of what is still unique: script/docs-only (already patched).

Master subjects naming the card: `869f31454 docs: plan CARD-1532 streaming stamps and sequence checkpoint fixes`.

Next: Do not land. git cherry shows no patch that origin/master lacks.

### CARD-1553 STALE-ABANDON

InProgress, High/Normal. Repair C1115 tests whose nested lease-busy assumption changed with CARD-1430

Branch `feat/card-task-a5f9c44c` tip `8c7765eb7674f9b28cbff20c60d3b0a724f354a1` (2026-10-23). Merge-base `877b9ce108e6`. Cherry `+0` `-6`. Chain: no shared unique commit with the other 48. Master commits since the merge-base: 3711. Of those, 14 touch the three-dot paths (latest 2026-10-31).

No unique patch. The branch commits are patch-equivalent to origin/master.

Review: no active Review stage-outcome.

Merge-tree exit 0, 0 conflict path(s).

Risk of what is still unique: none unique.

Next: Do not land. git cherry shows no patch that origin/master lacks.

### CARD-1548 STALE-ABANDON

Review, High/Normal. Update action-owning retention and purge fixtures for CARD-1259 dependencies

Branch `feat/card-task-3fa840bf` tip `68be5426098f1deb7f01db10d2162fb77493609b` (2026-10-23). Merge-base `877b9ce108e6`. Cherry `+0` `-7`. Chain: no shared unique commit with the other 48. Master commits since the merge-base: 3711. Of those, 14 touch the three-dot paths (latest 2026-11-09).

No unique patch. The branch commits are patch-equivalent to origin/master.

Review: no active Review stage-outcome.

Merge-tree exit 1, 1 conflict path(s). tests/Antiphon.Tests/TestHelpers/LandReceiptScanHarness.cs

Risk of what is still unique: none unique.

Next: Do not land. git cherry shows no patch that origin/master lacks.

### CARD-1040 STALE-ABANDON

Review, Normal/Soon. Unit timeouts/inherited failures: resolve CARD-1021 jq preconditions and measure limits

Branch `feat/card-task-d20ed3ac` tip `11d9121e3899dff70810f6606656f0b846bb804b` (2026-10-04). Merge-base `0e9ce3848414`. Cherry `+0` `-2`. Chain: no shared unique commit with the other 48. Master commits since the merge-base: 7467. Of those, 188 touch the three-dot paths (latest 2026-11-10).

No unique patch. The branch commits are patch-equivalent to origin/master.

Review 2026-10-04T22:22Z: Clean, round Final, scope Full, reviewedSourceClean false, SHA 11d9121e3899dff70810f6606656f0b846bb804b (at-tip). Detail: commits=0

Merge-tree exit 1, 1 conflict path(s). docs/testing-and-build.md

Risk of what is still unique: script/docs-only (already patched).

Master subjects naming the card: `90b786720 docs(CARD-1040): record S1 admission and retained timing; S2 V/R pending`; `02dbf8b11 docs(CARD-1040): document S1 jq admission; S2 proof awaits activation`; `5b46e3263 docs(CARD-1040): record successful manifest and isolated seam validation`; `1807f79dc docs(CARD-1040): freeze fifteen-method jq verification and isolated absence control`.

Next: Do not land. git cherry shows no patch that origin/master lacks.

### CARD-1510 STALE-ABANDON

InProgress, Normal/Soon. Windows PATH probe test rejects the PowerShell launcher alias prefix

Branch `feat/card-task-5019ea31` tip `705662142c95e9c3614a8a229f0fbd4b2bbf2c66` (2026-10-23). Merge-base `ef121b23f3d7`. Cherry `+0` `-3`. Chain: no shared unique commit with the other 48. Master commits since the merge-base: 3870. Of those, 4 touch the three-dot paths (latest 2026-10-29).

No unique patch. The branch commits are patch-equivalent to origin/master.

Review 2026-10-23T04:30Z: Clean, round Final, scope Full, reviewedSourceClean true, SHA 705662142c95e9c3614a8a229f0fbd4b2bbf2c66 (at-tip). Detail: commits=0

Merge-tree exit 0, 0 conflict path(s).

Risk of what is still unique: script/docs-only (already patched).

Master subjects naming the card: `f9836a507 docs: plan CARD-1510 PATH probe fixture verification`.

Next: Do not land. git cherry shows no patch that origin/master lacks.

### CARD-0972 STALE-ABANDON

Review, Normal/Soon. CARD-0954/0955 pending: Windows native half-reset row and SourceLanding Mutation PCs; frame cap keeps throw-site end only

Branch `feat/card-task-c2afd605` tip `4117d4846703c14c39ff942ff2cedc32fd37e80a` (2026-10-23). Merge-base `3d234eda960f`. Cherry `+0` `-2`. Chain: no shared unique commit with the other 48. Master commits since the merge-base: 3795. Of those, 2 touch the three-dot paths (latest 2026-10-23).

No unique patch. The branch commits are patch-equivalent to origin/master.

Review: no active Review stage-outcome.

Merge-tree exit 0, 0 conflict path(s).

Risk of what is still unique: none unique.

Next: Do not land. git cherry shows no patch that origin/master lacks.

### CARD-1368 STALE-ABANDON

Review, Normal/Normal. Checkpoint cleanup refuses runtime-created TestResults under the row output (output-ledger-mismatch) and invalidates green receipts

Branch `feat/card-task-30a13032` tip `359cc912235d840e9f94202592e86480d87db468` (2026-10-19). Merge-base `6ec71d427574`. Cherry `+0` `-17`. Chain: no shared unique commit with the other 48. Master commits since the merge-base: 4968. Of those, 193 touch the three-dot paths (latest 2026-11-10).

No unique patch. The branch commits are patch-equivalent to origin/master.

Review 2026-10-20T00:07Z: Clean, round Final, scope Full, reviewedSourceClean true, SHA 359cc912235d840e9f94202592e86480d87db468 (at-tip). Reviewed ref `refs/heads/feat/card-task-6dcbc7ca`. Detail: commits=0

Merge-tree exit 1, 10 conflict path(s). docs/testing-and-build.md, src/Antiphon.HostCleanup/Checkpoints/CheckpointFileReader.cs, tests/Antiphon.Tests/Checkpoints/OutputCleanupTests.cs, tests/Antiphon.Tests/Checkpoints/RunSchedulerTests.cs, tests/Antiphon.Tests/Infrastructure/HostCleanupCheckpointReadDeadlineTests.cs, tests/Antiphon.Tests/Infrastructure/HostCleanupCheckpointReceiptTests.cs, tools/Antiphon.Checkpoints/BuildConfinement.cs, tools/Antiphon.Checkpoints/CheckpointApp.cs, tools/Antiphon.Checkpoints/Execution/RunScheduler.cs, tools/Antiphon.Checkpoints/Report/ReportModel.cs

Risk of what is still unique: script/docs-only (already patched).

Master subjects naming the card: `4425fb803 docs(CARD-1356): plan build deadline witness after CARD-1368`; `33c8096ed fix(tests): distinguish CARD-1358 receipt stalls from caller cancellation`; `16c683d01 docs(CARD-1368): record implementation and incomplete verification`; `73b17f2e7 docs(CARD-1368): plan host-exit adoption of row-written output entries and the 1 GiB content-stamp bound`.

Next: Do not land. git cherry shows no patch that origin/master lacks.

### CARD-1376 STALE-ABANDON

Review, Normal/Normal. ChromiumRuntimeSmokeTests: thread the TUnit cancellation token into Playwright.CreateAsync (TUnit0015; CARD-1323 S4 follow-up)

Branch `feat/card-task-5ec48582` tip `9b97b7aebae4f387d0fea86b4fd494912e4dd800` (2026-10-20). Merge-base `6ec71d427574`. Cherry `+0` `-6`. Chain: no shared unique commit with the other 48. Master commits since the merge-base: 4968. Of those, 85 touch the three-dot paths (latest 2026-11-10).

No unique patch. The branch commits are patch-equivalent to origin/master.

Review 2026-10-20T05:10Z: Clean, round Final, scope Full, reviewedSourceClean true, SHA 9b97b7aebae4f387d0fea86b4fd494912e4dd800 (at-tip). Reviewed ref `refs/heads/feat/card-task-4392749f`. Detail: commits=0

Merge-tree exit 1, 1 conflict path(s). docs/superpowers/plans/2026-10-20-card-1323-rc-lane-landdelivery-placement-plan.md

Risk of what is still unique: none unique.

Next: Do not land. git cherry shows no patch that origin/master lacks.

### CARD-1528 STALE-ABANDON

InProgress, Normal/Normal. CARD-1483 follow-up: canary misses System.IO.File.Exists property functions and metadata-only reads

Branch `feat/card-task-2d02464e` tip `1a30f3bebf5e08055644f375157b8935ba49027e` (2026-10-23). Merge-base `bd215d7b4f55`. Cherry `+0` `-11`. Chain: no shared unique commit with the other 48. Master commits since the merge-base: 3823. Of those, 101 touch the three-dot paths (latest 2026-11-10).

No unique patch. The branch commits are patch-equivalent to origin/master.

Review 2026-10-23T08:21Z: Clean, round Final, scope Full, reviewedSourceClean true, SHA 33f51ccb37a8b4aa9af7cc8275877391bfaac635 (not-on-tip). Reviewed ref `refs/heads/feat/card-task-1e78def7`. Detail: commits=0

Merge-tree exit 1, 8 conflict path(s). docs/orchestration-loop.md, scripts/audit-land-verification-graph.ps1, scripts/test-land-graph-c1528-fixtures.ps1, tests/Antiphon.Tests/Application/Fixtures/land-graph-c1528.json, tests/Antiphon.Tests/Application/LandGraphEvaluationFilesystemTests.cs, tests/Antiphon.Tests/Application/LandGraphEvaluationScriptTests.cs, tools/Antiphon.LandGraphAudit/EvaluationExpressionPolicy.cs, tools/Antiphon.LandGraphAudit/SolutionAudit.cs

Risk of what is still unique: script/docs-only (already patched).

Master subjects naming the card: `b440d1e2a docs(land-audit): freeze CARD-1528 qualification and mutation handoff`.

Next: Do not land. git cherry shows no patch that origin/master lacks.

### CARD-1639 STALE-ABANDON

Review, Normal/Normal. CARD-1380 successor: equal-tree receipt land skip and lease doc drift

Branch `feat/card-task-48e15e43` tip `b032722199a0f32e9d03da52d3611351508dcc82` (2026-11-02). Merge-base `3a49b6013332`. Cherry `+0` `-1`. Chain: no shared unique commit with the other 48. Master commits since the merge-base: 1693. Of those, 43 touch the three-dot paths (latest 2026-11-10).

No unique patch. The branch commits are patch-equivalent to origin/master.

Review 2026-11-02T12:46Z: Clean, round Final, scope Full, reviewedSourceClean true, SHA b032722199a0f32e9d03da52d3611351508dcc82 (at-tip). Reviewed ref `refs/heads/feat/card-task-73a9b0f6`. Detail: commits=0

Merge-tree exit 0, 0 conflict path(s).

Risk of what is still unique: none unique.

Master subjects naming the card: `a2b753427 docs: the land releases the repository mutation lease for VerifyPreparedAsync`.

Next: Do not land. git cherry shows no patch that origin/master lacks.

### CARD-1715 STALE-ABANDON

Review, Normal/Normal. status_untrusted is opaque and invisible; two land sites mislabel it

Branch `feat/card-task-420b5c90` tip `e64c5cd801c7fc8eda2f254e668fe5c22a411847` (2026-11-05). Merge-base `1527c29fef27`. Cherry `+0` `-2`. Chain: no shared unique commit with the other 48. Master commits since the merge-base: 1094. Of those, 5 touch the three-dot paths (latest 2026-11-10).

No unique patch. The branch commits are patch-equivalent to origin/master.

Review 2026-11-06T00:54Z: Clean, round Final, scope Interim, reviewedSourceClean false, SHA e64c5cd801c7fc8eda2f254e668fe5c22a411847 (at-tip). Detail: commits=1

Merge-tree exit 1, 4 conflict path(s). client/src/features/delegations/TaskDetailBody.test.tsx, client/src/features/delegations/TaskDetailBody.tsx, docs/superpowers/mutations/2026-11-05-card-1715-controls.md, tests/Antiphon.Tests/Application/AgentTaskLandHalfResetTests.cs

Risk of what is still unique: none unique.

Next: Do not land. git cherry shows no patch that origin/master lacks.

### CARD-1728 STALE-ABANDON

Review, Normal/Normal. Debug Unit shard completion, coverage indexing cost and stop receipt

Branch `feat/card-task-b1e4f841` tip `0e841541e11d2c0690ce9ebbd8f0dbfd031973e7` (2026-11-07). Merge-base `5d5973be2cd8`. Cherry `+0` `-1`. Chain: no shared unique commit with the other 48. Master commits since the merge-base: 802. Of those, 35 touch the three-dot paths (latest 2026-11-10).

No unique patch. The branch commits are patch-equivalent to origin/master.

Review 2026-11-07T14:08Z: Clean, round Final, scope Full, reviewedSourceClean true, SHA 5acde1e2b7ad10791cabea5732f6b9a22cc84506 (not-on-tip). Reviewed ref `refs/heads/feat/card-task-b1ceb641`. Detail: commits=1

Merge-tree exit 0, 0 conflict path(s).

Risk of what is still unique: script/docs-only (already patched).

Next: Do not land. git cherry shows no patch that origin/master lacks.

### CARD-0569 STALE-ABANDON

Done, Normal/Normal. Clean up remaining stale sequential-override wording (antiphon-api.md, Coda string, delegate.ps1 comments)

Branch `feat/card-task-f0176553` tip `bca2fa8c4bbafe7d2a0792ad6f85bc4615935357` (2026-11-10). Merge-base `a69789c77865`. Cherry `+0` `-2`. Chain: no shared unique commit with the other 48. Master commits since the merge-base: 84. Of those, 3 touch the three-dot paths (latest 2026-11-10).

No unique patch. The branch commits are patch-equivalent to origin/master.

Review 2026-11-10T21:05Z: Clean, round Final, scope Full, reviewedSourceClean true, SHA bca2fa8c4bbafe7d2a0792ad6f85bc4615935357 (at-tip). Detail: commits=1

Merge-tree exit 0, 0 conflict path(s).

Risk of what is still unique: product code (already patched).

Master subjects naming the card: `25f63ea12 fix(CARD-0569): state the axis rule in the 409 coda and the stale docs`; `4bb287a7b test(CARD-0569): pin axis override wording before the cleanup`.

Next: Do not land. git cherry shows no patch that origin/master lacks.

### CARD-1293 STALE-ABANDON

Done, Normal/Normal. B-2 delay projection clearing, test coverage gaps, CP-24 label clash

Branch `feat/card-task-d953dc40` tip `427b7b9fe6fb93bd23d28ea11399e6393e815747` (2026-11-10). Merge-base `84bc17576287`. Cherry `+0` `-3`. Chain: no shared unique commit with the other 48. Master commits since the merge-base: 73. Of those, 3 touch the three-dot paths (latest 2026-11-10).

No unique patch. The branch commits are patch-equivalent to origin/master.

Review 2026-11-10T22:33Z: Clean, round Final, scope Full, reviewedSourceClean false, SHA 427b7b9fe6fb93bd23d28ea11399e6393e815747 (at-tip). Detail: commits=1

Merge-tree exit 0, 0 conflict path(s).

Risk of what is still unique: product code (already patched).

Master subjects naming the card: `2bd11c34b docs(CARD-1293): label the B-2 unit row B2-R2 so it is not master CP-24`; `d3daa17d2 fix(CARD-1293): keep unexplained waits, flag overdue observations, and stop one bad child from failing GET`; `461df136c test(CARD-1293): red witnesses for delay projection, session token, HTTP receipt, and GET mismatch`.

Next: Do not land. git cherry shows no patch that origin/master lacks.

### CARD-1300 STALE-ABANDON

Done, Normal/Normal. CARD-1289 follow-up: refusal order for closed cards, untested compared fields

Branch `feat/card-task-08ee53e0` tip `822d2de21c7cc4613fde8c8e1871aad0cacf3aeb` (2026-11-10). Merge-base `a69789c77865`. Cherry `+0` `-2`. Chain: no shared unique commit with the other 48. Master commits since the merge-base: 84. Of those, 5 touch the three-dot paths (latest 2026-11-11).

No unique patch. The branch commits are patch-equivalent to origin/master.

Review 2026-11-10T22:04Z: Clean, round Final, scope Full, reviewedSourceClean true, SHA 822d2de21c7cc4613fde8c8e1871aad0cacf3aeb (at-tip). Detail: commits=1

Merge-tree exit 0, 0 conflict path(s).

Risk of what is still unique: product code (already patched).

Master subjects naming the card: `6173cc88b fix(CARD-1300): refuse closed and changed cards before capacity and base git`; `93cea6071 test(CARD-1300): witness closed-card refusal order and compared fields`.

Next: Do not land. git cherry shows no patch that origin/master lacks.

### CARD-1225 STALE-ABANDON

Done, Normal/Normal. CARD-0038 split: orchestrator deny hook names powershell but the Linux runner image has only pwsh; define the Linux hook execution contract

Branch `feat/card-task-4b46ef18` tip `bcbc01f23e299c6cb4ba84a36abb82a7264424be` (2026-11-10). Merge-base `84bc17576287`. Cherry `+0` `-2`. Chain: no shared unique commit with the other 48. Master commits since the merge-base: 73. Of those, 4 touch the three-dot paths (latest 2026-11-10).

No unique patch. The branch commits are patch-equivalent to origin/master.

Review 2026-11-10T22:02Z: Clean, round Final, scope Full, reviewedSourceClean true, SHA bcbc01f23e299c6cb4ba84a36abb82a7264424be (at-tip). Detail: commits=1

Merge-tree exit 0, 0 conflict path(s).

Risk of what is still unique: product code (already patched).

Master subjects naming the card: `298c06473 fix(CARD-1225): run the orchestrator deny hook under pwsh on a Linux placement`; `911b5e3ba test(CARD-1225): red witness that a Linux placement deny hook still invokes powershell`.

Next: Do not land. git cherry shows no patch that origin/master lacks.

