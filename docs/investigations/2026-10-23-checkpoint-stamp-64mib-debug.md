# Checkpoint stamp 64 MiB refusal, 2026-10-23

**Confirmed: source stamps reuse a receipt reader capped at 64 MiB. Four of 81 surviving same-day completed checkpoint reports failed because ordinary build DLLs exceeded that cap.** The smallest correct repair is to use the existing full-content streaming digest for stamps, preserving the smaller receipt limit. Filed **CARD-1532**, Backlog, Importance High (`c47be8a2-e9c2-4c8b-8c07-4280ce673335`). This investigation changes documentation only; the implementation remains unfixed.

Debug `4f0126ba`, source `2a22d9b9855424f5d2758e329f3fced39f096df1`. No builds, tests, process stops, output deletion, settings changes or desktop/Windows qualification access. Other worktrees were read only. Generated census/evidence stays ignored under `/work/worktrees/task-4f0126ba/.antiphon/stamp-debug/`; the tables below are the durable findings.

## What gets stamped

| Boundary | Exact behavior and source |
|---|---|
| File population | [BuildConfinement](../../tools/Antiphon.Checkpoints/BuildConfinement.cs), `CaptureLedger`, `Capture`, `ListUntracked`: indexed paths from `git ls-files -z -s`, plus the union of `--others --exclude-standard` and `--others --ignored --exclude-standard`. Only root `.antiphon/` is excluded from the untracked/ignored population. Ignoring a file does not exempt it from integrity checks. |
| Outside current write roots | `CaptureLedger` stamps before slot acquisition, `AdmitLedger` checks after acquisition, `VerifyLedger` hashes after release. Default Linux admission can skip a hash only for unchanged, sufficiently old inode/ctime/stat evidence; strict admission hashes all. A recorded unreadable stamp refuses regardless of stat equality. |
| Inside current write roots | `Prepare` / `CaptureUntracked` stamp inside the slot and `Settle` compares after the build. Covered ledger paths are not stamped twice. Write roots change **when**, not **whether**, pre-existing files are checked. |
| Roots | `WriteRoots` and [ProjectWriteRoots.AddPair](../../tools/Antiphon.Checkpoints/ProjectWriteRoots.cs): current owned output layout plus the selected `bin-<id>/` and `obj/` beside statically visible projects. Another build's `bin-<different-id>/` is outside these roots. |
| Regular-file stamp | `Stamp` at line 427 calls `ReceiptFile.ReadBytes`, then emits `file|<length>|<lowercase SHA-256>`. Links hash the link target, directories and absence have shape-only stamps; unsafe/unreadable contents are unprovable. |
| Limit | [ReceiptFile.ReadBytes](../../src/Antiphon.HostCleanup/Checkpoints/ReceiptFile.cs), line 21, passes the literal **`64 * 1024 * 1024` = 67,108,864 bytes**. This is not a configurable knob. [CheckpointFileReader.Consume](../../src/Antiphon.HostCleanup/Checkpoints/CheckpointFileReader.cs) rejects an inspected size above the bound, and growth past it during reading, with `selected file exceeds size limit`. Exactly 64 MiB is permitted. |
| Classification | `Stamp` converts the exception to `unreadable|selected file exceeds size limit`. `Classify` reports `refused: unreadable` and an `UNREADABLE` path, not a proven content change. A separate readable change still makes the verdict `tampered`. |
| PowerShell parity | [run-checkpoint.ps1](../../scripts/run-checkpoint.ps1), `Get-CheckpointContentStamp` / `Get-CheckpointUntrackedManifest`, has the same inclusion and full-byte read. [checkpoint-file-reader.ps1](../../scripts/lib/checkpoint-file-reader.ps1), `Read-CheckpointBytes`, passes **67108864** to its embedded verified reader. |

Thus `tests/Antiphon.Tests/obj/Debug/net9.0/Antiphon.Tests.dll` is stamped because it is a **pre-existing ignored file**, even though it is a normal compiler artifact. `obj` is not an integrity exemption or proof of ownership. Its in-slot untracked capture can record unreadability without stopping compilation immediately; settlement then invalidates the build. A second build in the **same run** is enough to hit the defect, even when the first build and its tests passed. It does not require a retry or a previously red run.

