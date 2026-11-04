# Group-79 `test-owned disposal unresolved` in DelegationBriefCeilingPtyTests (rc/2026-11-05, run 766e3df5)

Investigate task 1c2d93f1, 2026-11-04. Base master `d4c88ddde554ae9f01fad65f27bc893175271b51`;
rc cut `1470340559889c2985ea722173181ea25c51b47c`.

## Verdict

**Probable one-off teardown race, not confirmed.** The single failure is a supplemental
**capture** error recorded by `TestOwnedPtyHost`. It is not a process that survived, a failed kill
or a timeout that was too short. The timing in the exception text shows that the capture ran
while the pty host was still finishing its normal shutdown after the runner acked it. That
shutdown deletes the manifest the capture reads. The inner exception that would prove which call
failed is in the TRX on the desktop, and this server2 mirror could not read it (see Gaps).

## Evidence

### The failing message, decoded

`AggregateException: 'test-owned disposal unresolved elapsed=00:00:00.0384351 (cleanup-unresolved
elapsed=00:00:00.0328774 (capture session=bf50c35d-... manifest=...\pty-hosts\manifests\bf50c35d....json))'`

`AggregateException.Message` appends one `(inner)` per inner exception. Each level shows exactly
one inner exception, so:

- `DisposeCoreAsync` (`tests/Antiphon.Tests/TestHelpers/TestOwnedPtyHost.cs:126-158`) collected
  exactly one failure. That failure is the `CleanupAsync` aggregate. The supplement, the
  session-stop phase, `disposeRuntime` and the handle releases all succeeded.
- `CleanupAsync` (`:160-185`) collected exactly one failure. It is the only entry carried over
  from `_errors` (`:163`). It has no `phase=kill`, `phase=wait`, `phase=probe` or
  `survivor/...` entries, so every retained process (host and descendants) was observed
  `Dead` at the end.
- An entry in `_errors` comes only from the catch-all in `Capture` (`:105`):
  `IOException("capture session=<id> manifest=<path>", inner)`. Something threw inside one
  `Capture(...)` call for this session. The candidates are `Under` → `Canonical` (which reads
  `FileInfo.LinkTarget` on the manifest itself), `ReadOwnedManifest`, `CaptureProcess`, the
  `host identity rejected` check, or the WMI `CaptureDescendants`.

So "unresolved" here does **not** mean a process failed to exit. It means that one capture
attempt threw, and the helper is designed to fail closed on that: an unknown result is never
reported as clean (CARD-1020 plan D-2/V-11).

### When the capture ran (timing reconstruction)

