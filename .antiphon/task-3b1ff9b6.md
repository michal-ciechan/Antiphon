# Review 3b1ff9b6: CARD-1199 Windows-runnable plan

Subject: `docs/superpowers/plans/2026-10-30-card-1199-windows-runnable-plan.md` at
`c31d947b5c3ebaff482d46cfa999068ac30f9922` (plan task 42241722, source base `fca55bdda`).
This is a read-only plan review on a server2 Linux mirror. It ran no product tests and none were
required: the plan's CP rows exercise code that does not exist yet.

**Verdict:** the policy-premise correction is true, and most of the design is sound. Three defects
(D-1..D-3) need a plan amendment before Code.

## Premise check (item 1): confirmed

- `tests/test-execution-policy.json` has no exclusion for CheckpointShapeConflictTests (S),
  CheckpointTrackedBaselineTests (T) or CheckpointOutputConfinementTests (O).
- `scripts/test-rc-platform.ps1:561` lists all three in `$decisionClasses`, and
  `decision-classes-are-not-class-excluded` refuses any exclusion for them.
- The synthetic gaps at `:997-999` are pushed through the real `ConvertTo-NightlyCoverageVerdict`.
  `scripts/lib/nightly-coverage.ps1:1038-1040` turns any NotExecuted identity into
  `coverageComplete=false` with reason `unexecuted-outcome:<ids>`.
- So today Windows treats these 35 as skipped but required. The gate neither passes nor hard-fails:
  coverage is reported incomplete, so the run is not Full.
- Removing the runtime gates without touching the policy is correct. The policy-removal roster is
  empty.
- Pins that move with this change:
  - The synthetic S gap identity `shape_conflict_rule` (named in the plan).
  - `Get-NamespaceCensus.windowsSkips` in `scripts/lib/checkpoint-usage.ps1:669-679`. The plan
    names the per-slice counts 23 -> 15 -> 10 -> 3 -> 0.
  - `selected` is computed from source, so there is no count pin to edit.
  - The historical `scripts/fixtures/nightly/c1410-antiphon-listing.txt` row stays historical
    (stated in the plan).
- Extra benefit: today on Windows the pattern `CheckpointOutputConfinementTests.*` declares the
  passing `windows_confined_build_leaves_use_app_host_unset` as a skip, which is already a mismatch.
  S-A's exact-method list fixes it.

## Defects

**D-1: CP-46 runs before the build it depends on.**
- Where: plan `### Checkpoints` CP-46 and the S-F row set; `tools/Antiphon.Checkpoints/Execution/RunScheduler.cs:181-185,739-751`.
- Failure: in the S-F invocation (`--rows CP-41,...,CP-46`), CP-46 is a serial command row. The
  scheduler treats a command row as ready while builds are still pending, and `readyExclusive`
  stops the first build from starting. So CP-46 runs first, before
  `tests/Antiphon.Tests/bin-c1199final/land-graph-audit/Antiphon.LandGraphAudit.dll` exists, and
  the audit fails.
- Running CP-46 later on its own does not work either: the plan removes owned outputs after each
  joined run, so no `bin-c1199final` exists then.
- Why it matters: R-3 acceptance can't go green as written. A red here would look like a
  graph/pin problem when it is really an ordering artefact.
- Fix: give CP-46 a tool that exists independently. For example, a named build-slot bootstrap of
  `tools/Antiphon.LandGraphAudit` to an owned `bin-<x>/` (as the script header at line 9 suggests),
  with CP-46 pointing `-ToolPath` at that absolute owned path. Alternatively, run CP-46 in a second
  invocation whose prerequisite build is stated and kept. Say which, and keep the one-`-Update` rule.

**D-2: the CARD-1613 collision and the landing target are not addressed.**
- Where: the plan has no landing-target or rc-port section; CP-36 and CP-45 filters; S-A edits to
  `scripts/test-rc-platform.ps1` and `ReleaseGatePlatformTests.cs`.
- Failure: CARD-1613 is in flight on rc (`origin/feat/card-task-06fc8af7`, tip `0f6058ac9`, fix
  round 4). It:
  - renames the TUnit wrapper `LinuxOnlyWindowsExclusions` to `LinuxOnlyWindowsExclusionPolicy`;
  - drops that wrapper's assertion count from 8 to 6;
  - rewrites `Test-LinuxOnlyWindowsExclusions` (392 changed lines in `test-rc-platform.ps1`; the
    gap list moves to `:616`).
