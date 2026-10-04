# CARD-0889: reconcile source, publication and platform receipts

Date: 2026-10-04. Investigate task: `15d06aa2-79cd-408b-a9cd-48d67e0068d7`.
Source inspected: `a06b4b242f6959b9f381fc66c100e8f85a02eeab`.
Assigned branch: `feat/card-task-15d06aa2`; no source changes, builds, tests,
mutations, deployment, card writes or delegation in this investigation.

## Outcome

**All five implemented groups are published.** Stored Code/Review/Windows reports
supply ordinary execution evidence for S1/S2/S3/S5/S7. Qualification is attributed
to its actual source SHA, not relabelled as a run at the landing SHA or today's
checkout. No completed SourceLanding Mutation qualification was found for these
groups. S4's child-host defect and S6's readiness race remain present; the existing
Plan can proceed to a separate TestDesign refresh, without reimplementing the five
published groups.

The missing Windows ordinary work described by the 2026-10-02 Review reports has
since been executed by `a85cc266` and `4646e932`. CARD-0936's text still says
Windows is pending; that text does not erase their later evidence. Actual raw
historical `source.json`/TRX files were unavailable from this mirror, so this
investigation establishes **report-backed ordinary qualification**, not fresh
independent receipt validation. The broad Unit results with platform skips are
not zero-skip strict certificates.

## Evidence boundary and reproducible queries

The [current Plan](../superpowers/plans/2026-10-04-card-0889-grouped-flaky-fixes-plan.md)
is commit `a06b4b242f6959b9f381fc66c100e8f85a02eeab`. Historical numbering below
means the [2026-10-02 Plan/TestDesign](../superpowers/plans/2026-10-02-flaky-test-fixes-plan.md),
not the new plan's renumbered CP rows.

Read-only API captures are under `.antiphon/c889-reconcile/` (generated and
uncommitted). They record UTC retrieval times and full response rows. Board scope
is `8988ca03-7414-47ad-b0b6-51556c701703`, project
`d4ea7ae9-e769-474b-95b9-aa25fbc1303f`. Reads began at 11:59 UTC. Relevant evidence
is retained here as task identities, landing/review facts and verbatim checkpoint
lines; callers can retrieve the durable rows using the supported front doors:

- `pwsh -NoProfile -File scripts/delegate.ps1 -Status <task-id>`; evidence below
  names `GET /api/agent-tasks/<id>` response fields `result`, `landing`,
  `reviewEvidence` and `summary` explicitly.
- `pwsh -NoProfile -File scripts/card.ps1 get CARD-<number> -Board Antiphon`;
  the companion searches used `search 'CARD-0742'` and `search 'CARD-0882'`, both
  with `-Board Antiphon -All` and complete enumeration.
- `GET /api/agent-tasks?boardId=8988ca03-7414-47ad-b0b6-51556c701703` returned
  3,149 task summaries, including settled Code/Review/Debug rows and open tasks.
  No capped subset was used to establish absence of a CARD-0886 Code task.

`git fetch origin master` observed
`origin/master=9b78712f6671d7ccb98bcf4a5ebea8f080d7cecd`.
`git merge-base --is-ancestor <publication-sha> HEAD` and the same command against
`origin/master` returned **0** for every publication listed below. Thus publication
is established both by durable landing rows and current remote containment.
The served `/api/version` was `bb18064ba647e0ddb03cae4da437ab60ed447d98`; this is
separate activation evidence, not a test receipt.

## S1 first: CARD-0742 readiness

**Implemented, landed, ordinary-qualified by stored reports; Mutation pending.**
At `tests/Antiphon.Tests/Agents/RunnerCodexAdapterReadyTests.cs:43`, a controlled 50 ms timer is registered
before advance; snapshot two is held by a test-owned barrier. Lines 65–83 inspect
registration, first completion and second attempt independently. The adapter's
optional clock and System fallback are at
`server/Infrastructure/Agents/SessionRunner/RunnerCodexAdapter.cs:36` and `:42`;
its readiness options use the clock at `:156`.

The common S1/S2/S5 publication is:

| Fact | Durable row / value |
|---|---|
| Code owner | `f0251152-5609-4f30-b00d-3b22db2d808a` |
| Reviewed source | `a8a8bec33741a11c625b41b5db4c233cbd802ab1` |
| Earlier repaired source | `f20e44f267c6cbcdc73db2dea5f70b9d24028577`, Code `b5603fc8-7279-4372-b637-df29062b5996` |
| Review | `ed5dcb00-8f1d-4e56-91ca-eaa077af2397`, `reviewEvidence.id=1c4a5526-3d79-4349-a712-346642d9129b`, Clean, `reviewedSourceClean=true` |
| Landing operation | `6b1670e6-6acf-43b8-ad69-201c67586148` |
| Publication | `c345371e2aa0ed5c127b27f71d3199a29421122f`, Landed, cleanup Complete, remote confirmed `2026-10-02T08:10:03.169989Z` |

These values come from `GET /api/agent-tasks/f0251152` (`landing`) and
`GET /api/agent-tasks/ed5dcb00` (`reviewEvidence` and `result`). The full trees of
`f20e44f2` and `a8a8bec3` both equal
`63e182bedc9430f25076cd83504fb52f827d5dbf`; their `git diff --stat` is empty.
The landed **whole tree** differs in nine unrelated policy/documentation/test
files. Do not describe `c345371e2` as whole-tree-identical to the Windows-tested
source. The nine clock-group source/test files have empty scoped diffs from
`f20e44f2` to inspected HEAD; their later remote versions are checked separately
in the source-comparison record below.

Linux owner `f0251152.result` records CP-1 **12/12** at `a8a8bec3`. Linux Final
Review `ed5dcb00.result` records CP-1 **12/12**, CP-2 **14/14**, CP-10 **8/8**,
quiet CP-14 **14/14**, quiet CP-17 **8/8**, all with zero failures/skips and
validator acceptance at `a8a8bec3`. Windows `a85cc266.result` records CP-1
**12/12** at `f20e44f2`; its unedited checkpoint line is preserved below.

## S2 next: CARD-0820 / CARD-0751 shared resilience driver

**Same landed owner/operation as S1; report-backed ordinary qualification on both
platforms; Mutation pending.** The slow-first test at
`tests/Antiphon.Tests/Infrastructure/Resilience/ResilienceBudgetTests.cs:58` retains cancellation registration (`:75`), proves
attempt cancellation at exactly 10 s (`:104`), holds completion at that instant
(`:117`), and stamps the subsequent request with the original 30 s deadline
(`:128`). The owner-limit test at `tests/Antiphon.Tests/Infrastructure/Resilience/HttpResilienceRegistrationTests.cs:118` uses
separate controlled runner/git clocks; boundary advances are at `:146` and `:199`.
`tests/Antiphon.Tests/Infrastructure/Resilience/ResilienceTestSupport.cs:147` validates registered timers and advances to one
boundary; `:178` re-drives due callbacks with zero time advance while waiting for
phase acknowledgment. Legacy `Pump` remains at `:134`; the other users were not
included in this repair.

There was an intermediate regression, not merely missing proof:
`e98a01c2-09e2-4878-bb11-9ecd761c3de3.result` at
`42b75f7ac7afd28c136143fccaa447307b08ace7` records loaded CP-14 **13/14 in three
failed branch runs** versus **14/14 in three master runs**, with a phase watchdog
failure persisting even at 60 s. `b5603fc8.result` reconstructs the cancellation
callback being disposed before acknowledgment; current
`tests/Antiphon.Tests/Infrastructure/Resilience/ResilienceTestSupport.cs:88` retains it. The repaired-source Review
`24a51e38-4529-4dee-8f6a-9632e2c26d04.result` at `f20e44f2` records loaded CP-14
**3/3 branch and 3/3 master**, each **14/14**, plus the quiet rows. Earlier
`42b75f7a` and `7f51f2e5` receipts cannot certify the repaired implementation.

Linux qualification is the repaired and linear-source evidence above. Windows
`a85cc266-074e-44fd-b16a-5c04b7cb5fff.result` records CP-2 **14/14** and
CP-14 **14/14 ×3**, at
`f20e44f2`, zero failed/notExecuted throughout. No arbitrary wall-time timing
window was substituted for the exact virtual boundaries.

