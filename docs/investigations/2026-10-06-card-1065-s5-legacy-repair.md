# CARD-1065 S5 legacy answer repair

Code repair task: `3bd0dbd8-d86f-406a-8e3d-c5037b2eecad`.
Original landing owner: `86238d1e-f3c4-45e6-8089-0f45404d6d56`.
Base: `cb286ba373fa253378c099c197caa9b9a40f6a5b`.
Plan: [blocked task parking](../superpowers/plans/2026-10-05-card-1065-blocked-task-parking-plan.md).

The plan assigns new-park continuation to S7/S8. S5 must preserve historical
CARD-0667 answer recovery without admitting new publication-free Blocked releases.
Four legacy delivery/admission fixtures now seed historical release records with
parking off instead of creating new parks. Complete text, exact UserPrompt,
crash recovery, quota/admission and one-launch assertions remain intact. The
conditional-command assertion becomes zero because setup seeds history and sends
no new command. `Incomplete_settlements_never_authorize_release` now expects
`PublicationRequired` with zero release records/commands, as required by D-3.

Historical ambiguous actions exposed an additional regression: S5's publication
gate also prevented read-only reconciliation after a fresh absent/exited runner
inventory. The coordinator now distinguishes audit-only reconciliation (no
observation and therefore no ability to qualify or send) from action paths.
Only a historical attempt with no park can reconcile without publication. Every
extant same-attempt park retains its source gate, including the final locked
revalidation. Ownership, generation, readiness, delivery and Working checks remain.
New reservations and every path capable of sending still require publication.
Parking and automatic release remain default-off; deadlines are unchanged.

Review `7dd51ee4` supplied baseline red: 17 executed, 12 passed, 5 failed.
The test-first commit `89193e9527b36006b422f527fcb57c6153eb1a05`, with unchanged
production code, executed the full legacy class: 39 executed, 37 passed, 2 failed.
The new `Legacy_ambiguous_answer_requires_fresh_absence_without_another_release`
failed because fresh absence left the record Unresolved. The existing
`Accepted_answer_after_release_is_delivered_once` reached `release-ambiguous`
and failed with attempt 1 instead of 2. The other four reported failures passed.
One prior build failed on the new fixture's inventory DTO type (zero tests); it
was corrected before this assertion-red run and is not red-first evidence.

## Bounded verification

The explicit repair brief's no-whole-Unit selection controls this S5 dispatch.
Run the full `TerminalRunnerSeatReleaseTests` class (`/*/*/TerminalRunnerSeatReleaseTests/*`,
39 executions), CP-5 through the checkpoint tool (V-12/V-13/V-14, three executions),
and `/*/*/(TestClassificationGuardTests*)|(SlowTestTripwireTests*)/*` (three).
Legacy and registry selections are brief-authorized additions to the plan's closed
manifest. Each build/test uses the build-slot gate and exact committed source SHA;
the registry may reuse the qualified legacy output. No full-assembly run or
production acceptance/restart is commissioned. Other plan V/R and rollout work
remain with their owning slices; none is claimed passed here.

The full execution report, actual tested SHA, unedited CHECKPOINT lines and receipt
validation are stored in `.antiphon/task-3bd0dbd8.md`; generated payloads stay ignored.

## Pending SourceLanding Mutation

All plan PC-1 through PC-226 remain pending, including S5 PC-94 through PC-117.
This repair adds these method-scoped controls for Mutation to qualify/discover:

| ID | Guarded variation | Method |
|---|---|---|
| PC-S5R-1 | Require publication for audit-only historical recovery again; fresh absence/exit must fail to confirm. | `Legacy_ambiguous_answer_requires_fresh_absence_without_another_release` |
| PC-S5R-2 | Treat live inventory as absent. | Same method, `live` arm must remain Unresolved. |
| PC-S5R-3 | Treat unknown inventory as empty. | Same method, `unknown` arm must remain Unresolved. |
| PC-S5R-4 | Ignore accepted-start mismatch for an exited replacement. | Same method, `replacement` arm must remain Unresolved. |
| PC-S5R-5 | Ignore runner availability. | Same method, `unavailable` arm must remain Unresolved. |
| PC-S5R-6 | Ignore server Working during audit recovery. | Same method, `working` arm must remain Unresolved. |
| PC-S5R-7 | Exempt an extant park from publication during audit-only recovery. | `C1065_ReportPublicationPrecedesPhysicalRelease`, dirty lost-reply/BeforeResponse arms must remain ReleasePending. |

Use literal method filters under `TerminalRunnerSeatReleaseTests` for PC-S5R-1
through PC-S5R-6 and `BlockedTaskParkReleaseTests` for PC-S5R-7. No deliberate
mutants were run in Code. Ordinary assertion-red evidence does not discharge PCs.

Activation needs a server restart after Review/landing, owned by the caller's
orchestrator. This repair changes no runner runtime. S7/S8 and staged capability
qualification still gate parking activation; this report does not enable it.
