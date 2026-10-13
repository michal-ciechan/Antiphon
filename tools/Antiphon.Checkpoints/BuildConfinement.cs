using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace Antiphon.Checkpoints;

// Builds have a clean tracked baseline. Settlement detects changes and never
// writes to the worktree or index. Output redirection reduces accidental writes;
// arbitrary MSBuild targets remain capable of changing source and are reported.
public static class BuildConfinement
{
    public const int WalkCap = 500;
    private static readonly HashSet<string> IsolationKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "OutputPath", "OutDir", "BaseOutputPath", "BaseIntermediateOutputPath",
        "MSBuildProjectExtensionsPath", "ArtifactsPath", "UseArtifactsOutput",
    };

    public sealed record Baseline(
        string Head,
        string Repository,
        Dictionary<string, string> Entries,
        Dictionary<string, string> Untracked,
        Dictionary<string, string> Content);

    public sealed record Settlement(string? Kind, string Detail, IReadOnlyList<string> Paths);

    public readonly record struct OutputLayout(string Owned, string Artifacts);

    public sealed record Prepared(
        string? Refusal,
        string Detail,
        IReadOnlyList<string> Arguments,
        Baseline? Baseline);

    public static string? ReportedRefusal(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return null;
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.TrimEnd('\r').Trim();
            if (line.StartsWith("refused:", StringComparison.Ordinal))
                return line;
        }

        return null;
    }

    public static OutputLayout LayoutFor(string workingDirectory, string project, string relativeOutput)
    {
        var projectFull = Path.GetFullPath(Path.IsPathRooted(project) ? project : Path.Combine(workingDirectory, project));
        var projectDir = ProjectWriteRoots.DirectoryOf(projectFull);
        if (string.IsNullOrEmpty(projectDir))
            projectDir = string.IsNullOrWhiteSpace(workingDirectory) ? Environment.CurrentDirectory : workingDirectory;
        var owned = AbsoluteOutput(projectDir, relativeOutput);
        return new OutputLayout(owned, owned.TrimEnd('/') + "/artifacts");
    }

    public static Prepared Prepare(
        string workingDirectory,
        IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string>? environment,
        CancellationToken cancellationToken)
    {
        if (!TryFindOutput(arguments, out var outputValue))
            return new Prepared(null, "", arguments, null);
        if (!TryFindProject(arguments, out var project))
            return Refuse("graph-unevaluated", "build command has no project path");

        var projectFull = Path.GetFullPath(Path.IsPathRooted(project) ? project : Path.Combine(workingDirectory, project));
        var projectDir = ProjectWriteRoots.DirectoryOf(projectFull);
        if (string.IsNullOrEmpty(projectDir))
            return Refuse("graph-unevaluated", "project directory missing");

        var repo = Git(workingDirectory, ["rev-parse", "--show-toplevel"]);
        if (repo.Error is not null || repo.Exit != 0 || string.IsNullOrWhiteSpace(repo.Stdout))
            return Refuse("tracked-snapshot", repo.Error ?? "git-toplevel");
        var repository = repo.Stdout.Trim();
        var status = ReadStatus(repository);
        if (status.Error is not null)
            return Refuse("tracked-snapshot", status.Error);
        if (status.Paths.Count > 0)
            return new Prepared("dirty-tracked", DirtyText(status.Paths), [], null);

        var captured = Capture(repository);
        if (captured.Error is not null || captured.Value is null)
            return Refuse("tracked-snapshot", captured.Error ?? "git-ls-files");
        var baseline = captured.Value;

        var layout = LayoutFor(workingDirectory, project, outputValue);
        var fresh = SafeWrite.WasCreated(layout.Owned) ? null : SafeWrite.CreateFreshDirectory(layout.Owned, SafeWrite.Under(layout.Owned));
        if (fresh is not null) return Refuse(fresh, layout.Owned);
        var rewritten = Rewrite(arguments, layout.Owned, layout.Artifacts);
        var forwarded = Forwarded(arguments);
        WalkResult walked;
        try
        {
            walked = Walk(projectFull, layout.Owned, layout.Artifacts, forwarded, workingDirectory, environment, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            var settled = Settle(baseline);
            if (settled.Kind is not null)
                return new Prepared(settled.Kind, settled.Detail, [], baseline);
            throw;
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or JsonException)
        {
            return PreferSettlement(baseline, Refuse("graph-unevaluated", ex.GetType().Name));
        }

        if (walked.Error is not null)
            return PreferSettlement(baseline, Refuse("graph-unevaluated", walked.Error));
        return new Prepared(null, "", rewritten, baseline);
    }

    public static Settlement Settle(Baseline baseline)
    {
        var status = ReadStatus(baseline.Repository);
        if (status.Error is not null)
            return new Settlement("tracked-snapshot", "refused: tracked-snapshot\n" + status.Error + "\nNOTHING outside tool-owned output/evidence directories was modified by the tool.\n", []);
        var paths = new HashSet<string>(status.Paths, StringComparer.Ordinal);
        var staged = new HashSet<string>(StringComparer.Ordinal);
        foreach (var pair in baseline.Content.Concat(baseline.Untracked))
            if (pair.Value == "unreadable" || Stamp(Combine(baseline.Repository, pair.Key)) != pair.Value)
                paths.Add(pair.Key);
        if (!IndexMatches(baseline, out var error))
        {
            if (error is not null)
                return new Settlement("tracked-snapshot", "refused: tracked-snapshot\n" + error + "\n", []);
            var listed = Git(baseline.Repository, ["ls-files", "-z", "-s"]);
            if (listed.Error is not null) return new Settlement("tracked-snapshot", "refused: " + listed.Error, []);
            var current = ParseIndex(listed.Stdout);
            if (current.Value is null) return new Settlement("tracked-snapshot", "refused: index-record", []);
            foreach (var key in current.Value.Keys) if (!baseline.Entries.ContainsKey(key)) staged.Add(key);
            foreach (var key in baseline.Entries.Keys.Union(current.Value.Keys))
                if (!baseline.Entries.TryGetValue(key, out var old) || !current.Value.TryGetValue(key, out var now) || old != now)
                    paths.Add(key);
        }
        var ordinary = Git(baseline.Repository, ["ls-files", "-z", "--others", "--exclude-standard"]);
        if (ordinary.Error is not null) return new Settlement("tracked-snapshot", "refused: " + ordinary.Error, []);
        foreach (var path in ordinary.Stdout.Split('\0', StringSplitOptions.RemoveEmptyEntries))
            if (!path.StartsWith(".antiphon/", StringComparison.Ordinal) && !baseline.Untracked.ContainsKey(path)) paths.Add(path);
        if (paths.Count == 0) return new Settlement(null, "", []);
        var text = new StringBuilder("refused: tampered\n");
        foreach (var path in paths.Order(StringComparer.Ordinal))
        {
            text.Append("TAMPERED ").Append(path).Append('\n');
            if (baseline.Entries.ContainsKey(path))
                text.Append("manual command: git -C ").Append(Quote(baseline.Repository)).Append(" restore --source=").Append(baseline.Head).Append(" --staged --worktree -- ").Append(Quote(path)).Append('\n');
            else if (staged.Contains(path))
                text.Append("manual command: git -C ").Append(Quote(baseline.Repository)).Append(" restore --staged -- ").Append(Quote(path)).Append('\n');
            else
                text.Append("manual command: git -C ").Append(Quote(baseline.Repository)).Append(" status --short -- ").Append(Quote(path)).Append("; inspect and recover user bytes from your backup\n");
        }
        text.Append("manual restore required; NOTHING outside tool-owned output/evidence directories was modified by the tool.\n");
        return new Settlement("tampered", text.ToString(), paths.Order(StringComparer.Ordinal).ToList());
    }

    private static string Combine(string repository, string relative) => Path.Combine(repository, relative.Replace('/', Path.DirectorySeparatorChar));

    private readonly record struct ManifestResult(Dictionary<string, string>? Value, string? Error);

    private static ManifestResult CaptureUntracked(string repository)
    {
        var ordinary = Git(repository, ["ls-files", "-z", "--others", "--exclude-standard"]);
        if (ordinary.Error is not null) return new ManifestResult(null, ordinary.Error);
        var listed = Git(repository, ["ls-files", "-z", "--others", "--ignored", "--exclude-standard"]);
        if (listed.Error is not null || listed.Exit != 0)
            return new ManifestResult(null, listed.Error ?? "git-ls-files");
        var files = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var relative in (ordinary.Stdout + listed.Stdout).Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            // Evidence is written by the checkpoint process while the build is observed.
            if (relative.StartsWith(".antiphon/", StringComparison.Ordinal)) continue;
            files[relative] = Stamp(Combine(repository, relative));
        }
        return new ManifestResult(files, null);
    }

    private static string Stamp(string full)
    {
        try
        {
            var file = new FileInfo(full);
            if (file.LinkTarget is not null)
                return StampLink(file.LinkTarget);
            var directory = new DirectoryInfo(full);
            if (directory.LinkTarget is not null)
                return StampLink(directory.LinkTarget);
            if (directory.Exists)
                return "directory|0|";
            if (!file.Exists)
                return "absent|0|";
            var bytes = File.ReadAllBytes(full);
            return "file|" + bytes.Length + "|" + Hash(bytes);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return "unreadable";
        }
    }

    private static string StampLink(string target)
    {
        var bytes = Encoding.UTF8.GetBytes(target);
        return "symlink|" + bytes.Length + "|" + Hash(bytes);
    }

    private static string Hash(byte[] bytes) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();

    private static Prepared PreferSettlement(Baseline baseline, Prepared graphRefusal)
    {
        var settled = Settle(baseline);
        if (settled.Kind is not null)
            return new Prepared(settled.Kind, settled.Detail, [], baseline);
        if (settled.Kind == "tampered")
            return new Prepared("tampered", settled.Detail, [], baseline);
        return graphRefusal;
    }

    private static Prepared Refuse(string reason, string detail) =>
        new(reason, "refused: " + reason + "\n" + detail + "\n", [], null);

    private static string DirtyText(IReadOnlyList<string> paths) => "refused: dirty-tracked\n" + string.Join("\n", paths.Select(path => "DIRTY " + path)) + "\n";

    private static string Quote(string value) => "'" + value.Replace("'", "'\\''", StringComparison.Ordinal) + "'";

    private static bool TryFindProject(IReadOnlyList<string> arguments, out string project)
    {
        project = "";
        for (var i = 1; i < arguments.Count; i++)
        {
            var token = arguments[i];
            if (token.StartsWith('-') || token.StartsWith("/p:", StringComparison.OrdinalIgnoreCase)
                || token.StartsWith("/property:", StringComparison.OrdinalIgnoreCase))
                continue;
            project = token;
            return true;
        }

        return false;
    }

    private static bool TryFindOutput(IReadOnlyList<string> arguments, out string value)
    {
        value = "";
        foreach (var token in arguments)
        {
            if (TryReadProperty(token, out var key, out var property) && key.Equals("OutputPath", StringComparison.OrdinalIgnoreCase))
            {
                value = property;
                return true;
            }
        }

        return false;
    }

    private static List<string> Rewrite(IReadOnlyList<string> arguments, string owned, string artifacts)
    {
        var result = new List<string>(arguments.Count + 3);
        var artifactsSeen = false;
        var useSeen = false;
        var hostSeen = false;
        foreach (var token in arguments)
        {
            if (TryReadProperty(token, out var key, out _))
            {
                if (key.Equals("OutputPath", StringComparison.OrdinalIgnoreCase))
                {
                    result.Add("--property:OutputPath=" + owned);
                    continue;
                }

                if (key.Equals("ArtifactsPath", StringComparison.OrdinalIgnoreCase))
                    artifactsSeen = true;
                else if (key.Equals("UseArtifactsOutput", StringComparison.OrdinalIgnoreCase))
                    useSeen = true;
                else if (key.Equals("UseAppHost", StringComparison.OrdinalIgnoreCase))
                    hostSeen = true;
            }

            result.Add(token);
        }

        if (!artifactsSeen)
            result.Add("--property:ArtifactsPath=" + artifacts);
        if (!useSeen)
            result.Add("--property:UseArtifactsOutput=true");
        if (!hostSeen)
            result.Add("--property:UseAppHost=false");
        return result;
    }

    private static List<string> Forwarded(IReadOnlyList<string> arguments)
    {
        var forwarded = new List<string>();
        foreach (var token in arguments)
        {
            if (!TryReadProperty(token, out var key, out var value) || IsolationKeys.Contains(key))
                continue;
            forwarded.Add("--property:" + key + "=" + value);
        }

        return forwarded;
    }

    private static string AbsoluteOutput(string projectDir, string outputValue)
    {
        var full = Path.IsPathRooted(outputValue)
            ? Path.GetFullPath(outputValue)
            : Path.GetFullPath(Path.Combine(projectDir, outputValue));
        var slash = full.Replace('\\', '/');
        return slash.EndsWith('/') ? slash : slash + "/";
    }

    private readonly record struct WalkResult(List<string> Projects, string? Error);

    private static WalkResult Walk(
        string projectFull,
        string owned,
        string artifacts,
        IReadOnlyList<string> forwarded,
        string workingDirectory,
        IReadOnlyDictionary<string, string>? environment,
        CancellationToken cancellationToken)
    {
        var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var seen = new HashSet<string>(comparer);
        var queue = new Queue<string>();
        var projects = new List<string>();
        queue.Enqueue(projectFull);
        while (queue.Count > 0)
        {
            var current = Path.GetFullPath(queue.Dequeue());
            if (!seen.Add(current))
                continue;
            if (projects.Count >= WalkCap)
                return new WalkResult([], "walk cap exceeded");
            projects.Add(current);
            var eval = Evaluate(current, owned, artifacts, forwarded, workingDirectory, environment, cancellationToken);
            if (eval.Error is not null)
                return new WalkResult([], eval.Error);
            foreach (var reference in eval.References)
            {
                if (!File.Exists(reference))
                    return new WalkResult([], "referenced project missing: " + reference);
                queue.Enqueue(reference);
            }
        }

        return new WalkResult(projects, null);
    }

    private readonly record struct EvalResult(List<string> References, string? Error);

    private static EvalResult Evaluate(
        string project,
        string owned,
        string artifacts,
        IReadOnlyList<string> forwarded,
        string workingDirectory,
        IReadOnlyDictionary<string, string>? environment,
        CancellationToken cancellationToken)
    {
        var args = new List<string>
        {
            "msbuild", project, "-getItem:ProjectReference", "-nologo", "-nodeReuse:false",
            "--property:OutputPath=" + owned,
            "--property:ArtifactsPath=" + artifacts,
            "--property:UseArtifactsOutput=true",
        };
        args.AddRange(forwarded);
        if (!forwarded.Any(token => token.StartsWith("--property:UseAppHost=", StringComparison.OrdinalIgnoreCase)))
            args.Add("--property:UseAppHost=false");

        var psi = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = string.IsNullOrWhiteSpace(workingDirectory) ? Environment.CurrentDirectory : workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);
        if (environment is not null)
            foreach (var (name, value) in environment)
                psi.Environment[name] = value;
        psi.Environment["MSBUILDDISABLENODEREUSE"] = "1";
        psi.Environment["DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER"] = "1";
        foreach (var name in new[] { "GIT_DIR", "GIT_WORK_TREE", "GIT_INDEX_FILE" })
            psi.Environment.Remove(name);

        using var process = new Process { StartInfo = psi };
        try
        {
            if (!process.Start())
                return new EvalResult([], "msbuild did not start");
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return new EvalResult([], ex.GetType().Name);
        }

        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        if (!process.WaitForExit(120_000))
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            try { process.WaitForExit(2_000); } catch (InvalidOperationException) { }
            return new EvalResult([], "msbuild evaluation timed out");
        }

        process.WaitForExit();
        var outText = stdout.GetAwaiter().GetResult();
        var errText = stderr.GetAwaiter().GetResult();
        if (process.ExitCode != 0)
            return new EvalResult([], "msbuild exit " + process.ExitCode);
        return ParseReferences(outText, errText);
    }

    private static EvalResult ParseReferences(string stdout, string stderr)
    {
        var start = stdout.IndexOf('{');
        if (start < 0)
            return new EvalResult([], "msbuild json missing");
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(stdout[start..]);
        }
        catch (JsonException)
        {
            return new EvalResult([], "msbuild json unusable");
        }

        using (doc)
        {
            if (!doc.RootElement.TryGetProperty("Items", out var items) || items.ValueKind != JsonValueKind.Object)
                return new EvalResult([], "msbuild items missing");
            if (!items.TryGetProperty("ProjectReference", out var references))
                return new EvalResult([], null);
            var paths = new List<string>();
            if (references.ValueKind == JsonValueKind.Object)
            {
                if (!TryFullPath(references, paths))
                    return new EvalResult([], "project reference FullPath missing");
            }
            else if (references.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in references.EnumerateArray())
                {
                    if (!TryFullPath(item, paths))
                        return new EvalResult([], "project reference FullPath missing");
                }
            }
            else
                return new EvalResult([], "project reference json unusable");

            _ = stderr;
            return new EvalResult(paths, null);
        }
    }

    private static bool TryFullPath(JsonElement item, List<string> paths)
    {
        if (!item.TryGetProperty("FullPath", out var full) || full.ValueKind != JsonValueKind.String)
            return false;
        var path = full.GetString();
        if (string.IsNullOrWhiteSpace(path))
            return false;
        paths.Add(path);
        return true;
    }

    private readonly record struct CaptureResult(Baseline? Value, string? Error);

    private static CaptureResult Capture(string repository)
    {
        var head = Git(repository, ["rev-parse", "HEAD"]);
        if (head.Error is not null || head.Exit != 0 || string.IsNullOrWhiteSpace(head.Stdout))
            return new CaptureResult(null, head.Error ?? "git-head");
        var listed = Git(repository, ["ls-files", "-z", "-s"]);
        if (listed.Error is not null || listed.Exit != 0)
            return new CaptureResult(null, listed.Error ?? "git-ls-files");
        var entries = ParseIndex(listed.Stdout);
        if (entries.Error is not null || entries.Value is null)
            return new CaptureResult(null, entries.Error ?? "index-record");
        var untracked = CaptureUntracked(repository);
        if (untracked.Error is not null || untracked.Value is null)
            return new CaptureResult(null, untracked.Error ?? "git-ls-files");
        var content = entries.Value.Keys.ToDictionary(path => path, path => Stamp(Combine(repository, path)), StringComparer.Ordinal);
        if (content.Values.Any(value => value == "unreadable")) return new CaptureResult(null, "tracked-unreadable");
        return new CaptureResult(new Baseline(head.Stdout.Trim(), repository, entries.Value, untracked.Value, content), null);
    }

    private readonly record struct IndexResult(Dictionary<string, string>? Value, string? Error);

    private static IndexResult ParseIndex(string text)
    {
        var entries = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var record in text.Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            var tab = record.IndexOf('\t');
            if (tab < 1 || tab >= record.Length - 1)
                return new IndexResult(null, "index-record");
            entries[record[(tab + 1)..]] = record[..tab];
        }

        return new IndexResult(entries, null);
    }

    private static bool IndexMatches(Baseline baseline, out string? error)
    {
        var listed = Git(baseline.Repository, ["ls-files", "-z", "-s"]);
        if (listed.Error is not null || listed.Exit != 0)
        {
            error = listed.Error ?? "git-ls-files";
            return false;
        }

        var parsed = ParseIndex(listed.Stdout);
        if (parsed.Error is not null || parsed.Value is null)
        {
            error = parsed.Error ?? "index-record";
            return false;
        }

        error = null;
        if (parsed.Value.Count != baseline.Entries.Count)
            return false;
        foreach (var pair in baseline.Entries)
        {
            if (!parsed.Value.TryGetValue(pair.Key, out var right) || !string.Equals(pair.Value, right, StringComparison.Ordinal))
                return false;
        }

        return true;
    }

    private readonly record struct StatusResult(List<string> Paths, string? Error);

    private static StatusResult ReadStatus(string repository)
    {
        var status = Git(repository, ["status", "--porcelain=v2", "-z", "--untracked-files=no"]);
        if (status.Error is not null || status.Exit != 0)
            return new StatusResult([], status.Error ?? "git-status");
        return ParseStatus(status.Stdout);
    }

    private static StatusResult ParseStatus(string text)
    {
        var parts = text.Split('\0', StringSplitOptions.RemoveEmptyEntries);
        var paths = new List<string>();
        for (var i = 0; i < parts.Length; i++)
        {
            var entry = parts[i];
            var count = entry[0] switch { '1' => 9, '2' => 10, 'u' => 11, _ => 0 };
            if (count == 0) return new StatusResult([], "status-record");
            var fields = entry.Split(' ', count);
            if (fields.Length != count || fields[^1].Length == 0) return new StatusResult([], "status-record");
            paths.Add(fields[^1]);
            if (entry[0] == '2')
            {
                if (++i >= parts.Length) return new StatusResult([], "status-rename");
                paths.Add(parts[i]);
            }
        }
        return new StatusResult(paths.Distinct(StringComparer.Ordinal).ToList(), null);
    }

    private readonly record struct GitResult(int Exit, string Stdout, string? Error);

    private static GitResult Git(string workingDirectory, IReadOnlyList<string> args)
    {
        var psi = new ProcessStartInfo("git")
        {
            WorkingDirectory = string.IsNullOrWhiteSpace(workingDirectory) ? Environment.CurrentDirectory : workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);
        foreach (var name in new[] { "GIT_DIR", "GIT_WORK_TREE", "GIT_INDEX_FILE" })
            psi.Environment.Remove(name);
        using var process = new Process { StartInfo = psi };
        try
        {
            if (!process.Start())
                return new GitResult(128, "", "git-unavailable");
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return new GitResult(128, "", "git-unavailable");
        }

        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(30_000))
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            return new GitResult(128, "", "git-timeout");
        }

        process.WaitForExit();
        _ = stderr.GetAwaiter().GetResult();
        if (process.ExitCode != 0)
            return new GitResult(process.ExitCode, "", "git-failed");
        return new GitResult(0, stdout.GetAwaiter().GetResult(), null);
    }

    private static bool TryReadProperty(string token, out string key, out string value)
    {
        key = "";
        value = "";
        string? body = null;
        if (token.StartsWith("--property:", StringComparison.OrdinalIgnoreCase))
            body = token["--property:".Length..];
        else if (token.StartsWith("-p:", StringComparison.OrdinalIgnoreCase))
            body = token["-p:".Length..];
        else if (token.StartsWith("/p:", StringComparison.OrdinalIgnoreCase))
            body = token["/p:".Length..];
        if (body is null)
            return false;
        var eq = body.IndexOf('=');
        if (eq <= 0)
            return false;
        key = body[..eq];
        value = body[(eq + 1)..];
        return true;
    }
}
