# CARD-1704 C: shared-repository landing design and risks

Investigate angle C (design), task c7f02607, base master `1470340559889c2985ea722173181ea25c51b47c`,
2026-11-04. Read-only: no product code changed. Sibling angles A (inventory, task 1cf13574) and
B (LibGit2Sharp capability, task 4a75ce55) had pushed nothing past the base when this was
written (`git ls-remote` of both branches = `1470340559…`). Their findings are therefore
reasoned from knowledge here and marked **[B to confirm]** / **[A to confirm]** where they gate
a design decision.

## 1. Verdict

A shared in-process repository handle for the land is feasible, but **only as a hybrid**, and the
win is narrower than "replace git.exe":

- The land already has the right hook. `ILandingGit.BeginOperationScope()` opens an
  `AsyncLocal` scope per repository-lease episode (`server/Infrastructure/Git/LandingGit.cs:639-648`,
  owned by `LandRepositoryLeaseScope`, `server/Application/Services/PreparedLandingVerification.cs:14`).
  The libgit2 session belongs to that scope: open on lease acquire, dispose on lease release
  (including the verification-window release) and reopen on reacquire.
- The seam is **under** `ILandingGit`, not a replacement of it. 61 test doubles subclass or
  implement `ILandingGit`/`LandingGit` (`grep` count over `tests/` and `server/`), and the protocol
  calls `RunAsync(argv)` directly for status, rev-parse, merge-base, diff and the rebase. The
  existing precedent (CARD-1432 G1 `ReadRefsAsync`, a default interface method that reproduces
  the old argv shape, `server/Application/Interfaces/ILandingGit.cs:65-76`) is the migration pattern.
- **libgit2 takes reads and local ref writes only.** Network (ls-remote, fetch, push,
  `--force-with-lease`), the rebase, worktree add/lock/remove, `reset --hard`/`clean -fdx` of the
  land worktree, `git cherry` and the canonical `merge --ff-only` stay on the CLI. Every one of
  those either has semantics libgit2 does not reproduce (patch-id skip in rebase, promisor/partial
  clone, credentials, hooks) or needs a killable process (the 5-minute budget).
- Measured on server2 (Linux, load 19-29, 24 cores): a trivial `git rev-parse` spawn costs
  ~6.6 ms; `git status --porcelain -z --untracked-files=no` ~137 ms; `git worktree list --porcelain -z`
  (369 worktrees) ~120 ms. The status and worktree-list costs are work, not spawn, and would not
  shrink in libgit2. The win is the **count** of small reads (the E2E fixture comment puts a native
  land at ~740 spawns, `tests/Antiphon.E2E/Fixtures/LandDeliveryOptions.cs:269`) multiplied by the
  **Windows** spawn cost (80-200 ms under load per the same comment). Expect the gain on the
  desktop and little on Linux.

next: plan (the root question is design, not a bug; the design below is concrete enough to plan
S0 and S1, with the gates for S2+ listed as open questions).

## 2. Evidence: the land as it runs today

### 2.1 Flow and ownership (citations on base 14703405)

