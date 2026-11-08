// Stream readers of test-phase-evidence.ps1 (TRX timing scanner and JSONL readers). Also compiled on its own
// by Add-Type under Windows PowerShell 5.1: keep it to the C# 5 / .NET Framework surface.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;

namespace Antiphon.TestPhase {
    public sealed class TrxTimingRow {
        public string TestId;
        public string Outcome;
        public string Name;
        public string Duration;
        public string StdOut;
    }

    public sealed class TrxTimingRead {
        public bool Complete;
        public List<TrxTimingRow> Rows = new List<TrxTimingRow>();
    }

    public sealed class TrxTimingStats {
        public bool Complete;
        public int Count;
        public double P50;
        public double P95;
        public double Max;
        public string[] SlowNames = new string[0];
        public double[] SlowSeconds = new double[0];
    }

    public sealed class JsonlTimingRead {
        public bool Complete;
        public List<string> Lines = new List<string>();
    }

    public sealed class JsonlHeaderRead {
        public bool Refused;
        public string Line;
    }

    public static class TrxTimingScanner {
        public static TrxTimingRead Read(Stream stream, Stopwatch watch, int deadlineMs, int maxTagBytes, int maxStdoutBytes) {
            var read = new TrxTimingRead();
            var scan = Scan(stream, watch, deadlineMs, maxTagBytes, maxStdoutBytes, true);
            read.Complete = scan.Complete;
            if (!scan.Complete) return read;
            for (int i = 0; i < scan.Rows.Count; i++) {
                var row = scan.Rows[i];
                read.Rows.Add(new TrxTimingRow {
                    TestId = row.TestId,
                    Outcome = row.Outcome,
                    Name = row.Name,
                    Duration = row.Duration,
                    StdOut = row.StdOut
                });
            }
            return read;
        }

        public static TrxTimingStats Summarize(Stream stream, Stopwatch watch, int deadlineMs, int maxTagBytes) {
            var stats = new TrxTimingStats();
            var scan = Scan(stream, watch, deadlineMs, maxTagBytes, 0, false);
            stats.Complete = scan.Complete;
            if (!scan.Complete) return stats;
            var ranked = new List<Ranked>();
            for (int i = 0; i < scan.Rows.Count; i++) {
                if (!scan.Rows[i].HasSeconds) continue;
                ranked.Add(new Ranked { Seconds = scan.Rows[i].Seconds, Name = scan.Rows[i].Name ?? "" });
            }
            stats.Count = ranked.Count;
            if (ranked.Count < 1) return stats;
            ranked.Sort(CompareRank);
            var sorted = new double[ranked.Count];
            for (int i = 0; i < ranked.Count; i++) sorted[i] = ranked[i].Seconds;
            stats.P50 = Rank(sorted, 0.50);
            stats.P95 = Rank(sorted, 0.95);
            stats.Max = Math.Round(sorted[sorted.Length - 1], 3, MidpointRounding.ToEven);
            int take = ranked.Count < 20 ? ranked.Count : 20;
            stats.SlowNames = new string[take];
            stats.SlowSeconds = new double[take];
            for (int i = 0; i < take; i++) {
                var row = ranked[ranked.Count - 1 - i];
                stats.SlowNames[i] = row.Name;
                stats.SlowSeconds[i] = Math.Round(row.Seconds, 3, MidpointRounding.ToEven);
            }
            return stats;
        }

        static int CompareRank(Ranked left, Ranked right) {
            int bySeconds = left.Seconds.CompareTo(right.Seconds);
            if (bySeconds != 0) return bySeconds;
            return string.CompareOrdinal(right.Name, left.Name);
        }

        static double Rank(double[] sorted, double fraction) {
            int n = sorted.Length;
            int rank = (int)Math.Ceiling(fraction * n);
            if (rank < 1) rank = 1;
            if (rank > n) rank = n;
            return Math.Round(sorted[rank - 1], 3, MidpointRounding.ToEven);
        }

