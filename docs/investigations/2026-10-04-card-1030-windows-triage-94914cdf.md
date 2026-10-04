# CARD-1030 Windows repair, task 94914cdf

Original Code task / landing owner: `7ee266d3-3df9-406a-823e-3716cb1593d3`.
Repair branch: `feat/card-task-94914cdf`.
Worktree: `C:\Antiphon\worktrees\card-task-94914cdf`.
Task base: `9f1cd6d398f572f179c9983b78d52e987b9d6663`.
Pre-CARD-1030 base B: `a4d2d4b851fa131763f4408ee3f1f70e1b387882`.
Plan: `docs/superpowers/plans/2026-10-04-card-1030-c1008-windows-host-fixture-plan.md`.
Generated final evidence: `.antiphon/task-94914cdf.md` and `.antiphon/checkpoints/`.

The explicit repair brief authorizes native Windows CP-4 through CP-6 only,
base replay of the seven reported failures, and no whole Unit. This is the
task-specific exception to its appended generic Final profile. Earlier Linux
106/106 and Unit 4,074 evidence remains historical, not rerun at this repair SHA.

## Classification

Three existing Remote methods reproduce `unknown switch b` at B:
`C1008_Recycle_audits_work_as_1654`,
`C1008_Recycle_refuses_unpublished_and_dirty_work`, and
`C1008_Recycle_refuses_uninspectable_git`. Base exits 128 with additional CRLF
and native-path errors; prior tip exits 129 on the same unsupported init flag.
This is an inherited Git setup limitation within the card's repair scope.

`C1008_Recycle_refuses_references_and_unknown_census` fails at B on the missing
held sentinel, before reaching the later cleanup. The reported tip IOException
is not reproduced at B; its inherited/introduced timing classification is
therefore masked, not proved either way.

`C1030_Windows_paths_convert_before_fixture_effects`,
`C1030_Windows_transport_ignores_ambient_launchers`, and
`C1030_Fixture_path_data_preserves_faults_and_round_trips` do not exist at B
(the whole portability file is absent). Their failures are new-test failures:
respectively disposal IOException, Git init rejection, Git init rejection.
No unrelated inherited failure was established, so no Backlog card is warranted.

Base replay used an isolated detached worktree `card-task-94914cdf-base`.
The first combined exact filter selected zero tests; the corrected suffix-star
filter reused the verified build and executed exactly the four intended methods,
all red. Neither zero execution nor build failure is counted as a defect proof.

## Repair and guards

Windows fixture Git setup now uses `git init -q` then `symbolic-ref HEAD
refs/heads/master`. Linux generation retains its original bytes and independent
goldens. This brief explicitly supersedes D-3's prohibition on changing Windows
Git init flags; production scripts remain untouched.

WSL launches now start in the stable temporary parent, outside the disposable
fixture root. All shell data paths remain absolute. Every owned child is still
awaited and disposed before deletion. No timeout, assertion or retry is loosened.
The old launch boundary was directly observed; a standalone one-shot cleanup
probe did not reproduce the transient IOException, so no claim is made that an
OS handle owner was independently identified.

The actual launch observer now rejects a working directory inside the fixture.
At `439a865ed87b2c579eaa3d7b088a661cd7c340dd`, its exact method executed once
and failed at `c1030-cwd-outside-fixture` (actual relative path `.`).
An earlier attempt at `8fd48a6df2f476d3afb2fd12c534cef1772cf769` did not build:
the new Shouldly call needed a named customMessage argument; it was fixed.

A fixture-local Git wrapper rejects init -b and forwards supported commands to
the resolved real Git, keeping this guard effective even on newer Git hosts.
After the cwd repair, at `0471f37c5991cc221d601767900a5dbf85f35211`, the same
method executed once and failed at `c1030-portable-git-init` with the wrapper's
`C1030_GIT_INIT_B_UNSUPPORTED` marker. These are ordinary red-first repair checks,
not deliberate Mutation cycles. Final CP-4..CP-6 results and unedited CHECKPOINT
lines are stored in the generated task report after this note is committed.

The only extra build is the gated checkpoint-tool bootstrap; extra diagnostic
rows are the required base replay and two new guard checks (plus the disclosed
zero-selection and compile-error attempts). Every driver uses the host slot.
No blanket repetitions or assembly/namespace runs are requested.

## Qualification and remaining ownership

Runner defaults and session-runner catalogue were read from the configured API.
Native Windows 10.0.19045, .NET test runtime 9.0.20, SDK 10.0.300. Absolute system
wsl.exe with LF stdin resolves bash 5.0.17, Git 2.25.1, node v20.20.1,
flock 2.34, jq 1.7.1 and pwsh 7.6.6; jq/pwsh are under /usr/local/bin.
No installation, persistent PATH change or host pin was made.

Restart: none. Activation/landing owner: caller, using original Code task above.
All PC-1..PC-52 and every variant stay pending SourceLanding Mutation. The plan
amendment adds PC-53/54 for these two guards; those also remain pending. Review
follows completed ordinary verification, before caller landing and Mutation.
