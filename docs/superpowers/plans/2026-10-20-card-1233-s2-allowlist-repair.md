# CARD-1233 S2 allowlist repair

Repair task: `6947f0c0-f4fe-406f-bb19-d598555f7678`.
Original Code and landing owner: `622bbe98-a431-4e9a-9f2b-00e1e869c248`.
Base: `196e0fb8d6e47aef8d8e2b8ce1fa3e8821aa4b62`.
Target: master only. Restart: none.

Final Review `372690b2` found that `ReadRegistry` consumes S1's reason for
`CheckpointInheritedFileTests`, leaving `CheckpointSourceGitBoundsTests` with
an empty reason. Add a dedicated CARD-1233 S2 comment immediately above the
second entry. Preserve S1's comment, Git capture code and the original plan's
CP-2 filter.

The repair brief explicitly requires one narrow green execution of
`Registry_matches_compiled_metadata*` through the checkpoint tool with
`--row-timeout 40m`. This is the complete ordinary verification selection for
this repair; it does not rerun the parent implementation's broader obligations.
The optional base red is omitted because this fresh worktree has no reusable
build. The existing Review finding is the defect witness; no new test is added.

- V-1: the compiled metadata registry guard accepts the dedicated S2 reason.
- R-1: inspect base-to-HEAD diff to confirm S1's reason, Git capture code and
  original CP-2 filter are unchanged.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | S2-repair | `tests/Antiphon.Tests -> bin-c1233-s2-repair-6947f0c0/` | registry-repair | `/*/*/TestClassificationGuardTests/Registry_matches_compiled_metadata*` | V-1 | exactly 1 passed, 0 failed/skipped | 1 | 12 | true |

Use committed HEAD as `--expected-source-sha`; inspect fresh TRX identities and
validate the report receipt. Bootstrap the checkpoint tool through
`scripts/build-slot.ps1` into `bin-c1233-repair-tool-6947f0c0/`; that supporting
build is required because this worktree has no tool binary. Remove owned
alternate outputs after the completed run. Run `scripts/check-evidence-diff.ps1`
over this repair's full base-to-HEAD range.

All inherited PC-1 through PC-11 variants and the four S2 discovery candidates
remain pending and unbound for method-scoped SourceLanding Mutation; see
[the S2 candidate](../mutations/2026-10-20-card-1233-s2-candidate.md).
The allowlist repair adds a missing-reason removal candidate for Mutation's
discovery; its detector is the exact method in CP-1. No deliberate mutation is
commissioned here. Ordinary completion hands off to Review; the caller retains
landing through the original Code owner.
