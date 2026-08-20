namespace Antiphon.Server.Application.Interfaces;

/// <summary>
/// Encrypts and decrypts stored API key values (CARD-0106 S1).
///
/// <para>Keyed on the ROW ID, never the key's name: renaming a key must not orphan its ciphertext,
/// and the id is the only identifier a rename cannot move.</para>
/// </summary>
public interface IApiKeyProtector
{
    string Protect(Guid keyId, string plaintext);
    string Unprotect(Guid keyId, string protectedValue);
}
