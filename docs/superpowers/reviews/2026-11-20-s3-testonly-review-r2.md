# Review r2: CARD-1820 S3 test-only follow-up fix (task c412d01f)

Reviewer task 0d878484 (Claude Opus; author Grok). Round 2: diff only, blockers only.
Subject `origin/feat/card-task-c412d01f` tip `b3e97d8f48f26d29917063916a6bdc83f4fab31b`,
range `3cc3852c4073a54bdef9e427a810fdbc4520e441..b3e97d8f4`. The commits are red `6fd33a1ce`, fix `ec12874c4` and checkpoint table `b3e97d8f4`.
The stack also carries the ef772e0a commits from base `8a89aa0e5`, which round 1 reviewed (task 652fbbe7, `4a1ee68d0`).
Master is `eec54154a`. All runs were on Linux server2. Windows is UNOBSERVED.

**Verdict: CLEAN. Round-1 D-1 is fixed. No blockers. next: land.**

## Evidence

| Check | Result |
|---|---|
| Build `tests/Antiphon.Tests` (build-slot, UseAppHost=false, native output, load 11.7) | exit 0, 0 errors, 5m44s |
| `RcInputOwnersMapTests` + guard batch (TestClassificationPolicy*, TestClassificationGuard*, SlowTestRegistryMerge*, SlowTestTripwire*, NativeLinuxPolicyWitnessTests*) at b3e97d8f4, `--timeout 570s`, load 9.5 | 81 total, 81 passed, 0 failed, 0 skipped (4m51s). TRX: RcInputOwnersMap 28, TestClassificationPolicy 42, SlowTestRegistryMerge 5, NativeLinuxPolicyWitness 3, SlowTestTripwire 2, TestClassificationGuard 1. All four new methods are present. |
| New witnesses at red `6fd33a1ce` (`scripts/test-rc-input-owners.ps1 -Case`, detached scratch worktree) | `HelperProjectBodySelectsReferencers` is red on `helper-selects-both:` (empty dirty). `E2EAddedTestSelectsSuites` is red on `e2e-file-selects-all:`, `e2e-file-selects-e2e` and `e2e-method-selects-all:`. `CanaryEditSelectsPtySuite` is red on `canary-selects-pty:`. The own-project controls are green at red: `helper-own-stays-deferred`, `own-body-still-defers`, `own-body-deferred-row`, `e2e-body-still-defers` and `canary-own-stays-deferred`. |
| `EnabledFalseMatchesBaseSelection` | Green at red, which is expected: it pins A-3 parity, not D-1. Reviewer mutant (scratch only, restored): ignoring `enabled` (`if ($policy.enabled)` changed to `if ($true)`) turns it red on `parity:own-body`, `parity-own-base-selects` and `parity:helper`. The witness can go red. |
| `check-evidence-diff.ps1` 3cc3852c4..b3e97d8f4 / 8a89aa0e5..b3e97d8f4 | commits=3 / 6, entries=0, violations=0, exit 0 |
| `git merge-tree --write-tree origin/master b3e97d8f4` | clean, tree `8e604f92d` |
| Scope / pins | The range touches five files: plan, `rc-input-manifest.ps1`, `rc-input-owners.json`, `test-rc-input-owners.ps1` and `RcInputOwnersMapTests.cs`. Over 8a89aa0e5..tip, `tests/derived-impact-index.json`, `scripts/test-live-lane-run.ps1`, `tests/Shared/NativeLinuxPolicyWitness.cs` and `tests/test-execution-policy.json` are untouched. |

The brief limited reruns, so these did not run: CP-2's other four classes (MechanicalPcContract, TaskPlatformGuidance, C1729LayeringGuard, TestLaneCategoryGuard), CP-3 DerivedImpactIndexTests, the CP-4 census and the CP-5 pin recompute. No checkpoint-tool receipt was produced, so `reviewedSourceClean` is false.

## Questions

