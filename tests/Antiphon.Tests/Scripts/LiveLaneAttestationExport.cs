using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Antiphon.TestSupport.LiveClaude;

namespace Antiphon.Tests.Scripts;

// CARD-1545 S2a-2: test-side producer of antiphon-live-lane-attestation-v1 documents from the real
// S1 objects (settings projection over an owned fake profile, executable receipts over inert owned
// image files and a fake observer). It never reads ~/.claude, starts a process or writes machine
// policy. The future native wrapper is the real producer; this exporter stands in until S1c-native.
internal static class LiveLaneAttestationExport
{
    public const string Lane = "fixture-lane";
    public const string Sentinel = "SENTINEL-c1545-credential";

    internal sealed class Variant
    {
        public string Image { get; set; } = "inert-owned-image-A";
        public string ImageName { get; set; } = "owned.exe";
        public string Version { get; set; } = "2.0.1";
        public string InstalledTarget { get; set; } = "installed-target-A";
        public string[] Chain { get; set; } = [];
        public bool DriftAfterStart { get; set; }
        public string Mode { get; set; } = "named-A";
        public bool ManagedProven { get; set; } = true;
        public string[]? AdditionalSources { get; set; }
        public string? Overlay { get; set; }
        public Dictionary<string, string> Environment { get; } = new(StringComparer.Ordinal)
        { ["CLAUDE_CODE_DISABLE_ALTERNATE_SCREEN"] = "1", ["DISABLE_AUTOUPDATER"] = "1" };
        public string DotnetRuntime { get; set; } = "10.0.0";
        public string? RestoredPackagesDigest { get; set; } = new('c', 64);
        public Action<LiveClaudeProfileFixture>? Profile { get; set; }
    }

    // Every document the attestation harness compares; each is produced independently.
    private static readonly (string Name, Action<Variant> Configure)[] documents =
    [
        ("base", _ => { }),
        ("base-again", _ => { }),
        ("image-b", v => v.Image = "inert-owned-image-B"),
        ("version-2", v => v.Version = "2.0.2"),
        ("installed-target", v => v.InstalledTarget = "installed-target-B"),
        ("drift", v => v.DriftAfterStart = true),
        ("chain-1", v => v.Chain = ["wrapper-W"]),
        ("chain-1b", v => v.Chain = ["wrapper-X"]),
        ("chain-2", v => v.Chain = ["wrapper-W", "wrapper-W"]),
        ("leaf-cl", v => v.ImageName = "cl.exe"),
        ("user", v => v.Profile = f => f.Write(Path.Combine(f.Context.ConfigDir!, "settings.json"),
            "{\"remoteControlAtStartup\":true,\"permissions\":{\"allow\":[\"Bash(git status)\"]}}")),
        ("project", v => v.Profile = f => f.Write(Path.Combine(f.Context.Cwd, ".claude", "settings.json"), "{\"remoteControlAtStartup\":false}")),
        ("local", v => v.Profile = f => f.Write(Path.Combine(f.Context.Cwd, ".claude", "settings.local.json"), "{\"remoteControlAtStartup\":false}")),
        ("managed", v => v.Profile = f => f.Write(f.Context.ManagedPolicyPath, "{\"remoteControlAtStartup\":false}", declare: false)),
        ("global", v => v.Profile = f => File.WriteAllText(GlobalState(f),
            "{\"numStartups\":1,\"remoteControlAtStartup\":false,\"projects\":{\"sibling\":{\"model\":\"sibling-model\"}}}")),
        ("overlay-a", v => v.Overlay = "{\"remoteControlAtStartup\":false}"),
        ("overlay-b", v => v.Overlay = "{\"remoteControlAtStartup\":true}"),
        ("model-haiku", v => v.Mode = "haiku"),
        ("model-other", v => v.Mode = "named-B"),
        ("env-absent", v => v.Environment.Remove("DISABLE_AUTOUPDATER")),
        ("env-empty", v => v.Environment["DISABLE_AUTOUPDATER"] = ""),
        ("runtime-dotnet", v => v.DotnetRuntime = "10.0.1"),
        ("runtime-packages", v => v.RestoredPackagesDigest = new('d', 64)),
        ("volatile", v => v.Profile = f => File.WriteAllText(GlobalState(f),
            "{\"numStartups\":9,\"lastSessionId\":\"15450000-0000-0000-0000-000000000009\",\"projects\":{\"sibling\":{\"model\":\"sibling-model\"}}}")),
        ("credential", v => v.Profile = f => File.WriteAllText(GlobalState(f),
            "{\"numStartups\":1,\"oauthAccount\":{\"emailAddress\":\"" + Sentinel + "\"},\"primaryApiKey\":\"" + Sentinel
            + "\",\"projects\":{\"sibling\":{\"model\":\"sibling-model\"}}}")),
        ("unknown-key", v => v.Profile = f => f.Write(Path.Combine(f.Context.ConfigDir!, "settings.json"), "{\"remoteControlAtStartup\":true,\"futureBehaviour\":1}")),
        ("managed-unverified", v => v.ManagedProven = false),
        ("unreadable", v => v.Profile = f => f.Write(Path.Combine(f.Context.ConfigDir!, "settings.json"), "{not json")),
        ("additional-source", v => v.AdditionalSources = ["unresolved-server-policy"]),
        ("runtime-missing", v => v.RestoredPackagesDigest = null),
    ];

