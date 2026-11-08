# CARD-1747 Review round 1 (task 1c1cf07a) — CLEAN

Subject: Code task 26d7bf3f-75fe-42bd-abd2-58f9f2166fbe (Grok), branch `feat/card-task-26d7bf3f`, tip `1516f551cbca8a9d07e16917a4aa5c816928fb4f`, red `58d4a347ba3935da49e96a00a224527fe581db22`, base `9e7ff34988cd8c273d94db3c002975d93ea26f9e`. Master at review time `6ccf8896e61a121bb2b34f1d19bcdf344dc51dfa`; `git merge-tree --write-tree origin/master HEAD` clean (tree `11caab88886851516083c1162cc667db3bc71c57`). Evidence diff over base..tip: commits=2 entries=0 violations=0 exit 0. No `### Checkpoints` closed list exists for this card (no plan document); the Code report is the manifest.

Verdict: CLEAN. No blocker. Five advisories (A-1..A-5). Every PC stays pending for SourceLanding Mutation.

## Review run (one checkpoint-tool run, run 20261108-025634-3ad2, one cold build bin-c1747-rv1, slot=granted, host load 47.4 on 24 cores)

| Row | Filter | Result | Timing |
|---|---|---|---|
| CP-1 | `/*/*/ProcessSpawnLimitTests/*` | 37/37 passed, 0 skipped, sourceState=clean buildSource=verified | waited 60 s, executed 9.7 s |
| CP-2 | guards: TestClassificationPolicy*, TestClassificationGuard*, SlowTestRegistryMerge*, SlowTestTripwire*, NativeLinuxPolicyWitnessTests*, MechanicalPcContractTests*, TaskPlatformGuidanceTests*, C1729LayeringGuardTests* | 75/75 passed, 0 skipped | waited 75 s, executed 119 s |
| CP-3 | `/*/Antiphon.Tests.Checkpoints/*/*` (width 2) | 862 total, 826 executed, 823 passed, 3 failed (inherited, A-1), 36 skipped (Windows) | waited 60 s, executed 1482 s (24m42s) |

`validate --evidence report.json --expected-source-sha 1516f551…` refuses `report_failed` because CP-3 carries the inherited reds and Windows skips, so `reviewedSourceClean` is false although CP-1 and CP-2 are clean rows on the verified build. The red run kept its output at `tests/Antiphon.Tests/bin-c1747-rv1` (1.2 GB) and `.antiphon/checkpoints/20261108-025634-3ad2/`; both are ignored and were left in place (the brief forbids `rm -rf`).

## Blocker checks from the brief

1. Shared state in the 44 lane classes. The only classes that start a child with `WorkingDirectory = CheckpointFixtures.RepoRoot` are the six strict ones (RerunFilterHostTests, CheckpointSourceExecutionTests, CheckpointRepeatBuildIsolationTests, CheckpointTerminalReceiptTests, CheckpointRepeatHostTests, CheckpointStatePublicationTests) plus their CheckpointRepeatFixture; CheckpointImportTests also names RepoRoot but drives an in-process fake driver and carries no limiter. Every lane class uses the checkout read-only: dot-sourcing or AST-parsing `scripts/lib/*.ps1` and `run-checkpoint.ps1`, copying `tools/Antiphon.Checkpoints/bin-c804-*` into a sandbox, reading docs. Every lane `run-checkpoint.ps1` execution runs inside a GUID-named fixture repository with `-NoSlot` (CheckpointColdBuildTests, CheckpointOutputConfinementTests, CheckpointShapeConflictTests, CheckpointTrackedBaselineTests, OutputCleanupTests; CheckpointInheritedFileTests runs fixture copies). Fixture roots are `c723-<guid7>` under TEMP; the temp-root registry and sweep were already exercised concurrently by unlimited classes. No fixed port (BuildSlotBrokerFixture is an in-process HttpMessageHandler), no host-wide process census, no in-process environment mutation in a lane class (TestSetTimingTests mutates ANTIPHON_TEST_TIMING_ROOT but is unlimited and pre-existing). The shared DOTNET_CLI_HOME `c1199-dotnet-cli-home` and NUGET_PACKAGES in CheckpointFixtureGitEnvironment are initialised once under a lock and are the ordinary concurrent-dotnet case. Nothing in the roster relies on ProcessSpawnLimit being 1 for correctness. CPU reasoning: a slots-off checkpoint build uses FixedSlotClient("off") whose default maxCpuCount is 4 (BuildStep adds `-maxcpucount:4`), so two lane occupants are at most eight MSBuild nodes inside the one test-host lease; that is not a CARD-0589 violation (the gate is per driver invocation and the fixture builds are children of the leased test host), but the in-lease footprint rises from one 4-node fixture build to two, above the grant's maxcpucount 6 by two nodes when they overlap.
2. Witness. `Checkpoint_tool_classes_carry_their_own_limiter` is real: 89 misses at the red SHA (red-method.trx), 2 misses for the TimeoutTests mutant (mutant-red.trx), 0 after restore, and the full roster class is 37/37 here. The strict roster contains the six shared classes, `Process_spawn_guard_still_covers_classes_outside_the_checkpoint_tool_lane` pins them to the strict lane, `No_class_carries_both_limiters` now spans three lanes.
3. Failures before/after (Code runs at 58d4a347b Limit 1 and three at 1516f551c width 2, plus this run):

