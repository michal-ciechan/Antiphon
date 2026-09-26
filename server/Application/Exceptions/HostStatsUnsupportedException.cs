namespace Antiphon.Server.Application.Exceptions;

public sealed class HostStatsUnsupportedException : Exception
{
    public HostStatsUnsupportedException() : base("The runner does not support host stats.") { }
}
