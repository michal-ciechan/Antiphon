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

### Checkpoints

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
| CP-5 | S3 | tests/Antiphon.Tests -> bin-c1901-s3/ | linux-host-regression | `/*/*/VolumeRetirementHostProofTests/*` | R-host | all 12 existing methods; 0 failed/skipped | 12 | 3 | true |
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
| CP-12 | S4-Windows | CP-11 | windows-host-regression | `/*/*/VolumeRetirementHostProofTests/*` | R-host native Windows | all 12 existing methods; 0 failed/skipped | 12 | 3 | true |
| CP-13 | S4 | CP-7 | linux-native-policy | `/*/*/ReleaseGatePlatformTests/(LinuxOnlyWindowsExclusionPolicy*)\|(C1686_LinuxOnlyCensus*)` | R-platform | exactly 2 passed; 0 failed/skipped | 2 | 1 | true |

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
the separate post-land verification companion with all 19 PCs and the exact
landing operation/source before closing that verification obligation.

--- next stage ---
next: decide
handoff: Record D-1..D-8: accept only exact canonical origin salvage in temp/v2, retain patch/reflog/tag protections, and commission the over-30-minute performance Debug obligation. Then dispatch Code against the closed verification design; leave live activation to a separate Deploy.
artifact: docs/superpowers/plans/2026-11-14-card-1901-retirement-audit-plan.md
