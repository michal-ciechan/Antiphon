# CARD-1368 — checkpoint cleanup: adopt what the row's own host wrote; stamp build entries over 64 MiB

Plan task 3a3709bf (Frontier), branch `feat/card-task-3a3709bf`, fast-forward from
`a653f881fe044acb9e75382deac13d10f9bece64` (`feat/card-task-55a1921b`, CARD-1358 S1-S3 and CARD-1356).
Master-only; the rc recommendation is D-6.

## Outcome

1. **S1 — host-exit adoption.** After a row's test host exits, the executor adopts the entries that host
   (and its child processes) created under the row's own sealed output root, under the same custody
   window rule the build seal uses (CARD-1242), with the driver-observed host exit as the window's upper
   bound. A green row then deletes `TestResults` with the rest of its output and `validate` returns
   `CHECKPOINT SOURCE VALID`. Nothing is adopted by name. A build entry the host modified, a link, a
   tracked path, and anything written after the host exit still refuse, exactly as today.
2. **S2 — content-stamp bound.** The seal-time content record and the cleanup-time comparison hash
   through a streaming digest bounded at 1 GiB instead of a 64 MiB in-memory receipt read, so a build
   entry such as Playwright's `node.exe` (93.6 MB) or Linux `node` (126.6 MB) is adopted at the seal and
   deleted on green. Receipt reads keep their 64 MiB cap.

One red-first witness per slice (W-1, W-2), deterministic and portable; one narrow green run of the
`### Checkpoints` table. S1 alone fits the 45-minute Code budget; S1 + S2 is about 70 minutes (D-2).

## Evidence and ground truth

Inspected at the task base `a653f881` (this branch already contains CARD-1358). CARD-1368 read in full
(0 revisions). The Windows evidence (`20261019-204804-6dce` under task 07e1d25f; Review 674b4e96) is not
reachable from this runner mirror; the card's reading of it is taken as given where marked. The CARD-1359
Code branch was fetched read-only at `6ededc82213a45440d2fb7035f6261e9888c084b` for its checkpoint table.
No builds or tests were run during Plan.

