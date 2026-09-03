# CARD-0341 — the herdr launch script carries the launch env; a gkp Grok launch without routing is refused

**Date:** 2026-09-03

**Plan task:** 81b20772 (Frontier, plan + build)

**Card:** CARD-0341 — "Herdr Grok launches skip gkp env: grok.com OAuth + cli-chat-proxy while llm-proxy key is never forwarded"

**Evidence base:** the card's `~/.grok/logs/unified.jsonl` table (five Grok processes in 12 h, three of them `auth started method=grok.com` with `has_external_api_key=false`, one gkp-launched process prefetching `cli-chat-proxy.grok.com` with the dummy key); `HerdrLaunchScript.cs` (`Env never enters the script`), `HerdrPaneChild.LaunchAsync` / `ResolveTargetPaneAsync` / `CompleteTypedLaunchAsync` / `AllocatePaneAsync`, `HerdrPaneSidecar`, `HerdrLastPane`, `HerdrProblemMapper`, `SessionRunnerHttpClient.StartAsync`, `AgentTuiLaunchResolver.ResolveAsync` (env layering), the CARD-0187 / CARD-0224 / CARD-0260 / CARD-0323 plans, `docs/herdr-sessions.md` §4, and a live `pwsh` probe of the script shape below.

## Decision

The typed launch script is the only thing that reaches a **reused** pane, so the launch env goes into the script. `request.Env` is applied as `Set-Item -LiteralPath 'Env:NAME' -Value '…'` lines before `& 'exe' @(args)`, on every typed launch (fresh root, allocated pane, and relaunch-in-place alike). `tab.create` / `pane.split` / `workspace.create` keep passing env too — that is what herdr's own shell inherits — but nothing depends on it any more.

A gkp-profile Grok launch (any argument whose file name is `gkp.ps1`) is refused **before the runner touches herdr** unless the launch env can route it: a resolvable project marker, `GROK_BASE_URL`, and a dummy key. The refusal is a 409 `herdr_gkp_env_missing` problem naming the missing names, and the server now surfaces the runner's problem detail as the session's `FailureReason` instead of "Response status code does not indicate success: 409".

`--project $env:X_LLM_PROJECT` (and any other whole-argument `$env:NAME` / `${env:NAME}` token) is resolved from the launch env **before quoting**, so the child gets the value regardless of what the pane shell has. A token whose name is absent from the env is left verbatim (the pre-existing behaviour; the gkp gate is what makes that loud for the profile that matters).

The "script kept on failure for diagnosis" rule survives, but the kept file is rewritten with every env value replaced by `<redacted>` and the argument tokens unresolved, so a secret never outlives a failed launch on disk. On success the script is deleted exactly as today.

**Out of scope, deliberately (card ask 5):** #28 — pool delegates launched as bare `grok.exe` from the registry definition. That path has no gkp at all; the gate here keys on `gkp.ps1` in the arguments and does not fire for it.

## Why here, not elsewhere

- **Not `Start-Process -Environment`.** It detaches the child from the pane's console (or needs `-NoNewWindow -Wait`, which changes the process tree herdr detects the agent through). `Set-Item Env:` is process-wide for the pane's shell, which is exactly the pty-host semantics the card expects, and it is what a relaunch-in-place can re-apply.
- **Not the server resolver for the `$env:` token.** The pty-host lane already works because `gkp.ps1` expands the hint itself from the process env. The defect is lane-specific: a single-quoted script argument. The resolution therefore lives where the quoting happens.
- **Gate on the runner.** The runner is the only component that knows what will be typed and into which pane. The server does not know whether the pane is fresh or reused, and does not need to: `request.Env` is the same either way, so the check is a pure function of the request and runs first, before `ConnectAndValidateAsync`.

## Measured script shape

Probed live with `pwsh 7` on this machine (`scratchpad/envcheck.ps1`): `Set-Item -LiteralPath 'Env:NAME' -Value '…'` accepts doubled single quotes, embedded newlines, `$`, backticks and double quotes byte-identical; an empty `-Value ''` leaves the variable present-and-empty (`Test-Path Env:` true); `Remove-Item -LiteralPath 'Env:NAME' -ErrorAction SilentlyContinue` is silent on a missing name; values persist in the calling session after `& 'script.ps1'` returns and reach a child started with `& 'exe' @(args)`.

```powershell
Remove-Item -LiteralPath 'Env:STALE_FROM_LAST_LAUNCH' -ErrorAction SilentlyContinue   # relaunch only
Set-Item -LiteralPath 'Env:GROK_BASE_URL' -Value 'http://localhost:10746/v1'
Set-Item -LiteralPath 'Env:X_LLM_PROJECT' -Value 'PredictionMarkets'
Set-Item -LiteralPath 'Env:XAI_API_KEY' -Value 'llm-key-proxy'
Set-Location -LiteralPath 'D:\worktrees\card'                                          # created root only, unchanged
& 'C:\Program Files\PowerShell\7\pwsh.exe' @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', 'C:\Users\x\.local\bin\gkp.ps1', '--project', 'PredictionMarkets')
```

Env lines are emitted in ordinal name order so the file is deterministic and diffable.

## S1 — env in the script; relaunch re-applies and clears

**Files:** `src/Antiphon.SessionRunner/HerdrLaunchScript.cs`, `HerdrPaneChild.cs`, `HerdrPaneSidecar.cs`, `HerdrLastPane.cs`; tests `tests/Antiphon.SessionRunner.Tests/HerdrLaunchShapeTests.cs`.

