# CARD-0628 — Claude Code CLI on server2's persistent runner

Investigate, 2026-09-23. Evidence only; no fix designed or implemented.

## Verdict

**Confirmed: nothing about a Claude Code dispatch to server2 works today, and the reason is not the
image — it is five explicit `kind != AgentKind.Grok` refusals on the dispatch path.** The image gap
(no `claude` binary) is real but is the smaller half. Authentication has a clean, custody-clean
answer that mirrors the Grok precedent exactly: an interactive `claude auth login` inside the
persistent container, storing a refreshable credential on the `runner-state` volume. No secret needs
to enter Antiphon's stores, an environment value, an argv, or a Compose secret.

Two findings are security-relevant beyond the card's own ask and are recorded in §5.

## 1. How Claude Code CLI authenticates without a browser (Q1)

Measured against the desktop's own binary, `C:\Users\lndco\.local\bin\claude`, version 2.1.266
(`claude --version`), by reading its `--help`/subcommand help and by string-grepping the binary
(the CARD-0353-era technique — the binary is the most current documentation of its own auth).

### The credential sources, in the CLI's own precedence order

Extracted from the binary's own resolver (`Jl()` / `Gp()`); the labels are its strings verbatim:

| # | Source | Evidence |
|---|---|---|
| 1 | `ANTHROPIC_API_KEY` | `" from ANTHROPIC_API_KEY"` |
| 2 | `apiKeyHelper` setting (a command that prints the key; TTL via `CLAUDE_CODE_API_KEY_HELPER_TTL_MS`) | `" from apiKeyHelper"`, `"Found CLAUDE_CODE_API_KEY_HELPER_TTL_MS env"` |
| 3 | the saved `/login` API key | `" from the saved /login API key"` |
| 4 | `ANTHROPIC_AUTH_TOKEN` | `" from ANTHROPIC_AUTH_TOKEN"` |
| 5 | `CLAUDE_CODE_OAUTH_TOKEN` | `" from CLAUDE_CODE_OAUTH_TOKEN"` |
| 6 | `CLAUDE_CODE_OAUTH_TOKEN_FILE_DESCRIPTOR` | `" from the OAuth token file descriptor"` |
| 7 | `CCR_OAUTH_TOKEN_FILE` (well-known path, e.g. `/run/ccr/session_token`) | `" from the OAuth token file"` |
| 8 | the saved claude.ai login | `" from the saved claude.ai login"` |

`--bare` is the explicit statement that a non-interactive path exists:

> `Anthropic auth is strictly ANTHROPIC_API_KEY or apiKeyHelper via --settings (OAuth and keychain
> are never read).`

### The four candidate paths, and what each actually costs

**A. `claude auth login` interactively inside the container — recommended.**
`claude auth --help` → `login | logout | status`. `claude auth login --help` → `--claudeai`
(default, Claude subscription) / `--console` (Anthropic Console, API usage billing) / `--sso`.
The flow is browser-based **but has a documented manual fallback for a machine with no browser**:

- `"Browser didn't open? Use the url below to sign in"`
- `"Paste code here if prompted >"`
- `"Ask the user to paste the full redirect URL from their browser's address bar, including the
  ?code=...&state=... query string."`

So `docker exec -it -u 1654:1654 <container> claude auth login` prints a URL, the operator opens it
in the desktop browser, and pastes the redirect URL back. That is byte-for-byte the shape of the
existing Grok provisioning step (`docker exec -it -u 1654:1654 <container> grok login`,
docs/agent-credentials.md §5).

It yields a **full-scope, refreshable** credential (see D below for why that matters), persisted to
`<CLAUDE_CONFIG_DIR>/.credentials.json` (9 occurrences of `.credentials.json` in the binary; the
only `secret-tool` / `kwallet-query` strings are an allow-list of *git/docker* credential helpers,
not Claude's own store — there is no libsecret dependency to satisfy in the container).

**B. `claude setup-token` → `CLAUDE_CODE_OAUTH_TOKEN` — works, with two documented limits.**
`claude setup-token --help`: *"Set up a long-lived authentication token (requires Claude
subscription)"*. The binary's own strings: `"Use this token by setting: export
CLAUDE_CODE_OAUTH_TOKEN=<token>"` and `"Store this token securely. You won't be able to see it
again."`

