// The live lane's repository reads (W2 slice S22): the seams of the isolated Antiphon.LiveLane project bound to the nightly
// core, the same reads the deleted live-lane-fingerprint.ps1 and live-lane-receipt.ps1 called. Git is TestSelectionGit (the
// hardened, read-only runner; its refusal reasons pass through as the message) and a blob's text RcProjectSelection.BlobText;
// the project graph is RcProjectSelection.Read, handed over as plain data (projects, paths, the literal imports of every
// parsed MSBuild document, the diagnostics). One instance holds one document cache keyed by blob id, as the PowerShell's
// -DocumentCache did: the CLI makes one per command, a test may share one across fingerprints. CI-2's change-impact graph
// (tools/ChangeImpact) can replace RcProjectSelection here without touching the lane's project. Antiphon.Tests compiles this file in.
// ASCII only.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Xml;
using Antiphon.LiveLane;
using Antiphon.Nightly;

namespace Antiphon.Scripts
{
    /// <summary>ILiveLaneGit and ILiveLaneProjectGraphReader over the nightly core.</summary>
    internal sealed class LiveLaneNightlySources : ILiveLaneGit, ILiveLaneProjectGraphReader
    {
        private readonly Hashtable _documents = new Hashtable(StringComparer.Ordinal);

        /// <summary>A fresh pair of seams (one document cache).</summary>
        internal static LiveLaneSources Create()
        {
            var nightly = new LiveLaneNightlySources();
            return new LiveLaneSources(nightly, nightly);
        }

        public LiveLaneGitResult Run(string root, IReadOnlyList<string> arguments, IReadOnlyList<int> allowedExits)
        {
            var result = TestSelectionGit.Run(root, arguments.ToArray(), allowedExits.ToArray());
            return new LiveLaneGitResult(result.Exit, result.Stdout);
        }

        public string BlobText(string root, string blob) => RcProjectSelection.BlobText(root, blob, _documents);

        public LiveLaneProjectGraph Read(string root, string sha, string listing)
        {
            var graph = RcProjectSelection.Read(root, sha, _documents, listing, null);
            var projects = new Dictionary<string, LiveLaneGraphProject>(StringComparer.Ordinal);
            foreach (var pair in graph.Projects) projects[pair.Key] = new LiveLaneGraphProject(pair.Value.References, pair.Value.Inputs);
            // Every MSBuild document the cache holds parsed for a path of this tree (the PowerShell looked the parsed document
            // up by the path's blob id): its literal imports, each resolved from the declaring file; one that does not
            // resolve is already a graph reason naming that file, and is left out.
            var imports = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
            foreach (var pair in graph.Blobs)
            {
                if (!(_documents[pair.Value] is XmlDocument document)) continue;
                var targets = new List<string>();
                foreach (XmlNode node in document.SelectNodes("//*[local-name()='Import']"))
                {
                    if (!(node is XmlElement element)) continue;
                    try { targets.Add(RcProjectSelection.Resolve(pair.Key, element.GetAttribute("Project"), pair.Key, false)); }
                    catch (RcProjectSelection.GraphFault) { }
                }
                imports[pair.Key] = targets;
            }
            return new LiveLaneProjectGraph(projects, graph.Paths, imports, graph.FallbackReasons);
        }
    }
}
