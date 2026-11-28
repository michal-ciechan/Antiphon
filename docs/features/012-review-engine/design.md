# Review engine (design)

Status: draft for operator review, 2026-11-22, with the operator's second- and third-round
decisions (D-18 to D-39). No card is bound yet. Phase cards are filed only once card claims exist (section 22, D-36).
Companion spec: [Antiphon Block Format v1](../../specs/block-format/v1.md) (a draft: breaking changes
are allowed until v1 is declared settled). To install and configure an instance, read
[getting-started.md](getting-started.md), which describes what the code does today.

Scope: a server-run, multi-stage code review for pull requests and commit ranges in **any registered
repository**, Antiphon included. Cheap models categorise the change and choose which checks apply.
Several reviewer agents then run in parallel, each with one pre-built context file. Humans take part
through gates, PR comments and an Antiphon review UI, and the engine learns from what they do.

## Operator direction (2026-11-21)

The operator gave this direction over one brainstorming session:

- Cheap categorisation first, then check selection from an ever-growing list of historic issues
  (thousands), then reviewer groups run in parallel.
- Each reviewer gets most of its context in one file. That file holds the diff (whole files when
  small, sections when large) and related files. Every related file records why it was loaded, so
  the lookup can improve.
- Visibility of stage, number of PRs in review and models in use. An editable workflow graph.
- Prompts are checked in under `.antiphon/`. They come from the local repo and common repos, are
  merged with traceability, and are re-merged only when an upstream hash changes.
- Several top-level agents, each in its own container, using the existing phone-home runners.
- The graph has human gates and **may be cyclic**: a wait for new changes, or a manual recheck.
- Rechecking the same PR reuses saved state, checks only what changed, and tracks outstanding issues
  through the PR's conversations.
- The code host and the categorisation API sit behind Antiphon's own interfaces, so providers and
  company caching layers can be swapped.
- The categorisation chain is **Jev → Luna → Haiku → Grok Low**. Jev is for enterprises with API
  pricing; subscription models are for everyone else. One API hides HTTP, one-shot CLI and
  standing-seat differences.
- **The server (antiphon.exe) coordinates the graph.**
- Other repos are in scope, not just Antiphon.
- Reviewers return a fixed, whitespace-tolerant format. That format is documented separately as a
  versioned spec, so it can be reused.
- A learn stage watches what humans ignore. Its proposals go first as PRs to the repo, then to the
  team when the pattern spans repos, then to the org or a language when it spans teams.
- A seeding function sends an agent to any source it can reach and extracts checks from it.
- Once a round has no issues, the categorisation decides whether a human must approve or the bot
  approves. Each later round raises the bar for posting, with fewer nits. After X bot-only rounds
  (default 3) a human gate opens. A human can always send the PR back for another round, with a
  custom steer prompt.
- A comment workflow: a cheap model decides what to do with each human comment. It can act on it,
  resolve it, or start a learning.
- Status checks go back to the PR: picked up, progress, finished.
- Standing seats (with `/clear` between requests) and one-shot CLI calls are both supported behind
  the common API. The standing seat comes first, and HTTP is supported from day 1 for Jev.
- A runner proves it may join by sending a shared secret (the same for every runner).
- No triage-first rollout. Findings post straight to the PR. With no PR, they appear in an Antiphon
  gate.
- Antiphon has queues per user, per team and overall, with random assignment within a team, a
  "not me" hand-back, and reviewer stats that reward useful feedback.
- PRs route to the owning team by CODEOWNERS. Findings go back to the author, who is resolved by AI
  when the PR came from a service account.
- Teams and orgs come from GitHub, mapped into Antiphon entities and reconciled by webhook or daily.
- GitHub uses personal access tokens for v1.
- After approval, the merge is left to the author. Settings allow auto-merge, keeping the branch up
  to date, and AI conflict resolution.
- Blast radius is a categorisation dimension: reach, external contracts, data, rollback at release,
  and reversal after months.
- An Antiphon UI is used to answer bot findings. It posts back to GitHub with attribution. Identity
  comes from a JWT, or from trusted headers set by the enterprise auth proxy.
- Configurable bot handles (for example `@MikeysBot`), including per-stage handles.
- The author may dismiss blockers by default (a high-trust environment). Incident investigations
  check for dismissed findings and count them against the person who dismissed them.
- Enterprise day 1: Jev through the company LiteLLM proxy at `/v1/systemone`, using the existing
  proxy key.

Later the same day the operator settled a second round of points: base drift, cost display, blockers
and trust, gates and code ownership, where settings live, graph sections, the ABF draft status, Jev at
work, events, deployment, promotion, prompt sources, wide-scope dismissals, bypass categories and card
claims. They are D-18 to D-36. On 2026-11-22 the operator answered the open points that followed
(D-37 to D-39, and changes to D-2, D-28, D-33 and D-35). Where one refines a line above (blocker
dismissal, prompt layers), the D-n row wins.

## Summary

1. **One engine, several workflows.** A graph executor stored in Postgres runs workflows defined in
   `.antiphon/review/workflow.yaml`. Node kinds are code, decision, completion, agent, fan-out, join,
   gate and wait. Every cycle passes through a gate or wait or is closed by a loop edge with an
   iteration limit, and every loop edge has one (3.2). The engine runs these workflows: PR review, recheck (the same graph in delta
   mode), comment handling, seeding, branch updates and author resolution. Named `sections` group
   nodes into collapsible blocks in the UI (3.3).
2. **Pluggable edges.** `ICodeHost` covers PRs, threads and statuses; GitHub with a PAT is v1, plus a
   local-git host. `IDecisionModel` covers typed choice, score and yes/no questions with
   probabilities, in Jev's shape. `ICompletionModel` covers text. Each provider runs over a
   transport: HTTP, standing seat, or one-shot CLI. Provider chains are configured per tenant.
3. **Cheap stages.**
   - Categorisation, blast radius, check selection, recheck "still present?", comment triage and
     approval routing are mostly one batched Jev call each.
   - The shared `state` is billed once per call, at $0.042 per million input tokens.
   - When Jev is unsure of one answer, only that question escalates up the chain.
4. **Context packs.** Each reviewer gets one file. The shared prefix holds the PR, diff and related
   files, and is byte-identical across reviewers so prompt caching applies. The suffix is that
   reviewer group's check prompts. Every included file has a ledger row giving its source and
   reason.
5. **Reviewer output** uses the [Antiphon Block Format](../../specs/block-format/v1.md)
   (`review-findings/1`):
   - findings carry quoted anchors, not just line numbers;
   - a `checks` record accounts for every assigned check;
   - a `context` record lists files the reviewer opened and files it found missing.
6. **Rounds.** The posting bar rises each round (severity × confidence), and nits appear only in
   round 1. Bypass categories (a configurable list; `security` and `data-loss` on by default) ignore
   the bar, can't be conceded by the adjudicator alone, and route to the owning team's queue (another
   team, such as a security group, is an override). After 3 bot-only rounds a human gate opens. A clean round goes to bot approval
   or human approval, as the categorisation decides.
7. **Humans.**
   - The Antiphon review UI posts back to the PR with attribution.
   - Queues exist per user, per team and overall. Routing uses code ownership (many-to-many, from
     CODEOWNERS), then random assignment among eligible members other than the author.
   - Findings go to the resolved author.
   - The bot never approves while a blocker is open. A human clears a blocker with a reason; the
     clear counts alone only when that person's **trust score** for the scope is at or above the
     threshold, otherwise a second trusted human confirms (11.6, 11.8).
   - Trust scores are kept per user per scope, recomputed nightly, adjustable by authorised people
     with an audit trail, and gate a fixed list of actions (11.8, 11.9).
   - Stats reward outcomes, not volume.
8. **Checks, prompts, workflow and policy are files; scoring settings are not.**
   - One markdown file per check under `.antiphon/review/checks/`. `# @model:` sections go to the
     model; `# @humans:` and `# @maintainers:` sections never do.
   - Prompts come in layers, configured per instance and resolved company → team → instance → repo.
     The company and team layers are folders in the instance-wide `.antiphon` unless a separate
     source repo is set. Layers are recorded in a lock file. An agent merges real conflicts, and the
     result arrives as a PR (section 10).
   - Leaderboard and impact weights, trust weights and trust thresholds live in the database:
     instance defaults plus team overrides, with revision history (D-26).
9. **Learning.** Signals are dismissed, accepted, ignored and missed. They become proposal PRs at
   the narrowest scope. A pattern in 2 repos of a team is promoted by PR to the team layer, and one
   in 2 teams to the company layer. A dismissal at path, repo or team scope always shows a
   preview to confirm first. The same signals calibrate the fallback models' confidence and tune
   related-file sources.
10. **Cost** is shown in dollars first: actual cost for Jev and other API calls, and an
    API-equivalent estimate for seats. Subscription quota appears only in a tooltip (section 15).
11. **Events and deployment.** Enterprise runs on Kubernetes and takes every GitHub webhook event.
    Home has no public webhook, so it polls, and a manual Refresh is available (8.5, 16.2).
12. **Phasing.** The first slice (P1) delivers Jev HTTP (enterprise) plus standing seats (home), the
    GitHub PAT host with webhooks, polling and Refresh, one reviewer, status checks and a replay
    evaluation. The rest follows over P2–P4 (section 20).

## Decisions

| # | Decision |
|---|---|
| D-1 | The server coordinates every workflow. Agents do the judgement work inside nodes and never move the graph. |
| D-2 | Workflows may be cyclic. A cycle is valid if it passes through a `gate`, a `wait`, or a loop edge with an iteration limit, and each loop edge has an iteration limit. Exceeding a limit always routes to a gate (3.2; OQ-11, accepted 2026-11-22). |
| D-3 | Code hosts sit behind `ICodeHost`. v1 has GitHub with a personal access token for a machine user, plus local git. |
| D-4 | Typed decisions use `IDecisionModel` (Jev shape) and text uses `ICompletionModel`. The default decision chain is Jev → Luna → Haiku → Grok Low. |
| D-5 | Transports are HTTP and standing seat in P1. The one-shot CLI follows later. |
| D-6 | Machine-to-machine text uses ABF (`docs/specs/block-format/v1.md`), and is stored as canonical JSON. ABF details are in D-28. |
| D-7 | A check is a markdown file with `# @audience: Name` section markers. The prompt hash covers `@model` sections only. |
| D-8 | Review config is read from the **base** branch, never from the PR head. |
| D-9 | Findings post straight to the PR. With no PR, they appear in an Antiphon review gate. There is no triage-first mode. |
| D-10 | Status goes back through GitHub commit statuses, `antiphon/review` and `antiphon/approval`, because a PAT cannot create check runs. |
| D-11 | Who may clear a blocker is configurable (`dismiss.blocker`). The default is still the author, but the clear is now **trust-gated** (D-21): it counts alone only when the person is at or above the `clearBlocker` threshold. |
| D-12 | After approval the merge is left to the author by default. Auto-merge and merge are per-repo settings. |
| D-13 | A human can always send a PR to another round, with an optional steer prompt. |
| D-14 | v1 runners are long-lived custom images that phone home. Joining needs a shared secret, then the server issues each runner its own token automatically. |
| D-15 | Teams, orgs and memberships come from GitHub into Antiphon entities, reconciled by webhook or daily. Antiphon overrides win. |
| D-16 | The engine listens to every PR in scope. It acts on its own threads, mentions, issues raised by humans, and repo questions in human threads (after a grace period). |
| D-17 | `@antiphon fix this` (pushing fixes from a comment) is deferred to P4. |
| D-18 | `baseDrift.affectedThreshold` is a policy setting, default 0.35. A probability at or above it that the base changes affect the PR means `affected`. The low default leans towards re-review (4.13). |
| D-19 | When the base moves while `await-fixes` waits, the PR branch is not updated. The drift is recorded, the drift check re-runs on the author's next push, and the branch is updated only after approval or when the author opts in (4.2, 4.13). |
| D-20 | Cost is shown in dollars first. Seat cost is an API-equivalent estimate (transcript tokens × the price table, as `AgentTask.CostUsd` is computed today). Jev and other API calls use their actual cost. Subscription quota % appears only in a tooltip. Under $1 shows as rounded cents ("5¢"); $1 or more as rounded dollars ("$4") (section 15). |
| D-21 | The bot never approves while a blocker is open. A human clears a blocker by marking it "not a blocker" or "accepted risk", with a reason. The clear counts alone only when the person's trust score for the scope is at or above the `clearBlocker` threshold; otherwise a second human at or above it must confirm. With every blocker cleared, the bot may approve when everything else is clean (11.6). |
| D-22 | Trust scores are kept per user per scope (repo, team, area) in the database and recomputed nightly. The person and their team lead can see them. New users start at a neutral score, which is above the thresholds in high-trust instances (an instance setting). Authorised people (team lead or maintainer, configurable) can adjust a score per scope by an offset or a pinned value, with a reason, an optional expiry and an audit trail. Nobody adjusts their own score. Raw and effective values are shown side by side (11.8). |
| D-23 | Trust thresholds gate a fixed list of actions, each with a set behaviour below the threshold: a second person is needed, the action becomes a proposal, or the person can't be assigned. Thresholds are database settings like the weights (11.9). |
| D-24 | The author can't decide their own gate, unless the owning team has one member and there are no other code owners. A per-repo override (`gates.authorMayDecide: always`) allows it (11.10). |
| D-25 | Code ownership is many-to-many. A path can have several owners (teams or users), and a user can belong to several teams (11.4, section 12). |
| D-26 | Scoring settings live in the database: leaderboard and impact weights, trust weights and thresholds. They are instance defaults plus team overrides, edited in a settings UI with revision history, like the dispatch-concurrency settings (CARD-0505). Files hold only config that travels with a repo: checks, prompts, workflow and policy. |
| D-27 | `workflow.yaml` has named `sections` (for example Intake, Review, Decide, Branch maintenance). The UI shows them as collapsible groups that expand on click. A collapsed fan-out shows as "reviewers ×4" and also expands (3.3, section 17). |
| D-28 | ABF is a **draft**: breaking changes are allowed until v1 is declared settled. The legacy scan does not look inside ABF documents. The two report-block parsers (`PipelineHandoff.cs`, `ReviewEvidence.cs`) are replaced by the ABF parser in P0, before the P1 shadow run, so the two readings never diverge in use. |
| D-29 | The enterprise Jev default is `{proxy}/v1/systemone` on LiteLLM. It uses TypeSafe's System One request shape. The live response in GH-58 records the answer fields (OQ-2, OQ-3). |
| D-30 | Enterprise takes **every** GitHub webhook event. The engine acts on a listed set; the rest are stored or ignored. An install without a public webhook (home) polls at a configurable interval (default 2 minutes), and a manual **Refresh** is available in the UI and the API (8.5). |
| D-31 | Enterprise runs on Kubernetes: the Antiphon server container with a Postgres sidecar, and agent runner containers as sidecars, possibly one instance per team (to be confirmed). Home runs on the desktop and server2, moving fully to Docker on server2 (CARD-1744) (16.2). |
| D-32 | Promotion thresholds: the same adjustment in 2 repos of a team goes by PR to the team layer; in 2 teams, to the company layer. Promotion PRs target those layers from day 1. A language or framework facet layer comes later (section 13). |
| D-33 | Prompt sources are configured per instance. v1 models the full layer stack, resolved **company → team → instance → repo** (lowest to highest). The instance layer is the instance-wide `.antiphon` (instance-, graph- and workflow-wide). Team and Org entities carry their layer source: by default a folder inside the instance-wide `.antiphon` (`company/`, `teams/<team>/`), unless a separate source repo is configured. Language and framework facets come later (section 10). This replaces the two-layer v1 of 2026-11-21. |
| D-34 | A dismissal at path, repo or team scope always shows a confirmation step that previews exactly what will happen, with an optional steer prompt. The learn PR and the original PR are linked both ways. Team scope queues a proposal until the 2-repo rule is met (11.7). |
| D-35 | Bypass categories are a configurable list, with `security` and `data-loss` on by default. They post in any round, can't be conceded by the adjudicator alone, and route to `bypassRouteTo`, which defaults to the **owning team** (the PR's own). Another team, such as a security group, is an override (section 5). |
| D-36 | This design's phase cards are not filed until the card-claim feature exists (section 22). |
| D-37 | `base-moved` with `keepUpToDate: true` compares the PR branch's merge-base with the base tip, so a branch that was already behind at review time is caught too. It emits `behind` or `moved` → `base-drift` → `update-branch`. With `keepUpToDate: false` it compares the reviewed base with the base tip (drift only). Rejected: the code host's "behind" status (provider-dependent, an extra API call, and it lags), and always updating (needless pushes, CI runs and approval churn) (4.12; OQ-13, accepted 2026-11-22). |
| D-38 | `base-drift[affected]` re-reviews just the change, in delta mode, and the approval is withdrawn until that re-review passes (4.13; OQ-12, accepted 2026-11-22). |
| D-39 | Trust scores run 0–100. New users start at 60 in a high-trust instance and 40 otherwise. Thresholds default to about 50. `trust.holdDays` defaults to 90 (11.8, 11.9; OQ-14, accepted 2026-11-22). |

