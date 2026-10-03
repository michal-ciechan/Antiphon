# CARD-1006: Grok Linux sign-in and trust qualification

Date: 2026-10-03. Original Plan task: `e6f1295a`; evidence amendment: `3fdafc77`.
Amendment baseline: `8d3148dcf6d0040ef1a69b0e2fceb38ce1a77b18`, Investigate
task `1e39a459` atop the original landed plan at `374fefe1`. This dispatch edits
only this plan. CARD-1006's three items and Acceptance were re-read through
`card.ps1 get`; the amendment brief expressly permits synthetic trust coverage.

## Outcome and admission

Make the sign-in remedy platform-neutral, regress the two real Linux SignIn
frames, and preserve the rule that a blocking screen never becomes Ready.
Trust coverage is now explicitly SYNTHETIC; it qualifies the existing predicate
and waiter, not the appearance or action keys of a real Linux trust screen.

[The investigation](../../investigations/2026-10-03-card-1006-linux-grok-signin-trust-capture.md)
and its [immutable frames JSON](../../investigations/2026-10-03-card-1006-linux-grok-frames.json)
confirm sign-in-first for Grok 1.0.41 in both credential-free, zero-input probes.
No trust, updater or Ready screen appeared in either 15-second window. Installed
production classification of both approval frames was SignIn/false. This is
bounded evidence, not a universal claim about all versions or network conditions.
A real credential-free trust capture is unavailable under this procedure; no
additional identical trust probe or authentication workaround is planned.

**Next is TestDesign**, as required by this dispatch's Plan -> TestDesign -> Code
pipeline. The inherited verification design remains below for that stage to
audit: revised fixture provenance, method names and anchor placement need its
explicit sign-off. S-0 lists owned Code-admission gates; the exact-image
throwaway-container receipt is still missing. Neither this amendment nor the
historical capture claims that gate passed. No implementation or runtime test
has been executed by this Plan task.

Owners consulted: `docs/project-context.md`, `docs/ops-http.md`,
`docs/antiphon-api.md`, `docs/orchestration-loop.md`,
`docs/agent-card-lifecycle.md`, `docs/agent-credentials.md`,
`docs/agent-kinds.md`, `docs/session-runtime-invariants.md`,
`docs/adr/0002-modern-conpty-backend.md`, and `docs/testing-and-build.md`.
Format references: the CARD-1004 and CARD-1008 plans. The CARD-1004 Review report
was retrieved with `delegate.ps1 -Status dfc07c3a`; its scratch probe source is
not present in this checkout. The 19 shapes below reconstruct its reported
inventory explicitly; they are not represented as recovered original bytes.

## Ground truth

References are relative to this baseline, not moving line numbers at execution.

| Card assumption / requirement | What the code or evidence actually does | Consequence |
|---|---|---|
| The remedy should apply to a Linux runner. | `src/Antiphon.Agents.Pty/GrokDetectors.cs:71` names a Windows user and says every pool launch on the machine fails. The store is selected per launch/home. | Replace the complete remedy with D-1 and pin it exactly, including its home-specific scope. |
| Linux sign-in and trust are qualified. | Existing tests contain 1.0.13 Windows examples; CARD-1004 stores Linux ready dashboards. Investigate now supplies two real redacted Linux 1.0.41 approval frames, but no trust frame. | Copy both real SignIn frames unchanged into new fixtures. Use labelled synthetic trust; do not claim real Linux trust qualification. |
| Both modals are accessible without auth. | Both independent fresh-home/cwd launches reached OAuth approval first and stayed there until the 15-second deadline, with zero input. This agrees with `docs/agent-kinds.md:408` and classifier order. | Retain sign-in-before-trust. D-3 explicitly revises the trust coverage basis; real trust requires a human-gated signed-in follow-up note only. |
| The existing capture store can supply sign-in text. | `server/Infrastructure/Agents/SessionRunner/GrokStartupCaptureStore.cs:45` suppresses all content after sign-in; otherwise it stores rendered text **and raw tail** at lines 56-65. | Preserve suppression. For this isolated measurement, extract only the rendered screen from the existing runner snapshot seam. Do not call the formatter with a false sign-in flag. |
| The server has a terminal snapshot route. | `docs/ops-http.md:233` says snapshot is runner-only. `src/Antiphon.SessionRunner.Contracts/SessionRunnerContracts.cs:252` separates `RawOutput` from `RenderedScreen`. | Use the isolated runner's `GET /sessions/{id}/snapshot`, never an invented `/api/.../snapshot`, and never serialize the whole response. |
| The capture used the exact production image in a throwaway container. | It used a local owned runner inside the running container. Production tag/digest is unavailable. A cached image with no RepoDigests proved binary equality only and lacked the selected runner executable. | A-1/A-2 remain owned admission gates; no invented digest, cached-image substitution or claim of container isolation. |
| Fresh homes and audit-off prevent persistence. | CARD-0857 owns ambient MCP isolation. `SessionRunnerRuntime.cs:2266` supplies an ordinary `.ansi.log`; `HostSession.cs:356` appends raw bytes despite audit-off and disabled transcripts. Raw logs lived in deleted scratch, not in committed evidence. | D-2 requires verified tmpfs custody for all writable capture paths, including ordinary logs; no logging code change in this card. |
| No input means no network. | Owned CLI/child outbound TCP 443 connections were observed, first at 57 ms in C-1; approval appeared around 2.5 seconds. Socket samples do not establish request purpose or offline renderability. | D-2 defines bounded network-enabled capture. Fixture replay needs no network; do not assert that OAuth capture works with `--network none`. |
| A recognized modal cannot be Ready. | `GrokStartupReadiness.cs:29` checks sign-in, then trust, before geometry/composer. Lines 32-67 require 30 rows, at most 120 columns, exact borders, an empty `>`/U+276F interior, blank status rows and exact hint. | Preserve precedence and the whole composer predicate. Test real approval anchors and synthetic trust over otherwise-valid composers. |
| Returning to Ready after a modal may reuse settlement. | `GrokStartupReadiness.cs:85` resets the tracker on every negative observation; lines 183-214 fail sign-in without input and answer trust at most once. | Pin reset, sign-in-before-trust, and trust-clearing behavior with controlled time. |
| An update modal has a detector. | `GrokDetectors.cs` has only trust/sign-in; `GrokStartupReason` has no update value. | D-4 reserves a real negative-fixture slot and retains explicitly synthetic update probes. No unmeasured update detector or new runtime state. |
| Windows has 99 recorded frames. | `Fixtures/card0778/startup-frames.json` has **114** captured checkpoints, counted at this baseline. `GrokStartupReadinessTests.cs:72` already checks their recorded reasons. | Keep fixture bytes unchanged and assert all 114 results. Separate replay from native Windows ConPTY evidence. |
| A runner rollout activates the classifier. | `server/Infrastructure/Agents/SessionRunner/RunnerGrokAdapter.cs:181` is the production readiness caller; its snapshot comes from the runner. | Server/AppHost activation after land is sufficient for the proposed source changes. No runner image or rollout-script change. |

Placement rechecked at 2026-10-03 13:00 UTC: GET `/api/runner-defaults`
returned revision 2 with a Linux default and no per-kind overrides; GET
`/api/session-runners` showed eligible Linux and Windows lanes and an unavailable
retired entry. This is evidence of available platforms, not an embedded fleet
location. Resolve both again at dispatch (plus pipeline/host occupancy for the
orchestrator). Omit `-Runner`; use `-Platform Linux` only for capture and
`-Platform Windows` for CP-5. Omit `-Platform` for portable work; `-Platform Any`
removes an existing OS pin. Checkpoint Group names identify the lane.

## Decisions

### D-1: exact remedy, scoped to the runner's home

`BlockReason(grokHome)` shall return the following concatenation. `{authPath}`
means `Path.Combine(grokHome, "auth.json")`; it is the only interpolation.

> ProviderSignInRequired: Grok opened on its sign-in screen. The credential store {authPath} has no usable session. Nothing was typed into it. Run `grok login` (or `grok login --device-auth` on a headless host) as the user that runs the session-runner, using that runner user's GROK_HOME, then re-dispatch. Every Grok pool launch using that GROK_HOME will fail the same way until then.

Keep the machine-readable `ProviderSignInRequired` prefix and existing launch-block
kind. There are no platform names or claims that every home on a machine shares
the failure. Add `GrokSignInPromptDetectorTests.C1006_Block_reason_is_platform_neutral`
as one `[Test]` that checks the full literal expected message for a POSIX-style
home and a Windows-style home. Use `Path.Combine` solely for the expected auth
path; call no filesystem APIs. The test pins the login alternatives, runner user,
`GROK_HOME`, no-input assurance and per-home scope. It must fail against baseline.

Rejected: OS branching, “container user” only, leaving the home unspecified,
and a substring-only test that would miss the existing incorrect sentence.
The message is operator guidance; this card never executes its login command.

### D-2: exact-image capture, network and temporary-log custody

Owner: the caller/orchestrator supplies the image identity; a separately
commissioned Linux Debug helper owns execution and cleanup. This Plan runs no
CLI capture. C-1/C-2 are completed historical observations, not future trust
targets. One additional SignIn-only capture is the S-0 isolation receipt; it
does not replace or rewrite the investigation files.

1. **Image gate A-1:** before launch record the selected production runner's
   literal repository:tag AND immutable `sha256:` RepoDigest, matched to that
   running container and platform. Both values are currently **unavailable**.
   The caller must supply them in the capture brief/receipt; this plan cannot
   honestly name an exact tag/digest today. Launch exactly
   `<verified-repository>:<verified-tag>@sha256:<verified-digest>` (an unfilled
   template is not executable admission). A local image ID, binary hash, mutable
   tag alone or the cached investigation image is insufficient. Verify that
   this digest contains the selected runner executable and Grok; record their
   versions/hashes. No fetch/build of a substitute or CLI upgrade/downgrade.
2. Use `docker run --rm` with a unique owned name, `--read-only`, `--init`,
   `--log-driver none`, no published ports and no production state, credential,
   project, profile or Docker-socket mounts. Run the existing runner executable
   in local mode with phone-home, Herdr and server adapters disabled. Its dynamic
   loopback snapshot address is private to the helper, never a fleet constant.
   If the chosen image cannot run this way, A-2 fails; no in-runner fallback.
3. **Network decision:** replaying retained SignIn fixtures needs no network.
   A new OAuth startup capture has NOT been shown to render offline: network
   was observed before approval, without proving which request was necessary.
   Do not require or claim `--network none` for the capture. Use an ephemeral
   isolated bridge with no inbound publication and enforced outbound access
   limited to DNS resolution through its configured resolver and public TCP
   443; deny other egress and access to private production services. The Debug
   owner must verify that policy before startup, recording rules, not packets
   or payloads. Do not infer hostnames from the observed IPs. If that policy
   cannot be provided, report A-2 unmet rather than use unrestricted/host
   networking. A separate offline experiment is unnecessary for admission.
4. **Raw-log custody:** mount a task-owned tmpfs at the disposable scratch root
   and `/tmp`; redirect HOME, GROK_HOME, XDG config/cache, TMPDIR, runner state,
   session/ANSI logs, PTY host logs/manifests and audit roots there. Verify all
   resolved writable paths, restrictive permissions and memory-backed storage
   before launch. tmpfs must not spill to persistent swap (verify host policy
   or an effective supported noswap mount); otherwise stop. No disk-backed
   scratch fallback, Docker stdout log, core dump, archive or log attachment.
   Ordinary `.ansi.log` writes still occur; this procedure confines and deletes
   them rather than claiming `ANTIPHON_PTY_AUDIT=0` suppresses them. Only the
   privacy-reviewed rendered JSON/provenance may leave tmpfs.
5. Start both runner and CLI through `/usr/bin/env -i` with explicit nonsecret
   PATH, the scratch paths, `TERM=xterm-256color`, `BROWSER=/bin/false`, and
   `ANTIPHON_PTY_AUDIT=0`. No inherited provider/proxy/auth variables. Never dump
   the parent environment or inspect auth-file content. Audit image-level MCP
   isolation from deployment/configuration metadata (CARD-0857), without reading
   secrets; absence of MCP UI is not proof. Use fresh cwd with no instructions.
   Launch `grok --no-alt-screen --session-id <new-guid>`, 120x30,
   `TranscriptEnabled=false`, no prompt/rules/resume arguments and no waiting
   server adapter. No login, token, browser approval, trust answer or model turn.
6. Read only the owned runner's `GET /sessions/{id}/snapshot`. Select
   `RenderedScreen` in memory; never serialize the full response or RawOutput.
   Poll no faster than once per 200 ms. Start a hard 15-second observation and
   network budget at process launch (not at first HTTP response); end early
   after the sanitized target is stable twice. An external owned deadline
   supervisor cuts container egress and stops the process at the deadline even
   if polling stalls. Allow up to 5 seconds for awaited cleanup, with no further
   network or observation. Record a miss without retries/input. Leave the
   installed updater setting unchanged; capture a spontaneous notice under the
   same deadline, never force/update/confirm one. Record version/hash afterward.
7. In `finally`, cut egress, individually kill the owned session through
   `/sessions/{id}/kill`, await runner/child exits, and stop/remove only this
   named container if necessary. Verify zero owned processes and container
   absence; remove the owned bridge/rules and tmpfs. Check nonempty canonical
   scratch path plus ownership marker before deletion. No `kill-all`, broad
   command-text process matching or home archive. Receipt includes zero input
   calls/bytes, actual network window and scratch/log deletion on every exit.

Rejected: pretending the original local-runner capture proves exact-image
isolation; inferring offline feasibility from zero input; retrying trust behind
sign-in; treating audit-off as log suppression; persistent raw logs followed by
a claim that nothing was ever written. The existing snapshot/PTY machinery
remains the transport; no new production tool, API or deployment change.