        static ScanBag Scan(Stream stream, Stopwatch watch, int deadlineMs, int maxTagBytes, int maxStdoutBytes, bool captureStdout) {
            var bag = new ScanBag();
            var cursor = new Cursor(stream, watch, deadlineMs);
            if (maxTagBytes < 64) maxTagBytes = 64;
            if (maxStdoutBytes < 0) maxStdoutBytes = 0;
            while (true) {
                int b = cursor.NextUntil((byte)'<');
                if (b == -2) { bag.Complete = false; return bag; }
                if (b < 0) break;
                string name = cursor.ReadName();
                if (!IsLocal(name, "UnitTestResult")) continue;
                string attrs;
                bool selfClose;
                if (!cursor.ReadStartTag(maxTagBytes, out attrs, out selfClose)) {
                    bag.Complete = false;
                    return bag;
                }
                string stdout = "";
                if (!selfClose) {
                    if (!cursor.ReadBody(name, maxTagBytes, maxStdoutBytes, captureStdout, out stdout)) {
                        bag.Complete = false;
                        return bag;
                    }
                }
                string duration = Attribute(attrs, "duration");
                double seconds;
                bool hasSeconds = TrySeconds(duration, out seconds);
                bag.Rows.Add(new HeldRow {
                    TestId = captureStdout ? Attribute(attrs, "testId") : "",
                    Outcome = captureStdout ? Attribute(attrs, "outcome") : "",
                    Name = Attribute(attrs, "testName"),
                    Duration = captureStdout ? duration : "",
                    StdOut = stdout ?? "",
                    Seconds = seconds,
                    HasSeconds = hasSeconds
                });
            }
            bag.Complete = true;
            return bag;
        }

        static bool TrySeconds(string text, out double seconds) {
            seconds = 0;
            if (string.IsNullOrEmpty(text)) return false;
            int i = 0;
            int sign = 1;
            if (text[0] == '-') { sign = -1; i++; }
            int first = ReadDigits(text, ref i);
            if (first < 0) return FallbackSeconds(text, out seconds);
            int days = 0;
            int hours = first;
            if (i < text.Length && text[i] == '.') {
                int mark = i;
                i++;
                int afterDot = ReadDigits(text, ref i);
                if (afterDot >= 0 && i < text.Length && text[i] == ':') {
                    days = first;
                    hours = afterDot;
                } else {
                    i = mark;
                    return FallbackSeconds(text, out seconds);
                }
            }
            if (i >= text.Length || text[i] != ':') return FallbackSeconds(text, out seconds);
            i++;
            int minutes = ReadDigits(text, ref i);
            if (minutes < 0 || i >= text.Length || text[i] != ':') return FallbackSeconds(text, out seconds);
            i++;
            int secs = ReadDigits(text, ref i);
            if (secs < 0) return FallbackSeconds(text, out seconds);
            double frac = 0;
            if (i < text.Length && text[i] == '.') {
                i++;
                int digits = 0;
                int value = 0;
                while (i < text.Length && text[i] >= '0' && text[i] <= '9' && digits < 7) {
                    value = value * 10 + (text[i] - '0');
                    digits++;
                    i++;
                }
                if (digits < 1) return FallbackSeconds(text, out seconds);
                double scale = 1;
                for (int d = 0; d < digits; d++) scale *= 10;
                frac = value / scale;
                while (i < text.Length && text[i] >= '0' && text[i] <= '9') i++;
            }
            if (i != text.Length) return FallbackSeconds(text, out seconds);
            seconds = sign * ((days * 86400.0) + (hours * 3600.0) + (minutes * 60.0) + secs + frac);
            return true;
        }

        static int ReadDigits(string text, ref int i) {
            if (i >= text.Length || text[i] < '0' || text[i] > '9') return -1;
            int value = 0;
            while (i < text.Length && text[i] >= '0' && text[i] <= '9') {
                value = (value * 10) + (text[i] - '0');
                i++;
            }
            return value;
        }

        static bool FallbackSeconds(string text, out double seconds) {
            seconds = 0;
            // TimeSpan.TryParse(string, IFormatProvider, out TimeSpan) is not in .NET Framework.
            try {
                TimeSpan span = TimeSpan.Parse(text, CultureInfo.InvariantCulture);
                seconds = span.TotalSeconds;
                return true;
            } catch (FormatException) {
                return false;
            } catch (OverflowException) {
                return false;
            } catch (ArgumentException) {
                return false;
            }
        }

