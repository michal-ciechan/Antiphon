// CARD-2093: scheduled TRX progress and ETA shared by status, qualification-eta and watch-qual.
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
        private const double PriorMatches = 20;
        private const double MinimumPercentileMatches = 20;
        private const double NormalP95 = 1.645;
        private static readonly StringComparer ChunkNames = StringComparer.OrdinalIgnoreCase;

        private sealed record ChunkTime(DateTimeOffset Start, DateTimeOffset Finish)
        {
            public double Seconds => (Finish - Start).TotalSeconds;
        }

        internal sealed record Observation(int TrxCount, int FinishedChunks, int? ScheduledEntries, JsonArray Failures,
            double? StartupSeconds, bool RunCompleted, QualificationEtaReading Eta);

        internal static IEnumerable<IFileInfo> TrxFiles(IFileSystem files, string directory) =>
            files.DirectoryInfo.New(directory).EnumerateFiles()
                .Where(file => file.Name.EndsWith(".trx", StringComparison.OrdinalIgnoreCase)
                    && (file.Attributes & FileAttributes.ReparsePoint) == 0);

        internal static Observation Read(IFileSystem files, string logDir, JsonObject schedule, JsonObject active, DateTimeOffset now,
            DateTimeOffset? started, bool completed, bool comparable, string previousRunLogDir, List<string> errors, bool reportFailures)
        {
            IFileInfo[] current;
            try
            {
                current = TrxFiles(files, logDir).OrderBy(file => file.Name, ChunkNames).ToArray();
            }
            catch (Exception error) when (OperatorRcStatus.ReadError(error))
            {
                errors.Add("rc TRX directory could not be read; progress, failures and ETA are unknown.");
                return null;
            }

            var failures = reportFailures ? new JsonArray() : null;
            var live = new Dictionary<string, ChunkTime>(ChunkNames);
            var duplicate = new HashSet<string>(ChunkNames);
            foreach (var file in current)
            {
                var name = files.Path.GetFileNameWithoutExtension(file.Name);
                try
                {
                    var (time, failed) = ReadLive(files, file.FullName, reportFailures);
                    if (!live.TryAdd(name, time))
                    {
                        duplicate.Add(name);
                        errors.Add("rc live TRX has a duplicate chunk identity: " + name + ".");
                    }
                    if (reportFailures)
                        foreach (var failure in failed)
                            failures.Add(new JsonObject { ["chunk"] = name, ["testName"] = failure, ["fixes"] = null });
                }
                catch (Exception error) when (OperatorRcStatus.ReadError(error) || error is XmlException || error is InvalidDataException)
                {
                    errors.Add("rc live TRX " + file.Name + " unreadable or incomplete; its failures and duration are unverified.");
                }
            }
            foreach (var name in duplicate) live.Remove(name);
            var firstStart = live.Values.Where(time => time != null).Select(time => (DateTimeOffset?)time.Start).Min();
            var startup = started.HasValue && firstStart >= started && firstStart <= now
                ? (firstStart - started)?.TotalSeconds : null;

            var planned = ScheduleKeys(schedule);
            var observation = new Observation(current.Length, live.Count, (schedule?["entries"] as JsonArray)?.Count,
                failures, startup, completed && planned != null, null);
            if (planned == null)
            {
                errors.Add("rc dispatch schedule is unavailable, incomplete or has invalid chunk identities; ETA is unknown.");
                return observation;
            }
            if (completed) return observation;
            if (!comparable)
            {
                errors.Add("rc historical ETA requires a sequential Windows Full run.");
                return observation;
            }

            var previous = ReadPrevious(files, logDir, previousRunLogDir, errors, out var path);
            return observation with { Eta = Project(planned, live, previous, path, active, now, errors) };
        }

        private static Dictionary<string, ChunkTime> ReadPrevious(
            IFileSystem files, string logDir, string supplied, List<string> errors, out string path)
        {
            var previous = new Dictionary<string, ChunkTime>(ChunkNames);
            path = null;
            try
            {
                path = OperatorRcStatus.PreviousRun(files, logDir, supplied, errors);
                if (path == null)
                {
                    errors.Add("Previous rc run not found; supply --previous-run-log-dir (status: --rc-previous-run-log-dir) if its logs are available.");
                    return previous;
                }

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
            catch (Exception error) when (OperatorRcStatus.ReadError(error))
            {
                errors.Add("Previous rc run could not be fully read; historical timings may be incomplete.");
            }
            return previous;
        }

        private static QualificationEtaReading Project(string[] planned, Dictionary<string, ChunkTime> live,
            Dictionary<string, ChunkTime> previous, string previousPath, JsonObject active, DateTimeOffset now, List<string> errors)
        {
            var remaining = planned.Where(chunk => !live.ContainsKey(chunk)).ToArray();
            var known = remaining.Where(chunk => previous.GetValueOrDefault(chunk) != null)
                .Select(chunk => (Chunk: chunk, Seconds: previous[chunk].Seconds))
                .OrderByDescending(chunk => chunk.Seconds).ThenBy(chunk => chunk.Chunk, ChunkNames).ToArray();
            var unknown = remaining.Where(chunk => previous.GetValueOrDefault(chunk) == null).ToArray();
            var finishedWithoutBaseline = planned.Count(chunk => live.ContainsKey(chunk) && previous.GetValueOrDefault(chunk) == null);
            var matched = planned.Where(chunk => live.GetValueOrDefault(chunk) != null && previous.GetValueOrDefault(chunk)?.Seconds > 0)
                .Select(chunk => (Live: live[chunk].Seconds, Prior: previous[chunk].Seconds)).ToArray();

            double ratio = 1;
            double? upperRatio = null;
            if (matched.Length > 0)
            {
                var priorSeconds = matched.Sum(chunk => chunk.Prior);
                var rawRatio = matched.Sum(chunk => chunk.Live) / priorSeconds;
                // The effective count discounts unequal weights. One long chunk can dominate the ratio
                // even when many short chunks have finished.
                var effective = priorSeconds * priorSeconds / matched.Sum(chunk => chunk.Prior * chunk.Prior);
                ratio += (rawRatio - 1) * effective / (effective + PriorMatches);
                if (effective >= MinimumPercentileMatches)
                {
                    // Use elapsed-second residuals so a very short historical chunk cannot inflate
                    // the interval just by putting a tiny denominator under its individual ratio.
                    var squaredResiduals = matched.Sum(chunk =>
                    {
                        var residual = chunk.Live - rawRatio * chunk.Prior;
                        return residual * residual;
                    });
                    var standardError = Math.Sqrt(squaredResiduals / (1 - 1 / effective)) / priorSeconds;
                    upperRatio = ratio + NormalP95 * standardError;
                }
            }

            var baselineSeconds = known.Sum(chunk => chunk.Seconds);
            var activeKey = OperatorRcStatus.Text(active, "state") == "Active" ? ScheduleKey(active) : null;
            var running = activeKey != null && remaining.Contains(activeKey, ChunkNames) ? 1 : 0;
            double? point = null;
            DateTimeOffset? p50 = null;
            DateTimeOffset? p95 = null;
            if (unknown.Length > 0)
                errors.Add("rc ETA is a lower bound: no previous timing for " + unknown.Length + " remaining chunk(s): "
                    + string.Join(", ", unknown.Take(BiggestChunkCount)) + (unknown.Length > BiggestChunkCount ? " and " + (unknown.Length - BiggestChunkCount) + " more" : "") + ".");
            if (known.Length > 0 || remaining.Length == 0)
            {
                var projectedMilliseconds = Math.Round(baselineSeconds * ratio * 1000);
                p50 = Finish(now, projectedMilliseconds, errors);
                if (p50 != null) point = projectedMilliseconds / 1000;
                if (upperRatio.HasValue)
                    p95 = Finish(now, Math.Round(baselineSeconds * upperRatio.Value * 1000), errors);
            }

            var confidence = point == null ? "unavailable" : upperRatio == null ? "provisional" : "matched";
            return new QualificationEtaReading(remaining.Length - running, running, planned.Length - remaining.Length,
                unknown, known, finishedWithoutBaseline, matched.Length, previousPath, ratio, baselineSeconds, point, p50, p95, confidence);
        }

        private static DateTimeOffset? Finish(DateTimeOffset now, double milliseconds, List<string> errors)
        {
            var availableMilliseconds = (DateTimeOffset.MaxValue - now).Ticks / TimeSpan.TicksPerMillisecond;
            if (milliseconds >= 0 && milliseconds <= availableMilliseconds)
                return now.AddTicks((long)milliseconds * TimeSpan.TicksPerMillisecond);
            errors.Add("rc projected duration is outside the representable time range.");
            return null;
        }

        internal static void PopulateReport(JsonObject report, Observation observed)
        {
            if (observed == null) return;
            report["trxCount"] = observed.TrxCount;
            report["finishedChunks"] = observed.FinishedChunks;
            report["scheduledEntries"] = observed.ScheduledEntries;
            report["failures"] = observed.Failures;

            var eta = report["eta"].AsObject();
            eta["observedStartupSeconds"] = observed.StartupSeconds;
            if (observed.RunCompleted) eta["status"] = "run-completed";
            var projected = observed.Eta;
            if (projected == null) return;
            eta["previousRunLogDir"] = projected.PreviousRunLogDir;
            eta["ratio"] = projected.Ratio;
            eta["matchedFinished"] = projected.MatchedFinished;
            eta["knownRemaining"] = projected.KnownRemaining.Length;
            eta["unknownRemaining"] = projected.UnknownRemaining.Length;
            eta["unknownRemainingChunks"] = new JsonArray(projected.UnknownRemaining.Select(chunk => (JsonNode)chunk).ToArray());
            eta["finishedWithoutBaseline"] = projected.FinishedWithoutBaseline;
            eta["baselineRemainingSeconds"] = projected.BaselineSeconds;
            eta["projectedRemainingSeconds"] = projected.PointSeconds;
            eta["projectedFinishUtc"] = projected.P50Utc?.ToString("O", CultureInfo.InvariantCulture);
            eta["p95FinishUtc"] = projected.P95Utc?.ToString("O", CultureInfo.InvariantCulture);
            eta["confidence"] = projected.Confidence;
            eta["lowerBound"] = projected.LowerBound;
            eta["biggestRemaining"] = new JsonArray(projected.KnownRemaining.Take(BiggestChunkCount).Select(chunk => (JsonNode)new JsonObject
            {
                ["chunk"] = chunk.Chunk,
                ["previousSeconds"] = chunk.Seconds,
            }).ToArray());
        }

        /// <summary>
        /// D-2: a historical run is a baseline only when its TRX files cover its own dispatch-schedule.json (a stopped or
        /// partial attempt does not). Null: complete; otherwise the reason.
        /// </summary>
        internal static string Incomplete(IFileSystem files, string directory)
        {
            var read = RcQualificationRecords.Read(files, files.Path.Combine(directory, "dispatch-schedule.json"));
            if (!read.Ok) return "its dispatch-schedule.json is unavailable (" + read.Reason + ")";
            var schedule = JsonNode.Parse(read.Record.Value.GetRawText()).AsObject();
            var planned = ScheduleKeys(schedule);
            if (planned == null)
                return "its dispatch schedule is incomplete or invalid";
            var timed = new HashSet<string>(TrxFiles(files, directory).Select(file => files.Path.GetFileNameWithoutExtension(file.Name)), ChunkNames);
            var missing = planned.Count(chunk => !timed.Contains(chunk));
            return missing == 0 ? null : missing + " of its " + planned.Length + " scheduled chunks have no TRX";
        }

        private static string[] ScheduleKeys(JsonObject schedule)
        {
            var planned = (schedule?["entries"] as JsonArray)?.Select(ScheduleKey).ToArray();
            if (planned == null || planned.Length == 0 || planned.Any(string.IsNullOrEmpty) || planned.Distinct(ChunkNames).Count() != planned.Length
                || schedule["preparationComplete"]?.ToJsonString() == "false")
                return null;
            return planned;
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

        private static (ChunkTime Time, List<string> Failures) ReadLive(IFileSystem files, string path, bool reportFailures)
        {
            using var stream = RcQualificationRecords.OpenSharedRead(files, path);
            using var reader = XmlReader.Create(stream, ReaderSettings());
            if (!IsTrx(reader)) throw new InvalidDataException("Not a TeamTest TestRun.");
            ChunkTime time = null;
            var failures = reportFailures ? new List<string>() : null;
            while (reader.Read())
            {
                if (reader.Depth == 1 && IsElement(reader, "Times")) time = Time(reader);
                if (reportFailures && IsElement(reader, "UnitTestResult") && reader.GetAttribute("outcome") is "Failed" or "Error" or "Timeout" or "Aborted")
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
