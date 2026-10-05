# CARD-1058: bind host jq qualification to one open file

Date: 2026-10-05. Plan task: `c3e154a8-0f5e-4eec-9b75-7e17cbc84d85`.
Original inspected source: `14316228eaac5afd8a404d75fca69d7a9fa94382`.
Repair task: `8ec6b28e-2cf0-4e25-b880-4ca75764d9ed`; repair base / prior
TestDesign audit: `ef153c8832e4fe6cb7971e59212e03bd04265ce3`.
Card: Antiphon `8650f8ec-02f8-4c1d-a45b-8fc243e38dfa`.

## Outcome and stage boundary

Reject a multiply linked canonical host jq before executing it. Open the admitted
file once, verify its identity, hash and execute through that retained descriptor,
and withhold the successful proof if the canonical leaf changes during the probe.
Keep the existing distinction between a functional preinstalled jq and a freshly
installed, pinned artifact. The wrapper continues consuming a point-in-time host
observation; it does not make a second remote pathname observation.

This is a medium change to an existing privileged-install boundary. The separate
TestDesign audit rejected composite controls. This repair brief explicitly asks
Plan to complete that verification design and hand off to Code when complete.
D-1 through D-8 preserve the selected compatibility contract; D-9/D-10 fix the
observation and control structure. There is no unresolved policy choice or
unapproved product default. Two Code slices remain within 30-60 minutes.

This Plan changes only this document. No host installation, deployment, image
rebuild, live race experiment, mutation, build or test run is claimed.

## Ground truth

The original Plan read live CARD-1058, CARD-1025, CARD-1054 and CARD-1040 and
their landed plans. This repair re-read the helper, affected tests/fixture,
wrapper boundary and full TestDesign audit at the repair base. Line references
below remain to the original source; historical card results are not new
measurements. The implementation is still absent at the repair base.

| Card assumption | What the code/evidence actually does | Consequence |
|---|---|---|
| A hardlink at the canonical leaf still qualifies. | `scripts/server2-host-jq.sh:23-60` admits a resolved canonical regular, executable, non-symlink file. It never reads link count. `readlink -f` cannot identify other names for the inode. | The premise is correct. Require link count exactly one, before any jq execution, in check and both provision qualification paths. |
| A swap after the symlink check can execute foreign bytes. | The helper invokes `"$resolved"` for version, both predicates and JSON emission, and hashes/stats that pathname after executing the predicates. Each access can resolve a different inode. | Bind all four execution sites and the hash/metadata to one descriptor; another late pathname check alone is insufficient. |
| Existing jq already has to match the release pin. | CARD-1025 D-3 and `C1025_Check_qualifies_deployment_shell` explicitly admit a functional `jq-other`. Only the freshly installed branch enforces the fixed digest/version/owner/mode. | Do not silently turn this defence-in-depth card into a host package-version migration. Existing mode records its actual digest; installed mode must check the pin before execution as well as retain final validation. |
| Link-count rejection can be inserted without changing publication. | `server2-host-jq.sh:144` publishes with atomic no-clobber `ln -T`. The staging name remains until the EXIT trap, so the new destination has **two** names during final qualification. | Remove only the owned staged file name after successful publication and before qualification; retain the destination and existing no-clobber publication. |
| The wrapper only compares path strings. | `scripts/deploy-server2.ps1:163-201` also validates exact JSON shape/types, lane/mode, semantics and installed pin, then binds a fresh durable receipt to source/run/phase/time. It does not stat the remote leaf. | Preserve this contract and schema. A desktop/local stat is the wrong filesystem; a second SSH stat cannot establish what a prior process executed. |
| A successful preflight protects later recycle execution against directory writers. | Later consumers resolve jq again; `c590-remote.sh` retains its independent `RecycleToolsMissing` guard. No descriptor is transferred from the helper to later phases. | Explicitly retain the canonical-directory and file-content integrity trust boundary. Do not advertise end-to-end race-free deployment. |
| The dependencies are still unlanded. | CARD-1025 is Done, with S1 at `dc7d1794b` and S2 at `fc3b3e1e8`; its card records a historical read-only live host check. CARD-1054 is Done at this base, with 18 ordinary results reported green. | Extend the landed helper; do not recreate either implementation. Their unexecuted PCs remain separate obligations. |
| CARD-1054 fixes the stronger inode/byte custody contract too. | `docker/session-runner-grok/verify-codex-image.sh:95-103` has PATH/canonical-leaf admission followed by pathname execution. It explicitly trusts directory integrity. | This card's implementation scope is the **host helper**. Keep the image limitation explicit; neither CARD-1054 nor this host change proves image activation or stronger image custody. |
| Ten proposed controls cover the planned guards independently. | The audit at `ef153c883` found 20 guard rows; PC-1, PC-3 and PC-10 removed multiple checks. | Freeze the observation order and 29 single-guard controls; 35 ordinary executions across three checkpoints. |
| The existing fixture already proves inode identity. | `HostJqFixture` uses real private paths, hardlink-capable tools and native Bash, but its `stat`/`sha256sum` shims synthesize owner/pin evidence for installation. | Use real device/inode/link-count/hash observations for the new identity tests. Preserve narrowly scoped owner/pin simulation only where a private non-root install fixture needs it. |

Owners: [project conventions](../../project-context.md),
[testing/build](../../testing-and-build.md), [Docker rollout](../../docker-stack.md),
[orchestration](../../orchestration-loop.md), [HTTP](../../ops-http.md),
[card lifecycle](../../agent-card-lifecycle.md). Dependency designs:
[CARD-1025](2026-10-04-card-1025-host-jq-prerequisite-plan.md),
[CARD-1054](2026-10-05-card-1054-jq-path-qualification-plan.md),
[CARD-1040](2026-10-04-card-1040-jq-prerequisites-and-unit-timing-plan.md).

## Decisions

- **D-1 — Reject link count other than one.** Check both the admitted leaf and
  the opened file. A regular canonical file with an additional name refuses
  even if its contents happen to match the pin. Aliases that are symlinks **to**
  the canonical single-link file remain valid. Rejected: treating `readlink -f`
  as hardlink detection, allowing a hardlink just because it is currently
  root-owned, or deleting someone else's extra link to repair admission.
- **D-2 — Retain one descriptor for the complete qualification.** Reserve FD 8
  in the helper, separate from its existing provision-lock FD 9. Capture the
  canonical leaf's non-following device/inode identity, open it for reading,
  then compare the descriptor's identity with both that observation and a fresh
  non-following leaf observation before executing anything. Require regular,
  executable, non-symlink canonical leaf, valid numeric device/inode observations,
  and link count one; malformed/unavailable metadata refuses. Use the retained
  `/proc/self/fd/8` for hashing, metadata and all jq invocations. Rejected:
  re-running `realpath` before each call, reopening the canonical path after
  hashing, or claiming Bash's ordinary redirection supplies `O_NOFOLLOW`.
- **D-3 — Preserve existing-mode compatibility; move installed pin admission
  ahead of execution.** For an existing functional canonical jq, record the
  digest of the opened file without comparing it to the install pin. For the
  just-published artifact, require that same descriptor's digest equal the
  existing fixed pin **before** version, predicates or formatter execute.
  Retain final version/owner/mode and installed proof checks. Rejected: imposing
  the release pin on all check/no-op calls, accepting a post-execution mismatch
  as adequate protection for a new install, or adding a caller-selectable pin.
