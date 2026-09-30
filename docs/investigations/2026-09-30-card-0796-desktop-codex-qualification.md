# CARD-0796 desktop Codex qualification: live startup and transcript receipt pass

Date: 2026-09-30. Latest verdict: **live diagnostic passed on the patched working tree**. The fourth bounded diagnostic reached positive readiness in 2,414 ms, bound its correct native rollout, and recorded one complete server-normalized `UserPrompt`, the exact expected answer and a successful `TurnEnd`. The source changes and this evidence are uncommitted; formal qualification publication and the CARD-0796 S3 task-admission change remain outstanding.

## Run identity and admission

- Operator sanction: the user approved the CARD-0796 desktop diagnostic in this conversation and explicitly approved using a copy of the normal Codex `auth.json` for the isolated home. No secret content was printed or committed.
- Source and loaded server SHA: `38abc430868272c9381d8686ba0b8f5d35c5c195`; `/api/version` advertised `land-v2`. Desktop runner was dispatch-eligible. The main checkout had no tracked edits.
- Standard Codex profile `cec57c0b-8d44-4205-b72d-6f57bb066b39`, revision `afb938fc-feaf-4f25-b819-b3666a86a865`, `Kind=Codex`, executable `codex.cmd`, `ModelArgumentName=--model`, with the existing `--no-alt-screen` and `--dangerously-bypass-approvals-and-sandbox` arguments. Resolved CLI reported version `0.158.0`.
- One cardless, Paused, non-AlwaysOn local PtyHost agent `2bf9480e-6ae3-477d-a54c-37b303322d6b`, session `0c86151a-8b4a-412e-8a9d-0d97408145e0`, model `gpt-5.6-luna` at Low tier. Remote control, automatic compaction, quota override, and model-disabled override were false. The scratch workspace was separate from the isolated home and checkout.
- Run ID `ab3bb8f784db4a9d98fdfb7b00eb3c78`. The isolated home was a new, ACL-restricted directory under `%TEMP%`; its initial `version.json` was absent. The copied auth file passed `codex login status` as “Logged in using ChatGPT”. The seeded `version.json` recorded `latest_version=999.0.0`, newer than installed `0.158.0`, and a current `last_checked_at`.

## Observation

Start was accepted at `20:38:37.245810Z`; the runner began the session at `20:38:38.008724Z`. The raw startup capture first showed Codex 0.158.0's `Trust this folder?` screen with `1. Trust and continue` selected. The existing trust handler advanced past it. The final rendered screen was the distinct first-run prompt, `Set up the Codex agent sandbox to protect your files and control network access`, offering administrator setup, a non-admin fallback, or Quit. It blocked the composer even though the background TUI showed the requested model and scratch cwd.

At `20:39:38.133Z`, the unchanged readiness gate logged `codex-startup not-ready reason=Unknown elapsedMs=60008 mcpSeen=False`. There was no positive ready receipt, and the server-normalized transcript contained zero entries. No diagnostic prompt was enqueued. The configured `check_for_update_on_startup=false` launch path did not show an update picker; this run cannot qualify the pending-update case because the sandbox onboarding screen intervened. The current classifier does not identify this exact screen as `Sandbox`.

The exact agent was stopped after evidence capture. The server reports `Stopped` with no live session; the runner reports this session `Exited`, `KilledByRequest`. Only then were the seeded `version.json` and copied `auth.json` removed from the isolated home. The user's normal `auth.json` remained present and was not modified. No second session or turn was attempted.

## Evidence inventory

The following diagnostic-only files remain under `%TEMP%\antiphon-c796-ab3bb8f784db4a9d98fdfb7b00eb3c78` with ACLs limited to the desktop user, the tool account, and SYSTEM. The copied credential and seed file in the active home were removed; `version.seed.json` is the non-secret seed copy.

| File | Bytes | SHA-256 |
|---|---:|---|
| `version.seed.json` | 119 | `5E12C2A0078F5C2CE644084F4FAE4518D0FCA5A033599C6DA60E09EB23CE98DF` |
| `rendered-screen.txt` | 592 | `5E0E08B58CD7731AF7BF1F50E2AC70014C2ABF06AD138F06A2BD0601E92A8C8A` |
| `startup-capture.txt` | 8462 | `5CF50532BFED15E95F833E8919315E01EEB94B9829190F0584850792914219ED` |
| `transcript.json` | 101 | `F31C75D44E7F9A6E43412DC94E7FB8DE95AAAB45FED0B00FC54520065EB62278` |

## Second diagnostic: first-run choice resolved, footer-only ready screen rejected

