# CARD-1609 continuation 10: 8b5c63b3

S5 remains incomplete. H7 is still RED at 8f480df1a8240218dd8d71b485ee933896404f8f; helper abort, public manual, CP-4 and J8 have passing receipts at the sources below. Full Linux qualification was not reached. Production deletes remain disabled; next stage is Code/Debug.

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

## Final acceptance verdict: incomplete, H7 remains red

The H7 fixture repair at8f480df1a8240218dd8d71b485ee933896404f8f did not resolve the failure. Its exact official retest executed1/0/1/0, method37.315s, build200.58s, granted slot150s, clean source/build verified. The full diagnostic is now retained in the assertion: `RecycleGitAuditUnknown audit check=origin-present status=0 repo=repos/school-revision`. The real writer produced its bound v2 header/bundle, but the helper did not obtain a comparison from that graph. Positive Ready and the seven malformed-proof vectors were NOT reached; do not credit them. The earlier local-origin control had the same classifier failure. Cause/introduced-versus-inherited attribution remains unresolved, and the earlier fixture diagnosis was insufficient. No assertion or gate was relaxed and no further speculative production fix was made.

J8 passed at85776c1c8e201699aa1318cb2f00d69f85b5e83f in481.892s, preserving its complete current public Close/persistence/Resume matrix. The H7/J8 run was2/1/1/0; its overall verdict remains red. Compiler202.42s, granted slot105s, actual test wall519.261s. A subsequent H7-only run was1/0/1/0. These failures block step2 and therefore the requested final closed Linux requalification. Do not call this branch green, landable, S5-complete or ready for final Opus Review.

The first leased isolated importer diagnostic passed FETCHED_COUNT=1 using the extracted real fetch_origin/c1609_unbundle_host_proof functions and an independent tiny A->B graph, after300s slot wait. It is a diagnostic, not TUnit/CP credit. The reproduction lives at /tmp/c1609-10-unbundle.sh and scratch /tmp/c1609-10-unbundle; it did not use Docker/production volumes/state. The second diagnostic adds changed file bytes and the exact audit Git environment (GIT_NO_LAZY_FETCH, global/system config disabled, core.commitGraph/gc.writeCommitGraph false); its outcome is recorded below. Next Debug should isolate the actual chroot/helper graph import and retain its sanitized failure evidence, rather than infer publication from volume-local heads. Inspect the temp fetch fallback and exact graph requirement while repairing it. Replay the exact existing H7 at the assigned base before calling an observed failure inherited.

| Checkpoint / probe | Executed / passed / failed / skipped this task | Source and credit |
|---|---:|---|
| Helper pre-repair |1/0/1/0|348624209d8e0153ec02edbab9dae563aab56392, intended assertion red|
| Helper repair |1/1/0/0|ac7504a0a3f1de2d89004cdd110d9024b04acbc8, verified green|
| Public manual red |1/0/1/0|30046456a7f1f4c043bf44adc550a70ce7348050|
| Public manual/W2/W3 |3/3/0/0|8a3fb06116e50a92da34c6b106d0f237edeb8aca|
| Four wrapper/v2 red assertions |4/0/4/0|9e2db7735899b9a66e6c2389170e0516b2c11079|
| CP-3 |not run|25-method full row owed|
| CP-4 |12/12/0/0|85776c1c8e201699aa1318cb2f00d69f85b5e83f, complete row green|
| CP-5 |not run|30 results owed|
| CP-6 |not run|132 results owed|
| CP-7 |not run|52 results owed|
| CP-8 |not run|10 methods owed; helper method alone is not row credit|
| CP-9 |not run|22-method full row owed|
| CP-10 |not run|3 real-Docker methods and owned cleanup owed|
| CP-11 |not run|12 native-Windows methods owed|
| CP-12 |not run|3 native-Windows methods owed; fresh portable closure also owed|
| CP-13 |not run|16 native-Windows methods owed; fresh Linux docs also owed|
| CP-15 |not run|16 methods owed|
| CP-16 |not run|23 results owed|
| H7/J8 acceptance |2/1/1/0|85776c1c8e201699aa1318cb2f00d69f85b5e83f; J8 passed, H7 failed|
| H7 fixture retest |1/0/1/0|8f480df1a8240218dd8d71b485ee933896404f8f; unresolved red|

