# Backlog refill triage (2026-11-17)

Read-only. No card was edited, moved, archived, or commented. Board Antiphon `8988ca03-7414-47ad-b0b6-51556c701703`.

Master read: `870a93d21f2b18c85a8b0905ab689b28e3099813` (`test: regenerate impact index on the batch 253 tree (CARD-1893 collector + CARD-1956 resume)`). `git fetch origin master` left `origin/master` at that SHA. This branch was already there.

## Enumeration

`pwsh -NoProfile -File scripts/card.ps1 list -Board Antiphon -Status Backlog -All -Json` returned one complete payload: `total=314`, `truncated=false`, `nextPageToken=null`, 314 card objects, all `status=Backlog`. Stderr was empty (no continuing-page line).

| Slice | Count |
|---|---|
| Listed with `-All` | 314 |
| `archivedAt` set | 12 |
| Live (no `archivedAt`) | 302 |
| Live importance High | 10 |
| Live importance Normal | 261 |
| Live importance Low | 31 |
| Stored urgency Soon, including archived | 21 |
| Live stored urgency Soon | 20 |
| Stored urgency Now, live or archived | 0 |
| Live effective urgency Now | 0 |

The 12 archived rows are CARD-1712, CARD-1628, CARD-1602, CARD-1595, CARD-0926, CARD-0925, CARD-0429, CARD-0563 (the only archived Low), CARD-0480 (the only archived Soon), CARD-0359, CARD-0279, CARD-0219. The brief's 315-by-status / 303-live figure is one card higher than this list.

Scope A is the live union of importance High or stored urgency Soon or Now: **21 cards**. That is 9 High/Soon, 1 High/Normal (CARD-1891), and 11 Normal/Soon. The only effective-Now row in the payload is archived CARD-0429.

Scope B is the other **281** live cards (Normal/Normal or Low). List JSON caps `description` at 200 characters (`hasMore` on 310 of 314). Scope B prefixes below are the first 400 characters from `GET /api/cards/{id}` (same card body as `card.ps1 get`), not the private-notes route. 277 of 281 were longer than 400 characters; 4 descriptions were shorter and were read whole.

## What was read

- In full (`card.ps1 get -Board Antiphon -Json` plus `card.ps1 history`): the 21 scope A cards. History was an empty body for the 20 whose list `revisionCount` was 0. CARD-1874 returned one `ContentEdit` (2026-11-13, reason "Record terminal green receipt and exact finalization cost").
- Relationships `GET /api/cards/{id}/relationships` for those 21: every `blockedBy` and `blocks` list was empty. No scope A card has a relationship blocker.
- By title plus the first 400 characters of description: the 281 scope B cards.
- Status and title only, via `card.ps1 get`, for related cards named below (CARD-1614, CARD-1820, CARD-1836, CARD-1850, CARD-1871, CARD-1882, CARD-1883, CARD-1892, CARD-1899, CARD-1911, CARD-1912, CARD-1926, CARD-1942, CARD-1432, and the closed parents CARD-0883, CARD-0888, CARD-0939, CARD-1265, CARD-1403, CARD-1544, CARD-1609, CARD-1619, CARD-1630, CARD-1677, CARD-1853, CARD-1862). Descriptions of CARD-1614, CARD-1899, CARD-1942, and CARD-1588 were read far enough to check overlap, not as a second full-board pass.

`GET /api/agent-tasks/pipeline?projectId=d4ea7ae9-e769-474b-95b9-aa25fbc1303f` at `2026-11-17T00:57:46.9737257Z` reported `investigateBacklog.total=298`. That is the pipeline's fresh-candidate count, not this card list. Server2 was `inFlight=7`, `effectiveLimit=10`, `remaining=3`.

## In flight, and the files those branches actually change

Diffs are `git diff --stat origin/master...<tip>`.

