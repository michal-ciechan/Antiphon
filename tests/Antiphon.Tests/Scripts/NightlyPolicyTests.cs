using System.Text.Json.Nodes;
using Antiphon.Nightly;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

// The test-execution policy reader and its rules (Antiphon.Nightly.ExecutionPolicy and NightlyPolicy), called
// in-process: no PowerShell. The schema and profile cases were Test-C599_ProfileSchema and Test-C599_ProfileSuites
// of scripts/test-release-gate.ps1. Every policy here is a real file whose canonical hash the reader validates.
[Category("Unit")]
public sealed class NightlyPolicyTests
{
    [Test]
    public void Shipped_policy_reads_with_its_stated_hash()
    {
        var root = C1514Support.RepoRoot();
        var read = NightlyPolicy.Read(root, "");
        read.SchemaVersion.ShouldBe(4, "shipped policy is v4");
        read.Hash.ShouldBe(read.Object.PolicyHash, "stated hash is the canonical hash");
        read.Object.Profiles.ShouldNotBeNull("shipped policy has profiles");
        read.Object.Profiles!["rc"].ShouldNotBeNull("shipped policy defines rc");
        read.Object.Profiles["RC"].ShouldBeSameAs(read.Object.Profiles["rc"], "profile names ignore case");
        read.Object.Watchdogs["antiphon"].ShouldBe(3600000, "antiphon watchdog");
        read.Object.SelectionCatalog!.Path.ShouldBe("tests/test-selection-policy.json", "selection catalog path");
        NightlyPolicy.TestProjectUniverse(root, read.Object).Ok.ShouldBeTrue("every committed test project is accounted for");
        NightlyPolicy.ProfileExclusions(read.Object, "rc", "antiphon", "windows").ShouldNotBeEmpty("rc declares antiphon exclusions");
    }

    [Test]
    public void Profile_schema_versions()
    {
        var root = NewRoot();
        try
        {
            NightlyPolicy.Read("", Write(root, "v2", Policy(2))).SchemaVersion.ShouldBe(2, "v2 loads");

            var v1 = NightlyPolicy.Read("", Write(root, "v1", Policy(1, profiles: false)));
            v1.SchemaVersion.ShouldBe(1, "v1 still loads");
            string.Join(",", NightlyPolicy.ProfileRequiredSuites(v1.Object, "nightly")).ShouldBe("antiphon,client,scripts", "v1 nightly falls back to defaultSuites");
            Refusal(() => NightlyPolicy.Profile(v1.Object, "rc")).ShouldContain("schema 2", Case.Sensitive, "rc on a v1 policy refuses");

            var v2 = NightlyPolicy.Read("", Path.Combine(root, "v2", "policy.json"));
            Refusal(() => NightlyPolicy.Profile(v2.Object, "turbo")).ShouldContain("unknown profile", Case.Sensitive, "unknown profile refuses");

            Refusal(() => NightlyPolicy.Read("", Write(root, "stale", Policy(2), staleHash: true))).ShouldContain("stale policy hash", Case.Sensitive, "stale hash refuses");
            Refusal(() => NightlyPolicy.Read("", Write(root, "v7", Policy(7, profiles: false)))).ShouldContain("unsupported policy schema", Case.Sensitive, "unknown version refuses");
        }
        finally { Directory.Delete(root, true); }
    }

