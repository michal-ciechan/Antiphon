// CARD-1483 S5 (CARD-1528, CARD-1540): the built land-graph audit tool (tools/Antiphon.LandGraphAudit), kept current
// with its sources. It replaced scripts/lib/land-audit-tool.ps1 (deleted 2026-11-23, unit 11 of
// docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md); the land-graph-audit, land-graph-precheck and
// land-graph-land-refresh commands resolve the tool here and run it as a child process. The tool stays a separate
// program: it loads MSBuild, which this core never references.
//
// Resolve answers the tool's path, or the refusal text, or (noRebuild) that it would rebuild and changed nothing. An
// explicit tool path is used as given. Otherwise the default bin-land-audit DLL is used when its stamp
// (land-audit.source-hash beside it) equals the SHA-256 of the tool's sources: every .cs and .csproj under
// tools/Antiphon.LandGraphAudit and tools/Antiphon.LandGraphAudit.Core (bin, obj and bin-land-audit folders excluded)
// plus the repository's shared build files. A missing DLL, a missing stamp (a DLL built by hand) or a different hash
// rebuilds it once: under a lock file, into a fresh folder with its own artifacts path (never the projects' obj
// folders), and the finished folder replaces the old one only after the build succeeded. A failed build or a folder
// that cannot be replaced is a refusal ("built evaluator missing: ..."), so the caller refuses; nothing here approves
// anything. A checkout without the tool's project (a test fixture) keeps whatever DLL it was given.
// dotnet build runs on the host's launcher (Watched custody, output captured, in the checkout root), the lock and the
// folders on IFileSystem, the lock wait on the host's clock and sleep. Plain C#: no PowerShell types. ASCII only.
#nullable disable
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Antiphon.Nightly
{
    /// <summary>One resolution: the tool's Path, or the Error the caller prints and refuses with, or Rebuild (noRebuild only).</summary>
    public sealed record LandAuditToolResolution(string Path, string Error, bool Rebuild)
    {
        /// <summary>The tool to run.</summary>
        public static LandAuditToolResolution Found(string path) => new LandAuditToolResolution(path, null, false);

        /// <summary>The refusal ("built evaluator missing: ...").</summary>
        public static LandAuditToolResolution Missing(string error) => new LandAuditToolResolution(null, error, false);

        /// <summary>The default tool is missing or stale; a resolution with rebuilding allowed would rebuild it.</summary>
        public static LandAuditToolResolution Stale { get; } = new LandAuditToolResolution(null, null, true);
    }

    /// <summary>The land-graph audit tool's resolver and rebuild.</summary>
    public static class LandAuditTool
    {
        /// <summary>The tool's assembly.</summary>
        public const string DllName = "Antiphon.LandGraphAudit.dll";

        /// <summary>The stamp beside the default DLL: the source hash it was built from.</summary>
        public const string StampName = "land-audit.source-hash";

        /// <summary>The lock file under bin-land-audit that one rebuild holds.</summary>
        public const string LockName = "rebuild.lock";

        /// <summary>How long a resolution waits for another process's rebuild.</summary>
        public static readonly TimeSpan LockWait = TimeSpan.FromMinutes(15);

        /// <summary>How often the lock is tried while another process holds it.</summary>
        public static readonly TimeSpan LockPoll = TimeSpan.FromMilliseconds(250);

        /// <summary>The tool's two project folders, hashed in this order.</summary>
        public static readonly IReadOnlyList<string> SourceProjects = Array.AsReadOnly(new[] { "tools/Antiphon.LandGraphAudit", "tools/Antiphon.LandGraphAudit.Core" });

        /// <summary>The repository's shared build files, hashed when present.</summary>
        public static readonly IReadOnlyList<string> SharedBuildFiles = Array.AsReadOnly(new[]
        {
            "Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props", "global.json", "NuGet.config",
            "tools/Directory.Build.props", "tools/Directory.Build.targets",
        });

        private static readonly Regex SourceFile = new Regex(@"\.(cs|csproj)$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        private static readonly Regex BuildFolder = new Regex("/(bin|obj|bin-land-audit)/", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        private static readonly Regex ErrorLine = new Regex("error", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        /// <summary>The default tool folder of a checkout: tools/Antiphon.LandGraphAudit/bin-land-audit/Release/net10.0.</summary>
        public static string DefaultOutput(string repoRoot, IFileSystem files) =>
            files.Path.Combine(repoRoot, "tools", "Antiphon.LandGraphAudit", "bin-land-audit", "Release", "net10.0");

        /// <summary>
        /// The tool's source hash: the ordinal-sorted repository-relative paths (forward slashes) of its sources and the
        /// shared build files present, each written as its UTF-8 name and a line feed followed by its bytes, hashed with
        /// SHA-256 (upper-case hex).
        /// </summary>
        public static string SourceHash(string repoRoot, IFileSystem files)
        {
            if (files == null) throw new ArgumentNullException(nameof(files));
            repoRoot = Root(repoRoot, files);
            var entries = new List<string>();
            foreach (var project in SourceProjects)
            {
                var directory = files.Path.Combine(repoRoot, project);
                if (!files.Directory.Exists(directory)) continue;
                foreach (var file in files.Directory.GetFiles(directory, "*", SearchOption.AllDirectories))
                {
                    var relative = file.Substring(repoRoot.Length).Replace('\\', '/').TrimStart('/');
                    if (!SourceFile.IsMatch(relative)) continue;
                    if (BuildFolder.IsMatch("/" + relative + "/")) continue;
                    entries.Add(relative);
                }
            }
            foreach (var shared in SharedBuildFiles)
                if (files.File.Exists(files.Path.Combine(repoRoot, shared))) entries.Add(shared);
            entries.Sort(StringComparer.Ordinal);
            using var stream = new MemoryStream();
            foreach (var relative in entries)
            {
                var name = Encoding.UTF8.GetBytes(relative + "\n");
                stream.Write(name, 0, name.Length);
                var bytes = files.File.ReadAllBytes(files.Path.Combine(repoRoot, relative));
                stream.Write(bytes, 0, bytes.Length);
            }
            return Convert.ToHexString(SHA256.HashData(stream.ToArray()));
        }

        /// <summary>
        /// The tool to run for a checkout. toolPath non-empty: that file (full path) when it exists, else the refusal. The
        /// default DLL when current; with noRebuild, Stale where a rebuild is due (nothing changes); otherwise the rebuild
        /// under the lock (progress lines on host.Error). A failed rebuild leaves the old tool in place and refuses.
        /// </summary>
        public static LandAuditToolResolution Resolve(string repoRoot, string toolPath, bool noRebuild, NightlyHost host, IFileSystem files)
        {
            if (host == null) throw new ArgumentNullException(nameof(host));
            if (files == null) throw new ArgumentNullException(nameof(files));
            repoRoot = Root(repoRoot, files);
            if (!string.IsNullOrEmpty(toolPath))
            {
                var explicitPath = files.Path.GetFullPath(toolPath);
                return files.File.Exists(explicitPath) ? LandAuditToolResolution.Found(explicitPath) : LandAuditToolResolution.Missing("built evaluator missing: " + explicitPath);
            }
            var root = files.Path.Combine(repoRoot, "tools", "Antiphon.LandGraphAudit", "bin-land-audit");
            var output = DefaultOutput(repoRoot, files);
            var dll = files.Path.Combine(output, DllName);
            var stamp = files.Path.Combine(output, StampName);
            var project = files.Path.Combine(repoRoot, "tools", "Antiphon.LandGraphAudit", "Antiphon.LandGraphAudit.csproj");
            if (!files.File.Exists(project))
                return files.File.Exists(dll) ? LandAuditToolResolution.Found(dll) : LandAuditToolResolution.Missing("built evaluator missing: " + dll);
            var hash = SourceHash(repoRoot, files);
            if (Current(dll, stamp, hash, files)) return LandAuditToolResolution.Found(dll);
            if (noRebuild) return LandAuditToolResolution.Stale;

            files.Directory.CreateDirectory(root);
            var lockPath = files.Path.Combine(root, LockName);
            var deadline = host.UtcNow + LockWait;
            Stream held = null;
            while (held == null)
            {
                try
                {
                    held = files.File.Open(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                }
                catch (IOException)
                {
                    if (host.UtcNow > deadline) return LandAuditToolResolution.Missing("built evaluator missing: " + dll + " (rebuild lock timed out)");
                    host.Sleep(LockPoll);
                }
            }
            using (held)
            {
                // Another process may have rebuilt it while this one waited.
                if (Current(dll, stamp, hash, files)) return LandAuditToolResolution.Found(dll);
                host.Error.WriteLine("land audit tool: rebuilding (" + (files.File.Exists(dll) ? "sources changed" : "missing") + ")");
                var work = files.Path.Combine(root, "build-" + Guid.NewGuid().ToString("N"));
                var built = files.Path.Combine(work, "out");
                files.Directory.CreateDirectory(work);
                try
                {
                    var started = host.Clock.GetTimestamp();
                    var build = host.Launch(new NightlyLaunch("dotnet", BuildArguments(project, built, files.Path.Combine(work, "artifacts")), repoRoot, CaptureOutput: true));
                    if (build.ExitCode != 0 || !files.File.Exists(files.Path.Combine(built, DllName)))
                    {
                        var tail = string.Join(" | ", SplitLines(build.LogText).Where(line => ErrorLine.IsMatch(line)).Take(5));
                        return LandAuditToolResolution.Missing("built evaluator missing: " + dll + " (rebuild failed, exit " + build.ExitCode + "): " + tail);
                    }
                    files.File.WriteAllText(files.Path.Combine(built, StampName), hash, new UTF8Encoding(false));
                    string old = null;
                    if (files.Directory.Exists(output))
                    {
                        old = files.Path.Combine(root, "old-" + Guid.NewGuid().ToString("N"));
                        try
                        {
                            files.Directory.Move(output, old);
                        }
                        catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
                        {
                            return LandAuditToolResolution.Missing("built evaluator missing: " + dll + " (the built tool is in use and cannot be replaced: " + e.Message + ")");
                        }
                    }
                    files.Directory.CreateDirectory(files.Path.GetDirectoryName(output));
                    files.Directory.Move(built, output);
                    if (old != null) TryDelete(old, files);
                    var seconds = (int)Math.Round(host.Clock.GetElapsedTime(started).TotalSeconds, MidpointRounding.ToEven);
                    host.Error.WriteLine("land audit tool: rebuilt in " + seconds + " s");
                    return LandAuditToolResolution.Found(dll);
                }
                finally
                {
                    if (files.Directory.Exists(work)) TryDelete(work, files);
                }
            }
        }

        /// <summary>The rebuild's dotnet arguments: Release into its own output and artifacts path, quiet, two nodes, no reused nodes or compiler server.</summary>
        public static IReadOnlyList<string> BuildArguments(string project, string output, string artifacts) => new[]
        {
            "build", project, "-c", "Release", "-o", output, "--artifacts-path", artifacts, "-nologo", "-v", "q", "-m:2", "-nodeReuse:false", "-p:UseSharedCompilation=false",
        };

        /// <summary>
        /// The environment a land-graph child gets: the caller's environment without the names starting with MSBuild
        /// (ordinal, ignoring case), so a dotnet run or test parent cannot steer the evaluator's SDK discovery; null
        /// (inherit this process's environment) when the caller passed none.
        /// </summary>
        public static IReadOnlyDictionary<string, string> WithoutMsBuild(IReadOnlyDictionary<string, string> environment)
        {
            if (environment == null) return null;
            var child = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var pair in environment)
                if (!pair.Key.StartsWith("MSBuild", StringComparison.OrdinalIgnoreCase)) child[pair.Key] = pair.Value;
            return child;
        }

        /// <summary>The lines of a captured text (line feed or carriage return line feed), without a trailing empty one.</summary>
        internal static IEnumerable<string> SplitLines(string text)
        {
            var lines = (text ?? "").Replace("\r\n", "\n").Split('\n');
            var count = lines.Length > 0 && lines[lines.Length - 1].Length == 0 ? lines.Length - 1 : lines.Length;
            return lines.Take(count);
        }

        private static string Root(string repoRoot, IFileSystem files) => files.Path.GetFullPath(string.IsNullOrEmpty(repoRoot) ? "." : repoRoot).TrimEnd('\\', '/');

        private static bool Current(string dll, string stamp, string hash, IFileSystem files) =>
            files.File.Exists(dll) && files.File.Exists(stamp) && string.Equals(files.File.ReadAllText(stamp).Trim(), hash, StringComparison.Ordinal);

        private static void TryDelete(string directory, IFileSystem files)
        {
            try { files.Directory.Delete(directory, true); }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { }
        }
    }
}
