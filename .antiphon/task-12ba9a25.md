# CARD-1677 S2 runner temp sweep: Review 12ba9a25 (round 1 of 3, full)

**Outcome: two defects found (D-1, D-2), so this is not landable yet.** Subject `feat/card-task-5f63ea92` at tip
`c9ab2497cd6034385b4d168261f529feff1df954` (that commit is the report only). Tested code:
`b15f66352a1f6f13f55251875ac5be28fc2f4af3`. Base: master `f32aed42e58a4f7aed9b012d39c4bf4ae90a24a0`, which is still
origin/master.

The deletion core is sound:
- descriptor-relative, no-follow delete;
- a symlinked entry is refused;
- an inner symlink is unlinked, never followed;
- the walk covers every descendant (nested-fresh entries are kept);
- socket and FIFO entries are refused fail-closed;
- DryRun never deletes;
- the temp sweep is off by default in the shipped JSON.

D-1 is a real over-match of every family regex. D-2 is a missing producer-to-recipient and OS-branch witness.

## Evidence

### Checkpoint run `20261103-172818-225a` at b15f66352 (verdict GREEN, exit 0, unlisted: none)

```
CHECKPOINT CP-7 commit=b15f66352a1f6f13f55251875ac5be28fc2f4af3 build=ok filter=/*/*/(C1677TempSweepTests*)|(C1677TempFamilyRegistryTests*)/* executed=27 passed=27 failed=0 skipped=0 trx=/work/worktrees/task-12ba9a25/.antiphon/checkpoints/20261103-172818-225a/rows/CP-7/run.trx slot=granted waited=15s dirty=0 source=b15f66352a1f6f13f55251875ac5be28fc2f4af3 sourceState=clean buildSource=verified
CHECKPOINT CP-8 commit=b15f66352a1f6f13f55251875ac5be28fc2f4af3 build=reused filter=/*/*/C1677TempSweepNativeTests/* executed=3 passed=3 failed=0 skipped=0 trx=/work/worktrees/task-12ba9a25/.antiphon/checkpoints/20261103-172818-225a/rows/CP-8/run.trx slot=granted waited=30s dirty=0 source=b15f66352a1f6f13f55251875ac5be28fc2f4af3 sourceState=clean buildSource=verified
CHECKPOINT CP-13 commit=b15f66352a1f6f13f55251875ac5be28fc2f4af3 build=reused filter=/*/Antiphon.TestSupport/NativeLinuxPolicyWitnessTests/* executed=3 passed=3 failed=0 skipped=0 trx=/work/worktrees/task-12ba9a25/.antiphon/checkpoints/20261103-172818-225a/rows/CP-13/run.trx slot=granted waited=15s dirty=0 source=b15f66352a1f6f13f55251875ac5be28fc2f4af3 sourceState=clean buildSource=verified
CHECKPOINT CP-9 commit=b15f66352a1f6f13f55251875ac5be28fc2f4af3 build=ok filter=/*/*/C1677TempSweepServerTests/* executed=3 passed=3 failed=0 skipped=0 trx=/work/worktrees/task-12ba9a25/.antiphon/checkpoints/20261103-172818-225a/rows/CP-9/run.trx slot=granted waited=15s dirty=0 source=b15f66352a1f6f13f55251875ac5be28fc2f4af3 sourceState=clean buildSource=verified
```

Every count meets the test design's Min floor: 27, 3, 3 and 3. These row numbers come from the test design
(`docs/superpowers/plans/2026-11-03-card-1677-test-design.md` `### Checkpoints`), which replaces the plan's rows.

### Unlisted runs

Each ran in a detached scratch worktree at b15f66352, with one build-slot lease per build or run and native output
with `UseAppHost=false`:
- 1 build of SessionRunner.Tests;
- 3 runs, listed in the probe and mutant tables below.

The scratch worktree, the mutations and the probe file were all removed afterwards. Both trees are clean.

### Throwaway probes on UNMUTATED code (4 cases, file never committed)

