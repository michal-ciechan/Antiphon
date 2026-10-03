# CARD-1006 A-2 custody and cleanup receipt

Date: 2026-10-03, prerequisite inspection completed at 14:22:03 UTC.
Debug task: `abc9f7d5`. Assigned branch: `feat/card-task-abc9f7d5`.
Start ref: `5f214b0c1daef4d6d7fbbbfbebd14deb83636639`.

**A-2 UNMET: capture infeasible under the frozen isolation procedure.**
No interactive Grok startup was launched. The supplied running-container
identity and CLI version match this session, but its accessible nested Docker
daemon cannot launch the supplied image. Persistent host swap is active and
the available tmpfs has no verified protection against spill to that swap.
Restricted capture egress and image-level configuration isolation have not
been established. This is a prerequisite failure, not a missing screen, and
does not discharge Code admission.

Authority: [the frozen plan](../superpowers/plans/2026-10-03-card-1006-grok-linux-signin-trust-plan.md),
D-2 and its Code admission and landing freeze. D-2.2 says:
"If the chosen image cannot run this way, A-2 fails; no in-runner fallback."
D-2.4 requires that tmpfs cannot spill to persistent swap and says
"otherwise stop." The dispatch allows an evidence-backed infeasibility report;
it supplies no verified alternative to these custody requirements.

## Identity and prerequisite evidence

The caller supplied host `server2`, container `5d9e09695ab3`, name
`antiphon-runner-session-runner-1`, image
`antiphon-server2/session-testing:4358939ecd85`, image ID
`sha256:743279186cff4b4ca4507c6a64d7a139caced22dd2f3c757ced00048c9eba620`,
created `2026-10-02T20:11:00Z`, and no registry digest because it was built
locally. The image tag, image ID and creation time are supplied provenance,
not independently inspected image metadata.

Measured inside this task's own runner session:

```text
hostname: 5d9e09695ab3
container suffix shared by every /proc/self/cgroup controller:
/docker/5d9e09695ab3f777b56fd87ee9c66adcd476045d51d634a03e2d79ce94d8cdb3
grok --version: grok 1.0.41 (4220f3b224a6) [stable]
CLI SHA-256:
9ce03ed23e16ea01072b4496263d6213a27899e1e3e107f008d36edf82e70407
/app/Antiphon.SessionRunner.dll SHA-256:
d58a2dbe3eb58e5eef2d5e41cceb4151e53104652728f784de200e4a1658b781
```

There is no measured container-ID or CLI-version difference. Container identity
does not independently prove the supplied image ID. Read-only Docker metadata
queries through the available socket produced:

```text
docker version --format 'server={{.Server.Version}}'
server=27.5.1
docker image inspect --format '{{.Id}}' antiphon-server2/session-testing:4358939ecd85
Error response from daemon: No such image: antiphon-server2/session-testing:4358939ecd85
docker image inspect --format '{{.Id}}' sha256:743279186cff4b4ca4507c6a64d7a139caced22dd2f3c757ced00048c9eba620
Error response from daemon: No such image: sha256:743279186cff4b4ca4507c6a64d7a139caced22dd2f3c757ced00048c9eba620
docker container inspect --format '{{.Id}}' 5d9e09695ab3
Error response from daemon: No such container: 5d9e09695ab3
```

No image was pulled, built, substituted or upgraded. No external executor was
given snapshot access. The accessible daemon is not a view of the outer running
container. No disposable capture container or bridge was created.

```text
findmnt -T /dev/shm -o TARGET,FSTYPE,OPTIONS
/dev/shm tmpfs rw,nosuid,nodev,noexec,relatime,size=65536k
findmnt -T /tmp -o TARGET,FSTYPE,OPTIONS
/tmp ext4 rw,relatime,data=ordered
cat /proc/swaps
Filename     Type  Size   Used   Priority
/swap.img    file  542716 432360 -2
cat /proc/sys/vm/swappiness
60
uname -r
4.15.0-213-generic
/proc/self/status effective capabilities:
CapEff: 0000000000000000
```

The reported tmpfs options do not include a no-swap guarantee. Active swap and
these mount observations do not prove that any particular page spilled; they
do establish that the required prevention has not been verified. No host swap,
mount, firewall or production-network policy was changed. Egress enforcement
was not tested because the exact disposable-image prerequisite already failed.
Image-level MCP/configuration isolation remains unverified; real provider
configuration and credential directories were not inspected.

## Seven requested receipt items

