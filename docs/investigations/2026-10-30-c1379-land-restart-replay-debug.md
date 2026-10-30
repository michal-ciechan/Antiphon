# C1379 land restart replay Debug

Task 472dd1cb; base `335b4fbb3b1eac700cb8d3c64ea032d9579c6b5f`; Linux runner mirror.

## Initial evidence

Windows qualification d47848cc, candidate cde8def41, group 79: 288/289 passed,
C1379_OldHeldReplaysBeforeConcurrentNewRequest canceled at original test line 92
(the 20 ms database-observation polling delay), with cleanup at line 102.
The test shares a fixed 30-second CancellationTokenSource across startup, first
consumption, terminal-state polling and second consumption. The token is supplied
to BackgroundService.StartAsync, which also links it to execution lifetime.
Classification remains under investigation; cancellation alone does not prove
an ordering error or a Windows-only cause.

The real hosted reader requires reconciliation and calls ReconcileQueueAsync
before each selection; the queue sorts complete durable requests by RequestedAt
then request identity. A fresh provider clears volatile queue state. The test
uses real fixture-owned Git repositories and an isolated PostgreSQL database.
CARD-0331, CARD-1379 and CARD-1364 public card descriptions were read with
scripts/card.ps1; no private notes were read.

## Verification plan and evidence

Pending: unchanged exact case at least 50 alone and 30 with recorded concurrent
build load through the host slot gate. TEMP=/tmp, UseAppHost=false, isolated
producer-owned bin-c1379-debug/ outputs. Preserve per-invocation logs/TRX and
timing under .antiphon/c1379-debug (gitignored). No native run, live qualification
mutation, tags, publication, definition update, rebase or rc branch mutation.

The task's exact narrow selections override ordinary broad-suite defaults: this
Debug is for one qualification failure. Additional runs will be named and justified.

## Owed native verification

Do not run until Windows qualification d47848cc releases the native lock.
Windows: LandRestartReplayTests class once; then the exact case 20 times alone
and 10 times under recorded load, retaining per-test timing and failure text.
Native coverage remains owed; Linux receipts cannot discharge it.