- **Inference-only scope.** `"token has no scope accepted by /api/oauth/validate (needs
  user:profile, user:office, or user:ccr_inference; env-var and setup-token sessions default to
  user:inference only)"`, and `"Remote Control requires a full-scope login token. Long-lived tokens
  (from claude setup-token or CLAUDE_CODE_OAUTH_TOKEN) are limited to inference-only for security
  reasons. Run claude auth login to use Remote Control."`
- **No self-refresh.** `"OAuth 401: keeping the user-supplied CLAUDE_CODE_OAUTH_TOKEN instead of
  adopting the stored credential. Mint a fresh token with claude setup-token and restart with it"` —
  i.e. expiry is a manual re-mint, and a re-mint needs a redeploy or an env edit.

Also: setup-token itself is interactive and browser-based, so it does not remove the one-time
interactive step — it only moves the secret into an environment value, which is strictly worse
custody than A.

**C. `ANTHROPIC_API_KEY` — technically the easiest, and the one to refuse by default.**
This is the exact trap docs/agent-credentials.md §1 already names for Grok: *"it moves pool spend
from the SuperGrok subscription onto console.x.ai metered billing without anyone choosing that. It
is an opt-in, not the default."* The Claude equivalent moves every server2 Opus turn from the Max
subscription onto Console metered billing. `{{key:NAME}}` + `AgentTuiSecret` would carry it safely
(§3/§4 of that doc), so the machinery exists — but it must be a deliberate operator choice, not the
default.

**D. `CLAUDE_CODE_OAUTH_TOKEN_FILE_DESCRIPTOR` / `CCR_OAUTH_TOKEN_FILE` — not available to us.**
Attractive on custody grounds (the secret never becomes an env *value*), but: the fd variant needs
the launcher to pass an inherited fd, which the PtyHost launch path does not do; and the CLI
itself says both are host-injected and unrefreshable — `"This token is injected by the CCR host;
check the host session."`, `"...has no refresh token — it cannot self-refresh"`. `CCR` is
Anthropic's own cloud-runner harness, not a general facility.

### Recommendation for Q1

**A (interactive `claude auth login` in the container), with C available as an explicit opt-in.**
A is the only option that is simultaneously: the established precedent in this repo, refreshable,
full-scope, and keeps the credential entirely out of Antiphon's stores, env, argv and Compose
secrets.

Store it on the persistent volume, not in `$HOME`: set `CLAUDE_CONFIG_DIR=/state/claude` (the
tailer already honours that variable — `src/Antiphon.SessionRunner/TranscriptTailer.cs:1106-1112`).
`HOME` is `/home/app` (`docker/session-runner-grok/dind-entrypoint.sh:114`), which lives **in the
image, not on a volume**, so a login stored at `~/.claude` would be destroyed by the next
redeploy. `/state` is the `runner-state` named volume (`docker-compose.server2-runner.yml`), which
is exactly where `GROK_HOME=/state/grok` already puts Grok's store.

## 2. The Grok precedent and the custody discipline (Q2)

`docker/session-runner-grok/Dockerfile`, `runtime-base` stage, is the template to copy:

```
 && curl -fsSL https://x.ai/cli/install.sh | env HOME=/tmp/grok-install GROK_BIN_DIR=/usr/local/bin bash -s 1.0.40 \
 && install -m 0755 /tmp/grok-install/.grok/downloads/grok-linux-x86_64 /usr/local/bin/grok \
 && ln -sf grok /usr/local/bin/agent \
 && /usr/local/bin/grok --version \
 && /usr/local/bin/grok --version | grep -F '1.0.40' \
 && rm -rf /tmp/grok-install
```

Five properties worth preserving verbatim for Claude: an **exact pinned version**, a **throwaway
`HOME`** so the installer's own dotfiles never land in the image, the binary **installed to
`/usr/local/bin`**, a **post-install assertion that the installed version is the pinned one**, and
the temp tree **deleted in the same layer**.

