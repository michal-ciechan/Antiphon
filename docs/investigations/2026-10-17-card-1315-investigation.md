# CARD-1315 — c590-remote.sh fail-closed leftovers

Date: 2026-10-17. Read-only. No script or test edit.

Card fa1b36d3-8e53-4105-969a-eaa5f2aa1d25, label `rc-2026-10-15`. The four leftovers were named from seed-hardening `origin/feat/card-task-efc5b7be` at `8c0001ce68a63146d04242b0dce0d2cee74105b5` (`fix: fail F-7 when volume inspect is not positive absence`). That commit is an ancestor of both tips below. Its three earlier static findings are already closed on the rc line: production seed hash status (`c0cc11bfd`, `scripts/c590-remote.sh:2375-2378`, token `CacheSeedMetadataFailed`), smoke-helper removal (`2cc1cb0fd`, `CacheSeedHelperRemovalFailed`), and F-7 retention inspect (`8c0001ce6`, `c849_fixture_resource_state`, token `FixtureVolumeInspectFailed`). None of those diffs touch the four sites below.

## Tips

| Tip | SHA | `scripts/c590-remote.sh` blob |
|---|---|---|
| `origin/rc/2026-10-15` | `5e5d5cc728949420647d15a932766e70755a6f01` | `410b30179cbf0491bc30d100cf48465068b80e62` |
| `origin/master` | `9b4d3c89a5912795a51956d84a5a235f5de2ffd6` | same blob |

Local task tip `3adbe0d5efe214075b1325cd55081e3524cd148f` has blob `af11ebf15`. The only difference from the rc blob is CARD-1324 at `c1008_tasks_collect` (hunk starts at line 5957). The four sites are below that hunk, so the line numbers below are the lines on the rc tip, on master, and on `3adbe0d5`.

Checked and not a fix for these sites: CARD-1316 `3999bbaca` (audit cap 10800s), CARD-1317 `e6ffa1bd6` (settled task with no land), CARD-1319 `da326d763` (pass-0 settle), CARD-1324 `d7b8bbd25` (pass-1 no-land appearance), CARD-1311 audit scratch `0277e15d7` (dirty-audit reap and empty `for-each-ref` scratch). `git show` of each against `scripts/c590-remote.sh` has no hunk for these tokens or functions.

Train: all four bugs are in code already on `rc/2026-10-15`. Land on that rc line, then no-ff merge the rc tip to master. Not master-only.

## Confirmed

Bash reproduction is GNU bash 5.2.15(1)-release, `set -euo pipefail`, matching `scripts/c590-remote.sh:11`. Stubs stand in for `sha256sum` and `docker`. No TUnit run and no live daemon: the witness methods do not exist yet, and the operator bar is one narrow filter each with no whole-lane run.

`c849_fixture_resource_state` (`scripts/c590-remote.sh:3390-3412`) already classifies docker inspect: return 0 present, 1 only for an exact missing-resource line, 2 for any other nonzero result including a daemon error. F-7 uses it. The sites below do not.

The script EXIT trap (`scripts/c590-remote.sh:8497`, and `c849_fixture_exit` at 5191-5195) records `UnhandledExit <status>` when the shell dies before `write_result`.

### 1. F-3 tool hash — still open as an unnamed abort

`c849_fixture_seed` at `scripts/c590-remote.sh:3939-3942`:

```bash
before="$(c849_payload_fs reader 'sha256sum "$1"' bash "$tree/packages/c913.tools/2.0.0/tool" | cut -d' ' -f1)"
after="$(c849_payload_fs reader 'sha256sum "$1"' bash "$recovery/packages/c913.tools/2.0.0/tool" | cut -d' ' -f1)"
[ "$before" = "$after" ] && [ "$(c849_payload_fs reader 'stat -c %a "$1"' bash "$recovery/packages/c913.tools/2.0.0/tool")" = 755 ] \
    || write_result false FixtureSeedPayloadChanged 2
```

`set -o pipefail` is on for the whole script (line 11) and again at line 3915. There is no `set +o pipefail` in the file. Card lines ~3899-3900 are these lines; blame `33d0c53a4b` (2026-10-12), unchanged since the F-7 commit.

Measured: a `sha256sum` that exits 1 with empty stdout makes the assignment exit 1. The equality test is not reached, `PASS F-3` is not printed, and the marker comparison does not succeed. With the script trap that exit is `UnhandledExit 1`, not `FixtureSeedPayloadChanged`. Two empty hashes plus mode 755 do not pass while pipefail stays on. Turning pipefail off does make that comparison pass; that is not the script's option set.

A failing `stat` inside the `&&` / `||` list does emit `FixtureSeedPayloadChanged` and exit 2. Only the hash assignments abort unnamed.

Smallest fix: copy the production seed capture at 2375-2378. Assign the `sha256sum` line without `| cut`, `|| write_result false FixtureSeedPayloadChanged 2`, require `^[0-9a-f]{64}$` on each side, then compare the two hashes and the mode. Do not publish `PASS F-3` on a failed read.

Red-first witness, one filter: `/*/*/CacheFixtureCompletionTests/Fixture_seed_tool_hash_failure_is_named`. Model the reader `sha256sum` of that tool path as exit nonzero and empty stdout. Assert diagnosis `FixtureSeedPayloadChanged`, exit 2, and no `PASS F-3`. On current source the model exits 1 with `UnhandledExit`, so the assertion is red.

### 2. F-8 manifest hash — still open as an unnamed abort

`c849_fixture_prune` at `scripts/c590-remote.sh:4776-4779`:

```bash
payload="$(c849_payload_fs reader 'sha256sum "$1"' bash "$recovery/recovery.manifest" | cut -d' ' -f1)"
image_id="$(docker image inspect -f '{{.Id}}' "$image")" || write_result false CacheRecoveryImageMissing 2
printf 'schema=3\n...manifest-sha256=%s\n' ... "$payload" > "$C849_READY"
```

Card line ~4736 is 4776. Blame `33d0c53a4b` for the pipeline, `5b3f76a66a` for the printf. Unchanged since F-7.

Measured: the same failing `sha256sum` exits 1 at the assignment. `/tmp` marker file stayed absent. The printf does not run, so a later 64-hex check in `c849_require_ready` (`scripts/c590-remote.sh:2473`) is not what stops this failure. The trap records `UnhandledExit 1`.

Smallest fix: same capture as 2375-2378, before the printf. On a failed read or a non-64-hex digest, `write_result false CacheSeedMarkerInvalid 2` and do not create `$C849_READY`. `CacheSeedMarkerInvalid` is already the token `c849_require_ready` uses for a bad `manifest-sha256`. Leave `CacheManifestFailed` on the `c849_manifest_write` line above (4775).

Red-first witness, one filter: `/*/*/CacheFixtureCompletionTests/Fixture_prune_manifest_hash_failure_is_named`. Stub that manifest `sha256sum` to exit nonzero. Assert `CacheSeedMarkerInvalid`, exit 2, and no ready marker. On current source the marker stays absent and the status is exit 1 / `UnhandledExit`, so the named-token assertion is red.

### 3. Fixture volume inspect treated as free — still open, false continue

Six raw checks. Success means occupied. Any inspect failure, including a daemon error, skips the `&&` and the function continues and registers the name.

| Function | Lines |
|---|---|
| `c849_fixture_prepare` | 3798-3799 |
| `c849_fixture_seed` | 3884 |
| `c849_fixture_nuget_race` | 4225 |
| `c849_fixture_apphost` | 4290-4291 |
| `c849_fixture_npm` | 4400-4401 |
| `c849_fixture_contract_controls` | 4873-4874 |

Each is `docker volume inspect "$name" >/dev/null 2>&1 && write_result false FixtureNamespaceOccupied 2`. Card lines ~3758, 3844, 4185, 4251, 4361, 4834 are these six, about forty lines earlier. 4251 maps to the apphost loop at 4291. Helper lookups at 4256-4257 and 4300-4301 already use `c849_fixture_helper_lookup` (0 free, 1 occupied, other unknown). Leave them.

Measured with `docker` exiting 1 and stderr `Cannot connect to the Docker daemon`: the prepare-shaped loop printed `registered <name>` and exited 0. The same stub exiting 0 printed `TOKEN FixtureNamespaceOccupied` and exited 2.

Smallest fix: at each of the six sites, call `c849_fixture_resource_state volume "$name"`. Return 0: existing `FixtureNamespaceOccupied`. Return 1: name is free, keep the current register-and-create path. Any other status: `write_result false FixtureVolumeInspectFailed 2` and do not append the name or create the volume. Reuse the F-7 token. Do not widen any removal target.

Red-first witness, one filter: `/*/*/CacheFixtureCompletionTests/Fixture_namespace_inspect_failure_is_not_free`. One model covers all six sites: inspect exits 1 with a daemon error line that is not `No such volume`. Assert `FixtureVolumeInspectFailed`, exit 2, and the name absent from `.fixture-volumes`. On current source the name is registered and the model exits 0, so the assertion is red. Keep one positive-absence case and one present case in that same method so the free and occupied paths stay distinct; the red row is the daemon case.

### 4. Retired-cache inspect — still open, unknown becomes MainTmpMissing

`case_verify_runner_caches_retired` at `scripts/c590-remote.sh:5359-5365`. Blame `b51e8c0c07` (2026-09-30), unchanged since.

```bash
for volume in runner-tmp work runner-state dind-data; do
    if docker volume inspect "${TEMP_PROJECT}_$volume" >/dev/null 2>&1; then
        write_result false TempPrivateVolumeRemains 2
    fi
done
docker volume inspect "${HOST_PROJECT}_runner-tmp" >/dev/null 2>&1 \
    || write_result false MainTmpMissing 2
```

Measured, same daemon-error stub: the temp loop printed `temp_loop_continued`, then the main inspect printed `TOKEN MainTmpMissing` and exited 2. An exact `No such volume` stub took the same `MainTmpMissing` path. Present temp volumes still take `TempPrivateVolumeRemains` because the `if` body runs only on inspect success.

Smallest fix: classify both inspects with `c849_fixture_resource_state`. Temp volume return 0: `TempPrivateVolumeRemains`. Return 1: continue. Anything else: `write_result false CacheVolumeInspectFailed 2`. Main `runner-tmp` return 0: continue. Return 1: keep `MainTmpMissing`. Anything else: `CacheVolumeInspectFailed`. New live token so a daemon error is not the absence token. Do not remove any volume on that path.

Red-first witness, one filter: `/*/*/CacheFixtureCompletionTests/Retired_cache_inspect_failure_is_not_absence`. Stub volume inspect to exit 1 with a daemon error. Assert `CacheVolumeInspectFailed`, and assert the output does not contain `MainTmpMissing`. On current source the daemon stub emits `MainTmpMissing`, so the assertion is red. The same method keeps exact `No such volume` on the main volume as `MainTmpMissing`, and a present temp volume as `TempPrivateVolumeRemains`.

## Not done, noted

`case_verify_runner_caches_retired` lines 5355-5357 treat a failed `docker ps` as no temp containers (`if [ -n "$(docker ps ...)" ]` continued and exited 0 under a daemon-down stub); fold that status into the same retired-cache witness if the volume fix is not enough. No production edit in this investigation.
