# C1717 open-writer stale-tail Debug

The rc profile 1701 failure is an inherited test-fixture defect. The portable
reproduction, fix, truncation-removal mutant and restored run all produced their
expected results. No production files changed. Executed under the caller's
standing Debug authority, entirely on the Linux runner; no desktop access.

## Cause and invariant

`C1717_B3_WindowsOpenWriterRefusesTheDelete` creates `approved-bytes` (14 UTF-8
bytes), approves it, opens it with `FileMode.Open`, `FileAccess.Write`, and
`FileShare.ReadWrite | FileShare.Delete`, then calls the real no-follow delete.
After catching the refusal it writes `later-bytes` (11 bytes) at position zero.
`FileMode.Open` preserves length. Without truncation, the final three bytes are
still `tes`, giving exactly the reported `later-bytestes`. The reported final
assertion also means the preceding IOException and file-exists assertions passed.

The invariant is that an open writer makes Windows deletion refuse, and that the
writer can subsequently leave exactly its intended replacement content. The
fixture now calls `SetLength(0)` through the same held stream before the shorter
write, and checks whole-content equality instead of a suffix. Its sharing flags,
delete call, refusal assertion and file-exists assertion remain in place.

The new portable `C1717_B3_OpenWriterRewriteTruncatesEarlierBytes` first proves the
old sequence leaves the exact stale tail, then uses the same rewrite helper as
the Windows test and requires exactly `later-bytes`. Removing only `SetLength(0)`
makes that final assertion fail again.

## Production and history audit

Read `WorktreeNoFollowDelete`, `GuardedWorktreeRemoval`, `WorktreeSetAside`, and
`WorktreeRenames`. Windows hashing uses GENERIC_READ and OPEN_EXISTING, with
`denyWrite: true` excluding write sharing for the per-entry hash. An existing
writer therefore refuses that open. Deletion uses handle disposition metadata;
move-aside and restore rename the directory. Set-aside JSON writes are to the
administrative receipt under the common Git directory, not working-file content.
There is no held-file content rewrite in this path and no data-loss finding.
This failure is not evidence of a Windows sharing-mode or rename race: the
fixture's shorter write occurs after the delete returns, on its original handle.

Inspected `git log 1038d07e6..origin/master` for both production files and the test:

- CARD-1717 added fd-relative Linux deletion, content/index/identity binding,
  custody and per-directory checks, link handling, late repository markers,
  live authorization rereads and Git clean-form comparison.
- `d9dfe336d9b4a490589b3b27d61b0387fda66d90` introduced the defective Windows
  write/assertion sequence. `4fd7c0e46c7a0b6f2a07d9c3c29429a99028d50e`
  added Linux write-stability checks and Windows deny-write hashing, not file writes.
- CARD-1737 R1 `395e3aeebdd9858c893864b89bada5f7cedcccfe` added the separate
  Linux post-hash preservation witness and changed this class to Integration.
  Batch 94's `955145f83ab717e3467a06cd192401eb13618129` only added its
  `CancellationToken` parameter; it did not change the Windows fixture.
- CARD-1737 R2 `dcb973d896f1d12776a796b71466977b22737b79` adds salvage-source
  witnesses in `C1708ReflogSalvageTests`, policy and live-lane selection changes.
  Later snapshot, path, ref-status and teardown/join repairs likewise do not
  change this Windows write. Guarded removal's later salvage and status-verdict
  changes do not rewrite working-file bytes.

## Source and execution evidence

Base/master: `62ca0a9d7c6ce78282cbd6c4ad23951798a1e7f9`.
Red commit: `c4cefcd717b2f794925601ac0e1744c24f00f0a1`.
Fix and tested commit: `5542a2944cc0ee883f9a188d968903445daea25f`.
Both source commits were pushed to `feat/card-task-0a59a861` before their runs.

