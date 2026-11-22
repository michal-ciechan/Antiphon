# Why group 138 found unmapped live gates

Windows Full run `c9816b02` at `ac8339c754a84ab53ca07708c091d4e79bd1fb5b`
reported `C1545 V-3/valid unmappedGateCount == 0` in
`LiveLaneInventoryTests.EveryLiveGateHasDisposition` after 29.7 seconds.
The run reported one failed assertion for the aggregate count. The native TRX
was unavailable from the Linux worktree.

The same method failed on Linux at master
`c1e03aac509d8e949f34750a4ead2147b13695d0` with the same assertion.
The master source scan found 149 gates and zero unresolved shapes against 146
catalog entries. Both revisions have the same scanner, catalog and three
gate definitions.

| Unmapped gate | Shape | Introducing commit |
|---|---|---|
| `CodexBootPointerCanaryTests.Real_Codex_consumes_and_submits_the_boot_pointer` | `RealCliStub` | `ac15e6c89abb32093c56d2e49ce63357deb8a9a2` |
| `RcManifestScaleTests.ScaleReplay` | `Explicit` | `f7e2e04a892e50a9729ce2e65bc42fb9c248818d` |
| `RcManifestScaleTests.ScaleSamples` | `Explicit` | `f7e2e04a892e50a9729ce2e65bc42fb9c248818d` |

The introducing changes added the tests without refreshing the live-gate
catalog. The Codex canary checkpoint plan selected the canary and related
guards. The nightly S08 witness list selected the journal, recovery,
integration and publisher checks. Neither selected the inventory method that
reconciles both source trees with the checked-in catalog.

Running `pwsh -NoProfile -File scripts/test-live-lane-manifest.ps1 -Case ScanGates`
adds the three `diagnostic` dispositions owned by CARD-1545. When a test adds
a gate shape recognized by the scanner, regenerate the catalog in the same
change and select
`/*/Antiphon.Tests.Scripts/LiveLaneInventoryTests/EveryLiveGateHasDisposition`
in its checkpoints.
