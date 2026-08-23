# EchoGateway sample

The smallest complete third-party Antiphon messaging gateway: the console is the
channel. Type a line, it is produced to `channels.inbound` as
`Channel = "echo"`; replies consumed from `channels.outbound` print to stdout.

This is the copy-from starting point for a real adapter (Discord, WhatsApp, SMS,
Matrix, an internal chat tool). The full walkthrough is
[`docs/messaging/build-your-own-gateway.md`](../../docs/messaging/build-your-own-gateway.md).
The wire contract is [`docs/messaging/contract/v1/CONTRACT.md`](../../docs/messaging/contract/v1/CONTRACT.md).

## Run (in this repo)

```
dotnet run --project samples/EchoGateway -- --self-test
dotnet run --project samples/EchoGateway
```

`--self-test` is a Kafka-free round-trip through `IChannelAdapter` +
`InMemoryGatewayBus`. The default host needs a broker at `localhost:19092`
(the local Redpanda from `docker-compose.dev.yml`).

In-repo the project `ProjectReference`s `Antiphon.Messaging` and
`Antiphon.Messaging.Gateway`. A third-party copy uses a `PackageReference`
from nuget.org instead — see the getting-started doc. GitHub Packages is
the first-party / internal mirror.
