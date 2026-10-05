# CARD-1058: bind host jq qualification to one open file

Date: 2026-10-05. Plan task: `c3e154a8-0f5e-4eec-9b75-7e17cbc84d85`.
Inspected source: `14316228eaac5afd8a404d75fca69d7a9fa94382`.
Card: Antiphon `8650f8ec-02f8-4c1d-a45b-8fc243e38dfa`.

## Outcome and stage boundary

Reject a multiply linked canonical host jq before executing it. Open the admitted
file once, verify its identity, hash and execute through that retained descriptor,
and withhold the successful proof if the canonical leaf changes during the probe.
Keep the existing distinction between a functional preinstalled jq and a freshly
installed, pinned artifact. The wrapper continues consuming a point-in-time host
observation; it does not make a second remote pathname observation.

This is a medium change to an existing privileged-install boundary. The brief
does **not** fold TestDesign into Plan. The methods, controls and checkpoint table
below are concrete proposals for a separate **test-design** stage to freeze,
not an authorization for Code to run an unfrozen manifest. There is no unresolved
product-policy choice: D-1 through D-8 are the selected design, preserving the
landed compatibility contract. Two Code slices each fit a 30-60 minute budget.

This Plan changes only this document. No host installation, deployment, image
rebuild, live race experiment, mutation, build or test run is claimed.

## Ground truth

Read live CARD-1058, CARD-1025, CARD-1054 and CARD-1040, their landed plans, the
helper, wrapper and affected test bodies at the source above. Line references
are to that source; historical card results are not new measurements.

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
  the descriptor and canonical leaf's identity, regular/non-symlink/executable
  state and single-link condition before printing the buffered document.
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
approximately 10:16 UTC through the configured task API. Defaults revision was
2; the catalogue exposed eligible Linux and Windows runners and one unavailable,
draining entry. These are scheduling observations, not a reservation or image
qualification. Re-read both routes before dispatch; embed no fleet hostname.

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

### S1 — Single-link admission and compatible publication (35-50 minutes)

Change `scripts/server2-host-jq.sh` to reject canonical link count other than one
before invoking jq, including the existing/no-op and locked qualification paths.
Change its successful publication sequence to remove the owned staging name
before final qualification, failing closed on removal failure. Preserve no-clobber,
lock and cleanup behavior.

Extend `tests/Antiphon.Tests/Scripts/HostJqPrerequisiteScriptTests.cs` with M-1
and M-2 below. Extend the existing `HostJqFixture` minimally for actual hardlinks
and a staged-name removal failure. Read real link counts; never fake `nlink=1`.
Keep the root/owner and install pin seams limited to the existing simulation.
Commit/push S1, then run CP-1 once. Commit any required fix before a justified
rerun; retain failed evidence and the SHA it actually tested.

### S2 — Descriptor-bound qualification, proof boundary and docs (45-60 minutes)

Change the same helper for D-2, D-3 and D-5, retaining S1's rules. Extend the
same test file with M-3 through M-10 and fixture barriers that expose the real
open/hash/execute ordering. Adapt old final-digest/owner/mode fixture faults to
the new descriptor observation so their named failures are still exercised;
do not let an earlier unrelated guard mask the intended defect.

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

## TestDesign handoff

TestDesign must append `## Verification design`, inspect the listed test bodies
and fixture shims, and freeze the following method roster, substitution/barrier
inventory, exact manifest, assertion witnesses and costs before Code. M-1..M-10
are new non-parameterized methods in `HostJqPrerequisiteScriptTests`; looped
vectors contribute one TUnit result per method. Existing regressions retain their
current assertions. No full-class filter is proposed.

| Method / behavior | Proposed method name | Decisive ordinary assertions |
|---|---|---|
| M-1 / B-1 / V-1 / R-1 | `C1058_Check_rejects_canonical_hardlink` | Real additional hardlink to a working canonical executable: direct and wrapper check/provision exit 2, no jq execution, no success receipt/banner, unchanged inode/bytes/links and zero install effects. Include direct canonical and symlink-alias single-link controls. |
| M-2 / B-2 / V-2 / R-2 | `C1058_Provision_removes_only_owned_stage_link` | Missing install succeeds, observed nlink becomes 1 before any jq call, destination inode/bytes survive, second provision is unchanged no-op. Removal failure refuses without success or destination removal. Preserve foreign sentinels, no-clobber and owned cleanup. |
| M-3 / B-3 / V-3 / R-3 | `C1058_Open_descriptor_must_match_canonical_leaf` | Deterministic regular-file and symlink replacement between initial identity capture and open cannot execute the foreign file. Open/stat/procfs failure is invalid, never install authority; equal-inode/different-device synthetic metadata must not compare equal. Healthy native/shebang descriptor controls execute. |
| M-4 / B-4 / V-4 / R-4 | `C1058_Digest_is_bound_to_open_descriptor` | Temporarily replace the leaf after capture, hash while replacement is present, restore the original name before final observation. Successful proof contains the real original descriptor digest, not replacement bytes; no foreign execution. Native hash shim observes actual bytes/identity, not a canned expected digest. |
| M-5 / B-5 / V-5 / R-5 | `C1058_Installed_pin_is_checked_before_execution` | Tamper the published file after staging verification but before final capture: pin mismatch exits 2 with zero jq calls and no success proof. A healthy install passes. Existing healthy non-pin version/digest still qualifies under CARD-1025 D-3. |
| M-6 / B-6 / V-6 / R-6 | `C1058_Version_probe_uses_open_descriptor` | Replace the leaf after descriptor hashing, before version executes. Original descriptor may execute; foreign invocation count stays zero. Persistent replacement ultimately refuses with no success JSON/receipt. |
| M-7 / B-7 / V-7 / R-7 | `C1058_True_probe_uses_open_descriptor` | Replace after the admitted version call, before the true predicate; require zero foreign invocations and no successful proof for persistent replacement. |
| M-8 / B-8 / V-8 / R-8 | `C1058_False_probe_uses_open_descriptor` | Replace after the admitted true predicate, before the false predicate; require zero foreign invocations and no successful proof for persistent replacement. |
| M-9 / B-9 / V-9 / R-9 | `C1058_Receipt_formatter_uses_open_descriptor` | Replace after the admitted false predicate, before `-cn`; require zero foreign invocations, not merely exit 2. Buffering must not leak a success document. |
| M-10 / B-10 / V-10 / R-10 | `C1058_Changed_leaf_cannot_emit_success_proof` | Replace canonical leaf while the admitted formatter returns valid JSON. Helper must withhold buffered stdout and exit 2; real wrapper yields no success receipt/banner. Unchanged control retains canonical public paths and descriptor-derived metadata. |

