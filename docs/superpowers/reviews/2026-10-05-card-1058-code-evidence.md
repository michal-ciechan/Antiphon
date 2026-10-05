# CARD-1058 Code evidence

S1 and S2 are implemented and ordinary verification is complete: CP-1 10/10, CP-2 15/15, CP-3 10/10; zero final failures or skips. Next: Review. All 29 positive controls remain pending for separately commissioned post-land SourceLanding Mutation.

Plan: [frozen implementation and verification design](../plans/2026-10-05-card-1058-host-jq-file-identity-plan.md).
Original Code task / landing owner: `e6f3b241-608d-4299-a402-dc978134d36e`.
Branch: `feat/card-task-e6f3b241`.
Worktree: `/work/worktrees/task-e6f3b241`.
Base: `719fc095a4b8ad198824f20ccb744764bdd41666`.
Final tested implementation SHA: `a934273624340717ee553ef2293cc994daf7aebd`. This report is a subsequent documentation-only commit, not an additional test claim.

## Implementation

- `scripts/server2-host-jq.sh`: shared direct-call identity parser; P0/O/F0/L0/H then FD version/true/false/formatter, L1 and F1. Five independent exact-one-link policies, separate device/inode comparisons, installed pin before execution, buffered proof and two final live executable checks. FD 8 is closed before each attempt and after successful emission; process refusal closes it. FD 9 retains lock ownership.
- Publication keeps atomic no-clobber `ln -T`, then removes only the owned stage leaf and refuses either unlink failure form before qualification. EXIT cleanup and published destination custody remain intact.
- `tests/Antiphon.Tests/Scripts/HostJqPrerequisiteScriptTests.cs`: all 18 named non-parameterized methods, native tuples/hashes/hardlinks, independent C# hashes, real native and traced shebang controls, named before/after barriers, per-invocation nonce, inherited-FD observations, PID/start identity custody and unchanged five-/ten-second deadlines. Existing owner/group/mode faults target FD metadata. Final-digest now asserts pre-execution refusal and zero calls.
- `docs/docker-stack.md` and the outer deployment host row in `docs/testing-and-build.md`: host-only guarantees, unchanged existing-version policy and remaining trust boundary. The CARD-1040 fifteen-method manifest, image/child qualification, wrapper, c590 consumer and CARD-1054 sources are unchanged. These narrow owner-document edits are identified for the caller/CARD-1040 owner to coordinate at Review.

## Scope and setup

The explicit Code brief and frozen D-8/checkpoint table authorize only CP-1 then serial CP-2/CP-3. No whole Unit, class, namespace or assembly run was made. This is Final under that specific closed scope; no ordinary V/R or manual acceptance ID is deferred. The private wrapper route proves SSH-child transport, parsing and durable local receipt persistence, not remote authentication or host/image activation.

ANTIPHON_TASK_TOKEN was present. GET /api/runner-defaults and GET /api/session-runners succeeded on 2026-10-05 (defaults revision 2; eligible native Linux lane). No fleet placement/configuration was changed. Fixture prerequisites were exercised in the actual child environment: native Bash/jq, procfs, GNU stat/sha256sum/hardlinks, pwsh/git/node. No host tool installation or shared PATH mutation occurred.

The only build outside checkpoint rows was the plan-authorized checkpoint bootstrap, through scripts/build-slot.ps1, at S1 SHA 86d454763d9cc293bcf49f432a65fbf172f40df1:

```text
pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1058-checkpoint-bootstrap -- dotnet build tools/Antiphon.Checkpoints --property:OutputPath=bin-c1058-tool/ --property:UseAppHost=false --nologo
BUILD SLOT granted lease=89fb8af2-0b6c-413a-be24-1d7ade18d2ac waited=0s maxcpucount=6
BUILD SLOT released lease=89fb8af2-0b6c-413a-be24-1d7ade18d2ac held=5s
```

Bootstrap succeeded (zero errors). Each checkpoint row had its own isolated output build and exact filter through the tool-owned build-slot gate. No unleased run, timeout increase, assertion weakening, deliberate mutant, loaded repetition or unlisted test selection occurred.

## Commits and repairs

