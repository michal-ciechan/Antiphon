# CARD-0395: installed Grok transport measurements

Measured 2026-09-06 by task cb9e8180. Executable `C:\Users\lndco\.grok\bin\grok.exe`; version `grok 1.0.13 (5e9a58528b76) [stable]`; SHA-256 `BF43DC75F5478A106EAB1E86D422C963E4DBE9666CF14DAB363733D27BF1E672`.

## Method

Ran `grok --version`, `grok --help`, `grok inspect --help`, and `grok agent --help` directly. A disposable Python `ThreadingHTTPServer` bound to `127.0.0.1` on an OS-assigned port captured JSON request bodies; it never recorded credential headers. Child processes used Python `subprocess.run` with an argument list, `shell=False`, an individual probe cwd/home and a 25-second ceiling. No production server/runner or delegate was involved.

Each child used `GROK_HOME=<case>\home`, both `GROK_CLI_CHAT_PROXY_BASE_URL` and `GROK_XAI_API_BASE_URL` pointing to that loopback port, synthetic `GROK_CODE_XAI_API_KEY`, telemetry/feedback disabled, and updater disabled. The completed matrix/follow-ups also used a constant synthetic external auth-provider command in the disposable environment. No operator auth store was copied, read, rewritten, or printed. `GROK_*`, `XAI_*`, `X_LLM_*` and `ANTIPHON_*` inherited overlays were removed before constructing each child overlay. This was a disposable measurement harness; future committed tests must use `RealCliStubEnv.ForGrok`.

Baseline command shape:

```text
grok.exe --always-approve --no-subagents --disable-web-search --max-turns 1 -p CARD0395-PROMPT-<case> <case flags>
```

Follow-ups used `--max-turns 3`. The stub supplied complete Responses SSE, including `input_tokens_details`, `output_tokens_details`, per-event `sequence_number`, and content-part `annotations`. Older minimal fixture attempts failed on these missing fields or timed out; they are not successful measurements. A few final helper requests were canceled by Grok after it completed; all final CLI runs still exited 0 and printed the stub reply.

In this binary/model configuration, the main user turn used `/responses`, with model `grok-4.6`, a system message, task nonce and 24/25 tools. Helper/title requests also hit `/responses` but had a much smaller instruction set. The measured body, not the URL alone, identifies the user-turn oracle. Every completion case recorded the synthetic credential match on `/api-key`. These were local stub responses, not live model calls.

## Results

| Case / exact added flags or input | Exit | Captured result |
|---|---:|---|
| `--rules CARD0395-LITERAL` | 0 | Literal in system `<human_rules>`. Baseline tool set: 24. |
| `--rules @<absolute path with spaces>\rules with spaces.md` | 0 | Literal `@` plus path in `<human_rules>`; file start/end sentinels absent. |
| `--rules <absolute path with spaces>\rules with spaces.md` | 0 | Literal path in `<human_rules>`; file start/end sentinels absent. |
| `GROK_RULES= CARD0395-ENV` followed by LF and a second line | 0 | Env marker absent from user-turn input. |
| `GROK_RULES_FILE=<rules path>` | 0 | File sentinels absent. |
| `<isolated GROK_HOME>\rules\antiphon.md` | 0 | File sentinels absent in this setup. |
| `--agent <case>\agent.md`, valid front matter | 0 | File body in system prompt; default prompt/tool behavior changed. |
| Same, explicit `promptMode: extend` | 0 | Same change; 25 tools rather than 24. |
| `<isolated GROK_HOME>\AGENTS.md` | 0 | File sentinels absent in this setup. Follow-up `inspect --json` also did not list this file. Probe cwd was under a gitignored `.antiphon` tree; this is not a universal claim about home-file discovery. |
| `[agent] definition='<rules path>'`, file without valid definition front matter | 0 | Sentinels absent; not evidence against config support. |
| `GROK_AGENT=<rules path>`, same invalid definition | 0 | Sentinels absent; not evidence against env support. |
| Config definition with valid front matter (follow-up) | 0 | File body in system prompt. Native agent-file configuration supported. |
| `GROK_AGENT` with valid definition (follow-up) | 0 | File body in system prompt. Native agent-file env supported. |
| Short literal `--rules` read-file bootstrap plus scripted native `read_file` (follow-up) | 0 | CLI executed actual filesystem read; subsequent tool output carried start/end markers and every content line. |
| Fresh `--session-id <uuid> --rules CARD0395-OLD-RULE` (follow-up) | 0 | OLD in system prompt. |
| Same cwd/home, `--resume <uuid> --rules CARD0395-NEW-RULE` (follow-up) | 0 | OLD remains in both tool-bearing requests; NEW absent from both. |

