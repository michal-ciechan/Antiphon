using System.Text.Json;
using System.Text.Json.Nodes;
using Antiphon.Nightly;
using Antiphon.Tests.Scripts.Nightly;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-0599 V-5: the release-gate Windmill registration (ReleaseGateRegistration, `antiphon-scripts
/// register-release-gates`), in process against a stateful installed-API fake in which write acceptance is separate from
/// stored state, so only a GET readback proves a definition reached the workspace. These cases replaced C599_Preview,
/// C599_ApplyReadback, C599_Concurrency and C599_ScheduleProvenance of scripts/test-release-gate.ps1, which ran the deleted
/// scripts/register-release-gates.ps1 through a Windmill seams file (unit 12 of 2026-11-22-remaining-ps1-to-csharp.md); the
/// row names are kept. The fake stands for the HTTP transport; the definitions are the checked-in scripts/windmill files.
///
/// <para>The decisive behaviours: a preview writes nothing and sends no token; an apply reads every definition back and
/// refuses on content drift; schedules are always created DISABLED and only an explicit --enable-schedule (which itself
/// requires --apply) turns one on; a reapply is idempotent and preserves an already-enabled schedule; concurrent cron or
/// timezone drift refuses instead of overwriting; and the Windmill wrappers derive scheduled provenance from the runner's
/// own schedule context rather than a caller-supplied trigger string. Nothing here touches a live Windmill workspace.</para>
/// </summary>
[Category("Unit")]
public sealed class ReleaseGateRegistrationTests
{
    private const string Token = "wm-secret-token-value";
    private const string BaseUrl = "http://windmill.invalid";
    private static readonly string Definitions = Path.Combine(NightlyPlanCases.Repo, "scripts", "windmill");
    private static readonly string[] Paths = ["u/lndcobra/antiphon_nightly_tests", "u/lndcobra/antiphon_nightly_readiness", "u/lndcobra/antiphon_release_candidates"];

    /// <summary>The installed API: scripts and schedules by path, failures by key (with commit-then-lose-response), a request trace.</summary>
    private sealed class FakeWindmill : IDisposable
    {
        public readonly NightlyFixture Files = NightlyFixture.Create("c599-windmill");
        public readonly Dictionary<string, JsonObject> Scripts = new(StringComparer.Ordinal);
        public readonly Dictionary<string, JsonObject> Schedules = new(StringComparer.Ordinal);
        public readonly HashSet<string> Fail = new(StringComparer.Ordinal);
        public readonly HashSet<string> Commits = new(StringComparer.Ordinal);
        public readonly List<string> Trace = new();
        public readonly List<string> Sent = new();
        public readonly string TokenFile;
        public readonly string Profile;

        public FakeWindmill()
        {
            TokenFile = Files.PathOf("token.txt");
            File.WriteAllText(TokenFile, Token + "\n");
            Profile = Files.PathOf("profile.json");
            WriteProfile(p => { });
        }

        public void WriteProfile(Action<JsonObject> edit)
        {
            var profile = new JsonObject
            {
                ["windmillBaseUrl"] = BaseUrl, ["workspace"] = "mc", ["tokenFile"] = TokenFile, ["repositoryPath"] = @"C:\src\Antiphon",
                ["projectId"] = "00000000-0000-0000-0000-000000000001",
            };
            edit(profile);
            File.WriteAllText(Profile, profile.ToJsonString());
        }

