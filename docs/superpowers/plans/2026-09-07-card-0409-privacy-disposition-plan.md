# CARD-0409: disposition of existing Antiphon card privacy findings

Plan task `8bb54929`, 2026-09-07. **Disposition: 68 cards Private, 58 cards with sensitive description excerpts moved to PrivateNotes, and 27 findings accepted as publishable engineering context.** These 153 decisions are mutually exclusive; the six finding categories overlap. The remaining 275 cards retain their investigation classification of "no specific finding", not a publication certification.

This is a plan. No card, private note, publication setting, generated export or ignore rule was changed. TestDesign should specify the migration controls before Code applies the decisions. There is no recovery of vanished working-tree exports to perform.

## Ground truth

| Earlier premise | Verified state and consequence |
|---|---|
| 409 untracked markdown files need sweeping and recovery. | The authoritative investigation swept 428 database cards, including five archived cards. CARD-0408 already reconciled away the old exports. Current `git ls-files -- docs/cards` and `git ls-tree -r --name-only HEAD -- docs/cards` are empty; status reports no pending working-tree or Git removal. Do not recreate an unsanitized export to investigate it. |
| Antiphon's repo may be private. | `gh repo view --json nameWithOwner,visibility` reports PUBLIC. Antiphon's configured `RepositoryVisibility` is still Unknown; classification is not a change to GitHub visibility. |
| AutoCommit is the only protection. | Live card-file status has `enabled=true`, `syncCardFiles=false`, `repositoryVisibility=Unknown`, `autoCommit=false`, `removalPending=false`. Missing-ignore and unknown-visibility warnings remain. Do not infer safety from AutoCommit alone: an untracked file can be manually added. |
| All findings require the same treatment. | Full manifest validated: 428 unique cards, 153 findings, 275 without a specific finding; all source public-field hashes and line references match the capture. A fresh full-board GET, flattening `columns[].cards` with `includeArchived=true`, still has 428 cards and no changed scanned public fields, additions or removals. All have Inherit visibility and no notes. |
| PrivateNotes alone removes public detail. | `CardFilePublicCard` exports description, title, alias, labels, terminal/archive reasons and external metadata. Notes are excluded, but an unchanged public description remains exportable. Move private detail out of public fields in the same content edit that saves the notes. |
| Any public field can be sanitized through `card.ps1 edit`. | `UpdateCardContentRequest` and `CardService.UpdateContentAsync` accept description/title/alias/labels/notes/visibility but **not TerminalReason, ArchivedReason, ArchivedBy or external-issue metadata**. An unknown content property is rejected. Sensitive terminal evidence is a whole-card Private decision here, not an instruction to PATCH an unsupported field. |
| Whole-card exclusion requires a new feature. | Existing `CardFileVisibility.Private` excludes the card and its INDEX entry. Public does not bypass project, board, archive, path or ignore gates. No new privacy schema, default, endpoint or renderer behavior is needed for this cleanup. |
| The sweep found a credential leak. | No literal credentials were identified. CARD-0357 contains the operator's own email, not a third-party address. CARD-0124's user-at-host syntax and CARD-0150's public service mailbox are not evidence of leaked personal email. No rotation or external-history erasure is planned. |

Owners read: [card-file privacy](../../card-file-privacy.md), [card lifecycle](../../agent-card-lifecycle.md), [HTTP operations](../../ops-http.md), [credentials](../../agent-credentials.md), [project conventions](../../project-context.md), [testing/build](../../testing-and-build.md), and [stage workflow](../../orchestration-loop.md).

Local investigation inputs, **not for Git or attachment/publication**:

- `.antiphon/task-82a5f7b7.md`: full sweep report and its limitations.
- `.antiphon/privacy-manifest-82a5f7b7.json`: all 428 classifications, identities, field hashes and snapshot tokens; read in full and validated.
- `.antiphon/privacy-sweep-82a5f7b7.json`: original sensitive public-field capture. SHA-256 `0c4cc5836a2eefc3cbccb59f07d64335311d40e56965b6712ddcc0b059a193e2`.
- `.antiphon/privacy-disposition-8bb54929.json`: this plan's 153 decisions with source hashes; generated locally from the explicit selections below, containing no original excerpts.
- `.antiphon/privacy-plan-tools-8bb54929.py`: local validation/reference helper. Its `validate`, `live`, `evidence` and `dispositions` modes support planning; it has no card-write mode. The `evidence` mode displays local source excerpts and must not be redirected into tracked evidence.

The investigation reviewed candidates in context, not every card line by line. It did not read private notes, discussion/revision bodies, attachments or external sites. This plan accepts or treats the findings actually identified; it does not expand that detection result into a claim that all other surfaces are sanitized.

## Decisions

### D-1: retain technical learning; separate private deployment evidence

Use three concrete dispositions, with the per-card table below authoritative over category generalizations:

- **Private (68):** set `CardFileVisibility.Private`, preserving the card's existing content and lifecycle. This is the final disposition of these cards under this plan. Personal/customer incidents or dense private work-system inventories are not needed in a public engineering ledger in their present form. This also covers sensitive terminal evidence that the existing content API cannot rewrite. A later neutral write-up can preserve a technical lesson without republishing the original card.
- **Notes (58):** move the cited private portions of description lines to PrivateNotes and replace them in the description with neutral technical statements. Keep the card Inherit after the atomic content edit. The source references identify concrete lines to start from, not permission to ignore duplicates elsewhere in the public fields.
- **Accept (27):** leave content and Inherit visibility unchanged; the cited material is acceptable engineering context. Do not create Public flags merely to record this table. This acceptance covers the finding, not permission to turn on the board or skip review of future edits.

Reject blanket Private for all 153: it unnecessarily removes useful technical records whose only finding is a canonical Antiphon path or ordinary configuration preference. Also reject blanket acceptance of N/O findings: corporate routing, private workload inventories, account balances, private conversations and personal contact information remain private even when no credential is present.

| Category | Private | Notes | Accept | Rule |
|---|---:|---:|---:|---|
| T: third-party names | 4 | 2 | 0 | Whole-card exclusion for the identifiable guest/customer/property/family incidents; neutralize the two limited named examples in CARD-0072 and CARD-0173. Hypothetical family examples still do not need a real name. |
| F: family/customer/personal activity | 23 | 10 | 0 | Keep real personal requests, conversations and workload inventories internal; preserve generic notification, delivery, scheduling and privacy requirements. |
| I: contact/private-conversation identifiers | 17 | 5 | 0 | Move or exclude personal email, private group/DM/thread/native-conversation identifiers and identifying account/job references. They are not automatically bearer credentials, but that does not make them publishable. |
| P: filesystem layout | 30 | 12 | 21 | Accept the explicitly listed Antiphon checkout/service/log/generated-worktree paths. Remove account-home names, encoded native transcript paths and unrelated work/private repository layout. Do not redact every absolute path mechanically. |
| N: private-system/deployment context | 62 | 37 | 1 | Keep private hostnames, real workload/project rosters, corporate proxies, routes and live configuration out of exports. CARD-0382's generic deployment-specific wrapper filename and implementation boundary are accepted. |
| O: operator identity/account/config | 17 | 19 | 6 | Keep full identity/contact links, private quotations, balances/spend, auth inventory and billing mappings internal. Accept the specifically listed first-name product attribution, model/reasoning preferences and generic historical model substitution. |

