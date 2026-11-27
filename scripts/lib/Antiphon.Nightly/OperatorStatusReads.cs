// CARD-2087 D-3/D-5: the status verb's stable JSON shape and its bounded, concurrent reads.
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;

namespace Antiphon.Nightly
{
    /// <summary>
    /// The documented `status --json` keys (.claude/skills/antiphon-status-update/SKILL.md). Every key is present on every
    /// path, null when its fact is unknown.
    /// </summary>
    internal static class OperatorStatusShape
    {
        internal static readonly string[] Top =
        {
            "observedAtUtc", "projectId", "runner", "counts", "pipeline", "orchestratorInstructions", "master", "rcRefs", "rc",
            "taskScope", "tasks", "mergeInFlight", "fleetHosts", "fleetConcurrencyScopes", "runnerDefaults", "landQueue",
            "runnerCatalogue", "runnerSlots", "cardsAtRisk", "errors",
        };

        internal static readonly string[] Rc =
        {
            "candidateId", "pin", "runId", "phase", "elapsedSeconds", "lastRun", "runLogDir", "stateDir", "activeChunk",
            "heartbeat", "heartbeatAgeSeconds", "trxCount", "finishedChunks", "scheduledEntries", "failures", "eta",
        };

        internal static readonly string[] Eta =
        {
            "status", "previousRunLogDir", "ratio", "matchedFinished", "knownRemaining", "unknownRemaining", "unknownRemainingChunks",
            "finishedWithoutBaseline", "baselineRemainingSeconds", "projectedRemainingSeconds", "projectedFinishUtc", "lowerBound",
            "observedStartupSeconds", "startupOverheadNote", "biggestRemaining",
        };

        internal const string OverheadNote = "Sequential test time only. Build and startup overhead are outside the projection.";

        internal static JsonObject Skeleton(IEnumerable<string> keys)
        {
            var node = new JsonObject();
            foreach (var key in keys) node[key] = null;
            return node;
        }

        internal static JsonObject RcSkeleton()
        {
            var rc = Skeleton(Rc);
            var eta = Skeleton(Eta);
            eta["startupOverheadNote"] = OverheadNote;
            rc["eta"] = eta;
            return rc;
        }
    }

    /// <summary>
    /// The status verb's reads (D-5): API GETs on a fixed pool of background workers, each with its own deadline, and
    /// side jobs (git, the rc filesystem) on their own background threads, all inside one budget measured from
    /// construction. A read past its deadline or the budget is reported once and its late answer ignored; the waiting
    /// thread never blocks past the budget. Errors are reported in the order the caller consumes the reads.
    /// </summary>
    internal sealed class OperatorStatusReads : IDisposable
    {
        internal const int Workers = 8;

        private sealed class Read
        {
            public string Path;
            public TimeSpan Timeout;
            public readonly ManualResetEventSlim Started = new ManualResetEventSlim(false);
            public readonly ManualResetEventSlim Done = new ManualResetEventSlim(false);
            public TimeSpan StartedAt;
            public JsonNode Value;
            public readonly List<string> Errors = new List<string>();
            public string Failure;
            public bool Settled;
            public bool Reported;
        }

        private readonly Func<string, List<string>, JsonNode> _fetch;
        private readonly TimeSpan _timeout;
        private readonly TimeSpan _budget;
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private readonly BlockingCollection<Read> _queue = new BlockingCollection<Read>();
        private readonly Dictionary<string, Read> _reads = new Dictionary<string, Read>(StringComparer.Ordinal);
        private readonly List<string> _budgetSkipped = new List<string>();
        private volatile bool _closed;

        internal OperatorStatusReads(Func<string, List<string>, JsonNode> fetch, TimeSpan timeout, TimeSpan budget)
        {
            _fetch = fetch ?? throw new ArgumentNullException(nameof(fetch));
            _timeout = timeout;
            _budget = budget;
            for (var i = 0; i < Workers; i++)
                new Thread(Work) { IsBackground = true, Name = "status-read-" + i.ToString(CultureInfo.InvariantCulture) }.Start();
        }

