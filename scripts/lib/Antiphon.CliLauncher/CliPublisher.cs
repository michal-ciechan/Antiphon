// N0 (unit 57): builds and publishes the CLI for the checkout's current inputs, once per identity, under the build lock.
//
// Ensure: under bin-launcher/antiphon-scripts/build.lock (FileShare.None; a caller waits, at most LockWait, then gets one
// line), compute the identity. A complete published folder for it is used as it is (the pointer moves to it). Otherwise
// the CLI is built into the shared folder build/ (dotnet build --artifacts-path, Release, quiet, two nodes, no reused
// nodes or compiler server, without the caller's MSBuild* environment, an empty stdin, the output to build.log), the
// identity is computed again (inputs that changed during the build are built again, at most Attempts times), and the
// output is copied to tmp-<guid>/ with its manifest and lease and renamed to <identity>/ in one step: a build never
// writes into a folder a process runs from. Then the pointer moves and old folders are pruned (CliCache.Prune).
//
// The shared build is incremental, which MSBuild decides by file times. It is used only when the build record shows that
// every input whose bytes changed since the last build is newer than that build's end, and no build file (anything but a
// .cs source) was added or removed; otherwise build/ is removed first and the build is clean. So a change whose time is
// older than the last build (a copy that keeps times) never reaches a published folder stale. Unit 59: Ensure publishes
// another product (CliProduct) the same way, in its own cache, with its own recipe and failure lines. ASCII only.
#nullable disable
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace Antiphon.CliLauncher
{
    /// <summary>One build of the CLI into the shared folder.</summary>
    public sealed class CliBuildRequest
    {
        /// <summary>The checkout (the build's working directory, so its global.json picks the SDK).</summary>
        public string Checkout { get; init; } = "";

        /// <summary>The CLI's project file.</summary>
        public string Project { get; init; } = "";

        /// <summary>The shared artifacts path.</summary>
        public string Artifacts { get; init; } = "";

        /// <summary>The dotnet arguments.</summary>
        public IReadOnlyList<string> Arguments { get; init; } = Array.Empty<string>();
    }

    /// <summary>What a build returned: its exit code and its output (stdout then stderr).</summary>
    public sealed class CliBuildResult
    {
        /// <summary>The exit code.</summary>
        public int ExitCode { get; init; }

        /// <summary>The build's output.</summary>
        public string Log { get; init; } = "";
    }

    /// <summary>How Ensure waits, builds and reports; the defaults are production's.</summary>
    public sealed class CliPublishOptions
    {
        /// <summary>Where the one failure line goes.</summary>
        public TextWriter Error { get; init; } = Console.Error;

        /// <summary>The clock.</summary>
        public Func<DateTime> UtcNow { get; init; } = () => DateTime.UtcNow;

        /// <summary>Waits between lock attempts and pointer retries.</summary>
        public Action<TimeSpan> Sleep { get; init; } = Thread.Sleep;

        /// <summary>How long a caller waits for another process's build.</summary>
        public TimeSpan LockWait { get; init; } = TimeSpan.FromMinutes(15);

        /// <summary>How often the lock is tried while it is held.</summary>
        public TimeSpan LockPoll { get; init; } = TimeSpan.FromMilliseconds(250);

        /// <summary>Builds tried when the inputs keep changing during the build.</summary>
        public int Attempts { get; init; } = 3;

        /// <summary>Published folders kept, the current one included (more when in use).</summary>
        public int Keep { get; init; } = 3;

        /// <summary>Runs one build (production: dotnet).</summary>
        public Func<CliBuildRequest, CliBuildResult> Build { get; init; } = CliPublisher.RunBuild;
    }

    /// <summary>Builds and publishes the CLI for a checkout's current inputs.</summary>
    public static class CliPublisher
    {
        /// <summary>Exit code when the CLI could not be built or started (the launchers' own).</summary>
        public const int Unavailable = 9;

        /// <summary>The project path in a product's dotnet arguments.</summary>
        public const string ProjectToken = "<project>";

        /// <summary>The artifacts path in a product's dotnet arguments.</summary>
        public const string ArtifactsToken = "<artifacts>";

        /// <summary>The dotnet arguments of a build, with the project and artifacts path as tokens.</summary>
        public static readonly IReadOnlyList<string> BuildTemplate = Array.AsReadOnly(new[]
        {
            "build", ProjectToken, "-c", "Release", "--artifacts-path", ArtifactsToken, "-nologo", "-v", "q", "-m:2",
            "-nodeReuse:false", "-p:UseSharedCompilation=false",
        });

        /// <summary>
        /// The CLI's build recipe (CliProduct.Cli.Recipe): the build arguments, the output it copies and the environment
        /// rule, one line. Part of the identity, so a build made by an older recipe is never taken for the current one. Kept
        /// as text (no hashing on the launch check's path).
        /// </summary>
        public static string Recipe => CliProduct.Cli.Recipe;

        /// <summary>The build arguments for a project and artifacts path.</summary>
        public static IReadOnlyList<string> BuildArguments(string project, string artifacts) =>
            BuildTemplate.Select(argument => argument == ProjectToken ? project : argument == ArtifactsToken ? artifacts : argument).ToArray();

        /// <summary>
        /// The CLI's DLL in the published folder of the checkout's current inputs, built and published first when there is
        /// none. Null after one line on options.Error when the checkout has no CLI, the lock was not had in time, the build
        /// failed or could not start, or the inputs kept changing during the build.
        /// </summary>
        public static string Ensure(string checkout, CliPublishOptions options) => Ensure(checkout, options, CliProduct.Cli);

        /// <summary>
        /// Ensure for a product: its DLL in the published folder of its current inputs, under its own cache, lock and
        /// recipe; the failure line starts with the product's prefix.
        /// </summary>
        public static string Ensure(string checkout, CliPublishOptions options, CliProduct product)
        {
            options ??= new CliPublishOptions();
            product ??= CliProduct.Cli;
            CliCache cache;
            try
            {
                cache = new CliCache(checkout, product);
                Directory.CreateDirectory(cache.Root);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException)
            {
                return Fail(options, product, "cannot create " + product.Title + " cache under " + checkout + ": " + e.Message);
            }
            using var held = Lock(cache, options);
            if (held == null) return null;
            try
            {
                return Publish(cache, options);
            }
            catch (CliInputException e)
            {
                return Fail(options, product, e.Message);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                return Fail(options, product, "cannot publish " + product.Title + " in " + cache.Root + ": " + e.Message);
            }
        }

        /// <summary>
        /// Whether the shared build may be incremental: there is a record of it from this recipe and system, its output
        /// exists, every input whose bytes changed (or that is new) is newer than the record's build end, and no build file
        /// (anything but a .cs source) was added or removed since.
        /// </summary>
        public static bool IncrementalIsSafe(IReadOnlyList<CliInput> inputs, CliManifest current, CliManifest record, bool outputExists)
        {
            if (record == null || !outputExists || record.BuildEnd == 0 || record.Os != current.Os || record.Recipe != current.Recipe) return false;
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var input in inputs)
            {
                names.Add(input.Name);
                var now = current.Files[input.Name];
                if (record.Files.TryGetValue(input.Name, out var before))
                {
                    if (string.Equals(before.Hash, now.Hash, StringComparison.Ordinal)) continue;
                }
                else if (!CliInputs.IsSource(input.Name)) return false;
                if (input.Ticks <= record.BuildEnd) return false;
            }
            return record.Files.Keys.All(name => names.Contains(name) || CliInputs.IsSource(name));
        }

        /// <summary>Production's build: dotnet with the arguments, in the checkout, without MSBuild* variables, stdin empty, output captured.</summary>
        public static CliBuildResult RunBuild(CliBuildRequest request)
        {
            var start = new ProcessStartInfo(DotnetHost())
            {
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = request.Checkout,
            };
            foreach (var argument in request.Arguments) start.ArgumentList.Add(argument);
            foreach (var name in start.Environment.Keys.Where(key => key.StartsWith("MSBuild", StringComparison.OrdinalIgnoreCase)).ToArray())
                start.Environment.Remove(name);
            start.Environment.Remove(CliGate.Variable);
            using var process = Process.Start(start) ?? throw new InvalidOperationException("dotnet did not start");
            process.StandardInput.Close();
            var errors = process.StandardError.ReadToEndAsync();
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            return new CliBuildResult { ExitCode = process.ExitCode, Log = output + errors.Result };
        }

        /// <summary>The dotnet host this process runs under, else dotnet from PATH.</summary>
        public static string DotnetHost()
        {
            var process = Environment.ProcessPath;
            return !string.IsNullOrEmpty(process) && string.Equals(Path.GetFileNameWithoutExtension(process), "dotnet", StringComparison.OrdinalIgnoreCase)
                ? process : "dotnet";
        }

        private static string Publish(CliCache cache, CliPublishOptions options)
        {
            var product = cache.Product;
            var project = Path.Combine(cache.Checkout, product.Project.Replace('/', Path.DirectorySeparatorChar));
            for (var attempt = 1; attempt <= Math.Max(1, options.Attempts); attempt++)
            {
                var record = CliManifest.Read(cache.RecordPath);
                var now = options.UtcNow();
                var inputs = CliInputs.Discover(cache.Checkout, product.Project);
                var before = CliIdentity.Compute(inputs, record, now, product.Recipe);
                var folder = cache.FolderOf(before.Name);
                if (CliCache.IsPublished(folder, before.Identity, product.DllName))
                {
                    // Built before (another process, or these sources earlier): point at it again.
                    CliCache.Touch(folder);
                    return Point(cache, before.Name, options);
                }
                if (!IncrementalIsSafe(inputs, before, record, File.Exists(Path.Combine(cache.BuildOutput, product.DllName))))
                {
                    CliCache.Discard(cache.BuildDirectory);
                    if (Directory.Exists(cache.BuildDirectory)) return Fail(options, product, "cannot remove the old build " + cache.BuildDirectory);
                }
                // An interrupted build leaves no record, so the next one is clean.
                if (File.Exists(cache.RecordPath)) File.Delete(cache.RecordPath);
                CliBuildResult build;
                try
                {
                    build = options.Build(new CliBuildRequest
                    {
                        Checkout = cache.Checkout, Project = project, Artifacts = cache.BuildDirectory,
                        Arguments = product.Arguments(project, cache.BuildDirectory),
                    });
                }
                catch (Exception e) when (e is Win32Exception || e is InvalidOperationException)
                {
                    return Fail(options, product, "dotnet did not start to build " + product.Title + ": " + e.Message);
                }
                var ended = options.UtcNow();
                before.WithBuildEnd(Math.Max(ended.Ticks, 1)).Write(cache.RecordPath);
                File.WriteAllText(cache.LogPath, build.Log ?? "", new UTF8Encoding(false));
                if (build.ExitCode != 0 || !File.Exists(Path.Combine(cache.BuildOutput, product.DllName)))
                {
                    var first = (build.Log ?? "").Replace("\r\n", "\n").Split('\n').Where(line => Regex.IsMatch(line, @"\berror\b", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)).Take(1).Select(line => line.Trim()).ToArray();
                    return Fail(options, product, product.Title + " did not build (exit " + build.ExitCode + ", log " + cache.LogPath + ")" + (first.Length > 0 ? ": " + first[0] : ""));
                }
                var after = CliIdentity.Compute(CliInputs.Discover(cache.Checkout, product.Project), before, options.UtcNow(), product.Recipe);
                if (!string.Equals(after.Identity, before.Identity, StringComparison.Ordinal)) continue;

                var temporary = Path.Combine(cache.Root, "tmp-" + Guid.NewGuid().ToString("N"));
                try
                {
                    Copy(cache.BuildOutput, temporary);
                    before.Write(Path.Combine(temporary, CliCache.StampName));
                    File.WriteAllBytes(Path.Combine(temporary, CliCache.LeaseName), Array.Empty<byte>());
                    if (Directory.Exists(folder))
                    {
                        // An incomplete folder under this name (never published): out of the way first.
                        CliCache.Discard(folder);
                        if (Directory.Exists(folder)) return Fail(options, product, "cannot replace the incomplete " + folder);
                    }
                    Directory.Move(temporary, folder);
                }
                finally
                {
                    if (Directory.Exists(temporary)) CliCache.Discard(temporary);
                }
                return Point(cache, before.Name, options);
            }
            return Fail(options, product, product.Title + "'s inputs changed during each of " + Math.Max(1, options.Attempts) + " builds; run it again");
        }

        private static string Point(CliCache cache, string name, CliPublishOptions options)
        {
            cache.WritePointer(name, options.Sleep);
            cache.Prune(name, options.Keep, options.UtcNow());
            return Path.Combine(cache.FolderOf(name), cache.Product.DllName);
        }

        private static FileStream Lock(CliCache cache, CliPublishOptions options)
        {
            var deadline = options.UtcNow() + options.LockWait;
            while (true)
            {
                try
                {
                    return new FileStream(cache.LockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                }
                catch (IOException)
                {
                    if (options.UtcNow() >= deadline)
                    {
                        Fail(options, cache.Product, "the build lock " + cache.LockPath + " was still held after " + Describe(options.LockWait));
                        return null;
                    }
                    options.Sleep(options.LockPoll);
                }
                catch (UnauthorizedAccessException e)
                {
                    Fail(options, cache.Product, "cannot open the build lock " + cache.LockPath + ": " + e.Message);
                    return null;
                }
            }
        }

        private static string Describe(TimeSpan wait) =>
            wait.TotalMinutes >= 1 ? Count((int)wait.TotalMinutes, "minute") : Count((int)Math.Ceiling(wait.TotalSeconds), "second");

        private static string Count(int value, string unit) => value + " " + unit + (value == 1 ? "" : "s");

        private static void Copy(string source, string target)
        {
            Directory.CreateDirectory(target);
            foreach (var directory in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
                Directory.CreateDirectory(Path.Combine(target, Path.GetRelativePath(source, directory)));
            foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
                File.Copy(file, Path.Combine(target, Path.GetRelativePath(source, file)), true);
        }

        private static string Fail(CliPublishOptions options, CliProduct product, string message)
        {
            options.Error.WriteLine(product.Prefix + ": " + message.Replace("\r", " ").Replace("\n", " "));
            options.Error.Flush();
            return null;
        }
    }
}
