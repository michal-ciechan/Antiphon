# CARD-1960 CP-5 re-run extract (Debug task 233b0e13)

Receipt for the test-support fix of the inherited phone-home replacement race that made CP-5 of
[the CARD-1960 extract](2026-11-17-card-1960-checkpoints.md) red in serial run `20261117-102716-6a1f` at
`1e82c08aeb3877095d6430c42f7acec66bcf0610`
([why missed](../../investigations/2026-11-17-why-missed-phone-home-replacement-race.md)).

The change touches only `tests/Antiphon.Tests/TestHelpers/PhoneHomeTestHost.cs` (`ConnectPeerAsync` waits for
its registration's epoch; an opt-in `AfterHandshake` hold) and adds one witness method to
`PhoneHomeRecoveryEligibilityTests`. CP-1 is the CP-5 filter unchanged; its Min is the 119 the red run
executed plus the witness. Of the other fourteen rows only CP-1, CP-3 and CP-4 name classes that call
`ConnectPeerAsync`; they are CP-2..CP-4 here with their original filters and floors. The rest do not use
`PhoneHomeTestHost` and keep their green result from `20261117-102716-6a1f`. All rows share one build.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | D1 | tests/Antiphon.Tests -> bin-c1960-dbg/ | portable-client-impact-b | `/*/*/(PhoneHomeRecoveryEligibilityTests*)\|(PhoneHomeSessionRoutingTests*)\|(PhoneHomeSpillTransportTests*)\|(PhoneHomeStandingLaunchTests*)\|(PhoneHomeTaskDispatchProjectionTests*)\|(PtyConditionalReadClientTests*)\|(RunnerCapacityEndpointTests*)\|(RunnerCodexCliEvidenceTests*)\|(SessionMessageQueuePhoneHomeDropTests*)\|(TaskPlatformDispatchTests*)\|(TranscriptSinceClientTests*)/*` | CARD-1960 CP-5 re-run, replacement witness | every named class, 0 failed | 120 | 15 | true |
| CP-2 | D1 | tests/Antiphon.Tests -> bin-c1960-dbg/ | portable-route | `/*/*/C1960QuarantineReleaseRouteTests/*` | CARD-1960 CP-1 (ConnectPeerAsync caller) | every method and case, 0 failed/skipped | 9 | 12 | true |
| CP-3 | D1 | tests/Antiphon.Tests -> bin-c1960-dbg/ | portable-slot-server-regression | `/*/*/(C0824WorkspaceSlotServerTests*)\|(C1656RecordReleaseHardeningTests*)/*` | CARD-1960 CP-3 (ConnectPeerAsync caller) | both classes, 0 failed | 35 | 14 | true |
| CP-4 | D1 | tests/Antiphon.Tests -> bin-c1960-dbg/ | portable-client-impact-a | `/*/*/(AgentTaskInputFallbackTests*)\|(C1818RepositoryMaintenanceStatusTests*)\|(ClaudeDialogDeliveryTests*)\|(DefaultRunnerPinTests*)\|(DelegateBranchDeliveryTests*)\|(GrokRulesReadyOrderingTests*)\|(PhoneHomeBackpressureTests*)\|(PhoneHomeCompletedCardTransportTests*)\|(PhoneHomeConnectionTests*)\|(PhoneHomeImmediateSendTests*)\|(PhoneHomeQueuedTurnTests*)\|(PhoneHomeReadAdmissionTests*)/*` | CARD-1960 CP-4 (ConnectPeerAsync callers) | every named class, 0 failed | 12 | 15 | true |
