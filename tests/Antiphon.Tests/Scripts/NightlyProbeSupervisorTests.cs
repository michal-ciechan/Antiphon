using System.Diagnostics;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Antiphon.Nightly;
using Antiphon.Tests.Application;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;
using Antiphon.TestSupport;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-1375 V-2, in-process (moved from test-nightly-probe-supervisor.ps1). NightlyProbeSupervisor.Run starts a
/// real owned root, child and grandchild (the c1375-owned-tree mode of the script harness host; no self-exit). Its
/// injected execution clock reads 0 until all three identities are published, alive and in the supervisor's
/// descendant inventory, then 29,999 and 30,001 ms. Every process must then be observed exited and joined, both
/// streams at EOF, within one real 5 s bound from expiry, before the independent fallback below can kill anything;
/// a green run needs no fallback. Evidence is kept in .antiphon/c1375-evidence/supervisor/&lt;nonce&gt;/.
/// </summary>
[NativeLinuxOnly("CARD-1375 native expiry qualification requires Linux.")]
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class NightlyProbeSupervisorTests
{
    private static readonly string[] HarnessRows =
    [
        "C1375 ready identities", "C1375 no early expiry", "C1375 expired",
        "C1375 root exited/joined", "C1375 child exited/joined", "C1375 grandchild exited/joined",
        "C1375 both EOFs", "C1375 shared cleanup elapsed bound", "C1375 no fallback needed"
    ];

    [Test]
    public Task C1375_DeadlineKillsAndJoinsTree()
    {
        NativeLinuxGate.Require("CARD-1375 native expiry qualification requires Linux.");
        return Task.Run(() => new OwnedTreeWitness().Run(HarnessRows));
    }

    private sealed record Owned(string Role, int Pid, int ParentPid, string Start, Process Process);

    private sealed class OwnedTreeWitness
    {
        private static readonly string[] Roles = ["root", "child", "grandchild"];
        private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

        private readonly List<(string Name, bool Passed, string Detail)> _rows = [];
        private readonly Dictionary<string, Owned> _observed = new(StringComparer.Ordinal);
        private readonly string _nonce = Guid.NewGuid().ToString("N");
        private readonly Stopwatch _readiness = Stopwatch.StartNew();
        private readonly Stopwatch _expiry = new();
        private string _ownedDirectory = "";
        private int _phase;
        private bool _ready;
        private bool _noEarlyExpiry;

        private void Record(bool condition, string name, string detail = "")
        {
            _rows.Add((name, condition, detail));
            Console.WriteLine(condition ? "PASS " + name : "FAIL " + name + " - " + detail);
        }

        private static void WriteJson(string path, JsonNode value) =>
            File.WriteAllText(path, value.ToJsonString(Indented) + Environment.NewLine, new UTF8Encoding(false));

        private static JsonArray Strings(IEnumerable<string> values) => new(values.Select(value => (JsonNode?)value).ToArray());

        private void ReadOwnedIdentity(string role)
        {
            if (_observed.ContainsKey(role)) return;
            var path = Path.Combine(_ownedDirectory, role + ".json");
            if (!File.Exists(path)) return;
            using var record = JsonDocument.Parse(File.ReadAllText(path));
            var fields = record.RootElement;
            string? Text(string name) => fields.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
            if (!string.Equals(Text("nonce"), _nonce, StringComparison.Ordinal) || !string.Equals(Text("role"), role, StringComparison.Ordinal))
                throw new InvalidOperationException("Fixture nonce/role mismatch.");
            var pid = fields.GetProperty("pid").GetInt32();
            var handle = Process.GetProcessById(pid);
            _observed[role] = new Owned(role, pid, fields.GetProperty("parentPid").GetInt32(), Text("start") ?? "", handle);
            // /proc start ticks are the shared kernel identity. Process.StartTime converts
            // boot-relative time to wall time separately in each process and can differ.
            var stat = NightlyProbeSupervisor.StatFields(pid);
            if (!string.Equals(stat[19], _observed[role].Start, StringComparison.Ordinal) || handle.HasExited)
                throw new InvalidOperationException($"Fixture {role} start identity mismatch.");
        }

        // Independent native identity check; does not use the supervisor's alive helper.
        private static bool OwnedAlive(Owned identity)
        {
            try
            {
                var stat = NightlyProbeSupervisor.StatFields(identity.Pid);
                return string.Equals(stat[19], identity.Start, StringComparison.Ordinal)
                    && !string.Equals(stat[0], "Z", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(stat[0], "X", StringComparison.OrdinalIgnoreCase)
                    && !identity.Process.HasExited;
            }
            catch (IOException)
            {
                return false;
            }
        }

        private long Elapsed(NightlyProbeProcess root, NightlyProbeProcess[] descendants)
        {
            if (_phase == 0)
            {
                foreach (var role in Roles) ReadOwnedIdentity(role);
                if (_readiness.ElapsedMilliseconds > 5000) throw new InvalidOperationException("Fixture readiness exceeded five seconds.");
                if (_observed.Count != 3) return 0;
                foreach (var identity in _observed.Values)
                {
                    if (!OwnedAlive(identity)) throw new InvalidOperationException("Fixture process exited before readiness.");
                }
                if (_observed["root"].Pid != root.Pid || !string.Equals(_observed["root"].Start, root.Start, StringComparison.Ordinal)
                    || _observed["child"].ParentPid != _observed["root"].Pid || _observed["grandchild"].ParentPid != _observed["child"].Pid)
                    throw new InvalidOperationException("Fixture tree identity mismatch.");
                var expected = string.Join(",", new[] { "child", "grandchild" }.Select(role => _observed[role].Pid + ":" + _observed[role].Start).Order(StringComparer.Ordinal));
                var actual = string.Join(",", descendants.Select(identity => identity.Pid + ":" + identity.Start).Order(StringComparer.Ordinal));
                if (!string.Equals(actual, expected, StringComparison.Ordinal)) return 0;
                _ready = true;
                _phase = 1;
                return 29_999;
            }
            if (_phase != 1) throw new InvalidOperationException("Execution continued after injected expiry.");
            _noEarlyExpiry = _observed.Values.All(OwnedAlive);
            _phase = 2;
            _expiry.Start();
            return 30_001;
        }

        public void Run(string[] harnessRows)
        {
            var results = OwnedTempRoot.CreateOwned();
            _ownedDirectory = Directory.CreateDirectory(Path.Combine(results, _nonce)).FullName;
            var evidenceDirectory = Path.Combine(DelegateScriptRunner.RepoRoot, ".antiphon", "c1375-evidence", "supervisor", _nonce);
            Directory.CreateDirectory(evidenceDirectory);
            NightlyProbeReceipt? receipt = null;
            string? fixtureError = null;
            var fallbackNeeded = false;
            var fallbackErrors = new List<string>();
            JsonObject fallback;
            try
            {
                var host = Path.Combine(AppContext.BaseDirectory, "script-harness-host", "Antiphon.ScriptHarnessHost.dll");
                if (!File.Exists(host)) throw new FileNotFoundException("The script harness host was not staged.", host);
                receipt = NightlyProbeSupervisor.Run("dotnet", [host, "c1375-owned-tree", "root", _ownedDirectory, _nonce], _ownedDirectory, Elapsed);
                if (!_ready) throw new InvalidOperationException("Fixture readiness failed: " + string.Join("; ", receipt.Errors));

                // Snapshot exit/join observations BEFORE the independent finally can rescue anything.
                var beforeFallback = new List<(string Role, Owned Identity, bool Exited, bool Joined, bool Alive)>();
                foreach (var role in Roles)
                {
                    var identity = _observed[role];
                    var exited = identity.Process.HasExited;
                    var joined = identity.Process.WaitForExit((int)Math.Max(0, 5000 - _expiry.ElapsedMilliseconds));
                    beforeFallback.Add((role, identity, exited, joined, OwnedAlive(identity)));
                }
                _expiry.Stop();
                JsonObject Row((string Role, Owned Identity, bool Exited, bool Joined, bool Alive) row) => new()
                {
                    ["role"] = row.Role, ["pid"] = row.Identity.Pid, ["start"] = row.Identity.Start,
                    ["exited"] = row.Exited, ["joined"] = row.Joined, ["alive"] = row.Alive,
                };
                WriteJson(Path.Combine(evidenceDirectory, "before-fallback.json"), new JsonObject
                {
                    ["nonce"] = _nonce, ["supervisor"] = receipt.ToJson(),
                    ["beforeFallback"] = new JsonArray(beforeFallback.Select(row => (JsonNode?)Row(row)).ToArray()),
                    ["elapsedFromExpiryMs"] = _expiry.ElapsedMilliseconds, ["ready"] = _ready, ["noEarlyExpiry"] = _noEarlyExpiry,
                });

                Record(_ready, "C1375 ready identities");
                Record(_noEarlyExpiry && _phase == 2, "C1375 no early expiry");
                Record(receipt.Expired, "C1375 expired");
                foreach (var row in beforeFallback)
                {
                    bool reported;
                    if (row.Role == "root")
                    {
                        reported = receipt.RootJoined && receipt.Pid == row.Identity.Pid && string.Equals(receipt.Start, row.Identity.Start, StringComparison.Ordinal);
                    }
                    else
                    {
                        var matches = receipt.Descendants.Where(d => d.Pid == row.Identity.Pid && string.Equals(d.Start, row.Identity.Start, StringComparison.Ordinal)).ToArray();
                        reported = matches.Length == 1 && matches[0].Joined;
                    }
                    Record(row.Exited && row.Joined && !row.Alive && reported, $"C1375 {row.Role} exited/joined", Row(row).ToJsonString());
                }
                var pipes = receipt.StdoutDrained && receipt.StderrDrained;
                foreach (var role in Roles)
                {
                    pipes = pipes && receipt.Stdout!.Contains($"C1375 {role} stdout {_nonce}", StringComparison.Ordinal)
                        && receipt.Stderr!.Contains($"C1375 {role} stderr {_nonce}", StringComparison.Ordinal);
                }
                Record(pipes, "C1375 both EOFs");
                Record(receipt.CleanupElapsedMs <= 5000 && _expiry.ElapsedMilliseconds <= 5000 && receipt.CleanupSuccess && receipt.Descendants.Length == 2,
                    "C1375 shared cleanup elapsed bound", $"supervisor={receipt.CleanupElapsedMs}ms observed={_expiry.ElapsedMilliseconds}ms");
            }
            catch (Exception e)
            {
                fixtureError = e.Message;
                WriteJson(Path.Combine(evidenceDirectory, "fixture-failure.json"), new JsonObject
                {
                    ["error"] = fixtureError, ["supervisor"] = receipt?.ToJson(), ["phase"] = _phase,
                    ["observedRoles"] = Strings(_observed.Keys), ["readinessElapsedMs"] = _readiness.ElapsedMilliseconds,
                });
                Record(false, "C1375 fixture/observation failure", fixtureError);
            }
            finally
            {
                // The independent fallback kills and joins only these owned identities, on every outcome.
                var fallbackClock = Stopwatch.StartNew();
                foreach (var role in Roles)
                {
                    try { ReadOwnedIdentity(role); }
                    catch (Exception e) { fallbackErrors.Add(e.Message); }
                }
                foreach (var role in new[] { "grandchild", "child", "root" })
                {
                    if (!_observed.TryGetValue(role, out var identity)) continue;
                    try
                    {
                        if (OwnedAlive(identity))
                        {
                            fallbackNeeded = true;
                            identity.Process.Kill();
                        }
                        if (!identity.Process.WaitForExit((int)Math.Max(0, 5000 - fallbackClock.ElapsedMilliseconds)))
                            fallbackErrors.Add($"{role} fallback join failed");
                    }
                    catch (Exception e)
                    {
                        fallbackErrors.Add(e.Message);
                    }
                }
                foreach (var identity in _observed.Values) identity.Process.Dispose();
                fallback = new JsonObject
                {
                    ["needed"] = fallbackNeeded, ["errors"] = Strings(fallbackErrors),
                    ["elapsedMs"] = fallbackClock.ElapsedMilliseconds, ["fixtureError"] = fixtureError,
                };
                WriteJson(Path.Combine(evidenceDirectory, "fallback.json"), fallback);
                Console.WriteLine("C1375 EVIDENCE " + evidenceDirectory);
                OwnedTempRoot.Release(results);
            }
            Record(!fallbackNeeded && fallbackErrors.Count == 0, "C1375 no fallback needed", fallback.ToJsonString());

            var failures = _rows.Where(row => !row.Passed).Select(row => row.Name + " : " + row.Detail).ToList();
            if (_rows.Count < harnessRows.Length) failures.Add($"ROW COUNT expected at least={harnessRows.Length} actual={_rows.Count}");
            failures.ShouldBeEmpty("C1375 rows failed; evidence " + evidenceDirectory);
            _rows.Select(row => row.Name).ShouldBe(harnessRows, "C1375 harness rows; evidence " + evidenceDirectory);
        }
    }
}