## 1. What exists and what is reused

| Need | Exists | Gap |
|---|---|---|
| Direct model calls | Raw Anthropic `/v1/messages` and OpenAI chat clients in `server/Infrastructure/Agents/AgentDraftGenerator.cs:125-224` (agent drafting only). `LlmClientFactory.cs:75-96`: only OpenAI is real; other providers throw `NotImplementedException` (`:127`). | A pipeline decision/completion service with transports, chains and a usage ledger. No TypeSafe client exists. |
| Cheap standing seats | The Diagnose and Distill specialist seats are Claude Code sessions pinned to Low (`SpecialistTaskRunner.cs:44,95-97`, `server/Bundles/diagnose.md`). Pool release, stall detection, transcripts and quota (`/api/subscription-usage`) exist. | A request/response protocol over a seat, with `/clear` policy and a pool. |
| Model routing | Level-to-alias mapping (`ModelLevelAliases.cs:58-125`), alias vocabulary (`server/Application/Core/ModelAlias.cs`), holds (`ModelAvailability.cs`), role × complexity chains (`ComplexityRoutingService`). | Provider chains for decisions and completions per tenant. |
| LiteLLM errors | `tests/Antiphon.Tests/Application/ApiErrorClassifierTests.cs:75` already classifies LiteLLM virtual-key errors. | Reuse it for the Jev HTTP adapter. |
| Code host | `server/Infrastructure/GitHub/GitHubService.cs` (REST: create PR `:62`, comments `:104`, status `:139`, detail `:176`, find by branch `:285`). `GitHubMonitorService.cs:74` polls (legacy engine). | PR by number, threads, resolve (GraphQL), commit statuses, webhooks, provider abstraction, cache layer. |
| Diff | `GitService.cs:202,213` (branch and worktree diffs), `UnifiedDiffParser.cs:8-58` (per file, +/- counts). | Commit-range and PR intake, hunk-plus-enclosing-member slicing, interdiff after force-push. |
| Related files | Path → test class only: `tests/impacted-classes.json`, `tests/derived-impact-index.json` (2,449 edges). | Path → path with reasons, plus a ledger. |
| Historic issues | 122 `docs/investigations/*-why-missed-*.md`, the CARD-1809 R1–R5 checklist (`docs/testing-and-build.md:134-160`), 80 review docs, 164 mutation docs. All prose. | A structured check list and a seeding workflow. |
| Workflow graph | `PipelineDefinition` revisions with `{role, bundleKey, allowedNext[]}`, hashed (`server/Application/Core/PipelineStagesJson.cs:12-28`), selected board > project > built-in (`PipelineResolution.cs:41-54`). The orchestrator agent moves stages. | Node kinds, fan-out/join, gates, waits, a server-run executor. |
| Prompts | `server/Bundles/*.md`, stamped `[bundle:key vHASH]` (`InstructionBundles.cs:11-33`). | Multi-source layering. `.antiphon/` is wholly gitignored (`.gitignore:53-55`), and task footprints skip it (`AgentTaskReplyService.cs:5047`, `DeliverableBundleService.cs:521`). |
| Runners | Phone-home runners, `PhoneHome__Capacity` (`docker-compose.server2-runner.yml:44,64`), a per-id config map (`PhoneHomeRunnerSettings.cs:31,194,388-395`), second-runner recipe (`docs/docker-stack.md:2250-2280`). One `RunnerRepository` per runner (`:21-22`). | Shared-secret enrollment, capability labels, multi-repo mirrors, a custom image contract. |
| Projects | `Project` has GitRepositoryUrl, LocalRepositoryPath, BaseBranch (`server/Domain/Entities/Project.cs:11-70`). | Team, org, membership and identity entities. Only `User` exists. |
| Usage | Per-task tokens and cost (`AgentTask.cs:519-536`, `DelegationUsageRollup.cs:22`). `AgentTask.CostUsd` is an API-equivalent estimate: token counters × the `Delegation:Pricing` rates (`DelegationCost.Estimate`, `PricingVersion` 2). `/api/agent-tasks/summary`, `/api/audit/cost-summary`, quota samples at `GET /api/subscription-usage`. | A ledger for direct calls with a cost basis (actual or estimate); grouping by model; the dollar display rules (D-20). |
| Revisioned settings | `DispatchConcurrencySettings` and `DispatchConcurrencyRevision` (CARD-0505, `server/Domain/Entities/DispatchConcurrencySettings.cs`): a global row plus per-scope overrides, each change a full snapshot with reason and provenance, served at `/api/dispatch-concurrency`. | The same pattern for scoring settings (instance + team), plus a settings UI (D-26). |
| Card ownership | `Card.OwnerSessionId` means the card spawned and owns an agent session (`docs/agent-card-lifecycle.md:113,154`). It is not a claim. | A card claim (section 22). |
| UI | Mantine 8, TanStack Query, SignalR invalidation (`client/src/hooks/useSignalRInvalidation.ts:28`), stage strips, attention feed (`AttentionEndpoints.cs:19`). Only `mermaid` for graphs. | Review pages, a graph editor (`@xyflow/react` + `elkjs`), queues, stats. |

## 2. Architecture

```
                  ┌──────────── antiphon.exe ─────────────────────────────────────────────┐
 webhooks/poll ──►│ ICodeHost (GitHub PAT | local git) ◄── cache layer (SHA-immutable, ETag)
                  │      │                                                                  │
                  │ Workflow executor (Postgres state machine) ── workflow.yaml revisions  │
                  │      │ code nodes: intake, delta, slicing, packs, collate, routing, publish│
                  │      │ decision/completion nodes ──► IDecisionModel / ICompletionModel  │
                  │      │                                 ├─ HTTP: Jev (LiteLLM/TypeSafe/OpenRouter), OpenAI-compatible, Anthropic
                  │      │                                 ├─ standing seats (on runners)
                  │      │                                 └─ one-shot CLI (later)
                  │      │ agent nodes ──► runner seats (phone-home containers)
                  │      │ gate nodes ──► queues + review UI + attention feed
                  │ Check index · prompt layers + lock · finding ledger · context ledger · stats│
                  └─────────────────────────────────────────────────────────────────────────┘
```

## 3. Workflow graph

### 3.1 Node kinds

| Kind | What it does |
|---|---|
| `code` | A C# step registered by name. No model. |
| `decision` | One or more `IDecisionModel` calls with typed questions. |
| `completion` | One `ICompletionModel` call that returns an ABF document. |
| `agent` | An agent session on a runner seat (reviewer, research, conflict resolution, seeding). Read-only unless the node grants write. |
| `fan-out` / `join` | Runs a child node once per item (for example per reviewer group), then waits for all of them. A failure policy applies (`all`, `quorum:n`, `best-effort`). |
| `gate` | A human decision. It opens a queue item and lists its allowed decisions. It times out with a configured action. It may take a bounded number of automatic passes first (`autoRetries`, 3.2). |
| `wait` | An external event: push, recheck command, comment, merge/close, timer. |

### 3.2 Cycle rule

The graph may contain cycles. Saving a revision fails unless **every cycle contains at least one
`gate` or `wait` node, or is closed by a back edge with `maxIterations`**. Each back edge has
`maxIterations`; going over it **always** routes to a gate (D-2). An over-limit target that is not a
gate fails validation. A run is long-lived (one per PR, kept
open until merge or close). Each pass round a loop is a numbered **iteration** with its own node
executions.

- **Bounded loops without a human.** `bot-approve → base-moved → base-drift[affected] → intake` has
  no gate or wait. It is valid because `base-drift[affected]` carries `maxIterations: 10`, which
  opens `human-review` when exceeded. Each pass also needs a new base commit (4.12).
- **Gate automatic passes.** A gate may declare `autoRetries` (0–3, counted per item, such as a
  finding) and an `autoDecision`. The first `autoRetries` arrivals of an item take `autoDecision`
  without opening a queue item; the next arrival opens the gate. A gate breaks a cycle only while
  `autoRetries` is a whole number from 0 to 3. A missing or unbounded value fails validation,
  because the loop could then skip the human forever. The editor shows it on the gate and on the
  edge its automatic decision takes, for example "re-review · auto ×1, then human".
- **Quick fixes.** A cycle error in the workflow editor offers two one-click fixes, staged like any
  other change and validated again:
  - **Insert gate:** route the closing edge through a new gate, with decisions that go back into the
    loop or leave it.
  - **Add iteration limit:** put `maxIterations` on the closing edge, so the configured gate opens
    over the limit.

### 3.3 Definition and revisions

- The workflow lives in `.antiphon/review/workflow.yaml` and is layered like the prompts
  (section 10).
- The resolved definition is stored as a revision with a SHA-256, like `PipelineDefinitionRevision`
  does today. Every run snapshots the revision it started on.
- Edits from the graph editor write a new revision. In repos they arrive as a PR to `workflow.yaml`.
- Every node, and optionally every edge, may carry a `description`: one or two sentences for
  humans, shown on the run page, in the workflow editor and in status and queue explanations. It is
  never sent to models. A missing description is a validation warning, not an error. Editing a
  description creates a new revision like any other edit, but the description is excluded from the
  node's inputs hash, so a description-only edit does not invalidate cached node executions
  (section 3.4).
- **Sections** (D-27) are named groups of nodes, for example Intake, Review, Decide and Branch
  maintenance. The run page and the workflow editor draw each section as a collapsible group. A
  collapsed section is one box with a status roll-up ("Review · 2/4 running"), and a click expands
  it. Edges into or out of a collapsed section attach to its box. A collapsed fan-out shows as one
  node with its child count for the iteration ("reviewers ×4"), and also expands on click.
  - A node is in at most one section. Nodes in no section are drawn ungrouped.
  - Sections are presentation only. Like descriptions, they are excluded from every inputs hash.
  - Whether a section starts collapsed is set per section (`collapsed: true`). After that, each
    viewer's expand and collapse choices are kept per viewer.

```yaml
workflow: pr-review
version: 1
sections:
  intake:  { title: Intake, nodes: [intake, delta, categorise, select, group, packs] }
  review:  { title: Review, nodes: [review, reviewer, collate, verify, publish], collapsed: true }
  decide:  { title: Decide, nodes: [route, await-fixes, record-drift, needs-human-approval, bot-approve, human-approve, human-review] }
  branch:  { title: Branch maintenance, nodes: [base-moved, base-drift, update-branch, await-merge], collapsed: true }
nodes:
  intake:        { kind: code, run: Intake }
  delta:         { kind: code, run: Delta }            # full on first iteration, delta afterwards
  categorise:    { kind: decision, run: Categorise, chain: decisions-default }
  select:        { kind: decision, run: SelectChecks }
  group:         { kind: code, run: GroupChecks }
  packs:         { kind: code, run: BuildPacks }
  review:        { kind: fan-out, over: groups, node: reviewer, policy: all }
  reviewer:      { kind: agent, role: Reviewer, output: review-findings/1, runner: { labels: [review] } }
  collate:       { kind: join, from: review, run: CollateFindings, description: "Gathers every reviewer's findings, collapses duplicates and re-anchors them at the reviewed SHA." }
  verify:        { kind: completion, run: VerifyFindings, chain: completions-cheap }
  publish:       { kind: code, run: Publish }          # PR comments, summary, statuses
  route:         { kind: code, run: RouteAfterRound }  # outputs: bot-round-limit | findings | clean
  await-fixes:   { kind: wait, on: [push, recheck, close, base-moved, update-requested], timeout: 14d, description: "Parks the run, with no seat held, until the author pushes fixes or someone asks for a recheck. Times out after 14 days." }
  record-drift:  { kind: code, run: RecordBaseDrift, description: "Notes that the base moved while fixes are awaited. No model call, no re-review and no branch update; the next push re-runs the drift check." }
  base-drift:    { kind: decision, run: BaseDriftCheck, chain: decisions-default }  # outputs: affected | unaffected; approved PRs only (D-19)
  needs-human-approval: { kind: code, run: NeedsHumanApproval }  # outputs: yes | no
  bot-approve:   { kind: code, run: BotApprove }
  human-approve: { kind: gate, queue: owning-team, decisions: [approve, another-round, close] }
  human-review:  { kind: gate, queue: owning-team, decisions: [approve, another-round, close] }
  base-moved:    { kind: code, run: CheckBaseMoved }   # outputs: unchanged | moved | behind; no model (D-37)
  update-branch: { kind: code, run: UpdateBranch }     # outputs: approved | open; after base-drift or an author opt-in; rebase or merge base in, AI conflict resolution
  await-merge:   { kind: wait, on: [merge, close, push, base-moved], description: "Holds the approved PR until it's merged or closed. Keeps the approval while only the base branch moves." }
edges:
  - intake -> delta -> categorise -> select -> group -> packs -> review -> collate -> verify -> publish -> route
  - route[bot-round-limit] -> human-review
  - route[findings] -> await-fixes
  - route[clean] -> needs-human-approval
  - needs-human-approval[no] -> bot-approve -> base-moved
  - needs-human-approval[yes] -> human-approve
  - human-approve[approve] -> base-moved
  - human-approve[another-round] -> intake      # carries the steer prompt
  - human-review[another-round] -> intake
  - human-review[approve] -> base-moved
  - base-moved[unchanged] -> await-merge        # no update, no model call
  - base-moved[moved|behind] -> base-drift
  - update-branch[approved] -> await-merge
  - update-branch[open] -> await-fixes          # after an author opt-in; its own push is base-only
  - await-fixes[push|recheck] -> intake         # back edge, maxIterations: 20; delta re-runs the drift check if a drift is recorded
  - await-merge[push] -> intake                 # back edge, maxIterations: 20; revokes the approval
  - await-fixes[base-moved] -> record-drift     # D-19: no branch update while fixes are awaited
  - record-drift -> await-fixes
  - await-fixes[update-requested] -> update-branch   # the author opts in (D-19)
  - await-merge[base-moved] -> base-drift
  - base-drift[affected] -> intake              # back edge, maxIterations: 10; carries a rebase steer
  - base-drift[unaffected] -> update-branch     # approval carried forward (patch ids unchanged)
```

### 3.4 Execution