| Probe | Result | Meaning |
|---|---|---|
| `c1514-<hex>\n` (trailing newline), 49 h old, owned, no holder | **RED: the entry was deleted** | D-1 |
| FIFO inside an old entry | green: kept `unproven_content` | fail-closed confirmed |
| Live unix-socket listener (child pwsh) inside an old `antiphon-c1653-sock-*` entry | green: kept `unproven_content` (`non-regular s`). The real `/proc` scan reported `complete=True holders=0` | The scan does NOT see a socket holder: `/proc/<pid>/fd` shows `socket:[inode]`. The socket inode itself keeps the entry. |
| Fresh directory of `File.Copy`'d files with old mtimes (Copy and Copy-Item keep mtime on Linux) | RED: `removed` | A-1 (plan-faithful) |

### Mutants (env-gated, same build)

| Mutant | Result |
|---|---|
| Age check removed (`newest > cutoff` never keeps) | **killed**: `Newest_file_write_decides_age(new-top-file)` and `(new-nested-file)`, 2 of 27 |
| `DeleteAsync` uses plain `Directory.Delete(path, true)` instead of the no-follow primitive | **survived**: 27 of 27 suite cases green (the 2 extra reds were the known-red probes above) |

### Other checks

- **check-evidence-diff.** `pwsh -NoProfile -File scripts/check-evidence-diff.ps1 -Repository . -BaseRef f32aed42e -HeadRef c9ab2497c` reported `commits=3 entries=1 violations=0`.
- **merge-tree.** `git merge-tree --write-tree origin/master <b15f66352|c9ab2497c>` is clean (origin/master is f32aed42e).
- **Live-lane pin.** `pwsh -NoProfile -File scripts/test-live-lane-run.ps1 -PrintInputDigest` printed
  `a3025fcdf6912bca2ba75b6262b38de3ce66cf3e47db604137962fc17c1687e7`, which equals the committed pin.
- **Policy diff.** The change is only the new `C1677TempSweepNativeTests` windows/Linux-only row plus `policyHash`.
  `scripts/test-rc-platform.ps1` adds the class to the roster and to `documentClasses` only. CP-13 is green. The
  re-records are honest.

## Defects

### D-1: every family regex admits a trailing newline (`$` is not end-of-string in .NET)

- **Where.**
  - `src/Antiphon.SessionRunner/RunnerTempSweep.cs:60-96`: all ten patterns end in `$`, and `TempFamily.Regex`
    uses no option that changes that.
  - The witness `tests/Antiphon.SessionRunner.Tests/C1677TempFamilyRegistryTests.cs:67`
    (`Family_regexes_are_anchored_and_exact`) asserts that each pattern ends with `$` and tests only `Sample+"x"`
    and `"x"+Sample`.
- **Failure.** In .NET, `$` without `Multiline` also matches before a final `\n`. A runner-owned
  `/tmp/c1514-<32hex>\n` (or any family name plus `\n`) that is 48 h old and has no holder is classed as the family
  and **deleted**. The probe above did this on unmutated b15f66352. Under `claude-scratch`,
  `claude-1654/-work-worktrees-task-0123abcd\n` matches too. Its `name[^8..]` then probes the mirror
  `/work/worktrees/task-123abcd\n`, so a LIVE mirror's scratch reads as "mirror absent".
- **Why it matters.** The invariant is "only a registered name is a candidate" (plan §3.5, I-13). That name is not
  produced by any registered producer. Exposure is limited to runner-uid-owned entries, because the owner walk
  still applies. Even so, it is a family over-match in a file-deleting engine, and the witness that claims to pin
  "anchored and exact" cannot go red for it.
- **Fix.**
  - Anchor with `\z` (for example `^...\z`; keep `^`), or match with `RegexOptions.NonBacktracking`. `$` would
    still need `\z`.
  - Make V-48c assert `Pattern.EndsWith(@"\z")` and `!Regex.IsMatch(Sample + "\n")` for every family, and
    `!IsMatch(Sample + "\r")` too.
  - Add a portable sweep case: an entry named `<family-sample>\n` stays as `unregistered`, intact.
  - Optionally refuse any name that contains a control character before family matching.

