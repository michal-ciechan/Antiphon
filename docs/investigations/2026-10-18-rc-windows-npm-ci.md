# Windows RC npm bootstrap diagnosis, 2026-10-18

Task: `e340b1a2`, Debug. Source inspected: `a1d24b6fc92d45ae5274b418bf54c6b8819a8697`, branch `feat/card-task-e340b1a2`. The running release checkout and all production processes were left unchanged. This report is the only tracked deliverable; scratch diagnostics are under `C:\Antiphon\scratch\e340b1a2-npm`.

## Outcome and confidence

The npm launcher failed **before npm ci started**. It tried to load npm's own entry points relative to the client working directory. There is no npm package in the client dependency graph and no client node_modules directory at inspection time. A normal client dependency installation cannot explain or fix the launcher's incorrect base directory.

The cause is a **locally reproduced repository launcher defect**: `NativeProcessOwner` quotes every argument using CRT rules, including the bare `npm.cmd` passed to `cmd.exe /c`. The installed npm batch shim derives its own files from `%~dp0`. The exact native owner reproduces both missing paths in an empty scratch cwd, and an instrumented batch file proves `%~dp0` becomes that cwd. Absolute npm.cmd and absolute node plus npm-cli.js both succeed. A scratch PATH shim then passed npm ci, bundle build and lint on an archive of the pinned client. Exact historical executable paths were not recorded in the original npm log, so the installed paths below are current resolution/reproduction evidence, not a captured process-start receipt from the original run.

The run is already ineligible for RC complete-green. The failure is recorded and propagated; continuing .NET tests is **not a fail-open release verdict**. Do not publish based on the eventual .NET results. A launcher repair belongs in a separate Code task; this Debug task did not change executable source.

## Run identity and exact failure

Observed wrapper PID `38396`: `C:\Program Files\PowerShell\7\pwsh.exe`, launched with `-NoProfile -File C:\Antiphon\releases\checkout\scripts\nightly-run.ps1 -NoSync -Profile rc -Trigger rc -Ref rc/2026-10-18 -ExpectedSha a1d24b6fc92d45ae5274b418bf54c6b8819a8697 -CandidateId rc-2026-10-18-a1d24b6fc92d`, the release checkout, candidate-private state/log roots, and `C:\Antiphon\verification` coordination root. The saved launch file is `C:\Antiphon\releases\start-rc-2026-10-18.cmd`; it only prepends the regular PowerShell installation to PATH.

Run ID: `42d2c78424dd4612af43eed3afeaecc2`; start `2026-10-18T16:47:34.4931667Z`. The state file was still `phase=tests`, with no terminal exit at inspection. The outer log names the invoked nightly-tests script. The actual npm failure is in:

`C:\Antiphon\releases\candidates\rc-2026-10-18-a1d24b6fc92d\logs\2026-10-18-1647-42d2c784\npm-ci.log`

Its complete text is:

```text
node:internal/modules/cjs/loader:1413
  throw err;
  ^

Error: Cannot find module 'C:\Antiphon\releases\checkout\client\node_modules\npm\bin\npm-prefix.js'
    at Module._resolveFilename (node:internal/modules/cjs/loader:1410:15)
    at defaultResolveImpl (node:internal/modules/cjs/loader:1051:19)
    at resolveForCJSWithHooks (node:internal/modules/cjs/loader:1056:22)
    at Module._load (node:internal/modules/cjs/loader:1219:37)
    at TracingChannel.traceSync (node:diagnostics_channel:322:14)
    at wrapModuleLoad (node:internal/modules/cjs/loader:238:24)
    at Module.executeUserEntryPoint [as runMain] (node:internal/modules/run_main:154:5)
    at node:internal/main/run_main_module:33:47 {
  code: 'MODULE_NOT_FOUND',
  requireStack: []
}

Node.js v24.6.0
node:internal/modules/cjs/loader:1413
  throw err;
  ^

Error: Cannot find module 'C:\Antiphon\releases\checkout\client\node_modules\npm\bin\npm-cli.js'
    at Module._resolveFilename (node:internal/modules/cjs/loader:1410:15)
    at defaultResolveImpl (node:internal/modules/cjs/loader:1051:19)
    at resolveForCJSWithHooks (node:internal/modules/cjs/loader:1056:22)
    at Module._load (node:internal/modules/cjs/loader:1219:37)
    at TracingChannel.traceSync (node:diagnostics_channel:322:14)
    at wrapModuleLoad (node:internal/modules/cjs/loader:238:24)
    at Module.executeUserEntryPoint [as runMain] (node:internal/modules/run_main:154:5)
    at node:internal/main/run_main_module:33:47 {
  code: 'MODULE_NOT_FOUND',
  requireStack: []
}

Node.js v24.6.0
```

