# CARD-0665: Land cleanup with disposable ignored content

Date: 2026-09-24. Plan task: fb612012 (server2 Linux runner). Next: **Code**.
Test design is folded into this plan (the brief commissions Code as the next stage).
Inspected base: 42eecdea2c6f682121b075383eaf1ba987057153.

## Outcome and boundaries

After this card, the guarded worktree remover classifies every git-ignored path in a
source worktree into one of three classes: **disposable** (deleted with the tree),
**evidence** (byte-copied to the retained report root first, then deleted with the tree)
and **protected** (refuses removal, as today). Removal proceeds only when the tree holds
no protected path and every evidence path has a verified retained copy. The refusal
`ignored_content_preserved` keeps its code and gains a detail naming the offending paths,
so an operator can extend the allowlist from evidence instead of guessing.

The rule lives in one place, `GuardedWorktreeRemoval.RemoveDirectoryAsync`, so the
Publication lane (land cleanup and its scheduled retries), LocalMerge and SettledTask
retirement all get it. `WorktreeResidue:Execute` stays `false` in shipped configuration;
this plan records the decision, adds the retry cooldown that makes activation safe, and
documents the operator activation steps.

Out of scope: CARD-0664's never-released consumer reservations (a separate plan; the
SettledTask lane stays refused at claim time until it lands); a release policy for the
188 never-landed Plan/Review/Investigate worktrees (needs its own card, see Follow-ups);
CARD-0642 land speed; the Verification-purpose remover (`GuardedVerificationRemoval`
never consulted ignored content); delegates' own `bin-*` hygiene; any database migration.

Primary inputs: the card text, the
[CARD-0664 investigation](../../investigations/2026-09-24-card-0664-consumer-slot-release.md),
the [CARD-0459 investigation](../../investigations/2026-09-19-card-0459-worktrees-never-get-cleaned-up.md)
§3, the [CARD-0459 plan](2026-09-19-card-0459-worktree-cleanup-plan.md) D-6, and CARD-0452
(Backlog, whose D2 item this card decides; V-07/V-21/V-29 and R1-R3 stay on CARD-0452).

## Ground truth

