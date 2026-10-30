using System.Diagnostics;
using System.Reflection;
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
using Entry = Antiphon.Tests.Scripts.WindowsUnitSkipRoster.Entry;

namespace Antiphon.Tests.Scripts;

[Timeout(600_000)]
[Category("Unit")]
public sealed class WindowsUnitPlatformAdmissionTests
{
    private static readonly XNamespace Trx = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";
    private const string ChildFilter = "/*/*/(HostJqPrerequisiteScriptTests*)|(JqRunnerImageContractTests*)|(GrokSha256PinTests*)|(CodexRootExecContractTests*)|(GitMinimumVersionGuardTests*)|(ChannelOutboundStorageTests*)|(RefreshGithubTokenRemoteCommandTests*)/*";

    [Test]
    public void C1210_Guard_matches_native_platform()
    {
        foreach (var entry in WindowsUnitSkipRoster.Native.DistinctBy(entry => entry.Class))
        {
            var type = typeof(WindowsUnitPlatformAdmissionTests).Assembly.GetType(entry.Namespace + "." + entry.Class);
            type.ShouldNotBeNull("c1210-exact-reason: " + entry.Class);
            var field = type.GetField("NativeLinuxReason", BindingFlags.Static | BindingFlags.NonPublic);
            field.ShouldNotBeNull("c1210-exact-reason: " + entry.Class);
            field.GetRawConstantValue().ShouldBe(entry.Reason, "c1210-exact-reason: " + entry.Class);
            Exception? observed = null;
            try { NativeLinuxGate.Require((string)field.GetRawConstantValue()!); }
            catch (Exception exception) { observed = exception; }
            if (OperatingSystem.IsLinux()) observed.ShouldBeNull("c1210-native-admission");
            else
            {
                observed.ShouldNotBeNull("c1210-native-admission");
                observed.GetType().ShouldBe(typeof(SkipTestException), "c1210-native-admission");
                observed.Message.ShouldBe(entry.Reason, "c1210-exact-reason");
            }
        }
    }

    [Test]
    public void C1210_Native_linux_entries_guard_before_work()
    {
        var methods = SourceMethods();
        WindowsUnitSkipRoster.Native.Length.ShouldBe(84, "c1210-native-identity-count");
        WindowsUnitSkipRoster.Native.Sum(entry => entry.Results).ShouldBe(95, "c1210-native-result-count");
        foreach (var entry in WindowsUnitSkipRoster.Native)
        {
            var matches = methods.Where(method => Identity(method) == entry.Identity).ToArray();
            matches.Length.ShouldBe(1, "c1210-first-statement: " + entry.Identity);
            SyntaxFactory.AreEquivalent(matches[0].Body?.Statements.FirstOrDefault(),
                SyntaxFactory.ParseStatement("NativeLinuxGate.Require(NativeLinuxReason);"))
                .ShouldBeTrue("c1210-first-statement: " + entry.Identity);
        }
    }

    [Test]
    public void C1210_Roster_closure_matches_source_gates()
    {
        var roots = SourceRoots();
        var expected = WindowsUnitSkipRoster.Native.Concat(WindowsUnitSkipRoster.NativeSourceOnly)
            .Concat(WindowsUnitSkipRoster.Existing).ToArray();
        AssertClosure(roots, expected, true);
        Entry[] vectorRoster = [new("Antiphon.Tests.Scripts", "Probe", "Case", 1, "literal")];
        const string healthy = "namespace Antiphon.Tests.Scripts; class Probe { void Case() { C1008HostFixture.RequireNativeLinux(); } }";
        AssertClosure([CSharpSyntaxTree.ParseText(healthy).GetRoot()], vectorRoster, false);
        foreach (var source in new[]
        {
            healthy.Replace("void Case()", "void Unknown()", StringComparison.Ordinal),
            healthy.Replace("C1008HostFixture.RequireNativeLinux()", "global::Antiphon.Tests.TestHelpers.NativeLinuxGate.Require(string.Empty)", StringComparison.Ordinal).Replace("void Case()", "void Unknown()", StringComparison.Ordinal),
            "using Gate = Antiphon.Tests.TestHelpers.NativeLinuxGate; namespace Antiphon.Tests.Scripts; class Probe { void Unknown() { Gate.Require(\"reason\"); } }",
            "using static Antiphon.Tests.TestHelpers.NativeLinuxGate; namespace Antiphon.Tests.Scripts; class Probe { void Unknown() { Require(\"reason\"); } }",
            "namespace Antiphon.Tests.Scripts; class Probe { System.Action Property => () => NativeLinuxGate.Require(\"reason\"); }",
        })
            Should.Throw<ShouldAssertException>(() => AssertClosure([CSharpSyntaxTree.ParseText(source).GetRoot()], vectorRoster, false))
                .Message.ShouldContain("c1210-unrostered-gate");
        Should.Throw<ShouldAssertException>(() => AssertClosure([CSharpSyntaxTree.ParseText(
            healthy.Replace("C1008HostFixture.RequireNativeLinux();", "", StringComparison.Ordinal)).GetRoot()], vectorRoster, false))
            .Message.ShouldContain("c1210-roster-without-gate");
    }

