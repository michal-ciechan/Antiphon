// Unit 59: a program the cached launcher builds and publishes by the content identity of its build inputs. Cli
// (antiphon-scripts, scripts/lib/Antiphon.Scripts) is the launchers' own. The checkpoints command publishes the checkpoint
// tool (tools/Antiphon.Checkpoints) the same way (Antiphon.Nightly's CheckpointTool.Product): its inputs from its own
// project graph, its cache bin-launcher/antiphon-checkpoints/ with the same pointer, lease, lock, incremental build and
// pruning. A product's recipe is part of its identity, so two products never share a build. Unit 63: a product may name
// its own dotnet arguments, output folder and main file (the PowerShell entry, CliEntry.Product, is a single-file publish);
// the defaults are the build above. ASCII only.
#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;

namespace Antiphon.CliLauncher
{
    /// <summary>A program built and published by content identity under &lt;checkout&gt;/bin-launcher/&lt;CacheName&gt;/.</summary>
    public sealed class CliProduct
    {
        /// <summary>A product.</summary>
        /// <param name="cacheName">The cache folder under bin-launcher.</param>
        /// <param name="project">The project, relative to a checkout, '/' separators.</param>
        /// <param name="assembly">The project's assembly name: its build output is bin/&lt;assembly&gt;/release and its DLL &lt;assembly&gt;.dll.</param>
        /// <param name="title">What the failure lines call it ("the CLI").</param>
        /// <param name="prefix">The failure lines' prefix ("antiphon-cli").</param>
        /// <param name="sweepsLegacyLauncher">Whether pruning also removes the old launcher's folders (the CLI only).</param>
        /// <param name="template">The dotnet arguments with CliPublisher.ProjectToken and ArtifactsToken; null: CliPublisher.BuildTemplate.</param>
        /// <param name="output">The output folder the build leaves, relative to the artifacts path, '/' separators; null: bin/&lt;assembly&gt;/release.</param>
        /// <param name="mainFile">The file a complete output holds and the publisher returns; null: &lt;assembly&gt;.dll.</param>
        public CliProduct(string cacheName, string project, string assembly, string title, string prefix, bool sweepsLegacyLauncher = false,
            IReadOnlyList<string> template = null, string output = null, string mainFile = null)
        {
            if (string.IsNullOrWhiteSpace(cacheName)) throw new ArgumentException("a cache name is required", nameof(cacheName));
            if (string.IsNullOrWhiteSpace(project)) throw new ArgumentException("a project is required", nameof(project));
            if (string.IsNullOrWhiteSpace(assembly)) throw new ArgumentException("an assembly name is required", nameof(assembly));
            CacheName = cacheName;
            Project = project;
            Assembly = assembly;
            Title = title ?? assembly;
            Prefix = prefix ?? cacheName;
            SweepsLegacyLauncher = sweepsLegacyLauncher;
            Template = Array.AsReadOnly((template ?? CliPublisher.BuildTemplate).ToArray());
            Output = string.IsNullOrWhiteSpace(output) ? "bin/" + assembly + "/release" : output;
            DllName = string.IsNullOrWhiteSpace(mainFile) ? assembly + ".dll" : mainFile;
            Recipe = "dotnet " + string.Join(" ", Template) + "; copy " + Output + "; environment without MSBuild*";
        }

        /// <summary>antiphon-scripts, the CLI the launchers run.</summary>
        public static CliProduct Cli { get; } = new CliProduct("antiphon-scripts", CliInputs.EntryProject, "Antiphon.Scripts", "the CLI", "antiphon-cli", sweepsLegacyLauncher: true);

        /// <summary>The cache folder under bin-launcher.</summary>
        public string CacheName { get; }

        /// <summary>The project, relative to a checkout, '/' separators.</summary>
        public string Project { get; }

        /// <summary>The project's assembly name.</summary>
        public string Assembly { get; }

        /// <summary>The main file in a published folder (the DLL, or the entry's executable).</summary>
        public string DllName { get; }

        /// <summary>The dotnet arguments, with CliPublisher.ProjectToken and ArtifactsToken for the project and artifacts path.</summary>
        public IReadOnlyList<string> Template { get; }

        /// <summary>The build's output folder relative to the artifacts path, '/' separators.</summary>
        public string Output { get; }

        /// <summary>The dotnet arguments for a project and artifacts path.</summary>
        public IReadOnlyList<string> Arguments(string project, string artifacts) =>
            Template.Select(argument => argument == CliPublisher.ProjectToken ? project : argument == CliPublisher.ArtifactsToken ? artifacts : argument).ToArray();

        /// <summary>What the failure lines call it.</summary>
        public string Title { get; }

        /// <summary>The failure lines' prefix.</summary>
        public string Prefix { get; }

        /// <summary>Whether pruning also removes the old launcher's folders (bin-launcher/run-*, the in-place build's bin and obj).</summary>
        public bool SweepsLegacyLauncher { get; }

        /// <summary>
        /// The build recipe: the build arguments, the output it copies and the environment rule, one line. Part of the
        /// identity, so a build made by an older recipe (or for another product) is never taken for this one's.
        /// </summary>
        public string Recipe { get; }
    }
}
