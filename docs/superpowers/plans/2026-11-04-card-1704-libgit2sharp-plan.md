# CARD-1704: LibGit2Sharp track versus fewer processes (plan)

Plan task 31e20f5a, 2026-11-04, server2 runner mirror (Linux), base master
`1470340559889c2985ea722173181ea25c51b47c`. Docs only. No code, build or test ran in this task.

Inputs read:
- CARD-1704 (operator request 2026-11-04);
- investigation A, the git usage inventory: `origin/feat/card-task-1cf13574`,
  `docs/investigations/2026-11-04-card-1704-git-usage-inventory.md`;
- investigation B, LibGit2Sharp capability and benchmarks: `origin/feat/card-task-4a75ce55`,
  `docs/investigations/2026-11-04-card-1704-libgit2sharp-capability-and-benchmarks.md`, plus its bench folder;
- investigation C, the shared-repository land design: `origin/feat/card-task-c7f02607`,
  `docs/investigations/2026-11-04-card-1704-shared-repository-land-design.md`;
- master commits `2b342761f`, `ae91aeff1`, `81ce05099` and `147034055`;
- the code named in the tables below.

The three reports are cited as **A**, **B** and **C**. Their numbers are not re-measured here.

The operator asked for this to be worked on a branch: *"swap to libgit2sharp as another abstraction over git
instead of git cli, ideally a shared repository for the whole landing operation, on a branch"*. Nothing in this
plan lands on master or rc without the decision in §9.

## Summary

**Value case.**

- **On Linux, the swap buys test seams and fewer processes, but no speed.** One land in the unit land harness
  starts **211** top-level git processes and takes 6.95 s of wall time on server2. Only **0.495 s** of that is
  spent inside git (A, trace2). A process spawn costs about 1.3 to 6.6 ms on loaded server2 (B: 1.26 ms;
  C: 6.6 ms; A: 4.0 ms). On Linux, libgit2 is 1.25 to 17 times *slower* for the whole-tree work the land also
  does: status, full-ref scans, merge trees and worktree cherry-pick (B §3).
- **The possible win is on the Windows desktop**, and nobody has measured it. The repository's own estimate is
  20 to 200 ms per spawn (B §3; `tests/Antiphon.E2E/Fixtures/LandDeliveryOptions.cs:269-270`). At that cost,
  211 spawns are **4 to 42 s per land**. Lands run one at a time on a single global land channel, so every
  second added to a land is also queue time for each land waiting behind it.
- **This plan therefore starts with measurement.**
  - **S0** is a Windows task, run after the live qualification run ends or is replaced. It runs B's desktop
    benchmark, a `GIT_TRACE2_EVENT` trace of the harness land on the desktop, the desktop's own
    `Land git profile` log lines, and a read-only check of the canonical repository's format.
  - The result is a GO or NO-GO against explicit thresholds (D-1, §2.3).

**Two tracks are compared.**

| | Track B: fewer processes with the CLI | Track L: LibGit2Sharp |
|---|---|---|
| Processes removed per land (of 211) | **54 to 73** by default (26 to 35%). Up to about 91 if the operator allows reuse of remote proofs (Q-8) | **137 to 161** with the read slice S2 (65 to 76%). About 50 to 74 stay on the CLI permanently |
| Desktop saving at 20 to 200 ms per spawn | 1.1 to 14.6 s per land | 2.7 to 32 s per land |
| New dependency | none | a native library on every host (libgit2 1.8.6; upstream has two newer security releases with no LibGit2Sharp build) |
| Works on a partial clone or a reftable repository | yes | no: such a repository refuses to open, and the whole session falls back to the CLI |
| Effort to the first production benefit | two small Code slices | four slices (S1 to S4) plus a Compare canary |
| Wasted if S0 says NO-GO | no: B1 helps every host | S1 is reused by Track B; S2 to S5 are not built |

**Recommendation (§9).**

1. Run S0 first.
2. In parallel, on the branch, build **S1**: the typed read methods and the observer, with zero behaviour
   change. S1 is the foundation of *both* tracks: a typed method is where the CLI implementation can batch
   (Track B) and where libgit2 can answer (Track L). Also build **B1**, which removes processes with no new
   dependency and is not wasted under either outcome.
3. Let the S0 numbers, re-measured after B1 and B2, decide whether S2 to S5 are built.

**Facts that limit what the card can get, whichever track is chosen.**

- "One shared repository handle for the whole landing operation" is really **one handle per working
  directory**, keyed by path and sharing a common directory. A land has at least three: the canonical
  checkout, the land worktree and the task's source worktree.
- The handles serve **reads only**.
- They are **closed while the verification build runs**, so one land has two handle sessions.
- Rebase, all network operations, worktree add/lock/remove, working-tree writes, status, whole-ref scans and
  (in this plan) ref writes stay on the CLI permanently (D-4).

## 1. Ground truth: what the card assumes vs what the code does

| Card / brief assumption | Code or measurement at `14703405` | Consequence |
|---|---|---|
| "Every land step starts git.exe … cherry-pick, merge-tree" | The land runs **no** `cherry-pick`, `merge-tree`, `patch-id`, `bundle`, `gc` or `notes` (A). `git cherry` and `rev-list --min-parents=2` belong to dispatch-time base inspection (`WorktreeBaseInspection.cs:87,178`) | libgit2's slow paths (B: merge tree 17x, cherry-pick 5x) are not on the land path. Its fast paths (ref and graph reads) are where the process count is |
| A land starts a lot of processes | 211 top-level (244 counting children) in `PostLandMutationDeliveryTests.C478_V09a_LandProducerToCaller` (A, trace2). `ae91aeff1`'s message gives 271 for the same test including the land-producer bridge. The E2E fixture comment says about 740 for a native E2E land | The harness figure is the one both tracks are compared against. The production figure is in the desktop's `Land git profile processes=` log lines (`AgentTaskLandService.cs:550`); S0 reads them |
| Process startup is the cost | Linux: 0.495 s of 6.95 s is inside git; the rest is spawn, .NET and Postgres, which trace2 cannot separate (A). The **real-repository** costs are work, not spawn: `status` 520 ms (0.36 s of it index refresh), `worktree list` 77 ms (A) | On Linux, neither track changes land time noticeably. On Windows the spawn share is unknown, and S0 measures it |
| Network is a small part | 19 of 211 are network round trips: 12 `ls-remote`, 7 `fetch`, 1 `push`. The harness uses a local bare remote. B measured 449 ms per CLI `ls-remote` to GitHub (187 ms with libgit2) | In production the round trips may cost **6 to 10 s per land on any OS**, more than the spawn cost on Linux. Neither track's default removes them (D-4). S0 measures it, and Q-8 asks about reusing remote proofs |
| "ONE shared repository handle for the whole landing operation" | `LandRepositoryLeaseScope` (`PreparedLandingVerification.cs:14-59`) owns the lease and the `ILandingOperationScope` (AsyncLocal cache, `LandingGit.cs:639-686`). It is **released for the verification window** and re-acquired with a new scope (`AgentTaskLandService.cs:714-734`) | The session is created and disposed with this scope: one per lease episode, so two per land (D-6). Handles are per working directory (C §2.1) |
| A git abstraction gives test seams | `ILandingGit` already is one. 51 class declarations in 36 files at `14703405` (`class … : LandingGit` 37 including `EvidenceGit`; `: ILandingGit` 16 including `ControlledLandingGit`; C counted 61 with a looser grep). CARD-1432 G1 `ReadRefsAsync` (`ILandingGit.cs:65-76`) is the precedent: a default interface method reproduces the old argv, so fakes keep their command shapes | S1 follows that precedent. TestDesign takes the exact roster |
| Crash cuts and E2E progress hang off git processes | `ControlledLandingGit` hooks (`BeforeObservedCommand`/`AfterCommand`) key on **mutating** argv (`rebase`, `merge --ff-only`, `push`, `update-ref`, `worktree remove`). `EvidenceGit` writes one `protocol-git-<guid>.json` per `RunAsync`/`RunOwnedAsync` call (`LandDeliveryOptions.cs:274-340`). `ProtocolProgress()` counts those files plus `protocol-verify-*.json` (`147034055`) | Moving reads in-process does not move a crash cut, because cuts are on writes, which stay on the CLI. It does remove E2E renewals. The observer (S1) must exist before any read moves (C §3.8) |
| libgit2 can open our repositories | It supports only the `noop`, `objectformat` and `worktreeconfig` extensions. A format-v1 repository with `extensions.partialclone` or `extensions.refStorage` refuses to open (B). The server2 mirror is v1 with `promisor=true` and `blob:none`, but currently has no `extensions.partialclone` (B, C fact 7). **The desktop is not inspected** | A hard precondition for Track L on each host (Q-4). An unopenable repository falls back to the CLI for the whole session (D-5), which gives zero benefit |
| libgit2 has the land's write semantics | No CAS ref update in the LibGit2Sharp API: `git_reference_create_matching` is exported but not imported (B). No credential helpers. No `-x` footer. A lease push is possible through the negotiation callback (B, reproduced) | Ref writes and network stay on the CLI in this plan (D-4). A later card can revisit writes |
| A prior "no LibGit2" decision is on record | `server/Application/Services/GitWorkspaceService.cs:10-14` ("Shells out (no LibGit2) so behaviour matches whatever git the machine has"). NFR-01 (`docs/features/001-kanban-agent-orchestration/01-requirements.md:47`) confines libgit2sharp to `Infrastructure/` | Track L stays inside `server/Infrastructure/Git/` and inside the land session. `GitWorkspaceService` keeps the CLI (Q-5) |
| The git handler is part of the server | CARD-1546 (Backlog) asks for the git operations handler in its own csproj, per test-performance rule R2 | Track L code goes under `server/Infrastructure/Git/LibGit2/`. If CARD-1546 lands first, it moves with the handler (Q-5) |
| An undisposed handle is harmless | After `Dispose`: 0 mapped packs and 0 fds. Undisposed: 2 mapped packs and 1 fd remain even after a full GC, because there is no finalizer (B) | On Windows that blocks `gc`/repack unlinks, and possibly a worktree delete (unmeasured). Dispose before every delete and at every lease release (D-6, S3) |
| `GIT_*` environment and `-c` apply | libgit2 does not read `GIT_CONFIG_PARAMETERS`/`-c`, or `GIT_DIR`/`GIT_WORK_TREE` without `FROM_ENV` (not used by LibGit2Sharp). It honours `safe.directory` from global/system config, with owner validation on by default (B) | Reads that depend on `-c` stay on the CLI. Ownership is checked on the desktop service account in S0 and S2 |

