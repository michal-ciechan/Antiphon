# CARD-1006: Linux Grok sign-in versus trust capture

Date: 2026-10-03. Investigate task: `1e39a459`. Source baseline:
`374fefe1e58d58ea3a14864a2fb27667292810aa`. Evidence only; no source, tests,
fixtures or plan edited. CARD-1006 was read with `card.ps1 get -Board Antiphon`.

## Outcome

**Confirmed within the measured startup window:** two independent fresh-home,
fresh-cwd Grok 1.0.41 launches with zero input parked on OAuth approval, before
any directory-trust prompt. Trust was blocked by sign-in throughout both
15-second observation windows. No trust, updater/notice or ready dashboard was
observed. This resolves the plan's credential-free capture premise against
admission: a fresh cwd does not yield the missing real trust frame here.
Trust's appearance *after successful authentication* is unmeasured because
authentication was prohibited. This is not proof about every future CLI version,
network condition or arbitrarily long wait.

The existing sign-in detector matches both real Linux approval frames, and the
existing classifier returns `SignIn`, never Ready. Its anchors already cover
this screen. The platform-wording defect is independently confirmed: the
unconditional `BlockReason` still says “as the Windows user” on Linux.
**Code admission is not satisfied:** real trust is absent and the plan's
exact-image throwaway-container isolation was not established.

## Provenance and isolation limits

Captured 2026-10-03T12:47:16.476Z–12:47:51.818Z on the assigned Linux runner
(Debian 12), installed `grok 1.0.41 (4220f3b224a6)`, terminal 120 columns by
30 rows. CLI SHA-256 before/after was
`9ce03ed23e16ea01072b4496263d6213a27899e1e3e107f008d36edf82e70407`.
The transport was a task-owned local `/app/Antiphon.SessionRunner` child and its
native `pty-host` snapshot route, using newly created empty homes/cwds under
`mktemp -d` root `/tmp/antiphon-card1006-jowC56X6`; the root is now deleted.
The running production **image tag/digest is unavailable**, not inferred from
CLI equality. The local runner reported Linux, wire backend label
`InboxConhost`, and build SHA `4358939ecd85d6e7ff0941f970879499cb930e3d`.
That label is recorded literally; it is not evidence of Windows ConPTY on Linux.
Source task identity is `1e39a459`; session identifiers were not retained.
The adjacent JSON contains one `real-rendered` Connecting frame and two
`real-rendered-redacted` approval frames, not constructed fixtures.

The available Docker daemon reported version 27.5.1 and an empty `docker ps -a`.
It is the nested daemon, not a view of the running production runner. A cached
`antiphon-c986-fix-grok-layer:6c6ca773` image has local image ID
`sha256:6eb4c02ba84ea3f961ab36a06d8d5f77d73d01ecda79b2443b331af915ff3347`,
no RepoDigests and no `/app/Antiphon.SessionRunner.dll`. A clean-environment,
network-disabled `docker run --rm` confirmed its Grok binary hash/version equal
to the installed binary; that establishes binary equality only. It cannot supply
the selected runner executable or prove the production image digest.
The discovery container exited and was automatically removed.

The brief permits a local capture fallback. Accordingly the measurement used
an isolated local runner in the current production container, **not** a new
throwaway container. This deviation from plan D-2.1 is explicit: these are real
credential-free screen observations, but they do not discharge its container
and exact-image custody gate. No production session or authenticated home was
used. No credentials were read, copied, supplied or inspected, including scratch
home contents. Isolation from every possible image-level configuration source
(CARD-0857) remains unproven; no MCP UI was observed.

