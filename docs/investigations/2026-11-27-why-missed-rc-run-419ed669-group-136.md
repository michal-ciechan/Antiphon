# Why missed: group 136 pinned a changing selector roster

Windows qualification run `419ed669`, group `antiphon-all-group-136`, failed
`ImpactedClassesSelectorTests.C1801_why_missed_paths_select_their_classes_and_windows_filters` at
`5c0883bf203d122fdc7d4556e79087af2a683669`. The same single method failed on Linux master
`8195ad899a2481f4a4c992d036ab401f41d439cf` at the exact Linux class-filter comparison.

## Introducing commit and card

The first mismatch after the previous RC pin came from the five-card Merge
`27f8ede0c0e65c73b2174e6495e791cdf299a09c`. Its refreshed index put the new CARD-2069
`ReleaseImpactEnvelopeTests` reverse edge into the bounded selection for
`tests/test-execution-policy.json`. The bound moved `ReleaseTagAllocatorTests` out of the
selected set. A selector replay at the first parent matched the literal. The same replay at
the merge yielded the one-for-one substitution.

Two more changes produced the exact RC delta:

| Commit | Selected input and effect |
|---|---|
| `ad6a181bdee9ff2c2ee10750e1d27b4668ccdb80` (remaining-PS1 unit 58) | The live-lane refresh put `LiveLaneReceiptTests` in the execution-policy selection and moved `ReleaseSelectionHopSeedTests` out. |
| `1b07bdeecea8262d9fcaa26d1c77c0e603e7a76d` (remaining-PS1 unit 62) | CARD-1801's witness replaced the deleted `scripts/lib/nightly-core.ps1` input with `tests/Antiphon.Tests/Scripts/TestSelectionTests.cs`. The former selected `ReleaseImpactScriptTests` and `SourceRevisionStampContractTests`. The latter selects `TestSelectionCatalogTests`, `TestSelectionDependencyTests` and `TestSelectionExecutionTests`. |

The RC selector replay and Linux master both returned 112 classes for the 111-name literal.
The five additions and four removals are listed above. The shared classes kept their order.
The Windows rows kept the same projects, class membership and syntax. Running the same
selector over the cut refresh `d893f5c109879e686b66c57e9290e6c67fd95aac` and its parent
gave identical filters for these inputs. The historical migration, stall sweep and cut
refresh changes did not produce the delta.

The test declaration already existed at the previous run `1fe763a9` pin
`0cfc4ac1ffb41f5af159a0c1db423fe119950267`, where selector replay matched the expected
filter. CARD-2082's timing inventory marks that run partial and group 136 TRX missing.
The previous Windows execution outcome is therefore unverified. The test was introduced
by CARD-1801 in `d3451d20e25c014df27a715cf1d7e7d3e2ef527b`.

## Approving reviews

The [CARD-2069 verification appendix](../superpowers/plans/2026-11-26-card-2069-p2-integrity-test-design.md)
selects `/*/*/ReleaseImpactEnvelopeTests/P2CanonicalBytes` and
`/*/*/ReleaseImpactEnvelopeTests/TamperedBindingsRefuse` for the new behavior.
The [round-two supplement](2026-11-26-card-2069-code.md) records the prior review
`fdfb4a55` and the extended admission witness in CP-2. The planned rows omit the
selector class. A replay of the entire landed diff through the ordinary selector can
yield the class, which does not establish that an individual subject's Review ran it.
The unit 58 and 62 plan entries list focused live-lane and nightly fixture checks.
Review scope and exact executed Review rosters were unavailable for these commits.
[Remaining PowerShell conversion plan](../superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md)

## Outside the selection

The short Merge contract verifies the subjects' narrow witness rows, policy pins and
regeneration. The Merge selector uses the fixed and hand-table subsets. It does not
use derived reverse edges to add tests. Replaying `--merge-subset --range
27f8ede0c^..27f8ede0c` produced eight Linux rows without this class. The rc cut's seven-class
guard row also omitted it. `DerivedImpactIndexTests` verifies index behavior and freshness.
The Linux `Unit` method depends on the resulting repository-wide selection even though
none of those guards asserts that selection against the old literal.
[Merge verification](../orchestration-loop.md#merge-verification-is-short-2026-11-18) ·
[Cut receipts](2026-11-26-rc-cut-gates-pass3-bdb2790af.md)

CARD-2082 includes the class with disposition **SPLIT** in its repository-invariant timing
inventory. That is a planned extraction, and the cut receipt shows that its proposed coverage
was not part of the active six-guard union plus the class-selection guard.
[CARD-2082 class timing](../superpowers/plans/2026-11-26-card-2082-class-timing.md) ·
[CARD-2082 plan](../superpowers/plans/2026-11-26-card-2082-repository-invariants-plan.md)

## Cheapest pre-land check

`tests/derived-impact-index.json` or `tests/impacted-classes.json` or
`tests/Antiphon.Tests/Scripts/ImpactedClassesSelectorTests.cs` →
`/*/*/ImpactedClassesSelectorTests/C1801_why_missed_paths_select_their_classes_and_windows_filters`,
after index regeneration. This is a proposed CARD-1782 mapping for the shipped-input
witness. The repair retains the named why-missed coverage, mirror routing, ordinal filter
shape, JSON/text agreement, and the missing-Windows-class control while allowing legitimate
reverse-edge membership changes. The separate single-path load and notification filter
assertion keeps its own exact list.

## Exemption

None.
