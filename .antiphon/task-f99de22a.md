# Review f99de22a: rc land-graph approval regeneration 16477bad (Interim scope, per brief)

Outcome: SOUND. Commit 16477bad028ad0443ff58babd7d09004a1898524 changes only the two approval
record lines. Its digest 20DE49F6/66 is reproduced independently and approved by a real
`-Check` (with evaluation) at the tip. The parent fails `-Check` only on the manifest mismatch.
No defects found.

## (1) Diff: only approval records changed
`git show --stat 16477bad0`: 2 files, 2 insertions, 2 deletions.
- `server/Application/Services/LandVerificationBuildGraph.evaluation.txt:1` changed from `841DD41B42D1...D052` to `20DE49F6B177...DECB6`. Line 2 `10.0.403` and line 3 `749d65387b4e9ae7f84fba8c7b6f42658b430dc7` are unchanged.
- `server/Application/Services/LandVerificationBuildGraph.manifest.txt:1` gets the same digest change. Line 2 `66` is unchanged.

Nothing else changed: no tool, script, library, test, pins blob or docs file. The pins blob at HEAD is
`749d65387b4e9ae7f84fba8c7b6f42658b430dc7`, the same as master. These are exactly the two files
that `-Update` publishes (`scripts/audit-land-verification-graph.ps1:224-225`).
`check-evidence-diff.ps1 -BaseRef 2b13c32ad -HeadRef 16477bad0` printed commits=1 violations=0.

## (2) Independent recompute
- **Pure re-implementation.** I wrote a Node script that applies the C# `LandVerificationBuildGraph.Includes` inventory to `git ls-tree -r -z`, sorts ordinally, NUL-joins and hashes with SHA-256. It gives tip `20DE49F6...DECB6 count=66` and parent `20DE49F6... count=66`, because the parent tree has the same definition files.
- **Real `-Check` at the tip.** Run in a scratch clone at 16477bad0 through build-slot (lease 49bbaffe). The evaluator was built Release in a third scratch clone through build-slot (lease 79fa65e6). Output: `digest=20DE49F6...DECB6 count=66`, then `phase=pass2`, then `approved`, exit 0.
  - The sha256 of all 9,155 tracked files was identical before and after.
  - `git status --porcelain --ignored` was identical before and after.
  - The canary directory `antiphon-landgraph-canary-9eaef6bd...` was gone afterwards.
- **Real `-Check` at the parent 2b13c32ad.** Scratch clone, build-slot lease 62dd6f3d. Output: `digest=20DE49F6... count=66`, then `manifest does not match the selected graph`, then `audit refused: land graph pre-check refused`, exit 1. Tracked files and status were identical before and after, and no canary was left.
  - The only refusal is the digest mismatch. The tip proves the same definition tree evaluates clean. The check is therefore not vacuous, and the regeneration was needed.
- **Why the digest moved.** The series original is `f038db25f`, whose tree really does hash to 841DD41B/66. Compared with it, the rc tree lacks one line in each of two files:
  - `tests/Antiphon.Tests/Antiphon.Tests.csproj`: `<Compile Include="..\Shared\DispatchWaitDiagnostic.cs" .../>`
  - `tests/Antiphon.E2E/Antiphon.E2E.csproj`: the same Compile line.

  So the same 66 paths have different blob ids, which matches the commit message.

## (3) The digest is a function of the tree; -Update evaluates first
- **Digest.** It is SHA-256 over ordinal-sorted `ls-tree -r -z HEAD` rows (mode, type, oid, path) of definition paths (`scripts/audit-land-verification-precheck.ps1:1647-1713`). Land recomputes the same function over the candidate tree (`LandVerificationBuildGraph.FingerprintOf`, `server/Application/Services/LandVerificationBuildGraph.cs:51-57`) and compares it with the embedded manifest. A digest that does not match the tree only forces a build.
- **-Update order.** The pre-check writes a temporary manifest from HEAD. Then `Assert-EvaluatedGraph` runs: both evaluation passes plus the canary, and any non-zero exit throws. Then `Assert-HeadSource` runs. Only after all of that does it publish (`scripts/audit-land-verification-graph.ps1:197-225`). So `-Update` cannot record a digest for a tree it did not evaluate.
- **Hand-set bytes.** The record files are plain text, so the land gate itself cannot tell evaluated bytes from hand-typed ones. That is enforced at author time by `-Check` and Review; see A-2. For this commit, the real `-Check` re-evaluates the exact tree and approves it.

