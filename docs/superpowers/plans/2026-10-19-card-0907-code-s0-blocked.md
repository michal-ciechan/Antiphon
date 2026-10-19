# CARD-0907: Code stopped at the S0 capture gate

Date: 2026-10-19. Outcome: **Blocked; return to Plan.** No parser, test, or
fixture was changed, and no model turn was submitted. This is a preparation
record, not completed S0 or ordinary verification evidence.

Original Code task / landing owner: `e840b26d-1add-46e4-8174-7f24e458d551`.
Assigned branch: `feat/card-task-e840b26d`.
Worktree: `/work/worktrees/task-e840b26d`.
Dispatch base: `d5dec9555bd726ad11bb30073c1733d6b3e7bb0e`.
Target remains master only; restart: **none**.

## Inputs inspected

- Plan: `docs/superpowers/plans/2026-10-19-card-0907-codex-0160-parser-plan.md`
  at `08bdb7087f3903fcc7f2980ff520a3a4997c250a`, fetched from
  `feat/card-task-046b1e67`.
- TestDesign: `docs/superpowers/plans/2026-10-19-card-0907-codex-0160-test-design.md`
  at `fa25211a188944e5acd1d41628ab1806eb686fda`, fetched from
  `feat/card-task-9d7ac7b5`.
- Prior parser investigation at
  `c5543c29839e9c9d6d19e012daedb927551a78af`.

These artifacts were read from Git without merging their branches. Their
checkpoint manifest, parser decisions and positive-control inventory are unchanged.

## Blocking observations

The brief requires the capture through `docker exec -u 1654:1654` with
`HOME=/home/app` and `CODEX_HOME=/state/codex` in the server2 runner container.
This task shell reports uid/gid 1654. Its local Docker daemon returns no running
containers, so it does not expose the parent session-runner container for that
command. The documented host route was tried with bounded, noninteractive SSH.
The first attempt failed host-key verification; after recording the previously
unknown host key using `StrictHostKeyChecking=accept-new`, authentication failed
with `Permission denied (publickey)` (exit 255). No remote command executed.
SSH keys or other credentials were not inspected or modified.

The brief also says "do not print or copy auth.json or any file content except
the captured screen text", whereas S0 requires a fresh scratch CODEX_HOME with
an owner-only credential copy and prohibits using the service home/history.
The explicit brief's no-copy restriction was honored. No credential file was
read, copied or linked; no alternative service-home launch was substituted.
The sanctioned capture procedure needs a consistent credential/home instruction
before it can run.

Fresh platform reads at approximately 14:54 UTC succeeded:
`GET /api/runner-defaults` returned revision 2, global runner `server2`, no kind
overrides. `GET /api/session-runners` reported server2 eligible/accepting on Linux,
with version 0.160.0 checked at 14:53:02 UTC and advisory `stderr_output`;
desktop was eligible on Windows and the temporary runner unavailable.
These catalogue observations are neither a host-access grant nor an S0 executable
fingerprint. No fleet address was embedded in a dispatch and no task was delegated.

## Binding and verification state

| Binding | Actual outcome |
|---|---|
| B-1 working | Unbound: capture not launched; no claim about the observed status grammar. |
| B-2 queue | Unbound: no running frame; absence of a distinct queue footer was not observed or inferred. |
| B-3 receipt/rollout | Unbound: zero submitted turns, no native prompt/reply/usage receipt. |
| B-4 modals | Unbound: no current trust/back, signed-out or update scene captured. |
| B-5 execution | Unbound: no new test methods or mutation statement sites. |

| Ordinary ID | Actual outcome |
|---|---|
| V-1 | Not run: captured fixture fidelity requires S0 inputs. |
| V-2 | Not run: current working grammar remains unbound. |
| V-3 | Not run: current running/done pair absent. |
| V-4 | Not run: independent queue witness absent. |
| V-5 | Not run: current startup fixtures absent. |
| V-6 | Not run: modal compatibility slice not started. |
| V-7 | Not run: current native rollout absent. |
| V-8 | Not run: current submit-consumer fixture absent. |
| V-9 | Not run: real-queue/recovery suite not implemented. |
| R-1 | Not run: working/lifecycle slice not started. |
| R-2 | Not run: readiness/modal slice not started. |
| R-3 | Not run: normalizer slice not started. |
| R-4 | Not run: submit slice not started. |
| R-5 | Not run: delivery slice not started. |

CP-1, CP-2, CP-3, CP-4, CP-5, CP-6, CP-7, CP-8 and CP-9: **not run**.
Tests executed: 0. Builds: 0. `slot=not-requested waited=0s`.
No CHECKPOINT line, TRX, source-clean certificate or build provenance exists.
No unlisted build/test driver or whole-lane run occurred. The brief explicitly
selects narrow checkpoint groups despite its generic Final profile; that scope
needs no execution while the prerequisite gate is blocked.

Every PC-1 through PC-73, including every case/variant, remains **pending for
SourceLanding Mutation**, 0/73 executed. Payload-dependent controls remain
binding required; reachability remains unproven. Q-1 through Q-7, including the
Q-7 delivery-failure recovery seam, remain unbound/unexecuted. This task did not
establish that Q-7 is impossible; S0 blocked before that assessment.

## Acceptance and handoff

Keep all original acceptance gaps open: current working/done/native prompt,
independent queue footer, current Linux ready/trust/back/signed-out/update,
interactive originator/usage, complete queue/caller receipts and recovery.
Live quota refusal, compaction/continue, post-login, uncaptured MCP scenes,
unseen status spellings/geometries, Windows qualification and post-deployment
Plan canary remain unobserved. Existing legacy evidence is not promoted.

Plan must provide an executable uid-1654 capture path and reconcile the
no-credential-copy brief with the scratch-home requirement. It may explicitly
qualify an in-container invocation if that is intended; this task did not assume
equivalence to the required host-side docker exec. Then recommission S0 with the
same one-turn budget and cleanup bounds. The turn budget remains unused.
Bind B-1/B-2/B-3 and V-1 before parser edits, and independently assess Q-7.
No timeout, assertion or production policy was loosened.
