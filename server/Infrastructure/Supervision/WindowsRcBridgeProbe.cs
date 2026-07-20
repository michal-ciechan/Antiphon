using System.Net;
using System.Runtime.InteropServices;
using System.Text.Json;
using Antiphon.Server.Application.Interfaces;

namespace Antiphon.Server.Infrastructure.Supervision;

/// <summary>
/// Windows implementation of the RC bridge probe — a server-side port of the proven
/// rc-status.ps1 probe: reads Claude's own <c>%USERPROFILE%\.claude\sessions\&lt;pid&gt;.json</c>
/// for the armed flag (bridgeSessionId) and counts the pid's established TCP connections to
/// Anthropic (160.79.0.0/16, port 443) via GetExtendedTcpTable.
/// </summary>
public sealed class WindowsRcBridgeProbe : IRcBridgeProbe
{
    private static readonly string SessionsDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "sessions");

    public RcProbeResult Probe(int pid)
    {
        var (armed, found) = ReadArmedState(pid);
        var connections = CountAnthropicConnections(pid);
        return new RcProbeResult(armed, connections, found);
    }

    private static (bool Armed, bool Found) ReadArmedState(int pid)
    {
        try
        {
            var path = Path.Combine(SessionsDir, $"{pid}.json");
            if (!File.Exists(path))
                return (false, false);

            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var armed = doc.RootElement.TryGetProperty("bridgeSessionId", out var bridge)
                && bridge.ValueKind == JsonValueKind.String
                && !string.IsNullOrEmpty(bridge.GetString());
            return (armed, true);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return (false, false);
        }
    }

    // ── GetExtendedTcpTable (IPv4; calibration showed the bridge uses IPv4) ─────────────────

    private const int AfInet = 2;
    private const int TcpTableOwnerPidAll = 5;
    private const uint MibTcpStateEstab = 5;

    private static int CountAnthropicConnections(int pid)
    {
        try
        {
            var size = 0;
            _ = GetExtendedTcpTable(IntPtr.Zero, ref size, false, AfInet, TcpTableOwnerPidAll, 0);
            var buffer = Marshal.AllocHGlobal(size);
            try
            {
                if (GetExtendedTcpTable(buffer, ref size, false, AfInet, TcpTableOwnerPidAll, 0) != 0)
                    return 0;

                var count = Marshal.ReadInt32(buffer);
                var rowPtr = buffer + 4;
                var rowSize = Marshal.SizeOf<MibTcpRowOwnerPid>();
                var matches = 0;
                for (var i = 0; i < count; i++)
                {
                    var row = Marshal.PtrToStructure<MibTcpRowOwnerPid>(rowPtr + i * rowSize);
                    if (row.OwningPid != pid || row.State != MibTcpStateEstab)
                        continue;

                    var remotePort = (ushort)((row.RemotePort[0] << 8) | row.RemotePort[1]);
                    if (remotePort != 443)
                        continue;

                    var remote = new IPAddress(row.RemoteAddr).GetAddressBytes();
                    if (remote[0] == 160 && remote[1] == 79)
                        matches++;
                }

                return matches;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        catch
        {
            // Probe must never take supervision down; 0 lets the consecutive-failure
            // threshold (not a single hiccup) decide.
            return 0;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MibTcpRowOwnerPid
    {
        public uint State;
        public uint LocalAddr;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
        public byte[] LocalPort;
        public uint RemoteAddr;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
        public byte[] RemotePort;
        public int OwningPid;
    }

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern int GetExtendedTcpTable(
        IntPtr pTcpTable,
        ref int pdwSize,
        bool bOrder,
        int ulAf,
        int tableClass,
        uint reserved);
}
