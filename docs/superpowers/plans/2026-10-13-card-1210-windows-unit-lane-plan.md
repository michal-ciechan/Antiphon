# CARD-1210 Windows Unit lane: gates, fixtures, stale harnesses and a bounded partition

Date: 2026-10-13. Stage: Plan; next stage: TestDesign. Inspected checkout:
`6e58767debf33d6a09c4e88050095f1cfdf76332` (master tip at dispatch). This dispatch changes this
document only. Every test, fix, count and positive control below is proposed; none has been
executed by Plan. The Debug triage is the ground truth for the failures:
`origin/feat/card-task-8db33c5e` commit `8dfad0572`,
`docs/superpowers/reports/2026-10-13-card-1210-windows-unit-triage.md` (read with
`git show`; not on master). Post-land Mutation for CARD-1209 was not run; this plan does not depend on it.

The card's premise is partly right: about 89 methods are inherited red on the Windows Unit lane,
and the whole lane does not complete inside 15 minutes. The triage classifies all 89 (plus four
HostJq descriptor probes the broad run never reached) into five causes. It did **not** find a
permanently stuck Windows child; both sampled waits completed when selected narrowly. This plan
fixes the five causes in six independently landable slices, bounds the lane with a per-test
timeout policy, and ends with an explicit Windows partition that CARD-1114 can schedule. The
whole-lane completion question stays open and is measured, not asserted, by the closing slice.

Session checklist: this plan adds or changes no session that waits for input, so the CARD-1083
release checklist does not apply.

## Evidence and ground truth

| Card assumption / triage claim | What the code or receipts actually show | Design consequence |
|---|---|---|
| "About 89 tests fail", mostly un-gated Linux tests. | CARD-1171's base TRX: 100 failed results, 89 distinct methods. Triage CP rows: 191 results, 89 passed, 102 failed, 0 skipped. 57 method identities (68 results) are Linux-only tests that **assert** `OperatingSystem.IsLinux()` instead of skipping: `HostJqPrerequisiteScriptTests` (39), `JqRunnerImageContractTests` (4 C1054 methods + the 12-argument `Version_row`), `GrokSha256PinTests` (11, runs `/bin/sh` directly), `CodexRootExecContractTests` (2 heal methods, `unshare` + bind mount on `/state`). | S1: entry gate before any fixture work, reported as skipped with a result-local reason, plus a bounded skip roster. |
| The repo already has a platform gate. | `C1008HostFixture.RequireNativeLinux()` at `tests/Antiphon.Tests/Scripts/RollingVolumeRecycleScriptTests.cs:787` throws `TUnit.Core.Exceptions.SkipTestException` with one CARD-1050 reason; `C1008PlatformContractTests` audits it three ways: exact reason, Roslyn first-statement check, and a Windows child-TRX roster audit (`outcome=NotExecuted`, `DebugTrace="Skipped: <reason>"`). | Reuse this idiom, generalised to a reason per contract; reuse the audit shape for the new roster. |
| "Some may be real Windows defects." | None established. 27 `RemoteScriptContractTests` methods and 3 `CodexRunnerImageContractTests` methods fail because `scripts/fixtures/c913-image-contract.sh`, `c913-seed-contract.sh` and `c1192-workspace-contract.sh` are `text=auto` in `.gitattributes` (index LF, Windows working copy CRLF under `core.autocrlf=true`) and run under WSL bash: `set: pipefail\r`. `.gitattributes` pins only `scripts/*.sh` and `c973-marker-reader.sh`. | S2: pin every `*.sh` to LF and prove it from the working tree; no harness normalisation. |
| Symlink tests are Windows defects. | Two `ChannelOutboundStorageTests` methods call `File.CreateSymbolicLink` and fail with "A required privilege is not held by the client" before any production assertion. `TestHelpers/DirectoryLink.cs` already makes a junction on Windows without privilege; `ChannelReplyAttachmentReader.cs:55` rejects any `ReparsePoint`, so a junction exercises the same rejection. | S3: junction for the directory case; capability-qualified named skip for file links; never a vacuous pass. |
| Stale tests. | Three `C849_*` methods extract function blocks from `scripts/c590-remote.sh`; `c849_seed` (line 2178) now calls `c849_payload_select`, `c849_payload_root`, `c849_payload_fs`, `c849_payload_call`, `c849_payload_note` (lines 1632-1713), none of which the extracted closures include, and publishes through `c849_payload_fs writer 'mv "$1" "$2"'` while the test still asserts `mv "$stage" "$recovery"`. `C1066FixtureHarness` already lists the payload closure (line 1267). `c849_validate_seed_tree` (1788-1811) does not call the payload selector, so the triage's stated cause for `C849_Seed_refuses_invalid_donors_and_partial_payloads` is not confirmed from source. | S4: one shared payload closure + a closure preflight; confirm the third method's exact failure text from the retained CP-12 TRX before editing. |
| "The lane hangs; a child never exits." | CP-10 (whole `RemoteScriptContractTests`, 3-minute cap) timed out without TRX. `ProcessSpawnLimit.Limit` is 1; the class has 178 `[Test]` attributes, most under the limiter, each spawning WSL bash on Windows. Dumps show 31 TUnit tasks queued behind one admitted spawn; `C973` waits in `LinuxShell -> Process.WaitForExit(60000)`; children changed between samples. CP-11 (C983, 18 child invocations) passed in 34 s; CP-13 (C973) passed 6/6 in 50 s. | Root cause of the timeout = a serial, unmeasured WSL queue plus CRLF failures, under a class/lane deadline below its runtime. No proof of a never-exiting child. S5 bounds every spawning test; S6 measures the class with a real budget. |
| Timeouts can be loosened to make it pass. | `LinuxShell` 60 s, ScriptHarness 300 s, `C983Fixture.Run` 5 min per child, Grok pin 15 s. The C983 driver script has an unbounded `WaitForExit` followed by pipe drains and a private pwsh resolver that bypasses CARD-1171's shared fix. | No existing deadline is widened. The C983 driver hardening is a separate card (D-12). |
| C1008's two Windows audits are red. | They need `Antiphon.Tests.exe`; CARD-1171 built with `UseAppHost=false`. With the normal Windows apphost (triage CP-14) all six pass. | Windows rows never pass `UseAppHost=false` (D-8). |
| Two more reds (channel phrase, Grok pin). | Repaired by CARD-1209 (`ea2ea7986`), green in triage CP-2/CP-6. | No duplicate work; CARD-1209 classes are not in this plan's rows. |
| Platform can be inferred from the card. | Card says Any. `GET /api/runner-defaults` revision 2: global default to a Linux runner, no kind overrides. `GET /api/session-runners`: one eligible Windows lane (capacity 2), one eligible Linux lane (capacity 10), one stale descriptor. | Linux rows use the default placement; Windows rows dispatch with `-Platform Windows` and no `-Runner`. No fleet location is embedded. |
| Nothing recurring runs Windows audits (CARD-1114). | `tests/test-execution-policy.json` profiles run whole suites; the nightly has no class-list mode and does not stage the `checkpoint-build-source.json` stamp the C1050 audits need. The checkpoint tool consumes a plan table and writes that stamp. | S6 publishes a partition table the tool can run; CARD-1114 schedules that run (D-10). |

