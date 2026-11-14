# C1514 history timeout attribution — experiment protocol

Debug task 7bd634da; same host, serial class runs, 75 minute time box.

Pinned master: `8c06c7e11f40cc224235ecbc68c2389913cc67ee` (verified with ls-remote).
Reviewed branch: `25fe10cebde5e8e17530d579a7c4e0b57f080515`.

- A: detached scratch worktree at pinned master, no branch changes.
- B: pinned master plus the exact source patches of `4510b8ee700e71ce084e932a2d8e9e8b0ea291f9` and `8aa0333f00a956a3b945e0ec84727537d7ffe92d`.
- C: pinned master plus only the HostLoadIngestService.cs patch from `8aa0333f00a956a3b945e0ec84727537d7ffe92d`.

The derived-index commit is deliberately excluded; no index regeneration is needed for this class. B/C are staged experimental patches in detached worktrees, not deliverables or release qualification. No branch is created or switched; the assigned branch remains the only publication branch. Each experimental tree ID and patch is retained under `.antiphon/debug-7bd634da`.

Build each arm once via the host slot gate using native outputs and UseAppHost=false. Run exactly `/*/*/C1514HostLoadHistoryTests/*` three times in order A,B,C,A,B,C,A,B,C. Each run retains TRX, full output, start/end load and wall duration. No co-scheduled child test processes. Source stays frozen while commands run. Three repetitions are descriptive observations, not statistical proof.

Results pending execution.
