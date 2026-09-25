# CARD-0688: land in one dedicated land worktree — source as a ref, master only in the main checkout, no worktree walks

Plan date: 2026-09-25. Plan task: `ee01d18e` (Frontier, server2 Linux runner, worktree).
Code and `origin/master` inspected at `ada146ea6904838325995335dc3b7382fd08d262`, which is also what the
desktop serves (`GET /api/version` read 2026-09-25, `land-v2`), so CARD-0642 R1 (lease-scoped cache,
`IdentityAndStatus` inspections, live target listing before `update-ref`) and CARD-0664 (consumer
Launch-row releases) are both in the code this plan changes.
Card `0a62339c-aea3-4ac2-85fb-f0b3cedc3e44` (CARD-0688), board `8988ca03-7414-47ad-b0b6-51556c701703`.
Evidence: the source files named below, the CARD-0642 plan
([land speed](2026-09-24-card-0642-land-speed-plan.md)), the R1 verification commits (`520d9afc`,
`52baaf28`, `1e4554ab`: the `Land git profile` lines), and the desktop API (`GET /api/stage-outcomes`,
`GET /api/agent-tasks/{id}`) read 2026-09-25 01:40Z–02:10Z. No code was changed, no build or test was run,
no database was written.

The deliverable is three bounded, sequential Code rounds. The verification design is folded into this
plan (the dispatch says `Next stage: Code`).

## Summary

The card's model is right and the code confirms it: a land needs the reviewed commit (a ref in the shared
object store), one clean place to rebase and build, and one known path to fast-forward afterwards. Today
the protocol instead treats the task's own worktree as the source (so it inspects it 19 times per land),
rebases and builds inside it (so it must be clean, unique, unlocked, on its branch, with no sequencer),
advances the *local* `master` before the push through whichever worktree a registration walk says has it
checked out, and pushes last. That is where the 7 `worktree list` calls, the 19 inspections, the 37
remote round trips and roughly 600 git processes per land come from, and on the desktop each spawn costs
about 0.37 s under load: the 12 lands measured tonight spent a median **386 s** of their **521 s**
admission-to-confirmation time outside the rebase and build clocks, and even the "Rebase" stage clock
(median 54 s for a 1–2 s `git rebase`) is mostly recheck time.

The design here: (1) the source is `refs/heads/feat/card-task-<id>` plus the reviewed SHA, read with one
`show-ref` and rechecked with one `show-ref`; the task worktree is not touched until cleanup. (2) One
persistent, locked, detached land worktree per repository under the managed worktree root is reset to the
source SHA at the start of each land; the rebase, the verification build and the push all happen from
there. (3) The rebase base is the **observed `origin/master`**, the push comes **first**, and the canonical
checkout (CARD-0358) is fast-forwarded **after** publication as one known path, refusing to advance (never
refusing the land) when it is dirty, diverged, or when `master` is checked out anywhere else. (4) "master
is checked out only in the main checkout" becomes an explicit invariant, true by construction of
`WorktreeManager` branch names and asserted once per land by reading `<common>/worktrees/*/HEAD` files,
not by `git worktree list`. What stays: the lease, the approval/DB rechecks at every boundary, the recovery
pins, the one-round-trip remote source recheck before each mutation (CARD-0642 D-6, absorbed here), the
remote target observation before the push, and the guarded cleanup machinery (with one contract change:
the branch is deleted at the SHA it actually has, which is no longer the landed SHA).

CARD-0642 R2 is superseded, not composed: its S4 (one-round-trip recheck) is absorbed into R1 here and its
S5 (non-buildable build skip) is re-hosted as R2 here unchanged in design; R2 of CARD-0642 must not be
dispatched separately (D-1). Expected effect after R1: about 200 git processes per land instead of 600,
at most 3 listings (all in cleanup), 2 inspections (cleanup), about 14 remote round trips; land time
becomes rebase + build + push + cleanup, roughly 3–4 minutes built and under 2 minutes with the R2 skip.
The card's "a minute or two" needs R2 (doc-only skip) and R3 (incremental build in the stable worktree),
and the profile line measures each round on real lands.

## Ground truth

| Card / brief assumption | What the code and evidence show | Consequence |
|---|---|---|
| "~47 `git worktree list` calls, 34 canonicalisation sweeps, ~600 git processes per land" (CARD-0642 plan numbers). | Those were pre-R1. At `ada146ea` (R1 active) the V-7 real-git land profile is `processes=603 worktreeList=7 registrationHits=46 canonicalHits=135 inspections=19 remote=37` (commit `1e4554ab`; `520d9afc` measured `gitSeconds=220.49` for the same land on the desktop, i.e. ~0.37 s per spawn). R1 removed the *repeated* listings and sweeps; it did not remove the inspections, rechecks and target checks that spawn the processes. The 7 listings and ~600 processes are itemised below. | The remaining cost is the protocol's shape, not its cache. Only removing the source-worktree inspections and the target-checkout walks changes it (D-2, D-4, D-5). |
| "The build is only ~30 s of it." | Verify stage median tonight is **136 s** for the 9 lands that built (94–215 s); the three `base_unchanged` lands still spent 13–22 s in Verify on rechecks alone. Measured table below. | Build time is real; the doc-only skip (CARD-0642 S5) stays worth doing as R2, and the stable land worktree makes an incremental build possible (R3). |
| "The source commit is already in the shared object store as the task's branch ref ... Read the ref, compare it to the expected SHA, done." | True for a desktop task. For a server2 task the desktop branch can be *behind* the remote when settlement sync did not run (`RemoteWorkspaceService` fast-forwards the desktop worktree at settlement; `AgentTaskLandSourceResolver` lines 193–199 and 234–287 fast-forward it again at land time with `merge --ff-only` inside the worktree). `ObserveSourceAsync` already fetches the remote branch into a `source-observed` pin, so the reviewed objects are local either way. | The ref read is one `show-ref`; a Behind local branch lands from the observed remote SHA without touching the worktree, and cleanup deletes the branch at the SHA it has (D-2, D-6). |
| "Nothing else is touched" during rebase/verify. | Today the rebase runs in `op.WorktreePath` (`AgentTaskLandingProtocol.cs:212`), which moves the task branch to the rebased commit; `VerifyWithObserverAsync` builds in that worktree; cleanup deletes the branch at `VerifiedSourceSha` (`ExpectedDeletionSha`, `GuardedWorktreeRemoval.cs:218`). With a detached land worktree the branch never moves, so the landed SHA and the branch SHA differ. | Cleanup contract change: `ExpectedDeletionSha = SourceLocalSha`, `LandedSha` carried separately for the remote containment proof (D-6). Old published ops keep the old equality. |
| "Updating the canonical checkout (CARD-0358) ... after the push, fast-forward it." | Today the local target advances **before** the push (`TargetAdvanceStarted → LocalTargetAdvanced → PushStarted`), via `merge --ff-only` in whichever registration has `master` (the main checkout) or `update-ref` when none has. A rejected push leaves local `master` ahead of `origin/master`, the exact CARD-0358 divergence. `remote_ahead_or_diverged` refuses when `origin/master` is not an ancestor of local `master`, which is why a stale main checkout refuses every land until someone runs `git pull --rebase`. | Push first, rebase onto the observed `origin/master`, fast-forward the main checkout afterwards; a stale main checkout stops refusing (D-4). |
| "Enforce it at worktree creation." | `WorktreeManager.CreateAsync` only ever checks out `feat/card-<id>` (`BuildBranchName`, `worktree add -b`), and `CreateVerificationAsync` likewise; nothing in Antiphon can put `master` in a linked worktree. The protocol nonetheless searches for it at every boundary (`TargetCheckoutAsync`, 10 calls) because it never trusted the construction. | The invariant is already true by construction; the plan makes it explicit, adds the cheap per-land assertion, and documents it (D-5). No creation-time code change is needed; a guard that cannot go red is not added. |
| "Assert it cheaply by reading `.git/worktrees/*/HEAD` files." | Correct and sufficient: a linked worktree's HEAD file is `ref: refs/heads/<branch>` or a SHA; `<common>/HEAD` is the main checkout's. `git worktree list` reads the same files and additionally stats every directory (1.33 s at 650 registrations, CARD-0641). Locked/prunable state is not needed for the assertion. | `LandWorkspace.ScanHeadFiles` (plain file reads, no git) runs once, immediately before the canonical advance (D-5). |
| "Keep: the lease, the approval/DB rechecks, and the one pre-mutation recheck of the target ref." | The lease scopes everything (`RunRequestAsync` holds it from admission through cleanup); the approval recheck is DB-only; the remote target recheck is `ObserveAsync` (ls-remote + fetch + pin + merge-base) at 3 points. | All kept; target observed once at creation (the rebase base) and once before the push (D-4); `RecheckApprovalAsync` stays at every boundary (D-8). |
| "Consider folding this into CARD-0642 R2, replacing its S4/S5 scope." | R2 edits the same methods this plan rewrites (`RunAsync`, `RecheckRemoteSourceAsync`, the Prepared block, `ControlledLandingGit`). S4's one-round-trip recheck is exactly what the new protocol wants before each mutation; S5's diff-based skip is independent of where the rebase runs. | Supersede: S4 into R1 here, S5 as R2 here, docs merged; CARD-0642 closes on R1 (D-1). |
| A persistent worktree under the managed root would be seen by the janitor, the residue tooling and `ListAsync`. | `WorktreeManager.ListAsync`, `ScanDelegateWorktreesAsync` and `PruneStaleAsync` all filter on the `feat/card-` branch prefix and on metadata records; a detached worktree with no metadata is invisible to all three; `git worktree prune` skips a locked worktree. | Land worktree at `<WorktreeBasePath>/land/<hash>`, detached and `worktree lock`ed (D-3), pinned by V-1(e). |
| An interrupted rebase "requires inspection". | `interrupted_rebase_requires_inspection` exists because the rebase mutated the task's worktree. In a disposable land worktree the reset step is the inspection. | An op interrupted at `RebaseStarted` becomes `Refused: interrupted_rebase`; the next request's reset aborts and cleans (D-3). The child journal still gates the lease until any orphaned git child is accounted for. |
| A land's phase timestamps and the profile are enough to judge each round. | `StageOutcomes` carry Rebase/Verify/Cleanup seconds; the profile line carries git counts and seconds; nothing records the land worktree reset, the push, or the canonical advance. | Profile line gains `reset`, `push`, `canonical` seconds and the per-phase wall (D-11); no DTO change. |

