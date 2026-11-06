# S4a A-custody mutation wave 2 — task e469bdcd

**Complete for the assigned two-control slice: A17-D5 and A9 killed, with both switch-off baselines and both fresh restored-source greens passing.** Six executed test results: four passes, two expected mutation failures, zero skips. Two builds passed with zero errors. No selected control survived, was equivalent, was second-guard-masked, or lacked its witness. No selected run had an unrelated failure or timeout.

This is the explicitly **waived ordinary-worktree informational sweep**, not CARD-0478 SourceLanding evidence. There is no sourceLandingOperationId and none is invented. No overall S4a/SourceLanding completion, activation, product regression or native Windows qualification is claimed. The caller's standing authority covered execution and report publication; no extra approval was requested. Only this report is committed. No product/test repair, landing, deployment, desktop execution, rc/master push, or land-graph update occurred.

## Tested identity and selection

Tested base and execution HEAD: `10c3a8dd043326ac3abfefdb113b2fdd3e65a3c9`. Assigned branch: `feat/card-task-e469bdcd`; checkout: `/work/worktrees/task-e469bdcd`. Tracked source/index were clean initially and after exact source restoration. Host: Linux server2, Linux 4.15.0-213-generic x86_64; native test banner: Debian 12 x64, .NET 9.0.22, TUnit 1.44.0 / Microsoft Testing Platform 2.2.2.

Read Investigate ce37c38f's full inventory/pack/masking/brief skeleton at `0037ffbd94ab6d42074d77b31f90015f43ea6a34`, wave 1's report at `84103916e2d73d228f89b22421363557d8517ea1`, the testing/build and performance owners, Custom 6569dd36's full brief/result/report, and the selected sources/witnesses plus Code/Review evidence. A17-D5 binds 73c94e0b's D-5 path-walker control; A9 binds 23889c9b's M9 per-entry control. The selected production and witness files have **no diff** between Investigate/wave 1 base `fed5899951daa3c66be3dee31355e6261286dc01` and this assigned base; the empty comparison is stored in `base-binding-diff.txt`. Historical author/Review results are not relabelled as this task's executions.

A17-D5 fences deletion outside the managed tree through an exchanged directory symlink. A9 fences deletion of newly rewritten, unpushed working bytes during an approved walk. Both existing witnesses have direct preservation outcomes and no known masking/missing schedule in the inventory. These were selected ahead of reason-only or known-gap controls. A18's extra method obligations remain in the continuation ledger.

| Selector | Temporary mutation / exact-method filter | Required red / observed result |
|---|---|---|
| A17-D5 | Route only Linux Delete to the existing portable path walker. `/*/*/WorktreeNoFollowDeleteTests*/C1717_D5_ExchangedSymlinkDoesNotDeleteOutsideFiles` | Zero outside files may be deleted. Actual: 52 of 80 remained at attempt 4, after 3,595 exchanges; **28 outside files deleted**. |
| A9 | Bypass the Linux per-entry RejectChanged call (including its hash callback), preserving the other receipt/listing/identity/final-stat guards. `/*/*/WorktreeNoFollowDeleteTests*/C1717_B3_BytesChangedDuringTheWalkAreKept` | Rewritten file must survive. Actual: no ApprovedTreeRejectedException, and the changed file was unlinked; its pre-unlink bytes and immediate post-unlink absence were recorded. |

The two temporary branches share one file but run in **separate processes, never as simultaneous mutations**. A17-D5 selects the path walker with A9 inactive. A9 leaves the Linux fd-relative walker selected with A17-D5 inactive. No B-switch/salvage/policy mutant was introduced. Thus neither selected mutation masks the other's intended guard in its process.

## Frozen pack and selector admission

One frozen source tree, one packed native-default-output Antiphon.Tests build. Environment `ANTIPHON_MUTATION_E469BDCD` accepts only empty (switch off), `A17-D5`, or `A9`; an unknown value throws in both the external driver before spawning and the temporary server module initializer before test execution. The driver rejection was exercised without starting any build/test or requesting a lease (`unknown-selector-rejection.log`). No test-name/reflection selector mapping exists. Each selected branch emitted its activation marker in its fresh TRX. Baselines are selector-off controls on the **packed assembly**, not pristine-source builds.

A9 also includes evidence-only instrumentation on its selected Linux file branch: it reads the file before the ordinary unlink, archives the known rewritten bytes when encountered, then records immediate absence after the ordinary unlink returns. It changes no test and removes no additional fence. It is dormant for selector-off and A17-D5 processes. This observation adds file IO to A9 red, so that run's timing is not a production-performance benchmark. Its `outcome.Error` assertion runs before the witness's final byte assertion; the red method stops there. The later byte assertion is **not** claimed to have executed in red. The extra filesystem observation supplies the actual deletion evidence; restored green executes the complete unchanged witness.

