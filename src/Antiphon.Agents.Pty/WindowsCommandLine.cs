namespace Antiphon.Agents.Pty;

/// <summary>
/// CARD-0497: the CRT/<c>CommandLineToArgvW</c> serialization already used by
/// <see cref="ModernConPtyConnection.BuildCommandLine"/>, exposed for length
/// measurement. Do not invent a second escaping algorithm.
/// </summary>
public static class WindowsCommandLine
{
    public static string Serialize(string executable, IReadOnlyList<string>? arguments = null) =>
        ModernConPtyConnection.BuildCommandLine(executable, arguments as string[] ?? arguments?.ToArray(), verbatim: false);

    public static int Measure(string executable, IReadOnlyList<string>? arguments = null) =>
        Serialize(executable, arguments).Length;

    public static string ResolveExecutable(string app, string cwd, IDictionary<string, string> environment) =>
        ModernConPtyConnection.ResolveAppPath(app, cwd, environment);
}