Both runner and CLI started through `/usr/bin/env -i`, with explicit nonsecret
`PATH=/usr/local/bin:/usr/bin:/bin`, scratch `HOME`, `XDG_CONFIG_HOME`,
`XDG_CACHE_HOME`, `TMPDIR`, `TERM=xterm-256color`, `BROWSER=/bin/false`,
`ANTIPHON_PTY_AUDIT=0`, and a separate fresh `GROK_HOME` per CLI launch.
The runner had a dynamically allocated loopback listener, phone-home disabled,
Herdr/host statistics/CPU watchdog disabled, scratch runner state/log paths and
`PtyHostSourceDir=/app`. Audit cleanup was confined by the scratch TMPDIR.
Each CLI ran `grok --no-alt-screen --session-id <unretained-generated-id>`;
`TranscriptEnabled=false`, backend `pty-host`, no rules/resume/prompt argument.
No updater-disable variable was set: ordinary installed updater behavior was
retained. There was no server adapter to answer prompts.

## Ordered observations and privacy

The helper polled only its local `GET /sessions/{owned-id}/snapshot`, slept at
least 200 ms between reads and selected `RenderedScreen`; no response or raw
ANSI history was serialized. Source snapshot separation is
`src/Antiphon.SessionRunner/SessionRunnerRuntime.cs:2965`.
The helper never serialized raw history. However, the runner itself supplies a
non-null ordinary `.ansi.log` path at
`src/Antiphon.SessionRunner/SessionRunnerRuntime.cs:2266` and passes it to the
host at line 2321. `src/Antiphon.PtyHost/HostSession.cs:356` unconditionally
appends raw output when that path is non-null, independently of PTY audit and
transcript enablement. These exact statements were also inspected with
`git show 4358939ecd85d6e7ff0941f970879499cb930e3d:<path>`, matching the installed
runner build. This reconstructs temporary unsanitized ANSI persistence in the
scratch runner-state directory; its files were never read or copied and the
entire root was deleted. The plan's pre-disk sanitization premise is therefore
not discharged by `ANTIPHON_PTY_AUDIT=0` or rendered-only HTTP selection. This
is an additional capture-procedure custody gap for Plan to resolve, not a
change to ordinary production logging.

`GrokStartupCaptureStore.Format` was never used: its sign-in suppression at
`server/Infrastructure/Agents/SessionRunner/GrokStartupCaptureStore.cs:45`
remains intact, including the raw-history boundary at line 57.

| Probe | Start UTC | Screen changes, elapsed after launch response | Observations | End |
|---|---|---|---:|---|
| C-1, new home A/cwd A | 12:47:18.661 | blank 15 ms; Connecting 2336 ms; OAuth approval 2640 ms, unchanged thereafter | 62 | Individual kill returned Exited, exit code 0; 15195 ms including kill |
| C-2, new home B/cwd B | 12:47:34.256 | blank 11 ms; OAuth approval 2511 ms, unchanged thereafter | 62 | Individual kill returned Exited, exit code 0; 15505 ms including kill |

C-2 did not sample the transient Connecting frame; this is a sampling miss,
not a claim that the CLI skipped that state. Blank frames were not retained.
Both loops stopped polling by their 15000 ms deadline; final HTTP/kill time is
included separately above. Trust was never visible before sign-in, nor at any
sample after sign-in appeared. The only observed blocking state was approval.

The screen automatically displayed an OAuth device code without input. Each
retained approval frame replaces its nine-character `AAAA-BBBB`-shaped value at
zero-based row 15, columns 56–64 with `<CODE-9>` plus one trailing space (nine
cells). There are **two retained redactions**, one in each independent frame;
104 repeated snapshot sanitizations encountered this category. No original code
was printed or retained in the evidence artifacts. The ordinary runner-log
caveat below prevents a stronger claim about temporary disk writes. No URL, email, account/session identifier, bearer/key
material or unexpected operator path was retained. The displayed `/t/...` header
is the CLI's spelling of the task-generated cwd and was not rewritten.
No detector anchor was redacted.

An earlier helper attempt terminated its invoking task shell during cleanup
because its ownership filter matched the shell's command text; no usable evidence
survived it. A subsequent attempt rejected a device-code frame before retention.
Both were cleaned up, and the complete C-1/C-2 run above used a corrected
ownership filter and in-memory code redaction. They are not extra trust trials.

## Exact screen geometry and sanitized frames

