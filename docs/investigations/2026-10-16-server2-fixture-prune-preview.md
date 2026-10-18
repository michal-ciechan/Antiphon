# server2 Fixture prune preview diagnosis, 2026-10-16

**Systematic script defects confirmed; no cleanup or operational rerun performed.**
F-8's preview still treats recovery as a host path after Fixture moved recovery
into a Docker work volume. A separate finalizer ordering defect records the
still-live fixture donor as residue before its authorized release. Fix both
together before retrying this gate. Inventory also has a latent error-reporting
defect; the model and evidence gaps below explain why native failures remained.

Task: `5676e3a4` (Debug). Deliverable only, on `feat/card-task-5676e3a4`.
Investigated source: `6ca859d006b0a3cac9a88dc6bd551c37e98b2217`.
Deploy report: `docs/investigations/2026-10-16-rc-activation.md` at
`92462b873a42f05b3ce26dd41e9e5d6488b66e68`, fetched from
`origin/feat/card-task-feb1f83b`.

All source line numbers below refer to the investigated SHA, **not** this task's
older base `6e23e0b4c8d6b2ae12218c2c0058593dbf773530`. Canonical was read-only at
the requested SHA. Git blob comparisons verified the canonical copies of
`c590-remote.sh`, `c590-real.ps1`, both shell models and
`RemoteScriptContractTests.cs` against that commit. No implementation changed.

## Retained failure and its cause

The Deploy invocation was:

```powershell
pwsh -NoProfile -File scripts/verify-card0849-caches.ps1 -Case Fixture -Sha 6ca859d006b0a3cac9a88dc6bd551c37e98b2217
```

It ran from `C:\src\Antiphon`, 12:54:39-12:59:38 UTC. Wrapper exit was **1**;
both retained results say
`{"accepted":false,"diagnosis":"FixturePrunePreviewFailed","exit":2}`.
There are **7 completed groups, 20 expected-red families, 26 negative variants**,
not the required 9/32/47. No `preview.txt`, `budgets.txt`, `consumers.txt`,
`prune.txt` or final summary was retained in the case directory. The remote
case-file inventory agrees with the local copy.

Evidence root:
`C:\src\Antiphon\.antiphon\c849-c849a76febf8be3e43f3\0\`.
Its manifest binds `runId=c849a76febf8be3e43f30` and the full investigated SHA.
Case receipts are in its `runner-cache-fixture` child; the corresponding host
directory is `/work/test-evidence/c849a76febf8be3e43f30/runner-cache-fixture/`.

The exact failing call is `scripts/c590-remote.sh:4684`:

```bash
( write_result() { printf '%s\n' "$2"; exit "$3"; }; c849_preview ) \
    || write_result false FixturePrunePreviewFailed 2