No raw-logging code change is needed for this tmpfs procedure. If a future
capture must work without memory-backed storage, the session-runtime owner must
separately commission an explicit capture-only no-ANSI-log policy through
`SessionRunnerRuntime.cs` and `HostSession.cs`, with tests proving the null-log
path while preserving ordinary production logs. That is a follow-up note, not
an added CARD-1006 implementation slice or a defect claim about normal logging.

### D-3: real SignIn fixtures and explicitly synthetic trust

Owner: TestDesign freezes this provenance contract; Code implements it under
`tests/Antiphon.Tests/Agents/Fixtures/card1006/`. The three planned files remain
`linux-blocking-frames.json`, `synthetic-blocking-frames.json`, `provenance.md`.
The existing fixture glob copies them; no project-file edit is needed. Preserve
both investigation files byte-for-byte at `8d3148dc` and link them as source.

Copy the three decoded screens from the investigation JSON, without trimming
or rewriting headers. `linux-blocking-frames.json` uses a `captures` array:

| Capture ID | Label | Expected reason / Ready | Rows / maximum width | Pinned decoded UTF-8 SHA-256 |
|---|---|---|---|---|
| C1-connecting | `real-rendered` | Unknown / false | 30 / 67 | `beb362aeb8dfa6bbb1a2c95eff5e6fcc49527d0acf67c941653d40172fb2b277` |
| C1-sign-in | `real-rendered-redacted` | SignIn / false | 30 / 85 | `e70a9a5d6630171b164c3bfa3a660e04a590514a0635c4044dd82db25dfb6d06` |
| C2-sign-in | `real-rendered-redacted` | SignIn / false | 30 / 85 | `9a5793ae8bd3e74c1ef60c9125b516817ae59133b276f6b8aff2f43850707f52` |

Both approval frames have exactly `<CODE-9> ` (including the trailing space)
at zero-based row 15, columns 56..64: nine cells replacing an `AAAA-BBBB`-shaped
device code. Record category `device-code`, row 15, column 56, length 9; never
retain the original. The approval text at row 13, browser-code hint at 17 and
waiting text at 25 are unmodified detector anchors. `GrokSignInPromptDetector`
matches those text phrases, not code characters or `<CODE-9>`. V-2 checks both
original sanitized frames and in-memory nine-cell blank/`XXXXXXXXX` substitutions
to pin that independence. Such substitutions are synthetic controls, not new
real captures. No menu, selection cursor or box exists in either approval frame.

Follow CARD-1004's actual convention: `captures` records contain `screen`,
`cols`, `rows`, `cliVersion`, `session`, `host`, `sha256`; adjacent `provenance.md`
states source, capture time, decoding/reconstruction and evidence limits. That
JSON has **no label field and no synthetic captures**; do not invent a historical
CARD-1004 synthetic label. Its tests construct synthetic overlays separately.
For CARD-1006 extend this schema explicitly with `captureId`, `label` and
`source`; retain ASCII-escaped non-ASCII/control JSON, exact LF rows and decoded
UTF-8 hashes. Record source task `1e39a459`, CLI `1.0.41 (4220f3b224a6)`, runner
build `4358939ecd85d6e7ff0941f970879499cb930e3d`, Linux/Debian 12, literal reported
backend `InboxConhost`, window times, args/environment and zero-input cleanup.
Use `session: null` with `not-retained`, generic host description and
`imageTag: null`, `imageDigest: null` with `unavailable`; do not substitute the
later A-2 receipt or infer Windows ConPTY from that backend label.

Use the exact new machine label `synthetic-derived` and a **SYNTHETIC** heading
in provenance for every P-01..P-19 base and every composed/transformed control.
Each record/definition names `probeId`, source fixture/test anchor, transformation,
expected reason, and decoded hash for stored screens. No invented session/time
or claimed real Linux trust version. P-01 is the synthetic trust fixture: place
`Do you trust the contents of this directory?`, `U+276F Yes, proceed y`, and
`No, quit n` on rows 5/7/8 of a blank 120x30 screen (U+276F decoded, at column 2).
The question/choices derive from the existing 1.0.13 trust test and CARD-1004's
`Sign_in_and_trust_override_a_linux_composer` overlay, not Linux observation.
P-02 sign-in-menu and P-06/P-07 updater compositions are also SYNTHETIC.

V-4 pins real hashes independently in test source and asserts exact label/source
separation, redaction cells, round-trip equality and geometry. A missing required
fixture fails, never skips. This brief replaces the original two-real-modal
acceptance with two real SignIn frames plus synthetic trust behavioral coverage;
the orchestrator records that distinction on the card, not a claim that trust
was captured. Real trust remains unqualified.

Future replacement evidence would need a separately human-authorized SIGNED-IN
Linux session, a fresh untrusted cwd, verified tag/digest, sanitized rendered
trust screen, actual choices/keys, ordered states and cleanup provenance. Owner:
caller/human plus session-runtime maintainer. This is only an out-of-scope
follow-up note, not a task, credential exception or prerequisite for these
synthetic tests. Retain synthetic regression IDs even if real evidence is later
added; do not silently relabel them.

For additional captures, scan rendered text in memory before console/export for
account/email identifiers, bearer/key material, OAuth URLs and codes, and
unexpected paths. Preserve anchors and equal-width geometry; redact before any
durable output. Review scanner output and sanitized bytes before committing.
Raw temporary custody follows D-2; it is distinct from export sanitization.
`GrokStartupCaptureStore` sign-in suppression stays unchanged.

### D-4: conditional update evidence, explicit deferral

Reserve `realUpdate: { status: "not-observed", captureIds: [] }` in the provenance
schema. Until a real update screen appears, defer real updater qualification and
production detector work. Commit two separately labelled synthetic negative
fixtures (P-06/P-07 below); they prove only their specified shapes are not Ready.
Do not infer safety for an unknown modal drawn above an intact composer.

If Debug observes a real update/notice frame, commit it as required by card item 3
and change the slot to `captured` with a nonempty capture ID list. First replay it
against baseline. If it is already not Ready, add the regression without changing
production. If it is Ready, return to TestDesign with the exact frame: define a
narrow `GrokUpdatePromptDetector` in `GrokDetectors.cs`, composed of its measured
title/body plus action/menu anchors, checked before the composer and returning
existing `Unknown`/not-ready. Never auto-dismiss it, match a generic “update” word,
or add an enum value without a separate need. Freeze those predicates and their
positive controls before Code. Synthetic evidence cannot choose vendor anchors.

### D-5: preserve fail-closed readiness and Windows behavior

Retain sign-in before trust, rendered-screen-only readiness, the exact two
composer glyphs, current geometry, hint/status checks and settlement semantics.
The existing sign-in anchors match both real frames; no predicate change is
justified. Preserve all existing 1.0.13 matches and Codex nonmatches. Do not infer trust from a menu
cursor or authorize `y` from a generic question. A changed trust action/menu
contract requires measurement and plan revision; this card does not guess keys.

Windows fixtures and current native ConPTY selection stay unchanged. CP-5 is a
Windows helper task at the same Code SHA, using fakes, not a real provider turn.
It supplements the 114-frame replay; it does not discharge CARD-1011's real Windows
canary or approve routing changes.

## Scope and slices

Only this plan file changes in Plan. Future implementation allowlist:

| Slice | Files and work | Tests / completion gate |
|---|---|---|
| S-0 admission | Caller owns A-1 image identity and the card acceptance record; separately commissioned Linux Debug owns A-2 isolated SignIn-only receipt; TestDesign owns A-3 fixture/probe review below. Evidence is linked from this plan and future `Fixtures/card1006/provenance.md`. | A-1/A-2 pending; A-3 is next. No real trust capture gate remains. Do not claim Code-ready until all three close. |
| S-1 wording regression | `tests/Antiphon.Tests/Agents/GrokSignInPromptDetectorTests.cs`: add the exact D-1 test against unchanged production. | Commit/push; CP-1 must fail at the message assertion, not build/setup. |
| S-2 wording and Linux evidence | `src/Antiphon.Agents.Pty/GrokDetectors.cs` (D-1 wording only); `tests/Antiphon.Tests/Agents/GrokLinuxBlockingPromptTests.cs` (new, local fixture reader); `tests/Antiphon.Tests/Agents/Fixtures/card1006/{linux-blocking-frames.json,synthetic-blocking-frames.json,provenance.md}`. Use both real approval frames, Connecting and labelled synthetic definitions. | V-1..V-12; commit/push; CP-2. No sign-in/trust predicate change indicated; an observed updater follows D-4 revision before Code. |
| S-3 integration/regression closure | New Grok test class and this plan for receipts. `src/Antiphon.Agents.Pty/GrokStartupReadiness.cs` remains read-only under current evidence; any production change requires a revised design. | Commit/push; CP-3/CP-4 and separate Windows CP-5 at final implementation SHA. |

S-0 is explicit Code admission, not a reason to repeat Plan or postpone TestDesign:

| Admission | Owner | Required receipt / current state |
|---|---|---|
| A-1 exact image | Caller/orchestrator with production image visibility | Verified literal tag AND RepoDigest in the commissioned capture brief, tied to the selected running Linux container; currently unavailable. Record the amended synthetic-trust acceptance on CARD-1006 before Code. |
| A-2 isolation/custody | Caller-commissioned Linux Debug helper | One bounded SignIn-only throwaway-container run under D-2, verified network policy, tmpfs/no-persistent-swap custody, image-level configuration isolation and exited/deleted receipt. Historical local-runner evidence does not close this. A miss is reported, not bypassed. |
| A-3 executable verification | TestDesign | Audit immutable frames and labels, freeze all transformations and V methods, review A-1/A-2 receipts, import five CP rows and confirm minima. If A-1/A-2 are still absent, identify those owned gates before handing to Code; do not silently waive them. |

Read-only regression files: `GrokStartupReadinessTests.cs`,
`GrokLinuxStartupReadinessTests.cs`, `GrokTrustPromptDetectorTests.cs`,
`GrokStartupCaptureStoreTests.cs`, `RunnerGrokAdapterReadyTests.cs`,
`RunnerGrokAdapterReadyTestsPty.cs`, `RunnerGrokAdapterSignInPromptTests.cs`, and
`RunnerGrokAdapterTrustPromptTests.cs`, all under `tests/Antiphon.Tests/Agents/`.
Preserve both `Fixtures/card0778/` and `Fixtures/card1004/` byte-for-byte.

### Landing order and overlap

The caller owns landing serialization. CARD-1006's only current source edit is
Grok wording plus its tests/fixtures; the following are read-only dependencies,
not permission to broaden into runner, attention or deployment work.

| Card | Files/area versus CARD-1006 | Who lands first / coordination |
|---|---|---|
| CARD-1008 | `scripts/c590-remote.sh`, `scripts/deploy-server2.ps1`, Docker harness, `tests/Antiphon.Tests/Scripts/RemoteScriptContractTests.cs`; its documentation checks also touch `DockerStackDocumentationTests.cs` which this Plan only runs. No write overlap. | This docs amendment may land first. CARD-1008 owns and lands any deployment/script changes first; CARD-1006 never edits them. Finish any rollout before selecting A-1's image and keep that image fixed through A-2. Recount docs tests if integrating a changed baseline. |
| CARD-0965 | Server attention/hold/input work, including `server/Application/Services/AttentionService.TaskInputs.cs`, `AgentTaskReplyService.cs`, `ParkedMessageSweepService.cs` and related tests; outside this allowlist. | No source dependency; either independent change may land first. If a proposed blocker remedy expands into attention/hold, CARD-0965 lands first and this card requires replanning; keep D-1 text-only now. |
| CARD-1001 | `tools/Antiphon.Checkpoints/Coverage/CoverageCommand.cs` and coverage tests. CARD-1006 invokes `import`, not the coverage verb, and edits none of these. | Either independent change may land first; any shared tool repair belongs to CARD-1001 and lands first, then re-import the plan. No tool fork in this card. |
| CARD-0959 | Runner capabilities/phone-home contracts, CLI-version advertisement and dispatch admission (`src/Antiphon.SessionRunner*`, `server/Infrastructure/Agents/SessionRunner/*` and related tests). No write overlap. | Either docs/source change may land first. A CARD-0959 runner rollout must complete before A-1 image selection; if it changes the selected image during capture, restart A-1/A-2 qualification. Do not infer Grok/Codex versions from image/build equality. |

No auth, delivery, routing, generated `docs/cards/` or deployment-script edits.
Read/import use of a tool does not authorize modifying it. No rebase, merge of
master or fetch in this amendment; the landing service performs integration.

## Verification design

All new tests are portable, deterministic and process-free. Use existing controlled
time patterns from `RunnerGrokAdapterReadyTests` for `GrokReadyWait`; do not use
real sleeps or widen deadlines. The new class has **11 single-result methods**
below; loops print a case ID on failure and are not counted as TUnit results.
Their production calls and first assertion are specified here. V-1 lives in the
existing sign-in class; V-2..V-12 live in `GrokLinuxBlockingPromptTests`.

