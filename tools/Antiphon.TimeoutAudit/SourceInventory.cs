using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Antiphon.TimeoutAudit;

internal static class SourceInventory
{
    // Deliberately retain unresolved expressions. A static inventory is not proof of a runtime deadline.
    public static void Read(string repository, AuditReport report)
    {
        foreach (var folder in new[] { "tests", "scripts", "tools", "client", "docs/superpowers/plans" })
        foreach (var file in Files(Path.Combine(repository, folder)))
        {
            var path = Path.GetRelativePath(repository, file).Replace('\\', '/');
            var text = File.ReadAllText(file);
            if (path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)) ReadCSharp(path, text, report);
            else if (path.EndsWith(".md", StringComparison.OrdinalIgnoreCase)) ReadPlan(path, text, report);
            else if (path.EndsWith("test-execution-policy.json", StringComparison.Ordinal)) ReadPolicy(path, text, report);
            else if (path.EndsWith("nightly-class-costs.json", StringComparison.Ordinal)) ReadCosts(path, text, report);
            else if (new[] { ".ps1", ".mjs", ".js", ".ts", ".tsx", ".json", ".yaml", ".yml", ".props", ".targets", ".runsettings", ".csproj" }.Contains(Path.GetExtension(path)))
            {
                var lines = text.Split('\n');
                for (var i = 0; i < lines.Length; i++)
                {
                    var line = lines[i].Trim();
                    if (line.StartsWith('#') || line.StartsWith("//", StringComparison.Ordinal)) continue;
                    if (!Regex.IsMatch(line, @"(?i)timeout|deadline|WaitForCondition|WaitUntil|Task\.Delay|CancelAfter|MaxWait|WaitForExit")) continue;
                    var number = Regex.Match(line, @"(?i)(?<name>[\w-]*(?:timeout|deadline|MaxWait)[\w-]*)\s*[=:]\s*(?<value>[\d_]+)");
                    var seconds = number.Success ? Scale(Number(number.Groups["value"].Value), number.Groups["name"].Value) : null;
                    Add(report, path, i + 1, "tool", line, seconds);
                }
            }
        }
    }

    internal static IEnumerable<string> Files(string root)
    {
        if (!Directory.Exists(root)) yield break;
        foreach (var file in Directory.EnumerateFiles(root))
            if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) == 0) yield return file;
        foreach (var dir in Directory.EnumerateDirectories(root))
        {
            var name = Path.GetFileName(dir);
            if (name is "obj" or "bin" or "node_modules" or ".git" or ".antiphon" or "dist" or "workspace" || name.StartsWith("bin-", StringComparison.Ordinal)) continue;
            if ((File.GetAttributes(dir) & FileAttributes.ReparsePoint) != 0) continue;
            foreach (var child in Files(dir)) yield return child;
        }
    }

    private static void ReadCSharp(string path, string text, AuditReport report)
    {
        var tree = CSharpSyntaxTree.ParseText(text);
        var root = tree.GetRoot();
        var constants = root.DescendantNodes().OfType<VariableDeclaratorSyntax>()
            .Where(x => x.Parent?.Parent is FieldDeclarationSyntax field && field.Modifiers.Any(SyntaxKind.ConstKeyword))
            .GroupBy(x => x.Identifier.ValueText).Where(x => x.Count() == 1)
            .ToDictionary(x => x.Key, x => x.Single().Initializer?.Value);
        foreach (var method in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
        {
            var attributes = method.AttributeLists.SelectMany(x => x.Attributes).ToArray();
            var timeout = attributes.FirstOrDefault(IsTimeout);
            if (timeout == null && !attributes.Any(x => x.Name.ToString().Split('.').Last() is "Test" or "TestAttribute")) continue;
            var type = method.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault();
            timeout ??= type?.AttributeLists.SelectMany(x => x.Attributes).FirstOrDefault(IsTimeout);
            if (timeout == null) continue;
            var ns = method.Ancestors().OfType<BaseNamespaceDeclarationSyntax>().Reverse().Select(x => x.Name.ToString());
            var types = method.Ancestors().OfType<TypeDeclarationSyntax>().Reverse().Select(x => x.Identifier.ValueText);
            var name = string.Join('.', ns.Concat(types).Append(method.Identifier.ValueText));
            var value = timeout.ArgumentList?.Arguments.FirstOrDefault()?.Expression;
            var seconds = Eval(value, constants) / 1000;
            Add(report, path, Line(timeout), "test", timeout.ToString(), seconds, name);
        }
        // Defaults still appear when a partial/inherited/generated test is not statically resolvable.
        foreach (var attr in root.DescendantNodes().OfType<AttributeSyntax>().Where(IsTimeout))
            Add(report, path, Line(attr), "test-default", attr.ToString(), Eval(attr.ArgumentList?.Arguments.FirstOrDefault()?.Expression, constants) / 1000);
        foreach (var node in root.DescendantNodes())
        {
            if (node is InvocationExpressionSyntax call && Regex.IsMatch(call.Expression.ToString(), @"WaitForCondition|WaitUntil|WaitForExit|WaitAsync|CancelAfter|(?:Task\.)?Delay|Thread\.Sleep"))
            {
                var values = call.ArgumentList.Arguments.Select(x => x.Expression).ToArray();
                var duration = values.Select(x => Duration(x, constants)).FirstOrDefault(x => x != null);
                // Delay/WaitForExit integer overloads use milliseconds; other positional numbers stay unresolved.
                if (duration == null && Regex.IsMatch(call.Expression.ToString(), @"Delay|WaitForExit|Sleep|CancelAfter") && values.Length > 0)
                    duration = Eval(values[0], constants) / 1000;
                Add(report, path, Line(call), "call", call.ToString(), duration);
            }
            else if (node is ObjectCreationExpressionSyntax creation && creation.Type.ToString().EndsWith("CancellationTokenSource", StringComparison.Ordinal) && creation.ArgumentList?.Arguments.FirstOrDefault() is { } arg)
                Add(report, path, Line(creation), "call", creation.ToString(), Duration(arg.Expression, constants) ?? Eval(arg.Expression, constants) / 1000);
            else if (node is AssignmentExpressionSyntax assignment && Regex.IsMatch(assignment.Left.ToString(), @"(?i)timeout|deadline|MaxWait"))
                Add(report, path, Line(assignment), "call", assignment.ToString(), Duration(assignment.Right, constants) ?? Scale(Eval(assignment.Right, constants), assignment.Left.ToString()));
            else if (node is VariableDeclaratorSyntax variable && variable.Initializer is { } initializer && Regex.IsMatch(variable.Identifier.ValueText, @"(?i)timeout|deadline|MaxWait"))
                Add(report, path, Line(variable), path.StartsWith("tools/", StringComparison.Ordinal) ? "tool" : "call", variable.ToString(), Duration(initializer.Value, constants) ?? Scale(Eval(initializer.Value, constants), variable.Identifier.ValueText));
        }
    }

    private static bool IsTimeout(AttributeSyntax attribute) => attribute.Name.ToString().Split('.').Last() is "Timeout" or "TimeoutAttribute";
    private static int Line(SyntaxNode node) => node.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
    private static double? Duration(ExpressionSyntax expression, Dictionary<string, ExpressionSyntax?> constants)
    {
        if (expression is not InvocationExpressionSyntax invocation) return null;
        var name = invocation.Expression.ToString();
        var value = Eval(invocation.ArgumentList.Arguments.FirstOrDefault()?.Expression, constants);
        if (name.EndsWith("TimeSpan.FromMilliseconds", StringComparison.Ordinal)) return value / 1000;
        if (name.EndsWith("TimeSpan.FromSeconds", StringComparison.Ordinal)) return value;
        if (name.EndsWith("TimeSpan.FromMinutes", StringComparison.Ordinal)) return value * 60;
        if (name.EndsWith("TimeSpan.FromHours", StringComparison.Ordinal)) return value * 3600;
        return null;
    }
    private static double? Eval(ExpressionSyntax? expression, Dictionary<string, ExpressionSyntax?> constants, int depth = 0)
    {
        if (expression == null || depth > 16) return null;
        if (expression is LiteralExpressionSyntax literal && double.TryParse(literal.Token.ValueText, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)) return number;
        if (expression is ParenthesizedExpressionSyntax parens) return Eval(parens.Expression, constants, depth + 1);
        if (expression is IdentifierNameSyntax id && constants.TryGetValue(id.Identifier.ValueText, out var value)) return Eval(value, constants, depth + 1);
        if (expression is PrefixUnaryExpressionSyntax unary && unary.IsKind(SyntaxKind.UnaryMinusExpression)) return -Eval(unary.Operand, constants, depth + 1);
        if (expression is BinaryExpressionSyntax binary)
        {
            var left = Eval(binary.Left, constants, depth + 1);
            var right = Eval(binary.Right, constants, depth + 1);
            return binary.Kind() switch { SyntaxKind.MultiplyExpression => left * right, SyntaxKind.DivideExpression when right != 0 => left / right, SyntaxKind.AddExpression => left + right, SyntaxKind.SubtractExpression => left - right, _ => null };
        }
        return null;
    }
    private static double? Scale(double? number, string name) => name.ToLowerInvariant() switch
    {
        var n when n.Contains("millisecond") || n.EndsWith("ms", StringComparison.Ordinal) => number / 1000,
        var n when n.Contains("minute") => number * 60,
        var n when n.Contains("second") => number,
        _ => null
    };
    private static double? Number(string text) => double.TryParse(text.Replace("_", ""), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : null;
    private static void ReadPolicy(string path, string text, AuditReport report)
    {
        using var doc = JsonDocument.Parse(text);
        if (!doc.RootElement.TryGetProperty("watchdogs", out var watchdogs)) return;
        foreach (var item in watchdogs.EnumerateObject())
            Add(report, path, 1, "policy", "watchdogs." + item.Name, item.Value.GetDouble() / 1000);
    }
    private static void ReadCosts(string path, string text, AuditReport report)
    {
        using var doc = JsonDocument.Parse(text);
        if (!doc.RootElement.TryGetProperty("classes", out var classes)) return;
        foreach (var item in classes.EnumerateObject())
            Add(report, path, 1, "cost", item.Name + " basis=" + item.Value.GetProperty("basis").GetString(), item.Value.GetProperty("seconds").GetDouble(), item.Name);
    }
    private static void ReadPlan(string path, string text, AuditReport report)
    {
        string[]? header = null;
        var active = false;
        var lines = text.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (line == "### Checkpoints") { active = true; continue; }
            if (!active) continue;
            if (line.StartsWith('#')) break;
            if (!line.StartsWith('|')) continue;
            var cells = Regex.Split(line.Trim('|'), @"(?<!\\)\|").Select(x => x.Trim().Trim('`').Replace("\\|", "|")).ToArray();
            if (header == null) { header = cells; continue; }
            if (cells.All(x => x.Trim('-', ':', ' ').Length == 0)) continue;
            var estimateIndex = Array.IndexOf(header, "EstimatedMinutes");
            var filterIndex = Array.IndexOf(header, "Filter");
            if (estimateIndex < 0 || filterIndex < 0 || cells.Length != header.Length)
            { report.Warnings.Add(path + ":" + (i + 1) + ": unresolved checkpoint columns"); continue; }
            var estimate = Number(cells[estimateIndex]);
            // Current importer semantics, not R5 policy. Expose the actual implicit watchdog for audit.
            Add(report, path, i + 1, "row", cells[0] + " EstimatedMinutes=" + cells[estimateIndex], estimate == null ? null : Math.Max(15, 3 * estimate.Value) * 60, filter: cells[filterIndex]);
            var windowsIndex = Array.IndexOf(header, "EstimatedMinutesWindows");
            if (windowsIndex >= 0 && !string.IsNullOrWhiteSpace(cells[windowsIndex]))
            {
                var windows = Number(cells[windowsIndex]);
                Add(report, path, i + 1, "row-windows", cells[0] + " EstimatedMinutesWindows=" + cells[windowsIndex], windows == null ? null : Math.Max(15, 3 * windows.Value) * 60, filter: cells[filterIndex]);
            }
        }
    }
    private static void Add(AuditReport report, string path, int line, string kind, string expression, double? seconds, string? test = null, string? filter = null)
    {
        if (seconds is { } value && !double.IsFinite(value)) seconds = null;
        var id = $"{path}:{line}:{kind}:{test ?? filter ?? expression}";
        if (!report.Inventory.Any(x => x.Id == id)) report.Inventory.Add(new(id, path, line, kind, expression, seconds, test, filter));
    }
}
