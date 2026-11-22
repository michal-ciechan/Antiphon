# Review engine server: test backlog (first slice)

Owner: the server track of the first slice. Tests deferred under the slice's testing budget, and spec
or corpus questions found while implementing. Each row says what to test and why it is not done yet.

## ABF parser (`server/Application/Review/Abf/`)

| # | Test later | Why |
|---|---|---|
| A-1 | A wrapped document whose body holds a bare fence line that equals the wrapper's closer (for example a lone ```` ``` ```` opening a code block inside a ```` ``` ````-wrapped document). Today the wrapper's closer wins and ends the document. | Spec 2 says the wrapped document ends at the first closer "not inside a fence opened within the document"; a bare opener line is not inside a fence, so this follows the spec, but the result surprises a model that fences code inside a fenced document. Spec question for ABF-6. |
| A-2 | A `decision-response` choice/score answer with a value and confidence but no option rows. | Spec 7.3 read literally: every option is `missing-option` (probability 0), the sum is 0, so `bad-value` and the answer is refused. Implemented literally; confirm this is intended before seats answer in ABF. |
| A-3 | `missing-answer` line number. | The spec does not say where it is reported. The parser uses the header line. |
| A-4 | `unknown-record` / `unknown-key` at `info` for a newer minor (`review-findings/1.3` read by a 1.0 consumer). | Implemented; no corpus case. |
| A-5 | `unsupported-syntax` (`@abf 2 ...`), `wrong-type`, `missing-document`. | Implemented; no corpus case. `wrong-type` is reported at the last header line, which the spec does not fix. |
| A-6 | Row repairs: `bad-row` (unclosed quote in a non-last column) and `missing-column`. | Implemented; `missing-column` carries the first missing column as its name and both drop the row from canonical JSON as well as typed JSON. The spec says "the row is dropped" without saying which form. |
| A-7 | An unexpected row in a known record with no columns is kept in canonical JSON as a string. | Spec 3.5 says the row is dropped, spec 4 says canonical JSON keeps everything; the parser keeps the string in canonical and drops it from typed. |
| A-8 | `completion-request` `expect` naming a request type is `bad-value`; `seed-candidates` `missing-provenance`, `missing-trigger`, `bug-commit == fix-commit`, `unknown-check`. | Implemented; no corpus case. |
| A-9 | `review-findings` with an assignment: `missing-check`, `unknown-check`; `duplicate-check`, `dangling-finding`, `check-status-mismatch`. | Implemented; the corpus has no assignment context and no case for the warnings. `check-status-mismatch` is skipped when the `checks` record is missing, so `invalid/004` reports only the missing record. |
| A-10 | Question counts above the LiteLLM limits (more than 128 questions, 255 choices, 10 levels). | No diagnostic code is defined for them; the adapter splits work. Not validated. |
| A-11 | The legacy generic profile (`--- name ---` other than the two known blocks) with fields, repeated keys and a heading inside a closed fence. | Only `legacy/008`'s empty `separator` block covers it. |
| A-12 | A TypeScript-port parity run over the same corpus. | Client track. |

## Workflow graph (`server/Application/Review/Workflows/`)

| # | Test later | Why |
|---|---|---|
| W-1 | A gate with `autoDecision` but no `autoRetries` fails (`gate-auto-retries`); a gate with neither breaks cycles. | Design 3.2 says "a missing or unbounded value fails validation"; the default workflow's gates declare neither, so the validator reads "missing" as "an automatic decision with no bound". Design question. |
| W-2 | Section overlap, unknown section member, fan-out `policy` (`quorum:n`), join `from`. | Implemented, untested. |
| W-3 | `WorkflowGraphDto` round trip: GET default, POST it back to validate, expect no diagnostics. Includes `params` arriving as `JsonElement`. | Covered only for an invalid graph over HTTP. |
| W-4 | `BackEdges()` marks DFS back edges as well as every `maxIterations` edge. | Untested beyond one edge. |
| W-5 | YAML loader errors (`WorkflowYamlException`): bad arrows, a label on the last endpoint, a non-integer `maxIterations`. | Untested. |

## Decision model (`DecisionChain`, `JevSystemOneClient`)