For M-6..M-9, exercise both a symlink to a working foreign script and a different
regular executable. Foreign scripts must return valid version/predicate/JSON
results and leave an unmistakable execution marker so the test fails for an
unsafe invocation even if a later identity/pin check refuses. Include check and
existing provision; newly installed ordering is separately pinned by M-5.

Use `ParallelLimiter<ProcessSpawnLimit>` on every new process-spawning method.
Use private marker/release barriers, the existing bounded wait/collect custody,
and `finally` release plus awaited child reaping. No probabilistic race loops,
sleep-based hit rates or live filesystem races. The fixture may intercept its
private external `stat`/`sha256sum` calls or its traced executable; it must still
perform the real syscall/hash and must not replace the production admission
predicate or insert a production test hook. Require exact replacement counts
for fixed private paths. Record where a barrier sits relative to descriptor
capture and each invocation so a test cannot quietly stop before its witness.

### Proposed positive controls

One proposed control per behavior, all **pending**. TestDesign must reject an
equivalent/masked mutant, keep each defect compiling, and refine any newly found
independent guard rather than claiming unobserved coverage. Every baseline,
red and restored-green run selects exactly its listed method, with filter
`/*/*/HostJqPrerequisiteScriptTests/<literal method name from the table above>`.
No other method, class, namespace or suite is authorized for that PC.

| PC | Behavior / exact method | Deliberate defect | Required red assertion |
|---|---|---|---|
| PC-1 | B-1 / M-1 | Bypass the link-count-equals-one policy at its admission/recheck sites, retaining other identity rules. | Multiply linked working jq must refuse without executing; mutant instead qualifies. |
| PC-2 | B-2 / M-2 | Omit successful publication's staged-name unlink, leaving normal EXIT cleanup unchanged. | Healthy missing install must succeed with one link before qualification; mutant refuses while both names exist. |
| PC-3 | B-3 / M-3 | Bypass comparison of the opened device/inode against the pre-open and current leaf identities, retaining basic regular/single-link checks. | Regular replacement during open must execute zero foreign calls, even if final proof later refuses. |
| PC-4 | B-4 / M-4 | Hash `"$resolved"` instead of the retained descriptor, preserving descriptor execution. | Transient swap yields the wrong digest in an otherwise successful proof; original digest equality fails. |
| PC-5 | B-5 / M-5 | Omit only the newly installed descriptor's pre-execution pin comparison; retain final pin checks. | Mismatched published bytes must execute zero jq calls; final refusal alone cannot satisfy this assertion. |
| PC-6 | B-6 / M-6 | Execute version through `"$resolved"` rather than FD 8. | Foreign execution marker must remain absent. |
| PC-7 | B-7 / M-7 | Execute only the true predicate through `"$resolved"`. | Foreign execution marker must remain absent. |
| PC-8 | B-8 / M-8 | Execute only the false predicate through `"$resolved"`. | Foreign execution marker must remain absent. |
| PC-9 | B-9 / M-9 | Execute only JSON formatting through `"$resolved"`. | Foreign execution marker must remain absent, regardless of later exit. |
| PC-10 | B-10 / M-10 | Release buffered successful JSON without the final canonical-leaf identity admission. | Persistent formatter-time replacement must produce empty successful stdout and no wrapper success receipt. |

All mutations touch the same helper, so run them serially, restoring before
each next control. Execute only after ordinary Code, separate Review and
confirmed land under a separately commissioned SourceLanding Mutation task.
Such a snapshot never commits/pushes; its records remain in its assigned external
evidence root. CARD-1025's 67 and CARD-1054's 13 pending controls are not duplicated
or discharged here. The card records that Mutation was paused; this Plan does
not resume it or commission those cycles.

### Cost

Estimates, not measured timings: CP-1 8 minutes, CP-2 7, CP-3 8, including each
row's isolated build. **Ordinary Code floor: 23 minutes**, plus 3 minutes initial
checkpoint-tool/prerequisite setup. S1: 24-39 minutes authoring + 3 setup + 8
verification = 35-50. S2: 30-45 authoring/docs + 15 verification = 45-60.
Review executes the same bounded ordinary scope at its reviewed source SHA.

Separate Mutation floor: ten method-scoped baseline builds/runs at 3 minutes
each plus ten red/restored-green cycles at 6 minutes each = **90 minutes**, 30
method executions; allow 3 setup minutes separately, before investigation/report
authoring. Provisional total verification floor for Code plus Mutation is
119 minutes (23 ordinary + 3 setup + 90 PC + 3 PC setup), excluding separate
Review. No measured savings are claimed. If a slice exceeds its estimate, push
an honest checkpoint and retain scope; do not delete guards, broaden filters,
relax assertions or extend deadlines to fit the estimate.

### Checkpoints

