# CARD-1328 npm refill fact is red because network create prints no id

Confirmed. `RemoteScriptContractTests.C1192_Npm_seed_install_and_refill_use_work_volume` is still red at `origin/rc/2026-10-15` `5e5d5cc728949420647d15a932766e70755a6f01` and at `origin/master` `f01104c85c2c862ff9ca6e5388c58c415fb62590`. Those tips and this branch share one blob for each of the three files:

| File | Blob |
|---|---|
| `scripts/c590-remote.sh` | `410b30179cbf0491bc30d100cf48465068b80e62` |
| `scripts/fixtures/c1192-workspace-contract.sh` | `6b5bbee79ca14f57d95e053bee9550e95281c364` |
| `tests/Antiphon.Tests/Scripts/RemoteScriptContractTests.cs` | `8b8302c6a13d4c494fabfc00e920c43a26a9569c` |

The stale side is the fixture's Docker network model. The test expectation and `c849_fixture_refill_tarball` still agree.

## What fails

The test calls `C1192Run("npm")` and requires three facts (`tests/Antiphon.Tests/Scripts/RemoteScriptContractTests.cs:1061-1066`). `C1192Run` runs `bash scripts/fixtures/c1192-workspace-contract.sh npm <repo>` and reads `key=value` lines (`RemoteScriptContractTests.cs:8601-8626`).

At this tip that command exits 0 and prints:

```text
npm-work-volume=1
refill-volume-source=0
offline-network-none=0
npm-code=2
npm-refill-code=0
npm-hash-ok=1
```

There is no `C1192_FAIL` line. Shouldly stops on the first mismatch, so the reported failure is `refill-volume-source` expected 1, actual 0. `offline-network-none` is also 0 and is not reached.

`mode_npm` only calls `c849_fixture_refill_tarball` when `c849_fixture_npm` returns 0 (`scripts/fixtures/c1192-workspace-contract.sh:2127-2132`). Captured stdout of that call is `DIAGNOSIS=FixtureNpmNetworkFailed`. `npm-code=2` is that exit. `npm-refill-code=0` is the initializer at `c1192-workspace-contract.sh:2112`, because refill never runs. The run log ends on the hash-tarball line. It has no `cache add /fixture/npm-offline/pkg.tgz` row, so `refill_ok` stays 0 (`c1192-workspace-contract.sh:2060`, `2075-2080`). The emit at `2151-2154` then requires `refill_ok=1`, and writes 0. `npm-work-volume` can still be 1: seed, the offline tarball, and the hash completed before network create.

## Mechanism

`c849_fixture_npm` now keeps the network id (`scripts/c590-remote.sh:4449-4452`):

```bash
identity="$(docker network create --label io.antiphon.owner=c849-fixture \
    --label "io.antiphon.fixture-run=$RUN" --label io.antiphon.fixture-role=npm-network "$network")" \
    || write_result false FixtureNpmNetworkFailed 2
[[ "$identity" =~ ^[0-9a-f]{64}$ ]] || write_result false FixtureNpmNetworkFailed 2
```

The fixture's `docker()` network arm returns 0 for every non-`ls` command outside `cleanup-probe` and `deletion-audit`, and prints nothing (`c1192-workspace-contract.sh:1190-1209`). An empty `identity` fails the 64-hex test. Real `docker network create` prints the id.

That check arrived in `27dc531ab4846f1eec090952db29227d1a8ed6c7` (2026-10-13, CARD-1208). Its parent `ba7b590eaa6507f87ac0d8d7a98c033e4bb503c6` still had `docker network create "$network" >/dev/null`, so empty stdout was success. `git log -L 4449,4452:scripts/c590-remote.sh` shows no later edit of those lines. The fallthrough `return 0` in the fixture dates from `b1320236bddc550020a1bfba8977c218e9799a2a`; CARD-1208 extended the cleanup/deletion inspect branch in the same commit as the id check and left create printing nothing. `27dc531ab4` is an ancestor of both `origin/master` and `origin/rc/2026-10-15`.

The refill command and the classifier were introduced together in `eb689b16baacbd352c19daab742d08d16abdf3f2` (2026-10-12, CARD-1192) and the refill function body has not changed since. Current refill (`c590-remote.sh:4677-4685`) is `docker run --network none` with `${work_mount},readonly` and `cache add /fixture/npm-offline/pkg.tgz`. The classifier accepts that shape (`c1192-workspace-contract.sh:2075-2080`). The test method last changed in that same CARD-1192 commit.

A copy of the fixture that printed one 64-hex line from network create, and otherwise matched this tip, printed:

```text
npm-work-volume=1
refill-volume-source=1
offline-network-none=1
npm-code=0
npm-refill-code=0
npm-hash-ok=1
```

`npm-code=0` means `c849_fixture_network_record` accepted the id (`c590-remote.sh:3339-3341`, called at `4453`). Refill then ran and the existing classifier emitted 1. The production refill line is not stale.

## Smallest fix

Not done, noted: no source change in this investigation.

In `docker()`'s `network)` arm, after the `cleanup-probe` / `deletion-audit` `fi` and before `return 0` at `c1192-workspace-contract.sh:1209`, when the subcommand is `create`, print one line matching `^[0-9a-f]{64}$` and return 0. Leave that arm's `*) fail "unexpected network command"` in place so those two modes still refuse create. Do not change `c849_fixture_refill_tarball`, `classify_npm_runs`, or the test.

The red witness already exists. Do not add a test. One filter:

`/*/*/RemoteScriptContractTests/C1192_Npm_seed_install_and_refill_use_work_volume`

Removing the printed id returns `FixtureNpmNetworkFailed`, `npm-code=2`, and `refill-volume-source=0`.

The extracted fixture script does not include `ensure_build_slots_broker` (`c590-remote.sh:990-994`), the other `docker network create`. That caller appends stdout to a log. The npm scenario's only create is the captured id at `4449`.

## Remaining uncertainties

- The TUnit host was not run. The bash mode it asserts was. No whole lane was run.
- Windows was not re-run. CARD-1210 already recorded this assertion as expected 1, actual 0, on the Windows LF base. The failing statements are the shared bash model.
- The id-printing copy lived under `/tmp` and was not committed. `cleanup-probe` and `deletion-audit` were not re-run. Their create path stays the existing `fail` only if the new printf stays outside that `if`.
- This pass did not open the CARD-1315 review transcript named on the card. The card text and this reproduction are the evidence used.
