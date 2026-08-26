# CARD-0118 — Codex test residue: what it is, what causes it, how to clean it safely

**Status:** investigation + design (task `0100d53d`, 2026-08-26). No process, session, remote
agent, SQLite row or rollout file was touched. Every destructive experiment below ran against a
*copy* of `CODEX_HOME` under this task's scratchpad, never the real `C:\Users\lndco\.codex`.

**Codex versions measured:** CLI `codex-cli 0.147.0` (npm, `%APPDATA%\npm`), Codex Desktop
`26.810.7004.0` (MSIX `OpenAI.Codex_26.810.7004.0_x64`, bundling CLI `0.148.0-alpha.9`).

---

## 0. One-paragraph verdict

The residue is **local, not remote**. Headed Codex tests (`ANTIPHON_CODEX_HEADED_TESTS=1`) and the
A-tier / B-runner stub-proxy canaries (`ANTIPHON_REAL_CLI_STUB_TESTS=1`) launch `codex.exe` with
**no `CODEX_HOME` override**, so every run writes into the user's real Codex state: a rollout JSONL
under `~/.codex/sessions/`, a row in `state_5.sqlite` `threads`, and rows in
`thread_history_1.sqlite`. The tests delete their temp *cwd* afterwards but nothing deletes the
Codex-side record, so each run leaves a thread whose project directory no longer exists. The manual
2026-08-20 cleanup deleted rollout *files* by hand, which is exactly what produced the `codex doctor`
warning the prior task found: **25 `threads` rows now point at files that no longer exist** (the DB
is Codex's index; nothing reconciles it against the filesystem). Codex Cloud has **zero** tasks
(`codex cloud list` → "No tasks found"; the prior task's `http.list_tasks … items=0` agrees), the
`thread_spawn_edges` table (sub-agents) is empty, and the only `remote_control_enrollments` row is
the desktop app's own enrolment of this machine (`DESKTOP-KTLKPIF`) — a real user artefact. There
are no leaked test *processes*. The sanctioned cleanup primitive is `codex delete --force <uuid>`,
measured on a copy to remove the `threads` row, the `thread_history` rows and the rollout file,
and to succeed on a stale row whose file is already gone. **The safe discriminator is a positive
allow-list on the state row's `cwd` (the test helpers' fixed `%TEMP%` prefixes) plus
`model_provider = 'stub'`; 13 of the 25 stale rows are ad-hoc probes run by agent sessions, not by
any test helper, and cannot be claimed by rule — they are listed by id in §3 for the user to
confirm.** The durable fix is to stop writing test residue into the user's home at all (§5.1), with
a report-first sweep script on the existing Windmill pattern as the one-time cleanup and backstop
(§5.3). A TUnit teardown that deletes from the live user DB is **not** recommended (§5.2).

---

## 1. "Remote foundational agents" — what the phrase does and does not map to

The user's description (2026-08-21) was "5 remote foundational agents left over". Measured:

| Candidate surface | Finding |
|---|---|
| Codex's own vocabulary | The desktop app bundle (`app.asar`, 26.810.7004.0) contains **no** occurrence of `foundational` in any UI string (the only hits are a crochet demo caption, Apple `Foundation.framework` and a TLD list). "Remote agent" as a phrase: **0** hits. The term is the user's paraphrase, not a Codex label. |
| Codex Cloud tasks (`codex cloud list`, chatgpt.com/codex) | **"No tasks found."** Nothing Antiphon runs invokes `codex cloud …`; the tests run the TUI / `codex exec` locally with `--dangerously-bypass-approvals-and-sandbox`. |
| Sub-agents (`thread_spawn_edges`) | Empty. No test spawns agents. |
| Remote control (`remote_control_enrollments`) | One row: `Codex Desktop` client, server `srv_e_6a83…`, environment `env_e_6a83…`, name `DESKTOP-KTLKPIF`, `remote_control_enabled=1`. This is the desktop app enrolling *this machine* as a remotely controllable environment for the ChatGPT web/mobile Codex UI (`codex-mobile-has-connected-device: true` in `.codex-global-state.json`). **Real user artefact — never touch.** |
| Threads shown through that remote-control view | The desktop app-server (pid 13816, `codex.exe … app-server`, child of `ChatGPT.exe`) serves `thread/list` from `state_5.sqlite`. The mobile/web Codex UI and the desktop sidebar therefore show **every row in `threads`**, including rows whose rollout file was deleted and whose cwd (a `%TEMP%` test dir) no longer exists. The sidebar groups by project (`flat-project-sidebar-preferences-v1.mode = "project"`), so orphaned test cwds surface as dead "projects". **This is the most plausible thing the user saw**, but the count cannot be reconciled from the data: on 2026-08-21 there were 25 stale rows across 15 distinct temp cwds, not 5. |

**Conclusion:** no *remote* object exists to clean. If the user saw a count of 5 somewhere, the
surface needs to be named (a screenshot, or "the phone app / the desktop sidebar / chatgpt.com/codex")
before anything is designed against it — nothing in this investigation could produce a 5.

---

## 2. Causal link: the tests *do* create the residue, and here is the exact mechanism

### 2.1 Launch shape

| Test | Launcher | `CODEX_HOME` | cwd | Provider | Lands in user's real DB? |
|---|---|---|---|---|---|
| `Antiphon.Agents.Pty.Tests` `Codex*CanaryTests` (headed) | `CxSession.BuildLaunch` → vendored `codex.exe` TUI | **not set** (inherits) | `CxSession.TempCwd()` = `%TEMP%\antiphon-codex-canary*` | `openai` (real ChatGPT auth) | **yes** — 13 rows |
| `Antiphon.Tests` `CodexAdapterIntegrationTests` (headed) | `HeadedCodexGate.BuildLaunch` via `RunnerCodexAdapter` | **not set** | `%TEMP%\antiphon-codex-roundtrip*` | `openai` | **yes** — 4 rows |
| `Antiphon.Tests` `CodexRealCliStubProxyCanaryTests` A-tier (`codex exec`) | `HeadedCodexGate` | **not set** | `%TEMP%` itself | `stub` (FakeLlmApi, `RealCliStubEnv.ForCodex`) | **yes** — 7 rows |
| … B-runner (`SessionRunnerRuntime` + `codex exec`) | same | **not set** | `%TEMP%\codex-brunner-<guid>\cwd` | `stub` | **yes** — 2 rows |
| `Antiphon.Tests` `CodexHerdrRealCliStubProxyCanaryTests` | herdr pane | **set per test** (`CodexHerdrRealCliStubProxyCanaryTests.cs:59`) | per-test | `stub` | **no** — the pattern that already works |

`RealCliStubEnv.ForCodex` (`src/Antiphon.FakeLlmApi/RealCliStubEnv.cs:82`) sets only
`OPENAI_API_KEY` plus five `-c model_providers.stub.*` args — it redirects the *model* traffic, not
the *state* directory. The `TEST stub-proxy` rows in the user's DB are the proof: `model_provider =
'stub'`, `originator = 'codex_exec'`, `cwd = %TEMP%`.

### 2.2 What each run writes, and what the tests clean

Per session Codex writes: `sessions/YYYY/MM/DD/rollout-<ts>-<uuid>.jsonl`; a `threads` row in
`state_5.sqlite` (`id`, `rollout_path`, `cwd`, `source` ∈ {`cli`,`exec`,`vscode`}, `model_provider`,
`first_user_message`, …); `thread_turns` / `thread_items` / `thread_history_projection_state` rows in
`thread_history_1.sqlite`; and log rows in `logs_2.sqlite` (130 MB, no per-thread key worth
chasing). The tests' `finally` blocks delete only the temp **cwd** (`CxSession.BestEffortDelete`,
`CodexAdapterIntegrationTests.TryDelete`) and kill the process (`KillAsync`, plus the CARD-0206
`PtyHostLeakSweep` `[After(Assembly)]` in `Antiphon.SessionRunner.Tests`). **No test, hook or script
in the repo deletes a Codex thread** (`grep` for `codex delete|codex archive|File.Delete` across the
Codex test files: only the three cwd/home `Directory.Delete` calls).

### 2.3 Why the doctor warning exists

`codex doctor` (this run, 2026-08-26): `rollout DB rows 101 · active files 75 · stale rows 25 ·
sources cli=75, exec=19, vscode=7 · model providers openai=91, stub=10`. The 25 stale rows are all
dated 2026-08-20 04:38–16:47 UTC — precisely the files the manual cleanup removed. Deleting a rollout
file by hand orphans its DB row; `codex delete` is the only path that removes both.

---

## 3. Census of the real state DB (101 rows, read from a copy)

| class | rows | rollout present | rollout GONE (stale row) |
|---|---|---|---|
| USER desktop app (`source=vscode`, originator `Codex Desktop`; cwds `Documents\Codex\…`, `C:\src\Antiphon`, `C:\src\ClaudeBot`) | 7 | 7 | 0 |
| USER other (`cli`, cwd `C:\Users\lndco`, "hi") | 1 | 1 | 0 |
| ANTIPHON delegate session (`cli`, cwd `C:\src\Antiphon` or `C:\Antiphon\worktrees\card-task-*`, first message `[antiphon-task:…]`) | 51 | 51 | 0 |
| TEST Pty.Tests headed canary (`%TEMP%\antiphon-codex-canary*`) | 13 | 1 | 12 |
| TEST `CodexAdapterIntegrationTests` (`%TEMP%\antiphon-codex-roundtrip*`) | 4 | 4 | 0 |
| TEST stub-proxy canary (`model_provider='stub'`; `%TEMP%`, `%TEMP%\codex-brunner-*`, one `<worktree>\.antiphon\llm-stub-probe\…`) | 10 | 10 | 0 |
| PROBE ad-hoc — `codex` run by an *agent session*, not by any test helper (`%TEMP%\claude\…\scratchpad`, `%TEMP%\codex-tui-probe-*`, `%TEMP%\codexprobe1`, `%TEMP%\cx0108-probe*`) | 15 | 2 | 13 |

Plus one archived row (`archived_sessions/rollout-…-01a01198…jsonl`, the user's MagSafe thread).

Full row-level listing of everything that is *not* USER/ANTIPHON (the only rows any cleanup may
consider). Thread ids are what `codex delete --force <id>` takes.

| created (UTC) | thread id | class | rollout | cwd | first message |
|---|---|---|---|---|---|
| 2026-08-20 04:38 | `01a01d76-d22f-78a1-a94b-8f9431fae11a` | PROBE ad-hoc | GONE | `%TEMP%\claude\C--src-antiphon\49f47739-…\scratchpad` | Reply with exactly the word OK and nothing e |
| 2026-08-20 04:38 | `01a01d77-1a6d-7b21-a3e7-9112c9911153` | PROBE ad-hoc | GONE | same | Reply with exactly the word OK and nothing e |
| 2026-08-20 04:40 | `01a01d78-eaf0-7b53-b7a1-f58f3dbc7482` | PROBE ad-hoc | GONE | same | What is your codeword? Answer with just the |
| 2026-08-20 04:41 | `01a01d79-83db-7ca3-be47-1c1ded725e1f` | PROBE ad-hoc | GONE | same | hi |
| 2026-08-20 04:41 | `01a01d79-9c26-7650-9876-6248fc8ed64c` | PROBE ad-hoc | GONE | same | hi |
| 2026-08-20 04:42 | `01a01d7a-020f-7f92-965a-979a3133afef` | PROBE ad-hoc | GONE | same | What is your codeword? Answer with just the |
| 2026-08-20 04:42 | `01a01d7a-1a19-7de1-a596-ec93bb4d7f87` | PROBE ad-hoc | GONE | same | What is your codeword? Answer with just the |
| 2026-08-20 15:14 | `01a01fbc-a122-7403-9c81-751b9ed69349` | PROBE ad-hoc | GONE | `%TEMP%\codexprobe1` | Reply with exactly the word PROBE and nothin |
| 2026-08-20 15:15 | `01a01fbd-d553-7340-b6a5-cffbe3533d56` | PROBE ad-hoc | GONE | `%TEMP%\codex-tui-probe-e4b9719f` | with exactly the token PROBE-657f87d519fc an |
| 2026-08-20 15:16 | `01a01fbe-bb3c-7e23-98db-5c45532de525` | PROBE ad-hoc | GONE | same | b2526831 and nothing else. |
| 2026-08-20 15:18 | `01a01fc0-642c-7ed1-82c1-39c33b343ba0` | PROBE ad-hoc | GONE | same | 2 |
| 2026-08-20 15:19 | `01a01fc1-d208-7681-8f19-cfc7392f7650` | PROBE ad-hoc | GONE | same | Count slowly from 1 to 900, one number per l |
| 2026-08-20 15:21 | `01a01fc3-13f2-7dd1-b237-ad039d6c663b` | PROBE ad-hoc | GONE | same | Count slowly from 1 to 900, one number per l |
| 2026-08-20 15:26 | `01a01fc8-2fc3-7030-9caa-f65b7876d026` | TEST Pty.Tests canary | GONE | `%TEMP%\antiphon-codex-canary0g2qdxr1.q4x` | CX-ONESHOT reply with exactly OK and nothing |
| 2026-08-20 15:44 | `01a01fd8-b715-75f1-9ec8-dbc17a2549c3` | TEST Pty.Tests canary | GONE | `%TEMP%\antiphon-codex-canaryp2gsdtta.d35` | Reply with exactly the token CX-380c22f76734 |
| 2026-08-20 15:45 | `01a01fd9-8647-72c2-ac4a-6bab748ba5ab` | TEST Pty.Tests canary | GONE | `%TEMP%\antiphon-codex-canary5p4eywwe.hex` | Reply with exactly the token CX-0fd241b52e25 |
| 2026-08-20 15:47 | `01a01fda-fe0d-7aa3-ba63-7e832e814413` | TEST Pty.Tests canary | GONE | `%TEMP%\antiphon-codex-canaryhkrko2ts.bma` | Reply with exactly the token CX-eec81f9c67a8 |
| 2026-08-20 15:48 | `01a01fdc-7317-7152-b329-2d852af85a2a` | TEST Pty.Tests canary | GONE | `%TEMP%\antiphon-codex-canaryg1ioy3gf.mok` | CX-ONESHOT reply with exactly OK and nothing |
| 2026-08-20 16:08 | `01a01fee-ca09-7b23-b552-c2e8730f6fb5` | TEST Pty.Tests canary | GONE | `%TEMP%\antiphon-codex-canarykww3nh5j.xgs` | *(empty — never prompted)* |
| 2026-08-20 16:09 | `01a01fef-97de-7b42-adcb-7c3cd546599e` | TEST Pty.Tests canary | GONE | `%TEMP%\antiphon-codex-canaryhbgf1bpp.ykf` | Reply with exactly the token CX-ROLLOUT-PROB |
| 2026-08-20 16:14 | `01a01ff4-4229-7983-abe3-998c0e2c51c3` | TEST Pty.Tests canary | GONE | `%TEMP%\antiphon-codex-canaryiafkfygi.o3t` | CX-ONESHOT reply with exactly OK and nothing |
| 2026-08-20 16:18 | `01a01ff7-c04c-78e3-b40e-39bb5b950b86` | TEST Pty.Tests canary | GONE | `%TEMP%\antiphon-codex-canaryslcglnsn.j2i` | Reply with exactly the token CX-ROLLOUT-PROB |
| 2026-08-20 16:18 | `01a01ff7-ddd2-7601-947b-211c115c6b38` | TEST Pty.Tests canary | GONE | `%TEMP%\antiphon-codex-canaryixe4ptzw.0xh` | CX-ONESHOT reply with exactly OK and nothing |
| 2026-08-20 16:45 | `01a0200f-dccf-7cf3-a1a6-ba3c2389bc29` | TEST Pty.Tests canary | GONE | `%TEMP%\antiphon-codex-canaryiy1m1d1b.v01` | CX-ONESHOT reply with exactly OK and nothing |
| 2026-08-20 16:47 | `01a02012-0e76-7820-b77b-5edd78c4ceae` | TEST Pty.Tests canary | GONE | `%TEMP%\antiphon-codex-canaryq2kc3g04.mxa` | CX-ONESHOT reply with exactly OK and nothing |
| 2026-08-20 19:47 | `01a020b7-29a9-7653-ad2a-cd6f1f5eb129` | PROBE ad-hoc | present | `%TEMP%\cx0108-probeholmx4yl.gre` | What is the capital of Japan? Answer with on |
| 2026-08-20 21:37 | `01a0211b-9361-7ca1-b9d6-e4e6c4e12854` | TEST CodexAdapterIntegrationTests | present | `%TEMP%\antiphon-codex-roundtriptng3kb1i.bfx` | Reply with exactly PONG and no other text. |
| 2026-08-20 21:38 | `01a0211c-79f4-7752-a7ab-814591c65c1c` | TEST Pty.Tests canary | present | `%TEMP%\antiphon-codex-canaryabf43ewn.mha` | CX-DONE reply with exactly OK and nothing el |
| 2026-08-20 21:46 | `01a02124-16fb-73c0-9df5-49f8ab56543f` | TEST CodexAdapterIntegrationTests | present | `%TEMP%\antiphon-codex-roundtripemp2f2hi.bhb` | Reply with exactly PONG and no other text. |
| 2026-08-20 21:49 | `01a02126-7a06-7313-9f43-4f298920e979` | TEST CodexAdapterIntegrationTests | present | `%TEMP%\antiphon-codex-roundtripfu20iab5.qxi` | Reply with exactly PONG and no other text. |
| 2026-08-20 21:51 | `01a02128-abda-75d1-bd01-f7cbf1dcba48` | TEST CodexAdapterIntegrationTests | present | `%TEMP%\antiphon-codex-roundtripkgy3g0zw.fwn` | Reply with exactly PONG and no other text. |
| 2026-08-24 12:19 | `01a033b5-ef5f-7810-b4c4-53cdc50a3806` | TEST stub-proxy (probe) | present | `<worktrees>\card-task-14fb184e\.antiphon\llm-stub-probe\cwd-codex2` | Reply with EXACTLY the single token STUBPROB |
| 2026-08-24 13:18 | `01a033ec-2784-7bf3-bb74-29ce61fca522` | TEST stub-proxy | present | `%TEMP%` | Reply with exactly: STUBCANARY-CODEX |
| 2026-08-24 13:19 | `01a033ed-289c-7c53-b349-c1d9af5316cf` | TEST stub-proxy | present | `%TEMP%` | Reply with exactly this token and nothing el |
| 2026-08-24 13:19 | `01a033ed-357e-7593-848f-5efd5c091b34` | TEST stub-proxy | present | `%TEMP%` | This prompt contains nonce STUBCANARY-7451d5 |
| 2026-08-24 13:52 | `01a0340b-3b7d-7db0-b36a-729eb0a6e8ed` | TEST stub-proxy | present | `%TEMP%` | Reply with exactly this token and nothing el |
| 2026-08-24 13:52 | `01a0340b-4551-7dd3-afe2-e5b38e3efa10` | TEST stub-proxy | present | `%TEMP%` | This prompt contains nonce STUBCANARY-f20a5a |
| 2026-08-24 15:49 | `01a03476-3e80-7340-ac4e-f717392dd0e6` | TEST stub-proxy (B-runner) | present | `%TEMP%\codex-brunner-c8b82f786e014a75b53712875fa03905\cwd` | Reply with exactly this token and nothing el |
| 2026-08-24 16:13 | `01a0348c-ae61-7022-8eff-2e2d803916c7` | TEST stub-proxy | present | `%TEMP%` | Reply with exactly this token and nothing el |
| 2026-08-24 16:13 | `01a0348c-bffc-7d00-93f3-86b1ac1ba98d` | TEST stub-proxy | present | `%TEMP%` | This prompt contains nonce STUBCANARY-d956c7 |
| 2026-08-24 16:14 | `01a0348c-d9ae-7c03-abee-db254f8df1bc` | TEST stub-proxy (B-runner) | present | `%TEMP%\codex-brunner-34b1472bcebb47e69bda22e92bfa3d6c\cwd` | Reply with exactly this token and nothing el |
| 2026-08-25 14:25 | `01a0394f-7e32-7a92-b736-324911883d36` | PROBE ad-hoc | present | `%TEMP%\claude\C--src-antiphon\fdffe536-…\scratchpad\cxprobe` | Reply with exactly: OK |

### 3.1 Live processes (nothing leaked by tests)

| pid | what | verdict |
|---|---|---|
| 13816 → 14612 | `codex.exe … app-server` + `codex-code-mode-host.exe`, children of `ChatGPT.exe` (4220), up since 2026-08-17 | **the user's Codex Desktop — never touch** |
| 35036 (← node 17592 ← cmd 9476 ← `Antiphon.PtyHost.exe` 41828) | npm-shim TUI, `--model gpt-5.6-terra`, caveman bundle; Antiphon runner session `f04cd114-18d9-4cf0-b71e-3ef581f9261a`, `Running` since 2026-08-22 12:58Z (the CARD-0190 transcript-bind probe, task `d3bd6bad`) | **an Antiphon-launched session, not test residue**; the runner still reports it Running. Whether anyone still wants it is a card-board question, not a cleanup-script question. Not touched. |

The CARD-0206 `PtyHostLeakSweep` and every test's `KillAsync` already cover leaked *processes*; the
gap is purely Codex's on-disk record.

---

## 4. The sanctioned primitive, measured on a copy

Setup: copied `state_5.sqlite(+wal)` and `thread_history_1.sqlite(+wal)` into
`<scratchpad>\cxhome2`, rewrote `rollout_path` to the copy, copied one real test-residue rollout
(`cx0108-probe`, id `01a020b7…`) into the mirror tree, then ran with `CODEX_HOME=<copy>`:

| command | result |
|---|---|
| `codex delete <uuid>` (no flag, non-interactive) | exit 1: `cannot confirm session deletion without an interactive terminal; rerun with --force and a session UUID` |
| `codex delete --force 01a01d76-…` (row exists, **file already gone**) | exit 0 `Deleted session …` — `threads` row gone; `thread_history` rows were already 0 |
| `codex delete --force 01a020b7-…` (row + file present) | exit 0 — `threads` row gone, 6 `thread_items` + 1 projection row gone, rollout file gone; 99 rows left of 101 |
| `codex delete <uuid>` in an *empty* home | `No active or archived session found matching …` (it resolves via the DB/sessions tree, so a foreign id is refused cleanly) |

So `codex delete --force <uuid>` is a complete, Codex-owned, id-addressed delete that also repairs
the doctor's stale-row warning. `codex archive <uuid>` is the reversible sibling (sets `archived=1`
and moves the file to `archived_sessions/`) but was **not** measured against a missing file and is
not useful for stale rows.

Caveats an implementer must carry:

- The real `state_5.sqlite` is held open by the desktop app-server (pid 13816). SQLite WAL makes a
  concurrent `codex delete` safe on disk, but the desktop/mobile thread list may not refresh until
  the app restarts. Measure once on the real home with a single allow-listed id before batching.
- `~/.codex/thread-writer-locks/` exists; a thread being written by a **live** `codex.exe` must never
  be deleted. The exclusion is by process: enumerate live `codex.exe` cwds (`Win32_Process`) and
  skip any row whose `cwd` matches, in addition to the age gate.
- `codex doctor --json --all` lists only a 5-row *sample* of stale rows (measured), so it cannot be
  the enumerator; the sweep must read the DB. Python 3.10 with `sqlite3` is on this machine and was
  used here with `?mode=ro&immutable=1` for read-only access; a PowerShell-only reader would need
  `System.Data.SQLite`/`Microsoft.Data.Sqlite` shipped alongside — implementer's choice, but the
  reader must be read-only and the **only writer is `codex delete --force`** (never SQL `DELETE`,
  never `Remove-Item` on a rollout — that is how the stale rows were created).

---

## 5. Design

### 5.1 Stop the bleeding: give tests their own `CODEX_HOME` (the fix at the source)

This is the change that makes the problem disappear rather than get swept, and the repo already has
the working pattern (`CodexHerdrRealCliStubProxyCanaryTests` sets `CODEX_HOME` per test and has left
**zero** rows in the user's DB).

1. **Stub-proxy canaries (A-tier `codex exec`, B-runner)** — set `CODEX_HOME` to a per-test temp dir
   in `RealCliStubEnv.ForCodex`'s overlay env (or at the two call sites). They authenticate with a
   synthetic `OPENAI_API_KEY` against FakeLlmApi, so an empty home costs nothing. Delete the dir in
   `finally`. Zero risk, removes 10 of the 44 residue rows' *source* immediately. Note Codex prints
   `WARNING: … Refusing to create helper binaries under temporary dir` for a `%TEMP%` home — a
   warning only (measured), but pin it in the test so a future version that hard-fails is caught.
2. **Real-service headed tests (`Pty.Tests` canaries, `CodexAdapterIntegrationTests`)** — these need
   the user's ChatGPT auth, which lives in `CODEX_HOME\auth.json`, so an *empty* temp home cannot
   log in. Use a **persistent, dedicated test home** instead:
   `%LOCALAPPDATA%\Antiphon\codex-test-home` (not `%TEMP%`, so Codex will create its helper
   binaries and the auth survives), seeded **once by the user** with
   `CODEX_HOME=%LOCALAPPDATA%\Antiphon\codex-test-home codex login` and a minimal `config.toml`
   (`approval_policy = "never"`, `sandbox_mode = "danger-full-access"` — whatever
   `--dangerously-bypass-approvals-and-sandbox` already implies). `HeadedCodexGate` /
   `CxSession` then export `CODEX_HOME` on every launch and **skip** (not fail) with a clear message
   when the test home is missing or has no `auth.json`. **Never copy the user's `auth.json`
   programmatically** — that is a credential copy into a second location the user did not choose.
   With this, every headed run's rollouts, rows and logs live in the test home; the desktop app and
   the remote-control view never see them; and cleanup of the whole thing is `Remove-Item` on one
   directory, or `codex delete` inside it, with no attribution question at all.
3. `CodexTranscriptTailer` resolves `CODEX_HOME` from the **launch env first** (`CodexTranscriptTailer.cs:166-175`),
   so the runner-side tailing in `CodexAdapterIntegrationTests` keeps working when the env carries
   the override — but verify with the existing `CodexTranscriptTailerTests` launch-env case.
4. Document the opt-in in `docs/agent-kinds.md` (Codex section) next to `ANTIPHON_CODEX_HEADED_TESTS`:
   the test home path, the one-time `codex login`, and that headed runs no longer touch `~/.codex`.

Out of scope, stated so nobody widens this: **Antiphon delegate sessions** (51 rows, `cwd`
`C:\src\Antiphon` / `C:\Antiphon\worktrees\card-task-*`) deliberately run in the user's real home so
they appear in the desktop app; rows whose worktree has since been removed look just like the test
residue in the sidebar but are the user's real work product. Not this card.

### 5.2 Per-run TUnit teardown — considered, not recommended

An `[After(Test)]` that runs `codex delete --force <this test's thread id>` against the *user's* home
would (a) still write every run into the user's DB first, (b) race the desktop app-server that holds
that DB, (c) need the thread id, which the TUI never prints and the tests only discover by
cwd-matching rollouts (the CARD-0006 discovery rules exist precisely because this is fragile), and
(d) do nothing for the ad-hoc probes that make up 13 of the 25 stale rows. Once §5.1 is in, there is
nothing in the user's home for a teardown to delete. If §5.1 is *not* adopted for the real-service
tests, prefer §5.3 over a teardown for the same reasons.

### 5.3 One-time cleanup + backstop: `scripts/cleanup-codex-test-residue.ps1` on the Windmill pattern

Mirror `scripts/cleanup-claude-sessions.ps1` (CARD-0144) exactly in shape — it is the proven,
already-scheduled pattern (`u/lndcobra/claude_session_cleanup`, Mon 09:15 Europe/London, SSH from
the desktop worker; see memory `reference_windmill_cleanup_schedule`). Schedule the new job at
**Mon 09:30** so the three SSH sessions never overlap; do **not** add a Windows Scheduled Task.

Contract:

- **Dry run by default; `-Execute` to mutate; `-MaxPerRun` (default 10) refuses rather than
  truncates; JSON report under `logs/codex-test-residue/` built from an allow-list of fields.
  ASCII-only. Exit codes as CARD-0144.**
- **Reader:** `state_5.sqlite` `threads` opened read-only (`immutable=1`); join to filesystem
  existence of `rollout_path`. **Writer:** `codex delete --force <id>` only; parse its
  `Deleted session` line as the success oracle and re-read the row afterwards.
- **Candidate = ALL of:**
  1. `cwd` (after stripping `\\?\`) matches one of the test helpers' fixed prefixes —
     `%TEMP%\antiphon-codex-canary*`, `%TEMP%\antiphon-codex-roundtrip*`,
     `%TEMP%\codex-brunner-*\cwd` — **or** `model_provider = 'stub'`. (The A-tier stub canaries run
     in bare `%TEMP%`; `stub` is what identifies them, and `stub` is never a real provider.)
     The prefixes live in one place in the script and the tests reference the same literals — a
     new test helper that invents a new prefix without adding it here leaves residue, which the
     report will show, not silently delete.
  2. `source` ∈ {`cli`, `exec`} — **never** `vscode` (that is the desktop app, i.e. the user).
  3. `created_at` older than `-OlderThanHours` (default 24) so an in-flight headed run is never
     touched.
  4. No live `codex.exe` whose cwd equals the row's `cwd`, and the row's `id` is not named by any
     file under `~/.codex/thread-writer-locks/`.
  5. `archived = 0` and not `is_pinned` (a pinned or archived row is a human decision).
- **Never a candidate, by construction:** `source='vscode'`; any cwd outside `%TEMP%` unless
  `model_provider='stub'`; anything in `remote_control_enrollments`, `thread_sections`, or
  `archived_sessions/`; any `%TEMP%\claude\…\scratchpad`, `codex-tui-probe-*`, `codexprobe*`,
  `cx0108-probe*` row — those are **ad-hoc probes** typed by agent sessions with no test helper
  behind them. The script **reports** them under a separate `unattributed` heading with id, cwd and
  first message so the user can decide, and never deletes them. This is the honest answer to the
  card's "if you cannot distinguish, say so": the 13 stale ad-hoc rows and the 2 present ones in §3
  look like probes to a human (every first message is "Reply with exactly …"), but no rule in the
  repo produced them, so no rule may delete them.
- **Report-first rollout, as CARD-0144:** run report-only for two weekly cycles; the first
  `-Execute` is a manual run by the user against the §3 allow-listed ids (12 stale canary rows + 4
  roundtrip + 1 present canary + 10 stub = 27 rows), after which `codex doctor` should read
  `stale rows 12` (the 13 unattributed stale rows remain until the user confirms them) and the
  desktop sidebar loses the dead `antiphon-codex-*` projects.

### 5.4 Guard-rail additions

- `AGENTS.md` gotcha: *"Never delete a Codex rollout file by hand — `codex delete --force <uuid>` is
  the only delete; a hand-deleted file leaves a `threads` row that `codex doctor` reports as stale
  and the desktop/mobile sidebar still lists."*
- `AGENTS.md` gotcha: *"Headed and stub-proxy Codex tests must set `CODEX_HOME`; a launch that
  inherits the user's `~/.codex` writes into the user's Codex Desktop thread list."* Pin with a
  test-side assertion that every `HeadedCodexGate`/`CxSession` launch env carries `CODEX_HOME`.
- Agents running ad-hoc `codex exec` probes from a scratchpad (the source of 15 rows) should do so
  with `CODEX_HOME=<scratchpad>\codex-home`; add that line to `docs/agent-kinds.md` under Codex.

---

## 6. Slices for the implementer

| # | Slice | Verification |
|---|---|---|
| S1 | `CODEX_HOME` per test for the stub-proxy canaries (`RealCliStubEnv.ForCodex` overlay or call sites) + `finally` delete + warning pin | `ANTIPHON_REAL_CLI_STUB_TESTS=1` run of `CodexRealCliStubProxyCanaryTests`; then `codex doctor` row count unchanged from before the run |
| S2 | Dedicated persistent test home for real-service headed tests; skip-with-message when unseeded; docs in `agent-kinds.md` | user seeds once; `ANTIPHON_CODEX_HEADED_TESTS=1` run of `CodexAdapterIntegrationTests`; rollout appears under the test home, `~/.codex` row count unchanged |
| S3 | `scripts/cleanup-codex-test-residue.ps1` + `scripts/test-cleanup-codex-test-residue.ps1` (fixture = a saved `threads` dump, `-InputJson` shape as CARD-0144) | report on the real home lists exactly the 27 allow-listed + 15 unattributed rows from §3, zero USER/ANTIPHON rows |
| S4 | Windmill schedule `u/lndcobra/antiphon_codex_residue_cleanup` Mon 09:30, report-only | two weekly reports, then user-run `-Execute` |
| S5 | AGENTS.md gotchas (§5.4) | — |

Estimated effort: S1 small; S2 medium (auth-seeding UX + skip path); S3 medium (mirror CARD-0144);
S4 small (memory `reference_windmill_cleanup_schedule` has the API procedure); S5 trivial.

---

## 7. Open questions for the user (blocking only for the "5" part)

1. **Where** were the "5 remote foundational agents" seen — desktop sidebar, phone app, or
   chatgpt.com/codex — and are they still there? Nothing measured here produces a 5; if they are
   still visible, a screenshot resolves this in one look.
2. Confirm the 15 `PROBE ad-hoc` rows in §3 are deletable (they were typed by agent sessions during
   CARD-0099/0108/0168/0190 work). The script will never decide this itself.
3. Is Antiphon session `f04cd114` (CARD-0190 probe, running since 08-22) still wanted? Unrelated to
   tests; flagged because it is the only non-desktop live `codex.exe` on the box.
