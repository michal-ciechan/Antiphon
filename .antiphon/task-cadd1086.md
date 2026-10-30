# CARD-1609 continuation cadd1086

Partial S5 implementation: J10 was added red-first and CP-3 reached 25/25. N1-N3 now exist and their direct owned-Docker cases passed; managed native qualification exposed a missing-CID cancellation race and a row timeout. Repairs and final receipts are distinguished below. S5 is NOT complete and production deletion is NOT authorized.

Base: 30cf65bb58b78e89116400327c8eed195877ea2b
Code tip: 11d67e9ff2549b8e9612a77e6419e2118b7c63ee
Branch: feat/card-task-cadd1086. Every committed slice was pushed without rebase, amend, reset, merge, tags or force. No desktop/Windows native access, C:\Antiphon, live qualification d47848cc, activation, rollout or production audit/delete was used.

## Changes and protective intent

- J10: actual Ready writer/consumer and actual four-target removal path. A manual flag alone refuses; protected canonical per-operation authority and an exact attestation bind actor, authority reference, time window, reason, phase, targets, fingerprints and preview digest. Wrong/missing/expired/unsigned inputs cannot consume Ready. A valid commissioned case applies the same proof. The dirty control now first succeeds with its own valid manual grant/attestation, then adds dirty bytes and requires refusal with unchanged Ready. The public manual entry and grant producer remain incomplete; this is receipt-level acceptance.
- Native fixtures: private randomly named/labeled objects on the owned nested daemon only; no production names. Real host-origin Git A->B proof, real kernel UID/read-only/offline/nocopy/write denial and linked /tmp checkout; hook/dirty refusals; exact owned cancellation while a same-operation foreign-nonce helper survives. Real replacement, named/bind references, exact rollout-lock inode/blocked flock child and real writer continuation. Actual removal-loop bytes exercise exact four-volume deletion, crash-after-rm resume, replacement and no-intent absence refusals; main, all three caches and sentinel incarnations/bytes survive. Native construction does no IO before Linux admission. Roster totals are 89 identities / 100 results.
- Production temp helper now has read-only root, network none, read-only nocopy /work and actual /tmp, separate /audit-scratch, unchanged capability/UID/init/no-new-privileges restrictions. Main v1 namespace/transport remains selected by its existing branch. Every temp origin consumes host proof before any helper remote lookup.
- Native N1 caught a real creation-before-CID cleanup gap. Temp cleanup now recovers only the exact operation+nonce name AND label, validates the full ID and releases it; signal handling joins its client. Missing-CID fault is deterministic: CID is withheld and Docker signal proxy disabled only in the disposable child. The same-prefix foreign helper must remain running. Identity checks retain both daemon name and ID in one query to reduce repeated CLI overhead.
- H7's old assertion required helper ls-remote/auth classification, contradicting the now-offline gate. Replaced it with assertions of zero helper remote lookup, plus real host ls-remote AND fetch. Denied host proof, unpublished child refusal, advanced-head ancestry, volume absence of B and Ready admission assertions remain. No test was removed or skipped.
- File-backed adapter builds real fresh origin graphs only from local owned origins outside every target volume; it never manufactures a PASS from volume objects. It explicitly maps the validated native /tmp arguments into its private legacy namespace; N1 supplies actual kernel evidence. Legacy historical-image relocation methods were not deleted or claimed requalified.

## Red-first evidence and costs

J10 red source 57cefc5e6d88c2cdd79036eb65af0f2c5a48181e: 1 executed / 0 passed / 1 intended assertion failure / 0 skipped (manual without authority accepted at baseline); build 330.44s, admission 195s, slot held 425s. A prior short-SHA refusal executed zero tests and is not red credit. J10 initial green source 290499d6d28c9d8a2d7c54b5fb271dd92abb0949: 1/1/0/0, body 85.875s, build 342.01s, admission 15s, held 571s. Logs .antiphon/cadd-j10-red.log and .antiphon/cadd-j10-green.log; phase root .antiphon/cadd-j10-green-phases.

Direct Node witnesses are interpreted native fixture probes, NOT TUnit/CP-10 receipts. N1 red at bdab75b8a2a598eb77a3443e05a90f4e5d44c81f failed N1_OFFLINE; normal N1 later passed at c68c57db0db3de15965106fa6eeea735501ba3a4 (52s). N2 name-only disposable-copy PC at e1e5fc77b35c8927f777eeb18b5304bbb83f1012 failed the intended same-incarnation assertion (26s); normal N2 passed at 49efbcb8fd394c777c0d908213109424ca4c83ad (59s). N3 no-intent disposable-child PC failed the durable-intent-before-effect assertion (30s); normal N3 passed at that same 49ef source (181s including driver). Positive-control mutations touched only disposable children; normal modes restored without worktree mutations. Initial fixture failures (Git path, injection marker, UTS identity, shell dollar interpolation, lock observer and wrapper joining) are recorded in logs but do not count as assertion red. The lock observer uses actual blocked flock kernel wchan plus exact FD inode because this nested host omits its owners from /proc/locks.

