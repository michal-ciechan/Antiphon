# CARD-1006 A-2 capture receipt (server2 host lane)

Date: 2026-10-03. Capture window 16:09:03Z to 16:09:07Z on `server2`.
Executed by an orchestrator fork under the operator's chat approval of the host
lane ("Fine to do option 1 if you need"), after the read-only preflight and the
A-2 infeasibility receipt (`2026-10-03-card-1006-a2-custody-receipt.md`, branch
`feat/card-task-abc9f7d5`, start ref `b62e9d14`). Evidence only: no source,
test, fixture or plan edit.

## Verdict

**A-2 capture MET against D-2, with the caveats below for the caller.** One
bounded, zero-input SignIn capture ran in a disposable container created from
the exact image id, on an isolated bridge with enforced egress, with host swap
off and every writable path memory-backed. All host changes were reverted and
verified identical to the baseline. The three sanitized frames reproduce the
committed frames exactly except for the cwd line.

Caveats (none hidden):

1. **A-1 literal gate still open.** The plan's A-1 asks for a repository:tag AND
   an immutable RepoDigest. The image is local-only and has no RepoDigest. The
   capture used the exact image id
   `sha256:743279186cff4b4ca4507c6a64d7a139caced22dd2f3c757ced00048c9eba620`
   (tag `antiphon-server2/session-testing:4358939ecd85`), confirmed identical on
   the outer server2 Docker daemon (server 24.0.2). Whether the id satisfies
   A-1 is the caller's decision under the frozen plan; this receipt does not
   claim a digest.
2. **Real trust is still absent.** The credential-free window shows sign-in
   only, as in C-1/C-2. This capture adds a third live reproduction of the
   sign-in-first startup; it does not provide a trust frame.
3. **Two bounded deviations from D-2.2/D-2.4 wording.** (a) The helper script
   was supplied through a read-only bind mount of a task-owned directory that
   held only that script (no production state, credential, project, profile or
   Docker-socket mount). (b) The scratch tmpfs was mounted with `exec` because
   the runner executes a shadow copy of its pty-host from it; `/tmp` and
   `/dev/shm` stayed `noexec`. Both are stated here so the caller can accept or
   reject them.
4. **Swap was disabled host-wide for 12.9 s** (16:08:57.305Z to 16:09:10.243Z).
   Swap-used went from 432,360 KiB before to 0 after, because the paged-out
   data returned to RAM; swap is back on with the original file and size.
5. **Attempt history.** Three launches of the host script, one capture. Run 1
   aborted at container start (the entrypoint path to node was wrong), before
   any helper code ran. Run 2 passed the egress checks and started the owned
   runner but its launch request returned 500 (scratch tmpfs was `noexec`); no
   Grok process started, 243 ms, zero frames, zero egress. Run 3 is the capture.
   Each run created and removed its own network, rules and container.

## Seven receipt items

1. **Created paths and sizes.** All capture-created paths sit under the
   task-owned tmpfs `/scratch/run` (home, grok home, cwd, tmp, xdg dirs, runner
   logs and state, shadow-copied pty-host): 33 directories, 66 files,
   176,342,778 bytes. Nothing under `/tmp` and nothing newer than the ownership
   marker under `/dev/shm`. No disk-backed scratch, no Docker log (log driver
   `none`), no archive, no volume (volume count 29 before and after).
2. **Ordinary raw ANSI custody.** One ordinary raw log was written as the plan
   expected, `/scratch/run/runner/logs/<guid>.ansi.log`, 15,293 bytes, mode
   0644, on tmpfs `rw,nosuid,nodev` (scratch) with swap off for the whole life
   of the file. One pty-host log existed under `pty-hosts/logs`. Neither was
   opened, copied or exported; audit-off did not suppress the ANSI log, as
   documented. The `/proc/swaps` listing inside the container had only its
   header line. Writable-path filesystem types: scratch, `/tmp`, `/dev/shm` all
   tmpfs; root filesystem read-only overlay (write probe returned EROFS).
3. **Deletion verification.** The canonical root `/scratch/run` equalled its
   real path and the ownership marker existed before deletion; the root was
   removed by exact path. A marker find over `/scratch`, `/tmp` and `/dev/shm`
   then returned 0 entries newer than the marker. The container was started
   with `--rm`; afterwards 0 containers named `a2cap-capture` remained and the
   tmpfs went with it.
