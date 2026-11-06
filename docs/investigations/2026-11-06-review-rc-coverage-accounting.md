# Review: rc coverage accounting, Windows-only policy rows (round 1)

Review task 8eb266f0 (Claude; the author was Codex). Subject: `origin/feat/card-task-07ab1e49`.
Code and landing owner: 07ab1e49-119c-4b0b-aba6-3706085e27f8 (CARD-1726).

| Item | Identity |
|---|---|
| Reviewed tip | `13e9c1c8b01916bf1e26a18e8945a2b209192b20` |
| Red witness SHA | `53d2a87aec366d381e3ca9bf947d4160df3e8fb1` |
| Policy fix SHA | `5e6a1e703082395ff633f8e70ac265a3d591d61a` |
| Reviewed range | `cc4ab8e31349db759181a2931e8840ec6d79805f..13e9c1c8b01916bf1e26a18e8945a2b209192b20` (5 commits; `git cherry` marks the three batch-85 commits below them `-`, already on master) |
| Master checked | `0dbd8295a` (inherited-red base), `3b7d80c66` (merge-tree) |
| Verification | Interim, as the brief commissioned: targeted runs only, no whole Unit lane (CARD-1720), no E2E rerun |

Verdict: **no blockers**. All 11 Windows-only rows exclude only methods that skip unconditionally on
Windows at runtime. Every portable sibling stays required. Coverage code is byte-identical to base and master, and
both digests are correct and agree. Seven advisories follow (A-1 should be fixed before
SourceLanding Mutation runs PC-3).

## 1. The 11 Windows-only rows

Each excluded method was read with its gate. All gates are unconditional Windows skips
(`!OperatingSystem.IsLinux()`, `OperatingSystem.IsWindows()`, or `NativeLinuxGate.Require`, which
is `!IsLinux() -> SkipTestException`). None is a slow-or-failing exclusion. Every new
`[NativeLinuxOnly("...")]` text equals the runtime skip reason byte for byte (34/34 markers
compared mechanically).

| Row (suite / class) | Scope | Gate | Portable siblings kept required |
|---|---|---|---|
| pty-host / LinuxCustodyReleaseHostTests | class (13/13 methods) | all 13 `!IsLinux` "The inbox pty launch runs on Linux." | none exist |
| agents-pty / Card1685LinuxCustodyRunnerTests | 1 method (class has 1) | `!IsLinux` | none exist |
| agents-pty / LinuxCgroupContainmentTests | 1 of 11 | `!IsLinux`, `unshare` | 10 |
| antiphon / TempRootSweepAnchoredDeleteTests | class (9/9) | all 9 `!IsLinux` | none exist |
| antiphon / LandingGitTests | 14 of 42 | 7 `IsWindows` skip, 6 `!IsLinux`, C1714 `IsWindows` (POSIX mode bits) | 28, including C448_V*/C1076_RunOptionsBudget* native rows |
| antiphon / OwnedProcessTreeTests | 2 of 19 | `!IsLinux` (live Linux grandchild, `/bin/sleep`) | 17 |
| antiphon / CheckpointReceiptStallBudgetTests | 1 of 5 (2 argument rows) | `!IsLinux` | 4 |
| antiphon / PosixHarnessToolTests | 2 of 7 | `NativeLinuxGate.Require` | 5, including `Qualification_timing_completes_without_mkfifo_chmod_or_ln_on_path` |
| antiphon / AgentReportStoreBudgetTests | 1 of 11 | `NativeLinuxGate.Require` | 10 |
| antiphon / CardRelationshipScriptTests | 1 of 10 | `!IsLinux` (Linux PTY) | 9, including `Block_fails_loudly_when_relationships_returns_404` |
| antiphon / C0824DesktopLegacyReleasePolicyTests | split method `Status_that_cannot_read_a_posix_directory_holds_it` | `IsWindows` skip in the shared helper, reached only by this method | `Status_that_cannot_read_the_whole_tree_holds_it(long-path-config)` and 36 others |