CARD-0820's checkpoint temp-root contention, file-handle cleanup and other timing
cases remain outside this shared-clock repair (card `cb01ee9b-8363-4a9a-8bcf-d9d070b53857.description`).

## S5: CARD-0889 submit confirmation

**Same published clock group; Linux and Windows ordinary evidence exists.**
`server/Infrastructure/Agents/CodexSubmitConfirmation.cs:18` resolves null/omitted clocks to System; `:95`
calculates deadlines from that clock, `:114` preserves termination when the global
budget expires, and `:166` uses it for blind settle. Eight controlled-clock tests
are present in `RunnerCodexAdapterSubmitConfirmTests.cs`. Linux Review
`ed5dcb00.result` records historical CP-10/17 **8/8** each at `a8a8bec3`; Windows
`a85cc266.result` records **8/8** each at `f20e44f2`. The production 20 s default
and legitimate expiry before a fourth Enter are preserved; this investigation
makes no new live-delivery claim.

## S3 and S7: Grok platform gate and scaled source clock

Both groups landed through Code owner `cab6a109-dfd3-4c8f-9bcf-2801c80b0f3b`:

| Fact | Durable row / value |
|---|---|
| Reviewed source | `dabf9f20da15e14d2d61a14a6311a12cd5f4769a` |
| Review | `e21bd32f-1703-45dd-b552-ed9ec3b1394a`, Clean; evidence `e40e620c-7701-42ed-af42-a957ac724ce8`, `reviewedSourceClean=true` |
| Landing operation | `bfb76cbf-0cf9-416d-b4ea-a47921b57896` |
| Confirmed publication | `6cff76eae6326654794ed68d6da8564adabcd9f2`, Landed, cleanup Complete, `2026-10-02T04:51:48.666007Z` |
| S3 implementation | `da9edf0b837b81e8fdf4ccc19c8f80be3b78de75` |
| S7 tests / helper | `07b1e8fee1e43d64ffb8688bc612da7e6ea9d8a6` / `6cff76eae6326654794ed68d6da8564adabcd9f2` |

Source details: `tests/Antiphon.Tests/Application/GrokRulesLaunchRefusalTests.cs:100` gates the shared helper before
fixture creation; only the two named Windows-policy rows call it. The three other
class results remain applicable on Linux. `tests/Antiphon.Tests/TestHelpers/ScaledTimeProvider.cs:16` takes one
source, defaults to System at `:21`, and uses that source for UTC/timestamps/timers
at `:31`, `:38`, `:42`, `:48`. Six fake-source tests are at
`tests/Antiphon.Tests/TestHelpers/ScaledTimeProviderTests.cs:13` onward. All three group files have empty scoped
diffs between reviewed `dabf9f20`, publication, inspected HEAD and observed remote.
The full reviewed and published trees differ in eleven unrelated files.

| Group | Linux evidence at reviewed source | Windows evidence at same reviewed source | Missing / limitation |
|---|---|---|---|
| S3 | `cab6a109.result` and `e21bd32f.result`: whole-class diagnostic **3 passed, 2 explicit policy skips**; pure policy CP-5 **26/26** | `4646e932-d575-4435-a341-b6656805a367.result`: CP-4 **5/5**, pure policy CP-5 **26/26**, named Herdr/Pty methods **1/1 each**, zero skips | Historical CP-3/15 selected zero; whole-class diagnostic supplies applicable Linux execution evidence, not the missing zero-skip CP-3 certificate. Windows PC-labelled runs were unmutated green inventory only. |
| S7 | Code CP-12 **6/6**, quiet CP-19 **180/180 over 30 ordinals**; Review CP-12 **6/6**, consumers **164/164** | `4646e932.result`: CP-12 **6/6**, zero skips | Loaded CP-19 produced no receipt; CARD-0932 owns that controller failure. All seven SourceLanding controls pending. |

No Windows ordinary group is wholly missing. Missing historical stress/filter
certificates and SourceLanding controls must remain separately disclosed.

## Remaining mechanisms and S4/S6 dependencies

### S4 / CARD-0890