4. **Owned processes.** Container start: `docker-init` and the helper only.
   The runner started, the session was killed individually (`kill` returned 200,
   state `Exited` in 97 ms; no kill-all), the runner exited with code 0, and
   nothing lingered after the runner exit. Remaining processes before container
   exit: none. Seven distinct PIDs were sampled across the run. Container
   absence verified from the host.
5. **Network.** Policy, installed on the host and scoped to bridge
   `br-a2cap` (subnet 172.31.250.0/24, no published ports): chain `A2CAP`
   permits DNS to the pinned public resolver 1.1.1.1 (UDP and TCP 53) and public
   TCP 443, drops 10/8, 172.16/12, 192.168/16, 100.64/10 (tailnet range),
   169.254/16, 127/8 and everything else; host-directed traffic from the bridge
   dropped in INPUT; IPv6 forwarding from the bridge dropped and IPv6 disabled in
   the container (no global address). Checks run from the capture container's
   own namespace before launch, all as expected: resolve a name (pass); public
   443 (pass); public port 80, TCP 53 to another resolver, 10/8, 172.16/12,
   192.168/16, 100.64/10, 169.254/16, the bridge gateway port 22, and IPv6 443
   (all denied). Window: capture-begin 16:09:03.422Z to egress cut
   16:09:07.662Z (4.24 s; helper-measured 4,211 ms, ended early because the
   sanitized sign-in frame was stable twice). First established outbound
   connection at 1,334 ms after launch; samples show remote port 443 only, at
   most 2 concurrent. Counter delta over the window: UDP 53 to 1.1.1.1 12
   packets / 648 bytes; TCP 443 2,853 packets / 161,089 bytes; every drop rule
   0 (no forbidden attempt). The pre-window counters hold only the deliberate
   probes. An external supervisor cut egress with a drop rule at the end marker
   (and would have cut at 55 s and killed the container at 60 s). No packets or
   payloads were retained and no hostnames were inferred.
6. **Sanitized frame comparison.** Three distinct sanitized frames were seen:
   a blank frame (sha256 `a0bdb7e7...`, not retained), Connecting at 3,410 ms
   and OAuth approval at 3,684 ms after the launch request began. Retained in
   `2026-10-03-card-1006-a2-capture-frames.json` (30 rows each; maximum widths
   67 and 85):

   | Frame | Decoded UTF-8 SHA-256 | Compared with | Result |
   |---|---|---|---|
   | A2-connecting | `adc45f2b0e98f64829fa0f18c7ba5b63e4a79ef5b1340a2469275128121e2e46` | C1-connecting | rows 0 and 2-29 identical; row 1 differs |
   | A2-sign-in | `5c3041eb215bc899260973e93ff0d6840d01b4ac0337932e5cabd6b61ba59480` | C1-sign-in and C2-sign-in | rows 0 and 2-29 identical; row 1 differs |

   Row 1 is the CLI's spelling of the task cwd (`/scratch/run/cwd` here versus
   `/t/antiphon-card1006-.../C-n-cwd` before); nothing else differs, so there
   is no drift in anchors, geometry or the logo. In the approval frame the
   nine-cell code was replaced in memory before any retention by `<CODE-9> `
   at row 15, columns 56 to 64 (3 replacements across polls; the original value
   was never printed, stored or logged). Privacy scan over both frames and this
   receipt returned zero matches; a leftover code-shaped string scan returned
   none. No trust prompt, updater notice or ready dashboard appeared in the
   4.2 s window. After the window the installed CLI was unchanged: SHA-256
   `9ce03ed23e16ea01072b4496263d6213a27899e1e3e107f008d36edf82e70407`, version
   `grok 1.0.41 (4220f3b224a6)`; the updater setting was not touched.
