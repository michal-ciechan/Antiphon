# CARD-1657 Review round 1 (task ba9f47c2)

Verdict: Clean. Reviewed `6dbd129b791e0ca13b2cff1a0eb5930cd5af53e2..e216cc4af796ece143bfea1f9a1296fda33b0cb4`; tested SHA e216cc4af796ece143bfea1f9a1296fda33b0cb4 (Linux, server2 runner mirror).

## Findings

No defects.

1. A real land cannot be reported as uncertain. `/land/v2` maps to `QueueLandAsync`, which returns `Results.Accepted(..., LandRequestResult)` (`server/Api/Endpoints/AgentTaskEndpoints.cs:437-447`). The endpoint path builds that record only at `AgentTaskLandService.cs:326`, with `request.Id`. A new request gets `Guid.NewGuid()` (`NewRequest`, `:1613`). An idempotent re-submit (`requeued`) gets the existing row id from `EnsureRequestAsync`. Refusals are `ConflictException` (409) or `NotFoundException` (404), never a 2xx without an id. The JSON is web-default camelCase `requestId`, which is the name the script reads. Success text and exit 0 are unchanged; 144 Application DelegateScript tests are green, including the Queued and Requeued paths.
2. The remedy works. `-Status {2}` gets `$Land`, the task id the caller typed, not the request id. `-Status` prints `Land: <state>; request <id>` (`delegate.ps1:714-716`). No advisory.
3. The stub change is test-only. Only `DelegateScriptLandCompatibilityTests.C495_TransportFailureAfterPostIsUncertain` sets `V2AbortAfterRead`, and the new line sits inside that branch. On HTTP.sys, `ContentLength64` only stages a header and nothing is sent before `Abort()`, so the reset behaviour should be unchanged. That is reasoning only; Windows was not run here.

## Runs (one build-slot lease per build or run, native output)

| Run | Build | Total | Passed | Skipped | Failed |
|---|---|---|---|---|---|
| `Antiphon.Tests.Application/DelegateScript*` | tip | 149 | 144 | 5 | 0 |
| `Antiphon.Tests.Scripts/DelegateScript*` | tip | 12 | 11 | 0 | 1 (a) |
| `AgentTaskLandContractEndpointTests` + `DelegateScriptLandCompatibilityTests` | tip rebuilt after mutants | 39 | 39 | 0 | 0 |
| Mutant A: no-id guard removed (script only) | tip | 3 | 1 | 0 | 2: C1657 `()` and `({})`, ExitCode 0 not 1 |
| Mutant B: stub line reverted | rebuilt | 3 | 2 | 0 | 1: C495, "did not complete" missing (got the no-id message) |
| Mutants A+B (equivalent to 632cc72bd) | B build | 3 | 0 | 0 | 3: C495 ExitCode 0; C1657 x2 |

(a) `DelegateScriptBindDiagnosisTests.a_bind_400_with_an_unknown_served_build_is_diagnosed` is inherited. It is also red with the base `6dbd129b7` `delegate.ps1`; CARD-1655 fixes it.

Total across DelegateScript*: 161 tests, 155 passed, 5 skipped, 1 failed, which matches the worker. Every mutant was restored, and the worktree is clean.

## Merge and gates

- `git merge-tree --write-tree` is clean for: tip x CARD-1655 `f41d357de`; tip x CARD-1610 S4a `8bbad39c5`; tip x S4a fix `b72fa281b`; 1655 x S4a; 1655 x S4a fix; `origin/master` `30cb5279b` <- tip; `origin/rc/2026-11-01` `10a826b9d` <- tip.
- In the merged `delegate.ps1`, tip+S4a-fix and tip+1655 both keep the CARD-1657 guard and parse with 0 errors.
- `check-evidence-diff`: commits=2 entries=0 violations=0, exit 0.
- Land graph `-Check`: approved, count=69, digest=D92450D9AABCBF9C783B506F73DF06C45635F415D03E65C5F1A97AF415CC36BE (matches). `-Update` was not run.

## Advisory

- A-1: In `tests/linux-test-roster.json`, `directTestMethods: 24` and `sourceSha256` for `DelegateScriptLandCompatibilityTests` are stale. The class now has 25 methods, and the hash was already stale at base. Nothing enforces either field.

## Scope

The brief commissioned all DelegateScript* classes plus witnesses and mutants, and that ran in full. The Final profile's whole Unit lane did not run. The change is limited to the `delegate.ps1` land branch and one stub branch; the class is excluded from the Linux roster. That makes the scope Interim.
