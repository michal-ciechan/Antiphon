# CARD-0466 Fresh/resume ownership gap fixed

Fixed the P1 reproduced in `2026-09-10-card-0466-review-a13f5c6f.md`. Final focused verification: **124 executed, 124 passed, 0 failed, 0 skipped**. Return to Review.

Baseline: `96570320` in the shared checkout `C:\src\Antiphon`. Implementation and test commits: `9a80aedb`, `a4615985`, `abff2f7e`. The final run exercised `abff2f7e` with all temporary mutations restored; this report is a subsequent documentation-only commit.

## Changes and guarantees

- `AgentControlService.cs`: includes the chosen Fresh target in the ordered session lock set, even before its row exists. Its delivery lock remains held through reservation commit and enqueue. Stop can record revocation after commit; a competing resume cannot advance the target until launch ownership is registered. Existing finally cleanup releases acquired locks on success or rollback. Runner preflight remains outside the transaction.
- `AgentSessionLaunchQueue.cs`: requires the accepted `StartedAt` at every cardless enqueue and carries it into the worker. Both `AgentTaskDispatcher.cs` call sites now supply their accepted generation.
- `AgentSessionService.cs`: compares the queued generation before adapter creation, carries it through subsequent authorization checks, and uses it again under the failure-evidence row locks. PostgreSQL microsecond precision is normalized explicitly. A superseded failure settles without changing newer session/agent outcomes, continuity holds, or raising a queued-launch alert against the newer generation. Direct synchronous callers can still capture the generation on entry.
- `docs/session-runtime-invariants.md`: records the commit-to-enqueue and immutable-generation contract.

The retained review reproducer is permanent as `StandingSessionSwitchConcurrencyTests.CommitGap.cs`. It still gates immediately after Fresh commits and before enqueue. Because a fixed implementation prevents the original successful intervening resume, it now verifies the target lock, issues the competing resume while paused, then lets the revoked worker settle and verifies resume-mode launch. It also explicitly redelivers the old Fresh work after the resumed generation completes, checking that no obsolete adapter launches or overwrites the winner.

`StandingSessionSwitchConcurrencyTests.Generation.cs` adds six cases: newer Running, Failed and Stopped outcomes, with obsolete work delayed either before launch or during readiness. Assertions cover adapter creation/start/cleanup, input, accepted generation, completion/failure/termination fields, agent pointer/status, continuity hold, queued messages and alerts. The in-flight cases deliberately advance a row while the fake adapter is gated to exercise failure merging; the separate commit-gap test verifies that ordinary Start serializes the real competing requests.

The existing ownership test supplies the new mandatory enqueue generation. Two specialist Stop cases initially failed because their automatic probe explicitly requested Fresh, which the existing operator-only guard rejects before specialist intent is checked. `SpecialistStartIntentTests.cs` now probes ordinary automatic resume and retains the expected `specialist_start_refused` assertion. No production refusal policy changed for that adjustment.

## Verification

Every row below has a fresh TRX under `C:\src\Antiphon\tests\Antiphon.Tests\bin-3904d52e\TestResults\`. Logs with the same stem are under `C:\src\Antiphon\.antiphon\` with `.log` extensions.

| Evidence stem | Executed | Passed | Failed | Skipped | Result |
|---|---:|---:|---:|---:|---|
| `3904d52e-baseline` | 1 | 0 | 1 | 0 | Exact unmodified retained reproducer against baseline production; `obsolete.Started` was True. |
| `3904d52e-fixed` | 1 | 1 | 0 | 0 | Initial adopted regression green. |
| `3904d52e-regression` | 124 | 122 | 2 | 0 | Only the two specialist automatic-Fresh probe mismatches described above. |
| `3904d52e-boundaries` | 28 | 28 | 0 | 0 | Strengthened concurrent-resume regression and corrected specialist probes. |
| `3904d52e-generation-red` | 1 | 0 | 1 | 0 | Temporarily omitted accepted generation at queue-to-worker handoff; `obsolete.Started` became True. |
| `3904d52e-generation-green` | 1 | 1 | 0 | 0 | Restored handoff; same exact method green. |
| `3904d52e-evidence-red` | 6 | 3 | 3 | 0 | Temporarily removed failure-generation guard; all three in-flight cases overwrote newer status or failure evidence. Pre-launch cases remained protected. |
| `3904d52e-evidence-green` | 6 | 6 | 0 | 0 | Restored guard; same exact method and all arguments green. |
| `3904d52e-final` | 124 | 124 | 0 | 0 | Final combined regression; build succeeded. |

Final TRX class counts were checked: StandingSessionSwitchConcurrencyTests 24; AgentControlServiceIntegrationTests 31; AgentSessionLaunchFailureTests 52; AgentSessionLaunchQueueOwnershipTests 3; SpecialistStartIntentTests 4; GrokRulesReadyOrderingTests 2; BootLivenessProbeScopeTests 8.

The two new controls used exact method filters for both red and restored-green runs. Restoring source rewrote its last-write time. `C:\src\Antiphon\.antiphon\3904d52e-restored-build.json` records the rebuilt server DLL hash, checks that it differs from the evidence-mutation DLL, and verifies that it is newer than restored source. No temporary source mutation remains. The 14 alternate output directories are retained and listed in `C:\src\Antiphon\.antiphon\3904d52e-output-inventory.txt`.

Rerun the final focused regression from `C:\src\Antiphon`:

```powershell
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-3904d52e/ -- --treenode-filter '/*/Antiphon.Tests.Application/(StandingSessionSwitchConcurrencyTests*)|(AgentControlServiceIntegrationTests*)|(AgentSessionLaunchFailureTests*)|(AgentSessionLaunchQueueOwnershipTests*)|(SpecialistStartIntentTests*)|(GrokRulesReadyOrderingTests*)|(BootLivenessProbeScopeTests*)/*' --report-trx --report-trx-filename 3904d52e-review.trx
```

For the two new methods separately, use `/*/*/StandingSessionSwitchConcurrencyTests/Delayed_fresh_enqueue_cannot_launch_over_a_later_resumed_generation` and `/*/*/StandingSessionSwitchConcurrencyTests/Obsolete_queued_launch_cannot_overwrite_a_newer_outcome` as exact filters.

This follow-up extends V-21/V-22 and validates the reproduced gap with fake adapters and the test PostgreSQL fixture. It does not rerun the entire prior V-01-V-28/PC-1-PC-7 matrix, client suite or E2E. No production stack restart, live provider launch or provider-home access occurred. Interrupted-startup durability remains excluded as instructed.

Next: Review the accepted-generation propagation, Fresh target lock lifetime, superseded failure handling, and retained regression evidence.
