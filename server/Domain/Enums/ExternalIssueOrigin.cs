namespace Antiphon.Server.Domain.Enums;

/// <summary>
/// Which side created the link between a card and an external issue. Field authority for
/// bidirectional sync (CARD-0166) is static per this value — never timestamp last-write-wins.
/// </summary>
public enum ExternalIssueOrigin
{
    ExternalImport = 0,
    AntiphonExport = 1
}
