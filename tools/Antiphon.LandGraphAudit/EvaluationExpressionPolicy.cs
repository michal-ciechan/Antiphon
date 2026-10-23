using System.Text.RegularExpressions;
using System.Xml;
using Microsoft.Build.Evaluation;

namespace Antiphon.LandGraphAudit;

/// <summary>
/// A skip approval requires independence from unhashed filesystem state, including absent
/// files and values used only in metadata. A matching canary result cannot prove that.
/// This gate recognizes call identities, never expands arguments or evaluates paths.
/// </summary>
internal static class EvaluationExpressionPolicy
{
    // Deliberately member-scoped. New members require a matched allowance/refusal fixture.
    // These are lexical operations only; discovery, reflection and environment access refuse.
    private static readonly HashSet<string> PureFunctions = new(StringComparer.OrdinalIgnoreCase)
    {
        "System.IO.Path::Combine", "System.IO.Path::GetDirectoryName",
        "System.IO.Path::GetFileName", "System.IO.Path::GetExtension", "System.IO.Path::ChangeExtension",
        "System.String::Copy", "System.String::IsNullOrEmpty",
        "MSBuild::NormalizePath", "MSBuild::NormalizeDirectory", "MSBuild::EnsureTrailingSlash", "MSBuild::Subtract",
    };

