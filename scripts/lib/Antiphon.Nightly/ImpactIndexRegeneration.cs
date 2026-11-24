// CARD-1838: the regeneration of tests/derived-impact-index.json, the last Merge step before DerivedImpactIndexTests and
// the push. It replaced scripts/regenerate-derived-impact-index.ps1 (deleted 2026-11-24, unit 41 of
// docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md); the CLI runs it as
// `antiphon-scripts regenerate-derived-impact-index [--repo <dir>] [--no-build]`.
//
// The builder (DerivedImpactIndexBuilder) needs Roslyn, which this core does not carry, so its driver project
// (scripts/regenerate-derived-impact-index, which calls DerivedImpactIndexBuilder.Build and .Write) runs as a child, never
// as a reference. Two children, both dotnet, both in the checkout (its global.json picks the SDK), both sharing this
// process's streams, so the build log and the driver's own lines ("derived-impact windowsOnly count=...", then
// "derived-impact-index changed" or "derived-impact-index unchanged") pass through unchanged:
// - unless --no-build: dotnet build <driver csproj> -p:UseAppHost=false --nologo [-maxcpucount:<ANTIPHON_BUILD_SLOT_MAXCPUCOUNT>]
// - dotnet run --project <driver csproj> --no-build -p:UseAppHost=false -- <repo>
// The repository defaults to the checkout. It commits nothing. Exit 0 when the driver wrote the index (changed or
// unchanged); 1 when the driver project is missing, dotnet cannot start, the build failed or the driver failed. The
// server's land regeneration (server/Application/Services/DerivedImpactIndexRegen.cs) runs the same two children in a
// land worktree under its own build slot. Plain C#: no PowerShell types. ASCII only.
#nullable disable
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.IO.Abstractions;

namespace Antiphon.Nightly
{
    /// <summary>The regenerate-derived-impact-index command's options.</summary>
    public sealed class ImpactIndexRegenerationOptions
    {
        /// <summary>The CLI's usage line.</summary>
        public const string Usage = "regenerate-derived-impact-index [--repo <dir>] [--no-build]";

        private static readonly string[] Valued = { "--repo" };

        private static readonly string[] Switches = { "--no-build" };

        /// <summary>The checkout whose driver project is built and run (the CLI's own checkout): a full path.</summary>
        public string Checkout { get; init; } = "";

        /// <summary>The repository whose tests/derived-impact-index.json is regenerated: a full path, the checkout by default.</summary>
        public string Repo { get; init; } = "";

        /// <summary>Run the driver as it is already built: no dotnet build first.</summary>
        public bool NoBuild { get; init; }

        /// <summary>The build's -maxcpucount (ANTIPHON_BUILD_SLOT_MAXCPUCOUNT as the CLI read it, as given); blank for none.</summary>
        public string MaxCpuCount { get; init; } = "";

        /// <summary>
        /// The arguments after the command name. A blank --repo is the checkout; a relative one is resolved against
        /// currentDirectory. NightlyUsageException for a malformed line.
        /// </summary>
        public static ImpactIndexRegenerationOptions Parse(IReadOnlyList<string> arguments, string checkout, string currentDirectory, string maxCpuCount)
        {
            if (string.IsNullOrWhiteSpace(checkout)) throw new ArgumentException("checkout is required", nameof(checkout));
            var flags = NightlyArgs.Parse(arguments, Valued, Switches);
            var root = Path.GetFullPath(checkout);
            var repo = flags.Value("--repo", "");
            return new ImpactIndexRegenerationOptions
            {
                Checkout = root,
                Repo = string.IsNullOrWhiteSpace(repo) ? root : Path.GetFullPath(repo, string.IsNullOrWhiteSpace(currentDirectory) ? root : currentDirectory),
                NoBuild = flags.Switch("--no-build"),
                MaxCpuCount = maxCpuCount ?? "",
            };
        }
    }

    /// <summary>The derived impact index regeneration (CARD-1838): the driver built and run as children.</summary>
    public static class ImpactIndexRegeneration
    {
        /// <summary>The driver project, relative to a checkout.</summary>
        public const string ProjectRelative = "scripts/regenerate-derived-impact-index/Antiphon.DerivedImpactIndex.csproj";

        /// <summary>The driver project of a checkout: a full path.</summary>
        public static string ProjectPath(string checkout) =>
            Path.Combine(checkout, "scripts", "regenerate-derived-impact-index", "Antiphon.DerivedImpactIndex.csproj");

        /// <summary>The driver build: no apphost, and -maxcpucount when the slot's grant is not blank.</summary>
        public static IReadOnlyList<string> BuildArguments(string project, string maxCpuCount)
        {
            var arguments = new List<string> { "build", project, "-p:UseAppHost=false", "--nologo" };
            if (!string.IsNullOrWhiteSpace(maxCpuCount)) arguments.Add("-maxcpucount:" + maxCpuCount);
            return arguments;
        }

        /// <summary>One driver run over the repository, the driver as already built.</summary>
        public static IReadOnlyList<string> RunArguments(string project, string repo) =>
            new[] { "run", "--project", project, "--no-build", "-p:UseAppHost=false", "--", repo };

        /// <summary>
        /// The regenerate-derived-impact-index command: unless options.NoBuild the driver build, then the driver run, each a
        /// Watched launch on host (dotnet, in options.Checkout, this process's streams). Exit 0 when the driver wrote the
        /// index; 1 when the driver project is missing or a child failed or did not start (one line on host.Error).
        /// </summary>
        public static int Run(ImpactIndexRegenerationOptions options, NightlyHost host, IFileSystem files)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (host == null) throw new ArgumentNullException(nameof(host));
            if (files == null) throw new ArgumentNullException(nameof(files));
            var project = ProjectPath(options.Checkout);
            if (!files.File.Exists(project))
            {
                host.Error.WriteLine("regenerate-derived-impact-index: missing " + project);
                return 1;
            }
            if (!options.NoBuild && !Child(host, BuildArguments(project, options.MaxCpuCount), options.Checkout)) return 1;
            return Child(host, RunArguments(project, options.Repo), options.Checkout) ? 0 : 1;
        }

        private static bool Child(NightlyHost host, IReadOnlyList<string> arguments, string checkout)
        {
            NightlyLaunchResult result;
            try
            {
                result = host.Launch(new NightlyLaunch("dotnet", arguments, checkout));
            }
            catch (Exception e) when (e is Win32Exception || e is InvalidOperationException)
            {
                host.Error.WriteLine("regenerate-derived-impact-index: dotnet did not start: " + e.Message);
                return false;
            }
            return result.ExitCode == 0;
        }
    }
}
