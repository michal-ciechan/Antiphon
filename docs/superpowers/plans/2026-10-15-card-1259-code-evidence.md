# CARD-1259 Code evidence

Original Code/landing owner: `eddab1c0-8f84-44bf-b222-fdeabf425004`.
Branch: `feat/card-task-eddab1c0`; worktree: `/work/worktrees/task-eddab1c0`.
Task base: `d1fc275773a1e04c73985771eecb387440c3eecd`.
Plan: `2026-10-15-card-1259-transcript-pump-decoupling-plan.md`.
Executable selection: `2026-10-15-card-1259-transcript-pump-verification.md`.

## S1

The four S0 cases now require the other session's entire ordered transcript and
marked task result while the first session's side effect remains held. Each case
owns a PostgreSQL clone and registers the real session state store.

The three V-2 variants use the real confirmation query barrier, stream-only
confirmation production, independent observer connections and controlled time.
The ready-marker fake emits the held payload and original copy. No production
implementation has changed. CP-1 is intended assertion-red, not passing evidence.

Source-reader audit repeated at the task base: searches of all test source for
the three filenames and read helpers find no source-reading consumers.
ScopeDriftTests and DelegationUnitTests contain inert path examples only.
Existing affected guard classes remain in CP-11 and CP-12.

Runner defaults/catalogue read at task start: revision 2, no kind overrides;
eligible Linux and Windows runners. No placement pin is required.

An isolated checkpoint-tool bootstrap is an explicitly named prerequisite build
outside the closed product-checkpoint table; it takes the host build slot.
Ordinary V/R and all 113 independent PCs remain pending. Mutation owns PC red/
restore/green after source landing. Restart after landing: server.
