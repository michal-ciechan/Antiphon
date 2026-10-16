# Release candidate native E2E verification - 2026-10-16

**IN PROGRESS / NOT QUALIFIED.** This report records the exact three-class continuation of the prior Windows release-candidate pass. Final outcomes and failure attribution will replace this preliminary record after execution.

- Candidate source and built assembly identity: `067b47e2dc903247f1a7c8aa539032cc4266e4ad`.
- Morning baseline for each observed failure: `cc8798c1fa4f3c2faa834283aef080d8e5fd28f0`.
- Selection: `Antiphon.E2E.AgentTaskLandDeliveryE2ETests`, `Antiphon.E2E.DispatchBaseWarningDeliveryE2ETests`, `Antiphon.E2E.WorktreeRetirementDeliveryE2ETests` only.
- Evidence root: `C:\src\Antiphon\.antiphon\investigations\rc-native-30129456`.
- No product/test code changes. The client bundle was rebuilt; the E2E build passed. All drivers are gated through `scripts/build-slot.ps1`. Isolated runner children inherit `PhoneHome__Enabled=false` and a dead-loopback server origin; headed/live opt-ins are cleared. No production AppHost/runner restart is authorized or performed.

--- next stage ---
next: decide
handoff: Execution is in progress; retain the release gate as incomplete until the final report.
artifact: docs/investigations/2026-10-16-release-candidate-native-e2e.md