        public NightlyReportHttp Http => (method, uri, headers, body) =>
        {
            var path = uri.StartsWith(BaseUrl, StringComparison.Ordinal) ? uri[BaseUrl.Length..] : uri;
            Trace.Add(method + " " + path);
            Sent.Add(uri + "\n" + (body ?? ""));
            if (headers.TryGetValue("Authorization", out var auth) && auth.Length > "Bearer ".Length)
            {
                auth.ShouldBe("Bearer " + Token);
                Trace.Add("TOKEN-PRESENT");
            }
            var key = path.Contains("/scripts/create", StringComparison.Ordinal) ? "script-create"
                : path.Contains("/schedules/create", StringComparison.Ordinal) ? "schedule-create"
                : path.Contains("/schedules/setenabled", StringComparison.Ordinal) ? "schedule-enable"
                : path.Contains("/scripts/get", StringComparison.Ordinal) ? "script-get"
                : path.Contains("/schedules/get", StringComparison.Ordinal) ? "schedule-get" : "";
            var fail = Fail.Contains(key);
            if (method == "GET")
            {
                if (fail) return new NightlyReportHttpResponse(503, "");
                var store = key == "script-get" ? Scripts : Schedules;
                var name = path[(path.IndexOf(key == "script-get" ? "/scripts/get/p/" : "/schedules/get/", StringComparison.Ordinal) + (key == "script-get" ? 15 : 15))..];
                return store.TryGetValue(name, out var stored) ? new NightlyReportHttpResponse(200, stored.ToJsonString()) : new NightlyReportHttpResponse(404, "");
            }
            if (fail && !Commits.Contains(key)) return new NightlyReportHttpResponse(500, "");
            var payload = JsonNode.Parse(body ?? "{}")!.AsObject();
            if (key == "script-create")
                Scripts[(string)payload["path"]!] = new JsonObject
                {
                    ["path"] = (string)payload["path"]!, ["hash"] = ReleaseGateRegistration.DefinitionDigest(body!), ["content"] = (string)payload["content"]!,
                    ["tag"] = (string)payload["tag"]!,
                };
            else if (key == "schedule-create")
                Schedules[(string)payload["path"]!] = new JsonObject
                {
                    ["path"] = (string)payload["path"]!, ["schedule"] = (string)payload["schedule"]!, ["timezone"] = (string)payload["timezone"]!,
                    ["script_path"] = (string)payload["script_path"]!, ["enabled"] = (bool)payload["enabled"]!, ["tag"] = (string)payload["tag"]!,
                };
            else if (key == "schedule-enable")
            {
                var name = path[(path.IndexOf("/schedules/setenabled/", StringComparison.Ordinal) + "/schedules/setenabled/".Length)..];
                if (Schedules.TryGetValue(name, out var schedule)) schedule["enabled"] = (bool)payload["enabled"]!;
            }
            if (fail) return new NightlyReportHttpResponse(500, "");
            return new NightlyReportHttpResponse(200, "{}");
        };

        public ReleaseGateRegistrationResult Register(bool apply = false, string enable = "", string? definitions = null)
        {
            var options = new ReleaseGateRegistrationOptions { ProfilePath = Profile, Apply = apply, EnableSchedule = enable, DefinitionRoot = definitions ?? Definitions };
            return ReleaseGateRegistration.Run(options, Files.Host(), Http);
        }

        public bool Enabled(string path) => Schedules.TryGetValue(path, out var s) && (bool)s["enabled"]!;

        public void Dispose() => Files.Dispose();
    }

    [Test]
    public void C599_Preview()
    {
        using var fx = new FakeWindmill();
        var rows = new NightlyCaseRows();
        var r = fx.Register();
        rows.Check(r.ExitCode == 0 && r.Mode == "preview", "C599 Preview a plain run is a preview", r.Refusal);
        rows.Check(r.Writes == 0, "C599 Preview a preview performs zero writes", r.Writes.ToString());
        rows.Check(fx.Scripts.Count == 0 && fx.Schedules.Count == 0, "C599 Preview the workspace is untouched after a preview");
        rows.Check(r.Rows.Count == 3, "C599 Preview all three definitions are previewed", r.Rows.Count.ToString());
        foreach (var row in r.Rows)
        {
            rows.Check(row.Action.StartsWith("preview:", StringComparison.Ordinal), $"C599 Preview {row.Key} row is marked preview", row.Action);
            rows.Check(row.DesiredDigest.Length > 0 && row.Cron.Length > 0 && row.Timezone.Length > 0, $"C599 Preview {row.Key} row carries digest, cron and timezone", row.Cron);
        }
        // A preview never reads the token file at all.
        rows.Check(!fx.Trace.Contains("TOKEN-PRESENT"), "C599 Preview a preview sends no token", string.Join(";", fx.Trace));
        var enable = fx.Register(enable: "rc");
        rows.Check(enable.Refusal == "enable-requires-apply" && enable.ExitCode == 3, "C599 Preview -EnableSchedule without -Apply refuses", enable.Refusal);
        rows.ShouldAllPass(12);
    }