| Card/brief assumption | What the code actually does at the inspected base | Design consequence |
|---|---|---|
| Land cleanup refuses whenever any ignored file is present. | Confirmed. `LandingGit.InspectAsync` runs `git ls-files --others --ignored --exclude-standard -z` with no `--directory`, so every ignored **file** is listed (`server/Infrastructure/Git/LandingGit.cs:144-152`). `GuardedWorktreeRemoval.HasProtectedIgnored` is `IgnoredPaths.Length != 0` and runs at both inspections (`GuardedWorktreeRemoval.cs:62`, `:67`, `:252`). Non-forcing `git worktree remove` then deletes ignored files itself (comment at `:61`). | Replace the length check with a classifier at both readings; no pre-deletion pass is needed because git removes ignored files with the tree. |
| 57 of 86 lands since 09-19 end `cleanup=Refused: ignored_content_preserved`. | Taken from the CARD-0664 investigation's API read; not re-measured here (no desktop DB from server2). The mechanism is pinned by `AgentTaskLandPublicationTests.C448_V04_IgnoredFilesSurviveConfirmedPublication` and the 35,446-path Code tree in the CARD-0459 investigation. | Treat the count as motivation, not as a checkpoint value. |
| Agents cannot delete `bin-*` under the command policy. | Not verified: `.claude/settings.json` has no deny rule, and the delegate-basics bundle tells delegates to delete their `bin-<name>` directories. `scripts/cleanup-build-junk.ps1` deletes aged `bin-*` weekly on the desktop only. | Treat `bin-*` as "routinely left behind", which is enough; the policy does not depend on why. |
| TRX and checkpoint evidence live in the worktree. | `scripts/run-checkpoint.ps1` writes `<ResultsRoot>/<CP>-<stamp>/*.trx` with `ResultsRoot` defaulting to `.antiphon/checkpoints` (`:21`, `:55`); plans use `.antiphon/c<card>-checkpoints`. The Code report's `CHECKPOINT` line carries `trx=<path>` (`docs/testing-and-build.md:135`). Nothing copies them out of the tree today. | Evidence class: `.antiphon/**/*.trx` and `.antiphon/*checkpoints*/**` are copied to the retained root before removal. |
| The deliverable is extracted to `.antiphon/deliverables`. | `DeliverableBundleService` writes under `FirstNonEmpty(task.RepoPath, task.WorkingDirectory)/.antiphon/deliverables/<short>` (`:130-138`); for Worktree tasks `RepoPath` is the canonical repository, so bundles are already outside the tree. Reports ≥ `Delegation:DistillMinChars` for Session-reply non-specialist tasks are stored by `AgentReportStore` at `<main checkout>/.antiphon/reports/<taskId>/<sha256>.md` (`AgentReportStore.cs:107-132`), which refuses any root inside the worktree (`:134-137`). Otherwise `task.ResultFilePath` may point at the spill `<WorkingDirectory>/.antiphon/task-<short>.md` (`AgentTaskReplyService.cs:2576-2600`), which is inside the tree when `WorkingDirectory` is the worktree. | The report spill `.antiphon/task-<8 hex>.md` is evidence. After retention, any task artifact pointer inside the tree must be in the retained set or removal refuses `artifact_unpreserved`, mirroring `TaskWorktreeRetirementService.ArtifactsRetrievableAsync` (`:517-533`), which the Publication lane lacks today. |
| Briefs are disposable. | Server-written inbox content is `.antiphon/inbox/<uuid>.md` (brief), `.antiphon/inbox/<stem>.md` (typed-body spills, `TypedBodySpill.cs:93-96`), `.antiphon/inbox/spawn-*.md`, `.antiphon/task-<short>-brief.md` (`AgentTaskDispatcher.cs:4834-4856`) and `.antiphon/task-<short>-refinement-<stamp>.md` (`AgentTaskReplyService.cs:566-571`). Channel attachments (photos, files) also land in `.antiphon/inbox` with their own extensions (`ChannelPreamble.cs:72`). | Only `.antiphon/inbox/*.md` and the two `task-*` spill shapes are disposable; a non-`.md` inbox file is protected. |
| The residue sweep needs its own copy of the policy. | The Publication lane only re-queues `AgentTaskLandService.RequestCleanupRetryAsync` (`WorktreeResidueSweepService.cs:235-258`), which re-runs the same protocol and remover. The SettledTask lane calls `TaskWorktreeRetirementService.TryRetireAsync`, which reaches the same `RemoveDirectoryAsync`. | One implementation in the remover covers every lane; the sweep is not touched for policy. |
| `WorktreeResidue:Execute` defaults false, so retirement never runs. | Confirmed (`WorktreeResidueSettings.cs:23`, `server/appsettings.json:155`). Also: `PersistCooldownAsync` writes `WorktreeResidueCandidateCursors.NotBefore` (`:318-337`) but **no production code reads it** (repo-wide `NotBefore` readers are routing pins only); `C459_CooldownSurvivesRestart` passes because nothing is released, not because the cooldown is enforced. The Publication lane has no cooldown and no ordering; with `Execute=true` a permanently protected tree would be re-queued every day against a 25-action budget. | D-7: keep the default; make both lanes consult the cursor and give the Publication lane a 7-day per-operation cooldown before activation is recommended. |
| An allowlist is the right shape. | `.gitignore` covers secrets and user-local state: `appsettings.*.json`, `*.user`, `.claude/*` except `settings.json`/`skills/`, `scripts/nightly-watchdog/*.local.json`, `logs/`, `backups/`. A "delete everything ignored" rule would erase them. | D-1: allowlist with unknown → protected. |
| Existing tests pin total refusal. | `C448_V04_IgnoredFilesSurviveConfirmedPublication` (`.antiphon/report.md`, `.claude/settings.local.json`, `bin-private/data.txt`); `LandingRemovalPolicyControlTests.C448_V18` (`bin-private/keep.txt`, fake git, 3-arg constructor); `WorktreeGuardedCleanupTests.C443_*IgnoredBoundary` (`bin-private/keep.txt`); `AgentTaskLandCleanupSafetyTests.C448_V18_CleanupRetryPreservesChangedWork` and `WorktreeResidueSweepTests.C459_IgnoredInventoryReachesPolicy` (`.antiphon/report.md`). | Cases using `bin-private/...` move to a protected path (`.claude/settings.local.json`) and gain a disposable twin; `.antiphon/report.md` is not on any allowlist and stays protected, so those tests are unchanged. |
| Test fixtures use real git. | `LandingGitFixture` seeds `.gitignore` with `.antiphon/`, `.claude/`, `bin-*/` (`LandingGitFixture.cs:41`); `LandingSafetyHarness` builds `GuardedWorktreeRemoval` explicitly (`:72-75`); `DelegationTestServices` registers it (`:89`) and builds one (`:125`); `LandingRemovalPolicyControlTests` and `DelegationWorktreeTests` use the 3-argument constructor. | Add `obj/` to the fixture ignore list; the gate is an optional constructor parameter (D-6). |
| Globbing needs a new dependency. | `Microsoft.Extensions.FileSystemGlobbing` 10.0.5 is already referenced (`server/Antiphon.Server.csproj:49`) and used on string lists by `ScopeDriftPolicy` (`:115-131`). | Reuse `Matcher(StringComparison.OrdinalIgnoreCase).Match(paths)`. |
| Paths are OS-specific. | `ls-files -z` emits worktree-relative, `/`-separated paths on both OSes; `LandSourceSnapshot.IgnoredPaths` keeps them verbatim (`LandingDtos.cs:25-27`). | Classify on the relative string; tests build inputs with `/` and file paths with `Path.Combine`; no `/tmp` or drive letters in tests. |

## Decisions

- **D-1: Allowlist with three classes; unknown is protected.** Disposable, Evidence and
  Protected. A path that matches no allowlist pattern is protected and refuses removal
  exactly as today. Rejected: "delete anything ignored" (erases `appsettings.*.json`,
  `*.user`, `.claude/settings.local.json`); a per-land force flag (CARD-0452's other
  option) because the failure is systematic for every built Code tree, not a per-land
  exception an operator could reasonably grant one land at a time.
