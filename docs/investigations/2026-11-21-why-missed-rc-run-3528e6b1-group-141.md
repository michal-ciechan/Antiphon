# Expected-duration roster drift in rc group 141

## Introducing commit

`000375cf3440441cf3d70430eea1cf85443e79c1` added the cost row for the CARD-2004 A-1 class `Antiphon.E2E.DelegationScratchCallerTests` as part of the C1584 repair. It updated the C1584 source roster and expanded census without regenerating `tests/Shared/expected-duration-roster.json`. At rc pin `bd34a7ff6da9db1a3fa3690f824890ef9b7cc2c0` and master start `4e3adda4c5191f03be90798f79f161e80bace3eb`, both commits have the same 772-row projection and 773-row cost file. The one missing row accounts for the failure.

## Approval evidence

The [cut investigation](https://github.com/michal-ciechan/Antiphon/blob/5e00ffd553283744e1d7ffd78a4b0a2b227faf6b/docs/investigations/2026-11-20-rc-cut-2026-11-30.md) specified `/*/*/C1514LandingSplitBudgetTests/(C1584_E2EExpandedBudget*)` for the C1584 repair. The approving Review identity, scope, and executed selection could not be verified from the available evidence.

## Outside the selection

The C1584 witness checks test identities, expanded counts, and class costs. The projection comparison lives in the Antiphon.Tests Integration test at `tests/Antiphon.Tests/TestHelpers/ExpectedDurationSyncTests.cs`. It is outside the C1584 witness filter. The first observed failure was `RosterMatchesCostFile` in Windows qualification run `3528e6b1`, `antiphon-all-group-141.trx`.

## Cheapest pre-land check

`scripts/lib/nightly-class-costs.json` → `/*/*/ExpectedDurationSyncTests/RosterMatchesCostFile`

## Related investigation

The [C1584 investigation](2026-11-20-why-missed-astra-migration-and-c1584.md) covers the original missing E2E identity and its census. This group covers a second file that depends on the repaired cost row.
