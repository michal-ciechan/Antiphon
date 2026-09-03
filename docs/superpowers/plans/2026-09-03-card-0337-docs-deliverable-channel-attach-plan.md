# CARD-0337 — a document task's Done must put the PDF and the sources in the chat, not a SHA

**Plan pass, 2026-09-03. Sources verified at `42daeceb`: `ChannelContracts`, `ChannelPreamble`,
`ChannelReplyDispatcher` (`PrepareReplyBody`, `DispatchMachineTurnAttachmentsAsync`),
`ChannelBridgeSettings`, `AgentTaskReplyService` (`ResolveDeliverableAsync`, completion-note
enqueue), `DelegationReportFormatter.BuildCompletionNote`, `AgentTaskLandService.DeliverAsync`,
`DelegationGitFacts`, `GitWorkspaceService`, `ChatChannelService.SendAsync`, `AgentIncidentKind`,
`server/Bundles/orchestrator.md`, `AgentWorkspaceProvisioner`, the CARD-0250 and CARD-0262 plans,
GitHub issues #30 (CARD-0338) and #31 (this card). No production code is changed by this plan.**

## 1. Verdict up front

1. **The live miss has three causes stacked, and only the bottom one is new.** (a) The
   orchestrator's `[task done]` turn is not delivered unless it carries `[[attach:]]` — CARD-0250's
   deliberate boundary, and the whole of #30/CARD-0338. (b) Even a delivered turn that names
   `7bd8eba0` and four `docs/features/…md` paths is prose: `ChannelReplyDispatcher` only turns a
   marker line into bytes (`PrepareReplyBody`, `:801`), and the delegate's own report reaches the
   orchestrator as a `UserPrompt`, from which nothing is ever extracted. (c) **Antiphon has no
   document renderer at all** — `grep -ri pdf` over `server/`, `src/`, `scripts/` finds only the
   marker text, MIME table and test fixtures. Every PDF that has ever reached Slack was hand-built
   by an agent with whatever tool it found. The orchestrator on the mav-ref machine had no PDF to
   attach even if it had remembered the marker.
2. **This is Antiphon-side, not project-side.** The mav-ref (PredictionMarkets) instance runs the
   same build (`7f8b0e37`, per the card) on another machine; the fix ships in the server and
   reaches it on its next deploy. Nothing in the mav-ref repo, its KB (`f5792203`, a foreign
   project store — CARD-0262 §1) or its orchestrator workspace needs to change. The one host
   requirement is a Chromium-family browser for PDF printing; every Windows host has Edge
   (`C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe`), verified here, Chrome also
   present.
