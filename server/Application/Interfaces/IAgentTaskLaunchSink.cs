using Antiphon.Server.Application.Dtos;

namespace Antiphon.Server.Application.Interfaces;

/// <summary>The task dispatcher's final launch boundary. Test harnesses can record the exact spec.</summary>
public interface IAgentTaskLaunchSink
{
    void Enqueue(Guid sessionId, Guid agentId, DateTime acceptedGeneration, AgentLaunchSpec spec);
}
