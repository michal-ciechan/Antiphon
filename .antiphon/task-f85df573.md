# Review f85df573: C508 round 2 (7f0711ca) and C540 (12ffcf04)

I found no defects in either subject. A and B compose cleanly. Scope is **Interim**: I ran only the classes the brief names, not the whole Unit lane. The two E2E classes are Windows-gated, so I compiled them but did not run them. **A native Windows run of DispatchBaseWarningDeliveryE2ETests (C508 x2, C540 claim and projection cuts) is still pending.**

- Subject A: 7f0711ca-03fa-402a-8b0c-9f25eb71896b at `98351f13f2d3aeab3d7ae4fae34df8d65c06fc72` (d461d3044 and 98351f13f on top of e5f9fef33).
- Subject B: 12ffcf04-2fe9-4281-9445-dec726db4fab at `13a8d32719a70bcd14be592f8fc5ef6a497b5ce4` (parent e5f9fef33).

## Weakened-test and hidden-regression analysis

### (1)/(2) A: does the retargeted scenario produce exactly two intents and one delivered prompt?

**The product writes both intents.** `server/Application/Services/AgentTaskDispatcher.cs:6388-6405` (`BuildDispatchWarningDrafts`) adds two drafts:
- `MismatchKey` (`base-observation-stale`) when `observation.ObservedBaseRef` differs from `claimed.WorktreeBaseRef`.
- `DefaultUnresolvedKey` (`default-unresolved`) when `decision.NewlyRecorded` and `Resolved.UnresolvedDefault` is set.

Both are independent `if` branches, so the mismatch intent persists when the default is unresolved. Both keys are real product constants at `DispatchBaseNotificationPayload.cs:28-29`. The detail strings are `MismatchDetail` (`:94-95`, which names both observed `trunk` and actual `HEAD`) and `DefaultUnresolvedDetail` (`:97-98`).

**The lease edit and the branch delete take different paths.** CARD-1428 revalidates only the `Project.BaseBranch` string under the lease. Deleting the `trunk` branch leaves that string unchanged, so the claim is not refused. Under the lease, the cut then re-resolves `trunk`, cannot find it, and falls back to HEAD/RepoHead.

**The Linux witness uses the real delivery path.** `DispatchBaseNotificationTests.Coverage.cs:487` (`C508_VanishedDefaultWarningReachesCaller`, 2 rows) uses:
- the real dispatcher (`TickInspectedAsync`) and real PostgreSQL;
- the real `AgentTaskLandNotificationService.ReconcileAsync`;
- the real `SessionMessageQueueService`: `BridgeQueueHarness.cs:47,183` resolves the production service, whose `DeliverNextLockedAsync` is `SessionMessageQueueService.cs:1663`.

It asserts the intents' own `Body` with `PromptSubmissionMatch.IsCompleteIn` on transcript UserPrompt rows and on adapter submissions. That is the same predicate and the same body `AssertDispatchReceiptsAsync` uses in the E2E (`LandDeliveryFixture.Dispatch.cs:383-385`).

**The E2E checks producer to recipient.** `AssertDispatchReceiptsAsync` checks:
- notification ids equal the intent `NotificationId`s;
- each body appears exactly once in both the transcript UserPrompt and the native `updates.jsonl` `user_message_chunk`;
- exactly one queue row per notification;
- the total `[dispatch-base` prompt count equals 2.

The busy variant is stronger than the pre-CARD-1349 original (`91b45006b`). It asserts 2 Pending rows with 0 attempts, 2 unconfirmed notes, and zero transcript/native prompts before `ReleaseBusyAsync`.

**The hook is a faithful stand-in for a vanishing default.** The fixture repository is `git init -b master` (`LandDeliveryFixture.cs:65`). `trunk` is created from master and is never checked out anywhere, so `git branch -D trunk` succeeds on Windows and Linux alike.

