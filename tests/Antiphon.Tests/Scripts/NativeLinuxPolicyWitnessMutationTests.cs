using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using System.Text.Json;
using Antiphon.NativeLinuxMigrationAudit;
using Antiphon.TestSupport;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

[Category("Unit")]
public class NativeLinuxPolicyWitnessMutationTests
{
    const string Prefix = "Linux-only by construction: fixture";

    [Test]
    public void Ungated_overload_is_rejected()
    {
        var report = Inspect("R1", "R1-overload.cs.txt", true, Row("OverloadHost", Prefix, "Target"));
        Has(report, "missing-marker", "Target(System.String)");
        Lacks(report, "missing-marker", "Target(System.Int32)");
    }

    [Test]
    public void Case_and_namespace_twins_are_all_checked()
    {
        var report = Inspect("R2", "R2-twins.cs.txt", true,
            Row("Twin", Prefix, "Work"),
            Row("Only.Exact", Prefix),
            Row("Exact", Prefix));
        Has(report, "missing-marker", "Two.Twin");
        Has(report, "missing-marker", "Two.twin");
        Has(report, "missing-marker", "Other.Exact");
        Lacks(report, "missing-marker", "One.Twin");
        Lacks(report, "missing-marker", "Only.Exact");
    }

    [Test]
    public void Nested_and_generic_decoys_do_not_cover_a_test()
    {
        var report = Inspect("R3", "R3-decoy.cs.txt", true,
            Row("Real", Prefix),
            Row("Real`1", Prefix),
            Row("Outer+Inner", Prefix));
        Has(report, "missing-marker", "Decoy.Real");
        Lacks(report, "missing-marker", "Real`1");
        Lacks(report, "missing-marker", "Outer+Inner");
        Lacks(report, "reverse-coverage", "");
    }

    [Test]
    public void Inherited_and_partial_tests_are_all_checked()
    {
        var report = Inspect("R4", "R4-inherited.cs.txt", true,
            Row("Child", Prefix),
            Row("Derived", Prefix),
            Row("MarkedDerived", Prefix),
            Row("HideDerived", Prefix),
            Row("NewTestDerived", Prefix));
        Has(report, "missing-marker", "FromBase()");
        Has(report, "missing-marker", "Inherited()");
        Has(report, "missing-marker", "Hidden()");
        Has(report, "missing-marker", "PartialSibling()");
        Has(report, "missing-marker", "row=HideDerived");
        Lacks(report, "missing-marker", "MarkedInherited()");
        Lacks(report, "missing-marker", "row=NewTestDerived");
        report.Diagnostics.Any(item => item.Code == "missing-marker" && item.Row == "MarkedDerived").ShouldBeFalse(report.Text);
        Lacks(report, "reverse-coverage", "");
    }

    [Test]
    public void Legacy_gate_spellings_do_not_replace_the_marker()
    {
        string[] classes =
        [
            "ConditionalHost", "AliasHost", "ShadowHost", "DeferredHost", "ConstructorHost", "HookHost",
            "UnicodeHost", "PreprocessorHost", "CommentHost", "LiteralHost", "UnreachableHost", "SwallowedHost", "SkipWhenHost",
        ];
        var report = Inspect("R5", "R5-legacy-gates.cs.txt", true, classes.Select(name => Row(name, Prefix)).ToArray());
        foreach (var name in classes)
            Has(report, "missing-marker", name);
        report.Diagnostics.Count(item => item.Code == "missing-marker").ShouldBe(classes.Length, report.Text);
    }

    [Test]
    public void Lookalike_attribute_identity_is_rejected()
    {
        var lookalike = Inspect("R11Look", "R11-lookalike.cs.txt", false, Row("LookHost", Prefix));
        Has(lookalike, "missing-marker", "SameName()");
        Has(lookalike, "missing-marker", "Ordinary()");
        Has(lookalike, "missing-marker", "Subclass()");

        var real = Inspect("R11Real", "R11-real.cs.txt", true, Row("RealHost", Prefix));
        real.Success.ShouldBeTrue(real.Text);
    }

