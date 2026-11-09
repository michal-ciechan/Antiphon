// Moved from the header of scripts/audit-land-verification-precheck.ps1 when its rules were ported here.
// CARD-1483 S5: internal emulation pre-check. This is not evaluation approval.
// The production entry point is audit-land-verification-graph.ps1, which requires both checks.
// CARD-1385: hash the dotnet-build definition files at HEAD and compare them
// with server/Application/Services/LandVerificationBuildGraph.manifest.txt.
// CARD-1390: the trailing NUL check is ordinal. Every ls-tree row is validated,
// not only definition rows.
// CARD-1400: -Check and -Update refuse an EmbeddedResource, Content, None,
// Compile, or AdditionalFiles include that can select docs/**/*.md or the root
// README.md. Matching is case-insensitive after one %XX decode. Every such
// include is resolved from the definition file's own directory and from each
// importing project's directory, including through another import. The nearest
// Directory.Build.props and Directory.Build.targets above a project count as
// imported. An Import Project path that starts with $(MSBuildThisFileDirectory)
// or $(MSBuildProjectDirectory) is expanded from that property and is not
// prefixed with the importing file's folder again. $(MSBuildThisFileDirectory)
// in an include is that file's directory. $(MSBuildProjectDirectory) is the
// directory being checked.
// CARD-1409 repair 4 (Review 937bb1c9): file items are classified by what they
// RESOLVE to, not by spelling. Every $(...) in a path-item Include or Update is
// expanded from the tracked definition files' own property values (every
// definition, where it is defined; the two directory properties; an output or
// intermediate root such as $(IntermediateOutputPath); a Pkg* path property with
// GeneratePathProperty; an MSBuild task output, also through
// @(Item->'%(RootDir)%(Directory)'), %(Item.Metadata) and GetDirectoryName). A
// property the audit cannot resolve statically refuses by name; a path rooted
// at an output or package root accepts any tail without '..'. A repo pattern is
// expanded against every blob tracked at HEAD with a real glob matcher (honouring
// Exclude): the root README.md, an exact docs/ path other than the one
// allow-listed input, a wildcard whose first segment can be docs/ (whatever it
// matches today), and a markdown or text match outside the document allow-list
// each refuse naming the resolved path. Item metadata names are allow-listed per
// item type (ProjectReference AdditionalProperties, Properties and every unknown
// name refuse). A property value that reads a file through a property function
// outside an output root, or whose literal part is a docs path, refuses.
// CARD-1409 repair 5 (Review f0fb9035): one path normaliser (ConvertTo-CanonicalPath) is the only
// way resolved text becomes a repository path, and every comparison consumes its result: include and
// update parts, Exclude parts, import targets, project references, literal property-value paths, the
// tail of an output- or package-rooted path, every tracked match a wildcard selects and every tracked
// definition path. Each segment is held to the Windows land host's rules first: a trailing '.' or
// space, a segment of only dots or spaces, a ':' anywhere (the drive prefix is converted before this
// point) and an 8.3 short name ('~' then a digit) refuse naming the segment; '.' and '..' collapse only
// afterwards; letter case is kept and compared ordinal-ignore-case. A property value is free text, so
// there a divergent segment is read as the host reads it and refuses only when that reading is docs/
// or the root README.md. The MSBuild item-operation surface is a closed table (plan D-29): Include and
// Update are censused, including Update metadata for %(Item.Metadata) and Pkg* expansion; Exclude
// subtracts only; Remove subtracts only and may carry no metadata; KeepMetadata, RemoveMetadata,
// KeepDuplicates, MatchOnMetadata and MatchOnMetadataOptions refuse by name; an element with no
// operation refuses; any other attribute is metadata and must be allow-listed. A path item whose
// resolved pattern reaches a tracked symbolic link (mode 120000), as a match or as a directory it
// passes through, refuses naming the link.
// CARD-1409 repair 6 (Review c64987ea): one pipeline (Resolve-ItemPathText) turns every item or import text
// into path specs in MSBuild's order, read from a scratch 'dotnet msbuild -getItem' run: expand properties
// and metadata (values spliced in their escaped form), split the expanded text on ';' (an escaped %3B is not
// a separator; a ';' inside an expanded value is), decode %XX once per part (two hex digits in either case
// become one character; %252E is the literal %2E; a malformed %X or %GG stays literal; %00 refuses by name),
// then normalise (D-28). A decoded $( @( %( is literal text, never a reference. An escaped * or ? is literal
// for MSBuild; the audit reads the decoded include as a wildcard, which selects at least the literal name,
// and drops an Exclude part whose decoded text gained a wildcard character. A root belongs to the first part
// only. Every consumer runs through the pipeline and it is the only ';' split: Include, Update, Exclude,
// Import Project, ProjectReference, name items, Pkg* ids, literal property-value paths, the tail of a
// file-reading function argument (which may not climb out of its output root; a property no tracked file
// may set is the SDK's own, read as one opaque segment) and output-root tails. CARD-1409 repair 7 (Review
// 7db88932): the D-25 skip of a value part that carries an item, metadata or property-function reference is
// per part, so the other parts of the same value are still classified. CARD-1409 repair 8 (Review d43d1e4a):
// with the two directory properties only (imports, values, name items, Pkg* ids), the text is split on ';'
// first and a directory property at the start of each part is that directory, as MSBuild expands then splits.
// CARD-1409 repair 9 (Review 4ecdb753): MSBuild escapes % $ @ ; ' ( ) * ? in $(MSBuildProjectDirectory) and not
// in $(MSBuildThisFileDirectory), so a tracked definition path holding one refuses by name (char=<c>) before any
// file is read; every spliced directory and every import or reference target is such a path.
// CARD-1409: an Import Project path that still contains $(, @( or %( after the
// two known properties are expanded is refused and is not followed; there is no
// allow-list. An Import with an attribute other than Project, Condition or Label
// is refused; Condition is not evaluated. An Import Project is split on ';' as
// MSBuild splits it, and every non-empty part is audited as its own import. Each
// part must be tracked at HEAD inside the repository as a *proj, *proj.user,
// .props or .targets file; an empty Project, a path outside the repository, a
// path absent from HEAD or a tracked file with another extension is refused. A
// part that contains * or ? is refused outright: land digests only definition
// files, so a later non-definition match would never be re-audited (Review
// 23aedc52). The nearest Directory.Packages.props above a project counts as
// imported like Directory.Build.props, and a tracked <project>.user beside a
// project is imported by it; both resolve includes from the project directory.
// ReadLinesFromFile, Copy, and property functions that read files are not
// inspected.
// CARD-1409 (Review 8989ece8 F1): Microsoft.Common.props and .targets import
// $(MSBuildProjectExtensionsPath)$(MSBuildProjectFile).*.props and .*.targets,
// obj/ by default, and the audit does not follow those wildcards. A tracked
// definition file under any obj/ path segment (case-insensitive) is refused by
// its path, whatever it contains. A definition file that sets
// BaseIntermediateOutputPath, MSBuildProjectExtensionsPath or
// IntermediateOutputPath is refused because a redirected extension directory
// cannot be evaluated; reading one of them inside another property is not
// setting it.
// CARD-1409 (Review 31c77116): the audit is default-deny. A definition file may
// set only an allow-listed property name, seeded from the tracked definition
// files (the plan keeps the inventory), wherever the PropertyGroup sits and
// including a task Output PropertyName; declare only an allow-listed item type,
// each under a rule: a path item (Compile, Content, None, EmbeddedResource,
// AdditionalFiles, EditorConfigFiles, GlobalAnalyzerConfigFiles and the
// repository's own target-time items) must stay inside the repository and never
// select docs markdown or the root README.md, a ProjectReference must resolve to
// a tracked *proj from every directory it is evaluated from, and a package-style
// item (PackageReference, FrameworkReference, InternalsVisibleTo) is a bare
// name; and hold under <Project> only PropertyGroup, ItemGroup, Import, Target
// and an allow-listed <Sdk>. The Project Sdk attribute is allow-listed too. An
// include that carries @( or %( refuses. A tracked .rsp file is parsed: each
// non-comment line is one switch among -nodeReuse, -maxcpucount, -verbosity and
// -nologo with a checked value; -p:, -property:, @file and every other switch
// refuse. Anything the audit cannot classify refuses by name. Target task
// parameters are not evaluated; item metadata names are (repair 4).

using System.Collections;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;

namespace Antiphon.LandGraphAudit;

/// <summary>
/// CARD-1483 S5: the internal emulation pre-check of the land build graph. This is not evaluation approval;
/// scripts/audit-land-verification-graph.ps1 requires both this check and real MSBuild evaluation.
/// A port of the former scripts/audit-land-verification-precheck.ps1 (which now only runs this through the
/// LandGraphAudit tool's <c>precheck</c> command); every rule below keeps the script's CARD-1385, 1390, 1400,
/// 1409, 1483 and 1613 semantics, its messages and its exit codes. The PowerShell operators it was written
/// with are reproduced by <see cref="Ps"/>, including the XML adapter's property shadowing, so a definition
/// file is classified exactly as the script classified it.
/// <para>
/// CARD-1385: hash the dotnet-build definition files at HEAD and compare them with
/// server/Application/Services/LandVerificationBuildGraph.manifest.txt. CARD-1390: the trailing NUL check is
/// ordinal and every ls-tree row is validated. CARD-1400 and CARD-1409 (repairs 4 to 9): default-deny over
/// the definition files' structure, properties, item types, item metadata, item operations, imports,
/// project references, response files and every path an item can resolve to; see the rule comments below.
/// </para>
/// Check (the default) prints <c>digest=&lt;HEX&gt; count=&lt;N&gt;</c> and returns 0 only when both manifest
/// lines match. Update rewrites the manifest from HEAD. 1 is a mismatch, a refusal or bad usage; 2 is a git
/// failure.
/// </summary>
public static class LandGraphPrecheck
{
    public const int Approved = 0;
    public const int Refused = 1;
    public const int GitFailed = 2;

    /// <param name="repoRoot">The repository root (the script's parent of <c>$PSScriptRoot</c>).</param>
    /// <param name="check">The script's <c>-Check</c>.</param>
    /// <param name="update">The script's <c>-Update</c>.</param>
    /// <param name="manifestPath">The script's <c>-ManifestPath</c>; empty selects the repository manifest.</param>
    /// <param name="stdout">Receives the digest line.</param>
    /// <param name="stderr">Receives refusal and failure messages.</param>
    /// <param name="environment">Variables set (null removes) for the git processes, and PATH for finding git.</param>
    public static int Run(string? repoRoot, bool check, bool update, string? manifestPath,
        TextWriter stdout, TextWriter stderr, IReadOnlyDictionary<string, string?>? environment = null)
    {
        using var audit = new PrecheckAudit(stdout, stderr, environment);
        try
        {
            return audit.Execute(repoRoot, check, update, manifestPath);
        }
        catch (PrecheckExit exit)
        {
            return exit.Code;
        }
        catch (Exception error)
        {
            // The script ran with $ErrorActionPreference = 'Stop': an uncaught error ended it with exit 1.
            stderr.WriteLine("land graph pre-check failed: " + error.Message);
            return Refused;
        }
    }
}

internal sealed class PrecheckExit(int code) : Exception("pre-check exit " + code.ToString(CultureInfo.InvariantCulture))
{
    public int Code { get; } = code;
}

internal sealed class PathText(string kind, string text, bool anchored, string source = "")
{
    public string Kind { get; set; } = kind;

    public string Text { get; set; } = text;

    public bool Anchored { get; set; } = anchored;

    public string Source { get; } = source;
}

internal sealed class Canonical
{
    public string Status { get; init; } = "";

    public string Path { get; init; } = "";

    public string Segment { get; init; } = "";

    public string Reason { get; init; } = "";

    public Canonical? Divergent { get; init; }

    public bool Refused => Status == "refused";
}

internal sealed record PropertyDefinition(string File, string Value, bool Conditional);

internal sealed record ItemDeclaration(string File, string Include, Hashtable Metadata);

internal sealed record TailProblem(string Problem, Canonical? Canonical);

internal sealed class PrecheckAudit : IDisposable
{
    // CARD-1409 (Review 31c77116): the allow-lists. Every entry is used by a tracked
    // definition file; the plan's inventory lists where. A new name is a reviewed edit here.
    private static readonly string[] AllowedProjectSdks = ["Microsoft.NET.Sdk", "Microsoft.NET.Sdk.Web"];
    private static readonly string[] AllowedSdkElements = ["Aspire.AppHost.Sdk"];
    private static readonly string[] AllowedProjectChildren = ["PropertyGroup", "ItemGroup", "Import", "Target", "Sdk"];