**A hook failure is not silent.**
- The hook records `exit` and `stderr` in `delete-default-on-lease.fired.json`, then throws `IOException` on a non-zero exit (`LandDeliveryOptions.cs:153-156`).
- After a throw, `_fired` is already 1, so the next tick cuts from `trunk` with zero intents. The wait predicate (HEAD + RepoHead + 2 intents) can never hold, so the test goes red on timeout or on the Failed assertion. It cannot pass green.
- The routing works: `TryAcquirePreflightAsync` and the owner-tag overload reach the decorator through default interface methods (`IRepositoryMutationLease.cs:7-21`).
- `EvidenceGit.DropCachedReads` clears the volatile caches. Stable reads cover only `--git-common-dir`, `check-ref-format` and `remote get-url`, so no cached `trunk` resolution survives.

### (3) B: can the relaxed C540 predicate still fail?

**The id check is exact.** A projection Warning always carries the intent id:
- `DispatchBaseWarningIntentService.cs:48-55`: intent `Id = payload.WarningEventId`.
- `:114` and `:123`: materialization writes `WarningEvent(intent)` with that id.

So any leaked projection at the claim or projection cut has `Id ∈ intentIds` and fails the cut, whatever its text. The detail list adds a text-based backstop. `MaterializedAt == null` is still asserted, and the notification count must be 0.

Counter-examples:
- **Projection leaked with the intent id:** red (id match).
- **Duplicate projection with a fresh id and sibling or mismatch text:** red (detail match).
- **Duplicate or lost delivery after recovery:** red in the unchanged `AssertDispatchReceiptsAsync`, which requires exactly one complete prompt per original intent and the same intent identities.

**The witness can go red.** The 15 `DispatchCrashWarningTests` cases pin both directions of the classifier: 1 id case, 7 detail cases, 6 non-projection cases and 1 format case. A constant `false` fails 8 cases; a constant `true` fails 6.

**Is "obsolete assertion" the right call?** It is plausible but unproven; see A-2. CARD-1428 (`c00e3d1da`) adds no Warning writer. The claim-time Warning writers that can coexist with open intents are all non-projection, and none reaches the caller as a `[dispatch-base` prompt:
- quota (`AgentTaskDispatcher.cs:5685-5694`, Grok);
- baseline capture `progress=unavailable; reason=…` (`:5951-5966`, `:7810`, Code role);
- scope overlap (`:1068-1080`);
- capacity consume (`:8237-8252`);
- launch-enqueue refusal (`:6245`).

A mismatch-text Warning without a matching intent cannot exist, because the projection is the only writer of that text. If the leftover were a mismatch or sibling warning, B's predicate would fail it.

### (4) Composition

`git cherry-pick 13a8d3271` onto `98351f13f` has **zero conflicts** (tree `ba5f9ca5e`). A does not touch `tests/Shared/DispatchWaitDiagnostic.cs`, contrary to the brief. In `DispatchBaseWarningDeliveryE2ETests.cs`, A edits the C508 region and B edits the C540 region plus a `using`.

- Antiphon.Tests and Antiphon.E2E both compile with 0 errors on the composition. Antiphon.E2E also compiles with 0 errors on A alone and on B alone.
- All named Linux tests pass on the composition (counts below).
- Pick simulation of e5f9fef33, d461d3044, 98351f13f and 13a8d3271 onto `origin/master` `f8bddb6e`: all clean.
- `git merge-tree --write-tree` of A, B and the composition onto `rc/2026-10-27` `cf1a12403`: all clean.

### (5) Names, roster and plan

- No renames. `scripts/` is untouched. Roster `c1584-e2e-source-roster.txt:97-109` still pins both C508 methods and all eleven C540 methods.
- PC-50 and V-16 text (plan `:1394`, `:1501`, `:2190`, `:2430`, `:2701`, Amendment E) correctly names the vanished-trunk producer, both E2E methods, the DBN witness, and the zero-stale DG half.

### (6) Diagnostics