| Commit | Outcome |
|---|---|
| `86d454763d9cc293bcf49f432a65fbf172f40df1` | S1; first CP-1 9 passed / 1 failed: newly generated fixture PID had `$` instead of `$$`. |
| `6596700c840990593e08fbbd5412465ee78c388d` | Repair 1; CP-1 10/10 green. |
| `575df78f4887a15dfeef8d9f8c25674058837d05` | S2; CP-2 14/15, CP-3 10/10. Metadata test correctly refused malformed input but its ledger separator expanded unquoted `~` to the home directory. |
| `a934273624340717ee553ef2293cc994daf7aebd` | Repair 2; quoted ledger separator, invocation nonce and witness ordering; native existing-file owner metadata. CP-2 15/15 and CP-3 10/10 green. |

Every slice/fix was committed and pushed before its checkpoint group. Source stayed frozen until each whole group terminated. Both failures were introduced fixture defects, not inherited red or positive-control evidence. The permitted two repair rounds were used. CP-3 reran because the shared fixture changed; no unchanged green proof was repeated afterward.

## Checkpoint records (unedited)

Run `20261005-115126-1614`, tested `86d454763d9cc293bcf49f432a65fbf172f40df1`, source state `clean`, build provenance `verified`.
Structured receipt: `/work/worktrees/task-e6f3b241/.antiphon/checkpoints/20261005-115126-1614/report.json`.

```text
CHECKPOINT CP-1 commit=86d454763d9cc293bcf49f432a65fbf172f40df1 build=ok filter=/*/*/HostJqPrerequisiteScriptTests/(C1058_Check_rejects_canonical_hardlink*)|(C1058_Provision_removes_only_owned_stage_link*)|(C1058_Staged_unlink_failure_refuses*)|(C1025_Check_qualifies_deployment_shell*)|(C1025_Check_has_no_install_effects*)|(C1025_Provision_requires_missing_jq*)|(C1025_Provision_serializes_and_rechecks*)|(C1025_Provision_publishes_complete_no_clobber*)|(C1025_Provision_cleans_only_owned_staging*)|(C1025_Provision_requalifies_published_jq*) executed=10 passed=9 failed=1 skipped=0 trx=/work/worktrees/task-e6f3b241/.antiphon/checkpoints/20261005-115126-1614/rows/CP-1/run.trx slot=granted waited=0s dirty=0 source=86d454763d9cc293bcf49f432a65fbf172f40df1 sourceState=clean buildSource=verified
```

Run `20261005-115532-b0cc`, tested `6596700c840990593e08fbbd5412465ee78c388d`, source state `clean`, build provenance `verified`.
Structured receipt: `/work/worktrees/task-e6f3b241/.antiphon/checkpoints/20261005-115532-b0cc/report.json`.

```text
CHECKPOINT CP-1 commit=6596700c840990593e08fbbd5412465ee78c388d build=ok filter=/*/*/HostJqPrerequisiteScriptTests/(C1058_Check_rejects_canonical_hardlink*)|(C1058_Provision_removes_only_owned_stage_link*)|(C1058_Staged_unlink_failure_refuses*)|(C1025_Check_qualifies_deployment_shell*)|(C1025_Check_has_no_install_effects*)|(C1025_Provision_requires_missing_jq*)|(C1025_Provision_serializes_and_rechecks*)|(C1025_Provision_publishes_complete_no_clobber*)|(C1025_Provision_cleans_only_owned_staging*)|(C1025_Provision_requalifies_published_jq*) executed=10 passed=10 failed=0 skipped=0 trx=/work/worktrees/task-e6f3b241/.antiphon/checkpoints/20261005-115532-b0cc/rows/CP-1/run.trx slot=granted waited=0s dirty=0 source=6596700c840990593e08fbbd5412465ee78c388d sourceState=clean buildSource=verified
```

Run `20261005-120601-878b`, tested `575df78f4887a15dfeef8d9f8c25674058837d05`, source state `clean`, build provenance `verified`.
Structured receipt: `/work/worktrees/task-e6f3b241/.antiphon/checkpoints/20261005-120601-878b/report.json`.