| Card / brief assumption | Code and repository truth | Consequence |
|---|---|---|
| "The test host creates TestResults under the isolated output after the ledger snapshot." | True, with one precision. The row's own host receives `--results-directory <run>/rows/<CP>` (`BuildStep.RunArguments`, `tools/Antiphon.Checkpoints/Execution/BuildStep.cs:83-100`), so it writes nothing beside the DLL. The writer is the **child TUnit host** that `LandDeliveryFixture.UseChildAsync` launches: `dotnet <Antiphon.E2E.dll> --treenode-filter /*/*/AgentTaskLandDeliveryE2ETests/C467_V22*` with no `--results-directory` and no working directory (`tests/Antiphon.E2E/Fixtures/LandDeliveryFixture.cs:262-270`). Every C540 crash test goes through it (`CrashAsync`, `tests/Antiphon.E2E/DispatchBaseWarningDeliveryE2ETests.cs:112-115`), and CP-2 of the CARD-1359 run selected two of them. Microsoft.Testing.Platform's default results directory is "the directory that contains the test application" (string present in Microsoft.Testing.Platform 2.2.2; the SDK leaves `RunWorkingDirectory` empty, `Microsoft.NET.Sdk.targets:1408`), so the child's TUnit HTML report lands in `bin-c1359-green/TestResults` whatever the working directory. The seal (`CheckpointBuildBinding.Write` → `OwnedOutputLedger.Seal`, `RowRunner.cs:233`, `RunScheduler.cs:452`) runs right after the build, and `Check` refuses any live entry without a creation record (`src/Antiphon.HostCleanup/Checkpoints/OwnedOutputLedger.cs:49-52`). | Adopt at the host exit under the custody window (D-1, D-5). |
| "The output ledger tolerates (or pre-declares) runtime-created TestResults." | Pre-declaring the directory cannot work: `Check` and `Remove` inventory the live tree and need a record per entry (`TestResults/<report>.html`), and `RecordContent` is append-only (`CheckpointCreationRegistry.cs:23`, `TryAdd`), so the sealed ledger file cannot be rewritten either. CARD-1264 ruled out adoption by name ("no report directory is adopted by name", `docs/testing-and-build.md:1139`). | Window-based adoption, never by name; the disk ledger stays the build-time inventory (D-1). |
| Brief: the 93 MB `node.exe` refusal in Review 674b4e96 is CARD-1358's read deadline. | Only partly. Content stamps go through `ReceiptFile.ReadBytes` → `CheckpointFileReader.ReadBytes(..., 64 * 1024 * 1024)` (`src/Antiphon.HostCleanup/Checkpoints/ReceiptFile.cs:21`), which refuses `selected file exceeds size limit` before any byte is read (`CheckpointFileReader.cs:68`). Microsoft.Playwright 1.63.0 copies `.playwright/node/win32_x64/node.exe` (93,580,104 bytes) on Windows and `linux-x64/node` (126,595,440 bytes) on Linux into the output (`Microsoft.Playwright.targets:21,34`). Both exceed 64 MiB, so `AdoptCreated` leaves them unrecorded and cleanup refuses `output-ledger-mismatch; left in place: ...node(.exe)` on every green E2E row on every platform, with or without CARD-1358; the stall window only decides whether the open survives a Windows filter scan. `Check` names the first unrecorded entry in breadth-first inventory order, so a depth-1 `TestResults/<report>.html` is named before the depth-3 `node.exe`: the Code run saw TestResults, the Review run (no child host in its selection) saw `node.exe`. | S2 is required for the E2E lane to clean at all (D-2, D-3). |
| Brief: should a refusal confined to the row's own output invalidate the source receipt? | Today any refusal flips a green run to exit 2 / `RED` (`tools/Antiphon.Checkpoints/CheckpointApp.cs:362-376, 399-412, 420-430`) and both validators return `cleanup_refused` whatever the kind (`Report/ReportValidator.cs:27`; `scripts/validate-checkpoint-receipt.ps1:70`); `CheckpointSourceExecutionTests.cleanup_refusals_disqualify_green_rows_in_both_validators` pins both. Source cleanliness is proven separately by the Start/End observations and the build-source binding (`ReportValidator.IsSourceEligible`). `wait`'s receipt binding requires bound `checkpoint-build-source.json` receipts whenever exit 0 leaves outputs (`Report/ReportWriter.cs:116-124`). | Not in this card (D-4); sibling card with the sketch below. |
| rc evidence (`docs/superpowers/evidence/2026-10-16-rc-e2e-d7-d12.md:64-65`): two Playwright `node.exe` entries refused as hard links on the desktop. | Unverifiable from here: no Windows output is reachable, and no hard-link copy switch exists in the repo (`Directory.Build.props`, `Directory.Build.targets`, `Directory.Build.rsp`, `Microsoft.Playwright.targets`). An entry with more than one link is refused `multiple-links` by `OutputCustody.Refusal` and `selected file has multiple links` by the reader; S2 does not and must not change that. | Measure on the desktop after S2 lands: `fsutil hardlink list tests\Antiphon.E2E\bin-<id>\.playwright\node\win32_x64\node.exe` right after a checkpoint build. More than one path means the desktop E2E lane still refuses and the D-4 sibling becomes the unblocking change there. |
| Brief: build on CARD-1358; must this land after it? | The branch contains CARD-1358 S1-S3 (and CARD-1356). S2 edits `CheckpointFileReader.cs`, whose chunk loop CARD-1358 S1 rewrote (stall window, 256 KiB chunks). | Land after CARD-1358. A land of this branch first would carry CARD-1358's commits past its Review. |
| Brief: Code budget 45 minutes, one witness. | S1 is one production method, one call site, one witness. S2 is a reader refactor with its own witness. | D-2: S1 + S2 by default (about 70 minutes); S1 alone when the budget is firm. |
| Platform routes. | `GET /api/runner-defaults` and `GET /api/session-runners` are not reachable from the server2-temp container by design (caller refinement, 2026-10-19); the server is up. Last reading on this fleet, same day (CARD-1358 plan): `globalRunnerId: server2`; runners `desktop` (windows), `server2` and `server2-temp` (linux). | Both witnesses are portable; dispatch unpinned (see Platform and dispatch). |

## Decisions

