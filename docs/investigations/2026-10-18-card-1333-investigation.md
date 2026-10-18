# CARD-1333 sentinel ledger, seed-subshell pin, network-create failure

Confirmed at `61117dd1663d2f47ce4708c18b609b8936491986` (`feat/card-task-d3a538a6`, rc tip). Three separate gaps. The sentinel failure is a stale fixture. The seed-subshell failure is a stale text pin. `FixtureNpmNetworkFailed` on a failing `docker network create` is implemented and untested. CARD-1315's hash fail-closed edit is not the cause of either red.

CARD-1333 (`948ed8b0-154f-4eb8-bfb4-97820b7a7f18`) names two inherited `RemoteScriptContractTests` reds from review `6b367dd6`, reproduced in serial runs `20261018-105244-0e79` (review HEAD) and `20261018-110315-9caf` (base `5e5d5cc7`). Those trx files are not in this worktree. The mechanisms below were measured here, on the same files the tests read. The TUnit host was not run. No whole lane was run. The four Linux reds the card assigns to CARD-1216 (transfer, locks, barriers, partial) were left alone.

`27dc531ab4846f1eec090952db29227d1a8ed6c7` (CARD-1208) is an ancestor. So are `7c6147b64f456b6203b529cd5388ec8d3a2a49e2`, `40bb77c9ca355e2039b02d6864cc18c92e1ec9a4` (CARD-1315), and `dab22827dc8be5d9b52717e16823eef03846a767` (CARD-1328).

## 1. Sentinel — stale fixture

`C1192_Npm_volume_sentinel_and_empty_cache_miss_are_observed` (`tests/Antiphon.Tests/Scripts/RemoteScriptContractTests.cs:1075-1081`) runs `C1192Run("npm-sentinel")` and requires `missing-sentinel-refused`, `wrong-sentinel-refused`, and `offline-miss-refused` to be `1`.

At this tip the fixture command exits 0 and prints:

```text
missing-sentinel-refused=0
wrong-sentinel-refused=0
offline-miss-refused=0
exact-sentinel-accepted=0
```

`C1192Run` (`RemoteScriptContractTests.cs:8693-8700`) accepts that exit. Shouldly then stops on `missing-sentinel-refused` expected `1`, actual `0`.

Each of the five install tags (`good`, `missing`, `wrong`, `miss`, `other`) recorded status `1`, empty stdout, sentinel bytes `ABSENT`, and an empty event log. `scripts/fixtures/c1192-workspace-contract.sh:2191-2205` copies those files. Empty events mean `c849_fixture_npm_install` returned before `docker run` and before `test -f` / `cat --`.

`mode_npm_sentinel` plants the cache and does not record it (`c1192-workspace-contract.sh:2183`, `plant_cache` at `1693-1698`). Since `27dc531a`, `c849_fixture_cache_mount_ok` returns 1 when the name is absent from `$CASE_DIR/.fixture-volumes` (`scripts/c590-remote.sh:4000-4003`). `c849_fixture_npm_install` calls that helper before any container (`c590-remote.sh:4404-4406`) and returns 1 (`c590-remote.sh:4413` is never reached). `scenario.err` stayed empty, so this is the ledger short-circuit, not `c849_fixture_cleanup_unknown` and not CARD-1315's inspect classifier.

Production `c849_fixture_npm` records the same cache name before it uses the volume (`c590-remote.sh:4429-4438`). `c849${RUN}npmcache-npm` is already an owned name (`c590-remote.sh:3383`). `plant_cache` already writes the owner, run, schema, and cache-role labels `c849_fixture_cache_identity` checks (`c590-remote.sh:3305-3316`).

A copy of the fixture with one added line after `plant_cache`:

```bash
printf '%s\n' "$cache" >> "$CASE_DIR/.fixture-volumes"
```

printed all four facts as `1` and exited 0. That single line is enough for the missing-file `test -f`, the wrong-byte `cat --`, and the `ENOTCACHED` → `NpmOfflineCacheMiss` split inside `mode_npm_sentinel` (`c1192-workspace-contract.sh:2212-2235`). The test expectations match `c849_fixture_npm_install` (`c590-remote.sh:4413-4418`) and `c849_fixture_volume_test_file` / `c849_fixture_volume_read` (`c590-remote.sh:4057-4082`).

## 2. Seed subshell — stale test

`C1191_Fixture_failure_records_unhandled_exit_without_seed_receipt` (`RemoteScriptContractTests.cs:716-737`) still requires this exact guard inside each seed subshell, before `c849_seed`:

```text
write_result() { printf '%s\n' "$2"; exit "$3"; }
```

The trap and exit-function assertions still match. `c849_fixture` traps with `trap 'ec=$?;` (`c590-remote.sh:5262`). `c849_fixture_exit` calls `c849_fixture_finalize` and then `write_result false "UnhandledExit $ec" "$ec"` (`c590-remote.sh:5235-5239`). Both close strings are present:

```text
c849_fixture_inner seed c849_seed ) || write_result false FixtureSeedFailed 2
c849_fixture_inner seed-rerun c849_seed ) || write_result false FixtureSeedRerunFailed 2
```

