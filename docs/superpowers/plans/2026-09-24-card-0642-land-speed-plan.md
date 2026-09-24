# CARD-0642: land speed — cut the recheck machinery, skip non-buildable verification, cache registrations

Plan date: 2026-09-24. Plan task: `e070938a` (Frontier, server2 Linux runner, worktree).
Code and master inspected at `42eecdea2c6f682121b075383eaf1ba987057153`.
Card `b71b872a-7f14-4d5f-926d-c3c6b237bfe1` (CARD-0642), board `8988ca03-7414-47ad-b0b6-51556c701703`.
Primary evidence: the CARD-0641 investigation
([land outcome delivery](../../investigations/2026-09-23-card-0641-land-outcome-delivery.md), §2)
and the live desktop API (`GET /api/stage-outcomes`, `GET /api/agent-tasks/{id}`) read on 2026-09-24
between 15:40Z and 15:55Z.

The deliverable is two bounded, sequential Code rounds. The verification design is folded into
this plan (the dispatch asks for red-first checkpoints and `next: code`). No production changes,
restarts, database writes, builds or test runs were performed in Plan; the counts below come from
source rosters and the API, not from a run.

## Summary

The card is right that a doc-only land ran an 89 s `dotnet build` and that `git worktree list`
is called repeatedly against ~650 registrations. It is wrong about proportion. Measured on the
25 lands the desktop completed today, the build is a minority of the time: lands that skipped the
build (`base_unchanged`) took a median **521 s** from admission to `Landed`, lands that built took
**552 s**. The median land spends **354 s** outside the Rebase/Verify/Cleanup stage clocks, in the
inspection and recheck machinery that runs at every phase boundary: about **17 source inspections**
(each two `worktree list` calls, two 650-path canonicalisation sweeps, one `status`, one
`ls-files --ignored`), about **47 `worktree list` calls**, and about **37 GitHub round trips**, of
which 20 are `RecheckRemoteSourceAsync` fetches that also leave a loose ref behind each time.

So the order of work is: (1) make one land's git I/O cheap without changing what the protocol
checks — an operation-scoped registration cache, an inspection scope that stops listing ignored
files where nothing reads the result, and a single-round-trip remote source recheck placed before
each mutation instead of a fetch at every boundary; then (2) skip the build when the landed diff,
or the base's own movement, touches only paths that cannot be build inputs, defined by an explicit
allowlist that survives the two facts that would otherwise make "docs/**" unsafe. A per-land git
profile log line lands first so the before/after is measured on real lands, not estimated.

## Ground truth

