# CARD-1350 final Linux roster at df6b422c

Status: **pending execution**. This is the Linux CARD-1287 subset, not the Windows
rc profile or a publication receipt.

Requested source: `df6b422c71b0828b4bd7f855a86e2a51d1987e79`.
Test checkout: `/work/worktrees/rc-final-aaee7f74` (detached at that exact source).
Report branch: `feat/card-task-aaee7f74`, based on
`f99f8bffb5887d43fafae61ae979ae9e11a36f80`; its HEAD is not the tested source.
Raw evidence: `/work/evidence/task-aaee7f74/`.

The method is the first gate's `docs/investigations/2026-10-18-rc-linux-lane.md`
from `feat/card-task-dcba76bd`: one gated isolated build, then one gated no-build
execution of the frozen 373-class filter, with `UseAppHost=false` and Detailed
TRX output. Expected filter SHA-256:
`9ba95d1db683438f6c382422c9ca4a596d2280930f100da9973b657596020ad7`.
Output path: `bin-rcfinal-aaee7f74/`.

The child driver removes the inherited task ID/token pair together; neither value
is recorded or substituted. Headed, distiller, broker and parallel-test overrides
are absent. Server2 workload snapshots retain process names/cwds and broker
occupancy without command lines or secrets. Product and test sources remain clean.
All driver children are supervised and awaited, with logs retained outside Git.

Any red will be reported without repair. Only the failing method will run at
`61117dd1663d2f47ce4708c18b609b8936491986` to classify inheritance. A recurrence of
the prior `disk_ledger_cannot_authorize_user_deletion(outside)` failure will be
reported without another candidate roster run.

--- next stage ---
next: decide
handoff: Execution is pending; this preparation record provides no passing credit.
artifact: docs/investigations/2026-10-19-rc-linux-final.md
