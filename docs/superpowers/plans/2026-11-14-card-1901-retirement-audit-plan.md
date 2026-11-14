# CARD-1901 retirement audit publication rules

Plan owner: task `2c3ecf9c`. Written 2026-11-14 against current master
`8c06c7e11f40cc224235ecbc68c2389913cc67ee`, also the assigned branch base.
The fetched `origin/master` equalled that commit during inspection. No production
script, test, volume, running service or remote publication namespace was changed
to produce this plan. Verification design is folded into this dispatch.

The recommended change lets temp retirement prove **exact Git objects** through
one origin salvage namespace. It does not equate patches, erase reflogs, or treat
local preservation as publication. The recommendation needs a recorded policy
decision before Code: until then the existing branch-head rule remains effective.

Read with [the rollout evidence](../../investigations/2026-11-13-server2-rollout-rc-2026-11-19.md),
[retirement owner](../../docker-stack.md#volume-recycling-and-disk-reclaim-card-1008),
[testing owner](../../testing-and-build.md), and
[CARD-1708 implementation](../../../server/Infrastructure/Git/WorktreeReflogSalvage.cs).
CARD-1901 is `339c2931-aef0-4464-885d-4a2483f84d6d`; CARD-1898 is
`c89deb89-ec09-4764-badf-339bb0348462`, both on the Antiphon board.

## Decisions

These are explicit proposed defaults, D-1 through D-8, for the caller to accept
as a set or return with a narrower policy. This is a complete plan under those
defaults, not authorization to change live retirement policy during Plan.

- **D-1 — Exact origin salvage, temp/v2 only.** In addition to origin branch
  heads, accept a commit under
  `refs/antiphon/salvage/<lowercase GUID in D format>/<40-lowercase-hex OID>`
  only when the final component equals its advertised object ID and that object
  is a commit. This matches `WorktreeReflogSalvage.Scope`/`RefPrefix`, gives the
  existing preservation convention a precise publication form, and preserves
  the original commit's metadata, tree and parents. A fresh, approved-origin
  advertisement, fetched object graph and bound v2 manifest are all required.
  A name alone grants nothing. Keep the existing requirement for at least one
  origin branch head; salvage-only origins may conservatively refuse.
- **D-2 — No patch-id authority.** Rebase/cherry-pick twins, identical trees,
  empty commits, merge commits and commits labelled generated/scratch do not
  prove the original commit recoverable. Patch IDs omit commit identity and
  history and are unsuitable for authorizing volume deletion. Reject both a
  patch-id exemption and a bounded patch-id search; there is no new diff scan.
- **D-3 — All local recovery locations remain obligations.** Local salvage refs
  in either namespace, both sides of every reflog record, detached linked HEADs,
  stale origin tracking refs, other-remotes' tracking refs and pseudorefs remain
  positive roots in the protected census. They pass only if the exact retained
  graph is contained in an accepted origin graph. Never use `--all`, `--remotes`
  or local salvage refs as publication exclusions.
- **D-4 — Tags keep their present conservative semantics.** Advertised tags
  prove their exact non-commit objects. An annotated tag's peeled commit still
  needs ancestry from an accepted commit root; a lightweight tag alone is not
  a commit comparison root. A local annotated tag is protected even if its
  commit is published. Trees may also be proven reachable from accepted commit
  roots; blobs still require exact advertisement. Do not turn all tags into
  ancestry roots in this card.
- **D-5 — One closed namespace, no automatic publisher.** Reject
  `refs/salvage/*`, other `refs/antiphon/*`, malformed canonical salvage names,
  non-commit salvage targets and mismatching name/OID pairs as proof authority.
  Retain exact origin archive branches as the compatible recovery path.
  CARD-1708 already writes canonical local refs; its writer, pruning policy,
  permissions and retention are unchanged. Publication remains a separately
  authorized push of the original object followed by fresh proof. Do not add
  an automatic push, remote prune, branch deletion or Merge-settlement reflog
  expiry. A Succeeded task is not evidence that its earlier history is safe to
  discard.
- **D-6 — Validate the v2 graph before using it.** Any failed v2 unbundle,
  namespace/type validation or advertisement/bundle equality check refuses
  `RecycleGitAuditUnknown`; it must not fall back to locally present heads.
  Main/v1 retains its existing head-only fetch and conservative present-head
  fallback. The extension belongs to temp's offline v2 proof, not every caller
  of the shared shell function. Keep freshness, origin registration, nonce,
  operation, inventory, exact coverage and per-effect renewal checks.
- **D-7 — Minimal coordinated implementation.** Change the current v2 writer,
  v2 manifest consumer and shared audit's v2 branches together. Editing only
  `c590-remote.sh` cannot implement this policy: writer validation and renewal
  currently reject the new roots. Keep proof header version 2 and receipt
  schemas unchanged: old consumers reject the extra ref namespace, and old
  proofs cannot authorize salvage-only work. Mixed versions may refuse; they
  must never pass by ignoring unsupported evidence.
- **D-8 — Witness, not a wider fixture exception.** Add CARD-1898's own-`.git`
  file and symlink cases. Keep `selector_fixture` unchanged: only a real `.git`
  directory at that exact checkout, with no remote of any name, under its
  independently audited parent's ignored selector scratch, is disposable.
  Passing parent worktree linkage is not a witness for the nested fixture's
  own gitdir shape.

Rejected alternatives also include counting arbitrary remote refs, trusting an
external bundle without the bound origin advertisement, trusting a local fetched
ref whose remote name has disappeared, and suppressing findings by deleting
reflogs. All can hide locally unique objects or weaken provenance. Retaining the
current heads-only rule is the safe fallback if D-1 is declined; in that case
commission only the preservation witnesses/documentation after revising this
plan, not a half-implemented namespace change.

## Ground truth

| Card assumption / question | Current code and observed behavior | Planned rule / consequence |
|---|---|---|
| Gate 9's first refusal was nested fixture noise | `selector_fixture` in `scripts/c590-remote.sh` now exempts the specific ignored no-remote selector fixtures; the rollout's retry reached the mirror and then refused 16 tips. | Keep that fix and parent audit. Add the two missing shape witnesses from CARD-1898. |
| Salvaging the 16 commits should have unblocked retirement | The rollout pushed two `refs/salvage/gate9-16/...` refs. The chain tip reached all 16 objects on origin, but the selected publication namespace excluded them. A subsequent `refs/heads/salvage/gate9-16-477ce4ed` push made the fresh preview Ready. | Existing refusal was correct under the selected policy, not evidence of 16 lost patches. Legacy salvage refs remain excluded; the explicit canonical namespace is the only proposed expansion. |
| `c1008-host-proof.ps1` owns the active producer | It owns the v1 implementation and dispatches `ProofVersion=2` to `scripts/c1609-host-proof.ps1`. `Invoke-C1609HostOriginProofAttempt`, `Read-C1609Advertisement` and `Assert-C1609RequiredObjects` all currently select heads/tags, with only heads used for commit ancestry. | Change the v2 implementation and renewal checks. Keep v1 producer unchanged. |
| The unbundle switch is the only reader to change | `c1008-content-audit.sh:c1609_apply_host_proof` rejects refs outside heads/tags; `consider_common` does too. `c1609_unbundle_host_proof` skips tags/HEAD, counts only heads and checks v2 bundle refs equal the manifest. | All three readers must agree with the producer; exact graph validation remains mandatory. |
| A local `refs/salvage/*` or `refs/antiphon/salvage/*` means published | `consider_refs`, `consider_packed`, `consider_tips` census local refs as work. They are not comparison roots. | Still false. Even a correctly named canonical local salvage ref must be published on the approved origin and freshly proved. |
| A commit reachable only from a reflog is disposable | `consider_logs` reads old and new IDs, including the oldest old tip. `consider_linked` examines each administrative directory, even missing/prunable checkouts. | Still protected. Exact ancestry on an accepted origin root can satisfy it without rewriting the reflog. |
| Patch-equivalent commits on origin suffice | `consider_common` uses `rev-list --count --stdin` over local positive roots minus exact origin comparison roots, not patch IDs. Existing `Gone_tracking_retains_original_commit_protection` is not a complete patch-id differential. | Keep exact identity. New tests explicitly prove unequal OIDs and equal stable patch IDs before expecting refusal. |
| Origin tags are full publication roots | Tags/peeled tags enter `advertised`, but only heads enter comparisons. Local tag objects must themselves be advertised; peeled commits are checked separately. | Keep this distinction, adding canonical salvage commits to the comparison set only for v2. Tag-only history can still refuse. |
| Other remote tracking refs prove publication | `for-each-ref` collects them as local tips; only `origin` supplies comparison authority. | Other remotes remain irrelevant to authority. A backup's copy alone cannot release the volume. |
| Detached linked HEADs escape the branch census | `consider_linked` calls `consider_tips` on every `worktrees/<id>` admin directory, and its files/logs are inspected too. | Exact detached history remains protected, independently of the main checkout's branch. |
| A broken v2 bundle always refuses | `fetch_origin` failure currently clears fetched roots and `consider_common` falls back to `present`. With all advertised heads already local, bundle failure need not eliminate comparisons. | D-6 closes this fallback for v2 before accepting a new proof namespace. Test with locally present main so the witness cannot pass merely because there were no comparisons. |
| The audit scans every loose object | The documented census protects named refs, recovery tips and supported Git administrative state; an object no protected location names is outside its GC-like contract. Ignored ordinary content also has existing exclusions. | Preserve this boundary. Do not claim an all-object recovery guarantee or add an fsck/dangling-object census. |

The rollout's prose label `PUBLISHED-EQUIVALENT` describes a manual patch
comparison, not the audit's publication verdict. Fourteen patch equivalents and
two index regenerations still had distinct commit objects. The old gate preserved
them; the final origin branch publication satisfied the exact-object rule.

## Implementation boundary and slices

Production changes are confined to three files:

1. `scripts/c1609-host-proof.ps1`: expand the **v2** advertisement request to
   explicit `refs/heads/*`, `refs/tags/*`, `refs/antiphon/salvage/*` patterns.
   Do not retain `--heads --tags` on that query, because those switches exclude
   the new namespace. Add the same explicit salvage fetch refspec, retaining
   `--prune` for retry scratch. Validate the exact GUID/OID shape and equality,
   and validate commit object type after fetch/unbundle, including prior-proof
   reuse. Use heads plus validated canonical commits in required-commit and
   required-tree reachability; replace the latter's heads-only `--branches`
   traversal with the explicit validated roots. Keep advertisement equality
   before/fetch/after, limits, five-attempt snapshot-only retry, and one shared
   network deadline. Do not use all refs as exclusions. Keep tag checks intact.
2. `scripts/c1008-content-audit.sh:c1609_apply_host_proof`: accept only that
   additional canonical ref shape, validate the suffix/OID relationship, and
   retain duplicate, byte/count/hash, origin, nonce, inventory and clock checks.
   A parse/type/coverage uncertainty remains Unknown. No new classifier exemption.
3. `scripts/c590-remote.sh`: in `c1609_unbundle_host_proof`, accept canonical
   salvage only with `C1609_CONTENT_V2=1`; validate its name and commit type and
   place its OID into the scratch comparison refs alongside heads. Keep the
   full bundle/manifest comparison; never union local salvage refs into it.
   In `consider_common`, parse the extra v2 rows without promoting them to
   present-head authority before bundle validation; failed v2 `fetch_origin`
   must refuse. The v1 advertisement, fetch, fallback and tag policy stay intact.
   Update nearby comments to distinguish commit comparison roots from exact
   advertised objects. Keep local-tip collection and the final `rev-list` rule.

Tests use a new `tests/Antiphon.Tests/Scripts/VolumeRetirementPublicationTests.cs`
and a narrowly shared
`tests/Antiphon.Tests/Scripts/VolumeRetirementPublicationFixture.cs`. Reuse
`VolumeRetirementFixture.SeedPublishedOuter`, `Preview`, `AuditText` and the
existing real-host-proof transport pattern. Prefer the new helper over changing
global fixture behavior. No new remote executor, actual Docker daemon or
production origin is required. Bash/audit methods carry native-Linux guards;
portable writer methods do not. Every process-spawning method uses the assembly's
`ProcessSpawnLimit`, not the git-only limiter (Bash and PowerShell are children).
Classify the new real-Git/proof class as `Integration` and `Slow`, with its exact
fully qualified name and adjacent CARD-1901 reason added to
`tests/Antiphon.Tests/slow-tests-allowlist.txt`; it is not a fast Unit fixture.
The app batch is runner deployment / Git operations. The existing
`Antiphon.Tests` project graph supplies the C1008 and script-harness callers;
no runtime project dependency changes. A dedicated test project would duplicate
this fixture graph and is not justified for this small policy change.
Add exactly V1-V9 as method-level Windows exclusions in
`tests/test-execution-policy.json`, owned by CARD-1901, and regenerate its policy
hash with `Get-NightlyPolicyHash` from `scripts/lib/nightly-policy.ps1`, following
the existing `Write-C1323Policy` serialization pattern. H1-H3 stay required on both platforms;
do not exclude the whole class. This is policy data, not a schema change.

| Slice | Code authoring budget | Files and deliverable | Tests/checkpoints |
|---|---:|---|---|
| S1 | 45–60 min | Add the two new test files, matching native-policy rows and deterministic graph/proof helpers. Freeze the new named test roster below. Commit and push the two acceptance witnesses before production fixes. Existing preservation cases may already pass; do not manufacture baseline red for them. | CP-1 and CP-2: real-writer acceptance and invalid-bundle refusal assertion failures, separate exact methods on one isolated build. |
| S2 | 45–60 min | Implement v2 producer, exact namespace/type checks, required-object renewal and scratch reuse in `c1609-host-proof.ps1`. Tests use real local bare origins and new proof destinations. Commit/push. | CP-3: three portable host methods green. Full-audit acceptance remains pending S3, not green by implication. |
| S3 | 45–60 min | Implement the two shell consumers and v2 mandatory bundle validation. Complete local-ref/reflog/tag/linked/fixture preservation tests. Commit/push. | CP-4 new full class; CP-5 existing host-proof class; CP-6 selected content regressions. |
| S4 | 30–45 min | Update `docs/docker-stack.md` publication paragraphs, canonical salvage recovery and activation wording. Regenerate `tests/derived-impact-index.json` using `scripts/regenerate-derived-impact-index.ps1`; no hand edits. Check final impact selection and complete ordinary guards. Commit/push before checks. | CP-7 through CP-10 and CP-13 on Linux; CP-11/CP-12 on Windows for the portable producer path. |

Test/build waiting is accounted separately from the authoring budgets. Intermediate
commits may truthfully contain intended-red tests; only the final coherent slice
is a land candidate. Code runs ordinary verification, then separate ordinary
Review. Post-land SourceLanding Mutation owns deliberate PC cycles. Do not perform
mutation work from the Code checkout or from this Plan task.

## Why this is safe

For a protected commit `c`, the rule remains: every commit reached from `c` must
be reachable from at least one allowed origin commit root. The additional root
is an exact, freshly fetched, origin-advertised commit with a closed name shape.
It is not evidence synthesized from a local ref or from a matching patch. The
same origin-only graph must prove the required objects again during publication
renewal before the existing destructive gates proceed.

The only intended new successful classification is an otherwise safe temp volume
whose retained original objects are fully reachable from the new canonical
origin refs. The v2 fallback tightening can add refusals, never permissions.
An extra unique commit in any protected local location still causes refusal.
An unavailable or malformed proof still refuses. No audit command mutates the
target repository. Publishing exact history to an origin archive branch remains
the existing compatible recovery for legacy salvage namespaces.

Unchanged: volume names and ownership, container absence, drain/hold/census,
trust registry, receipt schema, apply token, effect journal, refresh/expiry,
rollback, kernel readonly/offline helper mounts, credentials, temporary scratch
ownership, partial-clone completeness, tag metadata protection, seed exceptions,
ignored-content policy, pending-child leases, CARD-1708 retention and Merge
settlement. There are no database migrations, public DTO/route changes or SQL
statement budgets. No new session waits for input, so there is no release/parking
deadline to design.

## Performance budget

Apply [performance standards](../../testing-and-build.md#performance-standards)
and [test performance standards](../../test-performance-standards.md). This Plan
runs no builds or product tests. **New-method and checkpoint p95 are unmeasured
(n=0)**; the figures below are provisional CARD-1901 commissioning budgets, not
measured percentiles. Preserve that distinction in Code and Review.

Evidence anchors: the [CARD-1609 plan](2026-10-29-card-1609-content-derived-volume-retirement-plan.md)
records the 2026-10-30 Debug measurements and round-16 expansion: its old combined
content/receipt row was budgeted at 27 minutes including build and explicitly
had unknown current p95. Its host/wrapper estimate was 3 minutes. Those selections
are not this selection; they justify using exact content methods rather than
running that large unrelated combined row. The rollout's roughly 10–23-minute
previews are live-volume observations, not temporary-fixture p95. Do not copy
production's 10800-second audit cap into a test timeout.

| Component | Provisional expected cost | Commissioning limit / measurement |
|---|---|---|
| Cold `Antiphon.Tests` build | 4–8 min each; load dependent | 16 min, recorded separately from test execution; use host build-slot gate and record SHA, OS, load, wait and build duration. |
| One native test host start | 2–10 s | Record separately; existing host harness bounds remain. |
| Shared immutable Git graph template | 1–3 s once per test run/process | Build base graph once; clone/copy into case-owned repositories. Never reuse mutable origin refs between cases. |
| Per-case clone/reset and proof setup | 0.1–2 s local Git, 1–3 s PowerShell | Use one fixture for a method's labelled vectors when independent clones are unnecessary; fresh proof scratch is the subject of the freshness/reuse tests. |
| Host-writer method body | 5–30 s | New method timeout 60 s provisionally; capture Git child counts and durations. |
| Audit method body | 5–45 s | New method timeout 90 s provisionally; include helper startup and joined teardown. No polling sleep or retry to hide failure. |
| Fixture teardown | 0.1–3 s | Join every owned child; existing cleanup safety holds. Record separately, never classify timeout as success. |
| Containers, DB, migrations, web host | 0 | None started. Existing C1008 Docker shim invokes the real audit over owned files only. |
| S1/S2/S3/S4 Linux executions | approximately 1 / 2 / 7 / 6 min, excluding builds | Test-row commissioning caps 2x the row estimates; shared-build groups remain under 30 min expected. |
| Windows writer selection | approximately 3 min excluding build | Only portable real-Git/PowerShell writer tests, no Bash audit substitute. |

Ordinary verification across all slices/lane builds is provisionally **39–59
minutes**; Code authoring is 165–225 minutes. This aggregate exceeds 30 minutes:
the caller must commission/link a **CARD-1901 performance Debug task** with this
closed selection and the CARD-1609 timing evidence before Code executes the
planned aggregate. Do not repeat or widen it without that analysis. Splitting
the calls is operational convenience, not an exemption
from R1. The Debug task should measure cold build cost and fixture/process phases,
then reduce repeated setup/build cost without dropping assertions. No such new
measurement or Debug completion is claimed by this Plan.

Use the first required Code runs and subsequent ordinary Review samples to record
per-method p95 with sample size, commit SHA, platform, load and exact membership.
Do not rebuild repeatedly solely to obtain a percentile. Until comparable samples
exist, report p95 unknown; after they exist set timeouts about 2x measured p95.
Do not widen a timeout or retry a red without attribution. A required row exceeding
30 min gets a Debug finding with its receipt, not another broad run.

Record `FixtureSetup`, `Filesystem`, `ProcessWait`, `OurCode` and
`FixtureTeardown` using existing admitted operation labels such as `shape-git`,
`clone` and `git`; verify the actual label roster before using a literal. New
label vocabulary is not part of this card. Keep timing JSONL, TRX, stderr spans,
source/build receipts and checkpoint outputs under ignored
`.antiphon/evidence/card-1901/` and the checkpoint tool's owned directories.
Enable `ANTIPHON_TEST_PHASE_ROOT` plus run/group identifiers per run. Missing
phase attribution stays unknown, not zero. Production network/fetch budgets,
retry count and audit deadline are unchanged; the implementation adds one
explicit refspec and linear ref validation, not a per-commit network call or
quadratic patch comparison.

Mutation allowance: 19 method-scoped variants below, provisionally 95–190 minutes
including bound-pack rebuilds and restoration. SourceLanding commissioning must
include a performance Debug obligation for this over-30-minute battery and use
measured method/variant batches; this cost is separate from Code's ordinary runs.

## Verification design

### Independent real-Git oracle

Create only disposable local repositories in owned test roots: a bare approved
origin, a separate bare backup, a publisher and the audited work/common+linked
directories. No command contacts the real origin, SSH server, Docker host or
volume. The transport shim maps the approved fixture URL to the local bare
origin; it must not fabricate Git output or the publication verdict. Retain
actual bundle/list-heads output, exact-OID reachability observations and labels
for every internal vector. Never call an apply path against a real resource.

Build deterministic small DAGs with fixed identities/times. Include an origin
head `A`, unique local child `U`, patch-equivalent `U2` with a different parent
or commit metadata, a second unique sibling `X`, an annotated tag object and a
linked detached HEAD. Assert the graph facts before invoking the product:
`U != U2`, equal stable patch IDs where intended, no accepted origin root reaches
`U`, and publishing the exact `U` chain changes that ancestry. A useful 16-commit
rollout-shaped vector has fourteen patch-equivalent originals and two distinct
index-like changes. Its golden verdict remains refusal until the exact chain
tip is published, with all sixteen original OIDs accounted for.

Use explicit expected sets of accepted object IDs and expected verdicts; do not
copy production's ref predicate or `rev-list` construction into the oracle.
Normalize only temporary paths, timestamps and operation IDs for golden output.
Never normalize away an OID, ref namespace, object type, refusal code, Ready
marker, finding or removal record. For unaffected vectors, compare current
master's committed audit with the candidate over separate identical fixture
copies; explicitly list the two intentional deltas (canonical remote salvage
accepted, invalid-v2 present-head fallback refused). No long-lived checkout of
the base is needed: extract the three exact base script blobs into owned test
scratch. Baseline fixtures and output must retain their source SHA.

### New closed test roster

All names below belong to `VolumeRetirementPublicationTests`; each is one TUnit
method, with named internal vectors, not a claimed count per assertion. H1–H3 are
portable and also run on Windows. V1–V9 require native Linux.

| ID / method | Required cases and assertions |
|---|---|
| H1 `Host_origin_salvage_proves_exact_required_objects` | Real v2 writer: initially a required unique commit refuses with no proof header; publish its canonical ref on the bare origin and accept the original commit, its ancestor and reachable tree. Inspect manifest and bundle for the exact canonical OID/ref. A unique sibling/child remains refused. A v1 proof excludes canonical salvage and still follows head-only policy. This is S1's first intended red. |
| H2 `Host_salvage_namespace_and_types_are_closed` | Valid GUID/OID name; local-only canonical ref; legacy `refs/salvage/*`; lookalike prefix; invalid GUID; name/OID mismatch; tag/tree/blob target; canonical salvage with no origin branch head. Excluded legacy names do not invalidate otherwise ordinary head proofs, but cannot prove their unique commits. Malformed refs within the requested canonical prefix refuse, not silently disappear. |
| H3 `Host_salvage_snapshot_prunes_and_renews_exact_objects` | Publish canonical U, create proof, remove/move it on origin, and request fresh proof with U required; refuse. A bounded snapshot retry must prune disappeared salvage refs from its scratch. Same-advertisement prior-bundle reuse still validates types/required objects. Extra or missing bundle refs, wrong origin/nonce/inventory, duplicate refs and changed bundle/hash refuse. |
| V1 `Origin_salvage_graph_passes_the_full_offline_audit` | Inventory -> real v2 producer -> offline full preview over a retained original chain. The new canonical ref alone covers that chain; no branch accidentally reaches it. Before publication refuses, after exact publication Ready, extra local sibling refuses. Assert zero removals, no helper network lookup, unchanged target refs/reflogs/object presence. Run after both producer and consumer changes so an old writer refusal cannot masquerade as an audit witness. |
| V2 `Local_salvage_refs_are_protected_roots` | Unique commits named only by loose/packed local legacy/canonical salvage refs refuse; an unrelated canonical origin ref does not hide them. Exact origin archive-head publication is the passing control. |
| V3 `Reflog_patch_equivalence_never_proves_publication` | Explicit unequal OIDs/equal patch IDs, including the 16-commit chain; old-only oldest reflog ID and new-side-only ID each remain protected. Include an empty commit and merge-history vector. Exact chain publication passes without expiring a reflog. |
| V4 `Other_remote_tracking_refs_do_not_prove_publication` | Backup-only copy plus loose/packed tracking refs refuses; stale origin tracking for a deleted branch refuses; exact ancestry on a current origin head passes. A current branch name containing `salvage/` remains an ordinary head. |
| V5 `Detached_linked_heads_and_logs_require_exact_publication` | Main is published, linked worktree HEAD is detached at U; census retains U and linked reflog-only X. Cover a missing checkout with surviving admin state. Refuse until both exact histories are covered. All Git pointers remain inside the fixture mounts. |
| V6 `Tags_preserve_object_and_ancestry_requirements` | Lightweight tag-only U refuses; advertised annotated tag-only U refuses its commit ancestry; local annotation on published A refuses its tag object; exact advertised annotation plus accepted ancestry passes. Add tree/blob and tag-of-tag controls retaining current distinctions. |
| V7 `V2_invalid_bundle_cannot_use_present_head_fallback` | Main and all advertised heads are already present locally. Start with a real valid head-only proof, then use a bound manifest with recomputed file hash but an unusable bundle or mismatched bundle-ref set. Require Unknown/no Ready, proving unbundle validation was enforced rather than relying on missing comparison objects. Head-only valid proof remains green; v1 fallback remains unchanged. This is S1's second intended red, independent of salvage support in the old writer. |
| V8 `Selector_fixture_own_gitdir_file_stays_in_full_audit` | Independent no-remote fixture below the ignored selector path with its own `.git` gitdir file pointing to owned in-volume admin data; published enclosing parent. Confirm Git opens that fixture, and full preview retains it/refuses Unknown rather than labelling it disposable. Include ordinary directory positive control. |
| V9 `Selector_fixture_own_gitdir_symlink_stays_in_full_audit` | Same witness with its own `.git` symlink to an owned in-volume directory. Assert absence of `disposable-fixture` for this exact nested path, retained entry/finding, refusal and zero removals. Include dirty/unpublished parent controls. |

For V8/V9, an invalid pointer, confinement error or unrelated dirty parent is not
the desired primary witness. Validate pointer resolution first and show the
fixture reached the classification boundary. If another enumeration of the same
admin directory still refuses after the classifier mutation, additionally invoke
the **extracted production `selector_fixture` function** with the actual derived
Git paths and assert its false result; this isolates the classifier assertion
without replacing the full preview witness. The existing equality checks overlap
the file/symlink guards: deleting just a redundant `-d` or `! -L` may be equivalent.
Do not claim such a survivor proves a hole; use the complete shape-check removal
described below to test the behavior.

### Existing tests affected and impact census

Required owner is [CARD-1782 impacted classes](../../testing-and-build.md#impacted-classes-card-1782),
with the hand-authored table at `tests/impacted-classes.json`. Its `remote-scripts`
row matches both changed shell files and requires the five exact remote deploy
methods in CP-8. `test-lane-category` matches the new test files; timing spans
also select `TestTimingOperationLabelTests`; the new native-only annotations
select the two `ReleaseGatePlatformTests` methods in CP-13. The required policy
data edit also matches `nightly-live-lane`: CP-10 includes its `LiveLaneRunnerTests`
and `NightlyCoreTests` (five methods total; offline harnesses, not a live run).
There are **no**
migration, public contract or statement-budget changes to select those rows.

Run existing `VolumeRetirementHostProofTests` in full (12 methods at this base),
the five content methods in CP-6 (12 results at this base: eight selector
arguments and four singletons), four original Git-audit core methods in CP-7
(30 argument results at this base), and v1 host-proof/ASCII/documentation methods
in CP-9. This preserves the callers of changed remote functions and the adjacent
regressions without the unrelated whole Unit lane or whole retirement suite.

The read-only prospective selector was run against the three production files,
existing fixture/content/host-proof paths and the owner doc. It returned the two
hand kinds above plus 37 broad derived classes, including unrelated database,
image/deployment and GC families. As in the CARD-1887 execution plan, those broad
reverse-graph suggestions are recorded, not silently credited as executed.
The ordinary scope here is the explicit changed functionality, named callers,
hand-glob obligations and guards below. After adding the new files/timing/gates
and regenerating the index, Code must save the exact final
`scripts/impacted-classes.ps1 -Range <base>..<tip> -Json` result, distinguish hand
obligations from broad suggestions, and justify any new difference before running
an unlisted row. No automatic expansion to a whole project or namespace.

### Positive controls: post-land Mutation

Use the exact method `/*/*/VolumeRetirementPublicationTests/<method>` named in
the table. Each numbered variant is a separate compiling defect, intended
assertion failure, restoration and fresh green cycle, on a bound method pack.
Zero tests, setup errors, timeouts and build errors are not a red. No mutation
may modify the golden expectations or test inputs. Retain each PC's evidence.

| PC | Mutation / protected behavior | Exact method from roster |
|---|---|---|
| PC-1 | Omit the canonical salvage fetch refspec in the v2 writer. | H1 `Host_origin_salvage_proves_exact_required_objects` |
| PC-2 | Use heads alone for required-commit ancestry after fetching valid salvage. | H1 `Host_origin_salvage_proves_exact_required_objects` |
| PC-3 | Keep required-tree traversal at `--branches` instead of validated roots. | H1 `Host_origin_salvage_proves_exact_required_objects` |
| PC-4 | Accept a canonical name whose suffix differs from its OID. | H2 `Host_salvage_namespace_and_types_are_closed` |
| PC-5 | Accept/peel a non-commit canonical target instead of refusing. | H2 `Host_salvage_namespace_and_types_are_closed` |
| PC-6 | Reuse old proof authority after the origin removes the salvage root, bypassing fresh required-object validation. | H3 `Host_salvage_snapshot_prunes_and_renews_exact_objects` |
| PC-7 | Ignore missing/extra canonical bundle refs in manifest equality. | H3 `Host_salvage_snapshot_prunes_and_renews_exact_objects` |
| PC-8 | Restore the old heads-only unbundle switch (valid salvage proof cannot admit). | V1 `Origin_salvage_graph_passes_the_full_offline_audit` |
| PC-9 | Add local salvage refs to comparison exclusions. | V2 `Local_salvage_refs_are_protected_roots` |
| PC-10 | Exclude patch-id-equivalent originals from the positive commit roots. Use a real patch-id predicate in this mutant, not unconditional success. | V3 `Reflog_patch_equivalence_never_proves_publication` |
| PC-11 | Read only the new OID in each reflog row. | V3 `Reflog_patch_equivalence_never_proves_publication` |
| PC-12 | Add other-remotes/stale tracking OIDs to comparisons. | V4 `Other_remote_tracking_refs_do_not_prove_publication` |
| PC-13 | Skip linked-admin HEAD/tip collection. | V5 `Detached_linked_heads_and_logs_require_exact_publication` |
| PC-14 | Treat every advertised peeled tag commit as a comparison root. | V6 `Tags_preserve_object_and_ancestry_requirements` |
| PC-15 | Skip exact tag-object publication and check only its peeled commit. | V6 `Tags_preserve_object_and_ancestry_requirements` |
| PC-16 | Restore the v2 present-head fallback after failed graph validation. | V7 `V2_invalid_bundle_cannot_use_present_head_fallback` |
| PC-17 | Remove the complete own-gitdir shape guard from `selector_fixture`, file vector. | V8 `Selector_fixture_own_gitdir_file_stays_in_full_audit` |
| PC-18 | Same shape-guard defect, symlink vector, independently recorded. | V9 `Selector_fixture_own_gitdir_symlink_stays_in_full_audit` |
| PC-19 | Expand the v2 writer's fetch/accepted comparison namespace to legacy `refs/salvage/*`; a legacy-only original must still be refused by the host proof test. | H2 `Host_salvage_namespace_and_types_are_closed` |

| PC-20 | Skip renewal when the object digest is unchanged. | `/*/*/VolumeRetirementPublicationTests/Unchanged_objects_renew_at_each_ready_gate` | pending |
| PC-21/deleted | Allow a previously advertised canonical ref to disappear. | `/*/*/VolumeRetirementPublicationTests/Salvage_deleted_after_sealing_refuses_ready` | pending |
| PC-21/rolled-back | Allow a previously advertised branch OID to change. | `/*/*/VolumeRetirementPublicationTests/Head_rolled_back_after_sealing_refuses_ready` | pending |
| PC-21/replaced | Allow the same canonical name to advertise another OID. | `/*/*/VolumeRetirementPublicationTests/Salvage_replaced_after_sealing_refuses_ready` | pending |
| PC-22 | Accept a failed final live-origin read. | `/*/*/VolumeRetirementPublicationTests/Origin_unreachable_at_final_check_refuses_ready` | pending |
| PC-23 | Omit the fresh origin binding/approval check. | `/*/*/VolumeRetirementPublicationTests/Origin_rebound_at_final_check_refuses_ready` | pending |
| PC-24 | Make the offline scanner an exit-0 authority without live completion. | `/*/*/VolumeRetirementPublicationTests/Offline_scan_requires_live_completion` | pending |
| PC-25/deleted-after-staging | Skip the final live advertisement after staging. | `/*/*/VolumeRetirementPublicationTests/Salvage_withdrawn_after_staging_refuses_control` | pending |
| PC-25/rolled-back-after-staging | Skip the final live advertisement after staging. | `/*/*/VolumeRetirementPublicationTests/Head_rolled_back_after_staging_refuses_control` | pending |
| PC-25/unreachable-after-staging | Accept final network failure after successful graph renewal. | `/*/*/VolumeRetirementPublicationTests/Origin_unreachable_after_staging_refuses_control` | pending |
| PC-26 | Reuse the sealed bundle instead of fetching the fresh graph at a gate. | `/*/*/VolumeRetirementWrapperTests/Retire_temp_real_wrapper_renews_each_effect` | pending |
| PC-27 | Start the effect-window clock after the final binding refresh, allowing an old URL observation. | `/*/*/VolumeRetirementPublicationTests/Final_origin_binding_refresh_is_inside_effect_window` | pending |
| PC-28/missing | Admit a missing observation clock as current. | `/*/*/VolumeRetirementPublicationTests/Binding_clock_record_requires_current_kernel_and_call` | pending |
| PC-28/duplicate | Accept one record when multiple observation clocks are supplied. | `/*/*/VolumeRetirementPublicationTests/Binding_clock_record_requires_current_kernel_and_call` | pending |
| PC-28/boot | Omit kernel boot identity matching. | `/*/*/VolumeRetirementPublicationTests/Binding_clock_record_requires_current_kernel_and_call` | pending |
| PC-28/namespace | Omit time-namespace identity matching. | `/*/*/VolumeRetirementPublicationTests/Binding_clock_record_requires_current_kernel_and_call` | pending |
| PC-28/namespace-empty | Admit an empty clock-domain identity. | `/*/*/VolumeRetirementPublicationTests/Binding_clock_record_requires_current_kernel_and_call` | pending |
| PC-28/past | Admit an observation from before this inventory invocation. | `/*/*/VolumeRetirementPublicationTests/Binding_clock_record_requires_current_kernel_and_call` | pending |
| PC-28/future | Admit a future observation tick. | `/*/*/VolumeRetirementPublicationTests/Binding_clock_record_requires_current_kernel_and_call` | pending |
| PC-28/malformed | Admit a malformed observation tick. | `/*/*/VolumeRetirementPublicationTests/Binding_clock_record_requires_current_kernel_and_call` | pending |
| PC-29 | Publish Ready during preparation before successful final control. | `/*/*/VolumeRetirementPublicationTests/Salvage_withdrawn_after_staging_refuses_control` | pending |
| PC-30 | Cache origin URL before emitting the binding clock. | `/*/*/VolumeRetirementPublicationTests/Binding_clock_precedes_fresh_origin_url_observation` | pending |

### Checkpoint execution and lanes

Placement was read through `GET /api/runner-defaults` and
`GET /api/session-runners` on 2026-11-14: revision 2 selected an eligible Linux
default; an eligible native Windows peer was also available and the temp entry
was retired. These are observations, not a hard-coded fleet destination. Read
both routes at dispatch. Omit `-Runner`; use `-Platform Linux` only for the Bash
lane and `-Platform Windows` only for the portable producer's native witness.
The table's groups name the lane. The Windows portion is small and separately
commissioned; a Linux pass cannot claim it.

Use the checkpoint tool, one run per committed slice group, `--serial`. Bootstrap
its executable once through `scripts/build-slot.ps1` in native default output;
then run the built coordinator, which obtains its own build/test leases. Do not
hold a launcher build lease while its child coordinator waits for another lease.
This small tool bootstrap (provisional 10–60 s) is an explicitly allowed build,
recorded separately. Run, await, and keep waiting on exit
75; do not leave a detached run on settlement. For example:

```powershell
pwsh -NoProfile -File scripts/build-slot.ps1 -Label card1901-checkpoint-bootstrap -- dotnet build tools/Antiphon.Checkpoints/Antiphon.Checkpoints.csproj
dotnet tools/Antiphon.Checkpoints/bin/Debug/net9.0/Antiphon.Checkpoints.dll run --plan docs/superpowers/plans/2026-11-14-card-1901-retirement-audit-plan.md --rows CP-4,CP-5,CP-6 --serial
```

CP-1/CP-2 are the only baseline expected-red executions. CP-3 is run after S2;
CP-4/5/6 after S3; CP-7/8/9/10/13 after S4; CP-11/12 on Windows at the final
committed candidate. Build reuse is confined to one `After` group and platform.
CP-1..CP-13 are the closed ordinary list. Do not repeat earlier rows at final
solely because documentation/index changed; rerun only if source changes affect
them, retaining the actual tested SHA. Review checks provenance for every row.

The required non-test checks are `git diff --check`, `bash -n` on the two changed
shell files, PowerShell parse-only validation of the changed writer, final impact
selection/index regeneration, `scripts/check-evidence-diff.ps1 -BaseRef <base>
-HeadRef <tip>`, and `git fetch origin master` followed by
`git merge-tree --write-tree origin/master HEAD`. Regeneration is a build driver:
gate it with `build-slot.ps1`. Never merge the target into the task branch.
No optional full build, E2E, live preview or benchmark is hidden in this list.

Checkpoint import defaults (15-minute floor/3x estimate) are not performance
measurements. In ignored imported YAML set each row's `timeoutMinutes` to 2x
its table estimate, `timeouts.rowMinutes: 16`, and `build.slotWaitMinutes: 10`.
The scheduler derives build deadlines from dependent rows and that floor:
16 minutes for S1/S2/S4/Windows, 20 minutes for S3. The latter is an explicit
provisional build-budget exception imposed by the shared row/build calculation,
not measured build p95. Charged execution caps for separate S1/S2/S3/S4/Windows
runs are 30/30/44/40/30 minutes, with each hard wall adding the 10-minute admission
allowance. These satisfy the current selected-estimate total derivation. A slice's
total estimate includes its build; do not interpret `Min` as minutes. Slot
timeout exit 4 is not-run/blocked, never permission to run without a lease.


## Round 2: live-origin renewal (Code e594649c)

Review dc70c718 reproduced deleted-salvage and rolled-back-head fail-open states.
CP-101, CP-102 and CP-104 are committed assertion-red witnesses before repair;
their frozen receipts keep the original SHA and outcomes. CP-103's first runs
failed in setup and are not qualifying regression reds. Its corrected witness
transfers the replacement object, independently counts the unique live-origin
commit, asserts exit 0 from the real committed pre-fix reader/writer, and then
asserts refusal from the current coordinator. CP-105 was corrected to change the
URL after the scan; its first green only covered the existing initial binding.
The corrected witness was red before the repair. R2-1..R2-5 map to those rows;
R2-6 is repeated same-object gate execution, R2-7 is provisional offline scan
completion, and R2-8..R2-11 are four independent seeded 70-shape Git corpora, each split into two 35-shape methods. R2-12..R2-14 withdraw publication only after the full writer and staging succeeded, to discriminate the final control check.

The v2 scan remains offline and read-only. With a supplied proof its successful
exit is 6/Unknown, a provisional content observation. The production parent
accepts that status only as input to its live gate. Supplied proofs without the
coordinator cannot produce Ready. Every gate re-observes origin bindings and
approvals, invokes the real v2 writer with FreshGraph (fresh scratch fetch, bound
manifest and exact required-object coverage), and rejects any prior advertised
ref that disappeared or changed OID. A final host control read repeats the live
advertisement after staging; the five-second monotonic bound includes the complete
final helper observation phase, that request, custody recording and the Ready/effect boundary. Each destructive volume
effect repeats the full gate. A ref or URL moved after the final observation
cannot be detected atomically across hosts; this residual interval is at most
five seconds, and no later effect reuses it. No schema or v1 authority changes.

The repair necessarily adds the existing retirement coordinator and credential-
capable bridge to the production boundary: c1008-retirement.sh, c590-real.ps1
and the c1008-host-proof.ps1 v2 dispatch parameter. It preserves the approved
origin registry and offline Docker boundary. Tests drive the real shell gate,
writer and final check; the fixture maps only its private network/CLI boundaries.

R2-15 / CP-125 covers the complete final binding refresh in the existing
five-second effect window. Its real-gate witness delays the second real binding
check by six seconds after the URL observation, while both the writer and final
advertisement succeed. Before the clock-scope repair this reaches Ready; after
repair it must refuse Unknown. The first binding scan and graph-fetch budgets
are unchanged. No timeout or assertion is widened. PC-27 remains pending.
The witness's initial entry hook was overwritten by module loading and was not
a valid red. Its corrected owned module ran the delay, but the fixture's cold
PowerShell launch could itself expire the control window. Preview now keeps one
owned live-check host warm, matching the production controller; every request
still calls the real helper and reads origin, and the process is joined before
preview returns. With that fixture CP-125 was assertion-red at committed source
cead11e754c1586a83a1ec614bdc21e7ea6f7723: actual Ready/exit 0, expected Unknown/2.
The first repaired run kept CP-125 green but exposed CP-106's normal path taking
about 12 seconds in inventory invocation preparation; the actual read-only
helper ran for about 0.6 seconds. The helper now emits its monotonic start before
any repository/content discovery. The host admits exactly one canonical record
only from the same kernel boot and time namespace, within this call's start/end
bracket. This excludes setup that observes no repository state while preserving
the unchanged five-second bound over all actual observations. R2-16 / CP-126
drives the real producer and consumer with valid, absent, duplicate, foreign-boot,
foreign-namespace, empty namespace, past, future and malformed records. The explicit
global clock domain applies only when both time-namespace interfaces are absent;
a present but unreadable interface refuses. This runner witnesses the absent/global
branch. A native Linux host exposing time namespaces still owes the producer's
present-interface branch; no seam is credited as that native execution. Internal inventory
diagnostics gain this clock line; proof headers and receipt schemas do not change.

CP-24 preserves all 11 V-6/V-7 vectors across four methods. CP-36 (40 methods),
CP-39 (225 methods) and CP-40 (23 methods) are split by their complete declared
rosters, without dropping argument expansions. CP-49 retains all four complete
31-assertion jq modes in separate methods. CP-60's three methods have separate
rows. CP-76's public end-to-end entry and CP-98's real wrapper exercise were
extracted into their own rows, retaining their existing 450s and 360s child caps.
CP-98 was not established as a hang: its old aggregate combined portable calls
with that native exercise and exhausted a four-minute row. The split will record
actual outcomes instead of increasing that timeout. These preserve historical
reds/deadlines, do not loosen assertions, and add no proof repetitions after green.

For this single committed Round-2 Final group, the ignored imported manifest may
reuse one new isolated Antiphon.Tests build across S3/S4/S5/R2Final rows; their
historical After values remain in this table for attribution. Keep separate
project builds for the mirrored limiter rows. Run the new renewal rows first,
then H/V, the named still-unrun/adjacent selections and guards. Historical S1
assertion reds, redundant S2/S5 repair duplicates and base-attribution rows are
retained as history rather than repeated after green. CP-4.H1/H2/H3 and CP-4.V1-V9
are the current H/V selections. CP-81 is split into its five remaining exact methods; CP-98 and CP-98.native
cover the other two class methods. CP-97 is the duplicate retained-volume repair
and is credited by CP-81.retained. Every other final row retains its exact filter.

The C1364 method split extends its frozen native-witness key without expanding
the exclusion policy. CP-121 covers the adjacent witness model/matcher classes;
CP-122..124 run the shared witness in its other three compiled suites. They are
ordinary tests over synthetic inspection inputs, not SourceLanding PC cycles.
The final guard also exposed three existing host-load labels missing from the
recorder vocabulary. The exact C1620 method failed identically at task base
b559bfba2e9a420495122d27171352fe9b7e1ba4 (one executed assertion failure).
Add only those literal labels; keep unlisted-text omission assertions unchanged.
Its base-only native build and exact-method run are unlisted attribution work
required by git-common, with native default outputs and separate build slots.

All test rows are serial and at most nine minutes; H rows and new short gate
rows use two minutes, V rows and corpus rows seven minutes, the retained CP-98
portable row four minutes, and its native row seven minutes. These new-slice
samples are provisional (n=0), bounded by the accepted CARD-1901 budget and
CARD-1906 loaded-host observation. Cold build retains the tool's 15-minute
watchdog floor and is monitored against the eight-minute preparation budget.
Admission is separately bounded at ten minutes; exit 4 is not executed and is
re-requested leased under this brief. Record /proc/loadavg with timings.

CP-0 prepares the standalone derived-index driver in native output; regeneration
runs twice and its second output must be unchanged, in its own index commit.
Re-pin the nightly policy and live-lane digest in their own commit after these
native-policy inputs. CP-26 still needs root; CP-11.H1/H2/H3 and CP-12 require
native Windows. The new native wrapper witness is also excluded only on Windows.
No restart, migration, live preview, deployment or real volume operation applies.

### Checkpoints

The final hand-glob selection also matches `spawn-limit-rosters`: CP-90/91
run its mirrored ProcessSpawnLimitTests in Agents.Pty and SessionRunner.
Each project owns an isolated build, with a provisional 2 min test cap and
the tool's 15 min build watchdog floor. All activities remain serial, including
Antiphon.Tests and Agents.Pty. No FakeClaude or full Pty suite is selected.


S4 completion additions (Code 0a2ad036): CP-14/15/16 rerun the three red
publication methods, CP-17 reruns the two host methods using the repaired
transport seam. Assertions and timeouts are unchanged. CP-20 onward measure
the budget report's 28 outstanding adjacent classes, using this same S4 build
and serial rows capped at 9 min (provisional n=0). The four large classes are
split by method; earlier CP-6 and CP-9 methods are credited from their exact
rows rather than repeated. These are named shared retirement-audit callers and
contract readers, not an assembly or Unit-lane selection. A timeout or native
skip remains explicit incomplete evidence, never a passed measurement.
The current assigned base declares 13 host-proof methods, so CP-5/12 now name
13; the earlier S3 TRX executed all 13 (12 passed, one seam failure).


Code refinement (0a2ad036): CP-0 is declared preparation: gated native build
of `scripts/regenerate-derived-impact-index/Antiphon.DerivedImpactIndex.csproj`.
It ran at 9fd0cf5c78345b48ea1b7ddb96b536c60adff35f after the preparation commit.
Use the budget report caps in ignored imported YAML: H=2 min, V=7 min,
CP-6a/b/c=5/5/7 min, CP-10a/b/c/d=4/2/9/6 min, CP-5/12=3 min,
CP-7/8/9/13=4/1/9/3 min; admission 10 min separately. Cold preparation
is provisionally 8 min tests + 1 min driver. The tool build watchdog has a
15 min floor, an explicit limitation, not an 8 min enforced watchdog.
The 28 adjacent full-class census rows in the budget report require separate
serial measurement under 10 min each; never credit an incomplete row.

S5 verification repair (Code 0a2ad036): CP-92/94/95 repair fixture input or
expected evidence format, preserving all acceptance assertions. CP-96 completes
ordinary prior-proof header vectors and preserves the required graph while
omitting a bundle ref, isolating equality from object availability. All PCs
remain pending for Mutation. CP-97/98 rerun the two CP-81 child-deadline failures separately, after
CP-100 checks the same methods at the task base. CP-100 exhausted its combined
4 min budget after Retire_temp_uses_current_proof_protocol completed its body
in 226.5 s (diagnostic only, no TRX); Retained_volumes had just started.
Splitting keeps each method under the existing 4 min row bound. No child or row
timeout is widened. S5 caps are CP-92/93=9 min, CP-94/95=7 min, CP-96=2 min,
CP-97/98=4 min; admission remains 10 min separately. Expected execution including
cold preparation is provisionally under 30 min; actual timings and load decide.
CP-99/100 run in an owned detached base checkout at
ca8737e78170c9a441e216408da5fab636c34ca0, with that checkout HEAD as expected SHA,
not the candidate SHA. Caps are 9/4 min; expected build plus execution under
30 min. Preserve baseline outcomes and all S4 reds/timeouts. The S4 batch
exceeded 30 min and its capped class/method selections need a separately
commissioned performance Debug using the retained source, membership and load.
Native Windows CP-11/12 and root-capable CP-26 remain incomplete until executed.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | S1 | tests/Antiphon.Tests -> bin-c1901-s1/ | linux-host-red | `/*/*/VolumeRetirementPublicationTests/Host_origin_salvage_proves_exact_required_objects` | H1 baseline | 1 intended publication-acceptance assertion failure; no build/setup failure | 1 | 6 | true |
| CP-2 | S1 | CP-1 | linux-audit-red | `/*/*/VolumeRetirementPublicationTests/V2_invalid_bundle_cannot_use_present_head_fallback` | V7 baseline | 1 intended invalid-proof refusal assertion failure; no build/setup failure | 1 | 1 | true |
| CP-3.H1 | S2 | tests/Antiphon.Tests -> bin-c1901-s2/ | linux-host | `/*/*/VolumeRetirementPublicationTests/Host_origin_salvage_proves_exact_required_objects` | H1 | exactly 1 passed; 0 failed/skipped | 1 | 1 | true |
| CP-3.H2 | S2 | tests/Antiphon.Tests -> bin-c1901-s2/ | linux-host | `/*/*/VolumeRetirementPublicationTests/Host_salvage_namespace_and_types_are_closed` | H2 | exactly 1 passed; 0 failed/skipped | 1 | 1 | true |
| CP-3.H3 | S2 | tests/Antiphon.Tests -> bin-c1901-s2/ | linux-host | `/*/*/VolumeRetirementPublicationTests/Host_salvage_snapshot_prunes_and_renews_exact_objects` | H3 | exactly 1 passed; 0 failed/skipped | 1 | 1 | true |
| CP-4.H1 | S3 | tests/Antiphon.Tests -> bin-c1901-s3/ | linux-publication | `/*/*/VolumeRetirementPublicationTests/Host_origin_salvage_proves_exact_required_objects` | H1 | exactly 1 passed; 0 failed/skipped | 1 | 1 | true |
| CP-4.H2 | S3 | tests/Antiphon.Tests -> bin-c1901-s3/ | linux-publication | `/*/*/VolumeRetirementPublicationTests/Host_salvage_namespace_and_types_are_closed` | H2 | exactly 1 passed; 0 failed/skipped | 1 | 1 | true |
| CP-4.H3 | S3 | tests/Antiphon.Tests -> bin-c1901-s3/ | linux-publication | `/*/*/VolumeRetirementPublicationTests/Host_salvage_snapshot_prunes_and_renews_exact_objects` | H3 | exactly 1 passed; 0 failed/skipped | 1 | 1 | true |
| CP-4.V1 | S3 | tests/Antiphon.Tests -> bin-c1901-s3/ | linux-publication | `/*/*/VolumeRetirementPublicationTests/Origin_salvage_graph_passes_the_full_offline_audit` | V1 | exactly 1 passed; 0 failed/skipped | 1 | 3 | true |
| CP-4.V2 | S3 | tests/Antiphon.Tests -> bin-c1901-s3/ | linux-publication | `/*/*/VolumeRetirementPublicationTests/Local_salvage_refs_are_protected_roots` | V2 | exactly 1 passed; 0 failed/skipped | 1 | 3 | true |
| CP-4.V3 | S3 | tests/Antiphon.Tests -> bin-c1901-s3/ | linux-publication | `/*/*/VolumeRetirementPublicationTests/Reflog_patch_equivalence_never_proves_publication` | V3 | exactly 1 passed; 0 failed/skipped | 1 | 3 | true |
| CP-4.V4 | S3 | tests/Antiphon.Tests -> bin-c1901-s3/ | linux-publication | `/*/*/VolumeRetirementPublicationTests/Other_remote_tracking_refs_do_not_prove_publication` | V4 | exactly 1 passed; 0 failed/skipped | 1 | 3 | true |
| CP-4.V5 | S3 | tests/Antiphon.Tests -> bin-c1901-s3/ | linux-publication | `/*/*/VolumeRetirementPublicationTests/Detached_linked_heads_and_logs_require_exact_publication` | V5 | exactly 1 passed; 0 failed/skipped | 1 | 3 | true |
| CP-4.V6 | S3 | tests/Antiphon.Tests -> bin-c1901-s3/ | linux-publication | `/*/*/VolumeRetirementPublicationTests/Tags_preserve_object_and_ancestry_requirements` | V6 | exactly 1 passed; 0 failed/skipped | 1 | 3 | true |
| CP-4.V7 | S3 | tests/Antiphon.Tests -> bin-c1901-s3/ | linux-publication | `/*/*/VolumeRetirementPublicationTests/V2_invalid_bundle_cannot_use_present_head_fallback` | V7 | exactly 1 passed; 0 failed/skipped | 1 | 3 | true |
| CP-4.V8 | S3 | tests/Antiphon.Tests -> bin-c1901-s3/ | linux-publication | `/*/*/VolumeRetirementPublicationTests/Selector_fixture_own_gitdir_file_stays_in_full_audit` | V8 | exactly 1 passed; 0 failed/skipped | 1 | 3 | true |
| CP-4.V9 | S3 | tests/Antiphon.Tests -> bin-c1901-s3/ | linux-publication | `/*/*/VolumeRetirementPublicationTests/Selector_fixture_own_gitdir_symlink_stays_in_full_audit` | V9 | exactly 1 passed; 0 failed/skipped | 1 | 3 | true |
| CP-5 | S3 | tests/Antiphon.Tests -> bin-c1901-s3/ | linux-host-regression | `/*/*/VolumeRetirementHostProofTests/*` | R-host | all 13 current-base methods; 0 failed/skipped | 13 | 3 | true |
| CP-6a | S3 | tests/Antiphon.Tests -> bin-c1901-s3/ | linux-content-regression | `/*/*/VolumeRetirementContentTests/(Selector_fixture_requires_ignored_scratch_no_remote_and_safe_parent*)\|(Selector_fixture_cannot_use_disposable_seed_parent_as_publication_proof*)` | R-content,CARD-1898 | exactly 9 passed; 0 failed/skipped | 9 | 2 | true |
| CP-6b | S3 | tests/Antiphon.Tests -> bin-c1901-s3/ | linux-content-regression | `/*/*/VolumeRetirementContentTests/(Every_ref_recovery_tip_and_tag_object_requires_publication*)\|(Gone_tracking_retains_original_commit_protection*)` | R-content,CARD-1898 | exactly 2 passed; 0 failed/skipped | 2 | 2 | true |
| CP-6c | S3 | tests/Antiphon.Tests -> bin-c1901-s3/ | linux-content-regression | `/*/*/VolumeRetirementContentTests/Private_origin_advanced_head_uses_real_host_proof` | R-content,CARD-1898 | exactly 1 passed; 0 failed/skipped | 1 | 3 | true |
| CP-7 | S4 | tests/Antiphon.Tests -> bin-c1901-s4/ | linux-git-regression | `/*/*/RemoteScriptContractTests/(C1105_Git_audit_recovery_tips*)\|(C1105_Git_audit_seed_content*)\|(C1105_Git_audit_index_content*)\|(C1105_Git_audit_object_completeness*)` | R-v1,R-completeness | all 4 methods / 30 results; 0 failed/skipped | 30 | 7 | true |
| CP-8 | S4 | CP-7 | linux-remote-impact | `/*/*/RemoteScriptContractTests/(C1105_Deploy_parent_orders_checkout_and_boot_files_before_recycle*)\|(C0995_Deploy_temp_checks_host_absence_before_checkout*)\|(C849_Deploy_temp_names_saved_import_when_seed_is_missing*)\|(C849_Deploy_ordering_contract_is_pinned*)\|(C849_Deploy_temp_observes_inaccessible_docker_mountpoints_with_sudo*)` | R-1782 remote-scripts | all 5 named methods; 0 failed/skipped | 5 | 1 | true |
| CP-9 | S4 | CP-7 | linux-v1-transport | `/*/*/RollingVolumeRecycleScriptTests/(C1008_Host_proof_admits_only_an_authenticated_private_origin*)\|(C1008_Host_proof_file_omits_credentials*)\|(C1008_Transport_scripts_are_ascii*)\|(C1008_Documentation_and_transport_pins_match*)` | R-v1-transport | exactly 4 passed; 0 failed/skipped | 4 | 2 | true |
| CP-10a | S4 | CP-7 | linux-final-guards | `/*/*/(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)/*` | R-policy,R-timing,R-index,R-nightly-policy | every named class; 0 failed | 4 | 2 | true |
| CP-10b | S4 | CP-7 | linux-final-guards | `/*/*/(NativeLinuxPolicyWitnessTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)/*` | R-policy,R-timing,R-index,R-nightly-policy | every named class; 0 failed | 4 | 1 | true |
| CP-10c | S4 | CP-7 | linux-final-guards | `/*/*/(TestLaneCategoryGuardTests*)\|(ProcessSpawnLimitTests*)\|(TestTimingOperationLabelTests*)\|(DerivedImpactIndexTests*)/*` | R-policy,R-timing,R-index,R-nightly-policy | every named class; 0 failed | 4 | 4 | true |
| CP-10d | S4 | CP-7 | linux-final-guards | `/*/*/(ReleaseImpactEnvelopeTests*)\|(LiveLaneRunnerTests*)\|(NightlyCoreTests*)/*` | R-policy,R-timing,R-index,R-nightly-policy | every named class; 0 failed | 3 | 3 | true |
| CP-11.H1 | S4-Windows | tests/Antiphon.Tests -> bin-c1901-win/ | windows-host | `/*/*/VolumeRetirementPublicationTests/Host_origin_salvage_proves_exact_required_objects` | H1 | exactly 1 passed; 0 failed/skipped | 1 | 1 | true |
| CP-11.H2 | S4-Windows | tests/Antiphon.Tests -> bin-c1901-win/ | windows-host | `/*/*/VolumeRetirementPublicationTests/Host_salvage_namespace_and_types_are_closed` | H2 | exactly 1 passed; 0 failed/skipped | 1 | 1 | true |
| CP-11.H3 | S4-Windows | tests/Antiphon.Tests -> bin-c1901-win/ | windows-host | `/*/*/VolumeRetirementPublicationTests/Host_salvage_snapshot_prunes_and_renews_exact_objects` | H3 | exactly 1 passed; 0 failed/skipped | 1 | 1 | true |
| CP-12 | S4-Windows | tests/Antiphon.Tests -> bin-c1901-win/ | windows-host-regression | `/*/*/VolumeRetirementHostProofTests/*` | R-host native Windows | all 13 current-base methods; 0 failed/skipped | 13 | 3 | true |
| CP-13 | S4 | CP-7 | linux-native-policy | `/*/*/ReleaseGatePlatformTests/(LinuxOnlyWindowsExclusionPolicy*)\|(C1686_LinuxOnlyCensus*)` | R-platform | exactly 2 passed; 0 failed/skipped | 2 | 1 | true |
| CP-14 | S4 | CP-7 | linux-red-repair | `/*/*/VolumeRetirementPublicationTests/V2_invalid_bundle_cannot_use_present_head_fallback` | V7 setup-red rerun | exactly 1 passed; 0 failed/skipped | 1 | 3 | true |
| CP-15 | S4 | CP-7 | linux-red-repair | `/*/*/VolumeRetirementPublicationTests/Selector_fixture_own_gitdir_file_stays_in_full_audit` | V8 clean own-gitdir repair | exactly 1 passed; 0 failed/skipped | 1 | 3 | true |
| CP-16 | S4 | CP-7 | linux-red-repair | `/*/*/VolumeRetirementPublicationTests/Selector_fixture_own_gitdir_symlink_stays_in_full_audit` | V9 clean own-gitdir repair | exactly 1 passed; 0 failed/skipped | 1 | 3 | true |
| CP-17 | S4 | CP-7 | linux-red-repair | `/*/*/VolumeRetirementHostProofTests/(Proof_coverage_binding_and_freshness_are_exact*)\|(Origins_and_git_configuration_cannot_redirect_credentials*)` | R-host transport seam repair | exactly 2 passed; 0 failed/skipped | 2 | 1 | true |
| CP-20 | S4 | CP-7 | linux-adjacent-measurement | `/*/*/C1364BoundLandWaitProofTests/C1364_Wrapper_redeploy_reports_waited` | M-adjacent C1364BoundLandWaitProofTests | every selected native method; report skips; 0 failed | 1 | 3 | true |
| CP-21 | S4 | CP-7 | linux-adjacent-measurement | `/*/*/C1364BoundLandWaitProofTests/C1364_Wrapper_cleanup_reports_waited` | M-adjacent C1364BoundLandWaitProofTests | every selected native method; report skips; 0 failed | 1 | 3 | true |
| CP-22 | S4 | CP-7 | linux-adjacent-measurement | `/*/*/C1364BoundLandWaitProofTests/C1364_Wrapper_retire_volumes_reports_waited` | M-adjacent C1364BoundLandWaitProofTests | every selected native method; report skips; 0 failed | 1 | 3 | true |
| CP-23 | S4 | CP-7 | linux-adjacent-measurement | `/*/*/C1364BoundLandWaitProofTests/C1364_Host_recycle_admission_reports_waited` | M-adjacent C1364BoundLandWaitProofTests | every selected native method; report skips; 0 failed | 1 | 3 | true |
| CP-24 | S4 | CP-7 | linux-r2-final | `/*/*/C1364BoundLandWaitProofTests/C1364_Host_recycle_post_removal_reports_waited` | R-1 V-6 post-audit late land and temp | every intended method; 0 failed/skipped | 1 | 3 | true |
| CP-24.resume | S4 | CP-7 | linux-r2-final | `/*/*/C1364BoundLandWaitProofTests/C1364_Host_recycle_post_removal_reports_waited_resume` | R-1 V-6/V-7 resume | every intended method; 0 failed/skipped | 1 | 3 | true |
| CP-24.main_volumes | S4 | CP-7 | linux-r2-final | `/*/*/C1364BoundLandWaitProofTests/C1364_Host_recycle_post_removal_reports_waited_main_volumes` | R-1 V-6/V-7 main_volumes | every intended method; 0 failed/skipped | 1 | 3 | true |
| CP-24.temp_volumes | S4 | CP-7 | linux-r2-final | `/*/*/C1364BoundLandWaitProofTests/C1364_Host_recycle_post_removal_reports_waited_temp_volumes` | R-1 V-6/V-7 temp_volumes | every intended method; 0 failed/skipped | 1 | 3 | true |
| CP-25 | S4 | CP-7 | linux-adjacent-measurement | `/*/*/C1364BoundLandWaitProofTests/C1364_Host_cleanup_reports_waited` | M-adjacent C1364BoundLandWaitProofTests | every selected native method; report skips; 0 failed | 1 | 3 | true |
| CP-26 | S4 | CP-7 | linux-adjacent-measurement | `/*/*/CacheSeedNpmOwnershipTests/*` | M-adjacent CacheSeedNpmOwnershipTests | every selected native method; report skips; 0 failed | 1 | 3 | true |
| CP-27 | S4 | CP-7 | linux-adjacent-measurement | `/*/*/ClaudeTokenRefreshOptInTests/*` | M-adjacent ClaudeTokenRefreshOptInTests | every selected native method; report skips; 0 failed | 1 | 3 | true |
| CP-28 | S4 | CP-7 | linux-adjacent-measurement | `/*/*/DockerDeliveryBarrierTests/*` | M-adjacent DockerDeliveryBarrierTests | every selected native method; report skips; 0 failed | 1 | 3 | true |
| CP-29 | S4 | CP-7 | linux-adjacent-measurement | `/*/*/DockerDeliveryDatabaseTests/*` | M-adjacent DockerDeliveryDatabaseTests | every selected native method; report skips; 0 failed | 1 | 3 | true |
| CP-30 | S4 | CP-7 | linux-adjacent-measurement | `/*/*/DockerDeliveryRecoveryCommandTests/*` | M-adjacent DockerDeliveryRecoveryCommandTests | every selected native method; report skips; 0 failed | 1 | 3 | true |
| CP-31 | S4 | CP-7 | linux-adjacent-measurement | `/*/*/DockerStackContractTests/*` | M-adjacent DockerStackContractTests | every selected native method; report skips; 0 failed | 1 | 3 | true |
| CP-32 | S4 | CP-7 | linux-adjacent-measurement | `/*/*/DockerStackDocumentationTests/*` | M-adjacent DockerStackDocumentationTests | every selected native method; report skips; 0 failed | 1 | 3 | true |
| CP-33 | S4 | CP-7 | linux-adjacent-measurement | `/*/*/DockerStackSmokeCommandTests/*` | M-adjacent DockerStackSmokeCommandTests | every selected native method; report skips; 0 failed | 1 | 3 | true |
| CP-34 | S4 | CP-7 | linux-adjacent-measurement | `/*/*/DockerTestCommandTests/*` | M-adjacent DockerTestCommandTests | every selected native method; report skips; 0 failed | 1 | 3 | true |
| CP-35 | S4 | CP-7 | linux-adjacent-measurement | `/*/*/HostJqFixtureClosureTests/*` | M-adjacent HostJqFixtureClosureTests | every selected native method; report skips; 0 failed | 1 | 3 | true |
| CP-36 | S4 | CP-7 | linux-r2-final | `/*/*/HostJqPrerequisiteScriptTests/(C1058_Open_descriptor_must_match_canonical_leaf*)\|(C1058_Digest_is_bound_to_open_descriptor*)` | M-adjacent HostJqPrerequisiteScriptTests slice 1 | every intended method; 0 failed/skipped | 2 | 3 | true |
| CP-36.02 | S4 | CP-7 | linux-r2-final | `/*/*/HostJqPrerequisiteScriptTests/(C1058_Installed_pin_is_checked_before_execution*)\|(C1058_Version_probe_uses_open_descriptor*)` | M-adjacent HostJqPrerequisiteScriptTests slice 2 | every intended method; 0 failed/skipped | 2 | 3 | true |
| CP-36.03 | S4 | CP-7 | linux-r2-final | `/*/*/HostJqPrerequisiteScriptTests/(C1058_True_probe_uses_open_descriptor*)\|(C1058_False_probe_uses_open_descriptor*)` | M-adjacent HostJqPrerequisiteScriptTests slice 3 | every intended method; 0 failed/skipped | 2 | 3 | true |
| CP-36.04 | S4 | CP-7 | linux-r2-final | `/*/*/HostJqPrerequisiteScriptTests/(C1058_Receipt_formatter_uses_open_descriptor*)\|(C1058_Changed_leaf_cannot_emit_success_proof*)` | M-adjacent HostJqPrerequisiteScriptTests slice 4 | every intended method; 0 failed/skipped | 2 | 3 | true |
| CP-36.05 | S4 | CP-7 | linux-r2-final | `/*/*/HostJqPrerequisiteScriptTests/(C1058_Pre_execution_link_checks_are_independent*)\|(C1058_Identity_comparisons_include_device*)` | M-adjacent HostJqPrerequisiteScriptTests slice 5 | every intended method; 0 failed/skipped | 2 | 3 | true |
| CP-36.06 | S4 | CP-7 | linux-r2-final | `/*/*/HostJqPrerequisiteScriptTests/(C1058_Final_link_checks_are_independent*)\|(C1058_Final_permission_checks_are_independent*)` | M-adjacent HostJqPrerequisiteScriptTests slice 6 | every intended method; 0 failed/skipped | 2 | 3 | true |
| CP-36.07 | S4 | CP-7 | linux-r2-final | `/*/*/HostJqPrerequisiteScriptTests/(C1058_Proof_is_buffered_until_final_admission*)\|(C1058_Metadata_syntax_refuses*)` | M-adjacent HostJqPrerequisiteScriptTests slice 7 | every intended method; 0 failed/skipped | 2 | 3 | true |
| CP-36.08 | S4 | CP-7 | linux-r2-final | `/*/*/HostJqPrerequisiteScriptTests/(C1058_Observation_failures_are_invalid*)\|(C1058_Check_rejects_canonical_hardlink*)` | M-adjacent HostJqPrerequisiteScriptTests slice 8 | every intended method; 0 failed/skipped | 2 | 3 | true |
| CP-36.09 | S4 | CP-7 | linux-r2-final | `/*/*/HostJqPrerequisiteScriptTests/(C1058_Provision_removes_only_owned_stage_link*)\|(C1058_Staged_unlink_failure_refuses*)` | M-adjacent HostJqPrerequisiteScriptTests slice 9 | every intended method; 0 failed/skipped | 2 | 3 | true |
| CP-36.10 | S4 | CP-7 | linux-r2-final | `/*/*/HostJqPrerequisiteScriptTests/(C1025_Check_qualifies_deployment_shell*)\|(C1025_Check_rejects_canonical_leaf_symlink*)` | M-adjacent HostJqPrerequisiteScriptTests slice 10 | every intended method; 0 failed/skipped | 2 | 3 | true |
| CP-36.11 | S4 | CP-7 | linux-r2-final | `/*/*/HostJqPrerequisiteScriptTests/(C1025_Receipt_rejects_canonical_lookup_with_unapproved_target*)\|(C1025_Check_has_no_install_effects*)` | M-adjacent HostJqPrerequisiteScriptTests slice 11 | every intended method; 0 failed/skipped | 2 | 3 | true |
| CP-36.12 | S4 | CP-7 | linux-r2-final | `/*/*/HostJqPrerequisiteScriptTests/(C1025_Provision_requires_missing_jq*)\|(C1025_Provision_admits_host_prerequisites*)` | M-adjacent HostJqPrerequisiteScriptTests slice 12 | every intended method; 0 failed/skipped | 2 | 3 | true |
| CP-36.13 | S4 | CP-7 | linux-r2-final | `/*/*/HostJqPrerequisiteScriptTests/(C1025_Provision_refuses_unsafe_destination*)\|(C1025_Provision_serializes_and_rechecks*)` | M-adjacent HostJqPrerequisiteScriptTests slice 13 | every intended method; 0 failed/skipped | 2 | 3 | true |
| CP-36.14 | S4 | CP-7 | linux-r2-final | `/*/*/HostJqPrerequisiteScriptTests/(C1025_Provision_verifies_download_before_use*)\|(C1025_Provision_publishes_complete_no_clobber*)` | M-adjacent HostJqPrerequisiteScriptTests slice 14 | every intended method; 0 failed/skipped | 2 | 3 | true |
| CP-36.15 | S4 | CP-7 | linux-r2-final | `/*/*/HostJqPrerequisiteScriptTests/(C1025_Provision_cleans_only_owned_staging*)\|(C1025_Provision_requalifies_published_jq*)` | M-adjacent HostJqPrerequisiteScriptTests slice 15 | every intended method; 0 failed/skipped | 2 | 3 | true |
| CP-36.16 | S4 | CP-7 | linux-r2-final | `/*/*/HostJqPrerequisiteScriptTests/(C1025_Provision_requires_canonical_source*)\|(C1025_Transport_is_bounded_and_reaped*)` | M-adjacent HostJqPrerequisiteScriptTests slice 16 | every intended method; 0 failed/skipped | 2 | 3 | true |
| CP-36.17 | S4 | CP-7 | linux-r2-final | `/*/*/HostJqPrerequisiteScriptTests/(C1025_Real_transport_emits_no_pipeline_output*)\|(C1025_Receipt_requires_complete_current_proof*)` | M-adjacent HostJqPrerequisiteScriptTests slice 17 | every intended method; 0 failed/skipped | 2 | 3 | true |
| CP-36.18 | S4 | CP-7 | linux-r2-final | `/*/*/HostJqPrerequisiteScriptTests/(C1025_Receipt_persistence_is_required*)\|(C1025_Transport_preserves_secret_custody*)` | M-adjacent HostJqPrerequisiteScriptTests slice 18 | every intended method; 0 failed/skipped | 2 | 3 | true |
| CP-36.19 | S4 | CP-7 | linux-r2-final | `/*/*/HostJqPrerequisiteScriptTests/(C1025_Explicit_tool_phases_never_deploy*)\|(C1025_Preflight_precedes_every_phase*)` | M-adjacent HostJqPrerequisiteScriptTests slice 19 | every intended method; 0 failed/skipped | 2 | 3 | true |
| CP-36.20 | S4 | CP-7 | linux-r2-final | `/*/*/HostJqPrerequisiteScriptTests/(C1025_Preflight_is_fresh_for_each_entry*)\|(C1025_Final_recycle_guard_remains*)` | M-adjacent HostJqPrerequisiteScriptTests slice 20 | every intended method; 0 failed/skipped | 2 | 3 | true |
| CP-37 | S4 | CP-7 | linux-adjacent-measurement | `/*/*/LinuxTestRosterTests/*` | M-adjacent LinuxTestRosterTests | every selected native method; report skips; 0 failed | 1 | 3 | true |
| CP-38 | S4 | CP-7 | linux-adjacent-measurement | `/*/*/PlanCoverageGoldenTests/*` | M-adjacent PlanCoverageGoldenTests | every selected native method; report skips; 0 failed | 1 | 3 | true |
| CP-39 | S4 | CP-7 | linux-r2-final | `/*/*/RemoteScriptContractTests/(C1374_Host_census_recovers_transient_reads*)\|(C1361_Host_census_tolerates_open_status_changes_between_list_and_detail*)\|(C1361_Host_census_tolerates_open_status_changes_between_passes*)\|(C1697_Host_standing_census_compares_only_rows_bound_to_server2*)\|(C1066_Fixture_tree_fault_reaches_validator_under_nounset*)\|(C1066_Fixture_npm_install_uses_argument_log_under_nounset*)\|(C1066_Fixture_warm_probe_uses_argument_log_under_nounset*)\|(C1189_Fixture_package_zip_does_not_call_host_pwsh*)` | M-adjacent RemoteScriptContractTests slice 1 | every intended method; 0 failed/skipped | 8 | 3 | true |
| CP-39.02 | S4 | CP-7 | linux-r2-final | `/*/*/RemoteScriptContractTests/(C1189_Fixture_npm_integrity_does_not_call_host_pwsh*)\|(C1189_Fixture_escape_link_is_removed_after_pc08_and_on_exit*)\|(C1191_Lock_probe_is_created_before_uid_handoff*)\|(C1191_Fixture_exit_removes_owned_workspace_without_host_sudo*)\|(C1192_Fixture_has_no_host_payload_tree_or_payload_bind*)\|(C1192_Finalization_precedes_success_and_preserves_primary_failure*)\|(C1192_Evidence_export_preserves_fixture_roster_without_payloads*)\|(C1192_Cleanup_consumer_requires_positive_absence*)` | M-adjacent RemoteScriptContractTests slice 2 | every intended method; 0 failed/skipped | 8 | 3 | true |
| CP-39.03 | S4 | CP-7 | linux-r2-final | `/*/*/RemoteScriptContractTests/(C1192_Cleanup_helper_requires_positive_absence*)\|(C1192_Cleanup_masked_requires_positive_absence*)\|(C1192_Cleanup_work_requires_positive_absence*)\|(C1192_Cleanup_mask_requires_positive_absence*)\|(C1192_Cleanup_cache_requires_positive_absence*)\|(C1192_Cleanup_network_requires_positive_absence*)\|(C1192_Daemon_loss_after_compose_refuses_success*)\|(C1192_Acquisition_unknown_ownership_retains_registration_and_residue*)` | M-adjacent RemoteScriptContractTests slice 3 | every intended method; 0 failed/skipped | 8 | 3 | true |
| CP-39.04 | S4 | CP-7 | linux-r2-final | `/*/*/RemoteScriptContractTests/(C1207_Compose_teardown_requires_recorded_positive_absence*)\|(C1208_Foreign_cache_is_dropped_and_untouched*)\|(C1208_Legacy_container_replacement_is_untouched*)\|(C1208_Legacy_network_replacement_is_untouched*)\|(C1208_Unacquired_roster_is_untouched*)\|(C1208_Acquired_objects_are_removed*)\|(C1208_Recorded_container_id_does_not_delete_replacement*)\|(C1208_Legacy_owned_markers_are_migrated*)` | M-adjacent RemoteScriptContractTests slice 4 | every intended method; 0 failed/skipped | 8 | 3 | true |
| CP-39.05 | S4 | CP-7 | linux-r2-final | `/*/*/RemoteScriptContractTests/(C1208_Unknown_identity_retains_residue*)\|(C1208_Wrong_cidfile_preserves_foreign_container*)\|(C1208_Recorded_id_with_changed_labels_is_untouched*)\|(C1208_Recorded_container_inspect_failure_refuses_cleanup*)\|(C1208_Wrong_helper_id_preserves_foreign_container*)\|(C1208_Wrong_network_id_is_untouched*)\|(C1208_Recorded_network_inspect_failure_refuses_cleanup*)\|(C1208_Owned_packages_registration_survives_production_controls*)` | M-adjacent RemoteScriptContractTests slice 5 | every intended method; 0 failed/skipped | 8 | 3 | true |
| CP-39.06 | S4 | CP-7 | linux-r2-final | `/*/*/RemoteScriptContractTests/(C1208_Dangling_network_record_retains_residue*)\|(C1208_Invalid_network_record_retains_text*)\|(C1208_Confirmed_absent_network_record_is_cleared*)\|(C1208_Recorded_creation_fingerprint_is_reverified*)\|(C1208_Fingerprint_acquisition_refuses_unowned_paths*)\|(C1208_Metadata_writers_refuse_file_type_changes*)\|(C1208_Update_publication_replaces_link_without_writing_victim*)\|(C1208_Create_publication_refuses_link_without_writing_victim*)` | M-adjacent RemoteScriptContractTests slice 6 | every intended method; 0 failed/skipped | 8 | 3 | true |
| CP-39.07 | S4 | CP-7 | linux-r2-final | `/*/*/RemoteScriptContractTests/(C1208_Model_inspection_reads_live_fields_without_external_reader_processes*)\|(C1208_Deadline_fixture_keeps_required_logs_and_omits_unrelated_setup*)\|(C1191_Fixture_failure_records_unhandled_exit_without_seed_receipt*)\|(C1192_Workspace_registration_is_absent_only_and_survives_prepare*)\|(C1192_Cleanup_releases_only_registered_current_run_resources*)\|(C1192_Partial_workspace_acquisition_is_cleaned*)\|(C1192_Toxic_probe_stays_in_work_volume*)\|(C1192_Summary_validator_runs_under_the_production_interpreter*)` | M-adjacent RemoteScriptContractTests slice 7 | every intended method; 0 failed/skipped | 8 | 3 | true |
| CP-39.08 | S4 | CP-7 | linux-r2-final | `/*/*/RemoteScriptContractTests/(C1192_Foreign_volume_is_refused_after_create*)\|(C1192_Harness_cleanup_stays_inside_its_private_directory*)\|(C1192_Harness_cleanup_refuses_ancestor_swap*)\|(C1192_Helper_argv_keeps_discarded_env_file_visible*)\|(C1192_Race_programs_and_feed_share_the_work_volume*)\|(C1192_Race_barriers_observe_volume_state*)\|(C849_Delayed_slot_grant_still_starts_both_restores*)\|(C849_Build_slot_exit_is_FixtureBuildSlotTimeout*)` | M-adjacent RemoteScriptContractTests slice 8 | every intended method; 0 failed/skipped | 8 | 3 | true |
| CP-39.09 | S4 | CP-7 | linux-r2-final | `/*/*/RemoteScriptContractTests/(C849_Refused_fixture_releases_probes_without_waiting*)\|(C849_Refused_launch_keeps_a_foreign_container_and_signals_release_proven_cids*)\|(C849_Signal_during_exit_finishes_custody_cleanup*)\|(C849_Group_signal_contract_requires_the_handler_to_exit*)\|(C849_Driver_deadline_witness_does_not_require_mount_namespaces*)\|(C849_Late_container_id_is_released_and_its_lease_deleted*)\|(C1192_Lock_probe_distinguishes_shared_and_private_scratch*)\|(C1192_Npm_seed_install_and_refill_use_work_volume*)` | M-adjacent RemoteScriptContractTests slice 9 | every intended method; 0 failed/skipped | 8 | 3 | true |
| CP-39.10 | S4 | CP-7 | linux-r2-final | `/*/*/RemoteScriptContractTests/(C1192_Npm_volume_sentinel_and_empty_cache_miss_are_observed*)\|(C1333_Npm_network_create_failure_reports_FixtureNpmNetworkFailed*)\|(C1192_Real_cache_cases_keep_the_host_payload_backend*)\|(C1192_Volume_transfer_preserves_metadata_and_refuses_partial_copy*)\|(C1192_Fixture_seed_and_rerun_keep_payloads_in_volume*)\|(C1192_Recovery_controls_validate_volume_bytes_modes_and_manifest*)\|(C1193_Lock_probe_passes_home_and_nuget_scratch*)\|(C1193_Fixture_exit_trap_copies_race_logs_before_tree_removal*)` | M-adjacent RemoteScriptContractTests slice 10 | every intended method; 0 failed/skipped | 8 | 3 | true |
| CP-39.11 | S4 | CP-7 | linux-r2-final | `/*/*/RemoteScriptContractTests/(C1193_Fixture_log_copy_skips_symlinks_and_copies_regular_files*)\|(C1194_Fixture_prune_scales_budget_outside_arithmetic_substitution*)\|(C1194_Fixture_budget_scale_refuses_empty_observation*)\|(C1192_Apphost_mask_is_an_empty_exact_volume_mount*)\|(C1195_Fixture_import_chmod_restores_volume_root*)\|(C1195_Seed_import_chmod_restores_volume_root*)\|(C1195_Retention_probe_is_app_uid_and_does_not_copy_image_content*)\|(C1197_Post_removal_compare_accepts_temp_churn_and_refuses_bound_work*)` | M-adjacent RemoteScriptContractTests slice 11 | every intended method; 0 failed/skipped | 8 | 3 | true |
| CP-39.12 | S4 | CP-7 | linux-r2-final | `/*/*/RemoteScriptContractTests/(C913_Seed_accepts_complete_ordinary_packages_without_framework_packs*)\|(C913_Full_marker_binds_verified_recovery_before_publication*)\|(C913_Marker_versions_keep_legacy_full_and_cold_contracts_distinct*)\|(C913_Prune_validates_recovery_without_refilling_image_packs*)\|(C913_Fixture_uses_owned_payloads_and_proven_absent_temp*)\|(C913_Receipts_reject_mixed_digest_types_and_false_smoke_claims*)\|(C1087_Host_census_filters_and_names_cause*)\|(C1008_Recycle_exact_default_volumes*)` | M-adjacent RemoteScriptContractTests slice 12 | every intended method; 0 failed/skipped | 8 | 3 | true |
| CP-39.13 | S4 | CP-7 | linux-r2-final | `/*/*/RemoteScriptContractTests/(C1008_Recycle_refuses_references_and_unknown_census*)\|(C1008_Recycle_audits_work_as_1654*)\|(C1008_Recycle_refuses_unpublished_and_dirty_work*)\|(C1008_Recycle_refuses_uninspectable_git*)\|(C1008_Recycle_reaps_dirty_scratch*)\|(C1008_Recycle_audits_promisor_checkout*)\|(C1105_Git_audit_recovery_tips*)\|(C1105_Git_audit_seed_content*)` | M-adjacent RemoteScriptContractTests slice 13 | every intended method; 0 failed/skipped | 8 | 3 | true |
| CP-39.14 | S4 | CP-7 | linux-r2-final | `/*/*/RemoteScriptContractTests/(C1105_Git_audit_index_content*)\|(C1105_Git_audit_object_completeness*)\|(C1105_Git_audit_overall_timeout*)\|(C1316_Recycle_audit_cap_clears_the_standing_volume*)\|(C1105_Git_audit_volume_scale*)\|(C1105_Git_audit_resume_preserves_proof*)\|(C1105_Resume_extraction_is_closed*)\|(C1105_Git_audit_hidden_locations*)` | M-adjacent RemoteScriptContractTests slice 14 | every intended method; 0 failed/skipped | 8 | 3 | true |
| CP-39.15 | S4 | CP-7 | linux-r2-final | `/*/*/RemoteScriptContractTests/(C1335_Mirror_owner_stamp_in_linked_admin*)\|(C1105_Jq_argv_forms_are_absent*)\|(C1105_Jq_file_forms_are_present*)\|(C1105_Jq_argv_limit*)\|(C1008_Recycle_preserves_tmp_copyup*)\|(C1105_Verify_tmp_accepts_an_absent_pty_host*)\|(C1008_Recycle_resume_requires_matching_receipt*)\|(C1008_Recycle_receipt_records_disk_and_partial_failure*)` | M-adjacent RemoteScriptContractTests slice 15 | every intended method; 0 failed/skipped | 8 | 3 | true |
| CP-39.16 | S4 | CP-7 | linux-r2-final | `/*/*/RemoteScriptContractTests/(C1008_Retire_temp_rechecks_absence_and_retirement*)\|(C1008_Retire_temp_reclaims_below_cache_disk_gate*)\|(C1008_Recycle_dry_run_never_mutates*)\|(C1338_Retire_temp_dry_run_lists_exactly_the_temp_volumes*)\|(C1105_Redeploy_accepts_previous_generation_and_requires_new_bind*)\|(C1105_Dry_run_previews_a_generation_change_without_moving_the_checkout*)\|(C1105_V218_dry_run_reaches_the_generation_line*)\|(C1105_Relative_dry_run_refuses_before_removal*)` | M-adjacent RemoteScriptContractTests slice 16 | every intended method; 0 failed/skipped | 8 | 3 | true |
| CP-39.17 | S4 | CP-7 | linux-r2-final | `/*/*/RemoteScriptContractTests/(C1105_Generation_identity_refuses_mismatch*)\|(C1105_Main_state_init_without_session_runner_refuses*)\|(C1105_Deploy_parent_orders_checkout_and_boot_files_before_recycle*)\|(C1105_Compose_cmp_refuses_before_removal*)\|(C1105_Resume_identity_refusals*)\|(C1105_Resume_state_init_image_follows_the_journal*)\|(C1105_Resume_foreign_leftover_is_not_an_image_mismatch*)\|(C1105_Resume_digest_pins_previous_sha12*)` | M-adjacent RemoteScriptContractTests slice 17 | every intended method; 0 failed/skipped | 8 | 3 | true |
| CP-39.18 | S4 | CP-7 | linux-r2-final | `/*/*/RemoteScriptContractTests/(C1105_Resume_post_removal_census_tracks_open_work*)\|(C944_All_cache_loop_variables_are_local*)\|(C951_Cache_paths_refuse_symlinked_mountpoints_and_refusing_sudo*)\|(C976_Missing_helper_image_preserves_final_receipt_diagnosis*)\|(C946_Rolling_harness_cleans_only_its_owned_success_root*)\|(C957_Scratch_mutation_controls_go_red_and_restore*)\|(C946_Green_harness_removes_root_and_failed_keep_run_retains_evidence*)\|(C980_Repo_root_assignment_preserves_linux_bytes*)` | M-adjacent RemoteScriptContractTests slice 18 | every intended method; 0 failed/skipped | 8 | 3 | true |
| CP-39.19 | S4 | CP-7 | linux-r2-final | `/*/*/RemoteScriptContractTests/(C980_Repo_root_assignment_converts_windows_paths*)\|(C980_Linux_shell_rejects_unconverted_repo_root*)\|(C980_Repo_root_conversion_failure_stops_before_body*)\|(C983_Required_jq_refuses_unavailable_probe_before_any_group*)\|(C983_Required_jq_driver_rejects_successful_false_probe*)\|(C905_Missing_linux_pwsh_skips_all_five_cases_before_a_script_block*)\|(C905_Linux_shell_finds_pwsh_when_installed*)\|(C849_front_door_passes_every_full_case_name_to_the_invoker*)` | M-adjacent RemoteScriptContractTests slice 19 | every intended method; 0 failed/skipped | 8 | 3 | true |
| CP-39.20 | S4 | CP-7 | linux-r2-final | `/*/*/RemoteScriptContractTests/(Nested_lane_never_uses_sudo_or_python*)\|(C1067_Nested_lane_sudo_guard_is_sensitive*)\|(Testing_payload_expects_linux_backend_never_windows*)\|(Custody_containment_case_measures_the_v1_path_as_the_app_uid*)\|(Custody_residue_counts_subdirectories_only*)\|(Custody_containment_is_routed_to_the_remote_and_not_left_pending*)\|(Linux_custody_case_requires_the_linux_backend*)\|(Scrub_covers_github_token_prefixes*)` | M-adjacent RemoteScriptContractTests slice 20 | every intended method; 0 failed/skipped | 8 | 3 | true |
| CP-39.21 | S4 | CP-7 | linux-r2-final | `/*/*/RemoteScriptContractTests/(Smoke_deletes_its_branch*)\|(Child_project_is_run_scoped_and_distinct*)\|(Host_compose_uses_the_deployed_tag_not_this_runs_sha*)\|(Retirement_is_anchored_on_the_run_scoped_prefix*)\|(Host_daemon_is_never_pruned*)\|(Lane_detection_separates_a_bare_host_from_the_runner*)\|(Every_case_declares_a_lane*)\|(Deploy_parent_probes_the_phone_home_secret_as_the_app_uid*)` | M-adjacent RemoteScriptContractTests slice 21 | every intended method; 0 failed/skipped | 8 | 3 | true |
| CP-39.22 | S4 | CP-7 | linux-r2-final | `/*/*/RemoteScriptContractTests/(Deploy_parent_phone_home_grep_matches_the_runners_registration_failure_template*)\|(Deploy_parent_refuses_a_runner_whose_phone_home_keeps_failing*)\|(Deploy_parent_retires_its_own_superseded_images*)\|(Temp_deploy_uses_its_own_project_and_the_inspected_grok_store*)\|(Temp_retire_requires_the_retired_receipt_and_downs_only_temp_volumes*)\|(Deploy_parent_creates_the_git_identity_file_without_overwriting*)\|(Deploy_parent_seeds_or_verifies_runner_checkout*)\|(Deploy_parent_seeds_a_fresh_runner_checkout_before_starting_the_runner*)` | M-adjacent RemoteScriptContractTests slice 22 | every intended method; 0 failed/skipped | 8 | 3 | true |
| CP-39.23 | S4 | CP-7 | linux-r2-final | `/*/*/RemoteScriptContractTests/(Persistent_restart_ensures_the_identity_file_before_stopping_an_older_runner*)\|(Deploy_parent_refuses_a_git_identity_symlink_and_leaves_its_target_mode*)\|(Deploy_parent_creates_the_codex_home_directory_without_reading_it*)\|(Deploy_parent_creates_the_github_token_directory_without_reading_it*)\|(Deploy_parent_codex_home_refuses_a_symlink_or_file_and_keeps_contents*)\|(Host_lane_ownership_reset_never_enters_the_codex_home*)\|(C849_Cache_cases_use_only_the_validated_host_lane*)\|(C849_Cache_prepare_is_idempotent_and_preserves_payloads*)` | M-adjacent RemoteScriptContractTests slice 23 | every intended method; 0 failed/skipped | 8 | 3 | true |
| CP-39.24 | S4 | CP-7 | linux-r2-final | `/*/*/RemoteScriptContractTests/(C849_Cache_prepare_refuses_foreign_or_unsafe_roots*)\|(C849_Prune_refuses_stale_or_busy_authority*)\|(C849_Closure_preflight_names_a_missing_dependency*)\|(C849_Seed_publishes_complete_payloads_before_its_marker*)\|(C849_Seed_refuses_invalid_donors_and_partial_payloads*)\|(C849_Saved_donor_archive_is_checked_and_imported_without_a_container*)\|(C849_Saved_donor_rejects_unsafe_archives_empty_payload_and_busy_counters*)\|(C849_Saved_donor_rejects_declared_size_bomb_before_writing*)` | M-adjacent RemoteScriptContractTests slice 24 | every intended method; 0 failed/skipped | 8 | 3 | true |
| CP-39.25 | S4 | CP-7 | linux-r2-final | `/*/*/RemoteScriptContractTests/(C849_Retired_guard_refuses_docker_ps_errors*)\|(C0995_Deploy_temp_checks_host_absence_before_checkout*)\|(C0995_Cold_ready_seed_refuses_a_temp_container*)\|(C849_Cold_marker_reuse_writes_gate_receipts*)\|(C849_Deploy_temp_names_saved_import_when_seed_is_missing*)\|(C849_Deploy_ordering_contract_is_pinned*)\|(C849_Deploy_prepares_and_verifies_before_acceptance*)\|(C849_Deploy_temp_observes_inaccessible_docker_mountpoints_with_sudo*)` | M-adjacent RemoteScriptContractTests slice 25 | every intended method; 0 failed/skipped | 8 | 3 | true |
| CP-39.26 | S4 | CP-7 | linux-r2-final | `/*/*/RemoteScriptContractTests/(C849_Docker_volume_paths_have_no_bare_host_reads*)\|(C849_Prune_preview_is_read_only_and_bounded*)\|(C997_Cache_budget_gate_uses_controlled_disk_readings*)\|(C997_Saved_donor_space_gate_preserves_headroom_and_payload_allowance*)\|(C997_Saved_donor_probe_failure_refuses_before_copy*)\|(C997_Saved_donor_default_probe_remains_real*)\|(C849_Prune_and_rollback_retain_roots_and_recovery*)\|(C849_Cache_receipts_exclude_credentials_and_payloads*)` | M-adjacent RemoteScriptContractTests slice 26 | every intended method; 0 failed/skipped | 8 | 3 | true |
| CP-39.27 | S4 | CP-7 | linux-r2-final | `/*/*/RemoteScriptContractTests/(C912_Cold_seed_with_unrelated_bind_initializes_only_three_labelled_roots*)\|(C912_Cold_seed_refuses_when_created_volume_disappears_before_init*)\|(C912_Cold_volumes_seed_accepts_busy_main_without_packages*)\|(C912_Cold_volumes_seed_refuses_unknown_mounted_or_populated_targets*)\|(C912_Cold_volumes_seed_rechecks_initialization_and_probe_boundaries*)\|(C912_Cold_cache_probe_requires_uid_writes_cleanup_and_bounded_exit*)\|(C912_Cold_marker_has_distinct_validation_and_full_context_refusal*)\|(C912_Cold_runner_verification_uses_mounts_and_writability_not_payloads*)` | M-adjacent RemoteScriptContractTests slice 27 | every intended method; 0 failed/skipped | 8 | 3 | true |
| CP-39.28 | S4 | CP-7 | linux-r2-final | `/*/*/RemoteScriptContractTests/(C912_Cold_exception_never_weakens_maintenance_or_donor_gates*)\|(C973_Cold_marker_readers_accept_pruned_seed_image_and_refuse_invalid_markers*)\|(C973_Cold_helper_readers_resolve_current_main_after_seed_image_prune*)\|(C973_Prune_and_saved_donor_still_require_full_after_seed_image_prune*)\|(C1168_State_init_tolerates_a_file_deleted_mid_walk*)\|(C1168_State_init_refuses_a_persistent_chown_error*)\|(C1168_State_init_owns_a_dangling_symlink*)\|(C1331_State_init_chowns_a_populated_tree_in_find_batches*)` | M-adjacent RemoteScriptContractTests slice 28 | every intended method; 0 failed/skipped | 8 | 3 | true |
| CP-39.29 | S4 | CP-7 | linux-r2-final | `/*/*/RemoteScriptContractTests/(C1168_Rollout_docs_keep_redeploy_while_temp_holds_the_shared_store*)` | M-adjacent RemoteScriptContractTests slice 29 | every intended method; 0 failed/skipped | 1 | 3 | true |
| CP-40 | S4 | CP-7 | linux-r2-final | `/*/*/RetiredTempContainerHostTests/(RequireNativeLinux*)\|(C994_Exited_owned_containers_are_removed_without_volumes*)` | M-adjacent RetiredTempContainerHostTests slice 1 | every intended method; 0 failed/skipped | 2 | 3 | true |
| CP-40.02 | S4 | CP-7 | linux-r2-final | `/*/*/RetiredTempContainerHostTests/(C994_Absent_cleanup_is_idempotent*)\|(C994_Older_retirement_receipt_does_not_block_cleanup*)` | M-adjacent RetiredTempContainerHostTests slice 2 | every intended method; 0 failed/skipped | 2 | 3 | true |
| CP-40.03 | S4 | CP-7 | linux-r2-final | `/*/*/RetiredTempContainerHostTests/(C994_Host_retirement_proof_is_typed*)\|(C994_Host_main_admission_is_required*)` | M-adjacent RetiredTempContainerHostTests slice 3 | every intended method; 0 failed/skipped | 2 | 3 | true |
| CP-40.04 | S4 | CP-7 | linux-r2-final | `/*/*/RetiredTempContainerHostTests/(C994_Host_task_and_land_census_is_complete*)\|(C994_Census_and_inspect_fail_closed*)` | M-adjacent RetiredTempContainerHostTests slice 4 | every intended method; 0 failed/skipped | 2 | 3 | true |
| CP-40.05 | S4 | CP-7 | linux-r2-final | `/*/*/RetiredTempContainerHostTests/(C994_Container_service_and_state_are_restricted*)\|(C994_Production_mount_topology_is_proven*)` | M-adjacent RetiredTempContainerHostTests slice 5 | every intended method; 0 failed/skipped | 2 | 3 | true |
| CP-40.06 | S4 | CP-7 | linux-r2-final | `/*/*/RetiredTempContainerHostTests/(C994_Whole_candidate_set_precedes_removal*)\|(C994_Rollout_lock_covers_final_census*)` | M-adjacent RetiredTempContainerHostTests slice 6 | every intended method; 0 failed/skipped | 2 | 3 | true |
| CP-40.07 | S4 | CP-7 | linux-r2-final | `/*/*/RetiredTempContainerHostTests/(C994_Each_removal_rechecks_live_proofs*)\|(C994_Final_absence_is_observed*)` | M-adjacent RetiredTempContainerHostTests slice 7 | every intended method; 0 failed/skipped | 2 | 3 | true |
| CP-40.08 | S4 | CP-7 | linux-r2-final | `/*/*/RetiredTempContainerHostTests/(C994_Removal_failure_and_crash_retry_preserve_progress*)\|(C994_Receipt_write_and_copy_fail_closed*)` | M-adjacent RetiredTempContainerHostTests slice 8 | every intended method; 0 failed/skipped | 2 | 3 | true |
| CP-40.09 | S4 | CP-7 | linux-r2-final | `/*/*/RetiredTempContainerHostTests/(C994_Host_context_is_strict*)\|(C994_Host_preview_never_mutates*)` | M-adjacent RetiredTempContainerHostTests slice 9 | every intended method; 0 failed/skipped | 2 | 3 | true |
| CP-40.10 | S4 | CP-7 | linux-r2-final | `/*/*/RetiredTempContainerHostTests/(C1609_Cleanup_does_not_require_historical_image*)\|(C1609_Audit_uses_current_image*)` | M-adjacent RetiredTempContainerHostTests slice 10 | every intended method; 0 failed/skipped | 2 | 3 | true |
| CP-40.11 | S4 | CP-7 | linux-r2-final | `/*/*/RetiredTempContainerHostTests/(C994_Cleanup_receipts_redact_unapproved_fields*)\|(C994_Fixture_custody_is_bounded*)` | M-adjacent RetiredTempContainerHostTests slice 11 | every intended method; 0 failed/skipped | 2 | 3 | true |
| CP-40.12 | S4 | CP-7 | linux-r2-final | `/*/*/RetiredTempContainerHostTests/(C1105_Temp_cleanup_uses_previous_generation*)` | M-adjacent RetiredTempContainerHostTests slice 12 | every intended method; 0 failed/skipped | 1 | 3 | true |
| CP-41 | S4 | CP-7 | linux-adjacent-measurement | `/*/*/RetiredTempContainerScriptTests/*` | M-adjacent RetiredTempContainerScriptTests | every selected native method; report skips; 0 failed | 1 | 3 | true |
| CP-42 | S4 | CP-7 | linux-adjacent-measurement | `/*/*/RetireTempRunnerTimestampTests/*` | M-adjacent RetireTempRunnerTimestampTests | every selected native method; report skips; 0 failed | 1 | 3 | true |
| CP-43 | S4 | CP-7 | linux-adjacent-measurement | `/*/*/RollingProductionMountTests/*` | M-adjacent RollingProductionMountTests | every selected native method; report skips; 0 failed | 1 | 3 | true |
| CP-44 | S4 | CP-7 | linux-adjacent-measurement | `/*/*/RollingVolumeRecycleCensusChurnTests/*` | M-adjacent RollingVolumeRecycleCensusChurnTests | every selected native method; report skips; 0 failed | 1 | 3 | true |
| CP-45 | S4 | CP-7 | linux-adjacent-measurement | `/*/*/RollingVolumeRecycleScriptTests/(C1374_Wrapper_census_recovers_transient_reads*)\|(C1087_Read_failures_name_their_cause*)\|(C1087_Project_resolves_by_repository_identity*)` | M-adjacent RollingVolumeRecycleScriptTests | every selected native method; report skips; 0 failed | 3 | 3 | true |
| CP-46 | S4 | CP-7 | linux-adjacent-measurement | `/*/*/RollingVolumeRecycleScriptTests/(C1693_Retire_temp_census_ignores_standing_runner_churn_between_passes*)\|(C1087_Census_filters_open_work_and_pending_lands*)\|(C1087_Check_census_phase_is_read_only*)` | M-adjacent RollingVolumeRecycleScriptTests | every selected native method; report skips; 0 failed | 3 | 3 | true |
| CP-47 | S4 | CP-7 | linux-adjacent-measurement | `/*/*/RollingVolumeRecycleScriptTests/(C1008_Present_or_unknown_temp_keeps_null_refusal*)\|(C1008_Busy_routed_and_land_in_flight_refuse*)\|(C1353_Wrapper_waits_for_a_bound_land_then_proceeds*)` | M-adjacent RollingVolumeRecycleScriptTests | every selected native method; report skips; 0 failed | 3 | 3 | true |
| CP-48 | S4 | CP-7 | linux-adjacent-measurement | `/*/*/RollingVolumeRecycleScriptTests/(C1008_Same_sha_and_partial_retries_are_safe*)\|(C1008_Option_manifest_is_strict*)\|(C1008_Wrapper_option_manifest_is_strict*)` | M-adjacent RollingVolumeRecycleScriptTests | every selected native method; report skips; 0 failed | 3 | 3 | true |
| CP-49 | S4 | CP-7 | linux-r2-final | `/*/*/RollingVolumeRecycleScriptTests/(C1008_Refusal_receipts_do_not_leak_secrets*)\|(C1008_Wrapper_refusal_receipts_do_not_leak_secrets*)` | R-receipt-custody | every intended method; 0 failed/skipped | 2 | 3 | true |
| CP-49.present | S4 | CP-7 | linux-r2-final | `/*/*/RollingVolumeRecycleScriptTests/C1008_Legacy_rolling_and_jq_rosters_remain_present` | R-legacy complete 31-assertion roster present | every intended method; 0 failed/skipped | 1 | 3 | true |
| CP-49.absent | S4 | CP-7 | linux-r2-final | `/*/*/RollingVolumeRecycleScriptTests/C1008_Legacy_rolling_and_jq_rosters_remain_absent` | R-legacy complete 31-assertion roster absent | every intended method; 0 failed/skipped | 1 | 3 | true |
| CP-49.missing_shell | S4 | CP-7 | linux-r2-final | `/*/*/RollingVolumeRecycleScriptTests/C1008_Legacy_rolling_and_jq_rosters_remain_missing_shell` | R-legacy complete 31-assertion roster missing_shell | every intended method; 0 failed/skipped | 1 | 3 | true |
| CP-49.failing_shell | S4 | CP-7 | linux-r2-final | `/*/*/RollingVolumeRecycleScriptTests/C1008_Legacy_rolling_and_jq_rosters_remain_failing_shell` | R-legacy complete 31-assertion roster failing_shell | every intended method; 0 failed/skipped | 1 | 3 | true |
| CP-50 | S4 | CP-7 | linux-adjacent-measurement | `/*/*/RollingVolumeRecycleScriptTests/(C1008_Retired_absent_null_is_accepted*)\|(C1008_Wrapper_retired_absent_null_is_accepted*)\|(C1008_Dry_run_preview_binds_a_dry_run_cleanup_receipt*)` | M-adjacent RollingVolumeRecycleScriptTests | every selected native method; report skips; 0 failed | 3 | 3 | true |
| CP-51 | S4 | CP-7 | linux-adjacent-measurement | `/*/*/RollingVolumeRecycleScriptTests/(C1594_Cross_sha_cleanup_image_binds_the_retired_temp*)\|(C1594_Older_retirement_receipt_is_ignored*)\|(C1594_Disagreeing_cross_sha_images_refuse*)` | M-adjacent RollingVolumeRecycleScriptTests | every selected native method; report skips; 0 failed | 3 | 3 | true |
| CP-52 | S4 | CP-7 | linux-adjacent-measurement | `/*/*/RollingVolumeRecycleScriptTests/(C1594_Antiphon_evidence_gitdir*)\|(C1008_Preview_helper_removal_is_owned*)\|(C1317_Census_tolerates_a_task_that_settles_without_a_land*)` | M-adjacent RollingVolumeRecycleScriptTests | every selected native method; report skips; 0 failed | 3 | 3 | true |
| CP-53 | S4 | CP-7 | linux-adjacent-measurement | `/*/*/RollingVolumeRecycleScriptTests/(C1319_Census_tolerates_a_pass0_settled_task_that_leaves_before_pass1*)\|(C1324_Census_tolerates_a_no_land_task_that_appears_in_pass1*)\|(C1326_Census_refuses_a_land_timestamp_or_land_pending_appearance*)` | M-adjacent RollingVolumeRecycleScriptTests | every selected native method; report skips; 0 failed | 3 | 3 | true |
| CP-54 | S4 | CP-7 | linux-adjacent-measurement | `/*/*/RollingVolumeRecycleScriptTests/(C1353_Host_census_passes_off_runner_lands_and_waits_for_bound_lands*)\|(C1361_Wrapper_census_tolerates_open_status_changes_between_list_and_detail*)\|(C1361_Wrapper_census_tolerates_open_status_changes_between_passes*)` | M-adjacent RollingVolumeRecycleScriptTests | every selected native method; report skips; 0 failed | 3 | 3 | true |
| CP-55 | S4 | CP-7 | linux-adjacent-measurement | `/*/*/RollingVolumeRecycleScriptTests/(C1697_Wrapper_standing_census_compares_only_rows_bound_to_server2*)\|(C1371_Census_distinguishes_cross_read_count_drift_from_malformed_counts*)\|(C1371_Post_removal_count_drift_does_not_abort*)` | M-adjacent RollingVolumeRecycleScriptTests | every selected native method; report skips; 0 failed | 3 | 3 | true |
| CP-56 | S4 | CP-7 | linux-adjacent-measurement | `/*/*/SeedSmokeTargetingPackContractTests/*` | M-adjacent SeedSmokeTargetingPackContractTests | every selected native method; report skips; 0 failed | 1 | 3 | true |
| CP-57 | S4 | CP-7 | linux-adjacent-measurement | `/*/*/StandingPipelinePolicyDocumentationTests/*` | M-adjacent StandingPipelinePolicyDocumentationTests | every selected native method; report skips; 0 failed | 1 | 3 | true |
| CP-58 | S4 | CP-7 | linux-adjacent-measurement | `/*/*/TestAppHostPathTests/*` | M-adjacent TestAppHostPathTests | every selected native method; report skips; 0 failed | 1 | 3 | true |
| CP-59 | S4 | CP-7 | linux-adjacent-measurement | `/*/*/VolumeRetirementApplyGateTests/*` | M-adjacent VolumeRetirementApplyGateTests | every selected native method; report skips; 0 failed | 1 | 3 | true |
| CP-60 | S4 | CP-7 | linux-r2-final | `/*/*/VolumeRetirementContentTests/Current_tooling_and_missing_history_do_not_block` | M-current-tooling | every intended method; 0 failed/skipped | 1 | 3 | true |
| CP-60.credentials | S4 | CP-7 | linux-r2-final | `/*/*/VolumeRetirementContentTests/State_credentials_refuse_before_removal` | M-state-credentials | every intended method; 0 failed/skipped | 1 | 3 | true |
| CP-60.keys | S4 | CP-7 | linux-r2-final | `/*/*/VolumeRetirementContentTests/Claude_session_keys_are_disposable_state_bound_into_the_inventory` | M-session-key-inventory | every intended method; 0 failed/skipped | 1 | 3 | true |
| CP-61 | S4 | CP-7 | linux-adjacent-measurement | `/*/*/VolumeRetirementContentTests/(Pristine_inner_docker_layout_is_disposable_and_bound_into_the_inventory*)\|(Worktree_content_and_admin_closure_are_complete*)\|(Evidence_names_and_filesystem_shapes_fail_closed*)` | M-adjacent VolumeRetirementContentTests | every selected native method; report skips; 0 failed | 3 | 3 | true |
| CP-62 | S4 | CP-7 | linux-adjacent-measurement | `/*/*/VolumeRetirementContentTests/(All_target_roles_are_classified*)\|(Read_failures_and_incomplete_inventory_refuse*)\|(Audit_identity_and_namespace_are_stable*)` | M-adjacent VolumeRetirementContentTests | every selected native method; report skips; 0 failed | 3 | 3 | true |
| CP-63 | S4 | CP-7 | linux-adjacent-measurement | `/*/*/VolumeRetirementContentTests/(No_content_or_credentials_are_executed_or_exported*)\|(Absent_work_does_not_skip_other_volumes*)\|(Preview_lists_every_offender_without_effects*)` | M-adjacent VolumeRetirementContentTests | every selected native method; report skips; 0 failed | 3 | 3 | true |
| CP-64 | S4 | CP-7 | linux-adjacent-measurement | `/*/*/VolumeRetirementContentTests/(Fetch_head_requires_proven_objects_not_fresh_names*)\|(Only_exact_known_seed_fixtures_are_disposable*)\|(Claude_bash_edit_diff_snapshots_are_disposable_tool_scratch*)` | M-adjacent VolumeRetirementContentTests | every selected native method; report skips; 0 failed | 3 | 3 | true |
| CP-65 | S4 | CP-7 | linux-adjacent-measurement | `/*/*/VolumeRetirementContentTests/(Fixture_setup_runs_once_across_audit_traps*)\|(Temp_recycle_refuses_a_present_owned_container_before_any_audit*)\|(Temp_recycle_generation_binding_refuses_a_present_owned_container_alone*)` | M-adjacent VolumeRetirementContentTests | every selected native method; report skips; 0 failed | 3 | 3 | true |
| CP-66 | S4 | CP-7 | linux-adjacent-measurement | `/*/*/VolumeRetirementContentTests/(Temp_recycle_topology_layer_refuses_a_present_owned_container_alone*)\|(Real_census_read_returns_only_the_census_text*)\|(Hold_lift_clear_after_deploy_is_not_refused_for_its_own_volumes*)` | M-adjacent VolumeRetirementContentTests | every selected native method; report skips; 0 failed | 3 | 3 | true |
| CP-67 | S4 | CP-7 | linux-adjacent-measurement | `/*/*/VolumeRetirementContentTests/(Hold_lift_clear_refuses_a_retired_or_other_build_runner*)\|(Exchange_inventory_refusal_names_its_check_and_detail_file*)\|(Exchange_registry_refusals_name_the_registry_cause*)` | M-adjacent VolumeRetirementContentTests | every selected native method; report skips; 0 failed | 3 | 3 | true |
| CP-68 | S4 | CP-7 | linux-adjacent-measurement | `/*/*/VolumeRetirementContentTests/(Exchange_proof_failure_names_its_stage*)\|(Exchange_registry_with_a_wrong_shape_is_invalid_before_the_exchange*)\|(Exchange_exports_only_this_projects_registered_origins*)` | M-adjacent VolumeRetirementContentTests | every selected native method; report skips; 0 failed | 3 | 3 | true |
| CP-69 | S4 | CP-7 | linux-adjacent-measurement | `/*/*/VolumeRetirementContentTests/(Exchange_inventory_diagnosis_skips_git_error_lines*)\|(Exchange_malformed_private_origin_lines_are_never_approved*)` | M-adjacent VolumeRetirementContentTests | every selected native method; report skips; 0 failed | 2 | 3 | true |
| CP-70 | S4 | CP-7 | linux-adjacent-measurement | `/*/*/VolumeRetirementReceiptTests/Preview_receipt_is_required_and_consumed` | M-adjacent VolumeRetirementReceiptTests | every selected native method; report skips; 0 failed | 1 | 3 | true |
| CP-71 | S4 | CP-7 | linux-adjacent-measurement | `/*/*/VolumeRetirementReceiptTests/Receipt_shape_custody_and_digest_are_strict` | M-adjacent VolumeRetirementReceiptTests | every selected native method; report skips; 0 failed | 1 | 3 | true |
| CP-72 | S4 | CP-7 | linux-adjacent-measurement | `/*/*/VolumeRetirementReceiptTests/Proof_directory_custody_matches_the_applying_account` | M-adjacent VolumeRetirementReceiptTests | every selected native method; report skips; 0 failed | 1 | 3 | true |
| CP-73 | S4 | CP-7 | linux-adjacent-measurement | `/*/*/VolumeRetirementReceiptTests/Volume_incarnation_and_operation_are_bound` | M-adjacent VolumeRetirementReceiptTests | every selected native method; report skips; 0 failed | 1 | 3 | true |
| CP-74 | S4 | CP-7 | linux-adjacent-measurement | `/*/*/VolumeRetirementReceiptTests/Freshness_and_policy_version_are_rechecked` | M-adjacent VolumeRetirementReceiptTests | every selected native method; report skips; 0 failed | 1 | 3 | true |
| CP-75 | S4 | CP-7 | linux-adjacent-measurement | `/*/*/VolumeRetirementReceiptTests/Legacy_receipts_are_history_only` | M-adjacent VolumeRetirementReceiptTests | every selected native method; report skips; 0 failed | 1 | 3 | true |
| CP-76 | S4 | CP-7 | linux-r2-final | `/*/*/VolumeRetirementReceiptTests/Manual_attestation_is_exact_and_cannot_waive_findings` | M-manual-attestation | every intended method; 0 failed/skipped | 1 | 3 | true |
| CP-76.public | S4 | CP-7 | linux-r2-final | `/*/*/VolumeRetirementReceiptTests/Manual_attestation_public_entry_preserves_all_guards` | M-manual-public-entry | every intended method; 0 failed/skipped | 1 | 3 | true |
| CP-77 | S4 | CP-7 | linux-adjacent-measurement | `/*/*/VolumeRetirementReceiptTests/Changed_content_and_remote_proof_refuse` | M-adjacent VolumeRetirementReceiptTests | every selected native method; report skips; 0 failed | 1 | 3 | true |
| CP-78 | S4 | CP-7 | linux-adjacent-measurement | `/*/*/VolumeRetirementReceiptTests/Every_delete_rechecks_full_live_guards` | M-adjacent VolumeRetirementReceiptTests | every selected native method; report skips; 0 failed | 1 | 3 | true |
| CP-79 | S4 | CP-7 | linux-adjacent-measurement | `/*/*/VolumeRetirementReceiptTests/Journal_intents_and_partial_resume_are_exact` | M-adjacent VolumeRetirementReceiptTests | every selected native method; report skips; 0 failed | 1 | 3 | true |
| CP-80 | S4 | CP-7 | linux-adjacent-measurement | `/*/*/VolumeRetirementReceiptTests/Lock_order_excludes_supported_writers` | M-adjacent VolumeRetirementReceiptTests | every selected native method; report skips; 0 failed | 1 | 3 | true |
| CP-81 | S4 | CP-7 | linux-r2-final | `/*/*/VolumeRetirementWrapperTests/Manual_parameters_require_operator_attestation` | M-adjacent VolumeRetirementWrapperTests | exactly 1 intended method; 0 failed/skipped | 1 | 3 | true |
| CP-81.bridge | S4 | CP-7 | linux-r2-final | `/*/*/VolumeRetirementWrapperTests/Bridge_validates_and_preserves_receipt_fields` | M-adjacent VolumeRetirementWrapperTests | exactly 1 intended method; 0 failed/skipped | 1 | 3 | true |
| CP-81.stack | S4 | CP-7 | linux-r2-final | `/*/*/VolumeRetirementWrapperTests/Retire_temp_ignores_missing_stack_and_historical_compose` | M-adjacent VolumeRetirementWrapperTests | exactly 1 intended method; 0 failed/skipped | 1 | 3 | true |
| CP-81.main | S4 | CP-7 | linux-r2-final | `/*/*/VolumeRetirementWrapperTests/Main_recycle_and_existing_deploy_contracts_are_preserved` | M-adjacent VolumeRetirementWrapperTests | exactly 1 intended method; 0 failed/skipped | 1 | 3 | true |
| CP-81.retained | S4 | CP-7 | linux-r2-final | `/*/*/VolumeRetirementWrapperTests/Retained_volumes_block_deploy_and_completion` | M-adjacent VolumeRetirementWrapperTests | exactly 1 intended method; 0 failed/skipped | 1 | 3 | true |
| CP-90 | S4 | tests/Antiphon.Agents.Pty.Tests -> bin-c1901-agpty/ | linux-spawn-impact | `/*/*/ProcessSpawnLimitTests/*` | R-1782 spawn-limit-rosters Agents.Pty | all named class; 0 failed | 1 | 1 | true |
| CP-91 | S4 | tests/Antiphon.SessionRunner.Tests -> bin-c1901-runner/ | linux-spawn-impact | `/*/*/ProcessSpawnLimitTests/*` | R-1782 spawn-limit-rosters SessionRunner | all named class; 0 failed | 1 | 1 | true |
| CP-92 | S5 | tests/Antiphon.Tests -> bin-c1901-s5/ | linux-auth-repair | `/*/*/RollingVolumeRecycleScriptTests/C1008_Host_proof_admits_only_an_authenticated_private_origin` | R-v1-transport | exactly 1 passed; real immutable published bundle; 0 failed/skipped | 1 | 5 | true |
| CP-93 | S5 | CP-92 | linux-index-repair | `/*/*/DerivedImpactIndexTests/*` | R-index | all 27 current methods; 0 failed/skipped | 27 | 2 | true |
| CP-94 | S5 | CP-92 | linux-publication-repair | `/*/*/VolumeRetirementPublicationTests/V2_invalid_bundle_cannot_use_present_head_fallback` | V7 | exactly 1 passed; including restoration; 0 failed/skipped | 1 | 3 | true |
| CP-95 | S5 | CP-92 | linux-publication-repair | `/*/*/VolumeRetirementPublicationTests/Selector_fixture_own_gitdir_file_stays_in_full_audit` | V8 | exactly 1 passed; exact census identity; 0 failed/skipped | 1 | 2 | true |
| CP-96 | S5 | CP-92 | linux-host-repair | `/*/*/VolumeRetirementPublicationTests/Host_salvage_snapshot_prunes_and_renews_exact_objects` | H3 | exactly 1 passed; prior headers and present-graph missing-ref vector; 0 failed/skipped | 1 | 1 | true |
| CP-97 | S5 | CP-92 | linux-wrapper-rerun | `/*/*/VolumeRetirementWrapperTests/Retained_volumes_block_deploy_and_completion` | R-wrapper | exactly 1 intended method; record actual outcome; 0 failed/skipped for green | 1 | 2 | true |
| CP-98 | S5 | CP-92 | linux-r2-final | `/*/*/VolumeRetirementWrapperTests/Retire_temp_uses_current_proof_protocol` | R-wrapper-portable | every intended method; 0 failed/skipped | 1 | 2 | true |
| CP-98.native | S5 | CP-92 | linux-r2-final | `/*/*/VolumeRetirementWrapperTests/Retire_temp_real_wrapper_renews_each_effect` | R-wrapper-native; existing 360s child cap; 7min row | every intended method; 0 failed/skipped | 1 | 3 | true |
| CP-99 | BaseAttribution | tests/Antiphon.Tests -> bin-c1901-base/ | linux-base-cost | `/*/*/C1364BoundLandWaitProofTests/C1364_Host_recycle_post_removal_reports_waited` | R-base-attribution | exactly 1 intended method; record actual outcome at task base; a deadline is incomplete, never green | 1 | 9 | true |
| CP-100 | BaseAttribution | CP-99 | linux-base-wrapper | `/*/*/VolumeRetirementWrapperTests/(Retained_volumes_block_deploy_and_completion*)\|(Retire_temp_uses_current_proof_protocol*)` | R-base-attribution | both exact methods at task base; record actual outcome; do not widen child deadline | 2 | 2 | true |
| CP-101 | R2Final | tests/Antiphon.Tests -> bin-c1901-r2-final/ | linux-renewal-green | `/*/*/VolumeRetirementPublicationTests/Salvage_deleted_after_sealing_refuses_ready` | R2-1 | exactly 1 green; pre-fix assertion red is retained | 1 | 2 | true |
| CP-102 | R2Final | CP-101 | linux-renewal-green | `/*/*/VolumeRetirementPublicationTests/Head_rolled_back_after_sealing_refuses_ready` | R2-2 | exactly 1 green; pre-fix assertion red is retained | 1 | 2 | true |
| CP-103 | R2Final | CP-101 | linux-renewal-green | `/*/*/VolumeRetirementPublicationTests/Salvage_replaced_after_sealing_refuses_ready` | R2-3 | exactly 1 green; pre-fix assertion red is retained | 1 | 2 | true |
| CP-104 | R2Final | CP-101 | linux-renewal-green | `/*/*/VolumeRetirementPublicationTests/Origin_unreachable_at_final_check_refuses_ready` | R2-4 | exactly 1 green; pre-fix assertion red is retained | 1 | 2 | true |
| CP-105 | R2Final | CP-101 | linux-renewal-green | `/*/*/VolumeRetirementPublicationTests/Origin_rebound_at_final_check_refuses_ready` | R2-5 | exactly 1 green; pre-fix assertion red is retained | 1 | 2 | true |
| CP-106 | R2Final | CP-101 | linux-r2-final | `/*/*/VolumeRetirementPublicationTests/Unchanged_objects_renew_at_each_ready_gate` | R2-6 | every intended method; 0 failed/skipped | 1 | 2 | true |
| CP-107 | R2Final | CP-101 | linux-r2-final | `/*/*/VolumeRetirementPublicationTests/Offline_scan_requires_live_completion` | R2-7 | every intended method; 0 failed/skipped | 1 | 2 | true |
| CP-108 | R2Final | CP-101 | linux-r2-final | `/*/*/VolumeRetirementPublicationTests/Origin_graph_corpus_seed_1901` | R2-8 | every intended method; 0 failed/skipped | 1 | 3 | true |
| CP-109 | R2Final | CP-101 | linux-r2-final | `/*/*/VolumeRetirementPublicationTests/Origin_graph_corpus_seed_1902` | R2-9 | every intended method; 0 failed/skipped | 1 | 3 | true |
| CP-110 | R2Final | CP-101 | linux-r2-final | `/*/*/VolumeRetirementPublicationTests/Origin_graph_corpus_seed_1903` | R2-10 | every intended method; 0 failed/skipped | 1 | 3 | true |
| CP-111 | R2Final | CP-101 | linux-r2-final | `/*/*/VolumeRetirementPublicationTests/Origin_graph_corpus_seed_1904` | R2-11 | every intended method; 0 failed/skipped | 1 | 3 | true |
| CP-112 | R2Final | CP-101 | linux-r2-final | `/*/*/VolumeRetirementPublicationTests/Salvage_withdrawn_after_staging_refuses_control` | R2-12 | all named methods; 0 failed/skipped | 1 | 2 | true |
| CP-113 | R2Final | CP-101 | linux-r2-final | `/*/*/VolumeRetirementPublicationTests/Head_rolled_back_after_staging_refuses_control` | R2-13 | all named methods; 0 failed/skipped | 1 | 2 | true |
| CP-114 | R2Final | CP-101 | linux-r2-final | `/*/*/VolumeRetirementPublicationTests/Origin_unreachable_after_staging_refuses_control` | R2-14 | all named methods; 0 failed/skipped | 1 | 2 | true |
| CP-115 | R2Final | CP-101 | linux-r2-final | `/*/*/C1008HostFixtureSetupInsertionTests/*` | R-fixture-entry | all named methods; 0 failed/skipped | 1 | 2 | true |
| CP-116 | R2Final | CP-101 | linux-r2-final | `/*/*/C1008PlatformContractTests/*` | R-fixture-platform | all named methods; 0 failed/skipped | 1 | 2 | true |
| CP-117 | R2Final | CP-101 | linux-r2-final | `/*/*/VolumeRetirementPublicationTests/Origin_graph_corpus_seed_1901_second_half` | R2-corpus-1901-second-half | exactly 1 method, 35 independent shapes; 0 failed/skipped | 1 | 3 | true |
| CP-118 | R2Final | CP-101 | linux-r2-final | `/*/*/VolumeRetirementPublicationTests/Origin_graph_corpus_seed_1902_second_half` | R2-corpus-1902-second-half | exactly 1 method, 35 independent shapes; 0 failed/skipped | 1 | 3 | true |
| CP-119 | R2Final | CP-101 | linux-r2-final | `/*/*/VolumeRetirementPublicationTests/Origin_graph_corpus_seed_1903_second_half` | R2-corpus-1903-second-half | exactly 1 method, 35 independent shapes; 0 failed/skipped | 1 | 3 | true |
| CP-120 | R2Final | CP-101 | linux-r2-final | `/*/*/VolumeRetirementPublicationTests/Origin_graph_corpus_seed_1904_second_half` | R2-corpus-1904-second-half | exactly 1 method, 35 independent shapes; 0 failed/skipped | 1 | 3 | true |
| CP-121 | R2Final | CP-101 | linux-witness-adjacent | `/*/*/(NativeLinuxPolicyWitnessMutationTests*)\|(NativeLinuxPolicyMatcherParityTests*)/*` | R-native-witness-model | both named classes; 0 failed/skipped | 2 | 2 | true |
| CP-122 | S4 | tests/Antiphon.Agents.Pty.Tests -> bin-c1901-agpty/ | linux-witness-mirror | `/*/*/NativeLinuxPolicyWitnessTests/*` | R-native-witness Agents.Pty | exactly 3 passed; 0 failed/skipped | 3 | 2 | true |
| CP-123 | S4 | tests/Antiphon.SessionRunner.Tests -> bin-c1901-runner/ | linux-witness-mirror | `/*/*/NativeLinuxPolicyWitnessTests/*` | R-native-witness SessionRunner | exactly 3 passed; 0 failed/skipped | 3 | 2 | true |
| CP-124 | R2Final | tests/Antiphon.PtyHost.Tests -> bin-c1901-ptyhost/ | linux-witness-mirror | `/*/*/NativeLinuxPolicyWitnessTests/*` | R-native-witness PtyHost | exactly 3 passed; 0 failed/skipped | 3 | 2 | true |
| CP-125 | R2Final | CP-101 | linux-r2-final | `/*/*/VolumeRetirementPublicationTests/Final_origin_binding_refresh_is_inside_effect_window` | R2-15 | exactly 1 passed; real delayed binding observation refuses Unknown | 1 | 2 | true |
| CP-126 | R2Final | CP-101 | linux-r2-final | `/*/*/VolumeRetirementPublicationTests/Binding_clock_record_requires_current_kernel_and_call` | R2-16 | exactly 1 passed; all 9 real clock-record variants discriminated | 1 | 2 | true |
| CP-127 | R2Final | CP-101 | linux-r2-final | `/*/*/VolumeRetirementPublicationTests/Binding_clock_precedes_fresh_origin_url_observation` | R2-17 | exactly 1 passed; actual URL changes at clock and is read afterward | 1 | 2 | true |

Every OR operand has parentheses and a trailing `*`; Markdown escapes its pipes.
New method names are deliberately distinct. Confirm source declarations and the
fresh execution TRX contain only the intended suffix matches. `Min` is a floor;
the Expect roster must also match, with no missing method hidden by arguments
or additional matches. A static roster check is not execution evidence.
The Plan's static check includes linked `tests/Shared` declarations in
`Antiphon.Tests`; matching only files physically beneath its project directory
would incorrectly call two required guard classes missing. Tool import and actual
execution remain Code obligations; Plan does not build the checkpoint tool just
to validate Markdown.

## Activation after landing

Nothing in this Plan deploys or runs a real retirement preview/apply. After the
caller records D-1..D-8, Code completes ordinary checks, Review approves the exact
candidate, and landing is confirmed, commission a separate Deploy using the
reviewed script set. Master publication alone is not activation.

From the authorized canonical control-plane checkout containing the landed
scripts, use the documented `deploy-server2.ps1 -Rolling -Sha <rollout-source>
-Phase retire-temp -DryRun` path at the appropriate rollout gate. The
`c590-real.ps1` bridge stages `c590-remote.sh`, `c1008-retirement.sh`,
`c1008-content-audit.sh` and `c1008-volume-metadata.sh` in a fresh operation-owned
directory on server2, checks SHA-256 and seals that script bundle before execution.
The coordinating control plane must load the matching `c1609-host-proof.ps1`
through `c1008-host-proof.ps1`. Compare the staged `tooling.sha256` to the reviewed
files and record the producer file hash; checking runner `buildVersion` or
restarting the server does not establish script activation.

Retirement uses the existing trusted `audit-current.json` runtime descriptor.
Verify its expected available image/protocol and ordinary rollout prerequisites;
do not replace it with an old cleanup image or alter live registry/credentials
to overcome a refusal. A script-only activation requires no application restart.
Keep this policy aligned with the current rc rollout source without using an
old `-Sha` as proof of which staged scripts ran.

The first live acceptance is a read-only preview with the newly staged scripts
and fresh host proofs. Retain the exact operation, source/tooling hashes, complete
findings, proof ref list and original OIDs. To exercise new salvage acceptance,
use only already-authorized preserved work whose canonical ref was separately
published and checked on its approved origin; do not seed real volumes with a
test commit. If no such work exists, retain local fixture evidence and mark live
salvage acceptance unobserved. `RecycleUnpublishedWork` or `RecycleWorktreeDirty`
stops the phase for recovery; malformed/unavailable proof remains Unknown.

Only a later authorized rollout action may consume a fresh Ready token through
the existing apply gate, re-proving objects before each effect. It must not reuse
the 2026-11-13 token or expire reflogs to obtain Ready. Run the documented
post-retirement cache/volume checks only after a successful apply. Do not claim
this Plan or local tests have qualified deletion of real volumes. Commission
the separate post-land verification companion with all 28 PCs and their variants and the exact
landing operation/source before closing that verification obligation.

--- next stage ---
next: review
handoff: Accepted D-1..D-8: accept only exact canonical origin salvage in temp/v2, retain patch/reflog/tag protections, and commission the over-30-minute performance Debug obligation. Then dispatch Code against the closed verification design; leave live activation to a separate Deploy.
artifact: docs/superpowers/plans/2026-11-14-card-1901-retirement-audit-plan.md

Round-2 Ready preparation correction: source 83840716c5cbd06e9857f53f70772ecb503ae82f
executed CP-4.V1 and CP-4.V2 as assertion reds (1 each): correct live proof, but
snapshot/manual/daemon/digest preparation after control aged the unchanged five-second
window. Prepare these inputs in process memory before the final binding observation;
publish no Ready artifact until control succeeds. Keep the same receipt schema,
manual admission checks, one-use nonce and final freshness assertion. CP-112/113/114
also assert absence of receipt.json after refused final control. PC-29 remains
pending SourceLanding Mutation. This is a normal-path timing repair, not a retry or
an enlarged timeout. Re-run the affected publication/receipt rows and unfinished
closed rows; retain already-green unrelated guards with their actual tested SHA.

The first receipt-preparation repair still left CP-4.V1 red at 6f2fdfcc9010ba903ec7405213e33c9960cad35d:
99.7 seconds, actual Unknown/2 after successful live control, no Ready receipt.
The final inventory completed about 2.8 seconds after observation and control was
durable at about 4.0 seconds; repeated external dirname/sed processes in the
remaining custody/header checks exhausted the unchanged five-second bound.
Use Bash path decomposition and one bounded header read, preserving every
ownership, mode, symlink, hard-link, header-binding and completion-age predicate.
All receipt/custody rows remain in the required adjacent selection.

R2-17 / CP-127 pins the real origin-observation boundary. The real helper enumerates
repositories first; the clock must immediately precede its final inventory URL
loop. The test changes the actual Git URL at clock emission and requires the new
URL in the actual inventory, after the clock and after repository enumeration.
Caching a URL before that clock fails this witness (PC-30 pending). Earlier prose
that times all repository/content discovery is superseded: those preparatory
checks still run, but the five-second bound covers all final URL observations,
approval recheck, live advertisement, custody and the effect. No scan is skipped.
At 7d87426eebb3a6d34121c53f5004fb62bada0346 V1-V6 and the renewal/delay/withdrawal
rows were green, but V7 positive control expired at5360ms before any Ready receipt.
The missing receipt rules out Ready-file batching as a repair for that failure.
