# CARD-1617 S3 amendment: CARD-1681 Linux pty EIO end of output

Date: 2026-11-03. Code task f4830a56, caller decision (A) after the S2 report. Parent plan:
`docs/superpowers/plans/2026-11-03-card-1617-custody-kill-bound-plan.md` (unchanged). Card:
CARD-1681.

## Fact

On Linux the pty master read throws `IOException` "Input/output error" (errno 5, EIO) once the
last slave descriptor closes. `PtyAgentRunner.ReadStreamAsync` stored every `IOException` as
`_custodyReadFailure` when custody was set, so `DrainCustodyOutputAsync` threw and every Linux
explicit release of an empty tree returned `Unknown/observation_failed`. The parent plan's W3 and
W2's last leg were red at S1 for this reason.

## Change (S3)

- **S3a (red first).** `PtyAgentRunner.IsHangupAfterRootExit(IOException, bool rootExited)` and
  `ProcStatShowsExit(string?)` exist as inert stubs that return false. New tests:
  - `Antiphon.Agents.Pty.Tests` `Card1681PtyHangupTests` (pure, `Unit`):
    - EIO with the root exited is end of output;
    - EIO with the root alive is a failure;
    - other HResults, and an EIO-shaped `IOException` subclass, are failures;
    - `/proc/<pid>/stat` parsing: gone (null), `Z`, `X` or PF_EXITING (0x4) count as exited; a
      live state, a `)` inside comm, and short or garbage text do not.
  - `LinuxCustodyReleaseHostTests`:
    - `C1681_Eio_after_root_exit_drains_to_receipt`: red before the fix;
    - `C1681_Eio_while_root_alive_stays_drain_failure`: a live root closes its tty descriptors.
      It is a guard, green before the fix and red under an "every EIO is EOF" fix.
- **S3b (fix).** Only the `catch (IOException)` of `ReadStreamAsync` changes. On the Linux custody
  lane (`_custody` and `_containment` set), an EIO counts as end of output, flushed like a zero
  read, only when the root has exited. "Exited" means the runner observed the exit, or
  `/proc/<pid>/stat` shows the root gone, a zombie, dead, or flagged PF_EXITING. The kernel sets
  PF_EXITING in `do_exit` before `exit_files` closes the descriptors. Any other case keeps
  `_custodyReadFailure`.

  Untouched:
  - the process-tree emptiness check (`ReadActive`);
  - the rule that `Exited` needs an empty tree;
  - the CARD-1617 bound;
  - the Windows job-object branch;
  - non-custody reads.

### Checkpoints

Lane: Linux (server2). S3a rows run at the red commit and record RED. S3b rows run at the fix commit
and re-run the parent plan's CP-3 and CP-4 filters unchanged, with W2 and W3 unamended.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-5 | S3a | `tests/Antiphon.PtyHost.Tests -> bin-c1681-red/` | c1681-host-red | `/*/*/LinuxCustodyReleaseHostTests/(C1681_*)\|(C1617_Failed_termination_retries_on_capped_backoff*)\|(C1617_Bounded_release_still_finalizes*)` | C1681 host witnesses red-first; W2/W3 still red | 4 executed; 3 FAILED (C1681 after-exit, W2 last leg, W3) and C1681 while-alive green; record RED | 4 | 4 | false |
| CP-6 | S3a | `tests/Antiphon.Agents.Pty.Tests -> bin-c1681-unit-red/` | c1681-unit-red | `/*/*/Card1681PtyHangupTests/*` | classifier red-first | 15 executed; 5 FAILED (EIO after exit; stat null, Z, X, PF_EXITING); record RED | 15 | 5 | false |
| CP-3 | S3b | `tests/Antiphon.PtyHost.Tests -> bin-c1681/` | c1617-host-s3 | `/*/*/LinuxCustodyReleaseHostTests/*` | parent CP-3 at the S3 tip: W1-W4, C1614 x3, C1681 x2 | 9 executed, 0 failed, 0 skipped | 9 | 4 | false |
| CP-4 | S3b | CP-3 | custody-floor-s3 | `/*/*/(HostCustodyPlatformTests*)\|(CustodyReceiptBackendTests*)/*` | parent CP-4 floor at the S3 tip | 6 executed, 0 failed, 0 skipped | 6 | 1 | false |
| CP-7 | S3b | `tests/Antiphon.Agents.Pty.Tests -> bin-c1681-unit/` | c1681-unit | `/*/*/Card1681PtyHangupTests/*` | classifier green | 15 executed, 0 failed, 0 skipped | 15 | 5 | false |

Positive controls (pending, Mutation):

| PC | Mutant | Must turn red |
|---|---|---|
| PC-11 | Every EIO is end of output (ignore `rootExited`) | `C1681_Eio_while_root_alive_stays_drain_failure`, `C1681_Eio_while_root_alive_stays_failure` |
| PC-12 | Any `IOException` after root exit is end of output (ignore HResult/type) | `C1681_Other_read_errors_after_root_exit_stay_failures`, `C1681_Eio_shaped_subclass_stays_failure` |
| PC-13 | The catch keeps recording the failure (S3b reverted) | `C1681_Eio_after_root_exit_drains_to_receipt`, W2, W3 |
| PC-14 | `ProcStatShowsExit` ignores PF_EXITING, or parses the first `)` | the PF_EXITING and `)`-in-comm stat cases |

Windows owed (not run here): the parent plan's PtyCustodyTests and HostCustodyTests rows. The
`Card1681PtyHangupTests` class is pure and also runs on Windows in the Unit lane.
