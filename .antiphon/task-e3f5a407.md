# CARD-1518 S1 incident regression witnesses

Original Code/landing owner: `e3f5a407-85ad-4b88-910d-df4705dcf567`.
Branch: `feat/card-task-e3f5a407`.
Worktree: `/work/worktrees/task-e3f5a407`.
Task base: `bb54a11909e456ef92ce22905d7c5dc9cab40978`.
Plan: `docs/superpowers/plans/2026-10-23-card-1518-permission-wait-classification-plan.md`.

## Scope and verification binding

The explicit brief limits this assignment to S1, intentionally red and not
landable. No production behavior, budgets, detector, input policy or deployment
is changed. S2-S4 remain required. The two test methods use existing production
dispatcher/queue/policy entry points, the real CARD-0680 observation contract,
an isolated PostgreSQL schema and a controlled clock. Optional observation
absence is asserted before dereference; absent kind/proof cases belong to V-2.

`PermissionWaitFixture.cs` retains the incident's literal refusal and menu,
one unresolved Bash ToolCall, a zero-attempt pending refinement, a live owned
session and a source marker. No Bash command or tool process is launched.
`PermissionWaitTests.cs` pins independently observable warning, note identity,
destination, wording, deadline, failure notification and retained-custody facts.

- V-1 / `C1518_CapturedDialogNeedsDecisionBeforeStall`: first eligible tick,
  with generic stall detection disabled, must persist one Warning and one
  ProviderInputWait note; repeated scan at 60 seconds must retain one episode.
  Baseline red must be the missing Warning assertion, after fixture validity,
  maintenance success and custody checks.
- V-4 / `C1518_NinetyMinuteBoundaryKeepsCustody`: 89:59 stays Working; exactly
  90:00 fails once and notifies the caller; 90:01 preserves that terminal result.
  Source, owner/session and pending refinement survive. Baseline red must be
  the missing permission-specific failure wording after those checks pass.

The S1 closed checkpoint is CP-1 only, one build and the exact V-1/V-4 filter.
Expected outcome: two reached assertion failures, zero skips. A new isolated
build must succeed before either red is valid. The receipt validator only
certifies green and is expected to reject intentional red as `report_failed`;
retain its actual result, raw CHECKPOINT line, source provenance and fresh TRX.
No Unit lane, whole assembly, live CLI or Windows qualification is authorized.
Checkpoint-tool bootstrap, if necessary, is a separately reported gated setup
build. The committed report is frozen before source qualification; final raw
receipts and execution outcomes are retained in ignored `.antiphon/checkpoints/`
and `.antiphon/evidence/c1518-s1/settlement.md`, and summarized in the final report.

V-2, V-3, V-5 through V-13 and R-1 through R-4 are deferred to S2-S4 final
verification, not passed. V-4's vanished/unknown final-read subcases and a first
sweep already beyond 90 minutes also remain for S2's expanded V-4 witness.

## Execution history before final qualification

