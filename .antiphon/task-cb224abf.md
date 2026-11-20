# CARD-1820 S3 owner map: Review round 3 (Final, blockers only), task cb224abf

- Subject: origin/feat/card-task-1e0a0939 at b43f1f5c1ef202a2569ede736810e05af714e8bb. Author: Grok. Reviewer: Claude Opus.
- Diff reviewed: e86d138eb93c83fb176d44780f6bc1dd00a46d59..b43f1f5c1ef202a2569ede736810e05af714e8bb (3 commits: 0bcf909a2, bb5de1e7d, b43f1f5c1).
- Original Code landing owner: 1f7120d3-1bdc-4ae5-9e50-bae4a216f741. Fix tasks: 68b1380d, 1e0a0939. Earlier rounds: 0b1b4c83, d7414a53.

Verdict: BLOCKERS (2). D-1 persists in two new path classes. This is round 3, so it goes to the operator.

The fix claims that two scan classes are "proven closed" by the census. One of them is not. The census
resolves only literal FILE paths, so a test that walks a literal DIRECTORY is invisible to it. Two
such readers select fewer suites than their true owners.

## Counts

- Author census, re-run in CP-6 at the tip with broadening disabled: paths-checked=11132,
  suites-checked=27, pairs-checked=787, under-selected=0. This matches the claim, but the census
  cannot see directory walks.
- Independent inventory: I ran the shipped `Read-RcInputInventory -SkipUnresolvedBroadening` at
  b43f1f5c1.
  - 27 suites, 185,150 present records, 18 unresolved items.
  - 11,132 tracked paths x 27 suites.
- Independent truth check. This is my own scan, not derived from the author's literal scan:
  - Literals: string literals, including interpolated and verbatim strings, from each suite's
    compiled sources (its project tree plus linked Compile items, globs resolved).
  - Matching: each literal and each of its `/` segments was matched against tracked files, tracked
    directories, basenames (no hit cap) and path suffixes.
  - Enumerations: a second pass covered every directory enumeration (`GetFiles`, `EnumerateFiles`,
    `GetDirectories`, `ls-files`, `ls-tree`, `Get-ChildItem`, `DirectoryInfo`). It joined the
    literal runs within the 6 lines above each call into a tracked directory, then required every
    file under it to be owned by the suite.
  - Raw output: 191 literal hits and 32 enumeration hits. Each was triaged by reading the code.
- **Under-selected by my independent check: 2,167 paths in 4,297 (path, suite) pairs, across 3
  suites.**
  - Resilience.Tests: 2,139 pairs, tests/Antiphon.Tests/**/*.cs.
  - Server.Git.Tests: 2,152 pairs, tests/Antiphon.Tests/**/*.cs.
  - Antiphon.Tests: 6 pairs, src/Antiphon.Messaging.Service/Migrations/*.

## Defects

### D-1a (blocker): tests/Antiphon.Tests/**/*.cs is not closed

- Where: `scripts/lib/rc-input-owners.json`, row
  `tests/Antiphon.Tests/**/*.cs -> [GitInProcess.Tests]` (reason `gitinprocess-source-scan`). Its
  sibling suites are missing.
- Failure:
  - `tests/Antiphon.Resilience.Tests/ResilienceAssemblyTests.cs:44,83` and
    `tests/Antiphon.Server.Git.Tests/ServerGitAssemblyTests.cs:59` call
    `SourceFiles(Path.Combine(root,"tests","Antiphon.Tests"))`. That is a recursive walk of every
    Antiphon.Tests `.cs` file. They assert class and method counts:
    - `CountClass(antiphon,"StaleWorkingTranscriptWatchdogTests").ShouldBe(1)`;
    - `CountClass(...,"AssemblyHooks").ShouldBe(0)`;
    - `CountDeclarations(antiphon,"Missing_checkout_returns_path_error").ShouldBe(1)`.
  - In the real inventory, Resilience.Tests owns 22 of the 2,161 tracked tests/Antiphon.Tests .cs
    files, and Server.Git.Tests owns 9. Both target files are owned only by Antiphon.Tests and
    GitInProcess.Tests:
    - `Application/StaleWorkingTranscriptWatchdogTests.cs`;
    - `Application/GitIndexLockPathTests.cs`.
  - Concrete cuts that go red unselected:
    - Renaming `StaleWorkingTranscriptWatchdogTests` turns
      `ResilienceAssemblyTests.Mirror_holds_the_resilience_tests` red.
    - Adding a class `AssemblyHooks` under tests/Antiphon.Tests turns
      `ResilienceAssemblyTests.Assembly_hook_is_local_to_the_mirror` red.
    - Renaming `Missing_checkout_returns_path_error` turns
      `ServerGitAssemblyTests.Test_project_does_not_reference_the_server` red.
    - In each case the cut selects Antiphon.Tests and GitInProcess.Tests only.
