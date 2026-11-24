using System.Text;
using System.Text.Json;
using Antiphon.Nightly;
using Antiphon.Tests.Application;
using Microsoft.Extensions.Time.Testing;

namespace Antiphon.Tests.Scripts.Nightly;

/// <summary>One TRX row (Write-C487Trx): the test id, the class and method, and the outcome (Passed when empty).</summary>
public sealed record NightlyTrxRow(string Id, string ClassName, string MethodName, string Outcome = "Passed");

/// <summary>
/// One TUnit node of a discovery or an execution document (Write-C487Discovery, Write-C487Execution). An empty State
/// reads as Discovered in a discovery document and Passed in an execution document; an empty ClassName as Type.
/// </summary>
public sealed record NightlyTestNode(string Uid, string Type, string Method, string Namespace = "", string State = "", string ClassName = "");

/// <summary>
/// W2-nightly S01: the C# twin of the c487-harness fixture functions (scripts/lib/c487-harness.ps1). One temporary
/// root per test (New-C487Root), deleted on Dispose after clearing read-only files without following links
/// (Clear-C487OwnedResultReadOnly). It writes the owned clone (New-C487OwnedClone), TRX, execution, discovery and
/// evidence files (Write-C487Trx/Execution/Discovery/Evidence), and builds a NightlyHost from fakes in place of the
/// seams file (Write-C487Seams): FakeGit, RecordingLauncher, FakeProcessTable, FakeHostProbe and a FakeTimeProvider.
/// </summary>
public sealed class NightlyFixture : IDisposable
{
    /// <summary>New-C487OwnedClone's default origin.</summary>
    public const string DefaultOrigin = "https://github.com/michal-ciechan/Antiphon";

    /// <summary>The fake clock's start (the instant the C487 health seams pin).</summary>
    public static readonly DateTimeOffset DefaultNow = new(2026, 9, 11, 12, 0, 0, TimeSpan.Zero);

    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    private NightlyFixture(string root)
    {
        Root = root;
        Directory.CreateDirectory(root);
    }

    /// <summary>A fresh root under the temp directory; prefix names the test family.</summary>
    public static NightlyFixture Create(string prefix = "nightly") =>
        new(Path.GetFullPath(Path.Combine(Path.GetTempPath(), prefix + "-" + Guid.NewGuid().ToString("N"))));

    /// <summary>The fixture root (the C487 results directory).</summary>
    public string Root { get; }

    /// <summary>The host's standard output.</summary>
    public StringWriter Out { get; } = new();

    /// <summary>The host's standard error.</summary>
    public StringWriter Error { get; } = new();

    /// <summary>The host's trace lines (the TracePath seam).</summary>
    public StringWriter Trace { get; } = new();

    /// <summary>A path under the root.</summary>
    public string PathOf(params string[] parts) => Path.Combine(new[] { Root }.Concat(parts).ToArray());

    /// <summary>
    /// A NightlyHost over fakes: each argument left null gets a new default fake (FakeGit, RecordingLauncher,
    /// FakeProcessTable, FakeHostProbe, a FakeTimeProvider at DefaultNow), with this fixture's Out, Error and Trace.
    /// </summary>
    public NightlyHost Host(FakeGit? git = null, INightlyLauncher? launcher = null, INightlyProcessTable? processes = null,
        INightlyHostProbe? probe = null, TimeProvider? clock = null) => new()
    {
        Clock = clock ?? new FakeTimeProvider(DefaultNow),
        Git = (git ?? new FakeGit()).Run,
        Launcher = launcher ?? new RecordingLauncher(),
        Processes = processes ?? new FakeProcessTable(),
        Probe = probe ?? new FakeHostProbe(),
        Out = Out,
        Error = Error,
        Trace = Trace,
    };

