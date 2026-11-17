# Why missed: chmod in two CARD-1957 witnesses on Windows

rc run `f2de5b30c626461099fbfbb73fd8ada8`, rc/2026-11-26 (cut
`29ba0c5002e642c4f4f91805be7e17660a40906d`), chunk `antiphon-all-group-135`: 230 executed,
228 passed, 2 failed. Both failures threw the same exception at `Process.Start`, before any
assertion ran:

```
Win32Exception: An error occurred trying to start process chmod with working directory
C:\Antiphon\releases\checkout-4025018e\tests\Antiphon.Tests\bin\Debug\net9.0.
The system cannot find the file specified.
```

- `VolumeRetirementHostProofTests.Coordinator_receipt_names_the_failing_script_and_reason`
- `VolumeRetirementHostProofTests.Desktop_children_start_when_the_working_directory_is_264_characters`

The same chunk passed in run 06863906 on the earlier cut `870a93d21`, because neither method
existed then. CARD-1514 Debug task 7f4a215b. This fix changes only tests and policy.

## Introducing commit and card

- `3c7164b4bb990b06561ffc73381d3110a5630694` (CARD-1957, 2026-11-17 00:16Z),
  `test(card-1957): require live-scratch custody, named receipts, and short child cwds`, added
  both methods.
- `882a685bec` (CARD-1957, 00:32Z), `bind the receipt harness switches and create the
  264-character cwd`, changed them.
- `819783fd3e` is the product fix: `Get-C1609StartDirectory` and the named receipt.
- Review `a299c1fcae` was Final round 1, clean.
- The four commits reached master in merge batch 257 (index regen `7901e9ba53`). Batch 254
  (`321f98ec99`) does not contain them. They are on rc/2026-11-26 and not on `870a93d21`.

## Diagnosis: Linux-only by construction

Each method writes `#!/bin/sh` fakes (`ssh`, and for the 264-character test also `scp` and
`git`) into a `bin` directory and runs `chmod 0755` on them. It then puts that directory in
front of `PATH` for a pwsh child that runs `scripts/c590-real.ps1`.

These fakes cannot work on Windows:

- `chmod` is not on the desktop test host's PATH, which is the failure seen.
- If Git for Windows `usr\bin` were on PATH, the next step still could not work.
  `Invoke-C1609BoundedProcess -FileName ssh` and the coordinator's ssh launch resolve
  `ssh.exe` through Windows PATH lookup, so an extensionless sh script is never started. The
  real OpenSSH client would run instead.

The 264-character test is a Linux emulation of a Windows rule. Its fakes refuse when
`pwd` is 260 characters or longer, which models the CreateProcess MAX_PATH cwd limit. Its
comment already says "Nothing here runs on native Windows". The product behaviour it
protects (`Get-C1609StartDirectory` relocates a cwd of 260 characters or more to the temp
directory) is platform-neutral PowerShell. The Linux run proves that behaviour against the
emulated rule. A native Windows version needs a real `ssh.exe`/`scp.exe`/`git.exe` stand-in
and a pwsh host that can itself start under a 264-character cwd, which Windows refuses. That
is not a small test fix. Native Windows confirmation stays where Review a299c1fcae put it:
the orchestrator's `retire-temp` preview from the export root.

The receipt test asserts how the coordinator names a failure (`failure-script`,
`failure-reason`, `failure-line`). That is PowerShell text handling and has no Windows
branch. Linux coverage is the full witness for it.

The sibling `Child_deadline_and_output_redaction_are_bounded` in the same class shows the
portable pattern: `git.cmd` on Windows, and a sh fake plus `chmod` only in the `else`
branch. That pattern does not fit these two methods, for the `ssh.exe` reason above.

## Fix

This follows the CARD-1777 and CARD-1907 precedent. `DeployServer2ApiDefaultTests`'
reason is the same: "puts a #!/bin/sh docker and ssh on PATH and chmod's them".

1. Both methods carry `[NativeLinuxOnly("Linux-only by construction: ...")]`. That
   `SkipAttribute` skips with the reason on any non-Linux host, so an ad hoc or Unit-lane
   Windows run reports a skip instead of a red.
2. `tests/test-execution-policy.json` `profiles.rc.exclusions` gains one class row for
   `VolumeRetirementHostProofTests` listing exactly those two methods: `platform: windows`,
   the "Linux-only by construction:" reason, owner CARD-1514. Windows Full then does not
   launch them, the same way as the other Linux-only rows. Linux still requires them.
3. The re-pin is in its own commit:
   - `policyHash`: `c2e39398...6909` -> `5c478a13259f9d4bc3e638ab28ecee3ef517dc292d04f075246a796da944e352`.
   - `scripts/test-live-lane-run.ps1` `PinnedInputDigest`: `7cb7b55c...1869` ->
     `5e8c164421506fd1024e6da39351c097c4119f475957c41ee030c0c391488994`.
   - `LegacyExclusionDigest` (`6d6ff646...252a`) is unchanged: Linux-only rows stay out of it.

No `NativeLinuxGate.Require` call was added, so `WindowsUnitSkipRoster` and the C1210 closure
are unchanged. `tests/expected-platform-skips.json` is unchanged too, because the rc profile
excludes the two methods rather than tolerating a skip. No assertion, timeout or product
script changed.

