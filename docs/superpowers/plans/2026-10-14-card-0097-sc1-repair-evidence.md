# CARD-0097 S-C1 D1/D2 repair

Repair task: `eaef623d-4502-40ea-bd80-772fe9a3072a`.
Original Code task / landing owner: `6812413b-f769-4554-9d4a-5b73c6a11afa`.
Branch: `feat/card-task-eaef623d`; worktree: `/work/worktrees/task-eaef623d`.
Repair starts at `5a13f07c32a13394958734dd12d3e812e85dc062`.
Full evidence-diff base: `f7ba0684c5680e250473a2310e292e23179b3642`.
Review: `/work/worktrees/task-64f32586/.antiphon/task-64f32586.md`.

Plan: [matrix plan](2026-10-14-card-0097-matrix-plan.md).
Test design and repair amendment: [matrix test design](2026-10-14-card-0097-matrix-test-design.md).
Controls: [pending Mutation candidate](../mutations/2026-10-14-card-0097-sc1-candidate.md).

## Implementation

D1: non-default RolePolicy cells with availability or pin-read diagnostics retain their
blocking empty-chain verdict. Chain-read failures expose an unknown route/stored cell,
withhold the speculative policy target, and leave the explanation in the alert. Known pin
and standing-agent bindings survive chain-read diagnostics; their stored cells read unknown.
V-C1 and V-C4 bind these cases with literal expectations.

D2: absent `defaultComplexity` leaves the headers without a default suffix and selects the
former RolePolicy boundary. V-C5 serves responses without that property and without
`effective`, then checks headers, boundary and stored-cell rendering. The Required pin banner
now says its override applies while the pin is active, covering the expiry wording disclosure.

## Ordinary scope and tests-first evidence

Final scope is CP-16 and CP-23, plus the brief's typecheck/production bundle. Server Unit and
integration classes are excluded by the slice brief. No S-C1 manual/E2E acceptance is named.
V-C1 through V-C6 and R-C1 remain required; PC-24, PC-25 and PC-26 remain pending for
method-scoped SourceLanding Mutation after Review and caller landing of the original Code owner.
Mutation also owns missing-control discovery for the repair rows. Restart: none.

CP-16 tests-first at `f49306a699debb42f7b6ed84ce1962d77c06d009`:
74 results, 12 assertion failures, 62 passes, no skipped results. Per file:
`routingSettingsModel.test.ts` 31 passed / 2 failed;
`RoutingSettingsTab.test.tsx` 24 passed / 10 failed;
`complexityChains.test.tsx` 7 passed. The twelve failures are the corrected policy/unknown
labels, older-server header, pin/agent stored-unknown assertions and banner lifetime copy.
No deliberately introduced mutant was run.

Generated evidence: `.antiphon/checkpoints/20261014-134842-386f/`.
Clean committed source was passed via `--expected-source-sha`; command-row build provenance
is `notApplicable`. Client rows emit Vitest output instead of TRX.

```text
CHECKPOINT CP-16 commit=f49306a699debb42f7b6ed84ce1962d77c06d009 build=n/a filter=pwsh -NoProfile -File scripts/test-client.ps1 src/features/settings/routingSettingsModel.test.ts src/features/settings/RoutingSettingsTab.test.tsx src/api/complexityChains.test.tsx executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a exit=1 slot=granted waited=0s dirty=0 source=f49306a699debb42f7b6ed84ce1962d77c06d009 sourceState=clean buildSource=notApplicable
```

The imported manifest uses rowMinutes 40 and the runs pass `--row-timeout 40m
--total-timeout 150m --serial`, without `--keep-outputs`. A gated checkpoint-tool bootstrap
build is an unlisted prerequisite: exit 0, two existing nullable warnings,
slot=granted waited=0s, output `tools/Antiphon.Checkpoints/bin-eaef-tool/`.
Implementation verification is pending at this commit; the stored task report records the
actual tested SHA, terminal receipts, final counts, merge, publication and cleanup facts.

The first merged-tip production build at `7ccdaf9da905148ace12dfa2279a22a4857940e3`
failed typecheck (TS2769): the new legacy-header test used an unsupported `exact` option
on `getByRole`. The repair removes that option; its string `name` remains an exact match.
Build slot: granted, waited=45s. No production assertion or timeout was relaxed.
The merge incorporated `origin/master` at `666c75d9fa1f64dde6fabcaf69b99c834cd3c951`
without conflicts; that fetched delta touched S-D scripts/tests, not the client.