- **D-4 — Preserve atomic no-clobber publication.** After successful `ln -T`,
  unlink only `$stage_root/jq`, require success, then qualify the destination.
  The standing lock remains held. EXIT cleanup still removes only this call's
  temporary directories. A failure leaves the published destination for
  diagnosis and produces no success receipt; never unlink the destination to
  roll back. Rejected: `mv` that may overwrite a racer, direct copy to the final
  path, permitting link count two in production admission, or skipping the
  final check. A concurrent read-only check may refuse during the short
  publication interval; it must not accept a two-link file or retry silently.
- **D-5 — Buffer the proof, then validate identity before releasing stdout.**
  Format the unchanged schema-1 JSON using FD 8 into a shell variable. Recheck
  the canonical leaf against the retained descriptor identity, establishing
  regular/non-symlink state, and check both leaf and descriptor executable
  access and single-link condition before printing the buffered document.
  Persistent replacement during any probe therefore cannot yield a success
  proof. Initial path refusals keep their existing typed path observation;
  new link/descriptor/change failures use existing `HostJqInvalid` (exit 2,
  no success JSON). Formatter failure keeps `HostJqProofUnavailable`.
  Rejected: streaming success before final admission, formatting diagnostics
  with an unapproved jq, adding receipt fields with no consumer need, or
  accepting an old pathname hash as the executed-file digest.
- **D-6 — Do not add wrapper re-stat.** The helper is the remote observation
  authority; the wrapper keeps strict proof parsing and durable receipt
  admission. Another SSH round trip observes a later pathname, introduces
  another check-to-use window and cannot authenticate the executed inode.
  Rejected: local PowerShell `Get-Item`, remote stat as an independent execution
  proof, and adding a new runtime/API for this small change. A schema-valid
  forged success from a compromised SSH/helper boundary remains outside the
  wrapper's threat model; do not claim it becomes cryptographic attestation.
- **D-7 — State the remaining trust boundary precisely.** `/usr/local/bin`,
  its ancestors, the canonical inode's contents and the host toolchain remain
  trusted. A descriptor pins an inode, **not immutable bytes**: in-place writes
  through an existing writable handle can change it. A writer can also plant
  an otherwise admissible regular file before qualification, perform an ABA
  swap between observations, or replace the leaf after proof emission and
  before a later consumer. This design limits hardlink admission and pathname
  substitution during this helper; it does not defend a compromised host
  administrator. Rejected: claiming repeated hashes seal a writable inode,
  copying binaries into a new privileged cache, native `memfd`/`fexecve`
  machinery, or hardening every later jq consumer under this card.
- **D-8 — Keep implementation and verification local and bounded.** Use the
  existing helper and private native-Linux fixture; no new service, package,
  production test flag, global PATH edit, root fixture or image rebuild.
  Preserve CARD-1040 S2's fifteen-method/activation gate and CARD-1054's row
  contract. Ordinary Code/Review uses only the checkpoint selection below;
  deliberate PCs require separate post-land Mutation. Rejected: whole Unit,
  full installer/image/legacy-roster reruns, timeout increases, retries, or
  treating ordinary green as PC evidence.
- **D-9 — Freeze separate native observation boundaries.** Use P0/O/F0/L0/H,
  four FD calls, then L1 and F1 in the order specified below. Keep five separate
  link admissions and two final live executable-access checks. A private native
  stat barrier can then isolate each observation. Rejected: simultaneous composite
  final checks, broad mutations, and counting persistent rejection as proof of
  each redundant observation. Final non-symlink/regular state follows from the
  non-following leaf identity and retained regular inode; do not invent an
  impossible same-inode type mutation.
- **D-10 — Factor identity observation errors, not policy guards.** A directly
  called snapshot reader owns stat status/framing/device/inode syntax. Each
  caller retains its exact-one-link and individual device/inode comparisons.
  This keeps failure handling uniform and avoids copied propagation guards.
  Raise the PC cap from ten to 29 and add eight focused test methods. Rejected:
  hiding multiple removals behind one PC ID, permissive numeric coercion, a
  canned inode/hash fixture, or retaining the rejected verification cost.

## Implementation shape

Make descriptor ownership explicit in `scripts/server2-host-jq.sh`: close any
previous qualification FD before each new attempt, open once only after PATH
and initial canonical-leaf admission, and retain it until formatting/final
admission finishes. Failed/missing qualification releases it; process exit also
closes it. Keep the provision lock on FD 9 and compose, rather than replace,
the existing staging cleanup traps. Descriptor/procfs/open/stat/hash failures
are invalid qualification, never the missing state that authorizes installation.

Use numeric, unambiguous stat fields for device, inode, link count and file
metadata; inspect the leaf without `-L`, and inspect the descriptor target with
`-L`. Compare the device/inode pair, not inode alone. Do not parse localized
file-type descriptions. Preserve original `lookupPath` and canonical `path` in
the receipt: `/proc/self/fd/8` is an internal execution handle, not its public
path identity. Keep existing refusal-path escaping and actual-child PATH rules.

All four jq execution sites must use the descriptor: version, true predicate,
false predicate and JSON formatter. Hash before the first of them. The newly
installed call receives an internal expected-digest argument/state; initial and
post-lock existing checks do not. This is not another CLI mode or an environment
override. The inherited descriptor must work with the native jq binary and the
fixture's shebang executable. If the lane cannot provide that behavior, refuse;
do not fall back to executing the mutable canonical pathname.

The final identity observation is the receipt's point-in-time admission. It is
not atomic with subsequent printing or deployment. No new descriptor transfer,
lock for unrelated writers, syscall-level no-follow claim or persistent custody
promise is introduced. `scripts/deploy-server2.ps1` and `c590-remote.sh` require
no production changes.

## Placement and dependencies

Read `GET /api/runner-defaults` and `GET /api/session-runners` on 2026-10-05 at
11:09 UTC through the configured task API during this repair. Defaults revision
was 2; the catalogue exposed eligible Linux and Windows lanes and one unavailable,
draining entry. The Linux lane reported full occupied capacity at that instant.
These are scheduling observations, not a reservation or image qualification.
Re-read both routes before dispatch; embed no fleet hostname.

Plan/TestDesign need no OS pin. Code, ordinary Review and these filesystem PCs
need the **native Linux host-jq fixture lane** (Bash, procfs FD execution, GNU
coreutils and real same-filesystem hardlinks), so use `-Platform Linux` for that
work. Omit `-Runner` unless commissioning one specific host. Omit `-Platform`
for portable documentation work; `-Platform Any` clears an inherited pin.

The fixture also needs its already required native jq, pwsh, git and node.
Observe their actual child environment; missing tools are a prerequisite failure,
not skipped proof and not permission to install into the standing runner.
Tests use only private copies/links beneath their scratch root. No shared
canonical jq, production runner endpoint, real SSH host or Docker daemon is
modified. Confirm the implementation base contains the landed CARD-1025 and
CARD-1054 source; the inspected base does. Do not rebase this task branch.

## Slices and files

### S1 — Single-link admission and compatible publication (35-55 minutes)

Change `scripts/server2-host-jq.sh` to reject canonical link count other than one
before invoking jq, including the existing/no-op and locked qualification paths.
Change its successful publication sequence to remove the owned staging name
before final qualification, failing closed on removal failure. Preserve no-clobber,
lock and cleanup behavior.

