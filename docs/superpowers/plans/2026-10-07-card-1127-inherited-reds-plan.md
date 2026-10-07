# CARD-1127: repair two inherited reds (migration pin, stale script-test fake)

Date: 2026-10-07. Plan task: `4abc2deb` (branch `feat/card-task-4abc2deb`).
Inspected checkout: `origin/master` at `eb1fe3407142f5ab11027fae7e6a0d138cc644fd`
(every `file:line` below was read at that SHA in a detached scratch worktree).
Inherited-red base named by the card: `303dc7853e8888b69bf987ac821d97c878da3d15`;
review SHA that found them: `4aef5c37f10f0d87614eb71985da075b8d82b6f3`.
Status: plan complete; **next: code**. The brief folds the verification design
into this dispatch, so the `## Verification design` section below is the closed
list Build executes.

## Outcome and scope

Both reds are **stale tests**. Neither is a live regression of production code,
of `scripts/delegate.ps1`, or of the base-preview contract orchestrators read.

1. `ChannelOutboundDurabilitySchemaTests.C519_Upgrade_preserves_ordinals_and_history`
   pins `20261004231302_ExtendChannelOutboundRecovery` as the newest applied
   migration. Five migrations have landed after it (the card knew four; CARD-1082
   added `20261006212209_AddAgentTaskSyncDebts` on 2026-10-06). At master the
   assertion reads `should be 197 but was 192`. Stale since
   `a232065dd` (2026-10-05, CARD-0667 S3a, `AddRunnerSeatReleases`).
2. `DelegateScriptWorktreeBaseTests.T0442_V15_initial_post_prints_the_service_base_preview(unknown_fallback)`
   injects its Git failure through a hook on `rev-parse --verify --quiet
   refs/heads/<branch>^{commit}`. CARD-0788 (`2ac7836a9`, 2026-10-01) replaced that
   per-candidate probe with one batched `for-each-ref` query, so the hook never
   fires, the resolver sees a healthy sibling, and the script prints the
   **Continue** preview line instead of the Target-plus-unknown-warning pair the
   arm expects. The script's preview output is unchanged: the line was neither
   moved, dropped, nor reworded. CARD-0788's plan ordered the obsolete hook
   replaced in the resolver tests and did so there, but this class was outside
   its checkpoint filter and kept the dead hook.

Repair only the two test files. No production, script, migration or docs edit.
The repaired script arm additionally asserts that its fault was reached, so an
obsolete hook can never again pass as coverage.

## Evidence and ground truth

Read the live card with `scripts/card.ps1 get CARD-1127 -Board Antiphon`; read the
CARD-1120 review run's `failures.md` (path below); diffed the resolver at
`2ac7836a9^` against master; read `GET /api/runner-defaults` and
`GET /api/session-runners` on 2026-10-07.

