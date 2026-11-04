# CARD-0824 S4a Windows-native checkpoints

The Windows-native row of the [S4a test design](2026-11-03-card-0824-s4a-test-design.md) (N-1..N-3,
PC-66..PC-68), kept in its own doc so that the server2 run of CP-23/CP-24 never schedules a class that skips
on Linux. Run it on the desktop lane (`-Platform Windows`, no pinned runner) **only after the live Windows rc
qualification run has ended**. Nothing in S4a runs on the desktop before then.

`C0824DesktopLegacyReleaseNativeTests` has three methods: N-1 has 2 cases, N-2 has 1 and N-3 has 3, so
`Min` is 6. The 15 min estimate is provisional (the Windows `Antiphon.Tests` build is unmeasured); the first
run measures it.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-25 | S4a | tests/Antiphon.Tests -> bin-c0824-s4aw/ | windows-native-desktop-legacy-policy | /*/*/C0824DesktopLegacyReleaseNativeTests/* | N-1..N-3 | class, 0 failed/skipped | 6 | 15 | true |
