# CARD-1006: Grok Linux sign-in and trust qualification

Date: 2026-10-03. Plan task: `e6f1295a`. Baseline:
`f0fadc61635e3d977f045876d65e1e09ef06b17a`. This is a documentation-only dispatch.
CARD-1006's three items and Acceptance were read through `card.ps1 get`.

## Outcome and admission

Make the sign-in remedy platform-neutral, qualify real Linux blocking screens,
and preserve the rule that a blocking screen never becomes Ready. This plan
includes verification design as commissioned. It does not claim captures or
implementation have been executed.

There is a material premise conflict to resolve before Code: the brief assumes
an empty `GROK_HOME` plus a fresh cwd can expose both sign-in and trust without
credentials. The repository's measured contract says sign-in gates trust
(`docs/agent-kinds.md:408`). That measurement is 1.0.13; the Linux 1.0.41 order is
unqualified. A fresh cwd alone does not prove that trust is reachable under the
brief's credential prohibition. **Next is Investigate**, using the bounded Debug
capture procedure below to measure that order. Do not silently use a signed-in
home, seed authentication, or substitute a synthetic trust screen for acceptance.
If Linux has the same gate, return the observed obstruction to the caller for a
separate authorization or acceptance decision. This plan remains a usable design;
its Code admission condition is deliberately not marked satisfied.

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
| Linux sign-in and trust are qualified. | `tests/Antiphon.Tests/Agents/GrokSignInPromptDetectorTests.cs:14` and `GrokTrustPromptDetectorTests.cs:15` contain 1.0.13 Windows examples. `Fixtures/card1004/provenance.md:3` records only Linux ready dashboards. | Add independent real Linux captures; changing a path in an old fixture is not a capture. |
| Both modals precede any prompt, so both are accessible without auth. | `docs/agent-kinds.md:408` says missing auth paints sign-in before trust. `GrokSignInCanaryTests.cs:29` in `tests/Antiphon.Agents.Pty.Tests` uses a fresh home for sign-in only. | Measure Linux ordering without entering anything. Trust acceptance stays pending if sign-in blocks it. |
| The existing capture store can supply sign-in text. | `server/Infrastructure/Agents/SessionRunner/GrokStartupCaptureStore.cs:45` suppresses all content after sign-in; otherwise it stores rendered text **and raw tail** at lines 56-65. | Preserve suppression. For this isolated measurement, extract only the rendered screen from the existing runner snapshot seam. Do not call the formatter with a false sign-in flag. |
| The server has a terminal snapshot route. | `docs/ops-http.md:233` says snapshot is runner-only. `src/Antiphon.SessionRunner.Contracts/SessionRunnerContracts.cs:252` separates `RawOutput` from `RenderedScreen`. | Use the isolated runner's `GET /sessions/{id}/snapshot`, never an invented `/api/.../snapshot`, and never serialize the whole response. |
| A fresh home isolates the CLI. | CARD-0857 records ambient MCP servers loaded outside `GROK_HOME`. `PtySessionAudit.cs:23` in `src/Antiphon.Agents.Pty` enables raw audit through the helper process environment. | Use a throwaway container of the selected runner image with no production state/home/project mounts, a clean environment and audit disabled before process startup. |
| A recognized modal cannot be Ready. | `GrokStartupReadiness.cs:29` checks sign-in, then trust, before geometry/composer. Lines 32-67 require 30 rows, at most 120 columns, exact borders, an empty `>`/U+276F interior, blank status rows and exact hint. | Preserve precedence and the whole composer predicate. Test authentic modal content over otherwise-valid composers. |
| Returning to Ready after a modal may reuse settlement. | `GrokStartupReadiness.cs:85` resets the tracker on every negative observation; lines 183-214 fail sign-in without input and answer trust at most once. | Pin reset, sign-in-before-trust, and trust-clearing behavior with controlled time. |
| An update modal has a detector. | `GrokDetectors.cs` has only trust/sign-in; `GrokStartupReason` has no update value. | D-4 reserves a real negative-fixture slot and retains explicitly synthetic update probes. No unmeasured update detector or new runtime state. |
| Windows has 99 recorded frames. | `Fixtures/card0778/startup-frames.json` has **114** captured checkpoints, counted at this baseline. `GrokStartupReadinessTests.cs:72` already checks their recorded reasons. | Keep fixture bytes unchanged and assert all 114 results. Separate replay from native Windows ConPTY evidence. |
| A runner rollout activates the classifier. | `server/Infrastructure/Agents/SessionRunner/RunnerGrokAdapter.cs:181` is the production readiness caller; its snapshot comes from the runner. | Server/AppHost activation after land is sufficient for the proposed source changes. No runner image or rollout-script change. |

