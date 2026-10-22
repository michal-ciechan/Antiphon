# CARD-1412 wait routing test pins renewal behaviour

Brief-scoped test-adequacy change (no separate Plan stage), follow-up to CARD-1411 (Review clean at
`6c1804ce`, landed as `13048a2eb` on master and `806b23084` on rc/2026-10-20).
`LandDeliveryWaitRoutingTests` pinned which wait method is *named* before each evidence string, so
three mutants of the E2E wait routing survived:

- M2b: a scan wait that keeps the plain `UntilAsync(` name but passes `ProtocolProgress` as its
  progress argument.
- M5: removing `ProtocolProgress()` from `UntilDispatchAsync`'s renewal
  (`LandDeliveryFixture.Dispatch.cs:187`); `UntilProtocolAsync`'s renewal body was never pinned either.
- M6: a comment containing `UntilDispatchAsync(` between a plain call and the evidence string hides a
  call that was changed to plain; the original two search tokens could be hidden the same way.

No product change: the E2E waits already renew correctly; only the pin was name-deep.

## Change

- S1 (tests only): `tests/Antiphon.Tests/LandDeliveryWaitRoutingTests.cs`.
  - `NearestWait` now strips `//` and `/* */` comments (string and character literals kept) and
    resolves the nearest preceding wait call whose parentheses enclose the evidence string;
    evidence that follows a call without being one of its arguments has no owning wait.
  - `Plain_waits_pass_no_progress_probe` (V-1, M2b): every plain evidence's owning call is
    `UntilAsync(` with at most three top-level arguments and no `progress:` named argument.
  - `Renewing_helpers_forward_the_protocol_probe_to_the_shared_wait` (V-2, M5): the inner
    `UntilAsync(` call in `UntilProtocolAsync` forwards `ProtocolProgress`; the one in
    `UntilDispatchAsync` forwards `() => ProtocolProgress() + dispatch` and its predicate wrapper
    refreshes `dispatch` from `DispatchProgressAsync()` on every poll.
  - `A_comment_naming_another_wait_does_not_route_the_evidence` (V-3, M6): the comment stripper
    and the owning-call resolver on synthetic text.
  - Every assertion of `Protocol_bound_waits_renew_on_git_progress_and_scan_waits_do_not` is kept;
    its `NearestWait` calls are now comment-stripped and span-checked (strictly stronger).

## Verification

- V-1: `Plain_waits_pass_no_progress_probe` green on the committed source.
- V-2: `Renewing_helpers_forward_the_protocol_probe_to_the_shared_wait` green on the committed source.
- V-3: `A_comment_naming_another_wait_does_not_route_the_evidence` green on the committed source.
- R-1: `LandDeliveryWaitRoutingTests` whole class (4 methods).
- Red witnesses (brief-required, run by Code outside the closed list, one mutant at a time on the
  uncommitted working tree, build-slot gated, restored with `git checkout` before the next):
  - W-M2b: `LandDeliveryFixture.Dispatch.cs` `TwoNotificationScansAsync` passes
    `, 60, ProtocolProgress` after `"two completed notification scans"`; V-1 must go red.
  - W-M5: `LandDeliveryFixture.Dispatch.cs:187` `() => ProtocolProgress() + dispatch` becomes
    `() => dispatch`; V-2 must go red.
  - W-M6: `DispatchBaseWarningDeliveryE2ETests.cs:130` `f.UntilDispatchAsync(` becomes
    `LandDeliveryFixture.UntilAsync(` and a `// UntilDispatchAsync(` comment is inserted before the
    key-retention evidence string; the queue-cut pin in the existing method must go red.
- PC-1 (pending, Mutation): W-M2b, method-scoped to `Plain_waits_pass_no_progress_probe`.
- PC-2 (pending, Mutation): W-M5, method-scoped to
  `Renewing_helpers_forward_the_protocol_probe_to_the_shared_wait`.
- PC-3 (pending, Mutation): W-M6, method-scoped to
  `Protocol_bound_waits_renew_on_git_progress_and_scan_waits_do_not`.
- PC-4 (pending, Mutation): drop the `ProtocolProgress` argument from `UntilProtocolAsync`'s inner
  `UntilAsync` call (`LandDeliveryFixture.cs:368`); V-2 must go red.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1412-1/` | wait-routing | `/*/*/LandDeliveryWaitRoutingTests/*` | V-1, V-2, V-3, R-1 | all listed, 4 methods (3 new), 0 failed/skipped | 4 | 6 |

### Cost

Ordinary floor 6 minutes (CP-1 including its build). The three red witnesses add one isolated
build and three method-scoped runs outside the table.