## 2. S0: measurement (Windows, read-mostly; a Windows task, not now)

### 2.1 When and where

- **When.** After the live Windows qualification run ends or is replaced. Never during it, because the
  benchmark loads the host and would skew both.
- **Lane.** Pass `-Platform Windows` and do not pin a runner. `GET /api/session-runners` today lists `desktop`
  (Windows, capacity 2) as the only Windows runner. `GET /api/runner-defaults` gives `globalRunnerId=server2`,
  which is why `-Platform Windows` is needed.
- **Role.** Investigate, writing one report.
- **Starting branch.** Start from `origin/feat/card-task-4a75ce55`, so the bench folder is present: the script
  resolves its sources from `$PSScriptRoot`.
- **Budget.** About 2 h, including one Windows `Antiphon.Tests` build (unmeasured; 15 min is provisional).

### 2.2 Steps (each prints a number into the report; the scratch paths are printed for exact-path cleanup)

1. **Repository format and ownership (read-only, about 1 min).** Run this in the canonical desktop checkout that
   lands use (`task.RepoPath`; `C:\src\Antiphon` per `docs/logs.md`). The operator can run the same block (Q-4):
   ```powershell
   $r = 'C:\src\Antiphon'
   git -C $r config --show-origin --get-regexp '^(core\.repositoryformatversion|core\.longpaths|extensions\..*|remote\.origin\.(promisor|partialclonefilter))$'
   git -C $r rev-parse --is-shallow-repository --git-common-dir
   (Get-ChildItem (Join-Path $r '.git\objects\pack') -Filter *.promisor -ErrorAction SilentlyContinue).Count
   git config --show-origin --get-all safe.directory
   whoami; (Get-Acl (Join-Path $r '.git')).Owner
   (git -C $r for-each-ref | Measure-Object).Count; (git -C $r worktree list --porcelain | Select-String '^worktree ').Count
   ```
2. **The production profile (read-only, about 5 min).** Run `scripts/logs.ps1 -Source desktop-server` over the
   retained 5 days, and grep `Land git profile`. For every land, record `wallSeconds`, `processes`,
   `gitSeconds`, `remote`, `verify` and `cleanup`. `gitSeconds` is the .NET-observed time inside git processes,
   spawn included (`LandingGitProfile.Record`). Report the median and p95 per field, and
   `wallSeconds - verify`.
3. **B's per-call benchmark (about 20 min).** Run
   `pwsh -NoProfile -File docs/investigations/2026-11-04-card-1704-libgit2sharp-bench/run-desktop-bench.ps1 -Iterations 100`,
   with B's three manual additions:
   - a >260-character path with `core.longpaths=true`;
   - a repository owned by another account, to compare libgit2's ownership refusal with git's;
   - a worktree delete and a `git gc` while an undisposed `Repository` is alive.
4. **The harness land under trace2 (about 30 min including the build).** Use A's method on Windows. Run
   `PostLandMutationDeliveryTests/C478_V09a_LandProducerToCaller` five times through `scripts/build-slot.ps1`,
   with `GIT_TRACE2_EVENT=<scratch>\trace<n>` and `GIT_TRACE2_EVENT_NESTING=1`. For each run, record:
   - the land wall time (first common-dir read to `update-ref --stdin`);
   - the process count;
   - `Σ t_abs`, the time inside git;
   - the .NET-observed git time. The test prints no profile today, so use the elapsed time per trace file
     between the parent's `start` and `exit` events. If that cannot be attributed, record it as *unknown*, not
     zero.
5. **The network cost (read-only, about 2 min).** Run
   `git -C C:\src\Antiphon ls-remote --refs --exit-code origin refs/heads/master` 20 times, and report the
   median and p95. This is the cost of one land observation round trip in production.
6. **CPU load and Defender exclusions,** as the script prints them. Repeat steps 3 and 4 once at the
   qualification-like load if the operator allows it, and otherwise mark them "idle host only".

### 2.3 Derived numbers and the GO/NO-GO rule (D-1)

- `T_spawn = (Σ .NET-observed git time - Σ t_abs) / processes`, the median over the five step-4 runs.
- `spawnPerLand = T_spawn × P`, where `P` is the production median `processes` from step 2, or 211 if step 2
  has no lines.
- `landWallExVerify` is the median of `wallSeconds - verify` from step 2.
- Predicted savings: Track B ≈ `60 × T_spawn` (the midpoint of 54 to 73). Track L S2 ≈ `145 × T_spawn` (the
  midpoint of 137 to 161) minus about 0.15 s of in-process cost.

| Outcome | Rule | Next |
|---|---|---|
| **NO-GO** | `spawnPerLand < 2 s` **and** `< 10%` of `landWallExVerify`, **or** step 1 shows `extensions.partialclone`/`refStorage`, an ownership refusal by libgit2 for the service account, or a `.promisor` pack on the canonical repository | Track L stops after S1. Track B B1 still ships; B2 only if `60 × T_spawn ≥ 1 s` |
| **Track B first** | otherwise, unless GO | B1 and B2. Then repeat step 4 at the B2 tip and re-apply this table to the **remaining** spawn cost |
| **GO** (Track L S2) | after B1 and B2, the remaining `spawnPerLand ≥ 5 s` **or** `≥ 20%` of `landWallExVerify`, **and** step 1 is clean | S2, then S3, then the S4 canary |

The thresholds are stated defaults (Q-7). 5 s at about 200 processes is about 25 ms per spawn, the low end of the
Windows estimate. Below 2 s, a land saves less than its own run-to-run noise on a loaded desktop (A: up to 19x
cold/warm variance).

## 3. Track B: cut the process count with the existing CLI

### 3.1 Where the 211 go, and what can be removed without libgit2

Histogram (A, top-level processes, one harness land):
- 32 `show-ref --verify`, 26 `rev-parse --verify`, 24 `remote get-url`, 21 `symbolic-ref`;
- 19 `rev-parse --path-format=absolute` (common-dir and `--git-path index.lock`), 13 `check-ref-format`,
  12 `ls-remote`;
- 10 each of `merge-base --is-ancestor`, `rev-parse --absolute-git-dir` and `worktree list`;
- 7 each of `fetch` and `status`, 6 `show-ref --exists`, 3 `update-ref`;
- the rest are single commands.

By phase, cleanup and branch delete account for 82 of the 211.

