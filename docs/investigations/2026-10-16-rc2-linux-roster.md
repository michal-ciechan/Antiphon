# RC2 Linux frozen roster verification

Status: execution pending. Candidate `refs/heads/rc/2026-10-15` was verified by `git ls-remote` as `f7900158ef3416894c720d892bb1c21cc8095057`. Assigned branch starts at that exact commit. Previous candidate for failure comparison: `067b47e2dc903247f1a7c8aa539032cc4266e4ad`.

The frozen [Linux qualification roster](2026-10-15-card-1287-linux-unit.md), class list and literal filter match `origin/master` at `69031cfa11244dc988c8d79d3f516bc627dd3824`. This run tests the pinned RC2 source, retaining all 373 classes and the historical 4,516-case census as a comparison, without extending the selection or its exclusions.

Execution uses the documented native Linux commands through `scripts/build-slot.ps1`, with task-owned `OutputPath=bin-rc2-ca4254f8/`, `UseAppHost=false`, Detailed output, fresh TRX and no production runner/AppHost operations. Raw logs, argv, timing and output inventories are retained outside Git at `/work/evidence/task-ca4254f8/`.

Results and failure classification will be appended after all owned foreground drivers finish. No code fixes are commissioned by this pass.
