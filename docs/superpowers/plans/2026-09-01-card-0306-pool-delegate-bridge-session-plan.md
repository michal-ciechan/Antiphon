# CARD-0306 — Stop Claude Code auto-connecting a claude.ai bridge on launches that did not ask for remote control

**Date:** 2026-09-01 (Plan pass, task 1a5680a4 — design only; no code changed)
**Card:** CARD-0306 "A plain pool delegate (not named-agent create, not UI Add-Work) still gets a real claude.ai bridge-session with no /remote-control command sent"
**Diagnosis:** done, on the card (Grok task 2126a179). This plan verifies that diagnosis against the launch path and the live CLI, then designs the fix.

**Sources (verified this pass):** CARD-0306 text, `AgentTaskDispatcher.ComposeDelegateArgs` / `EnqueueInteractiveSession(..., remoteControlName: null)`, `AgentSessionLaunchComposer`, `AgentSessionService.BuildRuntimeLaunchSpecAsync` / `SendRemoteControlCommandsAsync` (CARD-0292 S1/S2 already shipped), `RemoteControlPolicy`, `claude --help` 2.1.252, Claude Code settings/remote-control docs (2026-09-01), this machine's `~/.claude/settings.json` / `~/.claude.json` / Antiphon and gym-stat project settings, CARD-0145 Gotcha #55, CARD-0240/0212/0292/0293 plans, `DelegateLaunchArgvIntegrityTests`, `AgentSessionLaunchFailureTests.Fresh_launch_never_probes_the_bridge`.

---

## Decision

The bridge is Claude Code's own auto-connect, not Antiphon typing `/remote-control`. Antiphon's `remoteControlEnabled` flag, `remoteControlName: null`, and the launch preamble are all downstream of a CLI default this repo never touches. There is no `--no-remote-control`. The control that exists is `remoteControlAtStartup: false` via `--settings`.