        private TimeSpan Left => _budget - _clock.Elapsed;

        private static TimeSpan Positive(TimeSpan value) => value < TimeSpan.Zero ? TimeSpan.Zero : value;

        internal static string Seconds(TimeSpan value) => value.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture) + " s";

        private void Work()
        {
            foreach (var read in _queue.GetConsumingEnumerable())
            {
                read.StartedAt = _clock.Elapsed;
                read.Started.Set();
                try
                {
                    if (!_closed && Left > TimeSpan.Zero) read.Value = _fetch(read.Path, read.Errors);
                }
                catch (Exception)
                {
                    read.Value = null;
                    read.Errors.Add("GET " + read.Path + " unavailable (HTTP unreachable).");
                }
                finally
                {
                    read.Done.Set();
                }
            }
        }

        /// <summary>Queue a GET (once per path); timeout replaces the per-read deadline.</summary>
        internal void Request(string path, TimeSpan? timeout = null)
        {
            lock (_reads)
            {
                if (_reads.ContainsKey(path)) return;
                var read = new Read { Path = path, Timeout = timeout ?? _timeout };
                _reads[path] = read;
                if (!_queue.IsAddingCompleted) _queue.Add(read);
            }
        }

        /// <summary>
        /// The answer to a GET, requesting it if needed. problems receives its errors on the first consuming call; a null
        /// problems list waits without consuming them (a later call reports them).
        /// </summary>
        internal JsonNode Take(string path, List<string> problems)
        {
            Request(path);
            Read read;
            lock (_reads) read = _reads[path];
            if (!read.Settled)
            {
                if (!read.Started.Wait(Positive(Left)))
                {
                    read.Failure = "budget";
                }
                else
                {
                    var deadline = read.StartedAt + read.Timeout;
                    var budgetBound = deadline > _budget;
                    if (!read.Done.Wait(Positive((budgetBound ? _budget : deadline) - _clock.Elapsed)))
                        read.Failure = budgetBound ? "budget" : "GET " + path + " unavailable (no answer within " + Seconds(read.Timeout) + ").";
                    else if (read.Value == null && read.Errors.Count == 0 && read.StartedAt >= _budget)
                        read.Failure = "budget";
                }
                read.Settled = true;
            }
            if (problems != null && !read.Reported)
            {
                read.Reported = true;
                if (read.Failure == "budget") _budgetSkipped.Add(path);
                else if (read.Failure != null) problems.Add(read.Failure);
                else problems.AddRange(read.Errors);
            }
            return read.Failure == null ? read.Value : null;
        }

        /// <summary>A side job (git, filesystem) on its own background thread, inside the same budget.</summary>
        internal Func<List<string>, T> Start<T>(string label, Func<List<string>, T> work, T fallback)
        {
            var errors = new List<string>();
            var done = new ManualResetEventSlim(false);
            T value = fallback;
            new Thread(() =>
            {
                try { value = work(errors); }
                catch (Exception error) { errors.Add(label + " unavailable (" + error.GetType().Name + ")."); value = fallback; }
                finally { done.Set(); }
            }) { IsBackground = true, Name = "status-" + label }.Start();
            var settled = false;
            var finished = false;
            return problems =>
            {
                if (!settled)
                {
                    finished = done.Wait(Positive(Left));
                    settled = true;
                    if (finished) problems.AddRange(errors);
                    else problems.Add(label + " not observed within the status budget of " + Seconds(_budget) + ".");
                }
                return finished ? value : fallback;
            };
        }

        /// <summary>One line for every read the budget cut off (names the first few).</summary>
        internal void Summarize(List<string> problems)
        {
            if (_budgetSkipped.Count == 0) return;
            var named = string.Join(", ", _budgetSkipped.Take(5)) + (_budgetSkipped.Count > 5 ? " and " + (_budgetSkipped.Count - 5) + " more" : "");
            problems.Add(_budgetSkipped.Count + " API read(s) not answered within the status budget of " + Seconds(_budget) + ": " + named + ".");
        }

        public void Dispose()
        {
            _closed = true;
            _queue.CompleteAdding();
        }
    }
}
