# CARD-1545: live-Claude lane readiness and changed inputs

2026-10-31. Investigator task `719ec15b`; source checkout `96715b56623d57a16c6788838bd18281a40e768d`, branch `feat/card-task-719ec15b`.

**INPUTS CHANGED.** Latest directly verified C680 GREEN: **2026-10-30 05:14:14 UTC**, source `00dc13544ffb1abfb6239fe7b86279cb7fa74298`, Claude **2.1.312**, Haiku, Windows/ModernConPty. Installed Claude now reports **2.1.316** and has different bytes. Current rc also changes native custody, exit observations and remote-control input fencing. **Carve-out: defined, partly implemented on task branches, not executable as the paid lane on master/rc.** A six-result manual diagnostic selection is expressible with existing tests, but cannot start alongside the qualification's native lock. No live test was run here.

## Scope, evidence boundaries and confidence

Read-only investigation of product/runtime state; the only authored deliverable is this report. Builds=0, tests=0, provider sessions=0, provider prompts=0. Only CLI `--version` commands were executed. No login or credential store was opened, no profile was changed, and no service was restarted. Git refs were fetched without checking out another branch.

The brief both invites older candidate outcome searches and says that anything under `C:\Antiphon\releases` may be inspected by directory name only. The stricter boundary was honored: candidate directory names were listed, contents were not read. The live d47848cc supervisor, lock, candidate and logs were not inspected or changed. Its lock ownership is **caller-supplied current context, not an independently measured observation**. Never infer that it has released from this report; the eventual executor must acquire the shared lock normally.

Searches covered repository investigations/plans, relevant Git history (including `git log --all -S ANTIPHON_HEADED_TESTS -- tests scripts` and headed/C680/live commit messages), the desktop nightly log tree, canonical checkout Markdown/log evidence and named retained C680/C514 TRX. `C:\Antiphon\nightly\logs` contains only the two 2026-09-04 run directories found here; no live-core result was found there. Repository memory-named files were inventoried; they did not provide a newer live-core receipt. Search coverage does not establish that an unretained run never happened. Historical source claims and directly read TRX are distinguished below.

## Last passes: the six-result first lane

The exact six methods come from the parent plan on `origin/feat/card-task-76bd1ec4`, `docs/superpowers/plans/2026-10-31-card-1545-rc-scoped-lanes-live-claude-lane-plan.md`, section “Scoped project and first roster”. Its inventory is broader than this first lane. The S1c design on `origin/feat/card-task-8b284c8f` preserves this membership and keeps native acceptance pending.

| Core test, fully qualified class under the stated project | Last supported execution/pass found | Source / CLI / qualification limit |
|---|---|---|
| Antiphon.Tests: `Antiphon.Tests.Agents.ClaudeDialogWatchdogCanaryTests.C680_LiveNudgeRefusesPermissionAndQuestionDialogs` | Direct TRX: 30 Oct, latest finish 05:14:14.305436Z; three consecutive 1 passed / 0 failed / 0 skipped | `00dc13544ffb1abfb6239fe7b86279cb7fa74298`; Claude 2.1.312; Haiku; ModernConPty. Both dialog probes have complete UserPrompt submission receipts and refusal/zero-write evidence. |
| Antiphon.Agents.Pty.Tests: `ClaudeSubmitContractLiveTests.Submitting_via_two_writes_completes_a_turn(claude)` | **No dated, source-bound live pass found** | Ordinary fake submit passes or headed skips do not qualify this argument. Current live verdict uses a screen done-pattern. |
| Same project: `ClaudeRemoteControlAtStartupCanaryTests.Control_auto_connects_a_bridge_session_before_the_first_user` | 1 Sep historical source claim for the startup experiment; **individual passing result not recovered** | Commit `418718b53523f0bb47ee0c558ea4f848d20cb8b5` says live Claude 2.1.252 proved settings-off and later slash arm. It does not separately attest this control's result. |
| Same: `ClaudeRemoteControlAtStartupCanaryTests.Settings_file_prevents_auto_connect_and_keeps_startup_records` | 1 Sep historical source claim of success; no individual TRX recovered | Same commit/2.1.252. Not a current-source receipt. |
| Same: `ClaudeRemoteControlAtStartupCanaryTests.Remote_control_slash_command_still_arms_after_settings_off` | 1 Sep historical source claim of success; no individual TRX recovered | Same commit/2.1.252. Not a current-source receipt. |
| Same: `ClaudeRemoteControlMenuCanaryTests.C514_Idle_menu_clearance_preserves_bridge_and_converts_prompt` | Direct TRX: 14 Sep 04:15:18.8688131Z–04:15:29.0762247Z, Passed, body 10.2074116s | Stored Code report attributes this to `86d4627b983a4231d74892f8fc3b1ac6000f5035`; **CLI version not recorded**. A screen-level diagnostic pass, not complete bridge/transcript acceptance. |

C680 raw evidence root: `C:\Antiphon\worktrees\card-task-29984c60\.antiphon\c680-fix29984c60`. Directly read `live1/run.trx`, `live2/run.trx`, `live3/run.trx`, their `metadata.json`, `build.json`, and `cli-provenance.json`. All three TRX outcomes are Passed. Driver times: 106.239, 53.084, 43.867 seconds; bodies: 59.492, 28.136, 24.934 seconds. Metadata records clean source before/after and assembly SHA-256 `15AE13A1CD5A25BDEE53EBFF74DDE2F82F49A5DAD72D8881995ED5EB1EC66F1E`. CLI SHA-256 was `6099672EBE5DCB518B67D1D32DDE1E3360BC6BEE1DAE7F8D28A69C49ACC7C865`. Build took 509.434 seconds. See also `docs/investigations/2026-10-30-c680-submission-repair.md:13` and `:42`.

Unedited evidence lines in **each** C680 TRX:

```text
C680 case=Bash submission=confirmed evidence=UserPrompt
C680 native cli=2.1.312 backend=ModernConPty case=Bash inputRevision=2 backendDelta=0 refused=true
C680 case=AskUserQuestion submission=confirmed evidence=UserPrompt
C680 native cli=2.1.312 backend=ModernConPty case=AskUserQuestion inputRevision=2 backendDelta=0 refused=true
```

C514 raw evidence: `C:\src\Antiphon\.antiphon\c514-v9-canary\run.trx:8`; source attribution and explicit limitations: `C:\src\Antiphon\.antiphon\task-1f0c3963.md:90`. The TRX itself binds the method and built `bin-c514` path, not a Git SHA or provider version. Do not strengthen that provenance.

