using Antiphon.Server.Application.Interfaces;

namespace Antiphon.Tests.ApiKeys;

// FakeApiKeyProtector on its own, so a project that needs the fake (the live Claude project's C680 harness) compiles this
// file without the store's tests.
public partial class ApiKeyStoreTests
{
    /// <summary>Reversible stand-in for the real protector — the crypto itself has its own tests.</summary>
    internal sealed class FakeApiKeyProtector : IApiKeyProtector
    {
        public string Protect(Guid keyId, string plaintext) =>
            $"{keyId:N}:{Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(plaintext))}";

        public string Unprotect(Guid keyId, string protectedValue)
        {
            var prefix = $"{keyId:N}:";
            if (!protectedValue.StartsWith(prefix, StringComparison.Ordinal))
            {
                throw new System.Security.Cryptography.CryptographicException(
                    "API key payload is invalid.");
            }

            return System.Text.Encoding.UTF8.GetString(
                Convert.FromBase64String(protectedValue[prefix.Length..]));
        }
    }
}
