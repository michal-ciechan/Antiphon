# Agent CLI upgrade runbook (Codex first, then Claude Code and Grok)

Owner of: how to move an agent CLI (codex-cli, Claude Code, Grok Build) to a new version so a
new model is picked up, and how to change the model ladder afterwards. The operating skill is
[antiphon-cli-upgrade](../.claude/skills/antiphon-cli-upgrade/SKILL.md). Mechanics owned
elsewhere stay there: the staged server2 rollout is
[docker-stack.md](docker-stack.md#staged-server2-rolling-rollout-card-0934), AppHost restart is
[apphost-runbook.md](apphost-runbook.md), kinds, launch argv and the ladder are
[agent-kinds.md](agent-kinds.md), checkpoints and build slots are
[testing-and-build.md](testing-and-build.md).

**The rule (operator, 2026-11-21): every model or CLI change starts with "upgrade the CLI, then
run the tests."** The CLI is upgraded in a throwaway environment, its real TUI screens are
captured, the tests are run against those measured screens, and only then is anything rolled out.
A model ladder or routing change comes last.

Why: the 2026-11-20/21 rollout of codex-cli 0.170.0 and GPT-6.2 found three production bugs one
AppHost restart at a time. Each one was visible on the first real screen of the new CLI, and a
CLI-first test pass would have found all three before the rollout:

1. The ready gate read a new last row (`← for agents · ? for shortcuts`) as the footer, so every
   Codex launch stayed `Unknown` and was SIGKILLed at 120 s
   ([not-ready note](investigations/2026-11-21-codex-0170-not-ready-on-server2.md), commit
   `25b83a250c962280a10ebcfeaa9cd5d1742813d5` on `origin/feat/card-task-9730feee`). Fixed by
   `fd16cd4ca`, Review r1 clean.
2. The pointer-composer evidence learned the same step, but with text typed 0.170 drops the
   shortcuts text and leaves a chip-only last row (`⚠ 2 warnings · f2 to view`). Enter was
   withheld and the task failed after 10 minutes with `DeliveryVerificationFailed`
   ([delivery note](investigations/2026-11-21-codex-0170-delivery-verification-failed.md),
   commit `243f9924bc7333a4f7fc07b5eae4bf1ddfb72101` on `origin/feat/card-task-d6e31ffd`).
   Fixed by `9adbf3ba1` (shortcuts step) and `b963a3e8b` (chip step, master `a07bd92d8`),
   Reviews r2 and r3 clean.
3. The with-text fixture for fix 2 was **derived** from the idle capture, not measured, so it
   stayed green on a broken reader. The real-CLI test that types a pointer (`C1521`) is
   `[Explicit]`, excluded from rc and Windows-gated, so it never ran
   ([real-CLI note](investigations/2026-11-21-codex-real-cli-tests-on-0170.md), commit
   `4acbd15ec08bf34389e22ef9f88c24d4b4b2e2ab` on `origin/feat/card-task-206053cc`). CARD-2046
   tracks the missing gate.

Order of the whole procedure:

| Step | What | Who runs it |
|---|---|---|
| 0 | Find the new version and what it unlocks | any delegate |
| 1 | Upgrade in a throwaway environment; capture the real screens as fixtures | Investigate/Code delegate |
| 2 | Run the tests first, against the measured captures | Code delegate, checkpoint tool |
| 3 | Land code/test/catalogue changes; cut or fast-forward the rc | orchestrator |
| 4 | Roll the server2 image; upgrade the desktop CLI | orchestrator (rolling phases) |
| 5 | Restart the AppHost onto the rc tip | orchestrator |
| 6 | Verify with real turns | orchestrator, canary tasks |
| 7 | Routing change last: ladder cells, model holds | operator decision, recorded |

## Step 0. Find the new version and what it adds

- npm (Codex): `npm view @openai/codex version` gives `latest`; `npm view @openai/codex time --json`
  gives publish times. The image pins the native `linux-x64` platform tarball
  (`codex-<version>-linux-x64.tgz`) by version and SHA-512; read `dist.integrity` from
  `npm view @openai/codex@<version>-linux-x64 dist.integrity` and convert the base64 to hex for
  `CODEX_SHA512`. The 2026-11-19 note is the worked example
  ([codex-cli 0.170 and GPT-6.2](investigations/2026-11-19-codex-cli-0-170-and-gpt-6-2.md)).
- Vendor changelog (https://github.com/openai/codex/releases): read every release between the
  pinned and the new version for removed flags or config keys Antiphon passes
  (`--no-alt-screen`, `--dangerously-bypass-approvals-and-sandbox`, `--model`,
  `-c model_reasoning_effort`, `-c disable_paste_burst`, `-c developer_instructions`,
  `check_for_update_on_startup`; the launch is in `server/Application/Services/CodexLaunchArgs.cs`
  and [agent-kinds.md §6](agent-kinds.md#6-codex-openai-codex-cli)).
- Which model slugs it unlocks: the first CLI whose bundled catalogue lists a slug is its floor.
  Codex has no unversioned aliases; a slug the CLI does not know is rejected locally and by the
  service ([agent-kinds.md §3](agent-kinds.md#3-model-levels)).

Every repo place that names a model or a CLI version (check all of them in the same change):

| Place | What it holds |
|---|---|
| `server/Application/Services/ModelLevelAliases.cs` | The ladder (`ForCodexEntry`, `ForClaude`, `ForGrok`) and `MinimumCodexCliVersion` floors. The runner uses the shared GPT-6.2 catalog floor at cold launch when the selected launcher has a fresh, matching version observation. |
| `server/Application/Core/ModelAlias.cs` | Slug constants, `DelegatableAliases` (what `-Model` and holds accept), `Normalize`, `CanonicalHoldAlias`. |
| `server/Application/Services/AgentTuiRunnerCatalog.cs` | Curated profile model lists per kind. |
| `GET /api/model-availability` / `scripts/antiphon model-availability` | Live holds (verbs `get`, `hold`, `clear`). |
| `GET /api/complexity-chains` | The role x complexity matrix (cells, not pins). |
| `docs/agent-kinds.md` §3 and §4-6 | Ladder table, floors, per-kind launch notes. |
| `docker/session-runner-grok/Dockerfile` | `CODEX_VERSION` + `CODEX_SHA512`, `CLAUDE_CODE_VERSION` + `CLAUDE_CODE_SHA256`, `GROK_VERSION` + `GROK_SHA256`. |
| `tests/Antiphon.Tests/Infrastructure/CodexRunnerImageContractTests.cs` | Asserts the Codex `Version` and `NpmIntegrity` against the Dockerfile; update both together. |
| `tests/Antiphon.Tests/Infrastructure/GrokRunnerImageContractTests.cs`, `GrokSha256PinTests.cs`, `DindRunnerContractTests.cs`, `DockerStackContractTests.cs` | Grok and Claude pin contracts. |
| Desktop | npm global `codex.cmd` (`server/appsettings.json` Codex `Exe`); Claude and Grok desktop binaries. |

## Step 1. Upgrade in a throwaway environment and capture the real screens

Nothing is rolled out in this step. Use a logged-in **throwaway** `CODEX_HOME` (never the shared
`/state/codex` auth store, never a copy of `~/.codex`): a scratch directory in a scratch
container or on the desktop, signed in for this purpose. An unsigned home only reaches the
sign-in screen, which is useful (capture it) but not enough.

Install the new CLI beside the old one, not over the pinned one:

- Linux: unpack the pinned tarball into a scratch prefix, or `npm install -g @openai/codex@<new> --prefix <scratch>`
  (the form `CodexBootWedgeProbeTests` P3 uses for 0.149.1).
- Desktop: the same `--prefix <scratch>` install; do not replace the global npm package until step 4.

Launch with the production argv at the production size, 120 columns by 30 rows (the
`AgentLaunchOptions` defaults), as the runner user (uid 1654 on server2). The argv used for the
0.170 probe was `--no-alt-screen --dangerously-bypass-approvals-and-sandbox --model <slug>
-c model_reasoning_effort=high -c disable_paste_burst=true -c check_for_update_on_startup=false`.

Capture each of these screens, both the raw ANSI stream and the rendered 30-row grid:

| Screen | Why it matters (0.170 lesson) |
|---|---|
| Idle composer after boot | The ready gate (`CodexStartupScreen.Classify`, `src/Antiphon.Agents.Pty/CodexStartupReadiness.cs`). 0.170 added the shortcuts row here. |
| Composer with short text typed | Pointer evidence (`CodexPointerComposerEvidence.TryReadComposer`). 0.170 changed the last row here. |
| Composer with the boot pointer typed, multi-line and wrapped | The real delivery shape: a `›` row plus indented wrap rows. Use a real `[antiphon-task:…] … YOUR BRIEF IS NOT IN THIS MESSAGE …` pointer of about 530 characters. |
| A warning chip (`⚠ N warnings · f2 to view`) on screen | Where the chip sits moved between 0.160 (footer line) and 0.170 (own row). |
| Trust prompt, sign-in screen | Modal detectors in `Classify`. |
| Update / model-migration prompt | `BlockingUpdate` and numbered pickers. |
| A running turn (`Working (… esc to interrupt)`) and the done frame | Working and done detection. |

Ways to capture, all read-only for production:

- A PTY probe of your own (`script` on Linux), as the 0.170 not-ready note did.
- A live session's raw stream: `GET /api/sessions/<sessionId>/buffer` is the ANSI stream (same
  bytes as `/state/session-runner/<sid-n>.ansi.log` on the runner); the runner's
  `GET :8080/sessions/<sessionId>/snapshot` inside the runner container gives the rendered screen.
  Replay the buffer at 120x30 through the production `TerminalScreen`.

Save the captures under `tests/Antiphon.Agents.Pty.Tests/Fixtures/CodexStartup/` with the version
in the name (`v0170-idle.txt`, `v0170-typed-pointer.txt`, …), and a provenance file in the shape of
`v0160-provenance.md` (capture time, uid, PTY, TERM, size, executable SHA-256, argv, byte
digests, "no trimming or ANSI stripping"). **A fixture derived by editing another capture is not
a measurement.** Label it derived in its comment and keep looking for the measured one; the 0.170
derived with-text grid hid bug 2.

## Step 2. Run the tests first

Run everything below against the new CLI's captures **before** any land or rollout. Each run goes
through the checkpoint tool (`dotnet run --project tools/Antiphon.Checkpoints -- run --plan
<plan.md>`) or, for a one-off, through the build-slot gate (`pwsh -NoProfile -File
scripts/build-slot.ps1 -Label <what> -- <command>`); see
[testing-and-build.md](testing-and-build.md). The 0.170 plans are the template:
`docs/superpowers/plans/2026-11-21-codex-0170-ready-fix-checkpoints.md`,
`…-pointer-fix-checkpoints.md`, `…-chip-fix-checkpoints.md`.

1. **Version-specific readiness and composer classes** (`Antiphon.Agents.Pty.Tests`, Unit, run on
   server2): `CodexStartupReadinessTests`, `CodexV0160StartupTests`, `CodexAccountBootstrapTests`,
   `CodexReadyTrackerTests`, `CodexReadyWaitTests`. Add one method per new measured screen
   (precedent: `Codex_0170_shortcuts_row_under_the_footer_is_ready`,
   `Codex_0170_typed_pointer_with_warning_chip_row_is_complete`), and keep the previous version's
   fixtures green. Commit the new test red first. The CP-1 filter used in Review r3:
   `/*/*/(CodexAccountBootstrapTests*)|(CodexStartupReadinessTests*)|(CodexV0160StartupTests*)|(CodexReadyTrackerTests*)|(CodexReadyWaitTests*)/*`
   (83 tests at `bd34a7ff6`).
2. **Alias and ladder tests** (`Antiphon.Tests`), when a slug or floor changes:
   `CodexGpt62LadderTests`, `ModelAliasTests`, `CodexDelegateDispatchTests`,
   `CodexCliObservationTests`, and `CodexRunnerImageContractTests` for the image pin.
3. **Real-CLI boot canary** against the candidate binary. Run this in an isolated Linux
   image environment with the repository test prerequisites and test Postgres fixture. The
   same method runs on Windows with the modern ConPTY redistributable. It is an ordinary
   Integration test in the rc roster.

   ```powershell
   $env:ANTIPHON_CODEX_EXE = '<absolute path to the candidate codex binary>'
   $env:ANTIPHON_LINUX_COMPILER_POLICY = '0'
   if (-not (Test-Path -LiteralPath $env:ANTIPHON_CODEX_EXE -PathType Leaf)) { throw 'Candidate Codex CLI is missing' }
   & $env:ANTIPHON_CODEX_EXE --version
   if ($LASTEXITCODE -ne 0) { throw 'Candidate Codex CLI did not report its version' }
   # Set ANTIPHON_CODEX_CANARY_MODEL to the candidate slug when changing the ladder.
   pwsh -NoProfile -File scripts/build-slot.ps1 -Label codex-boot-pointer -- dotnet run --project tests/Antiphon.Tests -p:UseAppHost=false -- --treenode-filter "/*/*/CodexBootPointerCanaryTests/Real_Codex_consumes_and_submits_the_boot_pointer" --report-trx
   if ($LASTEXITCODE -ne 0) { throw 'Codex boot pointer canary failed' }
   ```

   Require one passed method in the fresh TRX. Record the version, model and screen evidence
   path printed by the test. The production ready gate opens, the production queue types
   and consumes a formatted boot pointer before its separate Enter, and a matching
   transcript prompt and loopback Responses API request confirm delivery. The stub seeds
   synthetic metadata in a fresh home. On a host without Codex, the roster records
   `Codex CLI unavailable (ANTIPHON_CODEX_EXE or PATH); boot pointer canary skipped`.
   The pre-rollout command requires the candidate executable and a pass.


## Step 3. Land, then cut or fast-forward the rc

Land the fixture, classifier, catalogue and pin changes through Review to master. Product hosts
track the `rc/YYYY-MM-DD` tip, never master
([release-gates.md](release-gates.md)); a master-only land activates nothing. Port the reviewed
commits to the current rc (or cut a new line) under
[orchestration-loop.md](orchestration-loop.md).

Where the code runs: **the classifier (`CodexStartupReadiness`, `CodexPointerComposerEvidence`) and
the ladder run in the server (AppHost)** for every runner. The runner image carries only the CLI.
So a readiness fix needs an AppHost restart (step 5), and a CLI bump needs an image roll (step 4).

## Step 4. Roll the image, then the desktop CLI

The image goes **before** the AppHost restart. A cold Codex launch is refused with HTTP 409
`codex_cli_model_unsupported` when the selected launcher's CLI observation is fresh and
fingerprint-matched, older than the 0.169.1 floor, and the model is exactly `gpt-6.2-sol`
or `gpt-6.2-astra`. Unknown, stale, skewed, unparsable or non-Codex observations fail open
and never refuse, so an unobserved CLI can still receive the new slug and reject it
([0.170 everywhere note](investigations/2026-11-20-codex-0-170-everywhere.md) §4).
`FailureCode` stays null. The message names the model, the floor and the observed version.

Server2, from `C:\src\Antiphon` on the desktop, `$env:ANTIPHON_API` set, one phase per invocation,
`-Sha` the full 40-hex rc tip whose Dockerfile carries the new pin. Never `-Phase all` (the
default), never `-RecycleVolumes`. The gate table with every stop condition is
[docker-stack.md](docker-stack.md#staged-server2-rolling-rollout-card-0934); the worked
command-by-command example is the post-cut activation runbook
(`docs/investigations/2026-11-20-post-cut-activation-runbook.md`, commit
`35ed9b229ed2ab9f44d0542f3156a26097118f39` on `origin/feat/card-task-33896114`).

1. Status: `scripts\antiphon.cmd runner-drain status --runner-id server2` (and
   `server2-temp`), `scripts\antiphon.cmd runner-slots list --runner-id server2`.
2. `pwsh -NoProfile -File scripts/deploy-server2.ps1 -Rolling -Sha <sha> -Phase check-census`
3. `… -Phase check-host-jq` (on `HostJqMissing` only: `-Phase provision-host-jq`, as the runbook says).
4. Cache prep: `pwsh -NoProfile -File scripts/verify-card0849-caches.ps1 -Case Fixture -Sha <sha>`,
   then `-Case Inventory -Sha <sha>`.
5. Record `ssh mc@server2 'docker inspect -f "{{.Id}} {{.State.StartedAt}}" antiphon-runner-session-runner-1'`,
   run `… -Phase deploy-temp`, re-run the inspect: id and start time unchanged.
6. Smoke temp as uid 1654, never root:

   ```powershell
   ssh mc@server2 'c=$(docker ps -q --filter label=com.docker.compose.project=antiphon-runner-temp --filter label=com.docker.compose.service=session-runner); test -n "$c" && docker exec -u 1654:1654 -e HOME=/home/app -e CODEX_HOME=/state/codex "$c" codex --version'
   ```

   Require `codex-cli <new>`. Then a sanctioned Plan canary `--runner server2-temp --no-commit`.
7. `… -Phase drain-old -WaitIdleMinutes 240`. `OldRunnerStillBusy` at 240 minutes is a report, not
   permission; the four-hour cap allows releasing the remaining **server2** seats only
   (`scripts\antiphon.cmd runner-slots release`, see the gate table).
8. `… -Phase redeploy-old` only when server2 `sessions`, `runnerSessions`, `queuedTasks` are zero.
9. Smoke server2 the same way (project `antiphon-runner`), a Plan canary `--runner server2 --no-commit`,
   then `pwsh -NoProfile -File scripts/verify-card0849-caches.ps1 -Case Both -Sha <sha>`.
10. `… -Phase drain-temp -WaitIdleMinutes 240`.
11. `… -Phase retire-temp -DryRun`, then immediately the enabled apply with that preview's token
    (`$env:ANTIPHON_VOLUME_RETIREMENT_APPLY = 'enabled-<operation-id>'`, `-RetirementProof
    <operation-id>`, unset the variable after). Verify with `-Case Retired`.

Keep dispatching through the whole rollout: one of server2 and server2-temp is always accepting
([continuous dispatch](docker-stack.md#continuous-dispatch-during-rollout)).

Gotchas from the 0.170 rollout:

- **A stale NeedsResolution land bound to server2 blocks the `redeploy-old` census**
  (`RecycleLandInFlight`). Cancel the stale owner task; the land sweep then sets the request
  `Canceled` (`task_no_longer_eligible`) and clears the land timestamps, and `redeploy-old` can be
  rerun. Do not request a new land for that task: a Canceled owner refuses
  `recovery_owner_ineligible`, and a new reviewed land would re-block the census. Record:
  `docs/investigations/2026-11-21-stale-needsresolution-land-blocks-redeploy.md` (commit
  `308665b4b`, `origin/feat/card-task-cb7670b0`).
- **`retire-temp` refuses on an unclosed retirement journal.** `deploy-temp` and retire refuse
  while a journal is unresolved; recovery is Resume or `scripts/retire-runner-volumes.ps1 -Close`
  ([docker-stack.md](docker-stack.md), the Close paragraph under the gate table). A receipt-less
  never-Ready preview no longer blocks Close since the fix series ending `328eafa3d`
  (`a57d46776 fix(retirement): exempt receipt-less previews in Close`).
- **`retire-temp` refuses `RecycleGitAuditUnknown … worktree-status status=127`** on a nested
  scratch worktree a review left under the runner's `/tmp`. CARD-2043; diagnosis
  `docs/investigations/2026-11-21-retire-temp-git-audit-127.md` (commit `3f2e4ce61`,
  `origin/feat/card-task-51244fe8`). Under the gate 9 rule this is "any other refusal": one fresh
  preview and apply, then a Debug task.
- **Do not restart the AppHost onto a new ladder before the image has the CLI.**

Desktop CLI (Codex): install while no desktop Codex session is Working. A running session keeps
its old binary until it exits.

```powershell
npm install -g @openai/codex@<new>
codex --version
```

Require `codex-cli <new>` from the PATH shim the runner resolves (`codex.cmd`). Rollback is the
same command with the previous version. A runner restart is not needed for new processes to use
the new files; the version observation (`codex-cli-version-v1`) refreshes on its next probe. If a
restart is wanted, use `pwsh -NoProfile -File scripts/restart-session-runner.ps1` with
`logs/apphost.restart.lock` and `logs/apphost.launch.lock` clear. Wait for Windows
qualification to release the desktop before restarting. Leave `-KillSessions` off.

The first desktop runner restart after the AppHost activates deploy-only SHA
stamping requires `scripts/restart-session-runner.ps1 -Hard` to replace the
resident supervisor. Run `scripts/check-daemon-build.ps1` and confirm the
runner's `/capabilities` field `build.commitSha` equals checkout HEAD.

## Step 5. AppHost restart onto the rc tip

Only after step 4's server2 smoke prints the new version and the desktop `codex --version` does.
From `C:\src\Antiphon`, with HEAD detached at the rc tip (no `git pull`: a pull lands on master):

```powershell
git -C C:\src\Antiphon rev-parse HEAD
pwsh -NoProfile -File scripts/restart-apphost.ps1 -ExpectedServerSha <rc tip>
Invoke-RestMethod "$env:ANTIPHON_API/api/version"
```

Exit 0 only when dashboard, `/health` and `/api/version` agree with that SHA; exit 3 is a
refusal, exit 5 a SHA mismatch with the lock retained ([apphost-runbook.md](apphost-runbook.md)).

## Step 6. Verify with real turns

Two canaries, then one real-work task. Each is a sanctioned spend of one launch.

```powershell
# Ladder: no -Model. High resolves through ForCodex.
scripts/antiphon delegate create --role Custom --kind Codex --level High --runner server2 --no-commit --title '<ver> ladder canary' --goal 'Reply with one line ready. Do not edit files.'
# Explicit slug.
scripts/antiphon delegate create --role Custom --kind Codex --level High --model <new slug> --runner server2 --no-commit --title '<ver> explicit canary' --goal 'Reply with one line ready. Do not edit files.'
```

Then a real-work canary with a checkable answer, for example a goal asking for `git rev-parse HEAD`,
the `git log -1 --format=%s` subject, and one arithmetic result. Compare each answer yourself.

Confirm, for every canary:

- `scripts/antiphon delegate status <taskId>` reaches a report; the task record's
  `requestedModelAlias` is the explicit slug (null for the ladder canary).
- `GET /api/sessions/<sessionId>/transcript?since=0` has a `UserPrompt` (the delivery verdict).
- The model in the Codex rollout file inside the runner container (session files only; do not
  open `auth.json`):

  ```bash
  f=$(find /state/codex/sessions -name 'rollout-*.jsonl' -newer <marker> | tail -1)
  grep -o '"model":"[^"]*"' "$f" | sort -u
  ```

- Validation probes: `-Model <unknown text>` is 422 `model_alias_unknown`; the new slug on the
  wrong kind (for example `-Kind ClaudeCode -Model <codex slug>`) is also 422
  `model_alias_unknown` (`AgentTaskService.ExplicitDelegatableAlias`; a standing-model conflict is
  `model_alias_conflict`).

When a canary fails, read in this order:

1. `GET /api/agents/<agentId>/incidents` (`DeliveryVerificationFailed`, `TranscriptBindFailed`).
2. Runner log (`/state/runner-logs/session-runner-<yyyymmdd>.log` on server2): `Codex auth probe:
   loggedIn=…`, `Phone-home Launch … outcome=started`, then `Phone-home KillGeneration … outcome=killed`
   means the ready gate never saw Ready. See [logs.md](logs.md).
3. The screen itself: `GET /api/sessions/<sessionId>/buffer` replayed at 120x30, or the runner
   snapshot. Compare it with the captured grids in the three 2026-11-21 investigation notes.

## Step 7. Routing change last

Only after step 6 passes on the new CLI everywhere:

- Ladder cells: change `ModelLevelAliases` (and `docs/agent-kinds.md` §3) in a reviewed change. The
  role x complexity matrix is `GET /api/complexity-chains`: edit cells there, not routing pins.
- Model availability: `scripts/antiphon model-availability get`, `hold --kind Codex
  --model <slug> [--until <ISO-8601Z>] [--reason r]`, `clear --kind Codex --model <slug>`. A hold refuses
  only that alias.
- Record every ladder or hold change with the operator's date in `docs/agent-kinds.md`, in the
  shape "operator 2026-11-19".

## Failure layers seen in the 0.170 rollout

| Symptom | Log line / evidence | File | Fix |
|---|---|---|---|
| Task fails `Agent process did not become ready.; stopped by Antiphon (SIGKILL)` at about 120 s; transcript empty | `Phone-home Launch … outcome=started`, then `Phone-home KillGeneration … outcome=killed`; screen ends `← for agents · ? for shortcuts` | `src/Antiphon.Agents.Pty/CodexStartupReadiness.cs` (`TryReadBottomComposer`) | `IsAgentShortcutsRow` step, `fd16cd4ca` |
| Pointer typed, Enter never sent, task fails at 10 min "boot prompt was never delivered" | incident `DeliveryVerificationFailed` `pointer-consumption-unconfirmed … Enter withheld`, 15 s after the write; then `TranscriptBindFailed` `0 cwd-matched` | `src/Antiphon.Agents.Pty/CodexPointerComposerEvidence.cs` (`TryReadComposer`) | shortcuts step `9adbf3ba1`, then chip-row step `b963a3e8b` (`IsWarningChipRow`) |
| Unit lane green, production red | derived with-text fixture in `CodexAccountBootstrapTests` | `tests/Antiphon.Agents.Pty.Tests/CodexAccountBootstrapTests.cs` | measured capture; CARD-2046 real-CLI gate |
| `redeploy-old` refuses `RecycleLandInFlight` | census names a server2-bound pending land | land request of a stale owner | cancel the stale owner, rerun the phase |
| `retire-temp` refuses on journal / `worktree-status status=127` | `C1609_RETIREMENT` / `RecycleGitAuditUnknown` | `scripts/c1008-retirement.sh`, `scripts/c590-remote.sh` | Close fix `328eafa3d` series; CARD-2043 |
| New slug rejected by the CLI | CLI error naming model metadata | CLI is below the model catalog floor | roll the image before the AppHost |

## Rollback

- Image: the previous image is `antiphon-server2/session-testing:<previous sha12>` (no separately
  named rollback tag). While temp still accepts, accepting temp is the live rollback; after a
  failed old smoke, `scripts\antiphon.cmd runner-drain drain --runner-id server2
  --redirect-to server2-temp --reason 'post-upgrade smoke failed'`. Returning to an older SHA runs that
  SHA's own script ([docker-stack.md](docker-stack.md)).
- CLI pin: revert the `CODEX_VERSION`/`CODEX_SHA512` pair (and `CodexRunnerImageContractTests`) in a
  reviewed change; desktop `npm install -g @openai/codex@<previous>`.
- AppHost: detach `C:\src\Antiphon` at the previous rc tip and `restart-apphost.ps1
  -ExpectedServerSha <previous tip>`. Check migrations first: a migration whose `Down` throws
  (for example `20261120091515_AddRequestedModelAlias`) cannot be downgraded.
- Ladder: put the previous slug back in `ModelLevelAliases`; a hold on the new slug is the
  immediate stop without a restart.

## Claude Code

- Pins: `CLAUDE_CODE_VERSION` + `CLAUDE_CODE_SHA256` in `docker/session-runner-grok/Dockerfile`
  (digest from `https://downloads.claude.ai/claude-code-releases/<version>/manifest.json`,
  `platforms["linux-x64"].checksum`). Contract tests: `DindRunnerContractTests`,
  `DockerStackContractTests`, `DockerStackDocumentationTests`, `VerifyDindRunnerScriptTests`.
- Models: `ModelLevelAliases.ForClaude` uses family aliases (`fable`, `opus`, `sonnet`, `haiku`),
  so a new model arrives with a CLI that maps the alias to it; the minimum CLI per alias is
  recorded in [agent-kinds.md §3](agent-kinds.md#3-model-levels). Confirm the resolved model from
  `modelUsage`, as that section did. `DelegatableAliases` and the curated catalogue list the same
  four aliases.
- Launch sets `DISABLE_AUTOUPDATER=1` ([agent-kinds.md §4](agent-kinds.md#4-claude-code)), so the
  desktop CLI changes only when someone installs it.
- Tests in the same order: capture the new screens, then `ClaudeStartupReadinessTests`,
  `ClaudeStartupTrustPromptTests`, `ClaudeScreenTests`, `ClaudeDetectorsTests` (Unit), the headed
  `ClaudeTrustPromptCanaryTests` when the trust dialog changes ([testing-and-build.md](testing-and-build.md)),
  and `ClaudeRealCliStubProxyCanaryTests` (Windows, `ANTIPHON_REAL_CLI_STUB_TESTS=1`, Explicit).
  The `LiveClaude*` startup and delivery audits exist in `Antiphon.Agents.Pty.Tests`. There are no
  version-named Claude capture fixtures like `Fixtures/CodexStartup/` yet.

## Grok

- Pins: `GROK_VERSION` + `GROK_SHA256` in `docker/session-runner-grok/Dockerfile`; contract tests
  `GrokRunnerImageContractTests`, `GrokSha256PinTests`. The Windows Grok CLI auto-updates (it moved
  to 1.0.46 on 2026-10-03 while the image stayed on 1.0.41,
  [agent-kinds.md §5](agent-kinds.md#5-grok-xai-grok-build-tui)), so desktop and image versions can
  differ without anyone upgrading.
- Models: `ModelLevelAliases.ForGrok` returns one id at every tier (`grok-4.7`); the curated list in
  `AgentTuiRunnerCatalog` keeps `grok-4.6`/`grok-4.5`.
- Tests: readiness is `src/Antiphon.Agents.Pty/GrokStartupReadiness.cs`, covered by
  `RunnerGrokAdapterReadyTests`, `RunnerGrokAdapterTrustPromptTests` and
  `GrokStartupReadyOrderingTests` (`Antiphon.Tests`). Real CLI: `GrokRealCliStubProxyCanaryTests`
  (Windows, needs a local Grok login) and the headed `GrokCanaryTests` (`ANTIPHON_HEADED_TESTS=1`,
  Explicit). There is no version-named Grok capture set and no Linux real-CLI gate; the 1.0.41
  dashboard capture is described in agent-kinds §5. Use the same capture-then-test-then-roll order.

## Checklist (paste into a tracking note)

```text
CLI upgrade <kind> <old> -> <new>, target slug(s) <slugs>, operator ask <date>
[ ] 0  npm view / changelog read; floors and slugs recorded; every model place listed
[ ] 1  throwaway home (not /state/codex); new CLI in a scratch prefix; captured at 120x30 as uid 1654:
       idle / text typed / wrapped boot pointer / warning chip / trust / sign-in / update or migration / running / done
[ ] 1  fixtures saved with the version in the name + provenance; any derived grid labelled derived
[ ] 2  red test committed first; readiness+composer classes green on the measured captures (count: __)
[ ] 2  alias/ladder/image-contract classes green (count: __)
[ ] 2  candidate-image real-CLI boot pointer canary passed (version: __, model: __, TRX/screens: __)
[ ] 3  Review clean; landed to master; rc ported/cut; rc tip __
[ ] 4  server2: check-census, check-host-jq, cache prep, deploy-temp (id unchanged), smoke temp uid 1654 = codex-cli <new>,
       drain-old, redeploy-old (zero counters), smoke old + Both, drain-temp, retire-temp preview + apply
[ ] 4  desktop npm install -g @openai/codex@<new>; codex --version = codex-cli <new>
[ ] 5  AppHost restart -ExpectedServerSha <rc tip>; /api/version = rc tip
[ ] 6  ladder canary ok; explicit canary ok; real-work canary answers checked; rollout model slug seen; 422 probes ok
[ ] 7  ladder cells / holds changed and recorded with the operator date (or "none")
Rollback: image tag __, old CLI pin __, previous rc tip __
```
