# Review f1ddbabd: PhoneHome round 4 (task 5374167e), same-target repeats and the cancelled read

Outcome: one defect, D-1. The round-3 D-1 and D-2 scenarios are fixed. The 3 new witnesses and all 15 channel tests pass at `d486e5641`. But the same-target loss has a second path that round 4 did not close. A scratch witness reproduced it, red at the tip. By code reading it is a regression against rc base `34a7d0b8`, and it has been there since round 2 (`56f64a2bf`).

Scope is Interim. The whole Unit lane was not run, and most of the brief's narrow rows did not run either (see Runs). This review cannot approve a land.

Subject: task `5374167e`, `feat/card-task-5374167e`, fix `d486e5641622543a1af7793001e31b28ee142bc4`, witnesses `3baeb279a`. Reviewed on server2/Linux in `/work/worktrees/task-f1ddbabd` at HEAD `d486e5641`. The tree was clean apart from a scratch test, which has been removed.

## D-1 (defect, regression vs rc base): after the debounce queues a partial, a second same-target line in the next chunk is dropped

- **Where:**
  - `server/Application/Services/AgentMentionRouter.cs:88-89`: `MentionsToEnqueue(..., skipPendingDuplicate: true)` runs for **every** completed line, and the pending state is still present.
  - `:310-316`: the `already` snapshot also applies to every completed line.
  - `:136-137`: the state is removed only after all lines.
  - The key is the target only (`:440`).
- **Failure:**
  1. `@helper please inspect` arrives, and the source pauses for at least 300 ms.
  2. The debounce queues the partial. `RoutedKeys` = {helper}.
  3. The next chunk is ` it\n@helper then run lint\n`.
  4. Line 1 is skipped as the duplicate of the partial, which is correct. Line 2, `@helper then run lint`, is a new message, but it is also skipped.
  5. Trail: `command-skipped-duplicate target=helper` x2, then `pending-cleared`. One command is enqueued in total, so `then run lint` is never routed.
- **Base:** `34a7d0b8` and round 1 `0dc30f5e1` did `_pendingStates.TryRemove` inside the per-line loop (base lines 61-65). Only the first completed line was deduplicated, so line 2 routed. Round 2 moved the removal after the read. Round 3's claim and round 4's `already` copy kept the dedupe applied to every line.
- **Witness:**
  - Scratch `R1ddb_debounced_partial_then_second_same_target_line_routes`. It uses the router directly with diagnostics and the real 300 ms debounce.
  - Red at `d486e5641`: `CommandEnqueued should be 2 but was 1`.
  - Not run at base. The base-green claim rests on the code reading above.
- **Why:** a legitimate mention is silently lost on ordinary pty pacing (a pause mid-line). Brief priority (1) is no loss.
- **Fix (for Code):** dedupe against the debounce's keys only for mentions on the **first** completed line. That is `lines[0]`, the line the pending text continues. Do this in both places:
  - pass `skipPendingDuplicate: i == 0`;
  - make `ClaimCompletedMentions` skip only first-line mentions whose target is in `RoutedKeys`.

  All later lines always queue. Add a committed witness for this scenario (fake clock), red at `d486e5641`.

## Design note (the brief asked)

Four rounds have each produced a new edge case from the same two roots:

- a target-only key that stands in for "the partial already delivered";
- a per-state `Version` that restarts at 1 whenever a state is recreated (see A-1).

Recommendation: simplify rather than patch again.

- The debounce dedupe applies only to the continuation line (`lines[0]`).
- The version comes from a router-wide monotonic counter (`Interlocked.Increment`).
- Everything else is enqueued.

## Checked and sound at d486e5641

- **Round-3 D-1 (two same-target lines after a partial with no debounce fire):** fixed. The `already` copy no longer dedupes within the batch. Witnesses `Same_target_mentions_*` (2) are green.
- **Round-3 D-2 (cancelled read):**
  - Fixed. The cancelled read calls `Settle(null)`, which enqueues with no stamp, removes the claimed state, stores the remainder and schedules the debounce, then rethrows.
  - The debounce for the claimed version is stale, so the line is delivered once. A later same-target mention routes.
  - The non-cancellation read exceptions take the general catch and the same null-stamp `Settle`, so none is swallowed silently: each is logged as a warning.
  - Note: `AgentSessionRuntime.ObserveOutputAsync:140-151` catches every exception from the router, OCE included, and logs it. The rethrow therefore reaches the runtime, not the pump. The pump sees the cancellation at `PublishToGroupAsync(ct)`. This is pre-existing and harmless.
- **Enqueue throw:** unreachable in practice.
  - `TryWrite` does not throw, and `MentionRouteDiagnostics.Record` swallows its own exceptions (`MentionRouteDiagnostics.cs:42-53`).
  - Only a throwing logger could reach `ReleaseUnqueuedClaim`. On the 1st-of-2 same-target case the key is kept, so the second mention is lost rather than double-delivered.
  - When no pending state exists, a throw leaves `_pendingText` with the full text, and the next delta re-routes the already-queued mentions (double). That is pre-existing.
  - Acceptable given it is unreachable.
