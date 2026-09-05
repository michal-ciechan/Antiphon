# CARD-0396 — is Astra a real Codex model?

**Date:** 2026-09-05 (task `c8a12749`). **Card:** CARD-0396.
**Status:** confirmed. No app code was changed. Isolated `CODEX_HOME` probes used `--ephemeral`; no rollout landed under `~/.codex/sessions/2026/09/05`. A copied `auth.json` used for isolated probes was deleted after the run.

**Verified against:** worktree `feat/card-task-c8a12749`; npm `codex.cmd` **0.152.0** at `C:\Users\lndco\AppData\Roaming\npm\codex.cmd` (`npm list -g @openai/codex` = `0.152.0`; `npm view @openai/codex version` = **0.153.4**); `C:\Users\lndco\.codex\models_cache.json` (`fetched_at` `2026-09-04T07:57:58.396187200Z`, `client_version` `0.152.0`); `C:\Users\lndco\.codex\version.json`; `codex debug models` (bundled and live); four `codex exec --json --ephemeral` probes; OpenAI's published pages retrieved 2026-09-05.

---

## Verdict, in one sentence

**Yes.** Astra is a real OpenAI Codex model. The selectable slug is **`gpt-6-astra`**, not `astra` and not `gpt-5.6-astra`. It is **not selectable on this machine today**: installed CLI 0.152.0 has no Astra in its catalog, and the Codex backend HTTP 400s `-m gpt-6-astra` with *"The 'gpt-6-astra' model requires a newer version of Codex."* npm latest is **0.153.4**, which added Astra to the bundled picker (and made it the bundled default). Do not add it to Antiphon's ladder until that CLI is on the box and a post-upgrade exec actually accepts the slug.

---

## Plain yes/no for the card

| Question | Answer |
|---|---|
| Is Astra a real Codex model? | **Yes.** Official slug `gpt-6-astra`. |
| Is the bare id `astra` a Codex model? | **No.** Same backend 400 as a garbage id. |
| Is `gpt-5.6-astra` a Codex model? | **No.** Same backend 400 as a garbage id. GPT-6 did not ship Sol/Terra/Luna rungs. |
| Can Antiphon launch it on this box today? | **No.** CLI 0.152.0 is too old. |
| Plausible Antiphon tier? | **Frontier** (`ModelLevelAliases.ForCodex` currently `gpt-5.6-sol`). High is a product choice (keep `gpt-5.6-terra`, or slide Sol down onto High). |

---

## 1. This repo today (no Astra)

Repo-wide `astra` / `Astra` in `*.{cs,md,json}` is only an unrelated Unicode comment in `tests/Antiphon.Tests/Application/ColumnTextTests.cs:43` ("astral chars"). The Codex ladder is still the GPT-5.6 celestial family:

```52:57:server/Application/Services/ModelLevelAliases.cs
    public static string ForCodex(AgentModelLevel level) => level switch
    {
        AgentModelLevel.Frontier => "gpt-5.6-sol",
        AgentModelLevel.High => "gpt-5.6-terra",
        AgentModelLevel.Medium or AgentModelLevel.Low => "gpt-5.6-luna",
        _ => "gpt-5.6-terra",
    };
```

Same three slugs: `AgentTuiRunnerCatalog.cs:44`, `ModelAlias.cs:20-37`, `docs/agent-kinds.md:138-141`. That matches CARD-0099's 2026-08-20 measurement against CLI 0.147.0 (`docs/superpowers/plans/2026-08-20-card-0099-codex-delegate-kind-plan.md` §1). It is stale relative to GPT-6, not a local typo.

---

## 2. Installed CLI catalog (measured 2026-09-05)

`codex.cmd --version` → `codex-cli 0.152.0`. `--help` / `exec --help` contain no "astra" (findstr). There is no `codex models` subcommand; the catalog dump is `codex debug models` (`--bundled` skips refresh).

**Bundled catalog (CLI 0.152.0 binary), 10 rows, zero Astra:**

