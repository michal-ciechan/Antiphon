# rc/2026-11-28 cut and Windows Full launch prep (2026-11-18, task fc9a0547)

Outcome: `rc/2026-11-28` is cut at master `997f9b8d9ffc4ba870d2fd56caf7c03781591624` and pushed
as a new ref. There was no force push and no tag. The candidate `rc-2026-11-28-997f9b8d9ffc` is
journalled. Its owned checkout is at the cut, the failed-first seed from run f2de5b30 is built and
validated, and the launch set is staged in `C:\Antiphon\releases\task-fc9a0547`.

The run was **not** launched, and nothing was restarted or deployed. The scripts have no
`-WhatIf`/`-DryRun` for the start itself. The read-only `preflight.ps1` stands in for it.

## 1. Branch name and master check

- `git ls-remote --heads origin 'rc/*'`: the newest line was `rc/2026-11-27` (e229fb4d3), and
  `rc/2026-11-28` did not exist. The line naming takes the next unused date after the newest
  line, not today's date, because the lines are already dated ahead. The cut is therefore
  `rc/2026-11-28`.
- `origin/master` was re-fetched immediately before the push. It was still
  `997f9b8d9ffc4ba870d2fd56caf7c03781591624`, so it had not moved.
- Required contents are all ancestors of the cut:
  - Postgres cutover and move scripts: batches 268-271. Each has a `regenerate derived impact
    index on the batch 26x/27x tree` commit. Batch 267 has no index commit of its own;
    e229fb4d3 itself is the `batch 266 tree` regeneration. The changes include G-1
    `ANTIPHON_SKIP_DEV_POSTGRES`, the G-2 patch pin, the G-4/backup-retarget snapshot target,
    the phase-6 rollback, and CARD-1844 part B cutover.
  - CARD-1994 land-verify generation reuse: 7835f524a, bf689c1a3, 5197ac56b, 997f9b8d9.
  - 115189b61: Resilience.Tests links TestDbFailureEvidence.cs.
  - 5b8e7ab8f: land-graph refresh.
  - c1906/c346 fixtures: 84ff126e0 and the 23843d720 why-missed note.
  - CARD-1989: 8 commits, round 2 clean (832fc75cb).
  - CARD-1986: 10 commits, round 1 clean (2557319e4).
  - **`ae97c85e` does not exist** in this repository (`git cat-file`: not a valid object, and not
    in `--all`). The CPU-layout work on master is CARD-1406: 1cffbb8ad, 317aa9e60 (one-core
    Postgres pin, unpinned runner), b327770d9 (final one-core layout doc) and 462ef487e (review
    clean). Taken as the intended item.
  - The five f2de5b30 fixes:
    - chmod: 7e5fe6db4 keeps the two CARD-1957 POSIX-shim witnesses off Windows, with the
      af6c14977 note.
    - late slot: c8782ac5b.
    - profile-413 failure-evidence interceptor: 6d692aa3d, 2c1b05349, plus the 115189b61 link
      fix.
    - group-24 harness race: 77d110e15.
    - C1722 git budget rows: a6393e929.

## 2. Cut

`git push origin 997f9b8d9ffc4ba870d2fd56caf7c03781591624:refs/heads/rc/2026-11-28` reported a
`[new branch]`. A remote re-read confirms `refs/heads/rc/2026-11-28` =
**`997f9b8d9ffc4ba870d2fd56caf7c03781591624`**.

## 3. What is not in this rc, and contents since rc/2026-11-27

