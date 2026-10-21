# CARD-1391 RC harness follow-ups

Plan for the four CARD-1350 harness follow-ups. Inspected at origin/master
`569cdd327612ee23583119802e815f0559154984`. That commit and origin/rc/2026-10-20
`aeaf16647` have identical `scripts/lib/nightly-coverage.ps1` and
`scripts/lib/nightly-tests-impl.ps1`. One local probe
(`/tmp/c1391-probe.ps1`, deleted after the read) dot-sourced the coverage
script at that master tip. `GET /api/runner-defaults` and
`GET /api/session-runners` on `127.0.0.1:17202` were refused (connection
refused). Checkpoints name no host and no OS.

## Decisions

D-1. `Test-NightlyClassAssigned` and `Test-NightlyRequiredClassesPresent`
match the namespace-qualified bare name only. Bare means
`Get-NightlyBareClassIdentity`: a constructor suffix is removed only when
`(` is past the first character and the name ends with `)`. Drop the short-name
candidate and the `wantSimple` comparison in `Test-NightlyClassAssigned`
(about lines 640 and 650-654) and the short-name index and `wantSimple`
acceptance in `Test-NightlyRequiredClassesPresent` (about lines 981 and
987-990). `Antiphon.Tests.Application.Widget(Factory)` matches required
`Antiphon.Tests.Application.Widget`. `Antiphon.Tests.Agents.Widget` does not.
An unqualified required name does not match a namespaced class.

The empty-required synthesis in `ConvertTo-NightlyNativeSuiteVerdict` (about
lines 1129-1137) must pass each discovery node's bare `ClassName`, not the
short `Type`. Otherwise the default lane either collapses both
`SpecialistToolPolicyTests` classes into one short name, or, after D-1,
reports every namespaced TRX row missing. Do not change
`Test-NightlyProfileNodeExcluded` or `Get-NightlyIdentityBagKey`.

Rejected: keep short-name equality. The card's collision is real
(`Antiphon.Tests.Application.SpecialistToolPolicyTests` and
`Antiphon.Tests.Agents.SpecialistToolPolicyTests`). The rc exclusion row is
the short name `SpecialistToolPolicyTests`, and the default lane passes no
required classes into `Test-NightlyClassAssigned`, so the bad match is not
reached today. A later required class with that short name would mark both
namespaces assigned.

D-2. Client lint stays a reported build step after `npm ci` and `npm run
build`. Profile `nightly` does not let a lint failure skip `dotnet build` or
the selected suites. Every other profile, including `rc`, still stops before
`dotnet build` and before every suite. `npm ci` and `npm run build` stay hard
stops on every profile.

Nightly lint failure: the `client lint` build row is `FAIL`, `reasons`
contains `client-lint-failed`, `testsPassed` is false, `coverageComplete` is
false, and the process exit is nonzero. Do not use
`prerequisite-failed:client lint` for that path. Leave the outcome at `BUILD`
only when the suites do not themselves set a later outcome. A suite failure
stays `TESTS`.

Gate lint failure: `lintOk` is false, the reason stays
`prerequisite-failed:client lint`, the outcome is `BUILD`, and the launch
record has no `Antiphon.sln` and no suite command.

Rejected: lint always stops the pipeline. That is today's behavior
(`nightly-tests-impl.ps1` about lines 489-515 and the suite `break` on
`lintOk`), and it is why a lint-red night has no .NET results. Rejected: lint
never stops, including a gate run. `docs/release-gates.md` still blocks
qualification on the same client lint failure that reddens CI, and the
2026-10-21 qualification has to stop. Rejected: skip lint on nightly. The
failure must still be in the summary.

D-3. A second structural record on the same line is split, not dropped.
`Get-NightlyDiagnosticRecords` starts a new record at every
`TestNodeUpdateMessage { ... TestNode = TestNode { Uid = TestNodeUid`,
including when that envelope is not at the start of the line. A bare
`TestNode { Uid` inside explanation text does not start a record.