    private static void AssertClosure(SyntaxNode[] roots, Entry[] expected, bool requireLegacyHelper)
    {
        var methods = roots.SelectMany(root => root.DescendantNodes().OfType<MethodDeclarationSyntax>()).ToArray();
        expected.Select(entry => entry.Identity).Distinct().Count().ShouldBe(expected.Length, "c1210-duplicate-roster");
        var actual = new List<string>();
        foreach (var call in roots.SelectMany(root => root.DescendantNodes().OfType<InvocationExpressionSyntax>()))
        {
            var name = GateName(call);
            if (name is null) continue;
            var method = call.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault();
            method.ShouldNotBeNull("c1210-unrostered-gate: call outside a method");
            var identity = Identity(method);
            if (identity == "Antiphon.Tests.Scripts.WindowsUnitPlatformAdmissionTests.C1210_Guard_matches_native_platform"
                && name == "NativeLinuxGate.Require") continue;
            if (identity == "Antiphon.Tests.Scripts.C1008HostFixture.RequireNativeLinux" && name == "NativeLinuxGate.Require") continue;
            if (identity == "Antiphon.Tests.Scripts.C1008PlatformContractTests.C1050_Guard_matches_native_platform"
                && name == "C1008HostFixture.RequireNativeLinux") continue;
            if (identity == "Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105AuditContract"
                && name == "C1008HostFixture.RequireNativeLinux")
            {
                SyntaxFactory.AreEquivalent(method.Body?.Statements.FirstOrDefault(),
                    SyntaxFactory.ParseStatement("C1008HostFixture.RequireNativeLinux();"))
                    .ShouldBeTrue("c1210-roster-without-gate: helper edge");
                continue;
            }
            var entry = expected.SingleOrDefault(entry => entry.Identity == identity);
            entry.ShouldNotBeNull("c1210-unrostered-gate: " + identity);
            var expectedCall = entry.Helper ? "C1105AuditContract" : WindowsUnitSkipRoster.Native.Contains(entry)
                || WindowsUnitSkipRoster.NativeSourceOnly.Contains(entry)
                ? "NativeLinuxGate.Require" : "C1008HostFixture.RequireNativeLinux";
            name.ShouldBe(expectedCall, "c1210-roster-without-gate: " + identity);
            actual.Add(identity);
        }
        actual.Order(StringComparer.Ordinal).ToArray().ShouldBe(
            expected.Select(entry => entry.Identity).Order(StringComparer.Ordinal).ToArray(), "c1210-roster-without-gate");
        if (requireLegacyHelper) methods.Count(method => Identity(method) == "Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105AuditContract"
            && method.DescendantNodes().OfType<InvocationExpressionSyntax>().Any(call => call.Expression.ToString() == "C1008HostFixture.RequireNativeLinux"))
            .ShouldBe(1, "c1210-roster-without-gate: helper implementation");
    }

    private static string? GateName(InvocationExpressionSyntax call)
    {
        var expression = call.Expression;
        if (expression is IdentifierNameSyntax identifier && identifier.Identifier.ValueText == "C1105AuditContract") return "C1105AuditContract";
        if (expression is not MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Require" or "RequireNativeLinux" }
            && expression is not IdentifierNameSyntax { Identifier.ValueText: "Require" or "RequireNativeLinux" }) return null;
        var root = call.SyntaxTree.GetRoot();
        var uses = root.DescendantNodes().OfType<UsingDirectiveSyntax>().ToArray();
        foreach (var (type, member, canonical) in new[] { ("NativeLinuxGate", "Require", "NativeLinuxGate.Require"),
                     ("C1008HostFixture", "RequireNativeLinux", "C1008HostFixture.RequireNativeLinux") })
        {
            if (expression is MemberAccessExpressionSyntax access && access.Name.Identifier.ValueText == member)
            {
                var receiver = access.Expression.ToString();
                if (receiver == type || receiver.EndsWith("." + type, StringComparison.Ordinal)
                    || uses.Any(use => use.Alias?.Name.Identifier.ValueText == receiver
                        && (use.Name?.ToString() == type || use.Name?.ToString().EndsWith("." + type, StringComparison.Ordinal) == true))) return canonical;
            }
            if (expression is IdentifierNameSyntax unqualified && unqualified.Identifier.ValueText == member
                && uses.Any(use => use.StaticKeyword.RawKind != 0
                    && (use.Name?.ToString() == type || use.Name?.ToString().EndsWith("." + type, StringComparison.Ordinal) == true))) return canonical;
        }
        return null;
    }

