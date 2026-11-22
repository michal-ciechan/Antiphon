# Windows full qualification run 79d9e6f9

## Group 138 live-gate inventory

W2-nightly S15 (`75d96a8c7d9a0252ffcaf9f9f05fb98536ed6a0b`) added `NightlyTestsRunTests.C487_G044`, which seeds the headed opt-in flag to verify that the child launcher clears it.
The PowerShell scanner and its S23 port both cataloged mentions of the flag as headed gates. The same rule had cataloged `ReleaseGateE2EEvidenceTests.C599_EnvironmentIsolation`.
Short Merge replays each subject's named witnesses. The S15 plan selected the nightly runner and related harness classes, leaving the inventory class outside its witness set.
Include `/*/Antiphon.Tests.Scripts/LiveLaneInventoryTests/*` when changes to either scanned test project introduce a gate marker or when `LiveLaneGateScan.cs` changes.