**Confirmed historical Linux child-host failure; current launcher mechanism
unchanged.** `0f936bc2-91c2-405a-b816-5b910abca946.result` and the retained
[original investigation](2026-10-02-flaky-test-root-causes.md) at artifact commit
`f811ba1c8c2a23be8d7a654e1f74b08b4529fda3` record a slot-gated Linux C448 run:
**7 executed, 0 passed, 7 failed before worker-ready**, plus a reflection probe
showing `TypeInitializationException <Module>` -> `FileLoadException` for
`System.Text.Json, Version=10.0.0.0` with a manifest mismatch.
CARD-0890 additionally records the seven-case failing baseline at
`8331a9cf1cbb1db564791b3acce5e9af2b298b3a`. The later investigation's raw run
envelope is unavailable; its artifact commit is not represented as a tested SHA.

Current `tests/Antiphon.Tests/Application/AgentTaskLandRecoveryTests.cs:130` still writes a `pwsh` reflection script
with `Assembly.LoadFrom` at `:133`; launch arguments use the assembly location
at `:144`, readiness is required at `:153`, and resume changes argument index 6
at `:173`. The adjacent delivery launcher does the same at
`tests/Antiphon.Tests/Application/PostLandMutationDeliveryTests.cs:776`/`:779`/`:807`; retirement does so at
`tests/Antiphon.Tests/Application/WorktreeResidueRecoveryTests.cs:376`/`:382`. `tests/Antiphon.Tests/TestHelpers/TestWorkerModes.cs:15` still lists
six modes, without the two modes in the existing S4 plan. This reconstructs why
these callers retain the host-binding risk; it does not prove a new failure for
every sibling cut or today's installed PowerShell version.

Independent delivery coverage gap: `tests/Antiphon.Tests/Application/PostLandMutationDeliveryTests.cs:925` returns
when `QueueMessageId` is null after accepting RetryPending. The later complete
persisted receipt assertions (`Confirmed` and sequence 11 at `:970`) are then
unreachable. Worker-ready alone cannot establish caller receipt. The old
PC-27..32 obligations already cover this gap; they remain outstanding.

### S6 / CARD-0900

**Confirmed observation race by stored evidence and current control flow.** Card
`1613eaa8-a0ab-4079-8f59-456ebbcd4d2f.description` attributes the failure to Review
`e772aac0-354b-441b-807b-41aa00bcd079`, source
`1d994aac952bed0a65506db0bb84742b7e9183e4`, Linux under concurrent lanes: the failed assertion
was `C578 FailedBuild retains stdout and stderr`, while both markers were present.
That Review's durable `result` independently records quiet **24/24** and loaded
**6/6 at head and 6/6 at base** as historical passes, not
proof that the loaded race was corrected.

At `scripts/test-run-checkpoint.ps1:749`, failed-build uses `-FailBuild` without
a hold. `Wait-C578Ready` has a ten-second deadline (`:656`) and tests wrapper exit
before ready/entry (`:660`), returning Ready=false at `:661`. Thus a schedule where
the child writes both markers, exits 37, and the wrapper exits before the parent's
next poll rejects readiness despite retained logs; the assertion at `:756` demands
both Ready and markers. A live child whose ready marker arrives after the local
ten-second deadline also yields Ready=false. These are control-flow
reconstructions, not new runtime repros. The hold/release facility exists in the
script; the streaming/interruption cases use it. The TUnit bridge still requires
five failed-build labels at `tests/Antiphon.Tests/Scripts/RunCheckpointScriptTests.cs:28`. No handshake fix or
seven-label certificate is present.

### Dependency refresh (publication and occupancy are separate)

