# CARD-0723: a checkpoint runner — one command runs the plan's table, one wait returns one report or one evidence folder

Plan task `7775c8ed` (Plan, Frontier), written on the server2 Linux runner at `af10bde1` on
`feat/card-task-c94fb1eb` (= `origin/master` `8a79fbef` plus the CARD-0723 investigation). Card
revision 3, `InProgress`, importance Normal. TestDesign is folded into this plan (the brief asks for
rounds, red tests and a `### Checkpoints` table); next stage is Code.

Baseline this plan is measured against:
[docs/investigations/2026-09-25-card-0723-agent-time-and-turns.md](../../investigations/2026-09-25-card-0723-agent-time-and-turns.md)
(149 settled tasks, 99.3 h; Linux Claude Code task 28 min / 79 tool calls; Linux Grok 47 min / 184;
a one-command runner removes about 14, 20 and 90 tool calls on the three measured shapes; Linux Unit
lane median 173 s; class checkpoint median 98 s; git-heavy land classes hit the 590 s wrapper on both
machines; Windows Codex reviews are the longest bucket at 11 tasks / 17.6 h).

Related: [CARD-0585 checkpoint manifest](2026-09-20-card-0585-checkpoint-manifest-plan.md) (the
table and `scripts/run-checkpoint.ps1`, landed), [CARD-0589 build fan-out cap](2026-09-25-card-0589-build-fanout-cap-plan.md)
(plan landed; Code on `feat/card-task-75089dd7` at `cf5e42e5`, Review canceled, not on master),
CARD-0615 (quoted `-Expect`, landed), CARD-0671 (`-MsBuildProperty`, `UseAppHost=false` off Windows,
landed), CARD-0677 (`dotnet-ef` in the repo tool manifest, landed).

## Summary

The card's premise holds in the shape the investigation measured, not in the shape the card
describes. Polling is not the median Linux cost (median 0 polls on Linux Claude Code); the ceremony
is launches plus TRX re-reads (16 of 79 calls), plus 14 poll gaps on Grok and 94 on one Windows
`until grep` loop. The wall is the tests themselves, run one row after another. So the tool has to
do three things, in this order of value:

1. **One command, one wait, one report.** `antiphon-checkpoints run --plan <plan.md>` reads the
   plan's `### Checkpoints` table, builds each isolated output once, runs every row into a fresh
   TRX, and prints the CARD-0585 `CHECKPOINT` lines plus a final `--- checkpoint report ---` block
   the delegate pastes unedited. A red run leaves one self-contained evidence folder.
2. **Overlap independent rows** (default two at a time on Linux, one on Windows; process-spawning
   test projects always alone), inside the CARD-0589 build-slot budget when that broker exists.
   This is the only lever that moves the median wall.
3. **Survive the harness.** Claude Code caps one foreground call at 600 s and Grok backgrounds a
   command after 15 s, so a 30-minute run cannot be one blocking call on any harness we have. The
   run is a detached executor; `run` is `start` plus `wait`; `wait` blocks with a heartbeat and
   returns the report, or exit 75 after `--max-wait`, and is simply called again. Four calls
   replace ninety-four.

Two things the card asks for are deliberately narrowed, each with a reason in Decisions: the flaky
rerun is **named-only and always reported** (D-9), because a silent retry is the failure mode
`delegate-basics` forbids; and the repo tool-manifest entry is **deferred** (D-3), because the repo
has no package feed and a manifest entry without a restorable package breaks `dotnet tool restore`
for `dotnet-ef` too.

## Ground truth

Read at `af10bde1` on 2026-09-25 (19:00Z) on the server2 runner: 24 cores, MemAvailable 92.9 GB,
load 12–23, SDK `10.0.401`, runtimes net9.0.20 and net10.0.12, pwsh 7.5.4, `setsid` and `nohup`
present, nuget.org reachable, 190 packages in `~/.nuget/packages` (YamlDotNet, TUnit 1.44 cached).