**Proposed, pending TestDesign freeze.** All three rows use the native Linux
host-jq fixture lane, named in `Group`. These form the entire proposed ordinary
scope: three isolated builds, 26 TUnit results (21 unique methods), zero skipped.
No `[Arguments]` expansion is proposed. Require literal roster equality, not only
the `Min` floor. Prefix stars accommodate the repository's TUnit discovery form;
they do not authorize other method names. CP-1 closes S1; CP-2 and CP-3 close S2.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1058-links/` | linux-host-jq-links | `/*/*/HostJqPrerequisiteScriptTests/(C1058_Check_rejects_canonical_hardlink*)\|(C1058_Provision_removes_only_owned_stage_link*)\|(C1025_Check_qualifies_deployment_shell*)\|(C1025_Check_has_no_install_effects*)\|(C1025_Provision_requires_missing_jq*)\|(C1025_Provision_serializes_and_rechecks*)\|(C1025_Provision_publishes_complete_no_clobber*)\|(C1025_Provision_cleans_only_owned_staging*)\|(C1025_Provision_requalifies_published_jq*)` | V-1, V-2, R-1, R-2 | exactly the 9 listed methods, 9 passed, 0 failed/skipped | 9 | 8 | true |
| CP-2 | S2 | `tests/Antiphon.Tests -> bin-c1058-identity/` | linux-host-jq-identity | `/*/*/HostJqPrerequisiteScriptTests/(C1058_Open_descriptor_must_match_canonical_leaf*)\|(C1058_Digest_is_bound_to_open_descriptor*)\|(C1058_Installed_pin_is_checked_before_execution*)\|(C1058_Version_probe_uses_open_descriptor*)\|(C1058_True_probe_uses_open_descriptor*)\|(C1058_False_probe_uses_open_descriptor*)\|(C1058_Receipt_formatter_uses_open_descriptor*)\|(C1058_Changed_leaf_cannot_emit_success_proof*)` | V-3, V-4, V-5, V-6, V-7, V-8, V-9, V-10, R-3, R-4, R-5, R-6, R-7, R-8, R-9, R-10 | exactly M-3..M-10, 8 passed, 0 failed/skipped | 8 | 7 | true |
| CP-3 | S2 | `tests/Antiphon.Tests -> bin-c1058-compat/` | linux-host-jq-compat | `/*/*/HostJqPrerequisiteScriptTests/(C1058_Check_rejects_canonical_hardlink*)\|(C1058_Provision_removes_only_owned_stage_link*)\|(C1025_Check_qualifies_deployment_shell*)\|(C1025_Check_rejects_canonical_leaf_symlink*)\|(C1025_Receipt_rejects_canonical_lookup_with_unapproved_target*)\|(C1025_Check_has_no_install_effects*)\|(C1025_Provision_verifies_download_before_use*)\|(C1025_Provision_requalifies_published_jq*)\|(C1025_Receipt_requires_complete_current_proof*)` | V-1, V-2, V-3, V-4, V-5, V-10, R-1, R-2, R-3, R-4, R-5, R-10 | exactly M-1/M-2 and the 7 listed existing methods, 9 passed, 0 failed/skipped | 9 | 8 | true |

After TestDesign freezes the manifest, commit/push each slice before its run.
Bootstrap the checkpoint tool once through the host build-slot gate, with a
forward-slash alternate output, for example:

```powershell
pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1058-checkpoint-bootstrap -- dotnet build tools/Antiphon.Checkpoints --property:OutputPath=bin-c1058-tool/ --property:UseAppHost=false --nologo
$c1058Source = (git rev-parse HEAD).Trim()
dotnet tools/Antiphon.Checkpoints/bin-c1058-tool/Antiphon.Checkpoints.dll run --plan docs/superpowers/plans/2026-10-05-card-1058-host-jq-file-identity-plan.md --after S1 --serial --expected-source-sha $c1058Source --max-wait 50s
```

For S2 refresh the SHA and use `--after S2`. Rows take their own build slots;
do not wrap that DLL run in another slot. On exit 75, use the tool's `wait` for
the emitted run ID with `--max-wait 50s` until terminal. Keep source frozen and
own/await every child; never settle while a run is outstanding. Slot refusal or
timeout is not green and does not permit an unleased retry. Use TUnit through
the checkpoint tool's `dotnet run`, never `dotnet test`.

Retain unedited CHECKPOINT lines, exact expanded rosters/counts, source SHA,
clean-source and verified-build provenance in the stored report. Validate
receipts against their actual SHA. Generated JSON/TRX/logs remain ignored.
Remove only owned `bin-c1058-*` outputs, including bootstrap outputs, after all
children exit. Check links, `git diff --check`, and run
`scripts/check-evidence-diff.ps1 -BaseRef <task-base> -HeadRef <pushed-sha>` over
the full Code/Review task range. These and the named bootstrap are declared
setup, not extra test selections. For an inherited regression, reproduce only
its exact method at the committed base in equivalent prerequisites; never
rerun the base assembly. No repetition after green is commissioned.

## Acceptance and later operations

Code/Review can establish the helper's private behavioral contract and unchanged
wrapper compatibility. After reviewed publication, the rollout owner may run the
existing read-only `check-host-jq` phase from the reviewed canonical checkout
and retain its source-bound host receipt. A hardlink refusal is a host integrity
finding to diagnose; it does not authorize automatic removal/reinstallation.
No installation is required if the actual host already qualifies.

Keep live host proof separate from CARD-1040's immutable image/activation proof
and its fifteen consumer results. No restart, image activation, release change,
host setting mutation or shared filesystem access is part of these Code slices.
The final completion report must retain the residual trust boundary and pending
Mutation status; it cannot claim that all user-controlled bytes are excluded
from every host/image consumer.

--- next stage ---
next: test-design
handoff: Freeze CARD-1058's two host-helper slices, ten exact-method PCs and three narrow Linux checkpoints. Preserve non-pin existing jq; require installed pin before FD execution, remove the owned publish link, and validate/buffer proof. Prove deterministic swaps and real inode/link/hash observations; no wrapper re-stat, image change, whole Unit or Mutation execution.
artifact: docs/superpowers/plans/2026-10-05-card-1058-host-jq-file-identity-plan.md

## Verification design

TestDesign task `583b6151-4164-4b90-a854-47b0b551c932`, inspected at
`0de12dac930ad52a235604c566ee643e71b21daa` on 2026-10-05.

**Disposition: return to Plan; the ten-PC proposal is not frozen for Code.**
The two implementation slices, ten method names and three narrow Linux ordinary
selections can be retained. The proposed ten mutations cannot establish the
required independent guard coverage. PC-1 deletes several admission checks,
PC-3 deletes both identity comparisons, and PC-10 deletes the final admission
block. Passing those controls would not establish that each independently
bypassable guard has an effective assertion. This appendix does not change
D-1..D-8 or authorize implementation, builds, live operations or Mutation.

The repair is technical, not a product-policy choice: Plan must split the
composite controls, specify the observation order needed to isolate them, and
revise the ten-control ceiling and cost. Do not delete a guard or weaken D-1,
D-2 or D-5 to fit ten. The audit below deliberately does **not** issue the
`guards=N, mapped=N, missing=0, duplicate PC maps=0; all PCs executable`
certificate. A return-to-Plan finding is not a Code handoff.

### Inspection

Bodies read, not inferred from method names:

- `scripts/server2-host-jq.sh`, entire 145-line helper: `qualify`, `emit`,
  `refuse_path`, initial/post-lock/final qualification, publication and traps
  | link policy -> V-1/R-1; staging -> V-2/R-2; opened identity, hash, pin and
  execution -> V-3..V-9/R-3..R-9; buffered admission -> V-10/R-10.
- `tests/Antiphon.Tests/Scripts/HostJqPrerequisiteScriptTests.cs`, all test
  bodies and both fixtures. In particular, the eleven existing methods selected
  by CP-1/CP-3, and all of `HostJqFixture`'s constructor, `JqScript`, `Existing`,
  `Hash`, `Inode`, `WriteTool`, `InitializeRepo`, `Wrapper`, receipt assertions,
  `Start`, `Run`, `Collect`, marker waits and `Dispose`
  | fixture limitations and exact methods below; unchanged rollout/preflight,
  transport and source-custody methods excluded from ordinary scope.
- `scripts/deploy-server2.ps1`: entry setup and complete `Invoke-HostJq` body
  | actual child transport, proof parser, durable receipt and rejection ->
  V-1, V-10, R-10 and CP-3. No wrapper re-stat is proposed.
- `tests/Antiphon.Tests/Infrastructure/JqRunnerImageContractTests.cs`, complete
  bodies including `JqFixture`; `verify-codex-image.sh`'s `jq-version` arm;
  `c590-remote.sh`'s `c1008_recycle` admission
  | exclusion: image qualification and later pathname consumers remain separate.
- `docs/testing-and-build.md`: checkpoint schema/runner, source provenance,
  Linux output handling, method-scoped Mutation, jq qualification and delivery
  sections; `docs/docker-stack.md`'s host prerequisite; project conventions;
  orchestration stage contract; CARD-1040's frozen checkpoint table
  | ordinary manifest/cost below; no whole Unit or image/activation substitution.
- `tools/Antiphon.Checkpoints/Manifest/PlanTableImporter.cs`:
  `ImportMarkdown`, `ExtractSection`, `SplitRow`
  | importer reads the **first** exact `### Checkpoints` heading. The appendix
  repeats the original table unchanged, so no different selection is hidden
  behind an ignored second table. On repair, make one authoritative importable
  table, or keep both tables identical. Changing only the second is insufficient.

