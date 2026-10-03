using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Antiphon.Agents.Pty;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Agents;

/// <summary>Real approval frames and explicitly synthetic trust/layout controls; no provider processes.</summary>
[Category("Unit")]
public class GrokLinuxBlockingPromptTests
{
    private const string RealSource = "docs/investigations/2026-10-03-card-1006-linux-grok-frames.json";
    private const string LinuxSource = "tests/Antiphon.Tests/Agents/Fixtures/card1004/linux-startup-frames.json#1.0.41";
    private const string TrustSource = "tests/Antiphon.Tests/Agents/GrokTrustPromptDetectorTests.cs#GrokTrustScreen";
    private const string BlankSource = "CARD-1006#synthetic-blank-canvas";
    private const string Approval = "Approve in your browser to finish signing in.";
    private const string BrowserCode = "Make sure your browser shows this code.";
    private const string Waiting = "Waiting for approval...";
    private const string Question = "Do you trust the contents of this directory?";
    private const string Yes = "\u276f Yes, proceed y";
    private const string No = "No, quit n";

    // Independent literal oracles from the TestDesign freeze, never fixture-declared expectations.
    private static readonly (string Id, string Hash, int Width)[] RealOracles =
    [
        ("C1-connecting", "beb362aeb8dfa6bbb1a2c95eff5e6fcc49527d0acf67c941653d40172fb2b277", 67),
        ("C1-sign-in", "e70a9a5d6630171b164c3bfa3a660e04a590514a0635c4044dd82db25dfb6d06", 85),
        ("C2-sign-in", "9a5793ae8bd3e74c1ef60c9125b516817ae59133b276f6b8aff2f43850707f52", 85)
    ];
    private static readonly (string Id, string Recipe, string Reason, string Hash)[] ProbeOracles =
    [
        ("P-01", "Patch(B,T)", "Trust", "f66b2fea1ba97e618925e2e0bcdfdfc63af78d55f7b23c04d10945546d50212c"),
        ("P-02", "Patch(Patch(B,A),{7:\"  \\u276f Sign in\",8:\"  Exit\"})", "SignIn", "4418ebab3c1ece20cbd1b7d91658c01ceed11a533f83415def04949d31a75544"),
        ("P-03", "Patch(Patch(L,T),{22:\"  Working...\"})", "Trust", "97d11cc8559d13659a638d2daf20e59871f410d99d096471791304be66ebe86a"),
        ("P-04", "Patch(Patch(L,A),{22:\"  Working...\"})", "SignIn", "b9c1f1acb87c8a03ca0a92f3b342c08039a50058b26094c47c0b5a6759f0a4c7"),
        ("P-05", "Patch(L,A)", "SignIn", "923d4c79b2f2c688ffb80cc5f2ed5766a983fff18bcc95c62715351d21f1fe7b"),
        ("P-06", "Patch(B,{5:\"  Update available\",7:\"  \\u276f 1. Update now\",8:\"  2. Later\"})", "Unknown", "2b37f534e6fcc26fd6b3929350928ccea9d43f68f53d10e3416cf79705c57d74"),
        ("P-07", "Patch(B,{9:\"  \\u256d\"+repeat(\"\\u2500\",58)+\"\\u256e\",10:\"  \\u2502 \"+PadRight(\"A new version is available\",57)+\"\\u2502\",11:\"  \\u2502 \"+PadRight(\"Press Enter to update\",57)+\"\\u2502\",12:\"  \\u2570\"+repeat(\"\\u2500\",58)+\"\\u256f\"})", "Unknown", "205dff66669ae99acdab778a9aefdd5c0662cb487765b31cc07260b95a53addb"),
        ("P-08", "Patch(L,{25:I(\"\\u276f 1. Yes\")})", "ComposerUnavailable", "e1323daca816ab8c4da430ee88dff35429c4202271b4084d749c4d492ff952d1"),
        ("P-09", "Patch(B,{9:U,10:I(\"\\u276f\"),11:D})", "Unknown", "d44437ad4d8458fc44208507451b7ac65a255be4d928d6efaacaefa91fa795a6"),
        ("P-10", "Patch(B,{11:U,12:I(\"\\u276f\"),13:D})", "Unknown", "e220881f8aa44b658d08fce841320d43bfb0a9316396d3fad682112c2e6a9ee3"),
        ("P-11", "Patch(B,{20:\"  \\u276f\"})", "Unknown", "725b420cec04e6a64cc41168699f49f86d23772b23580ecd326fe8cb56ae3d33"),
        ("P-12", "Patch(B,{25:\"    \\u276f\"})", "Unknown", "5080a26557c91e2960b1aec7ecca46f6c802a74553d9cec5cf5a4c9b0f1a3014"),
        ("P-13", "Patch(L,{25:I(\"\\u276f typed text\")})", "ComposerUnavailable", "a66c40b80ec6ff4d219a2e042f735a57b0c28793350bb1d548d7c8bcef1cc3c1"),
        ("P-14", "Patch(L,{25:I(\">\\u276f\")})", "ComposerUnavailable", "0b2c173e1941f51dc5707b231311776afd9fb77d43f3dcf979ca236ae3455bae"),
        ("P-15", "Patch(L,{25:I(\"\\u276f\\u276f\")})", "ComposerUnavailable", "b7c5f4c1fd591bee881f8e7e0b8be1681fc271f45fa03d509d840766930c27e1"),
        ("P-16", "Patch(L,{28:\"\"})", "Unknown", "bd37147536b6b6e8f486fa6de369ade22257fc0930090c0f5031c4794d3e4c35"),
        ("P-17", "Patch(L,{22:\"  Working...\"})", "Working", "a6a585d8e37768bb383509566583860176d3200eabda780f8ed2c834b993be42"),
        ("P-18", "Patch(L,{26:\"\"})", "ComposerUnavailable", "500edace876383c5ce7200c4f07c444eb41530ee9d74ff487ab467e332048f28"),
        ("P-19", "L.Take(29)", "Unknown", "adbbfa427a081bd2ff1d39eeede70ba02c04e23ce8d425eba62c06a84f2a3358"),
    ];