- Node executions are rows (`node_execution`): status, iteration, inputs hash, outputs (canonical
  JSON), model/provider/transport, tokens, cost with its basis (`actual` or `api-equivalent`, D-20),
  timings.
- The executor is a server service with leases, not Hangfire, which runs a single worker
  (`server/Program.cs:1021`).
- Agent nodes dispatch through the existing delegation path onto runner seats.
- Gate and wait nodes park the run with no seat held.
- SignalR events drive the live graph overlay and the status-check updates.
- A node whose inputs hash matches a completed execution from an earlier iteration is reused.
  This is how a recheck skips unchanged work.

#### 3.4.1 Failure policy: which failures stop a review

Not every failed step should stop a review. A step that does work (code, decision, completion,
agent, join) carries `onFailure: continue | stop`, and the default is `stop`. Gates, waits and
fan-outs are never continued past.

- On `continue`, a failed step is shown as `FailedContinued` (failed, continued), with the reason
  first in its summary. The run then goes on along the step's `fallback` output, or else its
  unlabelled edge.
- Some steps fall back to something better than nothing:
  - select reviews every check whose trigger matched when no decision model answers;
  - verify passes every finding through, and the review says they were not verified.
- `Review:Policy:OnFailure:<node>` overrides the workflow per instance, so the operator decides.
  For example, `publish = continue` keeps a review going when GitHub refuses the review post.
  The node window on the graph shows each step's policy.
- The default `pr-review` continues past categorise, select, verify, base-drift (falling back to
  `affected`, the safe answer) and bot-approve. It stops at intake, delta, the reviewers, collate,
  publish and route.
- Commit statuses are a courtesy to the PR. A status the code host refuses is noted on the step
  that posted it (`status antiphon/review not posted: ...`), and the step still succeeds. Set
  `Review:Policy:StatusesRequired = true` to make a refused status fail its step instead.
- A run that fails sets `antiphon/review` to `error` with the failed step and reason, best effort,
  so the PR does not stay pending.

### 3.5 Other workflows on the same engine

| Workflow | Trigger | Shape |
|---|---|---|
| `pr-review` | PR opened or marked ready, repo in scope, manual | 3.3 above |
| recheck | push, `/recheck`, UI button, `await-fixes`, `base-drift[affected]` | the same graph; `delta` limits work to the change since the last reviewed SHA (4.2) |
| `comment` | a human response: PR comment, thread reply, review submission, gate decision | section 6 |
| `seed` | manual or scheduled per source | section 14 |
| `update-branch` | the base moved under an approved PR (`base-moved` after the approval, or `await-merge`) and `base-drift` says it does not affect the PR; the author opts in while fixes are awaited (D-19); conflicts | sections 4.12, 4.13 |
| `resolve-author` | intake, when the PR author is a service account | section 11.5 |

### 3.6 Artifacts

- **Every node declares named, typed inputs and outputs.** An input is either an artifact from an
  earlier node or configuration and context: a policy key, a questions file, CODEOWNERS, CI status,
  a steer prompt. Each input records what it is used to decide.
- **Artifacts are stored and validated.** Each artifact kind has a definition: kind, format and
  version, fields, producer and readers, validation rules, storage, retention, and the file or
  setting where it is defined. Every instance is validated when it is produced. A failed rule fails
  the node and opens an attention item; a warning is shown on the run page.
- **The UI explains itself.** Clicking a node shows its inputs and outputs; clicking an artifact
  shows its definition, this run's validation results, an example and where to edit it.