**Missing setup to implement, not evidence already present:** M-1..M-10 do not
exist. `Inode` currently reads only `%i`; no helper exposes real device/inode/link
tuples. `Existing("healthy")` copies native jq and does not trace its execution.
The existing hash shim returns the release pin whenever bytes equal `Payload`;
the stat shim synthesizes owner/mode only for pathname-specific shapes. Neither
is suitable unchanged for descriptor identity/hash witnesses. There is no
open/hash/probe/formatter barrier, no exact staged-unlink failure arm, and no
independent foreign-execution record. The existing `download-barrier` is a timed
sleep; it is not a deterministic swap barrier. `Collect` joins the root process;
if that root has exited, its `finally` alone does not establish death of a
blocked grandchild. New barriers must release and await their own children too.

Read-only tool discovery found Bash, pwsh, git, node, stat, sha256sum and ln;
PATH resolves jq to `/home/app/.local/bin/jq`. This is discovery only, not a
native execution/procfs/hardlink qualification or an outer-host/image receipt.
The fixture copies native jq into its private canonical location; this PATH
observation does not authorize any standing-container installation. No test or
build was run by TestDesign.

#### Deterministic fixture and witness requirements

These requirements refine the proposed tests without inserting production test
hooks or replacing the admission predicate:

1. Give every invocation a private nonce, trace and marker/release pair beneath
   the ignored fixture root. Intercept only private tool calls and the traced
   executable. Record readiness **after** the real operation whose result is
   being held; emit that captured result only after release. Select a barrier
   by operation/target/phase, not an unexplained global stat call number. Assert
   exactly one hit and one release for the selected boundary.
2. Observe `%d`, `%i`, `%h`, numeric file mode and actual SHA-256 with absolute
   native tools. Use non-following stat for the canonical leaf and following
   stat for `/proc/self/fd/8`. Independently hash original and replacement bytes
   in C#. Record the descriptor tuple inside the child that inherited FD 8:
   `/proc/self/fd/8` in the test parent's unrelated process is not that file.
3. Rename the original to a private holding name before replacing the leaf;
   preserve that original inode with **one** link. Do not retain it with a
   hardlink in an identity test, which would trip D-1 first. Restore the
   original name with rename for the transient-hash arm. Assert precondition
   tuples and different original/replacement hashes before starting the helper.
4. Trace original and foreign executable invocations separately, including
   invocation kind and argv. A foreign script must itself produce a healthy
   version, true/false outputs with their proper exit codes, and valid formatter
   JSON. Prove its behavior in fixture setup, then clear its markers. A later
   refusal cannot erase an unsafe invocation. Native jq controls prove native
   FD execution; traced shebang controls prove observable invocation ordering.
5. M-3 holds the initial native leaf stat result, swaps before returning it,
   then permits open. A separate vector opens the original and swaps before
   the fresh leaf observation. These exercise different comparisons. The
   equal-inode/different-device case substitutes **only** a device field after
   a real stat and records both real and supplied values. It is a comparison
   test; it cannot prove behavior on a second real filesystem.
6. M-4's hash shim pauses on the qualification hash regardless of whether the
   argument is the descriptor or canonical path. While a regular replacement
   occupies the leaf, invoke native sha256sum on the **actual supplied argument**,
   save its actual result, restore the original name, then return that result.
   Require successful proof with the independent original hash. A shim that
   always hashes FD 8 would silently repair PC-4 and is forbidden.
