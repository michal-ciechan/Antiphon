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
        // Test-first seam. The native-launch implementation follows the red checkpoint.
    }
}
