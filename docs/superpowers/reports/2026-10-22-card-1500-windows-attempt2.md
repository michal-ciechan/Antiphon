# CARD-1500 Windows CP-3 second attempt

Landing owner for the complete CARD-1500 delta, as assigned by this brief:
b94623ea-2250-419e-a1a2-569eff8b379c. Branch: feat/card-task-b94623ea.
Worktree: C:\Antiphon\worktrees\card-task-b94623ea.
Task base: 9c42243747ba4fe252f85a7ec5c39bebeac0138b.
Original implementation Code: 05f0443d-947d-4933-97ea-e56ee9b17be0.
Previous Windows Code: f7f161f3-dacd-4b13-ae15-8e46c4868e29.
Whole implementation base / plan prerequisite: c6d08dccb8aece1dcf2a757b6a64ffe2824d6ee6.

This report is committed before execution. It records scope, not a future pass.
The final checkpoint uses committed HEAD as expected source SHA; tracked source
and this report remain unchanged through qualification. Later results, unedited
CHECKPOINT lines, actual tested SHA, fresh TRX roster, receipt validation, costs,
publication previews and cleanup facts go into the ignored stored task report
.antiphon/task-b94623ea.md and the caller's final report.

The brief authorizes only CP-3 / V-3 / R-3 on Windows, with private fixtures,
one isolated test-project build and one exact method selection. It overrides the
generic Final Unit/full-class profile. No shared production implementation is
changed in this continuation and no broader test selection is authorized.
The invariant is coherent read-only observation of a writer-held private lock
under Windows PowerShell 5.1 and pwsh, with ASCII scripts and preserved bytes.
The row executes one TUnit method containing four fixture assertions.

Plan: [closed checkpoint manifest](../plans/2026-10-22-card-1500-qualification-ops-plan.md).
The fresh worktree has no CP-3 compiler output; its plan output directory is
therefore fresh and does not reuse either predecessor attempt's outputs.
The necessary checkpoint-tool bootstrap is the sole additional utility build,
through scripts/build-slot.ps1 with isolated bin-c1500-b94623ea-tool/ output.
The already-built checkpoint tool owns admission for the isolated build and row;
no outer slot is held while its drivers request their slots. No unlisted test.

The predecessor report was read from its ignored local report and task API result
after the specified git-show path proved absent. Its first CP-3 execution at
55c0dc6da27c587e9a1b3a2012e099fa98968393 was fixture setup red: WindowsApps pwsh
alias rejected before the fixture launched. The installed-host repair is at this
task's base. Its subsequent attempt was stopped after budget expiry before the
row ran; that attempt is incomplete, not green. Historical costs and evidence
remain in the predecessor reports, without relabeling.

CP-1 / V-1 / R-1 and CP-2 / V-2 / R-2 have inherited clean, validated passes at
e90c47450df4247bc254a027390711a555e59980, one test each. They are not new-tip
executions. The predecessor records manual review of the invocation, ownership
and ancestry checks, unknown/reused identity aborts, 30-second post-stop census,
exclusive start hold, retained same-volume lock bytes, distinct run.lock handling
and publication warning. No destructive live acceptance action is required.

The runner-defaults and session-runners routes were read: desktop is available
and is the Windows lane required by the brief. No fleet location is embedded.
Nothing under C:\Antiphon\releases or the protected live qualification is an
input to this task. All test inputs and children belong to the existing fixture.
The 60-minute wall budget includes admission waits. A fixture-only red permits
one minimal repair and one rerun; assertions and deadlines must not be loosened.

All controls remain pending method-scoped SourceLanding Mutation, including
missing-control discovery: PC-1a/b/c/d/e, PC-2a/b/c, PC-3a/b/c, PC-4a/b/c/d/e,
PC-5, PC-6a/b and PC-7. The [unbound inventory](../mutations/2026-10-22-card-1500-status-unbound.md)
does not claim any red/restore/green cycle. Accepted D-1 is status plus manual
stop/recovery documentation; automated stop remains separate work. Restart: none.

On qualified CP-3 completion the next stage is ordinary Review of this whole
landing-owner chain, followed by caller landing and SourceLanding Mutation.
