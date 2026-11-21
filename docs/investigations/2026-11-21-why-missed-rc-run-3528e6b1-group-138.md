# Why missed: rc 3528e6b1 group 138

The seeded Windows Full run of `rc/2026-11-30` at
`bd34a7ff6da9db1a3fa3690f824890ef9b7cc2c0` reported two failures in
`antiphon-all-group-138.trx`. Both repairs change test fixtures.

## Introducing commits

- CARD-2005 introduced the `FlushFailure` witness in
  `6ab139c48662677c3d99b6c129bf1551e1cfe193`. The fixture used `chmod 500`
  on a directory holding two old evidence files. Windows failed at that
  command in 1.9 s, before either deferred write was queued.
- CARD-1836 S3a introduced `ConfigFixtureScriptAndLockInputs` and its
  45 s script budget in `907ea2ba316c61fe505e287cdef9fe3eee14c478`.
  `929b75dd49dd55979a5921bb0bb9c8e02e5d6059` added valid project XML and
  the known-closure assertion. The Windows test reached 45.5 s.
  At the execution deadline the root and both output streams were still
  pending. The case emits its first assertion after all manifest comparisons.

## Approving reviews

- CARD-2005 Review `4bd63eb8` was Final/Full on server2 Linux at
  `b137f78fdbb9edc125a30e43c4cd8fed7b59b012`. CP-1
  `/*/*/AstraFixesBTests/*` passed 3/3. Its checkpoint report remained red
  with an expected guard skip and a merge-tree pin conflict. Follow-up
  Review `d5263722-a14a-43ed-95c3-85b14d1b20cf` was Final/Full at
  `a85fe1b83685b523c435ac5cb1b765f72137256c`. The same Linux class
  filter passed 5/5 as part of 12 validated checkpoint rows.
- CARD-1836 S3a Review `16c6dbc2` ran `/*/*/RcInputManifestTests/*`
  on Linux, 4/4, and held the land for a separate fanout witness defect.
  Round 2 `ebfb9548` approved the diff at
  `9ec9b850cbbd79dd9bfe180f60d2dae6dde1416e`, with all three review
  checkpoints validated. Linux CP-2 reran the class, 4/4 in 41.8 s
  including test-host overhead.

## Outside the selection

Both tests were selected as Integration tests. Their approving executions
used Linux. The Astra fixture reached a working Unix permission command.
The manifest fixture fit its per-child deadline on Linux despite serial Git
work for ten modified inputs, an addition, a deletion, and a historical
re-read. The manifest case uses only its scratch Git repository.

## Reproduction and repairs

- Masking `chmod` with a throwing PowerShell function made the rc-base
  `FlushFailure` exit 1 before any assertion. Fix
  `fcff1b6e4e81484e07e9a6f7408eceff07788957` uses the journal fixture's
  existing `BeforeWrite` seam to throw an `IOException` for both queued
  writes. It verifies both old files persist, clears the seam for the retry,
  and verifies the replacement landed on disk. The script retains its
  PowerShell 7 host requirement and ASCII source.
- On Linux with PowerShell 7.5.4, the rc-base manifest case passed 1/1 in
  11.7 s with LF inputs and 11.8 s with CRLF inputs in a scratch archive.
  Git Trace2 recorded 198 child starts for each run. At master
  `b5690352bb0a4467542cce83bb833820477eff5a`, the CRLF case passed 1/1
  in 18.3 s with 198 Git starts. These are single samples under varying
  host load.
- The rc source uses the pre-CARD-2009 attribute-count scanner. The
  verbatim-string fix `d32f2d70b0155fa9c8c0d600823364dc8af96971` is
  present on the sampled master and absent from the rc tip. The fixture
  owner map omits `testOnly`, so both revisions bypass test identity
  scanning in this case. As a separate master check, a Git fixture
  committed LF and CRLF copies of `ContractSnapshotTests.cs` from
  `tests/Antiphon.E2E` (75,941 and 77,397 bytes). A method rename
  returned true and a body-only edit returned false in both versions.
  `Test-RcTestIdentityChange` took 4.24/1.75 s for LF and 1.53/1.51 s
  for CRLF, in rename/body order.
- Adding 230 ms before each fixture Git invocation on Linux made the
  original CRLF manifest case time out at 45 s (167 starts by timeout,
  exit 124, zero assertion rows). The native Windows run has no child
  timing breakdown, so this establishes sensitivity to process latency.
  Fix `a0f56eda2efcc12e87a9cec601bef796aca637c9` gives each of three
  fixture families its own unchanged 45 s budget. It still checks each
  input individually and makes each family edit the sole change in its
  comparison. The add/delete and historical checks run in
  `ConfigAndFixtures`. Under the same CRLF and latency probe that family
  passed 1/1 with 93 Git starts, holding its build slot for 32 s.

## Cheapest pre-land check

Run the following impacted selections on Windows with the intended
Git-for-Windows and PowerShell versions. The Linux selections remain useful
for functional coverage.

| Path glob | TUnit method filter |
|---|---|
| `scripts/test-astra-fixes-b.ps1` | `/*/*/AstraFixesBTests/FlushFailure` |
| `scripts/test-rc-input-manifest.ps1` | `/*/*/RcInputManifestTests/ConfigFixtureScriptAndLockInputs*` |
| `scripts/lib/rc-input-manifest.ps1` | `/*/*/RcInputManifestTests/ConfigFixtureScriptAndLockInputs*` |

## Linux verification

One gated build and a combined class run at
`a0f56eda2efcc12e87a9cec601bef796aca637c9` used the default project outputs
and `ANTIPHON_LINUX_COMPILER_POLICY=0`. Build admission load was 11.96.
The fresh TRX recorded 5/5 Astra cases and 6/6 manifest cases, with zero
failures or skips. TUnit wall time was 102.2 s. `FlushFailure` took 7.5 s.
The three manifest families took 11.4 s, 9.0 s, and 7.5 s for
`ConfigAndFixtures`, `ScriptAndBuildInputs`, and `ClientContainerAndDocs`,
respectively.

## Windows rerun owed

Use one gated Windows build and one gated TUnit invocation on the integrated
repair tip, after run 3528e6b1 releases the native lock. Expect 5 Astra
and 6 manifest cases, including all three fixture families. Record a fresh
TRX and the built SHA.

```powershell
$results = Join-Path $env:TEMP ('group138-' + [guid]::NewGuid().ToString('N'))
pwsh -NoProfile -File scripts/build-slot.ps1 -Label group138-windows-build -- dotnet build tests/Antiphon.Tests -p:UseAppHost=false --nologo
pwsh -NoProfile -File scripts/build-slot.ps1 -Label group138-windows-tests -- dotnet run --project tests/Antiphon.Tests --no-build -p:UseAppHost=false -- --treenode-filter '/*/*/(AstraFixesBTests*)|(RcInputManifestTests*)/*' --timeout 570s --report-trx --report-trx-filename group138.trx --results-directory $results
```

## Exemption

None.