Extend `tests/Antiphon.Tests/Scripts/HostJqPrerequisiteScriptTests.cs` with M-1
and M-2 plus M-11 below. Establish the reusable native tuple/trace/barrier and
child-custody support in `HostJqFixture`, with actual hardlinks and both staged
unlink failure forms. S1 captures P0 for its initial-link guard; S2 extends that
snapshot reader and phase ledger. Read real link counts; never fake `nlink=1`.
Keep the root/owner and install pin seams limited to the existing simulation.
Commit/push S1, then run CP-1 once. Commit any required fix before a justified
rerun; retain failed evidence and the SHA it actually tested.

### S2 — Descriptor-bound qualification, proof boundary and docs (50-60 minutes)

Change the same helper for D-2, D-3 and D-5, retaining S1's rules. Extend the
same test file with M-3..M-10 and M-12..M-18, reusing S1's barrier/custody
support to expose native open/hash/execute/final-observation ordering. Retarget
owner/group/mode faults to FD metadata. Explicitly update the old final-digest
vector to the new pre-execution gate (see verification design); do not claim
an earlier refusal reached the now-redundant final scalar comparison.

Update the host prerequisite paragraphs in `docs/docker-stack.md` and the
**outer deployment host** entry in `docs/testing-and-build.md` to describe
single-link qualification, descriptor scope, unchanged existing-version policy,
point-in-time receipts, and explicit remaining integrity assumptions. Link this
plan. Keep the image/child qualification paragraphs explicit that the image
probe still trusts directory integrity; do not imply this host fix changed it.
If amending CARD-1025's historical plan, append only a short supersession link
for this host behavior; do not rewrite old results or discharge its 67 PCs.

Read but leave `scripts/deploy-server2.ps1`, `scripts/c590-remote.sh`, image
Dockerfiles/probe, CARD-1054 tests and CARD-1040's frozen fifteen-method manifest
unchanged. Coordinate shared owner-doc edits with the CARD-1040 owner.
Commit/push S2 with docs, then run CP-2/CP-3 as one serial checkpoint-tool group.
No extra build/test is needed for subsequent report-only prose; keep its source
provenance honest. Code/Review run the full-range evidence-diff policy check.

## Verification design

Repair task `8ec6b28e-2cf0-4e25-b880-4ca75764d9ed`, based on TestDesign audit
`ef153c8832e4fe6cb7971e59212e03bd04265ce3` (2026-10-05). The rejected proposal
and audit remain in Git history; this section supersedes both earlier checkpoint
tables and both earlier handoffs. The current brief authorizes completing this
verification repair in Plan and handing off to Code. **Frozen for Code:** two
slices, eighteen new non-parameterized methods, three narrow Linux checkpoints,
and 29 separately commissioned positive controls. No runtime result is claimed.

### Inspection

Re-read the complete `scripts/server2-host-jq.sh`, affected C1025 method bodies
and `HostJqFixture` in
`tests/Antiphon.Tests/Scripts/HostJqPrerequisiteScriptTests.cs`, the real wrapper's
`Invoke-HostJq` parsing/persistence boundary in `scripts/deploy-server2.ps1`,
and `tools/Antiphon.Checkpoints/Manifest/PlanTableImporter.cs`. Re-read the
TestDesign audit's complete fixture/guard inventory. The importer uses the first
exact checkpoint heading; there is now exactly one authoritative table.

Source-to-test boundaries: helper admission/publication -> V/R-1..3,11..15,17..18;
helper hash/pin/execution -> V/R-4..9; helper emission and real wrapper receipt ->
V/R-10,16. The previous audit's image and later-consumer findings remain exclusions,
not new measurements by this repair. Owners are linked above.

Missing setup is implementation work: none of M-1..M-18 exists yet. The current
fixture's `Inode` returns only `%i`; healthy existing jq is untraced; installation
hashing synthesizes the release pin; there are no descriptor/barrier witnesses.
Build one reusable snapshot/barrier/trace fixture and use small vector loops.
Do not copy eighteen separate subprocess harnesses. Extend its ignored runtime
inventory and process custody before adding the dependent test methods.

### Observation order and isolation

Use these labels in fixture evidence, not production flags or environment hooks.
The qualification routine is shared by check, initial provision, locked recheck
and the newly published artifact. Reserve FD 8; retain FD 9 for the lock.

| Order | Observation/action | Admission and lifetime |
|---|---|---|
| P0 | Non-following canonical leaf stat after existing PATH/canonical/type admission | Capture real device, inode and link count; require exactly one link. |
| O | Open the canonical file once on FD 8 | Open failure is invalid (qualify result 4), never missing (3); no pathname fallback. |
| F0 | Following stat of `/proc/self/fd/8` | Require one link and compare device and inode separately to P0. Retain this admitted identity. |
| L0 | Fresh non-following stat of canonical leaf | Compare device and inode separately to F0 and require one link. This explicitly covers link growth after F0. |
| H | Hash the actual FD argument; read existing uid/gid/mode proof metadata from FD | Validate command status and digest syntax; installed mode additionally compares the pin here, before any jq call. Existing mode keeps its actual digest. |
| Qv, Qt, Qf | Version, true predicate, false predicate | Each executes FD 8. Preserve actual output and exit semantics. |
| Qj | Formatter | Execute FD 8 and capture complete JSON in a shell variable; formatter failure refuses with `HostJqProofUnavailable`. No success bytes yet. |
| L1 | Non-following final leaf stat, then leaf executable-access check | Compare device and inode to F0, require one link, then test `-x` on the canonical leaf. |
| F1 | Following final descriptor stat, then descriptor executable-access check | Require one link, then test `-x` on `/proc/self/fd/8`. Only after all checks print the buffered document once. |

All final observations occur after Qj has returned. Do not merge L1 and F1 or
change their order: their independent witnesses depend on this ordering. No
extra final hash is proposed; an open descriptor does not seal mutable bytes.
The regular/non-symlink final state follows from the **non-following** L1 identity
matching the retained, initially regular inode. An open inode cannot become a
symlink/directory in place. Repeating `-f`/`! -L` after that comparison is redundant,
not a new independently bypassable guard; do not claim a fictitious native
same-inode type-change PC. Likewise F1's device/inode cannot change while this
routine owns the unopened-again FD. Do not invent a final FD identity guard to
compare a descriptor to itself. Initial inherited type/PATH checks remain.

Use one shared, directly called identity-snapshot reader at P0/F0/L0/L1/F1,
with the target and follow/non-follow option as inputs. It captures GNU stat's
`%d %i %h`, checks status, checks a single-line three-token frame, then validates
device and inode as decimal strings. Link validity is the **exact string `1`**
at each of the five consumers; this simultaneously rejects missing, malformed,
zero and multiple-link values. Do not add a second numeric-nlink predicate that
would mask its policy control. Compare identities as strings, not arithmetic.
A suitable frame regex has three non-whitespace capture groups separated by one
space, anchored at both ends; numeric validation is separate. Stat emits this
format, so no localized type words or fuzzy splitting is needed.

