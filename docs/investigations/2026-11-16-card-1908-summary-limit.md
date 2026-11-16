# CARD-1908: Full-summary entry-limit investigation

2026-11-16. Investigate task `cf58c961-d72c-412a-998c-d8359003160f`, assigned branch `feat/card-task-cf58c961`. **Parser mechanism confirmed with Linux scratch reproduction; failure of an actual Full-run summary remains unconfirmed.** Seven cases matched expectations, including two expected refusals. Next: **decide**, as commissioned; no input contract change is selected here.

## Source and stored-evidence provenance

- Exact HEAD and fetched `origin/master`: **`48a4615dcb4d67033886a3e31629dc7351ed0852`**. `git fetch origin master` completed; both identities remained equal. The scratch run used the unmodified extractor at this commit.
- Extractor read: `scripts/lib/rc-historical-stream.cs`, Git blob `8b75a7d42290f13eb6b853f53582d33e08812f0a`, file SHA-256 `bad4ad576a3e9fb566d3729a67c82f688834984809580ab00ab5067b479c8ba7`; wrapper `scripts/lib/rc-historical-evidence.ps1`, Git blob `1d32d8d57319c07a2df803dba49777b303ad78e8`, file SHA-256 `d8a91bef8261364164efbb9bb49571927294f277bcebff94f7a9d81cb85c2493`.
- Full card retrieved with `pwsh -NoProfile -File scripts/card.ps1 get CARD-1908 -Board Antiphon`: ID `5cde3eca-9ba9-4b41-b4dd-7b8dbdcb7fa2`, board `8988ca03-7414-47ad-b0b6-51556c701703`, title “S5a historical extractor cannot ingest a Full-run summary.json (1,000,000-entry limit; 850 MB)”. Its description explicitly says **“not run to confirm”**. `history CARD-1908 -Board Antiphon -Json` returned revision 1, ID `01a1e588-2765-7c63-a421-67eb76be5933`, Move Backlog → InProgress at `2026-11-16T18:04:27.877616Z`, reason “Task cf58c961 (Investigate, High) dispatched against this card.” No content revision establishing an actual summary failure was returned.
- Survey read from `origin/feat/card-task-ae89df49` = `e992ac4572313ca17790e94f419f9a4b2d58a2e7`, `docs/investigations/2026-11-16-server2-refill-survey.md:24`, `:83`, `:170`: bounded extraction succeeded; investigate the untested summary concern before changing the contract.
- Original report read from `origin/feat/card-task-ddbdc4f3` = `08dbfa38faab81a604d992b3a731802cfa5daf22`, `docs/investigations/2026-11-13-run-97150a0b-final.md:12` and `:115`–`:140`. It identifies closed Windows run `97150a0b03454892a10a086837fe8058`, source `ab7af12c699039270af7e22a5d7fd794035bc325`, summary **890,419,287 bytes**, SHA-256 `297affe03a5f3449956a227c193878bb928c5ee8fdb7ac27551128255d52f125`. Its predicted scanner refusal was explicitly never executed. Its separate Python line-reader result (19.8 MB peak) is not an S5a parser result.

The brief's **about 960 MB** is the relevant Full-summary scale, not a fresh measurement of active run `a21d8174`. The older report's 890,419,287 bytes is a different, precisely identified file. Neither bytes nor line count establishes the JSON-value count. The active run's final summary is unavailable until the run ends. No desktop, live Windows root, running qualification, setting, source file or generated index was touched.

## Confirmed mechanism

`EntryLimit` is 1,000,000 (`scripts/lib/rc-historical-stream.cs:25`). Three guards can report `evidence-entry-count`:

| Guard | Count and scope | Source in `rc-historical-stream.cs` |
|---|---|---|
| JSON traversal | Each call to `Value`, including root, objects, arrays, ignored nested scalars and array elements; per JSON file. Property names are not values. Refuses at value 1,000,001. | `:308`, `:325`–`:356`; new scanner per file at `:165` |
| Normalized output | Each emitted JSON scalar or TRX row; cumulative across the whole export. | `:137`, `:156`–`:169`, `:285`–`:293` |
| Retained names | More than 8 MiB of simultaneously retained property-name characters across open objects. | `:347`–`:357` |

Only whitelisted **top-level** JSON scalar fields emit rows (`scripts/lib/rc-historical-stream.cs:31`–`:35`, `:342`, `:356`). Nested ignored values still consume traversal budget. Consequently an ignored array can refuse while emitting **zero normalized rows**. Large ignored strings instead consume one value each and are validated/skipped in blocks (`:386`–`:429`). The 4 GiB file cap (`:24`, `:245`) is separate.

