# PS1 to C# migration plan (CARD-1514 umbrella)

- Base: master `5f0804e425384f8b03f88b833fbdad0e93f966d3` (2026-11-19).
- Stage: Plan (task f43b0b69). Read-only investigation plus this document. No code changed.
- Operator direction (2026-11-19): every PowerShell script becomes C#. Scripts end thin or absent,
  and callers invoke a published exe (or `dotnet`) directly. Windows PowerShell 5.1 compatibility
  is not a design constraint for C#. The operator-run desktop helpers stay 5.1-safe only until
  they are ported.
- Every count below comes from this checkout. The reproduction commands are in
  [Appendix B](#appendix-b-how-the-numbers-were-produced). UNKNOWN means not established here.

## Summary

The repository tracks **427** `.ps1`/`.psm1` files with **142,248** lines
(`git ls-files '*.ps1' '*.psm1'`). Of these:

- 222 files are run by C# tests (their name appears in `tests/**/*.cs`). Those files hold
  94,587 lines.
- 141 files are PowerShell test drivers (`scripts/test-*.ps1`, `scripts/c<nnn>-*`) with
  59,608 lines.
- 46 files are nightly/rc libraries under `scripts/lib`, with 27,529 lines.

The migration already started in two places:

- `scripts/lib/Antiphon.Nightly` (7,184 C# lines, 43 commits since 2026-11-07) is a library that
  PowerShell builds into `%TEMP%/antiphon-nightly-core/<hash>` and loads with `Add-Type`
  (`scripts/lib/nightly-core.ps1`).
- `scripts/lib/Antiphon.Scripts` (2,671 lines) is already a command-line exe (`select-release`)
  that the PowerShell calls, falling back to PowerShell on exit 10 (`NotHandled`).

This plan inverts that hybrid. **One CLI, `tools/Antiphon.Cli` (command `antiphon`), owns the
flow, and each ps1 becomes a 5-line launcher and then disappears.** The CLI is published per host
and per source identity into a user cache, so no call pays for an SDK build. The plan has three
waves:

- **Wave 1:** 12 slices of 1–3 hours each. Foundation, then the nightly pin and Windows
  PowerShell 5.1 pain, then checkpoint and build-slot, then the small HTTP tools, then card and
  delegate behind a parity harness.
- **Wave 2:** a family backlog of about 95 slices.
- **Wave 3:** the desktop helpers, with secret custody last.

About 20 files must stay PowerShell, each for a stated reason (section 6).

## Decisions

Each decision is stated as a default that the operator or Review may overturn.

- **D-1 One multi-command CLI.** Add `tools/Antiphon.Cli` (AssemblyName `antiphon`), with
  subcommands named after the family (`antiphon status-snapshot`, `antiphon card get`,
  `antiphon nightly input-digest`, `antiphon build-slot -- <cmd>`).
  - Why: one publish, one identity, one launcher and one HTTP client for 427 scripts. The repo
    already treats `Antiphon.Scripts` as a CLI with this shape.
  - Kept separate: `tools/Antiphon.Checkpoints` (detached executor, shadow-copies itself, packs
    as a dotnet tool, has its own exit-code contract) and `tools/Antiphon.LandGraphAudit` (needs
    MSBuildLocator against the SDK, so it cannot be a standalone single-file exe).
  - `scripts/lib/Antiphon.Scripts` moves into the CLI during W2-nightly.
  - `scripts/lib/Antiphon.Nightly` drops its `PowerShellStandard.Library` compile reference and
    its PSObject interop once no ps1 loads it, and becomes a plain library the CLI references.
  - Rejected:
    - One exe per script: N publishes, N identities, N drift checks.
    - Folding everything into `Antiphon.Checkpoints`: couples board/delegate releases to the
      checkpoint executor.
    - Continuing the `Add-Type` hybrid: it keeps both languages, both fallbacks and the 5.1 copies
      alive (`nightly-core.ps1` header).
- **D-2 PowerShell-compatible argument binder.** Add an in-house `PsArgs` binder with these rules:
  - case-insensitive `-Name value` and `-Name:value`;
  - switches;
  - unique-prefix matching;
  - declared positionals;
  - comma lists delivered as `pwsh -File` delivers them (strings).
  - Why: launchers forward raw tokens, and existing callers use PowerShell syntax. `delegate.ps1`
    is referenced in 269 doc files and 20 server C# files; `server/Application/Services/LandApproval.cs:19`
    emits `pwsh -NoProfile -File scripts/delegate.ps1 -Land ...`.
  - Rejected: System.CommandLine. It is not used anywhere in the repo, it has no PowerShell
    prefix semantics, and adding it needs a new NuGet pin under CARD-1229.
- **D-3 CLI-local wire records, proven by a wire-parity test.** One `AntiphonApiClient` handles
  `ANTIPHON_API`, the task token, the capability token and `X-Antiphon-Caller`. Request and
  response records are CLI-local. A test in `Antiphon.Tests` serializes the real server DTOs with
  the server's JSON options and binds them into the CLI records.
  - Why: the DTOs (`server/Application/Dtos`, 122 files) import `Antiphon.Server.Domain.Enums`
    (54 using lines) and `Domain.Entities`. Referencing them drags the server assembly (ASP.NET,
    EF) into the CLI.
  - Rejected: referencing `Antiphon.Server`; linking DTO sources (domain dependencies, and a
    wildcard Compile glob breaks the checkpoint coverage verb).
  - Deferred: extracting an `Antiphon.Api.Contracts` project. That is its own card.
- **D-4 Framework-dependent, RID-specific, single-file, ReadyToRun publish into a per-user cache.**
  RIDs are `win-x64` and `linux-x64`.
  - Why: every host that runs sessions already has the .NET 10 SDK and runtime (`global.json`
    10.0.204; server2 runtimes 9.0.23 and 10.0.15; the runner image is built from
    `mcr.microsoft.com/dotnet/sdk:10.0`). The output is small and fast to start.
  - Measured on server2: the cached framework-dependent `Antiphon.Scripts` apphost started in
    146–439 ms, against 616–715 ms for `pwsh -NoProfile`.
  - Self-contained stays an option (`-p:SelfContained=true`) for a pre-runtime host, but nothing
    in waves 1–2 needs it.
  - Rejected:
    - `dotnet run` per call: MSBuild evaluation, plus a build-slot lease per call under CARD-0589.
    - Self-contained per identity: tens of MB per source change per host.
    - Native AOT: needs a native toolchain per OS, cannot cross-compile, and restricts
      reflection-based JSON.
    - Checked-in binaries: repository bloat and no provenance.
    - Server-served binaries: the server runs the rc tip, and a worktree needs its own source.
  - TFM: `net10.0` with `RollForward=Major`, the same `RollForward` as `Antiphon.Checkpoints`.
- **D-5 Source identity is computed with git alone.** The identity is
  `git ls-files -s -- <inputs> | git hash-object --stdin` plus the RID and the SDK major.
  - `<inputs>` is a committed list, `tools/Antiphon.Cli/cli-inputs.txt`: the project directory,
    its ProjectReference closure, `Directory.Build.*`, `global.json` and `NuGet.Config`.
  - A test holds that list equal to the real project closure.
  - With uncommitted changes under the inputs, the identity also covers the working-tree blobs
    (`git hash-object` of each modified or untracked input).
  - Why: `sh`, `cmd` and 5.1 PowerShell can all compute this with only `git`.
  - Rejected: hashing files in PowerShell, as `nightly-core.ps1` does today. It is slow and not
    available to the `sh`/`cmd` launchers.
- **D-6 Front-door scripts stay as launchers until their references move.**
  - The names stay: `delegate.ps1`, `card.ps1`, `status-snapshot.ps1`, `checkpoint-task.ps1`,
    `build-slot.ps1` and others. A final docs/server-text sweep moves the references to
    `scripts/antiphon <cmd>`, and only then are the launchers deleted.
  - The `X-Antiphon-Caller` labels do not change (`card.ps1`, `delegate.ps1`, ...). That keeps
    `AgentTaskGetPollMetrics.RegisteredLabels` and its tests stable.
- **D-7 Parity window.** The porting slice copies the old implementation byte-for-byte to
  `scripts/legacy/<name>.ps1`, with its dot-source closure.
  - Parity tests run the legacy copy and the CLI against the same stub server and compare the
    exit code, the request log, and normalised stdout/stderr.
  - Each command declares its divergences, with a reason for each.
  - The family's retire slice deletes `scripts/legacy/**`. The parity tests then become C#-only
    behaviour tests.
  - Rejected: parity against `git show <base>:path`. It fails on shallow runner mirrors.
- **D-8 Wave-1 CLI tests live in `tests/Antiphon.Tests/Cli/`.** `Antiphon.Tests` references
  `tools/Antiphon.Cli` the way it already references `tools/Antiphon.Checkpoints`
  (`Antiphon.Tests.csproj:91`).
  - Why: a new test project needs `tests/test-selection-policy.json` sets/`remainderProjects`.
    That reseals `catalogHash`, which moves `tests/test-execution-policy.json`
    `selectionCatalog.hash`, which moves `policyHash`, which moves the live-lane
    `PinnedInputDigest`.
  - A dedicated `tests/Antiphon.Cli.Tests` comes later, batched with D-10.
- **D-9 Pin recipe.**
  - S2 makes today's `PinnedInputDigest` computable byte-identically by
    `antiphon nightly input-digest`. Nothing pinned changes in S2.
  - S4 narrows the pinned inputs to the census C# sources plus a census projection of
    `tests/test-execution-policy.json`, and drops the whole file.
  - This is the deliberate, reviewed act `scripts/test-live-lane-run.ps1:12-15` demands, and
    Review must approve it.
  - Rejected: pinning the whole policy JSON. It was 91 of the 158 commits touching the pinned
    inputs since 2026-10-19.
- **D-10 One forward-looking selection edit.** S3 extends the `P-AUTHORITY` pattern in
  `tests/test-selection-policy.json` once, adding `tools/Antiphon\.Cli/(Nightly|Release|Checkpoint|BuildSlot)/.*`.
  That is one reseal chain instead of one per slice.
- **D-11 Kept ps1 stay ASCII and 5.1-safe.**
  - This covers the section-6 files and the launcher helper `scripts/lib/antiphon-cli.ps1`.
    Windmill and Task Scheduler start them under `powershell` 5.1.
  - C# carries no 5.1 constraint.
  - The launcher's parse under 5.1 needs one `-Platform Windows` witness row.
- **D-12 Order.** Foundation → nightly pin and 5.1 → receipt validator and build-slot → small HTTP
  tools → card → delegate (only after the parity harness has proven itself on four commands) →
  wave-2 families → desktop helpers, with secret custody last.
- **D-13 Cache location.**
  - Windows: `%LOCALAPPDATA%\Antiphon\cli\<rid>\<identity>\`.
  - Linux: `${XDG_CACHE_HOME:-$HOME/.cache}/antiphon/cli/<rid>/<identity>/`, created mode `0700`.
  - `ANTIPHON_CLI_CACHE` overrides the cache. `ANTIPHON_CLI_PATH` (an exe or `.dll`) bypasses it
    for tests.
  - Not `/tmp`: there is a live server2 `/tmp` custody investigation (`3f24857e8`), and a /tmp
    sweep could take the cache away. Not in the repo: the cache would be per-worktree and would
    dirty the source tree.

## Ground truth: what the brief assumes vs what the code does

| # | Brief assumes | Code does (evidence) |
|---|---|---|
| G1 | "the Antiphon.Nightly C# core already exists" | True, but it is a PowerShell-hosted library. It is built per source hash into `%TEMP%/antiphon-nightly-core`, loaded with `Add-Type`, and compiled against `PowerShellStandard.Library` (`scripts/lib/Antiphon.Nightly/Antiphon.Nightly.csproj`, `scripts/lib/nightly-core.ps1:15-91`). It cannot load under 5.1, so `nightly-run.ps1` and `release-cut.ps1` keep PowerShell copies (`nightly-core.ps1:1-13`). A second C# exe already exists: `scripts/lib/Antiphon.Scripts` (`select-release`, exit 10 = NotHandled). |
| G2 | "dotnet run is slow" | Direction confirmed and partly measured on server2 at load 21–24 on 24 cores: `pwsh -NoProfile -Command exit` took 616/700/715 ms; pwsh plus dot-sourcing `nightly-common.ps1` and `nightly-coverage.ps1` took 1,182/1,313 ms; the cached `Antiphon.Scripts` apphost took 146/153/439 ms. **UNKNOWN:** `dotnet run --no-build` and cold `dotnet publish` durations (S1 measures them). Under CARD-0589 any build takes a slot, so a per-call build is ruled out. |
| G3 | "PinnedInputDigest pins churn on every nightly-area landing" | The digest is over 7 paths (`scripts/test-live-lane-run.ps1:24-28`): `nightly-common.ps1`, `nightly-policy.ps1`, `nightly-coverage.ps1`, `nightly-core.ps1`, `Antiphon.Nightly/NightlyCore.cs`, `tests/test-execution-policy.json` and `scripts/fixtures/nightly/c487-probe/discovery.diag`. Since 2026-10-19, 158 master commits touched them: 91 the policy JSON, 64 the 4 ps1, 11 `NightlyCore.cs`. The pin line was resealed 63 times. **Porting ps1 alone removes at most the ps1 share; the JSON share needs D-9.** |
| G4 | "policyHash" is a ps1-fed pin | `policyHash` is a self-hash of the canonical JSON of `tests/test-execution-policy.json` minus its own field (`scripts/lib/nightly-policy.ps1:53-71`, canonicaliser in `nightly-common.ps1`). ps1 feeds it as code, not as a hashed input. A C# port must reproduce the canonical bytes, or every stored hash goes stale. |
| G5 | "operator-run desktop helpers" are the 5.1 constraint | The 5.1 runtime entry points are wider: Windmill runs `powershell -NoProfile -ExecutionPolicy Bypass -File ...` for `nightly-run.ps1`, `nightly-health.ps1` and `release-cut.ps1`, plus two snapshot wrappers (`scripts/windmill/*.json`). `install-autostart.ps1:133-149` falls back to `powershell.exe`. The delegate skill says to use `powershell` when `pwsh` is missing (`.claude/skills/antiphon-delegate/SKILL.md:26`). 201 files state a 5.1/ASCII contract in their text; 122 are pwsh-7-only (`#requires -Version 7` or 7-only APIs). |
| G6 | delegate.ps1/card.ps1 are the board's front door | Confirmed. In addition, the server emits `scripts/delegate.ps1` command text (20 C# files, for example `LandApproval.cs:19`, `DelegationReportFormatter.cs:797`), and `CheckpointReceiptValidator.cs:242` **spawns `pwsh`** at runtime. |
| G7 | First slice must not touch what the live rc Windows run uses | The rc run executes its own checkout at the candidate SHA (`scripts/qualification/launch-rc.ps1` refuses unless HEAD == expectedSha). A master change reaches it only through an rc cut or port. S1 touches no nightly, rc or qualification file. |
| G8 | A parity harness must be built | Most of it exists. `tests/Antiphon.Tests/Application/DelegateScriptRunner.cs` runs the real `delegate.ps1` against a test-owned API in 20 test files. `Stub`/`HeaderStub` HTTP fixtures exist in `CardListSearchScriptTests` and `RegisteredCallerScriptTests`. 166 `ScriptHarness.RunHarnessCaseAsync` call sites run ps1 harness cases. |
| G9 | Share API client types with the server | No client library exists, and the DTOs depend on the domain (G6/D-3). |
| G10 | 8–12 slices of 1–3 h | That covers wave 1 (about 11k lines). The full surface needs about 110 Code tasks (section 8). |
| G11 | Tests shelling out to ps1 cost time | Windows class costs (`scripts/lib/nightly-class-costs.json`): the 72 `Antiphon.Tests.Scripts.*` classes total 220 serial minutes of 2,833, and 61 measured classes total 48 minutes. `CardListSearchScriptTests` measured 42 s for 11 nodes (≈3.8 s per test). Every pwsh-spawning class is on the 1-wide `ProcessSpawnLimit` lane or the `ScriptModelSpawnLimit` lane. |
| G12 | The derived-impact index and pins key on ps1 | 80 of the 2,412 derived-impact edges key on ps1 paths, and none of the 155 capped paths do. `tests/impacted-classes.json` names 5 ps1 paths. The `test-selection-policy` `P-AUTHORITY` regex names nightly/release ps1 (full rc). |

## 1. Inventory

### 1.1 Totals by role

| Role (path family) | Files | Lines |
|---|---:|---:|
| `scripts/*.ps1` operational (orchestrator, operator, deploy, ops) | 141 | 40,513 |
| Test drivers (`scripts/test-*.ps1`, `scripts/c<nnn>-*`, `scripts/card<nnnn>*`) | 141 | 59,608 |
| Nightly/rc/release/qualification libraries (`scripts/lib/{nightly,test-phase,live-lane,rc-,release,qualification,c487,test-selection,test-timings,toolchain}*`) | 46 | 27,529 |
| Other `scripts/lib/*.ps1` | 19 | 4,424 |
| Test fixtures (`scripts/fixtures/**`, `tests/**`) | 42 | 7,251 |
| Root dev-stack scripts | 20 | 1,140 |
| `scripts/qualification/*` | 7 | 842 |
| `scripts/orchestration/*` | 4 | 262 |
| `scripts/windmill/*` | 3 | 470 |
| Historical evidence (`docs/investigations/**`, `.antiphon/**`) | 4+1 | 226 |
| **Total** | **427** | **142,248** |

### 1.2 Host (Windows PowerShell 5.1 vs pwsh 7)

- **pwsh 7 only:** 122 files, 36,534 lines. They carry `#requires -Version 7` (103 files) or use
  7-only APIs such as `::HashData`, `::ToHexString`, `-AsHashtable`, `IsPathFullyQualified` or
  `??` (19 files).
- **States a 5.1/ASCII contract:** 201 files, 93,230 lines. The text names
  "Windows PowerShell 5.1", "5.1" or "ASCII-only". This is a contract claim, not proof that 5.1
  ever runs them.
- **States nothing:** 104 files, 12,484 lines. Nine files contain non-ASCII bytes (five root
  `dev-*.ps1`, `verify-dev-stack.ps1` and three others).
- **Real 5.1 launches** (G5):
  - Windmill entries: `nightly-run.ps1`, `nightly-health.ps1`, `release-cut.ps1` and the
    snapshot wrappers.
  - Scheduled tasks (`install-autostart.ps1` prefers pwsh 7 and falls back to 5.1):
    `autostart-apphost.ps1`, `autostart-session-runner.ps1`, `watchdog-apphost.ps1`,
    `apphost-watchdog-state-observer.ps1`, and `cleanup-build-junk.ps1` via
    `install-cleanup-task.ps1`.
  - `bootstrap-check.ps1`, documented as `powershell.exe -File`.
  - Agents with no `pwsh` on PATH.
  - `C1514NativeProbe` in tests runs `powershell.exe -File scripts/summarize-test-phases.ps1`.

### 1.3 Who calls them (summary; per file in Appendix A)

- **Orchestrator and agent tooling** (skills, AGENTS.md and server-emitted text):
  - `delegate.ps1`: 1,593 lines; 269 doc files and 20 server C# files reference it.
  - `card.ps1`: 1,191 lines; 307 doc files.
  - `status-snapshot.ps1` (192), `checkpoint-task.ps1` (206), `keep-six.ps1` (134),
    `capability.ps1` (165), `dispatch-concurrency.ps1` (201), `runner-slots.ps1` (67).
  - `build-slot.ps1` plus `lib/build-slot.ps1`: 428 lines; 626 doc references.
  - `run-checkpoint.ps1` (1,520), `impacted-classes.ps1` (1,375),
    `validate-checkpoint-receipt.ps1` (208).
- **Server runtime:**
  - `CheckpointReceiptValidator.cs:242` spawns `pwsh` to run `validate-checkpoint-receipt.ps1`.
  - `WorkspaceHookRunner.cs:121` runs hooks with `powershell.exe` (inline; not a ps1 file).
  - `DelegationWorktreeService.cs:527` uses an inline `powershell -NoProfile -Command` deny hook
    (not a ps1 file).
- **Operator on the desktop:** `desktop-win-phase1-secrets.ps1` (1,035, secret custody),
  `desktop-slot-provision.ps1` (484), the restart/autostart family and the root `dev-*.ps1`.
- **Test and qualification drivers:** 141 `scripts/test-*.ps1`, run by `Antiphon.Tests` through
  `ScriptHarness`. `scripts/qualification/*` (7 files, 842 lines) launch and observe the rc run.
  `scripts/orchestration/watch-*.ps1` are the watchers.
- **Nightly:** `nightly-run.ps1` (Windmill, 5.1) → `lib/nightly-run-impl.ps1` → spawns
  `pwsh nightly-tests.ps1` → `lib/nightly-tests-impl.ps1`, `lib/test-phase-evidence.ps1` and the
  `Antiphon.Nightly` core.
- **CI** (`.github/workflows`, `shell: pwsh`): `check-evidence-diff.ps1`,
  `test-server-docker-inputs.ps1`, `check-server-docker-inputs.ps1`.
- **Container images:** `docker/session-runner-grok/Dockerfile` and `docker/tests/Dockerfile`
  install pwsh 7.5.4. The root `Dockerfile` copies `scripts/lib/*.cs`.

### 1.4 Pinned inputs of the nightly

- `PinnedInputDigest` (`scripts/test-live-lane-run.ps1:24-28`, printed by `-PrintInputDigest`)
  covers four ps1, `NightlyCore.cs`, the policy JSON and one fixture (G3). It is resealed when any
  of them changes (63 times since 2026-10-19).
- `policyHash` is code-coupled to `nightly-policy.ps1` and `nightly-common.ps1` (G4).
- `catalogHash` (`tests/test-selection-policy.json`) is pinned as `selectionCatalog.hash` inside
  the policy JSON. So any selection-rule edit chains into `policyHash` and `PinnedInputDigest`
  (D-8/D-10).
- The land-graph digest is a separate identity (`docs/orchestration-loop.md` rule 11). It moves
  when a selected build-definition record changes, for example a new csproj in `Antiphon.sln`.

### 1.5 Most-churned ps1 since 2026-10-19 (master commits)

| ps1 | Commits |
|---|---:|
| `scripts/test-live-lane-run.ps1` | 74 |
| `scripts/lib/nightly-tests-impl.ps1` | 67 |
| `scripts/test-nightly-failed-first.ps1` | 63 |
| `scripts/lib/test-phase-evidence.ps1` | 54 |
| `scripts/test-rc-platform.ps1` | 52 |
| `scripts/test-nightly-tests.ps1` | 48 |
| `scripts/test-release-gate.ps1` | 40 |
| `scripts/audit-land-verification-graph.ps1` | 40 |
| `scripts/lib/nightly-coverage.ps1` | 39 |

Totals for the window: 1,234 of 6,446 master commits touched at least one ps1.

## 2. Target architecture

### 2.1 Projects

```text
tools/Antiphon.Cli/                 exe "antiphon" (net10.0, RollForward=Major)
  Program.cs                        command table, exit-code contract, version
  Cli/PsArgs.cs                     D-2 binder
  Cli/Output.cs                     stdout/stderr writers (no 80-col wrap; parity normalisation documented)
  Http/AntiphonApiClient.cs         ANTIPHON_API, X-Antiphon-Task-Token, X-Antiphon-Caller (CallerLabels.cs)
  Process/                          reuse scripts/lib/CheckpointProcessRunner.cs (linked, as Antiphon.Scripts does)
  Commands/<Family>/<Command>.cs    one file per ported script verb
  cli-inputs.txt                    D-5 identity inputs
tools/Antiphon.Checkpoints/         unchanged (gains `validate` parity in S6, `row` replaces run-checkpoint.ps1 in W2)
tools/Antiphon.LandGraphAudit*/     unchanged (SDK-bound)
scripts/lib/Antiphon.Nightly/       becomes a plain library referenced by Antiphon.Cli (W2-nightly drops PowerShellStandard)
scripts/lib/Antiphon.Scripts/       commands move into Antiphon.Cli (W2-nightly); project then deleted
scripts/antiphon                    POSIX sh launcher
scripts/antiphon.cmd                cmd launcher
scripts/lib/antiphon-cli.ps1        ASCII, 5.1-safe launcher used by ps1 front doors
scripts/legacy/**                   frozen pre-port copies during each parity window (D-7)
```

- **Shared code with the server:** `Antiphon.HostCleanup` (Windows job objects, already referenced
  by `Antiphon.Checkpoints`), `Antiphon.IO.WriteBehind` and `Antiphon.Hosting/PwshPrivateCache.cs`
  (only while the CLI still spawns pwsh). No reference to `Antiphon.Server` (D-3).
- **Exit codes:** each ported command keeps its script's documented codes. For example, a ported
  `run-checkpoint` keeps 0/1/2/3/8, and `status-snapshot` keeps 0/2. Launcher failures use a
  reserved code, `antiphon-cli-unavailable` = exit 9 with one stderr line. Code 9 is not used by
  the S1–S12 scripts; each slice re-checks that.

### 2.2 How callers invoke it

1. **In wave 1, callers do not change.** `pwsh -NoProfile -File scripts/delegate.ps1 ...` reaches
   a launcher:

   ```powershell
   # scripts/status-snapshot.ps1 - launcher. Implementation: tools/Antiphon.Cli (antiphon status-snapshot).
   . "$PSScriptRoot/lib/antiphon-cli.ps1"
   exit (Invoke-AntiphonCli -Command 'status-snapshot' -Arguments $args)
   ```

2. **New callers** use `scripts/antiphon <cmd>` on Linux or `scripts\antiphon.cmd <cmd>` on
   Windows, or the resolved exe directly. `antiphon which` prints the path for scripts that want
   to cache it.
3. **The launcher, in all three variants:**
   1. If `ANTIPHON_CLI_PATH` is set, it uses that path: `dotnet exec <dll>` for a `.dll`, or the
      exe itself.
   2. It computes the identity (D-5).
   3. If `<cache>/<rid>/<identity>/identity` matches, it execs the exe, forwarding the argv
      untouched, the exit code and the stdio.
   4. On a miss, it takes `<cache>/<rid>/<identity>.lock` and re-checks.
   5. It publishes through the build-slot gate:
      `build-slot -Label antiphon-cli-publish -- dotnet publish tools/Antiphon.Cli -c Release -r <rid> --self-contained false -p:PublishSingleFile=true -p:PublishReadyToRun=true -p:AntiphonCliIdentity=<id> --artifacts-path <cache>/build-<guid>/artifacts -o <cache>/build-<guid>/out/`.
      `--artifacts-path` keeps `obj/` out of the worktree, as `Install-NightlyCoreBuild` already
      does. The output path ends with a forward slash.
   6. It writes `identity` last, renames the directory atomically into place, and execs the exe.
4. **No SDK, a failed publish, or a slot timeout (exit 4):**
   - During a family's parity window, the launcher runs `scripts/legacy/<name>.ps1` and prints one
     `antiphon-cli-fallback reason=<r>` stderr line.
   - After the window it exits 9 with the reason.
   - It never retries unleased and never uses `-NoSlot` (CARD-0589).
5. **Refresh:** a source change makes a new identity, which makes one publish per host. All
   worktrees and runner mirrors on that host that share the identity reuse it, so a session pays
   at most one publish per CLI change, not one per call.
6. **Garbage collection:** best-effort deletion of identity directories untouched for 14 days,
   skipping any in use (Windows locks the running exe).
7. **Version stamp and drift:**
   - `antiphon --version` prints `antiphon <identity> <git-sha> <rid> <runtime>`.
   - `InformationalVersion` carries the same values.
   - The launcher compares the `identity` file before exec.
   - HTTP calls send `User-Agent: antiphon-cli/<identity>`. A server-side drift view is optional
     and needs no server change in wave 1.
   - The registered `X-Antiphon-Caller` labels stay the script names (D-6).

### 2.3 Hosts

The platform reads come from `GET /api/runner-defaults` and `GET /api/session-runners` at
2026-11-19T20:30Z:

- `globalRunnerId` is `server2`.
- `server2`: linux, capacity 10, 8 occupied at read time.
- `desktop`: windows, capacity 2.
- `server2-temp` is retired.

The cache is per host: server2 runner mirrors share `~app/.cache/antiphon/cli`, and the desktop
uses `%LOCALAPPDATA%`. **UNKNOWN:** whether the server2 runner's home directory survives a runner
image rollout. If it does not, the first call after a rollout republishes. This is a cost, not a
correctness problem.

## 3. Bootstrap

- **A session with no built exe still works.** The front-door ps1 stay as launchers (D-6), and the
  launcher publishes on first use. The only prerequisites are `git`, a .NET SDK and a build slot,
  and all three exist wherever a session can build code. While a family's legacy copy exists, a
  launcher failure degrades to the old PowerShell, so the board stays reachable.
- **Board access when publish is impossible after the legacy window closes:** the launcher exits 9
  with an actionable message, for example `install the .NET SDK from global.json, or set
  ANTIPHON_CLI_PATH`. Every server2 and desktop session already builds .NET, so the only exposed
  case is a fresh machine. That is `bootstrap-check.ps1`'s job, and it stays PowerShell
  (section 6).
- **The build-slot lease used to publish the CLI must not need the CLI.** S7 ports `build-slot.ps1`
  into `antiphon build-slot`. The launcher therefore keeps a minimal lease client in
  `scripts/lib/antiphon-cli.ps1` (pwsh/5.1) and in `scripts/antiphon` (sh, using `curl`), speaking
  the runner's lease API. The `cmd` launcher shells to the ps1 helper.
  - Rejected: bootstrapping from a previously cached CLI ("bootstrap by predecessor"). It runs
    stale code exactly when the lease protocol changes.
- **The operator desktop secret helper is ported last, as W3-desktop's final slice.** Steps:
  1. Port to `antiphon desktop secrets-phase1`, keeping the dry-run default. `-Execute`,
     `-Rotate` and `-WhatIf` keep their meanings.
  2. Port the test seams `DW3_FAIL_STEP`, `DW3_REPLACE_FAULT`, `DW3_RESTORE_FAULT` and
     `DW3_MODE_AT_WRITE` as environment seams with the same names.
  3. Convert the witnesses in `scripts/test-desktop-win-phase1-secrets.ps1` (773 lines) to
     Windows-native TUnit tests. They get a runtime `OperatingSystem.IsWindows()` skip and a
     Linux rc exclusion row (CARD-1811), and run on the desktop lane (`-Platform Windows`).
  4. Run parity on the desktop in dry-run mode only. Point the script's own path overrides
     (`-UserSecretsPath`, `-SecretFile`, `-ProfileHome`, `-Phase1ConfigPath`) at scratch
     directories, never at real secrets. Compare the REFUSED/changed lines and the produced
     files' ACLs and bytes (scratch secret only).
  5. The operator runs the new dry-run before any `-Execute`.
  6. The old helper stays in `scripts/legacy/` for one rc cycle.
  7. The secret never reaches argv or a child process; this design is unchanged.

## 4. Migration order

Each slice is sized for one Code task of 1–3 hours. A slice is done only when:

- the ported command passes its parity tests (D-7);
- the ps1 is a launcher;
- `scripts/legacy/` holds the old copy;
- `tests/derived-impact-index.json` is regenerated
  (`scripts/regenerate-derived-impact-index.ps1`);
- for a csproj/sln change, `scripts/land-graph-land-refresh.ps1` has run and
  `scripts/audit-land-verification-graph.ps1 -Check` exits 0;
- `scripts/impacted-classes.ps1 -Range <base>..<tip> -Json` has been run on the diff.

### 4.1 Wave 1 (12 slices)

| Slice | Ports | Files | Witnesses / tests | ps1 becomes | Risk | Depends |
|---|---|---|---|---|---|---|
| **S1** Foundation + `status-snapshot` | CLI host, PsArgs, API client, caller labels, launchers ×3, identity/cache/publish, `status-snapshot` | new `tools/Antiphon.Cli/**`, `scripts/antiphon`, `scripts/antiphon.cmd`, `scripts/lib/antiphon-cli.ps1`, `scripts/legacy/status-snapshot.ps1`; changed `scripts/status-snapshot.ps1`, `Antiphon.sln`, `tests/Antiphon.Tests/Antiphon.Tests.csproj`, `tests/rc-project-categories.json` (`tools/Antiphon.Cli/Antiphon.Cli.csproj: ["tooling"]`), new `docs/antiphon-cli.md` | new `AntiphonCliBinderTests`, `AntiphonCliCallerLabelTests` (C# list == `AgentTaskGetPollMetrics.RegisteredLabels` == `antiphon-caller.ps1` list), `AntiphonCliInputsTests` (D-5 list == closure), `AntiphonCliLauncherTests` (fake `dotnet` shim: hit, miss → publish under slot, lock, fallback, exit 9), `AntiphonCliStatusSnapshotParityTests`; changed `StatusSnapshotScriptTests`, `RegisteredCallerScriptTests` (set `ANTIPHON_CLI_PATH` to the staged dll) | launcher | Medium: launcher correctness on 3 shells | none |
| **S2** Live-lane input digest in C# | `Get-ProtectedInputDigest` | `Commands/Nightly/InputDigestCommand.cs`; `scripts/test-live-lane-run.ps1` `-PrintInputDigest` calls it | parity: C# hex == ps1 hex at the base and on a CRLF copy; mutation (one changed byte) flips both | unchanged script, digest delegated | Low: no pinned input edited | S1 |
| **S3** Policy reader + canonical JSON | `nightly-policy.ps1` (537) reading and hashing; `ConvertTo-NightlyCanonicalJson` from `nightly-common.ps1` | `Commands/Nightly/Policy*.cs` (or into `Antiphon.Nightly`); `nightly-policy.ps1` functions forward to the CLI; D-10 selection-rule edit | parity on the real policy file (stated `policyHash` == C# hash == ps1 hash) and on fixtures; `test-nightly-core.ps1` cases that touch policy | thin forwarding functions (still dot-sourced) | High: pinned input → one deliberate reseal (pin, `catalogHash`, `selectionCatalog.hash`, `policyHash`) | S1, S2 |
| **S4** Census pin narrowing (D-9) | census pieces of `nightly-coverage.ps1` and `nightly-core.ps1` | `Antiphon.Nightly/Census/*.cs`; `scripts/test-live-lane-run.ps1` `ProtectedInputs` re-keyed | `C1545_*` full-inputs and sink-audit cases green; new digest documented with the hunk classification of the 64 ps1 commits (how many would still have moved it) | census functions forward | High: changes what the paid live-lane commission pins. Review must approve | S3; after the in-flight nightly perf batch lands (same source area) |
| **S5** Nightly health / readiness off 5.1 | `nightly-health.ps1` (41) + `lib/nightly-health.ps1` (956) | `Commands/Nightly/Health*.cs`; legacy copies | `test-nightly-health.ps1` cases ported to in-process TUnit (5 harness call sites today); parity on the readiness fixtures | launcher (5.1-safe; Windmill command unchanged) | Medium: Windmill readiness runs it nightly | S1, S3 |
| **S6** Receipt validator in-process | `validate-checkpoint-receipt.ps1` (208) → `Antiphon.Checkpoints` `validate` parity | `tools/Antiphon.Checkpoints` validate verb; `server/Infrastructure/Verification/CheckpointReceiptValidator.cs` calls the library instead of spawning `pwsh` | 10 test files reference the script: parity on their receipts; server validator tests without pwsh | launcher to `antiphon-checkpoints validate` | Medium: Review-evidence gate (`reviewedSourceClean`) | S1 |
| **S7** Build-slot gate | `build-slot.ps1` (199) + `lib/build-slot.ps1` (229), fixtures `c589-*` | `Commands/BuildSlot/*.cs`; launcher's minimal lease client (section 3) | `test-build-slot.ps1` (943 lines, 7 harness cases) parity; `BUILD SLOT` lines byte-identical; exit 4 timeout preserved | launcher | High: every build goes through it | S1 |
| **S8** Small HTTP tools | `checkpoint-task.ps1` (206), `keep-six.ps1` (134), `dispatch-concurrency.ps1` (201), `runner-slots.ps1` (67) | `Commands/Ops/*.cs` | `CheckpointTaskScriptTests` (5), `KeepSixReminderTests`, `RegisteredCallerScriptTests` checkpoint case; parity | launchers | Low | S1 |
| **S9** `card` read verbs | `card.ps1` `get/history/list/search/diagnose/order/-Limits` | `Commands/Card/*.cs` | `CardListSearchScriptTests` (11), `CardDiagnoseScriptTests`, `CardRelationshipScriptTests`, `CardFilePrivacyScriptTests` in parity mode; `-All` paging refuses on a failed page | launcher handles read verbs; write verbs still run `scripts/legacy/card.ps1` | Medium | S1, S8 |
| **S10** `card` write verbs | `new/edit/move/close/reopen/archive/unarchive/block/unblock/reorder` | same | `CardCloseDeploymentPromptScriptTests`, `CardWorkTransitionServiceTests`, `CardCorrectionIntegrationTests` paths that run the script; concurrency-token refresh parity | full launcher | Medium | S9 |
| **S11** `delegate` core | create/`-Status`/`-Land`/`-Continue`/`-Reply` | `Commands/Delegate/*.cs` | `DelegateScriptRunner` gains a `Mode` (ps1, cli) and the 20 test files run both; `GrokDelegateEndToEndTests` reaches the real `AgentTaskService` | launcher for these parameter sets | High: the board's front door | S1, S8, S9 |
| **S12** `delegate` rest + `capability` | remaining parameter sets, `capability.ps1` (DPAPI via `System.Security.Cryptography.ProtectedData`), `lib/runner-operator-token.ps1` | `Commands/Delegate/*`, `Commands/Capability/*` | DPAPI round-trip: Windows-native test on the desktop lane with a Linux rc exclusion row; `DelegatePlatformScriptTests`; parity for the 409 `concurrency_limit` and `provider_sign_in_required` refusal texts | full launchers | High: credentials | S11 |

**S2 to S5 sequencing:** S3 and S4 edit pinned nightly inputs. Schedule them after the live rc
qualification run settles, and after the in-flight `Antiphon.Nightly` perf batch (48 commits
2026-11-07..19) lands, to avoid the same-source-area Code deferral. S1, S2, S6 and S8 can start
immediately on server2.

### 4.2 Wave 2 backlog (by family; each line becomes 1–3 h slices)

Disposition codes match Appendix A. Production lines port at roughly 1,000 lines per slice. Test
drivers convert at roughly 2,000 lines per slice. A test driver converts in the same slice as the
library it tests.

| Family | Prod files / lines | Test files / lines | Est. slices | ps1 becomes | Notes |
|---|---|---|---:|---|---|
| D-sweep (one-off proofs) | 11 / 895 | – | 1 | deleted | Root `approve-gate`, `check-artifacts`, `check-workflow`, `create-workflow`, `monitor-workflow`, `test-api`; `verify-card0594-linux-launch`, `validate-card0418-ordinary-evidence`, `build-card0490-bootdisk`, `prepare-card0490-linux-assets`, `run-card-1885-bounded-experiment`. Zero test or C# references; an Investigate pass confirms docs and history before deletion. |
| W2-nightly | 40 / 17,365 | 47 / 21,042 | 17 + 11 | launchers, then deleted | `nightly-tests-impl` (2,164), `test-phase-evidence` (3,516), `nightly-run-impl` (1,067), `live-lane-*` (4,511), `c487-harness`, `nightly-failure-history`, `nightly-stopped-run-seed`, `nightly-dispatch`, `qualification-host-stats`, `toolchain-floor`, `test-selection`, `test-timings`; `nightly-run.ps1` becomes a 5.1 launcher that syncs, then publishes from the synced clone (the self-update hop is replaced by identity); `Antiphon.Scripts` folds in; `Antiphon.Nightly` loses `PowerShellStandard`. |
| W2-rc | 39 / 10,882 | 34 / 15,326 | 11 + 8 | launchers, then deleted | `release-gate` (1,111), `release-authority` (937), `rc-ledger` (1,100), `rc-*`, `release-selection`, `release-impact*`, `qualification/*`, `qualification-supervisor*`, `publish/promote-release`, `release-cut` (Windmill, 5.1); `ManifestPublicationHarness.ps1` (1,920) converts. |
| W2-deploy | 37 / 15,720 | 29 / 13,378 | 16 + 7 | launchers | `deploy-server2.ps1` (1,564), `postgres-snapshot.ps1` (2,303), `cutover-postgres-server2.ps1` (1,265), `postgres-cutover-monitor.ps1`, `restart-linux-server.ps1` (production restart, CARD-1744), `c590-*`/`c1609-*`/`c1008`/`verify-card0849-caches` (live inputs of `RemoteScriptContractTests`, 4,591 s Windows pace), windmill snapshot wrappers. |
| W2-land | 21 / 5,546 | (in ops/land-T) | 6 | launchers | `impacted-classes.ps1` (1,375), `land-graph-land-refresh.ps1`, `audit-land-verification-*` (to `LandGraphAudit`), `check-os-witnesses.ps1`, `check-evidence-*` + `lib/evidence-policy.ps1` (CI workflow calls these), `post-land-early-warning`, `complexity-chain`, `task-commit`, `orchestrator-workspace`. |
| W2-checkpoint | 13 / 3,995 | 20 / 4,312 | 4 + 2 | launcher to `antiphon-checkpoints row`, then deleted | `run-checkpoint.ps1` (1,520) duplicates the tool's row runner; `lib/checkpoint-*`, `build-driver-environment`, `linux-compiler-evidence`, `verify-checkpoint-*`. |
| W2-ops | 14 / 4,530 | (in ops/land-T) | 5 | launchers | `cleanup-*`, `reap-zombie-agents` (996), `reap-orphaned-pty-hosts`, `recover-repository-children` (server alarm text), `host-load`, `measure-*`. |
| W2-http | 17 / 2,258 | – | 3 | launchers | `project`, `schedule`, `routing-pin`, `specialist-routing`, `model-availability`, `pipeline-definition`, `runner-drain`, `github-sync`, `herdr-pane`, `logs`, `hangfire-dashboard`, `distiller`, `migrate-stage-pins-to-cells`, `digest`, `orchestration/*` watchers. |
| W2-client | 5 / 547 | 4 / 712 | 1 + 1 | launchers | `lint-client`, `typecheck-client`, `test-client`, `client-mode`, `lib/client-npm-install`. |
| W2-ops/land tests | – | 13 / 2,180 | 1 | – | `test-orchestration-watchers`, `test-os-witnesses`, `test-land-graph-*`, `test-logs`, ... |
| W3-desktop | 4 / 2,794 | (incl.) | 3 | launchers, then deleted | `desktop-slot-provision`, `desktop-win-phase1-secrets` (**last**), and their tests. |
| W3-devstack | 12 / 728 | – | 1 | ported or retired | Root `dev-backup/fresh/restore/start/stop/upgrade`, `restart*.ps1`, `stop-server`, `assert-dev-ports-clear`, `verify-dev-stack`. Low value; check for retirement first. |
| Retire legacy + reference sweep | – | – | 3 | front-door launchers deleted | Docs/skills/server text move to `scripts/antiphon <cmd>`; `scripts/legacy/**` deleted; pwsh removed from images only if section 6 allows. |

## 5. Effect on the test-execution policy, pins and indexes

- **PinnedInputDigest.**
  - S2 proves the C# digest equals the ps1 digest, so the recipe moves without a reseal.
  - S3 reseals once, for the policy port.
  - S4 changes the recipe: the pinned inputs become `Antiphon.Nightly/Census/*.cs` plus the census
    projection of the policy (D-9). Reseal is then `antiphon nightly input-digest --print`.
  - Expected effect: an edit to non-census nightly code (most of the 64 ps1-touching commits
    edited the 1,150-line and 1,564-line catch-all libraries) no longer moves the pin. A policy
    edit outside the projection no longer moves it either. **UNKNOWN:** the exact fraction. S4
    classifies the 64 commits' hunks and records it.
- **policyHash.** The value does not change. C# canonicalisation must equal `nightly-common.ps1`
  byte for byte. The witness is the stated `policyHash` of the committed policy, recomputed by
  both implementations. A divergence is a blocker, never a reseal.
- **Selection catalog.**
  - `P-AUTHORITY` (full rc) today keys on nightly/release ps1 regexes and on
    `tools/Antiphon\.Checkpoints/.*`.
  - D-10 adds the CLI's nightly, release, checkpoint and build-slot namespaces once in S3.
  - `P-BUILD` already makes any csproj/sln change a full selection, so S1 is a full rc selection
    by construction.
  - Rules with explicit ps1 `paths` (3 rules, 4 entries) and the harness `"script":` prerequisites
    (23 ps1 mentions in the selection policy, 21 in the execution policy) are re-keyed in the
    family slice that retires each script.
- **Land graph.**
  - S1 adds `tools/Antiphon.Cli/Antiphon.Cli.csproj` to `Antiphon.sln` and a ProjectReference
    from `Antiphon.Tests`. That changes the batch's own selected records, so S1 runs
    `scripts/land-graph-land-refresh.ps1` (rule 11: refresh once, never ask).
  - Later CLI-only slices change no build-definition record unless they add a package or a
    project.
- **Derived-impact index.** The 80 ps1-keyed edges move to `tools/Antiphon.Cli/**` source paths as
  logic moves. Every slice regenerates and commits `tests/derived-impact-index.json`
  (`DerivedImpactIndexTests.committed_index_matches_the_builder`).
- **Impacted classes (CARD-1782).** The `tests/impacted-classes.json` rows naming
  `land-graph-land-refresh.ps1`, `nightly-core.ps1`, `capability.ps1`, `c487-harness.ps1` and
  `test-nightly-core.ps1` are re-keyed when those move (S3/S4, S12, W2-land, W2-nightly).
- **Change-aware rc selection.** `scripts/lib/release-impact-scripts.ps1` computes a ps1 closure
  that is "always incomplete with fallback Full". Once code is C#, its closure is the
  ProjectReference graph that `rc-project-selection` already walks, so selection becomes exact
  for ported code. This needs the `rc-project-categories.json` entry from S1.
- **NativeLinux and Windows witnesses.**
  - A harness case that becomes an in-process C# test moves its `NativeLinuxOnly` attribute and
    policy row with it. The row is resealed from the witness output (repo recipe).
  - New Windows-native C# (ACL, DPAPI, `File.Replace`) gets a runtime `IsWindows()` skip, a Linux
    rc exclusion row, and a `-Platform Windows` checkpoint row.
  - `scripts/check-os-witnesses.ps1` obligations apply to each launcher's OS branch (sh, cmd,
    ps1).
- **Test time.**
  - Today: every harness case is pwsh startup (≈0.6–0.7 s) plus dot-sourcing (≈1.2–1.3 s for two
    nightly libs) on the 1-wide `ProcessSpawnLimit` lane or the `ScriptModelSpawnLimit` lane.
  - Ported behaviour tests call `CliApp.RunAsync(args, env, stdout)` in-process with no limiter.
    That is where the 220 Windows serial minutes of `Antiphon.Tests.Scripts.*` come back.
  - Parity tests temporarily add one exe spawn per case until the retire slice.

## 6. What must NOT move (and why)

| Kept as PowerShell | Why |
|---|---|
| `scripts/bootstrap-check.ps1` | It runs on a machine that may not have dotnet yet (`powershell.exe -File scripts/bootstrap-check.ps1 -NoStack`), and one of its jobs is to say that dotnet is missing. |
| `scripts/lib/antiphon-cli.ps1`, `scripts/antiphon`, `scripts/antiphon.cmd` | They find or publish the exe, so they cannot be the exe. They include the minimal build-slot lease client (section 3). |
| `install-autostart.ps1`, `install-cleanup-task.ps1`, `autostart-apphost.ps1`, `autostart-session-runner.ps1`, `watchdog-apphost.ps1`, `apphost-watchdog-state-observer.ps1`, `set-apphost-maintenance.ps1` | Task Scheduler actions run `pwsh`/`powershell -File` at logon. The build-slot gate is served by the session runner that `autostart-session-runner.ps1` starts, so a logon-time CLI publish would wait on a slot service that is not up yet. The watchdog must also work when the main checkout's source does not build. |
| `restart-apphost.ps1`, `apphost-common.ps1` (1,341), `dev-aspire.ps1`, `restart-session-runner.ps1` (both), `session-runner-restart-health.ps1`, `run-daemon.ps1`, `check-daemon-build.ps1`, `start-apphost.ps1` | The restart chain rebuilds and kills the stack, including the runner that serves build slots (`restart-session-runner.ps1 -Hard`). It must keep working with a broken build (`BuildFailed` path) and must not depend on a slot from the runner it restarts. Revisit after wave 2 with a pinned last-known-good CLI copy installed by `install-autostart.ps1`. |
| Windmill entry points `nightly-run.ps1`, `nightly-health.ps1`, `release-cut.ps1` and `scripts/windmill/*.ps1` (as launchers) | The Windmill flows run `powershell` 5.1 over ssh. Changing a flow is an outward-facing operator action, outside this plan's authority. The 5.1-safe launcher keeps the flows unchanged. A later operator change can call `scripts\antiphon.cmd` directly. |
| PowerShell-subject fixtures: `tests/Antiphon.Tests/Scripts/Fixtures/directory-link.ps1`, `script-harness-process.ps1`, `Agents/Fixtures/claude-effort-*.ps1`, `tests/Antiphon.SessionRunner.Tests/Fixtures/CodexVersionChild.ps1`, `RunnerRestart/platform.ps1` | They test how the product handles PowerShell children (pty, Herdr, process probes, runner restart). The subject is PowerShell. |
| `scripts/fixtures/script-harness-*-witness.ps1`, `script-groups-windows-witness.ps1` | ScriptHarness self-witnesses. They live as long as ScriptHarness does. |
| `docs/investigations/evidence/card-1477/**`, `.antiphon/c959-second-evidence/inspect-receipts.ps1` | Historical evidence is preserved unedited. |
| Inline hooks (`WorkspaceHookRunner` `powershell.exe`, the orchestrator deny hook) | These are not ps1 files and are out of scope. |

`KEEP-T` (12 files, 3,079 lines) are the harnesses of the kept restart and autostart scripts.
They stay with their subjects.

## 7. Windows specifics

- **File replace.**
  - The DW-3 helper reaches `File.Replace(string,string,string)` through reflection, so that
    Windows PowerShell 5.1 can bind it (`desktop-win-phase1-secrets.ps1:708-748`). Off Windows it
    uses `File.Move(temp, target, true)`.
  - C# calls `File.Replace(temp, target, backup, ignoreMetadataErrors: false)` on Windows
    (ReplaceFileW, same-volume atomic with a backup) and `File.Move(..., overwrite: true)`
    (rename(2)) on Unix.
- **ACLs.**
  - Today: `icacls` child processes, `Get-Acl`/`Set-Acl` and `DOMAIN\user` name strings
    (`Set-OwnerOnlyFile`, `Test-Dw3OwnerOnly`, `Get-PathOwner`).
  - C# creates the file with its DACL atomically through
    `FileSystemAclExtensions.Create(FileInfo, FileMode.CreateNew, FileSystemRights.FullControl, FileShare.None, 4096, FileOptions.WriteThrough, FileSecurity)`.
    The DACL is protected (`SetAccessRuleProtection(true, false)`), with one rule for
    `WindowsIdentity.GetCurrent().User` (a SID, so no name resolution).
  - Ownership checks use `GetAccessControl().GetOwner(typeof(SecurityIdentifier))`.
  - No `icacls` process, and no window in which the file exists with inherited ACEs.
  - On Unix, `FileStreamOptions.UnixCreateMode = UserRead | UserWrite` gives the mode at
    creation, replacing `chmod 600` after the fact.
  - Linux pwsh cannot construct `SecurityIdentifier` or `DirectorySecurity`. C# keeps these APIs
    behind `[SupportedOSPlatform("windows")]` plus a runtime check, with Windows-native witnesses.
- **JSON.**
  - The DW-3 helper carries its own JSON reader (`Read-Dw3Json*`, about 280 lines), because 5.1
    lacks `-DateKind` and pwsh 7 `ConvertFrom-Json` turns date-shaped strings into `DateTime`.
  - `System.Text.Json` `JsonNode` keeps strings as strings, so the reader is deleted. Parity
    covers a date-shaped string round trip.
- **DPAPI.** `capability.ps1:78` and `delegate.ps1:450` call `ProtectedData.Protect`/`Unprotect`.
  C# uses the `System.Security.Cryptography.ProtectedData` package, which needs a CARD-1229 pin.
  The Linux store behaviour is **UNKNOWN** here; S12 reads it first.
- **Secrets.** A secret never goes into argv, an environment variable passed to a child, or a log.
  This is unchanged; the C# helper writes in-process as the ps1 does.
- **Process spawn.**
  - Use `ProcessStartInfo.ArgumentList`, never a joined string. This removes the
    `pwsh -File`/`-Command` quoting hazards recorded in the repo's pwsh argv notes.
  - Use the shared `scripts/lib/CheckpointProcessRunner.cs` for bounded output reads, and
    `Antiphon.HostCleanup` job objects for child trees on Windows.
  - Tests that spawn the CLI exe or pwsh carry `[ParallelLimiter<ProcessSpawnLimit>]` (the exe is
    not on the `ScriptModelSpawnLimit` allow-list of bash/node/pwsh/jq/git/coreutils).
  - In-process command tests carry no limiter.
  - Do not co-schedule `Antiphon.Tests` with `Antiphon.Agents.Pty.Tests`.
- **Encoding and long paths.**
  - The 5.1 CP1252 parse hazard (the ASCII-only rule) does not apply to C#.
  - Console output stays UTF-8 without a BOM.
  - Machine-read lines (`CHECKPOINT`, `BUILD SLOT`, `- bound to CARD-nnnn`) are byte-compared in
    parity.
  - .NET handles long paths natively. `git config core.longpaths` is still required for git.
- **Redirected output.** Redirected pwsh wraps `Write-Error` at 80 columns, and C# does not. The
  parity normaliser declares this per command instead of reproducing the wrap.

## 8. First slice, risk register and effort

### 8.1 First slice: S1 (can start now on server2)

S1 touches no nightly, rc, qualification or Windows-run file. It adds the CLI, the three
launchers, the parity harness and one read-only port (`status-snapshot.ps1`: GET only, writes
nothing, one existing stub-HTTP test). It runs unpinned, which places it on server2 by default.
Only the Windows launcher rows use `-Platform Windows`.

- **Files:** see S1 in 4.1. Put the CLI docs in a new `docs/antiphon-cli.md`. Editing
  `docs/testing-and-build.md` or `AGENTS.md` would also match `P-AUTHORITY` (full rc); link them
  in a later slice.
- **Red-able witnesses:**
  - Drop the `X-Antiphon-Caller` header in `AntiphonApiClient`, and `RegisteredCallerScriptTests`
    plus the parity test go red.
  - Change one output token, and the parity test goes red.
  - Make the identity ignore a dirty input, and `AntiphonCliLauncherTests` goes red.
  - Remove the lock, and the concurrent-publish case shows two publishes.

#### S1 proposed checkpoint rows (input to TestDesign; not the `### Checkpoints` section)

Lane: server2 (linux) for CP-1 to CP-4. Desktop (`-Platform Windows`) for CP-5.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-cli-s1/` | s1-cli-linux | `/*/*/(AntiphonCli*)\|(StatusSnapshotScriptTests*)\|(RegisteredCallerScriptTests*)\|(AgentTaskGetPollMetricsTests*)/*` | S1 | all listed, 0 failed | TestDesign counts | 18 |
| CP-2 | S1 | CP-1 | s1-guards-linux | `/*/*/(DerivedImpactIndexTests*)\|(TestClassificationPolicyTests*)\|(ImpactedClassesSelectorTests*)\|(LinkedTestHelperClosureTests*)/*` | S1 | all listed, 0 failed | TestDesign counts | 6 |
| CP-3 | S1 | n/a | s1-land-graph | `pwsh -NoProfile -File scripts/audit-land-verification-graph.ps1 -Check` | S1 | exit 0 | n/a | 6 |
| CP-4 | S1 | n/a | s1-real-publish-linux | `sh scripts/antiphon --version` with `ANTIPHON_CLI_CACHE` at an ignored `.antiphon/` path | S1 | exit 0; the identity printed equals `identity`; one `BUILD SLOT` lease | n/a | 6 |
| CP-5 | S1 | n/a | s1-launchers-windows | `scripts\antiphon.cmd --version`, then `powershell -NoProfile -File scripts/status-snapshot.ps1 -?` (5.1 parse) | S1 | exit 0 both; 5.1 parses the launcher | n/a | 8 |

Notes on the rows:

- The roster was checked for suffix collisions: `AntiphonCli*` matches no existing class (only S1's
  new ones). Each other operand matches exactly one existing class.
- CP-1 and CP-2 share one build within the same `After` group.
- `Min` is left for TestDesign to count from the final method list.
- The estimates are provisional; see the performance budget.

### 8.2 Risk register

| # | Risk | Likelihood / impact | Mitigation |
|---|---|---|---|
| R1 | The binder differs from PowerShell: prefix matching, `-Switch:$false` arriving as a string under `-File`, arrays from in-process `& script.ps1` callers (card.ps1 has 7 ps1 callers) | M / H | `AntiphonCliBinderTests` table from the real parameter blocks; each slice audits its in-process ps1 callers and converts them to the CLI or `-File` form. |
| R2 | Output drift breaks agents that grep output (`CHECKPOINT`, `- bound to CARD-`, refusal texts) | M / H | Byte-compare machine lines; a declared divergence list per command, reviewed. |
| R3 | Launcher cannot publish (no SDK, slot exit 4, restore failure), cutting a session off the board | L / H | Legacy fallback during the parity window; afterwards exit 9 with a reason; never unleased. A pre-warm `antiphon --version` in runner image builds is optional. |
| R4 | Concurrent first use across many worktrees publishes N times, or a half-written cache is used | M / M | Lock file, re-check, atomic directory rename, `identity` written last (the `Install-NightlyCoreBuild` pattern). |
| R5 | Windows locks a running exe, so the cache cannot be replaced or collected | M / L | Identity directories are immutable; GC skips locked ones. |
| R6 | Reseal chains (pin → policy → catalog) on every slice | H / M | D-8 (no new test project in wave 1), D-10 (one forward selection edit), S2 parity before any recipe change. |
| R7 | rc ports: rc carries no `docs/superpowers/plans`; launcher and CLI must travel together; nightly slices need ordered ports | M / M | Port per family with launcher, CLI and legacy copy as one pick. A family is not cut into rc until its parity window closes on master. |
| R8 | server2 load: publishes add slot demand (load 21–24 on 24 cores at read time) | M / L | One publish per identity per host; ReadyToRun publish cost measured in S1. |
| R9 | Credential handling (DPAPI store, phase-1 secrets) | L / H | Credential and secret slices last (S12, W3); Windows-native witnesses; dry-run-only desktop parity on scratch paths. |
| R10 | Antivirus or SmartScreen delays a new exe under `%LOCALAPPDATA%` | **UNKNOWN** | CP-5 records first-run latency on the desktop. |
| R11 | The parity window doubles spawn cost in parity tests | H / L | Retire slices remove the legacy runs. |
| R12 | Server-emitted text and 269+ doc references to `scripts/delegate.ps1` | certain / L | Launchers keep them valid (D-6); a final reference sweep is its own slice. |
| R13 | Collision with the in-flight `Antiphon.Nightly` perf work (same source area) | H / M | S3/S4 wait for it; S1/S2/S6/S8 do not touch it. |
| R14 | `policyHash` canonicalisation drift during S3 | L / H | Recompute the stated hash on the committed policy with both implementations; a divergence blocks. |

### 8.3 Effort estimate (ESTIMATE, not measured)

Rate assumptions:

- Production ps1 → C# with parity: about 350 lines per Code-hour.
- Test-driver conversion: about 700 lines per Code-hour.
- About 1 hour of fixed overhead per slice: checkpoint rows, index regeneration, land graph.
- Review at about 0.5× Code.

The only repo data point: `Antiphon.Nightly` gained 7,184 C# lines in 43 commits over 12 days
(2026-11-07..19) of incremental perf work. That gives scale, not an hourly rate.

| Wave | Lines | Slices | Code h | Review h |
|---|---:|---:|---:|---:|
| Wave 1 (S1–S12) | 11,328 | 12 | ~30 | ~15 |
| Wave 2 production | 60,843 | ~66 | ~240 | ~120 |
| Wave 2 test drivers | 56,950 | ~29 | ~110 | ~55 |
| Wave 3 + retire/sweep | 3,522 + sweep | ~8 | ~20 | ~10 |
| Kept (section 6), historical, delete | 8,710 + 895 | – | – | – |
| **Total** | **142,248** | **~115** | **~400** | **~200** |

That is about 600 agent-hours, with ±50% uncertainty. At the Code depth cap of four, calendar time
is UNKNOWN. It is bounded by Review/land throughput and rc port cadence rather than authoring.
6–10 weeks is a planning guess, not a measurement.

## Existing tests affected

This section is required for migrations, public DTOs/routes, operation labels, remote script
functions and statement budgets. It points at the impacted-classes table (CARD-1782).

- **Migrations:** none in any wave-1 slice.
- **Public DTOs and routes:** none. The CLI consumes existing routes; S6 changes server internals
  (the validator call), not a route.
- **Operation labels:**
  - The `X-Antiphon-Caller` labels are unchanged (D-6), so `AgentTaskGetPollMetricsTests` and
    `RegisteredCallerScriptTests` keep their expectations.
  - `RegisteredCallerScriptTests` and `StatusSnapshotScriptTests` (S1), `CheckpointTaskScriptTests`
    (S8), the card script test classes (S9/S10) and the `DelegateScriptRunner` users (20 files,
    S11/S12) change to run through the launcher with `ANTIPHON_CLI_PATH`.
- **Remote script functions and statement budgets:** none in wave 1. `RemoteScriptContractTests`
  (its inputs `c590-*`, `c1609-*`, `deploy-server2.ps1`, `verify-card0849-caches.ps1`) belongs to
  W2-deploy, and its plan must list it.
- **Harness-backed classes:** each family slice lists the `ScriptHarness.RunHarnessCaseAsync`
  classes of the scripts it ports (166 call sites; top: `test-release-gate.ps1` 30,
  `test-nightly-run.ps1` 12, `test-nightly-tests.ps1` 10, `test-toolchain-floor.ps1` 10).
- **Impacted classes:** each slice runs `pwsh -NoProfile -File scripts/impacted-classes.ps1 -Range <base>..<tip> -Json`
  on its diff and adds the selected rows. S1's diff touches `Antiphon.sln` and a csproj, so the
  fixed `-MergeSubset` applies at merge.

## Performance budget

This is a plan with no tests of its own. The budget below is for S1. Later slices state their own
in their plans, using `docs/testing-and-build.md#performance-standards`.

- **Selection.**
  - CP-1 runs the new `AntiphonCli*` classes (about 25 methods, mostly in-process) plus
    `StatusSnapshotScriptTests` (1), `RegisteredCallerScriptTests` (5) and
    `AgentTaskGetPollMetricsTests`.
  - CP-2 runs four guard classes.
  - Project graph: `tests/Antiphon.Tests` with its new reference to `tools/Antiphon.Cli`. No
    downstream app categories change; `P-BUILD` makes the rc selection full anyway.
- **Expected durations.**
  - In-process binder, label and inputs tests: under 100 ms each.
  - Spawn tests: 2–5 s each. The basis is `CardListSearchScriptTests` on Windows at 42 s for 11
    nodes (≈3.8 s per test, measured row in `nightly-class-costs.json`), plus server2 samples on
    2026-11-19 at load 21–24 on 24 cores: pwsh start 616–715 ms (n=3), pwsh with two nightly libs
    1.2–1.3 s (n=2), apphost start 146–439 ms (n=3). These are samples, not a p95.
  - CP-1 test time is about 2 minutes on top of the build.
- **Build and publish.**
  - The `Antiphon.Tests` build time on loaded server2 is **UNKNOWN** here. It is reported
    separately, with slot wait separate.
  - Cold `dotnet publish` of the CLI is **UNKNOWN**. CP-4 measures 5 cold samples (fresh
    `ANTIPHON_CLI_CACHE`) and 5 warm samples (cache hit). The row timeout is about 2× the measured
    p95 after that.
- **Phases.**
  - Build, publish, test body and teardown, with slot wait reported separately.
  - Parity tests record `BeginPhase("legacy")` and `BeginPhase("cli")`, so the per-invocation
    cost of both paths is recorded evidence for D-4.
- **Shared setup.**
  - The CLI dll is built once with the `Antiphon.Tests` build and staged for all tests through
    `ANTIPHON_CLI_PATH`. No per-test build or publish, except the single CP-4 row.
  - The stub HTTP server and `ScratchGitRepo` are per-test and cheap, as in today's
    `StatusSnapshotScriptTests`.
- **Timeouts.**
  - Provisional per-test `[Timeout(30_000)]` for spawn tests. That is ≈2× a pessimistic ≈15 s
    loaded bound (3.8 s mean × 4 for server2 load). It is provisional because there is no p95
    yet; TestDesign replaces it with 2× the measured p95 from CP-1's first green TRX
    (10 samples).
  - In-process tests use the assembly default.
- **Over 30 minutes:** no selection, row, batch or run is expected to exceed it. If one does, a
  Debug task is opened with timing evidence.

## Checklist: sessions that wait for input

The plan adds and changes no session that waits for input. Parking stays disabled by default
(CARD-1083), so there is no automatic release deadline.

## Next

The orchestrator lands this plan document (Docs). Then S1 goes to TestDesign, which writes S1's
`## Verification design` and the real `### Checkpoints` table from 8.1. Then Code runs on server2,
unpinned.

## Appendix A: per-file inventory (427 files)

Columns:

- **Callers:** tracked files that name the script.
  - ps1/psm1, then test `.cs` files under `tests/`, then Markdown under `docs/`, `.claude/` and
    AGENTS (excluding the generated `docs/cards/` and `.antiphon/`).
  - Then other flags: `C#:n` (server/src/tools code), Windmill(5.1), SchedTask, CI, server spawn.
- **Tests shell out:** the script name appears in `tests/**/*.cs`.
- **Pin:** a `PinnedInputDigest` input.
- **Host:**
  - `7 only` means `#requires -Version 7` or 7-only APIs.
  - `5.1 named` means the file states a 5.1/ASCII contract.
  - `unstated` means neither.
- **Disposition:** the slice or family from sections 4 and 6.

Counting is by basename match. A shared basename such as `probe.ps1` or `restart-session-runner.ps1`
can over-count; Appendix B gives the commands.

| Path | Lines | Role | Callers: ps1 / test .cs / docs / other | Tests shell out | Pin | Host | Disposition |
|---|---:|---|---|---|---|---|---|
| `.antiphon/c959-second-evidence/inspect-receipts.ps1` | 17 | historical evidence | ps 0 / test.cs 0 / docs 0 | no |  | unstated | HIST |
| `approve-gate.ps1` | 8 | root legacy | ps 0 / test.cs 0 / docs 0 | no |  | unstated | DELETE? (S0 audit) |
| `assert-dev-ports-clear.ps1` | 73 | root dev-stack script | ps 3 / test.cs 0 / docs 0 | no |  | unstated | W3-devstack |
| `check-artifacts.ps1` | 34 | root legacy | ps 0 / test.cs 0 / docs 0 | no |  | unstated | DELETE? (S0 audit) |
| `check-workflow.ps1` | 3 | root legacy | ps 0 / test.cs 0 / docs 0 | no |  | unstated | DELETE? (S0 audit) |
| `create-workflow.ps1` | 56 | root legacy | ps 0 / test.cs 0 / docs 0 | no |  | unstated | DELETE? (S0 audit) |
| `dev-aspire.ps1` | 239 | root restart chain | ps 18 / test.cs 1 / docs 44 | yes (1) |  | unstated | KEEP (sec. 6) |
| `dev-backup.ps1` | 50 | root dev-stack script | ps 3 / test.cs 0 / docs 11 | no |  | unstated (non-ASCII) | W3-devstack |
| `dev-fresh.ps1` | 48 | root dev-stack script | ps 0 / test.cs 0 / docs 3 | no |  | unstated (non-ASCII) | W3-devstack |
| `dev-restore.ps1` | 78 | root dev-stack script | ps 0 / test.cs 0 / docs 6 | no |  | unstated (non-ASCII) | W3-devstack |
| `dev-start.ps1` | 83 | root dev-stack script | ps 4 / test.cs 0 / docs 6 | no |  | unstated (non-ASCII) | W3-devstack |
| `dev-stop.ps1` | 36 | root dev-stack script | ps 3 / test.cs 0 / docs 2 | no |  | unstated | W3-devstack |
| `dev-upgrade.ps1` | 48 | root dev-stack script | ps 0 / test.cs 0 / docs 1 | no |  | unstated (non-ASCII) | W3-devstack |
| `docs/investigations/evidence/card-1477/run-witness.ps1` | 32 | historical evidence | ps 0 / test.cs 0 / docs 1 | no |  | unstated | HIST |
| `docs/investigations/evidence/card-1477/stub/claude-home/skills/launch-remote/launch-claude.ps1` | 4 | historical evidence | ps 1 / test.cs 0 / docs 1 | no |  | unstated | HIST |
| `docs/investigations/evidence/card-1477/test/harness.ps1` | 78 | historical evidence | ps 1 / test.cs 0 / docs 1 | no |  | unstated | HIST |
| `docs/investigations/evidence/card-1477/test/run-case.ps1` | 95 | historical evidence | ps 1 / test.cs 0 / docs 3 | no |  | unstated | HIST |
| `monitor-workflow.ps1` | 49 | root legacy | ps 0 / test.cs 0 / docs 0 | no |  | unstated | DELETE? (S0 audit) |
| `restart-client.ps1` | 33 | root dev-stack script | ps 1 / test.cs 0 / docs 0 | no |  | unstated | W3-devstack |
| `restart-server.ps1` | 49 | root dev-stack script | ps 2 / test.cs 2 / docs 2 | yes (2) |  | unstated | W3-devstack |
| `restart-session-runner.ps1` | 16 | root restart chain | ps 8 / test.cs 5 / docs 48 / C#:3 | yes (5) |  | unstated (non-ASCII) | KEEP (sec. 6) |
| `restart.ps1` | 37 | root dev-stack script | ps 0 / test.cs 0 / docs 6 | no |  | unstated | W3-devstack |
| `scripts/apphost-common.ps1` | 1341 | bootstrap/restart/autostart | ps 15 / test.cs 1 / docs 16 | yes (1) |  | 5.1 named | KEEP (sec. 6) |
| `scripts/apphost-watchdog-state-observer.ps1` | 128 | bootstrap/restart/autostart | ps 2 / test.cs 1 / docs 3 / SchedTask, C#:1 | yes (1) |  | 5.1 named | KEEP (sec. 6) |
| `scripts/audit-land-verification-graph.ps1` | 169 | land/selection/orchestrator support | ps 2 / test.cs 5 / docs 65 / C#:4 | yes (5) |  | 5.1 named | W2-land |
| `scripts/audit-land-verification-precheck.ps1` | 93 | land/selection/orchestrator support | ps 0 / test.cs 3 / docs 4 / C#:4 | yes (3) |  | 5.1 named | W2-land |
| `scripts/autostart-apphost.ps1` | 141 | bootstrap/restart/autostart | ps 4 / test.cs 1 / docs 12 / SchedTask | yes (1) |  | 5.1 named | KEEP (sec. 6) |
| `scripts/autostart-session-runner.ps1` | 141 | bootstrap/restart/autostart | ps 4 / test.cs 1 / docs 14 / SchedTask | yes (1) |  | unstated | KEEP (sec. 6) |
| `scripts/bootstrap-check.ps1` | 687 | bootstrap/restart/autostart | ps 0 / test.cs 1 / docs 14 / pre-dotnet | yes (1) |  | 5.1 named | KEEP (sec. 6) |
| `scripts/build-card0490-bootdisk.ps1` | 226 | one-off card proof | ps 0 / test.cs 0 / docs 2 | no |  | unstated | DELETE? (S0 audit) |
| `scripts/build-slot.ps1` | 199 | build-slot gate | ps 24 / test.cs 9 / docs 626 / C#:4 | yes (9) |  | 7 only | S7 |
| `scripts/c1008-host-proof.ps1` | 295 | deploy/server2/postgres | ps 1 / test.cs 5 / docs 6 | yes (5) |  | unstated | W2-deploy |
| `scripts/c1609-host-proof.ps1` | 659 | deploy/server2/postgres | ps 2 / test.cs 5 / docs 21 | yes (5) |  | unstated | W2-deploy |
| `scripts/c1609-manual.ps1` | 226 | deploy/server2/postgres | ps 1 / test.cs 1 / docs 3 | yes (1) |  | unstated | W2-deploy |
| `scripts/c1609-read-packet.ps1` | 60 | deploy/server2/postgres | ps 2 / test.cs 2 / docs 0 | yes (2) |  | unstated | W2-deploy |
| `scripts/c590-command.ps1` | 70 | deploy/server2/postgres | ps 3 / test.cs 4 / docs 1 | yes (4) |  | unstated | W2-deploy |
| `scripts/c590-real.ps1` | 988 | deploy/server2/postgres | ps 6 / test.cs 12 / docs 55 | yes (12) |  | 7 only | W2-deploy |
| `scripts/c849-import-saved-donor.ps1` | 142 | deploy/server2/postgres | ps 1 / test.cs 1 / docs 6 | yes (1) |  | unstated | W2-deploy |
| `scripts/capability.ps1` | 165 | credential tool | ps 1 / test.cs 3 / docs 6 / agents (DPAPI store) | yes (3) |  | 5.1 named | S12 |
| `scripts/capture-nightly-probe.ps1` | 59 | nightly/selection | ps 0 / test.cs 0 / docs 2 | no |  | 7 only | W2-nightly |
| `scripts/card.ps1` | 1191 | orchestrator front door | ps 7 / test.cs 14 / docs 307 / C#:7 | yes (14) |  | 5.1 named | S9-S10 |
| `scripts/check-agent-context.ps1` | 60 | land/selection/orchestrator support | ps 1 / test.cs 0 / docs 2 | no |  | unstated | W2-land |
| `scripts/check-daemon-build.ps1` | 85 | bootstrap/restart/autostart | ps 8 / test.cs 2 / docs 11 | yes (2) |  | 5.1 named | KEEP (sec. 6) |
| `scripts/check-evidence-deletion.ps1` | 10 | land/selection/orchestrator support | ps 0 / test.cs 4 / docs 3 | yes (4) |  | unstated | W2-land |
| `scripts/check-evidence-diff.ps1` | 8 | land/selection/orchestrator support | ps 0 / test.cs 4 / docs 297 / CI | yes (4) |  | unstated | W2-land |
| `scripts/check-os-witnesses.ps1` | 291 | land/selection/orchestrator support | ps 1 / test.cs 0 / docs 40 | no |  | 7 only | W2-land |
| `scripts/check-server-docker-inputs.ps1` | 114 | deploy/server2/postgres | ps 1 / test.cs 0 / docs 0 / CI | no |  | 7 only | W2-deploy |
| `scripts/checkpoint-orphan-owner.ps1` | 40 | checkpoint tooling | ps 1 / test.cs 0 / docs 1 | no |  | unstated | W2-checkpoint |
| `scripts/checkpoint-task.ps1` | 206 | orchestrator HTTP tool | ps 1 / test.cs 3 / docs 13 / C#:1 | yes (3) |  | 5.1 named | S8 |
| `scripts/cleanup-build-junk.ps1` | 46 | cleanup/ops | ps 2 / test.cs 1 / docs 19 / SchedTask | yes (1) |  | unstated | W2-ops |
| `scripts/cleanup-claude-sessions.ps1` | 664 | cleanup/ops | ps 1 / test.cs 0 / docs 6 | no |  | 5.1 named | W2-ops |
| `scripts/cleanup-codex-test-residue.ps1` | 443 | cleanup/ops | ps 1 / test.cs 0 / docs 0 | no |  | 5.1 named | W2-ops |
| `scripts/cleanup-gym-stat-one-off-agents.ps1` | 478 | cleanup/ops | ps 0 / test.cs 0 / docs 1 | no |  | 5.1 named | W2-ops |
| `scripts/client-mode.ps1` | 141 | client wrappers | ps 1 / test.cs 0 / docs 15 | no |  | 5.1 named | W2-client |
| `scripts/codex-boot-census.ps1` | 94 | cleanup/ops | ps 0 / test.cs 0 / docs 7 | no |  | 7 only | W2-ops |
| `scripts/complexity-chain.ps1` | 345 | land/selection/orchestrator support | ps 2 / test.cs 4 / docs 13 / C#:2 | yes (4) |  | 5.1 named | W2-land |
| `scripts/cutover-postgres-server2.ps1` | 1265 | deploy/server2/postgres | ps 2 / test.cs 0 / docs 25 | no |  | 5.1 named | W2-deploy |
| `scripts/delegate.ps1` | 1593 | orchestrator front door | ps 6 / test.cs 43 / docs 269 / C#:20 | yes (43) |  | 5.1 named | S11-S12 |
| `scripts/deploy-am-service.ps1` | 416 | deploy/server2/postgres | ps 2 / test.cs 1 / docs 21 | yes (1) |  | 5.1 named | W2-deploy |
| `scripts/deploy-local.ps1` | 157 | deploy/server2/postgres | ps 2 / test.cs 0 / docs 13 | no |  | 5.1 named | W2-deploy |
| `scripts/deploy-nightly-watchdog.ps1` | 252 | deploy/server2/postgres | ps 1 / test.cs 1 / docs 5 | yes (1) |  | 5.1 named | W2-deploy |
| `scripts/deploy-server2-postgres.ps1` | 500 | deploy/server2/postgres | ps 1 / test.cs 0 / docs 10 | no |  | 5.1 named | W2-deploy |
| `scripts/deploy-server2-server.ps1` | 414 | deploy/server2/postgres | ps 0 / test.cs 3 / docs 8 | yes (3) |  | 5.1 named | W2-deploy |
| `scripts/deploy-server2-staging-server.ps1` | 327 | deploy/server2/postgres | ps 1 / test.cs 1 / docs 2 | yes (1) |  | 5.1 named | W2-deploy |
| `scripts/deploy-server2.ps1` | 1564 | deploy/server2/postgres | ps 3 / test.cs 14 / docs 100 | yes (14) |  | 7 only | W2-deploy |
| `scripts/desktop-slot-provision.ps1` | 484 | operator desktop | ps 1 / test.cs 0 / docs 1 | no |  | 5.1 named | W3-desktop |
| `scripts/desktop-win-phase1-secrets.ps1` | 1035 | operator desktop secret custody | ps 1 / test.cs 0 / docs 1 / operator desktop (secrets) | no |  | 5.1 named | W3-desktop (LAST) |
| `scripts/digest.ps1` | 35 | HTTP/ops tool | ps 0 / test.cs 0 / docs 1 | no |  | unstated | W2-http |
| `scripts/dispatch-concurrency.ps1` | 201 | orchestrator HTTP tool | ps 1 / test.cs 1 / docs 4 | yes (1) |  | 5.1 named | S8 |
| `scripts/distiller.ps1` | 82 | HTTP/ops tool | ps 0 / test.cs 0 / docs 5 | no |  | unstated | W2-http |
| `scripts/export-expected-duration-roster.ps1` | 56 | nightly/selection | ps 0 / test.cs 2 / docs 2 | yes (2) |  | 5.1 named | W2-nightly |
| `scripts/export-nightly-prior-failures.ps1` | 16 | nightly/selection | ps 0 / test.cs 0 / docs 5 | no |  | 5.1 named | W2-nightly |
| `scripts/export-nightly-stopped-run-seed.ps1` | 19 | nightly/selection | ps 1 / test.cs 0 / docs 5 | no |  | 5.1 named | W2-nightly |
| `scripts/fixtures/c1339-fixture-state.ps1` | 126 | test driver/fixture | ps 1 / test.cs 0 / docs 0 | no |  | unstated | W2-rc-T |
| `scripts/fixtures/c1339-release-git.ps1` | 146 | test driver/fixture | ps 2 / test.cs 0 / docs 1 | no |  | unstated | W2-rc-T |
| `scripts/fixtures/c1500-status.ps1` | 162 | test driver/fixture | ps 1 / test.cs 0 / docs 3 | no |  | 5.1 named | W2-rc-T |
| `scripts/fixtures/c1731-evidence-path.ps1` | 144 | test driver/fixture | ps 1 / test.cs 0 / docs 0 | no |  | 5.1 named | W2-rc-T |
| `scripts/fixtures/c1731-snapshot.ps1` | 58 | test driver/fixture | ps 1 / test.cs 0 / docs 0 | no |  | unstated | W2-rc-T |
| `scripts/fixtures/c1733-allocator.ps1` | 111 | test driver/fixture | ps 1 / test.cs 0 / docs 0 | no |  | unstated | W2-rc-T |
| `scripts/fixtures/c487-readonly-windows-witness.ps1` | 73 | test driver/fixture | ps 0 / test.cs 1 / docs 2 | yes (1) |  | unstated | W2-nightly-T |
| `scripts/fixtures/c589-command-shim.ps1` | 45 | build-slot gate | ps 1 / test.cs 1 / docs 1 | yes (1) |  | 5.1 named | S7 |
| `scripts/fixtures/c589-slot-shim.ps1` | 44 | build-slot gate | ps 4 / test.cs 3 / docs 3 | yes (3) |  | 5.1 named | S7 |
| `scripts/fixtures/c727-fake-http.ps1` | 214 | test driver/fixture | ps 1 / test.cs 3 / docs 10 | yes (3) |  | unstated | W2-deploy-T |
| `scripts/fixtures/c727-fake-verify.ps1` | 206 | test driver/fixture | ps 1 / test.cs 3 / docs 13 | yes (3) |  | unstated | W2-deploy-T |
| `scripts/fixtures/c889-c578-observation.ps1` | 464 | test driver/fixture | ps 1 / test.cs 2 / docs 1 | yes (2) |  | 5.1 named | W2-deploy-T |
| `scripts/fixtures/c913-image-wrapper.ps1` | 107 | test driver/fixture | ps 0 / test.cs 1 / docs 1 | yes (1) |  | 5.1 named | W2-deploy-T |
| `scripts/fixtures/c913-receipts.ps1` | 146 | test driver/fixture | ps 0 / test.cs 1 / docs 3 | yes (1) |  | unstated | W2-deploy-T |
| `scripts/fixtures/cache-bridge-diagnostics.ps1` | 79 | test driver/fixture | ps 0 / test.cs 1 / docs 1 | yes (1) |  | unstated | W2-deploy-T |
| `scripts/fixtures/checkpoint-output-windows-witness.ps1` | 24 | test driver/fixture | ps 0 / test.cs 0 / docs 1 | no |  | unstated | W2-checkpoint-T |
| `scripts/fixtures/deploy-server2-output-witness.ps1` | 123 | test driver/fixture | ps 0 / test.cs 1 / docs 0 | yes (1) |  | unstated | W2-deploy-T |
| `scripts/fixtures/evidence-recovery-windows-witness.ps1` | 37 | test driver/fixture | ps 0 / test.cs 0 / docs 0 | no |  | unstated | W2-ops/land-T |
| `scripts/fixtures/evidence-supplemental-windows-witness.ps1` | 37 | test driver/fixture | ps 0 / test.cs 0 / docs 1 | no |  | unstated | W2-ops/land-T |
| `scripts/fixtures/group76-native-diagnostics.ps1` | 59 | test driver/fixture | ps 0 / test.cs 0 / docs 1 | no |  | 7 only | W2-nightly-T |
| `scripts/fixtures/group77-windows-input-witness.ps1` | 96 | test driver/fixture | ps 0 / test.cs 0 / docs 1 | no |  | unstated | W2-nightly-T |
| `scripts/fixtures/nightly/c1325-diagnostic-records.ps1` | 105 | test driver/fixture | ps 2 / test.cs 0 / docs 4 | no |  | 5.1 named | W2-nightly-T |
| `scripts/fixtures/nightly/c1375-diagnostic-records.ps1` | 20 | test driver/fixture | ps 2 / test.cs 0 / docs 1 | no |  | unstated | W2-nightly-T |
| `scripts/fixtures/nightly/c1375-owned-tree.ps1` | 39 | test driver/fixture | ps 1 / test.cs 0 / docs 1 | no |  | 7 only | W2-nightly-T |
| `scripts/fixtures/nightly/d13-error-pair.ps1` | 86 | test driver/fixture | ps 2 / test.cs 0 / docs 4 | no |  | unstated | W2-nightly-T |
| `scripts/fixtures/rc-boundary-pack-golden/serializer-order.ps1` | 185 | test driver/fixture | ps 2 / test.cs 0 / docs 0 | no |  | 5.1 named | W2-rc-T |
| `scripts/fixtures/retired-temp-platform-witness.ps1` | 20 | test driver/fixture | ps 0 / test.cs 0 / docs 0 | no |  | unstated | W2-deploy-T |
| `scripts/fixtures/script-groups-windows-witness.ps1` | 180 | fixture: ScriptHarness witness | ps 0 / test.cs 0 / docs 1 | no |  | unstated | KEEP until harness retires |
| `scripts/fixtures/script-harness-cleanup-flow-witness.ps1` | 85 | fixture: ScriptHarness witness | ps 0 / test.cs 0 / docs 1 | no |  | unstated | KEEP until harness retires |
| `scripts/fixtures/script-harness-linux-admission-witness.ps1` | 14 | fixture: ScriptHarness witness | ps 0 / test.cs 0 / docs 0 | no |  | unstated | KEEP until harness retires |
| `scripts/fixtures/seed-smoke-targeting-packs.ps1` | 61 | test driver/fixture | ps 0 / test.cs 0 / docs 1 | no |  | 7 only | W2-nightly-T |
| `scripts/github-sync.ps1` | 126 | HTTP/ops tool | ps 0 / test.cs 1 / docs 10 | yes (1) |  | unstated | W2-http |
| `scripts/hangfire-dashboard.ps1` | 72 | HTTP/ops tool | ps 1 / test.cs 0 / docs 7 / C#:4 | no |  | 5.1 named | W2-http |
| `scripts/herdr-pane.ps1` | 99 | HTTP/ops tool | ps 0 / test.cs 1 / docs 5 | yes (1) |  | unstated | W2-http |
| `scripts/host-load.ps1` | 109 | cleanup/ops | ps 1 / test.cs 0 / docs 4 | no |  | 7 only | W2-ops |
| `scripts/impacted-classes.ps1` | 1375 | land/selection/orchestrator support | ps 4 / test.cs 4 / docs 87 | yes (4) |  | 5.1 named | W2-land |
| `scripts/inspect-checkpoint-temp.ps1` | 74 | checkpoint tooling | ps 0 / test.cs 1 / docs 3 | yes (1) |  | unstated | W2-checkpoint |
| `scripts/install-autostart.ps1` | 424 | bootstrap/restart/autostart | ps 6 / test.cs 1 / docs 14 | yes (1) |  | 5.1 named | KEEP (sec. 6) |
| `scripts/install-cleanup-task.ps1` | 37 | bootstrap/restart/autostart | ps 1 / test.cs 0 / docs 5 | no |  | unstated | KEEP (sec. 6) |
| `scripts/keep-six.ps1` | 134 | orchestrator HTTP tool | ps 2 / test.cs 2 / docs 13 / C#:1 | yes (2) |  | 5.1 named | S8 |
| `scripts/land-graph-land-refresh.ps1` | 387 | land/selection/orchestrator support | ps 1 / test.cs 2 / docs 10 / C#:1 | yes (2) |  | 5.1 named | W2-land |
| `scripts/lib/antiphon-caller.ps1` | 31 | lib (caller labels) | ps 4 / test.cs 1 / docs 3 | yes (1) |  | 5.1 named | S1 (C# twin; ps1 kept for ps1 callers) |
| `scripts/lib/build-driver-environment.ps1` | 68 | checkpoint tooling | ps 9 / test.cs 0 / docs 1 | no |  | 5.1 named | W2-checkpoint |
| `scripts/lib/build-slot.ps1` | 229 | build-slot gate | ps 24 / test.cs 9 / docs 626 / C#:4 | yes (9) |  | 7 only | S7 |
| `scripts/lib/c487-harness.ps1` | 394 | nightly/selection | ps 42 / test.cs 3 / docs 19 / C#:1 | yes (3) |  | 5.1 named | W2-nightly |
| `scripts/lib/checkpoint-file-reader.ps1` | 602 | checkpoint tooling | ps 15 / test.cs 5 / docs 12 | yes (5) |  | 7 only | W2-checkpoint |
| `scripts/lib/checkpoint-owned-output.ps1` | 85 | checkpoint tooling | ps 4 / test.cs 0 / docs 4 | no |  | 7 only | W2-checkpoint |
| `scripts/lib/checkpoint-process-runner.ps1` | 42 | checkpoint tooling | ps 17 / test.cs 4 / docs 4 | yes (4) |  | 7 only | W2-checkpoint |
| `scripts/lib/checkpoint-repeat.ps1` | 141 | checkpoint tooling | ps 2 / test.cs 0 / docs 4 | no |  | 5.1 named | W2-checkpoint |
| `scripts/lib/checkpoint-source.ps1` | 148 | checkpoint tooling | ps 3 / test.cs 3 / docs 14 | yes (3) |  | 7 only | W2-checkpoint |
| `scripts/lib/checkpoint-usage.ps1` | 824 | checkpoint tooling | ps 2 / test.cs 2 / docs 37 | yes (2) |  | 7 only | W2-checkpoint |
| `scripts/lib/client-npm-install.ps1` | 101 | client wrappers | ps 4 / test.cs 0 / docs 1 | no |  | 5.1 named | W2-client |
| `scripts/lib/dev-postgres-compose.ps1` | 83 | deploy/server2/postgres | ps 4 / test.cs 0 / docs 2 | no |  | 5.1 named | W2-deploy |
| `scripts/lib/evidence-policy.ps1` | 377 | land/selection/orchestrator support | ps 2 / test.cs 2 / docs 5 | yes (2) |  | 7 only | W2-land |
| `scripts/lib/land-audit-tool.ps1` | 116 | land/selection/orchestrator support | ps 3 / test.cs 2 / docs 1 / C#:1 | yes (2) |  | 5.1 named | W2-land |
| `scripts/lib/linux-compiler-evidence.ps1` | 51 | checkpoint tooling | ps 3 / test.cs 0 / docs 0 | no |  | unstated | W2-checkpoint |
| `scripts/lib/live-lane-attestation.ps1` | 284 | nightly/selection | ps 1 / test.cs 0 / docs 2 | no |  | 7 only | W2-nightly |
| `scripts/lib/live-lane-fingerprint.ps1` | 390 | nightly/selection | ps 3 / test.cs 0 / docs 2 | no |  | 7 only | W2-nightly |
| `scripts/lib/live-lane-manifest.ps1` | 3044 | nightly/selection | ps 1 / test.cs 0 / docs 3 | no |  | 7 only | W2-nightly |
| `scripts/lib/live-lane-outcome.ps1` | 200 | nightly/selection | ps 1 / test.cs 1 / docs 3 | yes (1) |  | 7 only | W2-nightly |
| `scripts/lib/live-lane-receipt.ps1` | 359 | nightly/selection | ps 2 / test.cs 0 / docs 2 | no |  | 7 only | W2-nightly |
| `scripts/lib/live-lane-run.ps1` | 234 | nightly/selection | ps 3 / test.cs 0 / docs 3 | no |  | 7 only | W2-nightly |
| `scripts/lib/live-startup-timing.ps1` | 37 | nightly/selection | ps 9 / test.cs 0 / docs 1 | no |  | 5.1 named | W2-nightly |
| `scripts/lib/nightly-common.ps1` | 1150 | nightly lib (PINNED) | ps 43 / test.cs 10 / docs 33 / C#:1 | yes (10) | **PIN** | 5.1 named | S3 (canonical JSON) / W2-nightly |
| `scripts/lib/nightly-core.ps1` | 341 | nightly loader (PINNED) | ps 15 / test.cs 4 / docs 11 / C#:1 | yes (4) | **PIN** | 7 only | S3-S4 / W2-nightly (retire loader) |
| `scripts/lib/nightly-coverage.ps1` | 1564 | nightly lib (PINNED) | ps 21 / test.cs 6 / docs 47 / C#:11 | yes (6) | **PIN** | 5.1 named | S4 (census) / W2-nightly |
| `scripts/lib/nightly-dispatch.ps1` | 248 | nightly/selection | ps 2 / test.cs 0 / docs 14 | no |  | 5.1 named | W2-nightly |
| `scripts/lib/nightly-failure-history.ps1` | 713 | nightly/selection | ps 6 / test.cs 0 / docs 15 / C#:1 | no |  | 5.1 named | W2-nightly |
| `scripts/lib/nightly-health.ps1` | 956 | nightly health (Windmill readiness) | ps 2 / test.cs 2 / docs 10 | yes (2) |  | 5.1 named | S5 |
| `scripts/lib/nightly-owned-process.ps1` | 54 | nightly/selection | ps 3 / test.cs 2 / docs 12 | yes (2) |  | 7 only | W2-nightly |
| `scripts/lib/nightly-policy.ps1` | 537 | nightly lib (PINNED) | ps 24 / test.cs 6 / docs 34 / C#:1 | yes (6) | **PIN** | 5.1 named | S3 |
| `scripts/lib/nightly-probe-supervisor.ps1` | 154 | nightly/selection | ps 2 / test.cs 0 / docs 2 | no |  | 7 only | W2-nightly |
| `scripts/lib/nightly-run-impl.ps1` | 1067 | nightly/selection | ps 7 / test.cs 2 / docs 40 | yes (2) |  | 5.1 named | W2-nightly |
| `scripts/lib/nightly-stopped-run-seed.ps1` | 590 | nightly/selection | ps 2 / test.cs 0 / docs 11 / C#:1 | no |  | 5.1 named | W2-nightly |
| `scripts/lib/nightly-tests-impl.ps1` | 2164 | nightly/selection | ps 19 / test.cs 6 / docs 77 / C#:6 | yes (6) |  | 5.1 named | W2-nightly |
| `scripts/lib/nightly-watchdog-admission.ps1` | 421 | nightly/selection | ps 2 / test.cs 0 / docs 4 | no |  | 5.1 named | W2-nightly |
| `scripts/lib/phase-journal-diagnostic.ps1` | 5 | nightly/selection | ps 1 / test.cs 0 / docs 0 | no |  | unstated | W2-nightly |
| `scripts/lib/post-land-early-warning.ps1` | 1178 | land/selection/orchestrator support | ps 1 / test.cs 1 / docs 3 | yes (1) |  | unstated | W2-land |
| `scripts/lib/qualification-graph.ps1` | 422 | rc/release/qualification | ps 1 / test.cs 0 / docs 2 | no |  | 7 only | W2-rc |
| `scripts/lib/qualification-host-stats.ps1` | 115 | nightly/selection | ps 0 / test.cs 1 / docs 1 | yes (1) |  | 5.1 named | W2-nightly |
| `scripts/lib/qualification-supervisor-heartbeat.ps1` | 644 | rc/release/qualification | ps 6 / test.cs 0 / docs 8 / C#:1 | no |  | 5.1 named | W2-rc |
| `scripts/lib/qualification-supervisor.ps1` | 324 | rc/release/qualification | ps 5 / test.cs 0 / docs 1 | no |  | 5.1 named | W2-rc |
| `scripts/lib/rc-admission.ps1` | 321 | rc/release/qualification | ps 6 / test.cs 0 / docs 10 | no |  | 7 only | W2-rc |
| `scripts/lib/rc-baseline.ps1` | 128 | rc/release/qualification | ps 1 / test.cs 0 / docs 5 | no |  | 7 only | W2-rc |
| `scripts/lib/rc-boundary.ps1` | 322 | rc/release/qualification | ps 5 / test.cs 0 / docs 7 | no |  | 5.1 named | W2-rc |
| `scripts/lib/rc-chunk-digest.ps1` | 183 | rc/release/qualification | ps 3 / test.cs 0 / docs 8 | no |  | 7 only | W2-rc |
| `scripts/lib/rc-forced-full.ps1` | 182 | rc/release/qualification | ps 2 / test.cs 0 / docs 5 | no |  | 7 only | W2-rc |
| `scripts/lib/rc-historical-evidence.ps1` | 23 | rc/release/qualification | ps 2 / test.cs 0 / docs 2 | no |  | 7 only | W2-rc |
| `scripts/lib/rc-incremental-plan.ps1` | 347 | rc/release/qualification | ps 3 / test.cs 0 / docs 5 | no |  | 7 only | W2-rc |
| `scripts/lib/rc-input-manifest.ps1` | 266 | rc/release/qualification | ps 3 / test.cs 1 / docs 3 | yes (1) |  | 7 only | W2-rc |
| `scripts/lib/rc-ledger.ps1` | 1100 | rc/release/qualification | ps 4 / test.cs 0 / docs 8 | no |  | 5.1 named | W2-rc |
| `scripts/lib/rc-project-selection.ps1` | 271 | rc/release/qualification | ps 5 / test.cs 0 / docs 9 / C#:1 | no |  | 7 only | W2-rc |
| `scripts/lib/rc-qualification-status.ps1` | 195 | rc/release/qualification | ps 6 / test.cs 0 / docs 11 | no |  | 5.1 named | W2-rc |
| `scripts/lib/release-authority.ps1` | 937 | rc/release/qualification | ps 7 / test.cs 3 / docs 26 | yes (3) |  | 5.1 named | W2-rc |
| `scripts/lib/release-gate.ps1` | 1111 | rc/release/qualification | ps 20 / test.cs 2 / docs 26 | yes (2) |  | 5.1 named | W2-rc |
| `scripts/lib/release-impact-scripts.ps1` | 808 | rc/release/qualification | ps 0 / test.cs 1 / docs 13 | yes (1) |  | 5.1 named | W2-rc |
| `scripts/lib/release-impact.ps1` | 127 | rc/release/qualification | ps 2 / test.cs 1 / docs 15 | yes (1) |  | 5.1 named | W2-rc |
| `scripts/lib/release-selection-worker.ps1` | 15 | rc/release/qualification | ps 1 / test.cs 0 / docs 0 | no |  | 7 only | W2-rc |
| `scripts/lib/release-selection.ps1` | 493 | rc/release/qualification | ps 11 / test.cs 1 / docs 21 / C#:2 | yes (1) |  | 5.1 named | W2-rc |
| `scripts/lib/runner-operator-token.ps1` | 13 | credential tool | ps 6 / test.cs 1 / docs 3 | yes (1) |  | unstated | S12 |
| `scripts/lib/test-phase-evidence.ps1` | 3516 | nightly/selection | ps 7 / test.cs 6 / docs 14 / C#:10 | yes (6) |  | 5.1 named | W2-nightly |
| `scripts/lib/test-phase-paths.ps1` | 61 | nightly/selection | ps 3 / test.cs 1 / docs 0 | yes (1) |  | 5.1 named | W2-nightly |
| `scripts/lib/test-selection.ps1` | 379 | nightly/selection | ps 14 / test.cs 2 / docs 19 / C#:3 | yes (2) |  | 7 only | W2-nightly |
| `scripts/lib/test-timings.ps1` | 218 | nightly/selection | ps 4 / test.cs 0 / docs 7 | no |  | 7 only | W2-nightly |
| `scripts/lib/toolchain-floor.ps1` | 157 | nightly/selection | ps 6 / test.cs 2 / docs 0 | yes (2) |  | 5.1 named | W2-nightly |
| `scripts/lib/windows-phone-home-env.ps1` | 293 | deploy/server2/postgres | ps 6 / test.cs 0 / docs 9 | no |  | 5.1 named | W2-deploy |
| `scripts/lint-client.ps1` | 87 | client wrappers | ps 4 / test.cs 0 / docs 4 | no |  | 5.1 named | W2-client |
| `scripts/live-lane-status.ps1` | 28 | nightly/selection | ps 3 / test.cs 0 / docs 2 | no |  | 7 only | W2-nightly |
| `scripts/logs.ps1` | 108 | HTTP/ops tool | ps 1 / test.cs 0 / docs 16 | no |  | unstated | W2-http |
| `scripts/measure-host-stats-overhead.ps1` | 200 | cleanup/ops | ps 0 / test.cs 0 / docs 3 | no |  | 7 only | W2-ops |
| `scripts/measure-linux-shared-compiler.ps1` | 171 | cleanup/ops | ps 1 / test.cs 0 / docs 3 | no |  | 7 only | W2-ops |
| `scripts/measure-session-state-cache.ps1` | 444 | cleanup/ops | ps 0 / test.cs 1 / docs 5 | yes (1) |  | 7 only | W2-ops |
| `scripts/merge-nightly-seeds.ps1` | 487 | nightly/selection | ps 1 / test.cs 0 / docs 9 | no |  | 5.1 named | W2-nightly |
| `scripts/migrate-stage-pins-to-cells.ps1` | 256 | HTTP/ops tool | ps 0 / test.cs 1 / docs 6 | yes (1) |  | 5.1 named | W2-http |
| `scripts/model-availability.ps1` | 151 | HTTP/ops tool | ps 2 / test.cs 1 / docs 9 | yes (1) |  | 5.1 named | W2-http |
| `scripts/new-review-scratch.ps1` | 31 | land/selection/orchestrator support | ps 1 / test.cs 1 / docs 3 / C#:1 | yes (1) |  | 7 only | W2-land |
| `scripts/nightly-health.ps1` | 41 | nightly health (Windmill readiness) | ps 1 / test.cs 2 / docs 10 / Windmill(5.1) | yes (2) |  | 5.1 named | S5 |
| `scripts/nightly-report.ps1` | 970 | nightly/selection | ps 9 / test.cs 2 / docs 18 | yes (2) |  | 5.1 named | W2-nightly |
| `scripts/nightly-run.ps1` | 178 | nightly/selection | ps 16 / test.cs 2 / docs 48 / Windmill(5.1) | yes (2) |  | 5.1 named | W2-nightly |
| `scripts/nightly-tests.ps1` | 121 | nightly/selection | ps 15 / test.cs 1 / docs 28 / C#:2 | yes (1) |  | 5.1 named | W2-nightly |
| `scripts/orchestration/trx-failures.ps1` | 50 | HTTP/ops tool | ps 1 / test.cs 0 / docs 2 | no |  | 5.1 named | W2-http |
| `scripts/orchestration/watch-master.ps1` | 42 | HTTP/ops tool | ps 1 / test.cs 0 / docs 1 | no |  | 5.1 named | W2-http |
| `scripts/orchestration/watch-qual.ps1` | 112 | rc/release/qualification | ps 2 / test.cs 0 / docs 6 | no |  | 5.1 named | W2-rc |
| `scripts/orchestration/watch-tasks.ps1` | 58 | HTTP/ops tool | ps 1 / test.cs 0 / docs 1 | no |  | 5.1 named | W2-http |
| `scripts/orchestrator-workspace.ps1` | 302 | land/selection/orchestrator support | ps 0 / test.cs 3 / docs 3 / C#:3 | yes (3) |  | 7 only | W2-land |
| `scripts/pg-qps.ps1` | 383 | deploy/server2/postgres | ps 1 / test.cs 0 / docs 3 | no |  | 5.1 named | W2-deploy |
| `scripts/pipeline-definition.ps1` | 85 | HTTP/ops tool | ps 0 / test.cs 2 / docs 3 | yes (2) |  | 5.1 named | W2-http |
| `scripts/post-land-early-warning.ps1` | 144 | land/selection/orchestrator support | ps 0 / test.cs 1 / docs 3 | yes (1) |  | unstated | W2-land |
| `scripts/postgres-cutover-monitor.ps1` | 1061 | deploy/server2/postgres | ps 1 / test.cs 0 / docs 7 | no |  | 5.1 named | W2-deploy |
| `scripts/postgres-jit-window.ps1` | 139 | deploy/server2/postgres | ps 0 / test.cs 1 / docs 4 | yes (1) |  | 5.1 named | W2-deploy |
| `scripts/postgres-snapshot.ps1` | 2303 | deploy/server2/postgres | ps 5 / test.cs 0 / docs 17 | no |  | 5.1 named | W2-deploy |
| `scripts/prepare-card0490-linux-assets.ps1` | 77 | one-off card proof | ps 0 / test.cs 0 / docs 1 | no |  | unstated | DELETE? (S0 audit) |
| `scripts/project.ps1` | 272 | HTTP/ops tool | ps 0 / test.cs 2 / docs 12 | yes (2) |  | 5.1 named | W2-http |
| `scripts/promote-release.ps1` | 233 | rc/release/qualification | ps 3 / test.cs 1 / docs 12 | yes (1) |  | 5.1 named | W2-rc |
| `scripts/prune-test-data.ps1` | 387 | deploy/server2/postgres | ps 0 / test.cs 0 / docs 6 | no |  | 5.1 named | W2-deploy |
| `scripts/publish-release.ps1` | 436 | rc/release/qualification | ps 5 / test.cs 0 / docs 22 | no |  | 5.1 named | W2-rc |
| `scripts/qualification-eta.ps1` | 228 | nightly/selection | ps 3 / test.cs 0 / docs 1 | no |  | 5.1 named | W2-nightly |
| `scripts/qualification/launch-rc.ps1` | 77 | rc/release/qualification | ps 6 / test.cs 1 / docs 15 | yes (1) |  | 7 only | W2-rc |
| `scripts/qualification/observe.ps1` | 45 | rc/release/qualification | ps 4 / test.cs 0 / docs 3 | no |  | 5.1 named | W2-rc |
| `scripts/qualification/preflight.ps1` | 26 | rc/release/qualification | ps 1 / test.cs 0 / docs 5 | no |  | 5.1 named | W2-rc |
| `scripts/qualification/qualification-stop.ps1` | 436 | rc/release/qualification | ps 2 / test.cs 0 / docs 2 | no |  | 5.1 named | W2-rc |
| `scripts/qualification/start-supervisor.ps1` | 86 | rc/release/qualification | ps 3 / test.cs 0 / docs 3 | no |  | 5.1 named | W2-rc |
| `scripts/qualification/stop-supervisor.ps1` | 46 | rc/release/qualification | ps 2 / test.cs 0 / docs 5 | no |  | 5.1 named | W2-rc |
| `scripts/qualification/supervise-rc.ps1` | 126 | rc/release/qualification | ps 4 / test.cs 0 / docs 12 | no |  | 5.1 named | W2-rc |
| `scripts/rc-ledger-import.ps1` | 52 | rc/release/qualification | ps 1 / test.cs 0 / docs 4 | no |  | 7 only | W2-rc |
| `scripts/rc-ledger-plan.ps1` | 12 | rc/release/qualification | ps 1 / test.cs 0 / docs 7 | no |  | 7 only | W2-rc |
| `scripts/rc-ledger-stamp-duration.ps1` | 30 | rc/release/qualification | ps 1 / test.cs 0 / docs 3 | no |  | 5.1 named | W2-rc |
| `scripts/rc-ledger-status.ps1` | 18 | rc/release/qualification | ps 1 / test.cs 0 / docs 7 | no |  | 7 only | W2-rc |
| `scripts/rc-qualification-status.ps1` | 19 | rc/release/qualification | ps 5 / test.cs 0 / docs 11 | no |  | 5.1 named | W2-rc |
| `scripts/reap-orphaned-pty-hosts.ps1` | 272 | cleanup/ops | ps 0 / test.cs 0 / docs 10 | no |  | 5.1 named | W2-ops |
| `scripts/reap-zombie-agents.ps1` | 996 | cleanup/ops | ps 1 / test.cs 0 / docs 9 / C#:2 | no |  | 5.1 named | W2-ops |
| `scripts/recover-repository-children.ps1` | 174 | cleanup/ops | ps 2 / test.cs 7 / docs 17 / C#:4 | yes (7) |  | 5.1 named | W2-ops |
| `scripts/refresh-nightly-class-costs.ps1` | 127 | nightly/selection | ps 1 / test.cs 0 / docs 7 | no |  | 5.1 named | W2-nightly |
| `scripts/refresh-server2-github-token.ps1` | 107 | deploy/server2/postgres | ps 0 / test.cs 3 / docs 4 | yes (3) |  | 7 only | W2-deploy |
| `scripts/regenerate-derived-impact-index.ps1` | 34 | land/selection/orchestrator support | ps 1 / test.cs 1 / docs 38 | yes (1) |  | 7 only | W2-land |
| `scripts/register-postgres-snapshot.ps1` | 114 | deploy/server2/postgres | ps 1 / test.cs 0 / docs 2 | no |  | 5.1 named | W2-deploy |
| `scripts/register-release-gates.ps1` | 291 | rc/release/qualification | ps 1 / test.cs 0 / docs 8 | no |  | 5.1 named | W2-rc |
| `scripts/release-candidate.ps1` | 325 | rc/release/qualification | ps 4 / test.cs 0 / docs 16 | no |  | 5.1 named | W2-rc |
| `scripts/release-cut.ps1` | 200 | rc/release/qualification | ps 4 / test.cs 0 / docs 23 / Windmill(5.1) | no |  | 5.1 named | W2-rc |
| `scripts/release-status.ps1` | 93 | rc/release/qualification | ps 1 / test.cs 0 / docs 6 | no |  | 5.1 named | W2-rc |
| `scripts/remove-review-scratch.ps1` | 84 | land/selection/orchestrator support | ps 1 / test.cs 1 / docs 2 | yes (1) |  | 7 only | W2-land |
| `scripts/restart-apphost.ps1` | 470 | bootstrap/restart/autostart | ps 18 / test.cs 5 / docs 118 / C#:2 | yes (5) |  | 5.1 named | KEEP (sec. 6) |
| `scripts/restart-linux-server.ps1` | 333 | deploy/server2/postgres | ps 2 / test.cs 3 / docs 10 | yes (3) |  | 5.1 named | W2-deploy |
| `scripts/restart-session-runner.ps1` | 31 | bootstrap/restart/autostart | ps 8 / test.cs 5 / docs 48 / C#:3 | yes (5) |  | unstated | KEEP (sec. 6) |
| `scripts/retire-runner-volumes.ps1` | 166 | deploy/server2/postgres | ps 0 / test.cs 2 / docs 9 | yes (2) |  | 7 only | W2-deploy |
| `scripts/routing-pin.ps1` | 295 | HTTP/ops tool | ps 3 / test.cs 4 / docs 22 / C#:1 | yes (4) |  | 5.1 named | W2-http |
| `scripts/run-card-1885-bounded-experiment.ps1` | 83 | one-off card proof | ps 0 / test.cs 0 / docs 2 | no |  | 7 only | DELETE? (S0 audit) |
| `scripts/run-checkpoint.ps1` | 1520 | checkpoint tooling | ps 15 / test.cs 14 / docs 197 / C#:2 | yes (14) |  | 7 only | W2-checkpoint |
| `scripts/run-daemon.ps1` | 207 | bootstrap/restart/autostart | ps 2 / test.cs 5 / docs 22 | yes (5) |  | 5.1 named | KEEP (sec. 6) |
| `scripts/run-live-lane.ps1` | 19 | nightly/selection | ps 2 / test.cs 0 / docs 4 | no |  | 7 only | W2-nightly |
| `scripts/run-tests-watched.ps1` | 134 | cleanup/ops | ps 0 / test.cs 0 / docs 12 | no |  | unstated | W2-ops |
| `scripts/runner-drain.ps1` | 140 | HTTP/ops tool | ps 0 / test.cs 2 / docs 9 | yes (2) |  | 5.1 named | W2-http |
| `scripts/runner-slots.ps1` | 67 | orchestrator HTTP tool | ps 3 / test.cs 1 / docs 21 / C#:3 | yes (1) |  | 5.1 named | S8 |
| `scripts/schedule.ps1` | 334 | HTTP/ops tool | ps 0 / test.cs 1 / docs 8 | yes (1) |  | 5.1 named | W2-http |
| `scripts/select-rc-project-batches.ps1` | 19 | nightly/selection | ps 0 / test.cs 0 / docs 1 | no |  | 7 only | W2-nightly |
| `scripts/select-test-rosters.ps1` | 41 | nightly/selection | ps 9 / test.cs 1 / docs 12 / C#:2 | yes (1) |  | 7 only | W2-nightly |
| `scripts/selector-replay-host.ps1` | 54 | nightly/selection | ps 1 / test.cs 1 / docs 1 | yes (1) |  | 7 only | W2-nightly |
| `scripts/server2-repository-mirrors.ps1` | 204 | deploy/server2/postgres | ps 0 / test.cs 3 / docs 5 | yes (3) |  | 5.1 named | W2-deploy |
| `scripts/session-runner-restart-health.ps1` | 312 | bootstrap/restart/autostart | ps 2 / test.cs 6 / docs 15 | yes (6) |  | 5.1 named | KEEP (sec. 6) |
| `scripts/set-apphost-maintenance.ps1` | 127 | bootstrap/restart/autostart | ps 6 / test.cs 1 / docs 10 | yes (1) |  | 5.1 named | KEEP (sec. 6) |
| `scripts/specialist-routing.ps1` | 53 | HTTP/ops tool | ps 0 / test.cs 1 / docs 2 | yes (1) |  | 5.1 named | W2-http |
| `scripts/stage-value-report.ps1` | 201 | land/selection/orchestrator support | ps 1 / test.cs 0 / docs 7 | no |  | 5.1 named | W2-land |
| `scripts/start-apphost.ps1` | 3 | bootstrap/restart/autostart | ps 0 / test.cs 1 / docs 0 | yes (1) |  | unstated | KEEP (sec. 6) |
| `scripts/start-windows-phone-home-runner.ps1` | 91 | deploy/server2/postgres | ps 1 / test.cs 0 / docs 9 | no |  | 7 only | W2-deploy |
| `scripts/status-snapshot.ps1` | 192 | orchestrator tool | ps 1 / test.cs 3 / docs 9 / C#:1 | yes (3) |  | 5.1 named | S1 |
| `scripts/summarize-test-phases.ps1` | 53 | nightly/selection | ps 1 / test.cs 2 / docs 5 | yes (2) |  | 5.1 named | W2-nightly |
| `scripts/summarize-test-trace.ps1` | 86 | nightly/selection | ps 0 / test.cs 1 / docs 1 | yes (1) |  | 7 only | W2-nightly |
| `scripts/task-commit.ps1` | 70 | land/selection/orchestrator support | ps 0 / test.cs 2 / docs 3 / C#:2 | yes (2) |  | 5.1 named | W2-land |
| `scripts/test-apphost-alert-sinks.ps1` | 108 | harness for kept script | ps 0 / test.cs 0 / docs 2 | no |  | 5.1 named | KEEP-T |
| `scripts/test-apphost-dead-launch.ps1` | 267 | harness for kept script | ps 0 / test.cs 0 / docs 2 | no |  | 5.1 named | KEEP-T |
| `scripts/test-apphost-git-index-lock.ps1` | 108 | harness for kept script | ps 0 / test.cs 0 / docs 4 | no |  | 5.1 named | KEEP-T |
| `scripts/test-apphost-graceful-stop.ps1` | 668 | harness for kept script | ps 2 / test.cs 0 / docs 4 | no |  | 5.1 named | KEEP-T |
| `scripts/test-apphost-health-window.ps1` | 431 | harness for kept script | ps 0 / test.cs 0 / docs 0 | no |  | 5.1 named | KEEP-T |
| `scripts/test-apphost-lock-age.ps1` | 239 | harness for kept script | ps 1 / test.cs 0 / docs 8 | no |  | 5.1 named | KEEP-T |
| `scripts/test-apphost-lock-hints.ps1` | 155 | harness for kept script | ps 0 / test.cs 0 / docs 0 | no |  | 5.1 named | KEEP-T |
| `scripts/test-apphost-main-worktree-guard.ps1` | 139 | harness for kept script | ps 2 / test.cs 0 / docs 8 | no |  | 5.1 named | KEEP-T |
| `scripts/test-apphost-probe-class.ps1` | 127 | harness for kept script | ps 1 / test.cs 0 / docs 3 | no |  | 5.1 named | KEEP-T |
| `scripts/test-apphost-server-version.ps1` | 653 | harness for kept script | ps 0 / test.cs 0 / docs 4 | no |  | 5.1 named | KEEP-T |
| `scripts/test-astra-fixes-b.ps1` | 411 | test driver/fixture | ps 0 / test.cs 1 / docs 0 | yes (1) |  | 7 only | W2-nightly-T |
| `scripts/test-build-driver-environment.ps1` | 189 | test driver/fixture | ps 0 / test.cs 0 / docs 8 | no |  | 7 only | W2-checkpoint-T |
| `scripts/test-build-slot.ps1` | 943 | build-slot gate | ps 1 / test.cs 1 / docs 11 | yes (1) |  | 5.1 named | S7 |
| `scripts/test-c1361-windows-input.ps1` | 140 | test driver/fixture | ps 0 / test.cs 0 / docs 2 | no |  | 7 only | W2-nightly-T |
| `scripts/test-c1989-affected.ps1` | 52 | test driver/fixture | ps 0 / test.cs 0 / docs 1 | no |  | unstated | W2-nightly-T |
| `scripts/test-c1989-control-window.ps1` | 250 | test driver/fixture | ps 0 / test.cs 0 / docs 2 | no |  | unstated | W2-nightly-T |
| `scripts/test-cache-build-slot-inputs.ps1` | 53 | test driver/fixture | ps 0 / test.cs 0 / docs 1 | no |  | 7 only | W2-checkpoint-T |
| `scripts/test-card0490-native.ps1` | 28 | test driver | ps 0 / test.cs 1 / docs 3 | yes (1) |  | unstated | W2-deploy-T |
| `scripts/test-change-aware-release.ps1` | 412 | test driver/fixture | ps 0 / test.cs 1 / docs 5 | yes (1) |  | 7 only | W2-rc-T |
| `scripts/test-checkpoint-bounded-reader.ps1` | 74 | test driver/fixture | ps 0 / test.cs 0 / docs 3 | no |  | unstated | W2-checkpoint-T |
| `scripts/test-checkpoint-compiled-facade.ps1` | 74 | test driver/fixture | ps 0 / test.cs 1 / docs 1 | yes (1) |  | 7 only | W2-checkpoint-T |
| `scripts/test-checkpoint-git-encoding.ps1` | 125 | test driver/fixture | ps 0 / test.cs 1 / docs 1 | yes (1) |  | 7 only | W2-checkpoint-T |
| `scripts/test-checkpoint-large-stamp.ps1` | 215 | test driver/fixture | ps 1 / test.cs 1 / docs 4 | yes (1) |  | 7 only | W2-checkpoint-T |
| `scripts/test-checkpoint-output-inventory.ps1` | 309 | test driver/fixture | ps 0 / test.cs 1 / docs 1 | yes (1) |  | 7 only | W2-checkpoint-T |
| `scripts/test-checkpoint-owned-output.ps1` | 134 | test driver/fixture | ps 0 / test.cs 1 / docs 1 | yes (1) |  | 7 only | W2-checkpoint-T |
| `scripts/test-checkpoint-placement-skip.ps1` | 53 | test driver/fixture | ps 0 / test.cs 0 / docs 4 | no |  | unstated | W2-checkpoint-T |
| `scripts/test-checkpoint-pool-saturation.ps1` | 65 | test driver/fixture | ps 0 / test.cs 1 / docs 0 | yes (1) |  | 7 only | W2-checkpoint-T |
| `scripts/test-checkpoint-reader-deadline.ps1` | 298 | test driver/fixture | ps 1 / test.cs 1 / docs 2 | yes (1) |  | 7 only | W2-checkpoint-T |
| `scripts/test-checkpoint-stamp-ps51-parse.ps1` | 46 | test driver/fixture | ps 1 / test.cs 0 / docs 2 | no |  | 5.1 named | W2-checkpoint-T |
| `scripts/test-checkpoint-stamp.ps1` | 61 | test driver/fixture | ps 0 / test.cs 1 / docs 2 | yes (1) |  | 7 only | W2-checkpoint-T |
| `scripts/test-class-cost-measurement.ps1` | 307 | test driver/fixture | ps 0 / test.cs 1 / docs 1 | yes (1) |  | 5.1 named | W2-nightly-T |
| `scripts/test-cleanup-claude-sessions.ps1` | 467 | test driver/fixture | ps 1 / test.cs 0 / docs 3 | no |  | 5.1 named | W2-ops/land-T |
| `scripts/test-cleanup-codex-test-residue.ps1` | 121 | test driver/fixture | ps 2 / test.cs 0 / docs 2 | no |  | 5.1 named | W2-ops/land-T |
| `scripts/test-client-gate-exit.ps1` | 396 | test driver | ps 0 / test.cs 0 / docs 3 | no |  | 5.1 named | W2-client-T |
| `scripts/test-client-lint.ps1` | 139 | test driver | ps 0 / test.cs 0 / docs 2 | no |  | 5.1 named | W2-client-T |
| `scripts/test-client-mode.ps1` | 60 | test driver | ps 1 / test.cs 0 / docs 1 | no |  | 5.1 named | W2-client-T |
| `scripts/test-client-typecheck.ps1` | 117 | test driver | ps 0 / test.cs 0 / docs 2 | no |  | 5.1 named | W2-client-T |
| `scripts/test-client.ps1` | 139 | client wrappers | ps 11 / test.cs 2 / docs 184 | yes (2) |  | 5.1 named | W2-client |
| `scripts/test-cutover-postgres-server2.ps1` | 2326 | test driver/fixture | ps 0 / test.cs 1 / docs 14 | yes (1) |  | 5.1 named | W2-deploy-T |
| `scripts/test-cutover-postgres-verify.ps1` | 759 | test driver/fixture | ps 0 / test.cs 0 / docs 10 | no |  | 5.1 named | W2-deploy-T |
| `scripts/test-deploy-am-service-readiness.ps1` | 143 | test driver/fixture | ps 1 / test.cs 0 / docs 2 | no |  | unstated | W2-deploy-T |
| `scripts/test-deploy-am-service.ps1` | 469 | test driver/fixture | ps 1 / test.cs 1 / docs 9 | yes (1) |  | 5.1 named | W2-deploy-T |
| `scripts/test-deploy-nightly-watchdog.ps1` | 253 | test driver/fixture | ps 1 / test.cs 2 / docs 3 | yes (2) |  | 5.1 named | W2-nightly-T |
| `scripts/test-deploy-server2-jq.ps1` | 90 | test driver/fixture | ps 0 / test.cs 2 / docs 13 | yes (2) |  | unstated | W2-deploy-T |
| `scripts/test-deploy-server2.ps1` | 554 | test driver/fixture | ps 1 / test.cs 1 / docs 25 | yes (1) |  | unstated | W2-deploy-T |
| `scripts/test-derived-impact-driver.ps1` | 58 | test driver/fixture | ps 0 / test.cs 0 / docs 6 | no |  | 7 only | W2-checkpoint-T |
| `scripts/test-desktop-slot-provision.ps1` | 502 | operator desktop | ps 0 / test.cs 0 / docs 2 | no |  | 7 only | W3-desktop |
| `scripts/test-desktop-win-phase1-secrets.ps1` | 773 | operator desktop secret custody | ps 0 / test.cs 0 / docs 1 | no |  | unstated | W3-desktop (LAST) |
| `scripts/test-dev-postgres-optout.ps1` | 199 | test driver/fixture | ps 0 / test.cs 0 / docs 1 | no |  | 5.1 named | W2-deploy-T |
| `scripts/test-docker-container.ps1` | 70 | test driver/fixture | ps 0 / test.cs 2 / docs 5 | yes (2) |  | unstated | W2-deploy-T |
| `scripts/test-docker.ps1` | 118 | test driver/fixture | ps 0 / test.cs 1 / docs 3 | yes (1) |  | unstated | W2-deploy-T |
| `scripts/test-duration-tripwire.ps1` | 145 | test driver/fixture | ps 1 / test.cs 2 / docs 19 | yes (2) |  | unstated | W2-nightly-T |
| `scripts/test-expected-platform-skips.ps1` | 306 | test driver/fixture | ps 1 / test.cs 1 / docs 0 | yes (1) |  | 5.1 named | W2-nightly-T |
| `scripts/test-fake-provider-layout.ps1` | 56 | test driver/fixture | ps 0 / test.cs 0 / docs 4 | no |  | 7 only | W2-checkpoint-T |
| `scripts/test-hooks.ps1` | 35 | harness for kept script | ps 1 / test.cs 0 / docs 4 | no |  | 5.1 named | KEEP-T |
| `scripts/test-host-load.ps1` | 17 | test driver/fixture | ps 0 / test.cs 2 / docs 1 | yes (2) |  | 7 only | W2-ops/land-T |
| `scripts/test-isolated-helper-build.ps1` | 155 | test driver/fixture | ps 0 / test.cs 0 / docs 5 | no |  | 7 only | W2-checkpoint-T |
| `scripts/test-job-status.ps1` | 563 | test driver/fixture | ps 0 / test.cs 0 / docs 5 | no |  | 5.1 named | W2-rc-T |
| `scripts/test-land-git-resolution.ps1` | 43 | test driver/fixture | ps 0 / test.cs 0 / docs 0 | no |  | unstated | W2-ops/land-T |
| `scripts/test-land-graph-c1528-fixtures.ps1` | 74 | test driver/fixture | ps 0 / test.cs 0 / docs 4 | no |  | 7 only | W2-ops/land-T |
| `scripts/test-linux-shared-compiler.ps1` | 225 | test driver/fixture | ps 0 / test.cs 0 / docs 4 | no |  | 7 only | W2-checkpoint-T |
| `scripts/test-live-lane-attestation.ps1` | 271 | test driver/fixture | ps 0 / test.cs 1 / docs 2 | yes (1) |  | 7 only | W2-nightly-T |
| `scripts/test-live-lane-delivery.ps1` | 56 | test driver/fixture | ps 0 / test.cs 0 / docs 0 | no |  | unstated | W2-nightly-T |
| `scripts/test-live-lane-evidence.ps1` | 171 | test driver/fixture | ps 0 / test.cs 1 / docs 4 | yes (1) |  | 5.1 named | W2-nightly-T |
| `scripts/test-live-lane-fingerprint.ps1` | 367 | test driver/fixture | ps 0 / test.cs 1 / docs 2 | yes (1) |  | 7 only | W2-nightly-T |
| `scripts/test-live-lane-isolation.ps1` | 104 | test driver/fixture | ps 0 / test.cs 0 / docs 4 | no |  | unstated | W2-nightly-T |
| `scripts/test-live-lane-manifest.ps1` | 646 | test driver/fixture | ps 1 / test.cs 2 / docs 3 | yes (2) |  | 5.1 named | W2-nightly-T |
| `scripts/test-live-lane-outcome.ps1` | 340 | test driver/fixture | ps 0 / test.cs 1 / docs 3 | yes (1) |  | 7 only | W2-nightly-T |
| `scripts/test-live-lane-receipt.ps1` | 299 | test driver/fixture | ps 0 / test.cs 1 / docs 4 | yes (1) |  | 7 only | W2-nightly-T |
| `scripts/test-live-lane-run.ps1` | 400 | test driver + pin owner | ps 0 / test.cs 1 / docs 43 | yes (1) |  | 7 only | S2 (digest) / W2-nightly |
| `scripts/test-live-lane-startup.ps1` | 56 | test driver/fixture | ps 0 / test.cs 0 / docs 0 | no |  | unstated | W2-nightly-T |
| `scripts/test-live-startup-timing.ps1` | 15 | test driver/fixture | ps 0 / test.cs 1 / docs 1 | yes (1) |  | unstated | W2-nightly-T |
| `scripts/test-logs.ps1` | 38 | test driver/fixture | ps 0 / test.cs 0 / docs 0 | no |  | 5.1 named | W2-ops/land-T |
| `scripts/test-longpaths-checkout.ps1` | 372 | test driver/fixture | ps 0 / test.cs 1 / docs 1 | yes (1) |  | 5.1 named | W2-deploy-T |
| `scripts/test-mechanical-pc-contract.ps1` | 712 | test driver/fixture | ps 0 / test.cs 1 / docs 4 | yes (1) |  | 5.1 named | W2-checkpoint-T |
| `scripts/test-nightly-core.ps1` | 1349 | test driver/fixture | ps 0 / test.cs 1 / docs 9 | yes (1) |  | 7 only | W2-nightly-T |
| `scripts/test-nightly-failed-first.ps1` | 1650 | test driver/fixture | ps 1 / test.cs 3 / docs 6 | yes (3) |  | 5.1 named | W2-nightly-T |
| `scripts/test-nightly-health.ps1` | 878 | nightly health (Windmill readiness) | ps 0 / test.cs 3 / docs 9 | yes (3) |  | 5.1 named | S5 |
| `scripts/test-nightly-native.ps1` | 360 | test driver/fixture | ps 0 / test.cs 2 / docs 7 | yes (2) |  | 7 only | W2-nightly-T |
| `scripts/test-nightly-probe-supervisor.ps1` | 170 | test driver/fixture | ps 0 / test.cs 1 / docs 3 | yes (1) |  | 7 only | W2-nightly-T |
| `scripts/test-nightly-report.ps1` | 853 | test driver/fixture | ps 1 / test.cs 0 / docs 8 | no |  | 5.1 named | W2-nightly-T |
| `scripts/test-nightly-run.ps1` | 1540 | test driver/fixture | ps 1 / test.cs 6 / docs 17 | yes (6) |  | 5.1 named | W2-nightly-T |
| `scripts/test-nightly-stopped-seed.ps1` | 1640 | test driver/fixture | ps 0 / test.cs 1 / docs 2 | yes (1) |  | 5.1 named | W2-nightly-T |
| `scripts/test-nightly-summary-witness.ps1` | 219 | test driver/fixture | ps 0 / test.cs 1 / docs 0 | yes (1) |  | 7 only | W2-nightly-T |
| `scripts/test-nightly-tests.ps1` | 3109 | test driver/fixture | ps 1 / test.cs 7 / docs 41 | yes (7) |  | 5.1 named | W2-nightly-T |
| `scripts/test-orchestration-watchers.ps1` | 215 | test driver/fixture | ps 0 / test.cs 1 / docs 1 | yes (1) |  | 5.1 named | W2-ops/land-T |
| `scripts/test-os-witnesses.ps1` | 286 | test driver/fixture | ps 0 / test.cs 0 / docs 4 | no |  | 7 only | W2-ops/land-T |
| `scripts/test-pg-qps.ps1` | 359 | test driver/fixture | ps 0 / test.cs 0 / docs 2 | no |  | 7 only | W2-deploy-T |
| `scripts/test-postgres-cutover-monitor.ps1` | 1022 | test driver/fixture | ps 0 / test.cs 0 / docs 5 | no |  | 5.1 named | W2-deploy-T |
| `scripts/test-postgres-snapshot.ps1` | 2709 | test driver/fixture | ps 0 / test.cs 1 / docs 10 | yes (1) |  | 5.1 named | W2-deploy-T |
| `scripts/test-qualification-eta.ps1` | 198 | test driver/fixture | ps 0 / test.cs 1 / docs 1 | yes (1) |  | 5.1 named | W2-nightly-T |
| `scripts/test-qualification-graph.ps1` | 421 | test driver/fixture | ps 1 / test.cs 1 / docs 2 | yes (1) |  | 7 only | W2-nightly-T |
| `scripts/test-qualification-stop.ps1` | 622 | test driver/fixture | ps 0 / test.cs 0 / docs 2 | no |  | 5.1 named | W2-nightly-T |
| `scripts/test-qualification-supervisor-heartbeat.ps1` | 267 | test driver/fixture | ps 0 / test.cs 1 / docs 3 | yes (1) |  | 5.1 named | W2-nightly-T |
| `scripts/test-qualification-supervisor.ps1` | 517 | test driver/fixture | ps 0 / test.cs 1 / docs 3 | yes (1) |  | 5.1 named | W2-nightly-T |
| `scripts/test-qualification-timing.ps1` | 2012 | test driver/fixture | ps 0 / test.cs 1 / docs 2 | yes (1) |  | 7 only | W2-nightly-T |
| `scripts/test-rc-admission-reader.ps1` | 114 | test driver/fixture | ps 2 / test.cs 0 / docs 0 | no |  | 7 only | W2-rc-T |
| `scripts/test-rc-admission.ps1` | 138 | test driver/fixture | ps 0 / test.cs 1 / docs 3 | yes (1) |  | 7 only | W2-rc-T |
| `scripts/test-rc-baseline.ps1` | 104 | test driver/fixture | ps 0 / test.cs 1 / docs 3 | yes (1) |  | 7 only | W2-rc-T |
| `scripts/test-rc-boundary-pack-golden.ps1` | 294 | test driver/fixture | ps 0 / test.cs 0 / docs 0 | no |  | 7 only | W2-rc-T |
| `scripts/test-rc-boundary-pack-real-tree.ps1` | 130 | test driver/fixture | ps 0 / test.cs 0 / docs 1 | no |  | 7 only | W2-rc-T |
| `scripts/test-rc-boundary-pack.ps1` | 195 | test driver/fixture | ps 0 / test.cs 1 / docs 4 | yes (1) |  | 7 only | W2-rc-T |
| `scripts/test-rc-boundary.ps1` | 353 | test driver/fixture | ps 0 / test.cs 1 / docs 9 | yes (1) |  | 5.1 named | W2-rc-T |
| `scripts/test-rc-chunk-digest.ps1` | 227 | test driver/fixture | ps 0 / test.cs 1 / docs 3 | yes (1) |  | 7 only | W2-rc-T |
| `scripts/test-rc-coverage-admission.ps1` | 152 | test driver/fixture | ps 0 / test.cs 1 / docs 2 | yes (1) |  | 7 only | W2-rc-T |
| `scripts/test-rc-forced-full.ps1` | 196 | test driver/fixture | ps 0 / test.cs 1 / docs 3 | yes (1) |  | 7 only | W2-rc-T |
| `scripts/test-rc-historical-evidence.ps1` | 142 | test driver/fixture | ps 0 / test.cs 1 / docs 1 / C#:1 | yes (1) |  | 7 only | W2-rc-T |
| `scripts/test-rc-input-manifest.ps1` | 191 | test driver/fixture | ps 0 / test.cs 1 / docs 1 | yes (1) |  | 7 only | W2-rc-T |
| `scripts/test-rc-ledger-duration.ps1` | 244 | test driver/fixture | ps 0 / test.cs 1 / docs 0 | yes (1) |  | 5.1 named | W2-rc-T |
| `scripts/test-rc-ledger-status.ps1` | 158 | test driver/fixture | ps 0 / test.cs 1 / docs 3 | yes (1) |  | 7 only | W2-rc-T |
| `scripts/test-rc-ledger.ps1` | 411 | test driver/fixture | ps 0 / test.cs 1 / docs 2 | yes (1) |  | 5.1 named | W2-rc-T |
| `scripts/test-rc-platform.ps1` | 1181 | test driver/fixture | ps 0 / test.cs 2 / docs 18 | yes (2) |  | 7 only | W2-rc-T |
| `scripts/test-rc-project-selection.ps1` | 132 | test driver/fixture | ps 1 / test.cs 1 / docs 3 | yes (1) |  | 7 only | W2-rc-T |
| `scripts/test-rc-roster-repairs.ps1` | 91 | test driver/fixture | ps 1 / test.cs 1 / docs 2 | yes (1) |  | 5.1 named | W2-rc-T |
| `scripts/test-rc-speed-os-witness.ps1` | 46 | test driver/fixture | ps 1 / test.cs 1 / docs 2 | yes (1) |  | 5.1 named | W2-rc-T |
| `scripts/test-reap-zombie-agents.ps1` | 615 | test driver/fixture | ps 1 / test.cs 0 / docs 2 | no |  | 5.1 named | W2-ops/land-T |
| `scripts/test-release-gate.ps1` | 5232 | test driver/fixture | ps 4 / test.cs 15 / docs 33 | yes (15) |  | 5.1 named | W2-rc-T |
| `scripts/test-release-selection-cli.ps1` | 332 | test driver/fixture | ps 0 / test.cs 2 / docs 5 | yes (2) |  | 7 only | W2-rc-T |
| `scripts/test-release-selection-hop-seed.ps1` | 214 | test driver/fixture | ps 0 / test.cs 1 / docs 0 | yes (1) |  | 5.1 named | W2-rc-T |
| `scripts/test-restart-linux-server.ps1` | 451 | test driver/fixture | ps 0 / test.cs 1 / docs 0 | yes (1) |  | 5.1 named | W2-deploy-T |
| `scripts/test-run-checkpoint.ps1` | 1386 | test driver/fixture | ps 3 / test.cs 4 / docs 32 | yes (4) |  | 7 only | W2-checkpoint-T |
| `scripts/test-scripts-lane-repairs.ps1` | 154 | test driver/fixture | ps 0 / test.cs 1 / docs 2 | yes (1) |  | unstated | W2-nightly-T |
| `scripts/test-selector-census.ps1` | 269 | test driver/fixture | ps 0 / test.cs 0 / docs 3 | no |  | 7 only | W2-nightly-T |
| `scripts/test-server-docker-inputs.ps1` | 45 | test driver/fixture | ps 0 / test.cs 0 / docs 1 / CI | no |  | 7 only | W2-deploy-T |
| `scripts/test-server2-postgres-compose.ps1` | 995 | test driver/fixture | ps 0 / test.cs 0 / docs 10 | no |  | 5.1 named | W2-deploy-T |
| `scripts/test-server2-staging-server-compose.ps1` | 303 | test driver/fixture | ps 0 / test.cs 0 / docs 0 | no |  | 5.1 named | W2-deploy-T |
| `scripts/test-set-apphost-maintenance.ps1` | 149 | harness for kept script | ps 0 / test.cs 0 / docs 4 | no |  | 5.1 named | KEEP-T |
| `scripts/test-stage-value-report.ps1` | 188 | test driver/fixture | ps 2 / test.cs 0 / docs 2 | no |  | 5.1 named | W2-ops/land-T |
| `scripts/test-test-selection.ps1` | 459 | test driver/fixture | ps 0 / test.cs 6 / docs 11 | yes (6) |  | 7 only (non-ASCII) | W2-nightly-T |
| `scripts/test-timing-report.ps1` | 24 | test driver/fixture | ps 0 / test.cs 1 / docs 2 | yes (1) |  | 7 only | W2-nightly-T |
| `scripts/test-toolchain-floor.ps1` | 341 | test driver/fixture | ps 0 / test.cs 1 / docs 1 | yes (1) |  | 7 only | W2-nightly-T |
| `scripts/test-watch-qual-eta.ps1` | 140 | test driver/fixture | ps 0 / test.cs 1 / docs 0 | yes (1) |  | 5.1 named | W2-nightly-T |
| `scripts/test-windows-phone-home-runner.ps1` | 581 | test driver/fixture | ps 0 / test.cs 1 / docs 3 | yes (1) |  | 7 only | W2-deploy-T |
| `scripts/test-worktree-residue.ps1` | 42 | test driver/fixture | ps 0 / test.cs 1 / docs 3 | yes (1) |  | 5.1 named | W2-ops/land-T |
| `scripts/typecheck-client.ps1` | 79 | client wrappers | ps 5 / test.cs 0 / docs 5 | no |  | 5.1 named | W2-client |
| `scripts/validate-card0418-ordinary-evidence.ps1` | 95 | one-off card proof | ps 0 / test.cs 0 / docs 1 | no |  | unstated | DELETE? (S0 audit) |
| `scripts/validate-checkpoint-receipt.ps1` | 208 | checkpoint receipt validator | ps 1 / test.cs 10 / docs 123 / server spawns pwsh | yes (10) |  | 7 only | S6 |
| `scripts/verify-agent-tui-profile.ps1` | 305 | cleanup/ops | ps 0 / test.cs 1 / docs 5 | yes (1) |  | 7 only (non-ASCII) | W2-ops |
| `scripts/verify-card0594-linux-launch.ps1` | 257 | one-off card proof | ps 0 / test.cs 0 / docs 3 | no |  | unstated | DELETE? (S0 audit) |
| `scripts/verify-card0604-dind-runner.ps1` | 393 | deploy/server2/postgres | ps 0 / test.cs 1 / docs 6 | yes (1) |  | unstated | W2-deploy |
| `scripts/verify-card0660-codex-image.ps1` | 207 | deploy/server2/postgres | ps 1 / test.cs 3 / docs 11 | yes (3) |  | unstated | W2-deploy |
| `scripts/verify-card0849-caches.ps1` | 294 | deploy/server2/postgres | ps 2 / test.cs 1 / docs 27 | yes (1) |  | unstated | W2-deploy |
| `scripts/verify-checkpoint-orphan-usage.ps1` | 253 | checkpoint tooling | ps 1 / test.cs 1 / docs 5 | yes (1) |  | unstated | W2-checkpoint |
| `scripts/verify-checkpoint-temp-usage.ps1` | 147 | checkpoint tooling | ps 1 / test.cs 3 / docs 7 | yes (3) |  | 7 only | W2-checkpoint |
| `scripts/verify-docker-stack.ps1` | 435 | deploy/server2/postgres | ps 4 / test.cs 7 / docs 17 | yes (7) |  | unstated | W2-deploy |
| `scripts/verify-phone-home-grok.ps1` | 808 | deploy/server2/postgres | ps 0 / test.cs 3 / docs 10 | yes (3) |  | 7 only | W2-deploy |
| `scripts/watchdog-apphost.ps1` | 350 | bootstrap/restart/autostart | ps 4 / test.cs 0 / docs 11 / SchedTask | no |  | 5.1 named | KEEP (sec. 6) |
| `scripts/windmill/invoke-postgres-snapshot-stale.ps1` | 25 | deploy/server2/postgres | ps 0 / test.cs 0 / docs 1 / Windmill(5.1) | no |  | 5.1 named | W2-deploy |
| `scripts/windmill/invoke-postgres-snapshot.ps1` | 228 | deploy/server2/postgres | ps 2 / test.cs 0 / docs 9 / Windmill(5.1) | no |  | 5.1 named | W2-deploy |
| `scripts/windmill/job-status.ps1` | 217 | deploy/server2/postgres | ps 3 / test.cs 0 / docs 4 | no |  | 5.1 named | W2-deploy |
| `scripts/workspace-slots.ps1` | 169 | land/selection/orchestrator support | ps 0 / test.cs 1 / docs 19 | yes (1) |  | 5.1 named | W2-land |
| `scripts/worktree-residue.ps1` | 102 | land/selection/orchestrator support | ps 1 / test.cs 0 / docs 11 | no |  | 5.1 named | W2-land |
| `stop-server.ps1` | 13 | root dev-stack script | ps 0 / test.cs 0 / docs 7 | no |  | unstated | W3-devstack |
| `test-api.ps1` | 7 | root legacy | ps 0 / test.cs 0 / docs 0 | no |  | unstated | DELETE? (S0 audit) |
| `tests/Antiphon.SessionRunner.Tests/Fixtures/CodexVersionChild.ps1` | 99 | fixture: PowerShell is the subject | ps 0 / test.cs 2 / docs 4 | yes (2) |  | unstated | KEEP-subject |
| `tests/Antiphon.SessionRunner.Tests/Fixtures/RunnerRestart/platform.ps1` | 53 | fixture: PowerShell is the subject | ps 1 / test.cs 3 / docs 3 | yes (3) |  | unstated | KEEP-subject |
| `tests/Antiphon.Tests/Agents/Fixtures/claude-effort-dialog.ps1` | 59 | fixture: PowerShell is the subject | ps 1 / test.cs 1 / docs 5 | yes (1) |  | 5.1 named | KEEP-subject |
| `tests/Antiphon.Tests/Agents/Fixtures/claude-effort-trace-contract.ps1` | 48 | fixture: PowerShell is the subject | ps 0 / test.cs 1 / docs 1 | yes (1) |  | unstated | KEEP-subject |
| `tests/Antiphon.Tests/Infrastructure/Fixtures/c504-deploy-am-service-baseline.ps1` | 389 | test driver/fixture | ps 0 / test.cs 2 / docs 1 | yes (2) |  | 5.1 named | W2-deploy-T |
| `tests/Antiphon.Tests/Scripts/Fixtures/c997-import-saved-donor.ps1` | 37 | test driver/fixture | ps 0 / test.cs 1 / docs 1 | yes (1) |  | unstated | W2-deploy-T |
| `tests/Antiphon.Tests/Scripts/Fixtures/directory-link.ps1` | 20 | fixture: PowerShell is the subject | ps 0 / test.cs 2 / docs 3 | yes (2) |  | 7 only | KEEP-subject |
| `tests/Antiphon.Tests/Scripts/Fixtures/rc-full-watchdog-witness.ps1` | 731 | test driver/fixture | ps 0 / test.cs 1 / docs 0 | yes (1) |  | 5.1 named | W2-rc-T |
| `tests/Antiphon.Tests/Scripts/Fixtures/script-harness-process.ps1` | 108 | fixture: PowerShell is the subject | ps 0 / test.cs 1 / docs 4 | yes (1) |  | unstated | KEEP-subject |
| `tests/Antiphon.Tests/TestHelpers/ManifestPublicationHarness.ps1` | 1920 | test driver/fixture | ps 0 / test.cs 1 / docs 0 | yes (1) |  | 5.1 named | W2-rc-T |
| `tests/fixtures/card1336-release-gate-followups.ps1` | 491 | test driver/fixture | ps 1 / test.cs 1 / docs 6 | yes (1) |  | 7 only | W2-rc-T |
| `verify-dev-stack.ps1` | 180 | root dev-stack script | ps 5 / test.cs 1 / docs 25 | yes (1) |  | unstated (non-ASCII) | W3-devstack |

## Appendix B: how the numbers were produced

All commands ran at `5f0804e42` in the server2 runner mirror on 2026-11-19.

```bash
git ls-files '*.ps1' '*.psm1' | wc -l                                   # 427
git ls-files '*.ps1' '*.psm1' | xargs wc -l | tail -1                  # 142248 total
git log --since=2026-10-19 --format=%h origin/master -- <pinned paths> | wc -l       # 158
git log --since=2026-10-19 --format=%h -G'PinnedInputDigest = ' origin/master -- scripts/test-live-lane-run.ps1 | wc -l   # 63
git log --since=2026-10-19 --format=%h origin/master -- tests/test-execution-policy.json | wc -l                        # 91
git log --since=2026-10-19 --format=%h origin/master -- '*.ps1' | wc -l   # 1234 (of 6446)
git grep -h "RunHarnessCaseAsync(" -- tests | grep -o '"[a-z0-9-]*\.ps1"' | sort | uniq -c   # harness call sites
curl -s "$ANTIPHON_API/api/runner-defaults"; curl -s "$ANTIPHON_API/api/session-runners"
# startup samples (bash, date +%s%N around each): pwsh -NoProfile -Command 'exit 0';
#   <cache>/Antiphon.Scripts-*/Antiphon.Scripts --help; pwsh -NoProfile -Command '. nightly-common.ps1; . nightly-coverage.ps1'
```

Two node scripts produced the per-file table. They are not committed; the method is reproduced here.

- **Callers.** For each tracked ps1, the script searched every tracked `*.ps1|*.psm1|*.cs|*.md|*.json|*.sh|*.yml|*.csproj|*.props|*.targets|*.cmd|*.ts|*.mjs`
  (excluding `docs/cards/`, `.antiphon/` and `tests/derived-impact-index.json`) for the basename, with a non-identifier character before it.
- **Host.** Matches of `#requires -Version 7`, of `::HashData(|::ToHexString(|-AsHashtable|ForEach-Object -Parallel|??|IsPathFullyQualified|Text.Json`,
  or of `5.1|Windows PowerShell|ASCII-only`.
- **Disposition.** An ordered path-regex table that mirrors sections 4 and 6.
- **Derived-impact edges.** Taken from `tests/derived-impact-index.json` `edges[].path` that end in `.ps1`.
- **Class costs.** Taken from `scripts/lib/nightly-class-costs.json` `classes` (Windows platform).
