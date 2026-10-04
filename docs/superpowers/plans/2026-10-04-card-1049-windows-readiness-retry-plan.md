# CARD-1049: Windows readiness fixture and Git tool discovery

Status: **Windows I-1 measured; fixture failure confirmed; Plan reconciliation
before Code**. The original Plan was authored on Linux without Windows
measurements. The 2026-10-05 Windows investigation below meets I-1's no-listener
exit gate. Verification design remains the previously recorded design; this
evidence amendment implements no fix and authorizes no Code dispatch.

Inspected source: `19cd9131393fa104ad2f39879b20018a08400ef8`. Read CARD-1049,
CARD-1050 and CARD-1048 using `scripts/card.ps1 get <card> -Board Antiphon`.
Also read the full retained Windows Review
`133888ed-117c-4d87-8c18-102ab187c9fd`, not just its summary.

## Ground truth

| Card assumption | What the code/evidence actually establishes | Consequence |
|---|---|---|
| A closed local port must promptly return curl 7. | `ReadinessFixture` binds an IPv4 socket to `127.0.0.1:0` and holds it without calling `Listen` during the initial failures. It does not release the port and then reacquire it. Its endpoint is numeric IPv4; DNS/IPv6 fallback is not involved. | Ordinary release/rebind reuse is not present in the fixture. This does not by itself prove Windows exclusivity, absence of another listener, or packet-filter behavior. |
| Exit 28 merely breaks an assertion. | The generated curl wrapper waits for the listen acknowledgment only on code 7; the C# transition starts `Listen()` only after the exact journal entry `curl-end\|N\|7`. `AssertSuccess` also demands a prefix of 7s, and the permanent-refusal branch expects five 7s. | An assertion-only edit cannot fix delayed readiness: exit 28 leaves the server permanently unstarted. Both sides of the fixture handshake need the same admission rule. |
| Production might need to retry 28 differently. | `scripts/deploy-am-service.ps1` retries every nonzero curl result, retains `--connect-timeout 2 --max-time 3 -fsS`, allows five attempts/four two-second sleeps, and exits 44 before migrations/log checks on exhaustion. | Preserve the production script byte-for-byte. The reported exit 44 is its intended failed-readiness behavior. |
| The baseline proves the two curl commands have identical timing. | The pinned `10fe51b0f668a272d621a935c27e53313b8de1c8` baseline runs `curl -fsS` without a connect deadline. Windows Review reports one exit 7 there, versus exit 28 with the current two-second connect deadline. | Compare identical executable, endpoint ownership and arguments before attributing the difference to filtering. The deadline is a material variable. |
| All three tests are broken by Windows networking. | Retained Windows CP-3: machine PATH gave 0/3 passes because `cygpath` could not launch. With Git's `usr/bin` added, immediate readiness passed and the two retry methods failed (1/3 passes). | Tool discovery and networking are separate defects. Retained counts are historical evidence, not fresh qualification at this plan SHA. |
| Git's tools must be on the machine PATH. | `ToPosixPath` starts bare `cygpath`; `FindProgram` scans the parent's PATH for curl/sleep; the PowerShell seam starts bare `sh`. Child PATH currently only prepends wrappers. | Discover a consistent Git shell/tool directory locally and modify only child environments; do not require a machine PATH edit. Preserve the original curl selection to avoid hiding this failure by choosing a different curl build. |
| CARD-1050 supplies a shared cygpath helper. | Its landed artifact is a plan for explicit Linux placement of C1008 host tests; it introduces no Git-tool resolver. At this source its proposed `RequireNativeLinux` implementation is not present. Existing `RemoteScriptContractTests.LinuxShell` uses WSL on Windows. | Do not depend on an unimplemented helper or use WSL: a different network namespace would not exercise the native .NET loopback fixture. |
| CARD-1048 fixes the same environment problem. | `scripts/build-slot.ps1` now removes child `PSModulePath` only for the exact Windows `powershell.exe` executable leaf. Readiness launches `pwsh`. | Reuse the existing build-slot wrapper unchanged. It is the common execution boundary, not a shell-path resolver. |