The reader stores into caller-owned variables/arrays (for example, a Bash
nameref), not through `$(read_snapshot ...)`. Its shared status/frame/numeric
failures call `refuse HostJqInvalid` in the main shell. This avoids five copied
error-return checks that could be bypassed separately. The `stat` command itself
is captured with its exit status retained even when it writes valid stdout.
The H hash status/syntax checks remain separate. Existing uid/gid/permissions
validation is retained with its narrow owner simulation; it is not folded into
the new identity parser. No standalone procfs preflight or fallback is added:
FD target unavailability is the shared stat/hash failure path.

Fixture barriers wrap only private native stat/hash calls, private traced jq,
and the owned stage-unlink operation. Each invocation has a nonce and an
operation/target/phase ledger. Identify P0/F0/L0 through the qualification's
readlink/target sequence and L1/F1 through the formatter trace; validate that
sequence rather than using an unexplained global stat ordinal. Capture the real
operation result before its after-observation barrier; a separate named
before-operation barrier permits controlled changes before the native syscall.
Every selected barrier has exactly one hit/release. Unexpected/missing calls fail.

Record absolute-native-tool `%d`, `%i`, `%h`, mode and SHA-256, and independently
hash the original/replacement bytes in C#. FD observations run inside a child
that inherited FD 8; the C# parent's `/proc/self/fd/8` is unrelated. Retain both
native and supplied tuples for declared metadata substitutions. Native controls
use the copied native jq. Ordering controls use an observable shebang wrapper
that calls native jq and records original/foreign kind and argv separately.
Prove the foreign executable's healthy version/predicate/formatter behavior in
setup, then clear setup markers. Distinct original/replacement hashes and native
inode tuples are preconditions, not assumptions.

Rename the original to a private holding name during replacement; never retain
it through an extra hardlink in an identity test. This keeps its native nlink
one. Regular and symlink replacements remain until refusal except for H's
explicit transient swap. The test-only device cases change one numeric device
field after native stat, retaining the native inode; they prove comparison
logic, not a real second filesystem. Parser cases declare their substituted
field(s); their recorded native observations remain independently inspectable.

The following schedules isolate guards without suppressing their neighbors:

| Witness | Exact schedule and why another check cannot mask it |
|---|---|
| Initial link (PC-1) | Start with a real extra hardlink. Hold P0 after capturing nlink 2, remove only the test-owned alias, then return the captured tuple. Every later observation is nlink 1. Also retain persistent-hardlink refusal vectors. |
| Pre-open inode (PC-3) | Hold P0's original tuple, rename original aside and install a different regular executable before releasing. O/F0/L0 see the replacement consistently. Bypassing only P0-vs-F0 inode equality permits foreign execution. |
| Fresh inode (PC-11) | Hold F0's real tuple, replace the leaf, then release into L0. P0/F0 still agree. Bypassing only L0-vs-F0 inode equality permits original execution before final refusal; assert zero **total** calls. |
| Opened link (PC-15) | Capture P0 with nlink 1; while it is held add an alias. F0 captures nlink 2; remove alias before returning F0. L0 and final observations see one. |
| Fresh link (PC-29) | Capture F0 with nlink 1; while held add alias. L0 captures 2; remove alias before returning L0. Initial/opened/final observations see one. |
| Final leaf link (PC-16) | Add alias in Qj. L1 captures nlink 2; remove alias before returning L1. F1 sees one. Device/inode/hash and executable access never change. |
| Final FD link (PC-17) | L1 captures nlink 1; while held add alias. F1 captures 2; remove alias before returning F1. L1's stored tuple remains one. |
| Final leaf access (PC-18) | After native Qj produces JSON, chmod original to 0644. L1 returns while it is nonexecutable. If the mutant reaches F1, its before-operation shim restores 0755 before doing native stat; the intact FD access guard then passes. Baseline exits before F1. |
| Final FD access (PC-19) | L1 and its live `-x` see 0755. On F1 entry chmod to 0644, capture native mode/tuple, then return. No later execution or permission guard can mask this omission. |
| Output ordering (PC-20) | Replace the leaf in Qj, return valid JSON and retain final refusal. Moving only printf ahead of L1 makes direct stdout nonempty even though exit 2 and wrapper refusal still hold. |
| Unlink error (PC-21) | The exact stage-leaf rm shim successfully performs native unlink, records destination nlink 1, then returns nonzero. Qualification is otherwise healthy. Removing only status refusal permits success; a remaining second link cannot mask it. |

Restore modes/aliases in fixture teardown even when baseline refuses before a
later callback. Do not wait unconditionally for F1 in the PC-18 baseline; observe
either the permitted next barrier or the awaited process exit. Timeout is not
an assertion witness. No sleeps to hit races, retry loops, shared paths or root
installation are permitted.

For all new process-spawning methods use `ParallelLimiter<ProcessSpawnLimit>`,
immediate stdout/stderr drains, the existing five-second barrier deadline and
ten-second child deadline. Register each barrier child's PID and start identity;
release in `finally`, join it and the root, then delete scratch. The current
root-only `Collect` cleanup is insufficient if a grandchild outlives it. A
missed barrier, rescued child, compile/fixture failure or zero tests is never PC
red. Do not widen deadlines to fit new vectors; loops use fresh bounded children.

Wrapper arms use the real wrapper -> SSH child shim -> `bash -s` -> helper
route, with `SyntheticProof` unset. Remove inherited `C727_TEST_STATE`. Initialize
the private Git repository after fixed scripts are written, and ignore all
runtime markers and replacements so source cleanliness does not intercept the
intended fault. Do not modify shared PATH, runner settings, image or host jq.

Owner/pin simulation remains limited to installation: only exact unmodified
Payload bytes may stand for the release pin; never pin arbitrary `/proc` targets.
Raw-hash tests disable that mapping and use independent original hashes. Retarget
`final-owner`, `final-group`, `final-mode` to H's FD metadata, recording a marker
and preserving the named final owner/mode refusal. `final-digest` now deliberately
exercises the new **pre-execution** pin refusal and requires zero calls. Its old
post-execution diagnostic is superseded: the retained final comparison of the
same unmodified digest scalar is logically redundant after H. Do not pretend
that an earlier refusal reaches `HostJqFinalDigestInvalid`, add a fake production
variable override, or add a second hash just to resurrect that unreachable arm.
Version and both predicate fault arms still reach their named probes.

### Delivery inventory

Zero new asynchronous paths: this is a synchronous helper/wrapper call. No queue,
outbox, wake-up, session input or recovery worker is introduced; busy-recipient
and queued recovery cases have no applicable boundary. No UserPrompt claim.

For the changed synchronous success path, producer = Qj plus final admission;
recipient = real `Invoke-HostJq`; persistence = exclusive `CreateNew`,
`Flush(true)`, close; identity = source SHA + run ID + selected/executing phase +
mode and canonical public path/digest. Recovery is a new invocation/run. V-10
must read and validate the actual persisted receipt via `AssertReceipt`, not
stop at JSON stdout. Refusal must leave zero success receipts and no success
banner. The private SSH substitute proves transport/parsing/local persistence,
not remote authentication, host activation or a later deployment consumer.
Unchanged transport/receipt-write fault coverage remains CARD-1025's obligation.

### Proves it works now

This is a frozen future method roster, not executed evidence. All methods belong
to `HostJqPrerequisiteScriptTests` in the existing test file. Each is one `[Test]`
result with looped vectors, no `[Arguments]`. V-n, R-n and M-n share their number.

