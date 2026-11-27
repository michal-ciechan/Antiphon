// CARD-2093: scheduled TRX progress and ETA shared by status, qualification-eta and watch-qual.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Numerics;
using System.Text.Json.Nodes;
using System.Xml;

namespace Antiphon.Nightly
{
    internal static class OperatorTrxStatus
    {
        private const int BiggestChunkCount = 8;
        private const double PriorMatches = 20;
        private const int MinimumMatchedPriorSeconds = 20;
        private const int MinimumPercentileMatches = 20;
        private const double NormalP95 = 1.645;
        private static readonly StringComparer ChunkNames = StringComparer.OrdinalIgnoreCase;

        private sealed record ChunkTime(DateTimeOffset Start, DateTimeOffset Finish)
        {
            public TimeSpan Duration => Finish - Start;
            public double Seconds => Duration.TotalSeconds;
        }

        internal sealed record Observation(int TrxCount, int FinishedChunks, int? ScheduledEntries, JsonArray Failures,
            double? StartupSeconds, bool RunCompleted, QualificationEtaReading Eta);

        internal static IEnumerable<IFileInfo> TrxFiles(IFileSystem files, string directory) =>
            files.DirectoryInfo.New(directory).EnumerateFiles()
                .Where(file => file.Name.EndsWith(".trx", StringComparison.OrdinalIgnoreCase)
                    && (file.Attributes & FileAttributes.ReparsePoint) == 0);

        internal static Observation Read(IFileSystem files, string logDir, JsonObject schedule, JsonObject active, DateTimeOffset now,
            DateTimeOffset? started, bool completed, JsonObject state, string previousRunLogDir, List<string> errors, bool reportFailures)
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
            var live = ReadTrxChunks(files, current, failures, "rc live TRX", errors);
            ReadCommandCompletions(files, logDir, schedule, live, errors);
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
            if (!OperatorRcStatus.IsEtaEligible(files, logDir, schedule, state, errors))
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
            path = null;
            try
            {
                if (!string.IsNullOrEmpty(supplied))
                {
                    var selected = files.Path.GetFullPath(supplied);
                    if (!OperatorRcStatus.SamePath(files, logDir, selected))
                    {
                        path = selected;
                        var reason = Incomplete(files, selected, errors, out var previous);
                        if (reason != null)
                            errors.Add("Supplied previous rc run is not a complete baseline: " + reason + ".");
                        return previous;
                    }
                }
                else
                {
                    foreach (var group in OperatorRcStatus.PreviousRunsByStart(files, logDir))
                    {
                        var complete = new List<(string Dir, Dictionary<string, ChunkTime> Chunks)>();
                        foreach (var directory in group)
                            if (Incomplete(files, directory, null, out var previous) == null)
                                complete.Add((directory, previous));
                        if (complete.Count == 0) continue;
                        if (complete.Count == 1)
                        {
                            path = complete[0].Dir;
                            return complete[0].Chunks;
                        }
                        break;
                    }
                }
                errors.Add("Previous rc run not found; supply --previous-run-log-dir (status: --rc-previous-run-log-dir) if its logs are available.");
            }
            catch (Exception error) when (OperatorRcStatus.ReadError(error))
            {
                errors.Add("Previous rc run could not be fully read; historical timings may be incomplete.");
            }
            return new Dictionary<string, ChunkTime>(ChunkNames);
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
                .Select(chunk => (Live: live[chunk].Seconds, Prior: previous[chunk].Duration)).ToArray();

