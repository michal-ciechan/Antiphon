# CARD-1034: Pin the published Review and Debug routing sentences

Status: ready for Code. Complexity: easy; TestDesign is folded into this plan.
Code budget: 30–60 minutes. Baseline: `ff57123014a952d522f8b896bb38780c73e1095b`.

## Outcome and scope

Make the documentation contracts fail when any of the three published routing
copies loses the Human Required Grok/High then ClaudeCode/High policy for both
Review and Debug on every platform, adds a Codex-first claim, or restores the
superseded publication hold. The current documentation is correct. Implement
tests only; no runtime, bundle, routing-pin, image-version or qualification change.

Implementation file:
`tests/Antiphon.Tests/Application/PublishedRoutingDocumentationTests.cs` (new).
Read the committed documents through `DelegateScriptRunner.RepoRoot`, following
the existing documentation tests. Use TUnit, Shouldly and `[Category("Unit")]`.

Protected inputs:

- `docs/orchestration-loop.md`
- `docs/agent-kinds.md`
- `docs/ai-agent-tui-configuration.md`

## Ground truth

| Card assumption | Observed source at the baseline | Consequence |
|---|---|---|
| Published routing is correct today. | The owner's `## Windows Review and Debug routing` section and the CARD-1011 paragraphs in both other documents publish the ordered Human Required pair for both roles on every platform. | Preserve their bytes; this is a missing-guard fix. No production defect was found in this review. |
| The published sentences are not pinned by the old roster. | `InstructionBundleTests.C1011_owner_role_wide_policy_and_fallback` already checks the owner's role/platform scope, Human/Required and ordered pair, using section-wide positive substring assertions. It does not check the equivalent sentences in the other two documents or reject an added contradictory sentence. | Retain that partial coverage and add guards for all three copies. The premise is a coverage gap, not total absence of tests. |
| The 14-class docs roster passed 176 cases. | `docs/investigations/2026-10-04-card-1026-1027-1019-docs-batch.md` records that result. The card reports the later surviving `Debug is Codex-first` insertion. This Plan has not rerun that historical mutation. | Reproduce the specific miss with PC-2 after implementation; do not rerun the 14 classes or whole Unit lane. |
| A publication-hold claim would be stale. | Commit `8154bc4eb` replaced the owner's `CARD-1011 publication hold`, the agent-kinds “approved future role-wide Debug policy is gated” text, and the TUI guide's “proposed Debug policy is inactive until” text. Current text says the prompt landed; WQ-4 remains post-activation acceptance. | Pin publication independently from provider order, retaining the legitimate outstanding WQ-4 work. |
| Grok 1.0.41 already has a guard. | `GrokRunnerImageContractTests.Qualified_pin_agrees_with_install_assertion_docs_fixtures_and_verifiers` checks the image installer, docs and verifier inputs against 1.0.41. | Reuse its single non-process method as a regression check; add no image-version test. |
| This needs a Windows lane because the owner heading says Windows. | The new tests read Markdown and make string assertions; the policy itself covers every platform. The existing routing integration suite seeds isolated DB pins, which is a different behavior. | Portable Unit lane; no database, pty, authenticated CLI or Windows-only acceptance run. |

Platform discovery was performed on 2026-10-04 using `GET /api/runner-defaults`
and `GET /api/session-runners`: defaults revision 2, no per-kind overrides, with
eligible Linux and Windows inventory. These are observations, not placement
constants. Fresh dispatch reads them again. Omit `-Runner` and `-Platform`;
use `-Platform Any` only to remove an inherited constraint. No host is pinned.

## Decisions

- **D-1 — One dedicated test class.** Keep the three related behaviors together
  and parameterize each method over the three literal document paths. This gives
  nine independently reported cases with the failing path in each assertion.
  Extending the already large `InstructionBundleTests` or creating three parallel
  classes adds unnecessary coupling or duplication.
