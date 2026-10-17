# CARD-1288 Closed-card task refusal scope

Date: 2026-10-17. Tip: `7b3b9adbe98b18ae306b73cfb220331dc433f801` (`rc/2026-10-15`).
Card `fac9826f-e3b3-4f96-9e0f-226819053917`. Read-only. No test run.

## Verdict

`CreateAsync` already refuses a Done, Canceled, or archived card for `Code`, `Custom`, `Debug`, `Docs`, `Merge`, `Commit`, and `Deploy` after the card id is resolved. A title `CARD-nnnn`, the caller parent's card, and a follow-up's card all set that id. They do not skip the 409. The lifecycle sentence the card cites is the CARD-0738 sweep rule, and it is still the text on this tip. The missing work is to say that in `docs/agent-card-lifecycle.md` and to pin the three `CreateAsync` bind forms. Server-spawned merge and commit helpers are a second insert path and stay outside this fix.

## What the card and the triage claimed

The card says CARD-1281 S0 (plan V-26) added 409 `card_implementation_closed` for those seven roles on a Done, Canceled, or archived card, including a card bound only by a `CARD-nnnn` title or a parent task, while `docs/agent-card-lifecycle.md:59` still says such tasks are allowed.

The triage on `origin/feat/card-task-2ea653b0` (`docs/investigations/2026-10-17-backlog-triage.md`) asks to confirm whether a title-bound or parent-bound task on a Done card still escapes that code, and says the sentence at line 59 no longer says those tasks are allowed.

## CreateAsync mechanism

Binding runs before the row exists and does not read card status.

- `AgentTaskService.CreateAsync` takes `parent = caller.Task` (`server/Application/Services/AgentTaskService.cs:626`) and a follow-up's card from `prior.CardId` (`:460`).
- `AgentTaskCardBinder.BindAsync` (`server/Application/Services/AgentTaskCardBinder.cs:85`) returns no card for Check, Distill, and Diagnose (`:91-92`; `AgentTaskRoles.IsSpecialist`, `server/Domain/Enums/AgentTaskEnums.cs:431-432`).
- An explicit card is resolved with no status filter (`AgentTaskCardBinder.cs:129-155`). Identifier lookup is `Cards.Identifier == canonical` (`CardIdentifierScope.MatchesAsync`, `server/Application/Services/CardIdentifierScope.cs:108-118`).
- With no explicit card, the first `CARD-nnnn` in the title wins when it resolves (`AgentTaskCardBinder.cs:44-46`, `:107-116`). Otherwise the inherited id is `parent.CardId` or the follow-up card (`AgentTaskService.cs:739`, `AgentTaskCardBinder.cs:124-126`).
- An ambiguous title binds nothing (`AgentTaskCardBinder.cs:180-182`). `AgentTaskCardBindingTests.an_identifier_on_two_boards_with_no_scope_binds_nothing_and_says_so` covers that on an open card.
- The row stores `CardId = binding.CardId` (`AgentTaskService.cs:1447`).

Admission then loads that card under `FOR NO KEY UPDATE` (`AgentTaskService.cs:1575-1580`). The refusal is `AgentTaskService.cs:1651-1654`: when `admissionCard` is set and `IsImplementationAdmission(task.Role)`, a status of `CardStatus.Done` or `CardStatus.Canceled`, or a non-null `ArchivedAt`, throws `ConflictException` with code `card_implementation_closed` and message "Implementation work requires an open card." `IsImplementationAdmission` (`:1888-1890`) is `Code`, `Custom`, `Debug`, `Docs`, `Merge`, `Commit`, or `Deploy`.

The closed check is first, so a card that is already Done and a card moved to Done while the lock is held both return `card_implementation_closed`. `Mutation` is not in the set. Unbound creates have a null `admissionCard` and skip the check.

`docs/ops-http.md:338-345` and `docs/antiphon-api.md:983-991` already name this 409 for those seven roles on Done, Canceled, or archived cards, for ordinary create and a new handoff child. Neither document limits it to an explicit `-Card`.

## The lifecycle sentence

`docs/agent-card-lifecycle.md:59` is one paragraph from `4bb48ff4c2` (CARD-0738, 2026-09-26). `git blame -L 59,59` on this tip still attributes that commit. The paragraph ends: "A task created after the close, including a post-Done Mutation, is left alone."

