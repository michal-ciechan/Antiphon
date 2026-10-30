# CARD-1609: content-derived volume retirement

Round-2 Plan task `02c3c411`, branch `feat/card-task-02c3c411`, fast-forward
from `42d094a8c8cebb49c3277e20d7839a8cbe1a82c3` (round-1 plan and independent
review). Re-ground the document on landed CARD-1607; do not rebase this branch or
merge a target. This is a design for independent Review, not authority to retire
live volumes. Only this plan changes. Verification design is folded into this plan.
Next: independent design Review before Code.

## Round-2 revision ledger

Skeleton published first; each review defect below receives its own pushed amendment.
Review source: `.antiphon/task-3372044d.md`, read in full on 2026-10-30.

| Review defect | Revision obligation | Status |
|---|---|---|
| D-1 | Whole-minute checkpoint estimates; import verification recorded below | Done: original 11 rows import successfully after correction |
| D-2 | Landed CARD-1607 baseline and explicit fate of its two tests | Done: baseline and preservation R6 |
| D-3 | Complete read-only offender preview, human and machine forms | Done: D-15, C11 |
| D-4 | FETCH_HEAD, narrow fixture classifier, gone-tracking decisions | Pending |
| D-5 | Bounded census retries, durable partial/resume behavior | Pending |
| D-6 | Retained-volume deploy refusal, C849 contract, external caches | Pending |
| D-7 | Master/rc port order after top-up 38, definition scope | Pending |
| D-8 | Windows skip roster and closure checkpoint | Pending |
| D-9 | Documentation pins and checkpoint | Pending |
| D-10 | Real private-origin path baseline-red witness | Pending |

## Problem and evidence

A retired deployment's source SHA, Compose files and cleanup image say how it was
created. They do not establish whether deleting its current contents loses work.
The volume gate must inspect current content with current trusted tools, prove
publication on a credential-capable host, and authorize only the inspected volume
incarnations. Missing historical receipts must not block that process.

Round-2 code baseline, read without merging on 2026-10-30: master
`98fc3ceb6440a17b68cafe3ecc21c76d190b18ca` and rc/2026-10-27
`ade54222543381e2e1b31ad79d8ae33a4035f70b`. CARD-1607 is landed on master as
`4277f94cb3f85236409db1ec286ba5b6a1a8c7e1` plus parser fix
`337c94aeb22bd5d4714d26944b232ac4cbd7ccfb`, and rc as
`29218e553d15854fca3540f29fd5cb3df2d2b122`. The four touched existing production
scripts (c590-remote, c590-real, deploy-server2, c1008-host-proof) are byte-identical
between those inspected master/rc tips. The assigned branch intentionally retains
older product source; Code must start from the then-current landed baseline, carry
this plan forward, and record its actual baseline SHA before S1 reds.

Round-1 evidence read on 2026-10-29 (historical observations):

- Antiphon board CARD-1609, CARD-1607 and CARD-1594, using `scripts/card.ps1 get`.
  CARD-1594 records four independent refusal families and the earlier writable
  alias / relocated `/work` / inaccessible-program defects. Its current board
  state is historical evidence, not a claim that all those defects remain at base.
- `scripts/c590-remote.sh`: `c1008_git_program`, both audit callers, generation and
  cleanup-image lookup, volume census, locks, recycle journal and deletion loops;
  `scripts/deploy-server2.ps1`, `scripts/c590-real.ps1`, the Docker owner and tests.
- CARD-1607's `scripts/c1008-host-proof.ps1` and its shell consumer at
  `d301a70ba8855e8180708d85b3bafecbbd838420`, branch `feat/card-task-b7de5a59`.
  That historical branch is superseded by the landed revisions above. Round 2 reads
  the landed helper and both consumers directly; no product merge is part of Plan.