    [Test]
    public void Every_row_and_method_must_resolve()
    {
        var report = Inspect("R12", "R12-resolution.cs.txt", true,
            Row("NoSuchType", Prefix),
            Row("AbstractOnly", Prefix),
            Row("EmptyHost", Prefix),
            Row("MethodHost", Prefix, "Present", "Missing"),
            Row("MixedBuilder", Prefix),
            Row("NamesakeOnly", Prefix),
            Row("OrdinaryOnly", Prefix),
            Row("InheritedBuilder", Prefix),
            OtherSuite("not-a-suite", "Ghost"));
        Has(report, "unresolved-row", "NoSuchType");
        Has(report, "unresolved-row", "AbstractOnly");
        Has(report, "unresolved-row", "EmptyHost");
        Has(report, "unresolved-row", "NamesakeOnly");
        Has(report, "unresolved-method", "Missing");
        Has(report, "unsupported-test-attribute", "Build()");
        Has(report, "unsupported-test-attribute", "BuildMarked()");
        Has(report, "unsupported-test-attribute", "CustomBuilderAttribute");
        Has(report, "unsupported-test-attribute", "InheritedBuild()");
        Has(report, "unknown-exclusion", "not-a-suite");
        Lacks(report, "missing-marker", "");
        Lacks(report, "unsupported-test-attribute", "OrdinaryOnly");

        var broken = InspectBroken();
        Has(broken, "unresolved-row", "ReflectionTypeLoadException");
    }

    [Test]
    public void New_exclusion_without_marker_fails()
    {
        var ungated = Inspect("R13", "R13-ungated.cs.txt", true, Row("UngatedNew", Prefix));
        Has(ungated, "missing-marker", "UngatedNew");
        Lacks(ungated, "legacy-digest-mismatch", "");
        Lacks(ungated, "policy-hash", "");

        var droppedPrefix = Inspect("R13b", "R13-ungated.cs.txt", true, Row("UngatedNew", "a different reason"));
        Has(droppedPrefix, "legacy-digest-mismatch", "");
        Lacks(droppedPrefix, "policy-hash", "");

        var changedLegacy = InspectPolicy(Compile("R13c", "R13-ungated.cs.txt", true), MutateFirstLegacy(Policy()));
        Has(changedLegacy, "legacy-digest-mismatch", "");
        Lacks(changedLegacy, "policy-hash", "");
    }

    [Test]
    public void Marker_pin_and_origin_are_enforced()
    {
        var mutated = File.ReadAllText(Path.Combine(RepoRoot(), "tests", "Shared", "NativeLinuxOnlyAttribute.cs"))
            .Replace("IsLinux", "IsLinuxX", StringComparison.Ordinal);
        var pinAssembly = CompileSource("R14Pin", "public class PinHost { }", referenceHost: false, mutated);
        var pin = InspectAssembly(pinAssembly, Policy(), checkPin: true, baseline: "", suite: "antiphon");
        Has(pin, "attribute-pin", "module");
        pin.Diagnostics.Any(item => item.Code == "attribute-pin" && item.Detail != "module" && item.Detail != "shape" && item.Detail != "usage")
            .ShouldBeTrue(pin.Text);

        var reasonAssembly = Compile("R14Reason", "R14-reason.cs.txt", true);
        var wrong = InspectAssembly(reasonAssembly, Policy(Row("ReasonHost", Prefix)), checkPin: false, baseline: Baseline("ReasonHost", "Run(System.Int32)", "Linux-only by construction: frozenX", true));
        Has(wrong, "reason-mismatch", "Run(System.Int32)");

        var omitted = InspectAssembly(reasonAssembly, Policy(Row("ReasonHost", Prefix)), checkPin: false, baseline: Baseline("ReasonHost", "Run(System.Int32)", null, false));
        Has(omitted, "baseline-omitted-reason", "Run(System.Int32)");

        var matched = InspectAssembly(reasonAssembly, Policy(Row("ReasonHost", Prefix)), checkPin: false, baseline: Baseline("ReasonHost", "Run(System.Int32)", "Linux-only by construction: frozen", true));
        matched.Success.ShouldBeTrue(matched.Text);

        var oneSided = InspectAssembly(reasonAssembly, Policy(Row("ReasonHost", Prefix)), checkPin: false, baseline: Baseline("ReasonHost", "Run(System.Int32)", "Linux-only by construction: other", true));
        Has(oneSided, "reason-mismatch", "Linux-only by construction: frozen");

        var unresolved = InspectAssembly(
            reasonAssembly,
            Policy(),
            checkPin: false,
            baseline: Baseline("Missing.Type", "Run()", "x", true, "session-runner"),
            suite: "session-runner");
        Has(unresolved, "baseline-unresolved", "Missing.Type");

        var body = Compile("R14Body", "R14-body.cs.txt", true);
        var portable = InspectAssembly(body, Policy(Row("BodyHost", Prefix)), checkPin: false, baseline: Baseline("BodyHost", "Run()", "Linux-only by construction: body", true));
        portable.Success.ShouldBeTrue(portable.Text);

        var before = Fixture("R14-before.cs.txt");
        var marker = Fixture("R14-marker.cs.txt");
        var edited = Fixture("R14-body.cs.txt");
        NativeLinuxWindowsReasonAudit.CompareAnnotationDelta(before, marker).ShouldBeEmpty();
        NativeLinuxWindowsReasonAudit.CompareAnnotationDelta(Fixture("R14-before-using.cs.txt"), Fixture("R14-after-using.cs.txt")).ShouldBeEmpty();
        NativeLinuxWindowsReasonAudit.CompareAnnotationDelta(before, edited).ShouldNotBeEmpty();
    }