N3's first complete 181s measurement justified changing its NEW provisional body budget from 180 to 360s (~2x), class budget 480s including first preparation. No timeout failure was hidden by this change; CP-10 remains at the brief's 8-minute cap. The subsequent managed row still timed out and is preserved below, never greenwashed.

## Current-task checkpoint receipts

All runs used TEMP=/tmp, UseAppHost=false, host slots and isolated outputs. The original plan was imported into an ignored manifest; Debug caps were pinned CP-3=22m,4=5m,5=2m,6=6m,7=4m,8=17m,9=30m,10=8m,15/16=6m. Each run was observed until terminal and its children joined before edits. Added exact selections were justified by native namespace/proof changes and later by the observed N1/H7 failures and strengthened J10, avoiding full-suite repetition. Final run has a bounded 17m execution/18m hard-wall budget to respect this task's remaining budget; this does not change row caps.

### 20261030-212414-fac8

Phase=done; exit=0; reason=null; source=290499d6d28c9d8a2d7c54b5fb271dd92abb0949; sourceState=clean; buildSource=verified.

Build bin-cadd-cp3: ok, seconds=311.9480306, admission=135s, execution=320.3913344s, wall=476.8525851s.

```text
CHECKPOINT CP-3 commit=290499d6d28c9d8a2d7c54b5fb271dd92abb0949 build=ok filter=/*/Antiphon.Tests.Scripts/(VolumeRetirementContentTests*)|(VolumeRetirementReceiptTests*)/* executed=25 passed=25 failed=0 skipped=0 trx=/work/worktrees/task-cadd1086/.antiphon/checkpoints/20261030-212414-fac8/rows/CP-3/run.trx slot=granted waited=0s dirty=0 source=290499d6d28c9d8a2d7c54b5fb271dd92abb0949 sourceState=clean buildSource=verified
```

Budget: wall=1636.2346367s; execution=1501.236188s; admission-only=134.9984435s.

### 20261030-223858-9779

Phase=done; exit=5; reason=row-deadline-exceeded; source=bd7db0223dbb477597232509c7ddbdf1375823a7; sourceState=clean; buildSource=verified.

Build bin-cadd-native-final: ok, seconds=335.326988, admission=0s, execution=346.6777403s, wall=372.852452s.

```text
CHECKPOINT CP-4 commit=bd7db0223dbb477597232509c7ddbdf1375823a7 build=ok filter=/*/Antiphon.Tests.Scripts/(VolumeRetirementHostProofTests*)|(VolumeRetirementWrapperTests*)/* executed=12 passed=12 failed=0 skipped=0 trx=/work/worktrees/task-cadd1086/.antiphon/checkpoints/20261030-223858-9779/rows/CP-4/run.trx slot=granted waited=0s dirty=0 source=bd7db0223dbb477597232509c7ddbdf1375823a7 sourceState=clean buildSource=verified
CHECKPOINT CP-10 commit=bd7db0223dbb477597232509c7ddbdf1375823a7 build=reused filter=/*/Antiphon.Tests.Scripts/VolumeRetirementDockerTests/* executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a timeout=8m slot=granted waited=0s dirty=0 source=bd7db0223dbb477597232509c7ddbdf1375823a7 sourceState=clean buildSource=verified
CHECKPOINT NATIVE-AFFECTED commit=bd7db0223dbb477597232509c7ddbdf1375823a7 build=reused filter=/*/Antiphon.Tests.Scripts/(VolumeRetirementContentTests*)|(VolumeRetirementReceiptTests*)/(Audit_identity_and_namespace_are_stable*)|(Private_origin_advanced_head_uses_real_host_proof*)|(Manual_attestation_is_exact_and_cannot_waive_findings*) executed=3 passed=2 failed=1 skipped=0 trx=/work/worktrees/task-cadd1086/.antiphon/checkpoints/20261030-223858-9779/rows/NATIVE-AFFECTED/run.trx slot=granted waited=0s dirty=0 source=bd7db0223dbb477597232509c7ddbdf1375823a7 sourceState=clean buildSource=verified
CHECKPOINT C1210-LINUX commit=bd7db0223dbb477597232509c7ddbdf1375823a7 build=reused filter=/*/Antiphon.Tests.Scripts/WindowsUnitPlatformAdmissionTests/(C1210_Guard_matches_native_platform*)|(C1210_Native_linux_entries_guard_before_work*)|(C1210_Roster_closure_matches_source_gates*) executed=3 passed=3 failed=0 skipped=0 trx=/work/worktrees/task-cadd1086/.antiphon/checkpoints/20261030-223858-9779/rows/C1210-LINUX/run.trx slot=granted waited=0s dirty=0 source=bd7db0223dbb477597232509c7ddbdf1375823a7 sourceState=clean buildSource=verified
CHECKPOINT DOCS-LINUX commit=bd7db0223dbb477597232509c7ddbdf1375823a7 build=reused filter=/*/Antiphon.Tests.Infrastructure/DockerStackDocumentationTests/* executed=16 passed=16 failed=0 skipped=0 trx=/work/worktrees/task-cadd1086/.antiphon/checkpoints/20261030-223858-9779/rows/DOCS-LINUX/run.trx slot=granted waited=0s dirty=0 source=bd7db0223dbb477597232509c7ddbdf1375823a7 sourceState=clean buildSource=verified
```