- **D-2 — Literal policy clauses, normalized whitespace.** Use a small local
  reader/normalizer (`Regex.Replace(text, @"\s+", " ")`) and explicit expected
  strings. Positive assertions operate in the document's routing section below;
  heading lookup must fail visibly if missing. Do not derive expected values
  from another document, a routing implementation constant, or live API data:
  all copies could otherwise drift together. Do not snapshot whole documents or
  pin hashes/dates, which would reject unrelated editorial changes.
- **D-3 — Check absence as well as presence.** Check the known stale claims in
  the normalized full contents of each of these three current guides, ignoring
  case and inline backticks. This catches an appended contradiction even while
  the correct paragraph survives. Do not search historical investigation/plan
  documents, ban the word “pending”, or build a general natural-language policy
  parser. The guarantee covers the named claims, not every possible paraphrase.
- **D-4 — Publication is independent of remaining acceptance.** Require the
  landed prompt statement and the published pair, preserve WQ-1's excluded/not
  passed distinction and WQ-4's post-activation status. An image pin or a pending
  WQ-4 does not reopen the published policy. Leave live routing and all evidence
  ledgers untouched. No new operator policy decision is needed.
- **D-5 — Bounded verification.** Run nine new cases, five existing C1011
  methods and the one image-pin contract method, sharing one isolated build.
  Exactly one representative PC per new behavior, after separate Review and
  confirmed land. A cross-product of mutations across every guide/provider is
  unnecessary for this three-copy parameterization and Code budget.

## Implementation slices

### S1 — Add the documentation contracts

Create `tests/Antiphon.Tests/Application/PublishedRoutingDocumentationTests.cs`
with exactly the three parameterized methods in V-1 through V-3. Each has three
`[Arguments]` entries, one per protected path. Helpers remain private to the
class, read the actual repository file, and report the document and guard name
on assertion failure. Do not replace these tests with scratch-string self-tests.

Positive section boundaries (find the heading, then the next `## ` heading):

| Document | Heading |
|---|---|
| `docs/orchestration-loop.md` | `## Windows Review and Debug routing` |
| `docs/agent-kinds.md` | `## 5. Grok (xAI Grok Build TUI)` |
| `docs/ai-agent-tui-configuration.md` | `## Local Grok Build TUI profile` |

Commit and push the complete test class before running CP-1 through CP-3.
Keep source frozen throughout that run. Any necessary fix is a new commit,
followed by only the failed/affected row, with reruns reported. Do not edit the
three correct guides merely to simplify an assertion. An unexpected source
contradiction is a finding to report, not authorization to change policy.

## Verification design

### Behaviors and regressions

| ID | Class and method | Required assertions and result count |
|---|---|---|
| V-1 | `PublishedRoutingDocumentationTests.Required_pair_is_published_for_both_roles` | Three cases. Pin the document-specific policy clause below, including both roles, every-platform scope, Human Required and the ordered pair. Use diagnostic `published-required-pair` with the path. |
| V-2 | `PublishedRoutingDocumentationTests.Current_guides_do_not_claim_codex_first_review_or_debug` | Three cases. Reject `Debug is Codex-first` and `Review is Codex-first` in each full current guide after D-3 normalization; diagnostic `obsolete-codex-first`. Correct positive text elsewhere must not mask the added sentence. |
| V-3 | `PublishedRoutingDocumentationTests.Publication_is_landed_with_post_activation_acceptance` | Three cases. Each routing section must contain `The prompt change landed`, `WQ-1`, `operator-excluded`, the owner's `excluded (not passed)` or either short guide's `not passed`, and `WQ-4 remains post-activation acceptance work`. Owner also keeps `CARD-1011 published policy`. Reject the three historic hold claims below in every full current guide. Diagnostics `published-status`, `qualification-status`, `obsolete-publication-hold`, each with the path. |
| R-1 | `InstructionBundleTests.C1011_composed_windows_routing_contract`, `InstructionBundleTests.C1011_model_kind_and_tier_contract`, `InstructionBundleTests.C1011_owner_role_wide_policy_and_fallback`, `InstructionBundleTests.C1011_qualification_gates`, `InstructionBundleTests.C1011_activation_order` | Five existing cases retain owner pointers, pin/placement separation, fallback, qualification and activation ordering. No new cases or edits. |
| R-2 | `GrokRunnerImageContractTests.Qualified_pin_agrees_with_install_assertion_docs_fixtures_and_verifiers` | One existing case retains the independent Grok 1.0.41 image pin. Do not select the process-spawning image-version argument matrix. |