    [Test]
    public void Profile_suites_and_exclusions()
    {
        var root = NewRoot();
        try
        {
            var policy = NightlyPolicy.Read("", Write(root, "a", Policy(2))).Object;
            var nightly = NightlyPolicy.ProfileRequiredSuites(policy, "nightly");
            var rc = NightlyPolicy.ProfileRequiredSuites(policy, "rc");
            nightly.ShouldNotContain("e2e", "nightly excludes e2e");
            rc.ShouldContain("e2e", "rc requires e2e");
            rc.Length.ShouldBe(nightly.Length + 1, "rc is nightly plus e2e");

            // A manual-mode suite the profile requires is runnable; one it does not is not.
            NightlyPolicy.ProfileSuiteRunnable(policy, "rc", "e2e").ShouldBeTrue("manual e2e is runnable in rc");
            NightlyPolicy.ProfileSuiteRunnable(policy, "nightly", "e2e").ShouldBeFalse("manual e2e stays out of nightly");

            // An rc profile that forgot e2e is visibly not the rc contract.
            var noE2E = NightlyPolicy.Read("", Write(root, "x", Policy(2, omitE2EFromRc: true))).Object;
            NightlyPolicy.ProfileRequiredSuites(noE2E, "rc").ShouldNotContain("e2e", "missing e2e is detectable");

            // A partial explicit selection is never a complete profile.
            var partial = NightlyPolicy.RequiredSuitesPresent(new[] { "antiphon" }, rc);
            partial.Ok.ShouldBeFalse("partial selection is incomplete");
            partial.Missing.Length.ShouldBeGreaterThanOrEqualTo(2, "partial selection names the missing suites");
            NightlyPolicy.RequiredSuitesPresent(rc, rc).Ok.ShouldBeTrue("full rc selection is complete");

            // The declared rows, per suite; the method row keeps its methods.
            var antiphon = NightlyPolicy.ProfileExclusions(policy, "rc", "antiphon", "windows");
            antiphon.Length.ShouldBe(1, "one antiphon row");
            string.Join(",", antiphon[0].Methods).ShouldBe("Live_row", "method row");
            NightlyPolicy.ProfileExclusions(policy, "rc", "", "windows").Length.ShouldBe(2, "blank suite lists every row");

            // Every exclusion row needs a reason and an owner, even one filtered out.
            var bad = Policy(2);
            bad["profiles"]!["rc"]!["exclusions"] = new JsonArray(new JsonObject { ["suite"] = "e2e", ["class"] = "X" });
            var badPolicy = ExecutionPolicy.Parse(bad.ToJsonString());
            Refusal(() => NightlyPolicy.ProfileExclusions(badPolicy, "rc", "antiphon", "windows")).ShouldContain("reason and owner", Case.Sensitive, "exclusion without reason/owner refuses");
        }
        finally { Directory.Delete(root, true); }
    }

    [Test]
    public void Exclusion_platforms()
    {
        var policy = Policy(2);
        var row = policy["profiles"]!["rc"]!["exclusions"]![0]!.AsObject();
        row["platform"] = "linux";
        var parsed = ExecutionPolicy.Parse(policy.ToJsonString());
        NightlyPolicy.ProfileExclusions(parsed, "rc", "e2e", "linux").Length.ShouldBe(1, "linux row applies on linux");
        NightlyPolicy.ProfileExclusions(parsed, "rc", "e2e", "windows").ShouldBeEmpty("linux row is not a windows row");
        Refusal(() => NightlyPolicy.ProfileExclusions(parsed, "rc", "e2e", "macos")).ShouldContain("unsupported exclusion platform", Case.Sensitive, "unknown platform asked");

        var stated = NightlyPolicy.HashText(policy.ToJsonString());
        row["platform"] = "windows";
        NightlyPolicy.HashText(policy.ToJsonString()).ShouldNotBe(stated, "platform-only change changes the hash");

        foreach (var invalid in new JsonNode?[] { "macos", "", "Windows", 1, true, null, new JsonArray("linux"), new JsonObject { ["os"] = "linux" } })
        {
            row["platform"] = invalid;
            var p = ExecutionPolicy.Parse(policy.ToJsonString());
            // Refused before filtering: the bad row is in e2e, the request is for antiphon.
            Refusal(() => NightlyPolicy.ProfileExclusions(p, "rc", "antiphon", "windows")).ShouldContain("invalid platform", Case.Sensitive, "invalid platform " + (invalid?.ToJsonString() ?? "null"));
        }
    }

    [Test]
    public void Hash_is_canonical_over_keys_and_numbers()
    {
        // Key order, whitespace and policyHash do not count; key text, values and numbers as written do.
        var a = NightlyPolicy.HashText("{\"schemaVersion\":2,\"b\":[1,2.5,\"x\\ty\"],\"a\":{\"z\":true,\"y\":null}}");
        NightlyPolicy.HashText("{ \"a\": { \"y\": null, \"z\": true }, \"policyHash\": \"ignored\", \"b\": [1, 2.50, \"x\\ty\"], \"schemaVersion\": 2 }").ShouldBe(a, "reordered and restated");
        NightlyPolicy.HashText("{\"schemaVersion\":2,\"b\":[1,2.5,\"x\\ty\"],\"a\":{\"z\":false,\"y\":null}}").ShouldNotBe(a, "a value");
        NightlyPolicy.HashText("{\"schemaVersion\":2,\"b\":[2,1,2.5,\"x\\ty\"],\"a\":{\"z\":true,\"y\":null}}").ShouldNotBe(a, "array order");
        // Only the top-level policyHash is left out.
        NightlyPolicy.HashText("{\"a\":{\"policyHash\":\"1\"}}").ShouldNotBe(NightlyPolicy.HashText("{\"a\":{\"policyHash\":\"2\"}}"), "nested policyHash counts");
    }

