# CARD-1540: land audit approves environment- and timestamp-gated graphs

Investigate stage, 2026-10-23, base `77eb7806d` (CARD-1528 recut on master). Evidence only.
Board check: `card.ps1 search` for `timestamp`, `PATH`, `land audit`, `ModifiedTime` and
`environment property` (all with `-All`) found no duplicate. Related but distinct cards:
CARD-1504 (target bodies), CARD-1498 (MSBuildProjectDirectory in props), CARD-1483/1528 (parents).

## Verdict

**Confirmed: the gap is real, and wider than `$(PATH)`.** The audit evaluates the graph once, in the
author's environment. The land reuses that verdict without evaluating. Any plain `$(Name)` that the
environment can supply, and any `%(ModifiedTime|CreatedTime|AccessedTime)` read, can decide
whether a docs input is in the graph. The audit approves both kinds of expression. So a graph that
is docs-independent at author time can depend on docs in the land host's environment, and a
docs-only land then skips the build and tests that would have caught a docs-driven failure.

`$(PATH)` on its own is only a symptom: changing it needs a function such as `.Contains`, and
those are refused (P5). The general hole is any undefined plain property, because MSBuild fills an
undefined property from an environment variable of the same name.

## Facts

1. **What the land compares.** `AgentTaskLandingProtocol.cs:306` calls
   `LandVerificationPolicy.DecideAsync`. It skips only when `diff-tree target..candidate` is
   ordinary `README.md`/`docs/**/*.md` blobs (`LandVerificationPolicy.cs:35-65`) **and** the
   SHA-256 of the candidate's definition-file `ls-tree` rows (mode, blob SHA, path) plus their count
   equal the embedded manifest (`LandVerificationPolicy.cs:71-95`,
   `LandVerificationBuildGraph.cs:133-153`). The manifest covers definition-file **text** only. It
   does not cover evaluated values, environment, working directory, host OS, SDK or timestamps.
   `evaluation.txt` (digest, SDK, pins SHA) is read only by `-Check`
   (`audit-land-verification-graph.ps1:133-141`), never at land. The land runs no evaluator (script
   header line 5). The skipped command is `dotnet build; dotnet run --project tests/Antiphon.Tests`
   in the server process's environment (`AgentTaskLandingProtocol.cs:311-313`).
2. **What the audit admits.** `EvaluationExpressionPolicy.cs:24,72` admits any `$(Name)` that matches
   `PlainProperty`. Item metadata is never inspected lexically, so `%(ModifiedTime)` and similar
   reads pass. The docs-timestamp refusal that the card mentions is not a timestamp rule: the
   docs **item** itself is refused (`SolutionAudit.cs:146-153`; P9). Docs-path checks run on
   evaluated items, in a single environment sample. An item whose `Condition` is false at author
   time is never examined.
3. **Evaluator cwd.** On Linux, evaluation-time well-known timestamp metadata of a relative item
   spec resolves against the **process working directory**, not the project directory. The wrapper
   starts the tool with cwd = repo root (`audit-land-verification-graph.ps1:33`). The audit therefore
   reads `""` where a project-directory evaluation reads the real time (P12).
4. **Directory items have empty timestamps.** A directory item (`docs`, `docs/sub`) has an empty
   `%(ModifiedTime)` (P7 `-getItem`), so the docs-directory-mtime channel is closed by MSBuild
   itself (seen on Linux; not re-checked on Windows).
5. **The real tree already has environment knobs at evaluation time** (census of non-Target
   evaluation text in 55 tracked definitions): `UsePublishedPackages` and `AntiphonMessagingVersion`
   (`samples/EchoGateway/EchoGateway.csproj:24-38`, which switches ProjectReference to
   PackageReference), `EnablePublicApiAnalyzers` (`src/Messaging.Pack.props:15-19`, which switches
   analyzer items), `AntiphonCleanFileMaxBytes` (`Directory.Build.targets:26`) and
   `PkgMicrosoft_Windows_Console_ConPTY` (`src/Antiphon.Agents.Pty/Antiphon.Agents.Pty.csproj:28`,
   defined only after restore). None of them reaches docs today. The tree uses no timestamp metadata.
   `$(OS)` gates already refuse as a missing pin (P3).

## Probe table

Probes use the real evaluator: `tools/Antiphon.LandGraphAudit` built once through the build slot
(Release, `bin-c1540/`, since deleted). Each fixture is a git repo with the real `global.json`, one
net9.0 project in `Antiphon.sln`, and `docs/a.md`. `-getItem` columns come from
`dotnet msbuild -getItem` run through the build slot, with cwd = the project directory.

