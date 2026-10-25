# Non-antiphon L1-L4 Debug result: d2095b9c

L1-L3 were repaired and verified on server2/Linux. The historical L4 failures did
not reproduce in file isolation; a different, intermittent DRAG timeout occurred
and is preserved below. Final L4 file runs passed. Native Windows reruns remain;
this is not a passing Windows qualification or a claim that L4's cause is fixed.

## Source and changes

Started at fetched `origin/master`:
`e5a867361937752b959c1451824ab9716fac83c0`. Assigned branch:
`feat/card-task-d2095b9c`. Source tip tested by the final client selections:
`ce70c00e39cf229f16cd345cf3c399d819535734`.

Each repair was committed and pushed separately:

- `7f66529f9b297babb54c029f07fa4b6db2455eef`: L1 adds
  `src/Antiphon.Observability` to `scripts/check-daemon-build.ps1`'s runner path
  list. This is the sole operational/product behavior change: source changes in
  the runner's actual Observability dependency now participate in staleness
  detection. The direct csproj reference is legitimate; deleting it or weakening
  the closure assertion would be the wrong repair. The existing closure test was
  red before the change and green after it.
- `c5db4293e2d0889f19b03b42bd7b3c7ced76933c`: L2's
  `Install-C716Scripts` creates `scripts/lib` and copies the real
  `live-startup-timing.ps1` helper. Production restart behavior and all existing
  graceful-stop assertions remain intact.
- `ce70c00e39cf229f16cd345cf3c399d819535734`: L3 adds `ProviderInputWait`
  to both independent expected test inventories and updates the expected
  liveness size/name from ten to eleven. No client runtime code changed.

L3's intended owner is CARD-1518 D-6 in
`docs/superpowers/plans/2026-10-23-card-1518-permission-wait-classification-plan.md`:
the client union, visual and rail mapping must include the new kind. The existing
`ProviderInputWait.test.tsx` explicitly requires the visual and liveness mapping.
Both runtime lists therefore remain unchanged. The every-kind loops now exercise
the new kind, while exact inventory equality, uniqueness/size and exclusions are
retained.

The historical extraction, read from fetched `origin/feat/card-task-b5042266`,
places the alias payload mismatch in **CardEditModal**, and the other timeout in
**SessionTerminal.mouse**. Those locations supersede the brief's reversed summary.

## Verification and retained failures

All build/test drivers used `scripts/build-slot.ps1` on Linux; every lease was
granted with zero reported queue wait and `maxcpucount=6`. No desktop/native
tests, native lock, qualification controls, release scripts, tags or live restart
commands were touched.

| Selection | Source | Before / initial result | Verified result |
| --- | --- | --- | --- |
| RunnerCapabilitiesTests, exact class | base -> L1 commit | 6 passed / 1 failed: missing `src/Antiphon.Observability` | 7 passed / 0 failed / 0 skipped |
| test-apphost-graceful-stop.ps1, all eleven top-level cases | base -> L2 commit | 2 passed / 9 failed: helper missing before scenario effects | 11 passed / 0 failed; existing Windows PowerShell parse subcheck unavailable |
| attentionVisuals.test.ts, exact file | base -> source tip | 30 passed / 1 failed: expected-kind inventory omits ProviderInputWait | 31 passed / 0 failed |
| homeTasksModel.test.ts, exact file | L1 commit -> source tip | 36 passed / 1 failed: expected liveness inventory omits ProviderInputWait | 37 passed / 0 failed |
| ProviderInputWait.test.tsx, exact file | source tip | Additional owner-contract witness | 1 passed / 0 failed |
| CardEditModal.test.tsx, exact file isolation | source tip | Historical timeout and alias mismatch did not reproduce | 14 passed / 0 failed |
| SessionTerminal.mouse.test.tsx, first file isolation | source tip | 13 passed / 1 failed: `protocol 'DRAG' stays local` timed out at 20 seconds; recorded duration 26.405 s. Historical reset case passed. | Failure retained; no source change |
| SessionTerminal.mouse.test.tsx, `-t DRAG` isolation | source tip | Diagnose the new timeout | 1 passed / 13 filtered skips; body 3.41 s |
| SessionTerminal.mouse.test.tsx, `-t DRAG` at actual base | base, detached temporary worktree | Check attribution without changing the task branch | 1 passed / 13 filtered skips; body 3.55 s |
| SessionTerminal.mouse.test.tsx, final whole-file diagnostic | source tip, unchanged source | Check the unresolved file-level failure with individual timings | 14 passed / 0 failed; reset case 15.699 s, DRAG 7.649 s |

All client wrapper selections passed their typecheck and lint gates before
Vitest. No timeout was raised, no assertion was deleted and no retry was added to
tests. The final mouse file rerun was diagnostic after its new timeout, not a
replacement for the failed evidence. Base DRAG passed: an inherited regression is
not established, and the unrelated L1-L3 changes do not establish an introduced
mouse defect. Intermittence is observed; its cause is unresolved. No speculative
L4 fixture/product repair was made.

Exactly one .NET build was needed, at the base commit:

```sh
pwsh -NoProfile -File scripts/build-slot.ps1 -Label d2095b9c-runner-build -SlotWaitMinutes 8 -- dotnet build tests/Antiphon.SessionRunner.Tests --property:OutputPath=bin-d2095b9c/ --property:UseAppHost=false --nologo
```

It completed in 46.56 s (48 s lease hold), with 0 errors / 55 existing warnings.
The test assembly was reused with `--no-build`: no C# or project files changed,
and the closure test reads the current script from the checkout. This avoids the
checkpoint inventory reader's reported 64 MiB assembly limit. Both class runs
used the gate and:

```sh
dotnet run --project tests/Antiphon.SessionRunner.Tests --no-build --property:OutputPath=bin-d2095b9c/ --property:UseAppHost=false -- --treenode-filter '/*/Antiphon.SessionRunner.Tests/RunnerCapabilitiesTests/*'
```

## Timing and evidence limits

Each client file was a separate wrapper invocation, executed sequentially. The
existing dedicated CARD-1518 test was the only additional client selection, to
verify the owner decision. No Unit lane or full suite was run.

| Selection | Test/file wall | Test bodies | Gate lease hold |
| --- | --- | --- | --- |
| Runner class red / green | 1.132 s / 0.781 s | Individual durations not separately retained in text | 3 s / 3 s |
| Graceful-stop red / green | Case wall unknown | Per-case timestamps unavailable | 32 s / 33 s |
| Attention visuals red / green | 2.55 s / 2.40 s | 0.108 s / 0.082 s | 122 s cold install+gates / 18 s |
| Home model red / green | 2.18 s / 2.38 s | 0.041 s / 0.068 s | 17 s / 17 s |
| ProviderInputWait | 5.37 s | 1.21 s | 18 s |
| CardEditModal isolation | 73.27 s | 69.88 s | 87 s |
| Mouse first file / final file | 93.20 s / 62.63 s | 86.68 s / 58.60 s | 123 s / 95 s |
| DRAG isolation tip / base | 7.43 s / 7.64 s | 3.41 s / 3.55 s | 19 s / 82 s |

These are individual Linux samples under uncontrolled shared-host load, not p95
estimates. Vitest logs retain transform, setup, import, environment and body
aggregates; final mouse JSON retains per-case results/durations. Other individual
passing-case timings, CLI/filesystem/HTTP spans and unattributed gate overhead
are unknown. No database/container setup or real application host was required.
Native timing/acceptance remains unmeasured. Existing 20 s client test deadlines
remain unchanged. Command-level hard bounds were 150-1000 seconds depending on
selection, including finite slot waits of 1-8 minutes; none expired.

Task-local generated evidence (gitignored):
`/work/worktrees/task-d2095b9c/.antiphon/debug-d2095b9c/` contains build.log,
l1/l2 red and green logs, both L3 red logs, the five file logs, tip/base DRAG
isolation logs, final mouse log/JSON and the saved L1 green HTML report.
The JSON's duplicate stdout reporters do not represent duplicate test executions.
This committed document preserves essential results if mirror-local generated
evidence is retired. The base worktree and all ten owned `bin-d2095b9c` output
directories were removed after their runs completed.

## Native rerun rows and handoff

Run these on the Windows qualification owner when its current run permits;
this task started none of them:

| Row | Exact native selection | Outstanding acceptance |
| --- | --- | --- |
| N1 | `pwsh -NoProfile -File scripts/test-apphost-graceful-stop.ps1` through its native build-slot gate | All eleven top-level cases, including T7's actual Windows PowerShell 5.1 parse branch. Linux T7 reports that branch skipped because powershell.exe is absent. |
| N2 | `pwsh -NoProfile -File scripts/test-client.ps1 src/features/board/CardEditModal.test.tsx` through its native build-slot gate | Historical `sends only the fields that changed, plus the token and editedBy` timeout and `sends a changed short alias and an emptied one as a clear` payload mismatch. Linux file isolation passed both. |
| N3 | `pwsh -NoProfile -File scripts/test-client.ps1 src/features/board/SessionTerminal.mouse.test.tsx` through its native build-slot gate | Historical `reset and instances remain independent` timeout and retained intermittent Linux DRAG timeout. Exact diagnostic filters are `-t 'reset and instances remain independent'` and `-t DRAG`; native qualification must retain the full file's result. |

Fetched master at completion remained
`e5a867361937752b959c1451824ab9716fac83c0`. The source tip's
`git merge-tree --write-tree origin/master HEAD` was clean, tree
`2081426b4be8569ef89a43bb0bb35d6327136445`; the final report commit gets a fresh
merge-tree/evidence-diff check before handoff. The first evidence-diff check found
3 commits / 0 generated entries / 0 violations. No production/test repairs beyond
the three small slices above are proposed. Land the pushed repairs and preserve
the native rows; do not treat this report as a completed rc qualification.

--- next stage ---
next: land
handoff: Land the three verified repairs; retain N1-N3 and the intermittent L4 timeout as native qualification obligations.
artifact: docs/investigations/2026-10-25-non-antiphon-l1-l4-fixes-d2095b9c.md