| Artifact | Kind | Produced by | Read by | Validated by | Defined / edited in |
|---|---|---|---|---|---|
| PR snapshot | JSON record | `intake` | delta, categorise, packs, approval, publish | SHAs pinned in the mirror; config read at base (D-8) | `antiphon.areas.json`, `policy.yaml` intake tags, CODEOWNERS |
| Change delta | JSON record | `delta` | categorise, select, packs, collate | every open finding re-anchored or outdated, with a `still_present` answer | `.antiphon/review/questions/recheck.yaml` |
| Categorisation | decision | `categorise` | select, group, packs, approval, publish, gates | every question answered or refused; served-by recorded; escalations below threshold | `.antiphon/review/questions/categorise.yaml`; provider chain settings |
| Check selection | decision | `select` | group, approval | every candidate recorded with its probability; ≤ 128 questions per call | check `# @model: Selector` sections and triggers; `policy.yaml` `rounds.post` |
| Reviewer groups | JSON record | `group` | packs, review | each check in exactly one group; bypass groups alone; suffix budget | `.antiphon/review/groups/` |
| Context pack (per group) | file | `packs` | reviewer, verify | shared prefix byte-identical across packs; token budget; every check's `@model` sections; a ledger row per file; nothing from the head's `.antiphon/` | `related.yaml`, check `relatedFiles`, `policy.yaml` `packs` |
| Review findings (per group) | ABF `review-findings/1` | `reviewer` | collate, approval | parses (one repair call); every assigned check accounted for; anchors resolve | `docs/specs/block-format/v1.md` §7.1; `prompts/reviewer.md` |
| Collated findings | JSON record | `collate` | verify | every reviewer reported; no duplicates above the similarity threshold | `policy.yaml` `collate` |
| Verified findings | ABF | `verify` | publish, route | every finding kept or disproved; bypass findings not dropped alone | `prompts/verify.md`; provider chain settings |
| Posted review | code-host object | `publish` | route, human-review | only findings above the bar inline; every posted finding linked to its thread | `policy.yaml` `rounds.post`, `handles`; `prompts/summary.md` |
| Round route / human-approval verdict | JSON record | `route` / `needs-human-approval` | await-fixes, needs-human-approval, gates | exactly one output; every fired rule cites its evidence | `policy.yaml` `rounds`, `approval` |
| Gate decision | event | `human-approve`, `human-review` | intake (with the steer), base-moved | decider authorised, at or above the gate's trust threshold (11.9), and not the author unless 11.10 allows it; decision allowed by the gate; a steer from below `acceptSteer` held for review | `workflow.yaml` gate nodes; `policy.yaml` `gates` |
| Wait event | event | `await-fixes`, `await-merge` | intake, record-drift, base-drift, update-branch | back edge under `maxIterations` | `workflow.yaml` `await-fixes`, `await-merge` |
| Drift record | JSON record | `record-drift` | delta | base range and code-only overlap stored; no model call; branch untouched | `workflow.yaml` `record-drift` |
| Base-drift assessment | decision | `base-drift`, `delta` (recorded drift) | intake (with the rebase steer), packs, update-branch | code-only prefilter before any model call; verdict against `baseDrift.affectedThreshold`; an affected verdict lists the overlapping commits and files | `workflow.yaml` `base-drift`; `policy.yaml` `baseDrift`; `.antiphon/review/questions/base-drift.yaml` |
| Approval record | code-host object | `bot-approve`, gates | base-moved, update-branch, await-merge | head equals the reviewed SHA; records the base it was reviewed against; no open blocker for a bot approval (D-21) | `policy.yaml` `approval.autoApproveRequires` |
| Base-tip check | JSON record | `base-moved` | base-drift, await-merge | base ref fetched after the approval; exactly one output; `unchanged` only when the compared base (the branch's merge-base with `keepUpToDate`, else the reviewed base) equals the tip; `behind` only with `keepUpToDate` | `workflow.yaml` `base-moved`; `policy.yaml` `afterApproval.keepUpToDate` |
| Branch update | event | `update-branch` | await-merge, await-fixes | approval carried only when `base-drift` said unaffected and patch ids are unchanged; `not-needed` only when `keepUpToDate` is off and the head merges cleanly; on an open PR only after the author opted in | `policy.yaml` `afterApproval` |

## 4. Review stages

### 4.1 Intake

- Input is a PR (`ICodeHost`) or a commit pair (local-git host). Base and head SHAs are pinned.
- The repo mirror is fetched, along with the PR refs. Code comes from git; only conversation comes
  from the API (section 8).
- Policy, checks and workflow are read **at the base SHA** (D-8).
- Code-only tags: languages, `antiphon.areas.json` areas, path-glob classes, size bucket,
  generated-file detection, and every code owner (team or user) of the changed paths (11.4).
- The base tip is pinned too. When a drift was recorded while fixes were awaited, this is the base
  the drift check in `delta` uses (D-19).

### 4.2 Delta (recheck)

1. **Find the change.** The diff runs from the last reviewed head to the new head.
   - After a force-push or rebase the old head may no longer be reachable. Then compare the two
     trees and match commits by patch id (an interdiff).
   - A base-only update leaves the PR's own patch ids unchanged, and is marked as such. Whether
     it needs a new review is decided by `base-drift` (4.13), not here.
   - **Recorded drift (D-19).** When `record-drift` logged base moves while fixes were awaited, delta
     re-runs the drift check (4.13 steps 1 and 2) for the range from the reviewed base to the base
     tip pinned at intake. An affected verdict adds the rebase steer to every pack and adds the
     overlapping files to the scope. An unaffected verdict adds nothing. Either way the branch is not
     updated, and the record is cleared.
2. **Re-anchor open findings.**
   - Each open finding moves through the diff to its new location.
   - Its anchor (quoted code) is searched for at the new head.
   - A finding whose anchored region was deleted becomes `outdated`.
3. **Ask Jev, once per open finding:** `still_present` (yes/no), with the new code around the
   anchor plus the thread replies as state. If the author has replied, also ask `rebuttal_strength`
   (score); the comment workflow takes those findings.
4. **Narrow the scope.** Only changed files, and files whose related context changed, go through
   categorise, select and review again. Cached decisions and pack chunks are reused by hash.
5. **Brief reviewers.** Their packs list the outstanding findings, so they don't re-report them and
   can confirm fixes.

### 4.3 Categorisation and blast radius

One Jev call per state, with all questions batched. A large PR is split by area. Example (the
proxy hostname comes from config):

```json
{
  "model": "jev-latest",
  "state": "<PR title + description + changed files with stats + diff summary + code-only tags>",
  "questions": {
    "change_kind": { "type": "choice", "instructions": "What kind of change is this?",
      "choices": [{"value":"feature"},{"value":"bugfix"},{"value":"refactor"},{"value":"test"},
                  {"value":"docs"},{"value":"infra"},{"value":"dependency"}] },
    "external_contract": { "type": "noul", "instructions": "Does this change an API, message schema, public type, CLI flag or config key that other teams, systems or users rely on?" },
    "persistent_data": { "type": "noul", "instructions": "Does this change database schema, migrations, stored data, or a persisted file or message format?" },
    "reach": { "type": "score", "instructions": "How many components, callers or services does this affect?",
      "levels": [{"label":"One place"},{"label":"A few"},{"label":"A module"},{"label":"Many modules"},{"label":"System-wide"}] },
    "rollback_at_release": { "type": "score", "instructions": "If this breaks on release, how hard is it to roll back?",
      "levels": [{"label":"Trivial"},{"label":"Easy"},{"label":"Moderate"},{"label":"Hard"},{"label":"Very hard"}] },
    "reversal_after_months": { "type": "score", "instructions": "After months in production, how hard is it to undo this design?",
      "levels": [{"label":"Trivial"},{"label":"Easy"},{"label":"Moderate"},{"label":"Hard"},{"label":"One-way door"}] },
    "exposure": { "type": "score", "instructions": "How user-facing and high-traffic is the changed behaviour?",
      "levels": [{"label":"Internal only"},{"label":"Low"},{"label":"Medium"},{"label":"High"},{"label":"Critical path"}] }
  }
}
```

There are further yes/no questions per risk area: concurrency, persistence, auth/security, session
runtime, infra, performance hot path, and whatever the repo, team or org layers add.

| Blast-radius dimension | Code-only evidence placed in the state |
|---|---|
| Reach | Fan-in of changed symbols; referencing projects; consumers in other repos (code search) |
| External contract | Paths matching API, proto, schema and contract globs; public type changes; config keys |
| Data | Migration files, ORM model changes, storage clients, serialisation formats |
| Rollback at release | Feature flag present; stateless change; additive vs destructive migration; Down step present |
| Reversal after months | New persisted formats or identifiers; newly published contracts |
| Exposure | Routes, UI paths |

The outputs feed:
- the needs-human-approval decision (4.10)
- reviewer tier and extra groups (for example migration safety, API compatibility)
- the summary comment and status description
- notifications to consuming teams. A contract → consumer register starts from org code search and
  can later read a service catalogue.

### 4.4 Check selection

1. **Code prefilter.** Each check's triggers (globs, languages, tags, symbol patterns, risk areas) are
   matched, plus embedding similarity between the check text and the diff summary (pgvector on the
   existing Postgres). That leaves about 100–200 candidates.
2. **Jev decides applicability.** One yes/no `applies` question per candidate check, with the
   check's `@model: Selector` text as instructions, against one shared state. The LiteLLM route
   allows at most 128 questions per call, so the candidates are split across calls.
3. **Rounds.** A round's posting bar also drops checks whose severity can't clear it (section 5).
4. **Record everything.** Selected and rejected checks are both stored, with their probability.

### 4.5 Grouping

Selected checks are bin-packed into reviewer groups by category affinity and the token budget for
the group suffix. The group's tier comes from its most severe check and the blast radius. Bypass
categories always get their own group. The defaults come from `.antiphon/review/groups/`.

### 4.6 Context packs

One file per reviewer, written to the run's artifact store and staged to the runner:

```
[shared prefix: byte-identical across a run's reviewers → prompt cache hits]
  PR metadata, intent, categorisation, blast radius, round number + steer prompt
  outstanding findings (recheck)
  the diff: numbered lines; whole files under the size threshold, otherwise hunks widened to the
           enclosing member (Roslyn for C#, tree-sitter for TS/others) + an outline of the rest
  related files (each with its ledger reason) and tool output (analyzer warnings, test impact)
[group suffix]
  the selected checks' @model sections (Prompt, Examples), the group's instructions,
  the output contract: review-findings/1 (ABF)
```

Related-file sources each write a ledger row
`(run, iteration, file, source, reason, score, tokens, included?)`. The sources:
- the impact index
- callers of changed symbols
- git co-change history
- DI and registration files
- `relatedFiles` hints declared on checks
- the repo's `.antiphon/review/related.yaml` rules
- model suggestions

Files are included in score order until the budget is spent.

### 4.7 Reviewers

- Read-only agent sessions on runner seats, with a checkout at the head SHA from the repo mirror.
- Every file a reviewer opens that wasn't in the pack is logged. Together with the `context` record
  of its output, that is the main signal for improving related-file lookup.
- Output is `review-findings/1` (ABF). A parse failure gets one cheap repair call; after that the
  reviewer is retried.

### 4.8 Collate, dedupe, verify

- Duplicates across groups, and against open findings and human comments, are collapsed.
- A cheaper verifier tries to disprove each finding; one it disproves is dropped and recorded.
- Anchors are re-resolved at the reviewed SHA; a finding whose anchor can't be found is dropped.
- **Optional pattern: gate disputed findings.** This pattern is not in the default workflow. A repo
  that would rather re-review a finding the verifier disputes than drop it adds a `disputed` output
  to `verify` and a gate:

  ```yaml
  disputed-findings: { kind: gate, queue: owning-team, decisions: [re-review, drop, keep], autoRetries: 1, autoDecision: re-review, timeout: 2wh, onTimeout: drop }
  edges:
    - verify[disputed] -> disputed-findings
    - disputed-findings[re-review] -> packs       # back edge, maxIterations: 2
    - disputed-findings[drop|keep] -> publish
  ```

  - **Re-review:** reviews only the disputed findings, with the verifier's objection added to the
    pack.
  - **Drop:** removes the finding.
  - **Keep:** posts the finding as is.

  The first dispute of a finding re-reviews automatically. A second dispute of the same finding
  opens the gate for the owning team. A plain `verify[disputed] -> packs` edge fails the cycle rule
  (3.2); the editor's quick fixes stage either this gate or `maxIterations` on that edge.

### 4.9 Posting

- Findings that clear the round's bar (section 5) are posted as **one PR review**: inline comments
  plus one summary comment that is edited in place each iteration.
- Below-bar findings are stored but not posted. Round-1 nits are listed in the summary comment only.
- With no PR (a commit range), findings go to an Antiphon review gate instead (D-9).

### 4.10 Needs human approval

A clean round (nothing above the bar and no open findings) goes to `needs-human-approval`, which
answers one question: must a human approve? `yes` goes to the `human-approve` gate, `no` to
`bot-approve`.

**The answer is yes if any of these is true.** A yes is sticky for the rest of the PR.
- the change touches a risk category or blast-radius threshold, configurable (for example
  `external_contract` or `reversal_after_months` ≥ 4)
- a selected check has `requiresHuman`
- the PR is large
- the author is new to the repo
- two or more disputes were conceded by the bot
- a high-severity check was marked `unverified`

**The bot approves only when all of these hold:**
- no open blocker. This rule is not configurable (D-21). A blocker is closed only when it is fixed,
  or cleared by a human under 11.6. A clear still waiting for a second person keeps it open.
- no open findings
- every check given to a reviewer is accounted for
- head = reviewed SHA
- CI is green
- no human threads are open

The approval records the SHA it covers and the base it was reviewed against. Either approval then
goes to `base-moved` (4.12). The UI and summary list which rules fired.

### 4.11 Status checks

These are commit statuses on the head SHA, linking to the run page in Antiphon:

| Context | When | Example description |
|---|---|---|
| `antiphon/review` | picked up | pending: "Picked up by Antiphon (queued, 2 ahead)" |
| `antiphon/review` | progress (throttled) | pending: "Categorised · 14 checks · 2/4 reviewers done" |
| `antiphon/review` | waiting | pending: "Waiting on human approval (team-x)" |
| `antiphon/review` | done | success or failure: "3 findings (1 major)" |
| `antiphon/approval` | approval | success only after bot or human approval; this is the one branch protection requires |

`ICodeHost` exposes `SupportsCheckRuns`. A later GitHub App adapter can switch to check runs, which
carry a markdown summary and annotations.

### 4.12 After approval

- **Merge setting per repo:** `afterApproval: leave-to-author (default) | enable-auto-merge | merge`,
  with a merge method. Built as a gate and a step:
  - **`merge-gate`** follows every approval. It is a gate of kind `MergeGate` and is on by default:
    a person confirms the merge in Antiphon (Merge or Don't merge), or merges on the code host, which
    ends the run wherever it stands. Opening it leaves `antiphon/approval` as it is, and deciding it
    records no new approval.
  - **Switching it off:** the workflow marks it `optional: true`, and
    `Review:Policy:GatesOff:merge-gate` lists the repos (or `*`) where it passes without a person, so
    Antiphon merges by itself. Only optional gates can be switched off. With no PR, or a code host
    that cannot merge, the gate passes and the merge is left to the author.
  - **After it passes:** `base-moved`, `base-drift` and `update-branch` bring the branch up to date,
    so the drift check is fresh at merge time.
  - **`merge`** then merges through the code host with `Review:Policy:MergeMethod` (default `rebase`,
    so no merge commit). The merge is pinned to the approved head, and GitHub refuses with 409 if the
    head moved.
  - **A refused merge** fails the step and continues (`onFailure: continue`), so the PR waits in
    `await-merge` for a person. The code host's merged event closes the run.
- **Checking the base after approval:** every approval (`bot-approve`, or `approve` at
  `human-approve` or `human-review`) goes to `base-moved`. This code step makes no model call. It
  fetches the base ref and compares a base with the current base tip (D-37):
  - **`keepUpToDate: true`:** it compares the PR branch's **merge-base** with the tip, so a branch
    that was already behind at review time is caught too. `moved` means the base moved since the
    reviewed base (pinned at intake for the approved iteration). `behind` means it didn't, but the
    branch lacks base commits.
  - **`keepUpToDate: false`:** it compares the **reviewed base** with the tip (drift only), and
    emits `unchanged` or `moved`.

  Then:
  - `unchanged` → `await-merge`, with no update and no model call.
  - `moved` or `behind` → `base-drift` (4.13), which decides whether the missing base commits
    affect the PR, and then `update-branch`.

  A human gate can stay open for days, so a move is common on the human path. On the bot path the
  check catches a base that moved during the round. Two alternatives were rejected: the code host's
  "behind" status (it depends on the provider, costs an API call and lags) and always updating
  (needless pushes, CI runs and approval churn).
- **Waiting for the merge:** an approved PR parks on `await-merge` (no seat held) until it is merged
  or closed. A push revokes the approval and starts a new iteration; a base move goes to `base-drift`
  (4.13).
- **Keeping the branch up to date:** `update-branch` runs when `base-drift` says a base move does
  not affect an approved PR, whether the move was found by `base-moved` or during `await-merge`. It
  brings the branch up to date by merge or rebase, per repo.
- **While fixes are awaited (D-19):** a base move is only recorded (`record-drift`). The branch is not
  updated until the PR is approved, unless the author opts in with `@antiphon update-branch` or the
  run page's **Update branch** button. That runs `update-branch` from `await-fixes` and returns
  there (`update-branch[open]`). Its push is base-only, so it starts no new round, and the recorded
  drift stays for the next author push.
- **`keepUpToDate: false`:** `update-branch` does not merge the base in. It checks that the head still
  merges cleanly into the new base, records `not-needed` and goes on to `await-merge` without
  pushing, so the approval stands. Only conflicts make it update the branch. This stays inside
  `update-branch` rather than adding a `base-drift → await-merge` edge, so the decision node never
  reads merge policy or runs a merge check.
- **Conflicts:** an agent resolves them on a runner and pushes. Only the conflict hunks are rechecked.
- **Carrying the approval forward:** the approval moves to the new head without a new round only if
  `base-drift` said unaffected and the PR's own patch ids are unchanged. Any other push revokes it.

### 4.13 Base drift

When the base branch moves under an **approved** PR, `await-merge` sends a `base-moved` event to
`base-drift`, a cheap decision node. The `base-moved` check after an approval (4.12) enters the same
node through `base-moved[moved|behind]`. The range checked runs from the compared base (the
branch's merge-base with `keepUpToDate`, else the reviewed base, D-37) to the base tip.
When the base moves under an **open** PR that waits in `await-fixes`, the move is only recorded
(`record-drift`), and `delta` runs steps 1 and 2 on the author's next push (D-19, 4.2):

1. **Code-only prefilter.** Do the base commits in that range touch the PR's changed files,
   its related files (impact index, callers of changed symbols), or the contract and schema globs?
   No overlap means `unaffected`, with no model call.
2. **One Jev call on overlap.** A single yes/no question, "Could these base changes affect this PR?",
   with the base commit summaries and the overlapping files as state. The verdict is `affected` when
   the probability of yes is at or above `baseDrift.affectedThreshold` (policy, default 0.35, D-18).
   The default is deliberately low: a false re-review is cheaper than a missed semantic conflict. A
   low-confidence answer escalates up the chain like any other decision.
3. **Route.**
   - `affected` → back to intake for a re-review of just the change, in delta mode (back edge,
     maxIterations 10; the recheck row in 3.5). The approval is withdrawn until that re-review
     passes (D-38).
     Every pack gets an automatic steer: "Base moved `<old>..<new>`: review as if rebased onto the
     new base; check interactions with these commits/files: …".
   - `unaffected` → `update-branch` (4.12), and the approval carries forward when the PR's patch ids
     are unchanged.

The assessment (base range, commits, overlap, prefilter result, probability, verdict, steer) is
stored as an artifact (3.6) and shown on the run page. The threshold is a policy setting (D-18).
Prefilter sources, contract globs and the steer template are per-repo parameters of the node.

## 5. Rounds policy

`.antiphon/review/policy.yaml` is layered like everything else (section 10):

```yaml
handles: ["@antiphon"]                  # e.g. add "@MikeysBot"
stageHandles: { security: "@antiphon-security", recheck: "@antiphon-recheck" }
listen:
  prs: in-scope                         # configured repo, owning team is a CODEOWNER, or a configured CODEOWNERS handle
  actOn: [own-threads, mentions, human-raised-issues, repo-questions]
  repoQuestionGraceWorkingMinutes: 30
rounds:
  humanGateAfterBotOnlyRounds: 3
  post:
    - { round: 1,  minSeverity: nit,   minConfidence: 0.5,  maxNits: 5, nitsInline: false }
    - { round: 2,  minSeverity: minor, minConfidence: 0.65 }
    - { round: 3+, minSeverity: major, minConfidence: 0.8 }
  bypassCategories: [security, data-loss]   # D-35: on by default; posted in any round; the adjudicator cannot concede them alone
  bypassRouteTo: owning-team                # default: the PR's owning team; or name another team, e.g. a security group
severities: [nit, minor, major, blocker]
dismiss:
  blocker: author                       # who may clear a blocker: author | other-member | maintainer; trust-gated (11.6)
gates:
  authorMayDecide: sole-owner           # sole-owner (default: only when the owning team has one member and no other code owners) | always (11.10)
baseDrift:
  affectedThreshold: 0.35               # D-18; leans towards re-review
approval:
  sticky: true
  humanRequiredWhen:
    categories: [migration, auth, security, public-api, infra, session-runtime]
    blastRadius: { external_contract: ">=0.6", reversal_after_months: ">=4" }
    checkFlag: requiresHuman
    size: large
    author: first-time
    concededDisputes: ">=2"
    unverifiedHighSeverity: true
  autoApproveRequires: [no-open-findings, all-checks-accounted, head-is-reviewed, ci-green, no-open-human-threads]
afterApproval: { mode: leave-to-author, keepUpToDate: true, updateMethod: rebase, resolveConflicts: ai }
```

- **Rounds and the counter.** The round number only goes up. The bot-only counter resets on any human
  involvement:
  - a review submission
  - a comment in a thread
  - resolving or dismissing a finding
  - a gate decision

  Author pushes and bot or agent activity don't reset it. People are told apart from bots by
  identity flags, `[bot]` logins and Antiphon delegate commit trailers.
- **Steer prompts.** `another-round` takes an optional steer prompt. It is stored on the round and
  goes into the shared prefix of every pack ("Human steer from <name>: …"). A steer from a person
  below the `acceptSteer` threshold waits for review first (11.9). Steers that keep recurring become
  candidate checks (section 13).
- **Bypass categories (D-35).** `bypassCategories` is a per-repo list, layered like the rest of the
  policy, with `security` and `data-loss` on by default. A finding in a bypass category, by its
  check's `flags` or the finding's own `flags`:
  - posts in any round, whatever the round's bar;
  - gets its own reviewer group (4.5);
  - can't be conceded by the adjudicator alone (section 6); a concession opens a gate instead;
  - opens a review item in the queue of the `bypassRouteTo` team (11.3). By default that is the
    owning team, the same team as the PR. Naming another team, such as a security group, is an
    override, and that team's item is added next to the owning team's.
- **What is not in this file.** Scoring weights, trust weights and trust thresholds are database
  settings (D-26, section 15), because they belong to people and teams, not to a repo.

## 6. Comment workflow

```
human response (PR comment / thread reply / review submission / Antiphon gate or review UI)
  → normalise + debounce (batch one submitted review)
  → trust: who (author / maintainer / member / outside contributor / bot); addressed to us?
  → triage: one Jev call. One yes/no question per intent (a comment can carry several),
            plus `scope` (choice) and `reason_category` (choice)
  → route each intent → act → reply in the thread → update the finding record
```

| Intent | Action |
|---|---|
| Dismiss / by design / won't fix | Resolve the thread, store the reason and scope, emit a learn signal. On a blocker this is a clear ("not a blocker" or accepted risk) and follows the trust gate (11.6). At path, repo or team scope the confirmation step comes first (11.7) |
| Dispute | Adjudicator (completion): concede (resolve + learn signal), hold (reply with reasoning), or escalate to a gate. Bypass categories can't be conceded by the adjudicator alone |
| Claims fixed | Recheck that one finding now |
| Will fix | Mark it pending; the next push checks it |
| Question about a finding | Answer in the thread from the pack context (completion) |
| Question about the repo (including human-to-human threads) | Read-only research agent; the answer is posted and marked as a bot answer. Unless Antiphon is mentioned, it waits out the grace period and skips if a human answers first |
| Request recheck | Start the recheck workflow |
| New issue raised by a human | Add it to the finding list as human-raised, tracked by rechecks; emit a "missed" learn signal |
| Defer | Create a follow-up issue or card; resolve with a link |
| Instruction to the bot ("don't flag generated files") | Learn proposal PR to `.antiphon/review/` |
| Approval / LGTM | Human involvement; satisfies a gate if the commenter is authorised |
| Request fix | Deferred to P4 (D-17) |
| Noise | Nothing |

**Guardrails**
- **Comment text is untrusted data.** Triage only picks from the intent list, and each intent maps
  to an allowlisted action.
- **High-stakes actions** need an authorised commenter and high confidence: clearing a blocker,
  dismissing at path, repo or team scope, or any write to the branch. Otherwise Antiphon asks for
  confirmation or routes the item to a gate. A wide-scope dismissal always gets the confirmation,
  with its preview (11.7): "Resolve F3 as by-design for this repo? This opens a PR on `<repo>` that
  adds: … Reply `/antiphon yes`, or `/antiphon yes steer: <text>` to have the rule drafted with your
  steer."
- **Trust gates.** A clear, a wide-scope dismissal, a gate decision or an approval made in a comment
  follows the same thresholds as in the review UI (11.9).
- **Locking.** Actions take a lock on the finding record, and queue while a review iteration is in
  flight.
- **Antiphon first, then the code host.** A decision made in Antiphon on a finding that has a
  code-host thread posts the reply to the thread, then resolves the thread, then closes the finding
  locally. If posting fails, the finding is held `pending-sync` and retried. It is never closed in
  Antiphon only.

**Reason categories:** false positive, by design, out of scope, accepted risk, fix later, duplicate,
style preference.

**Scopes:** this instance (this one finding), this path, this repo, suggest to the team. The last
three are wide scopes (11.7).

## 7. Model access

### 7.1 Interfaces

```csharp
public interface IDecisionModel
{
    Task<DecisionResult> DecideAsync(DecisionRequest request, DecisionOptions options, CancellationToken ct);
}
// DecisionRequest: State (text or structured), Questions: name -> Noul | Choice(values[]) | Score(levels[])
// DecisionResult: Answers: name -> { Kind, Value, Confidence, Probabilities, Refused, ServedBy },
//                 Usage, CostUsd, ServedBy { Provider, Transport, ResolvedModel }

public interface ICompletionModel
{
    Task<CompletionResult> CompleteAsync(CompletionRequest request, CompletionOptions options, CancellationToken ct);
}
// CompletionRequest: Instructions, Input, ExpectedDocument (ABF type/version) | free text
```

Callers name a **chain** (for example `decisions-default`), never a provider. A chain is an ordered
list of provider entries. Each entry is a provider plus a transport.

### 7.2 Transports

| Transport | Phase | Notes |
|---|---|---|
| HTTP | P1 | Jev (all dialects below), OpenAI-compatible chat, the Anthropic Messages API. Company gateways via `baseUrl`. |
| Standing seat | P1 | One or more warm agent sessions per runner container. Each takes one ABF request (`decision-request/1` or a completion request) and returns ABF. `/clear` runs after each request, every N requests, or when context passes a threshold. A seat pool gives concurrency. Reuses specialist-seat stall detection, transcripts and quota. |
| One-shot CLI | P4 | `claude -p --output-format json`, `codex exec`, Grok equivalent. `claude -p` is implemented for reviewers, completions and decisions (7.7). |

Decisions over a chat model (seat or HTTP) ask for `decision-response/1` with a probability per
option. Those probabilities are **not calibrated**; see 7.5.

### 7.3 Jev dialects

Each Jev dialect sends `{ model, state, questions }`. Each key in `questions` names a question with
one of these `type` values.

- `noul`: `instructions`
- `choice`: `instructions` and a `criteria` object mapping option values to descriptions. Use JSON
  `null` for an option whose name is enough.
- A `score` question sends `instructions` and a `criteria` array of level descriptions, lowest
  first. Each entry is a string. A level with no description uses its label.

The response keys `answers` by question name. Choice answers carry `choice`, `confidence` and
`probabilities`. A `noul` answer carries the probability of yes in `noul`. A numeric `score` selects
the zero-based level in the request. For a fractional score, the adapter uses the nearest level and
rounds ties up. It maps the selected index and score probability keys to the caller's original
level labels. The call record includes the resolved `model`, `usage.input_tokens` and
`usage.output_tokens`. See
[GH-58](https://github.com/michal-ciechan/Antiphon/issues/58) and the
[System One OpenAPI](https://docs.typesafe.ai/api/system-one.md).

| Dialect | Endpoint | `model` | Errors | Cost |
|---|---|---|---|---|
| `litellm-systemone` (enterprise default, D-29) | `{proxy}/v1/systemone` on the latest LiteLLM; request shape identical to TypeSafe's | proxy alias, e.g. `jev-latest` | LiteLLM `{"error":{...}}` envelope; pre-validation 400s; a missing answer key = refusal | `x-litellm-response-cost` header |
| `litellm-passthrough` | `{proxy}/typesafe/v1/systemone` | upstream model | TypeSafe native | price table × `usage.input_tokens` |
| `typesafe-native` | `https://api.typesafe.ai/v1/systemone` | `jev-1.14.0` (pin) or alias | 400/401/413/415/422; retry 429/529 | price table ($0.042 per million input tokens, output free) |
| `openrouter-systemone` | `https://openrouter.ai/api/v1/systemone` | native | native | price table, or OpenRouter usage cost |

- **Cost basis.** Every dialect's cost is an **actual** cost, as the table gives it, and is recorded
  as such in `llm_call` (D-20).
- **Provider errors.** An HTTP 400 records one rejected call and stops the chain as `Rejected`
  without fallback, including when the diagnostic body stalls. Caller cancellation still
  propagates. Error-stream reads get up to two seconds, bounded by the remaining provider and
  HTTP client deadlines. The client reads at most 16 KiB plus one byte and accepts at most 12
  levels of JSON nesting, 256 value nodes and 64 items per array. It publishes fixed validation
  reasons from recognized error envelopes or TypeSafe `detail`, with recognized schema paths
  where available. Question names in a path become zero-based positions in the request. The
  projected diagnostic is limited to 300 characters. Stalled, unreadable, malformed, undecodable
  or over-limit bodies use the fixed message `Provider diagnostic omitted.`, as do bodies
  without a recognized reason.
- **Auth.** Every dialect sends `Authorization: Bearer <key>`. Enterprise uses the existing proxy
  virtual key, which is the same key used for its OpenAI-compatible endpoints, with no separate Jev
  key. Keys go through Antiphon credential custody (`docs/agent-credentials.md`).
- **Never `/v1/decisions`.** That route takes the OpenAI Decisions format, and the enterprise notice
  puts the older Luna `/v1/decisions` endpoint into decommissioning from 16 November.
- **Limits on the LiteLLM route** (`litellm/proxy/decisions_endpoints/endpoints.py`, v1.104.2+):
  1–128 questions, 1–255 choices, 1–10 levels, 64k tokens per request and 32k per question unless
  `TypeSafe-Danger-Zone-Long-Context: true` is sent. The adapter splits work rather than using long
  context, which TypeSafe documents as slower and less accurate.
- **Model version.** `jev-latest` moves. Every answer records the resolved model the response
  reports (spend is logged under it, for example `typesafe/jev-1.13.0`), and calibration keys on
  it.
- **No proxy-side caching.** LiteLLM doesn't cache systemone calls (`litellm/types/caching.py`), so
  Antiphon caches decisions on (resolved model, state hash, question hash).
- **Retries.** When the proxy has fallbacks configured (`retry: proxy-managed`), Antiphon doesn't
  add its own retries on top.

### 7.4 Chains, fallback and escalation

- **Default chains**
  - `decisions-default`: Jev → Luna seat → Haiku seat → Grok Low seat.
  - `completions-cheap`: Luna → Haiku → Grok Low, or the enterprise proxy's chat models when
    configured.
- **Skip to the next entry when** the provider is not configured, held (`ModelAvailability`), over
  quota (subscription usage), over budget, timed out, returned 429/529 after backoff, or failed the
  schema after one repair.
- **Fail loudly with no fallback** on 400/401/403/422. Those mean our request or credentials are
  wrong, and falling back would hide the defect. The run node fails and an attention item opens.
- **`claude-cli` failures.** Launch and authentication failures are `Rejected` and stop the
  decision or completion chain. Other non-zero exits and missing CLI results are `Unavailable`,
  and timeouts are `Timeout`. Those outcomes allow fallback. If the chain runs out of entries,
  the node summary includes the CLI diagnostic. Exit diagnostics include the exit code and a
  fixed reason for recognized overload, rate-limit, connection, or unknown-option errors. The diagnostic
  masks sensitive values before clipping its private input and publishes only those fixed reasons.
- **Escalate by confidence.** An answer below its question's per-provider threshold escalates
  **that question only** to the next entry. A refused answer escalates the same way.
- **Record every call** in `llm_call`: chain, entry, provider, transport, resolved model, tokens,
  cost and cost basis (`actual` for HTTP, `api-equivalent` for seats, D-20), latency, outcome.

### 7.5 Calibration

Jev's probabilities are calibrated; the chat fallbacks' are not.
- A sample of decisions runs Jev and one fallback side by side (shadow).
- Labelled outcomes from the learn stage are added.
- A calibration map is fitted per (provider, resolved model, question type).

Thresholds are set per provider. TypeSafe's examples (0.3 and 0.5) are illustrations, not
defaults.

### 7.6 Configuration (first implementation)

Keys are named by configuration key only (`ApiKeyConfigKey`, `TokenConfigKey`) and resolved at
startup; a key's value is never stored in these sections or logged.

- **Reviewers, `Review:Agents`.** With `Transport` `delegation` (the default; `cli` is 7.7), each
  reviewer group is one read-only delegate task: role `Role`
  (default `Review`), a fresh worktree cut at the reviewed head SHA, never committed on settle,
  agent `Kind` (default `ClaudeCode`) at the level of the group's tier (`Levels`: `deep` High,
  `standard` Medium), on `Runner` when set (absent is the delegation default runner, `local` the
  desktop). A repo maps to a project by a `Repos` row (`Repo`, `ProjectId` or `Project`, optional
  `LocalPath`), else by the project whose `GitRepositoryUrl` ends in `owner/name` (GitHub) or whose
  `LocalRepositoryPath` is the subject's path (local git); with no match the reviewer node fails
  and names the fix. A GitHub PR head is fetched from origin's `refs/pull/<n>/head` into the
  project's checkout first. The pack is inlined in the brief up to `InlinePackMaxChars` (16,000);
  a larger pack is read from `GET /api/review-runs/{id}/packs/{iteration}/{group}`. The reviewer
  node is asynchronous: it records the task id, parks Waiting with the run queued, and completes on
  a later pass from the task's final report. A task not finished after `TimeoutMinutes` (90) is
  cancelled and the node fails.
- **Completions, `Review:Completions`.** Providers by id with a `Dialect`: `openai-chat`
  (`BaseUrl`, `Model`, key; `litellm-chat` defaults to Jev's key, `Review:Secrets:JevApiKey`),
  `anthropic-messages` (`anthropic`: Claude Haiku 5.5 at `https://api.anthropic.com`, key
  `Review:Secrets:AnthropicApiKey`; `AuthScheme: bearer` for a gateway, optional `Effort`), and
  `delegation` (`haiku-delegate`, `luna-delegate`: a Low read-only delegate task, bounded by the
  entry's `TimeoutSeconds`, 240). When no `completions-cheap` chain is configured, it is built from
  the HTTP providers that have their URL, key and model; seats join only by configuration. An
  empty chain skips verify and says so.
- **Thread resolution.** GitHub threads are resolved over GraphQL at the host's
  `Review:CodeHosts:Hosts:<id>:GraphQlUrl`, which defaults from `BaseUrl` (`/api/graphql` for
  GitHub Enterprise Server). The token needs pull requests read/write.

### 7.7 One-shot `claude -p` transport

An instance with no session runner can run real reviewers, a real verifier and real decisions on
the server machine's own Claude Code sign-in (the subscription, no API key). Every call is
`claude -p --output-format json --setting-sources "" --no-session-persistence --strict-mcp-config
--model <m> --tools <t>` with the prompt on stdin, never `--bare` (it disables the subscription
sign-in). The Claude Code nesting variables (`CLAUDECODE` and the session markers) are removed from
the child's environment. The process tree is killed on timeout or cancellation. A non-zero exit,
`is_error: true` or output with no JSON result fails the call. The public reason is selected from
stderr or the structured error result. The prompt goes only to stdin. Usage comes from the result: tokens
from `usage` (input counts cache reads and writes), the model from the main `modelUsage` key
(for example `claude-haiku-5-5`), and cost from
`total_cost_usd`, recorded as `api-equivalent` (D-20) with transport `OneShot`.

- **Reviewers, `Review:Agents:Transport` = `cli`** (default `delegation`, read at startup; any other
  value stops startup). Each reviewer group runs in the background under a `cli:<guid>` handle: the
  node parks Waiting and completes on a later pass, as with a delegate task. The head is checked out
  as a detached worktree of the `Review:Agents:Repos` row's `LocalPath` (a local-git subject's own
  path when there is no row): `git -C <repo> fetch --no-tags origin
  +refs/pull/<n>/head:refs/antiphon/review/pull/<n>` when the SHA is missing, then `worktree add
  --detach`, and `worktree remove --force` plus `worktree prune` afterwards. With no path the
  reviewer runs in an empty folder with the pack alone, and the brief says so. Point `LocalPath`
  at a clone the server may fetch into; this transport takes no repository mutation lease.
  `claude` gets `Read,Grep,Glob` only (`--tools` and `--allowedTools`), the brief with the pack
  always inline, the model of the group's tier from `Review:Agents:Cli:Models` (`deep` `opus`,
  `standard` `sonnet`, any other tier `DefaultModel`, `sonnet`), and `Review:Agents:TimeoutMinutes`
  (90). `Review:Agents:Cli:MaxParallel` (2) bounds how many run at once. Runs live in memory, so a
  handle from before a server restart fails the node ("the run was lost"); Refresh reruns it.
- **Completions and decisions, dialect `claude-cli`.** Set a `Model` (an alias such as `haiku`,
  or a full id) and use `Transport: OneShot` in the chain. Startup warns when a `OneShot` entry
  names a different or missing provider dialect. The configured dialect continues to select the
  adapter so existing HTTP entries can keep serving. The CLI dialect ignores `BaseUrl`
  and `ApiKeyConfigKey`. For proxy-backed decisions, use `Transport: Http` with
  `litellm-systemone` or another Jev dialect. The CLI runs with no tools in an empty scratch
  folder, bounded by the provider's `TimeoutSeconds`. A completion's prompt is the instructions followed by each input as a labelled
  section. A decision's prompt gives the state and every question (its name, its kind, the allowed
  values of a choice and the levels of a score) and asks for one JSON object
  `{"answers":[{"name":...,"value":...,"p":0.0-1.0}]}`, where `p` is the probability that `value` is
  right. The answer may be fenced or wrapped in prose. It maps as Jev's does: a noul is yes or no
  with a yes/no distribution (`p` for a yes, `1 - p` for a no), a choice is one of its values, and a
  score is one of its level labels, with `p` as the confidence. A missing or invalid answer is
  refused for that question only, and an answer with no JSON at all is a schema failure, so the
  chain moves on. Its probabilities are not calibrated (7.5).
- **Command.** `Review:Agents:Cli:Command` (`claude`) names the program for all three; set a full
  path when the server's `PATH` lacks it.

```json
{
  "Review": {
    "Agents": {
      "Transport": "cli",
      "TimeoutMinutes": 60,
      "Cli": { "Command": "claude", "MaxParallel": 2, "Models": { "deep": "opus", "standard": "sonnet" } },
      "Repos": [ { "Repo": "acme/widgets", "LocalPath": "C:\\repos\\widgets" } ]
    },
    "Completions": {
      "Providers": { "haiku-cli": { "Dialect": "claude-cli", "Model": "haiku", "TimeoutSeconds": 240 } },
      "Chains": { "completions-cheap": [ { "Provider": "haiku-cli", "Transport": "OneShot" } ] }
    },
    "Decisions": {
      "Providers": { "haiku-cli": { "Dialect": "claude-cli", "Model": "haiku", "TimeoutSeconds": 120 } },
      "Chains": { "decisions-default": [ { "Provider": "haiku-cli", "Transport": "OneShot" } ] }
    }
  }
}
```

A configured `Review:Decisions:Providers` replaces the default provider set (Jev and the seats),
and a configured chain replaces the default chains, so the block above runs on the CLI alone. The
default decision `TimeoutSeconds` is 30, which is too short for a cold `claude -p` start; give it 120.

## 8. Code host

### 8.1 `ICodeHost`

It covers:
- PR metadata and commits; compare; file at a ref
- threads, comments and reactions; post a review with many comments; reply; resolve a thread
- commit statuses
- the authenticated identity
- org, team and membership listing; CODEOWNERS
- webhook verification and translation into a common event model (`PrOpened`, `PrUpdated`,
  `Pushed`, `BaseMoved`, `CommentCreated`, `ReviewSubmitted`, `ThreadResolved`, `CiChanged`,
  `Merged`, `Closed`, `MembershipChanged`, `RepoChanged`, `ConfigChanged`)
- polling that produces the same events, for installs without a webhook (8.5)

Capability flags: `SupportsCheckRuns`, `CanResolveThreads`, `ApprovalCountsForMerge`,
`SupportsAutoMerge`.

### 8.2 Adapters

- GitHub (P1, PAT; GraphQL where REST can't, for example resolving threads)
- local git (P1; no conversation, findings go to Antiphon gates)
- a GitHub App (later, for check runs)
- GitLab, Azure DevOps, Bitbucket (later)

### 8.3 GitHub with a personal access token (v1)

- **Use a dedicated machine-user account.** Its review is an ordinary user review, so with write
  access its approval counts for branch protection. It cannot approve PRs it opened, so a separate
  account should open Antiphon's own PRs.
- **Fine-grained token permissions:** pull requests (read/write), commit statuses (read/write),
  contents (read), metadata (read), org members (read) for team sync.
- **Rate limits.** The limit is 5,000 requests an hour per token. Conditional requests that return
  304 don't count against it, which makes polling and daily reconciliation cheap.
- **Webhooks** need a publicly reachable endpoint and admin rights on the repo or org. Without one,
  the adapter polls through the cache layer (8.5).

### 8.4 Mirrors and caching

- **Mirrors.** The server keeps a bare mirror per registered repo, and runners fetch from the
  server's mirror. Diffs and file content come from git, not the API.
- **Cache layer.** A wrapper around any adapter. Content addressed by SHA is cached forever; other
  responses use ETags. It keeps a per-host rate-limit budget.
- **Base URL.** Configurable, for GitHub Enterprise Server and company proxies.

### 8.5 Events: webhooks, polling and Refresh

**Enterprise: every webhook event (D-30).** The org webhook is set to send every event. The engine
acts on this set:

| GitHub event | Actions | Common event | Drives |
|---|---|---|---|
| `pull_request` | opened, reopened, ready_for_review, converted_to_draft, edited, synchronize, closed | `PrOpened`, `PrUpdated`, `Pushed`, `Merged`, `Closed` | intake, scope, the `await-fixes` and `await-merge` waits |
| `pull_request_review` | submitted, dismissed | `ReviewSubmitted` | comment workflow, the bot-only counter (section 5) |
| `pull_request_review_comment` | created, edited | `CommentCreated` | comment workflow |
| `pull_request_review_thread` | resolved, unresolved | `ThreadResolved` | finding state |
| `issue_comment` | created, edited, on PRs only | `CommentCreated` | comment workflow, `/recheck`, mentions |
| `push` | to the base branch of an open PR; to a default branch touching `CODEOWNERS` or `.antiphon/` | `BaseMoved`, `ConfigChanged` | `base-moved` wait events; ownership and config reload |
| `status`, `check_suite`, `check_run` | state changes, completed | `CiChanged` | the `ci-green` approval rule |
| `membership`, `team`, `organization` | added, removed, renamed, member_added, member_removed | `MembershipChanged` | team sync (section 12) |
| `repository` | renamed, transferred, archived, deleted | `RepoChanged` | scope, mirrors |
| `ping` | | | webhook health on the providers page |

- **Everything else is stored or ignored, never acted on.** An instance setting lists the other
  event names to store in `webhook_delivery`, with a retention period, for audit and later replay.
  The rest are acknowledged and dropped.
- **Delivery.** The `X-Hub-Signature-256` signature is checked against the webhook secret, which is
  held under credential custody. Deliveries are deduplicated by `X-GitHub-Delivery`, acknowledged at
  once, and processed from a queue.
- **Reconciliation.** Webhook installs still run one poll a day, so a missed delivery is caught.

**Home: polling plus Refresh.** An install without a public webhook polls every in-scope repo at
`codeHost.pollInterval` (instance setting, default 2 minutes). Each poll goes through the cache layer
and reads open PRs by update time, their reviews, comments and threads since the last cursor, base
tips and CI status. Conditional requests that return 304 don't count against the rate limit (8.3).
Polling produces the same common events as webhooks, so the engine only sees a difference in latency.

**Refresh.** A **Refresh** button on the review runs page (every in-scope repo) and on a run page
(one PR) runs a poll at once, without waiting for the interval. The API is
`POST /api/review/refresh`, with an optional body `{ "repo": "<owner/name>", "pr": <number> }`. It
goes through the cache layer like any poll. It works on webhook installs too, to recover from a
missed delivery.

#### Polling (first implementation)

There is no public webhook at home, so polling is the integration. `ReviewPoller` is a hosted
service with settings under `Review:Polling`: `Enabled` (default false), `IntervalSeconds` (default
120, at least 30), `CommandPrefix` (default `/antiphon`) and `Repos`, each with `Repo`, `Host`
(default GitHub; the host id and credential come from `Review:CodeHosts:Repos`), `BaseBranches`
(empty means all), `IncludeDrafts` (false), `SkipLabels` (default `antiphon:skip`) and an optional
`RequireLabel`. A host that cannot list PRs says so through `CanListPullRequests`; local git cannot.

Each pass, per repo, reads the open PRs (`GET pulls?state=open`) and the PR conversation comments
since the repo's cursor (`GET issues/comments?since=`, PR comments only). Both are conditional
GETs, and the cursor moves only when a comment arrives, so a quiet repo costs 304s. Then, in order:

1. **Comment commands** from `OWNER`, `MEMBER` or `COLLABORATOR` (any other `author_association` is
   ignored): `/antiphon review [steer]` starts a run when the PR has none (or only a finished one,
   ignoring the filters), and otherwise asks for a round with that steer. `/antiphon recheck` is the
   same with no steer. The command must start the comment. Each gets an `eyes` reaction and a short
   reply ("Round 2 queued, steer: …", or the refusal), both best effort, with the App's Pull requests
   permission (`issues/{n}/comments` and comment reactions on a PR).
2. **New PRs**: every open PR that passes the filters and has no run gets one (trigger `opened`).
3. **Moved or closed PRs**: a run whose PR head differs from the run's head, or whose PR is no longer
   open, is refreshed (as the Refresh button does). An unchanged run is left alone, so a quiet pass
   publishes nothing. A queued run is skipped too, because its next intake pins the current head.

A round request (the comment, or `POST /api/review-runs/{id}/rounds` from the run page) completes
`await-fixes` with `recheck` and stores the steer. At a gate it is refused, because the gate's
another-round decision is the way there. On an approved, awaiting-merge or failed run it starts a
`recheck` iteration at the current head. That supersedes the parked wait and sets `antiphon/approval`
back to pending, as a push revokes an approval. A posted approving review is not dismissed yet.

**Dedupe without a migration.** A command's comment id is recorded as `comment:<id>`: as the trigger
of the iteration it started, or in the summary of the wait it completed. A refusal's reply carries
the id in a hidden tag. The poller checks all three before acting, so a command is never acted on
twice, including across a restart. On start it looks back only two intervals. A transient failure
(503) holds the cursor at that comment, and the next pass retries it.

**Failures and state.** One repo's failure (rate limit, 403, network) is logged and recorded as that
repo's `error`, and never stops the other repos or the loop. The last pass (started, finished, and
per repo the open PRs, runs started, refreshed, commands handled and the last error) is kept in
memory, and is read by `GET /api/review-polling`. `POST /api/review-polling/poll` runs a pass now,
even with the timer off. The runs page shows this as a strip with **Check GitHub now**.

Enabling polling on a busy repo starts a run for every matching open PR on the first pass. Use
`RequireLabel` to opt PRs in gradually.

## 9. Check catalogue

One file per check: `.antiphon/review/checks/<category>/<id>.md`.

```markdown
---
id: CHK-0142
title: Lock released before async save completes
severity: major
flags: []                      # e.g. [security], [data-loss]
status: active                 # candidate | shadow | active | retired
requiresHuman: false
triggers: { globs: ["**/*.cs"], languages: [csharp], tags: [concurrency, persistence], symbols: ["SaveChangesAsync"] }
relatedFiles: ["**/DbContext*.cs"]
provenance: [CARD-1234, docs/investigations/…-why-missed-….md, commit:abc1234]
revisions:
  - { v: 3, date: 2026-11-21, why: "narrowed to EF SaveChanges; v2 flagged Dapper (40% FP)", eval: "replay 9/10, FP 6%" }
---
# @model: Selector
One paragraph used as the Jev `applies` question for check selection.
# @model: Prompt
What to look for and how to judge it. Any normal markdown, including ## headings.
# @model: Examples
Bad and good snippets.
# @humans: Why this check exists
Short text shown in the review UI next to a finding from this check.
# @maintainers: Rationale
Why it exists, the incident it came from, what it has caught.
# @maintainers: Known false positives
# @maintainers: Revision notes
What was tried, what was rejected and why.
```

- **Section markers.** A section starts only at a column-0 line matching `# @<audience>: <name>`
  outside a code fence. Every other heading is content. Audiences are `model`, `humans` and
  `maintainers`; the set can be extended.
- **Prompt hash.** Computed over `@model` sections only. Editing rationale or revision notes doesn't
  invalidate caches, calibration or evaluation history.
- **Lifecycle.** `candidate` (awaiting approval) → `shadow` (selected and evaluated, never posted) →
  `active` → `retired`. Promotion out of shadow needs a precision threshold on real PRs or the
  replay set.
- **Index and stats.** The DB indexes the files with embeddings. Stats are kept per scope: times
  selected, posted, accepted, dismissed, ignored, plus precision.

## 10. Prompt sources and layering

Prompt sources are configured **per instance** (D-33), like the graph. v1 models the full layer
stack. Layers resolve **company → team → instance → repo**, lowest first, so in `override` mode the
repo wins:

| Layer | Default source | Holds |
|---|---|---|
| Company | `company/` in the instance-wide `.antiphon`, or the source repo and ref the `Org` entity names | Company-wide checks, prompts and policy defaults |
| Team | `teams/<team>/` in the instance-wide `.antiphon`, or the source repo and ref the `Team` entity names | The team's checks, context notes, suppressions and policy |
| Instance | `review/` in the instance-wide `.antiphon` (the repo and ref named in the instance settings) | Instance-wide checks, prompts, groups and policy defaults, and the default workflow graph. It applies to every repo and workflow the instance runs |
| Repo | The repo's own `.antiphon/review/`, read at the base SHA (D-8) | The repo's checks, overrides, policy, workflow edits and `related.yaml` |

```
<instance-wide .antiphon>/   # the repo and ref named in the instance settings
  review/                    # instance layer
  company/                   # company layer, unless the Org entity names a separate source repo
  teams/<team>/              # one team layer per team, unless the Team entity names a separate source repo

.antiphon/review/            # each layer folder above, and each repo, uses this layout
  policy.yaml  workflow.yaml  related.yaml
  checks/  groups/  prompts/  questions/
  sources.lock               # repo layer only: per path, each lower layer's source, commit and content hash; merged hash; method; merged-by
  resolved/                  # repo layer only: agent-merged outputs, referenced by the lock
```

- **Which team layer a repo gets.** Each registered repo has one **layer team**. It is set on the
  repo in Antiphon, and defaults to the owner of the CODEOWNERS catch-all rule (`*`), or else to the
  team that owns the most files. A repo can have several owning teams (D-25), but only its layer
  team's layer applies. Parent teams add no layers in v1. A repo with no layer team skips that
  layer.
- **Who owns a layer.** Whoever owns its folder or repo. CODEOWNERS on the instance-wide `.antiphon`
  can give `teams/<team>/` to that team and `company/` to the instance's administrators.
- **Refs.** The instance settings pin the instance-wide `.antiphon` ref; a separate team or company
  source repo is pinned on its entity. Every run stamps each commit it read. Moving a pin is a
  settings change with revision history.
- **Merge modes.**
  - `override`, the default: the highest layer wins.
  - Declarative, set in the file's header: `extends`, `append`, `patch`.
  - **Agent merge** for real conflicts only. It writes `resolved/<path>` plus a lock entry, and runs
    again only when an input hash changes. Lock changes arrive as a PR for human approval.
- **Provenance.** Every run stamps the workflow revision hash and every prompt hash, following the
  existing `[bundle:key vHASH]` convention. The UI shows each prompt's final text and which layer
  each part came from.
- **Re-merge.** A change to any lower layer re-runs the agent merge only for repo paths whose lock
  records a different upstream hash. Each affected repo gets one PR.
- **Later.** Language and framework facet layers, and a repo-level `sources.yaml` that adds further
  common repos. Facets stay out of v1 because they cut across teams, so they need their own place in
  the resolution order, and the 2-teams promotion already has the company layer as its target.
- **Repo changes needed.**
  - Add a `.gitignore` exception for `.antiphon/review/`.
  - Stop task footprints skipping `.antiphon/review/`.
  - Stop slot wipes discarding `.antiphon/review/` (the `.antiphon/**/*.md` keep rule should still
    apply).

## 11. Human review

### 11.1 Identity

- **JWT bearer:** validate issuer, audience and signing keys.
- **Trusted headers from the enterprise auth proxy:** configurable header names for user, email,
  groups and scopes. They are accepted only from the configured proxy.
- Either way, the identity maps to an Antiphon `User`, the code-host login and teams. Scopes decide
  which repos and teams a person may act for.

### 11.2 Review UI

- **Per PR or run:** each finding with the code at its anchor, the check's `@humans` text, the thread
  history, and the blast-radius summary.
- **Choices per finding:** will fix / fixed / dismiss (reason + scope) / dispute / question / defer,
  with free text. A blocker offers **not a blocker** and **accepted risk** instead of dismiss, each
  with a required reason (11.6). A wide scope opens the confirmation step (11.7).
- **Trust.** When a choice needs a second person under the thresholds, the screen says so before
  submit ("Your clear needs a confirmation from a member of team-x").
- **Submit** posts one code-host review (one notification). Thread replies follow the "Antiphon
  first, then the code host" rule (section 6).
- **Attribution.** The machine user posts, signed "— <display name> via Antiphon". This uses the
  name, not an @mention, so nobody is pinged; that's configurable.
- **Gate decisions** (approve, another round with a steer, close) happen on the same screen.

### 11.3 Queues

- **Mine:** assigned to me, plus unassigned items from teams I've selected.
- **Team** and **overall**.
- **Item kinds:** review gate, approval gate, escalated dispute, prompt-merge approval, seed
  candidate, learn proposal, and the trust items: blocker-clear confirmation, second approval,
  second gate decision, steer review and wide-scope proposal (11.9). A bypass-category finding goes
  to the `bypassRouteTo` team's queue: the owning team by default, or an override team (section 5).
- **Trust items** go to members at or above the threshold concerned, never to the person who made
  the first decision.
- **Item states:** unassigned → assigned → in progress → done, or passed back.
- **"Not me"** returns the item to the team queue with a reason, and excludes that person from the
  next pick.
- **SLA ageing** is measured in working hours. Notifications go out through the existing channels
  (Telegram) and the attention feed.

### 11.4 Routing

Code ownership is **many-to-many** (D-25). A path can have several owners, each a team or a user,
and a user can belong to several teams.

1. Changed paths are matched against the ownership rules (from CODEOWNERS, section 12). Each path
   yields its full owner set: every team and user on the matching rule.
2. The owning teams are the teams in those sets. The primary team is the one with the most changed
   lines; the other teams get read-only visibility. A user owner who is in no owning team is added
   to the gate as an individual reviewer. A user in several owning teams counts once.
3. If the primary team has opted in, a reviewer is picked at random from its members who are opted
   in and eligible (at or above the `randomAssignment` threshold, 11.9). The pick is weighted by
   open load and excludes the author and anyone who passed.
4. Findings go to the **author's** queue as well as the PR.

### 11.5 Author resolution

The PR author is used unless the account is a service account (identity flag, a `[bot]` suffix, or
a known bot login). In that case the `resolve-author` workflow runs:
1. Code signals: `Co-authored-by` trailers, commit authors mapped to users, "on behalf of" in the PR
   body, the linked issue or card assignee, the Antiphon task requester, the branch name.
2. A Jev `choice` between the candidate users.
3. A general agent (Haiku or Luna seat) with read access to commit history, linked cards and issues,
   and the team directory.
4. Only then, the team queue tagged "author unknown".

The deciding signal is recorded.

### 11.6 Blockers, dismissals and incidents

- **The bot never approves while a blocker is open** (D-21, 4.10).
- **Clearing a blocker.** A human marks a blocker **not a blocker** (a false positive, or a severity
  that doesn't block) or **accepted risk**, with a required reason. A clear that stands closes the
  finding with reason category `false-positive`, `by-design` or `accepted-risk`.
- **Who may clear** is `dismiss.blocker`: `author` (default, D-11), `other-member` or `maintainer`.
- **Trust gate.** The clear counts alone only when the person's effective trust score for the scope
  is at or above `clearBlocker` (11.9). Otherwise it is `pending-confirmation`:
  - a blocker-clear confirmation item goes to members of the owning team at or above the threshold,
    never to the person who cleared it;
  - the confirmer agrees, so the clear stands, or rejects, so the blocker reopens with a reply on
    its thread;
  - until then the blocker stays open.
- **Approval after clearing.** Once every blocker is fixed or cleared, the bot may approve if
  everything else in 4.10 holds.
- **Records.** The finding records who cleared it, who confirmed it and both reasons. For trust and
  stats, a confirmation counts as a dismissal by the confirmer too.
- **Bypass categories.** An adjudicator concession never clears a blocker. Only humans can clear one.
- **Incidents.**
  - The Debug and why-missed investigation templates (including Antiphon's own
    `docs/investigations/why-missed-template.md`) gain a step: "search Antiphon's finding history for
    dismissed findings on the code involved, and link them".
  - A linked dismissal is marked **wrong**. That counts heavily against the trust score and stats of
    everyone who dismissed or confirmed it (11.8), raises the check's weight, and queues the
    dismissal reason for review.

### 11.7 Wide-scope dismissals

A dismissal at path, repo or team scope **always shows a confirmation step** (D-34), in the review
UI and, as a reply, in the comment workflow (section 6).

1. **Preview.** It shows exactly what will happen:
   - the target: the repo's `.antiphon/review/` (path or repo scope), or the repo's team layer
     (team scope), naming its folder or source repo;
   - the rule text: a suppression, context note or severity change (section 13);
   - the files the learn PR will touch;
   - for team scope while the 2-repo rule (D-32) is unmet, that a proposal is queued instead, for
     example "Queued as a team proposal: seen in 1 of the 2 repos needed";
   - when the person is below `dismissWideScope` (11.9), that the wider scope becomes a proposal.
2. **Optional steer prompt.** With a steer, an agent first drafts the updated rule from the
   dismissal, the finding, its thread and the steer, and the learn PR is opened with that rule.
   Without one, the previewed rule text is used as is.
3. **After confirming,** the learn PR is opened (or the proposal queued), and the confirmation shows
   its link. Cancelling changes nothing.
4. **Links both ways.**
   - The learn PR body links the original PR, the finding and its thread.
   - A reply on the original PR's thread links the learn PR.
   - Antiphon stores the link on both records: the finding and the learn proposal (`learn_link`).

### 11.8 Trust scores

Trust scores (D-22) are kept per user per scope: **repo**, **team** and **area** (a named path area,
from `antiphon.areas.json` where the repo has one, or an ownership-rule path). They are stored in
the database and recomputed nightly by the stats job (section 15).

- **Visibility.** The person and their team lead can see them, as can the people allowed to adjust
  them. They never appear on leaderboards.
- **Which score an action uses.** The narrowest scope that covers the action: the area, then the
  repo, then the team. A scope with too little data borrows the next wider one, and then the
  neutral start.

| Component | Effect | Evidence |
|---|---|---|
| Wrong dismissal | Heavy negative | A dismissal or confirmation later linked to a bug or incident (11.6) |
| Dismissal that held up | Positive | A dismissal or confirmation with no wrong link after the hold period (`trust.holdDays`) |
| Raised issue later confirmed | Positive | A human-raised finding that was accepted or fixed (section 13) |
| Suggestion merged | Positive | A suggestion from the person that was merged |
| Code-owner status | Positive | An ownership rule for the scope names the person or one of their teams |
| Tenure | Positive, capped | Time since the person's first activity in the scope |

The component weights are database settings (D-26). Scores run **0–100** (D-39). The hold period
`trust.holdDays` defaults to 90 days.

- **New users** start at the neutral score (`trust.neutralStart`): **60** in a **high-trust
  instance** (instance setting `trust.highTrust`) and **40** otherwise. With thresholds near 50, a
  new user in a high-trust instance acts alone from day one; this is the "author may dismiss
  blockers" direction. Elsewhere a new user's clears need confirmation until their record builds.
- **Adjustments.** Authorised people can adjust a user's score per scope. They are the team lead or
  a maintainer by default (setting `trust.adjusters`, with team overrides).
  - An adjustment is an **offset** (±n) or a **pinned value**. It carries a required reason and an
    optional expiry.
  - Nobody adjusts their own score.
  - The effective score is the latest active pin if there is one; otherwise the raw score plus the
    active offsets. It is clamped to the scale and recomputed at each adjustment and each expiry.
  - Adjustments are append-only rows (`trust_adjustment`): who, when, scope, kind, value, reason and
    expiry. Revoking one adds a row.
  - Every view of a score shows the **raw computed** and **effective** values side by side, with the
    active adjustments listed.

### 11.9 Trust thresholds

Each threshold gates one action and has a set behaviour below it (D-23). The thresholds are database
settings: instance defaults plus team overrides, with revision history (D-26). Each is compared with
the effective score for the action's scope (11.8). Scores run 0–100, and every threshold defaults to
about 50 (D-39). A team override can raise or lower one threshold without touching the others.

| Threshold | Action it gates | At or above | Below |
|---|---|---|---|
| `clearBlocker` | Clearing a blocker alone (not a blocker, accepted risk) | The clear counts. With every blocker cleared, the bot may approve if the rest is clean | **Second person needed:** a blocker-clear confirmation item; the blocker stays open until a member at or above the threshold confirms (11.6) |
| `dismissWideScope` | Dismissing at path, repo or team scope | The confirmation step opens the learn PR (team scope: queued until the 2-repo rule is met) (11.7) | **Becomes a proposal:** the dismissal applies to this finding only, and the wider scope goes to the owning team's queue as a wide-scope proposal |
| `decideGate` | Deciding a gate: approve, another round, close, or any other gate decision | The decision takes effect | **Second person needed:** a second gate decision item; the gate stays open until a member at or above the threshold decides |
| `approve` | Approving at `human-approve` or `human-review` (it must also clear `decideGate`) | The approval counts and `antiphon/approval` goes green | **Second person needed:** recorded as a first approval; a second approver at or above the threshold completes it |
| `randomAssignment` | Being picked for random assignment (11.4) | In the pick pool | **Can't be assigned** at random |
| `acceptSteer` | Having a steer prompt accepted without review (section 5) | The steer goes into the next round's packs as written | **Second person needed:** a steer review item; the round starts once a member at or above the threshold accepts, edits or drops the steer |

A second person is never the person who made the first decision, and is never the author for an
approval or a gate decision (11.10).

### 11.10 Gate deciders

A gate decision counts only when the decider:
1. is a member of an owning team or a user owner of the changed paths (11.4), or is otherwise
   authorised for the gate's queue;
2. clears the thresholds for the decision (11.9);
3. is not the PR author (the resolved author, 11.5), unless `gates.authorMayDecide` allows it
   (D-24):
   - `sole-owner` (default): only when the owning team has one member and no other team or user owns
     any changed path;
   - `always`: a per-repo override that always allows it.

A home install where one person owns everything meets `sole-owner` without an override.

## 12. Teams and orgs

- **Entities:**
  - `Org`, with its **layer source** for the company layer: by default the `company/` folder in the
    instance-wide `.antiphon`, or a separate source repo and ref (section 10)
  - `Team`, with a parent team, its **layer source** (by default `teams/<team>/` in the
    instance-wide `.antiphon`, or a separate source repo and ref), a working-hours calendar and an
    auto-assign setting
  - The registered repo (`Project`) gains its **layer team**, the team whose layer it reads
    (section 10)
  - `Membership`, joining a user to a team, with an auto-assign opt-in and a role (member, lead,
    maintainer). A user can have memberships in several teams (D-25).
  - `Identity`, mapping code-host login, email and user, with a service-account flag
  - `OwnershipRule`, one per CODEOWNERS line: repo, path pattern, order, source line
  - `OwnershipOwner`, joining a rule to each of its owners, each a team or a user. A rule can have
    several owners, so a path can too (D-25).
  - `TrustScore` and `TrustAdjustment`, per user per scope (11.8)
- **Sync:** code-host org, team and membership data maps in. It is reconciled by webhook
  (membership events) where reachable, otherwise daily using conditional requests. Ownership rules
  are reloaded when a push to a default branch touches `CODEOWNERS` (8.5). Manual overrides in
  Antiphon take precedence.
- **Uses:** learning scopes (repo → team → company; language and framework facets later), queue
  routing, gate deciders, trust scopes, stats and the prompt layers all read these entities.

## 13. Learn

| Signal | Source |
|---|---|
| Accepted | Code at the anchor changed and the recheck says fixed; 👍; "fixed" reply; "will fix" then fixed |
| Dismissed | Resolved with no code change; 👎; a dismissal with reason and scope (PR or Antiphon UI) |
| Ignored | PR merged with the finding still open (weak negative) |
| Missed | A human raised an issue the bot didn't (recall gap) |
| Wrong dismissal | A dismissed finding later linked to a bug or incident (11.6) |
| Steer | Recurring steer prompts on "another round" |
| Context | Files reviewers opened beyond the pack, files they found missing, and pack files never cited |

**Outputs are proposal PRs at the narrowest scope first.** In the repo's `.antiphon/review/`, a
proposal can be:
- a **suppression** (a check off for a glob)
- a **context note** ("in this repo EF retries cover this; flag only outside a retry scope"), which
  goes into future packs so reviewers learn why
- a severity change
- a new candidate check, from missed issues and steers
- a related-file rule

A dismissal at path, repo or team scope reaches these outputs only through the confirmation step
(11.7), and its learn PR is linked both ways with the original PR.

**Promotion** is by repetition (D-32):

| The same normalised adjustment seen in | Proposed as |
|---|---|
| 2 repos of one team | a PR to that team's layer: its `teams/<team>/` folder, or its separate source repo |
| 2 teams | a PR to the company layer: `company/`, or the Org's separate source repo |

Promotion PRs target these layers from day 1 (D-32, D-33). Language and framework facets come
later (section 10).

Each proposal links to its evidence threads. A promoted rule that repos keep overriding is flagged
for demotion. Low-volume repos borrow team-level statistics until they have enough data of their
own. The minimum evidence for a context note, which was part of OQ-6, is now OQ-15.

**The same data also:**
- calibrates fallback confidence (7.5)
- scores each related-file source by hit rate (cited in a finding / opened by a reviewer / never
  touched), which reweights the sources in 4.6

## 14. Seeding

A `seed` workflow takes **any source an agent can reach**, for example:
- a docs folder (the 122 why-missed docs are one example)
- `git log --grep`
- incident reports through MCP, Jira or Confluence
- the code host's PR review history
- postmortems

```
source spec → enumerate + shard → extraction agents (fan-out; any agent kind with the right tools)
  → seed-candidates/1 (ABF): candidate checks with provenance (source URI, excerpt,
    bug commit, fix commit), replay cases (bug + fix commits), related-file hints
  → join: dedupe against the list (embeddings + Jev duplicate / refinement / new)
  → PR to the target layer → gate → shadow → active
```

Each source keeps a **cursor** (last commit, date or document hash), so reruns only process new
material. Replay cases grow the evaluation set (section 20).

## 15. Stats

**Storage**
1. An append-only `review_event` log: gate opened, assigned, passed or decided; comment posted;
   finding state changed; and so on.
2. A nightly job writes `stats_daily(date, scope_type, scope_id, metric, value)` for each user, team,
   repo and org. The same job recomputes trust scores (11.8).
3. Weekly, monthly and yearly rollups are built from the daily rows.

Percentiles come from raw events, because daily p90s can't be averaged. Durations are in
**working hours**, from the team calendar with user overrides.

| Area | Metrics |
|---|---|
| Gates | Opened and closed by kind; time to first response; time to decision (p50/p90); end-of-day backlog and age; passes and reassignments; SLA breaches |
| Decisions | Approved, sent back, another round with steer; dismissed by reason; accepted; disputes won or lost |
| Activity | Comments, words, threads, questions to the bot, bot-answer 👍/👎 |
| Outcomes | Suggestions merged; human-raised issues confirmed; learnings merged and promoted; wrong dismissals (count against) |
| Quality signal | Fast approvals of large PRs (a "rubber-stamp" indicator), shown to the person and the team lead, not ranked |
| Per PR / team | Rounds per PR; time from open to bot-clean to human approval to merge; bot precision; cost per PR; **cost per accepted finding** (dollars, D-20) |
| Trust | Clears confirmed or rejected; second-person items opened and their wait; adjustments made (shown to the person, the lead and adjusters only) |

Leaderboards are per team and per month, and **rank outcomes, not volume**. Words never score on
their own.

**Scoring settings live in the database (D-26).** Leaderboard and impact weights, trust component
weights, trust thresholds (11.9), `trust.holdDays`, `trust.neutralStart` and `trust.adjusters` are
settings with an instance default and optional team overrides. `trust.highTrust` is instance only.
They follow the dispatch-concurrency pattern (CARD-0505): every change stores a full snapshot with
its revision, reason and author, history survives clearing an override, and an edit names the
revision it was based on so concurrent edits conflict instead of overwriting. A settings page edits
them and shows their history. Files never hold them; files hold only config that travels with a
repo (checks, prompts, workflow, policy).

**Cost display (D-20)**

| Source | Amount shown | Basis |
|---|---|---|
| Jev and other HTTP API calls | Actual cost: `x-litellm-response-cost`, the provider's usage cost, or the price table where that is the bill (7.3) | `actual` |
| Standing seats and agent nodes | API-equivalent estimate: transcript tokens × the price table, as `AgentTask.CostUsd` is computed today (`DelegationCost.Estimate`, `Delegation:Pricing` rates) | `api-equivalent` |
| Totals (per node, run, PR, accepted finding, team, day) | The sum of both | mixed |

- **Dollars first, everywhere cost is shown:** run lists, the run overlay, the providers page, stats
  and queue items.
- **Format:** under $1, rounded cents ("5¢"); $1 or more, rounded dollars ("$4").
- **Tooltip:** the exact amount, the split between actual and estimate, and for seats the
  subscription quota used, from the `GET /api/subscription-usage` samples ("≈ 3% of the Claude
  weekly window"). Quota % appears only there.

## 16. Runners and deployment

### 16.1 Runners

- **Contract.**
  - The session runner, either as an `antiphon/runner-base` image to build `FROM` or as a versioned
    download.
  - git, plus the agent CLIs for whichever kinds the runner advertises.
  - Phone-home settings: server URL, the join secret (mounted as a file), capacity, and capability
    labels (for example `review`, `dotnet9`, `node22`, the repos it may access).
  - Volumes for state, mirrors and credentials.
  - One or more standing seats per container (7.2).
- **Enrollment.**
  - A runner proves it may join by sending the **shared join secret** (the same for every runner).
  - The server then issues the runner its own token, which the operator never sees or configures, so
    one runner can be removed without rotating the shared secret.
  - Two join secrets can be valid at once, for rotation.
  - This sits alongside today's per-id `PhoneHomeRunner:Runners:<id>` map.
- **Doctor.** `antiphon-runner doctor` checks the contract at registration. A runner missing a
  capability is marked degraded. Workflow nodes can require labels.
- **Custom image docs.** `docs/runner-custom-image.md` will hold a Dockerfile template, a compose
  template, the required env vars and the enrollment steps.
- **Later:** continuing a previous session on a seat; ephemeral per-session containers launched by a
  host runner.

### 16.2 Deployment

| | Enterprise | Home |
|---|---|---|
| Platform | Kubernetes | The desktop and server2 today, moving fully to Docker on server2 (CARD-1744, a separate card; `docs/docker-stack.md`) |
| Server | The Antiphon server container with a Postgres sidecar in the same pod, on a persistent volume. One replica per instance, because there is one writer | AppHost on the desktop until the cutover; after it, the server container on server2 with its Postgres service |
| Runners | Agent runner containers as sidecars in the pod | The existing phone-home runners (server2 runner, desktop runner) |
| Instances | Possibly one instance per team (to be confirmed, OQ-5) | One |
| Events | Every webhook event (8.5) | Polling plus Refresh (8.5) |
| Models | Jev through the company LiteLLM proxy; the proxy's chat models | Subscription seats |

**Implications**
- **Learnings cross instances only through PRs.** Learn PRs target a repo's `.antiphon/review/`, its
  team layer or the company layer (section 13). By default the team and company layers are folders
  in the instance-wide `.antiphon`, so their rules stay inside the instance. When several instances
  name the same separate source repo for a team or for the company (the `Team` or `Org` layer
  source), a promotion PR there reaches every instance that reads it. That is how a learning
  crosses instances, for example with one instance per team (OQ-5).
- **Stats stay per instance.** Trust scores, leaderboards and rollups are computed per instance and
  never merged. A person active in two instances has two independent trust records.
- **The runner join secret is a Kubernetes Secret,** mounted as a file in each runner sidecar (16.1).
  Rotation uses the two-valid-secrets rule. The per-runner token the server issues is kept in the
  runner's state volume.
- **Enrollment.** A sidecar runner enrolls on pod start with the join secret. When a restarted pod
  has lost its state, it enrolls again as a new runner, and the server retires runners that have
  stopped phoning home. Capability labels come from the pod spec.

## 17. Visibility (UI)

| Page | Contents |
|---|---|
| Review runs | PRs by stage, the number in review, queue depth, models and providers in use, cost per run in dollars (D-20) with tokens and quota in the tooltip, cost per accepted finding. A **Refresh** button polls every in-scope repo now (8.5) |
| Run detail | The live workflow graph overlay (status, iteration, model, time, cost per node), drawn in collapsible sections; a collapsed fan-out shows as "reviewers ×4" and expands on click (3.3). Findings, rounds, statuses posted, recorded drift, and a **Refresh** button for this PR |
| Workflow editor | `@xyflow/react` + `elkjs`. Back edges are drawn as loops with iteration counts. Sections are drawn as collapsible groups, as on the run page. Saving validates the cycle rule and produces a revision or PR. The graph is read-only: changes are made by AI prompt into a staging area (preview, diff, validation, replay estimate) before saving. Each node's description, typed parameters and section are edited in an inspector and staged the same way |
| Pack viewer | Per reviewer: what was loaded and why, and what it opened that the pack missed |
| Checks | Selection rate, precision per scope, last hit, shadow candidates, noisy checks to retire |
| Queues / review UI | Section 11 |
| Stats | Section 15 |
| Providers | Calls, cost in dollars (actual or estimate), escalations and fallbacks per chain entry; subscription quota % in the tooltip only; webhook health |
| Trust | For a person: their score per scope, raw and effective side by side, the components, and active adjustments. For a lead or adjuster: the same for their team, plus adjusting (offset or pin, reason, expiry) and the audit trail (11.8) |
| Settings | Scoring weights, trust weights and thresholds: the instance defaults, team overrides and revision history (D-26) |

Live data arrives through `useSignalRInvalidation` entries. A by-model grouping is added for agent
tasks and `llm_call`.

## 18. Data model (new tables)

| Area | Tables |
|---|---|
| Runs | `review_subject` (host, repo, PR or commit range), `review_run`, `review_iteration`, `workflow_definition`, `workflow_revision`, `node_execution`, `drift_record` |
| Findings | `finding` (with clear and confirmation fields), `finding_event`, `finding_thread_link`, `human_finding`, `learn_link` (learn PR ↔ original PR, finding and thread) |
| Events | `webhook_delivery` (stored deliveries, deduplicated by delivery id), `poll_cursor` |
| Context | `context_item` (ledger), `pack` (hash, staged path) |
| Models | `llm_call`, `decision_cache`, `calibration_map`, `provider_chain` |
| Checks | `check_index` (path, hashes, embedding), `check_stat` (per scope) |
| Prompts | `prompt_source` (one row per company, team and instance layer: folder or repo and ref), `prompt_lock_entry`; a layer-team column on the repo registration |
| People | `org`, `team`, `membership` (user ↔ team, many-to-many), `identity`, `ownership_rule`, `ownership_owner` (rule ↔ team or user, many-to-many), `work_calendar` |
| Trust | `trust_score` (user, scope type, scope id, raw, effective, components, computed at), `trust_adjustment` (append-only) |
| Settings | `scoring_settings`, `scoring_settings_revision` (instance and team scopes, as `DispatchConcurrencySettings`) |
| Queues | `gate`, `queue_item`, `assignment` |
| Stats | `review_event`, `stats_daily`, rollup views |
| Seeding / eval | `seed_source`, `seed_cursor`, `seed_run`, `eval_case`, `eval_run` |
| Runners | `runner_enrollment`, `runner_capability` |

## 19. Security

- Review config is read from the base branch; a PR can't change its own rules (D-8).
- Diffs, comments and PR text are untrusted data. Triage outputs are limited to the intent list, and
  actions are allowlisted.
- Reviewers are read-only and carry no credentials beyond their model sign-in. Writes (conflict
  resolution, later fixes) are separate node kinds that need a grant.
- Trusted identity headers are accepted only from the configured proxy.
- Webhook deliveries are accepted only with a valid signature (8.5).
- Trust: nobody adjusts their own score, a second person is never the first decider, and every
  adjustment and settings change is audited (11.8, section 15).
- Each tenant has a setting for whether code may be sent to external APIs (enterprise plans can
  offer zero data retention).
- The machine user has least-privilege token permissions (8.3). Secrets follow
  `docs/agent-credentials.md`, and are never printed or logged. On Kubernetes, the join secret and the
  webhook secret are Secrets mounted as files (16.2).

## 20. Phasing

| Phase | Delivers |
|---|---|
| **Before P0** | The card-claim feature (section 22). Phase cards are filed only after it exists (D-36) |
| **P0: specs and seed** | ABF v1 draft spec + conformance corpus + C#/TS parsers, with the legacy scan skipping ABF documents (D-28); **replace `PipelineHandoff.cs` and `ReviewEvidence.cs` with the ABF parser**, so the server and the engine read reports the same way before the P1 shadow run (the corpus's `currentParsers` values then retire); check file format; `.antiphon/review/` layout, gitignore exception, footprint and slot-wipe fixes; data model; seed ~100 checks with a manually run seeding agent (why-missed docs, CARD-1809 R1–R5); replay set of ~20 why-missed cases (bug + fix commits) |
| **P1: thin vertical slice** | Executor core (code, decision, completion, agent, gate, wait; fixed default workflow; the `sections` key accepted and stored); `IDecisionModel` + Jev HTTP (all four dialects; the first live call from work settles OQ-2 and OQ-3) + standing-seat transport + chains + `llm_call` with cost basis; `ICompletionModel` over seats; `ICodeHost` GitHub (PAT) + local git + mirrors + cache layer; prompt resolution over all four layers (company → team → instance → repo) in `override` mode from the default folders, with the repo's layer team set by hand; **events**: the webhook receiver for the consumed set with stored deliveries (enterprise), polling at `codeHost.pollInterval` (home), and the Refresh API; intake by PR or commit range; categorisation with blast radius; pack builder with ledger; **one** reviewer (fixed check set); publish to the PR + commit statuses; Antiphon gate when there's no PR; the enterprise Kubernetes shape (server + Postgres sidecar + runner sidecars, per-id runner config); replay evaluation run; shadow run on Antiphon's own Review stage |
| **P2: parallel review and rounds** | Check selection, grouping, fan-out/join on runner seats; collate and verify; rounds policy and bypass categories (configurable, `security` and `data-loss` by default); needs-human-approval and bot approval, never with an open blocker; blocker clears with reasons, trust-gated against the neutral start (a high-trust instance clears alone; elsewhere a second member confirms in the review UI); gate deciders without the author, with the `always` override; recheck workflow; comment workflow (no fix); review runs page with the Refresh button and dollar cost display; review UI; JWT and trusted-header identity |
| **P3: people and editing** | Workflow editor and live overlay with collapsible sections and collapsed fan-outs; declarative merge modes, the lock and agent merge across the four layers, separate team and company source repos, and the layer-team default from CODEOWNERS; teams/orgs sync, many-to-many ownership, routing, queues, random assignment, author resolution; the `sole-owner` gate exception; **trust**: scores per scope with the nightly recompute, thresholds and weights as database settings with team overrides and history, adjustments with audit, the trust and settings pages, trust queue items, steer review, assignment eligibility; bypass routing to the owning team, or an override team; stats and rollups; runner shared-secret enrollment (join secret as a Kubernetes Secret) + doctor + custom image docs |
| **P4: learning and automation** | Learn loop (signals, proposals, promotion PRs to the team layer at 2 repos and the company layer at 2 teams, calibration, related-file tuning); the wide-scope confirmation step with preview, steer drafting and two-way learn-PR links (until then a wide-scope dismissal is stored with its scope and opens no PR); seeding as a product workflow with cursors; the base-moved check, `record-drift`, the author's update-branch opt-in, update-branch, base drift with `baseDrift.affectedThreshold`, and AI conflict resolution (until then an approval goes straight to `await-merge`); `@antiphon fix this`; GitHub App adapter (check runs); one-shot CLI transport; continue-session; ephemeral per-session containers |
| **Later** | Language and framework facet layers; a repo-level `sources.yaml` for further common repos |

The replay evaluation runs from P1 onward. Every workflow, prompt or check revision is scored on
the replay set (recall on known bugs, findings posted, cost) before activation. Revisions can also
be A/B'd in shadow on live PRs.

## 21. Open questions

| # | Question | Answer or recommendation |
|---|---|---|
| OQ-1 | Which LiteLLM version the enterprise proxy runs, and whether it configures `/v1/systemone` (alias `jev-latest`, as the example shows) or the `/typesafe` pass-through. | **Resolved 2026-11-21 (D-29).** `{proxy}/v1/systemone` on the latest LiteLLM, with a request shape identical to TypeSafe's. The adapter keeps the pass-through dialect. |
| OQ-2 | Exact answer field names. | The sanitised GH-58 fixture covers `choice`, `noul` and numeric `score`. See 7.3. |
| OQ-3 | How to send descriptions for choices and levels. | System One uses `criteria`. The adapter sends choice descriptions in the map and score descriptions in rubric order, using the label for an undescribed level. See 7.3. |
| OQ-4 | Webhook reachability at home and in the enterprise. | **Resolved 2026-11-21 (D-30).** Enterprise takes every webhook event. Home polls at a configurable interval, with a manual Refresh (8.5). |
| OQ-5 | Whether enterprise runs its own Antiphon instance (data residency) or a tenant of a shared one. | **Partly resolved 2026-11-21 (D-31).** Enterprise runs its own instance on Kubernetes (16.2). Still open: one instance per team, or one for several teams. |
| OQ-6 | Promotion thresholds N and M. | **Resolved 2026-11-21 (D-32).** 2 repos → team layer; 2 teams → company layer (language and framework facets later). The context-note part moved to OQ-15. |
| OQ-7 | Which repos make up the initial common prompt layer, and who owns it. | **Resolved 2026-11-22 (D-33).** The company and team layers exist from day 1, as `company/` and `teams/<team>/` in the instance-wide `.antiphon` unless the Org or Team entity names a separate source repo. Each is owned by whoever owns its folder or repo (CODEOWNERS on the instance-wide `.antiphon`). |
| OQ-8 | Where ABF legacy blocks end. | **Resolved 2026-11-21.** They do not end at a blank line. A block runs to the next line whose trimmed text starts with `---`, or to the closing report token, or to end of input, as `PipelineHandoff.cs` and `ReviewEvidence.cs` do. Since D-28 the legacy scan skips ABF documents. Per-type profiles (spec §6) give the same values as both parsers for every input except where a legacy line falls inside an ABF document; corpus `legacy/` pins both. |
| OQ-9 | Should a dismissal scoped to a path or repo show an inline confirmation ("This opens a PR on `<repo>`: …") before opening the learn PR? | **Resolved 2026-11-21 (D-34).** Always, for path, repo and team scope, with a preview, an optional steer and two-way links. Team scope queues a proposal until the 2-repo rule is met (11.7). |
| OQ-10 | Should `security` and `data-loss` findings bypass the round's posting bar, be immune to adjudicator concession, and route to a security group? | **Resolved 2026-11-21 (D-35).** Yes, as a configurable list with both on by default. Routing defaults to the owning team (changed 2026-11-22); another team, such as a security group, is an override (section 5). |
| OQ-11 | The cycle rule (D-2) was relaxed to "gate, wait, or a loop edge with an iteration limit". Accept? | **Resolved 2026-11-22 (D-2).** Accepted: a loop is valid if it passes through a gate, a wait or an iteration limit, and exceeding a limit always routes to a gate (3.2). |
| OQ-12 | `base-drift[affected]` does a delta re-review, and the approval is withdrawn until it passes again. Accept? | **Resolved 2026-11-22 (D-38).** Accepted. |
| OQ-13 | With `keepUpToDate: true`, a branch already behind the reviewed base takes `base-moved[unchanged]` and is never updated. | **Resolved 2026-11-22 (D-37).** With `keepUpToDate` on, `base-moved` compares the branch's merge-base with the base tip and emits `behind` or `moved` → `base-drift` → `update-branch`. With it off, it compares the reviewed base with the tip. The code host's "behind" status and always updating were rejected (4.12). |
| OQ-14 | Trust scale, neutral start, `trust.holdDays` and default threshold values (11.8, 11.9). | **Resolved 2026-11-22 (D-39).** A 0–100 scale; new users start at 60 in a high-trust instance and 40 otherwise; thresholds default to about 50; `holdDays` 90. |
| OQ-15 | The minimum evidence for a context note (from OQ-6). | Open. |

## 22. Follow-ups

### 22.1 Card claims

This design's phase cards are not filed until card claims exist (D-36). Without a claim, an
orchestrator's Backlog pickup could dispatch a card that a person or session is already working on.

- **Field.** `ClaimedBy`: a user or a session, with `ClaimExpiresAt`.
- **Shown on the card,** with the claimant and the time left.
- **Skipped** by orchestrator Backlog pickup and by dispatch while the claim is live.
- **Released** on expiry, or by hand (the claimant, or an operator).
- **Not `OwnerSessionId`.** The existing `Card.OwnerSessionId` means the card spawned and owns an
  agent session (`docs/agent-card-lifecycle.md:113,154`). It is not a claim and is left unchanged.

## 23. References

- [Antiphon Block Format v1](../../specs/block-format/v1.md) (draft): legacy blocks in §6 (OQ-8),
  document types in §7, and the [conformance corpus](../../specs/block-format/conformance/README.md)
- Review rounds: [docs/orchestration-loop.md](../../orchestration-loop.md#review-rounds) (CARD-1635)
- Review bundle: `server/Bundles/stage-review.md`
- Fast-track lane plan: [2026-11-20-fast-track-lane-plan.md](../../superpowers/plans/2026-11-20-fast-track-lane-plan.md)
- Why-missed template: [docs/investigations/why-missed-template.md](../../investigations/why-missed-template.md)
- Card spawn path and `OwnerSessionId`: [docs/agent-card-lifecycle.md](../../agent-card-lifecycle.md)
- Revisioned settings pattern: `server/Domain/Entities/DispatchConcurrencySettings.cs` (CARD-0505)
- server2 production cutover: [docs/docker-stack.md](../../docker-stack.md#production-server-on-the-server2-host-card-1744) (CARD-1744)
- TypeSafe System One API: https://docs.typesafe.ai/api/system-one.md
- LiteLLM Decisions: https://docs.litellm.ai/docs/decisions
- LiteLLM TypeSafe pass-through: https://docs.litellm.ai/docs/pass_through/typesafe
- OpenRouter Jev: https://openrouter.ai/docs/guides/community/jev.md
