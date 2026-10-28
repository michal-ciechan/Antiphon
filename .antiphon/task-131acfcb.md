# Final Review 131acfcb: CARD-1573 seal-limit redo (subject 56ddd55e)

Reviewed `feat/card-task-56ddd55e` at 39a7f70c4642785a0ce51461a5dc8d1d78fd8129 (base rc/2026-10-27 f84228f285dd3e4ad7b5dc8260cbc9f819256f50, still the rc tip and an ancestor). Host server2/Linux. Read-only; every mutation was made on scratch copies of `scripts/` outside the worktree.

Outcome: the D1 (compiled facade) and D2 (inode reuse) repairs work on Linux, with red witnesses. One new defect: the new Unit test is red by construction on Windows (D-1). The Unit-lane shard plan is a template only: a Full Final receipt is claimed, not demonstrated.

## Runs (all through scripts/build-slot.ps1)

- Build: `dotnet build tests/Antiphon.Tests --property:OutputPath=bin-r131/`, lease d4b16d32, 183 s, 0 errors.
- Witnesses: `--no-build` with filter `/*/Antiphon.Tests.Checkpoints/(CheckpointFreshOutputSealTests*)|(CheckpointLargeStampScriptTests*)|(CheckpointOwnedOutputTests*)/*`, lease 3bea5fe1. total 15, passed 15, failed 0, skipped 0. That is 2 C1573 tests, 3 C1532 tests (`C1532_script_stamp_matches_full_digest`, `C1532_script_digest_limits_stalls_and_eof`, parser) and 10 owned-output cases. C1573 embedded test 5.8 s, compiled facade 3.9 s.
- `scripts/check-evidence-diff.ps1 -Repository . -BaseRef f84228f2 -HeadRef 39a7f70c`: 3 commits, 0 violations. The three new scripts are ASCII.
- The Unit lane was not run, because the brief forbids it. No checkpoint-tool receipt was produced. The plan has no `### Checkpoints` table, so no CP-n lines could be matched.

## Priority answers

1. **Compiled facade end to end works.** `test-checkpoint-compiled-facade.ps1` loads `Antiphon.Checkpoints.dll`, then the real `checkpoint-file-reader.ps1`, and stamps a small file and a 70 MiB file. The seal and ledger digest match. It passes with the embedded facade too. Red witness: a scratch copy whose seal calls `ConfinedFileReader]::ReadSealed` fails with `compiled facade missing ReadSealed`; the unmodified copy passes. `CheckpointLargeStampScriptTests` passed 3/3. `checkpoint-file-reader.ps1` is unchanged, so there is no availability regression.
2. **The second digest holds against the D2 cases.**
   - Red witness: removing the second-digest block on a scratch copy turns 9 assertions red: inode-reuse, appended, truncated and swapped-same-size are each refused with no ledger, and `inode-reuse-wrong-digest` shows the ledger kept the old digest. The `identity-preserved` assertion stayed green, so inode, size and mtime really were reused.
   - Change during the first hash: a 1 GiB file was overwritten in place 6 s into the seal, keeping inode, size and mtime. Result: `changed while sealing`, no ledger.
   - Change after the first hash: covered by the witness.
   - Residual (A1): see below.
   - Cost:

     | Input | Single pass | Two passes (HEAD) | Memory |
     |---|---|---|---|
     | Real 667 MB / 1,692-file Antiphon.Tests output (68.9 MB dll) | 11.1 s and 9.5 s | 12.5 s and 18.7 s | managed growth at most 23 MB; peak working set up about 37 MB |
     | 1 GiB file | 8.1 s and 7.2 s | 14.0 s and 18.5 s | managed growth about 5 MB |

     The cost is about 2x on large files. It is acceptable inside a build row. The stall window applies per chunk and per pass.