After the user explicitly authorized repeated bounded launches, the isolated home retained its recorded workspace trust and was given `[windows] sandbox = "elevated"`, matching the sanitized setting in the normal home. A fresh copy of the normal auth file was used only for this run and removed after its exact session exited. The pending-update sentinel was seeded again and removed afterward. The main home was unchanged.

- Run `a130966552cf47f984f3df3afe08e4a2`, agent `d55571d2-8748-4c17-84f2-37a68652611d`, session `d8361b13-98f4-47b7-8d96-836ce3cd0355`; source and loaded SHA remained `38abc430868272c9381d8686ba0b8f5d35c5c195` and the same profile revision and Luna Low model were used.
- Start was accepted at `20:54:18.009460Z`; runner session started at `20:54:18.453579Z`. No sandbox chooser or update picker appeared. The final 0.158.0 screen had an empty `› Ask Codex to do anything` composer and a `GPT-5.6-Luna low · <scratch cwd>` footer (plus one PATH-alias warning). It had **no separate `model:` row**. This is a visible idle composer, but `CodexStartupScreen.TryReadSelectedModel` requires that row.
- At `20:55:18.503Z`, the readiness gate logged `codex-startup not-ready reason=Unknown elapsedMs=60013 mcpSeen=False` and saved a startup capture. No prompt was sent; the normalized transcript again had zero entries. The exact agent was stopped and the runner reports `Exited`, `KilledByRequest`.
- CARD-0859 records the parser defect. A proposed guarded footer-only model path must still require the banner, empty composer, model/effort/cwd footer, and absence of any blocking modal; a blank or loading `model:` row must stay non-ready. CARD-0858 records the separately observed sandbox chooser and its `Unknown` classification.

Second-run evidence under `%TEMP%\antiphon-c796-ab3bb8f784db4a9d98fdfb7b00eb3c78\attempt-a130966552cf47f984f3df3afe08e4a2`:

| File | Bytes | SHA-256 |
|---|---:|---|
| `version.seed.json` | 119 | `CEA4AE80B583EF646C73CB3C040AE6E750B51A8A7334040F301EC9762AFF5164` |
| `rendered-screen.txt` | 750 | `A75A28273F72AF82C69FFCBD879B628FAAE4D90324988680C0924EC4BFC6D08B` |
| `startup-capture.txt` | 5659 | `8EF46524128456CAD2933A8A26E95CEC936D6ADCFD0F44F792BFEE3207019C92` |
| `transcript.json` | 101 | `0B88D63F7CD62CE02EB28FA5766C87765BC5B83CD5E60E49D94E214903EE678D` |

The proposed [CARD-0859 source-and-test patch](2026-09-30-card-0859-codex-startup.patch) adds the exact sandbox-chooser classification and a guarded footer-only model path. `git apply --check` passed against this checkout. A cloned `Antiphon.Agents.Pty` project compiled with zero warnings/errors, and its production classifier passed six measured/negative startup cases, including the 0.158.0 footer-only screen, sandbox chooser, old model-row screen, empty/loading model rows, and unsupported composer text. The repository source path and `.git` are read-only to the tool account, so the patch is not applied or committed in the canonical checkout. This validation does not replace the full Pty TUnit filter or a fresh live prompt receipt on the patched server.

## Third diagnostic: ready and answered, but C4 rejects the native rollout

The user applied the CARD-0859 patch and ran the focused Pty test command and canonical AppHost restart from an independent PowerShell. The checkout has the intended tracked source/test edits. The server assembly write time is `21:14:20Z` and `/api/version` still reports source HEAD `38abc430868272c9381d8686ba0b8f5d35c5c195` because the patch is uncommitted. The AppHost log confirms a server build and adopted the already-running session runner. That runner's `/capabilities` still reports its older process start `16:38:43Z` and commit SHA `7063845a9d8e85d7a7cbc7ffcbc2f528ea850c4b`; no runner source changed in CARD-0859, and readiness runs in the new desktop server assembly. A routine runner restart attempted from the tool shell failed before any process stop at `write-state: Access ... logs/session-runner.state is denied`; the runner stayed healthy. The focused TUnit output was not available to this shell, so its count is not claimed here.

The third fresh cardless session used the same standard profile revision, Codex 0.158.0, Luna Low, ModernConPty, separate workspace and isolated home with `[windows] sandbox = "elevated"`. The user had authorized repeated bounded launches and use of a temporary copy of the normal auth file. Run `71d98a359fe846f1b39ca5c1d05a0794`, agent `b7b30e3f-b093-44cd-87f6-75fcb94b00c9`, server/runner session `9f19cc69-74d3-4262-9dc5-24b7d93c55c3`, native Codex thread `01a0f42f-2344-7583-ad56-96be6d6f73e5`. Start was accepted at `21:18:43.021Z`, runner started at `21:18:43.831Z`; the server reported live session `Running` and profile revision `afb938fc-feaf-4f25-b819-b3666a86a865`. The captured positive screen has an idle `› Ask Codex to do anything` composer and `GPT-5.6-Luna low` footer with no modal. The adapter's positive-settle diagnostic is logged only at Debug and was not present in the ordinary server log; this run therefore records Running plus the screen, not an invented exact gate elapsed time.