- **D-1 — Adopt at the host exit under the custody window; never by name.** `OwnedOutputLedger.Adopt(root, hostExited)` re-inventories the sealed root and calls `SafeWrite.AdoptCreated` for every entry without a record, with `OutputCustody.For(context, root, hostExited)` as the window: root created owner-only by this process, entry a regular single-link file or directory owned by the tool's uid, ctime inside `[root creation, host exit]`, mtime not after the exit; Windows uses the CARD-1310 change-time rule; other platforms adopt nothing. An existing record always wins, so a build entry the host rewrote still mismatches. Rejected: a by-name allowance for `TestResults` (CARD-1264 rejected it; a name is not custody, and the next runtime-created directory would refuse again); passing `--results-directory` in `UseChildAsync` (one E2E launch site, not the tool; see D-6); changing the host's working directory (the platform ignores it); rewriting `.checkpoint-output-ledger.json` at adoption (the append-only registry would then refuse the ledger file itself; the disk ledger is diagnostic and cannot authorize deletion, so it stays the build-time inventory).
- **D-2 — S2 ships in the same Code task by default.** Without S2 every green E2E row still refuses on `node(.exe)`, so the card's blocking symptom persists on both platforms after S1. Each slice is independently landable; the Code brief may take S1 only if the 45-minute budget is firm, in which case S2 becomes the next Code task on this plan.
- **D-3 — Content stamps stream with a 1 GiB bound; receipts keep 64 MiB.** `CheckpointFileReader.Digest` shares the open, inspection, link and growth checks and the stall window with `ReadBytes` through one private `Consume` loop and feeds `IncrementalHash` instead of a `MemoryStream`; `ReceiptFile.Digest` mirrors `ReadBytes`'s Windows preflight and `receipt-read-refused` wrapping; `CheckpointWrite.Describe` and `MatchesCreation` use it and the record string `<length>:<SHA-256 hex>` is unchanged. Rejected: raising the receipt cap (receipts are materialised in memory); no bound (an absurd build could hold the seal indefinitely); a second record format for large files (two formats for one comparison).
- **D-4 — `cleanup_refused` stays an invalidation in this card; sibling card for classification.** Position for the sibling: a refusal confined to the row's own `bin-<id>` root (`output-ledger-mismatch`, `output-inventory-failed`, `still-present`, `left in place: …` under the root) is output hygiene and should not invalidate a source-clean receipt; `tracked`, `outside-isolated-root`, `not-isolated-output`, `path-unsafe`, `link-component`, `inventory-missing`, `not-created-by-run` and `output-memory-authority-missing` are containment doubts and should. Not here because it changes the run's exit and verdict (three blocks in `CheckpointApp`), the `wait` receipt binding, both validators and the docs (six files, over the budget), and because it keeps hundreds of MB of E2E output on disk per green run, which the card asks to delete. Sketch: `CleanResult.Refusals` becomes `(reason, contained)`; `CheckpointApp` keeps exit 0 / `GREEN` with `cleanedOutputs: false` and the refusals listed when every refusal is contained; `ReportValidator` and the script return `cleanup_refused` only for an uncontained refusal; `report.md` keeps `outputs: refused/kept …`; one witness through `ExecuteAsync` with both validators.
- **D-5 — Adoption runs in `RowRunner` after each host invocation.** After the primary run and after each rerun, only when the driver reported `Exited` and the invocation did not time out; the `row` verb and the scheduler share the path. Rejected: adopting in `RunScheduler` (no driver-observed exit there without a new `RowRunResult` field); adopting at cleanup with `DateTimeOffset.UtcNow` (no observed exit is fail-closed by CARD-1242's design).
- **D-6 — Master-only now; rc-worthy.** Both slices are bug fixes to code already on `rc/2026-10-18` (the custody seal landed with CARD-1242 on 2026-10-14; the reader cap predates it), so the Fix rule in `docs/release-gates.md:186` admits them on the line. Cherry-pick after the master land when the rc line next needs a desktop E2E receipt (the CARD-1305/1359 lane). Passing `--results-directory` in `UseChildAsync` is a separate, optional hygiene change under the E2E owner and is not needed for this fix.
- **D-7 — No session that waits for input is added or changed.** The checkpoint tool is a child of a Code or Review task; nothing here parks or blocks on input (CARD-1083 checklist: not applicable).

## Design

### S1 — host-exit adoption

`src/Antiphon.HostCleanup/Checkpoints/OwnedOutputLedger.cs`:

```csharp
public static int Adopt(string root, DateTimeOffset? hostExited) =>
    new CheckpointOutputLedger(CheckpointCleanupDefaults.Context).Adopt(root, hostExited);

// CARD-1368: entries the row's own host wrote under the sealed root are adopted at the driver-observed
// host exit under the CARD-1242 custody window. An existing record wins; a link anywhere in the tree,
// a root this process did not create, or no observed exit adopts nothing and cleanup refuses as before.
public int Adopt(string root, DateTimeOffset? hostExited)
{
    try
    {
        if (!Io.DirectoryExists(root) || !Io.FileExists(Path.Combine(root, Ledger))) return 0;
        var window = SafeWrite.WasCreated(root) ? OutputCustody.For(context, root, hostExited) : null;
        if (window is null) return 0;
        var adopted = 0;
        foreach (var relative in Inventory(root).Keys)
        {
            var full = Path.Combine(root, relative);
            if (SafeWrite.CreationRecord(full) is not null) continue;
            SafeWrite.AdoptCreated(full, window);
            if (SafeWrite.CreationRecord(full) is not null) adopted++;
        }
        return adopted;
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return 0; }
}
```

`tools/Antiphon.Checkpoints/Execution/RowRunner.cs`, after `var sourceStable = request.SourceBoundary?.Invoke() != false;` (line 268) and after each `second` rerun result (line 326):

```csharp
if (binding is not null && !run.TimedOut) OwnedOutputLedger.Adopt(binding.OutputPath, run.Exited);
```

`binding.OutputPath` is the sealed root (`CheckpointBuildBinding.Expected` resolves the same absolute
`bin-<id>` the seal used). Referenced projects write under `<root>/artifacts` and the E2E apphost copy
under `<root>/ptyhost-native`, both inside it. The window's upper bound is the host exit, so a write by a
lingering grandchild after the host exit is refused `changed-after-build` and stays for manual removal.

Docs (`docs/testing-and-build.md`): in the CARD-0723 paragraph (`:466`) after "…is retained for manual
removal.", one sentence: entries the row's own test host and its children write under the sealed root are
adopted at the driver-observed host exit under the same window (CARD-1368); in the exit-codes paragraph
(`:491`) after "…only for entries inside the seal-time custody window above:" the same rule in one clause;
rewrite `:1139` "Unknown post-seal files still refuse cleanup; no report directory is adopted by name." to
state that post-seal files are adopted only by custody at the host exit, never by name, and that a file
outside that window still refuses.

### S2 — content-stamp bound

`src/Antiphon.HostCleanup/Checkpoints/CheckpointFileReader.cs`:

```csharp
public const long ContentLimit = 1L << 30; // content stamps (seal and cleanup); receipts keep 64 MiB
public static (long Length, string Sha256) Digest(CheckpointCleanupContext context, string root, string path,
    long limit, CancellationToken cancellationToken = default)
{
    using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    long total = 0;
    Consume(context, root, path, limit, null, cancellationToken, span => { hash.AppendData(span); total += span.Length; });
    return (total, Convert.ToHexString(hash.GetHashAndReset()));
}
// ReadBytes keeps its signature and 64 MiB callers: a MemoryStream sink over the same Consume.
private static void Consume(CheckpointCleanupContext context, string root, string path, long limit,
    Action? afterVerification, CancellationToken cancellationToken, Action<ReadOnlySpan<byte>> sink)
{ /* today's open, Inspect, Refusal, size check, afterVerification, stall-window chunk loop with long totals;
     the one-byte-past-the-limit growth check and the complete-read stall exemption unchanged */ }
```

`ReceiptFile.Digest(context, path)` mirrors `ReadBytes` (Windows directory/reparse preflight, exceptions
wrapped as `receipt-read-refused: <path>: <msg>`) with `CheckpointFileReader.ContentLimit`.
`CheckpointWrite.Describe` returns `length + ":" + sha` from `ReceiptFile.Digest`; `MatchesCreation`
compares the same string. The `AdoptCreated` retry and the `OutputCustody.Stat` before/after check are
untouched, so the custody tests' stream seam still sees one open per adoption.

Docs: the "Build-output deletion authority comes from append-only creation records in the current process,
including file size and hash at creation" sentence (`:466`) gains "hashed through a streaming digest bounded
at 1 GiB; an entry over that bound stays unrecorded (CARD-1368)".

## Slices

### S1 — host-exit adoption

Files: `src/Antiphon.HostCleanup/Checkpoints/OwnedOutputLedger.cs`;
`tools/Antiphon.Checkpoints/Execution/RowRunner.cs`;
`tests/Antiphon.Tests/Checkpoints/CheckpointSourceExecutionTests.cs` (W-1);
`tests/Antiphon.Tests/Checkpoints/CheckpointTestSupport.cs` (`CheckpointFixtures.AfterFileClock` moved from
`OutputCleanupTests`, which keeps calling it); `docs/testing-and-build.md`.

Red commit: W-1 only (the production path exists, so no inert seam is needed; W-1 is red on its first
assertion, exit 2 where 0 is expected, with `cleanupRefusals` naming the HTML report). Fix commit: D-1 and
D-5 plus the doc sentences. Red run `--rows CP-1`, then the green run after S2.

### S2 — content-stamp bound

Files: `src/Antiphon.HostCleanup/Checkpoints/CheckpointFileReader.cs`;
`src/Antiphon.HostCleanup/Checkpoints/ReceiptFile.cs`; `src/Antiphon.HostCleanup/Checkpoints/SafeWrite.cs`;
`tests/Antiphon.Tests/Checkpoints/OutputCleanupTests.cs` (W-2); `docs/testing-and-build.md`.

Red commit: W-2 only (red on `MatchesCreation(big).ShouldBeTrue()`, false at the base because the 64 MiB
receipt read refuses the entry at the seal). Fix commit: D-3 and the doc clause. Red run `--rows CP-2`.

Order: S1 then S2; each lands on its own if the other is dropped. When the Code task takes both, the two
witnesses may share one red commit and one red run (`--rows CP-1,CP-2`), since the build is the cost.

## Verification design

Witnesses. Each is one TUnit method; "red" is the assertion that fails at the slice's red commit.

| ID | Behaviour | Witness (class.method), seam, decisive assertions |
|---|---|---|
| W-1 | A file the row's host writes under the sealed root after the seal is adopted at the host exit and deleted on green; both validators accept. | `CheckpointSourceExecutionTests.host_written_entries_under_the_row_output_are_adopted_and_cleaned_on_green`: repo = `TempDir()` with `sample/sample.csproj` (as in `clean_and_dirty_runs_publish_bound_source`); manifest build `bin-c1368` on `sample`, CP-1 `/*/*/ExampleSurfaceTests/*`, `Expect = ["ExampleSurfaceTests"]`; `RunRequest { KeepOutputs = false, Slots = "off" }`; `FakeDriver` build → default result (no `Exited`, the seal adopts nothing, the two SafeWrite receipts are recorded); run → `CheckpointFixtures.WriteResults(TrxFile(request), ("Antiphon.Tests.ExampleSurfaceTests.case", "Passed"))`, then `Directory.CreateDirectory(<repo>/sample/bin-c1368/TestResults)` and `File.WriteAllText(.../TestResults/sample_report.html, "<html>host</html>")`, returning `new DriverResult(0, "", "") { Exited = CheckpointFixtures.AfterFileClock(html) }`. Assert `ExecuteAsync` returns 0; `report.Verdict == "GREEN"`, `ExitCode == 0`, `CleanedOutputs == true`, `CleanupRefusals` empty; `Directory.Exists(output)` false; `Program.RunAsync(["validate", "--repo-root", repo, "--evidence", <run>/report.json, "--expected-source-sha", Sha]) == 0`; `ValidateFileWithScriptAsync(evidence).Exit == 0`. Red at base: exit 2, `CleanupRefusals == ["output-ledger-mismatch; left in place: " + html]`, `report.md` contains `refused: output-ledger-mismatch`, both validators say `cleanup_refused`. |
| W-2 | A build entry over the 64 MiB receipt cap is adopted at the seal and removed on green; the receipt cap itself stays. | `OutputCleanupTests.build_entry_over_the_receipt_limit_is_adopted_at_seal_and_removed_on_green` (skip unless Linux or Windows: other platforms adopt nothing by design): `InitGitRepo()`; `CreateFreshDirectory(<root>/bin-c1368)`; `File.WriteAllText(app.dll, "COMPILER")`; `big.bin` via `File.Create` + `SetLength(64L * 1024 * 1024 + 1)`; `exited = AfterFileClock(app, big)`; `Should.Throw<IOException>(() => ReceiptFile.ReadBytes(big)).Message.ShouldContain("selected file exceeds size limit")` (control, green before and after); `SafeWrite.Require(OwnedOutputLedger.Seal(output, exited))`; `SafeWrite.MatchesCreation(big).ShouldBeTrue()`; `CleanOwnedOutputs(root, ["bin-c1368"], 0, false, false, createdPaths: [output])`: `Refusals` empty, `Deleted == [output]`, `Directory.Exists(output)` false. Red at base: `MatchesCreation(big)` is false. |

Regression controls executed green in the same run: R-1 the forty-eight existing `OutputCleanupTests`
(seal adoption, late additions, links, tracked paths, entry cap, Windows custody) and the four
`CheckpointOwnedOutputTests` (forged, missing, truncated and outside disk ledgers never authorize deletion);
R-2 the eight existing `CheckpointSourceExecutionTests` including `cleanup_refusals_disqualify_green_rows_in_both_validators`
(D-4: a refusal still invalidates); R-3 the reader and custody suites the S2 refactor passes through:
`HostCleanupCheckpointCustodyTests` (13), `HostCleanupCheckpointReceiptTests` (8),
`HostCleanupCheckpointReadDeadlineTests` (2), `HostCleanupCheckpointAuthorityTests` (6),
`HostCleanupCheckpointExtractionTests` (3), `HostCleanupCheckpointCompatibilityTests` (45),
`CheckpointToolCopyCleanupTests` (13); R-4 the row path the S1 call sits on: `RunSchedulerTests` (17) and
`TimeoutTests` (8 listed, 7 off Windows).

### Checkpoints

Lane: `Antiphon.Tests`, portable; `CheckpointSourceExecutionTests` and `CheckpointOwnedOutputTests` spawn
`pwsh`/`git` under `ParallelLimiter<ProcessSpawnLimit>` and are class-listed in `slow-tests-allowlist.txt`
already. One build, `bin-c1368/`, rebuilt by each run; every row is `After S1-S2` because rows that reuse
one build must share an After group (importer rule), and a red run selects its rows with `--rows`. Counts
are executed TUnit cases; platform skips are listed.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1-S2 | `tests/Antiphon.Tests -> bin-c1368/` | process-unit-adopt | `/*/*/CheckpointSourceExecutionTests/host_written_entries_under_the_row_output_are_adopted_and_cleaned_on_green*` | W-1 | 1 executed, 0 failed/skipped | 1 | 2 |
| CP-2 | S1-S2 | CP-1 | portable-unit-stamp | `/*/*/OutputCleanupTests/build_entry_over_the_receipt_limit_is_adopted_at_seal_and_removed_on_green*` | W-2 | 1 executed, 0 failed/skipped | 1 | 1 |
| CP-3 | S1-S2 | CP-1 | portable-unit-cleanup | `/*/*/OutputCleanupTests/*` | R-1 | 49 listed; on Linux 45 executed and the 4 Windows-only methods skipped, on Windows 35 executed and the 14 Linux-only methods skipped; 0 failed | 35 | 5 |
| CP-4 | S1-S2 | CP-1 | process-unit-execution | `/*/*/CheckpointSourceExecutionTests/*` | R-2 | 9 executed, 0 failed/skipped | 9 | 4 |
| CP-5 | S1-S2 | CP-1 | portable-unit-custody | `/*/*/HostCleanupCheckpointCustodyTests/*` | R-3 | 13 executed, 0 failed/skipped | 13 | 1 |
| CP-6 | S1-S2 | CP-1 | portable-unit-receipt | `/*/*/HostCleanupCheckpointReceiptTests/*` | R-3 | 8 executed, 0 failed/skipped | 8 | 1 |
| CP-7 | S1-S2 | CP-1 | portable-unit-reader | `/*/*/HostCleanupCheckpointReadDeadlineTests/*` | R-3 | 2 executed, 0 failed/skipped | 2 | 1 |
| CP-8 | S1-S2 | CP-1 | process-unit-owned | `/*/*/CheckpointOwnedOutputTests/*` | R-1 | all listed cases executed; the `link` variant of `changed_inventory_is_refused` skips on Windows; 0 failed | 4 | 1 |
| CP-9 | S1-S2 | CP-1 | portable-unit-toolcopy | `/*/*/CheckpointToolCopyCleanupTests/*` | R-3 | 13 executed, 0 failed/skipped | 13 | 2 |
| CP-10 | S1-S2 | CP-1 | portable-unit-authority | `/*/*/(HostCleanupCheckpointAuthorityTests*\|HostCleanupCheckpointExtractionTests*)/*` | R-3 | 9 executed, 0 failed/skipped | 9 | 1 |
| CP-11 | S1-S2 | CP-1 | portable-unit-compat | `/*/*/HostCleanupCheckpointCompatibilityTests/*` | R-3 | 45 executed, 0 failed/skipped | 45 | 4 |
| CP-12 | S1-S2 | CP-1 | portable-unit-scheduler | `/*/*/RunSchedulerTests/*` | R-4 | 17 executed, 0 failed/skipped | 17 | 2 |
| CP-13 | S1-S2 | CP-1 | portable-unit-timeouts | `/*/*/TimeoutTests/*` | R-4 | 8 listed; off Windows 7 executed and `windows_quick_row_finishes_beside_a_slow_row` skipped; 0 failed | 7 | 2 |

Red evidence: commit each slice's witness first and run only its row (`--rows CP-1`, `--rows CP-2`, or
`--rows CP-1,CP-2` for one shared red commit) expecting exit 1 with the red assertion texts above in the
row console log; then commit the fix(es) and run the whole table once. Review repeats the final required
selection with fresh provenance and reads `cleanupRefusals: []`, `cleanedOutputs: true` and the absent
`bin-c1368` in W-1's run folder, not exit codes alone.