    private static MethodDeclarationSyntax[] SourceMethods() => SourceRoots()
        .SelectMany(root => root.DescendantNodes().OfType<MethodDeclarationSyntax>()).ToArray();

    private static SyntaxNode[] SourceRoots() => Directory.EnumerateFiles(
            Path.Combine(DelegateScriptRunner.RepoRoot, "tests/Antiphon.Tests"), "*.cs", SearchOption.AllDirectories)
        .Where(path => !Path.GetRelativePath(DelegateScriptRunner.RepoRoot, path).Split(Path.DirectorySeparatorChar)
            .Any(part => part == "obj" || part == "bin" || part.StartsWith("bin-", StringComparison.Ordinal)))
        .Select(path => CSharpSyntaxTree.ParseText(File.ReadAllText(path)).GetRoot()).ToArray();

    private static string Identity(MethodDeclarationSyntax method) =>
        (method.Ancestors().OfType<BaseNamespaceDeclarationSyntax>().FirstOrDefault()?.Name.ToString() ?? "") + "."
        + (method.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault()?.Identifier.ValueText ?? "") + "." + method.Identifier.ValueText;

    private sealed record Observation(string Identity, string Outcome, string Reason);

    private static void Audit(XDocument document, Entry[] hosts, Entry[] portable, Entry[] conditional, int exit)
    {
        var root = document.Root;
        (root?.Name == Trx + "TestRun").ShouldBeTrue("c1210-trx-root");
        var definitionSections = root!.Elements(Trx + "TestDefinitions").ToArray();
        definitionSections.Length.ShouldBe(1, "c1210-definition");
        var resultSections = root.Elements(Trx + "Results").ToArray();
        resultSections.Length.ShouldBe(1, "c1210-host-roster");
        var definitions = definitionSections[0].Elements(Trx + "UnitTest").ToArray();
        var results = resultSections[0].Elements(Trx + "UnitTestResult").ToArray();
        var observations = new List<Observation>();
        foreach (var result in results)
        {
            var id = (string?)result.Attribute("testId");
            string.IsNullOrEmpty(id).ShouldBeFalse("c1210-definition");
            var matches = definitions.Where(definition => (string?)definition.Attribute("id") == id).ToArray();
            matches.Length.ShouldBe(1, "c1210-definition");
            var methods = matches[0].Elements(Trx + "TestMethod").ToArray();
            methods.Length.ShouldBe(1, "c1210-method");
            var className = (string?)methods[0].Attribute("className");
            var methodName = (string?)methods[0].Attribute("name");
            (!string.IsNullOrEmpty(className) && !string.IsNullOrEmpty(methodName)).ShouldBeTrue("c1210-method");
            var traces = result.Elements(Trx + "Output").Elements(Trx + "DebugTrace").ToArray();
            observations.Add(new Observation(className + "." + methodName, (string?)result.Attribute("outcome") ?? "",
                traces.Length == 1 ? traces[0].Value : "<missing-or-ambiguous>"));
        }
        var expected = hosts.Concat(portable).Concat(conditional).ToArray();
        observations.All(item => expected.Any(entry => entry.Identity == item.Identity)).ShouldBeTrue("c1210-host-roster: identity");
        foreach (var (entries, label) in new[] { (hosts, "c1210-host-roster"), (portable, "c1210-portable-roster"), (conditional, "c1210-conditional-roster") })
        {
            observations.Where(item => entries.Any(entry => entry.Identity == item.Identity)).Select(item => item.Identity).Order(StringComparer.Ordinal).ToArray()
                .ShouldBe(entries.SelectMany(entry => Enumerable.Repeat(entry.Identity, entry.Results)).Order(StringComparer.Ordinal).ToArray(), label);
        }
        foreach (var item in observations)
        {
            var host = hosts.SingleOrDefault(entry => entry.Identity == item.Identity);
            var capability = conditional.SingleOrDefault(entry => entry.Identity == item.Identity);
            if (host is not null)
            {
                item.Outcome.ShouldBe("NotExecuted", "c1210-host-roster");
                item.Reason.ShouldBe("Skipped: " + host.Reason, "c1210-result-reason");
            }
            else if (capability is not null)
            {
                (item.Outcome is "Passed" or "NotExecuted").ShouldBeTrue("c1210-conditional-roster");
                if (item.Outcome == "NotExecuted") item.Reason.ShouldBe("Skipped: " + capability.Reason, "c1210-result-reason");
            }
            else item.Outcome.ShouldBe("Passed", "c1210-portable-roster");
        }
        var summaries = root.Elements(Trx + "ResultSummary").ToArray();
        summaries.Length.ShouldBe(1, "c1210-counters");
        var counters = summaries[0].Elements(Trx + "Counters").ToArray();
        counters.Length.ShouldBe(1, "c1210-counters");
        foreach (var (name, value) in new[] { ("total", observations.Count), ("executed", observations.Count(item => item.Outcome != "NotExecuted")),
                     ("passed", observations.Count(item => item.Outcome == "Passed")), ("failed", 0), ("notExecuted", observations.Count(item => item.Outcome == "NotExecuted")) })
            ((string?)counters[0].Attribute(name)).ShouldBe(value.ToString(System.Globalization.CultureInfo.InvariantCulture), "c1210-counters: " + name);
        exit.ShouldBe(0, "c1210-child-exit");
    }

