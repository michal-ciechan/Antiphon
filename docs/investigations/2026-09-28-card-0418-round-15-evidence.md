# CARD-0418 round 15: Windows CP-9 and Windows Final sweep

This continues the [round-14 ledger](2026-09-28-card-0418-round-14-evidence.md). This round ran on the Windows desktop host (`DESKTOP-KTLKPIF`, Windows 10.0.19045, 8 cores) so that CP-9's two browser-descendant tests could run on Windows process trees. No live destination, shared stack, restart, or land was used.

## Checkpoint tool crash on Windows (fixed, `07f9dbf7`)

The first three CP-9 runs (`20260928-194741-8992`, `-194850-8aa9`, `-194919-ef9f`) ended at exit 6 with `scheduler crashed` before any build ran. A local, uncommitted diagnostic exposed the inner exception: `BuildSlotClient.ReadInt` called `JsonElement.TryGetInt32` on `"renewEverySeconds": null`. The desktop runner's pid-liveness broker (`BuildSlotBroker`: `RenewEverySeconds` only when `HolderLiveness == "renew"`) returns that null on every grant, and `TryGetInt32` throws on a Null element. Every checkpoint-tool run against a pid-liveness broker therefore crashed at its first slot grant. The tool is identical on `origin/master`, so the defect is not specific to this branch. `ReadInt` now reads only Number elements. `BuildSlotClientTests.pid_liveness_grant_with_null_renewal_is_granted_without_renewing` failed with the same `InvalidOperationException` against the old line (`.antiphon/c0418-r15-slotnull/red`) and passed after it was restored, with the class at 11/11 (`.antiphon/c0418-r15-slotnull/green`). These were unlisted runs: they are the red/green proof for the new test. CP-1 covers the test on the swept tip.

## CP-9 Windows process-tree evidence (V-19, R-12)

CP-9 ran through the checkpoint tool on Windows:

| Run | Commit | Executed | Passed | Failed | Skipped |
|---|---|---:|---:|---:|---:|
| `20260928-195602-41af` | `07f9dbf7` | 19 | 19 | 0 | 0 |
| `20260928-195852-b2d9` | `2a32ff8e` | 19 | 19 | 0 | 0 |

In the TRX, `A_real_browser_timeout_stops_its_child_process` ran for 1.08 s (1 s render timeout) and `Caller_cancellation_stops_the_real_browser_process_tree` for 0.42 s. Both launched `fake-browser/Antiphon.MarkdownPdf.FakeBrowser.exe`, read the parent and descendant PID files it wrote, and asserted that both identities exited after the timeout or cancellation and that no PDF was claimed. This is the Windows process-tree evidence that rounds 10–14 deferred.

V-19 also requires "temp HTML cleaned". No test asserted that. From `2a32ff8e`, the fake browser records its argument list. Both tests assert that the renderer's staged `file://` HTML exists while the browser runs and is gone after the timeout or cancellation. With the renderer's `finally { File.Delete(htmlPath) }` removed locally, both tests failed at the post-exit `File.Exists(stagedHtml).ShouldBeFalse()` (line 253; `.antiphon/c0418-r15-v19/red`). The renderer was restored and CP-9 above is green on that tip. The renderer launches no separate profile directory, so there is no profile to clean. On Windows, the Linux-only renderer rows (`Real_browser_exit_zero_requires_a_fresh_nonempty_pdf`, `Browser_nonzero_exit_cannot_reuse_a_stale_pdf`, `Symlinked_staged_document_is_refused_before_browser_launch`) return early and pass vacuously. Their evidence remains the Linux CP-9 in round 14.

## Windows portability of branch tests (`841b513a`, `285d09fb`)

The first Windows Final sweep, `20260928-200145-0d8e` at `2a32ff8e`, found four red rows caused by this branch's own tests, plus CP-8 inherited and CP-10 platform-skipped:

- CP-1 and CP-4: `OutboundConversionManifestTests.Invalid_output_is_rejected_with_a_valid_adjacent_control(linked_file|linked_directory)` hit `IOException: A required privilege is not held by the client` (ERROR_PRIVILEGE_NOT_HELD). An unprivileged Windows user cannot create symlinks. `linked_directory` now falls back to an `mklink /J` junction (the method carries `ParallelLimiter<ProcessSpawnLimit>`). The junction is removed non-recursively before the root is deleted, as in `AgentTaskLandActiveSourceClaimTests`. With the production linked-directory `ReparsePoint` guard disabled locally, only that row failed (`Task invalid should throw`; `.antiphon/c0418-r15-junction-red`). `linked_file` has no unprivileged Windows equivalent, so on that privilege error it skips and names the Linux lane.
- CP-2: `DeliverableBundleServiceTests.A_plan_role_reads_a_branch_only_path_via_git` expected CRLF bytes, but this host's system `core.autocrlf=true` normalised the committed blob to LF. The fixture repository now pins `core.autocrlf=false`, and the byte-exact assertion is unchanged.
- CP-3: `ChannelOutboundEndpointTests.Legacy_renderer_keys_warn_once_on_fresh_host_without_browser` read the live host's Serilog file with a non-sharing `File.ReadAllLines`, which Windows refuses while the sink holds the file. It now reads with `FileShare.ReadWrite | FileShare.Delete`. The once-only count is unchanged.

