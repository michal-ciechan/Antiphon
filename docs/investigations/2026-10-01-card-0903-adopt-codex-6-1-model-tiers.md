# CARD-0903: Can Antiphon use the Codex 6.1 model, and what must change?

**Date:** 2026-10-01
**Stage:** Investigate (task `4ca86971`, branch `feat/card-task-4ca86971`)
**Operator question:** "check if we can use Codex's latest model for 6.1"
**Measured on:** the server2 session-runner container. Inputs were the installed `/usr/local/bin/codex`
(resolves to `/opt/codex/0.156.1/...`) and npm-registry tarballs of `@openai/codex` 0.157.1, 0.158.0,
0.159.0, 0.159.1, 0.159.2, 0.159.3, 0.160.0 and 0.161.0-alpha.13 (`-linux-x64`), each unpacked into a
scratch directory. Vendor pages were fetched on 2026-10-01.
**No model session was started, no prompt was sent and no quota was spent.** Each downloaded binary
was run only as `--version` or `debug models --bundled`, with `HOME`/`CODEX_HOME` pointed at an
empty scratch directory. No auth file, token or real Codex home was read. The live (network)
catalog refresh was not run; see §6.

Confidence tags: **[confirmed]** means measured or read verbatim here. **[probable]** means strong
indirect evidence. **[unconfirmed]** means not established.

---

## Verdict

- **The 6.1 generation is one model: `gpt-6.1-sol`.** [confirmed: vendor models page, API model
  page and every bundled catalog from 0.159.1 on.] There is no `gpt-6.1-astra` and no
  `gpt-6.1-luna` in any published catalog up to 0.161.0-alpha.13, or on the models page.
  [confirmed]
- **Neither runner can use it today.** The server2 image pins **codex-cli 0.156.1** [confirmed].
  The desktop was last recorded at **0.158.0** on 2026-09-30 [probable; not re-measured, see §2.2].
  The first CLI that bundles the model is **0.159.1** [confirmed].
- **Adoption is feasible.** It is a CLI upgrade on both runners followed by a small code and
  test change; no new tier is needed. Whether this account's backend accepts `gpt-6.1-sol` is
  **[unconfirmed]**, because confirming it requires a probe the operator has not sanctioned (§6).
- Filed **CARD-0903** "Adopt Codex 6.1 model tiers" (Backlog, Normal/Normal). The board had no
  duplicate (searches in §7).

---

## 1. How Antiphon maps tiers to Codex model IDs today

| Fact | Source | Confidence |
|---|---|---|
| `ForCodex`: Frontier `gpt-6-astra`, High `gpt-6-sol`, Medium `gpt-5.6-terra`, Low `gpt-5.6-luna`, default arm `gpt-6-sol` | `server/Application/Services/ModelLevelAliases.cs:65-72` | confirmed |
| Launch-arg selection goes through `ForLaunch`, which uses `ForCodex` for Codex; display text uses `For` | `ModelLevelAliases.cs:91-107` | confirmed |
| The dispatcher picks the tier alias via `TierAliasFor` | `server/Application/Services/AgentTaskDispatcher.cs:5789-5795` | confirmed |
| The model reaches the CLI as `--model <slug>`, via the revision's `ModelArgumentName` (CARD-0182 D1) or `AgentRegistry.Resolve` on the profile-less path | `server/Application/Services/AgentTuiLaunchResolver.cs:486-527`; `server/Application/Dtos/AgentLaunchSpec.cs:41-42` | confirmed |
| Reasoning effort is always set explicitly: `-c model_reasoning_effort=` xhigh/high/medium/low for Frontier/High/Medium/Low | `server/Application/Services/CodexLaunchArgs.cs:58-70`; `AgentTaskDispatcher.cs:5829`; `AgentSessionLaunchComposer.cs:73` | confirmed |
| No per-runner or per-host model override exists. `GET /api/runner-defaults` holds only runner routing (`globalRunnerId: server2`, `kindDefaults: []`, revision 2). The only model override is a pinned TUI profile's exact `ModelId` | live GET 2026-10-01; `AgentTuiLaunchResolver.cs:486-527` | confirmed |
| `server/appsettings.json:272` `ContextWindow:ModelOverrides` is `{}`. `gpt-6.1-sol` has the same catalog context (272000/872000) as `gpt-6-sol` | appsettings; bundled catalog | confirmed |
| Docs: `docs/agent-kinds.md` §3 "Model levels" (Codex bullet at about lines 253-270) records the 0.153.4 floor for Astra and 0.156.1 for `gpt-6-sol` | file | confirmed |