7. M-5 tampers after staging verification/publication, before final capture;
   leave a valid, executable, single-link, wrong-hash script. Use real hashing
   for the changed bytes, and assert no jq invocation at all. The narrowly
   scoped existing simulation may identify the unmodified install payload as
   the pin, but must not pin arbitrary bytes or all `/proc` arguments. This
   proves ordering under an admitted fixture artifact, not release authenticity.
8. M-6 swaps after the qualification hash, M-7 inside the original version
   wrapper before it returns, M-8 inside the original true wrapper before it
   returns, M-9 inside the original false wrapper before it returns, and M-10
   inside the admitted formatter before it returns valid JSON. Persistent
   regular/symlink replacements remain in place through final admission.
   Preserve each probe's real output and exit code when adding its barrier.
9. Run real direct helper and real wrapper with separate fresh fixtures where
   filesystem effects differ. Wrapper arms must use the SSH child shim's real
   `bash -s` route, never `SyntheticProof`. Explicitly remove inherited
   `C727_TEST_STATE` for these children: with that variable set, the real wrapper
   chooses the unrelated synthetic prerequisite route. Initialize the private
   Git source after writing fixed fixture scripts; runtime markers/swaps remain
   ignored so provision reaches the helper's intended guard.
10. Every new method carries `ParallelLimiter<ProcessSpawnLimit>`. Start
    stdout/stderr drains immediately. Keep the current 5-second barrier and
    10-second execution bounds; release in `finally` and await the run even
    when an assertion fails. Record/retain the barrier child's process identity
    and join it before deleting scratch. A timeout, missed barrier, build error
    or rescued child is failure, never a positive-control red.

The owner/pin shims may continue simulating root installation in a non-root
private fixture. They must preserve **real** device/inode/link values, mode
observations used for identity safety, and M-4's raw digest. Retarget existing
`final-owner`, `final-group`, `final-mode` faults to the admitted descriptor
metadata, one field at a time. Put `final-digest` at the final installed digest
check if that legacy guard is the intended assertion; a pre-execution mismatch
belongs to M-5. Prove each vector reaches its named fault with an independent
trace marker. A generic exit 2 from an earlier check does not preserve coverage.

### Delivery inventory

There are **zero new or changed asynchronous delivery paths**. This helper is
a synchronous child process returning stdout/stderr to a waiting wrapper. No
queue, session input, outbox, wake-up or recovery worker is introduced. Busy
recipient, already-eligible recipient and queue handoff recovery tests therefore
have no applicable path. No session-delivery or UserPrompt claim is made.

For the synchronous evidence path, the producer is the admitted FD-backed jq
formatter/helper, the recipient is the real `Invoke-HostJq` parser, and the
persistence boundary is its exclusive `CreateNew` receipt followed by
`Flush(true)` and close. Identity is source SHA + run ID + selected/executing
phase + mode, with canonical public paths and the observed digest. Recovery is
a new invocation/new run, not replay of an old receipt. M-10's wrapper control
must read the actual persisted document and match those fields using
`AssertReceipt`; refusal must leave zero success receipts and no success banner.
The SSH substitute exercises streaming, parsing and private persistence; it
does not prove a real remote host, SSH authentication, later deployment use or
user-session receipt. A printed request or successful child start is insufficient.

Existing transport crash/timeout and receipt-write failure behavior is unchanged
and remains CARD-1025's separately designed obligation. This card does not
claim it is reverified by the three selected rows. M-10 must at least prove that
the helper's changed success-emission boundary reaches the real wrapper and its
actual receipt, not stop at a synthetic JSON assertion.

### Proves it works now

These are intended ordinary V/R cases, not claims that unimplemented methods
already pass. All M-n names refer to `HostJqPrerequisiteScriptTests`.

- V-1: hardlink refusal in check and existing provision | native private helper
  and wrapper | `C1058_Check_rejects_canonical_hardlink` | real nlink 2 refuses
  before jq, no install effects or success receipt, unchanged tuple/bytes/links;
  direct and alias single-link controls succeed. Add a post-lock hardlink arm:
  hold the real lock, let missing provision wait, create the hardlinked jq,
  release, and require invalid/no download.
- V-2: publication leaves exactly one destination link | native filesystem |
  `C1058_Provision_removes_only_owned_stage_link` | install succeeds, native
  nlink 1 before first jq call, only owned stage leaf removed, destination
  tuple/hash retained; second provision has no install effects. Include unlink
  failure, retained destination, owned cleanup and foreign sentinels.
- V-3: one opened inode is admitted | native helper, declared metadata faults |
  `C1058_Open_descriptor_must_match_canonical_leaf` | before-open and after-open
  substitutions refuse before any jq call; malformed/unavailable observations
  are invalid, never missing/install authority. Healthy native and shebang FD
  controls pass. Check, existing provision and locked qualification are distinct
  vectors, not implicit coverage from calling `qualify` once.
- V-4: proof hashes the retained file | native SHA-256 and transient rename |
  `C1058_Digest_is_bound_to_open_descriptor` | healthy success contains the
  independently measured original digest, not the different replacement digest.
- V-5: newly installed bytes meet the pin before execution | native helper with
  declared install substitution | `C1058_Installed_pin_is_checked_before_execution`
  | changed published bytes produce zero jq calls and exit 2; healthy install
  succeeds; functional existing non-pin jq remains accepted in check/provision.
- V-6: version uses the descriptor | native helper |
  `C1058_Version_probe_uses_open_descriptor` | original version executes once,
  foreign version never executes, persistent replacement yields no success JSON.
- V-7: true predicate uses the descriptor | native helper |
  `C1058_True_probe_uses_open_descriptor` | original true call executes once,
  foreign true call count is zero; no successful proof after replacement.
- V-8: false predicate uses the descriptor | native helper |
  `C1058_False_probe_uses_open_descriptor` | original false call executes once
  with its real false/1 result, foreign false count is zero; no successful proof.
- V-9: formatter uses the descriptor | native helper |
  `C1058_Receipt_formatter_uses_open_descriptor` | original formatter executes,
  foreign formatter count is zero and buffered success does not escape.
- V-10: changed final state cannot release proof | native helper plus real
  wrapper/private SSH child | `C1058_Changed_leaf_cannot_emit_success_proof` |
  admitted formatter returns valid JSON, persistent replacement exits 2 with
  empty success stdout and zero success receipts/banner. Unchanged control
  persists canonical public paths and descriptor-derived observations.

