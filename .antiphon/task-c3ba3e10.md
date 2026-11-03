# Review c3ba3e10 — CARD-1675 Grok store probe ancestor walk (round 1 of 3, full)

Subject: origin/feat/card-task-80170e29 tip c52a96a026bf321c7a3866cb442297be54b17fb9
(base master bcfe77b3d685301030ed1a84bc4ccfdb7a6190a6; red witnesses 45fe7ed3b, fix c52a96a02).
Outcome: defects found (D-1 Windows test portability, D-2 Windows dangling directory link reads Missing). Linux behaviour is sound.

## Runs (server2 Linux, one build-slot lease per build/run, native output, UseAppHost=false)

| Run | Tree | Result |
|---|---|---|
| GrokNativeSessionStoreTests `/*/*/(GrokNativeSessionStoreTests*)/*` | c52a96a02 | 12 total, 12 passed, 0 failed, 0 skipped |
| HerdrGrokResumeGuardTests `/*/*/(HerdrGrokResumeGuardTests*)/*` | c52a96a02 | 9 total, 9 passed, 0 failed, 0 skipped |
| M1: ancestor walk removed (`when (!File.Exists(root))`), store class | scratch | RED 6/12: every home- and parent-level case (file, file-link, dangling-link) |
| M1, guard class | scratch | RED 2/9: A_file_at_or_above_grok_home...(home), (parent) (no IOException thrown) |
| M2: `if (!File.Exists(current))`, store class | scratch | RED 2/12: Probe_reports_missing_for_a_missing_chain_of_directories, Probe_distinguishes_missing_and_found_storage |

Existing behaviour unchanged: Probe_distinguishes_missing_and_found_storage, Probe_reports_unavailable_for_non_directory_storage (CARD-1669) and Guid_resume_with_no_directory_throws_the_named_code green.

No plan file / ### Checkpoints table exists for CARD-1675; the brief names the selection (two classes + two mutants) and that whole selection ran. No checkpoint-tool receipts, so reviewedSourceClean=false (TRX are in my scratchpad only).

check-evidence-diff (-Repository . -BaseRef bcfe77b3d -HeadRef c52a96a02): commits=2 entries=0 violations=0, exit 0.

Merge: origin/master a06d64f71 merge-tree clean. rc/2026-11-03 919d8d42f: merge-tree clean and per-commit cherry-pick simulation of 45fe7ed3b+c52a96a02 clean. rc/2026-11-01 10a826b9d: c52a96a02 CONFLICTS in GrokNativeSessionStore.cs because rc/2026-11-01 lacks CARD-1669 e72c27bdb (the `when (!File.Exists(root))` filter); port there needs CARD-1669 first.

## Linux edge probe (scratch-only probe test calling Probe directly, uid 1654)

| Input | Result | Expected |
|---|---|---|
| link loop at GROK_HOME / loop as parent | Unavailable / Unavailable | ok |
| dangling directory link at GROK_HOME | Unavailable | ok on Linux (see D-2 for Windows) |
| directory link to an existing empty dir | Missing | ok (fresh start) |
| chain of missing dirs under existing dir | Missing | ok |
| "/" | Missing | ok |
| "" and "   " | Missing (relative "sessions", no throw) | ok; never reached: ResolveGrokHome falls back to ~/.grok for blank |
| file + trailing separator | Unavailable | ok |
| 5,500-char missing path | Unavailable (PathTooLong) | safe direction |
| ancestor without search permission (missing home / existing dir) | Unavailable / Unavailable | ok |
| sessions dir execute-only | Unavailable | ok |
| path with NUL | THROWS ArgumentException out of Probe | pre-existing, see A-2 |

Walk terminates on string parent (Path.GetDirectoryName), so link loops, roots and drive roots end.

## Defects