| Dependency | Current source/publication evidence | Consequence for existing S4/S6 plan |
|---|---|---|
| CARD-0788 | Owner `697ca82c-c6f0-40ae-b8d2-b2a53af786cd.landing`: O=`aed6661d-20ef-40f7-93a3-c07fcdea4944`, source `f61f9bf93db3c0ece998c55e3a04a4e401052a74`, publication `4380891cca92111cd6271a0bf0f3662965a7440d`, Landed/Complete. Review `22882994` records all Linux rows; Windows `38712755` reports seven inherited Unit failures verified at base `2241aeb3`, targeted rows green. | Publication is contained; the old blanket wait for 0788 is discharged. Spill-pointer confirmation changes are present in `server/Application/Services/AgentTaskLandNotificationService.cs:171`. |
| CARD-0886 | Scoped history contains Plan `a3c7520f` and TestDesign `28eaa3bf`, with publications `95d99126b0bfae9b1b3c39a0d678f0e4a42d7187` and `2b4d7313324b6eaeadf6e45fb15808bacfad6263`. No Code task found for its card. Current `tests/Antiphon.Tests/TestHelpers/LandingProtocolHarness.cs:48` still initializes each harness; proposed reuse implementation is absent. | Review column does not mean the optimization landed. Its planned shared harness/script changes remain a potential future collision; no active 0886 writer was observed. |
| CARD-0885 | Owner `e9d313a9-7ca8-4b65-bfac-8ff5404b62d9.landing`: O=`2229c788-9f3e-4c45-b57d-df5ba7da6712`, reviewed `ad1fb15470c91c568ff337a3ad3ea672feca421b`, publication `808677658cc418dc439d906de4526aaea32e231a`, Landed/Complete. Review `93a3d93f`: Linux rows green; Windows `cbb4215c` CP-8 **1/1**, isolation sequence **1/1**. | Repeat/tooling prerequisite is published. CARD-0968 still owns missing planned test classes (5+6+3=14), benchmark/other disclosures; its stale Windows CP-8 item has later evidence. Those obligations do not mean tooling is wholly unlanded. |
| CARD-0888 | Owner `bed0cdb8-2ca6-4a56-bf0e-24cb685848d2.landing`: O=`31666eb4-6a12-4c5c-a871-07ce25b84d37`, reviewed `3608a491b2ea62d4d4ba05f836b8d3abfe592c9a`, publication `829ed78b36f31866f71176285b9c00a57dfadae3`, Landed/Complete. Review `cc5fd1a7`: Linux CP-5 **40/40**; Windows `b42062a5` **40/40** plus D1/D2 **14/14**. | Runner-bound spill/input persistence is already present. `server/Application/Services/SessionMessageQueueService.cs:143`, `:229` and notifier spill matching at `:201` affect the delivery infrastructure consumed by S4; the success-required early return remains. These unrelated platform receipts do not certify the C478 crash matrix. CARD-0965 is Done despite historical pending prose; no inference of PC-clean from that status. |

The board-scoped open-task snapshot has no open task bound to 0788/0886/0885/0888.
Open Code rows include `15d5a0c9` (1017), `2c35a27d` (1013), `ffc43849` (1022),
`6141b0b0` (1030), `fdb14a5c` (0892). Their declared `scope`/`observedScope` fields
were empty. Their full goals were captured; none explicitly commissions the three
S4 launcher files or S6 script in this refresh. This is not a reservation or a
proof that their footprints cannot expand. CARD-1017 has an already contained
foundation publication `bb18064ba` and planned later retirement work intersecting
`WorktreeResidueRecoveryTests` (its plan `:246`, `:968`); the foundation replay goal
is narrower. Re-read actual stage occupancy and exact edited paths before Code.

## Outstanding evidence and controls

No SourceLanding Mutation task appeared in the board-scoped task history for the
inspected group/companion card IDs. Scratch reds in Code/Review are recorded as
such; they do not discharge the source-bound Mutation obligation.

| Existing inventory | Outstanding qualification |
|---|---|
| S1 PC-1..4 | Four controls; readiness barrier, completion/attempt separation and System-default seam. |
| S2 PC-5..10 | Six controls; original total budget, exact attempt/owner boundaries and driver behavior. CARD-0937 specifically notes the held-completion witness was a test-side mutation and its driver witness remains due. |
| S3 PC-11..14 | Four controls; PC-11..13 need Windows red/restore/green. Windows task 4646e932 supplied green inventory only. Portable policy PC-14 had a scratch red, still pending SourceLanding. |
| S5 PC-33..40 | Eight controls; Enter limit, body once, distinct blind looks, System defaults, legitimate deadline expiry, receipt floor and clock use. |
| S7 PC-48..54 | Seven controls; UTC/timestamp/timer scaling, offset-only advance, speed one, cancellation and invalid speed. Seven Code scratch reds and selected Review spot checks are not SourceLanding completion. |
| S4 PC-15..32 | Eighteen planned controls; worker ownership/validation/registry/warmup/cleanup and all six correlated delivery guards PC-27..32. Implementation and ordinary qualification remain due. |
| S6 PC-41..47 | Seven planned controls; readiness ownership/late event, retained streams, exact child/wrapper exit, no tests after failed build and process cleanup. Implementation and ordinary qualification remain due. |