The relevant producer shape can be reconstructed: each chunk row retains census exclusions (`scripts/lib/nightly-tests-impl.ps1:1594`–`:1603`), while the terminal summary retains `timingChunks`, `suites`, and, when prepared, another chunk-row projection under `dispatchProgress.suites` (`:2244`, `:2261`, `:2267`). Actual exclusion rows contain uid, class, method, categories, disposition, reason and owner (`scripts/lib/Antiphon.Nightly/NightlyCore.cs:343`–`:353`). Repeated metadata can therefore consume many values independently of log-string size. This does **not** establish the count in an actual summary. The scratch shape deliberately reduces exclusion rows to two properties; it is not an exact Full-run fixture.

The D-3 plan requires freezing summary bytes and bounded token traversal, and describes a **normalized obligation cap** of one million (`docs/superpowers/plans/2026-11-12-card-1836-incremental-activation-plan.md:62`, `:77`). The implemented scanner additionally caps ignored structural values. This is the concrete distinction requiring a contract decision; this investigation does not select a change.

## Linux reproduction and measurements

PowerShell **7.5.4**, **.NET 9.0.10**. A fresh process loaded the existing C# using the production wrapper (`scripts/lib/rc-historical-evidence.ps1:3`–`:17`). Inputs and bundles were created outside the checkout under `/tmp/card-1908-cf58c961`. Inputs were generated in repeated streaming blocks. Successful exports were explicitly reverified against their inventory digest; every selected input SHA-256 remained unchanged before/after extraction.

Command (one host build-slot lease; no solution build or TUnit suite):

```sh
pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1908-bounded-witness -- pwsh -NoProfile -File /tmp/card-1908-cf58c961/witness.ps1
```

Essential execution transcript, unedited:

```text
BUILD SLOT granted lease=4b843538-0d7e-429e-9498-5d34b9b11028 waited=0s maxcpucount=6
BUILD SLOT warning: maxcpucount=6 is not applied to pwsh: a dotnet build inside it runs at the default node count unless it passes -maxcpucount:$ANTIPHON_BUILD_SLOT_MAXCPUCOUNT; lease each build separately, never one lease around a loop
C1908 cases=7 failures=0 root=/tmp/card-1908-cf58c961/run-1699277783724bb9860cf4b18506e771
BUILD SLOT released lease=4b843538-0d7e-429e-9498-5d34b9b11028 held=45s
```

Driver exit **0**; **seven scratch cases, zero unexpected failures**, including two expected refusals. This is not an ordinary qualification certificate. The wrapper warning concerns a non-dotnet command; the child invoked Add-Type and extraction, with no nested dotnet build.

| Case | Selected bytes | JSON values | Emitted rows | Observed result | Export + explicit verify seconds |
|---|---:|---|---:|---|---:|
| array-at-limit | 2,000,009 | 1,000,000 | 0 | valid | 0.755 |
| array-over-limit | 2,000,011 | 1,000,001 | 0 | evidence-entry-count | 0.652 |
| exclusions-at-limit | 10,333,321 | 1,000,000 | 0 | valid | 1.208 |
| exclusions-over-limit | 10,333,352 | 1,000,003 total if completed | 0 | evidence-entry-count | 0.848 |
| two-files-over-total | 2,400,026 | 600,002 each / 1,200,004 total | 0 | valid | 0.475 |
| large-ignored-string | 960,000,045 | 4 | 2 | valid | 20.750 |
| selected-trx-sidecar | 297 | small | 7 | valid | 0.033 |

Count derivations and controls:

- `{"ignored":[0,...]}` has root object + array + N zeros = **N+2** values. N=999,998 succeeds; N=999,999 refuses.
- `{"suites":[{"exclusions":[{"uid":"x","reason":"profile"},...]}]}` has four enclosing containers plus three values per exclusion = **4+3N**. N=333,332 succeeds at one million; N=333,333 refuses on traversal value 1,000,001.
- Two files with 600,000 zeros each succeed despite 1,200,004 total values. This demonstrates the per-file parser reset. The export-wide emitted-row cap is established by source inspection; a million-row TRX stress test was not run.
- The 960,000,045-byte control contains four values: root, runId, ignored stdout, complete. It emits two rows and verifies. This is a real-size synthetic **low-value-count** shape, not the actual Full-summary shape.
- The selection control includes one TRX result, one TRX method, four timing scalars and one dispatch scalar. An intentionally malformed unselected `summary.json` is never read. This confirms explicit `RelativePath` behavior, not permission to exclude summary from the importer contract.