Every affected class has exactly one source file and no partial (also true in the merged tree at master 3b7d80c66). So
the matcher's simple-name, case-insensitive class match cannot reach another class. The batch-85
C1677 rows are unchanged: the policy diff vs base is purely additive apart from `policyHash`. No
CARD-1199 identity and none of the `test-rc-platform.ps1` decision classes or methods is excluded (the
census case `decision-classes-are-not-class-excluded` passes).

## 2. Accounting unchanged; where the Linux obligation lives

`git diff` of `scripts/lib/` and `scripts/nightly*.ps1` is empty, both vs base cc4ab8e31 and vs
master. `nightly-coverage.ps1` (missing-expanded-row, unexecuted-outcome, zero-result) is
untouched. The admission witness drives the real `Get-NightlyProfileExclusions`,
`Get-NightlyDiscoveryDisposition` and `ConvertTo-NightlyCoverageVerdict`. Its
`required-skips-stay-incomplete` row proves a required NotExecuted still poisons coverage.

The rc qualification is Windows-only. Linux rc is a `PlatformDiagnostic` (docs/release-gates.md,
RC coverage), so nothing in the same qualification executes these methods. They never executed on
Windows either: they were launched and Skipped, which was why coverage was incomplete. The change
therefore removes no executed Windows assertion. It turns 46 undeclared skipped methods (47 expanded
results; `healthy_receipts_*` has two argument rows) into owned exclusions. The Linux side stays required in the rc Linux profile. The proof
is the `linux-retains-native` row in `scripts/test-rc-coverage-admission.ps1`, whose inventory
(lines 9-57) is the roster. Mutant M3 (drop `platform`) turns it red. Linux execution itself
remains the existing Linux checkpoint and Unit/Integration lanes, which these rows do not affect.

## 3. Separate manual obligations

`LiveLaneIsolationTests.StartupArgvMatchesProductionApplyOff` (CARD-1545) and
`AgentReportStoreLoadTests.Store_samples_record_normal_and_contended_phases` (CARD-1605) are
method-only, all-platform rows. Their reasons name the separately commissioned workflow. The
other LiveLaneIsolationTests methods stay required (the planner turns the partly excluded class
into exact method chunks). The excluded UID is recorded in the suite row's `exclusions` and
`excludedUidCount` with owner and reason (`nightly-tests-impl.ps1:1236-1238`), so it is declared,
not silently dropped. Both rows were appended to the frozen non-native digest (89 to 91 records,
`be806b71...`), and the compiled witnesses in all three affected suites validate it.

## 4. Digests (recomputed at the tip)

- `Get-NightlyPolicyHash` = shipped `policyHash` = `Read-NightlyExecutionPolicy().Hash` =
  `0ac7b7c0dfcf6949ef1714fcfa02d4acd58e6258a8556209248338408f8f46a2`.
- `test-live-lane-run.ps1 -PrintInputDigest` = pinned `PinnedInputDigest` =
  `f510daab843687c8a873d4d3167e01088be50987e796d693729db0418ed16ada`.
- Master 3b7d80c66 has base-identical bytes for all five protected inputs, the policy, the pin
  script and `NativeLinuxPolicyWitness.cs`. The merged tree (below) carries the tip's bytes, so
  both digests remain valid after the land.

## 5. CP-9 aggregate red and CP-8 receipt

`pwsh scripts/test-nightly-tests.ps1` (one lease each) gives the same result at master 0dbd8295a and at the tip: 51 PASS
and 3 FAIL (`G039 entry missing expanded uid`, `G040 partial selection not complete`,
`G040 production discovery generated`). The FAILs come from `Invoke-E` throwing `unmatched project
scripts/fixtures/nightly/c487-probe/Probe.csproj`. Then `C487_G044` calls
`Invoke-NightlyOwnedProcess`, which throws `Nightly native custody requires Windows.`
(`nightly-owned-process.ps1:39`) and aborts the harness before its summary. The PASS/FAIL row sets
are identical (`diff` empty). This is **inherited**, Linux-by-construction, and none of it is credit or
debit for this branch.

