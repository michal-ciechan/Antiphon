# Why Merge missed the checkpoint project contract failure

The Windows Full run `3528e6b1` on `rc/2026-11-30` reported one failure in
`antiphon-all-group-123.trx`. The Unit method `csproj_references_only_host_cleanup`
expected the checkpoint tool's only project reference to be HostCleanup. The
project XML also listed GitInProcess.

## Introducing commit and isolation contract

`869fe194e9cbe1c98df7cd420cb3e5686103741e` added the direct reference so
`SourceSnapshot` could use `WorkTreeIdentity` instead of spawning Git for each
source fingerprint. The project comment, friend assembly declaration and source
change all record the intended dependency. The introducing commit and the
available card records did not identify a card for this performance change.

The [CARD-0723 plan](../superpowers/plans/2026-09-25-card-0723-checkpoint-runner-plan.md)
started the standalone tool with zero project references. The
[CARD-0987 custody repair](../superpowers/plans/2026-10-15-card-0987-project-reference-contract-repair.md)
admitted exactly HostCleanup in `b7b2f112da759fe578b1a48ba16dfb747c55418c`.
`b2d76659060085f9df3a9720441a2bb82159a645` later made HostCleanup depend only on
GitInProcess for its tracked-path check. GitInProcess is a leaf in the project
graph. It declares only LibGit2Sharp 0.32.0, which resolves LibGit2Sharp.NativeBinaries
2.0.324. Its server and session-runner friend declarations grant those assemblies
access to its internals. The dependency graph has no path from the tool to either
product project.

## Approving reviews

History records source-identity review fixes in
`6192bf1dfb167c628c0c2eb920f35417ffe6af90` and
`b93f4b8b606c172f0315e3db1d0ca1946a5a7a24`. They cover Git fallback, fingerprint
parity, and native-library loading. The round-one diff adds source-state parity
coverage. The repository and scoped task search yielded no approving Review task
ID, Full or Interim scope, or executed filter for the introducing commit.

## Outside the selection

Running the historical selector with its table at the introducing commit and
range `869fe194e^..869fe194e` matched `checkpoint-tool` via
`tools/Antiphon.Checkpoints/**`. That row selected
`CheckpointReceiptSafetyTests`, `CheckpointTerminalReceiptTests` and
`HostCleanupCheckpointReceiptTests`. The `gitinprocess-tests` row and the fixed
short Merge subset also omitted `CheckpointToolProjectContractTests`. The dependency
built successfully in the Linux reproduction below. The Full run selected the
Unit XML assertion that rejected it.

Leased Linux builds and the class filter
`/*/Antiphon.Tests.Infrastructure/CheckpointToolProjectContractTests/*`
reproduced the same assertion at the rc pin
`bd34a7ff6da9db1a3fa3690f824890ef9b7cc2c0` and the sampled master pin
`4aaeea4760983178357cf99c35bc2f0118247b9c`. Each ran four tests with three
passing, one failing and zero skipped.

## Cheapest pre-land check

Add this row to `tests/impacted-classes.json`. The test reads all three projects,
so edits to either library's dependency declarations also select it.

```json
{
  "id": "checkpoint-tool-project-contract",
  "globs": [
    "tools/Antiphon.Checkpoints/*.csproj",
    "src/Antiphon.HostCleanup/Antiphon.HostCleanup.csproj",
    "src/Antiphon.GitInProcess/Antiphon.GitInProcess.csproj"
  ],
  "classes": [
    "CheckpointToolProjectContractTests"
  ]
}
```

With this row in a temporary copy of the historical table,
`scripts/impacted-classes.ps1 -MergeSubset` put the contract in
`CP-merge-subset-tests` for the introducing range. The repair updates the test
contract. The selector row is proposed here for a follow-up.

## Exemption

None.