Budget: wall=1275.7355582s; execution=1275.7296425s; admission-only=0.0059074s.

### 20261030-230205-a8cb

Phase=stopped; exit=null; reason=null; source=ffa21dde856332fc97c63597b5234e720a633d2d; sourceState=clean; buildSource=unknown.

Build bin-cadd-finish: building, seconds=0, admission=0s, execution=nulls, wall=nulls.

```text
CP-10: queued, no completed counts
OFFLINE-H7: queued, no completed counts
MANUAL-J10: queued, no completed counts
HELPER-OWNERSHIP: queued, no completed counts
```

Budget: wall=1.2597974s; execution=1.2597952s; admission-only=0s.

### 20261030-230337-1481

Phase=done; exit=5; reason=hard-wall-deadline-exceeded; source=11d67e9ff2549b8e9612a77e6419e2118b7c63ee; sourceState=clean; buildSource=unknown.

Build bin-cadd-finish2: ok, seconds=483.6167592, admission=30s, execution=493.9593878s, wall=601.3285401s.

```text
CP-10: hard-wall-deadline-exceeded, no completed counts
OFFLINE-H7: skipped, no completed counts
MANUAL-J10: skipped, no completed counts
HELPER-OWNERSHIP: skipped, no completed counts
```

Budget: wall=1081.7796468s; execution=1021.7474864s; admission-only=60.032156s.

The 25/25 CP-3 receipt is at 290499d6, before native production/helper changes; it is NOT final-current-source full requalification. CP-4 passed 12/12 at bd7db022. In run 223858, NATIVE-AFFECTED had C8/J10 pass and H7 fail (old helper-lookup assertion); CP-10 timed out with N1's cleanup assertion logged and no full-row counts. C1210 3/3 and docs 16/16 passed, but their added manifest rows had a mistaken expected identity and therefore roster-miss: preserve those incomplete gates, do not call them green rows. Stopped run 230205 was stopped/joined during admission for the deterministic no-signal-proxy cut, with zero tests and no red credit. See final run above for actual repaired results; no result is inferred from direct probes.

Inherited outcomes remain historical: predecessor CP-4/5/6/7/8/15/16 = 275 pass, CP-9 = 21/22 followed by the repaired method 1/1; prior C1210/docs = 3/3 and 16/16. No inherited row is relabeled fresh. CP-1/2/14 historical red-first provenance stays in predecessor reports; their current methods are included in CP-3 rather than counted as new baseline reds.

## Known incomplete scope / next Code