CP-8 (E2E `ReleaseGateReportDeliveryTests`, 2/2 passed) was refused with `output-ledger-mismatch` at
`bin-07ab-e2e/C:/logs/antiphon/check-interpreter`. `AntiphonAppFixture` overrides
`Delegation:DiagnoseWorkingDirectory` and `OutputDistillerWorkingDirectory` (lines 646-649) but not
`CheckInterpreterWorkingDirectory`. So the host keeps `server/appsettings.json`'s
`C:\logs\antiphon\check-interpreter`, which on Linux is a relative directory created under the test
output (the host cwd), outside the checkpoint's output ledger. The branch changes no server, E2E or
configuration file, so this too is inherited (A-5). It was not rerun.

## 6. Red, green and mutants

Script witnesses ran from a detached scratch worktree at the tip. Compiled witnesses ran from a copy
of the tip build inside that tree, so `RepoRoot()` read the mutated policy. Every mutant was resealed
(`policyHash`) where the policy changed, and restored with `git checkout --`. The final status was clean.

| Run | Result |
|---|---|
| Admission witness at red SHA 53d2a87 (red script; tip script on the red tree) | both 2 passed / 5 failed |
| Controls at tip: admission / C1323_PlatformPolicy / LinuxOnlyWindowsExclusionPolicy / C599_ProfileSchema / WrapperOnlyPaidManifest | 7/7, 20/20, 6/6, 9/9, 21/21 |
| Compiled NativeLinuxPolicyWitnessTests at tip: antiphon / pty-host / agents-pty (each with TestClassificationGuard for the pty suites) | 3/3, 4/4, 4/4 |
| M1 own: drop LinuxCgroupContainmentTests row | admission red (exact-windows-dispositions, coverage-sweep); C1323 red (card-1614-mixed-platform-rows); census red (marker-without-row); agents-pty compiled red 2/4 |
| M2 own: portable sibling `C1614_Missing_tree_throws_before_termination` into that row | admission red (exact-windows-dispositions); C1323 red; census red (row-without-marker) |
| M9: drop CardRelationshipScriptTests row (PC-1) | admission red; census red; antiphon compiled red 2/3 |
| M10 own: portable sibling `Block_fails_loudly_when_relationships_returns_404` into that row | admission red; census red; antiphon compiled red 1/3 (Policy_rows_require_pinned_attribute) |
| M12: drop LinuxCustodyReleaseHostTests row (PC-1) | pty-host compiled red 2/4 |
| M3: OwnedProcessTreeTests row to all platforms (PC-2) | admission red (linux-retains-native) |
| M4/M11: LiveLane / load-sample row broadened to class (PC-3) | admission exit 1 by **exception**, see A-1 |
| M5: drop load-sample row (PC-4) | admission red on 4 named rows |
| M6: remove the Cutover_window marker (PC-5, source census) | census red (markers-and-rows-agree) |
| M7: stale policyHash (PC-6) | admission and C599 refuse with `stale policy hash` |
| M8: stale PinnedInputDigest (PC-6) | WrapperOnlyPaidManifest red (full-inputs, crlf-inputs) |

M1 and M2 against the antiphon compiled witness stayed green, which is correct. That witness inspects only
`antiphon` rows, and the agents-pty witness caught M1 (M13). The compiled PC-5 (marker omission) needs a
rebuilt assembly and stays pending for Mutation, like every PC.

## 7. Sweeps and guards

One Antiphon.Tests build, native default output, `UseAppHost=false`, one lease per build and run:

- Run 1: `(TestClassificationPolicy*)|(TestClassificationGuard*)|(SlowTestRegistryMerge*)|(SlowTestTripwire*)`:
  47/47 passed, 0 skipped (39 / 1 / 5 / 2), 2m24s.
- Run 2: `(NativeLinuxPolicy*)|(RcCoverageAdmissionTests*)|(MechanicalPcContractTests*)`: 29/29 passed
  (NativeLinuxPolicyWitnessTests 3, WitnessMutation 12, MatcherParity 1, Execution 1,
  RcCoverageAdmissionTests 1, MechanicalPcContractTests 11), 25s.