### Measurements

Twelve consecutive lands from the desktop API, 2026-09-24 19:26Z to 2026-09-25 00:23Z, all completed
before the desktop could have restarted onto `ada146ea` (committed 00:19Z; the last land ran uninterrupted
to 00:23Z), so these are the CARD-0642 R1
"before" numbers. `start→confirmed` is `landRequest.startedAt` to `landing.remoteConfirmedAt`; stage seconds
are the server `StageOutcome` rows; `other` = start→confirmed − rebase − verify (cleanup runs after
confirmation).

| task | requested (UTC) | start→confirmed s | rebase s | verify s | verify detail | cleanup s | other s | outcome |
|---|---|---:|---:|---:|---|---:|---:|---|
| db094bcc | 09-25 00:16:50 | 323 | 32 | 94 | build OK | 40 | 197 | Landed |
| 2b722beb | 09-25 00:07:22 | 491 | 34 | 163 | build OK | 49 | 294 | Landed |
| 7f644884 | 09-24 23:39:54 | 563 | 45 | 185 | build OK | 53 | 333 | Landed |
| 0506a1cd | 09-24 23:19:13 | 476 | 52 | 107 | build OK | 74 | 317 | Landed |
| f6fff836 | 09-24 23:01:03 | 521 | 56 | 14 | base_unchanged | 79 | 451 | Landed |
| b18bb8e3 | 09-24 22:24:54 | 757 | 62 | 136 | build OK | 29 | 559 | LandedWithResidue |
| db8122fd | 09-24 22:07:43 | 729 | 64 | 138 | build OK | 77 | 527 | Landed |
| 7d58c913 | 09-24 21:34:57 | 522 | 58 | 13 | base_unchanged | 48 | 451 | LandedWithResidue |
| f3e2953f | 09-24 20:36:20 | 472 | 35 | 105 | build OK | 50 | 332 | Landed |
| 303a8c1f | 09-24 20:30:27 | 510 | 84 | 22 | base_unchanged | 74 | 404 | Landed |
| d5219854 | 09-24 19:51:07 | 889 | 84 | 215 | build OK | 110 | 590 | Landed |
| 492a3ddb | 09-24 19:26:17 | 530 | 50 | 112 | build OK | 60 | 368 | Landed |

Medians: start→confirmed **521 s**; rebase stage **54 s** (for one `git rebase`); verify **136 s** when
built (n=9), 13–22 s when skipped; cleanup **57 s**; other **386 s**. The R1 profile on the desktop
(`520d9afc`: `processes=603 gitSeconds=220.49`) says ~220 s of that is git process time; the rest is DB
round trips per checkpoint and process start latency under load.

### Where the 7 listings and ~600 processes come from (at `ada146ea`, fresh rebased no-filter land)

| Source of I/O | `worktree list` | processes (approx.) |
|---|---:|---:|
| First `IdentityAsync` in the resolver (fills the scope cache; every later identity read is a hit: `registrationHits=46`) | 1 | |
| `rebase` invalidates the cache (D-4 own-mutation rule); the post-rebase `prepared` inspection re-lists | 1 | |
| `CheckLiveTargetAsync` before `update-ref`/`merge --ff-only` (R1 repair: always a fresh listing) | 1 | |
| `merge --ff-only`/`update-ref refs/heads/master` invalidates; the `CheckTargetAsync` after the advance re-lists | 1 | |
| `GuardedWorktreeRemoval.RegistrationsAsync` (its own `RunAsync`, never cached by design): before removal, after removal, before branch deletion | 3 | |
| 19 `InspectAsync` (2 resolver + 15 protocol + 2 cleanup `Full`) × ~15: `check-ref-format` ×2, `IdentityAsync` ×2 each `symbolic-ref`, `show-ref --exists`, `show-ref --verify`, `rev-parse HEAD`, `rev-parse <branch>`, `rev-parse --absolute-git-dir`, then `status`; cleanup's two add `ls-files --ignored` | | ~285 |
| 14 `ObserveSourceAsync` (resolver 4, protocol `RecheckRemoteSourceAsync` 10) × ~9: `check-ref-format` ×2, `remote get-url` ×3, `ls-remote`, `fetch`, `rev-parse <pin>`; each leaves a loose `source-recheck/<guid>` ref | | ~126 |
| 10 `CheckTargetAsync` × ~6: `rev-parse <target>`, `rev-parse --absolute-git-dir` (sequencer), `symbolic-ref HEAD`, `status`, `rev-parse HEAD` in the target checkout | | ~60 |
| 15 `RecheckSourceAsync` pin reads (`show-ref --verify` per recorded pin) | | ~35 |
| Target `ObserveAsync` ×3 and cleanup `ObserveAsync` ×2 × ~10 (`check-ref-format`, `symbolic-ref`, `remote get-url` ×4, `ls-remote`, `fetch`, `rev-parse`, `merge-base`) | | ~50 |
| Pins 3 × 5, rebase + HEAD read, target advance, push (+`DestinationAsync`), ancestry, index-lock probes, `worktree remove`, branch delete | | ~45 |
| **Total** | **7** | **~600** (`processes=603`; `remote=37`) |

After R1 of this plan the same land is: 0 protocol inspections, 3 listings and 2 `Full` inspections in
cleanup (unchanged, owned by CARD-0459/0543 and being replaced by CARD-0692), 1 `ObserveSourceAsync`, 4
one-round-trip source rechecks, 2 target observes (+1 on the already-present path), 2 cleanup observes, 1
push, ~9 processes for the land worktree reset, ~7 for the canonical advance, 3 pins, rebase, and the pin
reads: about **200 processes**, `worktreeList ≤ 3`, `inspections ≤ 2`, `remote ≤ 16`, no loose recheck
refs. At 0.37 s per spawn that is ~75 s of git, of which cleanup is ~25 s.

## Invariants the walks enforce today, and how each holds under the new design