| Card assumption / question | Code or recorded execution at `eb1fe3407` | Consequence |
|---|---|---|
| Both reds are inherited, not CARD-1120/1122 induced. | `failures.md` of run `20261006-223628-f2d1` (CP-9, CP-15) at `4aef5c37`; the card records identical method-scoped reds at base `303dc7853`. `server/Migrations/` holds 198 migration classes; `ExtendChannelOutboundRecovery` is index 192, so at master the pin fails `should be 197 but was 192`. The script fake's command is still never issued (row below). | Inherited at every SHA since 2026-10-05 / 2026-10-01. No preliminary red replay is needed; the review receipts are the provenance. |
| Item 1 is a stale "is last" pin. | `tests/Antiphon.Tests/Application/ChannelOutboundDurabilitySchemaTests.cs:27` `position.ShouldBe(migrations.Length - 1)`; line 28 `ShouldBeGreaterThan(0)`; line 29 pins the predecessor `20261004011911_CompletedCardWorktreeCleanup`. Successors in `server/Migrations/`: `20261005121558_AddRunnerSeatReleases`, `20261005222935_AddAgentTaskParks`, `20261006064548_AddHostOccupancySamples`, `20261006154102_AddBlockedTaskParkLegacyReclaim`, `20261006212209_AddAgentTaskSyncDebts`. | Delete line 27 only. Lines 28–29 already assert the migration's identity and its predecessor, which is the stable fact. Never reintroduce an is-last or computed-index pin. |
| The down/up chain at line 76 still holds with newer migrations. | Line 76 `MigrateAsync(migrations[position - 1])` now reverts six migrations (the five successors plus the pinned one) and line 78 re-applies them over the seeded rows. The five successors' `Down()` bodies contain only `DropTable`/`DropColumn` calls and no `Sql(`; `ExtendChannelOutboundRecovery` has one `Sql(` in `Up()` (`...ExtendChannelOutboundRecovery.cs:92`) that this test already exercised when it last passed. Lines 33–34 run the empty-database chain `MigrateAsync("0")` then `MigrateAsync()`. | Keep both chains and every history assertion (lines 80–154). If a successor's `Down()`/`Up()` now fails over the seeded rows, that is a migration-reversibility defect: stop, file a card, do not edit a migration here (D-8). |
| The other three schema methods pass. | Card evidence: `failed: 1 succeeded: 3` for the class at base. Roster at master: 4 methods, 4 results. | Whole-class row of exactly 4 results. |
| Item 2: the script dropped or moved the preview line (card hypothesis). | `scripts/delegate.ps1:1206-1223`: `if ($created.worktreeBase)` prints one of three `base preview:` lines (Continue 1211, WaitForLand 1215, else 1218 `"{decision} at {fallbackRef}; landing target {2}; reason {3}"`), then `WARNING: {warning}` per preview warning (1221–1223). The failing arm's output satisfied line 74 (`ShouldContain("base preview:")`) and failed only line 93 (`ShouldContain("unknown")`, case-insensitive per the Shouldly message). | The script is not regressed and prints every decision. No script, header or docs change. |
| The arm's fake still injects a failure. | Fake at `DelegateScriptWorktreeBaseTests.cs:183-191` intercepts `["rev-parse","--verify","--quiet", "refs/heads/<branch>^{commit}"]`. Resolver on master: branch tips come from one `for-each-ref --format=%(objectname) %(refname)` call (`AgentTaskWorktreeBaseResolver.cs:442-447`); the only `rev-parse --verify --quiet` left is `CommitAsync(fallback)` for the target (`:132`, `:434`), i.e. `master^{commit}`. At `2ac7836a9^` the resolver called `CommitAsync($"refs/heads/{branch}")` per row and its `IOException` produced `Task {id} branch {branch} inspection is unknown (IOException).` then fell through to Target. | The hook is dead; the sibling resolves as a normal Continue source (`:315-320`), which prints no "unknown". Stale test, inherited since `2ac7836a9`. |
| CARD-0788 knew about this hook. | Its plan (`docs/superpowers/plans/2026-09-29-card-0788-...-plan.md:283-288`): "replace RefLookupFailureGit with a failed common-directory probe on that candidate's distinct linked checkout, preserving Target plus an unknown-row warning … An obsolete rev-parse hook that never fires is not green deadline/error coverage." Done in `AgentTaskWorktreeBaseResolverTests.cs:886-899` (common-dir fault keyed by checkout path, `Calls` asserted at `:615`) and `AgentTaskDispatchBaseGuardTests.cs:573-579` (`for-each-ref` batch fault). Its CP-3 filter (`:608`) covered only those classes. | Repair this class to the same standard: a row-local fault that is proven reached. |
| What the arm was meant to prove. | CARD-0442 plan V-15 (`2026-09-08-card-0442-worktree-continuity-plan.md:449`): "Within unknown_fallback, repeat hard Git error and controlled inspection timeout"; V-8 (`:442`): "Auto says inspection unknown/fallback". Current assertions: "unknown" (`:93`) and the source branch name (`:94`), plus the shared lines 69–74 and 101–107. | The repair must yield decision **Target** with a warning that names the candidate branch and says it is unknown, not a batch-level `Unknown` decision. |
| Where a row-local inspection error survives on master. | `AgentTaskWorktreeBaseResolver.cs:294-300`: a throwing `CheckoutSafeAsync` on a maximal tip adds `Task {id} branch {branch} inspection is unknown ({ex.GetType().Name}).`, removes the tip, and the loop ends at `:321` with `Result(Target)`. `CheckoutSafeAsync` (`:502-507`) throws `IOException("git_worktree_listing_failed")` when `["worktree","list","--porcelain"]` fails against `task.RepoPath`. That exact three-argument form is issued through `ILandingGit` only by the resolver; other callers use the four-argument `-z` form (`LandingGit.cs:204`, `GuardedWorktreeRemoval.cs:399`, `WorktreeManager.cs:1113,1124`). | Hook the listing command (D-3). A common-directory fault would need a distinct checkout because `SeedKeptSiblingAsync` sets `RepoPath = repo.Path` (`AgentTaskDispatchBaseGuardTests.cs:1586`) and `CommonAsync` caches per path (`:415-417`); the same path is probed first at `:131`, which would turn it into a batch-level `Unknown`. |
| The fixture reaches the listing call. | Sibling `A` is Succeeded with one commit past master (`SeedKeptSiblingAsync` `:1559-1597`); `continue` arm proves the path to `Continue` with real git. With the listing fault: eligible → single maximal tip → `CheckoutSafeAsync` throws → warning → `Target`. Script then prints `  base preview: Target at master; landing target master; reason ` and `WARNING: Task <id> branch feat/card-task-<id> inspection is unknown (IOException).` | Both current assertions hold (`unknown` matches `inspection is unknown`; the branch is in the warning). Add the `Target at master` line as the `fresh` arm already does (`:88`). |
| The create path persists the preview before any tick. | `AgentTaskService.cs:1506-1507` resolves the preview inside `CreateAsync`; `:1518` serialises it to `WorktreeBasePreviewJson`. Test asserts one request, `/api/agent-tasks`, `Queued`, null worktree/session, non-null preview JSON (`:71-72`, `:104-107`). | Unchanged assertions remain in the arm. |
| Both classes need special listing. | `tests/Antiphon.Tests/slow-tests-allowlist.txt:213` and `:242` already list them; `AgentTaskWorktreeBaseResolverTests` `:205`. The schema class is `[NotInParallel]`, `Integration`, `Slow`; the script class carries `ParallelLimiter<ProcessSpawnLimit>` and runs the real `pwsh` script (`DelegateScriptRunner.cs:37`). | No allowlist change. Serial rows. |
| Other migration tests carry the same stale pattern. | Only this file uses `migrations.Length - 1`. Siblings pin a predecessor or membership: `ChannelOutboundMigrationTests.cs:34-39`, `OutputDistillationMigrationTests.cs:30-35`, `SettlementSyncDebtSchemaTests.cs:29-31`. | Adopt the sibling pattern; no census widening. |
| Lane. | `runner-defaults`: `globalRunnerId=server2`, operator reason "default all work to server2". `session-runners`: `server2` draining and not accepting work; `server2-temp` Linux, available, dispatch-eligible, 4/10 occupied; `desktop` Windows 0/2. The CARD-1120 review ran both classes on the Linux mirror (Docker 27 Testcontainers PostgreSQL, `pwsh` present). | Portable Linux PostgreSQL lane with `pwsh`; omit `-Runner` and `-Platform`. Re-read the three routes when dispatching. |

