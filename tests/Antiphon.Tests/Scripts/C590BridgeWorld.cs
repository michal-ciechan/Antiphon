using System.Collections.Concurrent;
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
/// Unit 66 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md: the live server2 bridge (C590Bridge, C1609Coordinator,
/// which replaced scripts/c590-real.ps1's live cases and scripts/c590-command.ps1) in process. This world answers the bridge's ssh,
/// scp, git, gh and bw, the coordinator's ssh child and its bounded ssh and scp children from a scenario's rules, with the semantics
/// of the capture shims the PowerShell was recorded against (a rule: name, match over the arguments joined by one space, stdout and
/// stderr lines, exit, readStdin, copyFrom for an scp -r, protocol steps out, outFields, read). Each call is logged as the shims
/// logged it: its name, arguments and working directory, an uploaded local file's text (4 KiB or less) or digest, and an end record
/// with what it read. git rev-parse HEAD answers bbbb...; an scp -r of test evidence copies the scenario's remote directory. Real
/// files under a temporary checkout; nothing is started and nothing leaves the process.
/// </summary>
internal sealed class C590BridgeWorld : IC590BridgeWorld, IC1609Processes, IDisposable
{
    public const string Head = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

    private readonly List<JsonObject> _rules = [];
    private readonly object _gate = new();

    public C590BridgeWorld(string? root = null)
    {
        Root = root ?? Directory.CreateTempSubdirectory("c590-bridge-").FullName;
        Checkout = Path.Combine(Root, "repo");
        Directory.CreateDirectory(Path.Combine(Checkout, "scripts", "lib"));
        Remote = Path.Combine(Root, "remote");
        foreach (var name in C590Bridge.StagedTools.Concat(["c849-import-saved-donor.ps1"]))
            File.Copy(Path.Combine(C590Harness.RepoRoot, "scripts", name), Path.Combine(Checkout, "scripts", name));
        Variables["ANTIPHON_OPERATOR_TOKEN_FILE"] = Path.Combine(Root, "no-operator-token");
        Home = Path.Combine(Root, "home");
        Directory.CreateDirectory(Home);
    }

    public string Root { get; }

    public string Checkout { get; }

    /// <summary>The scp -r source: the remote case directory's files.</summary>
    public string Remote { get; }

    public string Home { get; }

    public Dictionary<string, string?> Variables { get; } = new(StringComparer.Ordinal);

    public List<JsonObject> Calls { get; } = [];

    public bool BwPresent { get; set; } = true;

    public string CurrentDirectory { get; set; } = "";

    public int Guids { get; private set; }

    /// <summary>The scenario's rules (first match wins), then the default git and scp -r rules.</summary>
    public void Rules(JsonArray? rules)
    {
        foreach (var rule in rules ?? []) if (rule is JsonObject r) _rules.Add(r);
        _rules.Add(new JsonObject { ["name"] = "git", ["match"] = "rev-parse HEAD", ["stdout"] = new JsonArray(Head), ["exit"] = 0 });
        _rules.Add(new JsonObject { ["name"] = "scp", ["match"] = "^-o BatchMode=yes -r mc@server2:/work/test-evidence/", ["copyFrom"] = Remote, ["exit"] = 0 });
    }