    [Test]
    public void C599_ApplyReadback()
    {
        var rows = new NightlyCaseRows();
        using (var fx = new FakeWindmill())
        {
            var r = fx.Register(apply: true);
            rows.Check(r.ExitCode == 0, "C599 ApplyReadback control apply succeeds", r.Refusal);
            foreach (var path in Paths)
            {
                rows.Check(fx.Scripts.ContainsKey(path), $"C599 ApplyReadback script {path} is stored");
                rows.Check(fx.Schedules.ContainsKey(path), $"C599 ApplyReadback schedule {path} is stored");
                rows.Check(fx.Schedules.ContainsKey(path) && !fx.Enabled(path), $"C599 ApplyReadback schedule {path} was created DISABLED");
            }
            var rc = fx.Schedules.GetValueOrDefault(Paths[2]);
            rows.Check((string?)rc?["schedule"] == "0 30 8,16 * * *", "C599 ApplyReadback the rc cron is the two-slot schedule", rc?["schedule"]?.ToString() ?? "");
            rows.Check((string?)rc?["timezone"] == "Europe/London", "C599 ApplyReadback the rc timezone is Europe/London");
            rows.Check((string?)fx.Schedules.GetValueOrDefault(Paths[0])?["schedule"] == "0 30 0 * * *", "C599 ApplyReadback the master cron is unchanged");
            foreach (var row in r.Rows)
                rows.Check(row.ReadbackHash.Length > 0, $"C599 ApplyReadback {row.Key} recorded a readback hash", row.ReadbackHash);
            rows.Check(fx.Trace.Contains("TOKEN-PRESENT"), "C599 ApplyReadback apply sends the token from the token file");
            var printed = fx.Files.Out.ToString() + fx.Files.Error.ToString();
            rows.Check(!string.Join("\n", fx.Trace).Contains(Token, StringComparison.Ordinal) && !string.Join("\n", fx.Sent).Contains(Token, StringComparison.Ordinal)
                && !printed.Contains(Token, StringComparison.Ordinal), "C599 ApplyReadback the token VALUE never reaches the trace");

            // Idempotent reapply: no duplicates, and an enabled schedule stays enabled.
            var enabled = fx.Register(apply: true, enable: "readiness");
            rows.Check(enabled.ExitCode == 0 && enabled.Enabled == "readiness", "C599 ApplyReadback an explicit selector enables exactly one schedule", enabled.Refusal);
            rows.Check(fx.Enabled(Paths[1]), "C599 ApplyReadback the selected schedule is enabled on readback");
            rows.Check(!fx.Enabled(Paths[2]), "C599 ApplyReadback enabling readiness did not enable rc");
            rows.Check(!fx.Enabled(Paths[0]), "C599 ApplyReadback enabling readiness did not enable nightly");
            var again = fx.Register(apply: true);
            rows.Check(again.ExitCode == 0, "C599 ApplyReadback a second apply succeeds", again.Refusal);
            rows.Check(again.Writes == 0, "C599 ApplyReadback a second apply writes nothing (idempotent)", again.Writes.ToString());
            rows.Check(fx.Scripts.Count == 3, "C599 ApplyReadback a second apply created no duplicate scripts", fx.Scripts.Count.ToString());
            rows.Check(fx.Enabled(Paths[1]), "C599 ApplyReadback a reapply PRESERVES an already enabled schedule");
        }

        // Persistence cuts across each write, with ready and unavailable recipients.
        foreach (var key in new[] { "script-create", "schedule-create" })
            foreach (var cut in new[] { "before-write", "commit-then-lose-response" })
            {
                using var fx = new FakeWindmill();
                fx.Fail.Add(key);
                if (cut != "before-write") fx.Commits.Add(key);
                var bad = fx.Register(apply: true);
                rows.Check(bad.ExitCode != 0, $"C599 ApplyReadback {key} {cut} fails the apply", bad.Refusal);
                if (cut == "before-write" && key == "script-create")
                    rows.Check(fx.Scripts.Count == 0, "C599 ApplyReadback a failed script write stored nothing");
                fx.Fail.Remove(key);
                fx.Commits.Remove(key);
                var recovered = fx.Register(apply: true);
                rows.Check(recovered.ExitCode == 0, $"C599 ApplyReadback {key} {cut} restart completes the apply", recovered.Refusal);
                rows.Check(fx.Scripts.Count == 3 && fx.Schedules.Count == 3, $"C599 ApplyReadback {key} {cut} recovery ends with exactly three of each", fx.Scripts.Count.ToString());
            }

        // A readback outage is not success.
        using (var fx = new FakeWindmill())
        {
            fx.Fail.Add("script-get");
            var outage = fx.Register(apply: true);
            rows.Check(outage.ExitCode != 0, "C599 ApplyReadback a readback outage fails the apply", outage.Refusal);
        }

        // An enable that does not read back enabled is not success.
        using (var fx = new FakeWindmill())
        {
            fx.Register(apply: true);
            fx.Fail.Add("schedule-enable");
            var enableFail = fx.Register(apply: true, enable: "rc");
            rows.Check(enableFail.ExitCode != 0, "C599 ApplyReadback a failed enable is not success", enableFail.Refusal);
            rows.Check(!fx.Enabled(Paths[2]), "C599 ApplyReadback the rc schedule is still disabled after a failed enable");
        }
        rows.ShouldAllPass(42);
    }

