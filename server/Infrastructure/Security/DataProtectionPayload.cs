using System.Security.Cryptography;
using Microsoft.AspNetCore.WebUtilities;

namespace Antiphon.Server.Infrastructure.Security;

/// <summary>
/// Reads the key id out of an ASP.NET DataProtection payload header.
///
/// <para>Shared rather than duplicated (CARD-0106 S1): the readiness handshake both protectors
/// depend on has to name the EXACT key the payload was written under — a protector that revalidated
/// against "some key" would happily decrypt with a key ring that had silently lost the one this
/// ciphertext needs — and a hand-rolled binary header parser is not a thing to have two copies of.</para>
/// </summary>
internal static class DataProtectionPayload
{
    private const int EncodedPrefixLength = 28;
    private const int DecodedPrefixLength = 21;

    public static Guid GetKeyId(string protectedValue, string invalidPayloadMessage)
    {
        if (protectedValue.Length < EncodedPrefixLength)
            throw new CryptographicException(invalidPayloadMessage);

        try
        {
            var header = WebEncoders.Base64UrlDecode(protectedValue[..EncodedPrefixLength]);
            if (header.Length != DecodedPrefixLength
                || header[0] != 0x09
                || header[1] != 0xF0
                || header[2] != 0xC9
                || header[3] != 0xF0)
            {
                throw new CryptographicException(invalidPayloadMessage);
            }

            return new Guid(header.AsSpan(4, 16));
        }
        catch (FormatException exception)
        {
            throw new CryptographicException(invalidPayloadMessage, exception);
        }
    }
}
