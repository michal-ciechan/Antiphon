# rc/2026-10-20 backport: CARD-1396 reader and CARD-1400 docs-input audit

Date: 2026-10-21. Code task 0f30ca55, branch `feat/card-task-0f30ca55`, start ref `806b23084fa91daaa61ff0ff1957f39c5fe76baa` (rc/2026-10-20 tip). Code writes this plan because the brief requires one checkpoint row per item. No Unit lane: the brief forbids a whole-Unit run. Next stage after the green rows: review.

## Scope

1. **CARD-1396 reader only.** The `Read-NightlyLockRecord` hunk of master `8683447c6` (open with `FileAccess.Read` and `FileShare.ReadWrite`, dispose in `finally`). The body on rc equals master's pre-fix body, so the hunk is applied byte for byte. Not picked: the `reclaim-shared-reopen-failed` log in `Enter-NightlyReclaimedLock` and the `Open-NightlySharedLockStream` seam (master `b8a10eb2d`), which depend on CARD-1389; the `Resolve-NightlyStaleLock` comment that names CARD-1389 D-2; the `docs/testing-and-build.md` note that describes both.
2. **CARD-1400** master `8856d4cb1`, `0fde552f4`, `cfcb9955d`, `83cd564b4`, cherry-picked with `-x`. Patch-id equality against master is checked per commit.

## Adapted test

Master's `Test-C1396_WriteHeldLockReadable` uses CARD-1389 helpers rc lacks (`Reset-C1389HeldLock`, `Wait-C1389File`, `Read-C1389JsonFile`). The rc case drops the reset (it never enters the lock), waits on the child with `WaitForExit`, and reads the result file directly. Linux allows `FileShare.Read` against a `FileAccess.Write` + `FileShare.Read` holder, so the two reads cannot go red on this host (master saw the same). The case therefore adds one AST row: the `[System.IO.File]::Open` call inside `Read-NightlyLockRecord` passes `[System.IO.FileShare]::ReadWrite` and the function calls no `File.ReadAll*`. That row is the Linux red; the in-process and second-process reads are the Windows proof. `NightlyDeadOwnerLockTests.C1396_WriteHeldLockReadable` runs the case with a four-row inventory.

Unlisted red run (S1, before the reader hunk): `pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1396-red -- pwsh -NoProfile -File scripts/test-nightly-run.ps1 -Case C1396_WriteHeldLockReadable`, lease `47569174-dff1-4241-8cb5-9964dc74100b`, waited=30s, held=2s. 3 passed, 1 failed: `C1396 Reader opens the lock with FileShare.ReadWrite - readAll=True share=`. Harness exit 1.

## Slices

- **S1** test: `scripts/test-nightly-run.ps1` case, `NightlyDeadOwnerLockTests` wrapper, this plan.
- **S2** reader hunk in `scripts/lib/nightly-common.ps1`.
- **S3** the four CARD-1400 cherry-picks.

## Verification design

| ID | Behaviour | Witness |
|---|---|---|
| V-1 | `Read-NightlyLockRecord` reads a run.lock held with `FileAccess.Write` + `FileShare.Read`, in-process and from a second process; a `FileShare.None` open still refuses; the reader opens `FileShare.ReadWrite`. | `NightlyDeadOwnerLockTests.C1396_WriteHeldLockReadable` |
| V-2 | CARD-1400 V-1..V-11 and the CARD-1390 refusal hold on rc; `-Check` matches the committed manifest. | `LandNonCodeSkipTests` `C1400_*`, `C1390_*`, `C1385_RegistryMatchesNarrowGraph` |
| V-3 | `audit-land-verification-graph.ps1 -Check` prints digest `F240D0F1DBF9AA627A0A3637B52318B8030181B60F20E677C6E3DE2D2285B72F` count 57. | manual command |

No R-n. No new positive controls. Existing PCs stay pending for SourceLanding Mutation.

### Checkpoints

The checkpoint tool adds `UseAppHost=false` on Linux. One build; CP-2 reuses it.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S3 | `tests/Antiphon.Tests -> bin-rcbp/` | c1396-reader | `/*/*/NightlyDeadOwnerLockTests/C1396_WriteHeldLockReadable` | V-1 | all listed, 0 failed/skipped | 1 | 15 |
| CP-2 | S3 | CP-1 | c1400-docs | `/*/*/LandNonCodeSkipTests/(C1400_*)\|(C1390_*)\|(C1385_RegistryMatchesNarrowGraph*)` | V-2 | all listed, 0 failed/skipped | 12 | 5 |
