# CARD-1609: content-derived volume retirement

Round-3 Plan task `ccd2fcdc`, branch `feat/card-task-ccd2fcdc`, fast-forward
from `286fe7196c717d2c5eb1f5bf2c4e8267bd7c36a2` (round-2 plan and re-review).
This task revises only the plan; it grants no authority to retire live volumes.
Verification design is folded into this plan. Final plan-cap round; next: Code
from then-current `origin/master`, with the rc port after top-up 38.

## Round-3 revision ledger

Review source: `.antiphon/task-1f7eb8e4.md`, read in full on 2026-10-30.
The skeleton records the remaining work before its individually pushed fixes.
The round-2 findings and validation below remain historical evidence.

| Review defect | Required revision | Status |
|---|---|---|
| D-1 | Read-only wrapper preflight before retirement clear; exact blocking journal states and audited closure; W6 ordering witness | Done: D-20, S3/S4, J8/W6 |
| D-2 | Every affected existing test disposition and checkpoint; reconcile counts and import | Done: 16 rows imported; 360 executions reconcile; final validation below |
| D-3 | Reciprocal CARD-1609 before CARD-1615 order and source/docs overlap | Done: D-21 and master/rc order; current 1615 D-11 checked |
| D-4 | Exact seed commit object bytes, no extra headers; sixth adversarial repo witness | Done: D-17 exact raw bytes, C13/PC-SeedHeaders |
| A-1 | First live preview measures unclassified crashed-test repos before apply is enabled | Done: rollout step 4 is preview-only with measured refusal prevalence |

## Round-2 revision ledger

Skeleton published first; every review defect below has its own pushed amendment.
Review source: `.antiphon/task-3372044d.md`, read in full on 2026-10-30.

| Review defect | Revision obligation | Status |
|---|---|---|
| D-1 | Whole-minute checkpoint estimates; import verification recorded below | Done: original 11 and final 14 rows import successfully |
| D-2 | Landed CARD-1607 baseline and explicit fate of its two tests | Done: baseline and preservation R6 |
| D-3 | Complete read-only offender preview, human and machine forms | Done: D-15, C11 |
| D-4 | FETCH_HEAD, narrow fixture classifier, gone-tracking decisions | Done: D-16..18, C12..14, operator choices |
| D-5 | Bounded census retries, durable partial/resume behavior | Done: D-19, J6/J8 |
| D-6 | Retained-volume deploy refusal, C849 contract, external caches | Done: D-20, W6 |
| D-7 | Master/rc port order after top-up 38, definition scope | Done: port and ordering section |
| D-8 | Windows skip roster and closure checkpoint | Done: slice edits and CP-12 |
| D-9 | Documentation pins and checkpoint | Done: S5 pin rewrites, CP-13 and CP-8 |
| D-10 | Real private-origin path baseline-red witness | Done: H7 real-path baseline, CP-14 red / CP-3 green |

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
| The no-remote seed can be recognized by tree and identity fields alone | `ReportBudgetWorkspace.TemplateAsync` writes the two exact files and invokes `git commit -m seed`; Git commit objects can also contain extra headers, and this invocation does not explicitly turn ambient signing/encoding off. | D-17 compares the entire raw commit byte shape and intentionally refuses extra headers, including fixture-produced ones. |
| Repeated census reads are stable | PowerShell retries Drift only; Unstable throws. Shell also permits final Drift after journaled container removal. | D-19 defines bounded v2 retry without accepting incomplete proof; keep v1 adapter explicit. |
| Container absence proves no access | `c1008_references` additionally detects bind sources overlapping a volume mount; named-volume queries alone miss them. | Preserve all-container census and canonical alias checks. |
| Existing receipts bind an immutable Docker ID | `c1008_volume` has Name/CreatedAt/Mountpoint/selected labels/driver/options; named local volumes have no container-style immutable Id in this implementation. | Bind daemon identity and the whole incarnation tuple; never substitute name alone. |
| Final removal is already exact-name-only | Temp uses `compose_temp down -v`, then verifies target absence; main uses exact volume rm. | Temp retirement switches to an exact recorded allowlist, no Compose deletion. |
| No temp containers means deploy-temp starts clean | Landed `case_deploy_temp_runner` checks only `c849_no_temp_containers`; Compose can reuse retained named volumes. C849 Retired requires zero private volumes. | D-20 blocks deploy until retirement completes; C849 zero stays mandatory. |
| A host deploy check can protect the retirement epoch | `deploy-server2.ps1:1196` POSTs `/drain/clear` before the host cases at 1214-1215; `Assert-TempProjectAbsent` checks containers only. `c1008_status_proof` binds both retiredAt and retireWhenIdle. | Wrapper read-only preflight must refuse before either drain POST or any mutating host case, preserving resumability. |
| Every Refused journal must block deployment forever | v2 is new; a preview writes Pending/Refused even when no target effect occurs. There is no existing v2 closure command. | D-20 distinguishes inert preview records from effects and defines audited, observation-only closure. |
| Historical Compose is necessary when temp is absent | Both C994 cleanup and recycle materialize models before absence is fully handled; generation binding can inspect old source. | An absent generation needs no old model. Exited-container ownership comes from fresh actual topology. |
| A checksum proves a receipt came from the operator | Current JSON/hashes are files under trusted host custody; a copied self-authored hash is not authentication. | Apply accepts only its protected host journal, not arbitrary supplied PASS JSON. |
| Manual proof is already a reusable receipt schema | Prototype lacks schema, operation/volume incarnation, expiry and durable intents; publication rows omit reflog objects and original tag objects. | Preserve as historical evidence; no automatic conversion to Ready. |

## Decisions

D-1 through D-21 are the stated defaults for Code and subsequent Review. They make the design concrete;
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
in both preview and apply with read-only volume mounts, `volume-nocopy` on
every named-volume mount (including work and /tmp), `--network none`,
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
| runner-tmp | Audit registered checkouts in their real namespace; enumerate the rest so an unregistered repository cannot hide as scratch. Known ephemeral runtime/build/log artifacts remain disposable; only D-17's exact no-remote seed classifier exempts Git-shaped fixtures; all other unknown Git state refuses. |
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
| Deploy admission / Close | D-20 phase and active-owner/child facts, all container/volume intents/results, current absence and journal census. Close appends prior digest, actor/authority/reason/UTC, observed reconciliations and terminal Closed digest; never a Ready capability. |

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
   revalidate that incarnation, full live guards with D-19 bounded read retries, and all remaining references.
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
`scripts/retire-runner-volumes.ps1` with mutually exclusive Preview, Apply and Close
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

**D-16 — Stale FETCH_HEAD is a fact to classify, not an automatic refusal.**
Parse every entry (including not-for-merge lines) under sanitized offline Git.
An entry naming a commit is harmless only when the fresh host origin graph proves
that commit and its retained ancestry published under D-2; age, branch deletion
or the fact that a fetch wrote it is not proof. For a lazy-fetch blob/tree/tag,
retain exact-object/reachable-tree rules from D-2; require published object evidence
and no unpublished retained commits. Such a proved entry is listed as
published-fetch-head, ignored as a blocker, and never deleted/rewritten. An
unpublished commit/object is a listed refusal; malformed/unreadable/missing objects
are unknown. Test mixed multi-entry files so one public row cannot mask another.
Rejected: ignoring all FETCH_HEAD, deleting it during preview, or treating a
non-commit OID as vacuous publication. Operator recovery: publish retained commits
to an origin branch/archive branch that satisfies D-2, or explicitly recover/remove
the stale pointer outside this command, then preview again.

**D-17 — Recognized no-remote seed fixtures are disposable by exact content.**
The landed `ReportBudgetWorkspace.cs` creates
`<checkout>/.antiphon/test-output/card-1605/template` with no remote, one root
commit, author/committer `Fixture <fixture@example.invalid>`, message `seed`,
`seed.txt` bytes `benign fixture`, and `.gitignore` bytes `.antiphon/\n`.
The path and identity alone are forgeable and MUST NOT grant disposal. The v2
trusted classifier catalog (current tooling, never a file supplied by the volume)
initially permits only that template shape within an independently inventoried
outer checkout in work or runner-tmp. Match complete relative path components,
no symlink/hardlink traversal, expected UID/regular-file shapes, no remotes or
external includes/alternates/hooks/custom filters, and a complete readable object
closure. HEAD/main and every retained reflog side must identify the same sole
parentless seed commit; reject all other refs/tags/stash/pseudorefs, linked admins,
operations or extra commits, including commits with the same fixture author.
Validate the **whole raw commit object**, not a parsed subset of its headers.
Catalog v1 permits SHA-1 repositories only; another object format is unclassified
until its exact shape is reviewed. The trusted expected tree is
`831fc61e72b5c6b9570f91d6eabc272ad8f2899f`, independently derived from two
mode-100644 entries: `.gitignore` blob
`94d1ec507d791eab738351179c145df29bd8fb9d` (`.antiphon/` plus one LF) and
`seed.txt` blob `e50285176007e126bede4a894b09763c6c194f6d` (`benign fixture`, no LF).
Validate these raw tree/blob bytes as well as their IDs; an input tree ID is not
the source of the expected value.

Read `git cat-file commit <oid>` as a length-checked byte stream, with replacement
objects/lazy fetch disabled. Do not use pretty/log formatting, header maps,
shell command substitution (which strips final LF), text normalization or trimming.
After a strict full-buffer grammar check, capture only the author/committer epoch
and timezone tokens. Each epoch is canonical nonnegative decimal seconds (no sign,
leading zero except `0`, whitespace or overflow); each timezone is exactly a sign
and four decimal digits with hours 00..23 and minutes 00..59. Reject unknown forms.
Construct this expected ASCII byte buffer independently, interpolating only those
four validated timestamp tokens; `\n` below is exactly byte 0A:

```text
tree 831fc61e72b5c6b9570f91d6eabc272ad8f2899f\n
author Fixture <fixture@example.invalid> <author-epoch> <author-zone>\n
committer Fixture <fixture@example.invalid> <committer-epoch> <committer-zone>\n
\n
seed\n
```

