# Review 4d98fbcb: CARD-1199 plan round 2 (final)

Subject: `docs/superpowers/plans/2026-10-30-card-1199-windows-runnable-plan.md` at
`13c691b16316a3db36baabd4b7ac26155822f48c` (origin/feat/card-task-dd46f01b, plan task dd46f01b,
round-2 base `421c3de68`). This was a read-only plan review on a server2 Linux mirror. No product
tests ran: the CP rows exercise code that does not exist yet.

**Verdict: two defects remain.**
- Round-1 D-1 and D-3 are closed.
- Round-1 D-2 is closed for rc, but the master port is not executable as written (D-2 below).
- New defect D-1: the self-building CP-46 command row is plausibly a vacuous green on the
  native Windows lane.
- All nine round-1 advisories (A-1..A-9) were taken.

## Defects

**D-1 (new; PLAUSIBLE, Windows lane only): CP-46's quoted `pwsh -Command` can pass without building or auditing.**

- **Where:** the `### Checkpoints` CP-46 command cell. On Windows, `tools/Antiphon.Checkpoints/Execution/RowRunner.cs:152-155`
  runs a command row as `cmd.exe /d /s /c <Command>`, and `ProcessDriver.cs:97-98` passes that
  through `ProcessStartInfo.ArgumentList`.
- **Failure:**
  1. .NET pastes the Command argument as `"pwsh -NoProfile -Command \"dotnet build ... && pwsh ... .dll\""`.
  2. `cmd /s` strips only the outer quotes. `\"` is not an escape to cmd, so `&&` sits inside a
     cmd-quoted region and is not an operator.
  3. pwsh's argv parser turns each `\"` into a literal quote. So `-Command` receives the tokens
     `"dotnet` `build` ... `.dll"`, which join into one PowerShell string literal.
  4. pwsh prints that string and exits 0.

  The last step is demonstrated on this host: `pwsh -NoProfile -Command '"dotnet' build ... '&&'
  pwsh ... 'x.dll"'` echoed the text and returned exit 0. The cmd and CRT steps follow their
  documented rules and could not be reproduced on Linux.
- **Effect:** the Windows R-3 acceptance (CP-46 "build and audit exit 0") goes green with no
  evaluator build and no `-Check`. Linux is unaffected, because `/bin/sh -lc` honours the quotes.
- **Fix:**
  - Remove the wrapper and every embedded quote. Both `sh -lc` and `cmd /c` honour a bare `&&`:
    `dotnet build tools/Antiphon.LandGraphAudit -c Release --property:OutputPath=bin-c1199auditcheck/ -maxcpucount:1 -nodeReuse:false && pwsh -NoProfile -File scripts/audit-land-verification-graph.ps1 -Check -ToolPath tools/Antiphon.LandGraphAudit/bin-c1199auditcheck/Antiphon.LandGraphAudit.dll`
  - Make the row non-vacuous: require the audit's own success line in `rows/CP-46/console.log`,
    and the DLL digest the plan already says to record, not exit 0 alone.
  - Re-import afterwards.
  - The CARD-1446 CP-6 row (`pwsh -NoProfile -Command "..."`) has the same shape. File it if it
    was ever credited on Windows.
- **Round-1 D-1 itself is closed.** `RunScheduler.cs:181-185` lets a ready command row claim the
  idle lane ahead of builds, and `Pick` (`:739-751`) never waits on a command row's Build cell.
  CP-46 does run first in the S-F set. Because it now builds its own evaluator inside the row (the
  shell `&&` supplies the dependency), the output exists when `-Check` runs.
  `LandGraphAudit.csproj` references only `Antiphon.LandGraphAudit.Core`, so the inventory to
  clean up is two `bin-c1199auditcheck/` roots plus `obj/Release`.

**D-2 (round-1 D-2 partly open): the separate master port is not executable as planned.**