    [Test]
    public void C1210_Audit_rejects_empty_or_incomplete_outcomes()
    {
        Entry[] capabilities = [new("Antiphon.Tests.Application", "ChannelOutboundStorageTests", "C1059_Source_reads_reject_file_links", 1,
            "CARD-1210: file-link controls require symbolic-link creation privilege on Windows; capability unavailable."),
            new("Antiphon.Tests.Application", "ChannelOutboundStorageTests", "Output_rename_and_link_swap_after_inspection_seals_the_opened_file", 1,
            "CARD-1210: file-link controls require symbolic-link creation privilege on Windows; capability unavailable.")];
        Entry[] storagePortable =
        [
            new("Antiphon.Tests.Application", "ChannelOutboundStorageTests", "C1059_Source_reads_require_captured_roots_without_traversal", 1, ""),
            new("Antiphon.Tests.Application", "ChannelOutboundStorageTests", "C1059_Source_reads_reject_directory_links", 1, ""),
            new("Antiphon.Tests.Application", "ChannelOutboundStorageTests", "C1059_Source_length_is_checked_before_reading_with_a_finite_budget", 1, ""),
            new("Antiphon.Tests.Application", "ChannelOutboundStorageTests", "C1059_Source_reads_refuse_nonregular_files_without_blocking", 1, ""),
            new("Antiphon.Tests.Application", "ChannelOutboundStorageTests", "Failed_staging_never_exposes_a_partial_snapshot", 5, ""),
            new("Antiphon.Tests.Application", "ChannelOutboundStorageTests", "Frozen_reply_and_input_bytes_survive_source_mutation", 1, ""),
            new("Antiphon.Tests.Application", "ChannelOutboundStorageTests", "Oversized_reply_never_exposes_snapshot_directory", 1, ""),
            new("Antiphon.Tests.Application", "ChannelOutboundStorageTests", "Exactly_fourteen_mebibytes_of_original_bytes_can_be_staged", 1, ""),
            new("Antiphon.Tests.Application", "ChannelOutboundStorageTests", "Serialized_limit_counts_unicode_metadata_and_base64_at_exact_boundary", 1, ""),
            new("Antiphon.Tests.Application", "ChannelOutboundStorageTests", "Near_default_wire_cap_accepts_exact_serialized_size_and_refuses_one_more_byte", 1, ""),
            new("Antiphon.Tests.Application", "ChannelOutboundStorageTests", "Converted_files_are_sealed_with_original_sources_and_frozen_routing", 1, ""),
            new("Antiphon.Tests.Application", "ChannelOutboundStorageTests", "Manifested_zip_sources_are_expanded_from_attached_bytes", 1, ""),
            new("Antiphon.Tests.Application", "ChannelOutboundStorageTests", "Source_zip_expansion_accepts_exact_budget_and_refuses_one_extra_byte", 1, ""),
            new("Antiphon.Tests.Application", "ChannelOutboundStorageTests", "Inline_and_zip_sources_share_the_expanded_budget", 1, ""),
            new("Antiphon.Tests.Application", "ChannelOutboundStorageTests", "Attachment_source_url_does_not_supply_worker_input", 1, ""),
            new("Antiphon.Tests.Application", "ChannelOutboundStorageTests", "Source_zip_manifest_rejects_unsafe_or_ambiguous_members", 9, ""),
            new("Antiphon.Tests.Application", "ChannelOutboundStorageTests", "Worker_created_sealed_reply_cannot_bypass_manifest_validation_on_recovery", 1, ""),
            new("Antiphon.Tests.Application", "ChannelOutboundStorageTests", "Output_manifest_cannot_override_frozen_routing", 1, ""),
            new("Antiphon.Tests.Application", "ChannelOutboundStorageTests", "Complete_source_stamp_requires_every_manifested_attachment", 1, ""),
        ];
        WindowsUnitSkipRoster.Conditional.ShouldBe(capabilities, "c1210-conditional-roster: frozen identities and reasons");
        WindowsUnitSkipRoster.StoragePortable.ShouldBe(storagePortable, "c1210-portable-roster: frozen storage identities");
        FileLink.PrivilegeReason.ShouldBe(capabilities[0].Reason, "c1210-filelink-exact-reason");
        ChildFilter.ShouldBe("/*/*/(HostJqPrerequisiteScriptTests*)|(JqRunnerImageContractTests*)|(GrokSha256PinTests*)|(CodexRootExecContractTests*)|(GitMinimumVersionGuardTests*)|(ChannelOutboundStorageTests*)|(RefreshGithubTokenRemoteCommandTests*)/*",
            "c1210-child-storage-selection");
        foreach (var (conditional, skipped) in new[] { (Array.Empty<Entry>(), false), (capabilities, false), (capabilities, true) })
        {
            var portable = conditional.Length == 0 ? WindowsUnitSkipRoster.Portable : WindowsUnitSkipRoster.Portable.Concat(storagePortable).ToArray();
            var valid = ValidDocument(portable, conditional, skipped);
            Audit(valid, WindowsUnitSkipRoster.Native, portable, conditional, 0);
            void Reject(Action<XDocument> damage, string label, string vector)
            {
                var changed = new XDocument(valid);
                damage(changed);
                var failure = Should.Throw<ShouldAssertException>(() => Audit(changed, WindowsUnitSkipRoster.Native,
                    portable, conditional, 0), "c1210-negative: " + vector);
                failure.Message.ShouldContain(label, Case.Sensitive, "c1210-negative: " + vector);
            }
            XElement Host(XDocument doc) => doc.Descendants(Trx + "UnitTestResult").First();
            XElement Portable(XDocument doc) => doc.Descendants(Trx + "UnitTestResult").First(result => (string?)result.Attribute("testId") == "portable-0");
            Reject(doc => doc.Root!.Name = Trx + "Other", "c1210-trx-root", "trx-root");
            Reject(doc => doc.Root!.Name = XNamespace.None + "TestRun", "c1210-trx-root", "trx-namespace");
            Reject(doc => doc.Descendants(Trx + "UnitTest").First().Remove(), "c1210-definition", "missing-definition");
            Reject(doc => { var def = doc.Descendants(Trx + "UnitTest").First(); def.AddAfterSelf(new XElement(def)); }, "c1210-definition", "definition");
            Reject(doc => Host(doc).SetAttributeValue("testId", "unknown"), "c1210-definition", "unknown-testId");
            Reject(doc => doc.Descendants(Trx + "TestMethod").First().Remove(), "c1210-method", "method");
            Reject(doc => { var method = doc.Descendants(Trx + "TestMethod").First(); method.AddAfterSelf(new XElement(method)); }, "c1210-method", "duplicate-method");
            foreach (var attribute in new[] { "className", "name" })
            {
                Reject(doc => doc.Descendants(Trx + "TestMethod").First().SetAttributeValue(attribute, "Wrong"), "c1210-host-roster", "identity");
                Reject(doc => doc.Descendants(Trx + "TestMethod").First().SetAttributeValue(attribute, ""), "c1210-method", "empty-method");
            }
            Reject(doc => Host(doc).Remove(), "c1210-host-roster", "missing-host");
            Reject(doc => Host(doc).AddAfterSelf(new XElement(Host(doc))), "c1210-host-roster", "host-multiplicity");
            Reject(doc => Host(doc).SetAttributeValue("outcome", "Passed"), "c1210-host-roster", "host-passed");
            foreach (var reason in new[] { "", "Skipped: wrong" })
                Reject(doc => Host(doc).Descendants(Trx + "DebugTrace").Single().Value = reason, "c1210-result-reason", "result-reason");
            Reject(doc => Host(doc).Descendants(Trx + "DebugTrace").Single().Remove(), "c1210-result-reason", "missing-reason");
            Reject(doc => { var trace = Host(doc).Descendants(Trx + "DebugTrace").Single(); trace.AddAfterSelf(new XElement(trace)); }, "c1210-result-reason", "duplicate-reason");
            Reject(doc => Portable(doc).Remove(), "c1210-portable-roster", "portable");
            foreach (var outcome in new[] { "Failed", "NotExecuted" })
                Reject(doc => Portable(doc).SetAttributeValue("outcome", outcome), "c1210-portable-roster", "portable-" + outcome);
            foreach (var counter in new[] { "total", "executed", "passed", "failed", "notExecuted" })
                Reject(doc => doc.Descendants(Trx + "Counters").Single().SetAttributeValue(counter, "999"), "c1210-counters", "counters-" + counter);
            if (conditional.Length > 0)
            {
                Reject(doc => doc.Descendants(Trx + "UnitTestResult").Last().SetAttributeValue("outcome", "Failed"), "c1210-conditional-roster", "conditional");
                if (skipped) Reject(doc => doc.Descendants(Trx + "DebugTrace").Last().Value = "Skipped: wrong", "c1210-result-reason", "conditional-reason");
            }
            Should.Throw<ShouldAssertException>(() => Audit(valid, WindowsUnitSkipRoster.Native, portable,
                conditional, 1), "c1210-negative: child-exit").Message.ShouldContain("c1210-child-exit");
        }
        AssertLaunchVectors();
    }