The display line breaks separate the five byte segments; concatenate them with
no additional separators. Require equal length and **byte-for-byte equality** of
the entire raw object against that expected buffer. No parent line (zero parents),
extra header, repeated/reordered header, continuation line, CR, BOM, NUL, extra
blank line or trailing byte is admitted. In particular `gpgsig`, `gpgsig-sha256`,
`mergetag`, `encoding` and custom headers always refuse, even if a Git parser
otherwise reports the same tree/author/committer/message or the signature is valid.
Header-carried bytes are protected content, not disposable scaffolding. Only the
four timestamp tokens vary; no raw candidate header or message is reused to build
the expected object. Validate index/worktree against that exact seed with
no extra content (apart from empty directories and standard Git init scaffolding).
A full match emits disposable-fixture plus classifier version, path and commit ID;
it needs no origin. Otherwise report no-remote-unclassified with all available
commits/paths; do not attempt a network lookup or silently omit the repository.

This is a narrow generated-artifact classification, not a change to publication
semantics for real repositories. An attacker copying arbitrary work under this
path or setting Fixture as author still fails the tree/closure checks. A byte-for-
byte duplicate of the known seed carries no unique protected content. Initially
`cases/<id>/main`, other card test outputs and arbitrary temp repos are NOT
exempt; add another reviewed exact shape only with its own witness. Normal clean
published repositories in test-output still use ordinary host proof. Rejected:
path-only, author-only, all-of-/tmp disposal, and a volume-authored disposal marker.
Also rejected: comparing only tree/identity/message or ignoring unknown commit
headers. The reviewed fixture command does not explicitly disable ambient signing
or encoding settings: if those produce extra headers, even that fixture is refused
by this narrower rule. This intentional false refusal is measured in rollout step 4;
do not relax the byte contract to accommodate it.

**D-18 — Gone-tracking after a rebase remains content-based refusal.** A local
ref whose origin branch was deleted passes only if the original retained OIDs
remain reachable under the existing origin-head predicate. An equivalent rebased
tree, patch-id, successful land/task, or a deleted remote ref cannot prove that.
List each unadvertised-local/gone-tracking location and unpublished original
commit. Recommend operator recovery/publishing to a durable archive **branch**
under origin (so it satisfies the existing heads predicate), followed by a fresh
preview. Never push or prune on the operator's behalf. Any policy that discards
rebased originals is a separate CARD-1105 replay-3b decision; default is preserve.

Refusal estimate for the historical a04846da summary: at least one volume-set
refusal with up to 32 reflog-only commit blockers plus two original annotated tag
objects not proved by the prototype, and the ambiguous state .key. Exact unique
refusal counts/rate, FETCH_HEAD/gone-ref overlap and fixture prevalence are unknown
without the retained full inventory; this host cannot read the desktop receipts.
Do not claim 32 unpublished objects merely because they were omitted. A new
read-only preview/replay measures findings by reason, unique object and repository
in one pass; the original deleted volumes cannot be rescanned.

**D-19 — Retry transient census reads at effect boundaries, never effects.**
The landed PowerShell census retries Drift three times but immediately refuses
Unstable. The shell also contains CARD-1371's post-container-removal Drift
allowance; v2 must NOT use that allowance to authorize a volume delete. Preserve
v1 behavior in its adapter. For v2 both wrapper and shell share this contract:
at preview, apply admission, after container cleanup, and immediately before each
volume delete, collect a complete fresh two-pass project-closure census. Retry
only classified cross-read Drift/Unstable, at most four total attempts with
2 s, 5 s, 10 s backoffs, inside one 180 s monotonic gate budget (provisional; record
production p95 before tuning). Every HTTP call also respects the remaining budget
and its existing call deadline. No nested retry may reset the gate budget/count.
Discard comparison snapshots on retry; preserve every attempt/read, cause,
duration and backoff in the immutable phase evidence. No inventory/helper/network
proof or volume deletion is repeated merely to retry a census.

A real bound task/land, nonzero/unknown counter, malformed/incomplete pagination,
changed identity or unsafe topology is not Unstable and is not forgiven by this
loop. Keep the existing separately bounded bound-land wait at admission; it cannot
turn an unsafe observation between deletions into permission to continue. After a
successful retry recheck all current guards and remaining incarnations/references;
if the wait aged publication evidence, refresh it under D-7 before any effect.
Deadline or fourth Unstable/Drift refuses; no stale census is accepted as zero.

Before the first volume intent/rm, refusal leaves every volume intact (any earlier
container cleanup has its own journal/outcome). Between two deletes there is no
cross-volume atomicity: immediately stop before the next intent/rm, persist
PartialRefused with completed/intended targets and census evidence, and retain all
remaining volumes. A crash during backoff has no new effect. Resume must consume
the same durable journal and re-prove remaining roles, not restart the entire
retirement blindly. This guarantees retries themselves cannot create unrecorded
partial retirement; it does not promise rollback of an earlier successful delete.
Rejected: unlimited retries, reusing the first census, or accepting unstable final
rows merely because containers have already been removed.

**D-20 — Retained volumes stop the rollout and block a new temp deployment.**
Add `Assert-TempRetirementClearable` to the **wrapper's deploy-temp recreation
branch**, immediately after the existing container-absence check and BEFORE
`/drain/clear`, `/drain`, `runner-cache-seed` or any mutating host case. This is a
read-only census of the four exact names and the protected operation journal;
it creates no retirement record, changes no runner state, and performs no cleanup.
Failure returns nonzero with no drain POST or host deployment/seed invocation.
In particular a refused/partial retirement keeps its original retiredAt and
retireWhenIdle, so its Preview/Resume recovery remains reachable.

Use the D-8 host coordinator to own FD 8 across this check and the wrapper's
reactivation handshake; do not release the lock between admission and clear,
nor acquire it again through a nested SSH child. EOF/timeout releases the owned
coordinator without deployment. Existing already-current, available temp reuse
does not clear retirement or recreate volumes and keeps its existing path.
`case_deploy_temp_runner` also rechecks the same predicate under FD 8 immediately
before checkout/build/Compose, covering direct host callers. The host recheck is
defense in depth; it cannot replace the wrapper check. Do not adopt retained state.

The predicate is over **all** protected journal entries for this daemon and these
four names, not just the most recent operation or matching retirement epoch.
Phase, target effects/intents and helper ownership are recorded independently of
the status label. An incomplete/unreadable/unknown-schema journal census refuses.

| Current observation / journal state | Deploy-temp disposition |
|---|---|
| Any of the four volumes is present, even with no journal or a Completed/Closed record | Block `TempVolumesRetained`; list exact retained names, incarnation and operation or unjournaled. |
| Active operation/owned helper or unresolved child ownership, even with no target intent | Block; stop/join through the owning operation before reconciliation. |
| Preview-only Pending/Ready/Refused, no container/volume effect or durable intent, no active owner/helper, all four absent | Inert; does not block. Preserve the record, including expired Ready. A refused no-effect preview never needs a close merely to deploy. |
| Apply Pending/Ready/Refused before any target intent/effect, inactive and all four absent | Inert by the same no-effect rule; never infer this from a status string alone. |
| Any nonterminal record with a durable container/volume intent or observed effect; Applying, Partial, PartialRefused or interrupted cleanup/apply | Block, including all-absent. Resume/reconcile or explicitly Close; an absent target cannot silently resolve an intent. |
| Completed with all intents/results resolved and owned children joined, or an audited Closed record | Nonblocking only when current census independently proves all four absent. |
| Inconsistent terminal state, unknown outcome, lost custody or failed enumeration | Block unknown; no timestamp, newer operation or operator reason bypasses missing proof. |

Add a mutually exclusive, operator-authorized parameter set to the D-10 entry:
`pwsh -NoProfile -File scripts/retire-runner-volumes.ps1 -Close -OperationId <id>
-ExpectedJournalDigest <sha256> -ReasonFile <file>`. This is an audited **journal
transition**, never a delete or retirement-clear command. Under the same lock,
verify authority/custody/digest, inactive operation and joined children, exact
daemon/target identity, complete container/reference census and all four volumes
absent. Reconcile prior intents as `removed-observed` only where the absence was
actually observed; record un-attributed absence separately when no intent exists.
Append actor/authority, UTC, reason, prior digest, observations and final `Closed`
digest without rewriting history. Failed persistence leaves the blocker intact.
A stale retiredAt does not prevent this observation-only closure; it never grants
apply authority. Present/replaced volumes, unknown journal/children or any unsafe
census refuse Close and require content recovery plus Preview/Resume. A later
Completed operation does not silently erase an older unresolved effect record.
Document this command and these refusals in the retirement runbook. No force-close,
automatic supersede, manual file deletion or adopt switch is provided.

A content/census refusal in retire-temp keeps the standing main accepting work but
returns nonzero and leaves rollout gate 9 incomplete. It cannot report the rollout
complete or run C849 CP-5 as if retirement succeeded. Resume/recover the retained
set, then require completed retirement and the normal Retired cache verification.
`verify-card0849-caches.ps1 -Case Retired` keeps its success expectation
`externalVolumes=3 tempPrivateVolumes=0 mainTmpRetained=true`; document its
precondition explicitly instead of weakening zero to allow retained volumes.
The verification script needs no success-format change. Refused retirement is an
incomplete gate, not a failing cache policy to waive. A later rollout's deploy-temp
also refuses until that pending operation is resolved; no `-Adopt` bypass is added.

The v2 allowlist contains only the four explicitly bound temp-private volumes.
Reject main volumes and the three CARD-0849 external caches by exact name:
`antiphon-runner-cache-nuget-packages`,
`antiphon-runner-cache-nuget-scratch`,
`antiphon-runner-cache-npm-content`. Manual input cannot broaden that allowlist.
N3 proves all three survive. Rejected: automatic remount of retained work/state,
Compose down -v, declaring success with retained private volumes, or changing C849
to accept them as a successful Retired state.

