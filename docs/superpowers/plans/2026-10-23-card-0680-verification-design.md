# CARD-0680 Code verification design

Companion to [the implementation plan](2026-10-23-card-0680-claude-dialog-send-guard-plan.md). Its checkpoint table is unchanged.

## Verification design (Code owner 52a5dc76)

The commissioning brief skips the separate TestDesign stage. The closed rows
below remain unchanged. CP-1 uses the existing transcribed permission/idle
layouts plus explicitly synthetic question fixtures; those fixtures do not
qualify native AskUserQuestion layouts. CP-2/CP-3 exercise the real runner with
controlled child I/O, rendered terminal bytes and backend-write recordings.
They assert refusals and bytes, including a clear control, rather than comparing
the classifier to itself. CP-4 uses the real expectation delivery service, queue
and isolated database, asserting pre-attempt custody and a complete UserPrompt
receipt. CP-5 invokes actual direct services. Its required full inventory also
includes boot, retries, interactive/startup exceptions and RC maintenance; those
cases remain implementation obligations until executed, even if an initial
subset of that method passes. CP-6/CP-7 retain their original assertions.

Async boundaries: admission precedes the durable expectation attempt; the body
and each Enter are separately fenced; a post-body refusal retains the typed-body
hold and does not claim delivery. Only the complete correlated UserPrompt past
the floor confirms delivery. A failed backend write changes input revision and
invalidates positive history. Generation/replacement/adoption invalidate prior
identity. An unavailable transport or capability has no raw fallback. Native
CP-8/CP-9 remain owed under the commissioning restriction; Q1 stays with 38c4e51a.

Pending SourceLanding Mutation inventory (none executed by Code):

| PC | Independently bypassable guard / variants | Exact method | EstimatedMinutes |
|---|---|---|---:|
| PC-1 | frame bounds: character cap / row cap | C680_OnlyCurrentBoundedDialogsBlock | 3 |
| PC-2 | current composer / unknown frame / question choice | C680_OnlyCurrentBoundedDialogsBlock | 4 |
| PC-3 | dialog refusal / separate Enter refusal / final output fence | C680_EveryWriteRechecksTheCurrentDialog | 4 |
| PC-4 | accepted generation / runtime identity / input revision | C680_EveryWriteRechecksTheCurrentDialog | 4 |
| PC-5 | failed-write revision / uncertainty / adoption proof | C680_InputProofInvalidatesOnWriteFailureAndAdoption | 4 |
| PC-6 | pre-attempt refusal / pending row retention / receipt requirement | C680_QueueAndExpectationKeepDialogInputPending | 5 |
| PC-7 | each automated writer and unsupported-peer fallback | C680_AllAutomatedWritersUseTheGuard | 8 |
| PC-8 | typed-body hold on withheld Enter and retry | C680_QueueAndExpectationKeepDialogInputPending | 3 |
| PC-9 | exact negative action / interactive input distinction | C680_AllAutomatedWritersUseTheGuard | 3 |

The PC estimate is a 38-minute floor plus discovery/reporting. Mutation must
expand every inventory variant into a method-scoped cycle and identify missing
controls; a listed method is not a claim that all its required cases exist yet.
Ordinary portable green remains the plan's 15-minute estimate, native watchdog
16 minutes separately owed, parent Q1 12 minutes separately owned. No arbitrary
mutants or native deliberate red runs belong to this Code task.


## Resumed implementation coverage

CP-2 adds identity replacement, output changes during the awaited delivery-evidence
read, missing revision/wrong epoch, trust key versus arbitrary text, and effort
highlight checks at the production runner write boundary. CP-3 adds two-empty-frame
arming, complete versus head-only native prompt, later input and single-frame veto.
CP-4 adds body-before-dialog custody, withheld Enter, and a swallowed first Enter
followed by a modal that must veto retry. CP-5 calls real runtime, channel, health,
watchdog and runner Claude boot adapters (verification on/off), interactive input,
RC dismissal plus concurrent-modal refusal, and real HTTP/phone-home clients for
snapshot/request forwarding and missing-capability no-fallback. CP-6/CP-7 retain
all original assertions; their fakes explicitly model generation/guard support.
CP-8/CP-9 are authored against isolated native runner sessions and the actual
expectation nudge service; neither is executed by this Code commissioning.

PC-3 additionally requires the awaited-evidence and replacement variants. PC-4
covers absent metadata and both transport forwarders. PC-5 additionally covers
two-frame arming, complete prompt and binding changes. PC-9 includes each startup
intent and RC's additional modal veto. All variants remain pending Mutation;
there is no Code mutation qualification. The 38-minute estimate remains a floor.
