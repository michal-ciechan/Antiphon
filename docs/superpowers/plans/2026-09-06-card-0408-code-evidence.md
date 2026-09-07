# CARD-0408 Code evidence

Task `0998ad05`; checkout `C:\Antiphon\worktrees\card-task-0998ad05`;
branch `feat/card-task-0998ad05`; reviewed plan base `3efe2c69`.

S1-S7 are implemented. Full green qualification is **incomplete**: the actual
managed-file symlink test cannot create its fixture without Windows privilege,
and the repository category guard reports one unchanged baseline class. Neither
failure is counted as a pass. No live deployment or cleanup was performed.

## Lineage and scoped commits

The review task made no commit. Origin has no `feat/card-task-f67c413b` ref;
its local branch points to `fc9cdf5c`, whereas the review's worktree and full
report identify `3efe2c69`. Code uses that reviewed plan plus the brief's amendments.

- S1 `7821f65a`: private-by-default schema and CLI-generated EF migration,
  immutable scalar publication projection, literal NUL exact-path Git add and
  commit, containment/reparse guards and gates. No board-create opt-in was
  reachable in this intermediate commit.
- S2 `1fa299ae`: explicit notes API, private snapshots and atomic content edits;
  ordinary DTO presence-only fields; sanitized validation; settings CAS/status;
  locally configured repository visibility; failed status does not roll back saves.
- S3/S4 `a66dfc48`: revoke-before-publish reconciliation, exact-path unstaging
  for never-in-HEAD private additions even with AutoCommit=false, genuine HEAD
  cleanup pending, neutral removal-only commits, pinned ownership, canonical Git
  gates, setup ignore protection, read-only readiness and expanded acceptance tests.
- S5 `46989073`: file-only notes input and truthful CLI publication/cleanup output.
- S6 `5e0aa6fe`: separate notes/public fields, explicit inert current/history panels,
  board policy CAS and preview/refusal, project classification, ID-event invalidation.
- Symlink qualification fixture `9c10989f`: executable managed-file test, with an
  explicit environmental failure on this host rather than a skipped/passing substitute.
- S7 is the documentation/evidence commit containing this file: normative
  `docs/card-file-privacy.md`, operator links, bundle prohibition, mixed validation
  keys, backup/history/frozen/pending guidance, read-only bootstrap probe and controls.

Root `.gitignore` and generated `docs/cards/` are untouched; CARD-0409 owns the
Antiphon rollout choice. No shared AppHost restart, live board opt-in, production
runner launch, live tracker/agent dispatch or live cleanup occurred.

## Final verification

Backend final targeted runs: **269 passed, 1 baseline failure** in 30 class/filter
entries. A separate actual-file-symlink run adds **0 passed, 1 environmental
failure**. Earlier adjacent regressions passed **107**, with no failures. These
are separate runs, not one full-suite result: 376 observed passing backend cases,
1 baseline guard failure and 1 unavailable fixture. No full-solution suite or E2E
run is claimed. Existing nullable/analyzer build warnings remain.

`CardSpawnModelArgumentTests` and `AgentServiceIntegrationTests` below are the
single privacy-related method filters; other rows are class runs. The 18-row
permission matrix counts as one test, and parameterized cases use TRX case counts.

| Class/filter | Passed | Failed |
|---|---:|---:|
| CardFilePolicyApiTests | 28 | 0 |
| CardFilePrivacyBoundaryAcceptanceTests | 4 | 0 |
| CardFilePrivacyPersistenceTests | 2 | 0 |
| CardFileIgnoreAcceptanceTests | 11 | 0 |
| CardFilePrivacyOwnershipAcceptanceTests | 16 | 0 |
| CardFilePrivacyPathTests | 3 | 0 |
| CardFilePrivacyConcurrencyTests | 10 | 0 |
| CardFilePrivacySyncAcceptanceTests | 35 | 0 |
| CardFileGitFailureAcceptanceTests | 7 | 0 |
| CardFilePrivacyHostedServiceTests | 3 | 0 |
| CardFilePrivacyScriptTests | 18 | 0 |
| CardFileBootstrapCheckTests | 3 | 0 |
| CardServiceTrackerPushTests | 6 | 0 |
| CardSpawnModelArgumentTests | 1 | 0 |
| ExternalTrackerSyncIdentifierTests | 5 | 0 |
| ProjectSetupServiceTests | 7 | 0 |
| AgentServiceIntegrationTests | 1 | 0 |
| CardRankingOrderTests | 3 | 0 |
| TestLaneCategoryGuardTests | 0 | 1 |
| CardFilePrivacyGitTests | 16 | 0 |
| CardTaskFileServiceTests | 21 | 0 |
| CardFileIgnoreTests | 8 | 0 |
| CardFilePrivacyOwnershipTests | 5 | 0 |
| CardFilePrivacyRecoveryTests | 7 | 0 |
| CardPrivateNotesBoundaryTests | 2 | 0 |
| CardPrivateNotesApiTests | 19 | 0 |
| CardFilePrivacySyncTests | 22 | 0 |
| CardFilePolicyTests | 4 | 0 |
| CardFilePrivacyMigrationTests | 1 | 0 |
| CardFilePrivacyDocumentationTests | 1 | 0 |