| run | failed | names | stamp median / p95 (s) |
|---|---|---|---|
| before, Limit 1, 45m02s | 4 | receipt blank ×3, C1725_LargeAsciiCallers(R4) watchdog 8 s | 0.85 / 1.51 |
| after1 17m20s | 3 | receipt blank ×3 | 0.85 / 1.70 |
| after2 21m33s | 3 | receipt blank ×3 | 1.65 / 2.62 |
| after3 19m58s | 4 | receipt blank ×3, tracked_stamp_shapes(tool, regular) 5.2 s | 0.71 / 13.71 |
| review 24m42s (load 47) | 3 | receipt blank ×3 | 1.69 / 2.57 |

   The receipt red is inherited (A-1). C1725(R4) failed only once, on the serial run: load flake, not this diff. The stamp 4 s bound: one failure in 56 width-2 cases against none in 14 Limit-1 cases, with the per-case median doubling in two of four width-2 runs; see A-2. Other wall-clock-bounded lane tests (FixtureProcess, SafeDelete, Routing, BoundedReader, InheritedFile, SourceGitBounds, VerifierBounds) show no shift.
4. Docs. `docs/testing-and-build.md` describes the three-lane model and the new lane in two places. test-performance-standards: R1 for the 45-minute serial run is CARD-1728 itself; no timeout changed (R5); TRX per run retained under the Code worktree's `.antiphon/c1747/` (R4).

## Advisories

- A-1 (inherited, verification): `receipt_row_and_build_identities_refuse_blank_without_writing` cases "", whitespace, null fail identically on all five runs with "file inventory changed" for report.json rows/builds × stop/wait. The test file and `tools/Antiphon.Checkpoints` are byte-identical from base to tip and untouched on master since the base. CARD-1739's fix `5d5973be2c` is already an ancestor of the base and addressed the different receipt_counts failure of the CP-fixture healthy control; it does not cover this. Filed CARD-1772 (Debug). No fresh base-commit rerun was made within the review budget; attribution rests on identical sources plus the Limit-1 run at the red SHA.
- A-2 (pre-existing R5 exception, observe): CheckpointStampSafetyTests wraps a pwsh+dotnet child in a 4 s hard wall bound; isolated reruns reach 3.0 s under server2 load, so the bound is about 1.3× observed max rather than 2× p95. Width 2 adds a second lane sibling and raised the case median in two of four runs. Not a blocker on this evidence (this run 14/14, max 2.57 s), but a card to re-derive the bound from measured p95 with the NativeLinux policy row reseal is recommended if it fails again.
- A-3: the census only classifies Checkpoints types that already carry a limiter; a new process-spawning class with no limiter is not flagged (same floor semantics as the strict roster).
- A-4: AGENTS.md line 66 still names a single `ParallelLimiter<ProcessSpawnLimit>`; stale since CARD-1421 and now also for this lane. Owner doc is updated.
- A-5: the Code's roster/red/mutant/guards runs carry no BUILD SLOT lines in their evidence; the four namespace runs do. The roster and guards were re-run here under the tool's own lease.

--- review evidence ---
subjectTaskId: 26d7bf3f-75fe-42bd-abd2-58f9f2166fbe
reviewedSourceSha: 1516f551cbca8a9d07e16917a4aa5c816928fb4f
reviewedSourceClean: false
ordinaryScopeCompleted: Full
reviewRound: 1
reviewedRange: 9e7ff34988cd8c273d94db3c002975d93ea26f9e..1516f551cbca8a9d07e16917a4aa5c816928fb4f