**D-21 — Land CARD-1609 before CARD-1615 on both target lines.** CARD-1609 owns
content retirement, the pre-clear retained-volume gate, v2 census retries and
journal closure. CARD-1615 owns controller workspace/admission and its canonical
activation/evidence-root rules. Preserve bound pending-land refusal and the
existing separate bounded wait here; add no self-held-land exemption or controller
admission changes. Implement this card on then-current origin/master, port it to
rc only after top-up 38, and let CARD-1615 Code start from the recorded landed
CARD-1609 SHAs on both lines. Reject parallel overlapping implementation; no
unlanded plan is a dependency satisfied by assumption.

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
| F-24 | Tests manufacture a receipt or fake success without running the real gate | Red at base for observable behavior, real Git witnesses and separate real Docker cases; CP-1/2/14 and N1-N3. |

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
| S1: regression witnesses | Add `tests/Antiphon.Tests/Scripts/VolumeRetirementContentTests.cs`, `VolumeRetirementReceiptTests.cs` and a shared `VolumeRetirementFixture.cs`; extend `scripts/fixtures/c1008-fake-docker.sh` only for observations/fault cuts. Drive current entrypoints so no missing-method/unknown-argument error can masquerade as red. Add native guards and their `WindowsUnitSkipRoster.cs` entries in this same test slice. | C1/C2/J1/H7 fail on intended readiness/no-deletion assertions against unchanged production at the landed Code baseline; CP-1/2/14. |
| S2: inventory and host proof | Keep `c1008_git_program` as the v1-compatible extraction boundary in `scripts/c590-remote.sh`; extract/add `scripts/c1008-content-audit.sh` for the v2 inventory protocol. Refactor the landed `scripts/c1008-host-proof.ps1` at the baseline above, preserving sanitization but adding full origin ancestry/coverage/age. Add the role metadata walker in `scripts/c1008-volume-metadata.sh`. Adapt `scripts/fixtures/c1105-git-audit.cjs` to provide equivalent proof input to v2 without changing its expected classifications. | Finish C1-C14, H1-H7 and R3-R6; D-15 full-roster output. |
| S3: receipt and delete coordinator | Add `scripts/c1008-retirement.sh`; wire the temp-only branch and FD-8 deploy-temp retained-volume/journal recheck in `scripts/c590-remote.sh`. Implement strict schema, custody, locks, current runtime resolution, all-set proof, exact deletes, partial resume and D-20 blocking/Close state transitions. Change C994's temp retirement preflight to actual topology; bypass old image/Compose/stack requirements. Keep main v1 adapter unchanged. Update legacy tests' provenance expectations and their skip-roster identities in the same commit. | J1-J10, R1/R2 and N2/N3; J8 covers Close. |
| S4: wrapper and manual entry | Add `scripts/retire-runner-volumes.ps1` including the audited Close parameter set; update `scripts/deploy-server2.ps1` with read-only `Assert-TempRetirementClearable` BEFORE clear/drain/host effects and the single-owner FD-8 handshake. Update `scripts/c590-real.ps1` for versioned typed transport, operation-scoped script staging, host proof exchange, auto-preview/apply, D-19 bounded census protocol and explicit attestation. Update current audit descriptor during supported host tool installation; do not activate or deploy during Code tests. | Add `VolumeRetirementHostProofTests.cs` and `VolumeRetirementWrapperTests.cs`; H1-H6/W1-W6. W6 observes the actual wrapper HTTP and host-call trace. ASCII transport scripts; LF shell files. |
| S5: native qualification and docs | Add `VolumeRetirementDockerTests.cs` and `scripts/fixtures/c1609-retirement-real-cases.mjs` using the existing owned-daemon fixture infrastructure. Update `docs/docker-stack.md` retirement/recycling sections and `docs/orchestration-loop.md` pinned-audit/manual-authority wording together. Document D-20 gate-9 refusal, retained-volume deployment block, and unchanged C849 Retired zero requirement. Keep AGENTS as index unless an actual obsolete mandatory statement needs correction. | N1-N3, exact final CP-3..13 plus CP-15/16; update runbook command examples and refusal recovery. |

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

S5 also edits `tests/Antiphon.Tests/Infrastructure/DockerStackDocumentationTests.cs`:
`Retired_temp_cleanup_and_retirement_gates_match` must pin the new retained-volume
stop/recovery contract while preserving separate container gates.
`Main_volume_recycling_is_scripted_only` must distinguish unchanged main v1 from
new temp v2: replace temp's historical-image/default-network/auth-fallback/no-proof
sentences with current tooling, offline helpers, all-origin host proof, exact
volume deletion and complete refusal roster. Retain main's scripted-only rule,
location/object safeguards and credential omissions. Do not leave obsolete temp
wording merely to satisfy an old string pin. CP-13 now selects the whole 16-method class, including both methods.
Update `RollingVolumeRecycleScriptTests.C1008_Documentation_and_transport_pins_match`
for the same v1/v2 distinction and run it in CP-8; preserve its transport/custody
assertions. These doc pins complement behavior tests, not replace them.

### Complete existing-test dispositions (round-3 D-2)

Recounted from `origin/master` at `98fc3ceb6440a17b68cafe3ecc21c76d190b18ca`:
the actual source has **16** RetiredTempContainerScriptTests methods (not 15),
**22** RetiredTempContainerHostTests methods (13 more than the old nine-method
CP-9), and **16** DockerStackDocumentationTests methods. Each is one execution;
internal vector loops are not additional TUnit results. The host class's
`RequireNativeLinux` is a setup hook, not a 23rd test. No method below is silently
dropped. "Unchanged/run" preserves every assertion; fixture wiring may supply
the new complete protocol without manufacturing PASS. "Rewrite" retains the name
and all safety counterexamples while updating the specifically named old contract.

All rows in this first table are in
`tests/Antiphon.Tests/Scripts/RetiredTempContainerScriptTests.cs`, S3/S4,
**CP-15 (Linux, all 16)**:

| Method | Disposition / invariant |
|---|---|
| `C994_Drain_waits_for_exit_and_removes_containers` | Unchanged/run: bounded exit, container-only gate 8, gate 9 separate. |
| `C994_Drain_uses_one_deadline` | Unchanged/run: one monotonic drain deadline. |
| `C994_Retire_recovers_exited_and_absent_temp` | Rewrite trace for v2 current proof/auto-preview/apply; preserve exited/absent/null/zero/live refusal and operation linkage. |
| `C994_Wrapper_retirement_proof_is_typed` | Unchanged/run: full typed status matrix. |
| `C994_Wrapper_main_admission_is_required` | Unchanged/run: main eligibility and SHA requirement for drain. |
| `C994_Wrapper_task_and_land_census_is_complete` | Rewrite v2 transient retry expectations only; retain complete matrix, bound-task/land and malformed refusal. |
| `C994_Strict_volume_admission_remains_independent` | Unchanged/run: retired absent-null exception never weakens shared zero checks. |
| `C994_Host_strict_volume_admission_remains_independent` | Unchanged/run: host version of the same null/zero matrix. |
| `C994_Preview_reports_pending_volume_proof` | **Rewrite** obsolete `volumeProof=pending-confirmed-absence auditPending=true` pin: present exited containers yield typed Pending/no Ready; absent containers require actual v2 inventory/host proof before Ready. Both paths have zero target effects and POSTs. Keep name for traceability. |
| `C994_Resume_never_runs_container_cleanup` | Rewrite to v2 operation journal input; retain no-new-container-cleanup and present-container refusal. |
| `C994_Host_resume_never_runs_container_cleanup` | Unchanged/run: main v1 reconciliation tuples and no new effects. |
| `C994_Cleanup_receipt_is_required_before_recycle` | Rewrite to current cleanup intents/results plus current Ready proof; retain missing/mismatched/copy-failed/cleanup-failed/drift stops. Historical cleanup receipt is not required when already absent. |
| `C994_Verifier_manifest_is_strict` | Rewrite/add v2 context alongside v1 adapter; retain duplicate/type/unknown-field/wrong-lane/resume/operation refusals. |
| `C994_Bridge_transport_preserves_literal_context` | Rewrite/add typed v2 literal transport; retain all raw-context and timestamp vectors. |
| `C994_Host_bridge_transport_preserves_literal_context` | Rewrite bridge staging/receipt assertions for operation-scoped current tooling; retain ASCII/LF, UTC normalization, copy-failure and custody assertions. |
| `C994_Separate_invocations_preserve_audit_identity` | Rewrite historical-image identity into per-operation current-runtime/proof identity; retain separate invocation IDs and deploy refusal before any POST. |

All rows below are in
`tests/Antiphon.Tests/Scripts/RetiredTempContainerHostTests.cs`, S3,
**CP-9 (Linux, whole class, 22)**:

| Method | Disposition / invariant |
|---|---|
| `C994_Exited_owned_containers_are_removed_without_volumes` | Unchanged/run: exact owned exited IDs only, no volume effects. |
| `C994_Absent_cleanup_is_idempotent` | Unchanged/run: absence is independently observed; no historical model required. |
| `C994_Older_retirement_receipt_does_not_block_cleanup` | Rewrite historical image field pin as optional provenance; old Completed cleanup never lends authority to current cleanup. D-20 separately governs unresolved effects before deploy. |
| `C994_Host_retirement_proof_is_typed` | Unchanged/run: retirement field matrix. |
| `C994_Host_main_admission_is_required` | Unchanged/run: main eligibility matrix. |
| `C994_Host_task_and_land_census_is_complete` | Rewrite only v2 retry protocol; retain every scope/bound-work/malformed vector. |
| `C994_Census_and_inspect_fail_closed` | Unchanged/run: incomplete/duplicate/malformed/unknown Docker observations refuse. |
| `C994_Container_service_and_state_are_restricted` | Unchanged/run: service, cardinality, project, exited-state restrictions. |
| `C994_Production_mount_topology_is_proven` | Rewrite old rendered-Compose dependency to current versioned actual-topology roster; keep bind/tmpfs/private-name/token missing/source/RW counterexamples and 15/3 supported mount shapes. Old Compose text changing alone is now harmless; unknown actual topology still refuses. |
| `C994_Whole_candidate_set_precedes_removal` | Unchanged/run: bad later candidate prevents every removal in either ordering. |
| `C994_Rollout_lock_covers_final_census` | Unchanged/run: real flock, rollout-before-cache order and final receipt under lock. |
| `C994_Each_removal_rechecks_live_proofs` | Rewrite historical generation comparison only; preserve changed live image/container identity, mounts/state/ID/retirement/task/land refusals. An image need not be installed, but observed candidate drift is still unsafe. |
| `C994_Final_absence_is_observed` | Unchanged/run: rm-success with remaining/replaced/unknown container cannot complete. |
| `C994_Removal_failure_and_crash_retry_preserve_progress` | Unchanged/run: all four durable crash cuts, exact remaining IDs, fresh guards, volumes retained. |
| `C994_Receipt_write_and_copy_fail_closed` | Unchanged/run: intent/outcome/copy failures stop. |
| `C994_Host_context_is_strict` | Rewrite/add current schema while preserving strict v1 adapter and malformed context vectors. |
| `C994_Host_preview_never_mutates` | Rewrite image/provenance output only if needed; preserve zero container/volume mutation and exact preview roster. |
| `C994_Image_receipt_lookup_is_bound` | Replace with `C1609_Cleanup_does_not_require_historical_image`; preserve unknown topology/ownership refusals, remove only old image equality requirement. |
| `C994_Audit_image_is_pinned_and_required` | Replace with `C1609_Audit_uses_current_image`; current descriptor/digest/UID required, old image optional, unknown current runtime refused. |
| `C994_Cleanup_receipts_redact_unapproved_fields` | Unchanged/run: sentinel absent from success/refusal/stdout/host/copy, credential binds untouched. |
| `C994_Fixture_custody_is_bounded` | Unchanged/run: owned roots/labels, production-name refusal, bounded joined children. |
| `C1105_Temp_cleanup_uses_previous_generation` | Rewrite old-stack-SHA refusal into supported older actual topology without historical source; retain 14-mount/token-absent shape and reject an altered actual mount. |

