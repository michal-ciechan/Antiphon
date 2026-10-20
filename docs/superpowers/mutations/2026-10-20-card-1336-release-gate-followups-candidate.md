# CARD-1336 unbound SourceLanding Mutation candidate

Original S1 Code task / landing owner: `c6cc11da`.
Stacked S2 Code task / landing owner: `cde61484-46ec-4c8a-8999-3f95c155b1c3`.
Master-only. S1 and S2 implemented. Bind the exact landed SHA and patch payloads
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
| PC-5..PC-23 | `/*/*/ReleaseGateFollowupTests/C1336_NightlyCandidateIdentity*` | S2 admission in `nightly-run-impl.ps1`: parser `-Sha`, derived local/result assignments, ordinal explicit-id check, existing ref/SHA/root/sync guards, selection requests, child arguments, state and credit writers. All 25 `C1336 N` rows are implemented; exact mutations/oracles remain those in the plan. |
| PC-24..PC-38 | `/*/*/ReleaseGateRunTests/C599_CreditVerdicts*` | Existing credit-predicate guards in the plan; S2's CP-4 includes this collateral method. |
| PC-39..PC-42 | `/*/*/ReleaseGateRegistrationTests/C599_ScheduleProvenance*` | Existing pre-enabled/workspace/inline-token/token-file refusal guards in `register-release-gates.ps1`; exact named assertions are in the plan. |

W implements all nine planned labels: own-0, own-23, missing-0, missing-23,
foreign-0, foreign-23, schedule, master-unchanged, documentation, each prefixed
`C1336 W `. Captures use the unchanged decoded production Bash body and a
readonly builtin-only ssh function, with an empty executable PATH. No real SSH,
release-cut, publish/promote entrypoint or tag operation is permitted by these
tests. S is the existing offline registration fixture.

N copies current source into a fresh committed local Git fixture for every
invocation. It uses the real selection worker/validator and real self/workload
children, with parameter observers and synthetic workload/report I/O. Its
`omitted` expected identity uses the literal date plus the independent committed
SHA prefix, never the parser under test. The workload observer records the real
result and adds an optimistic reportDelivered field to synthetic summary I/O so
the NoReport guard remains reachable. Master defaults are redirected only to a
private sentinel root. No identity, selection or credit validator is replaced.
The unmodified harness deadline remains 300 seconds plus 10 seconds cleanup.
All controls and every listed variant remain pending; no Code result grants
release credit, publication authority or PC-clean status.

Keep controls method-scoped and independently prove each expected assertion red;
zero tests, build/setup errors or an earlier unrelated failure do not qualify.
SourceLanding records evidence externally and never commits or pushes its snapshot.