    [Test]
    public void Suites_and_environment()
    {
        var policy = Policy(2);
        policy["safeEnvironment"] = new JsonObject { ["clear"] = new JsonArray("ANTIPHON_TG_TEST_TOKEN"), ["set"] = new JsonObject { ["CI"] = "1" } };
        var parsed = ExecutionPolicy.Parse(policy.ToJsonString());
        string.Join(",", NightlyPolicy.ResolveSelectedSuites(parsed, new[] { "Client, antiphon", "client" })).ShouldBe("client,antiphon", "named suites in first-seen order");
        string.Join(",", NightlyPolicy.ResolveSelectedSuites(parsed, new string[0])).ShouldBe("antiphon,client,scripts", "none named: the default suites");
        Refusal(() => NightlyPolicy.ResolveSelectedSuites(parsed, new[] { "nope" })).ShouldContain("unknown suite nope", Case.Sensitive, "unknown suite refuses");

        var env = NightlyPolicy.SafeChildEnvironment(parsed, "messaging", new System.Collections.Hashtable { ["ANTIPHON_TG_TEST_TOKEN"] = "SECRET", ["PATH"] = "p" });
        env["antiphon_tg_test_token"].ShouldBe("", "credential cleared, names ignore case");
        env["CI"].ShouldBe("1", "set applied");
        env["PATH"].ShouldBe("p", "the rest kept");
        env["ANTIPHON_BROKER_TESTS"].ShouldBe("1", "messaging gets the broker");
        NightlyPolicy.SafeChildEnvironment(parsed, "antiphon", new System.Collections.Hashtable()).ContainsKey("ANTIPHON_BROKER_TESTS").ShouldBeFalse("only messaging gets the broker");
        NightlyPolicy.CredentialLeak("log SECRET", new[] { "", "SECRET" }).ShouldBeTrue("leak found");
        NightlyPolicy.CredentialLeak("log", new[] { "", "SECRET" }).ShouldBeFalse("no leak");
    }

    // ---- fixtures ---------------------------------------------------------------------------------------

    private static JsonObject Policy(int schemaVersion, bool profiles = true, bool omitE2EFromRc = false)
    {
        var suites = new JsonObject
        {
            ["antiphon"] = new JsonObject { ["name"] = "Antiphon.Tests", ["project"] = "tests/Antiphon.Tests/Antiphon.Tests.csproj", ["assembly"] = "Antiphon.Tests", ["mode"] = "unattended" },
            ["client"] = new JsonObject { ["name"] = "client", ["mode"] = "unattended" },
            ["scripts"] = new JsonObject { ["name"] = "scripts", ["mode"] = "unattended" },
            ["e2e"] = new JsonObject { ["name"] = "Antiphon.E2E", ["project"] = "tests/Antiphon.E2E/Antiphon.E2E.csproj", ["assembly"] = "Antiphon.E2E", ["mode"] = "manual" },
        };
        var policy = new JsonObject
        {
            ["schemaVersion"] = schemaVersion,
            ["policyHash"] = "",
            ["defaultSuites"] = new JsonArray("antiphon", "client", "scripts"),
            ["projects"] = new JsonArray("tests/Antiphon.Tests/Antiphon.Tests.csproj", "tests/Antiphon.E2E/Antiphon.E2E.csproj"),
            ["suites"] = suites,
        };
        if (profiles)
        {
            var rc = omitE2EFromRc ? new JsonArray("antiphon", "client", "scripts") : new JsonArray("antiphon", "client", "scripts", "e2e");
            policy["profiles"] = new JsonObject
            {
                ["nightly"] = new JsonObject { ["requiredSuites"] = new JsonArray("antiphon", "client", "scripts"), ["exclusions"] = new JsonArray() },
                ["rc"] = new JsonObject
                {
                    ["requiredSuites"] = rc,
                    ["exclusions"] = new JsonArray(
                        new JsonObject { ["suite"] = "e2e", ["class"] = "LiveCanaryTests", ["reason"] = "real headed provider", ["owner"] = "CARD-0599" },
                        new JsonObject { ["suite"] = "antiphon", ["class"] = "MixedTests", ["methods"] = new JsonArray("Live_row"), ["reason"] = "live row", ["owner"] = "CARD-0599" }),
                },
            };
        }
        return policy;
    }

    // The policy written as root/name/policy.json with its canonical hash stated (or a stale one).
    private static string Write(string root, string name, JsonObject policy, bool staleHash = false)
    {
        policy["policyHash"] = staleHash ? new string('0', 64) : NightlyPolicy.HashText(policy.ToJsonString());
        var dir = Path.Combine(root, name);
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "policy.json");
        File.WriteAllText(path, policy.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        return path;
    }

    private static string Refusal(Action action) => Should.Throw<NightlyRefusal>(action).Message;
    private static string Refusal<T>(Func<T> action) => Should.Throw<NightlyRefusal>(() => { action(); }).Message;

    private static string NewRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "antiphon-policy-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }
}
