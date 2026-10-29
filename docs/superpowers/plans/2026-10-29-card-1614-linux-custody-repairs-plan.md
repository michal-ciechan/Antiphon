# CARD-1614: Linux custody and PtyHost test repairs

Status: skeleton published for incremental completion; not implementation authority yet.
Base: `3a003152840f07911e027747ee15e4da58538cc7`. Scope: a written repair plan only.
Evidence: [full investigation](../../investigations/2026-10-29-ptyhost-linux-reds-classification.md).

## Decisions

- D-1: Keep the three confirmed product defects distinct from fixture/build defects; a Linux test exclusion cannot repair custody.
- D-2: Root exit with live descendants keeps custody open. Observation must not initiate termination. Explicit release remains available; no new automatic kill or stall deadline.
- D-3: Complete the six missing baseline reruns before implementation. Preserve expected reds as baseline evidence, never as a passing gate.
- D-4: The finished artifact goes to independent plan review by a different company's model before implementation.

## Ground truth

| Card assumption | Observed code/evidence | Planning consequence |
|---|---|---|
| Ten unexplained Linux failures | Investigation distinguishes artifacts, fixture races, identity, helper expansion and automatic sealing | Repair each cause with a production-path witness |
| cgroup v2 required | Shipped helpers support v1; observed host has v1 pids/freezer | Native v1 qualification required; native v2 remains separate evidence |
| Launcher failures imply broken production payload | Test output lacks apphost; observed live payload has it | Fix staging and readiness tests; do not infer deployed launch failure |
| Custody defects affect current production sessions | Live capability probe advertised no custody backend | Blast radius is source reachability until activation/session evidence exists |

## Product repair design

Pending source-confirmed roots, minimal changes and red-first witnesses for PID identity, helper expansion, and root-exit observation.

## Test and build repair design

Pending portable inbox fixture, daemon/launcher readiness and once-per-build native apphost staging.

## Implementation slices

Pending exact files and verification mapping. No product source changes are authorized by this Plan dispatch.

## Performance budget

Apply [performance standards](../../testing-and-build.md#performance-standards).
Investigation samples are small, not measured p95: isolated tracked PID body/teardown 120.199 s; historical seal cases about 300 s each; native launcher runs 0.872–1.780 s except the readiness failure. Build slot waits reached 435 s and are separate from execution. Final section will specify phase recording, shared setup, bounded collection, and approximately 2x measured-p95 deadlines; no fabricated p95.

## Verification design

The first six checkpoint rows will be the missing exact baseline methods, followed by red-first witnesses and targeted green qualification. Native Linux and Windows lanes will remain explicit. Pending importer validation.

## Safe residue cleanup and release

Plan only: do not delete the three leftover cgroup trees in this dispatch. Final procedure will require exact literal paths, verified ownership and fresh empty membership before nonrecursive removal. Product fixes require rc and master assessment; no tags, publish, deployment or rc rebase here.