V-1's literals after collapsing whitespace (retain Markdown backticks for this
positive check; no date or SHA is required):

- Owner: ``Policy on release: Review and Debug on every platform, including Linux, follow the same policy; use Human Required pins with Grok/High then ClaudeCode/High (`grok-4.7`, then `opus`).``
- Agent kinds: `effective pin reads confirm Review and Debug on every platform use Human Required Grok/High then ClaudeCode/High.`
- TUI guide: `effective reads that day confirmed Human Required Grok/High then ClaudeCode/High for Review and Debug on every platform.`

V-3's forbidden literals, taken from the superseded source at `8154bc4eb^`:

- `CARD-1011 publication hold`
- `approved future role-wide Debug policy is gated`
- `proposed Debug policy is inactive until`

The actual policy owner and its valid pending WQ-4 text are the acceptance
canary for these checks: all nine cases must pass with current documents,
including the older historical canary tables. No whole-document exact equality,
database fixture, provider invocation or live pin read belongs in a unit test.

### Positive controls — post-land Mutation

Each PC is one behavior and one edit to one real document. Run the unchanged
method's three path cases in baseline, red and restored-green phases, with
fresh external evidence/output directories for each phase. These are planned
controls, not executed evidence and not part of ordinary Code CPs.

| PC | Behavior / method filter | Mutation | Expected red and restoration |
|---|---|---|---|
| PC-1 | V-1; `/*/*/PublishedRoutingDocumentationTests/Required_pair_is_published_for_both_roles*` | In the agent-kinds CARD-1011 paragraph only, replace `Grok/High then ClaudeCode/High` with `ClaudeCode/High then Grok/High`. | Three executed: one agent-kinds failure at `published-required-pair`, two pass. Restore exact bytes; three pass. |
| PC-2 | V-2; `/*/*/PublishedRoutingDocumentationTests/Current_guides_do_not_claim_codex_first_review_or_debug*` | Append `Debug is Codex-first.` as a paragraph to `docs/agent-kinds.md`, leaving all correct policy sentences intact. | Three executed: one agent-kinds failure at `obsolete-codex-first`, two pass. Restore exact bytes; three pass. This reproduces the reported surviving mutation. |
| PC-3 | V-3; `/*/*/PublishedRoutingDocumentationTests/Publication_is_landed_with_post_activation_acceptance*` | Add `**CARD-1011 publication hold.**` immediately after the owner's routing heading, preserving its existing published paragraph and WQ-4 sentence. | Three executed: one owner failure at `obsolete-publication-hold`, two pass. Restore exact bytes; three pass. |

Use the unchanged `scripts/run-checkpoint.ps1` copied with its `lib/build-slot.ps1`
dependency to the assigned external evidence root, as the testing owner requires
for SourceLanding. Every phase takes the host slot, uses a fresh forward-slash
`bin-c1034-pcN-phase/` output and a fresh external results root, `-MinExecuted 3`,
and `-Expect PublishedRoutingDocumentationTests.<method>`. Use the method-prefix
wildcard above for TUnit's argument rows, never a class/suite PC filter. Inspect
the path-specific TRX outcomes: build/fixture failure or zero tests is not red.
Await each phase before editing. Restore all mutations, record restoration,
remove owned alternate outputs, and keep all generated evidence external and
uncommitted. Mutation source snapshots never commit or push.

### Execution and evidence