| Run | Passed | Failed | Skipped | Execution wall |
| --- | ---: | ---: | ---: | --- |
| Red exact portable method | 0 | 1 expected | 0 | 5.516 s |
| Fixed class plus requested guards, one batch | 85 | 0 | 1 Windows gate | 103.728 s |
| Remove-truncation mutant, exact portable method | 0 | 1 expected | 0 | 5.508 s |
| Restored fix, exact portable method | 1 | 0 | 0 | 4.399 s |

Both red assertions require `later-bytes` but observe `later-bytestes`; neither is
a fixture/build/discovery error. Red test body: 151 ms; mutant: 146 ms; fixed
portable witness in the batch: 36 ms. The Linux open-handle-during-hash witness
passed in 4.539 s, and the separate post-hash witness passed in 64 ms.

| Batch class | Passed | Skipped |
| --- | ---: | ---: |
| WorktreeNoFollowDeleteTests | 10 | 1 |
| TestClassificationPolicyTests | 39 | 0 |
| TestClassificationGuardTests | 1 | 0 |
| SlowTestRegistryMergeTests | 5 | 0 |
| SlowTestTripwireTests | 2 | 0 |
| NativeLinuxPolicyWitnessTests | 3 | 0 |
| MechanicalPcContractTests | 11 | 0 |
| TaskPlatformGuidanceTests | 5 | 0 |
| C1729LayeringGuardTests | 9 | 0 |

All eight build/test commands acquired separate build-slot leases immediately
(`waited=0s`, `maxcpucount=6`), then released them. Builds used native default
outputs and `-p:UseAppHost=false`; every run used `--no-build` and the same
property. Build elapsed times: red 197.83 s, fix 110.81 s, mutant 128.20 s,
restored 94.69 s. No test timeout was changed. Each run had the brief's bounded,
generous `--timeout 5m`; this is a task execution allowance, not a measured p95.
Phase journals were enabled; uninstrumented phases remain unknown.

The single batched selector was:

```text
/*/*/(WorktreeNoFollowDeleteTests*)|(TestClassificationPolicy*)|(TestClassificationGuard*)|(SlowTestRegistryMerge*)|(SlowTestTripwire*)|(NativeLinuxPolicyWitnessTests*)|(MechanicalPcContractTests*)|(TaskPlatformGuidanceTests*)|(C1729LayeringGuardTests*)/*
```

PC runs selected only:

```text
/*/*/WorktreeNoFollowDeleteTests/C1717_B3_OpenWriterRewriteTruncatesEarlierBytes
```

The transient mutant was restored with an exact-file `git checkout --`.
The file's Git object hash before and after was
`0642ca578e50cc954aab6a649c96ce022ef8b7f1`; the tracked tree was clean.
`check-evidence-diff.ps1` over base..fix reported 2 commits, 0 violations.
After fetching master, `git merge-tree --write-tree origin/master <fix>` exited
zero with tree `e64fbb729c0516e65133d3d715638011610a1145` and no conflicts.

Raw TRX, logs, source/binary digests, per-run receipts, phase journals and the
one-line mutant patch are retained in the ignored archive
`.antiphon/debug-0a59a861-evidence.tar.gz`. Build outputs and unpacked test results
are removed after archival. Generated evidence is not committed.

## Native Windows follow-up

The original test is Windows-gated and was explicitly skipped on Linux. Its
native refusal behavior is not claimed as executed here. After applying the fix
to the qualification checkout, the smallest remaining confirmation is one
native Windows build and that one method, through separate build-slot leases:

```powershell
pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1717-winwriter-build -- dotnet build tests/Antiphon.Tests -p:UseAppHost=false --nologo
pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1717-winwriter -- dotnet run --project tests/Antiphon.Tests --no-build -p:UseAppHost=false -- --treenode-filter '/*/*/WorktreeNoFollowDeleteTests/C1717_B3_WindowsOpenWriterRefusesTheDelete' --timeout 5m --report-trx --report-trx-filename c1717-winwriter.trx --results-directory .antiphon/c1717-winwriter
```

Expected: exactly 1 passed, 0 failed, 0 skipped. This Debug repair hands off to
land as requested for a test-side defect with portable red/green and mutant
evidence; it does not assert that rc Windows qualification is complete.
