# Review ff7ab869 (round 1 of 3): Debug 52410842, S4a status_untrusted hold + core.longpaths pre-check

**Verdict: CLEAN.** No D-n. Tested SHA `29e6816ceec487f1c1dbdb48a144d309030b747a`; branch tip
`5233faefdedd2d024fcbbabeb02e1e9146b03a70` adds only `.antiphon/task-52410842.md` (verified diff). Subject task
`52410842-b68f-4817-8c5b-ec887e532748`, base `84e19071e25abc7a595fddfac145b187dc5f2d11`.

## Runs (server2, Linux, uid 1654 not root; one build-slot lease per build and per run, UseAppHost=false, native output)

| Run | Selection | Result |
|---|---|---|
| build | tests/Antiphon.Tests at 29e6816ce | exit 0, 2m38s |
| A | `(C0824DesktopLegacyReleaseSettingsTests*)\|(C0824DesktopLegacyReleasePolicyTests*)\|(TestClassificationPolicy*)\|(TestClassificationGuard*)\|(SlowTestRegistryMerge*)\|(SlowTestTripwire*)` | **201/201** passed, 0 skipped (CP-23 154 = Policy 122 + Settings 32; guards 47 = 39 + 1 + 5 + 2). Includes both `Status_that_cannot_read_the_whole_tree_holds_it` cases green. |
| B | `LandingRemovalPolicyControlTests*`, `DelegationWorktreeTests*`, `AgentTaskLandAdoption*`, `AgentTaskLandMonitoring*`, `AgentTaskLandRequest*`, `AgentTaskLandTargetRace*`, `AbandonedLandingReconciler*`, `LandRefRetention*`, `LandReplacementApproval*`, `LandingProtocolGuard*`, `LandingProtocolHarness*`, `StageOutcomeSummary*`, plus `LandingGitTests*` (direct class of the changed `ExecuteProcessAsync`) | **412 total, 410 passed, 0 failed, 2 skipped** (C721/C665 Windows sharing-lock cases). Per class: RemovalPolicyControl 94, DelegationWorktree 49, LandingGit 72, LandRefRetention 39, LandRequest 30, LandMonitoring 25, ProtocolGuard 23, LandAdoption 21+7, ProtocolHarness 18, TargetRace 11+6, AbandonedReconciler 7, ReplacementApproval 5, StageOutcome 5. |
| — | 25 ControlledLandingGit classes | not run: the double is unchanged in 84e19071e..29e6816ce (the brief made them conditional). |

## Mutants (one build lease + one run lease each, filter `C0824DesktopLegacyReleasePolicyTests*/Status_that_cannot_read_the_whole_tree_holds_it*`)

