# S4a A-custody mutation wave 1 — task 5050b2de

**Complete for this assigned slice: A21-hash and A11 killed at their required preservation assertions; both exact methods passed after source restoration and one fresh restored build.** Six executed test results: four passes, two expected mutation failures, zero skips. Two builds passed. No survivors, equivalent controls, masked controls, missing witnesses, unrelated failures or timeouts occurred in these two selections.

This is the explicitly waived ordinary-worktree informational sweep, following Custom 6569dd36. It is not CARD-0478 SourceLanding evidence, has no sourceLandingOperationId, and makes no overall S4a/SourceLanding completion or activation claim. The caller's standing authority covered the work; no additional approval was requested. No product/test repair, landing, deployment, desktop execution, rc/master push, or land-graph update occurred.

## Identity and frozen mutation pack

Tested base and HEAD throughout execution: `fed5899951daa3c66be3dee31355e6261286dc01`. Assigned branch: `feat/card-task-5050b2de`; checkout: `/work/worktrees/task-5050b2de`; host: Linux server2 mirror, Debian 12, x86_64, .NET 9.0.22, TUnit 1.44.0 / Microsoft Testing Platform 2.2.2. Tracked source/index were clean initially. Only this report is committed.

Read the full Investigate plan from origin/feat/card-task-ce37c38f (0037ffbd9), including its pack/masking/skeleton sections, and Custom 6569dd36's full Goal, Result and report. Bindings were inspected against Code 0c839994 (A21-hash/M1), Code 23889c9b (A11/M11), their referenced Review evidence, and the two current source/witness files. The earlier author results remain historical; this report supplies the fresh ordinary-worktree executions at the pinned base.

One temporary environment selector, `ANTIPHON_MUTATION_5050B2DE`, accepted only empty, `A21-hash`, or `A11`. Unknown values throw before tests through both the driver validation and the temporary server module initializer. No test-name/reflection selector mapping was used. Baselines used the frozen packed assembly with the switch off; they are not pristine-build baselines. Only one mutation was active per red process. Each selected branch emitted an activation marker, retained in its log/TRX. No source edits occurred while any build/test ran.

| Selector | Temporary change | Exact filter | Intended red / actual result |
|---|---|---|---|
| A21-hash | In WorktreeNoFollowDelete.HashAt, bypass only post-hash SameContent(stat, ended); keep stat acquisition, after=ended, and final unlink-time check | `/*/*/WorktreeNoFollowDeleteTests*/C1717_B3_WriteThroughAnOpenHandleDuringTheHashIsKept` | File.Exists(big) false; actual expected assertion failed 1/1 |
| A11 | RestoreCustody returns null, bypassing custody for both restore callers; deletion-time CustodyOf remains intact | `/*/*/LandingRemovalPolicyControlTests*/C1717_B4_ForeignDirectoryIsNotRestored` | Foreign file moved/restored into Source; actual expected assertion failed 1/1 |

A11's temporary selected branch also copied the witness's `foreign.txt` and `rescued-real-tree/keep.txt` to the external red-run directory before returning null. This is evidence-only instrumentation before the bypass; no test was changed and no extra destructive fence was removed. Both preserved files have their exact expected bytes (below). The first preservation assertion stops the red method before its later reason/byte assertions; those later assertions are not claimed to have executed in red. Restored green executed the complete original witness.

## Source and binary hashes

All hashes are SHA-256 unless identified as a Git blob. The two exact paths were restored using `git checkout -- server/Infrastructure/Git/WorktreeNoFollowDelete.cs server/Infrastructure/Git/GuardedWorktreeRemoval.cs`. Their restored raw bytes equal the base Git blobs. The helper/module initializer was removed by that restoration.

| Exact source path | Original = restored SHA-256 | Packed SHA-256 | Base Git blob |
|---|---|---|---|
| server/Infrastructure/Git/WorktreeNoFollowDelete.cs | `636d5460bde9757b2a1f1aed39ca16096c4b08505c3c32f0ff1368f32e24f772` | `f158fe87b6f7c764f43a3e638872e56078be4eff3da8d6d4d996bdb705804fb6` | `5374b5c4a0849a7219ea4d45592a2bee45735372` |
| server/Infrastructure/Git/GuardedWorktreeRemoval.cs | `9ad3fee678ee5eae31b68f58d813ab6cfa8ade4fd63f435784f585f4a239025b` | `abff108a2ee93e39060f97051eae703fb7eb408ae0f328015c81246fe8acb41e` | `21532f34d70615a4363f1cb5ecc9bf06e3a70762` |

Archived patch: `/work/evidence/task-5050b2de/mutants.patch`; SHA-256 `3c56a2826f7f59978c13e2a6105549201f133e6f11befe7f30ded60b39676dcf`. The witness files were unchanged and matched base blobs; their hashes are in `witness-source-hashes.json`. The patch and temporary helper were never committed.