Both refusals left **no terminal inventory** and zero rows. Freeze completes before parsing (`scripts/lib/rc-historical-stream.cs:145`–`:166`); inventory publication follows successful parsing (`:181`–`:192`). Refused outputs were retained as partial evidence, never represented as verified bundles.

All five successes reported `maxBufferedBytes=524288`. Highest cumulative process peak working set was **245,121,024 bytes (233.8 MiB)**; the large-string case reported 240,025,600 bytes. These include PowerShell/compilation and are cumulative process high-water marks, not proof that `maxBufferedBytes` includes every allocation or a prediction of Windows peak. No timeout, retry or assertion was adjusted.

Scratch receipts: `/tmp/card-1908-cf58c961/run-1699277783724bb9860cf4b18506e771/receipts.jsonl`, SHA-256 **`ca0366895c1a77f3907a796e6de376864b7b6b7226b79f60c8067e8b0e0fded5`**. Each named row includes input lengths/digests, inventory presence/digest, verification, source stability, time and process peak. Generated payloads/JSON remain external and uncommitted; mirror retirement may remove them. The complete script below preserves reproducibility independently of scratch retention.

| Input summary | SHA-256 |
|---|---|
| array-at-limit | `89fa7dcc6d452b6bdad2f58938c0e42dbd51a2035b253761e43b174ab661527b` |
| array-over-limit | `e1d9e78bb4ad74385599d5169964ca7a183ec4e6f5bcbfd8f4d99a2ca7d9d794` |
| exclusions-at-limit | `7d8dbd9b6d4c0ae409db6cd58f0eb4f992d6908a01849faff7c2e39b739e5bc5` |
| exclusions-over-limit | `35c291ec9010ffe6a753a62ce54f510f78844a1cfcbe6a7f60ab2bf19bfe0015` |
| large-ignored-string | `d5f7ba346d8cfab9dd385a339a3b35214265c201da0c92bac79e4302534b2b29` |

## Extraction is separate from admission

The stored Windows report already establishes bounded **TRX + timing sidecars + dispatch schedule** extraction: 2,327 files, 85.9 MB, 57,217 rows, inventory SHA-256 `395db24cd2ebf8d9361b08490561d0d10d0bd9b81c8e6882c0e5078a43cb97f5`, export/verify valid, 72.3 s wall and 193.6 MB peak (`2026-11-13-run-97150a0b-final.md:117`–`:138` at the pinned report commit above). That history is retained, not rerun or replaced by the tiny scratch control. The report's stronger statement at `:140` exceeds what its unrun summary inference established.

The importer invokes export **without `RelativePath`**, attempting the whole root (`scripts/rc-ledger-import.ps1:16`). Parser acceptance is not historical provenance admission or carry eligibility. Current importer reports `admitted=0`, `forceFull=true`, `gateCredit=false` (`:45`–`:48`), and algorithm coverage remains incomplete (`scripts/lib/rc-admission.ps1:31`–`:34`). Successful selected extraction cannot establish a Windows Full anchor, source/build/launch joins or pass credit.

## Evidence needed after the real run ends

The terminal summary is written after terminal projection/timing collection (`scripts/lib/nightly-tests-impl.ps1:2190`–`:2228`, `:2279`). The brief says live Windows run **a21d8174** holds the desktop and its summary is unavailable until run end. Its reported `rc/2026-11-23` / source `9b98828eb34eb552ce62bcde5a64dd045a872a88` remain caller-supplied identities, not inspected here. No observation of that live root was necessary for these scratch witnesses.

After the run is terminal, a separately commissioned read of closed evidence should record:

1. Full resolved run ID, candidate/source SHA, terminal outcome, writer completion, summary path, exact bytes and SHA-256. Confirm stable identity/length/change state and digest around the read/copy. For the older 97150a0b allegation, use its specifically identified file/digest rather than substitute a different run.
2. An independent **bounded streaming census** of valid UTF-8 JSON matching production `Value` semantics: root/container/scalar totals, property count separately, and contributions from `suites`, `timingChunks`, `dispatchProgress.suites` and every `exclusions` subtree. Record path and byte offset of traversal value 1,000,001 if present. Also measure depth, maximum captured scalar/property-name sizes and maximum simultaneously retained name characters; those guards can preempt or share the refusal label. Count prospective emitted top-level scalar rows separately. Bytes and lines alone do not resolve this.
3. The unchanged, source-pinned production export selecting **only that genuine summary**, from a stable retained copy into a new external owned root. Record error or valid inventory + independent reverify, unchanged input checks, emitted rows, inventory presence, freeze/parse/verify/wall times and process peak. Correlate any `evidence-entry-count` with the census to distinguish traversal, name retention and emitted rows.
4. A separately identified **whole-root** extraction/import measurement before claiming importer readiness: file count/bytes, per-file value maxima, cumulative emitted rows, first failing file/guard, and exact original-run bindings. Summary-only success cannot rule out another JSON/file-count/row/deadline failure; selected TRX success cannot certify whole-root extraction.

