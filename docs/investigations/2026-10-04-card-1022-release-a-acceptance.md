# CARD-1022 release A acceptance reconciliation

Date: 2026-10-04. Investigate task: `d149ce14-897b-4f5f-8435-a5245fec5d05`.

**Confirmed: release A is published, but complete acceptance is not evidenced.**
Stored receipts reconstruct a successful ordinary Review with expressly accepted
prior Windows rows, followed by a landing rebase. They do not establish the
separate complete final-SHA Windows Debug qualification. The serving server and
Linux runner still load pre-A builds. Current Windows runner/retained-host
provenance and post-activation delivery remain unknown from this Linux mirror.
No same-board release-A Mutation companion was found in complete searches.
Mutation remains operator-paused; none was commissioned or executed.

No residual release-A implementation defect was demonstrated. The two Windows
fixture failures are already repaired. This is an acceptance/evidence gap,
not authority to replay implementation or revive the blocked original owner.

## Source, ownership and durable records

Assigned branch `feat/card-task-d149ce14` started clean at
`838eaf627122463c49e129c57c0f7341f68cbee2`. It was not rebased/reset/merged.
Read-only fetch/remote observation pinned current master **B =
`9b78712f6671d7ccb98bcf4a5ebea8f080d7cecd`**.

| Identity | Evidence measured on 2026-10-04 at 12:26 UTC |
|---|---|
| Card | `4b0d51f6-9b18-4409-b14e-586ecfa9ea3c`, CARD-1022, “Deprecate and remove the legacy inbox ConPTY backend (keep ModernConPty only)”; InProgress, revision 5. |
| Board/project | Antiphon board `8988ca03-7414-47ad-b0b6-51556c701703`; project `d4ea7ae9-e769-474b-95b9-aa25fbc1303f`. |
| Original attempt | `ffc43849-8d59-468d-8b6a-2a00033acbda`: Blocked; no landing/request. Its prerequisite history/admission failure remains its own record. |
| Implementation | `e41a7005-3049-48f9-957a-162b4a4f9fbb`: Succeeded; implementation tip `4e865e05d288374c0d61d8599080a5c399db5f4e`; no own publication. |
| Repair/publication owner | **`b7c17822-63b4-4626-b5bb-525cfccc0cea`**: Succeeded, expressly commissioned successor landing owner. Preserve this attribution. |
| Reviewed source C | `65a78f3b1f88c213bbb4ac595681212476447130`; Review `71a5997b-d0d0-45a0-8699-84817a158fd5`. |
| Review evidence | `d6ed9bd1-1784-44ec-b432-b488ffb1bb47`: Clean; subject b7c17822; reviewed source C; ref `refs/heads/feat/card-task-b7c17822`; `reviewedSourceClean=true`. Review result says `ordinaryScopeCompleted: Full`; verification round is Final. |
| Landing operation O | `8d0f6e2a-2aec-4204-8ca4-17fdae2deacc`; request `1a6acba8-ea5d-41b2-b718-d9c61347cc4c`. |
| Published L/R | **L = R = `f11d2715f340c7caa03343862cb8dd73cc27f66b`**; phase Complete, publication Landed, cleanup Complete, destination `refs/heads/master`; remote confirmation `2026-10-04T07:02:49.963726Z`. |

Sources are the durable `GET /api/agent-tasks/{full-task-id}` records:
`summary`, `reviewEvidence`, `verification`, `landing`, `landRequest` and `events`.
The historical ownership chain is also pinned in
[successor plan](../superpowers/plans/2026-10-04-card-1022-release-a-successor-plan.md):35.
The loaded server's task API can read publication records newer than its binary;
reading those records does not activate their code.

The b7c17822 `LandedWithResidue` event at `2026-10-04T07:03:28.314663Z`
contains `canonical=canonical_checkout_dirty`, even though structured publication
and task-tree cleanup are complete. This is stored evidence that canonical
activation was not established by that land. It does not establish today's
canonical checkout state, which this mirror cannot inspect.

Outcome notification `44bc48c3-fd52-4e56-b8c5-33d3f3e1c2b0` is Canceled,
`lastErrorCode=queue_canceled_unconfirmed`, with null confirmation/sequence.
Its destination was `08a89212-d040-4659-b166-94746c8df0eb`, queue message
`8f20f0ec-1f63-4b5d-b7ac-51fb4d1cbe42`. The distinct task-completion
notification is Confirmed at sequence 107951. Publication succeeded independently
of this unconfirmed outcome delivery; the latter authorizes no second land.

