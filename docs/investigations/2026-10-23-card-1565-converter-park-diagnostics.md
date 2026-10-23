# CARD-1565 converter identity and PARK diagnostics

Debug/fix task `6d45dc66`, Linux runner `0c18bb7f591e`, 2026-10-23.
**C519 is repaired and has passing receipts for all eight cases. Both PARK methods
pass locally. The historical Windows PARK admission hold remains unproved; this
report does not assign it the converter root or the later continuation-fixture root.**

## Proved identity defect and repair

`BuildBriefPointer` used `StringBuilder.AppendLine`, retaining Windows CRLF in the
persisted queue body. `SessionMessageQueueService.DeliverAsync` correctly converts
CRLF to LF through `PtyInputEncoding.NormalizeBody` before typing. The complete
native UserPrompt consequently differs from the persisted body even when delivery
is successful. The normalised runtime receipt matcher can confirm that delivery;
the exact qualification oracle correctly exposes the byte-identity disagreement.

The cross-platform reproduction puts CRLF in the converter title after the real
conversion-task commit/crash, before dispatch. This exercises the same formatter
and PTY normalisation without needing Windows to generate `AppendLine` CRLF.
The original complete-pointer equality and exact UserPrompt/sequence-floor
assertions remain unchanged. There is no seeded replacement brief.

At source `3b54f4835c80d4472bcdddaf01246b873b69efb7`, the trace at the
`conversion-task-committed` and `result-committed` cuts captures the actual submitted
string, queue body and complete transcript. Idle floor is 0. The persisted pointer
has one CR; the submitted pointer has zero. Exact equality is false and equality
after CRLF-to-LF conversion is true, with no other differing bytes. The idle cases
fail the exact receipt count (expected 1, actual 0). Busy companions additionally
expose their raw transcript writes not updating SessionStateStore. Interrupted
attach cases stop earlier because the fake has no accepted dialog generation.

The production fix normalises the formatted pointer to LF before persistence,
join-safe rendering and wire-byte measurement. It changes no receipt assertion,
receipt matcher, timeout, schema, replay policy or production dialog guard. It
applies to newly formatted briefs; it does not rewrite historical queue rows.

Only the card's test methods/helpers were adapted: supported Claude observations
and accepted generations for original and resumed recipients, and the existing
StoreAwareTranscript helper for the converter's busy/idle/receipt writes. Shared
fixtures and the other in-flight cards' areas were not edited.

## PARK evidence and remaining gap

At instrumentation-only source `d6284fa5f24ffdb6a0074dde36022925895f1664`, both
requested methods get **Parked**, `ReasonCode=park_published`, real non-null
publication and runner-seat-release receipt IDs, and pending sync that subsequently
recovers. Neither reproduces the historical Windows Held admission. Both then fail
at `ConfirmContinuationAsync`: `ReleasedSeatAnswer` remains non-null because the
test recipient lacks supported dialog observation. Explicitly binding that local
recipient fixes continuation and preserves the inline and production-chunk pointer
receipt assertions. Both full methods now pass, including the negative evidence
shapes within C1065.

`C1565_PARK` now retains the entire synthetic park record, including reason,
episode, generation, source SHA and both receipt IDs. The Parked assertion also
includes ReasonCode in its failure. This is diagnostic preparation for Windows,
not proof of the historical hold's cause. The assigned desktop checkout is
unreachable from this runner; native evidence was requested during this task.
To resolve the remaining root, apply the instrumentation-only commit to the
historical candidate `5a126f6fc5ae4e16d803b628716b8f0485a37174` on Windows and run
the two PARK methods below. Retain the first Held record and its underlying source
state before changing admission. Do not weaken the Parked or receipt assertions.

## Source and execution receipts

Every build/test/help driver used `scripts/build-slot.ps1`, maxcpucount 6. Builds
used isolated `bin-c1565/`; RC used `bin-c1565-rc/`. Test commands used `dotnet run
--project tests/Antiphon.Tests --no-build --property:OutputPath=bin-c1565/ --` followed
by the selection and TRX output arguments. No Unit lane, whole class, whole suite,
release script, tag, AppHost restart or live-stack change was run.

| Source / run | Selection | Executed / passed / failed | Outcome and cost |
|---|---|---|---|
| d6284fa5f, red/converter | `/*/*/ChannelOutboundUnifiedTransportTests/C519_Converter_handoff` | 8 / 0 / 8 | Earlier source queue Pending; unsupported fake dialog prevents reaching identity. 1m45.681s. |
| d6284fa5f, red/park | Initial parenthesised OR selection | 0 / 0 / 0 | Invalid selection, exit 8; no test credit. Corrected once below. |
| d6284fa5f, red/park-exact | `/*/*/BlockedTaskParkDeliveryTests/*ParkedReviewReply*` | 2 / 0 / 2 | Both Parked, later continuation failure. 1m03.510s. |
| 3b54f4835, identity-red/converter | Exact converter method above | 8 / 0 / 8 | Two idle cases reach exact identity red; other cases expose the separately described fixture gates. 4m42.787s. |
| bea1f3d1e, green/converter | Exact converter method above | 8 / 7 / 1 | 8m54.470s native, 60s admission. Outer 600s limit sends SIGTERM to last recovery probe (exit 143); wrapper exit 124. Complete TRX retained. All eight exact identity checks were reached. This run remains failed. |
| bea1f3d1e, green/park | Exact two-method wildcard above | 2 / 2 / 0 | 1m12.817s native; complete TRX. |
| bea1f3d1e, green/last-case | Exact UID below | 1 / 1 / 0 | 1m43.005s native, 76s admission; complete TRX, driver exit 0. |

