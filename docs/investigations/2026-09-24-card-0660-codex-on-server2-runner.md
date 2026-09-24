# CARD-0660 — Codex on the server2 phone-home runner (investigation)

Date: 2026-09-24. Stage: Investigate (retry; first attempt stalled and wrote nothing).
Investigator ran inside the server2 runner container (`PhoneHome__RunnerId=server2`, uid `app`), read-only.
Card: "Codex on the server2 phone-home runner (follow-up to default-runner)"; parent CARD-0659 keeps Codex local.

Status: COMPLETE. The mechanism for each question is established from code + measurement; the live end-to-end proof on server2 is gated on operator sign-in (B1). Next: Plan.

## 1. How a Codex session launches and is supervised today (desktop)

Codex is split across three processes exactly like Grok and Claude; the parts that matter for server2:

| Layer | Codex behaviour | Windows/desktop assumption? |
|---|---|---|
| Agent definition | `server/appsettings.json:82-86` — `Exe: codex.cmd`, `ArgsTemplate: ["--no-alt-screen", "--dangerously-bypass-approvals-and-sandbox"]` | **Yes** — `codex.cmd` is the npm Windows shim. |
| Launch args | `AgentTaskDispatcher.ComposeDelegateArgs` (`server/Application/Services/AgentTaskDispatcher.cs:5066-5150`): no `--name`; `-c model_reasoning_effort=<tier>`, `-c disable_paste_burst=true`; rules as ONE argv element `-c developer_instructions=<bundle>` (`CodexLaunchArgs.cs:40`), bounded by `InstructionBundleComposer.EnsureWithinCommandLineBudget` (30,000-unit budget) | No — argv only. Linux limit is `MAX_ARG_STRLEN` = 131,072 bytes per argv element; a ≤30,000-UTF-16-unit bundle is ≤90 KB UTF-8 worst case, so it fits (Grok needed a rules *file* on Linux, Codex does not). |
| Runner shim rewrite | `CodexWindowsLaunchPolicy.Apply` (`src/Antiphon.SessionRunner/CodexWindowsLaunchPolicy.cs:39-41`) rewrites `codex.cmd` → `node.exe codex.js` and enforces the CRT command-line ceiling | Windows-only by construction: `if (!OperatingSystem.IsWindows() ...) return request;` — a no-op on Linux, nothing to port. |
| Server adapter | `RunnerCodexAdapter` (`server/Infrastructure/Agents/SessionRunner/RunnerCodexAdapter.cs`) — talks only to `ISessionRunnerClient`: snapshot/screen for readiness, `GetTranscriptAsync` for submit confirmation and turn end | **None.** It is transport-agnostic; the phone-home client implements the same interface, which is how `RunnerGrokAdapter`/Claude already work against server2. |
| Readiness | `CodexReadyWait.WaitAsync` over `CodexStartupReadiness` (`src/Antiphon.Agents.Pty/CodexStartupReadiness.cs`) — positive gate on the rendered composer, MCP-boot status threshold, trust/update prompt handling, all from screen snapshots | Screen-text only; no path or OS logic. Needs one Linux capture to confirm the first-run screens (trust dir prompt, update nag, login screen) render the same through the Linux pty host. |
| Submit + done | `CodexSubmitConfirmation` requires a `UserPrompt` transcript row past the baseline; done = `TurnEnd` (`event_msg/task_complete`) row, screen tracker fallback | None beyond needing the transcript. |
| Rollout discovery | Runs **inside the session runner** (`SessionRunnerRuntime.cs:2093-2115`), `CodexTranscriptTailer` over `ResolveSessionsRoot(request.Env)` = `$CODEX_HOME/sessions` from the *launch* env, else the runner's own env, else `~/.codex/sessions` (`CodexTranscriptTailer.cs:170-181`). Binds only with C1 claim + C2 exact `session_meta.cwd` + C3 start time + C4 delivered-input match. | C2 compares case-sensitively on Linux (`CodexTranscriptTailer.cs:629`), which is correct. Nothing Windows-specific. |

