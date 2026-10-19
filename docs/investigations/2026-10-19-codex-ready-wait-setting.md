# CodexReadyMaxWaitMs: where it is read, and the 120000 proposal

Date: 2026-10-19. Lookup only. Nothing in this note was applied. No process was restarted.

Cited measurement: `docs/investigations/2026-10-18-codex-0160-ready.md` on `feat/card-task-13a42530` at `87fad6ed`. Codex 0.160.0 on server2 painted the ready footer at 51.47s cold. Session `e41748c6` was killed at 60.0s while the splash was still the last frame. A history wipe (task e59b0f15, CARD-1347) may shorten that cold start. 120000 is still the margin for a pass that is still on the splash at 60s.

## Outcome

Raise the value the **desktop Antiphon server** binds. A `Agents__CodexReadyMaxWaitMs` entry in `docker-compose.server2-runner.yml` does not move the gate. The server2 runner never binds `Agents`. The wait runs in the server process for every Codex launch, including pool launches and phone-home launches on server2.

Activation is an AppHost server restart after the server build that contains the new `server/appsettings.json` is what that host runs. A server2 image rebuild or rollout phase does not apply it. The session-runner process does not need a restart for this key.

## 1. Who reads `Agents:CodexReadyMaxWaitMs`

The options class is `AgentRegistrySettings` (`server/Application/Settings/AgentRegistrySettings.cs:63`):

```csharp
public int CodexReadyMaxWaitMs { get; set; } = 60000;
```

The tracked file repeats it (`server/appsettings.json:154`). The server host binds that section once, at process start (`server/Program.cs:303-305`):

```csharp
builder.Services.AddOptions<AgentRegistrySettings>()
    .Bind(builder.Configuration.GetSection("Agents"))
    .ValidateOnStart();
```

`AgentRegistrySettingsValidator` (`server/Infrastructure/Agents/Pty/AgentRegistrySettingsValidator.cs:86-87`) only requires the value to be positive. 120000 passes.

Production adapter construction is `AgentProtocolAdapterFactory.Create` (`server/Infrastructure/Agents/Pty/AgentProtocolAdapterFactory.cs:48-50`). Every `AgentKind.Codex` gets a `RunnerCodexAdapter` holding the server's `IOptions<AgentRegistrySettings>`. The in-process `CodexAdapter` (`server/Infrastructure/Agents/Pty/CodexAdapter.cs:12-13, 43-46`) reads the same property and is not constructed in production.

`RunnerCodexAdapter` copies `options.Value` in its constructor (`server/Infrastructure/Agents/SessionRunner/RunnerCodexAdapter.cs:40`) and passes it as the gate budget (`:156`):

```csharp
MaxWait = TimeSpan.FromMilliseconds(_settings.CodexReadyMaxWaitMs),
```

`CodexReadyWait.WaitAsync` (`src/Antiphon.Agents.Pty/CodexStartupReadiness.cs:435-460`) starts that budget at gate entry and returns false when it expires. The failure line is `codex-startup not-ready reason=... elapsedMs=...` (`:639-640`). The server logs it as a warning (`RunnerCodexAdapter.cs:162-167`). A ready latch logs `codex-startup ready reason=positive-settle elapsedMs=...` at Information (`:168-173`, emitted from `:596` and `:635`).

The caller is the server's `AgentSessionService.WaitForReadyOrThrowAsync` (`server/Application/Services/AgentSessionService.cs:2652-2665`). A false result throws `AgentLaunchBlockedException` or `InvalidOperationException` ("Agent process did not become ready."). `LaunchInteractiveProcessAsync` (`:478`, ready call at `:545`) is the interactive, pool, and delegate launch. Its catch (`:680-690`) calls `KillAndDisposeAsync`. That is the 60.0s kill. The same wait is used for a card start (`:220`), an interrupted-launch resume (`:967`), a running-brief reattach (`:1192`), and a card resume (`:2154`).

Pool launches are server-side. The factory does not fork on pool versus named versus phone-home. A remote runner id only swaps the terminal client (`AgentProtocolAdapterFactory.cs:39-41, 64-69`). The server polls snapshots over that client. The runner does not run `CodexReadyWait`.

`IOptions<T>` does not reload. Each new adapter sees the value captured when the server process started. Editing the file on disk does nothing until that process starts again.

`CodexReadyWaitOptions.MaxWait` (`CodexStartupReadiness.cs:394`) still defaults to 60 seconds. Production always overwrites it from `AgentRegistrySettings`. Leave the library default. Tests that omit `MaxWait` depend on it.