7. **Safety.** Input calls 0, input bytes 0. No login, no `grok login`, no
   credential or auth-file read, no model turn, no browser approval, no
   following of any sign-in address or code, no trust answer. Fresh `GROK_HOME`,
   `HOME`, XDG dirs and cwd on tmpfs; the image's `/root/.grok` directory exists
   but was not readable to the container user (EACCES) and was never listed or
   read. The container ran as uid/gid 1654 with all capabilities dropped
   (effective set 0), `no-new-privileges`, `--read-only`, `--init`, memory
   2 GiB, 512 pids. Runner and CLI received only an explicit nonsecret
   environment (PATH, the scratch paths, `TERM=xterm-256color`,
   `BROWSER=/bin/false`, `ANTIPHON_PTY_AUDIT=0`); the runner ran phone-home,
   Herdr, host statistics and the CPU watchdog disabled, with a random private
   loopback port. Image-level configuration audit (names only): the image
   environment names are PATH, APP_UID, ASPNETCORE_*_PORTS, DOTNET_*,
   PhoneHome__Enabled and ASPNETCORE_URLS, with no Grok or MCP name; no
   `/etc/grok*` or `/etc/xdg/grok*` path exists. Absence of an MCP surface is
   not proof of full isolation; CARD-0857 still owns that.

## Host changes and restore verification

Changes, each reverted: `swapoff /swap.img` and `swapon /swap.img`; Docker
network `a2cap` (bridge `br-a2cap`) created and removed; iptables chain `A2CAP`
plus three attach rules (DOCKER-USER, INPUT, ip6tables FORWARD) scoped by
`-i br-a2cap` and deleted. No existing rule, policy, daemon setting or other
container was touched; Docker was not restarted.

After the capture (and again after a final check): `iptables -S` 109 lines
before and after, `ip6tables -S` 13 before and after, `iptables -t nat -S` 58
before and after, each byte-identical to the baseline; no `br-a2cap` link, no
`a2cap` network, no references to the chain; running containers 33 before and
after; volumes 29 before and after; `/swap.img` 542,716 KiB active, used 0. The
host scratch directory holding only the helper script and baseline listings was
removed. The first `swapoff` (run 1) took about 2 min 41 s while swap held
data; the capture run's swap-off was instantaneous.

## What this does and does not close

Closes the A-2 isolation and custody receipt for the sign-in capture and gives a
live, drift-free reproduction of the three committed frames. It does not close
A-1's digest wording (caller decision), does not provide real trust or updater
frames, and does not change any plan, fixture or checkpoint roster. Host
swap-off and firewall edits were operator-approved in chat for this single
capture only.