## Decisions

- **D-1 Gate idiom.** Add `tests/Antiphon.Tests/TestHelpers/NativeLinuxGate.cs` with
  `internal static void Require(string reason)` that throws
  `TUnit.Core.Exceptions.SkipTestException(reason)` when `!OperatingSystem.IsLinux()`.
  `C1008HostFixture.RequireNativeLinux()` delegates to it with its unchanged CARD-1050 reason so
  `C1050_Guard_matches_native_platform` keeps passing. Each gated class declares
  `internal const string NativeLinuxReason = "CARD-1210: <contract> requires native Linux (<what>); Windows skips explicitly."`
  and every Linux-only test method starts with the statement `NativeLinuxGate.Require(NativeLinuxReason);`
  as its **first statement**, before any fixture construction or `using`. The existing
  `OperatingSystem.IsLinux().ShouldBeTrue(...)` assertions inside `HostJqFixture`, `C1025RolloutFixture`
  and `JqFixture` stay as a second line that fails loudly if a gate is ever bypassed.
  A gate must skip, never silently pass: no early `return`, no conditional assertion.
  Rejected: gating inside fixture constructors (a skip thrown after side effects, and
  `C1025_Final_recycle_guard_remains` asserts before constructing); whole-class gating of
  `JqRunnerImageContractTests` or `CodexRootExecContractTests` (discards their portable text checks);
  routing `GrokSha256PinTests` through WSL (`LinuxShell`) on Windows (the script is a Dockerfile
  fragment whose semantics are Linux-only, and WSL adds serial spawn cost for no additional proof).
- **D-2 Bounded skip roster.** `tests/Antiphon.Tests/Scripts/WindowsUnitSkipRoster.cs` holds the
  literal roster: `(className, methodName, expectedResults, reason)` for every Linux-only method
  (57 identities, 68 results) and a separate `Conditional` list for capability skips (S3's two
  file-link methods). `WindowsUnitPlatformAdmissionTests` (new, modelled on `C1008PlatformContractTests`) proves:
  (a) exact reason and skip type per class; (b) Roslyn first-statement check for every roster
  identity; (c) **closure**: every invocation of `NativeLinuxGate.Require` or
  `C1008HostFixture.RequireNativeLinux` under `tests/Antiphon.Tests/**/*.cs` maps to an enclosing
  method that is in this roster or in the C1050 rosters, and vice versa, so a new gated method cannot
  appear without a reviewed roster edit; (d) on Windows, a child `Antiphon.Tests.exe` run over the
  four classes plus `ChannelOutboundStorageTests` whose TRX `NotExecuted` multiset equals the roster
  exactly (identity, count, `Skipped: <reason>`), portable methods all `Passed`, conditional
  identities `Passed` or `NotExecuted` with the exact capability reason, and counters consistent.
  Rejected: deriving the roster from discovery or filters (the thing being audited), and attribute
  scanning by reflection (cannot see first statements).
- **D-3 LF fidelity.** Add `*.sh text eol=lf` to `.gitattributes` (a repo-wide rule; the eleven
  tracked `.sh` files are all LF in the index already, so no renormalisation commit is needed) and
  keep the existing card-specific lines. New `ShellFixtureLineEndingContractTests` proves the rule
  is active and effective for every tracked `*.sh` (same `EffectiveEol` idiom as
  `SessionRunnerGrokLineEndingContractTests`) and that each file's **working-tree bytes** contain no
  `\r`, which is the Windows-checkout proof. Rejected: normalising inside `LinuxShell` (it only
  normalises the stdin script; the fixtures are read by bash from disk, and hiding the checkout
  defect would also hide the deployment hazard CARD-0604 pinned); listing fixture files one by one
  (the set grew silently three times already). Existing Windows checkouts keep CRLF copies until
  the files are re-checked-out: the Windows Code delegate runs
  `git rm -q --cached -- scripts/fixtures/*.sh && git checkout -- scripts/fixtures/*.sh` once
  after committing the attribute change, and the operator does the same in the main desktop checkout
  after land; the LF test's failure message names this step.
