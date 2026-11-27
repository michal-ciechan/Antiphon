using System.IO.Abstractions;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Antiphon.Nightly;
using Antiphon.Tests.Scripts.Nightly;
using Antiphon.Tests.TestHelpers;
using Microsoft.Extensions.Time.Testing;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// Unit 67 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md: the CARD-1609 retirement entry (RetireRunnerVolumes,
/// the retire-runner-volumes command that replaced scripts/retire-runner-volumes.ps1 and scripts/c1609-manual.ps1) in process, with
/// the semantics of the fakes the PowerShell was captured against (scratchpad u67 handler-retire.ps1 and driver-retire.ps1): ssh
/// answers the first rule whose arguments and input match (stdout and stderr lines, exit), scp the first rule whose arguments match
/// (writeLast is the file it wrote, exit), the runner status answers its body (or fails), git answers Head and the verifier its exit
/// and lines. A missing ssh or scp cannot start. Each call is logged in the capture's shape (ssh's input as its shape: the packet's
/// base64 is &lt;PACKET&gt;, a shorter base64 text &lt;b64:...&gt;). A real temporary checkout and evidence; nothing is started.
/// </summary>
internal sealed class RetireRunnerVolumesWorld : IRetireRunnerVolumesWorld, IDisposable
{
    public const string Head = "0123456789abcdef0123456789abcdef01234567";
    public const string Token = "U67_OPERATOR_TOKEN_SENTINEL";

    private TextWriter _output = TextWriter.Null;

    public RetireRunnerVolumesWorld()
    {
        Root = Directory.CreateTempSubdirectory("c1609-retire-").FullName;
        Checkout = Path.Combine(Root, "repo");
        Directory.CreateDirectory(Path.Combine(Checkout, "scripts"));
        foreach (var name in new[] { "c1008-retirement.sh", "c590-remote.sh" })
            File.Copy(Path.Combine(C590Harness.RepoRoot, "scripts", name), Path.Combine(Checkout, "scripts", name));
        Variables["LOCALAPPDATA"] = Path.Combine(Root, "local");
        Variables["ANTIPHON_OPERATOR_TOKEN_FILE"] = Path.Combine(Root, "operator-token");
    }

    public string Root { get; }

    public string Checkout { get; }

    public Dictionary<string, string?> Variables { get; } = new(StringComparer.Ordinal);

    public List<JsonObject> Rules { get; } = [];

    public List<JsonObject> Calls { get; } = [];

    /// <summary>The status answer: {body} or {fail:true}; null fails.</summary>
    public JsonObject? Http { get; set; }

    /// <summary>The verifier's answer: exit and stdout lines (null: exit 0).</summary>
    public JsonObject? VerifyAnswer { get; set; }

    public bool SshMissing { get; set; }

    public bool ScpMissing { get; set; }

    public bool DeadlineSsh { get; set; }

    public int Guids { get; private set; }

