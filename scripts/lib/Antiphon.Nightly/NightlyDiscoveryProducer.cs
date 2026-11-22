// W2-nightly S15: the discovery producer (plan 2026-11-21-w2-nightly-port-slices.md, slice S15), the C# twin of
// Invoke-NightlyProduceDiscovery (scripts/lib/nightly-coverage.ps1). That file is a pinned live-lane input
// (scripts/test-live-lane-run.ps1 ProtectedInputs), so the PowerShell stays beside this until S27; nightly-tests.ps1 and
// live-lane-manifest.ps1 keep calling it until S17. The class is not named NightlyDiscovery: that name is the per-node
// passes in NightlyCore.cs, which is pinned too. Discovery is never execution evidence.
// Plain C#: no PowerShell types; the launch goes through the run's NightlyHost. ASCII only.
#nullable disable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Antiphon.Nightly
{
    /// <summary>A test assembly's --list-tests discovery, written as the antiphon-tunit-discovery-v1 document.</summary>
    public static class NightlyDiscoveryProducer
    {
        /// <summary>The listing's watchdog when the caller names none (Invoke-NightlyProduceDiscovery's default).</summary>
        public const int DefaultTimeoutMilliseconds = 600000;

        /// <summary>
        /// Invoke-NightlyProduceDiscovery: the test host (request.ExePath, after the non-blank argument prefix) runs
        /// --list-tests --no-progress --no-ansi --diagnostic --diagnostic-output-directory &lt;request.DiagnosticDirectory&gt; under
        /// Owned custody on the host; its newest diagnostic log is parsed as a discovery, hashed by the assembly (else the
        /// exe), and written to request.OutputPath. Returns the document. Refuses (NightlyRefusal, the PowerShell's texts) a
        /// blank exe, output or diagnostic directory, a timed-out listing, a missing diagnostic log and an empty discovery; a
        /// malformed log refuses as NightlyDiagnosticLog.Read does, and the output is then not written.
        /// </summary>
        public static object Produce(NightlyHost host, NightlyDiscoveryRequest request, int timeoutMilliseconds = DefaultTimeoutMilliseconds)
        {
            if (host == null) throw new ArgumentNullException(nameof(host));
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (string.IsNullOrWhiteSpace(request.ExePath)) throw new NightlyRefusal("missing assembly exe");
            if (string.IsNullOrWhiteSpace(request.OutputPath)) throw new NightlyRefusal("missing discovery output");
            if (string.IsNullOrWhiteSpace(request.DiagnosticDirectory)) throw new NightlyRefusal("missing diagnostic directory");
            Directory.CreateDirectory(request.DiagnosticDirectory);
            var arguments = new List<string>((request.ArgumentPrefix ?? new string[0]).Where(a => !string.IsNullOrWhiteSpace(a)))
            {
                "--list-tests", "--no-progress", "--no-ansi", "--diagnostic", "--diagnostic-output-directory", request.DiagnosticDirectory,
            };
            var run = host.Launch(NightlyChunkRunner.OwnedLaunch(request.ExePath, arguments, request.WorkingDirectory, timeoutMilliseconds, request.Environment, request.LogPath));
            if (run.TimedOut) throw new NightlyRefusal("discovery list-tests timed out");
            var diagnostic = NightlyDiagnosticLog.Latest(request.DiagnosticDirectory);
            if (string.IsNullOrWhiteSpace(diagnostic)) throw new NightlyRefusal("missing diagnostic discovery log in " + request.DiagnosticDirectory);
            var nodes = PsValue.AsArray(PsValue.Prop(NightlyDiagnosticLog.Read(diagnostic, "discovery"), "Nodes"));
            if (nodes.Length == 0) throw new NightlyRefusal("empty discovery");
            var hash = NightlyDiagnosticLog.AssemblyHash(string.IsNullOrWhiteSpace(request.AssemblyPath) ? request.ExePath : request.AssemblyPath);
            var document = NightlyDiagnosticLog.NewDiscoveryDocument(nodes, hash);
            NightlyDiagnosticLog.WriteDiscoveryDocument(request.OutputPath, document);
            return document;
        }
    }
}