The Dockerfile's standing comment states the boundary in its own words:

> `# Do not COPY OAuth credential files or any GROK_HOME contents (CARD-0575).`

docs/agent-credentials.md §5 (server2 runner credentials, CARD-0604) records the discipline that a
Claude credential must satisfy: generated on server2, 0600 owner `mc`, delivered as a Compose file
secret, re-staged by `dind-entrypoint.sh` onto the `/run/antiphon` **tmpfs** at 0400 uid 1654
(because a Compose secret arrives owned by the host uid and uid 1654 cannot open it — the failure
that produced 304 silent registration failures behind a green `/health`), readability *proved*
before the container proceeds, refusal with a named code rather than starting degraded, and never
printed, logged or hashed.

**Path A needs none of that machinery**, which is its main virtue: an interactive in-container login
never produces a file on the host that has to be delivered. The one piece of the discipline it does
inherit is the refusal-rather-than-degrade rule — see the probe gap in §5.

## 3. Image changes (Q3)

### What "latest" resolves to right now

Measured 2026-09-23:

| Channel | Version |
|---|---|
| npm `@anthropic-ai/claude-code` `latest` | **2.1.280** (`time.modified` 2026-09-22T16:38:19Z) |
| npm `stable` dist-tag | 2.1.267 |
| `https://downloads.claude.ai/claude-code-releases/latest` | **2.1.280** |
| `.../claude-code-releases/stable` | 2.1.267 |
| this desktop | 2.1.266 |

The container's install is decoupled from the desktop's, exactly as the card says. CARD-0611 stays
untouched.

### Install mechanics — the native binary, pinned by digest

There is a fully deterministic, pinnable, checksum-verifiable route that needs **no node and no
npm**, which matters because `node` is only `COPY --from=node22`'d into the `session-testing`
stage, not into `runtime-base`:

```
https://downloads.claude.ai/claude-code-releases/2.1.280/manifest.json
  → platforms["linux-x64"].checksum = 1e08503dbdf3c2cb0d706d32f3408277388d1c76ef108673e8fe42c1b322925b
  → platforms["linux-x64"].size     = 233709640
https://downloads.claude.ai/claude-code-releases/2.1.280/linux-x64/claude
```

(`manifest.json` also carries `commit`, `buildDate 2026-09-21T20:55:27Z`, and a
`manifestSignatureEnforcement` field.) Download, `sha256sum`-verify against the pinned digest,
`install -m 0755 … /usr/local/bin/claude`, assert `claude --version | grep -F 2.1.280`. That is the
Grok pattern with a **stronger** guarantee — Grok's install has no digest check.

