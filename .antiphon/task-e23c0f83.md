# CARD-0824 S4a survivor witnesses (task e23c0f83)

Test-only. Tested SHA `946c4e4a1f14abec0ec7ef984b5770c698d3ce1a` on `feat/card-task-e23c0f83` (base `5449f8f50b9272d6ea468920725724ec88c56d68`).
Files: `tests/Antiphon.Tests/Application/C0824DesktopLegacyReleasePolicyTests.cs`, `tests/Antiphon.Tests/Application/C0824DesktopLegacyReleaseSettingsTests.cs`. No production change.

## Red/green per witness

Method: the sweep's own gated exact-string edits (e5258e8c `edits.js`) applied to the three production files, plus an
untracked `server/MutationSwitch.cs`. One mutant build, one lease per mutant run (`ANTIPHON_MUT=<id>`, method-scoped
filter). Restored with `git checkout -- <exact path>` and the switch deleted. The PC-52 and the relocated PC-21e/X-13b
witnesses were rebuilt and re-run red.

| Survivor | Witness (LP unless LS) | Red (mutant) | Green (restored) |
|---|---|---|---|
| X-26 detached_head | `Detached_head_tree_is_never_released(before-filing / after-filing)` | 2/2 red: Outcome Refused, but was **Removed** | green |
| PC-21e refs/heads/ | `Full_ref_dependents_and_event_linked_sessions_keep_the_tree(startref-queued-refs-heads / startref-open-refs-heads)` | 2/3 red (Held, but was Removed) | green |
| X-13b event-linked session | same method `(live-session-event-linked)` | 1/3 red (Held, but was Removed) | green |
| M-2 pre-act re-read | `Fact_that_appears_after_selection_holds_the_tree(late-sync-debt / late-dependent / late-path-shared / late-slot-lease)` | 4/4 red (3 Removed; path-shared Held `identity_ambiguous`, not `path_shared`) | green |
| X-11 DirectChildOfRoot | `Only_ordinary_card_task_leaves_are_candidates(nested-under-root / outside-root-normalized)` | 2/11 red (nested: **Removed**) | green |
| PC-52 reopen any caller | `Caller_filed_release_refused_with_nothing_moved_is_not_reopened` | red (Residue slot_spent, but was null: removed) | green |
| PC-58 backed-off consume cap | `Backed_off_rows_ahead_of_an_eligible_tree_consume_no_cap` | red (eligible tree not removed) | green |
| M-5 newest-first retries | `Due_rechecks_go_least_recently_checked_first` | red (A not removed) | green |
| PC-11 `<=` | `Age_floor_is_48_hours(48h)` (clock on a whole second) + new `(48h-minus-1us)` | 1/4 red (48h) | green |
| X-18 unreadable retention | LS `Unreadable_retention_setting_counts_as_listed(listed / unlisted / unreadable)` | 1/3 red (unreadable) | green |
| T-D-4 retry | `Reopened_attempt_re_proves_a_tree_dirtied_after_the_refusal` | red under PC-29 (untracked-ignoring status): row2 not Refused | green |

Mutant runs waited 0-120 s for a slot.

## Final run (restored, clean build at 946c4e4a)

Filter `/*/*/(C0824DesktopLegacyReleaseSettingsTests*)|(C0824DesktopLegacyReleasePolicyTests*)|(TaskWorktreeRetirementTests*)|(WorktreeResidueSweepTests*)/*`:
**194 total, 194 passed, 0 failed, 0 skipped** (TRX per class: LS 32, LP 107, TaskWorktreeRetirementTests 40,
WorktreeResidueSweepTests 15; the sweep's 175 plus 19 new cases). Lease waited 15 s, held 131 s.
The first final run at 46b24a36 was also 194/194.

Not run: the whole Unit lane and the Native (Windows-only, PC-66..68) class. The brief scoped this task to the S4a
classes and nothing may run on the desktop.

## Findings

- **PC-52 masking in the old witness:** `Caller_filed_release_keeps_one_command` (and my first version) retries
  `TryRetireAsync` in the SAME scope. The tracked attempt keeps `CommandIntentId == null` in memory because
  `TryCommitIntentAsync` writes through the command store, so the reopen condition never fires and
  `TryCommitIntentAsync` then refuses `cleanup_command_slot_spent`. A real retry (a new request or tick) is a fresh
  scope. The new witness retries in a fresh scope.
- **X-11:** with `DirectChildOfRoot` removed, a tree nested under the root (`trees/nested/card-task-<id8>`) is
  **removed**. The guarded removal's confinement does not refuse a nested path, so the policy check is its only guard
  (now witnessed). This is not a product defect at base.
- **M-2 path-shared:** under the mutant, the service's identity check holds a shared path as `identity_ambiguous`.
  That is safe, but the reason belongs to the wrong owner. Sync debt, dependents and slot leases are guarded only by
  the re-read.
- **Equivalence:** I claimed none. Every assigned survivor now has a killing witness.

## Reflog-only commits (not implemented: design decision)

Can it occur with Antiphon's own flow? **Yes.**
1. The land itself rebases in a separate detached land worktree (`AgentTaskLandingProtocol.cs:264-270`,
   `LandWorkspace.cs:85-92`), so it does not create the case.
2. The land source resolver's adopt/resume path runs `reset --hard <expected>` **in the task worktree**
   (`AgentTaskLandSourceResolver.cs:349`, `:490`). It moves the task branch to the expected source, and the prior
   local tip survives only in the worktree/branch reflogs.
3. Agents create it too. git-common allows amending or resetting an *unpushed* commit, and the harness recommends
   "a temporary WIP commit" instead of a stash. A later `reset --hard` leaves a clean, pushed, at-tip tree whose
   discarded work lives only in its reflog.

S4a then removes the tree and drops both reflogs.

Recommended direction: have the pre-check run a read-only reflog census of the worktree `HEAD` and the branch. It
should hold (`reflog_unique_commits`) any tree that has a reflog commit not reachable from a ref or from origin.
Alternatively, copy `logs/HEAD` and the branch reflog into the retained-evidence root before the guarded removal, so
nothing becomes unrecoverable.

## Gates

- `scripts/check-evidence-diff.ps1 -Repository . -BaseRef 5449f8f50 -HeadRef HEAD`: commits=3 entries=0 violations=0.
- `git merge-tree --write-tree origin/master HEAD` (master `96882045e`): clean. The first cut conflicted with CARD-1701's
  new `Server_predicates_keep_the_tree` cases, so the PC-21e/X-13b cases moved to their own method.
  CARD-1701 replaced `BranchName` with `NamesBranch`/`BranchPrefixes` on master. The refs/heads/ witness still targets
  that prefix, which is in master's list.
- Deleted all bin/obj outputs. Every mutated file was restored. The tracked tree is clean.