| Lever | Source today | Removed per land (estimate) | How | Safety rule |
|---|---|---:|---|---|
| **B-1** Validate Antiphon-generated ref names in .NET | `check-ref-format` 13 (`LandingGit.cs:70-104`; refs/heads verdicts are already memoised per instance by `2b342761f`, so the remainder is pin/observation refs or misses across instances) | 11 to 13 | A .NET validator for refs that Antiphon builds from its own grammar (`refs/antiphon/(land\|progress)/<guid>/…` with fixed leaf names). Every other name still asks git | Parity corpus against `git check-ref-format` (valid, invalid and edge names). Code first measures which refs the 13 are |
| **B-2** Derive git paths from the layout stamp | 19 path-format plus 10 `--absolute-git-dir` | 15 to 22 | Reuse `2b342761f`'s `RepositoryChildJournal` layout-stamp rule (entry kind, link-resolved path, creation time, pointer text, commondir) for `--git-common-dir`, `--absolute-git-dir` and `--git-path index.lock` in `LandingGit`. Git is asked again whenever the stamp changes | The stamp rule is already reviewed. A changed layout re-asks |
| **B-3** Batch the identity sample | `IdentityAsync` (`LandingGit.cs:1171-1205`): `symbolic-ref -q HEAD`, `show-ref --exists`, `show-ref --verify --hash`, and `rev-parse --verify` twice, per sample. About 6 samples per land (6 `--exists`) | 15 to 20 | Fast path: `symbolic-ref -q HEAD` plus one `rev-parse HEAD^{commit} <src>^{commit} <src>` in a single process. **On any non-zero exit or unexpected output, re-run today's sequence**, so the reason code (`detached_head`, `source_ref_missing` vs `source_ref_error`, …) always comes from today's commands | "Errors are never absence": the batch only ever *confirms*. A refusal comes from the old path |
| **B-4** Read pins and refs in one process | Pin loops in `GuardedWorktreeRemoval.cs:390-395`, `LandOperationFactory.cs:42-148` and `AgentTaskLandSourceResolver.cs:309-617` (`show-ref --verify --hash` per ref) | 10 to 15 | Call the existing `ReadRefsAsync` (one `show-ref --verify` for N refs, CARD-1432 G1), keeping the first-missing-stops order | Same as G1. Fakes keep the per-ref argv through the default method |
| **Default total** | | **54 to 73** (26 to 35%) | | |
| B-5 (Q-8, not default) Reuse a remote proof inside one lease episode | Three `cleanup-observed` observations of about 9 processes each (`GuardedWorktreeRemoval.cs:346,400`); 12 `ls-remote` in all | up to 18 more, plus 2 network round trips | Reuse the observation taken earlier in the same lease episode when no Antiphon mutation happened in between and it is less than T s old | These are deliberate re-proofs before a destructive delete. Changing them is a safety-policy decision, so this is **not done by default** |
| Not removable | `worktree list` 10 (the live re-list is deliberate, CARD-0642 R1); the cleanup inspections (read 1 and read 2 around evidence retention, then authority immediately before delete, `GuardedWorktreeRemoval.cs:109-140`); `remote get-url`/`symbolic-ref` in each destination recheck (`ae91aeff1` already removed the duplicate); status, network, rebase, worktree verbs | 0 | | Each is a recheck immediately before a mutation |

The estimates come from A's histogram and the code paths above. They are not measured. B2's Code task re-runs A's
trace2 method at its tip and reports the actual count. Production lands use one `LandingGit` singleton, so the
B-1 share may already be smaller there.

### 3.2 Track B slices

**B1: name validation and path derivation (B-1, B-2).** Independent of S1, and it can start now.
- Files: `server/Infrastructure/Git/LandingGit.cs` (the validator and the stamp-keyed path cache);
  `server/Infrastructure/Git/RepositoryChildJournal.cs` (share the stamp helper, no behaviour change);
  `server/Application/Dtos/LandingDtos.cs` (`LandingGitProfile` gains `PathHits`).
- Tests (`tests/Antiphon.Tests/Infrastructure/C1704ProcessReductionTests.cs`, about 10):
  - **P-1** the ref-name parity corpus (about 40 `[Arguments]` names) equals `git check-ref-format` exit 0/1;
  - **P-2** a name outside the Antiphon grammar still spawns `check-ref-format`;
  - **P-3** a common dir, git dir and index.lock path come from git once per stamp;
  - **P-4** repointing `.git`, a new creation time or a changed `commondir` asks again;
  - **P-5** the path cache never crosses a lease episode;
  - **P-6** the harness land process count drops by at least 25 (TestDesign pins the exact number at the tip);
  - plus regression rows: `LandingGitProcessSavingTests` and `LandingGitTests`.
- Effort: one Code task, about 3 h. Risk: a stale path after an external `git worktree move`. Mitigated by the
  stamp, as `2b342761f` already does.
- Windows: the stamp reads NTFS creation time and reparse targets; one Windows row runs `LandWorkspaceTests`
  and the Windows-specific `AgentTaskLand*WindowsTests`.

**B2: batched identity and ref reads (B-3, B-4).** After S1 when both are built, so the batching goes into the
typed methods. Otherwise it goes directly into `LandingGit` and the three callers.
- Files: `LandingGit.cs` (`IdentityAsync` fast path); `GuardedWorktreeRemoval.cs`, `LandOperationFactory.cs`
  and `AgentTaskLandSourceResolver.cs` (pin loops to `ReadRefsAsync`).
- Tests (the same class, about 10 more):
  - **P-7** the batch success path returns an identical snapshot;
  - **P-8** each of today's identity refusals (detached, missing, error, mismatch, moved) is still reported with
    its exact code through the fallback, `[Arguments]` per code;
  - **P-9** a fault injected on today's argv (subclass `RunAsync` override) still yields its code, because the
    batch's failure re-runs today's commands;
  - **P-10** the pin loops keep first-missing order;
  - **P-11** the harness count drops by at least a further 20.
- Regression: `LandingGitBatchingTests`, `AgentTaskLandIdentityMatrixTests`, `AgentTaskLandCleanupSafetyTests`,
  `AgentTaskLandRemovalMatrixTests` and `PostLandMutationDeliveryTests`.
- Effort: one Code task, about 5 h, plus test churn. Some existing tests inject faults on the per-ref argv, and
  they must still fire through the fallback; TestDesign lists them.
- Risk: a batched `rev-parse` that accepts a name today's `--verify` would refuse. Mitigation: the batch output
  is only accepted when every line is a full object id **and** the old shape agrees on the first sample of each
  scope in Compare-style debug builds. TestDesign decides whether that check stays.

## 4. Track L: LibGit2Sharp (S1 to S5)

The seam is C §3.1, renumbered: the brief's S1 is C's S0, and S2 is C's S1. C's write slice is out of scope for
this plan (D-4).

```
ILandingGit                         land-facing API; typed reads are default interface methods = today's argv
  LandingGit : ILandingGit          CLI and the reference oracle; owns process, journal and kill
    └─ IGitSession? (AsyncLocal, created by BeginOperationScope when the backend is not Cli)
IGitSession : IDisposable           one per repository-lease episode; one handle per canonical working directory
  ├─ CliGitSession                  null object: every call returns NotHandled, so the CLI path runs
  └─ LibGit2Session                 S2; Infrastructure only
IGitOperationObserver               S1; called once per process (today) and once per typed in-process call (S2)
```

### S1: abstraction, CLI implementation and observer. Zero behaviour change (Linux; starts now)

**Content.**
- **Typed read methods on `ILandingGit`, as default interface methods that reproduce today's argv exactly:**
  `ResolveCommitAsync`, `IsAncestorAsync`, `ShowRefAsync` (Exists/Missing/Error tri-state),
  `SymbolicRefAsync`, `AbsoluteGitDirAsync`, `ConfigGetAsync`, `ListTreeZAsync` and `DiffTreeRawZAsync`.
  `StatusAsync` is typed too but stays CLI in every backend.
- **`LandingGit` implements each one by calling its own virtual `RunAsync`** with the same argv, so the 37
  subclasses that override `RunAsync` still see every read.
- **Read call sites move from argv to the typed methods:** `AgentTaskLandingProtocol`, `LandOperationFactory`,
  `AgentTaskLandSourceResolver`, `LandVerificationPolicy`, `GuardedWorktreeRemoval`,
  `GuardedVerificationRemoval`, `LandWorkspace`, `LandRefRetention` and `LandRecoveryWitness`. Mutating argv
  calls stay on `RunAsync`/`RunOwnedAsync`.
- **`IGitSession`, `GitAnswer<T>` (Handled / NotHandled(reason)) and `CliGitSession`.** `BeginOperationScope()`
  attaches a session only when the backend is not `Cli`. In S1 that is never.
- **`IGitOperationObserver`.**
  - `LandingGit` calls it once per completed process: the `RunAsync`/`RunOwnedAsync` path, plus the bytes-input
    path (`update-ref --stdin`, `check-attr`, `cat-file --batch`), which today writes no E2E record.
  - It passes `(kind, workdir, commandTemplate, backend, elapsed, exitCode)`.
  - `EvidenceGit` moves its record writing from its `RunAsync` override to the observer. The JSON shape stays the
    same, plus a `backend` field and `kind` (`process` / `stdin` / `inproc`). Its stable-read caches stay as
    they are.
