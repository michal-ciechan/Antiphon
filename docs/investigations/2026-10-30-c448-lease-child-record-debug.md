# C448 cancellation child-journal Debug

Task 4b7b1ca4; base `335b4fbb3b1eac700cb8d3c64ea032d9579c6b5f`;
Linux runner mirror; qualification failure observed on Windows in run d47848cc,
candidate cde8def41, group 110. Native execution is expressly excluded here.

## Initial trace (measurements pending)

`LandingGit.ExecuteProcessAsync` starts the durable intent before the child,
then saves its identity. Cancellation kills the exact process tree and awaits
root exit plus both redirected reads for `KillDrainGrace` (two seconds).
A complete drain synchronously calls `RepositoryChildJournal.Exited` before
returning cancellation. A grace expiry instead retains the process and installs
an EOF continuation, then returns cancellation with the journal still fencing
admission. `RepositoryMutationLease` separately owns `landing.lock`; disposing
that handle does not bypass the journal admission check.

Consequently the failing assertion precedes lease disposal and does not establish
an early exclusion release. Immediate enumeration can race the intentional EOF
continuation. A synchronous delete failure on the complete-drain path would
throw rather than reach this assertion. A drain fault is swallowed and leaves
the journal, while an EOF-continuation delete failure currently faults an
unobserved continuation. These remain distinct hypotheses until measured.

No live qualification, native checkout, tag, deployment or definition file has
been changed. Evidence is under `.antiphon/c448-debug/` (ignored).
Baseline gated isolated build succeeded: zero errors, 153 s lease held,
reported MSBuild time 151.99 s. Exact case repetitions and deterministic deletion
witnesses follow; this is an initial record, not a green verdict.

## Owed native verification

After the qualification run releases its native lock: Windows
`RepositoryMutationLeaseTests` once; the exact cancellation case 20 times alone
and 10 under build load. Do not run these while the qualification owns the lane.
