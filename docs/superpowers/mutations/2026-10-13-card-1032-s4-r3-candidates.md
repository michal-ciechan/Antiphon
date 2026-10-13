# CARD-1032 S4 round 3: unbound Mutation candidates

Code task and landing owner: 4da2d6db-7b6a-4c58-aa47-4062e234712f.
All existing plan/TestDesign controls, PC-551..560 and PC-561..572 with every
named variant remain pending. Review then caller landing precedes SourceLanding
Mutation. No deliberate mutants, red/restore/green cycles or post-land PCs ran.
The 14 ordinary baseline failures do not discharge any PC.

| PC | Pending variants | Precise detecting filter | Expected red |
|---|---|---|---|
| PC-573 | add-executable; remove-executable | `/*/*/CompletedCardWorkspaceRemovalTests/C1032_InspectionR3Mode` | dirty_source differs |
| PC-574 | missing-head; missing-objects; missing-refs; directory-head; case-head; case-objects; case-refs | `/*/*/CompletedCardWorkspaceRemovalTests/C1032_InspectionR3Bare` | nested_repository differs |
| PC-575 | replace; custom-replace; grafts | `/*/*/CompletedCardWorkspaceRemovalTests/C1032_InspectionR3History` | source_not_preserved differs |
| PC-576 | changed/unchanged source; clean/smudge, fsmonitor, textconv/external-diff suppression separately | `/*/*/CompletedCardWorkspaceRemovalTests/C1032_InspectionR3Filter` | external marker or source verdict differs |
| PC-577 | omit null git-dir or use configured origin alias for remote proof read | `/*/*/CompletedCardWorkspaceRemovalTests/C1032_InspectionReadOnlyGitCalls` | exact null git-dir / normalized endpoint assertion fails |

PC-577 has an adjacent behavioral witness in C1032_InspectionR3Filter's
unchanged variant: a configured foreign helper cannot prevent the real remote
read, and the actual unpublished source is refused as source_not_preserved.
Mutation owns binding, missing-control discovery and precise method cycles.
No timeout widening, weakened assertion, class/suite-scoped PC execution or
production cleanup activation is authorized by this candidate document.