| ID | Method | Inputs and observable assertions |
|---|---|---|
| V-1 | `GrokSignInPromptDetectorTests.C1006_Block_reason_is_platform_neutral` | Two home styles; exact full D-1 message from production. |
| V-2 | `GrokLinuxBlockingPromptTests.C1006_Real_sign_in_is_not_ready` | Require both C1/C2 SignIn frames; detector true, trust false, Reason=SignIn, IsReady=false. Repeat with D-3's in-memory nine-cell substitutions to prove redaction independence. Also require C1-connecting Unknown/false and neither detector. One result, internal loops. |
| V-3 | `GrokLinuxBlockingPromptTests.C1006_Synthetic_trust_is_not_ready` | Require SYNTHETIC P-01, label/source assertion then production trust detector true, sign-in false; Reason=Trust, IsReady=false. This asserts existing predicate behavior, not real Linux trust qualification. |
| V-4 | `GrokLinuxBlockingPromptTests.C1006_Fixture_bytes_and_provenance_are_pinned` | Pin all three D-3 real hashes and exact redaction cells; verify 120x30, widths 67/85/85, version/zero-input/limited-isolation provenance and JSON round trip. Require 19 distinct synthetic base definitions, their sources/labels and no synthetic capture masquerading as real. Missing data fails. |
| V-5 | `GrokLinuxBlockingPromptTests.C1006_Blockers_override_valid_composers` | Preserve all approval anchors from both real frames and, separately, P-01's synthetic trust anchors over valid Windows `>` and Linux U+276F dashboards. Prove base Ready first; each SYNTHETIC composition retains its modal reason/false. No accidental busy-status blocker. |
| V-6 | `GrokLinuxBlockingPromptTests.C1006_Current_frame_overrides_raw_history` | Current real approval or synthetic trust + stale ready raw remains its modal reason. Real ready current + stale sign-in/trust raw remains Ready. Raw history cannot override current rendered evidence. |
| V-7 | `GrokLinuxBlockingPromptTests.C1006_All_19_fail_open_shapes_stay_closed` | P-01..P-19, then each layout with real approval anchors and separately synthetic trust anchors. Exactly 57 cases; not Ready for all, SignIn/Trust for derived cases. Name each variant and provenance on failure. |
| V-8 | `GrokLinuxBlockingPromptTests.C1006_Blocker_resets_readiness_settlement` | For each real approval frame and synthetic trust: Ready at 0 ms=false, blocker at 900=false/count 0, Ready at 950=false/count 1, Ready at 1000=false, Ready at 1950=true. Settlement cannot reuse pre-blocker time. |
| V-9 | `GrokLinuxBlockingPromptTests.C1006_Sign_in_precedes_trust_and_types_nothing` | Script both real approval frames and a SYNTHETIC mix of approval/trust anchors over valid composer into production WaitAsync. All fail SignIn, fire OnSignIn and record zero input calls; combined classifier reason checked first. |
| V-10 | `GrokLinuxBlockingPromptTests.C1006_Trust_remains_blocked_until_cleared` | SYNTHETIC P-01 repeated to controlled deadline returns false/Trust with exactly one `y`; P-01 then two settled ready frames returns true with one `y`. This pins the existing action only; never call a real CLI. |
| V-11 | `GrokLinuxBlockingPromptTests.C1006_Update_fixture_policy_and_negatives` | P-06/P-07 must be labelled synthetic and not Ready. A captured realUpdate slot requires at least one real-labelled frame and every referenced frame not Ready. not-observed means no real-update claim, not a synthetic substitute. |
| V-12 | `GrokLinuxBlockingPromptTests.C1006_Windows_fixture_results_are_unchanged` | Iterate exactly all 114 captured Windows checkpoints; compare Reason and IsReady with recorded results. No fixture rewriting. |

V-4 is artifact integrity, not independently sufficient behavior coverage. V-2,
V-3 and V-5 call the production detector/classifier, and V-8..V-10 call the actual
tracker/waiter. No test compares only a test helper to its own constants.

V/guard IDs remain V-1..V-12; P-01..P-19, R-1..R-3, CP-1..CP-5 and
PC-1..PC-14 (including PC-5a/PC-5b) retain their IDs. No numbered row is retired
or renumbered. The proposed, never-implemented method names
`C1006_Real_trust_is_not_ready` (V-3/PC-4) and
`C1006_Real_blockers_override_valid_composers` (V-5/PC-5a/b) are retired in favor
of the honest names above; every method reference below uses the replacements.
No additional V method: still 12 single-result methods, 11 in the new class.
Fixture roster: three files, three retained real screens (two approval, one
Connecting), 19 synthetic base definitions; 38 V-7 derived cases constructed in
memory. Later A-2 evidence is separate provenance unless TestDesign explicitly
adds a new fixture and recounts internal cases; it does not overwrite C1/C2.

### Durable 19-shape inventory

These shapes reproduce the named Review inventory, with explicit construction
rules. Store synthetic base screens/definitions separately from real captures.
Start composer variations from CARD-1004's 1.0.41 dashboard (30 rows; composer
top/input/bottom at 24/25/26; enabled hint at 28). Preserve every unchanged row.
Approval bases use D-3's real anchors; trust bases use SYNTHETIC P-01. All
compositions, including the sign-in menu and updater shapes, are synthetic.

| Probe | Exact shape / transformation | Base expectation |
|---|---|---|
| P-01 | SYNTHETIC trust question/choices at D-3's rows 5/7/8, U+276F before affirmative. | Trust / false |
| P-02 | SYNTHETIC sign-in menu: approval anchors at rows 3/4/5, row 7 `  U+276F Sign in`, row 8 `  Exit`; blank elsewhere, no enabled composer. Not an observed menu. | SignIn / false |
| P-03 | SYNTHETIC trust anchors over dashboard with row 22 `  Working...`. | Trust / false |
| P-04 | SYNTHETIC approval-anchor overlay over the same working dashboard. | SignIn / false |
| P-05 | SYNTHETIC approval-anchor overlay plus a complete composer-shaped box whose interior is bare U+276F. | SignIn / false |
| P-06 | Synthetic `Update available` menu, `U+276F 1. Update now`, `2. Later`; no enabled composer/hint. | not Ready |
| P-07 | Synthetic boxed `A new version is available` / `Press Enter to update` dialog; no enabled composer/hint. | not Ready |
| P-08 | Valid composer geometry, interior `U+276F 1. Yes`. | ComposerUnavailable / false |
| P-09 | 30-row blank screen with only a three-line `│ U+276F │` box beginning at row 9. | not Ready |
| P-10 | The same bare box beginning at row 11. | not Ready |
| P-11 | Blank screen except a lone U+276F, no box. | not Ready |
| P-12 | Blank screen except U+276F at row 25, column 4. | not Ready |
| P-13 | Valid composer geometry, interior `U+276F typed text`. | ComposerUnavailable / false |
| P-14 | Valid composer geometry, interior `>U+276F`. | ComposerUnavailable / false |
| P-15 | Valid composer geometry, interior `U+276FU+276F`. | ComposerUnavailable / false |
| P-16 | Valid Linux dashboard except empty row 28 (hint removed). | Unknown / false |
| P-17 | Valid Linux dashboard except row 22 `  Working...`. | Working / false |
| P-18 | Valid Linux dashboard except row 26 empty (bottom border missing). | ComposerUnavailable / false |
| P-19 | Valid Linux dashboard with the final blank row removed (29 rows). | Unknown / false |

`U+276F` above denotes one actual decoded character, never those six literal ASCII
characters. V-7 checks 19 base shapes plus 19 sign-in-derived and 19 trust-derived
shapes: **57 internal cases, one TUnit result**. Freeze approval anchors (the
three exact D-3 phrases from C1; C2 has identical anchors) at rows 3/4/5,
column 2, and trust question/choices at rows 5/7/8, column 2. V-5 uses the same
placements; its mixed V-9 control puts approval at 3/4/5 and trust at 7/8/9.
These placements preserve composer/status rows and every probe's changed glyph.
Assert construction kept the complete respective anchor set. Transfer each probe's layout/glyph change,
not a different modal's text: the sign-in-derived and trust-derived sets retain
their respective reasons. The deliberate mixed-anchor case is V-9 and must
classify SignIn. Only V-9 intentionally mixes modal anchors. TestDesign verifies
these placements for every shape before Code; do not drop a variant.
V-5 is the stronger ready-composer precedence control, so a
second broken layout cannot hide a missed modal detector.

### Regression and lane obligations

R-1: unchanged sign-in/trust predicates, both original Linux ready dashboards and
all Windows recorded results. CP-3 has 47 results: 17 sign-in (16 existing + V-1),
4 trust, 5 startup and 21 Linux startup.

R-2: existing adapter/waiter/privacy integration classes. CP-4 has 25 results:
14 `RunnerGrokAdapterReadyTests`, 2 `RunnerGrokAdapterReadyTestsPty` (the prefix
intentionally selects it too), 3 sign-in adapter, 4 trust adapter and 2 capture
store. This includes native Linux PTY through FakeGrok, with no real auth/provider.
Honor the existing process limiter and serial scheduling. CARD-0988 records a
known full-load timing failure; any actual failure must still be reproduced at
the assigned base by exact method before calling it inherited.

R-3: a Windows helper runs CP-5 at the exact final source SHA. Its 35 results are
5 startup + 11 new blockers + 2 native FakeGrok through modern ConPTY + 17 sign-in.
The native test selects `modern`; record the actual backend. Linux replay cannot
stand in for this Windows receipt. Real Windows Grok is CARD-1011, outside scope.

CP minima count result expansion, not 114 fixture iterations or 57 probe cases.
They are derived from this baseline and the fixed new-method inventory; TestDesign
must recount/import if capture discoveries change the method roster. No whole
Unit/assembly run is required for this narrow card.

### Inspection

TestDesign task `1b43dce9`, 2026-10-03, source
`edb96aecd93cbe90fe8887c1a8d1523527bd49d9`. Read-only `git ls-remote origin refs/heads/master` at 13:58 UTC returned that
same SHA; the census therefore matches master at inspection. This freeze supplements the landed
verification design; D-1..D-5 and the implementation allowlist remain unchanged.
The earlier instruction to send this artifact to TestDesign is historical.
All runtime results and mutation outcomes below are requirements, not receipts.

| Bodies read | Boundaries -> coverage |
|---|---|
| `GrokDetectors.cs`, both detector bodies and BlockReason; entire `GrokStartupReadiness.cs`, including Classify, Observe, WaitAsync and Bounded | D-1 wording V-1; modal predicates V-2/V-3; precedence V-5/V-9; geometry V-7; settlement V-8/V-10 |
| Entire `GrokSignInPromptDetectorTests.cs`, `GrokTrustPromptDetectorTests.cs`, `GrokStartupReadinessTests.cs`, `GrokLinuxStartupReadinessTests.cs`, including their fixture readers | Existing examples/nonmatches and argument expansion R-1; independent immutable input and hash checks V-2/V-4/V-12 |
| Entire `RunnerGrokAdapterReadyTests.cs`, including ScriptedClient, JumpClock, PollGateClock, UtcJumpClock and timer-registration helpers | Current/raw separation V-6; deterministic waiter setup V-9/V-10; cancellation, deadline, minimum age and failure diagnostics R-2 |
| Entire `RunnerGrokAdapterReadyTestsPty.cs`, `RunnerGrokAdapterSignInPromptTests.cs`, `RunnerGrokAdapterTrustPromptTests.cs`, `GrokStartupCaptureStoreTests.cs`; production capture formatter and adapter WaitForReadyAsync | Sign-in write suppression, unchanged trust action, privacy R-2; native fake-provider transcript R-2/R-3 |
| `DirectSessionRunnerClient` constructor/StartAsync, snapshot, transcript, input and disposal bodies; test project fixture-copy and FakeGrok staging targets | Isolated native substitute, session identity and child ownership; no queue or live-provider claim |
| CARD-1004 JSON and provenance; CARD-0778 114 decoded checkpoints (117 distinct nonempty rendered rows, complete expected-reason roster), synthetic records and ReadyScreen selector; both CARD-1006 investigation files | Exact source bytes, canvas versus rendered width, copied frame limits and anchor positions; V-2/V-4/V-5/V-12 |
| Orchestration TestDesign/Mutation rules, testing checkpoint/import/slot/delivery rules, session startup/delivery owner, project conventions, CARD-0959 freeze, importer ExtractSection/SplitRow | Closed five-row scope, source census, post-land mutation separation, external Code-admission gates |

Missing setup is implementation work, not hidden evidence: new local card1006
fixture reader, three fixture/provenance files, eleven new class methods, one
wording method, and a local controlled-clock helper. Existing PollGateClock is
**private**; do not pretend the new class can access it. Implement a private
clock wrapper in the new class, forwarding GetTimestamp/TimestampFrequency/
CreateTimer to FakeTimeProvider and signaling each installed 50-ms poll timer.
Wait for timer installation before Advance, race installation against waiter
completion, and cancel/await in finally. Wall-clock WaitAsync is only a harness
watchdog; it must never decide the simulated readiness result. No shared helper
or old test-file edit is needed.

### Delivery inventory

**New or changed asynchronous delivery paths: zero.** The sole production edit
is a synchronous diagnostic string. There is no new producer, queue insertion,
durable message, retry policy or recipient handoff to commission. Busy-recipient,
already-eligible and crash/enqueue-failure queue matrices are therefore excluded
from this text/fixture change; an implementation that changes those paths must
return to Plan for real-queue tests and complete recipient evidence.

| Existing path exercised | Identity / persistence / recovery / observable receipt and limit |
|---|---|
| WaitAsync snapshot producer -> classifier/tracker -> readiness caller | A scripted snapshot's Sequence and CapturedAt identify the observation; state is in memory, no durable delivery. V-8 proves reset and V-9/V-10 prove ordered decisions. A false result plus zero write calls proves refusal at this seam, not downstream queue recovery. |
| Existing trust branch -> writeAsync -> scripted trust recipient | The test's explicit frame index and ordered writes identify the action; V-10 observes subsequent current frames. One `y` or callback completion is not delivery proof and is never reported as such. The fake cannot qualify a real Linux trust key. |
| Existing RunnerGrokAdapter.SendPromptAsync -> DirectSessionRunnerClient -> native PTY/FakeGrok -> Grok transcript tailer | Isolated SessionId joins launch and transcript; FakeGrok's session transcript is the persistent receipt. R-2/R-3 require exactly one complete UserPrompt with the exact `C1004 complete first prompt HEAD and TAIL` body, after an empty pre-send prompt roster. This is actual recipient evidence for the fake, but bypasses the server queue, production network, real Grok, multiline paste, and crash recovery. |
| BlockReason -> adapter LaunchBlock -> caller | R-2 checks ProviderSignInRequired, home and remedy on the same adapter instance; no user notification, Sent flag, event or ack is substituted for recipient evidence. This card makes no notification-delivery claim. |