    [Test]
    public void C1006_Real_sign_in_is_not_ready()
    {
        using var document = ReadReal();
        foreach (var id in new[] { "C1-sign-in", "C2-sign-in" })
        {
            var original = Screen(Real(document, id));
            foreach (var replacement in new[] { "<CODE-9> ", "         ", "XXXXXXXXX" })
            {
                var variant = replacement == "<CODE-9> " ? "original" : replacement == "XXXXXXXXX" ? "nine-X" : "nine-space";
                var rows = original.Split('\n');
                var changed = rows.ToArray();
                changed[15] = rows[15][..56] + replacement + rows[15][65..];
                changed[15].Length.ShouldBe(rows[15].Length);
                for (var row = 0; row < rows.Length; row++)
                    if (row != 15) changed[row].ShouldBe(rows[row]);
                var screen = Join(changed);
                RequireApproval(screen);
                GrokSignInPromptDetector.IsVisibleOnScreen(screen).ShouldBeTrue($"realSignInDetector:{id}:{variant}");
                GrokTrustPromptDetector.IsVisibleOnScreen(screen).ShouldBeFalse();
                AssertObservation(screen, GrokStartupReason.SignIn, $"{id}:{variant}");
            }
        }
        var connecting = Screen(Real(document, "C1-connecting"));
        GrokSignInPromptDetector.IsVisibleOnScreen(connecting).ShouldBeFalse();
        GrokTrustPromptDetector.IsVisibleOnScreen(connecting).ShouldBeFalse();
        AssertObservation(connecting, GrokStartupReason.Unknown, "C1-connecting");
    }

    [Test]
    public void C1006_Synthetic_trust_is_not_ready()
    {
        using var document = ReadSynthetic();
        var trust = Probe(document, "P-01");
        trust.GetProperty("label").GetString().ShouldBe("synthetic-derived");
        trust.GetProperty("source").GetString().ShouldBe(TrustSource);
        var screen = Screen(trust);
        GrokTrustPromptDetector.IsVisibleOnScreen(screen).ShouldBeTrue("syntheticTrustDetector:P-01");
        GrokSignInPromptDetector.IsVisibleOnScreen(screen).ShouldBeFalse();
        AssertObservation(screen, GrokStartupReason.Trust, "P-01");
        GrokTrustPromptDetector.IsVisibleOnScreen(Join(Patch(Blank(), new() { [5] = "  Yes, proceed y" })))
            .ShouldBeFalse("partialTrust:yes-only");
        GrokTrustPromptDetector.IsVisibleOnScreen(Join(Patch(Blank(), new() { [5] = "  " + Question })))
            .ShouldBeFalse("partialTrust:question-only");
    }

