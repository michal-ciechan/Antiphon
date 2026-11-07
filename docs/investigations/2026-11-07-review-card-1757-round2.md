# CARD-1757 Review round 2 (task ed6858e5): live-query witness, diff only

Subject: `origin/feat/card-task-aa1cb08e` tip `86b1a964b4816d8bbadfb0aedfa8db7d3afb47b3`
(Debug task `aa1cb08e-3555-4ed3-b1d2-7863851b6dab`, Grok; original Code landing owner
`67d2b6b3-601c-4aab-aa39-d53c1b391c52`). Previously reviewed `01199454a5093b4f6629f1192ef53d5160a673e5`
(round 1, task `dc7e1fe2-dd8a-4879-a386-6d5c4251cc96`). Range `01199454a5..86b1a964b4`: one commit,
3 files, +45/-18. Base `0e7aef0379a073c206e8398dcaed539bc33eaf9b`, master `c2965dc7b1a377ce57aabfede628d5179bcdfaad`.
Round 2, Final profile, blockers and regressions only.

Verdict: CLEAN. Round 1 B-1 is closed. No blocker, no regression. Two non-blocking advisories.

## B-1 closure

1. Dead method. `internal static long? WorkingSet(int)` is gone from `HostStatsSamplerService.cs`.
   `git grep` over every tracked file at the tip finds no `HostStatsSamplerService.WorkingSet`; every
   `.WorkingSet(` call is on a `SamplerProcessQuery` instance (production `HostStatsRoutes.cs:52`
   passes `processes.WorkingSet` from the singleton into the sampler; four calls in
   `HostStatsSamplerTests`). The plan mentions the old name in prose only. `SamplerProcessQuery.cs`
   is byte-identical across the range.
2. V-1 (`Working_set_reader_does_not_snapshot_the_system_once_per_process`) now rejects
   `GetProcessById` / `WorkingSet64` / `GetProcesses` / `NtQuerySystemInformation` in the
   `SamplerProcessQuery` `WorkingSet`, `Read` and `ReadWindows` bodies. The `MethodBody` anchors
   (`long? WorkingSet(`, `Reading Read(`, `Reading ReadWindows(`) each hit their first occurrence
   at lines 40 / 54 / 91 of the query; `Reading Read(` cannot match `ReadWindows`; the `ReadLinux`
   body (the one Linux `/proc` read) is outside the scan. The sampler source must not declare
   `static long? WorkingSet`. DI still resolves `IProcessCpuProbe` as `SystemProcessCpuProbe` and the
   sampler probe as `SamplerProcessQuery`.
   V-2 (`Production_query_reads_this_process_once`) adds the production-wired tick: it resolves the
   singleton `SamplerProcessQuery` and `HostStatsSamplerService` from `AddHostStats`, asserts the CPU
   probe is that instance, ticks once (self target only, no `SessionRunnerRuntime` registered, no host
   pid) for exactly one query, reads one stored `runner` row with a positive working set, then requires
   the following `WorkingSet(pid)` to cost exactly one query. That last assertion binds the working-set
   delegate to the same singleton: a different instance would leave the singleton's cache fresh and the
   call would cost zero.
   Mutant reproduced once, uncommitted and restored (tree clean afterwards): line 51
   `return Read(pid, startedAt: default).WorkingSet;` replaced by
   `return Process.GetProcessById(pid).WorkingSet64;`. Native build-slot build (29 s) and a
   build-slot `dotnet run --no-build` of the two witnesses: 2 executed, 2 failed. V-1 at
   `HostStatsSamplerTests.cs:80` (`GetProcessById` at index 224 of the `WorkingSet` body); V-2 at
   `HostStatsSamplerTests.cs:170` (`wired.Queries - consumed` was 0, expected 1). This is the plan's
   PC-1 expected red exactly.
3. Plan `### Positive controls`: PC-1..PC-3 are source mutants; PC-4 is the Windows native row and
   names `Production_query_reads_this_process_once`, not V-1 (round 1 A-1 carried).