The added `CardFilePrivacyGitTests.Managed_file_symlink_refuses_sync_and_repository_IO_before_mutation`
run compiled and ran one test: it failed at `File.CreateSymbolicLink` with
`IOException: A required privilege is not held by the client` (the independent
probe reports HRESULT `80070522`). Its refusal, unchanged external bytes/HEAD
and no-pin assertions were therefore **not reached**. Real directory junction
fixtures at root/cards/board pass; they do not qualify the file-symlink case.
Rerun on a Windows test runner that already permits file symlinks:

```powershell
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-c408/ -- --treenode-filter "/*/*/CardFilePrivacyGitTests/Managed_file_symlink_refuses_sync_and_repository_IO_before_mutation" --report-trx --report-trx-filename final-filesymlink.trx
```

The category guard's remaining failure is exactly
`tests/Antiphon.Tests/Application/HerdrLaunchContextResolverTests.cs::HerdrLaunchContextResolverTests`.
That class has method-level categories but no class category. Both guard and
flagged file are unchanged from `3efe2c69` (diff and base contents inspected).
This is evidence from unchanged deterministic inputs and the rerun, not a claim
that a complete pristine-base checkout was executed. The initial guard also found
our documentation class; its class category was corrected, and the rerun names
only Herdr. No unrelated class was changed or guard suppressed.

Earlier adjacent regression runs (before the final ownership fixes):

| Class/filter | Passed | Failed |
|---|---:|---:|
| CardTaskFileRendererTests | 11 | 0 |
| CardFileSyncEndpointTests | 2 | 0 |
| CardFileSyncDisabledEndpointTests | 1 | 0 |
| CardCorrectionApiTests | 12 | 0 |
| CardIdentifierResolutionTests | 18 | 0 |
| BoardProjectArchiveApiTests | 2 | 0 |
| ProjectDeletionTests | 16 | 0 |
| ProjectServiceTests | 2 | 0 |
| ProjectReadinessTests | 41 | 0 |
| CardDiagnoseScriptTests | 2 | 0 |

Final combined client run: **101 passed, 0 failed, 9 files**, using BoardPage,
CardModal, CardEditModal, CardHistory, CardFilePrivacy, ProjectConfig,
ProjectSetupModal, ProjectReadinessPanel and useSignalRInvalidation filters.
Production `npm run build` and changed-file ESLint both passed; the existing
chunk-size warning remains. Integrated history tests explicitly exercise text,
empty and unknown snapshots without fetching notes on public preview.

An earlier lint invocation supplied repo-relative paths from the client directory
and failed; the corrected final command passed. An earlier ProjectConfig run hit
jsdom's missing scrollIntoView; the established stub pattern was added before the
combined green run. Neither earlier failure is presented as a green run.

## Defects found and corrected during acceptance

The expanded tests reproduced cached board-create policy validation, disabled
board target overlap, prospective project-reassignment collisions, new-project
reparse validation, same-target opt-in ignore preservation and nested-repository
info/exclude path resolution defects. These now pass in the final API, ownership,
path and ignore runs. CardTaskFileService now requires its repository interface;
Application no longer constructs an Infrastructure fallback. Other exercised fixes
include check-ignore's literal-pathspec environment exception, subdirectory Git
diff relativity and deletion staging beneath ignored ancestors using exact add -u.

## Positive controls

