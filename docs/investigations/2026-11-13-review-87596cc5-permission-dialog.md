# Review 87596cc5: unsafe-delete dialog outside the refusal matcher's viewport assumptions

Debug task: `56681199`, branch `feat/card-task-56681199`.
Observed on 2026-11-13, with the final seat/task read at 07:13:53Z.

## Outcome

The Review attempted prohibited variable-based recursive cleanup. Claude withheld
execution. Antiphon's special unsafe-delete refusal missed the modal because its
`Bash command` heading had scrolled out of the 30-row rendered screen. A captured
screen regression reproduces the refusal failure, and the shared matcher repair
passes the runner's guarded-input tests. The repair is not deployed.

The failed Review still holds a server2 seat. Its session is Running and
`working=true`, with no open task. The brief authorizes release only when idle;
that condition is not satisfied. No terminal input, release, kill, restart,
deployment, retry, queue change, or modification of the Review worktree occurred.
Releasing this still-mid-turn session needs a caller decision widening the
idle-only authority. That question was submitted while verification continued.

## Evidence and chronology

The authoritative server transcript was read through
`GET /api/sessions/2e9a8fdb-f4aa-47d6-b0ab-fae74c5ebdfa/transcript?since=0`.
The task detail, runner slots, pending messages, runner status, and server version
were read through the routes in [ops-http.md](../ops-http.md) and
[logs.md](../logs.md). Runner-local snapshot/session/capabilities GETs supplied
current rendered-screen and input-history diagnostics; they do not replace the
server transcript as historical evidence.

* Actual task creation: 05:21:19Z; dispatch: 05:23:35Z. The brief's approximate
  04:20Z dispatch is not supported by the task row.
* Transcript row 39, 05:29:27Z, reports checkpoint run
  `20261113-052927-2249` started, five rows, executor PID 12978. This Debug did
  not inspect, stop, or rerun that independent run and assigns it no pass credit.
* Final transcript row 40, 05:29:47.248Z, is Bash tool use
  `toolu_01GEQnTrz7GCJ2n9HVLMfSF7`. There is no corresponding ToolResult.
  The server's last-ingested transcript time is 05:29:48.261Z.
* The attempted command assigned the Review's scratchpad to `S`, then looped
  over seven mutant directories with `rm -rf $S/$m; cp -r $S/orig $S/$m` before
  editing copied `scripts/lib/rc-historical-stream.cs` files. This violated the
  Review's existing cleanup prohibition; the brief did not require that deletion.
* The current modal says `Dangerous rm operation on possibly-empty variable
  path: $S/$m`, offers `1. Yes` / `2. No`, and ends with
  `Esc to cancel · Tab to amend`. Execution of row 40 is unconfirmed. Do not
  approve or replay it.
* At 05:30:48Z the task recorded a ProviderInputWait opening Warning, classified
  `permission dialog`, output sequence 3066 and input revision 4. Therefore the
  input wait was detected before the brief's reported 05:52Z AppHost restart.
* At 06:59:53Z the original local-tool deadline failed the task after 90 minutes,
  explicitly preserving its session, source and pending queue.

The runner's current snapshot has 30 rendered rows, 25 nonblank rows, and no
`Bash command` heading. The exact warning and ordered choices remain visible.
Its dialog evidence is Blocked/ToolPermission, with `inputHistoryKnown=true`,
`adopted=false`, and `inputUncertain=false`. Runtime epoch
`79fb6a8c-2f9a-4e85-a429-a7f3ef691bfa`, output sequence 3066, input revision 4,
and accepted generation 05:23:35.333223Z agree with the pre-restart warning.
The session detail also reports an exact transcript bind and `adopted=false`.

The server reports build `ab7af12c699039270af7e22a5d7fd794035bc325`; server2
reports `56e96eabf698e9c1fcd462836ad32ed1fb259e83` and advertises
`claudeDialogGuardV1`. Both Git versions contain the faulty heading requirement.
These observations rule out adopted/uncertain input history for this miss.
Desktop/AppHost logs were not accessed; the reported restart's exact timing is
not independently verified here. Its timing cannot explain the earlier miss.

## Cause and repair

