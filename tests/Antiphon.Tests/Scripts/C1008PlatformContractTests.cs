using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Xml.Linq;
using Antiphon.Tests.Application;
using Antiphon.Tests.TestHelpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Shouldly;
using TUnit.Core;
using SkipTestException = TUnit.Core.Exceptions.SkipTestException;

namespace Antiphon.Tests.Scripts;

[Category("Unit")]
public sealed class C1008PlatformContractTests
{
    private const string ExpectedReason = "CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately.";
    private const string Namespace = "Antiphon.Tests.Scripts.";
    // Independent literal identities: never infer the expected roster from discovery or filters.
    private static readonly string[] RemoteHosts =
    [
        "RemoteScriptContractTests.C1008_Recycle_exact_default_volumes",
        "RemoteScriptContractTests.C1008_Recycle_refuses_references_and_unknown_census",
        "RemoteScriptContractTests.C1008_Recycle_audits_work_as_1654",
        "RemoteScriptContractTests.C1008_Recycle_refuses_unpublished_and_dirty_work",
        "RemoteScriptContractTests.C1008_Recycle_refuses_uninspectable_git",
        "RemoteScriptContractTests.C1008_Recycle_preserves_tmp_copyup",
        "RemoteScriptContractTests.C1008_Recycle_resume_requires_matching_receipt",
        "RemoteScriptContractTests.C1008_Recycle_receipt_records_disk_and_partial_failure",
        "RemoteScriptContractTests.C1008_Retire_temp_rechecks_absence_and_retirement",
        "RemoteScriptContractTests.C1008_Retire_temp_reclaims_below_cache_disk_gate",
        "RemoteScriptContractTests.C1008_Recycle_dry_run_never_mutates",
        "RemoteScriptContractTests.C849_Deploy_prepares_and_verifies_before_acceptance",
    ];
    private static readonly string[] RemotePortable =
    ["RemoteScriptContractTests.C849_Deploy_ordering_contract_is_pinned"];
    // W2 is used here only as an in-memory oracle control. S2 supplies its actual child audit.
    private static readonly string[] RollingHosts =
    [
        "RollingVolumeRecycleScriptTests.C1008_Option_manifest_is_strict",
        "RollingVolumeRecycleScriptTests.C1008_Documentation_and_transport_pins_match",
        "RollingVolumeRecycleScriptTests.C1008_Refusal_receipts_do_not_leak_secrets",
        "RollingVolumeRecycleScriptTests.C1008_Retired_absent_null_is_accepted",
    ];
    private static readonly string[] RollingPortable =
    [
        "RollingVolumeRecycleScriptTests.C1008_Wrapper_option_manifest_is_strict",
        "RollingVolumeRecycleScriptTests.C1008_Transport_scripts_are_ascii",
        "RollingVolumeRecycleScriptTests.C1008_Wrapper_refusal_receipts_do_not_leak_secrets",
        "RollingVolumeRecycleScriptTests.C1008_Wrapper_retired_absent_null_is_accepted",
    ];
    private const string RemoteFilter = "/*/*/RemoteScriptContractTests/(C1008_Recycle_exact_default_volumes*)|(C1008_Recycle_refuses_references_and_unknown_census*)|(C1008_Recycle_audits_work_as_1654*)|(C1008_Recycle_refuses_unpublished_and_dirty_work*)|(C1008_Recycle_refuses_uninspectable_git*)|(C1008_Recycle_preserves_tmp_copyup*)|(C1008_Recycle_resume_requires_matching_receipt*)|(C1008_Recycle_receipt_records_disk_and_partial_failure*)|(C1008_Retire_temp_rechecks_absence_and_retirement*)|(C1008_Retire_temp_reclaims_below_cache_disk_gate*)|(C1008_Recycle_dry_run_never_mutates*)|(C849_Deploy_prepares_and_verifies_before_acceptance*)|(C849_Deploy_ordering_contract_is_pinned*)";
    private static readonly XNamespace Trx = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";

    [Test]
    public void C1050_Guard_matches_native_platform()
    {
        Exception? observed = null;
        try { C1008HostFixture.RequireNativeLinux(); }
        catch (Exception exception) { observed = exception; }
        if (OperatingSystem.IsLinux())
            observed.ShouldBeNull("c1050-native-admission: native Linux executes host contracts");
        else
        {
            observed.ShouldNotBeNull("c1050-native-admission: non-Linux explicitly skips");
            observed.GetType().ShouldBe(typeof(SkipTestException), "c1050-native-admission: exact skip type");
            observed.Message.ShouldBe(ExpectedReason, "c1050-native-admission: exact reason");
        }
    }