That sentence is the sweep. `CardTaskSettlement.SweepAsync` (`server/Application/Services/CardTaskSettlement.cs:161-179`) sets `LeftOpenCreatedAfterClose` when `task.CreatedAt >= closeAt` and does not cancel the row. It does not grant `CreateAsync` permission. The triage reading that line 59 no longer says post-close tasks are left alone does not match this file.

## Witnesses that exist

| Witness | Bind form | Status | Result |
|---|---|---|---|
| `PipelineSpeedIntegrationTests.C1281_B3AdmissionFence` (`PipelineSpeedIntegrationTests.cs:91-123`, request `:146-148` with `Card: w.Card.Id`) | explicit guid | raced to Done, and NeedsDecision | Done → `card_implementation_closed`, zero tasks; NeedsDecision → `card_admission_changed`; explicit `Mutation` admitted; `Card = null` admitted |
| `PipelineSpeedIntegrationTests.C1289_ConcurrentCardEdit` (`PipelineSpeedIntegrationTests.Admission.cs:109-149`) | same explicit `ChildRequest` | archived, and unrelated title/description/owner/token edits | archived → `card_implementation_closed`; the other four edits admit |

`AgentTaskCardBindingTests` (`tests/Antiphon.Tests/Application/AgentTaskCardBindingTests.cs`) binds title (`:95`), parent (`:165`), follow-up (`:206`), and merge inheritance (`:229`) on cards seeded in Backlog (`SeedCardAsync`, `:457-467`). Default request role is `Custom` (`:368`), which is inside the refusal set, but those tests never move the card to Done, Canceled, or archived. No test names Canceled for this 409. Canceled is the same condition as Done at `AgentTaskService.cs:1653`.

## Second insert path

`CreateMergeTaskAsync` (`AgentTaskService.cs:4071`) and `CreateCommitTaskAsync` (`:4193`) assign `CardId` from the parent task (`:4141`, `:4272`) and `_db.AgentTasks.Add` (`:4165`, `:4299`) without `IsImplementationAdmission`. Callers are a landing rebase conflict (`AgentTaskLandService.cs:654`) and a reply merge-back conflict (`AgentTaskReplyService.cs:2506`). A parent-bound Merge or Commit created through `CreateAsync` is refused. The same roles spawned by those two helpers are not. Check/Distill/Diagnose inserts (`SpecialistTaskRunner.cs:363`, `LegacyCheckNotePublicationService.cs:250`) are specialist rows and are outside the seven roles.

`docs/superpowers/plans/2026-10-15-card-1289-admission.md:9` leaves the closed-card role set for this card. This investigation keeps that set as published in `ops-http.md` and `antiphon-api.md`.

## Smallest fix

Keep the `CreateAsync` refusal and the seven roles. In `docs/agent-card-lifecycle.md`, immediately after the sweep sentence on line 59, state all of the following:

- The sweep still leaves a task created after the close uncanceled, including post-Done `Mutation`.
- `CreateAsync` (ordinary create and a new handoff child) returns 409 `card_implementation_closed` for `Code`, `Custom`, `Debug`, `Docs`, `Merge`, `Commit`, and `Deploy` when the resolved card is Done, Canceled, or archived.
- That resolution includes an explicit card, the first `CARD-nnnn` in the title, the caller parent's card, and a follow-up's card.
- Unbound creates stay admitted. `CreateMergeTaskAsync` and `CreateCommitTaskAsync` copy the parent card and do not apply this refusal.

Red-first witness: one new method `C1288_ClosedCardBindingForms` on `PipelineSpeedIntegrationTests`. It reads `docs/agent-card-lifecycle.md` and requires the sentence above, so it fails on `7b3b9adbe`. It then creates `Code` on an already-Done card three ways: explicit guid, `Card = null` with the title `CARD-nnnn`, and `Card = null` with a title that has no `CARD-nnnn` authenticated as the child of a task bound to that card. Each must throw `ConflictException` code `card_implementation_closed` and leave no new task. The create arms already pass on this tip; they are the regression pin for a later exemption. A green create arm alone is not evidence of a new refusal.

One filter: `/*/*/PipelineSpeedIntegrationTests/C1288_ClosedCardBindingForms`

Do not run the class, the Unit lane, or `C1281_B3AdmissionFence` again for this change.

## Not done, noted

Extending `card_implementation_closed` into `CreateMergeTaskAsync` and `CreateCommitTaskAsync` would stop a landing or merge-back conflict from spawning its helper after Done; that behavior change is outside this fix.
