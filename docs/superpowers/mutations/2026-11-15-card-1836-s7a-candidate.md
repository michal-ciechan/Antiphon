# CARD-1836 S7a unbound Mutation candidate

Original Code/landing owner 7342010d. This document is a candidate, not a bound
SourceLanding pack. All controls and variants below remain pending. The operator
waived deliberate mutation execution in Code; ordinary red/green is separate.
Caller commissions SourceLanding Mutation after ordinary Review even with zero PCs.
Use the commissioned immutable snapshot/evidence root, not this mutable branch.

| PC | Exact RcReleaseBaselineTests method | Deliberate defect / independent variants |
|---|---|---|
| PC-V7-precedence | AncestorReleaseThenSameLineFallback | Prefer rc over release; ignore explicit recorded release; reverse committer-date order; reverse ordinal tie; depend on pointer enumeration order |
| PC-V7-ref-pin | CrossPlatformMovedAndCorruptRefRefuse | Accept a moved release ref; ignore recorded SHA/digest conflict; accept duplicate conflicting pointers; silently replace missing recorded pointer |
| PC-V7-platform-lineage | CrossPlatformMovedAndCorruptRefRefuse / MissingBaselineIsFull | Ignore matching platform; select unrelated orphan; allow other rc line |
| PC-V7-envelope | CrossPlatformMovedAndCorruptRefRefuse | Skip bad admission seal to convenient rc; omit final seal/reader verification; accept unverified source/build/launch proof |
| PC-V7-anchor-history | AncestorReleaseThenSameLineFallback / MissingBaselineIsFull | Drop selected Full anchor; remove inherited algorithm Full gate; drop later failure facts; clear failures using older green; accept incomplete history |
| PC-V7-no-baseline | MissingBaselineIsFull | Report carry/green with zero eligible pointers |

Bind each variant to the smallest method that discriminates it. Discover missing
controls here, including final-read interleavings; do not claim a table promise as
an existing witness. Positive controls must reach the intended outcome assertion,
not a build/fixture error or zero tests. Red, restore and green are method-scoped;
never widen a timeout or loosen an assertion. No current outcome is a PC result.
