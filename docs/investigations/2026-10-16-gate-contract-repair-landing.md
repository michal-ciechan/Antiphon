# RC2 gate contract repair and CARD-1303 R3 landing

Current status: in progress. The caller authorized the production policy change
after the initial stop, citing clean Full Grok Review `3c95fb03`. The gate repair
has been published to RC and master. R3 is published to RC; master remains pending. The
initial stop below is retained as historical evidence.

## Initial stop (historical)

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

## Original required decision (resolved by caller)

Provide a corrected test/inventory-only source range, or explicitly recommission
landing of the production repair with its required review and verification
requirements. The existing test-only authorization does not cover this range.

## Authorized continuation

The caller explicitly included the seven policy lines in scope and commissioned
Part A and Part B again. The caller also commissioned CARD-1303 R3, citing clean
Grok Review `c1bd8e6c`, with `reviewedSourceClean=true`.

The R3 range
`b227f814c02a88ea2e4c9f0731c22d8ff0ffcd4d..f30b84754bcea458b206e3a67b915c78c493c448`
on `origin/feat/card-task-6121c04b` contains exactly:

1. `953c0f9a18390e649f919adc14ffc3bfc113f2ea`
2. `5503f4f428b4b9b6d445b65ac163b5f80aa06cdc`
3. `b59d918bb97ed95719589342b0ec78eed34e7b2d`
4. `f30b84754bcea458b206e3a67b915c78c493c448`

Its six-file delta is limited to `AgentTaskService.cs`'s admission lock mode,
`PipelineSpeedIntegrationTests.Admission.cs`, the shared observers/probe in
`PipelineSpeedIntegrationTests.cs`, and its plan/evidence/mutation notes. No
additional production changes were found.

### Publication receipts

| Publication | Target | Old SHA | New SHA | Verification |
|---|---|---|---|---|
| Gate repair | RC | `41d6f6adf431622b3982f9ac41417a8e555072ba` | `46fd97a187866e26f56a6f261038ada4d7d57870` | Build passed; 89/89 tests passed, zero skipped |
| Gate repair | master | `bc3168594e598cb4206b5e11f6d3aabc04561fd9` | `7bb30f231217c8a28ad549b40f7964764ebf354e` | Build passed; 89/89 tests passed, zero skipped |
| CARD-1303 R3 | RC | `46fd97a187866e26f56a6f261038ada4d7d57870` | `b7265a05aa8773f08dbf457b93c7561e7874822d` | Build passed; regression 1/1, class 36/36 passed, zero skipped |

The RC gate cherry-picks are
`48d1edaabe618087b478471693fb1f19e0954871` and
`46fd97a187866e26f56a6f261038ada4d7d57870`, each with `-x` provenance and
a clean tree between them. The target push was a fast-forward, confirmed by
`git ls-remote`.

The RC build used `scripts/build-slot.ps1`, `OutputPath=bin-5160-rc-gate/`,
and `UseAppHost=false`; it completed in 2m20s with 1060 warnings and zero errors.
All four drivers held granted host build slots. Fresh TRX files verified the
registry method (1), `C487_G059` (1), and the complete `AbsentLaunchPolicyTests`
class (87), including `C1149_Queue_columns_are_accounted_for`.

The master gate cherry-picks are
`398d3cddd3456c0e976ca955fe2538214cf63fad` and
`7bb30f231217c8a28ad549b40f7964764ebf354e`. Both applied cleanly, with `-x`
provenance and a clean tree between them. No registry conflict occurred on current
master; Git automatically retained the reviewed superset, and the resulting
registry is byte-identical to the repair source's registry. No existing entry was
removed. The resulting hunk is:

```diff
 # CARD-1032 S7: real PostgreSQL, scratch Git, phone-home recipient and receipt refusal matrices.
 Antiphon.Tests.Application.CardWorktreeMirrorCleanupTests
+# CARD-1259: isolated PostgreSQL commit faults, durable action debt and hosted worker lifecycle.
 Antiphon.Tests.Application.SessionRuntimeActionTests
 # CARD-1259: isolated schema recovery, real queue and retained transcript leases.
 Antiphon.Tests.Application.SessionRuntimeActionRecoveryTests
 # CARD-1259: hosted pump, actual review producer and real queue receipts on isolated schemas.
 Antiphon.Tests.Application.SessionRuntimeActionRoutingTests
+# CARD-1259: isolated schemas, real queue deferral/backoff and transcript retention leases.
+Antiphon.Tests.Application.SessionRuntimeActionDeferralTests
```

The master gate build used `OutputPath=bin-5160-master-gate/` and
`UseAppHost=false`, finishing in 2m03s with 1062 warnings and zero errors. The
same three selections passed 1 + 1 + 87 cases in fresh TRX files. All drivers held
granted host build slots, and `git ls-remote` confirmed the fast-forward push.

The R3 RC cherry-picks, in source order, are:

1. `09e768077c986c5408be8d90b8ae8937f8545873`
2. `c55c33a0779e2191245de6d625e474cda2056b40`
3. `4b956c1c13a534f3cbb21ab76313df9435cf6543`
4. `b7265a05aa8773f08dbf457b93c7561e7874822d`

All applied cleanly, individually with `-x`, and with a clean tree between picks.
The isolated R3 RC build used `OutputPath=bin-5160-rc-r3/` and
`UseAppHost=false`: 2m17s, 1060 warnings, zero errors. The exact regression passed
1/1 in 53s; the complete `PipelineSpeedIntegrationTests` class passed 36/36 in
5m55s. Both actually used PostgreSQL; no build-only fallback was necessary. Fresh
TRX rosters include `C1303_CardAdmissionAllowsClaimSessionInsert` in both runs.
All three drivers held granted host build slots. Remote readback confirmed the
fast-forward RC publication.

Master advanced independently after the gate repair, through two CARD-1291 docs
commits, to `56a1550eb895fabfac6eabc7c1ba6054128bced3`. R3's master publication
will start at that current remote tip, in a fresh detached landing checkout.

Raw build logs and TRX evidence remain ignored under
`.antiphon/landing-evidence/`. Separate detached landing checkouts keep the
assigned task branch unchanged except for this report. No canonical checkout,
AppHost, runner, or tag has been changed.

--- next stage ---
next: merge
handoff: Publish R3 to current master with narrow verification.
artifact: docs/investigations/2026-10-16-gate-contract-repair-landing.md