### D-1 Link witnesses fail, not skip, on a Windows host without symlink privilege
- Where: tests/Antiphon.Agents.Pty.Tests/GrokNativeSessionStoreTests.cs:66-72 (`File.CreateSymbolicLink` for the file-link and dangling-link cases, 6 of the 9 new cases).
- Failure: on a Windows account without SeCreateSymbolicLinkPrivilege/Developer Mode, File.CreateSymbolicLink throws IOException HResult 0x80070522 (ERROR_PRIVILEGE_NOT_HELD, 1314); the 6 cases go red in the Windows rc qualification (these tests reach the next candidate and run natively there).
- Why: the repo's established idiom is a privilege-classified skip: CARD-1028 `TestOwnedPtyHostIdentityTests` (catch IOException/UnauthorizedAccessException with HResult 0x80070522 on Windows only, `Skip.Test(<named reason>)`, plus a predicate self-check) and CARD-1210 `tests/Antiphon.Tests/TestHelpers/FileLink.cs` (`TryCreate` returns null on 0x80070522/1314, verifies a reparse point); CARD-0876 was filed for exactly this failure in ChannelOutboundStorageTests. The new comment copies the older CardFilePrivacyGitTests "explicit environmental failure" wording, which the later CARD-1028/1210 work superseded for this class of host.
- Fix: in Antiphon.Agents.Pty.Tests, guard link creation with the CARD-1028/CARD-1210 classification (Windows-only, exact HResult 0x80070522 / NativeErrorCode 1314) and `Skip.Test` with a named CARD-1675 reason; keep Linux errors propagating. The plain-file cases stay unconditional. Record the conditional skip for checkpoint receipts.

### D-2 (Windows, source-reasoned, PLAUSIBLE) A dangling directory link or junction at or above GROK_HOME reads Missing
- Where: src/Antiphon.Agents.Pty/GrokNativeSessionStore.cs:39-49 (`FirstExistingAncestorIsNonDirectory`).
- Failure: Windows, GROK_HOME (or {GROK_HOME}/sessions, or an ancestor) is a directory symlink or junction whose target is absent (drive letter gone, target folder deleted). Enumeration throws DirectoryNotFoundException (the Windows enumerator maps FILE_NOT_FOUND to PATH_NOT_FOUND). In the walk, File.Exists(link) is false and Directory.Exists(link) is TRUE on Windows (GetFileAttributesEx reports the link itself with FILE_ATTRIBUTE_DIRECTORY; dotnet/runtime's own `SymLinksMayExistIndependentlyOfTarget` test asserts Directory.Exists true / File.Exists false for a broken directory link on Windows, the reverse on Unix). Result: Missing, so HerdrGrokResumeGuard reports "native history is missing" and offers a fresh start for storage that is unreachable. Linux reads Unavailable for the same shape (probe row "dangling-dir-link-home"). Junctions need no privilege, so this is reachable on any Windows host.
- Why: the card's own contract is "a dangling link there = Unavailable", and the brief lists "a link to a directory that is missing" under must-never-read-Missing. Not a regression (CARD-1669's `!File.Exists(root)` was also Missing here) but it is the unwitnessed Windows half of what this card claims. Brief's suspicion about dangling FILE links on Windows does not hold: File.Exists is true for a broken file symlink on Windows too (same runtime test), so those test expectations are correct on a privileged Windows host.
- Fix: in the walk, when `Directory.Exists(current)` is true, check whether it is a link whose final target is absent (`Directory.ResolveLinkTarget(current, returnFinalTarget: true)` non-null with `!Exists`, or ReparsePoint attribute plus a failed target check) and return true (Unavailable). Add a Windows witness with a junction to a missing directory (no privilege needed; Antiphon.Tests has DirectoryLinkHelper's junction fallback) and the Linux twin with Directory.CreateSymbolicLink. Fix the source comment (line 38): File.Exists is true for a dangling FILE link on both OSes; the OS split is dangling DIRECTORY links.

## Advisories
- A-1: on Windows a GROK_HOME on a drive that does not exist (Q:\grok) walks to Q:\, finds nothing and reads Missing (pre-existing design "nothing existing = Missing"). A path with no existing ancestor at all cannot be a fresh-start location; consider Unavailable when the walk reaches a root without finding a directory.
- A-2: a NUL in GROK_HOME (reachable only through RunnerLaunchRequest.Env JSON; real env vars cannot hold NUL) throws ArgumentException out of Probe. Safe direction (launch fails, never Missing), pre-existing.
- A-3: rc/2026-11-01 port needs CARD-1669 e72c27bdb first.

## Producer/recipient
Only caller of Probe is HerdrGrokResumeGuard.Require (SessionRunnerRuntime.cs:619, HerdrPaneChild.cs:309); Unavailable maps to IOException "...unavailable; resume identity is preserved", Missing to HerdrLaunchException GrokNativeSessionMissing. The guard witnesses exercise that real path end to end. No queue/transcript/input-wait surface changed (no parking/release-deadline change).