    /// <summary>
    /// New-C487OwnedClone: Root/checkout with a .git directory whose config sets core.longpaths and the origin url and
    /// whose HEAD names master, and the .antiphon-nightly-owned marker (origin, kind nightly-clone). With copyScripts
    /// it also copies scripts/nightly-run.ps1 and scripts/lib (without bin/ and
    /// obj/), and the src/Antiphon.IO.WriteBehind sources the core builds against, from this checkout. Returns the clone.
    /// </summary>
    public string NewOwnedClone(string origin = DefaultOrigin, bool copyScripts = false)
    {
        var clone = PathOf("checkout");
        var git = Path.Combine(clone, ".git");
        Directory.CreateDirectory(git);
        File.WriteAllText(Path.Combine(git, "config"), "[core]\n\tlongpaths = true\n[remote \"origin\"]\n\turl = " + origin + "\n", Encoding.ASCII);
        File.WriteAllText(Path.Combine(git, "HEAD"), "ref: refs/heads/master\n", Encoding.ASCII);
        WriteJson(Path.Combine(clone, ".antiphon-nightly-owned"), new { origin, kind = "nightly-clone" });
        if (!copyScripts) return clone;
        var source = DelegateScriptRunner.RepoRoot;
        var scripts = Path.Combine(clone, "scripts");
        Directory.CreateDirectory(scripts);
        foreach (var entry in new[] { "nightly-run.ps1" })
            File.Copy(Path.Combine(source, "scripts", entry), Path.Combine(scripts, entry), true);
        CopyTree(Path.Combine(source, "scripts", "lib"), Path.Combine(scripts, "lib"));
        var writeBehind = Path.Combine(clone, "src", "Antiphon.IO.WriteBehind");
        Directory.CreateDirectory(writeBehind);
        foreach (var file in Directory.EnumerateFiles(Path.Combine(source, "src", "Antiphon.IO.WriteBehind")))
            if (file.EndsWith(".cs", StringComparison.Ordinal) || file.EndsWith(".csproj", StringComparison.Ordinal))
                File.Copy(file, Path.Combine(writeBehind, Path.GetFileName(file)), true);
        return clone;
    }

    /// <summary>Write-C487Trx: a minimal TRX (TestDefinitions, then Results) with one UnitTest and one UnitTestResult per row.</summary>
    public static void WriteTrx(string path, IEnumerable<NightlyTrxRow> rows)
    {
        var definitions = new StringBuilder("<TestDefinitions>");
        var results = new StringBuilder("<Results>");
        foreach (var row in rows)
        {
            var outcome = string.IsNullOrWhiteSpace(row.Outcome) ? "Passed" : row.Outcome;
            definitions.Append($"<UnitTest id=\"{row.Id}\" name=\"{row.ClassName}.{row.MethodName}\"><TestMethod className=\"{row.ClassName}\" name=\"{row.MethodName}\" /></UnitTest>");
            results.Append($"<UnitTestResult testId=\"{row.Id}\" testName=\"{row.ClassName}.{row.MethodName}\" outcome=\"{outcome}\" />");
        }
        definitions.Append("</TestDefinitions>");
        results.Append("</Results>");
        CreateParent(path);
        File.WriteAllText(path, "<?xml version=\"1.0\" encoding=\"utf-8\"?><TestRun>" + definitions + results + "</TestRun>");
    }

    /// <summary>Write-C487Execution: an antiphon-tunit-execution-v1 document (uid, state, type, method, namespace, className per node).</summary>
    public static void WriteExecution(string path, IEnumerable<NightlyTestNode> nodes) => WriteJson(path, ExecutionDocument(nodes));

    /// <summary>Write-C487Discovery: an antiphon-tunit-discovery-v1 document (assembly hash c487-test-hash; uid, type, method, namespace, state per node).</summary>
    public static void WriteDiscovery(string path, IEnumerable<NightlyTestNode> nodes) => WriteJson(path, DiscoveryDocument(nodes));

    /// <summary>Write-C487Evidence: the body as JSON in Root/&lt;case&gt;.json. Returns the path.</summary>
    public string WriteEvidence(string caseName, object body)
    {
        var path = PathOf(caseName + ".json");
        WriteJson(path, body);
        return path;
    }