### Literal fixture freeze

All row and column indices below are zero-based. Canvas metadata is **120x30**;
ready frames have maximum rendered width **118**, not 118 configured columns.
Do not pad rows to 120, trim spaces, normalize LF to CRLF, or remove the final
empty row. P-19 deliberately has 29 rows. C1/C2 approval widths are 85, not 118.

REAL source authority is the entire literal, ASCII-escaped JSON object in
`docs/investigations/2026-10-03-card-1006-linux-grok-frames.json` at the frozen
source SHA. Copy each decoded value byte-for-byte into the new captures array;
its complete literal text is reproduced in the immutable investigation. D-3's
three independently pinned decoded SHA-256 values were recomputed and match.
A fixture's own `sha256` field is not its expected oracle: V-4 has the three
literal digests in C# source and checks both declared hash and computed hash.
Likewise its expected capture IDs, labels, source task, sizes and redactions are
literal test data, not read back from the same document as expectations.

The REAL approval anchors, including punctuation, are:

```json
{"13":{"column":38,"text":"Approve in your browser to finish signing in."},"15":{"column":56,"text":"<CODE-9> "},"17":{"column":41,"text":"Make sure your browser shows this code."},"19":{"column":41,"text":"If it doesn't open, click here to copy."},"23":{"column":36,"text":"Copying not working? Click here to show full URL."},"25":{"column":49,"text":"Waiting for approval..."},"27":{"column":54,"text":"ctrl+q  quit"}}
```

`<CODE-9>` is eight characters; **one trailing U+0020 makes nine cells**.
V-4 requires row 15 length 65, 56 leading spaces and those exact nine cells in
each approval frame, and no placeholder in Connecting. Logo rows 4..10 occupy
columns 53..66 and remain unchanged. V-2 independently copies each approval row
array and replaces precisely [15][56..65) with nine spaces, then `XXXXXXXXX`;
it asserts unchanged other rows, unchanged length and the three literal phrases
before calling production. Neither expected text nor input generation may call
a redactor, GrokStartupCaptureStore, detector or classifier. No redaction code
is being implemented here; this contract tests classification independence from
the already-sanitized nine cells, not the historical sanitizer's privacy.
Do not synthesize an original credential or reverse the redaction.

The CARD-1004 schema actually has no synthetic label field. Preserve its source
labels/provenance; CARD-1006 explicitly adds `label`, `captureId` and `source`.
REAL rows use `real-rendered`/`real-rendered-redacted`; every definition below,
overlay, raw-history pair and redaction substitution is **SYNTHETIC**, with
`label: "synthetic-derived"`. Synthetic source objects name the source path,
capture selector, probeId and transformation, never a fabricated session/time.
`realUpdate` is exactly `{"status":"not-observed","captureIds":[]}` in this
freeze; encountering a real updater invokes D-4 before implementation.


Freeze the stored schema as follows (these are concrete field values; screen
values are the immutable source strings and synthetic recipes already pinned):

- `linux-blocking-frames.json`: root `schemaVersion: 1`, `captures` in order
  C1-connecting/C1-sign-in/C2-sign-in, `realUpdate` equal to the frozen object,
  and a `provenance` object with `sourceTask: "1e39a459"`,
  `sourcePath: "docs/investigations/2026-10-03-card-1006-linux-grok-frames.json"`,
  `sourceSha: "8d3148dcf6d0040ef1a69b0e2fceb38ce1a77b18"`,
  `cliVersionOutput: "1.0.41 (4220f3b224a6)"`,
  `runnerBuild: "4358939ecd85d6e7ff0941f970879499cb930e3d"`,
  `backend: "InboxConhost"`, `inputCalls: 0`, `inputBytes: 0`,
  `isolation: "local-runner-in-existing-container"`,
  `rawAnsiLogCustody: "temporary-scratch-deleted-not-tmpfs-qualified"`,
  `imageTag: null`, `imageDigest: null`, `imageIdentityStatus: "unavailable"`,
  `sessionIdentityStatus: "not-retained"`.
- Each REAL capture has `captureId`, D-3's exact `label`,
  `source` equal to `sourcePath + "#" + captureId`, `cliVersion: "1.0.41"`,
  `host: "Linux runner container (Debian 12)"`, `session: null`, `cols: 120`,
  `rows: 30`, literal `screen`, independent pinned `sha256`, and `redactions`.
  Connecting has `redactions: []`; each approval has exactly
  `[{"category":"device-code","row":15,"column":56,"length":9,"replacement":"<CODE-9> "}]`.
  No maximum-width padding is stored. All expected values are literals in V-4.
- `synthetic-blocking-frames.json`: root `schemaVersion: 1`, `probes` containing
  P-01..P-19 in numeric order, each with `probeId`,
  `label: "synthetic-derived"`, `source`, `transformation` (the literal recipe
  string from the table), `expectedReason`, `cols: 120`, `rows` (30 except P-19),
  `screen` and `sha256`. For L-based probes `source` is
  `"tests/Antiphon.Tests/Agents/Fixtures/card1004/linux-startup-frames.json#1.0.41"`;
  P-01 uses `"tests/Antiphon.Tests/Agents/GrokTrustPromptDetectorTests.cs#GrokTrustScreen"`;
  P-02 uses the REAL source path plus `#C1-sign-in`;
  blank-canvas P-06/P-07/P-09/P-10/P-11/P-12 use
  `"CARD-1006#synthetic-blank-canvas"`. No CLI version, capture timestamp,
  host or session is invented for a synthetic screen.
- `provenance.md` has explicit **REAL** and **SYNTHETIC** headings and links to
  both immutable investigation files and CARD-1004's provenance; it records
  the C-1/C-2 windows and limited cleanup claims verbatim from the investigation,
  explains all recipes and unobserved trust/updater, and later links A-2's
  separate receipt without replacing historical source metadata.

Runtime tests read only the copied `Agents/Fixtures/card1006` output files;
the source-path strings are provenance, not a dependence on docs being copied
into a test build. Use a private fixture reader in the new class and existing
GrokStartupFixture/GrokLinuxStartupFixture for old screens. JSON object order and
indentation are not hashed; decoded screens are. V-4 also checks raw fixture JSON is ASCII (apart from its ordinary LF/CR
formatting), with no literal ESC/control payload. V-4 checks every synthetic
recipe/hash against independently literal expected dictionaries as well as
metadata; a fixture's declared hash must never become its own expected value.

Canonical SYNTHETIC row construction follows. JSON strings use actual decoded
Unicode escapes, not the literal six-character notation. `B` is 30 empty strings;
`L` is CARD-1004 cliVersion `1.0.41`, independently pinned to decoded hash
`f2155460374433c4cd5da3849afc466541604d2d8faf9c9b1c71c93d02c6c1ec`.
Clone arrays; never mutate the shared fixture. `W` is CARD-0778 idle capture,
afterChunk 40, decoded hash
`708f5423814ba83ecce9769207051e30d69d7e1b15ab7483aa1d6ca34fe718e3`.
Both have borders at columns 2/117 on rows 24/25/26, composer marker at [25][4],
blank status rows 22/23, and the literal hint at row 28.

```text
A = {3: "  Approve in your browser to finish signing in.",
     4: "  Make sure your browser shows this code.",
     5: "  Waiting for approval..."}
T = {5: "  Do you trust the contents of this directory?",
     7: "  \u276f Yes, proceed y", 8: "  No, quit n"}
U = "  \u256d" + repeat("\u2500",114) + "\u256e"
I(s) = "  \u2502 " + PadRight(s,113,U+0020) + "\u2502"
D = "  \u2570" + repeat("\u2500",114) + "\u256f"
H = "  Shift+Tab:mode  \u2502  Ctrl+x:shortcuts"
```

`Patch(base,map)` replaces exactly the listed rows; Join uses one LF between
rows and no extra terminator. Applying A or T means Patch with the literal map.
P-05 uses the complete existing L composer, so the preserved footer is allowed.
P-09/P-10 intentionally have a full-width box away from row 24 and no hint;
they are Unknown. P-07's narrow box is not qualified composer geometry.

| Probe (all SYNTHETIC) | Literal construction | Expected reason / Ready | Decoded UTF-8 SHA-256 |
|---|---|---|---|
| P-01 | `Patch(B,T)` | Trust / false | `f66b2fea1ba97e618925e2e0bcdfdfc63af78d55f7b23c04d10945546d50212c` |
| P-02 | `Patch(Patch(B,A),{7:"  \u276f Sign in",8:"  Exit"})` | SignIn / false | `4418ebab3c1ece20cbd1b7d91658c01ceed11a533f83415def04949d31a75544` |
| P-03 | `Patch(Patch(L,T),{22:"  Working..."})` | Trust / false | `97d11cc8559d13659a638d2daf20e59871f410d99d096471791304be66ebe86a` |
| P-04 | `Patch(Patch(L,A),{22:"  Working..."})` | SignIn / false | `b9c1f1acb87c8a03ca0a92f3b342c08039a50058b26094c47c0b5a6759f0a4c7` |
| P-05 | `Patch(L,A)` | SignIn / false | `923d4c79b2f2c688ffb80cc5f2ed5766a983fff18bcc95c62715351d21f1fe7b` |
| P-06 | `Patch(B,{5:"  Update available",7:"  \u276f 1. Update now",8:"  2. Later"})` | Unknown / false | `2b37f534e6fcc26fd6b3929350928ccea9d43f68f53d10e3416cf79705c57d74` |
| P-07 | `Patch(B,{9:"  \u256d"+repeat("\u2500",58)+"\u256e",10:"  \u2502 "+PadRight("A new version is available",57)+"\u2502",11:"  \u2502 "+PadRight("Press Enter to update",57)+"\u2502",12:"  \u2570"+repeat("\u2500",58)+"\u256f"})` | Unknown / false | `205dff66669ae99acdab778a9aefdd5c0662cb487765b31cc07260b95a53addb` |
| P-08 | `Patch(L,{25:I("\u276f 1. Yes")})` | ComposerUnavailable / false | `e1323daca816ab8c4da430ee88dff35429c4202271b4084d749c4d492ff952d1` |
| P-09 | `Patch(B,{9:U,10:I("\u276f"),11:D})` | Unknown / false | `d44437ad4d8458fc44208507451b7ac65a255be4d928d6efaacaefa91fa795a6` |
| P-10 | `Patch(B,{11:U,12:I("\u276f"),13:D})` | Unknown / false | `e220881f8aa44b658d08fce841320d43bfb0a9316396d3fad682112c2e6a9ee3` |
| P-11 | `Patch(B,{20:"  \u276f"})` | Unknown / false | `725b420cec04e6a64cc41168699f49f86d23772b23580ecd326fe8cb56ae3d33` |
| P-12 | `Patch(B,{25:"    \u276f"})` | Unknown / false | `5080a26557c91e2960b1aec7ecca46f6c802a74553d9cec5cf5a4c9b0f1a3014` |
| P-13 | `Patch(L,{25:I("\u276f typed text")})` | ComposerUnavailable / false | `a66c40b80ec6ff4d219a2e042f735a57b0c28793350bb1d548d7c8bcef1cc3c1` |
| P-14 | `Patch(L,{25:I(">\u276f")})` | ComposerUnavailable / false | `0b2c173e1941f51dc5707b231311776afd9fb77d43f3dcf979ca236ae3455bae` |
| P-15 | `Patch(L,{25:I("\u276f\u276f")})` | ComposerUnavailable / false | `b7c5f4c1fd591bee881f8e7e0b8be1681fc271f45fa03d509d840766930c27e1` |
| P-16 | `Patch(L,{28:""})` | Unknown / false | `bd37147536b6b6e8f486fa6de369ade22257fc0930090c0f5031c4794d3e4c35` |
| P-17 | `Patch(L,{22:"  Working..."})` | Working / false | `a6a585d8e37768bb383509566583860176d3200eabda780f8ed2c834b993be42` |
| P-18 | `Patch(L,{26:""})` | ComposerUnavailable / false | `500edace876383c5ce7200c4f07c444eb41530ee9d74ff487ab467e332048f28` |
| P-19 | `L.Take(29)` | Unknown / false | `adbbfa427a081bd2ff1d39eeede70ba02c04e23ce8d425eba62c06a84f2a3358` |

For V-7 derived cases, first remove only the opposite modal's text from the
probe layout. The literal layout sources for P-01/P-02 are B with row 7
`"  \u276f"`; for P-03, L with row 7 `"  \u276f"` and row 22
`"  Working..."`; for P-04, L with row 22 `"  Working..."`; for P-05, L.
All other layout sources are their base screens above. This explicit extraction
prevents trust text surviving into a sign-in-derived case or sign-in text
surviving into a trust-derived case. Apply A or T to those sources. Updater text
remains in P-06/P-07; it is not an authentication anchor. P-11's lone marker
is deliberately at row 20 so neither overlay deletes it. No overlay touches
rows 22..28 or P-09/P-10's box rows. Assert source/overlay row counts and all
unassigned rows byte-for-byte, then detector anchor presence, then production
Reason/IsReady. Thus exactly 19 base + 19 sign-in + 19 trust cases survive;
only V-9 deliberately combines sign-in and trust. These explicit recipes resolve
the earlier ambiguous instruction to transfer a layout without another modal.