- **Not in rc vs master: empty.** The cut equals origin/master.
- `git log e229fb4d3..997f9b8d9`: **139 commits, 0 merges**. The groups overlap slightly, so the
  counts below are approximate.
  - **Postgres move to server2** (~35): the G-1/G-2/G-4 gaps, the backup retarget (with the G-4
    first attempt reverted and redone as `backup-retarget S1`), the phase-6 rollback, CARD-1844
    part B cutover (b1523e46c, uuid keys/fingerprint timeout/session pins), and CARD-1831 plan
    and review rounds.
  - **Land verifier, CARD-1994** (4): generation reuse after a normal build exit, set-aside only
    on kill, prune to two, per-target lanes.
  - **Retire-temp / volume retirement** (CARD-1986, CARD-1989, ~25):
    - Retirement bound to publication dependencies.
    - Batched dependency discovery.
    - A 3000 ms control-window floor.
    - Persisted step timings.
    - Bounded method rows.
    - Five investigation notes on the gate-9 refusals.
  - **Server2 CPU layout, CARD-1406** (8): Postgres on one shared core, runners unpinned.
  - **rc f2de5b30 failure repairs and test fixes** (~12):
    - The five fixes listed above.
    - c1906/c346 base-equivalence render.
    - Resilience.Tests compile, plus a guard test against helper links (a273242d1).
  - **Spawn lanes, CARD-1984** (10): script-model spawn lane, HostJq and Rolling moved onto it,
    ETXTBSY wide-lane fix.
  - **Retention/sweeps, CARD-1896** (~10): seven-day session ANSI retention, guarded review
    clones, temp residue paths.
  - **Token/quarantine, CARD-1976** (5), and **RcBridge Windows placement skip roster,
    CARD-1978** (5).
  - **Impact-index and land-graph regenerations** (6), and **why-missed notes** (5).
  - **Docs/policy**: "Merge verification is short (2026-11-18)" (f4d783033), and the CARD-1987
    repeat-read investigation and plan review.

## 4. Failed-first seed from run f2de5b30

- Tools: `scripts/export-nightly-stopped-run-seed.ps1`, then `scripts/merge-nightly-seeds.ps1`.
  This is the same procedure as the a21d8174 and 06863906 seeds.
- Export (140 s), run with:
  - `-RunDirectory ...\rc-2026-11-26-29ba0c5002e6\logs\2026-11-17-1832-f2de5b30`
  - `-SourceRunId f2de5b30c626461099fbfbb73fd8ada8 -SourceCandidateId rc-2026-11-26-29ba0c5002e6`
  - `-SourceSha 29ba0c5002e6... -TimingRoot ...\timing-f2de5b30...`

  Result: 1,250 chunks, all completed, 7 failed tests. The raw export is scratch only, sha256
  `16d11212...abe0`.
- Merge inputs:
  - `-Seed 06863906.json` (the previous cumulative ledger) and `-Seed <export>`.
  - Four `-Proven <suite|class|method>:f2de5b30...` pairs, one for each identity still open in
    06863906. All four ran green in f2de5b30: their chunks completed and none is in its failed
    list. The merge re-checked each one green itself, so no `proven-not-green` refusal. The four:
    - C1349 `DispatcherSiblingInspectionSchedulingTests`
    - C545 `NightlyWatchdogCoreTests`
    - C1192 `RemoteScriptContractTests`
    - `C1514HostLoadNativeTests.Windows_ProductionFactoryPreservesHostSamples`
- Output:
  - **`C:\Users\lndco\antiphon-seeds\f2de5b30.json`**, sha256 / digest
    **`0ceda907dba0aa653341e098f2874ac86f746cc49c18e72220ba1b388bdd24ca`**
  - Report `C:\Users\lndco\antiphon-seeds\f2de5b30.report.json`: 11 identities, 7 open and 4
    proven, `unplacedCount` 0.