The offending seven path instances in the four incident logs had already been removed when inspected, so their exact historical sizes cannot be restated from disk. An independent surviving artifact at `task-a3467729/tests/Antiphon.Tests/bin-c1483-s5-review/Antiphon.Tests.dll` measured **67,411,456 bytes = 64.2886 MiB**, mtime `2026-10-23T04:31:05.114Z`. This verifies the assembly has crossed the binary threshold; “65 MB” alone would not prove that. `BuildConfinement.cs` and `ReceiptFile.cs` at each incident's reported commit are byte-identical to this Debug's baseline, checked with `git show <sha>:<path>`.

## Why cleanup then refuses

1. [RunScheduler.BuildOneAsync](../../tools/Antiphon.Checkpoints/Execution/RunScheduler.cs) captures the ledger, acquires the slot, invalidates old binding state and creates the fresh output root **before** `AdmitLedger`. A large old output outside the current roots refuses admission; no compiler or test launches, but an output directory can already exist.
2. For pre-existing `obj` contents captured inside the slot, [ProcessDriver](../../tools/Antiphon.Checkpoints/Execution/ProcessDriver.cs) settles its baseline in `finally` after success, failure or cancellation. The compiler can report zero errors and still receive confinement exit 2. Only a fully accepted build reaches `CheckpointBuildBinding.Write` and the output custody seal. This is what happened in `269a3142/033639-6197`.
3. A timeout can instead leave partially built outputs, or a successfully built output whose test never obtained a slot. Neither implies a size failure. [CleanOwnedOutputs](../../tools/Antiphon.Checkpoints/Cleanup/OutputCleanup.cs) deliberately keeps nonzero runs unless `cleanOnRed` is set. All four size incidents had `keepOutputs=false`, `cleanOnRed=false`.
4. [CheckpointApp](../../tools/Antiphon.Checkpoints/CheckpointApp.cs), lines 473–489, subsequently checks selected output paths and appends `still-present` when they remain. Existing nonzero exit codes stay nonzero. Explicit cleanup can separately refuse missing/mismatched creation authority; it cannot adopt an old output merely because its name looks owned.
5. Both [ReportValidator.Validate](../../tools/Antiphon.Checkpoints/Report/ReportValidator.cs), line 26, and [validate-checkpoint-receipt.ps1](../../scripts/validate-checkpoint-receipt.ps1), line 70, return **`cleanup_refused` before checking the report's exit code or row failures**. This can obscure the original size refusal, timeout, compiler error or intentional red witness.

On retry, the same output ID can fail `pre-existing-build-source` / `pre-existing-output`. Choosing a new ID does not remove old files from the stamp population. “Repeat the run” in the unreadable diagnostic therefore cannot cure a deterministic oversized file. Failed evidence must remain historical; disabling cleanup validation or granting deletion authority from a disk ledger is not a fix.

## Same-day census

Captured **2026-10-23 04:34:16Z**. Enumerated all `/work/worktrees/task-*/.antiphon/checkpoints/*/report.json` files: **675 readable JSON reports**, **81** whose `startedAt` was 2026-10-23 UTC, no parse failures. Joined reports to their build logs, request and executor records. This covers surviving scheduler receipts, not deleted worktrees, still-running/missing reports, standalone PowerShell/direct-row runs or tool bootstrap. The main `reason` field was null on all four size refusals, so grepping only that field or only `cleanup_refused` is insufficient.

| Primary outcome | Runs | Evidence / interpretation |
|---|---:|---|
| Executor GREEN, exit 0 | 40 | Historical reported outcomes; no fresh receipt validation claimed. |
| **64 MiB source-stamp refusal** | **4** | Exact `UNREADABLE ... selected file exceeds size limit` in build logs; details below. |
| Executed test failure | 17 | Nonzero failed counts, includes intentional red-first witnesses; not 17 established product regressions. |
| Executed command failure | 7 | Nonzero command rows, also includes red-first checks. |
| Compiler error | 5 | Explicit CS diagnostics: `ccf88b5d/001401-c12e`, `52a5dc76/011536-08b4`, `/014233-6033`, `/015407-8e86`, `75c32a51/015408-ff6c`. |
| Total deadline | 4 | `550d40c4/034914-0c4b`, `663fcd39/035425-b9ef`, `f46a522f/035634-2a98`, `ddd3fc81/040009-d640`; exit 5. |
| Build execution deadline | 2 | `f46a522f/034930-52fd` and `269a3142/034937-1f5b`; surfaced as generic build-failed/exit 2. |
| Row/command execution deadline | 1 | `f93ec86b/014342-1d38`, CP-1 `timeout=2m`; exit 5. |
| Owner unverified | 1 | `52a5dc76/003945-c249`; exit 7. |
| **Total** | **81** | 41 nonzero runs: four size-cap, 29 executed failure/compiler diagnostics, seven deadlines, one owner failure. |

