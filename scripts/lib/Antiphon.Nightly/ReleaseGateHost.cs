// CARD-0599 D-8/D-15, CARD-1311 and CARD-1339: the host of the release gate's publisher and promoter (ReleasePublish,
// ReleasePromote: antiphon-scripts publish-release and promote-release), which replaced scripts/publish-release.ps1 and
// scripts/promote-release.ps1 with unit 14 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md.
//
// Everything outside the process goes through this host: the output lines, the wall clock (the journals' reservedAt,
// publishedAt and updatedAt, and the cut time of a journal that has none), the release card readback (HTTP GET
// /api/cards/{id}; ReleaseAuthority.ReadReleaseCard in production) and, in a test, the seams a -SeamsPath file's
// $ReleaseGateSeams used to hand the scripts: git, GitHub, the publisher's clock and the journal writer. Production binds
// no seam: git on PATH (NightlyNativeGit.Run), gh on PATH on a WatchedLauncher, the system clock and the atomic writer.
// Review bf928242: seams are admitted only in explicit test mode (ANTIPHON_RELEASE_GATE_TEST_MODE exactly '1', read by
// the caller and passed in as the options' TestMode), and test mode only when both the git and the GitHub seams are
// bound, so a real publication never runs on an injected clock. Plain C#; no PowerShell. ASCII only.
#nullable disable
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Antiphon.Nightly
{
    /// <summary>
    /// A publisher's or promoter's test seams (the deleted scripts' $ReleaseGateSeams): each null member is production.
    /// </summary>
    public sealed class ReleaseGateSeams
    {
        /// <summary>The git adapter (the remote reads, the local tag, the exact tag push and the commit log).</summary>
        public NightlyGit Git { get; init; }

        /// <summary>The GitHub adapter (release-view, release-create, release-upload, release-publish).</summary>
        public ReleaseGateGitHub GitHub { get; init; }

        /// <summary>The publisher's clock (D-15: evidence may not post-date it); refused outside test mode.</summary>
        public Func<DateTime> UtcNow { get; init; }

        /// <summary>The publication and publish journals' writer (path, text); null is ReleaseGateJournal.WriteAtomic.</summary>
        public Action<string, string> JournalWrite { get; init; }
    }

    /// <summary>The release gate's host: output, wall clock, the release card readback and, in a test, the seams.</summary>
    public sealed class ReleaseGateHost
    {
        /// <summary>The [publish-release] and [promote-release] lines (Write-Host in the scripts).</summary>
        public required TextWriter Out { get; init; }

        /// <summary>The wall clock: TimeProvider.System in production.</summary>
        public required TimeProvider Clock { get; init; }

        /// <summary>The test seams; null in production.</summary>
        public ReleaseGateSeams Seams { get; init; }

        /// <summary>Reads the bound release card (base URL, card id); null is ReleaseAuthority.ReadReleaseCard (one HTTP GET, 15 s).</summary>
        public Func<string, string, CardReadback> CardReader { get; init; }

        /// <summary>The production host: no seam, the system clock, the HTTP card readback.</summary>
        public static ReleaseGateHost Production(TextWriter output) =>
            new ReleaseGateHost { Out = output ?? throw new ArgumentNullException(nameof(output)), Clock = TimeProvider.System };

        /// <summary>The wall clock's now in UTC.</summary>
        internal DateTime UtcNow => Clock.GetUtcNow().UtcDateTime;

        /// <summary>The card readback.</summary>
        internal Func<string, string, CardReadback> Reader => CardReader ?? ReleaseAuthority.ReadReleaseCard;

        /// <summary>
        /// Import-ReleaseGatePublisherSeams' admission: seams (bound here, or a PowerShell caller's seams file) outside test
        /// mode refuse 'seams-outside-test-mode'; test mode without both the git and the GitHub seam refuses
        /// 'test-mode-real-remote'; a named seams file that does not exist refuses ('missing seams'). '' admits.
        /// </summary>
        internal string Admission(bool testMode, string seamsPath)
        {
            if (!testMode && Seams != null) return "seams-outside-test-mode";
            return ReleaseGateRemote.PublisherAdmission(testMode, seamsPath ?? "", Seams?.Git, Seams?.GitHub);
        }

        /// <summary>The remote over the seams (each null is production); the publisher's clock is the seam's, else the wall clock.</summary>
        internal ReleaseGateRemote Remote(bool testMode)
        {
            Func<DateTime> clock = () => UtcNow;
            var seam = Seams?.UtcNow;
            if (seam != null)
                clock = () =>
                {
                    if (!testMode) throw new NightlyRefusal("release-gate clock seam refused outside test mode");
                    return seam().ToUniversalTime();
                };
            return new ReleaseGateRemote(Seams?.Git, Seams?.GitHub, clock);
        }

        /// <summary>The journals: stamped by the wall clock, written through the seam (else atomically).</summary>
        internal ReleaseGateJournal Journals() => new ReleaseGateJournal(Clock, Seams?.JournalWrite);

        /// <summary>
        /// A candidate document (complete-green.json, candidate.json, publish.json) as plain values: its top-level fields by
        /// name ignoring case (strings, booleans, integers as long, other numbers as double, arrays as object[], objects as
        /// JsonElement). Null when the file is missing or is not one JSON object (ConvertFrom-Json's refusals included:
        /// names that differ only in case). Strings are never reinterpreted as dates.
        /// </summary>
        public static IReadOnlyDictionary<string, object> ReadDocument(string path)
        {
            if (!(PlainJson.Read(path) is JsonElement element) || element.ValueKind != JsonValueKind.Object) return null;
            var fields = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in element.EnumerateObject())
                if (!fields.ContainsKey(property.Name)) fields[property.Name] = AuthorityValue.FromJson(property.Value);
            return fields;
        }

        /// <summary>[string]$document.name: the field as PowerShell's [string] reads it; "" when the document or field is absent.</summary>
        public static string Text(IReadOnlyDictionary<string, object> document, string name) =>
            document != null && document.TryGetValue(name, out var value) ? PsValue.Str(value) : "";
    }
}
