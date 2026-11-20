# CARD-1820 S3 owner map M1 v2: Review round 4 (task 3fa5f039)

Subject: `feat/card-task-2dda603e` tip `06c6ff4635eaa432db0a0a91e76817ec83d3069b` (Grok author).
Round 4 is Final/Full, best effort, and covers blockers and regressions only. Landing owner: `1f7120d3-1bdc-4ae5-9e50-bae4a216f741`.

**Verdict: CLEAN. No blockers. Advisories A-1..A-8 follow; they do not hold the land.**

## Checkpoint rows (plan `docs/superpowers/plans/2026-11-19-incremental-s3-checkpoints.md`)

Run `20261120-054819-2ff5` at `06c6ff46`. Source state was clean and build provenance was verified. `validate` returned `CHECKPOINT SOURCE VALID rows=6`. The run had one build and no unlisted runs. Wall time was 16m21s.

| CP | Result |
|---|---|
| CP-1 owners/manifest/digest | executed=32 passed=32 failed=0 (floor 32) |
| CP-2 nine guard classes | executed=81 passed=81 failed=0 (floor 81) |
| CP-3 DerivedImpactIndexTests | executed=27 passed=27 failed=0 |
| CP-4 land graph -Check | exit 0 |
| CP-5 live-lane pin | inputDigest = pinned = ab041a1c...2cac |
| CP-6 census | exit 0: paths=11134, suites=27, pairs=10202, under-selected=0, unanalysable=0, noop-conflict=0, unowned=202, unresolved=18, creditBlockedBy=unowned-paths, needsOrchestratorDecision=True, repo-walk=none |

These rows reproduce the author's census numbers. `check-evidence-diff` from `5be17800` to `06c6ff46` covered 11 commits with 0 violations.

## Independent soundness check

This check does not use the author's census. A node scanner looked at each of the 27 suite projects: its directory `.cs` plus its literal `<Compile Include>` links. It extracted every string literal: regular, verbatim, interpolated with holes masked, and raw. It resolved each literal as an exact tracked file, a tracked directory of two or more segments (expanded to every file under it), or a unique basename (4 or fewer matches). It also joined consecutive literals in one statement, such as `Path.Combine` and `Join` chains, taking the longest resolving chain. Every script the scan reached was scanned one more level. Each (suite, path) pair was compared with the inputs of `Read-RcInputInventory` at `06c6ff46`.

- Tracked paths 11134. Suites 27. Pairs checked 19227.
- **Owned paths under-selected: 0 real.** The scanner reported 2225 raw misses. All are false positives:
  - filter or argument strings: the `Card0490NativeExecutionTests` and `NativeInputPolicy` filters, `PhaseJournalKillDiagnosticTests` and `RunSchedulerTests` fake project names, and `MergeHighRadiusSubsetTests` and `ImpactedClassesSelectorTests` data;
  - `TestClassificationMetadata`'s per-assembly allowlist switch;
  - temp-workspace `docs/superpowers` paths in `AgentTaskReplyIntegrationTests`;
  - the excluded `ServerGitAssemblyTests` csproj probe.
- **13 pairs land on unowned paths.** The real readers are listed in A-1. Every one is flagged, selects nothing, and blocks credit, as the redesign intends.

## Unowned and flag paths

- `Read-RcInputInventory` and `Get-RcInputManifest` return `unowned` (path, hint, reason), `needsOrchestratorDecision` and `creditBlockedBy=unowned-paths`.
- `New-RcChunkInputManifest` copies each unowned path into `unknown` as `unowned-path:<p>`.
- `Compare-RcChunkInputManifest` treats any `unknown` as dirty.
- Admission refuses `old.unknown.Count`.
- The admission rehash (`Restore-RcAdmissionManifest` without a snapshot) carries the tokens through `unresolved=@($Manifest.unknown)`, so a restored snapshot cannot drop them.
- No path exists where an unowned path selects a suite. Mutant B below shows the witnesses catch that.
- `dirtySuites` has no production consumer.
- The 18 unresolved flags have correct reasons: 11 dynamic-build Exec/target projects, 3 `.gitattributes` external-content contracts, 2 missing FakeGateway `PublicAPI.*.txt` files, and 2 unresolved MSBuild expressions.

## No-op list

- No test source names `docs/reviews` or `docs/superpowers/reviews`.
- No test walks the real `docs/` tree. Every `docs` walk found was in a temp repository or workspace.
- The census reports `noop-conflict=0`.

## Mutants (scratch git-archive copy; nothing changed in the subject)

- **B: unowned paths broadened to every suite again.** The added line called `Add-RcInventoryInput` on every suite for each unowned path. All three witnesses went RED: `UnknownPathSelectsNothing`, `NewCsprojNobodyReferencesIsFlagged`, `UnownedPathBlocksCarryCredit`.
- **A: deleted the `messaging-migration-walk` row.** The census stayed GREEN with under-selected=0 (see A-3).
- **A2: deleted the `messaging-migration-walk` row and both `derived-impact-source-walk` rows.** The census went RED with under-selected=16 Antiphon.Tests pairs, including `src/Antiphon.Messaging.Service/Migrations/*` and `src/Antiphon.Messaging.Telegram/*`. The census can fail.