| # | Test later | Why |
|---|---|---|
| D-1 | Timeouts: a provider slower than `TimeoutSeconds` is skipped as `timeout`, and caller cancellation is not reported as a timeout. | Needs a fake handler that waits; cheap but cut for budget. |
| D-2 | 5xx and transport failures fall back (`unavailable`); a non-JSON body or missing `answers` falls back (`schema-failure`). | Implemented, untested. |
| D-3 | `ProxyManagedRetry`: a 429 skips straight to the next entry with no backoff retry. | Implemented, untested. |
| D-4 | `typesafe-native` and `openrouter-systemone` default endpoints; `litellm-passthrough` path `/typesafe/v1/systemone`. | Only `litellm-systemone` and an explicit `typesafe-native` base URL are covered. |
| D-5 | Answer-shape tolerance: noul as a bare number, `p_yes`, `value: yes`; choice with only `probabilities`. | OQ-2 is open until the first live call; replace these with the sanitised live fixture then. |
| D-6 | The API key never appears in logs or exception messages. | Review-by-inspection only so far. |

## Review API (`ReviewService`, `/api/review-*`)

| # | Test later | Why |
|---|---|---|
| R-1 | 404 for unknown run, finding and gate ids; 409 for a decision on a decided gate, a response on a closed run, `NotABlocker` on a non-blocker, `Dismiss`/`Defer` on an open blocker; 422 for a dismiss with no reason category and a batch naming a finding of another run. | Implemented, not covered by the one workflow test. |
| R-2 | `assign` with no login picks a code owner who is neither the author nor anyone who passed, and 409s when nobody is eligible; `pass` excludes the passer from the next pick. | Implemented with code owners as the pool (no team membership yet). |
| R-3 | Queue scopes `Mine`, `Teams`, `All`; SLA state thresholds. | `Teams` has no membership data yet and returns every team-owned open gate. Working-hours ageing uses a Mon-Fri 09:00-17:00 UTC calendar until `work_calendar` exists. |
| R-4 | KPIs from seeded data: in review, waiting on humans, average rounds, cost today, cost per accepted finding, model usage. | Computed from the DB; asserted only loosely over HTTP. |
| R-5 | `ReviewRunChanged` published once per mutation, with the run id. | Needs a factory subclass with a recording `IEventBus`. |
| R-6 | `POST /api/review-runs/demo` is 404 when `Review:DemoSeedEnabled` is false. | The test host runs in Development, where the default is on. |
| R-7 | Migration up/down on a predecessor clone: `Down` throws (additive rule). | Run the migration and model classes before review. |
| R-8 | Viewer resolution (v0.1): `Review:DefaultViewerLogin`, the `X-Review-Viewer` header honoured only with `Review:AllowViewerHeader`, `Review:Viewers` display names and teams; the author's own gate shows `viewerCanDecide: false` and a decision is 403. | Implemented; only the default viewer is exercised over HTTP. |
| R-9 | `costActualUsd` / `costEstimatedUsd` split by cost basis; `costUsd` is their sum. | Computed from node executions; not asserted. |
| R-10 | `GET /api/review-workflows/revisions/{id}` for an unknown id (404) and the get-or-create race on the revision hash. | Only the happy path is covered. |
| R-11 | Queue `Mine` includes unassigned gates of the viewer's teams; `slaWorkingMinutes` is the breach budget (480). | Implemented, untested. |
| R-12 | Single `POST /api/review-findings/{id}/responses` and `POST /api/review-gates/{id}/pass` persist their new event/pass rows. Both once failed because a child with a client-set Guid added only through a navigation is treated as existing by EF (UPDATE of 0 rows, 500); fixed by adding through the context. | The batch route is covered by the workflow test; these two are not. |

## Code hosts (`ICodeHost`, `GitHubCodeHost`, `LocalGitCodeHost`, `CodeHostRouter`)

| # | Test later | Why |
|---|---|---|
| C-1 | `LocalGitCodeHost` over a fixture repo: base/head refs resolve to SHAs, title and description from the head commit, `--name-status`/`--numstat -z` parsing with a rename, a binary file and a deletion; a ref starting with `-` is `InvalidRequest`. | Implemented; needs a git fixture on the git spawn lane. Only the GitHub adapter is unit tested. |
| C-2 | `CodeHostRouter`: a repo row naming a host of the other kind is `InvalidRequest`; a local repo with no path is `NotConfigured`; an unknown host id; a GitHub repo with no row uses `github`. | Implemented, untested. |
| C-3 | Intake errors over HTTP: PR 404 is a 404, a bad token is 503 `review_code_host_rejected`, a rate limit is 503 `review_code_host_rate_limited`, a local repo without a path is 422. | Mapping in `CodeHostRouter.ToHttp`; not exercised. |
| C-4 | GitHub diff too large (406) during categorise: the state carries the file list and a note instead of failing the node. | Implemented; no fake for it. |
| C-5 | `CodeHostResponseCache` bounds: past 512 entries it starts again, and a body over 4M characters is not kept. | Implemented, untested. |
| C-6 | The token never appears in a log line or an exception message. | One assertion on a 401 message only. |
| C-7 | A PR with more than 3000 files stops at 30 pages; compare lists at most 300 files on a recheck. | GitHub limits; not modelled by the fake. |