Placement observation at 2026-10-03 12:24 UTC: GET `/api/runner-defaults`
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

### D-2: capture is a bounded Debug measurement using existing PTY machinery

The **caller commissions one Debug helper** on the Linux lane for C-1/C-2 below;
this Plan delegate does not sub-delegate. The helper runs version commands and
the existing runner terminal-snapshot mechanism only. No model prompt, login,
trust response, updater confirmation, browser approval, or unrelated canary.

1. Resolve the eligible Linux runner and its running image digest at execution.
   Launch a throwaway container from that same digest, with a unique task-owned
   name and no production state, credential home, project, host Docker socket or
   agent-profile mounts. Reuse the image's runner executable in isolated local
   mode, without phone-home registration. Its runner state/logs and fresh cwd live
   under the one disposable root. Do not edit Compose or deployment scripts.
   If this isolated launch cannot be established with the existing machinery,
   report it; do not fall back to a production session or authenticated home.
2. Give the isolated runner/helper a clean environment **before** starting it:
   `ANTIPHON_PTY_AUDIT=0`, fresh `HOME`, `XDG_CONFIG_HOME`, `XDG_CACHE_HOME`, and
   `GROK_HOME`; no inherited provider/proxy/auth variables or browser integration.
   In the child launch use `/usr/bin/env -i` with an explicit nonsecret PATH,
   those scratch paths, `TERM=xterm-256color` and `BROWSER=/bin/false`. This avoids
   an overlay accidentally retaining `XAI_API_KEY`, `GROK_CODE_XAI_API_KEY`,
   `GROK_AUTH_PATH`, or MCP configuration. Never enumerate or print the parent
   environment. The cwd must be outside the checkout and contain no project
   instructions/configuration. Do not read any real or scratch auth-file content.
3. Record `grok --version`, the actual image digest and OS/backend metadata under
   that same sanitized environment. No CLI install, upgrade or downgrade. Ask the
   isolated runner to start `grok --no-alt-screen --session-id <new-guid>` with
   `Cols=120`, `Rows=30`, `TranscriptEnabled=false`, and no prompt/rules/resume
   arguments. `RunnerLaunchRequest` at `SessionRunnerContracts.cs:3` is the
   existing contract. There is no server adapter waiting for Ready, so nothing
   can auto-answer trust or type a task brief.
4. Read `GET /sessions/{owned-id}/snapshot` only from that isolated runner. Select
   `RenderedScreen` in memory and discard `RawOutput`. Poll at most every 200 ms,
   for at most 15 seconds per launch, retaining only modal candidates; terminate
   early once a stable target is seen twice. Recognize candidates by visual text
   inspection after privacy filtering, not solely the detector under test. The
   observation count is metadata, not a Ready verdict. A missing target is a
   measured miss, not a reason to enter input.
5. C-1: launch in fresh home A/cwd A for sign-in. C-2: independently launch in
   fresh home B/cwd B for trust. Both launches send **zero input bytes**. C-2 is
   expressly an ordering probe: if it only paints sign-in, record “trust blocked
   by sign-in,” stop, and retain no fake substitute. Never reuse the live runner's
   home, copy or mount `auth.json`, generate/enter a token, use stub credentials,
   or invoke `grok login`. The existing real-CLI stub-proxy tests are not this
   capture procedure: they inject keys and submit turns.
6. Keep updater behavior at the installed binary's ordinary setting; record it.
   If an update/notice modal appears spontaneously before input, capture it by
   the same rules. Do not force an old version, run update, confirm, or keep
   retrying to manufacture the screen. It may prevent another target; record that
   outcome. This card does not qualify a self-updated version by accident.
7. In a `finally`, kill only the two owned capture sessions (the individual
   `/sessions/{id}/kill` route), await exit/disposal and remove the exact owned
   throwaway container. Never use `kill-all`. Delete only the created scratch
   root after checking its nonempty canonical path and ownership marker; never
   archive its home. Report session/container cleanup and zero inputs.