| # | Evaluation-time body (in `app/App.csproj`) | Audit verdict | Graph varies? |
|---|---|---|---|
| P0 | none | approved | n/a (control) |
| P1 | `<EmbeddedResource Include="../docs/a.md"/>` | refused (docs item) | n/a (control) |
| P2 | P1 + `Condition="'$(C1540_GATE)'=='1'"` | **approved** unset; refused with `C1540_GATE=1` | yes: `-getItem` `[]` vs `['../docs/a.md']` |
| P11 | `Include="$(DocsRoot)"`, `DocsRoot` taken from env `C1540_DOCS` | **approved** unset; refused with `C1540_DOCS=../docs/a.md` | yes |
| P3 | P1 + `Condition="'$(OS)'=='Windows_NT'"` | refused (missing pin) | (guarded) |
| P4 | `<ToolPathCopy>$(PATH)</ToolPathCopy>` | **approved** | yes: value tracks PATH |
| P5 | P1 + `Condition="$(PATH.Contains('c1540'))"` | refused (unsupported-function) | (guarded) |
| P6 | metadata `%(ModifiedTime)`, `%(CreatedTime)`, `%(AccessedTime)` of `Program.cs` | **approved** | yes, by clock |
| P7/P10 | `%(ModifiedTime)` of the `docs` / `docs/sub` directory item | approved | no: value is always `""` |
| P8 | `Stamp=Program.cs`; P1 with `Condition="'@(Stamp->'%(ModifiedTime)')'=='2001-01-01 00:00:00.0000000'"`, file touched to 2001 | **approved** (cwd = repo root); **refused** (cwd = `app/`) | yes: `-getItem` adds `../docs/a.md` |
| P9 | `%(ModifiedTime)` of the `docs/a.md` item | refused (docs item) | n/a |
| P12 | `<X Include="@(Stamp->'../docs/m%(ModifiedTime)m.md')"/>` | refused; path shows `mm` (cwd = repo) vs `m2001-01-01 00:00:00.0000000m` (cwd = `app/`) | yes: cwd |

## Consequence

- **What a land could skip.** A docs-only candidate whose docs file feeds, only in the land host's
  environment, an EmbeddedResource, Compile, Content or test input of `Antiphon.sln`. A docs edit
  that breaks that build or a test would land as `Skipped(non-code)` with no build and no tests.
- **Who can reach it.** Not a docs edit alone: the gate must already be on master, and the
  diff/manifest gate forces a build for any land that changes definition files. Reaching it takes a
  reviewed definition change that names an environment variable (or timestamp) set on the land host
  but not on the auditing runner, then `-Update`. The runner here already has a host-only
  variable, `DOTNET_RUNNING_IN_CONTAINER=true`. The audit exists to catch exactly this shape
  without relying on Review, so the defect is in the audit.
- No such gate exists in the tree today (fact 5). The defect is latent, not live.

## Smallest fail-closed repair (sketch for Plan/Code; not implemented)

Use the same shape as `PureFunctions`: refuse by name with a reason, add a negative fixture for every
allowance, and change the manifest only through `-Update`.

- **R1 `timestamp-metadata`:** refuse `%(ModifiedTime)`, `%(CreatedTime)` and `%(AccessedTime)`,
  bare or item-qualified, case-insensitive, in raw and unescaped repository evaluation text
  (attributes and bodies, including metadata and item definitions). The real tree uses none, so the
  manifest is unchanged.
- **R2 `environment-property`:** a plain `$(Name)` in repository evaluation text is admitted only
  if `Name` is in an explicit set: MSBuild reserved/well-known names, the audit's globals
  (`Configuration`, `Platform`, `Solution*`, `TargetFramework`, `ArtifactsPath`), and the reviewed
  repository knobs from fact 5. Any other name, including `PATH`, refuses.
- **R3 (decision for Plan):** R2 alone still leaves an **admitted** knob able to gate a docs item
  later (for example `Condition="'$(UsePublishedPackages)'=='true'"` on P1). Closing that needs a
  lexical, condition-independent rule: refuse any item Include/Update in any branch whose literal
  text can name `docs/**` or `README.md`. An alternative is to accept the knob set as a reviewed
  trust boundary. Environment reads inside the SDK (for example `OutDir`) stay under the existing
  SDK trust boundary (CARD-1528 D-4).
- **Witness ids:** V-1 P2 red then refused (`environment-property`); V-2 P11; V-3 P4 (`PATH`);
  V-4 P6 and P8 (`timestamp-metadata`); V-5 the R3 shape (an admitted knob gating a docs item); one
  positive control per admitted knob (EchoGateway, Messaging.Pack, CleanFile, ConPTY); the real
  tree must still be approved; P7 stays approved and documents that directory timestamps are empty.
  Code size: small to medium (two refusal classes in `EvaluationExpressionPolicy.cs`, about 8
  fixtures, one `-Update` that should not change the digest).

## Remaining uncertainties

- Windows: whether directory timestamps are empty and evaluation-time metadata resolves against
  cwd was not checked; no Windows lane was used, by instruction.
- Which build cwd the land's solution build uses for evaluation (P8 and P12 show the audit and a
  project-directory build disagree, so the audit is not representative either way).

## Not done, noted

- Fix idea: R1+R2 in `EvaluationExpressionPolicy`, with R3 decided in Plan. The wrapper could also
  pin the tool's cwd and environment, but that does not make the land's environment match.
