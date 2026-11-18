using System;
using System.Management.Automation;

namespace Antiphon.Nightly
{
    /// <summary>
    /// The pinned-text branch of ConvertFrom-NightlyDiagnosticLog in one call. PowerShell 7.4+ hands every
    /// argument of a .NET method call to AMSI, so each call that takes the whole diagnostic text costs a scan
    /// of it (~0.6 s for a 9 MB discovery diag); the marker count and the record loop share this one call.
    /// </summary>
    public static class DiagScan
    {
        private const string DiscoveredMarker = "DiscoveredTestNodeStateProperty {";

        /// <summary>
        /// Count of [regex]::Matches(text, 'DiscoveredTestNodeStateProperty \{') (case-sensitive, so ordinal):
        /// the marker is a literal that cannot overlap itself, so successive ordinal hits are the same matches.
        /// </summary>
        public static int DiscoveredMarkerCount(string text)
        {
            text = text ?? "";
            var count = 0;
            var at = text.IndexOf(DiscoveredMarker, 0, StringComparison.Ordinal);
            while (at >= 0)
            {
                count++;
                at = text.IndexOf(DiscoveredMarker, at + DiscoveredMarker.Length, StringComparison.Ordinal);
            }
            return count;
        }

        /// <summary>
        /// NightlyDiagnostics.ParseRecords plus a DiscoveredCount property (the raw marker count the caller
        /// checks the parse against), so the text crosses into .NET once.
        /// </summary>
        public static PSObject ParseRecords(string text, string kind, string path)
        {
            var count = DiscoveredMarkerCount(text);
            var parsed = NightlyDiagnostics.ParseRecords(text, kind, path);
            parsed.Properties.Add(new PSNoteProperty("DiscoveredCount", count));
            return parsed;
        }
    }
}