Tests that pin the ladder:

- `tests/Antiphon.Tests/Application/ModelAliasTests.cs:34-39,122-123,154-167`. Lines 163-167 pin `ForCodex(High) == Gpt6Sol` and the four-slug list.
- `tests/Antiphon.Tests/Application/DelegationKindDisplayTests.cs:59-60` and the following lines.
- `tests/Antiphon.Tests/Application/CodexDelegateDispatchTests.cs:52,90-91,239`.
- `CodexLaunchArgsTests.cs`, and `AgentTuiProfileServiceTests.cs`.
- Client: `client/src/features/delegations/taskVisuals.test.ts:110-111,129-130` and `ModelAvailabilityPanel.test.tsx`.

## 2. Installed CLIs and what they know

### 2.1 server2 runner [confirmed]

- `codex --version` returns `codex-cli 0.156.1`. `/usr/local/bin/codex` resolves to
  `/opt/codex/0.156.1/package/vendor/x86_64-unknown-linux-musl/bin/codex`.
- The install is pinned by version and SHA-512 in `docker/session-runner-grok/Dockerfile`
  (`ARG CODEX_VERSION=0.156.1`, `ARG CODEX_SHA512=...`). It is asserted again in
  `docker/session-runner-grok/verify-codex-image.sh:11,70`. The install is root-owned, with no
  auto-update. Updating means an image rebuild and redeploy, which is the CARD-0849 rollout path;
  nothing was touched here.
- The bundled catalog (`codex debug models --bundled`) has no 6.1 model. Priorities:
  1 `gpt-6-astra`, 2 `gpt-6-sol`, 3 `gpt-6-luna`, 6 `gpt-5.6-sol`, 7 `gpt-5.6-terra`,
  8 `gpt-5.6-luna`, 12 `gpt-5.5`.

### 2.2 Desktop runner (Windows) [probable]

- There is no read-only route to the desktop CLI version from this host.
  `GET /api/agent-tui/profiles/cec57c0b-8d44-4205-b72d-6f57bb066b39` returns
  `validationSummary.runnerVersion = null`, and every model row has `runnerVersion = null`.
- The last recorded value is **0.158.0** (`docs/investigations/2026-09-30-card-0796-desktop-codex-qualification.md:9`,
  "Resolved CLI reported version `0.158.0`"). 0.158.0 does not bundle `gpt-6.1-sol` (below).
  Re-measuring needs `codex --version` on the desktop.

### 2.3 The CLI floor for `gpt-6.1-sol` [confirmed]

| codex-cli (npm publish UTC) | bundles `gpt-6.1-sol`? |
|---|---|
| 0.156.1 (server2) | no |
| 0.157.1 (2026-09-26) | no |
| 0.158.0 (2026-09-28; desktop as last recorded) | no |
| 0.159.0 (2026-09-29 08:10) | no |
| **0.159.1** (2026-09-29 20:36) | **yes** |
| 0.159.2, 0.159.3 | yes |
| **0.160.0** (2026-10-01 20:26, npm `latest`) | yes |
| 0.161.0-alpha.13 (`alpha`) | yes; still no other 6.1 slug |

Integrity of the `-linux-x64` tarballs: each local `sha512sum` equals npm's `dist.integrity`
(compared byte for byte).

- 0.160.0: `288ffbdcea86ac7991d7cb3bc9aec4d4da95eab4f4cb7971af4b3c4b5a91da6eb353a41117f1e5479dbd8c02cb879be37549ec453e00868c6dd1ac5be2463df6`
- 0.159.3: `c651f275f3a4b2736dfeacf8044545be3316ba7c1e69548db616bf0e4554c16667559c5baa2270b174a5da2643934e1bea4fe0d0e23b8d4d3a5eacc7076b39a2`