1. **Created paths and sizes.** Interactive capture-created paths: none, because
   launch was refused during prerequisite inspection. No capture marker,
   temporary GROK_HOME, fresh cwd, runner state, ANSI log or audit root was
   created. This Markdown receipt is the only intentionally created evidence
   file. A whole-filesystem marker scan was not performed; this is not a claim
   that other sessions created no files or that a capture inventory passed.

2. **Ordinary raw ANSI custody.** The server2 compose declaration selects
   `SessionRunner__SessionLogPath=/state/session-runner`, and the snapshot/PTY
   machinery writes `/state/session-runner/<sessionId:N>.ansi.log`.
   Metadata-only `stat` measured `/state/session-runner` as owner `app:app`,
   UID/GID `1654:1654`, directory mode `0755`. A metadata-only enumeration
   found 879 existing ordinary ANSI files, all UID/GID `1654:1654`, mode `0644`;
   their names and contents were not retained. These are existing runner files,
   not files from this task. PTY diagnostic logs resolve to
   `/tmp/antiphon-pty-hosts/logs`, owner `app:app`, UID/GID `1654:1654`, mode
   `0755`, separately from the ordinary ANSI files.

   `SessionRunnerRuntime.cs:2266` constructs the non-null ANSI path and line
   2321 sends it to the host. `HostSession.cs:356-360` appends every nonempty
   raw output chunk when the path is non-null, with no audit or transcript
   enablement guard. The same statements were inspected with `git show` at
   installed build `4358939ecd85d6e7ff0941f970879499cb930e3d`. Therefore
   disabling audit and transcripts does not suppress ordinary ANSI writes.
   The format can retain control sequences and prior raw output beyond the
   currently rendered screen. Actual extra content in existing logs is
   unmeasured: none was opened. This task produced no interactive capture log.

3. **Deletion verification.** There was no capture scratch root or temporary
   GROK_HOME to delete. No second marker-based find was performed. Zero-left
   cleanup for an executed capture is **not established** and must not be
   inferred from refusing to launch. No capture container, bridge or rules
   existed to remove. No raw capture was archived or attached.

4. **Owned processes.** Interactive capture runner/PTY/Grok PID set: empty.
   The foreground version-only Grok command exited successfully; its PID was
   not retained. No startup process was launched, killed or checked with a
   post-capture `ps`. An exited/dead-process receipt for a real capture is
   **not established**. No existing session was stopped or modified.

5. **Network.** Interactive capture network window: zero seconds, because no
   startup capture was launched. No capture socket sampling or TCP delta was
   taken, and no claim about version-query network behavior is made. Historical
   startup network observations remain those of the earlier investigation;
   they are not observations from this task. No sign-in address was followed.

6. **Sanitized frame comparison.** There are no new frames to compare; live
   reproduction and drift remain unmeasured. A read-only Node artifact check
   validated the committed JSON's exact three keys, decoded hashes, LF row
   counts, maximum widths, JSON round trip and the nine-cell `<CODE-9> `
   replacement at row 15, columns 56 through 64 in both approval frames:

   | Frame | Rows | Maximum width | Decoded UTF-8 SHA-256 |
   |---|---:|---:|---|
   | C1-connecting | 30 | 67 | `beb362aeb8dfa6bbb1a2c95eff5e6fcc49527d0acf67c941653d40172fb2b277` |
   | C1-sign-in | 30 | 85 | `e70a9a5d6630171b164c3bfa3a660e04a590514a0635c4044dd82db25dfb6d06` |
   | C2-sign-in | 30 | 85 | `9a5793ae8bd3e74c1ef60c9125b516817ae59133b276f6b8aff2f43850707f52` |

   All three existing artifacts passed; their historical hashes match the
   frozen plan. Both prior investigation files remain unchanged from the start
   ref. This validates existing artifacts only, not capture admission.

7. **Safety.** No login, credential or auth-file read, model turn, browser
   approval, device-code following, trust answer or interactive input occurred.
   Input calls and input bytes: zero. Parent environment variables were never
   printed. Provider homes and real home `.grok` contents were never opened.
   No raw terminal history or existing log content was read or exported.

## Admission consequence and verification

A-1 supplied identity matches the measured running-container suffix and CLI
version. Its absent registry digest remains a difference from the literal
frozen-plan gate, for the caller to resolve; this difference alone was not used
as the reason to refuse capture. A-2 remains unmet independently because the
exact disposable image is unavailable and spill-proof custody is unverified.
A-3 remains the existing TestDesign freeze; A-4 is the Code owner's actual
five-row importer requirement. This task closes none of those external gates.
All P-01 through P-19 definitions remain unchanged, including synthetic trust
and updater coverage. No new real trust/update claim is made.