    [Test]
    public void Markers_cannot_remove_portable_siblings()
    {
        var selected = Row("SiblingHost", Prefix, "Selected");
        var report = Inspect("R15", "R15-siblings.cs.txt", true,
            selected,
            selected,
            Row("Wholesale", Prefix, "InRow"));
        Has(report, "reverse-coverage", "Sibling()");
        Has(report, "reverse-coverage", "OutOfRow()");
        Has(report, "duplicate-row", "SiblingHost");
        Lacks(report, "missing-marker", "Selected()");
        Lacks(report, "missing-marker", "InRow()");
    }

    static InspectionReport Inspect(string name, string fixture, bool referenceHost, params string[] rows) =>
        InspectAssembly(Compile(name, fixture, referenceHost), Policy(rows), checkPin: false, baseline: "");

    static InspectionReport InspectAssembly(Assembly assembly, string policy, bool checkPin, string baseline, string suite = "antiphon") =>
        NativeLinuxPolicyWitness.Inspect(new InspectionRequest(
            assembly,
            suite,
            policy,
            baseline,
            CompareBaseline: baseline.Length > 0,
            CheckAttributePin: checkPin));

    static InspectionReport InspectPolicy(Assembly assembly, string policy) =>
        InspectAssembly(assembly, policy, checkPin: false, baseline: "");

