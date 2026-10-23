# CARD-1535 verification continuation

Verification task: `4dcbfe6e-7be5-4967-8218-ccbe1a4e55d4`.
Original Code and landing owner: `c6e43a93-af54-41a9-8d18-b0626a9ca9f3`.
Branch: `feat/card-task-4dcbfe6e`.
Worktree: `/work/worktrees/task-4dcbfe6e`.
Task base: `b65dd283b5534d2b8773ec9e2f8aefcf67a565ce`.

The inherited allowlist repair is unchanged. Its previous checkpoint never
executed the guard: an accidentally unitless total timeout canceled the build.
That historical run remains invalid and does not establish a test outcome.

The closed checkpoint selection is CP-1 in
`docs/superpowers/plans/2026-10-23-card-1535-finish-plan.md`:
`/*/*/TestClassificationGuardTests/Registry_matches_compiled_metadata`.
V-1 and R-1 require the compiled metadata registry guard and SHA-bound clean
receipts with verified build provenance. Qualification is pending when this note
is committed; final results and unedited CHECKPOINT lines will be recorded in
`.antiphon/task-4dcbfe6e.md` without another source commit.

The authorized run uses an explicit `--total-timeout 25m`, with the plan-derived
15-minute build and row deadlines. There is one build and one filter, no Unit
lane, full class, full assembly, repetition, desktop or live Windows work.
An additional gated build may bootstrap the absent checkpoint tool, solely as
infrastructure preparation. Task-owned alternate outputs are removed before and
after qualification. Receipt validation, full task-range evidence diff and
merge-tree against fetched master complete the verification record.

No PC IDs or variants are defined. SourceLanding Mutation retains missing-control
discovery. Ordinary light Review follows completed verification. Restart: none.
