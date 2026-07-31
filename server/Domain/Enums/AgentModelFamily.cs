namespace Antiphon.Server.Domain.Enums;

/// <summary>
/// Which Claude model FAMILY the agent's sessions launch with. Passed to the CLI as the family
/// alias (<c>--model opus</c> etc.) — never a full versioned model id, so launches always pick up
/// the current model of that family (verified against the CLI 2026-07-31: opus → claude-opus-5,
/// sonnet → claude-sonnet-5, fable → claude-fable-5, haiku → claude-haiku-4-5).
/// Opus is the default (value 0 — existing rows backfill to it).
/// </summary>
public enum AgentModelFamily
{
    Opus = 0,
    Sonnet = 1,
    Fable = 2,
    Haiku = 3,
}