| Seat | Card | Role | Branch tip | Files vs master |
|---|---|---|---|---|
| Code | CARD-1957 | Code | `1b59dd109dd37c525d902b55ed3fbed1ef647637` | `docs/docker-stack.md`, `scripts/c1609-host-proof.ps1`, `scripts/c590-real.ps1`, `scripts/deploy-server2.ps1`, `tests/Antiphon.Tests/Scripts/VolumeRetirementHostProofTests.cs` |
| Code | CARD-1958 | Code | `d958d5b7d4f7a730fc8925f37cca92573d9b7f42` | checkpoint receipt scripts, `WindowsUnitPlatformAdmissionTests.cs`, `WindowsUnitSkipRoster.cs`, `tests/derived-impact-index.json` |
| Review | CARD-1514 | Review | `12105bfbc2e0f472614e51a96adc4240b2384a32` | `docs/docker-stack.md` only |
| Review | CARD-1882 | Review | `338e00cb1b56b8075b8b6d1cc91e17b3e738a4d6` | plan amendment markdown only |
| Review | CARD-1893 | Review | `5c1a0d5023b9aada4b62c1ff85d4be42e5b72bbe` | activation plan markdown only |
| Merge | CARD-1836 | Merge | `321f98ec990a575f77e442ba3b1554f0fde33521` | no files different from master |
| Investigate | CARD-1514 | Investigate | this task | triage doc only |

Held, whether or not the current tip already edits them: `docs/docker-stack.md`, `scripts/c1609-host-proof.ps1`, and `scripts/c590-real.ps1` (CARD-1957, and docker-stack also CARD-1514). Compose files and `scripts/deploy-server2.ps1` belong to the CARD-1893 activation; the Code tip of CARD-1957 also edits `deploy-server2.ps1`. The selector/index chain (`scripts/impacted-classes.ps1`, `scripts/lib/release-impact*`, `tests/impacted-classes.json`, `tests/derived-impact-index.json`) is one stage at a time. CARD-1958 is the stage on it now: it edits the derived index and the C1210 roster files.

## Ranked refill

Dispatch from the top. One custody Debug at a time. Do not put a second Code seat on transcript ingest, the selector/index chain, docker-stack, or `deploy-server2.ps1`.

