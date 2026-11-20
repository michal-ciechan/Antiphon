# Review r1: CARD-1820 / CARD-1836 S3 test-only class (task ef772e0a)

Reviewer task 652fbbe7 (Claude Opus; author Grok). Round 1, ordinary scope as commissioned.
Subject `origin/feat/card-task-ef772e0a` tip `3cc3852c4073a54bdef9e427a810fdbc4520e441`,
range `8a89aa0e5715f86062bbcc7ee778810c2cf4cb3e..3cc3852c4` (red b09b317e5, selector 97a54332f,
loader 3cc3852c4). Master `452dd5cfe`. Linux server2 only; Windows UNOBSERVED.

**Verdict: BLOCKER (1). next: code.**

## Evidence

| Check | Result |
|---|---|
| Build `tests/Antiphon.Tests` (build-slot, UseAppHost=false, native output) | exit 0, 0 errors, 5m13s |
| `RcInputOwnersMapTests` + guard batch (TestClassificationPolicy*, TestClassificationGuard*, SlowTestRegistryMerge*, SlowTestTripwire*, NativeLinuxPolicyWitnessTests*) at 3cc3852c4 | 77 total, 77 passed, 0 failed, 0 skipped (4m00s) |
| Six new witnesses at red commit b09b317e5 (`scripts/test-rc-input-owners.ps1 -Case`, no build) | 6/6 exit 1 on the stated assertion: `fixture-deferred`, `changed-not-run`, `link-body-skips-antiphon`, `noop-conflict-count:0`, `unowned-first-40:0`, `existing-csproj-hint:new-csproj` |
| `check-evidence-diff.ps1` 8a89aa0e5..3cc3852c4 | commits=3 entries=0 violations=0, exit 0 |
| `git merge-tree --write-tree origin/master 3cc3852c4` | clean, tree 0cd93b9b4 |
| enabled=false parity (6-case fixture probe, 3cc3852c4 lib with `"enabled": false` vs 8a89aa0e5 lib) | dirtySuites identical on all six cases |
| Pins / derived index | `tests/derived-impact-index.json`, `scripts/test-live-lane-run.ps1` untouched in range |

Not re-run (brief: build + six witnesses only): CP-3 DerivedImpactIndexTests, CP-4 census, CP-5 pins, land-graph `-Check`.
Real-repo `Get-RcInputManifest` probe exceeded 500 s and was stopped; the probes below use a small fixture repo
built from the subject's own `rc-input-owners.json` (same shape as `test-rc-input-owners.ps1`).

## D-1 (BLOCKER) ProjectReferenced test helper projects are deferred: false skip

- **Where:** `scripts/lib/rc-input-manifest.ps1` `Get-RcInputManifest` test-only loop (`Test-RcProductEdgeReason` on the input record's reason); the project walk in `Read-RcInputInventory` records files of a referenced project as `project-tree:<referenced csproj>`, and only the `.csproj` path itself carries `ProjectReference`.
- **Failure:** fixture: `tests/Antiphon.Tests` and `tests/Antiphon.SessionRunner.Tests` ProjectReference `tests/Antiphon.CustodyTestChild`; a body edit of `tests/Antiphon.CustodyTestChild/Program.cs` gives `dirty=[]`, `testOnlyDeferred` = Antiphon.Tests, SessionRunner.Tests, CustodyTestChild. Base (and enabled=false) selects all three. Real repo exposure: CustodyTestChild (Antiphon.Tests, SessionRunner.Tests, Agents.Pty.Tests), Card0490.NativeHarness (Agents.Pty.Tests, PtyHost.Tests), MarkdownPdf.FakeBrowser, DockerStack.Fixture, ScriptHarnessHost, Checkpoints.{Lifecycle,Filter,Repeat}Host/GitFixture, *.Probe, FileUseTestChild.
- **Why:** the plan names `ProjectReference` as a product edge that "still selects the suite", and the brief's question (a) names "anything under tests/** that another suite imports". A child-process or fixture project changes the behaviour of another suite's tests; it is not test-only for the referencing suite.
- **Fix:** treat a `project-tree:<P>` reason whose `<P>` is not the suite's own project as a product edge (the ProjectReference closure), or exclude non-suite helper projects via `exceptPaths`; add a witness (helper body edit selects the referencing suite) that is red on 3cc3852c4.

## Advisories (do not hold the land; card them)

- **A-1 (operator decision) cross-suite source readers on test-only paths defer.** A new `tests/Antiphon.E2E/*.cs` file with `[Test]`, or an added `[Test]` method, selects nothing (base: every suite via `runtime-reader-unproven`); the new file is only `changedNotRun` on E2E. That is the CARD-1820 motivating incident class (E2E tests added without the roster). Likewise a body edit of `tests/Antiphon.Tests/Agents/ClaudeDialogWatchdogCanaryTests.cs` defers its `runtime-reader-scan` reader Agents.Pty.Tests. The plan states reader rows on test-only paths defer and only `antiphon-tests-source-scan` is a walker, so this follows the design; suggest `"walker": true` on the `tests/Antiphon.E2E/**/*.cs` root (or adding reader reasons to `exceptReasons`).
- **A-2 classifier.** Structural change = file A/D/T or a change in the multiset of `class <Name>` regex hits per line (lines starting `//`, `/*`, `*` skipped). Text-based, not syntax: a `class X` inside a string or trailing comment over-selects (safe). `[Test]`->`[Skip]`, a static field/constant initialiser, an attribute, a ctor, a rename of a method all leave the multiset unchanged and are deferred for the owning suite and the walkers (fixture probe P4: `static readonly int Limit=5->9` plus `[Test]->[Skip]` selects nothing). Delete selects the walkers (P5). `record X`/`struct`/`interface` declarations are not counted. Per the operator model these defer by design; recorded in `testOnlyDeferred`.
- **A-3 enabled=false.** Selection matches the base evaluator on the six probes, but the manifest is not byte-identical: the new keys are always emitted, and the docs `except` carve-out plus the noop-vs-owner rule do not depend on the switch. No committed witness pins enabled=false parity.
- **A-4 noop-vs-owner.** Fail direction confirmed: `Add-RcInventoryInput -FromOwnerRoot` records the conflict and still adds the input (owner stays selected); census exits 1 on `noop-vs-owner>0`.
- **A-5 ToArray.** Other `List[object]` in the file: `$unowned` and `$changes` already return `ToArray()`; `edges` is pre-existing and unchanged. No other new call site of the same class.
- **A-6 reviewer process.** The guard run started at 1-minute load 28.0 (above the 16 threshold) without waiting; it passed.