(`c590-remote.sh:3939-3943` and `3966-3967`). The slice from `\n    (` through `c849_seed` does not contain the old guard. Shouldly fails on `FixtureSeedFailed` at `RemoteScriptContractTests.cs:735`. A Perl walk of the same `Block` / `IndexOf` rules produced `contains_guard=no` for both diagnoses.

`7c6147b64` (2026-10-16, `scripts/c590-remote.sh` and `scripts/c590-real.ps1` only) moved that override into `c849_fixture_inner` (`c590-remote.sh:3021-3038`). The inner subshell overrides `write_result` to record `fixture-$inner_phase.txt`, print the diagnosis, and `exit "$3"`. If `write_result` never runs, the inner EXIT trap records `diagnosis=UnhandledExit`. The outer `|| write_result false FixtureSeedFailed` still uses the case publisher. The parent `WROTE` flag stays unset during the inner call. The test file's guard pin is still the CARD-1191 text from `e76125e1c7` (`RemoteScriptContractTests.cs:726`).

`40bb77c9c` (CARD-1315) changed the sha256 lines under the first subshell (`c590-remote.sh:3950-3958`). It did not remove the guard. Those lines sit outside the slice the test searches.

## 3. Network create failure — missing test

`c849_fixture_npm` keeps the create status and the id (`c590-remote.sh:4484-4487`):

```bash
identity="$(docker network create --label io.antiphon.owner=c849-fixture \
    --label "io.antiphon.fixture-run=$RUN" --label io.antiphon.fixture-role=npm-network "$network")" \
    || write_result false FixtureNpmNetworkFailed 2
[[ "$identity" =~ ^[0-9a-f]{64}$ ]] || write_result false FixtureNpmNetworkFailed 2
```

The `||` branch arrived in `27dc531a`. CARD-1328 (`dab22827d`) made the fixture create arm print one 64-hex line (`c1192-workspace-contract.sh:1209-1213`) and still `return 0`. That arm has no fault. Stock `npm` mode at this tip prints `npm-work-volume=1`, `refill-volume-source=1`, `offline-network-none=1`, `npm-code=0`.

No test in `RemoteScriptContractTests.cs` mentions `FixtureNpmNetworkFailed`. `C1208` deletion-audit `wrong-network-id` (`RemoteScriptContractTests.cs:523`) is cleanup identity, not create failure.

A copy that returned 1 from `docker network create` before printing an id, and emitted `diagnosis_of` the captured `c849_fixture_npm` stdout, printed:

```text
npm-work-volume=1
refill-volume-source=0
offline-network-none=0
npm-code=2
npm-diagnosis=FixtureNpmNetworkFailed
```

Exit of the fixture command was 0 because `mode_npm` records the child status. The diagnosis is the production `||` at `c590-remote.sh:4486`. Refill did not run. The stock fixture cannot take this path.

## Smallest fix

Not done, noted: no source change in this investigation.

1. In `mode_npm_sentinel`, after `plant_cache`, append `"$cache"` to `$CASE_DIR/.fixture-volumes`. Leave `c849_fixture_npm_install` and `C1192_Npm_volume_sentinel_and_empty_cache_miss_are_observed` as they are. That test is already the red witness; the ledger line turns the three facts to `1`.
2. Retarget `C1191_Fixture_failure_records_unhandled_exit_without_seed_receipt` at `c849_fixture_inner`: both subshells call `c849_fixture_inner seed` / `c849_fixture_inner seed-rerun` and then `c849_seed`, and the inner `write_result` prints a diagnosis and `exit "$3"`, with the `UnhandledExit` trap when `inner_recorded` is 0. The current test is already the red witness. Putting the one-line override back would skip the bounded verdict `7c6147b64` records.
3. Add a fixture fault that makes `docker network create` return non-zero and print no id, and a test that runs `c849_fixture_npm` under that fault and requires diagnosis `FixtureNpmNetworkFailed` and exit 2. Commit that test while create still returns 0 (it fails), then add the fault. Keep the CARD-1328 success id at `c1192-workspace-contract.sh:1210-1212`.

One filter after those three edits:

```text
/*/*/RemoteScriptContractTests/(C1192_Npm_volume_sentinel_and_empty_cache_miss_are_observed)|(C1191_Fixture_failure_records_unhandled_exit_without_seed_receipt)|(<new network-create failure method>)
```

## Remaining uncertainties

- The TUnit host was not run. The bash mode `C1192Run` asserts was, and the C# `Block` / `IndexOf` walk was repeated in Perl. Windows was not re-run. Both failures are in the shared shell file and the shared fixture.
- The review trx paths named on the card were not in this checkout.
- `cleanup-probe` and `deletion-audit` were not re-run. Their network arm still `fail`s on create (`c1192-workspace-contract.sh:1206`) when the new fault stays outside that `if`, which is where the CARD-1328 printf already sits.
- The id-failure regex at `c590-remote.sh:4487` is a second `FixtureNpmNetworkFailed` path. This card's missing test is the non-zero create, which fires at `4486` before that regex.
