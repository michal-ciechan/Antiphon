# CARD-0585: The Code stage runs the plan's checkpoint list, not an ad hoc build/test loop

Status: Plan, written under stated defaults (D-1 to D-12). Verification design is included (§Verification design), so the next stage is Code. Process/tooling only: five markdown files, three stage bundles, one PowerShell script with its offline harness, and two test classes. No server, DTO, migration or `delegate.ps1` change.

Authoring baseline: `3c7a4057bc363cfcb1afa6dced63f411e9f243ce` (branch `feat/card-task-c523fcdf`, equal to `origin/master` at authoring). Plan task: `c523fcdf`. Card: CARD-0585 (Antiphon board, `49211e68`). Investigation: [2026-09-20-card-0585-batched-edit-test-workflow.md](../../investigations/2026-09-20-card-0585-batched-edit-test-workflow.md) at `5378b6340711fe7b7658d67ba0e245ae4a05ec6e` on `feat/card-task-0eb35cd2` (not yet on master; land it or read it at that commit). Line numbers below are at `3c7a4057`.

Owners to read before changing these areas: [testing and build](../../testing-and-build.md) (Fast lane, combined filters, PC execution; the new section's home), [orchestration loop](../../orchestration-loop.md) §3 (what a brief carries), [`server/Bundles/README.md`](../../../server/Bundles/README.md) (bundles are standing rules, versioned by hash, 2,500-char ASCII cap on stage bundles), [antiphon-delegate skill](../../../.claude/skills/antiphon-delegate/SKILL.md) (stage recipes).

## Outcome and scope

A Plan/TestDesign artifact ends with a `### Checkpoints` table: one row per isolated build plus one exact test-filter group, bound to the plan slice it closes. A Code dispatch's brief points at that table; the `stage-code` bundle obliges the delegate to run it as a **closed list** (each row once, in order, after its slice is committed; a red row is fixed and rerun as the same row; every other build or test command is reported as unlisted with a reason) and to report one line per row with commit, filter, executed/passed/failed/skipped counts and TRX path. The `stage-review` bundle rejects a Code report whose checkpoint lines do not cover the table, and both bundles name the tautological-stub failure that cost CARD-0459 a second $110 round. A small runner, `scripts/run-checkpoint.ps1`, produces that line from a fresh TRX so no delegate re-derives TRX parsing or opens the file.

The investigation's finding this is built on: the list already exists. CARD-0459's plan at `484fb214` has S1-S5, a coverage-to-class table, `OutputPath=bin-c459/`, and a Cost block with one build and sixteen named `Invoke-C459Tests` filter groups. What was missing was (1) binding each group to the slice that produces it, (2) an obligation on Code to run exactly that list, (3) a Review check per row, and (4) a rule that a test which cannot go red is not done. Nothing here shrinks the named Slow/native V/R work (the 159-minute batch is the coverage); it removes the extra rebuilds (CARD-0490: 19 builds for 8 test runs), the hunting for files the plan already named, and the second Code round.

Explicitly **not** designed, per the brief and the investigation §4C: an apply-all-edits-then-one-final-run mode. CARD-0459's first round greened `x.ShouldBe(x)` bodies; one end-of-round suite would have settled that as done.

Out of scope: the Grok harness's 15-second auto-background and the wait-only loops it causes (8-15 % of loops, independent of any manifest); Interim rounds (CARD-0544, dormant, own selection table); a server-side `verificationSelection` on Final rounds (§Not done, noted, with exact touch points); a table *executor* that runs the whole list unattended (that is the forbidden apply-all shape and would hide which slice a red belongs to); `/learn` and Hindsight (CARD-0583); `LandVerifyFilter` and the production verifier.

## Ground truth

| Card / handoff premise or plausible shortcut | Confirmed at `3c7a4057` | Design consequence |
|---|---|---|
| "A plan-authored manifest of which files to edit, in what order, and which filters after which edit groups" is a new artifact. | CARD-0459's plan (`484fb214`, `docs/superpowers/plans/2026-09-19-card-0459-worktree-cleanup-plan.md`) already has slices S1-S5 with files (`:320-416`), a coverage-to-class inventory, and a Cost block (`:1084-1130`) that builds once into `bin-c459/` and runs 16 named filter groups (`unit`, `settled-removal`, `publication-regression`, ...) through an inline `Invoke-C459Tests` that rejects zero executed tests. Missing: which slice each group closes, an order, and any obligation. | The manifest is a table added to the existing `## Verification design` (D-1) whose columns add exactly the missing facts: `After`, `Covers`, `Expect` (D-2). No new file. |
| "Code just never executed that as a closed checkpoint list" is a delegate habit. | `server/Bundles/stage-code.md:3` says "Build once into producer-owned isolated output; inspect fresh TRX ...; Report the filters and actual expanded counts" and `:7` "Run each V-n and R-n the round requires". Nothing says the list is closed, nothing names unlisted runs, nothing asks for per-group evidence. `stage-review.md:3` rejects a "missing coverage-to-class list, missing filter/count evidence" but checks no row. `grep -rn tautolog docs server/Bundles .claude/skills AGENTS.md` finds only an unrelated line in `herdr-sessions.md`. | The obligation, the unlisted-run rule, the per-row report line and the stub rule go into the bundles (D-5, D-8); the doc owns the schema (S1). |
| "A schema the `delegate.ps1` tooling could enforce" needs new tooling. | It exists, gated to a dormant feature: `-VerificationSelectionFile` (`scripts/delegate.ps1:94-97`, `:902-947`) sends `{artifactPath, artifactCommitSha, section}`; the server validates a repo-relative `docs/**/*.md` path, a full SHA, and that the section holds a markdown table with a separator and at least two rows at that commit (`InterimVerificationPolicy.ValidateSelectionShape` `:264-289`, `RequireCommittedSelectionAsync` `:292-320`, `HasSelectionRows` `:322-341`). The script refuses it unless `-VerificationRound Interim` (`:910`), and the policy treats any selection as an Interim request (`:62`). Interim "ships disabled with every card `FullOnly` and no enablement path; do not request Interim" (`orchestration-loop.md:1286-1295`). | No script or server change in this card (D-3). The table is written to satisfy `HasSelectionRows` as-is, and a test proves it (V-2), so the follow-up that lifts the Interim gate needs no doc change. |
| The bundle can just say it. | Stage bundles cap at 2,500 characters and must be ASCII (`InstructionBundleTests.each_stage_bundle_is_ascii_and_under_the_size_cap`, `:583-605`). LF-normalised lengths now: `stage-code` 2366, `stage-test-design` 2491, `stage-review` 2449. Verbatim pins: `VerificationRoundInstructionTests` (`:19-100`, whole ROUND sentences), `ScopedVerificationInstructionTests` (`:14-110`), `InstructionBundleTests.C470_composed_roles_separate_vr_from_pc` (`:84-100`) and `stage_bundle_invariants_are_pinned_by_substring` (`:608-635`), `C467_V21_DeliveryInventoryAndReviewAreMandatory` (`:637-648`). | Every bundle edit is a swap. The exact replacement texts are in S3-S5, measured 2492 / 2481 / 2483 chars, ASCII, every pinned phrase retained (D-6). |
| The server already renders a verification block into the brief, so the pointer should go there. | `DelegationReportFormatter.VerificationProfileBlock` (`:253-288`) renders `round: Final (profile v1)`, the ordinary-scope sentence and the PCs line into every Code/Review brief; only the Interim branch renders `selection: <path>@<sha> section "<name>"`. | The brief-side pointer copies that exact shape as a goal line, `checkpoints: <path>@<sha> section "### Checkpoints"` (D-4), so moving it server-side later changes nothing the delegate reads. |
| The `[antiphon-progress]` sentence in `stage-code.md` is load-bearing. | `ReportingContract` interpolates `ProgressClaimContract(taskId)` for every `AgentTaskRole.Code` task (`DelegationReportFormatter.cs:424-444`) and the only pin, `InstructionBundleTests.cs:48`, is on `BuildBrief` output, not on the bundle. | The bundle's copy is dropped to pay for the CHECKPOINTS paragraph (D-6). |
| Brief length matters for non-Claude delegates. | Every Grok/Codex delegate receives a file pointer at any length; a Claude delegate types inline to ~40 KB (`orchestration-loop.md:566-576`, CARD-0353). | The goal may carry the pointer line and, when the plan is not yet landed, the pasted table; no size concern. |
| Extra rebuilds are always waste. | Investigation §4A: rebuilds after compile errors (0459 race tuple, 0490 lease/recovery) were useful mid-stream feedback; rebuilds with no new failure (0490: 19 builds, 8 short runs) were not. | A compile-fix rebuild inside a checkpoint's build step is the same CP-n rerun; only a build or test command outside the table is "unlisted" and needs a reason (D-11). |
| A doc-pinning test needs a new shape. | `StandingPipelinePolicyDocumentationTests` (`tests/Antiphon.Tests/Application/StandingPipelinePolicyDocumentationTests.cs`) collapses whitespace, compares case-insensitively, reads through `DelegateScriptRunner.RepoRoot`. `NightlyVerificationContractTests.RunHarnessCaseAsync` (`tests/Antiphon.Tests/Scripts/NightlyVerificationContractTests.cs:52-84`) runs one `-Case` of a `scripts/test-*.ps1` harness and requires its `PASS <prefix> ...` inventory and the `C487 HARNESS EXIT CODE: 0` trailer. | S7 mirrors the first; S6's C# wrapper reuses the second by lifting it into a shared helper (D-7). |

## Decisions

### D-1. The manifest is a `### Checkpoints` subsection of `## Verification design`, authored by TestDesign (by Plan when TestDesign is folded)

It sits between `### Out of scope` and `### Cost`, because Cost sums it. It is a markdown table with a separator and one row per checkpoint. Rejected: a separate JSON/YAML file beside the plan. Two artifacts drift, nothing consumes the JSON today, `docs/` holds JSON only as evidence (`2026-09-19-card-0574-startup-captures.json`), and CARD-0544 already chose "committed markdown table under a named heading" as the machine-checkable shape with `HasSelectionRows` as its validator. Rejected: a new top-level plan section. The TestDesign bundle's structure is what agents actually follow, and the folded-Plan case would then need two places.

### D-2. A row is one isolated build plus one exact filter group, bound to the slice it closes

Columns: `CP | After | Build | Group | Filter | Covers | Expect | Min`. `After` names the plan slice(s) whose commits must exist before the row runs; rows are ordered by it. `Build` names the project and forward-slash `OutputPath` (`tests/Antiphon.Tests -> bin-c459/`), or `CP-n` to run `--no-build` against that earlier row's output, allowed only when this row's `After` equals that row's `After` (same committed source), which is how CARD-0459's one-build-sixteen-groups stays legal without sixteen rebuilds. `Filter` is the exact `--treenode-filter` (combined-class syntax per CARD-0403) or, for a non-TUnit group such as `scripts/test-worktree-residue.ps1`, the exact command. `Covers` lists V/R IDs so Review can check the union of rows is the whole ordinary scope. `Expect` is the roster rule: by default "every listed class/method executed, 0 failed", or a minimum executed count for a lane like `[Category=Unit]`. `Min` is the estimated minutes; the Cost block's ordinary floor is their sum. Rejected: file-level rows ("after editing `Foo.cs` run X"). The investigation's granularity is class/filter groups, which is what TestDesign already produces; file-level rows would be a second, finer manifest nobody has ever written.

### D-3. Consumption is a brief convention plus a bundle obligation; no `delegate.ps1` flag and no server field in this card

The existing `verificationSelection` machinery is the right eventual home (same triple, same table validator), but lifting its Interim-only gate is product work: `InterimVerificationPolicy.cs:62` (selection implies Interim), persistence for Final tasks (`VerificationAdmissionJson` is written only on Interim admission, so a column or migration), `VerificationProfileBlock` rendering, the script's refusal at `delegate.ps1:902-947`, and tests for each. That is the ballooning the brief warns about. The table is shaped so that follow-up needs no doc change (V-2 proves `HasSelectionRows` accepts it). Rejected: a `-Checkpoints <text>` switch that merely appends a line to `-Goal`; it would look like enforcement and enforce nothing.

### D-4. The pointer line mirrors the server's Interim `selection:` rendering

`checkpoints: <plan artifact path>@<full plan commit sha> section "### Checkpoints"`. One physical line in `-Goal`, next to the verbatim `handoff:` and `artifact:` the recipes already require. The SHA is the commit the Code delegate reads the table at (normally the landed plan/test-design commit). When the plan is not landed yet, the orchestrator pastes the table into the goal under the same line; the pointer still names the commit.

### D-5. Binding comes from four places, none of them a new mechanism

(a) `stage-code` obliges the closed list and the per-row line; (b) `stage-review` makes a missing row, zero count, unlisted run without reason, or stub test a defect; (c) `stage-test-design` requires the table so it always exists; (d) the doc owns the schema and the runner. The investigation shows CARD-0459's list existed and was still not executed as closed; what reaches a fresh delegate is the bundle (README: "recorded anywhere else ... it reaches nobody"), and what makes it stick is Review checking rows, not prose.

### D-6. Bundle edits are exact swaps under the cap; the replacement texts are in this plan

Measured with `tr -d '\r' | wc -m`: `stage-code` 2492, `stage-test-design` 2481, `stage-review` 2483; zero non-ASCII characters; every phrase pinned by `VerificationRoundInstructionTests`, `ScopedVerificationInstructionTests`, `InstructionBundleTests` and `C467_V21` retained verbatim. Paid for by dropping, from `stage-code`: the `[antiphon-progress]` sentence (already interpolated by `ReportingContract` for every Code task, see Ground truth), "Finish every owned command before settlement" (delegate-basics rule 1), "Mutation stays per-PC method-scoped" (restated two paragraphs later), "Build once ... Report the filters and actual expanded counts" (subsumed by CHECKPOINTS); from `stage-test-design`: wording compression of the Delivery inventory paragraph with every pinned token kept on one line; from `stage-review`: "Do not expand to a broad Application/full-assembly sweep merely because this is Review" (covered by "a broad run without named invariant/cost is a defect") and "Bundle text tests prove no delivery path" (a comment, not a rule). Code must re-measure after applying and must not "fix" an overrun by editing pinned sentences.

### D-7. A runner script standardises the row's evidence; it runs one row, never the table

`scripts/run-checkpoint.ps1` builds (unless `-NoBuild`), runs one filter into a fresh results directory, parses the TRX counters and the executed `Class.Method` roster, and prints the report line plus the roster so the delegate never opens the TRX. It refuses a backslash or trailing-space `OutputPath` (CARD-0448 hazard), a pre-existing results directory, zero executed tests (native exit 8), and a roster miss. CARD-0459 wrote this inline as `Invoke-C459Tests`; CARD-0490 had nothing and rebuilt nineteen times. Rejected: an executor that iterates the whole table (apply-all shape; also loses "which slice went red"). Rejected: no script (every Code delegate re-derives TRX parsing, which is the read-only loop tax the investigation measured at 43-47 %). The bundle says "the plan's table"; the doc says "use `scripts/run-checkpoint.ps1` or produce the identical line", so the bundle stays repo-agnostic in wording.

### D-8. The stub rule is static and lives in Code and Review, not in a Code-run red/green

"A new test that cannot go red against the production line it guards (self-compare, constant, no outcome assertion) is a stub, not done." Code does not run PCs (that is Mutation's cycle and doubling it is the cost this card is about); the rule is checkable by reading the body against the R-n "decisive assertion" column, which is what Review does. Rejected: requiring Code to demonstrate red for every new test.

### D-9. `stage-plan.md` is unchanged; AGENTS.md, §3 and the skill each get one pointer

The plan bundle (830 chars) already requires slices with files and tests, and slice ids are what `After` binds to; TestDesign maps rows to slices after the plan exists. AGENTS.md gets one bullet under "Tests and builds" naming the owner, `orchestration-loop.md` §3 gets one bullet (the pointer line and `-ExpectAbout` = sum of Min), and the delegate skill's Code recipe line carries the pointer. Rejected: restating the schema in any of them (the README's "three places drift in three places").

### D-10. TestDesign is folded; next stage is Code

Docs, three bundles, one script with an offline harness, two test classes: every case is enumerable now, and this plan's own `### Checkpoints` table is the first use of the schema. The caller may still dispatch a separate TestDesign; nothing here prevents it.

### D-11. Unlisted runs are allowed, reported and reasoned, never silent

A compile error found by a checkpoint's build is fixed and the same CP-n rerun (reported as a rerun). A red that needs method-scoped isolation to diagnose is an unlisted run with the reason "isolate CP-3 red to one method". What is forbidden is a build or test command that appears in the transcript and nowhere in the report. Rejected: forbidding unlisted runs outright, which would make the useful mid-stream feedback the investigation found (0459 race tuple, 0490 origin) a rule violation.

### D-12. `Expect` defaults to roster-complete and zero failures; the script prints the roster so the check is a glance

`-Expect` tokens are substrings each matched against at least one executed `Class.Method`; `-MinExecuted` covers lanes with no fixed roster. The TRX inspection the Fast lane already demands ("inspect the fresh TRX method/class/argument roster against the tables") becomes reading the script's `EXECUTED` lines.

## The checkpoint manifest

Schema (this exact table is the S1 insert's first table; it is what V-2 reads through `HasSelectionRows`):

| Column | Meaning |
|---|---|
| `CP` | `CP-1`, `CP-2`, ... in run order. |
| `After` | Plan slice(s) whose commits must exist first: `S1`, `S1-S3`, `all`. |
| `Build` | `<project> -> <bin-x/>` (forward slash, `bin-` prefix), or `CP-n` to reuse that row's output with `--no-build`, allowed only when both rows share the same `After`. |
| `Group` | Short name, unique in the table, used in results paths and the report line. |
| `Filter` | The exact `--treenode-filter` (CARD-0403 combined-class syntax), or the exact command for a non-TUnit group. |
| `Covers` | The V-n/R-n IDs this row is evidence for; the union of all rows is the whole ordinary scope. |
| `Expect` | Roster rule: `all listed, 0 failed` (default) or `>= N executed, 0 failed` for a lane. |
| `Min` | Estimated minutes including the build when the row builds; Cost's ordinary floor is the sum. |

Rules the doc states and the bundles enforce:

1. The table is the closed list of builds and test runs for the round. Each row runs once, in order, after its `After` slice is committed; a red row is fixed and rerun as the same row (count the reruns).
2. Any other build or test command is unlisted: it is reported with a reason, never omitted.
3. Every row is reported as one line: `CHECKPOINT CP-n commit=<sha> build=<ok|reused|failed> filter=<filter> executed=N passed=N failed=N skipped=N trx=<path>` plus `reruns=k` when k > 0. The script prints exactly this line.
4. A new test that cannot go red against the production line it guards (self-compare, constant, no outcome assertion) is a stub, not done; Review rejects it.
5. Review checks the report's lines against the table: a missing row, zero count, unlisted run without reason, or a broad run without named invariant/cost is a defect.
6. Nothing in the table is skipped to save time; splitting a row that exceeds one foreground window is done by the classes/methods it already names.

Worked example (three rows of CARD-0459 at `484fb214`, as the doc's second table):

| CP | After | Build | Group | Filter | Covers | Expect | Min |
|---|---|---|---|---|---|---|---|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c459/` | disposition-surface | `/*/*/(TaskWorktreeRetirementTests*)\|(WorktreeResidueEndpointTests*)\|(WorktreeResidueScriptTests*)/*` | V-2, R-2 | all listed, 0 failed | 12 |
| CP-2 | S2 | `tests/Antiphon.Tests -> bin-c459/` | workspace-races | `/*/*/WorktreeRetirementRaceTests/*` | V-4 | all listed, 0 failed | 25 |
| CP-3 | S2 | CP-2 | settled-removal | `/*/*/SettledWorktreeRemovalTests/*` | V-3 | all listed, 0 failed | 25 |

Running one row:

```powershell
pwsh -NoProfile -File scripts/run-checkpoint.ps1 -Name CP-3 -Project tests/Antiphon.Tests -OutputPath bin-c459/ -NoBuild -Filter '/*/*/SettledWorktreeRemovalTests/*' -Expect SettledWorktreeRemovalTests -ResultsRoot .antiphon/c459-checkpoints
```

## Slices

Order matters for the checkpoints below: S1-S2 docs, S3-S5 bundles, S6 script, S7 tests. Commit and push each slice.

### S1. `docs/testing-and-build.md`: new `### Checkpoint manifest (CARD-0585)` under Fast lane

Insert after "### Alternate-output cleanup safety (CARD-0448)" (ends at `:97`) and before "### Simulating a stale `index.lock` (CARD-0543)" (`:99`). Content: two sentences of purpose (the closed list; what it does and does not save, citing the investigation), the schema table above verbatim, the six rules, the worked example table, the `run-checkpoint.ps1` usage and its exit codes (0 green, 1 failures, 2 invalid input/build failure/no TRX, 3 zero executed or roster miss), the report line format, and one sentence that Cost's ordinary floor is the sum of `Min`. Also amend the Fast lane's second paragraph (`:73`): after "The brief/verification section must list coverage-to-class." add "and end with the `### Checkpoints` table (below)". About 60 lines. The heading text must be exactly `### Checkpoint manifest (CARD-0585)` (V-2 reads it).

### S2. `docs/orchestration-loop.md` §3, `.claude/skills/antiphon-delegate/SKILL.md`, `AGENTS.md`

- `orchestration-loop.md` §3, after the "Give outcomes, not procedures" bullet (`:562`): one bullet. "**A Code brief names the checkpoint list.** Add one line, `checkpoints: <plan artifact path>@<full plan commit sha> section "### Checkpoints"`, the shape the server already renders for an Interim `selection:`. The table stays in the committed plan (owner: [testing-and-build.md](testing-and-build.md), Checkpoint manifest); `-ExpectAbout` is the sum of its `Min` column plus authoring. The `stage-code` bundle carries the obligation to run it as a closed list and to report per row; do not restate that."
- `SKILL.md` "Stage recipes": the paragraph at `:78-81` changes "`-ExpectAbout` for Code is the ordinary V/R floor from the plan's `## Verification design` → `### Cost` block ... plus authoring time" to name the `### Checkpoints` `Min` sum as that floor; the Code recipe line (`:94`) becomes
  `pwsh -NoProfile -File scripts/delegate.ps1 -Role Code -Card CARD-nnnn -Title "build <card>" -Worktree -ExpectAbout <cp-min-sum+authoring> -Goal "execute <plan artifact path> and its verification section; checkpoints: <plan artifact path>@<full plan commit sha> section \"### Checkpoints\"; handoff from test-design/plan: '<verbatim>'"` with its comment line reading `# Code — runs the plan's ### Checkpoints table as a closed list; -ExpectAbout is the Min sum + authoring`.
- `AGENTS.md`, "### Tests and builds", one bullet after the TUnit bullet: "- A Code stage runs the plan's `### Checkpoints` table as a closed list: one isolated build and one exact filter per row, reported per CP-n with counts; unlisted builds/test runs need a stated reason, and a new test that cannot go red is a stub. Owner: [docs/testing-and-build.md](docs/testing-and-build.md) (Checkpoint manifest, CARD-0585)."

### S3. `server/Bundles/stage-code.md` (exact replacement, 2492 chars LF, ASCII)

~~~
Implement the landed plan and its verification design, including its tests.

SCOPE: Ordinary verification is Unit plus the named affected integration classes from the plan's coverage-to-class list. Unit-only is insufficient for native delivery, landing, leases or persistence. A namespace/full-assembly run needs the cross-cutting invariant, unbounded classes and expected cost named first. See docs/testing-and-build.md Fast lane.

CHECKPOINTS: the plan's ### Checkpoints table is the closed list of builds and test runs. Run each CP-n once, in order, after its named slice is committed: one isolated build (forward-slash OutputPath), one filter, one fresh TRX; inspect fresh TRX for each intended class/method and nonzero counts. Fix a red CP-n, then rerun that CP-n. Any other build/test command is unlisted: report it with a reason. Report per CP-n: commit, filter, executed/passed/failed/skipped, TRX path, reruns. A new test that cannot go red against the production line it guards (self-compare, constant, no outcome assertion) is a stub, not done.

ROUND: the brief's verification profile governs. Final (default, first round): whole Unit lane, every full affected class, every ordinary V/R, required manual work. Interim (explicit only): cumulative changed cases since the full baseline incl. earlier repair cases, unresolved-finding tests, named adjacent smoke; unbounded shared impact needs Final. List deferred-to-final IDs; never mark them passed.

INVARIANTS: Run each V-n and R-n the round requires; report every ID and actual outcome. Commit and push each meaningful slice and final ordinary-tested state. Report full commit SHA, branch and exact worktree, original Code task ID (landing owner), plan artifact and evidence paths.

Report every PC-n/variant pending for Mutation and any noticed coverage gaps. Mutation owns every deliberate mutant, red/restore/green and missing-control discovery, incl. zero-PC plans. Never widen a timeout or loosen an assertion (see delegate-basics).

next: review when implementation and ordinary V/R are complete, even with zero PCs. Review precedes land; PCs stay pending for post-land Mutation. Include restart: server/runner/none and original landing owner in the handoff.

next: code when implementation or ordinary verification remains (name it); decide when a human choice blocks. Do not settle next: land; never land or deploy. The caller lands the original Code task after ordinary Review, then commissions SourceLanding Mutation.
~~~

### S4. `server/Bundles/stage-test-design.md` (exact replacement, 2481 chars LF, ASCII)

~~~
Design verification for the landed plan. Append this structure; do not rewrite the fix design.
Every guard that protects a safety-critical assertion gets a PC-n positive control.
Read touched tests and fixtures/helpers before naming cases, the nearest fixture for new files. Record missing setup; cover boundary combinations or justify exclusion.

## Verification design
### Inspection
- <test/fixture bodies read> | <boundaries -> V/R IDs or exclusion>
### Delivery inventory
For each new/changed async outcome-delivery path enumerate producer, destination, persistence boundary, recovery and observable receipt, joined by the durable identity. Include a producer-to-recipient test through the real queue: busy recipient, one already eligible, crash/enqueue-failure recovery at each handoff. A request, queue insert, event, Sent flag or transport ack never proves delivery. Session input needs the matching complete UserPrompt transcript. Declare substitutes and what each cannot prove. Reject a design that stops before recipient evidence. Every safety-critical delivery/recovery guard needs a named positive control.
### Proves it works now
- V-1: <behaviour> | <layer> | <test/command> | <expected>
### Guards the regression
- R-1: <regression> | <test and decisive assertion>
### Guard inventory
- G-1: <plan ref + safety-critical guard/invariant> | PC-1
Inventory every safety-critical guard, incl. untested; split independently bypassable guards. Map each 1:1 to a distinct PC-n. Justify none.
### Positive controls
- PC-1: break <G-1> by <compiling defect>; expect <exact method> red at <assertion>.
  Mutation runs break/red/restore/green after land; Code writes tests and runs V/R; Review judges before land.
### Out of scope
- <exclusion and reason>
### Checkpoints
| CP | After | Build | Group | Filter | Covers | Expect | Min |
One isolated build + one exact filter per row; After = plan slice; Covers = V/R IDs; union = whole ordinary scope. Schema in docs/testing-and-build.md.
### Cost
- Separate ordinary V/R floor (Code) = sum of CP Min, and PC floor (Mutation), with filters and minutes. Total = setup/build + V/R + every PC red/restore/green; label estimated/measured. Quantify savings; justify zero.

Before handoff: bodies read; guards=N, mapped=N, missing=0, duplicate PC mappings=0; all PCs executable; numeric Cost. No placeholders/TBD.
next: code only when complete; plan for an unverifiable seam; decide for a human choice. Commit and push the plan doc.
~~~

### S5. `server/Bundles/stage-review.md` (exact replacement, 2483 chars LF, ASCII)

~~~
You are reviewing the build against its plan.

SCOPE: Re-run the claimed **scoped** ordinary checks (Unit plus named affected integration classes) before land. Executed PCs are not a prerequisite; Mutation runs them after publication. Check the Code report's CP-n lines against the plan's ### Checkpoints table: a missing row, zero count, unlisted build/test run without a reason, a broad run without named invariant/cost, or a new test that cannot go red (self-comparison, constant, no outcome assertion) is a defect. See docs/testing-and-build.md Fast lane.

ROUND: the brief's verification profile governs. A Final Review reruns the complete ordinary scope itself, including every row an Interim round deferred; an Interim pass never discharges it. Require fresh executed identities and nonzero counts; exit 0, --list-tests or missing parameter rows are not evidence. Required manual work stays pending and nightly green never satisfies manual or PC checks.

INVARIANTS: Read-only. Do not fix anything. Read the diff against the plan and its verification section; re-run claimed ordinary tests; judge ordinary evidence and PC evidence read-only (PCs stay pending). Reject missing regression tests or ordinary evidence. Carry the original Code landing owner through every handoff. Defects as Where / Failure / Why / Fix.

Audit each asynchronous delivery inventory: producer, destination, persistence boundary, recovery, observable receipt, durable identity. Trace ordinary V/R evidence through the real queue to busy and eligible recipients with crash/enqueue failures at each handoff. Session acceptance requires matching complete UserPrompt transcript evidence, not a queue insert, event, Sent flag or transport ack. Reject a missing producer-to-recipient test or a design that stops before recipient evidence as a defect.

Before the next-stage block, emit exactly one standalone review-evidence block:

```
--- review evidence ---
subjectTaskId: <full GUID of the original Code/Worktree landing owner>
reviewedSourceSha: <full SHA actually reviewed>
ordinaryScopeCompleted: <Full|Interim|None>
```

Full only when the complete required selection executed. The caller lands that original Code owner with `-ExpectedSourceSha` from this evidence, never the Review or a follow-up task.

next: land when there are no defects and this was a Final Review; review (Final) when a clean Interim; code when there are defects (name them in `handoff:`); decide when a human choice blocks.
~~~

After S3-S5, re-measure each file with `tr -d '\r' < server/Bundles/<f>.md | wc -m` (must be <= 2500) and confirm `LC_ALL=C grep -c '[^ -~]'` on the `\r\n\t`-stripped text is 0. Keep CRLF/LF as the repo's `.gitattributes` dictates; the composer LF-normalises before hashing.

### S6. `scripts/run-checkpoint.ps1`, `scripts/test-run-checkpoint.ps1`, fixtures, `tests/Antiphon.Tests/Scripts/RunCheckpointScriptTests.cs`

`scripts/run-checkpoint.ps1` (pwsh 7, ASCII-only, `$ErrorActionPreference = 'Stop'`):

```powershell
param(
    [Parameter(Mandatory)] [string]$Name,          # CP-3
    [Parameter(Mandatory)] [string]$Project,       # tests/Antiphon.Tests
    [Parameter(Mandatory)] [string]$OutputPath,    # bin-c459/
    [Parameter(Mandatory)] [string]$Filter,        # --treenode-filter value
    [string]$ResultsRoot = '.antiphon/checkpoints',
    [switch]$NoBuild,                              # row's Build column says CP-n (reuse)
    [int]$MinExecuted = 1,
    [string[]]$Expect,                             # each must match an executed Class.Method (substring, case-insensitive)
    [string]$DotnetShim                            # harness only: a .ps1 invoked instead of dotnet
)
```

Behaviour, in order: (1) refuse `OutputPath` not matching `^bin-[A-Za-z0-9._-]+/$` (backslash, trailing space, missing `bin-` prefix) with a message naming the CARD-0448 argv hazard, exit 2; (2) results directory `<ResultsRoot>/<Name>-<stamp>` where `<stamp>` is `<yyyyMMdd-HHmmss>-<4 hex>`, or the literal value of `$env:C585_STAMP` when set (harness only); refuse if it exists, create it; (3) unless `-NoBuild`, run `dotnet build <Project> --property:OutputPath=<OutputPath> --nologo`; nonzero prints `CHECKPOINT <Name> build=failed` and exits 2; (4) run `dotnet run --project <Project> --no-build --property:OutputPath=<OutputPath> -- --treenode-filter <Filter> --report-trx --report-trx-filename run.trx --results-directory <dir>`; (5) if no `run.trx` was written, exit 2; parse `Counters` (`total`, `executed`, `passed`, `failed`; skipped = total - executed) and join `UnitTestResult` outcome to `TestDefinitions/UnitTest/TestMethod@className` + `@name` as `test-duration-tripwire.ps1` does (never the display name); (6) print `CHECKPOINT <Name> commit=<git rev-parse HEAD> build=<ok|reused> filter=<Filter> executed=N passed=N failed=N skipped=N trx=<full path>`, then one `FAILED <Class.Method>` line per failure, then `EXECUTED <Class.Method>` lines capped at 300 with `EXECUTED ... +N more`, then `CHECKPOINT <Name> EXIT CODE: <n>`; (7) exit 1 if `failed > 0`, exit 3 if `executed < MinExecuted` or any `-Expect` token matches no executed name, else 0. The script never deletes anything, never edits a filter, never calls `--list-tests`. `-DotnetShim` substitutes the executable for both invocations so the harness runs with no build.

`scripts/test-run-checkpoint.ps1`: same shape as `scripts/test-stage-value-report.ps1` / `test-nightly-health.ps1` (`-Case`, `-ResultsDirectory`, `PASS C585 <name>` rows, `C487 HARNESS EXIT CODE: <n>` and `C487: <n> passed, 0 failed, <n> rows` trailer so the existing wrapper helper accepts it). It writes a temporary shim `.ps1` that, for `build`, exits with `$env:C585_BUILD_EXIT`, and for `run`, copies the fixture TRX named by `$env:C585_TRX` into the `--results-directory` argument and exits with `$env:C585_RUN_EXIT`. Fixtures (three small TRX files, TUnit shape with `Counters` and `TestDefinitions`): `scripts/fixtures/c585-green.trx` (3 executed, 3 passed), `scripts/fixtures/c585-failures.trx` (3 executed, 1 failed), `scripts/fixtures/c585-zero.trx` (0 executed). `scripts/fixtures/` is new. Cases: `C585_Green`, `C585_Failures`, `C585_ZeroExecuted`, `C585_RosterMiss`, `C585_BadOutputPath`, `C585_BuildFailed`, `C585_FreshResultsDir`, `C585_NoBuild`, `C585_LineFormat` (see V-5..V-13 for what each asserts).

`tests/Antiphon.Tests/Scripts/RunCheckpointScriptTests.cs`: `[Category("Integration")]`, `[ParallelLimiter<ProcessSpawnLimit>]`, one `[Test]` per case calling a shared helper. Lift `RunHarnessCaseAsync` out of `NightlyVerificationContractTests` into `tests/Antiphon.Tests/Scripts/ScriptHarness.cs` (`internal static class ScriptHarness`, same body, same 120 s timeout, same cleanup) and call it from both classes; the nightly class's `RunCaseAsync` becomes a one-line forward. No behaviour change to the nightly tests.

### S7. `tests/Antiphon.Tests/Application/CheckpointManifestDocumentationTests.cs` and bundle pins

Mirror `StandingPipelinePolicyDocumentationTests` (collapse whitespace, case-insensitive, `DelegateScriptRunner.RepoRoot`). Methods are named in V-1..V-4. Add to `InstructionBundleTests.stage_bundle_invariants_are_pinned_by_substring`: `code.ShouldContain("### Checkpoints")`, `code.ShouldContain("is a stub, not done")`, `code.ShouldContain("unlisted")`, `testDesign.ShouldContain("### Checkpoints")`, `review.ShouldContain("CP-n lines")`, `review.ShouldContain("cannot go red")`. Commit as the last slice; a documentation pin needs to be green at the commit that carries it.

## Verification design

TestDesign folded into this Plan (D-10). Bodies read: `InstructionBundleTests.cs:40-100, 575-660`, `VerificationRoundInstructionTests.cs` (whole), `ScopedVerificationInstructionTests.cs:1-110`, `StandingPipelinePolicyDocumentationTests.cs:1-60`, `NightlyVerificationContractTests.cs:1-84`, `InterimVerificationPolicy.cs:255-345`, `test-duration-tripwire.ps1:1-60`, `test-stage-value-report.ps1:1-40`.

### Inspection

- `InstructionBundleTests` size/ASCII/state guards and substring pins; `VerificationRoundInstructionTests` verbatim ROUND pins; `C467_V21` token pins | boundaries: 2,500-char cap (S3-S5 measured), non-ASCII (0), each pinned token present on one line -> R-1, R-2.
- `StandingPipelinePolicyDocumentationTests` shape | boundary: phrases wrapping across markdown lines -> collapse whitespace in V-1..V-4.
- `NightlyVerificationContractTests.RunHarnessCaseAsync` | boundary: trailer regex `C487: N passed, 0 failed, N rows` and `C487 HARNESS EXIT CODE: 0` -> the new harness prints the same trailer (V-5..V-13 run through it).
- `InterimVerificationPolicy.HasSelectionRows` | boundaries: heading match by text or anchor, section ends at a heading of the same or higher level, needs a separator row plus >= 2 table lines -> V-2 uses the exact heading `Checkpoint manifest (CARD-0585)`; the schema table has 8 rows.
- No fixture for TRX exists under `scripts/`; S6 adds `scripts/fixtures/` (recorded missing setup).

### Delivery inventory

None. No asynchronous outcome-delivery path is added or changed; the script is a synchronous foreground command and the docs/bundles are static text. No substitutes claimed.

### Proves it works now

- V-1: every copy carries the manifest vocabulary | docs/bundles | `CheckpointManifestDocumentationTests.the_checkpoint_phrases_are_pinned_in_every_copy` | `docs/testing-and-build.md` contains `### Checkpoint manifest (CARD-0585)`, `CP-n`, `run-checkpoint.ps1`, `closed list`; `docs/orchestration-loop.md` and `.claude/skills/antiphon-delegate/SKILL.md` contain `checkpoints:` and `### Checkpoints`; `AGENTS.md` contains `### Checkpoints` and `CARD-0585`; `server/Bundles/stage-code.md` contains `### Checkpoints`, `closed list`, `unlisted`, `stub, not done`; `stage-test-design.md` contains `| CP | After | Build | Group | Filter | Covers | Expect | Min |`; `stage-review.md` contains `CP-n lines` and `cannot go red` (all after whitespace collapse, case-insensitive).
- V-2: the doc's section is a selection-readable table | server validator over doc text | `CheckpointManifestDocumentationTests.the_doc_section_is_a_selection_readable_table` | `InterimVerificationPolicy.HasSelectionRows(File.ReadAllText(docs/testing-and-build.md), "Checkpoint manifest (CARD-0585)")` is true.
- V-3: the Code recipe carries the pointer | skill + loop doc | `CheckpointManifestDocumentationTests.the_code_recipe_carries_the_checkpoints_pointer` | SKILL.md contains `checkpoints: <plan artifact path>@<full plan commit sha> section` and `-ExpectAbout <cp-min-sum+authoring>`; orchestration-loop.md contains `sum of its` and `Min` within the §3 bullet (collapsed).
- V-4: the review rule names each defect class | bundle | `CheckpointManifestDocumentationTests.the_review_bundle_names_each_checkpoint_defect` | stage-review.md contains `missing row`, `zero count`, `unlisted build/test run without a reason`, `cannot go red`.
- V-5: green row | script via harness | `RunCheckpointScriptTests.C585_Green` (harness case with `c585-green.trx`, `C585_RUN_EXIT=0`) | exit 0; line `CHECKPOINT CP-1 ... executed=3 passed=3 failed=0 skipped=0 trx=`; three `EXECUTED` lines; trailer `CHECKPOINT CP-1 EXIT CODE: 0`.
- V-6: zero executed is red | script | `RunCheckpointScriptTests.C585_ZeroExecuted` (`c585-zero.trx`, runner exit 8) | exit 3; line shows `executed=0`; no `EXECUTED` lines.
- V-7: roster miss is red | script | `RunCheckpointScriptTests.C585_RosterMiss` (`c585-green.trx`, `-Expect NotInRoster`) | exit 3 and a `ROSTER MISS NotInRoster` line.
- V-8: OutputPath guard | script | `RunCheckpointScriptTests.C585_BadOutputPath` (`bin-x\`, `bin-x/ ` with trailing space, `x/`) | exit 2 for each, message contains `forward slash` and no `dotnet` invocation (shim records zero calls).
- V-9: fresh results directory | script | `RunCheckpointScriptTests.C585_FreshResultsDir` (the harness sets `$env:C585_STAMP` so the computed directory name is known, pre-creates it, runs) | exit 2 with `results directory exists` and no `dotnet` invocation; a second run under a different stamp succeeds.
- V-10: build failure stops the row | script | `RunCheckpointScriptTests.C585_BuildFailed` (`C585_BUILD_EXIT=1`) | exit 2; line `CHECKPOINT CP-1 build=failed`; the shim records no `run` invocation.
- V-11: `-NoBuild` reuses output | script | `RunCheckpointScriptTests.C585_NoBuild` | shim records no `build` invocation; line shows `build=reused`; exit 0.
- V-12: failures are red with names | script | `RunCheckpointScriptTests.C585_Failures` (`c585-failures.trx`, runner exit 1) | exit 1; `failed=1`; one `FAILED <Class.Method>` line naming the fixture's failing method by class name from `TestMethod@className`, not display name.
- V-13: line format is stable | script | `RunCheckpointScriptTests.C585_LineFormat` | the first output line matches `^CHECKPOINT CP-1 commit=[0-9a-f]{40} build=(ok|reused) filter=.+ executed=\d+ passed=\d+ failed=\d+ skipped=\d+ trx=.+$` and the last matches `^CHECKPOINT CP-1 EXIT CODE: \d$`.
- V-14: harness inventory | harness | `pwsh -NoProfile -File scripts/test-run-checkpoint.ps1` (all cases) | `C487 HARNESS EXIT CODE: 0`, zero `FAIL` rows (exercised per case through V-5..V-13's wrapper).

### Guards the regression

- R-1: stage bundles stay under the cap and ASCII | `InstructionBundleTests.each_stage_bundle_is_ascii_and_under_the_size_cap` for `stage-code`, `stage-test-design`, `stage-review` | `text.Length <= 2500`, every char < 128.
- R-2: every verbatim ROUND/scope pin survives the swap | `VerificationRoundInstructionTests` (6 methods), `ScopedVerificationInstructionTests` (whole class), `InstructionBundleTests.C470_composed_roles_separate_vr_from_pc`, `stage_bundle_invariants_are_pinned_by_substring`, `C467_V21_DeliveryInventoryAndReviewAreMandatory` | each `ShouldContain`/`ShouldNotContain` as written today.
- R-3: other pins in the edited docs survive | `StandingPipelinePolicyDocumentationTests`, `CommitOnSettleDocumentationTests` | unchanged assertions on `AGENTS.md`, `docs/orchestration-loop.md`.
- R-4: the nightly harness wrapper still works after the helper is lifted | `NightlyVerificationContractTests.C544_DailyValidity` (cheapest case, 6 rows) | exit 0, 6 `PASS C544` rows.
- R-5: the brief still carries the progress claim after the bundle sentence is dropped | `InstructionBundleTests` method at `:40-50` (the `[antiphon-progress:` pin on `BuildBrief`) | unchanged.

### Guard inventory

- G-1: the Code bundle states the closed-list obligation (D-5) | PC-1
- G-2: the doc's schema table is a selection-readable table (D-3 forward-compat) | PC-2
- G-3: the runner refuses zero executed tests (Fast lane: zero tests are not evidence) | PC-3
- G-4: the runner refuses a non-forward-slash OutputPath (CARD-0448) | PC-4
- G-5: the runner refuses a pre-existing results directory (fresh TRX rule) | PC-5
- G-6: the Review bundle rejects a stub test (D-8) | PC-6
- G-7: the stage-bundle size cap (existing, `each_stage_bundle_is_ascii_and_under_the_size_cap`) | PC-7
- G-8: the runner's roster check (D-12) | PC-8

guards=8, mapped=8, missing=0, duplicate PC mappings=0.

### Positive controls

Mutation runs these after land, method-scoped, red then restore then green; Code implements the tests and runs V/R only.

- PC-1: in `server/Bundles/stage-code.md` change `closed list` to `open list`; expect `CheckpointManifestDocumentationTests.the_checkpoint_phrases_are_pinned_in_every_copy` red at the `stage-code.md` `closed list` assertion.
- PC-2: in `docs/testing-and-build.md` delete the schema table's separator row (`|---|---|`); expect `CheckpointManifestDocumentationTests.the_doc_section_is_a_selection_readable_table` red (`HasSelectionRows` false).
- PC-3: in `scripts/run-checkpoint.ps1` remove the `executed -lt MinExecuted` branch; expect `RunCheckpointScriptTests.C585_ZeroExecuted` red at the exit-code-3 assertion.
- PC-4: in `scripts/run-checkpoint.ps1` change the OutputPath regex to `^bin-[A-Za-z0-9._-]+[/\\]$`; expect `RunCheckpointScriptTests.C585_BadOutputPath` red at the backslash case.
- PC-5: in `scripts/run-checkpoint.ps1` replace the exists-refusal with `New-Item -Force`; expect `RunCheckpointScriptTests.C585_FreshResultsDir` red at the `results directory exists` assertion.
- PC-6: in `server/Bundles/stage-review.md` delete `or a new test that cannot go red (self-comparison, constant, no outcome assertion)`; expect `CheckpointManifestDocumentationTests.the_review_bundle_names_each_checkpoint_defect` red at `cannot go red`.
- PC-7: append 40 characters of prose to `server/Bundles/stage-code.md`; expect `InstructionBundleTests.each_stage_bundle_is_ascii_and_under_the_size_cap` red for `stage-code` at the 2,500 assertion.
- PC-8: in `scripts/run-checkpoint.ps1` remove the `-Expect` loop; expect `RunCheckpointScriptTests.C585_RosterMiss` red at the exit-code-3 assertion.

Filters: `--treenode-filter "/*/*/CheckpointManifestDocumentationTests/<method>"`, `"/*/*/RunCheckpointScriptTests/<method>"`, `"/*/*/InstructionBundleTests/each_stage_bundle_is_ascii_and_under_the_size_cap"`; restore and refresh timestamps before each green.

### Out of scope

- The Grok 15-second auto-background and its wait-only loops: harness behaviour, not a manifest property (investigation §3).
- Interim rounds and their selection table: dormant (CARD-0544); the ROUND paragraphs are untouched and pinned.
- A server-side `verificationSelection` on Final rounds: §Not done, noted.
- `LandVerifyFilter` / production verifier: unchanged by policy (Fast lane text).
- A real `dotnet` build inside the harness: the shim stands in; the real command shape is exercised once by CP-2's own run of the script against `tests/Antiphon.Tests` (see Checkpoints, CP-4 uses the script for real).

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min |
|---|---|---|---|---|---|---|---|
| CP-1 | S3-S5 | `tests/Antiphon.Tests -> bin-c585/` | bundle-contracts | `/*/Antiphon.Tests.Application/(InstructionBundleTests*)\|(VerificationRoundInstructionTests*)\|(ScopedVerificationInstructionTests*)/*` | R-1, R-2, R-5 | all listed, 0 failed | 7 |
| CP-2 | S6 | `tests/Antiphon.Tests -> bin-c585/` | checkpoint-script | `/*/Antiphon.Tests.Scripts/(RunCheckpointScriptTests*)\|(NightlyVerificationContractTests*)/(C585_*)\|(C544_DailyValidity*)` | V-5..V-14, R-4 | all listed, 0 failed | 5 |
| CP-3 | S7 | `tests/Antiphon.Tests -> bin-c585/` | doc-pins | `/*/Antiphon.Tests.Application/(CheckpointManifestDocumentationTests*)\|(InstructionBundleTests*)\|(StandingPipelinePolicyDocumentationTests*)\|(CommitOnSettleDocumentationTests*)/*` | V-1..V-4, R-1, R-3 | all listed, 0 failed | 3 |
| CP-4 | S7 | CP-3 | unit-lane | `/*/*/*/*[Category=Unit]` (run through `scripts/run-checkpoint.ps1 -Name CP-4 -NoBuild -MinExecuted 1000`) | Final whole Unit lane | >= 1000 executed, 0 failed | 4 |

If CP-2's method-segment OR does not select on the pinned TUnit 1.44 (CARD-0403 notes it works for `Class/(A*)|(B*)` but the class-and-method OR combination above is unverified), split it into two rows sharing CP-2's build: `RunCheckpointScriptTests/*` and `NightlyVerificationContractTests/C544_DailyValidity`. Delete every `bin-c585` directory before settling.

### Cost

Estimated, not measured; no build or test ran in this Plan.

| Ordinary Code floor | Minutes |
|---|---:|
| First build of `tests/Antiphon.Tests` into `bin-c585/` (setup) | 5 |
| CP-1 bundle-contracts (incremental build + 3 unit classes) | 2 |
| CP-2 checkpoint-script (incremental build + 10 pwsh spawns) | 5 |
| CP-3 doc-pins (incremental build + 4 unit classes) | 3 |
| CP-4 unit-lane (no build) | 4 |
| **Ordinary V/R after setup** | **14** |
| **Code total including setup** | **19** |

Authoring: script 45, harness and fixtures 35, docs and skill 25, bundles with re-measure 10, tests 15 = 130. `-ExpectAbout 150`.

Separate post-land Mutation floor: 8 PCs, each red 0.5 + restore/rebuild 0.7 + green 0.5 = 1.7, total 13.6, plus 5 snapshot setup/baseline = **18.6**. Total verification floor = 5 setup + 14 ordinary + 18.6 Mutation = **37.6 minutes**, estimated.

Savings: booked as zero on this card's own run (it is small). The expected saving is on later Code rounds: CARD-0490's 19 builds -> one per row (4-6 on its slice shape), and the roster read replacing TRX reads; the investigation's estimate is 20-35 % of a 0490-shaped round and under 20 % of a 0459-shaped round, reconstructed from loop mix, not measured. Measure it on the first two cards that run under the new bundles (§Not done, noted).

Handoff audit: bodies read; guards=8, mapped=8, missing=0, duplicate PC mappings=0; every PC is a compiling or text defect with an exact method and assertion; numeric Cost above. Next stage: Code.

## Risks

- **Two truths until relaunch.** A warm-pool or in-flight Code delegate keeps the old bundles until it retires (60 min idle) or relaunches; the drift badge shows it. Same window as every bundle edit.
- **The cap is tight.** `stage-code` lands at 2492 of 2500. The next standing rule for Code will have to pay for itself by removing text; that is the README's intended pressure, not a defect of this plan.
- **A delegate may still run unlisted commands.** The rule makes them reportable and Review-visible; it does not make them impossible. Enforcement stronger than a Review defect is the server-side follow-up.
- **Method-segment OR in CP-2 is unverified on TUnit 1.44** (CARD-0403). The split fallback is stated in the Checkpoints section; Code reports which form ran.
- **`HasSelectionRows` accepts any table.** V-2 proves shape, not semantics; row semantics are Review's job, by design (D-3).

## Not done, noted

- **Follow-up card (recommended): server-side `verificationSelection` on Final rounds.** Lift `InterimVerificationPolicy.cs:62` so a Final Code/Review request may carry a selection; persist it for Final tasks (a `VerificationSelectionJson` column or reuse of `VerificationAdmissionJson`, migration); render `selection:` in `VerificationProfileBlock`'s Final branch; relax `delegate.ps1:909-911` so `-VerificationSelectionFile` is accepted with `-VerificationRound Final`; then the orchestrator passes `{artifactPath, artifactCommitSha, section: "### Checkpoints"}` and the server refuses a dispatch whose table is missing at that commit. The table shape here already validates (V-2). Code change with `InterimVerificationPolicyTests`, `DelegateScriptKindTests`-style script tests and `InstructionBundleTests` brief rendering.
- **Measure the saving.** After the first two Code rounds under the new bundles, compare `dotnet build` count, test-command count, read-only loop share and `costUsd` against the investigation's before-state rows (`0e3d0465`, `af90420b`, `d3319023`). A Plan-stage note, not a code change.
- **`orchestrator.md` says nothing about Code briefs** (`server/Bundles/orchestrator.md:34` only names the handoff block). Adding the pointer rule there would reach sub-orchestrators; it costs bundle budget and was left to the orchestration skill/§3 for now.
- **The 15-second auto-background** on Grok is the remaining wait-only tax (8-15 % of loops). A harness/rules question for a separate card.