| # | slug | display_name | priority |
|---|---|---|---|
| 0 | `gpt-5.6-sol` | GPT-5.6-Sol | 1 |
| 1 | `gpt-5.6-terra` | GPT-5.6-Terra | 2 |
| 2 | `gpt-5.6-luna` | GPT-5.6-Luna | 3 |
| 3 | `gpt-daybreak-blue-latest` | Daybreak Blue | 3 |
| 4 | `gpt-daybreak-red-latest` | Daybreak Red | 3 |
| 5 | `gpt-5.5` | GPT-5.5 | 7 |
| 6 | `gpt-5.4` | GPT-5.4 | 16 |
| 7 | `gpt-5.4-mini` | GPT-5.4-Mini | 23 |
| 8 | `gpt-5.2` | GPT-5.2 | 29 |
| 9 | `codex-auto-review` | Codex Auto Review | 43 |

`codex debug models` without `--bundled` printed the same 10 bundled rows (407,794 bytes). It did **not** rewrite `~/.codex/models_cache.json`.

**Account cache** `C:\Users\lndco\.codex\models_cache.json` (`fetched_at` 2026-09-04T07:57:58.396187200Z, etag `W/"1e10c2927ad7b0d7cddc841252b75cb1"`), 8 rows, **no `astra` substring anywhere in the 199,358-byte file:**

`gpt-reserve`, `gpt-5.6-sol`, `gpt-5.6-terra`, `gpt-5.6-luna`, `gpt-5.5`, `gpt-5.4`, `gpt-5.4-mini`, `codex-auto-review`.

`~/.codex/version.json` at probe time: `latest_version` `0.153.0`, `last_checked_at` `2026-09-03T19:24:59.555145900Z`, **`dismissed_version` `0.153.0`**. Astra catalog work landed in 0.153.1–0.153.4 (below); this box dismissed 0.153.0 on launch day.

Desktop native binary `C:\Users\lndco\AppData\Local\OpenAI\Codex\bin\e305f1c75d8da435\codex.exe` last write 2026-08-17 — older than the npm CLI, unused by Antiphon (`docs/agent-kinds.md` launches `codex.cmd`).

A live refresh of `https://chatgpt.com/backend-api/codex/models?client_version=0.152.0` during exec returned **HTTP 401 `token_expired`** (cf-ray `a3663171dc02ef49-LHR` isolated, `a36633ae0eea53a3-LHR` real home). The 2026-09-04 cache is therefore the last successful account catalog, not a 2026-09-05 refresh.

---

## 3. Backend accept/reject (trivial-cost exec probes)

Command shape (isolated `CODEX_HOME` with copied `auth.json` + minimal `config.toml` `model = "gpt-5.6-luna"` / `model_reasoning_effort = "low"`; `--ephemeral --skip-git-repo-check --sandbox read-only`; cwd a scratch dir; one-word prompt). Exit 1 each time; no completion tokens billed (turn failed before a model reply). Repeated `-m gpt-6-astra` against the real `~/.codex` with `--ephemeral`; no `~\.codex\sessions\2026\09\05` directory exists after the probes.

Local CLI first, then the ChatGPT-account Codex backend. Same two-step shape CARD-0099 measured for `-m luna` ("Model metadata for `luna` not found" then HTTP 400).

| `-m` | Local catalog | HTTP 400 `invalid_request_error` | elapsed |
|---|---|---|---|
| `astra` | `Model metadata for \`astra\` not found. Defaulting to fallback metadata; this can degrade performance and cause issues.` | `The 'astra' model is not supported when using Codex with a ChatGPT account.` | 6.1 s |
| `gpt-6-astra` | `Model metadata for \`gpt-6-astra\` not found.` (same fallback warning) | **`The 'gpt-6-astra' model requires a newer version of Codex. Please upgrade to the latest app or CLI and try again.`** | 1.9 s isolated / 8.2 s real home |
| `gpt-5.6-astra` | same fallback warning | `The 'gpt-5.6-astra' model is not supported when using Codex with a ChatGPT account.` | 3.1 s |
| `card0396-does-not-exist` (control) | same fallback warning | `The 'card0396-does-not-exist' model is not supported when using Codex with a ChatGPT account.` | 1.9 s |