`tests/card-file-privacy-controls.py` runs only in an isolated idle worktree. It
applies a named production mutation, records the failing test, restores original
bytes in finally, then reruns the same test. Build failures and empty runs do not
qualify. Do not run concurrent builds/edits against a checkout undergoing mutation.

**All 48 distinct controls qualified red then green**, covering PC-1 through PC-40,
PC-1b, PC-18a/b, PC-20a/b, PC-23b and PC-39a-e. PC-15, PC-22 and PC-25 were repeated
after the final backend corrections; all again qualified. PC-15's latest red run
failed both board and project post-gate policy checks (two failures); its restored
run passed all three cases. PC-22 red permitted undrained reassignment. PC-25 red
removed the expected installed ignore file. Their restored runs passed.

PC-31 was also repeated with the injected resource-element assertion first and
failed on a real `<img>` DOM element. PC-32 changes the actual notes query
completion to transfer its result into the bulk board cache. Every red oracle was
inspected; no compilation/empty-run failures are counted. The first 45-backend
batch exited 0, and 15 distinct production files matched the last mutation backups
before final fixes. The final repeats restored source and left no production diff
against the committed backend/client. No mutation is retained in a commit.

Counts below are failed cases under mutation and passed cases after restoration,
not additional distinct acceptance-test totals. A mutation may leave other cases
passing. All backend red exits were 2 and green exits 0; client red exits were 1
and green exits 0.

| Control | Mutated failures | Restored passes | Verdict |
|---|---:|---:|---|
| PC-1 | 1 | 1 | qualified |
| PC-1b | 1 | 1 | qualified |
| PC-2 | 2 | 2 | qualified |
| PC-3 | 2 | 2 | qualified |
| PC-4 | 1 | 1 | qualified |
| PC-5 | 1 | 1 | qualified |
| PC-6 | 1 | 1 | qualified |
| PC-7 | 1 | 1 | qualified |
| PC-8 | 1 | 1 | qualified |
| PC-9 | 1 | 1 | qualified |
| PC-10 | 1 | 1 | qualified |
| PC-11 | 1 | 1 | qualified |
| PC-12 | 10 | 10 | qualified |
| PC-13 | 1 | 1 | qualified |
| PC-14 | 1 | 1 | qualified |
| PC-16 | 1 | 1 | qualified |
| PC-18a | 1 | 1 | qualified |
| PC-18b | 1 | 1 | qualified |
| PC-19 | 1 | 1 | qualified |
| PC-20a | 2 | 2 | qualified |
| PC-20b | 1 | 1 | qualified |
| PC-22 | 1 | 1 | qualified |
| PC-23 | 1 | 1 | qualified |
| PC-23b | 1 | 1 | qualified |
| PC-24 | 1 | 2 | qualified |
| PC-27 | 1 | 1 | qualified |
| PC-29 | 2 | 3 | qualified |
| PC-30 | 1 | 1 | qualified |
| PC-31 | 1 | 1 | qualified |
| PC-32 | 1 | 1 | qualified |
| PC-33 | 1 | 1 | qualified |
| PC-34 | 1 | 1 | qualified |
| PC-35 | 1 | 1 | qualified |
| PC-36 | 1 | 2 | qualified |
| PC-37 | 1 | 1 | qualified |
| PC-38 | 5 | 10 | qualified |
| PC-39a | 2 | 5 | qualified |
| PC-39b | 1 | 5 | qualified |
| PC-39c | 1 | 5 | qualified |
| PC-39d | 1 | 5 | qualified |
| PC-40 | 1 | 1 | qualified |
| PC-15 | 2 | 3 | qualified |
| PC-17 | 3 | 3 | qualified |
| PC-21 | 3 | 3 | qualified |
| PC-25 | 1 | 1 | qualified |
| PC-26 | 1 | 2 | qualified |
| PC-28 | 1 | 1 | qualified |
| PC-39e | 1 | 1 | qualified |


## Commands and local evidence

```powershell
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-c408/ -- --treenode-filter "/*/*/<Class>/<Method-or-*>" --report-trx --report-trx-filename <artifact>.trx
pwsh -NoProfile -File scripts/test-client.ps1 BoardPage CardModal CardEditModal CardHistory CardFilePrivacy ProjectConfig ProjectSetupModal ProjectReadinessPanel useSignalRInvalidation
python tests/card-file-privacy-controls.py PC-15 PC-22 PC-25
```