| Step | Where | Git shape |
|---|---|---|
| Admission: yield to dispatch, then execution lease L (`<common>/antiphon/land-execution.lock`), then repository mutation lease R (`<common>/antiphon/landing.lock`, exclusive `FileStream`) | `AgentTaskLandService.cs:511-530`; `LandExecutionLease.cs:7-21`; `RepositoryMutationLease.cs:45-60` | `rev-parse --git-common-dir` (cached in scope) |
| Read scope opens with R | `PreparedLandingVerification.cs:14-18` | `BeginOperationScope()` |
| Source resolution | `AgentTaskLandSourceResolver.cs` | ls-remote + fetch to `refs/antiphon/land/...` pins, `merge-base`, `cherry`, `reset` |
| Operation create / inspect | `LandOperationFactory.cs`, `LandingGit.InspectAsync` (`LandingGit.cs:747`) | rev-parse, show-ref, status, ls-files |
| Pins | `AgentTaskLandingProtocol.cs:232-242`, `LandingGit.PinAsync` (`:1457`) | `update-ref` under `refs/antiphon/land/<op>/...` |
| Prepare land worktree | `LandWorkspace.cs:50,92-94,107` | `worktree add --detach --force --force`, `reset --hard`, `clean -fdx`, `worktree lock` |
| Rebase | `AgentTaskLandingProtocol.cs:269-290` | `-c rebase.autoStash=false -c rebase.updateRefs=false rebase <target-before>`; on failure `diff --name-only --diff-filter=U -z`, `rebase --abort` |
| Verification decision | `LandVerificationPolicy.cs:21-25` | `diff-tree --raw -r -z`, `ls-tree -r -z` |
| Verification window: **R released, L kept**, scope disposed, verifier runs (build), R reacquired, new scope | `AgentTaskLandService.cs:710-746` | none during the build |
| Push CAS: observe, push, re-observe | `AgentTaskLandingProtocol.cs:343-375`; `LandingGit.cs:1247-1278,1504-1511` | ls-remote, fetch into a unique pin, rev-parse, merge-base, plain `push <endpoint> <sha>:<ref>` |
| Canonical advance | `AgentTaskLandingProtocol.cs:475-545` | `update-ref --no-deref` or `-c merge.autoStash=false merge --ff-only`, status |
| Cleanup | `AgentTaskLandingProtocol.cs:421-470` → `WorktreeManager`/`GuardedWorktreeRemoval` | show-ref `--exists`, symbolic-ref, status, worktree registration rename, `push --force-with-lease=<ref>:<sha> :<ref>` (`LandingGit.cs:1532-1548`) |
| Ref release | `AgentTaskLandService.cs:565-575`, `LandRefRetention.cs:31` | `for-each-ref`, `update-ref --stdin` |
| Profile line | `AgentTaskLandService.cs:540-556` | counts processes, worktree lists, hits, remote round trips, git seconds |

A land touches **four** working directories that share one common directory: the canonical
checkout (`task.RepoPath`), the per-repository land worktree, the task's source worktree, and the
common directory itself for ref writes. A "single repository handle" is therefore really a session
holding **one handle per working directory, keyed by canonical path, sharing a common dir**.

### 2.2 Facts that constrain the design

1. **Every mutating child is journalled and fenced.** `MutateAsync`/`OwnedAsync`
   (`AgentTaskLandingProtocol.cs:912-935`) persists `ChildOperation`, then the child's pid and start
   ticks via the `started` callback. On re-entry a set `ChildOperation` without a dead pid refuses
   `interrupted_process_requires_inspection` (`:101-110`). `LandingGit.IsMutating`
   (`LandingGit.cs:508`) writes a `RepositoryChildJournal` record before `Process.Start`; the lease
   fences on unfinished children. In-process work has no child.
2. **Budget is a kill.** `LandingGit.DefaultBudget` = 5 min, then a process-tree kill and a
   2 s drain (`LandingGit.cs:15-25,121-200`). An in-process libgit2 call cannot be killed; only
   network transfer callbacks can be cancelled.
3. **Environment.** Every child runs with `GIT_TERMINAL_PROMPT=0`, `GIT_OPTIONAL_LOCKS=0`
   (`LandingGit.cs:132-133,1084-1085`). The server image runs git as uid 1654 with a pinned git
   (floor 2.43 for `show-ref --exists`, `docker/git-minimum-version.sh`); the desktop uses Git for
   Windows. Hosts set identity through `GIT_CONFIG_GLOBAL` (e.g.
   `docs/investigations/2026-09-26-card-0730-reply-class-failures.md:23`).
4. **Exit codes, not stderr, carry the reason codes.** The typed methods map exit 0/1/2/128 and
   parsed stdout into stable codes (`remote_read_failed`, `source_remote_missing` on ls-remote exit 2,
   `remote_ancestry_error` on merge-base exit ∉ {0,1}, `tracking_absent` on show-ref `--exists` 2;
   `LandingGit.cs:1247-1278,1335-1600`). Untyped failures become
   `LandingGitCommandException(template, exit, "git_exit_<n>")` with a command template from
   `LandFailureDiagnostic.CommandTemplate` (`LandingGit.cs:1627-1637`;
   `LandFailureDiagnostic.cs:149-183`), which surfaces in `LandInspectionDiagnostic`.