- Open failed tests, which are the failed-first set. Chunk names are this run's names; dispatch
  keys on class and method:

  | f2de5b30 chunk | Test |
  |---|---|
  | antiphon all-group-135 | `Antiphon.Tests.Scripts.VolumeRetirementHostProofTests.Desktop_children_start_when_the_working_directory_is_264_characters` |
  | antiphon all-group-135 | `Antiphon.Tests.Scripts.VolumeRetirementHostProofTests.Coordinator_receipt_names_the_failing_script_and_reason` |
  | antiphon all-profile-2156 | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1514_Late_second_slot_still_observes_volume_go` |
  | antiphon all-profile-413 | `Antiphon.Tests.Application.C0824DesktopLegacyReleasePolicyTests.Non_terminal_task_is_never_released` |
  | antiphon all-group-24 | `Antiphon.Tests.Application.AgentTaskWorktreeLockOutcomeTests.C443_D9_RecipientGetsSummarizedSiblingsAndDiagnostics` |
  | antiphon all-group-24 | `Antiphon.Tests.Application.AgentTaskWorktreeLockOutcomeTests.HarnessBuiltLandKeepsLongSiblingInsideProductionInspectionDeadline` |
  | session-runner all-group-1 | `Antiphon.SessionRunner.Tests.C1722GitTimeoutBudgetTests.Budget_table_covers_every_git_method` |

- Validation: `task-fc9a0547\verify-seed.ps1` runs the cut checkout's own reader, and its output
  is in `verify-seed-output.txt`.
  - Positive check: `Read-NightlyStoppedRunSeed -ExpectedDigest` and
    `Initialize-NightlyDispatchHistory -AcceptCrossCandidateSeed -SeedSourceCandidateId
    rc-2026-11-26-29ba0c5002e6` give fallback `""` and history loaded.
  - Negative controls:
    - wrong digest: `history-digest-mismatch`
    - no acknowledgement: `seed-acknowledgement-required`
    - wrong source: `seed-source-candidate-mismatch`
- The seed is stale 14 days after `sourceObservedAtUtc` 2026-11-18T10:11Z, so it expires around
  2026-12-02.

## 5. Launch set (staged, not started)

| Item | Value |
|---|---|
| Candidate id | `rc-2026-11-28-997f9b8d9ffc` (journal `C:\Antiphon\releases\candidates\rc-2026-11-28-997f9b8d9ffc\candidate.json`, sha = cut) |
| Owned checkout | `C:\Antiphon\releases\checkout-fc9a0547`: cloned by `scripts/release-candidate.ps1 -Ref rc/2026-11-28`, HEAD = cut, clean, `core.longpaths=true`, both markers. The `.antiphon-nightly-owned` marker was written by hand, the same way as the 4025018e checkout, because `-NoSync` refuses an unmarked clone. |
| Run id | `a627825956de493e891768878a814ba4` |
| Task root | `C:\Antiphon\releases\task-fc9a0547`, holding `launch-record.json`, `launch-rc.ps1` (byte-identical to the cut), `preflight.ps1`, `verify-seed.ps1` and `start-command.txt` |
| State root | `C:\Antiphon\releases\state-fc9a0547` (created by the run) |
| Log root | `C:\Antiphon\releases\candidates\rc-2026-11-28-997f9b8d9ffc\logs`; the run directory will be `logs\<yyyy-MM-dd-HHmm>-a6278259` |
| Coordination | `C:\Antiphon\releases\candidates\rc-2026-11-28-997f9b8d9ffc\coordination` |
| Heartbeat | `C:\Antiphon\releases\task-fc9a0547\supervisor-heartbeat.json` (also `supervisor.log`, `supervisor-identity.json`, `own-last-run.json`) |
| Timing | `...\logs\timing-a627825956de493e891768878a814ba4` |

`nightlyArguments`:
`-NoSync -Profile rc -Trigger rc -SelectionMode Full -RunId a627825956de493e891768878a814ba4 -Ref rc/2026-11-28 -ExpectedSha 997f9b8d9ffc4ba870d2fd56caf7c03781591624 -CandidateId rc-2026-11-28-997f9b8d9ffc -CheckoutRoot C:\Antiphon\releases\checkout-fc9a0547 -ReleaseRoot C:\Antiphon\releases -StateRoot C:\Antiphon\releases\state-fc9a0547 -LogRoot C:\Antiphon\releases\candidates\rc-2026-11-28-997f9b8d9ffc\logs -CoordinationRoot C:\Antiphon\releases\candidates\rc-2026-11-28-997f9b8d9ffc\coordination -Repository michal-ciechan/Antiphon -PriorFailuresPath C:\Users\lndco\antiphon-seeds\f2de5b30.json -PriorFailuresDigest 0ceda907dba0aa653341e098f2874ac86f746cc49c18e72220ba1b388bdd24ca -AcceptCrossCandidateSeed -SeedSourceCandidateId rc-2026-11-26-29ba0c5002e6`

Start command, for the orchestrator only. It must not be run before the AppHost is restarted
onto the cut and `preflight.ps1` is GO:

```powershell
Invoke-CimMethod -ClassName Win32_Process -MethodName Create -Arguments @{ CommandLine = '"C:\Program Files\PowerShell\7\pwsh.exe" -NoProfile -NonInteractive -File C:\Antiphon\releases\checkout-fc9a0547\scripts\qualification\start-supervisor.ps1 -TaskRoot C:\Antiphon\releases\task-fc9a0547 -LaunchScript C:\Antiphon\releases\task-fc9a0547\launch-rc.ps1 -LogRoot C:\Antiphon\releases\candidates\rc-2026-11-28-997f9b8d9ffc\logs -StateRoot C:\Antiphon\releases\state-fc9a0547 -RunId a627825956de493e891768878a814ba4'; CurrentDirectory = 'C:\Antiphon\releases\task-fc9a0547' }
```

Observe:

```powershell
pwsh -NoProfile -File C:\Antiphon\releases\checkout-fc9a0547\scripts\qualification\observe.ps1 -TaskRoot C:\Antiphon\releases\task-fc9a0547 -StateRoot C:\Antiphon\releases\state-fc9a0547
pwsh -NoProfile -File scripts/orchestration/watch-qual.ps1 -LogDir 'C:\Antiphon\releases\candidates\rc-2026-11-28-997f9b8d9ffc\logs\<run dir>' -StateFile '<owned-scratch>\qual.seen.txt' -Heartbeat 'C:\Antiphon\releases\task-fc9a0547\supervisor-heartbeat.json'
```

### Dry run (preflight) at 2026-11-18T18:15Z: NO-GO, 2 rows

Of the 25 rows, 23 passed. Passing rows include:

- HEAD, clean, markers, no alternates
- launch-rc identical to the cut
- run id unused
- no locks
- seed digest
- journal and remote ref = cut
- old f2de5b30 lock absent
- no live supervisor or nightly, no test hosts
- docker
- memory 8.7 GB, disk 328 GB
- no reboot pending

The two failures:

- `AppHost on the cut` (advisory). AppHost is on e229fb4d3 (rc/2026-11-27). The orchestrator
  restarts onto rc/2026-11-28 first.
- `antiphon-postgres Up (healthy)`. There is no local `antiphon-postgres` container, which is
  consistent with the Postgres move to server2 (`ANTIPHON_SKIP_DEV_POSTGRES`). AppHost `/health`
  is 200. Test suites use their own Testcontainers Postgres, so this row is stale for
  post-cutover launches and is not a blocker. The orchestrator should confirm that the AppHost
  restart is healthy against server2 Postgres.

The launch was not run. `launch-rc.ps1` and `start-supervisor.ps1` have no `-WhatIf`/`-DryRun`.
`release-candidate.ps1 -WhatIf` exists, but before the clone existed it could only refuse with
`rc-ref-unresolved`, so the real journal was run (clone + journal, no push).

## Decisions for the orchestrator

1. Restart the AppHost onto rc/2026-11-28 (997f9b8d9).
2. Re-run `C:\Antiphon\releases\task-fc9a0547\preflight.ps1`. Expect only the stale postgres
   row to fail.
3. Run the start command.
4. Arm the watchers on the paths above.