### D-2: no producer-to-recipient (runner-side op 47) or production-composition witness

- **Where.**
  - `src/Antiphon.SessionRunner/PhoneHomeCommandDispatcher.cs` (`TempSweepAsync`: the tempSweepV1 gate and payload
    deserialization, outside `MutationLock`).
  - `PhoneHomeRuntimeAdapter.TempSweepAsync`, `TempSweepFeatures(isLinux)` and the capability list.
  - `Program.cs` (constructed only on a Linux phone-home runner).
  - `RunnerTempSweep.CreateDefault` (`/tmp`, `TempSweepNative.LstatUid` with hand-laid statx offsets,
    `EffectiveUid`).
  - Server side: `RunnerWorktreeRetentionService.SweepTempAsync` branches.
- **Failure.**
  - On the runner, no test sends a TempSweep frame through the dispatcher. `grep PhoneHomeOperation.TempSweep|TempSweepV1|TempSweepFeatures tests/`
    finds only the server test, which uses a scripted peer.
  - None of these break any test:
    - removing the dispatcher's capability gate;
    - routing op 47 under the mutation lock;
    - advertising `tempSweepV1` on Windows;
    - constructing the sweep off Linux;
    - a wrong statx uid offset in the production owner seam. Every sweep test injects `Owner = _ => Uid`, so the
      real `LstatUid` never runs.
  - The server branches that the brief names as safety switches are untested:
    - "runner lacks tempSweepV1, so nothing is sent";
    - "preview route sends DryRun even with `TempSweep:Execute=true`";
    - "`Execute=true` sends Execute";
    - "`RunnerWorktreeRetention:Enabled=false` stops the temp sweep".

    The server suite has only `Disabled_sweep_sends_nothing`, the DryRun default and the claude-scratch default.
  - Nothing in the report shows `scripts/check-os-witnesses.ps1` was run for the Linux-only branches
    (docs/testing-and-build.md#os-branch-witnesses).
- **Why it matters.** These are the gates that keep a delete frame from the wrong runner or OS. The review contract
  rejects a missing producer-to-recipient test. By reading, the code is right today, and the statx offsets 20 (uid)
  and 28 (mode) match statx(2). Nothing would turn red if any of it regressed.
- **Fix.**
  - Add a dispatcher test, `C1677TempSweepDispatchTests`, in the style of `C1677RetentionDispatchTests`:
    - a surface without tempSweepV1 gives 501 and the engine is never reached;
    - with the feature, the frame's Families and Mode reach the sweep intact;
    - the op does not wait on a held `MutationLock`;
    - `TempSweepFeatures(false)` is empty.
  - Add a Linux-native case that drives `RunnerTempSweep.CreateDefault`'s real owner seam: the uid of a real
    temp file equals `EffectiveUid()`, and a link's own uid is read (bind it through the test root, not `/tmp`).
  - Add server cases for no-capability, preview with Execute=true (DryRun), Execute=true (Execute) and
    retention Enabled=false (no frame).
  - Record and run the check-os-witnesses declaration.

## Advisories (no Review rerun needed solely for these)

- **A-1, age by regular-file mtime only (plan-faithful).**
  - A freshly made entry of copied files keeps the copies' old mtimes. `File.Copy` and `Copy-Item` preserve mtime
    on Linux (probe above), so such an entry is eligible at once if no process holds it at the scan.
  - No current producer does this. The c487 harness writes fresh `.git/config` and `HEAD`, and `TranscriptAdoption`
    sets mtimes only minutes back.
  - Taking `max(mtime, ctime)` over every entry, directories included, would close it. ctime cannot be set and is
    refreshed by copy and create.
- **A-2, the plain-`Directory.Delete` mutant is not truly equivalent.**
  - It survives only because no test changes the tree between the walk and the delete. It is killable
    deterministically by:
    - a sweep seam that runs between `InventoryAsync` and `DeleteOwnedAsync` and swaps an inner directory for a
      symlink to an outside canary. The primitive then refuses `changed_since_inventory`, kept, while the mutant
      reports `removed`. Assert the verdict, not only the canary;
    - the primitive's 256 MiB per-file limit: one sparse 257 MiB file is kept `unproven_content` by the primitive
      and removed by the mutant.
  - PC-100 cannot be discharged by the current witnesses. Mutation should know this.
- **A-3, the walk's facts are not bound to the deleted object.**
  - `Identity()` is taken at delete time, not at walk time.
  - The age and owner checks are not re-run on the delete-time inventory. A file written, or a foreign file added,
    between the walk and the delete is removed with the entry.
  - This is narrow: the names are 128-bit random, the holder rescan runs right before, and `/tmp` is sticky. Carrying
    the walk-time dev:ino and re-checking the newest mtime on the inventory would close it.
- **A-4, holder definition.** A holder is `/proc/<pid>/cwd` or an `/proc/<pid>/fd/*` link under the path, for the
  runner's own uid. Not covered:
  - `exe`, `root`, `maps` (mmap with the fd closed) and argv;
  - socket listeners (`socket:[inode]`). These are covered instead by the socket inode refusal.

  A completed scan that skips unreadable `/proc/<pid>/fd` still says Complete. That is acceptable only because
  every entry must be runner-uid-owned.
- **A-5, Execute is not re-read at the send.** `SweepTempAsync` re-reads `Enabled`, the kill switch and the budget
  before `DescribeAsync`, but takes `Execute` from the snapshot at the top of the method. An Execute→false flip
  during that await still sends Execute. Re-read `TempSweep` right before building the request.
- **A-6, the delete reads every byte three times.** Inventory, validate and delete each hash every file, so 20 GiB
  per tick means about 60 GiB of reads. Each inventory also has a 30 s deadline, so a large slow entry refuses
  `unproven_content`. Measure on server2 before `Execute=true` (plan §8).
- **A-7, card wording.**
  - The CARD-1677 operator decision ("Enable this on by default") lists retention parameters only: runner list,
    12 h, DryRun=false.
  - The plan's D-15, `server/appsettings.json` (`TempSweep.Enabled=false`, `Execute=false`, `claude-scratch=false`)
    and the JSON-reading witness `C1677RetentionSettingsTests` all keep the temp sweep off, which is consistent.
  - If the operator meant both, that is their decision.

## Merge order with CARD-1686 (`feat/card-task-9e9cf510` tip 4282088c9, review 43f316b5)

`git merge-tree --write-tree 4282088c9 b15f66352` **conflicts** in three files:
- `scripts/test-live-lane-run.ps1`: the pin, 22fb43e1... against a3025fcd...;
- `scripts/test-rc-platform.ps1`: CARD-1686 deletes the hand roster and derives the census from the policy;
- `tests/test-execution-policy.json`: `policyHash`, and CARD-1686 also changes `selectionCatalog.hash`.

Either may land first. The second must be re-cut by the repo's procedure, without hand-merging hashes:
1. Drop its `test-rc-platform.ps1` roster hunk if CARD-1686 is in, because the census is then derived.
2. Keep its own policy row.
3. Reseal `policyHash` from the `NativeLinuxPolicyWitnessTests` output and, if the catalog covers it, the selection
   catalog hash.
4. Re-record `$script:PinnedInputDigest` with `pwsh -NoProfile -File scripts/test-live-lane-run.ps1 -PrintInputDigest`.
5. Re-run CP-13 and CP-4 (or CARD-1686's census rows).

I resolved nothing.

## Not run (scope)

The verification profile says Final, but the brief scoped this round to CP-7, CP-8, CP-9 and CP-13 plus two
mutants, in 75 minutes. Not run:
- the whole Unit lane (about 3 h on server2);
- S1 rows CP-1..CP-6 and CP-12;
- S3 rows CP-10 and CP-11.

So `ordinaryScopeCompleted` is Interim. Nothing ran on the desktop. All PCs (PC-85..PC-113, PC-115) stay pending for
SourceLanding Mutation.
