# CARD-0487 TestDesign: pinned discovery and selection probe

Date: 2026-09-11. Repository baseline: a7f2b493. This is bounded feasibility
evidence for the [verification design](../superpowers/plans/2026-09-11-card-0487-scoped-dispatch-testing-plan.md#verification-design),
not scheduled/full-suite qualification.

A temporary project at
C:\Antiphon\worktrees\card-task-3e844ff6\.antiphon\c487-probe
contains Probe.csproj, empty Directory.Build.props/targets (preventing repository
build imports), source, logs and results. It is untracked ignored task evidence;
the reproducer below is retained in this committed document. No repository
test assembly, shared service or production data was changed.

The project uses Microsoft.NET.Sdk, net9.0, ImplicitUsings/Nullable enabled,
IsTestProject=true, EnableMicrosoftTestingPlatformRunner=true and a single
PackageReference TUnit version 1.44.0. Runtime identified TUnit 1.44.0.0,
Microsoft Testing Platform 2.2.2, .NET 9.0.16 on Windows x64.

## Source as executed

```csharp
using TUnit.Core;
namespace C487.Probe;
public abstract class BaseCases { [Test] public void Inherited() {} }
[InheritsTests] public class Ordinary : BaseCases {
    [Test] public void Plain() {}
    [Test, Arguments(1), Arguments(2)] public void Rows(int n) { if (n < 1) throw new Exception(); }
    public static IEnumerable<int> Values() => [3, 4];
    [Test, MethodDataSource(nameof(Values))] public void Data(int n) { if (n < 3) throw new Exception(); }
}
[Category("Slow")] public partial class SlowCases { [Test] public void SlowOne() {} }
public partial class SlowCases { [Test] public void SlowTwo() {} }
[Category("OptIn"), Explicit] public class Manual { [Test] public void ManualOne() {} }
public class Hooks {
    [After(TestDiscovery)] public static void Export(TestDiscoveryContext context) {
        foreach (var p in context.GetType().GetProperties())
            Console.WriteLine($"PROP {p.Name}: {p.PropertyType}");
    }
}


```

Without InheritsTests the initial compile emitted TUnit0030. It was added before
the recorded nine-row discovery/execution. A public base method alone is not
sufficient to count an inherited test. The reflection-printing discovery hook
produced no output in list mode and is not an inventory-export recommendation.

## Reproduction commands

From a new temporary owned project with the same files, build once, then run
the built executable. Use initially absent result directories on every repeat.

    dotnet build Probe.csproj --nologo
    .\bin\Debug\net9.0\Probe.exe --list-tests --no-ansi
    .\bin\Debug\net9.0\Probe.exe --list-tests --output Detailed --no-ansi
    .\bin\Debug\net9.0\Probe.exe --list-tests --diagnostic --diagnostic-output-directory diag --no-ansi
    .\bin\Debug\net9.0\Probe.exe --report-trx --report-trx-filename all.trx --results-directory all --diagnostic --diagnostic-output-directory all-diag --no-ansi
    .\bin\Debug\net9.0\Probe.exe --treenode-filter '/*/*/*/*[Category=Slow]' --report-trx --report-trx-filename slow.trx --results-directory slow --no-ansi
    .\bin\Debug\net9.0\Probe.exe --treenode-filter '/*/*/(Ordinary*)|(SlowCases*)/*' --report-trx --report-trx-filename selected.trx --results-directory selected --no-ansi

The unsupported attempt --list-tests --report-trx was rejected with:
"'--report-trx' cannot be enabled when using '--list-tests'".
There was no discovery TRX. Normal and Detailed list output both showed only
display names, insufficient to disambiguate duplicate class/method display names.

## Recorded results

| Class/method | Discovered rows | Default passed | Slow passed | Class-OR passed |
|---|---:|---:|---:|---:|
| C487.Probe.Ordinary.Plain | 1 | 1 | 0 | 1 |
| C487.Probe.Ordinary.Rows (Arguments 1, 2) | 2 | 2 | 0 | 2 |
| C487.Probe.Ordinary.Data (MethodDataSource 3, 4) | 2 | 2 | 0 | 2 |
| C487.Probe.Ordinary.Inherited | 1 | 1 | 0 | 1 |
| C487.Probe.SlowCases.SlowOne | 1 | 1 | 1 | 1 |
| C487.Probe.SlowCases.SlowTwo (same partial class) | 1 | 1 | 1 | 1 |
| C487.Probe.Manual.ManualOne (Explicit/OptIn) | 1 | 0 | 0 | 0 |
| **Total** | **9** | **8** | **2** | **8** |

All three execution invocations returned native exit 0 and their TRX counters
and joined class/method identities agreed. **18 passed, 0 failed, 0 skipped**
across three invocations (8 distinct eligible rows; no claim of 18 distinct tests).
Recorded runner durations were 635ms, 631ms and 584ms respectively. These are
tiny probe runner durations, not repository build/startup/full-suite estimates.

The diagnostic discovery file at diag/log_260911114959160.diag contains nine
DiscoveredTestNodeStateProperty messages with TestNodeUid and
TestMethodIdentifierProperty (assembly, namespace, type, method, arity, parameter
types). all-diag/log_260911115019698.diag contains eight terminal Passed UIDs
matching the eligible discovery set. Do not count InProgress messages as results.

Expanded UID examples:

    C487.Probe.Ordinary.1.1.Rows(System.Int32).1.1.0
    C487.Probe.Ordinary.1.1.Rows(System.Int32).2.1.0
    C487.Probe.Ordinary.1.1.Data(System.Int32).1.1.0
    C487.Probe.Ordinary.1.1.Data(System.Int32).1.2.0
    C487.Probe.Ordinary.1.1.Inherited.1.1.0

The only discovered UID absent from terminal execution was:

    C487.Probe.Manual.1.1.ManualOne.1.1.0

It was neither passed nor counted as skipped. Explicit/manual exclusion therefore
needs its own identity ledger independent of result counters. The TRX uses GUID
testIds; join those to TestDefinitions for class/method identity. Do not assume
the raw discovery UID string equals a TRX GUID. Use matching diagnostic terminal
UIDs for discovery/execution set reconciliation and independently cross-check
TRX definition/row multiplicity and outcomes.

The task also executed the unchanged scripts/test-nightly-report.ps1 against its
HttpShim: **64 assertions passed, 0 failed** (reporter-baseline.log). Its green
fixture remains client-only at this baseline; passing it does not prove the
planned completeness/receipt guards.

## Limits and required follow-through

This is a feasible pinned export path, not a completed robust parser or a claim
that every repository data source has stable identity. Code must guard versions,
unknown formats, duplicate/colliding identities, incomplete records, missing
terminal rows and safe environment. Use structured MTP records if available with
equivalent proof; do not quietly degrade to console counts.

A metadata-only classification path must prove assembly/class fixtures never
run using explicit sentinels; this probe does not establish that across the
production test projects. S4 needs the full actual built-SHA inventory and a
manual full plus a real scheduled full, including broker/slow/native coverage
and real monitored notification receipt. Until that evidence exists, reduced
dispatch verification remains inactive.

## Task-local evidence hashes

SHA-256 for the source, built DLL and compact reconciled UID summary:

```json
[
  {
    "Path": "C:\\Antiphon\\worktrees\\card-task-3e844ff6\\.antiphon\\c487-probe\\Probe.cs",
    "Hash": "F801E0E027C48195DBB0E9A2E3A4F9B57B7615BFA6B740746D580E2EB609809B"
  },
  {
    "Path": "C:\\Antiphon\\worktrees\\card-task-3e844ff6\\.antiphon\\c487-probe\\bin\\Debug\\net9.0\\Probe.dll",
    "Hash": "0289CE99ADE27AAC752A6A5E2CA1C8937238463AD5F73E0A69BF37B6774E4596"
  },
  {
    "Path": "C:\\Antiphon\\worktrees\\card-task-3e844ff6\\.antiphon\\c487-probe\\verified-summary.json",
    "Hash": "7ADEB087F3FFE43B0BB253DE5EE6C4F165C36BE3F239CB84482911944008F1E4"
  }
]

```