A targeted Windows run of the two affected classes gave 36 passed, 0 failed, 1 skipped (`linked_file`). The CRLF method passed 1/1 (`.antiphon/c0418-r15-winfix`). Both were unlisted runs that checked the repairs before the sweep. No assertion was loosened and no timeout widened.

## CP-1 through CP-13 Final sweep (Windows)

The checkpoint tool ran the plan's closed list once, on committed `285d09fbe5396adf71a8a646df1c7a81da5bdc0c`, in `.antiphon/checkpoints/20260928-204420-f0f3/` (wall 31m02s, one row at a time on Windows). Every row held a granted slot. The tool ran no unlisted command. The red predecessor sweep `20260928-200145-0d8e` at `2a32ff8e` is the source of the portability findings above.

| Row | Executed | Passed | Failed | Skipped | Verdict |
|---|---:|---:|---:|---:|---|
| CP-1 Unit | 3514 | 3514 | 0 | 3 | Green. Windows skips: `Restored_key_file_symlink_is_rejected_without_mutating_target`, `linked_file`, `executor_survives_its_starter` |
| CP-2 source settlement | 340 | 340 | 0 | 0 | Green |
| CP-3 policy/schema | 30 | 30 | 0 | 0 | Green |
| CP-4 file boundary | 56 | 56 | 0 | 1 | Green; `linked_file` skipped on Windows, `linked_directory` ran as a junction |
| CP-5 purpose/deadline | 6 | 6 | 0 | 0 | Green |
| CP-6 crash/transport | 16 | 16 | 0 | 0 | Green |
| CP-7 routing/attention | 320 | 320 | 0 | 0 | Green |
| CP-8 existing deadlines | 71 | 69 | 2 | 0 | Red: inherited `PinnedAgentKindTests.T1`/`T2` `codex_desktop_unqualified`, the same failures as rounds 11–14. Not a branch regression, and not green. |
| CP-9 renderer | 19 | 19 | 0 | 0 | Green on **Windows**, including both browser-descendant tests |
| CP-10 real browser | 0 | 0 | 0 | 1 | Not run: the Docker browser fixture exists only on the Linux lane (`SkipTestException`), so `minExecuted=1` fails. Round 14's Linux run passed 1/1 at `d179d2f0`. Only tests changed in `tools/Antiphon.MarkdownPdf*` since then, but this tip still needs a Linux CP-10 run. |
| CP-11 gateway wire | 122 | 122 | 0 | 0 | Green (`ANTIPHON_BROKER_TESTS=1`, local Docker) |
| CP-12 client | 26 | 26 | 0 | n/a | Green; `CLIENT TESTS EXIT CODE: 0` |
| CP-13 client bundle | n/a | n/a | 0 | n/a | Build exit 0 |

Overall: eleven green rows, CP-8 red (inherited), CP-10 not executed on this platform, tool exit 1. Outside the closed list, this round ran: the three crashed CP-9 attempts and one diagnostic attempt; the red/green proofs for the slot-client test, the staged-HTML cleanup assertion and the junction row; the targeted portability check; and `npm ci` for client dependencies. All builds and tests held granted slots through the tool or `scripts/build-slot.ps1`.

## Remaining gates

This round closes the Windows browser-descendant part of V-19/R-12 and V-19's temp-HTML cleanup assertion. The ordinary V/R matrix otherwise stays as the [round-8](2026-09-28-card-0418-round-8-evidence.md#remaining-ordinary-scope), [round-13](2026-09-28-card-0418-round-13-evidence.md#remaining-ordinary-gates) and [round-14](2026-09-28-card-0418-round-14-evidence.md#remaining-gates) ledgers list it. Still open: full source-provenance/read-path, completion-note, send-shape/control, worker-purpose/refusal/deadline, file/ZIP/fault, and the complete routing/crash/multi-target/TTL/policy-race, monitor and near-default wire oracles. The R-14 validator (`ChannelOutboundEvidenceAccounting`) exists but has not been run over a committed evidence index. Running it honestly needs per-ID evidence for that open matrix, and indexing only the currently green names would overclaim. CP-10 needs a Linux run on this tip. V-25 is the later authorized live gate. PC-1–PC-30 remain pending method-scoped SourceLanding Mutation; no checkpoint discharges any of them. No land, restart or live message was performed.