```

This is the application's read-only cache-prune preview, **not** `docker prune`.
It observes the three cache volumes, measures recovery and backing-space bytes,
then would write preview/consumer receipts. No cache deletion occurs here.

The source and retained observations determine the failure:

1. `c849_fixture` changes `SERVER2_ROOT` to
   `$CASE_DIR/fixture-controller` (`c590-remote.sh:5103`). F-3 selects the volume
   payload backend (`:3802`), whose root is `/fixture/cache` (`:1726`).
2. F-8 creates and manifests
   `/fixture/cache/recovery-c849a76febf8be3e43f30-prune`, then writes that path
   into its host-side marker (`:4668-4677`). The manifest step and all budget
   observations completed before the reported call.
3. `payload-entries.txt` contains exactly two `seed volume /fixture/cache` lines
   and `preview volume /fixture/cache`. `volumes.txt` contains all three
   observations below, so preview got through `:2576-2578`.
4. At `:2582`, preview accepts only
   `"$SERVER2_ROOT"/cache/recovery-[a-z0-9]*`. The actual `/fixture/cache/...`
   path cannot match the host controller prefix. It calls
   `write_result false CacheRecoveryInvalid 2` before `df`, preview publication,
   consumer checks, or any prune.

| Role | Observed owner/group/mode | Bytes | Scaled budget |
|---|---|---:|---:|
| nuget-packages | 1654:1654:700 | 40960 | 51200 |
| nuget-scratch | 1654:1654:700 | 8192 | 10240 |
| npm-content | 1654:1654:700 | 49152 | 61440 |

**Inner output limitation:** `CacheRecoveryInvalid` and exit 2 are reconstructed
from the exact executed source and these receipts, not claimed as a retained
stdout line. The bridge consumes remote stdout into `$code` and does not save
it; see finding 3. The known Deploy session's normalized transcript and its
mapped native transcript were checked. They retain outer
`DIAGNOSIS=FixturePrunePreviewFailed`, wrapper exit 1 and the cleanup warning,
but not the inner preview diagnosis. The result receipt independently retains
case exit 2. Do not invent a missing preview log or treat the outer diagnosis as
the original inner error.

This is not an npm-log leftover, a uid mismatch at the failing predicate,
missing helper, Docker flag/version incompatibility, or timeout. The predicate
does not call Docker or touch recovery contents. The helper and volume reads
already succeeded; the rejection is a deterministic mismatch between two path
namespaces. No permission change can make the prefixes equal. The earlier npm
fix remains present at `:2321-2328` and the retained npm receipt says passed.

## Defect set and proposed repairs

### 1. Preview has three host-only recovery operations

Locations: `scripts/c590-remote.sh:2582`, `:2583`, `:2584`; producer `:4668`;
existing backend-aware validation `:1941` and filesystem dispatch `:1730`.

Changing only the prefix would expose the next failure: `[ -d "$recovery" ]`
still executes on the host, where `/fixture/cache/...` is not the work-volume
mount. The following host `sudo du` is wrong for the same reason. These are one
repair, not three rollout retries.

Proposed fix: validate against `c849_payload_root`, and perform directory/link/
canonical-path validation and byte measurement through the selected payload
recipient. Use the readonly uid-1654 reader for volume payloads; keep host
ownership and containment checks for production host recovery. Keep preview
receipts in the host controller. Do not weaken recovery validation, create a
host `/fixture` alias, or grant the helper broader host access.

Verification needed: execute the actual preview body for both backends, with
valid recovery, missing recovery, an escaping/link path, and failed size read;
then feed its actual receipt into the real F-8 prune/control flow. Assert no
payload mutation and no success receipt after a failed observation.

### 2. Finalization audits the donor before releasing it

Locations: donor creation `scripts/c590-remote.sh:3793`, `:3822-3825`;
project-wide collection `:4405-4415`; presence refusal `:4448-4455`;
finalizer `:5006-5013`, `:5034-5057`, `:5076`.

The fixture donor has the **same temp Compose project label** as the later
retention probe, but service `session-runner` rather than `probe`. The retained
`.fixture-compose-c849c849a76febf8be3e43f30temp` explicitly lists both containers.
The finalizer runs `compose down -v` for `shared.yml`, then immediately checks
every recorded project resource. It reaches the donor while the donor is still
owned by the separate acquired-consumer cleanup path. A present object is
recorded as `unknown-cleanup-outcome` by this audit. Only afterward does the
finalizer release acquired consumers, including the donor, by identity.

This explains the retained line:

```text
unknown-cleanup-outcome container c849-c849a76febf8be3e43f30-donor
```

The residue contains that name alone. Its legacy donor identity is
`c5daa7b49ba8b1d0f9b98963f1a2a5d7be47bdc815ad73d5de8b2a66ad449aa3`.
The later recorded resource census found no run resources. Thus the warning
does not prove a Docker outage or leftover requiring removal: this audit uses
the same wording for a known-present container. The source order and recorded
donor explain the warning without either hypothesis. Even on a successful
preview, the nonempty residue would make finalization refuse success.

Proposed fix: release the identity-validated donor/acquired consumers before
the final Compose absence audit, or give the donor its own project namespace
and explicitly audit it after its release. Preserve collection before teardown
and final positive-absence checks. Do not add blanket `--remove-orphans`, erase
the existing historical residue, or clear acquisition-uncertainty evidence just
because a later probe succeeds.

Verification needed: model the real same-project/different-service donor
alongside the probe. Successful cleanup must prove both absent without false
residue; a foreign/replaced/unknown donor must still remain protected and fail.

### 3. The bridge loses inner failure output

Locations: `scripts/c590-real.ps1:110-113`, `:505-516`;
`scripts/c590-remote.sh:4684-4685`, `:4703-4704`.

`Invoke-C590Ssh` emits SSH stdout and returns `$LASTEXITCODE` on PowerShell's
same success stream. `$code = Invoke-C590Ssh $remote` captures both, and the
normal receipt-present path never persists or emits `$code`. Inner preview/
apply diagnostics are stdout from their replacement `write_result`, while only
the enclosing `FixturePrunePreviewFailed`/`FixturePruneApplyFailed` is durable.
This is why retained evidence cannot quote the original inner line.

Proposed fix: separate the numeric SSH exit from bounded, scrubbed stdout/stderr
and retain a safe case log. Also preserve F-8's inner diagnosis and exit in an
allowlisted receipt before the outer wrapper failure. Apply the same capture to
prune apply, not just preview. Do not export full inspect output, environment,
package metadata or credentials. Test a known inner refusal and transport
failure independently; each must survive wrapper propagation.

### 4. Models do not exercise the failing composition or real uid effects

Locations and changes needed:

| Model location at investigated SHA | Gap | Proposed change |
|---|---|---|
| `scripts/fixtures/c1192-workspace-contract.sh:2231-2263` | `.payload-note-only` deliberately returns 85/86 immediately after backend selection; it proves the label, not preview's subsequent recovery operations. | Keep the boundary checks, add actual volume-preview execution with distinct host and container paths. |
| `scripts/fixtures/c913-seed-contract.sh:800-826` | `volume-prune` sets `CASE=runner-cache-prune` and manufactures preview files in `reset_prune`; it never calls `c849_preview` or the actual F-8 wrapper. | Add a production-shaped Fixture F-8 model that consumes the real preview output and all PC-21..25 paths. |
| `tests/Antiphon.Tests/Scripts/RemoteScriptContractTests.cs:6786-6802`, `:7925-7936` | Preview test executes only budget/headroom helpers plus text assertions; the other harness manufactures its preview too. | Assert backend-aware preview behavior rather than treating these checks as preview acceptance. |
| `scripts/fixtures/c1192-workspace-contract.sh:2832-2872`, `:2736-2763` | Finalization/Compose scenarios substitute seed and several groups, including prune. Their success setup does not reproduce the full native donor/retention/finalizer composition. | Plant a correctly acquired donor with the actual temp project and distinct service, retain it through Compose down, then run the actual release/audit ordering. |
| `scripts/fixtures/c913-seed-contract.sh:151-156`, `:246-298`, `:402-440`; `scripts/fixtures/c1192-workspace-contract.sh:315-316`, `:1151-1180` | Single-user filesystem remapping and mocked chown/uid/tool behavior cannot establish that root-created directory contents are removable by uid 1654. The npm verifier model checks invocation, not native ownership effects. | Keep the fast model, add a scoped native writer/create/remove assertion for each cross-uid transfer and npm verification. Record actual uid/modes and restoration; do not claim the model proves them. |

No direct fall-through to a real Docker CLI was found in the inspected model
dispatches. C913's `docker()` rejects unknown top-level verbs (`:307`); C1192
also installs a fail-closed PATH `docker` shim (`:261-263`). C913's executable
deny-list at `:27` lacks Docker, however, so future child-shell calls would not
inherit the unexported function. Add Docker to that deny-list and assert an
empty escaped-call trace. This is a model containment gap, **not evidence that
this run or an existing modeled branch invoked an unintended daemon**.

### 5. Running Inventory can hide earlier probe failures

Location: `scripts/c590-remote.sh:5207-5214`.

The running-container `/bin/sh -c` body has no `set -e` or explicit checks for
`dotnet nuget locals`, `npm config get cache`, or the `du` calls. Its exit is the
last `stat` result. An earlier permission/tool/read failure can therefore yield
an accepted but incomplete inventory if `stat` succeeds. This is latent, not the
observed Fixture failure.

The inventory model defaults `.State.Running` to false
(`scripts/fixtures/c913-seed-contract.sh:217`), so its normal acceptance cases
at `:923` and `:941` take the stopped-container branch. Its exec dispatcher at
`:234-245` does not model these inventory commands. The live PC-31 helper also
returns false for running (`c590-remote.sh:4740`). These do not prove successful
running-container inventory collection.

Proposed fix: explicitly fail on each required command while continuing to
represent deliberately absent cache paths as `absent`; add running-inventory
success and individual command-failure cases. Keep the probe at uid 1654.

## Remaining Fixture ownership and Inventory/Seed preconditions

Static read covered the rest of F-8, PC-21..32, F-9 evidence/inventory and
finalization, plus Inventory and cold/full/saved Seed admission. Nothing in
these sections was executed by this Debug task.

| Boundary examined | Finding and disposition |
|---|---|
| F-8 recovery copy, `c590-remote.sh:4648-4673`; source setup `:4196-4205`, `:4242-4245` | Copy runs as root, but `cp -a` preserves the source's uid-1654 ownership, and source import explicitly chowns the cache. The recovery parent, npm fixture content and manifest are writer-created. No second current root-owned-log defect established here. Retain preservation checks; a native uid assertion belongs with finding 4. |
| F-8 delete/refill, `:2853-2875`, `:4575-4603` | Deletes and npm refill run as uid 1654. Refill mounts only `_cacache` as shared cache; its npm logs are outside that mount in the disposable container. The work tarball is readonly. No analogous root-created working log found. The legacy root refill at `:2860-2867` explicitly chowns the copied versions; F-8 uses schema 3 and does not enter it. |
| PC-27..30, `:4717-4728`, `:4775-4809` | Warm probes use uid 1654 and a uid-1654 tmpfs; warm sentinel creation/removal uses that same uid. Recovery backup/change/restore use the writer. Root copy above preserves its ownership. No new uid mismatch identified. |
| F-9 and controller removal, `:4850-4874`, `:4910-4934` | Payload writes use writer; reads use readonly reader. Host controller removal validates its host-owner identity. Preview's future host receipts must stay owned by that controller user; fixing path handling must not introduce root-owned payload under it. |
| Inventory, `:5160-5217` | Requires host lane/jq, successful complete container lookups, typed status, one full container ID/image and a consistent retired-temp absence. Uses uid 1654 for running-container cache reads. No volume creation/deletion or root-vs-1654 cleanup boundary; finding 5 is its concrete defect. |
| Cold Seed, `:1981-2203` | Requires absent marker, explicitly retired/absent temp, identified unchanged main, local labelled roots at exact Docker volume paths, zero attachments including stopped containers/bind overlaps, and empty roots. Root initialization hands roots to 1654 before same-uid canary create/rename/delete. No payload/npm verification is run. These are intentional refusals, not a reason to reset or re-own existing volumes. |
| Full/saved Seed, `:2206-2400`, importer `scripts/c849-import-saved-donor.ps1:62-70` | Host staging belongs to the host user; saved import runs with that user's numeric uid/gid (`:1971`). Fixture streaming extracts as writer (`:2299-2302`). Import into cache runs as root and explicitly chowns/normalizes writable modes (`:2346-2349`). Host npm verification still runs as root (`:2325`), but its `/npm/_logs` is outside the bound `stage/npm` cache, so the observed prior fixture-log failure does not follow from this call. Any new cache directories npm creates inside the bind remain a native ownership coverage obligation, not a demonstrated new failure. |

Full Seed may reuse a validated marker. Without one it requires an explicit live
or saved donor and empty unmarked caches; saved import additionally requires
both drains/zero counters, no temp containers, no cache attachments and idle
build slots. A live donor must be drained/idle with no separate app-uid cache
writer before its controlled stop. Later smoke, recovery, reconnect and marker
checks remain mandatory. These conditions were read, not established live.

The scripts assume local Docker volumes at
`DockerRootDir/volumes/<name>/_data`, with the fixed labels and uid/mode above.
The retained F-8 observations satisfy that assumption. No Docker flag/version
failure or missing helper is evidenced. The acquired writer/reader fingerprint
records helper image
`sha256:ea8bafe24373694b3421d63afdffb4533faf0710f709d96dea661123b55e6835`,
the standing older image; the cache helper permits this fallback (`:1372-1405`).
It is not proof that a new RC image was deployed.

One provenance check matters for later PC-27: cache cases skip `ensure_checkout`
(`:8317-8321`) but bind readonly inputs from `$CHECKOUT` (`:4722-4724`,
`:4597-4598`, `:2365-2366`). A read-only host `git rev-parse HEAD` returned
`7507b3cfe0e2f7660a0c9f9f8eb19dc89a937bce`. The three consumed inputs
(`verify-codex-image.sh`, `build-slot.ps1`, `lib/build-slot.ps1`) have **no diff**
between that SHA and the requested SHA. Thus there is no current helper-content
mismatch to repair, but a future gate should bind/verify those input identities
rather than infer them from the manifest's source SHA. Do not move the shared
host checkout as part of this diagnosis.

## Evidence integrity and handoff

SHA-256 values of the local case receipts inspected:

| File | SHA-256 |
|---|---|
| `c590-result.json` | `19bd93fd7e4afd57bf552a60062b6c4657110f1729e0bf6d78fdcf1e09ac6530` |
| `payload-entries.txt` | `a54ef9ff21b51af387b667976d2d8cfbc87c3281902d56dd1800bf172d7e2f46` |
| `volumes.txt` | `5cf19db18b2e3ef527ad07ab96d43446690513089cfe6d6fc4f9af86a1d93c7b` |
| `fixture-cleanup-ledger.txt` | `eb467ac61a6d9f59c4a725ca1c3fbf830922fb2b16f8b073f7c7cfbb644a37a3` |
| `fixture-cleanup-residue.txt` | `22f7e24d9947887d37592ff3a8b694a0e33f5e5d2e228fbabc1e813f6dda539c` |

Generated evidence remains at its original ignored locations. The only server2
commands in this task listed files in the exact retained case directory and
read the host checkout's HEAD. No Docker command, container exec, cleanup,
drain, deployment, service restart or gate rerun was performed. Canonical stayed
at the requested SHA, with its two original untracked patch files unchanged.

Tests/builds/new operational gates: **0 run**. This is an evidence/static
diagnosis, not a passing checkpoint or native reproduction. Report whitespace
and the named source-blob identities were checked. The historical failed gate
remains failed. All findings are inherited at the investigated source; this
report introduces no executable changes.

Commission a bounded Plan/Code repair covering findings 1 and 2 together, the
actual preview/cleanup model composition, and durable inner diagnostics. Include
finding 5 as the scoped Inventory fix; keep native ownership verification
explicit. After ordinary Review/publication and a caller-supplied tested RC SHA,
the Deploy owner should run Fixture first from canonical at that exact SHA and
require 9 groups, 32 families, 47 variants, two inventories and clean finalization.
Do not repeat the current SHA or use cleanup to bypass the deterministic defect.
Inventory, Seed and all rolling gates remain pending and need fresh preconditions.

--- next stage ---
next: plan
handoff: Repair backend-aware preview recovery reads and donor-before-audit finalization together; cover the actual F-8/finalizer composition, preserve inner failure diagnostics, and fail incomplete running Inventory probes. No live cleanup or rerun authorized by this diagnosis. Publish/review a repair, obtain the tested RC SHA, then return to Deploy for Fixture and the remaining gates.
artifact: docs/investigations/2026-10-16-server2-fixture-prune-preview.md
