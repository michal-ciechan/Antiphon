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

Repair (Review a830c7b4 D1): when nothing survives the bounding (all entries
missing, or the first surviving entry is itself past the ceiling) the original
value is kept so the ceiling refuses it, instead of starting the child with an
empty PATH (a POSIX child then searches its working directory). Witness:
`Inherited_path_with_nothing_bounded_is_still_refused_before_start` (two cases,
red at 2ecc3f0da with "The probe process could not be started." from the start
seam). Fix: S2. Windows UNC entries skip the existence check (advisory: an
unreachable share must not block the probe before its timeout).

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S2 | `tests/Antiphon.Tests -> bin-c1414-probe/` | probe-class | `/*/Antiphon.Tests.AgentTui/RunnerProcessProbeTests/*` | V-1 (PATH bounded, probe runs), V-2 (non-PATH oversized refused), V-3 (nothing bounded still refused), whole affected class | all class cases pass, 0 failed (OS-specific cases may skip) | 31 | 12 |

### Cost

Ordinary floor: one Antiphon.Tests build plus one class run, about 12 minutes.
PCs pending for Mutation: PC-1 (remove the PATH bounding -> V-1 red), PC-2
(apply the bounding to every bootstrap variable -> V-2 red), PC-3 (skip the
missing-directory filter -> V-1 red), PC-4 (return the empty bounded value
instead of the original -> V-3 red).
