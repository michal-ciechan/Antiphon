Fixed two fixture defects; Linux verification passes (land graph 92/92, land verifier 8/8). The old rc's real-repository refusal also reproduces on Linux and is already repaired on master. Native Windows confirmation and reconciliation of the original ten graph failures remain pending.

Base: `e970c2c9ff2f723dd1cccf130e61901eeca5f200`. Assigned branch: `feat/card-task-ed7af4f3`.

| Cluster | Classification and evidence | Disposition |
|---|---|---|
| `C448_V32_VerifierOutcomeNeverMakesPrivateOutputDisposable(pass)` | Fixture defect, reproduced at the base on Linux. The directory/file were created successfully; successful guarded cleanup later removed the worktree because `bin-land/keep.txt` matches the disposable-output policy. This is unrelated to Windows separators or load. | Use `bin-land/keep.local.json`, which the existing user-local protection rule preserves inside build output. All assertions remain, including exact bytes, cleanup refusal, publication, source-ref retention and absence of remove/delete commands. All four verifier outcomes pass. |
| `C1483_S4_ExistsProbeOfDocsRefuses` path assertions and `C1528_PureMetadataAndFunctionsApprove` documentation-owner assertions | Windows-path normalization defect in the fixture. Product diagnostics contain native absolute paths, while the fixture compares forward-slash relative paths. Five forced drive/UNC cases fail on Linux before normalization; the Unix control passes. | Add `ToolRun.PathText` for separator-normalized comparisons. Preserve raw `Text`, raw `Lines`, verdicts, case sensitivity and all prior assertions. Apply the same correction to the imported-directory wrapper witness. Six pure-string witnesses pass afterward. No drive-letter or UNC path is opened by these witnesses. |
| `C1483_S4_RealRepositoryApproves` against rc `a97194ee7a4adf215454fd82c453ba46362596ee` | Inherited product/build-definition incompatibility, rather than an established Windows or load defect. A temporary detached clone of that exact rc, audited with the unchanged evaluator under a build slot, exits 1 with the diagnostic below. The evaluator source is identical between that candidate and this task's base. | Existing master repair `e48f12db25cb8a958b7e82db53c6de0e3b53b3a8` removes the environment-derived DefineConstants expression and uses fixed forwarding types. It is already an ancestor of the required base. Real-repository approval passes at both base and final source. This task changes no product code, pins, manifests or approval records. |
| Other original Windows graph failures | The brief names examples, not the complete ten-row failure list. Linux base passes all 86 graph cases. | Their individual attribution remains unverified; reconcile the full native failure list and fresh native receipts. No load-induced diagnosis is established. |

Exact old-rc diagnostic (read-only audit; no record regeneration):

```text
refused: src/Antiphon.HostCleanup/Antiphon.HostCleanup.csproj /Project[1]/PropertyGroup[2]/DefineConstants[1] environment-property
```

Commits, each pushed immediately:

- `20b406cf4351d0846bc395d8a60d20db154c7a3e`: protected private-output fixture.
- `99c8aa9b771f074de5ed08e5d0b86ff07a83d279`: failing Windows/UNC diagnostic witnesses, with the assertion consumers wired to an initially unchanged diagnostic view.
- `3f7e069faf29841448e3bfa9e40da89153f523d9`: separator normalization; final executable source.

Changed files are the five test files below. No sibling-assigned classes were changed:

- `tests/Antiphon.Tests/Application/AgentTaskLandVerifierTests.cs`
- `tests/Antiphon.Tests/Application/LandGraphEvaluationTests.cs`
- `tests/Antiphon.Tests/Application/LandGraphEvaluationProbeTests.cs`
- `tests/Antiphon.Tests/Application/LandGraphEvaluationFilesystemTests.cs`
- `tests/Antiphon.Tests/Application/LandGraphEvaluationScriptTests.cs`

| Run / source | Selection | Passed | Failed | Skipped | Duration |
|---|---|---:|---:|---:|---|
| Base `e970c2c9ff2f723dd1cccf130e61901eeca5f200` | Graph class | 86 | 0 | 0 | Combined run below |
| Same base | Verifier class | 7 | 1 | 0 | Combined classes 6m50.954s; slot held 415s |
| `99c8aa9b771f074de5ed08e5d0b86ff07a83d279` | Exact new path-witness method, six arguments | 1 | 5 expected assertion failures | 0 | 0.827s; slot held 4s |
| Same source | Verifier class after its fixture fix | 8 | 0 | 0 | 1m26.339s; slot held 92s |
| `3f7e069faf29841448e3bfa9e40da89153f523d9` | Entire graph class, including six path witnesses | 92 | 0 | 0 | 5m30.657s; slot held 334s |