    // Census-minimal trust boundary. A new name needs matched allowance/refusal fixtures.
    // Knob reads also require an exact XML pin, including inactive branches.
    private static readonly HashSet<string> ReservedProperties = new(StringComparer.OrdinalIgnoreCase)
    {
        "MSBuildThisFileDirectory", "MSBuildProjectDirectory", "OS",
    };
    private static readonly HashSet<string> ReviewedEnvironmentProperties = new(StringComparer.OrdinalIgnoreCase)
    {
        "UsePublishedPackages", "AntiphonMessagingVersion", "EnablePublicApiAnalyzers", "AntiphonCleanFileMaxBytes",
        "PkgMicrosoft_Windows_Console_ConPTY", "DefaultItemExcludes", "SourceRevisionId", "WarningsAsErrors",
    };
    private static readonly HashSet<string> Timestamps = new(StringComparer.OrdinalIgnoreCase)
    {
        "ModifiedTime", "CreatedTime", "AccessedTime",
    };
    private static readonly Regex PropertyRead = new(@"\$\(\s*(?<name>[A-Za-z_][A-Za-z0-9_-]*)\s*\)", RegexOptions.CultureInvariant);
    private static readonly Regex MetadataName = new(@"\A\s*(?:[A-Za-z_][A-Za-z0-9_-]*\s*\.\s*)?(?<name>[A-Za-z_][A-Za-z0-9_-]*)\s*\z", RegexOptions.CultureInvariant);
    private static readonly Regex MetadataFunction = new(@"->\s*(?:Metadata|WithMetadataValue|WithoutMetadataValue|HasMetadata|AnyHaveMetadataValue)\s*\(", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex LiteralMetadataArgument = new("\\A\\s*(['\"\x60])(?<name>[A-Za-z_][A-Za-z0-9_-]*)\\1\\s*(?:,|\\))", RegexOptions.CultureInvariant);

    // Called before Locator registration by TargetPins. Keep this method and its static
    // initialization free of Microsoft.Build dependencies. Decode the same single %xx layer
    // so an escaped read cannot avoid the pin predicate.
    internal static bool ReadsReviewedEnvironment(string text) =>
        PropertyRead.Matches(text).Any(match => ReviewedEnvironmentProperties.Contains(match.Groups["name"].Value))
        || PropertyRead.Matches(Regex.Replace(text, @"%([0-9a-fA-F]{2})",
                match => ((char)Convert.ToInt32(match.Groups[1].Value, 16)).ToString()))
            .Any(match => ReviewedEnvironmentProperties.Contains(match.Groups["name"].Value));

    private static readonly Regex PlainProperty = new(@"\A[A-Za-z_][A-Za-z0-9_-]*\z", RegexOptions.CultureInvariant);
    private static readonly Regex StaticHead = new(
        @"\A\s*\[\s*(?<type>[A-Za-z_][A-Za-z0-9_.]*)\s*\]\s*::\s*(?<member>[A-Za-z_][A-Za-z0-9_]*)\s*\(",
        RegexOptions.CultureInvariant);
    private static readonly Regex FilesystemType = new(
        @"\A\s*\[\s*System\.IO\.(File|Directory|FileInfo|DirectoryInfo)\s*\]",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static void Check(string repo, ISet<string> refusals)
    {
        foreach (var (relative, document) in TargetPins.DefinitionDocuments(repo))
            Visit(document.DocumentElement!, relative, refusals);
    }

    private static void Visit(XmlElement element, string relative, ISet<string> refusals)
    {
        // Program has already validated every Target's exact pin. Target execution is the
        // separate reviewed trust boundary; an OS pin on an evaluation element is no exemption.
        if (element.LocalName == "Target") return;
        var owner = relative + " " + TargetPins.ElementPath(element);
        foreach (XmlAttribute attribute in element.Attributes)
            Inspect(attribute.Value, owner + " @" + attribute.Name, refusals);
        Inspect(string.Concat(element.ChildNodes.OfType<XmlCharacterData>()
            .Where(text => text is XmlText or XmlCDataSection).Select(text => text.Value)), owner, refusals);
        foreach (var child in element.ChildNodes.OfType<XmlElement>())
            Visit(child, relative, refusals);
    }

    private static void Inspect(string text, string owner, ISet<string> refusals)
    {
        InspectSpelling(text, owner, refusals);
        // Use MSBuild's own decoding, after Locator registration. Inspect both spellings:
        // encoded call heads cannot evade the gate and raw calls cannot hide behind escapes.
        var unescaped = ProjectCollection.Unescape(text);
        if (unescaped != text) InspectSpelling(unescaped, owner, refusals);
    }

    private static void InspectSpelling(string text, string owner, ISet<string> refusals)
    {
        for (var offset = 0; (offset = text.IndexOf("%(", offset, StringComparison.Ordinal)) >= 0; offset += 2)
        {
            var end = ClosingParenthesis(text, offset + 1);
            var name = end < 0 ? Match.Empty : MetadataName.Match(text[(offset + 2)..end]);
            if (!name.Success || Timestamps.Contains(name.Groups["name"].Value))
                refusals.Add($"refused: {owner} timestamp-metadata");
        }
        foreach (Match function in MetadataFunction.Matches(text))
        {
            var argument = LiteralMetadataArgument.Match(text[function.Index + function.Length..]);
            if (!argument.Success || Timestamps.Contains(argument.Groups["name"].Value))
                refusals.Add($"refused: {owner} timestamp-metadata");
        }

        // Advance only past the opening '$(', not past the whole expression: nested calls
        // inside arguments (including quoted arguments) each receive their own decision.
        for (var offset = 0; (offset = text.IndexOf("$(", offset, StringComparison.Ordinal)) >= 0; offset += 2)
        {
            var end = ClosingParenthesis(text, offset + 1);
            var expression = end < 0 ? text[(offset + 2)..] : text[(offset + 2)..end];
            string? reason = null;
            if (FilesystemType.IsMatch(expression)) reason = "filesystem-function";
            else if (end < 0) reason = "unsupported-function";
            else if (PlainProperty.IsMatch(expression.Trim()))
            {
                var name = expression.Trim();
                if (!ReservedProperties.Contains(name) && !ReviewedEnvironmentProperties.Contains(name))
                    reason = "environment-property";
            }
            else
            {
                var head = StaticHead.Match(expression);
                if (!head.Success || !PureFunctions.Contains(head.Groups["type"].Value + "::" + head.Groups["member"].Value))
                    reason = "unsupported-function";
                else
                {
                    var close = ClosingParenthesis(expression, head.Length - 1);
                    // No property, indexer or instance-method chain is implicitly trusted
                    // merely because its first call is lexical.
                    if (close < 0 || expression[(close + 1)..].Trim().Length != 0)
                        reason = "unsupported-function";
                }
            }
            // Diagnostics identify the XML owner and refusal class, never argument/file bytes.
            if (reason is not null) refusals.Add($"refused: {owner} {reason}");
        }
    }

    private static int ClosingParenthesis(string text, int open)
    {
        var depth = 0;
        var quote = '\0';
        for (var i = open; i < text.Length; i++)
        {
            var c = text[i];
            if (quote != '\0')
            {
                if (c == quote) quote = '\0';
                continue;
            }
            if (c is '\'' or '"' or '`') quote = c;
            else if (c == '(') depth++;
            else if (c == ')' && --depth == 0) return i;
        }
        return -1;
    }
}
