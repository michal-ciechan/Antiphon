using System.IO.Abstractions;
using System.Text.Json.Nodes;
using Antiphon.Nightly;
using Antiphon.Tests.Scripts.Nightly;
using Antiphon.Tests.TestHelpers;
using Microsoft.Extensions.Time.Testing;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// Unit 67 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md: the CARD-0849 front door (C849CacheFrontDoor, the
/// verify-card0849-caches command that replaced scripts/verify-card0849-caches.ps1) in process, with the fakes the PowerShell was
/// captured against (scratchpad u67 driver-c849.ps1) and scripts/fixtures/c913-receipts.ps1 used: git answers Head (or a scenario's
/// head and exit), and the stack verifier writes the scenario's receipts for each call under &lt;evidenceRoot&gt;/&lt;case&gt;/, prints its
/// lines and exits with its code. Each call is logged in the capture's shape. A real temporary checkout; nothing is started.
/// </summary>
internal sealed class C849FrontDoorWorld : IDisposable
{
    public const string Head = "0123456789abcdef0123456789abcdef01234567";

    private int _verifyCalls;

    public C849FrontDoorWorld()
    {
        Root = Directory.CreateTempSubdirectory("c849-front-").FullName;
        Checkout = Path.Combine(Root, "repo");
        Directory.CreateDirectory(Path.Combine(Checkout, "scripts"));
    }

    public string Root { get; }

    public string Checkout { get; }

    public Dictionary<string, string?> Variables { get; } = new(StringComparer.Ordinal);

    public List<JsonObject> Calls { get; } = [];

    /// <summary>git's output line (null: none) and exit.</summary>
    public string? GitOutput { get; set; } = Head;

    public int GitExit { get; set; }

    /// <summary>The verifier's answer for each call (the last one repeats): exit, stdout lines and receipt files.</summary>
    public List<JsonObject> Verifies { get; } = [];

    /// <summary>A verifier callback instead of Verifies (case, manifest path, output): the exit code.</summary>
    public Func<string, string, TextWriter, int>? OnVerify { get; set; }

    public int Guids { get; private set; }

    public void Dispose()
    {
        try { ReadOnlyDirectory.Delete(Root); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    /// <summary>A path the scenarios wrote with $CHECKOUT and backslashes.</summary>
    public string Expand(string text) => text.Replace("$CHECKOUT", Checkout, StringComparison.Ordinal).Replace('\\', Path.DirectorySeparatorChar);

    public NightlyHost Host(StringWriter output, StringWriter error) => new()
    {
        Clock = new FakeTimeProvider(DateTimeOffset.Parse("2026-11-27T09:00:00Z", System.Globalization.CultureInfo.InvariantCulture)),
        Git = new FakeGit().Run,
        Launcher = new RecordingLauncher(_ => new NightlyLaunchResult { ExitCode = 0 }),
        Processes = new FakeProcessTable(),
        Probe = new FakeHostProbe(),
        Out = output,
        Error = error,
        Trace = TextWriter.Null,
        Variables = name => Variables.TryGetValue(name, out var value) ? value : null,
        Sleep = _ => { },
    };

    public C849CacheFrontDoorSeams Seams(TextWriter output) => new()
    {
        GitHead = repo =>
        {
            Calls.Add(new JsonObject { ["name"] = "git", ["args"] = new JsonArray("-C", repo, "rev-parse", "HEAD") });
            return (GitExit, GitOutput == null ? [] : [GitOutput]);
        },
        Verify = (hostCase, manifest) => Verify(hostCase, manifest, output),
        NewGuid = () => new Guid(++Guids, 0, 0, new byte[8]),
    };

    private int Verify(string hostCase, string manifestPath, TextWriter output)
    {
        var raw = File.ReadAllText(manifestPath);
        Calls.Add(new JsonObject { ["name"] = "verify", ["case"] = hostCase, ["manifestPath"] = manifestPath, ["manifest"] = raw });
        if (OnVerify != null) return OnVerify(hostCase, manifestPath, output);
        var spec = Verifies[Math.Min(_verifyCalls++, Verifies.Count - 1)];
        var root = JsonNode.Parse(raw)!["evidenceRoot"]!.GetValue<string>();
        if (spec["files"] is JsonObject files)
        {
            var dir = Path.Combine(root, hostCase);
            Directory.CreateDirectory(dir);
            foreach (var (name, text) in files) File.WriteAllText(Path.Combine(dir, name), text?.GetValue<string>() ?? "");
        }
        if (spec["stdout"] is JsonArray lines)
            foreach (var line in lines) if (line != null) output.WriteLine(line.GetValue<string>());
        return spec["exit"]?.GetValue<int>() ?? 0;
    }

    internal sealed record Outcome(int Exit, string Output, string Error);

    /// <summary>Runs the front door with these options (Checkout is this world's).</summary>
    public Outcome Run(string frontCase, string? sha = Head, string preview = "", string savedDonor = "", bool cold = false)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var options = new C849CacheFrontDoorOptions { Case = frontCase, Sha = sha ?? "", Preview = preview, SavedDonor = savedDonor, Cold = cold, Checkout = Checkout };
        var exit = C849CacheFrontDoor.Run(options, Host(output, error), new FileSystem(), Seams(output));
        return new Outcome(exit, output.ToString(), error.ToString());
    }

    /// <summary>The cases the verifier was handed, in order.</summary>
    public string[] Cases() => Calls.Where(c => c["name"]!.GetValue<string>() == "verify").Select(c => c["case"]!.GetValue<string>()).ToArray();

    /// <summary>The manifests the verifier was handed, in order.</summary>
    public JsonObject[] Manifests() => Calls.Where(c => c["name"]!.GetValue<string>() == "verify").Select(c => JsonNode.Parse(c["manifest"]!.GetValue<string>())!.AsObject()).ToArray();
}

/// <summary>
/// scripts/fixtures/c913-receipts.ps1's fake verifier (unit 67: the fixture is deleted with the front door it ran): the receipts one
/// verify call copies back for a remote case and runner, with the left (server2 or a single case) and right (server2-temp) seed
/// contract schemas, one fault and the reuse flag, exactly as its global:pwsh wrote them.
/// </summary>
internal static class C913Receipts
{
    private static readonly string A64 = new('a', 64);
    private static readonly string B64 = new('b', 64);
    private static readonly string C64 = new('c', 64);