3. **Chosen mechanism — the harness owns the document, the orchestrator's turn is the vehicle,
   the dispatcher is the guarantee (§4):**
   - **S1** At settlement, a document-producing task gets a **deliverable bundle** rendered by
     the server: one PDF (Markdig → HTML → headless Edge `--print-to-pdf`) plus the source `.md`
     files (individually when ≤ 5, else one zip), written under
     `<RepoPath>\.antiphon\deliverables\<taskShort>\` and recorded on the `AgentTask` row.
   - **S2** The completion note gains a `deliverable=` header bit and a trailing
     ready-to-copy block of `[[attach:]]` lines — the model's job becomes copy-paste.
   - **S3** `DispatchMachineTurnAttachmentsAsync` learns **implied attachments**: when the
     orchestrator's `[task … done]` turn ends and that task has an undelivered bundle, the bundle
     goes out with that turn — text and files, same conversation, same turn — whether or not the
     orchestrator re-emitted the markers. An exact `NO_REPLY` turn holds it. That is the answer to
     the card's point 1 ("same channel-delivered turn"), and it makes a docs task's `[task done]`
     turn deliverable as a side effect, which is the docs-shaped half of #30.
   - **S4** Two marker extensions, no new command: `[[attach: <directory>]]` zips the folder and
     `[[attach-pdf: <file.md | directory>]]` renders Markdown to PDF at dispatch. Both ride the
     existing 14 MB budget. That is the answer to point 2: a channel-bound agent on any machine can
     attach a folder or produce a PDF with no tooling of its own.
   - **S5** Point 3 (refuse a docs card's Done notification without an attach) is answered **no
     refusal, a surfaced guarantee instead**: a refusal at the channel is silence, the exact
     failure Mike hit. S3 sends the files; where it cannot (render failed, over cap, held by
     `NO_REPLY`), the text still goes with a ⚠️ line naming what was not attached, a new incident
     `DeliverableUndelivered = 46` is raised, and a card moved to Done while a bound task's bundle
     is still undelivered raises the same incident plus a WhenIdle note to the orchestrator
     carrying the attach lines. Never blocks the move, never withholds the text.
4. **Size cap stays the existing 14 MB cumulative per turn** (`ChannelBridgeSettings.MaxAttachmentBytes`,
   3/4 of the 20 MB Kafka cap after base64). Bundle order inside the budget: PDF first, then
   sources. An over-cap file is skipped with the existing ⚠️ note; nothing is silently dropped.
5. **Rejected:** a `POST /api/channels/{id}/send` file megaphone (CARD-0171, unchanged); a
   server-side push at settlement independent of the orchestrator's turn (§3 B); a PDF-only
   refusal of HTML (CARD-0250 §6 stands); PuppeteerSharp/QuestPDF (§4.1).

## 2. Verified current-code facts

- `ChannelContracts.AttachMarkerFormat` = `[[attach: <absolute file path>]]`; the regex takes
  one path per line, verbatim, unvalidated (`ChannelContracts.cs:26-49`). `PrepareReplyBody`
  (`ChannelReplyDispatcher.cs:801-851`) turns each into an `OutboundAttachment` with inline bytes,
  skipping missing/invalid/over-budget paths with a ⚠️ note appended to the text. `FileInfo` on a
  directory path reports `Exists == false`, so `[[attach: <folder>]]` today yields
  "⚠️ attachment not found".
- `DispatchMachineTurnAttachmentsAsync` (`:988-1112`, CARD-0250) gates: channel-bound session
  (newest Channel-origin `ConversationKey`); owning prompt matched no Channel row; owning prompt
  matches a `Delegation | Check | System` queued row that is `Sent` and unclaimed; the turn's
  AssistantText has ≥ 1 marker. Claim-before-produce reuses the row's `ChannelReplySettledAt`.
  An exact `NO_REPLY` with markers sends with empty text. Turns with no marker return before any
  claim — so a `[task done]` turn that only names a SHA is never delivered.
- The completion note is enqueued with `Origin = Delegation`, `ConversationKey = task:{RootTaskId:N}`,
  **`SourceTaskId = task.Id`**, a content digest and the header (`AgentTaskReplyService.cs:1501-1504`;
  `SessionQueuedMessage.SourceTaskId` at `:34`). The queued row therefore already links the
  orchestrator's `[task done]` turn back to the settled task — S3 needs no new column for that.
- `AgentTask.DeliverablePath / DeliverableRef` (`AgentTask.cs:190-194`) is the **first**
  `docs/[\w./-]+\.md` match in the immutable report that exists on disk or on the worktree branch
  (`ResolveDeliverableAsync`, `AgentTaskReplyService.cs:2004-2050`, CARD-0230). It feeds the
  pipeline view (`IsVerifiedPlanDeliverable`, `docs/superpowers/plans/` only). In the live case the
  cleanup task's report named four `docs/features/…md` paths — this regex would have caught the
  first; it never fed anything channel-facing.
- Settlement git facts: `DelegationGitFacts.ResolveBase` = `MergeTargetRef ?? WorktreeBaseSha`;
  `GitWorkspaceService.GetChangesSinceAsync(dir, base)` = `git diff --name-status -z --find-renames`;
  `GetContentAtAsync(repo, path, ref)` reads a file at a ref (`GitWorkspaceService.cs:112, 174`).
  `CodeProducingRoles` includes `Docs`; `Plan` is not in it (`DelegationGitFacts.cs:11-19`).
- Land: `AgentTaskLandService.DeliverAsync` (`:458-470`) enqueues the outcome line
  (`landed <branch> -> <target> as <sha>, pushed …`) as a `Delegation` row with
  `ConversationKey = land:{id}` and **no `SourceTaskId`**. Worktree removal happens at land, so a
  bundle written into the worktree would vanish before a Done-time attach; hence `RepoPath`.
- `.antiphon/` is gitignored (`.gitignore:48`); delegates already spill reports to
  `.antiphon/task-<short>.md` there.
- `BuildCompletionNote` header bits are `title · tier · duration · $cost · workspaceNote ·
  overlapping-running= · drift= · report= · git=` (`DelegationReportFormatter.cs:351-386`).
- Card Done is a human/orchestrator move (`card.ps1 close` / `move -To Done`, `CardService`);
  "Nothing automates Review → Done" (`docs/orchestration-loop.md:401`). There is no
  card-status → channel notification of any kind; the orchestrator's own turn is the only path.
- Server-composed channel sends exist (`ChatChannelService.SendAsync`, `AwayDigestNotifier`,
  `ChannelAlertRouter`) but none carries attachments today; `ChannelReply.Attachments` does.
- Renderer inventory: `Markdig`, `Microsoft.Playwright`, `PuppeteerSharp`, `QuestPDF` appear in no
  server/src csproj; Playwright is `tests/Antiphon.E2E` only. The client renders Markdown with
  `react-markdown` + `remark-gfm` (client-side, not reusable by the server). `System.IO.Compression`
  is already used (`DiagnosticsBundleService.cs:79`). Server runs processes with a bounded
  `RunProcessAsync` (`AgentTaskLandService.cs:495`).
- `AgentIncidentKind` next free value is **46** (`ProviderSignInRequired = 45`).
- Instruction text today: `orchestrator.md:53-61` (re-emit `[[attach:]]`; prefer PDF),
  `ChannelPreamble.BuildPreset` (`:74-76`), `AgentWorkspaceProvisioner` channel section
  (`:256-265`) — all say "attach a PDF" and none say where a PDF comes from.

## 3. Designs considered

**A — instruction-only ("orchestrator: always render and attach a PDF of the spec").** Rejected
as the primary fix. It is what CARD-0250 §7 and CARD-0262's preamble line already say, and the
2026-09-03 miss happened with that text live. A Grok orchestrator with no PDF tool on its host
cannot comply, and a rule the harness cannot enforce is how this class of miss recurs.

**B — server pushes the bundle to the bound conversation at settlement, independent of the
orchestrator's turn.** Rejected. It arrives *before* the orchestrator's narrative, so the human
gets a PDF with no sentence saying what it is; it duplicates when the orchestrator does re-emit
markers; and it bypasses the one control the orchestrator legitimately has — reading the report
first and sending a wrong spec back for rework without showing it. It also needs a new "which
conversation" resolution outside the dispatcher's matching, re-opening the stray-reply risk
CARD-0233 closed.

**C — extend the machine-turn follow-up with implied attachments (chosen).** The dispatcher
already knows the exact moment (the `[task done]` turn's end), the exact conversation (the newest
Channel-origin key), the exact task (`SourceTaskId` on the matched Delegation row) and has the
idempotency marker. Adding "this task's bundle, unless already attached or held" is additive,
touches no Channel-row settlement, and delivers text and files in one turn. Its one gap — a
sub-orchestrator whose *root* is channel-bound — is noted in §9.

**D — new CLI/API "attach file/folder" command.** Rejected in favour of marker extensions. A
command needs the agent to know a channel id and a route; the marker is the contract every
channel-bound agent already carries, the dispatcher already resolves paths, and CARD-0171 already
rejected a channel megaphone. Directory and `attach-pdf` markers are two regex alternations, not
a new surface.

**E — refuse the Done/notification without an attach marker.** Rejected: a refusal at the
channel is exactly the silence the card reports, and a refusal at `card.ps1 close` blocks a human
move on a property of a different agent's turn. Replaced by S3's guarantee plus S5's incident and
note (§4.5).

## 4. Design

### 4.1 S1 — `DeliverableBundleService` and `MarkdownPdfRenderer` (settlement)

**Trigger** (in `AgentTaskReplyService` settlement, immediately after `ResolveDeliverableAsync`,
same never-abort contract as CARD-0230): the task settled `Succeeded` **and** is document-producing:

- `Role ∈ {Plan, Docs}`, or
- the report names ≥ 1 `docs/**/*.md` that exists (today's `DeliverablePathPattern`, **all**
  matches, not just the first — the live case's Custom-role cleanup task is this shape), or
- Worktree task whose range diff (`GetChangesSinceAsync(WorktreePath, ResolveBase(task))`) is
  `A`/`M` entries that are **all** `.md` (a docs-only change by a Code task).

**Document set**, deduplicated, in report order then diff order: report-named paths ∪ (Worktree:
`.md` files added/modified in the range). Exclusions: `docs/cards/**` (generated, CARD-0004),
`.antiphon/**`, and anything not under the repo. A mixed code+docs Code task with no report-named
doc gets **no** bundle: code is reviewed on the board, not the phone.

**Content source**: `WorktreePath` (still present at settlement — cleanup is the land's job) or
`WorkingDirectory` on disk; fallback `GetContentAtAsync(RepoPath, path, WorktreeBranch)`.

**Output** under `<RepoPath ?? WorkingDirectory>\.antiphon\deliverables\<taskShort>\`:

| File | Content |
|---|---|
| `<CARD-nnnn or taskShort>-<slug>.pdf` | Cover line (card identifier + title, task short id, SHA when known, UTC date); one section per document with its repo-relative path as the H1 and a page break between; A4, `no-pdf-header-footer`. `slug` = the first document's parent directory name (`001-kalshi-ref-data-downloader`). |
| the source `.md` files, original names | Copied when ≤ 5 files; |
| `<same-stem>-sources.zip` | when > 5 files, or when any single source exceeds 1 MB. Relative paths preserved inside the zip. |
| `render.log` | renderer stdout/stderr and timings; never attached. |

**Renderer**: `Markdig` (MIT, one NuGet, `UseAdvancedExtensions()` for GFM tables, task lists,
autolinks, footnotes) → a self-contained HTML file with an embedded print stylesheet (system
font stack, `pre { white-space: pre-wrap }`, table borders, `@page { size: A4; margin: 18mm }`)
→ `msedge.exe --headless=new --disable-gpu --no-pdf-header-footer --print-to-pdf=<pdf>
file:///<html>` via the existing bounded process runner; **20 s** timeout; Chrome as fallback;
`Deliverables:BrowserPath` override for hosts with neither in the default locations. Mermaid
fences render as code blocks (documented; not a goal). PuppeteerSharp (downloads a 150 MB
Chromium per host) and QuestPDF (licence-gated, no HTML layout) are rejected; the browser is
already on every Windows host Antiphon runs on.

**Failure policy**: renderer absent / timeout / non-zero exit → bundle still holds the sources,
`DeliverableRenderError` records why (≤ 300 chars), and the attach lines omit the PDF. Never
throws into settlement; one Warning log.

**New `AgentTask` columns** (one migration): `DeliverableBundleDir` (≤ 1000), `DeliverablePdfPath`
(≤ 1000), `DeliverableFileCount` (int), `DeliverableRenderError` (≤ 300), `DeliverableDeliveredAt`
(nullable). `DeliverablePath / DeliverableRef` stay as they are (pipeline view).

**Settings** `Deliverables` (new class): `Enabled` (true), `BrowserPath` (null → auto-detect
Edge, then Chrome), `RenderTimeoutSeconds` (20), `MaxSourceFilesInline` (5), `MaxDocuments`
(40 — beyond that only the zip).

### 4.2 S2 — the completion note carries the attach lines

`BuildCompletionNote` gains a `deliverable` parameter rendered as a header bit
`deliverable=4 md, pdf 212 KB` (or `deliverable=4 md, pdf failed`), and the body gets a trailing
block, placed **after** the report and outside `FitReport` so excerpting can never remove it:

```
--- deliverable ---
To send these to your chat, copy the marker lines below into your reply as they are:
[[attach: C:\src\mav-ref\.antiphon\deliverables\3f4a6029\CARD-0002-001-kalshi-ref-data-downloader.pdf]]
[[attach: C:\src\mav-ref\.antiphon\deliverables\3f4a6029\01-requirements.md]]
…
```

The lines are the same paths S3 will attach on its own; a model that copies them produces the
same result and the dispatcher dedupes by full path. The land outcome note is **not** changed:
the bundle is made once at settlement, the landed SHA is prose in the land line as today.
CARD-0330's distiller already treats `[[attach:` as a must-keep anchor, so a distilled note keeps
this block.

### 4.3 S3 — implied attachments on the orchestrator's `[task done]` turn

In `DispatchMachineTurnAttachmentsAsync`, after the candidate Delegation/Check/System rows are
matched to the owning prompt and **before** the "no markers → return" check:

1. Collect `implied` = for each matched row with `Origin == Delegation` and `SourceTaskId != null`,
   the task's bundle files (PDF first, then sources/zip) where `DeliverableBundleDir != null` and
   `DeliverableDeliveredAt == null`.
2. If the turn's text is exactly `NO_REPLY` (`IsNoReply`) and it has no explicit markers →
   **hold**: return without claiming and without stamping. The orchestrator chose silence; S5's
   Done-time check catches a bundle that is still undelivered later.
3. If `explicit.Count == 0 && implied.Count == 0` → today's early return (no claim).
4. Otherwise proceed exactly as today with `paths = explicit ∪ implied` (dedupe by full path,
   explicit first so the orchestrator's own ordering wins), `PrepareReplyBody` applying the
   14 MB budget in that order. On a successful produce, stamp `DeliverableDeliveredAt = now` on
   every task whose bundle contributed ≥ 1 attachment; a task whose files were **all** skipped
   (over budget / missing) is not stamped and raises `DeliverableUndelivered` (§4.5). Un-claim on
   produce failure as today, leaving `DeliverableDeliveredAt` null.

Effect on the live sequence: the cleanup task settles → S1 renders the PDF + 4 `.md` → S2's note
reaches PM-Orchestrator-Grok → its turn "CARD-0002 is Done at 7bd8eba0…" ends → S3 finds the
Delegation row, the bundle, no explicit markers → Slack thread receives that sentence **plus** the
PDF and the four sources at 23:31, not a request for them at 06:05.

**What S3 must never do** (CARD-0250's never-weaken list, unchanged): settle, un-settle or match
any Channel-origin row; touch `PromptsMatch`/`Normalize`; publish a turn whose owning prompt
matched nothing machine-origin. Check/System rows contribute no implied attachments.

### 4.4 S4 — marker extensions for channel-bound agents

`ChannelContracts` grows one regex alternation and one flag on the extracted item:

- `[[attach: <absolute path>]]` — unchanged for files. When the path is an existing
  **directory**, `PrepareReplyBody` zips it to a temp file named `<dirname>.zip` (excluding
  `.git`, `node_modules`, `bin*`, `obj`, `.antiphon`, and files over the remaining budget, each
  exclusion listed in `render.log`-style text appended to the ⚠️ notes only when something was
  dropped) and attaches that.
- `[[attach-pdf: <absolute file.md | directory>]]` — renders the file, or every `.md` under the
  directory (sorted, recursive, same exclusions), to one PDF via `MarkdownPdfRenderer` and
  attaches it. A render failure becomes the existing ⚠️ note ("could not render … to PDF: …")
  and the source is attached instead, so the human still gets *something* readable.

`AttachMarkerFormat` and the preamble sentence are updated (S6). No new CLI, endpoint or
`delegate.ps1` switch. A delegate's `[[attach-pdf:]]` in its own report still reaches only its
caller as text (unchanged contract) — the caller gets S2's lines anyway.

### 4.5 S5 — surfacing, instead of refusing

New `AgentIncidentKind.DeliverableUndelivered = 46` (Warning), raised by:

- **S3** when a matched task's bundle contributed zero attachments (all over cap / missing) — the
  text still went, with the ⚠️ lines; `failureReason` names the files and sizes.
- **`CardService`** on a move/close into a terminal `Done` column: if any `AgentTask` with
  `CardId == card.Id` has `DeliverableBundleDir != null && DeliverableDeliveredAt == null` **and**
  the task's `ParentSessionId` belongs to an agent with an enabled `ChatChannels.AgentId` binding,
  raise the incident naming the card, and enqueue a `System`-origin WhenIdle note to that session:
  `"[System note from Antiphon: CARD-0002 is Done but its documents were never sent to slack
  "…". Reply with these lines to send them:" + the S2 block]`. Because that note is a System
  injection, the orchestrator's answering turn with the markers is delivered by the existing
  CARD-0250 path. The move itself is never blocked.

Dedupe: one incident per (task, reason). Surfaces on the attention feed like every other
incident; no new column, no alert sink.

### 4.6 S6 — instruction text (describes the code after S1–S5 ship, lands in the same build)

- `server/Bundles/orchestrator.md` channel-bound paragraph: replace "re-emit `[[attach:]]`
  yourself" guidance with: *"A `[task … done]` note for a task that produced documents ends with a
  `--- deliverable ---` block of `[[attach:]]` lines; Antiphon attaches those files to your reply
  to that note whether or not you copy them, unless your whole reply is `NO_REPLY`. To send any
  other folder or document, put `[[attach: <absolute folder>]]` (zipped) or
  `[[attach-pdf: <absolute .md file or folder>]]` (rendered to one PDF) on its own line. Naming a
  SHA or a path in prose sends nothing."*
- `ChannelPreamble.BuildPreset` attach paragraph: add the folder and `attach-pdf` sentence; the
  exact text is a compatibility contract, so `ChannelContractsTests` and the preamble endpoint
  tests update in the same slice.
- `AgentWorkspaceProvisioner` channel section: same two sentences.
- `DelegationReportFormatter.ReportingContract`: no change (one line already says the marker
  reaches only the caller).
- `docs/orchestration-loop.md`: a short "Documents reach the chat" paragraph under the
  channel-bound guidance pointing at this plan; `docs/messaging/slack-api-file-upload-brief.md`:
  note that Slack shows `.md` as a snippet (readable on a phone — intended for sources) and the
  PDF as a document.

## 5. Slices, tiers, verification

| Slice | Change | Tier | Verify (class filters, `--property:OutputPath=bin-c0337/`) |
|---|---|---|---|
| S1 | `Markdig` package; `MarkdownPdfRenderer`; `DeliverableBundleService`; `DeliverablesSettings`; 5 columns + migration; settlement hook | Coder (High) | `DeliverableBundleServiceTests` (new), `MarkdownPdfRendererTests` (new, `[ParallelLimiter<ProcessSpawnLimit>]`, skipped with a clear reason when no browser is found), `AgentTaskReplyIntegrationTests` |
| S2 | `BuildCompletionNote` `deliverable` bit + trailing block; enqueue site passes it | Coder (Medium) | `DelegationUnitTests` |
| S3 | implied attachments + `DeliverableDeliveredAt` stamping + hold on `NO_REPLY` | Coder (High) | `ChannelFollowUpAttachmentTests` (extend), `ChannelReplyDurabilityTests`, `ChannelBridgeTests` unchanged |
| S4 | directory zip + `attach-pdf` in `ChannelContracts`/`PrepareReplyBody` | Coder (Medium) | `ChannelContractsTests`, `ChannelBridgeTests` |
| S5 | incident 46; `CardService` Done check + System note | Coder (Medium) | `CardDoneDeliverableTests` (new, beside `CardCorrectionIntegrationTests`), `AgentSupervisionTests`-style incident query |
| S6 | three instruction texts + two docs | Coder (Low) | `InstructionBundleTests`, `ChannelContractsTests`, `AgentWorkspaceProvisionerTests` |

Order: S1 → S2 → S3 (the guarantee) → S4 → S5 → S6, S6 in the same build as S3 or later, never
before (text must describe shipped behaviour). S1 and S4 share `MarkdownPdfRenderer`; build S1
first.

### Tests to pin (red-first where marked)

1. **Live shape, red-first (S1+S2+S3):** Custom-role Worktree task whose report names four
   `docs/features/**.md` that exist in the worktree → settlement writes a PDF (skip-if-no-browser
   → assert sources only) and four `.md` copies under `RepoPath\.antiphon\deliverables\<short>\`;
   the enqueued note's body ends with the `--- deliverable ---` block; then the harness inserts the
   orchestrator's turn `"CARD-0002 is Done at 7bd8eba0"` with **no** markers → `OnTurnEndAsync` →
   exactly one reply to the bound conversation with that text and 5 attachments (or 4 without a
   browser), `DeliverableDeliveredAt` set. Fails today at the "no markers → return".
2. **Idempotent + restart-safe:** two more `OnTurnEndAsync` and one `Restarted(h)` → still one
   reply; a second task settling later gets its own reply.
3. **Explicit markers win and dedupe:** orchestrator turn re-emits the PDF line → 5 attachments,
   not 6; explicit order first.
4. **Hold:** turn is exactly `NO_REPLY` → nothing sent, nothing stamped, no incident; the later
   `CardService` Done move raises `DeliverableUndelivered` and enqueues the System note.
5. **Budget:** a 15 MB PDF + small sources → sources sent, PDF skipped with the ⚠️ line,
   `DeliverableDeliveredAt` set (something was delivered); all-over-cap → text sent, not stamped,
   incident 46.
6. **Detection:** Code task with a mixed `.cs` + `.md` diff and no report-named doc → no bundle;
   docs-only diff → bundle; `docs/cards/**` never included; Plan role with a report-named path
   on the branch only (worktree gone) → content via `GetContentAtAsync`.
7. **S4:** `[[attach: <dir>]]` → one zip named after the folder, `.git`/`bin-x` excluded;
   `[[attach-pdf: <dir>]]` → one PDF (skip-if-no-browser → the ⚠️ note plus sources); an
   `[[attach: <dir>]]` over budget → ⚠️ note, nothing sent for it.
8. **Renderer:** Markdig output for a GFM table + fenced code + task list; Edge invocation
   builds the exact argument list; timeout returns a failure, never throws; missing browser →
   `DeliverableRenderError` set, sources still bundled.
9. **Text pins:** the three instruction sources and `AttachMarkerFormat`.
10. **Never-weaken:** `ChannelReplyDurabilityTests`, `ChannelBridgeTests`, the existing
    `ChannelFollowUpAttachmentTests` pass unchanged.

## 6. Files to change

| File | Change |
|---|---|
| `server/Antiphon.Server.csproj` | `Markdig` package |
| `server/Application/Services/MarkdownPdfRenderer.cs` (new) | Markdig → HTML → headless Edge/Chrome → PDF; bounded; browser auto-detect |
| `server/Application/Services/DeliverableBundleService.cs` (new) | document detection, copy/zip, PDF, `render.log`, task columns |
| `server/Application/Settings/DeliverablesSettings.cs` (new) | §4.1 settings |
| `server/Domain/Entities/AgentTask.cs` + `AppDbContext` + migration | 5 columns |
| `server/Application/Services/AgentTaskReplyService.cs` | call the bundle service after `ResolveDeliverableAsync`; pass `deliverable` to the note |
| `server/Application/Services/DelegationReportFormatter.cs` | `deliverable=` bit; trailing attach block outside `FitReport` |
| `server/Application/Services/ChannelContracts.cs` | `attach-pdf` alternation; item flag; `AttachMarkerFormat` text |
| `server/Application/Services/ChannelReplyDispatcher.cs` | implied attachments + stamping + hold (S3); directory zip + `attach-pdf` (S4); incident raise |
| `server/Domain/Enums/AgentIncidentKind.cs` | `DeliverableUndelivered = 46` |
| `server/Application/Services/CardService.cs` | Done-move check + System note (S5) |
| `server/Application/Services/ChannelPreamble.cs`, `server/Bundles/orchestrator.md`, `AgentWorkspaceProvisioner.cs` | S6 text |
| `docs/orchestration-loop.md`, `docs/messaging/slack-api-file-upload-brief.md` | S6 docs |
| tests per §5 | new `DeliverableBundleServiceTests`, `MarkdownPdfRendererTests`; extended `ChannelFollowUpAttachmentTests`, `ChannelContractsTests`, `DelegationUnitTests`, pinned-text suites; new `CardDoneDeliverableTests` |

## 7. Operational notes for the mav-ref host

- Deploy the server build; no project-side change. Confirm `msedge.exe` exists at the default
  path on that machine or set `Deliverables:BrowserPath`. `render.log` under the bundle directory
  is the first thing to read when a PDF is missing.
- Bundles live in `<repo>\.antiphon\deliverables\` (gitignored). They are small (a spec PDF is
  hundreds of KB); no retention job in v1 — note as a follow-up if the directory grows.
- The existing CARD-0250 test fixtures prove the Slack adapter posts text then each file into the
  thread; a five-file bundle is five uploads in one thread reply, which is the shape Mike asked for.

## 8. Relationship to CARD-0338 (#30) and other open work

- **CARD-0338** owns text-only follow-up delivery ("CARD-0003 impl landed, review blocked" with
  no file). This plan makes every *document-bearing* `[task done]` turn deliverable as a side
  effect of S3; it does not deliver marker-less, bundle-less turns and does not change
  `ChannelReplyLost`. If CARD-0338 later chooses to deliver plain-text machine-turn follow-ups,
  S3's implied-attachment step composes with it unchanged (attachments are computed before the
  send either way).
- **CARD-0330 (distiller)**: attach markers are already must-keep anchors; the `--- deliverable ---`
  block survives distillation. No change.
- **CARD-0331 (land queue)**: bundles are made at settlement, not at land; no interaction.
- **CARD-0262 (pinned instructions)**: "always give me pdf" becomes harness behaviour for task
  deliverables; the pin mechanism remains the right home for other standing preferences.

## 9. Out of scope

- A sub-orchestrator whose root orchestrator is channel-bound: the child's `[task done]` note
  goes to the sub-orchestrator's session, which is not bound; the bundle reaches the root only
  through the sub-orchestrator's own report. Follow-up card if it bites.
- Rendering diagrams (Mermaid) inside the PDF; images referenced by relative path are inlined
  only when they are inside the document set's directory.
- Non-Markdown deliverables (`.docx`, `.xlsx`): attach as files via `[[attach:]]`, no rendering.
- Bundle retention/cleanup; a channel-side "send me the spec for CARD-nnnn" command (the
  orchestrator can answer that today with `[[attach-pdf: <folder>]]` after S4).
- Delivering `.md` sources for a Code task's incidental doc edits.
