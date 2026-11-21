// CARD-1836 S6c prerequisite. Replays the pre-port PowerShell golden.
// Require() fails closed until Antiphon.Nightly.RcShadowSample and RcDirtyOrder exist.
// The same methods then compare compiled verdicts to that fixture. Do not regenerate the fixture
// after the PowerShell libraries become wrappers.
#nullable disable
using System.Collections;
using System.Diagnostics;
using System.Dynamic;
using System.Globalization;
using System.Reflection;
using System.Text.Json.Nodes;
using Antiphon.Tests.Application;
using Antiphon.Tests.TestHelpers;

namespace Antiphon.Tests.Scripts;

internal static class RcPortCorpus
{
    const string ShadowType = "Antiphon.Nightly.RcShadowSample";
    const string DirtyType = "Antiphon.Nightly.RcDirtyOrder";
    const string SmaPath = "/opt/powershell/System.Management.Automation.dll";

    static readonly object Gate = new object();
    static JsonArray cases;
    static Assembly automation;

    public static void RequireShadow() => Require(ShadowType);

    public static void RequireDirty() => Require(DirtyType);

    public static void Replay(string witness)
    {
        lock (Gate)
        {
            var mismatches = new List<string>();
            var ran = 0;
            foreach (var node in Cases())
            {
                var row = node.AsObject();
                var name = Str(row, "witness");
                if (witness != null && !string.Equals(name, witness, StringComparison.Ordinal)) continue;
                ran++;
                try { ReplayOne(row); }
                catch (Exception ex) { mismatches.Add(Str(row, "id") + ": " + ex.Message); }
            }
            if (ran == 0) throw new InvalidOperationException("no golden cases for " + (witness ?? "corpus"));
            if (mismatches.Count > 0)
            {
                var head = string.Join("\n", mismatches.Take(12));
                throw new InvalidOperationException(mismatches.Count + " mismatched\n" + head);
            }
        }
    }

    static void Require(string name)
    {
        if (Find(name) == null)
            throw new InvalidOperationException("C# type " + name + " does not exist");
    }