## Shadow guarantees and portability

- The chunk manifest still writes `executionEvidence=$false`, `gateCredit=$false` and `activation='shadow-only'`.
- `scripts/release-*.ps1` is untouched by the whole chain.
- The changed scripts parse cleanly and declare `#requires -Version 7.0`. That was the existing design: they are not daemon scripts, so PowerShell 5.1 does not apply.
- No added line in the changed files contains a non-ASCII character. The non-ASCII bytes already in the incremental plan doc predate the chain.

## Merge-tree

- The chain is `0708ccee..06c6ff46` (11 commits from merge-base `5be17800`, including `655bf2b0`, `e86d138e`, `b43f1f5c` and `06c6ff46`). None of it is on master.
- `git merge-tree --write-tree` CONFLICTS in `tests/derived-impact-index.json` against both master `7fcfbeaa` and current master `dab889e65`. Master moved 8 commits, including `dab889e65` "chore: regenerate the derived impact index". The plan doc auto-merges.
- The file is generated. The land needs the Merge step: rebase the chain onto master, regenerate the index with `scripts/regenerate-derived-impact-index.ps1`, commit it, then run DerivedImpactIndexTests and CP-1/CP-6. Nothing in the reviewed logic changes.

## Advisories (cards, never land holds)

- **A-1. Census misses real readers, so they land as unowned instead of owned.**
  - Literal readers in Antiphon.Tests:
    - `.dockerignore` (DockerStackContractTests);
    - `.config/dotnet-tools.json` (DotnetToolManifestContractTests);
    - `.antiphon/fixtures/card-1521/partial-boot-pointers.approved.json` (SessionMessageQueueCodexPointerTests);
    - `restart-server.ps1` (NightlyScriptsTests, via `Path.Combine("..","restart-server.ps1")`).
  - Paths read through scripts:
    - `tests/fixtures/card1387-typecheck`, `card1393-lint` and `card1398-typecheck-ok` (test-client gate scripts);
    - `tests/live-lanes.json`, `tests/live-lane-inputs.json` and `tests/fixtures/live-lanes/*` (live-lane manifest and fingerprint libs);
    - `tests/fixtures/card0490-linux/*.sh` (card0490 scripts).
  - Cause: the census skips literals that start with `.` and never follows script-mediated reads.
  - Effect: credit is blocked, so this is not unsafe. The census's "under-selected=0" is weaker than it reads, though.
  - Fix: add owner rows, and teach the census about dot-leading literals and one script hop.
- **A-2. The unowned list is invisible above 40 paths.** At 202 paths the census prints only `UNOWNED-COUNT` and the path of the gitignored `.antiphon/rc-unowned-paths.txt`. A remote caller or receipt never sees the paths. Print every `UNOWNED-PATH` line, or put the list in the receipt.
- **A-3. The `messaging-migration-walk` row is redundant and has no witness.** `src/**/*.cs` already selects Antiphon.Tests, and Migrations holds only `.cs` files. Drop the row, or witness it with a non-`.cs` migration asset.
- **A-4. The test-only class is MISSING.** The operator asked for test-source-only changes (excluding `tests/Shared/**`, harness and infra, policy JSON and the build graph) to select nothing and be recorded as deferred test-only. The tip instead selects `tests/Antiphon.Tests/**` for three mirror suites and `tests/**/*.cs` for Antiphon.Tests. This is a known gap to file.
- **A-5. The evaluator is not fully config-driven.**
  - `rc-input-manifest.ps1` hard-codes `scripts/lib/rc-input-noop.json` and the `docs/` hint.
  - `rc-input-reader-census.ps1` hard-codes C# `Excluded()` literals: ServerGitAssemblyTests, PhaseJournalDiagnostic, `combined.pdf` and TestClassificationMetadata.
  - Move these into JSON.
- **A-6. A no-op row silently overrides explicit owner rows.** The skip in `Add-RcInventoryInput` runs before any owner is recorded. Only the census `noop-conflict` check guards it, and that check sees proven pairs only. Refuse a no-op pattern that matches an owned path.
- **A-7. A snapshot that is not a dictionary drops `unowned`.** `New-RcChunkInputManifest` reads `unowned` only from an `IDictionary` snapshot, so a PSCustomObject snapshot (for example, JSON without `-AsHashtable`) loses it silently. No caller does this today. Fail closed instead.
- **A-8. The `new-csproj` hint is misleading.** It is attached to `tools/Antiphon.NativeLinuxMigrationAudit`, an existing tool whose `NativeLinuxWindowsReasonAudit.cs` is linked into Antiphon.Tests.

Load-rule note: the 7-second checkpoint-tool build ran at load 20.95, above the rule. Every test row started at a load below 16.