            double ratio = 1;
            double? upperRatio = null;
            var totalTicks = BigInteger.Zero;
            var sumOfSquares = BigInteger.Zero;
            foreach (var chunk in matched)
            {
                var ticks = (BigInteger)chunk.Prior.Ticks;
                totalTicks += ticks;
                sumOfSquares += ticks * ticks;
            }
            // A few milliseconds of comparable history cannot establish the speed of the remaining run.
            if (totalTicks >= MinimumMatchedPriorSeconds * TimeSpan.TicksPerSecond)
            {
                var priorSeconds = (double)totalTicks / TimeSpan.TicksPerSecond;
                var rawRatio = matched.Sum(chunk => chunk.Live) / priorSeconds;
                // The effective count discounts unequal weights. One long chunk can dominate the ratio
                // even when many short chunks have finished.
                var squaredTotal = totalTicks * totalTicks;
                var effective = (double)squaredTotal / (double)sumOfSquares;
                ratio += (rawRatio - 1) * effective / (effective + PriorMatches);
                // Compare before floating-point division so fractional weights meet the threshold exactly.
                if (squaredTotal >= MinimumPercentileMatches * sumOfSquares)
                {
                    // Use elapsed-second residuals so a very short historical chunk cannot inflate
                    // the interval just by putting a tiny denominator under its individual ratio.
                    var squaredResiduals = matched.Sum(chunk =>
                    {
                        var residual = chunk.Live - rawRatio * chunk.Prior.TotalSeconds;
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
                var observationMillisecond = DateTimeOffset.FromUnixTimeMilliseconds(now.ToUnixTimeMilliseconds());
                var projectedMilliseconds = Math.Round(baselineSeconds * ratio * 1000);
                p50 = Finish(observationMillisecond, projectedMilliseconds, errors);
                if (p50 != null) point = projectedMilliseconds / 1000;
                if (upperRatio.HasValue)
                    p95 = Finish(observationMillisecond, Math.Round(baselineSeconds * upperRatio.Value * 1000), errors);
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
        /// Automatic baselines require terminal evidence for each scheduled chunk. A terminal chunk may have no price.
        /// </summary>
        private static string Incomplete(IFileSystem files, string directory, List<string> issues,
            out Dictionary<string, ChunkTime> previous)
        {
            var read = RcQualificationRecords.Read(files, files.Path.Combine(directory, "dispatch-schedule.json"));
            var schedule = read.Ok ? JsonNode.Parse(read.Record.Value.GetRawText()).AsObject() : null;
            var planned = ScheduleKeys(schedule);
            previous = new Dictionary<string, ChunkTime>(ChunkNames);
            if (planned == null && issues == null)
                return "its dispatch schedule is unavailable or invalid";

            var records = TrxFiles(files, directory).ToArray();
            ReadCommandCompletions(files, directory, schedule, previous, issues);
            if (issues == null)
            {
                // A stopped attempt may have many TRXs. Reject missing files before streaming their contents.
                var available = records.Select(file => files.Path.GetFileNameWithoutExtension(file.Name))
                    .Concat(previous.Keys).ToHashSet(ChunkNames);
                if (planned.Any(chunk => !available.Contains(chunk))) return "its dispatch schedule has unfinished chunks";
            }
            foreach (var (name, time) in ReadTrxChunks(files, records, null, "Previous rc TRX", issues))
                previous.TryAdd(name, time);

            if (!read.Ok) return "its dispatch-schedule.json is unavailable (" + read.Reason + ")";
            if (planned == null) return "its dispatch schedule is incomplete or invalid";
            var missing = planned.Length - planned.Count(previous.ContainsKey);
            return missing == 0 ? null : missing + " of its " + planned.Length + " scheduled chunks have no terminal record";
        }

        private static void ReadCommandCompletions(IFileSystem files, string directory, JsonObject schedule,
            Dictionary<string, ChunkTime> observed, List<string> issues)
        {
            if (schedule?["entries"] is not JsonArray entries) return;
            foreach (var entry in entries)
            {
                var suite = OperatorRcStatus.Text(entry, "suite");
                var chunk = OperatorRcStatus.Text(entry, "chunk");
                if (chunk != "all" || suite is not ("client" or "scripts")) continue;

                var name = suite + "-" + chunk;
                var path = files.Path.Combine(directory, name + ".timing.json");
                var sidecar = OperatorRcStatus.ReadRecord(files, path, issues, required: false);
                var runId = OperatorRcStatus.Text(sidecar, "runId");
                var attempt = OperatorRcStatus.Text(sidecar, "attempt");
                var scheduledAttempt = OperatorRcStatus.Text(schedule, "attempt");
                if (OperatorRcStatus.Text(sidecar, "suite") == suite
                    && OperatorRcStatus.Text(sidecar, "chunk") == chunk
                    && (runId == null || runId == OperatorRcStatus.Text(schedule, "runId"))
                    && (attempt == null || scheduledAttempt == null || attempt == scheduledAttempt)
                    && OperatorRcStatus.Text(sidecar, "lastPhase") == "complete"
                    && OperatorRcStatus.Time(OperatorRcStatus.Text(sidecar, "completedAt")) != null)
                    observed.TryAdd(name, null);
            }
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

        private static Dictionary<string, ChunkTime> ReadTrxChunks(IFileSystem files, IEnumerable<IFileInfo> records,
            JsonArray failures, string source, List<string> issues)
        {
            var observed = new Dictionary<string, ChunkTime>(ChunkNames);
            var names = new HashSet<string>(ChunkNames);
            foreach (var file in records)
            {
                var name = files.Path.GetFileNameWithoutExtension(file.Name);
                if (!names.Add(name))
                {
                    observed.Remove(name);
                    issues?.Add(source + " has a duplicate chunk identity: " + name + ".");
                    continue;
                }
                try
                {
                    var (time, failed) = ReadTrx(files, file.FullName, failures != null);
                    observed.Add(name, time);
                    if (failures != null)
                        foreach (var failure in failed)
                            failures.Add(new JsonObject { ["chunk"] = name, ["testName"] = failure, ["fixes"] = null });
                }
                catch (Exception error) when (OperatorRcStatus.ReadError(error) || error is XmlException || error is InvalidDataException)
                {
                    issues?.Add(source + " " + file.Name + " unreadable or incomplete; its failures and duration are unverified.");
                }
            }
            return observed;
        }

        private static (ChunkTime Time, List<string> Failures) ReadTrx(IFileSystem files, string path, bool reportFailures)
        {
            using var stream = RcQualificationRecords.OpenSharedRead(files, path);
            using var reader = XmlReader.Create(stream, ReaderSettings());
            if (!IsTrx(reader)) throw new InvalidDataException("Not a TeamTest TestRun.");
            ChunkTime time = null;
            var hasResultSummary = false;
            var failures = reportFailures ? new List<string>() : null;
            while (reader.Read())
            {
                if (reader.Depth == 1 && IsElement(reader, "Times")) time = Time(reader);
                if (reader.Depth == 1 && IsElement(reader, "ResultSummary")) hasResultSummary = true;
                if (reportFailures && IsElement(reader, "UnitTestResult") && reader.GetAttribute("outcome") is "Failed" or "Error" or "Timeout" or "Aborted")
                    failures.Add(reader.GetAttribute("testName") ?? "(test name missing)");
            }
            WatchQual.RequireResultSummary(hasResultSummary);
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
