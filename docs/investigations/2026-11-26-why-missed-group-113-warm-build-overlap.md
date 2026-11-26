# Why Windows group 113 missed the warm-build sharing delay

## Introducing commits

CARD-2054 started creating cache files through `SafeWrite.OpenCreate` in
`17bf7ebc38709d7b1b6891f4cd40b61701b44be5`. The overlap witness entered master
in `ab845da0ed0a63fe8444d9b23112fdd0dbf6a4fa`. The seal transition in
`7315616512b320fa6970145adda2de9ff783dc56` added two exclusive sibling creates
to a successful seeded build, one to invalidate the prior seal and one to
publish the new digest. The run used `rc/2026-12-06` pin
`0cfc4ac1ffb41f5af159a0c1db423fe119950267`. [CARD-2054 plan](../superpowers/plans/2026-11-24-card-2054-checkpoint-cache-seal-plan.md)

## Windows failure

The supplied `1fe763a9` `antiphon-all-group-113.trx` excerpt records one failed
test, `CheckpointWarmBuildTests.overlapping_build_keeps_first_rows_partition`,
with duration 13.8715 s, exit 130 and two `skipped` rows. The test calls
`RunScheduler` in-process with `SealDriver` and `SealSlots`. Its five-second
caller watchdog starts before it opens the cache. `RunScheduler` returns 130
(`ExitCodes.Canceled`, reason `caller-requested`) when that token fires. It labels
pending rows `skipped` before considering a build failure. With both rows still
pending, the second build was never released by the fake slot gate, which waits
for the first test driver's `OnRow` signal. The real build-slot broker, process
exit, and process-kill paths are absent from this fixture. [Overlap witness](../../tests/Antiphon.Tests/Checkpoints/CheckpointWarmBuildTests.cs) · [Scheduler](../../tools/Antiphon.Checkpoints/Execution/RunScheduler.cs)

Before the fix, `OpenCreate` opened the file with `FileShare.None`, then
`Remember` reopened the path to capture creation identity. On Windows
`FilesystemIdentityReader` calls `CustodyStat`. Its `GENERIC_READ` open can
return sharing violation 32 against the creator's own live stream. It waits 50 ms
between attempts and makes 21 attempts, consuming about a second before returning
an unbound identity. Linux `statx` reads metadata without this second open. The
first seeded build pays for two self-conflicts before publishing row readiness.
Fixture setup also creates the lock, props and seal before the watchdog starts.
The self-conflicts are a reproducible source of delay. The Windows record has no
operation timing, so the time attributable to each delay and the host load is
unknown. There is no evidence of a path-length or case refusal. [Exclusive create](../../src/Antiphon.HostCleanup/Checkpoints/SafeWrite.cs) · [Windows custody probe](../../src/Antiphon.HostCleanup/Checkpoints/CheckpointNativeIo.cs)

The scheduler derives the same key for both builds from the project, properties
and source. It records the first build's partition after seal publication and
protects that key while the first row is pending or running. The second candidate
returns cold at the `inUse` check, before any partition or seal inspection. The
first row receives its producing properties and runs under the same `run.lock`
gate. With separate output IDs and retained producer properties, the overlap
does not require a file in the first row's partition to be reopened by the second
build. [Partition admission and sealing](../../tools/Antiphon.Checkpoints/Execution/CheckpointIntermediateCache.cs)

## Windows path audit

| Path | Windows sharing behavior |
|---|---|
| First creation of `run.lock` | Affected through `CreateBlank` and `OpenCreate`. The creator now records identity from its held stream. |
| Creation or replacement of `CheckpointIntermediates.props` | Affected when `PublishNewBytes` creates a `.tmp` sibling. The same-bytes fast path only reads the existing import. The sibling is closed before rename. |
| First seal install, warm invalidation and digest publication | Each `WriteSeal` creates a blank or staged file and formerly reprobed its own exclusive handle. `Prepare` closes its validation handle before replacement and `WriteSeal` closes the stage before `MoveFileExW`. |
| Another executor or standalone warm replay contending for `run.lock` | `TrustedPath` may still spend the bounded custody-probe interval against another live handle. A scheduled executor falls back cold. A standalone warm replay refuses `build_cache_unavailable`. |
| Preparation and replay reads, publication digest and rename | `ReadKind`, `CheckReplayProps`, file digests and destination replacement can still encounter an external holder. Preparation declines reuse, invalidation falls back cold before launch, and failed publication retains the invalid seal while a verified output remains usable. The seal writer does not retain its own exclusive validation or staging handle through rename. |
| Same-key overlap within one executor | The `inUse` decision makes the second build cold before filesystem probing. The scheduled no-build row keeps the warm layout and gate. |

The repair gets the creation identity from the native stream whose exclusive
create already succeeded. It retains the existing regular-file, link, owner and
identity checks and still leaves unverified identities unrecorded. [Creation registry](../../src/Antiphon.HostCleanup/Checkpoints/CheckpointCreationRegistry.cs)

## Review and selection gap

- Review `6a747534` was Full, round 1, and returned the change to Code. Its
  ordinary Linux matrix included all 25 then-current warm-build cases, 13 Final
  guard classes, six scheduler/app/confinement/binding/row/driver classes and six
  receipt/cleanup/process-limit/selector classes. The source-validated run
  `20261125-123201-3d60` had 354 passes and 5 rostered skips. The historical review is at
  `19081daf0aeb876e9af29c2e57e282436dadbd76` on `origin/feat/card-task-6a747534`.
