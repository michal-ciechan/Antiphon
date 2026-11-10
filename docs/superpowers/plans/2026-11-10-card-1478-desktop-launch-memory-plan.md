# CARD-1478 desktop launch memory floor

The card is the behavior spec. This file binds the closed Code checkpoints so the checkpoint tool can run them. CP-1 is the red launch-count witness. CP-2 is the eight-method green class.

## Verification design

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---|---|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-session-memory-red/` | desktop-memory-red | `/*/*/DesktopLaunchMemoryAdmissionTests/Low_memory_holds_before_launch` | V1 low-memory no effects | exactly 1 selected; expected launch-count assertion red | 1 | 6 |
| CP-2 | S1-S2 | `tests/Antiphon.Tests -> bin-session-memory-green/` | desktop-memory-admission | `/*/*/DesktopLaunchMemoryAdmissionTests/*` | V1-V8 | all 8 named methods, 0 failed/skipped | 8 | 7 |
