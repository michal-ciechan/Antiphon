# CARD-1820 S3 owner map M1: Review round 1 (Final), task 0b1b4c83

Subject: origin/feat/card-task-1f7120d3 at 655bf2b050a1b888e7d344adecfd56082f04e0fc
(base 5be178005f06aa868bbb038e561c2ef22a654cb5, 3 commits). Author: Grok. Reviewer: Claude Opus.
Code landing owner: 1f7120d3.

Verdict: BLOCKERS (1). The M1 map under-selects: suites other than Antiphon.Tests and
Antiphon.E2E read or execute repo files at runtime, and those files no longer select them.

## Mechanical census

Method:
- Ran the shipped `Read-RcInputInventory` on the real tree at 655bf2b05 over all 11,130
  `git ls-files` paths and 27 suites (`tests/**/*.csproj`). The run was done twice.
- Run A used the shipped function unchanged.
- Run B used one change, in a scratch copy only: it removed the final
  `unresolved -> all inputs` broadening line, so the census shows what the map itself owns.
- Built a truth table from the C# string literals in each suite's compiled sources (project
  tree plus linked Compile). A literal counts as a runtime reference when it is an exact
  tracked path, a tracked path suffix, or a distinctive basename with at most 3 repo hits and
  a non-code extension.
- A pair (path, suite) is under-selected when the suite references the path but the map does
  not give the path to that suite.

Results:
- Run A (effective selector today): 18 unresolved items, so every real cut makes all 27
  suites dirty. Effective under-selection today: 0 of 11,130 paths.
  - `.gitattributes` x3 external-content-contract.
  - dynamic-build in Directory.Build.props/targets, AppHost, and 7 test csproj files.
  - 2 missing-input and 2 unresolved-expression.
  - The selector is sound only because it is not selective. Removing that mask is the plan's
    next step.
- Run B (the map): 0 unowned paths. The heuristic flagged 88 paths in 216 (path, suite)
  pairs as under-selected. When the evidence is limited to the suite's own test sources,
  56 paths in 60 pairs remain. The confirmed runtime readers, each read in code, are listed
  below. Every one selects fewer suites than its true owners.

