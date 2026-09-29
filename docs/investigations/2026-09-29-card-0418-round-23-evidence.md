# CARD-0418 round 23: explicit outbound policy closure

This continues the [round-22 ledger](2026-09-29-card-0418-round-22-evidence.md).
The tested code tip is `f3ffb08044aaecdc4a1b01aeb1e11d291286cc49`.
No shared stack, real destination, land, or SourceLanding Mutation was used.

## V-5 closed: policy, HTTP and client matrix

The following is the actual-run index for **each V-5 clause**, with a decisive
oracle rather than a broad-ID test name. All listed .NET methods have a Passed,
non-skipped result in the named fresh TRX. The final run root is
`.antiphon/checkpoints/20260929-100004-51e1/`; CP-3, CP-6 and CP-7 have
`rows/CP-3/run.trx`, `rows/CP-6/run.trx` and `rows/CP-7/run.trx` there.
CP-12's native `rows/CP-12/console.log` records 26/26 and
`CLIENT TESTS EXIT CODE: 0`.

| Assertion | Named execution | Decisive oracle |
|---|---|---|
| `V-5/policy-defaults` | CP-3 `ChannelOutboundMigrationTests.Upgrade_preserves_history_and_defaults`, `ChannelOutboundPolicyTests.Profile_binding_defaults_and_validation`; CP-7 `ChannelBridgeTests.First_inbound_message_discovers_the_channel_unrouted` | Migrated, newly inserted and actually inbound-discovered channels have null profile; default settings have no profiles. The unprofiled reply publishes directly after the configured profile is removed. |
| `V-5/validation-boundaries` | CP-3 `ChannelOutboundPolicyTests.Invalid_profile_bindings_are_atomic_failures` (13 data rows); `Profile_limits_refuse_out_of_range_values` | Disabled/unbound, mismatched projects, absent/same/nondelegatable/busy converter, inbound-bound converter, missing or escaping prompt, and absent workspace all refuse; timeout 9/10/120/300/301 and MaxPending 0/1/8/32/33 have their boundary verdicts. |
| `V-5/atomic-patch` | CP-3 `ChannelOutboundPolicyTests.Invalid_profile_bindings_are_atomic_failures`, `Profile_binding_defaults_and_validation`; `ChannelOutboundEndpointTests.Profile_patch_clear_and_rebind` | Invalid profile plus `DigestEnabled=true` leaves both profile null and digest false in the database. Unrelated PATCH preserves a valid binding; clear, unbind and actual agent rebind clear it. HTTP unknown-profile PATCH returns 422 and leaves the valid binding stored. |
| `V-5/client-round-trip` | CP-3 `ChannelOutboundEndpointTests.Profile_patch_clear_and_rebind`; CP-12 `ChannelsPage.test.tsx` save/clear and validation tests | X PATCH/GET/clear/GET round-trips while Y/Z remain null; HTTP profile/status carries project, converter, prompt SHA-256 revision, trigger, deadline and metered authorization. The client selects, saves, refetches, clears, refetches, shows pinned details, and leaves an API-refused selection unsaved. |
| Metered task per matched reply | CP-6 `ChannelOutboundDispatchIntegrationTests.Dispatcher_defers_only_the_bound_conversation_and_preserves_source_bytes`; earlier round-14 V-24 isolated native run | The matched X source makes exactly one linked converter task through the pump, including after publication; Y publishes source-only with no converter intent. The isolated X worker in round 14 executed once behind the held tool gate. This is local metered-work evidence, not a live-model receipt. |

The CP-3 validation method's 13 argument rows are individually present in the
TRX; a class pass was not substituted for absent rows. The CP-6 method checks
the linked task count before and after publication. The client log records two
files and 26 passing cases. The prior isolated V-24 native result is
`.antiphon/v24-r14-checkpoints/V24-r14d-20260928-180414-f31d/run.trx`
as recorded in the [round-14 ledger](2026-09-28-card-0418-round-14-evidence.md);
the V-5 closure does not depend on that path for its HTTP/client verdict.

## Final checkpoint run and timing triage

The complete closed CP-1 through CP-13 list ran once on the committed code tip
in `.antiphon/checkpoints/20260929-100004-51e1/`. Its `report.md`, TRX,
console logs and slot records are the native results. Every row held a granted
build slot, and the tool launched no unlisted row. A separately leased
bootstrap build of `tools/Antiphon.Checkpoints` was needed because this
worktree had no built tool; it succeeded with the existing `TaskOwnerGuard.cs`
CS8602 warning. Earlier committed slices ran full lists in
`.antiphon/checkpoints/20260929-083850-d4b6/` and
`.antiphon/checkpoints/20260929-090203-2bd4/` and
`.antiphon/checkpoints/20260929-092842-003d/`; the first exposed a test-only
400 versus actual 422 expectation, corrected before the final code tip.

| Row | Executed | Passed | Failed | Skipped | Verdict |
|---|---:|---:|---:|---:|---|
| CP-1 Unit | 3485 | 3484 | 1 | 33 | Timing assertion described below |
| CP-2 source settlement | 371 | 371 | 0 | 0 | Green |
| CP-3 policy/schema | 32 | 32 | 0 | 0 | Green; V-5 matrix |
| CP-4 file boundary | 57 | 57 | 0 | 0 | Green |
| CP-5 purpose/deadline | 7 | 7 | 0 | 0 | Green |
| CP-6 crash/transport | 16 | 16 | 0 | 0 | Green; one linked task oracle |
| CP-7 routing/attention | 320 | 320 | 0 | 0 | Green |
| CP-8 existing deadlines | 71 | 69 | 2 | 0 | Inherited T1/T2 `codex_desktop_unqualified` |
| CP-9 renderer | 19 | 19 | 0 | 0 | Green |
| CP-10 real browser | 1 | 1 | 0 | 0 | Green |
| CP-11 gateway wire | 125 | 125 | 0 | 0 | Green with broker opt-in |
| CP-12 client | 26 | 26 | 0 | n/a | Green |
| CP-13 client bundle | n/a | n/a | 0 | n/a | Build exit 0 |

CP-1's sole failure was the unrelated
`OperatorShutdownCoordinatorTests.Stop_proceeds_when_the_drain_bound_expires`:
the wall clock measured `00:00:00.9999331` against a one-second lower bound.
For this concrete timing risk, a separate **full CP-1 filter** ran on the same
code commit in `.antiphon/checkpoints/20260929-102630-b3b5/` and passed
3485/3485 with 33 Linux platform skips. This is a recorded one-time timing
failure, not a change to the shutdown code. CP-8's two failures are identical
to round 22 and unrelated to this test-only slice. The final full-list tool
exit is 1: eleven green rows, CP-1 timing red and CP-8 inherited red; the CP-1
triage run exits 0.

## Remaining ordinary and later gates

Whole **V-5** closes this round. Previously closed IDs remain V-1–V-4,
V-19–V-22, V-24, R-1 and R-12. The ordinary open set is now
**V-6–V-18, V-23, R-2–R-11, R-13–R-14**. The
[round-17 sub-assertion inventory](2026-09-28-card-0418-round-17-evidence.md#remaining-ordinary-vr-assertions),
as amended by rounds 18–22, identifies the missing behavior for each open ID.
This document adds a native actual-run index for V-5; it is not a complete
index for every open assertion, so R-14 remains open. R-3 also remains open
because the V-6 send-shape and V-18 policy-race matrices are incomplete.
V-25 still needs its separately authorized actual-destination receipt, and
PC-1–PC-30 still require method-scoped SourceLanding Mutation. Neither the
local checkpoint sweep nor this V-5 index discharges those gates.