Fix it at **argv**, on **every Antiphon-launched Claude session**, through the one funnel that actually starts the process — not in `AgentSessionLaunchComposer` (the investigation's suggested file, which pool delegates never call).

Three layers, in order:

1. **S0 — measure the CLI contract against real Claude before wiring it into production.** The investigation called `--settings '{"remoteControlAtStartup":false}'` "plausible, not live-tested". This machine's user/project settings do **not** set the key; auto-connect is the unset/org default. If `--settings` does not beat that default, the rest of the card is a different design.
2. **S1 — every Antiphon Claude launch passes `--settings <shipped file>` with `remoteControlAtStartup: false`.** File, not inline JSON (CARD-0101 quote hazard). Kind-gated to `ClaudeCode`. Operator-launched `claude` in a terminal is untouched. Antiphon's existing `/remote-control` preamble then becomes a real opt-in for the seats that set `remoteControlName`.
3. **S2 — keep the CARD-0292 menu dismiss as the backstop; do not extend the resume probe to fresh launches unless S0 fails.** After S1, a fresh RC-wanted session is unarmed when the preamble runs, so `/remote-control` is a first-time enable (the original CARD-0240 happy path). `Fresh_launch_never_probes_the_bridge` stays correct. If S0 shows auto-connect still happens, stop and take the fallback in §S0 outcomes — do not silently add a probe and ship.

Do **not** edit `~/.claude/settings.json`. Do **not** set `disableRemoteControl: true` (that refuses `--remote-control`, `/remote-control`, and the in-session toggle — it would break the opt-in). Do **not** write gym-stat or Antiphon project settings (shared with humans).

---

## Ground truth (checked, not guessed)

### What the incident session actually did

Gym Stat Orchestrator dispatched pool delegate `task-1ee199c1` → agent `1646e190-…` → session `98a6c91a-…` (2026-09-01T12:57:59Z, `C:\src\gym-stat`). Stored `remoteControlEnabled: false`. Dispatcher hardcoded `remoteControlName: null` (`AgentTaskDispatcher.cs:2086`). Transcript `~\.claude\projects\C--src-gym-stat\98a6c91a-….jsonl` entry [6] is a real `bridge-session` (`cse_01DcyaS64xZijSzLyxJahzPU`) **before** the first user record. Zero `<command-name>/remote-control` local-command records.

This is **not** CARD-0293 (named create with `remoteControlEnabled: true`), **not** Add-Work's forced-true checkbox, **not** CARD-0292 (resume of an already-bridged session).

### Where a pool-delegate command line is actually built

| Layer | Owner | Pool delegate? |
|---|---|---|
| Profile / definition + `--dangerously-skip-permissions` | `AgentTuiLaunchResolver` / `AgentRegistry.Resolve` | yes |
| `--name`, bundles as `--append-system-prompt` | `AgentTaskDispatcher.ComposeDelegateArgs` (`:2440`) | **yes — this is the ExtraArgs composer** |
| `--name`, bundles as `--append-system-prompt` | `AgentSessionLaunchComposer.ComposeForAgentAsync` | **no** — standing / card-assigned / orchestrator only |
| `--session-id` / `--resume` / `--continue` | `AgentSessionService.BuildSessionIdentityArgs`, called from `BuildRuntimeLaunchSpecAsync` (`:1101`) | yes |
| `/remote-control` + `/rename` typed after ready | `SendRemoteControlCommandsAsync` | no — returns at once when `remoteControlName` is blank (`:1337`) |

`BuildRuntimeLaunchSpecAsync` is the last mutation before `adapter.StartAsync`. All three production start sites go through it: card spawn (`:174`), interactive/pool (`:374`), card resume (`:923`). Nothing else in the server calls `StartAsync`. That is the CARD-0106 tripwire's reason for existing, and it is the right place to add a Claude argv overlay.

The investigation's "add it to `AgentSessionLaunchComposer`" would miss the incident path entirely. A standing-agent-only fix would also miss the check interpreter, card-spawn-without-RC, and any other `remoteControlName: null` launch.

### Why auto-connect happens here even with no setting

Live CLI 2.1.252:

- `--remote-control [name]` — "Start an interactive session with Remote Control enabled (optionally named)". **No `--no-remote-control`.**
- `--settings <file-or-json>` — "Path to a settings JSON file or a JSON string to load additional settings from".

Docs (code.claude.com, 2026-09-01):

- `remoteControlAtStartup` unset uses the **organization default**. `true` auto-connects; `false` waits for `/remote-control`. Project/local `false` beats a managed `true`. User settings, `--settings`, and managed settings may set `true`.
- `disableRemoteControl: true` refuses every RC start path. Too strong for this card.
- `--remote-control` / `/remote-control` remain the explicit ON switches.

This machine, 2026-09-01:

| File | `remoteControlAtStartup` |
|---|---|
| `~\.claude\settings.json` | **absent** |
| `~\.claude.json` | **absent** (has unrelated `remoteControlSurfacesSeen` / push counters only) |
| `C:\src\Antiphon\.claude\settings.json` | **absent** |
| `C:\src\gym-stat\.claude\settings.json` | file does not exist |

So auto-connect is the unset/org default, not a repo or user checkbox we can flip. The investigation's 68/120 / 66-before-first-user census matches that: essentially every interactive Claude launch on this machine, Antiphon or not.

Gotcha #55 (CARD-0145) already observed the symptom ("a plain `claude --dangerously-skip-permissions` launch creates a claude.ai session") and misattributed it to the slash command. It was auto-connect.

### CARD-0292 interaction (already shipped)

`SendRemoteControlCommandsAsync` currently:

- returns immediately when `remoteControlName` is blank (pool path — types nothing; CLI still auto-connects);
- on **resume**, probes `IRcBridgeProbe` and skips the send when already armed;
- on **fresh**, never probes (`Fresh_launch_never_probes_the_bridge`, `AgentSessionLaunchFailureTests.cs:453`) and types `/remote-control`;
- if the management menu is on screen, Esc-dismisses it (S2).

The card's "fresh wedge" warning is real **today**: an RC-wanted fresh launch auto-connects, then the preamble types `/remote-control` into an already-bridged TUI and opens Disconnect/QR/Continue. After S1, that sequence goes away for Antiphon launches because the CLI no longer auto-connects. Extending the probe to fresh is a fallback if S0 fails, not the primary fix.

`--name` is already on every Claude pool delegate (`ComposeDelegateArgs:2458`). Combined with auto-connect, that is why they appear on claude.ai under the agent name.

---

## S0 — Headed canary: prove `--settings` actually turns auto-connect off

**File:** `tests/Antiphon.Agents.Pty.Tests/ClaudeRemoteControlAtStartupCanaryTests.cs` (new), same attributes as `ClaudeRemoteControlMenuCanaryTests` (`[Explicit]`, `[Category("HeadedCanary")]`, `[NotInParallel("Headed")]`, `[ParallelLimiter<ProcessSpawnLimit>]`). Add the type to `ProcessSpawnLimitTests`'s headed allowlist.

Interactive TUI, **not** `claude -p`: auto-connect is documented as an interactive-session behaviour. Isolated cwd + `--session-id <fresh guid>` + `--dangerously-skip-permissions`. Do not write `~/.claude/settings.json`. Do not submit a model turn; the `bridge-session` record lands before the first user message.

Throwaway `--settings` file in the canary sandbox (the CARD-0247 hook canary already does this). Content exactly `{"remoteControlAtStartup":false}`.

| # | Launch | Pass |
|---|---|---|
| 1 | Control: current Antiphon-like argv, **no** `--settings` | JSONL contains `type=="bridge-session"` with a real `bridgeSessionId` before the first `user` record. **If this is skip/fail, auto-connect is no longer the default on this machine and the card's premise has moved — stop.** |
| 2 | Same + `--settings <off.json>` | No `bridge-session` record for at least 8 s after ready, and none before the first `user` record. TUI still reaches ready. |
| 3 | Case 2, then type `/remote-control` + Enter | Bridge appears (opt-in still works). Do **not** assert the management menu — that is the already-armed shape. |
| 4 | Merge smoke: case 2 JSONL still has the normal startup records this machine writes (`permission-mode` / `atis-latch` or whatever the control run shows). A settings overlay that *replaces* user/project settings would show up as a different startup shape or a missing hook. Log both transcripts; fail only on a crash or a missing composer. Residual merge risk (lost user hooks) is documented, not a hard assert, unless the control vs overlay startup kinds obviously diverge. |

`--settings` value is a **file path**, not an inline JSON string. Inline JSON is `{"remoteControlAtStartup":false}` — those embedded quotes are the CARD-0101 argv class. S1 will not use inline JSON even if a hand-typed probe happens to work in pwsh.

**S0 is the gate.** The implementer runs it locally (`ANTIPHON_HEADED_TESTS=1`) **before** S1. It will not run in CI (`[Explicit]`). Record the four outcomes in the execution report.

### S0 outcomes → what S1/S2 do

| S0 result | Follow |
|---|---|
| 1 green, 2 green, 3 green | S1 as written. S2 is docs + the existing menu dismiss. |
| 1 green, 2 red (`--settings` does not beat org default) | **Do not ship S1.** Try, in this order, still as canary-only: (a) `--settings` JSON-string form (argv-quoted, measured through `LaunchArgvGuard` first), (b) a project-local file is **forbidden**, (c) `--setting-sources` tricks are **forbidden**. If none work, the card is blocked on a CLI that has no per-launch off switch; report that rather than inventing `CLAUDE_CONFIG_DIR` isolation (it would also isolate auth). |
| 3 red (`/remote-control` no longer arms after off) | Do not ApplyOff on RC-wanted launches. Thread `remoteControlName` into `BuildRuntimeLaunchSpecAsync` and ApplyOff only when the name is blank. ON path stays auto-connect + CARD-0292 probe-on-fresh (the fallback below). |
| 1 red (control has no bridge) | Premise moved. Re-read the live default; do not add argv we cannot evidence. |

Fallback if S0 case 2 fails but we still need OFF: there is no honest one. `disableRemoteControl` would also break ON.

Fallback if S0 case 2 works but RC-wanted fresh still auto-connects for some other reason: extend `WaitForResumeBridgeArmedAsync` to fresh launches when `remoteControlName` is set (invert `Fresh_launch_never_probes_the_bridge`), and rely on CARD-0292 S2 Esc. That is a different slice, only if measured.

---

## S1 — ApplyOff on every Antiphon Claude launch

### Helper

New `ClaudeRemoteControlLaunchArgs` next to `CodexLaunchArgs` / `RemoteControlPolicy` (static, DI-free).

- Gate on `kind == AgentKind.ClaudeCode`. This is a **claude.exe argv flag**, the same kind of branch `--name` and `--append-system-prompt` already make in `ComposeDelegateArgs`. Do **not** reuse `RemoteControlPolicy.Permits` — that answers "may we type `/remote-control`", not "does this executable understand `--settings`". A future RC-capable kind would otherwise inherit a Claude flag.
- `OffSettingsJson` = `{"remoteControlAtStartup":false}` (no other keys).
- Resolve a **stable absolute file path** and pass `--settings`, that path. Prefer a shipped content file copied next to the server assembly (`server/Runtime/claude-remote-control-off.json`, `CopyToOutputDirectory` + `CopyToPublishDirectory`, resolved via `Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "claude-remote-control-off.json"))`). Alternate that is also fine: embed the JSON as a resource and extract once to a well-known path under the session-log root. Either way the argv **value is a path**, never the JSON.
- If `--settings` is **already** in `args` (a profile that ships its own file): do not append a second flag (CLI arity is singular; a second file could replace the first). Merge `remoteControlAtStartup: false` into a combined file we write for that launch, or, if the existing value is a JSON string, parse-and-add the key. Today no production profile or `ArgsTemplate` passes `--settings` (only headed canaries do). Pin the "already present" arm with a unit test so the day a profile grows one we do not wipe it.
- Strip nothing else. Do not touch `--remote-control` if a profile ever adds it.
- Idempotent: calling ApplyOff twice does not duplicate the flag.

### Funnel

`AgentSessionService.BuildRuntimeLaunchSpecAsync` (`:1101`), **after** `BuildSessionIdentityArgs`:

```
var args = UsesSessionIdentityArgs(session.AgentKind)
    ? BuildSessionIdentityArgs(launchSpec.Args, session.Id, resumeMode)
    : launchSpec.Args;
args = ClaudeRemoteControlLaunchArgs.ApplyOff(session.AgentKind, args);
```

Always ApplyOff for Claude, including RC-wanted launches. That is the point: auto-connect off is the default; `/remote-control` is the opt-in. Card resume (`ResumeAsync`) does not re-type `/remote-control` today; it still needs ApplyOff so a resumed card session does not auto-connect a bridge the original spawn never asked for.

Do **not** add this in `ComposeDelegateArgs` or `ComposeForAgentAsync`. Those are ExtraArgs composers; session-scoped CLI overlays already live next to `--session-id`. Putting it in one composer and not the other is how the investigation's suggested file would have missed the incident.

### Argv integrity

`DelegateLaunchArgvIntegrityTests.ComposeLaunchArgs` currently does `BuildLaunchSpec` + `BuildSessionIdentityArgs`. Production will do a third step. Extend `ComposeLaunchArgs` to call `ApplyOff` so the test still composes the command line production builds. The off-settings **path** must round-trip through `LaunchArgvGuard` (spaces in `AppContext.BaseDirectory` are allowed; quotes in the JSON are not, which is why it is a file).

### Tests (in-process, no real Claude)

| Test | Assert |
|---|---|
| `ClaudeRemoteControlLaunchArgsTests` | ClaudeCode → `--settings` + existing file path; file contents are exactly the one key. Grok / Codex / Raw / OpenCode → args unchanged. |
| Same | Already has `--settings` → still exactly one `--settings`; merged file contains `remoteControlAtStartup: false` and preserves the previous JSON's other keys. |
| Same | ApplyOff twice → one pair. |
| `DelegateLaunchArgvIntegrityTests` | Still round-trips after ApplyOff; `--session-id` still present; `--append-system-prompt` still char-for-char. |
| `DelegateBundleLaunchTests` | Unchanged (stops at `BuildLaunchSpec`, before the funnel). |
| `AgentSystemPromptLaunchTests` | ClaudeCode interactive still has `--append-system-prompt` and `--session-id`; now also `--settings`. Do not snapshot the whole argv. |
| `AgentSessionLaunchFailureTests` RC cases | Still type `/remote-control` then `/rename` on a fresh RC-wanted launch (S1 does not skip the preamble). `Fresh_launch_never_probes_the_bridge` still true. Pool/blank name still types nothing. |
| Fakeclaude | Production composers that spawn fakeclaude go through this funnel only for `AgentKind.ClaudeCode`. Fakeclaude must ignore an unknown `--settings <path>` (or the fake's arg parser already skips unknown flags). If it treats the path as a prompt, teach it to ignore `--settings` + value — do not drop ApplyOff in tests. |

### What S1 does not change

- `SendRemoteControlCommandsAsync` control flow.
- Stored `RemoteControlEnabled` / create/PATCH policy (CARD-0212).
- Add-Work checkbox (CARD-0293 remaining).
- Operator `claude` in Windows Terminal.

---

## S2 — Docs, Gotcha #55, CARD-0292 backstop

No production control-flow change if S0+S1 are green.

- **`docs/agent-kinds.md` launch composition** (section 2): add a layer between standing instructions and session identity, or immediately after session identity: "Claude remote-control overlay — `ClaudeRemoteControlLaunchArgs.ApplyOff` appends `--settings <file>` with `remoteControlAtStartup: false` for `AgentKind.ClaudeCode` only. Antiphon then types `/remote-control` only when `remoteControlName` is set."
- **Gotcha #55 (CARD-0145)** rewrite: the orphaned claude.ai rows are auto-connect, not `/remote-control`. Point at this card. CARD-0144 remains the cleanup owner. Keep the operator advice (`/exit`, do not duplicate a Running orchestrator on claude.ai) — that is still true for **manual** launches, which S1 does not touch.
- **New gotcha** (next number in agent-kinds / session-runtime-invariants): Antiphon's `remoteControlEnabled` / `/remote-control` preamble do not control whether claude.ai lists a session. The CLI auto-connects unless `remoteControlAtStartup` is false. Antiphon forces that false on its own launches via `--settings`; a pool delegate with `remoteControlName: null` must not appear on claude.ai.
- **CARD-0292 S2 menu dismiss** stays. It is the backstop if a CLI upgrade ignores `--settings` or a resume re-arms a historical bridge and someone types `/remote-control` anyway.
- **Do not** change `Fresh_launch_never_probes_the_bridge` unless S0 forced the probe-on-fresh fallback.

---

## What this card does not do

- CARD-0293 remaining: `AgentAddWorkModal` default-true checkbox; CARD-0255 orchestrator-preset RC flag. Those are Antiphon-surface bugs. They do not create the CLI bridge, and this card does not make them sufficient.
- CARD-0144: sweeping disconnected claude.ai sessions.
- Switching the ON path from typed `/remote-control` to argv `--remote-control <name>`. That is a reasonable follow-up if CARD-0240's handshake stays flaky once auto-connect is gone; it is not required to stop the incident. Do not combine it with S1 in the same slice — it would re-open every CARD-0292 assertion (`Prompts.ShouldBe(["/remote-control", "/rename …"])`).
- Disconnecting **already-live** pool sessions that already have a `bridgeSessionId`. They retire with `PoolIdleRetireMinutes`. A one-time pool recycle is an operator action in the execution brief if the sidebar is still noisy after deploy, not code.
- Grok / Codex. They have no claude.ai bridge.

---

## Test matrix

| Layer | Test |
|---|---|
| Headed `[Explicit]` | `ClaudeRemoteControlAtStartupCanaryTests` — S0 table of four |
| `Antiphon.Tests` unit | `ClaudeRemoteControlLaunchArgsTests` — kind gate, file contents, merge-with-existing-`--settings`, idempotence |
| `Antiphon.Tests` Application | `DelegateLaunchArgvIntegrityTests` still green with ApplyOff in `ComposeLaunchArgs` |
| `Antiphon.Tests` Application | `AgentSessionLaunchFailureTests` RC suite unchanged on prompts; blank-name types nothing |
| `Antiphon.Tests` Application | `AgentSystemPromptLaunchTests` Claude launches contain `--settings` |
| Pty / fakeclaude | Ignore `--settings` + path if a ClaudeCode fakeclaude launch would otherwise eat the path |

Run per `docs/testing-and-build.md`:

```powershell
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-card0306/ -- --treenode-filter "/*/*/ClaudeRemoteControlLaunchArgsTests/*"
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-card0306/ -- --treenode-filter "/*/*/DelegateLaunchArgvIntegrityTests/*"
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-card0306/ -- --treenode-filter "/*/*/AgentSessionLaunchFailureTests/*"
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-card0306/ -- --treenode-filter "/*/*/AgentSystemPromptLaunchTests/*"
dotnet run --project tests/Antiphon.Agents.Pty.Tests --property:OutputPath=bin-card0306-pty/ -- --treenode-filter "/*/*/ClaudeRemoteControlAtStartupCanaryTests/*"
```

Headed canary needs `ANTIPHON_HEADED_TESTS=1`. Delete the `bin-card0306*` directories afterwards (forward slash on OutputPath). `Antiphon.Tests` and `Antiphon.Agents.Pty.Tests` sequentially, never together.

---

## Sequencing and risks

**Order: S0 (local headed, recorded), then S1, then S2 docs.** S1 is blocked on S0 case 2. S2 can land with S1.

| Risk | Disposition |
|---|---|
| `--settings` does not override the org default | S0 case 2. Stop. Do not ship a no-op flag. |
| `--settings` **replaces** user/project settings (lost hooks, lost permission mode) | S0 case 4 smoke + residual. Overlay file contains **only** the one key so a merge is a no-op on everything else. If it replaces, orchestrator sessions lose user hooks — that is a ship-blocker; fall back to threading ApplyOff only onto pool delegates (`IsPoolDelegate` / blank `remoteControlName`) so standing orchestrators keep auto-connect. |
| Inline JSON shreds on Windows argv | Not used. File path. Integrity test. |
| Second `--settings` from a future profile | Helper merge arm + unit test. |
| RC-wanted `/remote-control` handshake (CARD-0240) becomes the only arming path and is flaky | Accepted: that is the original Antiphon model. CARD-0240 already bounds it. Follow-up (not this card): argv `--remote-control <name>` on fresh RC-wanted launches, and skip the typed send. |
| Fresh RC-wanted still auto-connects after S1 | S0-driven fallback: probe on fresh, skip send if armed, menu Esc already exists. Invert `Fresh_launch_never_probes_the_bridge`. |
| Live pool sessions already bridged | Out of scope; they retire. Execution brief may recycle the pool once after deploy. |
| Operator personal sessions | Untouched — we never write `~/.claude/settings.json`. |
| fakeclaude treats `--settings` path as a prompt | Teach fakeclaude to skip the flag, or pin that it already ignores unknown options. |

---

## Execution notes

- Run S0 on this machine before any production argv change. This is the same host the incident was observed on; a canary here is the actual default.
- After S1 deploys, one disposable pool-delegate (Worker/Code, no RC) in a scratch dir: JSONL must have no `bridge-session` before the brief. One disposable named Claude agent with `remoteControlEnabled: true`: `/remote-control` still arms (or CARD-0240 `RcDegraded` Warning + work prompt still delivered). Do not use Grok/Codex.
- If claude.ai still lists the incident session `98a6c91a-…`, that is CARD-0144 debris, not a failed fix.
