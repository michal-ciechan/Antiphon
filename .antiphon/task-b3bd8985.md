# CARD-1701 Code b3bd8985: witnesses for the Mutation f303b3ec survivors

All four requested witness groups added (test-only). Each goes red on its exact D-mutant and green on the restored code.

- Branch `feat/card-task-b3bd8985`, worktree `/work/worktrees/task-b3bd8985` (runner mirror). Original Code task (landing owner): b3bd8985.
- Base master `96882045e00c74f60f72a52aad51e47789b9d8a4`.
- Tested SHA (restored code, final runs): `4626c839bb796e306c43e9cac2439027082e25c6`.
- Commits:
  - `155a414b3a40f856463eb645029bb3567d0ac287`: witnesses.
  - `fcb13a392b7d36da9e78aab4bba6a78ae8de8f99`: the D7 helper reads the check cursor. A repeated Held row with the same reason is not rewritten, so `RowAsync` found no row on hold 2. This was a test bug, red on the baseline too.
  - `4626c839bb796e306c43e9cac2439027082e25c6`: the D7 witness asserts the 60 min backoff itself. An intermediate cursor-key assertion was removed so that the behavioural assertion is what goes red.
- Changed file: `tests/Antiphon.Tests/Application/C0824DesktopLegacyReleasePolicyTests.cs` (+ this report). No production change.
- Plan/evidence: the source of truth is the Mutation report `docs/investigations/2026-11-04-mutation-1701.md` on `feat/card-task-f303b3ec`. Generated evidence is in `.antiphon/mut1701w/` (gitignored). It holds the logs and TRX `tests/Antiphon.Tests/bin/Debug/net9.0/TestResults/c1701w-*.trx`. The TRX are deleted with bin/obj at cleanup; the counts below come from the unedited run logs.

## New test methods

They are placed after `Settled_missing_directory_is_never_re_checked`, so they are not inside `Server_predicates_keep_the_tree`.

- `Revision_suffixed_and_remote_tracking_start_refs_keep_the_tree(predicate)`: a queued dependent's `WorktreeBaseRequestedRef` keeps the tree before any git (`Held` / `dependent_open`, intact, branch kept, no release, no git). Cases:
  - `startref-queued-tilde` (`<branch>~0`): D3.
  - `startref-queued-upstream` (`<branch>@{u}`): D5.
  - `startref-queued-reflog` (`<branch>@{0}`): D5.
  - `startref-queued-refs-remotes` (`refs/remotes/origin/<branch>`): D6.
- `Filed_release_resets_an_earlier_hold`: D7. The tree is held with `source_dirty` at 60, then 120 min. It is then cleaned and filed with the removal deferred (`repository_lease_required`, harness lease held, `Filed == 1`). It is dirtied again, and the next hold must back off 60 min.
- D4 (`startref-queued-colon`): SKIPPED. `<branch>:` names a tree, and `git worktree add` cannot start from it, so no realistic dependent start is protected.
- D8: not duplicated. It is covered by `feat/card-task-e23c0f83`.
- D9: equivalent, left alone.

## Red/green per witness

Method: env-gated mutants (`C1701_MUT=<id>` through a temporary private `Mx(id)` helper), applied by an exact-path edit to `server/Application/Services/DesktopLegacyReleasePolicyService.cs`. The mutants match the report:

- D3: the cut set without `~`.
- D5: the `@{` cut skipped.
- D6: `refs/remotes/origin/` skipped in the prefix loop.
- D7: the post-filing `RecordCheck(... "passed" ...)` skipped.

The file was restored with `git checkout -- /work/worktrees/task-b3bd8985/server/Application/Services/DesktopLegacyReleasePolicyService.cs`. After that, `git diff --quiet HEAD` passed, and the rebuilt `Antiphon.Server.dll` contains no `C1701_MUT` string (UTF-16 or ASCII).

| Witness | Mutant run (filter, counts) | Red assertion | Unmutated, same build | Restored rebuild |
|---|---|---|---|---|
| tilde | D3, `Revision_suffixed*`: total 4, failed 1 (tilde only) | `row.Outcome` should be `Held` | green (BASE run 4/4) | green |
| upstream, reflog | D5, `Revision_suffixed*`: total 4, failed 2 (upstream + reflog) | `row.Outcome` should be `Held` | green | green |
| refs-remotes | D6, `Revision_suffixed*`: total 4, failed 1 (refs-remotes only) | `row.Outcome` should be `Held` | green | green |
| Filed_release_resets_an_earlier_hold | D7, `Filed_release*`: total 1, failed 1 (at `4626c839`'s build) | `ShouldBe(60)` but was `240` | green (BASE3 1/1) | green |

## Final runs (restored code, SHA `4626c839b`, one lease each)

| Run | Filter | Result | slot |
|---|---|---|---|
| restored build | `dotnet build tests/Antiphon.Tests -p:UseAppHost=false` | succeeded | waited=0s, held=210s |
| C0824DesktopLegacyReleasePolicyTests | `/*/*/C0824DesktopLegacyReleasePolicyTests*/*` | total 104, failed 0, skipped 0 (99 before + 5 new) | waited=0s, held=110s |
| C0824DesktopLegacyReleaseSettingsTests | `/*/*/C0824DesktopLegacyReleaseSettingsTests*/*` | total 29, failed 0, skipped 0 | waited=0s, held=6s |

All other leases also had waited=0s: three mutant builds (199, 156 and 229 s) and eight mutant/baseline runs.

## Unlisted builds and runs

There was no plan `### Checkpoints` table; the brief named the runs. Everything was done directly through `scripts/build-slot.ps1`, and the checkpoint tool was not used.

Three mutant builds were needed: the first one, then one after each D7 test correction. Each build was followed by its mutant and baseline runs. The first BASE run had the D7 witness red on unmutated code: `RowAsync` Single found no row on hold 2. That was a test bug, fixed in `fcb13a392`, and not a product defect.

The Unit lane was not run. The brief limited the scope to the two named classes, for a test-only, single-file change.

## Gates

- `scripts/check-evidence-diff.ps1 -Repository . -BaseRef 96882045e... -HeadRef HEAD`: commits=3 entries=0 violations=0, exit 0, before this report commit. It is rerun after the report commit, as noted in the final message.
- `git merge-tree --write-tree origin/master HEAD` (master `4c12963577c8b9bca176d76609f942475237cfc5`): clean.
- `git merge-tree --write-tree origin/feat/card-task-e23c0f83 HEAD` (`8c4040aaa2ff8ad178f959c3d0d06231a7eb652a`): clean.
- A scratch merge of master and e23c0f83, then this HEAD merged onto it: clean.

## Pending for Mutation

There are no new PCs. The witnesses' red proofs above are this task's own D3/D5/D6/D7 mutants, so SourceLanding Mutation can treat them as re-killed.