One `WhenIdle` message was posted at `21:19:58.765Z`. The API request was canceled after about 30 seconds while waiting for transcript confirmation, but inspection found exactly one queue row, `30246bb1-894f-461c-bac4-d5d104590486`, with one delivery attempt. It was never reposted. The queue body has 129 characters and an LF after `[card-0796-diagnostic:71d98a359fe846f1b39ca5c1d05a0794]`. Codex's native `response_item` user message has 128 characters, the same characters except that the LF is gone with no separator. The native rollout also contains the `C796_OK_...` answer and `task_complete`, and the rendered screen shows both. The runner refused its sole cwd-matched rollout under C4 after 60 seconds: `no prompt in it matches input delivered to this session`. Runner and server normalized transcripts had zero entries; the queue remained `Pending`, so an answer visible on screen does **not** qualify delivery. CARD-0860 records the structural defect: `SessionInputLog.MatchesRecordedInput` lacks the whitespace-free arm already used by `PromptSubmissionMatch.IsConfirmedBy` and `IsCompleteIn`. A [proposed runner patch](2026-09-30-card-0860-codex-c4.patch) adds that guarded arm and a regression test; `git apply --check` passed, but it is not applied.

The exact diagnostic agent was stopped after capture. The server says `Stopped`, the runner says `Exited/KilledByRequest`, and only then were the isolated home's copied `auth.json` and seeded `version.json` removed. The normal home was not modified. Raw evidence is under `%TEMP%\antiphon-c796-ab3bb8f784db4a9d98fdfb7b00eb3c78\attempt-71d98a359fe846f1b39ca5c1d05a0794`; the native rollout remains in that isolated home's `sessions/2026/09/30` directory and has SHA-256 `2CB2ECAD1AD1294E484ACFB4ED2A5D15F176B56AC9FB85C02257610F9DC414A0`. This record is session-scoped and contains no copied credential. The CARD-0860 patch has SHA-256 `DE49C49D6551868A4EC8E3BD5F0E439DE38A5C5C0C64321E4F7CF6376C2D1855`; it also promotes the positive readiness diagnostic to Information so ordinary server logs can record `positive-settle` without a frame or prompt. A scratch build of the actual production matcher sources had zero warnings/errors: the baseline reproduced `joinedMatch=False`, and the patched logic gave `joinedMatch=True`; ordinary multiline matching, stranger rejection, short-prompt rejection and full-body completion controls passed. The full runner TUnit filter and a live post-restart receipt have not run.

| Evidence file | Bytes | SHA-256 |
|---|---:|---|
| `version.seed.json` | 119 | `CB06EE52EB1A611394F500748ACF760906AA33F2ACA116D3BC56667C3D4CDC9C` |
| `ready-screen.txt` | 747 | `11B0CC67B717DBBFE487A1226B9CBC9E1091076F28859DAF1C63B7377D5CF166` |
| `prompt.txt` | 129 | `3AE26851331F67EF80FC0A4962EE8766874663D1DA36EE75CF2C561BD4BBE38D` |
| `native-user-prompt.txt` | 128 | `66D1BAA884BF4F72585B572780C93492C7B6A488EC996A3907F5BD5B143F1D28` |
| `after-screen.txt` | 931 | `60AFE7E7BE38647863B12298888E7F2614FBAA5F0018DF305C21069DF4757B65` |
| `queue.after.json` | 637 | `77DC35EBC46A467984128ED10E70532FEBBDC2B5AB35227FCE96D7A8F5C8BEF4` |
| `transcript.after.json` | 101 | `DD9FB7FA94301E4B5D1EA2FEFBE377C98745BB40620AA8B6F699F26EA09588CF` |
| `runner-bind-refusal.txt` | 686 | `69F7DF908A49445BBECCA437BB373EAADC82A5BC35732B696BAB3AE168590E62` |

## Fourth diagnostic: complete live receipt

After the user applied CARD-0860 and restarted both services, the runner reported source HEAD `38abc430868272c9381d8686ba0b8f5d35c5c195`, assembly write time `22:16:58.136Z` and process start `22:16:58.612Z`. The server binary was rebuilt at `22:17:59.243Z`. Both patches are present in the canonical working tree; it remains dirty, so these are local patched-build receipts rather than a clean published SHA.