| Mutant | unreadable-directory | long-path-config |
|---|---|---|
| M0: the three server files reverted to base (the red commits' server code) | **red** (Held expected, was Refused) | **red** (core.longpaths pin) |
| M1: drop `-c core.longpaths=true` | **red** (pin) | **red** (pin) |
| M2: drop the stderr hold | **red** (Refused) | green (expected: only the pin applies) |
| M3: flag set only on a non-zero exit | **red** (Refused) | green (expected) |
| M4: flag only when stderr contains "could not open directory" | green | green (**survives**, see A-1) |

All mutated files were restored with `git checkout HEAD -- <exact paths>`, and the worktree was clean after each mutant.
`check-evidence-diff -BaseRef 84e19071e -HeadRef 5233faefd`: commits=4 entries=1 violations=0.
`git merge-tree --write-tree origin/master(f205fdcec) 5233faefd` (and 29e6816ce): clean.

## Brief items

1. **Consumers.** `WroteStandardError` is an init-only, non-positional property that defaults to false. No
   constructor, deconstruction, record-equality assertion or JSON (de)serialisation of `LandingGitResult` exists
   in server/ or tests/. The only production `ILandingGit` is `LandingGit` (Program.cs:471; `TaskProgressGit`
   subclasses it), and no decorator rebuilds the result, so the flag reaches the policy. Every process path goes
   through `ExecuteProcessAsync:247`. The kill/timeout path throws (TimeoutException becomes the policy's
   `inspection_error` hold), so it never yields a "clean" result. `status` is never answered in process:
   `InProcessGitReads.Handled` takes only rev-parse/show-ref/symbolic-ref/merge-base, and with `-c` first,
   a[0] is `-c` anyway. `IsMutating`/`ChangesRegistrations`/`Profile.Record` are unaffected by the leading
   `-c` (they scan Any/SubcommandIndex).
2. **Strictness.** Clean `git status --porcelain` writes nothing to stderr. CRLF "will be replaced"
   warnings are not expected from status: index hashing without HASH_WRITE_OBJECT uses conv flags 0.
   Plausible host-wide sources that would hold every tree as `status_untrusted` (fail-closed, S4a inert):
   GIT_TRACE*/GIT_CURL_VERBOSE in the server environment, fsmonitor daemon warnings, Git for Windows'
   "encountered old-style path" config warning, and an unreadable XDG ignore/attributes file. See A-3 for
   visibility.
3. **core.longpaths.** It is passed by `-c` on the pre-check status only, and no shared config is changed.
   `DesktopLegacyWorld.Subcommand` uses `LandingGitProfile.SubcommandIndex`. The other doubles that match
   `args[0] == "status"` (ControlledLandingGit:574, LandingRemovalPolicyControlTests:880,
   SettledRemovalHarness:173, CompletedCardRemovalFixture:20/88, SettledWorktreeRemovalTests,
   WorktreeGuardedCleanupTests, LandSourceIdentityTests, the C527 Reply tests) all serve `InspectAsync`'s
   unchanged `status` argv, not the S4a pre-check. Only the C0824 classes reach the pre-check, and they all
   ran green.
4. **Remaining windows**, with the removal's `InspectAsync` (LandingGit.cs:801) still running without
   core.longpaths and ignoring stderr:
   - (a) A release whose command already started (`CommandIntentId` set) skips the pre-check on every
     later tick (policy :274), so only the blind re-check guards those retries. A deep untracked file
     written to a terminal tree of 48 h or more after its first (fixed) pre-check would be missed on
     Windows.
   - (b) A same-tick race between the pre-check and the removal's re-check.
   - (c) N-2, a deep *ignored* tree, is unchanged. git does not descend an ignored directory with or
     without core.longpaths, so the tree still ends Partial where the no-follow delete lacks `\\?\`, and it
     ends Removed on a long-path-aware host (ignored content goes through the CARD-0665 gate by design).
     It is not worse after the fix.
   - None of these is new or worsened. They match the Debug's open items 1 and 2.
   - CP-25 should also confirm that N-2 `Long_path_never_leaves_an_unregistered_orphan` is not now held:
     its `ReleasesAsync(a).Single()` would throw on a `status_untrusted` hold. This is not expected with
     longpaths.
5. **The Linux witness.** It is red at base server code (M0, 2/2) and green at 29e6816ce (2/2). It ran as
   uid 1654, so the chmod case is not vacuous. Modes are restored in `finally` for both the tree and
   set-aside paths, and the runs left no `/tmp/antiphon-c448-*` behind.

## Advisories (no Review rerun needed)

- **A-1.** M4 survives. The "ANY stderr holds" breadth is unpinned, so a later narrowing to a text match
  (which would also break under a localized git) would stay green. Owed PC for SourceLanding Mutation: a
  witness whose clean status writes a different stderr line. One option is a `LandingGit` subclass that
  returns `WroteStandardError=true` with empty output. Another is a repo whose config triggers a
  non-"could not open" warning.
- **A-2.** The `unreadable-directory` case has no root skip. Under root, git reads the directory, so the
  row is `source_dirty` and the test goes **red** (a false red, not vacuous). server2 runs as non-root.
- **A-3.** A `status_untrusted` hold is visible only as a Held row plus an Information log line, with a
  60 min backoff doubling to 24 h. There is no metric or alert. A host-wide stderr source (item 2) would
  silently make S4a hold everything. A follow-up could add a run-level census or alert when every
  pre-check of a run is `status_untrusted`.
- **A-4.** Window 4(a) (spent-intent retries) and the blind `InspectAsync` are the Debug's follow-up 1.
  Recommend the follow-up card it proposed.

## Not run / owed

- CP-25 (Windows native class, desktop lane). Nothing ran on the desktop because the qualification run
  b19a9880 is live.
- The whole Unit lane was not run: Interim scope was accepted by the operator, and every named class ran.
- Checkpoint-tool receipts were not produced (direct build-slot runs as the brief prescribed), so
  reviewedSourceClean is false.
- rc/2026-11-06 needs a port of this fix.
