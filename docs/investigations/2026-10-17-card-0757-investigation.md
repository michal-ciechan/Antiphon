# CARD-0757 ScaledTimeProviderTests flake

Date: 2026-10-17. Train: master-only. Checkout: `feat/card-task-6b04fd2f` at `aa3f12f1d7925f0f0df91971f00c86e6548dd64a` (same commit as `origin/master`). `origin/rc/2026-10-15` is `69978ec1a246cd7a860f88a364eafc53a416aff7`.

Verdict: already fixed on both tips. The 2026-10-02 cross-clock ratio is not in the current class. No code change.

## What failed

CARD-0757 (board Antiphon, id `28ea1b19-6b7b-47a3-a142-36070fe907a3`, still Backlog) records a CARD-0749 Windows Final review: 4 of 6 `ScaledTimeProviderTests` failed in a full Unit lane and the class passed 6/6 alone. The 2026-10-02 investigation (`docs/investigations/2026-10-02-flaky-test-root-causes.md`, commit `987d030b4496f393edd5e7d79aafe508edf39fc6`) names the mechanism: `Speed_10` samples `clock.GetUtcNow()` and `Stopwatch.Stop()` at different instants around `Task.Delay(100)`, so preemption between those reads lowers the apparent ratio. The token `b1b80c61` in the card is not a git object in this clone.

That source is the parent of the rewrite, `da9edf0b837b81e8fdf4ccc19c8f80be3b78de75`:

- `ScaledTimeProviderTests.cs:14-20` starts a `Stopwatch`, awaits `Task.Delay(100)`, then requires the scaled move to sit between 8× and 12× `real.Elapsed`.
- The other elapsed cases bound real time: `:30-31` (50–500 ms), `:43` (`Task.Delay(50)` then `IsCompleted`), `:53-54` (drift versus `DateTimeOffset.UtcNow` within 1 s), `:59` (at least 90 ms), `:68-69` (`Task.WhenAny` against a 500 ms real delay).
- `ScaledTimeProvider.cs:17-23` takes no clock source. `:22-23` and `:31` read `DateTimeOffset.UtcNow` / `Stopwatch`. `:47-48` creates timers on `TimeProvider.System`.

This pass did not rebuild that parent or repeat a loaded lane. The card text plus that committed source is the stored red witness.

## What is on master and rc

Both tips have the same blobs:

| File | Blob |
|---|---|
| `tests/Antiphon.Tests/TestHelpers/ScaledTimeProviderTests.cs` | `da79041c15a55350e9a02064409a607236c1ebdb` |
| `tests/Antiphon.Tests/TestHelpers/ScaledTimeProvider.cs` | `2e91e5a6751658a68321c3f892841e363aef7018` |

`git diff origin/rc/2026-10-15 origin/master` for those paths is empty. Nothing after these two commits touches either file on `origin/master`:

- `07b1e8fee1e43d64ffb8688bc612da7e6ea9d8a6` (2026-10-02, author date 03:21:11Z) `test: specify scaled clock boundaries with fake source` — rewrites the six tests.
- `6cff76eae6326654794ed68d6da8564adabcd9f2` (2026-10-02, author date 03:23:42Z) `test: inject source clock into scaled time provider` — adds the optional source. Parent is `07b1e8fee`.

Both commits are ancestors of `origin/master` and of `origin/rc/2026-10-15`. Neither message names CARD-0757. The plan commit `c345371e2aa0ed5c127b27f71d3199a29421122f` (09:07 +0100 the same day) already contains them, while `docs/superpowers/plans/2026-10-02-flaky-test-fixes-plan.md` still says S7 may start. That sentence is stale. The S7 controls in that plan (exact 100 ms source step, 1 s UTC and timestamp, timer incomplete at 99 ms and complete at 100 ms) match the file that landed.

Current class, `ScaledTimeProviderTests.cs:13-22`: a `FakeTimeProvider` is advanced exactly 100 ms and both UTC and timestamp elapsed must equal 1 s (`scaled-utc-100ms-is-1s`, `scaled-timestamp-100ms-is-1s`). There is no `Stopwatch`, no real `Task.Delay(100)`, and no comparison with `DateTimeOffset.UtcNow`. The other five methods advance the same fake source (`:35-38`, `:59-67`, `:89-92`, `:111-116`) or only check the constructor (`:128-129`).

`ScaledTimeProvider.cs:16-23` takes `TimeProvider? source` and defaults it to `TimeProvider.System`. `:31-33` scales `_source` elapsed. `:47-48` creates timers on `_source`. `Speed_10` has no second clock and no wait, so a preemption between a scaled read and a stopwatch stop cannot change the ratio.

## Narrow run

One class filter on this Linux host, not a lane. Build slot `card-0757-scaled`. Output `bin-c0757/`, `UseAppHost=false` (without that flag the Linux extensionless `fakeclaude` apphost collides with the `fakeclaude/` helper directory; that first build's MSB3027 is not a test result).

`dotnet run --project tests/Antiphon.Tests --no-build --property:OutputPath=bin-c0757/ --property:UseAppHost=false -- --treenode-filter '/*/*/ScaledTimeProviderTests/*'`

TUnit 1.44.0, .NET 9.0.21, linux-x64. total 6, failed 0, succeeded 6, skipped 0, duration 604 ms. Source SHA `aa3f12f1d7925f0f0df91971f00c86e6548dd64a`.

## Out of scope

Call sites that still construct `ScaledTimeProvider` on the system clock are the CARD-0735 harness speed-up, not this class: `BridgeQueueHarness.cs:111`, `DeliveredSpillFixture.cs:36`, `ChannelOutboundUnifiedTransportTests.cs:50`, `SessionMessageQueueDeliveryVerificationTests.C561.cs:23` (and the same constructor at `:62`, `:92`, `:117`, `:149`). The helper default stays `TimeProvider.System`, which is what S7 specified. CARD-0931 is a checkpoint filter that selects zero tests; it does not block this card (`blockedBy` empty in the 2026-10-17 triage).

## Remaining uncertainties

No Windows rerun and no 30-repeat loaded stress (plan CP-19). One quiet Linux pass does not replay the 2026-10-02 full lane. The old ratio cannot recur inside this class: both samples are one `FakeTimeProvider.Advance` on the calling thread.

## Not done, noted

No further edit: the landed fake-source assertions are the fix; a red witness would drop only the UTC multiplier so label `scaled-utc-100ms-is-1s` sees 100 ms instead of 1 s, under the same filter `/*/*/ScaledTimeProviderTests/*`.
