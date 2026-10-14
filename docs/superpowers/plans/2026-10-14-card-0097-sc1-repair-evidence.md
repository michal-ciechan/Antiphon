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

D1: non-default RolePolicy cells with availability diagnostics retain their
blocking empty-chain verdict. The pin-read verdict recorded by the first repair was rejected
by Final Review 792ca57b D3; the amendment below replaces it. Chain-read failures expose an unknown route/stored cell,
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

## D3 repair (f6a6f9a0)

Task `f6a6f9a0-619f-44ee-9c57-964524c60752`, branch `feat/card-task-f6a6f9a0`,
worktree `/work/worktrees/task-f6a6f9a0`, starting at
`3678d79f7f5050d762d8b4a523a8d7d2992d65e0`. Landing owner remains
`6812413b-f769-4554-9d4a-5b73c6a11afa`. Review source:
`/work/worktrees/task-792ca57b/.antiphon/task-792ca57b.md`.

The server's `ReadPartAsync("stage pin", ...)` falls back to `Decision.None`, then
`ComplexityEffective.Compose` can return source `rolePolicy` with a policy candidate.
That candidate does not establish routing when the pin read failed. Both tier variants
must show `Effective routing unknown`, preserve the diagnostic alert and report
`Stored cell: unset` when the chain read succeeded. The marker is the exact diagnostic
segment prefix `stage pin read failed (`; segments are joined by `; `.

Tests-first slice adds eleven cases, including Required/Preferred banners from the separate
pin endpoint, and corrects the existing non-default expectation. At
`fd94dfcec891f4449449d6c472865d444fe78ece`, CP-16 executed 85 cases: 74 passed,
11 assertion failures, zero skipped. The model file had 34 passed / 4 failed (38);
the tab had 33 passed / 7 failed (40); the API file had 7 passed.
The four model failures and seven DOM failures name the D3 pin-read rows. The
free-text negative case passed; it guards a future loose-substring implementation.
Mutation owns its deliberate-defect control, which has not been run.

```text
CHECKPOINT CP-16 commit=fd94dfcec891f4449449d6c472865d444fe78ece build=n/a filter=pwsh -NoProfile -File scripts/test-client.ps1 src/features/settings/routingSettingsModel.test.ts src/features/settings/RoutingSettingsTab.test.tsx src/api/complexityChains.test.tsx executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a exit=1 slot=granted waited=0s dirty=0 source=fd94dfcec891f4449449d6c472865d444fe78ece sourceState=clean buildSource=notApplicable
```

The shared `policyRoutingUnknown` predicate controls the verdict and the candidate list.
The stored-cell label still depends on chain-read success, independently of pin-read success.
No test timeout changed. Final ordinary verification is pending at this implementation commit.

### Combination audit after D3

This is a source audit of `ComplexityChainService.GetEffectiveAsync`, `ReadPartAsync`,
`ComplexityEffective.Compose`, `RolePolicyReadService.PolicyTarget`, and the model/tab.
It is not a claim of executed server integration coverage. D = default tier, N = non-default;
the single-failure axis is none, stage pin, chain, chain metadata, standing agent, availability.
"Present" means an applicable stage pin; expired/inapplicable pins resolve like absent pins.

