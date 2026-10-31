# CARD-1609 continuation 10: 8b5c63b3

S5 remains incomplete. Production deletes remain disabled; next stage is Code.

## Helper abort repair verified

Assigned base ac7504a0a3f1de2d89004cdd110d9024b04acbc8; branch feat/card-task-8b5c63b3.
The unchanged ownership method reproduced the intended foreign-collision assertion red before the repair (348624209d8e0153ec02edbab9dae563aab56392): 1 executed / 0 passed / 1 failed / 0 skipped, method 34.030s. It returned RecycleReceiptUnavailable instead of RecycleGitAuditUnknown after abort. Fresh isolated build 188.02s; slot wait 0s.
The repair at the assigned tip passed the identical method: 1/1/0/0, method 36.116s; fresh isolated build 177.51s; slot wait 0s. Both clean source/build verified, granted slots, UseAppHost=false, TEMP=/tmp. No test/assertion/budget was removed or relaxed. Green has opt-in phase journals; baseline phases beyond build/test wall are unknown.

```text
CHECKPOINT HELPER-ABORT-BASE commit=348624209d8e0153ec02edbab9dae563aab56392 build=ok filter=/*/*/RollingVolumeRecycleScriptTests/C1008_Preview_helper_removal_is_owned executed=1 passed=0 failed=1 skipped=0 trx=/tmp/c1609-10-abort-base/.antiphon/checkpoints/HELPER-ABORT-BASE-20261031-015624-a6e8/run.trx slot=granted waited=0s dirty=0 source=348624209d8e0153ec02edbab9dae563aab56392 sourceState=clean buildSource=verified
CHECKPOINT HELPER-ABORT-BASE EXIT CODE: 1
CHECKPOINT HELPER-ABORT-GREEN commit=ac7504a0a3f1de2d89004cdd110d9024b04acbc8 build=ok filter=/*/*/RollingVolumeRecycleScriptTests/C1008_Preview_helper_removal_is_owned executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-8b5c63b3/.antiphon/checkpoints/HELPER-ABORT-GREEN-20261031-020153-1761/run.trx slot=granted waited=0s dirty=0 source=ac7504a0a3f1de2d89004cdd110d9024b04acbc8 sourceState=clean buildSource=verified
CHECKPOINT HELPER-ABORT-GREEN EXIT CODE: 0
```

Evidence roots are literal paths from the CHECKPOINT lines. The detached pre-repair scratch is /tmp/c1609-10-abort-base. No volume/state directory or production resource was deleted.

## Acceptance and requalification

Manual public entry, strict v2 wrapper acceptance, J8 and final Linux roster remain owed at this initial verification boundary. Windows CP-11/12/13 and first production preview/audit remain owed. No definition file changed in this slice.

## Public manual acceptance

New J10 public-entry witness committed red first at 30046456a7f1f4c043bf44adc550a70ce7348050: 1/0/1/0, intended assertion that public Preview must generate an unsigned exact template; actual ManualPreviewApplyIncomplete. Method167.606s, build202.68s, slot0s. A mistyped expected SHA was rejected before tests (zero credit).

Public manual entry now commissions canonical, short-lived host authority after Get-RunnerOperatorToken, archives immutable grants/attestations, observes exact fingerprints under FD8/FD9, and uses the existing bridge/content/live-state gate. A template carries no signature or Ready authority. Preview and DryRun perform full content Preview; Apply pins the protected receipt digest; Resume needs fresh manual authority and original preview binding. Scripted Resume cannot consume a manual journal. Bounded evidence copying verifies the returned terminal record. No alternate delete loop was added.

Official selection at 8a3fb06116e50a92da34c6b106d0f237edeb8aca passed3/3/0/0 (J10,W2,W3), build200.21s, slot0s, clean source/build verified. J10 retains all earlier dirty/authority/refusal assertions and adds public-command success, unsigned/task-only/wrong-digest refusal, exact four-target effects, main/cache preservation and duplicate-free manual Resume. Interpreted local transport probe passed122s; it is not TUnit/CP credit. Earlier probe1 failed its host injection (WrongLane); fixed the adapter without weakening the gate. One immediate ungated Bash diagnostic exposed that fixture issue and earns no verification credit.

Known limit: this small-fixture manual acceptance does not establish the full long-scan v2 exchange. The short-lived authority currently can expire during inventory or audit; phase admission/renewal and strict v2 host proof remain owed. Production deletes remain disabled.

