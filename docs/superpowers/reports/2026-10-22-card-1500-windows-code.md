# CARD-1500 Windows CP-3 continuation

Code task f7f161f3-dacd-4b13-ae15-8e46c4868e29 continues original Code task
05f0443d-947d-4933-97ea-e56ee9b17be0. This task is the caller-designated landing
owner of the complete CARD-1500 chain. Branch: feat/card-task-f7f161f3.
Worktree: C:\Antiphon\worktrees\card-task-f7f161f3.
Task base: e90c47450df4247bc254a027390711a555e59980.
Whole implementation base: c6d08dccb8aece1dcf2a757b6a64ffe2824d6ee6.

The brief authorizes only CP-3/V-3/R-3, using private fixtures on Windows.
No whole Unit, full class, other tests, live qualification inspection or recovery
operation is authorized. The fixture holds its own native-run.lock open with
Write/Read sharing and invokes the actual status entrypoint under Windows
PowerShell 5.1 and pwsh. Its four internal assertions cover production ASCII,
both coherent live observations and retained lock bytes; TUnit execution count is one.

This report is committed before execution. It does not claim an unexecuted pass.
The final run uses committed HEAD as --expected-source-sha. Fresh TRX roster,
unedited CHECKPOINT lines, validator result, actual source SHA, build-slot facts,
merge previews and evidence-history guard results are retained in the ignored
.antiphon/task-f7f161f3.md and delivered in the final report. No tracked report
edit follows the final run. The necessary checkpoint-tool bootstrap is the only
utility build in addition to CP-3's isolated test-project build; it uses a host
build slot and alternate bin-c1500-tool/ output. No unlisted test is authorized.

The runner-defaults and session-runners GET routes were read: a Windows desktop
runner was available. No fleet location is embedded in production or fixture code.
All fixture roots are allocated by the harness; no live release root is an input.

The brief reports predecessor CP-1/V-1/R-1 and CP-2/V-2/R-2 green at the task base.
The tracked predecessor report preserves its initial behavioral red and subsequent
fix, with final receipt facts delegated to its ignored report. The suggested
git-show path .antiphon/task-05f0443d.md is absent even after fetching that branch;
this is not evidence that those final rows were absent or failed. Historical
results and costs remain in the predecessor report and caller's received report.
This task makes no new portable execution claim.

Accepted D-1 scope remains status plus manual stop/stale-lock documentation;
the automatic stop script is separate work. Manual safety review is recorded in
the predecessor report. No live stop/cleanup acceptance operation is required.
Restart: none.

All PC-1a/b/c/d/e, PC-2a/b/c, PC-3a/b/c, PC-4a/b/c/d/e, PC-5, PC-6a/b and PC-7
remain pending method-scoped SourceLanding Mutation, including missing-control
discovery. CP-3 green cannot discharge PC-7. The unbound inventory is
[2026-10-22-card-1500-status-unbound.md](../mutations/2026-10-22-card-1500-status-unbound.md).
The closed plan is
[2026-10-22-card-1500-qualification-ops-plan.md](../plans/2026-10-22-card-1500-qualification-ops-plan.md).

When CP-3 and receipt qualification succeed, next is ordinary Review of this
landing-owner chain. A fixture defect permits one minimal repair and one CP-3
rerun; a product-shape disagreement is reported without a workaround.

## Initial Windows result and fixture repair

Run 20261022-211505-021c completed red at committed
55c0dc6da27c587e9a1b3a2012e099fa98968393. Fresh TRX contains exactly
RcQualificationStatusTests.C1500_WindowsSharedReadAndPs51: 1 executed, 0 passed,
1 failed, 0 skipped. It failed before the fixture because the default harness
resolved the WindowsApps pwsh alias, which its native ownership guard rejects.
This is fixture setup red, not a behavioral mutation/control or a product defect.
The isolated build passed (1152 warnings, zero errors), slot granted/waited 0s,
368.1636823 seconds including checkpoint work. The test slot waited 311 seconds
for memory admission. Total checkpoint wall time was 13m25s. No gate was bypassed.

CHECKPOINT CP-3 commit=55c0dc6da27c587e9a1b3a2012e099fa98968393 build=ok filter=/*/*/RcQualificationStatusTests/C1500_WindowsSharedReadAndPs51 executed=1 passed=0 failed=1 skipped=0 trx=C:\Antiphon\worktrees\card-task-f7f161f3\.antiphon\checkpoints\20261022-211505-021c\rows\CP-3\run.trx slot=granted waited=311s dirty=0 source=55c0dc6da27c587e9a1b3a2012e099fa98968393 sourceState=clean buildSource=verified

The minimal repair selects the existing fixture resolver for a regular installed
PowerShell executable in CP-3 only. The nested pwsh invocation uses that host's
PSHOME executable. No production code, shared harness, timeout or assertion changes.
The permitted one CP-3 rerun is pending when this report is committed. Red output
is removed only after checkpoint termination, while its receipts and TRX remain.

The predecessor's final report was recovered through GET /api/agent-tasks for
05f0443d: CP-1 and CP-2 each passed one test at e90c47450df4247bc254a027390711a555e59980,
with clean source, verified build provenance, slot granted/waited 0s and validated
receipts. Those results remain inherited evidence; no portable rerun was authorized.
