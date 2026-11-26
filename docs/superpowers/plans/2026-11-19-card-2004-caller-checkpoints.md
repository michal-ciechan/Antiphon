# CARD-2004 caller-label checkpoints (Code task fa472ac7)

`scripts/delegate.ps1`, `scripts/status-snapshot.ps1`, `scripts/checkpoint-task.ps1`, and
`scripts/card.ps1` send their registered `X-Antiphon-Caller` label on every server call.
`scripts/lib/antiphon-caller.ps1` trims and lowercases the value and refuses anything that
is not in `AgentTaskGetPollMetrics.RegisteredLabels`. The refused value is not copied into
the header or the error. No new server label. `board-watch` stays registered and has no
script. The client `useAgentTask` drawer is unchanged and still sends no header.

Scripts that call the server and are not in that registry are unchanged:
`runner-drain.ps1`, `runner-slots.ps1`, `deploy-server2.ps1`, `verify-phone-home-grok.ps1`,
`c1609-manual.ps1`, and `postgres-cutover-monitor.ps1`. `start-windows-phone-home-runner.ps1`
and `scripts/lib/windows-phone-home-env.ps1` do not call the server API.

No migration. No positive-control row. Mutation execution is waived (operator 2026-11-12)
and is not discharged. Short tests only (operator 2026-11-18).

`WindowsUnitPlatformAdmissionTests` is not a row. The class is untouched, and
`C1210_Windows_linux_only_outcomes_are_exact` skips on Linux. A skipped result is not a
clean checkpoint certificate. `scripts/impacted-classes.ps1` on this diff selected no class.

The red witnesses ran once, unlisted, at `470a9191c2f2973908111e9ea269cdcedac35d67`
(filter `/*/*/RegisteredCallerScriptTests/*`, build slot `c2004-caller-red-build` waited=0s,
run slot `c2004-caller-red-run` waited=0s): executed 5, failed 5, passed 0. The four script
rows failed because `X-Antiphon-Caller` was absent. The refusal row failed because
`scripts/lib/antiphon-caller.ps1` did not exist. This table is the green closed list.

## Verification design

- V-1. `Delegate_status_sends_its_registered_caller_label`: `delegate.ps1 -Status` sends `delegate.ps1` on `GET /api/agent-tasks/{id}`.
- V-2. `Status_snapshot_sends_its_registered_caller_label`: every status-snapshot GET, including the task detail, sends `status-snapshot.ps1`.
- V-3. `Checkpoint_task_sends_its_registered_caller_label`: the task GET sends `checkpoint-task.ps1`.
- V-4. `Card_limits_sends_its_registered_caller_label`: `card.ps1 -Limits` sends `card.ps1`.
- V-5. `Unregistered_caller_label_is_refused_and_not_echoed`: the helper's list matches the server registry; `runner-drain.ps1`, `phone-home`, and a blank value are refused and not echoed.
- R-1. Policy sweep.
- R-2. Guard, tripwire, native Linux, mechanical contract, platform guidance, layering, and lane.
- R-3. Slow-registry merge.
- R-4. Derived-impact index after the witness class is recorded.
- R-5. Land graph, evidence diff, pin digest, and merge-tree.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---|---|---|---|
| CP-1 | S1 | tests/Antiphon.Tests -> bin-c2004call-a/ | c2004call-witness | `/*/*/RegisteredCallerScriptTests/*` | V-1, V-2, V-3, V-4, V-5 | 0 failed, 0 skipped, executed 5 | 5 | 15 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-2 | S1 | CP-1 | c2004call-policy | `/*/*/TestClassificationPolicyTests/*` | R-1 | 0 failed, 0 skipped, executed at least 42 | 42 | 5 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-3 | S1 | CP-1 | c2004call-guards | `/*/*/(TestClassificationGuardTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)\|(TestLaneCategoryGuardTests*)/*` | R-2 | 0 failed, 0 skipped, executed at least 34 | 34 | 8 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-4 | S1 | CP-1 | c2004call-slow | `/*/*/SlowTestRegistryMergeTests/*` | R-3 | 0 failed, 0 skipped, executed 5 | 5 | 5 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-5 | S1 | CP-1 | c2004call-index | `/*/*/(DerivedImpactIndexTests*)\|(DerivedImpactIndexInvariantTests*)/*` | R-4 | 0 failed, 0 skipped, executed at least 27 | 27 | 15 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-6 | S1 | n/a | c2004call-land | `pwsh -NoProfile -File scripts/audit-land-verification-graph.ps1 -Check` | R-5 | exit 0 | n/a | 5 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-7 | S1 | n/a | c2004call-evidence | `pwsh -NoProfile -File scripts/check-evidence-diff.ps1 -BaseRef 5be178005f06aa868bbb038e561c2ef22a654cb5 -HeadRef HEAD` | R-5 | exit 0 | n/a | 3 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-8 | S1 | n/a | c2004call-pins | `pwsh -NoProfile -File scripts/test-live-lane-run.ps1 -PrintInputDigest` | R-5 | exit 0 | n/a | 5 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-9 | S1 | n/a | c2004call-merge | `git fetch origin master && git merge-tree --write-tree origin/master HEAD` | R-5 | exit 0, no conflict | n/a | 5 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |

### Cost

Ordinary floor is 66 minutes (sum of EstimatedMinutes). Derived total timeout is 142 minutes.
CP-1 and CP-5 deadlines are 45 minutes. No row's `3 * EstimatedMinutes` exceeds the 45-minute advisory.