1. Full D-20 Close and J8 acceptance remains owed. Existing retire-runner-volumes.ps1 has no same-lock close transition, complete daemon/container/reference/child census or strict authority verification; failed docker volume inspect is currently treated as absence. Implement strict custody/identity/digest, inactive/joined children, exact absence vs unknown, reconciliation with attributable/unattributed intents, append-only durable closure and failure persistence. Test every refusal and actual wrapper/host trace; present/replaced volumes and unknown children/census must refuse with zero clear/delete. Existing W2/W3 passing tests are insufficient for that contract.
2. Public manual Preview remains census-only; no trusted authority-grant producer wires the new receipt check. Implement authenticated entry, exact attestation/template rejection and actual manual Preview/Apply/Resume acceptance; no manual finding waiver.
3. Strict wrapper/current v2 transport, proof-schema/coverage/freshness and trusted origin registry acceptance remains owed. Host bridge/consumer still use the old textual c1008-host-proof 1 envelope; offline consumption alone does not close the v2 acceptance gap. Preserve main v1.
4. Existing D5 role scan is shallow: recognized top-level dind dirs/sentinel do not establish full recursive persistent-store closure, and its process-substitution find error is not captured. Do not claim full D5 accepted from the native happy fixture.
5. Finish any failed/pending final rows above, then requalify the COMPLETE final Linux roster CP-3/4/5/6/7/8/9/10/15/16 after completing S5. CP-3 floor stays25, CP-9 full22, CP-10 full3. No skipped/removed/relaxed test or lowered floor. Legacy relocation methods C1008_Dry_run_preview_binds_a_dry_run_cleanup_receipt, C1594_Cross_sha_cleanup_image_binds_the_retired_temp, C1594_Older_retirement_receipt_is_ignored, C1594_Disagreeing_cross_sha_images_refuse remain unqualified; retain protective intent, do not silently drop them.
6. Windows CP-11/12/13 and first production PREVIEW/AUDIT remain owed. Production deletes stay disabled; no Apply/Close/rollout was performed. Next stage is Code, not final Opus Review.

### Exact continuation commands

Linux (fresh ignored manifest/output names; pin the Debug caps above and set all five ANTIPHON_TEST_PHASE_* variables to fresh valid values):

```sh
TEMP=/tmp pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1609-next-tool -- dotnet build tools/Antiphon.Checkpoints --property:OutputPath=bin-c1609-next-tool/ --property:UseAppHost=false
TEMP=/tmp dotnet tools/Antiphon.Checkpoints/bin-c1609-next-tool/Antiphon.Checkpoints.dll import --plan docs/superpowers/plans/2026-10-29-card-1609-content-derived-volume-retirement-plan.md --out .antiphon/c1609-next.yaml
# Split groups below 30 execution minutes, accounting for producing builds separately.
TEMP=/tmp dotnet tools/Antiphon.Checkpoints/bin-c1609-next-tool/Antiphon.Checkpoints.dll run .antiphon/c1609-next.yaml --rows CP-3 --total-timeout 29m --hard-wall-timeout 74m --max-wait 30s
TEMP=/tmp dotnet tools/Antiphon.Checkpoints/bin-c1609-next-tool/Antiphon.Checkpoints.dll wait --run <id> --max-wait 30s
# Separately select CP-4,5,6,7,8,9,10,15,16 with fresh manifests/outputs and bounded groups.
```

Windows, separately commissioned native checkout (not this mirror; no forced OS selector):

```powershell
pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1609-windows-tool -- dotnet build tools/Antiphon.Checkpoints --property:OutputPath=bin-c1609-windows-tool/ --property:UseAppHost=false
dotnet tools/Antiphon.Checkpoints/bin-c1609-windows-tool/Antiphon.Checkpoints.dll import --plan docs/superpowers/plans/2026-10-29-card-1609-content-derived-volume-retirement-plan.md --out .antiphon/c1609-windows.yaml
# Pin CP-11=10m, CP-12=2m, CP-13=2m; fresh phase evidence.
dotnet tools/Antiphon.Checkpoints/bin-c1609-windows-tool/Antiphon.Checkpoints.dll run .antiphon/c1609-windows.yaml --rows CP-11,CP-12,CP-13 --max-wait 30s
dotnet tools/Antiphon.Checkpoints/bin-c1609-windows-tool/Antiphon.Checkpoints.dll wait --run <windows-run-id> --max-wait 30s
```

Only after complete reviewed tooling is ported/activated and actual installed hashes, image digest/UID, descriptor policy/protocol and reviewed rc endpoints are verified:

```powershell
pwsh -NoProfile -File scripts/deploy-server2.ps1 -Rolling -Sha <reviewed-rc-tool-sha> -Phase retire-temp -DryRun -ProjectId <real-project-id>
# Verify this reaches full content Preview, not the existing census-only return.
# Future audited Close only after implementation/acceptance:
pwsh -NoProfile -File scripts/retire-runner-volumes.ps1 -Close -OperationId <id> -ExpectedJournalDigest <sha256> -ReasonFile <file>
```

## Static checks, publication and cleanup

No definition file changed. Real audit-land-verification-graph.ps1 -Check ran (never -Update), with digest 20DE49F6B177E5A238F20D4F8C7F33EAEE6B511A9163CDB902D9136EF06DECB6 and 66 definitions. See .antiphon/cadd-land-graph.log and .antiphon/cadd-final-land-graph.log for final status. Evidence-diff over base..code tip had zero violations; OS inventory lint passed with no new production OS selector or Windows startup change. These are lint/provenance checks, not native Windows coverage. Bash/embedded-Node/Node syntax and diff whitespace checks passed.

