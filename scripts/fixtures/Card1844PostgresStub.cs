// Test-only PostgreSQL wire peer. Owns one ephemeral loopback listener, never a database.
using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

public sealed class Card1844PostgresStub : IDisposable
{
    private readonly TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource stop = new CancellationTokenSource();
    private readonly Task worker;
    private TcpClient client;
    public int Port { get; }
    public bool ExpectedStartup { get; private set; }
    public bool ReadOnlyQuery { get; private set; }
    public bool SslRequested { get; private set; }
    public string Failure { get; private set; }

    public Card1844PostgresStub(string scenario)
    {
        listener.Start();
        Port = ((IPEndPoint)listener.LocalEndpoint).Port;
        worker = Task.Run(() => Serve(scenario));
    }

    private static byte[] Read(Stream stream, int count)
    {
        if (count < 0 || count > 65536) throw new IOException("invalid packet length");
        var bytes = new byte[count];
        for (int n = 0; n < count;)
        {
            int got = stream.Read(bytes, n, count - n);
            if (got == 0) throw new EndOfStreamException();
            n += got;
        }
        return bytes;
    }
    private static int Int(byte[] bytes) => IPAddress.NetworkToHostOrder(BitConverter.ToInt32(bytes, 0));
    private static void Int(Stream stream, int value) => stream.Write(BitConverter.GetBytes(IPAddress.HostToNetworkOrder(value)), 0, 4);
    private static void Short(Stream stream, short value) => stream.Write(BitConverter.GetBytes(IPAddress.HostToNetworkOrder(value)), 0, 2);
    private static void Text(Stream stream, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value + "\0"); stream.Write(bytes, 0, bytes.Length);
    }
    private static void Message(Stream stream, char type, Action<Stream> body)
    {
        using (var buffer = new MemoryStream())
        {
            body(buffer); stream.WriteByte((byte)type); Int(stream, (int)buffer.Length + 4);
            var bytes = buffer.ToArray(); stream.Write(bytes, 0, bytes.Length); stream.Flush();
        }
    }
    private static void Error(Stream stream, string code)
    {
        Message(stream, 'E', b => {
            b.WriteByte((byte)'S'); Text(b, "FATAL");
            b.WriteByte((byte)'C'); Text(b, code);
            // Deliberately toxic fixture error text: production must never print it.
            b.WriteByte((byte)'M'); Text(b, "fixture-secret-do-not-log"); b.WriteByte(0);
        });
    }
    private void Serve(string scenario)
    {
        try
        {
            client = listener.AcceptTcpClient();
            client.ReceiveTimeout = 6000; client.SendTimeout = 6000;
            using (var stream = client.GetStream())
            {
                var startup = Read(stream, Int(Read(stream, 4)) - 4);
                if (Int(startup) == 80877103) // SSLRequest; libpq owns TLS, not the bootstrap script.
                {
                    SslRequested = true; stream.WriteByte((byte)'N'); stream.Flush();
                    startup = Read(stream, Int(Read(stream, 4)) - 4);
                }
                var text = Encoding.UTF8.GetString(startup);
                ExpectedStartup = Int(startup) == 196608 && text.Contains("database\0expected-db\0") &&
                    text.Contains("user\0fixture-user\0") && text.Contains("default_transaction_read_only=on");
                if (scenario == "non-postgres") { Text(stream, "HTTP/1.1 200 OK\r\n\r\n"); return; }
                if (scenario == "timeout") { stop.Token.WaitHandle.WaitOne(10000); return; }
                if (scenario == "auth") { Error(stream, "28P01"); return; }
                if (scenario == "missing-db") { Error(stream, "3D000"); return; }
                Message(stream, 'R', b => Int(b, 0)); // AuthenticationOk
                Message(stream, 'S', b => { Text(b, "server_version"); Text(b, "16.0"); });
                Message(stream, 'S', b => { Text(b, "client_encoding"); Text(b, "UTF8"); });
                Message(stream, 'Z', b => b.WriteByte((byte)'I'));
                int type = stream.ReadByte();
                var query = Read(stream, Int(Read(stream, 4)) - 4);
                ReadOnlyQuery = type == 'Q' && Encoding.UTF8.GetString(query) == "SELECT current_database();\0";
                if (!ReadOnlyQuery) throw new IOException("unexpected query");
                Message(stream, 'T', b => {
                    Short(b, 1); Text(b, "current_database"); Int(b, 0); Short(b, 0);
                    Int(b, 25); Short(b, -1); Int(b, -1); Short(b, 0);
                });
                Message(stream, 'D', b => {
                    var bytes = Encoding.UTF8.GetBytes(scenario == "wrong-db" ? "another-db" : "expected-db");
                    Short(b, 1); Int(b, bytes.Length); b.Write(bytes, 0, bytes.Length);
                });
                Message(stream, 'C', b => Text(b, "SELECT 1"));
                Message(stream, 'Z', b => b.WriteByte((byte)'I'));
            }
        }
        catch (Exception e) { if (!stop.IsCancellationRequested) Failure = e.GetType().Name; }
    }
    public void Dispose()
    {
        stop.Cancel(); listener.Stop(); client?.Close();
        if (!worker.Wait(2000)) throw new TimeoutException("fixture worker did not stop");
        stop.Dispose();
    }
}