`gpt-6-astra` is the only id the backend treats as a known model. `astra` and `gpt-5.6-astra` are indistinguishable from a made-up slug. The version-gate 400 is the mechanism that stops a launch on 0.152.0 even if someone typed the official slug.

Real-home stdout (2026-09-05T15:17Z):

```json
{"type":"thread.started","thread_id":"01a07225-8982-7340-8ac4-605e8eb92419"}
{"type":"item.completed","item":{"id":"item_0","type":"error","message":"Model metadata for `gpt-6-astra` not found. Defaulting to fallback metadata; this can degrade performance and cause issues."}}
{"type":"turn.started"}
{"type":"error","message":"{\"type\":\"error\",\"status\":400,\"error\":{\"type\":\"invalid_request_error\",\"message\":\"The 'gpt-6-astra' model requires a newer version of Codex. Please upgrade to the latest app or CLI and try again.\"}}"}
{"type":"turn.failed","error":{"message":"{\"type\":\"error\",\"status\":400,\"error\":{\"type\":\"invalid_request_error\",\"message\":\"The 'gpt-6-astra' model requires a newer version of Codex. Please upgrade to the latest app or CLI and try again.\"}}"}}
```

---

## 4. OpenAI's published catalogue (retrieved 2026-09-05, URLs not invented)

