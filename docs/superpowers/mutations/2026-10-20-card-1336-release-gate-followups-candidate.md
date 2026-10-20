# CARD-1336 unbound SourceLanding Mutation candidate

Original Code task / landing owner: `c6cc11da`. Master-only. S1 implemented;
S2 is outside this Code dispatch. Bind the exact landed SHA and patch payloads
before execution. This inventory is not an executable mutation pack.

The [plan](../plans/2026-10-19-card-1336-release-gate-followups-plan.md)
owns all 42 controls, variants, guard mappings, named assertions and costs.
**Every PC-1 through PC-42 remains pending**, including every listed variant.
No deliberate mutant ran in Code. Mutation owns missing-control discovery and
each method-scoped baseline/red/restore/green cycle. Ordinary verification
does not discharge any positive control.

| Pending controls | Method filter | Fixed-source anchor / oracle |
|---|---|---|
| PC-1 | `/*/*/ReleaseGateFollowupTests/C1336_WrapperRef*` | RC JSON `content`, `-Ref rc/2026-10-18`; `C1336 W own-0` |
| PC-2 | Same exact method | RC JSON `content`, foreign/missing-context `SLOT=manual`; `C1336 W foreign-0` |
| PC-3 | Same exact method | RC JSON `content`, final `exit $?`; `C1336 W own-23`. Fixture clears launcher errexit before sourcing production bytes so this guard is reachable. |
| PC-4 | Same exact method | RC schedule JSON `enabled: false`; `C1336 W schedule` |
| PC-5..PC-23 | `/*/*/ReleaseGateFollowupTests/C1336_NightlyCandidateIdentity*` | S2 not implemented here; all 25 proposed `C1336 N` assertion rows and production anchors remain binding required. |
| PC-24..PC-38 | `/*/*/ReleaseGateRunTests/C599_CreditVerdicts*` | Existing credit-predicate guards in the plan; ordinary collateral execution belongs to S2. |
| PC-39..PC-42 | `/*/*/ReleaseGateRegistrationTests/C599_ScheduleProvenance*` | Existing pre-enabled/workspace/inline-token/token-file refusal guards in `register-release-gates.ps1`; exact named assertions are in the plan. |

W implements all nine planned labels: own-0, own-23, missing-0, missing-23,
foreign-0, foreign-23, schedule, master-unchanged, documentation, each prefixed
`C1336 W `. Captures use the unchanged decoded production Bash body and a
readonly builtin-only ssh function, with an empty executable PATH. No real SSH,
release-cut, publish/promote entrypoint or tag operation is permitted by these
tests. S is the existing offline registration fixture.

Keep controls method-scoped and independently prove each expected assertion red;
zero tests, build/setup errors or an earlier unrelated failure do not qualify.
SourceLanding records evidence externally and never commits or pushes its snapshot.