Backend runs used isolated build outputs and disposable PostgreSQL, loopback HTTP
and synthetic Git fixtures. The bootstrap test executes the new read-only privacy
probe; it does not operate the shared stack. Mutation reruns require the same test
prerequisites as acceptance and an idle isolated checkout.

Durable tracked evidence is this document, the plan, test sources and mutation
manifest. Raw logs remain local in ignored `.antiphon/c408-controls/`,
`.antiphon/c408-logs/`, and `.antiphon/c408-*.log`; TRX files are under
`tests/Antiphon.Tests/bin-c408/TestResults/`. Final summaries are
`.antiphon/c408-final-counts.json`, `.antiphon/c408-adjacent-counts.json` and
`.antiphon/c408-control-summary.json`. The extra fixture has
`final-filesymlink.trx`; it does not overwrite the 16-pass Git-class report.
Client final logs are `c408-client-final-combined.log`,
`c408-client-complete-build.log` and `c408-client-complete-lint.log` under `.antiphon/`.

Review should first rerun the managed-file symlink test on an eligible host and
assess the baseline category failure separately. Full green qualification must
not be inferred from this implementation artifact.

## Verification coverage ledger

Counts are recorded from completed runs in the Code evidence document. This ledger
maps the plan's groups to executable tests; it does not turn an unavailable fixture
or an unasserted subcase into a pass.