4. Regression. The range deletes an uncalled method and changes tests and the plan only. CP-1 green
   at the tip. `scripts/check-evidence-diff.ps1` base..head: 4 commits, 0 entries, 0 violations.
   `git merge-tree --write-tree origin/master HEAD` clean, tree
   `bb89bd0a7fb4cb3e2deb5595600fa868c68ca314`.

## Advisories (non-blocking)

- A-1 V-1 scans three method bodies. A regression that adds a new helper in `SamplerProcessQuery.cs`
  holding the snapshot tokens and calls it from `WorkingSet` beside `Read` keeps V-1 and V-2 green
  (`Queries` stays 1). Round 1 offered this body-scoped option explicitly; scanning the file with only
  the `ReadLinux` body excised would close the vector. A Mutation row may carry it. No action now.
- A-2 The new V-1/V-2 assertions arrived in the same commit as the dead-method deletion, so there is no
  committed red. The restored mutant above and the plan's PC rows supply the red. Mutation owns it.
- Not re-run: `scripts/check-os-witnesses.ps1`. The Code declaration JSON is ignored and not on this
  mirror. The OS-gated production path (`ReadWindows`) is unchanged in the range and PC-4 now names
  the right native test. Round 1 ran the lint on the previous tip.

## Verification this round

One checkpoint-tool run, CP-1 only, at the tip (`.antiphon/checkpoints/20261107-171259-74b2`,
`TEMP=/tmp`, cold output deleted by the tool on green):

```
CHECKPOINT CP-1 commit=86b1a964b4816d8bbadfb0aedfa8db7d3afb47b3 build=ok filter=/*/*/(HostStatsSamplerTests*)|(HostStatsProbeParseTests*)|(HostStatsEndpointTests*)|(HostStatsStoreTests*)/* executed=31 passed=31 failed=0 skipped=0 trx=/work/worktrees/task-ed6858e5/.antiphon/checkpoints/20261107-171259-74b2/rows/CP-1/run.trx slot=granted waited=0s dirty=0 source=86b1a964b4816d8bbadfb0aedfa8db7d3afb47b3 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-1 waited=0s executed=4.456s wall=4.508s
PHASES CP-1 slotWait=0s build=87.9500634s startup=2.6110859s testsWall=0.6244975s teardown=0.5328168s hostWall=3.7684136s
BUDGET wall=137.829s execution=137.826s admissionOnly=0.002s
unlisted: none (the tool ran no other build or test command)
wall: 2m17s  sequential-equivalent: 0m04s  builds: 2  max-concurrent-builds: 1  rows: 1 green 0 red 0 skipped
outputs: deleted bin-c1757-host/
verdict: GREEN exit=0
CHECKPOINT SOURCE VALID source=86b1a964b4816d8bbadfb0aedfa8db7d3afb47b3 rows=1
```

TRX identities: `Working_set_reader_does_not_snapshot_the_system_once_per_process` Passed 0.09 s,
`Production_query_reads_this_process_once` Passed 0.20 s; 31/31 Passed.

Other runs (both through `scripts/build-slot.ps1`, reason: the brief's one-off mutant reproduction):
mutant build `tests/Antiphon.SessionRunner.Tests` native output, and the two-witness run above, 2/2 red.

Not run: CP-2 (R-2 guard batch). None of its eight classes reads the three changed files; round 1
ran it green at `01199454a5` on the same inputs. The whole Unit lane, the Windows native row
(PC-4) and the desktop re-trace stay pending. Nothing ran on the desktop. PCs stay pending for
method-scoped SourceLanding Mutation.

--- review evidence ---
subjectTaskId: 67d2b6b3-601c-4aab-aa39-d53c1b391c52
reviewedSourceSha: 86b1a964b4816d8bbadfb0aedfa8db7d3afb47b3
reviewedSourceClean: true
ordinaryScopeCompleted: Full
reviewRound: 2
reviewedRange: 01199454a5093b4f6629f1192ef53d5160a673e5..86b1a964b4816d8bbadfb0aedfa8db7d3afb47b3
