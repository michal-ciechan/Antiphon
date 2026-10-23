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