Tool bootstrap: the separately gated build of `tools/Antiphon.Checkpoints` into
`bin-c1518-tool/` succeeded in 13.67 seconds, four existing compiler warnings,
zero errors; `slot=granted waited=0s`, lease held 14 seconds. It is setup cost,
not a test run. All CP runs have a 20-minute total hard deadline (narrower than
the manifest's 90-minute total); neither production nor row deadlines changed.

Run `20261023-052628-a4d4` at
`bf83da60c7c435abbc208849ecf241d142cb8287` failed compilation with CS0103:
the fixture lacked the existing `MessageSendMode` namespace import. No tests
executed. Fixed by `0833bcc7da66e04c09b1c9f3b32a19d85e470ff1`; no assertion
changed. The failed build's unsealed output was manually removed only after
executor death and containment/link checks. Run wall time was 3m02s. Build
slot was granted with zero wait; the row never acquired one.

```text
CHECKPOINT CP-1 commit=bf83da60c7c435abbc208849ecf241d142cb8287 build=failed filter=/*/*/PermissionWaitTests/(C1518_CapturedDialogNeedsDecisionBeforeStall)|(C1518_NinetyMinuteBoundaryKeepsCustody) executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a slot=skipped waited=0s dirty=0 source=bf83da60c7c435abbc208849ecf241d142cb8287 sourceState=clean buildSource=unknown
```

Run `20261023-053024-cd76` at
`0833bcc7da66e04c09b1c9f3b32a19d85e470ff1` built successfully but ran zero tests
(native exit 8, checkpoint exit 3). Fresh TRX has empty Results and TestDefinitions;
this is not either red witness. The existing plan omitted the suffix wildcards
required by the pinned TUnit 1.44 source-generated method-OR hint extractor,
documented in `docs/testing-and-build.md`, Combined class filters (CARD-0403).
CP-1 alone now uses the documented suffixed OR form; the final TRX must contain
exactly both named methods and no others. S2-S4 must similarly bind their own
filters before execution. This changes selection syntax, not any assertion or
execution floor. Build and row slots were granted with zero wait. Build wall
212.708065s; reported run wall 3m59s, plus tool cleanup before wait returned.
Output was deleted by the completed executor. The report's `builds: 2` counts
both manifest declarations; only `bin-c1518-red` executed, green was unused.

```text
CHECKPOINT CP-1 commit=0833bcc7da66e04c09b1c9f3b32a19d85e470ff1 build=ok filter=/*/*/PermissionWaitTests/(C1518_CapturedDialogNeedsDecisionBeforeStall)|(C1518_NinetyMinuteBoundaryKeepsCustody) executed=0 passed=0 failed=0 skipped=0 trx=/work/worktrees/task-e3f5a407/.antiphon/checkpoints/20261023-053024-cd76/rows/CP-1/run.trx slot=granted waited=0s dirty=0 source=0833bcc7da66e04c09b1c9f3b32a19d85e470ff1 sourceState=clean buildSource=verified
```

The final run and receipt validation remain pending at this report commit;
their actual outcomes belong to the frozen-source settlement evidence and
the final message. Neither historical failure is relabeled green.

## Mutation handoff

All controls remain pending for method-scoped SourceLanding Mutation; this
assignment performs no deliberate mutation. S1 bindings are PC-1 -> V-1
(remove early scan), PC-6 -> V-4 (retain LocalExecution diagnosis), PC-7.blocked
-> V-4 (pause the original clock), PC-16.input -> both methods (recorded writes),
PC-16.launch -> both (recorded adapter factory), PC-16.stop -> both (stopper and
adapter kills), PC-16.source -> both (source marker and recorded git calls).
These are independent assertions, not one composite positive control.

PC-2; PC-3; PC-4; PC-5; PC-7.unknown; PC-8; PC-9; PC-10; PC-11;
PC-12.queued/head-only/wrong-generation; PC-13.Check/attention/stall; PC-14;
PC-15.cadence/peer-isolation; PC-16.release; and PC-17.mapping/modal-action
remain pending with their later V-2..V-13 bindings in the plan. All additional
identity/history/counter/freshness/receipt-floor guards in the optional-input
table require individually identified variants as those implementations arrive.
No claim of complete feature verification or PC cleanliness is made.

## Caller and activation

S1 adds tests only: callers still receive no early ProviderInputWait note, and
the existing deadline describes waiting on a local tool. The intended later
note kind is `LandNotificationKind.ProviderInputWait`; its literal templates
are pinned by V-1 and the plan's Caller wording and recovery section. Its
headline is `[antiphon-permission-wait:{taskShortId}] Decision needed for
{cardLabel}: Claude permission dialog observed at {observedAtUtc} in session
{sessionId}.` It says execution is unconfirmed, refinement receipt is not
confirmed, an authorized answer/cancellation decision is needed, and the
existing 90-minute local-tool deadline remains in force. At expiry it says
the task Failed, the session was not killed, its seat may remain occupied,
Refine is refused, and source/queue must be preserved before explicit recovery.

Restart: **none for S1**. No new runtime behavior can activate from this slice.
The eventual diagnosis requires S2-S4 promotion and a server restart onto the
approved release tip, with compatible active CARD-0680 runners. Plan's exact
RC gap: release docs name `rc/2026-10-18`, while remote `rc/2026-10-20` and
observed `/api/version` were `5a126f6fc5ae4e16d803b628716b8f0485a37174`;
that tree lacks all three CARD-0680 observation files and master is not its
ancestor. Master publication is not active RC support. Confirm the effective
release target; promote dependencies together and verify loaded SHAs and
`claudeDialogGuardV1`. No desktop/release-directory/live-session action was taken.

Next: Review of S1's red witnesses, per the explicit brief. This is not approval
to land the intentionally red slice; the same original Code owner must continue
S2-S4 afterward.