```text
CHECKPOINT PUBLIC-MANUAL-RED commit=30046456a7f1f4c043bf44adc550a70ce7348050 build=ok filter=/*/*/VolumeRetirementReceiptTests/Manual_attestation_is_exact_and_cannot_waive_findings executed=1 passed=0 failed=1 skipped=0 trx=/work/worktrees/task-8b5c63b3/.antiphon/checkpoints/PUBLIC-MANUAL-RED-20261031-020828-ecc6/run.trx slot=granted waited=0s dirty=0 source=30046456a7f1f4c043bf44adc550a70ce7348050 sourceState=clean buildSource=verified
CHECKPOINT PUBLIC-MANUAL-RED EXIT CODE: 1
CHECKPOINT PUBLIC-MANUAL-GREEN commit=8a3fb06116e50a92da34c6b106d0f237edeb8aca build=ok filter=/*/*/(VolumeRetirementReceiptTests*)|(VolumeRetirementWrapperTests*)/(Manual_attestation_is_exact_and_cannot_waive_findings*)|(Manual_parameters_require_operator_attestation*)|(Bridge_validates_and_preserves_receipt_fields*) executed=3 passed=3 failed=0 skipped=0 trx=/work/worktrees/task-8b5c63b3/.antiphon/checkpoints/PUBLIC-MANUAL-GREEN-20261031-022538-a8e7/run.trx slot=granted waited=0s dirty=0 source=8a3fb06116e50a92da34c6b106d0f237edeb8aca sourceState=clean buildSource=verified
CHECKPOINT PUBLIC-MANUAL-GREEN EXIT CODE: 0
```

## Wrapper and version-2 candidate

Four new assertions were committed before their repair. At 9e2db7735899b9a66e6c2389170e0516b2c11079 the exact four-method selection executed 4/0/4/0: H4 received a v1 proof, H5 admitted an approved local origin, W1 regenerated Preview instead of consuming the requested prior proof, and W6 posted after a late census change. Build198.74s, slot15s, clean source/build verified. An earlier compiler error from an incorrect assertion overload executed zero tests and grants no red credit.

The pushed repair is 85776c1c8e201699aa1318cb2f00d69f85b5e83f. It adds a version-2 host-only graph producer, exact protected project-origin registry, fresh phase nonce and complete request digest, strict advertisement/graph hashes and offline coverage validation. Same-origin graphs are fetched once and rebound per repository. The main v1 adapter remains available. The wrapper consumes an explicit protected prior Ready digest and repeats the strong retirement-clearable predicate while it owns the existing FD8 POST handshake. The observation packet takes no nested lock. No alternate delete loop was added.

The new host prerequisite is protected SERVER2_ROOT/retirement-origins.json, schema1 with projects keyed by project GUID and an exact origins array per project. No production registry was installed. Unknown registry/project/origin refuses.

CP-4 at that source passed12/12/0/0, actual test wall157.081s, row execution161.348s. Compiler322.91s; driver build374.491s; build slot45s and row slot240s. Full run wall901.025s, charged616.320s, admission284.705s; clean source/build verified. The five-minute row cap was retained. This is one complete sample, not population p95. Run20261031-031300-4a70. Its default output cleanup removed bin-c1609-10-accept after finalization, so the following acceptance probe requires another isolated build; this extra build is a recorded efficiency deviation.

## Limits and continuation order

S5 is incomplete even if the selected methods are green. Long-scan phase admission/renewal and a single coordinator exchange remain unfinished: the current authority/proof freshness checks fail closed on expiry instead of renewing after a long audit. Full H6 pipe-holder/process-tree ownership is also unfinished; the new bounded reader kills a live root process tree but cannot yet prove cleanup of a pipe-holding descendant after its parent has exited. The current H6 method still exercises only sentinel redaction. Complete the planned recursive state/dind persistence and observation-error closure, locks/reservation/dependency/historical-container matrices and strict envelope completeness before claiming all planned acceptance.

The legacy CP-8 method C1008_Host_proof_admits_only_an_authenticated_private_origin still contains a fixed-time v1/ext-auth fallback fixture. Inspection suggests it conflicts with the new temp-only v2/offline gate. This is an inspected unresolved compatibility risk, not an attributed regression or verified baseline failure. Replay its exact method at ac7504a0a3f1de2d89004cdd110d9024b04acbc8 before attribution, then adapt the temp fixture to current authenticated v2 ancestry while preserving its main-v1/redaction/negative protective intent. Never relax the product gate to satisfy it.

After the remaining Code repairs, requalify the unchanged closed Linux roster CP-3/4/5/6/7/8/9/10/15/16. Required floors are25,12,30,132,52,10,22,3,16,23. Native Windows CP-11/12/13 remain owed with12,3,16; Linux portable roster/docs checks do not grant Windows credit. No test was removed/skipped or floor lowered. CP-1/2/14 are historical red witnesses, not final green rows.

