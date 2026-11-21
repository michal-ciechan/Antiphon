# Why master verification missed C1714 and C1199

## C1714 landing status trust

The native Linux witness first landed in `e2bc1c2dfe2eb89d2ff9941f16363ca2e4aba1a0`. The production regression came from `9bcd61113f631ec58f158ff3fb637736fb786fd4`, which removed the directory-readability fallback from in-process status. Git and libgit2 can both omit entries in an unreadable directory. The Git CLI reports a warning on standard error, which the landing inspection uses to refuse an apparently clean tree. An in-process empty result lost that signal.

On server2, uid 1654 had no effective capabilities. Listing the fixture's mode-000 directory returned permission denied. Git 2.47.3 returned exit zero, zero stdout bytes, and a permission warning on stderr. Restoring the directory mode made the unchanged sentinel visible to status. The fixture therefore exercises a real unreadable directory.

The optimization's comparison test deliberately accepted the permission warning while comparing path sets. The change's commit message recorded the expected effect on the Linux desktop release witness and deferred it to a Linux run. The synthetic landing and removal tests inject a result with its stderr flag already set, so they can check how callers handle the flag while missing a regression in how the runner obtains it. The group-47 round-two review later reproduced the native failure on the master baseline.

The repair marks the two status calls that consume the stderr flag. Landing inspection and desktop legacy release now receive Git's real flag through the existing runner. Ordinary verdict-only reads retain the in-process optimization.

## C1199 compiled checkpoint roster

`66624ae08667cce76d4969c8ddc67c635842e8d3` added the CARD-2006 tests `shape_fixture_sdk_build_count` and `baseline_staged_fixture_sdk_build_count` to classes that the C1199 census enumerates. Both tests create scratch Git fixtures and inspect their generated project definitions. They use no Windows-only mechanism and do not run a fixture SDK build. They stay required on both platforms.

CARD-2006's checkpoint plan and review ran the changed fixture classes and the general guard batch without `ReleaseGatePlatformTests.C1199_WindowsRequiredRoster`. The fixture tests check the generated project definitions. The separately pinned census checks the full compiled identity set, so a pass in the fixture classes cannot verify the census. The fixed short Merge selection for `ReleaseGatePlatformTests` covers the native-Linux census and exclusion methods, leaving this check dependent on the impacted selection. The group-47 candidate later reported 37 distinct compiled identities, and its master comparison stopped at the test's 120-second deadline before reporting the roster.

The C# and PowerShell expectations now pin all 37 names, including both new tests. The PowerShell check still demands unique identities, checks both platform policies, verifies that each required identity can independently prevent completeness, and pins the exact residual skips. The preexisting native Windows qualification remains a release obligation.

## C498 rc group 50

RC run `3528e6b1` reported PostgreSQL `40P01` in the monitor/resolver checkpoint witness at `bd34a7ff6da9db1a3fa3690f824890ef9b7cc2c0`. That tree has the fixture alias interceptor from `000375cf3440441cf3d70430eea1cf85443e79c1`. Its `ALTER TABLE ... ADD COLUMN IF NOT EXISTS` takes an ACCESS EXCLUSIVE lock even when the alias column already exists. Master contains `1cdc778c13c445543e18ca4048d4806d7b1f1994`, which checks column visibility and skips that DDL on a ready fixture database. The fix is absent from the tested rc SHA.

The exact C498 case passed 3/3 in three separate Linux test hosts using the fixture database at master `3ed773829e584eec0c93ec8b47bf3517a69204d7`. This is covered by the master fix. The trials did not run the Windows rc group.

## Master baseline evidence

Checkpoint run `20261121-204814-49e6` bound CP-1 through CP-5 to clean source at `3ed773829e584eec0c93ec8b47bf3517a69204d7` with verified build provenance. CP-1 failed the acceptance assertion. CP-2 failed the roster assertion with 37 unique compiled identities and no legacy shape case. CP-3 through CP-5 each passed one C498 execution. All five rows executed the exact selected method and reported zero skips.