| Group | Executable evidence |
|---|---|
| V-1 | CardFilePolicyTests defaults; CardFilePrivacyMigrationTests predecessor-to-current upgrade in a disposable database. |
| V-2 | CardFilePolicyTests 18-row matrix; CardFilePrivacySyncTests manual board-off; CardFilePrivacySyncAcceptanceTests dry/sweep and policy transitions. |
| V-3 | Manual and dry/sweep Unknown cases with remote and integration enabled; zero output and unchanged HEAD/index. |
| V-4 | CardTaskFileRendererTests immutable projection; CardFilePrivacyBoundaryAcceptanceTests hostile markers, rejected private SQL selection and actual HEAD blobs. |
| V-5 | Full private-card metadata/archive/index test, actual note-only CardService edits, Public/Private/Inherit transitions, existing renderer/order/archive tests. |
| V-6 | CardPrivateNotesApiTests atomic correction/history; failed-save rollback in CardFilePrivacyBoundaryAcceptanceTests. |
| V-7 | Card/private/project enum raw-token tests, UTF-16 supplementary-character limits, omitted/null/empty/whitespace semantics and /limits. |
| V-8 | API stale token plus two cached contexts waiting on one gate: one winner, revision and ID event. |
| V-9 | Dedicated GET by GUID/identifier/board/cwd/task scope, collision/missing/cross-card history, every non-content kind, empty/null history, no-store and malformed revision queries. |
| V-10 | Ordinary HTTP DTOs/events/logs; fake tracker close payload; fake card launch argv/env/prompts; actual diagnostic and delegation-brief composers with synthetic notes. |
| V-11 | Entity/API defaults, ProjectSetup, tracker import, automated AgentService board/project creation, disposable PostgreSQL copy, fresh migrated seeder with synthetic markdown. |
| V-12 | HTTP missing/wrong fields; gated CAS, idempotence, ID-only events and no publication. |
| V-13 | Settings and board-create refusal for Unknown/pathless/missing/non-Git/unprotected targets; configured Public opt-in and unavailable-target disable. |
| V-14 | Valid/invalid project enum APIs, omission retention, URL/path reset, equivalent path handling and guarded reassignment. |
| V-15 | Closed 16-field status contract, explicit nullable paths, ordinary DTO absence, successful save despite failed policy probe. |
| V-16 | Existing sync endpoint/disabled endpoint tests; service policy, busy, unsafe/overlap, operational error and dry-run checks. |
| V-17 | Actual prior feature HEAD, manual/sweep revocation and last/all-private cases; legacy unpinned cleanup, neutral removal subject, old history retained, unrelated files preserved. |
| V-18 | Disabled direct/sweep freeze, read-only pending status, re-enabled cleanup and disabled/manual-only hosted driver. |
| V-19 | Post-acquisition board/card/project policy loads; in-flight commit against card/board/project/create/correction writes; cancellation and one-winner note edits. |
| V-20 | Deterministic faults before/after deletion, pin, first write and staging; fresh services recover current policy and block new siblings until cleanup. |
| V-21 | Real Git exact add/commit path set, nested/sibling/untracked/staged contaminants; PC-18a and PC-18b independently break each operation. |
| V-22 | Old staged body replaced at the same generated path; real post-index hook alters bytes after staging; missing desired/reappearing deletion refusal and retry. |
| V-23 | Never-in-HEAD index-only unstage including AutoCommit=false, absent working directory, HEAD-tracked deletion and named pending status. |
| V-24 | Manual/dry/sweep policy and effects, isolated sweep continues past failure, bounded real hosted tick, disabled/interval-zero no-scope, per-board/target warning transitions. |
| V-25 | Dry-run snapshots of owned DB rows, files, ignore, index and HEAD for fresh/off/Unknown/archive/ignored/staged cases; warning state remains unconsumed. |
| V-26 | Stable/colliding slugs, title rename, common-root/subdirectory projects, overlap/reassignment/delete guards, containment and real root/cards/board junctions. Named managed-file symlink test ran and failed at fixture creation (Windows HRESULT 80070522); its assertions remain unqualified. |
| V-27 | 700 literal NUL paths exceeding 32,767 bytes, special paths, CRLF/autocrlf, separate Git operation guards, owned marker repair/retry, locks, timeout/cancellation and sanitized stderr. |
| V-28 | Project create/setup/path assignment; root/subdirectory ignore install, user bytes/BOM/newlines, effective global/info excludes, malformed/injected denied install preserving the project. |
| V-29 | Effective no-index ignore on tracked/untracked paths, owner exceptions, private/other-board suppression and cleanup despite ignores. |
| V-30 | Read-only status/dry/tick and actual bootstrap privacy probe against loopback; retained tracked/staged warnings and no blanket install over existing opt-in. |
| V-31 | Actual card.ps1 against loopback: exact policy/target/configured visibility, eligible/manual timing, one note-free JSON object and no implicit sync. |
| V-32 | Unknown/missing status, pending/unavailable cleanup, successful-save wording and stale-token failure without retry. |
| V-33 | File-only notes preserve UTF-8/shell literals; omitted/empty/clear/conflicting/invalid/over-limit args, fresh and supplied tokens, mixed validation-key casing. |
| V-34 | Both create UIs, atomic edit, failed-read preservation, explicit clear, limits/token/reason and separate public/notes fields. |
| V-35 | Policy switch CAS, 409/422 preservation/refetch, direct Public opt-in, preview/refusal counts, Unknown project defaults and explicit repository selection. |
| V-36 | Inert explicit note panels, resource-element absence, card/revision query keys, no bulk/persisted cache transfer, card switching, and integrated public/history snapshot separation for text/empty/null. |
| V-37 | ID-only server events and mounted client query invalidation/refetch for policy/projects/notes. |
| V-38 | Documentation contract test plus review: agent prohibition, backups/, root-ignore/generated-file exclusion, operator policy, pending/frozen/history and CARD-0409 rollout ownership. |

Regression mapping: R-1 -> V-1/2/11; R-2 -> V-3; R-3 -> V-4/5/10;
R-4 -> V-2/5/17; R-5 -> V-6/7/8/19; R-6 -> V-9/10/31/36/37;
R-7 -> V-17/18/24; R-8 -> V-20/23/26; R-9 -> V-21/27;
R-10 -> V-22/23; R-11 -> V-19/26; R-12 -> V-26;
R-13 -> V-24/25; R-14 -> V-28/29/30; R-15 -> V-12/15/16/31/32/35;
R-16 -> V-11/38; R-17 -> renderer/order and V-4/5/27.

Evidence boundaries: bootstrap tests execute the actual new privacy probe extracted
from the script, not the entire shared-stack/Docker diagnostic. Driver parity uses the exact synthetic working state restored between arms,
compares manual/dry counts and records actual driver writes/deletes plus final policy. Git scope tests inspect real
index/HEAD outcomes rather than exposing a separate recorded stdin path-list API.
The current client has no persistence adapter; cache, storage and query metadata
checks cover its present implementation. No live agent or tracker was launched.