## Qualification reconstructed from stored evidence

These are historical measured runs, **not runs performed by this investigation**.
`E/P/F/S` means executed/passed/failed/skipped. Every listed checkpoint line
reports `dirty=0 sourceState=clean buildSource=verified slot=granted waited=0s`.
Those are retained producer assertions; original Windows TRX/receipt files are
not reachable here and were not independently revalidated.

| Row | Current floor | Latest executed source | E/P/F/S | Durable producer |
|---|---:|---|---|---|
| CP-1 | 30 | C | 30/30/0/0 | Review 71a5997b |
| CP-2 | 12 | C | 12/12/0/0 | Review 71a5997b |
| CP-3 | 20 | C | 20/20/0/0 | Windows Debug 4608a549 |
| CP-4 | 4 | `4e865e05d288374c0d61d8599080a5c399db5f4e` | 4/4/0/0 | Windows Debug 28a1714e |
| CP-5 | 2 | `4e865e05d288374c0d61d8599080a5c399db5f4e` | 2/2/0/0 | Windows Debug 28a1714e |
| CP-6 | 44 | `4e865e05d288374c0d61d8599080a5c399db5f4e` | 44/44/0/0 | Windows Debug 28a1714e |
| CP-7 | 24 | C | 24/24/0/0 | Windows Debug 4608a549 |
| CP-8 | 1 | `4e865e05d288374c0d61d8599080a5c399db5f4e` | 1/1/0/0 | Windows Debug 28a1714e |
| CP-9 | 18 | C | 18/18/0/0 | Review 71a5997b |

The authoritative freeze is
[A/B/C plan](../superpowers/plans/2026-10-03-card-1022-modern-conpty-only-plan.md):1047,
not its earlier provisional table at line 265. Its blob is
`315e96ae3139d814925c7ff4022d258e70ba5860` at this branch, L and B.
The current **155-result** roster is unchanged: portable/Linux 60; Windows 95.
Arithmetic across historical rows is not a single-SHA 155/155 run.

Normalized server transcripts independently retain the full producer reports:

| Producer task | Session transcript | Report entry |
|---|---|---|
| `28a1714e-dbf0-44c4-9ef4-f617fd53ee69` | `480b1d0a-22c1-4c49-a4a1-cf6ce71c35d6` | AssistantText sequence **130**, `2026-10-04T05:59:35.389Z`; 131 retained entries |
| `4608a549-7ba1-4e67-a42d-2aff1f4d8286` | `f3486dc8-e430-4982-81d1-e90663e64009` | AssistantText sequence **95**, `2026-10-04T06:43:53.131Z`; 96 retained entries |
| `71a5997b-d0d0-45a0-8699-84817a158fd5` | `d77f669d-2693-4cfc-a123-dad8b9c0a3de` | AssistantText sequence **134**, `2026-10-04T06:58:00.787Z`; 135 retained entries |

All three `GET /api/sessions/{session-id}/transcript?since=0` reads returned 200.
These report entries quote test evidence; they are not production canary
UserPrompt receipts. Essential CHECKPOINT lines are preserved below.

### The original failures are already resolved

28a1714e actually ran six Windows rows at `4e865e05`: **92 executed,
90 passed, 2 failed, 0 skipped**. CP-3 was 17/18, CP-7 22/23; one repeat
of each failed identically. Neither was an old-default expectation or C1008
host-fixture failure.

The typed-input reader tried `EnumerateArray` on FakeClaude's string content.
The landed string/block handling is at
`tests/Antiphon.Agents.Pty.Tests/C1022TypedInputTests.cs:101`, and the
whole-body/typed-loss assertions remain at line 90. The partial-current receipt
was sequence 2 after TurnEnd 1, so it made the synthetic session Working:
`server/Infrastructure/Data/TranscriptWorkingStateQuery.cs:21` and line 39;
the idle-only flush returned Nothing at
`server/Application/Services/SessionMessageQueueService.cs:1639`.
The landed fixture adds TurnEnd after that prompt at
`tests/Antiphon.Tests/Application/SessionQueueReceiptPlumbingTests.cs:527`;
Pending/Truncated/no-input assertions remain at line 519.