- Deploy task `a04846da-9cc1-477a-92f2-9fecfb4b3725` and both prototype files at
  `C:\src\Antiphon\.antiphon\rolling-server2\manual-retire-20261029T022320Z\`.
  The original delegate's file route returned 404 after retirement; a surviving
  agent's supported Files read route for the same canonical workspace returned
  both complete files, with `truncated=false` and `missing=false`.
  `manifest.json` has 1,249,870 characters and was parsed in full; `proof.md` has
  25,284 characters. All 4,102 Antiphon and 9 school-revision publication rows
  report equal/ancestor, and all 59 worktree status records report clean.
- Conventions: the 2026-10-24 CARD-1540 master-recut and runner-lease-eligibility
  plans; `docs/testing-and-build.md` checkpoint/filter/performance sections;
  `docs/test-performance-standards.md`, `docs/orchestration-loop.md`,
  `docs/ops-http.md`, `docs/agent-credentials.md` and `docs/docker-stack.md`.

The prototype records two repositories, 59 worktree directories, an absent
`/tmp/review-adcc78ef` registration, 913 state files and four exact-name removals.
Its host `ls-remote` and scratch ancestry workflow is the model to retain. Its
omissions are not safe defaults: it excludes 32 reflog-only commits, uses peeled
commit IDs in its two annotated-tag proof rows, lacks Docker incarnation/freshness
bindings, and records removal in prose rather than a machine intent/result ledger.
This does not establish that the old deletion lost work; it establishes what that
receipt cannot authorize automatically. One 111-byte `claude/sessions/*.key` was
classified manually as a process marker. Do not generalize that exception by name.

## Ground truth

| Card assumption | Code / evidence at the pinned source | Consequence |
|---|---|---|
| All preview failures are still the original dry-run mismatch | Base `c994_lookup_image` accepts a named preview and has cross-SHA fallback, but still rejects conflicting or missing images and binds historical cleanup context. | Replace the dependency, not just one refusal branch. |
| The audit already lists every blocker | Landed `fail`, `refuse_unknown`, `refuse_dirty` and `refuse_unpublished` exit 2 immediately; one finding hides later repositories. | D-15 adds complete v2 collection; v1 behavior stays separate. |
| A passing preview is a delete capability | `c1008_recycle` returns from preview without an authorizing journal; missing image can yield `auditPending=true`. Apply independently audits. | Introduce explicit Ready proof; pending never authorizes apply. |
| Current tooling means current creating SHA | Apply falls back to `session-testing:${SHA:0:12}`; preview reads `.image` from cleanup. `case_retire_temp_runner` requires old stack.env. | Resolve trusted current audit runtime separately from retirement history and deployment `-Sha`. |
| Audit is already offline and equally hardened | Apply uses create/start/rm, read-only work/tmp mounts, but lacks preview's cap/security/init flags. Preview runs on the default network; neither uses `--network none`. | One owned, offline, read-only helper implementation for both phases. |
| Private host proof already proves ancestry | CARD-1607 emits advertised refs only after classified auth failure; the shell then uses fetched/present heads. `created >= started` has no upper age bound. | All origins move to a complete, fresh host proof; retain fail-closed error handling. |
| Branches and worktree HEADs are the whole safety gate | Existing scanner protects loose/packed refs, reflogs, pseudorefs, linked admins, indices, object completeness and unknown Git shapes. | Preserve that closure; no reflog or tag weakening. |
| Every target volume is audited | `c1008_audit` reads work plus registered tmp checkouts. No work volume returns `absent` before other content checks; state is not mounted. | Typed role inventory for every present target, including state and non-work content. |
| Container absence proves no access | `c1008_references` additionally detects bind sources overlapping a volume mount; named-volume queries alone miss them. | Preserve all-container census and canonical alias checks. |
| Existing receipts bind an immutable Docker ID | `c1008_volume` has Name/CreatedAt/Mountpoint/selected labels/driver/options; named local volumes have no container-style immutable Id in this implementation. | Bind daemon identity and the whole incarnation tuple; never substitute name alone. |
| Final removal is already exact-name-only | Temp uses `compose_temp down -v`, then verifies target absence; main uses exact volume rm. | Temp retirement switches to an exact recorded allowlist, no Compose deletion. |
| Historical Compose is necessary when temp is absent | Both C994 cleanup and recycle materialize models before absence is fully handled; generation binding can inspect old source. | An absent generation needs no old model. Exited-container ownership comes from fresh actual topology. |
| A checksum proves a receipt came from the operator | Current JSON/hashes are files under trusted host custody; a copied self-authored hash is not authentication. | Apply accepts only its protected host journal, not arbitrary supplied PASS JSON. |
| Manual proof is already a reusable receipt schema | Prototype lacks schema, operation/volume incarnation, expiry and durable intents; publication rows omit reflog objects and original tag objects. | Preserve as historical evidence; no automatic conversion to Ready. |

## Decisions

D-1 through D-15 are the stated defaults for review. They make the design concrete;
they do not authorize relaxing a refusal. Operator questions are collected below.

**D-1 — Separate retirement from deployment, starting with retired temp.** Add a
version-2 content retirement path shared by normal `retire-temp` and the manual
entry. Main `redeploy-old` still has a deployment/recreation generation contract;
its source/Compose bindings remain. Changes to shared scanning preserve its v1
behavior through an adapter. No main-state/cache recycling, pruning, runner-row
removal or restart is added. Rejected: deleting generation checks globally, which
would weaken main's recreation safety for an unrelated reason.

**D-2 — Preserve what counts as published and what is disposable.** Every retained
Git location from the existing scanner stays in scope, not just `refs/heads`:
local/tag/tracking/other refs, common and linked HEADs, both reflog sides,
ORIG_HEAD/FETCH_HEAD and supported recovery pseudorefs, stash, linked private refs
and admin indices. Stash is reported and its retained objects must pass the same
publication rule; active operations always refuse. A commit must satisfy the
existing origin-head reachability predicate; advertised non-commit objects retain
the existing exact-object / reachable-tree rules. Never replace an annotated tag
with its peeled commit as the object being proved. Missing required commits or
trees, grafts, replace refs, unsupported ref/index formats and unreadable state
remain unknown/refused. Absent promisor blobs retain the existing allowance with
lazy fetch disabled. Unnamed dangling objects retain existing gc-like semantics.
"Reproducible" does not mean a model thinks a commit could be recreated: it means
already permitted ignored/generated artifacts, or the same Git object recoverable
under the existing publication predicate. Rejected: waiving unpublished work with
an archive, an attestation, a matching tree, a reflog age or a task status.

**D-3 — Bind safety, record provenance.**

| Binding | Classification | Required behavior |
|---|---|---|
| Exact target names, roles, daemon identity, volume incarnation, operation id | Safety | Required at preview, apply, each delete and resume. |
| Fresh complete content inventory and origin proof, repository/origin identity, policy version | Safety | Required; a missing row is unknown, never implied PASS. |
| Retirement epoch, runner/project identity, drain/zero/routing/land census and no references | Safety | Retain and refresh; historical receipts cannot supply live observations. |
| Receipt custody, completion, freshness, attestation when manual | Safety | Required independently of historical build artifacts. |
| Current executing script hashes and resolved audit-image digest | Execution integrity plus provenance | Resolve trusted current tools once per phase; prevent mid-phase substitution; record them. No equality to the creating build. |
| Creating rc SHA, image digest, historical Compose digest, cleanup operation/run id | Provenance | Optional; absent/conflicting/invalid historical values are warnings, never content-gate refusals. Do not parse arbitrary old files in the authorization path. |
| Actual exited-container id/state/mount identity | Safety for container removal | Preserve exact live identity; its image is recorded, not required to be installed or equal to an old receipt. |

Rejected: removing all hashes. Inventory/receipt/tool integrity hashes serve a
different purpose from equality to a retired build.

**D-4 — Current trusted audit tooling, no historical image lookup.** Extend the
existing deployment tooling to publish an owner-writable, otherwise non-writable
`audit-current.json` beside host maintenance configuration, outside all target
volumes. It contains supported audit protocol/policy versions and the installed
current audit image reference. Resolve that reference with `docker image inspect`
to a digest once at phase start; revalidate supported tools and UID 1654 before
use. A trusted operator may explicitly select another installed compatible audit
image through `-AuditImage`; record that choice, never read it from a volume or
old cleanup receipt. A missing current runtime is a current prerequisite failure,
with instructions to install current tools, not a demand to recover the old build.

Stage current scripts in a private operation directory and record their hashes.
Stop overwriting a shared remote script path for concurrent retirement requests.
An apply started after a current-tool update uses the new tools. Compatible policy
versions may revalidate an older preview; an unsupported or changed policy requires
a new preview. This is not a cross-build equality test. No `git show <creating-sha>`
or image pull by historical SHA occurs in retirement. Retain old `-Sha` calls as
provenance-only compatibility input for `-Phase retire-temp`; make it optional
there, still required for actual deployment phases. Rejected: mutable `latest`
resolution on every helper, a digest supplied by untrusted inventory, or an
arbitrary old script/image selected because its receipt happened to exist.

**D-5 — One offline helper and a complete role inventory.** Use current audit code
in both preview and apply with read-only volume mounts, `--network none`,
`--cap-drop ALL`, `--security-opt no-new-privileges`, `--user 1654:1654`, `--init`,
a read-only root filesystem and owned bounded scratch. Retain `/work` exactly.
Mount runner-tmp at `/tmp` when present so absolute linked-worktree pointers keep
their production meaning; put helper scratch at a separate `/audit-scratch`
tmpfs and set TMPDIR there. No Docker socket, host root, credential directory,
SSH agent, provider auth or ambient secret environment enters the helper.

Keep GIT_OPTIONAL_LOCKS=0, GIT_NO_LAZY_FETCH=1, GIT_NO_REPLACE_OBJECTS=1 and disabled
system/global config. Force hooks/fsmonitor/textconv/filters and other external
commands off in the audited Git path; do not execute repository-supplied commands.
The scanner uses explicit Git-dir/worktree coordinates and the existing raw
index/content comparisons, including assume-unchanged/skip-worktree cases.
Unrecognized configuration that can alter proof or execute a program refuses.
All path enumeration is lossless, confines pointers, rejects control characters
where representation is ambiguous, and propagates traversal/permission failures.

| Target role | Required content classification |
|---|---|
| work | Enumerate every repository and linked admin once; all worktrees clean except already allowed ignored files. Traverse the whole work root for unclassified Git state. Preserve the special no-index seed rule, which does not allow arbitrary ignored content. |
| runner-tmp | Audit registered checkouts in their real namespace; enumerate the rest so an unregistered repository cannot hide as scratch. Known ephemeral runtime/build/log artifacts remain disposable; unknown Git-shaped state refuses. |
| runner-state | Host-side names/types/ownership scan only, no credential-file contents and no mount into the helper. Reject auth.json, .credentials.json, credential stores, token/SSH/private-key/certificate names and ambiguous credential-like files. Preserve strict no-follow confinement and unreadable-path refusal. The prototype's session `.key` is ambiguous by default, not a blanket exception. |
| dind-data | Read-only host metadata inventory, without starting its daemon or a root container. Require no nested containers, no named persistent volumes, no unknown persistence store, and only recognized disposable image/build-cache layout under a versioned classifier. If that cannot be established from the offline layout, refuse; do not chmod/chown it or assume an inaccessible directory is cache. |
| evidence / bindings | Preserve the full existing forbidden-name roster at every depth, including .git, HEAD, objects, refs, commondir, gitdir, packed-refs, reftable, shallow and recovery names. Apply it to top-level test-evidence/workspace-bindings as well as existing admin evidence directories. Symlinks, special files, hard-linked regular files and unreadability refuse. Names only; do not read evidence payloads. |

The state/dind metadata walker can use the existing narrow privileged host read
boundary; it cannot open credential payloads, change permissions, start daemons,
or follow links. Record counts and classification version. No absent-work shortcut
skips present state/tmp/dind. No classifier creates a volume when inspecting it.
Known generated/ignored data may be deleted as existing policy allows; neither
names-only scanning nor publication proof promises recovery of arbitrary secrets
or work disguised inside those payloads. That limitation is explicit, not a waiver
for an unclassified repository or storage layout.

**D-6 — Host publication proof for every origin.** Since the helper is offline,
all publication network reads run on the existing credential-capable control host,
using ambient Git login and a fresh isolated scratch bare repository per distinct
trusted origin. Reuse one fetched object graph across that origin's repositories
within a phase. Do not use a real worktree, its Git configuration or the target
volume's object store as the credential-bearing process's repository.

The offline inventory requests exact repo-relative identities, canonical-origin
hashes, object IDs/types and location counts/digests. Resolve each origin against
trusted project/repository configuration; reject mismatches, unapproved hosts,
userinfo, local/file/ext/helper protocols and redirects outside that trust set.
Only recognized HTTPS/SSH origin forms are allowed. Production cannot accept an
inventory-provided executable, credential-helper config or arbitrary URL.
Test origins use an explicitly isolated fixture adapter, not a production bypass.

For each approved origin: successful `ls-remote --heads --tags`, parse the complete
advertisement including tag objects and peeled rows; fetch exactly that advertised
snapshot into scratch; prove every requested object using D-2; re-observe the
advertisement to detect a moving snapshot. A missing ref/object, fetch mismatch,
unsupported object type, malformed/duplicate row, truncation or command failure
refuses. A changed remote snapshot may be retried once within the original bound;
continued churn refuses. Error 128 is not authorization. No non-auth error is
reclassified as auth, and no failure falls back to cached tracking refs as proof.

Return only ref names, object IDs/types, witness refs, verdicts and binding metadata
(origin hash, repo id, inventory digest, operation/phase nonce, observation times).
Do not transport raw origins with embedded credentials, Git stderr, headers or
credential helpers. Host claims are trusted under protected host custody and
exact request/response binding; a digest alone is not authentication. The
credential-free helper can validate completeness and local object identity;
ancestry was established by the host from the real advertised graph, not by the
boolean word PUBLISHED. Preserve exact-object proof for tags/tree/blob cases.
Rejected: mounting host credentials, allowing only a subset of requested tips,
using the existing auth-fallback advertisement as a full ancestry certificate,
or assuming all SSH/network failures mean private origin.

**D-7 — A new proof receipt, separate from all historical receipts.** Use a strict
schema-2 envelope and append-only phase artifacts in a protected host operation
directory; copy human-readable `manifest.json` and `proof.md` into the ignored
rolling evidence directory. Host records are authoritative; the copied files are
review material. `proof.md` is rendered from structured rows, never parsed to
authorize deletion. Large rows go in streamed files and are hashed/read by file,
not a giant jq argv (the prototype already exceeds practical argv limits).

| Record | Required fields / validation |
|---|---|
| Envelope | schema=2, policyVersion, operationId, phase nonce, mode=scripted/manual, projectId, runnerId, retirement instant, host/daemon identity, start/completion/expiry instants, state, strict typed outcome and all artifact digests. Reject duplicate JSON keys, unknown authorizing members, invalid sizes, invalid types and incomplete writes. |
| Each target | Exact name and role; presence; driver/scope/options; unmodified CreatedAt; canonical mountpoint plus host stat device/inode/ctime; complete ownership labels and any future volume UUID label. Hash canonical field encoding. Docker name is not an immutable Id. Missing required old-volume identity refuses; missing old receipt does not. |
| Inventory | Present/absent target roster, complete repo/admin/worktree/ref/object/evidence/state/dind row counts and digests, dirty/unknown findings, start/end identity observations. Hash the complete safety-relevant inventory, not just HEAD or timestamps. |
| Publication | Complete requested-object roster, one unambiguous verdict/witness per object, origin advertisement digest, origin observations, inventory digest, operation/phase and host completion. |
| Provenance | Optional old source/image/Compose/cleanup refs; current script hashes/image digest and protocol; warnings. Historical conflicts never change Ready to refused. |
| Apply ledger | Preview digest, fresh validation digests, ordered exact targets, per-target delete intent/result, final absence, partial/refused/completed state; attestation when manual. |

Write a temporary private regular file, flush file data, atomically rename and
flush directory metadata on the Linux deletion host before acknowledging Ready or
an intent. Refuse links, hardlinks, unexpected owners, writable ancestors and a
journal path inside any target. Never trust a caller's arbitrary local PASS file.
A caller names an operation ID and expected receipt digest; the host reopens its
own exact protected record. Preserve the original preview as an immutable artifact.

Preview expires 15 minutes after successful completion. Freshness is checked when
apply starts; it is not an execution deadline for a potentially long new audit.
Apply creates a new phase nonce and reruns the full content/publication gate.
Immediately before the first deletion, publication observations must be at most
15 minutes old and no more than 60 seconds in the future; re-observe the origin
advertisements after long content checks. Those re-observations must still prove
the same objects. Use host UTC plus monotonic phase duration, never file mtime as
a clock. Clock uncertainty refuses. Rejected: a preview valid forever, a lower
bound only, or expiring an otherwise valid long apply just because preview was slow.

**D-8 — Preview, apply, resume.** The predicate is:

`admitted retirement AND exact incarnations AND complete safe inventory AND fresh
complete publication proof AND trusted Ready receipt AND no current references`.

1. Preview acquires the locks below, observes the entire target set and live
   retirement guards, and writes Pending. If exited owned containers still
   reference targets, show their exact removal candidates but issue no Ready
   volume receipt. No workload container/volume removal occurs in preview.
2. Once container absence is established, inventory all target roles, obtain the
   host proof, validate again, join/remove the owned audit helper, and publish
   Ready with zero findings and its target/incarnation roster. D-15 collects all findings before refusal; failed observation
   is Refused, not an empty roster. Release locks; no lock survives waiting for
   an operator to invoke apply.
3. Apply requires a protected unexpired Ready receipt for the exact operation.
   Reacquire locks, recheck current runtime/policy, live guards and every target
   incarnation; rerun content and host publication proof with a fresh phase nonce.
   Compare the full safety inventory against preview. A changed inventory refuses
   and requires a new preview, even if the new object is also published. Changed
   remote advertisements may pass only with fresh proof of all the same objects.
4. Verify helper exit and absence. Recheck the all-container census, bind aliases
   and each exact `docker ps -a --filter volume=<name>` result. No helper remains
   attached during this final check. No unknown output/error is interpreted as zero.
5. Persist the complete delete plan and then each target's intent before executing
   `docker volume rm -- <exact-name>` without force. Immediately before every rm,
   revalidate that incarnation, full live guards and all remaining references.
   Keep work last in the stable order state, dind, tmp, work, so a partial failure
   preferentially retains repository data. All targets must pass before the first
   effect; removal is not an atomic transaction across volumes.
6. Confirm absence by successful census plus typed not-found; persist result
   before proceeding. A write/read/delete error stops further deletes and reports
   partial when any effect may have occurred. No success solely from exit zero.
7. Resume requires the same operation's durable intents. An absent target with a
   prior intent can become `removed-observed` (not a claim who removed it). A new
   same-name incarnation always refuses. Re-prove every remaining present role
   with current tools. Cross-volume dependency changes require a new preview of
   the remaining set. Never recreate a deleted volume to inspect it. Missing
   journal means a new preview of present volumes, not guessed historical results.

Normal `retire-temp` remains a single operator action: its coordinator performs
and persists preview then consumes it through the same apply path; this preserves
`-Phase all`. `-DryRun` stops after preview. New `-RetirementProof <operation-id>`
selects an explicit previous preview. If present exited containers require cleanup,
normal apply first performs the existing exact-ID cleanup, then generates a fresh
Ready preview of now-unreferenced volumes. It cannot treat a pending container
preview as a volume capability. No human prompt is added to the scripted path.

**D-9 — Remove historical coupling before the content gate too.** The retired-temp
entry must not first fail `TempStackMissing`, source lookup, old Compose parsing or
C994 image selection. When census proves no temp containers, skip those branches
entirely. With exited containers, validate actual IDs, service/ownership labels,
state and complete mounts against a versioned supported topology roster, including
old supported credential-bind destinations; unknown mounts still refuse. The
creating image is a diagnostic field, not required to exist. Persist exact
container intents/results and recheck current topology as C994 already does.
Running/paused/unknown containers refuse. Do not inspect/delete writable-layer
content via volume authorization: retain C994's separate established container
retirement contract. Main generation reconstruction remains unchanged.

**D-10 — Manual is an attested entry to the same gate.** Add
`scripts/retire-runner-volumes.ps1` with mutually exclusive Preview and Apply
parameter sets. It is callable by the operator on the credential-capable control
host, or a specifically commissioned delegate with recorded human authority and
access to the existing owner-only operator credential. Possessing an operation
ID or writing `operatorName` is not itself authority. Use the existing
`Get-RunnerOperatorToken` custody path; do not print or copy that token to helpers.
No new unauthenticated server API or interactive confirmation is introduced.

Proposed interface (new commands, not commands to run during this Plan):

- `retire-runner-volumes.ps1 -Preview -RetiredRunnerId <id> -ProjectId <id>
  -VolumeName <exact-name-list> -Manual -AttestationFile <operator-file>`
- `retire-runner-volumes.ps1 -Apply -OperationId <id> -ExpectedProofDigest <sha256>
  -Manual -AttestationFile <operator-file>`

The human attestation records actor, UTC, authority reference (operator decision
or exact delegated task), reason, operation ID, exact target fingerprints and
preview digest. Preview's initial intent names the exact target list; apply's
attestation must bind the completed preview. Reject blank identity/reason, wrong
mode or stale/different digest. A template is generated for review, never silently
signed by the script. Store under
`.antiphon/rolling-server2/manual-retire-<UTC>/manifest.json` and `proof.md`; resolve
timestamp collisions without overwriting an existing operation. The path has the
same permission/policy/content/live-guard checks as scripted apply. It needs no
old cleanup receipt, but always needs the new current proof. No `-Force`,
`-SkipAudit`, per-finding override or direct-name deletion fallback exists.

This is attestation under trusted local operator authority, not cryptographic
proof of a human's identity from a self-entered string. Existing standing rollout
authority can cover scripted retirement; manual attestation needs an explicit
human instruction covering that manual operation. Rejected: auto-switching to
manual on failure or treating an operator assertion as publication evidence.

**D-11 — Migration is re-proving, never relabeling.** Schema-1 cleanup/recycle
receipts, CARD-1607 proof v1 and the prototype remain readable history with their
original outcomes/costs. They cannot authorize v2 deletes. No bulk rewrite,
receipt repair or historical image recovery is required. A fresh v2 preview can
start when all history is missing. An unfinished v1 operation may be imported
only as a provenance link and observed prior effects; current content/incarnation
proof governs remaining volumes. Never infer an absent target was removed by
this operation without its durable intent. Main v1 resume stays in its old path.
Old clients cannot write v2 by spoofing v1 fields; unsupported versions fail closed.

**D-12 — Fail closed on ambiguous state rather than widen allowances.** The
prototype's `.key` exception and any unrecognized nested-Docker layout stay
refused pending a separately reviewed classification. Do not read secret contents
to decide. A future exact harmless-name rule needs shape/ownership/type bounds and
a regression witness, not an operator free-text bypass. This may require operator
recovery of that file outside the retirement command, followed by a new preview.

**D-13 — Retain operational gates and bounded ownership.** Keep retired/draining/
redirect/zero sessions/zero queued tasks/no routing or pins/no bound land checks,
including full paginated project closure and unknown-counter rules. Offline null
runnerSessions is admitted only under the existing retired-and-absent exception.
The two-process host-proof exchange is noninteractive: it has bounded deadlines,
EOF/cancel means refusal, and all helper/transport children are owned and joined.
This adds no agent session waiting for user input and changes no parking behavior.

**D-14 — Measure the narrower design; do not repeat broad qualification.** Keep
Git inventory once per common directory and worktree per phase; fetch once per
origin per phase, with deduplicated object proofs. Preview and apply are distinct
observations, so sharing their mutable evidence is forbidden. Share expensive
test setup once per run. Exact checkpoint selection below is the ordinary scope;
no full Unit/assembly or old 34-case Docker comparison is implicitly added.

**D-15 — List every offender in one read-only preview.** Both scripted DryRun and
manual Preview use the same list-all mode by default. Replace v2's first-failure
exit path with a typed findings collector; preserve v1's fail-fast adapter.
Enumerate each common directory/worktree/role once, keep scanning independent
locations after a dirty/unpublished/unknown finding, and request host proof for
all safely identifiable repositories even if another repository already refused.
Do not restart an audit per finding. A broken directory is a typed unknown with
its exact path and scope; continue other safely traversable scopes. It is never
represented as an empty or complete subtree. Fatal loss of custody, confinement,
or time/output budget stops the scan with complete=false, preserving every row
already collected and listing each unvisited scope. Neither findings nor an
incomplete enumeration can produce Ready or permit any container/volume deletion.

Write streamed `findings.jsonl` plus a strict summary (complete, visited/unvisited
scopes, counts by reason, artifact digest), and render `proof.md` from those rows.
Each row carries volume name/role, repository and admin/worktree paths relative to
that volume, location kind, ref/pseudoref/reflog-side/index path, full object ID and
type where present, offending commit IDs (all unpublished retained commits, not
only their tip), reason code and safe recovery advice. Non-Git findings use null
objectId/commitId with an explicit not-applicable reason. Deduplicate identical
object proofs, never their distinct location provenance. Use reversible JSON path
escaping and deterministic ordering, without reading evidence/credential payloads.
Raw credential-bearing origins or Git stderr never enter either form. To preserve
existing secret-name redaction, credential-shaped path components are represented
by an opaque finding ID and safe redacted path in exported artifacts; the exact
path mapping is retained only in the protected host-local roster, never transcript
or exported receipt. The operator uses that roster locally for exact recovery.
Both forms state that limitation; no redacted path is silently called exact.

A successful complete inventory with findings is Refused with the whole roster;
a successful zero-finding inventory may continue to Ready only through all other
gates. No pagination, clipping or summary-only counts substitute for the stored
roster. Recovery is outside this command; run one fresh complete preview afterward.
Rejected: five redeploy attempts to discover five blockers, suppressing later
findings after the first unsafe repo, or calling an interrupted scan complete.

## Locks, concurrency and deletion failure modes

Canonical order: host rollout lock (existing FD 8) -> cache-maintenance lock
(existing FD 9) -> operation journal lock -> sorted exact-volume reservations.
Reservations are durable host records keyed by daemon and volume incarnation;
preview owns them only while running, apply owns them through its final receipt.
All supported create/recreate/cleanup paths for these temp names must acquire the
rollout lock, including the current `case_deploy_temp` path. Cache-only holders
never acquire rollout later. Nested helper/host proof processes acquire none of
these locks and never call back into a path that needs them. The wrapper does
not hold a separate long-lived SSH lock and ask a second remote command to acquire
it again; one host coordinator owns the phase and accepts bound proof responses.

Fresh live retirement observations happen after lock acquisition and at each
effect boundary. A pending repository child journal refuses; no Git mutation
lease is acquired inside a read-only volume. A live process/foreign bind/open
writer that cannot be excluded is unknown/refused. No stale-lock deletion or
unmount/remount of the live storage root is used. Existing 60-second lock waits
remain finite; waiting makes no changes and writes no Ready proof.

Docker has no `volume rm if CreatedAt == X` compare-and-delete API here. Locks,
retirement, all-reference checks and immediate incarnation reinspection protect
supported writers, not an arbitrary privileged host/daemon actor bypassing the
protocol. D-7's tuple (and future creation UUID) detects observed replacement;
there is still a narrow inspect/rm race against an out-of-protocol privileged
actor. This is an explicit threat-model boundary and a design-review gate.
Never claim that a timestamp alone provides atomic deletion. If independent
privileged writers are an in-scope operating condition, enforce a daemon-wide
maintenance/exclusive mutation boundary before enabling apply; more polling is
not a substitute. No silent best-effort assumption is allowed.

| Failure/cut | Required result |
|---|---|
| Lock busy, coordinator EOF, remote disconnect | No new delete; cancel/join owned helpers, retain Pending/Refused evidence, release locks after children are known dead. |
| Helper timeout, kill/cleanup unconfirmed | No Ready or delete; record owned container ID and unknown cleanup, never reap a foreign helper by prefix. |
| Ready file rename/flush/copy fails | No apply authorization. Authoritative host evidence retained; caller gets receipt-unavailable. |
| Replaced volume, changed retirement, new task/land/reference | Refuse entire remaining set; preserve already recorded effects. |
| Crash before durable delete intent | No authorized effect; present volume needs fresh proof. |
| Crash after intent/before or after rm | Resume observes exact incarnation or confirmed absence; ambiguity stays partial/unknown. |
| Result write fails after successful rm | Stop; intent records possible effect, later resume must observe it. No second delete by name alone. |
| Later target fails after an earlier rm | Partial, never rollback by recreating; retain remaining targets and all evidence. |
| Final receipt copy fails | Host ledger remains authoritative; repair copy under same ID without replaying removals. |
| Current audit runtime/policy changes between phases | New phase resolves current runtime; incompatible policy requires a fresh preview, never recovery of historical build. |

## Defects and risks: fail-open analysis first

Each row names a way deletion could lose unpublished work or authorize the wrong
effect. Unknown inputs fail closed; matrix coverage below includes positive
controls so a test cannot pass merely because every attempt was refused.

| ID | Failure route | Prevention / witness |
|---|---|---|
| F-1 | Missing history interpreted as safe, or inventoryPending interpreted as audited | Require complete current Ready proof; C1, J1. |
| F-2 | Name reused for a new volume; wrong daemon/context; same-second creation | Full incarnation tuple, daemon identity, supported-writer locks, reinspection; J3, N2. Privileged bypass boundary above. |
| F-3 | Wrong operation/repo/origin or incomplete response reused | Nonces, exact roster digests/counts and typed coverage; J2/J3, H4. |
| F-4 | Old/future-dated preview or remote proof replayed | Upper/lower age bounds, current host time and phase binding; J4, H4. |
| F-5 | New commits/dirty bytes between preview and apply | Full fresh scan and comparison, not an old bool or HEAD-only hash; J5, N2. |
| F-6 | Worktree/ref changes during final scan/delete | Exclude supported writers, snapshot observations before/after, fail on drift; J7/N2. Out-of-protocol writes are not solved by read-only mounts. |
| F-7 | Only visible branches checked; reflog/stash/other refs/pseudorefs missed | Preserve entire existing location closure; C3, R3/R4. |
| F-8 | Annotated tag object lost because peeled commit is public | Record/prove object and peeled target separately; C3, H1. |
| F-9 | Bare HEAD, orphan admin, missing checkout, tmp or nested ignored repo missed | Complete namespace/admin inventory and independent role scan; C4/C6/C10, R4. |
| F-10 | Git failure, malformed output, empty list, truncated stream or permission error becomes zero | Typed command outcomes and complete-enumeration receipts; C7, H3. |
| F-11 | Stale tracking refs or scratch polluted by local unpublished objects prove publication | Fresh advertised snapshot and origin-only scratch graph; H1/H3. |
| F-12 | Missing objects hidden by commit graph/promisor behavior or index flags | Preserve object traversal and raw content checks, no lazy fetch; C3/C4, R3. |
| F-13 | Git config/hook/filter/origin injects code or redirects host credentials | Sanitized offline Git plus trusted-origin binding and isolated host repo; C9, H5. |
| F-14 | Symlink/hardlink/path escape hides Git/evidence/state or receipt content | Closed filesystem shapes, no-follow traversal, confinement and receipt custody; C5/C7, J2. |
| F-15 | Work is absent so state/tmp/dind are silently deemed disposable | Independent role inventory and generated-layout proof; C6/C10. |
| F-16 | Credentials or unknown persistent inner Docker volumes are discarded | Names-only state gate; offline inner-store classifier; ambiguity refuses; C2/C6. |
| F-17 | Audit writes to aliases, wrong UID/path mapping, network has credentials | Kernel read-only mounts, no alternate writable aliases in helper, nonroot/offline flags and real write probes; C8/C9, N1. |
| F-18 | Attestation/forged PASS/old schema bypasses machine proof | One gate, protected host record, manual authority and digest binding; J1/J2/J9/J10, W2. |
| F-19 | Exited/foreign container, bind alias, running work or bound land races deletion | Full live census and alias checks immediately before each rm; J6/J7, R1/R2, N2. |
| F-20 | Compose down/prune/wildcard removes an unlisted cache or main volume | Exact allowlist, no force, all-set preflight and per-effect intent; J6/J8, N3. |
| F-21 | Crash/retry guesses results or removes replacement after partial delete | Intent/result state machine and reproof of remaining set; J8, N3. |
| F-22 | Helper survives cancellation and retains access, or cleanup kills another helper | Owned ID+nonce+label, bounded termination/join, no prefix cleanup; H6, R1, N1. |
| F-23 | Current tool/schema mismatch accidentally accepts old semantics | Protocol/policy negotiation and current-tool integrity, no unknown-schema success; C1/J4/W3. |
| F-24 | Tests manufacture a receipt or fake success without running the real gate | Red at base for observable behavior, real Git witnesses and separate real Docker cases; CP-1/2 and N1-N3. |

Other risks: a fresh full re-audit costs time; use shared per-origin graph and
measure phases, never weaken content checks for speed. Host-origin credentials
can be unavailable; this is a current actionable refusal. Names-only policy cannot
identify a secret renamed as an ordinary log; retain the stated policy boundary.
A root-owned nested Docker layout may initially be unsupported, requiring a
classifier improvement rather than manual force. Historical prototype replay
cannot certify already deleted data. None of these are a reason to manufacture
passing evidence.

## Implementation slices

Commit failing tests before their fixes; commit and push every slice. No editing
while a checkpoint is in flight. Paths below are the planned Code scope, not edits
made by this Plan task.

| Slice | Files and concrete change | Tests / closure |
|---|---|---|
| S1: regression witnesses | Add `tests/Antiphon.Tests/Scripts/VolumeRetirementContentTests.cs`, `VolumeRetirementReceiptTests.cs` and a shared `VolumeRetirementFixture.cs`; extend `scripts/fixtures/c1008-fake-docker.sh` only for observations/fault cuts. Drive current entrypoints so no missing-method/unknown-argument error can masquerade as red. | C1/C2/J1 fail on intended readiness/no-deletion assertions against unchanged production at the task base; CP-1/2. |
| S2: inventory and host proof | Keep `c1008_git_program` as the v1-compatible extraction boundary in `scripts/c590-remote.sh`; extract/add `scripts/c1008-content-audit.sh` for the v2 inventory protocol. Refactor the landed `scripts/c1008-host-proof.ps1` at the baseline above, preserving sanitization but adding full origin ancestry/coverage/age. Add the role metadata walker in `scripts/c1008-volume-metadata.sh`. Adapt `scripts/fixtures/c1105-git-audit.cjs` to provide equivalent proof input to v2 without changing its expected classifications. | Finish C1-C11, H1-H6 and R3-R6; D-15 full-roster output. |
| S3: receipt and delete coordinator | Add `scripts/c1008-retirement.sh`; wire the temp-only branch of `scripts/c590-remote.sh`. Implement strict schema, custody, locks, current runtime resolution, all-set proof, exact deletes and partial resume. Change C994's temp retirement preflight to actual topology; bypass old image/Compose/stack requirements. Keep main v1 adapter unchanged. Update legacy tests' provenance expectations. | J1-J10, R1/R2 and N2/N3. |
| S4: wrapper and manual entry | Add `scripts/retire-runner-volumes.ps1`; update `scripts/deploy-server2.ps1` and `scripts/c590-real.ps1` for versioned typed transport, operation-scoped script staging, host proof exchange, auto-preview/apply and explicit attestation. Update current audit descriptor during supported host tool installation; do not activate or deploy during Code tests. | Add `VolumeRetirementHostProofTests.cs` and `VolumeRetirementWrapperTests.cs`; H1-H6/W1-W5. ASCII transport scripts; LF shell files. |
| S5: native qualification and docs | Add `VolumeRetirementDockerTests.cs` and `scripts/fixtures/c1609-retirement-real-cases.mjs` using the existing owned-daemon fixture infrastructure. Update `docs/docker-stack.md` retirement/recycling sections and `docs/orchestration-loop.md` pinned-audit/manual-authority wording together. Keep AGENTS as index unless an actual obsolete mandatory statement needs correction. | N1-N3, exact final CP-3..11; update runbook command examples and refusal recovery. |

Relocate the four old historical-image-specific methods in
`RollingVolumeRecycleScriptTests` (dry-run binding and three C1594 receipt-image
methods) into C1/J9's matrices. Keep every dirty/unpublished/unknown/namespace/
helper-custody vector; change only obsolete historical equality expectations.
Retain `C1594_Antiphon_evidence_gitdir` unchanged. In
`RetiredTempContainerHostTests`, replace the two historical-image methods with
`C1609_Cleanup_does_not_require_historical_image` and
`C1609_Audit_uses_current_image`; retain topology/ownership counterexamples.
`C1105_Temp_cleanup_uses_previous_generation` now proves older actual topology is
recognized without loading its old build. A changed test name is not coverage loss
when its behavioral vectors and witnesses remain explicit.

CARD-1607 test disposition (S2, preservation R6 in CP-8): retain both existing
method names so their vectors remain traceable. Rewrite
`C1008_Host_proof_admits_only_an_authenticated_private_origin` for v2 temp:
retain authenticated success, unpublished-child refusal, missing/wrong/old/malformed
proof, non-auth errors and no-credential/helper-custody assertions; replace the
"only after classified auth failure" expectation with proof for every origin.
Keep a v1-main adapter vector for the existing fallback. H2/H3/H4 extend those
vectors, not substitute for them. Rewrite
`C1008_Host_proof_file_omits_credentials` to retain artifact/log/argv sentinel
assertions: v2 rejects credential-bearing URLs before invocation rather than
silently sanitizing one into authority; a trusted clean origin with ambient fixture
credentials proves success without exporting secrets. Keep v1 scrubbing coverage
in its main adapter; H5/H6 add v2 protocol/deadline cases. Neither test is deleted.

## Performance budget

This Plan runs only the checkpoint importer (leased tool bootstrap if needed), not product tests or live audits. Apply
`docs/testing-and-build.md#performance-standards` and its owner. The 5225-second
800-repository audit and 1404-second OLD replay at load 17 are documented production
samples (CARD-1316), not a measured p95 for the new design. The existing real-Docker
comparison measured 618/709/827 seconds (CARD-1142) and is deliberately not selected;
N1-N3 use a smaller fixture. Older comparable Linux test builds in the inspected
runner-lease plan took 215-261 seconds; another CARD-1540 build took 519 seconds.
Those are load-sensitive historical samples, not a claimed current p95.

**Provisional collection obligation: CARD-1609, with CARD-1404/1514/1531 owning
measurement infrastructure.** Expected totals below are planning estimates. Record
at most five comparable samples per new method/phase from normal red/green/review
execution; do not rerun whole green selections to fill a sample. Until at least
three comparable samples exist, label p95 unknown/provisional. Set final test and
row limits near rounded 2x measured p95; preserve slower/failed samples and their
host/load/source/run IDs. A timeout is a diagnostic result, not permission to
increase it or retry unleased. Any expected/observed row, batch or complete run
over 30 minutes requires a Debug task with the timing evidence before repetition.

| Phase | Expected cost / initial collection cap | Setup and measurement |
|---|---|---|
| Build | About 4 minutes per isolated build; provisional 10-minute cap | Three builds total: red Linux, final Linux, native Windows. Tool bootstrap if absent is separately declared and leased, never hidden. |
| Portable fixtures | Usually 1-5 s/test, matrices 10-30 s; provisional per-test 60 s | Immutable tiny public/private-origin Git seeds once per run; clone/copy per-test mutable state. No real network/login. |
| Existing Git closure rows | CP-5 about 1 min, CP-6 about 3 min, CP-7 about 1.5 min | Reuse existing tiny-Git fixtures, share seeds where isolated copies preserve semantics. Measure actual per-argument execution, not internal assertions. |
| Native Docker start/readiness | About 30-60 s per native run; provisional 120 s | One private daemon and current fixture image per run, never production Docker/runner. |
| Native data preparation/clone | About 15-30 s/run; provisional 60 s | Separate exact volumes per test, small shared origin seed, no provider/AppHost/database setup. |
| Native body | About 30-90 s/test; provisional 180 s/test | N1-N3 share daemon/image; fresh lifecycle objects are the subject under test. |
| Teardown | About 10-30 s/run; provisional 60 s | Join owned helpers/transports, prove only owned volumes/daemon removed. |
| HTTP/CLI/IO boundaries in tests | Expected subsecond/few seconds; injected deadlines use fake clocks/barriers | Measure start, exit, stdout/stderr drain and cleanup separately; no arbitrary real-time sleeps. |
| Queue admission | Existing 45-minute build-slot allowance, distinct from execution | Exit 4 is not run/blocked. No unleased fallback. Hard wall is charged execution plus finite admission allowance. |

No web host, DB container, migration, browser or provider session is needed; those
phases are not applicable. Uninstrumented phases are unknown, remainder explicit.
Category: runner maintenance / Git operations. Antiphon.Tests directly references
Server, SessionRunner and DockerStack.Fixture (and the existing helper/tool graph);
source changes are scripts and their consuming tests, with no runtime API/schema
migration or client change. Record evaluated project/downstream graph in the
checkpoint receipt; these rows give ordinary change verification, not rc Full
coverage. Unknown graph input blocks scoped release credit.

Retain TRX, counts/rosters, phase JSONL, external-call spans, build/admission waits,
source/build provenance, platform/load and timeout derivations under ignored
`.antiphon/checkpoints/<run-id>/` and fixture evidence roots. Do not commit payloads.
Use assembly-local `ParallelLimiter<ProcessSpawnLimit>` for every process-spawning
method; native rows are serial. Do not co-schedule Pty/FakeClaude assemblies.

Production initial bounds remain separate: preserve the 10800 s existing content
scan cap while collecting new phase timings (a CARD-1316 legacy exception, not a
new p95 claim); host ls-remote 30 s, each proof fetch/graph phase 300 s within a
600 s phase-wide network allowance, transport idle/response waits bounded to that
allowance, owned-child teardown 10 s, host lock acquisition 60 s. An EOF or total
budget exhaustion refuses and joins, never extends itself through retries.
New role walks share the content-scan cap. Re-observation/final live guards have
individually bounded calls; no new shorter wrapper deadline silently kills a valid
scan. Reassess production p95 after first representative deployments, not using
small fixture timing. No test timeout is justified by the 3-hour production cap.

## Verification design

All named new methods are one TUnit execution each; vectors inside them are
assertion cases, not Min counts. Tests exercise production entrypoints and receipt
parsers, not self-comparisons or string-only claims. Every negative matrix has a
passing clean control and asserts no volume effects. Baseline tests run with only
S1 tests/fixture observation support added to the recorded base, before fixes.
Inherited failures are rerun only at that base with the exact failing selection.

### Witness map

C = `VolumeRetirementContentTests`, J = `VolumeRetirementReceiptTests`,
H = `VolumeRetirementHostProofTests`, W = `VolumeRetirementWrapperTests`,
N = `VolumeRetirementDockerTests`. Exact proposed method names and obligations:

| ID | Method | Design property and red/control |
|---|---|---|
| C1 | `Current_tooling_and_missing_history_do_not_block` | D-3/4/9: no receipt, missing old image/SHA/Compose/stack and conflicting old provenance all succeed with current tools and a real Ready proof. Red at base: pending/old binding instead of Ready. Also current runtime missing/unsupported refuses. |
| C2 | `State_credentials_refuse_before_removal` | D-5/12: auth/key/SSH/token/cert names, unreadable state and ambiguous process key refuse with zero removes; clean state control passes. Red at base: work-only audit permits removal. |
| C3 | `Every_ref_recovery_tip_and_tag_object_requires_publication` | D-2/6: private branch, annotated tag object with public peeled commit, reflog-only/stash/pseudoref/tracking/private linked refs refuse; exact/public controls pass. PC: omit each location or peel original tag. |
| C4 | `Worktree_content_and_admin_closure_are_complete` | Dirty/staged/untracked, index flags, orphan admin/prunable checkout and linked /tmp handling; in-progress operations refuse. PC: skip admin/index check. |
| C5 | `Evidence_names_and_filesystem_shapes_fail_closed` | Full name roster, nested names, links/hardlinks/special types, valid harmless files; PC: skip top-level evidence walker. |
| C6 | `All_target_roles_are_classified` | Work/tmp/state/dind coverage; unknown inner persistent stores/root permissions refuse without chmod; clean recognized cache control. PC: treat every non-work role as safe. |
| C7 | `Read_failures_and_incomplete_inventory_refuse` | Permission/parse/truncation/empty-success/missing count errors never produce Ready. PC: swallow an inventory exit. |
| C8 | `Audit_identity_and_namespace_are_stable` | Current digest resolution, UID, offline flags, /work and /tmp, helper scratch and mounts; PC: move /work or drop readonly. N1 supplies kernel evidence. |
| C9 | `No_content_or_credentials_are_executed_or_exported` | Repo hook/fsmonitor/filter/config and credential sentinel data cannot execute/leak; host scanner records names only. PC: allow hook or raw stderr. |
| C10 | `Absent_work_does_not_skip_other_volumes` | Work absent plus unsafe state/tmp/dind still refuses; all absent is a typed no-op, creates nothing. PC: early return from absent work. |
| C11 | `Preview_lists_every_offender_without_effects` | Two repos across work and tmp with distinct unpublished/reflog/FETCH_HEAD commits plus dirty/evidence/state findings: assert every exact location/kind/OID in JSONL and rendered proof, no deletes. An unreadable repo still lists other repos, marks unvisited scope and complete=false. PC: restore first-finding exit. |
| J1 | `Preview_receipt_is_required_and_consumed` | D-7/8: direct low-level apply without Ready refuses; wrapper auto-preview uses exact durable proof once. Red at base: low-level apply deletes despite absent authorizing preview. |
| J2 | `Receipt_shape_custody_and_digest_are_strict` | Invalid/duplicate fields, foreign owner/links/writable ancestor/truncated files/digest mismatch/failed flush refuse. PC: trust copied PASS or skip custody. |
| J3 | `Volume_incarnation_and_operation_are_bound` | Alter name/CreatedAt/daemon/inode/role/labels/op/nonce individually; each refuses. PC: compare name only. |
| J4 | `Freshness_and_policy_version_are_rechecked` | Expired/future proof, compatible current-build change, incompatible policy, long apply after valid admission; fake clock. PC: remove upper age check. |
| J5 | `Changed_content_and_remote_proof_refuse` | New private/public ref, dirty bytes after preview and remote snapshot loss; reprove permissible remote advancement. PC: reuse preview publication. |
| J6 | `Every_delete_rechecks_full_live_guards` | New running/exited refs, bind aliases, busy/routed/bound land or changed retirement between deletes; exact-name-only effects. PC: reuse initial census. |
| J7 | `Lock_order_excludes_supported_writers` | Real flock barriers: concurrent create/recycle/resume serializes; cache-only path cannot invert; timeout no mutation. PC: skip rollout lock. |
| J8 | `Journal_intents_and_partial_resume_are_exact` | Every persistence/delete cut, no-intent absence, partial success, replacement, remaining-role dependencies and copy failure. PC: infer deleted from missing file/error. |
| J9 | `Legacy_receipts_are_history_only` | All v1/missing/conflicting/foreign receipts preserve history but require fresh v2 proof; no schema-upgrade-to-PASS. PC: import old success as Ready. |
| J10 | `Manual_attestation_is_exact_and_cannot_waive_findings` | Correct human/delegate authority plus exact receipt permits same proof; wrong/missing attestation or any unsafe finding refuses. PC: manual flag bypasses audit. |
| H1 | `Host_proves_complete_origin_ancestry` | Same-ref equality, advanced-origin ancestry, tags/tree/blob semantics and origin graph absence; use real isolated Git. PC: accept OID presence without reachability. |
| H2 | `Private_and_public_origins_share_offline_gate` | Both origins use host proof with helper network none; no credential mount/env; private success fixture and unavailable credential refusal. PC: proof only for exit 128. |
| H3 | `Unknown_remote_and_object_failures_refuse` | Non-auth 128, nonzero ls-remote/fetch, malformed/empty advertisement, disappearing refs/objects and bounded churn. PC: fallback to local remotes. |
| H4 | `Proof_coverage_binding_and_freshness_are_exact` | Missing/duplicate/extra/wrong-origin/object/nonce/digest/timestamp row refuses, including multi-repo origin reuse. PC: subset proof treated complete. |
| H5 | `Origins_and_git_configuration_cannot_redirect_credentials` | Unapproved origin/protocol, userinfo, local config/redirect/helper injection refuse before credential invocation. PC: use inventory URL directly. |
| H6 | `Child_deadline_and_output_redaction_are_bounded` | Timeout, pipe holder, process-tree death, large stream and sentinel errors; no leaked auth/URLs/raw stderr. PC: child survives deadline or output copied verbatim. |
| W1 | `Retire_temp_uses_current_proof_protocol` | Auto-preview/apply, explicit receipt, DryRun, all phase and absent no-op entry behavior. |
| W2 | `Manual_parameters_require_operator_attestation` | Parameter sets, exact names/authority/digest, no force bypass; generated template is not accepted as signed. |
| W3 | `Bridge_validates_and_preserves_receipt_fields` | Strict JSON/quoting, multiline/path characters, large proof by file, per-operation staging, ignored evidence and ASCII/LF scripts. |
| W4 | `Retire_temp_ignores_missing_stack_and_historical_compose` | Entry-level no old stack/source/image requirement, including absent/exited containers and old callers with -Sha. |
| W5 | `Main_recycle_and_existing_deploy_contracts_are_preserved` | Non-retirement -Sha requirement, main generation/resume behavior and existing authority gates stay enforced. |
| N1 | `Read_only_namespace_and_host_proof_are_real` | Real helper kernel write-denial at each mounted alias, UID/offline/no secrets, linked pointers and hooks, before/after bytes; timeout kills only owned helper. |
| N2 | `Replacement_and_concurrent_reference_block_delete` | Real same-name replacement between phases, new named/bind reference, supported writer blocked by lock; no wrong volume deleted. |
| N3 | `Exact_deletion_and_crash_resume_preserve_other_volumes` | Real all-safe set removed by exact name; main/cache/sentinel survive; crash cuts resume without duplicate/replacement deletion. |

Preservation rows: R1 = seven original named RollingVolumeRecycleScriptTests methods in
CP-8; R6 = the two CARD-1607 methods in CP-8; R2 = nine named retired-container methods in CP-9; R3 = 30 argument results
from four Git recovery/seed/index/object methods in CP-5; R4 = the existing 132
hidden-location results in CP-6; R5 = 40 evidence-name plus 12 mirror-owner results
in CP-7. R3-R5 already pass for protected behavior at base; do not fabricate red
at base. Their regression strength comes from retained assertions and the named
method-scoped positive controls. New C3-J10/H/N controls run later in the separate
Mutation stage, scoped to exact methods on a bound pack, never a whole class.

### Lanes, roster and execution

Read `GET /api/runner-defaults` and `GET /api/session-runners` again at dispatch.
They were read again on 2026-10-30: revision 2; an eligible Linux default
and an eligible Windows peer were advertised (8/10 and 2/2 occupied respectively). These are
observations, not permanent placement instructions. CP-1..10 require the native
Linux maintenance lane (CP-4's portable logic is also exercised there); CP-11
requires the native Windows PowerShell/Git lane. No host is pinned. Omit `-Runner`;
use `-Platform Linux`/`Windows` only for the rows needing that OS. Plan/Review is
portable and needs no platform flag (`Any` clears a previous pin).

Suffix-collision audit at base: every selected existing class/method operand below
has exactly the intended declaration; hidden locations were counted from the
fixture's delimited roster (132), evidence arguments from attributes (40), and
mirror-owner attributes (12). New class names are absent at base and reserved by
this plan. Code must recheck after adding them; TUnit TRX must match all intended
names/arguments and no suffix sibling. No full-path OR. Every OR operand has
parentheses and a trailing star. Table pipes are escaped. No zero-test credit.

Use the checkpoint tool's `run --plan <this-file> --rows <row-list>` once per
committed group: S1 CP-1/2; S2-S5 Linux CP-3..10; S2-S5 Windows CP-11. Run bootstrap
through `scripts/build-slot.ps1` if needed; the checkpoint runner takes build slots
for its rows. Continue `wait` until terminal exit (75 is running), in foreground
wait windows no longer than 60 seconds. Preserve exact CHECKPOINT lines with source
and build provenance; the S1 rows are intentional assertion-red evidence, not green.
Remove only task-owned alternate outputs after all runs have joined. CP-11 intentionally builds on the native Windows lane; an importer duplicate-build warning is expected because cross-host CP-3 output cannot be reused.

Import the manifest to ignored YAML to set per-row deadlines explicitly rather
than inherit the importer's 15-minute/3x floor: provisional execution caps CP-1=10m,
CP-2=2m, CP-3=10m, CP-4=2m, CP-5=2m, CP-6=6m, CP-7=4m, CP-8=2m, CP-9=2m,
CP-10=8m, CP-11=10m. These are roughly 2x the stated estimates, not measured-p95
claims. Red-group charged cap 12m, final Linux 29m, Windows 10m; each hard wall
adds one finite 45m admission allowance. Reconcile with measured p95 under the
collection obligation before final qualification. If a group reaches 30m, stop
repeating it and hand timing evidence to Debug. No full assembly run is authorized.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1609-red/` | linux-content-red | `/*/Antiphon.Tests.Scripts/VolumeRetirementContentTests/(Current_tooling_and_missing_history_do_not_block*)\|(State_credentials_refuse_before_removal*)` | C1,C2 baseline | 2 executed, 2 intended assertion failures, no fixture/build errors | 2 | 5 | true |
| CP-2 | S1 | CP-1 | linux-receipt-red | `/*/Antiphon.Tests.Scripts/VolumeRetirementReceiptTests/Preview_receipt_is_required_and_consumed` | J1 baseline | 1 executed, intended no-deletion assertion failure | 1 | 1 | true |
| CP-3 | S2-S5 | `tests/Antiphon.Tests -> bin-c1609-final/` | linux-content-receipt | `/*/Antiphon.Tests.Scripts/(VolumeRetirementContentTests*)\|(VolumeRetirementReceiptTests*)/*` | C1-C11,J1-J10 | all 21 listed methods, 0 failed/skipped | 21 | 5 | true |
| CP-4 | S2-S5 | CP-3 | linux-host-wrapper | `/*/Antiphon.Tests.Scripts/(VolumeRetirementHostProofTests*)\|(VolumeRetirementWrapperTests*)/*` | H1-H6,W1-W5 | all 11 listed methods, 0 failed/skipped | 11 | 1 | true |
| CP-5 | S2-S5 | CP-3 | linux-git-core | `/*/Antiphon.Tests.Scripts/RemoteScriptContractTests/(C1105_Git_audit_recovery_tips*)\|(C1105_Git_audit_seed_content*)\|(C1105_Git_audit_index_content*)\|(C1105_Git_audit_object_completeness*)` | R3 | all 30 argument results, 0 failed/skipped | 30 | 1 | true |
| CP-6 | S2-S5 | CP-3 | linux-git-locations | `/*/Antiphon.Tests.Scripts/RemoteScriptContractTests/C1105_Git_audit_hidden_locations` | R4 | all 132 location results, 0 failed/skipped | 132 | 3 | true |
| CP-7 | S2-S5 | CP-3 | linux-evidence-shapes | `/*/Antiphon.Tests.Scripts/(RollingVolumeRecycleScriptTests*)\|(RemoteScriptContractTests*)/(C1594_Antiphon_evidence_gitdir*)\|(C1335_Mirror_owner_stamp_in_linked_admin*)` | R5 | all 52 argument results, 0 failed/skipped | 52 | 2 | true |
| CP-8 | S2-S5 | CP-3 | linux-recycle-guards | `/*/Antiphon.Tests.Scripts/RollingVolumeRecycleScriptTests/(C1008_Same_sha_and_partial_retries_are_safe*)\|(C1008_Option_manifest_is_strict*)\|(C1008_Wrapper_option_manifest_is_strict*)\|(C1008_Present_or_unknown_temp_keeps_null_refusal*)\|(C1008_Busy_routed_and_land_in_flight_refuse*)\|(C1008_Preview_helper_removal_is_owned*)\|(C1008_Wrapper_retired_absent_null_is_accepted*)\|(C1008_Host_proof_admits_only_an_authenticated_private_origin*)\|(C1008_Host_proof_file_omits_credentials*)` | R1,R6 | all 9 methods, 0 failed/skipped | 9 | 1 | true |
| CP-9 | S2-S5 | CP-3 | linux-container-guards | `/*/Antiphon.Tests.Scripts/RetiredTempContainerHostTests/(C994_Absent_cleanup_is_idempotent*)\|(C994_Exited_owned_containers_are_removed_without_volumes*)\|(C994_Whole_candidate_set_precedes_removal*)\|(C994_Each_removal_rechecks_live_proofs*)\|(C994_Receipt_write_and_copy_fail_closed*)\|(C994_Host_context_is_strict*)\|(C1609_Cleanup_does_not_require_historical_image*)\|(C1609_Audit_uses_current_image*)\|(C1105_Temp_cleanup_uses_previous_generation*)` | R2 | all 9 methods, 0 failed/skipped | 9 | 1 | true |
| CP-10 | S2-S5 | CP-3 | linux-real-docker | `/*/Antiphon.Tests.Scripts/VolumeRetirementDockerTests/*` | N1-N3 | all 3 methods and owned cleanup, 0 failed/skipped | 3 | 4 | true |
| CP-11 | S2-S5 | `tests/Antiphon.Tests -> bin-c1609-windows/` | windows-host-wrapper | `/*/Antiphon.Tests.Scripts/(VolumeRetirementHostProofTests*)\|(VolumeRetirementWrapperTests*)/*` | H1-H6,W1-W5 native Windows | all 11 methods, 0 failed/skipped | 11 | 5 | true |

## Rollout and compatibility

1. Independent design review first. Validate the fail-open/lock boundary, schema,
   generated-data classifier and attestation defaults. No production volume audit
   or deletion is part of this Plan or its Code fixture tests.
2. Implement the versioned scanner/proof/coordinator with the tests committed red
   first. Extend the landed CARD-1607 helpers deliberately, retaining current sanitization. Keep v1 main
   recycle behavior. Complete both native lanes before release credit.
3. Publish current compatible tooling and the audit descriptor through the normal
   reviewed deployment path. Observe installed runtime/protocol directly. A code
   land does not activate host scripts or an image. No runner restart is inherently
   required for this maintenance-only path.
4. First preview on a deliberately retired, known test volume set and retained
   mixed-history fixtures. Record complete refusals as well as successes. The
   original four prototype volumes are gone and cannot supply a live replay.
5. Enable current-proof `retire-temp` and the explicit manual entry. Preserve
   historical receipts unchanged, generated receipts ignored and main/cache
   volumes untouched. Existing callers with a creating `-Sha` continue to work;
   retirement treats it as optional provenance, not a lookup requirement.
6. Rollback before apply can switch current tooling to a reviewed compatible
   version and repeat preview. After any delete, rollback means reporting the
   irreversible partial outcome and preserving the rest; never recreate empty
   volumes or silently fall back to the old broad Compose deletion path.

## Operator questions and independent review gates

The plan is written under D-1..D-15 rather than waiting for answers. These are
explicit review choices, not permission questions during this Plan:

- Confirm D-2 retains reflog and original tag-object protection despite the manual
  prototype's weaker proof. Default: retain; abandoning unpublished reflog work
  or changing publication semantics needs a separate operator decision/card.
- Confirm D-12's ambiguous `.key` refusal and the exact supported nested-Docker
  cache layouts. Default: refuse unknown/ambiguous; no automatic prototype exception.
- Confirm D-7's 15-minute preview/origin freshness and 60-second future-skew bound.
  These are safety-policy defaults, separate from measured execution deadlines.
- Confirm D-10 manual attestation is by the operator or a specifically authorized
  delegate, using existing trusted host/operator credential custody. A free-text
  name is an audit statement, not cryptographic identity proof.
- Confirm the deletion host excludes privileged out-of-protocol volume writers
  during apply. If it cannot, require enforceable exclusive daemon maintenance
  before implementation can claim same-name replacement safety; Docker inspect
  plus rm alone cannot close that race.

Review should distinguish missing historical evidence (never a blocker by itself)
from missing current safety proof (always a blocker). If it changes any default,
update this plan and the witness roster together before Code. Performance p95 is
explicitly pending measurement; no fabricated timing or test success is claimed.