- **The `Landing:GitBackend` setting:** a **scalar string**, `Cli` (default) | `LibGit2` | `Compare`.
  - It is parsed once per `BeginOperationScope()`, so it is read per land and never changes a running one.
  - Any other value, including an array or an object, is refused at startup validation, never treated as
    empty. This is the CARD-0824 S1c lesson: `[]` binds to null and widens.
  - In S1, `LibGit2` and `Compare` are accepted values that log `git_backend_not_built` once and run `Cli`.
- **The contract corpus** `C1704ContractTests`, run with `[Arguments("Cli")]` only in S1.
  - It uses real scratch repositories, and the CLI result is the oracle.
  - The cases: empty repository, linked worktrees (live, locked, prunable, missing directory), detached HEAD,
    packed versus loose refs, a ref both packed and loose, 5,000 refs, unusual legal names, `text=auto` and
    `eol=lf` files, a ref `.lock` present, a corrupt loose object, a concurrent external `update-ref` between
    two calls, a format-v1 repository with an unknown extension, a blob:none partial clone with a missing blob.
  - S2 adds the `LibGit2` arguments to the same methods.

**Files.**
- `server/Application/Interfaces/ILandingGit.cs`;
- **new** `server/Application/Interfaces/IGitSession.cs` (with `GitAnswer<T>` and `IGitOperationObserver`);
- `server/Infrastructure/Git/LandingGit.cs`;
- **new** `server/Infrastructure/Git/CliGitSession.cs`;
- the nine callers above;
- `server/Application/Settings/LandingSettings.cs` (`GitBackend` string) and its validator;
- `server/Program.cs` (observer registration: a no-op in production);
- `server/Application/Dtos/LandingDtos.cs`: `LandingGitProfile` gains `InProcess`, `Fallbacks` and
  `Mismatches`, all 0 in S1, and the profile line gains `backend=`;
- `tests/Antiphon.E2E/Fixtures/LandDeliveryOptions.cs` (`EvidenceGit` to the observer).

**Tests (red first where a behaviour exists; about 25 new methods).**
- `C1704TypedReadShapeTests`:
  - **T-1** each typed default method issues exactly today's argv on a direct `ILandingGit` fake, `[Arguments]`
    per method (9);
  - **T-2** the same on a `LandingGit` subclass whose `RunAsync` records (9);
  - **T-3** each moved call site issues the same argv sequence as at the base. Recorded from
    `ControlledLandingGit.Trace` over the harness land and compared with a committed golden trace taken at
    `14703405`.
- `C1704GitObserverTests`:
  - **O-1** one observer call per process with today's template;
  - **O-2** the stdin path now produces one record;
  - **O-3** an `EvidenceGit` record set over the harness land equals the base's `protocol-git` set plus exactly
    the stdin records (count and argv);
  - **O-4** observer exceptions never fail a git call.
- `C1704SessionScopeTests`:
  - **SC-1** `Cli` attaches no session;
  - **SC-2** a nested scope borrows and only the outer scope disposes;
  - **SC-3** `ReleaseAsync` disposes the session before the lease `FileStream` closes (ordering probe);
  - **SC-4** `BeginReadScope` after reacquire creates a fresh session.
- `C1704BackendSettingTests`:
  - **BS-1** `Cli`, `LibGit2` and `Compare` bind;
  - **BS-2** an array, an object, an empty string or an unknown value refuses startup with
    `git_backend_invalid`;
  - **BS-3** the value is read once per scope.
- `C1704ContractTests` (`Cli`): **K-1** to **K-14**, the corpus above, about 14 methods. Several have
  `[Arguments]` cases.
- Regression rows:
  - `LandingGitTests` (39), `LandingGitBatchingTests` (4), `LandingGitProcessSavingTests` (4),
    `LandingGitProfileTests` (9), `LandWorkspaceTests` (7);
  - `PostLandMutationDeliveryTests` (35 methods), `AgentTaskLandCheckpointMatrixTests`,
    `AgentTaskLandFailureDiagnosticTests`;
  - one E2E land receipt method (`AgentTaskLandDeliveryE2ETests.C467_V29_RealOutcomeProducerMatrix_Batch2`),
    because `EvidenceGit` changed.

**Positive controls.**
- PC-L1: a typed default with one flag changed. T-1 and T-2 turn red.
- PC-L2: `LandingGit` typed methods call `ExecuteAsync` instead of the virtual `RunAsync`. T-2 and O-3 turn red.
- PC-L3: the observer is skipped on the stdin path. O-2 turns red.
- PC-L4: the session is disposed after the lease `FileStream`. SC-3 turns red.
- PC-L5: an array binds as `Cli`. BS-2 turns red.

**Risks.** A typed default whose argv differs by one flag silently changes what a fake matches (T-1 and T-2 pin
it). Golden-trace churn when someone else changes a read in `LandingGit.cs` (see §5).

**Effort.** One Code task, about 6 h, plus Review. This is the largest diff of the plan, about 10 files, all
mechanical.

**Windows.** No production change. One Windows row (`LandWorkspaceTests` plus the two
`AgentTaskLand*WindowsTests` classes) confirms the moved call sites on Windows paths.

### S2: read-only operations on libgit2, behind the flag, with Compare mode (gated on S0 GO)

**Content.**
- **`LibGit2Session`** (`server/Infrastructure/Git/LibGit2/`) serves `ResolveCommit`, `ShowRef`/`ReadRefs`,
  `IsAncestor`, `SymbolicRef`, `AbsoluteGitDir`/common dir, `ConfigGet` (including `remote get-url --push --all`
  with `pushurl`/`insteadOf`/`pushInsteadOf` rewriting, proven by the corpus or kept CLI), `check-ref-format`
  (`Reference.IsValidName`), `ListTreeZ` and `DiffTreeRawZ`.
- **The `-z` methods return the bytes the CLI would print.** The parsers stay where they are, and the corpus
  compares bytes.
- **`IsAncestor` is walk-bounded.** Past N commits it returns NotHandled, so the killable CLI runs instead.
- **Fallback per call (D-5).**
  - Any exception, unsupported repository shape (unknown extension, ownership refusal, missing promisor
    object), unsupported input, or `NotFoundException` outside the tri-state ref reads returns
    `NotHandled("libgit2:<category>")`, and the call re-runs on the CLI.
  - **A refusal reason never comes from libgit2.** Only a CLI verdict becomes a reason code.
  - An open failure turns the whole session into `CliGitSession`.
  - Each fallback is logged once per scope with its reason and counted (`fallbacks=`).
- **Compare mode.** Both backends answer, and **the CLI answer is used**. A mismatch logs the method, the inputs
  and both answers (object ids and ref names only, never file content) and counts `mismatches=`.
- **The observer is called for every in-process answer** (`kind: inproc`), so `ProtocolProgress()` keeps
  renewing.
- **The package:** `LibGit2Sharp` 0.32.0, pinned exactly (Q-6), in `server/Antiphon.Server.csproj`. The
  NativeBinaries version comes pinned through the nuspec. A startup log line names `GlobalSettings.Version`. The
  RID-less desktop output must carry `runtimes/win-x64/native`.

**Files.**
- **new** `server/Infrastructure/Git/LibGit2/LibGit2Session.cs`, `LibGit2ZFormatter.cs` and
  `LibGit2SessionFactory.cs`;
- `LandingGit.cs` (session-first in the typed methods; Compare);
- `server/Antiphon.Server.csproj`, `server/Program.cs`;
- `tests/Antiphon.Tests/Antiphon.Tests.csproj` (transitive native assets: verify);
- `Dockerfile` (no change expected: B verified bookworm glibc and libssl3; S2 adds a smoke step);
- `docs/agent-kinds.md` is not touched. `docs/project-context.md` records the hybrid rule.

**Tests.**
- `C1704ContractTests`: the `LibGit2` arguments are added to every K method, so the same corpus runs twice. An
  identical value, `-z` bytes and reason code are expected, except where a K case expects `NotHandled` with a
  CLI-served answer.
- `C1704LibGit2FallbackTests` (about 8):
  - **F-1** an unknown extension: the session falls back whole, with the CLI answer;
  - **F-2** a blob:none partial clone with a missing blob: the tree read falls back;
  - **F-3** a ref `.lock`, or a corrupt loose object: NotHandled, and the CLI's own reason;
  - **F-4** a walk past the bound: the CLI answers;
  - **F-5** libgit2 throws on a ref read: the CLI's verdict, never `Missing`;
  - **F-6** `-c`-dependent reads always go to the CLI;
  - **F-7** `fallbacks=` is counted;
  - **F-8** a libgit2 exception never surfaces as a reason code (reflection over the published codes).