All retained frames decode to exactly 30 LF-separated rows. Connecting has maximum
row length 67; both approval frames have maximum row length 85. Each has the
seven-row decorative braille logo at rows 4–10, columns 53–66. There are **no
box-drawing borders, composer boxes, U+276F or selection cursors** in these frames.
The complete non-ASCII glyphs are preserved as `\uXXXX` below and in JSON.

| Approval row | First text column | Exact nonprivate text |
|---:|---:|---|
| 13 | 38 | `Approve in your browser to finish signing in.` |
| 17 | 41 | `Make sure your browser shows this code.` |
| 19 | 41 | `If it doesn't open, click here to copy.` |
| 23 | 36 | `Copying not working? Click here to show full URL.` |
| 25 | 49 | `Waiting for approval...` |
| 27 | 54 | `ctrl+q  quit` |

The Connecting frame instead has `Connecting...` at row 13, column 54 and
`ctrl+q  quit` at row 22, column 54. No clicks, quit key, URL reveal, device-code
approval, trust response or model turn were performed. All geometry uses
zero-based screen coordinates.

UTF-8 SHA-256 covers each **sanitized decoded** frame, including blank rows and
the equal-width replacement's trailing space:

| JSON key | SHA-256 |
|---|---|
| C1-connecting | `beb362aeb8dfa6bbb1a2c95eff5e6fcc49527d0acf67c941653d40172fb2b277` |
| C1-sign-in | `e70a9a5d6630171b164c3bfa3a660e04a590514a0635c4044dd82db25dfb6d06` |
| C2-sign-in | `9a5793ae8bd3e74c1ef60c9125b516817ae59133b276f6b8aff2f43850707f52` |

The following JSON strings retain all rows and spaces; `\n` denotes LF.
```json
{
  "C1-connecting": "\n  /t/antiphon-card1006-jowC56X6/C-1-cwd\n\n\n                                                     \u2800\u2800\u2800\u2800\u2800\u2800\u28c0\u28c0\u2840\u2800\u2800\u2800\u2880\u2804\n                                                     \u2800\u2800\u2800\u28e0\u28fe\u283f\u281b\u281b\u281b\u281b\u2880\u2874\u2801\u2800\n                                                     \u2800\u2800\u28fc\u285f\u2801\u2800\u2800\u2800\u2880\u2874\u283b\u28ff\u2840\u2800\n                                                     \u2800\u2800\u28ff\u2847\u2800\u2800\u2800\u2814\u2801\u2800\u2800\u28ff\u2847\u2800\n                                                     \u2800\u2800\u28b9\u28f7\u2800\u2800\u2800\u2800\u2800\u2880\u28f4\u287f\u2800\u2800\n                                                     \u2800\u2880\u281e\u2801\u2820\u28b6\u28f6\u28f6\u28f6\u283f\u280b\u2800\u2800\u2800\n                                                     \u2810\u2801\u2800\u2800\u2800\u2800\u2800\u2800\u2800\u2800\u2800\u2800\u2800\u2800\n\n\n                                                      Connecting...\n\n\n\n\n\n\n\n\n                                                      ctrl+q  quit\n\n\n\n\n\n\n",
  "C1-sign-in": "\n  /t/antiphon-card1006-jowC56X6/C-1-cwd\n\n\n                                                     \u2800\u2800\u2800\u2800\u2800\u2800\u28c0\u28c0\u2840\u2800\u2800\u2800\u2880\u2804\n                                                     \u2800\u2800\u2800\u28e0\u28fe\u283f\u281b\u281b\u281b\u281b\u2880\u2874\u2801\u2800\n                                                     \u2800\u2800\u28fc\u285f\u2801\u2800\u2800\u2800\u2880\u2874\u283b\u28ff\u2840\u2800\n                                                     \u2800\u2800\u28ff\u2847\u2800\u2800\u2800\u2814\u2801\u2800\u2800\u28ff\u2847\u2800\n                                                     \u2800\u2800\u28b9\u28f7\u2800\u2800\u2800\u2800\u2800\u2880\u28f4\u287f\u2800\u2800\n                                                     \u2800\u2880\u281e\u2801\u2820\u28b6\u28f6\u28f6\u28f6\u283f\u280b\u2800\u2800\u2800\n                                                     \u2810\u2801\u2800\u2800\u2800\u2800\u2800\u2800\u2800\u2800\u2800\u2800\u2800\u2800\n\n\n                                      Approve in your browser to finish signing in.\n\n                                                        <CODE-9> \n\n                                         Make sure your browser shows this code.\n\n                                         If it doesn't open, click here to copy.\n\n\n\n                                    Copying not working? Click here to show full URL.\n\n                                                 Waiting for approval...\n\n                                                      ctrl+q  quit\n\n",
  "C2-sign-in": "\n  /t/antiphon-card1006-jowC56X6/C-2-cwd\n\n\n                                                     \u2800\u2800\u2800\u2800\u2800\u2800\u28c0\u28c0\u2840\u2800\u2800\u2800\u2880\u2804\n                                                     \u2800\u2800\u2800\u28e0\u28fe\u283f\u281b\u281b\u281b\u281b\u2880\u2874\u2801\u2800\n                                                     \u2800\u2800\u28fc\u285f\u2801\u2800\u2800\u2800\u2880\u2874\u283b\u28ff\u2840\u2800\n                                                     \u2800\u2800\u28ff\u2847\u2800\u2800\u2800\u2814\u2801\u2800\u2800\u28ff\u2847\u2800\n                                                     \u2800\u2800\u28b9\u28f7\u2800\u2800\u2800\u2800\u2800\u2880\u28f4\u287f\u2800\u2800\n                                                     \u2800\u2880\u281e\u2801\u2820\u28b6\u28f6\u28f6\u28f6\u283f\u280b\u2800\u2800\u2800\n                                                     \u2810\u2801\u2800\u2800\u2800\u2800\u2800\u2800\u2800\u2800\u2800\u2800\u2800\u2800\n\n\n                                      Approve in your browser to finish signing in.\n\n                                                        <CODE-9> \n\n                                         Make sure your browser shows this code.\n\n                                         If it doesn't open, click here to copy.\n\n\n\n                                    Copying not working? Click here to show full URL.\n\n                                                 Waiting for approval...\n\n                                                      ctrl+q  quit\n\n"
}
```