## Intake, Refresh and the executor (`ReviewService`, `ReviewWorkflowExecutor`)

| # | Test later | Why |
|---|---|---|
| E-1 | Refresh with a moved head: a new iteration with trigger `push`, the next round, pinned SHAs, the intake summary noting the move; the executor's delta is `incremental` since the previous head. | Covered only by reading; the one integration test stops after the first iteration. |
| E-2 | Refresh on a failed run with an unchanged head starts a `recheck` iteration in the same round; on a demo run (no pinned head) it only re-announces. | Implemented, untested. |
| E-3 | Delta falls back to the whole change when the previous head is unreachable (compare 404 after a force push) and says so in the summary. | Implemented, untested. |
| E-4 | Categorise with no configured provider (the default settings) fails the node with the chain's reason and the run becomes Failed; a chain whose every entry refuses a question fails with the question names and still records tokens and cost. | Loud-failure path; not exercised. |
| E-5 | A later iteration only raises the blast radius and adds categories. | Design choice; untested. |
| E-6 | Lease: two executors on one database never walk the same run (the conditional claim); a Running run whose `UpdatedAt` is older than `LeaseSeconds` is taken over; shutdown hands the node back as Pending and the run as Queued. | Needs two hosts on one schema or a direct executor harness. |
| E-7 | A Refresh that lands while a walk is running: the walk marks its running node Skipped (`superseded by iteration N`) and stops without overwriting the run. | Race; needs a blocking fake code host. |
| E-8 | Another-round on a run that came through a code host: the executor runs the pending intake for the new iteration. | Implemented; the demo run in `ReviewWorkflowApiTests` has no pinned head, so it is (correctly) never claimed. |
| E-9 | `ReviewRunChanged` once per node transition (Running, then Succeeded/Failed) and once at the stop point. | Needs a recording `IEventBus` (see R-5). |
| E-10 | Duplicate manual intake for a PR that already has an active run. | Today a second run is created; the design wants one run per subject (3.2). Decide 409 or return the existing run. |
| E-11 | Migration `AddReviewIterationShas` Down throws, and the tip-rewind fixtures (`MigrationTestDatabase.DetachHostLoadAsync` callers) need the review-engine tables and these columns detached before they rewind. | `AddReviewEngine` already had this gap on the branch; `tests/Antiphon.Tests/TestHelpers` is outside this track. |

## The whole workflow (`ReviewWorkflowExecutor`, `server/Application/Review/Execution/`)

Covered: `ReviewEndToEndApiTests` (a human path with findings, a fix, an approval gate, a base move
and a branch update; a bot path), `ReviewExecutorApiTests` (no project maps the repo: the real
reviewer runner fails the fan-out loudly), `ReviewRoundsPolicyTests` and `ReviewCheckParserTests`. E-1 and E-8 are now covered by the
human path.

