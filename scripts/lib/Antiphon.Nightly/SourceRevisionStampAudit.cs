// The SourceRevisionId census of the remaining PowerShell build surfaces (unit 39 of
// docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md; it replaced scripts/test-source-revision-stamp.ps1, the driver
// SourceRevisionStampContractTests ran). A deployed binary is compared with a SHA, so a product build that a PowerShell surface
// still runs (scripts/restart-*, deploy-*, publish-*, release-*, promote-*, run-daemon.ps1, start-*-runner.ps1) must pass
// -p:SourceRevisionId=$sourceRevision. The stamped builds themselves are C# now (units 10, 30, 36 and 37), so the census expects
// none; it keeps every surface honest until the last of them is ported.
//
// The driver read each surface with System.Management.Automation's parser. The test process cannot load that assembly, so
// this reads with the core's PowerShell reader (PsReader, unit 17) and walks the same nodes the driver walked:
//   - a dotnet build, pack, publish or run (not run --no-build) is a build; so is the dotnet argv build-slot.ps1 forwards
//     after its "--", and dotnet @splat. A build is stamped when exactly one argument is the stamp: the parameter
//     -p:SourceRevisionId=$sourceRevision, or that expandable string with $sourceRevision as its one nested variable. A splat
//     is stamped when the splat's last "=" in the call's own block is followed, in that block, by "+= <the stamp>";
//   - a command inside if ($false) { } is skipped;
//   - Invoke-Expression, iex and Open-Tab's last argument, and Start-Process (start, saps) -ArgumentList's elements, are read
//     again as PowerShell when they name dotnet build, run, pack, publish or @; Start-Process dotnet joins its argument list
//     after "dotnet ".
// One difference from PowerShell's parser is handled here: after "--" PowerShell reads an unquoted -p:x as two words, "-p:"
// and "x", so it is never the stamp; PsReader keeps it one word, so an expandable string that starts with a dash is not the
// stamp. A surface (or argument text) the reader cannot read is a finding, as the driver's parse error was. Plain C#: no
// PowerShell types. ASCII only.
#nullable disable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Antiphon.Nightly
{
    /// <summary>One product build a PowerShell text runs: whether it carries the stamp, and its command text.</summary>
    public sealed record SourceRevisionStampBuild(bool Stamped, string Invocation);

    /// <summary>The SourceRevisionId census of the PowerShell build surfaces (test-source-revision-stamp.ps1).</summary>
    public static class SourceRevisionStampAudit
    {
        /// <summary>The stamp argument's text.</summary>
        public const string Stamp = "-p:SourceRevisionId=$sourceRevision";

        /// <summary>The surfaces: the scripts/*.ps1 names that may build a deployed binary.</summary>
        public static readonly Regex SurfaceName = new Regex(@"^(restart|deploy|publish|release|promote)-|^run-daemon\.ps1$|^start-.*-runner\.ps1$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly string[] Dotnet = { "dotnet", "dotnet.exe" };
        private static readonly string[] BuildVerbs = { "build", "pack", "publish", "run" };
        private static readonly string[] ExpressionCommands = { "Invoke-Expression", "iex", "Open-Tab" };
        private static readonly string[] StartProcessCommands = { "Start-Process", "start", "saps" };
        private static readonly string[] ArgumentListNames = { "ArgumentList", "Args" };

        private static readonly Regex ChildDotnet = new Regex(@"(?:^|[;\s])dotnet(?:\.exe)?\s+(?:build|run|pack|publish|@)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly Regex BuildSlot = new Regex(@"build-slot\.ps1", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>The checkout's surfaces, as scripts/&lt;name&gt;.ps1, in name order.</summary>
        public static IReadOnlyList<string> Surfaces(string repoRoot)
        {
            var scripts = Path.Combine(repoRoot ?? "", "scripts");
            if (!Directory.Exists(scripts)) return Array.Empty<string>();
            return Directory.EnumerateFiles(scripts)
                .Select(Path.GetFileName)
                .Where(name => string.Equals(Path.GetExtension(name), ".ps1", StringComparison.OrdinalIgnoreCase) && SurfaceName.IsMatch(name))
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .Select(name => "scripts/" + name)
                .ToArray();
        }

        /// <summary>Every finding over the checkout's surfaces: "surface: invocation" per unstamped build, or the surface unreadable.</summary>
        public static IReadOnlyList<string> Audit(string repoRoot)
        {
            var findings = new List<string>();
            foreach (var surface in Surfaces(repoRoot))
            {
                IReadOnlyList<SourceRevisionStampBuild> builds;
                try
                {
                    builds = Builds(File.ReadAllText(Path.Combine(repoRoot, surface)));
                }
                catch (InvalidDataException e)
                {
                    findings.Add(surface + ": " + e.Message);
                    continue;
                }
                findings.AddRange(builds.Where(build => !build.Stamped).Select(build => surface + ": " + build.Invocation));
            }
            return findings;
        }

        /// <summary>The product builds a PowerShell text runs, in source order. Throws InvalidDataException when it cannot be read.</summary>
        public static IReadOnlyList<SourceRevisionStampBuild> Builds(string text)
        {
            var builds = new List<SourceRevisionStampBuild>();
            Collect(text ?? "", builds);
            return builds;
        }

        /// <summary>The invocations of the unstamped builds.</summary>
        public static IReadOnlyList<string> Unstamped(string text) =>
            Builds(text).Where(build => !build.Stamped).Select(build => build.Invocation).ToArray();

        private static void Collect(string text, List<SourceRevisionStampBuild> builds)
        {
            var root = PsReader.Parse(text, out bool failed);
            if (failed) throw new InvalidDataException("the PowerShell reader cannot read " + Quote(text));
            foreach (var command in root.FindAll())
            {
                if (command.Kind != PsKind.Command || DeadBranch(command, text)) continue;
                var name = command.CommandName;
                var elements = command.Items;
                int dotnetIndex = -1;
                if (In(name, Dotnet)) dotnetIndex = 0;
                else
                {
                    // The build-slot wrapper forwards the argv following its separator.
                    for (int i = 1; i < elements.Count - 1; i++)
                    {
                        if (Text(text, elements[i]) == "--" && In(ValueOf(elements[i + 1]), Dotnet)
                            && BuildSlot.IsMatch(string.Join(" ", elements.Take(i).Select(element => Text(text, element)))))
                        {
                            dotnetIndex = i + 1;
                            break;
                        }
                    }
                }
                if (dotnetIndex >= 0 && elements.Count > dotnetIndex + 1)
                {
                    var verb = elements[dotnetIndex + 1];
                    var arguments = elements.Skip(dotnetIndex + 2).ToList();
                    bool stamped;
                    if (verb.Kind == PsKind.Variable && verb.Splatted) stamped = StampedSplat(root, command, verb, text);
                    else if (In(ValueOf(verb), BuildVerbs))
                    {
                        if (Same(ValueOf(verb), "run") && arguments.Any(argument => Same(Text(text, argument), "--no-build"))) continue;
                        stamped = arguments.Count(argument => StampArgument(argument, text)) == 1;
                    }
                    else continue;
                    builds.Add(new SourceRevisionStampBuild(stamped, Text(text, command)));
                }

                var children = ScriptArguments(command, name);
                if (children.Count > 0 && In(name, StartProcessCommands)
                    && elements.Skip(1).Take(Math.Min(3, elements.Count - 1)).Any(element => Same(ValueOf(element), "dotnet")))
                {
                    var argv = children.Count == 1 ? ValueOf(children[0]) ?? "" : string.Join(" ", children.Select(child => Text(text, child)));
                    Collect("dotnet " + argv, builds);
                    continue;
                }
                foreach (var argument in children)
                {
                    if (argument.Kind != PsKind.StringConstant && argument.Kind != PsKind.ExpandableString) continue;
                    if (ChildDotnet.IsMatch(argument.Value ?? "")) Collect(argument.Value, builds);
                }
            }
        }

        // The parameter -p:SourceRevisionId=$sourceRevision, or that expandable string with $sourceRevision as its one nested
        // variable (not an unquoted word after "--", which PowerShell reads as "-p:" and the rest).
        private static bool StampArgument(PsAst element, string text)
        {
            if (element == null) return false;
            if (element.Kind == PsKind.CommandParameter) return Text(text, element) == Stamp;
            return element.Kind == PsKind.ExpandableString && element.Value == Stamp
                && element.Items.Count == 1 && element.Items[0].Kind == PsKind.Variable && element.Items[0].Value == "sourceRevision"
                && !IsDash(text[element.Start]);
        }

        // The splat's last "=" in the call's block, then a "+= <stamp>" after it in that same block. A comment or another
        // branch has no authority here.
        private static bool StampedSplat(PsAst root, PsAst command, PsAst splat, string text)
        {
            var block = Block(command);
            var assignments = root.FindAll()
                .Where(node => node.Kind == PsKind.Assignment && node.Left != null && node.Left.Kind == PsKind.Variable
                    && Same(node.Left.Value, splat.Value) && node.Start < command.Start)
                .ToList();
            var initial = assignments.LastOrDefault(assignment => assignment.Value == "=" && Block(assignment) == block);
            if (initial == null) return false;
            foreach (var append in assignments)
            {
                if (append.Value != "+=" || append.Start < initial.End || append.Right == null || append.Right.Kind != PsKind.CommandExpression
                    || !StampArgument(append.Right.Child, text)) continue;
                if (Block(append) == block) return true;
            }
            return false;
        }

        // Invoke-Expression, iex and Open-Tab: the last element. Start-Process: the element after -ArgumentList (-Args), an
        // array's elements, @(...) read through its one pure expression.
        private static IReadOnlyList<PsAst> ScriptArguments(PsAst command, string name)
        {
            var elements = command.Items;
            if (In(name, ExpressionCommands)) return new[] { elements[elements.Count - 1] };
            if (!In(name, StartProcessCommands)) return Array.Empty<PsAst>();
            for (int i = 1; i < elements.Count - 1; i++)
            {
                var parameter = elements[i];
                if (parameter.Kind != PsKind.CommandParameter || !In(parameter.Value, ArgumentListNames)) continue;
                var argument = elements[i + 1];
                if (argument.Kind == PsKind.ArrayExpression && argument.Child != null && argument.Child.Items.Count == 1)
                {
                    var statement = argument.Child.Items[0];
                    if (statement.Kind == PsKind.Pipeline) argument = PureExpression(statement);
                }
                if (argument == null) return Array.Empty<PsAst>();
                if (argument.Kind == PsKind.ArrayLiteral) return argument.Items;
                return new[] { argument };
            }
            return Array.Empty<PsAst>();
        }

        // PipelineAst.GetPureExpression: one command expression without redirections.
        private static PsAst PureExpression(PsAst pipeline) =>
            pipeline.Items.Count == 1 && pipeline.Items[0].Kind == PsKind.CommandExpression && pipeline.Items[0].Children.Count == 1
                ? pipeline.Items[0].Child
                : null;

        // Inside a clause whose condition is the text $false.
        private static bool DeadBranch(PsAst node, string text)
        {
            for (var block = node.Parent; block != null; block = block.Parent)
            {
                if (block.Kind != PsKind.StatementBlock || block.Parent == null || block.Parent.Kind != PsKind.If) continue;
                // An if's children: condition and block pairs, then the else block.
                var clauses = block.Parent.Children;
                int at = clauses.IndexOf(block);
                if (at % 2 == 1 && Same(Text(text, clauses[at - 1]), "$false")) return true;
            }
            return false;
        }

        private static PsAst Block(PsAst node)
        {
            for (var parent = node.Parent; parent != null; parent = parent.Parent)
            {
                if (parent.Kind == PsKind.StatementBlock || parent.Kind == PsKind.NamedBlock) return parent;
            }
            return null;
        }

        // A node's .Value as PowerShell has it: strings and constants only.
        private static string ValueOf(PsAst node) =>
            node != null && (node.Kind == PsKind.StringConstant || node.Kind == PsKind.ExpandableString || node.Kind == PsKind.Constant) ? node.Value : null;

        private static string Text(string text, PsAst node) => text.Substring(node.Start, node.End - node.Start);

        private static bool In(string value, string[] candidates) => value != null && candidates.Any(candidate => Same(value, candidate));

        private static bool Same(string left, string right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

        private static bool IsDash(char c) => c == '-' || c == (char)0x2013 || c == (char)0x2014 || c == (char)0x2015;

        private static string Quote(string text)
        {
            var line = (text ?? "").Replace("\r", " ").Replace("\n", " ");
            return "'" + (line.Length > 80 ? line.Substring(0, 80) + "..." : line) + "'";
        }
    }
}