    public void Dispose()
    {
        try { ReadOnlyDirectory.Delete(Root); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    /// <summary>A scenario's path or text: $DIR is the root, $PAD1M a megabyte of p; paths get this platform's separator.</summary>
    public string Expand(string text, bool path = false)
    {
        text = text.Replace("$DIR", Root, StringComparison.Ordinal).Replace("$PAD1M", new string('p', 1048576), StringComparison.Ordinal);
        return path ? text.Replace('\\', Path.DirectorySeparatorChar) : text;
    }

    /// <summary>Writes the operator token file (null: none).</summary>
    public void WriteToken(string? text = Token)
    {
        var path = Path.Combine(Root, "operator-token");
        if (text == null) File.Delete(path);
        else File.WriteAllText(path, text);
    }

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

    internal sealed record Outcome(int Exit, string Output, string Error);

    /// <summary>Runs the entry with these options (Checkout is this world's).</summary>
    public Outcome Run(RetireRunnerVolumesOptions options)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        _output = output;
        var exit = RetireRunnerVolumes.Run(new RetireRunnerVolumesOptions
        {
            Close = options.Close, DryRun = options.DryRun, Apply = options.Apply, Preview = options.Preview, Force = options.Force, Manual = options.Manual,
            Template = options.Template, Resume = options.Resume, OperationId = options.OperationId, ExpectedJournalDigest = options.ExpectedJournalDigest,
            ReasonFile = options.ReasonFile, AuthorityFile = options.AuthorityFile, Sha = options.Sha, RetiredRunnerId = options.RetiredRunnerId,
            ProjectId = options.ProjectId, VolumeName = options.VolumeName, ExpectedProofDigest = options.ExpectedProofDigest,
            AttestationFile = options.AttestationFile, EvidenceDirectory = options.EvidenceDirectory, Checkout = Checkout,
        }, Host(output, error), new FileSystem(), this, () => new Guid(++Guids, 0, 0, new byte[8]));
        _output = TextWriter.Null;
        return new Outcome(exit, output.ToString(), error.ToString());
    }

    private static IEnumerable<string> Lines(JsonObject? rule, string stream) => rule?[stream] switch
    {
        JsonArray lines => lines.Select(l => l?.GetValue<string>() ?? ""),
        JsonValue line => [line.GetValue<string>()],
        _ => [],
    };

    private static string Text(JsonObject? rule, string stream) => string.Concat(Lines(rule, stream).Select(l => l + "\n"));

    private JsonObject? Rule(string name, IReadOnlyList<string> args, string stdin = "") => Rules.FirstOrDefault(r =>
        r["name"]?.GetValue<string>() == name
        && (r["match"] is not JsonValue match || Regex.IsMatch(string.Join(" ", args), match.GetValue<string>()))
        && (r["stdinMatch"] is not JsonValue input || Regex.IsMatch(stdin, input.GetValue<string>())));

    /// <summary>The capture handler's shape of ssh's input; packets collects each packet's SHA-256.</summary>
    public static string Shape(string text, List<string> packets) => Regex.Replace(text, "[A-Za-z0-9+/]{24,}={0,2}", match =>
    {
        byte[] bytes;
        try { bytes = Convert.FromBase64String(match.Value); }
        catch (FormatException) { return match.Value; }
        var decoded = Encoding.UTF8.GetString(bytes);
        if (decoded.Length > 50000)
        {
            packets.Add(Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
            return "<PACKET>";
        }
        if (Regex.IsMatch(decoded, "[^\\x09\\x0a\\x0d\\x20-\\x7e]")) return match.Value;
        return "<b64:" + Shape(decoded, packets) + ">";
    });

    public RetireRunnerVolumesChild Server2Bash(string script, int timeoutMs)
    {
        if (SshMissing) throw new System.ComponentModel.Win32Exception(2, "An error occurred trying to start process 'ssh'. The system cannot find the file specified.");
        string[] args = ["-o", "BatchMode=yes", "-o", "ConnectTimeout=15", "mc@server2", "bash", "-s"];
        var packets = new List<string>();
        Calls.Add(new JsonObject
        {
            ["name"] = "ssh", ["args"] = new JsonArray(args.Select(a => (JsonNode)JsonValue.Create(a)).ToArray()), ["stdin"] = Shape(script, packets),
            ["packetSha"] = new JsonArray(packets.Select(p => (JsonNode)JsonValue.Create(p)).ToArray()),
        });
        if (DeadlineSsh) return RetireRunnerVolumesChild.Deadline;
        var rule = Rule("ssh", args, script);
        return new RetireRunnerVolumesChild(rule?["exit"]?.GetValue<int>() ?? 0, Text(rule, "stdout"), Text(rule, "stderr"));
    }

    public bool Scp(string remote, string destination)
    {
        if (ScpMissing) throw new System.ComponentModel.Win32Exception(2, "An error occurred trying to start process 'scp'. The system cannot find the file specified.");
        string[] args = ["-o", "BatchMode=yes", "-o", "ConnectTimeout=15", "mc@server2:" + remote, destination];
        Calls.Add(new JsonObject { ["name"] = "scp", ["args"] = new JsonArray(args.Select(a => (JsonNode)JsonValue.Create(a)).ToArray()) });
        var rule = Rule("scp", args);
        if (rule?["writeLast"] is JsonValue write) File.WriteAllText(destination, write.GetValue<string>());
        return (rule?["exit"]?.GetValue<int>() ?? 0) == 0;
    }

    public string RunnerStatus(string url, string token)
    {
        Calls.Add(new JsonObject
        {
            ["name"] = "http", ["method"] = "Get", ["uri"] = url, ["headers"] = new JsonArray(OperatorApi.OperatorTokenHeader), ["token"] = token, ["timeoutSec"] = 30,
        });
        if (Http == null || Http["fail"]?.GetValue<bool>() == true) throw new HttpRequestException("Response status code does not indicate success: 503 (Service Unavailable).");
        return Http["body"]!.GetValue<string>();
    }

    public IReadOnlyList<string> GitHead(string repo)
    {
        Calls.Add(new JsonObject { ["name"] = "git", ["args"] = new JsonArray("-C", repo, "rev-parse", "HEAD") });
        return [Head];
    }

    public int Verify(string hostCase, string manifest)
    {
        Calls.Add(new JsonObject { ["name"] = "verify", ["case"] = hostCase, ["manifestPath"] = manifest, ["manifest"] = File.ReadAllText(manifest) });
        foreach (var line in Lines(VerifyAnswer, "stdout")) _output.WriteLine(line);
        return VerifyAnswer?["exit"]?.GetValue<int>() ?? 0;
    }

    /// <summary>The names of the calls made, in order.</summary>
    public string[] Names() => Calls.Select(c => c["name"]!.GetValue<string>()).ToArray();

    /// <summary>The input of each ssh call (its shape), in order.</summary>
    public string[] Inputs() => Calls.Where(c => c["name"]!.GetValue<string>() == "ssh").Select(c => c["stdin"]!.GetValue<string>()).ToArray();
}
