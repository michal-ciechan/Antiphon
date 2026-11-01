using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Antiphon.NativeLinuxMigrationAudit;

public static class NativeLinuxWindowsReasonAudit
{
    public const string LinuxReasonPrefix = "Linux-only by construction:";

    public static BaselineDocument ExtractRepository(string repoRoot, string policyPath, string preS1Sha)
    {
        var policyJson = File.ReadAllText(policyPath);
        var rows = ReadRows(policyJson).Where(static row => row.Witnessed).ToList();
        var problems = new List<string>();
        if (rows.Count != 45)
            problems.Add("witnessed-row-count: expected 45, found " + rows.Count);

        var roster = new List<RosterEntry>();
        var reasons = new List<ReasonEntry>();
        var placements = new List<PlacementEntry>();
        foreach (var (suite, relative) in Suites)
        {
            var suiteRows = rows.Where(row => row.Suite == suite).ToList();
            if (suiteRows.Count == 0)
                continue;
            var root = Path.Combine(repoRoot, relative);
            Console.WriteLine("compile " + suite + " " + root);
            var compilation = CompileSuite(root);
            var folder = new WindowsFolder(compilation);
            var tests = folder.Tests().ToList();
            Console.WriteLine("tests " + suite + " " + tests.Count);
            foreach (var row in suiteRows)
            {
                var matched = tests.Where(test => RowSelects(row, test)).ToList();
                if (matched.Count == 0)
                    problems.Add("unresolved-row: " + row.Key.Replace('\n', '|'));
                foreach (var name in row.Methods)
                {
                    if (!matched.Any(test => test.Method.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                        problems.Add("unresolved-method: " + row.Suite + "|" + row.Class + "|" + name);
                }

                var folded = new List<(TestNode Node, Step Step)>();
                foreach (var node in matched)
                {
                    var step = folder.Fold(node);
                    folded.Add((node, step));
                    if (step.Flow == Flow.Unproved)
                    {
                        problems.Add("unproved: " + node.Identity(suite) + " " + step.Why + " at " + step.Site);
                        continue;
                    }
                    if (step.Flow != Flow.Skip || string.IsNullOrEmpty(step.Reason))
                    {
                        problems.Add("no-windows-skip: " + node.Identity(suite) + " " + step.Why);
                        continue;
                    }
                    var identity = node.Identity(suite);
                    roster.Add(new RosterEntry(suite, node.Concrete, node.Declaring, node.Signature, row.Class, row.Methods));
                    reasons.Add(new ReasonEntry(
                        suite, node.Concrete, node.Declaring, node.Signature,
                        step.Reason, Sha256Utf8(step.Reason), "NotExecuted", step.Site ?? "", step.Via ?? ""));
                    if (reasons.Count(item => item.Identity == identity) > 1)
                        problems.Add("duplicate-identity: " + identity);
                }

                var proved = folded.Where(item => item.Step.Flow == Flow.Skip && !string.IsNullOrEmpty(item.Step.Reason)).ToList();
                var distinct = proved.Select(item => item.Step.Reason!).Distinct(StringComparer.Ordinal).ToList();
                string placement;
                string? classReason = null;
                if (row.Methods.Count > 0)
                    placement = "method";
                else if (distinct.Count == 1 && proved.Count == matched.Count && matched.Count > 0)
                {
                    placement = "class";
                    classReason = distinct[0];
                }
                else
                    placement = "method";
                placements.Add(new PlacementEntry(suite, row.Class, row.Methods, placement, classReason, proved.Count));
            }
        }

        if (placements.Count != 45)
            problems.Add("placement-count: " + placements.Count);
        var rosterKeys = roster.Select(static item => item.Identity).Order(StringComparer.Ordinal).ToList();
        var reasonKeys = reasons.Select(static item => item.Identity).Order(StringComparer.Ordinal).ToList();
        if (!rosterKeys.SequenceEqual(reasonKeys, StringComparer.Ordinal))
            problems.Add("roster-reason-bijection");
        return new BaselineDocument(preS1Sha, policyPath, roster, reasons, placements, problems);
    }

    public static IReadOnlyList<PolicyRow> ReadRows(string policyJson)
    {
        using var document = JsonDocument.Parse(policyJson);
        var rows = new List<PolicyRow>();
        foreach (var element in document.RootElement.GetProperty("profiles").GetProperty("rc").GetProperty("exclusions").EnumerateArray())
        {
            var suite = element.GetProperty("suite").GetString() ?? "";
            var klass = element.GetProperty("class").GetString() ?? "";
            var methods = element.TryGetProperty("methods", out var methodElement)
                ? methodElement.EnumerateArray().Select(static item => item.GetString() ?? "").ToList()
                : new List<string>();
            var platform = element.TryGetProperty("platform", out var platformElement) ? platformElement.GetString() : null;
            var reason = element.GetProperty("reason").GetString() ?? "";
            var owner = element.TryGetProperty("owner", out var ownerElement) ? ownerElement.GetString() ?? "" : "";
            var witnessed = platform == "windows" && reason.StartsWith(LinuxReasonPrefix, StringComparison.Ordinal);
            rows.Add(new PolicyRow(suite, klass, methods, platform, reason, owner, RowKey(suite, klass, methods), witnessed));
        }
        return rows;
    }

    public static string RowKey(string suite, string klass, IReadOnlyList<string> methods) =>
        suite + "\n" + klass + "\n" + string.Join("\n", methods);

    public static LegacyPayload LegacyCanonicalRecords(string policyJson, IReadOnlySet<string> witnessedKeys)
    {
        using var document = JsonDocument.Parse(policyJson);
        var parts = new List<string>();
        foreach (var element in document.RootElement.GetProperty("profiles").GetProperty("rc").GetProperty("exclusions").EnumerateArray())
        {
            var suite = element.GetProperty("suite").GetString() ?? "";
            var klass = element.GetProperty("class").GetString() ?? "";
            var methods = element.TryGetProperty("methods", out var methodElement)
                ? methodElement.EnumerateArray().Select(static item => item.GetString() ?? "").ToList()
                : new List<string>();
            if (witnessedKeys.Contains(RowKey(suite, klass, methods)))
                continue;
            parts.Add(Canonical(element));
        }
        return new LegacyPayload(parts.Count, "[" + string.Join(",", parts) + "]");
    }

    public static string PolicyCanonical(string policyJson)
    {
        using var document = JsonDocument.Parse(policyJson);
        return Canonical(document.RootElement, "policyHash");
    }

    public static string Sha256Utf8(string text) => Sha256Utf8(Encoding.UTF8.GetBytes(text));

    public static string Sha256Utf8(byte[] bytes)
    {
        var hash = SHA256.HashData(bytes);
        var builder = new StringBuilder(hash.Length * 2);
        foreach (var part in hash)
            builder.Append(part.ToString("x2"));
        return builder.ToString();
    }

    public static IReadOnlyList<string> CompareAnnotationDelta(string before, string after)
    {
        var stripUsing = HasSupportUsing(after) && !HasSupportUsing(before);
        var beforeTokens = NormalizedTokens(before, stripMarker: false, stripSupportUsing: false);
        var afterTokens = NormalizedTokens(after, stripMarker: true, stripSupportUsing: stripUsing);
        var diffs = new List<string>();
        var limit = Math.Max(beforeTokens.Count, afterTokens.Count);
        for (var i = 0; i < limit; i++)
        {
            var left = i < beforeTokens.Count ? beforeTokens[i] : "<end>";
            var right = i < afterTokens.Count ? afterTokens[i] : "<end>";
            if (!string.Equals(left, right, StringComparison.Ordinal))
            {
                diffs.Add("token " + i + ": [" + left + "] != [" + right + "]");
                if (diffs.Count >= 20)
                    break;
            }
        }
        return diffs;
    }

    public static string FormatType(ITypeSymbol type)
    {
        if (type is IArrayTypeSymbol array)
            return FormatType(array.ElementType) + "[" + new string(',', Math.Max(0, array.Rank - 1)) + "]";
        if (type is IPointerTypeSymbol pointer)
            return FormatType(pointer.PointedAtType) + "*";
        if (type is INamedTypeSymbol named)
        {
            if (named.ContainingType is { } parent)
                return FormatType(parent) + "+" + named.MetadataName;
            var ns = named.ContainingNamespace;
            if (ns is null || ns.IsGlobalNamespace)
                return named.MetadataName;
            return ns.ToDisplayString() + "." + named.MetadataName;
        }
        return type is ITypeParameterSymbol parameter ? parameter.Name : type.Name;
    }

    public static string MethodSignature(IMethodSymbol method)
    {
        var builder = new StringBuilder();
        builder.Append(method.Name);
        if (method.IsGenericMethod)
            builder.Append('`').Append(method.Arity);
        builder.Append('(');
        for (var i = 0; i < method.Parameters.Length; i++)
        {
            if (i > 0)
                builder.Append(',');
            var parameter = method.Parameters[i];
            builder.Append(FormatType(parameter.Type));
            if (parameter.RefKind is RefKind.Ref or RefKind.Out or RefKind.In)
                builder.Append('&');
        }
        builder.Append(')');
        return builder.ToString();
    }

    static bool HasSupportUsing(string source) =>
        HadSupportUsing(CSharpSyntaxTree.ParseText(source).GetRoot());

    static List<string> NormalizedTokens(string source, bool stripMarker, bool stripSupportUsing)
    {
        var root = CSharpSyntaxTree.ParseText(source).GetRoot();
        if (stripMarker)
            root = new MarkerStripper().Visit(root)!;
        if (stripSupportUsing)
            root = new UsingStripper().Visit(root)!;
        return root.DescendantTokens().Select(static token => token.ValueText).ToList();
    }

    static bool HadSupportUsing(SyntaxNode root) =>
        root.DescendantNodes().OfType<UsingDirectiveSyntax>().Any(static item => item.Name?.ToString() == "Antiphon.TestSupport");

    static string Canonical(JsonElement element, string? skipProperty = null)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Null:
                return "null";
            case JsonValueKind.String:
                return Quote(element.GetString() ?? "");
            case JsonValueKind.True:
                return "true";
            case JsonValueKind.False:
                return "false";
            case JsonValueKind.Number:
                return element.GetRawText();
            case JsonValueKind.Array:
                return "[" + string.Join(",", element.EnumerateArray().Select(item => Canonical(item))) + "]";
            case JsonValueKind.Object:
                var seen = new HashSet<string>(StringComparer.Ordinal);
                var pairs = new List<(string Key, JsonElement Value)>();
                foreach (var property in element.EnumerateObject())
                {
                    if (property.Name.Length == 0 || property.Name == skipProperty || !seen.Add(property.Name))
                        continue;
                    pairs.Add((property.Name, property.Value));
                }
                pairs.Sort(static (left, right) => string.Compare(
                    left.Key,
                    right.Key,
                    StringComparison.CurrentCultureIgnoreCase));
                return "{" + string.Join(",", pairs.Select(pair => Quote(pair.Key) + ":" + Canonical(pair.Value))) + "}";
            default:
                throw new InvalidOperationException("unsupported JSON " + element.ValueKind);
        }
    }