## Network and cleanup receipts

Unprivileged `/usr/bin/ss -tnpH` samples associated established outbound TCP
connections with owned Grok processes during both probes. C-1 PID 467 and C-2
PID 4544 connected to `104.18.18.80:443`, `104.18.19.80:443` and
`104.18.28.234:443`; additional owned child PIDs 540 and 4625 connected to
`104.18.19.80:443`. C-1's first observed established connection was at 57 ms.
This confirms startup network contact without login or a model turn.
Socket evidence does **not** identify the HTTPS request, DNS name, purpose or
payload; authentication versus updater traffic cannot be assigned from these
samples. UDP/DNS traffic and connections shorter than a poll interval were not
measured. No network tools submitted provider requests; only Grok and its own
startup children contacted external addresses.

Both owned sessions were stopped through their individual kill routes; no
`kill-all` was called. The helper then terminated and awaited its own runner and
remaining owned children. Final runner exit was code 0. The complete successful
run's tracked PID set was
`32376,376,467,540,1154,1238,4480,4544,4625,5145,5146`; the final `/proc`
process check found **zero remaining owned processes**. The set includes
short-lived inspection children, not just Grok. The nonempty canonical mktemp
root and task ownership marker were verified before deletion. Successful and
aborted attempts' roots are all absent. No scratch home was archived.
**Input calls and bytes: zero.** Authentication, trust, browser opening,
URL/code following and model submission were never performed.

## Mechanism and reconstruction from production

The rendered OAuth frame supplies three independent existing anchors:
`Approve in your browser to finish signing in`, `Make sure your browser shows
this code`, and `Waiting for approval`. The screen remains a pending approval
state with only the quit hint; it does not expose a trust choice or composer.
That is the observed obstruction, consistent with the older measured gate in
`docs/agent-kinds.md:408`. Fresh homes/cwds alone cannot satisfy the proposed
two-real-modal admission within this no-auth/no-input procedure.