The existing `PtyAgentRunner.StartAsync` / `SnapshotScreen` / `KillAsync` seam
(`src/Antiphon.Agents.Pty/PtyAgentRunner.cs:81`, `:345`) is the implementation
behind the terminal capture, and the sign-in canary demonstrates its no-submit
lifecycle. Reuse those primitives if the isolated local harness is already
available; do not introduce a new production capture tool or new API. Do not run
the Windows headed canary wholesale: it logs a raw screen and uses different
eligibility/environment assumptions. The runner route above is the selected
transport. Its local address is discovered by the helper, never a fleet constant.

This design protects the credential boundary, but cannot make a gated trust
screen reachable. That empirical dependency is why the next stage is Investigate.
The specific measurements are CLI version, ordered rendered states, whether trust
appears with no auth/input, any preceding update modal, geometry and cleanup.

### D-3: real-frame custody and privacy

Debug transfers only sanitized rendered-frame JSON plus provenance from
`.antiphon/card1006-capture/`; Code commits it under
`tests/Antiphon.Tests/Agents/Fixtures/card1006/`, beside `card1004/`.
Use `linux-blocking-frames.json`, `synthetic-blocking-frames.json`, and
`provenance.md`. Existing fixture glob copying already includes this directory
(`tests/Antiphon.Tests/Antiphon.Tests.csproj:32`); no project-file change is needed.

Before console output or disk writes, scan the rendered text in memory for
account/email/user identifiers, bearer/JWT/key-like material, OAuth URLs/query
values, one-time device codes and unexpected absolute paths. Do not retain raw
response JSON or ANSI output. Prefer a blank sign-in/welcome frame with no such
material. If redaction is needed, replace only sensitive cells with equal-width
placeholders and record row/column/category, never original values. Preserve
detector anchors and geometry. A redaction touching an anchor disqualifies that
frame; acquire another safe frame or report the capture unavailable. Review the
sanitized frame and scanner result before committing. The runtime store's
`content: suppressed after sign-in` behavior remains unchanged.

Follow CARD-1004's ASCII-escaped JSON method: retain all 30 LF-separated rows,
escape non-ASCII/control characters as JSON `\uXXXX`, decode and assert exact
round-trip equality, and pin SHA-256 of UTF-8 **sanitized decoded screen** bytes.
Do not assert a maximum width of 118 for a modal without measuring it: 118 was
the ready-dashboard content width; the terminal is 120x30. Record actual width,
cursor/menu glyph positions, terminal dimensions, capture UTC, CLI/build version,
image digest, backend, source task/session identity, launch args, sanitized env
names/values, zero-input assertion, redaction metadata and exited cleanup receipt.
All paths in these records must be task-generated, not operator identities.

Label captures `real-rendered` or `real-rendered-redacted`, and compositions
`synthetic-derived`; never call a constructed or path-rewritten frame real.
Pin the screen digest independently in the test, not only next to editable data.
Missing real sign-in **or** trust is an unmet acceptance condition, never a skip.

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
Add measured Linux anchors only if existing detectors fail real captures. Preserve
all existing 1.0.13 matches and Codex nonmatches. Do not infer trust from a menu
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
| S-0 capture admission | Debug evidence only; Code receives reviewed frames and provenance, then records capture admission in this plan. | Both real modal captures and D-2 cleanup; if absent, stop before claiming Code-ready. No credential workaround. |
| S-1 wording regression | `tests/Antiphon.Tests/Agents/GrokSignInPromptDetectorTests.cs`: add the exact D-1 test against unchanged production. | Commit/push; CP-1 must fail at the message assertion, not build/setup. |
| S-2 wording and Linux evidence | `src/Antiphon.Agents.Pty/GrokDetectors.cs`; `tests/Antiphon.Tests/Agents/GrokLinuxBlockingPromptTests.cs` (new, includes its local fixture reader); `tests/Antiphon.Tests/Agents/Fixtures/card1006/{linux-blocking-frames.json,synthetic-blocking-frames.json,provenance.md}`. Add only measured detector adjustments. | V-1..V-12; commit/push; CP-2. Optional real update follows D-4, not an invented capture. |
| S-3 integration/regression closure | `src/Antiphon.Agents.Pty/GrokStartupReadiness.cs` only if capture-derived precedence/update handling requires it; the new Grok test class and same plan for final provenance/receipts. No gratuitous tracker refactor. | Commit/push; CP-3/CP-4 and separate Windows CP-5 at the final implementation SHA. |