[b7c17822 repair evidence](2026-10-04-card-1022-windows-test-repair-b7c17822.md):16
preserves portable reproductions at `e2086c6cad31edf113d513e6b2d46b53926f9541`
and passing repaired runs at `7e579dfda95b0945e201eb8c133243a208a19874`.
4608a549 subsequently executed **44/44** at C: CP-3 20/20, CP-7 24/24,
including the two previously failing native methods and three added portable results.

The **Review caller expressly accepted CP-4/5/6/8 at the prior SHA**, saying they
were not rerun because the later commits were test-only. This appears in
`GET /api/agent-tasks/71a5997b` field `goal`, paragraph `OWNER EVIDENCE`.
It explains the Clean Final/Full ordinary Review; it does not say that a fresh
six-row 95-result Debug run occurred. The same brief expressly says Mutation
paused. Preserve both facts, rather than invalidating ordinary Review or inventing
a complete new Windows run.

Historical Unit remains **4,030 passed / 15 failed / 53 skipped** at
`86981be09bd7a58c2a6e0805c2360cb706a5d5c7`; exactly those 15 failed at
base `a10bd1883e0803bcad3fe4513f3f1d1685c0b666`. This is the inherited
missing-jq evidence in [Code report](2026-10-04-card-1022-code-e41a7005.md):62,
not qualification at C/L/B and not a blanket waiver for future failures.

### Native evidence that exists, with its limits

28a1714e's CP-4 records three owned Windows hosts at `4e865e05`, covering unset,
configured modern, and explicit modern over inherited inbox. Unset session
`33066bf0-4b54-42f9-ac84-ebea3716dce0` used host PID 10748, child 35036,
OpenConsole 16760; raw request empty, no fallback. Configured session
`eaec31db-1ec1-4065-b9a8-5958ffdd0753` used host 22372, OpenConsole 22776.
Explicit session `a1caa5da-c451-4704-8f9a-a666a9fe1ec6` used host 30508,
OpenConsole 30192. Both capability producers reported deprecation false.

4608a549 records Windows 10.0.19045/x64 and process x64. Its C1011 owned host
session `7954eecf-fc85-4c2b-a326-12482d3f2f86`, PID 18720, loaded
`...\20261004-063810-be8e6147\Antiphon.PtyHost.exe`, product `1.0.0+<C>`.
The host log at `2026-10-04T06:38:11.5097222Z` records
`pty backend: ModernConPty (requested 'modern'): Microsoft.Windows.Console.ConPTY 1.24.260710001`.
Its pair hashes are:

```text
conpty.dll      39fba2713e2495117b1591ae8c32a3b904bea7aa66069cf7815e2844c76d75d8
OpenConsole.exe b7fd936c2668b87b9ecf7b3366dc6568afc1c6f981874cba3e955a1c35cf8160
```

The producer reports one destination UserPrompt equal to its sent body;
FakeGrok's stored row is
`{"role":"user","content":"C1011 HEAD 24192886a0ac4299b4916fb02edb5286 TAIL"}`.
PIDs 18720, 37204 and 14728 were captured and then observed exited.
The typed/paste native test passed, but emitted no stdout; its assertions are
the evidence for that method. Queue capability values are synthetic;
actual HTTP/phone-home projection coverage comes from CP-2/CP-4, not this rerun.

The prior Debug's production baseline was **11 Antiphon.PtyHost.exe plus
11 OpenConsole.exe**, no new/missing after each row. The rerun says 11
OpenConsole.exe and 0 **PtyHost.exe**. That exact-name observation does not prove
there were no **Antiphon.PtyHost.exe** processes. These historical inventories
must not be combined into a current zero-host or upgraded-host claim.

### Landing changed identity, not the named A implementation

`git merge-base --is-ancestor L B` returned 0. Comparing all **52** paths changed
from `a10bd1883..C` against L found only 21 master additions in
`docs/testing-and-build.md`; no changed A production/test bytes. Comparing those
same 52 paths from L to B returned no paths. This supports retaining the older
behavioral evidence with its actual SHAs. It does not make their receipt source
fields equal L.

The freeze at lines 1102–1117 expressly requires a separate final-SHA Windows
Debug across CP-3–CP-8, minimum **95**, and a refresh at landed SHA when landing
changes SHA. No task/report in the measured release-A records supplies that run
at C or L. No qualification execution was performed here.

## Loaded state measured directly