M-6..M-9 each cross check/existing-provision with regular/symlink replacement
(four vectors per method). Installed pin ordering is M-5; repeating all late
swaps through download is excluded because it adds installation effects without
another execution site. M-4 intentionally restores the leaf to isolate hashing;
it does not assert ABA resistance. Initial canonical symlink, home-shadow,
missing, nonexecutable, invalid predicate, install no-clobber/cleanup and wrapper
schema boundaries retain the selected C1025 assertions. Final hardlink growth,
permission loss and malformed metadata cannot be omitted merely because final
inode replacement is already tested; they require the independent controls below.

### Guards the regression

- R-1: hardlink bytes can be healthy and still forbidden | M-1's native nlink
  witness, zero traced jq calls and unchanged foreign names are decisive.
- R-2: `ln -T` temporarily makes two links | M-2 must observe nlink 1 **before**
  qualification; post-exit cleanup reaching nlink 1 is not sufficient.
- R-3: pathname admission and opened identity diverge | M-3 asserts zero total
  jq calls at each pre-execution mismatch, not only eventual refusal.
- R-4: hashing silently follows the replacement leaf | M-4 succeeds yet asserts
  exact equality to independently hashed original bytes.
- R-5: final pin rejection occurs after untrusted execution | M-5's zero-call
  assertion precedes any claim based on the helper's eventual exit.
- R-6: version reopens the path | M-6 requires zero foreign version calls.
- R-7: true predicate reopens the path | M-7 requires zero foreign true calls.
- R-8: false predicate reopens the path | M-8 requires zero foreign false calls.
- R-9: diagnostic/proof formatting executes a substituted file | M-9 requires
  zero foreign formatter calls even when final admission refuses.
- R-10: valid JSON escapes after the canonical leaf changed | M-10 requires
  empty helper success stdout **and** no real wrapper success receipt/banner.

### Guard inventory

The ten proposed PCs map to the first ten **behavior families** below, not to
ten independent guards. `PC needed` is an identified design gap, not a silently
waived control. Additional guard rows deliberately split independently bypassable
admission boundaries. This is a rejection inventory; Plan must finish the
per-site inventory after fixing the observation order and cannot call it complete.

| Guard | Plan reference and safety-critical condition | Proposed control / disposition |
|---|---|---|
| G-1 | D-1 initial canonical leaf has exactly one link | PC-1 is composite: it also removes opened/final checks; split it. |
| G-2 | D-4 successful publication removes the owned stage name before qualification | PC-2, healthy install witnesses missing unlink. |
| G-3 | D-2 opened identity equals the pre-open observation | PC-3 is composite with G-11; split it. |
| G-4 | D-2/D-3 digest comes from retained descriptor | PC-4, real-hash transient swap. |
| G-5 | D-3 installed descriptor digest matches fixed pin before any jq call | PC-5, wrong-hash executable and zero calls. |
| G-6 | D-2 version executes via retained descriptor | PC-6. |
| G-7 | D-2 true predicate executes via retained descriptor | PC-7. |
| G-8 | D-2 false predicate executes via retained descriptor | PC-8. |
| G-9 | D-2 formatter executes via retained descriptor | PC-9. |
| G-10 | D-5 final canonical identity still equals admitted identity | PC-10 currently removes the entire final block; narrow it. |
| G-11 | D-2 opened identity also equals the fresh post-open leaf observation | Distinct PC needed; pre-open equality does not protect an after-open replacement. |
| G-12 | D-2 device participates in identity equality, not inode alone | Distinct PC needed; isolate each independently implemented comparison. |
| G-13 | D-1/D-2 opened file has exactly one link before execution | Distinct PC needed; initial leaf nlink is an earlier observation. |
| G-14 | D-5 final canonical leaf is still single-linked | Distinct PC needed; adding a hardlink preserves device/inode and bytes. |
| G-15 | D-5 final descriptor is still single-linked | Distinct PC needed; isolate its observation from the final leaf's nlink guard. |
| G-16 | D-5 final executable-state admission | Distinct PC needed; chmod can preserve device/inode/hash. Separate leaf/descriptor checks if implemented independently. |
| G-17 | D-5 no buffered success bytes are printed before final admission succeeds | Distinct PC needed; deleting final admission does not test early printing while refusal remains. |
| G-18 | D-4 unsuccessful staged-name removal refuses even if a side effect occurred | Distinct PC needed; leaving both links masks a bypass with D-1. |
| G-19 | D-2 malformed numeric identity metadata refuses | Distinct PC needed; missing fields cannot be treated as valid equal identities. |
| G-20 | Implementation shape: open/procfs/stat/hash failure is invalid, never missing authority or pathname fallback | Distinct PC needed; enumerate each independently bypassable failure edge in the revised shape. |

Inherited PATH admission, predicate semantics, downloaded/staged pin checks,
no-clobber, source custody, final installed owner/mode/version, wrapper parsing
and persistence are unchanged CARD-1025 guards, with its 67 pending controls.
They are retained dependencies, not discharged here. Regular-file type of an
already-open retained inode cannot change in place; do not invent a native
same-inode regular-to-symlink mutation. Canonical leaf non-symlink/regular checks
may overlap non-following identity admission. Plan must explicitly distinguish
redundant checks from independently bypassable checks; a whole-block deletion
cannot establish either conclusion. Existing-mode compatibility additionally
needs a non-pin success assertion, retained in V-5 and C1025; a wrong installed
pin is not an existing-mode test.

Audit at this stage: **20 identified guard rows, 10 proposed control IDs;
at least 10 rows lack a distinct assigned control; PC-1, PC-3 and PC-10 are
composite deletions.** Further splitting of G-12/G-16/G-20 depends on the revised
observation shape. This fails the required bijection and executable-PC gate;
reporting `missing=0` or a final total of ten would be false.

### Positive controls

The following preserves the ten exact-method candidates and names the decisive
assertions. It is a review of proposed controls, **not** Mutation authorization
or executed red/green evidence. The revised plan must assign new IDs to each
additional independent guard rather than hide variants inside a composite PC.

