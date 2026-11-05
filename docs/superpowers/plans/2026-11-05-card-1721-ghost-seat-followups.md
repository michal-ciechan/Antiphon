# CARD-1721 ghost seat release follow-ups

Original Code owner: `1c7946d0-8231-429b-9c5b-c420711258ce`.
Base: `9c13c6d42bab1281e409dcb24c09cae85a943a65`.
No Plan/TestDesign artifact was supplied; this records the brief's implementation and ordinary verification selection.

## Invariants and implementation

S1 commits the red-first witnesses. S2 repairs A-2 first: Failed seats require ten minutes since the latest of completion, transcript activity, session LastSeenAt and CodexTokenCountAt, and a fresh clean, origin-equal settlement record immediately before release. Re-read current task/session ownership and idle evidence after asynchronous work. LastSeenAt carries output/resume activity in AgentSessionRuntime and AgentSessionService. Preserve the existing Working, uncertain, sourced, busy, standing and coordinator fences.

S2 also changes the held attention wording, and introduces a five-attempt per-sweep release cap. Failed runner calls consume the attempt budget too. The cap applies to the ghost path; the qualified coordinator retains its independent policy and ownership. Ordinary witnesses use the real dispatcher, PostgreSQL and RunnerSlotService, with fake runner I/O; coordinator witnesses exercise both qualified and held outcomes with AutomaticEnabled true.

No changes to project/build inputs, timeouts, or startup configuration. No changed OS-selected behavior; production callers remain portable. Activation after future landing requires a server restart, not a runner restart. This task performs neither.

## Performance budget

Use the checkpoint tool; each build/run takes its own host slot. TEMP=/tmp and UseAppHost=false. Checkpoint cold output custody is tool-owned. Bootstrap the tool through build-slot once, native output; this is the one explicitly listed tooling build outside checkpoint rows.

Prior Review bbd446f6 measured the 139-case core selection at 373s plus a 278s native build on Linux (slot waits zero). S1 runs the red-first class; its final method-only row adds session and Codex activity variants after inspecting their production writers. S2 broadens coverage to every dispatcher retire caller class, attention seat class and coordinator class because shared pool retirement changed. CP-2/3 are provisionally budgeted 12 minutes each including cold build for CP-2 and database/host startup, body and teardown; phase durations remain available in TRX rather than invented measurements. Slot admission allowance is separate (45 minutes per acquisition). No repetition after green. No full integration assembly run.

The initial plan included a whole-Unit CP-4 from the generic Final profile. It was never executed and is withdrawn before ordinary qualification under the brief's explicit "The whole Unit lane is NOT required (CARD-1720 owns that defect)" exception. CARD-1720 documents no-TRX expiries at 30 and 60 minutes and owns the sharding repair. Whole Unit is not claimed passed; the four named classification/slow Unit guard classes remain required. CP-4 now names the new method-scoped red activity variants, not the withdrawn lane.

The assembly fixture owns PostgreSQL startup/migration once per run; each World uses an isolated cloned database and disposes it. RunnerSeatReleaseFixture owns its established isolated queue/HTTP fixture. No live external runner or provider call. Newly added tests add no timeouts. Existing fixture watchdogs are unchanged.

## Verification design

| ID | Witness / required outcome |
|---|---|
| V-1 | `GhostSeatReleaseTests.Completed_followup_resets_idle_until_ten_minutes_after_last_activity`: transcript/session/Codex activity variants with old completion hold at nine minutes, release at ten. Existing completion-bound witness proves completion remains the lower bound. |
| V-2 | `GhostSeatReleaseTests.Mirror_changed_after_candidate_read_keeps_the_seat`: dirty, unpushed and unknown persisted evidence committed after candidate read each prevents release. |
| V-3 | `GhostSeatReleaseTests.One_sweep_releases_at_most_five_seats_and_the_next_sweep_drains_the_remainder`: seven eligible seats release five then two, with distinct session identities. |
| V-4 | `GhostSeatReleaseTests.Held_attention_describes_conditional_release_and_preservation`: real attention row states ten idle minutes and preservation of dirty/unpushed/unknown. |
| V-5 | `GhostSeatReleaseTests.Automatic_coordinator_owns_both_hold_and_release_without_force_fallback`: switch true and OwnsAutomaticPath asserted; held sends zero, qualified sends one across two sweeps, force/stopper sends zero. A compatibility witness may already pass at base. |
| R-1 | Whole GhostSeatRelease, TaskSupervisionVisibility and RunnerSlotEndpoint classes: existing mirror, task status and custody behavior. |
| R-2 | All full dispatcher retire callers plus TerminalRunnerSeatReleaseTests: pool TTL/cap/standing/blocked/settlement/schedule coexistence. |
| R-3 | AttentionServiceTests and classification/slow guard classes. |
| R-4 | Not commissioned under the specific brief exception: whole Antiphon.Tests Unit lane. Never mark this passed. Named Unit guards remain R-3; Unit does not substitute for delivery, landing, lease or persistence integration coverage. |
| R-5 | Manual: callers table, OS/startup audit, evidence-diff over full base..HEAD, merge-tree against fetched origin/master, published task-ref readback. |

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | S1 | tests/Antiphon.Tests -> bin-c1721-red/ | red-witnesses | /*/*/GhostSeatReleaseTests*/* | V-1,V-2,V-3,V-4,V-5 | red assertions for V-1 through V-4; coordinator is a baseline compatibility witness | 17 | 7 | true |
| CP-2 | S2 | tests/Antiphon.Tests -> bin-c1721-green/ | ghost-and-pool | /*/*/(GhostSeatReleaseTests*)\|(TaskSupervisionVisibilityTests*)\|(RunnerSlotEndpointTests*)\|(AgentTaskPoolTests*)\|(BlockedTaskParkReleaseTests*)\|(GrokDelegateDispatchTests*)\|(AgentTaskCheckScheduleTests*)\|(AgentTaskSettlementRaceTests*)/* | V-1,V-2,V-3,V-4,V-5,R-1,R-2 | all listed, 0 failed | 19 | 12 | true |
| CP-3 | S2 | CP-2 | attention-coordinator-guards | /*/*/(AttentionServiceTests*)\|(TerminalRunnerSeatReleaseTests*)\|(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)/* | R-2,R-3 | all listed, 0 failed | 6 | 12 | true |
| CP-4 | S1 | CP-1 | red-activity-variants | /*/*/GhostSeatReleaseTests*/Completed_followup_resets_idle_until_ten_minutes_after_last_activity* | V-1 | all three activity variants fail the guarded idle assertion at baseline | 3 | 7 | true |

## Mutation handoff

All deliberate mutants remain pending for SourceLanding Mutation, including inherited CARD-1719 PC-1/2/3 (clean gate closed, dirty treated as clean, idle bound nine). New method-scoped candidates:

- PC-4 variants transcript/session/Codex: omit that activity from idle max -> V-1.
- PC-5 variants dirty/unpushed/unknown: omit immediate record refresh -> V-2.
- PC-6: remove the five-attempt cap -> V-3.
- PC-7: restore the old never-killed text -> V-4.
- PC-8 variants hold/qualified: bypass coordinator ownership / suppress coordinator call -> V-5 (no force fallback / no gap).

No deliberate mutant is executed by Code. Ordinary red-first failures are production-baseline witnesses, not Mutation credit. Mutation owns missing-control discovery, red/restore/green and reachability.
