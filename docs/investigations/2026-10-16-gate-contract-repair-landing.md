# RC2 gate contract repair landing: blocked

Task: `5160a7b5` (Merge), 2026-10-16 UTC.

Neither target was changed or published. The brief requires stopping if the
specified source range changes anything outside tests, slow-test registry/inventory
entries, and plan/mutation notes. The repair commit changes production code in
`server/Application/Services/AbsentLaunchPolicy.cs`, so that gate failed.

## Source verification

`git fetch origin` completed successfully. The source branch
`origin/feat/card-task-8a76c085` points to
`c58f817fe819ded638de1675580c76064c582525`.

`git rev-list --reverse b227f814c02a88ea2e4c9f0731c22d8ff0ffcd4d..c58f817fe819ded638de1675580c76064c582525`
returned exactly these two commits, in order:

1. `a562ec689b60f62ae9976e82584f0bc164301960` — tests.
2. `c58f817fe819ded638de1675580c76064c582525` — repair.

The complete range changes five files:

- `docs/superpowers/plans/2026-10-16-rc2-gate-contract-repair.md`
- `docs/superpowers/mutations/2026-10-16-rc2-gate-contract-repair.md`
- `tests/Antiphon.Tests/Application/AbsentLaunchPolicyTests.cs`
- `tests/Antiphon.Tests/slow-tests-allowlist.txt`
- `server/Application/Services/AbsentLaunchPolicy.cs` — outside the allowed scope.

The production change adds seven lines: three extend the queue-column inventory
with review, runtime-action, and direct-reply fields; four supply the corresponding
null values in the queue snapshot construction. This is in the named repair commit,
not an unrelated commit accidentally included in the range.

## Target publication

Remote tips were read after fetch and confirmed with
`git ls-remote origin refs/heads/rc/2026-10-15 refs/heads/master`.

| Target | Old SHA | New SHA at stop | Outcome |
|---|---|---|---|
| `rc/2026-10-15` | `41d6f6adf431622b3982f9ac41417a8e555072ba` | `41d6f6adf431622b3982f9ac41417a8e555072ba` | Unchanged; not published |
| `master` | `bc3168594e598cb4206b5e11f6d3aabc04561fd9` | `bc3168594e598cb4206b5e11f6d3aabc04561fd9` | Unchanged; not published |

No cherry-picks, merges, registry conflict resolutions, target pushes, builds, or
tests were performed. Build/test execution counts are zero because the source
scope check is a prerequisite. No canonical checkout, AppHost, runner, or tag was
changed. This report is the only task-branch change.

## Required decision

Provide a corrected test/inventory-only source range, or explicitly recommission
landing of the production repair with its required review and verification
requirements. The existing test-only authorization does not cover this range.

--- next stage ---
next: decide
handoff: Resolve the production-code scope mismatch before commissioning another landing pass.
artifact: docs/investigations/2026-10-16-gate-contract-repair-landing.md