| # | Invariant (today's refusal) | Enforced today by | Under this plan |
|---|---|---|---|
| I-1 | The source is the reviewed commit (`reviewed_source_mismatch`) | Worktree inspection: `snapshot.HeadSha == ExpectedSourceSha` | `show-ref --verify --hash <sourceFullRef>` in `RepositoryPath` equals the expected SHA (Equal/LocalAhead), or the observed remote equals it (Behind), or the derivation rule of D-7. One process. |
| I-2 | The source does not move during the land (`source_changed`) | 15 inspections comparing HEAD, branch, registration head and paths | Branch tip re-read (`show-ref`) at every existing `RecheckSourceAsync` point equals `SourceLocalSha`. One process per checkpoint. |
| I-3 | The source worktree is the unique registered checkout of the branch, unlocked, not prunable, in this repository (`registration_mismatch`, `registration_unavailable`, `wrong_repository`) | Registration walk inside `IdentityAsync` | Not a landing precondition: nothing is mutated there. Cleanup keeps the full check unchanged (`GuardedWorktreeRemoval` `Full` inspection). |
| I-4 | The worktree HEAD is the branch, not detached (`detached_head`, `source_branch_mismatch`) | `symbolic-ref HEAD` in the worktree | Same as I-3: cleanup only. |
| I-5 | Clean tree, no sequencer where the rebase runs (`source_dirty`, `active_sequencer`) | `status`, sequencer files in the source worktree | The land worktree is reset to a clean, detached, sequencer-free state at `InputSha` before each rebase (D-3); a dirty task worktree is a cleanup concern (non-forcing `worktree remove` still refuses it). |
| I-6 | The remote source branch is what was reviewed and has not moved (`source_remote_changed`, `source_remote_missing`) | `ObserveSourceAsync` at resolution and 10 protocol rechecks (fetch + pin each) | One authoritative `ObserveSourceAsync` at resolution (fetch keeps the objects local), then the one-round-trip `RecheckSourceRemoteAsync` at protocol entry, before the land-worktree reset/rebase, and before the push (D-8). |
| I-7 | Approval latch, request identity, filter unchanged (`RecheckFinalVerificationAsync`, `stale_land_request`, `resume_approval_changed`, `verification_filter_changed`) | DB reads at every boundary | Unchanged, at every boundary (`RecheckApprovalAsync`). |
| I-8 | Recovery pins unchanged (`recovery_pin_changed`) | `show-ref` per pin at every `RecheckSourceAsync` | Unchanged. |
| I-9 | The rebase base is the target and it has not moved (`target_changed`, `remote_changed_before_push`) | Local `master` == `TargetBeforeSha` at 10 boundaries; remote observed at 3 | The base is the **observed** `origin/master` (`TargetBeforeSha`), pinned as `target-before`; re-observed once immediately before the push. Local `master` is not a precondition during the land (D-4). |
| I-10 | The remote is not ahead of the local target (`remote_ahead_or_diverged`) | `merge-base --is-ancestor remote local` | Inverted: local `master` must be an ancestor of, or equal to, the observed remote (`target_local_ahead` refusal at op creation); a stale main checkout is normal and is fast-forwarded after publication. |
| I-11 | The target checkout is unique, clean, on the target, sequencer-free, and never mutated under a foreign worktree (`ambiguous_target_checkout`, `target_registration_unavailable`, `target_checkout_changed`, `target_dirty_or_unknown`, `target_active_sequencer`; live listing before `update-ref`) | Registration walk + checkout reads at 10 boundaries + the live listing | Only at the canonical advance, after publication: the HEAD-file scan (no git) proves `master` is checked out nowhere but the main checkout; the main checkout's lock, sequencer, dirtiness and ancestry are read once; any failure is a recorded residue reason (`canonical=<reason>`), never a publication refusal and never an `update-ref`/`merge` under a foreign checkout (D-4, D-5). |
| I-12 | No `index.lock` in a checkout the land is about to mutate (admission hold and pre-mutation refusals) | Source-worktree probe plus a registration listing to find the target checkout | Probes the land worktree and the main checkout (found by `<common>/HEAD`, no listing); the source-worktree probe is kept because cleanup still mutates it (D-10). |
| I-13 | Only this invocation aborts the rebase it started; an interrupted rebase needs inspection | `ChildOperation`/PID journal; `interrupted_rebase_requires_inspection` | The land worktree is disposable: an op at `RebaseStarted` on resume becomes `Refused: interrupted_rebase`; the reset step of the next request aborts/cleans under the lease. `RepositoryChildJournal.HasUnfinishedAsync` still refuses the lease while an orphaned git child is unaccounted for. |
| I-14 | Publication is monotonic; cleanup authority is the publication receipt, the pins and remote containment | `AgentTaskLandingState.HasPublication`, `GuardedWorktreeRemoval.AuthorityAsync` | Unchanged; containment is proven for `LandedSha` (= `VerifiedSourceSha`) while the branch/worktree identity is `ExpectedDeletionSha` (= `SourceLocalSha`) (D-6). |
| I-15 | Cleanup never deletes unpushed work | Branch delete CAS at the SHA the land verified; sibling `git cherry` warning | Branch delete CAS at `SourceLocalSha`; the remote contains `VerifiedSourceSha`, which carries every reviewed patch by construction of the rebase. Sibling warning unchanged. |
| I-16 | **`master` is checked out only in the main checkout** (new, explicit) | Nothing (searched for at every boundary instead) | True by construction (`WorktreeManager.BuildBranchName` and `CreateVerificationAsync` only check out `feat/card-*`), asserted per land by the HEAD-file scan (`target_checked_out_elsewhere`), and written into AGENTS.md as a local-stack rule. |

## Decisions

- **D-1 — Supersede CARD-0642 R2; do not dispatch it.** S4 (`ILandingGit.RecheckSourceRemoteAsync`,
  `LandingSourceRecheck`, the four reasons, the fail-closed default; CARD-0642 D-6/D-8 and its V-14) is
  absorbed verbatim into R1 here with the placement re-specified for the new mutation set. S5 (the
  `Delegation:LandNonBuildableGlobs` allowlist, the two delta skips, `LandVerificationInputs`, the state
  arms; CARD-0642 D-1/D-2 and V-10..V-12) is R2 here unchanged in design, with the verifier's worktree
  argument being the land worktree. S6 docs merge into S8 here. Rejected: composing (R2 and this card both
  rewrite `RunAsync`, `RecheckRemoteSourceAsync`, the Prepared block and `ControlledLandingGit`; the second
  to land would rebase onto a protocol that no longer has the code it edited), and dropping S5 (doc-only
  lands still build for 94–215 s; the skip is orthogonal and already designed). The orchestrator records
  on CARD-0642 that R2 moved here and closes it on R1.

- **D-2 — The source is a ref plus the reviewed SHA; the task worktree is never read or mutated before cleanup.**
  Resolution reads `show-ref --verify --hash <sourceFullRef>` in `RepositoryPath` (`SourceLocalSha`),
  observes the remote once with the existing `ObserveSourceAsync` (fetch + pin: the reviewed objects are
  then local even when the desktop branch is behind), classifies with the existing `merge-base` rules,
  and accepts: Equal or LocalAhead when `expected == local`; Behind when `expected == observed` (**no**
  fast-forward of the local worktree, no `source-ff` child, no `AdvanceStarted` state); Diverged refuses as
  today. The op records `WorktreePath`, `CommonDirectory` and `GitDirectory` for cleanup exactly as today,
  with `GitDirectory` read by one `rev-parse --absolute-git-dir` in the task worktree when its directory
  exists and derived as `<common>/worktrees/<basename>` when it does not (cleanup only compares it while
  the directory exists; a land whose desktop worktree is already gone now lands and cleans up clean
  instead of refusing `registration_mismatch`). Every `RecheckSourceAsync` keeps the DB coordinate check
  and the pin reads and replaces the inspection with one `show-ref` equality against `SourceLocalSha`.
  Rejected: keeping the local fast-forward for Behind (it needs the inspections the card removes, and
  settlement sync already does it in the normal case), and reading the ref through the task worktree
  (same ref, extra path dependency).