Total: **29 controls for implemented groups + 25 for S4/S6 = 54**, all retain
separate method-scoped source-bound qualification obligations. No new control
inventory is designed here.

Historical evidence gaps: CARD-0931 zero-selection CP-3/15; CARD-0932 incomplete
loaded CP-19; CARD-0936 N=30 quiet/loaded recipes and pre-fix baselines. The current
Plan and `docs/testing-and-build.md:305` supersede the old repeat prescription
with at most three normal plus two loaded optional repetitions per unchanged
selection, none required after green. Preserve the historical gaps without
silently recommissioning 30 rounds. A later fresh unified certificate has not run:
current Plan roster **Linux 134 / Windows 136** includes still-open S4/S6 work.

Remaining uncertainty is bounded:

1. Raw Linux/Windows receipt envelopes, TRX rosters and validators could not be
   re-read: the named historical runner directories and `/tmp/rv24a5.T9SBOT`
   are absent, and Windows paths are unreachable. Durable task reports retain
   counts/provenance; exact-source raw import would resolve independent validation.
2. No new current-base C448/C578 runtime diagnostic ran. Stored C448 failure/probe
   and current source reconstruct the mechanism; stored C578 failure plus the
   exit-before-ready ordering reconstruct its race. Any dispute about current
   runtime behavior needs the existing plan's exact-method diagnostics with child
   exception, PID/start identity, ready/stream/exit evidence, not a broad Unit run.
3. Group-file equality preserves inspected implementations but does not transfer
   a full qualification across unrelated runtime/fixture changes. The separate
   TestDesign owns the final source/OS selection and current roster recount.
4. Open task states and scopes are an observation, not future collision clearance.

## Not done, noted

The existing Plan specifies a dotnet worker-mode migration for S4 and an owned readiness/stream/release handshake for S6; neither was designed or implemented in this investigation.

## Checkpoint provenance retained verbatim

These are copied from durable `result` fields, not regenerated claims. Their
source/slot/build/dirty tokens retain the original source and absolute receipt
paths. Independent raw validation remains unperformed here.

Task `a85cc266-074e-44fd-b16a-5c04b7cb5fff` (Flaky A Windows rows 2):

```text
CHECKPOINT CP-1 commit=f20e44f267c6cbcdc73db2dea5f70b9d24028577 build=ok filter=/*/*/RunnerCodexAdapterReadyTests*/* executed=12 passed=12 failed=0 skipped=0 trx=C:\Antiphon\worktrees\card-task-a85cc266\.antiphon\checkpoints\CP-1-20261002-081730-fe07\run.trx slot=granted waited=0s dirty=0 source=f20e44f267c6cbcdc73db2dea5f70b9d24028577 sourceState=clean buildSource=verified
CHECKPOINT CP-2 commit=f20e44f267c6cbcdc73db2dea5f70b9d24028577 build=reused filter=/*/*/(ResilienceBudgetTests*)|(HttpResilienceRegistrationTests*)/* executed=14 passed=14 failed=0 skipped=0 trx=C:\Antiphon\worktrees\card-task-a85cc266\.antiphon\checkpoints\CP-2-20261002-082033-91f7\run.trx slot=granted waited=0s dirty=0 source=f20e44f267c6cbcdc73db2dea5f70b9d24028577 sourceState=clean buildSource=verified
CHECKPOINT CP-10 commit=f20e44f267c6cbcdc73db2dea5f70b9d24028577 build=reused filter=/*/*/RunnerCodexAdapterSubmitConfirmTests*/* executed=8 passed=8 failed=0 skipped=0 trx=C:\Antiphon\worktrees\card-task-a85cc266\.antiphon\checkpoints\CP-10-20261002-082046-5ec2\run.trx slot=granted waited=0s dirty=0 source=f20e44f267c6cbcdc73db2dea5f70b9d24028577 sourceState=clean buildSource=verified
```
Task `4646e932-d575-4435-a341-b6656805a367` (Flaky S3 S7 Windows rows):