- PtyHost.Tests and Agents.Pty.Tests builds and witness runs: as in section 6. These are the plan's pending CP-6/CP-7
  classes, green here.
- `check-evidence-diff.ps1`: base cc4ab8e31: 5 commits, 0 entries, 0 violations; merge-base
  388ff4820: 8 commits, 0 violations.
- Merge-tree: raw `git merge-tree --write-tree origin/master HEAD` **conflicts** (A-2). With
  `--merge-base cc4ab8e31` against master 3b7d80c66 it is clean, tree `b4d0fb27d726189b769e9ffd6c1b03d769c82511`.

Not run here (Interim): whole Unit lane (CP-3), E2E CP-8 rerun, the remaining CP-2 classes beyond the
sweep above, Windows M-2 and the M-3 commissions. Every PC stays pending for SourceLanding Mutation.

## Blockers

None.

## Advisories (cards; none holds the land under D-2)

- **A-1** `scripts/test-rc-coverage-admission.ps1:91`. PC-3 variants (a manual row broadened to
  class scope, i.e. `methods` removed) crash with `Cannot index into a null array`. `@($null).Count`
  is 1, so the check falls through to `$rows[0].methods[0]`. The witness goes red by exception
  before any named row, so Mutation's PC-3 ("must fail portable-siblings/exact-windows-dispositions")
  cannot see its expected assertion. Fix: `$m = @($rows[0].methods | Where-Object { $_ })` and test
  `$m.Count -ne 1 -or $m[0] -cne $obligation.m`. Fix before Mutation runs PC-3, or Mutation must accept
  the frozen-digest red instead.
- **A-2** The branch still carries batch 85 as different commits (19b9e6951, 88ed031df,
  cc4ab8e31; master has their equivalents). A raw merge-tree against master conflicts on
  `tests/test-execution-policy.json` and `scripts/test-live-lane-run.ps1`. The rebase land drops the
  three `-` commits and replays the five new ones cleanly. Land by rebase only. The Code report's
  conflict-free merge-tree was against the older master 1527c29f.
- **A-3** The `PosixHarnessToolTests` and `CardRelationshipScriptTests` rows are owned by CARD-1323. The
  Debug named no owner, the tests came from other work, and `C1323_PlatformPolicy` now pins these two
  keys exactly, so a later owner correction needs a fixture change too.
- **A-4** docs/release-gates.md (RC coverage) does not list the two separately commissioned
  all-platform obligations (CARD-1545 parity consumer, CARD-1605 load sample). An operator reading the
  owner doc cannot find them.
- **A-5** (inherited) `tests/Antiphon.E2E/Fixtures/AntiphonAppFixture.cs:646-649` does not redirect
  `Delegation:CheckInterpreterWorkingDirectory`. Every Linux E2E checkpoint row that boots Program leaves
  `C:\logs\antiphon\check-interpreter` under its output and is refused `output-ledger-mismatch`.
- **A-6** (inherited) The full `test-nightly-tests.ps1` run on Linux aborts at G044 (Windows-only
  custody throw) without a summary, after the three Invoke-E FAILs. A Linux run of the whole harness
  can never be credited; G044 needs a recorded Windows-only disposition rather than an abort.
- **A-7** (out of scope, master) CARD-1725's `C1725_LargeAsciiCallers` has no Linux gate. It sits in
  `CheckpointInheritedFileTests`, which carries a class-level `[NativeLinuxOnly]` and a Windows class
  row (CARD-0590), so the new ASCII-scan witness never runs on Windows.

## Costs

Slot waits ranged from 0 to 300 s per lease, typically 2 to 5 minutes. Antiphon.Tests build: granted after 165 s, held 150 s.
Pty builds took 28 s and 15 s. Nightly harness held 111 s at the tip and 131 s at master. The admission
witness took 13 s per run. Every build/obj output created here, the scratch worktree and the
scratch archives were deleted by literal path. Retained logs and TRX files are outside the repository.
