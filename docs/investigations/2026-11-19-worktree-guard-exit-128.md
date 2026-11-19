# 2026-11-19 — `test-apphost-main-worktree-guard.ps1` exit 128 in rc run a6278259

Debug task 5184298c. Base: master `95d43ee27da2272a34eca5ae51d6e5b98743df2a`.
Source note: `docs/investigations/2026-11-19-rc-run-a6278259-end-state.md` at
`94023c9910134aa51fc5dc2cf0c91500d86d0fe5` (branch `feat/card-task-6efbbc13`).

- RUN = `C:\Antiphon\releases\candidates\rc-2026-11-28-997f9b8d9ffc\logs\2026-11-18-1823-a6278259`
- CUT = `C:\Antiphon\releases\checkout-fc9a0547` (HEAD `997f9b8d9ffc4ba870d2fd56caf7c03781591624`)

## Verdict

This is a **checkout-state defect**. The commissioner cloned CUT without `core.longpaths=true`. Every
other release checkout on the desktop has it. Without it, Git for Windows cannot write one tracked
file into the guard script's temporary worktree, because the full path is over MAX_PATH. The
checkout fails, and `git worktree add` exits 128.

Two harness gaps let this happen and hid it:

1. **Test-script defect.** `scripts/test-apphost-main-worktree-guard.ps1:63` discards git's
   output, so the reason was not visible.
2. **Commissioning gap.** Rc runs use `-NoSync`. In that mode nothing sets or checks
   `core.longpaths`:
   - `nightly-run-impl.ps1:584-592` sets it only when `-not $NoSync`.
   - The commission preflight does not check it.

It is **not a product defect**, and the AppHost guard behaviour is not involved. The first 3
assertions passed, and the run failed at the setup step that creates the disposable worktree.

## Evidence

### Reproduction (one run, as authorised)

I made a copy of the master script, which is byte-identical to the CUT script (SHA256 compared).
The copy is in the task scratchpad (`guard-copy\test-apphost-main-worktree-guard.ps1`) and differs
in only two places:

- It uses `C:\Antiphon\releases\checkout-fc9a0547` as `$sourceRoot` and dot-sources that
  checkout's `scripts\apphost-common.ps1`.
- Line 63 keeps git's merged output and prints the path length, the exit code and the last 15 git
  lines.

Command, run from the CUT directory at 2026-11-19 10:38:10Z–10:38:23Z:

```
pwsh -NoProfile -NonInteractive -File <scratchpad>\guard-copy\test-apphost-main-worktree-guard.ps1
```

Output:

```
PASS source checkout classifier verifies
PASS main classifier verifies
PASS main classifier reports IsMainWorktree true
DIAG linkedRoot length=105 path=C:\Users\lndco\AppData\Local\Temp\antiphon-apphost-worktree-guard-7900aa19279f4876a89095241e71ad7b\linked
DIAG git worktree add exit=128 lines=101
...
DIAG git: Updating files: 100% (10990/10990), done.
DIAG git: fatal: Could not reset index file to revision 'HEAD'.
FAIL test setup or execution - git worktree add failed with exit 128
APPHOST MAIN-WORKTREE GUARD TESTS EXIT CODE: 1
```

This matches the rc failure:

- The same 3 PASS lines come first, followed by the same exit 128.
- The duration matches. My run took 13 s; the rc harness took about 12 s (the previous script log
  closed at 19:16:33Z and this one at 19:16:45Z). Both times are those of a full checkout of
  10,990 files that fails at the end.

The run left nothing behind:

- `git worktree list` shows only the main worktree.
- `.git\worktrees` is empty.
- No `antiphon-apphost-worktree-guard-*` directory is left under `%TEMP%`.
- `git status --porcelain` on CUT is empty.

I did not touch the AppHost, the runner, Postgres or `C:\src\Antiphon`.

**Limit of the capture:** my copy printed only the last 15 git lines. The per-file `error: ...
Filename too long` line was earlier in the 101 lines and was not kept. The path-length cause rests on
the arithmetic and the config comparison below. Both are exact, and the fatal line is the one Git
prints after a failed per-file write.

### Path length