Code and Review use only the checkpoint table below; the brief's narrow scope
overrides the usual whole-Unit recipe. All rows use the **portable Unit lane**
on the runtime-selected eligible host, with no OS or runner pin. No integration,
full namespace/assembly, browser, native or live-provider run is required.

Run the checkpoint tool once for the committed S1 group:
`run --plan docs/superpowers/plans/2026-10-04-card-1034-published-routing-sentences-plan.md --rows CP-1,CP-2,CP-3 --serial --expected-source-sha <committed-SHA>`.
Wait in the foreground to completion; if the tool returns 75, call `wait` again
and do not settle with its executor running. Use short wait windows to retain
status visibility. CP-2 and CP-3 reuse CP-1's exact build and source stamp.

If no compiled checkpoint tool exists, one explicitly declared preparatory
build is allowed through `scripts/build-slot.ps1` to `bin-c1034-tool/`; invoke
the resulting tool DLL so its own row gate is the only slot-owning layer.
Report that bootstrap separately. No raw build/test driver or `-NoSlot` retry.
Slot timeout exit 4 means not run/blocked, not permission to bypass the gate.

Validate the finished receipt with `scripts/validate-checkpoint-receipt.ps1`
against the actual committed source SHA and all three rows. Require the exact
15-case roster, zero failures/skips, clean source and verified build provenance.
Preserve unedited CHECKPOINT lines, counts, SHA and evidence paths in the stored
report. Confirm unexpected existing failures at the base with the failing
method only before calling them inherited; no timeout/assertion relaxation.

Also run `git diff --check` and `scripts/check-evidence-diff.ps1 -BaseRef
<Code-task-base> -HeadRef <exact-pushed-SHA>` over the complete Code task range;
these are source/history checks, not extra test builds. Keep TRX/log/JSON outputs
ignored. Remove only the alternate outputs owned by this run, including the
tool bootstrap; never remove shared daemon bins or unrelated scratch paths.

### Cost

Ordinary checkpoint floor: **7 minutes** (5 + 1 + 1), one isolated test build,
three exact selections, **15 executed cases**. Allow 20–35 minutes authoring and
review of diagnostics and 3–8 minutes for bootstrap/evidence/cleanup: fits the
30–60 minute Code budget absent a slot or infrastructure block. No repeats after
green. Separate post-land Mutation estimate: 15–25 minutes for the three
method-scoped baseline/red/green cycles (nine phase runs), excluding slot waits.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1034-routing/` | unit-published-routing | `/*/Antiphon.Tests.Application/PublishedRoutingDocumentationTests/*` | V-1, V-2, V-3 | all 3 named methods with all 3 document arguments each; exactly 9 executed, 0 failed/skipped | 9 | 5 | true |
| CP-2 | S1 | CP-1 | unit-existing-routing | `/*/Antiphon.Tests.Application/InstructionBundleTests/C1011_*` | R-1 | exactly the 5 named C1011 methods, 0 failed/skipped | 5 | 1 | true |
| CP-3 | S1 | CP-1 | unit-existing-image-pin | `/*/Antiphon.Tests.Infrastructure/GrokRunnerImageContractTests/Qualified_pin_agrees_with_install_assertion_docs_fixtures_and_verifiers` | R-2 | exactly the named method, 0 failed/skipped | 1 | 1 | true |

## Handoff and completion

Plan deliverable is this file only. It must be committed/pushed on the assigned
task branch and landed through the normal server primitive. An active task
cannot land itself: `AgentTaskLandService` requires Succeeded before admission;
the caller requests land after this Plan report settles, and handles any lease
hold. Do not push directly to master or rewrite the runner branch.

Code is next with this folded verification design. Code returns `next: review`
after ordinary CPs and the full-range evidence guard; separate Review remains
required. Following confirmed code publication, the caller commissions the three
PCs through the normal same-board SourceLanding Mutation companion. No server
restart, image rollout, routing change or WQ-4 canary is part of this test-only
card, and no pending qualification is claimed complete.