## (4) Legitimate in design
`docs/orchestration-loop.md` (land non-code skip section) says:
- "Any other definition-file edit still changes the digest, so the skip stops until the manifest is regenerated."
- The script writes the manifest, and it is "never a hand-edited constant".
- "Updating the record still requires successful real evaluation and records the SDK used."

`-Update` is the only documented way to regenerate the record. A cherry-pick onto a tree with different definition blobs is exactly that case. Without the regeneration, the rc tip also fails `LandNonCodeSkipTests.C1385_RegistryMatchesNarrowGraph`, which binds the embedded manifest to the HEAD tree.

The regeneration does not satisfy the orchestrator's instruction to stop and report rather than run -Update. That process deviation is for the caller to judge. This independent recompute is the verification it called for.

## (5) Master port
Master `e65343132` currently hashes to `124B52D4...C055` with count 65 and records SDK 10.0.402. Do not carry 16477bad0 to master: its digest describes the rc tree only.
- If the series lands on master unchanged (`f038db25f`'s tree), 841DD41B is correct there.
- Otherwise the master landing tip needs its own `-Update`, followed by an independent `-Check` and a green C1385 on that exact tip.
- A stale record is fail-safe at land (it forces a build), but it turns C1385 red.

## Tests (tip 16477bad0)
I built Antiphon.Tests once in a scratch clone through build-slot (lease 328faa3f, Debug, `UseAppHost=false`, OutputPath `bin-rvf99/`, 0 errors). Each run below used `dotnet exec`, each in its own slot.

| Row | Filter | Result |
|---|---|---|
| LandGraphEvaluationTests + LandGraphAuditCleanupTests | `/*/*/(LandGraphEvaluationTests*)\|(LandGraphAuditCleanupTests*)/*` | 135/135 passed, 9m26s |
| RcRosterRepairTests (3), WindowsUnitPlatformAdmissionTests, C1514LandingSplitBudgetTests (census C1404, C1577, C1584, C1410, G2865) | `/*/*/(RcRosterRepairTests*)\|(WindowsUnitPlatformAdmissionTests*)\|(C1514LandingSplitBudgetTests*)/*` | 13 total: 12 passed, 1 skipped, 0 failed, 1m38s |
| LandNonCodeSkipTests (own slot) | `/*/*/LandNonCodeSkipTests/*` | 111/111 passed (C1385 passed), 11m43s |

Details of the second row:
- RcRosterRepairTests: Costs, Explicit and Mirror all passed.
- All five census tests passed.
- Four C1210 tests passed. `C1210_Windows_linux_only_outcomes_are_exact` was skipped by design: "CARD-1210: the child skip-roster audit requires Windows placement."

One earlier combined attempt was cut off by my own 590 s foreground timeout while its tests were still waiting for the limiter. I reran it as the split rows above. No orphaned processes were left.

## Advisories
- **A-1.** The Merge report `.antiphon/task-3ddc57d3.md` does not exist on `origin/feat/card-task-3ddc57d3`, whose tip is 16477bad0. I used the commit message and verified everything independently.
- **A-2 (pre-existing design).** Land compares only the embedded digest, so the "evaluated, not hand-set" property rests on author-time `-Check` and Review. No test runs a real-evaluator `-Check` against the repository's own HEAD; C1385 binds only digest to tree. Consider a nightly real `-Check` row.
- **A-3.** Other rc-port picks of the same commit carry the stale 841DD41B on trees that hash to 20DE49F6: `183fefb9a` (`feat/card-task-d8bbbac2`), `5e1692306` and `7cacecf55`. Each would turn C1385 red there and needs the same regeneration if it ever lands.
- **A-4 (pre-existing, unverified).** `Assert-HeadSource` uses `git diff --quiet HEAD`, which ignores untracked files (`scripts/audit-land-verification-graph.ps1:136-138`). An untracked definition file on disk during `-Update` could take part in evaluation without being hashed. My `-Check` ran in clean clones, so this verdict is unaffected.

## Verdict fields
- **reviewedSourceClean: false.** This is not a source finding. The brief prescribed build-slot plus `dotnet exec`, so there are no checkpoint-tool SHA-validated receipts with verified build provenance.
- **Not removed.** The scratch clones `tip`, `parent` and `toolsrc` (with `bin-land-audit` and `bin-rvf99`) remain under the session scratchpad. The cleanup rule forbids `rm -rf`, so I did not delete them.
