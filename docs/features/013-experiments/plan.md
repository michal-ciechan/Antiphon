# Experiments: A/B testing of workflow stages (plan)

Status: plan, 2026-11-24. Nothing here is built yet. It extends the review engine
([design](../012-review-engine/design.md)) and uses the `eval_case` / `eval_run` tables and the replay
set that design already reserves (sections 18 and 20).

## Goal

Make "does this setting, prompt or model make a stage better?" a question anyone can answer from the
run page, with evidence:

- Replay one stage (or a short chain of stages) of a recorded run with different settings, and
  compare the result with the original and with what was expected.
- Compare models and agents on the same stage: categorisation, check selection, a reviewer, verify.
- Let people write their own scenarios with pass criteria: a script, a prompt, or a combination.
- Feed the self-improvement loop: when a human catches something the bot missed, turn it into a
  case, and accept a prompt or check change only when it fixes the case without being specific to it.
- Keep history: every experiment, its inputs and its results stay viewable and can be run again.

## Terms

| Term | Meaning |
|---|---|
| **Case** | One recorded situation that can be replayed: the frozen inputs of a stage, the original output, why the case exists, and what to expect |
| **Expectation** | A pass criterion on a trial's output. A case has one or more |
| **Variant** | A named set of overrides: model or chain, prompt text, checks, policy values, agent kind and level |
| **Suite** | A named list of cases, with a tuning part and a held-out part |
| **Experiment** | Cases × variants, for any selection of steps |
| **Trial** | One execution of one case under one variant. It stores the output, cost, time and each expectation's result |
| **Baseline** | The variant "as recorded" (the original run) or "current settings". Every experiment has one |

## What is missing today

A stage cannot be replayed today. The code map (2026-11-24) found:

1. **Inputs are not frozen.** Most steps re-read the diff, file list, PR description, checks and
   files from the code host on every walk.
2. **Prompts are C# strings.** The reviewer brief, pack prefix and suffix, verify instructions and
   categorise questions cannot be overridden without a build, and the text sent is not stored.
3. **Raw answers are not stored**, only the parsed `OutputJson`. Per-call records (`llm_call`) are
   dropped.
4. **Settings in force are not stored**: policy values, chains, agent levels and models, CLI version,
   each check's `PromptHash`.
5. **Steps write shared state**: `ReviewRuns.Categories` and `Blast*`, finding statuses, PR comments
   and statuses. A replay needs a sandbox.
6. **No ground truth.** Human inline comments on the PR are not read back, and "missed" findings
   exist only in the design.
7. **The node panel shows no inputs, output or prompt**, and nodes have no actions.

Phases 1 and 2 close these. They are worth having without experiments: they make every run
explainable.

## Design

### 1. Recording (what makes a run replayable)

- **Step inputs go through one interface.** Each step reads the diff, files, checks, policy and
  prompts through `IStepInputs`. The live implementation reads from the code host and settings and
  records what it returned. The replay implementation serves the recorded values and nothing else.
- **Content-addressed storage.** A `review_blob` table (SHA-256, compressed bytes, size, first seen)
  holds diffs, file contents, the resolved check catalogue, prompt text, raw answers and packs.
  `ReviewNodeExecution` gains `InputsJson`: a manifest of named inputs, each a blob hash or a small
  inline value, plus what it was used for. `InputsHash` is the hash of that manifest and is set on
  every node, not just packs.
- **Prompts become named, versioned text.** A prompt registry (`reviewer.brief`, `pack.prefix`,
  `pack.suffix`, `verify.instructions`, `categorise.questions`, `select.instructions`, …) with the
  current C# text as the built-in default. Each node execution records the prompt keys, hashes and
  the final rendered text. This is the first step of design section 10 and is what a variant
  overrides.
- **Settings snapshot.** Each run stores the effective policy, chains, agent settings and CLI
  version once per iteration (a blob), and each node references it.
- **`llm_call` rows** (design 7.4) for every model call: provider, resolved model, tokens, cost,
  latency, outcome, request and response blob hashes.