Category totals overlap and must not be added to derive the number of edits. The T cases resolve to Private: CARD-0067, CARD-0191, CARD-0233, CARD-0313; Notes: CARD-0072, CARD-0173. CARD-0357 is Notes: replace the operator name/email example with `Example Operator` / `operator@example.invalid`, keeping the exact original identity evidence only in the private note. Do not copy that address into this plan, a commit or the cleanup report.

### D-2: move sensitive spans, not whole descriptions

For each Notes row, retain original cited private text under a note heading recording card identifier, source field/line references and source field hash. Keep only the working detail needed to understand the investigation; do not duplicate an entire long description. The largest set of currently cited excerpts is 2,136 UTF-16 code units before a provenance header, on CARD-0145. Validate the **complete prepared note**, including any preserved existing notes/header, against the actual 20,000-unit limit; never truncate to fit.

Public replacements must keep the trigger, failure, fix and validation intact:

- Replace names with roles such as `a sender`, `a customer` or `a household member`; keep private quotes and event/customer specifics in the note. Avoid replacing one real name with another plausible real identity.
- Replace private project/workload/agent names with `a sibling project`, `a worker` or an explicit synthetic example. Move associated live roster/epic/task inventories with them. Retain Antiphon code symbols, schema names, public provider names and relevant algorithmic facts.
- Replace contact/channel/thread/native conversation identifiers with visibly synthetic examples or role references, preserving relationships needed to explain matching/idempotency. Do not turn a measured incident into a claimed synthetic test: label the public explanation as a generalized account of a real incident.
- Replace account/private-project path segments with a neutral Windows example such as `C:\src\example-app`, `<worktree>` or `<profile>`; retain separators and path relationships needed by the bug. Keep the exact original path only in notes. Canonical paths on Accept cards remain as measured.
- Replace private hosts/routes/SSH identities with `scheduler.example.invalid`, `user@host.example.invalid` or a role; preserve whether a connection was local/remote and its protocol. Keep deployment-specific no-auth inventory, billing/key aliases and real account measurements in notes; retain generic security requirements and configuration field names.

Read all public fields of each affected card while preparing its edit, including duplicated terminal evidence and external metadata. Source references are 1-based description (`D`) / terminalReason (`T`) lines in the captured field, not generated markdown line numbers. Move only sensitive spans; if a cited line combines requirements and evidence, save its exact source line privately and retain a sanitized public line. Search the rest of that card for repeats of the identified private values. Do not run a global regex replacement of code, identifiers or paths.

If a Notes card has drifted, has sensitive uneditable public fields not covered by the sweep, or cannot preserve needed context within the notes limit, **leave/set that card Private and report the exception**. Do not publish it half-sanitized, add a new edit endpoint under this card, clear existing notes, or silently count it among the 58 completed Notes migrations. Expected counts assume the validated snapshot; any changed disposition requires an explicit evidence row.

Reject adding a note while leaving the original description unchanged. Reject editing generated markdown, direct database updates, reopening/reclosing cards to rewrite outcomes, and unarchive/archive cycles. Those would bypass the intended content operation or invent lifecycle events and tracker effects for a privacy edit.

### D-3: preserve concurrency, private-note custody and historical truth

The existing `scripts/card.ps1 edit` path is the writer. For a Notes edit, send the sanitized `-DescriptionFile`, original-sensitive-detail `-PrivateNotesFile`, visibility and a neutral `-Reason` together. `CardService.UpdateContentAsync` saves these in one content operation and records a revision. Keep payload files under gitignored `.antiphon/`, never under `docs/` or `docs/cards/`. Public reasons may say `CARD-0409 privacy disposition applied; technical meaning preserved`; they must not repeat the removed text.

Use a fresh full card read, verify its public-field hash against the source being edited, then pin that read's concurrency token with `-Token` while submitting the prepared change. A fresh automatic token by itself does not validate text prepared from an old snapshot. A 409 is a refusal: reread and reprepare, not a blind token refresh around the old body. If the intended new state already matches the local completion record, skip it rather than creating duplicate revisions/notes on rerun.

All current notes are empty. If Code finds `hasPrivateNotes=true` before a card was migrated, do not overwrite them. The [owner](../../card-file-privacy.md) says: "Agents must not read the notes route unless the card's own brief explicitly instructs". **No private-notes route was read in this Plan.** The Code brief should explicitly authorize private-notes reads only for the listed migration cards, to preserve any concurrent existing notes and to verify written note hashes/lengths without echoing text. If that authorization is absent, retain the existing note and exclude that card pending a scoped follow-up; do not infer permission from ordinary card GET access.

Private visibility and PrivateNotes are card-file publication boundaries. Existing descriptions in historical revisions, original GitHub issues, transcripts, other documents or earlier publications are not erased by this cleanup. Do not claim the whole database/history or external mirrors have been made private. No external edit, notification, rotation or history rewrite is in scope.

### D-4: Antiphon's `docs/cards/` gets a named holding measure and a defined end state

**Immediate Code action: install the managed blanket ignore below as a HOLDING measure.** Keep board sync off, configured repository visibility Unknown and AutoCommit false throughout this cleanup. Retain global synchronization enabled so revocation/reconciliation is not frozen. Preserve `.gitignore`'s existing bytes/newlines and the `backups/` protection.

```gitignore
# CARD-0409 HOLDING: reviewed card publication is not activated by the privacy cleanup.
# BEGIN ANTIPHON CARD FILES
/docs/cards/
# END ANTIPHON CARD FILES
```

This protects against an incidental `git add -A`. It is **not the final design for card documentation**, and its presence alone does not finish the 153 dispositions. CARD-0004's intended one-way, generated engineering record remains useful; the database continues to own it and AGENTS.md's "edit the card" instruction stays correct. Update [card-file privacy](../../card-file-privacy.md) to record this explicit Antiphon holding decision and link this plan.

**Selected end state: deliberate publication of reviewed engineering cards in `docs/cards/antiphon/`, with all other board directories protected.** Once the holding exit conditions below are met, the intended ignore shape is:

