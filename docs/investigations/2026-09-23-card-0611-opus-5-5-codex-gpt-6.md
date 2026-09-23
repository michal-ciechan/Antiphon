# CARD-0611 — Are Opus 5.5, Codex Sol 6 and Codex Terra 6 available and adoptable?

**Date:** 2026-09-23
**Stage:** Investigate (task `44919e60`, worktree `feat/card-task-44919e60`)
**Verified against:** installed `claude.exe` **2.1.266** (`C:\users\lndco\.local\bin\claude.exe`, the
binary every Antiphon Claude delegate launches); locally cached `~/.local/share/claude/versions/2.1.278`;
npm `@anthropic-ai/claude-code` dist-tags (`stable` 2.1.267, `latest` 2.1.280) and the downloaded
`@anthropic-ai/claude-code-win32-x64@2.1.280` native binary; installed `@openai/codex` **0.155.1**;
downloaded `@openai/codex@0.156.0-win32-x64` and `@0.156.1-win32-x64`; `codex debug models` bundled and
live; `C:\Users\lndco\.codex\models_cache.json`; `C:\Users\lndco\.codex\version.json`;
`C:\Users\lndco\.claude.json` gated-config cache; `learn.chatgpt.com/docs/changelog`.
**No exec/dispatch probe was run** — no tokens were spent. Every measurement below is a `--version`,
a catalog dump, a binary string read, or a free npm/registry/doc fetch.

---

## Verdict

| Model | Real? | Reachable from this box today? | What adoption needs |
|---|---|---|---|
| **Claude Opus 5.5** (`claude-opus-5-5`) | **Yes** | **No** — installed CLI 2.1.266 resolves `opus` to `claude-opus-5` | **Zero change to `ModelLevelAliases`.** Upgrade `claude` to **2.1.280+** on the box; then patch `ModelAlias.IsOpus` + stale doc/test text |
| **Codex Sol 6** (`gpt-6-sol`) | **Yes** | Listed in this account's live catalog under client 0.155.1; **not** in the installed CLI's bundled catalog | Upgrade `@openai/codex` to **0.156.1+**, re-probe `codex exec --ephemeral -m gpt-6-sol`, then bump `ForCodex(High)` |
| **Codex Terra 6** | **No — does not exist** | n/a | Nothing to adopt. GPT-6 shipped **Astra / Sol / Luna**; there is no `gpt-6-terra` |

---

## 1. Claude Opus 5.5

### 1.1 The alias IS the mechanism — but it resolves client-side

`ModelLevelAliases.ForClaude(High)` returns the bare family alias `"opus"`
(`server/Application/Services/ModelLevelAliases.cs:31`), and Antiphon launches
`claude.exe --model opus`. The card's premise — that a family alias auto-picks up the current
model — is **correct in the product** but **not at the API boundary**: the alias-to-id table is
compiled into the CLI binary, so the family's current model is whatever the *installed* CLI says
it is.

Alias table read out of each binary (`aliases:{opus:{default:...}}` / `latest_per_family`):

| Binary | `opus` alias default | `latest_per_family.opus` | `claude-opus-5-5` present? |
|---|---|---|---|
| `.local/bin/claude.exe` **2.1.266** (active) | `claude-opus-5` | `claude-opus-5` | **no** (0 occurrences) |
| `versions/2.1.278` (cached, Sep 20, not active) | `claude-opus-5` | `claude-opus-5` | **no** (0 occurrences) |
| npm `latest` **2.1.280** | **`claude-opus-5-5`** | **`claude-opus-5-5`** | **yes** (41 occurrences) |

2.1.280's catalog row, read verbatim from the binary:

```
{id:"claude-opus-5-5",family:"opus",display_name:"Opus 5.5",knowledge_cutoff:"June 2026",
 provider_ids:{first_party:"claude-opus-5-5",bedrock:"us.anthropic.claude-opus-5-5",
 vertex:"claude-opus-5-5",foundry:"claude-opus-5-5",...},
 vertex_region_env_var:"VERTEX_REGION_CLAUDE_5_5_OPUS",fallback_3p:"claude-opus-5",
 context:{window:1e6,native_1m:!0,supports_1m_beta:!0,supports_1m_suffix:!0},
 max_output_tokens:{default:128000,upper:128000},pricing:"tier_4_20_cache_read_0_20",...}
```

