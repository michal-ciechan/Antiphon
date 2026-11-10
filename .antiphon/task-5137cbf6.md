# CARD-0529 Review round 1 (Final)

CLEAN. Code owner `f135a199-647f-4bdb-81fd-94d94922166b`, reviewed tip `426e724227695f64a718cf8fc274ce98711a91ed`. Ordinary scope Full. No blockers.

URL anchors trim a trailing backtick, `>`, `*`, or quote along with `).,;`. A path whose length after the existing punctuation trim is under 3 is not an anchor. `/ab` and `./x` stay anchors. A URL whose last character is not in that trim set stays whole (`https://example.com/x` still fails closed on the full token, including `x`). Dropping a backtick-wrapped URL still rejects and now names the clean URL.

## Red at c54e5c1e63dde47d819c59f00f8de4c46d6c4a35

Detached worktree, native output, `UseAppHost=false`, build-slot lease `a0704bc0-c53c-4c65-8da6-57142e0d2c80` (build exit 0). Witness filter: 12 executed, 10 failed, 2 passed, exit 2.

The 10 failures are the new witnesses, for the pre-fix reason:

- six wrapped-URL cases still require the wrapper (`url:` ends in `` ` ``, `>`, `*`, `"`, or `'`)
- the omitted backtick-wrapped URL is named `` url:https://slides.desktop.codeperf.net/` `` rather than the clean URL
- `/p`, `/p.`, and `/q,` are still anchors (`path:/p`, `path:/q`)

The two passes are `a_three_character_path_remains_an_anchor` for `/ab` and `./x`. Those already fail closed before the length floor.

## Green at 426e724227695f64a718cf8fc274ce98711a91ed

Build-slot lease `4ee8a232-7dfb-4bfd-825e-f88eadb78a76`, `dotnet build tests/Antiphon.Tests -p:UseAppHost=false`, exit 0, 0 errors. Test lease `6d49ccae-e164-43bb-8e2d-824fff6fa067`, `--no-build`, TRX `.antiphon/card-0529-review/card-0529-review.trx`: 193 executed, 193 passed, 0 failed, 0 skipped.

| Class | Passed |
|---|---|
| OutputDistillationGateTests | 115 |
| TestClassificationPolicyTests | 41 |
| TestClassificationGuardTests | 1 |
| SlowTestRegistryMergeTests | 5 |
| SlowTestTripwireTests | 2 |
| NativeLinuxPolicyWitnessTests | 3 |
| MechanicalPcContractTests | 11 |
| TaskPlatformGuidanceTests | 5 |
| C1729LayeringGuardTests | 9 |
| TestLaneCategoryGuardTests | 1 |

The twelve new cases are inside the 115 and passed. The five-class policy sweep is 52. Mechanical contract, task-platform guidance, and layering rows are the profile's remaining guard batch (25). `TestLaneCategoryGuardTests` is the impacted-classes row for this range (1). Nine-class union 78, plus the touched class, 193.

No plan `### Checkpoints` table exists, so there was no checkpoint-tool row to replay. The commissioned selection is these leased runs.

## Other gates

- `scripts/check-evidence-diff.ps1` base `29e6736ee88b8ee686fea28128fe13056d79828d` head `426e724227695f64a718cf8fc274ce98711a91ed`: commits=2 entries=0 violations=0, exit 0.
- `git merge-tree --write-tree origin/master HEAD`: clean, tree `b933c54c29a74a5311f414e505a21759357e05dd`. `origin/master` is `a69789c77865b2db502500ebd26d58f72a8013c0`.
- Impacted classes: kind `test-lane-category` only. `windowsFilters` empty. Migrations kind did not match, so the downgrade selector count is 0.
- The production diff is portable string trimming. No OS branch. No plan OS-witness declaration.
- No positive-control rows in a plan. None were run. They stay pending for SourceLanding.
- Worktree porcelain was empty at the reviewed tip. `reviewedSourceClean: true`.

Proceeded under the 2026-11-10 authority to run reviews on Grok.
