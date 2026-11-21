# Why rc run 3528e6b1 missed the C959 exact Codex model launch

## Introducing commit

`740c4fffb7e544a345617d1a42f1cc1b24a1ac24` moved Codex High and Medium from
`gpt-6.1-sol` to `gpt-6.2-sol`. The Windows qualification at
`bd34a7ff6da9db1a3fa3690f824890ef9b7cc2c0` reported the failure in
`antiphon-all-group-45.trx`.

The failing High row pins a standing profile's `ModelId` to `gpt-6.1-sol`.
Its dispatch fixture omitted `AgentTuiLaunchResolver`, so the profile launch
resolved through the registry ladder. The old ladder returned the same model as
the pin and masked that fixture gap. The tier bump exposed the difference.
`556bddbd2096c7ed6df9840ca9b11e20e9158d70` supplied the resolver and its
dependencies, and `d3823553d6fda95d5ba44be70a46db1dfafbf82b` supplied the
metrics namespace. The delivery assertion remains an exact `gpt-6.1-sol` pin.

## Approval evidence and selection

The pin's Code task was `464ba2aa`. Its checkpoint plan selected
`CodexCliObservationTests.C959_Ladder_and_exact_models_share_floor` in CP-4
and left the other C959 launch assertions to nightly. The archived plan gives
the intended selection. The approving review identity, scope, and executed
selection were unavailable in the checkout.
[Pin checkpoint plan](../superpowers/plans/2026-11-19-codex-0-170-pin-checkpoints.md)

## Cheapest pre-land check

`server/Application/Services/ModelLevelAliases.cs` →
`/*/*/CodexCliObservationTests/C959_Old_version_with_floor_never_refuses`

The method exercises the exact profile launch through the Integration fixture.
C959 stubs the CLI observation and launch, so this method checks the selected
argv and transcript delivery. The bundled catalog first includes GPT-6.2 Sol
in CLI `0.169.1`. The sampled `0.156.1` is below that floor and also below
GPT-6.1 Sol's recorded `0.159.1` floor.
[Catalog measurement](2026-11-19-codex-cli-0-170-and-gpt-6-2.md)

## Master verification and related failures

Linux run `20261121-202620-813a` at
`3ed773829e584eec0c93ec8b47bf3517a69204d7` executed the exact C959 method
and both C1213 branch-delivery methods using the test Postgres fixture. Each
passed once, with zero failures or skips.

The C1213 failures share the schema-lock cause documented for group 86.
`1cdc778c13c445543e18ca4048d4806d7b1f1994` is on master and postdates the
qualification tip. It avoids the repeated `ACCESS EXCLUSIVE` lock when the
alias column is already present.
[Group 86 investigation](2026-11-21-why-missed-rc-run-3528e6b1-group-86.md)