Essential unedited receipts (from `/work/worktrees/task-1c540817/.antiphon/checkpoints/20261006-223628-f2d1/failures.md`, review SHA `4aef5c37`):

```text
ShouldAssertException: position
    should be
196
    but was
192
   at ...ChannelOutboundDurabilitySchemaTests.C519_Upgrade_preserves_ordinals_and_history() in .../ChannelOutboundDurabilitySchemaTests.cs:line 27

ShouldAssertException: run.Output
    should contain (case insensitive comparison)
"unknown"
    but was actually
"queued task 5758d44e (worker code on Frontier) - NO REPLY WILL BE ROUTED: read the result on the boa..."
   at ...DelegateScriptWorktreeBaseTests.RunArmAsync(...) in .../DelegateScriptWorktreeBaseTests.cs:line 93
```

## Decisions

- **D-1 — Item 1 is a stale pin; delete the is-last assertion only.** Remove
  `ChannelOutboundDurabilitySchemaTests.cs:27`. Keep lines 28–29 (membership and
  predecessor), the empty-database chain (33–35), the predecessor round trip
  (76–78) and every history assertion. Rejected: computing the expected index from
  the migration list (a second pin on the same fact, stale again at the next
  migration); pinning the successor name (stable but adds nothing the predecessor
  pin does not; the brief asks for the predecessor); any form of is-last check.
