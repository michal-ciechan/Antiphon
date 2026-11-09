# Why missed: CapabilityScriptTests DPAPI case on Linux

CARD-1811, Code task 8fcf2612. Code task bad528a5 (CARD-1808) found it on
server2 at master `2bf29ee9d`:
`CapabilityScriptTests.issue_does_not_print_the_token_and_dpapi_round_trips`
failed with "Windows DPAPI Protect is not supported" after the stub had served
the issue request. This task reproduced it on the base source (red commit
`6d1ac8f7`, otherwise identical test bodies): `run.ExitCode` was 1, not 0.

## Cause

`capability.ps1 issue` writes the bearer with
`[System.Security.Cryptography.ProtectedData]::Protect(..., CurrentUser)` and
the test reads it back with `ProtectedData.Unprotect`. Both are Windows-only.
The class carries `[SupportedOSPlatform("windows")]`, but that only satisfies
the CA1416 platform analyzer. TUnit still discovers and runs the case on Linux.
There was never a runtime gate: the file has one commit, `86902399c`
(2026-09-05, CARD-0398 S4), and no later edit.

## Introducing commit/card

`86902399c` (CARD-0398). The CARD-0398 plan already called it a "Windows-only
DPAPI runner" (CARD-1043 plan, Scripts row), so the platform need was known; it
was expressed as an analyzer attribute instead of a skip and a policy row.

## Approving reviews

Not traced in this task. The case passed on every Windows run, which is where
CARD-0398 was reviewed, so no review had a red row to reject.

## Outside the selection

Each Linux selection excluded it for an unrelated reason, so none ever ran it:

- Unit lane (Linux shards, `[Category=Unit]`): the class is
  `[Category("Integration")]`.
- CARD-0590 Linux roster (`tests/linux-test-roster.json`): disposition
  `exclude`, boundary `native: ... ProtectedData`. The roster is a frozen
  admission record, and its exclusion carried no runtime effect.
- Nightly and the rc qualification run on Windows, where the case passes.
- Linux `rc` execution is a `PlatformDiagnostic` run (`coverageComplete=false`).
  With no Linux exclusion row it would launch the case and go red. No retained
  investigation records such a run including it.

A class-name filter, such as a Code/Review checkpoint row or a Merge batch
naming `CapabilityScriptTests`, bypasses both the category and the roster. That
is how CARD-1808 hit it.

## Fix (test-only, `scripts/capability.ps1` unchanged)

- The DPAPI case skips first with `Skip.Test` when `!OperatingSystem.IsWindows()`.
  Its Windows body and assertions are unchanged.
- New rc exclusion row: suite `antiphon`, class `CapabilityScriptTests`, method
  `issue_does_not_print_the_token_and_dpapi_round_trips`, platform `linux`,
  owner CARD-1811. This follows the CARD-1614 `HostCustodyTests` pattern: a
  launched skip leaves the census incomplete. `error_paths_never_contain_the_token`
  has no row and stays required on both platforms.
- Witness `C1811_Dpapi_case_is_windows_required_and_linux_excluded` requires
  exactly that row. Off Windows it also requires a `SkipTestException` from the
  DPAPI case, so the case does not start `capability.ps1`.
- Resealed together: nightly `policyHash`
  `7e54681b56c7925bef082d528cde3285ec579a13d51f34625c8c3aad1ee2dbb0` ->
  `9cf98d229e32ca52e76d6b8946856776c7c390e3027e2eae191f5042aca3b697`;
  live-lane `PinnedInputDigest`
  `b499df304358cc5a1ac64adaa6a482fa9dc29a76bd3d9f6bcc65ba9a54c47faf` ->
  `b587b96f62c5d652f2de4db1488fabcd0d050a5b34d35aa83af2cca04d10f031`.

## Non-native digest maintenance record

The row is not `Linux-only by construction:`, so it joins the legacy
(non-native) records pinned by `NativeLinuxPolicyWitness.LegacyExclusionDigest`.
Before: 91 records, digest
`be806b71aa6cdf11edc0961cb1babd782034c0556e5f2e985dbbe8f2f7d7592f` (reproduced
from the base policy with `tools/Antiphon.NativeLinuxMigrationAudit digest`).
After: 92 records, digest
`25dae45aca52b4b8de5dee6bf4abb4ebcae13b455dab8018e9d64221d10313ff`. Witnessed
count stays 95, so no witnessed identity moved into legacy. All existing records
are kept byte for byte. Only this record is added:

```json
{
  "suite": "antiphon",
  "class": "CapabilityScriptTests",
  "methods": [
    "issue_does_not_print_the_token_and_dpapi_round_trips"
  ],
  "platform": "linux",
  "reason": "Windows DPAPI round trip: capability.ps1 writes and the test reads a ProtectedData CurrentUser blob, which only Windows supports. The method remains required on Windows; error_paths_never_contain_the_token stays required on both platforms.",
  "owner": "CARD-1811"
}
```

The literal stays a reviewed oracle. Tests never compute it from live policy.

## Cheapest pre-land check

When a test touches a Windows-only API (`ProtectedData`, ACL types, junctions),
grep its class for an `OperatingSystem.IsWindows()` skip and an rc exclusion
row. `[SupportedOSPlatform]` alone is the signature of this miss. Linux
`linux-test-roster.json` `native:` exclusions with no runtime gate are
candidates for the same defect.
