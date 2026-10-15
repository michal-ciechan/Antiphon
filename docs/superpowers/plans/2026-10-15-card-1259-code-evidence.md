# CARD-1259 Code evidence

Original Code/landing owner: `eddab1c0-8f84-44bf-b222-fdeabf425004`.
Branch: `feat/card-task-eddab1c0`; worktree: `/work/worktrees/task-eddab1c0`.
Task base: `d1fc275773a1e04c73985771eecb387440c3eecd`.
Plan: `2026-10-15-card-1259-transcript-pump-decoupling-plan.md`.
Executable selection: `2026-10-15-card-1259-transcript-pump-verification.md`.

## S1

The four S0 cases now require the other session's entire ordered transcript and
marked task result while the first session's side effect remains held. Each case
owns a PostgreSQL clone and registers the real session state store.

The three V-2 variants use the real confirmation query barrier, stream-only
confirmation production, independent observer connections and controlled time.
The ready-marker fake emits the held payload and original copy. No production
implementation has changed. CP-1 is intended assertion-red, not passing evidence.

Source-reader audit repeated at the task base: searches of all test source for
the three filenames and read helpers find no source-reading consumers.
ScopeDriftTests and DelegationUnitTests contain inert path examples only.
Existing affected guard classes remain in CP-11 and CP-12.

Runner defaults/catalogue read at task start: revision 2, no kind overrides;
eligible Linux and Windows runners. No placement pin is required.

An isolated checkpoint-tool bootstrap is an explicitly named prerequisite build
outside the closed product-checkpoint table; it takes the host build slot.
Ordinary V/R and all 113 independent PCs remain pending. Mutation owns PC red/
restore/green after source landing. Restart after landing: server.

CP-1 ran at `93d4e7f1e2787b48c45e583ccf5bfd1c1bb98e50`, run
`20261015-173603-1482`. Fresh TRX contains all four S0 tuples and all three V-2
ingress variants: 7 executed, 0 passed, 7 expected progress-assertion failures,
0 skipped. Every V-2 case reached the actual confirmation query (hit=1,
baseline=3) before failing A/B persistence while its read remained held. No
fixture/build/cleanup exception replaced the assertions. The tool retained the
failed build output (`refused: still-present`); it exited 1 and joined the executor.
Source was clean and build provenance verified. Wall: 8m15s; build 368.016s,
startup 57.459s, tests 44.077s, teardown 4.439s. Bootstrap: 6s leased, 0 errors.

```text
CHECKPOINT CP-1 commit=93d4e7f1e2787b48c45e583ccf5bfd1c1bb98e50 build=ok filter=/*/Antiphon.Tests.Application/SessionRunnerEventPumpTests/(S0_blocked_hosted_pump_probe*)|(C1259_confirming_prompt_and_other_session_progress_before_confirmation*) executed=7 passed=0 failed=7 skipped=0 trx=/work/worktrees/task-eddab1c0/.antiphon/checkpoints/20261015-173603-1482/rows/CP-1/run.trx slot=granted waited=0s dirty=0 source=93d4e7f1e2787b48c45e583ccf5bfd1c1bb98e50 sourceState=clean buildSource=verified
```

S3 dependency identified in the existing call graph: CompactionRecoveryService
sets its in-memory handled marker and durable watermark before queue insertion,
then swallows enqueue failures. Durable action retry must repair this handoff too;
simply moving its existing callback to a worker cannot satisfy H0-H4/PC-99.

## Incomplete Code handoff

Only S1 is delivered. S2-S4 have not been implemented; the unregistered draft
schema/coordinator scaffold was removed rather than retained as production work.
No migration, production DI, queue semantics, timeout or assertion weakening is
part of this branch. This is not ready for Review or landing. There is no external
blocker or human policy decision being asserted: the assigned implementation was
not completed in this run. Continue Code on this branch from the pushed tip.

| Requirement | Actual outcome |
|---|---|
| V-1 | Four expected assertion-red S0 variants at CP-1; no green acceptance |
| V-2 | Three expected assertion-red variants (live/startup/subscription-ready) at CP-1; no green acceptance |
| V-3 | Not implemented or run |
| V-4 | Not implemented or run |
| V-5 | Not implemented or run |
| V-6 | Not implemented or run |
| V-7 | Not implemented or run |
| V-8 | Not implemented or run |
| V-9 | Not implemented or run |
| V-10 | Not implemented or run |
| V-11 | Not implemented or run |
| V-12 | Not implemented or run |
| R-1 | Not run |
| R-2 | Not run |
| R-3 | Not run |
| R-4 | Not run; required stale-exit notification/consumption assertions still to add |
| R-5 | Not run; asynchronous completion fixture adaptations still pending |
| R-6 | Not implemented or run; retain C561 and add both restart payload variants |

CP-2 and CP-3 contract groups are pending. CP-4 through CP-13 final qualification
are pending at a single clean implementation SHA. No whole Unit/assembly run was
performed: the executable manifest explicitly excludes it. No required manual
acceptance or final call-graph closure is claimed. All **PC-1 through PC-113,
including every manifest variant and H0-H4 cut**, remain pending SourceLanding
Mutation; new-code mutation sites remain unbound. No deliberate mutant ran.

The CP-1 build kept the exact closed filter and used the committed S1 SHA as
expected source. It ran once, with no repeated proof. The only unlisted build
was the reported, slot-gated checkpoint-tool bootstrap. Source-reader audit
covered all test projects and found no extra production-source reader class.

Next work: preserve this seven-case baseline, implement the durable action
contracts and CP-2 tests, then the bound routing/review/projection contracts and
CP-3 tests. Retain the compaction enqueue obligation at H0/H1 and the queue's
exact existing verification policy. Complete S2-S4 and run CP-4..CP-13 before
commissioning ordinary Review; all PCs stay with SourceLanding Mutation.

## Receipt and cleanup qualification

`validate-checkpoint-receipt.ps1` against CP-1's untouched `report.json` and
tested SHA returned exit 2, `CHECKPOINT SOURCE INVALID reason=cleanup_refused`.
This remains an invalid qualification receipt, not Clean/Full evidence. Its
actual seven assertion failures and clean-source/build-provenance observations
above remain historical facts. Manual output cleanup does not rewrite that
receipt or promote its verdict.

After terminal executor exit and inspection of the exact owned roots, removed
`tests/Antiphon.Tests/bin-c1259-red` (2,606 entries) and
`tools/Antiphon.Checkpoints/bin-c1259-tool` (49 entries), with containment and
link checks. The ignored checkpoint run, logs and TRX remain in place. No
process or worker is left running by this task.

The evidence history guard passed over task base through report commit
`980624fee2cb4ac0c82b7a3af0c16f7b92d668da`: two commits, zero violations.
It is rerun through the final pushed report SHA before settlement.
`git fetch origin master` followed by `git merge-tree --write-tree origin/master
HEAD` returned exit 0 with tree `dbe9ea871bbc3223fd54b90b339556273ddc25f0`;
the fetched target was `e03508908` (abbreviated observation, not a tested SHA).
Merge feasibility is not acceptance or permission to land these red tests.