    private static XDocument ValidDocument(Entry[] portable, Entry[] conditional, bool skipped)
    {
        var results = new List<XElement>();
        var definitions = new List<XElement>();
        foreach (var (entries, prefix, outcome) in new[] { (WindowsUnitSkipRoster.Native, "host", "NotExecuted"),
                     (portable, "portable", "Passed"), (conditional, "conditional", skipped ? "NotExecuted" : "Passed") })
        {
            var index = 0;
            foreach (var entry in entries)
            for (var row = 0; row < entry.Results; row++)
            {
                var id = prefix + "-" + index++;
                definitions.Add(new XElement(Trx + "UnitTest", new XAttribute("id", id), new XElement(Trx + "TestMethod",
                    new XAttribute("className", entry.Namespace + "." + entry.Class), new XAttribute("name", entry.Method))));
                results.Add(new XElement(Trx + "UnitTestResult", new XAttribute("testId", id), new XAttribute("outcome", outcome),
                    outcome == "NotExecuted" ? new XElement(Trx + "Output", new XElement(Trx + "DebugTrace", "Skipped: " + entry.Reason)) : null));
            }
        }
        // CARD-1265 and CARD-1260 add four single-result native gates to the frozen audit totals.
        var total = conditional.Length == 0 ? 76 : 109;
        var passed = conditional.Length == 0 ? 4 : skipped ? 35 : 37;
        var notExecuted = conditional.Length > 0 && skipped ? 74 : 72;
        return new XDocument(new XElement(Trx + "TestRun", new XElement(Trx + "Results", results),
            new XElement(Trx + "TestDefinitions", definitions), new XElement(Trx + "ResultSummary", new XElement(Trx + "Counters",
                new XAttribute("total", total), new XAttribute("executed", passed), new XAttribute("passed", passed),
                new XAttribute("failed", 0), new XAttribute("notExecuted", notExecuted)))));
    }

