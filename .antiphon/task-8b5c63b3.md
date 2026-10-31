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
