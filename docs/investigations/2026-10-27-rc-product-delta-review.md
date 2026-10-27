# rc/2026-10-27 product delta review

Final review of the product path from the Windows-qualified candidate
`origin/rc/2026-10-25` `105792f7d22f297746cdb338976f52e424b76fa7` to
`origin/rc/2026-10-27` `d1d090f766451797a3bd9b30c9d2966f2f8c034c`.
Subject task `e3cd0ac9-3337-407b-9535-af42cf2ed2e2`. Read-only. No source fix.

Range: 54 commits, rc-25 is an ancestor of rc-27. Product path is
`git diff origin/rc/2026-10-25 origin/rc/2026-10-27` excluding `tests/`,
`docs/`, and `.antiphon/`. Fourteen paths change. No definition file
(`*proj`, `*.props`, `*.targets`, `*.sln`, `global.json`, `nuget.config`,
`packages.lock.json`, `Directory.Build.*`, `*proj.user`) changes.

Behavior inspection: no regression, no new fail-open, no unexplained product
change. This is not a clean land receipt. The Unit lane was not executed.
The Windows checkpoint reader has no native closure. Desktop reruns
`32365cdf`, `fb80be16`, and `595bc226` were left untouched.

## Evidence consulted

- Windows failure digest on `origin/feat/card-task-d5c669aa`:
  `docs/investigations/2026-10-26-rc-8f055f87-windows-failure-digest.md`
  (172604 bytes). Qualification run `8f055f87` of candidate `105792f7`.
- Whole-run digest on `origin/feat/card-task-7e719c57`:
  `docs/investigations/2026-10-27-rc-8f055f87-final-run-digest.md`.
  `a043b0a4` there is the branch name for commit
  `281542f75b74848819ac2340402ff9e2167ec5bd`, cherry-picked onto this rc as
  `ee48cd2e4a5f5c1b91e3c74c99bcdcb80c7d7642`.
- Land-graph precheck at the rc tip, detached worktree, then removed:
  `pwsh -NoProfile -File scripts/audit-land-verification-precheck.ps1 -Check`
  printed `digest=124B52D47DD3232B60F4AEAF083B5E50ACC68DEDDE374AB2ECD6D43E543DC055 count=65`
  and exited 0. Pins blob `749d65387b4e9ae7f84fba8c7b6f42658b430dc7` matches
  `LandVerificationBuildGraph.evaluation.txt` line 3. The full graph script
  `-Check` was not run: it compares `dotnet --version` with the recorded SDK
  `10.0.402`, and this host is `10.0.403`.

## Product files

### Cache preflight, CARD-1589

`scripts/c590-remote.sh` (`12b630bdd`, `bedea1c03`, `dc40b7bc4`).
`case_runner_cache_inventory` still refuses a missing primary `server2`
container. For an empty `server2-temp` session-runner id it now accepts two
offline shapes: a completed retirement (`retiredAt` non-empty and
`retireWhenIdle` true) or a never-started hold (`retiredAt` null and
`retireWhenIdle` false). Both still require `sessions` 0, `queuedTasks` 0,
`runnerSessions` null or 0, `available` / `dispatchEligible` /
`acceptingNewWork` false, `draining` true, and `redirectTo` server2.
Missing `redirectTo` or `retireWhenIdle` fails closed. A following
`docker ps -aq` of the whole temp project, including stopped containers and
every service, must be empty; a lookup failure or any id refuses. The census
file is deleted at entry so a later present inventory cannot keep a stale
absence proof (`c913-seed-contract.sh` asserts the file is gone).
`write_result` exits, so the census does not run after an earlier refusal.

PC-31 is 2 positive admissions (`hold`, `hold-null`) plus 16 expected-red
variants (the previous 3 plus 13 hold refusals). Roster, fixture summary,
`verify-card0849-caches.ps1`, and `c1192` `s7_roster` all say 60. The loop
variable in `c849_fixture_inventory_controls` is local. `scripts/fixtures/c1589-cache-inventory.sh`
models the admission and the refusal diagnoses without host Docker.
No production retirement or volume-removal authority is added.

### Land graph, CARD-1587

The Windows failure was `C1483_S5_CheckRequiresEvaluationRecord`: precheck
refused `Content Include=Fixtures\Card0418\**` because it resolved
`current-snapshots.md`. `052479a2c` renames the Card0418 markdown and text
fixtures to `*.fixture`. `74eb11759` keeps the browser consumers on those
bytes. The csproj glob is unchanged. Definition-file bytes are unchanged,
so the selected-graph digest is the same at both rc tips.

Recomputed from `git ls-tree -r -z` with `Test-DefinitionPath`: both tips
are `124B52D4...` count 65. The qualified base recorded the stale digest
`8D4CCE85...`. `5485bb701` (cherry-pick of `375fa985`, hash files only)
stores the real digest. Precheck at `d1d090f7` exited 0. A wrong digest
would have failed the manifest compare. Land still compares this digest to
the candidate; drift defaults to building. The refresh restores a matching
record. It does not widen `Includes`.

### Windows checkpoint reader

