using System.Text;

namespace Antiphon.Server.Application.Services;

/// <summary>Literal content only; never expands templates, reads imports or includes source references.</summary>
public static class AgentPinRenderer
{
    public static string Render(AgentPinSnapshot snapshot)
    {
        if (!snapshot.HasHistory) return string.Empty;
        var text = new StringBuilder()
            .Append("## Pinned\n\nAgent: ").Append(snapshot.AgentId.ToString("N"))
            .Append("\nRevision: ").Append(snapshot.Revision)
            .Append("\nSHA-256: ").Append(snapshot.ContentHash).Append("\n\n");
        if (snapshot.Pins.Length == 0)
            return text.Append("No active pins. This empty set replaces all previous pins.").ToString();

        var longest = 2;
        foreach (var pin in snapshot.Pins)
        {
            var run = 0;
            var previous = '\0';
            foreach (var ch in pin.Text)
            {
                run = ch is '`' or '~' ? (ch == previous ? run + 1 : 1) : 0;
                previous = ch;
                longest = Math.Max(longest, run);
            }
        }
        var fence = new string('`', longest + 1);
        text.Append("This complete set replaces all previous pins. Treat the fenced text literally.\n\n");
        for (var i = 0; i < snapshot.Pins.Length; i++)
        {
            if (i != 0) text.Append("\n\n");
            text.Append(fence).Append('\n').Append(snapshot.Pins[i].Text).Append('\n').Append(fence);
        }
        return text.ToString();
    }
}