Read-only regression files: `GrokStartupReadinessTests.cs`,
`GrokLinuxStartupReadinessTests.cs`, `GrokTrustPromptDetectorTests.cs`,
`GrokStartupCaptureStoreTests.cs`, `RunnerGrokAdapterReadyTests.cs`,
`RunnerGrokAdapterReadyTestsPty.cs`, `RunnerGrokAdapterSignInPromptTests.cs`, and
`RunnerGrokAdapterTrustPromptTests.cs`, all under `tests/Antiphon.Tests/Agents/`.
Preserve both `Fixtures/card0778/` and `Fixtures/card1004/` byte-for-byte.

No overlap with CARD-1008's `scripts/c590-remote.sh`,
`scripts/deploy-server2.ps1`, `RemoteScriptContractTests` or Docker harness;
CARD-0965's server attention/hold files; or CARD-1001's
`tools/Antiphon.Checkpoints/Coverage`. Reading/importing with the checkpoint tool
does not authorize changing it. No image, runner, server adapter, auth, lifecycle,
delivery, routing, generated `docs/cards/`, or deployment-script edits. If new
evidence needs one, return the scope change to the caller.

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
| V-2 | `GrokLinuxBlockingPromptTests.C1006_Real_sign_in_is_not_ready` | Require real sign-in capture; production sign-in detector true, trust false for an unambiguous frame; classifier Reason=SignIn, IsReady=false. |
| V-3 | `GrokLinuxBlockingPromptTests.C1006_Real_trust_is_not_ready` | Require real trust capture; trust detector true, sign-in false; classifier Reason=Trust, IsReady=false. |
| V-4 | `GrokLinuxBlockingPromptTests.C1006_Fixture_bytes_and_provenance_are_pinned` | Require the two real kinds, expected literal sanitized digests, actual geometry/version/zero-input provenance, valid label/redaction schema and exact JSON decode/re-encode/decode equality. Missing data fails, never skips. |
| V-5 | `GrokLinuxBlockingPromptTests.C1006_Real_blockers_override_valid_composers` | Preserve each capture's complete modal anchors over separately valid Windows `>` and Linux U+276F dashboards. First prove each base Ready; composed frames keep the modal reason and IsReady=false. Do not leave busy status as a second accidental blocker. |
| V-6 | `GrokLinuxBlockingPromptTests.C1006_Current_frame_overrides_raw_history` | Real modal current + stale ready raw remains its modal reason. Real ready current + stale sign-in/trust raw remains Ready. Raw history cannot override current rendered evidence. |
| V-7 | `GrokLinuxBlockingPromptTests.C1006_All_19_fail_open_shapes_stay_closed` | Exact P-01..P-19 inventory below, then each shape composed with each real modal's complete anchors. Assert not Ready for all; assert SignIn/Trust for modal-derived cases. Name every variant on failure. |
| V-8 | `GrokLinuxBlockingPromptTests.C1006_Blocker_resets_readiness_settlement` | For each real blocker: Ready at 0 ms=false, blocker at 900=false/count 0, Ready at 950=false/count 1, Ready at 1000=false, Ready at 1950=true. One-second settle cannot reuse pre-blocker time. |
| V-9 | `GrokLinuxBlockingPromptTests.C1006_Sign_in_precedes_trust_and_types_nothing` | Script real sign-in, and combined real sign-in/trust anchors over a valid composer, into production WaitAsync. Both fail with SignIn, fire OnSignIn, and record exactly zero input calls; combined classifier reason is checked first. |
| V-10 | `GrokLinuxBlockingPromptTests.C1006_Trust_remains_blocked_until_cleared` | Real trust repeated to controlled deadline returns false/Trust with exactly one `y`; real trust then two settled ready snapshots returns true with one `y`. Never call a real CLI. Pin reason/inputs before the success path. |
| V-11 | `GrokLinuxBlockingPromptTests.C1006_Update_fixture_policy_and_negatives` | P-06/P-07 must be labelled synthetic and not Ready. A captured realUpdate slot requires at least one real-labelled frame and every referenced frame not Ready. not-observed means no real-update claim, not a synthetic substitute. |
| V-12 | `GrokLinuxBlockingPromptTests.C1006_Windows_fixture_results_are_unchanged` | Iterate exactly all 114 captured Windows checkpoints; compare Reason and IsReady with recorded results. No fixture rewriting. |