| Card / brief assumption | What the code and evidence show | Consequence |
|---|---|---|
| "~6 `git worktree list` calls per land." | About 6 per **phase boundary** (CARD-0641 §2), not per land. Derived from `AgentTaskLandingProtocol.RunAsync` for a fresh, rebased, no-filter land: 17 `InspectAsync` calls (2 in `AgentTaskLandSourceResolver`, 15 in the protocol), each running `IdentityAsync` twice (`LandingGit.cs:168-199`), plus `TargetCheckoutAsync` in 10 `CheckTargetAsync` calls, 1 op-creation lookup and 1 admission probe: **~47 lists and ~34 full canonicalisation sweeps per land**, before cleanup adds its own. | Cache below `ILandingGit` for one land operation (D-4). Do not restructure the recheck count: tests key on inspection ordinals (`ControlledLandingGit.InjectInspection`). |
| "A full `dotnet build` ran for a doc-only change because the rebase moved its base." | True. `AgentTaskLandingProtocol.cs:248-257` skips only when `RebasedSourceSha == OriginalSourceSha`, the filter is empty and there is no derivation chain (`base_unchanged`). Any base movement builds the whole solution into a fresh `--artifacts-path` temp directory (`AgentTaskLandService.VerifyWithObserverAsync`). | Add two delta-based skips (D-2). |
| "docs/**, *.md and the like" are safe to skip. | Not as a directory rule. `tests/Antiphon.Tests/Antiphon.Tests.csproj:117` and `tests/Antiphon.Agents.Pty.Tests/Antiphon.Agents.Pty.Tests.csproj:91` embed `docs/superpowers/plans/2026-09-08-card-0449-claude-effort-dialog-captures.json` as an `EmbeddedResource`; `server/Antiphon.Server.csproj:25-28` embeds `server/Bundles/*.md` and `Bundles/Presets/*.md`. A `docs/**` or `**/*.md` rule would skip a build that those files change. | Allowlist by extension **and** path: `docs/**/*.md`, root `*.md`, `LICENSE` (D-1). Everything else builds. |
| The build dominates land time (the card's framing). | 25 lands today (table below): admission→Landed p50 **523 s** (min 313, max 914). Build-skipped lands p50 **521 s**; built lands p50 **552 s** with Verify p50 **132 s**. Stage rows since 09-22 (n=61 Verify): p50 98 s, mean 90 s. Unattributed time (total − Rebase − Verify − Cleanup) p50 **354 s**, max 628 s. Even `base_unchanged` Verify rows take **5–32 s**, which is one `RecheckSourceAsync` plus one `CheckTargetAsync` and nothing else. | The recheck machinery is round 1; the build skip is round 2. |
| "Several GitHub round trips." | ~37 per land: resolver 4 (`ObserveSourceAsync` + `RecheckRemoteAsync`, each `ls-remote` + `fetch`), protocol `RecheckRemoteSourceAsync` **10×** (entry + Inspected + 4 in RecoveryPinned + Prepared + Verified + TargetAdvanceStarted + LocalTargetAdvanced) = 20, target `ObserveAsync` 3× = 6, push 1, cleanup observes ~6. Each protocol recheck fetches into a fresh `refs/antiphon/land/<task>/<op>/source-recheck/<guid>` ref that is never deleted (the CARD-0641 timeline was reconstructed from their mtimes). | One-round-trip recheck (`ls-remote` only, no fetch, no pin) placed before each mutation (D-6). |
| Caching within one land is safe. | The land holds the `RepositoryMutationLease` for the whole `RunRequestAsync`; dispatch (`AgentTaskDispatcher.cs:3905`), worktree provision and settlement (`DelegationWorktreeService.cs:262,516`), gated commit and other lands take the same lease before mutating registrations. `WorktreeJanitorHostedService.PruneStaleAsync` does **not** take it. | Scope the cache to the lease; invalidate on the land's own mutating commands and on a cheap external signal (the `<common>/worktrees` directory stamp) so a janitor prune is still seen (D-4). |
| Land phases have timestamps reachable from the API. | `AgentTaskLanding` stores `RebaseStartedAt`, `PreparedAt`, `VerificationStartedAt`, `VerifiedAt`, `PushStartedAt`, `RemoteConfirmedAt`, `CleanupStartedAt`, `CleanupCompletedAt`, but `GET /api/agent-tasks/{id}` exposes only `remoteConfirmedAt` on `landing`. Rebase/Verify/Cleanup durations come from `StageOutcomes` (`Source=Server`); admission and terminal times from `landRequest.startedAt` and the `Landed` event. Nothing measures the inspection/recheck time between them. | Add a per-land git profile log line first (D-7) so round 2's effect is measured on real lands. No DTO/schema change. |
| `IgnoredPaths` from `InspectAsync` is needed by the protocol. | The only consumer is `GuardedWorktreeRemoval.HasProtectedIgnored` (`GuardedWorktreeRemoval.cs:252`), during cleanup. The protocol's `Matches(...)` compares HEAD and directories only. The `ls-files --others --ignored` listing on a built worktree cost 0.76–7.1 s per call (CARD-0641) and runs 17 times per land. | Inspection scope: protocol/resolver inspections skip the listing; cleanup keeps it (D-5). |
| A new skip reason is just a string. | `AgentTaskLandingState.HasVerification` enumerates exactly `base_unchanged` and `exact_remote_containment`; any other reason makes the `Prepared → Verified` transition throw `landing_transition_refused`. `AgentTaskLandingStateTests.C448_V31` pins that unknown labels are refused. | Add two structural arms to the state policy and pin them (D-2, V-15). |
| Worktree pruning belongs here. | 650 registrations are the multiplier on every `worktree list`, but residue handling has its own machinery (CARD-0459 preview/run, retirement, `scripts/worktree-residue.ps1`). | Out of scope; see Follow-ups. The cache removes the multiplier's effect inside a land regardless. |

### Measurements

Per-land timing from the desktop API, 2026-09-24 00:00Z–15:30Z, every completed land with a
request row (`wait` = requested→admitted; `total` = admitted→terminal event; stage seconds from
`StageOutcomes`):

| card | role | task | requested (UTC) | wait s | start→landed s | start→confirmed s | rebase s | verify s | verify detail | cleanup s |
|---|---|---|---|---:|---:|---:|---:|---:|---|---:|
| CARD-0664 | Investigate | 8afa8f97 | 09-24 15:21:49 | 4 | 432 | 396 | 36 | 137 | build OK | 35 |
| CARD-0662 | Code | 35b8f15d | 09-24 15:09:23 | 5 | 486 | 469 | 46 | 116 | build OK | 16 |
| CARD-0660 | Plan | f602d622 | 09-24 14:46:22 | 15 | 664 | 527 | 41 | 118 | build OK | 135 |
| CARD-0653 | Code | 2060e274 | 09-24 14:24:10 | 39 | 611 | 586 | 37 | 195 | build OK | 20 |
| CARD-0660 | Investigate | e617855b | 09-24 14:16:02 | 7 | 404 | 349 | 34 | 8 | base_unchanged | 55 |
| CARD-0640 | Code | 015dc1ee | 09-24 13:34:43 | 945 | 353 | 335 | 28 | 92 | build OK | 18 |
| CARD-0646 | Code | f646569d | 09-24 13:32:56 | 497 | 552 | 534 | 49 | 132 | build OK | 17 |
| CARD-0644 | Code | 4745f0d3 | 09-24 13:30:46 | 164 | 440 | 417 | 41 | 110 | build OK | 19 |
| CARD-0659 | Plan | d2ce3ed6 | 09-24 13:24:30 | 15 | 523 | 479 | 51 | 21 | base_unchanged | 44 |
| CARD-0650 | Code | e9f20adc | 09-24 12:58:52 | 27 | 354 | 336 | 38 | 98 | build OK | 15 |
| CARD-0657 | Plan | 5ef9bb0e | 09-24 12:43:37 | 285 | 653 | 398 | – | – | – | 63 |
| CARD-0647 | Code | 520f3e0a | 09-24 12:34:18 | 160 | 680 | 648 | 69 | 176 | build OK | 24 |
| CARD-0656 | Code | a143618c | 09-24 12:26:05 | 78 | 570 | 548 | 62 | 17 | base_unchanged | 22 |
| CARD-0644 | Code | 695a54a1 | 09-24 11:39:27 | 8 | 691 | 668 | 44 | 146 | build OK | 21 |
| CARD-0655 | Code | 3130100c | 09-24 11:21:57 | 6 | 521 | 495 | 77 | 32 | base_unchanged | 24 |
| CARD-0651 | Docs | 176e26d6 | 09-24 10:59:21 | 38 | 592 | 536 | 42 | 153 | build OK | 55 |
| CARD-0649 | Code | d0498628 | 09-24 10:30:16 | 6 | 515 | 502 | 35 | 156 | build OK | 13 |
| CARD-0641 | Code | 462c4d5e | 09-24 08:57:29 | 10 | 914 | 855 | 100 | 138 | build OK | 48 |
| CARD-0650 | Investigate | 8863c443 | 09-24 08:24:44 | 594 | 552 | 504 | 44 | 102 | build OK | 47 |
| CARD-0650 | Plan | af7c5c18 | 09-24 08:24:05 | 7 | 621 | 555 | 65 | 30 | base_unchanged | 65 |
| CARD-0647 | Custom | a8b2b9d5 | 09-24 07:44:31 | 4 | 417 | 365 | 34 | 12 | base_unchanged | 50 |
| CARD-0641 | Code | c3c432c4 | 09-24 07:30:15 | 5 | 614 | 596 | 71 | 111 | build OK | 14 |
| CARD-0647 | Code | 1d875ff4 | 09-24 07:22:09 | 3 | 313 | 297 | 52 | 12 | base_unchanged | 15 |
| CARD-0633 | Code | fd85b4f4 | 09-24 01:50:59 | 291 | 423 | 407 | 25 | 140 | build OK | 13 |
| CARD-0641 | Code | 82e228ee | 09-24 01:49:26 | 2 | 378 | 339 | 29 | 100 | build OK | 36 |

Summary: n=25; total p50 523 s, mean 531 s; unattributed (total − rebase − verify − cleanup)
n=24 p50 354 s, min 203 s, max 628 s; build-skipped n=7 p50 521 s; built n=17 p50 552 s,
Verify p50 132 s. Four of today's doc-stage lands (Investigate 8afa8f97, Plan f602d622, Docs
176e26d6, Investigate 8863c443) built for 102–153 s each because their base had moved.