V-5 relocates all three literal approval phrases from each real frame to A's
rows, rather than pasting whole rows 23/25 over the composer/status. Check each
source contains the literal phrases at its measured coordinates first. Use T
separately. For W and L, assert Ready before overlay, unchanged composer rows
24/25/26, empty rows 22/23, H at 28, and modal Reason/false afterward. There are
six modal overlays (two approval sources plus synthetic trust, each over W/L).
The two mixed V-9 compositions instead use A at 3/4/5 and trust question/yes/no
at 7/8/9, all column 2. Both classify SignIn before any WaitAsync invocation.

V-4 also pins the unchanged Windows fixture **file** SHA-256 to
`6990d5910e3983005afc6f7dd1eec002f8c59686613c80d081e0d3953394dbee`.
V-12 requires exactly 68 idle + 44 startup + 2 sign-in = **114** checkpoints;
the incident metadata object is not a frame. This does not add or relabel any
Windows rows. Preserve both source fixture directories byte-for-byte.

### Proves it works now

All twelve existing V method names above are frozen, not placeholders. Namespace
is `Antiphon.Tests.Agents`; each is one `[Test]`, no `[Arguments]`. The file for
V-1 is `tests/Antiphon.Tests/Agents/GrokSignInPromptDetectorTests.cs`; V-2..V-12
share the new `tests/Antiphon.Tests/Agents/GrokLinuxBlockingPromptTests.cs`.
The following freezes decisive assertions and boundary combinations in addition
to the earlier method roster. CP-2 is the ordinary command selection for all V;
CP-1 first demonstrates the wording regression against unchanged production.

| ID | Layer / fixed input order | Decisive assertion |
|---|---|---|
| V-1 | Pure production BlockReason; `/tmp/c1006-runner-home`, then `C:\Antiphon\c1006-runner-home` | `blockReasonExact` compares the complete D-1 sentence, with only Path.Combine(home,"auth.json") interpolated; no OS branch or substring oracle. |
| V-2 | Detector + classifier; C1-sign-in, C2-sign-in, then Connecting; each approval original, nine-space, nine-X | `realSignInDetector:<id>:<variant>` is true before classifier calls; trust false, Reason SignIn and IsReady false; Connecting is neither detector and Unknown/false. |
| V-3 | Detector + classifier; literal P-01 | `syntheticTrustDetector:P-01` is true after exact label/source check; sign-in false, Reason Trust and IsReady false. |
| V-4 | Artifact integrity; fixed ID/hash/source/row maps above | `realHash:<id>` and `redactionCells:<id>` match independently pinned constants; all 19 synthetic hashes/recipes/labels match; parse/serialize/parse preserves strings and metadata; absence is failure, never skip. |
| V-5 | Classifier; W then L, each C1/C2 approval plus T | First prove Ready on both bases; `modalReason:<base>:<id>` is SignIn/Trust before `modalReady` false, with untouched composer/status/hint assertions. |
| V-6 | Classifier; each C1/C2 approval and P-01 current with L raw, then W/L current with each blocker raw | `currentReason:<current>:<raw>` follows current rendered screen and never raw history; assert both reason and IsReady. |
| V-7 | Classifier; all 19 bases in numeric order, then their 19 A and 19 T derivatives | `probeReady:<probe>:<base-or-A-or-T>` is false **before** reason equality; preserve exactly 57 named probe cases and the full anchor/layout checks above. |
| V-8 | Production tracker, each blocker C1/C2/P-01; settle=1000ms | Ready at 0=false/count1; blocker at 900=false/count0 (`resetCount:<id>`); Ready at 950=false/count1; 1000=false; 1949=false; 1950=true. First assertion `firstReadyObservation` is false. |
| V-9 | Production waiter; two actual approval screens then two synthetic mixed screens, fixed MaxWait=2000ms, settle=1000ms, poll=50ms | Mixed classifier `mixedReason` is SignIn before waiter; each wait returns false/SignIn, OnSignIn receives the same complete frame exactly once, failure records last reason SignIn/count0/signInSeen=true, `signInInputs` is an empty list. |
| V-10 | Production waiter, synthetic P-01 and L; MaxWait=2000ms, TrustSettle=1000ms, settle=1000ms, poll=50ms, minimum age=0 | Persistent P-01 reads every 50ms: false/Trust at 1000ms and writes exactly `["y"]`; P-01 at 0/50ms then L from 100ms: incomplete at 1050ms and true at 1100ms, exactly one `y`; `persistentTrustReady` is first outcome assertion. |
| V-11 | Fixture policy + classifier; P-06/P-07 | Both synthetic labels and exact Unknown/false outcomes; frozen realUpdate is not-observed/empty. Unknown status or captured/empty is rejected by fixture policy. A newly observed real updater returns to D-4, never silently enters this test as guessed text. |
| V-12 | Classifier over unchanged Windows capture records | `windowsCount`=114 and every `windowsReason:<capture>:<afterChunk>` and readiness agrees with the recorded result; counts are internal iterations, not 114 TUnit results. |

Boundary coverage: both composer glyphs, both home syntaxes on each executing OS,
two independent REAL approval frames and independent redaction substitutions,
modal/current versus stale raw in both directions, single versus mixed blockers,
valid versus invalid composer layouts, before/at settlement and trust expiry,
and native Linux versus Windows receipt. Arbitrary sizes/versions, unknown vendor
modals above an intact composer, real post-login trust, provider authentication,
queue persistence and production transport recovery remain explicit exclusions.
No assertion claims every arbitrary non-composer screen is recognized; the
fail-closed claim is bounded to the frozen shapes and measured anchors.

### Guards the regression

The read-only source census at the frozen SHA is:

| Class under tests/Antiphon.Tests/Agents | Existing methods | Existing TUnit results | New results | Checkpoints selecting it |
|---|---:|---:|---:|---|
| GrokSignInPromptDetectorTests | 16 | 16 | 1 | CP-1 new method only; CP-2 new method only; CP-3/CP-5 all 17 |
| GrokLinuxBlockingPromptTests (new) | 0 | 0 | 11 | CP-2/CP-5 |
| GrokTrustPromptDetectorTests | 4 | 4 | 0 | CP-3 |
| GrokStartupReadinessTests | 5 | 5 | 0 | CP-3/CP-5 |
| GrokLinuxStartupReadinessTests | 8 | 21 | 0 | CP-3 |
| RunnerGrokAdapterReadyTests | 13 | 14 | 0 | CP-4 |
| RunnerGrokAdapterReadyTestsPty | 1 | 2 | 0 | CP-4/CP-5 |
| RunnerGrokAdapterSignInPromptTests | 3 | 3 | 0 | CP-4 |
| RunnerGrokAdapterTrustPromptTests | 4 | 4 | 0 | CP-4 |
| GrokStartupCaptureStoreTests | 2 | 2 | 0 | CP-4 |

Census command is `rg -n '^\s*\[(Test|Arguments)|public (async Task|void) '`
against those literal files, followed by inspecting the bodies and argument
lists. Linux startup expansion is 2+2+3+3+4+3+2+2=21; ready adapter is
2+12=14; native PTY is false/true=2. There are no dynamic data sources here.
Thus CP-1..CP-5 remain **1/12/47/25/35**, or 120 result executions across rows
(119 expected green and one deliberate CP-1 wording failure). Overlapping rows
are intentional. The 114 Windows iterations, 57 probe cases and internal loops
do not increase MinExecuted.

- R-1: unchanged predicates and recorded classifications | CP-3; original eight
  sign-in anchor matches and login-with-alone/Codex nonmatches remain, Ready
  dashboards accept exactly their marker, status/hint/geometry negatives refuse.
- R-2: waiter, adapter, privacy and native fake recipient | CP-4; zero writes on
  sign-in, exactly one `y` on trust, trust must clear and settle, deadline/exit/
  cancellation refuse, sign-in capture has no screen/raw content, and complete
  native UserPrompt equals the sent body. No timeout widening for CARD-0988.
- R-3: native Windows plus portable replay | CP-5 on the final implementation
  SHA, all 35 executions with zero skips, modern backend explicitly selected;
  Linux's 114-frame replay does not substitute for this receipt.

Static audit completed in this TestDesign: unchanged D-1..D-5/fix-design prefix,
exactly one unchanged five-row checkpoint table, all minima and 35-minute sum,
49 unique guard/PC mappings, 22 exact PC methods, 19 synthetic recipes/hashes,
three REAL decoded hashes and five untouched investigation/fixture source files.
`git diff --check` passed. These are read-only artifact/source checks, **not**
TUnit, importer, production classification or mutation execution receipts.

### Code admission and landing freeze

| Gate | Owner | Required evidence / state at this freeze |
|---|---|---|
| A-1 exact production image | Caller/orchestrator | Literal selected production tag and immutable RepoDigest tied to that Linux container, before **any** further capture; still unavailable. Record synthetic-trust acceptance on CARD-1006. |
| A-2 isolated capture and raw-log custody | Separately commissioned Linux Debug task | D-2 throwaway-container/network/tmpfs/no-persistent-swap receipt; explicitly account for ordinary raw `.ansi.log` paths despite audit-off; verify owned children/container/network/scratch cleanup. Still absent; historical captures do not satisfy it. |
| A-3 verification freeze | This TestDesign artifact | Literal fixtures, methods, anchor placements and read-only census above; implementation and mutation remain unexecuted. |
| A-4 actual five-row importer | Code owner before S1 | No prebuilt Antiphon.Checkpoints.dll exists in this worktree or the runner canonical bare repository `/work/repos/antiphon`. No build/import was run. Other tasks' outputs are not borrowed. Bootstrap/import below is an explicit Code-admission item, not a TestDesign blocker. |

A-4 bootstrap exception (four estimated minutes, separate from ordinary CPs):
build only `tools/Antiphon.Checkpoints` through `scripts/build-slot.ps1` with
`--property:OutputPath=bin-c1006-tool/`; invoke its built dll with
`import --plan docs/superpowers/plans/2026-10-03-card-1006-grok-linux-signin-trust-plan.md --out .antiphon/c1006-code-import.yml`.
Require exit 0, exactly **five** rows, minima 1/12/47/25/35, all filters unchanged
and serial=true. The seven-row reference belongs to CARD-0959; do not import its
table as CARD-1006 evidence. Static Markdown parsing cannot close A-4. Remove
only owned alternate outputs after all children finish. No extra docs checkpoint
is commissioned by this TestDesign; earlier Plan commands/receipts are historical.

CARD-1004 is already landed; keep its classifier glyph support, two Linux frames
and all 114 Windows rows. CARD-1001 owns checkpoint Coverage and related repairs;
read/import is allowed, edits are not. CARD-0959 owns runner capability/version
and admission changes; CARD-1008 owns rollout, deployment scripts and volume
recycling. Either independent source change may land first, but any rollout must
finish before A-1 selection and that image must stay fixed through A-2. Recheck
current active file footprints; a real collision waits for the existing owner,
not a same-file/different-method exemption. Preserve CARD-0965 and all earlier
allowlist exclusions. No fetch/rebase/reset of this task branch is authorized.

Landing sequence remains freeze -> owned admission receipts -> S1 expected-red
wording test -> S2 implementation/fixtures -> S3 portable and Windows closure ->
ordinary Review -> linked post-land companion -> publication/server activation ->
separately commissioned SourceLanding Mutation. No provider turn, runner image
edit, credentials or operational restart is performed by this TestDesign.

### Guard inventory

The original **15 cycles are preserved**, including distinct PC-5a/PC-5b; no ID
is removed, merged or renumbered. Inspection found additional independent guards
behind the freeze's safety assertions. PC-15..PC-48 supplement them, giving
**49 separately applicable cycles**. The standing [TestDesign bundle](../../../server/Bundles/stage-test-design.md)
requires: "List every safety-critical guard, incl. untested; split independently
bypassable guards. Map each 1:1 to a distinct PC-n." That requirement takes precedence over treating the inherited
15-cycle estimate as complete. Five CPs, twelve methods and the 19/57 probe
inventory are unchanged. This is additional verification, not a production fix.

