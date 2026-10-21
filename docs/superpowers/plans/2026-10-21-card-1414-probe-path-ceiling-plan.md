# CARD-1414 probe PATH ceiling

RunnerProcessProbe refused every probe when the inherited PATH exceeded the
4000-character per-value ceiling (4645 on the Windows qualification desktop).
The inherited PATH alone is now bounded on an entry boundary (trim, drop empty,
duplicate and missing entries, then cut at a whole entry in search order). Every
other inherited value and the secret-redaction bounds keep the hard ceiling.

Witness: d93c7f28e (`Inherited_path_over_the_ceiling_is_bounded_on_an_entry_boundary_and_the_probe_runs`,
red at base with "The bounded child environment is unavailable.";
`Oversized_inherited_non_path_value_is_still_refused_before_start` guards the
non-PATH refusal). Fix: S1 in `server/Infrastructure/Agents/Tui/RunnerProcessProbe.cs`.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1414-probe/` | probe-class | `/*/Antiphon.Tests.AgentTui/RunnerProcessProbeTests/*` | V-1 (PATH bounded, probe runs), V-2 (non-PATH oversized refused), whole affected class | all class cases pass, 0 failed (OS-specific cases may skip) | 29 | 12 |

### Cost

Ordinary floor: one Antiphon.Tests build plus one class run, about 12 minutes.
PCs pending for Mutation: PC-1 (remove the PATH bounding -> V-1 red), PC-2
(apply the bounding to every bootstrap variable -> V-2 red), PC-3 (skip the
missing-directory filter -> V-1 red).