Conclusion for (1): the runner binary that server2 already runs contains the whole Codex runtime (tailer, normalizer, rollout probe, sidecar re-adopt, `SupportedTranscriptFormats` already lists `codex` — `SessionRunnerRuntime.cs:25-26`, `:726`). The Windows-only pieces are the `codex.cmd` definition and the `CodexWindowsLaunchPolicy` rewrite, which already self-disables on Linux. What blocks Codex on server2 is admission, the image and auth — not the supervision code.

## 5a. Where Codex is refused today (dispatch path)

Three explicit gates, all intentional (CARD-0628 D-5, CARD-0659):

1. Server: `PhoneHomeLaunchPolicy.IsAdmittedKind` = `Grok or ClaudeCode` (`server/Application/Services/PhoneHomeLaunchPolicy.cs:46`); `RefuseUnsupportedStart` throws `phone_home_kind_refused` for a runner-bound task of any other kind (`:100-101`).
2. Server projection: `ProjectExe` maps only `grok(.exe)` / `claude(.exe)` or an exact `RawExeAllowList` entry (`:188-210`); `codex.cmd` falls through to `phone_home_wrapper_refused`. The projected env sets `GROK_HOME` and `CLAUDE_CONFIG_DIR` only (`:162-166`) — no `CODEX_HOME`.
3. Runner: `PhoneHomeCommandDispatcher.RejectUnsupportedLaunch` admits exe `grok`/`/usr/local/bin/grok`, `claude`/`/usr/local/bin/claude`, or the raw allow list (`src/Antiphon.SessionRunner/PhoneHomeCommandDispatcher.cs:321-331`) and refuses any `TranscriptFormat` other than Grok/Claude (`:365-370`, comment: "Anything else (codex) has no tailer here" — stale: the tailer is present, the image has no codex).
4. Auth probes: `ProviderAuthAsync` knows only `claude` and `grok` (`:234-256`); `RejectSignedOut{Claude,Grok}Async` are the per-launch backstops (`:264-296`).

## 2. What the runner image needs

Measured inside the server2 runner container (image `antiphon-server2/session-testing`, Debian 12 bookworm, uid 1654 `app`, 24 cores):

