namespace Antiphon.Server.Application.Settings;

public class GithubSettings
{
    public bool Enabled { get; set; }
    public string PersonalAccessToken { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = "https://api.github.com";
}