No timeout, skipped method or lowered floor was used to classify these official rows. Compiler errors and preflight refusals received zero test credit. Extra isolated builds and exact selections above were necessary red-first/acceptance diagnostics, not an attempt to substitute for the closed final roster. Full p95 remains unknown; no timing budget was raised.

## Exact continuation commands

Run from a new assigned Code checkout at its reviewed full source SHA. A new Code/Debug commission must first repair H7 and complete long-scan/manual/v2 acceptance; do not go directly to final Review. No native Windows work was authorized or run in this Linux task.

```powershell
pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1609-next-tool -- dotnet build tools/Antiphon.Checkpoints --property:OutputPath=bin-c1609-next-tool/ --property:UseAppHost=false
dotnet tools/Antiphon.Checkpoints/bin-c1609-next-tool/Antiphon.Checkpoints.dll import --plan docs/superpowers/plans/2026-10-29-card-1609-content-derived-volume-retirement-plan.md --out .antiphon/c1609-next.yaml
```

Set UseAppHost=false, fresh output bin-c1609-next-linux/, the corrected explicit row caps listed above, TEMP=/tmp and the opt-in phase environment on the generated manifest. Keep each exact plan filter/floor. Select serial groups with one isolated build and no simultaneous timing work, at most30m charged per group; CP-3 and CP-9 alone. If any cap fails, commission phase Debug and preserve its censored outcome instead of blindly increasing it.

```powershell
dotnet tools/Antiphon.Checkpoints/bin-c1609-next-tool/Antiphon.Checkpoints.dll run .antiphon/c1609-next.yaml --rows CP-3 --serial --keep-outputs --expected-source-sha <full-source-sha> --total-timeout 30m --hard-wall-timeout 60m --max-wait 30s
dotnet tools/Antiphon.Checkpoints/bin-c1609-next-tool/Antiphon.Checkpoints.dll wait --run <returned-run-id> --max-wait 30s
# Continue wait until non-75; run the remaining exact Linux rows in bounded serial groups.
```

Windows commands (owed; execute only on its separately assigned native host):

```powershell
pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1609-windows-tool -- dotnet build tools/Antiphon.Checkpoints --property:OutputPath=bin-c1609-windows-tool/ --property:UseAppHost=false
dotnet tools/Antiphon.Checkpoints/bin-c1609-windows-tool/Antiphon.Checkpoints.dll import --plan docs/superpowers/plans/2026-10-29-card-1609-content-derived-volume-retirement-plan.md --out .antiphon/c1609-windows.yaml
# Pin UseAppHost=false, CP-11=10m, CP-12/13=2m, a fresh isolated Windows output and phase variables.
dotnet tools/Antiphon.Checkpoints/bin-c1609-windows-tool/Antiphon.Checkpoints.dll run .antiphon/c1609-windows.yaml --rows CP-11,CP-12,CP-13 --serial --expected-source-sha <full-source-sha> --max-wait 30s
dotnet tools/Antiphon.Checkpoints/bin-c1609-windows-tool/Antiphon.Checkpoints.dll wait --run <returned-run-id> --max-wait 30s
```

After all Code/required verification and independent final Opus Review, port reviewed tools to rc after top-up38 and before CARD-1615, validate actual installed state, then PREVIEW ONLY:

```powershell
pwsh -NoProfile -File scripts/deploy-server2.ps1 -Rolling -Sha <reviewed-rc-tool-sha> -Phase retire-temp -DryRun -ProjectId <real-project-id>
```

Inspect complete findings/proof/refusal denominators. No production Apply or Close is authorized by this report. Production deletes remain disabled. Independent Review, Windows execution and production preview/audit are all owed.