## Why Linux-only verification missed it

- Every CARD-1957 run was on server2 Linux, where `chmod` and `/bin/sh` exist:
  - the Code checkpoints;
  - Review a299c1fcae, rows R-1 (`VolumeRetirementHostProofTests/*`, 19/19) to R-4;
  - the batch 257 merge rows.
- The derived impact index maps `scripts/c1609-host-proof.ps1`, `scripts/c590-real.ps1` and
  the fixtures to this class, but it selects classes, not platforms. Merge verification runs
  the selection on Linux only.
- The review's "Pending" item was the native Windows `retire-temp` preview of the product.
  It did not cover running the test class on Windows.
- No static guard covers this. The C1210 roster closure only accounts for existing
  `NativeLinuxGate`/`RequireNativeLinux` calls. The A5 census
  (`test-rc-platform.ps1 -Case LinuxOnlyWindowsExclusionPolicy`) only checks that markers and
  policy rows agree. A method with neither a marker nor a gate is invisible to both.
- The first lane that executes `Antiphon.Tests` on Windows is the Windows Full run: rc
  qualification or the Windows nightly. It runs after land and does not gate it. On
  2026-11-17 the desktop was dedicated to the live rc qualification, so the rc Full run was
  where the failure surfaced.

The C1817/C1818 note (`docs/investigations/2026-11-10-why-missed-c1817-c1818-windows.md`)
records the same class of miss a week earlier: POSIX tools started from tests, green on
Linux, red on Windows Full. Its remedy was a per-class impacted row, and that cannot catch a
new class.

## Cheapest pre-land guard (follow-up card, not built here)

Add a platform-neutral Roslyn guard test that runs on Linux in the standing guard union.

- **Scope.** For every method under `tests/**`, find each process start whose file name is
  a string literal naming a Unix-only tool, for example `Run("chmod", ...)`,
  `new ProcessStartInfo("bash")`, or `Process.Start("ln", ...)`. The tool set is `chmod`,
  `chown`, `bash`, `sh`, `ln`, `mkfifo`, `flock`, `setsid`, `stat`, `readlink`, `kill`,
  `pgrep`, `mount`, `unshare` and `nsenter`.
- **Pass condition.** Each such start must be inside a method that does one of these:
  - carries `[NativeLinuxOnly]`;
  - calls `NativeLinuxGate.Require` or `C1008HostFixture.RequireNativeLinux` first;
  - places the start in a branch dominated by `!OperatingSystem.IsWindows()` or
    `OperatingSystem.IsLinux()`.
- **What not to match.** Shebang text is not a reliable signal. Git hooks with
  `#!/bin/sh` legitimately run under Git for Windows' sh. A file-level grep finds about
  70 test files with shebangs or `"chmod"`, and literal starts of the tools above appear
  at 101 call sites in 48 files. The guard therefore matches starts only, not shebangs.
- **Rollout.** It needs a frozen, hashed baseline of today's ungated sites, as
  C1729 `LISTED` does. That is more than a few lines, so it is proposed as a follow-up card
  rather than folded into this repair.
- **What it would have caught.** Both CARD-1957 methods before Review, because they start
  `chmod` with no marker or gate.

## Evidence (server2 Linux, tip of this branch)

- Load was gated below 16 on the 1-minute average before every row.
- Every build or run went through `build-slot.ps1` with `ANTIPHON_LINUX_COMPILER_POLICY=0`,
  and no LINUX COMPILER line appeared.
- `--timeout 570s` on every run.

| Row | Selection | Result |
|---|---|---|
| index driver | `regenerate-derived-impact-index.ps1` | `derived-impact-index unchanged` |
| build | `dotnet build tests/Antiphon.Tests -p:UseAppHost=false` | 0 errors, 3m24s |
| two methods x20 | `VolumeRetirementHostProofTests/(Coordinator_receipt_names_the_failing_script_and_reason*)\|(Desktop_children_start_when_the_working_directory_is_264_characters*)`, 20 separate host runs | 20/20 runs, 40/40 passed, 0 skipped |
| class | `VolumeRetirementHostProofTests/*` | 19/19 passed |
| guard union | the 11 classes | 115 total, 114 passed, 1 skipped (`C1210_Windows_linux_only_outcomes_are_exact`, expected on Linux); C1729 `VIOLATIONS 0 LISTED 162` |
| A5 census | `test-rc-platform.ps1 -Case LinuxOnlyWindowsExclusionPolicy` | 6/6 PASS |
| live-lane pins | `test-live-lane-run.ps1 -Case C1545_WrapperOnlyPaidManifest` | 21/21 PASS (full-inputs, policy-edit) |

Windows was not run here: nothing may run on the desktop during the live qualification. Two
things prove the Windows outcome:

- The rc exclusion row keeps both methods out of Windows Full. The A5 census and
  `NativeLinuxPolicyWitnessTests`, both green above, check that row against the markers.
- `NativeLinuxOnlyAttribute.ShouldSkip` returns `!OperatingSystem.IsLinux()` for any other
  Windows execution.

The next Windows Full run on a candidate carrying this fix should show these two methods as
excluded, not executed.