`dotnet-build.log` ends with 1,136 warnings, zero errors, elapsed 2m14.10s. Initially the directory contained the Antiphon execution diagnostics and pending stdout/stderr files; it later advanced to SessionRunner tests. Neither a successful npm build log nor a client-build receipt existed when inspected. The wrapper was still running at the final process check.

## Launcher and filesystem evidence

`scripts/lib/nightly-tests-impl.ps1:340` passes `FilePath=$env:ComSpec`, arguments `('/c','npm.cmd','ci')`, and working directory `C:\Antiphon\releases\checkout\client`. `Invoke-NightlyOwnedProcess` delegates to the native owner. At `scripts/lib/nightly-owned-process.cs:143`, every argument is quoted, producing this command text for the observed environment:

```text
"C:\Windows\system32\cmd.exe" "/c" "npm.cmd" "ci"
```

Current command resolution is `C:\Program Files\nodejs\npm.cmd` and `C:\Program Files\nodejs\node.exe`; ComSpec is `C:\Windows\system32\cmd.exe`. `npm.ps1` is the ordinary PowerShell default but is **not** the nightly call. The installed npm package reports version `11.5.1`. Its batch shim constructs both missing filenames from `%~dp0`, falls back to PATH `node` if its adjacent node.exe is absent, attempts the prefix helper, then attempts npm-cli.js even after the helper fails. This explains the two consecutive errors. The installed prefix and CLI files both exist.

The two inspected launcher source files are byte-identical between this worktree and the running checkout. SHA256:

| File | SHA256 |
|---|---|
| nightly-tests-impl.ps1 | `526054F8E72774A924F1D50624EDD9DBE398474EA8862284474A223A4E67D7CA` |
| nightly-owned-process.cs | `C9D14596FFF7C60C08BAD985ED2669A3706BD68FFEA66DABEA5D688CE26A24EB` |

The package-lock has zero `node_modules/npm` entries, including nested entries. The occurrences of the key `npm` are engine constraints, not dependencies. Neither `client\node_modules`, `client\node_modules\npm`, nor `client\dist\index.html` exists at inspection. No local node/npm launcher is in client. The release checkout, client, and system nodejs directory are ordinary directories without a link target/reparse attribute. There is no evidence here of an npm self-update, a half-installed npm dependency, a copied nightly npm shim, or a junction redirect. An absent directory does not establish whether an earlier clean removed it; the reported clean can explain absent dependencies, but not why the system shim calculates its home as client.

The native launcher was introduced in `07e616f36bbc4f624456b7b01fdd5948c4b8dd8d` (CARD-1039, October 4), replacing Start-Process with the explicit CreateProcess/CRT quoting path. This is the likely introduction point, not a bisect-confirmed attribution. The caller's dead-lock recovery and `git clean -fdx` are reported context; no retained clean transcript was found in the named outer logs. `-NoSync` skips clone/fetch/reset/clean, **not** npm installation. A read of the release checkout's HEAD/ref confirms the pinned SHA on its local master branch; the RC identity comes from the wrapper's ref/expectedSha parameters.

