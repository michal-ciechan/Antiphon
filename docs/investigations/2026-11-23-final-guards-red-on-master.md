# Final guards red on master (2026-11-23)

Debug task 4869c70e. The CARD-2057 plan's CP-4 "Final guards" filter
(`docs/superpowers/plans/2026-11-22-card-2057-retirement-audit-repair.md`, row CP-4) passed
85/85 at master `739358378` around 04:00Z and failed 10/85 at master `d413d4058`.

## Reproduction

At `d413d4058e9944dff14747bd69cc93d0c7bf2beb` on server2, one build-slot build of
`tests/Antiphon.Tests` and one build-slot run of the CP-4 filter: 85 total, 75 passed,
10 failed, 0 skipped. The red is deterministic and not a stale build or leftover temp
state: the build was fresh at that SHA and every failure names the same class.

## One cause, ten cases

Every failure is the Antiphon.Tests lane-xor census reporting
`Antiphon.Tests.Review.ReviewLiveGitHubTests`, which carried only `[Category("LiveGitHub")]`
and neither `Unit` nor `Integration`:

| Case | Message |
|---|---|
| `TestLaneCategoryGuardTests.every_test_class_is_tagged_unit_xor_integration` | untagged test classes: `tests/Antiphon.Tests/Review/ReviewLiveGitHubTests.cs::ReviewLiveGitHubTests` |
| `TestClassificationGuardTests.Registry_matches_compiled_metadata` | `lane-xor Antiphon.Tests.Review.ReviewLiveGitHubTests` |
| `TestClassificationPolicyTests.C1383_Rebase_preserves_distinct_appended_pairs(target-only)` | same `lane-xor` line |
| `TestClassificationPolicyTests.C1383_Rebase_preserves_distinct_appended_pairs(common-base)` | same |
| `TestClassificationPolicyTests.C1383_Valid_reason_pairs_are_accepted(CRLF, False)` | same |
| `TestClassificationPolicyTests.C1383_Valid_reason_pairs_are_accepted(CRLF, True)` | same |
| `TestClassificationPolicyTests.C1383_Valid_reason_pairs_are_accepted(LF, False)` | same |
| `TestClassificationPolicyTests.C1383_Valid_reason_pairs_are_accepted(LF, True)` | same |
| `TestClassificationPolicyTests.C1383_Layout_opt_in_is_scoped(non-repository)` | same |
| `TestClassificationPolicyTests.C487_G068` | errors should not contain `lane-xor` |

The C1383 and C487_G068 cases call `TestClassificationMetadata.AssertRegistryMatches` over the
real compiled assembly, so one untagged class fails all of them.

## Introducing landing

`1eac2c0af` (2026-11-23 04:21Z, "test(review-engine): an opt-in live check against real
GitHub") added the class. It is not an ancestor of the green `739358378`. The other landings
in the window (groups 139/140, session-runner group 5, `61deca9b2`, fixture maintenance,
in-process git paths) touch no lane tag or classification input.

## Fix

Test-only: `[Category("Integration")]` on `ReviewLiveGitHubTests`. The class boots
`AntiphonWebAppFactory`, like its sibling `Review*ApiTests`, all of which are Integration.
Its two tests still skip without the `ANTIPHON_LIVE_GITHUB_*` variables.
`scripts/regenerate-derived-impact-index.ps1` reports the index unchanged (it records class
dependencies, not categories). Witness: CP-4 filter 85/85, 0 skipped, at the fix commit.

## Why it was missed

- The class was pushed straight to master by an operator-session commit, not through a
  Code/Review/land pipeline whose CP rows or Merge short tests would have run the lane guards.
- The author ran only the class's own filter (`/*/*/ReviewLiveGitHubTests/*`, via
  `scripts/review-live-github.ps1`, now the `review-live-github` command), which skips without
  credentials and never touches the census guards.
- The follow-up impact-index regeneration (`001f2f3a4`) selects classes by source dependency.
  The lane census reads every test source file by text, so it is not an impacted class of a
  new test file and was not selected.
- A new test class in Antiphon.Tests needs `Unit` xor `Integration` at birth. Before pushing
  any new test class directly, run the CP-4 Final-guards filter (about 90 s on server2).