        static string Attribute(string attrs, string name) {
            if (string.IsNullOrEmpty(attrs) || string.IsNullOrEmpty(name)) return "";
            for (int i = 0; i + name.Length <= attrs.Length; i++) {
                if (i > 0 && IsWord(attrs[i - 1])) continue;
                if (string.CompareOrdinal(attrs, i, name, 0, name.Length) != 0) continue;
                int j = i + name.Length;
                if (j < attrs.Length && IsWord(attrs[j])) continue;
                while (j < attrs.Length && char.IsWhiteSpace(attrs[j])) j++;
                if (j >= attrs.Length || attrs[j] != '=') continue;
                j++;
                while (j < attrs.Length && char.IsWhiteSpace(attrs[j])) j++;
                if (j >= attrs.Length) return "";
                char quote = attrs[j];
                if (quote != '"' && quote != '\'') continue;
                j++;
                int end = attrs.IndexOf(quote, j);
                if (end < 0) return attrs.Substring(j);
                return attrs.Substring(j, end - j);
            }
            return "";
        }

        static bool IsLocal(string name, string local) {
            if (name == local) return true;
            int colon = name.LastIndexOf(':');
            return colon > 0 && name.IndexOf(':') == colon && name.Substring(colon + 1) == local;
        }

        static bool IsWord(char c) {
            return (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_';
        }

        sealed class Ranked {
            public double Seconds;
            public string Name;
        }

        sealed class HeldRow {
            public string TestId;
            public string Outcome;
            public string Name;
            public string Duration;
            public string StdOut;
            public double Seconds;
            public bool HasSeconds;
        }

        sealed class ScanBag {
            public bool Complete;
            public List<HeldRow> Rows = new List<HeldRow>();
        }

        sealed class Cursor {
            readonly Stream _stream;
            readonly Stopwatch _watch;
            readonly int _deadline;
            readonly byte[] _buf = new byte[8192];
            byte[] _tag = new byte[256];
            int _pos;
            int _len;
            int _push = -1;

            public Cursor(Stream stream, Stopwatch watch, int deadline) {
                _stream = stream;
                _watch = watch;
                _deadline = deadline < 1 ? 1 : deadline;
            }

            public int Next() {
                if (_push >= 0) {
                    int pushed = _push;
                    _push = -1;
                    return pushed;
                }
                if (_pos >= _len) {
                    if (_watch.ElapsedMilliseconds >= _deadline) return -2;
                    _len = _stream.Read(_buf, 0, _buf.Length);
                    _pos = 0;
                    if (_len <= 0) return -1;
                }
                return _buf[_pos++];
            }

            public int NextUntil(byte target) {
                if (_push >= 0) {
                    int pushed = _push;
                    _push = -1;
                    if (pushed == target) return pushed;
                }
                while (true) {
                    if (_pos >= _len) {
                        if (_watch.ElapsedMilliseconds >= _deadline) return -2;
                        _len = _stream.Read(_buf, 0, _buf.Length);
                        _pos = 0;
                        if (_len <= 0) return -1;
                    }
                    int found = Array.IndexOf(_buf, target, _pos, _len - _pos);
                    if (found >= 0) {
                        _pos = found + 1;
                        return target;
                    }
                    _pos = _len;
                }
            }

            void Push(int value) { _push = value; }

            public string ReadName() {
                var chars = new StringBuilder();
                while (chars.Length < 200) {
                    int b = Next();
                    if (b < 0) return chars.ToString();
                    if (IsNameByte(b)) chars.Append((char)b);
                    else { Push(b); return chars.ToString(); }
                }
                return chars.ToString();
            }

            static bool IsNameByte(int b) {
                return (b >= 'A' && b <= 'Z') || (b >= 'a' && b <= 'z') || (b >= '0' && b <= '9')
                    || b == '_' || b == ':' || b == '-' || b == '.';
            }

            public bool ReadStartTag(int maxTagBytes, out string attrs, out bool selfClose) {
                attrs = "";
                selfClose = false;
                int len = 0;
                bool quoted = false;
                byte quote = 0;
                int last = -1;
                while (true) {
                    int b = Next();
                    if (b < 0) return false;
                    if (quoted) {
                        if (!StoreTag(ref len, maxTagBytes, (byte)b)) return false;
                        if (b == quote) quoted = false;
                        continue;
                    }
                    if (b == '"' || b == '\'') {
                        quoted = true;
                        quote = (byte)b;
                        if (!StoreTag(ref len, maxTagBytes, (byte)b)) return false;
                        continue;
                    }
                    if (b == '>') {
                        selfClose = last == '/';
                        int use = len;
                        if (selfClose && use > 0) use--;
                        attrs = Encoding.UTF8.GetString(_tag, 0, use);
                        return true;
                    }
                    if (!StoreTag(ref len, maxTagBytes, (byte)b)) return false;
                    last = b;
                }
            }

            bool StoreTag(ref int len, int maxTagBytes, byte value) {
                if (len >= maxTagBytes) return false;
                if (len >= _tag.Length) {
                    int size = _tag.Length * 2;
                    if (size < len + 1) size = len + 1;
                    if (size > maxTagBytes) size = maxTagBytes;
                    var next = new byte[size];
                    Buffer.BlockCopy(_tag, 0, next, 0, len);
                    _tag = next;
                }
                _tag[len++] = value;
                return true;
            }

            public bool ReadBody(string resultName, int maxTagBytes, int maxStdoutBytes, bool captureStdout, out string stdout) {
                stdout = "";
                int depth = 1;
                var captured = new List<byte>(64);
                bool capturing = false;
                int captureDepth = -1;
                while (depth > 0) {
                    int b;
                    if (capturing && captured.Count < maxStdoutBytes) b = Next();
                    else b = NextUntil((byte)'<');
                    if (b < 0) return false;
                    if (b != '<') {
                        if (capturing && captured.Count < maxStdoutBytes) captured.Add((byte)b);
                        continue;
                    }
                    int mark = Next();
                    if (mark < 0) return false;
                    if (mark == '!') {
                        if (!ReadSpecial(capturing, captured, maxStdoutBytes)) return false;
                        continue;
                    }
                    if (mark == '/') {
                        string endName = ReadName();
                        if (!SkipUntil('>')) return false;
                        depth--;
                        if (capturing && depth < captureDepth) capturing = false;
                        continue;
                    }
                    Push(mark);
                    string startName = ReadName();
                    string ignored;
                    bool selfClose;
                    if (!ReadStartTag(maxTagBytes, out ignored, out selfClose)) return false;
                    if (selfClose) continue;
                    depth++;
                    if (captureStdout && !capturing && IsLocal(startName, "StdOut")) {
                        capturing = true;
                        captureDepth = depth;
                    }
                }
                stdout = DecodeStdout(captured);
                return true;
            }

            bool ReadSpecial(bool capturing, List<byte> captured, int maxStdoutBytes) {
                int next = Next();
                if (next < 0) return false;
                if (next == '-') {
                    int second = Next();
                    if (second < 0) return false;
                    return SkipSequence(new byte[] { (byte)'-', (byte)'>' });
                }
                if (next == '[') {
                    byte[] cdata = Encoding.ASCII.GetBytes("CDATA[");
                    for (int i = 0; i < cdata.Length; i++) {
                        int got = Next();
                        if (got != cdata[i]) return false;
                    }
                    return ReadMarked(new byte[] { (byte)']', (byte)']', (byte)'>' }, capturing, captured, maxStdoutBytes);
                }
                return SkipUntil('>');
            }

            bool ReadMarked(byte[] marker, bool capturing, List<byte> captured, int maxStdoutBytes) {
                int matched = 0;
                while (matched < marker.Length) {
                    int b = Next();
                    if (b < 0) return false;
                    if (b == marker[matched]) {
                        matched++;
                        continue;
                    }
                    if (capturing) {
                        for (int i = 0; i < matched && captured.Count < maxStdoutBytes; i++) captured.Add(marker[i]);
                        if (captured.Count < maxStdoutBytes) captured.Add((byte)b);
                    }
                    matched = 0;
                    if (b == marker[0]) matched = 1;
                }
                return true;
            }

            bool SkipSequence(byte[] marker) {
                int matched = 0;
                while (matched < marker.Length) {
                    int b = Next();
                    if (b < 0) return false;
                    if (b == marker[matched]) matched++;
                    else matched = b == marker[0] ? 1 : 0;
                }
                return true;
            }

            bool SkipUntil(char expected) {
                while (true) {
                    int b = Next();
                    if (b < 0) return false;
                    if (b == expected) return true;
                }
            }

            static string DecodeStdout(List<byte> captured) {
                if (captured.Count < 1) return "";
                string text = Encoding.UTF8.GetString(captured.ToArray());
                if (text.StartsWith("<![CDATA[", StringComparison.Ordinal) && text.EndsWith("]]>", StringComparison.Ordinal) && text.Length >= 12) {
                    return text.Substring(9, text.Length - 12);
                }
                return text;
            }
        }
    }

