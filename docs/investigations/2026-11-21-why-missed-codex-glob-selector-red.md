# Why the Codex selector glob reached master

## Introducing commit

CARD-2046 added `docker/session-runner-grok/**/codex*` to the
`codex-real-cli-boot` row in
`7e77de239f56f77e184f978d6799df27c9766ee7`. The pattern matched zero tracked
paths at introduction. Git history contains no tracked match or deletion under
that pattern. The directory already had `verify-codex-image.sh`, whose basename
starts with `verify-`. The row selects the Dockerfile by its explicit path.

Both affected Unit methods in `ImpactedClassesSelectorTests` call the live
selector with `-Audit -Json` and fail when its exit code is 1. The empty glob
breaks the seed-inventory audit and prevents the downgrade case from reaching
its call-site comparison.

## Verification selection

The original Final Review `05dbe337-da2c-410a-971e-476c8b7b7d73` ran the
real CLI canary, ten guard classes, and the derived-index, runner-image, and
expected-skip classes. Its ordinary scope was incomplete and its handoff
requested additional Code verification. The master replay
`0f59f07a-97ec-42a3-a8c2-07d9d4a4a34d` ran its named canary, guards, index,
image, expected-skip, account-bootstrap, spawn-roster, and startup-readiness
rows. The replay's landing receipt records explicit caller approval.

Neither selection included `ImpactedClassesSelectorTests`. The new hand row
routed its input paths to the boot canary. The derived index folds hand rows
over tracked paths, so rerunning its generator or byte-snapshot test does not
expose a glob that has no matching path. CARD-2013 later ran the selector class
in its project-definition checkpoint and observed both failures.

## Cheapest pre-land check

On a change to `tests/impacted-classes.json`, run
`pwsh -NoProfile -File scripts/impacted-classes.ps1 -Audit -Json`. This checks
the entire table against tracked paths and class owners without a test assembly
build. The TUnit selection for that path is the two exact method filters below.

No same-cause exemption applies.

### Checkpoints

Run CP-1 and CP-2 at master `3ed773829e584eec0c93ec8b47bf3517a69204d7`.
Run CP-3 and CP-4 at the repair tip. Use `--serial` and admit each build
below one-minute host load 14. Each pair shares one build. The master rows
must each fail at the live selector audit with the named empty glob.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---|---|---|---|
| CP-1 | all | `tests/Antiphon.Tests -> bin-c7f53256-selector/` | selector-audit | `/*/Antiphon.Tests.Scripts/ImpactedClassesSelectorTests/seed_kinds_globs_and_owner_sentences_stay_present` | production table audit | 1 expected assertion failure at master | 1 | 6 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-2 | all | CP-1 | selector-downgrade-audit | `/*/Antiphon.Tests.Scripts/ImpactedClassesSelectorTests/downgrade_call_sites_are_exactly_the_derived_migration_files` | downgrade inventory audit | 1 expected assertion failure at master | 1 | 1 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-3 | all | `tests/Antiphon.Tests -> bin-c7f53256-fixed/` | selector-audit-repair | `/*/Antiphon.Tests.Scripts/ImpactedClassesSelectorTests/seed_kinds_globs_and_owner_sentences_stay_present` | production table audit | 1 passed at repair | 1 | 6 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-4 | all | CP-3 | selector-downgrade-audit-repair | `/*/Antiphon.Tests.Scripts/ImpactedClassesSelectorTests/downgrade_call_sites_are_exactly_the_derived_migration_files` | downgrade inventory audit | 1 passed at repair | 1 | 1 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