**27** reports carried cleanup refusals: **24 `still-present`**, three `output-ledger-missing`. Only **four** of those 27 are evidenced size-cap cases; the other **23 must not be counted as this defect**. In particular, `15cca245/030003-1412` is green; `663fcd39/035425-b9ef` is a total timeout followed by retained outputs. The starvation investigation independently establishes the deadline classifications above.

All short run IDs below have prefix `20261023-`; evidence root is `/work/worktrees/task-<task>/.antiphon/checkpoints/<run>/`. Every size incident has exit 2, cleanup `still-present`, and no test failures in its refused rows.

| Task / run | Refusing build / oversized path beneath `tests/Antiphon.Tests/` | Lost verification and retained partial success | Run minutes |
|---|---|---|---:|
| `1bada46b` / `024001-a19e` | `bin-c1486-final`; prior `bin-c1486-s5-green/Antiphon.Tests.dll` and `artifacts/obj/Antiphon.Tests/debug/Antiphon.Tests.dll` beneath that prior root | CP-11 refused before compilation; CP-10 passed **2/2** | 8.20 |
| `bb5773d2` / `031211-9b7e` | `bin-c1483-s5`; retained `bin-c1483-s5-red/` DLL and corresponding artifacts/obj DLL | CP-5 refused before compilation; **0 tests** | 1.27 |
| `269a3142` / `033639-6197` | `bin-c1510-green`; `obj/Debug/net9.0/Antiphon.Tests.dll` | Compiler **0 errors**, elapsed **00:06:40.44**, then unreadable settlement; CP-2/4/7 **0 tests** | 8.23 |
| `87c5538f` / `042203-b73e` | `bin-c0151-s1-green`; prior `bin-c0151-s1-red/` DLL and corresponding artifacts/obj DLL | CP-2 refused before compilation; CP-1 passed **8/8** | 6.83 |

These four unsuccessful runs blocked **six rows** and consumed **24.54 aggregate run-minutes**. That sum includes useful first-build/first-row work and waiting; it is not measured wasted CPU time or a claim that every refusal rebuilt the assembly. Subsequent successful receipts exist for `1bada46b` (CP-11 365/365, CP-10 2/2), `bb5773d2` (CP-5 144/144) and `269a3142` (CP-2/4/7 16/16); they do not erase these failed attempts.

| Incident source binding | Full reported SHA |
|---|---|
| `1bada46b` | `5425942116c201283ae5cc0fa47f1f727cd9838b` |
| `bb5773d2` | `a8b433277a88bce0ba76468c08d584312162fa75` |
| `269a3142` | `705662142c95e9c3614a8a229f0fbd4b2bbf2c66` |
| `87c5538f` | `db87d1282f505688e249c3d8dd63287bbce26434` |

Representative unedited build-log evidence, from `269a3142/20261023-033639-6197/builds/bin-c1510-green/build.log`:

```text
    0 Error(s)
Time Elapsed 00:06:40.44
refused: unreadable
UNREADABLE tests/Antiphon.Tests/obj/Debug/net9.0/Antiphon.Tests.dll: selected file exceeds size limit
```

Reproduce the census read-only by parsing the report glob, filtering `startedAt` by UTC date, and joining `builds/<id>/build.log` for exact `UNREADABLE` reasons plus compiler summaries. Join `request.json` and `executor.log` to distinguish execution/total deadlines. Do not infer root cause from the row state `slot-refused`: the size incidents report **slot=granted**, with a confinement refusal after acquisition.

## Repair ranking and integrity

