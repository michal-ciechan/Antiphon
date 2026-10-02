# CARD-0984 Code report

Implemented the named Windows skip for all 17 UnixPtyArgvTests cases; Linux executions and pass counts are unchanged. FakeGrok LF diagnosis is complete to the limit of Linux-readable evidence; no FakeGrok code or assertion was changed. Windows confirmation remains for the separate Debug task.

## Change and scope

- `tests/Antiphon.Agents.Pty.Tests/UnixPtyArgvTests.cs:6,153-155`: import `TUnit.Core.Exceptions`, then throw `SkipTestException` under the existing `OperatingSystem.IsWindows()` condition. Named reason: `Unix PTY argv contract requires a Unix PTY and node`.
- Reuses the existing mechanism/style in `PtyBackendContractTests.cs:27-31`, `FakeGrokContractTests.cs:53-57` and the opposite-direction `WindowsPtyArgvNativeTests.cs:19-21`. No new attribute/helper mechanism.
- No assertion, Linux selection, production checkpoint-selection code, neighbouring CARD-0863 V-check, shared helper, or CARD-0596 canary changed.
- Implementation commit: `844eec1f304eab388821dc4b6bae81dfcf2d038f`, pushed fast-forward on `feat/card-task-4deb485d`, based on `196c88ea2eb60264e91bcb46b22c647b26ad62d8`. This report is a separate documentation slice on that branch.
- No whole-project Linux run: the brief requires it only when shared helpers change; this guard is private to one test class. No Unit lane or Antiphon.Tests run.

## Linux verification

| Row | Executed | Passed | Failed | Skipped | Meaning |
|---|---:|---:|---:|---:|---|
| CP-1 corrected baseline at start SHA | 17 | 17 | 0 | 18 | Unix argv 17 pass/0 skip; FakeGrok 0 execute/18 existing named Windows-only skips |
| CP-2 clean implementation SHA | 17 | 17 | 0 | 18 | Exactly the same selection and outcomes; zero new Linux skips |
| CP-3 scratch guard inversion | 0 | 0 | 0 | 17 | All Unix argv cases selected and skipped with the new reason |

The CP-3 scratch edit inverted only the condition to `!OperatingSystem.IsWindows()`, used separate `bin-c984-mutation/` output, and was not committed or pushed. It is intentionally dirty diagnostic evidence, not a clean acceptance certificate. This is the explicit scratch exception to commit-before-run. Restored the exact committed file through a fresh edit; `git diff --exit-code` and `git status --short` returned empty. A final clean same-selection CP-4 rebuild after committing this report binds the final branch SHA and verifies restored source; its result is supplied in the caller-facing summary.

All rows used `scripts/run-checkpoint.ps1` with a granted build slot and the Linux default `UseAppHost=false`. Builds passed, with the inherited nullable warning at `GrokSubmitWhileWorkingCanaryTests.cs:77`. Nothing ran over two minutes.

Commands:

```sh
# Corrected baseline, reusing the baseline build from the first invocation:
pwsh -NoProfile -File scripts/run-checkpoint.ps1 -Name CP-1 -Project tests/Antiphon.Agents.Pty.Tests -OutputPath bin-c984-base/ -NoBuild -Filter '/*/*/(UnixPtyArgvTests*)|(FakeGrokContractTests*)/*' -MinExecuted 17 -Expect UnixPtyArgvTests -ExpectedSourceSha 196c88ea2eb60264e91bcb46b22c647b26ad62d8 -ResultsRoot .antiphon/c984-baseline-corrected
pwsh -NoProfile -File scripts/run-checkpoint.ps1 -Name CP-2 -Project tests/Antiphon.Agents.Pty.Tests -OutputPath bin-c984-green/ -Filter '/*/*/(UnixPtyArgvTests*)|(FakeGrokContractTests*)/*' -MinExecuted 17 -Expect UnixPtyArgvTests -ExpectedSourceSha 844eec1f304eab388821dc4b6bae81dfcf2d038f -ResultsRoot .antiphon/c984-green
# Scratch only, with inverted guard:
pwsh -NoProfile -File scripts/run-checkpoint.ps1 -Name CP-3 -Project tests/Antiphon.Agents.Pty.Tests -OutputPath bin-c984-mutation/ -Filter '/*/*/UnixPtyArgvTests*/*' -MinExecuted 0 -ResultsRoot .antiphon/c984-mutation
# Final clean SHA, after report publication:
pwsh -NoProfile -File scripts/run-checkpoint.ps1 -Name CP-4 -Project tests/Antiphon.Agents.Pty.Tests -OutputPath bin-c984-final/ -Filter '/*/*/(UnixPtyArgvTests*)|(FakeGrokContractTests*)/*' -MinExecuted 17 -Expect UnixPtyArgvTests -ExpectedSourceSha "$(git rev-parse HEAD)" -ResultsRoot .antiphon/c984-final
```

