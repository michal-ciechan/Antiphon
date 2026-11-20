// Small text, path and digest helpers of the test-phase evidence (moved from test-phase-evidence.ps1).
using System;
using System.IO;
using System.Management.Automation;
using System.Security.Cryptography;
using System.Text;

namespace Antiphon.Nightly
{
    public static class TestPhaseText
    {
        /// <summary>ConvertTo-TestPhaseMarkdownCell: backslash and pipe escaped, line breaks as spaces.</summary>
        public static string MarkdownCell(object value)
        {
            var text = value == null ? "" : PsValue.Str(value);
            return text.Replace("\\", "\\\\").Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ");
        }

        /// <summary>
        /// ConvertTo-TestPhaseRelativePath: path below logRoot with forward slashes; "" when either is blank or the path
        /// is not under the root.
        /// </summary>
        public static string RelativePath(string logRoot, string path)
        {
            if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(logRoot)) return "";
            var root = Path.GetFullPath(logRoot).TrimEnd('\\', '/');
            var full = Path.GetFullPath(path);
            if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return "";
            return full.Substring(root.Length).TrimStart('\\', '/').Replace('\\', '/');
        }

        /// <summary>ConvertTo-TestPhaseB64: base64 of the UTF-8 bytes.</summary>
        public static string ToB64(string text) => Convert.ToBase64String(Encoding.UTF8.GetBytes(text ?? ""));

        /// <summary>ConvertFrom-TestPhaseB64: the UTF-8 text of base64; "" for blank.</summary>
        public static string FromB64(string text) => string.IsNullOrEmpty(text) ? "" : Encoding.UTF8.GetString(Convert.FromBase64String(text));

        /// <summary>
        /// Get-TestPhaseSha256File: lower-case hex SHA-256 of the file as a reader sees it - a queued write while
        /// evidence writes are deferred, else the disk, read while another process may still write it.
        /// </summary>
        public static string Sha256File(string path)
        {
            if (EvidenceFiles.Buffered) return EvidenceFiles.Sha256(path);
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var sha = SHA256.Create())
            {
                var sb = new StringBuilder(64);
                foreach (var b in sha.ComputeHash(stream)) sb.Append(b.ToString("x2", System.Globalization.CultureInfo.InvariantCulture));
                return sb.ToString();
            }
        }

        /// <summary>Test-TestPhaseOrdinalEqual: [string] values (null as "") equal ordinally.</summary>
        public static bool OrdinalEqual(string left, string right) => string.Equals(left ?? "", right ?? "", StringComparison.Ordinal);

        /// <summary>
        /// Test-TestPhaseRecordDigest: the record's "digest" is the SHA-256 of the record text with that digest emptied.
        /// </summary>
        public static bool RecordDigest(string text)
        {
            text = text ?? "";
            const string needle = "\"digest\":\"";
            var start = text.IndexOf(needle, StringComparison.Ordinal);
            if (start < 0) return false;
            var hexStart = start + needle.Length;
            var end = text.IndexOf('"', hexStart);
            if (end < 0) return false;
            var hex = text.Substring(hexStart, end - hexStart).ToLowerInvariant();
            var probe = text.Remove(start, end + 1 - start).Insert(start, "\"digest\":\"\"");
            return TestPhaseRecords.Sha256Text(probe) == hex;
        }

        /// <summary>Get-TestPhaseEvidenceKey: suite|testId when the header names a suite, else testId.</summary>
        public static string EvidenceKey(object header, string testId)
        {
            var suite = header != null && PSObject.AsPSObject(header).Properties["suite"] != null ? PsValue.Str(PsValue.Prop(header, "suite")) : "";
            return string.IsNullOrWhiteSpace(suite) ? testId ?? "" : suite + "|" + (testId ?? "");
        }
    }
}