```text
CHECKPOINT CP-4 commit=dabf9f20da15e14d2d61a14a6311a12cd5f4769a build=ok filter=/*/*/GrokRulesLaunchRefusalTests*/* executed=5 passed=5 failed=0 skipped=0 trx=C:\Users\lndco\AppData\Local\Temp\claude\C--Antiphon-worktrees-card-task-4646e932\b75d6421-b500-47d7-9e09-c2837ac398f3\scratchpad\cp\CP-4-20261002-052800-8620\run.trx slot=granted waited=0s dirty=0 source=dabf9f20da15e14d2d61a14a6311a12cd5f4769a sourceState=clean buildSource=verified
CHECKPOINT CP-12 commit=dabf9f20da15e14d2d61a14a6311a12cd5f4769a build=reused filter=/*/*/ScaledTimeProviderTests*/* executed=6 passed=6 failed=0 skipped=0 trx=C:\Users\lndco\AppData\Local\Temp\claude\C--Antiphon-worktrees-card-task-4646e932\b75d6421-b500-47d7-9e09-c2837ac398f3\scratchpad\cp\CP-12-20261002-053146-4d26\run.trx slot=granted waited=0s dirty=0 source=dabf9f20da15e14d2d61a14a6311a12cd5f4769a sourceState=clean buildSource=verified
CHECKPOINT CP-5 commit=dabf9f20da15e14d2d61a14a6311a12cd5f4769a build=ok filter=/*/*/GrokRulesArgvPolicyTests*/* executed=26 passed=26 failed=0 skipped=0 trx=C:\Users\lndco\AppData\Local\Temp\claude\C--Antiphon-worktrees-card-task-4646e932\b75d6421-b500-47d7-9e09-c2837ac398f3\scratchpad\cp\CP-5-20261002-053157-fcde\run.trx slot=granted waited=0s dirty=0 source=dabf9f20da15e14d2d61a14a6311a12cd5f4769a sourceState=clean buildSource=verified
```

## Source comparison and handoff

The read-only `git diff --exit-code <tested-source> <target> -- <group-files>`
comparisons below all returned 0. The nine clock files are enumerated here;
the three Grok/scaled paths are those named in S3/S7. No whole-tree equality is implied for publication/current
source. These are source comparisons, not build-source receipts.

| Directory | Clock-group files compared |
|---|---|
| `server/Infrastructure/Agents/` | `CodexSubmitConfirmation.cs`, `SessionRunner/RunnerCodexAdapter.cs` |
| `tests/Antiphon.Tests/Agents/` | `RunnerCodexAdapterReadyTests.cs`, `RunnerCodexAdapterSubmitConfirmTests.cs`, `ScriptedCodexRunnerClient.cs` |
| `tests/Antiphon.Tests/Infrastructure/Resilience/` | `HttpResilienceRegistrationTests.cs`, `ResilienceBudgetTests.cs`, `ResilienceTestSupport.cs` |
| `tests/Antiphon.Tests/TestHelpers/` | `ControlledTimeProvider.cs` |

```text
f20e44f2 -> c345371e2aa0ed5c127b27f71d3199a29421122f files=9 scoped-diff-exit=0
f20e44f2 -> HEAD files=9 scoped-diff-exit=0
f20e44f2 -> origin/master files=9 scoped-diff-exit=0
dabf9f20 -> 6cff76eae6326654794ed68d6da8564adabcd9f2 files=3 scoped-diff-exit=0
dabf9f20 -> HEAD files=3 scoped-diff-exit=0
dabf9f20 -> origin/master files=3 scoped-diff-exit=0
```

--- next stage ---
next: test-design
handoff: Refresh the existing S4 worker migration and S6 C578 handshake TestDesign at the chosen contained base; preserve all five published groups and their SHA-attributed platform evidence, carry 54 pending controls, recount worker/delivery/retirement/registry/warmup and C578 labels, and recheck exact-path occupancy before Code.
artifact: docs/investigations/2026-10-04-card-0889-reconcile.md