Two invocation corrections are disclosed: the first CP-1 used a bare pipe between full paths, executed only the Unix class (17 pass/0 skip), and could not establish FakeGrok coverage. Corrected using the owner-documented parenthesized class operands with trailing wildcards, reusing its qualified build. The first CP-2 invocation had an incorrectly transcribed expected SHA and was refused before taking a slot/building/running (`source_mismatch`, exit 2); reran with the actual full SHA. No failed test result was hidden.

## FakeGrok LF diagnosis

Facts from source, not a Windows reproduction:

- `FakeGrokContractTests.cs:219-246` is already a Windows-only test. It writes a 12-line body containing 11 bare LFs, delays 25 ms, then writes a separate CR. Its oracle searches raw terminal output after deleting CR/LF, requiring HEAD and TAIL inside one `SUBMITTED:` region before `FAKE response`; it does not read native user records.
- `FakeGrokContractTests.cs:60-71,75` forces `new PtyAgentRunner("inbox")` and asserts `InboxConhost`. This reproduction does not select modern redistributable ConPTY. Its debug environment prints console mode/codepage, not per-read bytes.
- `PtyAgentRunner.cs:302-312,585-591` serializes each write, UTF-8 encodes it once and writes/flushes the connection stream. There is no LF normalization or CR insertion here. `PtySession.cs:34-48` forwards Porta's stream; `PtyAgentRunner.cs:203-207` chooses Porta for inbox. The test calls raw `WriteAsync`, bypassing production `SendLineAsync`/bracketed encoding.
- `Program.cs:386-404` uses `Console.OpenStandardInput().Read` on a background thread. `433-465` groups timestamped reads by the default 12 ms burst gap. `511-532` treats an all-CR-or-LF burst as Enter, irrespective of the Linux option. `559-580` converts CRLF to CR, splits/submits on CR, and drops bare LF inside text pieces. These paths can behave differently when the Windows console reader changes the byte shape or splits reads.
- `Program.cs:1512-1535` sets UTF-8 console codepages, clears line/echo/processed input and enables VT input. Native SetConsoleMode results are not checked; exceptions are swallowed. Existing `INMODE` output at `319-324` records the actual mode and codepage.
- `Program.cs:494-498` already offers `ANTIPHON_FAKE_INPUT_SHAPE_REPORT=<file>` with per-processed-burst byte/CR/LF counts. It does not capture individual native reads or full payload hex.

Ranked hypotheses, awaiting Windows probes:

1. **Windows inbox ConPTY / console read conversion:** a sent LF arrives as CR or CRLF; the fake then faithfully splits the received CR into partial turns. Runner-level source rules out conversion in Antiphon's UTF-8 writer, but does not establish what ConPTY/.NET receives.
2. **Fake reader/burst semantics:** even preserved LF can become a standalone burst and enter the unconditional `isLoneEnter` branch. A failed raw console-mode setup or Windows-specific read fragmentation may contribute. This is a concrete fake parser candidate; it is not proof that it happened in the recorded failure.
3. **Test oracle/transport assumption:** raw ConPTY output can include wrapping/cursor/erase sequences that the CR/LF-only cleanup leaves in tokens; the native prompt may be whole despite a failing regex. `PtyAgentRunner.cs:334-348` distinguishes raw output from the rendered screen. The assumption of bare LF staying inside text bursts is also platform-sensitive, but there is no Linux result for this Windows-only class. Thus no evidence proves a Unix-only assumption and no additional Windows skip is justified.
4. **Real product LF-unbracketed Grok behavior:** possible only if the installed real Grok exhibits the same fragmentation with this raw path. Fake comments describe measured 1.0.5, not current CLI behavior. This test alone cannot establish a product defect, and production bracketed delivery is a different path.

### History check

Ran `git log -p --follow -- src/Antiphon.FakeGrok/Program.cs` and inspected the relevant patches. CARD-1004's marker change is `b57e9436cc798f3283d41d067288ec0bc41d64da`: `ANTIPHON_FAKE_GROK_LINUX_COMPOSER=1` changes only the displayed `>` to U+276F at `339-343`; default is still `>`. The Linux Enter option was actually introduced earlier by CARD-0418, commit `3ea17c7f28587959c19989b18ca266c7efbc7664`: `ANTIPHON_FAKE_LF_ENTER=1` enables LF-to-CR conversion at `269,560-563`. Neither changes the default Windows LF parser. The option is an environment flag, not an OS check: if inherited as `1` on Windows it would alter the path. The contract helper does not set or clear either flag, so the Windows Debug task must capture/sanitize those non-secret settings. `b5f1dd7e9ee762568de9adc4d487ec89b0eb0e7d` added the startup composer/replay harness; replay is opt-in and does not revise LF parsing.

