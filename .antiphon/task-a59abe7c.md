# task-a59abe7c: Debug of group 111 C1076 journal move access denied (run aa64a906)

Outcome: fixed and green on Linux. This is a product defect that a test-side reader race exposed. It is not only a test race.

## Diagnosis

- Failing frame: `RepositoryChildJournal.SaveAsync`, called from `StartedAsync(int, long)`. It writes a staging temp file and then calls `File.Move(temp, journal, overwrite: true)`, which is `MoveFileEx(REPLACE_EXISTING)` on Windows.
- The handle that blocked it: the test's own reader. `C1076_TaggedPushJournalNamesTaskAndPurposeWhileAlive` polls `children/*.json` every 50 ms. The moment the begin record (start intent) appears, the test reads it with `File.ReadAllTextAsync`, which opens with `FileShare.Read` and no Delete sharing. `StartedAsync` replaces that same file a few milliseconds later. On Windows a rename-replace onto a file that another handle holds open fails with ERROR_ACCESS_DENIED, which surfaces as `UnauthorizedAccessException`. No attribute or ACL is involved. Under classic rename semantics this happens even when the reader shares Delete. The production fence readers (`FirstFencingAsync`, `HasUnjoinedVerificationAsync`, `RepositoryChildJournalInspector`) already open with `Read | Delete`, but any of them, or Defender, can still hold the file during the replace.
- Why it showed up only after e114313d3 (batch 8): `b5b3f44a0` and `be4094e92` added `CreateOwnedMarker` (on Windows, `CreateJobObject`) and `owned.Attach` (`AssignProcessToJobObject`) between the begin save and the started save. That widened the window in which the 50 ms poll lands on the begin record and is still reading it when the replace happens.
- The product defect: `Remove()` already retried delete denials (2 s / 50 ms), but `SaveAsync` had no tolerance at all. So a concurrent reader could fail a live git command at its start record.

## Fix (b37b992219789ea341b14ad188f5253328495765)

- `SaveAsync` retries the atomic replace on `IOException` or `UnauthorizedAccessException` for 2 s, with a 50 ms `Task.Delay` between attempts (about 40 attempts). If it still fails, it deletes only its own staging temp file, logs `repository_child_journal_save_failed path=...`, and throws `IOException("repository_child_journal_save_failed; ...")` with the original exception as the inner one. It never swallows the error and never deletes the journal. Because the replace is atomic, the prior durable record stays as the fence.
- An injectable replace seam was added: a new `BeginAsync(repository, tag, deleteRecord, replaceRecord, ct)` overload. It defaults to `File.Move(..., true)`.
- The batch 8 rule that the journal is kept whenever ownership cannot be determined is unchanged. `Remove()` is unchanged; its annotate-save now also goes through the retry, so a fully denied cleanup ends after about 4 s instead of about 2 s.
- Test readers of a live journal (2 in LandingGitTests, 2 in RepositoryMutationLeaseTests) now go through the new `tests/Antiphon.Tests/TestHelpers/RepositoryChildJournalReader.cs`, which opens with `FileShare.ReadWrite | FileShare.Delete`.

## Witnesses (RepositoryMutationLeaseTests)

The red commit 4e644ccfdc31c25cff8c98747d43a2d7a4eac1ea adds the seam with no change in behaviour. Each witness went red at its assertion, with no build errors:
- `C1076_JournalReplaceRetriesTransientReaderDenialBeforeStartReturns(access-denied|sharing-violation)`: two injected denials; asserts 3 attempts, the PID/ticks record on disk and an empty staging directory. Red: UnauthorizedAccessException / IOException thrown.
- `C1076_JournalReplaceFailureKeepsPriorRecordAndFailsLoudly`: permanent denial; asserts IOException `repository_child_journal_save_failed` with an inner UnauthorizedAccessException, more than 1 attempt, under 5 s, the begin record kept (ProcessId null), an empty staging directory and HasUnfinished true. Red: a raw UnauthorizedAccessException.
- `C1076_JournalReadersShareDeleteWithTheReplacingOwner`: source-contract check. Every read `FileStream` in RepositoryChildJournal(.cs, Inspector.cs) must share Delete, no ChildRecord deserialize in the two test files may use `File.ReadAllText`, and the helper must share `ReadWrite | Delete`. Red: 5 violations.

Red run: 8 tests, 4 failed (the 4 new cases), 4 passed (existing C1076_*).

## Verification (Linux server2, at b37b9922)

- LandingGitTests + RepositoryMutationLeaseTests + OwnedProcessTreeTests + RepositoryChildJournalInspectorTests: total 130, 128 passed, 0 failed, 2 skipped (Windows-only C448_V13, C448_V28). 130 = 126 + 4 new cases.
- `audit-land-verification-graph.ps1 -Check`: approved, digest D92450D9AABCBF9C783B506F73DF06C45635F415D03E65C5F1A97AF415CC36BE, count 69 (built the evaluator through build-slot; `bin-land-audit` dirs deleted afterwards).
- `check-evidence-diff.ps1 -BaseRef e114313d3 -HeadRef HEAD`: commits=2 entries=0 violations=0 (before this report commit).
- `git merge-tree --write-tree`: clean onto origin/rc/2026-11-01 (e114313d36ef) and onto origin/master (3a49b60133327f4441f06698ef3369b1d277d049).
- The Windows sharing violation itself cannot be reproduced on Linux. The group 111 Windows rerun is still owed.

--- next stage ---
next: Review (diff-only, Opus) of e114313d3..feat/card-task-a59abe7c, then Merge-role fast-forward rc/2026-11-01 and master
handoff: product fix in server/Infrastructure/Git/RepositoryChildJournal.cs SaveAsync (bounded 2 s/50 ms replace retry, loud IOException, prior record kept); test reader helper; group 111 Windows rerun in the next qualification run
artifact: .antiphon/task-a59abe7c.md