    private sealed record LaunchSpec(bool AppHostExists, bool SourceVerified, Dictionary<string, string?> Environment);

    private static LaunchSpec PrepareChildLaunch(bool appHostExists, bool sourceVerified, IDictionary<string, string?> inherited)
    {
        var environment = new Dictionary<string, string?>(inherited, StringComparer.OrdinalIgnoreCase);
        foreach (var key in environment.Keys.Where(key => key.StartsWith("ANTIPHON_C", StringComparison.OrdinalIgnoreCase)
                     && key.EndsWith("_WORKER", StringComparison.OrdinalIgnoreCase)
                     || key.StartsWith("C885_", StringComparison.OrdinalIgnoreCase)
                     || key.Equals("ANTIPHON_CHECKPOINT_NONCE", StringComparison.OrdinalIgnoreCase)).ToArray())
            environment.Remove(key);
        environment[ProductionRunnerGuard.BaseUrlEnvVar] = ProductionRunnerGuard.DeadRunnerBaseUrl;
        environment[ProductionRunnerGuard.CheckInterpreterEnvVar] = "false";
        return new LaunchSpec(appHostExists, sourceVerified, environment);
    }

    private static void ValidateLaunch(LaunchSpec launch)
    {
        launch.AppHostExists.ShouldBeTrue("c1210-child-apphost");
        launch.SourceVerified.ShouldBeTrue("c1210-child-source-stamp");
        launch.Environment.Keys.Any(key => key.StartsWith("ANTIPHON_C", StringComparison.OrdinalIgnoreCase)
            && key.EndsWith("_WORKER", StringComparison.OrdinalIgnoreCase)).ShouldBeFalse("c1210-child-worker");
        launch.Environment.Keys.Any(key => key.StartsWith("C885_", StringComparison.OrdinalIgnoreCase)).ShouldBeFalse("c1210-child-repeat");
        launch.Environment.ContainsKey("ANTIPHON_CHECKPOINT_NONCE").ShouldBeFalse("c1210-child-nonce");
        launch.Environment[ProductionRunnerGuard.BaseUrlEnvVar].ShouldBe("http://127.0.0.1:1", "c1210-child-runner");
        launch.Environment[ProductionRunnerGuard.CheckInterpreterEnvVar].ShouldBe("false", "c1210-child-interpreter");
    }