| PC | Break by a syntactically valid helper defect | Exact method in `HostJqPrerequisiteScriptTests` and required red | Design verdict |
|---|---|---|---|
| PC-1 | Bypass all nlink-equals-one checks as originally proposed. | `C1058_Check_rejects_canonical_hardlink`: `c1058-hardlink-no-execution`, traced calls must equal 0. | Reject composite; initial and later nlink checks need isolated controls. |
| PC-2 | Omit owned staged-name unlink, retaining normal EXIT cleanup. | `C1058_Provision_removes_only_owned_stage_link`: `c1058-install-single-link-before-probe`, healthy provision exit must equal 0. | Viable for G-2; not proof of unlink error handling. |
| PC-3 | Bypass both pre-open and fresh-leaf identity comparisons. | `C1058_Open_descriptor_must_match_canonical_leaf`: `c1058-open-no-execution`, total jq calls must equal 0. | Reject composite; split comparisons and device omission. |
| PC-4 | Hash `"$resolved"` instead of `/proc/self/fd/8`, retaining FD execution. | `C1058_Digest_is_bound_to_open_descriptor`: `c1058-digest-original`, proof digest must equal the independent original hash. | Viable only with the raw-argument hash barrier above. |
| PC-5 | Remove only installed pre-execution pin comparison; retain final installed pin check. | `C1058_Installed_pin_is_checked_before_execution`: `c1058-installed-pin-no-execution`, jq-call count must equal 0. | Viable; final refusal must not mask execution. |
| PC-6 | Change only version invocation to `"$resolved"`. | `C1058_Version_probe_uses_open_descriptor`: `c1058-version-foreign-zero`, foreign version count must equal 0. | Viable with a healthy foreign script. |
| PC-7 | Change only true invocation to `"$resolved"`. | `C1058_True_probe_uses_open_descriptor`: `c1058-true-foreign-zero`, foreign true count must equal 0. | Viable with barrier before true starts. |
| PC-8 | Change only false invocation to `"$resolved"`. | `C1058_False_probe_uses_open_descriptor`: `c1058-false-foreign-zero`, foreign false count must equal 0. | Viable with false/1 fixture semantics preserved. |
| PC-9 | Change only formatter invocation to `"$resolved"`. | `C1058_Receipt_formatter_uses_open_descriptor`: `c1058-formatter-foreign-zero`, foreign formatter count must equal 0. | Viable even if a later guard refuses. |
| PC-10 | Print buffered success without any final identity/state admission. | `C1058_Changed_leaf_cannot_emit_success_proof`: `c1058-final-success-stdout-empty`, helper stdout must be empty. | Reject composite; final identity and output ordering need different mutations. |

Concrete repair witnesses, to keep the next Plan bounded:

- For G-3 alone, replace the regular leaf after pre-open stat but before open,
  then keep the replacement stable. Bypassing only pre-open equality permits
  foreign calls even with fresh-leaf and final checks intact.
- For G-11 alone, open the original first, replace before the fresh leaf
  observation, then keep it stable. Bypassing only fresh-leaf equality permits
  **original** jq calls before final refusal. Assert zero total calls; zero
  foreign calls would miss this mutant.
- For G-12, return the native inode with a different numeric device at just one
  selected observation, keeping other fields real. Remove only that comparison's
  device equality; assert pre-execution zero calls or final empty stdout as
  appropriate. This is a declared metadata substitute, not a real cross-device
  witness.
- For G-14, add a real hardlink during the admitted formatter, retaining the
  original leaf/inode/hash. Removing only final leaf nlink admission can still
  be masked by final descriptor nlink admission. A timed add/remove around
  separate native observations can isolate the two only after Plan fixes their
  ordering; do not credit the existing persistent-swap test for either guard.
- For G-16, chmod the original during formatter return without changing its
  inode or bytes. A separate descriptor executable check can mask omission of
  the leaf check. Plan must specify an isolatable observation boundary or
  justify redundancy before calling either PC executable.
- For G-17, move the one successful `printf` before the still-active final
  validation and remove its old location. Final refusal remains exit 2 and the
  wrapper still refuses; direct helper stdout alone turns red at the named
  empty-stdout assertion. This compiles and is distinct from PC-10.
- For G-18, the private rm shim performs the native unlink successfully, records
  real nlink 1, then returns nonzero. Remove only the return-status refusal.
  Require direct exit 2 and no success proof; with this defect qualification
  succeeds, so the nlink guard cannot mask it. Do not mutate cleanup's rm arm.
- For G-19/G-20, use separate malformed-field and valid-output/nonzero-exit
  vectors at the exact observation, and an open-failure vector before jq.
  Missing, nonnumeric and command-error outcomes must not be credited to an
  earlier unrelated path refusal. Return-code guards and parsing guards are
  separate when they can be bypassed independently.

After the repaired design is accepted, Mutation runs each control serially:
baseline, break/red, exact restore/green. Every phase selects exactly
`/*/*/HostJqPrerequisiteScriptTests/<the listed literal method>` with
`-MinExecuted 1`; no method-prefix wildcard, class or Unit suite is needed for
these non-parameterized methods. Require one TUnit result and the intended
assertion failure, not a compile/fixture/timeout failure. Use the copied external
checkpoint driver and external evidence root specified by the SourceLanding
brief. Code executes ordinary V/R; Review judges these designs before land;
post-land Mutation remains separately commissioned and leaves no source edits.

### Out of scope

- Whole Unit/class/namespace, the full installer/image/legacy roster, and
  CARD-1040's fifteen methods: this card has three exact narrow selections.
- Image activation, live outer-host installation/check, deployment, restart,
  real SSH and fleet configuration: private fixture proof cannot establish them.
- Protecting in-place writes, ABA between observations, a malicious toolchain,
  or replacements after proof emission: D-7 explicitly retains these trust
  boundaries. The M-4 ABA-shaped setup isolates hash targeting only.
- Native memfd/fexecve, immutable byte custody, descriptor transfer to later
  consumers and wrapper re-stat: excluded by D-6/D-7, not implied by green tests.
