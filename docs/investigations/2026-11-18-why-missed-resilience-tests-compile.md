# Why missed: Antiphon.Resilience.Tests did not compile on master ae7bf7368

One committed note for the master compile break found by Investigate `4ac60096` (its note
`docs/investigations/2026-11-18-slow-direct-land-35-min.md` is on `feat/card-task-4ac60096` at
`ed55f4ad7dbb9386a542c1d9cfef31bcea6bcf3d`). Debug task `65ca8494`.
Owner: [why-missed write-up](../orchestration-loop.md#why-missed-write-up-card-1781).

## Failure

`dotnet build tests/Antiphon.Resilience.Tests/Antiphon.Resilience.Tests.csproj` at master
`ae7bf7368231d819f555386350e1c30b185a589a`:

```
tests/Antiphon.Tests/TestHelpers/TestDbFixtureLifecycle.cs(800,30): error CS0103: The name 'TestDbFailureEvidence' does not exist in the current context [tests/Antiphon.Resilience.Tests/Antiphon.Resilience.Tests.csproj]
tests/Antiphon.Tests/TestHelpers/TestDbFixtureLifecycle.cs(812,30): error CS0103: The name 'TestDbFailureEvidence' does not exist in the current context [tests/Antiphon.Resilience.Tests/Antiphon.Resilience.Tests.csproj]
```

The land verifier builds the whole solution for every non-docs diff, so every code land to
master was refused `verification_failed` after about 7 minutes.

Classification: **build defect in a mirror project**. `Antiphon.Resilience.Tests` (CARD-1829 mirror)
links Antiphon.Tests helpers one file at a time. `TestDbFixtureLifecycle.cs` started using a type
from a new file that the mirror did not link.

## Introducing commit/card

introducing commit/card: `2c1b05349067731304b6a237e5f06511615675e5` (cherry-pick of
`951ed73f8f32a8f4f73fa05d12f654b1a46b242a` from Debug branch `feat/card-task-fda961b9`, rc run
`f2de5b30` profile-413, no CARD number), landed on master with batch 270 (Merge 268, tip
`ae7bf7368`). It added `tests/Antiphon.Tests/TestHelpers/TestDbFailureEvidence.cs` and two uses in
`TestDbFixtureLifecycle.cs` (lines 800 and 812). Every file it touched is under `tests/Antiphon.Tests`.

## Approving reviews

approving review(s) with scope (Full/Interim) and the selection they actually ran:

- Review of `feat/card-task-fda961b9`: not established from this runner (UNOBSERVED). Its rows, and
  the Merge 268 rows, are Antiphon.Tests rows: the profile-413 witness lives in
  `tests/Antiphon.Tests`, and [Merge verification is short](../orchestration-loop.md#merge-verification-is-short-2026-11-18)
  allows "one build", in practice of `tests/Antiphon.Tests`.
- No land verifier build ran between batch 270 and the failed land: Investigate `4ac60096` found no
  `Landing verification decision` line 2026-11-15..18 before 12:44Z, so batch 270 reached master
  without the full-solution build that would have failed.

## Outside the selection

why the failing test was outside that selection (lane/category/path): no test failed; a **project**
failed to compile. `tests/Antiphon.Resilience.Tests` is a separate project in `Antiphon.sln` that
compiles linked copies of `tests/Antiphon.Tests/TestHelpers/*.cs`. Building `tests/Antiphon.Tests`
compiles the new file and succeeds; only a build of the mirror project (or of `Antiphon.sln`) sees
the missing link. The lanes that build every project are the nightly
(`scripts/lib/nightly-tests-impl.ps1:2096`, `dotnet build Antiphon.sln`) and the server land verifier
(full `dotnet build`). A Merge that pushes a batch to master without the land verifier runs neither.

Six projects link Antiphon.Tests files one by one; all six build on master + this fix
(`Antiphon.Resilience.Tests`, `Antiphon.GitInProcess.Tests`, `Antiphon.Observability.Tests`,
`Antiphon.SessionRunner.Tests`, `Antiphon.Checkpoints.LifecycleHost`,
`scripts/regenerate-derived-impact-index/Antiphon.DerivedImpactIndex.csproj`).

## rc exposure

No `rc/*` ref (33 checked, through `rc/2026-11-27` `e229fb4d3`) contains `2c1b05349` or
`951ed73f8`, none has `TestDbFailureEvidence.cs`, and `rc/2026-11-27`'s
`TestDbFixtureLifecycle.cs` does not name the type. rc/27 therefore does not carry this break
(source inspection; rc was not built). An rc port of `2c1b05349` must carry the
`Antiphon.Resilience.Tests.csproj` link from this fix in the same pick.

## Cheapest pre-land check

the cheapest pre-land check as a path-glob -> class-filter entry for the impacted-classes table of CARD-1782:

`tests/Antiphon.Tests/TestHelpers/**` -> `/*/*/LinkedTestHelperClosureTests/*`

`LinkedTestHelperClosureTests` (Antiphon.Tests, Unit, ~1.5 s, builds nothing) reads every csproj that
links a `tests/Antiphon.Tests` file and fails when a linked file names a type whose own file
(`TypeName.cs` under `tests/Antiphon.Tests`) the project neither links nor declares. It was red at
`ae7bf7368` with exactly this missing link and green after it. Its limit: it only knows types that
live in a same-named file. The full answer is a Merge rule: build `Antiphon.sln` (or each mirror
project) once when a batch touches `tests/Antiphon.Tests/TestHelpers/**` or a mirror csproj.

## Exemption

same cause as an already written note: none.