| Rank | Proposal | Integrity and cost |
|---|---|---|
| **1 — implement** | Stream a full-file digest under a separate content bound; reuse `ReceiptFile.Digest` / `CheckpointFileReader.Digest`. | Already implemented for [SafeWrite.Describe](../../src/Antiphon.HostCleanup/Checkpoints/SafeWrite.cs), with **`ContentLimit = 1L << 30` (1 GiB)**, 256 KiB chunks, verified regular single-link handle/containment, three-second progress-rearmed stall window and cancellation. Hashes every byte, preserving the existing stamp guarantee and bounded memory. Normalize the returned uppercase digest to the existing lowercase stamp format. Files above 1 GiB still refuse. Keep receipt `ReadBytes` at 64 MiB. Add equivalent streaming support for the PowerShell stamp in the same card. |
| **2 — only with replacement coverage** | Exclude directories genuinely owned by the current executor. | A directory name, ignored status or persisted output ledger proves no custody. Blanket `bin*/obj` exclusions weaken detection of changed/deleted pre-existing files. A narrow optimization can preserve integrity only by transferring equivalent full content/shape checks to verified same-process ownership records at the required boundaries. Fresh current output is already absent from the baseline; this does not solve ordinary pre-existing `obj` or older-process output, and is more complex than rank 1. |
| **3 — reject** | Size + mtime + hash of a prefix above a threshold. | A same-size change beyond the prefix with restored mtime compares equal. This removes full-content tamper evidence, especially on portable filesystems; neither a larger prefix nor the existing Linux admission shortcut repairs the missing capture/settlement coverage. |

Simply increasing the global `ReadBytes` literal preserves full hashing but allocates whole-file buffers, expands receipt resource admission and postpones the next limit incident. It is inferior to the streaming API already present. The “content stamps stream” comment in `CheckpointFileReader` currently describes creation-record hashes, not `BuildConfinement.Stamp`.

CARD-1532 acceptance should use focused fixtures: real unchanged stamps at 64 MiB and 64 MiB+1 under ignored prior `bin-*` and `obj`; full-content tail mutation with unchanged length/restored mtime; same-run second-build and retry admission; content-limit refusal; receipt cap unchanged; PowerShell parity; existing link, special-file, stall, cancellation and shape refusals retained. Preserve current admission/stat limitations documented in the testing owner; this is not a stronger concurrency-proof snapshot. No timeout, source-clean or cleanup-validation relaxation is needed.

## Ownership and duplicate check

The [testing owner](../testing-and-build.md#checkpoint-runner-tool-card-0723) documents both the 64 MiB source-reader bound and the separate 1 GiB output digest. CARD-0723 owns the single-run manifest/report tool; CARD-0585 owns closed checkpoint selections and fewer rebuild cycles with unchanged verification rigor. This repair supports those contracts rather than bypassing them.

Complete `card.ps1 search '<phrase>' -Board Antiphon -All` searches returned: `stamp` **170**, `64 MiB` **1**, `cleanup_refused` **10**, `unreadable` **28**, `size limit` **3**, `streaming hash` **0**, `64MiB` **0**, `large DLL` **0**; every search reported `truncated=false`, no continuation. Full relevant cards read: 0723, 0585, 1241, 1358, 1363, 1368, 1369, 1520, 0506 and 1531. The sole `64 MiB` hit, CARD-0506, has an unrelated concurrency-settings description.

| Existing owner | Relationship; no duplicate filed |
|---|---|
| CARD-1241 | Moves non-output hashing outside the build slot and defines admission checks; does not remove the size cap. |
| CARD-1358 / CARD-1363 / CARD-1369 | Progress-sensitive stalls, unreadable classification, PowerShell parity and EOF-stall hardening; different failure mechanism. |
| CARD-1368 | Runtime output custody; already supplied the streaming digest used by creation records. Source stamps still call the capped byte reader. |
| CARD-1520 (related CARD-1445) | Cleanup reason preservation / Windows cleanup investigation; does not own this pre-build or post-build source stamp refusal. |
| **CARD-1531 / Debug e8b38df0** | [Build-slot starvation investigation](2026-10-23-build-slot-starvation-debug.md), read from its sibling worktree, already separates three earlier unreadable incidents from deadlines and explains `cleanup_refused`. Its fix is admission/total-clock accounting, not stamping. This later census adds `87c5538f` and locates the cap/fix. |

Created exactly one new fix card, **CARD-1532**, at `2026-10-23T04:38:28Z`; response confirms Backlog and High importance. Land this diagnosis; commission its code repair separately. No historical receipt is relabeled and no live Windows work is implicated.

--- next stage ---
next: land
handoff: Land this documentation; plan CARD-1532 for full-content streaming stamps, including PowerShell parity. Keep CARD-1531 deadline accounting and CARD-1520 cleanup diagnostics separate.
artifact: docs/investigations/2026-10-23-checkpoint-stamp-64mib-debug.md