3. **Refusals are unchanged.** Hard link (`selected file has multiple links`) and unreadable (`errno=13`) are refused at both HEAD and base; HEAD now adds path, size and limit. The witness covers over-bound (limit+1), symlink, FIFO and growth past the bound. The 3 s stall is covered by C1532 reader-test parity. The `output-inventory-failed` prefix is kept, and `run-checkpoint.ps1:1194` only tests for null.
4. **Shard rows import correctly, but no concrete table exists.** I wrote a probe table to an ignored path, `.antiphon/r131/shard-plan.md`. It uses the documented pattern: a shared build, `(A*)\|(B*)` escaped pipes, and EstimatedMinutes 15. `Antiphon.Checkpoints.dll import --plan` imported 3 rows: the pipes were unescaped, the build was shared, and 15 minutes derived a 45 min timeout. A row at 16 minutes derived 48 min and produced the advisory WARNING. The doc names no classes and no per-row times, and shard runtimes are **unmeasured**. A Full Final receipt is therefore **claimed, not shown to be achievable**.
5. **Red witnesses are real** (see 1 and 2).

## Defect

**D-1 (PLAUSIBLE, test regression on Windows).** Code landing owner: 56ddd55e.
- Where: `scripts/test-checkpoint-output-inventory.ps1`. `Get-StatIdentity` calls `& stat -c '%d %i %s %Y'` from `Invoke-ReusedReplacement`, and `Assert-MutationRefused 'inode-reuse' 'reuse'` runs with no `$IsLinux` gate. Only the FIFO case is gated. `CreateSymbolicLink` also runs ungated. These are driven by `CheckpointFreshOutputSealTests.C1573_embedded_inventory_seals_past_64_mib_and_refuses_inode_reuse`, which is `[Category("Unit")]` and has no OS attribute.
- Failure: Windows pwsh has no `stat`. With PATH limited to pwsh, mkfifo and dotnet, the script exits 1 with `FAIL inode-reuse-mutated` and `FAIL inode-reuse-refused ... The term 'stat' is not recognized`. If a GNU `stat` is on PATH, NTFS file ids still make the eight-try inode-reuse loop unlikely to match, which also gives red. Symlink creation without the Windows privilege throws. I did not run it natively on Windows.
- Why: this adds a new red-by-construction Unit test to the Windows desktop and nightly Unit lane.
- Fix: gate the inode-reuse case, and the symlink case if it is unprivileged, on `$IsLinux`, as `test-checkpoint-large-stamp.ps1:204` does. Keep append, truncate and swap portable. Record a pending native Windows witness for the seal per the OS-branch rule.

## Advisories (D-2: no Review rerun solely for these)

- **A1 (docs).** `docs/testing-and-build.md`, Fresh-output inventory, says a change "after the ledger write" is outside the seal. The real window opens at each file's own second digest. A deterministic probe changed b.bin after its second digest while a.bin was being re-hashed. Result: `sealed`, and the ledger digest did not equal the bytes on disk. For the first file in a 1,692-file output, that window lasts the whole second pass (several seconds). This is no worse than base, but the documented bound is understated.
- **A2 (docs).** The Unit-lane section says "the seal above is what lets that build record buildSource=verified". The checkpoint tool, though, seals through C# `OwnedOutputLedger.Seal`: `tools/Antiphon.Checkpoints/Evidence/SourceRunGuard.cs:127` into `src/Antiphon.HostCleanup/Checkpoints/OwnedOutputLedger.cs:114`. That inventory is size-only (`file-size:`) and never had the 64 MiB limit. The PowerShell seal serves only `run-checkpoint.ps1`.
- **A3 (docs).** The partition rule takes only classes with a class-level Unit category. It drops method-level Unit tests such as `tests/Antiphon.Tests/Application/MatrixDefaultComplexityCreateTests.cs:130` (`[Test, Category("Unit")]` inside an Integration class). A shard set built by that rule would not equal `[Category=Unit]`.
- **A4 (performance).** The new test timeout is 180 s against a measured 4-6 s, about 30x the p95 instead of about 2x. There is no Performance budget plan.

Cleanup: deleted all 33 `bin-r131` directories, each realpath-confined to the worktree. Scratch copies are in the session scratchpad.