All three test-project builds succeeded with zero errors and the same 1,197 warnings as the base. Build durations: base 3m02.85s, witness/verifier fixture 3m23.00s, final graph 3m03.58s. Each build used `OutputPath=bin-ed7af4f3/` and a granted build slot with maxcpucount 6. Every test driver and the extra old-rc audit also received a slot; all grants reported zero wait. The two rebuilds compile committed fixture changes and the deliberate red-then-green witness; the extra audit resolves the candidate-versus-master discrepancy. No Unit lane, full assembly, native Windows or desktop process-spawning run was performed.

The exact baseline combined filter was:

```text
/*/Antiphon.Tests.Application/(LandGraphEvaluationTests*)|(AgentTaskLandVerifierTests*)/*
```

Subsequent filters were respectively:

```text
/*/*/LandGraphEvaluationTests/C1483_PathDiagnosticsUsePortableSeparators
/*/*/AgentTaskLandVerifierTests/*
/*/*/LandGraphEvaluationTests/*
```

Each run used `dotnet run --project tests/Antiphon.Tests --no-build --property:OutputPath=bin-ed7af4f3/ -- --treenode-filter <filter> --report-trx ...`, through `pwsh -NoProfile -File scripts/build-slot.ps1`. Builds used that gate too. Finite outer timeouts were 900s for the initial build/run, 600s for each rebuild, 240s for the pure-string red run, 360s for the verifier run, 720s for the final graph run, and 120s for the old-rc audit. Slot waits were capped separately at five minutes initially/builds, two minutes for targeted tests, and one minute for the audit. These are provisional bounds from this small sample, not measured p95 claims. Existing product/test timeouts are unchanged.

Generated evidence remains ignored under `/work/worktrees/task-ed7af4f3/.antiphon/debug-ed7af4f3/`:

- `build-base.log`, `base.log`, `base/base.trx`
- `build-witness.log`, `red.log`, `red/red.trx`
- `verifier.log`, `verifier/verifier.trx`
- `build-graph.log`, `graph.log`, `graph/graph.trx`
- `old-rc-audit.log`
- `red-phases/`, `verifier-phases/`, `graph-phases/`: per-case phase JSONL, run/group IDs and assembly MVIDs.

TRX retains individual durations and class membership. Baseline phase recording did not activate because its attempt metadata was omitted; those unavailable phases remain unknown. Later runs supplied all phase metadata. Verifier timing records one shared DB setup (db-create about 9.4s, db-migrate about 36.5s). Uninstrumented CLI/IO/body portions remain remainder/unknown; empty recorded phase lists are not zero-cost claims. There was concurrent Linux host work during some runs. No selection exceeded 30 minutes.

All 33 producer-owned `bin-ed7af4f3` directories and the temporary rc clone were removed after all runs finished. Generated logs, TRX and phase files are retained. Source was frozen during every build/test run. No qualification process, native lock, host service or running desktop stack was touched.

`git diff --check` and `scripts/check-evidence-diff.ps1` passed over the source commits (zero evidence violations). Fetched master was `c2e35da7c1ae24792b1f115c7f47996f7463f625`; `git merge-tree --write-tree origin/master 3f7e069faf29841448e3bfa9e40da89153f523d9` completed without conflicts. The final report-only tip receives the same checks before settlement. Nothing was rebased, merged, tagged or force-pushed.

Native rows to rerun, under the qualification owner's scheduling on the updated rc:

1. `LandGraphEvaluationTests`: all 92 expanded cases, especially the five original Exists variants, metadata controls, imported-directory wrapper, real-repository approval/check and the six forced path witnesses. The Unix-mode unreadable-link case intentionally skips on Windows; reconcile that platform skip explicitly.
2. `AgentTaskLandVerifierTests`: all eight cases, including pass/fail/cancel/throw and all three real TUnit selection outcomes.

Use one fresh isolated build and the combined filter above through the host build-slot gate; collect fresh native TRX per class. Do not credit Linux results as native qualification or rerun the stale candidate without its existing master graph repair. The fixture delta requires no new Review under the operator's fixture-only instruction. Current Windows verification and complete attribution of the original ten graph failures are still absent.

--- next stage ---
next: decide
handoff: Fixture fixes are pushed and pass on Linux. Reconcile the full original Windows failure list and schedule the two native class rows after the existing graph repair reaches the rc; decide fixture landing independently of native qualification credit.
artifact: /work/worktrees/task-ed7af4f3/.antiphon/task-ed7af4f3.md