- Once 1613 is in the target, `(LinuxOnlyWindowsExclusions*)` no longer matches. CP-36 then runs 2
  tests instead of 3, CP-45 runs 6 instead of 7, and both fail their row. S-A's gap-identity edit
  and its new `C1199_WindowsRequiredRoster` case also conflict textually with 1613's rewrite.
- Separately, the plan never says whether these slices land on master or rc, and never covers the
  rc port or ordering against the open rc top-ups (S3, slice B, manifest publication,
  CARD-1609/1610/1615). The brief asked for that analysis.
- Fix:
  - State the landing target and rc-port path.
  - Order S-A after CARD-1613, or plan the merge explicitly.
  - Write CP-36/CP-45 against the name that will exist at the Code base. Re-run the importer.
  - Note that 1613 marks only policy-excluded rows with `[NativeLinuxOnly]`. S/T/O are not in its
    25+20 rows, so the reflection witness does not touch these runtime gates, but the plan should
    say so.

**D-3: class-cost rows point at the wrong file, and the stale O cost is not handled.**
- Where: plan "Measurement provenance and Full cost", last paragraph.
- Failure: the plan says to record successful distributions "before changing
  `test-execution-policy.json` cost rows". The policy has no cost rows. Class costs live in
  `scripts/lib/nightly-class-costs.json`:
  - O is recorded at 6 nodes / 25 s, measured in a run where 5 tests failed early.
  - S and T have no row at all.
- Once these rows are enabled, Windows O is about 450 s (the 448.44 s passing control), T about
  1270 s and S about 770 s. Launch packing targets 900 s per group (`scripts/lib/nightly-tests-impl.ps1:142`),
  so the stale costs will mis-pack Windows Full groups.
- Why it matters: it undermines the 40-50 minute Windows Full forecast and the group/watchdog
  budgets, and it sends Code to the wrong file.
- Fix: name `scripts/lib/nightly-class-costs.json` (and `scripts/refresh-nightly-class-costs.ps1`).
  Require the measured-cost update, or an explicit statement of the packing consequence, in
  S-F/Debug before Windows Full credit is claimed.

## Advisories

- **A-1 (timeouts):** CP-21 and CP-24 get 4-minute row deadlines.
  - That is about 1.45x the 164.99 s and 166.16 s first-run samples. Those failures were late in
    the body ("Owned script Lib.dll should exist"), so both builds had run.
  - It is also shorter than each method's own `[Timeout(300_000)]`.
  - The passing control gives only a 448.44 s aggregate for the five cases. Use 6 minutes, or cite
    a per-case split.
  - The other per-row deadlines are about 2x their samples (2-6 minutes), and the batch deadlines
    for CP-41/42/43/47 come out at 2.2-2.7x. Sane.
- **A-2 (roster):**
  - CP-36 needs a `C1199_WindowsRequiredRoster` TUnit wrapper, but S-A says "only if a named
    wrapper is needed". Make it mandatory and name its assertion strings and count.
  - Bind the roster's 35 identities to compiled cases (`CheckpointRoster.CompiledCases`). A
    hard-coded list stays green after a rename, because the policy has no row to disagree with.
- **A-3 (Git isolation):**
  - The helper design is complete for config sources: global/system replaced, `NOSYSTEM`,
    `HOME`/`USERPROFILE`/`XDG_CONFIG_HOME`, `GIT_CONFIG*` plus `KEY_n`/`VALUE_n`, and local
    autocrlf pinned.
  - includes.path, safe.directory, credential helpers and init.defaultBranch all come only from
    global/system config, so they are dropped. That matches production `HardenGit`
    (`scripts/lib/CheckpointProcessRunner.cs:95-108`). Say so in the plan.
  - "repository/index redirectors" is vague. Enumerate them from `git rev-parse --local-env-vars`
    (`GIT_DIR`, `GIT_WORK_TREE`, `GIT_INDEX_FILE`, `GIT_OBJECT_DIRECTORY`,
    `GIT_ALTERNATE_OBJECT_DIRECTORIES`, `GIT_COMMON_DIR`, `GIT_PREFIX`, `GIT_NAMESPACE`,
    `GIT_GRAFT_FILE`, `GIT_SHALLOW_FILE`, `GIT_REPLACE_REF_BASE`, ...) plus
    `GIT_CEILING_DIRECTORIES`. Also state the `GIT_ATTR_NOSYSTEM` choice; production does not set it.
  - The red witness is genuine: autocrlf=true reached through a hostile per-child HOME/system
    config makes the index hold LF bytes while the CRLF file stays in the worktree. It reproduces
    on Linux too.