### Cost

Execution: one `Antiphon.Tests` build (about 8 minutes) plus about 27 row-minutes, two rows at a time on
Linux: roughly 20 minutes per full run, once green, plus one red run per slice (build-dominated, about 10
minutes each, or one shared red run). Authoring: S1 15 minutes, S2 20 minutes. S1 alone fits the 45-minute
budget; S1 + S2 is about 70 minutes including runs. No suite run is commissioned.

## Platform and dispatch

The platform routes are not reachable from this container by design (see the ground-truth table); the last reading on
this fleet is `globalRunnerId: server2` with `desktop` (windows), `server2` and `server2-temp` (linux).
W-1 and W-2 are portable (fresh owner-only root, driver-observed exit, Linux statx or Windows change time),
and the production change has no OS-specific branch, so dispatch Code and Review unpinned: omit `-Runner`
and `-Platform`. The failing lane was the Windows desktop; after the land, a re-run of the CARD-1359 CP-2
row there is a confirmation the orchestrator may ask of that task, together with the `fsutil hardlink list`
measurement in the ground-truth table, not a checkpoint row here.

## Answers to the brief

- **Why TestResults escapes the ledger.** The ledger is sealed after the build and before the row; the
  row's own host writes its results into the run folder, but the child TUnit host the E2E fixture launches
  has no `--results-directory`, and the testing platform's default is beside the test DLL, inside the
  sealed root. Every post-seal entry without a creation record refuses by design.
- **Should contained refusals invalidate the source receipt?** No in principle (D-4): source cleanliness
  and output hygiene are independent verdicts. Changing it is a validator and receipt contract change
  that does not fit this card; file the sibling with D-4's sketch.
- **Smallest safe fix.** S1 (one ledger method, one call site). It is not sufficient for the E2E lane on
  its own because of the 64 MiB content-stamp cap, which S2 lifts for stamps only.
- **Land order and rc.** After CARD-1358 (the branch contains it; S2 touches its reader). Master-only now;
  rc-worthy under the Fix rule (D-6).

## Completion and handoff

Land S1 and S2 to master through the normal pipeline after CARD-1358. Review must read the W-1 run folder
and the W-2 assertion texts, not exit codes alone. The next stage is **code**; this section is the
verification design, so TestDesign is not a separate stage unless the orchestrator wants a second reading
of the witness table.