- **D-2 — Item 2 is a stale test fake, not a script regression.** `delegate.ps1`
  prints the preview for every decision and the warnings after it; the arm failed
  because its injected failure never happened. No change to `scripts/delegate.ps1`,
  the resolver, `AgentTaskService`, or any doc.
- **D-3 — Replace the dead hook with the row-local checkout-listing fault.** Rename
  `RefLookupFailureGit` to `WorktreeListingFailureGit(string repositoryPath)`; it
  returns `LandingGitResult(128, "", "injected worktree listing failure")` when
  `args is ["worktree", "list", "--porcelain"]` and `repository == repositoryPath`
  (`repo.Path`), and counts `Calls`. Rejected: (a) failing `for-each-ref`
  (`CandidateBatchFailureGit` shape) yields a batch-level `Unknown`/`git_inspection_error`
  with no branch in the output, loses the arm's row-local intent and duplicates
  `AgentTaskDispatchBaseGuardTests`' `git_inspection_error` arm; (b) a common-directory
  fault on a distinct linked checkout mirrors V-8, which the resolver tests already
  own, and needs an extra `worktree add` fixture; (c) loosening lines 93–94 or
  retargeting the arm at the Continue line hides the arm.
- **D-4 — Prove the fault fired and pin the fallback line.** In the
  `unknown_fallback` branch assert `fault.Calls.ShouldBeGreaterThan(0, "the
  worktree-listing fault must be reached")` after the arm, as
  `AgentTaskWorktreeBaseResolverTests.cs:615` does, and add
  `run.Output.ShouldContain("Target at master")` beside the existing `unknown`
  and branch assertions. Nothing is removed or weakened; the
  `inspection_timeout` sub-arm (lines 47–52, 96–99) is untouched.
- **D-5 — Portable lane, no fleet pin.** Live defaults on 2026-10-07 provide an
  available, dispatch-eligible Linux lane with Docker and `pwsh`; the schema class
  needs only Testcontainers PostgreSQL. Omit `-Runner` and `-Platform`; resolve
  placement again at dispatch.
- **D-6 — Closed ordinary scope, all rows serial, no whole-Unit.** Exactly the two
  repaired classes (4 + 4 results) plus two unchanged regression classes that own
  the contracts the repairs rely on: `SettlementSyncDebtSchemaTests` (1 result, the
  chain tail the round trip now crosses) and `AgentTaskWorktreeBaseResolverTests`
  (67 results, the row-local unknown-warning contract the new fault mirrors).
  Positive controls are designed here for Mutation, not run during Code.
- **D-7 — No docs sentence.** The preview contract users and orchestrators read is
  unchanged; `docs/orchestration-loop.md`, `AGENTS.md` and the script header do not
  describe the hook or the pin.
- **D-8 — A migration that will not round-trip is a separate defect.** If CP-1 fails
  inside `MigrateAsync` rather than at an assertion, retain the TRX and message,
  file a card naming the migration and operation, and end the slice red. Do not
  edit a migration, add a catch, or shorten the chain to go green.

These are code-supported implementation decisions; no human decision is required
to start Code.

## Implementation slices

### S1 — Drop the is-last pin (30–35 minutes)

Files: `tests/Antiphon.Tests/Application/ChannelOutboundDurabilitySchemaTests.cs` only.

1. Delete line 27 `position.ShouldBe(migrations.Length - 1);`. Keep lines 28–29.
   Optionally add a one-line comment above line 28 stating that the migration is
   pinned relative to its predecessor, never as the newest.
2. No other edit. Commit (`test(CARD-1127): pin ExtendChannelOutboundRecovery to its
   predecessor, not as newest`), push, then run CP-1 and CP-2 (`--after S1`).
3. Require exactly 4 results in CP-1 with the repaired method green, 1 in CP-2, no
   skips. Report the `CHECKPOINT` lines unedited. Apply D-8 on a migration failure.

### S2 — Replace the dead Git hook and prove it fires (35–45 minutes)

Files: `tests/Antiphon.Tests/Application/DelegateScriptWorktreeBaseTests.cs` only.

1. Replace the `RefLookupFailureGit` class (lines 183–191) with
   `WorktreeListingFailureGit(string repositoryPath)` per D-3, exposing
   `public int Calls { get; private set; }`.
