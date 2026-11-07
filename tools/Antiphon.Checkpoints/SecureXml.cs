using System.Xml;
using System.Xml.Linq;

namespace Antiphon.Checkpoints;

/// <summary>Shared lexical restrictions for every checkpoint XML consumer.</summary>
public static class SecureXml
{
    private static XmlReaderSettings Settings() => new()
    {
        CheckCharacters = true,
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
        MaxCharactersFromEntities = 0,
        MaxCharactersInDocument = 16 * 1024 * 1024,
    };

    public static XDocument Parse(string text, bool preserveLiteralLineEndings = false)
    {
        var settings = Settings();
        using var validation = XmlReader.Create(new StringReader(text), settings);
        if (!preserveLiteralLineEndings)
            return XDocument.Load(validation);

        // XmlTextReader preserves literal CR/CRLF but admits illegal numeric
        // references. Validate first and discard normalized values, then project
        // the original text. DTDs/custom entities never reach the projection.
        while (validation.Read()) { }
        using var literal = new XmlTextReader(new StringReader(text))
        {
            DtdProcessing = settings.DtdProcessing,
            XmlResolver = null,
            EntityHandling = EntityHandling.ExpandCharEntities,
            Normalization = false,
        };
        using var projection = XmlReader.Create(literal, Settings());
        return XDocument.Load(projection);
    }
}
