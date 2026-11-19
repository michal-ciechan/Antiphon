# DW-3 desktop-win phase 1 secret helper (Code task 066a9073)

Operator-only helper `scripts/desktop-win-phase1-secrets.ps1`. Dry-run is the
default and prints key names, paths, and whether those paths exist. `-Execute`
generates a 32-byte secret in memory with `RNGCryptoServiceProvider`, writes
lowercase hex plus one LF to the secret file, and stores the same hex with no
newline in the server user-secrets JSON. `-WhatIf` stays a dry-run. A second
`-Execute` without `-Rotate` refuses `shared-secret-present` and writes nothing.

`dotnet user-secrets set` takes the value as a process argument, and the CLI
has no supported stdin form for that value, so the helper updates
`%APPDATA%\Microsoft\UserSecrets\antiphon-server\secrets.json` in-process.
It preserves every other key and the file mode or ACL. No child process
receives the value.

`scripts/lib/windows-phone-home-env.ps1` does not generate
`C:\Antiphon\runner\phone-home.phase1.json`. `-Execute` copies
`scripts/phone-home.phase1.example.json` into place when that file is absent.
The example has no secret. The operator can copy it by hand instead. The
helper does not create phase 2 `phone-home.json`.

Child homes come from `Join-PhoneHomeChild` in the env lib. The helper does
not set `StandingAgentId`. It refuses when `ANTIPHON_ORCHESTRATOR` or
`CLAUDECODE` is set, and when `USERNAME` is not the owner of the first
existing ancestor of the secrets directory. A directory created from an
elevated shell can be owned by `BUILTIN\Administrators`; run the helper from
a shell whose `USERNAME` owns that directory. An existing secret file with
no map key refuses `secret-file-present` unless `-Rotate`. Failure output
prints `step=` and `changed: none` only when every target matches the bytes
saved before the change. A failed restore prints `differs:` and `kept:` and
never a value. A dry-run prints `secret-file-matches-map: yes` or `no` and
never the value. `no` means re-run `-Execute -Rotate` before restarting AppHost.

Linux owner-only mode is chmod 600 and is witnessed. The file is created
empty at mode 600, or created empty and chmod 600, before any secret byte is
written. The Windows owner-only ACL (`icacls /inheritance:r`, then a
current-user `FileSystemAccessRule`, verified before the value is written) is
UNOBSERVED on this Linux runner and is not a pass. `secrets.json` is written
to an owner-only sibling and swapped with `File.Replace` on Windows or
`File.Move` overwrite on Linux, then the previous mode or ACL is put back.
On Windows the replace passes a same-directory owner-only `.dw3bak`. That
backup exists only during the swap. It is deleted after the swap and the byte
cross-check, and on a failure it is kept until the saved bytes are restored
and checked, then deleted. A failed restore keeps the backup and the staged
temp and prints their names. A failure compares each target with the bytes
saved before any change and restores any target that differs.
User-secrets JSON is parsed by the helper's own reader, not `ConvertFrom-Json`,
so a nested object and a date-shaped string round-trip unchanged on pwsh 7 and
on Windows PowerShell 5.1. No migration. No positive-control row. Mutation
execution is waived (operator 2026-11-12) and is not discharged.

The assigned branch is already pushed, so the brief's final rebase onto
origin/master is refused. Landing rebases. This plan does not re-pin nightly
inputs.

## Verification design

- V-1. `scripts/test-desktop-win-phase1-secrets.ps1`: dry-run writes nothing and prints no value; `-Execute` creates the secret file and the map keys; the value is absent from all six streams and from every child argument; pre-existing user-secrets keys survive; a second `-Execute` without `-Rotate` refuses and changes nothing; agent env markers refuse; Linux mode is 600 at the first secret write and at the end; Windows ACL is labelled UNOBSERVED. Rotate with an unwritable `secrets.json` leaves both old values. A simulated user-secrets temp-write failure leaves that file byte-identical and leaves no `.dw3part`, `.tmp`, or `.bak`. A simulated failure after the user-secrets commit rolls that half back. Nested objects and date-shaped strings round-trip unchanged. A failing chmod leaves no secret file. Malformed JSON and a non-object document are refused without a write. An existing secret file without a map key refuses unless `-Rotate`. `-Rotate` success replaces both halves with the same new value. Failure output names the step. `changed: none` is printed only when both targets match the bytes saved before the change. A partial replace of `secrets.json`, and of the secret file under `-Rotate`, restores both saved byte strings, leaves no `.dw3part` or `.dw3bak`, and exits non-zero. When that restore fails, the output names the half that still differs and keeps the temp and backup by name. An invalid JSON escape is refused. A dry-run prints `secret-file-matches-map: yes` or `no` and never the value. Floor from this fix: passed=197 failed=0.
- V-2. `TestClassificationPolicyTests` once. Floor 42.
- V-3. The other eight classes of the nine-class guard union, without `WindowsUnitPlatformAdmissionTests` and without `DerivedImpactIndexTests`. Floor 39.
- V-4. `DerivedImpactIndexTests` alone. Floor 27. Regenerate the index only if this row fails.
- V-5. `WindowsUnitPlatformAdmissionTests`. Five methods run on Linux. `C1210_Windows_linux_only_outcomes_are_exact` skips on Linux and is not a pass. Floor 5.
- V-6. `scripts/audit-land-verification-graph.ps1 -Check`.
- V-7. `scripts/test-live-lane-run.ps1 -PrintInputDigest`. Report the digest. Do not re-pin.
- V-8. `scripts/check-evidence-diff.ps1` from `4da65a3cb107d42578dca963f5c914030b477437` (origin/master at the fast-forward, parent of the red witness) to HEAD.