    public static class JsonlTimingScanner {
        static readonly byte[] Header = Encoding.ASCII.GetBytes("\"recordKind\":\"header\"");
        static readonly byte[] Fixture = Encoding.ASCII.GetBytes("\"recordKind\":\"fixture\"");
        static readonly byte[] Spawn = Encoding.ASCII.GetBytes("\"process-spawn\"");
        static readonly byte[] Wait = Encoding.ASCII.GetBytes("\"process-wait\"");
        static readonly byte[] Setup = Encoding.ASCII.GetBytes("\"fixture-setup\"");
        static readonly byte[] Teardown = Encoding.ASCII.GetBytes("\"fixture-teardown\"");

        public static JsonlTimingRead Read(Stream stream, Stopwatch watch, int deadlineMs, int maxLineBytes) {
            var read = new JsonlTimingRead { Complete = true };
            if (maxLineBytes < 1) maxLineBytes = 1;
            if (deadlineMs < 1) deadlineMs = 1;
            var buf = new byte[8192];
            var line = new byte[maxLineBytes];
            int lineLen = 0;
            while (true) {
                if (watch.ElapsedMilliseconds >= deadlineMs) { read.Complete = false; return read; }
                int count = stream.Read(buf, 0, buf.Length);
                if (count <= 0) break;
                for (int i = 0; i < count; i++) {
                    byte b = buf[i];
                    if (b == (byte)'\n') {
                        Accept(line, lineLen, read.Lines);
                        lineLen = 0;
                        continue;
                    }
                    if (lineLen >= maxLineBytes) {
                        read.Complete = false;
                        return read;
                    }
                    line[lineLen++] = b;
                }
            }
            if (lineLen > 0) Accept(line, lineLen, read.Lines);
            return read;
        }

