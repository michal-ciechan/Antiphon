# CARD-1111 Final Review report — task d99dc93f

Verdict: clean. No defect, no regression, no new reachable exposure. Ordinary scope ran in full at the reviewed SHA and is green. Disclosures filed as Backlog card CARD-1123 (`566ce84b-fe07-4c0c-8ccb-05d19b17b3ac`).

subjectTaskId: 4a3bdb78-f464-4ba2-aff9-da5af00e6593 (Code owner and landing owner, unchanged).
Reviewed source: `2abcee945777873267be5e0d43c66bc36e0b60a7` on `refs/heads/feat/card-task-4a3bdb78`, three commits fast-forward from `3599aa4b5d401112980c368662d476d49e46dff9`. Diff reviewed against that base: 7 files, +229/-17.

## Rerun (one checkpoint-tool run, serial, one isolated build)

Manifest: hand-made five-row table (scratchpad, outside the repo). Driver built via `scripts/build-slot.ps1` into `bin-c1111drv/` (slot granted, waited 15 s), then `start --plan ... --serial --expected-source-sha 2abcee945...`. Run `20261006-213528-1837`, host Debian 12, source clean, buildSource verified. Wall 6m36s, build 128 s, UseAppHost=false, `unlisted: none`. Green run deleted `bin-c1111rv/`; the driver `bin-c1111drv/` was removed by a root-confined rm after `validate`.

```
CHECKPOINT CP-1 commit=2abcee945777873267be5e0d43c66bc36e0b60a7 build=ok filter=/*/Antiphon.Tests.Application/HostOccupancySampleEndpointTests/* executed=9 passed=9 failed=0 skipped=0 trx=/work/worktrees/task-d99dc93f/.antiphon/checkpoints/20261006-213528-1837/rows/CP-1/run.trx slot=granted waited=15s dirty=0 source=2abcee945777873267be5e0d43c66bc36e0b60a7 sourceState=clean buildSource=verified
CHECKPOINT CP-2 commit=2abcee945777873267be5e0d43c66bc36e0b60a7 build=reused filter=/*/Antiphon.Tests.Application/SeatOccupancySamplerTests/* executed=9 passed=9 failed=0 skipped=0 trx=/work/worktrees/task-d99dc93f/.antiphon/checkpoints/20261006-213528-1837/rows/CP-2/run.trx slot=granted waited=0s dirty=0 source=2abcee945777873267be5e0d43c66bc36e0b60a7 sourceState=clean buildSource=verified
CHECKPOINT CP-3 commit=2abcee945777873267be5e0d43c66bc36e0b60a7 build=reused filter=/*/Antiphon.Tests.Application/HostEndpointTests/* executed=4 passed=4 failed=0 skipped=0 trx=/work/worktrees/task-d99dc93f/.antiphon/checkpoints/20261006-213528-1837/rows/CP-3/run.trx slot=granted waited=0s dirty=0 source=2abcee945777873267be5e0d43c66bc36e0b60a7 sourceState=clean buildSource=verified
CHECKPOINT CP-4 commit=2abcee945777873267be5e0d43c66bc36e0b60a7 build=reused filter=/*/Antiphon.Tests.Application/SeatOccupancyAttentionTests/* executed=9 passed=9 failed=0 skipped=0 trx=/work/worktrees/task-d99dc93f/.antiphon/checkpoints/20261006-213528-1837/rows/CP-4/run.trx slot=granted waited=0s dirty=0 source=2abcee945777873267be5e0d43c66bc36e0b60a7 sourceState=clean buildSource=verified
CHECKPOINT CP-5 commit=2abcee945777873267be5e0d43c66bc36e0b60a7 build=reused filter=/*/*/(TestClassificationGuardTests*)|(SlowTestTripwireTests*)/* executed=3 passed=3 failed=0 skipped=0 trx=/work/worktrees/task-d99dc93f/.antiphon/checkpoints/20261006-213528-1837/rows/CP-5/run.trx slot=granted waited=0s dirty=0 source=2abcee945777873267be5e0d43c66bc36e0b60a7 sourceState=clean buildSource=verified
verdict: GREEN exit=0
```

`validate --evidence report.json --expected-source-sha 2abcee945...`: `CHECKPOINT SOURCE VALID source=2abcee945777873267be5e0d43c66bc36e0b60a7 rows=5`, exit 0.

Executed identities (from the TRX files) include all five new methods: `C1111_Legacy_inventory_reason_is_error_without_the_stored_text`, `C1111_Limit_one_and_exact_cap_apply`, `C1111_Non_numeric_or_overflowing_limit_is_400_not_500`, `C1111_Reversed_window_is_200_with_no_rows` (endpoint class, 9 distinct methods) and `C1111_Disconnected_phone_home_reason_is_unavailable_phone_home` (sampler class, 9 distinct methods). Counts match the Code report (9/9, 9/9, 4/4, 9/9, 3/3).

Evidence guard: `scripts/check-evidence-diff.ps1 -BaseRef 3599aa4b5... -HeadRef 2abcee945...` → `EVIDENCE result commits=3 entries=0 violations=0`, exit 0.

Platform read before launching: `GET /api/runner-defaults` (global runner server2), `GET /api/session-runners` (desktop, server2, server2-temp all available), build-slot broker budget 4 with 3 occupied. No `-Runner` pin was needed.

## Checkpoint audit against the Code report