The call order on this test's teardown, at the cut and on master. The files are identical: `git diff
1470340559 origin/master` is empty for `TestOwnedPtyHost.cs`, `DirectSessionRunnerClient.cs`,
`DelegationBriefCeilingPtyTests.cs` and `NativePtyDiagnostics.cs`.

1. `Harness.DisposeAsync` (`DelegationBriefCeilingPtyTests.cs:360-361`) calls
   `_client.KillAsync`. The runtime awaits the child's exit. The host first writes its exit
   manifest (`HostSession.cs:479-489`, `SaveAtomic` = write `.tmp` + `File.Move` overwrite), then
   sends `Exited` (`:495`).
2. On `Exited` the runner sets `_exited` and fires `_ = Task.Run(ShutdownHostAsync)`
   (`SessionRunnerRuntime.cs:4743-4749`), so `KillAsync` returns **before** the host has acked.
   On the ack the host runs `Shutdown()` → `TryDeleteManifest()` (`File.Delete`) → `RequestExit`
   (`HostSession.cs:403-408`, `:548-558`). Only then does the host process exit.
3. `Harness.DisposeAsync` then calls `_client.DisposeAsync()` at once (`:362`). That runs the
   supplement `CaptureOwnedHosts` → `CaptureTracked(_runtime.List(), _startRequests)`
   (`DirectSessionRunnerClient.cs:417-428`), which calls `Capture` **twice** for this session.
   The session is still listed as Exited, and its start request is still queued. Each call goes
   through `Under(manifest)` and `ReadOwnedManifest(manifest)`.

Measured from the exception text:

| Quantity | Value | Source | Meaning |
|---|---|---|---|
| Whole disposal | 38.4 ms | outer `elapsed` (`:157`, clock started `:128`) | |
| CleanupAsync | 32.9 ms | inner `elapsed` (`:184`, clock started `:162`) | |
| Supplement + stop + records | **≤ 5.5 ms** | 38.4 − 32.9 | Neither supplement `Capture` reached the WMI `Win32_Process` descendant query. That needs a live host **and** a readable manifest. Each call therefore saw no manifest or threw before WMI (inference: a WMI process query is assumed to take well over 5 ms; not measured here). |
| CleanupAsync wait | 32.9 ms ≈ one `Task.Delay(20 ms)` tick on Windows (15.625 ms timer granularity → 31.25 ms) + ~1.6 ms | `WaitNativeAsync` `:226-235` | When cleanup started, at least one retained process was **still alive**. It died within one poll. If everything had already been dead, the wait phase would have taken ~0 ms. |

Taken together: about 5 ms after `KillAsync` returned, the host tree was still tearing down,
which is exactly the host's ack → manifest delete → exit sequence in step 2. The supplement's two
manifest reads therefore overlapped the host's `File.Delete` of the same file. Only one of the
three capture attempts failed (one inner entry). A deterministic fault, such as a Windows
identity mismatch or a stale manifest naming a dead host, would have failed every attempt that
found the manifest. One failure fits a transient condition.

### Live hypotheses

| # | Mechanism | Consistent with the numbers? | What the TRX would show |
|---|---|---|---|
| H1 (favoured) | A supplement `Capture` opened the manifest during the host's `File.Delete`. On Windows, an open while a delete handle is held without `FILE_SHARE_DELETE` (`File.ReadAllText` shares Read only) gives `ERROR_SHARING_VIOLATION` → `IOException`. An open of a delete-pending file gives `ERROR_ACCESS_DENIED` → `UnauthorizedAccessException`. This also applies to `Canonical`'s `FileInfo.LinkTarget` open of the manifest. `ReadOwnedManifest` (`:297-302`) treats only `FileNotFound`/`DirectoryNotFound` as absent, so either exception becomes the capture error. The second call moments later saw the manifest gone. | Yes: fast (< 1 ms), one failure, host still exiting | inner `IOException ... being used by another process` or `UnauthorizedAccessException: Access to the path '...manifests\bf50c35d....json' is denied`, thrown from `ReadOwnedManifest` or `Canonical` |
| H2 | The launch-time `Capture` in `DirectSessionRunnerClient.StartAsync` (`:212`) failed 34 s earlier, and the error sat in `_errors` until disposal. Examples: a `ManagementException` from the WMI query under the Full run's load, or a `Win32Exception` from `MainModule` on a descendant. | Yes: timing-neutral. For it to fit, both supplement calls must have found no manifest | inner `ManagementException`/`COMException`/`Win32Exception` with a `CaptureDescendants` frame; the C1020 stdout has a host `captured` line but no child `captured` lines |
| Excluded | Supplement `CaptureProcess` on an already-exited host whose kernel object is kept alive by the retained launch handle. `.NET` `MainModule` then loops `EnumProcessModules` about 51 × `Sleep(1)` before throwing `Win32Exception`. | No: ≥ 51 ms (≥ ~0.8 s at the default timer resolution), but the supplement took ≤ 5.5 ms (behaviour recalled from the .NET source, not re-read here) | — |
| Excluded | A budget too short. Disposal gives 2 s grace + 5 s fallback. | No: there are no `wait`/`survivor` entries, and everything was dead at 33 ms | — |

### Did the test body pass?

The frame `DelegationBriefCeilingPtyTests:162` maps to the `Console.WriteLine` after the last
assertion (line 158) at the cut. That points to the normal end-of-method `await using`
disposal. Note that a disposal exception thrown from `await using` **replaces** a body exception,
so the frame alone is not proof. The TRX StdOut line
`[CARD-0028] inline brief: N/M goal lines lost` proves the body completed.

### Is it known, and is it already fixed?

- `grep` of the repo docs and tests finds no prior report of this signature outside the CARD-1020
  plan. A board search (`card.ps1 search 'test-owned disposal' -All` and `'TestOwnedPtyHost'`)
  returned 0 cards.
- The only later change to `CaptureProcess` is `d1cffdb2f` (2026-10-20, in the cut). It handles a
  Linux exited process with a null `MainModule`. It does not touch the manifest read.
- Master `d4c88ddde` is identical to the cut for all four files, so **master has not fixed it**.
- Exposure: 28 test files construct `DirectSessionRunnerClient`. The kill-then-dispose order is
  common, so the window is shared by every Windows real-pty test that calls `KillAsync` and then
  disposes the client.

## Gaps (what would resolve the remaining uncertainty)

- **The TRX was not read.** It lives on the desktop, at
  `C:\Antiphon\releases\candidates\rc-2026-11-05-147034055988\logs\2026-11-04-1018-766e3df5\antiphon-all-group-79.trx`.
  This server2 mirror has no mount and no HTTP file-read route (`/api/filesystem/browse` lists
  directories only). One read on the desktop lane decides between H1 and H2:
  1. the full exception text of the failing result, specifically the type and message of the
     inner exception under `capture session=...`, and its top frame;
  2. the result's StdOut `C1020 cleanup operation=...` lines with their `at=` timestamps. A host
     plus child `captured` set at launch means H1. A host `captured` with no child lines means H2.
     Also check that `[CARD-0028] inline brief:` is present;
  3. the same test's outcome in earlier candidates' `antiphon-all-group-79.trx` (read-only), to
     confirm the pass history.
- Earlier-candidate pass history was not checked, for the same reason.
- Nothing was reproduced. The Windows sharing and delete-pending semantics do not exist on Linux,
  so no build or test was run on server2.

## Not done, noted

- Fix idea (H1): in `TestOwnedPtyHost.Capture`, treat a Windows `IOException`/`UnauthorizedAccessException` on the manifest open (`Canonical`/`ReadOwnedManifest`) as transient. Retry a few times at about 10 ms, then treat it as absent when this session's host generation is already in `_owned`. Add a red witness with an injected `ReadManifest` that throws once.
- Qualification action: rerun group 79 once. The evidence fits a single transient overlap. A repeat would point to H2, or to a broader Windows regression.
