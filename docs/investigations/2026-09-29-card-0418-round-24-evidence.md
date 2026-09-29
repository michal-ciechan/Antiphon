# CARD-0418 round 24: send-shape policy matrix

This continues the [round-23 ledger](2026-09-29-card-0418-round-23-evidence.md).
The tested code commit is `3be461e9ccc61afbf693bd00ea7883de727b61ca`.
No shared stack, real destination, land or SourceLanding Mutation was used.

## V-6 closed: actual dispatcher paths and source trigger

`ChannelOutboundDeliveryTests.Main_trailing_and_machine_use_the_same_policy`
is a parameterized integration method in CP-5's exact class filter. Its **eight
individual argument rows** are Passed and non-skipped in the fresh native TRX
`.antiphon/checkpoints/20260929-112440-f9d9/rows/CP-5/run.trx` (CP-5:
15/15). Each row uses the real `ChannelReplyDispatcher`, the configured
`ChannelOutboundService`, a database channel binding and the in-memory producer.
Every converting row reads its frozen intent and then ticks the real pump to
assert **one linked `AgentTask`**; direct rows assert zero conversion tasks.

| V-6 clause | Named argument row and decisive oracle |
|---|---|
| Main, trailing and machine share the facade | `main/explicit-md`, `trailing/explicit-md`, `machine/explicit-md`: each creates one Pending intent with its exact send kind, trigger and attached source bytes; each pump creates one linked task. The trailing row first publishes a plain interim reply; the machine row first publishes a plain channel reply, then dispatches a matched task-done note. Neither converted reply is published at admission. |
| Generated ZIP with source manifest | `machine/manifest-zip`: an actual ZIP contains the Markdown bytes, its task bundle has a version-1 complete source manifest naming the stored `-sources.zip` and source hash, and the task-done path attaches that ZIP. `ListAttachableFiles` confirms the bundle's admitted file before dispatch. The ZIP creates one Pending intent and linked task. CP-2 `DeliverableBundleServiceTests.Branch_only_git_sources_survive_zip_with_exact_bytes_and_manifest_hashes` separately verifies the production bundle's extracted bytes and manifest hashes on this same code commit. |
| Every reply trigger | `main/plain-markdown/EveryAgentReply`: a Markdown body without files creates one intent and linked task. |
| Markdown-only and archive controls | `main/plain-markdown/MarkdownSources` publishes directly with exact text and `Answer` kind. `main/unrelated-zip/MarkdownSources` has a real source task and manifest but attaches a ZIP absent from that manifest; it publishes the original ZIP bytes directly with no intent/task. Source task identity alone cannot qualify it. |
| Binding opt-in | `main/plain-markdown/none` leaves the channel unbound despite an available profile; it publishes exact text and `Answer` kind with zero intent/task. |

The prior CP-6 `ChannelOutboundDispatchIntegrationTests.Dispatcher_defers_only_the_bound_conversation_and_preserves_source_bytes` passed 16/16 as part of its full class row here. It supplies the separate X/Y positive/control route, one converter task after publication, and frozen bytes/handle. The V-6 verdict is local fake-destination evidence; it does not claim the V-25 live receipt.

## Final checkpoint result

The complete CP-1 through CP-13 closed list ran once against the tested code
commit in `.antiphon/checkpoints/20260929-112440-f9d9/`. The native
`report.md`, row TRXs, console logs and slot records are the authoritative
results. Every build/test driver held a granted build slot; the tool ran no
unlisted row. Tool exit 1 means **12 green rows and the inherited CP-8 red**.

| Row | Executed | Passed | Failed | Skipped | Verdict |
|---|---:|---:|---:|---:|---|
| CP-1 Unit | 3485 | 3485 | 0 | 33 | Green |
| CP-2 source settlement | 371 | 371 | 0 | 0 | Green |
| CP-3 policy/schema | 32 | 32 | 0 | 0 | Green |
| CP-4 file boundary | 57 | 57 | 0 | 0 | Green |
| CP-5 purpose/deadline | 15 | 15 | 0 | 0 | Green; eight V-6 rows |
| CP-6 crash/transport | 16 | 16 | 0 | 0 | Green |
| CP-7 routing/attention | 320 | 320 | 0 | 0 | Green |
| CP-8 existing deadlines | 71 | 69 | 2 | 0 | Inherited T1/T2 `codex_desktop_unqualified` |
| CP-9 renderer | 19 | 19 | 0 | 0 | Green |
| CP-10 real browser | 1 | 1 | 0 | 0 | Green |
| CP-11 gateway wire | 125 | 125 | 0 | 0 | Green with broker opt-in |
| CP-12 client | 26 | 26 | 0 | n/a | Green, two files |
| CP-13 client bundle | n/a | n/a | 0 | n/a | Build exit 0 |

The two CP-8 failures are the same pinned-kind refusals reported in round 23;
they are not introduced by this test-only slice. The CP-1 shutdown timer
flake did not recur.

The separately leased bootstrap build of `tools/Antiphon.Checkpoints` was
needed because this worktree had no built tool. A preliminary closed-list run
at `ddc3d20c5360a2b7f85f50c6af830e3c4e12cb44` found a missing test
interface import and could not execute CP-1–CP-8; five unaffected rows passed.
The next full list at `4a3bc738030a75809660cf3bfb06171fe33a5fb1`
executed all rows and exposed four new CP-5 fixture failures (three cleanup
foreign keys and one invalid ZIP filename). Two earlier starts were stopped
before row results when the new test was outside CP-5's exact class filter
and then while shared build slots were unavailable. The unlisted,
method-scoped `V6-shape-diagnostic` runs were justified solely to resolve the
concrete CP-5 failures: first 7/8 at
`.antiphon/v6-r24-diagnostics/V6-shape-diagnostic-20260929-111843-4e6e/`,
then 8/8 at
`.antiphon/v6-r24-diagnostics/V6-shape-diagnostic-20260929-112200-2e85/`.
Neither diagnostic substitutes for the final closed-list run.

## Remaining gates

Whole **V-6** closes this round. Previously closed IDs remain V-1–V-5,
V-19–V-22, V-24, R-1 and R-12. The ordinary open set is now
**V-7–V-18, V-23, R-2–R-11, R-13–R-14**. R-3 and R-4 remain open because
V-18 policy races and V-7 withheld/control callers remain; this V-6 row does
not discharge those complete regressions. The
[round-17 sub-assertion inventory](2026-09-28-card-0418-round-17-evidence.md#remaining-ordinary-vr-assertions),
as amended by rounds 18–23, continues to identify their missing behavior.
V-25 still requires separately authorized actual-destination acceptance, and
PC-1–PC-30 still require method-scoped SourceLanding Mutation. This local
checkpoint sweep discharges neither gate.