```gitignore
# BEGIN ANTIPHON CARD FILES
/docs/cards/*
!/docs/cards/antiphon/
# END ANTIPHON CARD FILES
```

Use the safe slug returned by live status if it changes; never broaden the exception to every board. The project must then be honestly classified **Public**, the specific board explicitly opted in, and the first generated diff reviewed before an exact-path manual Git commit/push. AutoCommit remains false; this cleanup does not authorize unattended publication or a live opt-in.

**Holding exit conditions:** the 153 dispositions are verified; every card then eligible for export has a fresh public-field review (including the 275 previously unflagged cards and additions since the sweep); unapproved cards are explicitly Private; the owner intentionally selects the board exception and opt-in; preview and generated card/INDEX review show only the approved set. This is the publication activation step, not extra cleanup to invent during Code. Keep holding until it is done.

CARD-0408 does not provide a `reviewed` flag or a Public-only allowlist: **Inherit is eligible on an opted-in board**, and newly created cards default to Inherit. Therefore neither 275 clean scan results nor a set of Public overrides can enforce review by themselves. Keep authoring public fields as public material, use Private/PrivateNotes before saving sensitive work, and review future generated diffs before committing. A guarantee of unattended publication without future card review would require a separate product policy change; it is not achieved by turning on AutoCommit here.

Reject permanent blanket-ignore-as-accidental-policy, silent negation of the CARD-0004 design, classifying the real public repo as Private, and enabling sync merely to obtain output for this cleanup. Existing exports are already gone; while holding, an empty export set is the correct result of reconciliation.

### D-5: scope is this board and this snapshot

The table covers all 153 findings exactly once. Accept cards and the 275 unflagged cards need no content/visibility write in Code. Private cards need a visibility-only content edit; Notes cards need one combined content/notes edit. Under unchanged input, that is **126 affected cards written**, final **68 Private / 360 Inherit / 0 Public**, with **58 cards holding notes**. No lifecycle move, label/ranking edit, tracker push or archive change is part of the operation.

New or changed cards discovered on the final full-board read remain unapproved for publication. Record identifiers and changed field names only, keep the holding state, and do not silently apply old line numbers or claim coverage of the additions. The five currently archived cards have no specific sweep finding; they remain in the coverage census and are not implicitly approved by being archived.

## Implementation slices

### S-1: install the holding protection and prepare a reproducible local batch

Files: `.gitignore`, `docs/card-file-privacy.md`, this plan; local payloads/receipts under `.antiphon/card-0409-disposition/`. Existing `scripts/card.ps1` is used unchanged.

1. Read current board status, resolve Antiphon by board name and verify the safe repository/board target. Fetch `GET /api/boards/{id}?includeArchived=true`, flatten `columns[].cards`, and reconcile the full manifest by GUID, identifier and public-field hash. Read all flagged fields; do not dump content into command logs. Snapshot current tokens, hashes and lifecycle metadata locally.
2. Confirm publication remains off and no removal is pending; if someone changed policy since this plan, stop dependent writes and report the new state instead of overwriting their decision. Add the explicit holding ignore, inspect effective Git protection using a nonexistent probe path, and preserve all unrelated ignore rules.
3. Prepare the 68 visibility-only operations and 58 paired description/notes payloads. Record desired final hashes/visibility, source hashes, note UTF-16 length and completion state locally. Treat the tracked table as the decision record; do not commit raw before/after bodies, sensitive replacement dictionaries or note files.
4. Review every prepared public description and unchanged public field against D-1/D-2 before submitting. No whole-board opt-in, unignore, fake card on the live board, or unsanitized export is needed for preparation.

Tests/verification owner: `CardFileIgnoreTests`, `CardFilePrivacyDocumentationTests`, and TestDesign's manifest coverage/ignore controls. No production source change is required.

### S-2: apply card exclusions and atomic note migrations

Surfaces: `scripts/card.ps1 edit`, `PATCH /api/cards/{id}/content`; local script/payloads in `.antiphon/card-0409-disposition/`. Do not add a permanent migration API or a generic redaction service.

- Resolve each Antiphon GUID from the fresh board capture; do not use board-ambiguous `CARD-nnnn` alone. Submit a Private edit for every Private row, preserving its body and lifecycle.
- For every Notes row, send sanitized description and note file in the same content edit, leaving Inherit. Preserve existing notes only through an explicitly authorized private-notes read; never overwrite unknown note content. Do not echo the note body or use `-Json` output as a public artifact, because ordinary responses still contain card descriptions.
- Check the returned card's visibility, note-presence and public-field result, and keep a sanitized receipt keyed by card ID. Verify private-note hashes only under the Code brief's explicit permission. Stop/reprepare on stale content or concurrency conflict; reruns skip completed matching states.
- CARD-0409's own description/title must also be corrected to the current 428-card/database disposition scope. Preserve its historical finding in the existing revision; move the cited other-project inventory to its private note. Replace the stale claim that AutoCommit is the sole gate with the actual multi-gate publication contract. Do not close/reopen the card as part of its content correction.

Tests/verification owner: existing `CardPrivateNotesApiTests`, `CardPrivateNotesBoundaryTests`, `CardFilePolicyTests` and `CardFilePrivacySyncTests`, plus TestDesign's batch controls. Existing fixtures must use synthetic data; never inject the real manifest/excerpts into test source, snapshots or TRX output.

### S-3: verify the database disposition and empty export state, then record evidence

Files: `docs/superpowers/plans/2026-09-07-card-0409-privacy-disposition-code-evidence.md`, the owner doc and holding ignore; sensitive verification artifacts stay local.

1. Read the full board again. Check every disposition, every changed public-field hash, note presence/authorized hash, and preservation of unrelated fields. Compare the 27 Accept cards and 275 unflagged cards to their original public-field hashes. Report any concurrent changes separately; do not attribute them to the cleanup or silently overwrite them.
2. Check current card-file status and use `POST /api/boards/{id}/card-files/sync?dryRun=true` to inspect policy and reconciliation. Under the holding policy, no card is eligible to be written. If reconciliation is needed, use the documented `dryRun=false` endpoint with publication still off; interpret response fields, not HTTP 200 alone. Confirm no writes, no pending removal and no generated card/INDEX content in working tree, index or HEAD. A timeout/unavailable inspection is not a passing verdict.
3. Verify `.gitignore` protects the actual board target and a different/future board target; inspect exact-path Git state. Do not force-add card exports or claim old Git/external history was erased. Existing no-export state is verified, not repaired by reconstructing files.
4. Commit/push only the intended ignore/doc/evidence changes. Record actual disposition counts, exception IDs, scoped checks, policy values and no-export results without raw sensitive text. Do not stage `.perfmon/`, `.antiphon/`, backups, note payloads, raw card responses or generated files.