    [Test]
    public void C1006_Fixture_bytes_and_provenance_are_pinned()
    {
        using var real = ReadReal();
        using var synthetic = ReadSynthetic();
        real.RootElement.GetProperty("schemaVersion").GetInt32().ShouldBe(1);
        synthetic.RootElement.GetProperty("schemaVersion").GetInt32().ShouldBe(1);
        var captures = real.RootElement.GetProperty("captures").EnumerateArray().ToArray();
        captures.Select(x => x.GetProperty("captureId").GetString()).ShouldBe(RealOracles.Select(x => x.Id));
        foreach (var (id, hash, width) in RealOracles)
        {
            var capture = Real(real, id);
            var screen = Screen(capture);
            Digest(screen).ShouldBe(hash, $"realHash:{id}");
            capture.GetProperty("sha256").GetString().ShouldBe(hash);
            capture.GetProperty("label").GetString().ShouldBe(id == "C1-connecting" ? "real-rendered" : "real-rendered-redacted");
            capture.GetProperty("source").GetString().ShouldBe(RealSource + "#" + id);
            capture.GetProperty("cliVersion").GetString().ShouldBe("1.0.41");
            capture.GetProperty("host").GetString().ShouldBe("Linux runner container (Debian 12)");
            capture.GetProperty("session").ValueKind.ShouldBe(JsonValueKind.Null);
            capture.GetProperty("cols").GetInt32().ShouldBe(120);
            capture.GetProperty("rows").GetInt32().ShouldBe(30);
            var rows = screen.Split('\n');
            rows.Length.ShouldBe(30);
            rows.Max(x => x.Length).ShouldBe(width);
            var redactions = capture.GetProperty("redactions").EnumerateArray().ToArray();
            if (id == "C1-connecting")
            {
                redactions.ShouldBeEmpty();
                screen.ShouldNotContain("<CODE-9>");
            }
            else
            {
                rows[15].ShouldBe(new string(' ', 56) + "<CODE-9> ", $"redactionCells:{id}");
                redactions.Length.ShouldBe(1);
                var r = redactions[0];
                r.GetProperty("category").GetString().ShouldBe("device-code");
                r.GetProperty("row").GetInt32().ShouldBe(15);
                r.GetProperty("column").GetInt32().ShouldBe(56);
                r.GetProperty("length").GetInt32().ShouldBe(9);
                r.GetProperty("replacement").GetString().ShouldBe("<CODE-9> ");
                RequireMeasuredApproval(screen);
            }
        }
        var provenance = real.RootElement.GetProperty("provenance");
        foreach (var (key, value) in new (string, string)[]
        {
            ("sourceTask", "1e39a459"), ("sourcePath", RealSource),
            ("sourceSha", "8d3148dcf6d0040ef1a69b0e2fceb38ce1a77b18"),
            ("cliVersionOutput", "1.0.41 (4220f3b224a6)"),
            ("runnerBuild", "4358939ecd85d6e7ff0941f970879499cb930e3d"),
            ("backend", "InboxConhost"), ("isolation", "local-runner-in-existing-container"),
            ("rawAnsiLogCustody", "temporary-scratch-deleted-not-tmpfs-qualified"),
            ("imageIdentityStatus", "unavailable"), ("sessionIdentityStatus", "not-retained")
        }) provenance.GetProperty(key).GetString().ShouldBe(value, key);
        provenance.GetProperty("inputCalls").GetInt32().ShouldBe(0);
        provenance.GetProperty("inputBytes").GetInt32().ShouldBe(0);
        provenance.GetProperty("imageTag").ValueKind.ShouldBe(JsonValueKind.Null);
        provenance.GetProperty("imageDigest").ValueKind.ShouldBe(JsonValueKind.Null);
        var probes = synthetic.RootElement.GetProperty("probes").EnumerateArray().ToArray();
        probes.Length.ShouldBe(19);
        probes.Select(x => x.GetProperty("probeId").GetString()).ShouldBe(ProbeOracles.Select(x => x.Id));
        for (var i = 0; i < ProbeOracles.Length; i++)
        {
            var (id, recipe, reason, hash) = ProbeOracles[i];
            var probe = probes[i];
            probe.GetProperty("label").GetString().ShouldBe("synthetic-derived", id);
            probe.GetProperty("source").GetString().ShouldBe(ProbeSource(id), id);
            probe.GetProperty("transformation").GetString().ShouldBe(recipe, id);
            probe.GetProperty("expectedReason").GetString().ShouldBe(reason, id);
            probe.GetProperty("sha256").GetString().ShouldBe(hash, id);
            Digest(Screen(probe)).ShouldBe(hash, $"syntheticHash:{id}");
            probe.GetProperty("cols").GetInt32().ShouldBe(120);
            var rows = Screen(probe).Split('\n');
            rows.Length.ShouldBe(id == "P-19" ? 29 : 30);
            probe.GetProperty("rows").GetInt32().ShouldBe(rows.Length);
            foreach (var forbidden in new[] { "session", "host", "cliVersion", "capturedAt" })
                probe.TryGetProperty(forbidden, out _).ShouldBeFalse($"synthetic provenance:{id}:{forbidden}");
        }
        foreach (var (file, document) in new[] { ("linux-blocking-frames.json", real), ("synthetic-blocking-frames.json", synthetic) })
        {
            var raw = File.ReadAllText(FixturePath(file));
            raw.All(c => c is >= ' ' and <= '~' or '\n' or '\r' or '\t').ShouldBeTrue("ASCII fixture JSON");
            using var roundTrip = JsonDocument.Parse(JsonSerializer.Serialize(document.RootElement));
            JsonElement.DeepEquals(document.RootElement, roundTrip.RootElement).ShouldBeTrue("fixture round-trip");
        }
        Digest(GrokLinuxStartupFixture.Screen("1.0.41")).ShouldBe("f2155460374433c4cd5da3849afc466541604d2d8faf9c9b1c71c93d02c6c1ec");
        Digest(GrokStartupFixture.ReadyScreen()).ShouldBe("708f5423814ba83ecce9769207051e30d69d7e1b15ab7483aa1d6ca34fe718e3");
        Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory,
            "Agents", "Fixtures", "card0778", "startup-frames.json"))))
            .ShouldBe("6990d5910e3983005afc6f7dd1eec002f8c59686613c80d081e0d3953394dbee");
        var note = File.ReadAllText(FixturePath("provenance.md"));
        note.ShouldContain("## REAL");
        note.ShouldContain("## SYNTHETIC");
        note.ShouldContain("not-observed");
        note.ShouldContain("2026-10-03-card-1006-a2-capture-receipt.md");
    }

    [Test]
    public void C1006_Blockers_override_valid_composers()
    {
        using var real = ReadReal();
        foreach (var (baseId, screen) in new[] { ("W", GrokStartupFixture.ReadyScreen()), ("L", GrokLinuxStartupFixture.Screen("1.0.41")) })
        {
            var original = screen.Split('\n');
            GrokStartupScreen.Classify(screen).IsReady.ShouldBeTrue($"baseReady:{baseId}");
            foreach (var id in new[] { "C1-sign-in", "C2-sign-in", "P-01" })
            {
                var map = TrustAnchors();
                if (id != "P-01")
                {
                    var source = Screen(Real(real, id));
                    RequireMeasuredApproval(source);
                    var measured = source.Split('\n');
                    map = new() { [3] = "  " + measured[13][38..], [4] = "  " + measured[17][41..], [5] = "  " + measured[25][49..] };
                }
                var overlay = Overlay(original, map, $"synthetic-derived:{baseId}:{id}");
                foreach (var row in new[] { 24, 25, 26 }) overlay[row].ShouldBe(original[row]);
                overlay[22].ShouldBe(""); overlay[23].ShouldBe("");
                overlay[28].ShouldBe("  Shift+Tab:mode  │  Ctrl+x:shortcuts");
                var observation = GrokStartupScreen.Classify(Join(overlay));
                observation.Reason.ShouldBe(id == "P-01" ? GrokStartupReason.Trust : GrokStartupReason.SignIn, $"modalReason:{baseId}:{id}");
                observation.IsReady.ShouldBeFalse($"modalReady:{baseId}:{id}");
            }
        }
        var linux = GrokLinuxStartupFixture.Screen("1.0.41").Split('\n');
        GrokStartupScreen.Classify(Join(linux)).IsReady.ShouldBeTrue("geometryBaseReady");
        var geometry = new Dictionary<string, Dictionary<int, string>>
        {
            ["left-3"] = new() { [24] = "   ╭" + new string('─', 113) + "╮", [25] = "   │ " + "❯".PadRight(112) + "│", [26] = "   ╰" + new string('─', 113) + "╯" },
            ["right-116"] = new() { [24] = "  ╭" + new string('─', 113) + "╮", [25] = "  │ " + "❯".PadRight(112) + "│", [26] = "  ╰" + new string('─', 113) + "╯" },
            ["upper-tail"] = new() { [24] = linux[24] + " " },
            ["upper-prefix"] = new() { [24] = ReplaceCell(linux[24], 0, 'x') },
            ["upper-dash"] = new() { [24] = ReplaceCell(linux[24], 50, '=') },
            ["input-tail"] = new() { [25] = linux[25] + " " },
            ["input-left"] = new() { [25] = ReplaceCell(linux[25], 2, 'x') },
            ["input-right"] = new() { [25] = ReplaceCell(linux[25], 117, 'x') },
            ["bottom-left"] = new() { [26] = ReplaceCell(linux[26], 2, 'x') },
            ["bottom-right"] = new() { [26] = ReplaceCell(linux[26], 117, 'x') }
        };
        geometry.Count.ShouldBe(10);
        foreach (var (id, map) in geometry)
        {
            var observation = GrokStartupScreen.Classify(Join(Overlay(linux, map, "synthetic-derived:geometry:" + id)));
            observation.IsReady.ShouldBeFalse($"geometryReady:{id}");
            observation.Reason.ShouldBe(id.StartsWith("input-", StringComparison.Ordinal) || id.StartsWith("bottom-", StringComparison.Ordinal)
                ? GrokStartupReason.ComposerUnavailable : GrokStartupReason.Unknown, $"geometryReason:{id}");
        }
    }

    [Test]
    public void C1006_Current_frame_overrides_raw_history()
    {
        using var real = ReadReal(); using var synthetic = ReadSynthetic();
        var blockers = new[] { ("C1-sign-in", Screen(Real(real, "C1-sign-in")), GrokStartupReason.SignIn),
            ("C2-sign-in", Screen(Real(real, "C2-sign-in")), GrokStartupReason.SignIn),
            ("P-01", Screen(Probe(synthetic, "P-01")), GrokStartupReason.Trust) };
        var linux = GrokLinuxStartupFixture.Screen("1.0.41");
        foreach (var (id, screen, reason) in blockers)
        {
            var current = GrokStartupScreen.Classify(screen, linux);
            current.Reason.ShouldBe(reason, $"currentReason:{id}:L");
            current.IsReady.ShouldBeFalse();
        }
        foreach (var (id, screen) in new[] { ("W", GrokStartupFixture.ReadyScreen()), ("L", linux) })
            foreach (var (rawId, raw, _) in blockers)
            {
                var current = GrokStartupScreen.Classify(screen, raw);
                current.Reason.ShouldBe(GrokStartupReason.Ready, $"currentReason:{id}:{rawId}");
                current.IsReady.ShouldBeTrue();
            }
    }

    [Test]
    public void C1006_All_19_fail_open_shapes_stay_closed()
    {
        using var synthetic = ReadSynthetic();
        var cases = 0;
        foreach (var (id, _, reason, _) in ProbeOracles)
        {
            var rows = Screen(Probe(synthetic, id)).Split('\n');
            var observation = GrokStartupScreen.Classify(Join(rows));
            observation.IsReady.ShouldBeFalse($"probeReady:{id}:base (synthetic-derived)");
            observation.Reason.ToString().ShouldBe(reason, $"probeReason:{id}:base");
            cases++;
        }
        foreach (var (variant, anchors, expected) in new[] { ("A", ApprovalAnchors(), GrokStartupReason.SignIn), ("T", TrustAnchors(), GrokStartupReason.Trust) })
            foreach (var (id, _, _, _) in ProbeOracles)
            {
                var layout = DerivedLayout(id, Screen(Probe(synthetic, id)).Split('\n'));
                var overlay = Overlay(layout, anchors, $"synthetic-derived:{id}:{variant}");
                var screen = Join(overlay);
                if (variant == "A") { RequireApproval(screen); GrokTrustPromptDetector.IsVisibleOnScreen(screen).ShouldBeFalse(); }
                else { RequireTrust(screen); GrokSignInPromptDetector.IsVisibleOnScreen(screen).ShouldBeFalse(); }
                var observation = GrokStartupScreen.Classify(screen);
                observation.IsReady.ShouldBeFalse($"probeReady:{id}:{variant} (synthetic-derived)");
                observation.Reason.ShouldBe(expected, $"probeReason:{id}:{variant}");
                cases++;
            }
        cases.ShouldBe(57, "probeCount");
    }

    [Test]
    public void C1006_Blocker_resets_readiness_settlement()
    {
        using var real = ReadReal(); using var synthetic = ReadSynthetic();
        var ready = GrokStartupScreen.Classify(GrokLinuxStartupFixture.Screen("1.0.41"));
        ready.IsReady.ShouldBeTrue();
        foreach (var (id, screen) in new[] { ("C1-sign-in", Screen(Real(real, "C1-sign-in"))), ("C2-sign-in", Screen(Real(real, "C2-sign-in"))), ("P-01", Screen(Probe(synthetic, "P-01"))) })
        {
            var tracker = new GrokReadyTracker(TimeSpan.FromMilliseconds(1000));
            tracker.Observe(ready, TimeSpan.Zero).ShouldBeFalse("firstReadyObservation");
            tracker.PositiveObservations.ShouldBe(1);
            tracker.Observe(GrokStartupScreen.Classify(screen), TimeSpan.FromMilliseconds(900)).ShouldBeFalse();
            tracker.PositiveObservations.ShouldBe(0, $"resetCount:{id}");
            tracker.Observe(ready, TimeSpan.FromMilliseconds(950)).ShouldBeFalse();
            tracker.PositiveObservations.ShouldBe(1);
            tracker.Observe(ready, TimeSpan.FromMilliseconds(1000)).ShouldBeFalse($"readyAt1000:{id}");
            tracker.Observe(ready, TimeSpan.FromMilliseconds(1949)).ShouldBeFalse();
            tracker.Observe(ready, TimeSpan.FromMilliseconds(1950)).ShouldBeTrue();
        }
        var zero = new GrokReadyTracker(TimeSpan.Zero);
        zero.Observe(ready, TimeSpan.Zero).ShouldBeFalse("firstReadyObservation:zero-settle");
        zero.Observe(ready, TimeSpan.Zero).ShouldBeTrue();
    }

    [Test]
    public async Task C1006_Sign_in_precedes_trust_and_types_nothing()
    {
        using var real = ReadReal();
        var scripts = new List<(string Id, string Screen)>();
        foreach (var id in new[] { "C1-sign-in", "C2-sign-in" })
        {
            var screen = Screen(Real(real, id)); RequireMeasuredApproval(screen);
            scripts.Add((id, screen));
            var rows = GrokLinuxStartupFixture.Screen("1.0.41").Split('\n');
            rows = Overlay(rows, ApprovalAnchors(), "synthetic-derived:mixed:" + id);
            rows = Overlay(rows, new() { [7] = "  " + Question, [8] = "  " + Yes, [9] = "  " + No }, "synthetic-derived:mixed:" + id);
            var mixed = Join(rows); RequireApproval(mixed); RequireTrust(mixed);
            GrokStartupScreen.Classify(mixed).Reason.ShouldBe(GrokStartupReason.SignIn, $"mixedReason:{id}");
            scripts.Add(("mixed:" + id, mixed));
        }
        foreach (var (id, screen) in scripts.OrderBy(x => x.Id.StartsWith("mixed:", StringComparison.Ordinal)))
        {
            var clock = new PollClock();
            var inputs = new List<string>(); var callbacks = new List<GrokStartupSnapshot>();
            Failure? failure = null;
            var frame = new GrokStartupSnapshot(screen, "", 1, clock.GetUtcNow().UtcDateTime);
            var options = Options(clock, onFailure: f => failure = f, onSignIn: callbacks.Add);
            var result = await GrokReadyWait.WaitAsync(_ => Task.FromResult<GrokStartupSnapshot?>(frame), options,
                (input, _) => { inputs.Add(input); return Task.CompletedTask; });
            result.ShouldBeFalse($"signInReady:{id}");
            inputs.ShouldBeEmpty($"signInInputs:{id}");
            callbacks.Count.ShouldBe(1, $"signInCallbackCount:{id}");
            ReferenceEquals(callbacks[0], frame).ShouldBeTrue("complete same sign-in frame");
            failure.ShouldNotBeNull();
            failure.Outcome.ShouldBe(GrokStartupReason.SignIn);
            failure.LastReason.ShouldBe(GrokStartupReason.SignIn);
            failure.Count.ShouldBe(0);
            failure.SignInSeen.ShouldBeTrue($"failureSignInSeen:{id}");
            ReferenceEquals(failure.Frame, frame).ShouldBeTrue();
        }
    }

    [Test]
    public async Task C1006_Trust_remains_blocked_until_cleared()
    {
        using var synthetic = ReadSynthetic();
        var trust = Screen(Probe(synthetic, "P-01"));
        var ready = GrokLinuxStartupFixture.Screen("1.0.41");
        var persistent = await DriveWaitAsync(_ => trust);
        persistent.Ready.ShouldBeFalse("persistentTrustReady");
        persistent.Inputs.ShouldBe(new[] { "y" }, customMessage: "persistentTrustInputs");
        persistent.Failure.ShouldNotBeNull();
        persistent.Failure.Outcome.ShouldBe(GrokStartupReason.Trust);
        persistent.Failure.LastReason.ShouldBe(GrokStartupReason.Trust);
        persistent.Failure.Elapsed.ShouldBe(TimeSpan.FromMilliseconds(1000));
        var cleared = await DriveWaitAsync(ms => ms < 100 ? trust : ready, assertAt1050: true);
        cleared.Ready.ShouldBeTrue("clearedTrustReady");
        cleared.Inputs.ShouldBe(new[] { "y" }, customMessage: "clearedTrustInputs");
        cleared.Elapsed.ShouldBe(TimeSpan.FromMilliseconds(1100));
        cleared.Failure.ShouldBeNull();
    }

    [Test]
    public void C1006_Update_fixture_policy_and_negatives()
    {
        using var real = ReadReal(); using var synthetic = ReadSynthetic();
        foreach (var id in new[] { "P-06", "P-07" })
        {
            var probe = Probe(synthetic, id);
            probe.GetProperty("label").GetString().ShouldBe("synthetic-derived");
            AssertObservation(Screen(probe), GrokStartupReason.Unknown, id);
        }
        var update = real.RootElement.GetProperty("realUpdate");
        update.GetProperty("status").GetString().ShouldBe("not-observed");
        update.GetProperty("captureIds").EnumerateArray().ShouldBeEmpty();
        foreach (var frame in RealUpdateFrames(real.RootElement))
            GrokStartupScreen.Classify(Screen(frame)).IsReady.ShouldBeFalse("real updater frame");
        foreach (var invalid in new[] { "{\"realUpdate\":{\"status\":\"unknown\",\"captureIds\":[]}}", "{\"realUpdate\":{\"status\":\"captured\",\"captureIds\":[]}}" })
        {
            using var bad = JsonDocument.Parse(invalid);
            Should.Throw<InvalidDataException>(() => RealUpdateFrames(bad.RootElement));
        }
    }

    [Test]
    public void C1006_Windows_fixture_results_are_unchanged()
    {
        using var windows = GrokStartupFixture.Read();
        var counts = new List<int>(); var count = 0;
        foreach (var capture in windows.RootElement.GetProperty("captures").EnumerateObject().Where(x => x.Value.TryGetProperty("checkpoints", out _)))
        {
            var frames = capture.Value.GetProperty("checkpoints").EnumerateArray().ToArray();
            counts.Add(frames.Length);
            foreach (var frame in frames)
            {
                var id = $"{capture.Name}:{frame.GetProperty("afterChunk").GetInt32()}";
                var expected = frame.GetProperty("expectedReason").GetString();
                var observation = GrokStartupScreen.Classify(Screen(frame));
                observation.Reason.ToString().ShouldBe(expected, $"windowsReason:{id}");
                observation.IsReady.ShouldBe(expected == "Ready", $"windowsReady:{id}");
                count++;
            }
        }
        count.ShouldBe(114, "windowsCount");
        counts.Order().ShouldBe(new[] { 2, 44, 68 });
    }

    private static string FixturePath(string file) => Path.Combine(AppContext.BaseDirectory, "Agents", "Fixtures", "card1006", file);
    private static JsonDocument ReadReal() => JsonDocument.Parse(File.ReadAllText(FixturePath("linux-blocking-frames.json")));
    private static JsonDocument ReadSynthetic() => JsonDocument.Parse(File.ReadAllText(FixturePath("synthetic-blocking-frames.json")));
    private static JsonElement Real(JsonDocument d, string id) => d.RootElement.GetProperty("captures").EnumerateArray().Single(x => x.GetProperty("captureId").GetString() == id);
    private static JsonElement Probe(JsonDocument d, string id) => d.RootElement.GetProperty("probes").EnumerateArray().Single(x => x.GetProperty("probeId").GetString() == id);
    private static string Screen(JsonElement frame) => frame.GetProperty("screen").GetString()!;
    private static string Digest(string screen) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(screen)));
    private static string Join(string[] rows) => string.Join('\n', rows);
    private static string[] Blank() => Enumerable.Repeat("", 30).ToArray();
    private static string ReplaceCell(string row, int column, char value) => row[..column] + value + row[(column + 1)..];
    private static Dictionary<int, string> ApprovalAnchors() => new() { [3] = "  " + Approval, [4] = "  " + BrowserCode, [5] = "  " + Waiting };
    private static Dictionary<int, string> TrustAnchors() => new() { [5] = "  " + Question, [7] = "  " + Yes, [8] = "  " + No };
    private static string[] Patch(string[] rows, Dictionary<int, string> changes)
    {
        var clone = rows.ToArray();
        foreach (var (row, value) in changes) clone[row] = value;
        return clone;
    }
    private static string[] Overlay(string[] rows, Dictionary<int, string> changes, string id)
    {
        var clone = Patch(rows, changes);
        clone.Length.ShouldBe(rows.Length, id);
        for (var row = 0; row < rows.Length; row++) clone[row].ShouldBe(changes.GetValueOrDefault(row, rows[row]), $"{id}:row{row}");
        return clone;
    }
    private static void RequireApproval(string screen)
    {
        foreach (var phrase in new[] { Approval, BrowserCode, Waiting }) screen.ShouldContain(phrase, customMessage: "synthetic-derived or real approval anchor");
    }
    private static void RequireMeasuredApproval(string screen)
    {
        var rows = screen.Split('\n');
        rows[13].ShouldBe(new string(' ', 38) + Approval);
        rows[17].ShouldBe(new string(' ', 41) + BrowserCode);
        rows[25].ShouldBe(new string(' ', 49) + Waiting);
    }
    private static void RequireTrust(string screen)
    {
        foreach (var phrase in new[] { Question, Yes, No }) screen.ShouldContain(phrase, customMessage: "synthetic-derived trust anchor");
    }
    private static void AssertObservation(string screen, GrokStartupReason reason, string id)
    {
        var observation = GrokStartupScreen.Classify(screen);
        observation.Reason.ShouldBe(reason, id);
        observation.IsReady.ShouldBeFalse(id);
    }
    private static string ProbeSource(string id) => id switch
    {
        "P-01" => TrustSource, "P-02" => RealSource + "#C1-sign-in",
        "P-06" or "P-07" or "P-09" or "P-10" or "P-11" or "P-12" => BlankSource,
        _ => LinuxSource
    };
    private static string[] DerivedLayout(string id, string[] screen) => id switch
    {
        "P-01" or "P-02" => Patch(Blank(), new() { [7] = "  ❯" }),
        "P-03" => Patch(GrokLinuxStartupFixture.Screen("1.0.41").Split('\n'), new() { [7] = "  ❯", [22] = "  Working..." }),
        "P-04" => Patch(GrokLinuxStartupFixture.Screen("1.0.41").Split('\n'), new() { [22] = "  Working..." }),
        "P-05" => GrokLinuxStartupFixture.Screen("1.0.41").Split('\n'),
        _ => screen.ToArray()
    };
    private static JsonElement[] RealUpdateFrames(JsonElement root)
    {
        var update = root.GetProperty("realUpdate");
        var ids = update.GetProperty("captureIds").EnumerateArray().Select(x => x.GetString()).ToArray();
        var status = update.GetProperty("status").GetString();
        if (status == "not-observed" && ids.Length == 0) return [];
        if (status != "captured" || ids.Length == 0) throw new InvalidDataException("Invalid realUpdate policy");
        return ids.Select(id =>
        {
            var frame = root.GetProperty("captures").EnumerateArray().Single(x => x.GetProperty("captureId").GetString() == id);
            frame.GetProperty("label").GetString().ShouldBe("real-rendered");
            return frame;
        }).ToArray();
    }

    private sealed record Failure(GrokStartupReason Outcome, GrokStartupReason LastReason,
        GrokStartupSnapshot? Frame, TimeSpan Elapsed, int Count, bool SignInSeen);
    private sealed record WaitResult(bool Ready, List<string> Inputs, Failure? Failure, TimeSpan Elapsed);
    private static GrokReadyWaitOptions Options(PollClock clock, Action<Failure> onFailure,
        Action<GrokStartupSnapshot>? onSignIn = null) => new()
    {
        MaxWait = TimeSpan.FromMilliseconds(2000), Settle = TimeSpan.FromMilliseconds(1000),
        TrustSettle = TimeSpan.FromMilliseconds(1000), PollInterval = TimeSpan.FromMilliseconds(50),
        MinimumAgeRemaining = TimeSpan.Zero, TimeProvider = clock,
        OnSignIn = onSignIn,
        OnFailure = (outcome, reason, frame, elapsed, count, _, seen) => onFailure(new(outcome, reason, frame, elapsed, count, seen))
    };
    private static async Task<WaitResult> DriveWaitAsync(Func<double, string> screenAt, bool assertAt1050 = false)
    {
        var clock = new PollClock();
        var start = clock.GetTimestamp();
        using var cancel = new CancellationTokenSource();
        var inputs = new List<string>(); Failure? failure = null; long sequence = 0;
        var wait = GrokReadyWait.WaitAsync(_ =>
        {
            clock.OperationStarted();
            return Task.FromResult<GrokStartupSnapshot?>(new(
                screenAt(clock.GetElapsedTime(start).TotalMilliseconds), "", ++sequence, clock.GetUtcNow().UtcDateTime));
        }, Options(clock, f => failure = f), (input, _) =>
        {
            clock.OperationStarted();
            inputs.Add(input);
            return Task.CompletedTask;
        }, ct: cancel.Token);
        try
        {
            while (!wait.IsCompleted)
            {
                // Only harness liveness uses wall time; every readiness decision uses the controlled clock.
                var installed = clock.NextPollAsync(cancel.Token);
                var completed = await Task.WhenAny(wait, installed).WaitAsync(TimeSpan.FromSeconds(5));
                if (completed == wait) break;
                await installed;
                var elapsed = clock.GetElapsedTime(start);
                if (assertAt1050 && elapsed == TimeSpan.FromMilliseconds(1050)) wait.IsCompleted.ShouldBeFalse("readyAt1050");
                elapsed.ShouldBeLessThan(TimeSpan.FromMilliseconds(2000), "waiter exceeded simulated deadline");
                clock.Advance(TimeSpan.FromMilliseconds(50));
            }
            var ready = await wait;
            return new(ready, inputs, failure, clock.GetElapsedTime(start));
        }
        finally
        {
            await cancel.CancelAsync();
            try { await wait; } catch (OperationCanceledException) when (cancel.IsCancellationRequested) { }
        }
    }
    private sealed class PollClock : TimeProvider
    {
        private readonly FakeTimeProvider _timer = new();
        private readonly Channel<bool> _polls = Channel.CreateUnbounded<bool>();
        private int _operationTimers;
        public override DateTimeOffset GetUtcNow() => _timer.GetUtcNow();
        public override long GetTimestamp() => _timer.GetTimestamp();
        public override long TimestampFrequency => _timer.TimestampFrequency;
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = _timer.CreateTimer(callback, state, dueTime, period);
            // Each scripted read/write is wrapped in its own Bounded deadline timer.
            // Exclude that timer even when its remaining budget happens to be 50 ms.
            if (_operationTimers > 0) _operationTimers--;
            else if (dueTime == TimeSpan.FromMilliseconds(50)) _polls.Writer.TryWrite(true);
            return timer;
        }
        public void OperationStarted() => _operationTimers++;
        public Task<bool> NextPollAsync(CancellationToken ct) => _polls.Reader.ReadAsync(ct).AsTask();
        public void Advance(TimeSpan amount) => _timer.Advance(amount);
    }
}
