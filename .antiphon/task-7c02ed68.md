# Review 7c02ed68 — CARD-1617 review D-1, round 2 of 3 (diff-only)

Subject: feat/card-task-333e1bd2, range fc709c93804c6922251f05f99272cbdfee1d22b8..da8f1a9d0c896f8b7d34f8243f275dbcb7301a2d.
Verdict: CLEAN. No D-n. One advisory.

## (1) Behaviour identity of the S4b refactor (PtyAgentRunner.cs)

Compared the old `RootExitedForHangup` body with the new `RootExitedForHangup` -> `RootExited` path line by line:
- Order is unchanged. `_exitTcs.Task.IsCompleted` is evaluated first and returns true before any read. The reader is only a delegate (`ProcStatReader` builds `() => File.ReadAllText(path)`), so nothing is read when the exit was observed. The only new step is that `_conn`/`Pid` is now evaluated before the short-circuit. That is a plain field and property read: Porta `Pid` and `ModernConPtyConnection.Pid { get; }` are stored values, so nothing new can throw.
- With no pid, `readStat` is null and the result is false. Same as before.
- Exception mapping is the same filters around the same `File.ReadAllText` call. `FileNotFoundException` and `DirectoryNotFoundException` set stat to null, which means gone (true). `IOException` and `UnauthorizedAccessException` return false, so the check still fails closed. Any other exception propagates, as before.
- `ProcStatShowsExit` is untouched. It parses after the LAST `)`. `fields[0]` is the state (field 3: Z/X/x). `fields[6]` is the flags (field 9), tested with `& 0x4` for PF_EXITING.

## (2) Witness adequacy (scratch detached worktree at da8f1a9d0, one build-slot lease per run)

- M5 (the IO/permission catch returns true): `Card1681PtyHangupTests` ran 30 tests, 3 failed: `C1681_Root_unreadable_stat_is_not_exited(io)`, `(access)` and `C1681_Root_stat_reader_reads_real_files`. This matches the worker's 3/30.
- W (my choice): the call site passes a null reader (`RootExited(_exitTcs.Task.IsCompleted, null)`). This drops the /proc signal from the production hangup decision. I confirmed the mutant was compiled in: the UTF-16 literal `/stat` is absent from the host-test `Antiphon.Agents.Pty.dll` and present in the control build. Result: CP-3 `LinuxCustodyReleaseHostTests` ran 9, passed 9, so W SURVIVES. The pure `C1681_Root_*` cases call `RootExited` directly and cannot reach the call site.

## (3) Pins

- The Windows job-object span (from `var modern =` to `finally { _writeGate.Release(); }`) has sha256 2fd1ec493d80b8038c32994e9bcae1742057e369498ce7cc0a62db9d2c712e9a at both fc709c938 and da8f1a9d0.
- The diff touches only 4 files: PtyAgentRunner.cs, Card1681PtyHangupTests.cs, the amendment plan and the Code report. There is no csproj, pin or land-graph change.

## (4) Checkpoints and gates

Checkpoint run 20261103-095710-f077 (`start --plan docs/superpowers/plans/2026-11-03-card-1617-review-d1-amendment.md --after S4b`) at commit da8f1a9d0c896f8b7d34f8243f275dbcb7301a2d: state=clean, buildSource=verified, GREEN, exit 0, unlisted: none. `validate` returned SOURCE VALID with rows=3.

| CP | Filter | Executed | Passed | Failed | Skipped |
|---|---|---:|---:|---:|---:|
| CP-3 | `/*/*/LinuxCustodyReleaseHostTests/*` | 9 | 9 | 0 | 0 |
| CP-4 | `/*/*/(HostCustodyPlatformTests*)\|(CustodyReceiptBackendTests*)/*` | 6 | 6 | 0 | 0 |
| CP-7 | `/*/*/Card1681PtyHangupTests/*` | 30 | 30 | 0 | 0 |

- CP-8 is the S4a red-first row and was not re-run at S4a. The worker recorded it red: 30 executed, 12 failed, versus 10 expected. The plan explains the two extra failures: the read-count asserts in `C1681_Root_live_stat_is_not_exited` are red under the stub. CP-7 runs the same filter at the tip, and the M5 run reproduces the red.
- `check-evidence-diff`: bcfe77b3d..da8f1a9d0 has 9 commits and 0 violations; fc709c938..da8f1a9d0 has 3 commits and 0 violations.
- `merge-tree --write-tree` has no conflicts onto origin/master a2584ef6e30029cae4332aec469ada1e9e95c729, or onto origin/rc/2026-11-03 919d8d42f14d1b11ae5dcba8dafd1e1b2a8d90cd (tree a70ebaee2).

## Advisory

**A-1.** PC-19 / the W mutant: the production wiring of the /proc signal (`RootExitedForHangup` passes `ProcStatReader`) has no deterministic witness. W stayed green on CP-3 in one run, which fits the plan's "race-dependent" note. The Mutation stage should therefore expect PC-19's first half to survive. Closing it would need an instance-level seam, for example a test-visible reader factory or pid source. I am not filing this as D-n: production behaviour is correct, and the gap is a one-expression delegation.

## Scope note

ordinaryScopeCompleted=Full means the complete ordinary selection that this round-2 diff-only brief commissioned. That is every S4b row of the amendment's `### Checkpoints` table, plus the two mutants. It does not mean the whole Unit lane. Every PC (PC-15 to PC-19) stays pending for SourceLanding Mutation.

## Cleanup

- Removed the scratch worktree with `git worktree remove`.
- Deleted the bin/obj outputs my runs created, by literal paths.
- The checkpoint evidence under .antiphon/checkpoints/20261103-095710-f077 is kept. It is gitignored.
