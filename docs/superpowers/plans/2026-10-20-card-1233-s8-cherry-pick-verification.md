# CARD-1233 S8 cherry-pick verification

Repair Code task: `9aa4cd39-d152-4081-91b6-635b5fef6d41`.
Original Code and landing owner: `c519b9f0-e8f6-4fcf-b9be-fcc93d51ceb6`.
Target: master only. Restart: none.

The assigned branch was advanced without rewriting history from
`9e78a08880cb300dab6fc3bedbefc1a47eed3284` to fetched master
`e6dced0b0636e1490cc4b46595f40f660bcaa0a3`. Only the two S8 commits
`cca098a42602c48c3971bd49deedc29655bffc2a` and
`eac767e32fe4b25b63f177e217363f738edac705` were then cherry-picked.
The original S4 history was not replayed.

Immediately after cherry-pick, the five changed C# files were byte-identical to
the source reviewed by Review `ff19e691` at
`eac767e32fe4b25b63f177e217363f738edac705`.
Master's landed `CheckpointVerifierBoundsTests`, CP-4 trailing wildcard,
and slow-test allowlist entries and reason comments remain intact. S8's
CP-8 trailing wildcard and separate allowlist reason are retained.

The first repair run, `20261020-113545-5530`, verified
`087d17358dabd91abf426552dbd1da42dba6f517`: CP-8 passed 4/4, but CP-REG
failed 1/1 with `lane-xor Antiphon.Tests.Checkpoints.CheckpointFixtureRoutingTests`.
That class had only `Slow`, while the registry requires exactly one of `Unit`
or `Integration`. The repair adds `[Category("Integration")]`, matching the
adjacent process tests. This is the only C# difference from the reviewed S8
source; test bodies, assertions, timeouts, fixture routing and shared runner
behavior are unchanged. Both required rows are rerun on the repaired commit
so the final evidence binds to one tip. The initial red is retained as the
guard's defect witness; it is not reclassified as a receipt refusal.

## Verification design

This repair's explicit brief requires Final verification limited to CP-8 and
`Registry_matches_compiled_metadata`, with `--row-timeout 40m`. It explicitly
excludes a whole-Unit run. The registry row below records the additional
selection authorized by that brief; it does not broaden S8's behavior scope.
Use the exact committed HEAD as `--expected-source-sha`.

W8 covers all F1/F2/F3/F9 cases, including ordinary stdout/stderr, nonzero
exit results where returned by the caller, cancellation-token identity,
bounded completion, and descendant death with both living and exited parents.
The historical pre-fix red is retained in the
[S8 Mutation candidate](../mutations/2026-10-20-card-1233-s8-candidate.md):
run `20261020-064446-befa` failed all four cases on surviving owned children.
No deliberate mutant is introduced by this cherry-pick repair.

The registry check guards the compiled Slow classification and its allowlist,
including both S4 and S8. There are no additional ordinary V-n/R-n acceptance
rows commissioned here. R-2 and every PC-1 through PC-11 variant remain pending
for their separately commissioned scope; this task does not discharge them.
The five S8 Mutation candidates remain pending and unbound.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-8 | S8-repair | `tests/Antiphon.Tests -> bin-c1233-s8/` | linux-process-token-fixtures | `/*/*/CheckpointFixtureRoutingTests/C1233_TokenCallers*` | W8 | 4 executed, 0 failed/skipped | 4 | 10 | true |
| CP-REG | S8-repair | `tests/Antiphon.Tests -> bin-c1233-s8-registry/` | registry | `/*/*/TestClassificationGuardTests/Registry_matches_compiled_metadata*` | Slow registry | 1 executed, 0 failed/skipped | 1 | 5 | true |

### Cost

The two isolated checkpoint builds and exact selections have a 15-minute
estimated floor. Bootstrap the checkpoint tool through `scripts/build-slot.ps1`;
its build is necessary infrastructure for this manifest. No full assembly or
whole-Unit run is authorized. Inspect fresh TRX identities and counts, validate
the source receipts, preserve unedited CHECKPOINT lines, and clean owned output.
On a receipt refusal, the brief permits deleting owned leftover output and one
retry, preserving the original outcome.