- `C1704LibGit2CompareTests` (about 5):
  - **C-1** the CLI answer is used when the two differ (injected mismatch);
  - **C-2** a mismatch is logged without content;
  - **C-3** `mismatches=` reaches the profile line;
  - **C-4** Compare never calls libgit2 for a mutating verb;
  - **C-5** the setting changes between two lands, never inside one.
- `C1704LibGit2NativeLoadTests` (both OSes, 2): **N-1** the native library loads and reports libgit2 `1.8.6`;
  **N-2** a session opens the test repository and resolves HEAD.
- `C1704LibGit2WindowsTests` (Windows only, `RequireWindows()` plus `WindowsUnitSkipRoster` entries, 4):
  - **W-1** a >260-character worktree path read with `core.longpaths=true` matches the CLI;
  - **W-2** a repository whose owner is not the current account: libgit2's refusal becomes NotHandled and the
    CLI answers, or both answer;
  - **W-3** after session `Dispose`, the worktree directory deletes, and `git gc` unlinks old packs with no
    "Unlink of file" retry;
  - **W-4** while a session is alive, a delete under the worktree is attempted and the outcome recorded. This
    probe documents the behaviour; S3's rule does not depend on it.
- A harness land in `LibGit2` mode (`PostLandMutationDeliveryTests` C478_V09a in an `[Arguments]` twin, or a new
  `C1704LibGit2LandTests` with 3 methods):
  - **H-1** the same outcome, pins and published sha as the `Cli` run;
  - **H-2** the process count drops by at least 100 (TestDesign pins the exact number);
  - **H-3** one observer record per typed in-process read.

**Positive controls.**
- PC-L6: return `Missing` on a libgit2 exception. F-5 turns red.
- PC-L7: Compare returns the libgit2 answer. C-1 turns red.
- PC-L8: drop the walk bound. F-4 turns red.
- PC-L9: no observer call for in-process reads. H-3 turns red.
- PC-L10: drop `Dispose` in the session. W-3 turns red on Windows.
- PC-L11: the `-z` formatter drops the trailing NUL. K ls-tree and diff-tree turn red.

**Risks.**
- Ref-read semantic drift: packed/loose precedence and symbolic refs (the corpus covers it).
- An ownership refusal on the desktop service account (W-2, S0 step 1).
- Native load failure in the AppHost output or a shadow copy (N-1).
- A native fault kills the server process rather than one git child. This is a new failure class, and S4 adds
  the stop signal.
- Synchronous calls block a thread with no cancellation token. Network calls never enter the session, and history
  walks are bounded.

**Effort.** One Code task, about 8 h. TestDesign first, because the corpus is the main cost.

### S3: one handle per working directory across a lease episode (Linux and Windows)

**Content.**
- Handles are opened lazily per canonical working directory and reused across the steps of one lease episode.
- They are invalidated on every CLI mutation in the same scope: rebase, reset, worktree add, fetch into a pin,
  push, and every `ChangesRegistrations` topology hit (`LandingGit.cs:606-637`).
- The session never caches ref **values**, only open handles (external writers, C fact 6). After a fetch into a
  pin, the next read reloads refs and refreshes the odb.
- **The session closes before the lease is released for the verification build** (`ReleaseAsync` ordering,
  SC-3), and a new one opens after reacquire.
- **`session.Release(path)` runs before every delete:** `GuardedWorktreeRemoval` before `TryRemoveAsync` and
  `UnregisterWorktreeAsync`, `LandWorkspace` before `reset --hard`/`clean -fdx`, and `GuardedVerificationRemoval`
  before `worktree remove`.
- A private lock per handle, plus a contention counter.

**Files.** `LibGit2Session.cs`, `LandingGit.cs` (invalidation hooks), `GuardedWorktreeRemoval.cs`,
`GuardedVerificationRemoval.cs`, `LandWorkspace.cs` and `PreparedLandingVerification.cs` (no new owner; ordering
assertions only).

**Tests.**
- `C1704SharedHandleTests` (about 8):
  - **SH-1** one open per working directory per episode (open counter);
  - **SH-2** a CLI mutation invalidates that working directory's handle, and a topology change drops all handles;
  - **SH-3** an external `update-ref` between two reads is seen by the second read;
  - **SH-4** a fetch into a pin is visible to the next read;
  - **SH-5** no handle is open during the verification window (a probe inside the controlled verifier);
  - **SH-6** `Release(path)` runs before each delete (call-order trace);
  - **SH-7** concurrent calls on one handle serialise and are counted;
  - **SH-8** a disposed session refuses use (`ObjectDisposedException` becomes NotHandled, then CLI).
- `C1704SharedHandleWindowsTests` (Windows only, 3):
  - **SW-1** after a land in `LibGit2` mode, no handle owned by the server test process exists under the deleted
    source or land worktree, checked with the existing `WindowsWorktreeLockDiagnostics`/`WorktreeHandleCsv`
    enumeration;
  - **SW-2** a `git gc` run by the controlled verifier inside the window unlinks packs;
  - **SW-3** the land worktree's `reset --hard` after `Release` succeeds with a long path.

**Positive controls.**
- PC-L12: cache ref values. SH-3 turns red.
- PC-L13: skip invalidation after a fetch. SH-4 turns red.
- PC-L14: keep the session across the window. SH-5 and SW-2 turn red.
- PC-L15: drop `Release` before a delete. SH-6 and SW-1 turn red.

**Risks.** A stale odb after a fetch into a pin. A Windows delete blocked by a missed `Release`, where the
diagnostics would now name the **server** process as the holder.

**Effort.** One Code task, about 4 h.

### S4: canary, telemetry and the criteria to switch hosts

**Content.**
- The profile line gains `backend= inProcess= fallbacks= mismatches= contention=`.
- One Information line per land, in Compare mode, with the mismatch count by method.
- `GET /api/version` or the startup log names the libgit2 version.
- A docs section in `docs/bootstrap.md` ("Land git backend (CARD-1704)") with the enabling steps and the
  rollback.
- No per-request override. The backend is a per-host setting (D-8).

**Tests.** `C1704CanaryTelemetryTests` (about 5):
- **CT-1** the profile line carries the four fields;
- **CT-2** Compare counts per method;
- **CT-3** a host set to `Cli` logs `backend=Cli inProcess=0`;
- **CT-4** the setting is read per land (two lands, flipped between them);
- **CT-5** a session open failure logs once and runs CLI.

**Rollout and switch criteria (D-9, Q-3).**
1. server2's server is set to `Compare` only for corpus breadth on Linux. It never goes to `LibGit2` unless S0's
   Linux numbers change.
2. The desktop is set to `Compare`.
3. After **30 consecutive real lands with `mismatches=0`**, `fallbacks` under 5% of typed calls, no stop signal
   (§7), and the land wall excluding verify within 10% of the pre-canary median, the desktop switches to
   `LibGit2`.
4. Re-measure §2.2 step 2 after 30 more lands and report the saving against the S0 prediction.

**Effort.** One Code task, about 3 h. Then canary time, measured in lands, not hours.

### S5: retire what is retired

- Decide whether `Compare` stays. Default: keep it as a diagnostic value and delete nothing, because the CLI is
  permanent.
- Remove the `EvidenceGit` stable-read caches only if E2E land classes stay inside their windows without them
  (`LandDeliveryOptions.cs:269-330`). Otherwise keep them: they also bound test spawn cost on the CLI path.
- Remove the S1 `git_backend_not_built` branch.
- Update `GitWorkspaceService.cs:10-14`'s comment only if Q-5 changes the decision.
- The CLI path is never retired: it is the oracle, the fallback and the only path for writes and network.

Tests: deletions only, with the S1 and S2 suites unchanged. Effort: about 2 h.

## 5. Interaction with in-flight work