The single missing-case rerun used `--filter-uid` (confirmed through the built
test host's gated `--help`), not another eight-case run:

```text
Antiphon.Tests.Application.ChannelOutboundUnifiedTransportTests.1.1.C519_Converter_handoff(System.Boolean, System.String).8.1.0
```

It verifies `C519_Converter_handoff(True, result-committed)` at the same source and
assembly as the first seven passing cases. The rerun's outer 420s bound comprises
at most 180s admission plus 240s execution/setup/teardown. No per-test timeout was
widened and no source changed during either run. Passing TRX receipts cover eight
distinct converter cases plus the two PARK methods; the earlier failed runs are
not relabelled green.

Three source builds passed with zero errors: d6284fa5f (3m02.45s), 3b54f4835
(2m47.57s), bea1f3d1e (4m53.84s). The three builds were needed to compile the
diagnostics, reach the identity witness after earlier fixture gates, and verify
the repair. Existing warnings remain (1179/1180/1180). The final DLL digest and
per-run full SHAs, exact case rosters, durations and outcomes are in
`results-summary.json` in the retained evidence root.

R4 instrumentation was enabled on native test launches through
`ANTIPHON_TEST_PHASE_ROOT` with run/group/attempt/suite metadata. JSONL retains DB
start, migration, clone and drop spans and the recorder remainder. Broker,
filesystem, HTTP and probe-process subdivisions unavailable in these fixtures are
unknown, not zero. These tests are Integration/Slow, in messaging/server. No new
fixture setup lifecycle or broader category policy was introduced. The measured
run remains below R1's 30-minute trigger. The aggregate 600s estimate was too short
under contention (load snapshot at 19:50 UTC: 41.75/32.85/30.01); the interrupted
case alone was rerun with separately bounded admission and execution.

## RC applicability

The production fix is needed for RC's exact converter identity qualification.
The combined patch applies cleanly to RC
`9e97df3f7b3d594c6872a1327502d6877feaf820`. Its full test-project compile fails with
four errors because RC lacks master's DialogGuardEnabled and
EnableClaudeDialogObservationAsync test helpers. This failed compile is retained;
the entire task branch is not an unconditional RC cherry-pick.

The production-only formatter hunk applies cleanly and `dotnet build
server/Antiphon.Server.csproj --property:OutputPath=bin-c1565-rc/ --nologo` passed
under a slot, zero warnings/errors, 6.25s, in the RC scratch worktree. Pick source:
`bea1f3d1e25247c5d4c8990a7431ee5bb74ebe5d`, **formatter file only**. The retained
`rc-product.patch` is that exact hunk. The local fixture adaptations target master's
new guard; RC test-helper prerequisites need their own selected picks if those
adaptations are carried. No RC push was made. No claim of native RC execution is
made. Server activation on the RC line remains the caller's landing/deployment
operation; this task did not touch the AppHost restart path.

## Commits and retained evidence

All task slices were pushed to `feat/card-task-6d45dc66`:

| Full SHA | Slice |
|---|---|
| d6284fa5f24ffdb6a0074dde36022925895f1664 | Identity and park diagnostics, no production change |
| 86e81d07c5d6bb3e28fc9a916d11aed087010d01 | Reach converter identity; CRLF witness |
| 3b54f4835c80d4472bcdddaf01246b873b69efb7 | Supported parked-review recipients |
| bea1f3d1e25247c5d4c8990a7431ee5bb74ebe5d | Canonical pointer fix and converter generation/store publication |

Generated logs, TRX, timing JSONL, byte-comparison JSON, source patches and result
manifest are retained outside the worktree at
`/work/repos/antiphon/.git/antiphon/debug/6d45dc66/`. Local originals were under
`.antiphon/c1565/`. Generated evidence is not committed. Task-owned alternate build
outputs and the detached RC scratch worktree are removed after joining every run.

The source-range evidence guard passed with zero violations. The fetched-master
merge-tree was conflict-free (tree `d8683c92bb9dcedce3e3e87b16781bb24c744d62`).
The final report commit reruns both read-only checks. C519 product acceptance and
local PARK receipt acceptance are complete; historical Windows PARK root attribution
remains incomplete. This is a substantive evidence gap, not an advisory correction.
