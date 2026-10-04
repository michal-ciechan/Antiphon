# CARD-1049: AmServiceDeployReadinessTests retry cases fail on Windows: curl exits 28 not 7, and cygpath missing from PATH

Outcome: **confirmed fixture failure mechanism; I-1's no-listener gate met**.
Native Windows curl's two-second connect deadline expires before a held,
non-listening endpoint reports refusal at approximately 2.05 seconds. The
fixture's two code-7-only transition gates then prevent it from starting its
listener. The packet-level reason for that refusal delay remains inferred.

Task: `a7c42072`, Investigate, Windows, evidence only. Measured 2026-10-05
00:52–00:53 Europe/London (2026-10-04 23:52–23:53 UTC).
Source: `d2a771ff8041908b03581f15c8ca965750804847`; the assigned branch began at
`19cd9131393fa104ad2f39879b20018a08400ef8` and fast-forwarded to the locally
available landed plan. Production and test source were unchanged during probes.

## Evidence custody and execution

Raw task-owned scripts, JSON, bodies, stderr and curl `--trace-time` output remain
gitignored at
`C:\Antiphon\worktrees\card-task-a7c42072\.antiphon\c1049-i1\`.
The durable measured results and essential transcript excerpts are below.
`results.json` has the original six probe records; the exact-body correction is
`exact-body/ready-short-result.json`. Each named probe also has `-tcp.json`,
`-trace.txt`, `-metrics.txt`, `-stderr.txt`, and an output body if curl created it.
`sockets.json` records binding/options/owner; `tools.json`, `curl-version.txt`,
`windows.json`, `firewall-readable.json`, `network-profiles.json` and
`capture-access.txt` record the host context. `evidence-sha256.json` inventories
the task evidence bytes separately from this report.

Commands were foreground and awaited, through the existing host slot gate:

```powershell
pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1049-i1-socket-diagnostic -- pwsh -NoProfile -File .antiphon/c1049-i1/probe.ps1
pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1049-i1-exact-http-body -- pwsh -NoProfile -File .antiphon/c1049-i1/probe.ps1 -EvidenceRoot .antiphon/c1049-i1/exact-body -Modes ready
```

Slot transcript: first lease `7e481925-f4eb-4bb9-bd6a-5959d40850e5`,
`waited=0s maxcpucount=4`, released `held=14s`; correction lease
`04c10ace-58ba-470a-a7bb-42e6a7aebeac`, `waited=0s maxcpucount=4`, released
`held=4s`. Both commands exited 0. The diagnostic enforced one outer
15-second deadline per curl process, a 1.5-second deadline per TCP-table child,
and a 15-minute diagnostic budget. Every owned child was joined; every owned
socket was disposed. No build, TUnit method, whole-class lane or deployment ran.

**Measurement correction:** the first ready probe on port 61129 returned an
incorrect 51-byte JSON object, rather than the fixture's 44-byte array. It is
retained as a discarded body-control attempt, not used to satisfy I-1. After
that command completed, only the ready control was repeated with the exact
constant from `tests/Antiphon.Tests/Infrastructure/AmServiceDeployReadinessTests.cs:17`.
The six accepted diagnostic observations below therefore come from seven total
curl invocations. The current `probe.ps1` contains the corrected body. No broad
repeat battery or additional closed-port pair was run.

## Host and executable selection

Windows 10 Pro, version `10.0.19045`, build `19045`. Resolving curl with the
fixture's original-PATH algorithm selected `C:\Windows\system32\curl.exe`:
`curl 8.13.0 (Windows) libcurl/8.13.0 Schannel zlib/1.3.1 WinIDN`, release
2025-04-02. The diagnostic used that absolute executable throughout, with
`--disable --noproxy '*'`; it did not select Git curl or WSL.

Original PATH has **no sh, sleep or cygpath** by that same search. Git is
`C:\Program Files\Git\cmd\git.exe`. Its installation-relative files exist:

| Tool candidate (no original-PATH resolution) | Absolute path | Measured version |
|---|---|---|
| sh | `C:\Program Files\Git\usr\bin\sh.exe` | GNU bash 5.2.37(1)-release, x86_64-pc-msys |
| sleep | `C:\Program Files\Git\usr\bin\sleep.exe` | GNU coreutils 8.32 |
| cygpath | `C:\Program Files\Git\usr\bin\cygpath.exe` | cygwin 3.6.3 |

No shell was required for these native .NET/curl probes. Those candidate tools
were only version-probed by absolute path; no PATH was modified. This confirms
the prerequisite absence in this session, independently of TCP behavior.
The existing fixture searches original PATH at
`tests/Antiphon.Tests/Infrastructure/AmServiceDeployReadinessTests.cs:309` and
launches bare cygpath at `:323`.

Domain, Private and Public firewall profiles all reported `Enabled=True`, with
both default-action properties `NotConfigured` in `Get-NetFirewallProfile`.
Active interface profiles were Ethernet/Public/IPv4 Internet and
Tailscale/Private/IPv4 LocalNetwork. Those profile properties are not packet-drop
evidence or a complete effective rule/security-product inventory. No firewall,
registry, installation or machine setting was changed.

## Socket ownership and results

All endpoints were fresh IPv4 `127.0.0.1:0` bindings held continuously until
their respective probes finished. Default socket options were
`ExclusiveAddressUse=false`, `ReuseAddress=0`, `Blocking=true`, `NoDelay=false`.
The separate exclusive control changed only `ExclusiveAddressUse=true`.
Initial socket owner PID was `12352`; corrected ready owner PID was `28212`.
The four initial binds were at UTC `23:52:32.1731198`, `23:52:36.7396853`,
`23:52:41.1683361`, and `23:52:44.5857453`, in the order recorded above.
The corrected ready bind was at UTC `23:53:06.7795764`.

Short probes used `--connect-timeout 2 --max-time 3 -fsS`; long probes used
`--connect-timeout 10 --max-time 12 -fsS`. Both included the same disable/proxy
flags, endpoint, trace and write-out arguments. The longer timeout was diagnostic
only. Wall time includes process polling/table sampling; curl's own total is
the comparison metric.

| Socket / probe | Port | curl PID | Exit | Outer wall ms | time_connect s | time_total s | HTTP | Accept / GET |
|---|---:|---:|---:|---:|---:|---:|---|---|
| Default bound, short | 61099 | 38444 | 28 | 2132.6 | 0.000000 | 2.014509 | 000 | 0 / 0 |
| Same default bound, long | 61099 | 24208 | 7 | 2154.4 | 0.000000 | 2.049447 | 000 | 0 / 0 |
| Exclusive bound, short | 61104 | 9452 | 28 | 2122.5 | 0.000000 | 2.013480 | 000 | 0 / 0 |
| Same exclusive bound, long | 61104 | 39140 | 7 | 2145.3 | 0.000000 | 2.052217 | 000 | 0 / 0 |
| Listening, withholding response | 61118 | 32936 | 28 | 3245.3 | 0.004323 | 3.036306 | 000 | 1 / 1 |
| Listening, exact fixture body | 61156 | 38736 | 0 | 413.6 | 0.010911 | 0.219926 | 200 | 1 / 1 |

All seven invocations completed inside their outer deadlines. The non-listening
probes emitted empty success bodies and no remote endpoint after connection
failure. The exact ready body was
`[{"channel":"telegram"},{"channel":"slack"}]` (44 bytes), stderr empty.

TCP-table evidence from `netstat -ano -p tcp`, filtered to each owned port:

```text
default short (20 samples, before/during/after):
TCP 127.0.0.1:61100 127.0.0.1:61099 SYN_SENT 38444
default long (20 samples):
TCP 127.0.0.1:61101 127.0.0.1:61099 SYN_SENT 24208
exclusive short (19 samples):
TCP 127.0.0.1:61105 127.0.0.1:61104 SYN_SENT 9452
exclusive long (20 samples):
TCP 127.0.0.1:61114 127.0.0.1:61104 SYN_SENT 39140
stall (21 samples; representative during rows):
TCP 127.0.0.1:61118 0.0.0.0:0 LISTENING 12352
TCP 127.0.0.1:61118 127.0.0.1:61119 ESTABLISHED 12352
TCP 127.0.0.1:61119 127.0.0.1:61118 ESTABLISHED 32936
exact ready (4 samples; representative during rows):
TCP 127.0.0.1:61156 0.0.0.0:0 LISTENING 28212
TCP 127.0.0.1:61156 127.0.0.1:61157 ESTABLISHED 28212
TCP 127.0.0.1:61157 127.0.0.1:61156 ESTABLISHED 38736
```

For both bound endpoints, before/after rows were empty and all during rows were
only the recorded curl PID's SYN_SENT connection. No LISTENING, ESTABLISHED or
foreign-owner row was observed. A bound-but-not-listening server socket is not
itself a listener row; its custody is evidenced by the same live socket object
and recorded bind/options, rather than inferred from an empty TCP table.
No `Accept` was called on either bound socket. The exclusive control also
reproduced the exit-code difference without releasing/rebinding the port.

The stall owner accepted at outer elapsed 135.1875 ms and recorded its GET at
151.279 ms, then withheld all response bytes. The exact ready owner accepted
at 247.7102 ms and recorded its GET at 264.7005 ms before sending HTTP 200.
These polling timestamps are observer timestamps, distinct from curl's faster
TCP connect measurements.

## Essential curl trace transcript

These are unedited lines from the named `--trace-time --trace-ascii` files;
timestamps are local Europe/London. `bound-default-short-trace.txt:1`:

```text
00:52:32.305000 == Info:   Trying 127.0.0.1:61099...
00:52:34.319000 == Info: Connection timed out after 2014 milliseconds
00:52:34.319000 == Info: closing connection #0
```

`bound-default-long-trace.txt:1`:

```text
00:52:34.540000 == Info:   Trying 127.0.0.1:61099...
00:52:36.588000 == Info: connect to 127.0.0.1 port 61099 from 0.0.0.0 port 61101 failed: Connection refused
00:52:36.588000 == Info: Failed to connect to 127.0.0.1 port 61099 after 2041 ms: Could not connect to server
00:52:36.588000 == Info: closing connection #0
```

The exclusive pair repeats this distinction: short trace reports
`Connection timed out after 2013 milliseconds`; long trace reports
`failed: Connection refused` after `2052 ms`. Its measured total is 2.052217 s.

`stall-short-trace.txt:2` and `:11`:

```text
00:52:41.224000 == Info: Connected to 127.0.0.1 (127.0.0.1) port 61118
00:52:44.255000 == Info: Operation timed out after 3036 milliseconds with 0 bytes received
```

`exact-body/ready-short-trace.txt:11` and `:21`:

```text
00:53:07.344000 <= Recv header, 17 bytes (0x11)
0000: HTTP/1.1 200 OK
00:53:07.345000 <= Recv data, 44 bytes (0x2c)
0000: [{"channel":"telegram"},{"channel":"slack"}]
```

## Mechanism, classification and uncertainties

**Confirmed/reconstructed:** production's two-second connect deadline wins
against an eventual refusal around 2.05 seconds on this Windows host. This is
a pre-connection timeout, unlike the accepted-stall control's connected HTTP
timeout around three seconds. Source binds/holds the fixture socket at
`tests/Antiphon.Tests/Infrastructure/AmServiceDeployReadinessTests.cs:174`;
the delayed-ready case does not listen initially (`:175`). The generated curl
wrapper only waits for the listener acknowledgment after code 7 (`:207`), and
the C# transition only calls Listen for `curl-end|N|7` (`:252`). Measured code
28 cannot satisfy either gate; the listener therefore stays unstarted for all
attempts. Production retries every failed curl probe, then exits 44 on the fifth
failure (`scripts/deploy-am-service.ps1:371`, translated error `:374`).
The success assertion requires initial 7s (`AmServiceDeployReadinessTests.cs:100`)
and permanent-refusal case requires five 7s (`:54`). This establishes the test
failure mechanism without changing its assertions or running another test lane.

**Classification:** observed delayed reporting of connection refusal; no
accepted stall, early listener or reuse/foreign owner found for the held
non-listening sockets. The two-second command returning 28, same-socket longer
command returning 7, exclusive control and exact immediate HTTP success meet
the landed plan's I-1 exit gate. There is no measured reason to remove native
Windows coverage or alter production readiness behavior.

**Remaining uncertainties:** no packet capture established whether SYN retries
followed RSTs. `pktmon status` failed with “Cannot obtain current state” / “The
system cannot find the file specified.” The token was Medium integrity and
Administrators was deny-only; no tshark executable was available on PATH.
No capture was started and no packet-monitor filters/settings were changed.
The specific Windows TCP retry/RST mechanism remains an inference; transient
filtering/security-product contributions are not ruled out at packet level.
A port-scoped loopback capture showing SYN/RST exchanges would resolve that
uncertainty. This does not prevent confirmation of the measured timeout versus
refusal distinction or the code-7-only fixture failure. This run also does not
qualify repaired tests, other curl versions, or the original Review host's exact
packet path. The cygpath absence was measured, but resolver behavior remains
unimplemented and untested.

## Not done, noted

I-1 supports the existing plan's D-2 fixture allowance for Windows 7/28 at both gates; implementation and Code authorization remain for Plan, with production unchanged.

--- next stage ---
next: plan
handoff: I-1 confirmed delayed refusal exceeds curl's two-second connect deadline and both fixture gates require 7; the documented no-listener gate passed, packet-level SYN/RST remains inferred. Reconcile the existing D-2 choice and authorize the already folded method-scoped Code lane; preserve production behavior.
artifact: docs/superpowers/plans/2026-10-04-card-1049-windows-readiness-retry-plan.md