The deploy-temp dependency scan finds **15 methods / 23 results** in
`tests/Antiphon.Tests/Scripts/RemoteScriptContractTests.cs`, including shared
block readers and C973's reader harness beyond the review's approximate nine.
Run this exact roster in **CP-16 (Linux)** after S3/S4. Extend extracted harnesses
with explicit new preflight/lock protocol inputs; an undefined shell function is
never expected refusal. Keep the real guard in ordering/refusal tests. Subject-
unrelated harnesses may stub it with explicit absent-volume/empty-journal fixtures;
W6/J8 own its behavioral matrix, and those stubs must not mask its own test calls.

| Method | Results | Disposition |
|---|---:|---|
| `C0995_Deploy_temp_checks_host_absence_before_checkout` | 1 | Rewrite/add actual volume/journal guard order before checkout; retain existing container/ps-error matrix and positive control. |
| `C849_Deploy_temp_names_saved_import_when_seed_is_missing` | 1 | Rewrite harness inputs for new preflight; preserve saved-import diagnosis and no checkout. |
| `C849_Deploy_prepares_and_verifies_before_acceptance` | 1 | Rewrite wrapper/host traces for preflight handshake; retain prepare/smoke/acceptance ordering, cache and failure vectors. |
| `C849_Deploy_temp_observes_inaccessible_docker_mountpoints_with_sudo` | 1 | Rewrite harness inputs; preserve inaccessible mountpoint/UID behavior, sudo boundary and positive control. |
| `C849_Docker_volume_paths_have_no_bare_host_reads` | 1 | Rewrite scanner to include new metadata/preflight helpers; require `sudo -n` on privileged host metadata reads, no payload reads or broad exemption. |
| `C849_Seed_publishes_complete_payloads_before_its_marker` | 1 | Rewrite old exact wrapper case-list pin to include the read-only preflight trace before any clear/seed; preserve payload-before-marker and cache seeding assertions. |
| `Temp_deploy_uses_its_own_project_and_the_inspected_grok_store` | 1 | Unchanged/run: project and inspected store isolation. |
| `Nested_lane_never_uses_sudo_or_python` | 1 | Rewrite narrow helper allowlist to include new host-only metadata walker, preserving no nested sudo/python. |
| `C1067_Nested_lane_sudo_guard_is_sensitive` | 4 | Rewrite historical lookup guard control to the current-runtime/metadata host guard; retain all four independently sensitive controls. |
| `Deploy_parent_creates_the_github_token_directory_without_reading_it` | 1 | Unchanged/run: token directory custody, no reads, ensure-before-checkout/Compose in both deploy blocks. |
| `C849_Saved_donor_archive_is_checked_and_imported_without_a_container` | 1 | Rewrite extracted deploy harness inputs only; preserve archive safety, import and no-container checks. |
| `C849_Deploy_ordering_contract_is_pinned` | 1 | Rewrite obsolete `compose_temp down -v` assertions to exact v2 intent/proof/rm ordering; preserve main v1/cache ordering. |
| `C912_Cold_runner_verification_uses_mounts_and_writability_not_payloads` | 1 | Unchanged/run: both deploy blocks retain cold mount/writability verification. |
| `C912_Cold_exception_never_weakens_maintenance_or_donor_gates` | 1 | Unchanged/run: jq guard and unchanged maintenance/donor zero/attachment gates. |
| `C973_Cold_marker_readers_accept_pruned_seed_image_and_refuse_invalid_markers` | 6 | Unchanged/run: its harness extracts only the real cache prepare/ready slice and cache-preservation predicate, not the new deployment preflight. Preserve all six reader identities, marker-independent retirement and external-cache identity protection; do not rewrite this narrow reader into a fake full retirement success. |

Shared fixture scope in S3/S4 includes `C1008WrapperFixture` and
`C1008HostFixture`, both declared in
`tests/Antiphon.Tests/Scripts/RollingVolumeRecycleScriptTests.cs`, plus
`scripts/fixtures/c1008-fake-docker.sh`, `scripts/fixtures/c727-fake-http.ps1` and
`scripts/fixtures/c727-fake-verify.ps1` (the actual wrapper fixture boundaries).
`scripts/fixtures/c973-marker-reader.sh` is read and retained unchanged.
Preserve old main paths; add complete typed observations
for v2, not a global audit-success stub. Record exact changed fixture paths in
the implementation report. Changes to skip identities remain in CP-12.

**CP-13 (Windows, S5)** now runs the whole
`tests/Antiphon.Tests/Infrastructure/DockerStackDocumentationTests.cs` class,
**16 results**. Rewrite `Recycle_census_preflight_is_documented` and
`Lands_proceed_during_rollout_is_documented` to explicitly distinguish the bounded
v2 2/5/10-second retry, four attempts/180-second gate and PartialRefused stop from
main v1. Preserve bound-land wait, off-runner lands/no global freeze and AppHost
pull wait. Rewrite `Retired_temp_cleanup_and_retirement_gates_match` and
`Main_volume_recycling_is_scripted_only` as above, adding pre-clear refusal and
audited Close recovery. Unchanged/run: `Docker_stack_doc_names_the_nested_daemon`,
`Docs_name_linux_cgroup_backend`, `Orchestration_loop_names_server2_producer`,
`Credentials_doc_names_the_deploy_key_custody`, `Card_0590_D6_is_annotated_superseded`,
`Ops_http_names_runner_status_and_runner_id`,
`Testing_doc_names_the_production_enablement_steps`,
`Agent_kinds_doc_names_the_raw_allow_list`, `Credentials_doc_names_the_claude_login`,
`Mount_generation_protocol_is_documented`, `Ops_http_names_provider_auth`, and
`Fixture_gate_slot_wait_is_outside_the_arrival_window`. Whole-class execution is
cheap string inspection and guards adjacent owner paragraphs against accidental loss.

Windows Unit admission is part of S1/S3/S5, not deferred cleanup. Update
`tests/Antiphon.Tests/Scripts/WindowsUnitSkipRoster.cs` alongside every source
rename/move: remove the four relocated historical-image entries, retain/update
`C1105_Temp_cleanup_uses_previous_generation`, and preserve both CARD-1607
identities. New Linux-only C/J/N methods use the established
`NativeLinuxGate.Require(NativeLinuxReason)` as their first statement, and each
has an exact Native roster entry with one result and its literal reason. Add the
class constant and process limiter; update the fixed Native identity/result totals
in `WindowsUnitPlatformAdmissionTests.cs` from the actual declarations (never
loosen or remove the census assertions). Portable H/W methods must not acquire a
Linux-only gate; they execute on Windows as well. CP-12 runs guard, first-statement
and bidirectional closure checks against the final source in the Windows lane.

CARD-1607 test disposition (S2, preservation R6 in CP-8): retain both existing
method names so their vectors remain traceable. Rewrite
`C1008_Host_proof_admits_only_an_authenticated_private_origin` for v2 temp:
retain authenticated success, unpublished-child refusal, missing/wrong/old/malformed
proof, non-auth errors and no-credential/helper-custody assertions; replace the
"only after classified auth failure" expectation with proof for every origin.
Keep a v1 scanner adapter vector for the existing fallback; do not add host-proof
transport to main. H2/H3/H4 extend those
vectors, not substitute for them. Rewrite
`C1008_Host_proof_file_omits_credentials` to retain artifact/log/argv sentinel
assertions: v2 rejects credential-bearing URLs before invocation rather than
silently sanitizing one into authority; a trusted clean origin with ambient fixture
credentials proves success without exporting secrets. Keep v1 scrubbing coverage
in its v1 scanner adapter; H5/H6 add v2 protocol/deadline cases. Neither test is deleted.

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
| Existing Git closure rows | CP-5 about 1 min, CP-6 about 3 min, CP-7 about 1.5 min (rounded to 2 in the manifest) | Reuse existing tiny-Git fixtures, share seeds where isolated copies preserve semantics. Measure actual per-argument execution, not internal assertions. |
| Existing retirement/deploy regressions | CP-9 about 3 min, CP-15 about 3 min, CP-16 about 3 min; provisional row caps 6 min each | Shared immutable Git seeds and assembled harness text once per run; fresh small fault-state copies only. Existing process-heavy matrices may exceed the 60 s portable-test estimate; record per-method duration and enforce a provisional 120 s method cap, pending CARD-1609 samples. No real infrastructure per method. |
| Documentation closure | CP-13 under 5 s body, rounded to 1 min in manifest | String-only shared source reads; no process or service setup. |
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