| V / M | Literal method | Ordinary assertions and vectors |
|---|---|---|
| V-1 / M-1 | `C1058_Check_rejects_canonical_hardlink` | Persistent nlink 2: direct and real-wrapper check/provision refuse, no calls/install/success receipts, tuples/hashes/links and sentinels unchanged. Native direct and alias nlink 1 succeed. Add P0-only link witness. Hold real provision lock, introduce hardlinked jq while missing caller waits, release; locked recheck refuses without download. |
| V-2 / M-2 | `C1058_Provision_removes_only_owned_stage_link` | Missing install succeeds; trace native two-link publication, exact owned-stage unlink and one-link destination before any jq call. Destination inode/hash survive; second provision is unchanged no-op. Preserve no-clobber, owned cleanup and foreign sentinels. |
| V-3 / M-3 | `C1058_Open_descriptor_must_match_canonical_leaf` | P0-to-O and F0-to-L0 replacements separately refuse before **any** jq call. Native and shebang FD controls succeed. Regular and symlink replacement; check, existing provision and locked recheck vectors. |
| V-4 / M-4 | `C1058_Digest_is_bound_to_open_descriptor` | At H, swap leaf, hash the **actual supplied argument** with native sha256sum, save result, restore original name, return result. Success proof equals independent original digest, differs from replacement; zero foreign calls. Never force the shim to hash FD when the mutant supplied the path. |
| V-5 / M-5 | `C1058_Installed_pin_is_checked_before_execution` | After staging verification/publication/unlink, before P0, replace with valid executable wrong-hash bytes. Exit 2, zero calls/no success; healthy install succeeds. Check and no-op provision accept functional non-pin version and digest with zero install effects. |
| V-6 / M-6 | `C1058_Version_probe_uses_open_descriptor` | Swap after H, before Qv. Original version count one, foreign version zero, no final proof. |
| V-7 / M-7 | `C1058_True_probe_uses_open_descriptor` | Swap in Qv after native output but before return. Original true count one, foreign true zero, no final proof. |
| V-8 / M-8 | `C1058_False_probe_uses_open_descriptor` | Swap in Qt after native output but before return. Original false count one with false/1, foreign false zero, no final proof. |
| V-9 / M-9 | `C1058_Receipt_formatter_uses_open_descriptor` | Swap in Qf before return. Original formatter count one and valid JSON, foreign formatter zero, no success stdout. |
| V-10 / M-10 | `C1058_Changed_leaf_cannot_emit_success_proof` | Qj returns valid JSON after persistent regular/symlink replacement. Direct empty stdout/exit 2; real wrapper has no success receipt/banner. Unchanged wrapper control persists complete source/run/phase/mode and canonical path/digest evidence. |
| V-11 / M-11 | `C1058_Staged_unlink_failure_refuses` | Separate rm failure without side effect and native unlink followed by failure. Both refuse with no jq/proof, retain destination inode/bytes, preserve sentinels and owned cleanup. Exactly one stage-leaf attempt; cleanup's directory rm is distinct. |
| V-12 / M-12 | `C1058_Pre_execution_link_checks_are_independent` | F0-only and L0-only real nlink 2 schedules; each refuses before any jq call with all other snapshots at one. Check, existing provision and locked recheck. |
| V-13 / M-13 | `C1058_Identity_comparisons_include_device` | Supply a different numeric device only at P0, L0 or L1, actual inode unchanged. First two refuse before any call; L1 refuses empty stdout after formatter. All native tuples/hashes remain recorded. Check and existing provision. |
| V-14 / M-14 | `C1058_Final_link_checks_are_independent` | L1-only and F1-only native link growth schedules; exit 2 and empty stdout, unchanged inode/hash, other snapshot one. Check and existing provision. |
| V-15 / M-15 | `C1058_Final_permission_checks_are_independent` | L1-only and F1-only access loss schedules; native mode 0644 at selected access, same inode/hash and nlink 1, exit 2 and empty stdout. Check and existing provision. |
| V-16 / M-16 | `C1058_Proof_is_buffered_until_final_admission` | On Qj replacement, direct stdout is empty and exit 2. Healthy case is one schema-1 JSON document. This tests output bytes even if a caller would refuse the process exit. |
| V-17 / M-17 | `C1058_Metadata_syntax_refuses` | Extra-token/multiline/missing-token records and nonnumeric device/inode/link at each snapshot refuse. Extra-token suffix isolates frame guard. For numeric device/inode controls, substitute the same malformed field consistently at all snapshots so string equality cannot mask missing validation. Record native and supplied fields; pre-probe faults allow zero calls, final faults allow no stdout. |
| V-18 / M-18 | `C1058_Observation_failures_are_invalid` | Real open failure, unavailable FD target/stat/hash, valid stdout with nonzero stat/hash exit, and malformed hash. Refuse `HostJqInvalid`, zero new install effects, no pathname fallback or success. Valid-output/nonzero stat runs at every snapshot; hash faults at H. Open failure removes the leaf after P0 in initial provision, proving no missing-authority download. |

V-6..9 each cross check/existing provision with regular/symlink replacements
(four vectors per method); final installed ordering is V-5, so no repeated
installation for every late swap. Metadata/parser vectors use direct check and
existing provision; late failures are judged by empty stdout rather than zero
prior calls. For genuinely absent procfs/stat output, refusal is ordinary proof;
the valid-output/nonzero variant is the isolatable status PC. No synthetic
field claims native cross-device or real procfs outage coverage.

### Guards the regression

Each label below is a required literal assertion message in its named M-n method;
fixture failures have separate labels. Use the vector/observation as extra context.

| R | Method | Decisive assertion labels |
|---|---|---|
| R-1 | `C1058_Check_rejects_canonical_hardlink` | `c1058-initial-link-refused`: exit 2 for the P0-only vector; `c1058-hardlink-no-execution`: calls zero. |
| R-2 | `C1058_Provision_removes_only_owned_stage_link` | `c1058-install-single-link-before-probe`: healthy exit 0 and native nlink 1 before first call. |
| R-3 | `C1058_Open_descriptor_must_match_canonical_leaf` | `c1058-pre-open-no-execution` and `c1058-fresh-leaf-no-execution`: total calls zero at their separate boundaries. |
| R-4 | `C1058_Digest_is_bound_to_open_descriptor` | `c1058-digest-original`: proof equals independent original hash. |
| R-5 | `C1058_Installed_pin_is_checked_before_execution` | `c1058-installed-pin-no-execution`: calls zero despite functional wrong-pin bytes. |
| R-6 | `C1058_Version_probe_uses_open_descriptor` | `c1058-version-foreign-zero`: foreign Qv calls zero. |
| R-7 | `C1058_True_probe_uses_open_descriptor` | `c1058-true-foreign-zero`: foreign Qt calls zero. |
| R-8 | `C1058_False_probe_uses_open_descriptor` | `c1058-false-foreign-zero`: foreign Qf calls zero. |
| R-9 | `C1058_Receipt_formatter_uses_open_descriptor` | `c1058-formatter-foreign-zero`: foreign Qj calls zero. |
| R-10 | `C1058_Changed_leaf_cannot_emit_success_proof` | `c1058-final-success-stdout-empty`: direct stdout empty; real wrapper has zero success receipts/banner. |
| R-11 | `C1058_Staged_unlink_failure_refuses` | `c1058-unlink-status-refused`: exit 2 even with destination already single-linked. |
| R-12 | `C1058_Pre_execution_link_checks_are_independent` | `c1058-opened-link-no-execution`, `c1058-fresh-link-no-execution`: total calls zero. |
| R-13 | `C1058_Identity_comparisons_include_device` | `c1058-pre-device-no-execution`, `c1058-fresh-device-no-execution`, `c1058-final-device-stdout-empty`. |
| R-14 | `C1058_Final_link_checks_are_independent` | `c1058-final-leaf-link-stdout-empty`, `c1058-final-fd-link-stdout-empty`. |
| R-15 | `C1058_Final_permission_checks_are_independent` | `c1058-final-leaf-access-stdout-empty`, `c1058-final-fd-access-stdout-empty`. |
| R-16 | `C1058_Proof_is_buffered_until_final_admission` | `c1058-buffered-stdout-empty`: direct stdout empty even with exit 2. |
| R-17 | `C1058_Metadata_syntax_refuses` | `c1058-metadata-frame-refused`, `c1058-device-syntax-refused`, `c1058-inode-syntax-refused`: exit 2 at each selected vector. |
| R-18 | `C1058_Observation_failures_are_invalid` | `c1058-stat-status-refused`: exit 2; `c1058-open-invalid-no-download`: zero download; `c1058-hash-status-no-execution`, `c1058-hash-syntax-no-execution`: calls zero. |

