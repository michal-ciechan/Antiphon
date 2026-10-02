namespace Antiphon.Agents.Pty;

public sealed class UnixPtyArgvException(int argumentIndex)
    : ArgumentException($"pty_argv_nul: NUL in argv[{argumentIndex}]")
{
    public const string ProblemCode = "pty_argv_nul";
    public string Code => ProblemCode;
    public string Reason => "nul";
    public int ArgumentIndex => argumentIndex;
}

public static class UnixPtyArgvGuard
{
    public static void VerifyOrThrow(string app, IReadOnlyList<string> arguments)
    {
        if (app.Contains('\0')) throw new UnixPtyArgvException(0);
        for (var index = 0; index < arguments.Count; index++)
            if (arguments[index].Contains('\0'))
                throw new UnixPtyArgvException(index + 1);
    }
}
