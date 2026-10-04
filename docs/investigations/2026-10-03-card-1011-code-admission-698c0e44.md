# CARD-1011 Code admission: blocked on active footprint

Code/landing owner: `698c0e44-127d-4a7a-9584-7031570573e5`.
Branch: `feat/card-task-698c0e44`.
Worktree: `/work/worktrees/task-698c0e44` (runner mirror).
Start SHA: `a3f5951d2700f26f78414c036563eef93cf4f894`.
Plan: `docs/superpowers/plans/2026-10-03-card-1011-windows-grok-routing-plan.md`.

## Outcome

Admission stopped before implementation, test authoring, or any build. The brief
explicitly requires STOP on an overlapping planned file. No prompt, skill,
production routing pin, backend setting, or test source was changed. This report
is the only new tracked file.

## Admission evidence

Read the full inbox brief, card through `card.ps1 get CARD-1011 -Board Antiphon`,
and the complete 921-line frozen plan. The initial checkout was clean.

The source census at the start SHA matches the freeze:

| Selection | Existing methods/results | Planned results after additions |
|---|---:|---:|
| InstructionBundleTests | 42 / 62 | 67 |
| TaskPlatformGuidanceTests plus RunnerDefaultGuidanceTests | 9 / 9 (5+4) | 9 |
| StandingPipelinePolicyDocumentationTests | 7 / 10 | 10 |
| RunnerGrokAdapterTrustPromptTests | 4 / 4 | 4 |
| CP-1 total | 85 results | 90 |
| RoutingPinCandidateCreateTests | 17 / 17 | 17 |
| TaskPlatformPlacementTests | 15 / 15 | 15 |
| WindowsGrokRoutingPolicyTests | absent | 12 |
| CP-2 total | 32 results | 44 |
| CP-3 exact native selection | 1 / 2 | 4 |
| CP-4 LF/CRLF cap | 1 / 2 | 2 |
| CP-5 exact queue selection | 2 / 7 | 7 |
| CP-6 exact tailer selection | 1 / 1 | 1 |

Planned total remains 148; delta is zero. These are source-derived counts, not
execution evidence. Raw LF-normalized orchestrator UTF-16 length remains 14,310.
The freeze's proposed edit and its 14,116 result were not applied.

Live remote master was `261500e2ff0566cc2b0c370de1cc411f715163a7`, ahead of
the assigned freeze (including CARD-1014, CARD-0901 and CARD-1006). The branch
contract forbids rebasing this pushed task branch; no rebase was attempted.

At 18:16:54 UTC, GET `/api/agent-tasks/pipeline` showed four in-flight Code
tasks: this owner plus `818582a5`, `535715ec`, and `3aebd909`. All three were
Dispatched with working sessions in `delegate.ps1 -Status`. Their task DTOs
had null declared/observed scopes, so null was not treated as proof of absence.
Their task goals and freshly fetched branch footprints were inspected.

**Collision:** CARD-0959 owner
`818582a5-dcea-44e1-9884-e78807cd514a`, fetched branch tip
`87efb5b179aa503fd30931454ede09bd28bc3cf6`, changes both:

- `docs/agent-kinds.md`
- `docs/ai-agent-tui-configuration.md`

Both paths are expressly required by CARD-1011 S1. The exact overlap was
confirmed with `git diff --name-only origin/master...origin/feat/card-task-818582a5`
restricted to those two paths. CARD-1005 tip
`740c9b570f4361ff5baad2c863140a23a9712973` owns checkpoint-tool/census work;
CARD-1008 tip `786f0474533874a606cc962c6f6cd6b84a92ea1c` owns Docker/scripts
work. No CARD-1011 file edits were made in either footprint.

GET `/api/runner-defaults` returned revision 2 with no per-kind defaults.
GET `/api/session-runners` returned eligible Windows and Linux runners and
an unavailable stale third runner. These are observations, not embedded fleet
policy. Literal localhost:17202 was unreachable in the mirror; the successful
reads used the scripts' configured `ANTIPHON_API` and task header without
printing the token.

The actual checkpoint importer was **not run**: collision STOP took precedence
over its bootstrap. Importer acceptance remains unproven; do not claim six rows
accepted. No build/test driver ran: `slot=not-requested`, `waited=0`.

## Remaining work and gates

All implementation remains: five bundle tests, the twelve-case policy matrix,
two native backend cases, Explicit trust observer/probe, qualification evidence
scaffolding and frozen guidance. Continue only after the active overlap clears
or the caller assigns serialized ownership. Re-run admission against the next
assigned base, then perform the leased importer build/import before authoring.

V-1, V-2, V-3, V-4, V-5 and R-1, R-2, R-3, R-4 are all **not run**.
CP-1 through CP-6 are all **not run**; no TRX or checkpoint pass exists for this
task. Whole Unit remains **not run** under the explicit Final brief, even though
the frozen plan proposes a narrower Unit profile. No full assembly run occurred.
No red-first proof was attempted, and no new test is claimed complete.

Windows Debug tasks must run these exact frozen rows at the eventual committed
implementation SHA; do not run them in this Linux mirror:

| Row | Exact filter | Required expanded results |
|---|---|---:|
| CP-3 | `/*/*/RunnerGrokAdapterReadyTestsPty/(Fake_dashboard_marker_reaches_ready_and_complete_first_prompt*)\|(C1011_windows_backends_reach_ready_and_complete_prompt*)` | 4 |
| CP-4 | `/*/*/InstructionBundleTests/orchestrator_bundle_points_to_operational_autonomy_without_growing*` | 2 |
| CP-5 | `/*/*/SessionQueueReceiptPlumbingTests/(C475_QueueCommitAndTransportRecovery*)\|(C475_AlreadyIdleWhenIdleHasRecipientReceipt*)` | 7 |
| CP-6 | `/*/*/SessionMessageQueueGrokPtyIntegrationTests/Multiline_delivery_is_transcript_confirmed_through_the_real_grok_tailer*` | 1 |

WQ-1/2 require separate real Review evidence on actual inbox and modern hosts;
WQ-3 requires visible fresh-worktree trust, exactly one y, settled Ready, whole
UserPrompt, useful response and release, plus backend restoration. Its paid
Explicit filter is
`/*/*/RunnerGrokAdapterReadyTestsPty/C1011_real_fresh_worktree_trust*`, minimum 1,
after scaffolding exists and under a separate S0 commission. None is passed here.
WQ-4 remains post-activation work. The accepted d6e4138e canary does not discharge
unknown backend/trust/restoration evidence or implementation Review.

Prompt landing and live Debug pin activation remain gated by WQ-1/2/3,
restoration and independent Final/Full Review. Subsequent order: confirmed land,
canonical source advance/restart and `/api/version`, fresh pin/default/runner
reads, approved Debug pin write/readback, bundle stamp and idle-gated refresh,
WQ-4 receipt/release, acceptance, SourceLanding Mutation. This task never lands
or deploys. Restart now: **none**; eventual policy activation: **server**, with
separately owned runner restoration if backend experiments require it.

PC-1 through PC-57, every listed variant, remain **pending** for method-scoped
post-land SourceLanding Mutation. No control is waived or passed. Estimated
ordinary plan work is 38 minutes including importer, plus authoring/whole Unit/
slot waits and separate Windows qualification; the frozen PC floor is 495 minutes.
Next stage is Code, not Review, because implementation and ordinary V/R remain.