| Rank | Card | Role | Kind | Min | Weight | Collision area | Why now |
|---|---|---|---|---|---|---|---|
| 1 | CARD-1930 | Debug | Opus | 60 | Heavy | `tests/Antiphon.PtyHost.Tests/LinuxCgroupCustodyTests.cs`. Same file as CARD-1929, CARD-1931, CARD-1932. CARD-1614 is Review and pipeline-ready; its text names older custody failures (Draining vs Exited, daemon.pid, tracked pid), not this method. Search for `C1614_Missing_tree_never_finalizes_naturally` returned only this card. Not on the 1957/1958 diffs. | High/Soon. Measured red: fixture `Directory.Delete` gets Access denied on the root-owned cgroup tree before the missing-tree case runs. Clearest of the four. |
| 2 | CARD-1929 | Debug | Opus | 60 | Heavy | Same test file. Search for `Lost_host_leaves_unknown_not_receipt` returned only this card. | High/Soon. Fixture cleanup re-reads an invalidated store binding after the body already asserted the mismatch. Dispatch after CARD-1930, not beside it. |
| 3 | CARD-1888 | Code | Opus | 60 | Heavy | Transcript ingest (`TranscriptIngestPrelude`, identity `ANY` probe, whole-session reads). No in-flight branch touches those files. | Normal/Soon. Measured: about 1,068,019 identity probes in 44 h, about 602 rows per call, 0.25 core. Dedup for replayed batches (CARD-1403, Done) has to keep holding. |
| 4 | CARD-1878 | Code | Grok | 45 | Light | `scripts/test-rc-boundary.ps1` and the boundary id library. CARD-1836 is the epic and is InProgress, but its merge tip has no unique files against master. CARD-1882 and CARD-1893 tips are plan markdown. | Normal/Soon. Parent CARD-1871 is Done. Advisories do not hold a land. AD-5 (native Windows PowerShell 5.1) stays owed and is not this Linux brief. |
| 5 | CARD-1896 | Code | Opus | 60 | Heavy | Runner `/tmp` sweep (CARD-1677, Done). CARD-1942 is Review and owns `C1677TempSweepDispatchTests.cs` (the witness still treats Input as the mutation-lock owner). Do not edit that test. CARD-1957 does not edit the sweep. | Normal/Soon. Specified from the 2026-11-13 disk incident: one row per unregistered prefix, fair by-bytes order, register review/master scratch clones. Preview stays default. |
| 6 | CARD-1889 | Code | Grok | 60 | Heavy | Host-load history bindings, host-stats process list, `LIVE_SLOW_CALL` tags. No in-flight overlap. Desktop `HostLoadHistory:Bindings` / `PhysicalHostKey` is operator configuration, not this code brief. | Normal/Soon. Measured gap: desktop history `binding-unknown`, host-stats attributed 0.55 cores at 99% CPU. |
| 7 | CARD-1931 | Debug | Opus | 60 | Heavy | Same custody test file. Search for `C1614_Natural_descendant_exit_finalizes_without_release` returned only this card. | High/Soon, but attribution is still open (no receipt before 20 s, including the once-alone retry). Dispatch after the two fixture Debugs. |
| 8 | CARD-1948 | Code | Grok | 45 | Light | Client attention and queue counts. No in-flight branch edits client files. | Normal. Server census is right; the client follows the loaded page. Not in scope A. |
| 9 | CARD-1920 | Code | Grok | 45 | Heavy | `server/Infrastructure/Data/ApplicationPostgresConnection.cs` (`SuppressJit` / `AssignsJit`). The scanner is still there. Not on an in-flight diff. | Normal. Operator disposition on CARD-1886 S1 was to land and card this. Clustered short switches leave jit on. Not in scope A. |
| 10 | CARD-1890 | Code | Grok | 60 | Heavy | Server Information logging (`SessionRuntimeActionCoordinator` and per-request lines). No in-flight overlap. | Normal. 36,800 lines/hour on the 2026-11-13 desktop log. Not in scope A. |
| 11 | CARD-1881 | Code | Grok | 45 | Light | `scripts/lib/rc-chunk-digest.ps1`. CARD-1836 epic, no unique files on the current merge tip. | Normal. A resolved collision id (`b-unassigned-<64 hex>`) fails membership recompute. Not a land blocker. Not in scope A. |
| 12 | CARD-1933 | Code | Opus | 60 | Heavy | `PersistTranscriptAsync` singleton inserts. Same transcript area as rank 3. One at a time. | Normal. 100 events become 100 inserts and invalidate the summary. Not in scope A. |
| 13 | CARD-1941 | Code | Grok | 60 | Heavy | Standing guard union in `docs/testing-and-build.md` (line 325 still omits `WindowsUnitPlatformAdmissionTests`), `tests/impacted-classes.json` (row `native-linux-gate-roster` watches three files, not every `NativeLinuxGate.Require`), plus the roster files CARD-1958 is editing. | High value, not this hour. The instance roster land is `50d2dfdd124ebc340cad4d5c58acd3ef8a07d7b4`. The structural gap (third occurrence after CARD-1265 and CARD-1544, both Done) is still open. Wait until CARD-1958 settles. |
| 14 | CARD-1900 | Debug | Opus | 60 | Heavy | `OutputDistillationDeliveryTests`. CARD-1899 is Review and pipeline-ready and owns that class for the 3000/14400 envelopes. | Two methods stayed unresolved at an S1 tip (`Mode_and_terminal_status_preserve_report`, `Apply_preserves_handoff_warnings_and_generated_deliverables`). They passed on master `c1fd1b7bc1eb905d2285b4258879955dda4fe96d`. Do not edit the class while CARD-1899's review branch is frozen. |
| 15 | CARD-1932 | Debug | Opus | 60 | Heavy | Same custody file. Search for `C1614_Populated_tree_refuses_entry_and_drain_success` returned only this card. | High/Soon, but the card says candidate image only and that the standing runner must not execute this witness. Not a server2 standing-seat refill. |
| 16 | CARD-1951 | Code | Opus | 60 | Heavy | Qualification profile-chunk packing. CARD-1836 is that epic. Do not change packing under a live Full run. | About 3.5 to 4 hours of a Windows Full is chunk startup. Highest-value B card that is not safe this hour. |
| 17 | CARD-1959 | Code | Grok | 45 | Heavy | CARD-1893 collector. The in-flight review tip is the activation plan only. Compose and `deploy-server2.ps1` stay with that activation. Production collector commit `40730e2ebae97b8060bece919fb7cbd441da9573` is already on master. | Path check, symlinks, witnesses. None held the collector land. Dispatch after the activation plan review, and not in those held files. |
| 18 | CARD-1945 | Code | Grok | 45 | Light | `scripts/impacted-classes.ps1` and `tests/derived-impact-index.json`. CARD-1958 holds the index. | Selector treats a string literal in the roster test as a real E2E class in Antiphon.Tests. After CARD-1958. |
| 19 | CARD-1870 | Code | Grok | 45 | Heavy | `scripts/regenerate-derived-impact-index/Antiphon.DerivedImpactIndex.csproj` linked-file list. Same index chain. | Batch 196 combined tree broke the driver. After the index stage. |
| 20 | CARD-1867 | Code | Grok | 30 | Light | `scripts/impacted-classes.ps1` Write-Host blind spot. Same selector chain. | CARD-1862 (Done) review advisories. After the index stage. |