    private static void AssertLaunchVectors()
    {
        var inherited = new Dictionary<string, string?>
        {
            ["ANTIPHON_C1050_WORKER"] = "1", ["antiphon_c1210_worker"] = "1", ["C885_REPEAT"] = "5",
            ["ANTIPHON_CHECKPOINT_NONCE"] = "inherited", [ProductionRunnerGuard.BaseUrlEnvVar] = "http://127.0.0.1:17204",
            [ProductionRunnerGuard.CheckInterpreterEnvVar] = "true", ["C1210_KEEP"] = "sentinel",
        };
        var safe = PrepareChildLaunch(true, true, inherited);
        foreach (var key in new[] { "ANTIPHON_C1050_WORKER", "antiphon_c1210_worker" })
            safe.Environment.ContainsKey(key).ShouldBeFalse("c1210-negative: child-worker");
        safe.Environment.ContainsKey("C885_REPEAT").ShouldBeFalse("c1210-negative: child-repeat");
        safe.Environment.ContainsKey("ANTIPHON_CHECKPOINT_NONCE").ShouldBeFalse("c1210-negative: child-nonce");
        safe.Environment[ProductionRunnerGuard.BaseUrlEnvVar].ShouldBe("http://127.0.0.1:1", "c1210-negative: child-runner");
        safe.Environment[ProductionRunnerGuard.CheckInterpreterEnvVar].ShouldBe("false", "c1210-negative: child-interpreter");
        ValidateLaunch(safe);
        AssertSourceStampVectors();
        safe.Environment["C1210_KEEP"].ShouldBe("sentinel", "c1210-child-environment-preserved");
        foreach (var (spec, label) in new[] { (safe with { AppHostExists = false }, "c1210-child-apphost"),
                     (safe with { SourceVerified = false }, "c1210-child-source-stamp") })
            Should.Throw<ShouldAssertException>(() => ValidateLaunch(spec), "c1210-negative: " + label).Message.ShouldContain(label);
        foreach (var (key, value, label) in new[] { ("ANTIPHON_C1210_WORKER", "1", "c1210-child-worker"),
                     ("C885_REPEAT", "5", "c1210-child-repeat"), ("ANTIPHON_CHECKPOINT_NONCE", "inherited", "c1210-child-nonce"),
                     (ProductionRunnerGuard.BaseUrlEnvVar, "http://127.0.0.1:17204", "c1210-child-runner"),
                     (ProductionRunnerGuard.CheckInterpreterEnvVar, "true", "c1210-child-interpreter") })
        {
            var changed = new Dictionary<string, string?>(safe.Environment, StringComparer.OrdinalIgnoreCase) { [key] = value };
            Should.Throw<ShouldAssertException>(() => ValidateLaunch(safe with { Environment = changed }), "c1210-negative: " + label)
                .Message.ShouldContain(label);
        }
    }