A digest-bound genuine file with a valid census crossing the guard and matching production refusal confirms the actual-summary allegation. A valid export/reverify with complete census below the bounds refutes it for that identified file, leaving other runs unmeasured. A refusal at another guard establishes that failure first; its error alone does not confirm the traversal-count claim.

Remaining uncertainties: actual-summary census/export not performed; active-run final size/shape unknown; native Windows paths/parser/memory not witnessed; whole-root import unmeasured; retained-name and aggregate emitted-row thresholds inspected but not stress-tested; historical provenance/admission and activation obligations remain separate. The **mechanism** is confirmed; the universal title “cannot ingest a Full-run summary” is not established.

## Not done, noted

No fix designed or implemented; the card's summary-inclusive versus selected-evidence contract choice remains for Decide after the actual-summary measurements.

## Reproduction script

Save the script below unchanged as `/tmp/card-1908-cf58c961/witness.ps1` (create the parent first), then run the gated command above. SHA-256: `b2df655e85273030b7ff048b011bb0f8fc9dcc5f3f32158b91e35837cea0f35f`. Each run creates new GUID outputs. Override `-Checkout` for a different Linux checkout. Peak disk includes the 960 MB source and its frozen copy. This witness already completed; the following is its rerun recipe.


```powershell
param([string]$Checkout='/work/worktrees/ws-04', [string]$EvidenceParent='/tmp/card-1908-cf58c961')
$ErrorActionPreference='Stop'
$root=Join-Path $EvidenceParent ('run-'+[guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($root)
. (Join-Path $Checkout 'scripts/lib/rc-historical-evidence.ps1')
$utf8=[Text.UTF8Encoding]::new($false)
function Write-Repeated($Path,$Prefix,$Item,[int]$Count,$Suffix) {
    $w=[IO.StreamWriter]::new($Path,$false,$utf8,65536)
    try {
        $w.Write($Prefix)
        $batch=[Math]::Max(1,[Math]::Min(1000,[int][Math]::Floor(1000000/$Item.Length)))
        $block=$Item*$batch
        while ($Count -ge $batch) { $w.Write($block); $Count-=$batch }
        if ($Count) { $w.Write($Item*$Count) }
        $w.Write($Suffix)
    } finally { $w.Dispose() }
}
function Array-File($Path,[int]$N) { Write-Repeated $Path '{"ignored":[' '0,' ($N-1) '0]}' }
function Exclusions-File($Path,[int]$N) {
    Write-Repeated $Path '{"suites":[{"exclusions":[' '{"uid":"x","reason":"profile"},' ($N-1) '{"uid":"x","reason":"profile"}]}]}'
}
$cases=@(
    @{name='array-at-limit';kind='array';n=999998;values='1000000';expect='valid'},
    @{name='array-over-limit';kind='array';n=999999;values='1000001';expect='evidence-entry-count'},
    @{name='exclusions-at-limit';kind='exclusions';n=333332;values='1000000';expect='valid'},
    @{name='exclusions-over-limit';kind='exclusions';n=333333;values='1000003';expect='evidence-entry-count'},
    @{name='two-files-over-total';kind='split';n=600000;values='600002 each / 1200004 total';expect='valid'},
    @{name='large-ignored-string';kind='large';n=960;values='4';expect='valid'},
    @{name='selected-trx-sidecar';kind='selected';values='small';expect='valid'}
)
$failures=0
foreach ($case in $cases) {
    $src=Join-Path $root ($case.name+'-source'); [void][IO.Directory]::CreateDirectory($src)
    $paths=@('summary.json')
    switch ($case.kind) {
        array { Array-File (Join-Path $src 'summary.json') $case.n }
        exclusions { Exclusions-File (Join-Path $src 'summary.json') $case.n }
        split {
            $paths=@('a.json','b.json')
            foreach ($p in $paths) { Array-File (Join-Path $src $p) $case.n }
        }
        large { Write-Repeated (Join-Path $src 'summary.json') '{"runId":"large","stdout":"' ('x'*1000000) 960 '","complete":true}' }
        selected {
            [IO.File]::WriteAllText((Join-Path $src 'summary.json'),'{"ignored":[0,]}',$utf8)
            [IO.File]::WriteAllText((Join-Path $src 'chunk.trx'),'<TestRun><Results><UnitTestResult testId="a" outcome="Passed" /></Results><TestDefinitions><UnitTest id="a"><TestMethod className="Fixture.Tests" name="Runs" /></UnitTest></TestDefinitions></TestRun>',$utf8)
            [IO.File]::WriteAllText((Join-Path $src 'chunk.timing.json'),'{"suite":"antiphon","chunk":"one","runWallSeconds":1.25,"lastPhase":"complete"}',$utf8)
            [IO.File]::WriteAllText((Join-Path $src 'dispatch-schedule.json'),'{"runId":"fixture"}',$utf8)
            $paths=@('chunk.trx','chunk.timing.json','dispatch-schedule.json')
        }
    }
    $inputRows=@(foreach ($p in $paths) { $f=Get-Item -LiteralPath (Join-Path $src $p); @{path=$p;bytes=$f.Length;sha256=(Get-FileHash -LiteralPath $f.FullName -Algorithm SHA256).Hash.ToLowerInvariant()} })
    $dest=Join-Path $root ($case.name+'-bundle')
    $clock=[Diagnostics.Stopwatch]::StartNew(); $result=$null; $reason=''; $verified=$false
    try {
        $result=Export-RcHistoricalEvidence -SourceRunRoot $src -EvidenceRoot $dest -RepositoryRoot $Checkout -RelativePath $paths
        $verified=(Test-RcHistoricalEvidence -EvidenceRoot $dest -ExpectedInventorySha256 $result.inventorySha256).valid
        $reason=if ($result.valid -and $verified) {'valid'} else {'invalid'}
    } catch {
        $exception=$_.Exception
        while ($exception.InnerException) { $exception=$exception.InnerException }
        $reason=$exception.Message
    }
    $clock.Stop()
    $rows=0; $reader=[IO.File]::OpenText((Join-Path $dest 'rows.jsonl'))
    try { while ($null -ne $reader.ReadLine()) { $rows++ } } finally { $reader.Dispose() }
    $unchanged=$true
    foreach ($inputRow in $inputRows) {
        if ((Get-FileHash -LiteralPath (Join-Path $src $inputRow.path) -Algorithm SHA256).Hash.ToLowerInvariant() -cne $inputRow.sha256) { $unchanged=$false }
    }
    $inventory=Test-Path -LiteralPath (Join-Path $dest 'inventory.json')
    $ok=($reason -ceq $case.expect -and $unchanged -and ($inventory -eq ($case.expect -ceq 'valid')))
    if (-not $ok) { $failures++ }
    $receipt=[ordered]@{name=$case.name;ok=$ok;expected=$case.expect;actual=$reason;jsonValues=$case.values;inputs=$inputRows;rows=$rows;inventory=$inventory;verified=$verified;sourceUnchanged=$unchanged;elapsedSeconds=$clock.Elapsed.TotalSeconds;maxBufferedBytes=$(if($result){$result.maxBufferedBytes}else{$null});inventorySha256=$(if($result){$result.inventorySha256}else{$null});processPeakWorkingSetBytes=[Diagnostics.Process]::GetCurrentProcess().PeakWorkingSet64;evidenceRoot=$dest}
    $line=ConvertTo-Json -InputObject $receipt -Depth 6 -Compress
    [IO.File]::AppendAllText((Join-Path $root 'receipts.jsonl'),$line+"`n",$utf8)
    Write-Output $line
}
Write-Output "C1908 cases=$($cases.Count) failures=$failures root=$root"
exit $(if($failures){1}else{0})
```

Standing authority covered this bounded Linux investigation and branch publication; no approval was requested. Only this Markdown report is committed; no source, index, contract or operational change was made.

--- next stage ---
next: decide
handoff: Parser value-count mechanism reproduced; actual Full-summary failure remains unconfirmed. After a21d8174 ends, bind the genuine summary digest, stream a structural census and run the unchanged summary-only exporter before choosing a contract change. Preserve separate whole-root and provenance/admission obligations.
artifact: docs/investigations/2026-11-16-card-1908-summary-limit.md
