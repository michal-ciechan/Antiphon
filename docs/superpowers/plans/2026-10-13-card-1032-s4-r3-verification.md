# CARD-1032 S4 repair round 3

Landing owner: Code 4da2d6db-7b6a-4c58-aa47-4062e234712f.
Base production: 283618f078424b3e6aa2edd5dd09a9446a9d08da.
Read with the landed plan, TestDesign S4 and S4 repair verification.
The new tests-only commit must demonstrate the four Review defects before repair.
No Unit lane: this repair's explicit brief excludes it. S5–S8 and production
cleanup activation remain deferred. One isolated runner build is shared by all
rows; UseAppHost=false is automatic on Linux. No whole assembly is selected.

## Verification design

V-3: hidden executable changes, damaged/case-varied bare repository shapes,
unmodified object ancestry and filter-free raw source inspection. V-2: read-only
Git environment and argument guards. V-4: existing fence regressions.
R-1/R-2: full affected removal, workspace service and legacy gate classes plus
binding and dispatcher compatibility. V-1: runner binding regressions only;
server capture/receipt obligations remain prior-slice/later-slice evidence.
V-5/V-6/R-3 and parent native Windows acceptance remain deferred, never passed.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-28 | S4-r3 | `tests/Antiphon.SessionRunner.Tests -> bin-c1032-s4r3/` | four-defects | `/*/*/CompletedCardWorkspaceRemovalTests/C1032_InspectionR3*` | V-3 | 14 new rows red before fix; green after fix | 14 | 3 | true |
| CP-6 | S4-r3 | CP-28 | inspection | `/*/*/CompletedCardWorkspaceRemovalTests/C1032_Inspection*` | V-2, V-3 | all 60; zero failed/skipped after fix | 60 | 3 | true |
| CP-5 | S4-r3 | CP-28 | fence | `/*/*/CompletedCardWorkspaceRemovalTests/C1032_Fence*` | V-4 | all 6; zero failed/skipped | 6 | 2 | true |
| CP-29 | S4-r3 | CP-28 | affected-classes | `/*/*/(CompletedCardWorkspaceRemovalTests*)\|(RunnerWorkspaceServiceTests*)\|(LegacyWorkspaceRemovalGateTests*)/*` | V-3, V-4, R-1, R-2 | all 110; zero failed/skipped | 110 | 4 | true |
| CP-30 | S4-r3 | CP-28 | binding | `/*/*/RunnerWorkspaceBindingTests/*` | V-1, R-2 | all 21; zero failed/skipped | 21 | 2 | true |
| CP-24 | S4-r3 | CP-28 | compatibility | `/*/*/PhoneHomeCommandDispatcherTests/Workspace_*` | R-1, R-2 | all 3; zero failed/skipped | 3 | 1 | true |

## Mutation handoff

Every existing PC and variant stays pending. Added method-scoped controls:

| PC / variants | Deliberate defect | Detecting method | Expected red | State |
|---|---|---|---|---|
| PC-573 add-executable/remove-executable | Trust core.fileMode=false or omit physical executable mode comparison | C1032_InspectionR3Mode | dirty_source differs | pending |
| PC-574 missing-head/missing-objects/missing-refs/directory-head/case-head/case-objects/case-refs | Require only complete exact-case bare markers | C1032_InspectionR3Bare | nested_repository differs | pending |
| PC-575 replace/custom-replace/grafts | Enable replacement objects or grafts | C1032_InspectionR3History | source_not_preserved differs | pending |
| PC-576 changed/unchanged | Run configured filters/fsmonitor/textconv | C1032_InspectionR3Filter | external marker appears or verdict differs | pending |

SourceLanding Mutation owns deliberate mutants and exact method red/restore/green.
New regression reds on unchanged production do not discharge these controls.