2. At lines 44–46 construct it as a typed local
   (`WorktreeListingFailureGit? fault = scenario == "unknown_fallback" ? new(repo.Path) : null;`),
   pass it to `RunArmAsync` as today, and after that call assert
   `fault!.Calls.ShouldBeGreaterThan(0, ...)` inside the existing
   `if (scenario == "unknown_fallback")` block before the timeout sub-arm.
3. In `case "unknown_fallback":` (lines 92–95) add
   `run.Output.ShouldContain("Target at master");` and keep the two existing
   assertions. Do not touch the other cases or the shared assertions.
4. Commit (`test(CARD-1127): inject the unknown_fallback Git fault at the checkout
   listing the resolver still issues`), push, then run CP-3 and CP-4 (`--after S2`).
5. Require exactly 4 results in CP-3 (`continue`, `wait`, `fresh`,
   `unknown_fallback`) and 67 in CP-4, no failures or skips.

Each slice includes its own checkpoint run; delete the `bin-c1127-*` outputs
before finishing. Run `scripts/check-evidence-diff.ps1 -BaseRef
5b713f6855ee417737ff7d7e47cb340d66e3d9b8 -HeadRef <pushed sha>` before the final
report.

## Verification design

### Proves it works now

| ID | Behaviour | Method filter (exact) | Expected observation |
|---|---|---|---|
| V-1 | The migration is applied and sits directly after `CompletedCardWorktreeCleanup`, with the empty-database chain and the predecessor round trip intact. | `/*/*/ChannelOutboundDurabilitySchemaTests/C519_Upgrade_preserves_ordinals_and_history*` | Passes at master with 198 applied migrations; every legacy-row assertion (lines 80–154) still evaluated. |
| V-2 | The down/up round trip crosses the five successor migrations without data loss on the pinned columns. | same as V-1 | Same green; failure inside `MigrateAsync` is D-8, not a repair target. |
| V-3 | A row-local Git inspection error on the only candidate makes the real script print the Target fallback line plus a warning naming the branch as unknown, with one create request and a persisted preview. | `/*/*/DelegateScriptWorktreeBaseTests/T0442_V15_initial_post_prints_the_service_base_preview*` | 4 results; `unknown_fallback` output contains `base preview:`, `Target at master`, `unknown`, the branch; `Calls > 0`. |
| V-4 | The injected fault is actually reached by the resolver. | same as V-3 | `fault.Calls` assertion; an obsolete hook fails the arm instead of passing it. |

### Guards the regression

| ID | Class | Why it is rerun |
|---|---|---|
| R-1 | `ChannelOutboundDurabilitySchemaTests` (3 constraint methods) | Same file; same isolated-schema fixture and helpers. |
| R-2 | `SettlementSyncDebtSchemaTests` | Owns the newest migration the round trip now reverts and re-applies. |
| R-3 | `DelegateScriptWorktreeBaseTests` (`continue`, `wait`, `fresh`, timeout sub-arm) | Same method; shared relay and assertions. |
| R-4 | `AgentTaskWorktreeBaseResolverTests` | Owns the row-local unknown-warning and Target-fallback contract (`:294-300`, `:321`) the new fault mirrors; unchanged by this card. |

### Positive controls (Mutation stage; one method filter each, restore after each)

| PC | Production mutation (revert with `git checkout -- <file>`) | Filter | Expected red |
|---|---|---|---|
| PC-1 (V-1, migration missing by name) | `server/Migrations/20261004231302_ExtendChannelOutboundRecovery.Designer.cs:15`: change the id to `20261004231302_ExtendChannelOutboundRecoveryX`. | V-1 filter | `position should be greater than 0 but was -1` at the membership assertion. 1 executed, 1 failed. |
| PC-2 (V-1, wrong predecessor) | `server/Migrations/20261004011911_CompletedCardWorktreeCleanup.Designer.cs`: change its `[Migration]` id suffix to `..._CompletedCardWorktreeCleanupX`. | V-1 filter | predecessor assertion `should be "20261004011911_CompletedCardWorktreeCleanup" but was "...X"`. 1 executed, 1 failed. |
| PC-3 (V-3, preview line absent) | `scripts/delegate.ps1:1218-1219`: delete the else-branch `Write-Output`. | V-3 filter | `unknown_fallback` and `fresh` fail at `ShouldContain("base preview:")`; `continue`, `wait` green. 4 executed, 2 failed. |
| PC-4 (V-3, warnings absent) | `scripts/delegate.ps1:1221-1223`: delete the `WARNING:` loop. | V-3 filter | `unknown_fallback` fails at `ShouldContain("unknown")`; `fresh` fails at `Fresh worktree omits`. 4 executed, 2 failed. |
| PC-5 (V-4, resolver swallows the listing failure) | `server/Application/Services/AgentTaskWorktreeBaseResolver.cs:507`: replace `throw new IOException("git_worktree_listing_failed")` with `return true;`. | V-3 filter | `unknown_fallback` fails at `Target at master` or `unknown` (sibling resolves as Continue); `Calls` still > 0. 4 executed, 1 failed. |

