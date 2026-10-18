# CARD-1321 closed-card Merge and Commit helpers

Confirmed at checkout `9b4d3c89a5912795a51956d84a5a235f5de2ffd6` on 2026-10-17. Reconstructed from source. The helpers already skip `card_implementation_closed`, and that exception is already one sentence in the lifecycle doc. What is not done is the witness: `C1288_ClosedCardBindingForms` never creates through a follow-up, and no test expects `card_implementation_closed` for `CardStatus.Canceled`.

Not a production defect. The helpers should keep inserting.

## Not already fixed

`origin/master` is `79dae0ed8dfc0b6f8b6a148b28f722584ecc2f69`. `origin/rc/2026-10-15` is `5e5d5cc728949420647d15a932766e70755a6f01`. This checkout is an ancestor of master and not of the release branch. For `AgentTaskService.cs`, `PipelineSpeedIntegrationTests.Admission.cs`, and `docs/agent-card-lifecycle.md`, `git diff HEAD` against both tips is empty.

The helper sentence landed in `1555df300429a04820c642f3101e6957f31ff966` (`docs(CARD-1288): state the closed-card CreateAsync refusal`) and is on both tips. Triage row 8 in `docs/investigations/2026-10-17-backlog-triage-2.md` on `origin/feat/card-task-ff95c795` still asks to document the exception and add the two witness arms. CARD-1321 (`9ee96a82-94b4-4468-ac52-482e664cfc9e`) is that follow-up from review `03d38d63`.

## CreateAsync refuses; the helpers do not

`CreateAsync` binds the card with no status filter, stores it, then re-reads that row under `FOR NO KEY UPDATE`. For an implementation role it throws `card_implementation_closed` when the locked card is Done, Canceled, or has `ArchivedAt` set.

- Bind input is `parent?.CardId ?? followUpCardId` (`server/Application/Services/AgentTaskService.cs:737-739`). `followUpCardId` is the prior task's `CardId` (`:460`), set before the live-or-retired split.
- A resolving title identifier outranks that inherited id (`server/Application/Services/AgentTaskCardBinder.cs:105-116`). A parent card outranks a follow-up (`AgentTaskService.cs:737-739`).
- The stored id is `binding.CardId` (`AgentTaskService.cs:1447`).
- The locked re-read is `:1575-1580`. The refusal is `:1651-1654`.
- Implementation roles are Code, Custom, Debug, Docs, Merge, Commit, and Deploy (`:1888-1890`). Merge and Commit are not specialists (`server/Domain/Enums/AgentTaskEnums.cs:431-432`), so a public `CreateAsync` of those roles still binds and still refuses. Mutation is absent from the list. `C1281_B3AdmissionFence` admits Mutation on a card flipped to Done (`tests/Antiphon.Tests/Application/PipelineSpeedIntegrationTests.cs:121-122`).

`CreateMergeTaskAsync` copies `conflicted.CardId` (`AgentTaskService.cs:4140-4141`) and `CreateCommitTaskAsync` copies `settled.CardId` (`:4272`). Both call `AdmitWorkspaceAsync` and `AgentTasks.Add` (`:4164-4165`, `:4298-4299`). Neither method reads the card. Callers do not check card status either:

- Land rebase conflict: `AgentTaskLandService.cs:654`. No `CardStatus` or `card_implementation_closed` in that service.
- Reply merge-back conflict: `AgentTaskReplyService.cs:2496-2507`.
- Commit-on-settle child: `AgentTaskReplyService.cs:4534-4538`, `:4552-4555`, and `SpawnCommitChildAsync` at `:4740`. `CommitOnSettleEligibility.IsEligible` (`server/Application/Services/CommitOnSettleEligibility.cs:9-15`) does not look at the card.

A close cancels only tasks that have not started and leaves a started task running (`server/Application/Services/CardTaskSettlement.cs:92-96`). The later sweep leaves a task created after `CompletedAt` or `ArchivedAt` open (`:157-180`). A Merge or Commit child spawned by that surviving settle or land is one of those later rows. Refusing it would stop a rebase conflict and a dirty settle on work the close already chose not to cancel.

The lifecycle owner already says this, in the paragraph after the sweep sentence (`docs/agent-card-lifecycle.md:61`):

`CreateMergeTaskAsync` and `CreateCommitTaskAsync` copy the parent card and do not apply this refusal.

`C1288_ClosedCardBindingForms` pins that sentence, including the helper clause (`tests/Antiphon.Tests/Application/PipelineSpeedIntegrationTests.Admission.cs:170-179`). `docs/ops-http.md:338-345` and `docs/antiphon-api.md:983-991` state the `CreateAsync` 409 and do not mention the helper carve-out. A second copy is not required.

## Witness gap

`C1288_ClosedCardBindingForms` (`Admission.cs:168-215`) sets `CardStatus.Done` and refuses Code three times: explicit card id (`ChildRequest`, `PipelineSpeedIntegrationTests.cs:146-148`), a title that contains the identifier, and a caller parent whose `CardId` is the Done card. Its own comment stops at those three (`Admission.cs:163-166`).

What that method does not do:

- `FollowUpOnTask` is never set. The open-card inherit pin is `AgentTaskCardBindingTests.a_follow_up_inherits_the_earlier_tasks_card` (`tests/Antiphon.Tests/Application/AgentTaskCardBindingTests.cs:206-225`). That card is the seed default, not Done or Canceled.
- `CardStatus.Canceled` is never the status under `card_implementation_closed`. The only other closed-card create codes are Done during the admission fence (`PipelineSpeedIntegrationTests.cs:109-119`) and `ArchivedAt` set while create waits (`Admission.cs:138-149`, `C1289_ConcurrentCardEdit`).

`w.Caller()` has a null task (`tests/Antiphon.Tests/TestHelpers/C544World.cs:313-314`), so a follow-up on that caller is not masked by a parent card. `TaskCountAsync` excludes only `Owner` (`C544World.cs:416-420`).

## Not done, noted

Extend `C1288_ClosedCardBindingForms` with a Canceled explicit-card refusal and one Shared follow-up refusal of the Done card (null caller task, title with no `CARD-` token, explicit Shared prior so the frozen-tip path is not taken); do not add the check to the helpers. Filter `/*/*/PipelineSpeedIntegrationTests/C1288_ClosedCardBindingForms`.

Those arms match the current predicate, so they are green on this HEAD. There is no failing test to commit first. Deleting `or CardStatus.Canceled` at `AgentTaskService.cs:1653` turns the Canceled arm red. Leaving `followUpCardId` unset at `:460` turns the follow-up arm red. The existing method was not re-run; a green run would not show the missing arms. No live row of a post-close helper was queried from this runner.
