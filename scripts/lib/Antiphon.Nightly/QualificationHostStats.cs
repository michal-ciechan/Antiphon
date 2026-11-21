// W2-nightly S03: the CARD-1514 A4 host-stats library, ported from scripts/lib/qualification-host-stats.ps1 (deleted;
// plan 2026-11-21-w2-nightly-port-slices.md). NOT WIRED (deferred to a later slice): production qualification does not
// call this type and must not load a probe DLL from the live build output. Entry conditions before any later wiring:
// sample through the real SampleCount 0 path, pass TimingRoot and key rows by run and attempt, and bind the probe after
// the qualification build from an immutable copy. Desktop 5.1 and .NET 8 never load the probe.
// RcFullTimingContractTests.HostSamplesPreserveUnknownAndStop holds the contract. Plain C#: no PowerShell types. ASCII only.
#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace Antiphon.Nightly
{
    /// <summary>One host sample. A null measure is unknown, never zero. Reason is "sample-failed" for a probe that threw.</summary>
    public sealed record QualificationHostSample(string At, double? CpuPercent, int? Cores, long? MemoryAvailableBytes,
        long? MemoryCommittedBytes, double? DiskQueue, string Reason = "");

    /// <summary>Test-QualificationHostRuntime: the runtime decision. Version alone is not measurement admission.</summary>
    public sealed record QualificationHostRuntimeGate(string Decision, bool MeasurementAdmission, bool DiagnosticComplete, string LoadReason);

    /// <summary>Invoke-QualificationHostSampler: the samples taken before the probe stopped.</summary>
    public sealed record QualificationHostSampling(IReadOnlyList<QualificationHostSample> Samples);

    /// <summary>New-QualificationHostStatsProvider: the runtime decision, admission, the loaded probe (null unless admitted) and why a load failed.</summary>
    public sealed record QualificationHostStatsProvider(string Decision, bool MeasurementAdmission, bool DiagnosticComplete, object Probe, string LoadReason);

    /// <summary>The host-stats provider gate and sampler (not wired).</summary>
    public static class QualificationHostStats
    {
        private static readonly Regex DotNetMajor = new Regex(@"\.NET ([0-9]+)");

        /// <summary>
        /// Test-QualificationHostRuntime: compatible only on the Core edition (any case), a framework description that does
        /// not name Framework, and .NET 9 or later. The loader is never invoked here; admission stays false either way.
        /// </summary>
        public static QualificationHostRuntimeGate TestRuntime(string edition, string frameworkDescription, Func<object> loader)
        {
            var major = -1;
            var framework = frameworkDescription ?? "";
            var match = DotNetMajor.Match(framework);
            if (match.Success) major = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
            var core = string.Equals(edition ?? "", "Core", StringComparison.OrdinalIgnoreCase);
            var frameworkHost = framework.IndexOf("Framework", StringComparison.OrdinalIgnoreCase) >= 0;
            var compatible = core && !frameworkHost && major >= 9;
            _ = loader;
            return new QualificationHostRuntimeGate(compatible ? "compatible" : "incompatible-runtime", false, true, "");
        }

        /// <summary>
        /// Invoke-QualificationHostSampler: up to sampleCount probe reads. A probe that throws gives one sample-failed row
        /// (every measure unknown); a null sample stops the sampler. ownerCompleted runs once after the last read.
        /// </summary>
        public static QualificationHostSampling Sample(Func<QualificationHostSample> probe, int sampleCount = 2, Action ownerCompleted = null)
        {
            var samples = new List<QualificationHostSample>();
            var index = 0;
            while (index < sampleCount)
            {
                index++;
                QualificationHostSample sample;
                try { sample = probe(); }
                catch (Exception) { sample = new QualificationHostSample("", null, null, null, null, null, "sample-failed"); }
                if (sample == null) break;
                samples.Add(sample);
            }
            ownerCompleted?.Invoke();
            return new QualificationHostSampling(samples);
        }

        /// <summary>
        /// New-QualificationHostStatsProvider: the runtime gate first (edition defaults to Core, the edition every .NET host
        /// of this type runs on; the description to this runtime's). An incompatible runtime never calls the loader. A
        /// compatible one calls it only with an assembly path; a loader that throws is wrapped as loader-failed, and only a
        /// loaded probe is admitted.
        /// </summary>
        public static QualificationHostStatsProvider NewProvider(string assemblyPath, Func<object> loader, string edition = null, string frameworkDescription = null)
        {
            if (string.IsNullOrWhiteSpace(edition)) edition = "Core";
            if (string.IsNullOrWhiteSpace(frameworkDescription)) frameworkDescription = RuntimeInformation.FrameworkDescription;
            var gate = TestRuntime(edition, frameworkDescription, loader);
            if (gate.Decision != "compatible") return new QualificationHostStatsProvider(gate.Decision, false, true, null, "");
            object loaded = null;
            var loadReason = "";
            if (loader != null && !string.IsNullOrWhiteSpace(assemblyPath))
            {
                try { loaded = loader(); }
                catch (Exception)
                {
                    loaded = null;
                    loadReason = "loader-failed";
                }
            }
            return new QualificationHostStatsProvider("compatible", loaded != null, true, loaded, loadReason);
        }
    }
}