V-4 is artifact integrity, not independently sufficient behavior coverage. V-2,
V-3 and V-5 call the production detector/classifier, and V-8..V-10 call the actual
tracker/waiter. No test compares only a test helper to its own constants.

### Durable 19-shape inventory

These shapes reproduce the named Review inventory, with explicit construction
rules. Store synthetic base screens/definitions separately from real captures.
Start composer variations from CARD-1004's 1.0.41 dashboard (30 rows; composer
top/input/bottom at 24/25/26; enabled hint at 28). Preserve every unchanged row.
Modal base shapes use the relevant captured anchors once S-0 is satisfied.

| Probe | Exact shape / transformation | Base expectation |
|---|---|---|
| P-01 | Real trust choices with U+276F before the affirmative choice. | Trust / false |
| P-02 | Real sign-in menu with a U+276F selection cursor. | SignIn / false |
| P-03 | Trust anchors over dashboard with row 22 `  Working...`. | Trust / false |
| P-04 | Sign-in anchors over the same working dashboard. | SignIn / false |
| P-05 | Sign-in anchors plus a complete composer-shaped box whose interior is bare U+276F. | SignIn / false |
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
shapes: **57 internal cases, one TUnit result**. For a derived case preserve the
real modal's complete anchors outside the changed composer/status rows; assert
that construction did not erase them. Transfer each probe's layout/glyph change,
not a different modal's text: the sign-in-derived and trust-derived sets retain
their respective reasons. The deliberate mixed-anchor case is V-9 and must
classify SignIn. If a capture cannot be composed without
erasing an anchor, freeze a different explicit placement before Code; do not
drop the variant. V-5 is the stronger ready-composer precedence control, so a
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

### PCs for the later SourceLanding Mutation stage

These are post-land obligations, not tests run by this Plan or ordinary Code.
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
| PC-4 | Make trust IsVisibleOnScreen return false. | `GrokLinuxBlockingPromptTests.C1006_Real_trust_is_not_ready` | Detector true on real trust. |
| PC-5a / PC-5b | Move respectively sign-in or trust classification after successful composer return. | `GrokLinuxBlockingPromptTests.C1006_Real_blockers_override_valid_composers` | Expected SignIn/Trust on otherwise-ready composer becomes Ready. |
| PC-6 | Check trust before sign-in in Classify. | `GrokLinuxBlockingPromptTests.C1006_Sign_in_precedes_trust_and_types_nothing` | Combined frame Reason=SignIn fails. |
| PC-7 | Replace exact composer interior match with Contains(U+276F). | `GrokLinuxBlockingPromptTests.C1006_All_19_fail_open_shapes_stay_closed` | P-08 not-ready fails (also P-13..P-15). |
| PC-8 | Remove enabled-hint rejection. | `GrokLinuxBlockingPromptTests.C1006_All_19_fail_open_shapes_stay_closed` | P-16 not-ready fails. |
| PC-9 | Before bottom validation, replace an empty bottom with `"  ╰" + new string('─', 114) + "╯"`, thereby accepting a missing border. | `GrokLinuxBlockingPromptTests.C1006_All_19_fail_open_shapes_stay_closed` | P-18 not-ready fails at an outcome assertion, without an indexing exception. |
| PC-10 | Permit 29 rows by removing only the row-count rejection. | `GrokLinuxBlockingPromptTests.C1006_All_19_fail_open_shapes_stay_closed` | P-19 not-ready fails. |
| PC-11 | Retain the old tracker region/time on a negative observation. | `GrokLinuxBlockingPromptTests.C1006_Blocker_resets_readiness_settlement` | PositiveObservations=0 after the real blocker fails; without that assertion, Ready at 1000 must fail. |
| PC-12 | Write affirmative key in WaitAsync's sign-in branch before failing. | `GrokLinuxBlockingPromptTests.C1006_Sign_in_precedes_trust_and_types_nothing` | Zero inputs fails on real sign-in. |
| PC-13 | Treat an uncleared trust frame as Ready after the affirmative write. | `GrokLinuxBlockingPromptTests.C1006_Trust_remains_blocked_until_cleared` | Persistent trust must return false/Trust. |
| PC-14 | Allow first ready observation immediately. | `GrokLinuxBlockingPromptTests.C1006_Blocker_resets_readiness_settlement` | First observation at 0 ms must be false. |

