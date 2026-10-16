# server2 cache Fixture permission failure: 2026-10-16

Outcome: the mandatory cache Fixture gate is blocked by a systematic mixed-UID
seed-validation defect in the requested RC. The investigation is complete; no
cleanup, retry, source repair, deployment, drain or restart was performed.

Task: `4f91a4f2` (Debug). Report branch: `feat/card-task-4f91a4f2`.
Investigated source: `b7265a05aa8773f08dbf457b93c7561e7874822d` in the canonical
checkout `C:\src\Antiphon`. Its `scripts/c590-remote.sh` Git blob is
`716a990e44b9087c895949868bc88618a31e4c28`; `git hash-object` of the canonical file
matched that committed blob. The task branch's starting HEAD was
`6e23e0b4c8d6b2ae12218c2c0058593dbf773530`; operational source was read from the
canonical RC, not inferred from that older branch.

## Finding and causal chain

The failing pathname is:

```text
/fixture/cache/stage-c8490fb677fc8c7642420-37NLzwfN/npm-cache/_logs/2026-10-16T11_10_17_996Z-debug-0.log
```

This is a path inside the run-owned Docker volume
`c849c8490fb677fc8c7642420-work`, mounted at `/fixture`. It is neither the standing
runner's npm cache nor an older host staging directory.

The RC's `scripts/c590-remote.sh` establishes the following sequence (line
numbers refer to the investigated RC):

| Location | Behavior and consequence |
|---|---|
| `c849_fixture_workspace_create_volume`, lines 3448-3482 | Requires absence before creating the run-specific work volume. An older same-name volume refuses acquisition. |
| `c849_fixture_workspace_init_root`, line 3484; `c849_fixture_helper_acquire`, line 3492 | Initialize the work area for UID/GID 1654 and create writer/reader helpers with `--user 1654:1654`; writer mounts work read/write. |
| `c849_seed`, lines 2285-2287 | Writer creates a fresh `mktemp` stage and its packages/npm directories. This is a new random stage for this invocation. |
| Lines 2299-2302 and 2317-2320 | Copy only the donor's package content and npm `_cacache` into the stage. Writer creates `npm-cache` and its `_cacache -> ../npm` symlink. Donor `_logs` is not copied here. |
| Lines 2322-2325 | Start an ephemeral container with `--user 0:0 --entrypoint npm`, mounting the work volume, and execute `cache verify --cache <stage>/npm-cache`. This is the process that creates the new npm `_logs` tree and timestamped debug log. |
| Line 2326 | Ask `c849_payload_fs writer` to remove `npm-cache`. That resolves through `c849_fixture_helper_exec` to the UID/GID 1654 writer. It cannot unlink entries from npm's root-owned log directory. |
| `c849_seed_failure`, lines 1793-1802 | Attempt stage removal through the same writer, explaining the second permission-denied message, then report `NpmStagedIntegrityFailed`. |
| `c849_fixture_seed`, lines 3839-3844 | Wrap the failed seed subprocess as `FixtureSeedFailed`, the retained case verdict. |

The causal ownership attribution is **root, UID/GID 0:0, for the npm-created
log tree**, versus **UID/GID 1654:1654 for its cleanup process**. These identities
come from the exact source launch arguments and the observed failing path. The
original inode owner/mode and npm container ID/PID were not retained and cannot
now be measured: finalization removed the entire work volume and helpers. No
claim is made that this investigation obtained an inode `stat` of the vanished
log. File deletion depends on write access to its parent directory; changing
only the log file's mode would not address the mixed ownership of `_logs`.

The log timestamp, `11:10:17.996Z`, falls within the failed Fixture's
`11:09:05-11:10:30` execution window. The fresh namespace/stage and content-only
copy exclude the proposed explanation of an older fixture/session log left in
this path. A new run would recreate the same ownership mismatch. This is an
inherited RC operational-gate defect, not a change introduced by this Debug task.

## Evidence and current state

The upstream Deploy report was read at exactly
`213d802b1920864751bc8facf8727b5c2272f91d` from
`origin/feat/card-task-feb1f83b`, path
`docs/investigations/2026-10-16-rc-activation.md`.
It records the failed canonical command:

```powershell
pwsh -NoProfile -File scripts/verify-card0849-caches.ps1 -Case Fixture -Sha b7265a05aa8773f08dbf457b93c7561e7874822d
```

Retained evidence:

- Local wrapper: `C:\src\Antiphon\.antiphon\c849-c8490fb677fc8c764242\0\c590-result.json`.
- Local case: `C:\src\Antiphon\.antiphon\c849-c8490fb677fc8c764242\0\runner-cache-fixture\`.
- Remote case: `/work/test-evidence/c8490fb677fc8c7642420/runner-cache-fixture/`.
- This investigation's ignored read-only observation:
  `C:\Antiphon\worktrees\card-task-4f91a4f2\.antiphon\fixture-permission-debug\readonly-host-observation.txt`.

Both original result files say
`{"accepted":false,"diagnosis":"FixtureSeedFailed","exit":2}`; the front door
returned exit 1. Remote receipts independently counted **2 completed groups,
15 expected-red control families, and 19 expected-red variants**, compared with
the required 9 groups, 32 families, 47 variants and 2 inventories. No complete
Fixture summary exists. These partial results do not qualify Fixture.
`payload-entries.txt` records `seed volume /fixture/cache`. The writer fingerprint
records creation at `2026-10-16T11:09:10.149681698Z` and image
`sha256:ea8bafe24373694b3421d63afdffb4533faf0710f709d96dea661123b55e6835`.

At **11:16:26 UTC**, an awaited, read-only SSH observation (exit 0) used successful
full `docker volume ls -q` and `docker ps -aq --no-trunc` censuses to compare the
recorded exact identities. All eight registered fixture volumes and the recorded
donor, writer and reader containers were absent. The controller directory was
absent; cleanup residue and ledger files were both zero bytes. Thus there is no
surviving fixture log to delete. No root cleanup or ownership change is justified.
An earlier direct inspection also returned Docker's explicit `no such volume` for
the exact work volume and `No such object` for the writer.

Main remained running with the same identity/start time as the Deploy report:

- Container: `fc2983ea4991fe85b31f54ee2ef66150e0d8fef03897d6cf1c2d6ca9c0889832`.
- Started: `2026-10-11T13:42:57.09302366Z`.
- Image: `sha256:ea8bafe24373694b3421d63afdffb4533faf0710f709d96dea661123b55e6835`.
- Image revision: `7507b3cfe0e2f7660a0c9f9f8eb19dc89a937bce`.

This is not RC activation on server2. No fresh assertion about its admission
counters is inferred from Docker's running state. Canonical HEAD remained the
requested RC, with only the original two untracked patch files; neither was
modified. No AppHost, desktop-runner or standing server2 state was changed.

## Proposed repair and handoff

Commission a separate Code task for `scripts/c590-remote.sh:c849_seed`:

1. In the volume-backend branch, run `npm cache verify --cache "$seed_path"`
   through the already acquired `c849_fixture_helper_exec writer`. That helper
   runs as UID/GID 1654, has the same image and a read/write work-volume mount,
   and is already isolated with `--network none`. Keep its exit checked and
   mapped to `NpmStagedIntegrityFailed`. Retain the existing host-backend
   isolated verification path. An equivalent explicit UID-1654 verifier may
   be evaluated in Code, but do not solve this by escalating cleanup or changing
   live cache ownership.
2. Keep verification followed by checked writer cleanup. Add a focused regression
   that exercises actual npm log creation and subsequent deletion as UID 1654
   on disposable storage, including failure cleanup and absence of an accepted
   seed marker on failure. It must fail on this source. Existing modeled volume
   and seed tests are useful accompanying checks but are not native UID evidence.
3. Complete ordinary Code/Review/land and explicitly select the repaired release
   SHA. Qualify native Fixture on that approved SHA, retaining the complete
   9-group/32-family/47-variant/2-inventory receipt and cleanup evidence. The
   current pinned RC must not be patched in place or silently replaced.

The brief explicitly requires stopping for a systematic defect. Consequently,
this task ran **0 new Fixture attempts, 0 tests and 0 builds**. The original
Fixture failure remains unresolved; there is no new passing gate. No alternate build outputs were created; no child command remains running.

After repair, caller-owned deployment still owes Fixture, Inventory, appropriate
Seed validation, and every staged rollout phase (deploy-temp, smoke/canary,
drain-old, redeploy-old, main smoke/Both, drain-temp, retire-temp/Retired), with
fresh preconditions. Nothing in this report authorizes bypassing Fixture or
changing the target RC without explicit release selection.

--- next stage ---
next: Code
handoff: Repair the volume-backend npm verifier's UID mismatch in c849_seed through separate Code/Review/land; add native UID-aware regression and then qualify the full Fixture gate on an explicitly selected repaired RC. Current RC b7265a05aa8773f08dbf457b93c7561e7874822d remains pinned and Fixture remains failed. No cleanup or retry was performed because this run already removed its fixture resources and the defect is systematic.
artifact: docs/investigations/2026-10-16-server2-fixture-permission.md
