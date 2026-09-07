# CARD-0409 Code evidence

Implementation of [the disposition plan](2026-09-07-card-0409-privacy-disposition-plan.md), including its appended TestDesign and brief amendments F-1/F-2/F-3. Publication behavior remains owned by [card-file-privacy.md](../../card-file-privacy.md).

## Outcome

The live batch applied **126 content edits: 72 whole-card Private exclusions and 54 atomic Notes migrations**. This includes four permitted Private exceptions to the original Notes cohort. The 27 Accept and 275 unflagged original cards retained their public hashes, visibility and notes-presence with no migration PATCH. Final read-back at **2026-09-07 07:34:22 UTC** found **429 cards, five archived, 72 Private, 357 Inherit, zero Public and 54 note-bearing cards**. The only added card is the authorized successor CARD-0429. No concurrent lifecycle/ownership/archival changes were observed against the Code capture. The revision endpoint independently confirms exactly **126 new migration ContentEdits**, one per intended GUID.

The original 428-card investigation, independent TestDesign snapshot and Code preparation agree on all original public-field hashes and identities. The 153-row ledger still has its original 68 Private / 58 Notes / 27 Accept assignments. Execution treats four Notes rows as the D-2 whole-card Private exception; it does not relabel them as successful Notes moves.

| Notes exception | Uneditable public field containing sensitive detail | Execution |
|---|---|---|
| CARD-0183 | terminalReason | Visibility-only Private; original content retained |
| CARD-0232 | title and terminalReason | Visibility-only Private; original content retained |
| CARD-0348 | title | Visibility-only Private; original content retained |
| CARD-0401 | terminalReason | Visibility-only Private; original content retained |

All 54 executed Notes rows have hash-bound reviews of their proposed public descriptions and unchanged public fields. The review covered trigger, failure, fix, validation, uncited repetitions and historical meaning. CARD-0357 uses explicitly synthetic identity examples; CARD-0072 and CARD-0173 retain roles and technical behavior. CARD-0409's title and description now state the 428-card database scope, archived cards, exceptions, multiple publication gates and the successor. Its title is included in the same paired content PATCH.

### Category reconciliation

Categories overlap; they are not six disjoint cohorts. Counts are Private / Notes / Accept.

| Category | Planned | Executed after D-2 exceptions |
|---|---:|---:|
| T | 4 / 2 / 0 | 4 / 2 / 0 |
| F | 23 / 10 / 0 | 23 / 10 / 0 |
| I | 17 / 5 / 0 | 18 / 4 / 0 |
| P | 30 / 12 / 21 | 30 / 12 / 21 |
| N | 62 / 37 / 1 | 65 / 34 / 1 |
| O | 17 / 19 / 6 | 18 / 18 / 6 |

## Holding rule and successor

The Code brief mistakenly requested `/docs/cards/*` plus `!/docs/cards/antiphon/`, conflicting with the original D-4/V-6/PC-10 blanket `/docs/cards/` hold. That was caught before Review and reverted at `2a99227a`: the shipped state is the blanket `/docs/cards/` hold from D-4/S-1, recorded in the owner document. **Antiphon and every other board are ignored**, with no exception. V-6's actual-board probe therefore expects Git exit **0**, matching future-board probes. PC-10 removes the blanket `/docs/cards/` rule in scratch Git and must turn all board assertions (Antiphon included) red. No claim is made that Git ignore protects the Antiphon directory from manual staging.

The original `.gitignore` byte prefix, newline convention and `backups/` entry remain intact. Exactly one named holding/managed block is appended. Reinstallation and nonexistent card/INDEX probes verify repeatability without creating real export files.

**F-1: backlog CARD-0429, "Activate reviewed Antiphon card publication after full public-field review",** owns the exit. Owner: Antiphon repository operator. Review date: **2026-09-21**, also recorded as the card's due date. Its identifier/title are immediately beside the owner document's exit conditions. It requires fresh review of all eligible public fields, including the original 275 unflagged cards and additions, explicit board opt-in and repository classification, and review of the first generated card/INDEX diff before the first manual publish. AutoCommit stays false. Creation of this successor is the separately authorized F-1 board write; it was not spawned or moved through a lifecycle stage.

## Custody and execution

The one-time batch and its payloads are gitignored under `.antiphon/card-0409-disposition/`. They are intentionally absent from this commit. They contain the frozen allowlist, source/desired hashes, exact source-slice manifests, semantic review hashes, binary UTF-8 payloads, durable intents, read-back receipts, timestamped policy samples and local verification results. Diagnostics use identifiers, field names and counts, never note bodies. The unchanged `scripts/card.ps1 edit` is the only migration writer.