- **D-2: Default patterns, operator-configurable.** New section `WorktreeCleanup` with
  `DisposableIgnored` and `RetainedIgnored` string arrays (defaults in code and listed in
  `server/appsettings.json`), validated on start. Patterns are worktree-relative globs with
  `/` separators; `..`, a leading `/`, a drive letter or a backslash is a startup failure.
  Defaults:
  - Disposable: `**/bin/**`, `**/obj/**`, `**/bin-*/**`, `**/node_modules/**`, `**/dist/**`,
    `**/storybook-static/**`, `**/.tmp/**`, `**/out/**`, `.antiphon-cache/**`,
    `.antiphon/inbox/*.md`, `.antiphon/task-*-brief.md`, `.antiphon/task-*-refinement-*.md`.
  - Evidence: `.antiphon/task-????????.md` (the report spill; the short id is 8 hex chars,
    `DelegationReportFormatter.Short`), `.antiphon/**/*.trx`, `.antiphon/*checkpoints*/**`.
  - Precedence: Evidence before Disposable before Protected. When an operator's lists
    overlap, the copy wins; `task-*-brief.md` does not match the 8-character evidence
    pattern, so briefs are not copied.
  - Deliberately protected by default: `.antiphon/report.md` and any other `.antiphon`
    file not named above, `.antiphon/inbox/<non-md>` (channel attachments),
    `.antiphon/deliverables/**`, `.claude/**`, `appsettings.*.json`, `*.user`, `logs/**`,
    `backups/**`, `.superpowers/**`, `.memsearch/**`, `tests/Antiphon.E2E/TestOutput/**`.
    The refusal detail (D-8) is how an operator learns which of these to add.
  Rejected: a hard-coded list (a new build-output name means a code change); matching on
  the `.gitignore` rule that ignored the path (git does not report it from `ls-files`).
- **D-3: Evidence is copied, byte-verified, before the second reading.** Retained root is
  the same root `AgentReportStore` resolves (`<main checkout>/.antiphon/reports`, else
  `Delegation:ReportStorageRoot`), never inside the worktree or a temp directory. Files go
  to `<root>/<taskId D>/worktree-evidence/<relative>` via temp file + `File.Move(overwrite)`
  + SHA-256 read-back; `manifest.json` (relative, bytes, sha256, source worktree, attempt id)
  is written last. Re-running on a retry is idempotent (same bytes reused, different bytes
  replaced). Caps: `MaxRetainedEvidenceBytes` 64 MiB and `MaxRetainedEvidenceFiles` 500,
  measured before copying; exceeding refuses `evidence_retention_exceeded`. No root refuses
  `evidence_retention_unavailable`; I/O failure `evidence_retention_failed`; a 60 s budget
  `evidence_retention_timeout`. Rejected: zipping (harder to inspect from a report line);
  storing in the database (blobs); deleting evidence uncopied (Review/Mutation may open a
  TRX after land).
- **D-4: Task artifact pointers inside the tree must be retained.** After retention, every
  non-null `ResultFilePath`, `DeliverablePath`, `DeliverableBundleDir`, `DeliverablePdfPath`
  that lies inside the worktree must be in the retained set, else refuse
  `artifact_unpreserved`. When cleanup completes, `AgentTaskLandService` rewrites
  `task.ResultFilePath` to the retained absolute path if it pointed inside the tree.
  Rejected: copying deliverable bundles from inside the tree (they are written to
  `RepoPath` in production; a bundle inside the tree is a misconfiguration to surface, not
  to paper over).
- **D-5: One gate in `RemoveDirectoryAsync`, both readings.** First reading: any protected
  → refuse; retain evidence; then the existing authority refresh and second reading; second
  reading: any protected → refuse; the evidence path set must equal the first reading's or
  refuse `ignored_content_changed`; disposable churn between readings is allowed. Rejected:
  a pass in `AgentTaskLandingProtocol.CleanupAsync` (SettledTask and LocalMerge would not
  benefit); deleting disposable files ourselves before `git worktree remove` (a second
  deletion path with no new safety).
- **D-6: Null gate means protect-all.** `GuardedWorktreeRemoval` takes an optional
  `WorktreeIgnoredContentGate? ignored = null`; null reproduces today's total refusal so an
  unwired composition stays fail-closed. Production DI, `DelegationTestServices` and
  `LandingProtocolHarness` wire the real gate. The 3-argument fake-git fixtures keep null
  and keep proving "each reading refuses before its next command".
- **D-7: `WorktreeResidue:Execute` stays `false` in code; activation is an operator step.**
  Reasons: it is CARD-0328's rollout switch and `docs/orchestration-loop.md:1158-1159` /
  `docs/testing-and-build.md:71` document it as a commissioned deploy; flipping the default
  would change every deployment's nightly behaviour on upgrade; the SettledTask lane cannot
  act before CARD-0664 lands; the never-landed trees need a release policy that does not
  exist; and the sweep's cooldown is not enforced today (Ground truth). S4 fixes the
  cooldown so activation cannot loop, and the Operator activation section gives the order:
  land this card, run a preview, set `Execute=true`, then land CARD-0664. Rejected: default
  `true` in this card; a new "PublicationOnly" execute mode (extra surface for a one-time
  activation).
