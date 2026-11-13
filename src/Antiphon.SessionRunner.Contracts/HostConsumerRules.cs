namespace Antiphon.SessionRunner.Contracts;

/// <summary>CARD-1884. Role names and the publishable shape of one top-consumer row.</summary>
public static class HostConsumerRules
{
    public const int MaxCount = 8;
    public const string Compiler = "compiler";
    public const string Build = "build";
    public const string Test = "test";
    public const string AgentCli = "agent-cli";
    public const string AntiphonRunner = "antiphon-runner";
    public const string PtyHost = "pty-host";
    public const string Docker = "docker";
    public const string Other = "other";

    public static bool IsRole(string? role) => role is Compiler or Build or Test or AgentCli
        or AntiphonRunner or PtyHost or Docker or Other;

    /// <summary>Basename only. A path, flag, or token character cannot be a published name.</summary>
    public static bool IsName(string? name)
    {
        if (name is null || name.Length is < 1 or > 64) return false;
        if (!char.IsAsciiLetterOrDigit(name[0])) return false;
        for (var i = 1; i < name.Length; i++)
        {
            var c = name[i];
            if (!char.IsAsciiLetterOrDigit(c) && c is not ('.' or '_' or '+' or '-')) return false;
        }
        return true;
    }

    public static bool IsPublishable(HostLoadTopConsumer row) =>
        row.Pid > 0 && row.StartTicks >= 0 && IsName(row.Name) && IsRole(row.Role)
        && CpuOk(row.CpuCores) && RssOk(row.RssBytes);

    private static bool CpuOk(double? cores) =>
        cores is null || (double.IsFinite(cores.Value) && cores.Value >= 0 && cores.Value <= 4096);

    private static bool RssOk(long? rss) => rss is null or >= 0;
}
