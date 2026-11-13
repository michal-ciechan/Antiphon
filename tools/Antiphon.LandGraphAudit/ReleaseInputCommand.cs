using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Build.Locator;

namespace Antiphon.LandGraphAudit;

/// <summary>Release-only evaluated inventory; never a land-audit approval or a skipping certificate.</summary>
internal static class ReleaseInputCommand
{
    internal static int Run(string[] args)
    {
        var watch = Stopwatch.StartNew();
        try
        {
            var allowed = new[] { "--repo", "--solution", "--source", "--framework", "--configuration", "--platform", "--rid" };
            var options = new Dictionary<string, string>(StringComparer.Ordinal);
            for (var i = 0; i < args.Length; i += 2)
                if (i + 1 >= args.Length || !allowed.Contains(args[i]) || !options.TryAdd(args[i], args[i + 1]))
                    throw new ArgumentException("invalid-options");
            if (allowed.Any(key => !options.ContainsKey(key))
                || options.Where(pair => pair.Key != "--rid").Any(pair => string.IsNullOrWhiteSpace(pair.Value)))
                throw new ArgumentException("missing-options");
            var repo = Path.TrimEndingDirectorySeparator(Path.GetFullPath(options["--repo"]));
            var sha = options["--source"];
            var before = Capture(repo, sha);
            var solution = Relative(repo, Path.GetFullPath(options["--solution"], repo));
            if (!before.Any(input => input.Path == solution)) throw new InvalidDataException("unbound-solution");
            // Evaluation executes no targets. Reuse the existing finite tool ceiling rather than
            // allowing an import/property function to leave an unowned evaluator behind.
            using var watchdog = new Timer(_ =>
            {
                Console.Error.WriteLine("refused: evaluation-timeout");
                Console.Error.Flush();
                Environment.Exit(Program.EvaluationFailed);
            }, null, TimeSpan.FromSeconds(EvaluationBudget.ToolCeilingSeconds), Timeout.InfiniteTimeSpan);
            var instance = MSBuildLocator.QueryVisualStudioInstances(new VisualStudioInstanceQueryOptions
            {
                DiscoveryTypes = DiscoveryType.DotNetSdk, WorkingDirectory = repo,
            }).FirstOrDefault() ?? throw new InvalidDataException("sdk-unavailable");
            MSBuildLocator.RegisterInstance(instance);
            var evaluation = Evaluate(repo, solution, before, instance.MSBuildPath, options);
            var after = Capture(repo, sha);
            if (!before.SequenceEqual(after)) throw new InvalidDataException("source-drift");
            var assembly = typeof(ReleaseInputCommand).Assembly.Location;
            var receipt = new
            {
                schema = "release-inputs-v1", sourceSha = sha,
                inputManifestDigest = Digest(before), inputs = before,
                algorithmBinaryDigest = HashFile(assembly),
                sdkVersion = instance.Version.ToString(), evaluation.SdkDigest,
                target = new { framework = options["--framework"], configuration = options["--configuration"],
                    platform = options["--platform"], runtimeIdentifier = options["--rid"] },
                evaluationMode = "evaluation-only-without-restore",
                complete = false, fallback = "Full",
                evaluation.IncompleteReasons, evaluation.Projects,
            };
            Console.WriteLine(JsonSerializer.Serialize(receipt, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
            return 0;
        }
        catch (ArgumentException)
        {
            Console.Error.WriteLine("refused: invalid-release-input-options");
            return Program.EvaluationFailed;
        }
        catch (InvalidDataException ex)
        {
            Console.Error.WriteLine("refused: " + ex.Message);
            return Program.Refused;
        }
        catch (Exception ex)
        {
            // Exceptions may include expanded environment/property values. Retain the type only.
            Console.Error.WriteLine("refused: evaluation-failed:" + ex.GetType().Name);
            return Program.EvaluationFailed;
        }
        finally { Console.Error.WriteLine($"release-input phase=total elapsedMs={watch.ElapsedMilliseconds}"); }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ReleaseEvaluationReceipt Evaluate(string repo, string solution, SourceInput[] inputs, string sdk,
        IReadOnlyDictionary<string, string> options) => ReleaseInputEvaluation.Run(repo, solution, inputs, sdk, options);

    internal sealed record SourceInput(string Path, string Blob, string Sha256);

    private static SourceInput[] Capture(string repo, string sha)
    {
        if (!Regex.IsMatch(sha, "\\A[0-9a-f]{40}\\z", RegexOptions.CultureInvariant)
            || Git(repo, "rev-parse", "HEAD").Trim() != sha)
            throw new InvalidDataException("source-mismatch");
        if (Git(repo, "status", "--porcelain=v1", "-z", "--untracked-files=all").Length != 0)
            throw new InvalidDataException("source-drift");
        var inputs = new List<SourceInput>();
        long totalBytes = 0;
        foreach (var row in Git(repo, "ls-tree", "-r", "-z", "--full-tree", sha).Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            var tab = row.IndexOf('\t');
            var header = row[..tab].Split(' ');
            var path = row[(tab + 1)..];
            if (header.Length != 3 || header[0] is not ("100644" or "100755") || header[1] != "blob")
                throw new InvalidDataException("unsupported-git-entry:" + path);
            var full = Path.GetFullPath(path, repo);
            if (Relative(repo, full) != path) throw new InvalidDataException("unsupported-path");
            CheckRegularPath(repo, full);
            using var file = File.OpenRead(full);
            totalBytes += file.Length;
            if (file.Length > 256L * 1024 * 1024 || totalBytes > 1024L * 1024 * 1024)
                throw new InvalidDataException("input-resource-limit");
            using var blob = IncrementalHash.CreateHash(HashAlgorithmName.SHA1); // Git's object format, not the receipt digest.
            using var content = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            blob.AppendData(Encoding.ASCII.GetBytes("blob " + file.Length.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\0"));
            var buffer = new byte[65536];
            int count;
            while ((count = file.Read(buffer)) != 0) { blob.AppendData(buffer, 0, count); content.AppendData(buffer, 0, count); }
            if (Convert.ToHexStringLower(blob.GetHashAndReset()) != header[2])
                throw new InvalidDataException("source-drift");
            inputs.Add(new(path, header[2], Convert.ToHexStringLower(content.GetHashAndReset())));
        }
        return inputs.OrderBy(input => input.Path, StringComparer.Ordinal).ToArray();
    }

    internal static string Relative(string repo, string full)
    {
        var relative = Path.GetRelativePath(repo, full).Replace('\\', '/');
        if (Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith("../", StringComparison.Ordinal))
            throw new InvalidDataException("input-outside-repository");
        return relative;
    }

    internal static void CheckRegularPath(string root, string full)
    {
        for (var path = full; path != root; path = Path.GetDirectoryName(path)!)
        {
            if (path is null) throw new InvalidDataException("input-outside-repository");
            if (File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint))
                throw new InvalidDataException("linked-input");
        }
    }

    internal static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    internal static string Digest<T>(T value) =>
        Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase })));

    private static string Git(string repo, params string[] args)
    {
        var start = new ProcessStartInfo("git") { WorkingDirectory = repo, RedirectStandardOutput = true,
            RedirectStandardError = true, UseShellExecute = false };
        foreach (var key in new[] { "GIT_DIR", "GIT_WORK_TREE", "GIT_INDEX_FILE" }) start.Environment.Remove(key);
        foreach (var arg in args) start.ArgumentList.Add(arg);
        var watch = Stopwatch.StartNew();
        using var process = Process.Start(start) ?? throw new InvalidDataException("git-unavailable");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        try
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            process.WaitForExitAsync(deadline.Token).GetAwaiter().GetResult();
            Task.WhenAll(output, error).WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
            if (process.ExitCode != 0) throw new InvalidDataException("git-refused");
            return output.GetAwaiter().GetResult();
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
            }
            Console.Error.WriteLine($"release-input phase=git-{args[0]} elapsedMs={watch.ElapsedMilliseconds}");
        }
    }
}

internal sealed record ReleaseEvaluationReceipt(string SdkDigest, string[] IncompleteReasons, object[] Projects);
