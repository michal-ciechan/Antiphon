# Astra B advisories review (CARD-2005, round 1, Final/Full)

Review task `d5263722-a14a-43ed-95c3-85b14d1b20cf`. Subject: Code task `265eefc9-a575-4bc8-83aa-27e503495b78`,
`origin/feat/card-task-265eefc9` tip `a85fe1b83685b523c435ac5cb1b765f72137256c`, base
`3f24857e8251ba0eb735f4b6dd1f4c963f3cc05d` (7 commits). Master at review: `9bcd61113f631ec58f158ff3fb637736fb786fd4`.

**Verdict: CLEAN.** No blocker. Advisories below go to cards and do not hold the land.

## Evidence

- Checkpoint run `20261119-211528-f3eb` at `a85fe1b83`: 12/12 rows green, one build (`bin-astra-adv/`),
  `unlisted: none`, `validate --expected-source-sha a85fe1b83...` -> `CHECKPOINT SOURCE VALID rows=12`, buildSource=verified.
  CP-1 AstraFixesBTests 5/5; CP-2 WriteBehindFileSystemTests 8/8; CP-3 NightlyCoreTests 1/1; CP-4 NightlyScriptsTests 7/7;
  CP-5 guard union 81/81; CP-6 derived driver build exit 0; CP-7 DerivedImpactIndexTests 27/27; CP-8 land-audit build exit 0;
  CP-9 land graph `-Check` approved; CP-10 policyHash `0396d6a5...`, inputDigest = pin `3162a7da...`;
  CP-11 check-evidence-diff exit 0; CP-12 merge-tree exit 0.
- Merge-tree `origin/master` (9bcd6111) x tip: clean, tree `c82a003b93982098f80aa5d804d1c8a388a75fe4`.
- Pins on that scratch merge: `test-live-lane-run.ps1 -PrintInputDigest` = `3162a7daa58003f844e4d2b39237095e7cb54df897efc5dd8fc002dfb25a78c6`,
  equal to the branch pin. Master's 6 commits since base touch no protected input (master alone still prints and pins
  `063da62b...`). The Astra A reseal (fb6a2e49) is not on master yet; whichever lands second must reseal.
- Red/green (harness run from git-archive scratch copies, one build-slot lease per case):
  - At red `634a331d`: FlushFailure 4 red (path-still-failed `PathFailed=False Kind=regular`, retry-keeps-failed `pathFailed=False`,
    summary-calls-verdict function absent, verdict-flushes `coverage=True`); JournalPublish unrelated-releases-lock red
    (`writer-lock-held`); StaleCore fresh-pwsh red. CompleteCorePath/CheckoutIdentity red at `634a331d` only because the
    function was not loaded (fixed by `0a488470`); at `0a488470` they are red for the stated reasons: CompleteCorePath.fresh-pwsh
    (message lacks the hint; `loaded=` was already named at base) and CheckoutIdentity.rechecked (`rechecked=False`).
  - At tip all five cases green (10/6/5/4/2 rows).
- Mutants on the tip (scratch, each killed):
  - A-2: `Test-TestPhaseJournalWriteFailure` returns `$false` (lock released on any failed path) -> JournalPublish.close-holds-lock red (`reacquired=True`).
  - A-4: `PathFailed` ignores `retainedFailedPaths` (later success / Flush(path) clears) -> path-still-failed and retry-keeps-failed red.
  - A-3: verdict without the leading `Flush()` -> verdict-flushes red (`coverage=True`).
  - A-6: return `$true` on the cached flag without re-verification -> CheckoutIdentity.rechecked red.
- Journal-path probe of `Test-TestPhaseJournalWriteFailure`: `journal/g/1.json` true; `journal2/...`, `journal.bak/...`,
  sibling sidecar false; `other/../groups-executed.journal/g/1.json` true (GetFullPath normalised).

## Judgement

1. A-2 ancestor test: prefix is `GetFullPath(journal).TrimEnd(/\) + separator`, so a same-prefix sibling cannot match; `..`
   is normalised; case-insensitive comparison only widens the hold (safe). Queue failure text is always
   `<GetFullPath(path)>: message` (`WriteBehindQueue` stores only `WriteBehindException` with the keyed full path), and both
   sides derive from `Context.TimingRoot`, so symlink and 8.3 spellings agree. No fail-open found in the new code.
2. A-4 state machine: queue failures are removed only by `ThrowFailures`; a later success never clears them; `Retain` adds
   both the text and the full path; only `TakeFailures` (Disable) clears. A failure cannot be lost; the same text can appear
   once retained, and `Merge` de-duplicates exact strings.
3. Verdict path: `nightly-tests.ps1` wraps the run in `Enable ... try/finally Disable`; the merge runs unconditionally before
   the only `summary.json` write. Exception, Ctrl+C and timeout exits write no summary, which downstream already forces
   `coverageComplete=false`. Fail-closed.
4. A-6: the stamp covers exactly the identity's file set (`.cs`/`.csproj` of both projects); a changed stamp recomputes the
   hash and either matches the accepted hash or runs `Assert-NightlyLoadedCoreIdentity`, which throws for any assembly not
   loaded from the new hash folder. No stale-core acceptance found. Cost: stamp 5.3 ms/call, identity 28 ms/call on loaded
   server2; hot callers short-circuit on `$script:NightlyCoreAvailable`.
5. Witnesses red for the stated reasons and green at tip; all four requested mutants killed.

## Advisories (cards; not blocking)

- A-1 (pre-existing, unchanged by this branch): `Close-TestPhaseExecutedContext` decides from `Flush()`, which returns only
  failures not yet drained. A journal commit failure already drained by `Write-TestPhaseCommitFile`'s `Flush($FinalPath)`
  leaves Close's `Flush()` empty, and `writer.lock` is released. Same at base; the verdict stays failed through
  `retainedFailures`. Now that `PathFailed` is sticky, Close could also hold the lock when a retained failed path is under
  the journal.
- A-2: sticky `PathFailed` makes `Add-TestPhaseJournalEnvelope`'s second attempt dead weight. A retry that lands is still
  refused `deferred-write-failed` by `Read-TestPhaseBoundedBytes` for the rest of the run. This fails closed and matches
  the A-4 claim, but document that a retry cannot recover the record.
- A-3: unparseable or pathless failure text is judged unrelated, so the lock is released. This cannot happen today, but
  returning `$true` for text without a rooted path would make it fail closed.
- A-4: `New-AstraCompleteCore` creates `Antiphon.Nightly-<random>` folders in the shared
  `$TMP/antiphon-nightly-core` cache and never removes them. Each CompleteCorePath/CheckoutIdentity run leaves one.
- A-5: the source stamp is name+length+mtime. Copying a same-length edit with timestamps preserved (robocopy defaults)
  would skip re-verification. The nightly checkout updates through git, which sets new mtimes.
- A-6: `Get-NightlyDeferredEvidenceWriteVerdict` swallows a non-aggregate `Flush()` exception with an empty catch. Queue
  failures are still peeked, but the exception itself is not reported.

--- review evidence ---
subjectTaskId: 265eefc9-a575-4bc8-83aa-27e503495b78
reviewedSourceSha: a85fe1b83685b523c435ac5cb1b765f72137256c
reviewedSourceClean: true
ordinaryScopeCompleted: Full
reviewRound: 1
reviewedRange: 3f24857e8251ba0eb735f4b6dd1f4c963f3cc05d..a85fe1b83685b523c435ac5cb1b765f72137256c
