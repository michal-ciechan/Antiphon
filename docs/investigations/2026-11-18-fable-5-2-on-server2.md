# 2026-11-18: getting Fable 5.2 onto the server2 runner

Investigate task `b07ae1e1` (operator Fable canary, tasks `4ba1f3ff` and `30a7a693`).
Master read: `709c41f16caf400b6fa91a80866a06d2c05fc318`.
Evidence was gathered read-only from inside the server2 runner container
(`antiphon-runner-session-runner-1`, container id `7c9c24dac62d`, uid 1654), where this task runs.
It ran `claude --version` only (no prompt, no tokens), a string scan of the installed binary, and
HTTP `HEAD` requests. Nothing was updated, rebuilt, restarted or deployed. No secret was read, and
the production Postgres was not touched.

## Verdict

**Confirmed.** server2's `fable` resolves to `claude-fable-5-1` because the runner image pins
Claude Code **2.1.280** by version and SHA-256. That build's baked model catalog stops at Fable 5.1,
and nothing on the runner can update the CLI in a way that survives. The pin has not moved since
CARD-0628 first set it (one commit touches it: `55a8c7d1c2`, 2026-09-23). Today's image, built
2026-11-18 01:25 UTC, therefore still carries 2.1.280.

Recommendation: **A (bump the image pin), with D as the stopgap until A is live.** The steps are
under [Recommendation](#recommendation).

## 1. Where the server2 CLI comes from

| Fact | Evidence |
|---|---|
| The image downloads the native linux-x64 binary from `downloads.claude.ai`, pinned by version and SHA-256 | `docker/session-runner-grok/Dockerfile:83-87` (`ARG CLAUDE_CODE_VERSION=2.1.280`, `ARG CLAUDE_CODE_SHA256=1e08503d…925b`), `:112-117` (curl, `sha256sum -c`, `install -m 0755 … /usr/local/bin/claude`, `--version` check under `DISABLE_AUTOUPDATER=1`) |
| A second literal guard in the `session-testing` (server2 runner) target | `docker/session-runner-grok/Dockerfile:276` (`claude --version \| grep -F '2.1.280'`) |
| The runner compose uses that image, tagged per source SHA | `docker-compose.server2-runner.yml` (runner `image: antiphon-server2/session-testing:${SOURCE_SHA12}`) |
| The docs record the pin and how to raise it | `docs/docker-stack.md:1934` ("Claude Code 2.1.280 … Raising the pin means a new version/SHA-256 pair, never dropping the check"), `docs/agent-kinds.md:38`, `docs/agent-kinds.md:241-249`, `docs/bootstrap.md:28` |
| Every Antiphon-launched Claude gets `DISABLE_AUTOUPDATER=1` | `server/Application/Services/AgentRegistry.cs:189-193`, `AgentTuiLaunchResolver.cs:659-660`, `docs/agent-kinds.md:316` ("an update mid-session rewrites the binary under a live pty") |

**Desktop compared with the runner.** The desktop CLI is the operator's own native install. Antiphon's
spawned agents carry `DISABLE_AUTOUPDATER=1`, so the desktop does not advance through them. It
advances through the operator's interactive Claude sessions, which run with auto-update on.
That explains why the desktop reached 2.1.327 (per the canary). I could not inspect the desktop
from here, so this mechanism is inferred, not observed. The runner has no interactive operator
session that persists anything. Its binary sits in a root-owned image layer, and the brief, the
`AgentRegistry` default and the image's `.claude.json` all keep auto-update off.

## 2. Live runner state (read-only, 2026-11-18 ~22:25 UTC)

| Probe | Result |
|---|---|
| `claude --version` | `2.1.280 (Claude Code)` |
| `which claude` | `/usr/local/bin/claude` (regular file, not a symlink), `root:root 0755`, 233,709,640 bytes, mtime and birth `2026-11-18 01:25:28 UTC` (image build) |
| `/usr/local/bin` writable by uid 1654 | **no** |
| Container start (PID 1) | `Wed Nov 18 03:55:43 2026`, so the runner was recreated today on the new image, which is still at 2.1.280 |
| `PATH` | `/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin`, with no `~/.local/bin` |
| Process env | `CLAUDE_CONFIG_DIR=/state/claude`, `DISABLE_AUTOUPDATER=1`, `HOME=/home/app` |
| `/state/claude/.claude.json` (update/model keys only) | `installMethod="native"`, `autoUpdates=false`, `autoUpdatesProtectedForNative=true`, `lastReleaseNotesSeen="2.1.309"` |
| `/state/claude/settings.json` keys | `skipDangerousModePermissionPrompt` only (no model or update keys) |
| `/state/claude/.last-update-result.json` | `{"timestamp":"2026-10-22T23:50:59.658Z","path":"native","outcome":"success","version_from":"2.1.280","version_to":"2.1.309"}` |
| `/home/app/.local/share` | contains no `claude` directory (Microsoft, NuGet, antiphon, applications, grove, powershell) |
| Persistent mounts | `/state` (named volume `runner-state`), `/work`, `/home/app/.nuget/packages`, `/home/app/.npm/_cacache`. **`/home/app` itself is the container layer.** |
| npm / curl | `/usr/local/bin/npm` 10.9.9, `/usr/bin/curl` |
| `HEAD https://downloads.claude.ai/claude-code-releases/2.1.327/linux-x64/claude` | `200`, 372,904,464 bytes, Last-Modified `Tue, 17 Nov 2026 20:20:33 GMT` |
| `HEAD …/2.1.327/manifest.json` | `200`, 2,101 bytes (holds the published checksums; I did not GET it) |
| `HEAD …/latest`, `…/stable` | `200`, 7 bytes each, Last-Modified 2026-11-17 23:24 UTC. A release after 2.1.327 may exist; I did not GET the body |

**Can the CLI update itself in place under uid 1654 and keep the update across a recreate? No.**
The evidence shows this has already happened once. On 2026-10-22 a native self-update of 2.1.280
to 2.1.309 reported `success`. Some Claude process that ran without `DISABLE_AUTOUPDATER` performed
it; which one is unknown. It also left `lastReleaseNotesSeen=2.1.309` in the persistent store. The
native installer writes under `$HOME` (`~/.local/share/claude`, `~/.local/bin`). `$HOME` is
`/home/app` in the container layer, which is not on `PATH`, and that directory is gone now. So the
update never shadowed `/usr/local/bin/claude`, and the next recreate discarded it. An in-place update
can only reach `/state` or `$HOME`. `/state` persists but is not on `PATH`. `/usr/local/bin` is
root-owned and image-owned.

## 3. What 2.1.280 knows about Fable (binary string scan)

Baked catalog in the 2.1.280 binary (offset ~191.99 MB):

```
aliases:{… fable:{default:"claude-fable-5-1",per_provider:{gateway:"claude-fable-5"}}}, best:"fable",
latest_per_family:{fable:"claude-fable-5-1",opus:"claude-opus-5-5",sonnet:"claude-sonnet-5",haiku:"claude-haiku-4-5"}
```

The binary has catalog rows for `claude-fable-5` and `claude-fable-5-1` only. The ID
`claude-fable-5-2` does not occur anywhere in it. This also shows that server2's **Medium** tier
still resolves `sonnet` to `claude-sonnet-5`, not Sonnet 5.5, as `docs/agent-kinds.md:244-249`
predicted for an un-bumped pin.

How 2.1.280 treats a model it does not know:

- **A full ID is passed through.** The main resolver, `function kt(e)`, maps only the aliases
  (`fable`, `opus`, `sonnet`, `haiku`, `opusplan`, `best`). Every other string ends at
  `return dE(n)`, and `function dE(e){return e}` is the identity. So `--model claude-fable-5-2` is
  sent unchanged. The CLI does not reject it locally. A model the API refuses surfaces as the
  API-side `model_not_found` message ("There's an issue with the selected model (…). It may not
  exist or you may not have access to it.").
- **The `fable` alias can be redirected per process by an environment variable:**
  `function OHe(){let e=a.ANTHROPIC_DEFAULT_FABLE_MODEL;return dE(e!==void 0?av(e):Ud())}`. With
  `ANTHROPIC_DEFAULT_FABLE_MODEL=claude-fable-5-2`, `--model fable` would send `claude-fable-5-2`.
  `ANTHROPIC_DEFAULT_FABLE_MODEL_SUPPORTED_CAPABILITIES` also exists.
- **`claude-fable-5-2` is an unknown model to 2.1.280, so the CLI does not handle it as Fable.** The
  canonicalizer `OF(e)` parses `claude-fable-5-2` as family `fable`, version 5.2 with no trailer.
  It then looks for a catalog row with the same family and the same major.minor (`PF`/`sen`), finds
  none, and returns the base ID itself, before the substring fallbacks
  (`includes("claude-fable-5")`) are reached. The ID is therefore uncatalogued, and the code shows
  these consequences:
  - Context-window enforcement for unknown models applies (`source:"unknown-model"` in `ok()`).
    Its notice reads "Until then auto-compact keeps this session within N tokens (the context
    window it assumes) … append [1m] to the model name for 1M … set CLAUDE_CODE_MAX_CONTEXT_TOKENS
    … CLAUDE_CODE_DISABLE_UNKNOWN_MODEL_WINDOW_ENFORCEMENT=1 restores the previous behavior".
  - The capability flags that catalog rows carry are not applied: effort, adaptive thinking,
    `rejects_disabled_thinking`, `per_turn_effort`, `fable_5_mitigations` and the prompt bundle.
  - Cost is reported as `tengu_unknown_model_cost` and falls back to the default model's price.

  The scan does not show what the assumed window is, nor how the API reacts to the missing
  capability flags. Only a token-spending canary on server2 can settle those.

## 4. Options

| | What | Effort | Risk | Gets real 5.2 handling? |
|---|---|---|---|---|
| **A** | Raise `CLAUDE_CODE_VERSION`/`CLAUDE_CODE_SHA256` in the image (to 2.1.327, the version the desktop canary proved; or a later one), rebuild, and roll the runner | Small Code task: one Dockerfile pair, about 10 coupled literals and tests, docs. Plus one runner recreate | The recreate interrupts running sessions, so batch it (see below). There are 47 CLI releases of behaviour change: the onboarding/trust seed was measured on 2.1.280/2.1.281 (`seed-claude-onboarding.mjs:4,19`, `dind-entrypoint.sh:116`), and pty/footer heuristics were measured on 2.1.280 (`RunnerStartupStaleObservationTests.cs:18`). The desktop already runs 2.1.327 through the same adapters, which reduces but does not remove the risk on Linux | **Yes**. The alias stays `fable`, holds stay keyed `fable`, and Medium also gains Sonnet 5.5 (2.1.284+) |
| **B** | Self-update in place (`claude update`) in the runner | None in the repo | **It does not persist.** It installs into `$HOME` in the container layer, off `PATH`, and is lost at recreate (already observed 2026-10-22). Nobody owns it, it breaks the SHA-256 custody of CARD-0628 D-2, and it rewrites a binary under live ptys. It is also outside this task's authority | No (the old binary stays first on `PATH`) |
| **C** | Explicit `claude-fable-5-2` for ClaudeCode Frontier | Small code change | **No per-runner override exists.** `ModelLevelAliases.ForClaude` (`server/Application/Services/ModelLevelAliases.cs`) is keyed by `AgentModelLevel` only, so the change would apply to the desktop as well. The real per-runner lever is `ANTHROPIC_DEFAULT_FABLE_MODEL` in the runner compose env, because the container env reaches every pty child (`docs/agent-kinds.md:42`; this session sees the runner's `CLAUDE_CONFIG_DIR`/`DISABLE_AUTOUPDATER`). But a compose env change **also needs a runner recreate**, the same disruption as A. 2.1.280 would run 5.2 as an **unknown model** (section 3: window cap, no capability flags). The explicit-ID variant also moves the alias that holds, walls and display are keyed by (`ModelAvailability` holds `(kind, alias)`; `DispatchModelAlias`) | It reaches the 5.2 model, but with degraded and unproven CLI handling |
| **D** | Route Frontier work that must be on 5.2 to the desktop (`-Runner desktop`) | None | Desktop capacity and the "prefer server2" operator default; the desktop CLI version drifts unpinned | Yes, on the desktop only |

## Recommendation

**A**, with **D** as the stopgap until the new runner is live.

Steps for A (a Code task; this note designs nothing beyond naming the touch points):

1. Read the 2.1.327 (or chosen) linux-x64 SHA-256 from
   `https://downloads.claude.ai/claude-code-releases/<v>/manifest.json`. Then download the binary
   and check that `sha256sum` matches before committing the pair.
2. Update `docker/session-runner-grok/Dockerfile:86-87` and the literal at `:276`. Also update the
   coupled pins and assertions: `scripts/verify-card0604-dind-runner.ps1:384`,
   `tests/Antiphon.Tests/Infrastructure/DockerStackContractTests.cs:560`,
   `tests/Antiphon.Tests/Infrastructure/DockerStackDocumentationTests.cs:268`,
   `tests/Antiphon.Tests/Scripts/VerifyDindRunnerScriptTests.cs:149`, `docs/docker-stack.md:1934`,
   `docs/agent-kinds.md:38,241-249`, `docs/bootstrap.md:28`. Decide whether
   `seed-claude-onboarding.mjs:19` (`pinnedOnboardingVersion`, set only when absent) and the
   `dind-entrypoint.sh:116` comment need a re-measure on the new binary.
3. Review, land, port to the current rc (product hosts track rc), and build the runner image.
4. Roll it out with the staged server2 rollout (`docs/docker-stack.md` "Staged server2 rolling
   rollout (CARD-0934)", with continuous dispatch). **Batch it with CARD-1893 S1 and the CARD-1744
   desktop-win slices**, which also need a runner recreate.
5. Acceptance: in the new runner, `claude --version` reports the new version. A one-turn Frontier
   canary (as in `4ba1f3ff`/`30a7a693`) reports `claude-fable-5-2` in `modelUsage`. Optionally, a
   Medium canary reports `claude-sonnet-5-5`.

**Rollback:** redeploy the previous `antiphon-server2/session-testing:<SOURCE_SHA12>` image through
the rollout's own failed-temp/abandon path (`docs/docker-stack.md` "Failed temp deployment and
abandoning a rollout (CARD-0957/0958)"), or revert the pin commit and roll again. `/state/claude` is
version-agnostic config, but a newer CLI may write keys that 2.1.280 ignores. That is a known
remaining uncertainty, not measured.

## 5. Cost, quota and walls

- `docs/agent-kinds.md:824`: usage walls are per model, not fleet-wide. A Fable 5 cap pauses `fable` on
  `ClaudeCode` and leaves the other aliases running. A per-model cap writes
  `DisabledUntil = now + ModelCapFallbackHoldHours` (default 6). Create or start of a held alias is
  a 409 `model_disabled`, and queued work is skipped until the hold clears.
- **Inside Antiphon, 5.1 and 5.2 share one hold** under A or D. Holds are keyed `(kind, alias)`
  (`server/Application/Services/ModelAvailability.cs:17,60`), and the dispatcher checks the task's
  dispatch alias (`AgentTaskDispatcher.cs:822-823`), which is `fable` on both hosts. A wall seen on
  either host therefore pauses Frontier on both. `UsageLimitWallParser.cs:39` already accepts a
  versioned "your Fable 5.x limit" text and maps it to the Fable alias.
- **On the provider side, whether 5.1 and 5.2 draw on one cap is not established.** The stored wall
  text names "Fable 5" (`UsageLimitWallParser.cs:35-36`). Nothing in the repo or the 2.1.280 binary
  says whether 5.2 has a separate bucket. Under C's explicit-ID variant, Antiphon's hold key would
  stop matching `fable`, so a wall on one host would not pause the other.

## Remaining uncertainties

- The desktop's update path (operator-interactive auto-update) is inferred. I could not inspect the
  desktop from server2.
- I did not identify the process behind the 2026-10-22 2.1.280 to 2.1.309 self-update.
- The assumed context window and the API's reaction for an unknown model on 2.1.280 (option C) are
  unmeasured. Measuring them would cost tokens.
- `stable`/`latest` may already be later than 2.1.327. Choose the version to pin, and accept that a
  later version is unproven on the desktop.

## Not done, noted

- A fix idea, for the Code stage only: bump the pin pair to 2.1.327 and roll it with the CARD-1893 S1
  and CARD-1744 runner recreate.
