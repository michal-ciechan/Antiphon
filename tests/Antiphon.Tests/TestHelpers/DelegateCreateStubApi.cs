using System.Net;
using System.Text;
using System.Text.Json;

namespace Antiphon.Tests.TestHelpers;

internal sealed class DelegateCreateStubApi : IDisposable
{
    private readonly HttpListener _listener = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _pump;
    private readonly string _responseJson;
    private readonly int _statusCode;

    public DelegateCreateStubApi(string? responseJson = null, int statusCode = 201)
    {
        _statusCode = statusCode;
        _responseJson = responseJson ??
            """
            {"id":"11111111-1111-1111-1111-111111111111","shortId":"11111111",
             "status":"Queued","modelLevel":"High","warning":null,"agentKind":"ClaudeCode"}
            """;
        BaseUrl = EphemeralHttpListener.BindLoopback(_listener);
        _pump = Task.Run(PumpAsync);
    }

    public string BaseUrl { get; }
    public JsonDocument? LastBody { get; private set; }
    public int RequestCount { get; private set; }

    private async Task PumpAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            HttpListenerContext context;
            try { context = await _listener.GetContextAsync(); }
            catch (Exception) { return; }

            RequestCount++;
            using (var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8))
            {
                var raw = await reader.ReadToEndAsync();
                if (!string.IsNullOrWhiteSpace(raw)) LastBody = JsonDocument.Parse(raw);
            }

            var payload = Encoding.UTF8.GetBytes(_responseJson);
            context.Response.StatusCode = _statusCode;
            context.Response.ContentType = "application/json";
            await context.Response.OutputStream.WriteAsync(payload);
            context.Response.Close();
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        try { _listener.Stop(); } catch (Exception) { }
        try { _pump.Wait(TimeSpan.FromSeconds(5)); } catch (Exception) { }
        _listener.Close();
        LastBody?.Dispose();
        _cts.Dispose();
    }
}