- Why:
  - The round-2 review named this pattern in GitInProcessAssemblyTests. The fix added only that one
    consumer. The two sibling mirror projects have identical `SourceFiles` walkers.
  - The census never resolves a directory literal, so it reported the class as proven.
- Fix: add Resilience.Tests and Server.Git.Tests to the `tests/Antiphon.Tests/**/*.cs` row, or star
  the row. Then add a directory-literal pass to `rc-input-reader-census.ps1` (see D-1c).

### D-1b (blocker): Antiphon.Tests reads src/Antiphon.Messaging.Service/Migrations at runtime

- Where: no owner row. `src/Antiphon.Messaging.Service/**` is claimed only through
  `project-tree:` by Messaging.Tests, so the `**` fallback never reaches Antiphon.Tests.
- Failure:
  - `tests/Antiphon.Tests/Infrastructure/AmServiceDeployReadinessTests.cs:461` calls
    `Directory.GetFiles(Root/src/Antiphon.Messaging.Service/Migrations,"*.cs")`.
  - `RunLegacyAsync` (:312-326, used by `SucceedsImmediatelyAndPreservesDeployContractAsync`) runs
    `scripts/test-deploy-am-service.ps1` and asserts exit 0.
  - That harness and `deploy-am-service.ps1:151,383` both derive the migration IDs from that
    directory.
  - Proof, with the real selector and a real red:
    - Probe commit 3a91347270a2b80c36213f6a3e05a85cd9edd4b1 (an unreferenced object with b43f1f5c1
      as parent) adds only `src/Antiphon.Messaging.Service/Migrations/MessagingDbContextFactory.cs`.
    - `Read-RcInputInventory -SkipUnresolvedBroadening` at the probe puts that path in exactly one
      suite, Messaging.Tests (`project-tree:src/Antiphon.Messaging.Service/...`). Antiphon.Tests
      does not get it.
    - `pwsh scripts/test-deploy-am-service.ps1` on a git-archive copy of b43f1f5c1 exits 0 (PASS).
    - The same copy plus that one file exits 1 with `Remote am-postgres migration history is
      missing: MessagingDbContextFactory`, because the script's remote filter `^\d{14}_.+$` drops
      the non-migration file.
    - So the Antiphon.Tests assertion goes red and the cut never selects it.
  - The round-2 review listed `AmServiceDeployReadinessTests.cs:461` as an untriaged candidate.
- Related, same reader: `Get-AmServiceDockerfileManifest` also resolves the Dockerfile COPY sources
  under src/ during the harness run. Antiphon.Tests owns none of
  src/Antiphon.Messaging.{Service,Telegram}.
- Fix: star `src/Antiphon.Messaging.Service/**` and the Dockerfile COPY sources
  (`src/Antiphon.Messaging*/`, `src/Messaging.Pack.props`), or give them Antiphon.Tests as an
  explicit consumer. Add the pair to `rc-input-reader-census.json`.

### D-1c (root cause, part of the blocker): the census is a literal-file scan only

- Where: `scripts/rc-input-reader-census.ps1`. `RcReaderCensusScan.Pairs` maps a literal only to:
  - a tracked file;
  - a tracked-file suffix;
  - a basename with at most 3 hits.

  A literal that names a directory never matches, and an enumeration over it is never counted.
- Failure: the plan's statement that "the census proves closed" is unsupported for any class whose
  readers walk directories. D-1a and D-1b are two concrete instances. CP-6 stays green over both.
- Fix: resolve a literal run to a tracked directory when it is passed to an enumeration call. That
  makes the pair (suite, every file under the directory). Keep the triage exclusions, and keep the
  check failing on any new pair.

## Attacks answered