PC-5 has two independent cycles; 15 cycles total. If D-4 needs a real updater
detector, TestDesign adds a method-bound knockout/precedence PC with its real
captured input before Code; the current roster does not pretend to cover an
unobserved vendor modal. Keep the unchanged privacy regression in ordinary R-2.

### Cost

Ordinary Code checkpoint floor: **35 minutes** (6+6+7+8+8), plus authoring and the
separately commissioned bounded capture investigation. Windows dispatch capacity
is an external lane dependency. Plan verification is one docs checkpoint plus
actual import; no classifier, native, provider or PC runs in this dispatch.

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
retain `.antiphon` receipts. Record actual outcomes in a subsequent evidence
amendment, without claiming the amendment itself was the tested source. If only
evidence prose changes, re-import at the final SHA; no duplicate docs run is needed.

### Plan verification receipt (2026-10-03)

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

The final amendment records these results and clarifies that the 57 derived
probe cases transfer layout without importing a foreign modal's anchors; V-9
owns deliberate mixed-modal precedence. It does not change the checkpoint table,
production code or tests. The docs test receipt remains attributed to the SHA
above, not retrospectively to this amendment. Re-import the unchanged table at
the final committed SHA before cleanup. This verifies plan syntax and the
existing documentation contracts; all future captures, V/R and PCs remain pending.

## Risks, landing order and follow-ups

1. Capture feasibility is an admission dependency, not an auth exception.
   Investigate C-1/C-2 first. A blocked trust screen cannot be bypassed within this
   brief. The caller may commission a revised, separately authorized method;
   this plan grants none. Fresh-home isolation must also account for CARD-0857.
2. Real UI wording, menu keys or geometry may differ from 1.0.13. Return novel
   anchors/actions to TestDesign, amend/import this plan, then implement. Never
   broaden Ready to accommodate a modal. Redaction must not invent the anchors.
3. Updater qualification is conditional and explicitly pending when not observed;
   the two synthetic shapes are limited evidence. Capture any encountered real
   frame before deciding whether production needs a detector.
4. Publish this plan, perform capture investigation, resolve admission, then Code,
   separate Final Review, durable post-land companion, confirmed Code publication
   and server activation. The orchestrator activates from the canonical checkout
   through its AppHost restart policy and verifies `/api/version` equals the
   activated source SHA. No runner rollout, auth-file operation or provider-spend
   canary is authorized by this plan. Existing successful CARD-1004 Linux canaries
   remain historical evidence, not a new execution of this card.
5. Execute every post-land PC against the confirmed SourceLanding snapshot and
   retain external restoration evidence. An implementation may be Done with the
   explicitly linked pending obligation; never claim PC-clean before it runs.

FOLLOW-UPS (board searched before proposing any new card): `card.ps1 search Grok
-Board Antiphon -All` completed, and the narrower `Grok updater` search returned
CARD-1006 itself. The capture-order finding and conditional update slot belong to
this card, so no duplicate is created. CARD-0857 owns ambient MCP isolation;
CARD-0988 owns the loaded trust timing failure; CARD-0861 owns other terminal
sizes; CARD-1011 owns real Windows canaries/routing. Those cards do not relax the
capture prerequisites or substitute for CP-5. No new structural defect outside
these existing obligations was established by this Plan dispatch.

--- next stage ---
next: investigate
handoff: Commission a Linux Debug capture using D-2's isolated runner snapshot route; measure whether credential-free fresh-home launches can expose real sign-in and trust, retain only sanitized frames, and resolve the documented sign-in-before-trust conflict before admitting Code. No login, tokens, auth-store access or model turn.
artifact: docs/superpowers/plans/2026-10-03-card-1006-grok-linux-signin-trust-plan.md