5. **The read cache is already scoped to R.** `OperationScope` caches common dir, canonical paths
   and worktree registrations, invalidated by any command `ChangesRegistrations` does not know as
   read-only (`LandingGit.cs:606-637`), and `LiveRegistrationsAsync` always re-lists (CARD-0642 R1).
   The scope ends **before** R is released for the verification window
   (`AgentTaskLandService.cs:723`, "End the cache scope before closing the real R handle").
6. **Other processes write the same common directory with no lease**: agents committing in their
   worktrees, the operator, `git gc --auto`. R serialises Antiphon's own mutators only.
7. **The server2 runner mirror is a blob:none partial clone.** `/work/repos/antiphon/.git/config`:
   `core.repositoryformatversion=1`, `remote.origin.promisor=true`,
   `remote.origin.partialclonefilter=blob:none`; 37 `*.promisor` packs. Today
   `git rev-list --objects --missing=print HEAD` lists 0 missing of 111,437 objects, so master's
   history happens to be complete, but any blob not yet faulted in is fetched lazily by git and
   **not** by libgit2. The desktop repository's shape was not inspected (not reachable from here).
8. **Repository scale on server2:** 5,862 refs, 369 worktrees, 39 packs (181,969 packed objects,
   ~230 MB), 4,085 loose objects, 7 `tmp_*` garbage pack files.
9. **Prior decision on record:** `GitWorkspaceService.cs:10-14` "Shells out (no LibGit2) so
   behaviour matches whatever git the machine has"; the onion rule confines libgit2sharp to
   `Infrastructure/` (`docs/features/001-kanban-agent-orchestration/01-requirements.md:47`).
10. **E2E progress accounting counts files.** `EvidenceGit` (a `LandingGit` subclass) overrides
    `RunAsync`/`RunOwnedAsync` and writes one `protocol-git-<guid>.json` per completed command
    (`LandDeliveryOptions.cs:294-300`); `ProtocolProgress()` counts those plus
    `protocol-verify-*.json` from `LandVerifierEvidence` (`LandDeliveryFixture.cs:372-374`;
    `LandVerifierEvidence.cs:17-77`). Any git work that bypasses `RunAsync` is invisible to the
    receipt quiet window, exactly the defect 147034055 fixed for the verifier.

## 3. Proposed design

### 3.1 Seam shape

Three layers, all in `server/Application/Interfaces` (contracts) and `server/Infrastructure/Git`
(implementations):

```
ILandingGit                    (unchanged land-facing API; 61 doubles keep compiling)
  └─ typed read methods added as default interface methods that reproduce today's argv
     (StatusAsync, ResolveCommitAsync, IsAncestorAsync, ShowRefExistsAsync, ListTreeAsync,
      DiffTreeRawAsync, ReadConfigAsync, ForEachRefAsync, UpdateRefsAsync already exists)
LandingGit : ILandingGit       (CLI; owns the process/journal machinery; the reference oracle)
  └─ consults IGitSession? Session  (AsyncLocal, set by BeginOperationScope when enabled)
IGitSession : IDisposable      (one per R episode; one IGitRepositoryHandle per canonical workdir)
  ├─ CliGitSession             (null object: every call returns NotHandled → CLI path)
  └─ LibGit2Session            (LibGit2Sharp; handles opened lazily per path)
```

`IGitSession` is deliberately narrow and typed; it never accepts argv:

```csharp
public interface IGitSession : IDisposable
{
    GitBackendKind Kind { get; }                       // Cli | LibGit2 | Compare
    // Each returns GitAnswer<T>: Handled(value) | Refused(code, synthExit) | NotHandled(reason)
    GitAnswer<string> ResolveCommit(string workdir, string revision);
    GitAnswer<bool>   IsAncestor(string workdir, string ancestor, string descendant);
    GitAnswer<RefRead> ShowRef(string workdir, string fullRef);         // Exists|Missing|Error
    GitAnswer<IReadOnlyDictionary<string,string>> ReadRefs(string workdir, IReadOnlyList<string> refs);
    GitAnswer<StatusEntries> Status(string workdir, StatusShape shape); // porcelain-v1 -z bytes
    GitAnswer<byte[]> ListTreeZ(string workdir, string treeish);
    GitAnswer<byte[]> DiffTreeRawZ(string workdir, string a, string b);
    GitAnswer<string?> ConfigGet(string workdir, string key);
    GitAnswer<RefWriteResult> UpdateRef(string workdir, string fullRef, string newOid, string? oldOid); // S2
    void Release(string workdir);                     // close the handle before a delete (3.3)
}
```

- **Sync inside, async outside.** libgit2 calls are synchronous and not cancellable. `LandingGit`
  calls the session on the land's own async flow (no `Task.Run` fan-out), checks `ct` before and
  after each call, and records the elapsed time in `LandingGitProfile` exactly as a process is
  recorded today (`LandingGit.cs:109-119`), with a new `inProcess=` counter beside `processes=`.
  Network operations never enter the session, so the only unbounded in-process call would be a
  pathological local walk; S1 bounds history walks by refusing (NotHandled) when a merge-base walk
  exceeds a commit count, falling back to the killable CLI.
- **`NotHandled` is the hybrid rule's single mechanism.** `LandingGit`'s typed method asks the
  session first; `NotHandled` (unsupported op, unsupported repository shape, libgit2 exception)
  falls through to the unchanged CLI code. The fallback is per call, logged once per scope with
  the reason, and counted in the profile (`fallbacks=`).
- **No argv parsing in the session.** `RunAsync(argv)` stays CLI forever. Callers that today pass
  argv for a read move to a typed method in S1 (default interface method = old argv), so a fake
  that only implements `RunAsync` keeps seeing identical command shapes.

### 3.2 Error model and stable reason codes

- The session returns values, never throws, for expected outcomes. `RefRead.Missing` maps to the
  same branch that exit 2 of `show-ref --exists` takes today; `IsAncestor=false` maps to exit 1;
  any `LibGit2SharpException` maps to `NotHandled("libgit2:<ErrorCategory>")` and the call is
  re-run on the CLI. **A libgit2 failure is never a reason code**; only the CLI's verdict is.
  That keeps every published code (`remote_ancestry_error`, `tracking_ref_query_error`, …)
  stable by construction and keeps "errors are never absence" (`ILandingGit.cs:17`) intact:
  libgit2 `NotFoundException` on a ref is `Missing` only for the ref-read methods whose CLI twin
  distinguishes missing (exit 2) from error (exit 1/128); everywhere else it is `NotHandled`.
- `Refused(code, synthExit)` exists only for S2 writes whose CLI twin has a deterministic
  non-zero exit (e.g. `update-ref` old-value mismatch = 1, `tracking_ref_moved`). The synthetic
  exit feeds the same `LandingGitResult(ExitCode, Output, Diagnostic)` and the same
  `LandFailureDiagnostic.CommandTemplate` (a fixed template per typed method, e.g.
  `"update-ref <ref> <new> <old>"`), so `LandInspectionDiagnostic` rows look the same.
- Output bytes: methods that today return `-z` output (status, ls-tree, diff-tree) return the
  **same bytes** the CLI would print, built by a formatter in Infrastructure. The parsers stay
  where they are; the contract suite (3.7) compares bytes.

### 3.3 Lifetime and ownership of the shared handle

- **Owner:** `LandRepositoryLeaseScope` via `ILandingGit.BeginOperationScope()`. Opening the scope
  creates the session (lazily: no handle until the first typed call); `ReleaseAsync()` disposes the
  session **before** R's `FileStream` closes (same ordering as the cache today,
  `PreparedLandingVerification.cs:33-44`). `BeginReadScope()` after reacquire opens a fresh session.
  Nothing libgit2 caches (refdb, packed-refs snapshot, odb pack list, index) survives a period in
  which R was not held.
- **Nested/borrowed scopes** (`BorrowedScope`, `LandingGit.cs:679-683`) borrow the live session;
  only the outer scope disposes it.