    static string Quote(string text)
    {
        var builder = new StringBuilder(text.Length + 2);
        builder.Append('"');
        foreach (var ch in text)
        {
            switch (ch)
            {
                case '\\': builder.Append("\\\\"); break;
                case '"': builder.Append("\\\""); break;
                case '\r': builder.Append("\\r"); break;
                case '\n': builder.Append("\\n"); break;
                case '\t': builder.Append("\\t"); break;
                default: builder.Append(ch); break;
            }
        }
        builder.Append('"');
        return builder.ToString();
    }

    static bool RowSelects(PolicyRow row, TestNode test)
    {
        var simple = SimpleIdentity(test.Concrete);
        var full = test.Concrete;
        if (!simple.Equals(row.Class, StringComparison.OrdinalIgnoreCase)
            && !full.Equals(row.Class, StringComparison.OrdinalIgnoreCase))
            return false;
        return row.Methods.Count == 0
            || row.Methods.Any(name => test.Method.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    static string SimpleIdentity(string typeKey)
    {
        var dot = typeKey.LastIndexOf('.');
        return dot < 0 ? typeKey : typeKey[(dot + 1)..];
    }

    static CSharpCompilation CompileSuite(string root)
    {
        var paths = Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(static path => !path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Any(static part => part is "bin" or "obj" || part.StartsWith("bin-", StringComparison.Ordinal)))
            .Order(StringComparer.Ordinal)
            .ToList();
        var options = new CSharpParseOptions(
            LanguageVersion.Preview,
            preprocessorSymbols: ["DEBUG", "TRACE", "NET", "NET9_0", "NET9_0_OR_GREATER", "NET8_0_OR_GREATER", "NET7_0_OR_GREATER", "NET6_0_OR_GREATER", "NET5_0_OR_GREATER", "NETCOREAPP", "NETCOREAPP3_1_OR_GREATER"]);
        var trees = new List<SyntaxTree>
        {
            CSharpSyntaxTree.ParseText("""
                global using System;
                global using System.Collections.Generic;
                global using System.IO;
                global using System.Linq;
                global using System.Net.Http;
                global using System.Threading;
                global using System.Threading.Tasks;
                """, options, path: "implicit-usings.cs"),
        };
        trees.AddRange(paths.Select(path => CSharpSyntaxTree.ParseText(File.ReadAllText(path), options, path)));
        trees.Add(CSharpSyntaxTree.ParseText("""
            namespace Antiphon.Server.Domain.Enums
            {
                public enum AgentTaskRole
                {
                    Custom,
                    Plan,
                    Code,
                    TestDesign,
                    Mutation,
                    Review,
                    Investigate,
                }
            }
            """, options, path: "agent-task-role-stub.cs"));
        var references = TrustedReferences();
        return CSharpCompilation.Create(
            "native-linux-audit",
            trees,
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true, nullableContextOptions: NullableContextOptions.Enable));
    }

    static List<MetadataReference> TrustedReferences()
    {
        var references = new List<MetadataReference>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var trusted = (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        foreach (var path in trusted)
            AddReference(references, seen, path);
        var tunit = "/home/app/.nuget/packages/tunit.core/1.44.0/lib/net9.0/TUnit.Core.dll";
        AddReference(references, seen, tunit);
        return references;
    }

    static void AddReference(List<MetadataReference> references, HashSet<string> seen, string path)
    {
        if (!seen.Add(path) || !File.Exists(path))
            return;
        references.Add(MetadataReference.CreateFromFile(path));
    }

    static readonly (string Suite, string Relative)[] Suites =
    [
        ("antiphon", "tests/Antiphon.Tests"),
        ("session-runner", "tests/Antiphon.SessionRunner.Tests"),
        ("pty-host", "tests/Antiphon.PtyHost.Tests"),
        ("agents-pty", "tests/Antiphon.Agents.Pty.Tests"),
    ];

    sealed class MarkerStripper : CSharpSyntaxRewriter
    {
        public override SyntaxNode? VisitAttributeList(AttributeListSyntax node)
        {
            var kept = node.Attributes.Where(static attribute => attribute.Name.ToString() is not "NativeLinuxOnly" and not "Antiphon.TestSupport.NativeLinuxOnly").ToList();
            if (kept.Count == 0)
                return null;
            if (kept.Count == node.Attributes.Count)
                return base.VisitAttributeList(node);
            return base.VisitAttributeList(node.WithAttributes(SyntaxFactory.SeparatedList(kept)));
        }
    }

    sealed class UsingStripper : CSharpSyntaxRewriter
    {
        public override SyntaxNode? VisitUsingDirective(UsingDirectiveSyntax node)
        {
            if (node.Name?.ToString() == "Antiphon.TestSupport")
                return null;
            return base.VisitUsingDirective(node);
        }
    }
}

public sealed record PolicyRow(
    string Suite,
    string Class,
    IReadOnlyList<string> Methods,
    string? Platform,
    string Reason,
    string Owner,
    string Key,
    bool Witnessed);

public sealed record LegacyPayload(int Count, string Payload);

public sealed record RosterEntry(
    string Suite,
    string Concrete,
    string Declaring,
    string Signature,
    string RowClass,
    IReadOnlyList<string> RowMethods)
{
    public string Identity => Suite + "|" + Concrete + "|" + Declaring + "|" + Signature;
}

public sealed record ReasonEntry(
    string Suite,
    string Concrete,
    string Declaring,
    string Signature,
    string Reason,
    string ReasonSha256,
    string WindowsOutcomeAtGate,
    string Site,
    string Via)
{
    public string Identity => Suite + "|" + Concrete + "|" + Declaring + "|" + Signature;
}

public sealed record PlacementEntry(
    string Suite,
    string Class,
    IReadOnlyList<string> Methods,
    string Placement,
    string? Reason,
    int Proved);

public sealed record BaselineDocument(
    string PreS1Sha,
    string PolicyPath,
    IReadOnlyList<RosterEntry> Roster,
    IReadOnlyList<ReasonEntry> Reasons,
    IReadOnlyList<PlacementEntry> Placements,
    IReadOnlyList<string> Problems)
{
    public string ToJson()
    {
        var options = new JsonSerializerOptions { WriteIndented = true };
        return JsonSerializer.Serialize(this, options) + "\n";
    }
}

enum Flow { Next, Skip, Unproved, Exit, Bool, Awaited, MaybeExit }

sealed class Step
{
    public Flow Flow { get; private init; }
    public string? Reason { get; private init; }
    public string? Site { get; private init; }
    public string? Why { get; private init; }
    public string? Via { get; private init; }
    public bool Bool { get; private init; }
    public Val Value { get; private init; }

    public static Step Next(Val value = default) => new() { Flow = Flow.Next, Value = value };
    public static Step Skip(string reason, string site, string via) => new() { Flow = Flow.Skip, Reason = reason, Site = site, Via = via };
    public static Step Unproved(string why, string? site = null) => new() { Flow = Flow.Unproved, Why = why, Site = site };
    public static Step Exit() => new() { Flow = Flow.Exit };
    public static Step Returned(bool value) => new() { Flow = Flow.Bool, Bool = value };
    public static Step Awaited() => new() { Flow = Flow.Awaited };
    public static Step MaybeExit() => new() { Flow = Flow.MaybeExit };
}

readonly struct Val
{
    public bool Known { get; init; }
    public bool IsBool { get; init; }
    public bool Bool { get; init; }
    public bool IsText { get; init; }
    public string? Text { get; init; }
    public static Val FromBool(bool value) => new() { Known = true, IsBool = true, Bool = value };
    public static Val FromText(string value) => new() { Known = true, IsText = true, Text = value };
}

sealed class TestNode
{
    public required IMethodSymbol Method { get; init; }
    public required INamedTypeSymbol ConcreteType { get; init; }
    public required string Concrete { get; init; }
    public required string Declaring { get; init; }
    public required string Signature { get; init; }
    public string Identity(string suite) => suite + "|" + Concrete + "|" + Declaring + "|" + Signature;
}

sealed class WindowsFolder
{
    readonly CSharpCompilation _compilation;
    readonly Dictionary<SyntaxTree, SemanticModel> _models = new();
    readonly Dictionary<IMethodSymbol, Step> _cache = new(SymbolEqualityComparer.Default);
    int _inlineDepth;

    public WindowsFolder(CSharpCompilation compilation) => _compilation = compilation;

    public IEnumerable<TestNode> Tests()
    {
        foreach (var type in AllTypes())
        {
            if (type.TypeKind is not (TypeKind.Class or TypeKind.Struct) || type.IsAbstract && !type.IsStatic)
                continue;
            foreach (var method in TestMethods(type))
            {
                yield return new TestNode
                {
                    Method = method,
                    ConcreteType = type,
                    Concrete = NativeLinuxWindowsReasonAudit.FormatType(type),
                    Declaring = NativeLinuxWindowsReasonAudit.FormatType(method.ContainingType),
                    Signature = NativeLinuxWindowsReasonAudit.MethodSignature(method),
                };
            }
        }
    }

    public Step Fold(TestNode node)
    {
        var steps = new List<Step> { WalkConstruction(node.ConcreteType) };
        foreach (var hook in BeforeHooks(node.ConcreteType))
            steps.Add(WalkMethod(hook, ImmutableDictionary<string, Val>.Empty, node.Method.Name));
        steps.Add(WalkMethod(node.Method, ImmutableDictionary<string, Val>.Empty, node.Method.Name));
        foreach (var step in steps)
        {
            if (step.Flow is Flow.Skip or Flow.Unproved)
                return step;
        }
        return steps.LastOrDefault() ?? Step.Unproved("empty test");
    }

    IEnumerable<INamedTypeSymbol> AllTypes()
    {
        var stack = new Stack<INamespaceOrTypeSymbol>();
        stack.Push(_compilation.GlobalNamespace);
        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (current is INamespaceSymbol ns)
            {
                foreach (var child in ns.GetNamespaceMembers())
                    stack.Push(child);
                foreach (var type in ns.GetTypeMembers())
                    stack.Push(type);
            }
            else if (current is INamedTypeSymbol type)
            {
                yield return type;
                foreach (var nested in type.GetTypeMembers())
                    stack.Push(nested);
            }
        }
    }

    IEnumerable<IMethodSymbol> TestMethods(INamedTypeSymbol concrete)
    {
        var types = new List<INamedTypeSymbol>();
        if (HasInherits(concrete))
        {
            for (var cursor = concrete; cursor is not null && cursor.SpecialType != SpecialType.System_Object; cursor = cursor.BaseType)
                types.Add(cursor);
            types.Reverse();
        }
        else
            types.Add(concrete);

        var chosen = new Dictionary<string, IMethodSymbol>(StringComparer.Ordinal);
        foreach (var type in types)
        {
            foreach (var method in type.GetMembers().OfType<IMethodSymbol>())
            {
                if (method.MethodKind != MethodKind.Ordinary || !IsTest(method))
                    continue;
                chosen[NativeLinuxWindowsReasonAudit.MethodSignature(method)] = method;
            }
        }
        return chosen.Values;
    }

    static bool HasInherits(INamedTypeSymbol type) =>
        type.GetAttributes().Any(static attribute => attribute.AttributeClass?.Name == "InheritsTestsAttribute");

    static bool IsTest(IMethodSymbol method)
    {
        for (var cursor = method; cursor is not null; cursor = cursor.OverriddenMethod)
        {
            if (cursor.GetAttributes().Any(static attribute =>
                    attribute.AttributeClass?.Name == "TestAttribute"
                    && attribute.AttributeClass.ContainingNamespace?.ToDisplayString() == "TUnit.Core"))
                return true;
        }
        return DeclaredTest(method);
    }

    static bool DeclaredTest(IMethodSymbol method)
    {
        foreach (var reference in method.DeclaringSyntaxReferences)
        {
            if (reference.GetSyntax() is MethodDeclarationSyntax syntax
                && syntax.AttributeLists.SelectMany(static list => list.Attributes).Any(static attribute => attribute.Name.ToString() is "Test" or "TUnit.Core.Test"))
                return true;
        }
        return false;
    }

    IEnumerable<IMethodSymbol> BeforeHooks(INamedTypeSymbol type)
    {
        var chain = new List<INamedTypeSymbol>();
        for (var cursor = type; cursor is not null && cursor.SpecialType != SpecialType.System_Object; cursor = cursor.BaseType)
            chain.Add(cursor);
        chain.Reverse();
        foreach (var cursor in chain)
        {
            foreach (var method in cursor.GetMembers().OfType<IMethodSymbol>()
                         .Where(IsBeforeTest)
                         .OrderBy(static method => method.Locations.FirstOrDefault()?.SourceSpan.Start ?? 0))
                yield return method;
        }
    }

    static bool IsBeforeTest(IMethodSymbol method)
    {
        foreach (var attribute in method.GetAttributes())
        {
            if (attribute.AttributeClass?.Name is not ("BeforeAttribute" or "Before"))
                continue;
            if (attribute.ConstructorArguments.Length > 0 && IsTestHookValue(attribute.ConstructorArguments[0]))
                return true;
        }
        foreach (var reference in method.DeclaringSyntaxReferences)
        {
            if (reference.GetSyntax() is not MethodDeclarationSyntax syntax)
                continue;
            foreach (var attribute in syntax.AttributeLists.SelectMany(static list => list.Attributes))
            {
                var name = attribute.Name.ToString();
                if (name is not ("Before" or "BeforeAttribute")
                    && !name.EndsWith(".Before", StringComparison.Ordinal)
                    && !name.EndsWith(".BeforeAttribute", StringComparison.Ordinal))
                    continue;
                var argument = attribute.ArgumentList?.Arguments.FirstOrDefault()?.ToString() ?? "";
                if (argument is "Test" or "HookType.Test" || argument.EndsWith(".Test", StringComparison.Ordinal))
                    return true;
            }
        }
        return false;
    }

    static bool IsTestHookValue(TypedConstant argument)
    {
        // TUnit 1.44 HookType.Test is the zero value. A bound attribute reports that
        // underlying value; source text "Test" is handled by the syntax fallback.
        if (argument.Type?.Name == "HookType")
            return argument.Value is 0 or "Test";
        return argument.Value?.ToString() is "Test";
    }

    Step WalkConstruction(INamedTypeSymbol type)
    {
        var chain = new List<INamedTypeSymbol>();
        for (var cursor = type; cursor is not null && cursor.SpecialType != SpecialType.System_Object; cursor = cursor.BaseType)
            chain.Add(cursor);
        chain.Reverse();
        foreach (var cursor in chain)
        {
            foreach (var field in cursor.GetMembers().OfType<IFieldSymbol>().Where(static field => !field.IsStatic))
            {
                foreach (var reference in field.DeclaringSyntaxReferences)
                {
                    if (reference.GetSyntax() is VariableDeclaratorSyntax declarator && declarator.Initializer is { } initializer
                        && InterestingSyntax(initializer))
                    {
                        var step = Eval(initializer.Value, Model(initializer.SyntaxTree), new Dictionary<string, Val>(StringComparer.Ordinal), ImmutableDictionary<string, Val>.Empty, cursor.Name).Item1;
                        if (step.Flow is Flow.Skip or Flow.Unproved)
                            return step;
                    }
                }
            }
            var ctor = cursor.Constructors.FirstOrDefault(static item => !item.IsStatic && item.Parameters.Length == 0);
            if (ctor is not null && ShouldInline(ctor))
            {
                var step = WalkMethod(ctor, ImmutableDictionary<string, Val>.Empty, type.Name);
                if (step.Flow is Flow.Skip or Flow.Unproved)
                    return step;
            }
        }
        return Step.Next();
    }

    static Step FromExpression((Step Step, Val Value) evaluated) =>
        evaluated.Step.Flow == Flow.Next && evaluated.Value.IsBool ? Step.Returned(evaluated.Value.Bool) : evaluated.Step;

    Step WalkMethod(IMethodSymbol method, ImmutableDictionary<string, Val> arguments, string via)
    {
        if (arguments.IsEmpty && _cache.TryGetValue(method, out var cached))
            return cached;
        if (_inlineDepth >= 12)
            return Step.Unproved("inline depth");
        if (method.DeclaringSyntaxReferences.Length == 0)
            return Step.Next();
        _inlineDepth++;
        try
        {
        return WalkMethodBody(method, arguments, via);
        }
        finally
        {
            _inlineDepth--;
        }
    }

    Step WalkMethodBody(IMethodSymbol method, ImmutableDictionary<string, Val> arguments, string via)
    {
        SyntaxNode? bodySyntax = null;
        foreach (var reference in method.DeclaringSyntaxReferences)
        {
            var syntax = reference.GetSyntax();
            if (syntax is MethodDeclarationSyntax methodSyntax && (methodSyntax.Body is not null || methodSyntax.ExpressionBody is not null)
                || syntax is ConstructorDeclarationSyntax ctor && (ctor.Body is not null || ctor.ExpressionBody is not null)
                || syntax is LocalFunctionStatementSyntax local && (local.Body is not null || local.ExpressionBody is not null))
            {
                bodySyntax = syntax;
                break;
            }
        }
        bodySyntax ??= method.DeclaringSyntaxReferences[0].GetSyntax();
        var locals = new Dictionary<string, Val>(StringComparer.Ordinal);
        var step = bodySyntax switch
        {
            MethodDeclarationSyntax methodSyntax when methodSyntax.ExpressionBody is { } arrow => FromExpression(Eval(arrow.Expression, Model(arrow.SyntaxTree), locals, arguments, via)),
            MethodDeclarationSyntax methodSyntax when methodSyntax.Body is { } body => WalkStatements(body.Statements, Model(body.SyntaxTree), locals, arguments, via),
            ConstructorDeclarationSyntax ctor when ctor.ExpressionBody is { } arrow => FromExpression(Eval(arrow.Expression, Model(arrow.SyntaxTree), locals, arguments, via)),
            ConstructorDeclarationSyntax ctor when ctor.Body is { } body => WalkStatements(body.Statements, Model(body.SyntaxTree), locals, arguments, via),
            LocalFunctionStatementSyntax local when local.ExpressionBody is { } arrow => FromExpression(Eval(arrow.Expression, Model(arrow.SyntaxTree), locals, arguments, via)),
            LocalFunctionStatementSyntax local when local.Body is { } body => WalkStatements(body.Statements, Model(local.SyntaxTree), locals, arguments, via),
            _ => Step.Next(),
        };
        if (step.Flow == Flow.Exit || step.Flow == Flow.Bool || step.Flow == Flow.Awaited || step.Flow == Flow.MaybeExit)
            step = step.Flow == Flow.Bool ? Step.Returned(step.Bool) : Step.Next();
        if (arguments.IsEmpty)
            _cache[method] = step;
        return step;
    }

    Step WalkStatements(IEnumerable<StatementSyntax> statements, SemanticModel model, Dictionary<string, Val> locals, ImmutableDictionary<string, Val> arguments, string via)
    {
        var list = statements as IReadOnlyList<StatementSyntax> ?? statements.ToList();
        var sawAwait = false;
        for (var i = 0; i < list.Count; i++)
        {
            var step = WalkStatement(list[i], model, locals, arguments, via);
            if (step.Flow == Flow.Awaited)
            {
                sawAwait = true;
                continue;
            }
            if (step.Flow == Flow.MaybeExit)
            {
                var rest = WalkStatements(list.Skip(i + 1), model, locals, arguments, via);
                if (rest.Flow is Flow.Skip or Flow.Unproved)
                    return Step.Unproved("skip after a branch that may return", rest.Site);
                return Step.Next();
            }
            if (step.Flow == Flow.Skip && sawAwait)
                return Step.Unproved("skip after await", step.Site);
            if (step.Flow is Flow.Skip or Flow.Unproved or Flow.Exit or Flow.Bool)
                return step;
        }
        return Step.Next();
    }

    Step WalkStatement(StatementSyntax statement, SemanticModel model, Dictionary<string, Val> locals, ImmutableDictionary<string, Val> arguments, string via)
    {
        switch (statement)
        {
            case BlockSyntax block:
                return WalkStatements(block.Statements, model, locals, arguments, via);
            case IfStatementSyntax ifStatement:
                return WalkIf(ifStatement, model, locals, arguments, via);
            case LocalDeclarationStatementSyntax local:
                foreach (var declarator in local.Declaration.Variables)
                {
                    if (declarator.Initializer is null)
                        continue;
                    var (step, value) = Eval(declarator.Initializer.Value, model, locals, arguments, via);
                    if (step.Flow != Flow.Next)
                        return step;
                    if (value.Known)
                        locals[declarator.Identifier.Text] = value;
                }
                return Step.Next();
            case ExpressionStatementSyntax expression:
                return Eval(expression.Expression, model, locals, arguments, via).Item1;
            case ReturnStatementSyntax returned:
                if (returned.Expression is null)
                    return Step.Exit();
                var (returnStep, returnValue) = Eval(returned.Expression, model, locals, arguments, via);
                if (returnStep.Flow != Flow.Next)
                    return returnStep;
                return returnValue.IsBool ? Step.Returned(returnValue.Bool) : Step.Exit();
            case ThrowStatementSyntax thrown:
                return WalkThrow(thrown.Expression, model, locals, arguments, via);
            case LocalFunctionStatementSyntax:
                return Step.Next();
            case TryStatementSyntax tried:
                return WalkTry(tried, model, locals, arguments, via);
            case ForStatementSyntax loop:
                return WalkLoop(loop.Statement, loop.Condition, model, locals, arguments, via);
            case ForEachStatementSyntax loop:
                return WalkLoop(loop.Statement, null, model, locals, arguments, via);
            case WhileStatementSyntax loop:
                return WalkLoop(loop.Statement, loop.Condition, model, locals, arguments, via);
            case DoStatementSyntax loop:
                return WalkLoop(loop.Statement, loop.Condition, model, locals, arguments, via);
            case LockStatementSyntax locked:
                return WalkStatement(locked.Statement, model, locals, arguments, via);
            case UsingStatementSyntax used:
                if (used.Expression is not null)
                {
                    var usedStep = Eval(used.Expression, model, locals, arguments, via).Item1;
                    if (usedStep.Flow != Flow.Next)
                        return usedStep;
                }
                return used.Statement is null ? Step.Next() : WalkStatement(used.Statement, model, locals, arguments, via);
            case UnsafeStatementSyntax unsafeStatement:
                return WalkStatement(unsafeStatement.Block, model, locals, arguments, via);
            case CheckedStatementSyntax checkedStatement:
                return WalkStatement(checkedStatement.Block, model, locals, arguments, via);
            case EmptyStatementSyntax:
                return Step.Next();
            case BreakStatementSyntax or ContinueStatementSyntax or GotoStatementSyntax:
                return Step.MaybeExit();
            default:
                return Step.Next();
        }
    }

    Step WalkIf(IfStatementSyntax statement, SemanticModel model, Dictionary<string, Val> locals, ImmutableDictionary<string, Val> arguments, string via)
    {
        var (conditionStep, condition) = Eval(statement.Condition, model, locals, arguments, via);
        if (conditionStep.Flow != Flow.Next)
            return conditionStep;
        if (condition.IsBool && condition.Bool)
            return WalkStatement(statement.Statement, model, locals, arguments, via);
        if (condition.IsBool && !condition.Bool)
            return statement.Else is null ? Step.Next() : WalkStatement(statement.Else.Statement, model, locals, arguments, via);
        var snapshot = new Dictionary<string, Val>(locals, StringComparer.Ordinal);
        var thenStep = WalkStatement(statement.Statement, model, locals, arguments, via);
        locals.Clear();
        foreach (var pair in snapshot)
            locals[pair.Key] = pair.Value;
        var elseStep = statement.Else is null ? Step.Next() : WalkStatement(statement.Else.Statement, model, locals, arguments, via);
        locals.Clear();
        foreach (var pair in snapshot)
            locals[pair.Key] = pair.Value;
        if (thenStep.Flow is Flow.Skip or Flow.Unproved || elseStep.Flow is Flow.Skip or Flow.Unproved)
            return Step.Unproved("non-dominating skip", thenStep.Site ?? elseStep.Site);
        if (thenStep.Flow is Flow.Exit or Flow.Bool or Flow.MaybeExit || elseStep.Flow is Flow.Exit or Flow.Bool or Flow.MaybeExit)
            return Step.MaybeExit();
        return Step.Next();
    }

    Step WalkLoop(StatementSyntax body, ExpressionSyntax? condition, SemanticModel model, Dictionary<string, Val> locals, ImmutableDictionary<string, Val> arguments, string via)
    {
        if (condition is not null)
        {
            var (conditionStep, value) = Eval(condition, model, locals, arguments, via);
            if (conditionStep.Flow != Flow.Next)
                return conditionStep;
            if (value.IsBool && !value.Bool)
                return Step.Next();
        }
        var snapshot = new Dictionary<string, Val>(locals, StringComparer.Ordinal);
        var hit = WalkStatement(body, model, locals, arguments, via);
        locals.Clear();
        foreach (var pair in snapshot)
            locals[pair.Key] = pair.Value;
        if (hit.Flow == Flow.Skip)
            return Step.Unproved("skip inside loop", hit.Site);
        if (hit.Flow == Flow.Unproved)
            return hit;
        if (hit.Flow is Flow.Exit or Flow.Bool or Flow.MaybeExit)
            return Step.MaybeExit();
        return Step.Next();
    }

    Step WalkTry(TryStatementSyntax statement, SemanticModel model, Dictionary<string, Val> locals, ImmutableDictionary<string, Val> arguments, string via)
    {
        var body = WalkStatements(statement.Block.Statements, model, locals, arguments, via);
        var swallows = statement.Catches.Any(clause => CatchCanSwallow(clause, model));
        if (swallows && body.Flow is Flow.Skip or Flow.Unproved)
            return Step.Unproved("skip inside a catch that can swallow it", body.Site);
        if (body.Flow != Flow.Next)
            return body;
        if (statement.Finally is { } finalizer)
        {
            var finalStep = WalkStatements(finalizer.Block.Statements, model, locals, arguments, via);
            if (finalStep.Flow == Flow.Skip)
                return Step.Unproved("skip inside finally", finalStep.Site);
            if (finalStep.Flow == Flow.Unproved)
                return finalStep;
        }
        return Step.Next();
    }

    bool CatchCanSwallow(CatchClauseSyntax clause, SemanticModel model)
    {
        if (clause.Filter is not null || clause.Declaration is null)
            return true;
        var symbol = model.GetTypeInfo(clause.Declaration.Type).Type;
        var name = symbol is null ? clause.Declaration.Type.ToString() : NativeLinuxWindowsReasonAudit.FormatType(symbol);
        return name is "Exception" or "System.Exception" or "object" or "System.Object"
            or "SkipTestException" or "TUnit.Core.Exceptions.SkipTestException";
    }

    Step WalkThrow(ExpressionSyntax? expression, SemanticModel model, Dictionary<string, Val> locals, ImmutableDictionary<string, Val> arguments, string via)
    {
        if (expression is ObjectCreationExpressionSyntax creation && IsSkipException(creation, model))
        {
            if (creation.ArgumentList is null || creation.ArgumentList.Arguments.Count != 1)
                return Step.Unproved("skip exception arity", Site(creation));
            var (step, value) = Eval(creation.ArgumentList.Arguments[0].Expression, model, locals, arguments, via);
            if (step.Flow != Flow.Next)
                return step;
            return value.IsText && value.Text is not null
                ? Step.Skip(value.Text, Site(creation), via)
                : Step.Unproved("non-constant skip reason", Site(creation));
        }
        if (expression is not null)
        {
            var thrown = Eval(expression, model, locals, arguments, via).Item1;
            if (thrown.Flow is Flow.Skip or Flow.Unproved)
                return thrown;
        }
        return Step.Exit();
    }

    (Step Step, Val Value) Eval(ExpressionSyntax expression, SemanticModel model, Dictionary<string, Val> locals, ImmutableDictionary<string, Val> arguments, string via)
    {
        switch (expression)
        {
            case ParenthesizedExpressionSyntax parenthesized:
                return Eval(parenthesized.Expression, model, locals, arguments, via);
            case LiteralExpressionSyntax literal when literal.IsKind(SyntaxKind.StringLiteralExpression):
                return (Step.Next(), Val.FromText(literal.Token.ValueText));
            case LiteralExpressionSyntax literal when literal.IsKind(SyntaxKind.TrueLiteralExpression):
                return (Step.Next(), Val.FromBool(true));
            case LiteralExpressionSyntax literal when literal.IsKind(SyntaxKind.FalseLiteralExpression):
                return (Step.Next(), Val.FromBool(false));
            case PrefixUnaryExpressionSyntax unary when unary.IsKind(SyntaxKind.LogicalNotExpression):
                var (negatedStep, negated) = Eval(unary.Operand, model, locals, arguments, via);
                if (negatedStep.Flow != Flow.Next)
                    return (negatedStep, default);
                return negated.IsBool ? (Step.Next(), Val.FromBool(!negated.Bool)) : (Step.Next(), default);
            case BinaryExpressionSyntax binary when binary.IsKind(SyntaxKind.LogicalAndExpression):
                return EvalAnd(binary, model, locals, arguments, via);
            case BinaryExpressionSyntax binary when binary.IsKind(SyntaxKind.LogicalOrExpression):
                return EvalOr(binary, model, locals, arguments, via);
            case BinaryExpressionSyntax binary when binary.IsKind(SyntaxKind.EqualsExpression) || binary.IsKind(SyntaxKind.NotEqualsExpression):
                return EvalEquality(binary, model, locals, arguments, via);
            case BinaryExpressionSyntax binary when binary.IsKind(SyntaxKind.AddExpression):
                var (leftStep, left) = Eval(binary.Left, model, locals, arguments, via);
                if (leftStep.Flow != Flow.Next)
                    return (leftStep, default);
                var (rightStep, right) = Eval(binary.Right, model, locals, arguments, via);
                if (rightStep.Flow != Flow.Next)
                    return (rightStep, default);
                return left.IsText && right.IsText && left.Text is not null && right.Text is not null
                    ? (Step.Next(), Val.FromText(left.Text + right.Text))
                    : (Step.Next(), default);
            case ConditionalExpressionSyntax conditional:
                var (condStep, cond) = Eval(conditional.Condition, model, locals, arguments, via);
                if (condStep.Flow != Flow.Next)
                    return (condStep, default);
                if (!cond.IsBool)
                    return (Step.Next(), default);
                return Eval(cond.Bool ? conditional.WhenTrue : conditional.WhenFalse, model, locals, arguments, via);
            case CastExpressionSyntax cast:
                return Eval(cast.Expression, model, locals, arguments, via);
            case IdentifierNameSyntax identifier:
                return (Step.Next(), Lookup(identifier, model, locals, arguments));
            case MemberAccessExpressionSyntax member:
                return (Step.Next(), LookupMember(member, model));
            case InvocationExpressionSyntax invocation:
                return EvalInvocation(invocation, model, locals, arguments, via);
            case ObjectCreationExpressionSyntax creation:
                return EvalCreation(creation, creation.Type, creation.ArgumentList, model, locals, arguments, via);
            case ImplicitObjectCreationExpressionSyntax creation:
                return EvalCreation(creation, null, creation.ArgumentList, model, locals, arguments, via);
            case AwaitExpressionSyntax awaited:
                var awaitedStep = Eval(awaited.Expression, model, locals, arguments, via).Item1;
                return awaitedStep.Flow == Flow.Next ? (Step.Awaited(), default) : (awaitedStep, default);
            case AssignmentExpressionSyntax assignment:
                var assigned = Eval(assignment.Right, model, locals, arguments, via);
                if (assigned.Item1.Flow == Flow.Next && assignment.Left is IdentifierNameSyntax target && assigned.Item2.Known)
                    locals[target.Identifier.Text] = assigned.Item2;
                return assigned;
            case ThrowExpressionSyntax thrown:
                var throwStep = WalkThrow(thrown, model, locals, arguments, via);
                return (throwStep, default);
            case InterpolatedStringExpressionSyntax interpolated:
                return EvalInterpolation(interpolated, model, locals, arguments, via);
            default:
                return (Step.Next(), Constant(expression, model));
        }
    }

    (Step, Val) EvalAnd(BinaryExpressionSyntax binary, SemanticModel model, Dictionary<string, Val> locals, ImmutableDictionary<string, Val> arguments, string via)
    {
        var (leftStep, left) = Eval(binary.Left, model, locals, arguments, via);
        if (leftStep.Flow != Flow.Next)
            return (leftStep, default);
        if (left.IsBool && !left.Bool)
            return (Step.Next(), Val.FromBool(false));
        var (rightStep, right) = Eval(binary.Right, model, locals, arguments, via);
        if (rightStep.Flow != Flow.Next)
            return (rightStep, default);
        return left.IsBool && right.IsBool ? (Step.Next(), Val.FromBool(left.Bool && right.Bool)) : (Step.Next(), default);
    }

    (Step, Val) EvalOr(BinaryExpressionSyntax binary, SemanticModel model, Dictionary<string, Val> locals, ImmutableDictionary<string, Val> arguments, string via)
    {
        var (leftStep, left) = Eval(binary.Left, model, locals, arguments, via);
        if (leftStep.Flow != Flow.Next)
            return (leftStep, default);
        if (left.IsBool && left.Bool)
            return (Step.Next(), Val.FromBool(true));
        var (rightStep, right) = Eval(binary.Right, model, locals, arguments, via);
        if (rightStep.Flow != Flow.Next)
            return (rightStep, default);
        return left.IsBool && right.IsBool ? (Step.Next(), Val.FromBool(left.Bool || right.Bool)) : (Step.Next(), default);
    }

    (Step, Val) EvalEquality(BinaryExpressionSyntax binary, SemanticModel model, Dictionary<string, Val> locals, ImmutableDictionary<string, Val> arguments, string via)
    {
        var (leftStep, left) = Eval(binary.Left, model, locals, arguments, via);
        if (leftStep.Flow != Flow.Next)
            return (leftStep, default);
        var (rightStep, right) = Eval(binary.Right, model, locals, arguments, via);
        if (rightStep.Flow != Flow.Next)
            return (rightStep, default);
        bool? equal = left.IsText && right.IsText ? string.Equals(left.Text, right.Text, StringComparison.Ordinal)
            : left.IsBool && right.IsBool ? left.Bool == right.Bool : null;
        if (equal is null)
            return (Step.Next(), default);
        var value = binary.IsKind(SyntaxKind.NotEqualsExpression) ? !equal.Value : equal.Value;
        return (Step.Next(), Val.FromBool(value));
    }

    (Step, Val) EvalInterpolation(InterpolatedStringExpressionSyntax interpolated, SemanticModel model, Dictionary<string, Val> locals, ImmutableDictionary<string, Val> arguments, string via)
    {
        var builder = new StringBuilder();
        foreach (var content in interpolated.Contents)
        {
            if (content is InterpolatedStringTextSyntax text)
            {
                builder.Append(text.TextToken.ValueText);
                continue;
            }
            if (content is InterpolationSyntax hole)
            {
                var (step, value) = Eval(hole.Expression, model, locals, arguments, via);
                if (step.Flow != Flow.Next)
                    return (step, default);
                if (!value.IsText || value.Text is null)
                    return (Step.Next(), default);
                builder.Append(value.Text);
            }
        }
        return (Step.Next(), Val.FromText(builder.ToString()));
    }

    (Step, Val) EvalInvocation(InvocationExpressionSyntax invocation, SemanticModel model, Dictionary<string, Val> locals, ImmutableDictionary<string, Val> arguments, string via)
    {
        var values = new List<Val>();
        if (invocation.ArgumentList is { } list)
        {
            foreach (var argument in list.Arguments)
            {
                var (step, value) = Eval(argument.Expression, model, locals, arguments, via);
                if (step.Flow != Flow.Next)
                    return (step, default);
                values.Add(value);
            }
        }
        var method = Resolve(invocation, model);
        if (method is not null && IsSkipTest(method))
        {
            if (values.Count != 1 || !values[0].IsText || values[0].Text is not string skipReason)
                return (Step.Unproved("non-constant Skip.Test", Site(invocation)), default);
            return (Step.Skip(skipReason, Site(invocation), via), default);
        }
        if (IsSkipTestSyntax(invocation) && method is null)
        {
            if (values.Count != 1 || !values[0].IsText || values[0].Text is not string unboundReason)
                return (Step.Unproved("unbound Skip.Test", Site(invocation)), default);
            return (Step.Skip(unboundReason, Site(invocation), via), default);
        }
        if (method is not null && OperatingSystemBool(method) is { } os)
            return (Step.Next(), Val.FromBool(os));
        if (method is null && OperatingSystemBoolSyntax(invocation) is { } syntaxOs)
            return (Step.Next(), Val.FromBool(syntaxOs));
        if (method is not null && ShouldInline(method))
        {
            var bindings = Bind(method, values);
            var walked = WalkMethod(method, bindings, via + " > " + method.ContainingType.Name + "." + method.Name);
            if (walked.Flow == Flow.Skip || walked.Flow == Flow.Unproved)
                return (walked, default);
            if (walked.Flow == Flow.Bool)
                return (Step.Next(), Val.FromBool(walked.Bool));
            return (Step.Next(), default);
        }
        if (method is null && GateName(InvocationName(invocation)))
            return (Step.Unproved("unresolved gate " + InvocationName(invocation), Site(invocation)), default);
        return (Step.Next(), default);
    }

    (Step, Val) EvalCreation(ExpressionSyntax creation, TypeSyntax? typeSyntax, ArgumentListSyntax? arguments, SemanticModel model, Dictionary<string, Val> locals, ImmutableDictionary<string, Val> outer, string via)
    {
        if (arguments is not null)
        {
            foreach (var argument in arguments.Arguments)
            {
                var step = Eval(argument.Expression, model, locals, outer, via).Item1;
                if (step.Flow != Flow.Next)
                    return (step, default);
            }
        }
        var symbol = model.GetSymbolInfo(creation).Symbol as IMethodSymbol;
        if (symbol is null && typeSyntax is not null)
        {
            var type = model.GetTypeInfo(typeSyntax).Type as INamedTypeSymbol;
            symbol = type?.Constructors.FirstOrDefault(candidate => candidate.Parameters.Length == (arguments?.Arguments.Count ?? 0));
        }
        if (symbol is not null && ShouldInline(symbol))
        {
            var walked = WalkMethod(symbol, ImmutableDictionary<string, Val>.Empty, via + " > new " + symbol.ContainingType.Name);
            if (walked.Flow is Flow.Skip or Flow.Unproved)
                return (walked, default);
        }
        return (Step.Next(), default);
    }

    ImmutableDictionary<string, Val> Bind(IMethodSymbol method, List<Val> values)
    {
        var builder = ImmutableDictionary.CreateBuilder<string, Val>(StringComparer.Ordinal);
        for (var i = 0; i < method.Parameters.Length; i++)
        {
            var parameter = method.Parameters[i];
            if (i < values.Count && values[i].Known)
                builder[parameter.Name] = values[i];
            else if (parameter.HasExplicitDefaultValue && parameter.ExplicitDefaultValue is string text)
                builder[parameter.Name] = Val.FromText(text);
            else if (parameter.HasExplicitDefaultValue && parameter.ExplicitDefaultValue is bool boolean)
                builder[parameter.Name] = Val.FromBool(boolean);
        }
        return builder.ToImmutable();
    }

    Val Lookup(IdentifierNameSyntax identifier, SemanticModel model, Dictionary<string, Val> locals, ImmutableDictionary<string, Val> arguments)
    {
        if (locals.TryGetValue(identifier.Identifier.Text, out var local))
            return local;
        if (arguments.TryGetValue(identifier.Identifier.Text, out var argument))
            return argument;
        return Constant(identifier, model);
    }

    Val LookupMember(MemberAccessExpressionSyntax member, SemanticModel model)
    {
        var constant = Constant(member, model);
        if (constant.Known)
            return constant;
        if (model.GetSymbolInfo(member).Symbol is IFieldSymbol field && field.HasConstantValue)
            return FromObject(field.ConstantValue);
        return default;
    }

    static Val Constant(ExpressionSyntax expression, SemanticModel model)
    {
        var value = model.GetConstantValue(expression);
        return value.HasValue ? FromObject(value.Value) : default;
    }

    static Val FromObject(object? value) => value switch
    {
        string text => Val.FromText(text),
        bool boolean => Val.FromBool(boolean),
        _ => default,
    };

    IMethodSymbol? Resolve(InvocationExpressionSyntax invocation, SemanticModel model)
    {
        var info = model.GetSymbolInfo(invocation);
        if (info.Symbol is IMethodSymbol method)
            return method;
        var candidates = info.CandidateSymbols.OfType<IMethodSymbol>()
            .Where(candidate => candidate.Parameters.Length == invocation.ArgumentList.Arguments.Count)
            .ToList();
        return candidates.Count == 1 ? candidates[0] : null;
    }

    bool ShouldInline(IMethodSymbol method)
    {
        if (GateName(method.Name))
            return method.DeclaringSyntaxReferences.Length > 0;
        foreach (var reference in method.DeclaringSyntaxReferences)
        {
            if (InterestingSyntax(reference.GetSyntax()))
                return true;
        }
        return false;
    }

    static bool InterestingSyntax(SyntaxNode node) => TextInteresting(node.ToString());

    static bool TextInteresting(string text) =>
        text.Contains("Skip.Test", StringComparison.Ordinal)
        || text.Contains("SkipTestException", StringComparison.Ordinal)
        || text.Contains("IsLinux", StringComparison.Ordinal)
        || text.Contains("IsWindows", StringComparison.Ordinal)
        || text.Contains("IsMacOS", StringComparison.Ordinal)
        || text.Contains("RequireNativeLinux", StringComparison.Ordinal)
        || text.Contains("RequireLinux", StringComparison.Ordinal)
        || text.Contains("RequireUnix", StringComparison.Ordinal)
        || text.Contains("RequirePlatform", StringComparison.Ordinal)
        || text.Contains(".Require(", StringComparison.Ordinal);

    static bool GateName(string name) => name is "Require" or "RequireNativeLinux" or "RequireLinuxJoin" or "RequireLinuxCustody"
        or "RequireUnix" or "RequireLinuxIntegration" or "RequirePlatform" or "Linux";

    static bool IsSkipTest(IMethodSymbol method) =>
        method.Name == "Test"
        && method.ContainingType.Name == "Skip"
        && method.ContainingType.ContainingNamespace.ToDisplayString() == "TUnit.Core";

    static bool IsSkipTestSyntax(InvocationExpressionSyntax invocation) =>
        invocation.Expression is MemberAccessExpressionSyntax member && member.Name.Identifier.Text == "Test" && member.Expression.ToString().EndsWith("Skip", StringComparison.Ordinal);

    static bool? OperatingSystemBool(IMethodSymbol method)
    {
        if (method.ContainingType.Name != "OperatingSystem" || method.ContainingType.ContainingNamespace.ToDisplayString() != "System")
            return null;
        return method.Name switch
        {
            "IsWindows" => true,
            "IsLinux" or "IsMacOS" or "IsMacCatalyst" or "IsFreeBSD" or "IsAndroid" or "IsIOS" => false,
            _ => null,
        };
    }

    static bool? OperatingSystemBoolSyntax(InvocationExpressionSyntax invocation)
    {
        if (invocation.Expression is not MemberAccessExpressionSyntax member || member.Expression.ToString() != "OperatingSystem")
            return null;
        return member.Name.Identifier.Text switch
        {
            "IsWindows" => true,
            "IsLinux" or "IsMacOS" or "IsMacCatalyst" or "IsFreeBSD" => false,
            _ => null,
        };
    }

    static bool IsSkipException(ObjectCreationExpressionSyntax creation, SemanticModel model)
    {
        if (model.GetSymbolInfo(creation).Symbol is IMethodSymbol method)
        {
            var type = method.ContainingType;
            if (type.Name == "SkipTestException" && type.ContainingNamespace.ToDisplayString() == "TUnit.Core.Exceptions")
                return true;
        }
        var name = creation.Type.ToString();
        return name is "SkipTestException" or "TUnit.Core.Exceptions.SkipTestException";
    }

    static string InvocationName(InvocationExpressionSyntax invocation) => invocation.Expression switch
    {
        IdentifierNameSyntax identifier => identifier.Identifier.Text,
        MemberAccessExpressionSyntax member => member.Name.Identifier.Text,
        _ => "",
    };

    SemanticModel Model(SyntaxTree tree) => _models.TryGetValue(tree, out var model) ? model : _models[tree] = _compilation.GetSemanticModel(tree);

    static string Site(SyntaxNode node)
    {
        var span = node.GetLocation().GetLineSpan();
        var path = span.Path.Replace('\\', '/');
        return path + ":" + (span.StartLinePosition.Line + 1);
    }
}
