# Review a753db19: CARD-0824 S1 survivor witnesses (round 1 of 3)

Verdict: clean. Subject `feat/card-task-a9f45276`, tested SHA `ae5f2550ff596ac744646567cb60e0826947e202`
(tests e05a82d68; the tip babc58867 adds only the Code report). Landing owner: Code task a9f45276.

1. Production unchanged: `git diff --stat 2cad8a419..ae5f2550f` touches only 4 files under
   `tests/Antiphon.SessionRunner.Tests/` and the plan doc. The only extra file at the tip is `.antiphon/task-a9f45276.md`.
2. Not vacuous: I applied my own env-gated mutants (exact-path edits of RunnerWorkspaceSlotPool.cs,
   PhoneHomeCommandDispatcher.cs and RunnerWorkspaceRetention.cs). There was one native build. Each run had its own
   build-slot lease. Gates off: 11/11 green. Mutant results:
   - X37: 3/3 red at `removes should be empty`.
   - X11: 1/1 red at Outcome `should be "quarantined"`.
   - X31: 3/3 red at `should throw`.
   - X06: 1/1 red at `settled should be null`.
   - X17: 1/1 red at `b.Path should not be`.
   - PC-74: 1/1 red at `entered.Task.IsCompleted`.
   - X47: 1/1 red at `response.Verdict`.
   I restored the files with `git checkout --`, and `grep RVW_M src server tests` finds nothing.
3. Temp roots: everything runs under `Path.GetTempPath()/c0824-ws-<guid>` (C0824SlotScratch) and
   `c1677-ret-<guid>` (C1677RetentionScratch). Each git call, from the test or the service, runs with
   WorkingDirectory set to a path inside that root. The PC-74 dispatcher's store-id, capacity state and AllowedCwd
   are also inside it. The runs left no residue in /tmp.
4. PC-74 can't flake red on correct code. While the gate is closed, the lock blocks every asserted condition, so
   load can only hide the mutant. After the window, the lease took about 0.6 s; the bounds are 5 s and 8 s inside
   the class's 10 s Timeout. The test took 2.6 s, about the same as its slowest sibling.
5. Checkpoint run `20261103-224954-87a8` passed 9/9 rows and 128/128 tests:
   - CP-1 19
   - CP-2 12
   - CP-3 18
   - CP-4 50
   - CP-5 10
   - CP-6 12
   - CP-7 3
   - CP-8 1
   - CP-9 3

   Every row has 0 failed and 0 skipped, buildSource=verified, sourceState=clean, `unlisted: none`. `validate`
   printed `CHECKPOINT SOURCE VALID source=ae5f2550... rows=9`. The TRX holds all 11 new cases as Passed.
   check-evidence-diff over 2cad8a419..ae5f2550f found 2 commits, 0 entries and 0 violations. Over ..babc58867 it
   found 3 commits, 1 entry and 0 violations. `merge-tree` onto origin/master 97e77f9f1 is clean.

Unlisted runs, with reasons: one native build of the checkpoint tool, to run it. One native gated mutant build of
SessionRunner.Tests. Eight `--no-build` runs: the baseline and seven mutants. All of them went through
scripts/build-slot.ps1 with one lease each.

Not run: the Unit lane. The brief scopes this round as Interim, named rows only, which the operator accepted for
master-only test work. The profile header says Final, but the Unit lane is multi-hour on server2 and does not fit
the 60-minute budget. The diff touches no Unit-lane class or production code. Every PC stays pending for
method-scoped SourceLanding Mutation.

Advisories: none.
