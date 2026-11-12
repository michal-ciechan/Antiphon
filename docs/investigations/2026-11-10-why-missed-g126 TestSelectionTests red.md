# Why missed: reused qualification clone keeps CRLF bytes after an eol=lf attribute

One committed note for rc qualification run `ae84478dd83d4cde8f537ba8303794fb`
(rc/2026-11-17 = `3db1a43b02246706dc28075fce7e7b5e84804ddb`), chunk
`antiphon-all-group-126`, one failure in `Antiphon.Tests.Scripts.TestSelectionTests`.
Debug task 6eea814d. CARD-1781 template.

## Failure

`C1313_SelectorLineEndings`: 37 total, 36 passed, 1 failed (Windows, native desktop).

```
ShouldAssertException: result.ExitCode should be 0 but was 1
Exception: C:\Antiphon\releases\checkout\scripts\test-test-selection.ps1:340
  if($lf.Contains("`r")){throw "live selector is not LF: $path"}
  live selector is not LF: scripts/select-test-rosters.ps1
```

The failure comes from the harness precondition, not from a selector assertion.
`New-C1313SelectorCandidate` reads the live working-tree selector and refuses a CR.

## Cause (classification: PRODUCT DEFECT in the nightly/qualification sync, Windows-only)

- The run's launch record (`C:\Antiphon\releases\task-231427bc\launch-record.json`)
  set `checkoutRoot` = `C:\Antiphon\releases\checkout`. That clone was created on
  2026-10-18. Every earlier passing run used a fresh `checkout-<task>` clone (dce78ee0
  used `checkout-42be26dd`, 6591e378 used `checkout-4dd2a426`, a6410818 used `checkout-c9cb914c`).
- The machine's `core.autocrlf=true` (system gitconfig) checked every `text=auto`
  `*.ps1` file out as CRLF in that clone.
- `3afdc27987038a45b2c113cf0ddee073b360df48` (2026-10-29) added `*.ps1 text eol=lf`.
  A fresh clone honours it. `Invoke-AntiphonNightlyRun`'s sync does `fetch`, then
  `reset --hard FETCH_HEAD`, then `clean -fdx`. `reset --hard` rewrites only index entries
  whose blob or stat changed. An attribute change does not rewrite a stat-clean file
  whose blob is unchanged.
- Result: 169 of 368 tracked `*.ps1` files in `C:\Antiphon\releases\checkout` at
  `3db1a43b0` are still CRLF, including `scripts/select-test-rosters.ps1` (last changed
  `e40736b644`, 2026-10-16). `scripts/lib/test-selection.ps1` changed after 2026-10-29,
  so it is LF. Read-only checks on that clone: `git ls-files --eol` reports `i/lf w/crlf attr/text eol=lf`.
- This is the same symptom as run 28bbb58e (`checkout-5906dbf2`, cloned 2026-10-28,
  before the attribute existed), which prompted 3afdc2798. It is not the 4e0d1159 failure
  (missing `nightly-core.ps1` copy; [2026-11-08-why-missed-test-selection.md](2026-11-08-why-missed-test-selection.md)).
  It is not the g131/g133 family either (new test projects missing from a list, roster or policy mirror).
  It is not a flake: it is deterministic for any reused pre-2026-10-29 clone.

Fix (master, this task): after `reset --hard`, `Repair-NightlyCheckoutLineEndings`
(`scripts/lib/nightly-run-impl.ps1`) reads `git ls-files --eol`. It removes and
re-checks-out (`checkout-index -f -u`) only the paths whose working bytes contradict an
explicit `eol=lf`/`eol=crlf` attribute on an LF blob. Files without an eol attribute keep
the configured `core.autocrlf` behaviour.

The test assertion is unchanged. Witness: `NightlyScriptsTests.C1313_ReusedCloneEolRepair`
(`scripts/test-nightly-run.ps1` `Test-C1313_ReusedCloneEolRepair`) uses a real local
seed, a clone made with `-c core.autocrlf=true`, and a later `*.ps1 text eol=lf` commit.
Because it sets `core.autocrlf` in the clone, it reproduces on Linux as well.

## Introducing commit/card

introducing commit/card: `3afdc27987038a45b2c113cf0ddee073b360df48` (no card; rc Debug task
7360d1bb, cherry-pick of `49ea76e88542f017450e8d422da1d6c467a07749`). It fixed fresh
clones only and assumed the sync re-applies attributes. The latent defect is the sync's
`reset --hard` reuse, from `afae299d4` (CARD-0487 S1-S3, 2026-09-11). The trigger was the
2026-11-11 launch choosing the long-lived 2026-10-18 clone.

## Approving reviews

approving review(s) with scope (Full/Interim) and the selection they actually ran:

- None found for 49ea76e88/3afdc2798. It went to rc under the 2026-10-23 "rc fixes direct
  to rc, no review" standing rule. Its own witness was
  `/*/*/SelectorLineEndingAttributeTests/*` (with `e55a901fe`/`ade38249c`), which checks
  `git check-attr` output and the committed blob bytes, never the bytes of a reused working tree.
- The launch of run ae84478d (Custom 231427bc) is operational, not reviewed code.

## Outside the selection

why the failing test was outside that selection (lane/category/path): Integration,
`tests/Antiphon.Tests/Scripts/TestSelectionTests.cs` `C1313_SelectorLineEndings`.
It is green in every fresh clone, including Linux runners and per-task Windows
worktrees. It can only go red on a Windows clone that is reused from before the attribute.
No pre-land selection runs from such a clone. The sync's existing real-git witness,
`C487_G026` (`NightlyScriptsTests`), reuses a clone but never changes `.gitattributes` between syncs.

## Cheapest pre-land check

the cheapest pre-land check as a path-glob -> class-filter entry for the impacted-classes table of CARD-1782:

`.gitattributes` -> `/*/*/NightlyScriptsTests/C1313_ReusedCloneEolRepair*`

`scripts/lib/nightly-run-impl.ps1` -> `/*/*/NightlyScriptsTests/*`

Operationally: a qualification launch should use a fresh `checkout-<task>` clone, or a sync
that carries this repair. Until a launcher carries the fix, a reused clone created before
the attribute must not be used.

## Exemption

same cause as an already written note: none