All 126 captured CLI responses were checked for the absence of note-body fields and for exact card/public-field identity. Visibility-only responses initially retained the unchanged source description in private scratch; retained output was reduced to metadata and response hashes before the custody scan. All 126 stderr captures were empty. The final scan covers the six intended tracked blobs, retained CLI output and public batch logs against 629 selected markers/source excerpts. Neutral Markdown table separators are excluded; short alphabetic identity markers use word boundaries so ordinary words do not count as private names.

F-2 freezes exactly the original 58 Notes GUIDs once, with a digest. Immediately before every private-notes GET, the helper checks GUID membership and Antiphon board identity; a mismatched token requires a fresh paired read. It never reads notes for other Private, Accept, unflagged or successor cards. Four allowed candidates ultimately take the Private exception, without writing their notes. Synthetic foreign-GUID and foreign-board attempts both refuse before HTTP (two refusals, zero GETs).

F-3 writes description and note payloads as bytes and verifies UTF-8 round-trip equality. The 54 real migrations had no preexisting notes; preservation of nonempty notes, mixed CRLF/LF, spaces, emoji, CJK, backticks and literal shell syntax is covered by synthetic tests. Real merged notes range below the 20,000 UTF-16-unit limit; the maximum is 9,540. Source slices remain exact and full merged-note hashes/UTF-16 lengths are checked again after persistence.

Every mutation uses its GUID, board and freshly prepared pinned token. A successful write must yield exact desired public/protected fields and note hash, a rotated token and one new revision. Unknown outcomes stop; retries reconcile against durable intent and persisted content before deciding whether a PATCH is needed. Completed markers alone never authorize skipping or rewriting a changed card. Private edits change visibility only; Notes edits change description/notes/Inherit, with CARD-0409's authorized title correction. Lifecycle, tracker, ownership and policy writes are excluded.

## Verification

The second apply finished at **2026-09-07T07:37:28.382991+00:00** with **zero PATCHes, 126 exact matches and zero refused live note reads**. Read-only rerun verification at **2026-09-07T07:38:42.139619+00:00** confirmed the same census, **zero additional revisions and no duplicate migration sections**. All 126 migration revision receipts remain unique. The real helper's **382 private-note GETs** across preparation, preflight, application and verification were confined to the frozen 58-GUID allowlist; only 54 GUIDs were reread after preparation.

All **524 timestamped policy samples**, from **2026-09-07T06:54:33.580650+00:00** through **2026-09-07T07:38:43.516160+00:00**, show Enabled=true, syncCardFiles=false, RepositoryVisibility=Unknown, AutoCommit=false and all three removal indicators explicitly false. Each of the three previews (prepared/final/rerun) returned dryRun=true, written=0, deleted=0, eligibleCards=0, error=null, commitSha=null and board_not_opted_in. Working tree (including ignored files), Git index and current HEAD contain no card/INDEX markdown. Actual/future board Git ignore probes and repeat-install byte checks pass. Exact-path staged custody passed on the final six blobs and retained output: 629 markers across 263 blobs, zero matches.

### Acceptance and regression mapping

| Acceptance | Evidence and result | Regression guarded |
|---|---|---|
| V-1 | Original capture digest, 428 identity/public-hash comparisons, independent TestDesign snapshot, exact 153 ledger rows/references/category counts; fresh preparation and identity-based final census. Source/token/board mismatches refuse before writing. | R-1, R-8 |
| V-2 | API integration stores the Inherit description/note pair with one ContentEdit; a fresh DB scope verifies equality/hash/UTF-16 length and private history. Real CLI fake-API test sends both byte-backed files, title and pinned token in one request. Valid 20,000-unit note accepted; 20,001 refused with unchanged state. Live preservation/read-back receipts cover every Notes write. | R-2 |
| V-3 | Stale combined PATCH returns 409 with unchanged winner content/note/token/revisions. Local injections cover before submit, after server commit before acknowledgement, and after response before receipt. Exact persisted state recovers safely with one total write; missing/mismatching completion cases and live zero-write rerun are checked. | R-1, R-3 |
| V-4 | Every candidate's proposed public fields reviewed; 54 reviewed Notes intents require source-slice/merged-note hashes and a semantic review hash. Recursively scan all public string fields for identified private values/variants. Remaining uneditable repeats select the four reported Private exceptions. | R-4 |
| V-5 | Open-gate scratch Git/test-DB fixtures actually render a public sibling, exclude active and archived Private cards plus all metadata from file/INDEX/working tree/index/HEAD, and exclude public-sibling notes. Revocation and explicit Public versus board-off/Unknown/ignored gates pass. DTO/event/log note boundaries pass. | R-5 |
| V-6 | Byte-prefix and repeated-install checks, actual/future card/INDEX Git probes and owner-document review. The brief-selected exception grammar was reverted at 2a99227a; the blanket D-4/S-1 hold described above is what shipped. | R-6 |
| V-7 | Strict policy checks before/after each edit and preview; dry-run counters/reason/error/commit checked explicitly; no generated markdown in working tree, index or current HEAD. Route trace excludes settings/lifecycle/tracker writes and non-dry synchronization. | R-7 |
| V-8 | Per-GUID intent/receipt and authorized-field equality, exact migration revision metadata, unchanged original Accept/unflagged hashes/visibility/notes-presence, separate additions/concurrent-state accounting, exact-path staging and custody scan. | R-7, R-8 |