| Card / brief assumption | What the code, the docs and the host show | Consequence |
|---|---|---|
| "Each checkpoint takes many agent turns and lots of polling … polls for completion in 2-minute waits." | Investigation: Linux Claude Code Code tasks median **0** polls, 10 launches, 6 TRX-parse commands out of 79 calls; Grok median 14 polls (its harness backgrounds after 15 s, `docs/superpowers/plans/2026-09-20-card-0585-checkpoint-manifest-plan.md:370`); Windows outlier `3bf6c1f4` 94 `until grep "EXIT CODE"` polls of ~120 s each. | The saving is launches + reads everywhere, polls only on Grok and on Windows loops. D-1, D-6. |
| "It then parses TRX files and composes the counts into its report by hand." | `scripts/run-checkpoint.ps1:143-233` already parses counters and the `TestMethod@className` roster and prints the pinned `CHECKPOINT` line; delegates still ran a median 6 later `grep CHECKPOINT\|run.trx\|Time Elapsed` commands to re-read it. Nothing on the server parses these lines (`grep "CHECKPOINT" server/ src/` = 0 hits); Review reads them by eye. | The tool prints one final block, keeps the line byte-compatible (D-10) and writes `report.md`/`report.json` so nothing is re-read from a log. |
| "It handles reruns, UseAppHost=false on Linux, isolated OutputPaths, MSBuild node reuse and stalls, and harness timeouts, each case by hand." | The script adds `UseAppHost=false` off Windows (CARD-0671) and guards `bin-<x>/`; raw `dotnet build` lines missed the flag 9 times of 54; the one real stall in a tool result today is `MSB3027` on the FakeClaude apphost (`f7833ef2`). No `Directory.Build.rsp` exists on master; CARD-0589's branch adds `-nodeReuse:false` there and measured 17 orphaned `nodeReuse:true` nodes (4.7 GB) on server2. `Directory.Build.targets` already resets a bloated file-writes ledger (CARD-0222). Harness timeouts are `timeout 590 …` wrappers typed by the agent. | Every build goes through one code path with `-nodeReuse:false`, `UseAppHost=false` off Windows, `--property:OutputPath=bin-<x>/`, `-maxcpucount:<grant or 4>` (D-7). Timeouts belong to the tool (D-8). |
| "A manifest file (YAML or JSON, versioned in the plan or under `.antiphon/`)." | CARD-0585 D-1 rejected a JSON/YAML file beside the plan ("two artifacts drift, nothing consumes the JSON"); the `### Checkpoints` table is what TestDesign writes, Review checks and `InterimVerificationPolicy.HasSelectionRows` can read; `.antiphon/` is gitignored (`.gitignore:55`). The table has 9 columns since CARD-0617 (`Min` = `-MinExecuted`, `EstimatedMinutes` = time); legacy plans before it hold minutes under `Min`. | The plan table stays the source of truth; the tool **imports** it. YAML is the generated/explicit form under `.antiphon/`, never a second committed artifact (D-2). |
| "A `dotnet` tool … packaging as a local tool in the repo manifest." | `.config/dotnet-tools.json` pins only `dotnet-ef 9.0.20` (CARD-0677), restored from nuget.org; `DotnetToolManifestContractTests` guards it. The repo has no NuGet feed, no `nuget.config`, no `dotnet pack` step in any script; `Antiphon.Messaging.FakeGateway` carries `PackAsTool` + `ToolCommandName antiphon-fake-gateway` and is never packed by the repo (downstream repos install it). `tools/Antiphon.Messaging.SchemaGen` is the existing `tools/` console project (net9.0, in the sln). A warm incremental build of that two-project tool on server2 measured **3 s** twice (`Time Elapsed 00:00:02.59`, `00:00:02.86`). | Ship a real tool project with `PackAsTool` metadata, prove packaging with `dotnet pack` + `dotnet tool install --add-source`, run it as `dotnet run --project tools/Antiphon.Checkpoints --` (3 s warm); the manifest entry waits for a feed (D-3). |
| "build slots (CARD-0589)" | Plan only on master. The Code branch `feat/card-task-75089dd7` (13 commits to `cf5e42e5`) adds `POST/GET/DELETE /build-slots` on the runner, `scripts/lib/build-slot.ps1`, `Directory.Build.rsp`, slot lines on the `CHECKPOINT` line (`slot=<state> waited=<n>s`) and bundle text; its Review `7ebf8aa2` was canceled. `GET http://127.0.0.1:8080/build-slots` on this runner answers **404** today. D-6 there: unreachable broker → unleased after 60 s at `-maxcpucount:4`; timeout → exit 4. | The tool carries its own slot client with a start-of-run probe (a 404 marks slots unavailable once, not 60 s per driver) and the same states/lines, so it works before and after 0589 lands (D-7). |
| "retries known-flaky tests once" | `server/Bundles/delegate-basics.md`: "never … add a retry to make red go green — that is how a real defect stays hidden". `docs/orchestration-loop.md:425`: re-running a failure **in isolation** to establish flaky-vs-real is allowed at Test tier. CARD-0585 rule 1: a red row is fixed and rerun as the same row, reruns counted. No Microsoft.Testing.Extensions.Retry package is cached; TUnit 1.44's engine strings include `retry` (attribute), `fail-fast`, `maximum-parallel-tests`; MTP 2.2.2 includes `timeout`, `minimum-expected-tests`, `exit-on-process-exit`, `ignore-exit-code`. | Reruns are the tool's own, only for names the run was told are flaky, once, method-scoped, and always printed as `RERUN` lines with `reruns=1` on the row (D-9). |
| "compares failures against a baseline commit (optional comparison against origin/master)" | No mechanism exists. `delegate-basics`: "Stash your changes, or check out the base commit, and re-run the failure there"; the environment note warns the stash stack is shared across worktrees. Land tests build real repos with `git worktree add` (`LandingGitFixture.cs:47`). | A detached `git worktree add` of the baseline under the run folder, one build there, the failed methods only, then `worktree remove --force` (D-11). |
| "one compact machine-readable report plus Markdown … in exactly the format stage reports need" | The pinned line is `CHECKPOINT CP-n commit=<40 hex> build=(ok\|reused) filter=… executed=N passed=N failed=N skipped=N trx=…` (`RunCheckpointScriptTests.C585_LineFormat`, regex in the 0585 plan V-13, anchored on `trx=.+$`), plus `reruns=k`; 0589's branch appends `slot=<state> waited=<n>s`. The stage report ends with `--- next stage ---` and, for Review, the `--- review evidence ---` block; `stage-code` asks per CP-n for commit, filter, counts, TRX path, reruns and for the full SHA, branch and worktree. | `report.md` = the lines plus a header with SHA/branch/worktree/host and an `unlisted:` line; `report.json` mirrors it (D-10). |
| "a single evidence folder with the failing tests' messages and stacks, TRX, console logs, host load, git state and diffs, plus the exact rerun commands" | Today: `.antiphon/checkpoints/<CP>-<stamp>/run.trx` per row and whatever the console showed. The MTP TRX carries `<Output><ErrorInfo><Message>` (fixture `scripts/fixtures/c585-failures.trx:8-10`) and, for the real reporter, `<StackTrace>` and `<StdOut>`. Host memory is read from `/proc/meminfo` on Linux (0589's `HostMemoryProbe`); `/proc/loadavg` is readable here. | `EvidenceFolder` writes `failures.md`, `rerun.txt`, `host.txt`, `git.txt`, build and console logs (D-12). |
| "cleans up its own `bin-*` output" | `delegate-basics`: delete the `bin-<name>` directories ("roughly a dozen, one per project") before finishing; `scripts/cleanup-build-junk.ps1` is the desktop's weekly Windmill sweep of `bin-*` older than 60 min (Windows-only, robocopy); `Directory.Build.props` excludes `bin-*/**` from globs. | Green run: delete this run's `bin-<x>` names under the repo root; red run: keep them (the rerun commands need them) and print the `clean` command (D-13). |
| "a single blocking command with a heartbeat line, so agents make one call instead of polling" | Claude Code's Bash tool caps a call at 600 s (agents wrap rows in `timeout 590`; the investigation's three ~600 s land runs are that cap, not test durations). Grok auto-backgrounds after 15 s. Codex limits are not in any transcript. `delegate-basics`: "RUN EVERY COMMAND IN THE FOREGROUND AND WAIT … never background a run and end your turn". Session lifetime custody exists on both hosts (Windows kill-on-close Job Object, Linux cgroup custody), so a detached child never outlives the session. | `start` launches a detached executor (shadow-copied, own session via `setsid` on Linux, `DETACHED_PROCESS \| CREATE_NEW_PROCESS_GROUP` and never job breakaway on Windows); `wait` blocks with a heartbeat and exit 75 on `--max-wait`; the docs say never end a turn while `wait` reports running (D-4, D-5). |
| "`run-checkpoint.ps1` becomes a thin wrapper around the tool." | The script is 252 lines with 15 pinned offline harness cases (`RunCheckpointScriptTests`, `scripts/test-run-checkpoint.ps1`, `-DotnetShim`); CARD-0589's unlanded branch rewrites its build/run section (+79 lines). | The tool ships a `row` verb with the script's exact semantics now; the script becomes the wrapper in Round 2 after 0589 lands or is abandoned, keeping the harness green through a `--dotnet <shim>` seam (D-14). |
| "Stage bundles tell agents to call the tool once and wait on the final result." | `stage-code` is 2492 of the 2500-char cap, `stage-review` 2483, `stage-test-design` 2490 (`each_stage_bundle_is_ascii_and_under_the_size_cap`); the pinned phrases are listed in Slice S7. `delegate-basics` (6393 chars) has no cap; its "RUN THE FULL SUITE ONCE" bullet says the suite "does not fit one 10-minute foreground window — chunk it". CARD-0589's branch also edits `delegate-basics`, `stage-code` and `stage-review`. | Exact replacement texts measured in this plan: `stage-code` 2444, `stage-review` 2487, both ASCII with every pin intact (S7). `delegate-basics` gets its bullet in Round 2 with 0589's (S9). |
| "Making the land classes quicker or splitting them." | Measured: land-class rows median 161 s Linux / 219 s Windows, three runs at the 590 s wrapper (`4aa8cc21` CP-7 `Worktree` filter, `3e24ff4a` `TaskWorktreeRetirement`, `88485f49` `AgentTaskLandDispatch`). Why: every land test builds a `LandingSafetyHarness` = `LandingGitFixture.InitializeAsync` (10 git commands: two inits, add, commit, two pushes, worktree add, clone) plus an isolated Postgres schema clone plus a DI host; the classes carry `[ParallelLimiter<ProcessSpawnLimit>]` (`Limit => 1`, 199 classes), so a 56-method class (`AgentTaskLandApprovalRecoveryTests`) runs strictly serially; 288 classes are `[NotInParallel]`. The Slow allowlist already names the retained real-git land matrices. | This card: per-class duration lines from the TRX (`SLOW CLASS`), import warns when a row's estimate exceeds the row timeout and proposes the class split, row timeouts scale with `EstimatedMinutes`, and land rows overlap other rows (D-8, D-15). Fixture and limiter changes are a follow-up card with the numbers this tool records. |
| Running two `Antiphon.Tests` hosts at once is safe. | `TestDbFixtureLifecycle` starts one `postgres:16-alpine` Testcontainer per test host and clones isolated schemas per test; `delegate-basics` already permits concurrent `Antiphon.Tests` shards for Mutation with the caveat that the spawn limiter is per process and `Antiphon.Agents.Pty.Tests`/FakeClaude must not be co-scheduled. | Default two rows in flight on Linux, one on Windows; pty-host and agents-pty rows always run alone; `serial: true` per row (D-8). |
| Where the tool's own tests live. | `tests/test-execution-policy.json` (`policyHash`) and the nightly enumerate seven suites; a new test project is a policy change. `Antiphon.Tests` builds in 1–2 min warm on Linux and already hosts script harness tests under `tests/Antiphon.Tests/Scripts/` with `DelegateScriptRunner.RepoRoot`. | Tests go in `tests/Antiphon.Tests/Checkpoints/` with a `ProjectReference` to the tool (D-16). |

## Decisions

Stated defaults for what the card and brief left open. All are covered by the caller's standing
authority ("Should we create a cli test runner helper to run check point etc"); nothing here is
irreversible, nothing kills a session, and no production path changes.

### D-1. The unit of value is "one run per committed slice group, one wait, one report", not "one command per task"

CARD-0585 rule 1 stands: each row runs once, after its `After` slice is committed. A Code round
therefore calls the tool once per slice group (`--after S1-S3`), and the last run covers the rest.
`report --merge` assembles the round's final block from every run of the same manifest (latest
result per CP wins, earlier attempts counted as `reruns=`). Rejected: forcing a single whole-table
run at the end (doubles test time on a round that already ran early rows) and per-row invocation
(today's shape).

### D-2. The plan table is the manifest; YAML is imported from it, never a second committed artifact

`run --plan <path>[@<sha>]` imports `### Checkpoints` (the CARD-0617 nine-column schema; a
legacy eight-column table is refused with the CARD-0617 rename instruction) and runs it. `import`
writes the same manifest as `.antiphon/checkpoints.yaml` for a delegate that needs to edit it
(add `knownFlaky`, mark `serial`, pin timeouts), and `run <manifest.yaml>` runs that. Both paths
produce one in-memory `CheckpointManifest`; the run folder always records `manifest.resolved.yaml`.
YAML over JSON: filters with `|` and `*` and a multi-line comment naming the plan row read better,
and JSON is valid YAML so either can be written. YamlDotNet 16.3.0 is already a server dependency
and cached on the runner. Rejected: a committed manifest beside the plan (0585 D-1, drift) and a
TOML/INI format nobody in the repo uses.

Import rules: `Build` `<project> -> <bin-x/>` becomes a build with id `bin-x`; `CP-n` reuse maps to
that row's build and is refused when the two `After` values differ; `Filter` is unescaped (`\|` →
`|`); a `Filter` that does not start with `/` is a `command` row (exit 0 expected, `Min` `n/a`);
`Min` becomes `minExecuted` (integer or `n/a`); roster tokens are the class names in the filter
(each `(Name*)` operand or the class segment; a `[Category=…]` lane has none); `EstimatedMinutes`
sets `estimatedMinutes` and the row timeout (D-8); `Expect` text is recorded verbatim and `>= N
executed` there also sets `minExecuted` when `Min` is absent.

### D-3. Ship a real tool project; run it with `dotnet run --project`; the tool-manifest entry waits for a feed

`tools/Antiphon.Checkpoints` (net9.0 console, `PackAsTool`, `ToolCommandName antiphon-checkpoints`,
`PackageId Antiphon.Checkpoints`, `IsPackable`, in `Antiphon.sln` under `tools`, **no** project
references, YamlDotNet only). The command every doc names is
`dotnet run --project tools/Antiphon.Checkpoints -- <verb> …` (3 s warm on server2, measured on
the sibling tool). CP-7 proves `dotnet pack` + `dotnet tool install --tool-path … --add-source …`
+ `antiphon-checkpoints --version`, so the manifest entry is a one-line follow-up once a feed
exists. Rejected: adding the entry now — `dotnet tool restore` fails as a whole when one package is
unresolvable, breaking the CARD-0677 `dotnet-ef` path on every host; a committed `.nupkg` feed
(binary churn per change); a global `dotnet tool install -g` (machine state, forbidden by 0677's
reasoning). Rejected: a PowerShell reimplementation — the scheduler, process-tree timeouts, the
detached executor and JSON/YAML handling are what a script does badly, and Windows PowerShell 5.1
compatibility rules would bind it.