- **D-4 Link capability.** Split `C1059_Source_reads_reject_file_and_directory_links` into
  `C1059_Source_reads_reject_directory_links` (uses `DirectoryLink.TryCreate`; a null result is a
  **failure**, junctions need no privilege) and `C1059_Source_reads_reject_file_links` (new
  `TestHelpers/FileLink.TryCreate` returning null only on Win32 error 1314
  `ERROR_PRIVILEGE_NOT_HELD` or `UnauthorizedAccessException`; null throws
  `SkipTestException(FileLink.PrivilegeReason)`). `Output_rename_and_link_swap_after_inspection_seals_the_opened_file`
  probes `FileLink.TryCreate` on a scratch path before staging and skips with the same reason.
  Both are roster `Conditional` entries. Rejected: hard links as a substitute (a different attack
  than the sealed-open-handle proof), enabling Developer Mode or elevation (host policy is not this
  task's to change), classifying the storage class Linux-only.
- **D-5 Stale harness.** Add `C849PayloadClosure()` returning the blocks for
  `c849_payload_controller`, `c849_payload_fixture_ready`, `c849_payload_select`, `c849_payload_call`,
  `c849_payload_fs`, `c849_payload_root`, `c849_payload_note` (whatever `c849_seed` and
  `c849_saved_copy` reference at the time of the slice; the Code task derives the list from
  `grep -oE '\bc849_[a-z_]+'` over the extracted blocks). Prefix every extracted harness with a
  **closure preflight**: for each `c849_*` name referenced by the extracted blocks,
  `declare -F` must succeed or the harness prints `MISSING <name>` and exits 97 before the case
  body runs. `C849_Seed_publishes_complete_payloads_before_its_marker` asserts the current
  publication path: `c849_smoke "$helper" seed` precedes the payload-controller move of `$stage` to
  `$recovery`, which precedes `docker start "$donor"`, which precedes the
  `mv -T -- "$C849_READY.tmp-$RUN" "$C849_READY"` marker publish. The third method's repair is
  chosen only after its exact CP-12 failure text is read. Rejected: gating these three as
  Linux-only (they are stale on Linux too; triage CP-12 is the proof they fail for a non-platform reason).
- **D-6 Heal fixture `/state`.** The two heal methods get the S1 gate with the reason
  "CARD-1210: Codex heal contracts require native Linux with unshare user+mount namespaces and a
  bind-mountable /state". On Linux a missing `/state` or a failing
  `unshare --user --map-root-user --mount true` is a **prerequisite failure** of that proof, reported
  by a new preflight line in `RunExtractedHeal` (`PREREQ unshare` / `PREREQ /state`), never a skip.
  Rejected: creating `/state` with `pivot_root` into a private root (a fixture rewrite for a test
  that is green on the Linux lane); `mkdir -p /state` on the real root (what fails under WSL).
- **D-7 Per-test timeout policy.** Every class that uses `[ParallelLimiter<ProcessSpawnLimit>]`
  carries a class-level `[Timeout(600_000)]` unless it already has one; existing method-level
  `[Timeout]` values stay and still win. No internal deadline changes (`LinuxShell` 60 s,
  ScriptHarness 300 s, `C983Fixture` 5 min per child, Grok 15 s). A timed-out test is reported
  failed with a TUnit timeout, so the lane completes and writes a TRX instead of dying at the
  checkpoint deadline with nothing. `ProcessSpawnTimeoutPolicyTests` (Roslyn) enforces the policy
  and the ceiling. Assumption A-1: TUnit 1.44's `TimeoutAttribute` applies at class level;
  TestDesign verifies with one deliberately slow probe before Code relies on it, and falls back to
  per-method attributes on the spawning methods if not.
- **D-8 Windows build profile.** Windows rows use the default apphost (`UseAppHost=true`); the
  checkpoint tool adds `UseAppHost=false` only off Windows. No Windows row or YAML pin may name
  `UseAppHost=false`; the C1050 and C1210 child audits need `Antiphon.Tests.exe` and the stamp.
- **D-9 Lanes.** Linux rows: default placement, omit `-Runner` and `-Platform`. Windows rows:
  `-Platform Windows`, omit `-Runner`. Re-read both routes immediately before each dispatch.
- **D-10 Partition and CARD-1114.** S6 commits
  `docs/superpowers/plans/2026-10-13-card-1210-windows-unit-partition.md` whose `### Checkpoints`
  table is the explicit Windows class list (not the lane) and is consumable by
  `tools/Antiphon.Checkpoints run --plan`. Recommended scheduling: a scheduled card action on
  CARD-1114 with `acceptSpend` that spawns a `-Platform Windows` Review-role checkpoint task at master
  tip on a weekly cadence, running that table. Alternative: a new `windows-unit-partition` suite
  in `tests/test-execution-policy.json` run by the Windmill nightly; rejected for now because it
  changes the policy hash, the nightly's suite model is whole-assembly, and it would still lack the
  build stamp unless the nightly adopts the checkpoint tool.
- **D-11 No suppression.** No retry, no widened assertion, no loosened deadline anywhere in this
  card. If S6's measurement row still exceeds its budget after S1-S5, the Code report ends with
  `next: investigate` naming the longest-running methods from the TRX durations, instead of a longer budget.
- **D-12 Out of scope, to be filed.** C983 driver script unbounded `WaitForExit` and pipe drains
  without an EOF deadline; its private first-existing pwsh resolver. File one test-harness card
  with method-scoped controls; do not claim it fixes the lane timeout.

## The platform-gate idiom

Existing gate, reused unchanged in meaning (`RollingVolumeRecycleScriptTests.cs:787`):

```csharp
internal static void RequireNativeLinux()
{
    if (!OperatingSystem.IsLinux())
        throw new TUnit.Core.Exceptions.SkipTestException(
            "CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately.");
}
```

Generalised form every S1 class uses:

```csharp
// tests/Antiphon.Tests/TestHelpers/NativeLinuxGate.cs
internal static class NativeLinuxGate
{
    internal static void Require(string reason)
    {
        if (!OperatingSystem.IsLinux()) throw new TUnit.Core.Exceptions.SkipTestException(reason);
    }
}

// in each gated class
internal const string NativeLinuxReason =
    "CARD-1210: C1025/C1058 host jq contracts require native Linux (bash, flock, /proc/self/fd, GNU coreutils); Windows skips explicitly.";

[Test, ParallelLimiter<ProcessSpawnLimit>]
public async Task C1058_Open_descriptor_must_match_canonical_leaf()
{
    NativeLinuxGate.Require(NativeLinuxReason);   // first statement, before any fixture
    ...
}
```

Rule: a gate throws `SkipTestException`; the TRX records `outcome="NotExecuted"` and a
result-local `Skipped: <reason>`. A gate that returns, asserts conditionally, or runs after a
fixture constructor is a defect the roster audit catches. The four expression-bodied HostJq probe
methods (`C1058_*_probe_uses_open_descriptor`) become block bodies so the first-statement check applies.

## Slices

Each slice is independently landable, 30-60 minutes of Code work, test-only unless stated.

### S1 Native Linux entry gates and the Windows skip roster (cause L1)

Files: `tests/Antiphon.Tests/TestHelpers/NativeLinuxGate.cs` (new);
`tests/Antiphon.Tests/Scripts/WindowsUnitSkipRoster.cs` (new);
`tests/Antiphon.Tests/Scripts/WindowsUnitPlatformAdmissionTests.cs` (new);
`tests/Antiphon.Tests/Scripts/HostJqPrerequisiteScriptTests.cs` (39 methods gated);
`tests/Antiphon.Tests/Infrastructure/JqRunnerImageContractTests.cs` (5 methods);
`tests/Antiphon.Tests/Infrastructure/GrokSha256PinTests.cs` (11);
`tests/Antiphon.Tests/Infrastructure/CodexRootExecContractTests.cs` (2, plus the D-6 preflight);
`tests/Antiphon.Tests/Scripts/RollingVolumeRecycleScriptTests.cs` (delegation only).

Decisions: D-1, D-2, D-6. Risks: the Roslyn closure check must ignore the gate's own definition
and the audit's own literals; the child audit inherits the C1050 environment hygiene (strip
`ANTIPHON_C*_WORKER`, `C885_*`, `ANTIPHON_CHECKPOINT_NONCE`; set the production-runner guard).
Expected Windows partition after S1: four classes, 72 results, 68 skipped with reasons, 4 passed.

