using System.Diagnostics;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Antiphon.Checkpoints;

// A real dotnet build is redirected onto one absolute OutputPath owned by the
// entry project. ArtifactsPath makes the SDK put each project's intermediates
// and project.assets.json under that directory. A shared BaseIntermediateOutputPath
// is not passed: global properties are not re-expanded, so $(MSBuildProjectName)
// would be one literal directory and restore then cycles. The static XML preflight
// stays in ProjectWriteRoots. A DotnetShim build is left unchanged.
public static class BuildConfinement
{
    public const int WalkCap = 500;
    private static readonly HashSet<string> IsolationKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "OutputPath", "OutDir", "BaseOutputPath", "BaseIntermediateOutputPath",
        "MSBuildProjectExtensionsPath", "ArtifactsPath", "UseArtifactsOutput",
    };

    public sealed record Snapshot(
        string Index,
        Dictionary<string, string> Hashes,
        List<string> Projects);

    public sealed record Prepared(
        string? Refusal,
        string Detail,
        IReadOnlyList<string> Arguments,
        Snapshot? Before);

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
        var projectDir = Path.GetDirectoryName(projectFull);
        if (string.IsNullOrEmpty(projectDir))
            return Refuse("graph-unevaluated", "project directory missing");

        var owned = AbsoluteOutput(projectDir, outputValue);
        var artifacts = owned.TrimEnd('/') + "/artifacts";
        var rewritten = Rewrite(arguments, owned, artifacts);
        var forwarded = Forwarded(arguments);
        WalkResult walked;
        try
        {
            walked = Walk(projectFull, owned, artifacts, forwarded, workingDirectory, environment, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or JsonException)
        {
            return Refuse("graph-unevaluated", ex.GetType().Name);
        }

        if (walked.Error is not null)
            return Refuse("graph-unevaluated", walked.Error);

        var shot = Take(workingDirectory, walked.Projects);
        if (shot.Error is not null)
            return Refuse("tracked-snapshot", shot.Error);
        return new Prepared(null, "", rewritten, shot.Value);
    }

    public static string? Compare(Snapshot before, string workingDirectory)
    {
        var after = Take(workingDirectory, before.Projects);
        if (after.Error is not null || after.Value is null)
            return TamperText(["snapshot " + (after.Error ?? "missing")]);
        var changed = new List<string>();
        var keys = new HashSet<string>(before.Hashes.Keys, StringComparer.Ordinal);
        keys.UnionWith(after.Value.Hashes.Keys);
        foreach (var key in keys.Order(StringComparer.Ordinal))
        {
            before.Hashes.TryGetValue(key, out var left);
            after.Value.Hashes.TryGetValue(key, out var right);
            if (!string.Equals(left, right, StringComparison.Ordinal))
                changed.Add(key);
        }

        if (!string.Equals(before.Index, after.Value.Index, StringComparison.Ordinal))
            changed.Add("git index changed");
        return changed.Count == 0 ? null : TamperText(changed);
    }

    private static Prepared Refuse(string reason, string detail) =>
        new(reason, "refused: " + reason + "\n" + detail + "\n", [], null);

    private static string TamperText(IReadOnlyList<string> changed)
    {
        var builder = new StringBuilder();
        builder.Append("refused: tampered\n");
        var shown = 0;
        foreach (var path in changed)
        {
            if (shown >= 20)
                break;
            builder.Append("TAMPERED ").Append(path).Append('\n');
            shown++;
        }

        if (changed.Count > 20)
            builder.Append("TAMPERED ").Append(changed.Count - 20).Append(" more\n");
        builder.Append("tracked files were not restored\n");
        return builder.ToString();
    }

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

    private readonly record struct ShotResult(Snapshot? Value, string? Error);

    private static ShotResult Take(string workingDirectory, IReadOnlyList<string> projects)
    {
        var top = Git(workingDirectory, ["rev-parse", "--show-toplevel"]);
        if (top.Error is not null || top.Exit != 0 || string.IsNullOrWhiteSpace(top.Stdout))
            return new ShotResult(null, top.Error ?? "git-toplevel");
        var repo = top.Stdout.Trim();
        var listed = Git(repo, ["ls-files", "-s"]);
        if (listed.Error is not null || listed.Exit != 0)
            return new ShotResult(null, listed.Error ?? "git-ls-files");
        var index = listed.Stdout.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd('\n');
        var roots = new List<string>();
        foreach (var project in projects)
        {
            var dir = Path.GetDirectoryName(project);
            if (string.IsNullOrEmpty(dir))
                continue;
            var rel = Relative(repo, dir);
            if (rel is null)
                continue;
            roots.Add(rel);
        }

        var hashes = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in index.Split('\n'))
        {
            if (line.Length == 0)
                continue;
            var tab = line.IndexOf('\t');
            if (tab < 0 || tab == line.Length - 1)
                continue;
            var path = line[(tab + 1)..];
            if (!Under(path, roots))
                continue;
            try
            {
                hashes[path] = Hash(Path.Combine(repo, path.Replace('/', Path.DirectorySeparatorChar)));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return new ShotResult(null, ex.GetType().Name);
            }
        }

        return new ShotResult(new Snapshot(index, hashes, projects.ToList()), null);
    }

    private static string? Relative(string repo, string directory)
    {
        var fullRepo = Path.GetFullPath(repo).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fullDir = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (fullDir.Equals(fullRepo, comparison))
            return "";
        var prefix = fullRepo + Path.DirectorySeparatorChar;
        if (!fullDir.StartsWith(prefix, comparison))
            return null;
        return fullDir[prefix.Length..].Replace('\\', '/');
    }

    private static bool Under(string path, List<string> roots)
    {
        foreach (var root in roots)
        {
            if (root.Length == 0)
                return true;
            if (path.Equals(root, StringComparison.Ordinal) || path.StartsWith(root + "/", StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    private static string Hash(string path)
    {
        if (Directory.Exists(path) && !File.Exists(path))
            return "directory";
        if (!File.Exists(path))
            return "missing";
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
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