- The temporary worktree root is `%TEMP%\antiphon-apphost-worktree-guard-<32 hex>\linked`. That is
  always 105 characters for `C:\Users\lndco\AppData\Local\Temp\`.
- The longest tracked path at 997f9b8d9 is 163 characters:
  `docs/investigations/2026-10-14-card-1259-live-transcript-ingestion-lags-the-cli-by-28-s-delivery-verifier-presses-enter-again-and-fails-at-30-s-desktop-evidence.md`.
  It was added in `efec74fd6` on 2026-10-14.
- 105 + 1 + 163 = **269 characters**, over MAX_PATH (260). The next longest path is 151 characters,
  which gives 257, under the limit. So exactly one file breaks the checkout.
- The CUT clone itself is fine: 38 + 163 = 201.

### Config comparison

Local `core.longpaths` in each release checkout:

| Checkout | `core.longpaths` | Reflog after clone |
|---|---|---|
| checkout-053aa2ea | true | clone, then checkout `<sha>` |
| checkout-4b599b91 | true | clone, then checkout `<sha>` |
| checkout-0637b75f | true | clone, then checkout `<sha>` |
| checkout-4025018e (run f2de5b30, guard PASS) | true | clone, then checkout `<sha>` |
| checkout-8049d727 | true | — |
| **checkout-fc9a0547 (run a6278259, guard FAIL)** | **unset** | clone only (2026-11-18 18:14:32Z, branch master) |

- The system config sets `core.symlinks`, `core.fscache` and `core.autocrlf`, but not
  `core.longpaths`. There is no global value. `C:\src\Antiphon` has no local value either.
- `git worktree add` uses the source repository's config, so a linked worktree of a checkout with
  `longpaths=true` can write the 269-character path, and one without it cannot.
- This explains the six earlier PASS runs, whose checkouts all had `longpaths=true`. It also
  explains why nothing in the script changed between the cut and master.
- The commissioning record `C:\Antiphon\releases\task-fc9a0547\preflight.ps1` is "adapted from
  task-4025018e". It checks HEAD, cleanliness, markers and alternates, but not `core.longpaths`. No
  clone command is recorded in the task directory. The clone step evidently omitted
  `-c core.longpaths=true`, which the earlier commissions used.

### Ruled out

- **Stale state:** no `.git\worktrees` entry, no `*.lock` under `.git`, and no leftover temp
  directory, both before and after the reproduction.
- **Branch or path collision:** the script uses `--detach` and a new GUID path.
- **Concurrent git activity on CUT:** reproduced on an idle desktop.
- **Product, guard or classifier code:** the 3 classifier assertions pass, and the failure comes
  before any guard entry point runs.

## Proposed fix (not implemented)

1. **Checkout state (orchestrator, exact path).** If `checkout-fc9a0547` is ever reused:
   ```
   git -C C:\Antiphon\releases\checkout-fc9a0547 config --local core.longpaths true
   ```
   For the next cut, the commission recipe must clone with
   `git clone -c core.longpaths=true <remote> C:\Antiphon\releases\checkout-<id>`, and the
   commission `preflight.ps1` must add a check:
   ```
   Check 'core.longpaths' ((git -C $rec.checkoutRoot config --local --get core.longpaths) -eq 'true') ''
   ```
2. **Code (test-script defect).** In `scripts/test-apphost-main-worktree-guard.ps1`:
   - Run `git -c core.longpaths=true -C $sourceRoot worktree add --detach ...`. The `-c` reaches the
     internal checkout through `GIT_CONFIG_PARAMETERS`, so the test no longer depends on the host
     checkout's config.
   - Capture the merged output and include its tail in the thrown message on a non-zero exit.
   - Make the same `-c` and capture change to `worktree remove` at `:106`.
3. **Code (structural, recommended).** On the `-NoSync` path, `scripts/qualification/launch-rc.ps1`
   or `nightly-run-impl.ps1` should refuse, with a named reason, a checkout whose local
   `core.longpaths` is not `true`. `docs/release-gates.md:558` currently promises long paths only
   for the bootstrap clone, not for the `-NoSync` hop.

## Rerun

After step 1 on the checkout under test, run this from that checkout, with no build:

```
pwsh -NoProfile -NonInteractive -File C:\Antiphon\releases\checkout-<id>\scripts\test-apphost-main-worktree-guard.ps1
```

Expect `APPHOST MAIN-WORKTREE GUARD TESTS EXIT CODE: 0`. The failure cannot be seeded (scripts
suite), so the next Full run on a correctly commissioned cut covers it.

next: code

--- next stage ---
next: code
handoff: Fix test-apphost-main-worktree-guard.ps1 (`-c core.longpaths=true` on worktree add and remove; surface git output on failure) and add a core.longpaths refusal for -NoSync rc checkouts (launch-rc.ps1 or nightly-run-impl.ps1), plus the commission preflight check. Orchestrator: clone the next rc checkout with `-c core.longpaths=true`.
artifact: docs/investigations/2026-11-19-worktree-guard-exit-128.md