API base resolved through `ANTIPHON_API` to
`https://antiphon.desktop.codeperf.net`, the configured access path to the desktop
server. Reads used the inherited task-token header without exposing its value.

| Surface/time UTC | Measured result | Acceptance implication |
|---|---|---|
| `/api/version`, 12:26:52 | `bb18064ba647e0ddb03cae4da437ab60ed447d98`; capabilities `land-v2`, `operator-shutdown-v1` | Serving server is pre-A. |
| `/api/hosts`, 12:26:52 | local: available/eligible, effective limit 2, inFlight 0; server2: available/eligible, capacity/limit 10, inFlight 9; temp: unavailable/ineligible, inFlight 0 | Capacity/availability is not backend/host provenance. |
| `/api/session-runners`, 12:26:52 | desktop Windows available/eligible, occupied 0/2; server2 Linux available/eligible, occupied 9/10; temp platform unknown, stale/draining, not accepting | Desktop occupied counts delegated tasks; 0 does not mean no retained hosts. |
| `/api/session-runners/server2/status`, 12:26:53 | build `4358939ecd85d6e7ff0941f970879499cb930e3d`, phone-home store `f519bd08-e53a-47d1-adb1-2ab33475446f`, boot `1287e593-53b4-4ed9-9037-b3efa80e0176`, eligible/accepting, sessions 8 | Serving Linux runner predates A; successive occupancy reads are not atomic. |
| `/api/session-runners/server2-temp/status`, 12:26:53 | unavailable/ineligible; build/store/boot null; draining, redirect server2, retired `2026-10-02T23:09:02.970849Z`, reason idle | No running temp build can be inferred. |
| `/api/session-runners/desktop/status`, 12:26:53 | null build/platform/store, `disconnectReason=desktop`, unavailable/ineligible | This phone-home status projection is not evidence the desktop is offline: catalogue says available. It gives no desktop build. |
| local runner `/capabilities`, 12:29:04 | HTTP 200; version `4358939...`; process start `2026-10-02T22:52:36.2510827Z`; platform linux; backend **InboxConhost**, requested **inbox**, reason `ANTIPHON_PTY_BACKEND='inbox'`, fallback false; deprecation absent | Old Linux reporting is directly observed; no Windows ConPTY execution is implied. |
| local runner `/sessions`, 12:29:04 | HTTP 200; 9 Running sessions, including this task's `bef93db9-fdd3-4423-a6e5-310b2e60d2d7`, host PID 12207; no per-host build/backend in returned rows | Establishes owned task/runner correlation; does not qualify Windows host adoption. |
| `/api/runner-defaults`, 12:26:52 | revision 2, global server2, no kind overrides, provenance Human | Placement policy was read, not changed. |

The Linux runner is at `http://127.0.0.1:8080`, as documented in
`docs/ops-http.md:161` and `docker-compose.server2-runner.yml:51`.
Its `/state/runner-store-id` equals the API phone-home store `f519bd08...`.
The capability `runnerStoreId=897f354b-0f0d-4032-93a3-d3addffccbe2` is the
separate runtime custody-ledger identity, not a conflicting phone-home identity:
`src/Antiphon.SessionRunner/PhoneHomeConnectionService.cs:99` versus
`src/Antiphon.SessionRunner/SessionRunnerRuntime.cs:72` and line 768.

This task's actual loaded host executable resolves via `/proc/12207/exe` to
`/tmp/antiphon-pty-hosts/bin/20261002-225244-4ce9b627/Antiphon.PtyHost`.
Its log `/tmp/antiphon-pty-hosts/logs/bef93db9fdd34423a6e5310b2e60d2d7.log`
retains these exact observations:

```text
2026-10-04T12:25:06.1449311Z [INF] pty-host starting: session bef93db9-fdd3-4423-a6e5-310b2e60d2d7, pipe /tmp/antiphon-pty-bef93db9fdd34423a6e5310b2e60d2d7, pid 12207
2026-10-04T12:25:06.5098210Z [INF] Launched /usr/local/bin/codex (child pid 12366); pty backend: InboxConhost (requested 'inbox'): ANTIPHON_PTY_BACKEND='inbox'
```

This was an existing inherited task process, not a canary launched here.
The shadow directory suffix is a content hash, not a Git SHA
(`src/Antiphon.PtyHost.Client/ShadowCopyStore.cs:8`). No host product SHA was
obtained from that path alone.