`src/Antiphon.Agents.Pty/GrokDetectors.cs:58` recognizes these exact anchors.
The trust predicate at line 37 requires both the directory-trust question and
`Yes, proceed`; neither occurs. The classifier checks sign-in before trust at
`src/Antiphon.Agents.Pty/GrokStartupReadiness.cs:29`. Thus the code's order already
agrees with observed sign-in-first startup; this finding supplies no evidence
for reversing it or broadening readiness.

A foreground PowerShell reflection probe loaded the installed
`/app/Antiphon.Agents.Pty.dll` and called its actual detector/classifier methods
on the retained sanitized frames. This was a read-only measurement, not a build
or test-suite run; its binary is the runner build recorded above, not a freshly
compiled claim about this checkout.

```text
frame=C1-connecting signIn=false trust=false reason=Unknown ready=false
frame=C1-sign-in   signIn=true  trust=false reason=SignIn  ready=false
frame=C2-sign-in   signIn=true  trust=false reason=SignIn  ready=false
```

A second call to `BlockReason("/tmp/card1006-measurement-home")` returned the
following wrong-platform remedy on this Linux host (argument was invented and
no filesystem call was made):

```text
... as the Windows user that runs the session-runner, then re-dispatch. Every Grok pool launch on this machine will fail the same way until then.
```

The source concatenates that unconditional text at
`src/Antiphon.Agents.Pty/GrokDetectors.cs:76`, with no platform branch. This
confirms the wording defect separately from the incomplete trust qualification.

## Effect on the existing plan; admission remains closed

These are evidence consequences for the next Plan stage, not an amended design
or implementation. Refer to
`docs/superpowers/plans/2026-10-03-card-1006-grok-linux-signin-trust-plan.md`.

| Existing item | Measured consequence / required resolution before Code admission |
|---|---|
| D-1 / S-1 wording | The reported platform defect exists; the existing platform-neutral wording slice remains applicable. No new wording design was commissioned here. |
| D-2 / S-0 capture | Record sign-in-first in Linux 1.0.41 and the absent real trust capture. Exact-image throwaway-container custody also remains unmet. The existing snapshot transport's ordinary unsanitized ANSI log contradicts a pre-disk-sanitized capture claim; audit-off does not disable that log. Caller must resolve these admission dependencies; this report cannot mark S-0 complete. |
| D-3 / S-2 fixtures | Two real redacted approval frames are available for review. Trust has no real frame. A labelled synthetic trust composition would supply only synthetic coverage, never the required real trust acceptance. Installed sign-in anchors already match; no changed sign-in predicate is indicated. |
| D-4 update slot | `realUpdate.status=not-observed`, captureIds empty. There is no real updater evidence or basis for choosing a new detector. P-06/P-07 remain explicitly synthetic. |
| D-5 / S-3 precedence | Current sign-in-before-trust order agrees with the measurement. Post-authentication trust action/keys remain unqualified; no change is justified by this capture. |
| V-2 | Has real sign-in input, subject to review of provenance/isolation deviation. |
| V-3 / V-4 / V-10 | Their required real trust input is absent. V-4's two-real-kinds assertion cannot pass on these artifacts; no skip or synthetic relabelling discharges it. |
| V-5..V-9 | Real sign-in derived controls are possible evidence inputs. Every real-trust-derived control and the mixed-real-anchor control remains pending; 57 real-derived cases cannot be claimed. |
| CP-1..CP-5 | No checkpoint became unnecessary because sign-in gated trust. Their current closed manifest is not executable to completion as designed: CP-2/CP-5 depend on unavailable V-3/V-4/V-10 inputs. Recount/reimport only after the caller resolves/amends acceptance and the roster; do not silently lower minima. CP-3/CP-4 remain existing regression obligations, and CP-5 remains Windows-only. |

