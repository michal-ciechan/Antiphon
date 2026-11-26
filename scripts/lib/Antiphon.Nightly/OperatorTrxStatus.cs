// CARD-2087: sequential rc ETA from matching TRX chunk durations.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Text.Json.Nodes;
using System.Xml;

namespace Antiphon.Nightly
{
    internal static class OperatorTrxStatus
    {
        private const int TimesPrefixBytes = 6000;
        private const int BiggestChunkCount = 8;
        private static readonly StringComparer ChunkNames = StringComparer.OrdinalIgnoreCase;

        private sealed record ChunkTime(DateTimeOffset Start, DateTimeOffset Finish)
        {
            public double Seconds => (Finish - Start).TotalSeconds;
        }

        internal static IEnumerable<IFileInfo> TrxFiles(IFileSystem files, string directory) =>
            files.DirectoryInfo.New(directory).EnumerateFiles("*.trx", new EnumerationOptions
            {
                MatchCasing = MatchCasing.CaseInsensitive,
                AttributesToSkip = FileAttributes.ReparsePoint,
                IgnoreInaccessible = false,
            });

        internal static void Read(JsonObject report, IFileSystem files, string logDir, JsonObject schedule, DateTimeOffset now,
            DateTimeOffset? started, DateTimeOffset? completed, bool comparable, Func<string> previousRun, List<string> errors)
        {
            var failures = new JsonArray();
            report["failures"] = failures;
            var eta = new JsonObject
            {
                ["previousRunLogDir"] = null,
                ["ratio"] = null,
                ["projectedRemainingSeconds"] = null,
                ["projectedFinishUtc"] = null,
                ["startupOverheadNote"] = "Sequential test time only. Build and startup overhead are outside the projection.",
                ["biggestRemaining"] = new JsonArray(),
            };
            report["eta"] = eta;
            IFileInfo[] current;
            try
            {
                current = TrxFiles(files, logDir).OrderBy(file => file.Name, ChunkNames).ToArray();
            }
            catch (Exception error) when (OperatorRcStatus.ReadError(error))
            {
                errors.Add("rc TRX directory could not be read; progress, failures and ETA are unknown.");
                return;
            }

            report["trxCount"] = current.Length;
            var live = new Dictionary<string, ChunkTime>(ChunkNames);
            var duplicate = new HashSet<string>(ChunkNames);
            foreach (var file in current)
            {
                var name = files.Path.GetFileNameWithoutExtension(file.Name);
                try
                {
                    var (time, failed) = ReadLive(files, file.FullName);
                    if (!live.TryAdd(name, time))
                    {
                        duplicate.Add(name);
                        errors.Add("rc live TRX has a duplicate chunk identity: " + name + ".");
                    }
                    foreach (var failure in failed)
                        failures.Add(new JsonObject { ["chunk"] = name, ["testName"] = failure, ["fixes"] = null });
                }
                catch (Exception error) when (OperatorRcStatus.ReadError(error) || error is XmlException)
                {
                    errors.Add("rc live TRX " + file.Name + " unreadable or incomplete; its failures and duration are unverified.");
                }
            }
            foreach (var name in duplicate) live.Remove(name);
            report["finishedChunks"] = live.Count;
            var liveStarts = live.Values.Where(time => time != null).Select(time => time.Start).ToArray();
            if (started != null && liveStarts.Length > 0 && liveStarts.Min() >= started && liveStarts.Min() <= now)
                eta["observedStartupSeconds"] = (liveStarts.Min() - started.Value).TotalSeconds;

            var entries = schedule?["entries"] as JsonArray;
            report["scheduledEntries"] = entries?.Count;
            var planned = entries?.Select(entry => ScheduleKey(entry)).ToArray();
            if (planned == null || planned.Any(string.IsNullOrEmpty) || planned.Distinct(ChunkNames).Count() != planned.Length
                || schedule["preparationComplete"]?.ToJsonString() == "false")
            {
                errors.Add("rc dispatch schedule is unavailable, incomplete or has invalid chunk identities; ETA is unknown.");
                return;
            }
            if (completed.HasValue)
            {
                eta["status"] = "run-completed";
                return;
            }
            if (!comparable)
            {
                errors.Add("rc historical ETA requires a sequential Windows Full run.");
                return;
            }

            var previous = new Dictionary<string, ChunkTime>(ChunkNames);
            try
            {
                var path = previousRun();
                if (path == null)
                {
                    errors.Add("Previous rc run not found; supply --rc-previous-run-log-dir if its logs are available.");
                }
                else
                {
                    eta["previousRunLogDir"] = path;
                    var buffer = new byte[TimesPrefixBytes];
                    foreach (var file in TrxFiles(files, path))
                    {
                        var name = files.Path.GetFileNameWithoutExtension(file.Name);
                        ChunkTime time = null;
                        try { time = ReadPrior(files, file.FullName, buffer); }
                        catch (Exception error) when (OperatorRcStatus.ReadError(error))
                        {
                            errors.Add("Previous rc TRX " + file.Name + " could not be read.");
                        }
                        if (!previous.TryAdd(name, time)) previous[name] = null;
                    }
                    if (previous.Count == 0) errors.Add("Previous rc run has no TRX records; ETA is unknown.");
                }
            }
            catch (Exception error) when (OperatorRcStatus.ReadError(error))
            {
                errors.Add("Previous rc run cannot be read; ETA is unknown.");
                // Keep the readable prefix if the directory changed during enumeration.
            }

            var remaining = planned.Where(chunk => !live.ContainsKey(chunk)).ToArray();
            var known = remaining.Where(chunk => previous.GetValueOrDefault(chunk) != null)
                .Select(chunk => (Chunk: chunk, Seconds: previous[chunk].Seconds))
                .OrderByDescending(chunk => chunk.Seconds).ThenBy(chunk => chunk.Chunk, ChunkNames).ToArray();
            eta["unknownRemaining"] = remaining.Length - known.Length;
            eta["knownRemaining"] = known.Length;
            eta["finishedWithoutBaseline"] = live.Keys.Count(chunk => previous.GetValueOrDefault(chunk) == null);
            eta["biggestRemaining"] = new JsonArray(known.Take(BiggestChunkCount).Select(chunk => (JsonNode)new JsonObject
            {
                ["chunk"] = chunk.Chunk,
                ["previousSeconds"] = chunk.Seconds,
            }).ToArray());
            var matched = planned.Where(chunk => live.GetValueOrDefault(chunk) != null && previous.GetValueOrDefault(chunk)?.Seconds > 0).ToArray();
            eta["matchedFinished"] = matched.Length;
            if (matched.Length == 0)
            {
                if (previous.Count > 0) errors.Add("rc ETA has no comparable finished chunk timings in the previous run.");
                return;
            }

            var ratio = matched.Sum(chunk => live[chunk].Seconds) / matched.Sum(chunk => previous[chunk].Seconds);
            var remainingSeconds = known.Sum(chunk => chunk.Seconds);
            eta["ratio"] = ratio;
            eta["baselineRemainingSeconds"] = remainingSeconds;
            if (known.Length == 0 && remaining.Length != 0)
            {
                errors.Add("rc ETA has no previous timings for the remaining chunks.");
                return;
            }
            var projected = remainingSeconds * ratio;
            if (projected > (DateTimeOffset.MaxValue - now).TotalSeconds)
            {
                errors.Add("rc projected duration is outside the representable time range.");
                return;
            }
            eta["projectedRemainingSeconds"] = projected;
            eta["projectedFinishUtc"] = now.AddSeconds(projected).ToString("O", CultureInfo.InvariantCulture);
        }

