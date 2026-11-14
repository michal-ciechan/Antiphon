# CARD-0824 F1-F4 round 2: Application roots seam

Task/landing owner: `1a125507-3ada-4753-a1c2-5462483a3e19`. Original producer
Code owner: `d684bc25-111b-46a0-b474-c7255a7a66d5`.
Base: `27a43f25221c1508f12a2c6a4b89500d0050986a`.
Parent: [accepted plan](2026-11-14-card-0824-all-roles-enablement-plan.md).

The repair preserves the exact `GitSettings` argument and default `create=false`
of `WorktreeRoots.Resolve`. DelegationWorktreeService already owns its allowed
edge. Move that call behind a method there, used by both preflight and producer.
Do not normalize again, change case/separators, create a directory, consult a
different settings instance, change admission, or extend the allowlist.

The repository witness inspects listed and unlisted forbidden edges regardless
of warn mode and rejects ambiguous forbidden references. Its pre-fix assertion
red is required; the full guard inventory must change VIOLATIONS 1 to 0 with
LISTED 162 unchanged. Deliberate old-edge restoration belongs to SourceLanding
Mutation, method-scoped on this witness, pending ordinary Review.

## Performance budget

Rows run serially, with a provisional 10-minute row/build bound and separate
slot admission, under CARD-0824/CARD-1906 load constraints. First red/green
provides the current sample; no timing-only repetitions. Main cold build is
historically 4-10 minutes; body guard scan is seconds to a minute. Native output
bootstrap of the coordinator and derived-index driver is explicitly required.
Other F checkpoints remain the parent's closed rows CP-1..CP-7, CP-14..CP-16,
CP-23..CP-30, plus its named guard union and gate-off callers from the brief.
Run each bounded committed group separately; no whole Unit or assembly run.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| R2-RED | red | tests/Antiphon.Tests -> bin-c0824-r2-red/ | portable-layering-red | `/*/*/C1729LayeringGuardTests/*` | warn-mode producer edge, existing query witness, inventory | one producer assertion red before fix; VIOLATIONS 1 LISTED 162 | 10 | 5 | true |
| R2-GREEN | fix | tests/Antiphon.Tests -> bin-c0824-r2-green/ | portable-layering-green | `/*/*/C1729LayeringGuardTests/*` | same full guard after seam | all pass; VIOLATIONS 0 LISTED 162 | 10 | 5 | true |

Windows CP-8 and live canary remain pending, never credited by Linux skips.
Parent F/P/L acceptance and all PC variants retain their original obligations.
No ordinary V-n/R-n acceptance IDs are declared by the parent F scope.