### 2.4 The `gpt-6.1-sol` catalog row (0.160.0 bundled) [confirmed]

Fields that differ from `gpt-6-sol`:

- `display_name`: `GPT-6.1-Sol`.
- `description`: "Latest workhorse model for coding and everyday work". `gpt-6-sol` is now "Previous generation workhorse model."
- `priority`: **1**. That is above `gpt-6-astra` (2), `gpt-6-sol` (3) and `gpt-6-luna` (4); the 5.6 models move to 5, 8 and 9.
- `default_reasoning_level`: `low`, the same as Astra's.
- `service_tiers`: priority "Fast" is "2x speed, increased usage".
- `supports_reasoning_effort_updates`: `true`.
- `availability_nux`: "Maximize usage with GPT-6.1 Sol. Try it on complex work for near-Astra performance at a lower cost."
- `model_messages.persistent_instructions` differs in length (62253 vs 59453 chars).

Fields the same as `gpt-6-sol`: `supported_reasoning_levels` (low/medium/high/xhigh/max/ultra),
`context_window` 272000, `max_context_window` 872000, `tool_mode`, `shell_type`,
`multi_agent_version`, `visibility: list`, `supported_in_api: true`, and `upgrade: null`.

Already in the 0.156.1 bundle, the current Medium and Low rungs carry `upgrade` pointers:
`gpt-5.6-terra` points to `gpt-6-sol`, and `gpt-5.6-luna` points to `gpt-6-luna`. Neither has a
`retirement_at`. The only `retirement_at` in any catalog is on `gpt-5.4` (2026-08-31), which is not
on the ladder. [confirmed]

## 3. Vendor documentation (fetched 2026-10-01)

