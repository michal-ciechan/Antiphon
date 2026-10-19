# CARD-0907 reduced compatibility execution

Code task and landing owner: `ddd05d7e-7e18-40e5-b3c9-70233ef03320`.
Base: `b5e08211edff926f8e5e18cc2a6d644611f9f766`. Target: master only.
Branch: `feat/card-task-ddd05d7e`. Restart: none.

The caller's reduced brief supersedes parser-repair S1/S2 in the original plan
and TestDesign `fa25211a188944e5acd1d41628ab1806eb686fda`:
`docs/superpowers/plans/2026-10-19-card-0907-codex-0160-test-design.md`.
S0 proved the existing working predicate recognizes the captured row. Its required
assertion-red against that predicate is not a real defect and is not manufactured.
No working, readiness, delivery, timeout or normalizer behavior changes are selected.

## Verification design

V-1: three fixture tests replay independently frozen idle/running/done grids in three
chunkings and pin exact byte hashes. V-2: direct current positive and labelled
negative controls. V-3: deterministic lifecycle plus captured adapter fallback.
V-5: current working blocks startup; current idle settles after working; derived
layout negatives keep the existing readiness boundary. V-7: a derived originator
control over the existing historical thread-item fixture, explicitly not the
missing current native rollout/usage witness. R-1 through R-4 run the complete
named compatibility classes. V-8 adds the captured SubmitEvidence held-body witness; adapter confirmation remains unchanged regression.

V-4: one additional in-place uid-1654 PTY capture sends a second prompt while the
first is Working. Bind a distinct queue token only if actually observed; otherwise
record absence and stop. V-6: current modal binding only from existing captures;
missing current accept/back/sign-in/update payloads stay gaps. No extra modal probe.
V-9/R-5: excluded from reduced scope because no shared predicate or queue behavior
changes; all native delivery, caller receipt and persistence acceptance stays open.
No integration class is affected by a behavior change. All old Q variants remain
unexecuted, not passed. This is compatibility coverage, not full CARD-0907 acceptance.

Final requires the whole Antiphon.Tests Unit lane in addition to narrow classes;
CP-10 records that stage-mandated selection despite the reduced brief's no-suite
preference. No full assembly is selected. The cross-cutting regression invariant
is existing Unit behavior; no production class has unbounded changed impact.
Budget: 12 minutes for Unit, 22 minutes for narrow rows, plus one renderer and one
checkpoint-tool bootstrap build (about 2 minutes each) and <=120s startup/<=120s
turn observation/<=5s cleanup for the single manual capture. No repeated paid probe.

All PC-1 through PC-73 and every original case/variant remain pending SourceLanding
Mutation, including out-of-scope or unbound controls. No PC is discharged by ordinary
green. PC-1/2/3 bind the new fixture methods and payload/hash/frame assertions;
PC-4/5/6/7/8/9/10/11 bind direct working/lifecycle tests; PC-51 binds the derived
originator control. Other original method bindings remain pending or historical.
Mutation owns deliberate defects, intended assertion-red, restoration and fresh green.

### Checkpoints

The reduced table replaces the original allocation; omitted CP-9 is excluded queue
recovery, never a zero-count pass. CP-4 binds the actually observed legacy queue token. Bootstrap via
build-slot.ps1, then run the checkpoint tool with committed expected-source-sha.
All rows are serial; each run retains clean source/build receipts and fresh TRX.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | all | `tests/Antiphon.Agents.Pty.Tests -> bin-c907-fixtures/` | fixture-replay | `/*/*/CodexV0160FixtureTests/*` | V-1 | all 3 methods, 0 failed/skipped | 3 | 3 | true |
| CP-2 | all | `tests/Antiphon.Tests -> bin-c907-working/` | working-witness | `/*/*/CodexWorkingIndicatorTests/C907_submitted_frame_is_working` | V-2 | 1 executed, 0 failed/skipped | 1 | 8 | true |
| CP-3 | all | CP-2 | working-lifecycle | `/*/Antiphon.Tests.Agents/(CodexWorkingIndicatorTests*)\|(RunnerCodexAdapterTurnCompleteTests*)/*` | V-2, V-3, R-1 | all 18 results, 0 failed/skipped | 18 | 2 | true |
| CP-4 | all | CP-1 | queue-witness | `/*/*/CodexV0160StartupTests/C907_running_queue_footer_blocks_ready` | V-4 reduced | 1 executed, 0 failed/skipped | 1 | 1 | true |
| CP-5 | all | CP-1 | startup-and-submit-frames | `/*/Antiphon.Agents.Pty.Tests/(CodexV0160StartupTests*)\|(CodexStartupReadinessTests*)\|(CodexReadyTrackerTests*)\|(SubmitEvidenceTests*)/*` | V-5, R-2, R-4 | all 48 results, 0 failed/skipped | 48 | 1 | true |
| CP-6 | all | CP-1 | startup-waits | `/*/Antiphon.Agents.Pty.Tests/(CodexReadyWaitTests*)\|(CodexV0156StartupPromptTests*)/*` | R-2 | all 27 results, 0 failed/skipped | 27 | 2 | true |
| CP-7 | all | `tests/Antiphon.SessionRunner.Tests -> bin-c907-rollout/` | rollout-compatibility | `/*/*/CodexTranscriptNormalizerTests/*` | V-7 partial, R-3 | all 18 results, 0 failed/skipped | 18 | 4 | true |
| CP-8 | all | CP-2 | submit-confirmation | `/*/*/RunnerCodexAdapterSubmitConfirmTests/*` | V-8 regression, R-4 | all 8 results, 0 failed/skipped | 8 | 1 | true |
| CP-10 | all | CP-2 | final-unit | `/*/*/*/*[Category=Unit]` | Final Unit | >= 1 executed, 0 failed | 1 | 12 | true |

## Binding outcome

The second prompt produced the existing `tab to queue message` token and a pending
banner; exact captures, offsets and hashes are in the fixture provenance. No parser
repair is warranted for the observed token. V-4 runs the independent derived witness,
without the old design's new-grammar or location-restriction assertion-red. The current
matcher intentionally searches the full screen; no restriction to footer position is
introduced or claimed. V-5 runs the captured working, idle settle and layout matrix;
its additional modal precedence/held-read variants remain legacy regression only.
V-6 remains unbound: existing supplied captures contain no current trust accept/back,
signed-out or update scenes. V-7 current native identity/usage and V-8 new adapter
first/second-look matrix remain gaps. No pending acceptance is labelled passed.

PC-15 binds the queue-only witness; PC-16 remains the original unimplemented
location-restriction proposal, not a guarded behavior claim. All 73 PCs/variants
stay pending Mutation for scope reconciliation and missing-control discovery.
