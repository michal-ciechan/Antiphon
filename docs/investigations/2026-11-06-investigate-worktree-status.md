# Worktree status — 2026-11-06

The work is incomplete: ordinary server2 tasks still receive a desktop checkout and a per-task runner mirror. Runner slot code has landed but is inactive; automatic runner retention ships on in newer source but is absent from the loaded November 3 build.

- Census at **13:35:05Z: 320 task worktrees**, comprising **197 terminal >24h**, **114 terminal <=24h**, and **9 open**. There is one additional hidden evidence directory. `/work` is 54% used.
- A current-capacity slot pool would target **14 reusable worktrees plus up to 4 overflow slots**, alongside unresolved legacy/excluded/quarantined directories.
- A 24h sweep alone would leave **123** task trees in this snapshot. Age qualification is not proof of safe deletion.
- **S1/S2/S4a are implemented; activation and acceptance remain separate. S4b/S5a/S5c have no implementation.**
- **S5b-1 landed; S5b-2 is in Review with recorded incomplete verification; S5b-3 is the missing producer** that stops desktop checkout growth. The decided desktop half is ref-only, not a desktop slot.
- Today's desktop count is unmeasured. The last readable November 5 cleanup report ended with 249 registrations and 275 held/deferred observed paths; neither is today's count.
- No cleanup, settings change, deployment, desktop command, product fix, build or test ran in this investigation. The existing server2 cleanup task was already Queued.

The [full CARD-0824 evidence report](2026-11-06-card-0824-per-slot-reusable-worktrees-instead-of-one-worktree-per-task-delete-remote-branch-after-land.md) contains landed SHAs, rc containment, per-slice defaults and activation status, file:line citations, persisted task events, all 320 census rows, cleanup/salvage limits and remaining uncertainties.

--- next stage ---
next: decide
handoff: Separate qualified-rc activation and the intended 24h/salvage retention policy from S1 slot canary and unfinished S5b-2/3; preserve recorded safety and verification obligations. The full report provides the implementation, activation and count evidence; no product fix was designed.
artifact: docs/investigations/2026-11-06-investigate-worktree-status.md