Use the checkpoint import from the plan, UseAppHost=false, TEMP=/tmp, isolated fresh outputs, expected full source SHA, serial rows and normal host slots. Preserve corrected collection caps CP-3=28m, CP-4=5m, CP-5=2m, CP-6=6m, CP-7=4m, CP-8=17m, CP-9=30m, CP-10=8m, CP-15/16=6m. These retained collection budgets are provisional; a timeout requires measured phase analysis and cannot justify blind extension. Keep timing-sensitive rows alone, with no simultaneous build. Full current-row p95 remains unknown. CP-9 should be its own charged group; other groups must account for producing build and stay below30 minutes, or commission Debug first. Do not repeat green rows just to fill timing samples.

## Static evidence and authority

No definition file changed; no -Update or approval regeneration ran. Real gated land-graph -Check passed at85776c1c8e201699aa1318cb2f00d69f85b5e83f with approved digest20DE49F6B177E5A238F20D4F8C7F33EAEE6B511A9163CDB902D9136EF06DECB6,66 definitions. Its slot waited60s/held31s. Evidence guard then found11 commits,2 report entries,0 violations. OS-witness inventory/lint passed for every changed path, with unchanged test platform guards and no production OS selector/startup change; native execution remains incomplete. One preliminary OS lint used HEAD instead of a required full SHA and was refused, zero credit. Shell/PowerShell/Node parsing and git diff --check passed before the feature commit.

No desktop/native-Windows files or live qualification d47848cc were accessed. No AppHost/runner restart, live rollout, production delete, tag, publish, rc change, definition update, force push, reset, rebase or target merge occurred. Standing server2 authority sufficed; no additional approval was requested. Production deletes remain disabled. First production action remains preview-only after independent Review, reviewed rc port, after top-up38 and before CARD-1615; validate installed tool hashes, image UID/protocol/policy, origin registry and actual endpoints first.

## H7/J8 candidate boundary

At85776c1c8e201699aa1318cb2f00d69f85b5e83f, the two-method acceptance probe executed2/1/1/0. J8 passed its unchanged persistence/Resume and public Close matrix. H7 failed its existing unpublished-child classifier assertion at line187, before positive v2 coverage. The child used a local origin with implicit fixture proof rather than the actual authenticated bridge proof. The next fixture repair retains the exact RecycleUnpublishedWork/no-Ready assertion but explicitly supplies the actual v2 host graph for that child, and corrects the SCP remote-root lookup. No product gate is relaxed. Attribution of the implicit-adapter refusal is unresolved; no baseline replay was performed for that separate fixture behavior. Full Linux qualification is not claimed.

```text
CHECKPOINT ACCEPT-H7-J8 commit=85776c1c8e201699aa1318cb2f00d69f85b5e83f build=ok filter=/*/Antiphon.Tests.Scripts/(VolumeRetirementContentTests*)|(VolumeRetirementReceiptTests*)/(Private_origin_advanced_head_uses_real_host_proof*)|(Journal_intents_and_partial_resume_are_exact*) executed=2 passed=1 failed=1 skipped=0 trx=/work/worktrees/task-8b5c63b3/.antiphon/checkpoints/ACCEPT-H7-J8-20261031-033026-af27/run.trx slot=granted waited=105s dirty=0 source=85776c1c8e201699aa1318cb2f00d69f85b5e83f sourceState=clean buildSource=verified
CHECKPOINT ACCEPT-H7-J8 EXIT CODE: 1
```

```text
CHECKPOINT WRAPPER-V2-RED commit=9e2db7735899b9a66e6c2389170e0516b2c11079 build=ok filter=/*/*/(VolumeRetirementHostProofTests*)|(VolumeRetirementWrapperTests*)/(Proof_coverage_binding_and_freshness_are_exact*)|(Origins_and_git_configuration_cannot_redirect_credentials*)|(Retire_temp_uses_current_proof_protocol*)|(Retained_volumes_block_deploy_and_completion*) executed=4 passed=0 failed=4 skipped=0 trx=/work/worktrees/task-8b5c63b3/.antiphon/checkpoints/WRAPPER-V2-RED-20261031-024401-4fc5/run.trx slot=granted waited=15s dirty=0 source=9e2db7735899b9a66e6c2389170e0516b2c11079 sourceState=clean buildSource=verified
CHECKPOINT WRAPPER-V2-RED EXIT CODE: 1
CHECKPOINT CP-4 commit=85776c1c8e201699aa1318cb2f00d69f85b5e83f build=ok filter=/*/Antiphon.Tests.Scripts/(VolumeRetirementHostProofTests*)|(VolumeRetirementWrapperTests*)/* executed=12 passed=12 failed=0 skipped=0 trx=/work/worktrees/task-8b5c63b3/.antiphon/checkpoints/20261031-031300-4a70/rows/CP-4/run.trx slot=granted waited=240s dirty=0 source=85776c1c8e201699aa1318cb2f00d69f85b5e83f sourceState=clean buildSource=verified
```