1. `HerdrLaunchScript.BuildContent(exe, args, env, workingDirectory, clearNames, redactEnv)`: `Remove-Item` lines for `clearNames`, then one `Set-Item` per env entry (ordinal order), then the existing `Set-Location` prelude and command. Whole-argument `$env:NAME` / `${env:NAME}` tokens (case-insensitive `env:`) are replaced by the env value when the name is present (exact name first, then case-insensitive); otherwise left verbatim. `redactEnv: true` renders every value as `<redacted>` and leaves tokens unresolved. `Write` gains the same parameters. The class doc no longer says env never enters the script; it says why it does and what the failure file looks like.
2. `HerdrPaneSidecar.LaunchEnvNames` and `HerdrLastPane.LaunchEnvNames` (`IReadOnlyList<string>?`, names only, never values; null on pre-field files). `WriteSidecar` records the request's names; `FromSidecar` copies. `TargetPane` carries `PreviousEnvNames` on the relaunch arm; `CompleteTypedLaunchAsync` computes `clearNames = previous − current` (case-insensitive) so a name the previous launch set and this one does not carry is removed from the reused shell rather than inherited.
3. `CompleteTypedLaunchAsync` passes `request.Env`. On any exception after the script is written (wrong kind, timeout, cancellation) it rewrites the file redacted before rethrowing; the runtime's existing catch still kills and disposes. Success still deletes the file.
4. Tests: script content with env and token resolution; success writes the secret into the file but never into a typed `pane.send_text`; the fresh-root test now asserts the `Set-Item` line is present; relaunch-in-place re-applies env and emits `Remove-Item` for the stale name; wrong-kind failure leaves a file that contains `<redacted>` and not the secret.

## S2 — refuse a gkp launch that cannot route; surface the detail

**Files:** `src/Antiphon.SessionRunner/HerdrGkpLaunchGuard.cs` (new), `HerdrPaneChild.cs`, `HerdrProblemMapper.cs`, `src/Antiphon.SessionRunner.Contracts/SessionRunnerContracts.cs` (`HerdrProblemTypes.GkpEnvMissing = "herdr_gkp_env_missing"`), `server/Infrastructure/Agents/SessionRunner/SessionRunnerHttpClient.cs`; tests `tests/Antiphon.SessionRunner.Tests/HerdrGkpLaunchGuardTests.cs` (new), `HerdrLaunchShapeTests.cs`, `tests/Antiphon.Tests/Agents/SessionRunnerHttpClientHerdrWireTests.cs`.

1. `HerdrGkpLaunchGuard.IsGkpLaunch(args)`: any argument whose `Path.GetFileName` is `gkp.ps1` (ordinal-ignore-case). `MissingRequirements(args, env)` returns the human-readable list of what is missing:
   - a project: `X_LLM_PROJECT` non-blank in env, **or** `--project <value>` / `--project=<value>` whose value is a literal (not an unresolvable `$env:` token);
   - `GROK_BASE_URL` non-blank;
   - a key: `XAI_API_KEY` or `GROK_CODE_XAI_API_KEY` non-blank.
   `Require(sessionId, args, env)` throws `HerdrLaunchException(code: herdr_gkp_env_missing)` naming the missing items and where to set them (agent `launchEnv`, project `DefaultLaunchEnv`). A gkp launch without `GROK_CLI_CHAT_PROXY_BASE_URL` is allowed but logged as a Warning — the card measured that exact leak (`cli-chat-proxy.grok.com` with the dummy key).
2. `HerdrPaneChild.LaunchAsync` runs the guard first, before `ConnectAndValidateAsync`, so a refused launch allocates, renames, and types nothing. `HerdrProblemMapper.TitleFor` names the new code; the 409 mapping already exists.
3. `SessionRunnerHttpClient.StartAsync` calls `ThrowForRunnerProblemAsync` before `EnsureSuccessStatusCode`, so a 409/404/503 problem from `POST /sessions` becomes `ConflictException(detail, code)` and the launch path's generic catch stores the runner's detail in `AgentSession.FailureReason`. `pane_occupied` (CARD-0224) gets the same improvement for free.
4. Tests: guard unit cases (literal `--project`, resolvable token, unresolvable token, each missing name, non-gkp args never fire); an end-to-end refusal with `fake.Requests` empty and the script never written; a passing gkp launch whose script carries the resolved `--project` value; the wire test for the 409 → `ConflictException` detail.

## S3 — docs

`docs/herdr-sessions.md` §4 launch sequence and the CARD-0224 relaunch row: the script now applies env (and clears stale names on relaunch), resolves `$env:` argument tokens, is redacted when kept on failure, and the gkp gate. `docs/ai-agent-tui-configuration.md` gkp profile section: what the launch env must carry on the herdr lane. Card file under `docs/cards/` is generated — not edited.

## Verify

```
dotnet run --project tests/Antiphon.SessionRunner.Tests --property:OutputPath=bin-card0341/ -- --treenode-filter "/*/*/HerdrLaunchShapeTests/*"
dotnet run --project tests/Antiphon.SessionRunner.Tests --property:OutputPath=bin-card0341/ -- --treenode-filter "/*/*/HerdrGkpLaunchGuardTests/*"
dotnet run --project tests/Antiphon.SessionRunner.Tests --property:OutputPath=bin-card0341/ -- --treenode-filter "/*/*/Herdr*/*"
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-card0341/ -- --treenode-filter "/*/*/SessionRunnerHttpClientHerdrWireTests/*"
```

Then delete every `bin-card0341` directory the build dropped.

## Live follow-through (operator, not this task)

The standing Grok agents' `launchEnv` and the projects' `DefaultLaunchEnv` were seeded on the other machine on 2026-09-03 (card §"Local ops already done"). After this ships, a relaunch of each standing gkp agent re-types the script with that env into its last pane; nothing needs to be re-seeded. A gkp agent whose env still lacks a name fails with `herdr_gkp_env_missing` and the names in `FailureReason` — that is the intended loud failure, not a regression.