- **Workspace record.** Every step that runs against a checkout records enough to rebuild it
  (section 2a). A case pins its commits in the mirror under `refs/antiphon/cases/<case-id>/…` so
  they survive pruning.
- **Retention.** Blobs referenced by a case are kept until the case is deleted. Others follow the
  run's retention.

### 2. Sandboxed replay

- A replay creates an **experiment run**: a `ReviewRun` flagged `Sandbox`, linked to its trial, never
  listed with real runs, never polled.
- The sandbox code host serves reads from the case and **captures writes** (review, comments,
  statuses, approvals) as trial output without sending them.
- **Scope is any selection of steps, from the whole workflow down to one step** (section 6a). Steps
  outside the selection are served from the case or left out. `publish` in a sandbox produces "what
  would have been posted".
- Run state a step reads (categories, blast radius, open findings, steer, round number) is part of
  the case, and a sandbox run starts with a copy of it.
- Trials go through the normal executor, leases, build slots and delegation limits. Agent trials
  are delegate tasks with a `Sandbox` tag, so they count against host budgets.
- **Reuse.** A trial is keyed by (case inputs hash, variant hash, sample number). An identical key
  reuses the stored trial unless the person asks for fresh samples.

### 2a. A clean workspace per run, one run per container

Decided 2026-11-24: every scenario run gets its own clean checkout that looks like the original
run's, and every run has its own container.