| Path (class) | M1 owners | Missed suite | Evidence |
|---|---|---|---|
| scripts/build-slot.ps1 | Tests, E2E | SessionRunner.Tests, MarkdownPdf.Tests | BuildSlotEndToEndTests.cs:176 executes it; FakeBrowserSolutionReleaseTests.cs:113 |
| scripts/run-daemon.ps1 | Tests, E2E | SessionRunner.Tests | DaemonLogRotationTests.cs:185, PtyHostDeploymentGuardTests.cs:48, DaemonStartupDiagnosticsTests.cs:67 |
| scripts/restart-session-runner.ps1, scripts/session-runner-restart-health.ps1 | Tests, E2E | SessionRunner.Tests | RestartFixture.cs:46-49 copies and runs them; RunnerRestartScriptCompatibilityTests.cs:17-23 |
| scripts/fixtures/c589-slot-shim.ps1 | Tests, E2E | SessionRunner.Tests | DaemonStartupDiagnosticsTests.cs |
| scripts/test-live-startup-timing.ps1 | Tests, E2E | Observability.Tests | StartupScriptTests.cs:15-18 executes it |
| scripts/test-live-lane-manifest.ps1 | Tests, E2E | Agents.Pty.Tests | LiveClaudeInventoryTests.cs:33 |
| docker/session-runner-grok/antiphon-custody-{common,enter,kill}.sh, dind-entrypoint.sh | Tests, E2E | Agents.Pty.Tests, SessionRunner.Tests | LinuxCgroupContainmentTests.cs:235-274 copies and runs them; CustodyNamespaceBootstrapTests.cs; LinuxRunnerCustodyLifecycleTests.cs |
| docker-compose.server2-runner{,.temp}.yml, docker-compose.runner-grok.yml, Dockerfile | Tests, E2E | SessionRunner.Tests | CustodyNamespaceBootstrapTests.cs:20-21 |
| tests/Antiphon.Tests/Agents/Fixtures/compact-boundary.jsonl | Tests, SessionRunner (Content) | Agents.Pty.Tests | ClaudeCompactionCanaryTests.cs:377 reads it from the repo root |
| tests/Antiphon.Tests/Fixtures/Card0418/manifest.json | Tests | PtyHost.Tests, MarkdownPdf.Tests | Card1619ExitWireTests.cs, MarkdownPdfRealBrowserTests.cs (heuristic hit) |
| tests/Antiphon.E2E/*.cs (LandDelivery*, DispatchBaseWarning*) | E2E | Antiphon.Tests | LandDeliveryWaitRoutingTests.cs:17-18 reads the E2E sources |
| tests/Antiphon.SessionRunner.Tests/slow-tests-allowlist.txt | SessionRunner | Antiphon.Tests | SlowTestRegistryMergeTests |
| client/src/** (App.tsx, api/attention.ts, features/attention/*) | E2E | Antiphon.Tests | StandingBootDocumentationTests, TestClientFilterTests, CardReviewServiceIntegrationTests |
| Antiphon.AppHost/Program.cs | Observability.Tests | Antiphon.Tests | HangfireDurableStorageTests.cs:76 `DockerStackDocuments.Read("Antiphon.AppHost/Program.cs")` |

Mechanism:
- `**` now applies only to paths that nobody claims.
- The reader roots (`scripts/**`, `docker/**`, `docs/**`, `**/*.md`, `.claude/**`,
  `docker-compose*`, `Dockerfile`, `client/**`) claim a path for two suites, or one.
- A `project-tree:` claim gives a path to its own project.
- Either kind of claim removes the all-suite fallback, and nothing restores the runtime
  readers in other suites.
- The plan states that "`scripts/**` is assigned to the two repository-reader projects".
  That premise is false: SessionRunner.Tests, Observability.Tests, Agents.Pty.Tests and
  MarkdownPdf.Tests shell out to or read `scripts/**`.

## Defects

D-1 (blocker): the M1 owner map under-selects runtime readers.
- Where: `scripts/lib/rc-input-owners.json` (reader roots) and `scripts/lib/rc-input-manifest.ps1:193-207`
  (the fallback is suppressed once any suite claims the path).
- Failure: suppose a cut changes only `scripts/run-daemon.ps1` or
  `docker/session-runner-grok/antiphon-custody-enter.sh`, and the remaining unresolved items
  are cleared. SessionRunner.Tests and Agents.Pty.Tests are then not selected, and their
  tests that execute those files are skipped by the rc gate.
- Why: the map treats Antiphon.Tests and E2E as the only repository readers, and project-tree
  claims block the fallback for cross-project fixture reads. The table above lists 14 confirmed
  classes.
- Fix (one option):
  - Make every reader root `*` until a derived runtime-reference index (CARD-1820 item 1)
    supplies positive edges per suite. At minimum, add SessionRunner.Tests, Agents.Pty.Tests,
    Observability.Tests and MarkdownPdf.Tests to `scripts/**` and `docker/**`.
  - Add a census witness: for each suite, every repo path literal in its sources must be owned
    by that suite. The truth-table method above is a usable seed.
  - Add a negative witness: a change under `scripts/` must select a non-reader suite that
    executes it.

Advisories (do not hold the land; file as cards):
- A-1, witness gap. No witness makes a runtime read select its suite. All 6 witnesses go red
  at 0708ccee for over-selection reasons, because the blanket `**` dirtied everything.
  `LinkedSourceSelectsIncludingProject` does catch a broken link walk; see the mutant below.
  `test-rc-input-manifest.ps1` replaced the committed-map census with structural checks, so
  the map's real ownership is not exercised.
- A-2, performance. One `Read-RcInputInventory` on the real tree takes 156-208 s
  (load ~20, 24 cores). `Get-RcInputManifest` reads two snapshots, so each rc cut costs about
  6-7 minutes. The path x pattern loop is O(roots x paths) per suite.
- A-3, nested-editorconfig edge. A new nested `.editorconfig` inside a project directory is
  claimed by that project only. It still applies to linked sources compiled by other projects.
  No such file exists today.
- A-4, fallback dependence. `.github/workflows/*`, `antiphon.areas.json`,
  `tests/derived-impact-index.json`, `tests/impacted-classes.json` and `.editorconfig` select
  all suites only through `unmatched-path-fallback`, not through an explicit forced root.
  This is sound now. It becomes under-selection if a reader root ever claims them.
- A-5, case sensitivity. The `**/NuGet.config` pattern is case-sensitive. A lowercase
  `nuget.config` under `scripts/` or `docs/` would fall to a reader root.

Force-everything list, as asked:
- Explicitly forced: Directory.Build.*, Directory.Packages.props, global.json, NuGet.config,
  *.sln, *.slnx, packages.lock.json, tests/test-execution-policy.json,
  tests/test-selection-policy.json, tests/rc-project-categories.json, tests/Shared/**,
  scripts/lib/** (nightly), scripts/qualification/**, scripts nightly-*/release-*/rc-* files,
  and the spawn-limit files.
- All suites through the fallback: antiphon.areas.json, .editorconfig, .github/**, and
  derived-impact-index.json.
- docker/** goes to readers only (D-1).

Shadow-only:
- Confirmed: `Get-RcInputManifest` returns `activation='shadow-only'`, `executionEvidence=$false`,
  `gateCredit=$false`.
- `scripts/release-selection.ps1` is unchanged (0 diff lines).

## Evidence

- Checkpoint run 20261119-195748-bc44 at 655bf2b05: GREEN, 5/5 rows. Source clean,
  buildSource verified, unlisted none. `validate --expected-source-sha 655bf2b05` returned
  `CHECKPOINT SOURCE VALID rows=5`.
  - CP-1: 20/20.
  - CP-2: 81/81 (guard batch).
  - CP-3: 27/27.
  - CP-4 land graph -Check: exit 0.
  - CP-5 pins: exit 0. The digest equals the pin.
- The first start attempt (20261119-195731-fff1) was launched inside a build-slot lease. Its
  executor died at startup: the slot's private TEMP was removed when the lease was released.
  The run was restarted from the built tool dll and its rows took their own slots.
- Red at 0708ccee: all 6 RcInputOwnersMapTests harness cases exit 1. All 6 are green at the tip
  (CP-1).
- Mutant: removing `Compile` from the walk XPath turns `LinkedSourceSelectsIncludingProject` red
  (`link-selects-including`, `link-compile-edge`). The other two cases tried stay green. The
  mutant was reverted in a scratch worktree, which was then removed.
- check-evidence-diff over 5be178005..655bf2b05: commits=3, entries=0, violations=0. The first
  attempt failed on the shallow mirror, so the repo was unshallowed first.
- merge-tree: clean against 3f24857e8 (tree 3c12aa5d) and against the current origin/master
  5f0804e42 (tree 00d3650c).
- ASCII-only: all 7 changed files.
- Mutation PCs: waived (operator 2026-11-12), not discharged.
