# CARD-1656 Code task c1cb4001 report

All three named advisories from review 4b605c04 (A-1 atomic stamp, A-3 requester charset, A-4 token compare) were reproduced and fixed. Each fix has a red-first witness and one mutant that turns that witness red.

- Branch: feat/card-task-c1cb4001. Base: 907edaebbd301af95629c31095f1b530803baea2 (master).
- Worktree: runner mirror /work/worktrees/task-c1cb4001 (server2, Linux).
- Commits:
  - 30afe130947fa83f6e955930663bcab7a05a277f: red witnesses.
  - fe16011f6567f44294197432fac266a5d4244b07: test-only fix. The Write tool had turned the ` `-style escapes into literal characters and the file did not compile.
  - 1c1189d00472c5340c1d3c0aa98809418bd68a61: the fix and the docs.
  - The report commit follows these.

## Invariants and fixes

| ID | Invariant | Fix |
|---|---|---|
| A-1 | record-release never stamps `RemoteWorkspaceSlotReleasedAt` on a task that is Dispatched, Working or Blocked, including one that goes live after the route's checks have read it. | `RecordOperatorReleaseAsync` now stamps through one conditional `ExecuteUpdateAsync`. Its WHERE clause covers id, attempt, runner, slot, generation, release still null and status not in HoldingLease. The update runs in one transaction with the audit event. If zero rows change, the route re-reads the status: a lease-holding task is refused 409 `workspace_slot_release_task_live`, anything else 409 `workspace_slot_release_changed` (new code, documented in ops-http). |
| A-3 | The requester recorded in the audit event and the log line contains no control, format, line-separator or paragraph-separator character. Checked per rune, so astral format characters are caught too. | `WorkspaceSlotReleaseService.IsAcceptableRequester` |
| A-4 | The requester never embeds the presented operator token, in any letter case (the token is lowercase hex). | `IsAcceptableRequester` compares the token with `OrdinalIgnoreCase`. |

The endpoint calls the new helper. `docs/ops-http.md` and `docs/session-runtime-invariants.md` are updated.

## Evidence

All runs were on server2, through build-slot, with `-p:UseAppHost=false` and native output. The test class is `tests/Antiphon.Tests/Application/C1656RecordReleaseHardeningTests.cs`, category Integration.

- **Red at base** (tests from 30afe1309 plus fe16011f6, production code unchanged from the base):
  - Filter: `/*/*/(C1656RecordReleaseHardeningTests*)/*`
  - Result: total 3, failed 3. Slot waited 135 s.
  - A-1 failed because the call should throw but did not, so the stamp landed on the task that went Working.
  - A-3 failed on U+2028: expected 400, got 404.
  - A-4 failed on the uppercased token: expected 400, got 404.
- **Green at 1c1189d00:**
  - Filter: `/*/*/(C1656RecordReleaseHardeningTests*)|(PhoneHomeRollingRunnerTests*)/(C1656*)|(C1649*)|(C0824_DrainSlotUnknown*)`
  - Result: total 5, failed 0, skipped 0. That is the 3 witnesses, plus `C1649_RecordReleaseStampsOnlyTheStuckAttemptOfATaskThatIsNotLive` and `C0824_DrainSlotUnknownIsBoundedAndAnOperatorRecordsTheRelease`.
- **Mutants.** One build of 1c1189d00 with env-gated mutants. The source was restored right after that build, and `git status` was clean before the runs. Each mutant was run once against the C1656 class:
  - M1 (A-1): removed `!HoldingLease.Contains(t.Status)` from the conditional update. Red: `C1656_ReleaseIsNeverStampedOnATaskThatGoesLiveWhileItIsRecorded` ("should throw ConflictException"). Total 3, failed 1.
  - M2 (A-3): only Control is refused (the old `char.IsControl` behaviour). Red: `C1656_RequesterRefusesFormatAndSeparatorCharacters` (the line-separator case got 404). Total 3, failed 1.
  - M3 (A-4): the token is compared with `Ordinal`. Red: `C1656_RequesterRefusesTheOperatorTokenInAnyCase`. Total 3, failed 1.
- Note: the worktree's native `tests/Antiphon.Tests` bin still holds the mutant-gated build. Rebuild before any `--no-build` run in this mirror. The mutants are inert without the `C1656_MUTANT` environment variable, but the binaries are still not the committed source.
- `scripts/check-evidence-diff.ps1 -Repository . -BaseRef 907edaebb.. -HeadRef 1c1189d00`: commits=3, violations=0, exit 0.
- `git merge-tree --write-tree origin/master HEAD`: clean, exit 0.

## Not run or not done (deferred)

- **Testing scope:** this follows the operator's minimal-testing policy in the brief: one red witness and one narrow green per fix. The Final-profile whole Unit lane and the full CP-4 and CP-21 classes were not run.
- **Not in the brief's three:** A-2 (rewording the audit text to "operator (requester label: x)") and A-5 (a retry keeping the old slot) were not changed.
- **A-3's reason field:** the card's optional "consider the same for reason" was not applied. The reason field still accepts newlines. Changing that would reject multi-line reasons that operators may already send, so it is a caller decision.
- No plan or checkpoint amendment was written. The brief gave direct verification instructions, and no checkpoint-tool run was made.
- **Pending for Mutation:** the three mutants above were run once as the brief's per-fix controls. CARD-1649's method-scoped PCs (attempt check, live-status guard, token check) are still pending, and the new A-1, A-3 and A-4 controls also stay pending for SourceLanding Mutation.