A corroborating first-party repository issue describes the same two errors from quoted bare npm.cmd and success with an absolute npm.cmd on Windows: [openai/codex #52281](https://github.com/openai/codex/issues/52281). That report used `/d /s /c`, so it corroborates the mechanism but is not an exact reproduction of this launcher. The [npm upstream shim](https://github.com/npm/cli/blob/latest/bin/npm.cmd) also derives its entry points from its batch-file directory. Local installed bytes, rather than the moving upstream version, were inspected for this diagnosis.

## Prior evidence: no established earlier successful RC wrapper gate

The retained `C:\Antiphon\releases\candidates` inventory contains only `rc-2026-10-18-a1d24b6fc92d`. Earlier October 18 outer logs show `unmarked-clone` refusals, including run `39c9715c1eb3402da417da276455fa3c`, exit 3; they did not reach npm. Therefore these files cannot establish whether this failure occurred in an earlier successful RC wrapper gate. No such successful receipt was found in that release root.

`docs/investigations/2026-10-16-rc2-cut.md:44` records a **manual, slot-gated** npm ci prerequisite: 818 packages, exit success, waited 0s, held 95s. CP-23 records `npm --prefix client run build` exit 0 at `f7900158ef3416894c720d892bb1c21cc8095057`. That document explicitly says Final verification was incomplete, and the combined checkpoint receipt had a separate build-confinement provenance problem. It is not evidence of a successful full nightly RC gate. The successful manual install/build also did not establish that `NativeProcessOwner -> cmd /c` works.

Older retained nightly logs at `C:\Antiphon\nightly\logs\2026-09-04-1735` and `2026-09-04-1748` show npm ci installing 814 packages. The latter summary gives npm ci/build exit 0 at `829e516a797044a354894790c06d6f8ebb7944e8`, ref `origin/feat/card-task-2aab4b1c`; the overall run is TESTS/red. It predates the October 4 launcher change and is not an RC green. Preserve all these distinctions when comparing the current clean clone with previously populated worktrees.

## Coverage and fail-open assessment

| Roster/prerequisite | Effect of this failure |
|---|---|
| npm ci | npm CLI never starts; dependencies are not installed by this invocation. |
| Client production bundle and lint | Both are guarded by npmOk and are omitted after this failure. No current bundle receipt is earned. |
| Client Vitest (`client`) | The suite is still attempted later. test-client.ps1 can independently run npm ci via PowerShell's call operator when Vitest is missing, so Vitest may recover and produce valid individual test observations. That cannot restore the omitted build/lint or clear overall RC failure. Await its real JSON/count/exit evidence. |
| Automated `Antiphon.E2E` | Requires current client/dist plus generated .NET Playwright installer/Chromium. The preflight runs the browser installation then checks bundle existence. With the observed missing dist, it records e2e-prerequisite-failed and executes no E2E chunk. Reassess only against final artifacts, not the browser download alone. |
| .NET Playwright browser installation | Comes from the built E2E project's playwright.ps1, not client/node_modules/playwright-core. Can install independently; that supplies no client-bundle or E2E pass credit. |
| Client Storybook/screenshot tools | Require client dependencies; Storybook also needs its build. These are not separate required RC suite IDs in this policy. |
| antiphon, session-runner, pty-host, agents-pty, messaging, scripts | Ordinary .NET/script rows can still produce useful diagnostics. They do not substitute for client/E2E coverage. Script census includes test-client.ps1 and may encounter its own install path. No blanket invalidation of unrelated .NET assertions is warranted. |

At nightly-tests-impl.ps1:343, nonzero npm ci sets npmOk=false, overallFailed=true, outcome=BUILD, and coverageComplete=false. The latter is never restored to true. Final exit is nonzero when overallFailed is set. nightly-run-impl.ps1:486 onward propagates the test exit, forces testsPassed=false for nonzero testExit, and asks the release credit predicate for green. That predicate requires coverageComplete, testsPassed, reportDelivered, exit 0 and matching identity. Thus the release decision fails closed even though .NET compilation and suites continue. The inner summary's testsPassed flag alone can describe test assertions while the build failed; consumers must use the whole verdict.

File a launcher/Windows batch invocation defect after the caller's duplicate search. Do **not** file the allegation that npm ci is ignored and a passing .NET suite makes this release green. A separate cost/early-stop improvement could avoid hours of work after a mandatory prerequisite fails, but that is a policy choice distinct from a release correctness defect.

## Scratch reproduction and remedy validation

The first attempt was refused by the host build slot: memory_floor (669 MB, later 653 MB, floor 6144 MB), then `BUILD SLOT timeout after 2m position=1`. No rows ran and no unleased retry occurred. Once the active gate advanced from Antiphon to SessionRunner, a new leased attempt succeeded. All native children exited, all output drained, and no termination was requested.

Six native-launch observations, all using the unchanged owner at the inspected source:

| Invocation | Observed result |
|---|---|
| `/c`, `npm.cmd`, `--version` | Exit 1; both original MODULE_NOT_FOUND errors reproduced under the scratch client cwd. |
| `/c`, `call`, `npm.cmd`, `--version` | Exit 1; quoted `call` is not recognized. Merely inserting another argument is not a fix. |
| `/c`, absolute system npm.cmd, `--version` | Exit 0; npm 11.5.1. |
| Absolute node.exe, absolute npm-cli.js, `--version` | Exit 0; npm 11.5.1. |
| `/c`, `probe.cmd`, `hello` | Exit 0; batch resides in shim-bin but `%~dp0` and `%~f0` incorrectly name client cwd. |
| `/c`, `call`, `probe.cmd`, `hello` | Exit 1; quoted `call` is not recognized. |

Lease `9088b315-0af8-42c2-83ae-776de9b970ee`, waited 0s, held 5s. Evidence: `C:\Antiphon\scratch\e340b1a2-npm\probe-results.json`, `probe.ps1`, and its six logs. These are diagnostic launches, not six application test cases; three exit-zero and three deliberately observed nonzero outcomes.

The validated operational shim is `C:\Antiphon\scratch\e340b1a2-npm\repair-shim\npm.cmd`:

```bat
@echo off
"C:\Program Files\nodejs\node.exe" "C:\Program Files\nodejs\node_modules\npm\bin\npm-cli.js" %*
exit /b %ERRORLEVEL%
```

It does not use `%~dp0`. SHA256 `AAB3D8573A4A01152B8C1263D8017F5C44C45E2B1562408672BFC7D228EB9C70`. Prepending this directory only to the scratch process's PATH made the **unchanged** native owner and original bare npm.cmd argument arrays work:

| Scratch row | Result |
|---|---|
| npm --version | Exit 0, 11.5.1 |
| npm ci | Exit 0; 818 packages added, 819 audited |
| npm run build | Exit 0; bundle generated |
| npm run lint | Exit 0; eslint with max-warnings 0 |

Lease `1a9e8513-b536-4a5a-aee5-dc94c7bc1911`, waited 0s, held 179s. All four children exited/drained with terminationRequested=false. `repair-results.json`, `repair-probe.ps1`, and `shim-*.log` are in the same scratch evidence root. The source was extracted with git archive from `a1d24b6fc92d45ae5274b418bf54c6b8819a8697` into `repair-copy\source\client`; npm used a scratch cache. The build emitted the expected git-rev-parse failure because the archive has no .git, and the configured version fallback is unknown; it also emitted the bundle-size warning. This validates command execution/compilation, **not a release bundle or RC coverage receipt**. No scratch bundle should be copied into the release checkout.

System tool hashes used: node.exe `3428A3D055501883385D78B19128550D3E8BE39A89E7EDBA683A9B06830257C7`; system npm.cmd `21B46C69AD6E2F231F02A9E120F4BA6C8E75FEF5A45637103002EAB99F888AB8`; npm-cli.js `3CE7CBA6F5128DD5F54C98B6A5036B0F850496878CC2E21044B675FE3C594E3E`.

## Smallest remedy and exact post-run procedure

There is no `-InstallClientDependencies`, `-SkipNpmCi`, or install-only flag on nightly-run.ps1/nightly-tests.ps1 at this SHA. Their supported automatic path already runs npm ci every time. Manually populating client/node_modules, adding npm to package.json, copying the npm package into client, globally updating npm, or repeating git clean does not repair a wrongly based shim. Do not modify the current checkout while its run owns it.

1. After PID 38396 and its owned test children have exited, retain the completed summary/state and all original logs. Verify the candidate's `state\run.lock` and `C:\Antiphon\verification\native-run.lock` have been released, and that checkout HEAD, candidate journal and remote rc ref still match the expected SHA. A moved ref requires a new candidate identity; do not reuse the old identity with another SHA. A live lock owner remains a stop condition. Do not remove locks just to make a rerun start.
2. Verify the scratch shim's hash above, retain it alongside the toolchain provenance, and prepend its directory to **only the new wrapper child's PATH**. This uses the wrapper's existing inherited command-resolution path; it is a validated local toolchain workaround, not a new documented install flag. Let the wrapper itself run npm ci/build/lint in its checkout. No node_modules deletion, npm dependency addition, global npm update, or copying of scratch dependencies is necessary.
3. Run the complete command below in a fresh PowerShell process after those checks. It automatically allocates a new run ID. Do not supply -Suites, -NoReport or seams. Preserve the original failed evidence; do not combine its native test passes with another run to claim RC complete-green.
4. Commission the durable Code fix separately: resolve npm.cmd to its absolute application path before constructing the native cmd invocation, and cover a path with spaces plus a different cwd in a real Windows batch-shim regression test. Preserve Job Object ownership, exit propagation and draining. The absolute-path version probe succeeded; full production implementation/review is still required. Once landed, run a full RC profile at the new pinned source SHA instead of using this old candidate identity.

The following rerun recipe uses the validated scratch shim at the unchanged pinned SHA. **It was not executed against the release checkout by this task.** Use a fresh PowerShell child so the PATH override ends with that child:

```powershell
$shim = 'C:\Antiphon\scratch\e340b1a2-npm\repair-shim\npm.cmd'
if ((Get-FileHash -LiteralPath $shim).Hash -ne 'AAB3D8573A4A01152B8C1263D8017F5C44C45E2B1562408672BFC7D228EB9C70') {
  throw 'Shim changed; requalify it before use.'
}
$env:PATH = 'C:\Antiphon\scratch\e340b1a2-npm\repair-shim;C:\Program Files\PowerShell\7;' + $env:PATH
& 'C:\Program Files\PowerShell\7\pwsh.exe' -NoProfile `
  -File 'C:\Antiphon\releases\checkout\scripts\nightly-run.ps1' `
  -NoSync -Profile rc -Trigger rc -Ref rc/2026-10-18 `
  -ExpectedSha a1d24b6fc92d45ae5274b418bf54c6b8819a8697 `
  -CandidateId rc-2026-10-18-a1d24b6fc92d `
  -CheckoutRoot 'C:\Antiphon\releases\checkout' `
  -StateRoot 'C:\Antiphon\releases\candidates\rc-2026-10-18-a1d24b6fc92d\state' `
  -LogRoot 'C:\Antiphon\releases\candidates\rc-2026-10-18-a1d24b6fc92d\logs' `
  -ReleaseRoot 'C:\Antiphon\releases' -CoordinationRoot 'C:\Antiphon\verification' `
  -RemoteUrl 'https://github.com/michal-ciechan/Antiphon'
```

## Verification limits and handoff

No .NET project build, application test suite, process termination, live checkout mutation, restart, or gate intervention was performed. Six launcher observations and four successful scratch prerequisites ran under granted slots after the initial refused attempt. The scratch bundle is not release evidence. Generated logs, JSON receipts, cache, dependencies and bundle remain external to Git in the scratch root; no alternate .NET output directories were created. The scratch probe has zero PowerShell parse errors. The full RC gate remains incomplete and must finish naturally.

The caller needs to commission/route the launcher repair and decide whether to use the now-validated temporary PATH workaround after the current run exits. Keep current RC verification incomplete and retain its useful native diagnostics. This document supplies the exact evidence and deferred commands rather than modifying a running gate.

--- next stage ---
next: decide
handoff: Retain this RC attempt as incomplete; after it exits, use the validated child-local PATH shim for a fresh full RC run if desired, and commission the Windows npm launcher repair separately.
artifact: docs/investigations/2026-10-18-rc-windows-npm-ci.md