Why *not* `curl -fsSL https://claude.ai/install.sh | bash -s 2.1.280`: it works (the target
argument is validated `^(stable|latest|N.N.N…)$` and forwarded to `"$binary_path" install
"$TARGET"`), but it **always downloads the latest binary first as a bootstrap** ("Always download
latest version (which has the most up-to-date installer)") before installing the pinned target, so
the build pulls two large binaries and depends on a moving artefact mid-build. It also installs a
launcher and shell integration under `$HOME`, which the `HOME=/tmp/...` trick then has to clean up.
The direct pinned download avoids both.

Debian bookworm base ⇒ **glibc**, so `linux-x64` (not `linux-x64-musl`). The image already carries
bash, curl and ca-certificates in `runtime-base`; `sha256sum` is in coreutils. **No new apt
packages are required.**

### Size

The `linux-x64` binary is **223 MiB** (233,709,640 bytes). On top of a `session-testing` image that
already carries dotnet SDK 10 + aspnet 9/10 + node 22 + the docker static tarball + pwsh 7.5.4,
this is material but not disqualifying. Disk on server2 was measured at 55 GB free during CARD-0604
(plan line 95).

### Which stage

`runtime-base`, alongside the Grok install, so every downstream target (`receipt-probe`,
`session-testing`, `runtime`) inherits it — mirroring how `grok` is placed today. server2 runs the
`session-testing` target (`docker-compose.server2-runner.yml`, `target: session-testing`).

### Also needed in the image/compose

- `CLAUDE_CONFIG_DIR=/state/claude` on the runner service, and `/runner-state/claude` added to
  `docker/stack/init-state.sh`'s mkdir/chown list beside the `/runner-state/grok` it already creates
  (`runner-state` mounts at `/state` in the runner service).
- `DISABLE_AUTOUPDATER=1` already arrives as a Claude *kind default*
  (`AgentTuiLaunchResolver.ApplyClaudeEnvironmentDefaults`, server/Application/Services/AgentTuiLaunchResolver.cs:590-605),
  together with `CLAUDE_CODE_DISABLE_ALTERNATE_SCREEN=1` and the nesting markers — so the pinned
  version stays pinned without a new setting. Worth re-asserting it survives the phone-home
  projection, which rebuilds the env dictionary (`PhoneHomeLaunchPolicy.Project`, line 146-152).
- `DockerStackContractTests` (`tests/Antiphon.Tests/Infrastructure/DockerStackContractTests.cs`)
  asserts stage shapes and a stage-installed health/tool manifest; a new install step in
  `runtime-base` will need its arm there. This is the existing pattern, not an obstacle.

## 4. Is `AgentKind.ClaudeCode` dispatchable through PhoneHomeRunner today? (Q4)

**No. Five hard refusals, three server-side and two runner-side. Every one is an explicit
Grok-or-nothing string, not an incidental assumption — which is the good news: the surface is small
and deliberate, not diffuse.**

| # | Where | Refusal |
|---|---|---|
| 1 | `server/Application/Services/AgentTaskService.cs:1044` | `"A runner-bound task must be Grok."` — a `ValidationException` at task *creation*, before anything is queued. This is the first wall a `delegate.ps1 -Runner server2` Claude dispatch hits. |
| 2 | `server/Application/Services/PhoneHomeLaunchPolicy.cs:100` | `"A runner-bound task must be Grok."` (`phone_home_kind_refused`) — the dispatch-time twin of #1, on the delegated-task arm. |
| 3 | `server/Application/Services/PhoneHomeLaunchPolicy.cs:113` | `"A runner-bound named agent must be Grok or Raw."` — blocks the named-agent shape the server2 acceptance cases use. (Line 87's *pinned* agent must stay Grok; CARD-0490's pinned agent is a separate, narrower contract and should not be widened.) |
| 4 | `server/Application/Services/PhoneHomeLaunchPolicy.cs:165-182` (`ProjectExe`) | Maps only `grok.exe`/`grok` → `grok`, else falls through to `RawExeAllowList` and otherwise throws `phone_home_wrapper_refused`. A `claude.exe` spec has no arm, so the projection itself fails. |
| 5 | `src/Antiphon.SessionRunner/PhoneHomeCommandDispatcher.cs:177-184` **and** `:203-205` | The runner's own backstop: `isGrok` (filename `grok`, or a path ending `/grok`) or an exact `RawExeAllowList` match, else `"Only an image-owned executable may launch."`; **and** `"Only the Grok transcript format is admitted."` A Claude launch fails both — the second is easy to miss because the exe fix alone would leave a 409 on `transcriptFormat: "claude"` (`AgentControlService.cs:1043-1045` maps `AgentKind.ClaudeCode` → `TranscriptFormats.Claude`). |

Plus one env projection that is Grok-shaped rather than refusing:

- `PhoneHomeLaunchPolicy.cs:146-152` unconditionally sets `GROK_HOME = ChildGrokHome` on every
  runner-bound launch. There is no `ChildClaudeHome`
  (`server/Application/Settings/PhoneHomeRunnerSettings.cs:33`) and no `ClaudeHome`
  (`src/Antiphon.SessionRunner/PhoneHomeSettings.cs:19`). A Claude launch needs
  `CLAUDE_CONFIG_DIR` projected the same way, or it writes its transcript and credentials to
  `$HOME/.claude` inside the image and loses both on redeploy.

### What is *not* blocked — measured, so Plan does not re-derive it

- **Registration / `dispatchEligible` is kind-agnostic.** `PhoneHomeRunnerDirectory.cs:218,238,255`
  and `PhoneHomeLiveConnection.cs:62-77` gate on lease, epoch and capacity only. CARD-0604's live
  `dispatchEligible` therefore already covers a Claude session.
- **Transcript tailing works on Linux unchanged.** `TranscriptTailer.cs:1106-1112` resolves
  `CLAUDE_CONFIG_DIR` first, else `SpecialFolder.UserProfile + "/.claude"`, then `/projects` — the
  same rule Claude Code itself uses, and `UserProfile` is `$HOME` on Linux.
- **The model ladder needs no change.** `ModelLevelAliases.ForClaude` sends the *alias* `opus`
  (High) / `fable` (Frontier), never a versioned id, so a fresh 2.1.280 in the container picks up
  whatever Opus that build knows — Opus 5.5 support is a property of the installed CLI, not of
  Antiphon. `ForLaunch` already has its `AgentKind.ClaudeCode` arm.
- **`delegate.ps1 -Runner` has no client-side kind check** (scripts/delegate.ps1:918-931 validates
  only Worktree / not-OnAgent / not-SourceLanding), so no script change is needed beyond whatever
  the server starts admitting.
- **`EnsureSpawnable` is correctly skipped** for runner-bound specs
  (`AgentControlService.cs:472-476`), so the Windows `claude.exe` not existing on a Linux runner is
  not itself a problem.
- **`ANTIPHON_PTY_BACKEND: inbox`** in the server2 compose is Windows-only terminology
  (`src/Antiphon.Agents.Pty/PtyBackend.cs:60`) and is inert on Linux; it is not a Claude-specific
  risk.

## 5. Two security-relevant findings the card did not ask for

**5a. `.dockerignore` excludes `**/.grok` and `**/auth.json` but not `**/.claude`.**
`.dockerignore:6,12` — the CARD-0575 denial covers Grok's provider home and OAuth file only. The
repo root *does* contain `.claude/` (verified: currently only `.claude/settings.json`, no
credential material), and the runner Dockerfile's build stage does `COPY . .`, so `.claude/` enters
the build context today. It is benign right now purely because nothing has put credentials there.
The moment the image carries a Claude CLI, the asymmetry becomes the same exposure CARD-0575 closed
for Grok. `docker/tests/Dockerfile.dockerignore` has the identical gap, and
`DockerStackContractTests` has `Runtime_context_denies_ProviderHome` /
`Tests_context_denies_ProviderHome` arms asserting `.grok/config.toml` with no Claude twin
(lines 366, 380).

**5b. There is no Claude analogue of the Grok credential pre-flight, so a credential-less Claude
runner would stall silently instead of refusing.**
`AgentTaskDispatcher.TryFailGrokCredentialProbeAsync` (server/Application/Services/AgentTaskDispatcher.cs:2527,
gated by `AgentRegistrySettings.GrokCredentialProbeEnabled`, :116) is what turns a missing Grok
OAuth store into the documented 409 `provider_sign_in_required` rather than a session parked on a
sign-in screen (CARD-0324). Nothing equivalent exists for Claude. A Claude dispatch to a container
whose `/state/claude` login has expired would launch `claude`, land on its login screen, and sit
there as a *stall* — which by the session rules is "a detection/decision state, never an automatic
kill", i.e. it burns a capacity slot and waits for a human.

There is a clean, secret-free probe available: **`claude auth status --json`**. Verified on this
desktop — it returns `{"loggedIn": true, "authMethod": "claude.ai", "apiProvider": "firstParty",
"projectsDirectory": …, "subscriptionType": "max", …}` and **prints no token, no key and no
fragment of either**. It is safe to run inside the container and safe to log. Because the store
lives in the container, the probe has to run runner-side (the server cannot stat `/state/claude`),
which makes it a natural addition to the runner's `Capabilities()` payload or to the
`dind-entrypoint.sh` refusal set — but note the entrypoint cannot *require* it on a first boot,
because the login can only be performed after the container is up.

## 6. Recommended approach for Plan

1. **Auth: interactive `claude auth login` inside the persistent container**, credential on
   `/state/claude` via `CLAUDE_CONFIG_DIR`, provisioned once by the operator exactly as `grok login`
   already is, documented in docs/agent-credentials.md §5 as a third row (names and locations only).
   `ANTHROPIC_API_KEY` via `{{key:NAME}}` stays an explicit opt-in with the billing consequence
   stated, not the default. Do **not** use `setup-token`/`CLAUDE_CODE_OAUTH_TOKEN` as the primary
   path: inference-only scope, no self-refresh, and it turns a refreshable store into an env value.
2. **Image: pinned native binary, digest-verified**, in `runtime-base` beside the Grok install —
   version 2.1.280 with sha256 `1e08503d…22925b`, throwaway `HOME`, `--version` assertion, temp
   tree removed in the same layer.
3. **Server: widen exactly four gates** (`AgentTaskService.cs:1044`,
   `PhoneHomeLaunchPolicy.cs:100`, `:113`, and a `claude` arm in `ProjectExe`), leaving the *pinned*
   CARD-0490 agent's Grok-only rule at `:87` alone.
4. **Runner: widen exactly two** (`PhoneHomeCommandDispatcher.cs:177-184` exe admission, and
   `:203-205` to admit `TranscriptFormats.Claude`). Keep the runner's independent copy of the rule —
   it is the image's own contract, and that independence is the point.
5. **Env projection: add `ChildClaudeHome`/`ClaudeHome` settings** and project `CLAUDE_CONFIG_DIR`
   for a Claude launch the way `GROK_HOME` is projected for Grok, with the same POSIX-absolute
   validation.
6. **Close 5a and 5b** in the same pass: `**/.claude` + `**/.credentials.json` in both
   `.dockerignore` files with contract-test arms, and a `claude auth status --json` readiness probe
   that produces a named refusal instead of a stall.
7. **Keep `RemoteControlEnabled=false`** on runner-bound Claude agents for the first cut. Remote
   control is declared Supported for ClaudeCode **only**
   (`AgentTuiRunnerCatalog.cs:116`; every other kind is Unsupported at :130/:144/:158/:172), and
   `AgentControlService.cs:255-261` silently drops the flag for the rest — so a runner-bound Claude agent is the first shape where the
   flag is live rather than ignored, and it is an unmeasured outbound-WS dependency from server2.

## 7. Remaining uncertainties

- **Whether `opus` on 2.1.280 resolves to Opus 5.5.** Not verifiable from here: the desktop's
  2.1.266 binary contains only `claude-opus-5` (grep: no `claude-opus-5-5`), and 2.1.280 is not
  installed anywhere on this machine. Resolve by a one-line probe after the image builds. Antiphon
  needs no change either way — it passes the alias.
- **Whether the raw pinned binary runs standalone without `claude install`'s launcher.** Highly
  likely (it is a self-contained bun binary, and the installer runs `"$binary_path" install` with
  that same binary), but it is a build-time assertion for Code, not something measured here. The
  fallback is the Grok-shaped `install.sh` route with a throwaway `HOME`.
- **Whether one Max subscription serving both the desktop and a server2 container hits a
  concurrency or rate ceiling.** `claude auth status --json` reports `subscriptionType: "max"`.
  Unmeasured; a live-load question, and an operator decision if it bites.
- **Whether `claude auth login`'s manual-paste fallback needs a TTY that `docker exec -it`
  satisfies.** The strings say it prints a URL and accepts a pasted code; the Grok precedent proves
  `docker exec -it -u 1654:1654` is a usable interactive lane in this exact container. Not executed
  against server2 in this investigation.
- **Whether Claude's TUI drives correctly over the Linux PtyHost.** Grok's does (CARD-0594), and
  the delivery contract (LF + bracketed paste + separate Enter) is kind-agnostic, but Claude's TUI
  has not been run on the Linux pty host. This is the largest *unknown-unknown* left and belongs in
  the plan's verification, not in its design.

## Not done, noted

- A fix idea, for Plan to accept or reject: gate the widened kind on a new
  `PhoneHomeRunner:AllowedKinds` list rather than adding `or AgentKind.ClaudeCode` at four sites, so
  the runner's admitted-kind surface is one configurable fact instead of four scattered literals —
  and so the runner-side copy can be derived from the same list.