- CARD-1025's 67 and CARD-1054's 13 Mutation obligations: unchanged and pending.
  No historical result is relabeled as current evidence.

### Checkpoints

**Selection retained, execution freeze withheld.** This table is deliberately
byte-for-byte identical to the earlier proposed table: the importer selects the
first one. These remain three isolated builds, 26 executions of 21 distinct
methods, with eleven existing methods and ten proposed new non-parameterized
methods. Require exact expanded roster equality, not just Min. Repairing the
PC inventory need not broaden these ordinary filters: add boundary vectors to
the ten named methods, subject to honest timing review.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1058-links/` | linux-host-jq-links | `/*/*/HostJqPrerequisiteScriptTests/(C1058_Check_rejects_canonical_hardlink*)\|(C1058_Provision_removes_only_owned_stage_link*)\|(C1025_Check_qualifies_deployment_shell*)\|(C1025_Check_has_no_install_effects*)\|(C1025_Provision_requires_missing_jq*)\|(C1025_Provision_serializes_and_rechecks*)\|(C1025_Provision_publishes_complete_no_clobber*)\|(C1025_Provision_cleans_only_owned_staging*)\|(C1025_Provision_requalifies_published_jq*)` | V-1, V-2, R-1, R-2 | exactly the 9 listed methods, 9 passed, 0 failed/skipped | 9 | 8 | true |
| CP-2 | S2 | `tests/Antiphon.Tests -> bin-c1058-identity/` | linux-host-jq-identity | `/*/*/HostJqPrerequisiteScriptTests/(C1058_Open_descriptor_must_match_canonical_leaf*)\|(C1058_Digest_is_bound_to_open_descriptor*)\|(C1058_Installed_pin_is_checked_before_execution*)\|(C1058_Version_probe_uses_open_descriptor*)\|(C1058_True_probe_uses_open_descriptor*)\|(C1058_False_probe_uses_open_descriptor*)\|(C1058_Receipt_formatter_uses_open_descriptor*)\|(C1058_Changed_leaf_cannot_emit_success_proof*)` | V-3, V-4, V-5, V-6, V-7, V-8, V-9, V-10, R-3, R-4, R-5, R-6, R-7, R-8, R-9, R-10 | exactly M-3..M-10, 8 passed, 0 failed/skipped | 8 | 7 | true |
| CP-3 | S2 | `tests/Antiphon.Tests -> bin-c1058-compat/` | linux-host-jq-compat | `/*/*/HostJqPrerequisiteScriptTests/(C1058_Check_rejects_canonical_hardlink*)\|(C1058_Provision_removes_only_owned_stage_link*)\|(C1025_Check_qualifies_deployment_shell*)\|(C1025_Check_rejects_canonical_leaf_symlink*)\|(C1025_Receipt_rejects_canonical_lookup_with_unapproved_target*)\|(C1025_Check_has_no_install_effects*)\|(C1025_Provision_verifies_download_before_use*)\|(C1025_Provision_requalifies_published_jq*)\|(C1025_Receipt_requires_complete_current_proof*)` | V-1, V-2, V-3, V-4, V-5, V-10, R-1, R-2, R-3, R-4, R-5, R-10 | exactly M-1/M-2 and the 7 listed existing methods, 9 passed, 0 failed/skipped | 9 | 8 | true |

After Plan repair and a successful TestDesign freeze, retain S1 -> CP-1 and
S2 -> serial CP-2/CP-3, one checkpoint-tool run per committed/pushed slice.
The earlier build-slot bootstrap and `--after` commands remain the intended
commands; no run is authorized by this rejection appendix. Await every exit-75
run to completion, preserve unedited CHECKPOINT lines and SHA-validated source
receipts, and remove only owned alternate outputs after children exit. Code and
Review run the full-task-range evidence-diff guard. Documentation-only
TestDesign needs diff/link/manifest inspection, not a test-project build.

### Cost

All numbers below are **estimates**, not measured wall time. Slot queueing and
authoring are additional. The missing controls mean the original 119-minute
total cannot be represented as the complete safety-verification floor.

- Ordinary Code V/R floor remains **23 minutes**: CP-1's exact nine-method OR
  filter 8, CP-2's exact eight-method OR filter 7, CP-3's exact nine-method OR
  filter 8. Each includes one isolated build. Initial prerequisite/checkpoint
  bootstrap is **3 minutes**, giving **26 minutes** setup plus ordinary proof.
- The original ten literal PC method filters cost **90 minutes**: ten baseline
  build/runs at 3 minutes and ten red/restore/green cycles at 6 minutes. The
  estimate is 3 minutes per build/run; restoration is included in the 6-minute
  cycle allowance. PC setup adds **3 minutes**. Thus **119 minutes** is only the
  original, incomplete ten-control scope: 3 + 23 + 3 + 90.
- A lower-bound repair budget with **20 independent controls** is **180 minutes**
  of PC execution (20 x (3 baseline + 3 red + 3 restored green)), **60 method
  executions**, plus 3 setup. Ordinary Code plus that Mutation floor is
  **209 minutes**: 3 + 23 + 3 + 180. This is **90 minutes more** than the rejected
  ten-control budget. G-12/G-16/G-20 can require further splits; each additional
  exact-method PC adds **9 minutes**, not a whole-class rerun. Plan must name
  those filters and freeze the final count before dispatch, rather than treat
  this lower bound as a completed estimate.
- Separate ordinary Review adds the same **26 minutes**, giving a **235-minute
  lower bound** across Code, Review and repaired Mutation, excluding authoring.
  No PC result is obtained by Code/Review green. No measured savings are claimed
  (**0 minutes**); there is no comparable complete baseline timing. The three
  narrow ordinary selections are retained to avoid unrelated broad work.

--- next stage ---
next: plan
handoff: Repair CARD-1058 verification: split PC-1/PC-3/PC-10 and assign distinct controls to final nlink/permissions, stdout ordering, unlink error and metadata failure guards. Specify isolatable native observation barriers; revise the ten-PC cap/cost. Preserve D-1..D-8, two slices and three narrow Linux filters, then return to TestDesign; Code is not authorized.
artifact: docs/superpowers/plans/2026-10-05-card-1058-host-jq-file-identity-plan.md