**Recorded on the original run** (the workspace record, stored with the step's inputs):

| Item | Why |
|---|---|
| Repository (host, `owner/name`, clone URL) | Where the commits come from |
| **Parent:** the commit the worktree was cut from, and the branch or ref it was on | The starting point |
| **Worktree:** the checked-out commit, branch name or detached, and its absolute path | What the step saw, and where |
| Base commit and merge-base, for a PR | Diffs resolve the same way |
| Uncommitted state: a patch of tracked changes and an archive of untracked, non-ignored files | Pipeline stages often start from a dirty worktree; review steps normally have none |
| Submodule commits, sparse-checkout patterns, LFS use | The tree matches file for file |
| How it was cut (fresh worktree, fetched `refs/pull/<n>/head`, shallow or full) | The same history depth is visible |
| Container image digest, runner labels, agent CLI version, tool versions | The same environment |
| Environment names the step relied on (never values) | The run can say what is missing |

The patch and archive are blobs. Secrets and ignored files are never captured.

**Recreated for each run**

- A new container from the recorded image digest (or the variant's image, when the image is what is
  being compared).
- The repository is cloned from the mirror at the pinned commits, and the worktree is created **at
  the same path** as the original, on the same branch name, cut from the same parent, with the
  uncommitted state applied. Paths in packs, prompts and tool output then match the original.
- The run starts from a verified state: the commit, the tree hash and the uncommitted-state hash
  must equal the record, or the trial fails as "workspace mismatch" before any model call.
- No network access to the code host for writes; the sandbox code host is the only one reachable.

**One run per container**

- A container holds exactly one trial and is never reused, so one run cannot see another's files,
  caches, agent memory or session history.
- Variants of the same case run in separate containers side by side, within the host's limits.
- When the trial ends, its outputs (report, findings, files changed as a patch, transcript) are
  stored as blobs and the container is removed. **Keep workspace** on a failed trial holds it for a
  set time for inspection.
- This uses the per-session containers the design already plans (phase P4, "ephemeral per-session
  containers") and the server2 runner image. Decision and completion steps make no checkout and need
  no container; they run in the server's sandbox.

### 3. Cases

A case records enough to replay cleanly and to understand later:

| Field | Content |
|---|---|
| Origin | The run, iteration and node it came from, the PR link, who captured it and when |
| Context | A title and "why this case exists" in plain words, with links: the human comment that caught the miss, the dismissal, the incident |
| Inputs | The manifest of frozen inputs for the first targeted step, the run state, and the workspace record (section 2a) |
| Original | The original output of each node in scope, and the settings and prompts in force |
| Expectations | One or more (below) |
| Tags | Stage, language, category, "missed", "false-positive", "regression" |

**Ways to create one**

1. **From the graph:** node panel → **Save as case**. Prefilled; the person writes the why and picks
   the expectations.
2. **From a finding:** "The bot should not have raised this" → a case on the reviewer (or verify)
   node with a `finding-absent` expectation.
3. **From a miss:** human inline review comments are read back from the code host and stored as
   human findings (design `human_finding`). Each one that no bot finding matched shows a **Bot
   missed this** action → a case with a `finding-present` expectation at that file and lines, with
   the comment as the description of what to find.
4. **From a file:** `.antiphon/review/scenarios/<name>.yaml` in the repo or instance layer (below).
5. **From a commit pair** (bug commit + fix commit), as the seeding design already plans.

### 4. Expectations (pass criteria)

| Kind | Passes when |
|---|---|
| `unchanged` | The output is equivalent to the original. Findings are matched by location and meaning, not by wording |
| `finding-present` | A finding matches: file, line range (with tolerance), optional check id or category, and a description judged for meaning |
| `finding-absent` | No finding matches that description or location |
| `field` | A field of the output satisfies a comparison: `categories contains security`, `blast.reach >= 2`, `selected contains SEED-0003`, `route == clean` |
| `count` / `budget` | Finding count, cost, tokens or duration is within a bound |
| `script` | A script gets the trial output and the case as JSON on stdin and exits 0. It prints an optional reason |
| `prompt` | A judge model answers a rubric with pass or fail and a reason. The judge's model is fixed per suite and recorded |
| `all` / `any` / `not` | Combinations of the above |

- Matching "the same finding" is one shared matcher (location overlap first, then a cheap model for
  meaning), used by `unchanged`, `finding-present` and the diff view. It gets its own tests.
- A finding that matches nothing in the original or the expectations is **new and unjudged**. The
  view asks a person to mark it good or noise, and that answer becomes an expectation on the case.
- Scripts come only from the instance layer or the base branch, and run on a runner, never in the
  server process.

### 5. Variants

A variant overrides any of:

- the chain or a single provider and model for a decision or completion node;
- the agent kind, level, model and transport for the reviewer;
- a prompt by key (full text or a patch on the current text);
- the checks in play (add, remove, replace a check's text);
- policy values (thresholds, posting bar, group sizes);
- a whole workflow revision, for chain-scoped experiments.

Variants are saved and named ("opus reviewer", "haiku verify, stricter rubric"), so the same one can
be run against new cases. A variant records the exact resolved values each trial used.

### 6. Experiments and scoring

- An experiment is: targets (section 6a), cases or a suite, variants including a baseline. Each
  cell runs **once** by default (decided 2026-11-24). The same case with the same settings is never
  run twice: its stored trial is reused (section 2). **Repeats are optional**, for one purpose: a
  model can answer differently to the same input, so a person who doubts a result can ask for more
  samples of that cell (**Run again**) or set repeats on the experiment.
- **Before starting**, the page shows the trial count and an estimated cost from the cases' original
  usage, and a budget cap. Starting needs an explicit confirmation of the spend.
- **Per cell:** pass or fail, cost and time. A cell with several samples shows passes out of samples
  ("2/3"), and one that both passes and fails is marked flaky, not averaged away.
- **Per variant:** cases passed, cases regressed against the baseline, recall on `finding-present`,
  noise (unjudged new findings and `finding-absent` failures), cost and time. Counts are shown as
  counts; a percentage appears only with its sample size.
- A variant "wins" only as a statement of what was measured: "passes 14/16, baseline 11/16, no
  regressions, cost +20%". No single score hides the trade-off.

### 6a. Targeting steps and per-run instructions

**Targets: from many steps to one.** An experiment names the steps it exercises:

| Target | Example | What runs |
|---|---|---|
| The whole workflow | intake → route | Every step, in the sandbox |
| A section | "Review" | Its steps; earlier outputs come from the case |
| A chain | `categorise` → `verify` | Those steps in order, each fed by the one before |
| Several separate steps | `categorise` and `verify` | Each replayed alone from its recorded inputs |
| One step | `verify` | That step only |
| One item of a fan-out | reviewer group `correctness` | That reviewer only |

- Steps are picked on the graph (click, shift-click, or a section header), or listed in a scenario
  file as `targets:`.
- For each step not targeted, the run says what happens to it: **recorded** (serve the original
  output, the default for steps before a target), **rerun** (run it with current settings, the
  default for steps between two targets of a chain), or **skip** (the default for steps after the
  last target).
- Expectations attach to a step (`at: verify`) or to the end of the run. Results and the comparison
  view are per step, so a chain shows where two variants first diverge.

**Instructions per run.** Every run of an experiment carries instructions, recorded with it:

- **Per step settings:** the variant's overrides are given per targeted step (this model for
  `categorise`, this prompt for `reviewer`, that threshold for `verify`).
- **Per step instruction text:** optional plain words added to that step's prompt for this run only,
  like a gate's steer ("pay particular attention to concurrency"). It is stored as part of the
  variant, so it shows in the prompt diff and counts in the generality check.
- **Run note:** what this run is for and what to look at ("does Haiku still catch the swapped
  operands when the pack is half the size?"). It is shown on the history page and the A/B view, and
  is never sent to a model.
- **Stop rule:** run to the end of the targets, stop at the first failed expectation, or stop at a
  cost bound.

The same experiment can be run again with different instructions; each run is its own history row.

### 7. Self-improvement without overfitting

The learn loop (design section 13) proposes a prompt, check or context-note change. Experiments are
its gate:

1. A miss or a wrong finding becomes a **target case**.
2. The loop drafts a candidate change as a variant.
3. The candidate must pass, in order:
   - the target case (once by default; the proposal says how many samples it was run with);
   - the **regression suite** for that stage: no case that passed under the baseline fails;
   - the **held-out** part of the suite, which the drafting agent never sees;
   - a **generality check**: the changed text names no file, identifier, PR or repo from the target
     case; the change is a rule, not an example of this one case; and its size is within a limit.
     A lint does the first part and a judge prompt the second.
4. Among passing candidates the loop prefers the one that fixes the most open target cases with the
   smallest text change, and prefers the widest scope that still passes (a general reviewer rule
   over a per-repo check).
5. The proposal PR links the experiment as its evidence. A human still approves it.

Suites split tuning and held-out by case, stably (by case id), and the held-out part is refreshed
with new cases over time so candidates cannot slowly fit it.

### 8. UI

**From the graph**

- The node side panel gains tabs: **Inputs**, **Output**, **Prompt** (final text with the layer or
  override each part came from), **Calls**, and **Experiments**.
- Node actions: **Save as case**, **Try another setting** (opens a new experiment with this run and
  node as the case and "as recorded" as the baseline), and **Experiments (n)** when any exist for
  this node. A small badge on the node shows that count.

**Pages**

| Route | Shows |
|---|---|
| `/experiments` | History: every experiment with its scope, suite, variants, result line, cost, who ran it and when. Filters by stage, repo and model |
| `/experiments/new` | Targets and per-step instructions (section 6a), cases or suite, variants, repeats, the cost estimate and the confirmation |
| `/experiments/:id` | The A/B view |
| `/experiments/cases`, `/cases/:id` | Cases: context, origin, inputs, expectations, and every trial ever run on the case |
| `/experiments/suites`, `/variants` | Saved suites and variants |

A nav entry "Experiments" sits beside "Reviews".

**The A/B view**

- A grid: cases down, variants across, the baseline first. Each cell shows pass count, cost and
  time, coloured by result with an icon and text as well as colour.
- A summary row per variant (section 6), and a "differences only" filter.
- Clicking a cell opens a side-by-side comparison with the original (or any other variant):
  - findings in three groups: matched, missing, new;
  - a field-by-field diff for decision outputs (categories, selections with probabilities);
  - each expectation with its result and reason;
  - the prompt diff and settings diff between the two sides;
  - the raw answer and the calls.
- Actions: **Run again**, **Add variant**, **Add cases**, **Mark new finding good / noise**,
  **Promote variant** (opens a settings change or a PR with the experiment linked; it never changes
  live settings directly).

### 9. Scenario files (people writing their own)

Scenarios travel with a repo or the instance layer, and are loaded as cases:

```yaml
# .antiphon/review/scenarios/done-bypasses-move.yaml
scenario: done-bypasses-move
why: >
  PR #2 added a /done endpoint that sets the status directly and skips Board.Move, so the
  timestamps and the allowed-transition rule are bypassed. A reviewer must catch this.
source: { repo: michal-ciechan/antiphon-demo, base: e737fb3, head: b511da6 }
targets: [categorise, select, group, packs, review, collate, verify]   # or one step, or `all`
expect:
  all:
    - finding-present:
        file: src/Demo.Web/CardEndpoints.cs
        about: The /done endpoint changes status without going through Board.Move
        severity-at-least: major
    - field: { path: categories, contains: correctness }
    - budget: { costUsd: 2.00 }
    - script: scenarios/checks/no-style-nits.ps1
    - prompt: >
        Pass only if no finding asks for a change that the diff already makes.
```

A guide, `docs/features/013-experiments/writing-scenarios.md`, covers: picking a case that shows one
thing; writing the why; choosing between `unchanged`, specific findings and a rubric; keeping
rubrics about behaviour rather than wording; how many repeats; clean cases (a PR that should produce
nothing) as well as defect cases; and how the held-out split works. The antiphon-demo repo gets
scenarios for its three PRs as worked examples.

### 10. Beyond the review engine

The model is keyed by (workflow, node), not by "review". The review engine is first because its
stages already have typed outputs. The same case, variant, expectation and trial tables then cover
the delegated pipeline stages (Investigate, Plan, Code, Review): a case is a task brief plus a
worktree commit, a variant is an agent kind, level or instruction file, and expectations are mostly
`script` (the build and a test filter) and `prompt`. That is phase 6, after the review engine proves
the shape.

### 11. Data model

| Table | Holds |
|---|---|
| `review_blob` | Content-addressed bytes |
| `llm_call` | One row per model call |
| `human_finding` | Human review comments read from the code host, with the bot finding they match, if any |
| `eval_case` | Origin, context, inputs manifest, original outputs, expectations, tags, source (captured or file) |
| `eval_suite`, `eval_suite_case` | Suites and membership, with the tuning or held-out flag |
| `eval_variant` | Name, overrides, hash |
| `eval_experiment` | Scope, suite or cases, variants, repeats, budget, status, who and when |
| `eval_trial` | Case, variant, sample, sandbox run id, output, usage, cost, duration, per-expectation results, key |
| `eval_judgement` | A person's good or noise mark on an unjudged finding |

New columns: `ReviewNodeExecution.InputsJson`, prompt hashes and settings blob; `ReviewRun.Sandbox`
and `TrialId`.

### 12. API

| Verb and route | Purpose |
|---|---|
| `GET /api/review-runs/{id}/nodes/{executionId}` | Inputs, output, prompt, calls for the node panel |
| `POST /api/eval/cases` (from a node, finding or human finding), `GET`, `PATCH`, `DELETE` | Cases |
| `GET/POST /api/eval/suites`, `/variants` | Suites and variants |
| `POST /api/eval/experiments/estimate` | Trial count and cost estimate |
| `POST /api/eval/experiments` (with the accepted spend), `GET`, `POST …/{id}/cancel` | Experiments |
| `GET /api/eval/experiments/{id}/trials/{trialId}`, `…/compare?with=` | Trial detail and comparison |
| `POST /api/eval/trials/{id}/judgements` | Mark a new finding |
| `GET /api/eval/by-node?workflow=&node=` | The count and list for the graph badge |

A CLI front door, `scripts/antiphon experiment run --suite <name> --variant <name>`, uses the same
API so a suite can gate a prompt or check PR in CI.

## Phases

Each phase lands on its own and is useful alone.

| Phase | Delivers | Checked by |
|---|---|---|
| **1. See a node** | `review_blob`, `InputsJson` and `InputsHash` on every node, `llm_call`, raw answers kept, settings snapshot, `OutputJson` in the DTO; node panel Inputs / Output / Calls tabs | An API test: after a scripted run, every node has a manifest whose blobs exist; a client test for the panel |
| **2. Prompts as data** | The prompt registry with today's text as defaults; rendered prompt and hashes recorded; Prompt tab | A test that the default registry renders byte-identical text to today's strings |
| **3. Replay one node** | `IStepInputs` live and replay; the sandbox run and code host; cases captured from a node; `unchanged` and `field` expectations; replay of decision and completion nodes (categorise, select, verify) | A test: capture a case from a scripted run, replay with the same scripted model, expect `unchanged`; replay with a changed answer, expect a fail; assert the sandbox sent nothing to the code host |
| **4. Experiments and the A/B view** | Variants, experiments, estimate and budget, trial reuse; the history page, A/B grid and comparison; graph actions and badge; the workspace record, and reviewer (agent) trials in a rebuilt workspace, one container per trial (section 2a); `finding-present`, `finding-absent`, `budget`; the finding matcher | Matcher unit tests; an API test for a 2×2 experiment; a test that a rebuilt workspace has the recorded commit, tree hash and path, and that a mismatch fails the trial; a story and a test for the grid |
| **5. Scenarios and ground truth** | Scenario files and the guide; `script`, `prompt` and combined expectations; suites with held-out; human comments read back, **Bot missed this**; demo-repo scenarios; the CLI | Parser tests; one live run of the demo suite on server2 comparing two reviewer models |
| **6. Self-improvement gate, and other workflows** | The learn loop's candidate gate (section 7) with the generality lint and judge; pipeline-stage cases | A test that a candidate naming a file from its target case is rejected; a test that a regression blocks a candidate |

Phases 1 to 3 are about two to three Code tasks each; phase 4 is the largest and is mostly client
work. Testing stays minimal: the checks above, with scripted models, and one live run in phase 5.

## Risks

| Risk | Handling |
|---|---|
| Experiments spend real model budget | Estimate first, budget cap, explicit confirmation, trial reuse, host concurrency limits apply |
| Outputs vary run to run | Repeats, counts shown as counts, flaky cells marked, meaning-based matching |
| The judge model is itself wrong | The judge is fixed and recorded per suite; `prompt` results show their reason; people can overrule with a judgement, which is stored |
| Fitting prompts to the suite | Held-out cases, refreshed over time; the generality check; a human approves every promotion |
| Cases hold source code | Cases inherit the repo's access; blobs are kept only while a case references them; cases can be deleted |
| Storage growth | Content addressing deduplicates diffs and packs; retention on unreferenced blobs |
| Refactoring every step to `IStepInputs` breaks live reviews | Phase 3 moves one stage at a time, each behind the byte-identical replay test |

## Decisions for the operator

1. **Name and place:** decided 2026-11-24: a top-level "Experiments" area.
2. **First stages:** this plan replays categorise, select and verify first (cheap, fast), then the
   reviewer. Reverse it if comparing reviewer models matters most.
3. **Repeats:** decided 2026-11-24: one run per cell, repeats optional. Still open: whether an
   experiment over a set budget needs a second approval.
4. **Scenario files location:** `.antiphon/review/scenarios/` as planned, or a separate repo so
   held-out cases are not visible to agents working in the repo.
5. **Judge model:** one fixed strong model for every `prompt` expectation, or a setting per suite.