- **No codex anywhere today.** `which codex` → nothing; no `/state/codex`, no `/home/app/.codex`. `claude 2.1.280` and `grok 1.0.40` are the only agent CLIs (`docker/session-runner-grok/Dockerfile:22-48`). The `session-testing` stage copies Node 22 from `node:22-bookworm` (`Dockerfile:111`); measured `node v22.23.3`, `npm 10.9.9`. **The default `runtime` stage has no Node** — relevant only if Codex were installed through npm's `codex.js` shim, which it should not be (below).
- **Pin target.** `npm view @openai/codex` on 2026-09-24: `latest = 0.156.1`, `linux-x64 = 0.156.1-linux-x64` — the same version the desktop is measured on (`docs/agent-kinds.md` §6, `CodexLaunchArgs.cs:49-58`, re-read 2026-09-23 on 0.156.1). Keeping desktop and runner on one version keeps the readiness fixtures valid for both.
- **Artifact shape (measured, scratchpad install, not the image).** `@openai/codex@0.156.1` is a 1-file JS shim (`bin/codex.js`) plus an optional dependency `@openai/codex-linux-x64` (370 MB unpacked) holding `vendor/x86_64-unknown-linux-musl/bin/codex` (**statically linked** — `ldd` → "statically linked"), `codex-code-mode-host`, `codex-resources/bwrap` and `codex-path/rg`. `codex --version` → `codex-cli 0.156.1`. So Codex needs **no Linux runtime deps and no Node** when the native binary is executed directly — the same pattern as the pinned native `claude` (`Dockerfile:44-48`).
- **Recommended install** (for the Plan, not done): a `CODEX_VERSION` + `CODEX_SHA256` ARG pair in `runtime-base`, fetching the pinned platform tarball (`npm pack @openai/codex@<v>-linux-x64` or the GitHub release asset `codex-x86_64-unknown-linux-musl.tar.gz`), `sha256sum -c`, install the whole `vendor/x86_64-unknown-linux-musl/` tree under `/opt/codex/<v>/` and symlink `/usr/local/bin/codex` → its `bin/codex` (the binary resolves `bin/codex-code-mode-host`, `codex-path/rg`, `codex-resources/bwrap` through `codex-package.json` in its package root — strings in the 0.156.1 binary — so the whole tree must be installed, not `bin/codex` alone), and a build-time `codex --version | grep -F <v>` with a throwaway `CODEX_HOME`. Which upstream artifact carries a published checksum is **not verified here** — the Plan must pick one that does, or pin the npm tarball's `dist.integrity`.
- **Auto-update.** `codex update` exists, and the TUI shows a "Press enter to continue" update nag (`CodexStartupReadiness.cs:267-268` treats it as `BlockingUpdate`). The image copy is root-owned and read-only to uid 1654, so it cannot self-update; the nag must be suppressed via config (`check_for_update_on_startup = false` in the runner-owned `config.toml`; the key is present in the 0.156.1 binary's `ConfigToml` strings, its effect on the nag is unmeasured) or it will block readiness exactly as on the desktop.
- **Sandboxing.** The desktop launches with `--dangerously-bypass-approvals-and-sandbox` (`server/appsettings.json:85`), so Codex's own Linux sandbox (landlock/seccomp + bundled `bwrap`) is not used; containment is the container itself, the same boundary Grok (`--always-approve`) and Claude (`--dangerously-skip-permissions`) already run under on server2. Kernel is 4.15 (landlock needs ≥5.13), so enabling Codex's sandbox would not work here anyway — keep the bypass flag. No `Sandbox` screen (`ContainsSandboxOrInputDisabled`) is expected with the bypass.
- **`CODEX_HOME` must not live under `/tmp`.** Measured: with `CODEX_HOME` under the scratchpad (`/tmp/...`) every invocation printed `WARNING: proceeding, even though we could not create PATH aliases: Refusing to create helper binaries under temporary dir "/tmp"`. `/state/codex` on the `runner-state` volume avoids it, and it has to be there anyway so auth survives container replacement.
- **Per-home state.** A first launch into an empty `CODEX_HOME` created `config.toml`, `installation_id`, `.tmp/`, `skills/`, and SQLite stores `state_5`, `logs_2`, `goals_1`, `memories_1`, `queue_1` (~2.5 MB) before any sign-in. Concurrent sessions share these on the desktop already (one `~/.codex`); the runner at capacity 2 is no worse.
- **Disk.** `/state` and `/work` share one host LV at **92% used, 18 GB free** (`df -h`). +370 MB per image layer is fine; rollouts on `/state` grow with use and need the same retention the desktop lacks (operator note, not a blocker).

## 3. Headless Codex auth and custody

Measured on codex-cli 0.156.1 (`codex login --help`, isolated scratchpad `CODEX_HOME`, nothing signed in):

| Mechanism | Measured | Fit for server2 |
|---|---|---|
| `codex login` (browser OAuth) | default in the TUI ("1. Sign in with ChatGPT") | Needs a browser callback to `localhost` on the runner — not usable in a phone-home container without port forwarding. |
| `codex login --device-auth` | flag present; the TUI also offers "2. Sign in with Device Code — Sign in from another device with a one-time code" | **Recommended.** Same shape as Grok's `grok login` (CARD-0604 D-16): the operator runs it once, interactively, in the running container as uid 1654 with `CODEX_HOME=/state/codex`. `auth.json` lands on the `runner-state` volume and survives redeploys. **Unverified:** whether the operator's ChatGPT account/workspace must first enable device-code sign-in in ChatGPT security settings. The Plan's first operator step finds out. |
| `codex login --with-api-key` (stdin) / `OPENAI_API_KEY` / `CODEX_API_KEY` env | present | **Refuse**, for the same reason `ANTHROPIC_API_KEY` is refused on server2 (`docs/agent-credentials.md` §5, CARD-0628): it moves turns onto metered billing. |
| `codex login --with-access-token` (stdin) / `CODEX_ACCESS_TOKEN` env | present. Binary strings tie it to "Agent Identity authentication" (`CODEX_ACCESS_TOKEN is required when --use-agent-identity-auth is set`) and list it among "auth is provided by environment" sources | Could become the `claude setup-token` analogue (a vault item mounted as a file, exported by the entrypoint). **Semantics, issuance and billing are unverified.** Do not design on it until measured. |
| Copying the desktop `~/.codex/auth.json` | — | **Forbidden** by the operator rule (AGENTS.md "Do not expose ... a user's Codex home"; `docs/agent-kinds.md` §6 "Do not copy the user's normal `auth.json` programmatically"). It is also operationally unsafe: ChatGPT auth refreshes by rotating tokens, so two machines sharing one `auth.json` would invalidate each other's refresh token. That is general OAuth refresh-rotation behaviour and was not measured here. |

Custody comparison:

- **Claude** (CARD-0628): primary is a `claude setup-token` file mounted read-only and exported by `dind-entrypoint.sh`; fallback is an interactive login store at `/state/claude`. The server refuses credential env names on runner-bound launches (`PhoneHomeLaunchPolicy.cs:153-159`).
- **Grok** (CARD-0604 D-16, CARD-0647): an interactive `grok login` into `/state/grok/auth.json`. The runner probe checks the file's *presence* only and never opens it.
- **Codex (proposed):** the Grok pattern. The store is `/state/codex` (`init-state.sh` creates it 0700 uid 1654). The operator runs `docker exec -it -u 1654:1654 -e CODEX_HOME=/state/codex <runner> codex login --device-auth` once. The server projects `CODEX_HOME=/state/codex` onto every runner-bound launch, alongside `GROK_HOME`/`CLAUDE_CONFIG_DIR` (`PhoneHomeLaunchPolicy.cs:162-166`). The compose file sets `CODEX_HOME` on the runner process too, so the tailer's fallback resolves the same root (`CodexTranscriptTailer.cs:170-181`). The runner-bound projection refuses `OPENAI_API_KEY`, `CODEX_API_KEY` and `CODEX_ACCESS_TOKEN` in the launch env (`phone_home_env_refused`), mirroring the Claude refusal. This is a separate identity from the desktop's home by construction, so runner rollouts never enter the user's Codex Desktop thread list. That is the same isolation argument the headed tests use (`docs/agent-kinds.md` §6 "Headed-test home").
- **Signed-out behaviour today would waste 60 s and mislabel the cause.** The measured 0.156.1 signed-out first screen is: "Welcome to Codex … Sign in with ChatGPT to use Codex as part of your paid plan or connect an API key … 1. Sign in with ChatGPT / 2. Sign in with Device Code / 3. Provide your own API key … Press enter to continue". `CodexStartupScreen.ContainsSignIn` matches only "Please sign in", "Sign in to continue" and "sign in to" (`CodexStartupReadiness.cs:257-260`), and none of those appears on this screen. Classification falls through to `ContainsBlockingUpdate` ("Press enter to continue", `:267-268`), so the ready wait runs to `CodexReadyMaxWaitMs` (60 s) and logs `reason=BlockingUpdate` rather than `SignIn`. This was reconstructed by applying the code to the captured text, not run through the classifier. It is harmless because `CodexReadyWait` writes Enter only for the trust prompt (`:436-463`), so it never picks a login option. But it is why a pre-launch auth probe is needed (§5), and the `SignIn` detector wants the new wording added (fixture: the capture above).

## 2b. Linux screen evidence for readiness (measured, 0.156.1, scratchpad only)

These captures come from the native binary under `script` (120×40 pty, `TERM=xterm-256color`), each with its own empty scratchpad `CODEX_HOME`. To get past sign-in without any credential, the runs used a dummy `OPENAI_API_KEY` against an unreachable stub provider (`-c model_provider=stub … base_url=http://127.0.0.1:9/v1`, the `docs/agent-kinds.md` §6 stub recipe). No OpenAI request could leave the box. The ANSI-stripped captures are in `docs/investigations/evidence/card-0660/`.

- **Ready screen markers exist on Linux** (`0156-linux-ready-root-trusted.txt`): the banner is `>_ OpenAI Codex (v0.156.1)`, then `model: GPT-6-Sol /model to change`, `directory: …`, `permissions: YOLO mode`, the composer `› Ask Codex to do anything` and the footer `GPT-6-Sol default · <cwd>`. These are the markers `CodexStartupScreen.Classify` gates on (`CodexStartupReadiness.cs:45-104`: banner, selected model, idle composer hint, `model effort · cwd` footer). Whether the runner's own VT renderer produces a `Ready` classification was **not run**. That is the live proof slice.
- **The 0.156.1 trust modal is not recognised by our detector.** On a fresh home, a linked git worktree shows `Folder access <cwd> — Note: You're in a subdirectory of a Git project. Trusting will apply to the repository root: <main repo> — Trust this folder? … › 1. Trust and continue 2. Quit — enter continue · esc quit` (`0156-linux-worktree-trust-prompt.txt`). `CodexTrustPromptDetector` requires `doyoutrustthecontentsofthisdirectory` **and** `yes,continue` (`src/Antiphon.Agents.Pty/CodexDetectors.cs:282-308`), and the 0.156.1 binary does not contain the string `Do you trust the contents` at all. So the ready wait will neither auto-accept this modal nor classify it as `Trust`, and it times out at 60 s. The desktop presumably never meets this modal because the operator's `~/.codex/config.toml` already trusts the repo root; that config was not inspected, by rule. On a fresh `/state/codex`, **every** launch would hit it. This is a latent desktop defect too, for any untrusted cwd. Filing a card for it is recommended.
- **Trust is keyed on the git repository root, and only the config *file* satisfies it.** Measured on a scratch repo `trepo` with a linked worktree `twt`, launching in `twt`:

  | CODEX_HOME seed | trust modal? |
  |---|---|
  | none | yes |
  | `-c 'projects."<twt>".trust_level="trusted"'` on argv | yes |
  | `-c 'projects."<trepo>".trust_level="trusted"'` on argv | yes |
  | `config.toml` `[projects."<trepo>"] trust_level = "trusted"` | **no** |
  | `config.toml` `[projects."<twt>"] trust_level = "trusted"` | **no** |

  Runner worktrees are linked worktrees of `/work/repos/antiphon` (`git rev-parse --git-common-dir` → `/work/repos/antiphon/.git`, measured in this task's worktree). One seeded `[projects."/work/repos/antiphon"] trust_level = "trusted"` in `/state/codex/config.toml` therefore covers every runner worktree. A per-launch `-c` does not.
- **Signed-out screen** (`0156-linux-signed-out.txt`): see §3. It is mis-classified as `BlockingUpdate`.
- `-c check_for_update_on_startup=false` produced no update nag in these runs. With no newer version published the nag would not show anyway, so this is not proof the key suppresses it.

## 4. Transcript / rollout discovery over phone-home

Nothing new is needed in the transport. Discovery runs where the process runs:

- The phone-home runner is the same `SessionRunnerRuntime`. For `TranscriptFormat = codex` it starts `CodexTranscriptTailer` in-process (`SessionRunnerRuntime.cs:2093-2115`) against `ResolveSessionsRoot(request.Env)`. The server-projected `CODEX_HOME=/state/codex` makes that `/state/codex/sessions` (`CodexTranscriptTailer.cs:170-181`).
- The server reads transcript rows over the existing phone-home `Transcript` operation (`PhoneHomeCommandDispatcher.cs:93`). `RunnerCodexAdapter` only ever calls `ISessionRunnerClient.GetTranscriptAsync` (`RunnerCodexAdapter.cs:283-300`), the same interface the phone-home client implements for Grok and Claude.
- The C1–C4 bind rules hold on the runner: C2 is an exact, case-sensitive `session_meta.cwd` match on Linux (`CodexTranscriptTailer.cs:623-631`). Runner worktrees are distinct `/work/worktrees/<name>` dirs, so two concurrent Codex sessions at capacity 2 cannot cross-bind. C4 uses the runner's own `SessionInputLog`, which phone-home input feeds because input goes through the same runtime.
- Sidecar re-adopt after a runner restart (`SessionRunnerRuntime.cs:2669-2700`, the Codex branch at `:2685`) uses the recorded rollout path under `/state`. `/state/session-runner` is on the persistent volume, so it survives a container replacement, as it already does for Grok and Claude.
- Server-side code that reads a *local* `.codex` is limited to `OrchestratorWorkspaceLayout` (`server/Application/Services/OrchestratorWorkspaceLayout.cs:220,512`), and only for orchestrator workspaces. Orchestrators are not runner-eligible (CARD-0659 plan), so it is not on the path.
- Kind-agnostic runner issues apply to Codex exactly as they do to Grok/Claude: CARD-0657 (runner-bound progress read from the desktop worktree), CARD-0653 (capacity leak on finished sessions) and CARD-0649 (attribution). None is Codex-specific.

## 5b. Dispatch changes needed (exe admission, auth probe, capacity, routing)

| Point | Today | Needed |
|---|---|---|
| Server kind admission | `IsAdmittedKind` = Grok/ClaudeCode (`PhoneHomeLaunchPolicy.cs:46`) | add `Codex`; the `phone_home_kind_refused` messages at `:101`, `:117` follow. |
| Server exe projection | `ProjectExe` maps `grok(.exe)` / `claude(.exe)` only (`:188-210`) | map file name `codex`, `codex.cmd`, `codex.exe` → bare `codex` (non-pinned only, like Claude). A profile whose exe is `node.exe … codex.js` does not arise: the `node.exe codex.js` rewrite happens runner-side and on Windows only (`CodexWindowsLaunchPolicy.cs:39-41`). |
| Server env projection | sets `GROK_HOME`, `CLAUDE_CONFIG_DIR`, `ANTIPHON_API` (`:162-166`) | add `CODEX_HOME = PhoneHomeRunner:ChildCodexHome` (default `/state/codex`, POSIX-absolute validation like `ChildGrokHome` in `PhoneHomeRunnerSettings.cs:75-78`). Refuse `OPENAI_API_KEY`, `CODEX_API_KEY` and `CODEX_ACCESS_TOKEN` in a runner-bound Codex launch env, extending the Claude refusal loop at `:153-159`. |
| Runner exe admission | `IsGrokExe` / `IsClaudeExe`, exact bare name or image path (`PhoneHomeCommandDispatcher.cs:302-319`) | `IsCodexExe`: `codex` or `/usr/local/bin/codex` exactly, with the same traversal-refusal tests as CARD-0640. |
| Runner transcript-format admission | Grok/Claude only (`:365-370`) | admit `codex`. The tailer already exists (`SessionRunnerRuntime.cs:25-26`). |
| Runner auth probe | `ProviderAuthAsync` knows `claude`, `grok` (`:234-256`) | a `CodexAuthProbe` shaped like `GrokAuthProbe` (`src/Antiphon.SessionRunner/GrokAuthProbe.cs`): **presence of `CODEX_HOME/auth.json` only, never opened**, with a `PhoneHome:CodexHome` setting (compose `PhoneHome__CodexHome=/state/codex`). Add `RejectSignedOutCodexAsync` as the launch backstop. `codex login status` also works as a probe (exit 1 + "Not logged in" when signed out, measured). But it prints a login summary when signed in and costs a process spawn, so file presence is the safer choice under the "never print a secret" rule. **Unverified:** that ChatGPT sign-in writes `auth.json` specifically. The CLI's own strings name `auth.json` next to `OPENAI_API_KEY`/`CODEX_API_KEY`/`CODEX_ACCESS_TOKEN` as its credential sources; confirm after the operator's login. |
| Server pre-flight | `ReadClaudeProviderAuthBeforeClaimAsync` / `TryFailClaudeCredentialProbeAsync` and the Grok pair in `AgentTaskDispatcher.cs:~2840-2990`; create-time Grok refusal `AgentTaskService.cs:3490-3515` | a Codex pair with an `episodeKey` of `codex-home:{runner}:{home}` and a remedy line `docker exec -it -u 1654:1654 -e HOME=/home/app -e CODEX_HOME=/state/codex antiphon-runner-session-runner-1 codex login --device-auth`. It fails `AuthenticationRequired` before a worktree is cut. |
| Capacity | `PhoneHome__Capacity=2` (compose), server bound `PhoneHomeRunner:MaxCapacity` 8 | no Codex-specific change; Codex shares the runner's slots. The CARD-0653 leak and CARD-0654 per-host budgets apply unchanged. A native Codex process is light next to the 24 cores. |
| Default routing (CARD-0659) | the plan keeps Codex out of default placement, with guards so that "Codex must never launch remotely", including reroute/rewalk (`docs/.../2026-09-24-card-0659-default-runner-plan.md` lines 13, 45, 127-129, 195, 249) | after Codex is admitted and proven, drop Codex from the exclusion set and turn the "Codex never remote" reroute guards into the ordinary kind-admission check (`IsAdmittedKind`). Sequenced **after** CARD-0659 lands, so this is a small follow-on diff rather than a fork of its plan. |
| Readiness detector | trust detector matches only the pre-0.156 wording; the sign-in detector misses the 0.156 wording (§2b, §3) | recognise `Trust this folder?` + `Trust and continue` as `Trust`, and `Sign in with ChatGPT` / `Sign in with Device Code` as `SignIn`, with fixtures from `docs/investigations/evidence/card-0660/`. Also pre-seed trust (below) so the runner never depends on auto-accepting a modal. |

## Recommended design

1. **Image.** Pin codex-cli `0.156.1` (the desktop's measured version) native linux-x64 musl in `runtime-base`: a version + SHA-256 ARG pair, the whole `vendor/x86_64-unknown-linux-musl` tree under `/opt/codex/<v>`, and `/usr/local/bin/codex` as a symlink to it, with a build-time `--version` check. No Node dependency.
2. **State.** `init-state.sh` creates `/state/codex` (0700, uid 1654) and writes a **runner-owned, non-secret** `/state/codex/config.toml` only when it is absent: `[projects."/work/repos/antiphon"] trust_level = "trusted"` and `check_for_update_on_startup = false`. Compose sets `CODEX_HOME=/state/codex` and `PhoneHome__CodexHome=/state/codex`, and the server `PhoneHomeRunner:ChildCodexHome=/state/codex`: one store in three places, as CARD-0628 D-6 did for Claude.
3. **Auth.** The operator runs `codex login --device-auth` once, interactively, in the running container as uid 1654, with `CODEX_HOME=/state/codex`. Nothing is copied from the desktop, nothing goes in the vault or image, and no API key is used. `docs/agent-credentials.md` gets a Codex row in the server2 table.
4. **Admission.** Add Codex to the server kind, exe and env projection and to the runner exe and format admission, with the `CodexAuthProbe` presence probe plus the server pre-flight and runner backstop.
5. **Readiness.** Update the trust and sign-in detectors for the 0.156 wording (a desktop fix as well).
6. **Proof on server2** (the card's acceptance): one runner-bound Codex Worker task → `Running` → transcript-confirmed `UserPrompt` → `TurnEnd` with the report text; then a relaunch/re-adopt check.
7. **Routing.** After CARD-0659 lands and the proof passes, admit Codex to default runner placement.

## Operator blockers

- **B1 — Codex sign-in on server2.** `codex login --device-auth` must be run interactively by the operator, since an agent must not log in. Before that, check whether the ChatGPT account/workspace requires device-code sign-in to be enabled in ChatGPT security settings (unverified here).
- **B2 — Which account.** The runner's Codex draws on whichever ChatGPT plan is signed in. If that is the operator's own account, desktop and server2 Codex share one rate-limit pool, and the Codex subscription-quota notices then describe a shared pool. Decide: same account, or a separate seat.
- **B3 — Redeploy.** The image change needs a server2 redeploy (deploy-parent / `scripts/c590-real.ps1`) and the server change a desktop restart. Both are operator-gated.
- **B4 — Disk** (non-blocking): `/state` and `/work` share a host volume at 92% used (18 GB free). Codex adds ~370 MB of image, plus rollouts that grow on `/state`.

## Slice outline (for the Plan)

- **S1 Detectors (desktop-safe, no runner):** trust and sign-in wording for 0.156 in `CodexDetectors.cs` / `CodexStartupReadiness.cs`, with the three captures as fixtures, red first against today's detector.
- **S2 Image + state:** Dockerfile codex pin, `init-state.sh` `/state/codex` + seeded `config.toml`, compose env (`CODEX_HOME`, `PhoneHome__CodexHome`), and an image test that `codex --version` equals the pin and that the default `runtime` stage carries it.
- **S3 Runner admission + probe:** `IsCodexExe`, the transcript-format admission, `CodexAuthProbe`, `RejectSignedOutCodexAsync` and the `ProviderAuthAsync` provider list; the dispatcher tests mirror the CARD-0640/0647 Grok ones.
- **S4 Server projection + pre-flight:** `IsAdmittedKind`, `ProjectExe`, `CODEX_HOME` projection, `ChildCodexHome` setting, credential-env refusal, Codex pre-flight and create-time refusal, and relaunch projection parity (CARD-0640 pattern).
- **S5 Docs:** `docs/agent-credentials.md` server2 Codex row, `docs/agent-kinds.md` §6 runner note.
- **S6 Operator + proof on server2:** B1–B3, then one real runner-bound Codex task end-to-end (evidence: the transcript `UserPrompt` + `TurnEnd` rows and the task report).
- **S7 Routing:** remove the Codex exclusion from CARD-0659's default-runner policy (depends on CARD-0659 landed + S6).

## Remaining uncertainties

- The runner's VT-rendered 0.156.1 ready screen was not classified by `CodexStartupScreen.Classify`. Only the text markers were measured (S6 settles it).
- Whether `-c check_for_update_on_startup=false` / the config key suppresses the update nag (no newer release to trigger it today).
- The device-code prerequisites on the operator's ChatGPT account, and the exact credential file name ChatGPT login writes (expected `auth.json`).
- `CODEX_ACCESS_TOKEN` / `--with-access-token` semantics (Agent Identity?) and billing: a possible non-interactive analogue of `claude setup-token`, not designed on.
- Whether the desktop's `~/.codex/config.toml` is what hides the trust-detector gap on the desktop. It was not inspected, by rule.
- Which upstream codex artifact publishes a checksum to pin against.

## Not done, noted

- Fix idea: file a card for the stale `CodexTrustPromptDetector` / `ContainsSignIn` wording. It affects any untrusted cwd on the desktop too, and slice S1 covers it.
- No code, image, deploy or login changes were made. Scratchpad installs and captures only.