### D-4. `run` = `start` + `wait`; the executor is a detached, shadow-copied process

`start` validates, creates `<resultsRoot>/<run-id>/`, copies the tool's own output directory to
`<run>/tool/`, and launches `dotnet <run>/tool/Antiphon.Checkpoints.dll execute --run <folder>` in
its own session (Linux: `setsid`; Windows: `CreateProcessW` with
`DETACHED_PROCESS | CREATE_NEW_PROCESS_GROUP`, **never** `CREATE_BREAKAWAY_FROM_JOB`, so the
session's kill-on-close Job Object still owns it). The executor writes `state.json` every 10 s and
`executor.log`; it never writes to the console. `wait <run-id|--latest>` tails the state, prints
one `HEARTBEAT` line per minute, and on completion prints the report and exits with the run's exit
code. Why shadow-copy: `wait` is itself `dotnet run --project …`, whose incremental build would
copy over the running executor's DLL (an `MSB3027` on Windows). Why detached: a Claude Code call
ending at 600 s kills the waiter's process group; a run inside it would die with it. Rejected:
one blocking process only (dies at the harness cap); a runner-side executor (the right eventual
home, listed under Follow-ups, but it puts a server change on the critical path of a tool that must
also work from an operator shell and the nightly).

### D-5. `wait --max-wait` returns exit 75 and a progress block; the docs make ending a turn mid-run a rule violation

Exit 75 (`EX_TEMPFAIL`) means "still running; call `wait` again". A Claude Code delegate runs
`timeout 590 dotnet run … -- run --plan …` then `… wait --latest --max-wait 570s` until the exit
is not 75. Grok's harness backgrounds `wait` after 15 s and the agent polls its background output
as it does today; for Grok the tool removes launches and TRX reads (about 9 of its 23 ceremony
calls), not its polls. `stop <run-id>` kills the executor tree; the total timeout is the backstop;
`status` lists runs. The `delegate-basics` rule stays true: the run is supervised, and the docs
(S7) say "never end your turn while `wait` reports running; `stop` it if you must".

### D-6. Rows overlap by default on Linux; process-spawning projects and `serial` rows run alone

`parallel.maxRows` defaults to 2 on Linux and 1 on Windows (the desktop's builds measured 2–3 min
and its test runs are slower; server2 has 24 cores and 90 GB free). Rows from
`tests/Antiphon.Agents.Pty.Tests` and `tests/Antiphon.PtyHost.Tests`, and any row with
`serial: true`, run with nothing else in flight. Rows sharing one build output may overlap: the
hosts read the same DLLs, results directories differ, each host owns its Postgres container.
Every driver (build or test host) takes one build-slot lease when a broker exists, so 0589's
budget bounds the effective width. Expected effect, to be measured by CP-8 and R3: a
ten-row Code round at the 98 s median drops from ~16 min of sequential rows to ~9 min at width 2.
Rejected: a per-class cost model for scheduling (no data yet; the tool starts recording it).

### D-7. Every build is one code path with safe defaults, and the slot client probes once

`dotnet build <project> --property:OutputPath=<bin-x/> [--property:UseAppHost=false off Windows
unless the manifest names UseAppHost] <manifest properties> -nodeReuse:false -maxcpucount:<N>
--nologo`, then `dotnet run --project <project> --no-build --property:OutputPath=<bin-x/> <same
properties> -- --treenode-filter <filter> --report-trx --report-trx-filename run.trx
--results-directory <run>/rows/<CP>/`. The output-path guard is the script's (`^bin-[A-Za-z0-9._-]+/$`).
`-nodeReuse:false` is passed on the command line so the tool is correct before and after 0589's
`Directory.Build.rsp`. `N` is the slot grant's `maxCpuCount`, else 4 (0589 D-6's unleased default),
else `build.maxCpuCount` when the manifest sets it (an operator shell on an idle host).

Slot client: endpoint `ANTIPHON_BUILD_SLOTS_URL`, else `http://localhost:17204/build-slots` on
Windows and `http://127.0.0.1:8080/build-slots` on Linux. At run start one `GET`: 200 → `enabled`;
404 → `unavailable` (no leases, no per-driver grace, `-maxcpucount:4`); refused/timeout → retry for
60 s once, then `unleased`. Per driver when enabled: `POST` with the executor pid, its start time,
label `<CP>@<project>` or `build:<bin-x>`, `ANTIPHON_SESSION_ID`/`ANTIPHON_TASK_ID`; 409
`build_slot_busy`/`build_slot_memory_floor` → wait, `BUILD SLOT waiting …` once a minute; grant →
`BUILD SLOT granted lease=… waited=…s maxcpucount=…`; `DELETE` in `finally`, `BUILD SLOT released`;
timeout after `slot.waitMinutes` (45) → the row is `slot-timeout` (exit 4) and rows depending on
that build are `skipped`. States on the line: `granted|unlimited|unleased|unavailable|skipped`.
Rejected: referencing `Antiphon.SessionRunner.Contracts` for the DTOs (they are not on master; the
tool keeps zero project references).

### D-8. Timeouts are the tool's, enforced by process-tree kill, scaled from the table

Row timeout default `max(15, 3 × EstimatedMinutes)` minutes (`timeoutMinutes` per row, `--row-timeout`
for all); total default `max(30, 2 × ΣEstimatedMinutes + 10)` (`--total-timeout`). A timed-out driver
is killed with `Process.Kill(entireProcessTree: true)` (the repo's pattern, e.g.
`GitWorkspaceService.cs:1111`); the row is `timeout` (exit 5) with `executed=n/a` and the console
log tail in evidence; on total timeout, running rows are killed and queued rows `skipped`. MTP's own
`--timeout` is not passed (its TRX behaviour on expiry is unverified); it is listed under Follow-ups
as a belt to evaluate. Rejected: inheriting the agent's `timeout 590` (the cap that produced the
three 600 s land runs).

### D-9. Reruns are named-only, once, method-scoped, and always printed

`rerun.knownFlaky` (manifest) or `--known-flaky A.B,C.D` (CLI). After a red row, the failed names
that appear in that list are rerun once, one invocation per class
(`/*/*/Class/(m1*)|(m2*)`; a bare `(m1)|(m2)` or a multi-class OR matches nothing on TUnit 1.44); each is printed as
`RERUN <Class.Method> first=Failed second=Passed|Failed`; the row line carries `reruns=1`; the
first TRX stays `run.trx` and each class rerun is `rerun-N.trx`; the row is green only when every
rerun passed and nothing else failed. A failed
name not in the list is never rerun. There is no standing repo list: known-flaky names are state
that belongs in the brief or plan for that dispatch (bundle README: "anything that will be wrong
tomorrow … belongs in the brief"). Rejected: rerun every failure once by default (the retry
`delegate-basics` forbids, and it would have hidden the live 64 KB truncation defect that
`orchestration-loop.md:425` cites); TUnit `[Retry]` attributes (a source change per test, invisible
in the report).

### D-10. The `CHECKPOINT` line is byte-compatible with the script and already carries 0589's suffix

`CHECKPOINT CP-n commit=<sha> build=<ok|reused|failed> filter=<filter> executed=N passed=N failed=N
skipped=N trx=<absolute path> slot=<state> waited=<n>s[ reruns=k]`. The CARD-0585 V-13 regex
(anchored on `trx=.+$`) still matches; the slot suffix is what 0589's branch appends. A `command`
row prints `build=n/a filter=<command> executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a
exit=<code> slot=… waited=…`. A timed-out TUnit row prints `executed=n/a … trx=n/a timeout=<m>m`.
`FAILED <Class.Method>` lines follow each red row; `EXECUTED` roster lines are capped at 300 on
the console and complete in `rows/<CP>/roster.txt`. The final block:

```
--- checkpoint report ---
run: 20260925-190102-ab12   manifest: .antiphon/checkpoints/20260925-190102-ab12/manifest.resolved.yaml
commit: <40 hex>  branch: feat/card-task-xxxx  worktree: /work/worktrees/task-xxxx  host: server2 linux cores=24
CHECKPOINT CP-1 … slot=granted waited=0s
CHECKPOINT CP-2 … reruns=1
RERUN Antiphon.Tests.Application.X.Y first=Failed second=Passed
FAILED Antiphon.Tests.Application.X.Z (CP-2) INTRODUCED: Expected 1 but was 2 -> rows/CP-2/failures.md
SLOW CLASS Antiphon.Tests.Application.AgentTaskLandApprovalRecoveryTests 212s tests=56 (CP-4)
unlisted: none (the tool ran no other build or test command)
wall: 14m03s  sequential-equivalent: 22m10s  builds: 1  rows: 5 green 1 red 0 skipped
outputs: kept bin-c723a/ (red run) -> dotnet run --project tools/Antiphon.Checkpoints -- clean --run 20260925-190102-ab12
evidence: /work/worktrees/task-xxxx/.antiphon/checkpoints/20260925-190102-ab12/report.md
verdict: RED exit=1
```

`report.json` (schemaVersion 1, camelCase) mirrors it: `runId, commit, branch, worktree, host{os,
cores, memAvailableMb, loadAvg}, startedAt, endedAt, wallSeconds, sequentialEquivalentSeconds,
exitCode, verdict, builds[], rows[] {id, group, filter|command, build, state, exitCode, executed,
passed, failed, skipped, reruns, trx, seconds, failures[] {name, message, baseline}, reruns[],
slowClasses[]}, unlisted: [], evidence`.

Exit codes: 0 green; 1 failed tests; 2 invalid manifest, build failed, no or malformed TRX; 3
`minExecuted` or roster miss; 4 slot timeout; 5 row/total timeout; 6 executor crashed or died; 75
still running (`wait --max-wait`). Run exit = highest-precedence row state in the order
2, 6, 4, 5, 1, 3, 0.