    static Type Find(string name)
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            var type = assembly.GetType(name, false);
            if (type != null) return type;
        }
        return Type.GetType(name, false);
    }

    static JsonArray Cases()
    {
        if (cases != null) return cases;
        var path = Path.Combine(DelegateScriptRunner.RepoRoot, "tests", "Antiphon.Tests", "Scripts", "Fixtures", "card-1836-s6c-rc-port-golden.json");
        var root = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        if (Str(root, "format") != "antiphon-rc-port-golden-v1")
            throw new InvalidOperationException("golden format");
        cases = root["cases"]!.AsArray();
        return cases;
    }

    static void ReplayOne(JsonObject row)
    {
        var op = Str(row, "op");
        var input = row["input"]!.AsObject();
        var expect = row["expect"]!.AsObject();
        switch (op)
        {
            case "proposal": ReplayProposal(input, expect); return;
            case "trust": ReplayTrust(input, expect); return;
            case "pilot": ReplayPilot(input, expect); return;
            case "dirty": ReplayDirty(input, expect); return;
            default: throw new InvalidOperationException("op " + op);
        }
    }

    static void ReplayProposal(JsonObject input, JsonObject expect)
    {
        var chunks = Materialize(input["chunks"]);
        var seed = Str(input, "seed");
        var root = DelegateScriptRunner.RepoRoot;
        var result = Invoke(ShadowType, "Proposal", out var error, chunks, seed, "", root);
        if (!string.IsNullOrEmpty(Str(expect, "error")))
        {
            Equals(error, Str(expect, "error"), "error");
            return;
        }
        Equals(error, "", "error");
        CompareResult(expect, result, null);
        if (expect.ContainsKey("secondError"))
        {
            var dir = Path.Combine(Path.GetTempPath(), "rc-port-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "proposal.json");
            try
            {
                _ = Invoke(ShadowType, "Proposal", out var firstError, chunks, seed, path, root);
                Equals(firstError, "", "first-write");
                _ = Invoke(ShadowType, "Proposal", out var second, chunks, seed, path, root);
                Equals(second, Str(expect, "secondError"), "secondError");
            }
            finally
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
        }
    }

    static void ReplayTrust(JsonObject input, JsonObject expect)
    {
        var result = Invoke(ShadowType, "Trust", out var error,
            Materialize(input["currentContract"]),
            Materialize(input["baselineContract"]),
            Materialize(input["evidence"]),
            Materialize(input["fullAnchor"]),
            DateTimeOffset.Parse(Str(input, "utcNow"), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
            Materialize(input["pilots"]),
            Materialize(input["carries"]),
            Materialize(input["sampleFailure"]),
            input["rosterChanged"]!.GetValue<bool>(),
            Materialize(input["operatorRecord"]),
            Str(input, "currentSample"),
            Str(input, "contractDigest"),
            "/tmp/rc-port-trust-unused.json");
        if (!string.IsNullOrEmpty(Str(expect, "error")))
        {
            Equals(error, Str(expect, "error"), "error");
            return;
        }
        Equals(error, "", "error");
        CompareResult(expect, result, null);
    }

    static void ReplayPilot(JsonObject input, JsonObject expect)
    {
        var result = Invoke(ShadowType, "PilotEligible", out var error,
            Materialize(input["pilot"]), Str(input, "contractDigest"));
        Equals(error, Str(expect, "error"), "error");
        if (!string.IsNullOrEmpty(error)) return;
        var eligible = result is bool bit && bit;
        Equals(eligible ? "true" : "false", Str(expect, "eligible"), "eligible");
    }

    static void ReplayDirty(JsonObject input, JsonObject expect)
    {
        var plan = new List<string>();
        if (input["repoPlan"] is JsonArray steps)
        {
            foreach (var step in steps) plan.Add(step!.GetValue<string>());
        }
        var obligations = Materialize(input["obligations"]);
        var attempts = Materialize(input["attempts"]);
        var fixes = Materialize(input["fixes"]);
        var candidate = Str(input, "candidate");
        if (plan.Count > 0)
        {
            WithScratchRepo(repo =>
            {
                var roles = BuildRepo(repo, plan);
                RunDirty(expect, obligations, Substitute(attempts, roles, "sourceSha"),
                    Substitute(fixes, roles, "fixSha"), repo, ResolveToken(candidate, roles));
            });
            return;
        }
        RunDirty(expect, obligations, attempts, fixes, "", candidate);
    }

    internal static void WithScratchRepo(Action<string> run)
    {
        var root = Path.Combine(Path.GetTempPath(), "rc-port-repo-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            run(root);
        }
        finally
        {
            GitFixtureCleanup.Delete(root);
        }
    }

    static void RunDirty(JsonObject expect, object obligations, object attempts, object fixes, string repo, string candidate)
    {
        var result = Invoke(DirtyType, "Launch", out var error, obligations, attempts, fixes, repo, candidate);
        if (!string.IsNullOrEmpty(Str(expect, "error")))
        {
            Equals(error, Str(expect, "error"), "error");
            return;
        }
        Equals(error, "", "error");
        CompareResult(expect, result, candidate);
    }

    static void CompareResult(JsonObject expect, object result, string candidate)
    {
        foreach (var pair in expect)
        {
            var key = pair.Key;
            if (key == "error" || key == "secondError") continue;
            var want = pair.Value!.GetValue<string>();
            Equals(Actual(key, result, candidate), want, key);
        }
    }

    static string Actual(string key, object result, string candidate)
    {
        switch (key)
        {
            case "reasons":
            case "invalidatedCarries":
            case "selected":
            case "omitted":
                return Join(Read(result, key));
            case "skips":
                return Join(Read(result, "expectedSkipOnly"));
            case "launch":
                return LaunchText(Read(result, "launch"));
            case "failureKind":
                return Text(Read(Read(result, "preservedSampleFailure"), "kind"));
            case "failureChunk":
                return Text(Read(Read(result, "preservedSampleFailure"), "chunkId"));
            case "failureOutcome":
                return Text(Read(Read(result, "preservedSampleFailure"), "outcome"));
            case "candidateEcho":
                return string.Equals(Text(Read(result, "candidateSha")), candidate, StringComparison.Ordinal) ? "present" : Text(Read(result, "candidateSha"));
            default:
                return Scalar(Read(result, key));
        }
    }

    static object Invoke(string typeName, string method, out string error, params object[] args)
    {
        error = "";
        var type = Find(typeName) ?? throw new InvalidOperationException("C# type " + typeName + " does not exist");
        MethodInfo match = null;
        foreach (var candidate in type.GetMethods(BindingFlags.Public | BindingFlags.Static))
        {
            if (!string.Equals(candidate.Name, method, StringComparison.Ordinal)) continue;
            if (candidate.GetParameters().Length != args.Length) continue;
            match = candidate;
            break;
        }
        if (match == null) throw new InvalidOperationException(typeName + "." + method + " is missing");
        try { return match.Invoke(null, args); }
        catch (TargetInvocationException ex) { error = Leaf(ex); return null; }
    }

    static string Leaf(Exception ex)
    {
        var cursor = ex.InnerException ?? ex;
        while (cursor.InnerException != null && !string.IsNullOrEmpty(cursor.InnerException.Message))
            cursor = cursor.InnerException;
        return cursor.Message ?? "";
    }

    static object Read(object source, string name)
    {
        if (source == null || name == null) return null;
        if (TryMap(source, name, out var value)) return value;
        if (TryPs(source, name, out value)) return value;
        var property = source.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
        return property == null ? null : property.GetValue(source);
    }

    static bool TryMap(object source, string name, out object value)
    {
        value = null;
        if (source is ExpandoObject) return false;
        if (source is IDictionary dictionary)
        {
            foreach (var key in dictionary.Keys)
            {
                if (key is string text && string.Equals(text, name, StringComparison.Ordinal))
                {
                    value = dictionary[key];
                    return true;
                }
            }
            return false;
        }
        var keys = source.GetType().GetProperty("Keys");
        var indexer = source.GetType().GetProperty("Item");
        if (keys?.GetValue(source) is IEnumerable list && indexer != null &&
            keys.PropertyType.IsGenericType)
        {
            foreach (var key in list)
            {
                if (key is string text && string.Equals(text, name, StringComparison.Ordinal))
                {
                    value = indexer.GetValue(source, new object[] { key });
                    return true;
                }
            }
        }
        return false;
    }

    static bool TryPs(object source, string name, out object value)
    {
        value = null;
        var typeName = source.GetType().FullName ?? "";
        if (typeName != "System.Management.Automation.PSObject" &&
            typeName != "System.Management.Automation.PSCustomObject") return false;
        var properties = source.GetType().GetProperty("Properties")?.GetValue(source) as IEnumerable;
        if (properties == null) return false;
        foreach (var property in properties)
        {
            var propertyName = property.GetType().GetProperty("Name")?.GetValue(property) as string;
            if (!string.Equals(propertyName, name, StringComparison.Ordinal)) continue;
            value = property.GetType().GetProperty("Value")?.GetValue(property);
            return true;
        }
        return false;
    }

    static string Scalar(object value)
    {
        if (value == null) return "";
        if (value is bool bit) return bit ? "true" : "false";
        if (value is string text) return text;
        return Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
    }

    static string Text(object value) => value == null ? "" : Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";

    static string Join(object value)
    {
        if (value == null) return "";
        if (value is string text) return text;
        var parts = new List<string>();
        if (value is IEnumerable list)
        {
            foreach (var item in list)
            {
                if (item != null) parts.Add(Convert.ToString(item, CultureInfo.InvariantCulture) ?? "");
            }
            return string.Join("|", parts);
        }
        return Text(value);
    }

    static string LaunchText(object value)
    {
        if (value is not IEnumerable list || value is string) return "";
        var parts = new List<string>();
        foreach (var item in list)
        {
            if (item == null) continue;
            parts.Add(Text(Read(item, "key")) + ":" + Text(Read(item, "band")) + ":" + Text(Read(item, "disposition")));
        }
        return string.Join(",", parts);
    }

    static void Equals(string actual, string expected, string label)
    {
        if (!string.Equals(actual, expected, StringComparison.Ordinal))
            throw new InvalidOperationException(label + " expected '" + expected + "' actual '" + actual + "'");
    }

    static object Materialize(JsonNode node)
    {
        var shape = node.AsObject();
        switch (Str(shape, "t"))
        {
            case "null": return null;
            case "str": return Str(shape, "v");
            case "bool": return shape["v"]!.GetValue<bool>();
            case "i64": return shape["v"]!.GetValue<long>();
            case "dt":
                return DateTimeOffset.Parse(Str(shape, "v"), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
            case "map": return MaterializeMapObject(shape);
            case "ps": return MaterializePs(shape);
            case "expando": return MaterializeExpando(shape);
            case "arr": return Items(shape).ToArray();
            case "list":
                var list = new List<object>();
                list.AddRange(Items(shape));
                return list;
            case "arraylist":
                var arrayList = new ArrayList();
                foreach (var item in Items(shape)) arrayList.Add(item);
                return arrayList;
            case "hashset":
                var set = new HashSet<object>();
                foreach (var item in Items(shape)) set.Add(item);
                return set;
            default: throw new InvalidOperationException("shape " + Str(shape, "t"));
        }
    }

    static object MaterializeMapObject(JsonObject shape)
    {
        if (Str(shape, "cmp") == "ignore")
        {
            var table = new Hashtable(StringComparer.OrdinalIgnoreCase);
            foreach (var row in shape["e"]!.AsArray())
                table[row!["k"]!.GetValue<string>()] = Materialize(row["v"]);
            return table;
        }
        var ordinal = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (var row in shape["e"]!.AsArray())
            ordinal[row!["k"]!.GetValue<string>()] = Materialize(row["v"]);
        return ordinal;
    }

    static object MaterializePs(JsonObject shape)
    {
        var assembly = Automation();
        var psObject = Activator.CreateInstance(assembly.GetType("System.Management.Automation.PSObject"));
        var properties = psObject.GetType().GetProperty("Properties").GetValue(psObject);
        var note = assembly.GetType("System.Management.Automation.PSNoteProperty");
        var add = properties.GetType().GetMethod("Add", new[] { assembly.GetType("System.Management.Automation.PSPropertyInfo") });
        if (add == null) throw new InvalidOperationException("ps-property-add");
        foreach (var row in shape["e"]!.AsArray())
        {
            var member = Activator.CreateInstance(note, row!["k"]!.GetValue<string>(), Materialize(row["v"]));
            add.Invoke(properties, new[] { member });
        }
        return psObject;
    }

    static object MaterializeExpando(JsonObject shape)
    {
        IDictionary<string, object> expando = new ExpandoObject();
        foreach (var row in shape["e"]!.AsArray())
            expando[row!["k"]!.GetValue<string>()] = Materialize(row["v"]);
        return expando;
    }

    static List<object> Items(JsonObject shape)
    {
        var items = new List<object>();
        foreach (var item in shape["items"]!.AsArray()) items.Add(Materialize(item));
        return items;
    }

    static Assembly Automation()
    {
        if (automation != null) return automation;
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (assembly.GetName().Name == "System.Management.Automation") return automation = assembly;
        }
        return automation = System.Reflection.Assembly.LoadFrom(SmaPath);
    }

    static object Substitute(object value, Dictionary<string, string> roles, string field)
    {
        if (value == null) return null;
        if (value is string) return value;
        if (value is not IEnumerable list) return value;
        var copy = new List<object>();
        foreach (var item in list)
        {
            if (item == null) { copy.Add(null); continue; }
            var token = Text(Read(item, field));
            copy.Add(string.IsNullOrEmpty(token) ? item : WithField(item, field, ResolveToken(token, roles)));
        }
        return copy.ToArray();
    }

    static object WithField(object row, string field, string value)
    {
        if (row is Hashtable table)
        {
            var copy = new Hashtable(StringComparer.OrdinalIgnoreCase);
            foreach (DictionaryEntry entry in table)
                copy[entry.Key] = entry.Key is string key && string.Equals(key, field, StringComparison.Ordinal) ? value : entry.Value;
            return copy;
        }
        if (row is Dictionary<string, object> map)
        {
            var copy = new Dictionary<string, object>(map, map.Comparer);
            copy[field] = value;
            return copy;
        }
        throw new InvalidOperationException("field row " + row.GetType().FullName);
    }

    static string ResolveToken(string token, Dictionary<string, string> roles)
    {
        if (token.StartsWith("role:", StringComparison.Ordinal)) return roles[token.Substring(5)];
        if (token.StartsWith("lit:", StringComparison.Ordinal)) return token.Substring(4);
        if (string.Equals(token, "unknown", StringComparison.Ordinal))
            return "deadbeefdeadbeefdeadbeefdeadbeefdeadbeef";
        return token;
    }

    static Dictionary<string, string> BuildRepo(string dir, List<string> plan)
    {
        var roles = new Dictionary<string, string>(StringComparer.Ordinal);
        Git(dir, "init", "-q", "-b", "main");
        Git(dir, "config", "user.name", "Fixture");
        Git(dir, "config", "user.email", "fixture@example.invalid");
        Git(dir, "config", "commit.gpgsign", "false");
        foreach (var step in plan)
        {
            var part = step.Split('|');
            switch (part[0])
            {
                case "commit":
                    File.WriteAllText(Path.Combine(dir, "note"), part[2] + "\n");
                    Git(dir, "add", "note");
                    Git(dir, "commit", "-qm", part[2]);
                    roles[part[1]] = Git(dir, "rev-parse", "HEAD");
                    break;
                case "branch":
                    Git(dir, "checkout", "-q", "-b", part[1]);
                    break;
                case "branch-from":
                    Git(dir, "checkout", "-q", "-b", part[1], roles[part[2]]);
                    break;
                case "trailer":
                    File.WriteAllText(Path.Combine(dir, "note"), part[2] + "\n");
                    Git(dir, "add", "note");
                    Git(dir, "commit", "-qm", part[2], "-m", "(cherry picked from commit " + roles[part[3]] + ")");
                    roles[part[1]] = Git(dir, "rev-parse", "HEAD");
                    break;
                case "empty":
                    Git(dir, "commit", "--allow-empty", "-qm", part[2]);
                    roles[part[1]] = Git(dir, "rev-parse", "HEAD");
                    break;
                default:
                    throw new InvalidOperationException("plan " + part[0]);
            }
        }
        return roles;
    }

    static string Git(string dir, params string[] args)
    {
        var start = new ProcessStartInfo("git")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        start.ArgumentList.Add("-C");
        start.ArgumentList.Add(dir);
        foreach (var arg in args) start.ArgumentList.Add(arg);
        using var process = Process.Start(start);
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        var stdout = stdoutTask.GetAwaiter().GetResult();
        var stderr = stderrTask.GetAwaiter().GetResult();
        if (process.ExitCode != 0)
            throw new InvalidOperationException("git " + string.Join(" ", args) + " exit " + process.ExitCode + " " + stderr.Trim());
        var line = "";
        using (var reader = new StringReader(stdout))
        {
            line = reader.ReadLine() ?? "";
        }
        return line.Trim();
    }

    static string Str(JsonObject obj, string name)
    {
        var node = obj[name];
        if (node == null) return "";
        return node.GetValue<string>();
    }
}
