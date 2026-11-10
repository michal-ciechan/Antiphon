# CARD-1478 Final Review, round 1

CLEAN. No blocker. Positive controls stay pending for method-scoped Mutation.

Subject task `039f4d58-42d7-46d8-b14d-f6f92025e453`. Reviewed source `2ada90c455953e968404d9be28008677cca0f85b` on `feat/card-task-039f4d58`. Reviewed range `b8812bcb12ba309092007e216251284466181b5d..2ada90c455953e968404d9be28008677cca0f85b` (red witness `555f593cbe34f3665704f1d1ca8fd3206faf8db5`, fix `2ada90c455953e968404d9be28008677cca0f85b`). `origin/master` `f125755c1648533c473ce46311c307788b4a9b65` is not an ancestor of that tip. `git merge-tree --write-tree origin/master HEAD` exited 0 with tree `291dfc24b518a56e14fe8b5d18ea211492c8b166`.

## What the floor does

`Delegation:DesktopLaunchMemoryFloorMb` defaults to 0. The options validator rejects a negative value at startup (`must be zero or positive`). A non-numeric value fails integer binding and the host does not start. Nothing in appsettings turns the floor on.

When the floor is positive, `DesktopMemoryApplies` is a Worker that is not Check, Distill, or Diagnose and whose `RunnerId` is empty. `HoldBeforeDesktopMemoryClaimAsync` runs at the start of `DispatchOneAsync`, before auth, the repository lease, and the claim. Both the ordinary tick and ready-waiters call `DispatchOneAsync`. A predicted warm reuse skips that pre-claim hold. If reuse later falls through to a fresh process, the same floor is checked again before program resolve, the session row, and the launch sink; a hold there drops the lease and abandons the claim.

The reading is cached desktop `MemoryAvailableBytes`. Whole mebibytes are `bytes / (1024 * 1024)`. Null and negative byte counts are unknown, never zero. Windows swap fields are not used. Fresh means `now - sample.ObservedAt <= TimeSpan.FromMilliseconds(HostStatsSettings.StaleAfterMs)` (default 15000) on the same `TimeProvider` `HostStatsCache.Record` uses. Equality admits (`available >= floor`). `HostStatsCache.Latest` is locked and returns null when no sample exists. The admission object is stateless. The dispatcher is scoped, and each admission pass creates a new scope.

A hold leaves the task Queued. It is not Failed and not dropped. The detail is one deduped `Held` event:

`desktop_launch_memory: freshness=<fresh|stale|unknown> sampleAt=<O or unknown> availableMb=<n or unknown> floorMb=<n>`

A stale or missing sample, while the floor is on, says `availableMb=unknown` and holds. That is the card ("When enabled, stale/unknown observations hold as unknown, not zero") and `Unknown_or_stale_is_held_when_enabled`. The first transition logs Information `Task {ShortId} held: {Detail}`. After `DispatchHeldWarningSeconds` (300) and `DispatchHeldErrorSeconds` (900) the same hold is included in the aged-hold warning. The full admission pass retries every `Delegation:PollIntervalSeconds` (default 5, minimum 1). Ready-waiter signals do not register a memory hold and do not reset that deadline. An exception in the admission pass logs Warning `Delegation admission pass failed` and leaves the task Queued.

The gate does not cover:

- `AgentTaskKind.Orchestrator`
- Check, Distill, and Diagnose
- a task whose `RunnerId` is already set, including server2 placement chosen at create
- warm reuse of a live session
- a pinned AlwaysOn standing agent with no live session: the pre-claim prediction exempts it, and `PlaceOnStandingAgentAsync` returns `WaitForAgent` before the spawn recheck
- `AgentSupervisorHostedService`, which starts standing sessions on its own tick and does not call this gate

Desktop aliases are cleared to null before dispatch, so they are treated as local. `RequiredPlatform.Windows` is left as Windows with a null runner. The floor does not send that work to server2. An existing Working session is not re-dispatched and is not killed.

The operator turns it on by setting `Delegation:DesktopLaunchMemoryFloorMb` to a positive MiB while a fresh desktop host-stats sample is available. Zero restores the previous launch behavior.