```text
CHECKPOINT ACCEPT-H7-REPAIR commit=8f480df1a8240218dd8d71b485ee933896404f8f build=ok filter=/*/Antiphon.Tests.Scripts/VolumeRetirementContentTests/Private_origin_advanced_head_uses_real_host_proof executed=1 passed=0 failed=1 skipped=0 trx=/work/worktrees/task-8b5c63b3/.antiphon/checkpoints/ACCEPT-H7-REPAIR-20261031-034735-7f15/run.trx slot=granted waited=150s dirty=0 source=8f480df1a8240218dd8d71b485ee933896404f8f sourceState=clean buildSource=verified
CHECKPOINT ACCEPT-H7-REPAIR EXIT CODE: 1
```

Second isolated importer diagnostic: PASS, exit0; it still does not reproduce the actual helper failure and grants no CP credit.

```text
BUILD SLOT granted lease=e7966d71-eeda-4c1e-be52-a3547326402b waited=420s maxcpucount=6
FETCHED_COUNT=1
BUILD SLOT released lease=e7966d71-eeda-4c1e-be52-a3547326402b held=0s
```

Final read-only guards at8f480df1a8240218dd8d71b485ee933896404f8f: evidence history12 commits/3 entries/0 violations; corrected OS-witness declaration lint passed, retaining native execution as incomplete. A preliminary final lint omitted -Plan and was refused before certification, zero credit. After git fetch origin master, merge-tree --write-tree origin/master HEAD exited0 with tree a849d6cef70b9ff68b20bc31a191de502c73e167. This is a conflict simulation, not a land or an acceptance verdict. No definition file changed after the real approved land -Check at85776c1c8e201699aa1318cb2f00d69f85b5e83f.

Owned output cleanup completed by literal paths only: tests/Antiphon.Tests/bin-c1609-10-wrapper-red2, bin-c1609-10-h7j8, bin-c1609-10-h7-green, tools/Antiphon.Checkpoints/bin-c1609-10-tool, src/Antiphon.HostCleanup/bin-c1609-10-tool. Earlier abort/manual/failed-compile bins were also removed; scheduled CP-4 cleaned its own bin-c1609-10-accept. No task bin-c1609-10-* remains. Scratch evidence and detached pre-repair checkout are retained; no volume/state directory was deleted manually. All owned runs were joined.

## Pushed implementation/history commits

```text
8f480df1a8240218dd8d71b485ee933896404f8f test: bind unpublished H7 control to the actual v2 bridge graph and record J8 acceptance
85776c1c8e201699aa1318cb2f00d69f85b5e83f fix: bind v2 host graphs and prior proof consumption and recheck retirement under the clear lock
9e2db7735899b9a66e6c2389170e0516b2c11079 test: require locked reobservation before retirement clear and correct assertion overloads
fafe9c8cc8d5f77029f2ed6de04603abab65157a test: require explicit prior proof consumption and versioned host proof bindings
74a970a6b0ea513fb1e7e9b41ba6f0755c92a5e2 docs: record public manual acceptance and remaining long-scan gate
8a3fb06116e50a92da34c6b106d0f237edeb8aca fix: bound manual evidence transport and verify its returned terminal record
7e09f1d4b0a4d4ea6e7f60a86743f31131124221 test: inject manual transport boundary at every host fixture trap
fa3fb432454e6fc80e272b3dc831645851a9fab2 test: use explicit confined transport adapter for public manual acceptance
e2ae3500b906fbc11f0f877b2b1dec38000f6446 test: confine encoded manual bridge staging to owned transport root
d9dbe0d7cef9a3dd65f8df2c13bed810fa5df63e feat: route attested manual retirement through protected host authority and shared gate
30046456a7f1f4c043bf44adc550a70ce7348050 test: require public manual templates and attested retirement acceptance
4fbb517d104e6d640663f3b7f356aeeb48ca3ef2 docs: verify helper abort repair against pre-repair assertion red
```

The final evidence-only commit follows these code/history commits. No speculative source fix was added after the H7 red retest. The bounded task ends with incomplete acceptance, not a green S5 boundary; the requested whole-roster requalification must follow its repair.

--- next stage ---
next: code
handoff: Debug H7 actual-helper origin-present refusal; complete long-scan/manual/v2 gates and CP-8 compatibility, then exact Linux and Windows qualification before independent final Opus Review.
artifact: /work/worktrees/task-8b5c63b3/.antiphon/task-8b5c63b3.md
