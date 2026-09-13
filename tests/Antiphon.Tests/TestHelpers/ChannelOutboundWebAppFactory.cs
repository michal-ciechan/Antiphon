using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Antiphon.Tests.TestHelpers;

/// <summary>
/// CARD-0418: the real application host with an outbound settings object the test can edit after
/// boot.
///
/// <para>A profile names a project id and an agent id, and both only exist once the test has seeded
/// them — which is after configuration is read. So the settings are registered as a mutable
/// singleton rather than bound from configuration, and <see cref="Configure"/> fills them in once
/// the rows exist. Everything else, including validation, the endpoints, the file store and the
/// real task service, is the production graph.</para>
///
/// <para>The periodic pump is removed: these suites drive <c>PumpOnceAsync</c> themselves so an
/// assertion is about the state the test produced, not about whichever background tick happened to
/// land first.</para>
/// </summary>
public sealed class ChannelOutboundWebAppFactory : AntiphonWebAppFactory
{
    /// <summary>Edited by the test after it has seeded the agents a profile refers to.</summary>
    public ChannelOutboundSettings Settings { get; } = new();

    /// <summary>The server-owned outbound store root for this host.</summary>
    public string StoreRoot { get; } = Path.Combine(
        Path.GetTempPath(), "antiphon-outbound-host-" + Guid.NewGuid().ToString("N"), "store");

    public void Configure(Action<ChannelOutboundSettings> change) => change(Settings);

    protected override void ApplyTestOverrides(IServiceCollection services)
    {
        Directory.CreateDirectory(StoreRoot);
        Settings.StoreRoot = StoreRoot;

        services.RemoveAll<IOptions<ChannelOutboundSettings>>();
        services.RemoveAll<IOptionsSnapshot<ChannelOutboundSettings>>();
        services.RemoveAll<IOptionsMonitor<ChannelOutboundSettings>>();
        services.AddSingleton<IOptions<ChannelOutboundSettings>>(Options.Create(Settings));

        foreach (var hosted in services
                     .Where(d => d.ImplementationType == typeof(ChannelOutboundHostedService))
                     .ToList())
        {
            services.Remove(hosted);
        }
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing)
            return;
        try { Directory.Delete(Path.GetDirectoryName(StoreRoot)!, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