Two complete records on one line parse as two nodes. Discovery keeps both
uids and types. Execution keeps both uids and each record's own outcome.
A truncated record spliced onto a complete record throws the existing
truncated or malformed error and returns no node. It must not borrow the
following record's type.

Rejected: always throw when two envelopes share a line. Discovery already
does that for two complete records (`malformed discovery record: missing
state`) and loses both. Rejected: keep the first record. Execution currently
returns one node and forces it to `Failed`.

D-4. A captured TypeName is a node only when it is a complete identifier with
optional balanced constructor arguments. Refuse `X(Fac` and a lone comma
before the node is returned, with the existing `truncated diagnostic record`
error. Accept `X(Fac)`, `CardFilePolicyApiTests(CardFileSyncEndpointWebAppFactory)`,
and ``Outer+Inner`1(FactoryA, FactoryB)``. Do not remove comma from
`$script:NightlyDiagnosticTypeNameClass`; constructor arguments contain
commas. The check runs after the capture, with `IndexOf` and a balance count,
so Windows PowerShell 5.1 can parse it.

Rejected: a narrower character class that forbids `(` or comma. That rejects
the fixture and generic names CARD-1350 already accepts.

D-5. Land on master first. Backport is required. The qualification checkout
runs these scripts from the rc tip, and origin/rc/2026-10-20 `aeaf16647` has
the same two files as master. A master-only land does not update that
checkout. The backport is a later publication task onto the `rc/YYYY-MM-DD`
line that qualification runs (today `rc/2026-10-20`). This plan does not
merge to master or rc. Older tips `rc/2026-10-18` `81cfb267a` and
`rc/2026-10-15` `61117dd16` are not the current line.

D-6. The four new cases are `-Case` only. They are not added to the default
loop in `scripts/test-nightly-tests.ps1`. `ExpectedRows` stays 80.
`Test-C1044_UnattendedCases` stays unchanged. No new TUnit wrapper. No PC
rows. New script text is ASCII and uses no PowerShell 7-only operators.

## Ground truth

Probe at master `569cdd327`. `sameNs` is required
`Antiphon.Tests.Application.Widget` against class
`Antiphon.Tests.Application.Widget(Factory)`. `otherNs` uses
`Antiphon.Tests.Agents.Widget(Factory)`.

| Item | What the card assumes | What the code does | Probe |
|---|---|---|---|
| A1 | Short-name match ignores namespace. Fix is the qualified bare name. | Both functions treat the last dotted segment as equal. | `sameNs=True`, `otherNs=True`, unqualified required name `True`, other-namespace presence `Ok=True`, same-namespace suffix presence `Ok=True`. |
| A2 | Lint failure skips dotnet and every suite. Nightly should still run suites. A gate run should still stop. | After `npm ci` and `npm run build`, lint failure sets `lintOk` false. Dotnet runs only when `npmOk -and lintOk`. The suite loop breaks when `lintOk` is false. | Source inspection of lines 484-515 and 802. No second process run. |
| A3 | Two records on one line drop one record. Split or fail loudly. | The splitter is line-anchored, so one line is one record. | Two lines: 2 nodes. One discovery line: throw `malformed discovery record: missing state`, raw records 1. One execution line: 1 node, outcome `Failed`, second uid gone. Truncated uid spliced onto a complete discovery record: 1 node, uid `truncated`, type taken from the second record. |
| A4 | Parser accepts complete-but-odd names ``X(Fac`` and a lone comma, which fail later. Refuse unbalanced names at parse time. | The TypeName class includes `(` and comma, and the field ends at `, MethodName = `. | Discovery nodes: type `X(Fac`, type `,`, type `X(Fac)`, generic ``Outer+Inner`1(FactoryA, FactoryB)``. |

## Slices

S1. Red harness cases only, in `scripts/test-nightly-tests.ps1`. No production
edit. One commit. Each `-Case` exits nonzero on the unmodified script.

- `Test-C1391_ClassMatch`: `otherNs` is false, the unqualified required name
  is false, `sameNs` is true, and
  `Test-NightlyRequiredClassesPresent` for bare
  `Antiphon.Tests.Application.Widget` plus
  `Antiphon.Tests.Agents.Widget` against a TRX row of only the Application
  class is not ok, with the Agents class missing. The suffix form of the
  Application class is present.
- `Test-C1391_LintGate`: reuse `New-C1341NpmFixture` and the
  `Test-C487_PrerequisiteGate` seam. `run lint` returns exit 2. Other
  launches return 0. `Suites = @('client')`. Profile `nightly` records
  `client-lint-failed`, `testsPassed` false, and launches `Antiphon.sln`
  and `test-client.ps1`. Profile `rc` records
  `prerequisite-failed:client lint` and launches neither.
- `Test-C1391_SplicedRecord`: two complete discovery records concatenated
  with no newline between them yield both uids. Two complete Passed
  execution records on one line yield both uids, both `Passed`. A truncated
  `TestNodeUpdateMessage` spliced onto a complete discovery record throws
  and does not publish uid `truncated`.
- `Test-C1391_OddTypeName`: `X(Fac` and a lone comma throw
  `truncated diagnostic record`. `X(Fac)` and
  ``Outer+Inner`1(FactoryA, FactoryB)`` still parse.

S2. Production edit, one commit, after the red commit.

- `scripts/lib/nightly-coverage.ps1`: D-1, D-3, D-4, including the bare
  `ClassName` synthesis.
- `scripts/lib/nightly-tests-impl.ps1`: D-2 only.

## Verification design

One red per item is the S1 `-Case` run, reported unlisted with reason
`red witness before the fix`. One narrow green is one checkpoint-tool run of
the command rows below after S2. Do not run the default 80-row harness and
do not run `Antiphon.Tests`.

- V-1: class match ignores a shared short name and keeps the suffix strip.
- V-2: nightly lint failure still launches dotnet and the client suite; an
  rc lint failure does not.
- V-3: a same-line pair of complete records parses as two nodes; a truncated
  splice does not borrow.
- V-4: `X(Fac` and a lone comma are truncated; balanced fixture names parse.
- V-5: `C1044_FixtureTypeName`, `C1044_FixtureClassChunks`, and
  `C487_PrerequisiteGate` stay green.
- R-1: one checkpoint-tool run of the rows below, exit 0,
  `buildSource=notApplicable`.

No PC-n rows.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Environment |
|---|---|---|---|---|---|---|---|---|---|
| CP-1 | S2 | n/a | class-match | `pwsh -NoProfile -File scripts/test-nightly-tests.ps1 -Case C1391_ClassMatch` | V-1 | exit 0 | n/a | 2 | n/a |
| CP-2 | S2 | n/a | lint-gate | `pwsh -NoProfile -File scripts/test-nightly-tests.ps1 -Case C1391_LintGate` | V-2 | exit 0 | n/a | 2 | n/a |
| CP-3 | S2 | n/a | spliced-record | `pwsh -NoProfile -File scripts/test-nightly-tests.ps1 -Case C1391_SplicedRecord` | V-3 | exit 0 | n/a | 2 | n/a |
| CP-4 | S2 | n/a | odd-typename | `pwsh -NoProfile -File scripts/test-nightly-tests.ps1 -Case C1391_OddTypeName` | V-4 | exit 0 | n/a | 2 | n/a |
| CP-5 | S2 | n/a | fixture-typename | `pwsh -NoProfile -File scripts/test-nightly-tests.ps1 -Case C1044_FixtureTypeName` | V-5 | exit 0 | n/a | 2 | n/a |
| CP-6 | S2 | n/a | fixture-chunks | `pwsh -NoProfile -File scripts/test-nightly-tests.ps1 -Case C1044_FixtureClassChunks` | V-5 | exit 0 | n/a | 2 | n/a |
| CP-7 | S2 | n/a | prerequisite-gate | `pwsh -NoProfile -File scripts/test-nightly-tests.ps1 -Case C487_PrerequisiteGate` | V-5 | exit 0 | n/a | 2 | n/a |

### Unlisted

The four S1 `-Case` runs are the red witness. Reason: a new assertion that
passes on unmodified code is a stub. They are not checkpoint rows.

### Cost

Ordinary floor is 14 minutes.
