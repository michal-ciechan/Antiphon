# C1385 land graph differential: Antiphon.E2E None changed (rc 7f8e9fd1)

Debug task aeecbb03, 2026-11-03, server2 Linux, base master `7dc9e1168a2b4f714a4e3e29ac99e2aad485524c`.

## Failure

Windows rc qualification run 7f8e9fd1 (rc/2026-11-01 tip `10a826b9d`, chunk antiphon-all-split-15,
checkout `C:\Antiphon\releases\checkout-8a7dbe6d`): `LandNonCodeSkipTests.C1385_RegistryMatchesNarrowGraph`
failed at `LandNonCodeSkipTests.cs:397` because `scripts/audit-land-verification-graph.ps1 -Check` exited 1:

```
digest=D92450D9...36BE count=69
phase=pass2
refused: differential {repo}\tests\Antiphon.E2E\Antiphon.E2E.csproj item None changed
```

The digest and count matched; only the pass-2 differential refused.

## What the differential is

`tools/Antiphon.LandGraphAudit/SolutionAudit.cs` evaluates every solution project twice:

1. pass 1 evaluates the real repository and records each project's evaluated properties, item
   `(ItemType, FullPath)` rows and imports (`EvaluationSnapshot`, normalised only for the repo root);
2. the copy phase builds a canary repository beside the checkout (`CanaryRepository.Create`): it copies
   `git ls-files --cached --others --exclude-standard` (tracked and untracked-unignored files) and
   rewrites every `docs/**/*.md` as canary text;
3. pass 2 evaluates the canary and `EvaluationSnapshot.Difference` refuses on any row that differs.

The comparison is meant to prove that changing documentation changes nothing in the build graph.

## Cause (deterministic given the state; the state is run-dependent)

A **git-ignored** file inside a project directory is matched by that project's SDK default globs
(`None Include="**/*"`) in pass 1, but the canary never copies ignored files, so the same glob finds
nothing in pass 2. The differential then reports `item None changed` for that project. It is not a
documentation dependency.

`tests/Antiphon.E2E/TestOutput/` is gitignored (`.gitignore` line 35) and is written by every E2E run
(`TestDiagnostics` logs, `IsolatedSessionRunner` runner root, screenshots). The SDK default excludes
cover `bin/`, `obj/` and dot-folders, not `TestOutput/`. Any other ignored file under a project
directory (for example a `bin-<name>/` alternate output) has the same effect.

Live evidence (read-only `GET /api/filesystem/browse`, directory existence only, 2026-11-03):
`C:\Antiphon\releases\checkout-8a7dbe6d\tests\Antiphon.E2E\TestOutput`, `...\TestOutput\Logs` and
`...\TestOutput\runner` exist; `...\TestOutput\Screenshots` does not (negative control). Of eleven
release checkouts, eight have `TestOutput` and three do not. Whether C1385 fails therefore depends on
whether an E2E chunk wrote into that checkout before antiphon-all-split-15 ran. The earlier runs
a074ff9a and aa64a906 did not list it, and no earlier rc failure list in `docs/investigations/`
names `C1385_RegistryMatchesNarrowGraph` (only `C1385_UnrelatedScriptChangeStillSkips`, 2026-10-23);
I could not map those runs to their checkouts from server2.

## Linux reproduction at master

One Antiphon.Tests build (`dotnet build tests/Antiphon.Tests -p:UseAppHost=false`, staged tool
`tests/Antiphon.Tests/bin/Debug/net9.0/land-graph-audit/Antiphon.LandGraphAudit.dll`):

| Run | Result |
|---|---|
| `C1385_RegistryMatchesNarrowGraph`, clean tree | passed (36 s) |
| `-Check -ToolPath <staged tool>`, clean tree | `digest=7456743D28E951E9A6FBF53874B166750CD2631588969556A891FCC2CA673C22 count=69`, approved, exit 0 |
| `-Check`, after `tests/Antiphon.E2E/TestOutput/Logs/run.log` was created | `refused: differential {repo}/tests/Antiphon.E2E/Antiphon.E2E.csproj item None changed`, exit 1 (the rc message) |
| new witness `LandGraphEvaluationTests.C1483_IgnoredFileInDefaultGlobApproves` | failed, `refused: differential ...` (red, commit `9b9796e6c`) |

## Fix (commit `269ce400e`)

`CanaryRepository.Create(repo, evaluatedItems)` takes the FullPaths pass 1 evaluated as items
(`EvaluationSnapshot.ItemPaths`). For each one that is an existing, non-link file inside the repository
and not in the copy set (so git-ignored), it writes an empty placeholder at the same relative path in the
canary, after the ordinary copy and through the same destination guard. The same globs then match the
same names in both passes. The comparison itself is unchanged (no rows are discarded); item contents are
not an evaluation input. An ignored file that only feeds an `Exists()` condition, or an ignored import,
still refuses (fail closed).

Files: `tools/Antiphon.LandGraphAudit/CanaryRepository.cs`, `EvaluationSnapshot.cs`, `SolutionAudit.cs`,
witness in `tests/Antiphon.Tests/Application/LandGraphEvaluationTests.cs`. The rc tree
`origin/rc/2026-11-01` has byte-identical copies of all four files; both commits pick clean onto rc
(`git merge-tree --merge-base <c>^ origin/rc/2026-11-01 <c>`) and the branch merges clean into master.

## Verification after the fix (Linux)

With `tests/Antiphon.E2E/TestOutput/Logs/run.log` still present (the rc state):

- `C1385_RegistryMatchesNarrowGraph` + `C1483_IgnoredFileInDefaultGlobApproves`: 2/2 passed;
- whole `LandGraphEvaluationTests` class (`/*/*/LandGraphEvaluationTests/*`): 138/138 passed, 0 skipped;
- `-Check -ToolPath <staged tool>`: `digest=7456743D28E951E9A6FBF53874B166750CD2631588969556A891FCC2CA673C22 count=69`, `approved`, exit 0.

## What a Windows rerun must show

On the rc checkout of the next qualification with the fix picked, with an E2E chunk having run first
(`tests\Antiphon.E2E\TestOutput` present): `C1385_RegistryMatchesNarrowGraph` passes and
`-Check` prints `approved` at the rc digest. Without the fix, deleting `tests\Antiphon.E2E\TestOutput`
before antiphon-all-split-15 is the workaround.