Case -> mutation -> red:

- Gate present per method -> remove `NativeLinuxGate.Require` from one roster method ->
  `C1210_Native_linux_entries_guard_before_work` red on both lanes; on Windows the child audit is red
  because that identity is `Failed`, not `NotExecuted`.
- Roster is closed -> add a gate to a method not in the roster -> `C1210_Roster_closure_matches_source_gates` red.
- Reason is exact -> change one class's `NativeLinuxReason` -> `C1210_Guard_matches_native_platform` red; Windows child audit red on `Skipped:` text.
- Skip, never pass -> replace a gate with `if (!IsLinux) return;` -> first-statement red; Windows child audit red (identity `Passed` while rostered as skipped).
- Audit is decisive -> `C1210_Audit_rejects_empty_or_incomplete_outcomes` corrupts a valid TRX
  fourteen ways (as C1050 does) and requires the named assertion.

### S2 LF fidelity for shell fixtures (cause W1)

Files: `.gitattributes` (one rule); `tests/Antiphon.Tests/Infrastructure/ShellFixtureLineEndingContractTests.cs` (new, 2 methods);
`docs/testing-and-build.md` (two sentences on the re-checkout step).

Decisions: D-3. Risks: a stale CRLF working copy in a pre-existing Windows checkout fails the
byte test until re-checked-out, by design; the summary-validator awk extractor in
`c1192-workspace-contract.sh` needs no change once the file is LF (the `^}$` predicate was failing on `}\r`).

Case -> mutation -> red:

- Rule active -> delete `*.sh text eol=lf` -> `Shell_scripts_are_forced_lf_by_gitattributes` red (effective eol unspecified for `scripts/fixtures/c913-seed-contract.sh`).
- Bytes are LF -> write one `\r\n` into a tracked `.sh` in the scratch worktree -> `Shell_scripts_have_no_carriage_returns` red naming the file and the re-checkout command.
- Fixtures run -> the 40 `RemoteScriptContractTests` fixture methods and 13 `CodexRunnerImageContractTests` methods are the behavioural proof on Windows (rows CP-18, CP-19).

### S3 Windows link capability in outbound storage (cause W2)

Files: `tests/Antiphon.Tests/TestHelpers/FileLink.cs` (new); `tests/Antiphon.Tests/Application/ChannelOutboundStorageTests.cs`
(one method split into two, one method gains a capability probe); `WindowsUnitSkipRoster.cs` (`Conditional` entries).

Decisions: D-4. Risks: `Directory.Delete` of a junction must remove the link only (DirectoryLink's
Dispose already does); the capability probe must distinguish error 1314 from a real I/O failure.

Case -> mutation -> red:

- Directory links rejected -> remove the `ReparsePoint` check at `ChannelReplyAttachmentReader.cs:55` -> `C1059_Source_reads_reject_directory_links` red on Linux (symlink) and Windows (junction).
- File links rejected -> same production mutation -> `C1059_Source_reads_reject_file_links` red on Linux and on a capable Windows host; on an incapable host it skips with `FileLink.PrivilegeReason` and the roster audit confirms the exact reason.
- Never vacuous -> make `FileLink.TryCreate` return null unconditionally -> on Linux CP-10 shows 1 skipped where 0 is expected (clean certificate refused).

### S4 Stale C849 extracted harnesses (cause S1)

Files: `tests/Antiphon.Tests/Scripts/RemoteScriptContractTests.cs` only (`C849PayloadClosure()`,
closure preflight in `CacheSeedTreeHarness`/the saved-donor harness, three repaired methods, one new
negative method `C849_Closure_preflight_names_a_missing_dependency`).

