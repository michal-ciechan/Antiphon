# Why missed: PtyDeliveryCeilingsTests inbox profile on Linux

CARD-1938. Two methods assert `DeliveryBackend.InboxConhost` for an `inbox` server.
On Linux `PtyBackendPolicy.Resolve` returns `UnixPty` before any Windows selector, and
CARD-1224 records that backend as `DeliveryBackend.UnixPty`. The product resolution is
the one Linux should keep. The tests are the Windows inbox profile.

## Introducing commit/card

introducing commit/card: `b35ed98633c4234f037a0fe589547206cf6b747d` (CARD-1224)

`CeilingsFor(PtyBackend.UnixPty)` started returning `DeliveryBackend.UnixPty` in that
commit. Before it, `UnixPty` fell through to `DeliveryBackend.InboxConhost`, so the two
assertions still passed on Linux. The methods themselves are from
`4dbed26ad79` (CARD-0037). `A_silent_runner_leaves_this_processes_own_decision_standing`
calls `RequireRedistributable` only after the inbox assertion.
`An_inbox_server_is_not_raised_by_a_modern_runner` has no Windows guard.
`08a367e98e` (CARD-1022) already returned `UnixPty` off Windows; the assertion stayed
green until the delivery backend stopped aliasing that result to inbox.

## Approving reviews

approving review(s) with scope (Full/Interim) and the selection they actually ran:

- Final review recorded at `97889de69b195a786ff737eaf4bceae95e32fc31` on
  `feat/card-task-b6ebea9c` (not an ancestor of this master). Round 1, Full.
  Reviewed source `bb4f2811c2516d3754db5e34487ce4f6a4497532`.
  `PtyDeliveryCeilingsTests` selection was CP-3 only:
  `/*/*/PtyDeliveryCeilingsTests/(The_inbox_backend_keeps_exactly_the_ceilings_that_shipped*)|(No_modern_ceiling_exceeds_the_measured_envelope*)|(A_runner_bound_brief_is_fitted_to_the_inbox_ceiling_not_the_desktop_pty*)`
  executed 3, passed 3, failed 0. The two methods were not in that filter.

## Outside the selection

why the failing test was outside that selection (lane/category/path): Unit lane, but
the closed CARD-1224 checkpoint named three other methods of the same class. The plan
treated the remaining Windows redistributable methods as already skipped and left them
out of CP-3. These two do not skip before the `InboxConhost` assertion, so that
assumption did not cover them. A later whole-class Linux sweep is what ran them.

## Cheapest pre-land check

the cheapest pre-land check as a path-glob -> class-filter entry for the impacted-classes table of CARD-1782:

`tests/Antiphon.Tests/Application/PtyDeliveryCeilingsTests.cs` -> `/*/Antiphon.Tests.Application/PtyDeliveryCeilingsTests/*`

## Exemption

same cause as an already written note: none

## Repair

Test-only. Both methods skip first off Windows with an annotated `Skip.Test`, and the
Windows assertions stay. The Linux rc profile excludes exactly those two methods
(owner CARD-1938). The other methods of the class stay required on both platforms.

Pins, old then new:

- policyHash `bd6689bbc7ea752f13a69f872ea86a171fc8afc94e90ef9ed0c8d53aab00fbaa` -> `b610138753e25545cda6f208abe19ebd507cdc77afc288c9888b778ad1ff6b9d`
- PinnedInputDigest `070479b6da9902cc6cbd2b791baf855f01333599f6041cc017c45a8fe0cf9c75` -> `26d90df028d4e2358ed43b02aa7ef4ad688224c5470aba371d2fd566ff4a663e`
- LegacyExclusionDigest `e377b73a3c681e2beef6a56d12a0b8fd7215ec4f5e41810e95a225ecdde75a3c` -> `6d6ff646207dba9e3c09316e5bc77651f6801fe945ccde83b9b58644367b252a` (94 records to 95; witnessed count stays 103)