| Binary | Packed SHA-256 | Restored SHA-256 |
|---|---|---|
| server/bin/Debug/net9.0/Antiphon.Server.dll | `a4a06b453adb89839df72b6f0b3b014ec32ab4ff4f3e850fee1b62c8fe9a50ec` | `9f814510427b53d15dad19c1f910196fce73f8ee9cad5ab2f07cb510ffd21d55` |
| tests/Antiphon.Tests/bin/Debug/net9.0/Antiphon.Server.dll | `a4a06b453adb89839df72b6f0b3b014ec32ab4ff4f3e850fee1b62c8fe9a50ec` | `9f814510427b53d15dad19c1f910196fce73f8ee9cad5ab2f07cb510ffd21d55` |
| tests/Antiphon.Tests/bin/Debug/net9.0/Antiphon.Tests.dll | `0e22f97ac59842e12f45fef4d7ff799ce96b67d004c2830002923d0ea59e6817` | `bfae0c33cfa9925d3ae8187ecc087a887eda1ecbfadec363b6c34edb27d174f2` |

Producer and staged server DLL hashes matched before every test invocation and stayed unchanged afterward within each frozen phase. Packed producer/staged binaries contained the selector/helper markers. The restored producer/staged binaries have neither marker and differ from packed binaries. Raw validation is in `packed-dll-validation.json` / `restored-dll-validation.json`; per-run source/DLL identities are in every invocation/receipt.

## Execution and fresh TRX ledger

Every build and test driver received its own genuine BUILD SLOT grant (eight grants total, maxcpucount=6). Native default output, `UseAppHost=false` and `TEMP=/tmp` were used for both builds and all no-build runs. Build admission was bounded at 10 minutes, execution at 20 minutes; exact-method admission at 5 minutes, execution at 3 minutes. These are the caller's explicit provisional envelopes, without test-attribute changes; not a newly measured p95. No admission retry, unleased fallback, -NoSlot, class/namespace/assembly run, or extra test selection occurred. All local foreground drivers and children completed and were joined.

Counts below are executed/passed/failed/skipped. Execution is observed grant-to-wrapper-exit time; wall includes admission and wrapper overhead. Admission values include acquisition overhead and differ slightly from the broker's rounded waited= values, preserved in the receipt.

| Operation | Selector | Exit | Counts | Admission s | Execution s | Wall s | Fresh TRX TestRun ID |
|---|---|---:|---|---:|---:|---:|---|
| build-packed | off / absent | 0 | build: 0 errors | 391.837 | 130.262 | 522.099 | — |
| baseline-A21 | off / absent | 0 | 1/1/0/0 | 0.724 | 8.654 | 9.378 | c4e285b4-b4da-4aaf-932a-d7364b6ca32b |
| red-A21 | A21-hash | 2 | 1/0/1/0 | 30.827 | 7.653 | 38.480 | 1049c529-b65b-4a77-a7f1-6fcc110ac7b2 |
| baseline-A11 | off / absent | 0 | 1/1/0/0 | 46.235 | 7.250 | 53.485 | ef2c1160-c15a-4210-9e3a-39a4b08adedf |
| red-A11 | A11 | 2 | 1/0/1/0 | 91.483 | 15.271 | 106.754 | 40e65f7e-d045-4dbc-8574-26856b2cc82c |
| build-restored | off / absent | 0 | build: 0 errors | 31.801 | 422.993 | 454.794 | — |
| restored-A21 | off / absent | 0 | 1/1/0/0 | 0.989 | 8.539 | 9.528 | ed438999-b47a-45bb-b3ff-ffd81a5b179b |
| restored-A11 | off / absent | 0 | 1/1/0/0 | 1.060 | 7.002 | 8.062 | faddfb9c-0ec5-4cec-b2d9-3bf1c2718729 |

Each method is non-parameterized: the expanded roster is exactly its named method with no arguments, one result per driver. All six TRX run IDs and six execution IDs are distinct. Full execution IDs, test IDs, outcomes, assertion strings and exact per-test durations are in `results.json` and each fresh `<run-name>/run.trx`. Test exit 2 means expected failed tests on this native driver; it was not a build/fixture error.

Packed build: 1,769 warnings, 0 errors, MSBuild 2m09.30s. Restored build: 1,746 warnings, 0 errors, MSBuild 7m02.32s. Existing analyzer/package warnings (including RouteHandlerAnalyzer AD0001) remain in the logs and are not mutation evidence.

Exact red A21-hash assertion:

```text
ShouldAssertException: File.Exists(big)
    should be
True
    but was
False

Additional Info:
    a file written while it was hashed is not unlinked
```

The preceding writerError-null and wrote-true assertions passed, so this was the required deleted-big-file failure, not missed inotify setup. The final unlink-time guard was retained; this result supplies no independent A23-final-stat credit.

Exact red A11 assertion:

```text
ShouldAssertException: File.Exists(Path.Combine(f.Source, "foreign.txt"))
    should be
False
    but was
True

Additional Info:
    the foreign directory is not restored
```

External preserved bytes:

| Artifact under red-A11/ | Bytes | Content | SHA-256 |
|---|---:|---|---|
| foreign.txt | 18 | foreign-only-bytes | `0300a09d7be4bbd2ad220276f1e61c571b08303e45e9c26dab40d507477179fe` |
| rescued-keep.txt | 12 | private work | `fab3822eb2e7b34ec79ef02cb5f2748edf7e8a7888958bf35696f29b30ce4969` |

## Timing and scope limits

The sequential execution envelope ran from 2026-11-06T09:36:20.601Z to 2026-11-06T09:57:36.573Z: 1275.972 seconds including between-command gaps. Summed driver wall 1202.580 seconds = admission 594.956 + execution 607.624 seconds. The outer task stayed below its 90-minute budget. No selection/batch execution exceeded 30 minutes. Prior Debug 5ae98d31 costs and provisional bounds remain historical.

Opt-in test phase recording was enabled per exact-method process, with unique run/group identities. It produced start/complete records, empty phase/process arrays and per-case remainder. Therefore detailed container/readiness, fixture, migration/schema, web-host, database, filesystem/CLI-call and teardown subdivisions are **unknown/uninstrumented**, not zero. The selected local fixtures require no PostgreSQL/DIND or real runner. The assembly's existing production-runner isolation guard was emitted in each TRX. No Pty test assembly was run.

The following TRX spans describe pre-case/discovery/setup, case, and post-case spans, not independently measured fixture/host phases. Native driver execution also includes process startup/shutdown outside the TRX span; that remainder is not assigned to an invented phase.

| Run | TRX pre-case s | TRX case s | TRX post-case s | TRX run s |
|---|---:|---:|---:|---:|
| baseline-A11 | 4.223 | 0.146 | 0.198 | 4.567 |
| baseline-A21 | 4.118 | 1.483 | 0.185 | 5.786 |
| red-A11 | 9.136 | 0.241 | 0.233 | 9.610 |
| red-A21 | 3.791 | 1.207 | 0.216 | 5.214 |
| restored-A11 | 3.730 | 0.131 | 0.171 | 4.031 |
| restored-A21 | 3.872 | 0.961 | 0.222 | 5.055 |

## Restoration, evidence and continuation

After the last red, both exact source files were restored/hash-verified before the restored build. One fresh restored build then produced the marker-free binaries used by both greens. All 72 task-owned native bin/obj roots (none existed initially) were inventoried, with 4,537 file/link records and file hashes, then removed through individually written literal absolute paths in `cleanup.ps1`. Cleanup completed and a fresh inventory found no remaining bin/obj/TestResults roots. Tracked source was clean before creating this report. Raw evidence was never stored in tracked source.

Persistent evidence: `/work/evidence/task-5050b2de`. It contains `manifest.json`, `mutants.patch`, source/DLL validation, `restoration.json`, both build directories, six method directories with invocation/receipt/log/TRX/HTML/timing, preserved A11 bytes, `results.json`, `phase-summary.json`, output inventory/hashes, and cleanup script/log. This informational sweep has no formal SourceLanding custody/restoration receipt.

To reproduce in a newly authorized ordinary worktree at the tested base: apply the archived patch, use the archived `run.cjs` driver after choosing a fresh evidence destination and matching source manifest, build once, run only each control's baseline and selected red, restore both literal source paths, rebuild once and run the same exact methods green. The driver command shapes used here were:

```sh
node /work/evidence/task-5050b2de/run.cjs build build-packed
node /work/evidence/task-5050b2de/run.cjs test baseline-A21 '' A21-hash
node /work/evidence/task-5050b2de/run.cjs test red-A21 A21-hash A21-hash
node /work/evidence/task-5050b2de/run.cjs test baseline-A11 '' A11
node /work/evidence/task-5050b2de/run.cjs test red-A11 A11 A11
# Restore both exact paths and verify base blobs, then:
node /work/evidence/task-5050b2de/run.cjs build build-restored
node /work/evidence/task-5050b2de/run.cjs test restored-A21 '' A21-hash
node /work/evidence/task-5050b2de/run.cjs test restored-A11 '' A11
```

Existing evidence directories are exclusive and must not be overwritten. The archived driver binds this checkout/evidence root; adapt only those identities for a commissioned repeat. Its full underlying build-slot/dotnet argv and properties are retained per invocation.

The remaining A-custody inventory is A1–A10, A12–A20, A22–A24, including the extra A18 witnesses (24 remaining method bindings). Those selections were not authorized or run in this slice. In particular A23 needs its deterministic post-hash/pre-unlink witness and A24 has no independent length-only red established. Windows/native and other group queues remain as recorded by ce37c38f. No repair is proposed or implemented here.

--- next stage ---
next: mutation
handoff: Retain A21-hash and A11 as completed waived informational cycles at fed589995; commission bounded slices for the remaining A-custody controls from ce37c38f. Preserve known missing/masked witnesses and the separate formal SourceLanding obligation.
artifact: /work/worktrees/task-5050b2de/docs/investigations/2026-11-06-mutation-s4a-A-custody-5050b2de.md
