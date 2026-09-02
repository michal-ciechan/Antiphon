namespace Antiphon.Server.Domain.Enums;

/// <summary>What a <see cref="Entities.Schedule"/> does when it fires (CARD-0057).</summary>
public enum ScheduleKind
{
    /// <summary>Enqueue a prompt onto a standing agent's session.</summary>
    Prompt = 0,

    /// <summary>Move a card, optionally releasing the auto-dispatch hold or spawning.</summary>
    Card = 1,
}
