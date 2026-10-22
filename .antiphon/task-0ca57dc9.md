# CARD-1483 S3 Code report

S3 implements the D-5 target/OS pin gate. This report is committed before the last
checkpoint so the pushed tip can be the exact tested source. Final outcomes,
source SHA and unedited CHECKPOINT lines are supplied in the completion message
and the ignored `.antiphon/c1483-final.md` supplement; this pre-run report does
not claim the final checkpoint passed.

Original Code/landing owner: `0ca57dc9-7d5c-459f-8bc2-bf201f51e528`.
Branch: `feat/card-task-0ca57dc9`.
Worktree: `/work/worktrees/task-0ca57dc9`.
Assigned base: `c5da1d2387c08adf86e301a3874a5cbcfd4d6a98`.
Plan: `docs/superpowers/plans/2026-10-22-card-1483-real-msbuild-land-audit-plan.md`.
Scope/checkpoints: `docs/superpowers/plans/2026-10-22-card-1483-s3-amendment.md`.
Mutation: `docs/superpowers/mutations/2026-10-22-card-1483-s3-candidates.md`.

## Implementation

`TargetPins.cs` reads tracked definitions (also XML-shaped unusual definition
names), hashes normalized target/OS-element OuterXml and refuses missing, changed,
stale, malformed or duplicate pins. It preserves attribute values, leaf text,
CDATA, comments and significant whitespace; only layout between element children
is removed. OS selectors are absolute ordinal element paths. Pins must themselves
be tracked and cannot be supplied through a link. The existing S1/S2 evaluation
checks still run after the pin comparison. The scan covers non-built definitions.

The pins file contains the twelve targets read in this task. No non-Target
OS-conditioned element was found in the current tracked project definitions.

| Definition | Target(s) | Reviewed purpose |
|---|---|---|
| Directory.Build.props | SetSourceRevisionIdFromGit | Read git HEAD for version stamping |
| Directory.Build.targets | AntiphonResetBloatedFileWritesLedger | Read/delete oversized intermediate build ledger |
| tests/Shared/CheckpointRepeat.targets | AntiphonGenerateCheckpointRepeat | Write generated repeat attributes under IntermediateOutputPath |
| tests/Antiphon.Agents.Pty.Tests/Antiphon.Agents.Pty.Tests.csproj | CopyFakeClaude | Stage custody child and fake provider build outputs |
| tests/Antiphon.E2E/Antiphon.E2E.csproj | CopyLandFakeGrok; CopyOwnedPtyHostAppHost | Stage fake Grok and Linux native pty host |
| tests/Antiphon.MarkdownPdf.Tests/Antiphon.MarkdownPdf.Tests.csproj | CopyFakeBrowser | Stage fake browser output |
| tests/Antiphon.SessionRunner.Tests/Antiphon.SessionRunner.Tests.csproj | CopyCustodyChild; CopyOwnedPtyHostAppHostForHerdr | Stage custody child and Linux native pty host |
| tests/Antiphon.Tests/Antiphon.Tests.csproj | CopyStagedHelpers; CopyOwnedPtyHostAppHost; CopyOwnedHerdrFakeAppHosts | Stage named helpers (including audit tool) and Linux native apphosts |

These are human-reviewed execution bodies, not nested task evaluation. Residual
R-3 still applies to dynamic task parameters. File-reading conditions outside
targets remain S4. Script wiring is S5; no production landing behavior is activated.
Restart: none.

`audit-land-verification-graph.ps1 -Update` ran successfully and regenerated the
unchanged manifest: digest `637CFA8577440D255076CBF5CBA40BECA0C3BC8F738705A5536A35182DAD9C3E`,
count 58. No definition file changed in S3, so no manifest diff is expected.

## Dependencies and red proof

The assigned base lacked the landed S2 chain. Only its six commits were
cherry-picked, each pushed, without rebasing or merging master:

| Landed S2 commit | Task prerequisite commit |
|---|---|
| df8c6695a | 54f22e930bb7cba0ed1f9e7b46647aa6950cf755 |
| 17dc4a989 | 386ad511bb035a2671b061a0b8aa09fe578e6284 |
| dd980c82e | 51f9e9663973ea0a10bce20e2bbf97e4234d56eb |
| 285aa79bf | fb7536413c1afe47889129b5ae7186e3380ea7f1 |
| aed1449a8 | e7b40b9d613fc457008b0a9834d18503b394bd5a |
| 8ef0baca8 | 3eb93a83ae6fa4503b8c3164257b1109be82bd82 |

Red tests were committed and pushed as
`7dd0ed06b8e4324a9fb2ef68f602fdef24f0d0ac` before implementation. Audit source
was byte-identical to current master/S2. Fresh TRX verified 20 results: all ten
inherited cases passed; six V-8 variants, missing-pin V-9 and all three missing
OS-pin V-10 variants failed because exit 0 `approved` was returned instead of
exit 1. No build or fixture failure is being counted as the red witness.

```text
CHECKPOINT CP-3 commit=7dd0ed06b8e4324a9fb2ef68f602fdef24f0d0ac build=ok filter=/*/*/LandGraphEvaluationTests/(C1483_S1_*)|(C1483_S2_*)|(C1483_S3_*) executed=20 passed=10 failed=10 skipped=0 trx=/work/worktrees/task-0ca57dc9/.antiphon/checkpoints/20261022-224816-4690/rows/CP-3/run.trx slot=granted waited=0s dirty=0 source=7dd0ed06b8e4324a9fb2ef68f602fdef24f0d0ac sourceState=clean buildSource=verified
```

Red run wall 498.070s, build 394.025s, test interval 41.618s, host 50.400s.
Both leases were granted, waited=0s. The checkpoint retained its red output and
reported cleanup `still-present`; receipt validation correctly rejected it with
`cleanup_refused`. It is diagnostic red proof, not an ordinary green certificate.
The dead executor was observed by checkpoint wait before owned output cleanup.
The final run retains outputs explicitly for the manual real-tree acceptance,
then those outputs are manually removed after executor death.

Unlisted build: checkpoint tool bootstrap through build-slot.ps1,
slot=granted waited=0s, lease b0c5e204-d626-453e-8458-4fe191fc04b2,
5.35s build, four inherited nullable warnings, zero errors. One mistaken DLL
path included net9.0; that invocation started no tool/build/test and was corrected.
The red CP-3 and bounded manual real-tree tool run are the documented unlisted
executions. No full assembly, Unit lane or repeat-proof execution was requested.

## Final obligations and handoff

CP-3: full affected LandGraphEvaluationTests class, 20 expected results, on the
committed tip, followed by fresh TRX census and validate-checkpoint-receipt.ps1.
V-1, V-2, V-3, V-4, V-5, V-6, V-7, V-8, V-9, V-10 and S1 repair
V-1a/V-1b/V-3a are required. There are no ordinary R-n rows. Unit is explicitly
deferred by the brief (CARD-1466 known failures), never claimed passed.
V-11/V-12/V-13 remain S4, V-14/V-15/V-16 remain S5, including the original
CARD-1498 evaluation witness. All PC-S3-1 through PC-S3-7 and their listed
variants, inherited S1 controls and PC-S2-1a/1b/1c/2a/2b/3a/3b/3c remain
pending SourceLanding Mutation.

Run check-evidence-diff.ps1 over assigned base..HEAD; fetch master and run
merge-tree without merging; remove task-owned bin outputs, confirm clean status,
push and confirm ls-remote. The completion supplement records actual results.

RC dependency order is S1 then S2 then S3. The requested bundle-C document/report
was not present on fetched branch feat/card-task-69f144fd at
`b3d62ad1eade7222e544b1039dce94f7e19a092a` during implementation. S3-only source
changes use the S2 API and can be ported after that chain, but the twelve pins
must match the RC target bodies; regeneration/review and RC qualification remain
required. No independent S3 RC adoption or verified bundle-C dry run is claimed.

After ordinary verification: Review. The caller lands this original Code task
and commissions SourceLanding Mutation separately.