| Guard | Plan assertion / independently bypassable guard | Positive control |
|---|---|---|
| G-1 | D-1 runner-user remedy is platform-neutral | PC-1 |
| G-2 | D-1 failed-launch scope belongs to this GROK_HOME | PC-2 |
| G-3 | D-3 REAL approval is recognized by the sign-in screen predicate | PC-3 |
| G-4 | D-3 SYNTHETIC measured-anchor trust predicate remains recognized | PC-4 |
| G-5 | D-5 sign-in prevents the successful-composer return | PC-5a |
| G-6 | D-5 trust independently prevents the successful-composer return | PC-5b |
| G-7 | D-5 sign-in wins when both modal predicates match | PC-6 |
| G-8 | D-5 composer interior is exactly one approved empty marker | PC-7 |
| G-9 | D-5 enabled hint is required | PC-8 |
| G-10 | D-5 absent/truncated bottom border cannot be accepted | PC-9 |
| G-11 | D-5 exactly 30 rows are required | PC-10 |
| G-12 | D-5 a negative observation resets prior positive settlement | PC-11 |
| G-13 | D-5 sign-in never calls writeAsync | PC-12 |
| G-14 | D-5 affirmative trust write cannot itself grant Ready | PC-13 |
| G-15 | D-5 first positive observation cannot settle, even at zero settle | PC-14 |
| G-16 | V-6 sign-in classification reads current screen, not stale raw | PC-15 |
| G-17 | V-6 trust classification reads current screen, not stale raw | PC-16 |
| G-18 | R-1 terminal rows wider than 120 are unqualified | PC-17 |
| G-19 | D-5 composer left column is exactly 2 | PC-18 |
| G-20 | D-5 composer right column is exactly 117 | PC-19 |
| G-21 | D-5 upper row ends at the right border | PC-20 |
| G-22 | D-5 upper row prefix before the border is blank | PC-21 |
| G-23 | D-5 upper border interior consists only of U+2500 | PC-22 |
| G-24 | D-5 input row ends at the right border | PC-23 |
| G-25 | D-5 input left border is U+2502 | PC-24 |
| G-26 | D-5 input right border is U+2502 | PC-25 |
| G-27 | D-5 bottom left border is U+2570 | PC-26 |
| G-28 | D-5 bottom right border is U+256F | PC-27 |
| G-29 | P-17 busy row 22 independently blocks a valid composer | PC-28 |
| G-30 | R-1 nonblank row 23 independently blocks a valid composer | PC-29 |
| G-31 | V-10 trust may be answered at most once per wait | PC-30 |
| G-32 | V-10 the only frozen affirmative key is literal `y` | PC-31 |
| G-33 | V-8 elapsed settle interval is required after the first candidate | PC-32 |
| G-34 | R-1 changed composer region starts a new settlement interval | PC-33 |
| G-35 | V-9 recognized sign-in returns false to the waiter caller | PC-34 |
| G-36 | V-9 sign-inSeen is retained for capture suppression | PC-35 |
| G-37 | V-9 recognized sign-in invokes the launch-block callback | PC-36 |
| G-38 | V-3 trust requires the directory question, not just Yes, proceed | PC-37 |
| G-39 | V-3 trust requires Yes, proceed, not just the generic question | PC-38 |
| G-40 | R-2 sign-in capture suppresses both rendered and raw content | PC-39 |
| G-41 | R-1 ordinary approval prose is not a sign-in predicate | PC-40 |
| G-42 | R-1 approval-in-browser anchor independently recognizes sign-in | PC-41 |
| G-43 | R-1 waiting-for-approval anchor independently recognizes sign-in | PC-42 |
| G-44 | R-1 browser-code anchor independently recognizes sign-in | PC-43 |
| G-45 | R-1 token-entry anchor independently recognizes sign-in | PC-44 |
| G-46 | R-1 open-URL anchor independently recognizes sign-in | PC-45 |
| G-47 | R-1 browser-open-failure anchor independently recognizes sign-in | PC-46 |
| G-48 | R-1 explicit Sign in to Grok anchor independently recognizes sign-in | PC-47 |
| G-49 | R-1 login-with requires ctrl+q as its independent second anchor | PC-48 |

Inspection audit: **guards=49, mapped=49, missing=0, duplicate PC maps=0**.
Every listed PC below targets a production guard with a compiling edit and an
exact first-detecting method. All are executable against the frozen future test
bodies; none has been run. Do not equate design completeness with PC-clean.
Operational A-1/A-2 are external receipt gates, not compilable production guards;
there is no pretend mutant for a missing image or cleanup receipt. V-4's pinned
fixture integrity is a test oracle, not new production redaction logic.

Independent geometry witnesses added inside V-5's existing single-result method
are a separate `geometry` subloop; they do not replace, add to or relabel V-7's
57 cases. Start from L, alter exactly the rows below, and assert
`geometryReady:<id>` false **before** exact reason. The unmodified L control must
be Ready first. For narrowed boxes I(s) uses the stated total width and still
has an otherwise-empty marker. These ten witnesses are SYNTHETIC; their row maps
below are their literal definitions and they are generated only in memory.

| Witness | Exact change from L | Expected reason |
|---|---|---|
| left-3 | row24=`"   \u256d"+repeat("\u2500",113)+"\u256e"`; row25=`"   \u2502 "+PadRight("\u276f",112)+"\u2502"`; row26=`"   \u2570"+repeat("\u2500",113)+"\u256f"` (right stays 117) | Unknown |
| right-116 | row24=`"  \u256d"+repeat("\u2500",113)+"\u256e"`; row25=`"  \u2502 "+PadRight("\u276f",112)+"\u2502"`; row26=`"  \u2570"+repeat("\u2500",113)+"\u256f"` (left stays 2) | Unknown |
| upper-tail | append one U+0020 to row24; right remains 117 | Unknown |
| upper-prefix | replace row24[0] with `x` | Unknown |
| upper-dash | replace row24[50] with `=` | Unknown |
| input-tail | append one U+0020 to row25 | ComposerUnavailable |
| input-left | replace row25[2] with `x` | ComposerUnavailable |
| input-right | replace row25[117] with `x` | ComposerUnavailable |
| bottom-left | replace row26[2] with `x` | ComposerUnavailable |
| bottom-right | replace row26[117] with `x` | ComposerUnavailable |

Also add two in-method V-3 negative witnesses, both SYNTHETIC, without changing
P-01: B with row5 `"  Yes, proceed y"`, then B with row5
`"  Do you trust the contents of this directory?"`; `partialTrust:yes-only` and
`partialTrust:question-only` must be false on the production trust detector.
They prohibit a generic question or isolated menu label from authorizing `y`.
V-8 additionally runs a zero-settle tracker: first Ready at 0=false, second at
0=true, as required by G-15. V-10's persistent-trust assertion order is result
false, literal write list `["y"]`, Trust outcome, simulated 1000ms elapsed;
this makes PC-30's first failure the duplicated writes, not a downstream timeout.
The V-10 driver pumps installed poll timers until the waiter completes or its
2000ms MaxWait is reached; 1000ms is the expected result time, not a harness
stop. Thus PC-30 can finish at the production deadline and fail its write-list
assertion. Race every next-poll wait against waiter completion so PC-13's early
true reaches `persistentTrustReady` instead of a missing-timer error.

### Positive controls

The original PC table under Execution remains historical shorthand. This exact
freeze supplies **one mutation per row**; PC-5a and PC-5b are separate rows/runs.
Restore source before the next row. Controls do not combine defects or rely on
another mutation. Existing methods with Arguments still use one exact method
filter and require all its argument results; no class wildcard is permitted.
All names below expand literally to `/*/*/ClassName/ExactTestMethod`.

| PC | Compiling defect to apply alone | Exact first-detecting method | First red assertion / witness |
|---|---|---|---|
| PC-1 | In the landed D-1 string replace `as the user` with `as the Windows user`, changing nothing else. | `GrokSignInPromptDetectorTests.C1006_Block_reason_is_platform_neutral` | `blockReasonExact`, POSIX home |
| PC-2 | Replace `Every Grok pool launch using that GROK_HOME` with `Every Grok pool launch on this machine`. | `GrokSignInPromptDetectorTests.C1006_Block_reason_is_platform_neutral` | `blockReasonExact`, POSIX home |
| PC-3 | Replace sign-in IsVisibleOnScreen body with `return false;`. | `GrokLinuxBlockingPromptTests.C1006_Real_sign_in_is_not_ready` | `realSignInDetector:C1-sign-in:original` expected true |
| PC-4 | Replace trust IsVisibleOnScreen expression with `false`. | `GrokLinuxBlockingPromptTests.C1006_Synthetic_trust_is_not_ready` | `syntheticTrustDetector:P-01` expected true |
| PC-5a | Move only Classify's sign-in early-return statement to immediately after the entire composer-search loop, before final Unknown. | `GrokLinuxBlockingPromptTests.C1006_Blockers_override_valid_composers` | `modalReason:W:C1-sign-in`, expected SignIn becomes Ready |
| PC-5b | Move only Classify's trust early-return statement to immediately after that loop, before final Unknown. | `GrokLinuxBlockingPromptTests.C1006_Blockers_override_valid_composers` | `modalReason:W:P-01`, expected Trust becomes Ready |
| PC-6 | Exchange the two early modal checks in Classify, trust first. | `GrokLinuxBlockingPromptTests.C1006_Sign_in_precedes_trust_and_types_nothing` | `mixedReason:C1-sign-in` expected SignIn becomes Trust |
| PC-7 | Replace the exact-interior rejection with rejection only when trimmed interior is not `>` AND interior does not contain U+276F. | `GrokLinuxBlockingPromptTests.C1006_All_19_fail_open_shapes_stay_closed` | `probeReady:P-08:base` expected false becomes true |
| PC-8 | Delete only the enabled-hint rejection block. | `GrokLinuxBlockingPromptTests.C1006_All_19_fail_open_shapes_stay_closed` | `probeReady:P-09:base` first becomes true (then P-10/P-16); P-09's full box has blank hint |
| PC-9 | Immediately after `var bottom = lines[top + 2];`, add `if (bottom.Length == 0) bottom = "  \u2570" + new string('\u2500', 114) + "\u256f";`. | `GrokLinuxBlockingPromptTests.C1006_All_19_fail_open_shapes_stay_closed` | `probeReady:P-18:base` expected false becomes true, no indexing exception |
| PC-10 | Delete only `lines.Length != 30 \|\|` from Classify's geometry condition, keeping the width clause. | `GrokLinuxBlockingPromptTests.C1006_All_19_fail_open_shapes_stay_closed` | `probeReady:P-19:base` expected false becomes true |
| PC-11 | Delete only `Reset();` inside Observe's negative branch. | `GrokLinuxBlockingPromptTests.C1006_Blocker_resets_readiness_settlement` | `resetCount:C1-sign-in` expected 0 becomes 1 |
| PC-12 | In WaitAsync's SignIn branch before its failure return, insert `if (writeAsync is not null) await writeAsync(GrokTrustPromptDetector.AffirmativeKey, ct);`. | `GrokLinuxBlockingPromptTests.C1006_Sign_in_precedes_trust_and_types_nothing` | `signInInputs:C1-sign-in` expected empty contains `y` |
| PC-13 | Insert `if (trustWritten) return true;` at the end of WaitAsync's Trust branch. | `GrokLinuxBlockingPromptTests.C1006_Trust_remains_blocked_until_cleared` | `persistentTrustReady` expected false becomes true |
| PC-14 | Change only the new-region branch's `return false;` in Observe to `return true;`. | `GrokLinuxBlockingPromptTests.C1006_Blocker_resets_readiness_settlement` | `firstReadyObservation` expected false becomes true |
| PC-15 | In Classify only, pass `rawOutput ?? screen` to the sign-in screen detector. | `GrokLinuxBlockingPromptTests.C1006_Current_frame_overrides_raw_history` | `currentReason:C1-sign-in:L` expected SignIn becomes Unknown |
| PC-16 | In Classify only, use `GrokTrustPromptDetector.IsVisible(rawOutput, screen)` for the trust check. | `GrokLinuxBlockingPromptTests.C1006_Current_frame_overrides_raw_history` | `currentReason:W:P-01` expected Ready becomes Trust |
| PC-17 | Delete only the `lines.Any(line => line.Length > 120)` arm, retaining row count. | `GrokLinuxStartupReadinessTests.Unqualified_geometry_is_unknown` | 30-row/121-width case Reason Unknown becomes Ready; 2 results required |
| PC-18 | Delete only `left != 2` from upper-geometry rejection. | `GrokLinuxBlockingPromptTests.C1006_Blockers_override_valid_composers` | `geometryReady:left-3` false becomes true |
| PC-19 | Delete only `right != 117` from upper-geometry rejection. | `GrokLinuxBlockingPromptTests.C1006_Blockers_override_valid_composers` | `geometryReady:right-116` false becomes true |
| PC-20 | Delete only `upper.Length != right + 1` from upper-geometry rejection. | `GrokLinuxBlockingPromptTests.C1006_Blockers_override_valid_composers` | `geometryReady:upper-tail` false becomes true |
| PC-21 | Delete only `upper[..left].Trim().Length != 0` from upper-geometry rejection. | `GrokLinuxBlockingPromptTests.C1006_Blockers_override_valid_composers` | `geometryReady:upper-prefix` false becomes true |
| PC-22 | Delete only `upper[(left + 1)..right].Any(c => c != '\u2500')` from upper-geometry rejection. | `GrokLinuxBlockingPromptTests.C1006_Blockers_override_valid_composers` | `geometryReady:upper-dash` false becomes true |
| PC-23 | Delete only `input.Length != right + 1` from the input/bottom validation condition. | `GrokLinuxBlockingPromptTests.C1006_Blockers_override_valid_composers` | `geometryReady:input-tail` false becomes true |
| PC-24 | Delete only `input[left] != '\u2502'` from that condition. | `GrokLinuxBlockingPromptTests.C1006_Blockers_override_valid_composers` | `geometryReady:input-left` false becomes true |
| PC-25 | Delete only `input[right] != '\u2502'` from that condition. | `GrokLinuxBlockingPromptTests.C1006_Blockers_override_valid_composers` | `geometryReady:input-right` false becomes true |
| PC-26 | Delete only `bottom[left] != '\u2570'` from that condition. | `GrokLinuxBlockingPromptTests.C1006_Blockers_override_valid_composers` | `geometryReady:bottom-left` false becomes true |
| PC-27 | Delete only `bottom[right] != '\u256f'` from that condition. | `GrokLinuxBlockingPromptTests.C1006_Blockers_override_valid_composers` | `geometryReady:bottom-right` false becomes true |
| PC-28 | Delete the complete nonblank `lines[top - 2]` status rejection block. | `GrokLinuxBlockingPromptTests.C1006_All_19_fail_open_shapes_stay_closed` | `probeReady:P-17:base` false becomes true |
| PC-29 | Delete the complete nonblank `lines[top - 1]` status rejection block. | `GrokLinuxStartupReadinessTests.Populated_rows_around_linux_composer_block_readiness` | row23 unexpected-status case Reason Unknown becomes Ready; 4 results required |
| PC-30 | Replace `!trustWritten && writeAsync is not null` with `writeAsync is not null`. | `GrokLinuxBlockingPromptTests.C1006_Trust_remains_blocked_until_cleared` | `persistentTrustInputs` expected `["y"]` contains repeated `y` |
| PC-31 | Set GrokTrustPromptDetector.AffirmativeKey to `"x"`. | `GrokLinuxBlockingPromptTests.C1006_Trust_remains_blocked_until_cleared` | `persistentTrustInputs` expected literal `["y"]`, actual `["x"]` |
| PC-32 | In Observe's final return delete the elapsed-time conjunct, keeping `PositiveObservations >= 2`. | `GrokLinuxBlockingPromptTests.C1006_Blocker_resets_readiness_settlement` | `readyAt1000:C1-sign-in` false becomes true after reset/restart at 950 |
| PC-33 | Change `_region != observation.Region` to `_region is null`. | `GrokStartupReadinessTests.Composer_change_or_blocker_restarts_settle` | changed-region observation at 1000ms expected false becomes true |
| PC-34 | Replace only SignIn branch's `return Fail(GrokStartupReason.SignIn);` with `return true;`. | `GrokLinuxBlockingPromptTests.C1006_Sign_in_precedes_trust_and_types_nothing` | `signInReady:C1-sign-in` expected false becomes true |
| PC-35 | Delete only `signInSeen = true;` in WaitAsync. | `GrokLinuxBlockingPromptTests.C1006_Sign_in_precedes_trust_and_types_nothing` | `failureSignInSeen:C1-sign-in` expected true becomes false |
| PC-36 | Delete only the `if (frame is not null) options.OnSignIn?.Invoke(frame);` statement. | `GrokLinuxBlockingPromptTests.C1006_Sign_in_precedes_trust_and_types_nothing` | `signInCallbackCount:C1-sign-in` expected 1 becomes 0 |
| PC-37 | Trust CompactMatch returns only `compact.Contains("yes,proceed")`. | `GrokLinuxBlockingPromptTests.C1006_Synthetic_trust_is_not_ready` | `partialTrust:yes-only` expected false becomes true |
| PC-38 | Trust CompactMatch returns only the directory-question Contains expression. | `GrokLinuxBlockingPromptTests.C1006_Synthetic_trust_is_not_ready` | `partialTrust:question-only` expected false becomes true |
| PC-39 | Replace `if (signInSeen)` with `if (signInSeen && frame is null)` in GrokStartupCaptureStore.Format. | `GrokStartupCaptureStoreTests.Content_is_bounded_and_sign_in_material_is_suppressed` | suppressed output must contain `content: suppressed after sign-in`; marker is absent |
| PC-40 | Replace the first sign-in anchor `approveinyourbrowsertofinishsigningin` with `approve`. | `GrokSignInPromptDetectorTests.A_reply_that_mentions_approval_without_the_anchors_is_not_sign_in` | ordinary approval prose expected false becomes true |
| PC-41 | Replace only the sign-in approval-in-browser Contains term with `false`. | `GrokSignInPromptDetectorTests.Matches_approve_in_your_browser` | ShouldBeTrue fails on the sole approval phrase |
| PC-42 | Replace only the waiting-for-approval Contains term with `false`. | `GrokSignInPromptDetectorTests.Matches_waiting_for_approval` | ShouldBeTrue fails on the sole waiting phrase |
| PC-43 | Replace only the make-sure-browser-code Contains term with `false`. | `GrokSignInPromptDetectorTests.Matches_make_sure_your_browser_shows_this_code` | ShouldBeTrue fails on the sole browser-code phrase |
| PC-44 | Replace only the paste-token Contains term with `false`. | `GrokSignInPromptDetectorTests.Matches_paste_your_token_here_welcome` | second ShouldBeTrue fails on `Paste your token here`; full welcome still matches login-with/ctrl+q |
| PC-45 | Replace only the open-URL Contains term with `false`. | `GrokSignInPromptDetectorTests.Matches_open_this_url_in_your_browser_to_approve` | ShouldBeTrue fails on the sole open-URL phrase |
| PC-46 | Replace only the could-not-open-browser Contains term with `false`. | `GrokSignInPromptDetectorTests.Matches_could_not_open_a_browser` | ShouldBeTrue fails on the sole browser-failure phrase |
| PC-47 | Replace only the signin-to-grok Contains term with `false`. | `GrokSignInPromptDetectorTests.Matches_sign_in_to_grok` | ShouldBeTrue fails on the sole Sign in to Grok phrase |
| PC-48 | Remove only `&& compact.Contains("ctrl+q")` from the login-with alternative. | `GrokSignInPromptDetectorTests.Login_with_alone_is_not_the_sign_in_screen` | login-with-only ShouldBeFalse becomes true |