    [Test]
    public void C599_Concurrency()
    {
        var rows = new NightlyCaseRows();
        using (var fx = new FakeWindmill())
        {
            fx.Register(apply: true);
            fx.Schedules[Paths[2]]["schedule"] = "0 0 3 * * *";
            var drift = fx.Register(apply: true);
            rows.Check(drift.Refusal == "schedule-drift" && drift.ExitCode == 3, "C599 Concurrency concurrent cron drift refuses", drift.Refusal);
            rows.Check((string?)fx.Schedules[Paths[2]]["schedule"] == "0 0 3 * * *", "C599 Concurrency the drifted schedule was not silently overwritten");
        }
        using (var fx = new FakeWindmill())
        {
            fx.Register(apply: true);
            fx.Schedules[Paths[0]]["timezone"] = "UTC";
            var tz = fx.Register(apply: true);
            rows.Check(tz.Refusal == "schedule-drift", "C599 Concurrency concurrent timezone drift refuses", tz.Refusal);
        }
        rows.ShouldAllPass(3);
    }

    [Test]
    public void C599_ScheduleProvenance()
    {
        var rows = new NightlyCaseRows();
        // The wrappers must derive trigger from the runner's schedule context.
        string Content(string file) => JsonDocument.Parse(File.ReadAllText(Path.Combine(Definitions, file))).RootElement.GetProperty("content").GetString() ?? "";
        var master = Content("antiphon-nightly-tests.json");
        rows.Check(master.Contains("WM_SCHEDULE_PATH", StringComparison.Ordinal), "C599 ScheduleProvenance the master wrapper reads WM_SCHEDULE_PATH");
        rows.Check(master.Contains("TRIGGER=manual", StringComparison.OrdinalIgnoreCase), "C599 ScheduleProvenance the master wrapper defaults to manual");
        rows.Check(master.Contains(Paths[0], StringComparison.OrdinalIgnoreCase), "C599 ScheduleProvenance the master wrapper checks its OWN schedule path");
        rows.Check(master.Contains("-Trigger $TRIGGER", StringComparison.OrdinalIgnoreCase), "C599 ScheduleProvenance the master wrapper passes the derived trigger");
        rows.Check(master.Contains("-Profile nightly", StringComparison.OrdinalIgnoreCase), "C599 ScheduleProvenance the master wrapper pins the nightly profile");
        var rcText = File.ReadAllText(Path.Combine(Definitions, "antiphon-release-candidates.json"));
        var rcContent = Content("antiphon-release-candidates.json");
        rows.Check(rcContent.Contains("WM_SCHEDULE_PATH", StringComparison.Ordinal), "C599 ScheduleProvenance the rc wrapper reads WM_SCHEDULE_PATH");
        rows.Check(rcContent.Contains(Paths[2], StringComparison.OrdinalIgnoreCase), "C599 ScheduleProvenance the rc wrapper checks its own schedule path");
        rows.Check(!rcContent.Contains("TRIGGER=scheduled", StringComparison.OrdinalIgnoreCase), "C599 ScheduleProvenance the rc wrapper never claims scheduled master credit");
        using (var rcSchedule = JsonDocument.Parse(File.ReadAllText(Path.Combine(Definitions, "antiphon-release-candidates.schedule.json"))))
        {
            rows.Check(rcSchedule.RootElement.GetProperty("enabled").ValueKind == JsonValueKind.False, "C599 ScheduleProvenance the checked-in rc schedule payload is disabled");
            var cron = rcSchedule.RootElement.GetProperty("schedule").GetString();
            rows.Check(cron == "0 30 8,16 * * *", "C599 ScheduleProvenance the rc payload carries the two-slot cron", cron ?? "");
        }

        // CARD-0616: a raw JSON escape in a definition's human-facing prose (a Windows path whose \releases became a
        // carriage return) survives registration and misreads the producer-owned clone path in the Windmill UI.
        var dirty = new List<string>();
        foreach (var file in Directory.EnumerateFiles(Definitions, "*.json"))
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(file));
            if (doc.RootElement.ValueKind != JsonValueKind.Object) continue;
            foreach (var field in new[] { "summary", "description" })
                if (doc.RootElement.TryGetProperty(field, out var value) && value.ValueKind == JsonValueKind.String && value.GetString()!.Any(c => c < 0x20))
                    dirty.Add(Path.GetFileName(file) + ":" + field);
        }
        rows.Check(dirty.Count == 0, "C599 ScheduleProvenance no definition summary or description carries a control character", string.Join(",", dirty));
        var rcDescription = JsonDocument.Parse(rcText).RootElement.GetProperty("description").GetString() ?? "";
        rows.Check(rcDescription.Contains(@"C:\Antiphon\releases\checkout", StringComparison.OrdinalIgnoreCase),
            "C599 ScheduleProvenance the rc description reads the producer-owned clone path intact", rcDescription);

        // A pre-enabled rc payload is refused rather than registered.
        using (var fx = new FakeWindmill())
        {
            var defs = fx.Files.PathOf("defs");
            Directory.CreateDirectory(defs);
            foreach (var file in Directory.EnumerateFiles(Definitions, "*.json")) File.Copy(file, Path.Combine(defs, Path.GetFileName(file)));
            var schedulePath = Path.Combine(defs, "antiphon-release-candidates.schedule.json");
            var bad = JsonNode.Parse(File.ReadAllText(schedulePath))!.AsObject();
            bad["enabled"] = true;
            File.WriteAllText(schedulePath, bad.ToJsonString());
            var r = fx.Register(apply: true, definitions: defs);
            rows.Check(r.Refusal == "rc-schedule-preenabled" && r.ExitCode == 3 && !fx.Schedules.ContainsKey(Paths[2]) && !fx.Scripts.ContainsKey(Paths[2]),
                "C599 ScheduleProvenance a pre-enabled rc payload refuses", r.Refusal);
        }

        // Profile hygiene: a non-mc workspace and an inline token both refuse.
        using (var fx = new FakeWindmill())
        {
            fx.WriteProfile(p => p["workspace"] = "other");
            var r = fx.Register();
            rows.Check(r.Refusal == "profile-invalid" && r.ExitCode == 3 && fx.Trace.Count == 0, "C599 ScheduleProvenance a non-mc workspace refuses", r.Refusal);
        }
        using (var fx = new FakeWindmill())
        {
            fx.WriteProfile(p => p["token"] = "inline-secret");
            var r = fx.Register();
            rows.Check(r.Refusal == "profile-invalid" && fx.Trace.Count == 0, "C599 ScheduleProvenance an inline token in the profile refuses", r.Refusal);
        }
        using (var fx = new FakeWindmill())
        {
            File.Delete(fx.TokenFile);
            var r = fx.Register(apply: true);
            rows.Check(r.Refusal == "token-unavailable" && r.ExitCode == 3 && fx.Trace.Count == 0, "C599 ScheduleProvenance a missing token file refuses the apply", r.Refusal);
        }
        rows.ShouldAllPass(16);
    }

    [Test]
    public void Refusals_print_one_line_and_the_rows_print_without_the_token()
    {
        using (var fx = new FakeWindmill())
        {
            fx.WriteProfile(p => p.Remove("projectId"));
            var r = fx.Register();
            r.Refusal.ShouldBe("profile-invalid");
            fx.Files.Out.ToString().Trim().ShouldBe("[register-release-gates] REFUSED: profile field projectId is required");
        }
        using (var fx = new FakeWindmill())
        {
            var r = fx.Register(apply: true);
            r.ExitCode.ShouldBe(0);
            var lines = fx.Files.Out.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            lines.Length.ShouldBe(3);
            lines[0].ShouldStartWith("[register-release-gates] nightly: create-script+create-schedule cron=0 30 0 * * * tz=");
            lines[0].ShouldContain(" liveEnabled=False");
            fx.Files.Out.ToString().ShouldNotContain(Token);
        }
        using (var fx = new FakeWindmill())
        {
            var r = fx.Register(definitions: fx.Files.PathOf("no-definitions"));
            r.Refusal.ShouldBe("definition-missing");
            r.ExitCode.ShouldBe(3);
        }
    }

    [Test]
    public void Options_take_the_script_parameters_in_kebab_case()
    {
        var o = ReleaseGateRegistrationOptions.Parse(["--profile", "p.json"], "D");
        o.ProfilePath.ShouldBe("p.json");
        o.Apply.ShouldBeFalse();
        o.EnableSchedule.ShouldBeEmpty();
        o.DefinitionRoot.ShouldBe("D");
        var applied = ReleaseGateRegistrationOptions.Parse(["--profile", "p.json", "--apply", "--enable-schedule", "rc", "--definition-root", "E"], "D");
        applied.Apply.ShouldBeTrue();
        applied.EnableSchedule.ShouldBe("rc");
        applied.DefinitionRoot.ShouldBe("E");
        Should.Throw<NightlyUsageException>(() => ReleaseGateRegistrationOptions.Parse([], "D"));
        Should.Throw<NightlyUsageException>(() => ReleaseGateRegistrationOptions.Parse(["--profile", "p.json", "--enable-schedule", "weekly"], "D"));
    }
}
