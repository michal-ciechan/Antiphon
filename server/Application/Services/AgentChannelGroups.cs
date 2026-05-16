namespace Antiphon.Server.Application.Services;

public static class AgentChannelGroups
{
    public static string Card(Guid cardId) => $"card:{cardId:N}";
}
