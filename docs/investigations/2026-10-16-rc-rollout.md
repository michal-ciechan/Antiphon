# RC server2 rollout 2026-10-16

Target: `rc/2026-10-15` at `e02719a9fca650c3f5415e57a090d720deb36597`.
Canonical checkout `C:\src\Antiphon` stayed detached at that SHA. AppHost and the desktop runner were not restarted. No phase was retried.

Outcome: stopped at Seed. Token `C849 receipt missing: status.json`. Standing `server2` is still accepting `7507b3cfe0e2f7660a0c9f9f8eb19dc89a937bce`. `server2-temp` is still the retired placeholder from 2026-10-11. Registered `buildVersion` and image revision do not equal the target SHA.

## Preflight

| Check | Time (UTC) | Result |
|---|---|---|
| `GET /api/version` | 2026-10-16T15:42Z | `e02719a9fca650c3f5415e57a090d720deb36597`, capabilities `land-v2`, `operator-shutdown-v1` |
| `GET /health` | same | 200 `Healthy` |
| Canonical `HEAD` | same | `e02719a9fca650c3f5415e57a090d720deb36597`, detached. Two pre-existing untracked patches under `docs/investigations/` were left untouched |
| `landPending=true` | same | 0 rows on project Antiphon and 0 fleet-wide |
| Runner defaults | same | revision 2, `globalRunnerId=server2`, no kind overrides |

Open tasks at preflight (project Antiphon):

| Task | Status | Runner | Role | Card |
|---|---|---|---|---|
| `024ec8fb-7f2b-4800-9154-f34b2d2ab2d2` | Blocked | server2 | Plan | CARD-1281 |
| `ef29e094-b623-49e4-9947-07e9016b5ad3` | Blocked | server2 | Code | CARD-0628 |
| `25c628b6-5c19-4925-afb3-c907daf49944` | Dispatched | server2 | Code | CARD-1313 |
| `ff722349-32e9-48b1-b1d2-d44d8e91931c` | Dispatched | server2 | Review | CARD-1303 |
| `f3cc14d6-9d7b-4c01-b968-4eea0e18e776` | Dispatched | desktop | Investigate | CARD-1314 |
| `765629e8-88e0-4768-91d8-22c18f2b1a9b` | Dispatched | desktop | Deploy | CARD-1311 |

No land was in flight. Canonical status was not a half-reset.

## Phases run

### check-census — pass

- Start `2026-10-16T15:42:20.7681505Z`, end `2026-10-16T15:42:39.7485708Z`, exit 0.
- `RECYCLE_PROJECT id=d4ea7ae9-e769-474b-95b9-aa25fbc1303f name=Antiphon resolvedBy=path`
- `RECYCLE_CENSUS runner=server2 open=13 boundOpen=4 landPending=0 scopes=2 elapsedMs=4872`
- `RECYCLE_CENSUS runner=server2-temp open=13 boundOpen=0 landPending=0 scopes=2 elapsedMs=3828`
- Evidence: `C:\src\Antiphon\.antiphon\rolling-server2\c727307536536077`

### check-host-jq — pass, no provision

- Start `2026-10-16T15:42:54.9823507Z`, end `2026-10-16T15:42:56.2779927Z`, exit 0.
- Receipt `C:\src\Antiphon\.antiphon\rolling-server2\c727d77c942e6e6e\host-jq-check-host-jq.json`: path `/usr/local/bin/jq`, version `jq-1.7.1`, digest `5942c9b0934e510ee61eb3e30273f1b3fe2590df93933a93d7c58b81d19c8ff5`, mode 755, `trueExit=0`, `falseExit=1`, `installed=false`, `outcome=existing`, `sshExit=0`, `sourceSha` the target SHA.
- `provision-host-jq` was not run. The host already had the qualified binary.

### Fixture — pass

- Start `2026-10-16T15:43:19.3518431Z`, end `2026-10-16T15:50:47.4136623Z`, exit 0.
- `C849_FIXTURE groups=9 controls=32 expectedRed=32 variants=47 expectedRedVariants=47 inventories=2 failures=0 productionMutations=0`
- Summary image `sha256:ea8bafe24373694b3421d63afdffb4533faf0710f709d96dea661123b55e6835`.
- Evidence: `C:\src\Antiphon\.antiphon\c849-c849bf7e98f8fc174ebe`

### Inventory — pass

- Start `2026-10-16T15:51:15.2962429Z`, end `2026-10-16T15:51:20.5592330Z`, exit 0.
- `C849_INVENTORY runners=2 source=e02719a9fca650c3f5415e57a090d720deb36597`
- Evidence: `C:\src\Antiphon\.antiphon\c849-c8498bf48bf995f24ee4`

