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