The session-runner host binds `SessionRunner`, `PhoneHome`, and `CodexCliVersion` (`src/Antiphon.SessionRunner/Program.cs:49-61`). It has no `Agents` section. Its baked `src/Antiphon.SessionRunner/appsettings.json` has none either.

## 2. How configuration reaches server2 today

`docker-compose.server2-runner.yml:1-4` is the standing deployment, and it runs no Antiphon server and no Postgres. The runner phones home to the desktop server. `docs/docker-stack.md` states the same (the persistent-deployment paragraph that begins "docker-compose.server2-runner.yml (project `antiphon-runner`)").

What the runner container actually receives:

- The `environment:` block in `docker-compose.server2-runner.yml:50-105`. That includes `CODEX_HOME`, `PhoneHome__CodexHome`, and `Agents__GrokCredentialProbeEnabled` (`:97`). The Grok probe flag is bound only by the server (`AgentRegistrySettings.GrokCredentialProbeEnabled`, read from `AgentTaskDispatcher` / `AgentTaskService`). The runner process never reads it. It is not evidence that `Agents__*` keys affect the runner.
- The image's own `src/Antiphon.SessionRunner/appsettings.json`, baked by `docker/session-runner-grok/Dockerfile`. No Agents keys.
- Volumes and read-only files: `/work`, `/state` (`runner-state`), the Codex home bind at `/state/codex`, git identity, GitHub token directory, Claude oauth file, phone-home secret, deploy key. None of these is an `appsettings` overlay.
- No user-secrets. AppHost user-secrets stay on the desktop.

`docker-compose.server2-runner.temp.yml` only overrides `PhoneHome__RunnerId` and the build-slots URL. It inherits the same unread `Agents__GrokCredentialProbeEnabled`.

The desktop server, which is the process that waits, is the AppHost `server` project (`Antiphon.AppHost/Program.cs:60-76`). AppHost forces `ASPNETCORE_ENVIRONMENT=Development` (`:66`) and does not set `Agents__CodexReadyMaxWaitMs`. Binding order for that process is `server/appsettings.json`, then a gitignored `server/appsettings.Development.json` if present, then user-secrets id `antiphon-server` (`server/Antiphon.Server.csproj`), then environment variables. This runner mirror cannot see the desktop secret store. A desktop overlay of `Agents:CodexReadyMaxWaitMs` would hide a tracked-file edit until it is removed. An environment variable on the server process outranks the overlay.

`docker-compose.yml` service `antiphon` (`:50-54`) overrides only the Grok and raw-sh definitions. It does not set `CodexReadyMaxWaitMs`, so a rebuilt server image would use the baked `server/appsettings.json`. That stack is not the desktop production server and not the server2 runner.

## 3. Smallest diff

Change the bound value in both places that production and `new AgentRegistrySettings()` read, plus the two assertions that pin the property initializer. Update the owner sentences in `docs/agent-kinds.md` so they state the budget the server will use. No documentation test pins those two sentences (`DockerStackDocumentationTests.Agent_kinds_doc_names_the_raw_allow_list` reads other phrases).

JSON binding overwrites the property initializer. A C# default change alone leaves the running server at 60000. A JSON change alone leaves `new AgentRegistrySettings()` at 60000, which `AgentRegistrySettingsTests` fails on.

