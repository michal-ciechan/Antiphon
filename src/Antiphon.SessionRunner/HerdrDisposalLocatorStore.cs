using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Antiphon.SessionRunner.Contracts;
using Microsoft.Win32.SafeHandles;

namespace Antiphon.SessionRunner;

internal sealed record HerdrDisposalFile(string Kind, Guid SessionId, string PaneId, string Hash);
internal sealed record HerdrDisposalLocators(IReadOnlyList<HerdrPaneDisposalClaim> Claims,
    IReadOnlyList<HerdrDisposalFile> Files, bool Complete);

internal sealed class HerdrDisposalLocatorStore(string root)
{
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);
    internal HerdrDisposalLocators Read(string paneId)
    {
        var claims = new List<HerdrPaneDisposalClaim>();
        var files = new List<HerdrDisposalFile>();
        var complete = true;
        foreach (var kind in new[] { "sidecar", "last-pane" })
        {
            var directory = kind == "sidecar" ? HerdrPaneSidecar.DirectoryFor(root) : HerdrLastPane.DirectoryFor(root);
            try
            {
                if (!Directory.Exists(directory)) continue;
                foreach (var path in Directory.GetFiles(directory, "*.json"))
                {
                    try
                    {
                        var bytes = File.ReadAllBytes(path);
                        HerdrPaneDisposalClaim claim;
                        string selected;
                        if (kind == "sidecar")
                        {
                            var row = JsonSerializer.Deserialize<HerdrPaneSidecar>(bytes, _json) ?? throw new JsonException();
                            selected = row.PaneId;
                            claim = new(row.SessionId, kind, row.Origin, row.LaunchPending, row.AgentKind, row.ChildPid, row.ChildStartedAtUtc);
                        }
                        else
                        {
                            var row = JsonSerializer.Deserialize<HerdrLastPane>(bytes, _json) ?? throw new JsonException();
                            selected = row.PaneId;
                            // LastChildPid and retirement timestamps are deliberately not process identity.
                            claim = new(row.SessionId, kind, row.Origin, false, row.AgentKind);
                        }
                        if (claim.SessionId == Guid.Empty || Path.GetFileNameWithoutExtension(path) != claim.SessionId.ToString("N"))
                        { complete = false; continue; }
                        if (selected != paneId) continue;
                        claims.Add(claim);
                        files.Add(new(kind, claim.SessionId, paneId, Convert.ToHexString(SHA256.HashData(bytes))));
                    }
                    catch (FileNotFoundException) { }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { complete = false; }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { complete = false; }
        }
        return new(claims, files, complete);
    }

    /// <summary>
    /// Open with DELETE access and without write/delete sharing, compare the captured generation,
    /// then mark THAT open file for deletion. A concurrent atomic publisher cannot replace it
    /// between comparison and deletion, including same-session publication on another pane.
    /// </summary>
    internal bool DeleteCaptured(HerdrDisposalFile record)
    {
        if (record.SessionId == Guid.Empty || record.Kind is not ("sidecar" or "last-pane")) throw new IOException("Invalid locator identity.");
        var path = record.Kind == "sidecar" ? HerdrPaneSidecar.PathFor(root, record.SessionId) : HerdrLastPane.PathFor(root, record.SessionId);
        using var handle = CreateFileW(path, 0x80000000 | 0x00010000, 1, IntPtr.Zero, 3, 0, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            var error = Marshal.GetLastWin32Error();
            if (error is 2 or 3) return false;
            throw new IOException("Locator cleanup is pending.", new Win32Exception(error));
        }
        using var stream = new FileStream(handle, FileAccess.Read);
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        var bytes = memory.ToArray();
        if (Convert.ToHexString(SHA256.HashData(bytes)) != record.Hash) return false;
        using var content = JsonDocument.Parse(bytes);
        if (content.RootElement.GetProperty("sessionId").GetGuid() != record.SessionId
            || content.RootElement.GetProperty("paneId").GetString() != record.PaneId) return false;
        var disposition = new FileDisposition { Delete = true };
        if (!SetFileInformationByHandle(handle, 4, ref disposition, (uint)Marshal.SizeOf<FileDisposition>()))
            throw new IOException("Locator cleanup is pending.", new Win32Exception(Marshal.GetLastWin32Error()));
        return true;
    }

    [StructLayout(LayoutKind.Sequential)] private struct FileDisposition { [MarshalAs(UnmanagedType.Bool)] public bool Delete; }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr security, uint mode, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetFileInformationByHandle(SafeFileHandle handle, int kind, ref FileDisposition info, uint size);
}