        static void Accept(byte[] line, int length, List<string> lines) {
            if (length > 0 && line[length - 1] == (byte)'\r') length--;
            if (length < 1) return;
            if (IndexOf(line, length, Header) < 0 && IndexOf(line, length, Fixture) < 0 && IndexOf(line, length, Spawn) < 0
                && IndexOf(line, length, Wait) < 0 && IndexOf(line, length, Setup) < 0 && IndexOf(line, length, Teardown) < 0) {
                return;
            }
            lines.Add(Encoding.UTF8.GetString(line, 0, length));
        }

        static int IndexOf(byte[] line, int length, byte[] needle) {
            int n = needle.Length;
            if (n == 0 || length < n) return -1;
            int last = length - n;
            for (int i = 0; i <= last; i++) {
                int j = 0;
                while (j < n && line[i + j] == needle[j]) j++;
                if (j == n) return i;
            }
            return -1;
        }

        public static JsonlHeaderRead ReadFirstLine(Stream stream, Stopwatch watch, int deadlineMs, int maxLineBytes) {
            var read = new JsonlHeaderRead();
            if (maxLineBytes < 1) maxLineBytes = 1;
            if (deadlineMs < 1) deadlineMs = 1;
            var buf = new byte[8192];
            var line = new byte[maxLineBytes];
            int lineLen = 0;
            while (true) {
                if (watch.ElapsedMilliseconds >= deadlineMs) { read.Refused = true; return read; }
                int count = stream.Read(buf, 0, buf.Length);
                if (count <= 0) break;
                for (int i = 0; i < count; i++) {
                    byte b = buf[i];
                    if (b == (byte)'\n') {
                        if (lineLen > 0 && line[lineLen - 1] == (byte)'\r') lineLen--;
                        read.Line = lineLen < 1 ? "" : Encoding.UTF8.GetString(line, 0, lineLen);
                        return read;
                    }
                    if (lineLen >= maxLineBytes) { read.Refused = true; return read; }
                    line[lineLen++] = b;
                }
            }
            if (lineLen > 0 && line[lineLen - 1] == (byte)'\r') lineLen--;
            read.Line = lineLen < 1 ? "" : Encoding.UTF8.GetString(line, 0, lineLen);
            return read;
        }
    }
}