### Guard inventory

The old audit's G-12 splits into three device comparisons; G-16 into two access
checks; G-19/G-20 into framing, two identity syntax checks, shared stat status,
open classification and hash status/syntax. Add L0 link admission explicitly.
Renumbered inventory below is authoritative: each G-n maps only to PC-n.
There is no ten-control ceiling; the revised commissioned cap is **29**.

| Guard | Plan reference and independently bypassable guard | Control |
|---|---|---|
| G-1 | D-1 P0 link equals one | PC-1 |
| G-2 | D-4 stage-name unlink occurs before qualification | PC-2 |
| G-3 | D-2 F0 inode equals P0 inode | PC-3 |
| G-4 | D-2 H targets FD, not canonical pathname | PC-4 |
| G-5 | D-3 installed pin before any jq execution | PC-5 |
| G-6 | D-2 Qv executes FD | PC-6 |
| G-7 | D-2 Qt executes FD | PC-7 |
| G-8 | D-2 Qf executes FD | PC-8 |
| G-9 | D-2/D-5 Qj executes FD | PC-9 |
| G-10 | D-5 L1 inode equals F0 inode | PC-10 |
| G-11 | D-2 L0 inode equals F0 inode | PC-11 |
| G-12 | D-2 F0 device equals P0 device | PC-12 |
| G-13 | D-2 L0 device equals F0 device | PC-13 |
| G-14 | D-5 L1 device equals F0 device | PC-14 |
| G-15 | D-1/D-2 F0 link equals one | PC-15 |
| G-16 | D-1/D-5 L1 link equals one | PC-16 |
| G-17 | D-1/D-5 F1 link equals one | PC-17 |
| G-18 | D-5 live canonical executable access after L1 | PC-18 |
| G-19 | D-5 live FD executable access after F1 | PC-19 |
| G-20 | D-5 success printf occurs only after final admission | PC-20 |
| G-21 | D-4 stage-leaf unlink nonzero status refuses | PC-21 |
| G-22 | D-2 shared native identity stat status must be zero | PC-22 |
| G-23 | D-2 shared identity record framing | PC-23 |
| G-24 | D-2 shared device decimal syntax | PC-24 |
| G-25 | D-2 shared inode decimal syntax | PC-25 |
| G-26 | D-2 open failure is invalid, never missing/install authority | PC-26 |
| G-27 | D-2/D-3 H command status must be zero | PC-27 |
| G-28 | D-2/D-3 H digest must be 64 lowercase hex digits | PC-28 |
| G-29 | D-1/D-2 L0 link equals one | PC-29 |

Status/parser guards exist once in the shared reader; their ordinary vectors
visit every call site. Each policy comparison remains separately mutated. Link
syntax is covered by the exact-one policies, not a hidden waived parser guard.
There is no separate procfs check, descriptor reopen/fallback or final FD identity
comparison. If Code duplicates a helper guard, changes the observation order or
adds an independent admission edge, revise the inventory/cost before claiming
this freeze covers that implementation. Do not silently expand PCs at execution.

Unchanged PATH/type checks, predicate semantics, formatter command failure,
owner/group/mode syntax and installed checks, download/staging pin, no-clobber,
source custody and wrapper parsing/persistence retain CARD-1025's 67 pending
controls. This inventory covers this card's changes, not their discharge.
The same-scalar final installed digest equality and final regular/non-symlink
implications are explicitly redundant, as explained above. A later content write,
ABA or compromised toolchain remains D-7; no untested stronger claim is made.

Design audit: **guards=29, mapped=29, missing=0, duplicate PC maps=0; all PCs
executable under the specified observation shape.** This certifies the proposed
mutations/witnesses, not execution. Review must verify that the actual code
preserves the shape and no new test is a stub before land.

### Positive controls

Each row is one compiling shell defect, one distinct guard and one literal
TUnit filter. Baseline, red and restored green each require exactly one result
(`-MinExecuted 1`); use the same exact filter for all three. No prefixes, class
filters, whole Utility/Unit, or multi-guard deletion. Where a method has several
vectors, the row's named witness must be the assertion that turns red.
Check that decisive assertion before generic exit/JSON assertions that the
same mutant could also break; for example PC-3 must fail its zero-call label
even when the mutant returns success. A later generic failure is insufficient.

