# CARD-1414 probe PATH ceiling

Date: 2026-10-21. Code task `51b55a49`, branch `feat/card-task-51b55a49`. Evidence:
`docs/investigations/2026-10-21-rc-2026-10-20-windows-qualification-reds.md` cause A (18 reds in
`RunnerProcessProbeTests` on the Windows desktop whose user PATH is 4645 characters). This file is
the closed checkpoint list for the Code slices; it carries no PC rows.

## Decision

`RunnerProcessProbe` keeps `MaximumEnvironmentValueLength = 4000` for every declared request value
and every inherited bootstrap value except the path list. An inherited `PATH` over the ceiling is
bounded on an entry boundary: empty, duplicate and non-existent entries are dropped first, then the
list is cut before the first whole entry that no longer fits, in the original order. A `PATH`
within the ceiling is passed verbatim and the filesystem is not consulted. The existence checks run
under the probe deadline like the other blocking path inspections, so a dead network entry times
the probe out instead of hanging it. The ceiling is not raised because it is also the per-value
bound the exact-match secret automaton allocates for; a secret-sized value in any other variable
still refuses before start with "The bounded child environment is unavailable."

Rejected: raising the ceiling for all variables (weakens the secret bound for every value), and
exempting `PATH` outright (an unbounded value in the child environment and in the redactor input).

## Slices

- S1 `test(CARD-1414)`: the over-ceiling PATH probe test (red at the base) and the non-PATH refusal
  guard, plus the helper `path` mode.
- S2 `fix(CARD-1414)`: the bounding in `RunnerProcessProbe`, the `PathEntryExists` seam, the pure
  bounding test, the deadline test, the `docs/ai-agent-tui-configuration.md` paragraph.

## Verification design

V-1 `RunnerProcessProbeTests.Inherited_path_over_the_ceiling_is_bounded_on_an_entry_boundary_and_the_probe_runs`: with the process PATH set over 4000 characters (real entries first, then forty existing scratch directories interleaved with empty, duplicate and missing entries) the probe starts pwsh, exits 0 with no error, and the child's PATH is at most 4000 characters and equals the longest prefix of the distinct existing entries that fits, with at least one existing entry cut.

V-2 `RunnerProcessProbeTests.Oversized_inherited_non_path_value_is_still_refused_before_start`: a 4001-character inherited `LOGNAME` (Linux) or `ComSpec` (Windows) still returns "The bounded child environment is unavailable." with no start attempt.

V-3 `RunnerProcessProbeTests.Inherited_path_bounding_drops_empty_duplicate_and_missing_entries_before_cutting_on_a_boundary`: the pure bounding returns a within-ceiling value verbatim without consulting existence; over the ceiling it trims whitespace and quotes, drops empty, duplicate and missing entries, cuts before the first whole entry that no longer fits, and consults existence once per distinct non-empty entry up to the cut.

V-4 `RunnerProcessProbeTests.Inherited_path_bounding_observes_the_probe_deadline`: an existence check that blocks ends the probe as timed out, not cancelled, before any start, within the one-second deadline.

R-1: the pre-existing 27 methods of `RunnerProcessProbeTests` stay green on Linux, which covers the unchanged inherited-environment path for a PATH within the ceiling.

No PC-n in this round; Mutation owns positive controls.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---|---|
| CP-1 | S1-S2 | `tests/Antiphon.Tests -> bin-c1414/` | probe-class | `/*/*/RunnerProcessProbeTests/*` | V-1, V-2, V-3, V-4, R-1 | all 31 methods, 0 failed/skipped | 31 | 9 |

### Cost

Ordinary floor: 9 minutes (one build plus the class). The one unlisted run is the S1 red run of
the two S1 methods against the base product code, reported in the Code report.