```text
CHECKPOINT CP-2 commit=575df78f4887a15dfeef8d9f8c25674058837d05 build=ok filter=/*/*/HostJqPrerequisiteScriptTests/(C1058_Open_descriptor_must_match_canonical_leaf*)|(C1058_Digest_is_bound_to_open_descriptor*)|(C1058_Installed_pin_is_checked_before_execution*)|(C1058_Version_probe_uses_open_descriptor*)|(C1058_True_probe_uses_open_descriptor*)|(C1058_False_probe_uses_open_descriptor*)|(C1058_Receipt_formatter_uses_open_descriptor*)|(C1058_Changed_leaf_cannot_emit_success_proof*)|(C1058_Pre_execution_link_checks_are_independent*)|(C1058_Identity_comparisons_include_device*)|(C1058_Final_link_checks_are_independent*)|(C1058_Final_permission_checks_are_independent*)|(C1058_Proof_is_buffered_until_final_admission*)|(C1058_Metadata_syntax_refuses*)|(C1058_Observation_failures_are_invalid*) executed=15 passed=14 failed=1 skipped=0 trx=/work/worktrees/task-e6f3b241/.antiphon/checkpoints/20261005-120601-878b/rows/CP-2/run.trx slot=granted waited=0s dirty=0 source=575df78f4887a15dfeef8d9f8c25674058837d05 sourceState=clean buildSource=verified
CHECKPOINT CP-3 commit=575df78f4887a15dfeef8d9f8c25674058837d05 build=ok filter=/*/*/HostJqPrerequisiteScriptTests/(C1058_Check_rejects_canonical_hardlink*)|(C1058_Provision_removes_only_owned_stage_link*)|(C1058_Staged_unlink_failure_refuses*)|(C1025_Check_qualifies_deployment_shell*)|(C1025_Check_rejects_canonical_leaf_symlink*)|(C1025_Receipt_rejects_canonical_lookup_with_unapproved_target*)|(C1025_Check_has_no_install_effects*)|(C1025_Provision_verifies_download_before_use*)|(C1025_Provision_requalifies_published_jq*)|(C1025_Receipt_requires_complete_current_proof*) executed=10 passed=10 failed=0 skipped=0 trx=/work/worktrees/task-e6f3b241/.antiphon/checkpoints/20261005-120601-878b/rows/CP-3/run.trx slot=granted waited=15s dirty=0 source=575df78f4887a15dfeef8d9f8c25674058837d05 sourceState=clean buildSource=verified
```

Run `20261005-121736-958d`, tested `a934273624340717ee553ef2293cc994daf7aebd`, source state `clean`, build provenance `verified`.
Structured receipt: `/work/worktrees/task-e6f3b241/.antiphon/checkpoints/20261005-121736-958d/report.json`.

```text
CHECKPOINT CP-2 commit=a934273624340717ee553ef2293cc994daf7aebd build=ok filter=/*/*/HostJqPrerequisiteScriptTests/(C1058_Open_descriptor_must_match_canonical_leaf*)|(C1058_Digest_is_bound_to_open_descriptor*)|(C1058_Installed_pin_is_checked_before_execution*)|(C1058_Version_probe_uses_open_descriptor*)|(C1058_True_probe_uses_open_descriptor*)|(C1058_False_probe_uses_open_descriptor*)|(C1058_Receipt_formatter_uses_open_descriptor*)|(C1058_Changed_leaf_cannot_emit_success_proof*)|(C1058_Pre_execution_link_checks_are_independent*)|(C1058_Identity_comparisons_include_device*)|(C1058_Final_link_checks_are_independent*)|(C1058_Final_permission_checks_are_independent*)|(C1058_Proof_is_buffered_until_final_admission*)|(C1058_Metadata_syntax_refuses*)|(C1058_Observation_failures_are_invalid*) executed=15 passed=15 failed=0 skipped=0 trx=/work/worktrees/task-e6f3b241/.antiphon/checkpoints/20261005-121736-958d/rows/CP-2/run.trx slot=granted waited=0s dirty=0 source=a934273624340717ee553ef2293cc994daf7aebd sourceState=clean buildSource=verified
CHECKPOINT CP-3 commit=a934273624340717ee553ef2293cc994daf7aebd build=ok filter=/*/*/HostJqPrerequisiteScriptTests/(C1058_Check_rejects_canonical_hardlink*)|(C1058_Provision_removes_only_owned_stage_link*)|(C1058_Staged_unlink_failure_refuses*)|(C1025_Check_qualifies_deployment_shell*)|(C1025_Check_rejects_canonical_leaf_symlink*)|(C1025_Receipt_rejects_canonical_lookup_with_unapproved_target*)|(C1025_Check_has_no_install_effects*)|(C1025_Provision_verifies_download_before_use*)|(C1025_Provision_requalifies_published_jq*)|(C1025_Receipt_requires_complete_current_proof*) executed=10 passed=10 failed=0 skipped=0 trx=/work/worktrees/task-e6f3b241/.antiphon/checkpoints/20261005-121736-958d/rows/CP-3/run.trx slot=granted waited=0s dirty=0 source=a934273624340717ee553ef2293cc994daf7aebd sourceState=clean buildSource=verified
```

