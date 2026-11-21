using Antiphon.Nightly;

namespace Antiphon.Tests.Scripts.Nightly;

/// <summary>One call FakeGit answered.</summary>
public sealed record FakeGitCall(string WorkingDirectory, IReadOnlyList<string> Arguments)
{
    /// <summary>The C487 trace line: "GIT &lt;arguments joined by spaces&gt;".</summary>
    public string Line => "GIT " + string.Join(" ", Arguments);
}

/// <summary>
/// W2-nightly S01: the git seam of a test (the Git seam of Write-C487Seams). Run is a NightlyGit. Each call is
/// recorded and answered from the reply table, the most recent matching reply first; with no match it answers the
/// default (exit 0 and a 40-character sha). "config --get core.longpaths" answers "true" unless a reply overrides it.
/// A gate holds every call that contains its argument until the gate is released (WaitGitSubcommand/WaitFile; like
/// the seam, a held call goes on after 30 s). Thread-safe.
/// </summary>
public sealed class FakeGit
{
    /// <summary>The default output: the seam's sha.</summary>
    public const string DefaultSha = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    private readonly object _lock = new();
    private readonly List<FakeGitCall> _calls = [];
    private readonly List<(Func<IReadOnlyList<string>, bool> Match, Func<FakeGitCall, NightlyGitResult> Answer)> _replies = [];
    private readonly List<(string Argument, FakeGitGate Gate)> _gates = [];
    private readonly NightlyGitResult _default;

    /// <summary>A fake whose unmatched calls answer exitCode and output.</summary>
    public FakeGit(string output = DefaultSha, int exitCode = 0)
    {
        _default = new NightlyGitResult(exitCode, output);
        Reply(arguments => arguments.Contains("--get") && arguments.Contains("core.longpaths"), new NightlyGitResult(0, "true"));
    }

    /// <summary>The calls so far, in order.</summary>
    public IReadOnlyList<FakeGitCall> Calls
    {
        get { lock (_lock) return _calls.ToArray(); }
    }

    /// <summary>The C487 trace of the calls.</summary>
    public IReadOnlyList<string> Lines => Calls.Select(call => call.Line).ToArray();

    /// <summary>Answers every later call whose arguments contain argument (ordinal; e.g. a subcommand) with result.</summary>
    public FakeGit Reply(string argument, NightlyGitResult result) => Reply(arguments => arguments.Contains(argument), result);

    /// <summary>Answers every later call that match accepts with result.</summary>
    public FakeGit Reply(Func<IReadOnlyList<string>, bool> match, NightlyGitResult result) => Reply(match, _ => result);

    /// <summary>Answers every later call that match accepts with answer(call).</summary>
    public FakeGit Reply(Func<IReadOnlyList<string>, bool> match, Func<FakeGitCall, NightlyGitResult> answer)
    {
        lock (_lock) _replies.Add((match, answer));
        return this;
    }

    /// <summary>A gate on calls whose arguments contain argument; they wait until it is released (at most 30 s).</summary>
    public FakeGitGate Gate(string argument)
    {
        var gate = new FakeGitGate();
        lock (_lock) _gates.Add((argument, gate));
        return gate;
    }

    /// <summary>The NightlyGit: records, waits on any gate it matches, then answers.</summary>
    public NightlyGitResult Run(string workingDirectory, IReadOnlyList<string> arguments)
    {
        var call = new FakeGitCall(workingDirectory, arguments.ToArray());
        FakeGitGate[] gates;
        Func<FakeGitCall, NightlyGitResult>? answer = null;
        lock (_lock)
        {
            _calls.Add(call);
            gates = _gates.Where(entry => call.Arguments.Contains(entry.Argument)).Select(entry => entry.Gate).ToArray();
            for (var i = _replies.Count - 1; i >= 0 && answer == null; i--)
                if (_replies[i].Match(call.Arguments)) answer = _replies[i].Answer;
        }
        foreach (var gate in gates) gate.Hold();
        return answer?.Invoke(call) ?? _default;
    }
}

/// <summary>A FakeGit gate: Entered is set when a call reaches it; the call goes on once Release is called.</summary>
public sealed class FakeGitGate
{
    private readonly ManualResetEventSlim _entered = new(false);
    private readonly ManualResetEventSlim _released = new(false);

    /// <summary>Waits until a call reaches the gate; false after timeout.</summary>
    public bool WaitEntered(TimeSpan timeout) => _entered.Wait(timeout);

    /// <summary>Lets every held call (and every later one) go on.</summary>
    public void Release() => _released.Set();

    internal void Hold()
    {
        _entered.Set();
        _released.Wait(TimeSpan.FromSeconds(30));
    }
}