All **19 fail-open probe shapes retain their stated coverage purpose**.
P-01/P-03 require real trust that is missing. P-02 describes a sign-in *menu*
with U+276F: the measured approval screen has **no menu or cursor**, so that
shape must be identified as a synthetic variant rather than a real observed
screen. P-04/P-05 can derive from retained approval anchors but remain synthetic
compositions. P-06/P-07 still cover only their labelled synthetic updater shapes.
P-08..P-19's composer/geometry controls are independent of credential-free trust
reachability and remain useful. Trust-derived variants of any probe cannot be
called real-derived until real trust evidence exists. No shape is deleted by
this measurement, and no new fixture, test roster or checkpoint count is chosen.

The caller's unresolved choice is how to satisfy the existing real-trust and
container admission conditions, or whether to revise acceptance explicitly.
This task authorizes neither a credential exception nor treating synthetic
trust as authentic. Another identical no-input fresh-home launch is not evidence
that the current obstruction has been resolved.

## Remaining uncertainties and verification

- Actual trust appearance/order/actions after successful sign-in are unmeasured.
  Real post-authentication evidence under a separately authorized procedure
  would resolve that gap; this investigation grants no authorization.
- Production image tag/digest and strict throwaway-container isolation remain
  unverified. An accessible selected digest and isolated executable would resolve
  that custody gap; cached CLI equality is insufficient.
- No spontaneous update/notice was seen during the bounded probes. This says
  nothing about an unknown updater modal above an otherwise valid composer.
- Sampling can miss short transient frames. An approval screen persisting beyond
  15 seconds, or behavior under other network/version conditions, was not measured.
- No claim is made that a blank fresh GROK_HOME alone isolates every MCP source.

No tests were added or run, and no builds were run, as commissioned.
The installed-assembly reflection probe is reported above. JSON parse,
decode/re-encode/decode equality, 30-row counts, decoded UTF-8 hashes and privacy
scan all passed. The frame scan `/@|token|code=|http/i` returned **zero matches**;
manual inspection also confirmed no identifiers, actual device codes or URLs.
The only retained redaction category is the nine-cell device code. `git diff
--check` is required before publication.

Re-run the artifact sanity check without a CLI/provider launch:

```sh
node -e 'const fs=require("fs"),s=fs.readFileSync("docs/investigations/2026-10-03-card-1006-linux-grok-frames.json","utf8"),f=JSON.parse(s);if(/@|token|code=|http/i.test(s))throw Error("privacy");for(const v of Object.values(f))if(v.split("\n").length!==30||JSON.parse(JSON.stringify(v))!==v)throw Error("frame");console.log(Object.keys(f).length+" frames valid");'
```

## Not done, noted

Existing plan D-1's platform-neutral remedy and real-frame regression work remain for separately commissioned Plan/Code; no fix was designed or implemented here.

## FOLLOW-UPS

`card.ps1 search Grok -Board Antiphon -All` completed successfully (274 matches;
its text is a preview, not an absence proof), followed by full `get` reads for
CARD-0857, CARD-0988, CARD-0861 and CARD-1011. The ordering/admission finding belongs
to CARD-1006. CARD-0857 already owns ambient MCP isolation; CARD-0988 owns loaded
trust-test timing; CARD-0861 owns other terminal sizes; CARD-1011 owns real Windows
canaries/routing. The ordinary ANSI-log capture-procedure gap was also searched (`ANSI log`, five matches); it belongs to this card's capture admission rather than a new claim that production logging is broken. No new card or duplicate was filed, and none of those existing
cards substitutes for missing real Linux trust evidence.

--- next stage ---
next: plan
handoff: Record Linux 1.0.41 sign-in-first measurements and review local-runner isolation and ordinary raw-log custody gaps; real trust is inaccessible in the credential-free 15-second probes. Resolve real-trust/container admission before Code, label menu/updater compositions synthetic, and preserve current sign-in precedence and all 19 probe purposes.
artifact: docs/investigations/2026-10-03-card-1006-linux-grok-signin-trust-capture.md
