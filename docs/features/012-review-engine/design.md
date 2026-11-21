# Review engine (design)

Status: draft for operator review, 2026-11-21. No card is bound yet; phase cards are filed after this
review. Companion spec: [Antiphon Block Format v1](../../specs/block-format/v1.md).

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

## Summary

1. **One engine, several workflows.** A graph executor stored in Postgres runs workflows defined in
   `.antiphon/review/workflow.yaml`. Node kinds are code, decision, completion, agent, fan-out, join,
   gate and wait. Every cycle passes through a gate or wait or is closed by a loop edge with an
   iteration limit, and every loop edge has one (3.2). The engine runs these workflows: PR review, recheck (the same graph in delta
   mode), comment handling, seeding, branch updates and author resolution.
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
   round 1. Bypass categories (`security`, `data-loss`) ignore the bar. After 3 bot-only rounds a
   human gate opens. A clean round goes to bot approval or human approval, as the categorisation
   decides.
7. **Humans.**
   - The Antiphon review UI posts back to the PR with attribution.
   - Queues exist per user, per team and overall. Routing uses CODEOWNERS, then random assignment
     among opted-in members other than the author.
   - Findings go to the resolved author.
   - Stats reward outcomes, not volume.
8. **Checks and prompts are files.**
   - One markdown file per check under `.antiphon/review/checks/`. `# @model:` sections go to the
     model; `# @humans:` and `# @maintainers:` sections never do.
   - Prompts come in layers (common repos → team → org → repo), recorded in a lock file. An agent
     merges real conflicts, and the result arrives as a PR.
9. **Learning.** Signals are dismissed, accepted, ignored and missed. They become proposal PRs at
   the narrowest scope, promoted to team, org or language when a pattern repeats. The same signals
   calibrate the fallback models' confidence and tune related-file sources.
10. **Phasing.** The first slice (P1) delivers Jev HTTP (enterprise) plus standing seats (home), the
    GitHub PAT host, one reviewer, status checks and a replay evaluation. The rest follows over P2–P4
    (section 20).

## Decisions

