# Instruction bundles

Every `*.md` file beside this one is a named, versioned block of standing instructions that
`InstructionBundleComposer` renders into an agent's launch instructions (CARD-0058,
plan `docs/superpowers/specs/2026-08-16-card-0058-0059-0060-instruction-bundles.md`). The carrier per
provider is described in
[Writing a brief](../../docs/orchestration-loop.md#3-writing-a-brief).

**This file is the only one here that is not a bundle** — it is excluded from the embedded-resource
glob in `Antiphon.Server.csproj`, and `InstructionBundleTests` pins the exact key set so an
accidentally-embedded file fails a test instead of reaching an agent.

## Editing one

- The file's **entire content is prompt text**. There is no frontmatter and no title heading; a
  comment you add for a human reader is text an agent pays for on every launch, so put that here
  instead.
- The **key** is the filename without `.md`; the **version** is the first 8 hex digits of the
  SHA-256 of the LF-normalised text, rendered as `[bundle:<key> v<hash8>]` above the body. Editing a
  bundle changes its version automatically — there is nothing to bump by hand.
- A change reaches an agent at a launch that composes the edited text. A fresh delegate gets it on
  dispatch; a warm pool delegate keeps what it launched with until the pool retires it
  (`Delegation:PoolIdleRetireMinutes`, configured default 60 idle minutes). Nothing types bundles
  into a live session. For a standing agent with drift, `PolicyRefreshService.ActAsync`
  (`server/Application/Services/PolicyRefreshService.cs`) applies its refusals and the Grok rules
  preflight before `ResolveLane` picks Relaunch or Notify; `PrepareToActAsync` gates follow. Tested
  outcomes:

  | Case | Test (TUnit, Antiphon.Tests) | What it asserts |
  |---|---|---|
  | `Auto`, Claude Code, AlwaysOn, transcript bound, idle | `PolicyRefreshServiceTests.Drift_and_idle_kills_resumes_and_records_incident` | the old session is killed, the new start carries `--resume`, the new stamp is recorded and a `PolicyRefreshed` incident is raised |
  | `Auto`, Codex | `PolicyRefreshServiceTests.Codex_is_Notify_not_a_kill` | no kill; one `PolicyDriftNotified` incident; the queued message says the change applies at the next launch |
  | `Auto`, transcript not bound | `PolicyRefreshServiceTests.Unbound_transcript_is_Notify_not_a_kill` | no kill; a `PolicyDriftNotified` incident and no `PolicyRefreshed` |
  | `Off` | `PolicyRefreshServiceTests.Off_mode_is_neither_lane` | the sweep acts on no agent and kills nothing; a forced refresh is refused with the not-resumable code |
  | Herdr pane with no stamp | `PolicyRefreshServiceTests.Herdr_null_stamp_does_nothing` | the sweep acts on no agent; no kill and no incident |
  | Session Working | `PolicyRefreshServiceTests.Working_is_skipped` | the sweep acts on no agent; no kill and no `PolicyRefreshed` |
  | Grok drift with no rules-file receipt, or with the rules payload removed | `PolicyRefreshServiceTests.Grok_legacy_policy_drift_refuses_before_kill_queue_or_stamp_mutation` | a forced refresh is refused `grok_rules_legacy_resume_requires_fresh_start` or `grok_rules_removal_requires_fresh_start`; no kill, no submitted body, no new session, stamps unchanged |
  | `Relaunch`, relaunch not eligible | not covered by a test | `ResolveLane` returns Notify |
- Never write `{agentName}` or `{channels}` into a bundle: `ChannelPreamble.Render` substitutes those
  over the whole composed append, so they would be replaced inside the bundle text too. A test pins
  this.
- Keep it **standing rules only**. Anything that will be wrong tomorrow — today's known-red tests, a
  warning count, "slices 1 and 5 already landed" — belongs in the brief for that one dispatch, not
  here. That split is the reason this directory exists.

Live operating settings are not bundle content. The generated file, its path, and why it stays out of this directory are owned by [Generated orchestrator instructions (CARD-0822)](../../docs/agent-instruction-file-contract.md#generated-orchestrator-instructions-card-0822).

## Fragments and `{{NAME}}` placeholders (CARD-1213)

`Fragments/*.md` are shared blocks a bundle pulls in by writing `{{NAME}}`, NAME being the fragment
key upper-cased (`Fragments/git-common.md` is `{{GIT-COMMON}}`). They are embedded by their own glob
and are not bundles: the catalog skips them, so they cannot be attached. `InstructionBundleComposer.Compose`
expands catalog bundle text only; the agent's own append is not scanned. In one composition the first
placeholder becomes `[fragment:<key> v<hash8>]` plus the text, later ones
`[fragment:<key> v<hash8>: see above]`, and the fragment stamp joins the stamp line after the bundle
stamps, so a fragment edit raises the drift badge. A `{{` that does not start a known, well-formed
placeholder fails the composition with the bundle, the token and the known names. A fragment opens
with `<NAME> contract v<n>.`, has an `INVARIANTS` line, contains no `{{`, `{agentName}` or
`{channels}`, and is at most 1,500 characters; the catalog refuses anything else. The tests that
exercise each rule are InstructionFragmentTests, plus the shipped-composition pins in
InstructionBundleTests (C1213_*); the checkpoint rows are CP-9 and CP-10 of
`docs/superpowers/plans/2026-10-13-card-1213-test-design.md`.

`delegate-basics` carries `{{GIT-COMMON}}`, so every non-specialist delegate role and the
orchestrator carry git-common once (C1213_RoleCompositionsResolve). git-common says stage rules may
narrow it, never widen it: Review's read-only INVARIANTS come first in its composition and still
govern (C1213_ReviewOverrideFollowsFragment).

Brief rules for git-common live in [Writing a brief](../../docs/orchestration-loop.md#3-writing-a-brief);
the two branch forms are in [Delegate branch names](../../docs/orchestration-loop.md#delegate-branch-names-card-1213).

## The orchestrator bundle's argv headroom (CARD-1332)

The composed append is one `CreateProcessW` argument, so the budget is counted in UTF-16 units
and the shipped worst case is the Orchestrator composition: `orchestrator` + `delegate-basics`
(with git-common expanded) + `board-api` + `style-explanatory` + the CRLF Telegram preset, beside
the launch's other arguments. Two measurements guard it, both in `Antiphon.Tests`:

| Measurement | Where | Fixed limit |
|---|---|---|
| The orchestrator **body**: `orchestrator.md` after the catalog's LF + Trim normalisation, LF and CRLF checkouts alike | `OrchestratorBundleOwnershipTests.C1332_OrchestratorBodyFitsItsAllocation` | at most **11,500** units |
| The whole **composition** as an argv estimate (each argument plus three, the append plus 28), for every non-specialist role and both task kinds | `InstructionBundleTests.the_worst_case_composition_measured_sits_far_under_the_budget` | at most **28,000**; the real guard is also called at the **29,500** reserve boundary, and the default budget must be the literal **30,000** |

The 28,000 early threshold is a fixed allowance, not today's size plus a margin: growth anywhere
in the composition (another bundle, a fragment, the preset) consumes it and fails the test before
the 500-character reserve is touched. The body allocation says *where* the growth happened when
it is the orchestrator. Neither test lowers or duplicates the reserve, and
`Delegation:CommandLineBudgetChars` stays the runner's 30,000 transport ceiling.

Editing `orchestrator.md`:

- Keep it **rules and pointers**. A procedure's full recipe, flags, refusal codes and examples live
  in its owner (`docs/orchestration-loop.md`); the bundle states the mandatory rule and links the
  owner anchor (`docs/orchestration-loop.md#delegate-start-ref`,
  `#repairsource-failed-owner-recovery`, `#standing-pipeline-policy`, `#verification-rounds`,
  `#runner-placement`). `C1332_BundleKeepsRulesAndOwnerPointers` requires each exact fragment and
  resolves it to exactly one `<a id>` anchor in the owner, and
  `C1332_OwnersContainRelocatedProcedures` pins the detail at the owner. Add a new pointer only
  with its anchor, and never restate the owner's detail in the bundle.
- The composition test prints its matrix on every run, pass or fail: one line per kind/role with
  the composed and estimated argv sizes, the signed headroom against 30,000 / 29,500 / 28,000, and
  an additive ledger (rendered bundle templates, separators, each fragment's replacement delta, the
  append), plus a maximum row. Read those lines from the console or the TRX output before trimming;
  the ledger total is asserted equal to the composed length.
- Every change to this file also changes its raw SHA-256 pin in
  `CheckpointRepeatDocumentationTests.repeat_budget_reaches_code_briefs_without_bundle_growth`:
  recompute it from the final LF bytes (`sha256sum server/Bundles/orchestrator.md` on an LF
  checkout) and rebuild `Antiphon.Server` before running `InstructionBundleTests`, which compares
  the embedded resource against the disk source. The content-derived `v<hash8>` version needs no
  hand edit.

## Which agent carries which bundle

Delegates get theirs from the role map in `InstructionBundles.ForDelegate` (Orchestrator tasks:
`orchestrator` + `delegate-basics`; a Worker whose role is a pipeline stage — Investigate, Plan,
TestDesign, Code, Mutation, Review — gets that stage's `stage-*` bundle then `delegate-basics`; other worker
roles: `delegate-basics`; specialist tasks: none, the specialist's contract is its own — and an
attachment does not reopen that, because the carve-out is about what the specialist can obey).
Stage bundles never name a kind: routing stays on pins, chains and RolePolicy.

Anything else is an **attachment**: an `AgentBundleAttachment` row naming this agent and this key,
edited in the agent settings modal. That is how `board-api` reaches an agent — it is in the catalog
but on no role, because widening the role map would hand it to every delegate of that role. Role
defaults come first in the composition, attachments after, and a bundle reachable both ways is
composed once.

Attachments are the only bundle state in the database. The key is a plain string with no foreign
key, so a bundle file **renamed or deleted** in a later PR leaves rows naming nothing: those are
dropped from the composition with a warning rather than failing the launch, and stop appearing in
the agent's "carries bundles" list. If you rename a bundle, detach the old key.

### The drift badge

`AgentSession.ComposedBundleStamp` records the stamp line a launch composed (`board-api v1a2b3c4d`
— stamps, never text). The agent DTOs expose `BundlesOutOfDate` when that no longer matches a
composition recomputed now, and the UI shows a quiet badge. It is informational: the agent picks the
new instructions up at its next launch and nothing forces one. Editing a bundle here will therefore
badge every agent carrying it until each next launches — that is the mechanism working, not a fault.

A rule that earns standing status gets PR'd into a file here. Recorded anywhere else — a findings
doc, a skill, one orchestrator's habit — it reaches nobody.

### The output-distiller bundle (CARD-0330)

`output-distiller.md` is the standing contract of the `antiphon-output-distiller` seat. Edits
arrive as Review cards from the weekly prompt-review loop
(`docs/orchestration-loop.md` §10), never as a live write to `SystemPromptAppend` or to the
running session. A review may change any sentence except the INVARIANTS block (pinned by
`InstructionBundleTests`); the file must stay at most 3 000 characters and open with
`You are the Antiphon OUTPUT DISTILLER (contract v`. The gates in `OutputDistillationGate` are
code, not this file — the loop must not change them. After a merge, stop and start the seat so
the new version composes; rollback is `git revert` plus the same restart.

## Reply styles

The `style-*` files (`style-normal`, `style-terse`, `style-caveman`, `style-brief`,
`style-phone`, `style-explanatory`) are chosen through `Agent.ReplyStyle`, never attached — `bundleKeys` refuses
them with 422 because two voices at once has nothing to dedup against. `Normal` composes to
nothing (the file still ships so every enum value has a block). The style block sits after
attachments and before the agent's own `SystemPromptAppend`, which keeps the last word.

`Phone` is explicit opt-in for human Telegram/Slack replies, including task/check/scheduled
follow-ups. Its sole wording owner is [style-phone.md](style-phone.md). Worker reports,
briefs, stage artifacts, specialists and terminal replies keep their own contracts.
Normal and both presets remain unchanged. See the [CARD-0417 plan](../../docs/superpowers/plans/2026-09-07-card-0417-channel-reply-conciseness-plan.md)
for the support-only deployment boundary and mandatory reviewed real-reply trial.

Default workflow: Code -> ordinary Review -> caller records same-board companion -> land
the original Code task -> confirmed publication -> required deployment -> SourceLanding
Mutation on the companion. Keep every PC/variant pending through Review; use a fresh
Worktree at O.VerifiedSourceSha. Follow the full CARD-0478 recipe in docs/orchestration-loop.md.
The landing outcome explicitly starts this continuation; do not synthesize a stage report.
Read parsed next= elsewhere. Mutation next=decide is caller triage of the full finding report,
not automatically a human question. Preserve the original Done verdict and keep the companion
open until its explicit disposition; no automatic card creation, tick spend or alert message. Commission binding-only at explicit Frontier, then eligible mechanical-execution at Low; one open Mutation per O.
Ordinary dispatch follows the live Role × complexity matrix; these scoped tier requests still respect routing pins. Low requires a bound mechanically-eligible pack; see docs/mechanical-pc-contract.md.