    private static readonly HashSet<string> AllowedProperties = new(
    [
        "_AntiphonCleanFileChars", "_AntiphonRepeatAdditional", "_AntiphonRepeatFile", "_CustodyChildOutputDir",
        "_F5PtyHostNativeDir", "_FakeClaudeOutputDir", "_FakeGrokOutputDir", "_GitHeadSha", "_GitHeadShaTrimmed",
        "_GitRevParseExitCode", "_LandFakeGrokDir", "_PtyHostNativeDir", "_StagedHelperDest", "_StagedHelperDir",
        "_StagedHelperFramework", "_StagedHelperGlob", "_StagedHelperShared", "AntiphonCleanFileMaxBytes", "AntiphonMessagingVersion",
        "AssemblyName", "Authors", "ConcurrentGarbageCollection", "DefaultItemExcludes", "Description",
        "EnableMicrosoftTestingPlatformRunner", "EnablePublicApiAnalyzers", "EnableTUnitSourceGeneration", "FakeBrowserOutputDir",
        "FakeBrowserTargetPath", "ImplicitUsings", "InvariantGlobalization", "IsAspireHost", "IsPackable",
        "IsTestProject", "Nullable", "OutputType", "PackageId", "PackageLicenseExpression", "PackageProjectUrl",
        "PackageReadmeFile", "PackageTags", "PackAsTool", "PublishRepositoryUrl", "RepositoryUrl", "RollForward",
        "RootNamespace", "ServerGarbageCollection", "SourceControlInformationFeatureSupported", "SourceRevisionId",
        "TargetFramework", "ToolCommandName", "UseAppHost", "UsePublishedPackages", "UserSecretsId", "Version",
        "WarningsAsErrors",
    ], StringComparer.OrdinalIgnoreCase);

    // CARD-1483 (Review cff23aba D-1): property -> the only element texts (ordinal) it may be set to. The value
    // reaches an MSBuild task Properties string, which splits on ';'; a task Output may not set it.
    private static readonly Hashtable PinnedPropertyValues = PinnedProperties();

    // Item type -> rule. path: resolved like a file include and refused when it leaves the repository
    // or selects docs markdown or the root README.md. project: a tracked *proj. name: a bare identifier.
    // text: source lines handed to WriteLinesToFile inside a target, never a file path (CARD-1409 repair 4).
    private static readonly Dictionary<string, string> ItemRules = Rules();

    // SDK internals are not general item types. Only the four exact removals in the
    // separately pinned fake-provider target are admitted; task outputs still refuse.
    private static readonly string[] SdkRemovalItems =
    [
        "_SourceItemsToCopyToOutputDirectory", "_SourceItemsToCopyToOutputDirectoryAlways",
        "_SourceItemsToCopyToOutputDirectoryIfDifferent", "AllItemsFullPathWithTargetPath",
    ];

    // CARD-1409 repair 4 (Review 937bb1c9 F3): item metadata names, as attributes or child elements, per
    // item type, seeded from the tracked definition files. A type absent here allows none. The global-property
    // metadata (AdditionalProperties, Properties, GlobalProperties, SetConfiguration, SetPlatform,
    // SetTargetFramework, UndefineProperties) and every other name refuse: they reopen the R2 property imports.
    // CARD-1483: the exceptions are the pinned entries below, each allowed only as its one tree literal
    // (the LandGraphAudit tool is built for net10.0); any other value, the child-element form or a Remove refuses.
    // Aspire uses IsAspireProjectResource only for application-resource registration/validation;
    // it neither changes the referenced project nor supplies global properties to its evaluation.
    private static readonly Dictionary<string, string[]> ItemMetadata = new(StringComparer.OrdinalIgnoreCase)
    {
        ["PackageReference"] = ["Version", "GeneratePathProperty", "ExcludeAssets", "PrivateAssets", "IncludeAssets"],
        ["ProjectReference"] = ["Aliases", "ReferenceOutputAssembly", "Private", "IsAspireProjectResource"],
        ["Content"] = ["CopyToOutputDirectory", "CopyToPublishDirectory", "Link", "TargetPath"],
        ["Compile"] = ["Link"],
        ["EmbeddedResource"] = ["LogicalName"],
        ["None"] = ["Pack", "PackagePath", "CopyToOutputDirectory"],
        ["StagedHelper"] = ["Folder", "Glob"],
    };

    // Item type -> metadata attribute name -> the only value (ordinal) under which that name is allowed.
    // HelperTargetFramework and SetTargetFramework reach an MSBuild Properties string, which splits on ';'
    // (Review cff23aba D-1); SkipGetTargetFrameworkProperties is held to its tree value as well.
    // CARD-1613: *.cs.txt is Czech to AssignCulture. The fixture include and the post-split
    // target hold culture off. Only these exact attribute values are admitted; any other value,
    // the child-element form, or a Remove still refuses.
    private static readonly Hashtable PinnedItemMetadata = PinnedMetadata();

    // Item element attributes that are not metadata (compared ordinally, as MSBuild reads them).
    private static readonly string[] ItemElementAttributes = ["Include", "Exclude", "Update", "Remove", "Condition", "Label"];

    // CARD-1409 repair 5 (plan D-29, O-table): item operations the audit does not evaluate, refused by name.
    // Every other attribute is metadata and must be allow-listed above, so no operation is left unclassified.
    private static readonly string[] RefusedItemOperations = ["KeepMetadata", "RemoveMetadata", "KeepDuplicates", "MatchOnMetadata", "MatchOnMetadataOptions"];

    // CARD-1409 repair 4 (Review 937bb1c9 F1/F2): properties whose value is the build's own output or
    // intermediate directory. A path rooted at one is a build product, not a tracked input. A definition file
    // cannot set one (the property allow-list and D-12 refuse), the parsed .rsp cannot, and the land build
    // passes no -p:, so the root is the SDK's.
    private static readonly string[] OutputRootProperties =
    [
        "IntermediateOutputPath", "BaseIntermediateOutputPath", "MSBuildProjectExtensionsPath",
        "OutDir", "OutputPath", "BaseOutputPath", "TargetDir", "TargetPath", "PublishDir",
    ];

    // The one docs/ path a tracked definition file may select, by exact resolved path (never by pattern).
    private static readonly string[] AllowedDocsInputs = ["docs/superpowers/plans/2026-09-08-card-0449-claude-effort-dialog-captures.json"];

    // Markdown and text documents outside docs/ never skip a land build, but the audit still names what it
    // accepts: the resolved patterns the tracked definition files use (from the item's own directory and from
    // every importing project directory), three bare names the shared props use, a README.md beside a
    // non-root project, and one exact path. Any other selected document refuses by its matched path.
    private static readonly string[] DocumentExtensions = [".md", ".markdown", ".txt"];

    private static readonly string[] AllowedDocumentPatterns =
    [
        "server/Bundles/*.md", "server/Bundles/Presets/*.md", "server/Bundles/Fragments/*.md",
        "tests/Antiphon.Agents.Pty.Tests/golden/**/*", "tests/Antiphon.Agents.Pty.Tests/Fixtures/CodexStartup/**/*",
        "tests/Antiphon.Agents.Pty.Tests/probes/**/*", "tests/Antiphon.Tests/Agents/Fixtures/**",
        "tests/Antiphon.Messaging.Tests/Fixtures/*",
        "tests/Antiphon.Tests/Fixtures/NativeLinuxPolicy/*.cs.txt",
    ];

    private static readonly string[] AllowedDocumentNames = ["PublicAPI.Shipped.txt", "PublicAPI.Unshipped.txt", "slow-tests-allowlist.txt"];
    private static readonly string[] AllowedDocumentPaths = ["server/Application/Services/LandVerificationBuildGraph.manifest.txt"];

    // A property function that reads the file system; its first argument must be rooted at an output root.
    private static readonly string[] FileReadingFunctions =
    [
        "ReadAllText", "ReadAllBytes", "ReadAllLines", "ReadLines", "GetFiles", "GetDirectories",
        "GetFileSystemEntries", "EnumerateFiles", "EnumerateDirectories", "EnumerateFileSystemEntries",
    ];

    // CARD-1409 repair 9 (Review 4ecdb753 R3, D-42): MSBuild escapes these characters in $(MSBuildProjectDirectory)
    // and leaves them as written in $(MSBuildThisFileDirectory); the audit does not re-implement that escaping.
    private static readonly string[] MSBuildEscapedCharacters = ["%", "$", "@", ";", "'", "(", ")", "*", "?"];

    private const string PropertyReference = @"\$\(\s*([A-Za-z_][A-Za-z0-9_]*)\s*\)";

    private readonly TextWriter _stdout;
    private readonly TextWriter _stderr;
    private readonly IReadOnlyDictionary<string, string?>? _environment;
    private readonly BlobReader _blobs;

    // CARD-1409 repair 4 (Review 937bb1c9 F2): the census of every parsed definition file, filled before any
    // include is classified: property definitions, task outputs, declared items and package path properties.
    private readonly Dictionary<string, List<PropertyDefinition>> _propertyDefinitions = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<string>> _taskPropertyOutputs = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<string>> _taskItemOutputs = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<ItemDeclaration>> _itemDeclarations = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _packagePathProperties = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _trackedPaths = [];
    private readonly List<string> _symlinkPaths = [];
    private string _unresolvedReference = "";

    public PrecheckAudit(TextWriter stdout, TextWriter stderr, IReadOnlyDictionary<string, string?>? environment)
    {
        _stdout = stdout;
        _stderr = stderr;
        _environment = environment;
        _blobs = new BlobReader(this);
    }

    public void Dispose() => _blobs.Dispose();

    private static Hashtable PinnedProperties()
    {
        var pinned = Ps.NewHashtable();
        pinned["_StagedHelperFramework"] = new[] { "%(StagedHelper.HelperTargetFramework)", "$(TargetFramework)" };
        return pinned;
    }

    private static Dictionary<string, string> Rules()
    {
        var rules = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pathItem in new[]
        {
            "Compile", "EmbeddedResource", "Content", "None", "AdditionalFiles", "EditorConfigFiles",
            "GlobalAnalyzerConfigFiles", "_StagedHelperFiles", "_StagedHelperTarget",
            "CustodyChildOutput", "FakeBrowserFile", "FakeClaudeOutput", "FakeGrokOutput", "LandFakeGrokOutput",
            "StagedHelper", "_CustodyChildTargetPath", "_FakeClaudeTargetPath", "_FakeGrokTargetPath",
            "_LandFakeGrokTarget",
        })
        {
            rules[pathItem] = "path";
        }