| PC | Deliberate defect (only this change) | Detecting filter | Required red assertion |
|---|---|---|---|
| PC-1 | Omit only P0 exact-one check. | `/*/*/HostJqPrerequisiteScriptTests/C1058_Check_rejects_canonical_hardlink` | P0-only transient link: `c1058-initial-link-refused`; mutant succeeds. |
| PC-2 | Omit owned stage-leaf unlink, leave EXIT cleanup. | `/*/*/HostJqPrerequisiteScriptTests/C1058_Provision_removes_only_owned_stage_link` | `c1058-install-single-link-before-probe`; mutant rejects healthy missing install while two links remain. |
| PC-3 | Omit only F0-vs-P0 **inode** equality; retain device equality. | `/*/*/HostJqPrerequisiteScriptTests/C1058_Open_descriptor_must_match_canonical_leaf` | `c1058-pre-open-no-execution`; regular replacement executes. |
| PC-4 | H hashes canonical path instead of FD; retain every execution target. | `/*/*/HostJqPrerequisiteScriptTests/C1058_Digest_is_bound_to_open_descriptor` | `c1058-digest-original`; successful transient-swap proof has replacement digest. |
| PC-5 | Omit only installed H pin comparison, retain final digest check. | `/*/*/HostJqPrerequisiteScriptTests/C1058_Installed_pin_is_checked_before_execution` | `c1058-installed-pin-no-execution`; wrong-pin script executes before final refusal. |
| PC-6 | Qv alone executes canonical path. | `/*/*/HostJqPrerequisiteScriptTests/C1058_Version_probe_uses_open_descriptor` | `c1058-version-foreign-zero`. |
| PC-7 | Qt alone executes canonical path. | `/*/*/HostJqPrerequisiteScriptTests/C1058_True_probe_uses_open_descriptor` | `c1058-true-foreign-zero`. |
| PC-8 | Qf alone executes canonical path. | `/*/*/HostJqPrerequisiteScriptTests/C1058_False_probe_uses_open_descriptor` | `c1058-false-foreign-zero`. |
| PC-9 | Qj alone executes canonical path. | `/*/*/HostJqPrerequisiteScriptTests/C1058_Receipt_formatter_uses_open_descriptor` | `c1058-formatter-foreign-zero`. |
| PC-10 | Omit only L1-vs-F0 **inode** equality; retain device, link and access checks. | `/*/*/HostJqPrerequisiteScriptTests/C1058_Changed_leaf_cannot_emit_success_proof` | `c1058-final-success-stdout-empty`; regular replacement on same filesystem emits success. |
| PC-11 | Omit only L0-vs-F0 **inode** equality. | `/*/*/HostJqPrerequisiteScriptTests/C1058_Open_descriptor_must_match_canonical_leaf` | `c1058-fresh-leaf-no-execution`; original calls occur before final refusal. |
| PC-12 | Omit only F0-vs-P0 device equality. | `/*/*/HostJqPrerequisiteScriptTests/C1058_Identity_comparisons_include_device` | `c1058-pre-device-no-execution`; P0 device substitution, same real inode. |
| PC-13 | Omit only L0-vs-F0 device equality. | `/*/*/HostJqPrerequisiteScriptTests/C1058_Identity_comparisons_include_device` | `c1058-fresh-device-no-execution`; L0 device substitution. |
| PC-14 | Omit only L1-vs-F0 device equality. | `/*/*/HostJqPrerequisiteScriptTests/C1058_Identity_comparisons_include_device` | `c1058-final-device-stdout-empty`; L1 device substitution. |
| PC-15 | Omit only F0 exact-one check. | `/*/*/HostJqPrerequisiteScriptTests/C1058_Pre_execution_link_checks_are_independent` | `c1058-opened-link-no-execution`; all later link observations one. |
| PC-16 | Omit only L1 exact-one check. | `/*/*/HostJqPrerequisiteScriptTests/C1058_Final_link_checks_are_independent` | `c1058-final-leaf-link-stdout-empty`; F1 still sees one. |
| PC-17 | Omit only F1 exact-one check. | `/*/*/HostJqPrerequisiteScriptTests/C1058_Final_link_checks_are_independent` | `c1058-final-fd-link-stdout-empty`; L1 stored one. |
| PC-18 | Omit only live leaf `-x` after L1. | `/*/*/HostJqPrerequisiteScriptTests/C1058_Final_permission_checks_are_independent` | `c1058-final-leaf-access-stdout-empty`; F1 entry restores access in mutant. |
| PC-19 | Omit only live FD `-x` after F1. | `/*/*/HostJqPrerequisiteScriptTests/C1058_Final_permission_checks_are_independent` | `c1058-final-fd-access-stdout-empty`; L1 already passed while executable. |
| PC-20 | Move the sole success printf before L1, removing its old location; retain all final refusals. | `/*/*/HostJqPrerequisiteScriptTests/C1058_Proof_is_buffered_until_final_admission` | `c1058-buffered-stdout-empty`; exit remains 2, leaked JSON makes stdout nonempty. |
| PC-21 | Ignore only owned stage-leaf rm's nonzero status. | `/*/*/HostJqPrerequisiteScriptTests/C1058_Staged_unlink_failure_refuses` | `c1058-unlink-status-refused`; side-effect-then-error arm succeeds incorrectly. |
| PC-22 | Ignore shared identity stat's status, retain frame/numeric checks. | `/*/*/HostJqPrerequisiteScriptTests/C1058_Observation_failures_are_invalid` | `c1058-stat-status-refused`; valid captured stdout plus nonzero status is accepted. |
| PC-23 | Remove only the shared frame regex end anchor, admitting a suffix. | `/*/*/HostJqPrerequisiteScriptTests/C1058_Metadata_syntax_refuses` | `c1058-metadata-frame-refused`; native three fields plus fourth token is accepted. |
| PC-24 | Omit shared device decimal validation, leave frame and inode checks. | `/*/*/HostJqPrerequisiteScriptTests/C1058_Metadata_syntax_refuses` | `c1058-device-syntax-refused`; consistent nonnumeric devices compare equal as strings and wrongly succeed. |
| PC-25 | Omit shared inode decimal validation, leave frame and device checks. | `/*/*/HostJqPrerequisiteScriptTests/C1058_Metadata_syntax_refuses` | `c1058-inode-syntax-refused`; consistent nonnumeric inodes wrongly succeed. |
| PC-26 | Change only failed-open qualify return from invalid 4 to missing 3. | `/*/*/HostJqPrerequisiteScriptTests/C1058_Observation_failures_are_invalid` | `c1058-open-invalid-no-download`; disappearing leaf now authorizes installation. |
| PC-27 | Ignore H's sha256sum exit status, retain syntax/pin checks. | `/*/*/HostJqPrerequisiteScriptTests/C1058_Observation_failures_are_invalid` | `c1058-hash-status-no-execution`; valid hash plus nonzero exit allows calls. |
| PC-28 | Omit H's digest-syntax validation only. | `/*/*/HostJqPrerequisiteScriptTests/C1058_Observation_failures_are_invalid` | `c1058-hash-syntax-no-execution`; existing-mode malformed digest reaches probes. |
| PC-29 | Omit only L0 exact-one check. | `/*/*/HostJqPrerequisiteScriptTests/C1058_Pre_execution_link_checks_are_independent` | `c1058-fresh-link-no-execution`; all other link observations one. |

All controls touch the same helper; execute serially, restoring before the next
control. They remain **pending** until separately commissioned post-land
SourceLanding Mutation after Code, Review and confirmed land. Use that brief's
external checkpoint driver and evidence root; do not commit/push from its
snapshot. Retain per-control baseline/red/restored-green results and exact
assertion evidence. CARD-1025's 67 and CARD-1054's 13 controls remain separate.
The recorded Mutation pause is not resumed by this repair.

### Out of scope

Whole class, Utility/Unit, namespace or assembly tests; full installer/image/
legacy rosters; CARD-1040's fifteen-method activation proof; live SSH, host
installation/deployment, image activation, fleet settings or restarts. None is
needed to establish this helper's private behavioral contract. No wrapper
re-stat, memfd/fexecve, second privileged cache or descriptor transfer. D-7's
in-place writes, ABA and later pathname use remain outside this admission proof.

### Cost

All timings are estimates, not measurements; queueing and investigation are
additional. Scope is larger than the rejected ten-PC proposal; savings are zero.

- Ordinary Code floor: CP-1's exact ten-method filter **9 minutes**, CP-2's exact
  fifteen-method filter **11**, CP-3's exact ten-method filter **9**, each with its
  own isolated build: **29 minutes**, three builds and **35 TUnit executions**.
  Bootstrap/prerequisites add **3**, giving **32 minutes**. The roster contains
  **29 unique methods**: eighteen new plus eleven inherited; no skipped results.