| # | Test later | Why |
|---|---|---|
| X-1 | `route[bot-round-limit]`: three bot-only rounds with findings open the `human-review` gate with the limit as its reason; a response or gate decision resets the counter. | The counter and route are unit tested; the gate path over HTTP is not. |
| X-2 | An edge past `maxIterations` (`await-fixes[push]` a 21st time, `base-drift[affected]` an 11th) opens its overflow gate with the limit in its reasons. | Implemented in `FollowAsync`; needs 20 scripted pushes. |
| X-3 | `base-drift[affected]`: an overlapping base change asks `base_change_affects_pr`, at or above 0.35 starts a `base-moved` iteration in the same round with the rebase steer in every pack and `antiphon/approval` back to pending. | Implemented; the human path covers only the no-overlap branch. |
| X-4 | `base-moved` with `keepUpToDate: false` (`unchanged`/`moved` from the reviewed base) and `behind` (tip equals the reviewed base, merge base older). | Unit-testable once the step takes a branch state; only `moved` and `unchanged` run end to end. |
| X-5 | `await-fixes[base-moved] -> record-drift -> await-fixes`: a base move while fixes are awaited is recorded, the run parks again, and the next push's delta notes the drift. The drift re-check inside delta (design 4.2 step 1) is not implemented. | Refresh only raises `base-moved` when the wait listens for it; untested. |
| X-6 | `await-fixes[update-requested] -> update-branch[open] -> await-fixes` (the author's opt-in, D-19). | No API raises `update-requested` yet (see the contract proposals in the report). |
| X-7 | `await-merge[push]`: a push after approval starts a new round; the new head has no `antiphon/approval` status, so the approval is revoked by construction. | Implemented; untested. |
| X-8 | Refresh while parked at a gate with a moved head closes the gate (Done, no decision, a `superseded:` reason) and skips its node. | Implemented in `SupersedeGatesAsync`; untested. |
| X-9 | A PR closed (not merged) on the code host ends the run Closed from any parked node. | Merged is covered; Closed shares the code path. |
| X-10 | Verify: a disproved finding (p < 0.5) is dropped and recorded; a bypass finding is kept; a refused answer keeps it; a wrong request id fails the node; no chain configured is Skipped with the reason and findings pass through. | The scripted verifier holds everything; the skip path runs only by reading. |
| X-11 | Reviewer answer that does not parse: one retry, then the child fails with the error codes; a `Pending` answer parks the child Waiting with its handle and the run queued, then completes on a later pass. | The scripted reviewer always answers valid ABF at once. The `Pending` path is now covered by `ReviewDelegationRunnerApiTests`; the retry is not (G-6). |
| X-12 | Collate: duplicates across groups keep the most severe and merge flags; a duplicate of an open finding is dropped as such; an anchor outside the diff resolves through `ReadFileAsync` and is summary-only; an unresolvable anchor is dropped. | Anchors are unit tested; the dedupe paths are not. |
| X-13 | Select: more than 128 candidates split across calls; a refused `applies` is included; the round bar drops a check whose severity cannot clear it (a nit check in round 2) unless it is a bypass check. | Implemented; the seed set is 7 checks. |
| X-14 | Check catalogue: the instance folder (`Review:InstanceChecksPath`) and the repo's `.antiphon/review/checks/` at the base SHA (a repo check overrides an instance check by id); a file that does not parse is skipped and named in the select summary; the seed set is used only when both are empty. | Only the seed set runs end to end; the scripted host reads no files. |
| X-15 | Needs human approval rules one by one: categories, `reversal_after_months`, a `requiresHuman` check (SEED-0003), a large PR, two conceded disputes, an unverified high-severity check; the yes stays sticky in later rounds. | Only the external-contract rule runs end to end. |
| X-16 | Bot approval refusals: an open blocker, an open finding above the bar, a head moved since the review; each fails the node with its reason. | Implemented; untested. |
| X-17 | Finding sync: a transient code-host failure keeps the response Pending for the next pass; a rejected one is Failed; a local host or a summary-only finding is NotApplicable; Dismiss, NotABlocker and Defer resolve the thread when the host can. | WillFix and the bot's Fixed are covered. |
| X-18 | Context packs: the prefix hash is identical across an iteration's packs; the ledger has one row per changed file with `included` false past the diff budget; a re-run replaces the iteration's packs and ledger. | Implemented; only the outstanding-findings text is asserted. |
| X-19 | `GitHubCodeHost` writes over a fake handler: the review body and `RIGHT`-side comments, the comment ids read back from `reviews/{id}/comments`, a status description cut to 140 characters, `update-branch` with `expected_head_sha`, compare's `base_commit`, `merge_base_commit` and `behind_by`, a 404 raw file read as null. | Implemented; the existing GitHub tests cover reads only. |
| X-20 | `LocalGitCodeHost` compare, branch state, `ls-tree` and `show` over a fixture repo; every write is `Unsupported`. | Needs the git spawn lane (see C-1). |
| X-21 | Migration `AddReviewEngineExecution`: `Down` throws, and the tip-rewind fixtures detach the new tables and columns. | Same gap as E-11. |
| X-22 | Lease takeover in the middle of a fan-out: a second executor resumes the Waiting fan-out without re-running finished reviewers. | Needs two executors on one schema (E-6). |

## Reviewers, completions and thread resolution (`DelegationReviewAgentRunner`, `ICompletionModel`s, GitHub GraphQL)

Covered: `ReviewDelegationRunnerApiTests` (the real runner under the executor with the delegation
seam scripted: a read-only Review task in a worktree at the head, the node parked Waiting with the
task id, completed from the task's report; a failed task fails the node), `CompletionModelTests`
(the OpenAI-compatible adapter's request, answer, usage, cost header and error kinds; a 429 falling
back to the Anthropic Messages adapter in a chain; the default chain; the delegate seat and its
deadline cancel) and `GitHubThreadResolutionTests` (thread ids read at publish, the mutation, a
comment id mapped across pages, GraphQL error kinds, the endpoint).

| # | Test later | Why |
|---|---|---|
| G-1 | `AgentTaskReviewGateway` against the real `AgentTaskService` on a fixture repo: the created row is Worker/Review, Worktree, `WorktreeBaseRequestedRef` = head, `CommitOnSettle` Never, project from the caller, no parent and no reply routing; `StartRefAvailability` finds the head locally after the PR fetch. | Needs a git fixture repo in `Delegation:AllowedRoots` and a host whose dispatcher does not race the test; the seam is scripted instead. |
| G-2 | `StartRefAvailability.TryFetchPullRequestHeadAsync` over a fixture origin with `refs/pull/<n>/head`: a fetch under the repository lease, a head already present (no fetch), a moved PR (the ref lacks the SHA), no origin, a busy lease past the network deadline. | Needs the git spawn lane (see C-1). |
| G-3 | Reviewer timeout: a task older than `Review:Agents:TimeoutMinutes` is cancelled and the node fails; a Blocked task is cancelled and fails with its question; a deleted task fails. | Implemented; needs a controllable clock or a seeded `CreatedAt` in the scripted seam. |
| G-4 | A pack over `InlinePackMaxChars`: the brief carries the `GET /api/review-runs/{id}/packs/...` pointer (group key escaped) and stays under the 20,000-character goal limit; the route 404s for an unknown group. | Only the inlined pack and the route's happy path run. |
| G-5 | Project mapping: a `Review:Agents:Repos` row by id and by name, a row's `LocalPath` overriding the project's, two projects matching one repo (fails and names both), an archived project ignored, a local subject matched by `LocalRepositoryPath`. | URL forms are unit tested; the rest run only by reading. |
| G-6 | The parse-failure retry with the real runner: the second attempt dispatches a new task (attempt 2) and the first task's report is discarded. | The fan-out retry is generic; with the delegation runner it costs a second agent run, so the brief may want the parse errors. Decide whether to pass them. |
| G-7 | A remote runner reviewer (`Review:Agents:Runner` = server2): the task's mirror has the PR head, and `ANTIPHON_API` from the runner reaches the pack route. | Needs server2; the brief's pointer assumes the runner can call the server. |
| G-8 | Anthropic adapter alone: `refusal` and `max_tokens` stop reasons are schema failures (the chain moves on), `AuthScheme: bearer`, a base URL ending in `/v1`, 529 rate limited, the price table when no usage is returned. | One fallback path covers the happy mapping. |
| G-9 | OpenAI adapter: content as an array of parts, `finish_reason: length`, `MaxOutputTokens` sent as `max_tokens`, the price table when the proxy sends no cost header. | Implemented, untested. |
| G-10 | Verify through a real chat model: the `decision-response/1.0` template in the instructions is answered in ABF by Luna/Haiku over LiteLLM and parsed; record a sanitised fixture. | Needs the first live call from work (like OQ-2 for Jev). |
| G-11 | The delegate seat under the executor: verify blocks the walk for up to the entry's `TimeoutSeconds` (240, under the 300 s lease); a slower seat times out and the chain moves on. Making verify park like the reviewer node is the follow-up if seats become the default. | Bounded synchronous wait by design for now. |
| G-12 | Finding sync with a finding posted before thread ids were recorded: the resolve passes the comment id and GitHub maps it; a GraphQL `FORBIDDEN` (token without pull-request write) marks the response Failed after the reply posted. | The mapping is unit tested on the adapter; the sync path with GitHub is not. |
| G-13 | GraphQL thread listing on a PR with more than 1,000 threads stops at 10 pages and the thread is reported not found. | Limit not modelled. |