    public static IReadOnlyList<string> WriteAll(string directory)
    {
        foreach (var (name, configure) in documents)
        {
            var variant = new Variant();
            configure(variant);
            File.WriteAllText(Path.Combine(directory, name + ".json"), Produce(variant).ToJsonString());
        }
        return documents.Select(d => d.Name).ToArray();
    }

    private static string GlobalState(LiveClaudeProfileFixture fixture) => new LiveClaudeProfilePaths(fixture.Context).GlobalState;

    public static JsonObject Produce(Variant variant)
    {
        using var fixture = new LiveClaudeProfileFixture(mode: variant.Mode);
        fixture.Context = fixture.Context with
        {
            ManagedLocationProven = variant.ManagedProven,
            Environment = new Dictionary<string, string>(variant.Environment, StringComparer.Ordinal)
        };
        variant.Profile?.Invoke(fixture);
        var args = new List<string>();
        if (variant.Overlay is not null)
        {
            File.WriteAllText(fixture.Overlay, variant.Overlay);
            args.AddRange(["--settings", fixture.Overlay]);
        }
        var settings = LiveClaudeSettingsProjection.Read(fixture.Context, new LiveClaudeProfilePaths(fixture.Context), args.ToArray(),
            additionalSources: variant.AdditionalSources);

        var bin = Directory.CreateDirectory(Path.Combine(fixture.Root, "bin")).FullName;
        var image = Path.Combine(bin, variant.ImageName);
        File.WriteAllText(image, variant.Image);
        var chain = variant.Chain.Select((bytes, i) =>
        {
            var path = Path.Combine(bin, $"wrapper-{i}.cmd");
            File.WriteAllText(path, bytes);
            return new LiveClaudeImage(path, LiveClaudeExecutable.Digest(path));
        }).ToArray();
        var installed = Path.Combine(bin, "installed-target.exe");
        File.WriteAllText(installed, variant.InstalledTarget);

        var rows = new JsonArray();
        foreach (var uid in LiveClaudeCore.All)
        {
            var binding = new LiveClaudeBinding(uid, "run-A", 1, new string('a', 40), "profile-A");
            var context = new LiveClaudeLaunchContext(binding, new(image, LiveClaudeExecutable.Digest(image)), "owner-A",
                () => new(true, "owner-A"), chain);
            var request = new LiveClaudeLaunchRequest(binding, image);
            var pre = LiveClaudeExecutable.Digest(image);
            var executable = LiveClaudeExecutable.Resolve(uid, request, context, () => throw new InvalidOperationException("legacy"));
            var process = new LiveClaudeProcess(Random.Shared.Next(100, 60000), "start-" + Guid.NewGuid().ToString("N")[..8],
                "session-" + Guid.NewGuid().ToString("N")[..8]);
            var receipt = executable.Start(_ => { }, _ => process, p => new(p, context.Image, chain));
            if (variant.DriftAfterStart && uid == LiveClaudeCore.All[^1]) File.AppendAllText(image, "drift");
            else executable.Complete(receipt, p => new(p, context.Image, chain));
            var post = LiveClaudeExecutable.Digest(image);
            if (variant.DriftAfterStart && uid == LiveClaudeCore.All[^1]) File.WriteAllText(image, variant.Image);
            var observed = receipt.ExecutedImage;
            rows.Add(new JsonObject
            {
                ["uid"] = uid,
                ["attested"] = new JsonObject
                {
                    ["route"] = "owned", ["path"] = context.Image.Path, ["sha256"] = context.Image.Sha256, ["version"] = variant.Version,
                    ["chain"] = Chain(context.Chain)
                },
                ["observed"] = new JsonObject
                {
                    ["path"] = observed.Image.Path, ["sha256"] = observed.Image.Sha256, ["chain"] = Chain(observed.Chain),
                    ["pid"] = observed.Process.Pid, ["started"] = observed.Process.Started, ["session"] = observed.Process.Session
                },
                ["pre"] = new JsonObject { ["sha256"] = pre },
                ["post"] = new JsonObject { ["sha256"] = post }
            });
        }

        var sources = new JsonArray();
        foreach (var source in settings.Sources)
            sources.Add(new JsonObject { ["kind"] = source.Kind, ["state"] = source.State, ["projectionDigest"] = Hash(source.Projection) });
        var environment = new JsonObject();
        foreach (var name in new[] { "CLAUDE_CODE_DISABLE_ALTERNATE_SCREEN", "DISABLE_AUTOUPDATER", "ANTHROPIC_BASE_URL" })
            environment[name] = !variant.Environment.TryGetValue(name, out var value) ? "absent" : value.Length == 0 ? "empty" : "value:" + value;
        return new JsonObject
        {
            ["format"] = "antiphon-live-lane-attestation-v1",
            ["schemaVersion"] = 1,
            ["laneId"] = Lane,
            ["host"] = new JsonObject { ["enrollmentId"] = "host-A", ["os"] = "windows", ["arch"] = "x64" },
            ["runtime"] = new JsonObject
            {
                ["dotnetRuntime"] = variant.DotnetRuntime, ["dotnetSdk"] = "10.0.100", ["pwsh"] = "7.5.0", ["node"] = "22.0.0",
                ["backend"] = "conpty", ["restoredPackagesDigest"] = variant.RestoredPackagesDigest
            },
            ["rows"] = rows,
            ["installedTarget"] = new JsonObject { ["sha256"] = LiveClaudeExecutable.Digest(installed) },
            ["settings"] = new JsonObject { ["digest"] = settings.Digest, ["reuseEligible"] = settings.ReuseEligible, ["sources"] = sources },
            ["model"] = new JsonObject
            {
                ["mode"] = fixture.Context.ModelMode, ["requested"] = fixture.Context.RequestedModel, ["effective"] = fixture.Context.EffectiveModel
            },
            ["environment"] = environment,
            ["account"] = new JsonObject { ["enrollmentId"] = "account-A", ["authMethod"] = "oauth", ["subscriptionClass"] = "max" },
            ["external"] = new JsonObject { ["epoch"] = "epoch-A" }
        };
    }

    private static JsonArray Chain(IEnumerable<LiveClaudeImage> chain) => new(chain.Select(i => (JsonNode)new JsonObject { ["sha256"] = i.Sha256 }).ToArray());
    private static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
}
