# CARD-0824 S3 checkpoints

Closed rows copied from the desktop hot-slot plan (CP-2 through CP-4). CP-1 is the S2 provision script and is not this task. Durations stay the plan's provisional minutes until the checkpoint tool measures them. The real `RmGetList` probe is not in this table.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-2 | S3 | `tests/Antiphon.SessionRunner.Tests -> bin-win-slot/` | holder-scan | `/*/*/C0824WindowsSlotHolderTests/*` | D-7 | Linux: 1 executed, 1 skipped, 0 failed; Windows: 2 executed, 0 failed | 1 linux / 2 windows | 15 |
| CP-3 | S3 | CP-2 | longpaths | `/*/*/C0824WindowsSlotLongPathTests/*` | D-9 | all listed, 0 failed | 2 | 8 |
| CP-4 | S3 | `tests/Antiphon.Tests -> bin-win-admit/` | admission | `/*/*/C0824WindowsSlotAdmissionTests/*` | D-8 | all listed, 0 failed | 2 | 20 |

CP-2 methods: `Incomplete_scan_defers_release`, `Rm_lock_defers_release` (skipped on Linux). CP-3 methods: `Slot_checkout_passes_longpaths`, `Legacy_mirror_omits_longpaths`. CP-4 methods: `Windows_runner_stays_legacy_until_listed`, `Listed_investigate_runner_leases`.
