using Antiphon.Server.Domain.Enums;

namespace Antiphon.E2E.Fixtures;

public sealed partial class LandDeliveryFixture
{
    internal Task<TimeoutException> ReceiptTimeoutAsync(TimeoutException original, LandNotificationKind kind,
        Guid? requestId, Guid? notificationId, Func<CancellationToken, Task<string>>? producerState = null)
        => Task.FromResult(original);
}