| Source | What it says |
|---|---|
| [openai.com/index/gpt-6-astra](https://openai.com/index/gpt-6-astra/) | GPT-6 Astra launch post. Rolling out to Plus/Pro/Business/Enterprise "over the coming days". API id `gpt-6-astra`. Codex: notes across context windows; experimental `config.toml` feature, default for Astra in coming weeks. |
| [developers.openai.com/api/docs/models/gpt-6-astra.md](https://developers.openai.com/api/docs/models/gpt-6-astra.md) | Model ID `gpt-6-astra`. 1,050,000 context. Reasoning: low/medium/high/xhigh/max. API $10 / $50 per 1M in/out. |
| [learn.chatgpt.com/docs/models](https://learn.chatgpt.com/docs/models) (Codex Models) | Recommended #1: **Astra**, copy-command **`codex -m gpt-6-astra`**. Then 5.6 Sol / Terra / Luna. "Choose Astra when a task needs the strongest capability… Sol offers depth and polish, Terra suits everyday work, Luna suits clear, repeatable tasks." GPT-6 has no Luna/Terra/Sol variants. |
| [learn.chatgpt.com/docs/changelog](https://learn.chatgpt.com/docs/changelog) | **0.153.1** (2026-09-03): "Added support for configuring GPT-6-Astra through the API without changing the default model or showing it in the model picker" ([#42605](https://github.com/openai/codex/pull/42605)). **0.153.4** (2026-09-04): "Fixed Astra’s visibility in the bundled model picker and made it the bundled default when no model is explicitly configured" ([#42874](https://github.com/openai/codex/pull/42874)). GitHub release tag `rust-v0.153.4`. |
| [learn.chatgpt.com/docs/pricing](https://learn.chatgpt.com/docs/pricing) | ChatGPT-credit table lists GPT-6 Astra above GPT-5.6 Sol (Plus 5–45 local messages / 5-hour window vs Sol 10–100). |

The celestial-family guess in the brief is half-right: Astra is the same naming *style*, but it is **GPT-6's single flagship**, not a fourth GPT-5.6 rung beside Sol/Terra/Luna.

---

## 5. If Antiphon adds it (not done this pass)

Prerequisite, outside this repo: **upgrade `codex.cmd` to ≥ 0.153.4** and re-run `codex exec --ephemeral -m gpt-6-astra` until the 400 is gone. 0.153.4 also makes Astra the bundled default when no `--model` is passed; Antiphon's Codex path always appends `--model <ladder slug>` (`docs/agent-kinds.md` §2), so delegates stay pinned, but a bare operator `codex` would flip. Do not bump the ladder first — 0.152.0 would 400 every Frontier Codex dispatch.

Plausible mapping after the CLI is current:

| Tier | Today | Conservative (recommended default) | Aggressive (use the new capability order) |
|---|---|---|---|
| Frontier | `gpt-5.6-sol` | **`gpt-6-astra`** | **`gpt-6-astra`** |
| High | `gpt-5.6-terra` | `gpt-5.6-terra` (unchanged) | `gpt-5.6-sol` |
| Medium / Low | `gpt-5.6-luna` | `gpt-5.6-luna` | `gpt-5.6-luna` (or Medium=`terra`) |

Conservative keeps today's High/Medium/Low spend and only moves Frontier onto the new flagship — matching "Astra is above Sol" without a four-rung reshuffle. Aggressive matches the docs' Astra > Sol > Terra > Luna order and would make High a real model change (today High is terra; Sol is only Frontier). GPT-6 Astra Pro is a plan-gated sibling; not probed; do not put it on the worker ladder without a separate measurement.

Files that would have to change together (CARD-0099's "two copies of a ladder" trap):

| File | Change |
|---|---|
| `server/Application/Services/ModelLevelAliases.cs` | `ForCodex` Frontier → `gpt-6-astra`; rewrite the 5.6-only / "bump when 5.7 ships" comments (`:16`, `:36-51`). |
| `server/Application/Services/AgentTuiRunnerCatalog.cs:44` | Prepend `gpt-6-astra` to the Codex identifier list. |
| `server/Application/Services/ModelAlias.cs` | `Gpt6Astra` constant, `DelegatableAliases` Codex row, `Normalize` / `IsAstra` (folded forms `gpt 6 astra` / `astra`). Bare `astra` must **not** be launched — the backend rejects it. |
| `docs/agent-kinds.md` | §3 table (`:138`) and the Codex launch line (`:297`). |
| `docs/features/011-ai-agent-tui-configuration/02b-runner-capabilities-and-model-discovery.md:57` | Codex suggestions list. |
| Tests that pin the 5.6 ladder | `DelegationKindDisplayTests.the_codex_ladder_answers_for_the_codex_kind` (`:59` Frontier=`gpt-5.6-sol`); **`the_codex_ladder_pins_full_versioned_slugs_and_never_a_bare_tier_name` asserts `ShouldStartWith("gpt-5.6-")` (`:81`) and would go red on `gpt-6-astra`**; `LaunchModelArgumentAppenderTests` `:22`; `AgentTuiProfileServiceTests` `:823`. |

`CodexLaunchArgs.cs` does not hard-code slugs (effort still comes from the tier). Headed Codex canaries pin `gpt-5.6-luna` and can stay there.

Astra is spendier than Sol (docs: Plus 5–45 vs 10–100 messages / window; API $10/$50 vs Sol $4/$20 per 1M). A Frontier bump is a scheduled spend, not a free rename.

---

## Uncertainties

1. **Account catalog could not be refreshed today.** `GET …/backend-api/codex/models?client_version=0.152.0` returned 401 `token_expired` from both isolated and real `CODEX_HOME`. Last successful fetch remains 2026-09-04 07:57Z (no Astra). The version-gate 400 for `gpt-6-astra` still arrived, so the backend evaluated the slug; we did not see a post-upgrade picker list for this ChatGPT account.
2. **No successful Astra turn was run.** Doing so requires CLI ≥ 0.153.4 and would spend real Frontier-class tokens. Not done.
3. **`gpt-6-astra-pro` was not probed.** Docs give it to Pro/Business/Enterprise. Out of scope unless someone wants it as a distinct rung.
4. **Rollout vs this account.** OpenAI said "over the coming days" / "once Astra is available to your account". The version-gate 400 (not "not supported") is evidence this account is far enough along that a new enough CLI would be the remaining gate — not proof a 0.153.4 picker would show it.
5. **401 during models refresh** may mean interactive Codex on this box also needs `codex login` before any upgrade test. Not investigated beyond the probe logs.

## Not done, noted

Upgrade npm `@openai/codex` to ≥ 0.153.4, then re-probe `codex exec --ephemeral -m gpt-6-astra` for a real accept before changing `ModelLevelAliases`.
