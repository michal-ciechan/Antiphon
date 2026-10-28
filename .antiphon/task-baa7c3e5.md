# Final Review 2 baa7c3e5: CARD-1596 legacy tool copies; sweep reverted (subject 0fb26592)

I reviewed `feat/card-task-0fb26592` at 46e1ea5486165c903dd744febfe3c50f37d5df28 against rc/2026-10-27 base f84228f285dd3e4ad7b5dc8260cbc9f819256f50 on server2/Linux. I changed no source. I restored every mutation, and `git status` is clean at the tip.

**Verdict: no regression and no new fail-open. Next stage: land.**

## Runs

Every run went through scripts/build-slot.ps1, and each slot was granted. Settings were OutputPath=bin-rvbaa/ and UseAppHost=false, and each build was followed by `dotnet exec` with an exact class filter.

| Run | Source | Selection | Result |
|---|---|---|---|
| build1 + run1 | clean tip 46e1ea54 | the 9 named classes | total 98, passed 86, failed 0, skipped 12. Every skip is a Windows-native row: 7 in CheckpointWindowsAnchoredDeleteTests and 5 in CheckpointWindowsAnchoredPathDeleteWitnessTests. Every class ran at least once (11/28/7/5/5/9/17/13/3). This matches the author's counts. |
| build2 + run2 | tip with 4 independent reverts (see below) | AnchoredPathDeleteWitnessTests and C1307ToolRemovalTests | total 20, failed 4. Each failure is the expected assertion; none is a build or fixture error. |

The four reverts in build2 were applied in different methods. Each one, and the witness that went red:

- **Legacy handling:** `LegacyUnbound => false`. `Legacy_unbound_record_is_removed_by_the_base_path_delete` failed on exit 2 instead of 0, with `tool-removal-incomplete: tool-authority-entry-mismatch`. That is the D-1 shape exactly.
- **Pre-marker admission:** `AdmitRemoval` returns null after `Check`. `Bound_identity_mismatch_is_refused_before_any_marker` failed with "tool-removal-incomplete: IOException: anchored-identity-mismatch; tool may be partially removed".
- **Token matching:** `HasExactCode` changed to a substring match. `directory_not_empty_matches_win32_145_exactly` failed because `win32=1450` should throw but did not.
- **DeleteCreatedFile:** `DeleteAnchored` changed back to `Io.Unlink`. `swapped_parent_cannot_redirect_created_file_delete` failed because the foreign `outside/payload.txt` was deleted.

Other checks:

- **Evidence diff:** `check-evidence-diff.ps1 -Repository . -BaseRef f84228f2 -HeadRef 46e1ea54` gave commits=4 entries=0 violations=0, exit 0.
- **Cleanup:** I deleted all 33 `bin-rvbaa` directories after confirming that each one resolved inside the worktree. `.antiphon` was pruned from the search.
- **Lane coverage:** following the brief, I did not run the whole Unit lane.
- **Receipts:** I did not use the checkpoint tool, so there are no SHA-validated receipts and `reviewedSourceClean` is false.
- **PCs:** they remain pending.

## Priority findings

1. **Regression check against base.**
   - **Temp-root sweep:** `git diff f84228f2 46e1ea54 -- CheckpointTempRootSweep.cs` contains only a 3-line comment. The sweep is byte-identical in behaviour, and the D-2 and D-3 regressions are gone. The FIFO witness `fifo_or_socket_root_is_reclaimed` is green.
   - **Legacy tool copies:** `RemoveLegacy` is token-identical to the base `Remove` body: the same preflight, `Matches`/`SameDirectory`, then `Unlink`/`RemoveDirectory`. `AdmitRemoval` returns null for a legacy record once it passes the same `Check()` that the base `Remove` ran. The marker is written exactly as at base, and the Wait exit is Green (witnessed through `WaitCommand`, and a second Wait is green with no previous-removal).
     - The only difference is the receipt reason, `identity-dead; legacy-unbound-record`. No code consumes `CleanupReceipt.Reason` for "identity-dead" (grep across src/, tools/ and tests/).
     - The witness strips `rootIdentity` and `entries[].identity` and re-digests. That is the exact JSON an older build wrote, because the old `CopyRecord` lacked those properties and serialization is camelCase.
   - **Rollback:** an older build reading a bound record ignores the unknown members (`CheckpointReceiptJson.Parse` sets no `UnmappedMemberHandling`).
   - **Bound copies:** a mismatch on a bound copy is refused before the marker, so no refusal state is permanent. A partially bound record cannot come from any Seal, because Seal throws when an entry is unbound, and such a record is refused before the marker.
2. **DeleteCreatedFile.** No legitimate case is newly refused.
   - `MatchesCreation` already required a bound, matching identity at base (4b10ed879 is an ancestor of the base), so directories, FIFOs and sockets were already refused there.
   - Read-only files: `unlinkat` ignores the mode on Linux, and on Windows both the base and the new delete refuse.
   - On Linux, ':' is still valid (`linux_colon_in_a_file_name_stays_deletable` is green).
   - The callers, the `TestRootGuard` temporary and `EvidenceFolder` `failures.md`, delete only regular files this process wrote.
3. **A non-regular created file is left in place.** This is unchanged from base. Base refused the same case through `MatchesCreation`, so the change adds no residue for the CARD-1590 recycle audit.
4. **Whole-token match for win32 145 and errno 39.** The match runs only on `DeleteAnchored`'s own messages: `anchored-unlink-failed: errno=<n>` on Linux and `win32=<n>` from `SetFileInformationByHandle` on Windows, where ERROR_DIR_NOT_EMPTY is 145. It is not too strict, because the real native formats match exactly. It is not too loose, because 1450 and 390 still throw. The case-insensitive text "directory not empty" also covers .NET's Linux message.
5. **Red witnesses:** all four reverts went red, as listed under Runs.

## Advisories (none blocking)

- **A-1:** `Bound_parent_swap_is_refused_before_any_marker` cannot go red from removing the pre-marker admission. The `Check()` preflight link refusal fires first, and did so at base too. It pins behaviour rather than witnessing the admission. `Bound_identity_mismatch_is_refused_before_any_marker` is the real witness.
- **A-2:** the Windows legacy FileDispositionInfo (class 4) versus POSIX-semantics delete, carried from 6e90a63b A-2. It applies only to bound tool copies, the ledger and DeleteCreatedFile on Windows. It remains unverified here and needs the desktop lane.
- **A-3:** `DeleteCreatedFile` now throws `anchored-entry-stat-failed: errno=2` where base `File.Delete` was silent. This happens only if the file vanishes between `MatchesCreation` and the delete, a narrow race on a file this process owns. In `TestRootGuard` that throw happens inside a `finally`, so it could mask the original exception. On platform "Other" (macOS), the delete throws `anchored-delete-unavailable`, as the ledger already did at base.
- **A-4:** these residuals were stated by the author and are confirmed:
  - a same-inode ancestor alias remains possible on the converted sites;
  - a zero birth time matches on device and inode alone;
  - the sweep is not ancestor-safe, which is the base behaviour.

Input waits: this change adds or alters none.