Other historical live claims found include `ClaudeVerifiedDeliveryTests` short/hugeLine/multiLine at `75f0a64a581a239963d6e1b7df46db389bf9ff9d` (21 July, 4/4 claim), and its batched body at `8a88dff2eaa03fe20f1bd6faad83fa1657217ae1` (22 July, 5/5 claim). Neither records a CLI version or current-lane qualification. The inventory appendix explicitly marks other dates/SHAs/versions as unknown rather than treating routine Full greens as live passes.

## Inputs since that baseline

Observed desktop CLI commands: `claude --version`, `where.exe claude`, `codex --version`, `where.exe codex`, `grok --version`, `where.exe grok`, plus `Get-Command` and `Get-FileHash -Algorithm SHA256` on each PowerShell-resolved command path. No model request was made.

| Command | Version now | Resolved path | SHA-256 |
|---|---|---|---|
| Claude | 2.1.316 (Claude Code) | `C:\Users\lndco\.local\bin\claude.exe` | `E896A9D8DA519353A49BF69CA4A695BA7E09067752687C675B3969041E632D52` |
| Codex | codex-cli 0.160.0 | `C:\Users\lndco\AppData\Roaming\npm\codex.ps1` | `0C149DB80ED0BF442C810146B0AD0163B74982FE4542D673F56C354D7B8229CB` |
| Grok | 1.0.62 (1474f9cbdd91), stable | `C:\Users\lndco\.grok\bin\grok.exe` | `86C6C766B7961597C2A327F77E43A7B73CBC44F5CEE9DA6DD74BA73B45AE8B68` |

Codex hash is the npm **entry shim**, not an attestation of its native payload chain. `where.exe codex` also reports the extensionless and `.cmd` shims. C680's baseline records no Codex/Grok versions: comparison for those two is UNKNOWN and they are outside Claude-only membership. No `cl.bat`, `cl.ps1`, `cl.cmd` or `cl.exe` was found in this process's PATH scan. Thus the legacy PTY resolver would fall back to Claude here; another launching shell must resolve again.