Picker label in 2.1.280: `label:"Opus", description:` **`Opus 5.5`** ` · ...`,
`descriptionForModel:"Opus 5.5 - best for everyday, complex tasks"`. Statsig key `"claude-opus-5-5":"opus55"`.
The 1M-context suffix form `claude-opus-5-5[1m]` exists. `claude-opus-5` survives as a selectable id
(79 occurrences, and it is 5.5's `fallback_3p`), so nothing that names the old id breaks.

### 1.2 Direct live proof that `opus` is still Opus 5 on this box

This very investigation session is the measurement. `Win32_Process` for the session named
`task-44919e60` (PID 69336) shows launch args **`--model opus`**, the process image is
`c:\users\lndco\.local\bin\claude.exe` (2.1.266), and the session's own runtime identity is
**`claude-opus-5` / "Opus 5"**. Dated 2026-09-23, i.e. after Opus 5.5's release. Zero cost, no probe.

### 1.3 Anthropic's own in-product statement agrees

`C:\Users\lndco\.claude.json`, cached gate `tengu_startup_announcements`:

```json
{ "id": "opus-5-5-update",
  "text": "Get to finished work sooner with Opus 5.5. Update Claude Code to try it.",
  "maxImpressions": 3, "priority": 0, "accentBar": false }
```

"**Update Claude Code to try it**" is the vendor saying, in the shipped gated config, that the CLI
upgrade is the gate. `announcementImpressions` records `"opus-5-5-update": 3` — this box has already
been shown it three times.

### 1.4 Version floor

`2.1.279` was never published to npm. Published versions run 2.1.277, 2.1.278, **2.1.280**. 2.1.278
does not carry `claude-opus-5-5`; 2.1.280 does. **The floor is `claude` 2.1.280.** npm `stable` is
still 2.1.267, so this is a `latest`-channel upgrade today, not a `stable` one.

Ops note, not a finding: the active launcher is 2.1.266 (Sep 9) while `versions/2.1.278` (Sep 20) sits
downloaded and unused, with `autoUpdatesProtectedForNative: true` in `.claude.json`. Whatever put
2.1.278 on disk did not swap the bin. That mechanism is unexamined here.

### 1.5 What in Antiphon would actually need changing

**`ModelLevelAliases.ForClaude` needs no edit** — the card's "zero changes needed" branch is the right
one for the ladder. Two real surfaces do go stale the moment the CLI is upgraded:

- **`server/Application/Services/ModelAlias.cs:147`** — `IsOpus(folded)` matches only
  `"opus"`, `"opus 5"`, `"claude opus"`, `"claude opus 5"`. `Fold()` turns `.`, `-` and `_` into spaces, so
  the two strings Opus 5.5 will present as — the TUI's `Opus 5.5` folding to `"opus 5 5"`, and the id
  `claude-opus-5-5` folding to `"claude opus 5 5"` — **both return null**. `Normalize` returning null means the
  CARD-0022/CARD-0309 model-availability-hold path cannot map an Opus 5.5 usage-limit line onto the
  canonical `opus` alias and falls back to the session's launch alias. `ModelAliasTests.cs:19` pins the
  current behaviour and would need the new arguments.
- **Doc/comment text** naming `opus` to `claude-opus-5`: `ModelLevelAliases.cs:12`.

Hold/ladder vocabulary itself is unaffected: `ModelAlias.Opus` is the family string `"opus"`,
`DelegatableAliases` carries `(ClaudeCode, "opus")`, and the client's `tierAlias` shows the alias, not
the id — none of those name a version.

---

## 2. Codex Sol 6 (`gpt-6-sol`) — real, and the slug is confirmed

### 2.1 Catalogs

`codex debug models --bundled` (installed **0.155.1**) — priority / slug:

```
1 gpt-6-astra   6 gpt-5.6-sol   7 gpt-5.6-terra   8 gpt-5.6-luna
10 gpt-daybreak-blue-latest  11 gpt-daybreak-red-latest  12 gpt-5.5  16 gpt-5.4  43 codex-auto-review
```

`codex debug models` (**live refresh**, same 0.155.1 binary) — the account catalog:

```
1 gpt-6-astra   2 gpt-6-sol   3 gpt-6-luna   3 gpt-reserve(hide)
4 gpt-5.6-sol   7 gpt-5.6-terra   8 gpt-5.6-luna   12 gpt-5.5   43 codex-auto-review
```

`C:\Users\lndco\.codex\models_cache.json`: `fetched_at` `2026-09-23T05:39:35.195492900Z`,
`etag W/"56739dad38b64be2ed8d94d8733a641d"`, **`client_version 0.155.1`**, slugs
`gpt-6-astra, gpt-6-sol, gpt-6-luna, gpt-reserve, gpt-5.6-sol, gpt-5.6-terra, gpt-5.6-luna, gpt-5.5, codex-auto-review`.

`gpt-6-sol` row (live): `visibility: "list"`, `supported_in_api: true`, `priority: 2`, `upgrade: null`,
`context_window` 272000 / `max_context_window` 872000, reasoning `low` through `ultra`,
`tool_mode "code_mode_only"`, `multi_agent_version "v2"`, `shell_type "unified_exec"`,
`service_tiers: [{id:"priority", name:"Fast", description:"1.5x speed"}]`. **The catalog carries no
minimum-CLI-version field** — there is no key resembling `min_version` or `requires` on any row.

### 2.2 Why "listed live under 0.155.1" is suggestive but not proof

CARD-0396 (`docs/investigations/2026-09-05-card-0396-codex-astra.md`) established the mechanism:
the account catalog is fetched from `https://chatgpt.com/backend-api/codex/models?client_version=<v>`
— **version-parameterized** — and on CLI 0.152.0 that catalog contained *no* Astra row while the
backend answered `-m gpt-6-astra` with HTTP 400 *"The 'gpt-6-astra' model requires a newer version of
Codex."* Today's fetch is tagged `client_version 0.155.1` **and contains `gpt-6-sol`**, which is the
opposite of the Astra signature. That is real evidence the service considers 0.155.1 eligible, but
the 400 is emitted at request time, not at catalog time, so it is not conclusive.

### 2.3 CLI version floor for Sol 6

| codex-cli | bundled catalog has `gpt-6-sol`? |
|---|---|
| 0.155.1 (installed) | **no** |
| 0.156.0 | **no** (`gpt-6-astra, gpt-5.6-sol, gpt-5.6-terra, gpt-5.6-luna, ...`) |
| 0.156.1 (npm `latest`) | **yes** — `1 gpt-6-astra, 2 gpt-6-sol, 3 gpt-6-luna, 6 gpt-5.6-sol, 7 gpt-5.6-terra, 8 gpt-5.6-luna, ...` |

`learn.chatgpt.com/docs/changelog`:
- **0.156.0** (2026-09-22): *"GPT-6 Sol and GPT-6 Luna are rolling out to Codex and ChatGPT Work at
  lower token prices than their GPT-5.6 predecessors."*
- **0.156.1** (2026-09-22): *"Choose GPT-6 Sol or GPT-6 Luna from the model picker. The rate-limit
  switch prompt now recommends GPT-6 Luna."*

This is the exact Astra shape (0.153.1 API-configurable, then 0.153.4 picker-visible). **Treat 0.156.1 as
the floor**, matching how the repo records `gpt-6-astra requires codex-cli 0.153.4+`
(`docs/agent-kinds.md:221`).

`~/.codex/version.json` on this box: `latest_version 0.155.1`, `last_checked_at 2026-09-22T17:25:40Z`,
`dismissed_version 0.154.0` — the box's own update check is a release behind npm `latest` (0.156.1).

---

## 3. Codex Terra 6 — not released

There is no `gpt-6-terra`, anywhere:

- absent from 0.155.1's bundled catalog, 0.156.0's, and **0.156.1's** (the newest published CLI);
- absent from the live account catalog — `grep -c "gpt-6-terra"` over the 433 KB live dump returns **0**;
- absent from the changelog: the only GPT-6 names in the 0.156.x entries are **Sol** and **Luna**.

GPT-6 shipped as **Astra / Sol / Luna**. Terra has no GPT-6 successor at all. `gpt-5.6-terra` is still
live at priority 7 with `upgrade: null` — it is not deprecated and has no migration target, unlike
`gpt-5.5`, whose live row now carries `upgrade: {model: "gpt-5.6-sol"}`.

The card's third item is therefore void as written. The live ladder question it raises is different and
belongs to Plan: **`gpt-6-luna` sits at priority 3, above `gpt-5.6-sol` (4) and far above
`gpt-5.6-terra` (7)**, so the Medium rung's real candidate is Luna 6, not a Terra 6 that does not exist.

---

## 4. Blast radius if the pins move (measured, not designed)

`gpt-5.6-sol` / `gpt-5.6-terra` appear in these **non-historical** surfaces:

| File | What |
|---|---|
| `server/Application/Services/ModelLevelAliases.cs` | `ForCodex` ladder + its doc comment |
| `server/Application/Services/ModelAlias.cs` | `Gpt56Sol`/`Gpt56Terra` consts, `DelegatableAliases`, `IsSol`/`IsTerra` folded-text arms (`"gpt 6 sol"` would need adding, same gap as `IsOpus`) |
| `server/Application/Services/AgentTuiRunnerCatalog.cs:44` | Codex suggestion list |
| `client/src/features/delegations/taskVisuals.ts:34,39` | `tierAlias` map + the 0.153.4 comment |
| `client/src/features/orchestrator/ModelAvailabilityHoldForm.tsx:28` | hold dropdown options |
| `client/src/test/mocks/handlers.ts:22` | MSW availability fixture |
| `docs/agent-kinds.md:185-186,218-224,386` | ladder table, the 0.153.4 floor note, launch-arg example |
| `docs/features/011-ai-agent-tui-configuration/02b-runner-capabilities-and-model-discovery.md:57` | suggestion table |
| `tests/Antiphon.Tests/Application/DelegationKindDisplayTests.cs:59-62,77-83,91,127,143` | ladder arguments, display strings, the "never a bare tier name" guard |
| `tests/Antiphon.Tests/Application/ModelAliasTests.cs:28-33` | Normalize arguments |
| `tests/Antiphon.Tests/AgentTui/AgentTuiProfileServiceTests.cs`, `tests/Antiphon.Tests/Application/CodexDelegateDispatchTests.cs`, `client/src/features/delegations/taskVisuals.test.ts`, `client/src/features/orchestrator/ModelAvailabilityPanel.test.tsx` | assertions on the current slugs |

Fixtures that record what a *past* CLI painted (`tests/Antiphon.Agents.Pty.Tests/Fixtures/CodexStartup/captures.json`,
`tests/Antiphon.SessionRunner.Tests/Fixtures/codex-tui-multi-turn.jsonl`,
`scripts/hooks/__tests__/fixtures/cefed08a-card0246.jsonl`) and everything under
`docs/investigations/` and `docs/superpowers/plans/` are historical — the card's own non-goal.

The Claude side is **not** in this list: nothing in `server/` or `client/` names `claude-opus-5` as a
launch value. The only two live mentions are `ModelLevelAliases.cs:12` (a doc comment) and
`ModelAliasTests.cs:19` (a Normalize argument).

---

## 5. Remaining uncertainties

1. **No exec probe was run for `gpt-6-sol`** — forbidden by this brief, and it is what CARD-0396 used
   to turn "catalog says yes" into "backend says yes". Until `codex exec --ephemeral -m gpt-6-sol`
   returns something other than the version-gate 400, the floor of 0.156.1 is inferred from the
   bundled-catalog diff and the changelog, not measured.
2. **Whether 0.155.1 would already accept `-m gpt-6-sol`** is open. The live catalog listing it under
   `client_version 0.155.1` says probably yes; the Astra precedent (catalog absence and backend 400
   moved together) says the safe read is to upgrade first.
3. **Opus 5.5 behind the upgraded alias is unexercised.** The 2.1.280 alias table is read from the
   binary; no session has run on it here. Whether this account is in the 5.5 rollout is not proven by
   a binary string — the vendor announcement being *targeted at this install* is the only account-level
   signal, and it is indirect.
4. **`claude` 2.1.280 is `latest`, not `stable`** (stable is 2.1.267). Upgrading the box to 2.1.280
   moves every Claude delegate 14 releases at once; nothing here measured that jump's other effects.
5. **Codex 0.156.1's other changes** (rate-limit switch prompt now recommending GPT-6 Luna, picker
   default behaviour) were not examined against CARD-0574's boot-wedge sensitivity to update banners.

---

## Not done, noted

- Fix idea, one line: leave `ForClaude` alone and upgrade the two CLIs, then bump `ForCodex(High)` to
  `gpt-6-sol`, consider `gpt-6-luna` for Medium in place of the never-shipped Terra 6, and extend
  `ModelAlias`'s `IsOpus` / `IsSol` / `IsLuna` folded-text arms so the new display names normalize.