Direct native scope /tmp/c1609-real-ERdeBT cleaned its exact labeled containers/volumes/image. The first managed timed-out scope /tmp/c1609-real-jqGF7y was explicitly cleaned through the slot (45s admission, 9s held): cleanup=absent. No recursive state/volume-directory deletion or shared prune. Final native and output cleanup, merge-tree and remote tip are recorded in the settlement appendix below. Evidence stays ignored and state journals remain intact.

## Commits

```text
Settlement: {
  "nativeScope": "/tmp/c1609-real-lSUjFf",
  "nativeSessionCleanup": "absent",
  "nativeCases": [
    {
      "id": "N2",
      "source": "11d67e9ff2549b8e9612a77e6419e2118b7c63ee",
      "failure": null,
      "cleanup": "absent"
    },
    {
      "id": "N3",
      "source": "11d67e9ff2549b8e9612a77e6419e2118b7c63ee",
      "failure": null,
      "cleanup": "absent"
    }
  ],
  "N1": "interrupted; no current qualified result",
  "finalQualification": "CP-10 hard-wall timeout, no final counts; H7/J10/helper checks not run",
  "finalCleanupMode": "pure fixture cleanup only; no build or test driver",
  "removedLiteralBuildOutputs": [
    "/work/worktrees/task-cadd1086/tests/Antiphon.Tests/bin-cadd-j10-red",
    "/work/worktrees/task-cadd1086/tests/Antiphon.Tests/bin-cadd-j10-green",
    "/work/worktrees/task-cadd1086/tests/Antiphon.Tests/bin-cadd-native-final",
    "/work/worktrees/task-cadd1086/tests/Antiphon.Tests/bin-cadd-finish2"
  ],
  "graphCheck": "approved; 66 definitions unchanged; 106s admission,49s held",
  "master": "cbe3e6b42401db766f4cee38bc9b34f5b9d314af",
  "mergeTree": "02cf5430ba5778fc36284aadd71f357658c12fd7",
  "activation": "none; production deletes disabled"
}

57cefc5e6d88c2cdd79036eb65af0f2c5a48181e test: require exact manual authority through retirement receipts
290499d6d28c9d8a2d7c54b5fb271dd92abb0949 fix: bind manual retirement to protected authority and exact attestation
e2845a8a83dffedb804ae2f48886513829ba0059 test: add owned native retirement namespace and volume witnesses
68952c1b6dd0c32b261f1198e40ea6288fbb3bc5 test: resolve native host Git and preserve per-phase provenance
af290b6cef422324abd5839e673da547984bdd0e test: anchor native harness at the final shell entrypoint
bdab75b8a2a598eb77a3443e05a90f4e5d44c81f test: retain the owned daemon UTS identity in native host children
3e6ccdbf2de88beab7fc4fa3ee5bf111304cfa73 fix: isolate temp retirement helpers in the production read-only namespace
26023d679fc09a59ac79b42ec7b5d2fe268004e8 fix: consume host publication proof before any offline temp lookup
dffb9aa7327cb2fb41ba27c4c1c5b112115e3dcc test: supply real local-origin graphs and strengthen native namespace controls
c68c57db0db3de15965106fa6eeea735501ba3a4 test: join native audit clients and isolate cancellation leftovers
e1e5fc77b35c8927f777eeb18b5304bbb83f1012 test: bind native incarnation and durable-intent controls to disposable children
0a8b64a135a10cfa5ca64d23a1ca61c03d18372b test: bind native lock witness to blocked child and exact file descriptor
424d418b0a98edf9d7251cda3ba0742d082587b9 test: preserve shell PID expansions in native entrypoint injection
49efbcb8fd394c777c0d908213109424ca4c83ad test: record actual writer process for native kernel lock witness
bd7db0223dbb477597232509c7ddbdf1375823a7 test: budget measured native retirement and exclude target origins from fixture proof
cea2c0b5702888e3e1f571d600f8888758cbea68 fix: release exact owned temp audit helper when CID publication is interrupted
ffa21dde856332fc97c63597b5234e720a633d2d test: require offline host ancestry and valid manual authority before dirty refusal
11d67e9ff2549b8e9612a77e6419e2118b7c63ee test: force the missing-CID release path without Docker signal proxy
```

--- next stage ---
next: code
handoff: complete Close/manual/v2 acceptance, finish owed qualification and retain production preview-only
artifact: /work/worktrees/task-cadd1086/.antiphon/task-cadd1086.md