Owners read: [project conventions](../../project-context.md),
[HTTP operations](../../ops-http.md), [orchestration](../../orchestration-loop.md),
and [testing/build operations](../../testing-and-build.md). Related designs:
[CARD-1050](2026-10-04-card-1050-windows-bash-host-tests-plan.md) and
[CARD-1048](2026-10-04-card-1048-build-slot-module-path-plan.md).

## Cause established versus packet mechanism still inferred

**Established from source:** the fixture treats one transport error code as a
readiness-transition signal, even though the deployment loop treats all failed
HTTP probes as not ready. The two code-7-only handshake gates explain why a
reported code 28 makes the success case exhaust its budget.

**Original leading explanation, packet mechanism still inferred:** Windows TCP
can resend SYN after a refusal and delay reporting the connection failure for
seconds. The curl maintainer documents this for both IP families and also notes
a newer curl workaround. The actual executable/version therefore matters.
See [the maintainer's account](https://daniel.haxx.se/blog/2024/08/14/slow-tcp-connect-on-windows/).
Curl documents that exceeding `--connect-timeout` produces exit 28, independently
of whether an eventual connection failure would produce another code; see
[curl's timeout contract](https://github.com/curl/everything-curl/blob/master/usingcurl/timeouts.md).
Together these explain how the reported timed/untimed difference can occur
without an accepting listener or port reuse. They do not establish which
mechanism occurred on the reviewed machine.

### I-1: required Windows measurement, before any assertion change

Commission one Windows Investigate, 15-minute diagnostic budget plus any host
slot wait. Use the same curl that the original parent's PATH selected; record
its absolute executable and `--version`, Windows version, selected `sh`, `sleep`
and `cygpath` paths. Record only those paths/versions, not the environment dump.
Run an owned PowerShell/.NET diagnostic through `scripts/build-slot.ps1`; no
full test build is needed to distinguish the socket states. Keep diagnostic
scripts/logs under ignored task evidence and await every child.

Use three fresh, owned IPv4 ephemeral endpoints, serially:

1. **Bound, not listening**, matching the current fixture. Retain the same
   socket/port for all probes. Record bind time, owning PID, socket options and
   the port-specific Windows TCP table before/during each probe. First use the
   fixture's socket options, then a separate exclusive-address-use socket as a
   control. Never release a chosen port while probing it.
2. **Listening and intentionally withholding an HTTP response.** Record accept
   and GET times/counts. This identifies an established-connection timeout.
3. **Listening and returning the exact fixture HTTP 200 body.** This must
   succeed; otherwise investigate tool/network admission rather than widening
   readiness assertions.

Against the retained non-listening endpoint compare
`--disable --noproxy '*' --connect-timeout 2 --max-time 3 -fsS` with a diagnostic
`--disable --noproxy '*' --connect-timeout 10 --max-time 12 -fsS`. The second
command is a probe, never a change to the deployment script or tests. Capture
curl exit, elapsed time, `time_connect`, `time_total`, HTTP code and bounded
verbose stderr separately from the response body. Keep one outer 15-second
deadline per probe. Repeat the pair once with a fresh endpoint only if needed
to separate an ownership race from a stable delay; no broad repeat battery.

Use the socket/TCP-table/accept observations to distinguish no listener from an
accepted connection and to identify any foreign owner. To claim SYN/RST retry
as the host cause, collect an available port-scoped loopback trace showing
RST replies followed by SYN retries. If no such observation is available, label
the TCP mechanism inferred rather than claiming filtering is ruled out. A
trace showing missing responses supports a filtering/drop investigation;
established TCP plus an accepted request supports a stalled listener. Never
change firewall, registry, curl installation or machine PATH to obtain green.

**Exit gate:** append the measured executable/version, port ownership, timing,
connection stage and cause classification to this plan. If there is no listener,
no reuse/foreign owner, the bounded command returns 28, the longer command
returns 7, and immediate HTTP succeeds, the fixture's failed-probe equivalence
is justified even if the low-level SYN mechanism remains explicitly inferred.
Accepting/listener/reuse evidence requires a fixture correction first. If the
required distinctions cannot be measured, keep next `investigate`; do not
promote the hypothesis to fact or dispatch Code.

### I-1 measured outcome, 2026-10-05, task a7c42072

**Confirmed:** the short command times out before the same held socket reports
refusal with the longer command. Both code-7-only fixture gates remain closed
after 28. Source/reconstruction citations and essential trace/TCP-table excerpts
are in the [investigation](../../investigations/2026-10-05-card-1049-amservicedeployreadinesstests-retry-cases-fail-on-windows-curl-exits-28-not-7-and-cygpath-missing-from-path.md).
Inspected/measured source was `d2a771ff8041908b03581f15c8ca965750804847`;
production and test source were unchanged.

Original-PATH curl selection: `C:\Windows\system32\curl.exe`, curl/libcurl
8.13.0, Schannel, release 2025-04-02. Host: Windows 10 Pro, `10.0.19045`, build
19045. Original-PATH sh/sleep/cygpath searches all failed; installation-relative
candidates exist in `C:\Program Files\Git\usr\bin\`: sh 5.2.37(1), sleep 8.32,
cygpath 3.6.3. No parent or machine PATH was modified and no WSL was used.

All ports were fresh IPv4 ephemeral binds retained throughout their probes.
Default options: ExclusiveAddressUse=false, ReuseAddress=0, Blocking=true,
NoDelay=false; the separate exclusive control changed only ExclusiveAddressUse.
Both non-listening sockets belonged to PID 12352. Before/during/after
`netstat -ano -p tcp` samples found only each curl PID's SYN_SENT on those ports,
no LISTENING/ESTABLISHED or foreign owner; socket custody was retained in the
owning process. There was no release/rebind. Listening controls recorded the
expected owned listener and established connection.

| Probe | Port | Deadline connect/max s | curl exit | Outer wall ms | time_connect s | time_total s | HTTP | Accept/GET |
|---|---:|---|---:|---:|---:|---:|---|---|
| Bound, default | 61099 | 2/3 | 28 | 2132.6 | 0.000000 | 2.014509 | 000 | 0/0 |
| Same bound socket | 61099 | 10/12 | 7 | 2154.4 | 0.000000 | 2.049447 | 000 | 0/0 |
| Bound, exclusive | 61104 | 2/3 | 28 | 2122.5 | 0.000000 | 2.013480 | 000 | 0/0 |
| Same exclusive socket | 61104 | 10/12 | 7 | 2145.3 | 0.000000 | 2.052217 | 000 | 0/0 |
| Listening, accepted stall | 61118 | 2/3 | 28 | 3245.3 | 0.004323 | 3.036306 | 000 | 1/1 |
| Listening, exact fixture body | 61156 | 2/3 | 0 | 413.6 | 0.010911 | 0.219926 | 200 | 1/1 |

Default curl trace: short reports `Connection timed out after 2014 milliseconds`;
long reports `failed: Connection refused` after approximately 2.05 seconds.
Exclusive control repeats that difference. Stall trace reports connected,
request completely sent, then no response before the max-time deadline. Exact
HTTP 200 returned the fixture's 44-byte body, stderr empty. These distinguish
delayed refusal reporting from an accepted HTTP stall. The packet-level reason
for the delay is not established.

Domain/Private/Public firewall profiles were Enabled=True; default inbound and
outbound properties were NotConfigured. Ethernet was Public and Tailscale
Private. This is configuration context, not packet-drop evidence. The session
was non-elevated (Medium integrity; Administrators deny-only); `pktmon status`
failed with cannot obtain state / file not found; tshark was absent from PATH.
No packet capture was started. **SYN/RST retry remains inferred; filtering or
security-product contributions are not ruled out at packet level.** A scoped
loopback SYN/RST capture would settle that narrower uncertainty. No firewall,
registry, tool installation or machine setting was changed.

Both diagnostic commands took host slots, completed foreground and released
their leases. Each curl had an outer 15-second deadline; none hit it. Six
accepted observations came from seven curl invocations: the initial ready
control used an incorrect 51-byte object and was discarded, then only that
control was corrected/repeated with the exact 44-byte fixture array. All raw
attempts remain under ignored `.antiphon/c1049-i1/`, including the correction
under `exact-body/`. No test build, TUnit method or whole-class lane ran. Every
owned child was awaited and socket disposed; no bin output was created.

**I-1 gate result:** no listener/reuse/foreign owner observed, short=28,
same-socket long=7, immediate exact-body HTTP=0. The existing D-2 prerequisite
is measured. No evidence requires the foreign-listener/reuse branch or supports
dropping native Windows coverage. Repaired-test qualification is still pending.

#### Not done, noted

I-1 supports existing D-2's Windows 7/28 fixture allowance at both gates; Plan must reconcile the measured choice before Code, keeping production unchanged.

## Decisions

- **D-1: retain the production readiness contract.** No changes to
  `scripts/deploy-am-service.ps1`, its retry count, waits, curl flags, exit 44,
  suppression of failed bodies, or downstream verification. Reject increasing
  connect timeouts, adding retries or replacing real curl with canned success.
- **D-2: preferred small repair after I-1's no-listener gate: admit 7 or 28
  for the fixture's initial not-ready probes on Windows.** Linux retains its
  exact 7 expectation for the non-listening fixture. Both platforms retain
  exact 28 for intentionally accepted/stalled HTTP and exact 22 for HTTP 503.
  Preserve raw curl exit codes in the journal; never rewrite 28 to 7. Update
  both handshake gates and the scenario-specific assertions together. Reject
  a blanket any-nonzero allowance and assertion-only repair. I-1 now records
  that this candidate's no-listener prerequisite was observed; implementation
  and final verification remain unperformed.
- **D-3: reject hunting for a fixed fast-refused port.** The fixture already
  retains its socket. Releasing/rebinding, choosing a fixed port or trying
  ports until one returns 7 introduces ownership races or selects around a
  stack timing characteristic. Choose an ownership/fixture repair instead of
  D-2 only if I-1 actually finds a foreign listener, early Listen, or reuse.
  In that event amend this design and its verification before Code.
- **D-4: keep native Windows coverage.** The class drives the supported local
  PowerShell deployment caller and real HTTP readiness, without C1008's POSIX
  ownership/flock requirements. A Linux-only class would erase relevant
  Windows coverage. Missing Git prerequisites are an actionable failure, not
  a platform skip or a green zero-test run.
- **D-5: resolve tools within this fixture.** Add a small private tool-path
  value/helper in `AmServiceDeployReadinessTests.cs`, not a repository-wide
  launcher abstraction. On Windows inspect PATH-resolved Git/sh locations and
  their installation-relative `usr/bin` candidates; require actual `sh.exe`,
  `cygpath.exe` and `sleep.exe` files from one Git installation. Do not hard-code
  a drive/install root, accept a WSL launcher, or search the whole disk.
  Invoke cygpath by absolute path with `ArgumentList` (`-u`, `--`, native path).
  Resolve real curl against the supplied original PATH before adding Git
  tools. Child PATH is wrappers, selected Git tool directory, then the original
  PATH; real sleep is absolute. Keep wrapper precedence, LF and secret removal.
  Parent PATH is never mutated. Missing tools name the missing prerequisite.
- **D-6: one Code slice, 30-60 minutes, after diagnosis.** Fold verification
  for this fixture-only repair into this plan. Exact methods only, no whole
  Unit, namespace or assembly run. Independent ordinary Review precedes land;
  two method-scoped positive controls run in post-land SourceLanding Mutation.
- **D-7: publish the plan through normal task landing.** This delegate commits
  and pushes only its assigned branch. Landing requires a succeeded task, so
  the caller lands the exact Plan tip promptly after settlement, then sends
  the Windows investigation. No direct master push or assigned-branch rebase.

## Implementation slices

| Slice | Files | Work and completion |
|---|---|---|
| I-1, prerequisite | This plan; ignored diagnostic evidence only | Perform the Windows measurement above. Record the selected decision and evidence. If D-2 is supported, the folded design below needs no separate TestDesign; otherwise revise it before implementation. |
| S1, one 30-60 minute Code dispatch | `tests/Antiphon.Tests/Infrastructure/AmServiceDeployReadinessTests.cs`; short readiness note in `docs/testing-and-build.md` | Implement D-2/D-5, preserve all three existing methods, extend their oracles as below, add the one Windows tool-discovery method. Commit/push before CP-1..CP-7. No production-script or shared C1008/build-slot helper changes. |

For D-2, use scenario-specific allowed initial failure codes. The shell
acknowledgment gate and C# transition must agree on the selected codes and exact
attempt number. Neither gate accepts 0 as an initial failure. The non-listening
case must record zero served requests before transition. Only the subsequent
real HTTP 200 allows deployment success and the later migration/log checks.

Add one deterministic accepted-timeout-then-ready subcase to the existing
`RetriesUntilHttpReadyAsync`, after the baseline and before the two existing
delayed-success subcases: listen from the start, accept/read requests but
withhold responses for the first two probes, then enable HTTP 200 only after
the second completed curl 28 is journaled. Reuse the same acknowledgment
handshake so the third probe starts after that transition. This produces
`[28,28,0]` with real curl on either OS and makes the 28-handling control
independent of the installed Windows curl's refusal optimizations. Distinguish
this explicit scenario from the non-listening scenario in names/diagnostics.
Retain and cancel/join accepted handler tasks and the transition task during
fixture cleanup; do not leave additional detached tasks or widen deadlines.

## Verification design

This is the folded design for D-2. **The I-1 gate still precedes Code.**

### Inspection

Read all three test bodies, `AssertSuccess`, `AssertCurlArguments`,
`LoadBaselineAsync`, `RunLegacyAsync`, `ReadinessFixture` and its shell wrappers;
the whole `scripts/test-deploy-am-service-readiness.ps1` seam; the `ReadinessCase`
dispatch and legacy curl seam in `scripts/test-deploy-am-service.ps1`; the
current and pinned-baseline deployment verification commands; the build-slot
module-path condition; and the C1008/LinuxShell entry/tool boundaries. No new
production asynchronous delivery path, queue or user/session receipt is involved.

### Delivery inventory

The local chain is real curl -> per-fixture attempt journal -> fixture listener
transition -> acknowledgment file -> next real HTTP request. Identity is the
fixture directory plus attempt ordinal. Fake Docker/SSH/SCP isolate deployment;
they do not establish a real remote rollout. Success requires the body, adapter
parse and ordering assertions, not an acknowledgment file alone.

### Proves it works now

- **V-1:** `RetriesUntilHttpReadyAsync` retains the pinned baseline (one 7,
  no sleeps/later calls, failed verdict), then real delayed success on attempts
  3 and 5. Assert exact call/sleep counts, only the allowed initial failures,
  final 0, exactly one output body and preserved migration/log/later-call order.
  Add the real `[28,28,0]` scenario described above with a decisive
  `C1049-timeout-transition-succeeds` assertion on shell exit 0 before the
  common success assertions. Observe accepted requests in that scenario.
- **V-2:** `FailsWhenHttpReadinessBudgetIsExhaustedAsync` retains all three
  internal modes: non-listening, accepted timeout and HTTP 503. Every mode
  requires exactly five failed probes/four sleeps, unchanged arguments, exit
  44, no later work or migrations/logs, empty success output, and no leaked
  response/sentinel. Assert zero served requests in non-listening mode and
  five served requests for the two listening modes. Windows permits only 7/28
  in the non-listening mode; Linux requires 7; other modes stay exactly 28/22.
- **V-3:** new Windows-only method
  `ResolvesGitToolsWithoutUsrBinOnPathAsync` supplies a fixture-local PATH with
  the discovered Git `usr/bin` entry removed and Git `cmd`/`bin` retained.
  First assert cygpath is absent from that supplied search list. Resolve through
  the production fixture helper and assert the full tool set with witness
  `C1049-toolchain-resolved-without-usrbin`; inspect a nonthrowing resolution
  result before launching, so a failed resolution is an assertion failure.
  Convert/read back a temp file whose native path contains spaces through real
  cygpath/sh; assert its sentinel and execute one immediate-ready fixture using
  that supplied PATH. Assert curl's selected absolute path still matches the
  original PATH resolution and the parent PATH is byte-for-byte unchanged.
  Use an existing supported TUnit Windows admission guard; CP-4 selects this
  method only on Windows, where absent Git tools must fail, never skip.

### Guards the regression

- **R-1:** `SucceedsImmediatelyAndPreservesDeployContractAsync` still requires
  one curl 0, one served request, no sleep, the real body/adapters, and the
  existing legacy harness success/contract labels. Preserve all assertions.
  This named existing method's embedded legacy run is authorized; an additional
  standalone deployment suite or PowerShell 5.1 battery is not needed here.
- **R-2:** V-1/V-2 keep exact production curl options, attempt boundaries,
  terminal verdicts and response non-disclosure. Review verifies the production
  script diff is empty. The historical baseline remains exactly 7; no broadening
  is justified for its command without a connect deadline.

### Guard inventory

Two changed behaviors, two distinct controls, zero unmapped changed guards.
Production retry/budget/non-disclosure guards are unchanged and retain R-1/R-2;
their older mutation battery is not recommissioned by this test-only repair.

G-1 maps only to PC-1 (timeout transition); G-2 maps only to PC-2 (tool
discovery without a global PATH change). No duplicate guard-to-PC mappings.

### Positive controls

| Guard / PC | Compiling test-fixture mutation | Exact method filter | Decisive red |
|---|---|---|---|
| G-1 / PC-1: the fixture can transition after real timeout failures | Restore code-7-only admission in both the curl wrapper's acknowledgment gate and C# transition, leaving the new accepted-timeout scenario and its assertions intact. | `/*/Antiphon.Tests.Infrastructure/AmServiceDeployReadinessTests/RetriesUntilHttpReadyAsync` | `C1049-timeout-transition-succeeds`: expected shell 0, observed 44 with five real curl 28s. All tools/HTTP fixture launched. |
| G-2 / PC-2: Windows Git tool discovery works without global PATH changes | Remove only installation-relative discovery of Git `usr/bin`, retaining ordinary supplied-PATH discovery and the nonthrowing result. | `/*/Antiphon.Tests.Infrastructure/AmServiceDeployReadinessTests/ResolvesGitToolsWithoutUsrBinOnPathAsync` | `C1049-toolchain-resolved-without-usrbin` fails at the resolution assertion; the independent supplied-PATH precondition and Git installation discovery succeeded. |

Run PC-1 in the Windows lane alongside PC-2. Each is one exact-method
baseline/red/restore-green cycle; each phase executes exactly one TUnit method.
Do not batch: both faults touch the same file. No build/fixture error, watchdog,
skip, missing TRX or zero tests counts as red. Preserve raw phase evidence in
the assigned external SourceLanding root; restore exact tracked bytes, join
all children and remove owned alternate outputs. Mutation makes no commits
from its snapshot. Code runs ordinary verification only.

### Out of scope

No production readiness policy, Linux-only class conversion, shared C1008/WSL
helper, build-slot policy, live deployment or machine configuration change.
I-1 is diagnostic evidence, not a replacement for final native Windows tests.

### Placement and execution

Read `GET /api/runner-defaults` and `GET /api/session-runners` at approximately
23:28 UTC on 2026-10-04: defaults revision 2, eligible Windows and Linux entries.
Re-read at dispatch; no fleet location is embedded as a plan requirement.
Use `-Platform Windows` for I-1, CP-1..CP-4 and PCs; `-Platform Linux` for
CP-5..CP-7's native comparison. Omit `-Runner`. Planning needs no OS pin;
`-Platform Any` clears an inherited constraint. The caller arranges the second
lane; a delegate does not sub-delegate. Do not mutate source between lane runs.

The checkpoint importer has no Platform column: the following row ranges and
Group names are the placement contract. Invoke the checkpoint tool once per
committed lane group, `run --plan <this-plan> --rows CP-1,CP-2,CP-3,CP-4
--expected-source-sha <S> --serial` on Windows, and rows CP-5,CP-6,CP-7 on
Linux. Do not run the whole table blindly on one OS. Keep every wait owned
until terminal; exit 75 means continue waiting. Each row is bounded at the
importer's 15-minute minimum timeout with estimates at most five minutes, or
18 minutes for CP-1; no row selects more than one method.

Use the existing checkpoint tool when available. If it requires bootstrap,
declare one supporting build through `scripts/build-slot.ps1` into
`bin-c1049-tool/`; invoke subsequent tool verbs with `dotnet run --no-build`
and that same output. Do not hold an outer slot while self-leasing rows run.
All other build/test drivers take the host slot. Slot timeout is not run, not
permission for an unleased retry. Record any supporting build and its SHA.
The ordinary table is otherwise closed; no full Unit run or extra repetitions.

Validate receipts for exact committed S, `dirty=0`, `sourceState=clean`,
`buildSource=verified`, exact roster and zero failures/skips. Retain unedited
CHECKPOINT lines and ignored TRX/JSON/logs. Code/Review run
`scripts/check-evidence-diff.ps1 -BaseRef <task-base> -HeadRef <S>` across the
full history. Clean only task-owned `bin-c1049-*/` outputs after children exit.

### Cost

Estimated ordinary floor: 21 minutes (12 Windows, 9 Linux), two test
project builds. Authoring 20-30 minutes plus optional tool bootstrap 5 minutes:
46-56 minutes total Code work including both lanes, excluding external slot or
restore delays. If lane execution needs a second task, it verifies the same
committed S. I-1's 15 minutes are separate. Mutation: two cycles/six phase
builds, estimated 30-42 minutes; ordinary plus PC floor 51-63 minutes, excluding
Review/authoring/diagnosis. Review repeats the relevant exact rows at final S.
Build reuse saves five unnecessary project builds versus building each row;
no unrelated coverage saving is claimed as correctness evidence.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1049-windows/` | windows-retry | `/*/Antiphon.Tests.Infrastructure/AmServiceDeployReadinessTests/RetriesUntilHttpReadyAsync` | V-1, R-2 | exactly 1 passed, 0 failed/skipped | 1 | 6 | true |
| CP-2 | S1 | CP-1 | windows-exhaustion | `/*/Antiphon.Tests.Infrastructure/AmServiceDeployReadinessTests/FailsWhenHttpReadinessBudgetIsExhaustedAsync` | V-2, R-2 | exactly 1 passed, 0 failed/skipped | 1 | 3 | true |
| CP-3 | S1 | CP-1 | windows-immediate-legacy | `/*/Antiphon.Tests.Infrastructure/AmServiceDeployReadinessTests/SucceedsImmediatelyAndPreservesDeployContractAsync` | R-1 | exactly 1 passed, 0 failed/skipped | 1 | 2 | true |
| CP-4 | S1 | CP-1 | windows-git-tool-discovery | `/*/Antiphon.Tests.Infrastructure/AmServiceDeployReadinessTests/ResolvesGitToolsWithoutUsrBinOnPathAsync` | V-3 | exactly 1 passed, 0 failed/skipped | 1 | 1 | true |
| CP-5 | S1 | `tests/Antiphon.Tests -> bin-c1049-linux/` | linux-retry | `/*/Antiphon.Tests.Infrastructure/AmServiceDeployReadinessTests/RetriesUntilHttpReadyAsync` | V-1, R-2 | exactly 1 passed, 0 failed/skipped | 1 | 5 | true |
| CP-6 | S1 | CP-5 | linux-exhaustion | `/*/Antiphon.Tests.Infrastructure/AmServiceDeployReadinessTests/FailsWhenHttpReadinessBudgetIsExhaustedAsync` | V-2, R-2 | exactly 1 passed, 0 failed/skipped | 1 | 2 | true |
| CP-7 | S1 | CP-5 | linux-immediate-legacy | `/*/Antiphon.Tests.Infrastructure/AmServiceDeployReadinessTests/SucceedsImmediatelyAndPreservesDeployContractAsync` | R-1 | exactly 1 passed, 0 failed/skipped | 1 | 2 | true |

## Acceptance and next stage

The evidence amendment can land now. I-1 records the Windows cause
classification and meets the existing D-2 prerequisite. Next is Plan to
reconcile the measured choice and authorize Code; this investigation has not
changed the implementation design. In the D-2 case, the previously folded
verification design supplies Code's closed list.
Acceptance needs all seven rows green at the same source S, production script
unchanged, preserved raw exit evidence, and the Windows sanitized-PATH proof.
No live deploy, runner restart, curl upgrade or global environment edit is in
scope. Next: **Plan**, with the confirmed fixture mechanism and explicit
packet-level uncertainty recorded above, before Code.