No source edits occurred while any build/test ran. Exact source restoration was performed only after both red drivers had exited.

## Source and binary hashes

Hashes are SHA-256 unless identified as Git blobs. Archived patch: `/work/evidence/task-e469bdcd/mutants.patch`; digest `d9e3c9becb15dc947725eab7e4790126fd2b88fa69a1526056eb8ae0c0471e1b`. Temporary switch, mutations and instrumentation were never committed.

| Exact source | Original = restored SHA-256 | Packed SHA-256 | Base Git blob |
|---|---|---|---|
| `server/Infrastructure/Git/WorktreeNoFollowDelete.cs` | `636d5460bde9757b2a1f1aed39ca16096c4b08505c3c32f0ff1368f32e24f772` | `555279e2caef4a3b0895de8c61484427108f242a3e4e906c50d7672dda8ba2f9` | `5374b5c4a0849a7219ea4d45592a2bee45735372` |

Unchanged witness `tests/Antiphon.Tests/Infrastructure/WorktreeNoFollowDeleteTests.cs`: SHA-256 `f5ef7ab0a980a78556990082766c02373635a2686c897a505d95d1b955e383e8`, base Git blob `365a2f89aac2f4ed2e47c7ff0146489a332af2d4`. Its raw bytes still match the base. Restoration used the literal command:

~~~sh
git checkout -- server/Infrastructure/Git/WorktreeNoFollowDelete.cs
~~~

The restored raw bytes were compared with the pinned base Git blob and original hash; both agree. Temporary helper/selector removal was verified in source. One fresh restored native-output build followed. Producer/staged binaries match, contain neither temporary marker after restoration, and differ from the packed binaries. Full checks: `restoration.json`, `packed-dll-validation.json`, `restored-dll-validation.json`.

| Binary | Packed SHA-256 | Restored SHA-256 |
|---|---|---|
| `server/bin/Debug/net9.0/Antiphon.Server.dll` | `c4896187ca71dcfbbf34542b4143093d8ba78dadcbf9d312ac26ca84cb97c8a2` | `91850efac4e27b842b8fdc87173c39c29b6e011ca59b1aec0348214e7edb298c` |
| `tests/Antiphon.Tests/bin/Debug/net9.0/Antiphon.Server.dll` | `c4896187ca71dcfbbf34542b4143093d8ba78dadcbf9d312ac26ca84cb97c8a2` | `91850efac4e27b842b8fdc87173c39c29b6e011ca59b1aec0348214e7edb298c` |
| `tests/Antiphon.Tests/bin/Debug/net9.0/Antiphon.Tests.dll` | `45fe7d04912ce3b947227c4c50309d77aecfc7b53da1cb80d13550bbbc4cfe27` | `22a14ccde53fd5fcdc823a92ee8d9851bbd5592058ef4ab12e30704d90442ea9` |

Every test invocation records source and pre/post DLL hashes. Producer and staged server DLLs agree before every invocation, and all three DLL hashes stay unchanged across each test process within its frozen phase.

## Fresh execution ledger

Each build and each exact-method test driver had its **own genuine BUILD SLOT grant**: eight grants, maxcpucount=6. Every command used native default output, `UseAppHost=false` and `TEMP=/tmp`. Build admission bound: 10 minutes; build execution cap: 20 minutes. Method admission bound: 5 minutes; execution cap: 3 minutes. These caller-specified provisional envelopes changed no test attributes and are not claimed as measured p95. No unleased fallback, admission retry, -NoSlot, class/namespace/assembly selection or extra executed test selection occurred. All foreground drivers were awaited to completion before the next dependent operation.

Counts are executed/passed/failed/skipped. Admission is observed request-to-grant overhead; execution is grant-to-wrapper-exit; wall includes both. The broker's rounded waited/held values and exact grant/release identities remain in each receipt/log.

| Operation | Selector | Exit | Counts | Admission s | Execution s | Wall s | Fresh TRX TestRun ID |
|---|---|---:|---|---:|---:|---:|---|
| build-packed | off | 0 | build: 0 errors | 1.200 | 422.800 | 424.000 | — |
| baseline-A17 | off | 0 | 1/1/0/0 | 196.221 | 19.757 | 215.978 | da381568-4284-446b-8d8e-5220bd78c6d2 |
| red-A17 | A17-D5 | 2 | 1/0/1/0 | 46.742 | 9.294 | 56.036 | e7905366-38af-4413-b56a-5542a7afe668 |
| baseline-A9 | off | 0 | 1/1/0/0 | 90.809 | 11.535 | 102.344 | ede01686-2081-42cf-b89f-9d2cec10233e |
| red-A9 | A9 | 2 | 1/0/1/0 | 0.653 | 9.334 | 9.987 | 1dc69b72-8398-4c29-aca4-8743474dc60b |
| build-restored | off | 0 | build: 0 errors | 0.942 | 144.179 | 145.121 | — |
| restored-A17 | off | 0 | 1/1/0/0 | 16.384 | 19.576 | 35.960 | 8ac91323-4042-40ad-9d29-762d4e2360a5 |
| restored-A9 | off | 0 | 1/1/0/0 | 16.794 | 20.662 | 37.456 | 63a26063-ba08-4669-b279-cf11a996675b |