Decisions: D-5. Risks: collides with CARD-1208 on `RemoteScriptContractTests.cs` and on the
`c590-remote.sh` functions the closure extracts; the Code task starts by reading the retained CP-12
TRX for `C849_Seed_refuses_invalid_donors_and_partial_payloads` and records the exact message in its report.

Case -> mutation -> red:

- Closure complete -> drop `c849_payload_select` from `C849PayloadClosure()` -> preflight prints `MISSING c849_payload_select`, exit 97, `C849_Saved_donor_archive_is_checked_and_imported_without_a_container` red before the case body.
- Preflight itself -> `C849_Closure_preflight_names_a_missing_dependency` extracts a harness with one name removed and asserts the `MISSING` line and exit 97.
- Publication order -> move `c849_smoke "$helper" seed` below the recovery move in a scratch copy of `c590-remote.sh` -> `C849_Seed_publishes_complete_payloads_before_its_marker` red.
- Marker last -> move the `mv -T -- "$C849_READY.tmp-$RUN"` line above `docker start "$donor"` -> same method red.

### S5 Per-test timeout policy for process-spawning tests (lane bound)

Files: `tests/Antiphon.Tests/Scripts/ProcessSpawnTimeoutPolicyTests.cs` (new, 2 methods);
class-level `[Timeout(600_000)]` on every class using `ProcessSpawnLimit` that lacks one
(expected about 20 files under `tests/Antiphon.Tests/Scripts` and `Infrastructure`; the Code task
lists them from the Roslyn census in its report); no method-level change.

Decisions: D-7, D-11. Risks: A-1; a class-level attribute on `RemoteScriptContractTests` touches
the same file as S4 and CARD-1208, so S5 lands after S4.

Case -> mutation -> red:

- Every spawning class bounded -> remove the class attribute from `GrokSha256PinTests` -> `Spawning_classes_carry_a_class_timeout` red naming the class.
- Ceiling -> set one class to `[Timeout(1_200_000)]` -> `Spawn_timeouts_do_not_exceed_ten_minutes` red.
- Timeout reports, lane completes -> TestDesign's probe: a `[Timeout(2_000)]` class with a 10 s sleep must appear in TRX as `Failed`, not hang the host (this is the A-1 verification, not a committed test).

