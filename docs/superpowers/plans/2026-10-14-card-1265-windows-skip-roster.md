# CARD-1265 Windows skip roster closure

Code task / landing owner: `c8d5341a-f946-4559-89d5-f1071792543c`.
Base fetched at start: `8c34bd258e40a963f0e53a22d277a608ce5a9998`.
Final profile, with the brief's explicit bounded-class exception: no whole Unit lane.

## Implementation

S1 records the existing closure defect and updates the independent exact count
oracles before the roster fix. Run CP-1 red. The closure assertion itself stays
unchanged from the base: its unrostered-gate failure is the defect witness.

S2 adds all missing native Linux entries, preserves their explicit reasons, and
standardizes the token-refresh class's reason field as `NativeLinuxReason`.
Include that class in the Windows child selection. Rerun CP-1 and run CP-2.
Do not change gate admission, assertion strength, or test deadlines.

The three CARD-1245 methods each contribute one identity and one result:
`Crlf_checkout_sends_carriage_return_free_remote_commands`,
`Upload_failure_surfaces_remote_stderr_without_the_token`, and
`Verify_failure_surfaces_remote_stderr` in `RefreshGithubTokenRemoteCommandTests`.
Therefore native identities change 57 -> 60, results 68 -> 71, synthetic audit
totals 72/105 -> 75/108, and not-executed totals 68/70 -> 71/73. Portable pass
counts stay 4 or 35/37. These remain independent literal oracles.

## Verification design

- V-1: unchanged `C1210_Roster_closure_matches_source_gates` fails before the
  roster repair and passes afterward; its bidirectional source scan checks every
  native gate in Antiphon.Tests, including helper edges and alias spellings.
- V-2: `C1210_Guard_matches_native_platform` and
  `C1210_Native_linux_entries_guard_before_work` verify exact reason constants,
  first-statement gates, and the 60-identity/71-result roster.
- V-3: `C1210_Audit_rejects_empty_or_incomplete_outcomes` checks the expanded
  child selection and independent 75/108 totals, preserving negative vectors.
- R-1: all three token-refresh methods pass on Linux with fake local bw/ssh.
- R-2: select the full admission class. Its Windows child audit is expected to
  skip on Linux with its existing explicit placement reason. Native Windows
  execution is disclosed as unavailable in this assigned Linux worktree.
- Manual: inspect fresh TRX identities/counts, inventory other missing gates,
  run both receipt validators, check full base..HEAD evidence policy, fetch and
  check merge-tree, verify pushed ref and clean worktree. No service restart.

CP-2 reuses CP-1's build to qualify the portable admission receipt without the
Windows-only skip. It is a planned diagnostic selection, not a repeat-proof run.
CP-1's full-row receipt is expected to fail strict zero-skips qualification on
Linux; do not mislabel it as a clean zero-skips certificate.

Use the checkpoint tool with `--expected-source-sha <committed HEAD>`,
`--row-timeout 40m --total-timeout 150m`, without `--keep-outputs`. The row override
sets effective manifest rowMinutes to 40; Linux defaults to UseAppHost=false.
One queued driver per invocation. Bootstrap the checkpoint tool once through
`scripts/build-slot.ps1` into `bin-c1265-tool/`; this supporting tool build is
the sole planned non-table build. Use the resulting tool with `--no-build`.
Retain ignored receipts; delete only literal producer-owned output paths.

### Positive controls (pending SourceLanding Mutation)

| ID | Method | Deliberate defect | Expected failure |
|---|---|---|---|
| PC-1 | C1210_Roster_closure_matches_source_gates | Remove each new roster entry separately (three variants) | c1210-unrostered-gate names the omitted method |
| PC-2 | C1210_Guard_matches_native_platform | Change token-refresh NativeLinuxReason only | c1210-exact-reason |
| PC-3 | C1210_Native_linux_entries_guard_before_work | Move each token-refresh gate below fixture setup (three variants) | c1210-first-statement |
| PC-4 | C1210_Audit_rejects_empty_or_incomplete_outcomes | Remove token-refresh class from ChildFilter | c1210-child-storage-selection |

Mutation owns all deliberate mutant/red/restore/green cycles. Code runs none.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | all | `tests/Antiphon.Tests -> bin-c1265-roster/` | roster-full | `/*/*/(WindowsUnitPlatformAdmissionTests*)\|(RefreshGithubTokenRemoteCommandTests*)/*` | V-1, V-2, V-3, R-1, R-2 | 7 passed, 0 failed, 1 Windows-only skip on Linux; S1 expected red | 7 | 10 | true |
| CP-2 | all | CP-1 | roster-portable | `/*/*/(WindowsUnitPlatformAdmissionTests*)/(C1210_Guard_matches_native_platform*)\|(C1210_Native_linux_entries_guard_before_work*)\|(C1210_Roster_closure_matches_source_gates*)\|(C1210_Audit_rejects_empty_or_incomplete_outcomes*)` | V-1, V-2, V-3 | exactly 4 passed, 0 failed/skipped | 4 | 1 | true |
