# Why run ae84478d g97 C0824RefOnlyCleanupTests red was missed

Run `ae84478d` (Windows rc qualification, `rc/2026-11-17` = `3db1a43b02246706dc28075fce7e7b5e84804ddb`),
chunk `antiphon-all-group-97`: `C0824RefOnlyCleanupTests.All_desktop_consumers_skip_ref_only_without_deleting_refs`
failed in 5.18 s against a 60 s class timeout. Windows extract:
`origin/feat/card-task-c3d52c5f:docs/investigations/2026-11-10-run-ae84478d-g97-c0824-ref-only-cleanup-red-windows-failure.md`.

## Classification

Test fixture defect: a semantic conflict between two test-only changes. Not timing, not the
slow-Windows-git family of group 56 (`13deffd27`), not a product defect and not a Windows skip.
Deterministic on every platform: the same refusal reproduces on Linux (server2, 0.87 s).

```
InvalidOperationException: c1017_checkout_under_temp: <TEMP>/c1017/<nonce> is under <TEMP>; run from a checkout outside the temp directory.
  at CompletedCardCleanupFixture..ctor  CompletedCardCleanupFixture.cs:51
  at C0824RefOnlyCleanupTests.AssertPersistentReportLocationsAsync  C0824RefOnlyCleanupTests.cs:260
```

## Cause

- CARD-0824 S5b-2 (`c8e61a69d`, on master 2026-11-06) gave `CompletedCardCleanupFixture` a
  `sourceRoot`/`reportRoot` overload and added the R-5 arm of `AssertPersistentReportLocationsAsync`.
  Its `temporarySource=true` iteration deliberately builds the fixture under `Path.GetTempPath()`
  with a persistent `ReportStorageRoot` outside temp, to prove reports survive a temp source.
- CARD-1230 (`b509cb8c0`/`61d6609b8`, reviewed 2026-10-15) made the fixture constructor refuse any
  root under temp (`c1017_checkout_under_temp`). Its premise: the fixture's report root lives inside
  its checkout, and `AgentReportStore` refuses a report root under temp. That premise only holds with
  no configured report root: `AgentReportStore.ResolveRootAsync` falls back to a configured
  `ReportStorageRoot` when the checkout candidate is not persistent.
- CARD-1230 was ported to master on 2026-11-11 03:32 as cherry-picks `bb5b2654b`/`d912ed6a0`
  (LAND-NOW entry in `docs/investigations/2026-11-11-unlanded-landing-queue.md`). From then on the
  R-5 temp arm threw before its first assertion. Run `6591e378` (`ac6d00022`) has `c8e61a69d` but not
  `d912ed6a0`, and its final-result note lists no failure for this class; `ae84478d` is the first
  rc with both. The class's own sources are unchanged since `ac6d00022`.

## Fix (test-only, on `feat/card-task-a5aebe24`)

- Red `78cc738f8be4fb5b9e0798b54d8d1f23cf27920c`: `CardDoneWorktreeCleanupTests.C1230_ConfiguredReportRootDecidesTheTempRefusal`
  over a `FixtureRootRefusal(root, reportRoot, temp)` stub that keeps the old behaviour. Red run:
  `C1230_ConfiguredReportRootDecidesTheTempRefusal` failed on its first literal, `All_desktop_consumers_...`
  failed with `c1017_checkout_under_temp` (3 run, 2 failed).
- Fix `f7f303e8388a56ee4f4a1e0d7cb4eb1dc5dd9aee`: the constructor refuses a temp source only when no report
  root is configured or the configured one is also under temp. The CARD-1230 refusal for the default
  layout and `C1230_TempCheckoutIsRefusedByName` are unchanged; no assertion was loosened.
- Green on Linux at `f7f303e83`: `/*/*/(C0824RefOnlyCleanupTests*)|(CardDoneWorktreeCleanupTests*)|(TestLaneCategoryGuardTests*)|(ProcessSpawnLimitTests*)/*`
  83 executed, 83 passed, 0 failed, 0 skipped. `All_desktop_consumers_...` 10.2 s,
  `Remote_cleanup_...` 9.4 s, `Legacy_missing_...` 9.6 s. Native Windows proof is owed (the
  `temporarySource=true` arm under `%TEMP%`).

## Why it was missed

- The approving Review of CARD-1230: 2026-10-15T07:08Z, Clean, round Final, scope Full,
  reviewedSourceClean true, at tip `4bf2d2e6e` (merge-base `78e400178`, 5,530 master commits behind the
  port). Its plan (`docs/superpowers/plans/2026-10-15-card-1230-c1017-temp-checkout.md`) selected
  CP-1 `CardDoneWorktreeCleanupTests/(C1230_*)|(C1017_TerminalTaskRequired*)`, CP-2 the three
  retirement classes and CP-3 the fixture consumers of that time (`CardWorktreeCleanupRecoveryTests`,
  `CardWorktreeMirrorCleanupTests`, `CompletedCardWorktreeRemovalTests`).
- `C0824RefOnlyCleanupTests` was not a consumer of the overload then: CARD-0824 added the overload
  and the temp arm three weeks after that Review. The Review was correct for its base.
- The port was landed on "merge-tree clean + Clean Final/Full at the tip" with no run at the
  landing target. The queue entry itself recorded 7 master commits since the merge-base touching the
  same paths (latest 2026-11-06, i.e. CARD-0824), and a clean textual merge cannot see the new caller.
- CARD-0824's own Reviews ran before the guard existed, so they could not see it either.

## Cheapest pre-land check

For a stale-base land or port, one impacted-classes row at the landing target, built from the
derived impact index for the subject's changed paths. On master the index already maps
`tests/Antiphon.Tests/TestHelpers/CompletedCardCleanupFixture.cs` to `C0824RefOnlyCleanupTests`,
`CardDoneWorktreeCleanupTests`, `CardWorktreeCleanupRecoveryTests` and `CompletedCardWorktreeRemovalTests`:

| CP | Build | Filter | Expect | EstimatedMinutes |
|---|---|---|---|---|
| CP-impacted | `tests/Antiphon.Tests` at the rebased/picked tip | `/*/*/(C0824RefOnlyCleanupTests*)\|(CardDoneWorktreeCleanupTests*)\|(CardWorktreeCleanupRecoveryTests*)\|(CompletedCardWorktreeRemovalTests*)/*` | 0 failed; 0 skipped (Linux) | 5 |

It goes red in under a minute on Linux at `d912ed6a0` on top of `c8e61a69d`. A merge-tree-only
LAND-NOW for a subject whose base is thousands of commits behind should carry this row.