C1729 must stay VIOLATIONS 0 LISTED 162. The original red witness is commit
`8c2d3bf644e40da22b6b59842c1ffdebd17859c6` (helper missing, passed=0 failed=1).
The blocker witnesses are commit `af1a06d64c1b9cf0ec6515ade5f316fb14801a0e`
(on `058dd52a`, passed=109 failed=30: unwritable rotate left a new secret
file, chmod failure left the file, nested and date-shaped JSON did not match,
and the temp-write and rollback seams were ignored). The round-3 partial-replace
witnesses are commit `926caf363a87d9f8e943ec03db984508d6195f8c` (passed=179
failed=18: `secrets.json` and the rotated secret file were absent while the
output said `changed: none`, the restore fault deleted the temp and did not
name the half, and an invalid escape was rewritten). The closed list below is
the official green run. The floor for CP-1 is passed=197 failed=0.

## OS branch witnesses

Changed production behavior is `Set-OwnerOnlyFile` in
`scripts/desktop-win-phase1-secrets.ps1`. The branch anchor is
`$env:OS -eq 'Windows_NT'`. The production type is
`System.Security.AccessControl.FileSystemAccessRule`.

- DW3-LINUX. targetOS Linux. testCase `execute-linux-mode-600`. checkpoint CP-1. executionMode native. Outcome is mode 600 via `chmod`. The Linux run of CP-1 is the native execution.
- DW3-WIN. targetOS Windows. testCase `unobserved-windows-acl`. checkpoint CP-1. executionMode native. The Windows ACL is pending. A label is not a pass.

`scripts/phone-home.phase1.example.json` has no OS read. The test script's
`$env:OS` branches only choose the observation. The ignored declaration is
`.antiphon/dw3-os-witnesses.json`. Startup impact is not required: this helper
is not on the AppHost or restart path. `merge-tree` is reported beside the
closed list and is not a row, because a master conflict is not fixed by merging.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---|---|---|---|
| CP-1 | dw3 | n/a | dw3-script | `pwsh -NoProfile -File scripts/test-desktop-win-phase1-secrets.ps1` | V-1 | RESULT passed=197 failed=0, exit 0 | n/a | 15 | true | n/a |
| CP-2 | dw3 | tests/Antiphon.Tests -> bin-dw3/ | dw3-policy | `/*/*/TestClassificationPolicyTests/*` | V-2 | 0 failed, executed at least 42 | 42 | 15 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-3 | dw3 | CP-2 | dw3-guards | `/*/*/(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)\|(TestLaneCategoryGuardTests*)/*` | V-3 | 0 failed, executed at least 39 | 39 | 12 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-4 | dw3 | CP-2 | dw3-index | `/*/*/DerivedImpactIndexTests/*` | V-4 | 0 failed, executed at least 27 | 27 | 12 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-5 | dw3 | CP-2 | dw3-windows | `/*/*/WindowsUnitPlatformAdmissionTests/*` | V-5 | 0 failed, executed at least 5; C1210_Windows_linux_only_outcomes_are_exact skips on Linux and is not a pass | 5 | 10 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-6 | dw3 | n/a | dw3-land | `pwsh -NoProfile -File scripts/audit-land-verification-graph.ps1 -Check` | V-6 | exit 0 | n/a | 8 | true | n/a |
| CP-7 | dw3 | n/a | dw3-pins | `pwsh -NoProfile -File scripts/test-live-lane-run.ps1 -PrintInputDigest` | V-7 | exit 0, digest printed, inputs not re-pinned | n/a | 5 | true | n/a |
| CP-8 | dw3 | n/a | dw3-evidence | `pwsh -NoProfile -File scripts/check-evidence-diff.ps1 -BaseRef 4da65a3cb107d42578dca963f5c914030b477437 -HeadRef HEAD` | V-8 | exit 0 | n/a | 5 | true | n/a |