A fixture or migration error during PC-1/PC-2 is not red; if the template
database refuses the renamed id, record it and keep the other control. Keep
each red-then-green pair method-scoped; never run the class or assembly as a
substitute. Negative evidence for the dead hook itself is the review receipt
above (the arm red at `4aef5c37` and `303dc7853` with the hook in place).

### Checkpoints

Exactly one isolated build and one filter per row; reuse rows name the building
row of the same `After` group. Counts are TUnit executed results at `eb1fe3407`:
`ChannelOutboundDurabilitySchemaTests` 4 (4 methods), `SettlementSyncDebtSchemaTests`
1, `DelegateScriptWorktreeBaseTests` 4 (1 method, 4 `[Arguments]` rows),
`AgentTaskWorktreeBaseResolverTests` 67 (16 methods). The repairs add no method or
argument row. Confirm the TRX roster equals the expected set, not merely `Min`.
Every row is PostgreSQL-backed and serial; the script row also spawns `pwsh`. The table
was validated at planning time with the checkpoint importer (`import --plan`, tool built
through `scripts/build-slot.ps1` to `bin-c1127-driver/`, output deleted afterwards):
4 rows imported, exit 0, no advisory warnings, no tests run.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1127-cp1/` | schema-s1 | `/*/*/ChannelOutboundDurabilitySchemaTests/*` | V-1, V-2, R-1 | exact 4 results including `C519_Upgrade_preserves_ordinals_and_history`, 0 failed/skipped | 4 | 6 | true |
| CP-2 | S1 | CP-1 | debt-s1 | `/*/*/SettlementSyncDebtSchemaTests/*` | R-2 | exact 1 result, 0 failed/skipped | 1 | 2 | true |
| CP-3 | S2 | `tests/Antiphon.Tests -> bin-c1127-cp3/` | script-s2 | `/*/*/DelegateScriptWorktreeBaseTests/*` | V-3, V-4, R-3 | exact 4 results (`continue`, `wait`, `fresh`, `unknown_fallback`), 0 failed/skipped | 4 | 5 | true |
| CP-4 | S2 | CP-3 | resolver-s2 | `/*/*/AgentTaskWorktreeBaseResolverTests/*` | R-4 | exact 67 results, 0 failed/skipped | 67 | 6 | true |

Run: `dotnet run --project tools/Antiphon.Checkpoints -- run --plan
docs/superpowers/plans/2026-10-07-card-1127-inherited-reds-plan.md --after S1
--expected-source-sha <sha>` after S1's commit and `--after S2` after S2's;
`wait` until the exit is not 75. Every build and run goes through the host build
slot (`run-checkpoint.ps1`/the tool take it themselves). No whole-Unit or
whole-assembly run; an unlisted run needs a stated reason.

### Cost

Estimates, not measurements. Ordinary Code floor = **19 minutes**, the sum of
`EstimatedMinutes` (S1 8, S2 11), each building row including about 2–3 minutes
of isolated build on the Linux mirror and 40 s of host start; tool bootstrap adds
about 2 minutes. Slot queue time is reported separately. Mutation: five
method-scoped controls at about 6 minutes each (incremental build into a
separate `bin-<mut>/`, one red run, restore, one green run) = **30 minutes**.

## Docs

None. The base-preview lines and warnings orchestrators read are unchanged, and
no document describes the test hook or the migration pin.

## Out of scope

- Any edit to `scripts/delegate.ps1`, the resolver, `AgentTaskService`, or a migration.
- Replacing the test's in-process relay or the shared `AgentTaskDispatchBaseGuardTests` helpers.
- A census of other dead Git hooks across the test suite; none was found by name
  (`RefLookupFailureGit` elsewhere already fails a live command).