- **Channel full / disposed:** the router logs `CommandDropped` plus a warning. `Settle` then removes the state and the remainder replaces the text, so the mention does not re-route. This is the same as before the change (bound 1024).
- **Shutdown while Settle runs:** `TryWrite` fails after `TryComplete`, so the command is dropped and logged. A debounce scheduled after `Dispose` touches a disposed `_cts.Token`, and that `ObjectDisposedException` lands in a discarded task. Both are pre-existing.
- **Debounce vs claim, both orders:** unchanged from round 3 and sound. The version bump under `Gate` makes the debounce stale at either check, and if the debounce wins, its key makes the claim skip the continuation line. No lock is held across an await.
- **Memory:** `RoutedKeys` lives per pending state and is dropped when the next completed line removes the state. `_pendingStates` and `_pendingText` are not cleared on source exit. That is pre-existing and bounded (round-3 A-1).
- **Round 2/3 properties:** unchanged, and their witnesses are green in the class run:
  - the observe-time stamp;
  - claim-before-read;
  - a failed or not-live read still delivers once;
  - the flush runs off the worker;
  - the held transcript confirm.
  - The original debounce purpose (an unfinished line is delivered once after 300 ms) also holds.
- **Witness determinism:** the new witnesses use the internal `Clock` seam with a `FakeTimeProvider`, and the cancel is a synchronous `cts.Cancel()` inside `capture`. Bounded waits only (`WaitUntilAsync`, 20 s cap).

## Advisories

- **A-1 (pre-existing, reproduced): the debounce version is reused, so a stale debounce delivers a fresh partial early and truncated**
  - Where: `SchedulePendingMentions:220-225` creates a new state at `Version` 1 after `Settle` removes the old one. The old state's v1 debounce then matches it at `:245-254`.
  - Scratch `R1ddb_old_debounce_does_not_queue_a_fresh_remainder_early`, red at tip. The trail runs:
    1. `Status: work` (v1, debounce at +17).
    2. At +200, `ing\n@helper second partial`: a new state v1, debounce re-armed.
    3. At +327 the **old** v1 debounce fires and queues `second partial` (14 chars), 122 ms after the text appeared.
    4. The completed line ` continues to the end\n` is then `command-skipped-duplicate`.
  - Result: the recipient gets the truncated message once. The base shows the same pattern by code reading.
  - Fix together with D-1: use a router-wide monotonic version.
- **A-2:** the `for … TryRemove` loop at `:136-137` is redundant; one removal is enough (carried from round 3).
- **A-3:** `git merge-tree --write-tree origin/master d486e5641` exits 1. These are the conflicts carried from the `c790cd94` history. The rc port and the landing need a decision; this is not a code change here.
- **A-4:** I did not rerun the author's witnesses at round-3 tip `261e35c82`. The claim that they are red there rests on the author's report. The scratch D-2-shaped scenario was red there in review 77d54926.

## Runs (tip d486e5641, server2/Linux, build-slot, no checkpoint-tool receipts)

The host was saturated: each slot wait was 10.5-12.5 min.

| Run | Lease | Result |
|---|---|---|
| Build `tests/Antiphon.Tests`, `OutputPath=bin-r1ddb/`, `UseAppHost=false` | `2cfcfc7b-4069-4148-819c-10499220f811` | 0 errors |
| `(R1ddbScratchTests*)\|(AgentChannelServiceIntegrationTests*)`, `dotnet exec --no-build` dll | `1999cae9-aba2-42c9-ad51-6a62eb5b1fed` | total 17, failed 2 (both scratch witnesses, as intended), succeeded 15 (the whole channel class, including the 3 new witnesses) |
| `scripts/check-evidence-diff.ps1 -BaseRef 261e35c82 -HeadRef d486e5641` | n/a | commits=2, entries=0, violations=0 |

**Not run:**

- the whole Unit lane;
- `PhoneHomeBackpressureTests` (10 alone, plus 5 under burners);
- the PhoneHome* neighbours;
- `RcRosterRepairTests`;
- the census cases C1404, C1577, C1584, C1410 and G2865;
- the base-commit D-1 witness.

They were skipped because of the 40-minute budget and the slot queue. A defect sends this back to Code anyway, so the next Review must run them. `PhoneHomeMirrorJoinTests.C1032_Join*` (6) are the known pre-existing Linux reds and were not exercised.

**Cleanup:**

- The scratch test was removed by its literal path.
- 32 `bin-r1ddb` output directories under `/work/worktrees/task-f1ddbabd` are left in place, per the brief's cleanup rule. They are gitignored.