### Three concrete Windows Debug probes

1. **Locate transformation/burst boundary.** Run the exact LF method with `ANTIPHON_FAKE_LF_ENTER`, `ANTIPHON_FAKE_GROK_LINUX_COMPOSER` and replay/clip/placeholder/swallow knobs unset. In a scratch diagnostic fixture pass `ANTIPHON_FAKE_INPUT_SHAPE_REPORT` to a unique owned file, capture `runner.OnData` from before start so `INMODE` survives the helper's `ClearLiveBuffer`, and temporarily log timestamp/hex per `stdin.Read` before grouping plus each `isLoneEnter`/CR-split decision and submitted body. Expected sent shape: 11 LF in the body, no CR until the separate Enter. Received CR before Enter supports rank 1; an LF-only submitting burst supports rank 2; an inherited LF_ENTER=1 identifies harness contamination. Repeat through inbox and modern using a diagnostic fixture, recording the actual backend (the current helper must stay inbox for ordinary evidence).
2. **Separate submit content from screen matching.** Launch FakeGrok with unique GROK_HOME/cwd/session-id, send the identical body and separate CR, then read `updates.jsonl` with shared-read access and count/compare `user_message_chunk` contents to the exact LF-removed body. Also save raw output and rendered screen. Multiple fragments prove input/parser behavior; one exact native user chunk with a failed regex proves an output-oracle defect. A tiny `a\nb` vs `a\rb` vs `a\r\nb` matrix and a temporarily larger burst gap separates character semantics from read grouping; do not weaken ordinary assertions or change their timing as the fix.
3. **Compare consumer/runtime and product.** Use the existing `NodeStdinProbe.cs`/`probes/stdin-probe.js` peer with raw mode and PROBE_OUT to record the same LF body and CR under inbox/modern; add scratch raw hex logging if needed. Compare with the fake's .NET input trace to separate console/runtime handling from generic ConPTY conversion. If traces still implicate product behavior, commission one explicitly authorized real installed-Grok canary comparing unbracketed LF and production bracketed LF, recording CLI version/backend and native transcript whole-body evidence. That may spend a turn and is not authorized by this Code task; no live provider was launched here.

## Windows confirmation command and expected counts

From the assigned desktop checkout, with final branch SHA and prerequisites matching the card's recorded run:

```powershell
pwsh -NoProfile -File scripts/run-checkpoint.ps1 -Name CP-W1 -Project tests/Antiphon.Agents.Pty.Tests -OutputPath bin-c984-windows/ -Filter '/*/*/*/*' -MinExecuted 618 -ExpectedSourceSha (git rev-parse HEAD) -ResultsRoot .antiphon/c984-windows
```

Expected on the card's 683-case roster: executed 618, passed 616, failed 2, skipped 65. The two remaining failures are FakeGrok LF (unchanged) and the CARD-0596 Grok inspect canary; UnixPtyArgvTests has 17 named skips and zero failures. This is a historical-roster expectation, not a claim that master has retained 683 tests. Report any changed discovery roster explicitly. Confirm the 17 skip reasons in the whole-project TRX. The whole selection runs offline real-provider oracles where installed; their dedicated opt-in follow-up is CARD-1007.

For an isolated LF probe, use the same driver with `-Filter '/*/*/FakeGrokContractTests*/Unbracketed_body_with_LF_line_endings_submits_as_one_turn_with_newlines_dropped*' -MinExecuted 1` and a fresh result directory; no Linux result discharges that Windows obligation.

## FOLLOW-UPS

- Separate Windows Debug: confirm 17 named Unix skips and investigate the unchanged LF failure using the three probes above. No proven test-assumption cause, so no speculative skip/fix for (b).
- CARD-0596 owns the existing Grok inspect canary; untouched.
- CARD-1007 created once for opt-in gating the two default offline provider oracles, after complete duplicate searches and reading CARD-0596. Creation returned a Linux `Join-Path` post-write error for the server's `C:` repository path; subsequent `card.ps1 get CARD-1007` confirmed the durable card, so creation was not retried.
- PCs remain pending for method-scoped SourceLanding Mutation. The scratch skip sensitivity check is diagnostic and does not discharge any PC.

## Exact checkpoint receipts