        private static string ScheduleKey(JsonNode entry)
        {
            var suite = OperatorRcStatus.Text(entry, "suite");
            var chunk = OperatorRcStatus.Text(entry, "chunk");
            return suite == null || chunk == null ? null : suite + "-" + chunk;
        }

        private static ChunkTime ReadPrior(IFileSystem files, string path, byte[] buffer)
        {
            try
            {
                using var stream = RcQualificationRecords.OpenSharedRead(files, path);
                var count = stream.ReadAtLeast(buffer, buffer.Length, false);
                using var prefix = new MemoryStream(buffer, 0, count, writable: false);
                using var reader = XmlReader.Create(prefix, ReaderSettings());
                if (!IsTrx(reader)) return null;
                while (reader.Read())
                    if (reader.Depth == 1 && IsElement(reader, "Times"))
                        return Time(reader);
            }
            catch (XmlException)
            {
                // A prefix ending before Times, or an invalid header, supplies no timing.
            }
            return null;
        }

        private static (ChunkTime Time, List<string> Failures) ReadLive(IFileSystem files, string path)
        {
            using var stream = RcQualificationRecords.OpenSharedRead(files, path);
            using var reader = XmlReader.Create(stream, ReaderSettings());
            if (!IsTrx(reader)) throw new InvalidDataException("Not a TeamTest TestRun.");
            ChunkTime time = null;
            var failures = new List<string>();
            while (reader.Read())
            {
                if (reader.Depth == 1 && IsElement(reader, "Times")) time = Time(reader);
                if (IsElement(reader, "UnitTestResult") && reader.GetAttribute("outcome") is "Failed" or "Error" or "Timeout" or "Aborted")
                    failures.Add(reader.GetAttribute("testName") ?? "(test name missing)");
            }
            return (time, failures);
        }

        private static XmlReaderSettings ReaderSettings() => new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };

        private static bool IsTrx(XmlReader reader) => reader.MoveToContent() == XmlNodeType.Element && IsElement(reader, "TestRun");

        private static bool IsElement(XmlReader reader, string name) =>
            reader.NodeType == XmlNodeType.Element && reader.LocalName == name && reader.NamespaceURI == WatchQual.TrxNamespace;

        private static ChunkTime Time(XmlReader reader)
        {
            var start = OperatorRcStatus.Time(reader.GetAttribute("start"));
            var finish = OperatorRcStatus.Time(reader.GetAttribute("finish"));
            return start != null && finish != null && finish >= start ? new ChunkTime(start.Value, finish.Value) : null;
        }
    }
}
