# Review a0aad3d0: failed-first plan, round-3 delta

Subject: `feat/card-task-4e264f6c` at `0df39b19eeb03283b4bf4364e71cd42f3e1646e7`. Delta `2c727e270..0df39b19e`, limited to `docs/superpowers/plans`. Both plan copies are byte-identical (`cmp`).

**Verdict: one defect, D-1.** The round-2 D-1 and D-2 closures hold. Slice sizing and the importer counts are correct, and the round-2 confirmations did not regress. The new problem: the supported replacement for an rc Partial, which is `-Profile nightly`, loses every rc exclusion.

## D-1: a nightly-profile Partial runs with no exclusions at all

- **Where:** plan D-20 (line 184, "Use the declared nightly profile's exclusions"), the commissioning text at line 355 ("the active nightly profile supplies exclusions"), D-3 ("Exclusions remain effective in both modes"), D-6 ("entirely-excluded classes" are resolution failures), and CP-9 ("no exclusion from the active profile executes").
- **Code:**
  - `tests/test-execution-policy.json` declares `profiles.nightly.exclusions: []`. The rc profile has 136 exclusion rows: 34 method-level and 57 platform-limited.
  - `scripts/lib/nightly-tests-impl.ps1:954` sets `$profileAware = ($Profile -ne 'nightly')`. That turns off exclusion filtering at `:1258`, `:1278` (Resolve-NightlySuiteChunks), `:1504` and `:1598`.
- **Failure:**
  1. Take an rc history whose failed classes have rc method-level exclusions. Examples are `TestDbFixtureLazyInitializationTests` (three child-only methods "launched and asserted by its parent", which fail or skip when run alone) and `PtyPasteMarkerExperiments`, `OrchestratorWorkspaceLayoutCanaryTests` and `ClaudeVerifiedDeliveryTests` (methods that launch real Claude or a live TUI).
  2. Run the only supported sweep: `-Profile nightly -Trigger manual -PartialSweep`.
  3. The exact-class replan runs the methods rc excludes. On Linux the sweep also runs the platform-limited rc rows, which are red there by construction.
  4. The resulting `sweepPassed`/`sweepCoverageComplete` do not predict the rc result. That defeats the user's goal: rerun the previously failed rc classes first or alone, and learn whether they now pass.
- **Why the tests miss it:** D-6's "entirely-excluded class" refusal can never fire, and CP-9's "no exclusion from the active profile executes" cannot go red, because there is nothing to exclude.
- **Fix (plan amendment):**
  1. Have Partial take its exclusion set from the profile recorded in the history (or from an explicit `-ExclusionProfile rc`). Read it from the committed policy only. It must not touch rc selection, attempts or credit.
  2. Force profile-aware filtering for Partial. Record the exclusion profile, the policy digest and the excluded UIDs in `prior-failure-selection.json`.
  3. If the history profile and the exclusion profile differ, refuse.
  4. CP-9 gets a selected class with an rc method-level exclusion. It must assert those UIDs never launch, and a mutation that falls back to nightly's empty set must go RED.
  - Alternative fix: refuse a sweep of any class that has an exclusion in the history profile. That is simpler but less useful.

## Checks requested by the brief

1. **D-1 (round 2) is closed for every entry path.**
   - Outer `scripts/nightly-run.ps1` only dot-sources its libraries before the `$invoke` splat at `:129-137`, so a refusal there writes nothing.
   - The core's first writes come later: the run lock (`nightly-run-impl.ps1:302`), the busy LastAttempt (`:344`), Started (`:377`) and selection (`:431-449`). The mirrored refusal at the start of Core and of `Invoke-AntiphonNightlyTests` (`nightly-tests-impl.ps1:870ff`) sits ahead of all of them.
   - The other entry paths are safe. `release-cut.ps1:173` and Windmill gain no sweep option (D-8). `c487-harness` goes through the same wrapper. There is no resume-from-completed feature: a `ContinueRunId` arrives only as a self-reexec hop, which rc Partial never reaches. The private supervisor is out of repo; commissioning step 3 covers it.
   - The new-path check is a real recursive listing diff (relative path, kind, hidden entries, plus hashes) rooted at the production ReleaseRoot. That matches where the round-2 attempt was created, `ReleaseRoot/candidates/<id>/attempts/<runId>`. The derived anchor moves outside ReleaseRoot when FullStateRoot is nested in it.
   - The refusal text is not specified. See A-1.