The explicit native definition was:

```yaml
---
name: antiphon-probe
description: probe
promptMode: extend
---
CARD0395-FILE-START
AGENT-PROFILE
CARD0395-FILE-END
```

The baseline literal-rules system message was 5,816 characters. The explicit agent-file system message was 4,954 characters with this sample body: the baseline browser-verification section disappeared. `wait_commands_or_subagents` appeared in the native agent-file tool set. The profile reads a file, but is not transparent append-only transport.

For the pointer round trip, the stub sent an actual Responses `function_call` named `read_file` with `arguments={"target_file":"<absolute rules path>"}`. The next user-turn request contained `function_call_output` with both `CARD0395-FILE-START` and `CARD0395-FILE-END`, all 180 repetitions of the content line, and `café`. The file was 14,262 bytes, SHA-256 `04746b21171f4a516ff829bd955ba62eb58be2ab074e32202590ac2f00c70501`; tool output was 13,965 characters. The disposable Python writer produced CRCRLF (364 CR, 182 LF), and Grok's read tool normalized/displayed line endings. This proves complete content retrieval, not byte-identical tool rendering. Production file writes must preserve the composed bytes exactly; tests should assert disk bytes separately from normalized provider read output.

No live model selected that read itself. No real multi-compaction survival experiment was run in Plan. Native compaction format in Antiphon is `auto_compact_completed`; the card's `compacted/context_compacted` pair describes Codex. These distinctions are mandatory acceptance gates in the plan.

## Reproduction artifacts and sources

The exact disposable harness and raw local bodies remain at:

```text
C:\Antiphon\worktrees\card-task-cb9e8180\.antiphon\card0395-probe\probe.py
C:\Antiphon\worktrees\card-task-cb9e8180\.antiphon\card0395-probe\extension.py
C:\Antiphon\worktrees\card-task-cb9e8180\.antiphon\card0395-probe\matrix-report.json
C:\Antiphon\worktrees\card-task-cb9e8180\.antiphon\card0395-probe\matrix-requests.json
C:\Antiphon\worktrees\card-task-cb9e8180\.antiphon\card0395-probe\report.json
C:\Antiphon\worktrees\card-task-cb9e8180\.antiphon\card0395-probe\requests.json
```

Run `python .antiphon/card0395-probe/probe.py`, then preserve its matrix files before `python .antiphon/card0395-probe/extension.py` overwrites the latest report/requests files. These ignored scratch artifacts may disappear at worktree cleanup; the method, exact flag/input matrix, numeric results and conclusions above are the durable record. Raw request bodies include discovered project/user instruction material and are deliberately not committed. TestDesign should build the permanent canary on the repository's sanctioned stub fixtures, not depend on scratch files.

Primary-source cross-checks: [CLI reference](https://docs.x.ai/build/cli/reference), [project rules guide](https://github.com/xai-org/grok-build/blob/main/crates/codegen/xai-grok-pager/docs/user-guide/12-project-rules.md), and [agent definition implementation](https://github.com/xai-org/grok-build/blob/72a61251fcffb464bcc687aeb5a998e5a98ec0c9/crates/codegen/xai-grok-agent/src/config.rs). Upstream source checked at `72a61251fcffb464bcc687aeb5a998e5a98ec0c9` describes definition prompt modes; it is not the installed build's commit and does not establish installed behavior. The installed binary captures above are the decisive evidence.