1. Census re-run (CP-6): 11132 / 27 / 787 / 0. Independent check: 4,297 pairs under-selected
   (D-1a, D-1b). BLOCKER.
2. Closure of the two consumer classes:
   - `tests/**/*.cs -> Antiphon.Tests` holds for every non-Antiphon.Tests reader I found. The
     readers that walk other suites' `.cs` files are:
     - WindowsShellGuardAudit, own project plus tests/Shared;
     - the LiveClaude mutants, own project, tests/Shared, and referenced src;
     - Resilience.Tests, which reads one E2E `.cs`, already starred.
   - `tests/Antiphon.Tests/**/*.cs -> GitInProcess.Tests` is NOT closed. See D-1a.
3. Removed rows:
   - `Antiphon.AppHost/Program.cs` is now covered by `Antiphon.AppHost/** -> *`.
   - `tests/Antiphon.E2E/**/*.cs` is now `*`.
   - The remaining mentions are the RuntimeReaderCensus seed (test-rc-input-owners.ps1:153) and
     `LinuxServerRestartGateTests.cs:27`. Both are covered by the starred rows. Nothing depends on
     the old narrow rows.
4. Unknown path still broadens:
   - The `**` fallback is unchanged and `unknown-path:` is unresolved.
   - The `UnknownPathDirtiesAll` case is green in CP-1.
   - The default (non-census) path still applies `unresolved-conservative-all-inputs`.
     `-SkipUnresolvedBroadening` is used only by the census and the RuntimeReaderCensus case.
5. Witness can fail. Mutant commit f4ad8a1aa19c884a53a2ecd5a378d205f611eefe (scratch worktree,
   removed afterwards) deletes only the `**/appsettings.json` row.
   - `test-rc-input-owners.ps1 -Case RuntimeReaderCensus` exits 1 with
     `FAIL RIO RuntimeReaderCensus.contract - census:scan-03:tests/Antiphon.Messaging.Tests/Antiphon.Messaging.Tests.csproj:server/appsettings.json`.
   - `rc-input-reader-census.ps1` exits 1 with `under-selected=1`,
     `under-selected: tests/Antiphon.Messaging.Tests/... server/appsettings.json`.
   - The witness is real for the pairs it lists. Its coverage gap is D-1c.
6. Shadow guarantees:
   - The diff does not touch `activation='shadow-only'`, `executionEvidence=$false` or
     `gateCredit=$false`.
   - `scripts/release-selection.ps1` has 0 diff lines over e86d138eb..b43f1f5c1.

## Evidence

- Checkpoint run 20261120-002527-4037 at b43f1f5c1: GREEN, 6/6 rows.
  - Source clean, buildSource verified, unlisted none, wall 22m19s.
  - `validate --expected-source-sha b43f1f5c1` -> `CHECKPOINT SOURCE VALID rows=6`.
  - CP-1: 26/26 (RcInputOwnersMapTests, RcInputManifestTests, RcChunkInputDigestTests).
  - CP-2: guard batch 81/81 (TestClassificationPolicy/Guard, SlowTestRegistryMerge,
    SlowTestTripwire, NativeLinuxPolicyWitness, MechanicalPcContract, TaskPlatformGuidance,
    C1729LayeringGuard, TestLaneCategoryGuard).
  - CP-3: DerivedImpactIndexTests 27/27.
  - CP-4: land graph `-Check` exit 0.
  - CP-5: pins exit 0; digest equals pin.
  - CP-6: census exit 0, 11132/27/787/0, 222 s.
- check-evidence-diff over 5be178005..b43f1f5c1: commits=8, entries=0, violations=0.
- merge-tree:
  - Against the brief master c83d14d48 and against current origin/master 56d7340c6: CONFLICT in
    `tests/derived-impact-index.json` only. This was expected, and a replay follows.
- Other runs:
  - One unlisted build: the checkpoint tool itself (`dotnet build tools/Antiphon.Checkpoints`,
    build-slot lease aa3eadfe, 37 s), so `start`/`wait`/`validate` could run from the built dll.
  - It started while the 1-minute load was 27, above the brief's 16 threshold. Every test and
    census run started below 16 (10.4-11.6).
- Scratch: the probe and mutant commits are unreferenced objects. The scratch worktree was removed
  with `git worktree remove` on its literal path.