- Review `a8bd4504` was Full, round 2, and returned the change to Code. Linux
  run `20261125-145444-fd84` had 369 passes and 6 rostered skips across 30
  warm-build cases, the 13 guards, the six named runtime classes plus
  `EvidenceFolderTests`, and the six impact classes plus
  `CheckpointToolCommandTests`. A separate strict standalone replay passed six
  cases. [Round 2 checkpoint report](/work/review-evidence/task-a8bd4504/20261125-145444-fd84/report.md)
- Review `8b2639b7` was Full, round 3, and escalated a Linux limiter guard
  failure. Run `20261125-165209-a749` covered the same class groups with 31
  warm-build cases and standalone replay and shell-argv witnesses. It had 372
  passes, one limiter failure and six rostered skips. Task `70562bdf` corrected
  that limiter and passed those seven rows plus the exact limiter guard on
  Linux in `20261125-174631-0fdb`. The API records the subsequent verified master
  publication at `62b732612c51256e0323b0141018a0968fd3d7b8`. [Round 3 checkpoint report](/work/review-evidence/task-8b2639b7/20261125-165209-a749/report.md)

All three Full reviews explicitly left the CARD-2054 R4 and native Windows
cache witnesses pending. The follow-up limiter receipt used Linux too.

`CheckpointWarmBuildTests` is Unit and already ran in the Linux review. That
selection used Linux metadata semantics. The fixed short Merge subset and the
`checkpoint-tool` impact row select receipt and terminal surfaces. They do not
add a native warm-build selection. The short Merge process can also replay a
subject's Linux witness, which still cannot exercise the Windows custody retry.
The short rc cut replay `20261126-065938-5e6e` selected two
`PlanCoverageBindingTests` methods for the earlier group 113 failure. It had no
warm-build case. [Merge verification](../orchestration-loop.md#merge-verification-is-short-2026-11-18) · [Impact catalog](../../tests/impacted-classes.json) · [Cut gate receipts](2026-11-26-rc-cut-gates-at-0b07453e4.md)

## Cheapest pre-land check

Add a native-required CARD-1782 mapping for
`tools/Antiphon.Checkpoints/Execution/CheckpointIntermediateCache.cs` and
`src/Antiphon.HostCleanup/Checkpoints/SafeWrite.cs` to
`tests/Antiphon.Tests` filter `/*/Antiphon.Tests.Checkpoints/CheckpointWarmBuildTests/*`.
For the incident itself, the smallest native row is
`/*/Antiphon.Tests.Checkpoints/CheckpointWarmBuildTests/overlapping_build_keeps_first_rows_partition`.
Run it on an eligible Windows build slot against the reviewed repair SHA and
keep the TRX and phase evidence. A provisional one-minute test-run allowance
covers discovery and the one 13.87-second case sample, with build and slot waits
recorded separately. Native repair verification and the plan's independent
Windows contention, replacement and reacquisition witnesses remain pending.

## Linux verification

Both runs used separate granted build and test leases via `scripts/build-slot.ps1`.
They built `tests/Antiphon.Tests` in its default output with `-p:UseAppHost=false`
and executed it with `dotnet run --no-build` and the same property. Source was
clean for each run. TRX, slot logs and per-test phase journals are under
`/work/worktrees/ws-14/.antiphon/warm-overlap/`. Internal cache-operation timing
is unavailable for the Windows failure.

| Run | Tested commit | Passed | Failed | Skipped | Build / test slot wait | Build / test elapsed |
|---|---|---:|---:|---:|---|---|
| Red, exact exclusive-create witness | `e5cc538feb6a13bd0f5b10a0d328e376167154e4` | 0 | 1 | 0 | 0 s / 0 s | 298.29 s / 10.38 s |
| Green, whole warm-build class plus seven guards | `1913261a296b463025405bed8ed40751b2e4bf15` | 91 | 0 | 0 | 15 s / 180 s | 24.43 s / 63.32 s |

The red fixture keeps a real exclusive `FileStream` open and simulates the
Windows refusal for a second metadata open on that path. It observed one
unexpected path probe. Green records the same file identity from the held handle
and observes zero probes. The fresh green TRX contains 32 warm-build cases,
57 cases across the six standard guard classes and two plan-class selection
cases. The overlap case took 0.543 s on Linux. The repair verification is
portable evidence and the native replay remains owed.

| Run | Exact tree filter | TRX SHA-256 |
|---|---|---|
| Red | `/*/Antiphon.Tests.Checkpoints/CheckpointWarmBuildTests/cache_file_creation_does_not_reopen_its_exclusive_handle` | `b3bc3a75d1453a9ad94dd634de14621cfbf68f4d41f31f7be22c2676538b4655` |
| Green | `/*/*/(CheckpointWarmBuildTests*)\|(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(TestLaneCategoryGuardTests*)\|(PlanCheckpointClassSelectionGuardTests*)/*` | `f9f4569325685cc5525f4319668ceae1e73d79dbc060595e3f2eb24a7421354a` |

## Exemption

None.