### D-11. Baseline comparison is a detached worktree, built once, running only the failed methods

`--baseline <ref>` (or `baseline.ref`): for each red TUnit row, once per build id,
`git fetch origin <branch>` (60 s, tolerated failure), `git worktree add --detach <run>/baseline
<sha>`, the same build into the same `bin-<x>/` name inside that worktree, then the failed methods
only; classify each `INHERITED` (red at baseline), `INTRODUCED` (green at baseline) or `NEW`
(not executed at baseline); `git worktree remove --force` in `finally`. Rejected: stash-based
checkout (shared stash stack; mutates the source under a running executor).

### D-12. The evidence folder is the run folder; a red run makes it self-contained

```
.antiphon/checkpoints/<run-id>/
  manifest.resolved.yaml  state.json  executor.log  report.md  report.json
  host.txt      # os, cores, MemAvailable, /proc/loadavg or Windows equivalents, dotnet --version, GET /build-slots
  git.txt       # HEAD, branch, status --porcelain, log -5, diff --stat <merge-base origin/master>..HEAD
  builds/<bin-x>/build.log            # full MSBuild output; console shows only the summary and errors
  rows/<CP>/run.trx  rerun-1.trx  console.log  roster.txt  failures.md  rerun.txt
  baseline/<bin-x>/build.log  rows/<CP>/run.trx  classification.txt
  tool/                               # the shadow copy; wait deletes it after the executor exits
```

