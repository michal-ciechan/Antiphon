# RC db2c2088 group 66: Windows freshness fixture

The Windows backlink fixture is repaired and its portable witness is red then green. Final changed-class verification passed **11 cases, with 3 native-only skips and 0 failures**. The unchanged removal class passed 9/9 at the base. Native confirmation of the original Windows failures remains pending; the deploy change adds diagnostics only.

## Scope and source

- Debug task `44298e2a`, assigned branch `feat/card-task-44298e2a`, Linux checkout `/work/worktrees/task-44298e2a`.
- Fetched starting `origin/master` and task base: `16d663409c27963e22d821af7a82b72822ad386a`.
- Final executable source: `88971916f1f50074f0d1054562aad51a72dc6a4b`. A later report-only commit does not change those sources.
- Windows failure counts came from the brief: qualification `db2c2088`, candidate `a13e544e`, group 66, freshness 6 / deploy readiness 1 / removal authority 1. The original desktop TRX and exact errors were unavailable here. No native test, live qualification lock, desktop build slot, release script or tag was touched.
- Product code is unchanged. Every existing assertion, timeout and retry remains intact. `WorktreeRemovalAuthorityTests` and the neighboring fixers' classes were not edited. No Unit lane or Review was run, as commissioned for fixture-only work.

## Repair and attribution

`LandingRegistrationFreshnessTests.BacklinkCorruption.RedirectAsync` constructed its admin `gitdir` backlink with `Path.Combine(observer, ".git")`. On Windows that produces backslashes. Git's backlink/registration spelling uses forward slashes; the analogous `LandHalfResetFixture` was already repaired in historical commit `cb1ceacbd441e566b9cb83b1cb5637c81b7304e4`. This task applies that existing approach to the standalone duplicate: ask the independent fixture Git reader for `rev-parse --absolute-git-dir`, trim its line ending and write that exact spelling with LF.

The same redirect is used by precisely six expanded cases: the three scopes of `C975_CachedBacklinkChangeRefusesIdentity`, two scopes of `C975_SecondIdentityReadSeesBacklinkChange`, and `C975_RecoveryProofSeesBacklinkChange`. The seventh existing case injects a registration read failure without redirecting. This matches the reported six Windows failures, but attribution to those original errors remains an inference until their native rerun.

The new pure-string witness, `C975_WindowsBacklinkUsesGitDirectorySpelling`, supplies `C:\fixture with spaces\observer` and a fake Git answer `C:/fixture with spaces/observer/.git\r\n`. It exercises the same helper as the real redirect and requires the exact forward-slash/LF backlink, one query, the original observer argument, and the absolute-Git-directory command. At `04d6cd9d2bdb2cbbd34ab19cea020ef18354352e`, the unchanged construction failed its intended path assertion on Linux. At final source it passes along with every original case. Freshness diagnostics now retain the written backlink, before/after parent stamp and entry count, and actual registration listing before the existing assertions.

## Deploy diagnostics and unresolved native work

There was no Linux reproduction of the deploy or removal failure, so no product mechanism was guessed.

The readiness harness previously saved its original caught error in `result.json`, then threw only `Verification shell did not execute.` The C# parent asserts child exit before reading that JSON, and fixture disposal removes the directory. The changed script emits a projection containing case, shell exit, later-call count, original error, type, source position and stack before the generic verdict. It also records the resolved shell application and preserves up to 4096 characters of unexpected launch stderr. Shell/HTTP output and process environment are excluded from the result projection. The C# fixture records PID, owned directory and selected curl/sh/sleep/cygpath paths.

An owned stub defining only `Invoke-AmServiceDeployment` to throw a synthetic Windows-path launch error tested that diagnostic path through the real harness. Before and after both correctly exited 1; before, stdout/stderr exposed only the generic verdict while JSON contained the original error; after, the original error/type/position/stack were present in stdout before the same verdict. These are diagnostic probes, **not** passing deployment tests or reproduction of the original Windows failure.

For removal, existing `ANTIPHON_C448_EVIDENCE` instrumentation already captures `LandingSafetyHarness`'s `committed_after_service` operation/event state and the fixture's filesystem/ref/registration images. Enable it on the native rerun to recover the actual cleanup refusal reason without editing the neighbor-owned class. Read-only Git objects, live handles, newline policy and registration/path issues remain possibilities rather than established causes of that original row.

## Build and execution evidence

Every build, test and synthetic harness invocation used `scripts/build-slot.ps1`, received a server2 slot, and completed before source edits or settlement. Every TUnit run used the compiled output with `--no-build`. The final DLL is 68,691,456 bytes, above the checkpoint reader's 64 MiB cap; the brief explicitly commissioned the direct gated route.

There was **one full isolated dependency build and two test-only recompiles into the same output**. The test-only recompiles used `--no-restore --property:BuildProjectReferences=false` to compile the committed red witness and the final repair. This exceeds a literal one-build-invocation reading of the brief; it avoids rebuilding dependencies while providing real red/green execution. Dependency/product sources are identical across these commits.

| Build source | Scope | Errors | Warnings | Build elapsed / slot held |
|---|---|---:|---:|---|
| `16d663409c27963e22d821af7a82b72822ad386a` | Full isolated graph | 0 | 1196 | 3m40.35s / 221s |
| `04d6cd9d2bdb2cbbd34ab19cea020ef18354352e` | Tests only, red witness | 0 | 1154 | 3m13.68s / 194s |
| `88971916f1f50074f0d1054562aad51a72dc6a4b` | Tests only, final | 0 | 1154 | 2m32.50s / 153s |

