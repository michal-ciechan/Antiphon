namespace Antiphon.Server.Application.Services;

/// <summary>External file read seam for preparing a frozen channel attachment.</summary>
public class ChannelAttachmentReader
{
    public virtual byte[] ReadAllBytes(string path) => File.ReadAllBytes(path);
}