```diff
--- a/server/Application/Settings/AgentRegistrySettings.cs
+++ b/server/Application/Settings/AgentRegistrySettings.cs
@@ -60,7 +60,7 @@ public class AgentRegistrySettings
     public int ClaudeDoneMaxWaitMs { get; set; } = 300000;
     public int CodexReadyQuietPeriodMs { get; set; } = 1000;
-    public int CodexReadyMaxWaitMs { get; set; } = 60000;
+    public int CodexReadyMaxWaitMs { get; set; } = 120000;

--- a/server/appsettings.json
+++ b/server/appsettings.json
@@ -151,7 +151,7 @@
     "ClaudeDoneMaxWaitMs": 300000,
     "CodexReadyQuietPeriodMs": 1000,
-    "CodexReadyMaxWaitMs": 60000,
+    "CodexReadyMaxWaitMs": 120000,

--- a/tests/Antiphon.Tests/Agents/AgentRegistrySettingsTests.cs
+++ b/tests/Antiphon.Tests/Agents/AgentRegistrySettingsTests.cs
@@ -398,7 +398,7 @@
     public void Codex_boot_threshold_binds_zero_default_and_custom_without_changing_ready_budget()
     {
         new AgentRegistrySettings().CodexBootStatusMaxWaitMs.ShouldBe(10_000, "V-3 default");
-        new AgentRegistrySettings().CodexReadyMaxWaitMs.ShouldBe(60_000, "V-3 ready budget unchanged");
+        new AgentRegistrySettings().CodexReadyMaxWaitMs.ShouldBe(120_000, "V-3 ready budget unchanged");
@@ -408,7 +408,7 @@
         var zero = ValidSettings(new AgentDefinition { Kind = "ClaudeCode", Exe = "cl.bat" });
         zero.CodexBootStatusMaxWaitMs = 0;
         new AgentRegistrySettingsValidator().Validate(name: null, zero).Failed.ShouldBeFalse("V-3 zero");
-        zero.CodexReadyMaxWaitMs.ShouldBe(60_000, "V-3 zero does not change ready budget");
+        zero.CodexReadyMaxWaitMs.ShouldBe(120_000, "V-3 zero does not change ready budget");

--- a/docs/agent-kinds.md
+++ b/docs/agent-kinds.md
@@ -504,7 +504,7 @@
 **Startup readiness (CARD-0574).** Cold Codex work is withheld until a positively identified
 loaded-model, empty-composer, model/effort/cwd-footer layout has settled for
-`CodexReadyQuietPeriodMs` (1 s) inside `CodexReadyMaxWaitMs` (60 s). An absent MCP marker, a quiet
+`CodexReadyQuietPeriodMs` (1 s) inside `CodexReadyMaxWaitMs` (120 s). An absent MCP marker, a quiet
@@ -514,7 +514,7 @@
 the readiness wait sends Escape once to skip it, then requires a fresh settled ready screen
 within the original deadline. The accepted CARD-0796 desktop run qualified task admission;
-the original 60-second readiness gate remains in force.
+the 120-second readiness gate remains in force.
```

Leave `AgentRegistrySettingsTests.cs:405` (`custom.CodexReadyMaxWaitMs = 60_000`). That line assigns a still-valid custom budget before the validator. It does not pin the default.

Leave these explicit fixture budgets. They do not read the production default:

- `tests/Antiphon.Tests/Agents/CodexAdapterLocalShellTests.cs:80` and `:214`
- `tests/Antiphon.Tests/Agents/CodexAdapterIntegrationTests.cs:71`
- `tests/Antiphon.Tests/Agents/CodexCommandLengthSessionTests.cs:151`

Do not add `Agents__CodexReadyMaxWaitMs` to `docker-compose.server2-runner.yml`. The runner would ignore it, and the rendered-config fixture `scripts/fixtures/c1105-compose-v218-config.json` plus the compose contract tests would have to move for a key that does not change the gate.

Do not add `.WithEnvironment("Agents__CodexReadyMaxWaitMs", "120000")` on the AppHost as the landed change. It would affect only a server launched by that AppHost, and it would leave `server/appsettings.json` and the property default at 60000. It remains a desktop-only emergency override if a restart is required before the tracked value ships. It still requires a server-process restart.

## 4. What must restart

Restart the desktop AppHost server (`scripts/restart-apphost.ps1` from the main checkout, after that checkout is on the build that contains the new `server/appsettings.json`). Product hosts track the current `rc/YYYY-MM-DD` tip. A land publishes the commit. The restart is what loads it. Confirm `GET /api/version` SHA equals that server build.

Before trusting the tracked 120000, confirm the desktop has no `Agents:CodexReadyMaxWaitMs` in user-secrets id `antiphon-server` and no such key in gitignored `server/appsettings.Development.json`. Either outranks `server/appsettings.json`.

Not required:

- Session-runner restart on the desktop (port 17204) or on server2.
- A server2 image rebuild.
- Any server2 rollout phase (`deploy-temp`, `redeploy-old`, drains). Those recreate the runner container. The runner still does not bind `Agents`.

## 5. Verification

After the server process is on the new value, cold-launch Codex on runner `server2`. The 2026-10-18 probe argv is the production delegate shape:

`codex --no-alt-screen --dangerously-bypass-approvals-and-sandbox -m <model> -c model_reasoning_effort=xhigh -c disable_paste_burst=true -c check_for_update_on_startup=false`

A real delegate launch on server2 is the same clock. A warm launch is not the test: the 2026-10-18 warm footer was 1.18s.

Read the **desktop server** log, not the runner log. Pass:

`Session <id> codex-startup ready reason=positive-settle elapsedMs=<n>`

with `<n>` above 60000 and the session left `Running`. That is a cold start the old gate would have killed, accepted inside 120000.

The old failure, if the server is still on 60000:

`Session <id> codex-startup not-ready reason=<...> elapsedMs=60000`

followed by the launch catch killing the session. `elapsedMs` is the server's gate clock, including snapshot round trips. The on-screen footer time is shorter than that clock by the 1s settle and the poll lag.