## Receipts

Both checkpoint runs used source `2ada90c455953e968404d9be28008677cca0f85b`, `dirty=0`, `sourceState=clean`, `buildSource=verified`. `validate` accepted each report for that SHA. The tool reported no unlisted build or test.

| Run | Row | Result |
|---|---|---|
| `20261110-155147-72db` | CP-2 `DesktopLaunchMemoryAdmissionTests/*` | executed 8, passed 8, failed 0, skipped 0 |
| `20261110-155147-72db` | CP-3 nine-class guard union | executed 78, passed 78, failed 0, skipped 0 |
| `20261110-161410-5cd3` | CP-4 land-notification plus `TestLaneCategoryGuardTests` | executed 338, passed 338, failed 0, skipped 0 |
| `20261110-161410-5cd3` | CP-5 `C1149_C1150_Statement_budgets*` | executed 14, passed 14, failed 0, skipped 0 |

CP-2 named all eight methods, each Passed: `Low_memory_holds_before_launch`, `Equality_and_above_admit`, `Unknown_or_stale_is_held_when_enabled`, `Disabled_preserves_existing_behavior`, `Recovery_releases_once_and_deduplicates_hold`, `Remote_and_warm_reuse_are_unchanged`, `Ready_waiter_cannot_bypass_guard`, `Existing_sessions_and_platform_binding_are_unchanged`.

CP-1 was not a separate row. Its Expect text is the pre-fix red witness, and that same method passed inside CP-2. The red commit is `555f593cbe34f3665704f1d1ca8fd3206faf8db5`.

`scripts/impacted-classes.ps1 -Range b8812bcb12ba309092007e216251284466181b5d..2ada90c455953e968404d9be28008677cca0f85b` selected `land-notification`, `statement-budgets`, and `test-lane-category`. Those are CP-4 and CP-5, and `TestLaneCategoryGuardTests` is also inside CP-3. The migrations row did not match. Downgrade selector count: 0. The card diff has no `server/Migrations` file.

`scripts/check-evidence-diff.ps1 -BaseRef b8812bcb12ba309092007e216251284466181b5d -HeadRef 2ada90c455953e968404d9be28008677cca0f85b` printed `commits=2 entries=0 violations=0` and exited 0. The same script with base `f125755c1648533c473ce46311c307788b4a9b65` exited 2, `base_not_ancestor`, because that master commit is not an ancestor.

`scripts/check-os-witnesses.ps1` against base `f125755c1648533c473ce46311c307788b4a9b65` and this tip exited 0. Restart impact is not required. The two-dot tree diff against master is wider than the two card commits because master has moved; the inventory covers that path list. The memory-floor decision does not branch on OS.

The nine-class guard receipt is 78, which is the documented union. The Code report's 135 is not this receipt.

## Advisories

These do not hold the land.

- A new desktop Debug or Review worker is held when the floor is on. Both are non-specialist Workers, which is what the card calls a new local worker. The orchestrator kind, the three specialists, runner-bound work, warm reuse, and AlwaysOn supervisor relaunch are outside the gate. Setting the floor back to 0 admits again.
- Home's queue label for this hold is `queued — next dispatch tick`. `ClassOf` returns Other because the sentence has no `Held:` prefix, and the pipeline only maps Lease and RemotePrep out of the current hold text. The sample time, available MiB, floor, and freshness are the task timeline Held event. The card excluded a new UI.
- There is no startup test for a negative, huge, or non-numeric floor. Negative is off at both the validator and `floor > 0`. The tests seed Shared Docs tasks. A non-Shared workspace, including the default Worktree, takes the same pre-claim hold because the reuse prediction returns false.
- `LogAdmission` does not count `HeldOnMemory`. The per-task Information line does, on the first change of the detail.

## Positive controls

Pending. No mutant was applied. Each method asserts a launch count, a Queued or Dispatched status, and the hold text. Removing the pre-claim hold, admitting below the floor, treating unknown as zero, holding while the floor is 0, repeating the hold after recovery, applying the gate to a runner id or a warm session, skipping `DispatchOneAsync` on the ready path, or killing a Working session would fail the matching assertion.