    public void Dispose()
    {
        try { ReadOnlyDirectory.Delete(Root); } catch (IOException) { } catch (UnauthorizedAccessException) { }
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

    /// <summary>
    /// Runs one live case through the verify-docker-stack command: the manifest (evidenceRoot defaults to the checkout's
    /// .antiphon/evidence), the rules, and an accepted remote result (diagnosis c780-fake-remote) unless the remote directory holds one.
    /// </summary>
    public Outcome RunCase(string hostCase, JsonObject manifest, bool refresh = false, JsonArray? rules = null)
    {
        manifest["evidenceRoot"] ??= Path.Combine(Checkout, ".antiphon", "evidence");
        var path = Path.Combine(Root, "manifest.json");
        File.WriteAllText(path, manifest.ToJsonString());
        Directory.CreateDirectory(Remote);
        if (!File.Exists(Path.Combine(Remote, "c590-result.json")))
            File.WriteAllText(Path.Combine(Remote, "c590-result.json"), "{\"accepted\":true,\"diagnosis\":\"c780-fake-remote\",\"exit\":0}");
        Rules(rules);
        using var output = new StringWriter();
        using var error = new StringWriter();
        var exit = DockerStackVerify.Run(new DockerStackVerifyOptions { Case = hostCase, Manifest = path, RefreshClaudeToken = refresh, Checkout = Checkout },
            Host(output, error), new FileSystem(), Seams());
        return new Outcome(exit, output.ToString(), error.ToString());
    }

    /// <summary>The names of the calls made, in order (end records aside).</summary>
    public string[] Names() => Calls.Where(c => c["end"] == null).Select(c => c["name"]!.GetValue<string>()).ToArray();

    /// <summary>The last argument of each ssh call (its remote command), in order.</summary>
    public string[] Remotes() => Calls.Where(c => c["end"] == null && c["name"]!.GetValue<string>() == "ssh").Select(c => c["args"]!.AsArray().Last()!.GetValue<string>()).ToArray();

    public C590BridgeSeams Seams() => new()
    {
        World = this,
        Processes = this,
        NewGuid = () => new Guid(++Guids, 0, 0, new byte[8]),
        CurrentDirectory = () => CurrentDirectory.Length > 0 ? CurrentDirectory : Checkout,
        Home = () => Home,
    };

    private JsonObject? Rule(string name, IReadOnlyList<string> args)
    {
        var joined = string.Join(" ", args);
        return _rules.FirstOrDefault(r => r["name"]?.GetValue<string>() == name
            && (r["match"] is not JsonValue match || Regex.IsMatch(joined, match.GetValue<string>())));
    }

    private static IEnumerable<string> Lines(JsonObject? rule, string stream) =>
        rule?[stream] is JsonArray lines ? lines.Select(l => l?.ToString() ?? "") : [];

    private static int Exit(JsonObject? rule) => rule?["exit"] is JsonValue exit ? exit.GetValue<int>() : 0;

    private JsonObject Log(string name, IReadOnlyList<string> args, string? cwd = null)
    {
        var record = new JsonObject { ["name"] = name, ["args"] = new JsonArray(args.Select(a => (JsonNode)JsonValue.Create(a)!).ToArray()), ["cwd"] = cwd ?? Checkout };
        if (name == "scp" && !args.Contains("-r"))
            foreach (var a in args)
                if (!a.StartsWith('-') && !a.StartsWith("mc@server2:", StringComparison.Ordinal) && File.Exists(a))
                {
                    var bytes = File.ReadAllBytes(a);
                    record["upload"] = bytes.Length <= 4096 ? Encoding.UTF8.GetString(bytes)
                        : "sha256=" + Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant() + " bytes=" + bytes.Length;
                }
        lock (_gate) Calls.Add(record);
        return record;
    }

    private void End(string name, string? stdin, IReadOnlyList<string>? responses, int exit)
    {
        var end = new JsonObject { ["name"] = name, ["end"] = true };
        if (stdin != null) end["stdin"] = stdin;
        if (responses != null) end["responses"] = new JsonArray(responses.Select(r => (JsonNode)JsonValue.Create(r)!).ToArray());
        end["exit"] = exit;
        lock (_gate) Calls.Add(end);
    }

    private static void CopyTree(string from, string to)
    {
        Directory.CreateDirectory(to);
        if (!Directory.Exists(from)) return;
        foreach (var file in Directory.GetFiles(from)) File.Copy(file, Path.Combine(to, Path.GetFileName(file)), true);
        foreach (var directory in Directory.GetDirectories(from)) CopyTree(directory, Path.Combine(to, Path.GetFileName(directory)));
    }

    private int Copy(JsonObject? rule, IReadOnlyList<string> args)
    {
        if (rule?["copyFrom"] is JsonValue from && from.GetValue<string>().Length > 0)
        {
            var leaf = args[^2].Split('/')[^1];
            CopyTree(from.GetValue<string>().Replace('\\', Path.DirectorySeparatorChar), Path.Combine(args[^1], leaf));
        }
        return Exit(rule);
    }

    // ---- IC590BridgeWorld ----

    public int Ssh(string remote, Action<string> line)
    {
        var args = C590BridgeOs.SshOptions.Concat([remote]).ToArray();
        Log("ssh", args);
        var rule = Rule("ssh", args);
        foreach (var text in Lines(rule, "stdout")) line(text);
        foreach (var text in Lines(rule, "stderr")) line(text);
        return Exit(rule);
    }

    public int SshWithInput(string remote, string input)
    {
        string[] args = ["-o", "BatchMode=yes", "-o", "ConnectTimeout=30", "mc@server2", remote];
        Log("ssh", args);
        var rule = Rule("ssh", args);
        var exit = Exit(rule);
        if (rule?["readStdin"]?.GetValue<bool>() == true) End("ssh", input + Environment.NewLine, null, exit);
        return exit;
    }

    public int Scp(IReadOnlyList<string> arguments, bool discardOutput)
    {
        Log("scp", arguments);
        return Copy(Rule("scp", arguments), arguments);
    }

    public string? GitHead(string repo)
    {
        string[] args = ["-C", repo, "rev-parse", "HEAD"];
        Log("git", args);
        var lines = Lines(Rule("git", args), "stdout").ToArray();
        return lines.Length == 0 ? null : string.Join("\n", lines);
    }

    public string GhToken()
    {
        string[] args = ["auth", "token"];
        Log("gh", args);
        return string.Concat(Lines(Rule("gh", args), "stdout").Select(l => l + Environment.NewLine));
    }

    public bool HasBw() => BwPresent;

    public (int Exit, string Output) BwPassword(string item, string session)
    {
        string[] args = ["get", "password", item, "--nointeraction"];
        Log("bw", args);
        BwSession = session;
        var rule = Rule("bw", args);
        return (Exit(rule), string.Concat(Lines(rule, "stdout").Select(l => l + Environment.NewLine)));
    }

    public string? BwSession { get; private set; }

    public IC1609Duplex StartCoordinator(string remote, string workingDirectory)
    {
        var args = C590BridgeOs.SshOptions.Concat([remote]).ToArray();
        Log("ssh", args, workingDirectory.Length > 0 ? workingDirectory : CurrentDirectory.Length > 0 ? CurrentDirectory : Checkout);
        var rule = Rule("ssh", args);
        if (rule?["start"]?.GetValue<string>() == "fail") throw new System.ComponentModel.Win32Exception(2, "The system cannot find the file specified.");
        return new ScriptedDuplex(rule, (responses, exit) => { if (responses != null) End("ssh", null, responses, exit); });
    }

    // ---- IC1609Processes: the coordinator's bounded ssh and scp (and, unless Git is set, git) ----

    public Func<C1609ProcessCall, C1609ProcessResult>? Git { get; set; }

    public C1609ProcessResult Run(C1609ProcessCall call)
    {
        if (call.TimeoutMs <= 0) throw new C1609Refusal("deadline");
        if (call.FileName == "git" && Git != null) return Git(call);
        Log(call.FileName, call.Arguments, call.WorkingDirectory.Length > 0 ? call.WorkingDirectory : CurrentDirectory.Length > 0 ? CurrentDirectory : Checkout);
        var rule = Rule(call.FileName, call.Arguments);
        if (call.FileName == "scp" && call.Arguments.Contains("-r")) return new C1609ProcessResult(Copy(rule, call.Arguments), "");
        return new C1609ProcessResult(Exit(rule), call.CaptureOutput ? string.Concat(Lines(rule, "stdout").Select(l => l + "\n")) : "");
    }

    /// <summary>The coordinator's ssh child playing a rule: its stdout and stderr lines, then its protocol steps, then its exit.</summary>
    internal sealed class ScriptedDuplex : IC1609Duplex
    {
        private readonly QueueReader _stdout = new();
        private readonly QueueReader _stderr = new();
        private readonly QueueWriter _stdin = new();
        private readonly Thread _thread;
        private volatile bool _exited;
        private int _exit;

        public ScriptedDuplex(JsonObject? rule, Action<List<string>?, int> done)
        {
            _thread = new Thread(() =>
            {
                List<string>? responses = null;
                try
                {
                    foreach (var line in Lines(rule, "stdout")) _stdout.Write(line + "\n");
                    foreach (var line in Lines(rule, "stderr")) _stderr.Write(line + "\n");
                    var last = "";
                    if (rule?["protocol"] is JsonArray steps)
                    {
                        responses = [];
                        foreach (var step in steps.OfType<JsonObject>())
                        {
                            if (step["out"] is JsonValue output) _stdout.Write(output.GetValue<string>() + "\n");
                            if (step["outFields"] is JsonValue template)
                            {
                                var fields = last.Split(' ');
                                _stdout.Write(Regex.Replace(template.GetValue<string>(), "\\{(\\d+)\\}", m => fields[int.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) - 1]) + "\n");
                            }
                            if (step.ContainsKey("read"))
                            {
                                last = _stdin.ReadLine() ?? "";
                                responses.Add(last);
                            }
                        }
                    }
                    _exit = Exit(rule);
                }
                finally
                {
                    _stdout.Complete();
                    _stderr.Complete();
                    _exited = true;
                    done(responses, _exit);
                }
            }) { IsBackground = true };
            _thread.Start();
        }

        public TextReader Stdout => _stdout;

        public TextReader Stderr => _stderr;

        public TextWriter Stdin => _stdin;

        public bool HasExited => _exited;

        public int ExitCode => _exit;

        public bool WaitForExit(int milliseconds) => _thread.Join(milliseconds);

        public void Kill()
        {
            _stdin.Close();
            _thread.Join(5000);
        }

        public void Dispose() => _stdin.Close();
    }