    [Test]
    public void C1050_Remote_entries_guard_before_work() =>
        AssertFirstStatements("RemoteScriptContractTests.cs", RemoteHosts);

    private static void AssertFirstStatements(string file, string[] roster)
    {
        var source = File.ReadAllText(Path.Combine(DelegateScriptRunner.RepoRoot, "tests/Antiphon.Tests/Scripts", file));
        var syntax = CSharpSyntaxTree.ParseText(source).GetRoot();
        foreach (var identity in roster)
        {
            var parts = identity.Split('.');
            var methods = syntax.DescendantNodes().OfType<MethodDeclarationSyntax>()
                .Where(method => method.Identifier.ValueText == parts[1]
                    && method.Parent is ClassDeclarationSyntax type && type.Identifier.ValueText == parts[0]).ToArray();
            var label = "c1050-first-statement: " + identity;
            methods.Length.ShouldBe(1, label);
            var first = methods[0].Body?.Statements.FirstOrDefault();
            (first is not null && SyntaxFactory.AreEquivalent(first, SyntaxFactory.ParseStatement("C1008HostFixture.RequireNativeLinux();")))
                .ShouldBeTrue(label);
        }
    }

    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public async Task C1050_Windows_remote_outcomes_are_exact(CancellationToken cancellationToken)
    {
        OperatingSystem.IsWindows().ShouldBeTrue("c1050-native-windows: audit requires Windows placement");
        await RunWindowsAuditAsync(RemoteFilter, RemoteHosts, RemotePortable, cancellationToken);
    }

