# Review: rc run ae84478d g126 reused-clone eol repair (Debug 6eea814d), round 1

Review task 3997c63a, CARD-1514. Round 1, Final, Full scope.
Subject: `feat/card-task-6eea814d` at `2efdae854085ff4bb837dffaff0586ff9f2de606`
(Grok Code author), reviewed range `56e96eabf698e9c1fcd462836ad32ed1fb259e83..2efdae854085ff4bb837dffaff0586ff9f2de606`.

Verdict: CLEAN. No blockers and no regressions. The advisories below go to cards and do not hold the land.

## Change

- `scripts/lib/nightly-run-impl.ps1`: `Get-NightlyStaleEolPaths` and
  `Repair-NightlyCheckoutLineEndings`. The sync calls them after `reset --hard FETCH_HEAD`
  and before `clean -fdx`.
- `scripts/test-nightly-run.ps1`: `Test-C1313_ReusedCloneEolRepair` builds a real local seed
  and a `core.autocrlf=true` clone, then commits `*.ps1 text eol=lf` afterwards.
- `tests/Antiphon.Tests/Scripts/NightlyScriptsTests.cs`: `C1313_ReusedCloneEolRepair`, with 5 named rows.
- `docs/investigations/2026-11-10-why-missed-g126 TestSelectionTests red.md`.

The live incident path is covered. `release-cut.ps1` runs `nightly-run.ps1` against
`<ReleaseRoot>\checkout`, so it reaches the patched `Invoke-AntiphonNightlyRunCore` sync.

## Safety checks

- **Dirty or modified files.** The repair runs only after `reset --hard`, which has already
  discarded tracked modifications. `git ls-files` lists only tracked paths. A probe placed an
  untracked CRLF `.ps1` in the clone. It kept its bytes and stayed `??` in `git status`.
- **Partial failure.** A failed `checkout-index` throws and fails the sync. A deleted tracked
  file can be recovered: the next sync's `reset --hard` restores it. Nothing untracked is touched.
- **Paths.** A Linux pwsh 7 probe used a stdout-redirected host, matching how the harness
  reads Out-String. The path with a space, the non-ASCII path (`uni/café-中.ps1`) and the 234-char path were all
  rewritten to LF, and a clean status followed. A C-quoted path (`q"uote.ps1`) is skipped
  by design and stays CRLF. The longest tracked path is 163 chars, so it stays below 260
  even under `C:\Antiphon\releases\checkout\`.
- **Fresh clone.** There are no stale paths, so the function returns after one `ls-files --eol`.
- **Cost.** `git ls-files --eol` over 10,497 tracked files takes 1.7 s on server2.
  Repairing 5 files takes 159 ms. Windows timing has not been measured (manual, below).
- **ASCII.** The diff adds no non-ASCII bytes. `The_three_scripts_are_ascii_only` passed.

## Evidence (all at 2efdae854, server2 Linux, native default output, UseAppHost=false)

| Row | Selection | Result |
|---|---|---|
| Build | `dotnet build tests/Antiphon.Tests` (build-slot lease f4de9691, 319 s) | succeeded |
| Touched class | `/*/*/NightlyScriptsTests/*` (lease 735491ff) | 6/6 passed, `C1313_ReusedCloneEolRepair` 6.2 s |
| Sync consumers | `(NightlyReclaimRaceTests*)\|(ToolchainFloorWarningTests*)\|(NightlyDeadOwnerLockTests*)\|(NightlyPrerequisiteTests*)\|(NightlyVerificationContractTests*)` (lease 6e20136f) | 60/60 passed (3+14+3+2+38) |
| Guard union | `(TestClassificationPolicy*)\|(TestClassificationGuard*)\|(SlowTestRegistryMerge*)\|(SlowTestTripwire*)\|(NativeLinuxPolicyWitnessTests*)` (lease e26e9586) | 52/52 passed (41+1+5+2+3) |
| Evidence diff | `check-evidence-diff.ps1 -BaseRef origin/master -HeadRef 2efdae854` | commits=4 entries=0 violations=0 |
| Merge-tree | `git merge-tree --write-tree origin/master 2efdae854` | clean, tree 07066656185aa3fad0d07b04b11c33bed8ffcd92 |

Red/green: the harness case ran on its own from `git archive` scratch copies, with no build involved.

| Copy | Result |
|---|---|
| tip 2efdae854 | 5/5 PASS |
| pre-fix test commit 3d29db261 | RED: `selector is LF after sync`, `clean after the eol repair` |
| M1: repair call removed | RED: `selector is LF after sync` |
| M2: `Remove-Item` before `checkout-index` dropped | RED: `selector is LF after sync` (this shows the delete is required) |
| M3: `eol=lf` match disabled | RED: `selector is LF after sync` |

The test commit lands before the fix commit, so the commit order is correct.

## Advisories (file as cards; do not hold the land)

- **A-1 (Windows PowerShell 5.1, non-ASCII stale path).**
  - Where: `Repair-NightlyCheckoutLineEndings` and `Invoke-NightlyGit` capture native output.
  - Failure: Windows PowerShell 5.1 decodes git stdout with the console OEM code page, so a stale non-ASCII path would be mangled. `checkout-index` then exits 1 (`is not in the cache`, reproduced) and the whole sync throws.
  - Why: today the repo tracks 0 non-ASCII paths, and pwsh 7 decodes correctly (probe above). The case is latent, not live.
  - Fix: skip paths that `Test-Path` cannot find before `checkout-index`, or read `ls-files --eol -z` as UTF-8.
- **A-2 (witness strength).** The `leaves a file without an eol attribute as configured` row
  cannot detect an over-broad rewrite. Under `core.autocrlf=true`, re-checking-out
  `other.txt` produces the same CRLF bytes. Such a rewrite would be harmless, so this is not a defect.
- **A-3 (silent skip).** C-quoted paths are skipped with no log line. A count of skipped
  paths in the existing `nightly sync:` line would make a residual stale file visible.

## Pending (not discharged by this round)

- Native Windows proof is owed and stays pending: Windows PowerShell 5.1 or pwsh on the
  desktop, a reused `core.autocrlf=true` clone such as `C:\Antiphon\releases\checkout`, and
  `ls-files --eol` timing there. Nothing ran on the desktop in this round.
- Every PC stays pending for method-scoped SourceLanding Mutation.
