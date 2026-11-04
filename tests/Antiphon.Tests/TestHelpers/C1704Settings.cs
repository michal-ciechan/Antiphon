using Antiphon.Server.Application.Settings;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Antiphon.Tests.TestHelpers;

/// <summary>
/// CARD-1704 F-5: a reloadable <c>appsettings.json</c> bound as Program.cs binds <c>Landing</c>
/// (<c>Configure&lt;LandingSettings&gt;</c>) plus <see cref="LandGitBackendSettingsRegistration.AddLandGitBackendSettings"/>.
/// </summary>
internal sealed class C1704Settings : IDisposable
{
    private readonly string _directory;
    public string Path { get; }
    public IConfigurationRoot Configuration { get; }
    public ServiceProvider Provider { get; }
    public C1704Log Log { get; } = new();

    private C1704Settings(string landingJson)
    {
        _directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "antiphon-c1704-settings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        Path = System.IO.Path.Combine(_directory, "appsettings.json");
        File.WriteAllText(Path, Document(landingJson));
        Configuration = new ConfigurationBuilder().AddJsonFile(Path, optional: false, reloadOnChange: true).Build();
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddProvider(Log));
        services.Configure<LandingSettings>(Configuration.GetSection("Landing"));
        services.AddLandGitBackendSettings(Configuration);
        services.AddSingleton<LandGitBackend>();
        Provider = services.BuildServiceProvider();
    }

    /// <summary><paramref name="landingJson"/> is the body of the Landing object, e.g. <c>"GitBackend": "Cli"</c>.</summary>
    public static C1704Settings Build(string landingJson) => new(landingJson);

    private static string Document(string landingJson) => "{ \"Landing\": { " + landingJson + " } }";

    public LandGitBackend Backend => Provider.GetRequiredService<LandGitBackend>();

    /// <summary>Writes a new Landing body. Callers poll for its effect (the change token fires asynchronously).</summary>
    public void Rewrite(string landingJson) => File.WriteAllText(Path, Document(landingJson));

    public static async Task UntilAsync(Func<bool> condition, string what)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("c1704_settings_reload_not_observed:" + what);
            await Task.Delay(50);
        }
    }

    public void Dispose()
    {
        Provider.Dispose();
        try { Directory.Delete(_directory, true); }
        catch (IOException) { }
    }
}

/// <summary>A thread-safe capturing logger provider.</summary>
internal sealed class C1704Log : ILoggerProvider
{
    private readonly List<(string Category, LogLevel Level, string Message)> _entries = [];
    public IReadOnlyList<(string Category, LogLevel Level, string Message)> Entries { get { lock (_entries) return _entries.ToList(); } }
    public ILogger CreateLogger(string categoryName) => new Logger(this, categoryName);
    public ILogger<T> For<T>() => new Typed<T>(this);
    public void Dispose() { }

    private void Add(string category, LogLevel level, string message) { lock (_entries) _entries.Add((category, level, message)); }

    private class Logger(C1704Log owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => owner.Add(category, logLevel, formatter(state, exception));
    }

    private sealed class Typed<T>(C1704Log owner) : Logger(owner, typeof(T).FullName ?? typeof(T).Name), ILogger<T>;
}

/// <summary>An options monitor that counts <see cref="CurrentValue"/> reads (BS-6).</summary>
internal sealed class CountingBackendMonitor(string value) : IOptionsMonitor<LandGitBackendSettings>
{
    private int _reads;
    public int Reads => Volatile.Read(ref _reads);
    public string Value { get; set; } = value;
    public LandGitBackendSettings CurrentValue
    {
        get
        {
            Interlocked.Increment(ref _reads);
            return new LandGitBackendSettings { GitBackend = Value };
        }
    }
    public LandGitBackendSettings Get(string? name) => CurrentValue;
    public IDisposable? OnChange(Action<LandGitBackendSettings, string?> listener) => null;
}