## Verification requirements for TestDesign

The next stage should turn these into executable V/R/PC controls without revisiting the selected dispositions:

- Coverage: the tracked table has exactly the 153 flagged identifiers, one action each, 68/58/27 totals; category counts match the overlapping table. All 428 original hashes/line references validate and the live census includes archived cards.
- Privacy transformation: each Notes operation stores its intended private context and removes the same sensitive values/duplicates from every exportable field while retaining technical meaning. A synthetic control that adds notes but leaves the source line public must fail review/validation. No original note/excerpt values may appear in public logs or tracked artifacts.
- Whole-card exclusion: in an isolated synthetic fixture with other policy gates open, a Private card appears in neither the rendered markdown nor INDEX, and notes never render. A Public override must not defeat a disabled board or ignored target. Use CARD-0408's existing fixtures/classes, not a live opt-in for this control.
- Concurrency/idempotency: changed source hash, stale token, unexpected existing notes and oversize prepared notes cannot produce a blind write. Exercise deliberate mismatches on synthetic/local prepared input, not destructive mutations of live card data. Rerunning a completed batch must not append duplicate note content or create another edit solely from the rerun.
- Holding: prove effective ignore protection in the real repo read-only; use a scratch repo to demonstrate that removing the holding rule makes the probe fail. The final live check must still show sync off, Unknown, AutoCommit false, no pending removal, and an empty export set. A clean directory by itself does not prove the 126 data operations occurred.
- Scope: no terminal/archive/lifecycle rewrite, no external tracker operation, no publication activation, no raw private artifacts in the commit. Note-read verification needs the narrow explicit permission described in D-3 in the Code brief.

This is a data disposition with a small ignore/doc change, not a reason to rerun every CARD-0408 suite or the full assembly. TestDesign should choose the class filters above that its actual checks depend on, run TUnit with the isolated `OutputPath` form from the testing owner, and keep process-spawning suites sequential. Manual review of 58 sanitized descriptions is part of the work and cannot be replaced by a passing unit test or a credential regex.

## Per-card disposition ledger

The following table is the complete decision list. Descriptions of findings are deliberately paraphrased; the original names, addresses, hosts, paths and conversations remain in the gitignored source capture. `Notes` means D-2's paired edit, `Private` means whole-card file exclusion, and `Accept` means no write for this finding. Source tokens/hashes must be revalidated before Code acts.