    private static async Task RunWindowsAuditAsync(string filter, string[] hosts, string[] portable,
        CancellationToken cancellationToken)
    {
        var evidenceRoot = Environment.GetEnvironmentVariable("C1050_EVIDENCE_ROOT")
            ?? Path.Combine(DelegateScriptRunner.RepoRoot, ".antiphon/c1050-audit");
        var root = Directory.CreateDirectory(Path.Combine(evidenceRoot, Guid.NewGuid().ToString("N"))).FullName;
        var assembly = typeof(C1008PlatformContractTests).Assembly;
        var assemblyPath = assembly.Location;
        var executable = Path.Combine(Path.GetDirectoryName(assemblyPath)!, "Antiphon.Tests.exe");
        File.Exists(executable).ShouldBeTrue("c1050-child-apphost: already built native host");
        var stampPath = Path.Combine(AppContext.BaseDirectory, "checkpoint-build-source.json");
        // OutputPath is the producer root; TargetFramework may add one directory below it.
        if (!File.Exists(stampPath))
            stampPath = Path.Combine(Directory.GetParent(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar))!.FullName,
                "checkpoint-build-source.json");
        File.Exists(stampPath).ShouldBeTrue("c1050-child-source: outer row build stamp required");
        var stamp = Path.Combine(root, "checkpoint-build-source.json");
        File.Copy(stampPath, stamp);
        var psi = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = DelegateScriptRunner.RepoRoot,
        };
        foreach (var argument in new[] { "--treenode-filter", filter, "--report-trx", "--report-trx-filename",
                     "raw.trx", "--results-directory", root, "--output", "Detailed" })
            psi.ArgumentList.Add(argument);
        // Never inherit fixture worker/repeat selectors into this ordinary same-build child.
        foreach (var key in psi.Environment.Keys.Where(key => key.StartsWith("ANTIPHON_C", StringComparison.Ordinal)
                     && key.EndsWith("_WORKER", StringComparison.Ordinal)
                     || key.StartsWith("C885_", StringComparison.Ordinal)
                     || key == "ANTIPHON_CHECKPOINT_NONCE").ToArray())
            psi.Environment.Remove(key);
        psi.Environment[ProductionRunnerGuard.BaseUrlEnvVar] = ProductionRunnerGuard.DeadRunnerBaseUrl;
        psi.Environment[ProductionRunnerGuard.CheckInterpreterEnvVar] = "false";
        var started = DateTimeOffset.UtcNow;
        using var process = Process.Start(psi)!;
        var pid = process.Id;
        // Drain concurrently even after cancellation; preserve failure artifacts too.
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromMinutes(5));
        try { await process.WaitForExitAsync(deadline.Token); }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None);
            await File.WriteAllTextAsync(Path.Combine(root, "stdout.log"), await stdout, CancellationToken.None);
            await File.WriteAllTextAsync(Path.Combine(root, "stderr.log"), await stderr, CancellationToken.None);
            var raw = Path.Combine(root, "raw.trx");
            var metadata = new
            {
                executable, argv = psi.ArgumentList.ToArray(), pid, started, ended = DateTimeOffset.UtcNow,
                exit = process.ExitCode, os = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
                assemblyPath, assemblySha256 = Hash(assemblyPath), mvid = assembly.ManifestModule.ModuleVersionId,
                stampPath, stampSha256 = Hash(stamp), rawTrxSha256 = File.Exists(raw) ? Hash(raw) : null,
            };
            await File.WriteAllTextAsync(Path.Combine(root, "child.json"), JsonSerializer.Serialize(metadata), CancellationToken.None);
            Console.WriteLine($"C1050 audit artifacts={root} assemblySha256={metadata.assemblySha256} mvid={metadata.mvid} stampSha256={metadata.stampSha256} rawTrxSha256={metadata.rawTrxSha256}");
        }
        Audit(XDocument.Load(Path.Combine(root, "raw.trx")), hosts, portable, process.ExitCode);
    }

    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    private sealed record Observation(string Identity, string Outcome, string Reason);

    private static void Audit(XDocument document, string[] hosts, string[] portable, int exit)
    {
        var root = document.Root;
        (root?.Name == Trx + "TestRun").ShouldBeTrue("c1050-host-roster: TRX root required");
        var definitions = root!.Elements(Trx + "TestDefinitions").Single().Elements(Trx + "UnitTest").ToArray();
        var results = root.Elements(Trx + "Results").Single().Elements(Trx + "UnitTestResult").ToArray();
        var observations = new List<Observation>();
        foreach (var result in results)
        {
            var id = (string?)result.Attribute("testId");
            string.IsNullOrEmpty(id).ShouldBeFalse("c1050-host-roster: testId required");
            var matching = definitions.Where(definition => (string?)definition.Attribute("id") == id).ToArray();
            matching.Length.ShouldBe(1, "c1050-host-roster: exactly one correlated definition");
            var methods = matching[0].Elements(Trx + "TestMethod").ToArray();
            methods.Length.ShouldBe(1, "c1050-host-roster: TestMethod required");
            var identity = (string?)methods[0].Attribute("className") + "." + (string?)methods[0].Attribute("name");
            var traces = result.Elements(Trx + "Output").Elements(Trx + "DebugTrace").ToArray();
            var reason = traces.Length == 1 ? traces[0].Value : "<missing-or-ambiguous>";
            observations.Add(new Observation(identity, (string?)result.Attribute("outcome") ?? "", reason));
        }
        var portableNames = portable.Select(name => Namespace + name).ToArray();
        var actualHosts = observations.Where(item => !portableNames.Contains(item.Identity, StringComparer.Ordinal))
            .OrderBy(item => item.Identity, StringComparer.Ordinal).ThenBy(item => item.Outcome, StringComparer.Ordinal)
            .ThenBy(item => item.Reason, StringComparer.Ordinal).ToArray();
        var expectedHosts = hosts.Select(name => new Observation(Namespace + name, "NotExecuted", "Skipped: " + ExpectedReason))
            .OrderBy(item => item.Identity, StringComparer.Ordinal).ToArray();
        actualHosts.ShouldBe(expectedHosts, "c1050-host-roster: exact identity/outcome/result-local reason multiset");
        var actualPortable = observations.Where(item => portableNames.Contains(item.Identity, StringComparer.Ordinal))
            .Select(item => (item.Identity, item.Outcome)).OrderBy(item => item.Identity, StringComparer.Ordinal).ToArray();
        var expectedPortable = portableNames.Select(name => (Identity: name, Outcome: "Passed"))
            .OrderBy(item => item.Identity, StringComparer.Ordinal).ToArray();
        actualPortable.ShouldBe(expectedPortable, "c1050-portable-roster: exact Passed multiset");
        var counters = root.Elements(Trx + "ResultSummary").Single().Elements(Trx + "Counters").Single();
        foreach (var (name, expected) in new[] { ("total", hosts.Length + portable.Length), ("executed", portable.Length),
                     ("passed", portable.Length), ("failed", 0), ("notExecuted", hosts.Length) })
            ((string?)counters.Attribute(name)).ShouldBe(expected.ToString(System.Globalization.CultureInfo.InvariantCulture),
                "c1050-counters: " + name);
        foreach (var item in observations)
            Console.WriteLine($"C1050 {item.Identity} outcome={item.Outcome}" + (item.Outcome == "NotExecuted" ? " reason=" + item.Reason : ""));
        exit.ShouldBe(0, "c1050-child-exit: rosters must be checked before exit");
    }

    [Test]
    public void C1050_Audit_rejects_empty_or_incomplete_outcomes()
    {
        foreach (var (hosts, portable) in new[] { (RemoteHosts, RemotePortable), (RollingHosts, RollingPortable) })
        {
            var valid = ValidDocument(hosts, portable);
            Audit(valid, hosts, portable, 0);
            void Reject(Action<XDocument> damage, string label)
            {
                var changed = new XDocument(valid);
                damage(changed);
                var failure = Should.Throw<ShouldAssertException>(() => Audit(changed, hosts, portable, 0),
                    "c1050-negative: corrupted receipt must fail " + label);
                failure.Message.ShouldContain(label, Case.Sensitive, "c1050-negative: decisive audit assertion");
            }
            XElement Host(XDocument doc) => doc.Descendants(Trx + "UnitTestResult").First();
            XElement Portable(XDocument doc) => doc.Descendants(Trx + "UnitTestResult").Last();
            Reject(doc => doc.Descendants(Trx + "Results").Single().RemoveNodes(), "c1050-host-roster");
            Reject(doc => Host(doc).Remove(), "c1050-host-roster");
            Reject(doc => Host(doc).AddAfterSelf(new XElement(Host(doc))), "c1050-host-roster");
            Reject(doc => doc.Descendants(Trx + "TestMethod").First().SetAttributeValue("name", "Substituted"), "c1050-host-roster");
            Reject(doc => Host(doc).SetAttributeValue("outcome", "Passed"), "c1050-host-roster");
            Reject(doc => Host(doc).Descendants(Trx + "DebugTrace").Single().Value = "Skipped: wrong", "c1050-host-roster");
            Reject(doc => Host(doc).Descendants(Trx + "DebugTrace").Single().Value = "", "c1050-host-roster");
            Reject(doc => Host(doc).Descendants(Trx + "DebugTrace").Single().Remove(), "c1050-host-roster");
            Reject(doc => Host(doc).SetAttributeValue("testId", "unknown"), "c1050-host-roster");
            Reject(doc => doc.Descendants(Trx + "TestMethod").First().Remove(), "c1050-host-roster");
            Reject(doc => Portable(doc).Remove(), "c1050-portable-roster");
            Reject(doc => Portable(doc).SetAttributeValue("outcome", "NotExecuted"), "c1050-portable-roster");
            Reject(doc => Portable(doc).SetAttributeValue("outcome", "Failed"), "c1050-portable-roster");
            Reject(doc => doc.Descendants(Trx + "Counters").Single().SetAttributeValue("total", "0"), "c1050-counters");
        }
    }

    private static XDocument ValidDocument(string[] hosts, string[] portable)
    {
        var names = hosts.Concat(portable).ToArray();
        return new XDocument(new XElement(Trx + "TestRun",
            new XElement(Trx + "Results", names.Select((name, i) => new XElement(Trx + "UnitTestResult",
                new XAttribute("testId", "id-" + i), new XAttribute("outcome", i < hosts.Length ? "NotExecuted" : "Passed"),
                i < hosts.Length ? new XElement(Trx + "Output", new XElement(Trx + "DebugTrace", "Skipped: " + ExpectedReason)) : null))),
            new XElement(Trx + "TestDefinitions", names.Select((name, i) => new XElement(Trx + "UnitTest",
                new XAttribute("id", "id-" + i), new XElement(Trx + "TestMethod",
                    new XAttribute("className", Namespace + name[..name.LastIndexOf('.')]),
                    new XAttribute("name", name[(name.LastIndexOf('.') + 1)..]))))),
            new XElement(Trx + "ResultSummary", new XElement(Trx + "Counters",
                new XAttribute("total", names.Length), new XAttribute("executed", portable.Length),
                new XAttribute("passed", portable.Length), new XAttribute("failed", 0), new XAttribute("notExecuted", hosts.Length)))));
    }
}
