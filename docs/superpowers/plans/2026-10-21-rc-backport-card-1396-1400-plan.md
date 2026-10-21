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

## Evidence

S3 cherry-picks applied with no conflicts. Patch-id (`git patch-id --stable`) equals master for each: `8856d4cb1`->`ab68fa6e05f0ee1a918587d958e32b072005604a` (29c112e5), `0fde552f4`->`6916cd8c11efbe382cf6410da77a252cbe966565` (1b9acac4), `cfcb9955d`->`c81cc3e6bd3ddafc3a7bb78eea671d05e32fee0e` (2c0d8095), `83cd564b4`->`795ee168e52f7de8bd325e8a93087dd11d10dbb7` (9944f5ed). The S2 `Read-NightlyLockRecord` body equals master's byte for byte.

V-3: `pwsh -NoProfile -File scripts/audit-land-verification-graph.ps1 -Check` at `795ee168e` printed `digest=F240D0F1DBF9AA627A0A3637B52318B8030181B60F20E677C6E3DE2D2285B72F count=57`.

Checkpoint run `20261021-205200-4860` at `795ee168e52f7de8bd325e8a93087dd11d10dbb7`, verdict GREEN exit=0, one build, unlisted none:

```
CHECKPOINT CP-1 commit=795ee168e52f7de8bd325e8a93087dd11d10dbb7 build=ok filter=/*/*/NightlyDeadOwnerLockTests/C1396_WriteHeldLockReadable executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-0f30ca55/.antiphon/checkpoints/20261021-205200-4860/rows/CP-1/run.trx slot=granted waited=45s dirty=0 source=795ee168e52f7de8bd325e8a93087dd11d10dbb7 sourceState=clean buildSource=verified
CHECKPOINT CP-2 commit=795ee168e52f7de8bd325e8a93087dd11d10dbb7 build=reused filter=/*/*/LandNonCodeSkipTests/(C1400_*)|(C1390_*)|(C1385_RegistryMatchesNarrowGraph*) executed=14 passed=14 failed=0 skipped=0 trx=/work/worktrees/task-0f30ca55/.antiphon/checkpoints/20261021-205200-4860/rows/CP-2/run.trx slot=granted waited=60s dirty=0 source=795ee168e52f7de8bd325e8a93087dd11d10dbb7 sourceState=clean buildSource=verified
```

CP-2 roster: 11 `C1400_*`, `C1390_UpdateRefusesDocsMarkdownInput`, `C1390_MissingNulFailsUnderNonInvariantCulture`, `C1390_MalformedManifestForcesTheBuild`, `C1385_RegistryMatchesNarrowGraph`.