## Final receipt and roster validation

Fresh TRX was inspected for every selected result and the full class name `Antiphon.Tests.Scripts.HostJqPrerequisiteScriptTests`. Exact set equality against each literal checkpoint filter was checked: 10 / 15 / 10, all Passed, no extra/missing methods. The final selection is 35 executions / 29 unique methods (18 new, 11 inherited). Across retained attempts: 70 executions, 68 passed, 2 fixture failures, zero skips. Counts below are the successful final selection, not an erasure of failed attempts.

```text
CHECKPOINT SOURCE VALID source=6596700c840990593e08fbbd5412465ee78c388d rows=1
CHECKPOINT SOURCE VALID source=a934273624340717ee553ef2293cc994daf7aebd rows=2
```

Both green schema-2 report.json receipts were SHA-validated using the checkpoint validate command with the exact tested SHA and selected rows. Each final row has dirty=0, sourceState=clean, buildSource=verified, slot=granted, waited=0s. The earlier CP-3 pass waited=15s; all other row waits were zero. Failed evidence remains in its original ignored checkpoint directory.

### CP-1 exact passing roster

- `C1025_Check_has_no_install_effects` — Passed
- `C1025_Check_qualifies_deployment_shell` — Passed
- `C1025_Provision_cleans_only_owned_staging` — Passed
- `C1025_Provision_publishes_complete_no_clobber` — Passed
- `C1025_Provision_requalifies_published_jq` — Passed
- `C1025_Provision_requires_missing_jq` — Passed
- `C1025_Provision_serializes_and_rechecks` — Passed
- `C1058_Check_rejects_canonical_hardlink` — Passed
- `C1058_Provision_removes_only_owned_stage_link` — Passed
- `C1058_Staged_unlink_failure_refuses` — Passed

### CP-2 exact passing roster

- `C1058_Changed_leaf_cannot_emit_success_proof` — Passed
- `C1058_Digest_is_bound_to_open_descriptor` — Passed
- `C1058_False_probe_uses_open_descriptor` — Passed
- `C1058_Final_link_checks_are_independent` — Passed
- `C1058_Final_permission_checks_are_independent` — Passed
- `C1058_Identity_comparisons_include_device` — Passed
- `C1058_Installed_pin_is_checked_before_execution` — Passed
- `C1058_Metadata_syntax_refuses` — Passed
- `C1058_Observation_failures_are_invalid` — Passed
- `C1058_Open_descriptor_must_match_canonical_leaf` — Passed
- `C1058_Pre_execution_link_checks_are_independent` — Passed
- `C1058_Proof_is_buffered_until_final_admission` — Passed
- `C1058_Receipt_formatter_uses_open_descriptor` — Passed
- `C1058_True_probe_uses_open_descriptor` — Passed
- `C1058_Version_probe_uses_open_descriptor` — Passed

### CP-3 exact passing roster