    /// <summary>A reader over text written to it from another thread; ReadAsync completes when text or the end is there.</summary>
    internal sealed class QueueReader : TextReader
    {
        private readonly StringBuilder _pending = new();
        private readonly object _gate = new();
        private TaskCompletionSource<bool> _signal = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private bool _complete;

        public void Write(string text)
        {
            lock (_gate) { _pending.Append(text); _signal.TrySetResult(true); }
        }

        public void Complete()
        {
            lock (_gate) { _complete = true; _signal.TrySetResult(true); }
        }

        public override async Task<int> ReadAsync(char[] buffer, int index, int count)
        {
            while (true)
            {
                Task wait;
                lock (_gate)
                {
                    if (_pending.Length > 0)
                    {
                        var n = Math.Min(count, _pending.Length);
                        _pending.CopyTo(0, buffer, index, n);
                        _pending.Remove(0, n);
                        return n;
                    }
                    if (_complete) return 0;
                    if (_signal.Task.IsCompleted) _signal = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                    wait = _signal.Task;
                }
                await wait.ConfigureAwait(false);
            }
        }
    }

    /// <summary>The child's input: each written line queued for the protocol's read steps; closed, a read is the end (null).</summary>
    internal sealed class QueueWriter : TextWriter
    {
        private readonly BlockingCollection<string> _lines = new();
        private readonly StringBuilder _partial = new();

        public override Encoding Encoding => Encoding.UTF8;

        public override void Write(char value)
        {
            lock (_partial)
            {
                if (value == '\n') { if (!_lines.IsAddingCompleted) _lines.Add(_partial.ToString()); _partial.Clear(); }
                else _partial.Append(value);
            }
        }

        public override Task WriteLineAsync(string? value)
        {
            Write((value ?? "") + "\n");
            return Task.CompletedTask;
        }

        public string? ReadLine()
        {
            try { return _lines.Take(); }
            catch (InvalidOperationException) { return null; }
        }

        public override void Close()
        {
            _lines.CompleteAdding();
            base.Close();
        }
    }
}