- **Refresh inside an episode.** Every CLI mutation in the same scope (rebase, reset, worktree
  add, push, fetch) and every `ChangesRegistrations` hit invalidates the session's handle for the
  affected workdir (and `topology` drops all handles), mirroring `OperationScope.Invalidate`. The
  handle is reopened on next use; a fetch into a pin calls `Refs` reload/odb refresh rather than
  trusting a pack list taken before the fetch. External writers (fact 6) are why the session never
  caches ref **values**, only the open handle.
- **Disposal before deletes (Windows).** A handle on a linked worktree can hold files under
  `<common>/worktrees/<name>/` and pack files under `<common>/objects/pack/`. libgit2's Windows
  share mode defaults to read|write without delete **[B to confirm: `GIT_OPT_SET_WINDOWS_SHAREMODE`
  default and whether LibGit2Sharp exposes it]**. Rules: (a) cleanup calls `session.Release(path)`
  for the source worktree and the land worktree before `WorktreeManager.TryRemoveAsync`/
  `UnregisterWorktreeAsync`; (b) `LandWorkspace` reset/clean is CLI and is preceded by
  `Release(landWorktree)`; (c) the session is per R episode, so a pack mmap is never held across a
  verification build or between lands, which bounds the window in which an agent's `git gc` on
  Windows meets "Unlink of file … failed. Should I try again?". The Windows lock diagnostics
  (`WindowsWorktreeLockDiagnostics.cs`, `WorktreeHandleCsv.cs`) will now name the **server**
  process as a holder if (a) is missed; S3 adds a test that asserts no server-owned handle under
  the deleted path.

### 3.4 Thread-safety

- A land runs its protocol sequentially on one async flow (the single global land channel reader,
  `docs/orchestration-loop.md` §5); no `Task.WhenAll` over git calls exists in the land services
  (only stream pumps inside `LandingGit` and the land verifier child, `AgentTaskLandService.cs:1998-2002`). The session is
  reachable only through the `AsyncLocal` scope, so the dispatcher, gated commits,
  `TaskProgressGit` and the monitors (same singleton `ILandingGit`, `Program.cs:471`) never see it
  and keep the CLI.
- libgit2 is safe for distinct `Repository` objects on distinct threads, not for concurrent use of
  one object. The session takes a private lock per handle so an accidental concurrent call
  serialises instead of corrupting; a contention counter in the profile makes that visible.
- Async continuations hop threads; LibGit2Sharp objects are not thread-affine, so that is fine
  **[B to confirm on the bundled libgit2 build: `GIT_THREADS` enabled]**.

### 3.5 Repository lease interaction

- R remains the only authority for Antiphon mutators; the session is **not** a lock and never
  substitutes for one (fact 6).
- The child journal (fact 1) protects R's fence against a surviving git child. An in-process write
  cannot outlive the server, so S2 writes record no journal entry, but they **must not** leave
  `ChildOperation` set: `OwnedAsync` would read the server's own pid as alive on a same-process
  retry and refuse `interrupted_process_requires_inspection` until restart. S2 therefore routes
  in-process writes through a separate `InProcessAsync` that records
  `ChildOperation="inproc:<op>"` with `ChildProcessId = null` and clears it in `finally`; on
  re-entry an `inproc:` marker with no live owner is treated as "died with the process" and the
  operation proceeds to its normal state re-observation (ref values are re-read, never assumed).
- A crash mid-write can leave `refs/...lock` or `index.lock` behind exactly as a killed git can.
  `GitIndexLock`'s census looks for git **processes** (`GitIndexLock.cs:131`), so a lock held by an
  in-process write in this server is classified like an orphan. Since S2 writes only refs (never
  the index; reset/rebase/merge stay CLI) the index-lock hold/refusal paths do not change; ref
  `.lock` residue is reported as the write's `Refused` and falls back to the CLI, which reports it
  in its own words.

### 3.6 Hybrid rule (what stays on the CLI)

