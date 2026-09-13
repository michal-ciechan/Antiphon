using Antiphon.Server.Application.Services;

// CARD-0418 F-3: parent-owned crash probe. The parent supplies a generated configuration file
// and kills this process at named barriers. This entry point is a host for that protocol.
Console.WriteLine("antiphon-channel-outbound-probe");
if (args is ["--help", ..] || args.Length == 0)
{
    Console.WriteLine("usage: Antiphon.ChannelOutbound.Probe --config <path>");
    return 2;
}

_ = typeof(ChannelOutboundService);
return 0;