Root-cause statement for the hang: the Windows failure mode is a serial queue, not a deadlock.
`ProcessSpawnLimit` admits one spawning test at a time; `RemoteScriptContractTests` alone has 178
`[Test]` attributes and 125 `[Arguments]` expansions, most of which launch WSL bash; 27 of them
also failed fast on CRLF. A 3-minute class cap and a 15-minute lane cap are below the serial
runtime of that class on Windows, which has never been measured. What remains unknown: the true
Windows runtime of `RemoteScriptContractTests` after S2, whether any child blocks past
`LinuxShell`'s 60 s bound under load, and why the historical Linux whole-Unit selection reached
its deadline (CARD-0971's sample was on Linux and is not explained by these receipts).

### S6 Bounded Windows partition and CARD-1114 scheduling (closing slice)

Files: `docs/superpowers/plans/2026-10-13-card-1210-windows-unit-partition.md` (new; its
`### Checkpoints` table lists, by class or exact method group, the Windows rows below plus the
C1050 audits and `DelegateLaunchArgvIntegrityTests`); `docs/testing-and-build.md` (a "Windows Unit
partition" paragraph pointing at that file and at CARD-1114); CARD-1114 card text proposal in the
Code report (the card is edited by the caller, not by the delegate).

Decisions: D-8, D-9, D-10, D-11. Risks: CP-23 is the first measured whole-class Windows run; its
budget is a measurement, not a promise. The partition is explicit and bounded: it is **not** the
Unit lane, and whole-lane completion stays an open obligation recorded on CARD-1210 until a later
card extends the partition class by class with measured budgets.

Case -> mutation -> red: the partition file is validated by the checkpoint importer (CP-25); a
malformed row or a `CP-n` reuse across lanes is an import refusal.

## Ordering and collisions

Order: S1 -> S2 -> S3 are independent and may run in parallel as three Code tasks (different
files). S4 waits for CARD-1208's current round on `RemoteScriptContractTests.cs` and
`scripts/c590-remote.sh` to land or to settle its function names; S5 after S4 (same file). S6 after
S1-S5 so its measurement row sees the repaired fixtures.

| Slice | Files touched | Collides with |
|---|---|---|
| S1 | `TestHelpers/NativeLinuxGate.cs`, `Scripts/WindowsUnitSkipRoster.cs`, `Scripts/WindowsUnitPlatformAdmissionTests.cs`, `Scripts/HostJqPrerequisiteScriptTests.cs`, `Infrastructure/JqRunnerImageContractTests.cs`, `Infrastructure/GrokSha256PinTests.cs`, `Infrastructure/CodexRootExecContractTests.cs`, `Scripts/RollingVolumeRecycleScriptTests.cs` | None in flight. CARD-0989-lineage branches touched `GrokSha256PinTests.cs` on 2026-10-12 and are landed. |
| S2 | `.gitattributes`, `Infrastructure/ShellFixtureLineEndingContractTests.cs`, `docs/testing-and-build.md` | None; CARD-1208 edits `c590-remote.sh` content, not its attributes. |
| S3 | `TestHelpers/FileLink.cs`, `Application/ChannelOutboundStorageTests.cs`, `Scripts/WindowsUnitSkipRoster.cs` | None in flight (CARD-1032/1033/1205 touch runner/server code, not outbound storage). The roster file is shared with S1: land S1 first or merge trivially. |
| S4 | `Scripts/RemoteScriptContractTests.cs` | **CARD-1208** (same file and the extracted `c849_*`/finalizer functions in `scripts/c590-remote.sh`); nine CARD-1192 S7 review branches touched the same file on 2026-10-12/13. Defer S4 while a CARD-1208 Code task is in flight. |
| S5 | about 20 test classes incl. `RemoteScriptContractTests.cs` | S4, CARD-1208 (same file). Land after both. |
| S6 | partition plan doc, `docs/testing-and-build.md` | None. |

CARD-1032/1033/1205 touch `server/` and `src/Antiphon.SessionRunner` only; no slice here touches
production code, so the only shared area is the S3 production mutation used as a positive control
(not a committed change).

## Operator decisions (D-n needing an answer, with recommendation)

- **OD-1** Weekly scheduled card action with `acceptSpend` on CARD-1114 running the partition
  table on the Windows lane (recommended) versus a nightly policy suite. Recommendation: scheduled
  action now; revisit the nightly once it adopts the checkpoint tool.
- **OD-2** Ten-minute class-level ceiling for spawning tests (recommended) versus five minutes.
  Ten keeps every measured method (C983 34 s, C973 50 s, C1050 child audit under 5 min) far below
  the bound and still forces a TRX within the row budget.
- **OD-3** Repo-wide `*.sh text eol=lf` (recommended) versus `scripts/fixtures/*.sh` only. The
  repo-wide rule also protects `docker/**` and `tests/fixtures/**`, which are already pinned one by one.
- **OD-4** Dispatch S1, S2, S3 in parallel now (recommended, within the standing six-in-flight
  authority) and hold S4/S5 behind CARD-1208.

## Verification design

### V/R matrix

| ID | Statement | Slice | Rows |
|---|---|---|---|
| V-1 | Every Linux-only method in the four S1 classes skips on Windows before fixture work with its exact reason, and executes on Linux. | S1 | CP-1..CP-4, CP-14, CP-15 |
| V-2 | The skip roster is closed in both directions (source gates = roster) and the Windows child TRX matches it exactly. | S1 | CP-5, CP-14 |
| V-3 | `C1008HostFixture.RequireNativeLinux` keeps its CARD-1050 reason and audits. | S1 | CP-6, CP-16 |
| V-4 | Every tracked `*.sh` is pinned LF and is LF on disk, including a Windows checkout. | S2 | CP-7, CP-17 |
| V-5 | The CRLF-failing fixture consumers pass on Windows under WSL and remain green on Linux. | S2 | CP-8, CP-9, CP-18, CP-19 |
| V-6 | Outbound storage rejects directory links on every platform and file links wherever the host can create them; incapable hosts skip with the exact capability reason. | S3 | CP-10, CP-20 |
| V-7 | The three C849 harnesses carry a complete closure, a preflight, and defect-sensitive publication-order assertions. | S4 | CP-11, CP-21 |
| V-8 | The two sampled "hang" methods complete inside their bounds on both lanes. | S4 | CP-11, CP-21 |
| V-9 | Every process-spawning class is bounded by a class-level timeout of at most ten minutes. | S5 | CP-12, CP-22 |
| V-10 | The bounded Windows partition imports and runs to a TRX with zero failures and only rostered skips. | S6 | CP-23..CP-25 |
| R-1 | `ProcessSpawnLimitTests` still caps spawning tests at one. | S5 | CP-13 |
| R-2 | C1050 Windows audits pass with the default apphost. | S1 | CP-16 |
| R-3 | CARD-1114's Windows-only argv round-trip runs on the Windows lane. | S6 | CP-24 |

### Positive controls (all pending; SourceLanding Mutation, method-scoped)

| PC | Exact detecting filter | Mutation (scratch copy) | Expected red |
|---|---|---|---|
| PC-1 | `/*/*/WindowsUnitPlatformAdmissionTests/C1210_Native_linux_entries_guard_before_work` | Remove the gate from `HostJqPrerequisiteScriptTests.C1025_Check_has_no_install_effects` | first-statement assertion names the identity |
| PC-2 | `/*/*/WindowsUnitPlatformAdmissionTests/C1210_Roster_closure_matches_source_gates` | Add `NativeLinuxGate.Require` to `JqRunnerImageContractTests.Qualification_grades_the_jq_row_for_both_targets` | closure assertion lists the unrostered identity |
| PC-3 | `/*/*/WindowsUnitPlatformAdmissionTests/C1210_Guard_matches_native_platform` | Change `GrokSha256PinTests.NativeLinuxReason` by one character | exact-reason assertion |
| PC-4 | `/*/*/WindowsUnitPlatformAdmissionTests/C1210_Windows_linux_only_outcomes_are_exact` (Windows) | Replace one gate with `if (!OperatingSystem.IsLinux()) return;` | host-roster multiset assertion (`Passed` where `NotExecuted` expected) |
| PC-5 | `/*/*/ShellFixtureLineEndingContractTests/Shell_scripts_are_forced_lf_by_gitattributes` | Delete the `*.sh text eol=lf` line | missing-rule / effective-eol assertion |
| PC-6 | `/*/*/ShellFixtureLineEndingContractTests/Shell_scripts_have_no_carriage_returns` | Write `\r\n` into `scripts/fixtures/c913-seed-contract.sh` | CR assertion naming the file |
| PC-7 | `/*/*/ChannelOutboundStorageTests/C1059_Source_reads_reject_directory_links` | Remove the `ReparsePoint` flag from the attribute mask at `ChannelReplyAttachmentReader.cs:55` | rejection assertion (Linux symlink, Windows junction) |
| PC-8 | `/*/*/ChannelOutboundStorageTests/C1059_Source_reads_reject_file_links` (Linux) | Same production mutation | file-link rejection assertion |
| PC-9 | `/*/*/RemoteScriptContractTests/C849_Saved_donor_archive_is_checked_and_imported_without_a_container` | Drop `c849_payload_select` from `C849PayloadClosure()` | `MISSING c849_payload_select`, exit 97 |
| PC-10 | `/*/*/RemoteScriptContractTests/C849_Seed_publishes_complete_payloads_before_its_marker` | Swap smoke and recovery-move order in copied `c590-remote.sh` | `Order` assertion |
| PC-11 | `/*/*/ProcessSpawnTimeoutPolicyTests/Spawning_classes_carry_a_class_timeout` | Remove `[Timeout]` from `CodexRootExecContractTests` | class named in failure |
| PC-12 | `/*/*/ProcessSpawnTimeoutPolicyTests/Spawn_timeouts_do_not_exceed_ten_minutes` | Set one class to `[Timeout(1_200_000)]` | ceiling assertion |
| PC-13 | `/*/*/CodexRootExecContractTests/Entrypoint_heal_chowns_the_tmp_directory_once` (Linux) | Make the preflight report `PREREQ /state` unconditionally | prerequisite failure, not skip |

### Checkpoints

Rows CP-1..CP-13 are the **Linux lane** (default placement). Rows CP-14..CP-25 are the
**Windows lane** (`-Platform Windows`, no `-Runner`, default apphost; never pass
`UseAppHost=false`). Each slice's Code task runs its own `--after` group on its lane; a Windows row
never reuses a Linux build. No row runs the whole Unit lane. CP-15's and CP-20's conditional skips
are stated in `Expect`; the strict zero-skip clean-source certificate is not claimed for those two
rows, and is claimed for every other row. `HostJqPrerequisiteScriptTests` and `GrokSha256PinTests` have no direct Windows row on purpose: every method skips there, and `run-checkpoint.ps1` drops `NotExecuted` results from the executed roster, so an `-Expect` token for a fully skipped class is exit 3; their Windows proof is the CP-14 child audit. CP-23 is a measurement row: a red by timeout is reported
as such and routes to `next: investigate` (D-11), never to a larger budget in the same task.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | EstimatedMinutesWindows | Serial |
|---|---|---|---|---|---|---|---:|---:|---:|---|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1210-l-s1/` | linux-hostjq | `/*/*/HostJqPrerequisiteScriptTests/*` | V-1 | all 39 passed, 0 skipped | 39 | 15 | n/a | true |
| CP-2 | S1 | CP-1 | linux-jq | `/*/*/JqRunnerImageContractTests/*` | V-1 | all 18 passed (12 Version_row arguments), 0 skipped | 18 | 3 | n/a | true |
| CP-3 | S1 | CP-1 | linux-grok-sha | `/*/*/GrokSha256PinTests/*` | V-1 | all 11 passed, 0 skipped | 11 | 2 | n/a | true |
| CP-4 | S1 | CP-1 | linux-codex-heal | `/*/*/CodexRootExecContractTests/*` | V-1 | all 4 passed, 0 skipped; `PREREQ` lines absent | 4 | 2 | n/a | true |
| CP-5 | S1 | CP-1 | linux-admission-static | `/*/*/WindowsUnitPlatformAdmissionTests/(C1210_Guard_matches_native_platform*)\|(C1210_Native_linux_entries_guard_before_work*)\|(C1210_Roster_closure_matches_source_gates*)\|(C1210_Audit_rejects_empty_or_incomplete_outcomes*)` | V-2 | exactly 4 passed, 0 skipped | 4 | 2 | n/a | false |
| CP-6 | S1 | CP-1 | linux-c1050-static | `/*/*/C1008PlatformContractTests/(C1050_Guard_matches_native_platform*)\|(C1050_Remote_entries_guard_before_work*)\|(C1050_Rolling_entries_guard_before_work*)\|(C1050_Audit_rejects_empty_or_incomplete_outcomes*)` | V-3 | exactly 4 passed, 0 skipped | 4 | 1 | n/a | false |
| CP-7 | S2 | `tests/Antiphon.Tests -> bin-c1210-l-s2/` | linux-lf-contract | `/*/*/ShellFixtureLineEndingContractTests/*` | V-4 | exactly 2 passed | 2 | 6 | n/a | false |
| CP-8 | S2 | CP-7 | linux-codex-image | `/*/*/CodexRunnerImageContractTests/*` | V-5 | all 13 passed, 0 skipped | 13 | 3 | n/a | true |
| CP-9 | S2 | CP-7 | linux-remote-fixtures | `/*/*/RemoteScriptContractTests/(C1192_*)\|(C913_*)\|(C1191_Lock_probe_is_created_before_uid_handoff*)\|(C1195_Retention_probe_is_app_uid_and_does_not_copy_image_content*)\|(C849_Prune_refuses_stale_or_busy_authority*)` | V-5 | 31 + 6 + 1 + 1 + 1 = 40 passed, 0 skipped | 40 | 8 | n/a | true |
| CP-10 | S3 | `tests/Antiphon.Tests -> bin-c1210-l-s3/` | linux-storage | `/*/*/ChannelOutboundStorageTests/*` | V-6 | all 33 passed (32 + the split method), 0 skipped, real symlinks | 33 | 7 | n/a | true |
| CP-11 | S4 | `tests/Antiphon.Tests -> bin-c1210-l-s4/` | linux-c849-harness | `/*/*/RemoteScriptContractTests/(C849_Saved_donor_archive_is_checked_and_imported_without_a_container*)\|(C849_Seed_refuses_invalid_donors_and_partial_payloads*)\|(C849_Seed_publishes_complete_payloads_before_its_marker*)\|(C849_Closure_preflight_names_a_missing_dependency*)\|(C983_Required_jq_driver_rejects_successful_false_probe*)\|(C973_Cold_marker_readers_accept_pruned_seed_image_and_refuse_invalid_markers*)` | V-7, V-8 | 4 + 1 + 6 = 11 passed, 0 skipped; C973/C983 durations recorded | 11 | 9 | n/a | true |
| CP-12 | S5 | `tests/Antiphon.Tests -> bin-c1210-l-s5/` | linux-timeout-policy | `/*/*/ProcessSpawnTimeoutPolicyTests/*` | V-9 | exactly 2 passed | 2 | 6 | n/a | false |
| CP-13 | S5 | CP-12 | linux-spawn-limit | `/*/*/ProcessSpawnLimitTests/*` | R-1 | all listed, 0 failed | 1 | 1 | n/a | true |
| CP-14 | S1 | `tests/Antiphon.Tests -> bin-c1210-w-s1/` | win-admission | `/*/*/WindowsUnitPlatformAdmissionTests/*` | V-1, V-2 | exactly 5 passed, 0 skipped; child audit prints 68 `NotExecuted` with roster reasons and 4 `Passed` | 5 | 10 | 10 | true |
| CP-15 | S1 | CP-14 | win-portable-direct | `/*/*/(JqRunnerImageContractTests*)\|(CodexRootExecContractTests*)/*` | V-1 | 4 passed, 18 skipped with roster reasons, 0 failed; no zero-skip certificate claimed | 4 | 3 | 3 | true |
| CP-16 | S1 | CP-14 | win-c1050-platform | `/*/*/C1008PlatformContractTests/*` | V-3, R-2 | all 6 passed, 0 skipped (default apphost) | 6 | 4 | 4 | true |
| CP-17 | S2 | `tests/Antiphon.Tests -> bin-c1210-w-s2/` | win-lf-contract | `/*/*/ShellFixtureLineEndingContractTests/*` | V-4 | exactly 2 passed after the one-time re-checkout of `scripts/fixtures/*.sh` | 2 | 8 | 8 | false |
| CP-18 | S2 | CP-17 | win-codex-image | `/*/*/CodexRunnerImageContractTests/*` | V-5 | all 13 passed, 0 skipped (WSL bash present) | 13 | 4 | 4 | true |
| CP-19 | S2 | CP-17 | win-remote-fixtures | `/*/*/RemoteScriptContractTests/(C1192_*)\|(C913_*)\|(C1191_Lock_probe_is_created_before_uid_handoff*)\|(C1195_Retention_probe_is_app_uid_and_does_not_copy_image_content*)\|(C849_Prune_refuses_stale_or_busy_authority*)` | V-5 | 40 passed, 0 skipped (WSL pwsh and jq present; a CARD-0905 skip is a prerequisite failure of this row) | 40 | 12 | 12 | true |
| CP-20 | S3 | `tests/Antiphon.Tests -> bin-c1210-w-s3/` | win-storage | `/*/*/ChannelOutboundStorageTests/*` | V-6 | 31 passed; `C1059_Source_reads_reject_file_links` and `Output_rename_and_link_swap_after_inspection_seals_the_opened_file` each Passed or NotExecuted with the exact `FileLink.PrivilegeReason`; 0 failed | 31 | 8 | 8 | true |
| CP-21 | S4 | `tests/Antiphon.Tests -> bin-c1210-w-s4/` | win-c849-harness | `/*/*/RemoteScriptContractTests/(C849_Saved_donor_archive_is_checked_and_imported_without_a_container*)\|(C849_Seed_refuses_invalid_donors_and_partial_payloads*)\|(C849_Seed_publishes_complete_payloads_before_its_marker*)\|(C849_Closure_preflight_names_a_missing_dependency*)\|(C983_Required_jq_driver_rejects_successful_false_probe*)\|(C973_Cold_marker_readers_accept_pruned_seed_image_and_refuse_invalid_markers*)` | V-7, V-8 | 11 passed, 0 skipped; C973/C983 durations and child ownership recorded | 11 | 12 | 12 | true |
| CP-22 | S5 | `tests/Antiphon.Tests -> bin-c1210-w-s5/` | win-timeout-policy | `/*/*/ProcessSpawnTimeoutPolicyTests/*` | V-9 | exactly 2 passed | 2 | 8 | 8 | false |
| CP-23 | S6 | `tests/Antiphon.Tests -> bin-c1210-w-s6/` | win-remote-class | `/*/*/RemoteScriptContractTests/*` | V-10 | >= 166 executed, 0 failed; TRX written; skips only the 12 C1050 host identities; per-method durations summarised in the report | 166 | 30 | 30 | true |
| CP-24 | S6 | CP-23 | win-argv-roundtrip | `/*/*/DelegateLaunchArgvIntegrityTests/*` | R-3 | all 7 passed, 0 skipped | 7 | 2 | 2 | false |
| CP-25 | S6 | n/a | win-partition-import | `dotnet run --project tools/Antiphon.Checkpoints --no-build --property:OutputPath=bin-c1210-w-tool/ -- import --plan docs/superpowers/plans/2026-10-13-card-1210-windows-unit-partition.md` | V-10 | exit 0, zero refusals, every row names an explicit class or method group | n/a | 2 | 2 | false |

Build and run through the checkpoint tool (`run --plan <this file> --after S1 --expected-source-sha <full committed SHA> --serial --max-wait 55s`, then `wait` until the exit is not 75), one run per committed slice group per lane. The tool bootstrap is one unlisted, build-slot-gated isolated build (`bin-c1210-<lane>-tool/`, `UseAppHost=false` on Linux only); every `bin-c1210-*` directory is removed before the task ends.

### Cost

Linux lane rows sum to 65 estimated minutes; Windows lane rows to 103. Per slice (Linux + Windows): S1 25 + 17, S2 17 + 24, S3 7 + 8, S4 9 + 12, S5 7 + 8, S6 0 + 34. Each Code dispatch's `-ExpectAbout` is its two lane sums plus 30-60 minutes of authoring; S6 is docs plus the measurement run.

--- next stage ---
next: test-design
handoff: Audit D-1..D-12 and A-1 (class-level TUnit Timeout), fix the exact method names and counts for the roster, the LF contract, the split storage methods and the C849 closure, confirm the third C849 method's CP-12 failure text, then release S1/S2/S3 Code in parallel and hold S4/S5 behind CARD-1208.
artifact: docs/superpowers/plans/2026-10-13-card-1210-windows-unit-lane-plan.md