- **D-3 — One persistent, locked, detached land worktree per repository; reset before every rebase; disposable.**
  New `ILandWorkspace` (Application/Interfaces) with `LandWorkspace` (Infrastructure/Git) over
  `ILandingGit`: `PathFor(common)` = `<worktree root>/land/<first 12 hex of SHA-256(canonical common dir)>`
  where the root is `GitSettings.WorktreeBasePath` resolved exactly as `WorktreeManager.ResolveWorktreeRoot`
  (factored into a shared static), so the desktop uses `C:\Antiphon\worktrees\land\<hash>` and every test
  fixture that sets `WorktreeBasePath` to its own root stays fixture-owned. `EnsureAsync(repository, path,
  sha, started, ct)` runs under the land lease: (1) if `<path>/.git` is absent, `worktree add --detach
  --force --force <path> <sha>` (the double force is git's documented override for a registered-but-missing
  or locked path, so a deleted directory heals without `worktree prune`); (2) otherwise verify
  `rev-parse --path-format=absolute --git-common-dir` in the path equals the repository's common dir
  (`land_worktree_foreign`), refuse on a live `index.lock` (existing `GitIndexLock` codes), abort any
  sequencer it finds (`rebase --abort`, `merge --abort`, `cherry-pick --abort`; still present →
  `land_worktree_sequencer_stuck`), then `reset --hard <sha>` and `clean -fdx` as owned children; (3)
  confirm `rev-parse HEAD == sha`, `symbolic-ref -q HEAD` exits 1 (detached) and `status --porcelain=v1 -z
  --untracked-files=all` is empty; (4) lock it once (`worktree lock --reason antiphon-land <path>` when
  `<common>/worktrees/<basename>/locked` is absent; a plain file check, no listing). The worktree is
  invisible to `ListAsync`, `ScanDelegateWorktreesAsync`, `PruneStaleAsync` and the residue tooling (all
  filter on the `feat/card-` prefix or on metadata) and immune to `git worktree prune` (locked). Rejected:
  a worktree inside the `.git` directory (unsupported layout), a fresh worktree per land (creation is the
  cost the card removes and it forfeits R3), a branch-attached land worktree (it would put a branch's
  checkout somewhere the invariant does not expect), and `worktree prune` in the heal path (it prunes every
  missing registration, which is CARD-0459's decision to make).

- **D-4 — Rebase onto the observed `origin/master`; push first; fast-forward the canonical checkout after publication; failure there is residue, never a refusal.**
  At op creation the resolver observes the target once (`ObserveAsync`, unchanged: ls-remote + fetch + pin
  + ancestry) and records `TargetBeforeSha = RemoteBeforeSha = observed`, plus the new `LocalTargetBeforeSha`
  (`rev-parse refs/heads/master`), requiring `merge-base --is-ancestor local observed` (else
  `target_local_ahead`: unpushed commits on local `master` are an operator situation, not a land's). The
  `target-before` pin records the observed SHA. Phases: `Inspected → RecoveryPinned → RebaseStarted →
  Prepared → Verified → PushStarted → PublicationConfirmed → CleanupStarted → Complete`; the
  `TargetAdvanceStarted`/`LocalTargetAdvanced` values stay in the enum for schema-2 rows and are never
  written by schema 3. Before the push the target is re-observed and must equal `TargetBeforeSha` or
  already contain `VerifiedSourceSha` (idempotent resume after an interrupted confirmation); the push is
  the plain `push <endpoint> <sha>:refs/heads/master` as today (a non-fast-forward is rejected by the
  remote; the pre-push observation is the CAS). After `PublicationConfirmed`, the canonical advance step
  (not a phase; columns `CanonicalAdvanceStartedAt`, `CanonicalAdvancedAt`, `CanonicalAdvanceReason`,
  `TargetCheckoutPath`/`TargetCheckoutRecorded`): run the HEAD-file scan (D-5); the main checkout is
  `Path.GetDirectoryName(common)` when the common dir is named `.git` (never `RepoPath`, which may itself
  be a linked worktree); if `<common>/HEAD` is `ref: <target>`, that checkout must have no `index.lock`,
  no sequencer, an empty `status`, and a HEAD that equals `VerifiedSourceSha` (already advanced) or is an
  ancestor of it, then `merge --ff-only <VerifiedSourceSha>` as an owned child, confirmed by `rev-parse
  HEAD`; if `master` is checked out nowhere, `update-ref --no-deref refs/heads/master <Verified>
  <current>` with the same ancestry rule. Reasons `target_checked_out_elsewhere`, `canonical_index_lock`,
  `canonical_active_sequencer`, `canonical_checkout_dirty`, `canonical_diverged`, `canonical_advance_failed`
  are recorded on the op, appended to the outcome line as `canonical=<advanced|already|reason>`, turn the
  terminal event into `LandedWithResidue` with a `Warning` event naming the checkout and the fix
  (`git pull --rebase` there, then `restart-apphost.ps1`, per the CARD-0358 runbook); publication is never
  reverted. Rejected: `--force-with-lease` on the push (the pushed commit descends from the observed tip,
  so a plain push already fails on any movement; the lease adds a second CAS with no new guarantee and a
  new failure mode), advancing local `master` before the push (the CARD-0358 divergence), and refusing the
  land when the main checkout is dirty (the checkout is an activation concern; the restart script already
  refuses to serve a SHA that is not HEAD).

- **D-5 — "master only in the main checkout" is an invariant, true by construction and asserted per land without git.**
  `LandWorkspace.ScanHeadFiles(common)` enumerates `<common>/worktrees/*/HEAD` (plus each `gitdir` file
  for the path in the residue message) and `<common>/HEAD`, parsing `ref: <name>` or a SHA. Immediately
  before the canonical advance, any linked worktree whose HEAD is the target ref yields
  `target_checked_out_elsewhere` (residue, D-4). The construction proof is `WorktreeManager.BuildBranchName`
  (`feat/card-<id>`) and `CreateVerificationAsync`, covered by existing `WorktreeManagerTests`; AGENTS.md
  gains one local-stack line and `docs/bootstrap.md` names the invariant next to the CARD-0358 gotcha.
  Rejected: a creation-time guard in `WorktreeManager.CreateAsync` (it cannot go red: the branch is built
  from a prefix), and trusting the construction with no assertion (the scan costs ~650 file reads, tens of
  milliseconds, and turns a hand-made `git worktree add ../x master` into a named residue instead of a
  corrupted checkout).

- **D-6 — Cleanup contract: the branch is deleted at the SHA it has; containment is proven for the landed SHA.**
  `AgentTaskLanding.SourceLocalSha` (new) is the branch tip at op creation; at `CleanupStarted`,
  `ExpectedDeletionSha = SourceLocalSha` for schema 3 (schema 1/2 keep `= VerifiedSourceSha`).
  `WorktreeRemovalRequest` gains `string? LandedSha = null`; the protocol passes `ExpectedSourceSha:
  op.ExpectedDeletionSha, LandedSha: op.VerifiedSourceSha`. `GuardedWorktreeRemoval.AuthorityAsync`
  (Publication) checks `op.ExpectedDeletionSha == request.ExpectedSourceSha` (unchanged) and
  `op.VerifiedSourceSha == (request.LandedSha ?? request.ExpectedSourceSha)`, and observes the remote for
  `LandedSha ?? ExpectedSourceSha`. `CompleteBranchAsync` deletes at `ExpectedSourceSha` (unchanged). The
  cleanup journal (`IWorktreeCleanupJournal.GetOrCreateAsync`) receives `ExpectedDeletionSha` where it
  received `VerifiedSourceSha`; `WorktreeGuardedCleanup` threads `LandedSha` through unchanged otherwise.
  `AgentTaskLandingState.Transition (PublicationConfirmed → CleanupStarted)` requires `ExpectedDeletionSha ==
  (SchemaVersion == 3 ? SourceLocalSha : VerifiedSourceSha)`. Rejected: moving the task branch to the
  landed SHA after publication (a branch moved under a checked-out worktree leaves that worktree's index
  and tree at the old commit, i.e. dirty, and cleanup would then refuse it), and deleting the branch by
  patch containment (`git cherry`) instead of a SHA CAS (the CAS is the unpushed-work guarantee).

- **D-7 — Schema version 3 for new operations; schema-2 operations are resumed only where the old and new protocols coincide.**
  New nullable columns on `AgentTaskLandings`: `SourceLocalSha`, `LandWorktreePath`, `LocalTargetBeforeSha`,
  `CanonicalAdvanceStartedAt`, `CanonicalAdvancedAt`, `CanonicalAdvanceReason` (one additive EF migration,
  `AddDedicatedLandWorktree`, no backfill). `HasIdentity` for schema 3 additionally requires
  `IsOid(SourceLocalSha)`, `IsOid(LocalTargetBeforeSha)` and a non-empty `LandWorktreePath`. At protocol
  entry a schema-2 op that `HasPublication` follows the cleanup path unchanged (cleanup-only retries of
  old lands keep working); an unpublished schema-2 op at `LocalTargetAdvanced` or `PushStarted` resumes
  through the new push step (its local `master` is already advanced, so the canonical step records
  `already`); an unpublished schema-2 op at any other phase is refused `landing_schema_superseded` and the
  request terminates `LandRefused` with that reason, so the orchestrator re-runs `-Land`. Because the old
  rebase moved the branch, the resolver keeps today's derivation acceptance as a read-only rule: when
  `local != expected` and the task's active op has `RebasedSourceSha == local && OriginalSourceSha ==
  expected`, the new op records `PreparationInputSha = local` and `PreviousPreparationOperationId`; the
  new protocol never creates that situation itself, so the rule is dead once no schema-2 unpublished op
  remains (follow-up). `AgentTaskLandRequest.SourceResolutionState.AdvanceStarted` is never written by
  schema 3 and a request found in it refuses `landing_schema_superseded`. Rejected: a feature flag keeping
  both protocols (two protocols to test, two to reason about; rollback is a build rollback, see Migration),
  and refusing every unpublished schema-2 op (an op between local advance and push would leave local
  `master` ahead of origin, the CARD-0358 state).

- **D-8 — Recheck placement.** `RecheckApprovalAsync` (DB: latch, request identity, filter) at every
  boundary where `RecheckRemoteSourceAsync` runs today (unchanged count). The one-round-trip
  `RecheckSourceRemoteAsync` at: protocol entry for an existing op, immediately before the land-worktree
  reset that precedes the rebase, immediately before `ConfirmAsync` on the already-present path, and
  immediately before the pre-push `ObserveAsync` (four; the resolver's post-checkpoint recheck also uses
  it, so the resolver keeps exactly one fetching `ObserveSourceAsync`). `RecheckSourceAsync` (DB + pins +
  one `show-ref`) stays at every point it runs today. The local target is not rechecked during the land
  (I-9). Rejected: fewer approval rechecks (DB reads are cheap and are the CARD-0544 D-5 guarantee), and
  caching the remote answer (a branch pushed after review must refuse).

- **D-9 — The verification build runs in the land worktree; R1 keeps the fresh temp artifacts path, R3 makes it stable.**
  `verifier.VerifyAsync(op.LandWorktreePath, filter, correlation, ct)`; `VerifyWithObserverAsync` gains an
  optional `artifactsPath` argument (default: today's `%TEMP%\antiphon-land-verify-<guid>`), carried on
  `LandingVerificationCorrelation` as `ArtifactsPath`. In R3 the protocol passes
  `<land worktree>-artifacts` (a sibling directory, outside the tree so `clean -fdx` never sees it), and
  the verifier deletes any prior `landing-verification.trx` under it before the test run so the fresh-report
  rule holds. CARD-0642 D-3 rejected a stable path because every land built from a different worktree
  path; with one path MSBuild's incrementality applies. R3 is dispatched only if the R1/R2 profile lines
  show Verify still dominating. Rejected in R1: bundling the artifacts change with the protocol rewrite
  (separate risk, separately measurable).

- **D-10 — Admission index-lock probes without a listing.** `ProbeAdmissionIndexLockAsync` probes the
  task worktree (kept: cleanup still mutates it, and CARD-0543 tests pin the hold), the land worktree when
  its directory exists, and the main checkout when `<common>/HEAD` names the target (file read, no
  `RegistrationsAsync`). The pre-mutation refusals (`git_index_lock_stale`/`held`) move to the land
  worktree (before reset and rebase) and the main checkout (before the canonical `merge`).

- **D-11 — Measurement: extend the profile line, no schema or DTO change.** `LandingGitProfile` gains
  nothing; `AgentTaskLandService` appends `reset={s} rebase={s} verify={s} push={s} canonical={s}
  cleanup={s}` computed from the op's timestamps (`RebaseStartedAt`, `PreparedAt`,
  `VerificationStartedAt`, `VerifiedAt`, `PushStartedAt`, `RemoteConfirmedAt`, `CanonicalAdvanceStartedAt`,
  `CanonicalAdvancedAt`, `CleanupStartedAt`, `CleanupCompletedAt`; reset = `RebaseStartedAt` − the new
  `LandWorkspaceReadyAt`, a column added with the others). Card exit evidence: three profile lines from
  real desktop lands after each round (one doc-only, two code), each with `processes ≤ 250`,
  `worktreeList ≤ 3`, `inspections ≤ 2`, `remote ≤ 16` after R1, and start→confirmed under 240 s for a
  built land and under 120 s for a skipped one after R2. Rejected: new `landing` DTO fields (the log line
  and `StageOutcomes` already reach `scripts/logs.ps1` and `scripts/stage-value-report.ps1`).

- **D-12 — Fakes and tests evolve deliberately; the refusal reasons that disappear are renamed, not silently dropped.**
  `ControlledLandingGit` models a third worktree `Land` (`<root>/trees/land`, detached) with real
  `trees/land/.git` and `git-common/worktrees/land/{HEAD,gitdir,locked}` files, and `git-common/HEAD`
  (`ref: refs/heads/master`) plus `git-common/worktrees/<name>/HEAD` for its other worktrees so the
  HEAD-file scan reads real files; new validated arms: `worktree add --detach --force --force <path> <sha>`,
  `worktree lock --reason antiphon-land <path>`, `reset --hard <sha>`, `clean -fdx`, `rev-parse`/`status`/
  `symbolic-ref` in the land path (detached), `rebase <sha>` **in the land path moving only the land HEAD**
  (the existing arm that moves the source branch is removed: no production caller rebases the source any
  more), `merge --ff-only <sha>` in `Repository` (canonical), `update-ref --no-deref refs/heads/master
  <sha> <expected>`, and `RecheckSourceRemoteAsync` with `SourceRemoteRechecks`, `OnSourceRecheck(n)` and
  an `ls-remote` trace entry (CARD-0642 D-8). Hooks `RewindSource`, `SwitchSourceBranch`, `LockSource`,
  `SetSourcePrunable`, `MarkSourceSequencer` stay for cleanup tests; `SetAmbiguousTargetRegistrations` also
  writes the alias's HEAD file. Reasons that no longer exist as landing refusals — `source_dirty`,
  `active_sequencer`, `registration_mismatch`, `registration_unavailable`, `detached_head`,
  `source_branch_mismatch`, `wrong_repository`, `interrupted_rebase_requires_inspection`,
  `target_checkout_changed`, `ambiguous_target_checkout`, `target_registration_unavailable`,
  `target_dirty_or_unknown`, `target_active_sequencer`, `remote_ahead_or_diverged`,
  `source_fast_forward_failed`, `source_advance_head_unexpected` (61 references across 30 test files) —
  are each mapped in the Code round's test migration table to their new observable: cleanup residue
  (`ignored_content_preserved`, `source_changed`, `registration_locked`, ...), a canonical residue reason,
  `target_local_ahead`, `interrupted_rebase`, or "lands". Tests keyed on `InspectionCalls`/`InjectInspection`
  ordinals (`AgentTaskLandSourcePersistenceTests`, `AgentTaskLandSourceFreshnessTests`,
  `AgentTaskLandFailureDiagnosticTests`) are re-keyed to `OnSourceRecheck(n)` / `BeforeCommand` on
  `show-ref`. Every existing `V-n` invariant that still holds keeps a test; a test whose invariant no
  longer exists is deleted with the reason in the commit message, never left asserting the old refusal.

- **D-13 — Three sequential Code rounds.** R1 = the protocol (S1–S7); R2 = the non-buildable skip and
  the docs (S8–S9); R3 = the stable artifacts path (S10), conditional on measurement. R2 and R3 each start
  after the previous round has landed and been activated (`GET /api/version`), and the orchestrator
  posts the profile lines on the card between rounds. All three edit `AgentTaskLandingProtocol.cs`.

- **D-14 — Left exactly as it is.** The guarded cleanup's own listings and `Full` inspections
  (CARD-0459/0543; CARD-0692 replaces them); the `RepositoryMutationLease` and the child journal; the
  approval model (CARD-0544); the delivery boundary and notifications (CARD-0641/0467); the land queue and
  sweep; `AgentTaskLandRequest` columns (the `SourceWorktreePath`/`SourceGitDirectory`/
  `SourceCommonDirectory` snapshots are filled from the task and the derived git dir so
  `LandSourceCheckpointPatch` is unchanged).

## Design

### The schema-3 land, end to end

1. **Admission** (`AgentTaskLandService.RunLeasedAsync`): unchanged except D-10 probes. Scope and profile
   as R1 of CARD-0642.
2. **Resolution** (`AgentTaskLandSourceResolver.ResolveAsync`, rewritten): coordinates and lease as today;
   `local = show-ref --verify --hash <source>` (missing → `source_ref_missing`); `observed =
   ObserveSourceAsync(RepositoryPath, source, prefix)`; classify; accept per D-2 (derivation per D-7);
   checkpoint `Observed`; `RecheckSourceRemoteAsync` must equal `observed`; checkpoint `Resolved`;
   `CreateOperationAsync`: `DestinationAsync`, `localTarget = rev-parse <target>`, `remote =
   ObserveAsync(target, expected)` (`remote_unknown`/reasons refuse), `IsAncestor(localTarget, remote.Sha)`
   else `target_local_ahead`, `GitDirectory` per D-2, op fields (`SchemaVersion = 3`, `SourceLocalSha =
   local`, `TargetBeforeSha = RemoteBeforeSha = remote.Sha`, `LocalTargetBeforeSha = localTarget`,
   `LandWorktreePath = landWorkspace.PathFor(common)`, `TargetCheckoutRecorded = false`), attach.
3. **Entry** (`AgentTaskLandingProtocol.RunAsync`): schema handling per D-7; published → cleanup;
   otherwise `RecheckApprovalAsync` + `RecheckSourceRemoteAsync` + `RecheckSourceAsync(InputSha)`.
4. **Inspected → RecoveryPinned**: pins `source` (`OriginalSourceSha`) and `target-before` (`TargetBeforeSha`).
5. **RecoveryPinned**: if `IsAncestor(InputSha, TargetBeforeSha)`: `RecheckSourceRemoteAsync`, re-observe
   the target, `exact_remote_containment` → `Verified` → `ConfirmAsync(AlreadyPresent)` → cleanup.
   Otherwise `RecheckApprovalAsync`, `RecheckSourceRemoteAsync`, `RecheckSourceAsync`; `landWorkspace.EnsureAsync(
   RepositoryPath, LandWorktreePath, InputSha)` (owned children; `LandWorkspaceReadyAt`); `RebaseStartedAt`;
   → `RebaseStarted`; `rebase <TargetBeforeSha>` in the land worktree (`MutateAsync`, same argument shape
   as today); on failure: `diff --name-only --diff-filter=U -z` and `rebase --abort` in the land worktree,
   `Refused: rebase_conflict|rebase_failed` with the conflict list (the merge helper task is created by
   the service exactly as today and works in the task's own worktree, whose branch is untouched);
   on success: `rev-parse HEAD` == `RebaseHeadSha`, pin `prepared`, `RebasedSourceSha`, → `Prepared`.
6. **Prepared**: `RecheckApprovalAsync`, `RecheckSourceAsync(RebasedSourceSha)`; `base_unchanged` skip
   as today (R2 adds the two delta skips); otherwise `verifier.VerifyAsync(LandWorktreePath, filter, ...)`;
   `RecheckSourceAsync`; → `Verified`.
7. **Verified**: `RecheckApprovalAsync`, `RecheckSourceRemoteAsync`, `RecheckSourceAsync(VerifiedSourceSha)`;
   `beforePush = ObserveAsync(target, VerifiedSourceSha)`: if it contains the source → confirm
   (`AlreadyPresent`); else `Require(beforePush.Sha == TargetBeforeSha, "remote_changed_before_push")`;
   `PushStartedAt` → `PushStarted`; `PushOwnedAsync`; re-observe; `push_unconfirmed|push_rejected`;
   `ConfirmAsync(Landed)` → `PublicationConfirmed`.
8. **Canonical advance** (D-4/D-5, inside the `PublicationConfirmed → CleanupStarted` step, exceptions
   caught into `CanonicalAdvanceReason`).
9. **Cleanup**: `ExpectedDeletionSha = SourceLocalSha`; `TryRemoveAsync(new(Publication, Coordinates(op),
   ..., ExpectedSourceSha: op.ExpectedDeletionSha, ExpectedTargetSha: op.TargetBeforeSha, ..., LandedSha:
   op.VerifiedSourceSha))`; unchanged otherwise.
10. **Service**: `Record(Rebase/Verify/Cleanup)` unchanged; `FormatOutcome` appends `canonical=...`;
    `LandedWithResidue` when `CanonicalAdvanceReason` is set; the `Warning` event of D-4; the profile line
    of D-11.

### Slices

- **S1 — `LandWorkspace`** (R1). New `server/Application/Interfaces/ILandWorkspace.cs`
  (`PathFor`, `EnsureAsync`, `ScanHeadFiles`, records `LandWorkspaceState(Path, HeadSha, Created,
  Reason)` and `LandingHeadFile(AdminDirectory, WorktreePath, SymbolicRef, Sha)`), new
  `server/Infrastructure/Git/LandWorkspace.cs` per D-3/D-5, `WorktreeRoots.Resolve(GitSettings)` shared
  with `WorktreeManager`, DI registration in `Program.cs` (singleton) and in
  `AddDelegationWorktreeGraph`. `LandFailureDiagnostic.CommandTemplate` gains `reset`, `clean`,
  `worktree add|lock`, `show-ref` templates so refusals stay diagnosable.
- **S2 — Resolver** (R1). `AgentTaskLandSourceResolver.cs`: D-2 resolution, D-4 creation, D-7 derivation
  and superseded-state handling; delete the `AdvanceStarted`/`source-ff` arms and `TargetCheckoutAsync`.
- **S3 — Protocol** (R1). `AgentTaskLandingProtocol.cs`: the flow above; constructor gains
  `ILandWorkspace landWorkspace`; delete `TargetCheckoutAsync`, `VerifiedTargetCheckoutAsync`,
  `CheckTargetAsync`, `CheckLiveTargetAsync`, `PreparationChangedAsync`'s checkout arm and the
  `LocalTargetAdvanced` block; add `AdvanceCanonicalAsync`, `RecheckApprovalAsync`, the one-round-trip
  `RecheckRemoteSourceAsync`. `ILandingGit.RecheckSourceRemoteAsync` + `LandingSourceRecheck` +
  `LandingGit` implementation (CARD-0642 S4 verbatim).
- **S4 — Cleanup contract** (R1). `WorktreeRemovalRequest.LandedSha`; `GuardedWorktreeRemoval.AuthorityAsync`;
  `WorktreeGuardedCleanup` and `IWorktreeCleanupJournal` intent (`ExpectedDeletionSha`);
  `AgentTaskLandingState` per D-6/D-7.
- **S5 — Entity, migration, service** (R1). `AgentTaskLanding` columns (D-7, D-11 `LandWorkspaceReadyAt`),
  migration `AddDedicatedLandWorktree`, `AgentTaskLandService` (probes D-10, outcome line, warning event,
  profile line D-11, `LandingVerificationCorrelation.ArtifactsPath` plumbing with the default unchanged).
- **S6 — Fakes** (R1). `ControlledLandingGit` per D-12; `LandingGitFixture` gains `Land` and a
  `MainCheckoutHead()` helper; `LandingSafetyHarness`/`LandingProtocolHarness` register `ILandWorkspace`
  (real `LandWorkspace` over the fixture's git in both harnesses; the fake git makes it deterministic).
- **S7 — Test migration** (R1). The table of D-12, applied class by class; new tests V-1..V-19.
- **S8 — Non-buildable skip** (R2). CARD-0642 S5 verbatim (`DelegationSettings.LandNonBuildableGlobs`,
  `LandVerificationInputs.cs`, `ILandingGit.ChangedPathsAsync` default member, Prepared-block decision
  order, state arms, Verify `StageOutcome` detail), with `VerificationCommand` set after the decision.
- **S9 — Docs** (R2). `docs/orchestration-loop.md` §5: the server's sequence (observe origin/master →
  reset land worktree → rebase → conditional build → push → fast-forward the main checkout → cleanup),
  the `canonical=` residue reasons and their fix, the disappearance of "origin/master moved ahead of local
  master", the skip reasons and setting; `docs/bootstrap.md` CARD-0358 gotcha: the pipeline now
  fast-forwards the main checkout after the push and reports `canonical=` when it cannot; the invariant
  I-16 next to it; `AGENTS.md` local-stack line for I-16; `docs/testing-and-build.md`: verification
  builds in `<WorktreeBasePath>/land/<hash>`, the skip setting, embedded-resource rule; `docs/logs.md`:
  the extended profile line; `docs/session-runtime-invariants.md` unchanged (no session behaviour changes).
- **S10 — Stable artifacts** (R3). D-9: `LandVerificationArtifacts.PathFor(landWorktreePath)`, TRX
  pre-clean, protocol passes the path, `docs/testing-and-build.md` note on retention.

### Code rounds

| Round | Slices | Files | Exit |
|---|---|---|---|
| R1 | S1–S7 | `ILandWorkspace.cs` (new), `LandWorkspace.cs` (new), `WorktreeManager.cs` (root helper only), `Program.cs`, `ILandingGit.cs`, `LandingDtos.cs`, `LandingGit.cs`, `AgentTaskLandSourceResolver.cs`, `AgentTaskLandingProtocol.cs`, `AgentTaskLandingState.cs`, `AgentTaskLandService.cs`, `AgentTaskLanding.cs` + migration, `WorktreeRemovalRequest.cs`, `GuardedWorktreeRemoval.cs`, `WorktreeGuardedCleanup.cs`, `LandFailureDiagnostic.cs`, `ControlledLandingGit.cs`, `LandingGitFixture.cs`, both harnesses, tests | CP-1..CP-8 green; commit message carries the V-18 profile line; the orchestrator restarts, lands one doc-only and two code cards, posts their three profile lines on the card |
| R2 | S8, S9 | `DelegationSettings.cs`, `LandVerificationInputs.cs` (new), `ILandingGit.cs`, `AgentTaskLandingProtocol.cs`, `AgentTaskLandingState.cs`, `AgentTaskLandService.cs`, `ControlledLandingGit.cs`, docs, tests | CP-9..CP-11 green; three more profile lines after activation |
| R3 | S10 | `AgentTaskLandService.cs`, `LandingVerifier.cs`, `AgentTaskLandingProtocol.cs`, `LandingDtos.cs`, docs, tests | CP-12..CP-13 green; two consecutive code lands' Verify seconds posted (second expected well under the first); dispatched only if R2's lines show Verify > 60 % of start→confirmed on built lands |

## Migration and rollback

- **Deploy R1.** Additive migration; restart via `restart-apphost.ps1` from the main checkout; confirm
  `GET /api/version` equals the landed SHA. The first land creates
  `C:\Antiphon\worktrees\land\<hash>` (one `worktree add --detach`, ~1–2 s, once per repository). No
  operator step. In-flight land requests at the restart are re-run by the sweep: published ops clean up as
  before; schema-2 ops at `LocalTargetAdvanced`/`PushStarted` finish through the push step; any other
  unpublished schema-2 op reports `LandRefused: landing_schema_superseded` and the orchestrator re-runs
  `-Land` (D-7). Existing refusal states caused by a stale main checkout ("origin/master moved ahead of
  local master") stop occurring; the CARD-0358 runbook remains the fix for `canonical=` residue.
- **Roll back R1** (build rollback: check out the previous SHA in the main checkout and restart). Unused
  columns are ignored. An unpublished schema-3 op is refused by the old code's
  `landing_schema_unsupported`; a fresh `-Land` then lands through the old protocol, and because the new
  protocol never moved the task branch, the old one finds the reviewed SHA in the worktree with no
  derivation. A *published* schema-3 op under the old build cannot clean up (old `HasIdentity` refuses
  schema 3): its worktree and branch stay as residue for CARD-0692/`worktree-residue.ps1`, publication
  is unaffected. The land worktree is inert under the old build; delete it with `git worktree unlock`
  + `git worktree remove --force <path>` if wanted.
- **R2 and R3** roll back independently (R2 by setting `Delegation:LandNonBuildableGlobs` to `[]` without
  a rollback, R3 by build rollback; artifacts directory deletable at any time).

## Verification design

Vocabulary: V-n is a new red-first test; R-n is an existing class kept green as the regression net.
All tests are cross-platform: paths through `Path.Combine`, git through `LandingGitFixture`/
`ControlledLandingGit`, no Linux-only strings, real-git classes under `[ParallelLimiter<ProcessSpawnLimit>]`.
"Red" states what fails today at `ada146ea`.

### Round 1

- **V-1** `LandWorkspaceTests` (new, real git, `LandingGitFixture` with `WorktreeBasePath` = fixture
  `trees`): (a) `EnsureAsync` creates `<trees>/land/<hash>` detached at the SHA with a `.git` file and a
  `locked` marker; (b) a second call with another SHA resets in place (HEAD equals it, `status` empty,
  still one registration for the path); (c) a leftover `rebase-merge` directory, a modified tracked file
  and an untracked file are cleared; (d) a deleted directory with a stale registration heals through
  `worktree add --detach --force --force`, never `worktree prune` (trace); (e) `WorktreeManager.ListAsync`,
  `ScanDelegateWorktreesAsync` and `PruneStaleAsync` on the same root ignore it; (f) a live `index.lock`
  in it refuses with the existing lock code. Red: type absent.
- **V-2** `LandingGitTests.C688_HeadFileScanFindsTargetCheckouts` (real git): main checkout on `master`,
  one linked worktree on a feature branch, one detached; the scan reports the main checkout only; after
  `git -C <linked> checkout --ignore-other-worktrees master` (git refuses the plain checkout, which is
  itself the construction proof) the scan reports it; the scan puts no `worktree list` in the trace.
- **V-3** `LandingGitTests.C642_RecheckSourceRemoteIsOneRoundTripWithoutPins` (CARD-0642 V-14, verbatim).
- **V-4** `AgentTaskLandPublicationTests.C688_LandNeverRunsGitInTheSourceWorktree` (real git): after a
  full land, the only trace entry whose working directory is the source worktree before the cleanup
  `worktree remove` is one `rev-parse --absolute-git-dir`; no `status`, `symbolic-ref`, `rebase`, `merge`
  or `worktree list` there; the profile's `inspections == 2` (cleanup). Red today: 19 inspections.
- **V-5** `AgentTaskLandPublicationTests.C688_RebaseAndVerifyRunInTheLandWorktree` (real git): the
  `rebase` entry's directory is `op.LandWorktreePath`; `Verifier.Invocations[0].Worktree` equals it; after
  publication and before cleanup the source branch still equals `OriginalSourceSha`; `op.SourceLocalSha ==
  OriginalSourceSha`; the land worktree HEAD equals `RebasedSourceSha` and is detached.
- **V-6** `AgentTaskLandPublicationTests.C688_PushPrecedesCanonicalAdvance` (real git): trace order
  `push` before `merge --ff-only` in the main checkout; `op.CanonicalAdvancedAt` set,
  `CanonicalAdvanceReason` null; main checkout HEAD and `refs/heads/master` equal `VerifiedSourceSha`;
  `LocalTargetBeforeSha == SeedSha`; outcome line contains `canonical=advanced`. Red: order is
  merge-then-push.
- **V-7** `AgentTaskLandPublicationTests.C688_DirtyCanonicalCheckoutLandsWithResidue` (real git): an
  untracked file in the main checkout → `HasPublication`, remote `master` equals `VerifiedSourceSha`,
  local `master` unchanged, no `merge`/`update-ref refs/heads/master` in the trace after the push, terminal
  `LandedWithResidue` with `canonical=canonical_checkout_dirty` and a `Warning` event naming the checkout.
  Red today: `target_dirty_or_unknown` refusal before any push.
- **V-8** `AgentTaskLandPublicationTests.C688_TargetCheckedOutElsewhereIsResidueNotRefusal` (real git):
  a second linked worktree forced onto `master` → publication confirmed, `canonical=target_checked_out_elsewhere`,
  no `update-ref`/`merge` of `master` anywhere. Red today: `ambiguous_target_checkout` refusal.
- **V-9** `AgentTaskLandSourceFreshnessTests.C688_BehindLocalBranchLandsFromRemoteWithoutFastForward`
  (fake): `AdvanceRemoteSource()`, expected = remote → lands; no `merge --ff-only` with the source
  directory; `op.SourceLocalSha == SeedSha`, `OriginalSourceSha == remote`; cleanup's `update-ref
  --no-deref -d <source> SeedSha` is in the trace. Red today: `merge --ff-only` in the source.
- **V-10** `AgentTaskLandSourceFreshnessTests.C688_SourceBranchMovedMidLandRefuses(int recheck)`,
  arguments 1, 2, 3: `OnSourceRecheck(n)` rewinds the local branch ref → `source_changed`, no `rebase`
  after it (n=1), no `push` (n=2,3), `HasPublication` false. Red: hook absent.
- **V-11** `AgentTaskLandSourceFreshnessTests.C688_RemoteSourceMovedBeforeMutationRefuses(int recheck)`,
  arguments 2, 3 (pre-rebase, pre-push): `source_remote_changed`, no mutation after that recheck
  (CARD-0642 V-9 adapted).
- **V-12** `AgentTaskLandPublicationTests.C688_LocalMasterBehindOriginRebasesOntoOrigin` (real git): a
  commit pushed to the remote `master` by the observer clone, main checkout not pulled → the `rebase`
  argument is the remote SHA, the push succeeds, the canonical `merge --ff-only` moves the main checkout
  through the foreign commit to `VerifiedSourceSha`. Red today: `remote_ahead_or_diverged`.
- **V-13** `AgentTaskLandPublicationTests.C688_LocalMasterAheadOfOriginRefuses` (real git): an unpushed
  commit on local `master` → `target_local_ahead` at resolution, no pins, no land worktree mutation.
- **V-14** `AgentTaskLandingStateTests.C688_SchemaThreeArms` (`[Arguments]` ×6): `(Verified → PushStarted)`
  accepted for schema 3 with `PushStartedAt`, refused for schema 2; `(PublicationConfirmed → CleanupStarted)`
  requires `ExpectedDeletionSha == SourceLocalSha` (3) and `== VerifiedSourceSha` (2); `HasIdentity`
  refuses a schema-3 row without `SourceLocalSha`/`LandWorktreePath`/`LocalTargetBeforeSha`.
- **V-15** `AgentTaskLandRecoveryTests.C688_SchemaTwoOperationsOnResume(string phase)`, arguments
  `prepared`, `local-target-advanced`, `published`: a seeded schema-2 op at `Prepared` refuses
  `landing_schema_superseded` (op `Refused`, request `LandRefused`); at `LocalTargetAdvanced` completes
  through the push with `canonical=already`; a published one runs cleanup with `ExpectedDeletionSha ==
  VerifiedSourceSha`.
- **V-16** `AgentTaskLandRecoveryTests.C688_InterruptedRebaseRecoversOnNextRequest` (real git): an op
  at `RebaseStarted` with a `rebase-merge` directory in the land worktree → resume refuses
  `interrupted_rebase`; a fresh request lands and the trace shows `rebase --abort` then `reset --hard`
  before the new `rebase`.
- **V-17** `WorktreeRemovalAuthorityTests.C688_PublicationCleanupDeletesBranchAtLocalSha` (real git): a
  published schema-3 op whose `SourceLocalSha` ≠ `VerifiedSourceSha` removes the worktree and deletes the
  branch at `SourceLocalSha` after observing the remote contains `VerifiedSourceSha`; a branch moved to a
  third SHA refuses `source_changed`; a remote that does not contain `VerifiedSourceSha` refuses
  `remote_no_longer_contains_source`.
- **V-18** `AgentTaskLandRefusedRetryTests.C688_RealLandProfile` (real git; replaces
  `C642_RealLandOpensOneScope`'s thresholds): `worktreeList ≤ 3`, `inspections ≤ 2`, `processes ≤ 250`,
  `remote ≤ 16`, `registrationHits` any; no `refs/antiphon/**/source-recheck/**` ref exists afterwards; the
  profile line contains `reset=`, `push=`, `canonical=`; print it as `C688_PROFILE:`. Red today: 603/7/19/37.
- **V-19** `AgentTaskLandIndexLockTests.C688_AdmissionProbesLandWorktreeAndMainCheckoutWithoutListing`
  (real git): a stale `index.lock` in the land worktree holds admission with `git_index_lock_stale`; one
  in the main checkout (on `master`) holds; the trace has no `worktree list` before the hold; removing the
  file resumes on the next sweep.
- **R-1** existing, kept green after the D-12 migration: `LandingGitTests`, `LandingGitProfileTests`,
  `ControlledLandingGitTests`, `LandingRemovalPolicyControlTests`, `AgentTaskLandingStateTests`,
  `AgentTaskLandSourcePersistenceTests`, `LandingProtocolGuardTests`, `LandingProtocolHarnessTests`,
  `AgentTaskLandFailureDiagnosticTests`, `AgentTaskLandPreparationIdentityTests`,
  `AgentTaskLandRefusedRetryTests`, `AgentTaskLandRecoveryTests`, `AgentTaskLandIndexLockTests`,
  `InterimVerificationLandGitTests`, `LandSourceIdentityTests`, `LandingSourceFreshnessTests`,
  `WorktreeRemovalAuthorityTests`, `AgentTaskLandStageOutcomeTests`, `AgentTaskLandBoundaryTests`,
  `AgentTaskLandCleanupSafetyTests`, `WorktreeLandingCleanupRetryTests`, `AgentTaskLandRemovalMatrixTests`,
  `AgentTaskLandCheckpointMatrixTests`, `AgentTaskLandIdentityMatrixTests`, `WorktreeManagerTests`,
  `PostLandMutationWorktreeTests` (verification snapshots are created from `op.VerifiedSourceSha`, which
  is unchanged in meaning), `AgentTaskLandVerifierTests`, and the E2E `AgentTaskLandDeliveryE2ETests`
  (its fixture already has a main checkout on `master`, a bare origin and one task worktree; the land
  worktree lands under the child server's `WorktreeBasePath`).

### Round 2

- **V-20** `AgentTaskLandingStateTests.C642_DeltaSkipArms` (CARD-0642 V-10, verbatim).
- **V-21** `LandVerificationInputsTests` (CARD-0642 V-11, verbatim).
- **V-22** `AgentTaskLandVerificationSkipTests` (CARD-0642 V-12 (a)–(g), with the base moved on the
  fixture remote rather than the local target, and `Verifier.Invocations[*].Worktree == op.LandWorktreePath`).
- **V-23** `LandingGitTests.C642_ChangedPathsListRenamesAsBothNames` (CARD-0642 V-13, verbatim).
- **R-2** existing: the R-1 list; docs checked by CP-11.

### Round 3

- **V-24** `LandVerificationArtifactsTests` (new, Unit): `PathFor` is a sibling directory of the land
  worktree, never inside it; a stale `landing-verification.trx` under it is removed before the test run;
  the build command carries `--artifacts-path <stable>`.
- **V-25** `AgentTaskLandPublicationTests.C688_VerifierReceivesStableArtifactsPath` (real git): two
  consecutive lands pass the same `ArtifactsPath` on the correlation; the temp `antiphon-land-verify-*`
  directory is not created.
- **R-3** existing: `AgentTaskLandVerifierTests` (`C448_V35_RealVerifierPreservesPreExistingOutput` keeps
  the default path behaviour), R-1 list.

### Checkpoints

Test project `tests/Antiphon.Tests` unless stated; isolated outputs `bin-c688a/` (R1), `bin-c688b/` (R2),
`bin-c688c/` (R3), forward slash; one build per round, every other row `--no-build`; on server2
`run-checkpoint.ps1` adds `UseAppHost=false` itself. `Min` is the count of `[Test]` methods in the named
classes at `ada146ea` (reproduced with `grep -c "^\s*\[Test"`) plus the new methods, argument-expanded rows
counted per argument, minus up to 10 % on rows whose classes lose tests under the D-12 migration; a
floor, not a census.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1-S7 | `tests/Antiphon.Tests -> bin-c688a/` | landing-git-real | `/*/*/(LandingGitTests*)\|(LandWorkspaceTests*)/*` | V-1, V-2, V-3, R-1 | all listed, 0 failed | 28 | 10 |
| CP-2 | S1-S7 | CP-1 | landing-unit | `/*/*/(AgentTaskLandingStateTests*)\|(ControlledLandingGitTests*)\|(LandingGitProfileTests*)\|(LandingRemovalPolicyControlTests*)/*` | V-14, R-1 | all listed, 0 failed | 37 | 3 |
| CP-3 | S1-S7 | CP-1 | landing-fake-protocol | `/*/*/(AgentTaskLandSourceFreshnessTests*)\|(AgentTaskLandSourcePersistenceTests*)\|(LandingProtocolGuardTests*)\|(LandingProtocolHarnessTests*)\|(AgentTaskLandFailureDiagnosticTests*)/*` | V-9, V-10, V-11, R-1 | all listed, 0 failed | 85 | 9 |
| CP-4 | S1-S7 | CP-1 | landing-real-protocol-a | `/*/*/(AgentTaskLandPublicationTests*)\|(AgentTaskLandPreparationIdentityTests*)\|(AgentTaskLandRefusedRetryTests*)\|(AgentTaskLandRecoveryTests*)\|(AgentTaskLandIndexLockTests*)/*` | V-4, V-5, V-6, V-7, V-8, V-12, V-13, V-15, V-16, V-18, V-19, R-1 | all listed, 0 failed | 52 | 14 |
| CP-5 | S1-S7 | CP-1 | landing-real-protocol-b | `/*/*/(InterimVerificationLandGitTests*)\|(LandSourceIdentityTests*)\|(LandingSourceFreshnessTests*)\|(WorktreeRemovalAuthorityTests*)\|(AgentTaskLandStageOutcomeTests*)/*` | V-17, R-1 | all listed, 0 failed | 42 | 10 |
| CP-6 | S1-S7 | CP-1 | landing-cleanup-slow | `/*/*/(AgentTaskLandBoundaryTests*)\|(AgentTaskLandCleanupSafetyTests*)\|(WorktreeLandingCleanupRetryTests*)\|(AgentTaskLandRemovalMatrixTests*)\|(AgentTaskLandCheckpointMatrixTests*)\|(AgentTaskLandIdentityMatrixTests*)/*` | R-1 (cleanup contract, D-6) | all listed, 0 failed | 36 | 10 |
| CP-7 | S1-S7 | CP-1 | worktree-tooling | `/*/*/(WorktreeManagerTests*)\|(PostLandMutationWorktreeTests*)\|(AgentTaskLandVerifierTests*)/*` | V-1(e) companions, R-1 | all listed, 0 failed | 60 | 8 |
| CP-8 | S1-S7 | `tests/Antiphon.E2E -> bin-c688e/` | land-delivery-e2e | `/*/*/AgentTaskLandDeliveryE2ETests/*` | R-1 (real server child, isolated runner per docs/testing-and-build.md) | all listed, 0 failed | 23 | 25 |
| CP-9 | S8-S9 | `tests/Antiphon.Tests -> bin-c688b/` | verification-unit | `/*/*/(LandVerificationInputsTests*)\|(AgentTaskLandingStateTests*)\|(ControlledLandingGitTests*)/*` | V-20, V-21, R-2 | all listed, 0 failed | 32 | 5 |
| CP-10 | S8-S9 | CP-9 | verification-skip-protocol | `/*/*/(AgentTaskLandVerificationSkipTests*)\|(AgentTaskLandPublicationTests*)\|(AgentTaskLandPreparationIdentityTests*)\|(AgentTaskLandRefusedRetryTests*)\|(InterimVerificationLandGitTests*)\|(LandingGitTests*)/*` | V-22, V-23, R-2 | all listed, 0 failed | 64 | 14 |
| CP-11 | S8-S9 | n/a | docs-named | `git grep -n -e "LandNonBuildableGlobs" -e "target_checked_out_elsewhere" -e "canonical=" -- AGENTS.md docs/orchestration-loop.md docs/bootstrap.md docs/testing-and-build.md docs/logs.md` | S9 | ≥ 6 matching lines across all five files, exit 0 | n/a | 1 |
| CP-12 | S10 | `tests/Antiphon.Tests -> bin-c688c/` | artifacts | `/*/*/(LandVerificationArtifactsTests*)\|(AgentTaskLandVerifierTests*)\|(AgentTaskLandPublicationTests*)/*` | V-24, V-25, R-3 | all listed, 0 failed | 27 | 8 |
| CP-13 | S10 | n/a | docs-artifacts | `git grep -n "land/<hash>-artifacts\|LandVerificationArtifacts" -- docs/testing-and-build.md` | S10 docs | ≥ 1 matching line, exit 0 | n/a | 1 |

The pipe characters inside the `Filter` cells are escaped for the table; the command line uses a plain
`|`, quoted as [docs/testing-and-build.md](../../testing-and-build.md#combined-class-filters-card-0403)
shows. Run each row with `scripts/run-checkpoint.ps1 -Name CP-n -Project <project> -OutputPath bin-c688x/
-Filter '<filter>' -MinExecuted <Min> -Expect <classes> -ResultsRoot .antiphon/c688-checkpoints`
(`-NoBuild` for reuse rows). CP-8 needs `client/dist` and the E2E isolation described in the testing guide;
if the Code runner cannot satisfy that, the row is reported as not run with the reason and Review runs it
on the desktop before the land. Unlisted runs need a stated reason; a compile error found by a row's own
build is fixed and the same row rerun.

### Cost

Ordinary Code floor: R1 = 89 minutes of checkpoints plus authoring (the protocol/resolver rewrite, the
fake's land-worktree model and the 30-file test migration: about 8 h; the test migration is the bulk and
is enumerated by D-12, not discovered); R2 = 20 minutes of checkpoints plus about 3 h (CARD-0642's own
estimate); R3 = 9 minutes plus about 1 h. `-ExpectAbout` for each Code dispatch is that sum. Review reads
the CHECKPOINT lines against the table and, for R1, the V-18 profile line and the three desktop profile
lines the orchestrator posts after activation.

## Follow-ups (not in this card)

- **Remove the derivation lineage** (`PreparationInputSha`/`PreviousPreparationOperationId` acceptance,
  D-7) once `SELECT count(*) FROM "AgentTaskLandings" WHERE "SchemaVersion" = 2 AND "Active" AND
  "RemoteConfirmedAt" IS NULL` is zero for a week; also delete `LandPhase.TargetAdvanceStarted`/
  `LocalTargetAdvanced` handling.
- **Cleanup is now the largest git consumer per land** (3 listings, 2 `Full` inspections, 2 observes,
  ~70 processes, ~25 s). CARD-0692's post-land Cleanup stage replaces it; until then CARD-0665's
  disposable-content classes decide whether it succeeds at all.
- **Residue sweeper** (CARD-0669): no new `source-recheck/<guid>` refs after R1; the `source-observed`,
  `remote-observed`, `cleanup-observed` pins and the three recovery pins per op still accumulate.
- **Remote dispatch preparation behind the land lease** (CARD-0672): a 3-minute land shortens the queue;
  the lease scope itself is that card's question. The land worktree reset is the only new mutation under
  the lease and takes ~2 s.
- **Sibling warning** `CollectUnlandedSiblingsAsync` runs `git cherry` per same-card kept branch after
  publication; unchanged here, but it is the next per-land git cost after cleanup.

--- next stage ---
next: code
handoff: CARD-0688 R1 per docs/superpowers/plans/2026-09-25-card-0688-dedicated-land-worktree-plan.md: S1 LandWorkspace (locked detached land worktree, HEAD-file scan), S2 ref-based resolver, S3 protocol (rebase onto observed origin/master in the land worktree, push first, canonical fast-forward after publication), S4 cleanup contract, S5 schema 3 + migration, S6 fakes, S7 test migration per D-12; run CP-1..CP-8 as a closed list; CARD-0642 R2 is superseded (S4 absorbed, S5 is R2 here).
artifact: docs/superpowers/plans/2026-09-25-card-0688-dedicated-land-worktree-plan.md