CARD-1111 has no plan file and no `### Checkpoints` table; the brief's closed class list is the ordinary scope and the Code report says so. The Code report's CP lines carry nonzero counts, one isolated build (`bin-c1111/`, UseAppHost=false), `-NoBuild` reuse rows, slot granted on every row, and a stated reason for every extra run: the red-first run on base (dirty tree, separate output `bin-c1111-red/`, failed=1 as expected), the superseded 8/1 endpoint run at `c2fa03c0b`, and the compile miss at `54288ab8b` (exit 2, no test result, same repair). Both output trees reported deleted. The whole-Unit lane was not run by Code or by this review; the brief replaces the Final profile's whole-Unit sentence with the closed class list ("no whole-Unit"), so `ordinaryScopeCompleted: Full` is capped by that commissioned scope.

## Hard checks from the brief

(a) Sanitise-on-read is a closed allow-list. `HostEndpoints.PublishedInventoryReason` is an exact, case-sensitive, untrimmed `switch` on the four tokens, `null => null`, `_ => "error"`. The stored text has no other path out: the route logs nothing; the only readers of the stored `InventoryReason` column are `HostEndpoints.ToDto` and the sampler's own write (`ToRow`); attention evidence (`AttentionService.Seats.DivergenceEvidence`) carries `inventory={InventoryState}` only; no slots or `/api/hosts` route reads samples; the client never reads `inventoryReason`. The write side is unchanged: `SeatOccupancySampler.RenderInventoryReason` only swapped the literal `"phone-home runner unavailable"` for `RunnerInventoryReasons.PhoneHomeUnavailable` with the same value, still trims, still maps unknown text to `error` and logs a truncated detail at Warning (CARD-1101 behaviour). `PhoneHomeRunnerDirectory.GetInventoryAsync` still emits raw `ex.Message` into `RunnerInventory.Unavailable` for RPC failures, which the sampler maps to `error` before storage, so the test's "unavailable " (trailing space) and secret-text rows are the right negatives.

(b) The legacy C1079 change is minimal: only the two asserted seeds and their expected value moved from `listed ok` to the legal category `error`; newest-first order, the two `AssertPopulated` calls, the out-of-window and other-host exclusions and the POST 405 check are untouched. `Sample` gained a nullable reason and a `state` parameter (default `listed`) for the new legacy test. No assertion weakened; the asserted value is still exact.

(c) Non-numeric limit. The empty 400 is real: `PhoneHomeTestHost` runs as `Testing`, and `RouteHandlerOptions.ThrowOnBadRequest` defaults to true only in Development, so outside Development minimal APIs write a bodiless 400 and `ExceptionMiddleware` never sees a `BadHttpRequestException`. The live desktop server is the AppHost, which pins `ASPNETCORE_ENVIRONMENT=Development` (`Antiphon.AppHost/Program.cs`), so production-as-deployed returns the problem body the test and the doc describe. The Docker `antiphon` service sets no environment (Production) and would return a bare 400. Nothing in `server/Program.cs` pins the option, so no neighbouring route returns a body outside Development; the only other `int?` query route (`/api/expectation-watchdog`) has no bind-failure test. Per the verdict policy this is not a defect: the bare 400 is the framework default and the test-host override mimics the environment the server actually runs in. It does hide the Production difference from the test and the doc sentence is unqualified, so it is disclosed on CARD-1123 with the preferred close (pin `ThrowOnBadRequest = true` once in `server/Program.cs`) and the alternative (qualify the doc). The test comment "An unset environment is Production" describes the default, not the `Testing` host; cosmetic, noted on the card.

(d) Reversed-window early return (`HostEndpoints.cs:59-60`). Reachable, not dead, but observationally equivalent to the fall-through: the `Where` on `SampledAt >= windowFrom && SampledAt <= windowTo` is empty for `windowFrom > windowTo` and the response carries the same `limit`, `from` and `to`. A mutant that only deletes the branch is an equivalent mutant; `C1111_Reversed_window_is_200_with_no_rows` can only kill a 400 or a returned seeded row, as the Code report already states. Judgement: remove the branch (the query already yields no rows) or record the deletion as an equivalent mutant where the PC list lives; disclosed on CARD-1123, not a defect.

(e) Docs match the code for `limit=1`/`limit=2000`, `limit_invalid`, `window_invalid`, the reversed window (200, `samples: []`, requested bounds), the four wire categories, the `phone-home runner unavailable` token and the legacy-row mapping. The one environment-dependent sentence is item (c). No assertion weakened anywhere in the diff.

New tests can go red: the legacy test failed first on base (Code receipt `c1111-red`, failed=1); the non-numeric test failed on the empty 400 before the host override; limit-one/cap asserts echoed limit, counts and order; reversed-window asserts 200, zero rows, echoed bounds and default limit; the phone-home sampler test drives the real `PhoneHomeRunnerDirectory` disconnected path and asserts the stored category and the absence of the raw token.

PCs: the five method-scoped controls in the Code report stay pending for SourceLanding Mutation; this review discharges none. Control 4's note (equivalent mutant on branch deletion) is correct.

Delivery/queue audit items in the stage bundle (producer/recipient, UserPrompt transcript evidence) do not apply: this change is a read-only HTTP projection with no queue, delivery or session path.

## Review branch

Review is read-only; no source change. This report is the only commit on `feat/card-task-d99dc93f`.
