# CARD-1930: the Linux missing-tree fault cut could not delete the root-owned cgroup

Date: 2026-11-25. Debug task 728017e0 on server2 (runner mirror, uid 1654, cgroup v1, kernel 4.15).
Branch `feat/card-task-728017e0`, base `bdca854ff5ecb92400f45c453b751211f6e192c4`.
The change touches tests only. No production source, helper, sudoers rule or host permission changed.

## Symptom

`LinuxCgroupCustodyTests.C1614_Missing_tree_never_finalizes_naturally` failed before it observed anything:

```
IOException: Access to the path '/sys/fs/cgroup/pids/antiphon-custody/<ns>/<execution>/tree' is denied.
   at System.IO.Directory.Delete(String path, Boolean recursive)
   at ...LinuxCgroupCustodyTests...C1614_Missing_tree_never_finalizes_naturally>b__0()
```

I reproduced this at base: 1 executed, 1 failed, with the same exception.

## Cause

The fault hook (`HostCustodyJournal.BeforeOrdinaryAccountingForTests`) called
`Directory.Delete(tree, recursive: false)` on the execution cgroup. That cgroup is created by
the root-owned `antiphon-custody-enter` shim under a root-owned 0755 parent. The test process
runs as uid 1654 and cannot remove it. Root can reach the tree only through the two allow-listed
sudo helpers, and neither one removes an execution tree. `antiphon-custody-kill` runs `remove_tree`
only for the probe GUID. CARD-1614 D-10 intends this: "Retain empty execution cgroups through
receipt creation; only probe trees may be removed immediately by helpers."

The plan assumed an isolated candidate's root provisioning fixture would do the rmdir (CARD-1614
plan, NP-34). The standing runner lane has no such fixture. As a result the method could never
reach its assertions on that lane.

## Fix (fixture-owned fault)

The test now installs a fixture-owned `MissingTreeContainment` through the existing
`HostSession.UseContainmentForTests` seam:

- It wraps a real `LinuxCgroupContainment` over the host's own container id, which it reads from
  `host-identity.json`. The journal writes that file before the runner places the child.
  Placement through the real shim, the real tracked tree, and kill through the real helper all
  stay unchanged.
- Once `tracking.json` exists, the existing hook arms the fault at the first ordinary post-exit
  accounting read. From then on, each accounting read uses a real `LinuxCgroupContainment` over a
  new, never-created execution id in the same live namespace. That means the real production
  reader (`LinuxCustodyOperations.Real`) reads a kernel path that genuinely does not exist. No
  population or exception is fabricated.

New assertions:

- The armed read ran at least once.
- The real reader failed closed on the absent tree: at least one IOException, and no population
  returned.
- The absent tree does not exist.
- The real tracked tree is retained.
- State is `Unknown` with no receipt, no termination call, no `producer-receipt.json`, no
  `linux-release-disposition.json`, and no automatic host exit.

## Discrimination at this base

I made a scratch mutation that restores missing-as-zero: `if (!File.Exists(path)) return [];` at
the top of `LinuxCustodyOperations.ReadTreePids`. With only the first fix commit, the mutant stayed
green. At this base the host never writes `linux-placement.json`, because that is the CARD-2060
placement-race defect. So `CanNaturalFinalize()` is false, and a fabricated zero also ends
`Unknown` with the same reason, `observation_failed`. The second commit adds the
reader-fails-closed counters. With that commit the mutant is red
(`containment.MissingPopulations should be 0`), and the restored source is green. Once CARD-2060
records placement, the state assertions also become discriminating on their own.

## Evidence (all through `scripts/build-slot.ps1`, `-p:UseAppHost=false`)

| Run | Source | Result |
|---|---|---|
| red | base `bdca854ff` | 1 executed, 1 failed (IOException access denied) |
| green | `9499a583` | 1 passed |
| probe | `9499a583` + missing-as-zero mutant | 1 passed (non-discriminating, see above) |
| probe | `09459f4f` + missing-as-zero mutant | 1 failed on `MissingPopulations` |
| green | `09459f4f` (mutant restored) | 1 passed, 0 skipped |

Filter: `/*/*/LinuxCgroupCustodyTests*/C1614_Missing_tree_never_finalizes_naturally*`.

## Not changed / related

- CARD-2060 (`feat/card-task-236ba4ee`, Code ce9a68f6) owns the placement handshake. This task
  did not touch it.
- CARD-2067 (runner host start-time equality) and CARD-2068 (SessionRunnerRuntimeTests
  cmd.exe on Linux) are different defects and are not duplicates of this one.
- By design (D-10), every native run leaves an empty execution tree in the live namespace. The
  server2 namespace `916c3fdf-...` held 104 entries after these runs. This is existing residue
  behaviour and not in scope here.
