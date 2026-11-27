# Why missed: P2 admission matrix timeouts on Windows

## Introducing commit and card

CARD-2069 landed in batch `5855e61481fa0f80f015ae1a4e8584619b96eb40`. Windows rc run
`419ed669` at `5c0883bf203d122fdc7d4556e79087af2a683669` timed out in group 138.
`GraphAndTrustBindingsRefuse` ran 30.15 s and `TamperedBindingsRefuse` ran 30.27 s.

## Approving review

Round 3 Final Review `801d2b17` selected
`/*/*/ReleaseImpactEnvelopeTests/TamperedBindingsRefuse`,
`/*/*/ReleaseImpactEnvelopeTests/GlobalContractChangesRefuse` and
`/*/*/ReleaseImpactEnvelopeTests/GraphAndTrustBindingsRefuse`. The reported Linux runs
took 8.48 s, 4.46 s and 14.00 s respectively.

## Outside the selection

The Unit methods were selected on Linux only. Native Windows under qualification load was outside
the review's timing sample. The 30-second Windows observations are timeouts and give only a lower
bound on the old fixtures' duration.

## Cheapest pre-land check

`tests/Antiphon.Tests/Scripts/ReleaseImpactEnvelopeTests.P2*.cs` →
`/*/*/ReleaseImpactEnvelopeTests/*` on native Windows with phase timings and the matrix methods'
per-test durations retained.

## Exemption

None.