| Operation | Backend | Why |
|---|---|---|
| rev-parse `--verify X^{commit}`, show-ref `--verify/--exists`, ReadRefs, symbolic-ref read, config get, check-ref-format | libgit2 (S1) | high count, pure reads; check-ref-format already memoised (`LandingGit.cs:70-104`) |
| merge-base `--is-ancestor` | libgit2 (S1), walk-bounded | high count; unbounded walks fall back |
| status `--porcelain=v1 -z --untracked-files=no` (land/source/canonical) | libgit2 **only after** the S1 benchmark shows a win | work-bound (~137 ms on server2), libgit2 lacks untracked cache/fsmonitor; must not write the index (`GIT_OPTIONAL_LOCKS=0` today) |
| status `--untracked-files=all --ignore-submodules=none` (canonical decision) | CLI | ignore/submodule rules are the riskiest equivalence |
| ls-tree `-r -z`, diff-tree `--raw -r -z --no-abbrev` (verification policy) | libgit2 (S1) | byte-for-byte formatter; partial clone needs no blob for these |
| worktree list `--porcelain -z` | CLI (keep cache) | ~120 ms is work; libgit2's worktree API does not emit `locked`/`prunable` lines in git's format |
| update-ref single ref with old value; `update-ref --stdin` delete batch under `refs/antiphon/` | libgit2 (S2) | reflog and transaction semantics must match (3.7) |
| ls-remote, fetch, push, `--force-with-lease`, origin delete | CLI | credentials (`core.sshCommand` in `/etc/gitconfig`, https helpers), promisor, server-side hooks, killable budget |
| rebase / rebase --abort / conflict listing | CLI | git drops commits whose patch-id is already upstream; libgit2 rebase does not; ort vs libgit2 merge differences; committer identity from `GIT_CONFIG_GLOBAL` (libgit2 does not read that variable **[B to confirm]**) |
| `git cherry` (patch-id) | CLI | no patch-id parity |
| worktree add/lock/remove/prune, reset --hard, clean -fdx, merge --ff-only, checkout | CLI | working-tree writes: filters (`text=auto`, `eol=lf`, `merge=union` in `.gitattributes`), hooks, long paths |
| anything on a repository libgit2 cannot open (unknown `extensions.*`, ownership check, promisor object missing) | CLI for the whole session | session open returns `NotHandled` → `CliGitSession` for that scope |

### 3.7 Testing

1. **Contract suite against both backends (S0 lays it, S1/S2 fill it).** One TUnit class,
   `[Arguments(Cli)] [Arguments(LibGit2)]`, each test builds a scratch repository and asks the
   typed `ILandingGit` method, with the CLI result as the oracle: identical values, identical
   `-z` bytes, identical reason codes and synthetic exits. Corpus: empty repo, linked worktrees
   (live, locked, prunable, missing directory), detached HEAD, packed vs loose refs, a ref both
   packed and loose, a 5,000-ref repository, refs with unusual but legal names, `text=auto` and
   `eol=lf` files on both OSes, CRLF in the index, executable bits, symlinks (Linux), a long path
   (Windows), a blob:none partial clone with a missing blob, `repositoryformatversion=1` with an
   unknown extension, a repository owned by another uid (ownership check), a corrupt loose object,
   a concurrent external `update-ref` between two calls, a ref `.lock` present.
2. **Write equivalence (S2).** For each write, run CLI and libgit2 on two clones of the same seed
   and compare `git for-each-ref`, reflog entries (message, identity, old/new) and
   `git fsck --strict` on both. No commit objects are written in-process in this design, so
   "byte-identical commits" reduces to "the rebase stays CLI"; if a later card moves the rebase,
   the oracle must pin `GIT_COMMITTER_DATE`/`GIT_AUTHOR_DATE` and identity and compare commit
   object bytes.
3. **Compare mode (S4) as a production oracle.** See 3.9.
4. **Existing suites unchanged.** With the flag off, the 61 doubles and every land/E2E class run
   the CLI path; S0 must be a no-op diff for them.

Test counts per slice are in section 4.

### 3.8 E2E progress accounting