Separate older-provider context: `docs/investigations/2026-09-06-card-0395-rules-transport-acceptance.md:25` records mapped Grok live attempt 5 passing at 18:37:28.756 UTC and Herdr live passing at 18:56:32.917 UTC on 6 September. That report attributes the installed CLI as 1.0.13 (`:488`), retains its archive digest and child/task identities, but does not bind those individual green runs to a full tested source SHA. The mapped and Herdr live methods in the appendix therefore have this dated historical report evidence; individual TRX was not reopened here. Its endurance arm is explicitly not a complete accepted pass. Compared with this older record Grok **has changed**, 1.0.13 to 1.0.62; it remains outside the Claude lane. Codex 0.147.0 is recorded by `docs/investigations/2026-08-27-card-0133-s0-boot-wedge-probe.md:14`, but that is a real-binary/**stub** experiment, not a green paid-provider baseline; the installed version is now 0.160.0.

Fetched observations: master `96715b56623d57a16c6788838bd18281a40e768d`; rc/2026-10-27 `53835c8c2033618a76e6cb512f386e31c11e4ed7`. These are source refs, not deployed/qualification candidate identity. The C680 repair appears on rc as cherry-pick `8fcb44c3f`; its source files have **no net diff** from the tested repair SHA. A literal `baseline..rc` log includes the cherry-picked repair even though it is not a new behavior change. Net diff and ancestry were considered separately.

Reproduction of the scoped history query (not a build/test):

```powershell
git log --oneline 00dc13544ffb1abfb6239fe7b86279cb7fa74298..origin/rc/2026-10-27 -- src/Antiphon.Agents.Pty src/Antiphon.PtyHost src/Antiphon.SessionRunner src/Antiphon.Agents tests/Antiphon.Agents.Pty.Tests tests/Antiphon.Tests/Agents/ClaudeDialogWatchdogCanaryTests.cs tests/Antiphon.Tests/Agents/ClaudeDialogCanaryDiagnostics.cs server/Application/Services/RemoteControlRecoveryService.cs server/Infrastructure/Agents
```

| Series found in the comparison | Representative commits | Could affect live behavior? |
|---|---|---|
| C514 arm race / composer proof and Enter fence | `a870bc8be`, `517477dda`; witnesses `6be99ebc8`, `2e5420e5a` | Yes for production remote-control delivery: refreshed refused Enter and one-snapshot composer/fence custody. Changed `RemoteControlRecoveryService`, `ClaudeAdapter`, `RunnerClaudeAdapter`. The direct PTY C514 test does not itself traverse the entire production coordinator. |
| CARD-1619 structured PTY exit/signal propagation | `74c18e602`, `e363ff0ac`, `a7738fe6a`, `b9dc68224`, `e49607899`, `7e004ccc7`, `1a3d3390e` | Yes: exit callbacks, stop attribution, host/runner recovery and cleanup, including Windows compatibility paths; Linux signal changes alone are not evidence of Windows input change. |
| CARD-1614 custody / launch containment / recovery and linger | `5ee70db1c`, `8da9b8c18`, `81844eb26`, `13873a9e9` | Yes for shared launch/release lifecycle and Windows linger; much of the new namespace/cgroup work is Linux-only. Changes include `PtyAgentRunner`, `PtySession`, `HostSession`, `SessionRunnerRuntime`. |
| CARD-1616 checkpoint admission/output and discovery cost | `09a103053`, `4647e8b65`, `f399faa98`, `f44e1c75e` | Changes qualification drivers/receipts/performance; not evidence of a changed Claude composer. Relevant if using checkpoint execution. |
| CARD-1618 aggregate Debug record | `fbeef58c1` | Documentation/aggregate diagnosis in this range, not a separately identified Windows PTY repair. Do not mislabel it as one. |
| C680 test repair and diagnostic series | `8b13b9ffe`, `f8656f74a`, `3451c52b4`, `8fcb44c3f` | Already represented by the baseline repair; C680/diagnostics files are net-identical. ClSession and the core PTY canary source files also show no net change against this baseline. |

**Verdict:** CLI changed definitely; relevant native and production delivery code changed definitely; core test files largely unchanged against this recent baseline. User settings/hooks, account state, service behavior and effective remote model are unmeasured. Equal local source would not prove those external inputs unchanged.

## What exists today, and why the lane is not ready

Neither fetched master nor rc contains `scripts/run-live-lane.ps1`, `scripts/lib/live-lane-run.ps1`, `tests/live-lanes.json`, `tests/live-lane-checkpoints/`, or `tests/Shared/LiveClaude/` (checked with `git ls-tree`). This is stronger than assuming a pending land completed.

| Slice | Observed task branch / tip | Existing capability and limit |
|---|---|---|
| S1b admission/outcomes, land task 102e8d54 described as pending by caller | `origin/feat/card-task-228e67c0`, `5ae578146ee2e5950efcd25f47c23c659082fe35`; Final review is of code `f41be02bd` | Diagnostic fixture admission, lock-before-slot, custody and typed outcomes. `scripts/run-live-lane.ps1:18` dispatches Native mode; `scripts/lib/live-lane-run.ps1` explicitly returns `20 native-unqualified` for non-Fixture requests. This is not a wrapper capable of running paid rows. Not contained in master/rc. |
| S1c executable | `origin/feat/card-task-b0b6ec31`, `333e481b82c4885e513dc5a9b2ab082432ec984a` | Portable executable/context/identity seams, six witnesses. Stored report `.antiphon/task-b0b6ec31.md:1` says native observer/issuer missing and native credit refused; tested source `2196368b0d0c22039fdb6def279e6144169ed8aa`. |
| S1c profile | `origin/feat/card-task-f72769c9`, `0288f26e9ea95e5e174cc3b9af79bea3a898a643` | Eleven portable profile/path/model/source witnesses. Stored report `.antiphon/task-f72769c9.md:1`; tested source `1e7b5fea3176583154c7f4abbbd563e1e25f76d0`. No provisioned native lane/profile attestation. |
| S1c startup | `origin/feat/card-task-e57194b7`, `7ebdd835639973ca91dbe129386c735b949005d9` | Five portable startup witnesses, shared control and loaded settings evidence. Stored report `.antiphon/task-e57194b7.md`; tested source `699c1defcbe7a8e3745438f4fcc144e7d02d7053`. Native bridge/hooks still unqualified; delivery/sanitization owed. |
| S1c delivery | caller names `feat/card-task-82f0a842` | No matching origin ref was returned by `git ls-remote --heads`; fetch of that name refused. No delivery implementation was found in fetched master/rc. Current status beyond that is UNKNOWN, not “failed” or “complete”. |

No attested executable/context on this Windows host, lane-owned config-directory binding, or current-source six-row native receipt was established. The portable branch reports are not Windows execution evidence. An installed executable's hash is not the missing native launch/image/session observer. S1a manifest/census and later sanitization, authoritative receipts, reuse/signing and gate migration likewise cannot be credited from absent paths. Do not globally turn on headed for Full: `tests/test-execution-policy.json:1229` clears it in safe child environments; C680's guard is `tests/Antiphon.Tests/Agents/ClaudeDialogWatchdogCanaryTests.cs:24` and it has no rc exclusion. An excluded sibling's absence is expected; C680's required skip remains incomplete.

The mechanism is reconstructed from stored source/evidence, not a new defect reproduction: the legacy runtime gates skip without headed; Full clears headed; S1b deliberately refuses native; S1c supplied only portable seams. Remaining uncertainty is historical pass coverage and missing native qualification, not whether setting the flag on Full buys this lane.

## Existing manual front door, selection and costs

**Can it execute now?** Existing master/rc tests can be selected without lane tooling, as diagnostics. **Can it start right now? No:** caller says d47848cc owns the shared native lock. Wait for normal release and then acquire it; never use a fresh coordination root, delete/age-steal the lock, hold a build slot while waiting, or run beside Full. `scripts/lib/release-gate.ps1:19`, `:310`, `:385`, `:449` define `C:\Antiphon\verification\native-run.lock` and atomic admission/release. Its busy result is deferred-busy. This investigation did not acquire it.

Run on the **Windows desktop**, from a separately owned clean checkout pinned to the intended candidate SHA; do not change the live qualification candidate. Run the two projects sequentially. Exact method filters below deliberately omit the other menu test and all unrelated headed tests. Six separate method invocations avoid uncertain mixed-class/mixed-method OR syntax and select Explicit startup/menu methods directly. This is one scoped campaign, not six retries.

The following is an **unexecuted operational command sequence for legacy master/rc**, not a new implementation or native lane certificate. It assumes the operator accepts PTY production-default model selection; C680 explicitly uses Haiku. It must not be copied onto the S1c task branches: those core callers require a native context that is not supplied here. Use a disposable `pwsh -NoProfile` shell, substitute the owned checkout and its full intended candidate SHA, and retain outputs locally. Existing native binaries, Node, authenticated Claude and database-test prerequisites must already be available.

```powershell
Set-Location '<owned clean checkout of the intended candidate>'
if ((git rev-parse HEAD).Trim() -ne '<full intended candidate SHA>') { throw 'wrong source' }
. ./scripts/lib/nightly-common.ps1
. ./scripts/lib/release-gate.ps1
$liveRunId = 'manual-live-claude-' + [guid]::NewGuid().ToString('N')
$native = Enter-ReleaseGateNativeLock -CoordinationRoot 'C:\Antiphon\verification' -RunId $liveRunId -Lane 'live-claude-core-windows'
if (-not $native.Ok) { throw ('deferred: ' + $native.Reason) }
try {
    $env:ANTIPHON_HEADED_TESTS = '1'
    $env:DISABLE_AUTOUPDATER = '1'
    $env:ANTIPHON_PTY_BACKEND = 'modern'
    $env:CLAUDE_CONFIG_DIR = $null
    $env:CLAUDE_CODE_CHILD_SESSION = $null
    $env:CLAUDE_CODE_SESSION_ID = $null
    $env:CLAUDE_CODE_BRIDGE_SESSION_ID = $null
    $env:ANTIPHON_C680_DEBUG_ROOT = $null
    $env:ANTIPHON_C680_DEBUG_MODEL = $null
    $env:ANTIPHON_C680_DEBUG_SUBMIT = $null
    # Preserve HOME/USERPROFILE and the operator's accepted profile.
    $results = Join-Path (Get-Location) ('.antiphon/' + $liveRunId)
    pwsh -NoProfile -File scripts/build-slot.ps1 -Label live-c680-build -- dotnet build tests/Antiphon.Tests --property:OutputPath=bin-live719/ --nologo --verbosity quiet
    if ($LASTEXITCODE -ne 0) { throw "C680 build exit $LASTEXITCODE" }
    pwsh -NoProfile -File scripts/build-slot.ps1 -Label live-pty-build -- dotnet build tests/Antiphon.Agents.Pty.Tests --property:OutputPath=bin-live719/ --nologo --verbosity quiet
    if ($LASTEXITCODE -ne 0) { throw "PTY build exit $LASTEXITCODE" }
    $rows = @(
        @('tests/Antiphon.Agents.Pty.Tests','ClaudeRemoteControlAtStartupCanaryTests','Control_auto_connects_a_bridge_session_before_the_first_user'),
        @('tests/Antiphon.Agents.Pty.Tests','ClaudeRemoteControlAtStartupCanaryTests','Settings_file_prevents_auto_connect_and_keeps_startup_records'),
        @('tests/Antiphon.Agents.Pty.Tests','ClaudeRemoteControlAtStartupCanaryTests','Remote_control_slash_command_still_arms_after_settings_off'),
        @('tests/Antiphon.Agents.Pty.Tests','ClaudeSubmitContractLiveTests','Submitting_via_two_writes_completes_a_turn'),
        @('tests/Antiphon.Agents.Pty.Tests','ClaudeRemoteControlMenuCanaryTests','C514_Idle_menu_clearance_preserves_bridge_and_converts_prompt'),
        @('tests/Antiphon.Tests','ClaudeDialogWatchdogCanaryTests','C680_LiveNudgeRefusesPermissionAndQuestionDialogs')
    )
    foreach ($row in $rows) {
        $filter = '/*/*/' + $row[1] + '/' + $row[2]
        $rowResults = Join-Path $results $row[2]
        pwsh -NoProfile -File scripts/build-slot.ps1 -Label $row[2] -- dotnet run --project $row[0] --no-build --property:OutputPath=bin-live719/ -- --treenode-filter $filter --report-trx --report-trx-filename run.trx --results-directory $rowResults
        if ($LASTEXITCODE -ne 0) { throw "row exit $LASTEXITCODE; retain evidence, no automatic retry" }
        [xml]$trx = Get-Content -LiteralPath (Join-Path $rowResults 'run.trx')
        $outcomes = @($trx.SelectNodes('//*[local-name()="UnitTestResult"]'))
        if ($outcomes.Count -ne 1 -or $outcomes[0].outcome -ne 'Passed') {
            throw 'not one passing result; retain evidence, skip is inconclusive'
        }
    }
} finally {
    # Release only after all foreground drivers/test-owned children have joined.
    Exit-ReleaseGateNativeLock -CoordinationRoot 'C:\Antiphon\verification' -RunId $liveRunId
}
```

Commands use the required host build-slot wrapper (`scripts/build-slot.ps1:6`), isolated forward-slash output, and six exact selectors; no build/test was run to validate this sequence. Exit 4 is not-run, not permission to bypass the gate. A row can exit zero while skipping; inspect each result before continuing. Stop after the first non-pass and retain earlier outcomes. Cleanup the task-owned `bin-live719` directories only after children join and evidence is retained, using an explicit workspace-contained literal-path list. No cleanup is authorized against another task's output.

| Row | Model / intended prompts | Estimate and source bound |
|---|---|---|
| Startup control | No model prompt; authenticated bridge may use network | Unmeasured wall. 15s bridge window plus readiness/cleanup (`ClaudeRemoteControlAtStartupCanaryTests.cs:35`, `:43`, `:183`, `:216`). |
| Settings off | No model prompt | Unmeasured; 8s absence plus composer/readiness/cleanup (`:34`, `:86`, `:122`). Its current independent negative assertion does not require the separate control to have passed. |
| Explicit slash arm | No model prompt | Unmeasured; up to 45s arm plus readiness/cleanup (`:36`, `:138`). |
| Submit contract | 1 prompt; legacy wrapper/CLI production default | Up to 2min response window plus startup/cleanup (`ClaudeSubmitContractTests.cs:75`, `:98`). Explicitly pins **InboxConhost**, despite the ambient modern setting (`:103`); this is intentional existing coverage. |
| C514 menu | 1 intended submitted prompt; legacy wrapper/CLI production default | Unmeasured; 12s menu + 12s token observation, echo waits and startup (`ClaudeRemoteControlMenuCanaryTests.cs:87`); historical body 10.207s. Current screen verdict cannot establish delivery/bridge survival. |
| C680 | 2 prompts; explicit Haiku | Measured 44–106s driver; 6min overall test deadline (`ClaudeDialogWatchdogCanaryTests.cs:30`, `:39`). |

Budgeting estimate: **about 15–25 minutes including two builds**, excluding unknown native-lock/build-slot waits; the only comparable large build measured here was 8.5 minutes, the PTY build and five PTY rows have no current-host timing sample. Reserve **25 minutes of live execution after builds** as the parent plan's provisional ceiling, not a measured p95 or enforcement provided by the above commands. Four intended model prompts total; proposed parent stop cap six submitted prompts, one attempt per method. Existing manual commands do **not** enforce an account token/currency ceiling or provide a campaign watchdog. Dollar/token cost is UNKNOWN; prompt count does not bound internal requests, tool turns, hooks or context cost. No quota was spent in this investigation.

Model correction to the brief's wrapper assumption: current `ClSession.ResolveOrThrow` prefers `cl.*` (`ClSession.cs:22`), but no wrapper was found on this shell's PATH. The PTY launches have **no `--model` argument**, so no named model can be honestly reported from their source; operator/CLI defaults apply. C680's normal path is explicitly Haiku. The S1c profile branch supports named versus explicit production-default through its context, but is not the legacy manual front door and has no qualified native issuer. Choosing Haiku for every row cannot be claimed from these existing commands.

## Profile setting and remaining decisions

The operator explicitly accepts the own-profile case. It is **not a blocker and not an open approval question**. With `CLAUDE_CONFIG_DIR` unset, existing tests use the current Windows user's `.claude` profile and settings; leave HOME and USERPROFILE alone. No authentication probe was commissioned, so actual signed-in/account/quota state is unverified.

Current `ClSession.HeadedSafeEnv` clears only three nested-session/bridge markers and deliberately preserves user hooks (`ClSession.cs:53`). Startup transcript/session reads hard-code UserProfile `.claude/projects` and `.claude/sessions` (`ClaudeRemoteControlAtStartupCanaryTests.cs:362`, `:373`), and sandbox trust writes hard-code UserProfile `.claude.json` (`:541`). In contrast production `ClaudeProjectTrust.DefaultConfigPath` already honors CLAUDE_CONFIG_DIR (`src/Antiphon.Agents.Pty/ClaudeProjectTrust.cs:35`). Therefore a config-dir override is **partly supported, not end-to-end coherent in the current legacy core**; changing HOME alone is not a demonstrated isolation mechanism. S1c-profile provides portable seams for that gap, not completed native provisioning.

Accepted effects still belong in the lane context: ambient hooks/settings/instructions can change behavior or add usage; scratch trust entries and session/transcript history touch the operator profile; CP-5/menu omit an explicit cwd and inherit the launching checkout (`ClaudeSubmitContractTests.cs:98`, `ClaudeRemoteControlMenuCanaryTests.cs:94`). Raw failure screens may enter TRX. Retain evidence privately; sanitization is not yet the lane's qualified boundary. These facts limit comparability even though the operator accepts them.

Operational disposition for the caller: use the already-authorized own profile, queue a Windows executor behind d47848cc's normal native-lock release, pin the intended rc source/CLI, then perform the existing six exact diagnostic selections once and return six fresh results. No further general “go ahead” is needed under the caller's standing authority. This investigation's explicit no-live commission prevented execution here; it did not authorize a lock bypass.

Remaining operator settings are **model** (accept existing production-default for PTY plus Haiku for C680, or wait for qualified context-controlled selection), **quota ceiling** (four intended prompts/six stop cap is a provisional prompt budget; account currency/token limit remains unspecified), and **timing** (after the held native lock, no overlap). These are context decisions for the executor, not a request to reapprove the accepted profile. A one-word acceptance of those stated provisional defaults can select the diagnostic campaign; it cannot turn it into a qualified lane receipt or silently sanction unbounded spend. If the ceiling must be mechanically enforced, the existing manual front door is insufficient.

## Not done, noted

Complete/qualify the already-planned native executor, launch/profile observations, delivery oracles, sanitization and receipts before this carve-out can earn lane/release credit; no fix was designed or implemented here.

## Remaining uncertainties

No fresh six-row native receipt; latest pass date unknown where specified; incomplete candidate archive search due the explicit releases boundary; C514 source attribution is report-based; no measured effective model/account/quota or current native session/image identity; delivery branch 82f0a842 unresolved; no independent observation of the live qualification lock or its candidate. Resolve historical uncertainty with retained per-method TRX/source/version receipts from the excluded archives or their owner. Resolve present readiness with the separately commissioned, serialized native execution after the existing lane prerequisites; portable greens cannot resolve it.

## Appendix: observed scoped Git history

Literal output of the history command above at the recorded rc ref (ancestry difference, not a count of net behavioral changes):

```text
1a3d3390e fix: bind Linux child stops to kernel identity and release helper gate
7e004ccc7 fix(card-1619): record signal-first issuer memory with bounded untrusted diagnostics
28ff4c03e test(card-1619): use actual launcher fallback disposal and disk-recovery boundaries
dd80f4317 test(card-1619): witness real stop ordering and issuer attribution before S4
e49607899 fix(card-1619): classify signals and consume actual structured exit evidence
b9dc68224 feat(card-1619): carry nullable exit evidence into server storage and projections
a7738fe6a fix(pty): carry observed exit signals through host and runner recovery
e363ff0ac fix: retain Linux PTY exit signals with fail-closed Porta access and replay
c6d5a9e77 test: force the real Porta callback and replay overlap
ec735f0b2 test: add owed native Windows exit compatibility witnesses
74c18e602 test: witness dropped Linux PTY signals and fast-exit subscription gaps
517477dda fix(C514): bind arm composer proof and Enter fence to one runner snapshot
a870bc8be fix(C514): refresh refused arm Enter within pinned input custody
8fcb44c3f test: confirm native C680 probe submission before dialog checks
3451c52b4 test: add opt-in C680 submission and transcript timing controls
f8656f74a test: use runner transcript DTO in C680 diagnostics
8b13b9ffe test: capture C680 native dialog setup diagnostics
81844eb26 fix(card-1614): recover only on an explicit seal and arm linger after release
13873a9e9 test(card-1614): pin root-exit seals and real native release faults
d43ea79ec test(card-1614): record the launch-failure namespace before the first write
56fd46d31 fix(card-1614): expose the containment test seam across the pty assembly
6f2f3e651 test(card-1614): witness that a released launch failure exits within its TTL
8da9b8c18 fix(card-1614): withhold Linux auto-seal and recover dead hosts
d81d6d557 test(card-1614): witness Linux seal, recovery, and Windows linger
af5c0ed33 test(card-1614): witness the entrypoint library guard
33e4e6b0a fix(card-1614): read custody trees through the namespace
49dcbc758 test(card-1614): witness the custody reader ignoring the namespace
5ee70db1c fix(card-1614): isolate custody trees and keep Linux unadvertised
96d56898a test(card-1614): witness identity, namespace, and admission
bd59c1b6c test: pin NativeLinuxOnly on the 45 Linux-only rows
7eec288ad test: add the pinned NativeLinuxOnly witness before annotation
```

## Appendix: complete source-method inventory of the live-provider families

Verified against the checked-out test sources, with method declaration lines below. This is a source roster, not compiled discovery: argument matrices can expand a method into several results. Only the six core results above are selected for the interim campaign. All dates/source SHAs/CLI versions/outcomes not explicitly supported in the main report are UNKNOWN; no individual green is inferred from Full, a class name or a fake/stub sibling.

Mixed families include zero-model-call installed-CLI/auth checks. In particular input probes, overlays, trust/TUI/startup checks, Grok sign-in/native-session/size, Codex MCP/empty-composer checks are not all paid model tests. Startup/remote-control can still need authenticated network service. The plan distinguishes those from actual model-turn methods; they are included to avoid silently dropping the inventory names. Pure replay/detector tests are not included.

### ClaudeAppendSystemPromptCanaryTests

Source: `tests/Antiphon.Agents.Pty.Tests/ClaudeAppendSystemPromptCanaryTests.cs`. Last supported run: core/historical evidence above where stated; otherwise UNKNOWN.

- `Append_marker_survives_interactive_launch_compact_and_resume` (line 44)
- `Multi_line_append_arg_survives_windows_arg_quoting` (line 87)

### ClaudeCompactionCanaryTests

Source: `tests/Antiphon.Agents.Pty.Tests/ClaudeCompactionCanaryTests.cs`. Last supported run: core/historical evidence above where stated; otherwise UNKNOWN.

- `Compact_writes_a_boundary_entry_and_renders_the_compacted_screen_line` (line 39)
- `Manual_compact_with_args_writes_raw_prompt_boundary_and_continuation_with_no_turn_end` (line 134)

### ClaudeComposerCaptureProbeTests

Source: `tests/Antiphon.Agents.Pty.Tests/ClaudeComposerCaptureProbeTests.cs`. Last supported run: core/historical evidence above where stated; otherwise UNKNOWN.

- `Composer_capture_matrix` (line 52)
- `Cut_point_resolution` (line 89)
- `Single_chunk_boundary` (line 133)

### ClaudeComposerRenderCanaryTests

Source: `tests/Antiphon.Agents.Pty.Tests/ClaudeComposerRenderCanaryTests.cs`. Last supported run: core/historical evidence above where stated; otherwise UNKNOWN.

- `Composer_render_contract_for_delivery_style_writes` (line 46)

### ClaudeDangerousTests

Source: `tests/Antiphon.Agents.Pty.Tests/ClaudeDangerousTests.cs`. Last supported run: core/historical evidence above where stated; otherwise UNKNOWN.

- `Claude_dangerous_version_exits_zero_with_version_string` (line 22)
- `Claude_dangerous_print_returns_pong` (line 40)
- `Claude_dangerous_tui_reaches_ready_within_30s` (line 59)
- `Claude_dangerous_single_prompt_returns_response` (line 78)
- `Claude_dangerous_tool_use_executes_without_permission_prompt` (line 104)
- `Claude_dangerous_sequential_prompts_share_context` (line 135)
- `Claude_dangerous_cruncheddetector_waits_through_slow_bash_task` (line 170)
- `Claude_dangerous_detects_claude_asking_a_question` (line 206)
- `Claude_dangerous_can_answer_claude_question_and_receive_acknowledgement` (line 236)
- `Claude_dangerous_kill_midflight_no_orphans` (line 279)

### ClaudeHeadedTests

Source: `tests/Antiphon.Agents.Pty.Tests/ClaudeHeadedTests.cs`. Last supported run: core/historical evidence above where stated; otherwise UNKNOWN.

- `Cl_version_via_pty_exits_zero_with_version_string` (line 19)
- `Cl_print_mode_returns_response` (line 36)
- `Cl_tui_reaches_ready_with_default_detector` (line 53)
- `Cl_headed_single_prompt_returns_response` (line 72)
- `Cl_headed_sequential_prompts_share_context` (line 97)
- `Cl_headed_tool_use_returns_systeminfo` (line 125)
- `Cl_headed_exit_command_completes_in_5s` (line 151)
- `Cl_headed_kill_midflight_no_orphans` (line 171)
- `Cl_headed_resume_preserves_context_across_runner_instances` (line 208)

### ClaudeHookAdditionalContextCanaryTests

Source: `tests/Antiphon.Agents.Pty.Tests/ClaudeHookAdditionalContextCanaryTests.cs`. Last supported run: core/historical evidence above where stated; otherwise UNKNOWN.

- `PreToolUse_additionalContext_reaches_the_model_without_blocking` (line 59)
- `additionalContext_survival_on_resume_is_not_confounded_by_assistant_text` (line 140)
- `Subagent_PreToolUse_carries_agent_id` (line 228)

### ClaudeInputProbeCanaryTests

Source: `tests/Antiphon.Agents.Pty.Tests/ClaudeInputProbeCanaryTests.cs`. Last supported run: core/historical evidence above where stated; otherwise UNKNOWN.

- `A_ready_claude_answers_the_input_probe_and_the_composer_is_left_empty` (line 50)
- `Ctrl_u_clears_a_single_typed_line_from_the_rendered_composer` (line 110)

### ClaudeInteractionTests

Source: `tests/Antiphon.Agents.Pty.Tests/ClaudeInteractionTests.cs`. Last supported run: core/historical evidence above where stated; otherwise UNKNOWN.

- `Screen_detects_permission_prompt_for_bash_tool` (line 36)
- `Approve_permission_prompt_via_numbered_choice_executes_tool` (line 79)
- `Deny_permission_prompt_via_numbered_choice_skips_tool` (line 124)

### ClaudeInterruptCanaryTests

Source: `tests/Antiphon.Agents.Pty.Tests/ClaudeInterruptCanaryTests.cs`. Last supported run: core/historical evidence above where stated; otherwise UNKNOWN.

- `Esc_mid_turn_stops_work_on_screen_and_writes_the_interrupt_marker_to_the_jsonl` (line 37)
- `Esc_during_a_pure_text_stream_unwinds_the_turn_without_a_marker` (line 152)
- `Safe_listed_bash_command_auto_approves_without_a_permission_dialog` (line 238)

### ClaudeLocalCommandCanaryTests

Source: `tests/Antiphon.Agents.Pty.Tests/ClaudeLocalCommandCanaryTests.cs`. Last supported run: core/historical evidence above where stated; otherwise UNKNOWN.

- `Clear_writes_a_command_record_and_no_turn_completion_after_it` (line 38)

### ClaudeOverlayCanaryTests

Source: `tests/Antiphon.Agents.Pty.Tests/ClaudeOverlayCanaryTests.cs`. Last supported run: core/historical evidence above where stated; otherwise UNKNOWN.

- `Esc_on_an_idle_empty_composer_is_a_noop_and_a_body_still_renders` (line 32)
- `One_Esc_dismisses_the_model_overlay_and_restores_the_composer` (line 67)

### ClaudePasteLossCanaryTests

Source: `tests/Antiphon.Agents.Pty.Tests/ClaudePasteLossCanaryTests.cs`. Last supported run: core/historical evidence above where stated; otherwise UNKNOWN.

- `Marked_bodies_into_real_claude_are_diffed_against_its_transcript` (line 55)

### ClaudeRemoteControlAtStartupCanaryTests

Source: `tests/Antiphon.Agents.Pty.Tests/ClaudeRemoteControlAtStartupCanaryTests.cs`. Last supported run: core/historical evidence above where stated; otherwise UNKNOWN.

- `Control_auto_connects_a_bridge_session_before_the_first_user` (line 43)
- `Settings_file_prevents_auto_connect_and_keeps_startup_records` (line 86)
- `Remote_control_slash_command_still_arms_after_settings_off` (line 138)

### ClaudeRemoteControlMenuCanaryTests

Source: `tests/Antiphon.Agents.Pty.Tests/ClaudeRemoteControlMenuCanaryTests.cs`. Last supported run: core/historical evidence above where stated; otherwise UNKNOWN.

- `One_Esc_dismisses_the_remote_control_management_menu` (line 35)
- `C514_Idle_menu_clearance_preserves_bridge_and_converts_prompt` (line 87)

### ClaudeSignalCanaryTests

Source: `tests/Antiphon.Agents.Pty.Tests/ClaudeSignalCanaryTests.cs`. Last supported run: core/historical evidence above where stated; otherwise UNKNOWN.

- `Real_claude_still_emits_the_turn_end_signal_we_depend_on` (line 42)

### ClaudeSubmitConfirmCanaryTests

Source: `tests/Antiphon.Agents.Pty.Tests/ClaudeSubmitConfirmCanaryTests.cs`. Last supported run: core/historical evidence above where stated; otherwise UNKNOWN.

- `Enter_on_an_empty_composer_is_a_no_op` (line 57)
- `A_collapsed_pastes_jsonl_record_carries_the_full_body_not_the_placeholder` (line 125)

### ClaudeSubmitContractLiveTests

Source: `tests/Antiphon.Agents.Pty.Tests/ClaudeSubmitContractLiveTests.cs`. Last supported run: core/historical evidence above where stated; otherwise UNKNOWN.

- `Submitting_via_two_writes_completes_a_turn` (line 20)

### ClaudeTrustPromptCanaryTests

Source: `tests/Antiphon.Agents.Pty.Tests/ClaudeTrustPromptCanaryTests.cs`. Last supported run: core/historical evidence above where stated; otherwise UNKNOWN.

- `An_untrusted_directory_blocks_the_tui_and_is_detected_within_seconds` (line 45)
- `The_2_1_258_dialog_is_the_highlighted_list_layout` (line 94)
- `A_trusted_directory_never_reads_as_blocked` (line 124)

### ClaudeTuiModeTests

Source: `tests/Antiphon.Agents.Pty.Tests/ClaudeTuiModeTests.cs`. Last supported run: core/historical evidence above where stated; otherwise UNKNOWN.

- `Spawned_claude_uses_classic_renderer_not_alternate_screen` (line 23)

### ClaudeVerifiedDeliveryTests

Source: `tests/Antiphon.Agents.Pty.Tests/ClaudeVerifiedDeliveryTests.cs`. Last supported run: core/historical evidence above where stated; otherwise UNKNOWN.

- `Real_evidence_appears_then_submit_completes_a_turn` (line 46)

### CodexCanaryTests

Source: `tests/Antiphon.Agents.Pty.Tests/CodexCanaryTests.cs`. Last supported run: core/historical evidence above where stated; otherwise UNKNOWN.

- `One_real_turn_writes_UserMessage_AgentMessage_token_count_and_task_complete` (line 54)
- `The_rollout_is_lazy_and_Enter_on_an_empty_composer_submits_nothing` (line 146)
- `The_TUI_does_not_print_its_session_id_and_a_typed_newline_does_not_submit` (line 201)

### CodexComposerCanaryTests

Source: `tests/Antiphon.Agents.Pty.Tests/CodexComposerCanaryTests.cs`. Last supported run: core/historical evidence above where stated; otherwise UNKNOWN.

- `Composer_submit_contract_on_the_modern_backend` (line 89)

### CodexDoneDetectionCanaryTests

Source: `tests/Antiphon.Agents.Pty.Tests/CodexDoneDetectionCanaryTests.cs`. Last supported run: core/historical evidence above where stated; otherwise UNKNOWN.

- `Turn_lifecycle_contract_on_the_modern_backend` (line 51)

### CodexMcpBootProbeTests

Source: `tests/Antiphon.Agents.Pty.Tests/CodexMcpBootProbeTests.cs`. Last supported run: core/historical evidence above where stated; otherwise UNKNOWN.

- `Baseline_scratch_cwd` (line 36)
- `Baseline_repo_cwd_incident_shape` (line 44)
- `Suppressed_repo_cwd` (line 57)

### CodexOverlayCanaryTests

Source: `tests/Antiphon.Agents.Pty.Tests/CodexOverlayCanaryTests.cs`. Last supported run: core/historical evidence above where stated; otherwise UNKNOWN.

- `Esc_on_an_idle_empty_composer_is_a_noop_and_a_body_still_renders` (line 31)

### FakeVsRealClipParityTests

Source: `tests/Antiphon.Agents.Pty.Tests/FakeVsRealClipParityTests.cs`. Last supported run: core/historical evidence above where stated; otherwise UNKNOWN.

- `Fake_and_real_claude_agree_on_the_chunk_boundary` (line 78)
- `Fake_and_real_claude_agree_that_a_bracketed_paste_is_not_clipped` (line 181)

### GrokCanaryTests

Source: `tests/Antiphon.Agents.Pty.Tests/GrokCanaryTests.cs`. Last supported run: core/historical evidence above where stated; otherwise UNKNOWN.

- `Models_cache_context_window_is_still_500000` (line 53)
- `Composer_submit_contract_on_the_modern_backend` (line 83)
- `Turn_end_signals_vs_turn_completed_and_updates_flush_latency` (line 223)
- `Esc_interrupt_shape_in_updates_jsonl` (line 334)
- `Session_recap_shape_and_trigger` (line 416)
- `Fresh_cwd_and_fresh_grok_home_launch_blocking` (line 547)

### GrokMidTurnInterruptCanaryTests

Source: `tests/Antiphon.Agents.Pty.Tests/GrokMidTurnInterruptCanaryTests.cs`. Last supported run: core/historical evidence above where stated; otherwise UNKNOWN.

- `Ctrl_C_on_an_empty_composer_mid_tool_turn_cancels_the_turn_and_keeps_the_session_alive` (line 30)
- `Ctrl_C_with_a_draft_in_the_composer_keeps_the_session_alive` (line 100)

### GrokNativeSessionCanaryTests

Source: `tests/Antiphon.Agents.Pty.Tests/GrokNativeSessionCanaryTests.cs`. Last supported run: core/historical evidence above where stated; otherwise UNKNOWN.

- `Resume_of_an_unknown_id_exits_nonzero_fast_and_creates_nothing` (line 22)
- `Create_writes_the_session_directory_at_launch` (line 68)
- `Resume_of_that_directory_starts` (line 119)

### GrokQuestionPopupCanaryTests

Source: `tests/Antiphon.Agents.Pty.Tests/GrokQuestionPopupCanaryTests.cs`. Last supported run: core/historical evidence above where stated; otherwise UNKNOWN.

- `Popup_present_after_ask_user_question_usage_does_not_match_answer_clears_esc_does_not` (line 42)

### GrokSignInCanaryTests

Source: `tests/Antiphon.Agents.Pty.Tests/GrokSignInCanaryTests.cs`. Last supported run: core/historical evidence above where stated; otherwise UNKNOWN.

- `Detector_matches_the_live_sign_in_screen_within_10s` (line 23)

### GrokSubmitWhileWorkingCanaryTests

Source: `tests/Antiphon.Agents.Pty.Tests/GrokSubmitWhileWorkingCanaryTests.cs`. Last supported run: core/historical evidence above where stated; otherwise UNKNOWN.

- `Submit_during_a_tool_turn_writes_cancelled_then_the_new_user_chunk` (line 28)
- `Submit_during_a_streaming_text_turn_writes_cancelled_then_the_new_user_chunk` (line 101)
- `Queued_follow_up_during_a_tool_turn_is_silent_on_jsonl_until_drain` (line 175)

### GrokUsageOverlayCanaryTests

Source: `tests/Antiphon.Agents.Pty.Tests/GrokUsageOverlayCanaryTests.cs`. Last supported run: core/historical evidence above where stated; otherwise UNKNOWN.

- `Esc_on_an_idle_empty_composer_is_a_noop_and_a_body_still_renders` (line 31)
- `One_Esc_dismisses_the_usage_overlay_and_restores_the_composer` (line 69)

### OrchestratorWorkspaceLayoutCanaryTests

Source: `tests/Antiphon.Agents.Pty.Tests/OrchestratorWorkspaceLayoutCanaryTests.cs`. Last supported run: core/historical evidence above where stated; otherwise UNKNOWN.

- `Claude_loads_parent_CLAUDE_md_from_a_nested_checkout` (line 45)
- `Claude_sibling_import_is_dropped_without_the_forward_slash_approval` (line 75)

### PtyPasteMarkerExperiments

Source: `tests/Antiphon.Agents.Pty.Tests/PtyPasteMarkerExperiments.cs`. Last supported run: core/historical evidence above where stated; otherwise UNKNOWN.

- `Real_claude_by_conpty_backend` (line 172)
- `Real_claude_controls_and_size_sweep` (line 204)
- `Real_claude_delivery_envelope` (line 250)

### DelegationPipelineE2ETests

Source: `tests/Antiphon.E2E/DelegationPipelineE2ETests.cs`. Last supported run: core/historical evidence above where stated; otherwise UNKNOWN.

- `A_small_well_defined_job_is_classified_as_a_worker` (line 42)
- `A_multi_step_job_is_classified_as_a_sub_orchestrator` (line 54)

### DelegationSequencingE2ETests

Source: `tests/Antiphon.E2E/DelegationSequencingE2ETests.cs`. Last supported run: core/historical evidence above where stated; otherwise UNKNOWN.

- `An_orchestrator_runs_two_delegates_in_sequence_and_the_second_sees_the_firsts_work` (line 46)

### OutputDistillationApplyCanaryTests

Source: `tests/Antiphon.E2E/OutputDistillationApplyCanaryTests.cs`. Last supported run: core/historical evidence above where stated; otherwise UNKNOWN.

- `Real_apply_and_long_fallback_reach_parent_and_files_survive_cleanup` (line 38)

### ClaudeAdapterIntegrationTests

Source: `tests/Antiphon.Tests/Agents/ClaudeAdapterIntegrationTests.cs`. Last supported run: core/historical evidence above where stated; otherwise UNKNOWN.

- `Full_round_trip_via_seam_returns_response_text` (line 20)

### ClaudeDialogWatchdogCanaryTests

Source: `tests/Antiphon.Tests/Agents/ClaudeDialogWatchdogCanaryTests.cs`. Last supported run: core/historical evidence above where stated; otherwise UNKNOWN.

- `C680_LiveNudgeRefusesPermissionAndQuestionDialogs` (line 22)

### CodexAdapterIntegrationTests

Source: `tests/Antiphon.Tests/Agents/CodexAdapterIntegrationTests.cs`. Last supported run: core/historical evidence above where stated; otherwise UNKNOWN.

- `Full_round_trip_via_the_production_runner_adapter_returns_the_models_answer` (line 55)

### GrokSizeCaptureTests

Source: `tests/Antiphon.Tests/Agents/GrokSizeCaptureTests.cs`. Last supported run: core/historical evidence above where stated; otherwise UNKNOWN.

- `Capture_native_sizes` (line 19)

### RunnerGrokAdapterReadyTestsPty

Source: `tests/Antiphon.Tests/Agents/RunnerGrokAdapterReadyTestsPty.cs`. Last supported run: core/historical evidence above where stated; otherwise UNKNOWN.

- `C1011_real_fresh_worktree_ready_and_one_turn` (line 77)

### GrokRulesCompactionAcceptanceTests

Source: `tests/Antiphon.Tests/Application/GrokRulesCompactionAcceptanceTests.cs`. Last supported run: core/historical evidence above where stated; otherwise UNKNOWN.

- `Live_inline_rules_baseline_records_two_genuine_auto_compactions` (line 26)
- `Live_file_rules_remain_effective_across_two_auto_compactions_and_resume` (line 29)

### GrokRulesHerdrAcceptanceTests

Source: `tests/Antiphon.Tests/Application/GrokRulesHerdrAcceptanceTests.cs`. Last supported run: core/historical evidence above where stated; otherwise UNKNOWN.

- `Live_model_herdr_standing_attachment_obeys_rules_after_startup_ack` (line 31)

### GrokRulesLiveMappedDispatchTests

Source: `tests/Antiphon.Tests/Application/GrokRulesLiveMappedDispatchTests.cs`. Last supported run: core/historical evidence above where stated; otherwise UNKNOWN.

- `Live_model_delegate_obeys_file_only_rules_and_consumes_full_brief` (line 32)

### SlashCommandMenuReconciliationTests

Source: `tests/Antiphon.Tests/Application/SlashCommandMenuReconciliationTests.cs`. Last supported run: core/historical evidence above where stated; otherwise UNKNOWN.

- `Catalog_matches_live_claude_slash_menu` (line 59)

Additional manual paid harness: `scripts/hooks/__tests__/live-s2-probe.mjs` (two intended Haiku print turns, 180-second cap per process); no run/pass receipt recovered here. `scripts/verify-phone-home-grok.ps1`, Docker real/live cases and OutputDistillation have distinct operating/approval contexts and are not core lane members.

RealCliStubGate families (Claude/Codex/Grok RealCliStubProxy/Herdr counterparts, Card0168InteractiveProbe, ClaudeEffortPrompt, SpecialistToolPolicy, CodexBootWedge/CommandLength, GrokRules stub-wire/calibration/dispatch), provider-oracle workspace methods, Herdr dummy-child tests, real Chromium/PDF, Telegram conformance and local OS timing were checked as separate inventory classes: a real executable/browser/external service is not proof of a paid model call. They are not enrolled in the six-result campaign. GrokRulesHerdr has a separate stub method excluded above.

Extra dated historical source claim: `ClaudeHookAdditionalContextCanaryTests.cs:13` and `:26`, commit `fee8c1f118fad7574538d0438a76fcf67bf986ca` (30 Aug), report measurements against Claude 2.1.251 including context/tool/subagent/resume behavior. No individual per-method TRX was recovered here; do not upgrade this to four certified current-source passes.

--- next stage ---
next: decide
handoff: Inputs changed (Claude 2.1.312 to 2.1.316 and rc native/delivery code); C680 last passed 2026-10-30. Own profile is accepted. Choose PTY production-default/model and quota ceiling; queue six exact diagnostic selections after d47848cc releases the shared native lock. Paid lane tooling/native receipts remain unqualified.
artifact: docs/investigations/2026-10-31-live-claude-lane-readiness.md