`src/Antiphon.HostCleanup/Checkpoints/CheckpointFileReader.cs:88` and the
embedded reader `scripts/lib/checkpoint-file-reader.ps1:147`.
`ee48cd2e4` adds `FILE_FLAG_OVERLAPPED` (`0x40000000`) to the existing
`OPEN_REPARSE_POINT | SEQUENTIAL_SCAN` flags. No other Windows open in
this range changed. `CheckpointNativeIo.OpenRead` passes `WindowsFlags`
through, and `OpenStream` is `new FileStream(handle, FileAccess.Read)`,
which selects the async strategy from `SafeFileHandle.IsAsync`. On Windows
that bit is the overlapped mode. The stall digest's failure was
`read-stalled: 0 of N bytes` at `ReadAsync` on the verified reader, including
32-byte receipts and a 68985344-byte DLL. A synchronous handle queues that
read on a worker that does not observe the three-second cancellation until
the read returns. The deadline value is unchanged. A normal zero-length
read is still EOF; the stall path still requires the retained handle size
to match. This is fail-closed on a short or stalled read.

Portable witness: `scripts/test-checkpoint-reader-deadline.ps1` mode
`windows-open` substitutes only `CreateFile`, requires `FileStream.IsAsync`,
keeps the no-follow and sequential bits, and checks the bytes. The script
states that this does not qualify native Windows IO. The final digest marks
native closure pending. That obligation stays pending.

OS witness: production type is the Windows branch of `CheckpointFileReader.Consume`
and the script's `OpenWindows`. Factory is `OpenRead` / `CreateFileW` then
`FileStream(SafeFileHandle, FileAccess.Read)`. Discriminating input is the
overlapped flag. Execution mode of the new witness is portable-forced.
Native Windows execution is pending, owned by the desktop reruns. This is
not an AppHost startup-path rename or delete. No restart is required to
judge the diff. `scripts/check-os-witnesses.ps1` was not run; there is no
committed witness JSON for this merge review.

Advisory, not a product defect: `tests/Antiphon.Tests/Checkpoints/CheckpointNativeDiagnostics.cs:87`
still opens its diagnostic inspect handle without `FILE_FLAG_OVERLAPPED`.
The verified read beside it goes through `ConfinedFileReader.Read`, which
has the new flag. The diagnostic inspect is not the stall path in the digest.

### Client

`client/src/features/agents/AgentsPage.tsx:552` (`6647f8db3`, cherry-pick of
`cd793a1f`). Shift+F10 and the ContextMenu key already `preventDefault`.
They now `stopPropagation` so the same keydown cannot reach the menu's
outside-dismiss listener and close the menu it just opened. The handler
stays limited to those two keys. The other `stopPropagation` calls in the
file are pre-existing click and mouse-down handlers.

`client/src/features/orchestrator/PipelineStagesPanel.tsx:70` (`a3df02c2b`,
cherry-pick of `58e097b1`). The glance box gains `w="100%"` beside the
existing `maw={560}` and `overflowX: hidden`, so the strip is bounded by
the viewport instead of its min-content width. No data or stage logic
changes.

Both were already reviewed clean on their source branches. The rc hunks
match those commits. No further defect.

### Other product paths

`scripts/test-rc-platform.ps1` (`1adeb7a34`) writes an empty
`antiphon-nightly-class-costs-v1` document into the C1323 synthetic-node
fixture and points the seam at it. Callers are only that test script.
`scripts/lib/nightly-tests-impl.ps1` still loads `nightly-class-costs.json`.
The fixture no longer inherits native class costs for seamed nodes.

`scripts/fixtures/c1192-workspace-contract.sh` (`e5f0b558f`, `f184fcd98`)
stops the read-only race model from `chmod a-w` on the shared backing tree
for the two exact production probes `test -f "$1"` and `test -e "$1"`.
A peer write is interleaved inside those probes and must succeed. Every
other read-only command still toggles the directory mode. The exception
matches five argv fields exactly.

`src/Antiphon.FakeGrok/Program.cs` and `SignInScreen.cs` were edited by
`ea1ff2531` and restored by `b3860c5f9`. Net diff against rc-25 is empty.

The three Claude TUI `/rc` badge fixes are already on the qualified base,
not in this delta: `de9a8fba4` (wrapped hint badge), `c90f8047b` (Claude
2.1.310 weekly-usage and paste-expand rows), `cdfd8366f` (wrapped badge
plus connection status), all `2026-10-26 08:45` and ancestors of `105792f7`.
No pty product file changes in this range.

## Verification

Ordinary scope in the profile is the Unit lane plus named integration
classes. That selection was not run. The brief forbids native and
process-spawning tests and assigns the desktop reruns elsewhere. No
checkpoint receipt was produced for `d1d090f7`. `reviewedSourceClean` is
false. `ordinaryScopeCompleted` is None. PCs stay pending. Behavior has
no substantive finding, so this is not a Code repair. D-2 does not
authorize land without complete required verification.

## Outcome

No product defect. Do not land `d1d090f766451797a3bd9b30c9d2966f2f8c034c`
from this review.
