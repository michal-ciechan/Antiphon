namespace Antiphon.Server.Application.Interfaces;

public interface IAgentTuiSecretProtector
{
    string Protect(Guid profileId, string environmentName, string plaintext);
    string Unprotect(Guid profileId, string environmentName, string protectedValue);
}