    public static string Contract(string schema) => schema switch
    {
        "legacy" => "schema=legacy\nkind=full\ndigest-type=payload-sha256\ndigest=" + A64 + "\nsmoke=passed\n",
        "2" => "schema=2\nkind=cold\ndigest-type=none\ndigest=none\nsmoke=not-run\n",
        _ => "schema=3\nkind=full\ndigest-type=manifest-sha256\ndigest=" + A64 + "\nsmoke=passed\n",
    };

    public static string Mounts(string project) =>
        "volume antiphon-runner-cache-nuget-packages /home/app/.nuget/packages true\nvolume antiphon-runner-cache-nuget-scratch /var/cache/antiphon/nuget-scratch true\n"
        + "volume antiphon-runner-cache-npm-content /home/app/.npm/_cacache true\nvolume " + project + "_runner-tmp /tmp true\n";

    private static int Variants(int n) => n switch { 13 or 14 or 29 or 30 => 2, 15 or 18 or 27 => 3, 28 => 4, 31 => 16, _ => 1 };

    /// <summary>One call's answer (exit 0 and its receipt files).</summary>
    public static JsonObject Call(string hostCase, string runner, string sha, string left = "3", string right = "3", string fault = "none", bool reuse = false)
    {
        var schema = runner == "server2-temp" ? right : left;
        var contract = Contract(schema);
        if (hostCase == "runner-cache-seed" && reuse) contract = contract.Replace("smoke=passed", "smoke=not-run", StringComparison.Ordinal);
        if (runner == "server2-temp" || hostCase != "verify-runner-caches")
        {
            contract = fault switch
            {
                "duplicate" => contract + "schema=" + schema + "\n",
                "missing" => System.Text.RegularExpressions.Regex.Replace(contract, "digest-type=.*\\n", ""),
                "type" => contract.Replace("manifest-sha256", "payload-sha256", StringComparison.Ordinal),
                "digest" => contract.Replace(A64, B64, StringComparison.Ordinal),
                "smoke" => contract.Replace("smoke=passed", "smoke=not-run", StringComparison.Ordinal),
                "toxic" => contract + "credential=DO_NOT_EXPORT_C913\n",
                "false-smoke" => contract.Replace("smoke=not-run", "smoke=passed", StringComparison.Ordinal),
                _ => contract,
            };
        }
        var mounts = Mounts(runner == "server2-temp" ? "antiphon-runner-temp" : "antiphon-runner");
        if (fault == "mount") mounts = mounts.Replace("/var/cache/antiphon/nuget-scratch", "/wrong", StringComparison.Ordinal);
        var files = new JsonObject
        {
            ["c590-result.json"] = "{\"accepted\":true,\"exit\":0}",
            ["status.json"] = "{\"sessions\":0,\"runnerSessions\":0,\"queuedTasks\":0,\"buildVersion\":\"" + sha + "\",\"dispatchEligible\":true,\"acceptingNewWork\":true,\"draining\":false}",
            ["seed-kind.txt"] = schema == "2" ? "cold\n" : "full\n",
            ["seed-hash.txt"] = A64 + "\n",
            ["seed-contract.txt"] = contract,
            ["smoke-summary.txt"] = "C849_SMOKE runner=" + runner + " uid=1654 restore=0 build=0 run=0 stdout=CARD0849_APPHOST_OK\n",
            ["runner-mounts.txt"] = mounts,
            ["rollback.txt"] = "rollback-image=sha256:" + B64 + "\n",
        };
        if (hostCase == "runner-cache-seed")
        {
            files["seed.txt"] = "ready=true donor=" + C64 + (reuse ? " reused=true" : "") + "\n";
            files["status.json"] = "{\"sessions\":0,\"runnerSessions\":0,\"queuedTasks\":0,\"draining\":true,\"acceptingNewWork\":false,\"dispatchEligible\":true,\"retireWhenIdle\":true,\"redirectTo\":\"server2\"}";
            files["smoke-summary.txt"] = "C849_SMOKE runner=seed uid=1654 restore=0 build=0 run=0 stdout=CARD0849_APPHOST_OK\n";
        }
        if (hostCase == "verify-runner-caches-retired") files["smoke-summary.txt"] = "C849_SMOKE runner=server2 uid=1654 restore=0 build=0 run=0 stdout=CARD0849_APPHOST_OK\n";
        if (hostCase == "runner-cache-fixture")
        {
            files["fixture-summary.txt"] = "source-sha=" + sha + "\nimage-id=sha256:" + B64
                + "\ninventories=2\ngroups=9\ncontrols=32\nexpected-red=32\nvariants=60\nexpected-red-variants=60\nproduction-mutations=0\n";
            var groups = Enumerable.Range(1, 9).Select(n => "PASS F-" + n).ToList();
            var controls = Enumerable.Range(1, 32).Select(n => "CONTROL PC-" + n.ToString("d2") + " expected-red observed=ExpectedRefusal").ToList();
            var variants = Enumerable.Range(1, 32).SelectMany(n => Enumerable.Range(1, Variants(n))
                .Select(v => "PC-" + n.ToString("d2") + "." + v.ToString("d2") + " expected-red observed=ExpectedRefusal")).ToList();
            switch (fault)
            {
                case "group-duplicate": groups[8] = groups[0]; break;
                case "family-duplicate": controls[31] = controls[0]; break;
                case "variant-duplicate": variants[59] = variants[0]; break;
                case "variant-missing": variants.RemoveAt(59); break;
                case "toxic": files["fixture-summary.txt"] = files["fixture-summary.txt"]!.GetValue<string>() + "package=DO_NOT_EXPORT_C913\n"; break;
            }
            files["fixture-groups.txt"] = string.Join("\n", groups) + "\n";
            files["fixture-controls.txt"] = string.Join("\n", controls) + "\n";
            files["fixture-control-variants.txt"] = string.Join("\n", variants) + "\n";
        }
        if (fault == "smoke-evidence") files["smoke-summary.txt"] = "wrong";
        return new JsonObject { ["exit"] = 0, ["files"] = files };
    }

    /// <summary>The remote case of a front case the c913 harness ran.</summary>
    public static string RemoteCase(string front) => front switch
    {
        "Both" => "verify-runner-caches",
        "Seed" => "runner-cache-seed",
        "Retired" => "verify-runner-caches-retired",
        _ => "runner-cache-fixture",
    };

    /// <summary>Invoke-Front: the front case on a fresh world with these receipts; the output or the refusal.</summary>
    public static (bool Accepted, string Text) Front(string front, string left = "3", string right = "3", string fault = "none", bool reuse = false)
    {
        using var world = new C849FrontDoorWorld();
        var remote = RemoteCase(front);
        if (front == "Both")
        {
            world.Verifies.Add(Call(remote, "server2", C849FrontDoorWorld.Head, left, right, fault, reuse));
            world.Verifies.Add(Call(remote, "server2-temp", C849FrontDoorWorld.Head, left, right, fault, reuse));
        }
        else world.Verifies.Add(Call(remote, "", C849FrontDoorWorld.Head, left, right, fault, reuse));
        var run = world.Run(front);
        return (run.Exit == 0, run.Exit == 0 ? run.Output : run.Error);
    }
}