        rules["_AntiphonRepeatLine"] = "text";
        rules["ProjectReference"] = "project";
        foreach (var nameItem in new[] { "PackageReference", "FrameworkReference", "InternalsVisibleTo" })
            rules[nameItem] = "name";
        return rules;
    }

    private static Hashtable PinnedMetadata()
    {
        var projectReference = Ps.NewHashtable();
        projectReference["SetTargetFramework"] = "TargetFramework=net10.0";
        projectReference["SkipGetTargetFrameworkProperties"] = "true";
        var stagedHelper = Ps.NewHashtable();
        stagedHelper["HelperTargetFramework"] = "net10.0";
        var embeddedResource = Ps.NewHashtable();
        embeddedResource["WithCulture"] = "false";
        embeddedResource["Culture"] = "";
        var pinned = Ps.NewHashtable();
        pinned["ProjectReference"] = projectReference;
        pinned["StagedHelper"] = stagedHelper;
        pinned["EmbeddedResource"] = embeddedResource;
        return pinned;
    }

    [System.Diagnostics.CodeAnalysis.DoesNotReturn]
    private void ExitAudit(int code, string message)
    {
        if (!string.IsNullOrEmpty(message)) _stderr.WriteLine(message);
        throw new PrecheckExit(code);
    }

    public int Execute(string? repoRoot, bool check, bool update, string? manifestPath)
    {
        if (check && update) ExitAudit(1, "Specify -Check or -Update, not both.");
        if (string.IsNullOrWhiteSpace(repoRoot)) ExitAudit(1, "script root is unknown");
        if (string.IsNullOrEmpty(manifestPath))
            manifestPath = Path.Combine(repoRoot, "server", "Application", "Services", "LandVerificationBuildGraph.manifest.txt");

        var bytes = GetGitBytes(repoRoot, "ls-tree -r -z HEAD");
        if (bytes is null) ExitAudit(2, "git ls-tree failed");

        var utf8 = new UTF8Encoding(false);
        var text = utf8.GetString(bytes);
        const string trailing = "\0";
        if (text.Length == 0 || !text.EndsWith(trailing, StringComparison.Ordinal))
            ExitAudit(1, "ls-tree output is empty or missing its trailing NUL");
        if (text.IndexOf('�') >= 0) ExitAudit(1, "ls-tree output is not valid UTF-8");

        var parts = text.Split('\0');
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var selected = new List<string>();
        var last = parts.Length - 1;
        for (var i = 0; i < last; i++)
        {
            var row = parts[i];
            var tab = row.IndexOf('\t');
            if (tab < 0) ExitAudit(1, "ls-tree row has no tab");
            var meta = row.Substring(0, tab).Split(' ');
            var path = row.Substring(tab + 1);
            if (meta.Length != 3) ExitAudit(1, "ls-tree metadata is not mode type object");
            if (!TestSafePath(path)) ExitAudit(1, "ls-tree path is not safe");
            if (!seen.Add(path)) ExitAudit(1, "duplicate path");
            var mode = meta[0];
            var kind = meta[1];
            var oid = meta[2];
            var blob = Ps.Eq(kind, "blob") && (Ps.Eq(mode, "100644") || Ps.Eq(mode, "100755") || Ps.Eq(mode, "120000"));
            var gitlink = Ps.Eq(kind, "commit") && Ps.Eq(mode, "160000");
            if (!blob && !gitlink) ExitAudit(1, "ls-tree row is not a blob or commit");
            if (!TestFullObjectId(oid)) ExitAudit(1, "ls-tree object id is not a full id");
            // CARD-1409 repair 4: every tracked blob is what an item glob is expanded against.
            if (blob) _trackedPaths.Add(path);
            // CARD-1409 repair 5 (D-30): a tracked symbolic link is recorded so a path item that reaches it refuses.
            if (blob && Ps.Eq(mode, "120000")) _symlinkPaths.Add(path);
            if (!TestDefinitionPath(path)) continue;
            // CARD-1409 repair 5 (D-28, N-15): a definition path is compared like every other path, so an 'obj.'
            // segment the land host would read as obj/ refuses by name instead of slipping past the obj/ rule.
            var definitionCanonical = ConvertToCanonicalPath(path);
            if (definitionCanonical.Refused) ExitAudit(1, FormatPathSegmentRefusal(path, "", definitionCanonical));
            if (TestPathUnderObjDirectory(path)) ExitAudit(1, FormatObjDefinitionRefusal(path));
            // CARD-1409 repair 9 (D-42): every directory the audit splices, and every import or reference target, is a
            // tracked definition path, so one holding a character MSBuild escapes refuses before any file is read.
            var escapedCharacter = GetEscapedDefinitionCharacter(path);
            if (escapedCharacter is not null) ExitAudit(1, FormatEscapedDefinitionPathRefusal(path, escapedCharacter));
            var regular = (Ps.Eq(mode, "100644") || Ps.Eq(mode, "100755")) && Ps.Eq(kind, "blob");
            if (!regular) ExitAudit(1, "definition path is not a regular blob");
            selected.Add(row);
        }

        var arr = selected.ToArray();
        Array.Sort(arr, StringComparer.Ordinal);
        AssertNoDocumentationInput(repoRoot, arr);
        var payload = string.Join(trailing, arr) + trailing;
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        var hex = BitConverter.ToString(hash).Replace("-", "", StringComparison.Ordinal);
        var count = arr.Length;
        _stdout.WriteLine("digest=" + hex + " count=" + count.ToString(CultureInfo.InvariantCulture));

        if (update)
        {
            var body = hex + "\n" + count.ToString(CultureInfo.InvariantCulture) + "\n";
            var manifestDir = Path.GetDirectoryName(manifestPath);
            if (!Directory.Exists(manifestDir)) Directory.CreateDirectory(manifestDir!);
            File.WriteAllText(manifestPath, body, Encoding.ASCII);
            return 0;
        }

        if (!File.Exists(manifestPath)) ExitAudit(1, "manifest missing");
        var lines = File.ReadAllLines(manifestPath);
        if (lines.Length != 2) ExitAudit(1, "manifest must be two lines");
        var manifestDigest = lines[0];
        var manifestCountText = lines[1];
        if (!Ps.CMatch(manifestDigest, "^[0-9A-F]{64}$")) ExitAudit(1, "manifest digest is not 64 uppercase hex characters");
        if (!Ps.Match(manifestCountText, "^[0-9]+$")) ExitAudit(1, "manifest count is not a decimal integer");
        var manifestCount = int.Parse(manifestCountText, CultureInfo.InvariantCulture);
        if (!Ps.CEq(manifestDigest, hex) || manifestCount != count) ExitAudit(1, "manifest does not match the selected graph");
        return 0;
    }

    private static bool TestAllowedName(IEnumerable<string>? allowed, string name, bool ignoreCase)
    {
        if (allowed is null) return false;
        foreach (var candidate in allowed)
        {
            if (ignoreCase)
            {
                if (Ps.Eq(candidate, name)) return true;
            }
            else if (string.Equals(candidate, name, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TestDefinitionPath(string path)
    {
        var name = Path.GetFileName(path);
        var extension = Path.GetExtension(path).ToLowerInvariant();
        if (extension.EndsWith("proj", StringComparison.Ordinal)) return true;
        foreach (var label in new[] { ".props", ".targets", ".sln", ".slnx", ".slnf", ".rsp" })
        {
            if (Ps.Eq(extension, label)) return true;
        }

        if (name.StartsWith("Directory.Build.", StringComparison.OrdinalIgnoreCase)) return true;
        if (TestProjectUserFile(path)) return true;
        var lower = name.ToLowerInvariant();
        foreach (var label in new[] { "global.json", "nuget.config", "packages.lock.json", "packages.config", "directory.packages.props" })
        {
            if (Ps.Eq(lower, label)) return true;
        }

        return false;
    }

    // CARD-1409: Microsoft.Common.targets imports $(MSBuildProjectFullPath).user when it exists.
    private static bool TestProjectUserFile(string path) =>
        Path.GetFileName(path).ToLowerInvariant().EndsWith("proj.user", StringComparison.Ordinal);

    // CARD-1409 (Review 8989ece8 F1): obj/ is where Microsoft.Common.props/.targets import
    // $(MSBuildProjectFile).*.props/.targets from by wildcard; the audit does not follow them.
    private static bool TestPathUnderObjDirectory(string path)
    {
        var segments = path.Replace("\\", "/", StringComparison.Ordinal).Split('/');
        for (var i = 0; i < segments.Length - 1; i++)
        {
            if (string.Equals(segments[i], "obj", StringComparison.OrdinalIgnoreCase)) return true;
        }

        return false;
    }

    // CARD-1409 (Review 8989ece8 F1): an element that sets a redirect of the extension or intermediate
    // directory, wherever it sits; returns the element name as written, or null.
    private static string? GetIntermediateRedirectElement(XmlDocument doc)
    {
        foreach (XmlNode node in doc.GetElementsByTagName("*"))
        {
            var local = Ps.Str(Ps.Get(node, "LocalName"));
            foreach (var name in new[] { "BaseIntermediateOutputPath", "MSBuildProjectExtensionsPath", "IntermediateOutputPath" })
            {
                if (local.Equals(name, StringComparison.OrdinalIgnoreCase)) return local;
            }
        }

        return null;
    }

    private static bool TestProjectXml(string path)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        if (extension.EndsWith("proj", StringComparison.Ordinal)) return true;
        if (Ps.Eq(extension, ".props") || Ps.Eq(extension, ".targets")) return true;
        return TestProjectUserFile(path);
    }

    // CARD-1409 (Review 31c77116 R1): MSBuild reads Directory.Build.rsp as command-line switches; every tracked .rsp is parsed.
    private static bool TestResponseFile(string path) => Ps.Eq(Path.GetExtension(path).ToLowerInvariant(), ".rsp");

    private static bool TestFullObjectId(string value)
    {
        if (value.Length != 40 && value.Length != 64) return false;
        foreach (var c in value)
        {
            var ok = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f');
            if (!ok) return false;
        }

        var zeros = new string('0', value.Length);
        return !Ps.CEq(value, zeros);
    }

    private static bool TestSafePath(string path)
    {
        if (string.IsNullOrEmpty(path)) return false;
        if (Ps.IndexOf(path, "\\") >= 0) return false;
        if (path.IndexOf('�') >= 0) return false;
        foreach (var c in path)
        {
            if (char.IsControl(c)) return false;
        }

        foreach (var component in path.Split('/'))
        {
            if (Ps.Eq(component, "") || Ps.Eq(component, ".") || Ps.Eq(component, "..")) return false;
        }

        return true;
    }

    private static string? ConvertToRepoRelative(string absolute, string root)
    {
        var full = absolute.Replace("\\", "/", StringComparison.Ordinal);
        var prefix = root.Replace("\\", "/", StringComparison.Ordinal).TrimEnd('/');
        var head = prefix + "/";
        if (full.StartsWith(head, StringComparison.OrdinalIgnoreCase)) return full.Substring(head.Length);
        return null;
    }

    private static List<string> NewSegmentList(string pattern)
    {
        var list = new List<string>();
        foreach (var part in pattern.Split('/'))
        {
            if (part.Length > 0) list.Add(part);
        }

        return list;
    }

    private static bool TestSegmentMatch(string glob, string text)
    {
        var body = Regex.Escape(glob).Replace(@"\*", "[^/]*", StringComparison.Ordinal).Replace(@"\?", "[^/]", StringComparison.Ordinal);
        return Regex.IsMatch(text, "^" + body + "$", RegexOptions.IgnoreCase);
    }

    private static bool TestPatternHasWildcard(string pattern) =>
        Ps.IndexOf(pattern, "*") >= 0 || Ps.IndexOf(pattern, "?") >= 0;

    // CARD-1409 repair 4 (Review 937bb1c9 F1): a wildcard pattern can reach docs/ when its first segment is
    // '**' or can match the name 'docs'. What it matches today does not matter: a docs file added later would
    // be a docs-only land, so the pattern itself is refused.
    private static bool TestPatternCanReachDocs(string pattern)
    {
        var segs = NewSegmentList(pattern);
        if (segs.Count == 0) return false;
        var first = segs[0];
        if (Ps.Eq(first, "**")) return true;
        return TestSegmentMatch(first, "docs");
    }

    private static bool TestPathUnderDocs(string path)
    {
        if (string.Equals(path, "docs", StringComparison.OrdinalIgnoreCase)) return true;
        return path.StartsWith("docs/", StringComparison.OrdinalIgnoreCase);
    }

    // A real glob matcher over repository-relative paths: * and ? stay inside a segment, ** spans segments.
    private static Regex GetGlobRegex(string pattern)
    {
        var segs = NewSegmentList(pattern);
        var sb = new StringBuilder();
        sb.Append('^');
        for (var i = 0; i < segs.Count; i++)
        {
            var seg = segs[i];
            var last = i == segs.Count - 1;
            if (Ps.Eq(seg, "**"))
            {
                sb.Append(last ? ".*" : "(?:[^/]+/)*");
                continue;
            }

            var body = Regex.Escape(seg).Replace(@"\*\*", "[^/]*", StringComparison.Ordinal)
                .Replace(@"\*", "[^/]*", StringComparison.Ordinal).Replace(@"\?", "[^/]", StringComparison.Ordinal);
            sb.Append(body);
            if (!last) sb.Append('/');
        }

        sb.Append('$');
        return new Regex(sb.ToString(), RegexOptions.IgnoreCase);
    }

    private static string GetGlobLiteralPrefix(string pattern)
    {
        var cut = pattern.Length;
        var star = Ps.IndexOf(pattern, "*");
        if (star >= 0 && star < cut) cut = star;
        var question = Ps.IndexOf(pattern, "?");
        if (question >= 0 && question < cut) cut = question;
        var prefix = pattern.Substring(0, cut);
        var slash = Ps.LastIndexOf(prefix, "/");
        if (slash < 0) return "";
        return prefix.Substring(0, slash + 1);
    }

    // Every path tracked at HEAD that the pattern selects, minus the Exclude patterns.
    private List<string> GetTrackedMatches(string pattern, IEnumerable<string> excludePatterns)
    {
        var hits = new List<string>();
        var regex = GetGlobRegex(pattern);
        var prefix = GetGlobLiteralPrefix(pattern);
        var excludes = new List<Regex>();
        foreach (var exclude in excludePatterns) excludes.Add(GetGlobRegex(exclude));
        foreach (var tracked in _trackedPaths)
        {
            if (prefix.Length > 0 && !tracked.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
            if (!regex.IsMatch(tracked)) continue;
            var excluded = false;
            foreach (var ex in excludes)
            {
                if (ex.IsMatch(tracked))
                {
                    excluded = true;
                    break;
                }
            }

            if (!excluded) hits.Add(tracked);
        }

        return hits;
    }

    private static bool TestDocumentPath(string path)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        return DocumentExtensions.Any(e => Ps.Eq(e, extension));
    }

    private static bool TestDocumentAllowed(string pattern, string path)
    {
        if (TestAllowedName(AllowedDocumentPatterns, pattern, true)) return true;
        if (TestAllowedName(AllowedDocumentPaths, path, true)) return true;
        var name = Path.GetFileName(path);
        if (TestAllowedName(AllowedDocumentNames, name, true)) return true;
        return string.Equals(name, "README.md", StringComparison.OrdinalIgnoreCase) && Ps.IndexOf(path, "/") >= 0;
    }

    // Walks pattern segments against target segments. With prefix the walk succeeds as soon as every target
    // segment is consumed, whatever pattern remains: the pattern then selects the target itself or something
    // beneath it (CARD-1409 repair 5, D-30).
    private static bool TestGlobWalk(List<string> pattern, int index, List<string> target, int targetIndex, bool prefix)
    {
        while (true)
        {
            if (prefix && targetIndex >= target.Count) return true;
            if (index >= pattern.Count) return targetIndex >= target.Count;
            var seg = pattern[index];
            if (Ps.Eq(seg, "**"))
            {
                if (TestGlobWalk(pattern, index + 1, target, targetIndex, prefix)) return true;
                if (targetIndex >= target.Count) return false;
                targetIndex++;
                continue;
            }

            if (targetIndex >= target.Count) return false;
            if (!TestSegmentMatch(seg, target[targetIndex])) return false;
            index++;
            targetIndex++;
        }
    }

    // CARD-1409 repair 5 (D-30): the first tracked symbolic link the canonical pattern selects or passes through.
    private string? FindSymlinkReached(string pattern)
    {
        if (_symlinkPaths.Count == 0) return null;
        var segs = NewSegmentList(pattern);
        foreach (var link in _symlinkPaths)
        {
            var target = NewSegmentList(link);
            if (TestGlobWalk(segs, 0, target, 0, true)) return link;
        }

        return null;
    }

    private static bool TestPatternMatchesReadme(string pattern)
    {
        var target = new List<string> { "README.md" };
        return TestGlobWalk(NewSegmentList(pattern), 0, target, 0, false);
    }

    // CARD-1409 repair 5 (Review f0fb9035 F1, plan D-28): the one path normaliser. Every path the audit
    // compares goes through here before any comparison: an include or update part, an Exclude part, an
    // import target, a project reference, a literal property-value path, the tail of a path rooted at an
    // output or package root, every tracked match a wildcard selects and every tracked definition path. The
    // text is split on '/' and '\' (equal) and each segment is held to the Windows land host's rules: a
    // trailing '.' or space, a segment of only dots or spaces, a ':' anywhere (the drive prefix is handled
    // by the caller, so it is the only legal colon) and an 8.3 short-name shape ('~' then a digit) each
    // REFUSE naming the segment; nothing is silently rewritten. Only then are '.' and empty segments
    // dropped and '..' popped. Letter case is kept; every comparison of a canonical path is
    // ordinal-ignore-case. Returns Status ok with Path, outside ('..' above the root), escape ('..' after a
    // wildcard segment) or refused with Segment and Reason. With fold (property values only, D-25: a value
    // is free text, not an input) a divergent segment is read as the host reads it instead of refusing:
    // trailing dots and spaces are dropped, a colon cuts the segment to the name before it, and the first
    // divergence is returned as Divergent so a docs or README reading can still be refused by segment; an
    // 8.3 short name refuses even then, because its long name is unknowable.
    private static Canonical ConvertToCanonicalPath(string text, bool fold = false)
    {
        var stack = new List<string>();
        Canonical? divergent = null;
        foreach (var raw in text.Replace("\\", "/", StringComparison.Ordinal).Split('/'))
        {
            var part = raw;
            if (part.Length == 0 || Ps.Eq(part, ".")) continue;
            if (Ps.Eq(part, ".."))
            {
                if (stack.Count == 0) return new Canonical { Status = "outside" };
                var top = stack[^1];
                if (TestPatternHasWildcard(top)) return new Canonical { Status = "escape" };
                stack.RemoveAt(stack.Count - 1);
                continue;
            }

            if (Ps.Match(part, "~[0-9]")) return Refusal(part, "8.3 short name");
            var colon = Ps.IndexOf(part, ":");
            if (colon >= 0)
            {
                if (!fold) return Refusal(part, "colon");
                divergent ??= new Canonical { Segment = part, Reason = "colon" };
                part = part.Substring(0, colon);
            }

            var trimmed = part.TrimEnd('.', ' ');
            if (trimmed.Length != part.Length)
            {
                var reason = "trailing dot or space";
                if (trimmed.Length == 0) reason = "only dots or spaces";
                if (!fold) return Refusal(part, reason);
                divergent ??= new Canonical { Segment = part, Reason = reason };
                part = trimmed;
            }

            if (part.Length == 0) continue;
            stack.Add(part);
        }

        if (stack.Count == 0) return new Canonical { Status = "outside" };
        return new Canonical { Status = "ok", Path = string.Join("/", stack), Divergent = divergent };

        static Canonical Refusal(string segment, string reason) =>
            new() { Status = "refused", Segment = segment, Reason = reason };
    }

    private static string FormatSegmentDetail(Canonical canonical) =>
        "segment=" + canonical.Segment + " (" + canonical.Reason + ")";

    private static string GetRepoDirectory(string path)
    {
        var directory = Path.GetDirectoryName(path.Replace("\\", "/", StringComparison.Ordinal));
        if (string.IsNullOrEmpty(directory)) return "";
        return directory.Replace("\\", "/", StringComparison.Ordinal);
    }

    private static bool TestProjFile(string path) =>
        Path.GetExtension(path).ToLowerInvariant().EndsWith("proj", StringComparison.Ordinal);

    private static int ConvertFromHexByte(string hex)
    {
        if (hex.Length != 2) return -1;
        var n = 0;
        foreach (var c in hex)
        {
            int d = c;
            int v;
            if (d >= 48 && d <= 57) v = d - 48;
            else if (d >= 65 && d <= 70) v = d - 55;
            else if (d >= 97 && d <= 102) v = d - 87;
            else return -1;
            n = (n * 16) + v;
        }

        return n;
    }

    // CARD-1409 repair 6 (Review c64987ea F1, plan D-33): MSBuild's unescape, read from a scratch 'dotnet msbuild
    // -getItem' run. Two hex digits after '%', in either case, become one character of that code (%C3%A9 is two
    // characters); anything else after '%' stays literal; %252E becomes the literal %2E. Returns null for %00,
    // which MSBuild keeps as a NUL character and the audit refuses by name. Called from ResolveItemPathText
    // only, so every path text is decoded exactly once, after expansion and after the ';' split, as MSBuild does.
    private static string? ConvertFromPercentEncoding(string value)
    {
        var sb = new StringBuilder();
        var i = 0;
        while (i < value.Length)
        {
            var c = value[i];
            if (c == '%' && (i + 2) < value.Length)
            {
                var n = ConvertFromHexByte(value.Substring(i + 1, 2));
                if (n >= 0)
                {
                    if (n == 0) return null;
                    sb.Append((char)n);
                    i += 3;
                    continue;
                }
            }

            sb.Append(c);
            i++;
        }

        return sb.ToString();
    }

    private static void AddCensusEntry<T>(Dictionary<string, List<T>> map, string key, T entry)
    {
        if (!map.TryGetValue(key, out var list))
        {
            list = [];
            map[key] = list;
        }

        list.Add(entry);
    }

    private static Hashtable GetItemMetadataValues(object? node)
    {
        var values = Ps.NewHashtable();
        foreach (var attribute in Ps.Each(Ps.Get(node, "Attributes")))
        {
            var attributeName = Ps.Str(Ps.Get(attribute, "Name"));
            if (TestAllowedName(ItemElementAttributes, attributeName, false)) continue;
            values[attributeName] = Ps.Str(Ps.Get(attribute, "Value"));
        }

        foreach (var child in Ps.Each(Ps.Get(node, "ChildNodes")))
        {
            if (IsNotElement(child)) continue;
            values[Ps.Str(Ps.Get(child, "LocalName"))] = Ps.Str(Ps.Get(child, "InnerText")).Trim();
        }

        return values;
    }

    // if ($child.NodeType -ne [System.Xml.XmlNodeType]::Element) { continue }
    private static bool IsNotElement(object? child) => Ps.IsTrue(Ps.NeOp(Ps.Get(child, "NodeType"), XmlNodeType.Element));

    private void RegisterDefinitionFacts(XmlDocument doc, string path)
    {
        foreach (XmlNode group in doc.GetElementsByTagName("PropertyGroup"))
        {
            var groupConditional = !string.IsNullOrWhiteSpace(Ps.Str(Ps.GetAttribute(group, "Condition")));
            foreach (var child in Ps.Each(Ps.Get(group, "ChildNodes")))
            {
                if (IsNotElement(child)) continue;
                var conditional = groupConditional || !string.IsNullOrWhiteSpace(Ps.Str(Ps.GetAttribute(child, "Condition")));
                var name = Ps.Str(Ps.Get(child, "LocalName"));
                AddCensusEntry(_propertyDefinitions, name, new PropertyDefinition(path, Ps.Str(Ps.Get(child, "InnerText")).Trim(), conditional));
            }
        }

        foreach (XmlNode output in doc.GetElementsByTagName("Output"))
        {
            var task = "";
            if (Ps.Get(output, "ParentNode") is not null) task = Ps.Str(Ps.Get(Ps.Get(output, "ParentNode"), "LocalName"));
            var propertyName = Ps.Str(Ps.GetAttribute(output, "PropertyName")).Trim();
            if (propertyName.Length > 0) AddCensusEntry(_taskPropertyOutputs, propertyName, task);
            var itemName = Ps.Str(Ps.GetAttribute(output, "ItemName")).Trim();
            if (itemName.Length > 0) AddCensusEntry(_taskItemOutputs, itemName, task);
        }

        foreach (XmlNode group in doc.GetElementsByTagName("ItemGroup"))
        {
            foreach (var node in Ps.Each(Ps.Get(group, "ChildNodes")))
            {
                if (IsNotElement(node)) continue;
                // CARD-1409 repair 5 (Review f0fb9035 F2): an Update sets metadata on the items it names, static
                // or inside a Target, so its values are declared values too; Remove sets nothing.
                var include = Ps.Str(Ps.GetAttribute(node, "Include"));
                if (string.IsNullOrWhiteSpace(include)) include = Ps.Str(Ps.GetAttribute(node, "Update"));
                if (string.IsNullOrWhiteSpace(include)) continue;
                var metadata = GetItemMetadataValues(node);
                AddCensusEntry(_itemDeclarations, Ps.Str(Ps.Get(node, "LocalName")), new ItemDeclaration(path, include, metadata));
                if (Ps.Eq(Ps.Str(Ps.Get(node, "LocalName")), "PackageReference"))
                {
                    var generate = "";
                    if (metadata.ContainsKey("GeneratePathProperty")) generate = Ps.Str(metadata["GeneratePathProperty"]);
                    if (Ps.Eq(generate, "true"))
                    {
                        // D-33: the id is item text, split and decoded once by the pipeline before it names a property.
                        foreach (var id in ResolveItemPathText(include, "", "", true)!)
                        {
                            if (id.Kind != "repo" || id.Anchored) continue;
                            _packagePathProperties.Add("Pkg" + id.Text.Replace(".", "_", StringComparison.Ordinal));
                        }
                    }
                }
            }
        }
    }

    // True when every task Output that fills the name comes from the MSBuild task, whose TargetOutputs are
    // other projects' build products. Any other task (Exec, ReadLinesFromFile) is not statically resolvable.
    private static bool TestTaskOutputOnly(Dictionary<string, List<string>> map, string name)
    {
        if (!map.TryGetValue(name, out var tasks)) return false;
        foreach (var task in tasks)
        {
            if (!string.Equals(task, "MSBuild", StringComparison.Ordinal)) return false;
        }

        return true;
    }

    private List<PathText>? SetUnresolved(string reference)
    {
        _unresolvedReference = reference;
        return null;
    }

    private static string GetFileDirectoryPrefix(string path)
    {
        var directory = GetRepoDirectory(path);
        if (string.IsNullOrEmpty(directory)) return "./";
        return directory + "/";
    }

    // Expands one text (an include or a property value) into every value it can take: candidates tagged repo
    // (a path inside the tree), output (rooted at a build output or intermediate directory) or package (rooted
    // at a restored package folder). The text and every spliced value stay in their escaped form; the pipeline
    // (ResolveItemPathText, D-33) splits and decodes afterwards. fileDir is what $(MSBuildThisFileDirectory) means in
    // the file holding the text. Returns null, with _unresolvedReference set, when a reference cannot be
    // resolved statically: a property no tracked file defines, a non-MSBuild task output, a property function
    // other than GetDirectoryName over an MSBuild task output, an item transform other than
    // @(MSBuildOutputItem->'%(RootDir)%(Directory)'), %(Item.Metadata) of an undeclared item, or a rooted
    // reference that is not the first token.
    private List<PathText>? ExpandCandidates(string text, string fileDir, string projectDir, int depth)
    {
        var result = new List<PathText>();
        if (depth > 8) return SetUnresolved(text);
        var transform = Regex.Match(text, @"^@\(\s*([A-Za-z_][A-Za-z0-9_]*)\s*->\s*'%\(RootDir\)%\(Directory\)'\s*\)$");
        if (transform.Success)
        {
            var itemName = transform.Groups[1].Value;
            if (TestTaskOutputOnly(_taskItemOutputs, itemName) && !_itemDeclarations.ContainsKey(itemName))
            {
                result.Add(new PathText("output", "", false));
                return result;
            }

            return SetUnresolved(text);
        }

        var batch = Regex.Match(text, @"^%\(\s*([A-Za-z_][A-Za-z0-9_]*)\.([A-Za-z_][A-Za-z0-9_]*)\s*\)$");
        if (batch.Success)
        {
            var itemName = batch.Groups[1].Value;
            var metadataName = batch.Groups[2].Value;
            if (!_itemDeclarations.TryGetValue(itemName, out var declarations)) return SetUnresolved(text);
            foreach (var declaration in declarations)
            {
                var value = "";
                if (declaration.Metadata.ContainsKey(metadataName)) value = Ps.Str(declaration.Metadata[metadataName]);
                var inner = ExpandCandidates(value, GetFileDirectoryPrefix(declaration.File), projectDir, depth + 1);
                if (inner is null) return null;
                result.AddRange(inner);
            }

            return result;
        }

        if (Ps.IndexOf(text, "@(") >= 0 || Ps.IndexOf(text, "%(") >= 0) return SetUnresolved(text);
        var directoryOf = Regex.Match(text, @"^\$\(\[System\.IO\.Path\]::GetDirectoryName\('\$\(\s*([A-Za-z_][A-Za-z0-9_]*)\s*\)'\)\)$");
        if (directoryOf.Success)
        {
            var inner = ExpandCandidates("$(" + directoryOf.Groups[1].Value + ")", fileDir, projectDir, depth + 1);
            if (inner is null) return null;
            foreach (var candidate in inner)
            {
                if (candidate.Kind != "output") return SetUnresolved(text);
                result.Add(new PathText("output", "", false));
            }

            return result;
        }

        if (Ps.IndexOf(text, "$([") >= 0) return SetUnresolved(text);
        var tokens = new List<(string? Literal, string? Property)>();
        var position = 0;
        foreach (Match m in Regex.Matches(text, PropertyReference))
        {
            if (m.Index > position) tokens.Add((text.Substring(position, m.Index - position), null));
            tokens.Add((null, m.Groups[1].Value));
            position = m.Index + m.Length;
        }

        if (position < text.Length) tokens.Add((text.Substring(position), null));
        foreach (var token in tokens)
        {
            if (token.Literal is not null && Ps.IndexOf(token.Literal, "$(") >= 0) return SetUnresolved(text);
        }

        result.Add(new PathText("repo", "", false));
        var first = true;
        foreach (var token in tokens)
        {
            if (token.Literal is not null)
            {
                foreach (var candidate in result) candidate.Text += token.Literal;
                first = false;
                continue;
            }

            var name = token.Property!;
            var values = GetPropertyCandidates(name, fileDir, projectDir, depth);
            if (values is null) return null;
            var next = new List<PathText>();
            foreach (var candidate in result)
            {
                foreach (var value in values)
                {
                    var rooted = value.Kind != "repo" || value.Anchored;
                    if (rooted && !first) return SetUnresolved("$(" + name + ")");
                    if (rooted) next.Add(new PathText(value.Kind, value.Text, value.Anchored));
                    else next.Add(new PathText(candidate.Kind, candidate.Text + value.Text, candidate.Anchored));
                }
            }

            if (next.Count > 64) return SetUnresolved("$(" + name + ")");
            result = next;
            first = false;
        }

        return result;
    }

    // The values one $(Name) can take: the two directory properties, an output root, a package path property,
    // an MSBuild task output, or the union of every PropertyGroup definition across the tracked files (each
    // expanded where it is defined), plus the empty value when a definition is conditional.
    private List<PathText>? GetPropertyCandidates(string name, string fileDir, string projectDir, int depth)
    {
        var values = new List<PathText>();
        if (Ps.Eq(name, "MSBuildThisFileDirectory"))
        {
            values.Add(new PathText("repo", fileDir, true));
            return values;
        }

        if (Ps.Eq(name, "MSBuildProjectDirectory"))
        {
            var projectText = projectDir;
            if (string.IsNullOrEmpty(projectText)) projectText = ".";
            values.Add(new PathText("repo", projectText, true));
            return values;
        }

        if (TestAllowedName(OutputRootProperties, name, true))
        {
            values.Add(new PathText("output", "", false));
            return values;
        }

        if (_packagePathProperties.Contains(name))
        {
            values.Add(new PathText("package", "", false));
            return values;
        }

        var defined = _propertyDefinitions.ContainsKey(name);
        if (_taskPropertyOutputs.ContainsKey(name))
        {
            if (defined || !TestTaskOutputOnly(_taskPropertyOutputs, name)) return SetUnresolved("$(" + name + ")");
            values.Add(new PathText("output", "", false));
            return values;
        }

        if (!defined) return SetUnresolved("$(" + name + ")");
        var conditional = false;
        foreach (var definition in _propertyDefinitions[name])
        {
            if (definition.Conditional) conditional = true;
            var inner = ExpandCandidates(definition.Value, GetFileDirectoryPrefix(definition.File), projectDir, depth + 1);
            if (inner is null) return null;
            values.AddRange(inner);
        }

        if (conditional) values.Add(new PathText("repo", "", false));
        return values;
    }

    // CARD-1409 repair 6 (Review c64987ea F1, plan D-33): the one pipeline that turns item or import text into
    // path specs, in MSBuild's order, which a scratch 'dotnet msbuild -getItem' run confirmed: expand properties
    // and metadata (values are spliced in their escaped form, as MSBuild stores them), split the expanded text
    // on ';' (an escaped %3B is not a separator; a ';' inside an expanded value is), decode %XX once per part,
    // and only then compare through ConvertToCanonicalPath. A decoded '$(' '@(' '%(' is literal text, never a
    // reference. A root (a directory property, an output or package root) belongs to the first part of its
    // expanded text; later parts are ordinary project-relative paths. With directoryPropertiesOnly the
    // expansion knows $(MSBuildThisFileDirectory) and $(MSBuildProjectDirectory) at the start of each part and
    // nothing else (imports, D-1; literal property values, D-25; name items; Pkg* ids): a part that still carries
    // a reference is returned with Kind unresolved for the consumer to refuse or skip. Returns null with
    // _unresolvedReference set when the full expansion cannot resolve a reference; otherwise a list of
    // specs with Kind (repo, output, package, unresolved, or nul for a part that decodes to a NUL character),
    // Text (decoded), Anchored and Source (the escaped part, for messages).
    private List<PathText>? ResolveItemPathText(string text, string fileDir, string projectDir, bool directoryPropertiesOnly)
    {
        List<PathText> candidates;
        var fileText = "";
        var projectText = "";
        if (directoryPropertiesOnly)
        {
            // CARD-1409 repair 8 (Review d43d1e4a R2, D-39): the directory values carry no ';', so splitting first
            // and expanding a directory property at the start of each part below is MSBuild's expand-then-split.
            candidates = [new PathText("repo", text, false)];
            fileText = string.IsNullOrEmpty(fileDir) ? "./" : fileDir;
            projectText = string.IsNullOrEmpty(projectDir) ? "." : projectDir;
        }
        else
        {
            var expanded = ExpandCandidates(text, fileDir, projectDir, 0);
            if (expanded is null) return null;
            candidates = expanded;
        }

        var specs = new List<PathText>();
        foreach (var candidate in candidates)
        {
            var parts = candidate.Text.Split(';');
            for (var i = 0; i < parts.Length; i++)
            {
                var part = parts[i].Trim();
                if (part.Length == 0) continue;
                var kind = "repo";
                var anchoredPart = false;
                if (i == 0)
                {
                    kind = candidate.Kind;
                    anchoredPart = candidate.Anchored;
                }

                if (directoryPropertiesOnly)
                {
                    var directory = Regex.Match(part, @"^(?i)\$\(\s*MSBuild(ThisFile|Project)Directory\s*\)");
                    if (directory.Success)
                    {
                        var root = fileText;
                        if (Ps.Eq(directory.Groups[1].Value, "Project")) root = projectText;
                        part = root + part.Substring(directory.Length);
                        anchoredPart = true;
                    }

                    if (Ps.IndexOf(part, "$(") >= 0 || Ps.IndexOf(part, "@(") >= 0 || Ps.IndexOf(part, "%(") >= 0)
                        kind = "unresolved";
                }

                var decoded = ConvertFromPercentEncoding(part);
                if (decoded is null)
                {
                    kind = "nul";
                    decoded = part;
                }

                specs.Add(new PathText(kind, decoded, anchoredPart, part));
            }
        }

        return specs;
    }

    private static int MeasureWildcardCharacters(string text)
    {
        var n = 0;
        foreach (var c in text)
        {
            if (c == '*' || c == '?') n++;
        }

        return n;
    }

    // CARD-1409 repair 4 and 6: the tail of a path rooted at an output or package root (an include's text after
    // the root, or a file-reading function's argument after it) is a build product only while it stays under
    // the root: a drive letter or a '..' segment is unclassified, and a divergent segment (D-28) refuses naming
    // it. tail is decoded text. Returns null, or a problem 'unclassified' or 'segment' (with Canonical).
    private static TailProblem? GetOutputTailProblem(string tail)
    {
        var text = tail.Replace("\\", "/", StringComparison.Ordinal);
        if (Ps.Match(text, "^[A-Za-z]:")) return new TailProblem("unclassified", null);
        foreach (var part in text.Split('/'))
        {
            if (Ps.Eq(part, "..")) return new TailProblem("unclassified", null);
        }

        var canonical = ConvertToCanonicalPath(text);
        if (canonical.Refused) return new TailProblem("segment", canonical);
        return null;
    }

    // A property a tracked definition file may set or fill: allow-listed (D-13), defined in the census, one of the
    // two directory properties, an output or package root, or a task output. Any other name is the SDK's own.
    private bool TestPropertyIsTrackable(string name)
    {
        if (Ps.Eq(name, "MSBuildThisFileDirectory") || Ps.Eq(name, "MSBuildProjectDirectory")) return true;
        if (AllowedProperties.Contains(name)) return true;
        if (_propertyDefinitions.ContainsKey(name)) return true;
        if (TestAllowedName(OutputRootProperties, name, true)) return true;
        if (_packagePathProperties.Contains(name)) return true;
        return _taskPropertyOutputs.ContainsKey(name);
    }

    // CARD-1409 repair 6 (D-33): in the tail of a file-reading function argument, a $(Name) no tracked definition
    // file may set, such as the SDK's $(CleanFile) in the real Directory.Build.targets, is replaced by one opaque
    // segment so the pipeline expands what the tracked files do define and refuses what it cannot resolve.
    private string ConvertSdkOwnedReferences(string text)
    {
        var sb = new StringBuilder();
        var position = 0;
        foreach (Match m in Regex.Matches(text, PropertyReference))
        {
            sb.Append(text, position, m.Index - position);
            sb.Append(TestPropertyIsTrackable(m.Groups[1].Value) ? m.Value : "sdk-owned");
            position = m.Index + m.Length;
        }

        sb.Append(text.Substring(position));
        return sb.ToString();
    }

    // The tail of a file-reading function argument after its output root, through the pipeline: a reference the
    // audit cannot resolve, a NUL escape, a second root, a drive letter, a '..' segment or a divergent segment
    // is a problem. Each ';'-piece is held to the rule. Returns null or the detail text for the message.
    private string? GetExpandedTailProblem(string tail, string fileDir, string projectDir)
    {
        if (tail.Length == 0) return null;
        var specs = ResolveItemPathText(tail, fileDir, projectDir, false);
        if (specs is null) return "cannot resolve " + _unresolvedReference;
        foreach (var spec in specs)
        {
            if (spec.Kind == "nul") return "decodes to a NUL character";
            if (spec.Kind != "repo" || spec.Anchored) return "has a second root";
            var problem = GetOutputTailProblem(spec.Text);
            if (problem is null) continue;
            if (problem.Problem == "segment") return FormatSegmentDetail(problem.Canonical!);
            return "climbs out of the output root";
        }

        return null;
    }

    // Resolves one repo candidate, evaluated from projectDirectory, to a canonical result (D-28): Status ok
    // with the repository-relative pattern, outside, escape, or refused with the segment. A drive-letter or
    // rooted path is converted to a repository path first, so the drive colon is the only colon that may pass.
    // An anchored candidate already starts at a known directory.
    private static Canonical ResolveCandidatePattern(string root, PathText candidate, string projectDirectory, bool fold = false)
    {
        var text = candidate.Text.Replace("\\", "/", StringComparison.Ordinal);
        if (Ps.Match(text, "^[A-Za-z]:/") || Ps.StartsWith(text, "/"))
        {
            var relative = ConvertToRepoRelative(text, root);
            if (relative is null) return new Canonical { Status = "outside" };
            text = relative;
        }
        else if (candidate.Anchored)
        {
            if (Ps.StartsWith(text, "./")) text = text.Substring(2);
        }
        else if (!string.IsNullOrEmpty(projectDirectory))
        {
            text = projectDirectory + "/" + text;
        }

        return ConvertToCanonicalPath(text, fold);
    }

    private static string? FindNearestDirectoryBuild(Hashtable known, string projectDirectory, string fileName)
    {
        var dir = projectDirectory;
        while (true)
        {
            var candidate = string.IsNullOrEmpty(dir) ? fileName : dir + "/" + fileName;
            if (known.ContainsKey(candidate)) return candidate;
            if (string.IsNullOrEmpty(dir)) return null;
            var slash = Ps.LastIndexOf(dir, "/");
            dir = slash < 0 ? "" : dir.Substring(0, slash);
        }
    }

    // CARD-1409: resolves an Import Project attribute through the pipeline (D-7, D-33: the two directory
    // properties at the start, the ';' split after that expansion, one decode) to the tracked, parsed project
    // paths it names, or refuses naming the whole attribute text and the resolved part.
    private List<string> ResolveImportTargets(string root, string fromFile, string projectDirectory, string projectAttr, Hashtable known)
    {
        var targets = new List<string>();
        var fromDir = GetRepoDirectory(fromFile);
        var specs = ResolveItemPathText(projectAttr, GetFileDirectoryPrefix(fromFile), projectDirectory, true)!;
        if (specs.Count == 0) ExitAudit(1, FormatMissingImportRefusal(fromFile, projectAttr, "(empty)"));
        foreach (var spec in specs)
        {
            var kind = spec.Kind;
            if (kind == "unresolved" || kind == "nul") ExitAudit(1, FormatUnresolvedImportRefusal(fromFile, projectAttr));
            if (TestPatternHasWildcard(spec.Text)) ExitAudit(1, FormatWildcardImportRefusal(fromFile, projectAttr));
            var canonical = ResolveCandidatePattern(root, spec, fromDir, false);
            if (canonical.Refused) ExitAudit(1, FormatPathSegmentRefusal(fromFile, "Import Project=" + projectAttr, canonical));
            if (canonical.Status != "ok") ExitAudit(1, FormatMissingImportRefusal(fromFile, projectAttr, "outside the repository"));
            var norm = canonical.Path;
            if (!known.ContainsKey(norm)) ExitAudit(1, FormatMissingImportRefusal(fromFile, projectAttr, norm));
            targets.Add(norm);
        }

        return targets;
    }

    private static void AddImporterBase(Hashtable map, string path, string projectDirectory)
    {
        if (!map.ContainsKey(path)) map[path] = new List<string>();
        var list = (List<string>)map[path]!;
        if (!list.Contains(projectDirectory)) list.Add(projectDirectory);
    }

    private void AddImportQueue(XmlDocument doc, string fromFile, string projectDirectory, Queue<string> queue, Hashtable known, string root)
    {
        foreach (XmlNode node in doc.GetElementsByTagName("*"))
        {
            if (Ps.IsTrue(Ps.NeOp(Ps.Get(node, "LocalName"), "Import"))) continue;
            var project = Ps.GetAttribute(node, "Project");
            foreach (var attribute in Ps.Each(Ps.Get(node, "Attributes")))
            {
                var attributeName = Ps.Str(Ps.Get(attribute, "Name"));
                var supported = false;
                foreach (var allowed in new[] { "Project", "Condition", "Label" })
                {
                    if (attributeName.Equals(allowed, StringComparison.Ordinal)) supported = true;
                }

                if (!supported)
                    ExitAudit(1, FormatUnsupportedImportAttributeRefusal(fromFile, Ps.Str(project), attributeName, Ps.Str(Ps.Get(attribute, "Value"))));
            }

            // CARD-1409: MSBuild splits Project on ';' into several imports, after expansion (D-7, D-33); the pipeline
            // inside ResolveImportTargets does that split, and every non-empty part is audited as its own import.
            foreach (var target in ResolveImportTargets(root, fromFile, projectDirectory, Ps.Str(project), known))
                queue.Enqueue(target);
        }
    }

    private Hashtable GetImportedProjectDirectories(string root, Hashtable known)
    {
        var bases = Ps.NewHashtable();
        foreach (var key in known.Keys.Cast<object>().ToArray())
        {
            var path = Ps.Str(key);
            if (!TestProjFile(path)) continue;
            var projectDir = GetRepoDirectory(path);
            var queue = new Queue<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            // CARD-1409: Microsoft.Common.props imports the nearest Directory.Packages.props (Review 23aedc52 D3).
            foreach (var name in new[] { "Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props" })
            {
                var hit = FindNearestDirectoryBuild(known, projectDir, name);
                if (hit is not null) queue.Enqueue(hit);
            }

            // CARD-1409: Microsoft.Common.targets imports a tracked <project>.user (Review 23aedc52 D4).
            var userFile = path + ".user";
            if (known.ContainsKey(userFile)) queue.Enqueue(userFile);
            AddImportQueue((XmlDocument)known[path]!, path, projectDir, queue, known, root);
            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                if (!seen.Add(current)) continue;
                AddImporterBase(bases, current, projectDir);
                if (known.ContainsKey(current))
                    AddImportQueue((XmlDocument)known[current]!, current, projectDir, queue, known, root);
            }
        }

        return bases;
    }

    private static string FormatApprovalRefusal(string path, string item, string include, string? resolved = null)
    {
        var text = "definition file includes docs markdown or the root README.md; -Update cannot approve it: " + path + " " + item + " Include=" + include;
        if (!string.IsNullOrEmpty(resolved)) text = text + " -> " + resolved;
        return text;
    }

    private static string FormatDocumentRefusal(string path, string item, string include, string matched) =>
        "definition file item selects a markdown or text document the audit does not allow; -Update cannot approve it: " + path + " " + item + " Include=" + include + " -> " + matched;

    private static string FormatUnresolvedPropertyRefusal(string path, string item, string include, string reference) =>
        "definition file item include uses a property the audit cannot resolve; -Update cannot approve it: " + path + " " + item + " Include=" + include + " " + reference;

    private static string FormatMetadataRefusal(string path, string item, string label, string name) =>
        "definition file item carries metadata the audit does not allow; -Update cannot approve it: " + path + " " + item + " " + label + " " + name;

    private static string FormatPropertyValueRefusal(string path, string name, string detail) =>
        "definition file property value names a docs path or reads a file the audit cannot classify; -Update cannot approve it: " + path + " " + name + " " + detail;

    private static string FormatUnresolvedImportRefusal(string path, string projectAttr) =>
        "definition file import cannot be resolved; -Update cannot approve it: " + path + " Import Project=" + projectAttr;

    private static string FormatUnsupportedImportAttributeRefusal(string path, string projectAttr, string name, string value) =>
        "definition file import carries an attribute the audit does not support; -Update cannot approve it: " + path + " Import Project=" + projectAttr + " " + name + "=" + value;

    private static string FormatMissingImportRefusal(string path, string projectAttr, string resolved) =>
        "definition file import target is not a tracked project file; -Update cannot approve it: " + path + " Import Project=" + projectAttr + " -> " + resolved;

    private static string FormatWildcardImportRefusal(string path, string projectAttr) =>
        "definition file import uses a wildcard the audit does not follow; -Update cannot approve it: " + path + " Import Project=" + projectAttr;

    private static string? GetEscapedDefinitionCharacter(string path)
    {
        foreach (var c in MSBuildEscapedCharacters)
        {
            if (path.IndexOf(c, StringComparison.Ordinal) >= 0) return c;
        }

        return null;
    }

    private static string FormatEscapedDefinitionPathRefusal(string path, string character) =>
        "definition file path holds a character MSBuild escapes in $(MSBuildProjectDirectory), so the audit cannot read a directory spliced from it; -Update cannot approve it: " + path + " char=" + character;

    private static string FormatObjDefinitionRefusal(string path) =>
        "definition file is tracked under an obj/ directory that MSBuild imports by wildcard; -Update cannot approve it: " + path;

    private static string FormatIntermediateRedirectRefusal(string path, string name) =>
        "definition file redirects the intermediate directory, which the audit cannot evaluate; -Update cannot approve it: " + path + " " + name;

    private static string FormatPropertyRefusal(string path, string name) =>
        "definition file sets a property the audit does not allow; -Update cannot approve it: " + path + " " + name;

    private static string FormatPinnedPropertyRefusal(string path, string name) =>
        "definition file sets a pinned property to a value the audit does not allow; -Update cannot approve it: " + path + " " + name;

    private static string FormatItemTypeRefusal(string path, string item) =>
        "definition file declares an item type the audit does not allow; -Update cannot approve it: " + path + " " + item;

    private static string FormatUnclassifiedIncludeRefusal(string path, string item, string include) =>
        "definition file item include cannot be classified; -Update cannot approve it: " + path + " " + item + " Include=" + include;

    private static string FormatProjectReferenceRefusal(string path, string item, string include, string resolved) =>
        "definition file project reference is not a tracked project file; -Update cannot approve it: " + path + " " + item + " Include=" + include + " -> " + resolved;

    // CARD-1409 repair 5 (D-28): detail is the construct the path came from ('EmbeddedResource Include=...',
    // 'Import Project=...', 'Content Include=... -> <match>', or '' for a tracked definition path).
    private static string FormatPathSegmentRefusal(string path, string detail, Canonical canonical)
    {
        var text = "definition file path has a segment the Windows land host would normalise; -Update cannot approve it: " + path;
        if (!string.IsNullOrEmpty(detail)) text = text + " " + detail;
        return text + " " + FormatSegmentDetail(canonical);
    }

    private static string FormatItemOperationRefusal(string path, string item, string label, string operation) =>
        "definition file item uses an operation the audit does not evaluate; -Update cannot approve it: " + path + " " + item + " " + label + " " + operation;

    private static string FormatSymlinkRefusal(string path, string item, string include, string link) =>
        "definition file item path reaches a tracked symbolic link the audit cannot follow; -Update cannot approve it: " + path + " " + item + " Include=" + include + " -> " + link;

    private static string FormatElementRefusal(string path, string element) =>
        "definition file contains an element the audit does not classify; -Update cannot approve it: " + path + " " + element;

    private static string FormatSdkRefusal(string path, string sdk) =>
        "definition file uses an SDK the audit does not allow; -Update cannot approve it: " + path + " Sdk=" + sdk;

    private static string FormatResponseFileRefusal(string path, string line) =>
        "response file switch the audit does not allow; -Update cannot approve it: " + path + " " + line;

    // CARD-1409 (Review 31c77116): the root must be <Project> with an allow-listed Sdk attribute, and its
    // children must be PropertyGroup, ItemGroup, Import, Target or an allow-listed <Sdk> element.
    private void AssertClassifiedStructure(XmlDocument doc, string path)
    {
        var root = Ps.Get(doc, "DocumentElement");
        if (root is null) ExitAudit(1, FormatElementRefusal(path, "(none)"));
        if (!string.Equals(Ps.Str(Ps.Get(root, "LocalName")), "Project", StringComparison.Ordinal))
            ExitAudit(1, FormatElementRefusal(path, Ps.Str(Ps.Get(root, "LocalName"))));
        var sdkAttribute = Ps.Str(Ps.GetAttribute(root, "Sdk"));
        if (!string.IsNullOrWhiteSpace(sdkAttribute))
        {
            foreach (var part in sdkAttribute.Split(';'))
            {
                var name = part.Trim();
                if (name.Length == 0) continue;
                if (!TestAllowedName(AllowedProjectSdks, name, true)) ExitAudit(1, FormatSdkRefusal(path, sdkAttribute));
            }
        }

        foreach (var child in Ps.Each(Ps.Get(root, "ChildNodes")))
        {
            if (IsNotElement(child)) continue;
            var local = Ps.Str(Ps.Get(child, "LocalName"));
            if (!TestAllowedName(AllowedProjectChildren, local, false)) ExitAudit(1, FormatElementRefusal(path, local));
            if (Ps.Eq(local, "Sdk"))
            {
                var sdkName = Ps.Str(Ps.GetAttribute(child, "Name")).Trim();
                if (!TestAllowedName(AllowedSdkElements, sdkName, true)) ExitAudit(1, FormatSdkRefusal(path, sdkName));
            }
        }
    }

    // CARD-1409 (Review 31c77116 R2): every PropertyGroup child, wherever the group sits, and every task
    // Output PropertyName must be an allow-listed property; an Output ItemName must be an allow-listed item type.
    private void AssertAllowedProperties(XmlDocument doc, string path)
    {
        foreach (XmlNode group in doc.GetElementsByTagName("PropertyGroup"))
        {
            foreach (var child in Ps.Each(Ps.Get(group, "ChildNodes")))
            {
                if (IsNotElement(child)) continue;
                var name = Ps.Str(Ps.Get(child, "LocalName"));
                if (!AllowedProperties.Contains(name)) ExitAudit(1, FormatPropertyRefusal(path, name));
                if (PinnedPropertyValues.ContainsKey(name) &&
                    !TestAllowedName((string[])PinnedPropertyValues[name]!, Ps.Str(Ps.Get(child, "InnerXml")), false))
                {
                    ExitAudit(1, FormatPinnedPropertyRefusal(path, name));
                }
            }
        }

        foreach (XmlNode output in doc.GetElementsByTagName("Output"))
        {
            var propertyName = Ps.Str(Ps.GetAttribute(output, "PropertyName")).Trim();
            if (propertyName.Length > 0 && !AllowedProperties.Contains(propertyName))
                ExitAudit(1, FormatPropertyRefusal(path, propertyName));
            if (PinnedPropertyValues.ContainsKey(propertyName)) ExitAudit(1, FormatPinnedPropertyRefusal(path, propertyName));
            var itemName = Ps.Str(Ps.GetAttribute(output, "ItemName")).Trim();
            if (itemName.Length > 0 && !ItemRules.ContainsKey(itemName)) ExitAudit(1, FormatItemTypeRefusal(path, itemName));
        }
    }

    // CARD-1409 (Review 31c77116 R3): every ItemGroup child, wherever the group sits, must be an allow-listed item type.
    private static bool TestReviewedSdkRemoval(object? node, string path, string name)
    {
        if (!Ps.CEq(path, "tests/Shared/FakeProviderOutput.targets")) return false;
        var target = Ps.Get(Ps.Get(node, "ParentNode"), "ParentNode");
        if (Ps.IsTrue(Ps.NeOp(Ps.Get(target, "LocalName"), "Target", ignoreCase: false)) ||
            Ps.IsTrue(Ps.NeOp(Ps.GetAttribute(target, "Name"), "ExcludeRootFakeProviderAppHosts", ignoreCase: false)) ||
            Ps.IsTrue(Ps.NeOp(Ps.Get(target, "ParentNode"), Ps.Get(Ps.Get(node, "OwnerDocument"), "DocumentElement"))))
        {
            return false;
        }

        var condition = "'%(" + name + ".TargetPath)' == 'fakeclaude' or '%(" + name + ".TargetPath)' == 'fakegrok'";
        return Ps.IsTrue(Ps.EqOp(Ps.Get(Ps.Get(node, "Attributes"), "Count"), 2)) &&
            !Ps.IsTrue(Ps.Get(node, "HasChildNodes")) &&
            Ps.IsTrue(Ps.EqOp(Ps.GetAttribute(node, "Remove"), "@(" + name + ")", ignoreCase: false)) &&
            Ps.IsTrue(Ps.EqOp(Ps.GetAttribute(node, "Condition"), condition, ignoreCase: false));
    }

    private void AssertAllowedItemTypes(XmlDocument doc, string path)
    {
        foreach (XmlNode group in doc.GetElementsByTagName("ItemGroup"))
        {
            foreach (var child in Ps.Each(Ps.Get(group, "ChildNodes")))
            {
                if (IsNotElement(child)) continue;
                var name = Ps.Str(Ps.Get(child, "LocalName"));
                if (TestAllowedName(SdkRemovalItems, name, false))
                {
                    if (!TestReviewedSdkRemoval(child, path, name))
                        ExitAudit(1, "unreviewed SDK item removal; -Update cannot approve it: " + path + " " + name);
                    continue;
                }

                if (!ItemRules.ContainsKey(name)) ExitAudit(1, FormatItemTypeRefusal(path, name));
            }
        }
    }

    // A package-style name is a bare identifier: after the pipeline's one decode (D-33) it carries no path
    // separator, reference text, wildcard, parent segment or drive.
    private static bool TestItemNameIsPathLike(string decoded)
    {
        foreach (var needle in new[] { "/", "\\", "$(", "*", "?", "..", ":" })
        {
            if (Ps.IndexOf(decoded, needle) >= 0) return true;
        }

        return false;
    }

    // CARD-1409 repair 4 (Review 937bb1c9 F3) and repair 5 (plan D-29, O-table): an item element must carry
    // Include, Update or Remove; a named operation the audit does not evaluate (KeepMetadata, RemoveMetadata,
    // KeepDuplicates, MatchOnMetadata, MatchOnMetadataOptions) refuses by name; every other attribute that is
    // not an item-element attribute, and every child element, is metadata: it must be allow-listed for the
    // item type, and a Remove element may carry none.
    private void AssertItemOperationsAllowed(object? node, string path, string item, string label)
    {
        var hasOperation = false;
        foreach (var name in new[] { "Include", "Update", "Remove" })
        {
            if (!string.IsNullOrWhiteSpace(Ps.Str(Ps.GetAttribute(node, name)))) hasOperation = true;
        }

        if (!hasOperation) ExitAudit(1, FormatItemOperationRefusal(path, item, label, "(none)"));
        var isRemove = string.IsNullOrWhiteSpace(Ps.Str(Ps.GetAttribute(node, "Include"))) &&
            string.IsNullOrWhiteSpace(Ps.Str(Ps.GetAttribute(node, "Update")));
        string[] allowed = [];
        if (ItemMetadata.TryGetValue(item, out var itemAllowed) && !isRemove) allowed = itemAllowed;
        foreach (var attribute in Ps.Each(Ps.Get(node, "Attributes")))
        {
            var attributeName = Ps.Str(Ps.Get(attribute, "Name"));
            if (TestAllowedName(ItemElementAttributes, attributeName, false)) continue;
            if (TestAllowedName(RefusedItemOperations, attributeName, true))
                ExitAudit(1, FormatItemOperationRefusal(path, item, label, attributeName));
            if (!isRemove && PinnedItemMetadata.ContainsKey(item) &&
                ((Hashtable)PinnedItemMetadata[item]!).ContainsKey(attributeName) &&
                string.Equals(Ps.Str(((Hashtable)PinnedItemMetadata[item]!)[attributeName]), Ps.Str(Ps.Get(attribute, "Value")), StringComparison.Ordinal))
            {
                continue;
            }

            if (!TestAllowedName(allowed, attributeName, true)) ExitAudit(1, FormatMetadataRefusal(path, item, label, attributeName));
        }

        foreach (var child in Ps.Each(Ps.Get(node, "ChildNodes")))
        {
            if (IsNotElement(child)) continue;
            var childName = Ps.Str(Ps.Get(child, "LocalName"));
            if (!TestAllowedName(allowed, childName, true)) ExitAudit(1, FormatMetadataRefusal(path, item, label, childName));
        }
    }

    private static string GetIncludeLabel(object? node)
    {
        foreach (var name in new[] { "Include", "Update", "Remove" })
        {
            var value = Ps.Str(Ps.GetAttribute(node, name));
            if (!string.IsNullOrWhiteSpace(value)) return name + "=" + value;
        }

        return "Include=";
    }

    // CARD-1409 repair 4 (Review 937bb1c9 F1): one expanded path-item candidate, evaluated from one directory.
    // An output or package root accepts any tail that does not climb out of it. A repo path becomes a
    // repository-relative pattern: the root README.md refuses; an exact path under docs/ refuses unless it is
    // the allow-listed input; a wildcard pattern that can reach docs/ refuses whatever it matches today; the
    // pattern is then expanded against the tracked tree, minus Exclude, and a match under docs/ or a
    // markdown/text document outside the document allow-list refuses naming that match.
    private void AssertPathCandidateAllowed(string root, string path, string item, string segment, PathText candidate,
        string directory, List<PathText> excludeCandidates)
    {
        var detail = item + " Include=" + segment;
        var kind = candidate.Kind;
        if (kind == "output" || kind == "package")
        {
            // CARD-1409 repair 5 (D-28) and 6 (D-33): the decoded tail is compared like every other path.
            var problem = GetOutputTailProblem(candidate.Text);
            if (problem is not null)
            {
                if (problem.Problem == "segment") ExitAudit(1, FormatPathSegmentRefusal(path, detail, problem.Canonical!));
                ExitAudit(1, FormatUnclassifiedIncludeRefusal(path, item, segment));
            }

            return;
        }

        var canonical = ResolveCandidatePattern(root, candidate, directory);
        if (canonical.Refused) ExitAudit(1, FormatPathSegmentRefusal(path, detail, canonical));
        if (canonical.Status == "escape") ExitAudit(1, FormatApprovalRefusal(path, item, segment));
        if (canonical.Status == "outside") ExitAudit(1, FormatUnclassifiedIncludeRefusal(path, item, segment));
        var pattern = canonical.Path;
        // CARD-1409 repair 5 (D-29, O-3): Exclude only subtracts; each part still goes through the normaliser.
        var excludePatterns = new List<string>();
        foreach (var exclude in excludeCandidates)
        {
            if (exclude.Kind != "repo") continue;
            var excludeCanonical = ResolveCandidatePattern(root, exclude, directory);
            if (excludeCanonical.Refused) ExitAudit(1, FormatPathSegmentRefusal(path, item + " Exclude=" + exclude.Source, excludeCanonical));
            if (excludeCanonical.Status != "ok") continue;
            excludePatterns.Add(excludeCanonical.Path);
        }

        // CARD-1409 repair 5 (D-30): a tracked symbolic link the pattern reaches, as a match or as a directory
        // it passes through, refuses whatever the link points at today.
        var link = FindSymlinkReached(pattern);
        if (link is not null) ExitAudit(1, FormatSymlinkRefusal(path, item, segment, link));
        if (TestPatternMatchesReadme(pattern)) ExitAudit(1, FormatApprovalRefusal(path, item, segment, "README.md"));
        if (!TestPatternHasWildcard(pattern))
        {
            if (TestPathUnderDocs(pattern))
            {
                if (!TestAllowedName(AllowedDocsInputs, pattern, true)) ExitAudit(1, FormatApprovalRefusal(path, item, segment, pattern));
                return;
            }

            if (TestDocumentPath(pattern) && !TestDocumentAllowed(pattern, pattern))
                ExitAudit(1, FormatDocumentRefusal(path, item, segment, pattern));
            return;
        }

        var hits = GetTrackedMatches(pattern, excludePatterns);
        // CARD-1409 repair 5 (D-28, N-12): a tracked match is compared like every other path.
        foreach (var hit in hits)
        {
            var hitCanonical = ConvertToCanonicalPath(hit);
            if (hitCanonical.Refused) ExitAudit(1, FormatPathSegmentRefusal(path, detail + " -> " + hit, hitCanonical));
        }

        if (TestPatternCanReachDocs(pattern))
        {
            var named = pattern;
            foreach (var hit in hits)
            {
                if (TestPathUnderDocs(hit) && !TestAllowedName(AllowedDocsInputs, hit, true))
                {
                    named = hit;
                    break;
                }
            }

            ExitAudit(1, FormatApprovalRefusal(path, item, segment, named));
        }

        if (TestAllowedName(AllowedDocumentPatterns, pattern, true)) return;
        foreach (var hitPath in hits)
        {
            if (TestPathUnderDocs(hitPath)) ExitAudit(1, FormatApprovalRefusal(path, item, segment, hitPath));
            if (TestDocumentPath(hitPath) && !TestDocumentAllowed(pattern, hitPath))
                ExitAudit(1, FormatDocumentRefusal(path, item, segment, hitPath));
        }
    }

    // CARD-1409 (Review 31c77116): one include or update text under its item type's rule. The text runs through
    // the pipeline (D-33: expanded, split on ';', decoded once) and every spec is resolved from every directory
    // the item is evaluated from. Every refusal names the file, the item type and the attribute text.
    private void AssertItemIncludeAllowed(string root, string path, string item, string rule, string segment,
        List<string> directories, Hashtable known, string exclude)
    {
        if (Ps.IndexOf(segment, "@(") >= 0 || Ps.IndexOf(segment, "%(") >= 0)
            ExitAudit(1, FormatUnclassifiedIncludeRefusal(path, item, segment));
        if (Ps.Eq(rule, "text")) return;
        var fileDir = GetFileDirectoryPrefix(path);
        if (Ps.Eq(rule, "name"))
        {
            foreach (var name in ResolveItemPathText(segment, fileDir, "", true)!)
            {
                if (name.Kind != "repo" || name.Anchored || TestItemNameIsPathLike(name.Text))
                    ExitAudit(1, FormatUnclassifiedIncludeRefusal(path, item, segment));
            }

            return;
        }

        foreach (var directory in directories)
        {
            var candidates = ResolveItemPathText(segment, fileDir, directory, false);
            if (candidates is null) ExitAudit(1, FormatUnresolvedPropertyRefusal(path, item, segment, _unresolvedReference));
            // CARD-1409 repair 5 (D-29, O-3): an Exclude only subtracts, so a text the audit cannot resolve is dropped
            // whole, and so is a part whose decoded text gained a wildcard character (D-33: MSBuild reads an escaped
            // '*' or '?' literally, so the decoded glob would subtract more than the build does).
            var excludeCandidates = new List<PathText>();
            var excludeText = exclude.Trim();
            if (excludeText.Length > 0 && Ps.IndexOf(excludeText, "@(") < 0 && Ps.IndexOf(excludeText, "%(") < 0)
            {
                var expanded = ResolveItemPathText(excludeText, fileDir, directory, false);
                if (expanded is not null)
                {
                    foreach (var candidate in expanded)
                    {
                        if (candidate.Kind == "nul") continue;
                        if (MeasureWildcardCharacters(candidate.Text) != MeasureWildcardCharacters(candidate.Source)) continue;
                        excludeCandidates.Add(candidate);
                    }
                }
            }

            foreach (var candidate in candidates)
            {
                if (candidate.Kind == "nul") ExitAudit(1, FormatUnclassifiedIncludeRefusal(path, item, segment));
                if (Ps.Eq(rule, "project"))
                {
                    if (candidate.Kind != "repo") ExitAudit(1, FormatUnclassifiedIncludeRefusal(path, item, segment));
                    var canonical = ResolveCandidatePattern(root, candidate, directory);
                    if (canonical.Refused) ExitAudit(1, FormatPathSegmentRefusal(path, item + " Include=" + segment, canonical));
                    if (canonical.Status != "ok") ExitAudit(1, FormatUnclassifiedIncludeRefusal(path, item, segment));
                    var resolved = canonical.Path;
                    if (TestPatternHasWildcard(resolved)) ExitAudit(1, FormatUnclassifiedIncludeRefusal(path, item, segment));
                    if (!TestProjFile(resolved) || !known.ContainsKey(resolved))
                        ExitAudit(1, FormatProjectReferenceRefusal(path, item, segment, resolved));
                    continue;
                }

                AssertPathCandidateAllowed(root, path, item, segment, candidate, directory, excludeCandidates);
            }
        }
    }

    // CARD-1409 repair 4 (Review 937bb1c9, D-18 items closed cheaply): a property value that reads the file
    // system through a property function must read under an output root and may not climb out of it (D-33),
    // and a value part that is a literal path (after the two directory properties) must not be a docs/ path,
    // the root README.md, or a wildcard that can reach them. Other references in a value are left alone: a
    // value is not an input by itself, and an include that uses it is expanded and classified where it is used.
    private void AssertPropertyValuesAllowed(string root, XmlDocument doc, string path)
    {
        var fileDir = GetFileDirectoryPrefix(path);
        var projectDir = GetRepoDirectory(path);
        var outputRooted = @"^\$\(\s*(" + string.Join("|", OutputRootProperties) + @")\s*\)";
        foreach (XmlNode group in doc.GetElementsByTagName("PropertyGroup"))
        {
            foreach (var child in Ps.Each(Ps.Get(group, "ChildNodes")))
            {
                if (IsNotElement(child)) continue;
                var name = Ps.Str(Ps.Get(child, "LocalName"));
                var value = Ps.Str(Ps.Get(child, "InnerText")).Trim();
                if (value.Length == 0) continue;
                foreach (Match call in Regex.Matches(value, @"::\s*([A-Za-z]+)\s*\(\s*(?:'([^']*)'|([^,)]*))"))
                {
                    var function = call.Groups[1].Value;
                    if (!TestAllowedName(FileReadingFunctions, function, true)) continue;
                    var argument = call.Groups[2].Value;
                    if (argument.Length == 0) argument = call.Groups[3].Value.Trim();
                    var rootMatch = Regex.Match(argument, outputRooted);
                    if (!rootMatch.Success) ExitAudit(1, FormatPropertyValueRefusal(path, name, function + "(" + argument + ")"));
                    // CARD-1409 repair 6 (D-33): the tail after the root is path text like an output-rooted include's
                    // and goes through the pipeline; a property no tracked file may set is the SDK's own (opaque).
                    var problem = GetExpandedTailProblem(ConvertSdkOwnedReferences(argument.Substring(rootMatch.Length)), fileDir, projectDir);
                    if (problem is not null) ExitAudit(1, FormatPropertyValueRefusal(path, name, function + "(" + argument + ") " + problem));
                }

                // The pipeline splits the value on ';' and decodes each part once (D-33). CARD-1409 repair 7 (Review
                // 7db88932 R1, D-36): the D-25 skip is per part, as it was before repair 6: a part whose escaped text
                // carries an item, metadata or property-function reference is left alone on its own, and so is a part
                // the pipeline returns unresolved (a reference other than a directory property at the start of its own
                // part, CARD-1409 repair 8, D-39); every other part of the same value is still classified.
                foreach (var spec in ResolveItemPathText(value, fileDir, projectDir, true)!)
                {
                    var source = spec.Source;
                    if (Ps.IndexOf(source, "@(") >= 0 || Ps.IndexOf(source, "%(") >= 0 || Ps.IndexOf(source, "$([") >= 0) continue;
                    if (spec.Kind != "repo") continue;
                    // A wildcard in a value is an exclude list or a glob some include expands where it is used (an
                    // escaped '*' is a literal character, so the escaped part decides), and a bare word (true, Exe,
                    // net10.0) is not a file reference; only a decoded part with a path separator or a document
                    // extension is resolved, from the file's own directory. An include that uses the value is
                    // expanded from every importing project directory where it is used.
                    if (TestPatternHasWildcard(spec.Source)) continue;
                    var decodedText = spec.Text.Replace("\\", "/", StringComparison.Ordinal);
                    if (Ps.IndexOf(decodedText, "/") < 0 && !TestDocumentPath(decodedText)) continue;
                    // CARD-1409 repair 5 (D-28): a value is free text, so it is read as the land host would read it
                    // (folded) and refuses only when that reading is docs/ or the root README.
                    var canonical = ResolveCandidatePattern(root, spec, projectDir, true);
                    if (canonical.Refused) ExitAudit(1, FormatPropertyValueRefusal(path, name, FormatSegmentDetail(canonical)));
                    if (canonical.Status != "ok") continue;
                    var pattern = canonical.Path;
                    var docs = TestPathUnderDocs(pattern) || Ps.Eq(pattern, "README.md");
                    if (docs && !TestAllowedName(AllowedDocsInputs, pattern, true))
                    {
                        var valueDetail = "-> " + pattern;
                        if (canonical.Divergent is not null) valueDetail = valueDetail + " " + FormatSegmentDetail(canonical.Divergent);
                        ExitAudit(1, FormatPropertyValueRefusal(path, name, valueDetail));
                    }
                }
            }
        }
    }

    // CARD-1409 (Review 31c77116 R1): one response-file line is one switch. Comments start with '#'.
    private static bool TestResponseSwitchAllowed(string line)
    {
        if (!(Ps.StartsWith(line, "-") || Ps.StartsWith(line, "/"))) return false;
        var body = line.Substring(1);
        var colon = Ps.IndexOf(body, ":");
        string name;
        string? value;
        if (colon < 0)
        {
            name = body;
            value = null;
        }
        else
        {
            name = body.Substring(0, colon);
            value = body.Substring(colon + 1);
        }

        string[] verbosities = ["q", "quiet", "m", "minimal", "n", "normal", "d", "detailed", "diag", "diagnostic"];
        var lower = name.ToLowerInvariant();
        // switch: the first clause that matches returns.
        if (Ps.Eq(lower, "nodereuse")) return Ps.Eq(value, "true") || Ps.Eq(value, "false");
        if (Ps.Eq(lower, "nr")) return Ps.Eq(value, "true") || Ps.Eq(value, "false");
        if (Ps.Eq(lower, "maxcpucount")) return value is null || Ps.Match(value, "^[1-9][0-9]{0,3}$");
        if (Ps.Eq(lower, "m")) return value is null || Ps.Match(value, "^[1-9][0-9]{0,3}$");
        if (Ps.Eq(lower, "verbosity")) return verbosities.Any(v => Ps.Eq(v, value));
        if (Ps.Eq(lower, "v")) return verbosities.Any(v => Ps.Eq(v, value));
        if (Ps.Eq(lower, "nologo")) return value is null;
        return false;
    }

    private void AssertResponseFileAllowed(string path, string content)
    {
        var text = content;
        if (text.Length > 0 && text[0] == '﻿') text = text.Substring(1);
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;
            if (Ps.StartsWith(line, "#")) continue;
            if (!TestResponseSwitchAllowed(line)) ExitAudit(1, FormatResponseFileRefusal(path, line));
        }
    }

    private void AssertNoDocumentationInput(string root, string[] rows)
    {
        var utf = new UTF8Encoding(false);
        var known = Ps.NewHashtable();
        foreach (var row in rows)
        {
            var tab = row.IndexOf('\t');
            var meta = row.Substring(0, tab).Split(' ');
            var path = row.Substring(tab + 1);
            // CARD-1409 (Review 31c77116 R1): a tracked response file is parsed, not only hashed.
            if (TestResponseFile(path))
            {
                var responseBytes = _blobs.Read(root, meta[2]);
                if (responseBytes is null) ExitAudit(2, "git cat-file failed");
                AssertResponseFileAllowed(path, utf.GetString(responseBytes));
                continue;
            }

            if (!TestProjectXml(path)) continue;
            var bytes = _blobs.Read(root, meta[2]);
            if (bytes is null) ExitAudit(2, "git cat-file failed");
            var content = utf.GetString(bytes);
            var doc = new XmlDocument { XmlResolver = null };
            try
            {
                doc.LoadXml(content);
            }
            catch (Exception)
            {
                ExitAudit(1, "definition file is not readable project XML and cannot be approved: " + path);
            }

            var redirect = GetIntermediateRedirectElement(doc);
            if (redirect is not null) ExitAudit(1, FormatIntermediateRedirectRefusal(path, redirect));
            // CARD-1409 (Review 31c77116): default-deny over the structure, the property names and the item types.
            AssertClassifiedStructure(doc, path);
            AssertAllowedProperties(doc, path);
            AssertAllowedItemTypes(doc, path);
            // CARD-1409 repair 4: the census every include expansion reads; complete before any file is classified.
            RegisterDefinitionFacts(doc, path);
            known[path] = doc;
        }

        var bases = GetImportedProjectDirectories(root, known);
        foreach (var key in known.Keys.Cast<object>().ToArray())
        {
            var path = Ps.Str(key);
            var doc = (XmlDocument)known[path]!;
            var directories = new List<string> { GetRepoDirectory(path) };
            if (bases.ContainsKey(path))
            {
                foreach (var dir in (List<string>)bases[path]!)
                {
                    if (!directories.Contains(dir)) directories.Add(dir);
                }
            }

            AssertPropertyValuesAllowed(root, doc, path);
            foreach (XmlNode group in doc.GetElementsByTagName("ItemGroup"))
            {
                foreach (var node in Ps.Each(Ps.Get(group, "ChildNodes")))
                {
                    if (IsNotElement(node)) continue;
                    var local = Ps.Str(Ps.Get(node, "LocalName"));
                    // Already checked as an exact metadata-free Remove; not an Include/Update allowance.
                    if (TestAllowedName(SdkRemovalItems, local, false)) continue;
                    var rule = ItemRules.TryGetValue(local, out var itemRule) ? itemRule : "";
                    AssertItemOperationsAllowed(node, path, local, GetIncludeLabel(node));
                    // Include and Update are classified; Remove only subtracts and is left alone. The attribute text
                    // goes in whole: the pipeline splits it on ';' after expansion (D-33).
                    foreach (var attributeName in new[] { "Include", "Update" })
                    {
                        var include = Ps.Str(Ps.GetAttribute(node, attributeName)).Trim();
                        if (string.IsNullOrWhiteSpace(include)) continue;
                        var exclude = "";
                        if (Ps.Eq(attributeName, "Include")) exclude = Ps.Str(Ps.GetAttribute(node, "Exclude"));
                        AssertItemIncludeAllowed(root, path, local, rule, include, directories, known, exclude);
                    }
                }
            }
        }
    }

    private ProcessStartInfo GitStart(string root, string arguments)
    {
        var start = new ProcessStartInfo
        {
            FileName = GitExecutable(),
            Arguments = arguments,
            WorkingDirectory = root,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (var (name, value) in _environment ?? new Dictionary<string, string?>())
        {
            if (value is null) start.Environment.Remove(name);
            else start.Environment[name] = value;
        }

        return start;
    }

    // The script started 'git' from its own process, whose PATH a caller could set. An injected PATH is
    // searched here the same way, since Process.Start would search this process's PATH instead.
    private string GitExecutable()
    {
        if (_environment is null) return "git";
        var path = _environment.FirstOrDefault(e => string.Equals(e.Key, "PATH",
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal));
        if (path.Key is null || path.Value is null) return "git";
        var name = OperatingSystem.IsWindows() ? "git.exe" : "git";
        foreach (var directory in path.Value.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(directory, name);
            if (File.Exists(candidate)) return candidate;
        }

        return "git";
    }

    private byte[]? GetGitBytes(string root, string arguments)
    {
        Process process;
        try
        {
            process = Process.Start(GitStart(root, arguments))!;
            if (process is null) return null;
        }
        catch (Exception)
        {
            return null;
        }

        using (process)
        {
            var errors = process.StandardError.ReadToEndAsync();
            using var body = new MemoryStream();
            try
            {
                process.StandardOutput.BaseStream.CopyTo(body);
                process.WaitForExit();
                _ = errors.Result;
            }
            catch (Exception)
            {
                try { process.Kill(); } catch (Exception) { }
                return null;
            }

            if (process.ExitCode != 0) return null;
            return body.ToArray();
        }
    }

    /// <summary>
    /// The script ran one 'git cat-file blob &lt;oid&gt;' per definition file, in sorted order, and stopped at the
    /// first failure. One 'git cat-file --batch' process answers the same requests in the same order, started
    /// on the first read: '&lt;oid&gt;^{blob}' peels as 'cat-file blob' does, and a missing object, another type
    /// or a dead process is that read's failure.
    /// </summary>
    private sealed class BlobReader(PrecheckAudit owner) : IDisposable
    {
        private Process? _process;
        private Task<string>? _errors;
        private bool _broken;

        public byte[]? Read(string root, string oid)
        {
            if (_broken) return null;
            try
            {
                if (_process is null)
                {
                    var start = owner.GitStart(root, "cat-file --batch");
                    start.RedirectStandardInput = true;
                    _process = Process.Start(start);
                    if (_process is null)
                    {
                        _broken = true;
                        return null;
                    }

                    _errors = _process.StandardError.ReadToEndAsync();
                }

                var input = _process.StandardInput.BaseStream;
                input.Write(Encoding.ASCII.GetBytes(oid + "^{blob}\n"));
                input.Flush();
                var output = _process.StandardOutput.BaseStream;
                var header = ReadLine(output);
                var fields = header?.Split(' ');
                if (fields is not { Length: 3 } || fields[1] != "blob" ||
                    !int.TryParse(fields[2], NumberStyles.None, CultureInfo.InvariantCulture, out var size))
                {
                    _broken = true;
                    return null;
                }

                var bytes = new byte[size];
                output.ReadExactly(bytes);
                if (output.ReadByte() != '\n')
                {
                    _broken = true;
                    return null;
                }

                return bytes;
            }
            catch (Exception)
            {
                _broken = true;
                return null;
            }
        }

        public void Dispose()
        {
            if (_process is null) return;
            try
            {
                _process.StandardInput.Close();
                if (!_process.WaitForExit(10_000)) _process.Kill();
                _errors?.Wait(1_000);
            }
            catch (Exception)
            {
            }
            finally
            {
                _process.Dispose();
            }
        }

        private static string? ReadLine(Stream stream)
        {
            var line = new List<byte>();
            while (true)
            {
                var b = stream.ReadByte();
                if (b < 0) return null;
                if (b == '\n') return Encoding.ASCII.GetString(line.ToArray());
                line.Add((byte)b);
            }
        }
    }
}