The scripted auto-preview/apply deliberately pays for two full content observations.
Using the old measured 1404 s and 5225 s samples only as scenario estimates, content
alone costs 2808-10450 s (about 47-174 minutes) per retirement, before new role
classification, list-all output, host fetch and census. The combined content cap
is 21600 s (6 hours), with at most 600 s host network budget per phase and D-19's
180 s per census gate (six base volume gates = at most 18 minutes, extra container
gates itemized separately). These are finite ceilings, not expected durations or
measured p95. Record preview inventory/publication/live-guards separately from
apply inventory/publication/per-delete guards and teardown, plus unique repos,
objects and findings. The former five 13-60 minute refusal runs are motivation for
one complete preview, not a measured v2 benchmark. No host object cache is added
now; measure fetch cost first. Any future shared cache may contain only origin
objects and must use fresh advertised tips each phase, never local volume objects.

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
N = `VolumeRetirementDockerTests`. H7 is intentionally in the Linux-only
`VolumeRetirementContentTests` class because it executes the real remote shell
path; H1-H6 remain portable host logic. Exact methods and obligations:

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
| C12 | `Fetch_head_requires_proven_objects_not_fresh_names` | Published old commit and published lazy-fetch blob pass without mutation; private commit/blob, missing object, malformed and mixed entries refuse with every location/OID. PC: skip FETCH_HEAD or accept every non-commit. |
| C13 | `Only_exact_known_seed_fixtures_are_disposable` | D-17 exact no-remote raw seed passes in work and tmp; copied real repo, second fixture-authored commit, changed tree/message/committer, remote, reflog-only work, dirty bytes, unapproved path, symlink and catalog spoof each refuse/list. Sixth adversarial repository: identical permitted fields/tree but payload in gpgsig, encoding or custom/continuation header; require refusal and zero deletes. Also reordered/duplicate header, parent, CR/NUL/trailing-byte variants refuse. PC-SeedHeaders substitutes field-only comparison: intended refusal assertion must turn red, then the same method green after restoration. |
| C14 | `Gone_tracking_retains_original_commit_protection` | Deleted origin ref with original commit reachable from another head passes; rebased equivalent tree without original OID refuses/list; real archive-head publication then passes. PC: equate patch/tree with reachability. |
| J1 | `Preview_receipt_is_required_and_consumed` | D-7/8: direct low-level apply without Ready refuses; wrapper auto-preview uses exact durable proof once. Red at base: low-level apply deletes despite absent authorizing preview. |
| J2 | `Receipt_shape_custody_and_digest_are_strict` | Invalid/duplicate fields, foreign owner/links/writable ancestor/truncated files/digest mismatch/failed flush refuse. PC: trust copied PASS or skip custody. |
| J3 | `Volume_incarnation_and_operation_are_bound` | Alter name/CreatedAt/daemon/inode/role/labels/op/nonce individually; each refuses. PC: compare name only. |
| J4 | `Freshness_and_policy_version_are_rechecked` | Expired/future proof, compatible current-build change, incompatible policy, long apply after valid admission; fake clock. PC: remove upper age check. |
| J5 | `Changed_content_and_remote_proof_refuse` | New private/public ref, dirty bytes after preview and remote snapshot loss; reprove permissible remote advancement. PC: reuse preview publication. |
| J6 | `Every_delete_rechecks_full_live_guards` | New running/exited refs, bind aliases, busy/routed/bound land or changed retirement between deletes; exact-name-only effects. Inject Unstable x3 then stable, x4 exhaustion before first delete, between state/dind deletes, and resume; assert 2/5/10 backoff with fake clock, no next delete and complete attempt journal. Malformed/bound-work is never retried as Unstable. PC: reuse initial census or accept last unstable rows. |
| J7 | `Lock_order_excludes_supported_writers` | Real flock barriers: concurrent create/recycle/resume serializes; cache-only path cannot invert; timeout no mutation. PC: skip rollout lock. |
| J8 | `Journal_intents_and_partial_resume_are_exact` | Every persistence/delete cut, no-intent absence, partial success, replacement, remaining-role dependencies and copy failure. D-20 Close: stale effect record/all absent closes with append-only audit; present/replaced volume, active/unknown child, wrong digest/authority, incomplete census and failed journal write refuse; no delete/clear and no invented attribution. PC: infer deleted from missing file/error or close without the full census. |
| J9 | `Legacy_receipts_are_history_only` | All v1/missing/conflicting/foreign receipts preserve history but require fresh v2 proof; no schema-upgrade-to-PASS. PC: import old success as Ready. |
| J10 | `Manual_attestation_is_exact_and_cannot_waive_findings` | Correct human/delegate authority plus exact receipt permits same proof; wrong/missing attestation or any unsafe finding refuses. PC: manual flag bypasses audit. |
| H1 | `Host_proves_complete_origin_ancestry` | Same-ref equality, advanced-origin ancestry, tags/tree/blob semantics and origin graph absence; use real isolated Git. PC: accept OID presence without reachability. |
| H2 | `Private_and_public_origins_share_offline_gate` | Both origins use host proof with helper network none; no credential mount/env; private success fixture and unavailable credential refusal. PC: proof only for exit 128. |
| H3 | `Unknown_remote_and_object_failures_refuse` | Non-auth 128, nonzero ls-remote/fetch, malformed/empty advertisement, disappearing refs/objects and bounded churn. PC: fallback to local remotes. |
| H4 | `Proof_coverage_binding_and_freshness_are_exact` | Missing/duplicate/extra/wrong-origin/object/nonce/digest/timestamp row refuses, including multi-repo origin reuse. PC: subset proof treated complete. |
| H5 | `Origins_and_git_configuration_cannot_redirect_credentials` | Unapproved origin/protocol, userinfo, local config/redirect/helper injection refuse before credential invocation. PC: use inventory URL directly. |
| H6 | `Child_deadline_and_output_redaction_are_bounded` | Timeout, pipe holder, process-tree death, large stream and sentinel errors; no leaked auth/URLs/raw stderr. PC: child survives deadline or output copied verbatim. |
| H7 | `Private_origin_advanced_head_uses_real_host_proof` | Real bridge/host writer/audit with fake Docker/SSH and isolated real Git A->B: credential-free ls-remote/fetch fail 128, host can read B, volume contains only A. Red at landed baseline: origin-present refusal despite A published; green v2: host ancestry proves A, complete Ready preview, zero removes. See baseline recipe below. |
| W1 | `Retire_temp_uses_current_proof_protocol` | Auto-preview/apply, explicit receipt, DryRun, all phase and absent no-op entry behavior. |
| W2 | `Manual_parameters_require_operator_attestation` | Parameter sets, exact names/authority/digest, no force bypass; generated template is not accepted as signed. |
| W3 | `Bridge_validates_and_preserves_receipt_fields` | Strict JSON/quoting, multiline/path characters, large proof by file, per-operation staging, ignored evidence and ASCII/LF scripts. |
| W4 | `Retire_temp_ignores_missing_stack_and_historical_compose` | Entry-level no old stack/source/image requirement, including absent/exited containers and old callers with -Sha. |
| W5 | `Main_recycle_and_existing_deploy_contracts_are_preserved` | Non-retirement -Sha requirement, main generation/resume behavior and existing authority gates stay enforced. |
| W6 | `Retained_volumes_block_deploy_and_completion` | Actual wrapper refuses retained/partial/unjournaled sets with zero `/drain/clear` or `/drain` POSTs and zero seed/deploy host-case calls; retiredAt/retireWhenIdle unchanged and same-operation Resume succeeds after recovery. Exercise every D-20 state-table row: all-absent unresolved intent blocks, inactive no-effect refused preview admits, completed/closed absence admits, present volume still blocks with a terminal journal. Direct host path rechecks under FD 8; barrier proves supported retirement cannot enter between preflight and clear. Retire-temp refusal returns nonzero, no C849 Retired invocation/success; completed retirement keeps zero-private-volume contract. PC: move guard after clear, check only containers, block every Refused label, or ignore refusal exit. |
| N1 | `Read_only_namespace_and_host_proof_are_real` | Real helper kernel write-denial at each mounted alias, UID/offline/no secrets, linked pointers and hooks, before/after bytes including an initially empty /tmp volume that stays empty (volume-nocopy on every named mount); timeout kills only owned helper. |
| N2 | `Replacement_and_concurrent_reference_block_delete` | Real same-name replacement between phases, new named/bind reference, supported writer blocked by lock; no wrong volume deleted. |
| N3 | `Exact_deletion_and_crash_resume_preserve_other_volumes` | Real all-safe set removed by exact name; main/all three named caches/sentinel survive; crash cuts resume without duplicate/replacement deletion. |

H7 baseline recipe (S1): construct a tiny bare origin with commit A and then B;
clone the volume snapshot before B, remove any retained advertisement of A at the
origin, and prove with real Git that A is B's ancestor while B is absent in the
volume. A fake Docker driver executes the extracted production audit, not a canned
PASS. A fake SSH/file transport runs the real `c590-real.ps1`
`Get-C1008RetireProofExports` / `Write-C1008HostOriginProof` path and real shell
consumer against owned local scratch. Boundary-specific Git shims map an approved
fixture HTTPS origin to the bare fixture only on the credential-capable host;
inside the helper, ls-remote and fetch return auth-classified 128 with a fake
sentinel diagnostic. All local Git graph/content operations use real Git. The
fixture adapter is test-only and cannot enable file/ext origins in production.
Retain the legacy image/cleanup context needed to reach the base audit, so missing
history, missing commands or new arguments cannot cause this red.

At the landed CARD-1607 baseline the real host writer advertises B, but the shell's
fetch cannot authenticate and B is not present locally: it refuses at
`origin-present`. Assert successful publication/preview admission as the intended
failing assertion, and separately retain trace evidence that the helper saw 128,
the real host writer ran, its advertisement named B, and the base refusal was the
expected publication check. Do not prewrite a proof receipt or mock its parser.
With v2, the real host writer fetches B's graph in origin-only scratch and proves
A; the same entry reaches Ready with no removals, no helper credentials and the
A->B witness in the receipt. Include a host-read-128 control and an actually
unpublished child control: each must refuse and list its cause. Commit the S1
red fixture before any production fix; CP-14 records one intended assertion red,
CP-3 selects the identical H7 method green with the whole content class. H7's
Native roster entry is added in S1 and checked in CP-12.

