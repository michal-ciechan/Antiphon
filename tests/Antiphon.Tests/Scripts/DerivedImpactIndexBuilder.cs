using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-1820. Committed reverse-reference index for the impacted-classes selector.
/// The selector reads the file. This builder parses tracked C# with no compilation.
/// </summary>
internal static class DerivedImpactIndexBuilder
{
    internal const int Cap = 25;
    internal const int ByteLimit = 2_000_000;
    internal const int OwedFirstNames = 8;

    // A direct type use outranks a path or dotted script literal, which outranks a one-hop project reference.
    // Object creation is the strongest type use. Hand-listed classes yield cap slots to the others.
    private const int RankHop = 1;
    private const int RankPath = 2;
    private const int RankType = 3;
    private const int RankSpecific = 4;

    internal static readonly string[] Allowlist =
    [
        "Antiphon.sln",
        "Directory.Build.targets",
        "Directory.Build.props",
        "global.json",
    ];

    // Tests inject CRLF here. Serialization still writes LF; the value is only observed.
    internal static Func<string> NewLineSource { get; set; } = static () => Environment.NewLine;

    private static readonly CSharpParseOptions ParseOptions = new(LanguageVersion.Preview, DocumentationMode.None);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        NewLine = "\n",
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static readonly Regex DottedToken = new(
        @"[A-Za-z_][A-Za-z0-9_]*(?:\.[A-Za-z_][A-Za-z0-9_]*)+",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    // A test class is windowsOnly when its file, a base, or a tests/ helper it reaches
    // within this many hops contains an OS-gate token. Over-classification stays on Windows.
    private const int OsGateReferenceDepth = 2;

    private static readonly string[] OsGateTokens =
    [
        "IsWindows",
        "IsOSPlatform",
        "OSPlatform.Windows",
        "RuntimeInformation",
        "Skip.Test",
        "Skip.When",
        "Skip.Unless",
        "SkipTestException",
        "[Skip",
        "[Windows",
        "[Linux",
        "Environment.OSVersion",
        "PlatformID",
    ];

    private static readonly Regex OsGateHelperName = new(
        @"\b(?!\w*Tests\b)(?:\w*(?:Windows|Linux)\w*(?:Gate|Skip|Guard)\w*|\w*(?:Gate|Skip|Guard)\w*(?:Windows|Linux)\w*)\b",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    internal static DerivedImpactIndex Build(string repoRoot)
    {
        var tracked = TrackedPaths(repoRoot);
        var sources = new List<string>();
        foreach (var path in tracked)
        {
            if (IsWalkedSource(path))
                sources.Add(path);
        }

        var scans = new ConcurrentBag<FileScan>();
        Parallel.ForEach(
            sources,
            new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount },
            path => scans.Add(Scan(repoRoot, path)));

        var bySimple = new Dictionary<string, List<Decl>>(StringComparer.Ordinal);
        var filesByFull = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var productionFiles = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var scan in scans)
        {
            foreach (var decl in scan.Decls)
            {
                if (!bySimple.TryGetValue(decl.Simple, out var same))
                {
                    same = [];
                    bySimple[decl.Simple] = same;
                }

                same.Add(decl);
                if (!filesByFull.TryGetValue(decl.Full, out var files))
                {
                    files = new HashSet<string>(StringComparer.Ordinal);
                    filesByFull[decl.Full] = files;
                }

                files.Add(decl.File);
                if (!decl.Production || decl.Full.IndexOf('.') < 0)
                    continue;
                if (!productionFiles.TryGetValue(decl.Full, out var produced))
                {
                    produced = [];
                    productionFiles[decl.Full] = produced;
                }

                if (!produced.Contains(decl.File))
                    produced.Add(decl.File);
            }
        }

        var uniqueBySimple = UniqueSimpleNames(bySimple);
        var productionNames = new HashSet<string>(productionFiles.Keys, StringComparer.Ordinal);
        var hits = new Dictionary<string, Dictionary<string, Hit>>(StringComparer.Ordinal);
        var hops = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var scan in scans)
        {
            foreach (var use in scan.Idents)
            {
                if (!uniqueBySimple.TryGetValue(use.Text, out var decls))
                    continue;
                filesByFull.TryGetValue(use.TestFull, out var ownFiles);
                var rank = use.Created ? RankSpecific : RankType;
                foreach (var decl in decls)
                {
                    RememberHop(hops, use.TestSimple, decl.File);
                    if (ownFiles is not null && ownFiles.Contains(decl.File))
                        continue;
                    AddHit(hits, decl.File, use.TestSimple, rank);
                }
            }

            foreach (var use in scan.DirectStrings)
                AddStringHits(hits, use.Text, use.TestSimple, tracked, productionFiles, productionNames);
        }

