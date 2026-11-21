# Why missed in rc run 3528e6b1, group 139

The Windows Full run at `bd34a7ff6da9db1a3fa3690f824890ef9b7cc2c0` on
`rc/2026-11-30` reported `selected` expected and `missed` observed for
`6591e378-composer-probe` in
`IncrementalSelectionSoundnessTests.Recorded_owner_map_status_matches_the_committed_map`.
Linux reproduced the same assertion at the pinned rc and at master
`831245467f933c207b356e45f8d0a6733b9d2259`. Each run executed two tests,
with one pass, one failure and zero skips.

## Introducing commit/card

CARD-1820 S3 commit `a2cfdce211b7005a87de9989723f35ee434fea11`
replaced the catch-all root with explicit roots and an inventory that walks
project references and linked compile inputs. The S2 replay from
`d63fce0464007c06a358f06957ce2632101566b8` kept comparing only explicit
roots to the historical path lists.

The [S2 replay record](2026-11-19-incremental-selection-soundness-replay.md)
describes how `git diff-tree --name-only` supplied the path lists, including
the composer commit `754d02f72f91245c85427f1ed2db7aa7ea9be7f3`. S2
checked in the expected `currentMap` values in
`tests/fixtures/incremental-selection-soundness.json` based on the
catch-all at that time. The repository has no separate generator for those
status values.

The S3 inventory at the pinned rc records six matching paths for the Pty
suite. The suite references `src/Antiphon.Agents.Pty/Antiphon.Agents.Pty.csproj`,
which accounts for `ComposerInputProbe.cs` and `ComposerDeliveryEvidence.cs`.
The suite also owns its three changed tests, and its FakeClaude reference owns
`src/Antiphon.FakeClaude/Program.cs`. All twelve current-map expectations
match the inventory. The linked resilience helpers also have `Compile` edges
in the inventory and remain the recorded miss of the separate M1 experiment.
Master has the same owner-map blob. CARD-2009 changes test identity parsing
and census checks, and the old test still reads explicit roots alone.

The repaired test uses `Read-RcInputInventory`, the same inventory function
`Get-RcInputManifest` calls before comparing immutable revisions and applying
test-only deferral. The fixture captures path ownership over historical path
sets. The expected composer status stays `selected`. The replay builds one
inventory for all cases. Literal project inputs now bypass the file scan
reserved for globs. At the pinned rc, the baseline inventory and the optimized
probe reported the same twelve results and flag counts, with respectively
272 and 69 seconds elapsed on server2. The 120-second process budget is
provisional from that sample. The existing M1 closure test still uses its
60-second budget.

## Approving reviews

- CARD-1820 S3 owner-map approval `3fa5f039`, Final/Full, run
  `20261120-054819-2ff5` at `06c6ff4635eaa432db0a0a91e76817ec83d3069b`.
  CP-1 ran `RcInputOwnersMapTests`, `RcInputManifestTests` and
  `RcChunkInputDigestTests` (32/32), CP-2 ran nine classification/policy
  guards (81/81), CP-3 ran `DerivedImpactIndexTests` (27/27), and CP-4
  through CP-6 ran the land graph, live-lane pin and census checks.
  [Review record at ca346f262](https://github.com/michal-ciechan/Antiphon/blob/ca346f262563ca1619cec5bcd864ffddb60de3e7/docs/reviews/2026-11-20-card-1820-s3-owner-map-round4-review.md)
- The later S3 test-only review `0d878484` approved `b3e97d8f` after an
  81-test owners-and-guards subset. Its record reports an incomplete ordinary
  scope. CARD-2009 round 3 subsequently ran `RcInputOwnersMapTests` (34/34),
  the nine-class guard union (84/84), `DerivedImpactIndexTests` (27/27) and
  the literal owner census. Both later selections also omitted the S2 class.
  [S3 follow-up review](https://github.com/michal-ciechan/Antiphon/blob/0263ae5c30bc5a2e569a7828ba748aa1931f2aec/docs/superpowers/reviews/2026-11-20-s3-testonly-review-r2.md),
  [CARD-2009 review](https://github.com/michal-ciechan/Antiphon/blob/4ead7c286/docs/superpowers/reviews/2026-11-21-card-2009-review-r3.md)

## Outside the selection

S3 CP-1 covers the Integration owner-map, manifest and digest harnesses.
Those harnesses call the production selector. The S2 replay is a separate
Unit class under `tests/Antiphon.Tests/Scripts/`, and its duplicate
root-only evaluator was outside the S3 checkpoint filters. Neither the
classification guards nor the reader census executes that class.

## Cheapest pre-land check

For CARD-1782, select
`/*/Antiphon.Tests.Scripts/IncrementalSelectionSoundnessTests/Recorded_owner_map_status_matches_the_committed_map`
for `scripts/lib/rc-input-owners*.json`, `scripts/lib/rc-input-noop.json`,
`scripts/lib/rc-input-manifest.ps1`, `scripts/test-rc-input-owners.ps1` and
`tests/fixtures/incremental-selection-soundness.json` changes.

## Exemption

None for the owner-map replay. The other group-139 failure,
`RcShadowDirtyDifferentialTests.CorpusMatchesGolden`, calls
`RcPortCorpus.Replay(null)`. Its dictionary-to-PSObject conversion calls
`RcPortCorpus.Automation()`, which uses `RcPortPowerShellAssembly.Load()`
on Debug 592e48bc tip `14057e5ed305df85a7239ef8657b9b4bdbe2f8d0`.
It shares the group-136 PowerShell assembly-path repair.
