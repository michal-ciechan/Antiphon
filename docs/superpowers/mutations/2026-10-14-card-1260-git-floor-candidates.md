# CARD-1260 Git floor mutation candidates

Unbound candidates for SourceLanding Mutation. No deliberate mutation was run by
Code f3a206f4. Mutation owns binding, red/restore/green and missing-control discovery.
The ordinary plan is [the Git floor plan](../plans/2026-10-14-card-1260-root-image-git-floor-plan.md).

| ID | Candidate | Method-scoped detecting filter | Status |
|---|---|---|---|
| PC-1 | Replace `sort -V -C` with `sort -C`; 2.9.0/2.100.0 boundary outcomes must fail | `/*/*/GitMinimumVersionGuardTests/Guard_refuses_git_below_the_landing_floor` | pending |
| PC-2 | Lower the guard floor to 2.39; refusal of 2.39.5 must fail | `/*/*/GitMinimumVersionGuardTests/Guard_refuses_git_below_the_landing_floor` | pending |
| PC-2 runner variant | Lower the guard floor to 2.39; the runner floor assertion must fail | `/*/*/DockerStackContractTests/Runner_git_meets_the_declared_minimum` | pending |
| PC-3 | Remove the root runtime guard invocation; required guard execution must fail | `/*/*/DockerStackContractTests/Server_image_git_meets_the_declared_minimum` | pending |
| PC-4 | Restore `ARG GIT_MINIMUM_VERSION=2.43` in the runner Dockerfile; single declaration must fail | `/*/*/DockerStackContractTests/Git_floor_is_declared_once` | pending |
| PC-5 | Add apt `git` to the root runtime; no-apt-Git assertion must fail | `/*/*/DockerStackContractTests/Server_image_git_meets_the_declared_minimum` | pending |
| PC-6 | Drop the malformed-version `case`; malformed-input outcomes must fail | `/*/*/GitMinimumVersionGuardTests/Guard_refuses_git_below_the_landing_floor` | pending |

PC-1 boundary values and PC-6 malformed inputs are variants within their existing
single test method. All remain pending; ordinary green does not discharge them.