<!-- CARD-0409-DISPOSITIONS -->
| Card | Categories | Source references | Disposition | Treatment / reason |
|---|---|---|---|---|
| CARD-0006 | P | D5,18 | **Notes** | Move the cited sensitive context to PrivateNotes; retain a neutral technical restatement. Windows account name, transcript directory and shared checkout location. |
| CARD-0011 | N | D2;T1 | **Private** | Exclude whole card: sensitive terminal evidence cannot be changed by content edit. Actual remote application hostname and reverse-proxy topology. |
| CARD-0020 | P | D30 | **Accept** | Accept Antiphon's canonical checkout, service/log or generated worktree path; no account home or unrelated private project. |
| CARD-0022 | O,N | D15,123 | **Notes** | Move the cited sensitive context to PrivateNotes; retain a neutral technical restatement. Actual shared-provider account arrangement and cross-project usage incident. |
| CARD-0032 | N | T3 | **Private** | Exclude whole card: sensitive terminal evidence cannot be changed by content edit. Names the other project used in the live setup incident. |
| CARD-0036 | I,N | T3,12 | **Private** | Exclude whole card: sensitive terminal evidence cannot be changed by content edit. Operator Telegram DM identifier and another live project named in deployment evidence. |
| CARD-0047 | O | D106-107 | **Notes** | Move the cited sensitive context to PrivateNotes; retain a neutral technical restatement. Actual deployment authentication inventory: disabled provider rows and subscription-based CLI authentication. |
| CARD-0056 | N,F,P | D14-18,37 | **Private** | Exclude whole card: private activity/system inventory is integral to the evidence. Live inventory ties family, education and club agent names to session IDs; shared checkout path. |
| CARD-0059 | P | D7;T21 | **Accept** | Accept Antiphon's canonical checkout, service/log or generated worktree path; no account home or unrelated private project. |
| CARD-0064 | N,F | D12,19,24,55 | **Private** | Exclude whole card: private activity/system inventory is integral to the evidence. Property-assistant and family session/traffic incident history, including live broker evidence. |
| CARD-0067 | T,F,I | D25,28,39,50;T10,24 | **Private** | Exclude whole card: sensitive terminal evidence cannot be changed by content edit. Family guest-list request names participants, gives approximate guest count, and identifies the private Telegram group. Does not contain the full guest list. |
| CARD-0068 | F | D6-13,22 | **Private** | Exclude whole card: private activity/system inventory is integral to the evidence. Retells the identifiable guest-list incident from CARD-0067, though no guest names are reproduced here. |
| CARD-0071 | N | D95 | **Notes** | Move the cited sensitive context to PrivateNotes; retain a neutral technical restatement. Identifies a live property-assistant deployment affected by the API-error incident. |
| CARD-0072 | T,F,N | D83-85 | **Notes** | Move the cited sensitive context to PrivateNotes; retain a neutral technical restatement. Names a real sender of unanswered private Telegram messages and links education/property agents to incidents. |
| CARD-0073 | P | D62,133,183 | **Notes** | Move the cited sensitive context to PrivateNotes; retain a neutral technical restatement. Actual worktree and encoded transcript locations; the separate trailing-backslash sample is illustrative. |
| CARD-0079 | P | D56 | **Accept** | Accept Antiphon's canonical checkout, service/log or generated worktree path; no account home or unrelated private project. |
| CARD-0085 | P | D18,34 | **Accept** | Accept Antiphon's canonical checkout, service/log or generated worktree path; no account home or unrelated private project. |
| CARD-0088 | P,O | D12,17;T1 | **Private** | Exclude whole card: sensitive terminal evidence cannot be changed by content edit. Operator-specific global instruction imports, sibling checkout, credential-custody inventory and prior machine drive layout. |
| CARD-0089 | P,O | D19;T1 | **Accept** | Accept canonical checkout path and generic historical model substitution; no quota balance, spend, contact or private-system identifier. |
| CARD-0090 | O | D3,13,18 | **Notes** | Move the cited sensitive context to PrivateNotes; retain a neutral technical restatement. Historical operator-provider availability/authentication incident, mixed with generic fallback design. |
| CARD-0094 | N,F | D22 | **Notes** | Move the cited sensitive context to PrivateNotes; retain a neutral technical restatement. Names actual family and property-management boards in the deployment inventory. |
| CARD-0095 | O | T5 | **Private** | Exclude whole card: sensitive terminal evidence cannot be changed by content edit. Measured live aggregate model usage and cost amount; personal operational spending/usage evidence, not a credential. |
| CARD-0102 | P | D11 | **Accept** | Accept Antiphon's canonical checkout, service/log or generated worktree path; no account home or unrelated private project. |
| CARD-0106 | O | D120 | **Notes** | Move the cited sensitive context to PrivateNotes; retain a neutral technical restatement. Live API-key/agent-environment inventory; generic placeholder design elsewhere is not a credential leak. |
| CARD-0107 | N,F,P,I | D112-125,142;T1-2 | **Private** | Exclude whole card: sensitive terminal evidence cannot be changed by content edit. Shared family/property Slack-Telegram gateway, server/container/Unix account layout, school-instance separation, local workspace and live channel identifiers. |
| CARD-0112 | P | D16,41 | **Notes** | Move the cited sensitive context to PrivateNotes; retain a neutral technical restatement. Windows username and real Claude/Codex transcript locations. |
| CARD-0114 | O | D20 | **Notes** | Move the cited sensitive context to PrivateNotes; retain a neutral technical restatement. Live authentication-store/profile inventory; no secret values in the migration description. |
| CARD-0118 | N,O | D3,10;T15-16 | **Private** | Exclude whole card: sensitive terminal evidence cannot be changed by content edit. Operator account/session residue and remote housekeeping deployment; distinguishes actual account state from generic isolation guidance. |
| CARD-0119 | N,F,I,P | D7,27,30-31;T20 | **Private** | Exclude whole card: sensitive terminal evidence cannot be changed by content edit. Real Slack app/workspace/DM identifiers, sibling checkout, shared family/property gateway and remote env-file custody. |
| CARD-0124 | N,P,I | D5,9;T3 | **Private** | Exclude whole card: sensitive terminal evidence cannot be changed by content edit. Actual scheduler user/workspace/job paths, SSH username and key-file location, schedules and nightly checkout. |
| CARD-0127 | P | D6 | **Notes** | Move the cited sensitive context to PrivateNotes; retain a neutral technical restatement. Windows account and unrelated real transcript location quoted in recovery evidence. |
| CARD-0131 | N | D5;T1 | **Private** | Exclude whole card: sensitive terminal evidence cannot be changed by content edit. Identifies the actual remote scheduler host and production job arrangement. |
| CARD-0136 | O | D21-30,44-51 | **Notes** | Move the cited sensitive context to PrivateNotes; retain a neutral technical restatement. Historical real provider account usage/reset measurements embedded in polling investigation. |
| CARD-0141 | O | D7,20-29 | **Notes** | Move the cited sensitive context to PrivateNotes; retain a neutral technical restatement. Specific remaining subscription balance/reset evidence and account reset interaction. |
| CARD-0144 | P,N,F,I | D5,13-14,39 | **Notes** | Move the cited sensitive context to PrivateNotes; retain a neutral technical restatement. Browser profile/account layout, actual remote conversation identifier, housekeeping host and named family/property bridges. |
| CARD-0145 | P,N,I | D4-8,12 | **Notes** | Move the cited sensitive context to PrivateNotes; retain a neutral technical restatement. Actual remote conversation IDs with contents/activity descriptions and sibling checkout/workspace paths. |
| CARD-0146 | O | D20,34 | **Notes** | Move the cited sensitive context to PrivateNotes; retain a neutral technical restatement. Specific historical provider quota workaround recorded alongside general pipeline design. |
| CARD-0150 | N,O | D33,44,91 | **Notes** | Move the cited sensitive context to PrivateNotes; retain a neutral technical restatement. Actual live broker lacks SASL/ACLs by operator choice, tailnet trust boundary, and private package consumer. NuGet support address is public service contact, not third-party personal data. |
| CARD-0159 | P | D12 | **Accept** | Accept Antiphon's canonical checkout, service/log or generated worktree path; no account home or unrelated private project. |
| CARD-0166 | N,P,I | D26-33;T1 | **Private** | Exclude whole card: sensitive terminal evidence cannot be changed by content edit. Real scheduler user/job/host/cadence and checkout path; linked public smoke issue is not itself private. |
| CARD-0171 | N,F,I | D5-14,67;T1 | **Private** | Exclude whole card: sensitive terminal evidence cannot be changed by content edit. Family notification destination, private group identifier, scheduler identity/cadence and live binding configuration. |
| CARD-0173 | T,F | D13 | **Notes** | Move the cited sensitive context to PrivateNotes; retain a neutral technical restatement. Named family member used in the issue-notification example, with family audience context. |
| CARD-0175 | P | T1 | **Accept** | Accept Antiphon's canonical checkout, service/log or generated worktree path; no account home or unrelated private project. |
| CARD-0181 | P,N,I | D5,35-56,94,108 | **Private** | Exclude whole card: private activity/system inventory is integral to the evidence. Separate work-machine account name, private project/work directories and detailed session-sidecar mapping. Imported from a linked public issue; that does not establish permission to republish. |
| CARD-0182 | N | D2,7,31-36 | **Notes** | Move the cited sensitive context to PrivateNotes; retain a neutral technical restatement. Named custom corporate/local proxy and model/profile setup. |
| CARD-0183 | N | D5,14 | **Notes** | Move the cited sensitive context to PrivateNotes; retain a neutral technical restatement. Real work-project workspace and named subordinate project/agent inventory. |
| CARD-0184 | N,P,O | D2,5,10,17;T1 | **Private** | Exclude whole card: sensitive terminal evidence cannot be changed by content edit. Work-project names, proxy billing bucket/ID, sibling repositories and separate machine layout; key label is not key material. |
| CARD-0185 | N,F | D3-9,23,38;T1 | **Private** | Exclude whole card: sensitive terminal evidence cannot be changed by content edit. Actual live broker hostname/port, tailnet restriction, family messaging route and active consumer configuration. |
| CARD-0186 | N | D11 | **Notes** | Move the cited sensitive context to PrivateNotes; retain a neutral technical restatement. Names real work-project agents as the motivating deployment. |
| CARD-0187 | N | D27,44,65 | **Notes** | Move the cited sensitive context to PrivateNotes; retain a neutral technical restatement. Real work-project layouts and custom proxy-launcher conventions. |
| CARD-0188 | N | D7,25 | **Notes** | Move the cited sensitive context to PrivateNotes; retain a neutral technical restatement. Identifies the work-project orchestrator and prior session incarnation. |
| CARD-0191 | T,F | D27,38-45;T1 | **Private** | Exclude whole card: sensitive terminal evidence cannot be changed by content edit. Private restaurant-review conversation names the server and venue/city. Customer-visit detail; no inference that the named server is family. |
| CARD-0195 | P | D6 | **Accept** | Accept Antiphon's canonical checkout, service/log or generated worktree path; no account home or unrelated private project. |
| CARD-0204 | P | D5 | **Accept** | Accept Antiphon's canonical checkout, service/log or generated worktree path; no account home or unrelated private project. |
| CARD-0210 | P,N | D24-26;T14 | **Private** | Exclude whole card: sensitive terminal evidence cannot be changed by content edit. Live sibling-project setup and real checkout path-normalization example. |
| CARD-0211 | N | D25-29;T34 | **Private** | Exclude whole card: sensitive terminal evidence cannot be changed by content edit. Actual work-project pane/agent mapping and cross-project live fleet inventory. |
| CARD-0212 | N | T32 | **Private** | Exclude whole card: sensitive terminal evidence cannot be changed by content edit. Named private-project worker cohort altered in rollout evidence. |
| CARD-0213 | N,P,F | D9-16,69;T32 | **Private** | Exclude whole card: sensitive terminal evidence cannot be changed by content edit. Work repository paths and pane/agent IDs; names family/property agents sharing restart exposure. |
| CARD-0216 | P,N | D78-79,260-265,283;T9 | **Private** | Exclude whole card: sensitive terminal evidence cannot be changed by content edit. Real checkout/sibling directories, remote access hostname and obsolete private-project agent inventory. |
| CARD-0218 | P,N | D9;T29-31 | **Private** | Exclude whole card: sensitive terminal evidence cannot be changed by content edit. Actual sibling-project checkout and board-scoped card example. |
| CARD-0220 | P | D25 | **Accept** | Accept Antiphon's canonical checkout, service/log or generated worktree path; no account home or unrelated private project. |
| CARD-0221 | N,F,I | D120-121;T24-25,36 | **Private** | Exclude whole card: sensitive terminal evidence cannot be changed by content edit. Live cross-project/family fleet census and named scheduler user/job/host. |
| CARD-0224 | N | D11,16,50 | **Notes** | Move the cited sensitive context to PrivateNotes; retain a neutral technical restatement. Actual work-project workspace and custom restart/profile convention. |
| CARD-0225 | N | D3,9,18;T34 | **Private** | Exclude whole card: sensitive terminal evidence cannot be changed by content edit. Actual work-project agent/profile mapping and cross-project rollout inventory. |
| CARD-0227 | P | D11 | **Accept** | Accept Antiphon's canonical checkout, service/log or generated worktree path; no account home or unrelated private project. |
| CARD-0232 | N,I | D7,24-26 | **Notes** | Move the cited sensitive context to PrivateNotes; retain a neutral technical restatement. Specific remote scheduler job/user and host arrangement. |
| CARD-0233 | T,F,I,N | D1-2,14-18;T7,13,51 | **Private** | Exclude whole card: sensitive terminal evidence cannot be changed by content edit. Real property-management request names a tradesperson/contact and certificate work, repeats private group ID and reply fragments. No street address or quote amount found. |
| CARD-0234 | P | D10-16 | **Accept** | Accept Antiphon's canonical checkout, service/log or generated worktree path; no account home or unrelated private project. |
| CARD-0237 | N,O | D10,43;T3 | **Private** | Exclude whole card: sensitive terminal evidence cannot be changed by content edit. Actual account-maintenance scheduling and credential-file dependency, without credential content. |
| CARD-0239 | N,P | D1-3,25,32 | **Private** | Exclude whole card: private activity/system inventory is integral to the evidence. Private sibling-project agent census, policies and checkout path. |
| CARD-0240 | N | D1-3,21,34,54-55 | **Private** | Exclude whole card: private activity/system inventory is integral to the evidence. Named private-project work items/agents and session incident inventory. |
| CARD-0245 | N,I,P | D3,5,16,46,77 | **Private** | Exclude whole card: private activity/system inventory is integral to the evidence. Private Slack DM/thread ID, quoted work request, separate machine log root and work-agent auth failure. |
| CARD-0248 | N | T34 | **Private** | Exclude whole card: sensitive terminal evidence cannot be changed by content edit. Names a private education-project investigation among live parallel work. |
| CARD-0250 | N,P,I | D7,14 | **Private** | Exclude whole card: private activity/system inventory is integral to the evidence. Named work project/spec artifact and its absolute path, tied to a private Slack request/operator identity. |
| CARD-0251 | P,N | D45,58-59,96;T11 | **Private** | Exclude whole card: sensitive terminal evidence cannot be changed by content edit. Real checkout/sibling tooling paths and private-project migration status. |
| CARD-0253 | P | D8 | **Accept** | Accept Antiphon's canonical checkout, service/log or generated worktree path; no account home or unrelated private project. |
| CARD-0255 | N,F,P | D7,19,33;T8 | **Private** | Exclude whole card: sensitive terminal evidence cannot be changed by content edit. Private education-project agent policy/configuration and checkout/session identifiers. |
| CARD-0256 | N,P | D5,11-14,36 | **Private** | Exclude whole card: private activity/system inventory is integral to the evidence. Work-project deliverable/epic and absolute worktree/sibling-repository locations. |
| CARD-0257 | N,F,I | D14-25,119-127 | **Private** | Exclude whole card: private activity/system inventory is integral to the evidence. Private project cross-session message, IPC endpoint, real channel inventory and notification settings. |
| CARD-0259 | N,P,O | D5,11-19,42,46 | **Private** | Exclude whole card: private activity/system inventory is integral to the evidence. Named work projects and relationships, key alias/billing mapping, internal proxy setup and worktree paths; no key value. |
| CARD-0260 | N,P,O | D3-5,13-30,64-75 | **Private** | Exclude whole card: private activity/system inventory is integral to the evidence. Actual work-project task titles, account/billing routing miss, private repository paths and proxy configuration; key names only. |
| CARD-0262 | N,O | D1-3,51,57 | **Notes** | Move the cited sensitive context to PrivateNotes; retain a neutral technical restatement. Operator Slack preference tied to private-project KB row and pilot deployments. |
| CARD-0264 | O | D45 | **Notes** | Move the cited sensitive context to PrivateNotes; retain a neutral technical restatement. Historical provider account usage percentage, presented as a hypothesis rather than verified cause. |
| CARD-0265 | N,F | D4-7,16;T1-2 | **Private** | Exclude whole card: sensitive terminal evidence cannot be changed by content edit. Actual remote gateway/database/container migration and family messaging deployment. |
| CARD-0270 | N,F | D2;T13,24,29 | **Private** | Exclude whole card: sensitive terminal evidence cannot be changed by content edit. Actual remote gateway deployment target and family-channel role. |
| CARD-0273 | P | D4-9,18-20 | **Accept** | Accept Antiphon's canonical checkout, service/log or generated worktree path; no account home or unrelated private project. |
| CARD-0281 | N,O | D3,15 | **Notes** | Move the cited sensitive context to PrivateNotes; retain a neutral technical restatement. Named work-project team credit/spending-limit incident and live provider switch. |
| CARD-0283 | N,P | D12-16,21,26 | **Private** | Exclude whole card: private activity/system inventory is integral to the evidence. Private-project work item/agent plus real Windows username, executable and log locations. |
| CARD-0284 | N,F | D1-2,12;T26 | **Private** | Exclude whole card: sensitive terminal evidence cannot be changed by content edit. Private education-project migration and live workflow inventory. |
| CARD-0286 | N | D7 | **Notes** | Move the cited sensitive context to PrivateNotes; retain a neutral technical restatement. Names a work-project feature and implementation assignment. |
| CARD-0287 | N | D7,15 | **Notes** | Move the cited sensitive context to PrivateNotes; retain a neutral technical restatement. Names the private-project work item/orchestrator motivating the follow-up. |
| CARD-0288 | P | D67 | **Notes** | Move the cited sensitive context to PrivateNotes; retain a neutral technical restatement. Actual Windows account and native session-transcript directory. |
| CARD-0289 | O | D10 | **Accept** | Accept model/reasoning configuration preferences; these disclose no credentials, balances or private workload. |
| CARD-0290 | O | D3,8 | **Notes** | Move the cited sensitive context to PrivateNotes; retain a neutral technical restatement. Operator account exhaustion and actual monitoring configuration state. |
| CARD-0291 | N | D3-8 | **Private** | Exclude whole card: private activity/system inventory is integral to the evidence. Private-project feature/task inventory, session IDs, timeline and status conversation. |
| CARD-0292 | P,I | D34,55,94 | **Notes** | Move the cited sensitive context to PrivateNotes; retain a neutral technical restatement. Actual remote session URL and Windows username/transcript path. URL is an identifier, not proven bearer access. |
| CARD-0293 | N,O | D3,15-24,65-68 | **Private** | Exclude whole card: private activity/system inventory is integral to the evidence. Operator-attributed private-project agent/feature inventory and remote-control settings. |
| CARD-0294 | N,O | D3,9-13 | **Private** | Exclude whole card: private activity/system inventory is integral to the evidence. Private work-project epic/agent and operator's sequential-work authorization timeline. |
| CARD-0295 | N,O | D3,10-14;T4-6 | **Private** | Exclude whole card: sensitive terminal evidence cannot be changed by content edit. Operator-attributed cleanup request and detailed private-project agent/board inventory. |
| CARD-0296 | O | D3 | **Accept** | Accept operator first-name attribution in ordinary product discussion; no full identity, contact detail or personal activity. |
| CARD-0298 | N,O | D1,6-12 | **Private** | Exclude whole card: private activity/system inventory is integral to the evidence. Operator-attributed deployment choice and actual account-maintenance host/credential dependency. |
| CARD-0299 | P | D46 | **Accept** | Accept Antiphon's canonical checkout, service/log or generated worktree path; no account home or unrelated private project. |
| CARD-0301 | O | D40 | **Accept** | Accept operator first-name attribution in ordinary product discussion; no full identity, contact detail or personal activity. |
| CARD-0302 | N,F | D30 | **Notes** | Move the cited sensitive context to PrivateNotes; retain a neutral technical restatement. Private education-project task/card/commit and seeding status. |
| CARD-0303 | O | D1 | **Accept** | Accept operator first-name attribution in ordinary product discussion; no full identity, contact detail or personal activity. |
| CARD-0306 | N,P,I | D3-6,26 | **Private** | Exclude whole card: private activity/system inventory is integral to the evidence. Private-project task/remote-control incident and real Windows account/transcript layout. |
| CARD-0308 | N | D3-5,49 | **Notes** | Move the cited sensitive context to PrivateNotes; retain a neutral technical restatement. Private sibling-project incident and board reference. |
| CARD-0311 | N,F | D17 | **Notes** | Move the cited sensitive context to PrivateNotes; retain a neutral technical restatement. Private education-project agent condition/context measurement. |
| CARD-0312 | N,F | D14 | **Notes** | Move the cited sensitive context to PrivateNotes; retain a neutral technical restatement. Private education-project agent condition in live liveness evidence. |
| CARD-0313 | T,F,I,N | D1,5-18 | **Private** | Exclude whole card: private activity/system inventory is integral to the evidence. Named family sender, operator request, private Telegram group ID and gateway delivery timeline. |
| CARD-0314 | P | D1 | **Accept** | Accept Antiphon's canonical checkout, service/log or generated worktree path; no account home or unrelated private project. |
| CARD-0315 | N,P,F | D16,28-29,45,59;T7-8 | **Private** | Exclude whole card: sensitive terminal evidence cannot be changed by content edit. Actual trusted checkout inventory, education project and Windows account/executable layout. |
| CARD-0323 | N,P | D5-7,15-25,59 | **Private** | Exclude whole card: private activity/system inventory is integral to the evidence. Work-project/workspace/agent inventory, project IDs and absolute directory. |
| CARD-0324 | N,O | D3;T1 | **Private** | Exclude whole card: sensitive terminal evidence cannot be changed by content edit. Actual operator authentication-store absence and custom standing profile; no authentication material. |
| CARD-0327 | O | D4,16;T1 | **Private** | Exclude whole card: sensitive terminal evidence cannot be changed by content edit. Explicit mapping of public issue login to operator's name and live workflow setting. Not third-party PII. |
| CARD-0328 | N | D40 | **Notes** | Move the cited sensitive context to PrivateNotes; retain a neutral technical restatement. Names private sibling project in land-verification follow-up. |
| CARD-0335 | N,O | D3 | **Notes** | Move the cited sensitive context to PrivateNotes; retain a neutral technical restatement. Private sibling-project source of operator provider-hold incident. |
| CARD-0337 | N,P,I | D3,5,13,40 | **Private** | Exclude whole card: private activity/system inventory is integral to the evidence. Private Slack thread ID/operator quote, work-spec title/branch and worktree source paths. |
| CARD-0338 | N,I,O | D3,23,46,71,76 | **Private** | Exclude whole card: private activity/system inventory is integral to the evidence. Private Slack thread ID, named work agents, author identity and communication activity/settings. |
| CARD-0340 | P | D15 | **Accept** | Accept Antiphon's canonical checkout, service/log or generated worktree path; no account home or unrelated private project. |
| CARD-0341 | N,P,O | D5-22,46,54-55 | **Private** | Exclude whole card: private activity/system inventory is integral to the evidence. Actual corporate proxy routes, work-project roster, process/session data, configuration and filesystem layout; stated dummy keys are not secrets. |
| CARD-0343 | N | D3,7 | **Notes** | Move the cited sensitive context to PrivateNotes; retain a neutral technical restatement. Operator's separate local launcher/tooling project and installed profile configuration. |
| CARD-0344 | N,P | D12,38;T3,6 | **Private** | Exclude whole card: sensitive terminal evidence cannot be changed by content edit. Separate operator tooling/install arrangement and local source-search root. |
| CARD-0345 | N | D8,19 | **Notes** | Move the cited sensitive context to PrivateNotes; retain a neutral technical restatement. Actual work-project name used in profile/env argument examples. |
| CARD-0348 | N | D1,27,40 | **Notes** | Move the cited sensitive context to PrivateNotes; retain a neutral technical restatement. Private-project task incidents and statuses. |
| CARD-0354 | N,P | D2-3,10,25 | **Notes** | Move the cited sensitive context to PrivateNotes; retain a neutral technical restatement. Sibling agent inventory, Windows account and shared transcript/checkout paths. |
| CARD-0357 | I,O | D11,16 | **Notes** | Move the cited sensitive context to PrivateNotes; retain a neutral technical restatement. Operator name and personal email in commit-identity evidence; not an unknown third party. |
| CARD-0358 | P | D5,9;T1 | **Accept** | Accept Antiphon's canonical checkout, service/log or generated worktree path; no account home or unrelated private project. |
| CARD-0361 | N | D3,35-36 | **Notes** | Move the cited sensitive context to PrivateNotes; retain a neutral technical restatement. Actual local proxy port/path and two private DM failures, though no personal names or channel IDs. |
| CARD-0363 | O | D5,13 | **Notes** | Move the cited sensitive context to PrivateNotes; retain a neutral technical restatement. Real tracker credential alias/configuration and account rate-limit measurement; no token value. |
| CARD-0365 | N | D19 | **Notes** | Move the cited sensitive context to PrivateNotes; retain a neutral technical restatement. Actual remote scheduler precedent/host referenced as a deployment option. |
| CARD-0366 | N,F | D1,15;T1 | **Private** | Exclude whole card: sensitive terminal evidence cannot be changed by content edit. Names private fitness/education projects and live cross-project provider/concurrency incident. |
| CARD-0367 | N | D12,89-90 | **Notes** | Move the cited sensitive context to PrivateNotes; retain a neutral technical restatement. Private-project task/concurrency incident and board context. |
| CARD-0370 | N | D69 | **Notes** | Move the cited sensitive context to PrivateNotes; retain a neutral technical restatement. Private sibling-project provenance for the serialization incident. |
| CARD-0372 | O | D5 | **Notes** | Move the cited sensitive context to PrivateNotes; retain a neutral technical restatement. Operator full name and dated private-channel quotation about review preferences. |
| CARD-0378 | P | D2,21 | **Accept** | Accept Antiphon's canonical checkout, service/log or generated worktree path; no account home or unrelated private project. |
| CARD-0380 | N | D19 | **Notes** | Move the cited sensitive context to PrivateNotes; retain a neutral technical restatement. Actual remote scheduler/SSH job arrangement; description's private-repo premise is stale for this public repo. |
| CARD-0382 | N | D18 | **Accept** | Accept wrapper filename and the fact that it is deployment-specific; no private hostname, account or workload is identified. |
| CARD-0384 | N | T13 | **Private** | Exclude whole card: sensitive terminal evidence cannot be changed by content edit. Actual work-project standing agent ID and intended workspace/tab mapping. |
| CARD-0388 | N,P | D3,14-20;T13 | **Private** | Exclude whole card: sensitive terminal evidence cannot be changed by content edit. Work-project directory, pane/workspace layout, agent identities and shared Slack ownership incident. |
| CARD-0389 | N,P,O | D5,11-20;T1 | **Private** | Exclude whole card: sensitive terminal evidence cannot be changed by content edit. Work-project proxy hold/session/key-label mapping, internal account classification and directories; key ID/label, not secret value. |
| CARD-0390 | P | D1,12-13 | **Accept** | Accept Antiphon's canonical checkout, service/log or generated worktree path; no account home or unrelated private project. |
| CARD-0397 | N,F | T5 | **Private** | Exclude whole card: sensitive terminal evidence cannot be changed by content edit. Names real family/fitness deployments in review context. |
| CARD-0400 | O | D1-3 | **Accept** | Accept model/reasoning configuration preferences; these disclose no credentials, balances or private workload. |
| CARD-0401 | O | D3 | **Notes** | Move the cited sensitive context to PrivateNotes; retain a neutral technical restatement. Actual operator provider-hold condition motivating investigation. |
| CARD-0407 | N,O | D1,3,25-26 | **Private** | Exclude whole card: private activity/system inventory is integral to the evidence. Private-project production deployment/backup and QA credential-work status; no credential value. |
| CARD-0408 | N,F,P | D7-17,106-108 | **Notes** | Move the cited sensitive context to PrivateNotes; retain a neutral technical restatement. Club correspondence privacy incident and volunteer role, public sibling checkout plus named private project classifications. No volunteer email value present here. |
| CARD-0409 | N,F,P | D3,14-15 | **Notes** | Move the cited sensitive context to PrivateNotes; retain a neutral technical restatement. Actual checkout and other projects' privacy/export inventory; original file-count premise is now superseded. |
| CARD-0410 | N,F,P | D3-5;T1 | **Private** | Exclude whole card: sensitive terminal evidence cannot be changed by content edit. Property-assistant agent/directory and real invoice-answer incident; no invoice amount/content, but context is private operational activity. |
| CARD-0413 | N,F | D19-23 | **Notes** | Move the cited sensitive context to PrivateNotes; retain a neutral technical restatement. Actual remote gateway deployment host/port and family test-group procedure. |
| CARD-0415 | O,P | D3,9 | **Notes** | Move the cited sensitive context to PrivateNotes; retain a neutral technical restatement. Operator provider wall and actual encoded specialist transcript location. |
| CARD-0417 | N,F | D4-6 | **Private** | Exclude whole card: private activity/system inventory is integral to the evidence. Live family/property/club/education/fitness project roster and deployed instruction snippets. |
| CARD-0418 | N | D9,48 | **Notes** | Move the cited sensitive context to PrivateNotes; retain a neutral technical restatement. Actual work-project/spec/private Slack agent context for PDF opt-in. |
