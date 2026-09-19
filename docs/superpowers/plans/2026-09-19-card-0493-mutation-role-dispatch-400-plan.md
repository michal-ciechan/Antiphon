# CARD-0493: `-Role Mutation` 400 was served-build skew, not a DTO gap

Plan task `944accde`, 2026-09-19, inspected checkout `7ae80b70` (master, and the SHA the live
server reports). The brief folded the investigation into this pass; the ground-truth table below
is that investigation. Prior design authority: [CARD-0470 plan](2026-09-09-card-0470-code-mutation-split-plan.md)
(the role's introduction, V-1 and V-12), [CARD-0495 plan](2026-09-12-card-0495-stale-server-prevention-plan.md)
(served-SHA restart gate). This plan changes no server code.

## Disposition in five lines

1. **The card's premise is wrong.** `CreateAgentTaskRequest.Role` is `AgentTaskRole`
   (`server/Application/Dtos/AgentTaskDtos.cs:14`), which has carried `Mutation = 16` since
   `b1f0508c5` (2026-09-10 12:46 BST). The production JSON options bind `"Mutation"` (and
   `"mutation"`) today; the card's exact 400 text is what the same binary returns for a name that
   is not in the enum. Nothing in the DTO, converter or role wiring is missing.
2. **The 400 came from a server process built before the enum member existed.** The process that
   answered on 2026-09-12 12:02 BST had started at the 2026-09-09 17:40 autostart, 19 hours before
   `b1f0508c5` landed, and was not restarted until about 13:45 BST on 09-12, roughly 100 minutes
   after the incident. The restart script gained its served-SHA-equals-HEAD gate (CARD-0495) at
   14:59 BST that day. The client side (`delegate.ps1`'s `ValidateSet`) is read from the
   dispatching worktree at call time, so it was current while the server was not: the two drift by
   construction and only the server's answer reveals it.
3. **Audit:** no Mutation-role row exists before 2026-09-14 06:12Z; eight exist since (seven
   Succeeded, one Failed on an unrelated SourceLanding admission refusal). The role was
   non-functional client-to-server only while a pre-`b1f0508c5` build was serving, i.e. from the
   09-10 landing until the 09-12 13:45 restart.
4. **What is delivered instead of a DTO fix:** (S1) a regression guard that POSTs every role the
   scripts accept into the real HTTP pipeline and a parity guard that the three scripts' role sets
   equal the hand-delegatable enum members, so the class of gap the card feared fails a test rather
   than a dispatch; (S2) a `delegate.ps1` diagnosis that turns a bind 400 into a served-build-vs-HEAD
   statement with the restart instruction, so the next skew is recognised at the failure instead of
   filed as a server bug; (S3) three one-sentence doc amendments.
5. **Activation needs no server restart.** S1 is tests, S2 is a script every dispatching session
   reads from its own checkout, S3 is docs.

## Ground truth

Verified on `7ae80b70` on 2026-09-19 against the live server (`GET /api/version` =
`7ae80b70b57aa4cee7f0d44cdcd451cdd0e2a596`, `land-v2`) and the dev Postgres. Times are BST unless
suffixed `Z`.

| Card assumption | Observed | Consequence |
|---|---|---|
| The DTO's role type may not include a `Mutation` member. | `AgentTaskRole` (`server/Domain/Enums/AgentTaskEnums.cs:26-79`) has `Mutation = 16`; `git log -S'Mutation = 16'` on that file shows exactly one commit, `b1f0508c5` `feat(CARD-0470): wire Mutation role and ownership split` (2026-09-10 12:46), which is an ancestor of `origin/master`. The DTO uses the enum directly (`AgentTaskDtos.cs:14`); grep finds no second role type. | No enum or DTO change (D-1). |
| A `JsonStringEnumConverter` case or naming mismatch could reject a well-formed `"Mutation"`. | The only HTTP JSON configuration is `Program.cs:277-281`: `JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: false)`; no `AddControllers`/`AddJsonOptions`; no `[JsonStringEnumMemberName]`/`[EnumMember]` on the enum. Live probe on the served build, token-authenticated, body `{"role":"<r>","goal":""}` (an empty goal is refused at `AgentTaskService.CreateAsync` line 200 before anything is persisted): `Mutation` → 422 `validation_failed` `errors.Goal[0] = "A goal is required."`; `mutation` → the same 422 (the converter reads case-insensitively); `Bogus` → 400 `The JSON value could not be converted to Antiphon.Server.Application.Dtos.CreateAgentTaskRequest. Path: $.role \| LineNumber: 0 \| BytePositionInLine: 15.` | The card's text, with `BytePositionInLine: 18`, is byte-for-byte what a build whose enum lacks the name produces: `{"role":"Mutation"` is 18 bytes and `{"role":"Bogus"` is 15. The 400 is a converter refusal of an unknown name, and the name is known to every build since `b1f0508c5`. |
| The server that refused on 2026-09-12 was current. | Windows System log: one reboot, 09-09 17:33-17:34; `logs/autostart-apphost.log`: AppHost up, backend healthy on 17202 at 09-09 17:40:45. The AppHost-managed FakeGateway (`Antiphon.AppHost/Program.cs:39-41`) logs `InboundUnconsumedMonitorStatus` to the Application log every 5-6 minutes; its timer phase (seconds field) drifts smoothly `:35 → :24 → :23 → :22 → :21` from 09-09 17:40 to 09-12 13:44 with no gap over 8 minutes and no persistent phase change, then shifts permanently at 09-12 13:44:21 → 13:47:43 (a 3m22s interval, the startup fire) and again at ~17:41, ~22:00, ~22:53 that evening, each following a land in the main checkout's reflog. Main checkout `master` first contained `b1f0508c5` at 09-10 16:44 (reflog `7a6b44f9`); at the 13:45 restart it was `39afe4ec`, which contains it. | The process serving 17202 at 12:02 on 09-12 was built from a 09-09 checkout. Skew, not code. (A dashboard-only restart of the server resource would not rebuild either, so the conclusion does not depend on the AppHost marker being the only restart path.) |
| The old restart path would have caught a stale build. | `restart-apphost.ps1`'s `GET /api/version`-equals-HEAD gate and the `land-v2` marker landed with CARD-0495 at `25c81dcf7`, 09-12 14:59, i.e. after the incident. Before that a stale-but-consistent build passed `/health` exactly as the CARD-0358 gotcha (`docs/bootstrap.md:549`, written 09-04) already warned. | The structural fix for the cause already shipped (CARD-0495). What this card adds is recognition at the point of failure (D-5) and a guard for the feared class (D-3, D-4). |
| No Mutation dispatch may ever have succeeded since CARD-0470. | `select … from "AgentTasks" where "Role"=16`: 8 rows. First `9a5d9aa6` 2026-09-14 06:12Z (Codex, Failed: `No PCs ran: this task lacks the required confirmed SourceLanding commissioning record…`, an admission refusal after creation, not a bind failure); then `28933547`, `3c388c4b` (Codex, Succeeded), `7a1cef7a`, `41cdaeb7`, `02c2aa2b`, `308ba49f`, `ba4782b3` (ClaudeCode, Succeeded); three of them SourceLanding-sourced. Zero rows between 09-10 and 09-13. The workaround task `4db8ee42` (Role Custom, title `Verify CARD-0488 bypass PCs (Mutation)`) was created 2026-09-12 11:02:15Z. Server-side creation of Mutation tasks (CARD-0552 auto-dispatch) is not implemented yet, so every row is a `delegate.ps1` dispatch. | The role has worked client-to-server since the 09-12 13:45 restart. No "fold PC work into Code/Custom" convention exists to unwind. |
| CARD-0470's rollout was incomplete on the create side. | CARD-0470 V-1 (`MutationPipelineTests.C470_storage_and_http_enum_contract`) pins integer storage and string `Mutation` on GET DTOs through the guarded factory; V-12 (`Scripts/RoutingPinScriptTests.C470_delegate_mutation_posts_shared_retained_directory`) runs the real script against a stub and pins the posted `role: "Mutation"`. Neither POSTs the name through the real server's JSON pipeline; `grep PostAsJsonAsync("/api/agent-tasks"` over `tests/Antiphon.Tests` finds no create-side HTTP test for any role. | The untested seam is script-body → server bind, which is exactly the seam skew breaks and exactly what a future missing enum member would break. S1 closes it for every scripted role, not just Mutation. |
| `delegate.ps1` accepted `Mutation` client-side. | `scripts/delegate.ps1:23` `ValidateSet` lists it (also `routing-pin.ps1:35`, `complexity-chain.ps1:33` with `Any`); all three since `b1f0508c5`. The 14 names equal `Enum.GetNames<AgentTaskRole>()` minus the three `AgentTaskRoles.IsSpecialist` members (`AgentTaskEnums.cs:379-380`: Check, Distill, Diagnose). | The parity guard (D-4) has an exact, code-derived expected set. |
| A 400 on `$.role` tells the operator nothing about why. | `Invoke-Antiphon` (`delegate.ps1:439-457`) prints `Antiphon POST /api/agent-tasks failed: <server body>` and exits 1. The script already knows how to probe `GET /api/version` (`Invoke-AntiphonLandVersionProbe`, `:408`) and how to find the checkout root (`Get-AntiphonCheckoutRoot`, `:460`). | D-5 composes those two into a diagnosis on the bind-failure path only. |

## Decisions

### D-1. Root cause is deployment skew; no DTO, converter or wiring change

Every candidate the card names was checked and is present on every commit since `b1f0508c5`.
Adding a converter, a `[JsonStringEnumMemberName]`, or a "role-list" registration would be a
change with no red test to justify it. **Rejected:** any production server edit for this card.

### D-2. The deliverable is a guard for the feared class plus recognition of the actual class

"So that `-Role Mutation` creates a task" is already true. Two things remain worth building: a
test that would have gone red had CARD-0470 really missed the create side (and will go red the
next time a role is added to a script but not the enum, or vice versa), and a script diagnosis
that names skew at the moment it bites, because the CARD-0358 gotcha in the docs did not stop a
Frontier-tier orchestrator from filing this as a server bug eight days later.

### D-3. Guard shape: HTTP bind of every scripted role, through the real `Program` JSON options

`AgentTaskRoleBindingTests` (new) reads the `-Role` `ValidateSet` line of `scripts/delegate.ps1`
via `DelegateScriptRunner.RepoRoot`, and for each name POSTs `{"role": name, "goal": ""}` to
`/api/agent-tasks` on `AntiphonWebAppFactory`'s client, asserting 422 `validation_failed` with the
`Goal` error. An empty goal is refused at the first statement of `CreateAsync`, so the test
creates nothing and needs no allowed root, agent or token. A negative control POSTs an unknown
name and pins the incident's 400 text so the positive assertion cannot pass vacuously.
**Rejected:** a service-level `CreateAsync(new CreateAgentTaskRequest(Role: …))` test (bypasses
the JSON bind, the seam that failed); a test that creates a real task per role (dispatch side
effects, allowed-root and agent fixtures, 14× slower, proves nothing more about binding).

### D-4. Parity guard: the three scripts' role sets equal the non-specialist enum members

Same class, same helper: `delegate.ps1` and `routing-pin.ps1` sets must equal
`Enum.GetNames<AgentTaskRole>()` filtered by `!AgentTaskRoles.IsSpecialist`; `complexity-chain.ps1`
must equal that set plus `Any`. Modelled on
`DelegateScriptKindTests.the_scripts_ValidateSet_is_exactly_the_servers_allowlist` (the `-Kind`
precedent). **Rejected:** client TypeScript list parity (`client/src/api/agentTasks.ts:47,392,588`):
Vitest cannot read the C# enum and the client already has fixture-contract tests; a cross-language
generated list is a different card.

### D-5. `delegate.ps1` diagnoses a bind 400 with served build vs checkout HEAD

In `Invoke-Antiphon`'s catch, when the status is 400 and the body matches
`could not be converted to [\w.]+\. Path: \$\.(?<field>\w+)`, the script (a) reads the offending
value from the request hashtable by that field name, (b) probes `GET /api/version` with the
existing `Invoke-AntiphonLandVersionProbe`, (c) relates the served SHA to `HEAD` of
`Get-AntiphonCheckoutRoot` with `git cat-file -e`, `git merge-base --is-ancestor` and
`git rev-list --count`, and (d) prints one `diagnosis:` line before the existing error and exit 1.
The path is generic over field and DTO, not special-cased to `role`.

**Rejected alternatives.** (a) A preflight `/api/version`-vs-HEAD comparison on every create: a
worktree's HEAD differs from the served SHA on nearly every dispatch, so it would warn constantly
and be ignored. (b) Automatic fallback to `-Role Custom`: it silently reroutes the role and hides
the skew; the manual workaround was right for that moment and wrong as policy. (c) Advertising
accepted role names in `/api/version` capabilities (the `land-v2` pattern): a list that must be
edited on every enum change, redundant with the server's own 400, and it would not help the next
DTO field. (d) Docs only: the gotcha has existed since 09-04 and was not consulted at the failure;
the hint has to be in the failure output.

### D-6. The diagnosis changes nothing else about the failure

Exit code stays 1, the server's own message is still printed, no retry, no second POST, and the
only added request is one `GET /api/version` on the bind-failure path. A failed version probe
still prints the diagnosis with the probe's status in place of the served SHA.

### D-7. Exact output fragments, so tests and script agree

| Case | Fragment (one physical line, `diagnosis:` prefix) |
|---|---|
| always | `diagnosis: served build <served7> rejected '<value>' for '<field>'` |
| served SHA known and a strict ancestor of HEAD | `HEAD <head7> is <N> commit(s) ahead of the served build` |
| served SHA equals HEAD | `HEAD equals the served build; the value is genuinely unknown to this source` |
| served SHA known, not an ancestor | `served build <served7> is not an ancestor of HEAD <head7>` |
| served SHA not a local commit | `served build <served7> is not a commit this checkout knows` |
| version probe failed | `GET <api>/api/version failed (<status or error>)` in place of `served build <served7>` |
| always, last | `restart the AppHost from the canonical checkout (pwsh -NoProfile -File scripts/restart-apphost.ps1), confirm GET <api>/api/version, then re-run this exact command; do not re-dispatch under a different -Role` |

`<served7>`/`<head7>` are the first seven hex characters. When the value cannot be read from the
request (field absent from the hashtable) print `'<unknown>'`.

### D-8. Docs: three sentences, no new section

`docs/bootstrap.md` CARD-0358 bullet gains the API-level signature of skew; `docs/ops-http.md`'s
`POST /api/agent-tasks` notes (around `:314`) gain the 400-means-served-build-predates-the-value
rule; `docs/orchestration-loop.md` Post-land activation paragraph (`:86`) gains one sentence that
a new enum member is a server capability like `land-v2`.

### D-9. The card record is corrected, not the code

The report to the orchestrator states the premise correction; the card description is the board's
to amend (CARD-0019 correctable in place). CARD-0470's report-side item (`next: mutation`
recognition) is untouched, per the brief.

## Implementation slices

### S1. Bind and parity guards (one commit)

- `tests/Antiphon.Tests/Application/AgentTaskRoleBindingTests.cs` (new):
  `[Category("Integration")]`, `[ClassDataSource<AntiphonWebAppFactory>(Shared = SharedType.PerTestSession)]`,
  tests `[NotInParallel]` as in `MutationPipelineTests`. Helper `ScriptedRoles(string script)`:
  read `Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", script)`, take the single trimmed
  line starting with `[ValidateSet('Investigate'` (assert exactly one), extract `'(\w+)'` names.
  Tests V-1, V-2, V-3 below.
- If the token-less POST is refused before `CreateAsync` (it should not be: `ResolveCallerAsync`,
  `AgentTaskEndpoints.cs:328`, returns an anonymous caller), seed a task token the way
  `AgentTaskCommitEndpointTests` does and add the `X-Antiphon-Task-Token` header; do not weaken
  the assertion to "not 400".

### S2. `delegate.ps1` bind-failure diagnosis (one commit)

- `scripts/delegate.ps1`: new `Write-AntiphonBindFailureDiagnosis -Field -Value -Detail`
  implementing D-5/D-7, called from `Invoke-Antiphon`'s catch only when
  `Get-AntiphonHttpStatusCode` is 400 and the regex matches; keep `Write-Error` + `exit 1`.
- `tests/Antiphon.Tests/Application/DelegateScriptRunner.cs`: add an optional
  `workingDirectory` argument (sets `ProcessStartInfo.WorkingDirectory`), default unchanged.
- `tests/Antiphon.Tests/Scripts/DelegateScriptBindDiagnosisTests.cs` (new):
  `[Category("Integration")]`, `[ParallelLimiter<ProcessSpawnLimit>]`. Local `HttpListener` stub
  (`EphemeralHttpListener.BindLoopback`, as `DelegateScriptKindTests.StubApi`) with programmable
  `CreateStatus`/`CreateBody`, `VersionStatus`/`VersionBody`, counters `CreateCalls`,
  `VersionCalls`. A temp git repo with two commits (`C1`, `C2 = HEAD`) as the script's working
  directory so `Get-AntiphonCheckoutRoot` resolves it. Tests V-4..V-9 below.

### S3. Documentation (may share S2's commit)

- `docs/bootstrap.md:549` bullet: append "The API-level signature of this skew is a 400
  `The JSON value could not be converted to …CreateAgentTaskRequest. Path: $.<field>` for a value
  the script's `ValidateSet` accepts; `delegate.ps1` now prints a `diagnosis:` line relating the
  served SHA to HEAD (CARD-0493, 2026-09-12)."
- `docs/ops-http.md` near `:314`: "`POST /api/agent-tasks` answers 400 `…could not be converted…
  Path: $.role` only for a name the served build's enum lacks; on master every scripted role binds
  (`AgentTaskRoleBindingTests`), so that 400 means the served build predates the value: check
  `GET /api/version` against HEAD and restart (CARD-0493)."
- `docs/orchestration-loop.md:86` paragraph: "A new enum member (a role, a workspace mode) is a
  server capability exactly like `land-v2`: the script accepting it proves nothing about the served
  build."

## Verification design

Executable by Build as written. S1 tests run in-process on the shared test Postgres through
`AntiphonWebAppFactory` (the real `Program`, so the real `ConfigureHttpJsonOptions`). S2 tests
spawn `pwsh` and carry `[ParallelLimiter<ProcessSpawnLimit>]`; they need no database.

### Delivery inventory

| Slice | File | Change |
|---|---|---|
| S1 | `tests/Antiphon.Tests/Application/AgentTaskRoleBindingTests.cs` | new class, helper, V-1..V-3 |
| S2 | `scripts/delegate.ps1` | diagnosis function + catch-path call |
| S2 | `tests/Antiphon.Tests/Application/DelegateScriptRunner.cs` | optional working directory |
| S2 | `tests/Antiphon.Tests/Scripts/DelegateScriptBindDiagnosisTests.cs` | new class, stub, V-4..V-9 |
| S3 | `docs/bootstrap.md`, `docs/ops-http.md`, `docs/orchestration-loop.md` | one sentence each |

### Proves it works now

| V | Test (class / method) | Setup | Red on `7ae80b70` | Green after |
|---|---|---|---|---|
| V-1 | `AgentTaskRoleBindingTests.C493_every_scripted_role_binds_on_create` | For each of the 14 `delegate.ps1` names, and again lower-cased, POST `{"role":name,"goal":""}`. | Green (guard; see PC-1 for its red). | 422, `code == "validation_failed"`, `errors.Goal[0] == "A goal is required."`, per-role failure message names the role and echoes the body. 28 requests. |
| V-2 | `AgentTaskRoleBindingTests.C493_an_unknown_role_is_the_incident_400` | POST `{"role":"NotARole","goal":""}`. | Green. | 400; `detail` contains `could not be converted to Antiphon.Server.Application.Dtos.CreateAgentTaskRequest` and `Path: $.role`. Negative control for V-1. |
| V-3 | `AgentTaskRoleBindingTests.C493_scripted_roles_equal_the_hand_delegatable_enum` | Sets from `delegate.ps1`, `routing-pin.ps1`, `complexity-chain.ps1`; expected = `Enum.GetNames<AgentTaskRole>()` where `!AgentTaskRoles.IsSpecialist`. | Green (see PC-2). | `delegate` and `routing-pin` sets equal expected; `complexity-chain` set equals expected ∪ `{Any}`; each script has exactly one such line. |
| V-4 | `DelegateScriptBindDiagnosisTests.a_422_is_reported_without_a_diagnosis` | Stub: POST → 422 `{"code":"validation_failed","errors":{"Goal":["A goal is required."]}}`. Run `-Role Mutation -Goal x`. | Green. | exit ≠ 0; output contains `validation_failed`; output does not contain `diagnosis:`; `CreateCalls == 1`; `VersionCalls == 0`. |
| V-5 | `…a_bind_400_with_an_unknown_served_build_is_diagnosed` | Stub: POST → 400 with the incident body verbatim (`Path: $.role \| LineNumber: 0 \| BytePositionInLine: 18.`); GET `/api/version` → `{"version":"<40×a>","capabilities":["land-v2"]}`. cwd = temp repo. | Red: no `diagnosis:` line. | exit 1; output contains `diagnosis: served build aaaaaaa rejected 'Mutation' for 'role'`, `is not a commit this checkout knows`, `restart-apphost.ps1`, `do not re-dispatch under a different -Role`; `CreateCalls == 1`; `VersionCalls == 1`; the server's own `could not be converted` text still present. |
| V-6 | `…a_bind_400_counts_commits_ahead_of_an_ancestor_build` | As V-5 with `version` = `C1` (temp repo's first commit). | Red. | output contains `HEAD <C2 first 7> is 1 commit(s) ahead of the served build`. |
| V-7 | `…a_bind_400_with_head_equal_to_the_served_build_says_genuinely_unknown` | As V-5 with `version` = `C2`. | Red. | output contains `HEAD equals the served build; the value is genuinely unknown to this source`. |
| V-8 | `…a_bind_400_on_another_field_is_diagnosed_generically` | Stub 400 body with `Path: $.workspace`; run `-Role Code -Worktree -Goal x`. | Red. | output contains `rejected 'Worktree' for 'workspace'`. |
| V-9 | `…a_bind_400_with_version_unavailable_still_diagnoses` | As V-5 with GET `/api/version` → 404. | Red. | output contains `GET <stub>/api/version failed (404)`, `rejected 'Mutation' for 'role'`, `restart-apphost.ps1`; exit 1; `CreateCalls == 1`. |

V-1's lower-cased pass documents the converter's case-insensitive read (observed live); it is not
a contract the scripts rely on. V-5..V-9 pin the D-7 fragments literally; Build must not paraphrase
them in either place.

### Guards the regression (carried forward, must stay green)

`MutationPipelineTests.C470_storage_and_http_enum_contract`;
`Scripts/RoutingPinScriptTests.C470_delegate_mutation_posts_shared_retained_directory`;
`DelegateScriptKindTests.the_scripts_ValidateSet_is_exactly_the_servers_allowlist` and
`an_undelegatable_Kind_is_refused_by_the_script_before_any_request` (the catch path they exercise
must still exit 1 with the server text and no diagnosis).

### Positive controls (Mutation, method-scoped)

| PC | Mutation | Expected red | Restore, then green |
|---|---|---|---|
| PC-1 | `server/Domain/Enums/AgentTaskEnums.cs`: `[JsonStringEnumMemberName("Mutate")]` on `Mutation = 16` (add `using System.Text.Json.Serialization;`). | V-1 red for `Mutation`/`mutation` with the incident's 400 text; V-2, V-3 unchanged. This is the card's feared defect reproduced exactly. | V-1 |
| PC-2 | `scripts/delegate.ps1:23`: remove `'Mutation'` from the `ValidateSet`. | V-3 red (`delegate` set lacks `Mutation`); `RoutingPinScriptTests.C470_delegate_mutation_posts_shared_retained_directory` red (script refuses the argument). V-1 stays green, which is why V-3 exists. | V-3, the C470 test |
| PC-3 | `delegate.ps1` diagnosis: regex `Path: \$\.(?<field>\w+)` → `Path: \$\.zzz(?<field>\w+)`. | V-5..V-9 red (no `diagnosis:`); V-4 green. | V-5..V-9 |
| PC-4 | `delegate.ps1` diagnosis: drop the status-400 condition. | V-4 red (`diagnosis:` appears, `VersionCalls == 1`). | V-4 |
| PC-5 | `delegate.ps1` diagnosis: skip `merge-base`/`rev-list`, always print the "not a commit this checkout knows" branch. | V-6, V-7 red; V-5 green. | V-6, V-7 |

PC-1 and PC-2 touch different files and different tests and may be batched; PC-3..PC-5 all edit
the same function and run one at a time. Filters:
`--treenode-filter "/*/*/AgentTaskRoleBindingTests/C493_every_scripted_role_binds_on_create"`,
`"/*/*/AgentTaskRoleBindingTests/C493_scripted_roles_equal_the_hand_delegatable_enum"`,
`"/*/*/DelegateScriptBindDiagnosisTests/<method>"`.

### Commands

Build once to an isolated output (forward slash), run the two new classes plus the carried-forward
classes with the CARD-0403 combined-class syntax, keep the TRX, delete every `bin-c493` directory
afterwards:

```
dotnet build tests/Antiphon.Tests --property:OutputPath=bin-c493/ --nologo
dotnet run --project tests/Antiphon.Tests --no-build --property:OutputPath=bin-c493/ -- --treenode-filter '/*/*/(AgentTaskRoleBindingTests*)|(DelegateScriptBindDiagnosisTests*)|(MutationPipelineTests*)|(RoutingPinScriptTests*)|(DelegateScriptKindTests*)/*' --report-trx --report-trx-filename c493.trx --results-directory .antiphon/c493
Get-ChildItem -Recurse -Directory -Filter bin-c493 | Remove-Item -Recurse -Force
```

Expected after S1+S2: every test in the five classes passes; the TRX names V-1..V-9 with nonzero
counts. Red confirmation before S2 is one run of `DelegateScriptBindDiagnosisTests` with only the
test files applied (V-5..V-9 fail on the missing `diagnosis:` line, V-4 passes); commit the test
files as the checkpoint before that run. S1 has no pre-fix red by design; its red is PC-1/PC-2.

## Out of scope

- The report-side `next: mutation` vocabulary (CARD-0470's open item, tracked separately per the
  brief).
- CARD-0552 server-side Mutation auto-dispatch and the CARD-0478 companion card.
- Client TypeScript role-list parity with the server enum (D-4).
- Any change to `restart-apphost.ps1` or `/api/version` (CARD-0495 already gates served SHA).
- A general "capabilities list" of enum members on `/api/version` (D-5 c).

## Cost and next stage

Small: one new test class per slice, one script function, one helper parameter, three doc
sentences. Estimate 3-4 hours of Code including the PC battery. Review should come from a
different company than Code. No server restart is needed for activation; land normally.

The brief asked for the acceptance guards and the regression test plan in this pass, so the
verification design above is complete and Build can execute it; if the orchestrator prefers a
separate TestDesign pass, dispatch it against this file.