`failures.md`: per failed test the class-qualified name, outcome, duration, `ErrorInfo/Message`,
`StackTrace`, `StdOut` (each capped at 200 lines), the baseline classification, and the two exact
rerun commands (the tool's `row` form and the raw `dotnet run … --treenode-filter` form). A green
run writes the folder without `failures.md`. `wait` deletes `tool/` once `phase=done` and the executor pid is gone.

### D-13. Cleanup deletes only what this run created, and only on a green run by default

On exit 0: every directory named exactly `bin-<x>` for this run's build ids, found under the repo
root (recursing, pruning `.git`, `node_modules`, `workspace`, `bin`, `obj`, other `bin-*`) and the
baseline worktree. `<run>/tool/` stays while the executor is alive; `wait` deletes it once
`phase=done` and that pid is gone, and the next `start` sweeps finished runs. On a red run the
test outputs stay (the rerun commands need them)
and the report prints the `clean --run <id>` command; `--clean-on-red` overrides. `clean
[--run <id> | --manifest <path>] [--older-than 7d]` is the verb; it never touches `bin/`, `obj/`,
`workspace/` or a `bin-*` name the manifest does not own (guarded by tests and PC-4).

### D-14. `run-checkpoint.ps1` keeps its contract now and becomes the wrapper in Round 2

The tool's `row` verb takes the script's parameters (`--name --project --output-path --filter
[--no-build] [--min-executed] [--expect] [--msbuild-property] [--results-root] [--dotnet <shim>]`)
and prints the identical lines and exit codes, including the 0589 `slot=`/`waited=` suffix. In
Round 2, after CARD-0589's branch lands or is abandoned, the script's body becomes a call to `row`
that forwards `-DotnetShim` as `--dotnet`, so the 15 harness cases keep passing. Rejected: doing it
in Round 1 (two live rewrites of one file; the harness would need the shim seam first).

### D-15. Land classes: measure and split in this card; speed up the fixtures in a follow-up

The tool records per-class seconds from the TRX (`SLOW CLASS` lines when a class exceeds 60 s),
`import` warns when `3 × EstimatedMinutes` exceeds the row timeout ceiling of 45 min or when a row
names more than four land classes, and the Cost block below carries the split rule: a land row is at
most ~120 `[Test]` methods (about 4 min at the measured 2–5 s per real-git test). Row timeouts scale
with the estimate (D-8) so a 12-minute land row no longer dies at a hand-typed 590 s. The fixture
lever — a per-assembly template repository cloned with `--shared` instead of ten git commands per
test, and a `GitSpawnLimit` (Limit 4) for git-only classes distinct from the pty
`ProcessSpawnLimit` — is a test-infrastructure change with its own risks and is filed as a
follow-up with this tool's numbers as the measurement.

### D-16. Tests live in `Antiphon.Tests` under `Checkpoints/`, driven through a fake process driver

`tests/Antiphon.Tests/Checkpoints/*` with a `ProjectReference` to `tools/Antiphon.Checkpoints`.
`IDriver` (start a process, stream output, kill tree, exit code) is the seam; `FakeDriver` answers
per command pattern with scripted output, exit codes, TRX fixture files copied into the results
directory, and optional blocking-until-cancelled, so scheduler, timeout, rerun, baseline, slot and
report behaviour are unit-testable without `dotnet`. One real-process class (`DetachedLauncherTests`,
`[ParallelLimiter<ProcessSpawnLimit>]`) proves the executor survives its starter on Linux; the
Windows half is verified by Review on the desktop (CP-9). The real dogfood is CP-8: the tool runs
this plan's own table.

## Design

### Verbs

| Verb | What it does | Exit |
|---|---|---|
| `run (--plan <md>[@<sha>] [--section "### Checkpoints"] \| <manifest.yaml>) [--rows CP-1,CP-3] [--after S1-S3] [--baseline <ref>] [--known-flaky A.B,…] [--row-timeout 15m] [--total-timeout 90m] [--parallel N] [--serial] [--results-root .antiphon/checkpoints] [--slots auto\|off] [--keep-outputs] [--clean-on-red] [--max-wait 570s] [--heartbeat 60s] [--json]` | `start` then `wait`. | run exit, or 75 |
| `start …` (same selection/options) | Validate, create the run folder, launch the executor, print `RUN <id> started rows=N executor=<pid>`. | 0, 2 |
| `wait [<id> \| --latest] [--max-wait <d>] [--heartbeat <d>] [--json]` | Block; heartbeat; final report; or progress block and 75. | run exit, 6, 75 |
| `status [--results-root …]` | One line per run: id, state, elapsed, rows green/red/queued. | 0 |
| `stop <id>` | Kill the executor tree; mark `stopped`; keep evidence. | 0 |
| `report [<id> \| --latest \| --merge] [--json]` | Re-render; `--merge` = latest result per CP across runs of the same manifest hash, earlier attempts as reruns. | 0 |
| `import --plan <md> [--section …] [--out .antiphon/checkpoints.yaml]` | Table → YAML, with the D-15 warnings. | 0, 2 |
| `row --name … --project … --output-path … --filter … [options]` | One row, `run-checkpoint.ps1` semantics and lines. | 0–4 |
| `clean [--run <id> \| --manifest <path>] [--older-than 7d] [--dry-run]` | D-13. | 0 |
| `execute --run <folder>` | Internal: the executor. | run exit |
| `--version` | Informational version and git SHA stamped by `Directory.Build.props`. | 0 |

### Manifest (YAML, schemaVersion 1)

```yaml
schemaVersion: 1
plan: docs/superpowers/plans/2026-09-25-card-0723-checkpoint-runner-plan.md@<sha>   # provenance
resultsRoot: .antiphon/checkpoints
build:
  properties: { }                # merged with UseAppHost=false off Windows unless UseAppHost is named
  maxCpuCount: 0                 # 0 = slot grant, else 4
  slots: auto                    # auto | off
  slotWaitMinutes: 45
parallel: { maxRows: 2 }         # default 2 Linux / 1 Windows
timeouts: { rowMinutes: 15, totalMinutes: 90 }
rerun: { knownFlaky: [] }
baseline: { ref: null }
builds:
  - { id: bin-c723a, project: tests/Antiphon.Tests, outputPath: bin-c723a/ }
checkpoints:
  - id: CP-1
    after: [S1, S2]
    build: bin-c723a
    group: tool-core
    filter: "/*/Antiphon.Tests.Checkpoints/(CheckpointManifestTests*)|(TrxReportTests*)/*"
    expect: [CheckpointManifestTests, TrxReportTests]     # roster tokens
    expectText: "all listed, 0 failed"
    minExecuted: 20
    estimatedMinutes: 3
    timeoutMinutes: 15
    serial: false
  - id: CP-7
    after: [all]
    command: "dotnet pack tools/Antiphon.Checkpoints -o .antiphon/c723-pack && …"
    expectText: "exit 0, prints the version"
    estimatedMinutes: 2
```

Validation (exit 2 before any process starts): output paths match the guard; build ids unique and
referenced; CP ids unique; `after` parses (`S1`, `S1-S3`, `S1,S3`, `all`); reuse rows share `after`
with their build's first row; a row has exactly one of `filter`/`command`; `minExecuted` is an
integer or absent for command rows; the results root is under the worktree.

### Scheduler

Builds are nodes; rows depend on their build; rows are ordered by CP number within their `after`
group. A worker pool of `parallel.maxRows` takes the next row whose build is `ok`; an exclusive row
(pty projects, `serial`) waits for the pool to drain and holds it. A failed build marks its rows
`build-failed` (exit 2) and continues with other builds. Each driver takes a slot lease (D-7).
The executor writes `state.json` (`runId, phase, executorPid, startedAt, totalTimeoutAt, builds[]
{id, state, seconds, slot}, rows[] {id, state, startedAt, seconds, lastOutputAt, line}`) every 10 s
and at every transition; `wait` derives the heartbeat from it:

```
HEARTBEAT run=20260925-190102-ab12 elapsed=12m34s | CP-1 green 98s | CP-2 running 3m12s last-output 4s ago | CP-3 queued | bin-c723b building 0m41s
```

### TRX parsing

Port of the script's rules: join `Results/UnitTestResult@testId` to
`TestDefinitions/UnitTest/TestMethod@className` + `@name` (never the display name); skip
`NotExecuted`; `Failed|Error|Timeout|Aborted` are failures; counters from `ResultSummary/Counters`
with roster-derived fallbacks; `skipped = total − executed` floored at 0. Additionally read
`Output/ErrorInfo/Message`, `Output/ErrorInfo/StackTrace`, `Output/StdOut` and `@duration` per
result, and aggregate seconds per class for `SLOW CLASS`. A missing or malformed TRX is exit 2,
never green (script rule 5).

## Slices

Round 1 (one Code dispatch) is S1–S7; Round 2 is S8–S9; Round 3 is the measurement.

### S1. Tool project, manifest model, import

Files: `tools/Antiphon.Checkpoints/Antiphon.Checkpoints.csproj` (net9.0, `OutputType Exe`,
`PackAsTool`, `ToolCommandName antiphon-checkpoints`, `PackageId Antiphon.Checkpoints`,
`IsPackable true`, `RollForward Major`, YamlDotNet 16.3.0), `Program.cs` (verb dispatch, `--version`),
`Manifest/CheckpointManifest.cs`, `Manifest/ManifestLoader.cs`, `Manifest/ManifestValidator.cs`,
`Manifest/PlanTableImporter.cs`, `Manifest/AfterSelector.cs`; `Antiphon.sln` entry under `tools`.
Tests: `tests/Antiphon.Tests/Checkpoints/CheckpointManifestTests.cs`, `CheckpointImportTests.cs`;
fixtures `tests/Antiphon.Tests/Checkpoints/Fixtures/plan-table-0688.md` (the CARD-0688 table
verbatim: escaped pipes, `CP-1` reuse, two command rows), `plan-table-legacy-min.md` (eight
columns, refused), `plan-table-illustrative.md` (the doc's example).

### S2. Driver, build step, row runner, TRX, line, exit codes, slot client

Files: `Execution/IDriver.cs`, `Execution/ProcessDriver.cs` (streams stdout/stderr to a log,
`Kill(entireProcessTree: true)`, honours `--dotnet <shim>`: a `.ps1` shim runs through `pwsh
-NoProfile -NonInteractive -File`), `Execution/BuildStep.cs`, `Execution/RowRunner.cs`,
`Execution/ExitCodes.cs`, `Trx/TrxReport.cs`, `Report/CheckpointLine.cs`,
`Slots/BuildSlotClient.cs` (`HttpClient` with an injectable handler), `Commands/RowCommand.cs`.
Tests: `TrxReportTests.cs` (the three `scripts/fixtures/c585-*.trx` plus a new
`c723-failure-with-stack.trx` fixture carrying `StackTrace` and `StdOut`), `CheckpointLineTests.cs`
(the V-13 regex plus suffixes), `ExitCodeTests.cs`, `RowRunnerTests.cs` (`FakeDriver`: green, red,
zero executed, roster miss, build failed, no TRX, malformed TRX, `--no-build`, property forwarding,
Linux/Windows `UseAppHost` default via an injected platform), `BuildSlotClientTests.cs` (fake
handler: 200 grant, 200 unlimited, 409 busy then grant, 409 memory floor, 404 at probe →
unavailable with zero later calls, refused → unleased after the grace, timeout → 4, release on
dispose).

### S3. Scheduler, timeouts, state file

Files: `Execution/RunScheduler.cs`, `Execution/RowTimeout.cs`, `State/RunState.cs`,
`State/RunStateStore.cs` (atomic write: temp file + rename). Tests: `RunSchedulerTests.cs` (one
build per id; a row never starts before its build; width respected with a `FakeDriver` that
blocks; pty rows exclusive; `serial` exclusive; failed build → dependent rows `build-failed`, others
continue), `TimeoutTests.cs` (row kill at the deadline, state `timeout`; total timeout kills
running and skips queued; deadline derivation from `EstimatedMinutes`), `RunStateStoreTests.cs`
(partial write never observed).

### S4. `start`, `wait`, `execute`, `status`, `stop`, detached launcher

Files: `Commands/StartCommand.cs`, `Commands/WaitCommand.cs`, `Commands/ExecuteCommand.cs`,
`Commands/StatusCommand.cs`, `Commands/StopCommand.cs`, `Execution/DetachedLauncher.cs`
(shadow copy; `setsid` on Linux, `CreateProcessW` flags on Windows), `Execution/RunCommand.cs`
(`start` + `wait`). Tests: `WaitCommandTests.cs` (state-driven: heartbeat cadence, exit 75 at
max-wait with a progress block, final report and exit code when done, exit 6 when the executor pid
is gone without a `done` phase — with a fake liveness probe), `DetachedLauncherTests.cs`
(Linux, real processes, `[ParallelLimiter<ProcessSpawnLimit>]`: start a sleeper executor, kill the
starter's process group, the executor is still alive and finishes; `RequireLinux()` skip otherwise),
`ShadowCopyTests.cs` (copies the DLL set, never the source tree, into `<run>/tool/`).

### S5. Report, evidence, cleanup

Files: `Report/ReportWriter.cs` (markdown + JSON), `Report/ReportMerger.cs`,
`Evidence/EvidenceFolder.cs`, `Evidence/HostSnapshot.cs`, `Evidence/GitSnapshot.cs`,
`Cleanup/OutputCleanup.cs`, `Commands/ReportCommand.cs`, `Commands/CleanCommand.cs`. Tests:
`ReportWriterTests.cs` (block layout; `unlisted: none`; `sequential-equivalent`; JSON schema
fields), `ReportMergerTests.cs` (latest per CP wins, earlier attempts as reruns), `EvidenceFolderTests.cs`
(red: `failures.md` with message/stack/stdout/baseline/commands, `rerun.txt`, `host.txt`, `git.txt`;
green: no `failures.md`, `tool/` removed), `OutputCleanupTests.cs` (deletes only the run's names in
a temp tree; never `bin`, `obj`, `workspace`, foreign `bin-*`; red keeps; `--clean-on-red`;
`--older-than`).

### S6. Rerun policy and baseline

Files: `Execution/RerunPolicy.cs`, `Baseline/BaselineComparer.cs`, `Baseline/GitWorktree.cs`.
Tests: `RerunPolicyTests.cs` (only listed names; once; OR-combined method filter; `RERUN` lines;
`reruns=1`; verdict rules; an unlisted failure never reruns), `BaselineComparerTests.cs` (`FakeDriver`
answers by working directory: INHERITED/INTRODUCED/NEW; `worktree add --detach` and `remove
--force` issued exactly once per build id; removal on failure).

### S7. Docs, bundles, contract tests

Files: `docs/testing-and-build.md` (new `### Checkpoint runner tool (CARD-0723)` after the
manifest section: verbs, manifest, run/wait and the 600 s cap, exit codes, rerun and baseline
rules, evidence layout, cleanup, the land-row split rule; rule 3 of the manifest section amended to
"`scripts/run-checkpoint.ps1` or the checkpoint tool prints exactly this line"); `AGENTS.md` (one
bullet under "Tests and builds"); `docs/orchestration-loop.md` §3 bullet (the delegate runs the
table through the tool; `-ExpectAbout` unchanged); `.claude/skills/antiphon-delegate/SKILL.md`
Code and Review recipes (the `run`/`wait` commands); `server/Bundles/stage-code.md` and
`server/Bundles/stage-review.md` (exact swaps below); `tests/Antiphon.Tests/Application/CheckpointManifestDocumentationTests.cs`
(new pins), `tests/Antiphon.Tests/Application/InstructionBundleTests.cs`
(`stage_bundle_invariants_are_pinned_by_substring` gains `code.ShouldContain("checkpoint tool")`
and `review.ShouldContain("checkpoint-tool run")`), `tests/Antiphon.Tests/Infrastructure/CheckpointToolProjectContractTests.cs`
(csproj carries `PackAsTool`, `ToolCommandName antiphon-checkpoints`, `PackageId`, no
`ProjectReference`; `Antiphon.sln` lists it; `tests/Antiphon.Tests.csproj` references it).

`stage-code.md`, the CHECKPOINTS paragraph, exact replacement (file measures **2444** chars LF,
ASCII, every pin below intact):

> CHECKPOINTS: the plan's ### Checkpoints table is the closed list of builds and test runs. Run it through the checkpoint tool (docs/testing-and-build.md, Checkpoint runner tool): one run per committed slice group, wait for its report, paste its CP-n lines unedited; inspect fresh TRX for each intended class/method and nonzero counts. Fix a red CP-n, then rerun that CP-n. Any other build/test command is unlisted: report it with a reason. A new test that cannot go red against the production line it guards (self-compare, constant, no outcome assertion) is a stub, not done.

Pins kept: `### Checkpoints`, `closed list`, `unlisted`, `is a stub, not done`, `Run each V-n and
R-n`, `next: review when implementation and ordinary V/R are complete, even with zero PCs`,
`inspect fresh TRX for each intended class/method and nonzero counts`, `Unit plus the named affected
integration classes`, `Report every PC-n/variant pending for Mutation`, `commissions SourceLanding
Mutation`, `original Code task ID`, `pending for Mutation`.

`stage-review.md`, three swaps (file measures **2487** chars LF, ASCII): SCOPE's first two
sentences become "Re-run the claimed scoped ordinary checks (Unit plus named affected integration
classes) before land as one checkpoint-tool run of the plan's table (docs/testing-and-build.md).
Executed PCs are not a prerequisite; Mutation runs them after land."; the trailing "See
docs/testing-and-build.md Fast lane." sentence is dropped; INVARIANTS becomes "Read-only. Do not
fix anything. Read the diff against the plan and its verification; re-run claimed ordinary tests;
judge ordinary evidence and PC evidence read-only (PCs stay pending). Reject missing regression
tests or ordinary evidence. Carry the original Code landing owner through handoffs. Defects as
Where/Failure/Why/Fix." Pins kept: `Read-only`, `Do not fix anything`, `CP-n lines`, `cannot go
red`, `Executed PCs are not a prerequisite`, `PCs stay pending`, `Required manual work stays pending
and nightly green never satisfies manual or PC checks.`, `PC evidence read-only`, `missing row`,
`zero count`, `unlisted build/test run without a reason`. Code re-measures both files with
`tr -d '\r' | wc -m` after applying and must not fix an overrun by editing a pinned sentence. If
CARD-0589's branch lands first, apply these swaps on top of its wording (its stage-review SCOPE
already contains the slot-gate clause; keep it).

### S8 (Round 2). `scripts/run-checkpoint.ps1` becomes the wrapper

After CARD-0589 lands or is abandoned: the script keeps its parameters and forwards them to
`dotnet run --project tools/Antiphon.Checkpoints -- row …`, passing `-DotnetShim` as `--dotnet`
and `C585_STAMP`/`C671_PLATFORM` through the environment; `scripts/test-run-checkpoint.ps1` and
the 15 `RunCheckpointScriptTests` cases are unchanged and must stay green. The build-slot seams
(`C589_SLOT_*`) map to the tool's fake-handler equivalents.

### S9 (Round 2). `delegate-basics.md`

One new bullet ("RUN THE CHECKPOINT TABLE WITH ONE COMMAND … `wait` until the exit is not 75;
never end your turn while it reports running; `stop` it if you must") and the "RUN THE FULL SUITE
ONCE" bullet amended so chunking is the tool's job. Written after 0589's bullet so the two compose;
`InstructionBundleTests` pins for `delegate-basics` (`FOREGROUND`, `DO NOT SUB-DELEGATE`, …) stay.

### S10 (Round 3). Measurement

Re-run the investigation's query (`GET /api/agent-tasks?boardId=…&since=<first card under the
tool>` plus per-session transcripts) for the first four Code and four Review tasks that ran under
the new bundles, and record tool calls, launches, polls, TRX reads and wall against the baseline
table in `docs/investigations/2026-09-25-card-0723-agent-time-and-turns.md` (a new "After" section).
The card's acceptance ("drop measurably against today's baseline") is judged there.

## Code rounds

| Round | Slices | Exit |
|---|---|---|
| R1 | S1–S7 | CP-1..CP-8 green; CP-8 is the tool running CP-1..CP-6 itself; bundles measured under the cap; `next: review` |
| R2 | S8, S9 | after CARD-0589's fate is settled; CP-R2-1 (script harness) and CP-R2-2 (bundle pins) green |
| R3 | S10 | after two Code and two Review tasks have run under the tool; the investigation's "After" section written |

## Verification design

TestDesign folded (brief). Bodies read: `scripts/run-checkpoint.ps1` (whole), `scripts/test-run-checkpoint.ps1:1-90`,
`tests/Antiphon.Tests/Scripts/RunCheckpointScriptTests.cs` (whole), `ScriptHarness.cs` (whole),
`scripts/fixtures/c585-green.trx`, `c585-failures.trx`, `InstructionBundleTests.cs:578-640`,
`VerificationRoundInstructionTests.cs:78-96`, `ScopedVerificationInstructionTests.cs:19-151`,
`CheckpointManifestDocumentationTests.cs` (whole), `DotnetToolManifestContractTests.cs` (whole),
`DirectoryBuildRspTests.cs` (0589 branch), `SlowTestTripwire.cs`, `TestDbFixtureLifecycle.cs:20-60`,
`LandingGitFixture.cs:35-49`, `LandingSafetyHarness.cs:36-39`, `SettledRemovalHarness.cs:29-34`,
`ProcessSpawnLimit.cs`, `AgentTaskLandService.cs:1329-1360`, `DelegateScriptRunner.cs:68-79`,
0589 branch `run-checkpoint.ps1` diff, `scripts/lib/build-slot.ps1:1-110`, `BuildSlotContracts.cs`.
"Red" states what fails at `af10bde1`: every V-n red because the tool does not exist (compile red
for the tool tests until S1 gives them a project; the doc/bundle pins red because the phrases are
absent).

### Inspection

- `RunCheckpointScriptTests` + `ScriptHarness` | boundary: the harness's exact PASS inventory and `C487 HARNESS EXIT CODE: 0` trailer → R-1 (unchanged in R1; S8 must keep it).
- `CheckpointLine` regex from 0585 V-13 `^CHECKPOINT CP-1 commit=[0-9a-f]{40} build=(ok|reused) filter=.+ executed=\d+ passed=\d+ failed=\d+ skipped=\d+ trx=.+$` | boundary: the `slot=`/`waited=`/`reruns=` suffix sits inside `trx=.+$` → V-4 asserts both the regex and the exact suffix order.
- TRX fixtures | boundaries: no namespace vs namespaced; `NotExecuted`; `Error|Timeout|Aborted`; `Counters` absent → V-3 covers each; new fixture with `StackTrace`/`StdOut`.
- `TestDbFixtureLifecycle` one container per host | boundary for D-6 → CP-8 runs two rows concurrently for real.
- `InstructionBundleTests` cap 2500 / ASCII / no kind names | boundaries: the two swaps measured 2444 and 2487 → R-2.
- `DelegateScriptRunner.RepoRoot` walks up to `Antiphon.sln` | the tool needs its own root finder (walk up from the cwd to `Antiphon.sln`; `--repo-root` override) → V-1.
- Missing setup recorded: no C# TRX fixture with `StackTrace` exists (S2 adds one); no existing detached-launch helper without pty dependencies (S4 adds `DetachedLauncher`); no fake HTTP handler helper in `Antiphon.Tests` for the slot client (S2 adds a 30-line `ScriptedHttpHandler`).

### Delivery inventory

One asynchronous path: executor → waiter. Producer: `ExecuteCommand` writing `state.json`
(atomic temp+rename) and `report.md`/`report.json`; destination: the run folder; persistence
boundary: the filesystem; recovery: `wait` re-reads on every tick and any later `wait`/`report`
call re-reads the final files; observable receipt: `wait` prints the report and exits with the
run's code; durable identity: the run id. Crash cases: executor dies without `phase=done` → `wait`
detects the pid gone (liveness probe) and exits 6 with `executor.log` tail (V-8); a partial
`state.json` is never observed (V-9); the starter dies → the executor continues (V-10). No
substitute claims: the receipt is the waiter's exit code, not the `RUN … started` line.

### Proves it works now

- V-1: manifest load + validation | tool unit | `CheckpointManifestTests` (`parses_yaml_and_defaults`, `rejects_backslash_or_trailing_space_output_path`, `rejects_duplicate_or_unknown_build_ids`, `rejects_reuse_across_different_after`, `after_selector_parses_ranges_and_all`, `results_root_must_be_under_worktree`) | expected exit 2 with a message naming the field; green for the sample manifest.
- V-2: plan table import | tool unit | `CheckpointImportTests` (`imports_the_card_0688_table`, `unescapes_pipes_in_filters`, `maps_cp_reuse_to_the_same_build`, `command_rows_become_command_checkpoints`, `derives_roster_tokens_from_the_filter`, `refuses_the_legacy_eight_column_table`, `warns_when_estimate_exceeds_row_timeout`) | 13 rows, 3 builds, `CP-11`/`CP-13` commands, `Min` `n/a` → no `minExecuted`.
- V-3: TRX parse | tool unit | `TrxReportTests` (`joins_results_to_class_qualified_names`, `counts_failed_error_timeout_aborted`, `skips_not_executed`, `falls_back_when_counters_missing`, `reads_message_stack_and_stdout`, `aggregates_seconds_per_class`, `malformed_trx_is_exit_2`) | matches the script's counts on `c585-*.trx`.
- V-4: line format | tool unit | `CheckpointLineTests.tunit_line_matches_the_pinned_regex_with_slot_suffix`, `command_line_carries_exit`, `timeout_line_carries_timeout` | regex true; suffix order `slot= waited= reruns=`.
- V-5: exit precedence | tool unit | `ExitCodeTests.run_exit_is_the_highest_precedence_row_state` (`[Arguments]` over the seven states) | 2 > 6 > 4 > 5 > 1 > 3 > 0.
- V-6: scheduler | tool unit | `RunSchedulerTests` (`builds_each_output_once`, `rows_wait_for_their_build`, `width_two_runs_two_rows_at_once`, `pty_project_rows_run_alone`, `serial_rows_run_alone`, `failed_build_fails_only_its_rows`) | `FakeDriver` call log.
- V-7: timeouts | tool unit | `TimeoutTests` (`row_deadline_kills_the_driver_tree_and_marks_timeout`, `total_deadline_kills_running_and_skips_queued`, `row_deadline_is_max_15_or_3x_estimate`) | kill invoked once; states as named.
- V-8: wait | tool unit | `WaitCommandTests` (`prints_a_heartbeat_per_interval`, `returns_75_with_progress_at_max_wait`, `prints_report_and_run_exit_when_done`, `returns_6_when_executor_died`) | scripted state files and liveness.
- V-9: state store | tool unit | `RunStateStoreTests.readers_never_see_a_partial_file` | temp+rename observed.
- V-10: detached executor | real process, Linux | `DetachedLauncherTests.executor_survives_its_starter` (`[ParallelLimiter<ProcessSpawnLimit>]`, `RequireLinux()`) | starter group killed, executor completes and writes `done`.
- V-11: rerun policy | tool unit | `RerunPolicyTests` (`reruns_only_known_flaky_names_once`, `unlisted_failure_never_reruns`, `rerun_filter_is_method_scoped`, `row_green_only_when_rerun_passed_and_nothing_else_failed`, `rerun_lines_and_reruns_suffix`) | one extra driver call per class, filter `/*/*/Class/(m1*)|(m2*)`.
- V-12: baseline | tool unit | `BaselineComparerTests` (`classifies_inherited_introduced_new`, `adds_and_removes_the_detached_worktree_once_per_build`, `removes_worktree_when_build_fails`) | worktree commands in order.
- V-13: evidence | tool unit | `EvidenceFolderTests` (`red_run_writes_failures_with_message_stack_stdout_and_commands`, `green_run_writes_report_only_and_removes_tool_copy`, `host_and_git_snapshots_present`) | files listed in D-12.
- V-14: report | tool unit | `ReportWriterTests` (`block_layout_matches_d10`, `unlisted_is_none`, `json_carries_every_row_field`), `ReportMergerTests.latest_per_cp_wins_and_earlier_attempts_count_as_reruns`.
- V-15: cleanup | tool unit | `OutputCleanupTests` (`deletes_only_this_runs_bin_names`, `never_deletes_bin_obj_workspace_or_foreign_bin_star`, `red_keeps_unless_clean_on_red`, `older_than_removes_run_folders`) | temp tree.
- V-16: slot client | tool unit | `BuildSlotClientTests` (`probe_404_marks_unavailable_and_makes_no_lease_calls`, `refused_becomes_unleased_after_grace`, `busy_waits_then_grants`, `memory_floor_waits`, `timeout_is_exit_4`, `release_on_dispose`) | scripted handler.
- V-17: row verb parity | tool unit | `RowRunnerTests` (green, red, zero executed → 3, roster miss → 3, build failed → 2, no TRX → 2, malformed → 2, `--no-build` → `build=reused`, properties on build and run, Linux default `UseAppHost=false`, Windows unchanged, explicit `UseAppHost` wins) | mirrors `C585_*`.
- V-18: docs and bundle pins | doc test | `CheckpointManifestDocumentationTests.the_checkpoint_tool_is_pinned_in_every_copy` (doc: `### Checkpoint runner tool (CARD-0723)`, `tools/Antiphon.Checkpoints`, `wait`, `exit 75`; AGENTS.md: `CARD-0723`; SKILL.md and orchestration-loop: `Antiphon.Checkpoints`; stage-code: `checkpoint tool`; stage-review: `checkpoint-tool run`) and `InstructionBundleTests` additions | present after whitespace collapse.
- V-19: tool project contract | contract test | `CheckpointToolProjectContractTests` (`csproj_packs_as_tool_named_antiphon_checkpoints`, `csproj_has_no_project_references`, `solution_lists_the_tool`, `tests_project_references_the_tool`) | XML/`.sln` text.
- V-20: packaging | non-TUnit | CP-7 | `dotnet pack` produces `Antiphon.Checkpoints.1.0.0.nupkg`; `dotnet tool install --tool-path` from `--add-source` succeeds; `antiphon-checkpoints --version` prints the version and SHA.
- V-21: dogfood | non-TUnit | CP-8 | one `run --plan` over CP-1..CP-6 returns one report, `unlisted: none`, exit 0, two rows observed concurrently in `state.json` history, `bin-c723a/` deleted after.

### Guards the regression

- R-1: `run-checkpoint.ps1` contract | `RunCheckpointScriptTests` (15) + `NightlyVerificationContractTests.C544_DailyValidity` | unchanged PASS inventories.
- R-2: bundle cap and pins | `InstructionBundleTests.each_stage_bundle_is_ascii_and_under_the_size_cap`, `stage_bundle_invariants_are_pinned_by_substring`, `VerificationRoundInstructionTests`, `ScopedVerificationInstructionTests` | `stage-code` 2444 ≤ 2500, `stage-review` 2487 ≤ 2500, every `ShouldContain` as written.
- R-3: existing doc pins | `CheckpointManifestDocumentationTests` (four existing methods), `StandingPipelinePolicyDocumentationTests`, `CommitOnSettleDocumentationTests` | unchanged.
- R-4: tool manifest untouched in R1 | `DotnetToolManifestContractTests` | unchanged.
- R-5: solution builds with the new project | CP-1's build of `tests/Antiphon.Tests` (which now references the tool) | exit 0.

### Guard inventory

- G-1: output-path guard (CARD-0448 hazard) | PC-1
- G-2: zero executed is never green (Fast lane) | PC-2
- G-3: roster miss is exit 3 (D-12 of 0585) | PC-3
- G-4: cleanup never deletes `bin/`, `obj/`, `workspace/` or foreign `bin-*` | PC-4
- G-5: row timeout kills the driver tree | PC-5
- G-6: pty-project rows never overlap another row | PC-6
- G-7: `wait` never returns 0 while the run is live | PC-7
- G-8: a missing TRX is exit 2, not green | PC-8
- G-9: unlisted failures never rerun | PC-9
- G-10: exit precedence puts invalid (2) above failed (1) | PC-10
- G-11: a 404 probe never becomes a lease; a slot timeout is exit 4, never an unleased run | PC-11
- G-12: baseline classification | PC-12
- G-13: stage-bundle cap | PC-13
- G-14: executor never job-breaks-away / survives its starter | PC-14
- G-15: the doc pins reach every copy | PC-15

guards=15, mapped=15, missing=0, duplicate PC mappings=0.

### Positive controls

Mutation runs these after land, method-scoped, red then restore then green. Filters:
`--treenode-filter "/*/Antiphon.Tests.Checkpoints/<Class>/<method>"` unless stated.

- PC-1: in `ManifestValidator.cs` change the guard regex to `^bin-[A-Za-z0-9._-]+[/\\]$`; expect `CheckpointManifestTests.rejects_backslash_or_trailing_space_output_path` red at the exit-2 assertion.
- PC-2: in `RowRunner.cs` remove the `executed < minExecuted` branch; expect `RowRunnerTests.zero_executed_is_exit_3` red.
- PC-3: in `RowRunner.cs` skip the roster loop; expect `RowRunnerTests.roster_miss_is_exit_3` red.
- PC-4: in `OutputCleanup.cs` drop `bin` from the pruned names; expect `OutputCleanupTests.never_deletes_bin_obj_workspace_or_foreign_bin_star` red.
- PC-5: in `RowTimeout.cs` replace `Kill(entireProcessTree: true)` with a no-op; expect `TimeoutTests.row_deadline_kills_the_driver_tree_and_marks_timeout` red at the kill-count assertion.
- PC-6: in `RunScheduler.cs` remove the pty-project exclusivity check; expect `RunSchedulerTests.pty_project_rows_run_alone` red.
- PC-7: in `WaitCommand.cs` return 0 instead of 75 at max-wait; expect `WaitCommandTests.returns_75_with_progress_at_max_wait` red.
- PC-8: in `RowRunner.cs` treat a missing TRX as `executed=0 passed=0` green; expect `RowRunnerTests.no_trx_is_exit_2` red.
- PC-9: in `RerunPolicy.cs` rerun every failed name; expect `RerunPolicyTests.unlisted_failure_never_reruns` red at the driver-call-count assertion.
- PC-10: in `ExitCodes.cs` swap the precedence of 1 and 2; expect `ExitCodeTests.run_exit_is_the_highest_precedence_row_state` red for the `(2,1)` argument.
- PC-11: in `BuildSlotClient.cs` treat the probe's 404 as `granted`; expect `BuildSlotClientTests.probe_404_marks_unavailable_and_makes_no_lease_calls` red.
- PC-12: in `BaselineComparer.cs` swap INHERITED and INTRODUCED; expect `BaselineComparerTests.classifies_inherited_introduced_new` red.
- PC-13: append 60 characters of prose to `server/Bundles/stage-code.md`; expect `InstructionBundleTests.each_stage_bundle_is_ascii_and_under_the_size_cap` red for `stage-code` (filter `/*/Antiphon.Tests.Application/InstructionBundleTests/each_stage_bundle_is_ascii_and_under_the_size_cap`).
- PC-14: in `DetachedLauncher.cs` launch the executor as an ordinary child (no `setsid`); expect `DetachedLauncherTests.executor_survives_its_starter` red on Linux (the executor dies with the group).
- PC-15: in `docs/testing-and-build.md` rename the heading to `### Checkpoint runner (CARD-0723)`; expect `CheckpointManifestDocumentationTests.the_checkpoint_tool_is_pinned_in_every_copy` red at the heading assertion.

### Out of scope

- A server-side or runner-side executor and a `delegate.ps1` flag (Follow-ups).
- Changing `ProcessSpawnLimit`, `LandingGitFixture` or any land test (D-15 follow-up).
- The Grok harness's 15-second auto-background (a harness question; the tool's `wait` behaviour is documented for it).
- MTP `--timeout` / `--minimum-expected-tests` as belts (Follow-ups, to evaluate).
- The nightly (`scripts/lib/nightly-tests-impl.ps1`) keeps its own runner; a later card may let it consume manifests.
- Interim `verificationSelection` and `LandVerifyFilter` are untouched.

### Checkpoints

Test project `tests/Antiphon.Tests` unless stated; one build per round into `bin-c723a/`; every
other row `--no-build`; on server2 `run-checkpoint.ps1` adds `UseAppHost=false` itself. `Min` counts
the `[Test]` methods named in the V-n rows above (argument-expanded rows per argument), a floor.
CP-1..CP-6 run through `scripts/run-checkpoint.ps1` (the tool does not exist until S1–S4 are green);
CP-8 then runs CP-1..CP-6 again through the tool — those second runs are the dogfood, not
unlisted runs. Pipes inside `Filter` cells are escaped for the table; the command line uses `|`.
CP-2 Min is `16 linux / 15 windows` because `DetachedLauncherTests` skips off Linux.
CP-7 runs the packed tool with `dotnet exec` on the installed assembly: cmd treats an unquoted
`.antiphon/...` token as a command plus switches, and that dll entry point is the same on Windows and Linux.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1-S2 | `tests/Antiphon.Tests -> bin-c723a/` | tool-core | `/*/Antiphon.Tests.Checkpoints/(CheckpointManifestTests*)\|(CheckpointImportTests*)\|(TrxReportTests*)\|(CheckpointLineTests*)\|(ExitCodeTests*)\|(RowRunnerTests*)\|(BuildSlotClientTests*)\|(ProcessDriverTests*)/*` | V-1..V-5, V-16, V-17, R-5 | all listed, 0 failed | 45 | 6 |
| CP-2 | S3-S4 | CP-1 | tool-engine | `/*/Antiphon.Tests.Checkpoints/(RunSchedulerTests*)\|(TimeoutTests*)\|(RunStateStoreTests*)\|(WaitCommandTests*)\|(ShadowCopyTests*)\|(DetachedLauncherTests*)\|(CheckpointAppTests*)/*` | V-6..V-10 | all listed, 0 failed (DetachedLauncherTests executes on Linux, skips elsewhere) | 16 linux / 15 windows | 4 |
| CP-3 | S5-S6 | CP-1 | tool-evidence | `/*/Antiphon.Tests.Checkpoints/(RerunPolicyTests*)\|(BaselineComparerTests*)\|(EvidenceFolderTests*)\|(ReportWriterTests*)\|(ReportMergerTests*)\|(OutputCleanupTests*)\|(RerunFilterHostTests*)/*` | V-11..V-15 | all listed, 0 failed | 19 | 3 |
| CP-4 | S7 | CP-1 | doc-and-bundle-pins | `/*/Antiphon.Tests.Application/(CheckpointManifestDocumentationTests*)\|(InstructionBundleTests*)\|(VerificationRoundInstructionTests*)\|(ScopedVerificationInstructionTests*)\|(StandingPipelinePolicyDocumentationTests*)\|(CommitOnSettleDocumentationTests*)/*` | V-18, R-2, R-3 | all listed, 0 failed | 60 | 3 |
| CP-5 | S7 | CP-1 | contracts-and-script | `/*/*/(CheckpointToolProjectContractTests*)\|(DotnetToolManifestContractTests*)\|(RunCheckpointScriptTests*)/*\|/*/*/NightlyVerificationContractTests/(C544_DailyValidity*)` | V-19, R-1, R-4 | all listed, 0 failed | 22 | 5 |
| CP-6 | S7 | CP-1 | unit-lane | `/*/*/*/*[Category=Unit]` (`-MinExecuted 1000`) | Final whole Unit lane | >= 1000 executed, 0 failed | 1000 | 4 |
| CP-7 | S7 | n/a | pack-and-install | `dotnet pack tools/Antiphon.Checkpoints -o .antiphon/c723-pack --nologo && dotnet tool install Antiphon.Checkpoints --tool-path .antiphon/c723-pack/tp --add-source .antiphon/c723-pack && dotnet exec .antiphon/c723-pack/tp/.store/antiphon.checkpoints/1.0.0/antiphon.checkpoints/1.0.0/tools/net9.0/any/Antiphon.Checkpoints.dll --version` | V-20 | exit 0; a version line naming the SHA | n/a | 2 |
| CP-8 | S7 | n/a (the tool builds `bin-c723a/` itself) | dogfood | `timeout 590 dotnet run --project tools/Antiphon.Checkpoints -- run --plan docs/superpowers/plans/2026-09-25-card-0723-checkpoint-runner-plan.md --rows CP-1,CP-2,CP-3,CP-4,CP-5,CP-6 --baseline origin/master --results-root .antiphon/c723-dogfood --max-wait 570s`, then `… wait --latest --max-wait 570s` until the exit is not 75 | V-21, D-1, D-4..D-8, D-13 | one `--- checkpoint report ---` block with six green CP lines, `unlisted: none`, exit 0, `state.json` history showing two rows in flight at once, `bin-c723a/` absent afterwards; paste the block in the Code report | n/a | 12 |

Round 2 rows (dispatched separately): CP-R2-1 `tests/Antiphon.Tests -> bin-c723b/`
`/*/*/(RunCheckpointScriptTests*)\|(BuildSlotScriptTests*)/*` all listed 0 failed, Min 15 (+ 0589's
rows if landed), 5 min; CP-R2-2 CP-R2-1 `/*/Antiphon.Tests.Application/(InstructionBundleTests*)\|(CheckpointManifestDocumentationTests*)/*`, Min 40, 3 min.

If TUnit 1.44 refuses the `Antiphon.Tests.Checkpoints` namespace segment with a class OR (CARD-0403
verified `/*/*/(A*)|(B*)/*` only), use `/*/*/(A*)|(B*)/*` for CP-1..CP-3 and report which form ran.
CP-8's `--rows` keeps CP-7 out because the packed tool path is stateful. Delete every `bin-c723a`
directory before settling (CP-8's green run does it; CP-1..CP-6's script runs leave the same
names, so a final `clean --manifest` or manual delete covers a red CP-8).

### Cost

Estimated, not measured, except the 3 s tool build and the investigation's medians.

| Ordinary Code floor (R1) | Minutes |
|---|---:|
| First build of `tests/Antiphon.Tests` into `bin-c723a/` (setup; includes the new project reference) | 4 |
| CP-1 tool-core (incremental build + 7 unit classes) | 6 |
| CP-2 tool-engine (incl. one real-process class) | 4 |
| CP-3 tool-evidence | 3 |
| CP-4 doc-and-bundle-pins | 3 |
| CP-5 contracts-and-script (15 pwsh spawns) | 5 |
| CP-6 unit-lane (Linux median 173 s) | 4 |
| CP-7 pack-and-install | 2 |
| CP-8 dogfood (tool rebuild ~2 min + CP-1..CP-6 at width 2 ≈ 10 min) | 12 |
| **Ordinary V/R after setup** | **39** |
| **Code total including setup** | **43** |

Authoring: tool ~2,500 lines C# (manifest/import 300, driver/row/TRX/line 500, scheduler/timeouts/
state 400, start/wait/execute/detach 450, report/evidence/cleanup 450, rerun/baseline 300, misc
100) ≈ 7 h; tests ~1,400 lines ≈ 3 h; docs, bundles with re-measure, contract tests ≈ 1 h.
`-ExpectAbout 700` for the R1 Code dispatch (43 + ~660). R2: 8 minutes of checkpoints plus about
1 h. R3: about 1 h of API queries and writing.