Unit costs measured on the desktop by CARD-0641 (read-only commands, `GIT_OPTIONAL_LOCKS=0`):
`worktree list --porcelain` 1.33 s at 650 registrations; canonicalising all 650 paths 0.42 s;
`ls-remote` 1.06–1.17 s; `ls-files --others --ignored` on a built worktree 0.76–7.1 s;
`status --untracked-files=all` in the main checkout 0.13–0.28 s. Today's per-inspection cost is
evidently higher under load (5–32 s for one inspection plus one target check), so these are floors.

Call counts per fresh, rebased, no-filter land, derived from the protocol source (the numbers
the S1 profile must reproduce, and the ones each slice removes):

| I/O | Today | After R1 | Source |
|---|---:|---:|---|
| `InspectAsync` | 17 | 17 | 2 resolver + 15 protocol; unchanged by design (D-4) |
| `git worktree list` | ~47 | ~8 | 34 from `IdentityAsync` + 12 `TargetCheckoutAsync` + 1 admission; after: one per invalidation (3 pins, rebase, target advance, entry) |
| 650-path canonicalisation sweeps | 34 | ~1 | one per `IdentityAsync`; cached per scope |
| `ls-files --others --ignored` (source) | 17 | 0 | protocol/resolver never read `IgnoredPaths`; cleanup keeps its own 2 |
| source remote round trips (`ls-remote`+`fetch`) | 24 | 8 | resolver 4 unchanged; protocol 10×2 → 4×1 (`ls-remote` only) |
| target observe round trips | 6 | 6 | unchanged |
| loose `source-recheck/<guid>` refs written | 10 | 0 | rechecks no longer fetch |
| git processes (all) | ~600 | ~450 | residual spawn cost; see Follow-ups |

## Decisions