Ancestry measurements: `L -> bb18064ba` exit 1; `bb18064ba -> L` exit 0;
`L -> 4358939e` exit 1; `L -> B` exit 0. The loaded server source at
`bb18064ba:src/Antiphon.Agents.Pty/PtyBackend.cs:97` still defaults unknown/empty
to InboxConhost. L's policy at `src/Antiphon.Agents.Pty/PtyBackend.cs:90`
first returns UnixPty on non-Windows and at line 99 chooses modern on Windows.
Shared runtime composition is at `src/Antiphon.SessionRunner/Program.cs:135`,
`SessionRunnerRuntime.cs:129`, local projection line 755 and
`PhoneHomeRuntimeAdapter.cs:61`; nullable deprecation is at
`src/Antiphon.SessionRunner.Contracts/SessionRunnerContracts.cs:989`.
These source and observed-build facts reconstruct the activation gap.

The Review caller described server2 as transport-unaffected, and the successor
plan says no Linux ConPTY rollout is required. Preserve that scope: this observed
old Linux **reporting** is not a newly demonstrated Unix transport regression or
authority to deploy a runner. It remains pre-A reporting if fleet-wide A
capability activation is claimed.

### Windows current-state limit

Read-only GETs to the documented desktop Tailscale address
`http://100.79.51.37:17204/capabilities` and `/sessions` each timed out after
10 seconds at 12:27:42 and 12:27:52. Attempts against this Linux namespace's
127.0.0.1:17204 returned connection refused; that port is not the desktop.
No remote shell/executor, restart, new session or process termination was used.

The **last stored direct Windows capability observation**, from both Debug
reports, is build `6a88d8ceaedb5934bb3a466802a622a4b56e5b37`, process start
`2026-10-04T03:44:10.9742312Z`, ModernConPty/requested modern/no fallback,
package 1.24.260710001 under canonical runner `bin\Debug\net9.0\conpty\win-x64`,
deprecation absent. This is historical, not a fresh 12:26 observation.
Current Windows runner build, adopted-host builds/packages/backend, canonical
checkout HEAD/locks and pending deliveries therefore remain unknown.
Runner availability and server health cannot substitute for them.

## Mutation companion and operator pause

Complete `scripts/card.ps1 search ... -Board Antiphon -All -Json` reads returned
`total=0 truncated=false nextPageToken=null` for:

```text
post-land-verification:b7c17822-63b4-4626-b5bb-525cfccc0cea
Post-land verification: CARD-1022
b7c17822
post-land-verification:e41a7005
```

A broader complete search for `post-land-verification` returned **10** cards,
`truncated=false nextPageToken=null`, none mentioning CARD-1022, b7c17822,
e41a7005 or ffc43849 in returned fields. The board-scoped delegated-task read
`GET /api/agent-tasks?boardId=8988ca03-7414-47ad-b0b6-51556c701703&since=2026-10-03T00:00:00Z`
returned 168 rows and **zero Mutation rows**. The API's since semantics retain
all open tasks and recent settled tasks (`docs/antiphon-api.md:299`), covering
O's creation on October 4. These searches establish absence of a discoverable
standard companion/current post-land Mutation task, not absence of every
possible differently worded historical record.

Original card revisions 1–5 are only Move revisions, with no reverse companion
link; the current description also contains none. The role brief's operator
pause and the stored Review brief both keep **PC-1–PC-73, all arguments/internal
boundary variants and missing-control discovery pending**. A missing companion
does not waive this obligation or authorize resumption. Companion requirements
are in `docs/orchestration-loop.md:1718`; sourced Mutation prohibition on snapshot
commits is at line 1764. This task is an ordinary investigation worktree, not
a SourceLanding snapshot.

## Proven gaps and what would resolve each

