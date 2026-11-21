# Why the C519 probe's satellite-resource setting reached master

Commit `b93c459685d670472d17c3ec0941d77c5e2b4f07` introduced
`SatelliteResourceLanguages=en` in `Antiphon.ChannelOutbound.Probe.csproj`
as part of the generated EF compiled-model work for CARD-0519. The earlier
C519 shared-broker commits are distinct from the generated-model batch.
The local master fetch first observed the batch at
`5d82c3bda60decec5776d3061607a535ee8b25d9` at 08:29 UTC on November 21.

## Review and landing evidence

The three generated-model review-fix commits (`094eaf4a39`, `8d5274dffe` and
`b6e51daa09`) document changes to test classification, model comparison,
process timeouts and cleanup. The checkout and available remote refs have
no matching report under `docs/superpowers/reviews/`. The Antiphon board
task list for November 20–21 also returned no matching Review. No retained
evidence establishes that a reviewer ran the audit or identifies the
publication command.

The parent of the introducing commit, `2c57d790100165d42287a3098bad63fe1e421eee`,
fingerprints to the manifest then in force,
`78497A2C9CC6C908D77259B0181A87AA4CA1B25F923AE358C16DD2DB2D1FA181`
with 80 selected definitions. The introducing commit changed two selected
project files and carried the old manifest. At Debug start
`2f3caabcc83d77b043806ab80a8d3d6ef1d95055`, `-Check` reproduced exit 1
and named `SatelliteResourceLanguages` in its pre-check refusal. That
pre-check rule was already in place when the batch reached master.

The [inherited-mismatch rule](../orchestration-loop.md#land-graph-refresh)
allows a warning for an inherited manifest or digest mismatch and explicitly
requires hard refusal for an unadmitted property. The original batch had
both a matching parent digest and a newly unadmitted property. Any subsequent
batch still sees the property refusal before the digest comparison. The
server's [land verification policy](../../server/Application/Services/LandVerificationPolicy.cs)
uses the definition digest to decide whether a docs-only candidate can skip
the build. That policy can let a candidate pass its build while the separate
audit would refuse the graph. The published history establishes a missing
enforced audit result for the introducing batch. It leaves open whether the
audit was skipped or its result was bypassed.

## Repair

The .NET SDK passes `SatelliteResourceLanguages` to `ResolvePackageAssets`.
The literal `en` limits package satellite resources copied to the probe's
output. `CopyStagedHelpers` stages that output under `channel-outbound-probe`
in `Antiphon.Tests`. Removing the setting would stage extra localized Roslyn
resources. The C519 tests run the probe and its generated model with neutral
assemblies. The resource filter is an output-size optimization.

The pre-check now admits the single reviewed literal `en` through its existing
pinned-property rule. The witness covers the literal, an unreviewed culture
and an MSBuild expression. The full audit also evaluates the solution and
checks all target and evaluation pins.

The repair refreshed the records through `scripts/land-graph-land-refresh.ps1`
with `-Base 2c57d790100165d42287a3098bad63fe1e421eee`, the ancestor whose
fingerprint matches the old manifest. The refresh commit
`1ecef2e25abb1d45e891afb15886167b667eb2d6` covers the two changed project
records and adds the conditional `System.Management.Automation` package
pin from `Antiphon.Tests.csproj`. The selected count stays at 80 and the
digest is `819858AE650809E79A56F06FEC131EE85FA720D2A05DCF53293403A766F7B8DD`.
