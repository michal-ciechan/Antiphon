# Why master verification missed the CARD-1199 closure documentation check

Commit `20d801e8a003d7026dfc31e77d933ad39533705a` added the two portable
CARD-2006 SDK-fixture census cases to the compiled roster, bringing the required
set to 37 on both platforms. It revised `docs/release-gates.md` to identify the
35-case, zero-runtime-skip census as the original CARD-1199 result. The
`C1199_FinalClosureDocumentation` unit test still required the old sentence
`CARD-1199 runtime skip census: 0 of 35.`

The roster witness in `ReleaseGatePlatformTests.C1199_WindowsRequiredRoster`
checks compiled identities and platform policy. The stale prose assertion
lives in `ReleaseBranchModelDocumentationTests`. Reconstructing the ordinary
impacted selection for `20d801e8a^..20d801e8a` selects
`PerTaskFinalUnitLaneWitnessTests`, `ReleaseGatePlatformTests` and
`TestLaneCategoryGuardTests`. It misses the documentation class because the
reverse index cannot discover the path from the separate `"docs"` and
`"release-gates.md"` literals. The short Merge selection for that range also
omits the class. Its fixed release-gate row selects
`C1686_LinuxOnlyCensus` and `LinuxOnlyWindowsExclusionPolicy`.

The cheapest pre-land check for an edit to this closure text is one checkpoint
row using the existing Antiphon.Tests build with the exact filter
`/*/*/ReleaseBranchModelDocumentationTests/C1199_FinalClosureDocumentation`.
The class has three lightweight Unit tests and is the appropriate selection
when an edit also affects release-branch pointers. The test now uses a single
repo-relative path literal so the regenerated reverse index selects the class
for future edits to `docs/release-gates.md` in ordinary impacted verification.
The short Merge selection uses its hand-maintained subset, so an edit on that
path still needs the explicit row there.
