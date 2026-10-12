# CARD-0995: ordinary guard for the temp-host absence check

Date: 2026-10-12. Code task on `feat/card-task-63021462`.
Base: `d8188da496b2369e9ee688f8977c4888510fbb9f`.
The live card was read with `scripts/card.ps1 get CARD-0995 -Board Antiphon`.

## Decision

The host absence check is already in `scripts/c590-remote.sh`. No production edit.

- `case_deploy_temp_runner` calls `c849_no_temp_containers` before `ensure_checkout` and before `compose_temp up`.
- `c849_no_temp_containers` refuses `CacheDonorLookupFailed` when `docker ps` fails and `CacheTempContainerExists` when the temp-project listing is non-empty, including a stopped id or any other text.
- `c849_seed`'s cold-ready arm (`if [ "$C849_KIND" = cold ]`) calls that same check before it writes `ready=true kind=cold`.

No ordinary TUnit guard pins either call. `C849_Deploy_prepares_and_verifies_before_acceptance` and `C849_Deploy_temp_names_saved_import_when_seed_is_missing` stub `c849_no_temp_containers() { :; }`. `C849_Deploy_ordering_contract_is_pinned` does not mention the call. The ready-marker refusal in `C849_Seed_publishes_complete_payloads_before_its_marker` (host-refusal=`CacheTempContainerExists`) stubs `c849_require_ready` and uses a non-cold marker, so it hits the empty-donor `else`, not the cold-ready arm. Deleting only the cold-ready call would still refuse through that `else`. The real-Docker fixture `scripts/fixtures/c973-rolling-host-cases.mjs` is outside this lane.

S2 adds two Unit methods on `RemoteScriptContractTests`. They read the script and run it under bash with a docker stub. They do not start Docker. Outcomes are a handwritten table. A row the table does not name fails the test. The house brief forbids a broad Unit lane; CP-1 is the two new methods only.

## Files

| Slice | File | Change |
|---|---|---|
| S1 | `docs/superpowers/plans/2026-10-12-card-0995-absence-guard.md` | This note. |
| S2 | `tests/Antiphon.Tests/Scripts/RemoteScriptContractTests.cs` | `C0995_Deploy_temp_checks_host_absence_before_checkout`, `C0995_Cold_ready_seed_refuses_a_temp_container`. |

## Tests

| Case | Mutation | Red |
|---|---|---|
| V-1 deploy order | Delete `c849_no_temp_containers` in `case_deploy_temp_runner`, or move it below `ensure_checkout` | `C0995_Deploy_temp_checks_host_absence_before_checkout`: the command index is missing or no longer before checkout, and `present` reaches checkout instead of `CacheTempContainerExists`. |
| V-1 deploy census | In `c849_no_temp_containers`, change `[ -z "$listed" ] \|\| write_result false CacheTempContainerExists 2` to `[ -z "$listed" ] \|\| true` | `present`, `stopped`, and `garbage` accept `GuardPassed` instead of `CacheTempContainerExists`. `ps-error` still refuses `CacheDonorLookupFailed`. |
| V-2 cold arm | Delete `c849_no_temp_containers` inside `c849_seed`'s cold-ready arm, or move it below the cold receipt `printf` | `C0995_Cold_ready_seed_refuses_a_temp_container`: the arm's call index fails. A move also leaves `seed.txt` on `present`. |
| V-2 cold census | Same weakening as V-1 | `present`, `stopped`, and `garbage` write `ready=true kind=cold writable=3` instead of refusing `CacheTempContainerExists`. |

Handwritten rows, both methods: `absent`, `present`, `stopped`, `garbage`, `ps-error`. `garbage` is a non-empty listing that is not a container id and must refuse `CacheTempContainerExists`. An unlisted mode fails closed.

## Verification design

V-1. `C0995_Deploy_temp_checks_host_absence_before_checkout` — one `c849_no_temp_containers` before `ensure_checkout` and before `compose_temp up`; shell rows match the handwritten deploy outcomes; checkout and compose stay at 0 on every refusal.

V-2. `C0995_Cold_ready_seed_refuses_a_temp_container` — the single cold-ready arm calls `c849_no_temp_containers` before `ready=true kind=cold` and before `write_result true`; shell rows match the handwritten cold outcomes; `seed.txt` exists only for `absent`, and `c849_optional_donor` is not probed.

### Positive controls (pending)

Post-land Mutation was not run. These stay pending for method-scoped SourceLanding. Code's red-first runs are not that pass.

| PC | Mutation | Detecting filter |
|---|---|---|
| PC-1 | Delete the `case_deploy_temp_runner` call | `/*/*/RemoteScriptContractTests/C0995_Deploy_temp_checks_host_absence_before_checkout` |
| PC-2 | Move that call below `ensure_checkout` | `/*/*/RemoteScriptContractTests/C0995_Deploy_temp_checks_host_absence_before_checkout` |
| PC-3 | Weaken `c849_no_temp_containers` to ignore a non-empty listing | `/*/*/RemoteScriptContractTests/C0995_*` |
| PC-4 | Delete the cold-ready call in `c849_seed` | `/*/*/RemoteScriptContractTests/C0995_Cold_ready_seed_refuses_a_temp_container` |
| PC-5 | Move that call below the cold receipt `printf` | `/*/*/RemoteScriptContractTests/C0995_Cold_ready_seed_refuses_a_temp_container` |

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | S2 | `tests/Antiphon.Tests -> bin-c0995/` | absence-guard | `/*/Antiphon.Tests.Scripts/RemoteScriptContractTests/C0995_*` | V-1, V-2 | Exactly the two C0995 methods; 2 executed, 0 failed/skipped | 2 | 12 | true |
