// The two committed files the live-lane captures produce (CARD-1545 S1a), compiled in W2-nightly S24 from
// live-lane-manifest.ps1 (Update-LiveLaneManifestBindings, Write-LiveLaneDiscoveryFixture) and the BindManifest and
// WriteFixture cases of scripts/test-live-lane-manifest.ps1 (now "antiphon-scripts live-lane-bind-manifest" and
// "live-lane-write-fixture"). Both read the roster captures <capture>/<project>.discovery.json that the roster rows
// leave (LiveLaneDiscovery.Run) and write as ConvertTo-Json -Depth 8 | Set-Content -Encoding utf8 wrote: the nodes
// and the manifest's gates and additiveContract rows as they were read (LiveLaneJson.cs). Plain C#. ASCII-only.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Antiphon.Nightly
{
    public static class LiveLaneManifestFiles
    {
        /// <summary>The census fixture ($script:LiveLaneFixtureRel).</summary>
        public const string FixtureRel = "tests/fixtures/live-lanes/full-discovery-before.json";

        /// <summary>The two roster projects and the S1a capture rows that discover them (CP-27, CP-28).</summary>
        private static readonly (string Project, string Cp)[] Captures =
        {
            (LiveLaneRoster.TestsProject, "CP-27"),
            (LiveLaneRoster.PtyProject, "CP-28"),
        };

        /// <summary>&lt;repo&gt;/.antiphon/live-lane-capture, the roster rows' capture directory.</summary>
        public static string DefaultCaptureDir(string repoRoot) => Path.Combine(Path.GetFullPath(repoRoot), ".antiphon", "live-lane-capture");

        /// <summary>
        /// Update-LiveLaneManifestBindings: rewrites tests/live-lanes.json (or a non-blank outputPath) with each core row bound to its
        /// one discovered node in the captures (uid and categories, bound true); gates and additiveContract are carried as
        /// read. Refuses capture-missing &lt;project&gt; and bind-miss &lt;method&gt; count=&lt;n&gt; before writing. Returns the path written.
        /// </summary>
        public static string BindManifest(string repoRoot, string captureDir = null, string outputPath = null)
        {
            repoRoot = Path.GetFullPath(repoRoot);
            var capture = string.IsNullOrWhiteSpace(captureDir) ? DefaultCaptureDir(repoRoot) : captureDir;
            using var manifest = LiveLaneGateScan.ReadManifest(repoRoot);
            var byProject = new Dictionary<string, List<LiveLaneNode>>(StringComparer.OrdinalIgnoreCase);
            foreach (var (project, _) in Captures)
            {
                var path = Path.Combine(capture, project + ".discovery.json");
                if (!File.Exists(path)) throw new InvalidOperationException("capture-missing " + project);
                using var doc = LiveLaneJson.ReadFile(path);
                var nodes = new List<LiveLaneNode>();
                foreach (var node in LiveLaneJson.Rows(doc.RootElement, "nodes")) nodes.Add(LiveLaneNode.FromJson(node));
                byProject[project] = nodes;
            }
            var core = new List<LiveLaneJsonObject>();
            foreach (var row in LiveLaneJson.Rows(manifest.RootElement, "core"))
            {
                var entry = LiveLaneEntry.FromJson(row);
                var identity = new LiveLaneIdentity(entry.Project, entry.Namespace, entry.Class, entry.Method, new List<string>(entry.Arguments).ToArray());
                byProject.TryGetValue(entry.Project, out var discovered);
                var hits = LiveLaneRoster.MatchingNodes(discovered, identity);
                if (hits.Count != 1) throw new InvalidOperationException("bind-miss " + identity.Method + " count=" + hits.Count);
                core.Add(new LiveLaneJsonObject
                {
                    { "project", identity.Project },
                    { "namespace", identity.Namespace },
                    { "class", identity.Class },
                    { "method", identity.Method },
                    { "arguments", identity.Arguments },
                    { "uid", hits[0].Uid },
                    { "categories", hits[0].Categories },
                    { "bound", true },
                });
            }
            var bound = new LiveLaneJsonObject
            {
                { "format", "antiphon-live-lanes-v1" },
                { "core", core },
                { "gates", LiveLaneJson.Rows(manifest.RootElement, "gates") },
                { "additiveContract", LiveLaneJson.Rows(manifest.RootElement, "additiveContract") },
            };
            var dest = string.IsNullOrWhiteSpace(outputPath) ? Path.Combine(repoRoot, "tests", "live-lanes.json") : outputPath;
            LiveLaneJson.WriteFile(dest, bound, finalNewLine: true);
            return dest;
        }

        /// <summary>
        /// Write-LiveLaneDiscoveryFixture: the census fixture (antiphon-live-lane-discovery-fixture-v1) at FixtureRel (or a
        /// non-blank outputPath, its directory made): producing.sourceSha, each capture's project, row and assembly hash, then the
        /// Antiphon.Tests nodes followed by the PTY nodes, as captured. A missing capture refuses. Returns the path written.
        /// </summary>
        public static string WriteFixture(string repoRoot, string captureDir, string sourceSha, string outputPath = null)
        {
            repoRoot = Path.GetFullPath(repoRoot);
            var capture = string.IsNullOrWhiteSpace(captureDir) ? DefaultCaptureDir(repoRoot) : captureDir;
            var docs = new List<JsonDocument>();
            try
            {
                var projects = new List<LiveLaneJsonObject>();
                var nodes = new List<JsonElement>();
                foreach (var (project, cp) in Captures)
                {
                    var doc = LiveLaneJson.ReadFile(Path.Combine(capture, project + ".discovery.json"));
                    docs.Add(doc);
                    projects.Add(new LiveLaneJsonObject { { "project", project }, { "cp", cp }, { "assemblyHash", LiveLaneJson.Field(doc.RootElement, "assemblyHash") } });
                    nodes.AddRange(LiveLaneJson.Rows(doc.RootElement, "nodes"));
                }
                var fixture = new LiveLaneJsonObject
                {
                    { "format", "antiphon-live-lane-discovery-fixture-v1" },
                    { "producing", new LiveLaneJsonObject { { "sourceSha", sourceSha ?? "" }, { "projects", projects } } },
                    { "nodes", nodes },
                };
                var dest = Path.GetFullPath(string.IsNullOrWhiteSpace(outputPath) ? Path.Combine(repoRoot, FixtureRel) : outputPath);
                Directory.CreateDirectory(Path.GetDirectoryName(dest));
                LiveLaneJson.WriteFile(dest, fixture, finalNewLine: true);
                return dest;
            }
            finally
            {
                foreach (var doc in docs) doc.Dispose();
            }
        }
    }
}