The build command was `dotnet build tests/Antiphon.Tests --property:OutputPath=bin-debug-44298e2a/ --nologo -v:q`, with the test-only properties above added for recompiles. All slot waits printed 0 seconds.

| Source / selection | Passed | Failed | Skipped | TUnit duration / slot held |
|---|---:|---:|---:|---|
| Base / `LandingRegistrationFreshnessTests/*` | 7 | 0 | 0 | 1.953s / 5s |
| Base / `AmServiceDeployReadinessTests/*` | 3 | 0 | 3 | 139.728s / 143s |
| Base / `WorktreeRemovalAuthorityTests/*` | 9 | 0 | 0 | 53.436s / 56s |
| Red / `LandingRegistrationFreshnessTests/C975_WindowsBacklinkUsesGitDirectorySpelling` | 0 | 1 intended | 0 | 1.306s / 4s |
| Final / `LandingRegistrationFreshnessTests/*` | 8 | 0 | 0 | 1.983s / 5s |
| Final / `AmServiceDeployReadinessTests/*` | 3 | 0 | 3 | 124.140s / 128s |

Each selection above had prefix `/*/*/` in its exact `--treenode-filter`. The red TUnit driver exited 2 with one executed assertion failure, not a build/fixture error; final drivers exited 0. No zero-test selection is credited. The three Linux skips are `ResolvesGitToolsWithoutUsrBinOnPathAsync`, `ResolvesGitToolsPastIncompleteInstallationAsync` and `RefusesWhenNoCompleteGitInstallationExistsAsync`.

Final assembly SHA-256: `22ff2e0ff97cb32ab5bbce57894011d57a6cbfbaf32fe473ad51aaccd1f1ddd9`. Base assembly SHA-256: `c562d1c341e50e02cfdddf2ffa58f61a352b990719d7f70c8959d3f92f702474`.

All selections are below 30 minutes. Durations are single-run observations, not measured p95 budgets; no timeout was changed. TRX retains per-case durations. Opt-in phase JSONL is retained with run IDs `44298e2a-base`, `44298e2a-red`, `44298e2a-green`, group `66` and suite `Antiphon.Tests`; uninstrumented phases and unattributed remainder stay unknown. Build/slot time is separate from case spans.

Generated evidence remains ignored at `/work/worktrees/task-44298e2a/.antiphon/evidence-44298e2a/`: `build-{base,red,green}.log`, `base-<Class>/` and `green-<Class>/` TRX/HTML plus matching logs, `backlink-red/` and its log, the two `deploy-diagnostic-*` directories/logs and their owned throw-only stub, `*-assembly.sha256`, `timing/`, and `output-inventory.txt`. An initial orchestration attempt found no `python3` and launched no build/test; it has no evidence credit.

## Native rerun rows

After the live qualification releases its lane, a native owner must create one fresh isolated build of the landed fixture source, including the neighboring worktree fixes, then run these exact rows. Preserve fresh TRX and stdout/stderr; these are scoped diagnostic reruns and do not qualify all of rc group 66.

| Native filter | Expected expanded cases | Evidence needed |
|---|---:|---|
| `/*/*/LandingRegistrationFreshnessTests/*` | 8 | Original six redirects plus witness; actual `C975` backlink/stamp/list diagnostics |
| `/*/*/AmServiceDeployReadinessTests/*` | 6 | All discovery cases execute; `C504` selected tools, actual shell resolution and first original error |
| `/*/*/WorktreeRemovalAuthorityTests/*` | 9 | Existing landing evidence with durable cleanup reason, filesystem/ref/registration images |

For each row, replace `<Class>` and use a distinct results directory:

```powershell
$env:ANTIPHON_C448_EVIDENCE = Join-Path $PWD '.antiphon/44298e2a-native/landing'
pwsh -NoProfile -File scripts/build-slot.ps1 -Label 44298e2a-native-<Class> -- dotnet run --project tests/Antiphon.Tests --no-build --property:OutputPath=bin-44298e2a-native/ -- --treenode-filter '/*/*/<Class>/*' --report-trx --report-trx-filename native.trx --results-directory .antiphon/44298e2a-native/<Class>
```

No native cause or pass is claimed from Linux skips. If a Windows deletion refusal persists, capture the exact path and holder identity while the fixture process is alive using the already configured trusted handle tooling; a later empty holder list cannot establish ownership. No handle-owner fix, newline-policy change or retry was invented here.

## Publication and cleanup

Every meaningful slice was committed and immediately pushed on the assigned branch:

- `04d6cd9d2bdb2cbbd34ab19cea020ef18354352e` — committed failing Windows-path witness.
- `bbacf558d900975fff54a4962312b034b412243b` — Git-written backlink construction and freshness diagnostics.
- `88971916f1f50074f0d1054562aad51a72dc6a4b` — deploy launch diagnostics.

The 33 task-owned `bin-debug-44298e2a` directories are removed before settlement. Generated evidence stays ignored. The final task Result records the report-only pushed tip, evidence-policy/whitespace checks and fresh conflict-free merge-tree result. No owned command remains in flight at settlement.

--- next stage ---
next: land
handoff: Land the fixture-only repair and diagnostics. Native rerun rows remain owed; this is not an RC qualification receipt.
artifact: /work/worktrees/task-44298e2a/docs/investigations/2026-10-25-rc-db2c2088-group-66-windows-freshness.md