- `C1025_Check_has_no_install_effects` — Passed
- `C1025_Check_qualifies_deployment_shell` — Passed
- `C1025_Check_rejects_canonical_leaf_symlink` — Passed
- `C1025_Provision_requalifies_published_jq` — Passed
- `C1025_Provision_verifies_download_before_use` — Passed
- `C1025_Receipt_rejects_canonical_lookup_with_unapproved_target` — Passed
- `C1025_Receipt_requires_complete_current_proof` — Passed
- `C1058_Check_rejects_canonical_hardlink` — Passed
- `C1058_Provision_removes_only_owned_stage_link` — Passed
- `C1058_Staged_unlink_failure_refuses` — Passed

## Ordinary invariant outcomes

| ID | Literal method | Actual outcome |
|---|---|---|
| V-1 / R-1 | `C1058_Check_rejects_canonical_hardlink` | Passed (CP-1 and latest CP-3). |
| V-2 / R-2 | `C1058_Provision_removes_only_owned_stage_link` | Passed (CP-1 and latest CP-3). |
| V-3 / R-3 | `C1058_Open_descriptor_must_match_canonical_leaf` | Passed (latest CP-2). |
| V-4 / R-4 | `C1058_Digest_is_bound_to_open_descriptor` | Passed (latest CP-2). |
| V-5 / R-5 | `C1058_Installed_pin_is_checked_before_execution` | Passed (latest CP-2). |
| V-6 / R-6 | `C1058_Version_probe_uses_open_descriptor` | Passed (latest CP-2). |
| V-7 / R-7 | `C1058_True_probe_uses_open_descriptor` | Passed (latest CP-2). |
| V-8 / R-8 | `C1058_False_probe_uses_open_descriptor` | Passed (latest CP-2). |
| V-9 / R-9 | `C1058_Receipt_formatter_uses_open_descriptor` | Passed (latest CP-2). |
| V-10 / R-10 | `C1058_Changed_leaf_cannot_emit_success_proof` | Passed (latest CP-2). |
| V-11 / R-11 | `C1058_Staged_unlink_failure_refuses` | Passed (CP-1 and latest CP-3). |
| V-12 / R-12 | `C1058_Pre_execution_link_checks_are_independent` | Passed (latest CP-2). |
| V-13 / R-13 | `C1058_Identity_comparisons_include_device` | Passed (latest CP-2). |
| V-14 / R-14 | `C1058_Final_link_checks_are_independent` | Passed (latest CP-2). |
| V-15 / R-15 | `C1058_Final_permission_checks_are_independent` | Passed (latest CP-2). |
| V-16 / R-16 | `C1058_Proof_is_buffered_until_final_admission` | Passed (latest CP-2). |
| V-17 / R-17 | `C1058_Metadata_syntax_refuses` | Passed (latest CP-2). |
| V-18 / R-18 | `C1058_Observation_failures_are_invalid` | Passed (latest CP-2). |

Manual work: Bash syntax and whitespace/diff checks passed. Native tuple/hash/link/access and no-op custody, both unlink faults, missing/open classification, existing non-pin compatibility, installed pin ordering, and real wrapper success/refusal persistence were exercised by the named methods. No live SSH, installation, deployment, image rebuild or restart is claimed. The post-review read-only host phase is later rollout-owner work, not a deferred ordinary acceptance test.

## Mutation status

Every PC and every named variant is PENDING. Ordinary fault vectors and the two fixture failures do not constitute deliberate mutant red/restore/green. Mutation owns those cycles and missing-control discovery after ordinary Review and caller landing of the original Code task. CARD-1025's 67 and CARD-1054's 13 controls are separate and unchanged.

