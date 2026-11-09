using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
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

    internal static readonly string[] Allowlist =
    [
        "Antiphon.sln",
        "Directory.Build.targets",
        "Directory.Build.props",
        "global.json",
    ];

    private static readonly CSharpParseOptions ParseOptions = new(LanguageVersion.Preview, DocumentationMode.None);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static readonly Regex DottedToken = new(
        @"[A-Za-z_][A-Za-z0-9_]*(?:\.[A-Za-z_][A-Za-z0-9_]*)+",
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
        var hits = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var hops = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var scan in scans)
        {
            foreach (var use in scan.Idents)
            {
                if (!uniqueBySimple.TryGetValue(use.Text, out var decls))
                    continue;
                filesByFull.TryGetValue(use.TestFull, out var ownFiles);
                foreach (var decl in decls)
                {
                    RememberHop(hops, use.TestSimple, decl.File);
                    if (ownFiles is not null && ownFiles.Contains(decl.File))
                        continue;
                    AddHit(hits, decl.File, use.TestSimple);
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
                    AddHit(hits, target, hop.Key);
            }
        }

        var allow = new HashSet<string>(Allowlist, StringComparer.Ordinal);
        var capped = new List<CappedPath>();
        var edges = new List<ImpactEdge>();
        foreach (var path in hits.Keys.Order(StringComparer.Ordinal))
        {
            var names = hits[path].Order(StringComparer.Ordinal).ToArray();
            if (allow.Contains(path) || names.Length > Cap)
            {
                var reason = allow.Contains(path) ? "allowlist" : "cap";
                capped.Add(new CappedPath(path, names.Length, reason));
                continue;
            }

            edges.Add(new ImpactEdge(path, names));
        }

        var document = new IndexDocument
        {
            schema = 1,
            cap = Cap,
            byteLimit = ByteLimit,
            allowlist = Allowlist,
            cappedPaths = capped.Select(row => new CappedDocument
            {
                path = row.Path,
                classes = row.Classes,
                reason = row.Reason,
            }).ToArray(),
            edges = edges.Select(row => new EdgeDocument
            {
                path = row.Path,
                classes = row.Classes.ToArray(),
            }).ToArray(),
        };
        var utf8 = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(document, JsonOptions) + "\n");
        if (utf8.Length > ByteLimit)
            throw new InvalidOperationException(
                "derived-impact index is " + utf8.Length.ToString() + " bytes, over the " + ByteLimit.ToString() + " limit.");

        return new DerivedImpactIndex(1, Cap, Allowlist, capped, edges, utf8);
    }

    internal static void Write(string repoRoot, DerivedImpactIndex index)
    {
        var path = Path.Combine(repoRoot, "tests", "derived-impact-index.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, index.Utf8);
    }

    private static void AddStringHits(
        Dictionary<string, HashSet<string>> hits,
        string raw,
        string testSimple,
        HashSet<string> tracked,
        Dictionary<string, List<string>> productionFiles,
        HashSet<string> productionNames)
    {
        var found = new HashSet<string>(StringComparer.Ordinal);
        CollectStringTargets(found, raw, tracked, productionFiles, productionNames);
        foreach (var path in found)
            AddHit(hits, path, testSimple);
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

    private static void AddHit(Dictionary<string, HashSet<string>> hits, string path, string testSimple)
    {
        if (!hits.TryGetValue(path, out var names))
        {
            names = new HashSet<string>(StringComparer.Ordinal);
            hits[path] = names;
        }

        names.Add(testSimple);
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
        var fullNames = new Dictionary<TypeDeclarationSyntax, string>();
        foreach (var type in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
        {
            if (!IsClassLike(type))
                continue;
            var name = FullName(type);
            fullNames[type] = name;
            var production = relative.StartsWith("src/", StringComparison.Ordinal)
                || relative.StartsWith("server/", StringComparison.Ordinal);
            scan.Decls.Add(new Decl(type.Identifier.ValueText, name, relative, production));
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
            scan.DirectStrings.Add(new OwnedText(ownerFull, ownerSimple, value));
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
            scan.Idents.Add(new OwnedText(ownerFull, ownerSimple, value));
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
    }

    private readonly record struct Decl(string Simple, string Full, string File, bool Production);

    private readonly record struct OwnedText(string TestFull, string TestSimple, string Text);

    private sealed class IndexDocument
    {
        public int schema { get; set; }
        public int cap { get; set; }
        public int byteLimit { get; set; }
        public string[] allowlist { get; set; } = [];
        public CappedDocument[] cappedPaths { get; set; } = [];
        public EdgeDocument[] edges { get; set; } = [];
    }

    private sealed class CappedDocument
    {
        public string path { get; set; } = "";
        public int classes { get; set; }
        public string reason { get; set; } = "";
    }

    private sealed class EdgeDocument
    {
        public string path { get; set; } = "";
        public string[] classes { get; set; } = [];
    }
}

internal readonly record struct CappedPath(string Path, int Classes, string Reason);

internal readonly record struct ImpactEdge(string Path, IReadOnlyList<string> Classes);

internal sealed record DerivedImpactIndex(
    int Schema,
    int Cap,
    IReadOnlyList<string> Allowlist,
    IReadOnlyList<CappedPath> CappedPaths,
    IReadOnlyList<ImpactEdge> Edges,
    byte[] Utf8);