- **Where:** plan D-9 and "Landing target and prerequisite order" (the "separate reviewed master
  port of each accepted test fix"); CP-36, CP-45 and CP-53.
- **Failure:**
  - `origin/master` (`98fc3ceb6`) still has `LinuxOnlyWindowsExclusions` with count 8
    (`ReleaseGatePlatformTests.cs:33-34`, `test-rc-platform.ps1:505,1039`). It has no
    `NativeLinuxPolicyWitnessTests` and no CARD-1613 commit.
  - On master, CP-36 would therefore match 2 tests, not 3, and CP-45 would match 6, not 7. Both
    rows fail.
  - S-A's gap-identity edit and the new `C1199_WindowsRequiredRoster` case would land in the
    pre-1613 function. That is a textual conflict with any later 1613 port.
  - S3's `scripts/lib/nightly-watchdog-admission.ps1` is absent on master, `rc/2026-10-27` and
    top-up 38 (`fcfa49ab`). CP-53's `admission-keeps-suite-watchdog` assertion can't exist on
    master, and on rc it needs S3 landed.
- **Fix:**
  - State the master-port prerequisites: the CARD-1613 master port lands first, or name a
    master-specific CP-36/CP-45 variant with the old name and counts.
  - Make S3 on the target a hard prerequisite of S-F-cost on each line. Today it is only the
    "simpler default", which the caller may waive.
  - Re-import.
  - The rc side is closed:
    - The wrapper is `LinuxOnlyWindowsExclusionPolicy` with six C1323 assertions on both 1613
      `0f6058ac9` and top-up `130ea525a`.
    - Its synthetic gap still names `shape_conflict_rule` at `:624`.
    - S/T/O are in `$decisionClasses` and absent from the NativeLinuxOnly inventory.
    - CP-36 = 3 and CP-45 = 7 are correct there, with no suffix collisions.
  - The classification is correct: all six slices are test fixes for rc code, so they go to both
    lines; nothing here is master-only.

## Round-1 D-3: closed

- Every cost edit targets `scripts/lib/nightly-class-costs.json`. On the top-up, O is `25 s / 6`
  measured in run 8f055f87, which had 5 failures (`:3714-3719`). S and T are absent, so they are
  priced at the default 0.5 s/node (`nightly-tests-impl.ps1:137`), not the "roughly 1s/node" the
  plan says.
- The numbers 771.72, 797.40, 448.44, 56.273, 164.99 and 166.16 all appear in the investigation.
  Recomputed sums: 711.93 = sum of the S-A rows; T ≈ 797.40 + 476.5; O ≈ 448.44 + 56.27.
- **S3 admission:** a corrected cost does not change what S3 admits or refuses.
  - `Resolve-NightlyLaunchWatchdog` refuses only planned group seconds above 2400
    (`launch-estimate-ceiling`) or invalid values.
  - T ≈ 1270 s goes alone, under the lone-class budget of 2400 (`nightly-tests-impl.ps1:143`).
    S ≈ 770 s and O ≈ 505 s are each below the ceiling.
  - The call site (`:1413`) passes no `-Calibration`, so the suite watchdog stays at 3600 s.
  - The effect is packing and planned seconds only. Stale prices hide about 2,500 s inside
    900 s-target groups, which is a watchdog-kill risk rather than an admission flip.
- **Policy hash:** `Get-NightlyPolicyHash` (`nightly-policy.ps1:67`) hashes policy JSON only. The
  cost change moves only `CostFileDigest` (S3 `nightly-tests-impl.ps1:1312`), as the plan says.

## Round-1 advisories: all taken

| Advisory | How it was taken |
|---|---|
| A-1 | CP-21 and CP-24 deadlines raised to 6 minutes |
| A-2 | Mandatory wrapper, six named assertions, and compiled-roster equality. `CheckpointRoster.CompiledCases` and `ScriptHarnessProcess.AdditionalArguments` both exist |
| A-3 | Git environment variables enumerated; `GIT_ATTR_NOSYSTEM` choice stated |
| A-4 | Explicit `RestoreConfigFile` |
| A-5 | CP-51 has a distinct name, outside CP-45's suffixes |
| A-6 | CP-52 added |
| A-7 | Separate Linux and Windows lane budgets |
| A-8 | Plan-only publication |
| A-9 | Conditional `linuxSkips`, not a blocker |

## Lanes, importer, premise, regressions

- **Lanes:** coherent apart from D-1 and A-1 below. CP-39 is Linux-only (`CheckpointFixtureRoutingTests`
  is in the Linux-only inventory). CP-40 and CP-44 are Windows-only. CP-53 is offline on either lane.
- **Importer:** `pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1199-r2review-import -SlotWaitMinutes 10 -- dotnet run --project tools/Antiphon.Checkpoints --property:OutputPath=bin-r4d98fbcb/ -- import --plan <plan> --out .antiphon/review-4d98fbcb/imported.yaml`
  - Exit 0; lease `2cbc7906`, waited 1 s, held 10 s.
  - 53 rows and 8 builds. Estimates sum to 223 whole minutes.
  - Per lane: 221 minutes on Linux (all rows except CP-40 and CP-44) and 222 on Windows (all
    except CP-39), matching the plan.
  - Same CP-41 and CP-42 derived-72-minute advisories as before, which the overlay addresses.
  - CP-1..35 have 35 distinct exact filters.
  - CP-36 = 3, CP-45 = 7, CP-53 = 1; CP-1 estimate 11; CP-46 estimate 5.
  - All 19 T/O names plus `missing_receipt_is_not_negative_success` and `shape_conflict_rule`
    resolve exactly once at the top-up. The 16 Arguments map one-to-one to the 16 planned shape
    names. S has 2, T has 13 and O has 6 `[Test]` methods, so CP-41 = 17, CP-42 = 13 and
    CP-43 = 5 hold.
  - `bin-r4d98fbcb` was removed from both projects.
- **Premise:** still true on the top-up 38 tree.
  - `tests/test-execution-policy.json` at `fcfa49ab` (and on master) has no S/T/O exclusion.
  - S/T/O carry no NativeLinuxOnly marker.
  - Census patterns are unchanged at `checkpoint-usage.ps1:672-674`.
- **Regressions:** none.
  - The only per-row changes in the word diff from `c31d947b5` are the CP-21 and CP-24 minutes,
    2 → 3.
  - The per-row seams and red witnesses are unchanged; no assertion was weakened.
- **Gates:**
  - `check-evidence-diff.ps1 -Repository . -BaseRef 421c3de68 -HeadRef HEAD`: 6 commits,
    0 entries, 0 violations.
  - `git merge-tree` of the plan tip with top-up `fcfa49ab` is clean.

## Advisories (round 2)

- **A-1 (CP-37 sequencing):** "obtain both lanes' CP-37 assertion failures, then author the fix"
  asks a Linux Code task to wait for Windows evidence it cannot produce. The red SHA is immutable,
  so Windows can run CP-37 at it after the fix is pushed. Drop the ordering, or split S-B1 Linux
  into separate red and green commissions.
- **A-2 (stale refs):** the plan's "observed `rc/2026-10-18`" is stale. Active rc refs run to
  `rc/2026-10-27` (`ade54222`, which is also the investigation SHA), and top-up 38 sits on it.
  The plan cites top-up at `952c22e93`, but its tip is now `130ea525a`; the wrapper name and count
  are unchanged.
- **A-3:** the plan says S/T were priced at "roughly 1s/node"; the actual default is 0.5 s/node.
- **A-4:** S3's call site passes the chunk's `costBasis`, not a hard-coded `default`. The
  conclusion holds because no calibration is passed.