`EvidenceGit` sees only `RunAsync`/`RunOwnedAsync`. When reads go in-process the receipt quiet
window loses its renewals and a slow land fails the 60 s window again (the 147034055 defect class).
Replacement: a production-neutral observer, `IGitOperationObserver.Completed(kind, workdir,
backend, elapsed, outcome)`, invoked by `LandingGit` once per typed call regardless of backend and
once per process (today's path). `EvidenceGit` implements it and writes the same
`protocol-git-*.json` shape with `arguments` = the typed method's command template and a new
`backend` field. `ProtocolProgress()` is unchanged (it counts files). This lands in **S0**, before
any read moves, with a probe test that a libgit2-served read still produces one record. The
existing `EvidenceGit` read caches (`LandDeliveryOptions.cs:269-330`) stay; they are a test-only
spawn-count crutch and become less necessary once S1 is on.

### 3.9 Feature flag, canary and rollout

- Setting `Landing:GitBackend` = `Cli` (default) | `LibGit2` | `Compare`, read per land at
  `BeginOperationScope()` (so a settings change affects the next land, never a running one), plus a
  per-request override on the land request (`delegate.ps1 -Land … -GitBackend LibGit2`) for canary
  lands. Recorded on the operation row and in the land profile line (`backend=`, `inProcess=`,
  `fallbacks=`, `mismatches=`).
- **Fallback:** any libgit2 exception or unsupported shape → `NotHandled` → CLI for that call; a
  session open failure → CLI for the whole scope. Fallbacks never change a land outcome.
- **Compare mode:** reads run on both backends; the **CLI answer is used**, the libgit2 answer is
  compared, and a mismatch is logged with the method, the inputs and both answers (no file
  contents) and counted. Writes are never doubled; in Compare mode S2 writes stay CLI and the
  libgit2 path only predicts (`would-write`) for comparison. Compare is the canary gate: N lands
  (operator to choose) with zero mismatches on each host before `LibGit2` is allowed there.
- Rollout order: server2 docker server (Linux) in Compare, then desktop Compare, then desktop
  `LibGit2` for reads (the host where the win is), then S2 writes.

### 3.10 Packaging risks

- `LibGit2Sharp` + `LibGit2Sharp.NativeBinaries` add a native `libgit2-<hash>.so/.dll` under
  `runtimes/<rid>/native`. The server image publishes `-r linux-x64 --self-contained false` on
  `mcr.microsoft.com/dotnet/aspnet:9.0` (Debian bookworm) (`Dockerfile:26,48`); the native binary
  must load there **[B to confirm its OpenSSL/libssh2 linkage on bookworm]**. The desktop builds
  RID-less (AppHost); `runtimes/win-x64/native` must be copied into the output and into the
  shadow-copy stores that already special-case native helpers
  (memory: fake pty-host shadow-copy closure). Tests in `Antiphon.Tests` and E2E get the native
  library transitively and must load it on both OSes.
- The image pins git (CARD-1260) and builds it `NO_OPENSSL`; libgit2 brings its own version and
  its own config discovery (system/global/xdg). Hosts that rely on `GIT_CONFIG_GLOBAL` or
  `safe.directory` in a file libgit2 does not read will refuse to open (ownership) or read a
  different identity. Because the session only reads and writes refs, the user identity matters
  only for reflog entries (S2); the ownership check matters from S1.
- Partial clone (fact 7): libgit2 cannot fault in a missing blob. S1's operations need no blobs
  except status (which compares stat data and index OIDs, not blob content, in the normal path);
  the session treats a missing-object error as `NotHandled`.

## 4. Slicing

| Slice | Content | Tests (new, approx.) | Risks |
|---|---|---|---|
| **S0** abstraction, no behaviour change | `IGitSession` + `CliGitSession` null object; `BeginOperationScope` creates it; `IGitOperationObserver` + `EvidenceGit` port; typed `ILandingGit` read methods as default interface methods reproducing today's argv; call sites in `AgentTaskLandingProtocol`, `LandOperationFactory`, `LandVerificationPolicy`, `GuardedWorktreeRemoval`, `LandWorkspace` move from argv to typed methods | ~12: argv-shape pins per typed default method (fakes see the old shapes), observer record per call, scope ownership/borrow/dispose ordering vs R release; plus the full land suites and the C467/E2E land classes unchanged | a typed method whose default argv differs by one flag silently changes a fake's match; mitigated by argv pins |
| **S1** read-only on libgit2 | `LibGit2Session` (Infrastructure only) for ResolveCommit, ShowRef/ReadRefs, IsAncestor (walk-bounded), ConfigGet, ListTreeZ, DiffTreeRawZ; status behind its own sub-flag after a benchmark; package reference + native load check on win-x64 and linux-x64; `Landing:GitBackend` setting with `Cli`/`LibGit2`/`Compare` | ~40-50 contract cases × 2 backends (corpus in 3.7), ~6 fallback tests (libgit2 exception, unopenable repo, missing promisor blob → CLI), 2 native-load smoke tests, 1 benchmark (not a gate) | ref-read semantic drift (packed/loose precedence, symbolic refs); ownership refusal on hosts; Windows file handles held between calls |
| **S2** ref writes | single-ref `update-ref` with old value (pins, tracking-ref delete, retirement pins), `refs/antiphon/` delete batch; `InProcessAsync` journal marker (3.5); reflog identity | ~20 write-equivalence cases (reflog, packed-ref deletion, concurrent writer, `.lock` present), 3 interrupted-marker re-entry tests | reflog message/identity mismatch; packed-refs rewrite racing an external `git pack-refs`; a write in-process cannot be killed |
| **S3** shared handle across the land | per-workdir handles reused across steps within an R episode; invalidation on CLI mutations; `Release(path)` before every delete; verification-window reopen | ~10: reuse/invalidation matrix, no server-owned handle under a deleted path (Windows), handle closed across the window, external writer between two reads is seen | stale odb after a fetch into a pin; Windows delete blocked by a missed `Release` |
| **S4** flag/canary/telemetry | per-request override, profile fields, Compare-mode mismatch log and counter, operator report line | ~8: compare uses CLI answer, mismatch logged, writes never doubled, setting read once per land | compare doubles read cost during canary |
| **S5** retire | remove `EvidenceGit` read caches that only existed for spawn cost; decide whether `Compare` stays; keep CLI as the fallback permanently (it is the oracle and the network path) | deletions only, suites unchanged | none new; the CLI path cannot be retired in this design |

## 5. Open questions for the operator

1. Is the win wanted on Linux at all? The measurements say the gain is Windows spawn cost; S1 could
   ship enabled on the desktop only and stay `Cli` on server2.
2. Accept that rebase, push/fetch, worktree and working-tree writes **stay CLI permanently**? The
   card's "ideally ONE shared handle" then means one handle for reads and ref writes, not for the
   whole land.
3. Canary threshold: how many zero-mismatch Compare lands per host before `LibGit2` reads?
4. Is the desktop canonical repository also a blob:none partial clone (fact 7 is server2's mirror)?
5. Should `GitWorkspaceService`'s explicit "no LibGit2" choice and NFR-01 be revisited, or does
   libgit2 stay confined to the land session (card scope says "not in scope until decided")?
6. Packaging: pin a LibGit2Sharp version and treat its libgit2 version as a floor like
   `git-minimum-version.sh`?

## 6. Remaining uncertainties

- Sibling angles A and B had not published; per-step process counts per land and LibGit2Sharp
  facts (Windows share mode default, `GIT_THREADS`, `GIT_CONFIG_GLOBAL`, bookworm native
  linkage, worktree API coverage, partial-clone behaviour) are from knowledge and marked
  **[B to confirm]**.
- Timings are server2 Linux under load 19-29; no Windows measurement was taken (the desktop runs a
  live qualification and is off limits).
- The "~740 spawns per land" figure is the E2E fixture's comment (CARD-0550/0459), not a fresh
  profile line; the production `Land git profile` log line (`AgentTaskLandService.cs:545`) is the
  source to pull for real counts.

## Not done, noted

- Fix idea (one line): ship S0+S1 with `Landing:GitBackend=Compare` on the desktop first and let
  the land profile's `mismatches=`/`inProcess=` decide S2.