Both methods are non-parameterized: each expanded roster is its one exact method with `args: []`. All six TestRun IDs and all six execution IDs are distinct. `results.json` retains full test/execution identities, per-test durations, complete assertion messages and method rosters. Red exit 2 is this native driver's failed-test result, with one expected assertion each; neither red is a build/fixture/filter error.

Packed build: 1,769 warnings, 0 errors, MSBuild 7m01.54s. Restored build: 1,746 warnings, 0 errors, MSBuild 2m23.56s. Package/analyzer warnings, including RouteHandlerAnalyzer AD0001, are retained in the build logs and do not count as mutation evidence. An initial preparation command found python3 unavailable and made no edit or build/test launch; preparation proceeded with Node. That bootstrap failure is recorded in provenance separately from test results.

## Exact red assertions and deletion evidence

A17-D5 fresh assertion (TRX contains the full additional IOException/stack):

~~~text
ShouldAssertException: Directory.GetFiles(outside).Length
    should be
80
    but was
52

Additional Info:
    attempt 4 swaps 3595 removed outside files (System.IO.IOException: Directory not empty ...)
~~~

The refusal/cleanup IOException shown inside the diagnostic does not explain away the failing count: 28 outside files had already been removed. This is the required outside-preservation failure, not exchanger/setup failure. The earlier attempts passed; the method stops on this failed attempt and does not claim all 24 attempts ran in red. Both selector-off passes complete the unchanged method.

A9 fresh assertion:

~~~text
ShouldAssertException: outcome.Error
    should be of type
Antiphon.Server.Infrastructure.Git.ApprovedTreeRejectedException
    but was
null
~~~

A9 TRX activation/observation:

~~~text
MUTATION activated=A9
MUTATION changed-before-unlink=a/f3959
MUTATION changed-after-unlink=a/f3959 absent=True
~~~

External `red-A9/changed-before-unlink.txt` contains the exact 24 bytes `CHANGED-bytes-mid-walk!!`; SHA-256 `a3cabf1ec80ae0dbd1f8bbca996bdc163926daeede66d02eb21e3c7b577d8b7b`. `red-A9/changed-after-unlink.txt` records `a/f3959 absent=True`. The writer-error-null and late-file-not-null fixture assertions succeeded before the missing-refusal assertion. The observed absence is immediately after production unlink, before fixture teardown. This is a byte-loss control, not merely a changed refusal reason.

## Timing and phase limits

The build/test execution envelope was 2026-11-06T10:57:51.518Z through 2026-11-06T11:16:20.651Z: 1109.133 seconds, including gaps. Summed driver wall 1026.882 seconds = admission 369.745 + execution 657.137. Preparation, restoration, fresh build/greens, output inventory/cleanup and report publication fit within the 90-minute task budget. No selected batch or driver exceeded 30 minutes. Prior Debug 5ae98d31 and wave 1 timings remain historical, not substituted for this task's measured timings.

Opt-in phase recording used unique run/group identities per method process. All six recorder journals contain one start/complete case, empty phase/process arrays and case remainder; they are retained under each run's timing directory. Container/readiness, data preparation, fixture setup/teardown, migrations/schema, web-host/database, filesystem/CLI-call subphases and outside-TRX process startup/shutdown subdivisions are **unknown/uninstrumented**, not measured zero. These local filesystem fixtures require no PostgreSQL/DIND or real runner. Existing assembly production-runner/Hangfire isolation guards are recorded in the TRX; no Pty test assembly ran.

The following TRX intervals are pre-case/discovery/setup, test-case and post-case spans; they are not independent fixture/body/teardown measurements. Case intervals include fixture/body work which is not separately instrumented. Wrapper execution also includes time outside the TRX span; that remainder is not assigned to an invented phase. `phase-summary.json` retains these intervals, native recorder records, run/test/group binding and unknown fields.