To obtain a successful A-2 receipt, the caller must provide a lane that can
launch this exact image as the D-2 disposable container, establish restricted
egress, verify tmpfs cannot spill to persistent swap, and verify image-level
configuration isolation. Then commission the single bounded 120x30,
15-second, zero-input capture and its complete inventory/cleanup checks. No
repeat or alternative capture was attempted here.

Verification: three existing frame artifacts passed integrity/privacy checks;
receipt privacy scan passed for all five required patterns with zero matches;
`git diff --check` passed. No build or test suite was run. This receipt is an
infeasibility finding, not a successful capture or runtime qualification.

## Minimal compliant lane options, appended at caller request

Advisory only: no capture, image transfer, mount, swap change or network probe
was performed for this addendum. Both options still require all D-2 safeguards.

| Lane | Image location and minimum preparation | Approval owner |
|---|---|---|
| Server2 host lane | The **outer server2 host Docker daemon** is the expected image holder, based on the supplied deployment identity; this session inspected only the nested daemon. The host owner must confirm the standing container's image ID with a narrowly formatted inspection, then inspect the supplied tag and immutable ID on that same daemon. Run only a separately commissioned disposable container. | Caller/orchestrator commissions the lane; the human server2 host operator approves privileged host preparation and firewall/swap changes outside the documented rollout autonomy. No standing-runner replacement is needed. |
| Isolated host lane | The host operator exports the pristine image from server2's outer daemon and imports it into an approved isolated host's Docker daemon, checking the same immutable image ID before and after. Transfer image layers only; never export a running container, provider home, state volume or repository snapshot. A swap-free isolated host avoids changing production swap policy. The current nested daemon is an alternative only after the same image is supplied and its shared host's swap protection is verified. | Caller/orchestrator approves the destination and commissions the lane; source and destination host operators approve image transfer and privileged preparation. This task does not authorize a new external executor. |

For either lane, **the caller owning A-1 and the frozen-plan acceptance must
resolve the supplied local image ID versus required registry digest before
claiming admission**. A matching image ID alone does not waive that gate.

| Requirement | Minimum evidence before any future capture | Approval owner |
|---|---|---|
| Tmpfs protected from persistent swap | Prefer an isolated host with swap disabled: verify host `/proc/swaps` is empty before launch and remains empty through deletion. Alternatively use a kernel that demonstrably supports newly mounted `tmpfs,noswap`; verify effective mount options in the capture namespace for scratch and `/tmp`, including every writable log/state/home path. On current server2, host-wide swap disablement requires a RAM-capacity review and operator-approved maintenance; restore prior policy only after deleting scratch. Neither `vm.swappiness=0` nor a container memory/swap limit proves tmpfs protection. | Human operator of the selected host approves swap/mount policy or host provisioning. Caller reviews the resulting custody evidence. No host/kernel upgrade or swap change is authorized here. |
| Restricted egress | The host/network owner installs capture-bridge-specific default-deny enforcement before launch: permit DNS only through the configured resolver and public TCP 443, deny host/production/private/management destinations and other traffic, and disable IPv6 or apply equivalent IPv6 rules. Verify the actual firewall backend, rule order, Docker DNS forwarding and host-directed traffic paths; with iptables, forwarding restrictions must run before Docker accepts traffic. Retain sanitized rules/counters and approved controlled allow/deny checks from the same network namespace, covering DNS, public 443, forbidden ports and private destinations. Use controlled targets rather than live production services; retain no packets or payloads. Socket observations alone are insufficient. | Human host/network operator approves and installs the scoped policy and controlled test targets; the commissioned Debug helper verifies it, and the caller reviews admission evidence. This addendum runs no checks. |

Technical basis: [Linux tmpfs documentation](//docs.kernel.org/filesystems/tmpfs.html)
documents ordinary tmpfs swapping and the `noswap` mount option;
[Docker's iptables documentation](//docs.docker.com/engine/network/firewall-iptables/)
places user forwarding policy before Docker acceptance in `DOCKER-USER`.
Host approval assignments follow
[the repository operational-autonomy policy](../orchestration-loop.md#orchestrator-operational-autonomy-restart-rollout);
the caller owns A-1 acceptance under the frozen plan. These options have not
been provisioned or verified and do not change the A-2 UNMET verdict.

--- next stage ---
next: code
handoff: A-2 UNMET. Keep Code admission closed until the caller supplies a compliant capture lane and a successful custody/cleanup receipt; preserve the A-1/A-3/A-4 gates and frozen probe roster.
artifact: docs/investigations/2026-10-03-card-1006-a2-custody-receipt.md
