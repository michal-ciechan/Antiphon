# CARD-1510 unbound mutation candidate

Original Code and landing owner: `5019ea31-13ce-48dd-bdc7-4017c6529e1f`.
Plan: `docs/superpowers/plans/2026-10-23-card-1510-path-probe-plan.md`.
This is an unbound candidate; SourceLanding Mutation binds the landed source.
All controls and variants below are PENDING. Ordinary red/green is not a PC cycle.

| PC | Exact method-scoped filter | Pending variants / guarded defect |
|---|---|---|
| PC-1 | `/*/*/RunnerProbePowerShellFixtureTests/WindowsApps_component_is_classified_independently_of_host` | Remove alias rejection; remove slash normalization (slash, mixed separators); use case-sensitive matching; match WindowsApps as an arbitrary substring (near-component negatives). The Windows-shaped table also includes package-qualified alias, Windows PowerShell alias, real pwsh install and empty input. |
| PC-2 | `/*/*/RunnerProbePowerShellFixtureTests/Probe_selects_regular_install_or_refuses_absence` | Accept earlier alias (alias-first); restore alias/bare-name fallback (alias-only, no-pwsh, empty-path); remove actual ReparsePoint rejection (reparse-first, real symbolic-link witness). |

Mutation owns deliberate edits, expected assertion red, exact restoration, same-method
green and missing-control discovery, including any additional native control needed
for the Windows call site. Never treat build/fixture errors, zero tests or timeouts
as assertion red. No control was executed by Code. Keep all deadlines/assertions.
Native V-3/R-3 is separately OWED; the portable seam cannot certify a Windows launch.
Restart: none.