- **A-4 (Linux behaviour):**
  - Moving HOME on Linux drops `~/.nuget/NuGet/NuGet.Config`.
  - Windows keeps `%APPDATA%\NuGet\NuGet.Config` because APPDATA is not moved.
  - The restore-source change is therefore asymmetric. Preserve the effective NuGet config
    explicitly.
- **A-5:** the "separate diagnostic legacy control" has no name or CP row. If it is a `[Test]`
  whose name starts with `fixture_commit_matches_hardened_status_under_hostile_config`, the CP-45
  count (7) breaks.
- **A-6:** S-C1 replaces the Windows `WriteProject` route. If `shape_staged_untracked` (CP-1)
  moves onto the pwsh fixture, add it to the S-C1 requalification set. CP-49 covers only the four
  collision cases.
- **A-7:** the two-lane slice budgets assume one Code task produces both Windows and Linux
  evidence. A single delegate cannot. Plan the second-lane task per slice; the plan allows it, but
  the slice ceilings still include it.
- **A-8 (land):** `git merge-tree --write-tree origin/master c31d947b5` conflicts (add/add) on
  `.antiphon/task-af2b78c8.md`, which comes from the investigation base, not the plan. Land only
  the plan range `fca55bdda..c31d947b5`, or land after the investigation lands.
- **A-9:** the Linux namespace census already omits Windows-only checkpoint skips (for example
  `CheckpointWindowsAnchoredPathDeleteWitnessTests`, `OutputCleanupTests.windows_*`). The plan's
  conditional "add the O sibling to linuxSkips if selected" matches current practice. Don't make
  it a slice blocker.

## Samples verified (item 2: 11 rows across slices)

| Row(s) | What was checked |
|---|---|
| CP-1, 4, 5, 6, 7 (S-A) | Judge branches for staged-untracked and the four collisions need the named refusal, exit 2, user bytes and the receipt. The new marker under `.antiphon/` is invisible to confinement (`BuildConfinement.cs:326,388`; `run-checkpoint.ps1:1008`), so marker-absent is a real build-did-not-run witness. |
| CP-11 (S-A) | Script-only `Get-CheckpointTrackedSnapshot` body read. |
| CP-26..35 (S-C) | Ports match the Linux commands one-for-one (`CheckpointShapeConflictTests.cs:322-333`) and run the same tool/script/Judge. Added assertions only strengthen them. |
| CP-21..25 (S-B2) | Methods and gates exist. O gate at `:480`; T gates via `RequireLinux` at `:616-619`. |
| CP-3 | Test-local only, disclosed by the plan. |

- A skipped row fails "exactly N passed; 0 skipped", so a no-op Windows run cannot pass any CP-1..35 row.
- All 35 method names exist. S has 16 `[Arguments]` plus 1 = 17; T has 13; O has 5 portable plus 1
  Windows sibling.
- Every class and method the CP-36/38/39/45 filters name resolves at base, including
  `CheckpointNamespaceCensusUsageTests` (`CheckpointTempUsageTests.cs:367`), `C1233_TokenCallers`
  (4 cases) and `C1385_RegistryMatchesNarrowGraph`.
- The S-B1 seam is real: `IConfinedDriver` (`Execution/IDriver.cs:40`), `ProcessDriver`, and
  `DriverRequest.Environment`.
- S-C fixture ports reuse the same production seam, weaken no assertion, and exclude nothing.
- R-2 stays open as an operator decision (default pending/required). The aggregate Debug obligation
  stays separate. No input-wait behaviour changes.

## Importer (item 6)

- `pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1199-review-import -- dotnet run --project tools/Antiphon.Checkpoints --property:OutputPath=bin-r3b1ff9b6/ -- import --plan <plan> --out .antiphon/review-3b1ff9b6/imported.yaml`
  - Exit 0; lease 5cb5af8a, 0 s wait, 27 s held.
  - `imported 50 rows`, 7 builds, whole-minute estimates.
  - Two advisories, CP-41 and CP-42 at a derived 72 minutes, which the plan's overlay addresses.
  - 35 single-target rows CP-1..35.
- No suffix collisions in the OR filters at base (A-5 is the one future risk).
- `bin-r3b1ff9b6` was deleted from both projects.

## Other gates

- `scripts/check-evidence-diff.ps1 -Repository . -BaseRef fca55bdda… -HeadRef HEAD`: exit 0,
  6 commits, 0 entries, 0 violations.

## Slice sizing (item 7)

- Every ceiling is at most 150 minutes: 110/150/100/125/125/150.
- Each slice leaves the remaining rows required/incomplete, so each lands on its own.
- D-7 (no definition-file change) holds: new `.cs` files only.
- The one `-Update` in S-F is fine once D-1 is fixed.