- **D-8: Refusals name the paths without changing the code.** `WorktreeRemoval` and
  `LandingProtocolResult` gain `Detail`; the land outcome event becomes
  `cleanup=Refused: ignored_content_preserved; protected: a/b.user, .claude/x (+3)` and a
  complete cleanup appends `retained=<n> files at <dir>`. `LastReason` keeps the bare code so
  every exact-match consumer and test stays valid. Up to five paths, total detail ≤ 400
  characters, clipped with `WorktreeCleanupPresentation.Clip`. No new column, no migration.
- **D-9: Settings, not constants, with validation.** `WorktreeCleanupSettings` bound to
  `WorktreeCleanup` beside `WorktreeResidue` in `Program.cs`, validated on start like
  `WorktreeResidueSettingsValidator`. `WorktreeResidueSettings` gains
  `PublicationRetryCooldownHours` (default 168, validator ≥ 1).
- **D-10: Platform-neutral tests.** No `/tmp`, no `C:\`, no backslash literals, no
  `OperatingSystem` branches in new tests; classifier inputs use `/` exactly as git emits;
  fixtures use `Path.Combine` on fixture roots. Symlink handling relies on
  `FileAttributes.ReparsePoint`, which .NET sets for Unix symlinks too.
- **D-11: The Windows lock diagnostics and the two-slot retry stay as they are.** A
  `bin-*` DLL held open on Windows still surfaces as `worktree_remove_failed` with the
  CARD-0443 capture and one retry; this card does not add a third slot.

## Design

### Classification (Application, pure)

`server/Application/Services/WorktreeIgnoredContentClassifier.cs`, constructed from
`IOptions<WorktreeCleanupSettings>`, builds two `Matcher(StringComparison.OrdinalIgnoreCase)`
instances once. `Classify(ImmutableArray<string> ignoredPaths)` normalises each path
(`\` → `/`, strip a leading `./`), routes any rooted path or `..` segment straight to
Protected, then Evidence match → Disposable match → Protected. It returns
`WorktreeIgnoredContent(ImmutableArray<string> Disposable, Evidence, Protected)` (new record
in `server/Application/Dtos/WorktreeIgnoredContent.cs`) with each array sorted ordinally so
two readings compare by `SequenceEqual`. Empty configured lists classify everything as
Protected, which is today's behaviour.

### Retention (Application interface, Infrastructure implementation)

`IWorktreeEvidenceRetention.RetainAsync(AgentTask task, string worktreePath,
ImmutableArray<string> relativePaths, Guid? attemptId, CancellationToken ct)` returns
`WorktreeEvidenceRetention(string? Root, ImmutableArray<RetainedFile> Files, string? Reason)`
with `RetainedFile(string Relative, string RetainedPath, long Bytes, string Sha256)`.
`server/Infrastructure/Files/WorktreeEvidenceRetention.cs` implements D-3 and D-4's pointer
check (it already holds the task row). Root resolution, `IsPersistent`, `EnsureIgnoredAsync`,
`HasReparseAncestor`, `Within` and `SamePath` move from `AgentReportStore` into a shared
`server/Infrastructure/Files/AgentReportRoot.cs`; `AgentReportStore` keeps its public
behaviour and tests. Until S3 lands, S2 registers `RefusingEvidenceRetention` (Application
nested type): success with no files when `relativePaths` is empty, otherwise
`evidence_retention_unavailable`, so an evidence tree is refused rather than deleted between
rounds.

### Gate (Infrastructure/Git)

`WorktreeIgnoredContentGate(WorktreeIgnoredContentClassifier classifier,
IWorktreeEvidenceRetention retention, IWorktreeRemovalEvidence evidence)` exposes
`Classify(LandSourceSnapshot)` and `RetainAsync(WorktreeRemovalRequest, ImmutableArray<string>
evidence, CancellationToken)`. The latter reads the task through a new
`IWorktreeRemovalEvidence.ReadTaskAsync(Guid taskId, CancellationToken)` (default
implementation returns null; `WorktreeRemovalEvidence` reads `AgentTasks` `AsNoTracking` in
its own scope like `ReadAsync`), refuses `artifact_pointers_unavailable` on null, and calls
the retention. `RemoveDirectoryAsync` changes only around lines 59-67:

```
var inspection = await git.InspectAsync(source, ct);
if (!Matches(inspection, request)) return Finish(inspection.Reason ?? "source_changed");
var first = Classify(inspection.Snapshot!);                       // gate null → all Protected
if (first.Protected.Length != 0) return Finish("ignored_content_preserved", ProtectedDetail(first));
var retained = await RetainAsync(request, first.Evidence, ct);    // gate null + empty → no-op
if (retained.Reason is not null) return Finish(retained.Reason);
reason = await AuthorityAsync(request, ct);
if (reason is not null) return Finish(reason);
var final = await git.InspectAsync(source, ct);
if (!Matches(final, request)) return Finish(final.Reason ?? "source_changed");
var second = Classify(final.Snapshot!);
if (second.Protected.Length != 0) return Finish("ignored_content_preserved", ProtectedDetail(second));
if (!second.Evidence.SequenceEqual(first.Evidence, StringComparer.Ordinal)) return Finish("ignored_content_changed");
```

`Finish(reason, detail)` carries `Detail` on `WorktreeRemoval` (new optional init member; a
clean result carries `retained=<n> files at <dir>` when n > 0). `WorktreeGuardedCleanup`
already propagates results with `with`, so `Detail` survives the capture/retry path.
`AgentTaskLandingProtocol.CleanupAsync` returns `new(op, removed.Residue, []) { Detail =
removed.Detail }`; `AgentTaskLandService` appends it to the cleanup stage record and the
`FormatOutcome` event via the existing `AppendDetail`, and rewrites `task.ResultFilePath`
per D-4. `TaskWorktreeRetirementService.TryRetireAsync` logs the detail; the sweep's
candidate row keeps `ReasonCode = Residue`.

### Sweep cooldown (S4)

`WorktreeResidueSweepService`: a private `InCooldownAsync(key, now)` reads
`WorktreeResidueCandidateCursors` for `CandidateKey == key && NotBefore > now`. SettledTask
lane: a cooling-down task is Held with `ReasonCode = "cooldown"` before `TryRetireAsync`.
Publication lane: landings ordered by `CleanupCompletedAt ?? UpdatedAt`; key
`landing:<opId:N>`; cooling-down operations are Held `retry_cooldown`; a queued retry
persists the cursor with `PublicationRetryCooldownHours`. `PersistCooldownAsync` takes the
duration as a parameter (SettledTask keeps 24 h).

## Slices and bounded Code rounds

Three Code rounds, each under about 90 minutes of authoring plus its checkpoint rows.
Every round commits its red tests first (the red commit compiles; only assertions fail),
then the implementation, and runs the rows listed for it.

### Round A (S1 + S2): classifier, settings, gate seam, wiring (~85 min)

- **S1** `server/Application/Settings/WorktreeCleanupSettings.cs`,
  `WorktreeCleanupSettingsValidator.cs`, `Program.cs` binding (next to `WorktreeResidue`,
  `:181-183`), `server/appsettings.json` section with the D-2 defaults,
  `server/Application/Dtos/WorktreeIgnoredContent.cs`,
  `server/Application/Services/WorktreeIgnoredContentClassifier.cs`.
  Tests: `tests/Antiphon.Tests/Application/WorktreeIgnoredContentClassifierTests.cs`
  (`[Category("Unit")]`), `WorktreeCleanupSettingsValidatorTests.cs` (Unit).
- **S2** `server/Application/Interfaces/IWorktreeEvidenceRetention.cs` (+ DTOs, +
  `RefusingEvidenceRetention`), `IWorktreeRemovalEvidence.ReadTaskAsync` default and
  `WorktreeRemovalEvidence` implementation, `server/Infrastructure/Git/WorktreeIgnoredContentGate.cs`,
  `GuardedWorktreeRemoval` gate + `Detail`, `WorktreeRemoval.Detail`,
  `LandingProtocolResult.Detail` through `CleanupAsync`, `AgentTaskLandService` detail on the
  cleanup stage and outcome event, DI in `Program.cs:364` area and
  `tests/Antiphon.Tests/TestHelpers/DelegationTestServices.cs` (`:89`, `:125`),
  `LandingProtocolHarness.cs:72-75`, `LandingGitFixture.cs:41` (`obj/`).
  Tests: `LandingRemovalPolicyControlTests` (fixture gains an `IgnoredPaths` list, a stub
  `AgentTask`, a recording `IWorktreeEvidenceRetention`; `C448_V18` argument
  `bin-private/keep.txt` → `.claude/settings.local.json`; new `C665_*` methods in V-2),
  `WorktreeGuardedCleanupTests` (`ContentBoundaryAsync` ignored path →
  `.claude/settings.local.json`; new methods in V-3), `AgentTaskLandPublicationTests`
  (`C448_V04` arguments → `.antiphon/report.md`, `.claude/settings.local.json`,
  `.antiphon/inbox/photo.png`; new methods in V-4).

### Round B (S3): evidence retention and artifact pointers (~85 min)

- `server/Infrastructure/Files/AgentReportRoot.cs` (extracted from `AgentReportStore`),
  `server/Infrastructure/Files/WorktreeEvidenceRetention.cs` (D-3, D-4), DI replaces
  `RefusingEvidenceRetention`, `AgentTaskLandService` `ResultFilePath` rewrite,
  `TaskWorktreeRetirementService.TryRetireAsync` detail log.
  Tests: `tests/Antiphon.Tests/Infrastructure/WorktreeEvidenceRetentionTests.cs`
  (`[Category("Integration")]`, real git temp repository, no database) and
  `tests/Antiphon.Tests/Application/AgentTaskLandEvidenceRetentionTests.cs`
  (`LandingSafetyHarness`, real git + isolated schema, `[Category("Slow")]`).

### Round C (S4 + S5): sweep cooldown and documentation (~55 min)

- **S4** `WorktreeResidueSweepService` cooldown reads and Publication ordering,
  `WorktreeResidueSettings.PublicationRetryCooldownHours` + validator.
  Tests: `WorktreeResidueSweepTests` new `C665_*` methods (V-7, V-8).
- **S5** `docs/bootstrap.md` (a `WorktreeCleanup` configuration paragraph under the
  Hangfire table and an "Activating WorktreeResidue:Execute" procedure),
  `docs/orchestration-loop.md:1152-1162` (three-class rule, retained evidence path, detail
  naming), `docs/testing-and-build.md:73` and `:99` (CARD-0452 wording → CARD-0665
  allowlist; fixture `.gitignore` now includes `obj/`), CARD-0452 gets a card note from the
  orchestrator that D2 is decided and implemented here (not a doc edit).

## Operator activation of `WorktreeResidue:Execute`

1. Land CARD-0665 (all three rounds) and restart the AppHost from the main checkout.
2. `POST /api/agent-tasks/worktree-residue/preview` (or `scripts/worktree-residue.ps1
   -Preview`): expect Publication-lane candidates with `publication_retry` and, for
   SettledTask, `release_required` or `retirement_claim_refused` until CARD-0664 lands.
3. Set `WorktreeResidue:Execute=true` in the desktop's `appsettings` override and restart.
   The next 10:00 run queues up to `MaxActionsPerRun` (25) cleanup-only land requests; a
   protected tree is held for `PublicationRetryCooldownHours` after each attempt, so the
   backlog drains in about three nightly runs without starving on refusals. Raise
   `MaxActionsPerRun` for one run if a faster drain is wanted.
4. Land CARD-0664; the SettledTask lane then retires explicitly released trees.
5. Read the outcome events: `cleanup=Refused: ignored_content_preserved; protected: …`
   lists what to add to `WorktreeCleanup:DisposableIgnored` if it is genuinely reproducible.

## Follow-ups

- Card: a release policy for settled, never-landed non-Code worktrees (188 at the
  investigation's read). Plan/Review/Investigate/TestDesign trees have no publication and no
  automatic release today; `POST /api/agent-tasks/{id}/worktree-retirement` is manual.
- CARD-0452 keeps V-07/V-21/V-29 and R1-R3; its D2 is closed by this card.

## Verification design

Ordinary Code verification follows the `### Checkpoints` table as a closed list. On
server2 a cold isolated build of `tests/Antiphon.Tests` measured 3m13s and an incremental
rebuild 1m23s (CARD-0664 investigation); the `EstimatedMinutes` column includes the build
when the row builds. Build with `--property:OutputPath=bin-c665-<x>/` (forward slash) and
`--property:UseAppHost=false` (the FakeClaude apphost collision on this runner); when
`scripts/run-checkpoint.ps1` is used, pass the extra property through `-DotnetShim` or run
the two `dotnet` commands by hand and print the identical `CHECKPOINT` line. Filters use
CARD-0403 combined-class syntax with trailing class wildcards, or the method-segment form
`/*/*/Class/C665_*`. Results directories are fresh, under `.antiphon/c665-checkpoints/`
(which this card's own evidence class will retain on land). Delete every `bin-c665-*`
directory this producer created before finishing. Existing red is confirmed on the base
commit with the exact failing method, never a full suite.

### Coverage and falsifiable assertions

| ID | Class / new executions | Behavioural oracle and expected baseline red |
|---|---|---|
| V-1 | `WorktreeIgnoredContentClassifierTests` (~24 results: `Default_disposable_paths_are_disposable` ×8 rows, `Default_evidence_paths_are_evidence` ×3, `Default_protected_paths_are_protected` ×7, `Unknown_path_is_protected`, `Evidence_precedes_disposable_on_overlap`, `Rooted_or_parent_segment_is_protected` ×3, `Empty_lists_protect_everything`) | Inputs are `/`-separated relative strings (e.g. `server/obj/project.assets.json`, `client/node_modules/x/y.js`, `tests/Antiphon.Tests/bin-c665/a.dll`, `.antiphon/inbox/1e3f.md`, `.antiphon/task-ab12cd34-brief.md` → Disposable; `.antiphon/task-ab12cd34.md`, `.antiphon/c665-checkpoints/CP-1-x/run.trx` → Evidence; `.claude/settings.local.json`, `server/appsettings.Development.json`, `x.user`, `.antiphon/report.md`, `.antiphon/inbox/photo.png`, `.antiphon/deliverables/ab12cd34/x.md`, `logs/a.log` → Protected). Red at S1-red because the committed classifier stub returns every path as Protected. |
| V-9 | `WorktreeCleanupSettingsValidatorTests` (3: valid defaults, rejects `../x` / `/abs` / `C:\x` / backslash / empty, rejects caps ≤ 0) | Validation failures name `WorktreeCleanup:<key>`. Red with the stub validator that accepts everything. |
| V-2 | `LandingRemovalPolicyControlTests`: `C448_V18` rows updated; new `C665_NullGateProtectsEveryIgnoredPath` (3-arg ctor, `obj/a.json` → `ignored_content_preserved`, zero mutations), `C665_DisposableOnlyProceedsToRemoval` (gate; `obj/a.json`, `bin-x/a.dll` → `worktree remove` recorded, clean), `C665_ProtectedRefusalNamesPaths` (gate; `.claude/settings.json` + `x.user` → code exact, `Detail` contains both relative paths), `C665_EvidenceRetainedBeforeSecondReading` (recording retention receives `.antiphon/task-0123abcd.md` once, before inspection 2; then removed; `Detail` starts with `retained=1`), `C665_RetentionRefusalPreservesTree` (retention returns `evidence_retention_exceeded` → residue equals it, zero mutations), `C665_EvidenceAppearingAtSecondReadingRefuses` (`AfterInspection` adds an evidence path at reading 2 → `ignored_content_changed`, zero mutations) | Fake git, Unit. Red at S2-red: with the gate constructed but `RemoveDirectoryAsync` still on `HasProtectedIgnored`, disposable/evidence cases refuse and `Detail` is null. |
| V-3 | `WorktreeGuardedCleanupTests`: four `C443_*IgnoredBoundary` rows now inject `.claude/settings.local.json`; new `C665_DisposableInjectedBetweenReadingsIsRemoved` (`bin-private/keep.txt` at reading 2 → `IsClean`, one remove) and `C665_EvidenceInjectedBetweenReadingsIsRefused` (`.antiphon/task-0123abcd.md` at reading 2 → `ignored_content_changed`, file bytes intact) | Real git + DB through the two-slot cleanup path, proving the gate runs inside both readings and the capture/retry path keeps `Detail`. |
| V-4 | `AgentTaskLandPublicationTests`: `C448_V04` rows updated; new `C665_DisposableOnlyIgnoredContentIsRemovedOnLand` (`obj/project.assets.json`, `bin-c665/out.dll`, `.antiphon/inbox/brief.md`, `.antiphon/task-<short>-brief.md` → `Cleanup=Complete`, directory absent, `LastReason` null, `Landed` event detail contains `cleanup=Complete`), `C665_ProtectedIgnoredRefusalNamesPaths` (`.claude/settings.local.json` plus `obj/x` → `Refused`, `LastReason == "ignored_content_preserved"`, `LandedWithResidue` detail contains `protected: .claude/settings.local.json`, bytes intact, remote source intact) | End-to-end land through `LandingSafetyHarness`. Red at S2-red for the same reason as V-2. |
| V-5 | `WorktreeEvidenceRetentionTests` (~8: copies under `<root>/<taskId>/worktree-evidence/`, bytes and sha256 verified, manifest last, idempotent rerun reuses, changed bytes replaced, over-cap refuses `evidence_retention_exceeded` before any copy, root inside worktree/temp refuses `evidence_retention_unavailable`, pointer inside tree not in set refuses `artifact_unpreserved`, pointer in set accepted) | Real git temp repository (a `.git` main checkout so `AgentReportRoot` resolves `<main>/.antiphon/reports`); no database. Red at S3-red because the `RefusingEvidenceRetention` seam is still registered / the class does not exist yet (add the compiling seam and the tests in the red commit). |
| V-6 | `AgentTaskLandEvidenceRetentionTests` (4: `C665_EvidenceRetainedThenTreeRemoved` — TRX under `.antiphon/c665-checkpoints/CP-1-x/run.trx` and `.antiphon/task-<short>.md` with `task.ResultFilePath` pointing at it → cleanup Complete, retained files byte-equal, `task.ResultFilePath` now the retained path, event detail contains `retained=2`; `C665_RetentionUnavailableKeepsTree` — retention double returns `evidence_retention_unavailable` via `ConfigureServices` → Refused, tree intact; `C665_DeliverableInsideTreeRefusesArtifactUnpreserved` — `task.DeliverableBundleDir` inside Source with a protected file absent → `artifact_unpreserved`; `C665_RetryAfterRetentionIsIdempotent` — first attempt's `worktree remove` fails through `BeforeCommand`, repost, second land completes with one manifest) | Real git + DB. Red at S3-red with the refusing seam. |
| V-7 | `WorktreeResidueSweepTests.C665_SettledRetirementRemovesDisposableOnly` (`SettledRemovalHarness`; `bin-x/a.dll` only → `IsClean`) | Proves the SettledTask lane inherits the rule; `C459_IgnoredInventoryReachesPolicy` (`.antiphon/report.md`) stays refused. |
| V-8 | `WorktreeResidueSweepTests.C665_PublicationRetryHeldWithinCooldown` (Execute=true, one confirmed-publication residue op: run 1 `retry_queued`, run 2 within 168 h `retry_cooldown` Held, run 3 after → queued), `C665_SettledCooldownIsConsulted` (existing cursor for a released, otherwise eligible task → Held `cooldown`, zero retire calls) | Red at S4-red because nothing reads `NotBefore`. `C459_CooldownSurvivesRestart` is kept as is. |
| R-1 | Existing `LandingRemovalPolicyControlTests`, `WorktreeGuardedCleanupTests`, `AgentTaskLandPublicationTests`, `AgentTaskLandCleanupSafetyTests`, `LandSourceIdentityTests`, `WorktreeRemovalAuthorityTests`, `WorktreeRemovalDefaultTests`, `WorktreeResidueSweepTests`, `DelegationWorktreeTests` (compile: 3-arg ctor) | No existing refusal weakens: `.antiphon/report.md` sentinels still refuse; each reading still refuses before its next command; recovery pins, remote proof and lease checks unchanged. |
| R-2 | `OutputDistillationDeliveryTests`, `OutputDistillationApplyRaceTests` (the classes that exercise `AgentReportStore`; confirm exact names at Code time) | The `AgentReportRoot` extraction changes no report-storage behaviour. |
| R-3 | `Antiphon.Tests` Unit lane after Round C | Settings, DTO and classifier changes keep ordinary unit contracts. |

A new test that cannot go red against the production line it guards is a stub. A build
failure, fixture error or zero-test run is not red. Use per-test schemas and the existing
`LandingSafetyHarness` / `SettledRemovalHarness`; never boot against the production runner.
Process-spawning classes keep `[ParallelLimiter<ProcessSpawnLimit>]`.

### Cost and execution rules

Estimated verification per round: A ≈ 19 min (CP-1..CP-5), B ≈ 15 min (CP-6..CP-8),
C ≈ 11 min (CP-9..CP-11): **45 minutes** total, including four cold isolated builds at the
measured server2 speed. Authoring 85 + 85 + 55 = 225 minutes. These are estimates, not
timeouts. Commit before each build; do not edit source while a row runs; report each row
as `CHECKPOINT CP-n commit=<sha> build=<ok|reused|failed> filter=<filter> executed=N
passed=N failed=N skipped=N trx=<path>` plus `reruns=k`. Red rows must show the named
assertion failures with nonzero counts. No full-assembly or namespace-wide run is
commissioned.

### Checkpoints

Pipes inside filters are escaped for Markdown; the real filter uses `|`.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1-red | `tests/Antiphon.Tests -> bin-c665-a/` | classifier-red | `/*/*/WorktreeIgnoredContentClassifierTests*/*` | V-1 red | all methods execute; Disposable/Evidence assertions fail against the all-Protected stub; no build/fixture errors | 20 | 5 |
| CP-2 | S1 | `tests/Antiphon.Tests -> bin-c665-a/` | classifier-green | `/*/*/(WorktreeIgnoredContentClassifierTests*)\|(WorktreeCleanupSettingsValidatorTests*)/*` | V-1, V-9 | all listed, 0 failed/skipped | 24 | 3 |
| CP-3 | S2-red | `tests/Antiphon.Tests -> bin-c665-b/` | gate-red | `/*/*/LandingRemovalPolicyControlTests/C665_*` | V-2 red | all 6 execute; disposable/evidence/detail assertions fail; `C665_NullGateProtectsEveryIgnoredPath` may already pass | 6 | 4 |
| CP-4 | S2 | `tests/Antiphon.Tests -> bin-c665-b/` | gate-green | `/*/*/(LandingRemovalPolicyControlTests*)\|(WorktreeGuardedCleanupTests*)/*` | V-2, V-3, R-1 | all listed, 0 failed/skipped; the four updated `C443_*IgnoredBoundary` rows and both `C665_*` cleanup rows present | 85 | 5 |
| CP-5 | S2 | `CP-4`, `--no-build` | land-green | `/*/*/AgentTaskLandPublicationTests/(C448_V04*)\|(C665_*)` | V-4 | 3 updated `C448_V04` rows + 2 `C665_*` methods, 0 failed | 5 | 2 |
| CP-6 | S3-red | `tests/Antiphon.Tests -> bin-c665-c/` | retention-red | `/*/*/(WorktreeEvidenceRetentionTests*)\|(AgentTaskLandEvidenceRetentionTests*)/*` | V-5, V-6 red | all execute; land rows refuse `evidence_retention_unavailable`, retention rows fail on missing copies | 10 | 5 |
| CP-7 | S3 | `tests/Antiphon.Tests -> bin-c665-c/` | retention-green | `/*/*/(WorktreeEvidenceRetentionTests*)\|(AgentTaskLandEvidenceRetentionTests*)\|(AgentTaskLandCleanupSafetyTests*)/*` | V-5, V-6, R-1 | all listed, 0 failed/skipped | 38 | 7 |
| CP-8 | S3 | `CP-7`, `--no-build` | report-store-regression | `/*/*/(OutputDistillationDeliveryTests*)\|(OutputDistillationApplyRaceTests*)/*` | R-2 | all listed, 0 failed; if a class name differs, amend this row with a reason | 1 | 3 |
| CP-9 | S4-red | `tests/Antiphon.Tests -> bin-c665-d/` | sweep-red | `/*/*/WorktreeResidueSweepTests/C665_*` | V-8 red | both cooldown methods execute and fail on the Held/`retry_cooldown` assertions; `C665_SettledRetirementRemovesDisposableOnly` passes already (Round A) | 3 | 4 |
| CP-10 | S4+S5 | `tests/Antiphon.Tests -> bin-c665-d/` | sweep-green | `/*/*/(WorktreeResidueSweepTests*)\|(WorktreeRemovalAuthorityTests*)\|(WorktreeRemovalDefaultTests*)\|(LandSourceIdentityTests*)/*` | V-7, V-8, R-1 | all listed, 0 failed/skipped | 40 | 5 |
| CP-11 | all | `CP-10`, `--no-build` | unit-final | `/*/*/*/*[Category=Unit]` | R-3 | ≥ 1 executed, 0 failed; roster contains the two new Unit classes | 1 | 2 |