**(a) Transitive helpers.** The ProjectReference walk is a per-suite transitive queue. Each referenced project's files are recorded as `project-tree:<that project>`, so any project other than the suite's own is a product edge. I ran a reviewer fixture probe at the tip, with a scratch case only. The chain was Antiphon.Tests → Messaging.Tests (a test project) → Helper1 → Helper2. A body edit of `Helper2/Program.cs` selects Helper1, Messaging.Tests and Antiphon.Tests, so no transitive miss. A body edit of `Messaging.Tests/Own.cs` selects Antiphon.Tests, which references it, and defers Messaging.Tests itself. That over-selection is harmless because the fail direction is selecting more. Confirmed.

Residual (pre-existing, not a regression): a helper consumed without a ProjectReference, for example `dotnet run --project` at test time or a copy Target, is still only in its own tree. Round 1 left it outside this class as well.

**(b) Star-walker `[Test]` count.** These are fixture probes at the tip:

- Renaming a `[Test]` method in place, a net-zero replacement, defers every suite.
- Adding `[Arguments]` rows to an existing `[Test]` defers every suite.
- Moving a `[Test]` from one existing E2E file to another selects every suite, because the destination's count rises.
- Adding a `[Skip]`ped `[Test]` selects every suite.

The roster (`scripts/fixtures/nightly/c1584-e2e-source-roster.txt`) keys rows by `class|method|Arguments`. That means the rename and the `[Arguments]` add do change roster identities without selecting the roster guard. This is a residual of the design the fix chose: a count rule, not an identity rule. The class is shadow-only with `gateCredit` false, so no gate is skipped today. See advisory A-1; it must be closed before any activation.

**(c) Canary.** The rule is derived: `runtime-reader-scan` was added to `productEdgeReasons`, and the evaluator has no path literal (0 hits for `ClaudeDialogWatchdog`/`E2E` in `rc-input-manifest.ps1`). It cannot select nothing for a matched path, because the reader's record carries the reason and any product-edge reason keeps the suite. See A-2 and A-3 for the edges.

**(d)** Confirmed above: the three red witnesses are red at 6fd33a1c for the stated reasons, and the own-project controls are green.

**(e)** Confirmed: five files, and the pins are unchanged.

## Advisories (do not hold the land; card them)

- **A-1 E2E roster identity vs `[Test]` count.** A net-zero rename, or added `[Arguments]` cases, under `tests/Antiphon.E2E/**/*.cs` changes the c1584 roster identities but defers every suite. Before activation, compare the multiset of `class|method|Arguments` identities, the roster's own key, instead of the `[Test]` count. Alternatively, select the roster guard on any E2E edit that changes a `[Test]`/`[Arguments]` line. The `[Test]` regex is line-based: a `[Test]` split across lines, or one inside a string or after code on a `*`-led line, is miscounted. That is minor.
- **A-2 stale literal root rows.** The canary rule and the other single-file `runtime-reader-scan` rows depend on literal root paths in `rc-input-owners.json`. If a file is renamed, the row matches nothing and the reader silently reverts to deferral. Nothing (census or witness) checks that each literal root path exists. Suggest a census line `root-matches-none=0`.
- **A-3 plan wording.** The plan says the canary is the only test-only path with reason `runtime-reader-scan`. Three more `tests/fixtures/**` rows carry it: `card0490-linux/card0490-live.example.json`, `card1336-release-gate-followups.ps1` and `incremental-selection-soundness.json`. Their reader is Antiphon.Tests, so edits to them now select Antiphon.Tests. That is the correct, over-selecting direction, but the wording is inaccurate.
- **A-4 reviewer process.** No checkpoint-tool receipt was produced for this round, so `reviewedSourceClean` is false. The brief limited this round to the touched class and the guard batch. All builds and tests started at a 1-minute load below 16.

--- review evidence ---
subjectTaskId: c412d01f-4e95-41e3-b629-1b299f5995d1
reviewedSourceSha: b3e97d8f48f26d29917063916a6bdc83f4fab31b
reviewedSourceClean: false
ordinaryScopeCompleted: Full
reviewRound: 2
reviewedRange: 3cc3852c4073a54bdef9e427a810fdbc4520e441..b3e97d8f48f26d29917063916a6bdc83f4fab31b