## Parked, blocked, or not a server2 seat

No scope A card has a `blockedBy` relationship. The blocks below are the card's own text plus the parent card's column.

- **CARD-1891** (High/Normal, rank 7). Quote: "They MUST be fixed in the slice that turns coverage on (the activation gate; track with CARD-1882 algorithm digest coverage and CARD-1883 reflection forms)." CARD-1882 is InProgress (plan review in flight). CARD-1883 is Review. PC-V6 is named on the card; mutation checks are waived (operator 2026-11-12), so that line is not a refill.
- **CARD-1872** (High/Soon). Quote: "Safety items for the later activation slices; none hold the land." A-1 (PowerShell `ConvertFrom-Json` DateTime) is marked important for S5b / S6d / S7b of CARD-1836. CARD-1836 is InProgress with a Merge seat. Fold into those slices. Not a parallel Code seat.
- **CARD-1866** and **CARD-1868** (High/Soon). Both say the notes do not hold the land and assign the work to later CARD-1820 slices (S1R6 oracle, delegate edges, driver precondition). CARD-1820 is Review, Critical/Now, and Code-ready on the pipeline. Do not open a second selector Code seat.
- **CARD-1876** (High/Soon). Quote: "None hold the land; they feed S2 (the Linux census / adapter slice)." Parent CARD-1850 is Review. A1-A5 stay on that card's next slice.
- **CARD-1642** (Normal/Soon). Parents CARD-1609 is Done. The owed rows are native Windows CP-11, CP-12, CP-13, plus a delete-enabled qualification the card says must finish before apply is enabled. CP-11's class is `VolumeRetirementHostProofTests`, which CARD-1957 is editing now. Not a Linux refill, and not beside CARD-1957.
- **CARD-1643** (Normal/Soon). Parent CARD-1619 is Done. CP-2 is native Windows. CP-12 is cgroup v1, and the card says server2 is not that host. Not a server2 seat.
- **CARD-1644** (Normal/Soon). Parent CARD-1630 is Done. The owed work is a native Windows rerun. Search for `C448_V28_CancellationAwaitsOwnedGitBeforeReleasingExclusion` returned CARD-1630 (Done), this card, and CARD-0752 (Backlog). Overlap with CARD-0752 on that one method; not a duplicate to close. The card itself says a red there may be CARD-0752.
- **CARD-1950** and **CARD-1903**. Default-on for the Linux compiler policy is gated on the 24-hour server2 trial. CARD-1903 lists advisories that must land before any default-on slice. Operator call, not an automatic seat.
- **CARD-1944**. The card labels itself a product question, not a defect fix.
- **CARD-1895**. Operator decision 2026-11-16: lowest priority. Not a refill.
- **CARD-1952**. The advisory is in `scripts/deploy-server2.ps1` phase `deploy-temp`. That file is on the CARD-1957 tip and is held for the CARD-1893 activation.

## Already done, with master evidence