Separate post-land Mutation floor: 15 PCs × (red 0.5 + restore/rebuild 0.7 + green 0.5) = 25.5,
plus 5 snapshot setup = **30.5 minutes**. Total verification floor = 4 setup + 39 ordinary + 30.5
Mutation = **73.5 minutes**, estimated.

Savings: booked as zero on this card's own R1 run (it bootstraps through the script). Expected on
later rounds, from the investigation's medians: about 14 tool calls (18 %) on a Linux Claude Code
Code task and about 90 on a Windows poll-loop task from the launch/read/poll ceremony; the
sequential-to-overlapped wall on a ten-row round from ~16 to ~9 minutes at width 2 (D-6). R3
measures both.

Handoff audit: bodies read; guards=15, mapped=15, missing=0, duplicate PC maps=0; every PC is a
compiling or text defect with an exact method and assertion; numeric Cost above. Next stage: Code.

## Risks

- **The 600 s harness cap is inferred, not documented in this repo.** The three ~600 s land runs and
  the `timeout 590` wrappers are the evidence. If a harness kills detached children too, `wait`
  reports exit 6 with the executor log and the run is re-issued with `--rows`; CP-8 and R3 show
  which harnesses need what.
- **Windows detachment** (`DETACHED_PROCESS | CREATE_NEW_PROCESS_GROUP` under the session Job
  Object) is verified by Review on the desktop (CP-2's `DetachedLauncherTests` skips there);
  Windows kills the tree at session close either way.
- **Concurrent test hosts raise peak memory** (each `Antiphon.Tests` host ~740 MB plus its Postgres,
  measured in the 0589 plan). Width 2 on a 90 GB host is safe; the 0589 broker's memory floor is
  the guard once it lands; `--serial` is the operator override.
- **CARD-0589's branch** touches `run-checkpoint.ps1`, three bundles and the doc section next to
  this card's. R1 avoids the script; the bundle swaps compose with either wording; S8/S9 wait.
- **`dotnet run --project` while an executor runs** is safe because of the shadow copy; a
  delegate that instead runs the DLL from `bin/` directly can still collide on Windows — the docs
  name only the `dotnet run` form.
- **Bundle cap headroom** after S7 is 56 chars on `stage-code` and 13 on `stage-review`; the next
  standing rule must pay by cutting.

## Follow-ups (not in this card)

- Manifest entry in `.config/dotnet-tools.json` once a package feed exists (GitHub Packages or a
  release asset); `DotnetToolManifestContractTests` then pins it like `dotnet-ef`.
- Runner-side execution: `POST /checkpoints` on the session runner running a manifest in the
  session's custody, so every harness gets one call and the stage report is assembled
  server-side; `report.json` is designed to be that payload.
- Land fixtures: per-assembly template repository (`git clone --shared`) in `LandingGitFixture`
  and a `GitSpawnLimit` distinct from the pty `ProcessSpawnLimit`; measured with this tool's
  `SLOW CLASS` lines.
- MTP `--timeout` and `--minimum-expected-tests` as belts under the tool's own enforcement.
- The nightly consuming manifests; the Grok harness background threshold.

## Not done, noted

- No tool code was written in this Plan (brief). No `dotnet` test ran; the only builds were two
  3 s warm builds of `tools/Antiphon.Messaging.SchemaGen` to price the `dotnet run` form, with the
  `bin-c723p` outputs deleted afterwards.
- CARD-0723's card text says "checkpoint takes many agent turns"; the investigation's tool-call
  and poll counts are what this plan optimises for, and the card's acceptance is measured in R3.