| Gap | Evidence | Missing evidence needed for acceptance |
|---|---|---|
| G-1: separate final-SHA Windows qualification | Windows transcripts 130/95 split evidence between `4e865e05` and C; fresh rerun is only 44. Freeze lines 1102–1117 requires six rows/95 at final identity; C differs from L. | Separate TestDesign disposition of exact published L qualification, preserving all 155 roster results and carried ordinary Review evidence; then the commissioned native Debug receipts with actual source/build, per-row counts, zero skips and host/recipient provenance. No new tests/fix are inferred. |
| G-2: serving server predates A | `/api/version=bb18064ba`; measured ancestry and old resolver bytes; land event says canonical checkout dirty. | Caller-owned canonical source/loaded server identity containing L, plus direct changed-feature/capability/ceiling and complete delivery evidence after required runner/host acceptance. No restart is authorized by this investigation. |
| G-3: current Windows runner/retained-host acceptance unknown | Only historical `6a88d8ce` capability; live endpoint timeout; historical 11 hosts survive Debug; catalogue occupied=0 counts delegates. | Read from the owning Windows host: current runner capability/build/request/fallback/deprecation, exact retained-host manifests/images/logs/package hashes and pending deliveries, followed by the required fresh owned modern host and complete destination receipt/separate-Enter evidence. |
| G-4: Linux A reporting not active | Serving server2 status and local capability both `4358939e`; exact inherited-host log says InboxConhost/inbox, no deprecation field. | Explicit caller disposition of metadata activation scope, or observed A-containing runner/capability identity if claiming fleet-wide UnixPty reporting. Unchanged Unix transport is not a missing Windows native test. |
| G-5: durable companion linkage missing while Mutation paused | Complete four targeted and one broad card searches; zero board Mutation rows; no reverse card link. | Caller reconciliation/record of the same-board companion for b7c17822/O/L/R and pending PC inventory, preserving the operator pause. Any later resume remains separately commissioned SourceLanding work. |
| G-6: publication outcome delivery unconfirmed | O is published; outcome notification 44bc48c3 is Canceled/unconfirmed while completion receipt is separately confirmed. | Caller acknowledgement/reconciliation of that existing publication outcome if required for closure; never a replacement publication. |

Confirmed mechanism: publication, ordinary Review, exact-source native Debug,
loaded binaries, retained host generations, destination receipts and post-land
Mutation are distinct records. Here the rebase changes C to L without an L
qualification receipt, the serving binaries remain older, and the standard
companion is undiscoverable. Successful task statuses and arithmetic across
SHAs cannot fill those gaps. This is reconstructed from durable task rows,
normalized transcript entries and direct runtime measurements.

Remaining uncertainties: original Windows receipt/TRX bytes and package files
cannot be read here; current Windows/canonical filesystem state is inaccessible;
a differently named/unlinked companion is possible; unobserved activation work
is not disproved by timeout, but no acceptance receipt for it was found in the
bounded release-A records. Separate TestDesign follows for the demonstrated
qualification gap; no new implementation or ownership decision is required.

## Not done, noted

No code fix designed or implemented; canonical runner-first/server-last activation and companion reconciliation remain caller-owned after qualification, with Mutation paused.

## Investigation validation and provenance

No build, test, mutant, live-provider canary, restart, deploy, routing/card write,
or source edit occurred. Only this authored Markdown is committed. Generated
HTTP/transcript snapshots and retrieval logs remain gitignored under
`.antiphon/acceptance-evidence/`; they are supporting scratch, not the durable
deliverable. This document preserves their material projections, identifiers,
timestamps and unedited historical checkpoint evidence. No secret was printed.

Document validation: roster blob equality at HEAD/L/B, source-path comparisons,
ancestry exits, receipt-count reconciliation, whitespace check and full assigned
base-to-report evidence-history guard. The guard/commit/push outcome is reported
by this task's final message. No product verification is claimed for the report SHA.

## Essential unedited historical CHECKPOINT evidence

The following lines are copied from the producer reports retained in the
normalized transcript entries named above. Inline Markdown delimiters around
4608a549's lines are excluded; CHECKPOINT content is unchanged. No source SHA,
path, count, filter or provenance field has been relabelled.