    static InspectionReport InspectBroken()
    {
        var directory = Path.Combine(Path.GetTempPath(), "c1613-r12-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(directory);
        try
        {
            var dependency = Path.Combine(directory, "NativeLinuxMissingDep.dll");
            var dependencyCompilation = CompileTrees("NativeLinuxMissingDep", Fixture("R12-dep.cs.txt"), referenceHost: false, dependencyReference: null);
            Emit(dependencyCompilation, dependency);
            var user = CompileTrees("R12Broken", Fixture("R12-broken.cs.txt"), referenceHost: false, dependencyReference: dependency);
            var assembly = Load(user, "NativeLinuxMissingDep");
            return InspectAssembly(assembly, Policy(Row("Broken", Prefix)), checkPin: false, baseline: "");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    static Assembly Compile(string name, string fixture, bool referenceHost) =>
        Load(CompileTrees(name, Fixture(fixture), referenceHost, dependencyReference: null), blockedName: null);

    static Assembly CompileSource(string name, string source, bool referenceHost, string? resource) =>
        Load(CompileTrees(name, source, referenceHost, dependencyReference: null), blockedName: null, resource);

    static CSharpCompilation CompileTrees(string name, string source, bool referenceHost, string? dependencyReference)
    {
        var references = TrustedReferences(referenceHost);
        if (dependencyReference is not null)
            references.Add(MetadataReference.CreateFromFile(dependencyReference));
        return CSharpCompilation.Create(
            name,
            [CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview))],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    static void Emit(CSharpCompilation compilation, string path)
    {
        using var stream = File.Create(path);
        var emit = compilation.Emit(stream);
        emit.Success.ShouldBeTrue(string.Join("\n", emit.Diagnostics));
    }

    static Assembly Load(CSharpCompilation compilation, string? blockedName, string? resource = null)
    {
        using var pe = new MemoryStream();
        var emit = EmitTo(compilation, pe, resource);
        emit.Success.ShouldBeTrue(string.Join("\n", emit.Diagnostics));
        pe.Position = 0;
        return new FixtureLoadContext(blockedName).LoadFromStream(pe);
    }

    static EmitResult EmitTo(CSharpCompilation compilation, Stream stream, string? resource)
    {
        if (resource is null)
            return compilation.Emit(stream);
        var bytes = Encoding.UTF8.GetBytes(resource);
        return compilation.Emit(stream, manifestResources:
        [
            new ResourceDescription(NativeLinuxPolicyWitness.AttributeResourceName, () => new MemoryStream(bytes), isPublic: true),
        ]);
    }

    static List<MetadataReference> TrustedReferences(bool referenceHost)
    {
        var references = new List<MetadataReference>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var path in ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            if (seen.Add(path))
                references.Add(MetadataReference.CreateFromFile(path));
        }
        var tunit = typeof(TestAttribute).Assembly.Location;
        if (seen.Add(tunit))
            references.Add(MetadataReference.CreateFromFile(tunit));
        if (referenceHost)
        {
            var host = typeof(NativeLinuxOnlyAttribute).Assembly.Location;
            if (seen.Add(host))
                references.Add(MetadataReference.CreateFromFile(host));
        }
        return references;
    }

    static string Policy(params string[] extras)
    {
        var policy = File.ReadAllText(Path.Combine(RepoRoot(), "tests", "test-execution-policy.json"));
        using var document = JsonDocument.Parse(policy);
        var witnessed = new HashSet<string>(NativeLinuxPolicyWitness.WitnessedRowKeys, StringComparer.Ordinal);
        var kept = new List<string>();
        foreach (var element in document.RootElement.GetProperty("profiles").GetProperty("rc").GetProperty("exclusions").EnumerateArray())
        {
            var suite = element.GetProperty("suite").GetString() ?? "";
            var klass = element.GetProperty("class").GetString() ?? "";
            var methods = element.TryGetProperty("methods", out var methodElement) && methodElement.ValueKind == JsonValueKind.Array
                ? methodElement.EnumerateArray().Select(item => item.GetString() ?? "").Where(item => item.Length > 0).ToList()
                : new List<string>();
            var key = suite + "\n" + klass + "\n" + string.Join("\n", methods);
            if (witnessed.Contains(key))
                continue;
            kept.Add(element.GetRawText());
        }
        kept.AddRange(extras);
        return NativeLinuxPolicyWitness.SealPolicyHash(ReplaceRcExclusions(policy, "[" + string.Join(",", kept) + "]"));
    }

    static string MutateFirstLegacy(string policy)
    {
        using var document = JsonDocument.Parse(policy);
        var first = document.RootElement.GetProperty("profiles").GetProperty("rc").GetProperty("exclusions")[0].GetRawText();
        var changed = first.Replace("canary workflow", "canary workflow changed", StringComparison.Ordinal);
        changed.ShouldNotBe(first);
        var rc = policy.IndexOf("\"rc\":", StringComparison.Ordinal);
        var at = policy.IndexOf(first, rc, StringComparison.Ordinal);
        return NativeLinuxPolicyWitness.SealPolicyHash(policy[..at] + changed + policy[(at + first.Length)..]);
    }

    static string ReplaceRcExclusions(string policy, string arrayLiteral)
    {
        var rc = policy.IndexOf("\"rc\":", StringComparison.Ordinal);
        var key = policy.IndexOf("\"exclusions\"", rc, StringComparison.Ordinal);
        var start = policy.IndexOf('[', key);
        var end = MatchingBracket(policy, start);
        return policy[..start] + arrayLiteral + policy[(end + 1)..];
    }

    static int MatchingBracket(string text, int open)
    {
        var depth = 0;
        var inString = false;
        var escape = false;
        for (var i = open; i < text.Length; i++)
        {
            var ch = text[i];
            if (inString)
            {
                if (escape) { escape = false; continue; }
                if (ch == '\\') { escape = true; continue; }
                if (ch == '"') inString = false;
                continue;
            }
            if (ch == '"') { inString = true; continue; }
            if (ch == '[') depth++;
            else if (ch == ']')
            {
                depth--;
                if (depth == 0)
                    return i;
            }
        }
        throw new InvalidOperationException("unbalanced exclusions");
    }

    static string Row(string klass, string reason, params string[] methods) =>
        Exclusion("antiphon", klass, reason, methods);

    static string OtherSuite(string suite, string klass) =>
        Exclusion(suite, klass, Prefix, []);

    static string Exclusion(string suite, string klass, string reason, string[] methods)
    {
        var methodJson = methods.Length == 0
            ? ""
            : ",\"methods\":[" + string.Join(",", methods.Select(item => "\"" + item + "\"")) + "]";
        return "{\"suite\":\"" + suite + "\",\"class\":\"" + klass + "\",\"platform\":\"windows\",\"reason\":\"" + reason + "\",\"owner\":\"CARD-1613\"" + methodJson + "}";
    }

    static string Baseline(string concrete, string signature, string? reason, bool includeReason, string suite = "antiphon")
    {
        var roster = "{\"Suite\":\"" + suite + "\",\"Concrete\":\"" + concrete + "\",\"Declaring\":\"" + concrete + "\",\"Signature\":\"" + signature + "\",\"RowClass\":\"" + concrete + "\"}";
        var reasons = includeReason
            ? "{\"Suite\":\"" + suite + "\",\"Concrete\":\"" + concrete + "\",\"Declaring\":\"" + concrete + "\",\"Signature\":\"" + signature + "\",\"Reason\":\"" + reason + "\"}"
            : "";
        return "{\"Roster\":[" + roster + "],\"Reasons\":[" + reasons + "]}";
    }

    static string Fixture(string fileName)
    {
        var assembly = typeof(NativeLinuxPolicyWitnessMutationTests).Assembly;
        var names = assembly.GetManifestResourceNames();
        var name = names.SingleOrDefault(item => item.EndsWith(fileName, StringComparison.Ordinal));
        if (name is null)
            throw new InvalidOperationException("missing fixture " + fileName + " in " + string.Join(",", names));
        using var stream = assembly.GetManifestResourceStream(name)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Antiphon.sln")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("Antiphon.sln was not found from " + AppContext.BaseDirectory);
    }

    static void Has(InspectionReport report, string code, string fragment) =>
        report.Diagnostics.Any(item => item.Code == code && item.ToString().Contains(fragment, StringComparison.Ordinal))
            .ShouldBeTrue(report.Text);

    static void Lacks(InspectionReport report, string code, string fragment) =>
        report.Diagnostics.Any(item => item.Code == code && (fragment.Length == 0 || item.ToString().Contains(fragment, StringComparison.Ordinal)))
            .ShouldBeFalse(report.Text);

    sealed class FixtureLoadContext(string? blockedName) : AssemblyLoadContext(isCollectible: true)
    {
        protected override Assembly? Load(AssemblyName assemblyName)
        {
            if (blockedName is not null && string.Equals(assemblyName.Name, blockedName, StringComparison.Ordinal))
                return null;
            return Default.Assemblies.FirstOrDefault(candidate =>
                string.Equals(candidate.GetName().Name, assemblyName.Name, StringComparison.Ordinal));
        }
    }
}