    /// <summary>The execution document WriteExecution writes.</summary>
    public static object ExecutionDocument(IEnumerable<NightlyTestNode> nodes) => new
    {
        format = NightlyDiagnosticLog.ExecutionFormat,
        tunitVersion = NightlyDiagnosticLog.SupportedTunit,
        mtpVersion = NightlyDiagnosticLog.SupportedMtp,
        nodes = nodes.Select(node => new
        {
            uid = node.Uid,
            state = string.IsNullOrEmpty(node.State) ? "Passed" : node.State,
            type = node.Type,
            method = node.Method,
            @namespace = node.Namespace,
            className = string.IsNullOrEmpty(node.ClassName) ? node.Type : node.ClassName,
        }).ToArray(),
    };

    /// <summary>The discovery document WriteDiscovery writes.</summary>
    public static object DiscoveryDocument(IEnumerable<NightlyTestNode> nodes) => new
    {
        format = NightlyDiagnosticLog.DiscoveryFormat,
        tunitVersion = NightlyDiagnosticLog.SupportedTunit,
        mtpVersion = NightlyDiagnosticLog.SupportedMtp,
        assemblyHash = "c487-test-hash",
        nodes = nodes.Select(node => new
        {
            uid = node.Uid,
            type = node.Type,
            method = node.Method,
            @namespace = node.Namespace,
            state = string.IsNullOrEmpty(node.State) ? "Discovered" : node.State,
        }).ToArray(),
    };

    /// <summary>Write-NightlyAtomicJson: indented UTF-8 without a BOM, through a temporary file renamed over the target.</summary>
    public static void WriteJson(string path, object value)
    {
        CreateParent(path);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(value, Indented), new UTF8Encoding(false));
        File.Move(temporary, path, overwrite: true);
    }

    /// <summary>Clears read-only files (git objects) without following links, then deletes the root; best effort.</summary>
    public void Dispose()
    {
        if (!Directory.Exists(Root)) return;
        ClearOwnedReadOnly(Root);
        try { Directory.Delete(Root, true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    /// <summary>
    /// Clear-C487OwnedResultReadOnly's twin (CARD-1687) on the disk: the owned tree under root, each directory listed with
    /// <see cref="DirectoryInfo.EnumerateFileSystemInfos()"/> (the find data's attributes, no per-entry lookup). Returns the
    /// files cleared.
    /// </summary>
    public static int ClearOwnedReadOnly(string root) =>
        ClearOwnedReadOnly<FileSystemInfo>(new DirectoryInfo(root), item => item.Attributes,
            directory => ((DirectoryInfo)directory).EnumerateFileSystemInfos(), item => item.Attributes &= ~FileAttributes.ReadOnly);

    /// <summary>
    /// Walks an owned tree from root and clears the read-only bit of each owned file, so the owner's recursive delete succeeds
    /// on Windows (Git's loose objects are read-only and Directory.Delete refuses them). A link is never followed: a linked
    /// root, a directory link and a file link are skipped. Each directory is listed once; a file is never listed; nothing is
    /// deleted (the owner deletes after joining its processes). Returns the files cleared. C487ReadOnlyCleanupWitnessTests
    /// witnesses it over a forced-Windows tree.
    /// </summary>
    public static int ClearOwnedReadOnly<TNode>(TNode root, Func<TNode, FileAttributes> attributes, Func<TNode, IEnumerable<TNode>> list,
        Action<TNode> clearReadOnly)
    {
        var cleared = 0;
        var pending = new Stack<TNode>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            if ((attributes(directory) & FileAttributes.ReparsePoint) != 0) continue;
            foreach (var item in list(directory))
            {
                var itemAttributes = attributes(item);
                if ((itemAttributes & FileAttributes.ReparsePoint) != 0) continue;
                if ((itemAttributes & FileAttributes.Directory) != 0) pending.Push(item);
                else if ((itemAttributes & FileAttributes.ReadOnly) != 0)
                {
                    clearReadOnly(item);
                    cleared++;
                }
            }
        }
        return cleared;
    }

    private static void CreateParent(string path)
    {
        var parent = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
    }

    private static void CopyTree(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var file in Directory.EnumerateFiles(source)) File.Copy(file, Path.Combine(target, Path.GetFileName(file)), true);
        foreach (var directory in Directory.EnumerateDirectories(source))
        {
            var name = Path.GetFileName(directory);
            // Build output stays behind: a clone builds its own core.
            if (name is "bin" or "obj") continue;
            CopyTree(directory, Path.Combine(target, name));
        }
    }
}