- S1 is **35-55 minutes**: 23-43 authoring + 3 setup + 9 CP-1. S2 is **50-60
  minutes**: 30-40 authoring/docs using the shared fixture + 20 CP-2/CP-3.
  If elapsed work exceeds this budget, push a truthful checkpoint and retain
  scope; do not weaken guards, widen filters, retry or change timeouts.
- Mutation floor: **29 x (3-minute baseline + 3-minute red + 3-minute restored
  green) = 261 minutes**, **87 exact-method executions**. Each PC uses its literal
  filter above for every phase. This includes per-phase build time and exact
  restoration, with **3 minutes** additional setup: **264 minutes**. This replaces
  the ten-PC/90-minute execution estimate, adding **171 minutes** for 19 controls.
- Code plus Mutation verification/setup floor: **296 minutes** (32 + 264).
  Separate ordinary Review repeats the same bounded 32-minute scope, giving
  **328 minutes** across Code, Review and Mutation, excluding source authoring.
  Do not present Code/Review green as any of the 87 PC executions.

### Checkpoint execution contract

Commit/push each slice before its run. Bootstrap the checkpoint tool once through
the host build-slot gate, using forward-slash alternate output:

```powershell
pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1058-checkpoint-bootstrap -- dotnet build tools/Antiphon.Checkpoints --property:OutputPath=bin-c1058-tool/ --property:UseAppHost=false --nologo
$c1058Source = (git rev-parse HEAD).Trim()
dotnet tools/Antiphon.Checkpoints/bin-c1058-tool/Antiphon.Checkpoints.dll run --plan docs/superpowers/plans/2026-10-05-card-1058-host-jq-file-identity-plan.md --after S1 --serial --expected-source-sha $c1058Source --max-wait 50s
```

For S2 refresh SHA and use `--after S2`. Rows take their own slots; do not wrap
the DLL runner in a second slot. Await every exit-75 run with the tool's `wait`
and `--max-wait 50s` until terminal, keep source frozen and own every child.
Slot refusal/timeout is not green; no unleased retry or `-NoSlot`. Use TUnit's
`dotnet run` through the driver, never `dotnet test`.

Retain unedited CHECKPOINT lines, exact expanded rosters/counts, SHA-validated
clean-source and verified-build provenance. JSON/TRX/logs stay ignored. After
all children exit, remove only owned `bin-c1058-*` outputs, including bootstrap
outputs across projects. Code/Review run `scripts/check-evidence-diff.ps1
-BaseRef <task-base> -HeadRef <pushed-sha>` over the full task range. Inherited red
is reproduced with only its exact failing method at base under the same
prerequisites; no base-assembly rerun. Diff/link/manifest checks and the named
bootstrap are declared setup, not extra test selections. Documentation-only
Plan repair needs no build/test-project run.

### Checkpoints

These are the **closed ordinary list**, all in the native Linux host-jq fixture
lane. Exact roster equality is required in addition to `Min`. Prefix stars
accommodate TUnit discovery spelling, not additional method names. CP-1 closes
S1; CP-2 and CP-3 run serially in one checkpoint-tool group after S2. Expanded
rosters are 10 / 15 / 10 results, with zero failures/skips. No parameter expansion.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1058-links/` | linux-host-jq-links | `/*/*/HostJqPrerequisiteScriptTests/(C1058_Check_rejects_canonical_hardlink*)\|(C1058_Provision_removes_only_owned_stage_link*)\|(C1058_Staged_unlink_failure_refuses*)\|(C1025_Check_qualifies_deployment_shell*)\|(C1025_Check_has_no_install_effects*)\|(C1025_Provision_requires_missing_jq*)\|(C1025_Provision_serializes_and_rechecks*)\|(C1025_Provision_publishes_complete_no_clobber*)\|(C1025_Provision_cleans_only_owned_staging*)\|(C1025_Provision_requalifies_published_jq*)` | V-1, V-2, V-11, R-1, R-2, R-11 | exactly 10 listed methods, 10 passed, 0 failed/skipped | 10 | 9 | true |
| CP-2 | S2 | `tests/Antiphon.Tests -> bin-c1058-identity/` | linux-host-jq-identity | `/*/*/HostJqPrerequisiteScriptTests/(C1058_Open_descriptor_must_match_canonical_leaf*)\|(C1058_Digest_is_bound_to_open_descriptor*)\|(C1058_Installed_pin_is_checked_before_execution*)\|(C1058_Version_probe_uses_open_descriptor*)\|(C1058_True_probe_uses_open_descriptor*)\|(C1058_False_probe_uses_open_descriptor*)\|(C1058_Receipt_formatter_uses_open_descriptor*)\|(C1058_Changed_leaf_cannot_emit_success_proof*)\|(C1058_Pre_execution_link_checks_are_independent*)\|(C1058_Identity_comparisons_include_device*)\|(C1058_Final_link_checks_are_independent*)\|(C1058_Final_permission_checks_are_independent*)\|(C1058_Proof_is_buffered_until_final_admission*)\|(C1058_Metadata_syntax_refuses*)\|(C1058_Observation_failures_are_invalid*)` | V-3, V-4, V-5, V-6, V-7, V-8, V-9, V-10, V-12, V-13, V-14, V-15, V-16, V-17, V-18, R-3, R-4, R-5, R-6, R-7, R-8, R-9, R-10, R-12, R-13, R-14, R-15, R-16, R-17, R-18 | exactly M-3..M-10 and M-12..M-18, 15 passed, 0 failed/skipped | 15 | 11 | true |
| CP-3 | S2 | `tests/Antiphon.Tests -> bin-c1058-compat/` | linux-host-jq-compat | `/*/*/HostJqPrerequisiteScriptTests/(C1058_Check_rejects_canonical_hardlink*)\|(C1058_Provision_removes_only_owned_stage_link*)\|(C1058_Staged_unlink_failure_refuses*)\|(C1025_Check_qualifies_deployment_shell*)\|(C1025_Check_rejects_canonical_leaf_symlink*)\|(C1025_Receipt_rejects_canonical_lookup_with_unapproved_target*)\|(C1025_Check_has_no_install_effects*)\|(C1025_Provision_verifies_download_before_use*)\|(C1025_Provision_requalifies_published_jq*)\|(C1025_Receipt_requires_complete_current_proof*)` | V-1, V-2, V-5, V-10, V-11, R-1, R-2, R-5, R-10, R-11 | exactly 10 listed methods, 10 passed, 0 failed/skipped | 10 | 9 | true |

## Acceptance and later operations

Code/Review establish the private helper contract and wrapper compatibility.
After reviewed publication, the rollout owner may run the existing read-only
`check-host-jq` phase from the reviewed canonical checkout and retain its
source-bound host receipt. A hardlink refusal requires diagnosis; it does not
authorize deleting aliases or reinstalling. No installation is needed for a
host that qualifies. Host proof remains separate from CARD-1040's immutable
image/activation gate. Report D-7's remaining trust boundary and pending Mutation.

--- next stage ---
next: code
handoff: Implement CARD-1058 S1/S2 on native Linux using the frozen observation order and shared fixture. Run only CP-1 then serial CP-2/CP-3 (10/15/10 results). Preserve existing non-pin jq and installed pre-execution pin. Review the 29 independent PC designs; execution stays separate post-land Mutation. No whole Utility/Unit or live host/image changes.
artifact: docs/superpowers/plans/2026-10-05-card-1058-host-jq-file-identity-plan.md