| Source | Says | Confidence |
|---|---|---|
| [learn.chatgpt.com/docs/changelog](https://learn.chatgpt.com/docs/changelog) | 2026-09-29: "GPT-6.1 Sol offers near-Astra performance for complex work at a lower cost than Astra… Use `gpt-6.1-sol`." **0.159.1**: "Added GPT-6.1 Sol as the default model in the bundled catalog and Amazon Bedrock Mantle and Runtime catalogs." Luna is not mentioned | confirmed |
| [learn.chatgpt.com/docs/models](https://learn.chatgpt.com/docs/models) | Order: Astra (`codex -m gpt-6-astra`, "Our most capable model"), **GPT-6.1 Sol** (`codex -m gpt-6.1-sol`), GPT-6 Luna, GPT-5.5. 6.1 Sol: "Standard and Fast modes are available at launch. Ultrafast support for GPT-6.1 Sol is coming later." It is available in the Codex CLI, the IDE extension, the desktop app and ChatGPT Work. `gpt-6-sol` and the 5.6 models are no longer listed on the page | confirmed |
| [developers.openai.com/api/docs/models/gpt-6.1-sol.md](https://developers.openai.com/api/docs/models/gpt-6.1-sol.md) | API id `gpt-6.1-sol`, no dated snapshot. Context 1,050,000; max output 128,000. API effort is low/medium(default)/high/xhigh/max, with **no `ultra`** (the Codex catalog lists ultra). Pricing is $2 input, $0.10 cached, $10 output per 1M tokens | confirmed (doc); the effort difference between API and Codex catalog is noted, not resolved |
| [learn.chatgpt.com/docs/pricing](https://learn.chatgpt.com/docs/pricing) | Credits per 1M input/cached/output: **6.1 Sol 50 / 2.5 / 250**; 6 Sol 50 / 5 / 250; Astra 250 / 25 / 1,250; 6 Luna 2.5 / 0.25 / 12.5; 5.6 Terra 50 / 5 / 300; 5.6 Luna 5 / 0.5 / 30. Plus local messages per 5 hours: 6.1 Sol 15-160, 6 Sol 15-150, Astra 5-45. "GPT-5.5 retires from ChatGPT, ChatGPT Work, and Codex on all plans on October 14, 2026." | confirmed |
| [developers.openai.com/api/docs/deprecations](https://developers.openai.com/api/docs/deprecations) | No entry for `gpt-6-*` or `gpt-5.6-*` | confirmed (absence on that page) |

On the subscription, `gpt-6.1-sol` costs the same as or less than `gpt-6-sol` (half the cached-input
rate) and has a slightly higher message band. Astra is about 5x the credits. [confirmed from the pricing page]

## 4. Change surface

The ladder maps rung by rung; **no new tier is needed**. The only 6.1 model is a Sol, so the
natural move is **High: `gpt-6-sol` to `gpt-6.1-sol`**. Frontier stays `gpt-6-astra`, which the
vendor still calls "most capable"; catalog priority 1 is the picker default, not a capability rank.
Moving Frontier to 6.1 Sol would cut Frontier credit cost by about 5x, but it is an operator
decision, not an evidence finding. Medium and Low are untouched (the Terra-vs-`gpt-6-luna`
question stays open, as in `ModelLevelAliases.cs:51-52`).

The change is **code, not config**: the ladder is compiled in and no setting overrides it. The
non-historical edit list:

| File | Change |
|---|---|
| `server/Application/Services/ModelLevelAliases.cs:65-72` (+ comments 14-23, 43-62) | High arm and default arm; floor note 0.159.1 |
| `server/Application/Services/ModelAlias.cs:21-41,92-96,176-181` | New const (e.g. `Gpt61Sol`); `DelegatableAliases`. **Normalize gap:** `Fold("gpt-6.1-sol")` gives `"gpt 6 1 sol"`, and `Fold("GPT-6.1-Sol")` gives the same. No arm matches, so `Normalize` returns null and a usage-limit hold line naming 6.1 Sol cannot map to an alias (the same class of gap as CARD-0611's `IsOpus`). Decide where bare `sol` points |
| `server/Application/Services/AgentTuiRunnerCatalog.cs:44` | Codex suggestion list |
| `server/Application/Services/CodexLaunchArgs.cs:43-57` | Comment only. `high` is in 6.1 Sol's catalog levels, so no value changes |
| `client/src/features/delegations/taskVisuals.ts:33-44` | `CODEX_ALIASES` mirror |
| `client/src/features/orchestrator/ModelAvailabilityHoldForm.tsx:27-30`, `client/src/test/mocks/handlers.ts:21-22` | Hold dropdown and MSW fixture |
| `client/src/features/orchestrator/pipelineStageModel.ts:162-171` | Comment. `gpt-6.1-sol` is 11 chars, the same length as `gpt-6-astra`, so `compactAlias` needs no new arm |
| `docs/agent-kinds.md` §3 Codex bullet; `docs/features/011-ai-agent-tui-configuration/02b-runner-capabilities-and-model-discovery.md` | Ladder text and floor |
| Tests in §1 | Expected slugs; new Normalize arguments |

Not affected [confirmed by reading]:

- `src/Antiphon.Agents.Pty/CodexStartupReadiness.cs:140-200,268+` reads the model row or footer by
  structure. `GPT-6.1-Sol high · <cwd>` passes the `GPT-` prefix and effort checks.
- No server code parses `gpt-` slugs beyond the files above.

Pre-existing hold rows keyed `gpt-6-sol` will not apply to the new alias.

## 5. Risks

1. **The CLI jump is the larger risk, not the model.** Both runners must reach 0.159.1 or later
   (0.160.0 is `latest`). That is 3 to 4 minor releases for server2. The 0.158.0 TUI already
   changed the ready screen (no `model:` row; CARD-0858/0859 on the desktop) and added the
   update modal (CARD-0777). The 0.159/0.160 TUI was not examined against the startup-readiness
   gate, the done detection (CARD-0108) or the transcript ingest. **[unconfirmed]**
2. **Backend acceptance for this account is not measured.** In the Astra precedent (CARD-0396),
   catalog absence and an HTTP 400 "requires a newer version of Codex" moved together.
   **[unconfirmed]**
3. **New model behaviour on stage reports.** This covers the `--- next stage ---` block, the
   `[antiphon-report:…]` token, and the Check and Review interpreters' reading of output.
   `persistent_instructions` changed by about 2.8k chars. **[unconfirmed]**
4. **Quota.** The message band is slightly higher and the cached rate lower. The Fast tier now
   says "increased usage", but Antiphon does not select service tiers. Rate-limit header shape is
   unexamined. **[unconfirmed]**
5. **Effort mismatch.** The API page omits `ultra`, but Antiphon never sends `ultra`. No impact. [confirmed]

## 6. Probes left for the operator (none run)

| # | Probe | Proves | Cost | Risk |
|---|---|---|---|---|
| P1 | On a runner already on codex-cli ≥0.159.1, using its signed-in home: `codex debug models` (live refresh, **no** `--bundled`) and grep `gpt-6.1-sol` | The account catalog lists it for that `client_version` | No model turn, no quota; one authenticated catalog GET | Rewrites `$CODEX_HOME/models_cache.json` in the runner's real home. Pointless on server2's 0.156.1, because the catalog is client-version-parameterised (CARD-0396) |
| P2 | Same runner: `codex exec --ephemeral -m gpt-6.1-sol -c model_reasoning_effort=low "Reply with OK"` | The backend accepts the slug on this account and CLI (what CARD-0611 ran for `gpt-6-sol`) | One model turn at low effort: base instructions plus a few tokens, a small slice of the 15-160 per 5h band | Spends subscription quota. A 400 "requires a newer version" means the CLI is too old |

## 7. Recommended rollout (for Plan)

1. **Desktop CLI.** Upgrade the global npm install to 0.160.0 (or 0.159.3) and record
   `codex --version`. Re-run the CARD-0796 desktop qualification shape: one cardless Low launch to
   an idle composer. Then run P1 and P2 with operator sanction.
2. **server2 CLI.** Bump `CODEX_VERSION`/`CODEX_SHA512` (hex digests in §2.3) and the
   `verify-codex-image.sh` pins. Ship it in a rollout **after** CARD-0849's, not inside it. Run
   `verify-codex-image.sh` (all verbs), then P2 on server2.
3. **Code change** (§4) behind its tests, then land. Confirm `GET /api/version` SHA after restart.
4. **Canary Plan.** Dispatch one Codex High Plan task. Check that the completion header shows
   `gpt-6.1-sol`, the session's `EffectiveModelId`, the transcript UserPrompt, the
   `--- next stage ---` block and report token, and that the Check digest is interpreted cleanly.
5. **Then Code.** Run one Codex High Code task with a checkpoint table. Check the CP-n per-row
   counts, the commit/push cadence, the land, and quota headroom on the runner.
6. **Optional.** The operator decides on Frontier and on the Medium rung's Terra vs `gpt-6-luna`.

## 8. Board search (duplicate check)

- `card.ps1 search 'Codex 6.1' -Board Antiphon -All`: 0 cards.
- `'gpt-6.1'`: 0 cards.
- `'6.1-sol'`: 0 cards.
- `'ModelLevelAliases'`: CARD-0246, 0099, 0172, 0193, 0167 (Backlog, unrelated: API-key/LLM proxy), 0169 and 0084. None covers 6.1.
- CARD-0903 was created with `-DescriptionFile`. `card.ps1 new` then printed a non-fatal
  `Join-Path … Cannot find drive 'C'` at `scripts/card.ps1:523` while trying the card-file sync
  (`board_not_opted_in`). `card.ps1 get CARD-0903` confirms the card saved.

## Remaining uncertainties

- Desktop CLI version today (last record 0.158.0).
- Account-level availability and backend acceptance (P1/P2).
- 0.159/0.160 TUI compatibility with startup readiness, done detection and transcripts.
- The model's behaviour on Antiphon stage-report contracts.

## Not done, noted

- Fix idea, one line: upgrade both CLIs to ≥0.159.1. Then move `ForCodex(High)` (and the default arm) to `gpt-6.1-sol` with matching `ModelAlias`, `Normalize`, client and doc updates, and leave Frontier, Medium and Low for operator decision.
