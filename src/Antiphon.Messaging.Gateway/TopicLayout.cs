namespace Antiphon.Messaging.Gateway;

/// <summary>
/// How inbound/outbound topics are named. Only <see cref="Shared"/> is implemented;
/// <see cref="PerProvider"/> is a documented follow-up (Tier 2 per-provider topics).
/// </summary>
public enum TopicLayout
{
    Shared,
    PerProvider,
}