    private static void AssertSourceStampVectors()
    {
        var root = Directory.CreateTempSubdirectory("c1210-stamp-").FullName;
        try
        {
            var expected = new Antiphon.Checkpoints.CheckpointBuildBinding
            {
                RepositoryRoot = root, Project = Path.Combine(root, "tests"), OutputPath = Path.Combine(root, "bin-c1210-fixture"),
                Commit = new string('a', 40), Fingerprint = "literal-clean-fingerprint", SourceState = "clean", Properties = [],
            };
            expected.Check().ShouldBe("unknown", "c1210-negative: source-stamp-missing");
            Should.Throw<ShouldAssertException>(() => ValidateLaunch(PrepareChildLaunch(true, expected.Check() == "verified",
                new Dictionary<string, string?>())), "c1210-negative: source-stamp").Message.ShouldContain("c1210-child-source-stamp");
            expected.Write();
            expected.Check().ShouldBe("verified", "c1210-source-stamp-healthy");
            foreach (var (field, value) in new[] { ("version", "2"), ("repositoryRoot", "\"wrong\""), ("project", "\"wrong\""),
                         ("outputPath", "\"wrong\""), ("commit", "\"wrong\""), ("fingerprint", "\"wrong\""),
                         ("sourceState", "\"dirty\""), ("properties", "[\"UseAppHost=false\"]") })
            {
                expected.Write();
                var json = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(expected.PathName))!;
                json[field] = System.Text.Json.Nodes.JsonNode.Parse(value);
                File.WriteAllText(expected.PathName, json.ToJsonString());
                expected.Check().ShouldBe("mismatch", "c1210-negative: source-stamp-" + field);
                Should.Throw<ShouldAssertException>(() => ValidateLaunch(PrepareChildLaunch(true, expected.Check() == "verified",
                    new Dictionary<string, string?>())), "c1210-negative: source-stamp").Message.ShouldContain("c1210-child-source-stamp");
            }
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Test, ParallelLimiter<ProcessSpawnLimit>]
    public async Task C1210_Windows_linux_only_outcomes_are_exact(CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows()) throw new SkipTestException("CARD-1210: the child skip-roster audit requires Windows placement.");
        var assembly = typeof(WindowsUnitPlatformAdmissionTests).Assembly;
        var executable = Path.Combine(Path.GetDirectoryName(assembly.Location)!, "Antiphon.Tests.exe");
        var source = new Antiphon.Checkpoints.SourceSnapshot().Capture(DelegateScriptRunner.RepoRoot);
        source.DirtyFiles.ShouldBe(0, "c1210-child-source-stamp: clean source");
        source.CaptureStatus.ShouldBe("known", "c1210-child-source-stamp");
        var binding = OuterRowBuildStamp.EnsureForBuildOutput(AppContext.BaseDirectory, DelegateScriptRunner.RepoRoot, source);
        var psi = new ProcessStartInfo(executable)
        {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
            WorkingDirectory = DelegateScriptRunner.RepoRoot,
        };
        var launch = PrepareChildLaunch(File.Exists(executable), binding.Check() == "verified", psi.Environment);
        ValidateLaunch(launch);
        psi.Environment.Clear();
        foreach (var pair in launch.Environment) psi.Environment[pair.Key] = pair.Value;
        var root = Directory.CreateDirectory(Path.Combine(DelegateScriptRunner.RepoRoot, ".antiphon/c1210-audit", Guid.NewGuid().ToString("N"))).FullName;
        var stamp = Path.Combine(root, "checkpoint-build-source.json");
        File.Copy(binding.PathName, stamp);
        foreach (var argument in new[] { "--treenode-filter", ChildFilter, "--report-trx", "--report-trx-filename", "raw.trx",
                     "--results-directory", root, "--output", "Detailed" }) psi.ArgumentList.Add(argument);
        var assemblyHash = Hash(assembly.Location);
        var stampHash = Hash(stamp);
        var started = DateTimeOffset.UtcNow;
        using var process = Process.Start(psi)!;
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
            await File.WriteAllTextAsync(Path.Combine(root, "child.json"), JsonSerializer.Serialize(new
            {
                executable, argv = psi.ArgumentList.ToArray(), pid = process.Id, started, ended = DateTimeOffset.UtcNow,
                exit = process.ExitCode, assemblyPath = assembly.Location, assemblySha256 = assemblyHash,
                mvid = assembly.ManifestModule.ModuleVersionId, stampSha256 = stampHash,
                rawTrxSha256 = File.Exists(raw) ? Hash(raw) : null, source.Commit,
            }), CancellationToken.None);
            Console.WriteLine("C1210 audit artifacts=" + root);
        }
        Hash(assembly.Location).ShouldBe(assemblyHash, "c1210-child-assembly");
        Hash(binding.PathName).ShouldBe(stampHash, "c1210-child-source-stamp");
        File.Exists(Path.Combine(root, "raw.trx")).ShouldBeTrue("c1210-trx-root: child receipt");
        Audit(XDocument.Load(Path.Combine(root, "raw.trx")), WindowsUnitSkipRoster.Native,
            WindowsUnitSkipRoster.Portable.Concat(WindowsUnitSkipRoster.StoragePortable).ToArray(),
            WindowsUnitSkipRoster.Conditional, process.ExitCode);
    }

    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
}