| Run | TRX pre-case s | TRX case s | TRX post-case s | TRX run s |
|---|---:|---:|---:|---:|
| baseline-A17 | 10.481 | 1.086 | 0.413 | 11.980 |
| baseline-A9 | 4.578 | 2.988 | 0.412 | 7.978 |
| red-A17 | 4.680 | 1.219 | 0.438 | 6.337 |
| red-A9 | 4.351 | 1.693 | 0.200 | 6.244 |
| restored-A17 | 10.926 | 1.486 | 0.934 | 13.346 |
| restored-A9 | 9.417 | 4.780 | 0.494 | 14.691 |

## Cleanup, evidence and continuation ledger

After the two red runs, the exact source path was restored/hash-verified and the helper removed before the one restored build. Both restored greens used its verified marker-free binaries. All 72 owned bin/obj roots (none existed initially) were inventoried with 4,537 file/link records and file hashes, then removed through individually specified literal absolute paths in `cleanup.ps1`. The script's paths were compared exactly with the fresh owned-root inventory before deletion. Final scan found zero remaining bin/obj/TestResults roots; tracked source/index were clean before this report was created. No Docker volumes, shared caches or foreign outputs were manually removed.

Persistent evidence: `/work/evidence/task-e469bdcd`. Includes plan/wave-1 copies, provenance/base binding comparison, manifest, full temporary patch, driver/validation/summarizer, source/witness/DLL hashes, all eight invocation/receipt/log directories, six fresh TRX/HTML/phase journals, preserved A9 bytes, results/phase summaries, evidence validation, output inventory/hashes and literal cleanup script/log/final scan. Generated payloads remain external; only this report is published. This ordinary-worktree sweep produces no formal SourceLanding custody/restoration receipt.

Continuation inventory after retaining A21-hash/A11 from wave 1 and A17-D5/A9 from this wave: **A1–A8, A10, A12–A16, A18–A20, A22–A24** (20 selector rows; 22 nominal method bindings including A18's two extra replacement/unsealed methods). No other controls were run in this slice.

| Pending distinction | Continuation requirement |
|---|---|
| A2 | Layered/reason-only: entry-manifest hash still preserves changed evidence when pin source-hash is omitted. Do not claim byte-loss kill from the reason alone. |
| A8 | Reason-only: rmdir still preserves late bytes without final emptiness re-list. |
| A18-D4 | Retain new-bytes, replacement and unsealed exact-method obligations; do not collapse three bindings into one. |
| A23-final-stat | Known missing deterministic post-hash/pre-unlink writer schedule. Existing hash-time witness is insufficient; not run and no A23 credit from A9/A21. |
| A24-seal-length | No independent length-only red on current content manifest; legacy seal is superseded as authority. Not run; no invented equivalence/kill. |
| U9 | Matching-content replacement is still needed to independently fence deletion-time directory identity; the restore witness is not that control. Outside this slice. |

Known masked/missing rows remain their historical distinct results, not fresh kills or equivalent controls. Windows/native and other group queues remain as in ce37c38f. Missing witnesses or product/test repairs require separately commissioned work. The remaining executable A controls should receive bounded Custom mutation slices; the caller retains the separate formal SourceLanding obligation.

To reproduce only this slice in a newly commissioned ordinary worktree at the tested base, apply the archived patch, adapt only driver checkout/evidence identities to a fresh exclusive destination and bind the source manifest. The exact driver commands used were:

~~~sh
node /work/evidence/task-e469bdcd/run.cjs build build-packed
node /work/evidence/task-e469bdcd/run.cjs test baseline-A17 '' A17-D5
node /work/evidence/task-e469bdcd/run.cjs test red-A17 A17-D5 A17-D5
node /work/evidence/task-e469bdcd/run.cjs test baseline-A9 '' A9
node /work/evidence/task-e469bdcd/run.cjs test red-A9 A9 A9
git checkout -- server/Infrastructure/Git/WorktreeNoFollowDelete.cs
# Verify bytes against base Git blob before the fresh build:
node /work/evidence/task-e469bdcd/run.cjs build build-restored
node /work/evidence/task-e469bdcd/run.cjs test restored-A17 '' A17-D5
node /work/evidence/task-e469bdcd/run.cjs test restored-A9 '' A9
~~~

Do not overwrite this task's existing evidence directories. The archived invocation JSON supplies all underlying build-slot/dotnet argv, filters and properties. No further test run is requested for the completed controls.

--- next stage ---
next: mutation
handoff: Retain A17-D5 and A9 as completed waived informational cycles at 10c3a8dd043326ac3abfefdb113b2fdd3e65a3c9, alongside wave 1 A21-hash/A11. Commission bounded remaining A-custody slices; retain A23/A24 and other known missing/masked distinctions without running them as convenient survivor greens. Formal SourceLanding qualification remains separate.
artifact: /work/worktrees/task-e469bdcd/docs/investigations/2026-11-06-mutation-s4a-wave2-A-custody-e469bdcd.md
