// The seams the live lane's fingerprint, receipts and Claude version probe read through (W2 slice S22). This project
// references nothing, so git, the rc project graph and child processes come from the caller: the CLI (Antiphon.Scripts)
// binds git and the graph to the nightly core's TestSelectionGit and RcProjectSelection (LiveLaneNightlySources), the
// same reads the deleted PowerShell called; a test binds the same adapter or a fake. The graph is plain data, so a later
// owner of the change-impact graph (CI-2's tools/ChangeImpact) can replace the adapter without touching this project.
// Plain C#, framework only. ASCII only.
using System;
using System.Collections.Generic;

namespace Antiphon.LiveLane
{
    /// <summary>One git read: the exit code and stdout as text.</summary>
    public sealed class LiveLaneGitResult
    {
        public LiveLaneGitResult(int exit, string stdout)
        {
            Exit = exit;
            Stdout = stdout ?? "";
        }

        public int Exit { get; }
        public string Stdout { get; }
    }

    /// <summary>The git reads (TestSelectionGit in the CLI): hardened, read-only, at full commit ids.</summary>
    public interface ILiveLaneGit
    {
        /// <summary>git with the arguments in root; an exit outside allowedExits (or a timeout) throws, its message the reason.</summary>
        LiveLaneGitResult Run(string root, IReadOnlyList<string> arguments, IReadOnlyList<int> allowedExits);

        /// <summary>A blob's text by its immutable id (cat-file blob), strict UTF-8; throws when it cannot be read.</summary>
        string BlobText(string root, string blob);
    }

    /// <summary>One project of the graph: the projects it references and the repository files its items name.</summary>
    public sealed class LiveLaneGraphProject
    {
        public LiveLaneGraphProject(IReadOnlyList<string> references, IReadOnlyList<string> inputs)
        {
            References = references ?? Array.Empty<string>();
            Inputs = inputs ?? Array.Empty<string>();
        }

        public IReadOnlyList<string> References { get; }
        public IReadOnlyList<string> Inputs { get; }
    }

    /// <summary>
    /// The rc project graph of one commit as the fingerprint reads it (RcProjectSelection.Read): the projects by path, every
    /// path of the tree, the literal Import targets of each MSBuild document the reader parsed (keyed by path; a file the
    /// reader did not parse as XML has no entry, an import that does not resolve is left out), and the graph's diagnostics.
    /// </summary>
    public sealed class LiveLaneProjectGraph
    {
        public LiveLaneProjectGraph(IReadOnlyDictionary<string, LiveLaneGraphProject> projects, IReadOnlyCollection<string> paths,
            IReadOnlyDictionary<string, IReadOnlyList<string>> imports, IReadOnlyList<string> fallbackReasons)
        {
            Projects = projects ?? new Dictionary<string, LiveLaneGraphProject>(StringComparer.Ordinal);
            Paths = new HashSet<string>(paths ?? Array.Empty<string>(), StringComparer.Ordinal);
            Imports = imports ?? new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
            FallbackReasons = fallbackReasons ?? Array.Empty<string>();
        }

        public IReadOnlyDictionary<string, LiveLaneGraphProject> Projects { get; }
        public IReadOnlySet<string> Paths { get; }
        public IReadOnlyDictionary<string, IReadOnlyList<string>> Imports { get; }
        public IReadOnlyList<string> FallbackReasons { get; }
    }

    /// <summary>Reads the project graph of a commit; listing is the commit's own `ls-tree -r -z` output.</summary>
    public interface ILiveLaneProjectGraphReader
    {
        LiveLaneProjectGraph Read(string root, string sha, string listing);
    }

    /// <summary>The repository reads of one fingerprint or status: git and the project graph.</summary>
    public sealed class LiveLaneSources
    {
        public LiveLaneSources(ILiveLaneGit git, ILiveLaneProjectGraphReader graph)
        {
            Git = git ?? throw new ArgumentNullException(nameof(git));
            Graph = graph ?? throw new ArgumentNullException(nameof(graph));
        }

        public ILiveLaneGit Git { get; }
        public ILiveLaneProjectGraphReader Graph { get; }
    }

    /// <summary>A finished child process: its exit code (null when it did not start or ran out of time) and stdout.</summary>
    public sealed class LiveLaneProcessResult
    {
        public LiveLaneProcessResult(int? exit, string stdout, bool timedOut = false)
        {
            Exit = exit;
            Stdout = stdout ?? "";
            TimedOut = timedOut;
        }

        public int? Exit { get; }
        public string Stdout { get; }
        public bool TimedOut { get; }
    }

    /// <summary>Starts a child process and waits for it (bounded); never a shell. The CLI binds the real one, a test a fake.</summary>
    public interface ILiveLaneProcess
    {
        LiveLaneProcessResult Run(string fileName, IReadOnlyList<string> arguments, TimeSpan timeout);
    }
}
