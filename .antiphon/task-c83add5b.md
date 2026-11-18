# Review c83add5b: CARD-1996 (round 1, Final)

Subject: origin/feat/card-task-4a4a5995 at 59d09644e3915a3a97b1d59a0e6e335bf7a3468d (Code owner task 4a4a5995, Grok). Base: master 997f9b8d9ffc4ba870d2fd56caf7c03781591624. Range: 997f9b8d9f..59d09644e3 (3 commits).

Verdict: CLEAN. No blockers, no regressions. Two advisories, listed below.

## Evidence

- Checkpoint run 20261118-191759-c9dd at 59d09644e3, source clean, buildSource verified, one build, verdict GREEN. `validate --expected-source-sha` printed `CHECKPOINT SOURCE VALID rows=2`.
  - CP-1 `/*/*/LinkedTestHelperClosureTests/*`: executed=3 passed=3 failed=0 skipped=0. testsWall 0.98 s, so the guard still takes about a second and builds nothing.
  - CP-G guard batch `(TestClassificationPolicy*)|(TestClassificationGuard*)|(SlowTestRegistryMerge*)|(SlowTestTripwire*)|(NativeLinuxPolicyWitnessTests*)`: executed=53 passed=53 failed=0 skipped=0.
- R-2 / Merge rule executability: `dotnet build Antiphon.sln -p:UseAppHost=false` with `ANTIPHON_LINUX_COMPILER_POLICY=0` ran once under build-slot lease bc3c4164 (held 182 s). Native output, 0 errors, 3m01s on server2 with a mostly cold output. It compiled all six linking projects: Resilience.Tests, GitInProcess.Tests, Observability.Tests, SessionRunner.Tests, Checkpoints.LifecycleHost and DerivedImpactIndex.
- `git merge-tree --write-tree origin/master HEAD`: clean, tree 7e029ee95b.
- `check-evidence-diff.ps1 -BaseRef 997f9b8d9f -HeadRef HEAD`: commits=3 entries=0 violations=0.
- Red-first: the witness commit 3083ca555d precedes the fix 39f37d95a2. At the witness commit, the census indexed only same-name files, so `HiddenHelper` in `NotTheTypeName.cs` could not be reported. I did not rebuild to rerun the red.
- Mutation is waived (operator 2026-11-12). PC-1 stays pending.

## Docs check

- The six linking csprojs are exactly the five mirrors named in the docs plus `scripts/regenerate-derived-impact-index` (`grep -l 'Antiphon.Tests[\\/]' **/*.csproj`), and all six are in Antiphon.sln.
- The rule is cheap under Merge-short: one leased solution build in native output (about 3 min on server2) or one build per mirror. The one-by-one Compile lists stay, and the docs give the reason: each mirror links a different subset.

## Advisories (go to cards; they do not hold the land)

A-1 (guard false negative). `Code()` strips comments before string literals. A `//` inside a string (`"https://..."`) therefore eats the rest of the line, including braces, and the new brace-depth top-level index goes out of balance. On the live tree four TestHelpers files end at depth 1: BlockedTaskParkFixture, CompletedCardCleanupFixture, SseStreamHandler and LinkedTestHelperClosureTests. Confirmed miss: `ReportStoreLog` is a namespace-level type at CompletedCardCleanupFixture.cs:395. The `}` lost on :94 (`GitRepositoryUrl = "https://example.test/fixture.git", ... }`) leaves it at depth 1, so it is not indexed. A lost `{` would do the opposite and index nested types as top-level (false positive). Char literals `'{'`/`'}'` and multi-line raw strings skew the count the same way. This is not a regression, because master never indexed these types, and the Merge solution build remains the primary gate. Fix: strip strings and comments in one left-to-right alternation (strings and char literals first), handle `"""` raw strings, and add a fixture with a URL string before a second top-level type.

A-2 (Merge trigger narrower than the link set). Mirrors also link files outside TestHelpers:
- `tests/Antiphon.Tests/Scripts/*` (DerivedImpactIndex)
- `Agents/Fixtures/*` (SessionRunner.Tests)
- `Checkpoints/*` (LifecycleHost)

A batch that edits one of those linked files so it uses a new type does not trigger the solution build. Only the census catches it, and only for same-name owners. The card's fix item 1 names exactly TestHelpers/** plus linking csprojs, so the branch matches the card. Fix: trigger on any `tests/Antiphon.Tests/**` path that a mirror csproj Compile-includes, or on any `tests/Antiphon.Tests/**/*.cs` change.

## Review evidence

subjectTaskId: 4a4a5995 (owner Code task; full GUID is with the caller)
reviewedSourceSha: 59d09644e3915a3a97b1d59a0e6e335bf7a3468d
reviewedSourceClean: true
ordinaryScopeCompleted: Full
reviewRound: 1
reviewedRange: 997f9b8d9ffc4ba870d2fd56caf7c03781591624..59d09644e3915a3a97b1d59a0e6e335bf7a3468d
