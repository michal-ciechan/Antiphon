# Why the deferred UI worker stop missed short verification

Windows Full run `b8cbfb24` on `rc/2026-12-05` at
`2fd61b643ea4231fa732cd4969b5284d43b33f39` reported one failure in
`antiphon-all-group-54`. The `safe-stub` case of
`C1259_deferred_UI_payload_preserves_original_fields_after_restart` threw
`TaskCanceledException` at the worker join after 7.600 seconds. The stack points
to the second restart, after the original-payload and persisted-row assertions.
The supplied excerpt lists only failures. `sanitized` is the only other
argument and was absent from that failure list. Its native pass row is unverified.
The seed's group-54 entry establishes an earlier failure in that group. The
method is present at the `acca11ba` source pin
`6ed4cb58cd75fe386e94965165d96752452431a8`, although it was absent from the
earlier run's same-numbered chunk results.

## Introducing commit and card

CARD-1259 S2 added the unconditional execution-task join in
`dd039a405c9425d7cffc83b64d60a1032d940636`. CARD-1259 S4 added the two
payload cases in `248fbaa8c2d7be1165ac51a79ef08ba8426875ef` on October 15,
as confirmed by `git log -S`. CARD-1729 moved the worker byte-for-byte in
`21dfde2193c8f66a0b1bc04ea94533aa9add9f4f`.

`PersistFixture.DiscoverAndDrainAsync` starts the worker with a default
token and polls until the restarted session has zero durable action rows.
Its 10-second token bounds the database reads and 20 ms polling delays.
The finally block passes a default token to `StopAsync`. The coordinator
runs discovery and four workers. At stop, the hosting base class cancels its
linked execution token and uses the caller's stop token to bound its own wait.
The override then joins the execution task directly, including after a host
deadline, to keep any running effects and scopes owned.

The resolved `Microsoft.Extensions.Hosting.Abstractions` 10.0.2 dependency queues
execution with its cancellable stopping token. On the second restart the
replayed transcript is already acknowledged, so the empty-store poll can
finish while execution is still queued. Stop can then cancel the queued task
before the coordinator enters. The base class treats this as normal shutdown.
The override's direct await propagates the task's cancellation. That sequence
accounts for the stack and is consistent with the reported desktop CPU load of
71–94%. The Windows excerpt does not show whether the worker entered or
establish that CPU load caused this scheduling order. [Hosting implementation](https://github.com/dotnet/runtime/blob/v10.0.2/src/libraries/Microsoft.Extensions.Hosting.Abstractions/src/BackgroundService.cs)

This is a product shutdown race visible through the fixture. Normal cooperative
stop now joins the worker and accepts its canceled completion. An active effect
still owns its session through the join, even after the host stop deadline. An
execution fault still propagates. Transcript actions are committed with the
transcript before the worker runs, so pending actions remain available for
restart discovery. The Windows trace establishes a failed stop, with no
evidence of data loss.

## Approving reviews

- Review `259aa3b9-ee83-4ab0-b865-1309976822cd` was Clean, Full, at
  `03838480d259a4558095d9ef3e4530f39882b3c0`, with 597 passes across its
  serial CP-29 and CP-1 through CP-14. CP-10 passed 3/3 with
  `/*/Antiphon.Tests.Application/AgentSessionRuntimeTests/(C561_a_NUL_in_tool_result_text_persists_as_U_FFFD*)|(C1259_deferred_UI_payload_preserves_original_fields_after_restart*)`.
  [D-1 checkpoint manifest](../superpowers/plans/2026-10-15-card-1259-d1-checkpoints.md)
- Earlier implementation Review `fe77f665-b03b-4976-995a-26ef71d2d758`
  was Found, Interim, at `7e4127324ca383e897d9c7d7f517f92cb20442c4`. Its
  qualifying run reported 589 passes across 14 rows. The plan-only Clean,
  Full Review `0224941e-3aba-4d3b-9422-274a657ee534` preceded the worker
  implementation.

## Outside the selection

The historical Linux payload guard did run and pass both argument cases.
CARD-1259 CP-13 and CARD-1304 CP-13 each report 3/3 including the adjacent
C561 case. That payload check depends on normal scheduling and leaves the
start/stop race to chance. The new direct witness gives the hosting base class
an already canceled start token to force the same canceled execution task,
then uses the fixture's default stop token. The existing shutdown ownership
case now holds an effect across a one-millisecond deadline advanced by fake
time. Neither witness depends on wall-clock delays or machine load.

[Original Linux receipt](../superpowers/plans/2026-10-15-card-1259-finish-evidence.md)
[Later Linux receipt](../superpowers/plans/2026-11-11-card-1304-code-evidence.md)

`AgentSessionRuntimeTests` is Integration and Slow. It is outside the Unit
lane. The short Merge runs subject rows and the fixed and hand-maintained
impact subset once, with no stress block. A selector replay for the hosted
service and the persistence test paths selected the fixed subset, cleanup and
lane-category guards. It omitted both lifecycle classes, even though the
derived index maps the service to them. The November 25 Linux rc cut gates
also omitted the class. The Windows scheduling order remains unverified. [Short Merge scope](../orchestration-loop.md#merge-verification-is-short-2026-11-18)
[Linux rc cut selections](2026-11-25-rc-cut-gates-at-4022b30f6.md)

## Cheapest pre-land check

Use this CARD-1782 path-to-class entry.
`server/**/SessionRuntimeActionHostedService.cs` →
`/*/*/SessionRuntimeActionTests/*`. For a short Merge, bind the specific
method `/*/*/SessionRuntimeActionTests/C1259_stop_joins_a_worker_cancelled_before_execution`.
The exact existing shutdown and payload methods cover effect ownership and
restart recovery alongside it.

## Exemption

None. The [earlier runtime action miss](2026-11-08-why-missed-session-runtime-action-routing.md)
was a historical schema rewind failure.
