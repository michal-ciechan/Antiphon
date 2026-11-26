using System.Globalization;
using System.Text;
using Antiphon.LiveLane;
using Shouldly;
using Antiphon.Tests.TestHelpers;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// W2 slice S22 (unit 58): the C# attestation, freshness, fingerprint, receipt, state and status give the same text the
/// deleted PowerShell gave (live-lane-attestation/-fingerprint/-receipt.ps1, live-lane-status.ps1). The cases, their
/// inputs and the PowerShell's captured outputs are tests/Antiphon.Tests/Scripts/Fixtures/live-lane-parity.json; this
/// class rebuilds the same repositories (fixed author, dates and bytes), documents, receipts and histories and compares
/// byte for byte. The status line is compared without the "claude" key, which the C# adds (the Claude version axis).
/// Also the canonical writer: the project's copy (LiveLaneData) writes what the nightly core's TestSelectionJson writes.
/// </summary>
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class LiveLaneParityTests
{
    private static readonly UTF8Encoding Utf8 = new(false);

    [Test]
    public void Canonical_writer_is_the_nightly_writer()
    {
        var values = new object?[]
        {
            null, "", "plain", "q\"uote\\back/slash", "\n\r\t\f\b\u0001\u001f\u007f", "\u0085\u2028\u2029", "mod\u00e8le \u4e2d \ud83d\ude00", true, false,
            0L, -1L, long.MaxValue, 1.5, -0.25, 1e21, 3, System.Numerics.BigInteger.Parse("123456789012345678901234567890")
        };
        foreach (var value in values) LiveLaneData.Canonical(value).ShouldBe(Antiphon.Nightly.TestSelectionJson.Canonical(value), "value " + value);
        const string document = "{\"b\":[1,2.5,{\"z\":null,\"a\":\"x\"}],\"a\":{\"\":true,\"B\":false,\"b\":\"\\u00e8\\u2028\"},\"n\":-7,\"big\":123456789012345678901234567890,\"e\":[]}";
        var mine = LiveLaneData.Parse(document);
        var theirs = Antiphon.Nightly.TestSelectionJson.Parse(document);
        LiveLaneData.Canonical(mine).ShouldBe(Antiphon.Nightly.TestSelectionJson.Canonical(theirs));
        LiveLaneData.Digest(mine).ShouldBe(Antiphon.Nightly.TestSelectionJson.Digest(theirs));
        LiveLaneData.Sorted(new[] { "b", "a", "B", "a", null! }).ShouldBe(Antiphon.Nightly.TestSelectionJson.Sorted(new object[] { "b", "a", "B", "a" }));
        LiveLaneData.LfText("a\r\nb\rc\n").ShouldBe(Antiphon.Nightly.TestSelectionJson.LfText("a\r\nb\rc\n"));
    }

    [Test]
    public void Captured_powershell_outputs_are_reproduced()
    {
        var spec = (LiveLaneMap)LiveLaneData.Parse(File.ReadAllText(Path.Combine(LiveLaneTestSources.RepoRoot, "tests/Antiphon.Tests/Scripts/Fixtures/live-lane-parity.json")))!;
        using var scratch = new LaneScratch("c1545-parity-");
        var world = new World(spec, scratch);
        var failures = new List<string>();
        var count = 0;
        foreach (LiveLaneMap @case in (List<object>)spec["cases"]!)
        {
            count++;
            var name = (string)@case["name"]!;
            var expected = @case["expected"];
            expected.ShouldNotBeNull("case " + name + " has no captured output");
            var actual = world.Run(@case, count);
            var wanted = expected is string text ? text : LiveLaneData.Canonical(expected);
            if (actual != wanted) failures.Add(name + "\n  expected: " + wanted + "\n  actual:   " + actual);
        }
        failures.ShouldBeEmpty(string.Join(Environment.NewLine, failures));
        count.ShouldBeGreaterThan(80);
    }

    /// <summary>The repositories, documents and placeholders of the spec, built as the capture built them.</summary>
    private sealed class World
    {
        private readonly LiveLaneMap _spec;
        private readonly LaneScratch _scratch;
        private readonly Dictionary<string, (string Root, Dictionary<string, string> Shas)> _repositories = new(StringComparer.Ordinal);
        private readonly Dictionary<string, object> _values = new(StringComparer.Ordinal);
        private readonly string _docs;
        private readonly string _lane;
        private readonly List<string> _roster;
        private readonly DateTimeOffset _now;

        public World(LiveLaneMap spec, LaneScratch scratch)
        {
            _spec = spec;
            _scratch = scratch;
            _lane = (string)spec["lane"]!;
            _roster = ((List<object>)spec["roster"]!).Cast<string>().ToList();
            _now = DateTimeOffset.Parse((string)spec["now"]!, CultureInfo.InvariantCulture);
            foreach (var pair in (LiveLaneMap)spec["repositories"]!) Build(pair.Key, (List<object>)pair.Value);
            _docs = scratch.Path("docs");
            Directory.CreateDirectory(_docs);
            var documents = (LiveLaneMap)spec["documents"]!;
            foreach (var pair in documents)
            {
                var definition = (LiveLaneMap)pair.Value;
                var document = definition["base"] is string @base && !definition.Contains("format") ? Edit(documents[@base]!, definition, v => v) : definition;
                File.WriteAllText(Path.Combine(_docs, pair.Key + ".json"), LiveLaneData.Canonical(document), Utf8);
            }
            var components = spec["components"]!;
            _values["components"] = components;
            _values["fp"] = LiveLaneFingerprint.Digest(_lane, Copy(components));
            _values["roster"] = LiveLaneReceipt.RosterDigest(_roster);
            _values["artifact"] = LiveLaneData.Sha256Text("console bytes\n");
            foreach (var pair in _repositories["rc"].Shas) _values[pair.Key] = pair.Value;
            var real = LiveLaneFingerprint.Compute(LiveLaneTestSources.Create(), _repositories["rc"].Root, _repositories["rc"].Shas["Y"], _lane, Path.Combine(_docs, "base.json"));
            _values["fp:Y:base"] = real.Components!;
            _values["fpd:Y:base"] = real.Digest!;
            _values["fplane:other-lane"] = LiveLaneFingerprint.Digest("other-lane", Copy(components));
            _values["digest:green"] = LiveLaneData.Sha256Text(ReceiptText("green"));
        }

        private void Build(string name, List<object> commits)
        {
            var repo = new LiveLaneRepo(_scratch.Path("repo-" + name));
            var shas = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (LiveLaneMap commit in commits)
            {
                if (commit["parent"] is string parent) repo.Checkout(shas[parent]);
                if (commit["write"] is LiveLaneMap write) foreach (var pair in write) repo.Put(pair.Key, (string)pair.Value);
                if (commit["delete"] is List<object> delete) foreach (string path in delete) repo.Drop(path);
                repo.Git("add", "--all");
                if (commit["chmodX"] is List<object> chmod) foreach (string path in chmod) repo.Git("update-index", "--chmod=+x", path);
                if (commit["symlink"] is LiveLaneMap links) foreach (var pair in links) repo.StageLink(pair.Key, (string)pair.Value);
                if (commit["gitlink"] is LiveLaneMap gitlinks) foreach (var pair in gitlinks) repo.StageGitlink(pair.Key, shas[(string)pair.Value]);
                shas[(string)commit["name"]!] = repo.CommitIndex((string)commit["name"]!);
            }
            _repositories[name] = (repo.Root, shas);
        }

        private static object? Copy(object? value) => LiveLaneData.Parse(LiveLaneData.Canonical(value));

        private object? Resolve(object? value)
        {
            switch (value)
            {
                case string text when text.StartsWith("${", StringComparison.Ordinal) && text.EndsWith('}'):
                    var name = text[2..^1];
                    if (!_values.TryGetValue(name, out var found)) throw new InvalidOperationException("unknown placeholder " + text);
                    return Copy(found);
                case LiveLaneMap map:
                    foreach (var key in map.Keys.ToList()) map[key] = Resolve(map[key]);
                    return map;
                case List<object> list:
                    for (var i = 0; i < list.Count; i++) list[i] = Resolve(list[i])!;
                    return list;
                default:
                    return value;
            }
        }

        private static object? Edit(object @base, LiveLaneMap? edit, Func<object?, object?> resolve)
        {
            var document = Copy(@base)!;
            if (edit == null) return document;
            if (edit["remove"] is List<object> removes) foreach (string pointer in removes) Remove(document, pointer);
            if (edit["set"] is LiveLaneMap sets) foreach (var pair in sets) Set(document, pair.Key, resolve(Copy(pair.Value)));
            return document;
        }

        private static (object Parent, string Last) Walk(object document, string pointer)
        {
            var parts = pointer[1..].Split('/');
            var node = document;
            for (var i = 0; i < parts.Length - 1; i++)
                node = node is LiveLaneMap map ? map[parts[i]]! : ((List<object>)node)[int.Parse(parts[i], CultureInfo.InvariantCulture)];
            return (node, parts[^1]);
        }

        private static void Set(object document, string pointer, object? value)
        {
            var (parent, last) = Walk(document, pointer);
            if (parent is LiveLaneMap map) { map[last] = value; return; }
            var list = (List<object>)parent;
            var index = int.Parse(last, CultureInfo.InvariantCulture);
            if (index == list.Count) list.Add(value!); else list[index] = value!;
        }

        private static void Remove(object document, string pointer)
        {
            var (parent, last) = Walk(document, pointer);
            if (parent is LiveLaneMap map) map.Remove(last);
            else ((List<object>)parent).RemoveAt(int.Parse(last, CultureInfo.InvariantCulture));
        }

        private string ReceiptText(string name) =>
            LiveLaneData.Canonical(Edit(Resolve(Copy(_spec["receipt"]))!, ((LiveLaneMap)_spec["receipts"]!)[name] as LiveLaneMap, Resolve));

        private string WriteHistoryItem(string directory, string name, string file)
        {
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, file);
            var receipts = (LiveLaneMap)_spec["receipts"]!;
            if (receipts.Contains(name))
            {
                File.WriteAllText(Path.Combine(directory, "console.log"), "console bytes\n", Utf8);
                File.WriteAllText(path, ReceiptText(name), Utf8);
                if ((receipts[name] as LiveLaneMap)?["artifactAppend"] is string append) File.AppendAllText(Path.Combine(directory, "console.log"), append, Utf8);
                return path;
            }
            File.WriteAllText(path, LiveLaneData.Canonical(Resolve(Copy(((LiveLaneMap)_spec["records"]!)[name]))), Utf8);
            return path;
        }

        private static string Error(LiveLaneRefusal refusal) => "ERR:" + refusal.Message;

        private string Iso(long secondsAgo) => LiveLaneReceipt.Iso(_now.AddSeconds(-secondsAgo));

        public string Run(LiveLaneMap @case, int index)
        {
            var name = (string)@case["name"]!;
            var directory = _scratch.Path($"case-{index:D3}-{name}");
            Directory.CreateDirectory(directory);
            try
            {
                switch ((string)@case["kind"]!)
                {
                    case "attestation":
                        var roster = @case.Contains("roster") && !LiveLaneData.Truthy(@case["roster"]) ? null : _roster;
                        return LiveLaneData.Canonical(LiveLaneAttestation.Environment(Path.Combine(_docs, @case["document"] + ".json"), _lane, roster).ToJson());
                    case "freshness":
                        return LiveLaneData.Canonical(Freshness(@case).ToJson());
                    case "fingerprint":
                    {
                        var (root, shas) = _repositories[(string)@case["repository"]!];
                        var attestation = @case["attestation"] is string document ? Path.Combine(_docs, document + ".json") : null;
                        return LiveLaneData.Canonical(LiveLaneFingerprint.Compute(LiveLaneTestSources.Create(), root, shas[(string)@case["commit"]!],
                            @case["lane"] as string ?? _lane, attestation).ToJson());
                    }
                    case "receipt":
                    {
                        var path = WriteHistoryItem(Path.Combine(directory, "r"), (string)@case["receipt"]!, "receipt.json");
                        var check = LiveLaneReceipt.Check(path, null, _roster);
                        var map = new LiveLaneMap(("valid", check.Valid), ("reasons", check.Reasons.ToList()), ("outcome", check.Outcome), ("code", check.Code),
                            ("digest", check.Digest), ("sequence", check.Valid ? check.Sequence : null), ("endedUtc", null));
                        if (check.Valid && check.EndedUtc != null) map["endedUtc"] = LiveLaneReceipt.Iso(check.EndedUtc.Value);
                        return LiveLaneData.Canonical(map);
                    }
                    case "state":
                        return LiveLaneData.Canonical(State(@case, directory).ToJson());
                    case "status":
                        return Status(@case, directory);
                }
            }
            catch (LiveLaneRefusal refusal) { return Error(refusal); }
            throw new InvalidOperationException("unknown kind in case " + name);
        }

        private LiveLaneFreshness Freshness(LiveLaneMap @case)
        {
            var ago = @case["executedAgoSeconds"] is long seconds ? seconds : 3600;
            List<object> probes;
            if (@case["probes"] is List<object> given)
            {
                probes = new List<object>();
                foreach (LiveLaneMap probe in given)
                {
                    var copy = (LiveLaneMap)Copy(probe)!;
                    if (copy["agoSeconds"] is long probeAgo) { copy["atUtc"] = Iso(probeAgo); copy.Remove("agoSeconds"); }
                    probes.Add(copy);
                }
            }
            else
                probes = new List<object>
                {
                    new LiveLaneMap(("kind", "auth"), ("nonce", "nonce-A"), ("host", "host-A"), ("account", "account-A"), ("result", "Ok"), ("atUtc", Iso(30))),
                    new LiveLaneMap(("kind", "control"), ("nonce", "nonce-A"), ("host", "host-A"), ("account", "account-A"), ("result", "Ok"), ("atUtc", Iso(30)))
                };
            static LiveLaneBinding Binding(object? value) => value is LiveLaneMap map
                ? new LiveLaneBinding { Nonce = map["nonce"], Host = map["host"], Account = map["account"], Epoch = map["epoch"], ReceiptId = map["receiptId"] }
                : null!;
            var candidate = @case.Contains("candidate") ? Binding(@case["candidate"])
                : new LiveLaneBinding { Nonce = "nonce-A", Host = "host-A", Account = "account-A" };
            var executed = @case.Contains("executed") ? Binding(@case["executed"])
                : new LiveLaneBinding { Epoch = "epoch-A", Host = "host-A", Account = "account-A", ReceiptId = "receipt-A" };
            var revocations = @case["revocations"] as List<object> ?? new List<object>();
            return LiveLaneAttestation.Freshness((string)@case["policy"]!, _now.AddSeconds(-ago), _now, candidate, executed, probes, revocations);
        }

        private LiveLaneState State(LiveLaneMap @case, string directory)
        {
            var paths = new List<string>();
            var i = 0;
            foreach (string item in (List<object>)@case["history"]!)
            {
                i++;
                paths.Add(WriteHistoryItem(Path.Combine(directory, i + "-" + item), item, item + ".json"));
            }
            var components = (LiveLaneMap)Copy(_spec["components"])!;
            if (@case["changed"] is LiveLaneMap changed) foreach (var pair in changed) components[pair.Key] = pair.Value;
            var fingerprint = new LiveLaneMap(("status", @case["status"] as string ?? "complete"), ("digest", LiveLaneFingerprint.Digest(_lane, components)),
                ("components", components), ("unknownReasons", new List<object>()),
                ("environment", new LiveLaneMap(("status", @case["environment"] as string ?? "complete"), ("reasons", new List<object>()))));
            var (root, shas) = _repositories["rc"];
            var candidate = new LiveLaneStateCandidate
            {
                LaneId = _lane, Repository = "fixture/antiphon", Ref = "rc/fixture-1", Sha = shas["Y"], Host = "host-A", Account = "account-A", Nonce = "nonce-A",
                Fingerprint = fingerprint
            };
            var probes = (List<object>)((LiveLaneMap)_spec["context"]!)["probes"]!;
            return LiveLaneReceipt.State(LiveLaneTestSources.Create().Git, candidate, paths, _roster, root, @case["policy"] as string ?? "Bounded24h", _now,
                probes, new List<object>());
        }

        private string Status(LiveLaneMap @case, string directory)
        {
            var (root, shas) = _repositories[(string)@case["repository"]!];
            var args = (LiveLaneMap)@case["args"]!;
            var sha = (string)args["sha"]!;
            if (sha.StartsWith("${", StringComparison.Ordinal)) sha = shas[sha[2..^1]];
            var attestation = args["attestation"] is string document ? Path.Combine(_docs, document + ".json") : "";
            var history = "";
            if (args["history"] is List<object> items)
            {
                history = Path.Combine(directory, "history");
                Directory.CreateDirectory(history);
                foreach (string item in items) WriteHistoryItem(history, item, item + ".json");
            }
            var context = "";
            if (args["context"] is string contextName)
            {
                context = Path.Combine(directory, contextName);
                File.WriteAllText(context, LiveLaneData.Canonical(_spec["context"]), Utf8);
            }
            var useRecord = args["useRecord"] is string useName ? Path.Combine(directory, useName) : "";
            var options = LiveLaneStatusOptions.Of(root, (string)args["lane"]!, sha, attestation, history, context, args["policy"] as string ?? "AlwaysRerun", useRecord);
            var result = LiveLaneStatus.Lane(LiveLaneTestSources.Create(), options);
            var line = (LiveLaneMap)Copy(result.Document)!;
            line.Remove("claude");
            return LiveLaneData.Canonical(new LiveLaneMap(("stdout", LiveLaneData.Canonical(line)), ("exit", (long)result.ExitCode)));
        }
    }
}