```text
CHECKPOINT CP-3 commit=4e865e05d288374c0d61d8599080a5c399db5f4e build=ok filter=/*/*/(PtyBackendContractTests*)|(ModernPtyDa1Tests*)|(PtyBackendEnvGuardTests*)|(ConPtyEnvironmentIsolationGuardTests*)|(C1022TypedInputTests*)|(PtyInputChunkingTests*)/* executed=18 passed=17 failed=1 skipped=0 trx=C:\Antiphon\worktrees\card-task-28a1714e\.antiphon\checkpoints\CP-3-20261004-064157-9c13\run.trx slot=granted waited=0s dirty=0 source=4e865e05d288374c0d61d8599080a5c399db5f4e sourceState=clean buildSource=verified
CHECKPOINT CP-3 commit=4e865e05d288374c0d61d8599080a5c399db5f4e build=ok filter=/*/*/(PtyBackendContractTests*)|(ModernPtyDa1Tests*)|(PtyBackendEnvGuardTests*)|(ConPtyEnvironmentIsolationGuardTests*)|(C1022TypedInputTests*)|(PtyInputChunkingTests*)/* executed=18 passed=17 failed=1 skipped=0 trx=C:\Antiphon\worktrees\card-task-28a1714e\.antiphon\checkpoints\CP-3-20261004-064340-473d\run.trx slot=granted waited=0s dirty=0 source=4e865e05d288374c0d61d8599080a5c399db5f4e sourceState=clean buildSource=verified
CHECKPOINT CP-4 commit=4e865e05d288374c0d61d8599080a5c399db5f4e build=ok filter=/*/*/(C1022BackendLaunchTests*)|(PtyBackendEnvGuardTests*)/* executed=4 passed=4 failed=0 skipped=0 trx=C:\Antiphon\worktrees\card-task-28a1714e\.antiphon\checkpoints\CP-4-20261004-064412-0cb8\run.trx slot=granted waited=0s dirty=0 source=4e865e05d288374c0d61d8599080a5c399db5f4e sourceState=clean buildSource=verified
CHECKPOINT CP-5 commit=4e865e05d288374c0d61d8599080a5c399db5f4e build=ok filter=/*/*/(ShadowCopyStoreTests*)|(PtyBackendEnvGuardTests*)/(Shipped_conpty_binaries_survive_the_deps_json_closure_filter*)|(The_suite_ignores_an_inherited_pty_backend*) executed=2 passed=2 failed=0 skipped=0 trx=C:\Antiphon\worktrees\card-task-28a1714e\.antiphon\checkpoints\CP-5-20261004-064501-ae93\run.trx slot=granted waited=0s dirty=0 source=4e865e05d288374c0d61d8599080a5c399db5f4e sourceState=clean buildSource=verified
CHECKPOINT CP-6 commit=4e865e05d288374c0d61d8599080a5c399db5f4e build=ok filter=/*/Antiphon.Tests.Application/(PtyDeliveryCeilingsTests*)|(SessionDeliveryProfileTests*)|(GrokDeliveryShapeTests*)|(TypedBodySpillTests*)/* executed=44 passed=44 failed=0 skipped=0 trx=C:\Antiphon\worktrees\card-task-28a1714e\.antiphon\checkpoints\CP-6-20261004-064541-b857\run.trx slot=granted waited=0s dirty=0 source=4e865e05d288374c0d61d8599080a5c399db5f4e sourceState=clean buildSource=verified
CHECKPOINT CP-7 commit=4e865e05d288374c0d61d8599080a5c399db5f4e build=ok filter=/*/*/(PtyBackendEnvGuardTests*)|(SessionQueueReceiptPlumbingTests*)|(RunnerGrokAdapterReadyTestsPty*)/(The_suite_ignores_an_inherited_pty_backend*)|(C475_*)|(C1022_Incomplete_or_stale_receipts_do_not_confirm*)|(C1011_windows_backends_reach_ready_and_complete_prompt*) executed=23 passed=22 failed=1 skipped=0 trx=C:\Antiphon\worktrees\card-task-28a1714e\.antiphon\checkpoints\CP-7-20261004-064827-4a1c\run.trx slot=granted waited=0s dirty=0 source=4e865e05d288374c0d61d8599080a5c399db5f4e sourceState=clean buildSource=verified
CHECKPOINT CP-7 commit=4e865e05d288374c0d61d8599080a5c399db5f4e build=ok filter=/*/*/(PtyBackendEnvGuardTests*)|(SessionQueueReceiptPlumbingTests*)|(RunnerGrokAdapterReadyTestsPty*)/(The_suite_ignores_an_inherited_pty_backend*)|(C475_*)|(C1022_Incomplete_or_stale_receipts_do_not_confirm*)|(C1011_windows_backends_reach_ready_and_complete_prompt*) executed=23 passed=22 failed=1 skipped=0 trx=C:\Antiphon\worktrees\card-task-28a1714e\.antiphon\checkpoints\CP-7-20261004-065331-88dd\run.trx slot=granted waited=0s dirty=0 source=4e865e05d288374c0d61d8599080a5c399db5f4e sourceState=clean buildSource=verified
CHECKPOINT CP-8 commit=4e865e05d288374c0d61d8599080a5c399db5f4e build=ok filter=/*/*/PtyBackendEnvGuardTests/* executed=1 passed=1 failed=0 skipped=0 trx=C:\Antiphon\worktrees\card-task-28a1714e\.antiphon\checkpoints\CP-8-20261004-065535-3a8b\run.trx slot=granted waited=0s dirty=0 source=4e865e05d288374c0d61d8599080a5c399db5f4e sourceState=clean buildSource=verified
CHECKPOINT CP-3 commit=65a78f3b1f88c213bbb4ac595681212476447130 build=ok filter=/*/*/(PtyBackendContractTests*)|(ModernPtyDa1Tests*)|(PtyBackendEnvGuardTests*)|(ConPtyEnvironmentIsolationGuardTests*)|(C1022TypedInputTests*)|(PtyInputChunkingTests*)/* executed=20 passed=20 failed=0 skipped=0 trx=C:\Antiphon\worktrees\card-task-4608a549\.antiphon\checkpoints\CP-3-20261004-073413-f21a\run.trx slot=granted waited=0s dirty=0 source=65a78f3b1f88c213bbb4ac595681212476447130 sourceState=clean buildSource=verified
CHECKPOINT CP-7 commit=65a78f3b1f88c213bbb4ac595681212476447130 build=ok filter=/*/*/(PtyBackendEnvGuardTests*)|(SessionQueueReceiptPlumbingTests*)|(RunnerGrokAdapterReadyTestsPty*)/(The_suite_ignores_an_inherited_pty_backend*)|(C475_*)|(C1022_Incomplete_or_stale_receipts_do_not_confirm*)|(C1022_Partial_receipt_turn_parks_interrupted_attempt*)|(C1011_windows_backends_reach_ready_and_complete_prompt*) executed=24 passed=24 failed=0 skipped=0 trx=C:\Antiphon\worktrees\card-task-4608a549\.antiphon\checkpoints\CP-7-20261004-073540-05ef\run.trx slot=granted waited=0s dirty=0 source=65a78f3b1f88c213bbb4ac595681212476447130 sourceState=clean buildSource=verified
CHECKPOINT CP-1 commit=65a78f3b1f88c213bbb4ac595681212476447130 build=ok filter=/*/*/(PtyBackendPolicyTests*)|(Da1StartupResponderTests*)/* executed=30 passed=30 failed=0 skipped=0 trx=/work/worktrees/task-71a5997b/.antiphon/c1022-final-review/CP-1-20261004-065007-8b3a/run.trx slot=granted waited=0s dirty=0 source=65a78f3b1f88c213bbb4ac595681212476447130 sourceState=clean buildSource=verified
CHECKPOINT CP-2 commit=65a78f3b1f88c213bbb4ac595681212476447130 build=ok filter=/*/*/(C1022BackendCapabilitiesTests*)|(RunnerCapabilitiesTests*)/* executed=12 passed=12 failed=0 skipped=0 trx=/work/worktrees/task-71a5997b/.antiphon/c1022-final-review/CP-2-20261004-065045-4c6d/run.trx slot=granted waited=0s dirty=0 source=65a78f3b1f88c213bbb4ac595681212476447130 sourceState=clean buildSource=verified
CHECKPOINT CP-9 commit=65a78f3b1f88c213bbb4ac595681212476447130 build=ok filter=/*/*/(UnixPtyArgvTests*)|(PtyBackendEnvGuardTests*)/* executed=18 passed=18 failed=0 skipped=0 trx=/work/worktrees/task-71a5997b/.antiphon/c1022-final-review/CP-9-20261004-065220-725f/run.trx slot=granted waited=0s dirty=0 source=65a78f3b1f88c213bbb4ac595681212476447130 sourceState=clean buildSource=verified
```

--- next stage ---
next: test-design
handoff: Bind missing release-A qualification to published L=f11d2715f340c7caa03343862cb8dd73cc27f66b; preserve b7c17822 ownership, the 155-result roster and accepted prior ordinary rows. Separate native Debug and activation/host receipt requirements; record the absent companion and keep all Mutation PCs paused. No implementation replay or restart is authorized.
artifact: docs/investigations/2026-10-04-card-1022-release-a-acceptance.md