        var stringHitsByFile = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var scan in scans)
        {
            var found = new HashSet<string>(StringComparer.Ordinal);
            foreach (var text in scan.Strings)
                CollectStringTargets(found, text, tracked, productionFiles, productionNames);
            stringHitsByFile[scan.Path] = [.. found];
        }

        foreach (var hop in hops)
        {
            foreach (var file in hop.Value)
            {
                if (!stringHitsByFile.TryGetValue(file, out var targets))
                    continue;
                foreach (var target in targets)
                    AddHit(hits, target, hop.Key, RankHop);
            }
        }

        var windowsOnly = ClassifyWindowsOnly(scans);
        Console.WriteLine("derived-impact windowsOnly count=" + windowsOnly.Count.ToString());

        var allow = new HashSet<string>(Allowlist, StringComparer.Ordinal);
        var handByPath = HandClassesByPath(repoRoot, hits.Keys);
        var capped = new List<CappedPath>();
        var edges = new List<ImpactEdge>();
        foreach (var path in hits.Keys.Order(StringComparer.Ordinal))
        {
            handByPath.TryGetValue(path, out var hand);
            var ranked = hits[path]
                .Select(pair => (Name: pair.Key, pair.Value.Rank, pair.Value.Count))
                .OrderBy(item => hand is not null && hand.Contains(item.Name) ? 1 : 0)
                .ThenByDescending(item => item.Rank)
                .ThenByDescending(item => item.Count)
                .ThenBy(item => item.Name, StringComparer.Ordinal)
                .ToArray();
            if (allow.Contains(path))
            {
                capped.Add(new CappedPath(path, ranked.Length, "allowlist", 0, null));
                continue;
            }

            if (ranked.Length > Cap)
            {
                var kept = ranked.Take(Cap).Select(item => item.Name).Order(StringComparer.Ordinal).ToArray();
                var remainder = ranked.Skip(Cap).Select(item => item.Name).ToArray();
                var first = remainder.Take(OwedFirstNames).ToArray();
                edges.Add(new ImpactEdge(path, kept));
                capped.Add(new CappedPath(path, ranked.Length, "cap", remainder.Length, first));
                continue;
            }

            edges.Add(new ImpactEdge(path, ranked.Select(item => item.Name).Order(StringComparer.Ordinal).ToArray()));
        }

        var document = new IndexDocument
        {
            schema = 1,
            cap = Cap,
            byteLimit = ByteLimit,
            allowlist = Allowlist,
            windowsOnly = windowsOnly.ToArray(),
            cappedPaths = capped.Select(row => new CappedDocument
            {
                path = row.Path,
                classes = row.Classes,
                reason = row.Reason,
                owed = row.Reason == "cap" ? row.Owed : null,
                first = row.Reason == "cap" ? row.First : null,
            }).ToArray(),
            edges = edges.Select(row => new EdgeDocument
            {
                path = row.Path,
                classes = row.Classes.ToArray(),
            }).ToArray(),
        };
        if (NewLineSource() is null)
            throw new InvalidOperationException("newline source returned null.");
        var utf8 = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(document, JsonOptions) + "\n");
        if (utf8.Length > ByteLimit)
            throw new InvalidOperationException(
                "derived-impact index is " + utf8.Length.ToString() + " bytes, over the " + ByteLimit.ToString() + " limit.");

        return new DerivedImpactIndex(1, Cap, Allowlist, windowsOnly.ToArray(), capped, edges, utf8);
    }

    internal static void Write(string repoRoot, DerivedImpactIndex index)
    {
        var path = Path.Combine(repoRoot, "tests", "derived-impact-index.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, index.Utf8);
    }

    private static void AddStringHits(
        Dictionary<string, Dictionary<string, Hit>> hits,
        string raw,
        string testSimple,
        HashSet<string> tracked,
        Dictionary<string, List<string>> productionFiles,
        HashSet<string> productionNames)
    {
        var found = new HashSet<string>(StringComparer.Ordinal);
        CollectStringTargets(found, raw, tracked, productionFiles, productionNames);
        foreach (var path in found)
            AddHit(hits, path, testSimple, RankPath);
    }

    private static void CollectStringTargets(
        HashSet<string> found,
        string raw,
        HashSet<string> tracked,
        Dictionary<string, List<string>> productionFiles,
        HashSet<string> productionNames)
    {
        var text = raw.Replace('\\', '/');
        if (text.Length >= 8 && tracked.Contains(text))
            found.Add(text);
        if (raw.IndexOf('.') < 0)
            return;
        foreach (var name in DottedNames(raw, productionNames))
        {
            if (!productionFiles.TryGetValue(name, out var files))
                continue;
            foreach (var file in files)
                found.Add(file);
        }
    }

    private static IEnumerable<string> DottedNames(string raw, HashSet<string> productionNames)
    {
        foreach (Match match in DottedToken.Matches(raw))
        {
            var parts = match.Value.Split('.');
            if (parts.Length < 2)
                continue;
            for (var start = 0; start < parts.Length - 1; start++)
            {
                for (var end = start + 1; end < parts.Length; end++)
                {
                    var candidate = string.Join('.', parts, start, end - start + 1);
                    if (productionNames.Contains(candidate))
                        yield return candidate;
                }
            }
        }
    }

    private static void RememberHop(Dictionary<string, HashSet<string>> hops, string testSimple, string file)
    {
        if (!hops.TryGetValue(testSimple, out var files))
        {
            files = new HashSet<string>(StringComparer.Ordinal);
            hops[testSimple] = files;
        }

        files.Add(file);
    }

    private static void AddHit(Dictionary<string, Dictionary<string, Hit>> hits, string path, string testSimple, int rank)
    {
        if (!hits.TryGetValue(path, out var names))
        {
            names = new Dictionary<string, Hit>(StringComparer.Ordinal);
            hits[path] = names;
        }

        if (!names.TryGetValue(testSimple, out var current))
            names[testSimple] = new Hit(rank, 1);
        else
            names[testSimple] = new Hit(Math.Max(current.Rank, rank), current.Count + 1);
    }

    // A hand row already selects its classes. Cap slots stay for classes the table does not name.
    private static Dictionary<string, HashSet<string>> HandClassesByPath(string repoRoot, IEnumerable<string> paths)
    {
        var covered = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var table = Path.Combine(repoRoot, "tests", "impacted-classes.json");
        if (!File.Exists(table))
            return covered;
        using var document = JsonDocument.Parse(File.ReadAllText(table));
        if (!document.RootElement.TryGetProperty("kinds", out var kinds) || kinds.ValueKind != JsonValueKind.Array)
            return covered;
        var matchers = new List<(Regex Pattern, string[] Classes)>();
        foreach (var kind in kinds.EnumerateArray())
        {
            var names = new List<string>();
            if (kind.TryGetProperty("classes", out var classes) && classes.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in classes.EnumerateArray())
                {
                    if (item.GetString() is { Length: > 0 } name)
                        names.Add(name);
                }
            }

            if (kind.TryGetProperty("methods", out var methods) && methods.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in methods.EnumerateArray())
                {
                    var text = item.GetString();
                    if (string.IsNullOrEmpty(text))
                        continue;
                    var slash = text.IndexOf('/');
                    names.Add(slash > 0 ? text[..slash] : text);
                }
            }

            if (!kind.TryGetProperty("globs", out var globs) || globs.ValueKind != JsonValueKind.Array)
                continue;
            var distinct = names.Distinct(StringComparer.Ordinal).ToArray();
            foreach (var glob in globs.EnumerateArray())
            {
                var pattern = glob.GetString();
                if (string.IsNullOrEmpty(pattern))
                    continue;
                matchers.Add((new Regex(GlobToRegex(pattern), RegexOptions.CultureInvariant | RegexOptions.Compiled), distinct));
            }
        }

        foreach (var path in paths)
        {
            HashSet<string>? hand = null;
            foreach (var (pattern, classes) in matchers)
            {
                if (!pattern.IsMatch(path))
                    continue;
                hand ??= new HashSet<string>(StringComparer.Ordinal);
                foreach (var name in classes)
                    hand.Add(name);
            }

            if (hand is not null)
                covered[path] = hand;
        }

        return covered;
    }

    private static string GlobToRegex(string glob)
    {
        var sb = new StringBuilder("^");
        for (var i = 0; i < glob.Length;)
        {
            if (i + 2 < glob.Length && glob.Substring(i, 3) == "**/")
            {
                sb.Append("(?:.*/)?");
                i += 3;
                continue;
            }

            if (i + 1 < glob.Length && glob.Substring(i, 2) == "**")
            {
                sb.Append(".*");
                i += 2;
                continue;
            }

            var c = glob[i];
            if (c == '*')
                sb.Append("[^/]*");
            else if (@"\.+()[]{}|^$?".Contains(c))
                sb.Append('\\').Append(c);
            else
                sb.Append(c);
            i++;
        }

        sb.Append('$');
        return sb.ToString();
    }

    private static bool IsWalkedSource(string path) =>
        path.EndsWith(".cs", StringComparison.Ordinal)
        && (path.StartsWith("src/", StringComparison.Ordinal)
            || path.StartsWith("server/", StringComparison.Ordinal)
            || path.StartsWith("tests/", StringComparison.Ordinal));

    private static HashSet<string> TrackedPaths(string repoRoot)
    {
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = repoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.ArgumentList.Add("-c");
        start.ArgumentList.Add("core.quotepath=false");
        start.ArgumentList.Add("ls-files");
        start.ArgumentList.Add("-c");
        using var process = Process.Start(start) ?? throw new InvalidOperationException("git did not start.");
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new InvalidOperationException("git ls-files failed: " + stderr.Trim());

        var tracked = new HashSet<string>(StringComparer.Ordinal);
        foreach (var line in stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            tracked.Add(line.Replace('\\', '/'));
        return tracked;
    }

    private static FileScan Scan(string repoRoot, string relative)
    {
        var full = Path.Combine(repoRoot, relative.Replace('/', Path.DirectorySeparatorChar));
        var text = File.ReadAllText(full);
        var root = CSharpSyntaxTree.ParseText(text, ParseOptions, relative).GetRoot();
        var scan = new FileScan(relative);
        var testsFile = relative.StartsWith("tests/", StringComparison.Ordinal);
        if (testsFile)
            scan.SourceHasOsGate = SourceHasOsGate(text);
        var fullNames = new Dictionary<TypeDeclarationSyntax, string>();
        foreach (var type in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
        {
            if (!IsClassLike(type))
                continue;
            var name = FullName(type);
            fullNames[type] = name;
            var production = relative.StartsWith("src/", StringComparison.Ordinal)
                || relative.StartsWith("server/", StringComparison.Ordinal);
            var simple = type.Identifier.ValueText;
            scan.Decls.Add(new Decl(simple, name, relative, production));
            if (testsFile)
                scan.Gates.Add(CollectTypeGate(type, simple, name, relative));
        }

        foreach (var literal in root.DescendantNodes().OfType<LiteralExpressionSyntax>())
        {
            if (!literal.IsKind(SyntaxKind.StringLiteralExpression))
                continue;
            var value = literal.Token.ValueText;
            if (string.IsNullOrEmpty(value))
                continue;
            if (value.Length < 8 && value.IndexOf('.') < 0)
                continue;
            scan.Strings.Add(value);
            if (!TryOwner(literal, fullNames, out var ownerFull, out var ownerSimple))
                continue;
            scan.DirectStrings.Add(new OwnedText(ownerFull, ownerSimple, value, false));
        }

        foreach (var ident in root.DescendantNodes().OfType<IdentifierNameSyntax>())
        {
            if (!CountsAsTypeUse(ident))
                continue;
            var value = ident.Identifier.ValueText;
            if (value.Length < 3)
                continue;
            if (!TryOwner(ident, fullNames, out var ownerFull, out var ownerSimple))
                continue;
            if (value == ownerSimple)
                continue;
            scan.Idents.Add(new OwnedText(ownerFull, ownerSimple, value, IsObjectCreation(ident)));
        }

        return scan;
    }

    private static bool TryOwner(
        SyntaxNode node,
        Dictionary<TypeDeclarationSyntax, string> fullNames,
        out string full,
        out string simple)
    {
        for (var current = node.Parent; current is not null; current = current.Parent)
        {
            if (current is not TypeDeclarationSyntax type || !IsClassLike(type))
                continue;
            simple = type.Identifier.ValueText;
            if (!simple.EndsWith("Tests", StringComparison.Ordinal))
                continue;
            if (!fullNames.TryGetValue(type, out full!))
                full = FullName(type);
            return true;
        }

        full = "";
        simple = "";
        return false;
    }

    // A simple name shared by two classes is not a type use: [Category] would otherwise
    // select every nested logger Category. Attribute names are not type edges.
    private static Dictionary<string, List<Decl>> UniqueSimpleNames(Dictionary<string, List<Decl>> bySimple)
    {
        var unique = new Dictionary<string, List<Decl>>(StringComparer.Ordinal);
        foreach (var pair in bySimple)
        {
            string? full = null;
            var same = true;
            foreach (var decl in pair.Value)
            {
                if (full is null)
                    full = decl.Full;
                else if (!string.Equals(full, decl.Full, StringComparison.Ordinal))
                {
                    same = false;
                    break;
                }
            }

            if (same && full is not null)
                unique[pair.Key] = pair.Value;
        }

        return unique;
    }

    private static bool CountsAsTypeUse(IdentifierNameSyntax ident)
    {
        if (IsAttributeName(ident))
            return false;
        if (ident.Parent is MemberAccessExpressionSyntax member && member.Expression == ident)
            return true;
        if (ident.Parent is ConditionalAccessExpressionSyntax access && access.Expression == ident)
            return true;
        return IsInTypePosition(ident);
    }

    private static bool IsAttributeName(SyntaxNode node)
    {
        for (var current = node; current is not null; current = current.Parent)
        {
            if (current.Parent is AttributeSyntax attribute && attribute.Name == current)
                return true;
            if (current.Parent is QualifiedNameSyntax or AliasQualifiedNameSyntax)
                continue;
            return false;
        }

        return false;
    }

    private static bool IsInTypePosition(SyntaxNode node)
    {
        var current = node;
        while (current.Parent is not null)
        {
            var parent = current.Parent;
            switch (parent)
            {
                case QualifiedNameSyntax qualified when qualified.Right == current:
                    current = parent;
                    continue;
                case AliasQualifiedNameSyntax alias when alias.Name == current:
                    current = parent;
                    continue;
                case TypeArgumentListSyntax:
                    return true;
                case NullableTypeSyntax nullable when nullable.ElementType == current:
                case ArrayTypeSyntax array when array.ElementType == current:
                case PointerTypeSyntax pointer when pointer.ElementType == current:
                case RefTypeSyntax refType when refType.Type == current:
                    current = parent;
                    continue;
                case TupleElementSyntax tuple when tuple.Type == current:
                case SimpleBaseTypeSyntax simple when simple.Type == current:
                case ObjectCreationExpressionSyntax created when created.Type == current:
                case ArrayCreationExpressionSyntax arrayCreated when arrayCreated.Type == current:
                case CastExpressionSyntax cast when cast.Type == current:
                case TypeOfExpressionSyntax typeOf when typeOf.Type == current:
                case DefaultExpressionSyntax fallback when fallback.Type == current:
                case VariableDeclarationSyntax variable when variable.Type == current:
                case ParameterSyntax parameter when parameter.Type == current:
                case PropertyDeclarationSyntax property when property.Type == current:
                case IndexerDeclarationSyntax indexer when indexer.Type == current:
                case EventDeclarationSyntax ev when ev.Type == current:
                case DelegateDeclarationSyntax del when del.ReturnType == current:
                case MethodDeclarationSyntax method when method.ReturnType == current:
                case OperatorDeclarationSyntax op when op.ReturnType == current:
                case ConversionOperatorDeclarationSyntax conversion when conversion.Type == current:
                case LocalFunctionStatementSyntax local when local.ReturnType == current:
                case ForEachStatementSyntax each when each.Type == current:
                case CatchDeclarationSyntax caught when caught.Type == current:
                case TypeConstraintSyntax constraint when constraint.Type == current:
                case DeclarationPatternSyntax declared when declared.Type == current:
                case TypePatternSyntax typePattern when typePattern.Type == current:
                case RecursivePatternSyntax recursive when recursive.Type == current:
                    return true;
                case BinaryExpressionSyntax binary when binary.IsKind(SyntaxKind.AsExpression) && binary.Right == current:
                    return true;
                default:
                    return false;
            }
        }

        return false;
    }

    private static bool IsObjectCreation(SyntaxNode node)
    {
        for (var current = node; current is not null; current = current.Parent)
        {
            if (current.Parent is QualifiedNameSyntax or AliasQualifiedNameSyntax)
                continue;
            return current.Parent is ObjectCreationExpressionSyntax created && created.Type == current;
        }

        return false;
    }

    private static bool SourceHasOsGate(string text)
    {
        foreach (var token in OsGateTokens)
        {
            if (text.Contains(token, StringComparison.Ordinal))
                return true;
        }

        if (text.Contains("DirectorySeparatorChar == '\\\\'", StringComparison.Ordinal))
            return true;
        return OsGateHelperName.IsMatch(text);
    }

    private static TypeGate CollectTypeGate(TypeDeclarationSyntax type, string simple, string full, string file)
    {
        var bases = new List<string>();
        if (type.BaseList is not null)
        {
            foreach (var baseType in type.BaseList.Types)
            {
                var name = SimpleTypeName(baseType.Type);
                if (name is null || name == simple)
                    continue;
                bases.Add(name);
            }
        }

        var helpers = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in type.DescendantNodes())
        {
            if (IsInsideNestedType(node, type))
                continue;
            if (node is AttributeSyntax attribute)
            {
                var name = SimpleTypeName(attribute.Name);
                if (name is null || name == simple || name.EndsWith("Tests", StringComparison.Ordinal))
                    continue;
                helpers.Add(name);
                continue;
            }

            if (node is not IdentifierNameSyntax ident
                || ident.Parent is not MemberAccessExpressionSyntax member
                || member.Expression != ident)
                continue;
            var target = ident.Identifier.ValueText;
            if (target.Length < 3 || target == simple || target.EndsWith("Tests", StringComparison.Ordinal))
                continue;
            helpers.Add(target);
        }

        return new TypeGate(
            simple,
            full,
            file,
            simple.EndsWith("Tests", StringComparison.Ordinal),
            bases,
            helpers);
    }

    private static bool IsInsideNestedType(SyntaxNode node, TypeDeclarationSyntax owner)
    {
        for (var current = node.Parent; current is not null && current != owner; current = current.Parent)
        {
            if (current is TypeDeclarationSyntax)
                return true;
        }

        return false;
    }

    private static string? SimpleTypeName(TypeSyntax type)
    {
        switch (type)
        {
            case IdentifierNameSyntax ident:
                return ident.Identifier.ValueText;
            case GenericNameSyntax generic:
                return generic.Identifier.ValueText;
            case QualifiedNameSyntax qualified:
                return qualified.Right.Identifier.ValueText;
            case AliasQualifiedNameSyntax alias:
                return alias.Name.Identifier.ValueText;
            case NullableTypeSyntax nullable:
                return SimpleTypeName(nullable.ElementType);
            default:
                return null;
        }
    }

    private static SortedSet<string> ClassifyWindowsOnly(IEnumerable<FileScan> scans)
    {
        var gates = new List<TypeGate>();
        var gatedFull = new HashSet<string>(StringComparer.Ordinal);
        var bySimple = new Dictionary<string, List<TypeGate>>(StringComparer.Ordinal);
        foreach (var scan in scans)
        {
            foreach (var gate in scan.Gates)
            {
                gates.Add(gate);
                if (!bySimple.TryGetValue(gate.Simple, out var same))
                {
                    same = [];
                    bySimple[gate.Simple] = same;
                }

                same.Add(gate);
                if (scan.SourceHasOsGate)
                    gatedFull.Add(gate.Full);
            }
        }

        for (var depth = 0; depth < OsGateReferenceDepth; depth++)
        {
            var next = new List<string>();
            foreach (var gate in gates)
            {
                if (gatedFull.Contains(gate.Full))
                    continue;
                if (ReferencesOsGate(gate, bySimple, gatedFull))
                    next.Add(gate.Full);
            }

            if (next.Count == 0)
                break;
            foreach (var full in next)
                gatedFull.Add(full);
        }

        var windowsOnly = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var gate in gates)
        {
            if (gate.TestClass && gatedFull.Contains(gate.Full))
                windowsOnly.Add(gate.Simple);
        }

        return windowsOnly;
    }

    private static bool ReferencesOsGate(
        TypeGate gate,
        Dictionary<string, List<TypeGate>> bySimple,
        HashSet<string> gatedFull)
    {
        foreach (var name in gate.Bases)
        {
            if (AnyDeclGated(name, bySimple, gatedFull))
                return true;
        }

        foreach (var name in gate.Helpers)
        {
            if (UniqueOrSameFileGated(gate, name, bySimple, gatedFull))
                return true;
        }

        return false;
    }

    private static bool AnyDeclGated(
        string name,
        Dictionary<string, List<TypeGate>> bySimple,
        HashSet<string> gatedFull)
    {
        if (!bySimple.TryGetValue(name, out var decls))
            return false;
        foreach (var decl in decls)
        {
            if (gatedFull.Contains(decl.Full))
                return true;
        }

        return false;
    }

    private static bool UniqueOrSameFileGated(
        TypeGate gate,
        string name,
        Dictionary<string, List<TypeGate>> bySimple,
        HashSet<string> gatedFull)
    {
        if (!bySimple.TryGetValue(name, out var decls))
            return false;
        string? full = null;
        var unique = true;
        foreach (var decl in decls)
        {
            if (full is null)
                full = decl.Full;
            else if (!string.Equals(full, decl.Full, StringComparison.Ordinal))
            {
                unique = false;
                break;
            }
        }

        if (unique && full is not null)
            return gatedFull.Contains(full);

        foreach (var decl in decls)
        {
            if (decl.File == gate.File && gatedFull.Contains(decl.Full))
                return true;
        }

        return false;
    }

    private static bool IsClassLike(TypeDeclarationSyntax type)
    {
        if (type is ClassDeclarationSyntax)
            return true;
        if (type is not RecordDeclarationSyntax record)
            return false;
        return !record.ClassOrStructKeyword.IsKind(SyntaxKind.StructKeyword);
    }

    private static string FullName(TypeDeclarationSyntax type)
    {
        var segments = new List<string> { type.Identifier.ValueText };
        for (var parent = type.Parent; parent is not null; parent = parent.Parent)
        {
            if (parent is TypeDeclarationSyntax enclosing)
                segments.Add(enclosing.Identifier.ValueText);
            else if (parent is BaseNamespaceDeclarationSyntax ns)
                segments.Add(ns.Name.ToString());
        }

        segments.Reverse();
        return string.Join(".", segments);
    }

    private sealed class FileScan(string path)
    {
        public string Path { get; } = path;
        public List<Decl> Decls { get; } = [];
        public List<string> Strings { get; } = [];
        public List<OwnedText> DirectStrings { get; } = [];
        public List<OwnedText> Idents { get; } = [];
        public bool SourceHasOsGate { get; set; }
        public List<TypeGate> Gates { get; } = [];
    }

    private readonly record struct Decl(string Simple, string Full, string File, bool Production);

    private readonly record struct TypeGate(
        string Simple,
        string Full,
        string File,
        bool TestClass,
        List<string> Bases,
        HashSet<string> Helpers);

    private readonly record struct OwnedText(string TestFull, string TestSimple, string Text, bool Created);

    private readonly record struct Hit(int Rank, int Count);

    private sealed class IndexDocument
    {
        public int schema { get; set; }
        public int cap { get; set; }
        public int byteLimit { get; set; }
        public string[] allowlist { get; set; } = [];
        public string[] windowsOnly { get; set; } = [];
        public CappedDocument[] cappedPaths { get; set; } = [];
        public EdgeDocument[] edges { get; set; } = [];
    }

    private sealed class CappedDocument
    {
        public string path { get; set; } = "";
        public int classes { get; set; }
        public string reason { get; set; } = "";
        public int? owed { get; set; }
        public string[]? first { get; set; }
    }

    private sealed class EdgeDocument
    {
        public string path { get; set; } = "";
        public string[] classes { get; set; } = [];
    }
}

internal readonly record struct CappedPath(string Path, int Classes, string Reason, int Owed, string[]? First);

internal readonly record struct ImpactEdge(string Path, IReadOnlyList<string> Classes);

internal sealed record DerivedImpactIndex(
    int Schema,
    int Cap,
    IReadOnlyList<string> Allowlist,
    IReadOnlyList<string> WindowsOnly,
    IReadOnlyList<CappedPath> CappedPaths,
    IReadOnlyList<ImpactEdge> Edges,
    byte[] Utf8);