| # | Decision |
|---|---|
| D-1 | The server coordinates every workflow. Agents do the judgement work inside nodes and never move the graph. |
| D-2 | Workflows may be cyclic. Every cycle must pass through a `gate` or `wait` node or be closed by a loop edge with an iteration limit, and each loop edge has an iteration limit (3.2). |
| D-3 | Code hosts sit behind `ICodeHost`. v1 has GitHub with a personal access token for a machine user, plus local git. |
| D-4 | Typed decisions use `IDecisionModel` (Jev shape) and text uses `ICompletionModel`. The default decision chain is Jev → Luna → Haiku → Grok Low. |
| D-5 | Transports are HTTP and standing seat in P1. The one-shot CLI follows later. |
| D-6 | Machine-to-machine text uses ABF (`docs/specs/block-format/v1.md`), and is stored as canonical JSON. |
| D-7 | A check is a markdown file with `# @audience: Name` section markers. The prompt hash covers `@model` sections only. |
| D-8 | Review config is read from the **base** branch, never from the PR head. |
| D-9 | Findings post straight to the PR. With no PR, they appear in an Antiphon review gate. There is no triage-first mode. |
| D-10 | Status goes back through GitHub commit statuses, `antiphon/review` and `antiphon/approval`, because a PAT cannot create check runs. |
| D-11 | Who may dismiss a blocker is configurable. The default is the author (high trust). |
| D-12 | After approval the merge is left to the author by default. Auto-merge and merge are per-repo settings. |
| D-13 | A human can always send a PR to another round, with an optional steer prompt. |
| D-14 | v1 runners are long-lived custom images that phone home. Joining needs a shared secret, then the server issues each runner its own token automatically. |
| D-15 | Teams, orgs and memberships come from GitHub into Antiphon entities, reconciled by webhook or daily. Antiphon overrides win. |
| D-16 | The engine listens to every PR in scope. It acts on its own threads, mentions, issues raised by humans, and repo questions in human threads (after a grace period). |
| D-17 | `@antiphon fix this` (pushing fixes from a comment) is deferred to P4. |

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
| Usage | Per-task tokens and cost (`AgentTask.cs:519-536`, `DelegationUsageRollup.cs:22`). `/api/agent-tasks/summary`, `/api/audit/cost-summary`. | A ledger for direct calls; grouping by model. |
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
`maxIterations`; going over it routes to a configured gate. A run is long-lived (one per PR, kept
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

```yaml
workflow: pr-review
version: 1
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
  await-fixes:   { kind: wait, on: [push, recheck, close, base-moved], timeout: 14d, description: "Parks the run, with no seat held, until the author pushes fixes or someone asks for a recheck. Times out after 14 days." }
  base-drift:    { kind: decision, run: BaseDriftCheck, chain: decisions-default }  # outputs: affected | unaffected-approved | unaffected-open
  needs-human-approval: { kind: code, run: NeedsHumanApproval }  # outputs: yes | no
  bot-approve:   { kind: code, run: BotApprove }
  human-approve: { kind: gate, queue: owning-team, decisions: [approve, another-round, close] }
  human-review:  { kind: gate, queue: owning-team, decisions: [approve, another-round, close] }
  base-moved:    { kind: code, run: CheckBaseMoved }   # outputs: unchanged | moved; approval's base vs base tip, no model
  update-branch: { kind: code, run: UpdateBranch }     # after base-drift only; rebase or merge base in, AI conflict resolution
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
  - base-moved[moved] -> base-drift
  - update-branch -> await-merge
  - await-fixes[push|recheck] -> intake         # back edge, maxIterations: 20
  - await-merge[push] -> intake                 # back edge, maxIterations: 20; revokes the approval
  - await-fixes[base-moved] -> base-drift
  - await-merge[base-moved] -> base-drift
  - base-drift[affected] -> intake              # back edge, maxIterations: 10; carries a rebase steer
  - base-drift[unaffected-approved] -> update-branch   # approval carried forward (patch ids unchanged)
  - base-drift[unaffected-open] -> await-fixes
```

### 3.4 Execution

- Node executions are rows (`node_execution`): status, iteration, inputs hash, outputs (canonical
  JSON), model/provider/transport, tokens, cost, timings.
- The executor is a server service with leases, not Hangfire, which runs a single worker
  (`server/Program.cs:1021`).
- Agent nodes dispatch through the existing delegation path onto runner seats.
- Gate and wait nodes park the run with no seat held.
- SignalR events drive the live graph overlay and the status-check updates.
- A node whose inputs hash matches a completed execution from an earlier iteration is reused.
  This is how a recheck skips unchanged work.

### 3.5 Other workflows on the same engine

| Workflow | Trigger | Shape |
|---|---|---|
| `pr-review` | PR opened or marked ready, repo in scope, manual | 3.3 above |
| recheck | push, `/recheck`, UI button, `await-fixes`, `base-drift[affected]` | the same graph; `delta` limits work to the change since the last reviewed SHA (4.2) |
| `comment` | a human response: PR comment, thread reply, review submission, gate decision | section 6 |
| `seed` | manual or scheduled per source | section 14 |
| `update-branch` | the base moved under an approved PR (`base-moved` after the approval, or `await-merge`) and `base-drift` says it does not affect the PR; conflicts | sections 4.12, 4.13 |
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
| Gate decision | event | `human-approve`, `human-review` | intake (with the steer), base-moved | decider authorised and not the author; decision allowed by the gate | `workflow.yaml` gate nodes |
| Wait event | event | `await-fixes`, `await-merge` | intake, base-drift | back edge under `maxIterations` | `workflow.yaml` `await-fixes`, `await-merge` |
| Base-drift assessment | decision | `base-drift` | intake (with the rebase steer), update-branch, await-fixes | code-only prefilter before any model call; an affected verdict lists the overlapping commits and files | `workflow.yaml` `base-drift`; `.antiphon/review/questions/base-drift.yaml` |
| Approval record | code-host object | `bot-approve`, gates | base-moved, update-branch, await-merge | head equals the reviewed SHA; records the base it was reviewed against | `policy.yaml` `approval.autoApproveRequires` |
| Base-tip check | JSON record | `base-moved` | base-drift, await-merge | base ref fetched after the approval; exactly one output, `unchanged` only when the approved base equals the tip | `workflow.yaml` `base-moved` |
| Branch update | event | `update-branch` | await-merge | approval carried only when `base-drift` said unaffected and patch ids are unchanged; `not-needed` only when `keepUpToDate` is off and the head merges cleanly | `policy.yaml` `afterApproval` |

## 4. Review stages

### 4.1 Intake

- Input is a PR (`ICodeHost`) or a commit pair (local-git host). Base and head SHAs are pinned.
- The repo mirror is fetched, along with the PR refs. Code comes from git; only conversation comes
  from the API (section 8).
- Policy, checks and workflow are read **at the base SHA** (D-8).
- Code-only tags: languages, `antiphon.areas.json` areas, path-glob classes, size bucket,
  generated-file detection, CODEOWNERS owners of the changed paths.

### 4.2 Delta (recheck)

1. **Find the change.** The diff runs from the last reviewed head to the new head.
   - After a force-push or rebase the old head may no longer be reachable. Then compare the two
     trees and match commits by patch id (an interdiff).
   - A base-only update leaves the PR's own patch ids unchanged, and is marked as such. Whether
     it needs a new review is decided by `base-drift` (4.13), not here.
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
  with a merge method.
- **Checking the base after approval:** every approval (`bot-approve`, or `approve` at
  `human-approve` or `human-review`) goes to `base-moved`. This code step makes no model call. It
  fetches the base ref and compares the base the approval covered (pinned at intake for the approved
  iteration) with the current base tip.
  - `unchanged` → `await-merge`, with no update and no model call.
  - `moved` → `base-drift` (4.13), which decides whether the move affects the PR.

  A human gate can stay open for days, so a move is common on the human path. On the bot path the
  check catches a base that moved during the round.
- **Waiting for the merge:** an approved PR parks on `await-merge` (no seat held) until it is merged
  or closed. A push revokes the approval and starts a new iteration; a base move goes to `base-drift`
  (4.13).
- **Keeping the branch up to date:** `update-branch` runs only when `base-drift` says a base move does
  not affect an approved PR, whether the move was found by `base-moved` or during `await-merge`. It
  brings the branch up to date by merge or rebase, per repo.
- **`keepUpToDate: false`:** `update-branch` does not merge the base in. It checks that the head still
  merges cleanly into the new base, records `not-needed` and goes on to `await-merge` without
  pushing, so the approval stands. Only conflicts make it update the branch. This stays inside
  `update-branch` rather than adding a `base-drift → await-merge` edge, so the decision node never
  reads merge policy or runs a merge check.
- **Conflicts:** an agent resolves them on a runner and pushes. Only the conflict hunks are rechecked.
- **Carrying the approval forward:** the approval moves to the new head without a new round only if
  `base-drift` said unaffected and the PR's own patch ids are unchanged. Any other push revokes it.

### 4.13 Base drift

When the base branch moves under an open or approved PR, both waits (`await-fixes`, `await-merge`)
send a `base-moved` event to `base-drift`, a cheap decision node. The `base-moved` check after an
approval (4.12) enters the same node through `base-moved[moved]`, when the base moved between the
reviewed base and the approval:

1. **Code-only prefilter.** Do the base commits since the reviewed base touch the PR's changed files,
   its related files (impact index, callers of changed symbols), or the contract and schema globs?
   No overlap means `unaffected`, with no model call.
2. **One Jev call on overlap.** A single yes/no question, "Could these base changes affect this PR?",
   with the base commit summaries and the overlapping files as state. The threshold is deliberately
   low: a false re-review is cheaper than a missed semantic conflict. A low-confidence answer
   escalates up the chain like any other decision.
3. **Route.**
   - `affected` → back to intake for a re-review in delta mode (back edge, maxIterations 10; the
     recheck row in 3.5). An approval is withdrawn until the re-review approves again. Every pack
     gets an automatic steer: "Base moved `<old>..<new>`: review as if rebased onto the new base;
     check interactions with these commits/files: …".
   - `unaffected-approved` → `update-branch` (4.12), and the approval carries forward when the PR's
     patch ids are unchanged.
   - `unaffected-open` → back to `await-fixes`.

The assessment (base range, commits, overlap, prefilter result, probability, verdict, steer) is
stored as an artifact (3.6) and shown on the run page. Prefilter sources, contract globs, the
threshold and the steer template are per-repo parameters of the node.

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
  bypassCategories: [security, data-loss]   # posted in any round; the adjudicator cannot concede them alone
severities: [nit, minor, major, blocker]
dismiss:
  blocker: author                       # author | other-member | maintainer
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
  goes into the shared prefix of every pack ("Human steer from <name>: …"). Steers that keep
  recurring become candidate checks (section 13).

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
| Dismiss / by design / won't fix | Resolve the thread, store the reason and scope, emit a learn signal |
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
- **High-stakes actions** need an authorised commenter and high confidence: dismissing a blocker
  where policy demands it, proposing a learning for a whole repo or more, or any write to the
  branch. Otherwise Antiphon asks for confirmation ("Resolve F3 as by-design for this repo? Reply
  `/antiphon yes`") or routes the item to a gate.
- **Locking.** Actions take a lock on the finding record, and queue while a review iteration is in
  flight.
- **Antiphon first, then the code host.** A decision made in Antiphon on a finding that has a
  code-host thread posts the reply to the thread, then resolves the thread, then closes the finding
  locally. If posting fails, the finding is held `pending-sync` and retried. It is never closed in
  Antiphon only.

**Reason categories:** false positive, by design, out of scope, accepted risk, fix later, duplicate,
style preference.

**Scopes:** this instance, this path, this repo, suggest to the team.

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
| One-shot CLI | P4 | `claude -p --output-format json`, `codex exec`, Grok equivalent. |

Decisions over a chat model (seat or HTTP) ask for `decision-response/1` with a probability per
option. Those probabilities are **not calibrated**; see 7.5.

### 7.3 Jev dialects

The request body is the same for every dialect: `{ model, state, questions }`. Question types are:
- `noul`: `instructions`
- `choice`: `instructions`, `choices: [{value}]`
- `score`: `instructions`, `levels: [{label}]`

Optional description fields on choices and levels are to be confirmed from TypeSafe's OpenAPI spec.

| Dialect | Endpoint | `model` | Errors | Cost |
|---|---|---|---|---|
| `litellm-systemone` (enterprise default) | `{proxy}/v1/systemone` | proxy alias, e.g. `jev-latest` | LiteLLM `{"error":{...}}` envelope; pre-validation 400s; a missing answer key = refusal | `x-litellm-response-cost` header |
| `litellm-passthrough` | `{proxy}/typesafe/v1/systemone` | upstream model | TypeSafe native | price table × `usage.input_tokens` |
| `typesafe-native` | `https://api.typesafe.ai/v1/systemone` | `jev-1.14.0` (pin) or alias | 400/401/413/415/422; retry 429/529 | price table ($0.042 per million input tokens, output free) |
| `openrouter-systemone` | `https://openrouter.ai/api/v1/systemone` | native | native | price table, or OpenRouter usage cost |

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
- **Escalate by confidence.** An answer below its question's per-provider threshold escalates
  **that question only** to the next entry. A refused answer escalates the same way.
- **Record every call** in `llm_call`: chain, entry, provider, transport, resolved model, tokens,
  cost, latency, outcome.

### 7.5 Calibration

Jev's probabilities are calibrated; the chat fallbacks' are not.
- A sample of decisions runs Jev and one fallback side by side (shadow).
- Labelled outcomes from the learn stage are added.
- A calibration map is fitted per (provider, resolved model, question type).

Thresholds are set per provider. TypeSafe's examples (0.3 and 0.5) are illustrations, not
defaults.

## 8. Code host

### 8.1 `ICodeHost`

It covers:
- PR metadata and commits; compare; file at a ref
- threads, comments and reactions; post a review with many comments; reply; resolve a thread
- commit statuses
- the authenticated identity
- org, team and membership listing; CODEOWNERS
- webhook verification and translation into a common event model (`PrOpened`, `PrUpdated`,
  `Pushed`, `CommentCreated`, `ReviewSubmitted`, `ThreadResolved`, `Merged`, `Closed`,
  `MembershipChanged`)

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
  the adapter polls through the cache layer.

### 8.4 Mirrors and caching

- **Mirrors.** The server keeps a bare mirror per registered repo, and runners fetch from the
  server's mirror. Diffs and file content come from git, not the API.
- **Cache layer.** A wrapper around any adapter. Content addressed by SHA is cached forever; other
  responses use ETags. It keeps a per-host rate-limit budget.
- **Base URL.** Configurable, for GitHub Enterprise Server and company proxies.

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

```
.antiphon/review/
  sources.yaml      # ordered layers: common repo(s)@ref → org → team → this repo
  sources.lock      # per path: each layer's source, commit and content hash; merged hash; method; merged-by
  policy.yaml  workflow.yaml  related.yaml
  checks/  groups/  prompts/
  resolved/         # agent-merged outputs, referenced by the lock
```

- **Merge modes.**
  - `override`, the default: the highest layer wins.
  - Declarative, set in the file's header: `extends`, `append`, `patch`.
  - **Agent merge** for real conflicts only. It writes `resolved/<path>` plus a lock entry, and runs
    again only when an input hash changes. Lock changes arrive as a PR for human approval.
- **Provenance.** Every run stamps the workflow revision hash and every prompt hash, following the
  existing `[bundle:key vHASH]` convention. The UI shows each prompt's final text and which layer
  each part came from.
- **Team and org layers.** These come from the Team and Org entities (section 12): each entity
  records its prompt source repo. A repo's `sources.yaml` can name them as `team:` or `org:` instead
  of URLs.
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
  with free text.
- **Submit** posts one code-host review (one notification). Thread replies follow the "Antiphon
  first, then the code host" rule (section 6).
- **Attribution.** The machine user posts, signed "— <display name> via Antiphon". This uses the
  name, not an @mention, so nobody is pinged; that's configurable.
- **Gate decisions** (approve, another round with a steer, close) happen on the same screen.

### 11.3 Queues

- **Mine:** assigned to me, plus unassigned items from teams I've selected.
- **Team** and **overall**.
- **Item kinds:** review gate, approval gate, escalated dispute, prompt-merge approval, seed
  candidate, learn proposal.
- **Item states:** unassigned → assigned → in progress → done, or passed back.
- **"Not me"** returns the item to the team queue with a reason, and excludes that person from the
  next pick.
- **SLA ageing** is measured in working hours. Notifications go out through the existing channels
  (Telegram) and the attention feed.

### 11.4 Routing

1. Changed paths are matched against CODEOWNERS to find the owning teams. The primary team is the one
   with the most changed lines; the others get read-only visibility.
2. If the team has opted in, a reviewer is picked at random from the opted-in members. The pick is
   weighted by open load and excludes the author and anyone who passed.
3. Findings go to the **author's** queue as well as the PR.

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

### 11.6 Dismissals and incidents

- `dismiss.blocker` is configurable; the default is `author` (D-11).
- The Debug and why-missed investigation templates (including Antiphon's own
  `docs/investigations/why-missed-template.md`) gain a step: "search Antiphon's finding history for
  dismissed findings on the code involved, and link them".
- A linked dismissal is marked **wrong**. That counts against the person who dismissed it in the
  stats, raises the check's weight, and queues the dismissal reason for review.

## 12. Teams and orgs

- **Entities:**
  - `Org`
  - `Team`, with a parent team, prompt source repo, working-hours calendar and auto-assign setting
  - `Membership`, with an auto-assign opt-in and a role
  - `Identity`, mapping code-host login, email and user, with a service-account flag
  - `RepoOwnership`, from CODEOWNERS
- **Sync:** code-host org, team and membership data maps in. It is reconciled by webhook
  (membership events) where reachable, otherwise daily using conditional requests. Manual overrides
  in Antiphon take precedence.
- **Uses:** learning scopes (repo → team → org, plus language and framework facets), queue routing,
  stats and prompt layers all read these entities.

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

**Promotion** is by repetition:

| The same normalised adjustment seen in | Proposed to |
|---|---|
| ≥N repos of one team | that team's prompt repo |
| ≥M teams | the org or company prompt repo |
| repos sharing a language or framework rather than a team | a language/framework layer |

Each proposal links to its evidence threads. A promoted rule that repos keep overriding is flagged
for demotion. Low-volume repos borrow team-level statistics until they have enough data of their
own.

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
   repo and org.
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
| Per PR / team | Rounds per PR; time from open to bot-clean to human approval to merge; bot precision; cost per PR; **cost per accepted finding** |

Leaderboards are per team and per month, and **rank outcomes, not volume**. Words never score on
their own.

## 16. Runners

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

## 17. Visibility (UI)

| Page | Contents |
|---|---|
| Review runs | PRs by stage, the number in review, queue depth, models and providers in use, tokens and cost per run, cost per accepted finding |
| Run detail | The live workflow graph overlay (status, iteration, model, time, tokens per node), findings, rounds, statuses posted |
| Workflow editor | `@xyflow/react` + `elkjs`. Back edges are drawn as loops with iteration counts. Saving validates the cycle rule and produces a revision or PR. The graph is read-only: changes are made by AI prompt into a staging area (preview, diff, validation, replay estimate) before saving. Each node's description and typed parameters are edited in an inspector and staged the same way |
| Pack viewer | Per reviewer: what was loaded and why, and what it opened that the pack missed |
| Checks | Selection rate, precision per scope, last hit, shadow candidates, noisy checks to retire |
| Queues / review UI | Section 11 |
| Stats | Section 15 |
| Providers | Calls, cost, escalations and fallbacks per chain entry; Jev vs subscription quota |

Live data arrives through `useSignalRInvalidation` entries. A by-model grouping is added for agent
tasks and `llm_call`.

## 18. Data model (new tables)

| Area | Tables |
|---|---|
| Runs | `review_subject` (host, repo, PR or commit range), `review_run`, `review_iteration`, `workflow_definition`, `workflow_revision`, `node_execution` |
| Findings | `finding`, `finding_event`, `finding_thread_link`, `human_finding` |
| Context | `context_item` (ledger), `pack` (hash, staged path) |
| Models | `llm_call`, `decision_cache`, `calibration_map`, `provider_chain` |
| Checks | `check_index` (path, hashes, embedding), `check_stat` (per scope) |
| Prompts | `prompt_source`, `prompt_lock_entry` |
| People | `org`, `team`, `membership`, `identity`, `repo_ownership`, `work_calendar` |
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
- Each tenant has a setting for whether code may be sent to external APIs (enterprise plans can
  offer zero data retention).
- The machine user has least-privilege token permissions (8.3). Secrets follow
  `docs/agent-credentials.md`, and are never printed or logged.

## 20. Phasing

| Phase | Delivers |
|---|---|
| **P0: specs and seed** | ABF v1 spec + conformance corpus + C#/TS parsers; check file format; `.antiphon/review/` layout, gitignore exception, footprint and slot-wipe fixes; data model; seed ~100 checks with a manually run seeding agent (why-missed docs, CARD-1809 R1–R5); replay set of ~20 why-missed cases (bug + fix commits) |
| **P1: thin vertical slice** | Executor core (code, decision, completion, agent, gate, wait; fixed default workflow); `IDecisionModel` + Jev HTTP (all four dialects) + standing-seat transport + chains + `llm_call`; `ICompletionModel` over seats; `ICodeHost` GitHub (PAT) + local git + mirrors + cache layer; intake by PR or commit range; categorisation with blast radius; pack builder with ledger; **one** reviewer (fixed check set); publish to the PR + commit statuses; Antiphon gate when there's no PR; replay evaluation run; shadow run on Antiphon's own Review stage |
| **P2: parallel review and rounds** | Check selection, grouping, fan-out/join on runner seats; collate and verify; rounds policy and bypass categories; needs-human-approval and bot approval; recheck workflow; comment workflow (no fix); review runs page; review UI; JWT and trusted-header identity |
| **P3: people and editing** | Workflow editor and live overlay; prompt layers, lock and agent merge; teams/orgs sync, CODEOWNERS routing, queues, random assignment, author resolution; stats and rollups; runner shared-secret enrollment + doctor + custom image docs |
| **P4: learning and automation** | Learn loop (signals, proposals, promotion, calibration, related-file tuning); seeding as a product workflow with cursors; the base-moved check, update-branch, base drift and AI conflict resolution (until then an approval goes straight to `await-merge`); `@antiphon fix this`; GitHub App adapter (check runs); one-shot CLI transport; continue-session; ephemeral per-session containers |

The replay evaluation runs from P1 onward. Every workflow, prompt or check revision is scored on
the replay set (recall on known bugs, findings posted, cost) before activation. Revisions can also
be A/B'd in shadow on live PRs.

## 21. Open questions

| # | Question |
|---|---|
| OQ-1 | Which LiteLLM version the enterprise proxy runs, and whether it configures `/v1/systemone` (alias `jev-latest`, as the example shows) or the `/typesafe` pass-through. The adapter supports both. |
| OQ-2 | Exact answer field names. They were checked against LiteLLM's code, not TypeSafe's OpenAPI. A P1 live smoke call captures a sanitised fixture. |
| OQ-3 | Whether choices and levels accept a description field (it improves question quality). |
| OQ-4 | Webhook reachability at home and in the enterprise. Otherwise polling through the cache layer. |
| OQ-5 | Whether enterprise runs its own Antiphon instance (data residency) or a tenant of a shared one. |
| OQ-6 | Promotion thresholds N and M, and the minimum evidence for a context note. |
| OQ-7 | Which repos make up the initial common prompt layer, and who owns it. |
| OQ-8 | **Resolved 2026-11-21.** ABF legacy blocks do not end at a blank line. A block runs to the next line whose trimmed text starts with `---`, or to the closing report token, or to end of input, as `PipelineHandoff.cs` and `ReviewEvidence.cs` do. Per-type profiles (spec §6) give the same values as both parsers for every input; corpus `legacy/` pins it. |

## 22. References

- [Antiphon Block Format v1](../../specs/block-format/v1.md): legacy blocks in §6 (OQ-8), document
  types in §7, and the [conformance corpus](../../specs/block-format/conformance/README.md)
- Review rounds: [docs/orchestration-loop.md](../../orchestration-loop.md#review-rounds) (CARD-1635)
- Review bundle: `server/Bundles/stage-review.md`
- Fast-track lane plan: [2026-11-20-fast-track-lane-plan.md](../../superpowers/plans/2026-11-20-fast-track-lane-plan.md)
- Why-missed template: [docs/investigations/why-missed-template.md](../../investigations/why-missed-template.md)
- TypeSafe System One API: https://docs.typesafe.ai/api/system-one.md
- LiteLLM Decisions: https://docs.litellm.ai/docs/decisions
- LiteLLM TypeSafe pass-through: https://docs.litellm.ai/docs/pass_through/typesafe
- OpenRouter Jev: https://openrouter.ai/docs/guides/community/jev.md