All conjunct deletions remove their adjacent boolean operator too, keeping valid
C#. PC-5a/b move live statements after the loop, not after an unconditional
return inside it; unreachable code/build errors do not count. PC-8's corrected
first failure is P-09, not P-16: inspection of the literal full-width bare box
shows that removing the hint admits it at row 9. The preserved mutation is the
same; the asserted first witness is now accurate.

Mutation executes baseline, break/red, exact restore, green with
MinExecuted=1, except PC-17=2 and PC-29=4 for existing argument expansions.
Use one isolated output and one exact method filter per invocation through the
host build-slot/checkpoint driver. Break/red/restore/green occurs **after land**;
Code executes the unchanged ordinary V/R table and Review judges this design
before land. Red must be the specified outcome assertion, not zero tests,
missing files, compilation, timer registration or harness timeout. SourceLanding
writes no snapshot commit/push: all receipts and restoration hashes go in the
caller-assigned external evidence root. A repair requires separate Code/Review/
land and a fresh snapshot.

### Out of scope

- A real Linux trust or updater screen remains unqualified. Synthetic coverage
  cannot choose vendor action keys, claim an observed menu, or license input.
  A novel captured modal needs the D-4 revised predicate/PC design before Code.
- No changed durable delivery path exists. Queue retry, busy/eligible recipients,
  enqueue failure and crash recovery require their own real-queue/transcript
  evidence when edited; direct fake input is not a substitute.
- Existing generic cancellation, exit, deadline/minimum-age, capture retention
  and file-size bounds are exercised by the frozen R-2 class roster but not
  changed or re-qualified by this card. Their production policies and mutation
  inventory remain CARD-0778's; incidental selection is not a claim that the
  49 controls enumerate every guard in the whole session runtime. No new guard
  or claim in D-1/D-3/D-5/V-1..V-12 is waived on that basis.
- Redundant structural checks `right <= left + 2` and `top + 4 >= lines.Length`
  cannot independently admit a bad frame while exact left/right and loop bounds
  stand. They need no independent positive control; removing either alone is
  semantically equivalent under the retained predicates. The same applies to
  the count>=2 conjunct after the new-region branch always returns false.
- MCP/header redraws and variable footer text are intentionally allowed by the
  existing classifier; tests must not turn their text into unmeasured gates.
- Other terminal sizes/glyphs/versions, real provider turns, raw-ANSI sanitizer
  implementation, auth files, deployment/runner changes, CARD-1001 Coverage and
  CARD-0959/1008 source edits remain excluded. The ten geometry and two partial-
  trust witnesses above are synthetic boundary controls, not new measured UI.

### Checkpoints