--- next stage ---
next: code
handoff: A-2 MET with the caveats above (A-1 digest wording is the caller's call; deviations 3a/3b; host-wide swap-off 12.9 s). Admission still needs A-3 and A-4 and the plan's trust-evidence decision (no real trust frame exists). Reuse these frames only as a third live reproduction; the committed investigation frames remain the fixtures.
artifact: docs/investigations/2026-10-03-card-1006-a2-capture-receipt.md

## Deadline enforcement (addendum, answers the Code admission question on D-2.6)

**D-2.6 as written is NOT satisfied.** D-2.6 requires an external supervisor
that cuts egress and stops the process 15 seconds after CLI launch even if
polling stalls. What I built and ran was weaker; the Verdict section above
should be read as qualified by this addendum (A-2 is not met for D-2.6; the
other D-2 custody and isolation points stand as recorded). Nothing below was
re-measured: it is read from the scripts and the three run logs.

1. **What enforced which deadline.**
   - *In-process 15 s limit (not external).* The helper inside the container
     loops `while (Date.now() - launchT < 15000)` (`cap.js`). `launchT` is taken
     immediately before the launch request is sent to the owned runner, so the
     origin is the launch request (the CLI spawns inside that request; run 3's
     request took 1,043 ms), not the CLI process spawn and not the first output.
   - *Egress cut on the helper's end marker (external action, helper-triggered).*
     The host script reads the container's attached output and, when the helper
     prints `CAPTURE-END`, inserts a drop-all rule at the top of chain `A2CAP`.
     In run 3 this cut egress at 16:09:07.662Z, 4.24 s after `CAPTURE-BEGIN`
     (16:09:03.422Z). That cut depends on the helper reaching its end marker.
   - *Independent external backstop, 55 s / 60 s.* A host-side subshell,
     started just before `docker run`, runs `sleep 55`, inserts the drop-all
     rule, runs `sleep 5`, then `docker kill a2cap-capture`. Its origin is the
     host's container launch (16:08:57.311Z in run 3), not CLI launch.
2. **What 55 and 60 are.** Fixed host sleeps of 55 s (egress cut) and 60 s
   (container kill) from the supervisor's start. In run 3 the CLI launch
   request began 6.11 s after container launch, so the backstop would have cut
   egress about 48.9 s after the launch request and killed the container about
   53.9 s after it. They were chosen as slack over startup, the 15 s window and
   5 s cleanup; they are not a 15 s-from-launch deadline.
3. **Would a stalled capture (no output) have been stopped within 15 s of CLI
   launch?** Only conditionally, and the evidence is the code, not a run.
   - Grok silent but the helper healthy: the helper loop exits at 15 s from the
     launch request (worst case roughly 19.8 s, because an in-flight snapshot
     poll can wait 1.5 s, a connection sample 3 s and the loop sleeps 0.25 s),
     prints `CAPTURE-END`, and the host then cuts egress and the helper kills the
     session. This path was never exercised: run 3 ended by `stable-twice`, run 2
     by a failed launch, run 1 before the helper started. No run ended by the
     15 s deadline.
   - The helper itself stalled (event loop blocked, or any hang that outlasts its
     own request timeouts, with no end marker): nothing acts at 15 s. The first
     independent stop is the 55 s egress cut (about 48.9 s after the launch
     request) and the 60 s container kill. The log shows the backstop never
     fired in any run (no `HOST watchdog-cut` line in the three logs; the
     `Terminated` lines are the subshell being stopped after a normal exit).
4. **Plain statement.** The external, independent, 15-second-from-CLI-launch
   deadline that D-2.6 describes was not implemented. The earlier sentence in
   item 5 ("An external supervisor cut egress with a drop rule at the end
   marker (and would have cut at 55 s and killed the container at 60 s)")
   conflated the helper-triggered cut with an independent timer; read it with
   this addendum. Meeting D-2.6 would need a host-side timer started at the
   helper's `CAPTURE-BEGIN` marker (or at the CLI spawn event) that cuts egress
   and kills the named container at begin + 15 s regardless of helper state,
   tested with a deliberately stalled launch; that is a new capture and host
   changes, which were not done here.

## D-2.6 re-run (one stalled-launch test, then one real capture)

Run on the operator's approval of the host lane, under the same hard rules as the
first run (same bounded host changes only, each reverted; zero input; abort and
restore on anything unexpected). Times are UTC on 2026-10-03.

**D-2.6 verdict: MET for a stalled capture after CLI launch**, with the
limits listed under "Limits" below. The external timer is independent of the
helper, was measured, and fired inside the 15 s plus or minus 1 s tolerance.

**What changed.** The host script now starts an external timer when it reads the
container's `CAPTURE-BEGIN` line (the helper prints it immediately before the
launch request, so the CLI spawns up to about 1.5 s after the marker; the timer
therefore runs slightly ahead of CLI launch, never behind it). At begin plus
15 s the timer inserts a drop-all rule at the top of chain `A2CAP` and then runs
`docker kill` on the named container, whatever the helper is doing. On
`CAPTURE-END` the host cancels that timer, cuts egress at once and caps the
awaited cleanup with a 5 s container kill (D-2.6's awaited-cleanup allowance).
The 55 s egress cut and 60 s container kill from the first run stayed as the
second layer.

**Stalled-launch test (before the real capture).** Same exact image id, same
`a2cap` bridge and chain, swap still on (no capture data involved). The
container ran `echo CAPTURE-BEGIN; exec sleep 600`, a harmless command that
prints the marker and then nothing:

| Event | Time | Offset from marker |
|---|---|---|
| container launch | 16:24:40.017Z | |
| begin marker seen by the host | 16:24:41.108Z | 0 |
| timer egress cut (drop-all inserted) | 16:24:56.153Z | +15.04 s |
| `docker kill` returned | 16:24:57.005Z | +15.90 s |
| `docker run` returned rc 137 | 16:24:57.100Z | +15.99 s |

Result: PASSED. The cut landed at 15.04 s; the kill signal was sent right after
it and `docker kill` itself took about 0.85 s to return, so the container was
gone by +15.99 s. After the fire the top rule of `A2CAP` was the inserted
`-A A2CAP -j DROP`, 0 stalled containers remained, and removing that one rule
restored the chain to its original ten rules (byte-compared) before the real
capture started. The test used a surrogate command, not Grok; the timer does not
depend on what the container runs.

**Real capture (zero input, 120x30).** Marker seen 16:25:04.796Z; the launch
request took 1,455 ms; the sanitized sign-in frame was stable twice and the
helper ended its window at 4,434 ms. The host cancelled the timer at
16:25:09.265Z (+4.47 s) and cut egress on the end marker at 16:25:09.343Z
(+4.55 s). The timer never fired, the 5 s cleanup cap never fired and the 55/60 s
backstop never fired (no fire line in the log). The owned session was killed
individually (200, `Exited` in 183 ms), the runner exited 0 and nothing
lingered. All 11 pre-launch egress checks were as expected. Counter delta over
the window: UDP 53 to 1.1.1.1 12 packets / 648 bytes; TCP 443 2,335 packets /
134,730 bytes; every drop rule 0. The sanitized frames are byte-identical to the
first run's (SHA-256 `adc45f2b...` and `5c3041eb...`, the same as in the frames
file) and again differ from the committed C1/C2 frames only in row 1, the cwd
line; the nine-cell code was replaced with `<CODE-9> ` at row 15, columns 56 to
64 (3 replacements on the wire), privacy scan clean, no leftover code-shaped
string. One ordinary ANSI log existed on tmpfs (13,919 bytes, never read); 66
files and 176,340,954 bytes were created under `/scratch/run`, none under `/tmp`;
the marker find after deleting the owned root returned 0 entries. Input calls 0,
input bytes 0; no login, auth read, model turn, browser step, or printed code or
sign-in address.

**Restore verification (final).** Swap was off from 16:24:57.286Z to
16:25:12.796Z (15.5 s); `/swap.img` is active again at 542,716 KiB with SwapFree
equal to SwapTotal. `iptables -S` 109 lines, `ip6tables -S` 13 and
`iptables -t nat -S` 58, each byte-identical before and after (the baseline was
taken inside this run, before any change). No `br-a2cap` link, no `a2cap`
network, no references to the chain in either table, 0 containers named `a2cap`,
running containers 33 before and after, volumes 29 before and after. The host
scratch directory (helper script and baseline listings only) was removed; no
`/tmp/a2cap.*` directory remains. The container's ownership marker and scratch
root were deleted by exact path before it exited. The earlier first-run
restore (and its final re-check) also held.

**Exec on tmpfs.** `exec` on the scratch tmpfs is required in the tested
layout, and it is the only relaxation used: `/tmp` and `/dev/shm` stayed
`noexec` in the successful runs. Evidence is a one-variable change between two
runs: with `/scratch` `noexec` the launch request returned 500 (no Grok process
started); with `exec` on `/scratch` and nothing else changed it returned 201 in
two runs. The 500 body was not recorded in that failing run, so the cause is
inferred from that single change, not read from an error message. The runner
executes its shadow-copied pty-host from `<log path>/pty-hosts/bin`; a narrower
exec-only mount for just that directory was not tested, so `exec` on the whole
scratch tmpfs is the minimum I have proven, not a proven global minimum.

**Limits (unchanged by this run).**
1. The timer starts at the `CAPTURE-BEGIN` marker. A helper that hangs before
   printing it is covered only by the 55 s / 60 s backstop measured from
   container launch, which the first-run addendum already records.
2. The real capture ended on `stable-twice`, so the timer was exercised only by
   the stalled-launch surrogate, not by a stalled real Grok.
3. `docker kill` took about 0.85 s to return in the test; the egress cut
   itself landed at 15.04 s.
4. A-1's RepoDigest wording, the absent real trust frame and the other caveats
   in the original Verdict are unchanged.

--- next stage ---
next: code
handoff: D-2.6 met for a post-launch stall (external begin+15 s timer, stalled-launch test 15.04 s cut / 15.90 s kill return, real capture clean, host restored byte-identical). Remaining caveats: pre-marker hang covered only by the 55/60 s backstop, A-1 digest wording, no real trust frame, exec-on-scratch proven minimal only at mount granularity.
artifact: docs/investigations/2026-10-03-card-1006-a2-capture-receipt.md