The generic `claudeDialogGuardV1` behavior was correct: a blocking modal refuses
automated task bodies and Enter. It does not grant general dialog dismissal.
The narrowly authorized unsafe-delete exception is separate: WatchdogService
should send exactly ASCII `2`, without Enter, and fail a still-open bound task
through `FailAutoRefusedUnsafeDeleteAsync`. See the
[dialog-guard owner](../session-runtime-invariants.md#claude-automated-input-dialog-guard-card-0680).

`ClaudeDialogActions.IsUnsafeDeleteApproval` required a visible `Bash command`
heading no more than twelve nonblank rows above the question. Even a taller
viewport retaining the heading would miss this command because the preview is
longer than that. WatchdogMatcher uses this predicate and withholds a numeric
answer when it fails. The runner uses the same predicate to authorize the typed
UnsafeDeleteNo intent, so a server-only workaround would still fail at the write
boundary. No adopted-session or Review tool-allowlist change is needed.

The repair anchors on the exact boxed dangerous-rm warning immediately before
the exact question, ordered choices and footer. It bounds the warning to twelve
rows, requires every warning row to retain its box prefix, and retains the
65,536-character / 512-row screen limits. Earlier command text cannot supply a
missing warning. The shared implementation covers both watchdog classification
and the runner's final conditional write. Only `2` is permitted; generation,
epoch, sequence, input revision, adoption and input-history checks remain in force.

The invariant is: an exact current unsafe-delete modal can consume one guarded
No byte even when its command heading has scrolled away; approval, Enter,
arbitrary bodies, altered/historical layouts and adopted input remain refused.

The Review brief already prohibited this cleanup. A replacement Review should
use fresh scratch directories for mutant copies, without a cleanup loop, and
retain that prohibition. Do not expand tool permissions or approve the old call.
The repair requires ordinary Review and activation on both server and runner
before claiming live recovery. No native-Claude intervention acceptance is
claimed by the in-memory runner test.

## Same-week search

Listed Failed tasks in project `d4ea7ae9-e769-474b-95b9-aa25fbc1303f`, including
specialist rows, with `since=2026-11-06T00:00:00Z`. The response contained 36
tasks. Read every task detail because the list projection omits failureReason,
then searched their failureReason fields for `permission dialog`. Only
`87596cc5` matched. No other failureReason matched unsafe cleanup / dangerous-rm
wording. This is a project-scoped retained failure-text census, not a claim that
no transient dialog or other-project occurrence happened.

## Seat disposition

At 07:13:53Z, `GET /api/session-runners/server2/slots` reports 4 occupied seats
out of declared capacity 10. Session `2e9a8fdb-f4aa-47d6-b0ab-fae74c5ebdfa`
is Running, `occupiesCapacity=true`, PID 10272, `openTaskId=null`, and
`orphan=false`. The task remains Failed; its session projection remains
Running / `working=true`. A separate project-scoped enumeration of Queued,
Dispatched, Working and Blocked tasks found no owner of that session.

The queue contains one Pending failure-supervision message with zero delivery
attempts, ID `a023b93d-46d7-4a49-868a-5d5873e47a67`. It was preserved.
Server2 reports draining since 06:53:36Z with redirect to server2-temp. This held
seat therefore still matters to the caller's drain.

The documented runner-slots release sends a process-tree stop, removes the
manifest, and audits the release. Calling it a release does not make it an idle
operation. The supplied authority specifically limits that path to an idle
session of a terminal task. No release was attempted against this Working
session. Before any later release, re-read its task, working state, pending
queue and openTaskId; a prior snapshot never establishes current eligibility.
After an authorized release, re-read slots to confirm the specific seat is free.

## Verification

Red-first test commit: `d461979acadb088d6cd85d260ca98e03bce67af9`.
Repair commit: `e158ce34de1aaecc9098cc064f568e5fa5d86079`.
Both were pushed before the corresponding run. The red witness tests the
unchanged base implementation; it is an assertion failure, not a build error.

* Red, filter `/*/*/ClaudeDialogInputGuardTests/C566_ScrolledUnsafeDeleteDialogOnlyAcceptsNo`:
  1 executed, 1 failed, 0 skipped. Expected `written`, actual `modal-blocked` at
  the exact No assertion. Test body 109 ms, reported run 813 ms; slot wait 60 s,
  combined build/test driver lease 141 s. This initial gated `dotnet run` used
  an implicit build; the wrapper explicitly noted that its maxcpucount cannot
  constrain an implicit build. Subsequent build and run were separated.
* Green build at the repair SHA, native default output, `UseAppHost=false`:
  0 errors, 176 warnings; MSBuild elapsed 82.65 s, lease 84 s, slot wait 30 s.
* Green, filter `/*/*/ClaudeDialogInputGuardTests/*`, same properties and
  `--no-build`: 4 passed, 0 failed, 0 skipped; reported run 1.109 s, lease 4 s.
  The new method covers the actual captured viewport plus rejection of ordinary
  input, approval, Enter, arbitrary text, changed footer/choices/warning,
  unrelated warning continuation, historical screen and adopted input.
* `scripts/check-evidence-diff.ps1` over dispatch base
  `cf17942491a7af951aac812da3eb18e2340a5ba7` through repair SHA:
  2 commits, 0 evidence entries, 0 violations.
* After `git fetch origin master`, `git merge-tree --write-tree origin/master HEAD`
  at the repair SHA returned clean tree `54316c47ede61a082dcb1c6070e9796a66b4878b`.

Selections are limited to the changed shared matcher and its guarded runner
consumer. No full assembly or live-provider canary was run. Tests use an
in-memory child and no database/container/real-Claude setup; fixture teardown
belongs to the existing test-owned fixture. No custom timeout was added. Raw
TRX, HTML, API captures and build logs remain outside Git under
`/tmp/task-56681199-evidence`; this document retains the essential provenance.
Those temporary files are local diagnostics, not durable qualification receipts.

The failed Review is still Failed and supplies no Clean/Full verdict. Its earlier
transcript describes large-TRX memory counterexamples; these were not independently
verified by this Debug. Preserve those findings and the outstanding Review work
when choosing recovery; passing the dialog regression grants no qualification
credit to CARD-1836.
