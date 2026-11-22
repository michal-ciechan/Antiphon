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