| Response source / applicable pin | Single read failure | D statement | N statement | Evidence / feasibility |
|---|---|---|---|---|
| rolePolicy / absent | none | Policy fallback target | Empty explicit tier blocks | V-C1 role-policy; V-C5 default-column-and-fallback |
| rolePolicy / absent | availability | Policy fallback target with unavailable diagnostic | Empty explicit tier blocks with alert | Model source audit; V-C4 non-default-availability-failure |
| rolePolicy / absent | chain | UNKNOWN; stored UNKNOWN | UNKNOWN; stored UNKNOWN | V-C1 read-failure rows; V-C4 policy-read-failure, empty-read-failure, default-policy-read-failure |
| rolePolicy / absent or present but unreadable | stage pin | UNKNOWN; stored unset | UNKNOWN; stored unset | V-C1 pin-read rows; V-C4 default/non-default pin-read rows and four banner variants |
| rolePolicy / present and read succeeds | none, chain, metadata, standing agent, availability | Different response source | Different response source | Compose yields pin, pin+chain or agent for an applied target pin; no rolePolicy verdict is reached |
| rolePolicy / absent | metadata, standing agent | Read not requested | Read not requested | Metadata requires role/any chain; standing-agent read requires an applied agent pin |
| role, any, config / absent | none | Own rule / Inherits Any role / Configuration fallback | Same rule-origin statement | V-C1 own-rule, any-role, configuration; these labels describe the chain origin |
| role, any, config / absent or present but unreadable | stage pin | Rule-origin statement + diagnostic | Same | V-C1 role-with-read-failure; V-C4 role-read-failure; origin does not assert a selected dispatch target |
| role, any / absent | metadata | Rule-origin statement + diagnostic | Same | V-C1 any-with-read-failure; V-C4 any-read-failure; metadata failure leaves loaded candidates intact |
| role, any, config / absent | availability | Rule-origin statement, unavailable candidate diagnostic | Same | V-C1 config-with-read-failure; V-C4 config-availability-failure |
| role, any, config / absent | chain | Becomes rolePolicy, UNKNOWN | Same | Load fallback has resolvedFrom none; policy chain-read rows above |
| role, any, config / present and read succeeds | none, chain, metadata, standing agent, availability | Pin/agent response source | Same | Applied target pin composes above chain origin |
| config / absent | metadata | Read not requested | Read not requested | Metadata is requested for role/any only |
| role, any, config / absent | standing agent | Read not requested | Read not requested | No applied agent pin |
| pin, pin+chain / present | none | Required/Preferred pin target | Same | V-C1 required-pin/preferred-pin; V-C4 pin-overlay-first |
| pin, pin+chain / present | chain | Known pin target; stored UNKNOWN | Same | V-C1 pin-with-read-failure; V-C4 pin-read-failure; Preferred uses same stored-cell branch |
| pin, pin+chain / present | metadata, availability | Known pin ordering + diagnostic | Same | Source audit: neither read changes Compose's pin source; candidate availability stays visible |
| pin, pin+chain / present | stage pin | Becomes chain source or rolePolicy | Same | Pin decision falls back to None; rows above apply |
| pin, pin+chain / absent | any listed failure | Response source not produced | Same | No applied pin candidate |
| pin, pin+chain / present | standing agent | Agent response source | Same | An applied agent binding selects Compose's agent branch |
| agent / present | none, chain, metadata, standing agent | Standing-agent binding; stored UNKNOWN for chain failure | Same | V-C1 standing-agent/agent-with-read-failure; V-C4 standing-agent/standing-agent-read-failure; unreadable label uses id (unavailable) |
| agent / present | availability | Standing-agent binding + diagnostic from stored chain | Same | Source audit: agent effective candidates are empty; stored-chain availability may fail |
| agent / present | stage pin | Becomes chain source or rolePolicy | Same | Read failure removes the applied decision; rows above apply |
| agent / absent | any listed failure | Response source not produced | Same | Agent branch requires an applied agent pin |

Unsupported policy is not an I/O read failure. With no readable pin and no chain, a rejected
policy yields the default-tier not-launchable explanation; an explicit non-default empty tier
blocks (V-C1 unlaunchable-policy; V-C4 non-default-unlaunchable-policy). Adding the stage-pin
failure changes that verdict to UNKNOWN (V-C1 pin-read-failure-after-policy-diagnostic).
An unusable Required candidate with no remaining target produces UNKNOWN via the empty-list
branch. These outcomes do not invent a runnable policy target.

Scope disclosure retained from Review 792ca57b: role/any/config labels and candidate lists
remain visible on pin-read failure as chain-origin information. The inherited Preferred-pin
projection may include a policy fallback slot for an empty non-default row. D3's assigned
repair changes rolePolicy-source verdicts; it does not rewrite that server projection.
