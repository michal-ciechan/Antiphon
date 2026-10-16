# RC2 candidate 1 final Linux roster verification

Status: **execution pending**. Candidate `refs/heads/rc/2026-10-15` was verified by `git ls-remote` at exactly `b7265a05aa8773f08dbf457b93c7561e7874822d`; the assigned branch started clean at that commit.

Run the unchanged frozen 373-class roster and literal filter from [the Linux qualification](2026-10-15-card-1287-linux-unit.md), matching `origin/master` at initial inspection. Build/test commands use `scripts/build-slot.ps1`, `OutputPath=bin-rc2-45e84b80/` and `UseAppHost=false`. The complete aggregate must finish before any pass is claimed. Every failure requires an exact isolated rerun on the candidate and RC1 `067b47e2dc903247f1a7c8aa539032cc4266e4ad`. No product/test repair is authorized.

Raw evidence: `/work/evidence/task-45e84b80/`. Final counts, timing, census, failure attribution and cleanup will be recorded here after execution.

--- next stage ---
next: none
handoff: Execution in progress; no release qualification yet.
artifact: /work/worktrees/task-45e84b80/docs/investigations/2026-10-16-rc2-final-linux-roster.md