- **CARD-1913** can be closed. The two measured sites are hoisted on master, and the guard the card asked for is the server scan. `3cdd5a4c62c417cb2cb481d0cad09dfdc085971c` (`fix(card-1912): stabilize host-load identity SQL and hoist EF clocks`) reads the clock into a local before the hot `ExecuteUpdate` paths, including `AgentTaskDispatcher.DispatchCoreAsync` (`var expiredAt = UtcNow()` at line 543) and `RecoverDeliveryFailureNotificationsAsync` (`var now = UtcNow()` at line 5292). `f658c8bd6375221fd1174a2cdd12d93a9fc20484` adds `EfLambdaClockGuardTests`, whose header names CARD-1913, and `Server_ef_lambdas_do_not_call_the_clock` fails if an EF lambda still calls the instance clock. Both SHAs are ancestors of `origin/master`. This triage did not re-run the test. Residual witness gaps are a different card, CARD-1946 (Low, "misses fifteen of the eighteen audited hoists"), and are not a reason to keep CARD-1913 open. CARD-1912 itself stays Review.
- **CARD-1588** can be closed. The card names hotfix commits `3aac0cdd4`, `98aa524aa`, and `f06b5f40d` and says they still need review and land. Those three are not ancestors of master. The same three patches are on `origin/master` under different SHAs (stable patch-id match): `eb284614b92204eefcbdea93aa28ac396f6524fc`, `4c4ad5126fe576320661517dbb785b65bafb470b`, and `1f650acc5b1cd355dae9aaf2c28de699d9cb101b` (all 2026-10-31). Search for `Claude 2.1.310 composer hint` returned only this card. Not in scope A. Read from the card body through the named SHAs (first 1200 of 1790 characters) plus those commits.
- **CARD-1874** has no master commit. The Debug run it names finished green and the receipt is now the card text (history revision 1): wall 1883.015 s, finalization 164.819 s of which output-cleanup was 164.437 s, 177/177 passed, source `2349a1938b8ff11829ca6becf3c4719383bf9e52`. That is an evidence disposition, not a land. Closing it is an operator decision. It is not a refill.

## Duplicate or stale

- **CARD-1588** is the completed-feature case above, not a second open bug.
- **CARD-1644 overlaps CARD-0752** on `C448_V28_CancellationAwaitsOwnedGitBeforeReleasingExclusion` only. Both stay open until a Windows run says which defect is left. CARD-1644 also names `C1076_RunOptionsBudgetKillsASlowPushAndClearsItsJournal` and the native snapshot clock.
- **CARD-1929, CARD-1930, CARD-1931, CARD-1932 are not duplicates of CARD-1614.** Method-name search returned one card each. CARD-1614's description does not contain those four method names.
- Scope B title-and-prefix scan (281 cards) for "land it", "already landed/fixed/on master", "superseded", "obsolete", "duplicate of", "hotfix", and "throwaway" hit CARD-1588 and CARD-0894. CARD-0894 is eight positive controls that cannot go red at their plan labels. That is not stale. Under the 2026-11-12 mutation waiver it is an operator close-or-keep, same as the PC-1..PC-81 line on CARD-0979.
- No other scope B card was obvious enough, from title plus 400 characters, to call stale. Older backlog rows were not re-audited against master.

## Operator questions

1. Close CARD-1913 on `3cdd5a4c62c417cb2cb481d0cad09dfdc085971c` and `f658c8bd6375221fd1174a2cdd12d93a9fc20484`, leaving CARD-1946 as the witness gap?
2. Close CARD-1588 on the three master patch-id matches above?
3. Close CARD-1874 on the receipt written on the card (no master commit)?
4. Mutation waiver: drop or keep the PC lines on CARD-0979 (81 SourceLanding controls, plus nine seam results and Windows V-5 that the waiver does not cover) and on CARD-0894?
5. Did the CARD-1885 / CARD-1906 24-hour Linux compiler trial succeed? CARD-1950 and CARD-1903 wait on that before any default-on seat.
6. CARD-1932 forbids the standing runner. Which qualified lane, if any, may run that witness?
7. CARD-1643 CP-12 needs a cgroup v1 host. Server2 is not one, per the card. Is that check waived?

## Not done, noted

No fix was designed or implemented. CARD-0669 was not classified as done: `LandRefRetention` on master (CARD-1432, Done) does delete `source-recheck` observation pins under `refs/antiphon/land/`, and the card also asks for land-verify temp artifacts, which this pass did not prove.