Preservation rows: R1 = seven original named RollingVolumeRecycleScriptTests methods in
CP-8; R6 = the two CARD-1607 methods in CP-8; R2 = all 22 retired-container host methods in CP-9; R3 = 30 argument results
from four Git recovery/seed/index/object methods in CP-5; R4 = the existing 132
hidden-location results in CP-6; R5 = 40 evidence-name plus 12 mirror-owner results
in CP-7. R3-R5 already pass for protected behavior at base; do not fabricate red
at base. Their regression strength comes from retained assertions and the named
method-scoped positive controls. R7 = all 16 retired-container script methods in CP-15; R8 = the exact 15-method /
23-result deploy contract roster in CP-16; R9 = all 16 documentation pins in CP-13.
New C3-J10/H/N controls run later in the separate
Mutation stage, scoped to exact methods on a bound pack, never a whole class.

**PC-SeedHeaders (required C13 red witness):** build an owned real Git seed with
the exact D-17 tree, then write a valid raw commit via `git hash-object -t commit -w
--stdin` with a custom header carrying a harmless unique sentinel; bind HEAD/main
and reflog sides to this sole commit. Add `gpgsig` continuation and `encoding`
variants independently. Real `cat-file commit` must show the extra bytes while
the separately parsed author/committer/tree/message match the allowed seed. The
fixture must reach the v2 fixture classifier, not fail a path/ownership/dirty-tree
prerequisite. Run only
`/*/*/VolumeRetirementContentTests/Only_exact_known_seed_fixtures_are_disposable`.
In the commissioned Mutation stage, replace only the full-byte comparison with
the round-2 field-only predicate: the assertion that the header-bearing repository
is refused must fail, with no fixture/build errors. Restore exact comparison and
run that identical method green; the canonical byte-identical control must be
admitted throughout. Preserve each header variant's result in the method's evidence.
This is a classifier positive-control red, **not** a claim that the unchanged
baseline accepts no-remote seeds; the baseline has no v2 exemption. It adds no
ordinary checkpoint execution beyond C13 in CP-3 and does not change S1's four reds.

### Lanes, roster and execution