`server2` at inventory: container `fc2983ea4991fe85b31f54ee2ef66150e0d8fef03897d6cf1c2d6ca9c0889832`, image `sha256:ea8bafe24373694b3421d63afdffb4533faf0710f709d96dea661123b55e6835`, `buildVersion=7507b3cfe0e2f7660a0c9f9f8eb19dc89a937bce`, sessions 4, runnerSessions 4, queuedTasks 0, accepting and not draining. Shared cache mounts present. NuGet packages about 3.2 GB and npm content about 327 MB, so the volumes are not empty.

`server2-temp`: container absent, `buildVersion=null`, sessions 0, runnerSessions null, queuedTasks 0, `retiredAt=2026-10-11T14:23:12.192262+00:00`.

Marker `/home/mc/antiphon-server2/cache/seed-accepted` is a regular file, 8 lines: `schema=2`, `kind=cold`, `cold=true`, `source-sha=1f43624d1353ea59338a9c4294912cb83e47fc2e`, `image=sha256:09fe5601ba1d88a8658e7d78d1818a16b15c1cf1d39cc859601fcdaedc364bc1`. Saved donor `/home/mc/runner-cache-donor/temp-runner-cache.tar` is present and was not used.

### Seed — stop

- Start `2026-10-16T15:53:20.3425223Z`, end `2026-10-16T15:53:32.2134970Z`, exit 1.
- Command: `pwsh -NoProfile -File scripts/verify-card0849-caches.ps1 -Case Seed -Sha e02719a9fca650c3f5415e57a090d720deb36597` from `C:\src\Antiphon`. No `-Cold` and no `-SavedDonor`.
- Token: `C849 receipt missing: status.json` at `scripts/verify-card0849-caches.ps1:63`.
- Evidence: `C:\src\Antiphon\.antiphon\c849-c849b8b653fef75d473c`
- Remote receipt `0\runner-cache-seed\c590-result.json`: `accepted=true`, `exit=0`, empty diagnosis.
- `seed.txt`: `ready=true kind=cold writable=3`
- `seed-kind.txt`: `cold`
- `seed-contract.txt`: `schema=2`, `kind=cold`, `digest-type=none`, `digest=none`, `smoke=not-run`
- That case directory has no `status.json` and no `main-status.json`.

Choice of command: volumes are populated, so `-Cold` is the empty-volume path and `c849_cold_seed` refuses an existing marker with `CacheSeedMarkerInvalid`. `-SavedDonor` calls `c849_require_ready` without `allow-cold`, and a cold marker is `CacheFullSeedRequired`. Ordinary Seed is the reuse path for an existing cold marker.

## Why the receipt is missing

On `e02719a9`, cold-marker reuse in `scripts/c590-remote.sh` (`c849_seed`, after `c849_require_ready allow-cold`) calls `c849_no_temp_containers`, writes `seed.txt`, and returns success. It does not write `status.json` or `main-status.json`.

Those two files are written only inside `c849_cold_proof` when `phase=P6`, which runs from `c849_cold_seed` during a new cold create.

`scripts/verify-card0849-caches.ps1` treats `ready=true kind=cold writable=3` as a cold receipt and reads both files before it prints `C849_SEED`. The remote accept and the desktop gate disagree.

Live status after the stop would have satisfied that cold check. `runner-drain.ps1 status` at about `2026-10-16T15:54:54Z`:

- `server2`: `buildVersion=7507b3cfe0e2f7660a0c9f9f8eb19dc89a937bce`, `dispatchEligible=true`, `acceptingNewWork=true`, `draining=false`, `redirectTo=null`, `retireWhenIdle=false`, sessions 4, runnerSessions 4, queuedTasks 0. `drainReason` is the leftover text `CARD-0727 rolling upgrade complete` from the previous upgrade, not a current drain.
- `server2-temp`: `retiredAt=2026-10-11T14:23:12.192262+00:00`, `available=false`, `dispatchEligible=false`, `acceptingNewWork=false`, `draining=true`, `retireWhenIdle=true`, `redirectTo=server2`, sessions 0, queuedTasks 0, `runnerSessions=null`.

## Not run

`deploy-temp`, temp smoke, Plan canary, `drain-old`, `redeploy-old`, old smoke, `-Case Both`, `drain-temp`, `retire-temp`, `-Case Retired`.

The four server2 tasks above are still bound. They were not stopped. Drain was not started.

## Decisions

1. Do not retry Seed on this SHA. Do not pass `-Cold` or `-SavedDonor`. Do not drain `server2` to force a full seed while it is the only accepting runner.
2. Repair cold-marker reuse so it writes the same reduced `status.json` and `main-status.json` that `c849_cold_proof` writes at P6, from the live temp and main status bodies, and keep the retired-absent temp checks. The current temp row matches the desktop cold checks; the missing files are the whole stop.
3. Land that repair on `rc/2026-10-15` and run a new Deploy at the new tip. This pass did not activate `e02719a9` on server2.
