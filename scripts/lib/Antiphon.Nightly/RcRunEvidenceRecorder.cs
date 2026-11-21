// CARD-1836 S6b. The recording policy around RcRunEvidence for one nightly run (moved from rc-run-evidence.ps1):
// the off switch, the suppression of seam-driven fixture runs, the evidence root, strict refusal or one warning
// line per distinct reason, and one discovery record per suite. Recording grants no gate credit. Plain C#.
using System;
using System.Collections.Generic;
using System.IO;

namespace Antiphon.Nightly
{
    public sealed class RcRunEvidenceRecorder
    {
        readonly bool strict;
        readonly Action<string> writeLine;
        // The PowerShell kept these in @{} tables, whose keys ignore case.
        readonly HashSet<string> warned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        readonly HashSet<string> discovered = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <param name="evidenceSwitch">ANTIPHON_RC_RUN_EVIDENCE: off records nothing; record records even under seams.</param>
        /// <param name="seamsInjected">The run's StartProcess or Git seam is set (a fixture run): recording is suppressed.</param>
        /// <param name="strict">A refusal throws instead of warning and answering unproven.</param>
        /// <param name="writeLine">Takes each warning line ("rc-run-evidence: reason"); null drops them.</param>
        public RcRunEvidenceRecorder(string evidenceSwitch, bool seamsInjected, bool strict, Action<string> writeLine)
        {
            Mode = ResolveMode(evidenceSwitch, seamsInjected);
            this.strict = strict;
            this.writeLine = writeLine;
        }

        /// <summary>on, off or suppressed.</summary>
        public string Mode { get; }

        public bool Recording => string.Equals(Mode, "on", StringComparison.Ordinal);

        public static string ResolveMode(string evidenceSwitch, bool seamsInjected)
        {
            if (string.Equals(evidenceSwitch, "off", StringComparison.OrdinalIgnoreCase)) return "off";
            if (string.Equals(evidenceSwitch, "record", StringComparison.OrdinalIgnoreCase)) return "on";
            return seamsInjected ? "suppressed" : "on";
        }

        /// <summary>The evidence root: the given one, else logRoot/rc-run-evidence, else "" (which refuses).</summary>
        public static string Root(string evidenceRoot, string logRoot)
        {
            if (!string.IsNullOrWhiteSpace(evidenceRoot)) return evidenceRoot;
            if (!string.IsNullOrWhiteSpace(logRoot)) return Path.Combine(logRoot, "rc-run-evidence");
            return "";
        }

        public RcRunEvidenceRecord SourceSnapshot(string evidenceRoot, string logRoot, string repository, string[] inputPaths, string expectedSha) =>
            Record(() => RcRunEvidence.SourceSnapshot(Root(evidenceRoot, logRoot), repository, inputPaths, expectedSha));

        public RcRunEvidenceRecord Build(string evidenceRoot, string logRoot, string suite, string command, string outputDirectory, string entry,
            string sourceDigest, string repository, string sha) =>
            Record(() => RcRunEvidence.Build(Root(evidenceRoot, logRoot), suite, command, outputDirectory, entry, sourceDigest, repository, sha));

        public RcRunEvidenceRecord Discovery(string evidenceRoot, string logRoot, string suite, string discoveryPath, bool carry, bool skip) =>
            Record(() => RcRunEvidence.Discovery(Root(evidenceRoot, logRoot), suite, discoveryPath, carry, skip));

        /// <summary>The suite's first discovery only: a later call for the same suite, or a blank suite, records nothing (null).</summary>
        public RcRunEvidenceRecord DiscoveryOnce(string evidenceRoot, string logRoot, string suite, string discoveryPath)
        {
            if (string.IsNullOrWhiteSpace(suite) || !discovered.Add(suite)) return null;
            return Discovery(evidenceRoot, logRoot, suite, discoveryPath, false, false);
        }

        public RcRunEvidenceRecord Launch(string evidenceRoot, string logRoot, string suite, string chunk, string outputDirectory, string[] arguments,
            string buildDigest, string repository, string expectedSha) =>
            Record(() => RcRunEvidence.Launch(Root(evidenceRoot, logRoot), suite, chunk, outputDirectory, arguments, buildDigest, repository, expectedSha));

        public RcRunEvidenceRecord ChunkRevision(string evidenceRoot, string logRoot, string suite, string chunk, string outcome, string reason, bool interrupt) =>
            Record(() => RcRunEvidence.ChunkRevision(Root(evidenceRoot, logRoot), suite, chunk, outcome, interrupt, reason));

        public RcRunEvidenceRecord AssertInputs(string evidenceRoot, string logRoot, string repository, string expectedSha) =>
            Record(() => RcRunEvidence.AssertInputs(Root(evidenceRoot, logRoot), repository, expectedSha));

        public RcRunEvidenceRecord Read(string evidenceRoot, string logRoot) =>
            Record(() => RcRunEvidence.Read(Root(evidenceRoot, logRoot)));

        RcRunEvidenceRecord Record(Func<RcRunEvidenceRecord> body)
        {
            if (!Recording) return RcRunEvidenceRecord.Idle(Mode);
            try
            {
                return body();
            }
            catch (Exception ex) when (!strict)
            {
                Warn(Innermost(ex).Message);
                return RcRunEvidenceRecord.Idle("unproven");
            }
        }

        static Exception Innermost(Exception ex)
        {
            while (ex.InnerException != null && !string.IsNullOrEmpty(ex.InnerException.Message)) ex = ex.InnerException;
            return ex;
        }

        /// <summary>The reason's first line, trimmed, at most 120 characters; each distinct text once per recorder.</summary>
        void Warn(string reason)
        {
            var text = string.IsNullOrWhiteSpace(reason) ? "unproven" : reason;
            text = text.Split('\r', '\n')[0].Trim();
            if (text.Length > 120) text = text.Substring(0, 120);
            if (!warned.Add(text) || writeLine == null) return;
            try { writeLine("rc-run-evidence: " + text); }
            catch (Exception) { }
        }
    }
}