`DispatchWaitDiagnostic.FormatWarnings` prints only task-owned Warning ids and details, flattened and capped at 700 characters. The standing-agent env warning lists variable names only (`AgentTaskDispatcher.cs:8690`). Nothing sensitive is printed.

## Defects

None.

## Advisories

- **A-1 (pre-existing, not caused by A or B): PC-49 has no killing test.** Since CARD-1349 S2, `DG.C508_GuardRefMismatchWarnedOnce` has no mismatch-producing row: rows (0..2, true) and (1, false) all expect zero `base-observation-stale` intents (`AgentTaskDispatchBaseGuardTests.cs:1353-1430`).
  - PC-49 ("add a second pair when the claim has more than one sibling intent") needs a mismatch together with at least 2 siblings. The new vanished-default witnesses have zero siblings, so the PC-49 mutant survives everywhere.
  - PC-48 is still killable, but by the equal-ref half; its text "unequal-ref row … one mismatch event" is stale.
  - Suggested fix: file a card for a vanished-default DG row with 2 divergent siblings (one mismatch, N+1 notes), then re-point PC-48 and PC-49 at it.
- **A-2: the identity of the Windows leftover Warning is unconfirmed.** B prints `C540 crash-cut warnings at <boundary>: …`. The pending native run must show a non-projection detail (quota, `progress=unavailable`, overlap or capacity) before "obsolete assertion" counts as confirmed.
- **A-3: the timeout diagnostic hides a failed hook.** `DispatchTimeoutAsync` reports `hookFired=true` even when `git branch -D` failed (`LandDeliveryFixture.Dispatch.cs:197-198`). The exit code and stderr are only in `delete-default-on-lease.fired.json`, and a failed hook surfaces only as a timeout. Consider adding the exit code to the snapshot.
- **A-4: the hook fires on the first lease acquisition in the process.** That matches `LeaseHook` in the unit twin. If a non-dispatch lease ever ran first, the failure mode is red, not a false green.

## Runs (server2/Linux, build slot, `UseAppHost=false`, `OutputPath=bin-f85df573/`)

**Builds** (0 errors each):

| Build | Tree | Lease |
|---|---|---|
| Antiphon.Tests | composition `f0c7ff7f9` | `76e7404a` |
| Antiphon.E2E | composition | `55b99d86` |
| Antiphon.E2E | A, then B | `7bd1dfea` |

**Test runs** (`dotnet exec --no-build`, composition):

| Lease | Classes | Total | Passed | Failed | Skipped |
|---|---|---|---|---|---|
| `d2435fed` | DispatchBaseNotificationTests, AgentTaskDispatchBaseGuardTests, DispatchCrashWarningTests | 169 | 169 | 0 | 0 |
| `db57c5f5` | ProgressAwareWaitTests, LandDeliveryWaitRoutingTests, RcRosterRepairTests, WindowsUnitPlatformAdmissionTests, C1514LandingSplitBudgetTests | 25 | 24 | 0 | 1 |

- The first run includes `C508_VanishedDefaultWarningReachesCaller(False)` (11.5 s) and `(True)` (2.8 s), all 15 DispatchCrashWarningTests cases, and `C508_GuardRefMismatchWarnedOnce` x4.
- In the second run, the C1404, C1577, C1584, C1410 and G2865 census cases and `C1210_Roster_closure` passed. The one skip is `C1210_Windows_linux_only_outcomes_are_exact`, which needs Windows placement.

**Other checks:**
- `check-evidence-diff.ps1` from 91b45006b: A commits=3 violations=0; B commits=2 violations=0.

**Not run:**
- Native Windows E2E (pending).
- The full Unit lane (Interim).
- All PCs (pending for SourceLanding Mutation).

**Cleanup:** I removed 16 `bin-f85df573` directories in the review worktree by literal path. I removed scratch worktrees `comp`, `b` and `m` with `git worktree remove --force` on literal scratch paths. The review worktree has no remaining changes besides this report. I fixed nothing.