### Synthetic test results

- Application privacy suites: **57/57**, comprising API 19, note boundary 2, policy 4, sync 23, ignore 8 and documentation 1.
- Real PowerShell script against fake API: **19/19**, including the new one-pinned-paired-PATCH test.
- Final sync rerun after adding explicit Git-index marker inspection: **23/23**.
- Local batch tests: **23/23**, including three interruption points, allowlist refusals, exact payload bytes and short-identity marker boundaries.
- First application attempt: **55 passed, 2 failed** from the new external-reference fixture's EF insertion state. Explicitly adding the new reference entity fixed the fixture; the complete rerun was 57/57. These were setup failures, not positive-control evidence. Existing compiler warnings remain.

TRX files are local under `tests/Antiphon.Tests/bin-c409/TestResults/`: `c409-application-green.trx`, `c409-script-green.trx`, and `c409-sync-final.trx`. Mutation runs used a detached, gitignored scratch checkout and isolated test databases; no production gate was opened for a control. Its 16 red/green TRX files were copied with hash receipts to `.antiphon/card-0409-disposition/control-reports/` before the owned mutation checkout was removed.

### Positive controls

Every listed unsafe variant produced the named assertion failure, then passed when restored. A compile/setup failure is not counted as a red control.

| Group | Mutations executed | Red / restored green |
|---|---|---|
| PC-1 | Missing row, duplicate GUID, foreign board, altered action | 4 / 4 local |
| PC-2 | Changed source with fresh token; changed token alone | 2 / 2 local |
| PC-3 | Omit description; omit notes from paired API PATCH | 2 / 2 API cases |
| PC-4 | Use current token while expecting stale conflict | 1 / 1 API case |
| PC-5 | Lost character, lost prefix, note-token race, UTF-16 overflow | 4 / 4 local |
| PC-6 | Note-only edit, alias/label/external repeat, corrupt slice, missing review hash | 6 / 6 local |
| PC-7 | Second write/revision, duplicate section, mismatched persisted note, missing durable intent | 4 / 4 local |
| PC-8 | Private becomes Inherit; public description contains note marker; active and archived variants | 4 / 4 API cases |
| PC-9 | Open disabled board, classify Unknown as Public, remove target ignore; two fixture variants each | 6 / 6 API cases |
| PC-10 | Remove the blanket `/docs/cards/` holding rule (shipped grammar, post-2a99227a) in scratch Git | 1 / 1 local; adjusted expectation documented above |
| PC-11 | Each unsafe policy/removal/null value, written/error/commit result, unavailable status | 14 / 14 local |
| PC-12 | Working/index/HEAD residue, settings/lifecycle/tracker request, protected-field change, evidence sentinel | 8 / 8 local |

Totals: **43 local mutation variants** plus **8 TUnit mutation variants exercising 13 red test cases**, all independently restored green. Per-run results are gitignored alongside the batch; TUnit mutation reports have unique filenames and executed-method checks.

## Reproduce and review

From this checkout, the original public-input validators are:

```powershell
python .antiphon/privacy-plan-tools-8bb54929.py validate
python .antiphon/privacy-plan-tools-8bb54929.py verify-plan
python .antiphon/card-0409-disposition/test_batch.py
python .antiphon/card-0409-disposition/run_local_controls.py
```

The prepared batch's read-only live verifier is `python .antiphon/card-0409-disposition/verify.py rerun`. It requires the retained local intents/receipts and the same explicitly authorized note-read scope. Later lifecycle/content changes can produce a safe refusal; compare them with the retained final census rather than rebuilding intent or overwriting them. Do not recreate intents from already-sanitized live cards or infer authorization for broader note reads. `run_safe.py apply` is the guarded writer; its proven second invocation made zero PATCHes. Do not run it for unrelated later changes.

Run TUnit sequentially with `dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-c409/ -- --treenode-filter '/*/*/<Class>/*' --report-trx --report-trx-filename <fresh-name>.trx`, selecting the six application classes named above or `CardFilePrivacyScriptTests`. There is no client/runtime implementation change and no stack restart.

## Limits

This cleanup does not certify the 275 unflagged cards or later additions for publication. Private visibility and private notes do not erase old revision bodies, Git history, external issue content or prior copies. CARD-0429 owns the fresh eligible-card review and publication decision. Policy evidence consists of timestamped observations around every operation, not a distributed lock against another operator changing settings between reads. No runtime setting, tracker, lifecycle stage or production service was changed by the migration.