2. **D-2 (round 2) is closed.**
   - Persisted identity: `<timing-root>/active-chunk.json` (`antiphon-qualification-active-chunk-v1`), written by `Write-QualificationActiveChunkIdentity` and read by `Read-QualificationActiveChunkIdentity` in `scripts/lib/qualification-supervisor-heartbeat.ps1`. The writer is called from `Invoke-NightlyScheduledChunk` on both the lazy and the global path.
   - Under the code's `Get-QualificationHeartbeatIdleBudget` (floor 10, default 20), an 1800 s chunk gets 30 minutes and a 120 s chunk gets 10, never the 20-minute unknown default. CP-6 asserts both, for legacy and qualified calls, in absent-history and empty-history Full.
   - The anchors check out: the `:194-201` fixture, the `:630` fallback, the `:1601` union and the `:1613` catch.
3. **Slice table is sound.**
   - 110 + 140 + 150 + 120 + 100 + 85 + 55 = 760 minutes; each slice is at most 150; 7 × 150 = 1,050.
   - Only S1 may start before M1/M2; S2 onward waits for reviewed M1/M2 on rc. S5-W is a separate Windows task dispatched with `-Platform Windows` and no host pin.
4. **Importer:** one build-slot-gated tool build, then `import --plan` on each copy. Lease `1048d120…`, 241 s slot wait, 0 errors, 5 existing warnings. Both copies imported **16 rows**, and their YAML differs only in the `plan:` path. Counts verified:
   - 7 builds: s1/s2/s3/s4a/s4b/s5/win.
   - 16 rows with `minExecuted: 1`, all `serial: true`.
   - Rows per build: 2/3/3/2/2/2/2.
   - Per-row estimates 6,2,7,2,1,7,2,2,8,2,7,1,6,2,8,2: **65 minutes total**, groups 8/10/11/10/8/8/10, matching the plan.
   - Isolated `bin-reva0aad3d0` outputs (two projects) removed.
5. **No regression of round-2 confirmations.** The D-15 field table only gained the A-2 timing-output row. The unresolved rules (D-5/D-6, Full reports and Partial refuses) are intact; A-5 only relaxed candidate SHA/policy equality. The CP-4 non-identity precondition got stronger with A-3 (a coordinator that ignores the schedule is now RED).

## Advisories

- **A-1:** D-20 fixes exit 3 / `partial-rc-profile` but not the message. Require the stdout refusal to name the supported command (`-Profile nightly -Trigger manual -PartialSweep -FullStateRoot … -PriorFailuresPath …`) and have CP-13 assert it.
- **A-2:** Both literal commissioning commands (plan lines 351-352) omit `-CheckoutRoot` and `-CoordinationRoot`. The defaults are `C:\Antiphon\nightly\checkout`, the master nightly clone, which a sync would `reset --hard` and `clean -fdx` to the rc ref, plus the default coordination root. The prose says to supply them; put them in the literal commands.
- **A-3:** The recursive no-new-path inventory covers only ReleaseRoot. In the separate-state-root layout, FullStateRoot and the default nightly root are protected only by four file digests, so a stray `logs/<run>` or `timing-*` directory there would pass. Extend the CP-10/13 inventory to those roots.
- **A-4:** S3 grew from 140 to 150 minutes with no headroom. Name the pre-agreed split point, for example the D-16 active-identity work plus the CP-6 heartbeat subcases as S3b.

PCs: none run (plan review). They stay pending for the SourceLanding Mutation stage.
