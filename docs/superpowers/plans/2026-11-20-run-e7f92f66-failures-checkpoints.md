# Run e7f92f66 groups 71, 74, 84, and 86

Debug task `d591d78b`. Branch `feat/card-task-d591d78b`. Base master `810e7fa73dfd66fd11d40af19e33f0818ecfba9d` (fast-forward from the assigned `cec1ed34f6c0b547ead0eb945c18d19f05f9a039`). Mutation is waived (operator 2026-11-12). No PC rows. Short tests only. No deploy and no restart.

The why-missed note is `docs/investigations/2026-11-20-why-missed-run-e7f92f66-groups-71-74-84-86.md`.

## What changed

`DetachHostLoadAsync` drops an empty `AddLandVerificationFailure` addition before a tip rewind, and throws that migration's own message when a failure value is stored. Down is unchanged. The phone-home activity hold also intercepts `-- rt.activity.write`. The task-input HTTP host registers `AgentTaskGetPollMetrics` after its throwing placeholders. The nightly dedicated-clone fixture sets local `core.longpaths` true. The shared checkout and the linked worktree in that test are unchanged.

## Performance budget

CP-1's 15 minutes is the cold `Antiphon.Tests` build plus the held-notification class. Later rows reuse that build except the derived-index driver, which is its own build. No whole Unit lane. `WindowsUnitPlatformAdmissionTests` is not a row: on Linux it skips, and a skip is not a pass. The desktop rc rerun owns it. `C0824RefOnlyIdentityTests` is Slow with a 60-second class timeout; CP-5 is the one method that rewinds a tip clone. Cost floor is the sum of `EstimatedMinutes` (80).

## Unlisted runs

These are not checkpoint rows. The five red methods and the five green methods below ran through `scripts/build-slot.ps1` before this plan existed. `ReviewedSourceClean` stays false for those runs.

- Red, then green, at the fix tip: `C641_Upgrade_preserves_existing_held_notification_debt`, `Mode_round_trips_with_legacy_default`, `Slow_observer_is_attributed_without_payload`, `Input_body_is_absent_from_task_summary_events_and_logs`, `NightlyKeepsDedicatedCloneGuard`.
- `scripts/check-evidence-diff.ps1` is CP-10. Merge-tree against fetched `origin/master` is reported beside it and is not a row.
- Nightly `policyHash` and `PinnedInputDigest` are recomputed and reported. Re-pin only if a guard fails. These commits do not change policy inputs.

## Verification design

- V-1. `AgentTaskLandHeldNotificationTests` passes, including the C641 rewind. The migration Down guard is not edited.
- V-2. `PhoneHomeBackpressureTests` passes, including the slow observer, on the existing 20-second `PhoneHomeSignalWait`.
- V-3. `AgentTaskInputFallbackTests` passes, including the public GET that must stay 200 and must not contain the private tail.
- V-4. `WorktreeDefaultCallerTests` passes. The dedicated clone exits 0. The shared tree and the linked worktree still exit 3.
- V-5. `Mode_round_trips_with_legacy_default` passes. The other methods of that Slow class do not rewind a tip through `AddLandVerificationFailure`.
- V-6. The nine-class guard union passes, and C1729 stays `VIOLATIONS 0 LISTED 162`. `TestClassificationPolicyTests` is in this row once. `DerivedImpactIndexTests` is CP-8 alone, after the driver build. `WindowsUnitPlatformAdmissionTests` is omitted on purpose.
- R-1. `scripts/audit-land-verification-graph.ps1 -Check` exits 0.
- R-2. `scripts/check-evidence-diff.ps1` over `810e7fa73dfd66fd11d40af19e33f0818ecfba9d..HEAD` exits 0.

No PC-n is named. Mutation execution is waived and is not discharged.

Desktop filters for the orchestrator after run `e7f92f66`, even when Linux is green:

- `/*/*/AgentTaskLandHeldNotificationTests/C641_Upgrade_preserves_existing_held_notification_debt`
- `/*/*/C0824RefOnlyIdentityTests/Mode_round_trips_with_legacy_default`
- `/*/*/PhoneHomeBackpressureTests/Slow_observer_is_attributed_without_payload`
- `/*/*/AgentTaskInputFallbackTests/Input_body_is_absent_from_task_summary_events_and_logs`
- `/*/*/WorktreeDefaultCallerTests/NightlyKeepsDedicatedCloneGuard`

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | fix | tests/Antiphon.Tests -> bin-d591/ | held-note | `/*/*/AgentTaskLandHeldNotificationTests/*` | V-1 | 12 cases, 0 failed, 0 skipped | 12 | 15 | true |
| CP-2 | fix | CP-1 | phone-home | `/*/*/PhoneHomeBackpressureTests/*` | V-2 | 9 cases, 0 failed, 0 skipped | 9 | 10 | true |
| CP-3 | fix | CP-1 | task-input | `/*/*/AgentTaskInputFallbackTests/*` | V-3 | 13 cases, 0 failed, 0 skipped | 13 | 8 | true |
| CP-4 | fix | CP-1 | nightly-clone | `/*/*/WorktreeDefaultCallerTests/*` | V-4 | 8 cases, 0 failed, 0 skipped | 8 | 8 | true |
| CP-5 | fix | CP-1 | ref-only-rewind | `/*/*/C0824RefOnlyIdentityTests/Mode_round_trips_with_legacy_default` | V-5 | 1 case, 0 failed, 0 skipped | 1 | 5 | true |
| CP-6 | fix | CP-1 | guards | `/*/*/(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)\|(TestLaneCategoryGuardTests*)/*` | V-6 | 81 cases, 0 failed, 0 skipped | 81 | 10 | true |
| CP-7 | fix | n/a | derived-driver | `dotnet build scripts/regenerate-derived-impact-index/Antiphon.DerivedImpactIndex.csproj -p:UseAppHost=false --nologo` | V-6 | exit 0, 0 errors | n/a | 8 | true |
| CP-8 | fix | CP-1 | derived-index | `/*/*/DerivedImpactIndexTests/*` | V-6 | 27 cases, 0 failed, 0 skipped | 27 | 5 | true |
| CP-9 | fix | n/a | land-graph | `pwsh -NoProfile -File scripts/audit-land-verification-graph.ps1 -Check` | R-1 | exit 0 | n/a | 8 | true |
| CP-10 | fix | n/a | evidence-diff | `pwsh -NoProfile -File scripts/check-evidence-diff.ps1 -BaseRef 810e7fa73dfd66fd11d40af19e33f0818ecfba9d -HeadRef HEAD` | R-2 | exit 0 | n/a | 3 | true |

CP-6 is TestClassificationPolicyTests 42, TestClassificationGuardTests 1, SlowTestRegistryMergeTests 5, SlowTestTripwireTests 2, NativeLinuxPolicyWitnessTests 3, MechanicalPcContractTests 11, TaskPlatformGuidanceTests 5, C1729LayeringGuardTests 11, TestLaneCategoryGuardTests 1. `RunnerDefaultGuidanceTests` shares a file with `TaskPlatformGuidanceTests` and is outside this filter.
