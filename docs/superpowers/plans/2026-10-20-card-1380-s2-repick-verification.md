# CARD-1380 S2 re-pick verification

Code repair task: `089bd557-40d2-49f3-b447-9f2eb3fa8bbf`.
Original Code/landing owner: `efc5dea7-0623-43b6-b660-5851fe4cf6ef`.
Start: fetched `origin/master` at `cff9b9bdadab0285d748be3d725bd76a406450d0`.
Source: `db8894a658bfa67fb86ba0644e958d3d48aec4f3`.
Target: master only. Restart: none.

The repair brief commissions the seven S2-only commits identified by
`git cherry origin/master db8894a658bfa67fb86ba0644e958d3d48aec4f3`, in order,
preserving newer master on overlap. All seven cherry-picked without conflict.
Every path changed by those seven commits matches the source tip before this
verification-only manifest is added. In particular LandingSettings.cs retains
ForceVerificationBuild (false by default) and ReuseVerificationArtifacts (true).
No manual settings conflict resolution was needed.

## Verification design

This Final repair has an explicit narrow scope: the 42 S2 cases once, through
the checkpoint tool, with `--row-timeout 40m` and trailing wildcards. It does
not repeat whole Unit or C688_RealLandProfile. The latter's seven worktree
listings against the unchanged limit of four were proved at the original
S1 base and S2 tip; their historical red receipts remain in
`.antiphon/task-efc5dea7.md`. No assertion or timeout is weakened.

The invariant is conservative classification of the actual rebased diff:
eligible documentation records persisted non-code skip and the remote candidate;
code, graph drift, unknown input, overrides and release targets build; failed
verification refuses publication. V-2 is rerun here. R-2 state/stage/retry results
remain historical, including the explicitly excluded inherited profile failure.
V-1/R-1 are landed S1 history; V-3 through V-9 and R-3 through R-9 belong to
uncommissioned later slices. Production timing acceptance remains pending the
operator's RC activation and real workload. PC-1 and PC-2, all variants, remain
pending method-scoped SourceLanding Mutation and missing-control discovery.
The unbound candidate is `docs/superpowers/mutations/2026-10-20-card-1380-s2-candidate.md`.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-4P | S2Repick | `tests/Antiphon.Tests -> bin-c1380-2repick/` | any-noncode-repick | `/*/*/LandNonCodeSkipTests*/*` | V-2, S2 repair | all 42 cases, 0 failed/skipped | 42 | 8 |

Use committed HEAD as `--expected-source-sha`, wait to terminal completion, and
inspect fresh TRX for all 13 methods and 42 data cases. On a receipt refusal,
remove only this run's owned leftover output and retry once as authorized.
No repeated green proof is required. The checkpoint CLI is absent in this fresh
worktree, so one additional gated build bootstraps it into an isolated output.
That tool build is infrastructure preparation, not a test pass.

### Cost

One bounded checkpoint: estimated 8 minutes plus tool bootstrap and inspection,
within the repair's 40-minute budget. No full assembly or unbounded class runs.