| PC | Pending variants / witness |
|---|---|
| PC-1 | PENDING — P0-only transient native hardlink; persistent check/provision/wrapper and locked recheck vectors. |
| PC-2 | PENDING — Omitted owned-stage unlink; healthy missing install must fail the single-link-before-probe witness. |
| PC-3 | PENDING — P0-to-open inode replacement; regular/symlink, check/existing provision/locked recheck. |
| PC-4 | PENDING — Transient H pathname swap; actual supplied hash target, independent original/replacement hashes. |
| PC-5 | PENDING — Wrong-pin functional installed replacement before P0; healthy install and existing non-pin controls. |
| PC-6 | PENDING — Qv pathname execution; check/existing provision × regular/symlink. |
| PC-7 | PENDING — Qt pathname execution; check/existing provision × regular/symlink. |
| PC-8 | PENDING — Qf pathname execution; check/existing provision × regular/symlink. |
| PC-9 | PENDING — Qj pathname execution; check/existing provision × regular/symlink. |
| PC-10 | PENDING — L1 inode comparison; Qj regular/symlink replacement, direct and real-wrapper refusal. |
| PC-11 | PENDING — F0-to-L0 inode replacement; regular/symlink, check/existing provision/locked recheck. |
| PC-12 | PENDING — P0 device substitution with native inode retained; check/existing provision. |
| PC-13 | PENDING — L0 device substitution with native inode retained; check/existing provision. |
| PC-14 | PENDING — L1 device substitution with native inode retained; check/existing provision. |
| PC-15 | PENDING — F0-only native extra link; check/existing provision/locked recheck. |
| PC-16 | PENDING — L1-only native extra link, removed before F1; check/existing provision. |
| PC-17 | PENDING — F1-only native extra link after L1; check/existing provision. |
| PC-18 | PENDING — L1 live access loss; optional F1 restoration only if reached; check/existing provision. |
| PC-19 | PENDING — F1 live access loss after L1 admission; check/existing provision. |
| PC-20 | PENDING — Premature success printf; direct stdout on Qj replacement and healthy one-document control. |
| PC-21 | PENDING — Stage unlink succeeds then reports failure; separate failure-without-side-effect arm. |
| PC-22 | PENDING — Valid stat stdout plus nonzero status at P0/F0/L0/L1/F1; check/existing provision. |
| PC-23 | PENDING — Extra-token suffix framing at each snapshot; multiline/missing-field ordinary vectors retained. |
| PC-24 | PENDING — Consistent nonnumeric device at all snapshots first; each individual snapshot also covered. |
| PC-25 | PENDING — Consistent nonnumeric inode at all snapshots first; each individual snapshot also covered. |
| PC-26 | PENDING — Real failed open after P0 in initial provision; zero download/install authority. |
| PC-27 | PENDING — Valid hash stdout with nonzero exit at H; check/existing provision. |
| PC-28 | PENDING — Malformed hash at H with raw hashing; check/existing provision. |
| PC-29 | PENDING — L0-only native extra link; check/existing provision/locked recheck. |

## Policy, cleanup and handoff

Full-range evidence policy passed for base `719fc095a4b8ad198824f20ccb744764bdd41666` through tested implementation `a934273624340717ee553ef2293cc994daf7aebd`: 4 commits, 0 evidence entries, 0 violations. The report-only delivery HEAD receives the same full-base check before final handoff.

The successful checkpoint tool removed all bin-c1058-links/, bin-c1058-identity/ and bin-c1058-compat/ outputs. The remaining owned tools/Antiphon.Checkpoints/bin-c1058-tool bootstrap output was removed after receipt validation. No checkpoint child remains in flight. JSON/TRX/log/build-provenance payloads stay ignored in their original evidence roots; only this Markdown report is committed.

Descriptor custody is point-in-time inode custody. Canonical directories/ancestors, inode contents and host toolchain remain trusted; in-place writes, ABA, an admissible file planted before qualification, and replacement after proof before later consumers remain outside the guarantee. The image probe has not gained host FD custody.

Restart: none. Owner of any later operational host qualification/activation: caller rollout orchestrator. Landing owner remains original Code task e6f3b241-608d-4299-a402-dc978134d36e; do not land a repair-source or report task instead.

Rerun from committed source by using the plan-authorized bootstrap, then checkpoint `run --plan docs/superpowers/plans/2026-10-05-card-1058-host-jq-file-identity-plan.md --after S1 --serial --expected-source-sha <HEAD> --max-wait 50s`, wait to terminal, then the equivalent `--after S2`. Review uses the same closed scope; PCs remain separately commissioned.

--- next stage ---
next: review
handoff: Review the CARD-1058 implementation and 18 ordinary tests against the frozen plan. CP-1/CP-2/CP-3 are 10/15/10 green with clean source receipts. Keep PC-1 through PC-29 pending; caller lands the original Code task after Review and commissions SourceLanding Mutation.
artifact: docs/superpowers/plans/2026-10-05-card-1058-host-jq-file-identity-plan.md