| Work | Interaction | Rule |
|---|---|---|
| `2b342761f` (master, in the base): journal common-dir reuse, the `check-ref-format` memo, `RefFormatHits`, the git lane for `PostLandMutationDeliveryTests` | B1 reuses its layout-stamp rule. S2 makes the memo moot on the in-process path, but the memo stays on the CLI and fallback path. `PostLandMutationDeliveryTests` stays on `GitProcessSpawnLimit` | The CP rows in this plan use that lane. `rc` lacks `GitProcessSpawnLimit` (memory: CARD-1421 `cc4a5564d`), so an rc port of any C1704 class swaps the limiter |
| `ae91aeff1` (someone else's land, in the base): `LandingGit.cs` only, +20/-9; `ConfirmedEndpointAsync`/`ReadDestinationAsync` return the endpoint at `PushAsync`, `PushOwnedAsync`, `PushSourceOwnedAsync` and the `ObserveWithPrefixAsync` loop | S1 does **not** type the destination/endpoint path: it is network-adjacent and stays argv. S2's `ConfigGet` for `remote get-url` must keep "one read per destination check" and must not reintroduce a second read | Every C1704 Code slice touches `LandingGit.cs`, so none runs concurrently with another Code task in that file (the orchestrator's same-area deferral). If `ae91aeff1` is ported to rc, C1704 ports start from that rc tip |
| CARD-0824 S2 (`PublishedBranchDeletionService`, shipped off, `Landing:DeleteSourceBranchAfterPublication`) | Uses `ConfirmTargetContainsAsync`, `RecheckSourceRemoteAsync`, `DeleteSourceBranchAsync` (`--force-with-lease` push) and `DeleteTrackingRefAsync` under the repository lease but **outside** a land scope. The runner side deletes its local refs over phone-home, with git on the runner | Out of Track L's reach by construction: no `BeginOperationScope`, so no session, and all of it is network or ref writes, which stay on the CLI (D-4). The runner is out of scope (card text). Track B B-4 does not touch these methods |
| `81ce05099`/`147034055`: `LandVerifierEvidence` heartbeat; `ProtocolProgress()` counts `protocol-git` plus `protocol-verify` | The verifier window is where S3 closes all handles. The verifier records are unchanged. **S1's observer exists so that S2's in-process reads keep producing `protocol-git` records**, preventing the same 60 s quiet-window defect for reads | O-3 and H-3 pin the record counts. Track B reduces the record count but not the gaps between records (one batch replaces several back-to-back reads), so the quiet window is unaffected. P-6 and P-11 runs also check the C467 method |
| Test-performance standards (`docs/test-performance-standards.md`) | R2 (separate slow components, CARD-1546) | The corpus is one class with backend `[Arguments]`, a template repository built once per class (`[Before(Class)]`) and cloned per test. No AppHost and no database in the corpus. Land rows reuse `TestDbFixture` and `LandingSafetyHarness` as today. Every row is under 30 min (§Performance budget) |
| CARD-1546 (Backlog): git handler into its own csproj | Moves `LandingGit` and friends | If it starts first, Track L files go into the new project and the package reference moves there. C1704 slices and CARD-1546 never run concurrently (same files) |

## 6. Decisions (stated defaults; the plan proceeds under them)

- **D-1 S0 decides Track L, with the thresholds in §2.3.**
  - Rejected: building S2 on the Linux benchmark alone. Linux shows no speed case (A, B), so a Linux-only
    decision would buy a native dependency for test seams that S1 already provides.
  - Rejected: skipping measurement because the operator asked for libgit2. The operator asked for it *because*
    land git looked slow on Windows, and that premise is unmeasured.
- **D-2 S1 is the shared foundation, and it ships whatever S0 says.**
  - Typed reads are the precondition for Track B's batching (a fake keeps the old argv through the default
    method) and for Track L's in-process answers.
  - The observer fixes an existing E2E gap: the stdin path writes no record today.
  - Rejected: C's order, where S0 builds the session before any measurement. The `CliGitSession` null object is
    kept, but it does nothing until S2.
- **D-3 Track B ships B1 first.** It is independent, small, and removes processes on every host. B2 waits for S1
  when both are built, so it is not written twice.
  - Rejected: a long-lived `git cat-file --batch-check` co-process per scope. It would remove the most
    processes, but it holds a child across steps, and that child must be journalled, killed and fenced, which is
    the machinery the land already struggles to keep simple. Its ref-cache staleness across external writes is
    also unproven.
- **D-4 What stays on the CLI permanently in this plan:**
  - rebase and its conflict listing and abort;
  - ls-remote, fetch, push and the lease push (credentials, ssh `core.sshCommand`, promisor, the killable
    budget);
  - worktree add, lock, remove and prune; `reset --hard`, `clean -fdx`, `merge --ff-only` and checkout;
  - status (B: 1.25x slower, no untracked cache or fsmonitor);
  - `worktree list` (its cost is the work itself);
  - whole-ref scans (B: 4.6x slower);
  - the recovery inspection (`check-attr`, `check-ignore`, `ls-files -v`);
  - **all ref writes** (no CAS in the LibGit2Sharp API; a P/Invoke of `git_reference_create_matching` and the
    in-process journal marker of C §3.5 are a later card if S4's numbers ask for it);
  - `git cherry`.

  Rejected: C's S2 write slice inside this plan. It adds a P/Invoke, a journal change and reflog parity for a
  handful of processes (3 `update-ref` plus 1 batch per land).
- **D-5 Fallback per call; a reason code never comes from libgit2.** This is C §3.2. Track B uses the same rule:
  a batched read only confirms, and a refusal re-runs today's commands. Both tracks therefore keep every published
  reason code by construction.
- **D-6 The session's lifetime is one repository-lease episode, owned by `LandRepositoryLeaseScope`** through
  `BeginOperationScope()`. It is disposed before the lease `FileStream` closes. There are two episodes per land
  (before and after verification). Handles are per working directory, and `Release(path)` runs before every
  delete.
  - Rejected: one handle for the whole land including the verification window. While the lease is released,
    another land or a CLI user can repack or move refs, and on Windows a held pack mapping blocks `gc`.
- **D-7 The session is reachable only inside a land scope.** The dispatcher, gated commits, `TaskProgressGit`,
  the monitors, `PublishedBranchDeletionService`, `GitWorkspaceService` and the runner keep the CLI. Test doubles
  never opt in: the backend comes only from injected `LandingSettings`, and the default constructor means `Cli`.
  This is why no existing fault-injecting subclass silently stops injecting.
- **D-8 The backend is a per-host scalar setting, read per land.** No per-request override, no DB column and no
  migration. Lands are serial per host, so host-level canarying is exact.
  - Rejected: C's `delegate.ps1 -Land … -GitBackend`. It needs an API, a DTO and a row field for a canary that a
    setting already gives.
- **D-9 The canary order is server2 `Compare`, then desktop `Compare`, then desktop `LibGit2`,** with the
  criteria in S4. server2 never goes to `LibGit2` by default (Q-1).
- **D-10 The package is pinned exactly at `LibGit2Sharp` 0.32.0**, with libgit2 1.8.6 recorded at startup and
  asserted by N-1. A version bump is its own card that re-runs the corpus on both OSes (Q-6).
- **D-11 Code location:** `server/Infrastructure/Git/LibGit2/` (NFR-01). `GitWorkspaceService` keeps its
  "no LibGit2" decision (Q-5).

## 7. Stop signals (canary, S4; any one stops it: set `Cli` and file a Debug task)

- **S-1** `mismatches > 0` on any land.
- **S-2** `fallbacks` at or above 5% of typed calls over 10 lands, or any session-open fallback on the desktop.
  Either means the repository or ownership shape defeats the session.
- **S-3** The server process dies with libgit2 in the faulting stack (Windows Event Log `Application Error` /
  .NET crash dump), or an access violation is logged.
- **S-4** A worktree delete, `git gc` "Unlink of file … failed", or a `WindowsWorktreeLockDiagnostics` row names
  the server process as the holder.
- **S-5** The land wall excluding verify rises more than 10% over the pre-canary median across 10 lands.
- **S-6** Server private bytes rise more than 300 MB over the pre-canary median (B: 123 MiB private with 5.7k
  refs on one handle).
- **S-7** Any E2E land receipt quiet-window failure whose last record kind is `inproc`.
- **S-8** A land outcome or reason code that differs from the CLI's on the same inputs, in any test or canary
  log.

Rollback is the setting set to `Cli`. It takes effect at the next land, with no restart if it is hot-reloaded,
and at the next start otherwise. The CLI is always present.

## 8. Waiting-input checklist

No slice adds or changes a session that waits for input. S0 is an ordinary Windows Investigate task.

## 9. Recommendation and what the operator must decide

**Order.**
1. **Now (Linux, in parallel):** S1 (Code, after TestDesign) and B1 (Code). Both ship with zero risk to land
   outcomes. S1 lands only as a refactor with `Cli` default; B1 lands as a performance change.
2. **When the qualification run ends (Windows):** S0.
3. **After S0:** apply §2.3. In the expected middle case, B2 comes next, then S0 step 4 is re-run at the B2 tip,
   and only a remaining `spawnPerLand ≥ 5 s` or `≥ 20%` opens S2.
4. S3 and S4 follow S2. S5 follows the canary.

**The operator decides:**
- whether S1 and B1 may land on master as soon as they are reviewed (they are behaviour-neutral or pure process
  savings), or stay on the branch with the rest. The card's text says nothing lands without a decision;
- the GO/NO-GO thresholds (Q-7);
- Q-8: remote proof reuse in cleanup;
- Q-1 to Q-6 below, where the defaults apply unless changed.

## Open questions (each has a default)

- **Q-1 Desktop-only enablement?** Default: yes. `LibGit2` is for the desktop only. server2 runs `Compare` for
  Linux corpus breadth during the canary, then `Cli`. The Linux measurements show no speed case.
- **Q-2 What stays CLI forever?** Default: the D-4 list. The card's "ideally ONE shared handle for the whole
  land" becomes one handle per working directory, for reads only, per lease episode. About 50 to 74 processes
  per land remain on the CLI.
- **Q-3 Compare-mode criteria?** Default: 30 consecutive real lands per host with `mismatches=0`, fallbacks under
  5%, no stop signal, and land wall excluding verify within 10%. Compare costs more than `Cli` (both backends
  run), so it is limited to 14 days per host. After that it either switches or goes back to `Cli`.
- **Q-4 Is the desktop canonical repository a partial clone?** Unknown. The read-only command block in §2.2
  step 1 answers it, and the operator can run it in `C:\src\Antiphon`. Default: `extensions.partialclone`,
  `extensions.refStorage` or any `.promisor` pack means NO-GO for Track L on that host. Converting the
  repository is not part of this card.
- **Q-5 Revisit the `GitWorkspaceService` decision?** Default: no. libgit2 stays inside the land session in
  `Infrastructure/Git/LibGit2` (NFR-01). `GitWorkspaceService` and every other git user keep the CLI. CARD-1546
  decides the project boundary if it runs first.
- **Q-6 Version pin?** Default: exact pin 0.32.0 (libgit2 1.8.6), with the version asserted at startup and in
  N-1. Accept the security lag: upstream 1.8.7 and 1.9.7 have no LibGit2Sharp release. The exposure is limited
  because the session only reads local repositories that our own git writes, and never touches the network.
  A bump is a card with corpus reruns on both OSes.
- **Q-7 The GO/NO-GO thresholds?** Default: §2.3. NO-GO below 2 s and below 10% of land wall excluding
  verify; GO at 5 s or more, or 20% or more, after Track B.
- **Q-8 May cleanup reuse a remote proof taken earlier in the same lease episode (B-5)?** Default: no. These are
  deliberate re-proofs before destructive deletes. If yes, it saves up to 18 processes and 2 network round trips
  per land, which in production may be worth more than any spawn saving (§1, network row).

## 10. Positive controls

| PC | Mutant | Must turn red |
|---|---|---|
| PC-B1 | The .NET ref validator accepts `refs/antiphon/x..y` | P-1 |
| PC-B2 | The path cache ignores a changed `commondir` | P-4 |
| PC-B3 | The path cache survives a lease episode | P-5 |
| PC-B4 | The identity batch failure returns `source_ref_missing` without re-running today's sequence | P-8, P-9 |
| PC-B5 | `ReadRefsAsync` continues past the first missing ref | P-10 |
| PC-L1 | A typed default changes one flag | T-1, T-2 |
| PC-L2 | Typed methods call `ExecuteAsync` instead of the virtual `RunAsync` | T-2, O-3 |
| PC-L3 | No observer call on the stdin path | O-2 |
| PC-L4 | The session is disposed after the lease stream closes | SC-3 |
| PC-L5 | The array value binds as `Cli` | BS-2 |
| PC-L6 | `Missing` on a libgit2 exception | F-5 |
| PC-L7 | Compare uses the libgit2 answer | C-1 |
| PC-L8 | No walk bound | F-4 |
| PC-L9 | No observer call for in-process reads | H-3 |
| PC-L10 | No `Dispose` | W-3 |
| PC-L11 | The `-z` formatter drops the trailing NUL | K-ls-tree, K-diff-tree |
| PC-L12 | Ref values are cached | SH-3 |
| PC-L13 | No invalidation after a fetch | SH-4 |
| PC-L14 | The session is kept across the window | SH-5, SW-2 |
| PC-L15 | No `Release` before a delete | SH-6, SW-1 |

## Performance budget

This applies [docs/testing-and-build.md#performance-standards](../../testing-and-build.md#performance-standards)
and [docs/test-performance-standards.md](../../test-performance-standards.md). No test ran in this task.

**Measured inputs.**
- The Antiphon.Tests build on loaded server2: p50 326 s, p95 579 s (n=117; CARD-0824 amendment receipts).
- `C478_V09a_LandProducerToCaller` alone: 50 s on server2, load 37 to 48, including the fixture (A).
- `PostLandMutationDeliveryTests`: 56 tests in 107 s on the laptop on the git lane (`2b342761f`). Not measured on
  server2.
- C0824 server class per-test p95: 17.2 s loaded (the CARD-1660 35 s basis).
- B's microbench: open plus first read of a `Repository` about 1 ms; a 5,000-ref scratch repository built in
  about 2 s.
- Windows: **unmeasured.** The Windows build and every Windows row are provisional at 15 min and 5 min, and
  S0's step 4 measures the build.

**Selection and graph.**
- Server, infrastructure and land changes select `tests/Antiphon.Tests`. The `EvidenceGit` change (S1) also
  selects `tests/Antiphon.E2E`, with one land method.
- No SessionRunner, Pty or client project changes.
- The E2E land class is `[Category("OptIn")]` with `ProcessSpawnLimit`. Its server2 feasibility without Chromium
  is TestDesign's to confirm. If it needs a browser, CP-6 moves to the Windows lane.

**Shared setup.**
- The corpus uses one template repository set per class (`[Before(Class)]`): plain, linked worktrees, 5,000 refs,
  partial clone and extension repositories. Each test clones it with `--shared` (about 0.2 s), or uses
  `git worktree add` for worktree cases.
- No AppHost, no container and no database in the corpus or the observer and session classes.
- Land rows reuse `TestDbFixture` (once per assembly) and `LandingSafetyHarness` as today.
- The 5,000-ref repository is built once per class, never per test.

**Per-test bodies and timeouts (provisional; TestDesign replaces each with about 2x measured p95).**

| Class | Body | Timeout | Basis |
|---|---|---|---|
| `C1704TypedReadShapeTests`, `C1704GitObserverTests`, `C1704SessionScopeTests`, `C1704BackendSettingTests` | ≤ 2 s (fakes; T-3 and O-3 run the harness land, ≤ 50 s) | 10 s; 120 s for T-3/O-3 | A's 50 s land, about 2x |
| `C1704ContractTests` (per case per backend) | ≤ 3 s | 15 s | Scratch git fixtures, like the C1677 classes (2 to 4 min for 9 to 15 cases) |
| `C1704ProcessReductionTests` | ≤ 3 s; P-6/P-11 ≤ 50 s | 15 s; 120 s | Same |
| `C1704LibGit2*` (portable) | ≤ 3 s; land H-n ≤ 50 s | 15 s; 120 s | Same |
| Windows-only classes | ≤ 20 s | 60 s | One process per test under `ParallelLimiter<GitProcessSpawnLimit>`; Windows git spawn is unmeasured |

**Phases recorded per row:** build, fixture/template setup, bodies and teardown. Slot wait is reported
separately and not counted. TRX per row with per-test durations. The P-6, P-11 and H-2 process counts are
written into the test output, so every run records process count against time.

**Runs.** Every After group is one checkpoint run under 30 min. None is expected over 20 min.

| Group | Lane | Run time (estimate) |
|---|---|---|
| B1 | portable | build about 6 to 10 min, plus rows about 8 min |
| S1 | portable | build about 6 to 10 min, plus rows about 15 min; the E2E build about 5 min, plus 1 method about 3 min |
| S1w | windows | build 15 min (provisional), plus rows 5 min |
| B2 | portable | build, plus rows about 15 min |
| S2 | portable | build, plus rows about 15 min (the corpus twice) |
| S2w | windows | build, plus rows about 8 min |
| S3 | portable | about 12 min |
| S3w | windows | build, plus rows about 12 min |
| S4 | portable | about 10 min |

**Production budgets.**
- S2's in-process cost per land is at most 0.3 s on either OS: about 150 calls at at most 1 ms, plus about 3
  opens at about 1 ms. Above that, the profile's `gitSeconds` for in-process calls flags it.
- The canary stop is at +10% land wall excluding verify (S-5).
- S0's own measurement is about 2 h of the desktop, outside any qualification run.

### Checkpoints

**Lanes.**
- `portable`: default placement. `GET /api/runner-defaults` gives `globalRunnerId=server2`, so omit `-Runner`
  and `-Platform`.
- `windows`: pass `-Platform Windows` and do not pin a runner (`GET /api/session-runners`: `desktop` is the only
  Windows runner, with capacity 2). Windows rows run only after the qualification run ends, like S0.

**Builds.** Each build is a fresh `bin-` custody output with a trailing forward slash, through the build-slot
gate. Each After group has its own builds, because the import refuses a build reused across groups. The Windows rows
have their own After groups (`S1w`, `S2w`, `S3w`): each is a separate checkpoint run on the Windows lane, after
its portable group is green at the same commit. Rows name at most four land classes (the import advisory).
S0 has no rows: it is an Investigate task.

**Filters.** Every OR operand has parentheses and a trailing `*`, with the pipe escaped.

**Validated.** `import --plan` imports all 17 rows with no warning at this commit (checkpoint tool built through
the build slot; no test ran). TestDesign re-runs
`dotnet run --project tools/Antiphon.Checkpoints -- import --plan docs/superpowers/plans/2026-11-04-card-1704-libgit2sharp-plan.md`,
sets `Min` from the counted `[Arguments]` cases, and pins the harness process-count numbers.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | B1 | tests/Antiphon.Tests -> bin-c1704-b1/ | portable-c1704-process-reduction-b1 | /*/Antiphon.Tests.Infrastructure/C1704ProcessReductionTests/* | P-1..P-6 | class, 0 failed/skipped | 6 | 4 | true |
| CP-2 | B1 | CP-1 | portable-landing-git-regression-b1 | /*/Antiphon.Tests.Infrastructure/(LandingGitTests*)\|(LandingGitProcessSavingTests*)\|(LandingGitProfileTests*)/* | B1 regression | 3 existing classes, 0 failed/skipped | 52 | 6 | true |
| CP-3 | S1 | tests/Antiphon.Tests -> bin-c1704-s1/ | portable-c1704-s1-new | /*/Antiphon.Tests.Infrastructure/(C1704TypedReadShapeTests*)\|(C1704GitObserverTests*)\|(C1704SessionScopeTests*)\|(C1704BackendSettingTests*)/* | T-1..T-3, O-1..O-4, SC-1..SC-4, BS-1..BS-3 | 4 classes, 0 failed/skipped | 14 | 8 | true |
| CP-4 | S1 | CP-3 | portable-c1704-contract-cli | /*/Antiphon.Tests.Infrastructure/C1704ContractTests/* | K-1..K-14 (Cli) | class, 0 failed/skipped | 14 | 5 | true |
| CP-5 | S1 | CP-3 | portable-landing-git-regression-s1 | /*/Antiphon.Tests.Infrastructure/(LandingGitTests*)\|(LandingGitBatchingTests*)\|(LandingGitProcessSavingTests*)\|(LandingGitProfileTests*)/* | typed reads inside LandingGit unchanged | 4 existing classes, 0 failed/skipped | 56 | 6 | true |
| CP-16 | S1 | CP-3 | portable-land-regression-s1 | /*/*/(LandWorkspaceTests*)\|(PostLandMutationDeliveryTests*)\|(AgentTaskLandCheckpointMatrixTests*)\|(AgentTaskLandFailureDiagnosticTests*)/* | call-site migration unchanged | 4 existing classes, 0 failed/skipped | 54 | 8 | true |
| CP-6 | S1 | tests/Antiphon.E2E -> bin-c1704-s1e/ | portable-e2e-land-receipt-s1 | /*/*/AgentTaskLandDeliveryE2ETests/C467_V29_RealOutcomeProducerMatrix_Batch2 | EvidenceGit on the observer | method, 0 failed/skipped | 1 | 8 | true |
| CP-7 | S1w | tests/Antiphon.Tests -> bin-c1704-s1w/ | windows-land-regression-s1 | /*/*/(LandWorkspaceTests*)\|(AgentTaskLandHalfResetWindowsTests*)\|(AgentTaskLandRegistrationSafetyWindowsTests*)/* | moved call sites on Windows paths | 3 existing classes, 0 failed/skipped | 13 | 12 | true |
| CP-8 | B2 | tests/Antiphon.Tests -> bin-c1704-b2/ | portable-c1704-process-reduction-b2 | /*/Antiphon.Tests.Infrastructure/C1704ProcessReductionTests/* | P-1..P-11 | class, 0 failed/skipped | 11 | 5 | true |
| CP-9 | B2 | CP-8 | portable-identity-regression-b2 | /*/*/(LandingGitBatchingTests*)\|(AgentTaskLandIdentityMatrixTests*)\|(AgentTaskLandCleanupSafetyTests*)/* | batched reads keep reason codes | 3 existing classes, 0 failed/skipped | 17 | 6 | true |
| CP-17 | B2 | CP-8 | portable-land-regression-b2 | /*/*/(AgentTaskLandRemovalMatrixTests*)\|(PostLandMutationDeliveryTests*)/* | cleanup pin reads and the harness land unchanged | 2 existing classes, 0 failed/skipped | 38 | 8 | true |
| CP-10 | S2 | tests/Antiphon.Tests -> bin-c1704-s2/ | portable-c1704-libgit2 | /*/Antiphon.Tests.Infrastructure/(C1704ContractTests*)\|(C1704LibGit2FallbackTests*)\|(C1704LibGit2CompareTests*)\|(C1704LibGit2NativeLoadTests*)\|(C1704LibGit2LandTests*)/* | K-1..K-14 x2, F-1..F-8, C-1..C-5, N-1, N-2, H-1..H-3 | 5 classes, 0 failed/skipped | 32 | 12 | true |
| CP-11 | S2 | CP-10 | portable-land-regression-s2 | /*/*/(LandingGitTests*)\|(PostLandMutationDeliveryTests*)\|(AgentTaskLandCheckpointMatrixTests*)/* | Cli default unchanged with the package present | 3 existing classes, 0 failed/skipped | 78 | 10 | true |
| CP-12 | S2w | tests/Antiphon.Tests -> bin-c1704-s2w/ | windows-c1704-libgit2 | /*/Antiphon.Tests.Infrastructure/(C1704LibGit2WindowsTests*)\|(C1704LibGit2NativeLoadTests*)\|(C1704ContractTests*)/* | W-1..W-4, N-1, N-2, corpus on NTFS | 3 classes, 0 failed/skipped | 20 | 14 | true |
| CP-13 | S3 | tests/Antiphon.Tests -> bin-c1704-s3/ | portable-c1704-shared-handle | /*/Antiphon.Tests.Infrastructure/(C1704SharedHandleTests*)\|(C1704LibGit2LandTests*)/* | SH-1..SH-8, H-1..H-3 | 2 classes, 0 failed/skipped | 11 | 8 | true |
| CP-14 | S3w | tests/Antiphon.Tests -> bin-c1704-s3w/ | windows-c1704-shared-handle | /*/Antiphon.Tests.Infrastructure/C1704SharedHandleWindowsTests/* | SW-1..SW-3 | class, 0 failed/skipped | 3 | 12 | true |
| CP-15 | S4 | tests/Antiphon.Tests -> bin-c1704-s4/ | portable-c1704-canary-telemetry | /*/Antiphon.Tests.Infrastructure/(C1704CanaryTelemetryTests*)\|(LandingGitProfileTests*)/* | CT-1..CT-5, profile line unchanged otherwise | 2 classes, 0 failed/skipped | 14 | 6 | true |

**Roster check** (grep of `class C1704*` and the existing operands in `tests/` at `14703405`).
- No `C1704*` class exists yet.
- The new names are `C1704{TypedReadShape,GitObserver,SessionScope,BackendSetting,Contract,ProcessReduction,
  LibGit2Fallback,LibGit2Compare,LibGit2NativeLoad,LibGit2Windows,LibGit2Land,SharedHandle,SharedHandleWindows,
  CanaryTelemetry}Tests`.
- No full new name is a prefix of another. `C1704SharedHandleTests*` cannot match `C1704SharedHandleWindowsTests`,
  because the names continue `…HandleT…` and `…HandleW…`. The `C1704LibGit2*` names diverge at the next letter.
- Existing operands, each matching exactly one class:
  - `LandingGitTests` (39 methods) does not match `LandingGitFixture`;
  - `LandingGitProcessSavingTests` (4), `LandingGitBatchingTests` (4) and `LandingGitProfileTests` (9);
  - `LandWorkspaceTests` (7) is in `Antiphon.Tests.Infrastructure`;
  - `PostLandMutationDeliveryTests` (35 methods over 2 files; it does not match `PostLandMutationDeliveryWorkerTests`);
  - `AgentTaskLandCheckpointMatrixTests` (4), `AgentTaskLandFailureDiagnosticTests` (8),
    `AgentTaskLandIdentityMatrixTests` (5), `AgentTaskLandCleanupSafetyTests` (8),
    `AgentTaskLandRemovalMatrixTests` (3), `AgentTaskLandHalfResetWindowsTests` (4) and
    `AgentTaskLandRegistrationSafetyWindowsTests` (2) are all in `Antiphon.Tests.Application`.
- The namespace segment is `*` wherever a row mixes namespaces.
- `Min` is the `[Test]` method count. `[Arguments]` cases raise the real count.
- New Windows-only methods need `WindowsUnitSkipRoster` entries, and the portable rows must not select them,
  because a skipped test fails validation (`skipped>0` is `row_failed`).
