# CARD-1853 round 1 Review (task 00d82b2d)

Subject: `origin/feat/card-task-202bdbb3` tip `1227766a70a5b4df90a51307b97c53aaebec0eae`
(Code task 202bdbb3, Grok author). Range `75878de2367fa059a16c0184ef297fa1dc7d9681..1227766a7`
(2 commits, test-only: `DescendantLeakScope.cs`, `DescendantLeakScopeTests.cs`,
`RepositoryMutationLeaseTests.cs`). No production code changed.

Verdict: **1 blocker (D-1)**, 3 advisories.

## Blocker

### D-1 The witness is red on every Windows CRLF checkout

- **Where:** `tests/Antiphon.Tests/TestHelpers/DescendantLeakScopeTests.cs:121-124`
  (`C24AdoptsInsideItsTry`): `body.IndexOf("\n        try\n", StringComparison.Ordinal)`
  and then `tryAt.ShouldBeGreaterThanOrEqualTo(0)`.
- **Failure:** `.gitattributes` has `* text=auto`, and the Windows machines run `core.autocrlf=true`
  (docs/superpowers/plans/2026-09-02-card-0004-card-task-file-sync-plan.md:72, :192). A Windows checkout
  puts CRLF in `RepositoryMutationLeaseTests.cs`, so the LF-only literal never matches.
  `tryAt` is -1, and `A_throwing_adopt_kills_the_root` fails on every Windows run, including the
  desktop and the rc qualification lane. Reproduced on server2: the tip build passed, then the file
  was converted to CRLF (`sed 's/$/\r/'`, 810 lines) and the same build was re-run with
  `--treenode-filter /*/*/DescendantLeakScopeTests/A_throwing_adopt_kills_the_root`. Result: 1/1
  failed at `DescendantLeakScopeTests.cs:124` with "should be greater than or equal to".
  The file was restored afterwards.
- **Why:** this adds a new test that is red on Windows by design. It is the same class of bug
  as CARD-1465 (an LF literal against a CRLF checkout), and the class has no Windows platform
  gate.
- **Fix:** normalise line endings before searching (for example
  `File.ReadAllText(...).ReplaceLineEndings("\n")`), or match `\r?\n        try\r?\n`. Prove the
  fix with the same CRLF conversion probe on Linux.

## Verified

- AdoptOrKill: the kill is limited to the caller's own `root` `Process` object. A `HasExited`
  guard runs before `Kill(entireProcessTree: true)`, and the bare `throw;` keeps the original
  exception and its stack. It cannot reach an unrelated process: the PID is held through the
  `Process` handle, and that handle has not exited.
- C24 has no weakened assertion. The only change is that `leaks.Adopt(process)` before the `try`
  becomes `leaks.AdoptOrKill(process)` as the first line inside it. A throwing adopt therefore
  reaches the `finally`, which kills the tree, runs `KillAll` and drains output. The scope tests'
  existing four methods are unchanged.
- Witness can go red. A mutant that swallows the kill (`root.Kill` replaced with a no-op) was
  rebuilt and run: 1/1 failed at `DescendantLeakScopeTests.cs:99` (`HasExited` should be True).
  The author's red at 4e91d930b had the same shape.
- The A-2 decision is sound. `Process.Start` exposes neither `CREATE_SUSPENDED` nor
  `PROC_THREAD_ATTRIBUTE_JOB_LIST`, and the race is a false negative only. The reason is recorded
  in the XML doc and on CARD-1853 revision 2.
- Rosters and guards: `DescendantLeakScopeTests` appears only in `tests/derived-impact-index.json`.
  No roster entry changed, so no guard union is needed. The batched guards ran green.

## Runs at the reviewed SHA (server2 Linux, build-slot leased, native output, UseAppHost=false)

| Run | Result |
|---|---|
| `(DescendantLeakScopeTests*)\|(RepositoryMutationLeaseTests*)` | first run 36/37: C24 red with `crash-worker.ps1:31` "Could not load file or assembly 'System.Text.Json, Version=10.0.0.0' ... (0x80131509)"; rerun 37/37 green |
| C24 alone at the tip, twice | 1/1, 1/1 green |
| C24 alone at base 75878de23 | 1/1 green |
| Guard batch `TestClassificationPolicy*\|TestClassificationGuard*\|SlowTestRegistryMerge*\|SlowTestTripwire*\|NativeLinuxPolicyWitnessTests*` | 52/52 green |
| CRLF probe, witness alone | 1/1 red (D-1) |
| Swallow-kill mutant, witness alone | 1/1 red (killed) |
| `check-evidence-diff.ps1` 75878de23..1227766a7 | commits=2 entries=0 violations=0 |
| `git merge-tree --write-tree origin/master(077f0b3a7) 1227766a7` | clean, tree 6149c8f8c |

## Advisories (do not hold the land)

- A-1: `Kill(entireProcessTree: true)` can throw `AggregateException` when the tree kill is
  incomplete. The catch filter only covers `InvalidOperationException` and `Win32Exception`, so that
  exception would replace the original adopt failure. The test still fails, but with a different
  message. Low probability.
- A-2: C24 now starts the stdout/stderr readers and the unrelated sleeper before the adopt. That
  slightly widens the window before the job is assigned (CARD-1853 A-2, a false negative only).
- A-3: the first class run's C24 red is a one-off pwsh System.Text.Json 10 load failure inside the
  crash worker. It is unrelated to the diff, because `Adopt` is a no-op on Linux. It went green
  alone at base, alone at the tip (twice) and in a class rerun. File it if it recurs.

Native Windows proof of a real `AssignProcessToJobObject` failure is still owed (manual, pending).
