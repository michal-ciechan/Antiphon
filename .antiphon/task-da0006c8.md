# Code da0006c8 — CARD-1690 review fix (Review d99c29ee D-1, A-1)

Outcome: D-1 is fixed. A poll error's message is now a lazy, cached, configurable getter that calls
the original printer. A failing wait's final message is byte-identical to base in all 21 probe
cases, and the RoutingSettingsTab print count matches the lossy helper's (0 vs 1,204 at base).
Tested source: e1094e501db692d9232ea6d265c0e26dfb64b5f6. Branch: feat/card-task-da0006c8, based on 86f3984458e66e9b26ad9f1702a00c65f4aefb83.

## Suite duration and benefit
- **Full client suite at e1094e501** (scripts/test-client.ps1 under build-slot.ps1; slot waited=60s, held=299s):
  - typecheck 0, lint 0.
  - 129 files: 1856 passed, 1 skipped, 0 failed. That is the 1853 from before plus 3 new.
  - Duration 282.56 s, with server2 load average 10–24 during the run.
- **Wall-clock is not comparable.** The host load varied too much for these numbers to compare the helpers.
  - Same RoutingSettingsTab file (191 tests), one run each: lazy 172.8 s and 183.0 s, lossy 257.1 s and 249.8 s, base 218.4 s.
  - Load average across those runs: 8–24.
  - CPU (user+sys, process tree, including typecheck and lint): lazy 240.7 s at load ~10, lossy 303.7 s at load 24, base 281.1 s at load 15.
  - I am not claiming a seconds saving from these. The full suite was run once (282.56 s). Your reference numbers (lossy 235.8 s, base 274.1 s) came from a different load.
- **Deterministic proxy: printer calls in RoutingSettingsTab.** A scratch counter (not committed) wrapped the default getElementError in setup.ts. One run per mode:

  | Mode | Prints | Characters | In-process print time |
  |---|---|---|---|
  | base (no helper) | 1,204 | 8,982,880 | 32.9 s |
  | lossy helper (86f398445 bytes) | 0 | 0 | 0 |
  | lazy helper (this fix) | 0 | 0 | 0 |

  The print time is load-inflated. The lazy getter avoids every print the lossy one avoided, because no poll error in that file is ever read. The saving is kept.

## Message-identity evidence (scratch, not committed)
- **How it was run:** one scratch test, 21 cases, run twice with the same lockfile.
  - "base": setup.ts with `installPollingElementError()` disabled. That setup.ts matches master's (git diff e56e4d1b6..HEAD -- setup.ts is only that install).
  - "tip": e1094e501.
  - Each run wrote {message, name, stack head} per case to JSON.
- **Result:** both JSON files are byte-identical, sha256 9d7733ea5b21fe7b4fc571fc4cd4d79ef1cbf6feaf7fb4687a9823713e344257. No timing normalisation was needed.
- **Cases:**
  - waitFor/getByText timeout and findByText timeout
  - several matches: waitFor, findByText, findByRole, and getByText outside a poll
  - within(portal) after 1,000 rows: waitFor, findBy, several matches
  - fake timers advanced inside the callback
  - getByRole in waitFor, and findAllByRole
  - expect failure, custom error, async callback
  - showOriginalStackTrace, and a custom onTimeout
  - getByText outside waitFor
  - waitForElementToBeRemoved
  - false-then-true for waitFor and for findBy (both pass)
- The probe source is kept at scratchpad zzScratchC1690.test.tsx.txt and was removed from the tree.

## Commits (pushed)
1. **092d066203c7cf7a5c79c8bbf3ac2fc5a34eb6a2** `test(client): pin the base failure messages of failing waits (red)`
   - client/src/test/setupGuards.test.tsx: the two lossy expectations are reverted to the base shape.
     - A read poll error carries its dump, taken from the DOM at read time; this proves the global install is lazy.
     - A timed-out waitFor prints 2 dumps.
   - Three new cases:
     - several matches inside waitFor list both elements;
     - within(portal dialog) after 1,000 rows prints the dialog;
     - fake timers advanced in the callback keep the DOM.
   - **Red at 86f398445: 5 failed / 3 passed.** All 5 failed on the intended assertion:
     - `+0 to be 1`
     - `1 to be 2`
     - `+0 to be 2`
     - `...to contain 'dialog-only-marker'`
     - `+0 to be 1`
2. **e1094e501db692d9232ea6d265c0e26dfb64b5f6** `fix(client): print a waitFor poll error lazily, when its message is read`
   - client/src/test/pollingElementError.ts: the poll error is `new Error()` named TestingLibraryElementError.
   - Its `message` getter is `printed ??= printing(message, container).message`, with the container passed through.
   - The getter is configurable, non-enumerable, and has a setter that reverts it to a data property.
   - The V8 stack header is formatted lazily too, so the stack heads also match base.
   - **setupGuards 8/8 green.**
3. This report commit.

## Advisory A-1 (honest correction)
- The earlier Code report's line "No timeout was widened" was wrong.
- In RoutingSettingsTab.test.tsx, `C1270_D1_READ_WAIT_MS` raised the three C1270 D-1 waitFor bounds from 1 s to 6 s (lines 727/752/780). The value is 2 × the loaded 2,885 ms, rounded up.
- That justification comment is kept unchanged. This task widened no timeout and loosened no assertion.

## Other checks
- **check-evidence-diff:** 86f398445..e1094e501 gave commits=2 entries=0 violations=0. f32aed42e..e1094e501 gave commits=10 entries=0 violations=0.
- **Rebase:** origin/master is now c2f1f83a3. `git merge-tree --write-tree origin/master HEAD` conflicts (exit 1), as before.
  - In a scratch detached worktree, `git rebase origin/master` skipped e20198949 and 75a2fe01b (already applied) and replayed 8 commits cleanly.
  - Its diff is byte-identical to 75a2fe01b..e1094e501.
  - **A rebase land is required.** The scratch worktree has been removed.
- **Not run:** the .NET Unit lane and the integration classes, because no .NET source changed. There is no checkpoint plan, so there are no CP, V or R rows.
- **PCs:** none are declared. These remain pending for Mutation:
  - mutate the getter to non-configurable;
  - drop the container argument;
  - drop the cache;
  - return the eager print during a poll.
- **Cleanup:**
  - Scratch edits to setup.ts and pollingElementError.ts were restored with git checkout after every run.
  - logs/client-tests.log and logs/client-typecheck.log were deleted.
  - client/node_modules (installed by test-client.ps1) and logs/client-npm-ci.lock remain.