Read `GET /api/runner-defaults` and `GET /api/session-runners` again at dispatch.
They were read again during round 3 on 2026-10-30: revision 2; an eligible Linux default
and an eligible Windows peer were advertised (7/10 and 2/2 occupied respectively). These are
observations, not permanent placement instructions. CP-1..10 and CP-14/15/16 require the native
Linux maintenance lane (CP-4's portable logic is also exercised there); CP-11/12/13
use the native Windows PowerShell/Git lane (CP-12/13 are portable checks reusing its build). No host is pinned. Omit `-Runner`;
use `-Platform Linux`/`Windows` only for the rows needing that OS. Plan/Review is
portable and needs no platform flag (`Any` clears a previous pin).

Suffix-collision audit at base: every selected existing class/method operand below
has exactly the intended declaration; hidden locations were counted from the
fixture's delimited roster (132), evidence arguments from attributes (40), and
mirror-owner attributes (12). New class names are absent at base and reserved by
this plan. Code must recheck after adding them; TUnit TRX must match all intended
names/arguments and no suffix sibling. No full-path OR. Every OR operand has
parentheses and a trailing star. Table pipes are escaped. No zero-test credit.

If Code adds or changes an OS-selected production branch, record its portable
branch witness and native execution obligation under CARD-1526 and run
`pwsh -NoProfile -File scripts/check-os-witnesses.ps1 -BaseRef <full-base-sha>
-HeadRef <full-code-sha> -EvidencePath <ignored-os-witnesses.json> -Plan <this-file>`.
This is a declared static author-time validation, not another test/build row;
retain the result and fulfill native Windows/Linux obligations through the named
rows above. A no-branch-change declaration must name the inspected paths rather
than silently omit the check. Code/Review also run `scripts/check-evidence-diff.ps1`
over base..HEAD and preserve source/build attribution.

Use the checkpoint tool's `run --plan <this-file> --rows <row-list>` once per
committed group: S1 CP-1/2/14; S2-S5 Linux CP-3..10 plus CP-15/16; S2-S5 Windows CP-11/12/13. Run bootstrap
through `scripts/build-slot.ps1` if needed; the checkpoint runner takes build slots
for its rows. Continue `wait` until terminal exit (75 is running), in foreground
wait windows no longer than 60 seconds. Preserve exact CHECKPOINT lines with source
and build provenance; the S1 rows are intentional assertion-red evidence, not green.
Remove only task-owned alternate outputs after all runs have joined. CP-11 intentionally builds on the native Windows lane; an importer duplicate-build warning is expected because cross-host CP-3 output cannot be reused.

Import the manifest to ignored YAML to set per-row deadlines explicitly rather
than inherit the importer's 15-minute/3x floor: provisional execution caps CP-1=10m,
CP-2=2m, CP-3=10m, CP-4=2m, CP-5=2m, CP-6=6m, CP-7=4m, CP-8=2m, CP-9=6m,
CP-10=8m, CP-11=10m, CP-12=2m, CP-13=2m, CP-14=2m, CP-15=6m, CP-16=6m. These are roughly 2x the stated estimates, not measured-p95
claims. The 16-row manifest has 360 planned executions: S1 4 intended assertion
reds, final Linux 325 green and final Windows 31 green. Estimated charged duration
is S1 7m, final Linux 26m, final Windows 7m (40m total across three separate runs,
including one build per run); do not launch the entire manifest as one >30m run.
Row estimates include the first build in CP-1/3/11; record its duration separately.
Red-group charged cap 14m, final Linux 52m, Windows 14m; each hard wall
adds one finite 45m admission allowance. Reconcile with measured p95 under the
collection obligation before final qualification. If a group reaches 30m, stop
repeating it and hand timing evidence to Debug. No full assembly run is authorized.

### Checkpoints

2026-10-30 commissioned Debug `a2935adb` supersedes the initial Linux collection
caps above for **CP-3/4/8/9 only**: 20/6/10/20 minutes. See
[`2026-10-30-card-1609-s5-checkpoint-debug.md`](../../investigations/2026-10-30-card-1609-s5-checkpoint-debug.md)
for the censored 600/120/120/360-second rows, the measured 153-second private-origin
and 82-second wrapper matrices, and the shared immutable Compose setup repair.
These remain provisional bounded collection caps, not measured row p95. The
six-row Debug aggregate retains every filter/count, including CP-3's 25-method
floor despite the three intentionally out-of-scope missing methods. Export the
caps explicitly to the imported manifest; retain the original outcomes and costs.
Completed row observations supply rounded 2x recommendations; collect up to five
normal S5/Review samples under CARD-1609 rather than repeating green groups.

2026-10-30 completed Debug measurement supersedes those collection caps for the next normal run: CP-3=22m, CP-4=5m, CP-8=17m, CP-9=30m. The table estimates below now use observed execution, with the shared build charged to CP-3. These are provisional rounded 2x single-row proxies, adjusted upward by measured replacement-method cost after the final repairs; they are not population p95. CP-3 still measures 22 present methods and must be re-budgeted when J7/J8/J10 are added. Preserve all original filters/floors. Export these caps explicitly after import; importer 3x defaults are not the execution budget. Split future qualification into measured groups below 30 minutes, accounting for each isolated build, and retain this commissioned Debug record before any long selection.

2026-10-31 continuation cdf3cde4 measures the expanded J8 Close matrix and
strengthened J10. The Debug record above now pins the next CP-3 collection to
28 minutes, with a provisional 27-minute estimate including its producing build;
its full current-row p95 remains unknown. Keep all 25 methods and every other
filter/floor. Use a 30-minute charged group bound, preserve the 3/2 Close repair
run as red, and retain exact source/build receipts for the remaining rows.

2026-10-31 round 13 amends the CP-8 table row only: `C1008_Retired_absent_null_is_accepted`
shares `GetRetirementRequestAsync` with the wrapper row and was selected by no
checkpoint filter. Floor and expect move from 10 to 11. No other row's filter
or floor changes here.

2026-10-31 round 13 adds CP-17 for the default-off apply gate. It shares the CP-3
build. No earlier row's filter or floor changes.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1609-red/` | linux-content-red | `/*/Antiphon.Tests.Scripts/VolumeRetirementContentTests/(Current_tooling_and_missing_history_do_not_block*)\|(State_credentials_refuse_before_removal*)` | C1,C2 baseline | 2 executed, 2 intended assertion failures, no fixture/build errors | 2 | 5 | true |
| CP-2 | S1 | CP-1 | linux-receipt-red | `/*/Antiphon.Tests.Scripts/VolumeRetirementReceiptTests/Preview_receipt_is_required_and_consumed` | J1 baseline | 1 executed, intended no-deletion assertion failure | 1 | 1 | true |
| CP-14 | S1 | CP-1 | linux-private-origin-red | `/*/Antiphon.Tests.Scripts/VolumeRetirementContentTests/Private_origin_advanced_head_uses_real_host_proof` | H7 baseline | 1 executed, intended publication-admission assertion failure; real base path reached | 1 | 1 | true |
| CP-3 | S2-S5 | `tests/Antiphon.Tests -> bin-c1609-final/` | linux-content-receipt | `/*/Antiphon.Tests.Scripts/(VolumeRetirementContentTests*)\|(VolumeRetirementReceiptTests*)/*` | C1-C14,J1-J10,H7 | all 25 listed methods, 0 failed/skipped | 25 | 27 | true |
| CP-4 | S2-S5 | CP-3 | linux-host-wrapper | `/*/Antiphon.Tests.Scripts/(VolumeRetirementHostProofTests*)\|(VolumeRetirementWrapperTests*)/*` | H1-H6,W1-W6 | all 12 listed methods, 0 failed/skipped | 12 | 3 | true |
| CP-5 | S2-S5 | CP-3 | linux-git-core | `/*/Antiphon.Tests.Scripts/RemoteScriptContractTests/(C1105_Git_audit_recovery_tips*)\|(C1105_Git_audit_seed_content*)\|(C1105_Git_audit_index_content*)\|(C1105_Git_audit_object_completeness*)` | R3 | all 30 argument results, 0 failed/skipped | 30 | 1 | true |
| CP-6 | S2-S5 | CP-3 | linux-git-locations | `/*/Antiphon.Tests.Scripts/RemoteScriptContractTests/C1105_Git_audit_hidden_locations` | R4 | all 132 location results, 0 failed/skipped | 132 | 3 | true |
| CP-7 | S2-S5 | CP-3 | linux-evidence-shapes | `/*/Antiphon.Tests.Scripts/(RollingVolumeRecycleScriptTests*)\|(RemoteScriptContractTests*)/(C1594_Antiphon_evidence_gitdir*)\|(C1335_Mirror_owner_stamp_in_linked_admin*)` | R5 | all 52 argument results, 0 failed/skipped | 52 | 2 | true |
| CP-8 | S2-S5 | CP-3 | linux-recycle-guards | `/*/Antiphon.Tests.Scripts/RollingVolumeRecycleScriptTests/(C1008_Same_sha_and_partial_retries_are_safe*)\|(C1008_Option_manifest_is_strict*)\|(C1008_Wrapper_option_manifest_is_strict*)\|(C1008_Present_or_unknown_temp_keeps_null_refusal*)\|(C1008_Busy_routed_and_land_in_flight_refuse*)\|(C1008_Preview_helper_removal_is_owned*)\|(C1008_Wrapper_retired_absent_null_is_accepted*)\|(C1008_Retired_absent_null_is_accepted*)\|(C1008_Host_proof_admits_only_an_authenticated_private_origin*)\|(C1008_Host_proof_file_omits_credentials*)\|(C1008_Documentation_and_transport_pins_match*)` | R1,R6,doc transport, unrostered retired-absent sibling | all 11 methods, 0 failed/skipped | 11 | 9 | true |
| CP-9 | S2-S5 | CP-3 | linux-container-guards | `/*/Antiphon.Tests.Scripts/RetiredTempContainerHostTests/*` | R2, complete host topology/census/crash roster | all 22 methods, 0 failed/skipped | 22 | 15 | true |
| CP-10 | S2-S5 | CP-3 | linux-real-docker | `/*/Antiphon.Tests.Scripts/VolumeRetirementDockerTests/*` | N1-N3 | all 3 methods and owned cleanup, 0 failed/skipped | 3 | 4 | true |
| CP-11 | S2-S5 | `tests/Antiphon.Tests -> bin-c1609-windows/` | windows-host-wrapper | `/*/Antiphon.Tests.Scripts/(VolumeRetirementHostProofTests*)\|(VolumeRetirementWrapperTests*)/*` | H1-H6,W1-W6 native Windows | all 12 methods, 0 failed/skipped | 12 | 5 | true |
| CP-12 | S2-S5 | CP-11 | windows-unit-roster | `/*/Antiphon.Tests.Scripts/WindowsUnitPlatformAdmissionTests/(C1210_Guard_matches_native_platform*)\|(C1210_Native_linux_entries_guard_before_work*)\|(C1210_Roster_closure_matches_source_gates*)` | Windows roster closure | all 3 methods, 0 failed/skipped | 3 | 1 | true |
| CP-13 | S2-S5 | CP-11 | windows-doc-pins | `/*/Antiphon.Tests.Infrastructure/DockerStackDocumentationTests/*` | R9, all owner-doc pins including census retry | all 16 methods, 0 failed/skipped | 16 | 1 | true |
| CP-15 | S2-S5 | CP-3 | linux-container-script-guards | `/*/Antiphon.Tests.Scripts/RetiredTempContainerScriptTests/*` | R7, all wrapper/bridge/preview methods | all 16 methods, 0 failed/skipped | 16 | 3 | true |
| CP-16 | S2-S5 | CP-3 | linux-deploy-contracts | `/*/Antiphon.Tests.Scripts/RemoteScriptContractTests/(C0995_Deploy_temp_checks_host_absence_before_checkout*)\|(C849_Deploy_temp_names_saved_import_when_seed_is_missing*)\|(C849_Deploy_prepares_and_verifies_before_acceptance*)\|(C849_Deploy_temp_observes_inaccessible_docker_mountpoints_with_sudo*)\|(C849_Docker_volume_paths_have_no_bare_host_reads*)\|(C849_Seed_publishes_complete_payloads_before_its_marker*)\|(Temp_deploy_uses_its_own_project_and_the_inspected_grok_store*)\|(Nested_lane_never_uses_sudo_or_python*)\|(C1067_Nested_lane_sudo_guard_is_sensitive*)\|(Deploy_parent_creates_the_github_token_directory_without_reading_it*)\|(C849_Saved_donor_archive_is_checked_and_imported_without_a_container*)\|(C849_Deploy_ordering_contract_is_pinned*)\|(C912_Cold_runner_verification_uses_mounts_and_writability_not_payloads*)\|(C912_Cold_exception_never_weakens_maintenance_or_donor_gates*)\|(C973_Cold_marker_readers_accept_pruned_seed_image_and_refuse_invalid_markers*)` | R8, deploy readers/ordering/privileged metadata | all 23 results from 15 methods, 0 failed/skipped | 23 | 3 | true |
| CP-17 | S2-S5 | CP-3 | linux-apply-gate | `/*/Antiphon.Tests.Scripts/VolumeRetirementApplyGateTests/*` | default-off apply gate at the delete loop | all 4 methods, 0 failed/skipped | 4 | 10 | true |

## Rollout and compatibility

1. This final Plan round incorporates both prior design reviews. Code executes
   the stated fail-closed defaults; ordinary implementation Review still validates
   the fail-open/lock boundary, schema, classifier, attestations and checkpoint
   evidence. No further Plan/TestDesign is required. No production volume audit
   or deletion is part of this Plan or its Code fixture tests.
2. Implement the versioned scanner/proof/coordinator with the tests committed red
   first. Extend the landed CARD-1607 helpers deliberately, retaining current sanitization. Keep v1 main
   recycle behavior. Complete both native lanes before release credit.
3. Publish current compatible tooling and the audit descriptor through the normal
   reviewed deployment path. Observe installed runtime/protocol directly. A code
   land does not activate host scripts or an image. No runner restart is inherently
   required for this maintenance-only path.
4. **Preview-only first, before enabling apply/deletes.** After the known retired
   test-volume preview, commission one read-only preview of the eligible live
   retired set using current installed tools. Record complete findings, total
   repositories, exact-template matches, unclassified crashed-test repos outside
   the CARD-1605 template (including `/tmp`), header-shape refusals and counts/rates
   by reason. Record unknown/incomplete denominators explicitly; do not call them
   zero. Retain both JSONL and proof.md and review this refusal prevalence before
   enabling apply. Out-of-template leftovers stay refused by design; recover them
   externally or commission a separately reviewed classifier, then preview again.
   No apply follows an incomplete or refused preview. The original four prototype
   volumes are gone and cannot supply a live replay.
5. Enable current-proof `retire-temp` and the explicit manual entry. Preserve
   historical receipts unchanged, generated receipts ignored and main/cache
   volumes untouched. Existing callers with a creating `-Sha` continue to work;
   retirement treats it as optional provenance, not a lookup requirement.
6. Rollback before apply can switch current tooling to a reviewed compatible
   version and repeat preview. After any delete, rollback means reporting the
   irreversible partial outcome and preserving the rest; never recreate empty
   volumes or silently fall back to the old broad Compose deletion path.

## Master/rc port and ordering

**Reciprocal dependency: CARD-1609 first, CARD-1615 second, on master and rc.**
Read CARD-1615's round-3 plan
`docs/superpowers/plans/2026-10-30-card-1615-deploy-land-deadlock-plan.md` at
`ef75a59413125a3ba92424ade043104abc7e1700` (task `7dd90600` snapshot), D-11:
it requires both landed CARD-1609 SHAs before its Code dispatch. This is a
coordination contract, not a claim either Code change has landed. Its current
D-3 explicitly preserves bound Held/Queued/Running/NeedsResolution land refusal
and adds no census exemption; do not resurrect the earlier self-held-land idea.

| Overlap | CARD-1609 ownership and handoff to CARD-1615 |
|---|---|
| `scripts/deploy-server2.ps1` phase admission, census call sites, retire-temp and deploy-temp | Land current-proof retirement/pre-clear gate first. CARD-1615 adds its controller identity/workspace/version admission around the landed layout, preserving these guards and the bounded census contract. |
| `scripts/c590-remote.sh` bound-land/census and retirement admission | CARD-1609 changes only v2 transient read retry and effect-boundary proof. CARD-1615 consumes/probes the resulting census; neither card exempts a pending bound land. |
| `docs/docker-stack.md` manual-retire evidence and census/rollout guidance (review's old :473/:513 region) | CARD-1609 documents v2 proof, Close, and the existing `.antiphon/rolling-server2/manual-retire-<UTC>` path under the current control checkout. CARD-1615 owns the subsequent `<controller-root>` relocation and Worktree-controller instructions; it must retain v2 custody/digests/close rules. |
| `docs/orchestration-loop.md`, `DockerStackDocumentationTests.cs`, wrapper/host fixture consumers | CARD-1609 lands its narrowly changed retirement authority and complete affected pins first. CARD-1615 reconciles controller wording/pins against that landed source and executes its own affected checks. |

Do not include CARD-1615's self-held-land/controller admission, controller-root
relocation, active-rollout marker or canonical activation changes in S1-S5.
The caller records both CARD-1609 publication SHAs, confirms the rc port follows
top-up 38, then dispatches CARD-1615 Code against those landed bases. A later rc
pick must not restore older wrapper ordering or overwrite either safety gate.

The caller's round-2 brief defines top-up 38 as the combined CARD-1613 / audit
repairs / CARD-1614 slice A / speed-ups S1 bundle, with ONE land-graph approval
regeneration. CARD-1609 belongs in a **later top-up after 38**; do not splice it
into that bundle or make top-up 38's retire-temp depend on unimplemented v2.
Until the later tooling is reviewed, ported and activated, use the then-reviewed
existing retirement path and preserve any refusal. CARD-1607 is already on both
lines at the full SHAs recorded above; do not cherry-pick it a second time.

S1-S5 add/change script bodies, test .cs files/fixture scripts, the Windows skip
roster and owner documentation. Under this plan they do **not** change .csproj,
.sln, Directory.Build.props/targets, package/SDK definitions, project references,
land-verification policy or its approval manifest. Existing default Compile and
fixture source-root access suffice. The current audit descriptor is host runtime
configuration installed by the deployment scripts, not an MSBuild definition.
No definition-driven approval regeneration is planned for CARD-1609. Code must
report any required scope expansion before changing a definition; integrate such
an expansion only in the later top-up and obtain its own normal graph approval,
never reuse or silently regenerate top-up 38's approval to cover new definitions.

1. Code from the then-current `origin/master` baseline with
   landed CARD-1607. Keep S1 assertion-red before S2-S5 fixes. Complete named Linux
   and native Windows checkpoints, independent Review, and authorized landing to
   master. This Plan performs none of those implementation/landing operations.
2. After top-up 38 completes its own reviewed integration, the caller commissions
   rc porting of the reviewed S1-S5 commits onto the then-current rc/2026-10-27
   tip (or its explicitly chosen successor). Plan-only commits/docs are not rc
   picks; retain this plan as the source artifact on master. Operational owner docs
   travel with their product change where rc's documentation policy allows them.
3. Before each rc pick, simulate its patch against the current accumulated candidate
   (three-way patch/merge-tree with that commit's parent as base), record conflicts
   and exact source/target SHA. Resolve only in the commissioned port branch under
   normal Review; never merge/rebase rc or silently drop tests. The four existing
   production scripts are identical at the two inspected tips, but that is not a
   promise about the future candidate. Reconcile later retirement/census edits.
4. Run the affected native checkpoints on the ported candidate, preserve source
   attribution, and pass normal rc gates. A master green receipt is not rc execution
   evidence. Inspect definition drift and use the standard graph check; no new
   approval regeneration is expected under the scope above. No tag/publish here.
5. Activate through the reviewed maintenance-tool deployment path. Before the first
   live v2 preview directly compare installed script hashes and audit-current.json
   protocol/policy plus resolved image digest with the reviewed rc artifact; verify
   the live server/runner version endpoints and intended rc SHA where applicable.
   A commit land alone is not activation. Record the observation in the operation.
   Only then commission a read-only preview of an eligible retired target set.

## Operator questions and independent review gates

The plan is written under D-1..D-21 rather than waiting for answers. These are
explicit review choices, not permission questions during this Plan:

- Confirm D-2 retains reflog and original tag-object protection despite the manual
  prototype's weaker proof. Default: retain; abandoning unpublished reflog work
  or changing publication semantics needs a separate operator decision/card.
- Confirm D-16: proved FETCH_HEAD entries are harmless; unpublished/unknown entries
  refuse. Recovery is external publication or explicit recovery/removal, then preview.
- Confirm D-17's exact CARD-1605 template classifier. Recommended: admit only the
  reviewed seed content; broader fixture identities/paths stay refused until reviewed.
- Decide CARD-1105 replay 3b gone-tracking after a rebase land (D-18). Recommended:
  retain original OID protection and publish recovered originals to an origin archive
  branch; do not equate landed patches with publication or automatically prune refs.
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

## Round-2 validation record

Historical record retained unchanged below; its 14-row/294-execution totals and
then-current checkpoint digest are superseded by the round-3 validation record.

Only this plan was edited. All ten defects and mount/performance/OS-witness
advisories are incorporated; no implementation, native test execution, live audit
or volume operation was performed. The original corrected 11-row table imported
at `94498d73cecbda04a6eddb7439e0fc07c3c84e0f`; the expanded table imported at
`e5434c38e9bf73f12cfbb3b50d26208617ad1e4a` with exit 0, 14 rows.
Checkpoint-section SHA-256: `2fb5abbe7ed80a3eec55044831d3288843faa17ac09965ea31cb59af8d6f5c57`.
This final prose-only validation amendment leaves that section unchanged.

The leased command was:

`pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1609-plan-final-import-new-output -- dotnet run --project tools/Antiphon.Checkpoints --no-build --property:OutputPath=bin-c1609plan/ -- import --plan docs/superpowers/plans/2026-10-29-card-1609-content-derived-volume-retirement-plan.md --out /work/worktrees/task-02c3c411/.antiphon/card-1609-plan-r2-final.yaml`

For a rerun choose a fresh owned ignored output path. The first import bootstrapped
the tool under the same host slot gate (7 s held, compile warnings, no errors).
One later import refused `unrecorded-existing` while trying to reuse the original
output; it did not invalidate table parsing. Repeating with the fresh path above
succeeded (3 s held). Both successful imports emitted only the expected CP-11
duplicate-build manifest warning; Windows must build locally, so cross-host reuse
of CP-3 is intentionally rejected. Generated YAML and tool outputs are not commits.

Static roster verification against the pinned master found 29 distinct selected
existing method operands with no suffix collisions; the five proposed new classes
and two replacement C1609 container methods are absent as expected. All existing
selected class declarations are unique (quoted class names in test strings are
not declarations). Recounted recovery/seed/index/object results = 10/11/5/4,
hidden-location fixture rows = 132, evidence-name arguments = 40, mirror-owner
arguments = 12. Planned totals are 294 executions across the three lanes/groups
above; these are expectations, not executed test counts.

`git diff --check` passed. After `git fetch origin master`,
`git merge-tree --write-tree origin/master HEAD` was conflict-free against
`98fc3ceb6440a17b68cafe3ecc21c76d190b18ca` at the validated tip. Final task-branch
publication and merge simulation are reported with the final full commit SHA.

## Round-3 validation record

This round changes only this plan, in place. No product code, product test run,
live audit, deployment or volume operation was performed. All four re-review
defects and the preview-only advisory are incorporated; no further Plan or
TestDesign is required. Code starts from then-current origin/master under the
stated fail-closed defaults. Independent implementation Review remains required.

Read the full round-2 re-review before work. Re-fetched origin/master and checked
source at `98fc3ceb6440a17b68cafe3ecc21c76d190b18ca`; inspected wrapper clear order,
retirement-epoch guard, host proof's `origin-present` refusal, fixture creation,
all added test dispositions and their consuming harnesses. The four production
scripts remain byte-identical against the recorded rc snapshot
`ade54222543381e2e1b31ad79d8ae33a4035f70b`. Desktop historical receipts remain
unavailable here; their evidence is explicitly attributed to the earlier plan
and reviews, not represented as a new observation.

The committed manifest at `18d2c19cce0b93534aaaeb650e8305a30deda484` imported
successfully through the host build-slot gate, exit 0, **16 rows**. Command:

```sh
pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1609-r3-import -SlotWaitMinutes 5 -- dotnet run --project tools/Antiphon.Checkpoints --property:OutputPath=bin-c1609r3/ -- import --plan docs/superpowers/plans/2026-10-29-card-1609-content-derived-volume-retirement-plan.md --out .antiphon/card-1609-plan-r3.yaml
```

Unedited essential output:

```text
BUILD SLOT granted lease=91ef2745-e3c8-4ed8-be93-34ede1421a40 waited=0s maxcpucount=6
WARNING CP-11: Build 'bin-c1609-windows' duplicates 'bin-c1609-final' from CP-3 (same project 'tests/Antiphon.Tests', After 'S2,S3,S4,S5', Repeat 1); write 'CP-3' in the Build cell to reuse it and save one build
imported 16 rows -> .antiphon/card-1609-plan-r3.yaml
BUILD SLOT released lease=91ef2745-e3c8-4ed8-be93-34ede1421a40 held=10s
```

The isolated tool bootstrap emitted five compiler/analyzer warnings and no errors;
no product tests ran. The CP-11 warning is intentional: native Windows builds
cannot reuse Linux output. The wrapper also noted that dotnet run's implicit
build does not take the grant's maxcpucount argument; it remained slot-leased.
Use a fresh ignored output for rerun. Both owned `bin-c1609r3/` directories
(HostCleanup and Checkpoints) and the generated YAML were removed after exit.

The final checkpoint section is unchanged by this validation prose:
SHA-256 `8454ccaf80e6096375615923fb96c7de3910489815ce0221e1a4767fe61a8e9b`
(from `### Checkpoints` through the bytes immediately before `## Rollout`).
All 16 estimates are whole minutes. Static floor sums are **360 planned** results:
S1 4 red; Linux 325 green; Windows 31 green. Estimated group costs are 7/26/7
minutes. These are planned counts/costs, not execution receipts or measured p95.
Source recount: Script 16; Host 22 after two one-for-one replacements; Docs 16;
CP-16 15 methods/23 results (C1067 four, C973 six, 13 singleton methods).
R3/R4/R5 floors remain 30/132/52. All selected existing class and method prefixes
were checked against the fetched source: zero suffix collisions; the five new
classes remain absent/reserved. Recheck new declarations and TRX at Code time.

The exact seed tree/blobs were independently recomputed with Node SHA-1 and
Git `hash-object` over the specified raw bytes, agreeing with D-17; no objects
were written. PC-SeedHeaders is designed, not executed by Plan. Live GETs for
runner defaults and runners confirmed the lane observations above; no host pin
or fleet address is prescribed.

`git diff --check` passed. `git merge-tree --write-tree origin/master HEAD`
was conflict-free at the imported commit (tree
`19e909b901e93c0b3ae54f999e5880340f5986f3`); final-tip fetch/simulation and remote
publication are repeated and reported at settlement. Only this Markdown path is
in the task delta. Slice publications, all fast-forward on the assigned branch:

- Skeleton: `278561924db5d5e5c200e6131b3cf002435fe49f`.
- D-1 pre-clear admission/journal closure: `1107de7b132d45c0a94d797ad649c633ec5edc6c`.
- D-2 test dispositions/checkpoints: `e7eabbf1eab61166f739da5ee6bf13b8c455a2fe`.
- D-3 CARD-1615 order: `5e1ae4e0acf0c298610c25d9b5ec3a7aa9e6935b`.
- D-4 exact seed bytes/preview-only rollout: `18d2c19cce0b93534aaaeb650e8305a30deda484`.