Closed ordinary Code list after capture admission. Each row owns one isolated
build and one literal filter. CP-1 is expected-red diagnostic evidence, not a
green certificate; CP-2..CP-5 require zero failures/skips. `portable-*` groups use
the default lane; `windows-*` requires a separate Windows task. No platform column
is added to the importer. Rows are serial to avoid overlapping native children.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1006-red/` | portable-wording-red | `/*/*/GrokSignInPromptDetectorTests*/C1006_Block_reason_is_platform_neutral*` | V-1 | 1 executed, 1 expected message assertion failure at unchanged production | 1 | 6 | true |
| CP-2 | S2 | `tests/Antiphon.Tests -> bin-c1006-blockers/` | portable-blockers | `/*/*/(GrokSignInPromptDetectorTests*)\|(GrokLinuxBlockingPromptTests*)/C1006_*` | V-1..V-12 | all 12 named single-result methods, 0 failed/skipped | 12 | 6 | true |
| CP-3 | S3 | `tests/Antiphon.Tests -> bin-c1006-regression/` | portable-classifier-regression | `/*/*/(GrokSignInPromptDetectorTests*)\|(GrokTrustPromptDetectorTests*)\|(GrokStartupReadinessTests*)\|(GrokLinuxStartupReadinessTests*)/*` | R-1 | all 47 expanded results, 0 failed/skipped | 47 | 7 | true |
| CP-4 | S3 | `tests/Antiphon.Tests -> bin-c1006-adapters/` | portable-adapter-native-privacy | `/*/*/(RunnerGrokAdapterReadyTests*)\|(RunnerGrokAdapterSignInPromptTests*)\|(RunnerGrokAdapterTrustPromptTests*)\|(GrokStartupCaptureStoreTests*)/*` | R-2 | all 25 expanded results including 2 native PTY cases, 0 failed/skipped | 25 | 8 | true |
| CP-5 | S3 | `tests/Antiphon.Tests -> bin-c1006-windows/` | windows-conpty-regression | `/*/*/(GrokStartupReadinessTests*)\|(GrokLinuxBlockingPromptTests*)\|(RunnerGrokAdapterReadyTestsPty*)\|(GrokSignInPromptDetectorTests*)/*` | R-3, V-12 | all 35 expanded results on Windows, 0 failed/skipped | 35 | 8 | true |

## Execution and post-land controls

Commit/push each slice before building. Code uses the checkpoint tool once per
committed slice group with `--expected-source-sha <full-sha>` and explicit row
selection; CP-1 alone, CP-2 after S2, CP-3/CP-4 after S3, CP-5 on Windows. Never
run the entire manifest on Linux and mislabel CP-5. Wait while exit is 75, keep
all children owned, and leave source unchanged until each run finishes. The
optional tool bootstrap is the only declared extra build: an alternate
`bin-c1006-tool/` output through `scripts/build-slot.ps1`. Do not double-wrap the
self-leasing checkpoint drivers. Exit 4 is not-run, not permission to bypass
the slot gate. Preserve exact CHECKPOINT lines, source receipts and actual counts.
Final Review uses CP-2..CP-5 at its reviewed SHA, with the Windows receipt linked.

### Historical inherited controls for SourceLanding Mutation

The 15-row outline below is retained from Plan. The TestDesign **Positive controls**
section above is authoritative for exact mutations/first assertions and adds 34
independent controls (49 total). These are post-land obligations, not tests run
by this Plan or ordinary Code.
The caller records the same-board companion before landing and commissions a
fresh SourceLanding Mutation after publication. Every cycle uses exactly
`/*/*/ClassName/ExactTestMethod` from the Method column, without a class wildcard.
Run each variant separately, expect the named assertion failure, restore bytes,
then run the same method green. Zero tests, fixture absence and build errors do
not count as red. No snapshot commits/pushes; keep receipts/restoration records in
the caller-assigned external evidence root. Repairs require a separate Code task.

| PC | Production mutation (one per cycle) | Exact method | First expected failing assertion / input |
|---|---|---|---|
| PC-1 | Restore “as the Windows user” in BlockReason. | `GrokSignInPromptDetectorTests.C1006_Block_reason_is_platform_neutral` | Full message equality for POSIX home. |
| PC-2 | Replace per-GROK_HOME launch scope with the old machine-wide statement. | `GrokSignInPromptDetectorTests.C1006_Block_reason_is_platform_neutral` | Full message equality. |
| PC-3 | Make sign-in IsVisibleOnScreen return false. | `GrokLinuxBlockingPromptTests.C1006_Real_sign_in_is_not_ready` | Detector true on real sign-in. |
| PC-4 | Make trust IsVisibleOnScreen return false. | `GrokLinuxBlockingPromptTests.C1006_Synthetic_trust_is_not_ready` | Detector true on labelled synthetic P-01 trust. |
| PC-5a / PC-5b | Move respectively sign-in or trust classification after successful composer return. | `GrokLinuxBlockingPromptTests.C1006_Blockers_override_valid_composers` | Expected SignIn/Trust on otherwise-ready composer becomes Ready. |
| PC-6 | Check trust before sign-in in Classify. | `GrokLinuxBlockingPromptTests.C1006_Sign_in_precedes_trust_and_types_nothing` | Combined frame Reason=SignIn fails. |
| PC-7 | Replace exact composer interior match with Contains(U+276F). | `GrokLinuxBlockingPromptTests.C1006_All_19_fail_open_shapes_stay_closed` | P-08 not-ready fails (also P-13..P-15). |
| PC-8 | Remove enabled-hint rejection. | `GrokLinuxBlockingPromptTests.C1006_All_19_fail_open_shapes_stay_closed` | P-16 not-ready fails. |
| PC-9 | Before bottom validation, replace an empty bottom with `"  ╰" + new string('─', 114) + "╯"`, thereby accepting a missing border. | `GrokLinuxBlockingPromptTests.C1006_All_19_fail_open_shapes_stay_closed` | P-18 not-ready fails at an outcome assertion, without an indexing exception. |
| PC-10 | Permit 29 rows by removing only the row-count rejection. | `GrokLinuxBlockingPromptTests.C1006_All_19_fail_open_shapes_stay_closed` | P-19 not-ready fails. |
| PC-11 | Retain the old tracker region/time on a negative observation. | `GrokLinuxBlockingPromptTests.C1006_Blocker_resets_readiness_settlement` | PositiveObservations=0 after real approval or synthetic trust fails; without that assertion, Ready at 1000 must fail. |
| PC-12 | Write affirmative key in WaitAsync's sign-in branch before failing. | `GrokLinuxBlockingPromptTests.C1006_Sign_in_precedes_trust_and_types_nothing` | Zero inputs fails on real sign-in. |
| PC-13 | Treat an uncleared trust frame as Ready after the affirmative write. | `GrokLinuxBlockingPromptTests.C1006_Trust_remains_blocked_until_cleared` | Persistent trust must return false/Trust. |
| PC-14 | Allow first ready observation immediately. | `GrokLinuxBlockingPromptTests.C1006_Blocker_resets_readiness_settlement` | First observation at 0 ms must be false. |

PC-5 has two independent cycles; 15 cycles in the inherited outline, all preserved
in the 49-cycle TestDesign freeze. If D-4 needs a real updater
detector, TestDesign adds a method-bound knockout/precedence PC with its real
captured input before Code; the current roster does not pretend to cover an
unobserved vendor modal. Keep the unchanged privacy regression in ordinary R-2.

### Cost

All figures are **estimated**, not measured. This docs-only TestDesign ran no
build, runtime test, capture or mutation. Ordinary Code V/R floor remains
**35 minutes**, exactly CP-1..CP-5's 6+6+7+8+8, each including its isolated build.
The filters are the unchanged five table rows; minima remain 1/12/47/25/35.
The separate Code importer bootstrap is **4 minutes**, so ordinary Code setup
plus verification is **39 minutes**, before authoring or host-slot waits.

Mutation uses a four-minute isolated build/run for each distinct method baseline,
a four-minute isolated mutant build/red run, 0.25 minutes to restore/check exact
bytes, and a four-minute isolated restored build/green run: **8.25 minutes per
PC**. All PCs can execute portably; Windows native qualification remains CP-5.
The method-specific costs and filters below include all 49 cycles, including the
preserved original 15 and 34 additional independent controls.

| Exact method filter (all portable) | PCs | Minimum results per invocation | Baseline minutes | Cycle minutes | Family floor minutes |
|---|---|---:|---:|---:|---:|
| `/*/*/GrokSignInPromptDetectorTests/C1006_Block_reason_is_platform_neutral` | PC-1, PC-2 | 1 | 4 | 2 x 8.25 | 20.5 |
| `/*/*/GrokLinuxBlockingPromptTests/C1006_Real_sign_in_is_not_ready` | PC-3 | 1 | 4 | 1 x 8.25 | 12.25 |
| `/*/*/GrokLinuxBlockingPromptTests/C1006_Synthetic_trust_is_not_ready` | PC-4, PC-37, PC-38 | 1 | 4 | 3 x 8.25 | 28.75 |
| `/*/*/GrokLinuxBlockingPromptTests/C1006_Blockers_override_valid_composers` | PC-5a, PC-5b, PC-18, PC-19, PC-20, PC-21, PC-22, PC-23, PC-24, PC-25, PC-26, PC-27 | 1 | 4 | 12 x 8.25 | 103 |
| `/*/*/GrokLinuxBlockingPromptTests/C1006_Sign_in_precedes_trust_and_types_nothing` | PC-6, PC-12, PC-34, PC-35, PC-36 | 1 | 4 | 5 x 8.25 | 45.25 |
| `/*/*/GrokLinuxBlockingPromptTests/C1006_All_19_fail_open_shapes_stay_closed` | PC-7, PC-8, PC-9, PC-10, PC-28 | 1 | 4 | 5 x 8.25 | 45.25 |
| `/*/*/GrokLinuxBlockingPromptTests/C1006_Blocker_resets_readiness_settlement` | PC-11, PC-14, PC-32 | 1 | 4 | 3 x 8.25 | 28.75 |
| `/*/*/GrokLinuxBlockingPromptTests/C1006_Trust_remains_blocked_until_cleared` | PC-13, PC-30, PC-31 | 1 | 4 | 3 x 8.25 | 28.75 |
| `/*/*/GrokLinuxBlockingPromptTests/C1006_Current_frame_overrides_raw_history` | PC-15, PC-16 | 1 | 4 | 2 x 8.25 | 20.5 |
| `/*/*/GrokLinuxStartupReadinessTests/Unqualified_geometry_is_unknown` | PC-17 | 2 | 4 | 1 x 8.25 | 12.25 |
| `/*/*/GrokLinuxStartupReadinessTests/Populated_rows_around_linux_composer_block_readiness` | PC-29 | 4 | 4 | 1 x 8.25 | 12.25 |
| `/*/*/GrokStartupReadinessTests/Composer_change_or_blocker_restarts_settle` | PC-33 | 1 | 4 | 1 x 8.25 | 12.25 |
| `/*/*/GrokStartupCaptureStoreTests/Content_is_bounded_and_sign_in_material_is_suppressed` | PC-39 | 1 | 4 | 1 x 8.25 | 12.25 |
| `/*/*/GrokSignInPromptDetectorTests/A_reply_that_mentions_approval_without_the_anchors_is_not_sign_in` | PC-40 | 1 | 4 | 1 x 8.25 | 12.25 |
| `/*/*/GrokSignInPromptDetectorTests/Matches_approve_in_your_browser` | PC-41 | 1 | 4 | 1 x 8.25 | 12.25 |
| `/*/*/GrokSignInPromptDetectorTests/Matches_waiting_for_approval` | PC-42 | 1 | 4 | 1 x 8.25 | 12.25 |
| `/*/*/GrokSignInPromptDetectorTests/Matches_make_sure_your_browser_shows_this_code` | PC-43 | 1 | 4 | 1 x 8.25 | 12.25 |
| `/*/*/GrokSignInPromptDetectorTests/Matches_paste_your_token_here_welcome` | PC-44 | 1 | 4 | 1 x 8.25 | 12.25 |
| `/*/*/GrokSignInPromptDetectorTests/Matches_open_this_url_in_your_browser_to_approve` | PC-45 | 1 | 4 | 1 x 8.25 | 12.25 |
| `/*/*/GrokSignInPromptDetectorTests/Matches_could_not_open_a_browser` | PC-46 | 1 | 4 | 1 x 8.25 | 12.25 |
| `/*/*/GrokSignInPromptDetectorTests/Matches_sign_in_to_grok` | PC-47 | 1 | 4 | 1 x 8.25 | 12.25 |
| `/*/*/GrokSignInPromptDetectorTests/Login_with_alone_is_not_the_sign_in_screen` | PC-48 | 1 | 4 | 1 x 8.25 | 12.25 |

PC floor: **492.25 minutes = 22 method baselines x4 + 49 cycles x8.25**;
with four minutes of Mutation driver/evidence setup, **496.25 minutes**.
The original 15 cycles account for 32 baseline + 123.75 cycle minutes; the
additional guard coverage adds 56 baseline + 280.5 cycle minutes. Each red/
green receipt remains separate even when several controls use the same method.
This is 120 method invocations (22 baseline + 49 red + 49 green); argument
expansion makes **132 TUnit executions**, not 120 (PC-17 adds three results over
its three invocations and PC-29 adds nine). Fixture and probe loops are not counted.

Combined build/setup + ordinary V/R + full PC floor is **535.25 minutes**:
4 Code setup + 35 CPs + 4 Mutation setup + 88 baselines + 404.25 cycles.
A separate ordinary Review repeats CP-2..CP-5 (29 minutes), making **564.25**.
The separately commissioned A-2 environment setup/capture/cleanup receipt is
estimated at 20 minutes once A-1 image identity is supplied: including it gives
**584.25 minutes** for admission, Code, Review and Mutation. Image discovery,
authoring, slot waits, Windows scheduling and repairs are additional external
latency, not invented measured run time.

Reusing each clean method baseline after verified restoration saves
**108 estimated minutes** versus 49 separate four-minute baselines:
(49-22)x4. Measured savings=0; no runtime execution occurred here. No whole-suite
run or class-wide mutation run substitutes for these exact method selections.

## Plan-stage verification

Commit this plan before the following direct self-leasing documentation check.
This follows CARD-1008's docs selection; it does not assert this new plan's runtime
behavior. At this baseline there are 11 DockerStackDocumentationTests, 20
CheckpointImportTests and 6 CheckpointManifestTests: **37 results**.

```powershell
pwsh -NoProfile -File scripts/run-checkpoint.ps1 -Name DOCS-1006 -Project tests/Antiphon.Tests -OutputPath bin-c1006-plan/ -Filter '/*/*/(DockerStackDocumentationTests*)|(CheckpointImportTests*)|(CheckpointManifestTests*)/*' -MinExecuted 37 -Expect DockerStackDocumentationTests,CheckpointImportTests,CheckpointManifestTests -ExpectedSourceSha <full-plan-sha> -ResultsRoot .antiphon/c1006-plan-checkpoints
```

Use literal pipes in the command (Markdown table cells above escape them).
The docs build also builds the tool via the test project's existing reference.
Run the actual importer from that isolated output, not a hand-written table parser:

```sh
dotnet tools/Antiphon.Checkpoints/bin-c1006-plan/Antiphon.Checkpoints.dll import --plan docs/superpowers/plans/2026-10-03-card-1006-grok-linux-signin-trust-plan.md --out .antiphon/c1006-plan-checkpoints/imported.yml
```

Import is a foreground metadata command, not another build/test. It must accept
all five CP rows, retain their counts/filters/slices and serial flags, and decode
literal pipes. Validate the checkpoint source receipt against the tested SHA.
Remove only task-owned `bin-c1006-plan/` outputs after all commands are finished;
retain `.antiphon` receipts. For amendment task `3fdafc77`, commit/push the complete
plan first, then run this documentation checkpoint **once at the final SHA**
and import at that same SHA. Report actual outcomes and source-receipt location
in the task report; do not make a later prose-only commit that would move the
final SHA beyond its test. The direct script self-leases; do not double-wrap or
use the owner-verified checkpoint execution tool for this Plan task (CARD-0853).
No extra build beyond this checkpoint. Grep stale real-trust/next-stage references,
compare numbered rosters and verify the investigation files are unchanged.

### Historical plan verification receipt (original task e6f1295a, 2026-10-03)

DOCS-1006 ran at committed source
`4b168744303a93afab6bacc00127fb0b609e6bdb`: **37 executed, 37 passed, 0 failed,
0 skipped**. Isolated build: 0 errors (581 compiler/analyzer warnings in unchanged
code). Slot granted after 60 seconds; held for 159 seconds. The source validator
returned `CHECKPOINT SOURCE VALID`, one row, clean source and verified build.

```text
CHECKPOINT DOCS-1006 commit=4b168744303a93afab6bacc00127fb0b609e6bdb build=ok filter=/*/*/(DockerStackDocumentationTests*)|(CheckpointImportTests*)|(CheckpointManifestTests*)/* executed=37 passed=37 failed=0 skipped=0 trx=/work/worktrees/task-e6f1295a/.antiphon/c1006-plan-checkpoints/DOCS-1006-20261003-123243-0690/run.trx slot=granted waited=60s dirty=0 source=4b168744303a93afab6bacc00127fb0b609e6bdb sourceState=clean buildSource=verified
```

The actual importer exited 0 and accepted **5 rows**, with minima 1/12/47/25/35,
all five independent output paths, literal pipe filters and `serial: true`.
Evidence root: `.antiphon/c1006-plan-checkpoints/`; imported manifest:
`imported.yml`; source receipt:
`DOCS-1006-20261003-123243-0690/source.json`.

Those receipts belong to the original Plan task, whose final `374fefe1` import
accepted five rows. They are not evidence that the present amendment passed.
This amendment keeps the checkpoint table/minima unchanged and changes fixture
provenance and coverage claims as described above. Its own final-SHA test/import
receipts are delivered with task `3fdafc77`. Runtime V/R and all PCs remain pending.

## Risks, landing order and follow-ups

1. S-0's A-1/A-2 exact-image and custody receipt remains pending, owned by the
   caller and Linux Debug helper; TestDesign can work from the committed real
   frames now. No further credential-free trust trial is required or represented
   as likely to succeed. The synthetic-trust decision is explicit; any signed-in
   real-trust follow-up is human-gated, outside scope and only a note here.
2. Real UI wording, menu keys or geometry may differ from 1.0.13. Return novel
   anchors/actions to TestDesign, amend/import this plan, then implement. Never
   broaden Ready to accommodate a modal. Redaction must not invent the anchors.
3. Updater qualification is conditional and explicitly pending when not observed;
   the two synthetic shapes are limited evidence. Capture any encountered real
   frame before deciding whether production needs a detector.
4. Publish this plan and its inherited investigation evidence, then TestDesign;
   close A-1/A-2/A-3 before Code. Follow with
   separate Final Review, durable post-land companion, confirmed Code publication
   and server activation. The orchestrator activates from the canonical checkout
   through its AppHost restart policy and verifies `/api/version` equals the
   activated source SHA. No runner rollout, auth-file operation or provider-spend
   canary is authorized by this plan. Existing successful CARD-1004 Linux canaries
   remain historical evidence, not a new execution of this card.
5. Execute every post-land PC against the confirmed SourceLanding snapshot and
   retain external restoration evidence. An implementation may be Done with the
   explicitly linked pending obligation; never claim PC-clean before it runs.

FOLLOW-UPS: the original Plan and Investigate tasks record completed board
searches; Investigate's `ANSI log` search found five matches and treats ordinary
raw-log custody as this card's admission issue, not a production logging defect.
This amendment creates no task/card. CARD-0857 owns ambient MCP isolation;
CARD-0988 owns loaded trust timing; CARD-0861 owns terminal sizes; CARD-1011 owns
real Windows canaries/routing. None substitutes for CP-5. The caller/human owns
the signed-in trust follow-up note; the session-runtime maintainer owns any
separately commissioned capture-only no-log policy (D-2). Neither widens this
card or authorizes credentials. Landing coordination is the explicit table above.

--- next stage ---
next: code
handoff: Implement frozen S1-S3 only after caller A-1 image/acceptance and Linux Debug A-2 cleanup/custody receipts, current-footprint check and Code A-4 real five-row import. Preserve 12 methods, 19/57 probes, CP minima 1/12/47/25/35, Windows SHA/35-result receipt; retain all 15 original PCs plus 34 mandatory independent controls (49 total) for post-land Mutation. No detector predicate change.
artifact: docs/superpowers/plans/2026-10-03-card-1006-grok-linux-signin-trust-plan.md