- **D-1 — Non-buildable paths are an explicit allowlist, matched by extension and path, configurable, strict.**
  New setting `Delegation:LandNonBuildableGlobs` (string array) with default
  `["docs/**/*.md", "*.md", "LICENSE"]`, matched with `Microsoft.Extensions.FileSystemGlobbing.Matcher`
  (already a server dependency; `ScopeDriftPolicy` is the precedent) using `StringComparison.Ordinal`
  against the forward-slash paths git prints. `*.md` matches root-level files only; `docs/**/*.md`
  matches any depth under `docs/`. A path is buildable unless it matches; an empty list disables
  the skip. `DelegationSettingsValidator` rejects an empty, whitespace, rooted (`/`, drive, `\`) or
  `..`-containing glob at startup. Rejected: `docs/**` (two test projects embed a JSON file from
  `docs/superpowers/plans/`), `**/*.md` (`server/Bundles/*.md` are embedded resources),
  `OrdinalIgnoreCase` (would let a differently-cased directory that git treats as distinct match
  the docs rule), an inferred set from csproj globs (fragile and unbounded), and adding `scripts/`,
  `.github/`, `client/` or `*.json` to the default (not build inputs today, but the card asks for a
  precise, safe set; the operator can extend the setting).

- **D-2 — Two delta-based skips, both fail-closed, recorded as skip reasons the state policy understands.**
  In the `Prepared` block, after the existing `base_unchanged` test and only when `VerificationFilter`
  is empty:
  1. `non_buildable_source_delta`: `git diff --name-only --no-renames -z <TargetBeforeSha> <RebasedSourceSha>`
     lists only allowlisted paths (an empty list counts: a rebase that dropped every already-applied
     commit lands a tree identical to the base). The combined tree's build inputs equal
     `TargetBeforeSha`'s, which is the target branch itself.
  2. `non_buildable_base_delta`: the source delta is buildable but
     `git diff --name-only --no-renames -z <InputSha>...<TargetBeforeSha>` (the base's own movement
     since the reviewed commit's merge-base) lists only allowlisted paths, **and** there is no
     derivation chain (`PreparationInputSha` null or equal to `OriginalSourceSha`). The combined
     tree's build inputs equal the reviewed commit's, which the pre-land Review/Code stages verified.
     This is exactly the trust `base_unchanged` already places in the reviewed SHA, extended to a
     base that only gained non-buildable commits. The derivation exclusion mirrors the existing rule.
  Any non-zero diff exit, unparsable output, or a path outside the allowlist runs the build as
  today. `--no-renames` makes a rename appear as delete + add so both names are classified;
  submodule and mode changes list their path and therefore build. `AgentTaskLandingState.HasVerification`
  gains two arms: reason `non_buildable_source_delta` requires `IsOid(RebasedSourceSha)` and an
  empty filter; `non_buildable_base_delta` additionally requires no derivation. `VerificationPassed`
  stays false. `VerificationCommand` is set **after** the decision: the diff command and path count
  for a skip, the dotnet string for a build, and `none (base unchanged)` for `base_unchanged`
  (today it always says `dotnet build; ...`, even on skipped rows). The Verify `StageOutcome`
  detail becomes `<reason>; <VerificationCommand>` for the delta skips. Rejected: a new
  `VerificationSkipDetail` column (migration weight for evidence the StageOutcome and log already
  carry), skipping when a filter is present (an explicit `-Verify` is a caller decision), and
  treating an explicit `LandVerifyFilter` as narrowable.

- **D-3 — Do not narrow the build; keep the fresh artifacts path.** Building a project subset needs
  a dependency graph the land does not have, the test projects reference everything, and the
  solution build is the contract Review relies on. A stable per-repository `--artifacts-path` for
  incremental builds would not help either: each land builds from a different worktree path, so
  MSBuild sees every input as new. Rejected both; the win is the skip, not a smaller build.

- **D-4 — An operation-scoped read cache below `ILandingGit`, bounded by the lease, invalidated by the land's own mutations and by an external registration stamp.**
  `ILandingGit.BeginOperationScope()` (default: a no-op scope) returns an `ILandingOperationScope`
  with a `LandingGitProfile`. `LandingGit` keeps the live scope in an `AsyncLocal` so its private
  helpers see it without signature churn. Inside the scope: (a) parsed `worktree list` registrations
  per repository, (b) `CanonicalDirectoryAsync` results per input path (successes only), (c)
  `CommonDirectoryAsync` results per input path (successes only). `AgentTaskLandService.RunRequestAsync`
  opens the scope right after `TryAcquireAsync` succeeds and disposes it with the lease, so the
  admission probe, resolver, protocol and cleanup share it. Registrations are re-listed when
  (i) `ExecuteAsync` ran a command that can change registrations or a worktree's HEAD/branch —
  `worktree` other than `list`, `rebase`, `merge`, `checkout`, `switch`, `reset`, `commit`,
  `branch`, `symbolic-ref` in its write form, and `update-ref` naming `HEAD` or a `refs/heads/`
  ref — or (ii) the registration stamp changed: `(LastWriteTimeUtc, entry count)` of
  `<common-dir>/worktrees` (absent → a fixed sentinel), read with one stat before every cached use.
  `fetch`, `push` and pin `update-ref`s do not invalidate. `Directory.Exists(entry.Path)` per entry
  stays live. Canonical and common-dir entries live for the whole scope (nothing Antiphon does under
  the lease re-points a junction). Nested `BeginOperationScope` returns the outer scope. Rejected: a
  TTL (time-dependent, hides races), a process-wide cache (blast radius beyond the lease; retirement,
  dispatch guards and cleanup services would inherit staleness), caching `status`/`ls-files` (those
  are the live dirty checks), and reducing the number of `InspectAsync`/`RecheckSourceAsync` calls
  (the ordinals are pinned by `InjectInspection`-keyed tests and the double `IdentityAsync` is the
  snapshot-consistency check; the cache makes the second read free instead).

- **D-5 — Inspection scope: the protocol and resolver ask for identity and status only; cleanup keeps the ignored listing.**
  `ILandingGit.InspectAsync(coordinates, LandInspectionScope scope, ct)` with
  `enum LandInspectionScope { Full, IdentityAndStatus }`, default-implemented as the existing
  two-argument call (fakes keep working). `LandingGit` skips `ls-files --others --ignored` for
  `IdentityAndStatus` and returns an empty `IgnoredPaths`. Every `InspectAsync` in
  `AgentTaskLandingProtocol` and `AgentTaskLandSourceResolver` passes `IdentityAndStatus`;
  `GuardedWorktreeRemoval` passes `Full` explicitly. The dirty check (`status --porcelain
  --untracked-files=all`) is unchanged; ignored files are not "dirty" and never were part of it.
  Rejected: caching the ignored listing (its consumer must see the live tree before deletion).

- **D-6 — Remote source rechecks become one round trip, placed before each mutation; approval rechecks stay everywhere; the resolver is unchanged.**
  Split today's `RecheckRemoteSourceAsync` into `RecheckApprovalAsync` (final-review latch and
  request identity, DB only, kept at every boundary exactly where the remote recheck is today) and a
  remote recheck that calls the new `ILandingGit.RecheckSourceRemoteAsync(repository, sourceFullRef,
  expectedSha, expectedFingerprint, ct)` → `LandingSourceRecheck(Sha, Fingerprint, Reason)`. The
  real implementation reads the push endpoint, compares its fingerprint, runs one
  `ls-remote --refs --exit-code <endpoint> <ref>` and returns the SHA: no fetch, no pin ref. The
  default interface implementation returns `Reason = "source_recheck_unsupported"`, which the protocol
  refuses (errors are never absence). Remote rechecks run at five points: protocol entry for an
  existing operation (the `else` branch, which is also the fresh path once the resolver has attached
  the operation), immediately before the `rebase` mutation, before `ConfirmAsync` on the
  `exact_remote_containment` path, before the target advance mutation, and before the pre-push
  `ObserveAsync`. The Inspected, Prepared and Verified boundaries and the three intermediate
  RecoveryPinned points keep only the approval recheck: between them the land runs local reads and
  DB writes, and a remote change there is caught by the next pre-mutation recheck before anything
  is mutated, which is the property the protocol guarantees. `AgentTaskLandSourceResolver` keeps
  `ObserveSourceAsync` (authoritative observation plus confirmation fetch). Rejected: dropping remote
  rechecks after resolution (would let a branch pushed after review land), caching the remote
  answer, and making `ObserveSourceAsync` itself skip the fetch (the resolver's pin is what later
  ancestry checks rely on).

- **D-7 — Measure first: a per-land git profile in the log, no schema change.** The scope's
  `LandingGitProfile` counts processes, `worktree list` calls, cache hits, inspections, remote round
  trips (`ls-remote`, `fetch`, `push`) and summed git wall time. `AgentTaskLandService.RunRequestAsync`
  logs one Information line at exit: `Land git profile task={TaskId} request={RequestId}
  outcome={Outcome} wallSeconds={W} processes={N} worktreeList={N} registrationHits={N}
  canonicalHits={N} inspections={N} remote={N} gitSeconds={S}`. Read it with `scripts/logs.ps1`
  per [docs/logs.md](../../logs.md). Rejected: new `AgentTaskLanding` columns, a new event type, or
  widening the `landing` DTO (useful later, not needed to judge this card).

- **D-8 — Fakes evolve through default interface members; the ordinal re-keying is named, not discovered.**
  Every new `ILandingGit` member has a default implementation so the eight test implementors
  compile unchanged. `ControlledLandingGit` gains: a `diff --name-only --no-renames -z <a> <b>` and
  `<a>...<b>` arm returning `ChangedPaths` / `BaseDeltaPaths` (defaults `["src/change.cs"]` and
  `["src/base.cs"]`, i.e. buildable, so every existing verifier-count assertion holds);
  `RecheckSourceRemoteAsync` with a `SourceRemoteRechecks` counter, an `OnSourceRecheck(n)` hook and
  a `ls-remote` trace entry; and an `InspectionScopes` recorder. Tests keyed on
  `OnSourceObservation` ordinals 1–3 are resolver-side and unaffected; the one keyed on the
  protocol's first recheck — `AgentTaskLandSourcePersistenceTests` `checkpoint == "resolved" && n == 4`
  (line 139) — is re-keyed to `OnSourceRecheck(1)` with its invariant unchanged.
  `AgentTaskLandSourcePersistenceTests:357` and `AgentTaskLandSourceFreshnessTests:151` assert
  resolver-only counts and stay as they are.

- **D-9 — Two sequential Code rounds; R2 dispatches after R1 lands.** Both rounds edit
  `AgentTaskLandingProtocol.cs`, `LandingGit.cs`, `ILandingGit.cs` and `ControlledLandingGit.cs`;
  running them in parallel would guarantee a rebase conflict on the same methods.

## Design

### S1 — git profile (R1)

- `server/Application/Interfaces/ILandingGit.cs`: `ILandingOperationScope BeginOperationScope()`
  default `LandingOperationScope.None`; `interface ILandingOperationScope : IDisposable { LandingGitProfile Profile { get; } }`.
- `server/Application/Dtos/LandingDtos.cs`: `LandingGitProfile` (thread-safe counters, `Describe()`).
- `server/Infrastructure/Git/LandingGit.cs`: `ExecuteAsync` times each process and classifies it
  into the live scope's profile; `InspectAsync` counts inspections.
- `server/Application/Services/AgentTaskLandService.cs`: open the scope after the lease, `try/finally`
  around the post-admission body, log the line with the run outcome (`Held`/`Complete` plus the
  terminal event type when one was written).

### S2 — operation cache (R1)

- `LandingGit`: `AsyncLocal<OperationScope?>`; `ListRegistrationsAsync(repository)` consults the
  scope (stamp check, then cache) before `RequiredAsync(["worktree","list","--porcelain","-z"])`;
  `IdentityAsync` and `RegistrationsAsync` use it; `CanonicalDirectoryAsync` / `CommonDirectoryAsync`
  memoise successes in the scope. `ExecuteAsync` invalidates registrations per D-4 before the
  process starts (so a failed mutation still forces a re-list). The registration stamp reads
  `<common>/worktrees` via `Directory.GetLastWriteTimeUtc` + `Directory.EnumerateFileSystemEntries(...).Count()`;
  the common dir comes from the (cached) `CommonDirectoryAsync(repository)`.
- No change to `GuardedWorktreeRemoval` or `WorktreeManager` listing calls (they parse their own
  `RunAsync` output; the land's own `worktree remove` invalidates anyway).

### S3 — inspection scope (R1)

- `LandingDtos.cs`: `enum LandInspectionScope`. `ILandingGit`: the three-argument overload with a
  default. `LandingGit`: real implementation; the two-argument overload calls it with `Full`.
- `AgentTaskLandingProtocol.cs` (5 call sites) and `AgentTaskLandSourceResolver.cs` (5 call sites):
  `IdentityAndStatus`. `GuardedWorktreeRemoval.cs:59,65`: `Full`.
- `ControlledLandingGit`: implement the overload, record `InspectionScopes`, delegate to the
  existing body (its ignored simulation stays on for both scopes; only the recorder differs).

### S4 — remote recheck (R2)

- `LandingDtos.cs`: `LandingSourceRecheck(string? Sha, string? Fingerprint, string? Reason)`.
- `ILandingGit`: `RecheckSourceRemoteAsync` with the fail-closed default.
- `LandingGit`: implementation per D-6 (`EndpointAsync` → fingerprint → `ls-remote` → parse with the
  existing two-field validation; `source_remote_endpoint_changed`, `source_remote_missing` (exit 2),
  `source_remote_unreadable`, `source_remote_response_invalid`).
- `AgentTaskLandingProtocol`: `RecheckApprovalAsync` + `RecheckRemoteSourceAsync` placement per D-6;
  refusal reasons unchanged (`source_remote_changed`, or the recheck's own reason).
- `ControlledLandingGit`: implementation per D-8.

### S5 — non-buildable skip (R2)

- `server/Application/Settings/DelegationSettings.cs`: `LandNonBuildableGlobs` + validator rules.
- New `server/Application/Services/LandVerificationInputs.cs`: `static bool IsNonBuildable(IReadOnlyList<string> paths, IReadOnlyList<string> globs)`
  (pure), `static IReadOnlyList<string>? ParseNulSeparated(string output)`, and the reason constants.
- `ILandingGit`: `ChangedPathsAsync(repository, from, to, ct)` as a **default** interface method built
  on `RunAsync(["diff","--name-only","--no-renames","-z", from, to])` (so real and fake share it;
  the fake only adds the command arm), returning `LandingChangedPaths(IReadOnlyList<string>? Paths, string? Reason)`.
- `AgentTaskLandingProtocol` Prepared block: decision order `base_unchanged` → source delta → base
  delta (no derivation) → build; `VerificationCommand` per D-2; one Information log with reason,
  count and up to five sample paths. Constructor gains `IOptions<DelegationSettings>? delegationSettings = null`
  (the existing optional-parameter pattern; harnesses that omit it get the defaults).
- `AgentTaskLandingState.HasVerification`: the two arms per D-2.
- `AgentTaskLandService.RunRequestAsync`: Verify `StageOutcome` detail for the delta skips.

### S6 — docs (R2)

- `docs/orchestration-loop.md` §5 (the "conditional build" sentence, line 762): name the four skip
  reasons, the setting and its default, that an explicit `-Verify` always builds, and the profile
  log line.
- `docs/testing-and-build.md` line 83: after "Do not silently edit `LandVerifyFilter` or the
  production verifier as part of a documentation policy change", add that CARD-0642's skip rule is
  configuration (`Delegation:LandNonBuildableGlobs`), and that embedded-resource paths must never be
  added to it.
- `docs/logs.md`: one line under the server log section naming the `Land git profile` line.

## Code rounds

| Round | Slices | Files | Exit |
|---|---|---|---|
| R1 | S1, S2, S3 | `ILandingGit.cs`, `LandingDtos.cs`, `LandingGit.cs`, `AgentTaskLandService.cs`, `AgentTaskLandingProtocol.cs`, `AgentTaskLandSourceResolver.cs`, `GuardedWorktreeRemoval.cs`, `ControlledLandingGit.cs`, tests below | CP-1..CP-5 green; commit message carries the profile line from one real-git protocol test |
| R2 | S4, S5, S6 | `ILandingGit.cs`, `LandingDtos.cs`, `LandingGit.cs`, `AgentTaskLandingProtocol.cs`, `AgentTaskLandingState.cs`, `DelegationSettings.cs`, new `LandVerificationInputs.cs`, `AgentTaskLandService.cs`, `ControlledLandingGit.cs`, docs, tests below | CP-6..CP-10 green |

R2 is dispatched only after R1 has landed (D-9). After R2 lands and the server is restarted,
the orchestrator reads three `Land git profile` lines (one doc-only land, two code lands) and posts
them on the card; that is the card's closing evidence, compared against the "Today" column above.

## Verification design

Vocabulary: V-n is a new red-first test; R-n is an existing class that must stay green and is the
regression net for a slice. All tests are cross-platform: paths through `Path.Combine`, git through
`LandingGitFixture`/`ScratchGitRepo`, no Linux-only strings. Real-git classes carry
`[ParallelLimiter<ProcessSpawnLimit>]` as `LandingGitTests` does.

### Round 1

- **V-1** `LandingGitProfileTests` (new, Unit): `Record(["worktree","list",...], 1.2s)` increments
  `WorktreeLists` and `Processes`; `ls-remote`/`fetch`/`push` increment `RemoteRoundTrips`;
  `Describe()` renders every counter; counters are safe under `Parallel.For`. Red: type absent.
- **V-2** `LandingGitTests.C642_ScopeCachesRegistrationsUntilOwnMutation` (real git): inside
  `BeginOperationScope`, three `RegistrationsAsync` calls put exactly one `worktree list` in
  `Git.Trace`; after `RunAsync(["worktree","add",...])` through the same instance the next call
  re-lists (two in total). Red today: three.
- **V-3** `LandingGitTests.C642_InspectAsyncListsRegistrationsOncePerScope`: two `InspectAsync` calls
  in one scope → one `worktree list`, and the second call's snapshot equals the first. Red today: four.
- **V-4** `LandingGitTests.C642_ScopeSeesExternalRegistrationChange`: list once inside a scope, add a
  worktree with a raw `ScratchGitRepo.GitInAsync` (a different process, not the traced instance),
  then `RegistrationsAsync` includes the new path. This is the safety guard for the stamp; Code
  proves it can go red by disabling the stamp check locally before committing (report the red run).
- **V-5** `LandingGitTests.C642_IdentityAndStatusScopeSkipsIgnoredListing`: with an ignored file under
  `.antiphon/` in the source, `InspectAsync(c, IdentityAndStatus)` has no `ls-files` in the trace
  and empty `IgnoredPaths`; `InspectAsync(c, Full)` lists it. Red: overload absent.
- **V-6** `AgentTaskLandPublicationTests.C642_ProtocolInspectionsAreIdentityAndStatus` (fake): after a
  full land, every recorded `InspectionScopes` entry before the first `push` trace entry is
  `IdentityAndStatus`. Red: nothing recorded.
- **V-7** `AgentTaskLandRefusedRetryTests.C642_RealLandOpensOneScope` (real git through the
  service): a landed scenario's trace contains at most **12** `worktree list` entries (today about 47;
  the land itself lists once per invalidation and cleanup adds its own uncached lists), and the
  profile line was logged with `worktreeList` ≤ 12 and `inspections` ≥ 15.
- **R-1** existing: `LandingGitTests` (C448/C498 rows), `AgentTaskLandRefusedRetryTests`,
  `InterimVerificationLandGitTests`, `AgentTaskLandSourceFreshnessTests`, `LandSourceIdentityTests`,
  `LandingSourceFreshnessTests`, `WorktreeRemovalAuthorityTests` (cleanup keeps `Full`:
  `ignored_content_preserved` still refuses), `AgentTaskLandPublicationTests`,
  `LandingProtocolHarnessTests`, `LandingProtocolGuardTests`, `AgentTaskLandPreparationIdentityTests`,
  `ControlledLandingGitTests`, `AgentTaskLandBoundaryTests`, `AgentTaskLandCleanupSafetyTests`,
  `WorktreeLandingCleanupRetryTests`.

### Round 2

- **V-8** `AgentTaskLandPublicationTests.C642_RemoteSourceRechecksAreOnePerMutation` (fake): a full
  fresh land records `SourceObservationAttempts == 2` (resolver only) and `SourceRemoteRechecks == 4`
  (entry, pre-rebase, pre-advance, pre-push), each `ls-remote` trace entry immediately preceding the
  matching `rebase` / `merge --ff-only` (or `update-ref`) / `push` entry, and no `fetch` of the
  source ref after the second observation. Red today: 11 observations, 0 rechecks.
- **V-9** `AgentTaskLandSourceFreshnessTests.C642_RemoteMovementBeforeMutationRefuses(int recheck)`
  with arguments 2, 3, 4: `OnSourceRecheck(n)` advances the remote source at that recheck; the land
  refuses `source_remote_changed`, `OwnedTrace` holds no `rebase`/`merge`/`push` after that point,
  and `HasPublication` is false. Red: hook absent.
- **V-10** `AgentTaskLandingStateTests.C642_DeltaSkipArms`: arguments `non-buildable-source-delta`
  (accepted), `non-buildable-source-delta-with-filter` (refused), `non-buildable-source-delta-no-rebase`
  (refused), `non-buildable-base-delta` (accepted), `non-buildable-base-delta-derived` (refused);
  the existing `unknown-skip` row stays refused.
- **V-11** `LandVerificationInputsTests` (new, Unit): empty → non-buildable; `docs/a.md`, `README.md`,
  `LICENSE`, `docs/a b/c.md` → non-buildable; `docs/superpowers/plans/x.json`, `server/Bundles/x.md`,
  `Docs/a.md`, `server/A.cs`, `docs/a.md` + `server/A.cs` → buildable; empty glob list → buildable;
  NUL parsing keeps a space-containing path intact and drops empty entries; validator rejects `""`,
  `"  "`, `"/docs/**"`, `"C:/x"`, `"../x"`, `"\\x"` and accepts the defaults.
- **V-12** `AgentTaskLandVerificationSkipTests` (new, Integration, fake harness; base moved via
  `h.Git` target advance as `AgentTaskLandPublicationTests` does):
  (a) `ChangedPaths = ["docs/x.md"]` → `Verifier.Calls == 0`, reason `non_buildable_source_delta`,
  `VerificationPassed == false`, land completes, Verify `StageOutcome` is `Skipped` with the
  reason and command in its detail; (b) `["docs/x.md","server/A.cs"]` → `Calls == 1`, reason null;
  (c) docs-only delta but `-Verify` filter → `Calls == 1` with that filter; (d) source delta
  buildable, `BaseDeltaPaths = ["docs/plan.md"]` → `Calls == 0`, reason `non_buildable_base_delta`;
  (e) as (d) but the derivation scenario from `AgentTaskLandPreparationIdentityTests` → `Calls`
  unchanged from that test (build); (f) `BeforeCommand` returns exit 128 for the diff → `Calls == 1`,
  reason null (fail closed); (g) `ChangedPaths = []` → skip with reason `non_buildable_source_delta`.
- **V-13** `LandingGitTests.C642_ChangedPathsListRenamesAsBothNames` (real git): commit a rename of
  `docs/a.md` to `src/a.cs` on the source; `ChangedPathsAsync(repo, base, head)` returns both paths;
  the three-dot form returns the base's own commits only.
- **V-14** `LandingGitTests.C642_RecheckSourceRemoteIsOneRoundTripWithoutPins` (real git):
  `RecheckSourceRemoteAsync` puts exactly one `ls-remote` and no `fetch` in the trace, writes no
  `refs/antiphon/**` ref, returns the remote SHA and fingerprint; a mismatched expected fingerprint
  returns `source_remote_endpoint_changed`; a deleted remote branch returns `source_remote_missing`.
- **R-2** existing: `AgentTaskLandPublicationTests`, `AgentTaskLandPreparationIdentityTests`
  (`Calls.ShouldBe(2)` and `ShouldBe(0)` rows), `AgentTaskLandRefusedRetryTests` (`Verifier.Invocations`
  rows), `InterimVerificationLandGitTests` (filter always builds), `AgentTaskLandingStateTests`.
- **R-3** existing, re-keyed only where D-8 says: `AgentTaskLandSourceFreshnessTests`,
  `AgentTaskLandSourcePersistenceTests` (line 139 re-keyed), `AgentTaskLandFailureDiagnosticTests`,
  `LandingProtocolGuardTests`, `LandingProtocolHarnessTests`, `ControlledLandingGitTests`.

### Checkpoints

Test project `tests/Antiphon.Tests`; isolated outputs `bin-c642a/` (R1) and `bin-c642b/` (R2),
forward slash; `--property:UseAppHost=false` on server2 (the fakeclaude collision); one build per
round, every other row `--no-build`. `Min` is the number of `[Test]` methods in the named classes
plus the new methods (each method yields at least one result; argument expansion only adds), so it
is a floor, not a census. `EstimatedMinutes` are desktop wall-clock estimates including the build
on rows that build.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1-S3 | `tests/Antiphon.Tests -> bin-c642a/` | landing-git-real | `/*/*/LandingGitTests/*` | V-2, V-3, V-4, V-5, R-1 | all listed, 0 failed | 19 | 9 |
| CP-2 | S1-S3 | CP-1 | landing-unit | `/*/*/(LandingGitProfileTests*)\|(ControlledLandingGitTests*)\|(LandingRemovalPolicyControlTests*)/*` | V-1, R-1 | all listed, 0 failed | 17 | 2 |
| CP-3 | S1-S3 | CP-1 | landing-real-protocol | `/*/*/(AgentTaskLandRefusedRetryTests*)\|(InterimVerificationLandGitTests*)\|(AgentTaskLandSourceFreshnessTests*)\|(LandSourceIdentityTests*)\|(LandingSourceFreshnessTests*)\|(WorktreeRemovalAuthorityTests*)/*` | V-7, R-1 | all listed, 0 failed | 83 | 10 |
| CP-4 | S1-S3 | CP-1 | landing-fake-protocol | `/*/*/(AgentTaskLandPublicationTests*)\|(LandingProtocolHarnessTests*)\|(LandingProtocolGuardTests*)\|(AgentTaskLandPreparationIdentityTests*)/*` | V-6, R-1 | all listed, 0 failed | 42 | 6 |
| CP-5 | S1-S3 | CP-1 | landing-slow-cleanup | `/*/*/(AgentTaskLandBoundaryTests*)\|(AgentTaskLandCleanupSafetyTests*)\|(WorktreeLandingCleanupRetryTests*)/*` | R-1 (cleanup keeps `Full`; scope spans cleanup) | all listed, 0 failed | 26 | 10 |
| CP-6 | S4-S6 | `tests/Antiphon.Tests -> bin-c642b/` | verification-unit | `/*/*/(LandVerificationInputsTests*)\|(AgentTaskLandingStateTests*)\|(ControlledLandingGitTests*)/*` | V-10, V-11, R-2 | all listed, 0 failed | 30 | 6 |
| CP-7 | S4-S6 | CP-6 | verification-skip-protocol | `/*/*/(AgentTaskLandVerificationSkipTests*)\|(AgentTaskLandPublicationTests*)\|(AgentTaskLandPreparationIdentityTests*)\|(AgentTaskLandRefusedRetryTests*)\|(InterimVerificationLandGitTests*)/*` | V-8, V-12, R-2 | all listed, 0 failed | 38 | 8 |
| CP-8 | S4-S6 | CP-6 | remote-recheck-protocol | `/*/*/(AgentTaskLandSourceFreshnessTests*)\|(AgentTaskLandSourcePersistenceTests*)\|(AgentTaskLandFailureDiagnosticTests*)\|(LandingProtocolGuardTests*)\|(LandingProtocolHarnessTests*)/*` | V-9, R-3 | all listed, 0 failed | 84 | 8 |
| CP-9 | S4-S6 | CP-6 | landing-git-real-r2 | `/*/*/LandingGitTests/*` | V-13, V-14, R-1 | all listed, 0 failed | 21 | 5 |
| CP-10 | S4-S6 | n/a | docs-setting-named | `git grep -n "LandNonBuildableGlobs" -- docs/orchestration-loop.md docs/testing-and-build.md docs/logs.md` | S6 | 3 matching lines (one per doc), exit 0 | n/a | 1 |

The pipe characters inside the `Filter` cells are escaped for the table; the command line uses a
plain `|`, quoted as [docs/testing-and-build.md](../../testing-and-build.md#combined-class-filters-card-0403)
shows. Run each row with `scripts/run-checkpoint.ps1 -Name CP-n -Project tests/Antiphon.Tests
-OutputPath bin-c642a/ -Filter '<filter>' -MinExecuted <Min> -Expect <classes> -ResultsRoot .antiphon/c642-checkpoints`
(`-NoBuild` for reuse rows). Unlisted runs need a stated reason; a compile error found by a row's
own build is fixed and the same row rerun.

### Cost

Ordinary Code floor: R1 = 37 minutes of checkpoints plus authoring (about 2 h); R2 = 28 minutes of
checkpoints plus authoring (about 3 h, the state arms and the fake's new arms are the bulk).
`-ExpectAbout` for each Code dispatch is that sum. Review reads the CHECKPOINT lines against the
table; the R1 review additionally reads the profile line V-7 captured.

## Follow-ups (not in this card)

- **Registrations count.** 650 registrations is the multiplier on every `worktree list` the cache
  cannot remove (about eight per land after R1). The residue tooling (`scripts/worktree-residue.ps1
  -Action Preview`, retirement) is the lever; recommend the operator runs a preview and files a
  card if the prunable count is large.
- **Verification artifacts are retained.** Each build lands in a fresh
  `%TEMP%\antiphon-land-verify-<guid>` that is never deleted (`VerifyWithObserverAsync`: "Unique
  owned outputs are retained"). At ~17 builds a day this is hundreds of MB daily; a bounded
  retention sweep is a separate card.
- **Loose recovery refs.** Ten `source-recheck/<guid>` refs per land stop after R2, but existing ones
  (and `remote-observed`, `cleanup-observed` pins) accumulate under `refs/antiphon/land/**`; every
  fetch negotiation advertises them. Measure `git for-each-ref refs/antiphon | wc -l` on the
  desktop; a retention rule for completed operations is a separate card.
- **Process spawn cost.** After R1 a land still spawns ~450 git processes (18 per inspection).
  Combining `IdentityAsync`'s nine reads into two `rev-parse` invocations is a further ~30–40 %
  cut; do it only if the R2 profile still shows inspections above ~3 s each.
- **Cleanup observes.** The three `cleanup-observed` round trips are owned by CARD-0459/0543's
  guarded removal and stay as they are.

--- next stage ---
next: code
handoff: CARD-0642 R1 per docs/superpowers/plans/2026-09-24-card-0642-land-speed-plan.md: S1 git profile, S2 lease-scoped registration/canonical cache with own-mutation and worktrees-dir stamp invalidation, S3 IdentityAndStatus inspection scope; run CP-1..CP-5 as a closed list; R2 (S4-S6) only after R1 lands.
artifact: docs/superpowers/plans/2026-09-24-card-0642-land-speed-plan.md