Run `1668cf47ccf740668fcda7b88de95a39` used agent `7c75e733-fe14-40e3-bc60-9c6b3da0f534`, Antiphon session `159bce66-bbea-4136-bee0-4f10ef1c65b5`, and native thread `01a0f466-94d1-7850-b3c8-3d94a45cca87`. The same approved profile revision, Luna Low model, scratch workspace, isolated home, copied auth and pending-update sentinel were used. ModernConPty reported no fallback. Start was accepted at `22:19:16.984Z`; the runner began at `22:19:17.582Z`. At `22:19:20.040Z`, the ordinary server log recorded:

```text
Session 159bce66-bbea-4136-bee0-4f10ef1c65b5 codex-startup ready reason=positive-settle elapsedMs=2414 mcpSeen=False
```

The ready snapshot showed the expected idle composer, requested model/effort and scratch cwd. The baseline transcript was empty. Exactly one LF-separated diagnostic body was posted through the WhenIdle queue. The post returned successfully and the pending queue drained. At `22:19:57Z`, the runner logged adoption by C1-C4 discovery of `rollout-2026-09-30T23-19-18-01a0f466-94d1-7850-b3c8-3d94a45cca87.jsonl` under the isolated home, with the correct cwd. The server transcript contains exactly:

1. Sequence 1, `UserPrompt`, `22:19:56.941Z`: the full diagnostic body with the single LF removed by Codex. Every other character equals the submitted 129-character body; the existing full-body whitespace-normalized receipt rule is satisfied, not merely the nonce or a head fragment.
2. Sequence 2, `AssistantText`, `22:19:58.417Z`: `C796_OK_1668cf47ccf740668fcda7b88de95a39` exactly.
3. Sequence 3, `TurnEnd`, `22:19:58.460Z`: `stopReason=end_turn`, correlated with the assistant reply through `apiCallId=01a0f467-28e7-72b1-8293-53b2aa144a98`.

Focused TUnit runs used the operator-built binaries with `--no-build --no-restore`, explicit class filters and build-slot leases. `CodexTranscriptTailerTests` plus `PromptSubmissionMatchTests`: **48 passed, 0 failed, 0 skipped**. `CodexStartupReadinessTests`: **31 passed, 0 failed, 0 skipped**. Their TRX files are `test-results/c860.trx` and `test-results-startup/c859.trx` under this run's evidence directory.

After capture, the exact agent was stopped and verified `Stopped` at the server and `Exited/KilledByRequest` at the runner. The copied auth and seeded version file were then removed; both are absent. No second turn was sent. The native rollout SHA-256 is `8A787F1627DFE98F3B94A55CE95E04998EF97A913E4882DC919E68E584955872`.

Evidence directory: `%TEMP%\antiphon-c796-ab3bb8f784db4a9d98fdfb7b00eb3c78\attempt-1668cf47ccf740668fcda7b88de95a39`. `receipt.json` is the machine-readable verdict; `inventory.json` records exact names, sizes and SHA-256 hashes for the diagnostic captures. Selected receipts:

| File | SHA-256 |
|---|---|
| `ready-receipt.log` | `BD8993203C592371C49630BBC9FD293E3E69841534C1C03FF36B64BB5C7CA2C1` |
| `ready-screen.txt` | `55DF4B1BB927E22BF6757A342FC4FC162D4DC9C0CBC7B040ABC15564F8CEE2DD` |
| `bind-receipt.log` | `98681314E34CF31E7BCAB6F9CE4822DCBA8C065F6BEAC8B2083A239B2E24791D` |
| `transcript.after.json` | `0A87641193D82972F002F86AE9EC5826C4142134A24941DC5B936F57944C2753` |
| `receipt.json` | `8105493C6D7C788B8C152F8D437C3E868A1ABE81497D6ECB0DF6EAFCC8B3E271` |
| `cleanup.json` | `B3A0AF0E54989BA41D85B78264787C963F91EE50E3762A962F6231A73DC6C18C` |

## Next action

The diagnostic and focused tests now pass. Commit and review the applied CARD-0859/CARD-0860 source/test changes together with this evidence; then follow CARD-0796 S3 to remove the desktop task refusal and verify its create/dispatch/reroute coverage. No admission guard was removed during qualification. CARD-0662 covers the trust prompt; CARD-0858 and CARD-0859 cover the two startup classifications. The [official Windows sandbox documentation](https://learn.chatgpt.com/docs/windows/windows-sandbox) supports the isolated home's explicit `sandbox = "elevated"` setting.
