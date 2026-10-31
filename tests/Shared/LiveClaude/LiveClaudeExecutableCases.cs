namespace Antiphon.TestSupport.LiveClaude;

// Owned inert files and capture callbacks: none of these fixtures launches a process.
internal sealed class LiveClaudeExecutableCases : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("c1545-executable-").FullName;
    private readonly string uid;
    private readonly Func<string, LiveClaudeLaunchRequest?, LiveClaudeLaunchContext?, Func<string>, LiveClaudeExecutable> prepare;
    private LiveClaudeLock held = new(true, "owner-A");
    private LiveClaudeObservation? observation;
    private int probes, starts;
    private readonly List<string> captured = [];
    public LiveClaudeLaunchRequest Request { get; }
    public LiveClaudeLaunchContext Context { get; }
    private readonly LiveClaudeProcess process = new(101, "start-A", "session-A");
    private readonly string decoy;

    public LiveClaudeExecutableCases(string uid,
        Func<string, LiveClaudeLaunchRequest?, LiveClaudeLaunchContext?, Func<string>, LiveClaudeExecutable> prepare)
    {
        this.uid = uid;
        this.prepare = prepare;
        var path = System.IO.Path.Combine(root, "owned.exe");
        File.WriteAllText(path, "inert-owned-image");
        foreach (var name in new[] { "cl.exe", "cl.bat", "claude.exe" })
            File.WriteAllText(System.IO.Path.Combine(root, name), "inert-decoy-image");
        decoy = System.IO.Path.Combine(root, "cl.bat");
        var binding = new LiveClaudeBinding(uid, "run-A", 1, new string('a', 40), "profile-A");
        Request = new(binding, path);
        Context = new(binding, new(path, LiveClaudeExecutable.Digest(path)), "owner-A", () => held, []);
        observation = new(process, Context.Image, []);
    }

    private LiveClaudeExecutable Select(LiveClaudeLaunchRequest? request, LiveClaudeLaunchContext? context)
        => prepare(uid, request, context, () => decoy);
    private LiveClaudeExecutableReceipt Start(LiveClaudeExecutable executable)
        => executable.Start(path => { probes++; captured.Add(path); }, path =>
        { starts++; captured.Add(path); return process; }, _ => observation);
    private static void Check(bool condition, string v, string variant, string predicate)
    {
        if (!condition) throw new InvalidOperationException($"C1545 {v}/{variant} {predicate}");
    }

    public void Selection(string v)
    {
        var executable = Select(Request, Context);
        var receipt = Start(executable);
        executable.Complete(receipt, _ => observation);
        Check(captured.Count == 2 && captured.All(p => p == Context.Image.Path), v, "owned", "probe/start image == attested image");
        Check(receipt.ExecutedImage == observation, v, "owned", "receipt records actual observation");
        // Explicit non-core regression uses the real ClSession seam in CP-17.
        var legacy = prepare("non-core-manual", null, null, () => decoy);
        Check(legacy.Path == decoy, v, "legacy", "non-core resolution preserved");
    }

    public void Admission(string v)
    {
        Start(Select(Request, Context));
        Check(probes == 1 && starts == 1, v, "healthy", "healthy companion reaches captures once");
        var b = Request.Binding;
        var cases = new (string Name, LiveClaudeLaunchRequest? Request, LiveClaudeLaunchContext? Context)[]
        {
            ("missing-context", null, null),
            ("missing-exe", Request with { Executable = null }, Context),
            ("context-UID", Request with { Binding = b with { Uid = "foreign" } }, Context),
            ("context-run", Request with { Binding = b with { Run = "run-B" } }, Context),
            ("context-attempt", Request with { Binding = b with { Attempt = 2 } }, Context),
            ("context-source-SHA", Request with { Binding = b with { Source = new string('b', 40) } }, Context),
            ("context-profile-identity", Request with { Binding = b with { Profile = "profile-B" } }, Context),
            ("attested-UID", Request, Context with { Binding = b with { Uid = "foreign" } }),
            ("wrong-exe", Request with { Executable = decoy }, Context),
            ("missing-image", Request, Context with { Image = Context.Image with { Path = System.IO.Path.Combine(root, "absent.exe") } }),
            ("wrong-image-hash", Request, Context with { Image = Context.Image with { Sha256 = new string('0',64) } })
        };
        foreach (var c in cases) Refuse(c.Name, c.Request, c.Context, v);
        held = new(false, "owner-A"); Refuse("lock-released", Request, Context, v);
        held = new(true, "owner-B"); Refuse("lock-replaced", Request, Context, v);
    }

    private void Refuse(string name, LiveClaudeLaunchRequest? request, LiveClaudeLaunchContext? context, string v)
    {
        probes = starts = 0;
        LiveClaudeRefusal? refusal = null;
        try { Start(Select(request, context)); } catch (LiveClaudeRefusal e) { refusal = e; }
        Check(refusal is { ExitCode: 20, Credit: false } && probes == 0 && starts == 0,
            v, name, "typed refusal before probe/spawn");
    }

    public void Images(string v)
    {
        var executable = Select(Request, Context);
        var receipt = Start(executable);
        executable.Complete(receipt, _ => observation);
        var good = observation!;
        var variants = new (string Name, LiveClaudeObservation? Observation)[]
        {
            ("image-path", good with { Image = good.Image with { Path = decoy } }),
            ("image-hash", good with { Image = good.Image with { Sha256 = new string('0',64) } }),
            ("image-PID", good with { Process = process with { Pid = 102 } }),
            ("image-start-identity", good with { Process = process with { Started = "start-B" } }),
            ("image-session", good with { Process = process with { Session = "session-B" } }),
            ("post-launch-missing", null),
            ("unbound-chain", good with { Chain = [Context.Image] })
        };
        foreach (var c in variants)
        {
            observation = c.Observation;
            ExpectMismatch(() => Start(executable), v, c.Name);
            ExpectMismatch(() => executable.Complete(receipt, _ => observation), v, c.Name + "-teardown");
        }
        observation = good;
        File.AppendAllText(Context.Image.Path, "drift");
        ExpectMismatch(() => executable.Complete(receipt, _ => observation), v, "post-drift");
    }

    private static void ExpectMismatch(Action action, string v, string variant)
    {
        LiveClaudeRefusal? refusal = null;
        try { action(); } catch (LiveClaudeRefusal e) { refusal = e; }
        Check(refusal is { Reason: "executable-mismatch", ExitCode: 20, Credit: false }, v, variant,
            "20 executable-mismatch; credit=false");
    }
    public void Dispose() => Directory.Delete(root, true);
}
